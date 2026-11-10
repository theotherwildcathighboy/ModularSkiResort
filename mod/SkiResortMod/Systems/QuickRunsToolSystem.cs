// The Resort Planner tool (code name Quick runs, experimental, DECISIONS 404). Its entry points are the hub's Resort Planner button and
// Ctrl+Shift+Q. Click the corners of a region. With the region empty, click a lift station instead: the region becomes that lift's corridor
// (QuickRunsToolSystem.Plan.cs).
// Enter scans the region with the runs-first planner and its realizer (QuickRunsScanJob: RunsFirstPlanner, RunsFirstRealizer) on a worker
// thread (Worker.cs). The tool previews the plan as an overlay in the piste map's colors: lift groups, proposed lifts, their pistes and
// cat tracks. A click leaves a run, a group or a lift out (or puts it back). The knobs live in the hub. Runs-first reads Density (a new
// plan, RunsFirstOptions.ForDensity) and Kinds (the realizer only). Lifts, Way home, Mix and Shuffle do nothing in runs-first: the tool
// logs and ignores them (plan 7.4, DECISIONS 632).
// Enter builds the enabled runs in one batch (emit, then Temp, then Apply, RunAreaEmit shared with the cat track tool). A refused run is
// dropped and the rest are re-emitted once. The hub's Undo removes the batch with a refund of exactly what the binds charged (never right
// click). The same tool fills the region with trees below a tree line (the glade compose path with a fill mask,
// QuickRunsToolSystem.Trees.cs).
// The skeleton is CatTrackGradeToolSystem's (a prefab-less ToolBaseSystem, point editing, emit then Temp then Apply, hotkey + tooltip
// idioms).
// Files: this (lifecycle, input, knobs), .Scan.cs (snapshot, obstacles, planner launch, warnings), .Worker.cs (the scan job on a worker
// thread, cancel, collect, the plan cache, existing lifts, access points, the dev scan and dump), .Plan.cs (pod / lift toggles, picks, plan around a lift), .Build.cs (emit,
// validate, apply, undo, refund), .Trees.cs (tree filler), .Draw.cs (overlay).
// Logs (silence impossible): ARMED / NOT ARMED per load. One line per activation, scan, toggle, build step, undo, refusal. Developer probes
// ([probe]) only with Measurement diagnostics on. An exception disables the tool until the next load.
// Engine notes: EngineNotes/QuickRunsToolSystem.md

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using SkiResortMod.Settings;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class QuickRunsToolSystem : ToolBaseSystem, IGladeTagSource
    {
        private const float PickPointM = 12f;      // press within this (xz) of a corner drags it
        private const float PickCornerPathM = 12f; // a click within this of an edge inserts a corner there
        private const int NoticeFrames = 300;

        /// <summary>Region: corners (and a stale or no preview). Scanning: the planner runs a budget per frame. Preview: candidates shown,
        /// clicks toggle runs. Emitted: the run definitions are out, waiting for the game's Temp areas and validation. Retry: the frame
        /// between a partial refusal's Clear and the survivors' re-emission. Applying: the frame after Apply.</summary>
        internal enum ToolPhase : byte { Region, Scanning, Preview, Emitted, Retry, Applying }

        /// <summary>Hub reason keys (root LocaleEN.cs) when the tool cannot open.</summary>
        internal const string ReasonOff = "SkiResort.Hub.QUICK_REASON[Off]";
        internal const string ReasonNotArmed = "SkiResort.Hub.QUICK_REASON[NotArmed]";
        internal const string ReasonDisabled = "SkiResort.Hub.QUICK_REASON[Disabled]";

        public override string toolID => "SkiResort Quick Runs Tool";

        public bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;
        public bool IsArmed => m_Armed;
        public bool IsDisabled => m_Disabled;

        /// <summary>The setting (Options, Runs: "Resort Planner (experimental)"). It is read live, null-tolerant (default off).</summary>
        public static bool OptionOn => Mod.Settings?.QuickRunsEnabled ?? false;

        /// <summary>Null when the tool can open. Otherwise the hub reason key (the button stays visible, grayed with this
        /// reason).</summary>
        internal string UnavailableReasonKey => !OptionOn ? ReasonOff : m_Disabled ? ReasonDisabled : !m_Armed ? ReasonNotArmed : null;

        internal ToolPhase Phase => m_Phase;
        internal int Corners => m_Edit.Count;
        internal bool Dragging => m_DragIndex >= 0;
        internal string Notice => m_Notice != null && m_Frame - m_NoticeFrame < NoticeFrames ? m_Notice : null;
        internal bool CanUndoBatch => m_Batch.Count > 0;
        internal int BatchRuns => m_Batch.Count;
        internal long BatchPaid => m_BatchPaidSeen;
        internal AutoPlanKnobs Knobs => m_Knobs;
        internal bool HasPreview => m_Phase == ToolPhase.Preview && m_Published.Status == AutoPlanStatus.Ok;
        internal QuickTotals Totals => m_Totals;
        internal IReadOnlyList<string> WarningKeys => m_WarnKeys;
        internal string StatusKey => m_StatusKey;
        internal float ScanProgress => m_Phase == ToolPhase.Scanning && m_ScanJob != null ? m_ScanJob.Progress : 0f;
        internal AutoPlanResult Result => m_Published;
        internal QuickRegionReadout Readout => m_Readout;
        /// <summary>Mean grade of the outlined ground (0.25 = 25 %): the corner phase's sampled estimate, replaced by the scan's figure
        /// once a scan finishes. NaN with fewer than 3 corners. The site verdict (QuickRunsRules.SiteTierOf) reads it.</summary>
        internal float SiteGrade => m_SiteGrade;
        internal float MaxAreaM2 => PlanGeometry.V1MaxAreaM2;
        /// <summary>Hover label key of the cluster under the cursor (tooltip), or null. HoverClusterRuns = its run count.</summary>
        internal string HoverClusterKey { get; private set; }
        internal int HoverClusterRuns { get; private set; }
        /// <summary>Bumped on every change the hub shows (the binding pushes when it moves).</summary>
        internal int HubRevision { get; private set; }
        internal string LastScanText { get; private set; } = "-";
        internal string LastBuildText { get; private set; } = "-";

        private OverlayRenderSystem m_Overlay;
        private TerrainSystem m_TerrainSystem;
        private WaterSystem m_WaterSystem;
        private SkiPrefabCatalogSystem m_Catalog;
        private SkiRunOwnershipSystem m_Ownership;
        private SkiRunPropSystem m_RunProps;
        private Game.Objects.SearchSystem m_ObjectSearch;
        private CameraUpdateSystem m_Camera;
        private CitySystem m_CitySystem;
        private NameSystem m_NameSystem;
        private ToolOutputBarrier m_Barrier;
        private EntityQuery m_AreaQuery, m_DefinitionQuery, m_TempAreaQuery, m_TreePrefabQuery;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Frame;
        private ToolPhase m_Phase;
        private JobHandle m_Deps;
        private readonly GradeEdit m_Edit = new GradeEdit();   // the region's corners (the cat track tool's point editor)
        private bool m_RegionClosed;   // the first corner was clicked again or a scan started: clicks past the region add nothing
        internal bool RegionClosed => m_RegionClosed && m_Edit.Count >= 3;
        private QuickRegionReadout m_Readout;
        private float m_SiteGrade = float.NaN;
        private float3 m_Hit;
        private bool m_HasHit, m_EnterRequested;
        private readonly List<(string key, int value)> m_HubQueue = new List<(string key, int value)>();   // HubSet queues, DrainHub drains
        private int m_HoverPoint = -1, m_DragIndex = -1;
        private float2 m_DragTo;
        private string m_Notice;
        private int m_NoticeFrame;

        // The run prefabs by RunKind (resolved per load)
        private readonly Entity[] m_RunPrefabs = new Entity[5];

        private void Bump() => HubRevision++;


        protected override void OnCreate()
        {
            base.OnCreate();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_WaterSystem = World.GetOrCreateSystemManaged<WaterSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_RunProps = World.GetOrCreateSystemManaged<SkiRunPropSystem>();
            m_ObjectSearch = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_Barrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_AreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Areas.Area>(), ComponentType.ReadOnly<Game.Areas.Node>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_DefinitionQuery = GetDefinitionQuery();   // All CreationDefinition, None Updated (ToolBaseSystem.cs:689-692)
            m_TempAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Areas.Area>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            m_TreePrefabQuery = GetEntityQuery(ComponentType.ReadOnly<TreeData>(), ComponentType.ReadOnly<PrefabData>());
            Mod.Log.Info($"{nameof(QuickRunsToolSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = m_ErrorLogged = false;
            // Entity handles never survive a load (DECISIONS 404: undo is per tool session. The lists are cleared here and on close)
            ClearBatch("load");
            ClearTreeBatch("load");
            ResetAll("load");
            m_SpeciesResolved = false;
            if (mode != GameMode.Game)
            {
                Mod.Log.Info($"QuickRuns [tool]: NOT ARMED (mode {mode})");
                return;
            }
            m_Armed = true;
            int missing = ResolveRunPrefabs();
            Mod.Log.Info($"QuickRuns [tool]: ARMED — '{toolID}' (experimental, DECISIONS 404; setting {(OptionOn ? "ON" : "OFF: the tool cannot open")}): " +
                         "click the corners of a region (drag one to move it, right click steps back), Enter scans it " +
                         $"(cap {PlanGeometry.V1MaxAreaM2 / 1e6f:F0} km2), a click leaves a run out, Enter builds the enabled runs in one batch " +
                         $"(refused when the city cannot pay the total; a refused run is dropped and the rest re-emitted once), the hub's Undo (not right click) removes the " +
                         $"batch with a refund of what the binds charged; tree filler below a tree line (glade compose with a fill mask). Run prefabs: " +
                         $"{(missing == 0 ? "all found" : missing + " MISSING (those kinds are never proposed)")}; probes with Measurement diagnostics " +
                         $"{(SkiResortSettings.DiagnosticsOn ? "ON" : "off")}");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyMode = ApplyMode.None;
            ResetAll(null);
            Mod.Log.Info($"QuickRuns [tool]: running — knobs {KnobsText(m_Knobs)}");
        }

        protected override void OnStopRunning()
        {
            try
            {
                Mod.Log.Info($"QuickRuns [tool]: closed in phase {m_Phase} with {m_Edit.Count} corner(s), {m_Batch.Count} run(s) and " +
                             $"{m_TreeBatch.Count} tree(s) undoable — the undo lists end with the session");
                if (m_Phase == ToolPhase.Emitted || m_Phase == ToolPhase.Retry)
                {
                    // ToolSystem reads the NEW tool's applyMode on a switch (ToolSystem.cs:147-156). Here we delete our run Temps with
                    // plain Deleted, as ToolClearSystem does (ToolClearSystem.cs:101). This is the cat track tool's idiom.
                    int n = DeleteRunTemps();
                    Mod.Log.Info($"QuickRuns [build]: closed mid-build — {n} preview run(s) of the chunk in flight deleted; {m_BuildDone} of " +
                                 $"{m_BuildTotal} run(s) built before stay as ordinary runs (bind and pay as drawn runs), the rest is not built");
                }
                else if (m_Phase == ToolPhase.Applying)
                    Mod.Log.Info($"QuickRuns [build]: closed in the frame after an Apply — {m_BuildDone} run(s) built before and " +
                                 $"{m_AppliedTemps.Count} applied this frame stay as ordinary runs; the rest of {m_BuildTotal} is not built");
                if (Building && m_Batch.Count > 0)
                {
                    m_BindCheck.Clear();
                    m_BindCheck.AddRange(m_Batch);   // the bind line still reports what was built (it survives the close)
                    m_BindCheckFrame = m_Frame + BindCheckFrames;
                }
                // Tree Temps: the incoming default tool's first frame clears them (the glade tool's OnStopRunning note,
                // DefaultToolSystem.cs:973-980). Never hand-delete thinning Temps.
                ClearBatch(null);
                ClearTreeBatch(null);
                ResetAll("stopped");
                if (!m_DefinitionQuery.IsEmptyIgnoreFilter)
                    EntityManager.DestroyEntity(m_DefinitionQuery);
            }
            catch (Exception e)
            {
                Fail(e, "OnStopRunning");
            }
            base.OnStopRunning();
        }

        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
        }

        protected override bool GetAllowApply()
        {
            return base.GetAllowApply() && (m_Phase == ToolPhase.Emitted || m_TreesPreviewReady);   // ToolBaseSystem.cs:533-540
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


        public bool TryActivate(string why)
        {
            string reason = UnavailableReasonKey;
            if (reason != null)
            {
                Mod.Log.Info($"QuickRuns [tool]: activation refused ({why}) — {(reason == ReasonOff ? "the Resort Planner (Quick runs) option is off: turn it on in Options > Modular Ski Resort > Runs" : m_Disabled ? "disabled after an exception until the next load" : "not armed (not in a game)")}");
                return false;
            }
            if (IsActive)
                return true;
            m_ToolSystem.activeTool = this;
            Mod.Log.Info($"QuickRuns [tool]: activated ({why})");
            return true;
        }

        public void Deactivate()
        {
            if (m_ToolSystem != null && m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
                Mod.Log.Info("QuickRuns [tool]: deactivated");
            }
        }

        /// <summary>The Build key (Enter): scan, build, or plant, by phase (the tooltip's prompt names which).</summary>
        public void RequestEnter()
        {
            if (IsActive)
                m_EnterRequested = true;
        }


        private void UpdateCore()
        {
            m_Frame++;
            applyMode = ApplyMode.None;
            m_ApplyingTrees = false;
            if (!OptionOn)
            {
                Say("QuickRuns [tool]: the Quick runs option was turned off — tool closed");
                Deactivate();
                return;
            }
            bool live = InputManager.instance.controlOverWorld;
            m_HasHit = live && GetRaycastResult(out ControlPoint cp, out bool _) && TakeHit(cp);
            if (Building)
            {
                // Right click stops the build at the next chunk boundary (what is built stays one undoable batch)
                SetActions(false, false, live);
                if (cancelAction.WasPressedThisFrame())
                    RequestStopBuild("right click");
                DrainHub(false);   // Stop acts. Any other hub action is refused as busy
                m_EnterRequested = false;
                if (m_Phase == ToolPhase.Emitted)
                    UpdateEmitted();
                else if (m_Phase == ToolPhase.Retry)
                    EmitBatch(m_EmitWhy);
                else
                    FinishApplied();
                Draw();
                return;
            }
            SetActions(live, false, live);

            // Undo trees: every frame is a slice's emit or Apply (UpdateTreesCycle returns true), so right click is read here
            if (RemovingTrees && cancelAction.WasPressedThisFrame())
                StopPlanting("right click");
            DrainHub(true);   // the hub Stop reaches a tree cycle too. Other actions wait for a free frame
            if (UpdateTreesCycle())
            {
                Draw();
                return;   // a tree Apply / undo frame: no other input this frame
            }

            if (m_Planting)
            {
                // Planting slice after slice: right click stops after the slice in flight. No other input until it ends
                if (cancelAction.WasPressedThisFrame())
                    StopPlanting("right click");
                DrainHub(false);
            }
            else if (m_BuildNextFrame)
            {
                m_BuildNextFrame = false;
                BuildFromTool("after the tree preview cleared");
            }
            else if (cancelAction.WasPressedThisFrame() && PlantPending)
                StopPlanting("right click");   // a Plant waiting for the tree compose job is canceled, the region stays
            else if (cancelAction.WasPressedThisFrame())
                StepBack();
            else if (m_DevRing != null)
                DevScanNow();      // the dev trigger's scan (Worker.cs, Measurement diagnostics)
            else if (m_HubQueue.Count > 0)
                DrainHub(false);   // the hub buttons and knobs take the path Enter takes
            else if (m_EnterRequested)
                Enter();
            else
                UpdateInput();
            m_EnterRequested = false;

            if (m_Phase == ToolPhase.Scanning)
                StepScan();
            UpdateHoverCluster();
            UpdateHoverPlan();
            if (m_TreesOn && !Dragging && m_Phase != ToolPhase.Scanning && TreeComposeDue())
                ComposeTrees("edit");   // debounced: TreeDebounceFrames after the last edit (Trees.cs TreeComposeDue)
            Draw();
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

        /// <summary>Enter by phase: with trees on, plant. In Region, scan. In Preview, build the enabled runs.</summary>
        private void Enter()
        {
            if (m_TreesOn)
            {
                RequestPlant("Enter");
                return;
            }
            switch (m_Phase)
            {
                case ToolPhase.Region:
                    BeginScan("Enter");
                    return;
                case ToolPhase.Preview:
                    BuildFromTool("Enter");
                    return;
                default:
                    return;   // scanning: Enter waits for the result
            }
        }

        /// <summary>Right click: cancel a drag, stop a scan, leave the preview, undo a corner edit, clear the corners, or, with nothing
        /// left, close. It never undoes built runs or planted trees: only the hub's Undo / Undo trees buttons do (field test 2026-10-25:
        /// right click after a build undid the batch 1-2 s later, twice, and the owner thought nothing was built. DECISIONS 418).</summary>
        private void StepBack()
        {
            if (Dragging)
            {
                m_DragIndex = -1;
                Say("QuickRuns [back]: move canceled (right click)");
                return;
            }
            if (m_Phase == ToolPhase.Scanning)
            {
                DropPreview("scan stopped (right click)");
                return;
            }
            if (m_Phase == ToolPhase.Preview)
            {
                DropPreview("preview left (right click): corners kept, Enter scans again");
                return;
            }
            if (m_BatchFresh || m_TreeBatchFresh)
                Say($"QuickRuns [back]: right click after a build — the {m_Batch.Count} run(s) / {m_TreeBatch.Count} tree(s) stay " +
                    "(Undo / Undo trees in the hub remove them)");
            if (m_Edit.Count > 0 && m_Edit.Revision == m_AroundRevision)
            {
                m_Edit.Clear();   // the corners "plan around this lift" set go together, not one by one
                RegionChanged("corners cleared (right click)");
                return;
            }
            if (RegionClosed)
            {
                m_RegionClosed = false;
                Say("QuickRuns [back]: region reopened (right click): clicks add corners again");
                Bump();
                return;
            }
            if (m_Edit.UndoDepth > 0)
            {
                m_Edit.Undo();
                RegionChanged($"undo (right click) — {m_Edit.Count} corner(s) left");
                return;
            }
            if (m_Edit.Count > 0)
            {
                m_Edit.Clear();
                RegionChanged("corners cleared (right click)");
                return;
            }
            Say("QuickRuns [back]: nothing left — tool closed (right click)");
            Deactivate();
        }

        private void UpdateInput()
        {
            float2 hit = m_Hit.xz;
            m_HoverPoint = m_HasHit && !Dragging ? m_Edit.PointNear(hit, PickPointM) : -1;
            if (Dragging)
            {
                if (m_HasHit)
                    m_DragTo = hit;
                if (!applyAction.IsPressed() || applyAction.WasReleasedThisFrame())
                {
                    int i = m_DragIndex;
                    m_DragIndex = -1;
                    if (m_Edit.Move(i, m_DragTo))
                        RegionChanged($"corner {i + 1} moved to ({m_DragTo.x:F0}, {m_DragTo.y:F0})");
                }
                return;
            }
            if (!m_HasHit || !applyAction.WasPressedThisFrame())
                return;
            if (m_HoverPoint >= 0 && (m_Phase != ToolPhase.Region || QuickRunsRules.RegionClick(RegionClosed, m_Edit.Count, m_HoverPoint, m_Edit.Count) == QuickRegionClick.DragCorner))
            {
                m_DragIndex = m_HoverPoint;
                m_DragTo = m_Edit.Points[m_HoverPoint];
                return;
            }
            if (m_Phase == ToolPhase.Preview)
            {
                PreviewPick(hit);   // a lift chord or a pod ring (Plan.cs), else a run (v1)
                return;
            }
            if (m_Phase != ToolPhase.Region)
                return;
            if (m_Edit.Count == 0 && TryPlanAround(hit))
                return;   // a census station: the region becomes that lift's corridor
            int index = m_Edit.InsertIndex(hit, PickCornerPathM);
            switch (QuickRunsRules.RegionClick(RegionClosed, m_Edit.Count, m_HoverPoint, index))
            {
                case QuickRegionClick.CloseRegion:
                    BeginScan("region closed");
                    break;
                case QuickRegionClick.InsertCorner:
                    m_Edit.Insert(index, hit);
                    RegionChanged($"corner inserted as {index + 1} of {m_Edit.Count} at ({hit.x:F0}, {hit.y:F0})");
                    break;
                case QuickRegionClick.AddCorner:
                    // Appending puts the corner on the closing edge (from the last corner to the first), which is where a click past the
                    // last corner belongs
                    m_Edit.Add(hit);
                    RegionChanged($"corner {m_Edit.Count} at ({hit.x:F0}, {hit.y:F0}, ground {m_Hit.y:F1})");
                    break;
            }
        }

        /// <summary>A corner edit: the readout follows. A preview (or scan) is dropped and, when one was showing, re-scanned at once. The
        /// tree preview recomposes.</summary>
        private void RegionChanged(string what)
        {
            bool rescan = m_Phase == ToolPhase.Preview || m_Phase == ToolPhase.Scanning;
            if (m_Edit.Count < 3)
                m_RegionClosed = false;
            ClearPlanToggles();   // pod / lift keys belong to one plan
            m_BatchFresh = false;
            m_TreeBatchFresh = false;
            m_StatusKey = null;
            UpdateReadout();
            Say($"QuickRuns [region]: {what}; {ReadoutText()}");
            if (rescan)
            {
                DropPreview(null);
                if (m_Edit.Count >= 3)
                    BeginScan("region edited");
            }
            if (m_TreesOn)
            {
                m_TreesDirty = true;
                m_TreeSnapStale = true;
            }
            Bump();
        }

        private void UpdateReadout()
        {
            float2[] corners = CornerArray();
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            m_SiteGrade = float.NaN;
            if (corners != null && hd.isCreated)
            {
                for (int i = 0; i < corners.Length; i++)
                {
                    float y = RunAreaEmit.Ground(ref hd, corners[i]).y;
                    lo = math.min(lo, y);
                    hi = math.max(hi, y);
                }
                TerrainHeightData terrain = hd;   // a copy: a lambda cannot capture the struct by reference
                m_SiteGrade = QuickRunsRules.EstimateMeanGrade(corners, p => RunAreaEmit.Ground(ref terrain, p).y);
            }
            m_Readout = QuickRunsRules.Readout(corners, lo, hi, PlanGeometry.V1MaxAreaM2, PlanGeometry.DefaultMinAreaM2);
        }

        private string ReadoutText() => m_Readout.State == QuickRegionState.Open
            ? "region open (under 3 corners)"
            : string.Format(CultureInfo.InvariantCulture, "region {0:F1} ha of {1:F0} ha ({2}), vertical under the corners {3:F0} m",
                m_Readout.AreaM2 / 10000f, PlanGeometry.V1MaxAreaM2 / 10000f, m_Readout.State, m_Readout.VerticalM);

        private float2[] CornerArray()
        {
            int n = m_Edit.Count;
            if (n == 0)
                return null;
            var a = new float2[n];
            for (int i = 0; i < n; i++)
                a[i] = m_Edit.Points[i];
            return a;
        }


        // ---------------------------------------------------------------- knobs and hub actions

        /// <summary>One hub action (SkiResortHubUISystem quickRunsSet(key, value), main thread, in the HUB's update): only queued here.
        /// The tool acts on it in its own OnUpdate (DrainHub), the path Enter takes: a command buffer from the hub's update throws
        /// "Trying to create EntityCommandBuffer when it's not allowed!" (field test 2026-10-25, DECISIONS 418).</summary>
        internal string HubSet(string key, int value)
        {
            if (!IsActive)
                return "tool not active";
            m_HubQueue.Add((key, value));
            return "queued";
        }

        /// <summary>The queued hub actions, in the tool's update. stopOnly: take the stop requests and leave the rest queued (a tree
        /// Apply frame, where nothing else may run).</summary>
        private void DrainHub(bool stopOnly)
        {
            if (m_HubQueue.Count == 0)
                return;
            var items = m_HubQueue.ToArray();
            m_HubQueue.Clear();
            foreach (var (key, value) in items)
            {
                if (stopOnly && key != "stop")
                {
                    m_HubQueue.Add((key, value));
                    continue;
                }
                Mod.Log.Info($"QuickRuns [hub]: {key}={value} — {HubApply(key, value)}");
            }
        }

        private string HubApply(string key, int value)
        {
            if (key == "stop")
            {
                RequestStopBuild("hub");
                StopPlanting("hub");
                return "stop";
            }
            if (Building || m_Planting)
                return "busy: " + (Building ? "building runs" : "planting trees") + " (Stop first)";
            if (m_Phase == ToolPhase.Scanning && ScanBlocks(key))
                return "busy: scanning (wait for the preview, or right click to stop the scan)";
            switch (key)
            {
                case "mix":
                case "lifts":
                case "home":
                case "shuffle":
                    // Runs-first has no mix presets, no lift cap, no way-home level and one deterministic layout (plan 7.4, DECISIONS 632)
                    Say($"QuickRuns [knobs]: {key} -> {value} ignored: the runs-first planner does not use it; {KnobsText(m_Knobs)}");
                    return key + " ignored (runs-first)";
                case "density":
                    return SetKnobs(m_Knobs.Density = QuickRunsRules.ClampDensity(value), "density");
                case "kind":
                    m_Knobs.Kinds = QuickRunsRules.ToggleKind(m_Knobs.Kinds, (AutoKinds)value);
                    return SetKnobs(m_Knobs.Kinds, "kinds");
                case "pod":
                    return TogglePlanKey(true, value);
                case "lift":
                    return TogglePlanKey(false, value);
                case "scan":
                    m_EnterRequested = false;
                    BeginScan("hub");
                    return "scan";
                case "build":
                    BuildFromTool("hub");
                    return "build";
                case "undo":
                    if (m_Batch.Count > 0)
                        UndoBatch("hub");
                    else
                        Say("QuickRuns [undo]: nothing to undo (hub)", NoticeKey("NOTHING_TO_UNDO"));
                    return "undo";
                case "trees":
                    SetTrees(value != 0, "hub");
                    return "trees " + (value != 0);
                case "plant":
                    RequestPlant("hub");
                    return "plant";
                case "treesUndo":
                    RequestTreeUndo("hub");
                    return "treesUndo";
                case "treeLine":
                    SetTreeLine(value, "hub");
                    return "treeLine " + value;
                case "group":
                    SetSpeciesGroup(value, "hub");
                    return "group " + value;
                case "species":
                    SetSpecies(value, "hub");
                    return "species " + value;
                case "level":
                    SetTreeLevel(value, "hub");
                    return "level " + value;
                default:
                    Mod.Log.Warn($"QuickRuns [hub]: unknown action '{key}' ({value}) — ignored (contract drift?)");
                    return "unknown " + key;
            }
        }

        /// <summary>A knob changed (Density or Kinds): the region scans again. The plan cache makes a Kinds change a realization only.</summary>
        private string SetKnobs<T>(T what, string name)
        {
            Say($"QuickRuns [knobs]: {name} -> {what}; {KnobsText(m_Knobs)}");
            Bump();
            if (m_Phase == ToolPhase.Preview || m_Phase == ToolPhase.Scanning)
                RescanForKnobs(name);   // Worker.cs: a running job is canceled, then the scan (a cache hit realizes only)
            return name;
        }

        private static string KnobsText(AutoPlanKnobs k) => $"density {k.Density}, kinds {k.Kinds} (mix, lifts, way home, shuffle: not used by runs-first)";

        private static string NoticeKey(string name) => QuickRunsRules.TipPrefix + name;


        private int ResolveRunPrefabs()
        {
            m_RunPrefabs[(int)RunKind.Groomed] = m_Catalog.EntityOf(SkiCatalog.Names.RunGroomed);
            // Run types R3 (DECISIONS 578): a planned snowmaking or night run is a groomed run with the kind's sliders (FinishApplied seeds
            // them). The retired looks are never emitted.
            m_RunPrefabs[(int)RunKind.Snowmaking] = m_Catalog.EntityOf(SkiCatalog.Names.RunGroomed);
            m_RunPrefabs[(int)RunKind.Night] = m_Catalog.EntityOf(SkiCatalog.Names.RunGroomed);
            m_RunPrefabs[(int)RunKind.Natural] = m_Catalog.EntityOf(SkiCatalog.Names.RunNatural);
            m_RunPrefabs[(int)RunKind.CatTrack] = m_Catalog.EntityOf(SkiCatalog.Names.RunCatTrack);
            int missing = 0;
            for (int k = 0; k < 4; k++)
                if (m_RunPrefabs[k] == Entity.Null)
                    missing++;
            return missing;
        }

        private Entity PrefabOf(RunKind kind)
        {
            int k = (int)kind;
            if (k < 0 || k >= m_RunPrefabs.Length)
                return Entity.Null;
            if (m_RunPrefabs[k] == Entity.Null)
                ResolveRunPrefabs();
            return m_RunPrefabs[k];
        }

        private bool IsOurRunPrefab(Entity prefab)
        {
            if (prefab == Entity.Null)
                return false;
            for (int k = 0; k < m_RunPrefabs.Length; k++)
                if (m_RunPrefabs[k] == prefab)
                    return true;
            return false;
        }

        private long CityMoney()
        {
            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.TryGetComponent(city, out PlayerMoney money))
                return long.MaxValue;   // no money component: the bind skips its charge too (SkiRunOwnershipSystem.Charge)
            return money.money;
        }


        /// <summary>Back to an empty Region (why != null) or just the preview and transient state (why == null).</summary>
        private void ResetAll(string why)
        {
            if (why != null)
            {
                m_Edit.Clear();
                m_RegionClosed = false;
                ClearPlanToggles();
                m_AroundRevision = -1;
                m_Readout = default;
                m_SiteGrade = float.NaN;
                m_BatchFresh = false;
                if (why == "load")
                {
                    m_PlanCache.Clear();
                    m_DoneJob = null;
                    // The knobs and the tree settings live for the load (a reopened tool keeps them)
                    m_Shuffle = 0;
                    m_Knobs = AutoPlanKnobs.Default(0);
                    m_TreeLevel = SkiGladeCompose.DefaultLevel;
                    m_TreeGroup = GroupConifer;
                    m_SpeciesPick = -1;
                }
                SetTreesOff();
            }
            DropPreview(null);
            m_Phase = ToolPhase.Region;
            m_HoverPoint = m_DragIndex = -1;
            m_EnterRequested = false;
            m_HubQueue.Clear();
            if (why != null)
                m_DevRing = null;   // OnStartRunning (why null) keeps a dev scan queued by the activation that started the tool
            m_Notice = null;
            HoverClusterKey = null;
            Bump();
        }

        private void Say(string line, string notice = null)
        {
            if (notice != null)
            {
                m_Notice = notice;
                m_NoticeFrame = m_Frame;
                Bump();
            }
            Mod.Log.Info(line);
        }

        private void Fail(Exception e, string where)
        {
            m_Disabled = true;
            Bump();
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(QuickRunsToolSystem)} EXCEPTION in {where} — the Quick runs tool is disabled until the next game " +
                              $"load. Please report this stack:\n{e}");
            }
        }

        private static string Describe(Entity e) => $"{e.Index}:{e.Version}";
    }
}
