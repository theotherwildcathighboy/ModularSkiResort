// LiftLineToolSystem — our own lift-line tool (T48 steps 3c + 4; build note docs/research/field/2026-10-20/t48-round2-build.md).
// The node set of the line IS the planner's plan: click the base station, click the top station, see the planned towers in an
// overlay preview, drag / add / remove towers (the planner re-plans around the user's pins), then Build: the line is emitted as one
// CreationDefinition + NetCourse per planned span and goes through the game's own apply path (validation, cost, ApplyNetSystem).
// The vanilla net tool stays the fallback (LiftTowerFitSystem fits whatever it builds, as before).
//
// SEAM (re-derived from the vanilla net tool and docs/research/parkmanager-dissection.md C.1-C.5; nothing copied, GPL):
//   * a prefab-less ToolBaseSystem (the SkiGladeToolSystem skeleton): base.OnCreate registers the tool and its Apply / Secondary
//     Apply / Cancel proxies (Game.Tools/ToolBaseSystem.cs:292-316); TrySetPrefab false (ToolSystem.cs:263-277); activation =
//     ToolSystem.activeTool (ToolSystem.cs:84-98) from LiftLineHotkeySystem; raycast = terrain only (InitializeRaycast).
//   * per span one entity CreationDefinition{m_Prefab = the line net, SubElevation} + NetCourse + Updated through the
//     ToolOutputBarrier's ECB, the shape of NetToolSystem.CreateStraightLine (NetToolSystem.cs:3603-3660): StraightCurve, IsFirst on
//     the first start, IsLast on the last end, IsLeft | IsRight everywhere, m_FixedIndex -1, m_ParentMesh -1, rotation
//     NetUtils.GetNodeRotation(tangent). The station ends carry CoursePos.m_Entity = the station's stub node (the node vanilla snaps
//     to; GenerateEdgesSystem.cs:1242-1248 / NodeMapKey m_Original); the inner nodes are plain positions shared bit-for-bit by the
//     two courses they join (NodeMapKey exact float3, GenerateEdgesSystem.cs:59-70), y = ground + E(member), NO FreeHeight (so
//     CourseSplitSystem.SampleCourseHeight keeps the y, CourseSplitSystem.cs:1643-1700).
//   * CourseSplitSystem still cuts any course longer than NetGeometryData.m_EdgeLengthRange.max into equal pieces
//     (CourseSplitSystem.cs:3487-3493) and ValidationHelpers flags an edge longer than 1.1 x max (ValidationHelpers.cs:1395-1425),
//     so WHILE THIS TOOL IS OPEN the four line prefabs' m_EdgeLengthRange.max / m_ElevatedLength (set from m_MaxPylonDistance,
//     NetInitializeSystem.cs:2180-2181) are raised to the planner's MaxSpanM (FG 60 -> 90, DQ 90 -> 245; gondola 250 and tram 1000
//     already reach it) and restored when it closes — the CursorAimSystem precedent (a prefab-entity data write, logged). The
//     vanilla net tool keeps its own split (LiftBatch LP-20: a permanent raise cut tonight's DQ 328 m line to one engine node).
//   * CalculateElevation re-derives each node's Elevation from the terrain beside it (CourseSplitSystem.cs:3549-3647); the fit
//     system then writes Elevation = (E, E) exactly (the T48 step 3 ladder rule), recognising the line as a TOOL LINE through
//     ToolLines (LiftLineEdit.cs LiftToolLines): heights only, with the tool's members, never moved — a pinned node is never
//     snapped.
//   * Apply: the frame after the emission the Temps exist and are validated (CourseSplit PostTool, GenerateNodes Modification1,
//     GenerateEdges Modification2, Validation ModificationEnd); then applyMode = Apply for one frame when GetAllowApply holds (no
//     Error, ToolBaseSystem.cs:533-540) and the definitions are destroyed (DestroyDefinitions, :506-518). Errors: Clear, back to
//     editing, WARN with the count. Cost is the vanilla Temp pricing (Game.Net/CostSystem, parkmanager-dissection C.6).
//
// EDIT LOOP (the Park Manager / Placemaker "generate -> edit -> build" pattern, re-derived; LiftLineEdit holds the model):
//   left click near a station = base, then top (same lift type); left click on the line = pin a tower there; left press on a tower
//   and drag = move it (release re-plans; a free tower becomes a pin); right click on a tower = remove it (a pin is unpinned; a free
//   tower is dropped and the others pinned, its spot excluded); right click elsewhere on the line = clear every pin; Build key
//   (Enter by default) = build; Esc = drop the line (a second Esc leaves the tool through the frontend's Back).
//
// PREVIEW (OverlayRenderSystem.Buffer, drawn at ToolUpdate, cleared by OverlayRenderSystem every frame, OverlayRenderSystem.cs:738-790):
//   the planned rope coloured by the lowest carrier's floor margin (LiftPlanDraw: green >= 2 m, amber 0-2, red below the floor,
//   violet above the max rope height); every tower a ground ring coloured by its spans (green clear, red a span below the floor =
//   SHORT, violet a span above the max rope height = ROPE TOO HIGH), a blue inner ring when pinned, a cross-bar at rope height sized
//   by the member; the hovered tower white; the dragged tower a white ring at the cursor. Span lengths and the plan line are in the
//   tooltip (LiftLineTooltipSystem) and in the log.
//
// LOGS (silence impossible): ARMED / patch lines per load; one line per activation, station pick, plan (towers, member mix, spans,
// clearance, pins), edit (what and the result), build (courses emitted), apply (Temps, errors, registered tool line); an exception
// disables the tool until the next load (one "!!!" ERROR with the stack).

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
        private const float StubNodeSearchM = 12f;     // the station's owned net node nearest the stub tip, within this
        private const int TempTimeoutFrames = 30;
        private static readonly UnityEngine.Color StationColor = new UnityEngine.Color(0.55f, 0.8f, 1f, 0.9f);
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

        /// <summary>Lines built by this tool this session (LiftTowerFitSystem asks Match for every line it fits).</summary>
        internal readonly LiftToolLines ToolLines = new LiftToolLines();

        public bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;
        public ToolState State => m_State;
        internal LiftLineEdit Edit => m_Edit;
        internal int HoverTower => m_HoverTower;
        internal int HoverStation => m_HoverStation;
        internal bool Dragging => !float.IsNaN(m_DragFrom);
        internal string LastMessage { get; private set; } = string.Empty;
        internal string PendingTypeName => m_Type != null ? m_Type.Id : string.Empty;

        private ToolOutputBarrier m_Barrier;
        private TerrainSystem m_TerrainSystem;
        private OverlayRenderSystem m_Overlay;
        private LiftCensusSystem m_Census;
        private SkiPrefabCatalogSystem m_Catalog;
        private EntityQuery m_DefinitionQuery;
        private EntityQuery m_TempEdgeQuery;
        private JobHandle m_Deps;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private ToolState m_State;
        private LiftLineEdit m_Edit;
        private LiftPlanDraw m_Draw;
        private LiftTypeDef m_Type;
        private LiftTowerPlanConstants m_C;
        private Entity m_LinePrefab;
        private Entity m_BaseStation, m_TopStation, m_BaseNode, m_TopNode;
        private float3 m_Hit;
        private bool m_HasHit;
        private int m_HoverTower = -1, m_HoverStation = -1;
        private float m_DragFrom = float.NaN, m_DragTo = float.NaN;
        private bool m_BuildRequested;
        private int m_EmitFrame, m_Frame, m_Seed = 1;
        private List<LiftLineNode> m_Emitted;

        // ── lifecycle ────────────────────────────────────────────────────────────────────────────

        protected override void OnCreate()
        {
            base.OnCreate();   // ToolBaseSystem.cs:292-316: tools.Add(this), proxies, Enabled = false
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
                Mod.Log.Info($"LiftLine [tool]: ARMED — '{toolID}': click a station, then the other station of the same lift; left " +
                             "click on the line pins a tower, drag a tower to move it, right click a tower to remove it (right click " +
                             "elsewhere on the line clears the pins), Build key builds, Esc drops the line. Objective: fewest towers " +
                             "(spans up to FG 90 / DQ 245 / GD 245 / tram 1000 m), tall members where they save a tower");
            }
            catch (Exception e)
            {
                Fail(e, "load");
            }
        }

        /// <summary>The vanilla (m_MaxPylonDistance) max edge length / elevated length of each line prefab, read at load.</summary>
        private readonly Dictionary<Entity, float2> m_VanillaSpans = new Dictionary<Entity, float2>();
        private bool m_SpansRaised;

        /// <summary>Load: remember each lift line prefab's vanilla max edge length (header SEAM, third bullet).</summary>
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

        /// <summary>Tool open: each line prefab's max edge length = max(vanilla, planner MaxSpanM), so CourseSplitSystem does not
        /// cut a planned span and ValidationHelpers does not flag it (header SEAM). Tool closed: the vanilla values back. Permanent
        /// edges are not validated (ValidationHelpers.ValidateEdge runs on Temp entities only), so a built tool line stays valid; a
        /// LATER vanilla edit that makes a Temp copy of a tool span longer than the vanilla max would see it flagged (risk, build
        /// note §7).</summary>
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
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyMode = ApplyMode.None;
            Reset("activated");
            SetSpansRaised(true);
            int stations = m_Census.StationCount;
            Mod.Log.Info($"LiftLine [tool]: running — {stations} lift station(s) in the last census; click the base station's stub " +
                         $"(within {StationPickM:F0} m), then the top station's");
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

        public override PrefabBase GetPrefab() => null;                 // ToolBaseSystem.cs:426
        public override bool TrySetPrefab(PrefabBase prefab) => false;   // :428; ToolSystem.cs:263-277

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();   // clears flags and masks (ToolBaseSystem.cs:430-443)
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

        // ── public controls (LiftLineHotkeySystem, main thread) ──────────────────────────────────

        public bool TryActivate(string why)
        {
            if (m_Disabled || !m_Armed)
            {
                Mod.Log.Info($"LiftLine [tool]: activation refused ({why}) — {(m_Disabled ? "disabled after an exception until the next load" : "not armed (not in a game)")}");
                return false;
            }
            if (IsActive)
                return true;
            m_ToolSystem.activeTool = this;   // ToolSystem.cs:84-98
            Mod.Log.Info($"LiftLine [tool]: activated ({why})");
            return true;
        }

        public void Deactivate()
        {
            if (m_ToolSystem != null && m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
                Mod.Log.Info("LiftLine [tool]: deactivated");
            }
        }

        /// <summary>The Build key (LiftLineHotkeySystem): builds the line being edited on the next tool frame.</summary>
        public void RequestBuild()
        {
            if (!IsActive)
                return;
            if (m_State != ToolState.Edit || m_Edit?.Plan == null)
            {
                Say($"LiftLine [build]: nothing to build ({m_State}) — pick the base and the top station first");
                return;
            }
            m_BuildRequested = true;
        }

        // ── frame ───────────────────────────────────────────────────────────────────────────────

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
            bool pending = m_State != ToolState.PickBase;
            SetActions(live, live && m_State == ToolState.Edit, pending);

            if (pending && cancelAction.WasPressedThisFrame())
            {
                if (Dragging)
                {
                    m_DragFrom = m_DragTo = float.NaN;
                    Say("LiftLine [edit]: drag cancelled");
                }
                else
                {
                    Say($"LiftLine [tool]: {m_State} line dropped (Esc) — click a base station to start again");
                    Reset("cancelled");
                }
                return;
            }

            if (m_State == ToolState.PickBase || m_State == ToolState.PickTop)
            {
                UpdatePick();
                DrawPick();
                return;
            }

            // Edit.
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

        // ── picking the stations ─────────────────────────────────────────────────────────────────

        private void UpdatePick()
        {
            m_HoverStation = m_HasHit ? NearestStation(m_Hit) : -1;
            if (!m_HasHit || !applyAction.WasPressedThisFrame())
                return;
            if (m_HoverStation < 0)
            {
                Say($"LiftLine [pick]: no {(m_State == ToolState.PickTop ? m_Type.Id + " " : string.Empty)}lift station within " +
                    $"{StationPickM:F0} m of the click — click next to a station's line stub");
                return;
            }
            LiftCensus.Station st = m_Census.StationAt(m_HoverStation);
            Entity station = m_Census.StationEntityAt(m_HoverStation);
            if (m_State == ToolState.PickBase)
            {
                LiftTypeDef def = st.Type < SkiCatalog.Lifts.Count ? SkiCatalog.Lifts[st.Type] : null;
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(def);
                Entity line = def != null ? m_Catalog.LineEntity(def) : Entity.Null;
                if (c == null || line == Entity.Null)
                {
                    Say($"LiftLine [pick]: station {Fmt(station)} has no lift type with a line prefab and planner constants — not usable");
                    return;
                }
                if (!TryStubNode(station, st.StubTip, line, out Entity node))
                    return;
                m_Type = def;
                m_C = c;
                m_LinePrefab = line;
                m_BaseStation = station;
                m_BaseNode = node;
                m_State = ToolState.PickTop;
                Say($"LiftLine [pick]: base = {def.Id} station {Fmt(station)} (stub node {Fmt(node)}); click the other station");
                return;
            }
            // PickTop.
            if (station == m_BaseStation)
            {
                Say("LiftLine [pick]: that is the first station — click the other one");
                return;
            }
            if (!TryStubNode(station, st.StubTip, m_LinePrefab, out Entity top))
                return;
            float3 a = EntityManager.GetComponentData<Game.Net.Node>(m_BaseNode).m_Position;
            float3 b = EntityManager.GetComponentData<Game.Net.Node>(top).m_Position;
            if (math.distance(a.xz, b.xz) < 2f * m_C.MinSpanM)
            {
                Say($"LiftLine [pick]: the stations are {math.distance(a.xz, b.xz):F0} m apart — too close for a line");
                return;
            }
            m_TopStation = station;
            m_TopNode = top;
            // Station ropes: the stub node + the shortest member's rope (the line's own HV lane rides node y + lane y, LaneSystem.cs
            // :4112-4115; the 2026-10-20 fit lines read 947.84 at a 940.84 stub = +7.0 FG, tram +26.25).
            float ropeA = a.y + m_C.RopeAboveBase(0), ropeB = b.y + m_C.RopeAboveBase(0);
            m_Edit = new LiftLineEdit(m_C, a, b, ropeA, ropeB);
            m_State = ToolState.Edit;
            Replan($"line {m_Type.Id} {Fmt(m_BaseStation)} -> {Fmt(m_TopStation)}, {m_Edit.LengthM:F0} m, station ropes " +
                   $"{ropeA:F2} / {ropeB:F2}");
        }

        /// <summary>The census station (of the pending type when picking the top) whose stub tip is nearest the hit, within
        /// StationPickM; -1 when none.</summary>
        private int NearestStation(float3 hit)
        {
            int best = -1;
            float bestD = StationPickM;
            int n = m_Census.StationCount;
            for (int i = 0; i < n; i++)
            {
                LiftCensus.Station st = m_Census.StationAt(i);
                if (st.Type == LiftCensus.NoType)
                    continue;
                if (m_State == ToolState.PickTop && (m_Type == null || st.Type >= SkiCatalog.Lifts.Count || SkiCatalog.Lifts[st.Type] != m_Type))
                    continue;
                float d = math.distance(st.StubTip.xz, hit.xz);
                if (d <= bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The station's own net node nearest its stub tip (the node the vanilla net tool snaps a line to: the station-owned
        /// stub node, LiftCensusSystem.StationOfNode), from its SubNet buffer.</summary>
        private bool TryStubNode(Entity station, float3 stubTip, Entity linePrefab, out Entity node)
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
                    $"({stubTip.x:F1}, {stubTip.z:F1}) — not usable (is the station's stub missing?)");
            return node != Entity.Null;
        }

        // ── editing ─────────────────────────────────────────────────────────────────────────────

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
                    m_DragTo = m_Edit.Snap(along);
                if (!applyAction.IsPressed() || applyAction.WasReleasedThisFrame())
                {
                    float from = m_DragFrom, to = m_DragTo;
                    m_DragFrom = m_DragTo = float.NaN;
                    LiftLineEditResult r = m_Edit.MoveTower(from, to);
                    if (r == LiftLineEditResult.Done)
                        Replan($"tower moved {from:F0} -> {to:F0} m (pinned)");
                    else
                        Say($"LiftLine [edit]: move {from:F0} -> {to:F0} m refused: {Reason(r)}");
                }
                return;
            }

            if (applyAction.WasPressedThisFrame())
            {
                if (!onLine)
                {
                    Say($"LiftLine [edit]: click {(m_HasHit ? $"{math.abs(lateral):F0} m off" : "not on the terrain")} — click within " +
                        $"{PickLateralM:F0} m of the line");
                    return;
                }
                if (m_HoverTower > 0)
                {
                    m_DragFrom = m_Edit.Plan.Nodes[m_HoverTower].AlongM;
                    m_DragTo = m_DragFrom;
                    return;
                }
                LiftLineEditResult r = m_Edit.AddPin(along);
                if (r == LiftLineEditResult.Done)
                    Replan($"tower added at {m_Edit.Snap(along):F0} m (pinned)");
                else
                    Say($"LiftLine [edit]: add at {along:F0} m refused: {Reason(r)}");
                return;
            }

            if (secondaryApplyAction.WasPressedThisFrame() && onLine)
            {
                if (m_HoverTower > 0)
                {
                    float at = m_Edit.Plan.Nodes[m_HoverTower].AlongM;
                    bool pinned = m_Edit.IsPinned(at);
                    LiftLineEditResult r = m_Edit.RemoveTower(at);
                    if (r == LiftLineEditResult.Done)
                        Replan(pinned ? $"tower at {at:F0} m unpinned" :
                            $"tower at {at:F0} m removed (the others pinned; no new tower within {m_Edit.RemovedRadiusM:F0} m of it)");
                    else
                        Say($"LiftLine [edit]: remove at {at:F0} m refused: {Reason(r)}");
                }
                else if (m_Edit.Pins.Count > 0 || m_Edit.Removed.Count > 0)
                {
                    m_Edit.ClearPins();
                    Replan("every pin cleared (right click off a tower): the planner's own line");
                }
            }
        }

        private static string Reason(LiftLineEditResult r)
        {
            switch (r)
            {
                case LiftLineEditResult.TooCloseToStation: return "too close to a station";
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

        // ── build ───────────────────────────────────────────────────────────────────────────────

        private void EmitCourses()
        {
            List<LiftLineNode> nodes = m_Edit.Emit();
            if (nodes.Count < 2 || !EntityManager.Exists(m_BaseNode) || !EntityManager.Exists(m_TopNode))
            {
                Say("LiftLine [build]: a station's stub node is gone — line dropped");
                Reset("stub gone");
                return;
            }
            // The station ends are the live stub nodes.
            LiftLineNode first = nodes[0], last = nodes[nodes.Count - 1];
            first.Position = EntityManager.GetComponentData<Game.Net.Node>(m_BaseNode).m_Position;
            last.Position = EntityManager.GetComponentData<Game.Net.Node>(m_TopNode).m_Position;
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
                course.m_StartPosition = Pos(course.m_Curve, a, 0f, i == 0 ? m_BaseNode : Entity.Null,
                                             i == 0 ? CoursePosFlags.IsFirst : 0);
                course.m_EndPosition = Pos(course.m_Curve, b, 1f, i + 1 == nodes.Count - 1 ? m_TopNode : Entity.Null,
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
            Say($"LiftLine [build]: {nodes.Count - 1} course(s) emitted for {nodes.Count - 2} tower(s) — {m_Edit.Describe()}");
        }

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
                applyMode = ApplyMode.Clear;
                m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                m_State = ToolState.Edit;
                return;
            }
            applyMode = ApplyMode.Apply;   // ToolOutputSystem.cs:22-29 -> ApplyNetSystem
            m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            LiftToolLines.Line reg = ToolLines.Register(m_Type.Id, m_Emitted);
            m_State = ToolState.Applying;
            Say($"LiftLine [build]: APPLIED — {temps} Temp edge(s) (planned {expected}{(temps == expected ? string.Empty : "; MISMATCH, please send the log")}), " +
                $"{m_Emitted.Count - 2} tower(s), {reg.Pins} pinned; registered as a tool line (the fit keeps its nodes and members); " +
                $"{ToolLines.Count} tool line(s) this session");
        }

        // ── preview ─────────────────────────────────────────────────────────────────────────────

        private void DrawPick()
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            int n = m_Census.StationCount;
            for (int i = 0; i < n; i++)
            {
                LiftCensus.Station st = m_Census.StationAt(i);
                if (st.Type == LiftCensus.NoType)
                    continue;
                if (m_State == ToolState.PickTop && (st.Type >= SkiCatalog.Lifts.Count || SkiCatalog.Lifts[st.Type] != m_Type))
                    continue;
                UnityEngine.Color c = i == m_HoverStation ? HoverColor : StationColor;
                buffer.DrawCircle(c, c, 0f, OverlayRenderSystem.StyleFlags.Projected, new float2(0f, 1f), st.StubTip, i == m_HoverStation ? 10f : 7f);
            }
            if (m_State == ToolState.PickTop && m_HasHit && EntityManager.Exists(m_BaseNode))
            {
                float3 a = EntityManager.GetComponentData<Game.Net.Node>(m_BaseNode).m_Position;
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
                float3 p = m_Edit.PointAt(m_DragTo);
                p.y = m_HasHit ? m_Hit.y : p.y;
                buffer.DrawCircle(HoverColor, HoverColor, 0f, OverlayRenderSystem.StyleFlags.Projected, new float2(0f, 1f), p, 8f);
            }
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────────

        private void Reset(string why)
        {
            m_State = ToolState.PickBase;
            m_Edit = null;
            m_Draw = null;
            m_Type = null;
            m_C = null;
            m_LinePrefab = m_BaseStation = m_TopStation = m_BaseNode = m_TopNode = Entity.Null;
            m_HoverTower = m_HoverStation = -1;
            m_DragFrom = m_DragTo = float.NaN;
            m_BuildRequested = false;
            m_Emitted = null;
        }

        private void Say(string line)
        {
            LastMessage = line.Length > 160 ? line.Substring(0, 160) : line;
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
