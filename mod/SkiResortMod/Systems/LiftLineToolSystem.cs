// Our own lift-line tool. The node set of the line is the planner's plan: click the base station, click the top station, see the
// planned towers in an overlay preview, drag / add / remove towers (the planner re-plans around the user's pins), then Build:
// the line is emitted as one CreationDefinition + NetCourse per planned span and goes through the game's own apply path
// (validation, cost, ApplyNetSystem). The vanilla net tool stays the fallback (LiftTowerFitSystem fits whatever it builds). The
// hub's lift-line tiles open THIS tool with their lift type (TryActivate(why, type)); the line prefabs have UIObject m_Group
// null, so the hub tile is the only way in. Enter on the default plan builds what quick-place built, with fewer, fitted towers.
//
// Seam (re-derived from the vanilla net tool and docs/research/parkmanager-dissection.md C.1-C.5; nothing copied, GPL):
//   * a prefab-less ToolBaseSystem (the SkiGladeToolSystem skeleton): base.OnCreate registers the tool and its Apply / Secondary
//     Apply / Cancel proxies (ToolBaseSystem.cs:292-316); TrySetPrefab false (ToolSystem.cs:263-277); activation =
//     ToolSystem.activeTool (ToolSystem.cs:84-98) from LiftLineHotkeySystem; raycast = terrain only (InitializeRaycast).
//   * per span one entity CreationDefinition{m_Prefab = the line net, SubElevation} + NetCourse + Updated through the
//     ToolOutputBarrier's ECB, the shape of NetToolSystem.CreateStraightLine (NetToolSystem.cs:3603-3660): StraightCurve, IsFirst
//     on the first start, IsLast on the last end, IsLeft | IsRight everywhere, m_FixedIndex -1, m_ParentMesh -1, rotation
//     NetUtils.GetNodeRotation(tangent). The station ends carry CoursePos.m_Entity = the station's stub node (the node vanilla
//     snaps to; GenerateEdgesSystem.cs:1242-1248 / NodeMapKey m_Original); the inner nodes are plain positions shared bit-for-bit
//     by the two courses they join (NodeMapKey exact float3, GenerateEdgesSystem.cs:59-70), y = ground + E(member), NO FreeHeight
//     (so CourseSplitSystem.SampleCourseHeight keeps the y, CourseSplitSystem.cs:1643-1700).
//   * CourseSplitSystem still cuts any course longer than NetGeometryData.m_EdgeLengthRange.max into equal pieces
//     (CourseSplitSystem.cs:3487-3493) and ValidationHelpers flags an edge longer than 1.1 x max (ValidationHelpers.cs:1395-1425),
//     so while this tool is open the four line prefabs' m_EdgeLengthRange.max / m_ElevatedLength (set from m_MaxPylonDistance,
//     NetInitializeSystem.cs:2180-2181) are raised to the planner's MaxSpanM and restored when it closes (the CursorAimSystem
//     precedent: a prefab-entity data write, logged). The vanilla net tool keeps its own split: a permanent raise cut a long
//     line to one engine node (LiftBatch LP-20).
//   * CourseSplitSystem.CheckHeightRange also cuts a course where its height profile changes elevation class (FreeHeight
//     transition nodes, CourseSplitSystem.cs:2884-2942), which built tool lines with more edges than planned. While the tool is
//     open the line prefabs carry PlacementFlags.ShoreLine, the gate that skips it (SetSplitGate). Crossings with other HV nets
//     still split; the fit matches a line with extra nodes (LiftToolLines.MatchSplit), and a mismatch logs the split points
//     ("engine split points not in the plan").
//   * CalculateElevation re-derives each node's Elevation from the terrain beside it (CourseSplitSystem.cs:3549-3647); the fit
//     system then writes Elevation = (E, E) exactly, recognizing the line as a tool line through ToolLines (LiftToolLines):
//     heights only, with the tool's members, never moved, so a pinned node is never snapped.
//   * Apply: the frame after the emission the Temps exist and are validated (CourseSplit PostTool, GenerateNodes Modification1,
//     GenerateEdges Modification2, Validation ModificationEnd); then applyMode = Apply for one frame when GetAllowApply holds (no
//     Error, ToolBaseSystem.cs:533-540) and the definitions are destroyed (DestroyDefinitions, :506-518). Errors: Clear, back to
//     editing, WARN with the count. Cost is the vanilla Temp pricing (Game.Net/CostSystem, parkmanager-dissection C.6).
//
// Edit loop (the "generate -> edit -> build" pattern; LiftLineEdit holds the model, the input rules are the pure LiftLineInput,
// tested in LiftBatch): left click = the first end, then the other end. An end is a station's stub (a station that already has
// its line is refused: bulldoze the section to rework, then click its loose ends), a loose end of a lift line of the same type (a
// node with one lift-line edge and no station: what a bulldozed section leaves, so a section is redrawn between its two
// neighbors), or, once the type is known (hub tile, or the first end), open ground (a fresh end node). Then left click on the
// line = add a tower (pinned); press on a tower and drag = move it; release it off the line = remove it. Right click = step back
// one action (cancel a drag, undo the last edit, drop the second end, drop the first end) and close the tool only when nothing is
// left. Build key (Enter by default) = build; Esc = the frontend's Back, which closes the tool (unverified in game whether Esc
// also fires the tool's Cancel action: then it steps back). Right click is the tool's Cancel action ONLY; Secondary Apply stays
// off, because both are the right mouse button by default and with both live one click removed a tower AND dropped the line.
//
// Preview (OverlayRenderSystem.Buffer, drawn at ToolUpdate, cleared every frame, OverlayRenderSystem.cs:738-790): the planned
// rope colored by the lowest carrier's floor margin (LiftPlanDraw: green >= 2 m, amber 0-2, red below the floor, violet above
// the max rope height); every tower a ground ring colored by its spans (green clear, red a span below the floor = SHORT, violet
// a span above the max rope height = ROPE TOO HIGH), a blue inner ring when pinned, a cross-bar at rope height sized by the
// member; the hovered tower white; the dragged tower a white ring at the cursor. Span lengths and the plan line are in the
// tooltip (LiftLineTooltipSystem) and in the log.
//
// Logs (silence impossible): ARMED / patch lines per load; one line per activation, station pick, plan (towers, member mix,
// spans, clearance, pins), edit, build (courses emitted), apply (Temps, errors, registered tool line); an exception disables
// the tool until the next load (one "!!!" ERROR with the stack).

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
        private const int NoticeFrames = 240;          // a refusal stays in the tooltip about 4 s
        private static readonly UnityEngine.Color StationColor = new UnityEngine.Color(0.55f, 0.8f, 1f, 0.9f);
        private static readonly UnityEngine.Color LooseColor = new UnityEngine.Color(1.0f, 0.75f, 0.15f, 0.9f);
        private static readonly UnityEngine.Color HoverColor = new UnityEngine.Color(1f, 1f, 1f, 0.95f);
        private static readonly UnityEngine.Color PinColor = new UnityEngine.Color(0.3f, 0.6f, 1f, 0.95f);
        private static readonly UnityEngine.Color[] TowerColors =
        {
            new UnityEngine.Color(0.25f, 0.85f, 0.35f, 0.9f),   // clear
            new UnityEngine.Color(0.95f, 0.2f, 0.2f, 0.95f),    // short (a span below the floor)
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
        internal string Notice => m_Notice != null && m_Frame - m_NoticeFrame < NoticeFrames ? m_Notice : null;

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
        private string m_Notice;
        private int m_NoticeFrame;
        private float3 m_Hit;
        private bool m_HasHit;
        private int m_HoverTower = -1, m_HoverStation = -1, m_HoverLoose = -1;
        private float m_DragFrom = float.NaN, m_DragTo = float.NaN;
        private bool m_DragOff;   // the dragged tower is off the line: release removes it
        private bool m_BuildRequested;
        private int m_EmitFrame, m_Frame, m_Seed = 1;
        private List<LiftLineNode> m_Emitted;


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
            Mod.Log.Info($"{nameof(LiftLineToolSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = m_ErrorLogged = false;
            ToolLines.Clear();
            Reset("load");
            if (mode != GameMode.Game)
            {
                Mod.Log.Info($"LiftLine [tool]: NOT ARMED (mode {mode})");
                return;
            }
            try
            {
                PatchSpans();
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

        /// <summary>Load: remember each lift line prefab's vanilla max edge length (see header, Seam).</summary>
        private void PatchSpans()
        {
            // Prefab entities outlive a city: a load while the spans were raised restores them before reading the vanilla values.
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
                  .Append(math.max(geo.m_EdgeLengthRange.max, c.MaxSpanM).ToString("F0")).Append("); ");
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
        /// !ShoreLine). Without it the engine cuts a tool course wherever its height profile changes elevation class (GetElevationType
        /// :2655-2662: 2 / 4 m above the ground at the piece's sides, m_ElevationLimit 4, NetInitializeSystem.cs:874): a span with an end
        /// tower of Elevation >= 4 m follows the chord where it is above the ground (:1381-1385, :1437-1438), and ground steeper than
        /// m_MaxSlopeSteepness is filled (:1436-1450). Other readers of the bit: CalculateElevation (:3574-3592, only over water >= 4 m
        /// deep) and ValidationHelpers.CheckSurface (:1604, the underwater check of OnGround nets is skipped); NetToolSystem (:6221) only
        /// when the vanilla net tool is the active tool. Same window and restore discipline as the span raise.</summary>
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
                Reset("stopped");
                if (!m_DefinitionQuery.IsEmptyIgnoreFilter)
                    EntityManager.DestroyEntity(m_DefinitionQuery);
                SetSpansRaised(false);
            }
            catch (Exception e)
            {
                Fail(e, "OnStopRunning");
            }
            base.OnStopRunning();
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
                Say($"LiftLine [build]: nothing to build ({m_State}) — pick both ends first", "Nothing to build yet: click both ends first");
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
                    Reset("built");
                    return;
            }

            applyMode = ApplyMode.None;
            // Right click is the tool's Cancel action only (step back); Secondary Apply stays off (see header)
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
                UpdatePick();
                DrawPick();
                return;
            }

            if (m_BuildRequested)
            {
                m_BuildRequested = false;
                EmitCourses();
                DrawPlan();
                return;
            }
            UpdateEdit();
            DrawPlan();
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

        private void UpdatePick()
        {
            RefreshLooseEnds();
            LiftTypeDef want = WantType;
            HoverEnd(want);
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
                    "not offered)", "Click next to a station's line stub, or a loose end of a lift line");
                return;
            }

            if (m_State == ToolState.PickBase)
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(def);
                Entity line = def != null ? m_Catalog.LineEntity(def) : Entity.Null;
                if (c == null || line == Entity.Null)
                {
                    Say($"LiftLine [pick]: lift type {(def != null ? def.Id : "?")} has no line prefab or planner constants — not usable",
                        "This lift type cannot be drawn yet");
                    return;
                }
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
                Say("LiftLine [pick]: that is the first end — click the other one", "That is the first end: click the other one");
                return;
            }
            if (end.Kind == LiftLineEndKind.Station && m_Base.Kind == LiftLineEndKind.Station && end.Station == m_Base.Station)
            {
                Say("LiftLine [pick]: same station as the first end", "That is the first station: click the other one");
                return;
            }
            float3 a = EndPos(m_Base), b = EndPos(end);
            bool bothStations = m_Base.Kind == LiftLineEndKind.Station && end.Kind == LiftLineEndKind.Station;
            float min = LiftLineInput.MinLengthM(m_C, bothStations);
            if (math.distance(a.xz, b.xz) < min)
            {
                Say($"LiftLine [pick]: the ends are {math.distance(a.xz, b.xz):F0} m apart — under {min:F0} m, too short for a line",
                    $"Too close: a line needs at least {min:F0} m");
                return;
            }
            m_Top = end;
            // End ropes. A station end: the height the built line's rope lane will have there, read as the fit reads it
            // (LiftLineInput.StationRope: the lowest HV sub-lane anchor of the stub node's sub-objects / its station within 30 m). A loose
            // end is a tower node at ground + E(member), whose rope is ground + TopM[member] - RopeBelowTopM = node y + RopeAboveBase(0); a
            // ground end gets the shortest member's rope on its fresh node: both keep the node formula (LiftTowerFitSystem uses it for any
            // end without a lane).
            float ropeA = EndRope(m_Base, a, out string srcA), ropeB = EndRope(end, b, out string srcB);
            m_Edit = new LiftLineEdit(m_C, a, b, ropeA, ropeB);
            m_State = ToolState.Edit;
            Replan($"line {m_Type.Id} {Describe(m_Base)} -> {Describe(m_Top)}, {m_Edit.LengthM:F0} m, end ropes {ropeA:F2} ({srcA}) / " +
                   $"{ropeB:F2} ({srcB})");
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
                Say($"LiftLine [pick]: station {Fmt(station)} has no lift type with a line prefab — not usable", "This station cannot take a line");
                return false;
            }
            if (!TryStubNode(station, st.StubTip, out Entity node))
                return false;
            int lines = LineEdgesAt(node);
            if (lines > 0)
            {
                Say($"LiftLine [pick]: station {Fmt(station)} already has its line ({lines} line edge(s) on stub node {Fmt(node)}) — refused; " +
                    "to rework a placed line, bulldoze the section and click its two loose ends",
                    "This station already has its line. To rework part of it, bulldoze that section, then click its two loose ends");
                return false;
            }
            end = new LineEnd { Kind = LiftLineEndKind.Station, Station = station, Node = node, Pos = NodePos(node) };
            return true;
        }

        /// <summary>The station's own net node nearest its stub tip (the node the vanilla net tool snaps a line to: the station-owned
        /// stub node, LiftCensusSystem.StationOfNode), from its SubNet buffer.</summary>
        private bool TryStubNode(Entity station, float3 stubTip, out Entity node)
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
            if (node == Entity.Null)
                Say($"LiftLine [pick]: station {Fmt(station)} has no own net node within {StubNodeSearchM:F0} m of its stub tip " +
                    $"({stubTip.x:F1}, {stubTip.z:F1}) — not usable (is the station's stub missing?)", "This station has no line stub");
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

        private float EndRope(LineEnd e, float3 pos, out string source)
        {
            float fallback = pos.y + m_C.RopeAboveBase(0);
            if (e.Kind != LiftLineEndKind.Station || !EntityManager.Exists(e.Node))
            {
                source = "node + " + m_C.RopeAboveBase(0).ToString("F2");
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
                              : $"no rope lane anchor within {LiftLineInput.AnchorSearchM:F0} m: node + {m_C.RopeAboveBase(0):F2}";
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
                                $"{m_Edit.RemovedRadiusM:F0} m of it)");
                        else
                            Say($"LiftLine [edit]: remove at {from:F0} m refused: {Reason(rr)}", "Cannot remove that tower: " + Reason(rr));
                        return;
                    }
                    LiftLineEditResult r = m_Edit.MoveTower(from, to);
                    if (r == LiftLineEditResult.Done)
                        Replan($"tower moved {from:F0} -> {to:F0} m (pinned)");
                    else if (r != LiftLineEditResult.Unchanged)
                        Say($"LiftLine [edit]: move {from:F0} -> {to:F0} m refused: {Reason(r)}", "Cannot move the tower there: " + Reason(r));
                }
                return;
            }

            if (applyAction.WasPressedThisFrame())
            {
                if (!onLine)
                {
                    Say($"LiftLine [edit]: click {(m_HasHit ? $"{math.abs(lateral):F0} m off" : "not on the terrain")} — click within " +
                        $"{PickLateralM:F0} m of the line", $"Click on the line (within {PickLateralM:F0} m) to add a tower");
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
                    Say($"LiftLine [edit]: add at {along:F0} m refused: {Reason(r)}", "Cannot add a tower there: " + Reason(r));
            }
        }

        private static string Reason(LiftLineEditResult r)
        {
            switch (r)
            {
                case LiftLineEditResult.TooCloseToStation: return "too close to an end of the line";
                case LiftLineEditResult.TooCloseToPin: return "too close to a pinned tower";
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LiftTowerPlan plan = edit.Replan(sampler);
            sw.Stop();
            m_Draw = LiftPlanDraw.Build(plan, s => edit.PointAt(s), _ => edit.Right);
            var sb = new StringBuilder();
            sb.Append("LiftLine [plan] #").Append(edit.Revision).Append(": ").Append(why).Append(" — ").Append(edit.Describe())
              .Append("; ").Append(sw.Elapsed.TotalMilliseconds.ToString("F1")).Append(" ms");
            for (int i = 1; i < plan.Nodes.Length - 1; i++)
            {
                LiftPlanNode n = plan.Nodes[i];
                sb.Append(i == 1 ? "\n  towers: " : ", ").Append(plan.Constants.Ladder[n.Member].Name.Replace("SkiLiftTower", string.Empty)
                  .Replace("SkiTramTower", "T")).Append(" @").Append(n.AlongM.ToString("F0")).Append(edit.IsPinned(n.AlongM) ? " (pin)" : string.Empty);
            }
            Say(sb.ToString());
        }


        private void EmitCourses()
        {
            List<LiftLineNode> nodes = m_Edit.Emit();
            if (nodes.Count < 2 || !EndAlive(m_Base) || !EndAlive(m_Top))
            {
                Say("LiftLine [build]: an end node is gone (station or loose end removed) — line dropped",
                    "An end of the line is gone: click both ends again");
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
            m_Emitted = nodes;
            m_EmitFrame = m_Frame;
            m_State = ToolState.Emitted;
            Say($"LiftLine [build]: {nodes.Count - 1} course(s) emitted for {nodes.Count - 2} tower(s), {Describe(m_Base)} -> " +
                $"{Describe(m_Top)} — {m_Edit.Describe()}");
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
            int temps = 0;
            using (var edges = m_TempEdgeQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                    if (EntityManager.GetComponentData<PrefabRef>(edges[i]).m_Prefab == m_LinePrefab)
                        temps++;
            }
            int expected = m_Emitted.Count - 1;
            if (temps == 0)
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
                Mod.Log.Warn($"LiftLine [build]: the game refuses the line ({errors} error(s) on the preview, {temps} Temp edge(s) of " +
                             $"{expected}) — nothing built; back to editing. Move the towers off whatever blocks them and Build again");
                LastMessage = $"Build refused: {errors} error(s)";
                m_Notice = $"The game refused the line ({errors} problem(s) on the preview): move the towers off what blocks them";
                m_NoticeFrame = m_Frame;
                applyMode = ApplyMode.Clear;
                m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                m_State = ToolState.Edit;
                return;
            }
            applyMode = ApplyMode.Apply;   // ToolOutputSystem.cs:22-29 -> ApplyNetSystem
            m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            LiftToolLines.Line reg = ToolLines.Register(m_Type.Id, m_Emitted);
            m_State = ToolState.Applying;
            Say($"LiftLine [build]: APPLIED — {temps} Temp edge(s) (planned {expected}{(temps == expected ? string.Empty : "; MISMATCH: the fit still matches the line when every planned node is built (R-6)")}), " +
                $"{m_Emitted.Count - 2} tower(s), {reg.Pins} pinned; registered as a tool line (the fit keeps its nodes and members); " +
                $"{ToolLines.Count} tool line(s) this session");
            if (temps != expected)
                Mod.Log.Info(DescribeSplits());
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
            bool onGround = m_HasHit && want != null && m_HoverStation < 0 && m_HoverLoose < 0;
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
                    if (plan.Spans[s].FloorViolated)
                        status = 1;
                    else if (plan.Spans[s].HeightViolated && status == 0)
                        status = 2;
                }
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
            m_DragFrom = m_DragTo = float.NaN;
            m_DragOff = false;
            m_BuildRequested = false;
            m_Emitted = null;
            m_LooseFrame = int.MinValue / 2;
        }

        /// <summary>Log a line; <paramref name="notice"/> = the short player text the tooltip shows for a few seconds (refusals).</summary>
        private void Say(string line, string notice = null)
        {
            LastMessage = line.Length > 160 ? line.Substring(0, 160) : line;
            if (notice != null)
            {
                m_Notice = notice;
                m_NoticeFrame = m_Frame;
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
