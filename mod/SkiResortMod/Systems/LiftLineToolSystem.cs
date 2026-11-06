// Our own lift-line tool: click the base and top station, drag / add / remove towers in an overlay preview (the planner re-plans
// around the user's pins), then Build: one CreationDefinition + NetCourse per planned span goes through the game's own apply path.
// While editing, the same definitions stand as the game's own Temp preview (ghost towers and cables, error icons included),
// re-emitted only when the plan changes (the vanilla net tool's pattern), hidden while a tower is dragged.
// The vanilla net tool stays the fallback (LiftTowerFitSystem fits whatever it builds). The hub's lift-line tiles open THIS tool
// with their lift type (TryActivate(why, type)); the line prefabs have UIObject m_Group null, so the hub is the only way in.
// While open it raises the line prefabs' span limits and sets PlacementFlags.ShoreLine (restored on close). Right click is the
// Cancel action only; Secondary Apply stays off. LiftLineEdit holds the model, LiftLineInput the input rules. Logs: silence is
// impossible; an exception disables the tool until the next load.
// Engine notes: EngineNotes/LiftLineToolSystem.md

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.UI.Localization;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftLineToolSystem : ToolBaseSystem
    {
        public enum ToolState : byte
        {
            PickBase,
            PickTop,
            Edit,
            Emitted,    // definitions emitted; waiting for the Temps
            Applying,   // Apply set this frame; reset next frame
        }

        private const float StationPickM = 40f;        // a click within this (xz) of a station's stub tip picks the station
        private const float LooseEndPickM = 25f;       // a click within this (xz) of a loose line end picks it
        private const float StubNodeSearchM = 12f;     // the station's owned net node nearest the stub tip, within this
        private const int TempTimeoutFrames = 30;
        private const int LooseRefreshFrames = 30;
        private const float NoticeSeconds = 5f;        // a refusal stays in the tooltip this long (real time, not frames: 240 frames read 1-2.5 s at high fps)
        private const float CrossEndMarginM = 5f;      // another lift met this close to an end of the line is the section's own joint, no crossing
        private static readonly UnityEngine.Color StationColor = new UnityEngine.Color(0.55f, 0.8f, 1f, 0.9f);
        private static readonly UnityEngine.Color LooseColor = new UnityEngine.Color(1.0f, 0.75f, 0.15f, 0.9f);
        private static readonly UnityEngine.Color HoverColor = new UnityEngine.Color(1f, 1f, 1f, 0.95f);
        private static readonly UnityEngine.Color PinColor = new UnityEngine.Color(0.3f, 0.6f, 1f, 0.95f);
        private static readonly UnityEngine.Color[] TowerColors =
        {
            new UnityEngine.Color(0.25f, 0.85f, 0.35f, 0.9f),   // clear
            new UnityEngine.Color(0.95f, 0.2f, 0.2f, 0.95f),    // short (a span below the floor), an edge too steep for the engine, or the rope bends past the sheave train's cap
            new UnityEngine.Color(0.7f, 0.35f, 1.0f, 0.95f),    // rope too high (a span above the max rope height)
        };
        private static readonly UnityEngine.Color[] SegColors =
        {
            new UnityEngine.Color(0.25f, 0.85f, 0.35f, 0.9f),
            new UnityEngine.Color(1.0f, 0.75f, 0.15f, 0.9f),
            new UnityEngine.Color(0.95f, 0.2f, 0.2f, 0.95f),
            new UnityEngine.Color(0.7f, 0.35f, 1.0f, 0.95f),
        };

        public override string toolID => "SkiResort Lift Line Tool";

        internal readonly LiftToolLines ToolLines = new LiftToolLines();

        public bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;
        public ToolState State => m_State;
        internal LiftLineEdit Edit => m_Edit;
        internal int HoverTower => m_HoverTower;
        internal bool Dragging => !float.IsNaN(m_DragFrom);
        internal bool DragOff => Dragging && m_DragOff;
        internal string LastMessage { get; private set; } = string.Empty;
        internal string PendingTypeName => m_Type != null ? m_Type.Id : m_Preset != null ? m_Preset.Id : string.Empty;
        internal LiftTypeDef PresetType => m_Preset;
        /// <summary>The notice line of the tooltip (a LocaleEN SkiResort.LiftTool.* key), else null. A refusal shows for NoticeSeconds and wins
        /// while live; under it the STANDING notices show for as long as the plan has the condition: the line runs along another lift, a
        /// crossing that cannot be built (CROSS_*), a station faces away, the line crosses another lift (CROSSES_OVER / CROSSES_UNDER /
        /// CROSSES, LiftLineInput.CrossKey), SUNK / SUNK_END (a station end, or a ground end's coming station, sinks the carriers), BEND (the line turns the rope by more than
        /// LiftAlignMath.BendWarnDeg where it joins a built line at a loose end; a warning, never a refusal), then the open-ground end hint
        /// (the second end is open ground).</summary>
        internal LocalizedString? Notice =>
            m_Notice.HasValue && UnityEngine.Time.unscaledTime - m_NoticeTime < NoticeSeconds ? m_Notice
            : m_AlongStanding ? T("ALONG")
            : m_CrossRefused ? CrossNotice()
            : m_BackwardStanding ? T("BACKWARD")
            : m_CrossKey != null ? CrossNotice()
            : m_SunkStanding != null ? T(m_SunkStanding)
            : m_BendStandingDeg > 0f ? T("BEND", ("DEG", Deg(m_BendStandingDeg)))
            : m_Edit != null && m_Top.Kind == LiftLineEndKind.Ground ? T("OPEN_END")
            : (LocalizedString?)null;

        internal LiftLineStep Step
        {
            get
            {
                switch (m_State)
                {
                    case ToolState.PickBase: return LiftLineStep.PickBase;
                    case ToolState.PickTop: return LiftLineStep.PickTop;
                    case ToolState.Edit: return LiftLineStep.Edit;
                    default: return LiftLineStep.Building;
                }
            }
        }

        private ToolOutputBarrier m_Barrier;
        private TerrainSystem m_TerrainSystem;
        private OverlayRenderSystem m_Overlay;
        private LiftCensusSystem m_Census;
        private SkiPrefabCatalogSystem m_Catalog;
        private EntityQuery m_DefinitionQuery;
        private EntityQuery m_TempEdgeQuery;
        private EntityQuery m_LineEdgeQuery;
        private JobHandle m_Deps;

        /// <summary>One end of the line being drawn. Station / LooseEnd: Node is the existing net node the course snaps to
        /// (CoursePos.m_Entity); Ground: Pos is the clicked ground point and the course makes a fresh node.</summary>
        private struct LineEnd
        {
            public LiftLineEndKind Kind;
            public Entity Station, Node;
            public float3 Pos;
        }

        private struct LooseEnd
        {
            public Entity Node;
            public float3 Pos;
            public LiftTypeDef Type;
        }

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private ToolState m_State;
        private LiftLineEdit m_Edit;
        private LiftPlanDraw m_Draw;
        private LiftTypeDef m_Type, m_Preset;
        private LiftTowerPlanConstants m_C;
        private Entity m_LinePrefab;
        private LineEnd m_Base, m_Top;
        private readonly List<LooseEnd> m_LooseEnds = new List<LooseEnd>();
        private int m_LooseFrame = int.MinValue / 2;
        private LocalizedString? m_Notice;
        private float m_NoticeTime;   // UnityEngine.Time.unscaledTime of m_Notice
        private float3 m_Hit;
        private bool m_HasHit;
        private int m_HoverTower = -1, m_HoverStation = -1, m_HoverLoose = -1;
        private float m_DragFrom = float.NaN, m_DragTo = float.NaN;
        private bool m_DragOff;   // the dragged tower is off the line: release removes it
        private bool m_BuildRequested;
        private int m_EmitFrame, m_Frame, m_Seed = 1;
        private List<LiftLineNode> m_Emitted;
        private string m_SunkStanding;   // SUNK / SUNK_END: the current plan has a sunk station end, else null (WarnSunk sets it per plan, Reset clears it)
        private bool m_BackwardStanding;   // a station end faces away from the line (WarnBackward sets it per plan, Reset clears it)
        // Crossings (FIX-LIST 60 a): CollectCrossings fills them before each plan, WarnCrossing reads the plan's verdict; Reset clears.
        private bool m_SameTypeCrossing;   // the line crosses a lift of its own type (the game joins the two there)
        private string m_CrossKey;         // LiftLineInput.CrossKey of the current plan, else null
        private bool m_CrossRefused;       // a crossing with another type cannot be built: Build is refused
        private float m_CrossNeedM;        // the separation that crossing needs (CROSS_CLEARANCE's {M})
        private readonly List<LiftPlanCrossing> m_Crossings = new List<LiftPlanCrossing>();
        // Road keep-outs (FIX-LIST 62): CollectKeepouts fills them before each plan; WarnKeepouts logs a tower the planner still put there.
        private readonly List<float2> m_Keepouts = new List<float2>();
        private readonly HashSet<string> m_CompositionChecked = new HashSet<string>();
        private bool m_AlongStanding;   // the current line runs along another lift: Build is refused (WarnAlong sets it per plan, Reset clears it)
        private float m_BendStandingDeg;   // the plan bend over the warning angle at a loose-end joint, else 0 (WarnBend per plan, Reset clears it)
        // Ghost preview: the definitions of m_GhostEdit at m_GhostRevision stand (no Updated after their frame, so they generate
        // nothing more; GenerateNodesSystem.cs:2030-2041) and their Temps render as the translucent preview (BatchInstanceSystem.cs:
        // 446-459, 589-609; tower sub-objects copy the Temp flags, SubObjectSystem.cs:2832-2841).
        private bool m_Ghost;
        private LiftLineEdit m_GhostEdit;
        private int m_GhostRevision = -1;
        private int m_GhostEmits;


        protected override void OnCreate()
        {
            base.OnCreate();   // ToolBaseSystem.cs:292-316: registers the tool and its proxies
            m_Barrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_DefinitionQuery = GetDefinitionQuery();   // All CreationDefinition, None Updated (ToolBaseSystem.cs:689-692)
            m_TempEdgeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            // Player-built line edges (a station's own stub edge carries Owner): the loose-end search
            m_LineEdgeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Owner>() },
            });
            CreateSections();   // the section delete by the rope (LiftLineToolSystem.Sections.cs)
            Mod.Log.Info($"{nameof(LiftLineToolSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = m_ErrorLogged = false;
            ToolLines.Clear();
            m_CompositionChecked.Clear();
            Reset("load");
            ResetSections(true);
            // A load drops the old world's Temps with it; PreDeserialize resets activeTool (ToolSystem.cs:611-614), so an open tool
            // also gets OnStopRunning, whose clear finds no Temp (RequireForUpdate, ToolClearSystem.cs:219) and does nothing.
            m_Ghost = false;
            m_GhostEdit = null;
            m_GhostRevision = -1;
            if (mode != GameMode.Game)
            {
                Mod.Log.Info($"LiftLine [tool]: NOT ARMED (mode {mode})");
                return;
            }
            try
            {
                PatchSpans();
                SetTypeLayers();
                m_Armed = true;
                Mod.Log.Info($"LiftLine [tool]: ARMED — '{toolID}' (T125: THE lift tool, opened by the hub's line tiles): click one end " +
                             "(station stub, loose line end, or open ground once the type is known), then the other; left click on the " +
                             "line adds a tower, drag moves one (released off the line: removed), right click steps back one action " +
                             "(closes the tool when nothing is left), Build key builds, Esc closes. Objective: fewest towers (spans up " +
                             "to FG 90 / DQ 245 / GD 245 / tram 1000 m), tall members where they save a tower");
            }
            catch (Exception e)
            {
                Fail(e, "load");
            }
        }

        private readonly Dictionary<Entity, float2> m_VanillaSpans = new Dictionary<Entity, float2>();
        private bool m_SpansRaised;
        private bool m_StationPlacing;

        /// <summary>A station on the object tool: placing or relocating it makes a Temp copy of the line's end span, which vanilla's
        /// 1.1 x max check refuses ("Distance too long") on any tool-built span over it, so the raise holds for that tool too.</summary>
        internal void SetStationPlacing(bool placing)
        {
            if (m_StationPlacing == placing)
                return;
            m_StationPlacing = placing;
            if (!IsActive)
            {
                SetCarriersPassable(placing);
                SetSpansRaised(placing);
            }
        }

        private readonly HashSet<Entity> m_CarriersMarked = new HashSet<Entity>();

        /// <summary>A station on a line's open end: the chairs SubObjectSystem hung on that span overlap its footprint, and vanilla refuses it
        /// ("Overlapping items"), though chairs are ours to move. The lift-line tool too: a section drawn to an existing tower (a redo of a
        /// bulldozed middle section, an extension from a loose end) gives the touched edges, their end nodes and those towers Temp copies,
        /// which ValidationHelpers checks against the permanent chairs; moving chairs ride through every tower and a broken line parks
        /// them where they stand, so a chair inside a tower's bounds is a non-overridable object of another top-level owner (its edge)
        /// and OverlapExisting refuses the build (field/2026-11-02/lifticon, DECISIONS 582). GeometryFlags.Marker gives
        /// a carrier an empty collision mask in game (ObjectUtils.GetCollisionMask, ValidationHelpers.CheckOverlap and ValidateObject both
        /// read it). Only while one of those tools is open, since the flag also lets GroundHeightSystem and the raycast treat the object
        /// as a marker. The grips of a split carrier (the carrier grip round: SkiLiftChair4Grip, SkiTramCarriage, ...) are separate
        /// objects on the same rope and not carriers (CarrierGripTops leaves them out), so they are marked too: without them a section
        /// delete or redo next to a riding chair was refused, OverlapExisting on the grip (bridge, 2026-11-03, F1).</summary>
        private void SetCarriersPassable(bool passable)
        {
            if (passable)
            {
                foreach (Entity carrier in m_Catalog.CarrierGripTops.Keys)
                    MarkPassable(carrier);
                foreach (Entity grip in m_Catalog.GripTypes.Keys)
                    MarkPassable(grip);
            }
            else
            {
                foreach (Entity carrier in m_CarriersMarked)
                {
                    if (!EntityManager.TryGetComponent<ObjectGeometryData>(carrier, out var geo))
                        continue;
                    geo.m_Flags &= ~Game.Objects.GeometryFlags.Marker;
                    EntityManager.SetComponentData(carrier, geo);
                }
                m_CarriersMarked.Clear();
            }
        }

        private void MarkPassable(Entity prefab)
        {
            if (!EntityManager.TryGetComponent<ObjectGeometryData>(prefab, out var geo) || (geo.m_Flags & Game.Objects.GeometryFlags.Marker) != 0)
                return;
            geo.m_Flags |= Game.Objects.GeometryFlags.Marker;
            EntityManager.SetComponentData(prefab, geo);
            m_CarriersMarked.Add(prefab);
        }

        /// <summary>First unused Game.Net.Layer bit for the per-type lift layers (the enum ends at NetFence 0x40000).</summary>
        private const int TypeLayerFirstBit = 24;

        /// <summary>Lifts of different types cross without joining (FIX-LIST 60 a). CourseSplitSystem.MergePositions joins a course to a net
        /// it crosses only when NetUtils.CanConnect (CourseSplitSystem.cs:2310-2312, NetUtils.cs:499-506: one side's RequiredLayers inside
        /// the other's ConnectLayers). Every lift line is high voltage (PowerlineHigh in both masks), so every crossing joined. Each type
        /// gets its own bit in both masks, PowerlineHigh kept: two types no longer connect, the same type, the station stub marker and city
        /// power lines still do (their RequiredLayers is PowerlineHigh alone). Permanent while the game runs: the vanilla tool and the
        /// fit's edits must not join either. Readers of the masks that test only PowerlineHigh (connection warnings, the voltage UI,
        /// raycasts) are unaffected (EngineNotes/LiftLineToolSystem.md, Type layers).</summary>
        private void SetTypeLayers()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
            {
                LiftTypeDef def = SkiCatalog.Lifts[i];
                Entity line = m_Catalog.LineEntity(def);
                if (line == Entity.Null || !EntityManager.TryGetComponent<NetData>(line, out var net))
                {
                    sb.Append(def.Id).Append(" n/a; ");
                    continue;
                }
                var bit = (Layer)(1u << (TypeLayerFirstBit + i));
                Layer req = net.m_RequiredLayers | bit, con = net.m_ConnectLayers | bit;
                if (req != net.m_RequiredLayers || con != net.m_ConnectLayers)
                {
                    net.m_RequiredLayers = req;
                    net.m_ConnectLayers = con;
                    EntityManager.SetComponentData(line, net);
                }
                sb.Append(def.Id).Append(" required 0x").Append(((uint)req).ToString("X")).Append(" connect 0x")
                  .Append(((uint)con).ToString("X")).Append("; ");
            }
            Mod.Log.Info($"LiftLine [layers]: one layer bit per lift type, so different types cross without joining: {sb}");
        }

        /// <summary>Load: remember each lift line prefab's vanilla max edge length (see the engine notes, Span length raise).</summary>
        private void PatchSpans()
        {
            // Prefab entities outlive a city: a load while the spans were raised restores them before reading the vanilla values.
            m_StationPlacing = false;
            SetCarriersPassable(false);
            SetSpansRaised(false);
            m_VanillaSpans.Clear();
            var sb = new StringBuilder();
            for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
            {
                LiftTypeDef def = SkiCatalog.Lifts[i];
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(def);
                Entity line = m_Catalog.LineEntity(def);
                if (c == null || line == Entity.Null || !EntityManager.TryGetComponent<NetGeometryData>(line, out var geo))
                {
                    sb.Append(def.Id).Append(" n/a; ");
                    continue;
                }
                m_VanillaSpans[line] = new float2(geo.m_EdgeLengthRange.max, geo.m_ElevatedLength);
                sb.Append(def.Id).Append(' ').Append(geo.m_EdgeLengthRange.max.ToString("F0")).Append(" m (tool ")
                  .Append(math.max(geo.m_EdgeLengthRange.max, c.MaxSpanM).ToString("F0")).Append("), max slope ")
                  .Append(geo.m_MaxSlopeSteepness.ToString("F2"))
                  .Append(math.abs(geo.m_MaxSlopeSteepness - c.MaxEdgeSlope) < 1e-3f ? " = planner" : $" vs planner {c.MaxEdgeSlope:F2} MISMATCH")
                  .Append("; ");
            }
            Mod.Log.Info($"LiftLine [spans]: line nets' max edge length (NetGeometryData.m_EdgeLengthRange.max = m_MaxPylonDistance): {sb}" +
                         "raised to the planner's max span only while the lift-line tool is open (the vanilla net tool keeps its split)");
        }

        /// <summary>Tool open: each line prefab's max edge length = max(vanilla, planner MaxSpanM), so CourseSplitSystem does not cut a
        /// planned span and ValidationHelpers does not flag it. Tool closed: the vanilla values back. Permanent edges are not validated
        /// (ValidationHelpers.ValidateEdge runs on Temp entities only), so a built tool line stays valid; a later vanilla edit that makes a
        /// Temp copy of a tool span longer than the vanilla max would see it flagged (risk).</summary>
        private void SetSpansRaised(bool raised)
        {
            if (m_SpansRaised == raised)
                return;
            m_SpansRaised = raised;
            var sb = new StringBuilder();
            foreach (KeyValuePair<Entity, float2> kv in m_VanillaSpans)
            {
                if (!EntityManager.TryGetComponent<NetGeometryData>(kv.Key, out var geo) ||
                    !m_Catalog.LiftLineTypes.TryGetValue(kv.Key, out LiftTypeDef def))
                    continue;
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(def);
                float len = raised ? math.max(kv.Value.x, c.MaxSpanM) : kv.Value.x;
                float elevated = raised ? math.max(kv.Value.y, c.MaxSpanM) : kv.Value.y;
                if (math.abs(geo.m_EdgeLengthRange.max - len) < 1e-3f && math.abs(geo.m_ElevatedLength - elevated) < 1e-3f)
                    continue;
                geo.m_EdgeLengthRange.max = len;
                geo.m_ElevatedLength = elevated;
                EntityManager.SetComponentData(kv.Key, geo);
                sb.Append(def.Id).Append(' ').Append(len.ToString("F0")).Append(" m; ");
            }
            Mod.Log.Info($"LiftLine [spans]: {(raised ? "raised for the tool" : "vanilla values back")}: {(sb.Length > 0 ? sb.ToString() : "no change")}");
            SetSplitGate(raised);
        }

        private readonly HashSet<Entity> m_GateAdded = new HashSet<Entity>();

        /// <summary>While the tool is open the four line prefabs carry PlacementFlags.ShoreLine, the one prefab-side gate of
        /// CourseSplitSystem.CheckHeightRange's elevation segmentation (CourseSplitSystem.cs:2884: sampleTerrain && !RequireElevated &&
        /// !ShoreLine); without it the engine cuts a tool course where its height profile changes elevation class. Same window and
        /// restore discipline as the span raise. Readers of the bit and the full reason: EngineNotes/LiftLineToolSystem.md.</summary>
        private void SetSplitGate(bool on)
        {
            var sb = new StringBuilder();
            if (on)
            {
                foreach (Entity line in m_VanillaSpans.Keys)
                {
                    if (!EntityManager.TryGetComponent<PlaceableNetData>(line, out var pnd) ||
                        (pnd.m_PlacementFlags & Game.Net.PlacementFlags.ShoreLine) != 0)
                        continue;
                    pnd.m_PlacementFlags |= Game.Net.PlacementFlags.ShoreLine;
                    EntityManager.SetComponentData(line, pnd);
                    m_GateAdded.Add(line);
                    sb.Append(m_Catalog.LiftLineTypes.TryGetValue(line, out LiftTypeDef d) ? d.Id : Fmt(line)).Append(' ');
                }
            }
            else
            {
                foreach (Entity line in m_GateAdded)
                {
                    if (!EntityManager.TryGetComponent<PlaceableNetData>(line, out var pnd))
                        continue;
                    pnd.m_PlacementFlags &= ~Game.Net.PlacementFlags.ShoreLine;
                    EntityManager.SetComponentData(line, pnd);
                    sb.Append(m_Catalog.LiftLineTypes.TryGetValue(line, out LiftTypeDef d) ? d.Id : Fmt(line)).Append(' ');
                }
                m_GateAdded.Clear();
            }
            if (sb.Length > 0)
                Mod.Log.Info($"LiftLine [split gate]: {(on ? "ON (no elevation-transition splits of the tool's courses)" : "OFF (vanilla placement flags back)")}: {sb.ToString().TrimEnd()}");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyMode = ApplyMode.None;
            Reset("activated");
            SetSpansRaised(true);
            SetCarriersPassable(true);
            int stations = m_Census.StationCount;
            m_LooseFrame = int.MinValue / 2;
            Mod.Log.Info($"LiftLine [tool]: running — type {(m_Preset != null ? m_Preset.Id : "any (pick a station)")}; {stations} lift " +
                         $"station(s) in the last census; click the first end (station stub within {StationPickM:F0} m, loose line end " +
                         $"within {LooseEndPickM:F0} m{(m_Preset != null ? ", or open ground" : string.Empty)}), then the other");
        }

        protected override void OnStopRunning()
        {
            try
            {
                if (m_State != ToolState.PickBase)
                    Mod.Log.Info($"LiftLine [tool]: stopped with a {m_State} line — dropped (nothing built)");
                bool temps = m_Ghost || m_State == ToolState.Emitted || m_DelEdge != Entity.Null || m_DelPending != Entity.Null;
                Reset("stopped");
                ResetSections(false);
                m_Ghost = false;
                m_GhostEdit = null;
                m_GhostRevision = -1;
                if (!m_DefinitionQuery.IsEmptyIgnoreFilter)
                    EntityManager.DestroyEntity(m_DefinitionQuery);
                if (temps)
                    ClearOwnTemps("stopped");
                SetSpansRaised(m_StationPlacing);
                SetCarriersPassable(m_StationPlacing);
            }
            catch (Exception e)
            {
                Fail(e, "OnStopRunning");
            }
            base.OnStopRunning();
        }

        /// <summary>Our ghost / build Temps go when the tool stops (Esc, another tool, the hub, a load), not on the next tool's first
        /// update. Vanilla tools never clear on stop: ToolSystem.ToolUpdate disables the last tool and updates it once
        /// (ToolSystem.cs:312-318), then the NEW tool's applyMode drives ToolOutputSystem (ToolSystem.applyMode = m_LastTool.applyMode,
        /// ToolSystem.cs:147-155; ToolOutputSystem.cs:22-30), and a tool clears only when its own first update sets Clear
        /// (DefaultToolSystem.cs:973-980). So we run the game's own clear here, ToolClearSystem (the ClearTool phase,
        /// SystemOrder.cs:711): Deleted on every Temp, the hidden originals shown again (ToolClearSystem.cs:63-113). Every Temp at
        /// this point is ours: the stop update runs before the new tool's ToolUpdate (ToolSystem.cs:312-327). Its commands play back
        /// at the ToolOutputBarrier (SystemOrder.cs:695); a second clear by the next tool in the same frame is harmless.</summary>
        private void ClearOwnTemps(string why)
        {
            ToolClearSystem clear = World.GetExistingSystemManaged<ToolClearSystem>();
            if (clear == null)
            {
                Mod.Log.Warn($"LiftLine [ghost]: ToolClearSystem missing — the preview's Temps are left to the next tool ({why})");
                return;
            }
            // The stopping tool updates BEFORE the ToolUpdate phase has allowed the barrier (ToolSystem.cs:312-317), and ToolClearSystem
            // creates its command buffer from it: it threw "not allowed" on every stop with a ghost standing (field test 3, 2026-10-26).
            // AllowUsage is public (SafeCommandBufferSystem.cs:11); the barrier shuts itself again at its next update.
            World.GetExistingSystemManaged<ToolOutputBarrier>()?.AllowUsage();
            clear.Update();
            Mod.Log.Info($"LiftLine [ghost]: preview cleared on {why} (ToolClearSystem run by the tool itself)");
        }

        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;   // ToolSystem.cs:263-277

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();   // ToolBaseSystem.cs:430-443
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
        }

        protected override bool GetAllowApply()
        {
            return base.GetAllowApply() && m_State == ToolState.Emitted;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            m_Deps = inputDeps;
            try
            {
                UpdateCore();
            }
            catch (Exception e)
            {
                Fail(e, "OnUpdate");
                applyMode = ApplyMode.Clear;
                try { Deactivate(); } catch (Exception) { /* logged */ }
            }
            return m_Deps;
        }


        /// <summary>Opens the tool. <paramref name="type"/> = the lift type of the hub tile that opened it (picks are limited to it and
        /// open ground is allowed from the first click); null = any type, set by the first station or loose end. Already open with
        /// another type: the line in progress is dropped (logged) and the tool restarts for the new type.</summary>
        public bool TryActivate(string why, LiftTypeDef type = null)
        {
            if (m_Disabled || !m_Armed)
            {
                Mod.Log.Info($"LiftLine [tool]: activation refused ({why}) — {(m_Disabled ? "disabled after an exception until the next load" : "not armed (not in a game)")}");
                return false;
            }
            if (IsActive)
            {
                if (type != m_Preset)
                {
                    if (m_State != ToolState.PickBase)
                        Mod.Log.Info($"LiftLine [tool]: type changed to {(type != null ? type.Id : "any")} ({why}) — the {m_State} line is dropped");
                    m_Preset = type;
                    Reset("type changed");
                }
                return true;
            }
            m_Preset = type;
            m_ToolSystem.activeTool = this;
            Mod.Log.Info($"LiftLine [tool]: activated ({why}; type {(type != null ? type.Id : "any")})");
            return true;
        }

        /// <summary>False after an exception (until the next load) or outside a game: the hub then falls back to the vanilla net tool.</summary>
        public bool Usable => m_Armed && !m_Disabled;

        public void Deactivate()
        {
            if (m_ToolSystem != null && m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
                Mod.Log.Info("LiftLine [tool]: deactivated");
            }
        }

        public void RequestBuild()
        {
            if (!IsActive)
                return;
            if (m_State != ToolState.Edit || m_Edit?.Plan == null)
            {
                Say($"LiftLine [build]: nothing to build ({m_State}) — pick both ends first", T("NOTHING_TO_BUILD"));
                return;
            }
            if (m_AlongStanding)
            {
                Say("LiftLine [build]: refused — the line runs along another lift (ALONG); building would merge the two", T("ALONG"));
                return;
            }
            if (m_CrossRefused)
            {
                Say($"LiftLine [build]: refused — a crossing with another lift cannot be built ({m_CrossKey}; the plan's CROSS lines " +
                    "say why)", CrossNotice());
                return;
            }
            m_BuildRequested = true;
        }


        private void UpdateCore()
        {
            m_Frame++;
            bool live = InputManager.instance.controlOverWorld;
            m_HasHit = live && GetRaycastResult(out ControlPoint cp, out bool _) && TakeHit(cp);

            switch (m_State)
            {
                case ToolState.Emitted:
                    SetActions(false, false, false);
                    UpdateEmitted();
                    DrawPlan();
                    return;
                case ToolState.Applying:
                    SetActions(false, false, false);
                    applyMode = ApplyMode.None;
                    ReportFelled();
                    Reset("built");
                    return;
            }

            applyMode = ApplyMode.None;
            // Right click is the tool's Cancel action only (step back); Secondary Apply stays off
            SetActions(live, false, live);

            if (cancelAction.WasPressedThisFrame())
            {
                StepBack();
                if (m_State == ToolState.Edit)
                    DrawPlan();
                return;
            }

            if (m_State == ToolState.PickBase || m_State == ToolState.PickTop)
            {
                ClearGhost("no line");
                UpdatePick(live);
                DrawPick();
                return;
            }

            if (m_BuildRequested)
            {
                m_BuildRequested = false;
                ClearGhost(null);
                EmitCourses(false);
                DrawPlan();
                return;
            }
            UpdateEdit();
            UpdateGhost();
            DrawPlan();
        }

        /// <summary>Edit: keep the ghost on the current plan. Clear + re-emit in the same frame when the plan changed (the Temps are
        /// recycled, GenerateNodesSystem.cs:1397-1404 / GenerateEdgesSystem.cs:675-680); nothing on an unchanged frame (NetToolSystem.cs:
        /// 6885-6902 keeps its Temps the same way); hidden while a tower is dragged (the overlay shows the move).</summary>
        private void UpdateGhost()
        {
            if (m_State != ToolState.Edit || m_Edit?.Plan == null)
                return;
            if (Dragging)
            {
                ClearGhost(null);
                return;
            }
            if (m_Ghost && m_GhostEdit == m_Edit && m_GhostRevision == m_Edit.Revision)
                return;
            ClearGhost(null);
            EmitCourses(true);
        }

        /// <summary>Drop the ghost: ApplyMode.Clear deletes every Temp (ToolClearSystem.cs:63-113) and the standing definitions go.</summary>
        private void ClearGhost(string why)
        {
            if (!m_Ghost)
                return;
            applyMode = ApplyMode.Clear;
            m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            m_Ghost = false;
            m_GhostEdit = null;
            m_GhostRevision = -1;
            if (why != null)
                Mod.Log.Info($"LiftLine [ghost]: preview cleared ({why})");
        }

        private bool TakeHit(ControlPoint cp)
        {
            m_Hit = cp.m_HitPosition;
            return math.all(math.isfinite(m_Hit));
        }

        /// <summary>One right click (LiftLineInput.RightClick): cancel a drag, undo an edit, drop the second end, drop the first end, or
        /// close the tool when nothing is left.</summary>
        private void StepBack()
        {
            LiftLineBack back = LiftLineInput.RightClick(Step, Dragging, m_Edit != null ? m_Edit.UndoDepth : 0);
            switch (back)
            {
                case LiftLineBack.CancelDrag:
                    m_DragFrom = m_DragTo = float.NaN;
                    Say("LiftLine [back]: move canceled (right click)");
                    return;
                case LiftLineBack.UndoEdit:
                    m_Edit.Undo();
                    Replan($"undo (right click; {m_Edit.UndoDepth} edit(s) left to undo)");
                    return;
                case LiftLineBack.DropTop:
                    m_Edit = null;
                    m_Draw = null;
                    m_Top = default;
                    m_HoverTower = -1;
                    m_State = ToolState.PickTop;
                    Say("LiftLine [back]: second end dropped (right click) — click the other end again");
                    return;
                case LiftLineBack.DropBase:
                    m_Base = default;
                    m_Type = null;
                    m_C = null;
                    m_LinePrefab = Entity.Null;
                    m_State = ToolState.PickBase;
                    Say("LiftLine [back]: first end dropped (right click) — click an end to start");
                    return;
                case LiftLineBack.CloseTool:
                    Say("LiftLine [back]: nothing left — tool closed (right click)");
                    Deactivate();
                    return;
            }
        }

        private void SetActions(bool apply, bool secondary, bool cancel)
        {
            SetEnabled(applyAction, apply);
            SetEnabled(secondaryApplyAction, secondary);
            SetEnabled(cancelAction, cancel);
        }

        private static void SetEnabled(IProxyAction action, bool value)
        {
            if (action != null && action.shouldBeEnabled != value)
                action.shouldBeEnabled = value;
        }


        /// <summary>The lift type a pick must have: the first end's once it is placed, else the hub tile's (null = any).</summary>
        private LiftTypeDef WantType => m_State == ToolState.PickTop ? m_Type : m_Preset;

        private void UpdatePick(bool live)
        {
            RefreshLooseEnds();
            LiftTypeDef want = WantType;
            HoverEnd(want);
            if (m_State == ToolState.PickBase)
            {
                // First step: a rope under the cursor names a built section; a click removes it (LiftLineToolSystem.Sections.cs).
                UpdateSection(want, live);
                if (SectionHovered && applyAction.WasPressedThisFrame())
                    ClickSection();
                if (!UpdatePendingDelete())
                    SyncSectionPreview();
                if (SectionHovered || m_DelPending != Entity.Null)
                    return;
            }
            else
            {
                ResetSectionHover();
                ClearSectionPreview("first end picked");
            }
            if (!m_HasHit || !applyAction.WasPressedThisFrame())
                return;

            LineEnd end;
            LiftTypeDef def;
            if (m_HoverStation >= 0)
            {
                if (!StationEnd(m_HoverStation, out end, out def))
                    return;
            }
            else if (m_HoverLoose >= 0)
            {
                LooseEnd le = m_LooseEnds[m_HoverLoose];
                if (!EntityManager.Exists(le.Node))
                {
                    m_LooseFrame = int.MinValue / 2;
                    return;
                }
                end = new LineEnd { Kind = LiftLineEndKind.LooseEnd, Node = le.Node, Pos = NodePos(le.Node) };
                def = le.Type;
            }
            else if (want != null)
            {
                end = new LineEnd { Kind = LiftLineEndKind.Ground, Pos = m_Hit };
                def = want;
            }
            else
            {
                Say($"LiftLine [pick]: nothing to start from within {StationPickM:F0} m of the click (no type chosen, so open ground is " +
                    "not offered)", T("NO_START"));
                return;
            }

            if (m_State == ToolState.PickBase)
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(def);
                Entity line = def != null ? m_Catalog.LineEntity(def) : Entity.Null;
                if (c == null || line == Entity.Null)
                {
                    Say($"LiftLine [pick]: lift type {(def != null ? def.Id : "?")} has no line prefab or planner constants — not usable",
                        T("TYPE_UNUSABLE"));
                    return;
                }
                // The engine's edge slope rule is the line prefab's own (NetInitializeSystem copies m_MaxSlopeSteepness as is):
                // plan with the live value, the For() default only when the prefab has none.
                if (EntityManager.TryGetComponent<NetGeometryData>(line, out var lineGeo) && lineGeo.m_MaxSlopeSteepness > 0f)
                    c = c.With(maxEdgeSlope: lineGeo.m_MaxSlopeSteepness);
                m_Type = def;
                m_C = c;
                m_LinePrefab = line;
                m_Base = end;
                m_State = ToolState.PickTop;
                Say($"LiftLine [pick]: first end = {Describe(end)} ({def.Id}); click the other end");
                return;
            }

            if (end.Kind != LiftLineEndKind.Ground && end.Node == m_Base.Node)
            {
                Say("LiftLine [pick]: that is the first end — click the other one", T("SAME_END"));
                return;
            }
            if (end.Kind == LiftLineEndKind.Station && m_Base.Kind == LiftLineEndKind.Station && end.Station == m_Base.Station)
            {
                Say("LiftLine [pick]: same station as the first end", T("SAME_STATION"));
                return;
            }
            float3 a = EndPos(m_Base), b = EndPos(end);
            bool bothStations = m_Base.Kind == LiftLineEndKind.Station && end.Kind == LiftLineEndKind.Station;
            float min = LiftLineInput.MinLengthM(m_C, bothStations);
            if (math.distance(a.xz, b.xz) < min)
            {
                Say($"LiftLine [pick]: the ends are {math.distance(a.xz, b.xz):F0} m apart — under {min:F0} m, too short for a line",
                    T("TOO_SHORT", ("MIN", N(min))));
                return;
            }
            m_Top = end;
            if (end.Kind == LiftLineEndKind.Ground)
                Say($"LiftLine [pick]: the second end is open ground ({Describe(m_Base)} -> ground): allowed, the tooltip hints that a " +
                    "station should finish it");
            // End ropes. A station end: the height the built line's rope lane will have there, read as the fit reads it
            // (LiftLineInput.StationRope: the lowest HV sub-lane anchor of the stub node's sub-objects / its station within 30 m). A loose
            // end is a tower node at ground + E(member, head), whose rope is ground + TopM[member] - RopeBelowTopM = node y +
            // LiftTowerEdit.RopeAboveNodeM (RopeAboveBase(0) for a Line head, the twin gap lower for Hold / Combo / Break). A ground end is
            // where a station goes (line-first): it plans as a station end with that station's own, lower anchor (LiftLineInput.GroundEndRope,
            // FIX-LIST 48: tram 2 of the Eishain pass planned +0.4 with node + 26.25 and read -6.58 once its stations stood).
            float ropeA = EndRope(m_Base, a, b, out string srcA), ropeB = EndRope(end, b, a, out string srcB);
            m_Edit = new LiftLineEdit(m_C, a, b, ropeA, ropeB, m_Base.Kind != LiftLineEndKind.LooseEnd, end.Kind != LiftLineEndKind.LooseEnd);
            // A loose end is a tower of the built line: its rope deflection is judged with the built neighbor's rope point beyond it.
            string outerA = OuterPoint(m_Base, a, out m_Edit.OuterDistA, out m_Edit.OuterRopeA);
            string outerB = OuterPoint(end, b, out m_Edit.OuterDistB, out m_Edit.OuterRopeB);
            m_State = ToolState.Edit;
            Replan($"line {m_Type.Id} {Describe(m_Base)} -> {Describe(m_Top)}, {m_Edit.LengthM:F0} m, end ropes {ropeA:F2} ({srcA}) / " +
                   $"{ropeB:F2} ({srcB}){outerA}{outerB}");
        }

        /// <summary>The built neighbor's rope point beyond a loose end (the far node of its one built lift-line edge): xz distance from
        /// the end and rope y (a station stub: its rope lane anchor, as EndRope; a tower node: node y + the rope above the head it carries,
        /// the node formula). NaN for a station or ground end, or when no built edge is found. Returns a log fragment.</summary>
        private string OuterPoint(LineEnd end, float3 endPos, out float dist, out float rope)
        {
            dist = rope = float.NaN;
            if (end.Kind != LiftLineEndKind.LooseEnd || !EntityManager.Exists(end.Node) ||
                !EntityManager.TryGetBuffer(end.Node, true, out DynamicBuffer<Game.Net.ConnectedEdge> edges))
                return string.Empty;
            for (int i = 0; i < edges.Length; i++)
            {
                Entity e = edges[i].m_Edge;
                if (EntityManager.HasComponent<Owner>(e) || EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e) ||
                    !EntityManager.TryGetComponent<PrefabRef>(e, out var pr) || !m_Catalog.LiftLineTypes.ContainsKey(pr.m_Prefab) ||
                    !EntityManager.TryGetComponent<Game.Net.Edge>(e, out var ed))
                    continue;
                Entity far = ed.m_Start == end.Node ? ed.m_End : ed.m_Start;
                if (far == end.Node || !EntityManager.TryGetComponent<Game.Net.Node>(far, out var farNode))
                    continue;
                dist = math.distance(farNode.m_Position.xz, endPos.xz);
                bool station = EntityManager.HasComponent<Owner>(far);
                rope = EndRope(new LineEnd { Kind = station ? LiftLineEndKind.Station : LiftLineEndKind.LooseEnd, Node = far, Pos = farNode.m_Position },
                               farNode.m_Position, endPos, out string src);
                return $"; junction at {Describe(end)}: built neighbor {Fmt(far)} {dist:F0} m beyond, rope {rope:F2} ({src})";
            }
            return string.Empty;
        }

        /// <summary>The station or loose end under the cursor (the nearer of the two, each within its pick radius, of the wanted type).
        /// Sets m_HoverStation / m_HoverLoose (-1 = none).</summary>
        private void HoverEnd(LiftTypeDef want)
        {
            m_HoverStation = m_HoverLoose = -1;
            if (!m_HasHit)
                return;
            float bestD = float.PositiveInfinity;
            int n = m_Census.StationCount;
            for (int i = 0; i < n; i++)
            {
                LiftCensus.Station st = m_Census.StationAt(i);
                if (st.Type == LiftCensus.NoType || (want != null && (st.Type >= SkiCatalog.Lifts.Count || SkiCatalog.Lifts[st.Type] != want)))
                    continue;
                float d = math.distance(st.StubTip.xz, m_Hit.xz);
                if (d <= StationPickM && d < bestD)
                {
                    bestD = d;
                    m_HoverStation = i;
                }
            }
            for (int i = 0; i < m_LooseEnds.Count; i++)
            {
                LooseEnd le = m_LooseEnds[i];
                if (want != null && le.Type != want)
                    continue;
                if (m_State == ToolState.PickTop && le.Node == m_Base.Node)
                    continue;
                float d = math.distance(le.Pos.xz, m_Hit.xz);
                if (d <= LooseEndPickM && d < bestD)
                {
                    bestD = d;
                    m_HoverLoose = i;
                    m_HoverStation = -1;
                }
            }
        }

        /// <summary>A census station as a line end: its stub node, refused when the station already has its line (drawing a new plan over
        /// a placed line read as editing it).</summary>
        private bool StationEnd(int index, out LineEnd end, out LiftTypeDef def)
        {
            end = default;
            LiftCensus.Station st = m_Census.StationAt(index);
            Entity station = m_Census.StationEntityAt(index);
            def = st.Type < SkiCatalog.Lifts.Count ? SkiCatalog.Lifts[st.Type] : null;
            Entity line = def != null ? m_Catalog.LineEntity(def) : Entity.Null;
            if (def == null || line == Entity.Null)
            {
                Say($"LiftLine [pick]: station {Fmt(station)} has no lift type with a line prefab — not usable", T("STATION_UNUSABLE"));
                return false;
            }
            if (!TryStubNode(station, st.StubTip, out Entity node))
                return false;
            int lines = LineEdgesAt(node);
            if (lines > 0)
            {
                Say($"LiftLine [pick]: station {Fmt(station)} already has its line ({lines} line edge(s) on stub node {Fmt(node)}) — refused; " +
                    "to rework a placed line, click a section's rope to remove it, then click its two loose ends",
                    T("STATION_HAS_LINE"));
                return false;
            }
            end = new LineEnd { Kind = LiftLineEndKind.Station, Station = station, Node = node, Pos = NodePos(node) };
            return true;
        }

        /// <summary>The station's own net node nearest its stub tip (the node the vanilla net tool snaps a line to: the station-owned
        /// stub node, LiftCensusSystem.StationOfNode), from its SubNet buffer.</summary>
        private bool TryStubNode(Entity station, float3 stubTip, out Entity node, bool quiet = false)
        {
            node = Entity.Null;
            float best = StubNodeSearchM;
            if (EntityManager.TryGetBuffer(station, true, out DynamicBuffer<Game.Net.SubNet> subNets))
            {
                for (int i = 0; i < subNets.Length; i++)
                {
                    Entity e = subNets[i].m_SubNet;
                    if (!EntityManager.TryGetComponent<Game.Net.Node>(e, out var n) || EntityManager.HasComponent<Deleted>(e))
                        continue;
                    float d = math.distance(n.m_Position.xz, stubTip.xz);
                    if (d <= best)
                    {
                        best = d;
                        node = e;
                    }
                }
            }
            if (node == Entity.Null && !quiet)
                Say($"LiftLine [pick]: station {Fmt(station)} has no own net node within {StubNodeSearchM:F0} m of its stub tip " +
                    $"({stubTip.x:F1}, {stubTip.z:F1}) — not usable (is the station's stub missing?)", T("NO_STUB"));
            return node != Entity.Null;
        }

        private int LineEdgesAt(Entity node)
        {
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<Game.Net.ConnectedEdge> edges))
                return 0;
            int count = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                Entity e = edges[i].m_Edge;
                if (EntityManager.HasComponent<Owner>(e) || EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e))
                    continue;
                if (EntityManager.TryGetComponent<PrefabRef>(e, out var pr) && m_Catalog.LiftLineTypes.ContainsKey(pr.m_Prefab))
                    count++;
            }
            return count;
        }

        /// <summary>Every loose end of a player-built lift line: a node with exactly one lift-line edge and no Owner (not a station's
        /// stub). What bulldozing a section leaves at both sides, and the far end of a line drawn line-first. Refreshed every
        /// LooseRefreshFrames while picking (a handful of lines: a main-thread walk).</summary>
        private void RefreshLooseEnds()
        {
            if (m_Frame - m_LooseFrame < LooseRefreshFrames)
                return;
            m_LooseFrame = m_Frame;
            m_LooseEnds.Clear();
            using (var edges = m_LineEdgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(edges[i]).m_Prefab;
                    if (!m_Catalog.LiftLineTypes.TryGetValue(prefab, out LiftTypeDef def))
                        continue;
                    Game.Net.Edge ed = EntityManager.GetComponentData<Game.Net.Edge>(edges[i]);
                    AddLoose(ed.m_Start, def);
                    AddLoose(ed.m_End, def);
                }
            }
        }

        private void AddLoose(Entity node, LiftTypeDef def)
        {
            if (node == Entity.Null || EntityManager.HasComponent<Owner>(node) || EntityManager.HasComponent<Deleted>(node) ||
                !EntityManager.HasComponent<Game.Net.Node>(node))
                return;
            for (int i = 0; i < m_LooseEnds.Count; i++)
                if (m_LooseEnds[i].Node == node)
                    return;
            if (LineEdgesAt(node) != 1)
                return;
            m_LooseEnds.Add(new LooseEnd { Node = node, Pos = NodePos(node), Type = def });
        }

        private float3 NodePos(Entity node) => EntityManager.GetComponentData<Game.Net.Node>(node).m_Position;

        private float EndRope(LineEnd e, float3 pos, float3 other, out string source)
        {
            if (e.Kind == LiftLineEndKind.Ground)
            {
                // The station that will finish this end: the lower end takes the base station. Its lot front lies FrontDistM beyond the
                // end, on the line's axis (the station is aligned to the line, StationAlign).
                StationDef st = m_Type != null ? SkiCatalog.StationOf(m_Type.Index, pos.y <= other.y ? StationRole.Base : StationRole.Top) : null;
                float frontY = float.NaN;
                float dist = LiftTowerPlanConstants.FrontDistM(st);
                float2 outward = pos.xz - other.xz;
                TerrainHeightData hd = m_TerrainSystem.GetHeightData();
                if (math.isfinite(dist) && math.lengthsq(outward) > 1e-6f && hd.isCreated)
                {
                    float2 f = pos.xz + math.normalize(outward) * dist;
                    frontY = TerrainUtils.SampleHeight(ref hd, new float3(f.x, 0f, f.y));
                }
                return LiftLineInput.GroundEndRope(m_C, st, pos.y, frontY, out source);
            }
            // The rope above the node the end carries (a twin head sits lower than the member's RopeAboveBase(0)).
            float above = LiftTowerDiagSystem.RopeAboveNode(EntityManager, e.Node, m_C);
            float fallback = pos.y + above;
            if (e.Kind != LiftLineEndKind.Station || !EntityManager.Exists(e.Node))
            {
                source = "node + " + above.ToString("F2");
                return fallback;
            }
            var sub = new List<float3>();
            if (EntityManager.TryGetBuffer(e.Node, true, out DynamicBuffer<Game.Objects.SubObject> subObjects))
                for (int i = 0; i < subObjects.Length; i++)
                    AddAnchors(subObjects[i].m_SubObject, sub);
            var own = new List<float3>();
            if (EntityManager.TryGetComponent<Owner>(e.Node, out var owner))
                AddAnchors(owner.m_Owner, own);
            float rope = LiftLineInput.StationRope(sub, own, pos, fallback, out bool fromLane);
            source = fromLane ? $"station rope lane anchor, node + {rope - pos.y:F2}"
                              : $"no rope lane anchor within {LiftLineInput.AnchorSearchM:F0} m: node + {above:F2}";
            return rope;
        }

        /// <summary>World end points of an object's prefab HV sub-lanes (the anchors LaneSystem.FindAnchors offers a line lane,
        /// LaneSystem.cs:2371-2416: both ends when the sub-lane joins two nodes, else its start).</summary>
        private void AddAnchors(Entity obj, List<float3> list)
        {
            if (obj == Entity.Null || !EntityManager.TryGetComponent<Game.Objects.Transform>(obj, out var t) ||
                !EntityManager.TryGetComponent<PrefabRef>(obj, out var pr) ||
                !EntityManager.TryGetBuffer(pr.m_Prefab, true, out DynamicBuffer<Game.Prefabs.SubLane> lanes))
                return;
            for (int i = 0; i < lanes.Length; i++)
            {
                Game.Prefabs.SubLane sl = lanes[i];
                if (!CableLanePrefabs.IsHighVoltage(EntityManager, sl.m_Prefab))
                    continue;
                list.Add(Game.Objects.ObjectUtils.LocalToWorld(t, sl.m_Curve.a));
                if (sl.m_NodeIndex.x != sl.m_NodeIndex.y)
                    list.Add(Game.Objects.ObjectUtils.LocalToWorld(t, sl.m_Curve.d));
            }
        }

        private float3 EndPos(LineEnd e) =>
            e.Kind != LiftLineEndKind.Ground && EntityManager.Exists(e.Node) && EntityManager.HasComponent<Game.Net.Node>(e.Node)
                ? NodePos(e.Node) : e.Pos;

        private float EndElevation(LineEnd e) =>
            e.Kind == LiftLineEndKind.LooseEnd && EntityManager.TryGetComponent<Game.Net.Elevation>(e.Node, out var el) ? math.cmin(el.m_Elevation) : 0f;

        private static string Describe(LineEnd e)
        {
            switch (e.Kind)
            {
                case LiftLineEndKind.Station: return "station " + Fmt(e.Station) + " (stub " + Fmt(e.Node) + ")";
                case LiftLineEndKind.LooseEnd: return "loose end " + Fmt(e.Node);
                default: return "ground (" + e.Pos.x.ToString("F0") + ", " + e.Pos.z.ToString("F0") + ")";
            }
        }


        private float PickAlongM => math.max(8f, 1.5f * m_C.StepM);
        private float PickLateralM => math.max(15f, 3f * m_C.HalfWidthM);

        private void UpdateEdit()
        {
            m_HoverTower = -1;
            float along = 0f, lateral = float.PositiveInfinity;
            if (m_HasHit)
            {
                along = m_Edit.Project(m_Hit, out lateral);
                if (math.abs(lateral) <= PickLateralM)
                    m_HoverTower = m_Edit.TowerNear(along, PickAlongM);
            }
            bool onLine = m_HasHit && math.abs(lateral) <= PickLateralM;

            if (Dragging)
            {
                if (m_HasHit)
                {
                    m_DragTo = m_Edit.Snap(along);
                    m_DragOff = LiftLineInput.DragRemoves(lateral, PickLateralM);
                }
                if (!applyAction.IsPressed() || applyAction.WasReleasedThisFrame())
                {
                    float from = m_DragFrom, to = m_DragTo;
                    bool off = m_DragOff;
                    m_DragFrom = m_DragTo = float.NaN;
                    m_DragOff = false;
                    if (off)
                    {
                        // Released off the line = remove
                        bool pinned = m_Edit.IsPinned(from);
                        LiftLineEditResult rr = m_Edit.RemoveTower(from);
                        if (rr == LiftLineEditResult.Done)
                            Replan(pinned ? $"tower at {from:F0} m unpinned (dragged off the line)" :
                                $"tower at {from:F0} m removed (dragged off the line; the others pinned; no new tower within " +
                                $"{m_Edit.RemovedRadiusM:F0} m of it unless the line has no other way)");
                        else
                            Say($"LiftLine [edit]: remove at {from:F0} m refused: {Reason(rr)}", RefusedNotice(rr));
                        return;
                    }
                    LiftLineEditResult r = m_Edit.MoveTower(from, to);
                    if (r == LiftLineEditResult.Done)
                        Replan($"tower moved {from:F0} -> {to:F0} m (pinned)");
                    else if (r != LiftLineEditResult.Unchanged)
                        Say($"LiftLine [edit]: move {from:F0} -> {to:F0} m refused: {Reason(r)}", RefusedNotice(r));
                }
                return;
            }

            if (applyAction.WasPressedThisFrame())
            {
                if (!onLine)
                {
                    Say($"LiftLine [edit]: click {(m_HasHit ? $"{math.abs(lateral):F0} m off" : "not on the terrain")} — click within " +
                        $"{PickLateralM:F0} m of the line", T("CLICK_ON_LINE"));
                    return;
                }
                if (m_HoverTower > 0)
                {
                    m_DragFrom = m_Edit.Plan.Nodes[m_HoverTower].AlongM;
                    m_DragTo = m_DragFrom;
                    m_DragOff = false;
                    return;
                }
                LiftLineEditResult r = m_Edit.AddPin(along);
                if (r == LiftLineEditResult.Done)
                    Replan($"tower added at {m_Edit.Snap(along):F0} m (pinned)");
                else if (r != LiftLineEditResult.Unchanged)
                    Say($"LiftLine [edit]: add at {along:F0} m refused: {Reason(r)}", RefusedNotice(r));
            }
        }

        /// <summary>The player notice for a refused add / move / remove (LocaleEN SkiResort.LiftTool.*); null when nothing needs saying.</summary>
        private LocalizedString? RefusedNotice(LiftLineEditResult r)
        {
            switch (r)
            {
                case LiftLineEditResult.TooCloseToStation: return T("ADD_TOO_CLOSE_END", ("M", N(m_Edit?.LastEndMarginM ?? 0f)));
                case LiftLineEditResult.TooCloseToPin: return T("ADD_TOO_CLOSE_TOWER", ("M", N(m_C?.MinSpanM ?? 0f)));
                case LiftLineEditResult.NoTower: return T("NO_TOWER");
                default: return null;
            }
        }

        /// <summary>The refused-Build notice for the error type the game flagged most (REFUSED[type], else REFUSED[Other]).</summary>
        private static LocalizedString RefusedBuild(string type)
        {
            switch (type)
            {
                case nameof(ErrorType.SteepSlope):
                case nameof(ErrorType.OverlapExisting):
                case nameof(ErrorType.LongDistance):
                case nameof(ErrorType.ShortDistance):
                case nameof(ErrorType.InWater):
                case nameof(ErrorType.ExceedsCityLimits):
                    return T("REFUSED[" + type + "]");
                default:
                    return T("REFUSED[Other]");
            }
        }

        internal const string LocPrefix = "SkiResort.LiftTool.";

        /// <summary>A lift-tool player text: LocaleEN key SkiResort.LiftTool.<paramref name="key"/> with its {ARG} values.</summary>
        internal static LocalizedString T(string key, params (string, ILocElement)[] args) => LocalizedString.Id(LocPrefix + key, args);

        /// <summary>A bend for a player text: one decimal, rounded up like the hub's LiftBend row (LiftCensus.BendWarnTenths).</summary>
        internal static ILocElement Deg(float deg) =>
            new LocalizedNumber<float>(math.max(LiftCensus.BendWarnTenths(deg), 0) / 10f, "floatSingleFraction");

        /// <summary>A whole number for a player text (meters, counts), rounded.</summary>
        internal static ILocElement N(float v) => new LocalizedNumber<int>((int)math.round(v), "integer");

        private string Reason(LiftLineEditResult r)
        {
            switch (r)
            {
                case LiftLineEditResult.TooCloseToStation:
                    return $"too close to that end of the line (keep {m_Edit?.LastEndMarginM ?? 0f:F0} m clear of it)";
                case LiftLineEditResult.TooCloseToPin: return $"too close to a pinned tower (keep {m_C?.MinSpanM ?? 0f:F0} m between towers)";
                case LiftLineEditResult.Unchanged: return "nothing changed";
                case LiftLineEditResult.NoTower: return "no tower there";
                default: return r.ToString();
            }
        }

        private void Replan(string why)
        {
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            if (!hd.isCreated)
            {
                Say("LiftLine [plan]: the terrain is not ready — try again");
                return;
            }
            LiftLineEdit edit = m_Edit;
            LiftTerrainSampler sampler = (s, l) => TerrainUtils.SampleHeight(ref hd, edit.PointAt(s, l));
            // The planner works from the end with the lower rope (LiftLineEdit.FromB); from B it reads this sampler, the exact ground
            // points a line drawn from that end first reads, so the plan does not depend on which end was clicked first.
            LiftTerrainSampler fromB = (s, l) => TerrainUtils.SampleHeight(ref hd, edit.PointFromB(s, l));
            CollectCrossings(edit);   // the planner keeps the box clearance at each crossing with another lift type (FIX-LIST 60 a)
            CollectKeepouts(edit);    // and new towers off the roads, tracks and paths under the line (FIX-LIST 62)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LiftTowerPlan plan = edit.Replan(sampler, fromB);
            sw.Stop();
            m_Draw = LiftPlanDraw.Build(plan, s => edit.PointAt(s), _ => edit.Right);
            MarkSteepSpans(plan, edit);
            var sb = new StringBuilder();
            sb.Append("LiftLine [plan] #").Append(edit.Revision).Append(": ").Append(why).Append(" — ").Append(edit.Describe())
              .Append("; ").Append(sw.Elapsed.TotalMilliseconds.ToString("F1")).Append(" ms (angles ").Append(plan.AnglesMs.ToString("F2"))
              .Append(" ms, work ").Append(plan.AngleWork).Append("; ").Append(plan.AngleSummary()).Append(')');
            for (int i = 1; i < plan.Nodes.Length - 1; i++)
            {
                LiftPlanNode n = plan.Nodes[i];
                sb.Append(i == 1 ? "\n  towers: " : ", ").Append(plan.Constants.NameOf(n.Member, n.Head).Replace("SkiLiftTower", string.Empty)
                  .Replace("SkiTramTower", "T")).Append(" @").Append(n.AlongM.ToString("F0")).Append(edit.IsPinned(n.AlongM) ? " (pin)" : string.Empty)
                  .Append(' ').Append(plan.AngleText(i));
            }
            int lastNode = plan.Nodes.Length - 1;
            if (plan.Nodes[0].HasAngles)
                sb.Append("\n  junction A: ").Append(plan.AngleText(0));
            if (lastNode > 0 && plan.Nodes[lastNode].HasAngles)
                sb.Append("\n  junction B: ").Append(plan.AngleText(lastNode));
            for (int i = 0; i < plan.AngleNotes.Count; i++)
                sb.Append("\n  angles: ").Append(plan.AngleNotes[i]);
            sb.Append("\n  edge slope: max ").Append(plan.MaxEdgeSlope.ToString("F2")).Append(" (the line prefab refuses ")
              .Append(m_C.MaxEdgeSlope.ToString("F2")).Append(", SteepSlope)");
            for (int i = 0; i < plan.Spans.Length; i++)
                if (plan.Spans[i].SlopeViolated)
                    sb.Append("; span ").Append(i).Append(" (").Append(plan.Nodes[i].AlongM.ToString("F0")).Append("..")
                      .Append(plan.Nodes[i + 1].AlongM.ToString("F0")).Append(" m) TOO STEEP ").Append(plan.Spans[i].EdgeSlope.ToString("F2"));
            Say(sb.ToString());
            WarnSunk(plan);
            WarnAlong(edit);
            WarnCrossing(plan);
            WarnKeepouts(plan, edit);
            WarnBackward();
            WarnBend();
        }

        /// <summary>The line is straight, so the only plan bend the tool makes is where it joins a built line at a loose end: the built edge's
        /// far node, the loose end and this line's other end (LiftLineInput.JoinBendWarnDeg). Over LiftAlignMath.BendWarnDeg the tooltip keeps
        /// the BEND notice standing; building stays allowed (owner verdict 2026-11-02: warn, never refuse). The hub's LiftBend row reports
        /// bends on built lines (LiftCensus.MeasureBends). Logged on every plan that has one.</summary>
        private void WarnBend()
        {
            float a = JoinBend(m_Base, m_Top), b = JoinBend(m_Top, m_Base);
            m_BendStandingDeg = math.max(a, b);
            if (m_BendStandingDeg > 0f)
                Mod.Log.Warn($"LiftLine [plan]: BEND — the rope turns {m_BendStandingDeg:F1}° at {Describe(a >= b ? m_Base : m_Top)} " +
                             $"(warning above {LiftAlignMath.BendWarnDeg:F0}°; building stays allowed)");
        }

        private float JoinBend(LineEnd end, LineEnd other)
        {
            if (end.Kind != LiftLineEndKind.LooseEnd || !EntityManager.Exists(end.Node) ||
                !EntityManager.TryGetBuffer(end.Node, true, out DynamicBuffer<Game.Net.ConnectedEdge> edges))
                return 0f;
            float worst = 0f;
            for (int i = 0; i < edges.Length; i++)
            {
                Entity e = edges[i].m_Edge;
                if (EntityManager.HasComponent<Owner>(e) || EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e) ||
                    !EntityManager.TryGetComponent<PrefabRef>(e, out var pr) || !m_Catalog.LiftLineTypes.ContainsKey(pr.m_Prefab) ||
                    !EntityManager.TryGetComponent<Game.Net.Edge>(e, out var ed))
                    continue;
                Entity far = ed.m_Start == end.Node ? ed.m_End : ed.m_Start;
                if (far == end.Node || !EntityManager.TryGetComponent<Game.Net.Node>(far, out var farNode))
                    continue;
                worst = math.max(worst, LiftLineInput.JoinBendWarnDeg(farNode.m_Position, EndPos(end), EndPos(other)));
            }
            return worst;
        }

        /// <summary>A station whose stub points away from the other end: the line runs back through the building and the game builds it
        /// (field test 2026-10-31). Logged on every plan; the tooltip keeps the notice standing while the plan has it.</summary>
        private void WarnBackward()
        {
            bool a = EndFacesAway(m_Base, m_Top), b = EndFacesAway(m_Top, m_Base);
            m_BackwardStanding = a || b;
            if (m_BackwardStanding)
                Mod.Log.Warn($"LiftLine [plan]: BACKWARD — {Describe(a ? m_Base : m_Top)} faces away from the other end; the line runs back through the building");
        }

        private bool EndFacesAway(LineEnd end, LineEnd other)
        {
            if (end.Kind != LiftLineEndKind.Station || !EntityManager.TryGetComponent(end.Station, out Game.Objects.Transform transform))
                return false;
            return StationAlign.LineLeavesBackward(StationAlign.Axis(transform.m_Rotation), end.Pos.xz, other.Pos.xz);
        }

        /// <summary>A line drawn along a built lift (FIX-LIST 60 b, field 2026-11-05): the game merges the two, so the build replaced the
        /// other lift's edges with this line's (a gondola over an FG line took the FG's upper span). Every lift type on both sides, the
        /// tram included. Logged on every plan; the tooltip keeps the notice standing and RequestBuild refuses while the plan has it.</summary>
        private void WarnAlong(LiftLineEdit edit)
        {
            m_AlongStanding = false;
            using (var edges = m_LineEdgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(edges[i]).m_Prefab;
                    if (!m_Catalog.LiftLineTypes.TryGetValue(prefab, out LiftTypeDef def))
                        continue;
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(edges[i]);
                    float m = LiftLineInput.AlongOverlapM(edit.A.xz, edit.B.xz, curve.m_Bezier.a.xz, curve.m_Bezier.d.xz, CrossEndMarginM);
                    if (m <= LiftLineInput.AlongMinOverlapM)
                        continue;
                    m_AlongStanding = true;
                    Mod.Log.Warn($"LiftLine [plan]: ALONG — the line runs along {def.Id} edge {edges[i].Index}:{edges[i].Version} for " +
                                 $"{m:F0} m (within {LiftLineInput.AlongMaxOffsetM:F0} m, {LiftLineInput.AlongMaxAngleDeg:F0} deg); building " +
                                 "would merge them, so Build is refused");
                    return;
                }
            }
        }

        /// <summary>The line's crossings with built lift lines, before each plan (they depend on the two ends only). Every crossing, of
        /// any type, goes to the planner (since the 2026-11-05 collision box round; m_SameTypeCrossing stays false and the CROSSES join
        /// notice is no longer shown). The same type is refused before planning (LiftCrossRefusal.SameType); another type never joins
        /// (12d5ff6, one layer bit per type) and is planned so the two edge boxes clear each other: the game
        /// validates the two edge boxes against each other (LiftPlanCrossing), so each crossing goes to the planner with the crossed edge as
        /// built: its curve height and slope at the crossing (the curve is straight in plan view, so the crossing's fraction of the edge
        /// is the curve's t), its composition's height range and width (NetCompositionData of Composition.m_Edge, checked against
        /// LiftTowerPlanConstants.NetHeightRange once per type and session), the plan angle and the distances to its nearest tower and
        /// station node (a station node: the census rule, LiftTowerFitSystem.IsStationEnd's).</summary>
        private void CollectCrossings(LiftLineEdit edit)
        {
            m_SameTypeCrossing = false;
            m_Crossings.Clear();
            using (var edges = m_LineEdgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(edges[i]).m_Prefab;
                    if (!m_Catalog.LiftLineTypes.TryGetValue(prefab, out LiftTypeDef def))
                        continue;
                    Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(edges[i]);
                    if (!edit.CrossAt(curve.m_Bezier.a.xz, curve.m_Bezier.d.xz, CrossEndMarginM, out float along, out float t))
                        continue;
                    // The same type goes to the planner too (collision box round, 2026-11-05): CourseSplitSystem joins a crossing only
                    // while neither box is wholly over the other at the intersection (IsOver / IsUnder, CourseSplitSystem.cs:2119-2185,
                    // 2301-2312). With the tight box a same-type line a few meters higher was "over" there (no join) while the game's
                    // validation over the whole overlap still found the boxes touching: "Clearance too low" and no join. Planned over
                    // with the box rule, the game still split the drawn course beside the crossing (unjoined), so the planner refuses a
                    // same-type crossing before planning (LiftCrossRefusal.SameType, CROSS_SAME_TYPE).
                    m_Crossings.Add(Crossing(edit, edges[i], def, curve.m_Bezier, along, t));
                }
            }
            edit.Crossings = m_Crossings.Count > 0 ? m_Crossings.ToArray() : null;
        }

        /// <summary>The stretches of the line where a road, track, taxiway or path (prefab RoadData / TrackData / TaxiwayData /
        /// PathwayData; not in a tunnel) comes within its half width (NetCompositionData.m_Width / 2) + the tower head's half width +
        /// LiftLineEdit.KeepoutClearM of the line in plan view: each edge curve as four chords. A tower standing there reads Overlapping
        /// items (vanilla object-vs-net validation; seen in the 2026-11-05 collision box pass), so the planner keeps new towers out of
        /// them where the line has a clear plan without (soft, LiftTowerPlanner.Keepouts).</summary>
        private void CollectKeepouts(LiftLineEdit edit)
        {
            m_Keepouts.Clear();
            float head = edit.C.HalfWidthM + LiftLineEdit.KeepoutClearM;
            using (var edges = m_LineEdgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(edges[i]).m_Prefab;
                    if (!EntityManager.HasComponent<RoadData>(prefab) && !EntityManager.HasComponent<TrackData>(prefab) &&
                        !EntityManager.HasComponent<TaxiwayData>(prefab) && !EntityManager.HasComponent<PathwayData>(prefab))
                        continue;
                    if (EntityManager.TryGetComponent<TaxiwayData>(prefab, out var taxi) && (taxi.m_Flags & TaxiwayFlags.Airspace) != 0)
                        continue;   // an aircraft lane in the sky, not on the ground
                    float half = 4f;
                    if (EntityManager.TryGetComponent<Game.Net.Composition>(edges[i], out var comp) &&
                        EntityManager.TryGetComponent<NetCompositionData>(comp.m_Edge, out var ncd))
                    {
                        if ((ncd.m_Flags.m_General & CompositionFlags.General.Tunnel) != 0)
                            continue;
                        half = 0.5f * ncd.m_Width;
                    }
                    Bezier4x3 curve = EntityManager.GetComponentData<Game.Net.Curve>(edges[i]).m_Bezier;
                    float2 prev = curve.a.xz;
                    for (int q = 1; q <= 4; q++)
                    {
                        float2 next = MathUtils.Position(curve, q * 0.25f).xz;
                        if (edit.KeepoutOf(prev, next, half + head, out float2 stretch))
                            m_Keepouts.Add(stretch);
                        prev = next;
                    }
                }
            }
            LiftLineEdit.MergeStretches(m_Keepouts);
            edit.Keepouts = m_Keepouts.Count > 0 ? m_Keepouts.ToArray() : null;
        }

        /// <summary>One line per plan with road stretches: the stretches, and a WARN for a new tower the planner still stood in one (no
        /// clear plan without it; vanilla shows Overlapping items: move the road or the tower). Pins are the player's and not flagged.</summary>
        private void WarnKeepouts(LiftTowerPlan plan, LiftLineEdit edit)
        {
            if (m_Keepouts.Count == 0)
                return;
            var sb = new StringBuilder("LiftLine [roads]: tower-free stretch(es) ");
            for (int i = 0; i < m_Keepouts.Count; i++)
                sb.Append(i == 0 ? string.Empty : ", ").Append(m_Keepouts[i].x.ToString("F0")).Append("..").Append(m_Keepouts[i].y.ToString("F0")).Append(" m");
            int on = 0;
            for (int n = 1; n < plan.Nodes.Length - 1; n++)
            {
                float s = plan.Nodes[n].AlongM;
                if (edit.IsPinned(s))
                    continue;
                for (int i = 0; i < m_Keepouts.Count; i++)
                    if (s > m_Keepouts[i].x && s < m_Keepouts[i].y)
                    {
                        sb.Append(on++ == 0 ? "; ON A ROAD (no clear plan without): tower @" : ", @").Append(s.ToString("F0"));
                        break;
                    }
            }
            if (on > 0)
                Mod.Log.Warn(sb.ToString());
            else
                Mod.Log.Info(sb.Append("; no tower in them").ToString());
        }

        private LiftPlanCrossing Crossing(LiftLineEdit edit, Entity edge, LiftTypeDef def, Bezier4x3 curve, float along, float t)
        {
            float3 p = MathUtils.Position(curve, t);
            float3 tan = MathUtils.Tangent(curve, t);
            float run = math.length(tan.xz);
            LiftTowerPlanConstants cc = LiftTowerPlanConstants.For(def);
            LiftTowerPlanConstants.NetHeightRange(def.Id, out float hMin, out float hMax);
            float width = 2f * (cc?.HalfWidthM ?? 3f);
            if (EntityManager.TryGetComponent<Game.Net.Composition>(edge, out var comp) &&
                EntityManager.TryGetComponent<NetCompositionData>(comp.m_Edge, out var ncd))
            {
                if (m_CompositionChecked.Add(def.Id))
                {
                    bool same = math.abs(ncd.m_HeightRange.min - hMin) < 0.01f && math.abs(ncd.m_HeightRange.max - hMax) < 0.01f &&
                                math.abs(ncd.m_Width - width) < 0.01f;
                    string text = $"LiftLine [cross]: {def.Id} edge composition height {ncd.m_HeightRange.min:F2}..{ncd.m_HeightRange.max:F2}, " +
                                  $"width {ncd.m_Width:F2} (constants {hMin:F2}..{hMax:F2}, {width:F2})";
                    if (same)
                        Mod.Log.Info(text + ": match");
                    else
                        Mod.Log.Warn(text + ": MISMATCH; the crossing uses the composition, a drawn line of this type plans with the constants");
                }
                hMin = ncd.m_HeightRange.min;
                hMax = ncd.m_HeightRange.max;
                width = ncd.m_Width;
            }
            float len = math.distance(curve.a.xz, curve.d.xz);
            float towerD = float.PositiveInfinity, stationD = float.PositiveInfinity;
            if (EntityManager.TryGetComponent<Game.Net.Edge>(edge, out var ed))
            {
                float dStart = t * len, dEnd = (1f - t) * len;
                if (IsStationNode(ed.m_Start)) stationD = math.min(stationD, dStart); else towerD = math.min(towerD, dStart);
                if (IsStationNode(ed.m_End)) stationD = math.min(stationD, dEnd); else towerD = math.min(towerD, dEnd);
            }
            return new LiftPlanCrossing
            {
                AlongM = along, CrossedY = p.y, CrossedSlope = run > 1e-4f ? math.abs(tan.y) / run : 0f,
                AngleDeg = edit.AngleTo(curve.a.xz, curve.d.xz), CrossedWidthM = width, CrossedMinM = hMin, CrossedMaxM = hMax,
                TowerDistM = towerD, StationDistM = stationD, CrossedClearM = cc?.StationClearM ?? 0f,
                CrossedHeadHalfM = cc?.HalfWidthM ?? 3f, CrossedType = def.Id,
            };
        }

        /// <summary>A station's connection node by the census rule (LiftTowerFitSystem.IsStationEnd): the census station node, else any
        /// owned node.</summary>
        private bool IsStationNode(Entity node) =>
            (m_Census != null && m_Census.IsStationNode(node)) || EntityManager.HasComponent<Owner>(node);

        /// <summary>The plan's crossing verdict (LiftLineInput.CrossKey): the standing notice, Build refused while a crossing cannot be
        /// built, and one CROSS line per crossing with another lift type (side, the separation required and planned between the two edge
        /// curves at the crossing, the reason when refused).</summary>
        private void WarnCrossing(LiftTowerPlan plan)
        {
            m_CrossKey = LiftLineInput.CrossKey(plan, m_SameTypeCrossing, out m_CrossRefused, out m_CrossNeedM);
            LiftPlanCrossing[] xs = plan.Crossings;
            for (int i = 0; i < xs.Length; i++)
            {
                LiftPlanCrossing x = xs[i];
                string line = $"LiftLine [plan]: CROSS {x.CrossedType} at {x.AlongM:F0} m, {x.AngleDeg:F0} deg: " +
                              $"{(x.Side == LiftCrossSide.None ? "not planned" : x.Side.ToString().ToUpperInvariant())}, separation required " +
                              $"{x.RequiredM:F1} m, planned {x.PlannedM:F1} m (crossed curve y {x.CrossedY:F1}, slope {x.CrossedSlope:F2}, box " +
                              $"{x.CrossedMinM:F2}..{x.CrossedMaxM:F2} x {x.CrossedWidthM:F1} m; nearest crossed tower {Dist(x.TowerDistM)}, " +
                              $"station {Dist(x.StationDistM)})";
                if (x.Refusal == LiftCrossRefusal.None)
                    Mod.Log.Info(line + " - OK, Build allowed");
                else
                    Mod.Log.Warn(line + " - REFUSED: " + CrossReason(x.Refusal));
            }
        }

        private static string Dist(float m) => float.IsPositiveInfinity(m) ? "none" : m.ToString("F0") + " m";

        private static string CrossReason(LiftCrossRefusal r)
        {
            switch (r)
            {
                case LiftCrossRefusal.Shallow: return $"the lines meet under {LiftTowerPlanner.CrossMinAngleDeg:F0} deg";
                case LiftCrossRefusal.Station: return "inside a station's run-out of either line (the rope height is fixed there)";
                case LiftCrossRefusal.Tower: return "at or beside a tower of the crossed lift";
                case LiftCrossRefusal.OwnTower: return "a tower of this line stands in the crossed lift's corridor";
                case LiftCrossRefusal.SameType: return "the crossed lift is of the same type (the game splits this line beside the crossing)";
                default: return "no tower arrangement gives the separation (over and under tried where the crossed line is high enough)";
            }
        }

        /// <summary>The crossing notice of m_CrossKey (literal keys, so LiftBatch LP-31 sees each one).</summary>
        private LocalizedString CrossNotice()
        {
            switch (m_CrossKey)
            {
                case "CROSS_SHALLOW": return T("CROSS_SHALLOW");
                case "CROSS_STATION": return T("CROSS_STATION");
                case "CROSS_TOWER": return T("CROSS_TOWER");
                case "CROSS_OWN_TOWER": return T("CROSS_OWN_TOWER");
                case "CROSS_SAME_TYPE": return T("CROSS_SAME_TYPE");
                case "CROSS_CLEARANCE": return T("CROSS_CLEARANCE", ("M", N(m_CrossNeedM)));
                case "CROSSES_OVER": return T("CROSSES_OVER");
                case "CROSSES_UNDER": return T("CROSSES_UNDER");
                default: return T("CROSSES");
            }
        }

        /// <summary>LiftPlanDraw colors a span by floor and height only: a span the engine will refuse as too steep (SteepSlope) is
        /// red too, also between two stations with no tower to mark (a green preview must not end in a refused Build).</summary>
        private void MarkSteepSpans(LiftTowerPlan plan, LiftLineEdit edit)
        {
            float3 start = edit.PointAt(0f);
            for (int g = 0; g < m_Draw.SegA.Length; g++)
            {
                float along = math.distance(0.5f * (m_Draw.SegA[g].xz + m_Draw.SegB[g].xz), start.xz);
                for (int s = 0; s < plan.Spans.Length; s++)
                    if (plan.Spans[s].SlopeViolated && along >= plan.Nodes[s].AlongM && along <= plan.Nodes[s + 1].AlongM)
                        m_Draw.SegClass[g] = 2;
            }
        }

        /// <summary>A station end (or a ground end: the station that will stand there) whose carriers leave below the ground (the station sits in the slope: the no-level terraform keeps
        /// the terrain; no prefab fix, DECISIONS 426: Move It recommended). Logged on every plan; the tooltip keeps the notice standing while the plan has it (m_SunkStanding).</summary>
        private void WarnSunk(LiftTowerPlan plan)
        {
            // FIX-LIST 55: a ground end counts too. It plans with the station that will stand there (GroundEndRope); past a crest
            // vanilla stands it on the lower lot front, so its carriers start below the ground ahead (SUNK_END).
            string sunk = null;
            if (LiftLineInput.SunkEnd(plan.StationA, m_Base.Kind, plan.EndCarrierA))
                sunk = $"{Describe(m_Base)} {-plan.EndCarrierA:F1} m";
            if (LiftLineInput.SunkEnd(plan.StationB, m_Top.Kind, plan.EndCarrierB))
                sunk = (sunk == null ? string.Empty : sunk + ", ") + $"{Describe(m_Top)} {-plan.EndCarrierB:F1} m";
            m_SunkStanding = LiftLineInput.SunkKey(plan, m_Base.Kind, m_Top.Kind);
            if (sunk == null)
                return;
            Mod.Log.Warn($"LiftLine [plan]: SUNK STATION — the carriers leave below the ground at {sunk} (carrier height at the stub tip vs " +
                         "the highest ground across the line). The planner ramps the floor up from there instead of forcing a tall tower " +
                         "beside the station; the chairs clip the slope until the first tower");
        }


        /// <summary>One CreationDefinition + NetCourse per planned span. <paramref name="ghost"/>: the edit preview (the state stays Edit,
        /// nothing is applied); else the Build emission (state Emitted, UpdateEmitted applies or refuses).</summary>
        private void EmitCourses(bool ghost)
        {
            List<LiftLineNode> nodes = m_Edit.Emit();
            if (nodes.Count < 2 || !EndAlive(m_Base) || !EndAlive(m_Top))
            {
                Say("LiftLine [build]: an end node is gone (station or loose end removed) — line dropped",
                    T("END_GONE"));
                Reset("end gone");
                return;
            }
            // Station and loose ends are the live nodes (CoursePos.m_Entity snaps the course to them); a ground end is a fresh node
            // at the clicked ground point. A loose end keeps its tower's Elevation.
            Entity baseNode = m_Base.Kind == LiftLineEndKind.Ground ? Entity.Null : m_Base.Node;
            Entity topNode = m_Top.Kind == LiftLineEndKind.Ground ? Entity.Null : m_Top.Node;
            LiftLineNode first = nodes[0], last = nodes[nodes.Count - 1];
            first.Position = EndPos(m_Base);
            first.ElevationM = EndElevation(m_Base);
            last.Position = EndPos(m_Top);
            last.ElevationM = EndElevation(m_Top);
            nodes[0] = first;
            nodes[nodes.Count - 1] = last;

            EntityCommandBuffer ecb = m_Barrier.CreateCommandBuffer();
            var random = new Unity.Mathematics.Random((uint)(m_Seed++ * 7919 + m_Frame) | 1u);
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                LiftLineNode a = nodes[i], b = nodes[i + 1];
                var course = new NetCourse
                {
                    m_Curve = NetUtils.StraightCurve(a.Position, b.Position),
                    m_FixedIndex = -1,
                };
                course.m_StartPosition = Pos(course.m_Curve, a, 0f, i == 0 ? baseNode : Entity.Null,
                                             i == 0 ? CoursePosFlags.IsFirst : 0);
                course.m_EndPosition = Pos(course.m_Curve, b, 1f, i + 1 == nodes.Count - 1 ? topNode : Entity.Null,
                                           i + 1 == nodes.Count - 1 ? CoursePosFlags.IsLast : 0);
                course.m_Length = MathUtils.Length(course.m_Curve);
                var definition = new CreationDefinition
                {
                    m_Prefab = m_LinePrefab,
                    m_Flags = CreationFlags.SubElevation,
                    m_RandomSeed = random.NextInt(),
                };
                Entity e = ecb.CreateEntity();
                ecb.AddComponent(e, definition);
                ecb.AddComponent(e, course);
                ecb.AddComponent(e, default(Updated));
            }
            // The tree corridor (601 e): the vanilla delete definition for every tree in it, with the same emission
            int fells = EmitCorridorFells(ecb, nodes);
            if (ghost)
            {
                m_Ghost = true;
                m_GhostEdit = m_Edit;
                m_GhostRevision = m_Edit.Revision;
                if (++m_GhostEmits <= 3 || m_GhostEmits % 25 == 0)
                    Mod.Log.Info($"LiftLine [ghost]: preview #{m_GhostEmits} of plan #{m_Edit.Revision}: {nodes.Count - 1} course(s), " +
                                 $"{nodes.Count - 2} tower(s) as Temp (the game's translucent preview, error icons included); " +
                                 $"{fells} tree(s) of the {CorridorWidthM:F1} m corridor shown felled" +
                                 (m_FellCapped > 0 ? $" ({m_FellCapped} over the cap stay)" : string.Empty));
                return;
            }
            m_Emitted = nodes;
            m_EmitFrame = m_Frame;
            m_State = ToolState.Emitted;
            Say($"LiftLine [build]: {nodes.Count - 1} course(s) emitted for {nodes.Count - 2} tower(s), {Describe(m_Base)} -> " +
                $"{Describe(m_Top)} — {m_Edit.Describe()}");
            Mod.Log.Info($"LiftLine [trees]: {fells} tree(s) in the {CorridorWidthM:F1} m corridor emitted to fell with the " +
                         $"line ({m_FellLine.Count} segment(s), {m_FellStubEdges} station stub edge(s)" +
                         (m_FellCapped > 0 ? $", {m_FellCapped} over the cap of {LiftTreeCorridor.MaxFellPerBuild} stay" : string.Empty) + ")");
        }

        private bool EndAlive(LineEnd e) =>
            e.Kind == LiftLineEndKind.Ground || (EntityManager.Exists(e.Node) && !EntityManager.HasComponent<Deleted>(e.Node));

        private static CoursePos Pos(Bezier4x3 curve, LiftLineNode n, float delta, Entity entity, CoursePosFlags end)
        {
            return new CoursePos
            {
                m_Entity = entity,
                m_Position = n.Position,
                m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, delta)),
                m_Elevation = new float2(n.ElevationM, n.ElevationM),
                m_CourseDelta = delta,
                m_SplitPosition = 0f,
                m_Flags = end | CoursePosFlags.IsLeft | CoursePosFlags.IsRight,
                m_ParentMesh = -1,
            };
        }

        private void UpdateEmitted()
        {
            // Count the Temp edges this emission CREATES. A loose end or station stub also gives the existing edge at that node a Temp
            // copy (Temp.m_Original set, no TempFlags.Create: GenerateEdgesSystem.cs:1371-1414): field test 2 counted it as planned 8 / Temp 9.
            int temps = 0, touched = 0;
            using (var edges = m_TempEdgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(edges[i]).m_Prefab != m_LinePrefab)
                        continue;
                    if ((EntityManager.GetComponentData<Temp>(edges[i]).m_Flags & TempFlags.Create) != 0)
                        temps++;
                    else
                        touched++;
                }
            }
            int expected = m_Emitted.Count - 1;
            if (temps == 0 && touched == 0)
            {
                if (m_Frame - m_EmitFrame > TempTimeoutFrames)
                {
                    Mod.Log.Warn($"LiftLine [build]: no Temp edges {TempTimeoutFrames} frames after the emission — nothing built; back " +
                                 "to editing (please send the log)");
                    applyMode = ApplyMode.Clear;
                    m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                    m_State = ToolState.Edit;
                }
                return;
            }
            int errors = m_ErrorQuery.IsEmptyIgnoreFilter ? 0 : m_ErrorQuery.CalculateEntityCount();
            if (!GetAllowApply())
            {
                string kinds = DescribeErrors(out string summary, out string top);
                Mod.Log.Warn($"LiftLine [build]: the game refuses the line ({errors} error(s) on the preview: {summary}; {temps} new Temp " +
                             $"edge(s) of {expected}, {touched} existing edge(s) touched) — nothing built; back to editing. Move the towers " +
                             $"off whatever blocks them and Build again{kinds}");
                LastMessage = $"Build refused: {errors} error(s), {summary}";
                m_Notice = RefusedBuild(top);
                m_NoticeTime = UnityEngine.Time.unscaledTime;
                applyMode = ApplyMode.Clear;
                m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                m_State = ToolState.Edit;   // the ghost comes back next frame, the game's error icons on it
                return;
            }
            applyMode = ApplyMode.Apply;   // ToolOutputSystem.cs:22-29 -> ApplyNetSystem
            m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            LiftToolLines.Line reg = ToolLines.Register(m_Type.Id, m_Emitted);
            m_State = ToolState.Applying;
            Say($"LiftLine [build]: APPLIED — {temps} new Temp edge(s) (planned {expected}{(temps == expected ? string.Empty : "; MISMATCH: the fit still matches the line when every planned node is built (R-6)")}" +
                $"{(touched > 0 ? $"; {touched} existing edge(s) at an end touched" : string.Empty)}), " +
                $"{m_Emitted.Count - 2} tower(s), {reg.Pins} pinned; registered as a tool line (the fit keeps its nodes and members); " +
                $"{ToolLines.Count} tool line(s) this session");
            if (temps != expected)
                Mod.Log.Info(DescribeSplits());
        }

        /// <summary>Every entity the game flagged (the Error query of ToolBaseSystem.GetAllowApply, ToolBaseSystem.cs:313, 533-540), one
        /// line each: kind, prefab, Temp flags, along / lateral on the line, and the error types of its icons (IconElement → icon
        /// PrefabRef → ToolErrorData.m_Error; ValidationSystem.AddIcon, ValidationSystem.cs:1747-1752) with the Target the icon names.
        /// <paramref name="summary"/> counts per type; <paramref name="player"/> is the same in plain words. Always logged on a refusal
        /// (the 2026-10-26 field test had five errors and no type).</summary>
        private string DescribeErrors(out string summary, out string top)
        {
            var counts = new SortedDictionary<string, int>();
            var sb = new StringBuilder();
            using (var flagged = m_ErrorQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < flagged.Length && i < 40; i++)
                {
                    Entity e = flagged[i];
                    string kind = EntityManager.HasComponent<Game.Net.Edge>(e) ? "edge" : EntityManager.HasComponent<Game.Net.Node>(e) ? "node"
                        : EntityManager.HasComponent<Game.Objects.Object>(e) ? "object" : "other";
                    sb.Append("\n  ").Append(kind).Append(' ').Append(Fmt(e)).Append(' ').Append(PrefabName(e));
                    if (EntityManager.TryGetComponent<Temp>(e, out var temp))
                        sb.Append(" Temp ").Append(temp.m_Flags).Append(temp.m_Original != Entity.Null ? " of " + Fmt(temp.m_Original) : string.Empty);
                    if (m_Edit != null && TryPosition(e, out float3 p))
                    {
                        float along = m_Edit.Project(p, out float lat);
                        sb.Append(" at ").Append(along.ToString("F0")).Append(" m (lateral ").Append(lat.ToString("F1")).Append(", y ")
                          .Append(p.y.ToString("F1")).Append(')');
                    }
                    if (!EntityManager.TryGetBuffer(e, true, out DynamicBuffer<Game.Notifications.IconElement> icons) || icons.Length == 0)
                    {
                        sb.Append(": no icon");
                        Count(counts, "untyped");
                        continue;
                    }
                    sb.Append(':');
                    for (int k = 0; k < icons.Length; k++)
                    {
                        Entity icon = icons[k].m_Icon;
                        string type = "untyped";
                        if (EntityManager.TryGetComponent<PrefabRef>(icon, out var ipr) &&
                            EntityManager.TryGetComponent<ToolErrorData>(ipr.m_Prefab, out var ted))
                            type = ted.m_Error.ToString();
                        Count(counts, type);
                        sb.Append(' ').Append(type);
                        if (EntityManager.TryGetComponent<Game.Common.Target>(icon, out var target) && target.m_Target != Entity.Null)
                            sb.Append(" (with ").Append(Fmt(target.m_Target)).Append(' ').Append(PrefabName(target.m_Target)).Append(')');
                    }
                }
                if (flagged.Length > 40)
                    sb.Append("\n  ... ").Append(flagged.Length - 40).Append(" more");
            }
            var sum = new StringBuilder();
            top = string.Empty;
            int topCount = 0;
            foreach (KeyValuePair<string, int> kv in counts)
            {
                sum.Append(sum.Length > 0 ? ", " : string.Empty).Append(kv.Key).Append(" x").Append(kv.Value).Append(" (")
                   .Append(ErrorWords(kv.Key)).Append(')');
                if (kv.Value > topCount)
                {
                    top = kv.Key;
                    topCount = kv.Value;
                }
            }
            summary = sum.Length > 0 ? sum.ToString() : "no error entity found";
            return sb.ToString();
        }

        private static void Count(SortedDictionary<string, int> counts, string key) =>
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

        private static string ErrorWords(string type)
        {
            switch (type)
            {
                case nameof(ErrorType.SteepSlope): return "a span too steep";
                case nameof(ErrorType.OverlapExisting): return "overlaps something";
                case nameof(ErrorType.LongDistance): return "a span too long";
                case nameof(ErrorType.ShortDistance): return "a span too short";
                case nameof(ErrorType.InWater): return "in water";
                case nameof(ErrorType.ExceedsCityLimits): return "outside the city";
                case nameof(ErrorType.LowElevation): return "too low";
                case nameof(ErrorType.TightCurve): return "a curve too tight";
                default: return type;
            }
        }

        private string PrefabName(Entity e) =>
            EntityManager.TryGetComponent<PrefabRef>(e, out var pr) && pr.m_Prefab != Entity.Null ? m_PrefabSystem.GetPrefabName(pr.m_Prefab) : "?";

        private bool TryPosition(Entity e, out float3 p)
        {
            if (EntityManager.TryGetComponent<Game.Net.Curve>(e, out var curve))
            {
                p = MathUtils.Position(curve.m_Bezier, 0.5f);
                return true;
            }
            if (EntityManager.TryGetComponent<Game.Net.Node>(e, out var node))
            {
                p = node.m_Position;
                return true;
            }
            if (EntityManager.TryGetComponent<Game.Objects.Transform>(e, out var t))
            {
                p = t.m_Position;
                return true;
            }
            p = default;
            return false;
        }

        /// <summary>The Temp nodes of this line that the tool did not emit (engine split points), each with its along from the first end,
        /// its height above the ground and whether every planned node has a Temp node within 0.5 m (what the fit's MatchSplit needs).</summary>
        private string DescribeSplits()
        {
            var nodes = new List<float3>();
            using (var edges = m_TempEdgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(edges[i]).m_Prefab != m_LinePrefab)
                        continue;
                    Game.Net.Edge ed = EntityManager.GetComponentData<Game.Net.Edge>(edges[i]);
                    AddNodePos(nodes, ed.m_Start);
                    AddNodePos(nodes, ed.m_End);
                }
            }
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            var sb = new StringBuilder();
            int extras = 0, planned = 0;
            for (int k = 0; k < nodes.Count; k++)
            {
                bool emitted = false;
                for (int j = 0; j < m_Emitted.Count && !emitted; j++)
                    emitted = math.distance(nodes[k].xz, m_Emitted[j].Position.xz) <= LiftToolLines.MatchTolM;
                if (emitted || m_Emitted.Count < 2 ||
                    LiftToolLines.Lateral(m_Emitted[0].Position, m_Emitted[m_Emitted.Count - 1].Position, nodes[k]) > LiftToolLines.ExtraLateralTolM)
                    continue;   // a planned node, or not on this line (a station's own stub node)
                extras++;
                float along = m_Edit != null ? m_Edit.Project(nodes[k], out _) : 0f;
                float above = hd.isCreated ? nodes[k].y - TerrainUtils.SampleHeight(ref hd, nodes[k]) : float.NaN;
                sb.Append(extras == 1 ? string.Empty : ", ").Append(along.ToString("F0")).Append(" m (")
                  .Append(above >= 0f ? "+" : string.Empty).Append(above.ToString("F1")).Append(" m)");
            }
            for (int j = 0; j < m_Emitted.Count; j++)
            {
                for (int k = 0; k < nodes.Count; k++)
                {
                    if (math.distance(nodes[k].xz, m_Emitted[j].Position.xz) <= LiftToolLines.MatchTolM)
                    {
                        planned++;
                        break;
                    }
                }
            }
            return $"LiftLine [build]: engine split points not in the plan: {extras} at {(extras > 0 ? sb.ToString() : "-")} (along from the " +
                   $"first end, node above the ground); planned nodes found {planned} of {m_Emitted.Count}; split gate " +
                   $"{(m_GateAdded.Count > 0 ? "on" : "OFF")} (R-6: elevation transitions are gated, so extras now mean crossings or a gate miss)";
        }

        private void AddNodePos(List<float3> list, Entity node)
        {
            if (node == Entity.Null || !EntityManager.TryGetComponent<Game.Net.Node>(node, out var n))
                return;
            for (int i = 0; i < list.Count; i++)
                if (math.distancesq(list[i].xz, n.m_Position.xz) < 1e-4f)
                    return;
            list.Add(n.m_Position);
        }


        private void DrawPick()
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            LiftTypeDef want = WantType;
            var ring = new float2(0f, 1f);
            int n = m_Census.StationCount;
            for (int i = 0; i < n; i++)
            {
                LiftCensus.Station st = m_Census.StationAt(i);
                if (st.Type == LiftCensus.NoType)
                    continue;
                if (want != null && (st.Type >= SkiCatalog.Lifts.Count || SkiCatalog.Lifts[st.Type] != want))
                    continue;
                UnityEngine.Color c = i == m_HoverStation ? HoverColor : StationColor;
                buffer.DrawCircle(c, c, 0f, OverlayRenderSystem.StyleFlags.Projected, ring, st.StubTip, i == m_HoverStation ? 10f : 7f);
            }
            // The loose ends a section can be redrawn from (amber)
            for (int i = 0; i < m_LooseEnds.Count; i++)
            {
                LooseEnd le = m_LooseEnds[i];
                if (want != null && le.Type != want)
                    continue;
                UnityEngine.Color c = i == m_HoverLoose ? HoverColor : LooseColor;
                buffer.DrawCircle(c, c, 0f, OverlayRenderSystem.StyleFlags.Projected, ring, le.Pos, i == m_HoverLoose ? 8f : 5f);
            }
            DrawSection(buffer);   // the section a click removes (first step only)
            bool onGround = m_HasHit && want != null && m_HoverStation < 0 && m_HoverLoose < 0 && !SectionHovered;
            if (onGround)   // open ground takes a fresh end here
                buffer.DrawCircle(HoverColor, HoverColor, 0f, OverlayRenderSystem.StyleFlags.Projected, ring, m_Hit, 4f);
            if (m_State == ToolState.PickTop && m_HasHit)
            {
                float3 a = EndPos(m_Base);
                buffer.DrawCircle(HoverColor, HoverColor, 0f, OverlayRenderSystem.StyleFlags.Projected, ring, a, 6f);
                buffer.DrawLine(StationColor, StationColor, 0f, OverlayRenderSystem.StyleFlags.Projected,
                    new Line3.Segment(a, m_Hit), 1.5f, new float2(1f, 1f));
            }
        }

        private void DrawPlan()
        {
            if (m_Draw == null || m_Edit?.Plan == null)
                return;
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            var absolute = (OverlayRenderSystem.StyleFlags)0;
            var round = new float2(1f, 1f);
            LiftPlanDraw d = m_Draw;
            for (int i = 0; i < d.SegA.Length; i++)
            {
                UnityEngine.Color c = SegColors[d.SegClass[i]];
                buffer.DrawLine(c, c, 0f, absolute, new Line3.Segment(d.SegA[i], d.SegB[i]), 0.6f, round);
            }
            LiftTowerPlan plan = m_Edit.Plan;
            for (int i = 0; i < d.Base.Length; i++)
            {
                if (d.IsStation[i])
                {
                    buffer.DrawCircle(StationColor, StationColor, 0f, absolute, new float2(0f, 1f), d.Rope[i], 2f);
                    continue;
                }
                int status = 0;
                for (int s = i - 1; s <= i; s++)
                {
                    if (s < 0 || s >= plan.Spans.Length)
                        continue;
                    if (plan.Spans[s].FloorViolated || plan.Spans[s].SlopeViolated)   // red: the engine refuses a too-steep edge (SteepSlope)
                        status = 1;
                    else if (plan.Spans[s].HeightViolated && status == 0)
                        status = 2;
                }
                if (plan.IsOverCap(i) || plan.IsTooSteep(i) || plan.IsOverClearance(i))   // red: past its sheave train's cap (FIX-LIST 55), its pitch cap (600) or its carrier clearance (600 c)
                    status = 1;
                bool hover = i == m_HoverTower && !Dragging;
                UnityEngine.Color tc = hover ? HoverColor : TowerColors[status];
                float ring = 4f + 2f * d.Member[i] / math.max(1, d.LadderLength - 1);
                buffer.DrawCircle(tc, tc, 0f, OverlayRenderSystem.StyleFlags.Projected, new float2(0f, 1f), d.Base[i], hover ? ring + 2f : ring);
                if (m_Edit.IsPinned(plan.Nodes[i].AlongM))
                    buffer.DrawCircle(PinColor, PinColor, 0f, OverlayRenderSystem.StyleFlags.Projected, new float2(0f, 1f), d.Base[i], ring * 0.5f);
                float half = 2f + 2f * d.Member[i] / math.max(1, d.LadderLength - 1);
                float3 r = d.Right[i] * half;
                buffer.DrawLine(tc, tc, 0f, absolute, new Line3.Segment(d.Rope[i] - r, d.Rope[i] + r), 0.5f, round);
                buffer.DrawCircle(tc, tc, 0f, absolute, new float2(0f, 1f), d.Rope[i], 1.6f);
            }
            if (Dragging && !float.IsNaN(m_DragTo))
            {
                // Off the line (release removes the tower): a red ring at the cursor; else white at the snapped spot.
                float3 p = m_DragOff && m_HasHit ? m_Hit : m_Edit.PointAt(m_DragTo);
                p.y = m_HasHit ? m_Hit.y : p.y;
                UnityEngine.Color dc = m_DragOff ? TowerColors[1] : HoverColor;
                buffer.DrawCircle(dc, dc, 0f, OverlayRenderSystem.StyleFlags.Projected, new float2(0f, 1f), p, 8f);
            }
        }


        private void Reset(string why)
        {
            m_State = ToolState.PickBase;
            m_Edit = null;
            m_Draw = null;
            m_Type = null;
            m_C = null;
            m_LinePrefab = Entity.Null;
            m_Base = m_Top = default;
            m_HoverTower = m_HoverStation = m_HoverLoose = -1;
            ResetSectionHover();   // a standing section preview is cleared by the next pick update (or by OnStopRunning)
            m_DragFrom = m_DragTo = float.NaN;
            m_DragOff = false;
            m_BuildRequested = false;
            m_Emitted = null;
            m_SunkStanding = null;
            m_BackwardStanding = false;
            m_SameTypeCrossing = false;
            m_CrossKey = null;
            m_CrossRefused = false;
            m_CrossNeedM = 0f;
            m_Crossings.Clear();
            m_AlongStanding = false;
            m_BendStandingDeg = 0f;
            m_LooseFrame = int.MinValue / 2;
            // A standing ghost is cleared on the next update (UpdateCore's pick branch); a built line consumed its Temps.
            if (why == "built")
            {
                m_Ghost = false;
                m_GhostEdit = null;
                m_GhostRevision = -1;
            }
        }

        /// <summary>Log a line; <paramref name="notice"/> = the short player text the tooltip shows for a few seconds (refusals).</summary>
        private void Say(string line, LocalizedString? notice = null)
        {
            LastMessage = line.Length > 160 ? line.Substring(0, 160) : line;
            if (notice.HasValue)
            {
                m_Notice = notice;
                m_NoticeTime = UnityEngine.Time.unscaledTime;
            }
            Mod.Log.Info(line);
        }

        private void Fail(Exception e, string where)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(LiftLineToolSystem)} EXCEPTION in {where} — the lift-line tool is disabled until the next " +
                              $"game load (the vanilla net tool still works). Please report this stack:\n{e}");
            }
        }

        private static string Fmt(Entity e) => e.Index + ":" + e.Version;
    }
}
