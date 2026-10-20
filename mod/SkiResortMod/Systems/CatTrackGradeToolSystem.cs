// CatTrackGradeToolSystem — the Cat Track Grading tool (T139c; DECISIONS 362 option A, DECISIONS 363 "build it now"; research
// docs/research/cattrack-grading-2026-10-20.md; build note docs/research/field/2026-10-20/grading-tool-build.md).
// Click points up the mountain; the tool solves a grade-limited track through them (CatTrackGrade.cs, pure, tested in GradeBatch),
// previews it as an overlay, and on Enter writes the corridor into the game's BASE heightmap once (HeightmapWriter): terrain the
// player keeps, stored in the save by the game itself (no save format of ours). The cat-track run is then drawn over graded ground
// with no change to it (its rater reads the new heights).
//
// SKELETON = LiftLineToolSystem's (the lift tool, DECISIONS 351; re-used, not re-derived): a prefab-less ToolBaseSystem
// (base.OnCreate registers it, Game.Tools/ToolBaseSystem.cs:292-316; TrySetPrefab false, ToolSystem.cs:263-277); activation =
// ToolSystem.activeTool (ToolSystem.cs:84-98) from the hub tile, the hotkey (CatTrackGradeHotkeySystem) or the dev tab; raycast =
// terrain only; the tool's Cancel action (right click) is its only secondary input (Secondary Apply off: both are the right mouse
// button by default, the T125 lesson); overlay through OverlayRenderSystem.Buffer from ToolUpdate; refusals as a few-second notice
// in the tooltip; one "!!!" ERROR disables the tool until the next load.
//
// INPUT (GradeInput, pure; the lift tool's model, DECISIONS 351): left click adds a point (within PickPathM of the track: inserts it
// between its two neighbors); press on a point and drag = move it; RIGHT CLICK steps back one action: cancel a drag, else undo the
// last add / insert / move, else (no point left) undo the last grade made since the tool opened, else close the tool. The Build key
// (Enter, the lift tool's binding: LiftLineHotkeySystem keeps it live while either tool is open) grades; Esc = the frontend's Back,
// which closes the tool (the points are dropped, logged). The step hint always names the current step (tooltip, LocalizedString.Value).
//
// SOLVE ON CHANGE, NOT PER FRAME: a point added / inserted / moved (on release) / undone or a grading setting changed marks the plan
// dirty; the next frame re-solves once: path + profile (microseconds), the preview raster on the CPU (cascade) heights, the guards'
// obstacles in the rect (search-tree walk, lift-line edges, water depth on the changed texels). Per frame: the raycast, the precomputed
// overlay lines (about one per 8 m of track plus ticks), the tooltip. Cost line logged per solve.
//
// APPLY (Enter, one frame, main thread): refuse when infeasible (too steep for the limit: the shortfall says how much more track) or
// a guard trips (a building or station lot, a lift line, water: GradeGuards, the reason in the tooltip). Else: read the BASE rect
// (exact heights, no net / lot overlay), re-rasterize on it, re-check the guards, write (verified bit-exact by readback; a mismatch
// writes the pre-image back and refuses), delete the trees standing on the flat top (the way a road clears its lane: plain Deleted on
// trees with no Owner, the SkiGladeCleanupSystem idiom), push the pre-image for undo, tell the run catalog to re-rate (terrain epoch),
// log one line. Free, like the vanilla terrain tool (DECISIONS 363).
//
// UNDO: GradeUndo keeps the last 8 grades of the session (64 MB cap). Right click with no point undoes the last grade made since the
// tool opened; the dev tab's "Undo last grade" reaches older ones. Undo writes the pre-image back only where the terrain still holds
// what the grade wrote (a later terrain-tool stroke survives). Removed trees do not come back. Nothing survives a save / load (the
// result is the game's own heightmap; after a reload the vanilla terrain tool is the only undo).
//
// GUARDS (DECISIONS 363: never under lifts, lots or water): lots = every Building / Extension entity's lot (BuildingData.m_LotSize x 8 m,
// BuildingUtils.CalculateCorners, Game.Buildings/BuildingUtils.cs:72-86) in the static object search tree over the rect; lift lines =
// every lift-line net edge (SkiPrefabCatalogSystem.LiftLineTypes), straight a -> d, LiftClearanceM each side; water = the water
// surface depth (WaterSystem.GetSurfaceData, WaterSystem.cs:754; WaterUtils.SampleDepth, WaterUtils.cs:80) above WetDepthM on any
// texel the grade changes. Roads and other nets are not guarded: their own FlattenTerrain overlay wins over the base map there.
//
// LOGS (silence impossible): ARMED / NOT ARMED per load; one line per activation, point edit, solve (plan, guard, cost), apply (rect,
// texels, volumes, trees, write verify), undo, refusal; the heightmap writer logs each read / write; an exception disables the tool.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class CatTrackGradeToolSystem : ToolBaseSystem
    {
        private const float PickPointM = 10f;      // press within this (xz) of a point drags it
        private const float PickPathM = 12f;       // a click within this of the track inserts a point there
        private const float LiftClearanceM = 6f;   // a changed texel this close to a lift line refuses
        private const float WetDepthM = 0.1f;      // water deeper than this on a changed texel refuses
        private const float TreeMarginM = 1f;      // trees within half the top width + this are cleared
        private const int NoticeFrames = 240;
        private const float ProbeLengthM = 40f;

        private static readonly UnityEngine.Color PointColor = new UnityEngine.Color(0.3f, 0.6f, 1f, 0.95f);
        private static readonly UnityEngine.Color HoverColor = new UnityEngine.Color(1f, 1f, 1f, 0.95f);
        private static readonly UnityEngine.Color EdgeColor = new UnityEngine.Color(1f, 1f, 1f, 0.45f);
        private static readonly UnityEngine.Color CutColor = new UnityEngine.Color(1f, 0.55f, 0.1f, 0.9f);
        private static readonly UnityEngine.Color FillColor = new UnityEngine.Color(0.25f, 0.55f, 1f, 0.9f);
        private static readonly UnityEngine.Color GuardColor = new UnityEngine.Color(0.95f, 0.15f, 0.15f, 0.95f);
        private static readonly UnityEngine.Color[] GradeColors =
        {
            new UnityEngine.Color(0.25f, 0.85f, 0.35f, 0.9f),   // within the limit
            new UnityEngine.Color(1.0f, 0.75f, 0.15f, 0.9f),    // up to 5 points over
            new UnityEngine.Color(0.95f, 0.2f, 0.2f, 0.95f),    // beyond
        };

        public override string toolID => "SkiResort Cat Track Grade Tool";

        public bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;
        public bool Usable => m_Armed && !m_Disabled;
        public bool IsArmed => m_Armed;
        public bool IsDisabled => m_Disabled;
        internal GradeStep Step => GradeInput.StepOf(m_Edit.Count, false);
        internal bool Dragging => m_DragIndex >= 0;
        internal bool CanUndoGrade => m_GradesThisOpening > 0 && m_Undo.Count > 0;
        internal string Notice => m_Notice != null && m_Frame - m_NoticeFrame < NoticeFrames ? m_Notice : null;
        internal string PlanLine => m_Path != null ? GradeInput.Describe(m_Path, m_Prof, m_Raster, m_Params) : null;
        internal bool PlanOk => m_Prof != null && m_Prof.Feasible && m_Guard == GradeGuardKind.None;
        internal string GuardLine => m_Guard != GradeGuardKind.None ? GradeGuards.Reason(m_Guard) : null;
        internal string BankLine => m_Raster != null && m_Raster.SteepBankTexels > 0
            ? string.Format(CultureInfo.InvariantCulture, "Steep bank: the hillside is steeper than the side slope (up to {0:F0} m)", m_Raster.MaxBankStepM)
            : null;
        internal string LastPlanText { get; private set; } = "-";
        internal string LastApplyText { get; private set; } = "-";
        internal int UndoCount => m_Undo.Count;

        private OverlayRenderSystem m_Overlay;
        private TerrainSystem m_TerrainSystem;
        private WaterSystem m_WaterSystem;
        private SkiPrefabCatalogSystem m_Catalog;
        private SkiRunCatalogSystem m_RunCatalog;
        private Game.Objects.SearchSystem m_ObjectSearch;
        private CameraUpdateSystem m_Camera;
        private EntityQuery m_NetEdgeQuery;
        private HeightmapWriter m_Writer;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Frame;
        private readonly GradeEdit m_Edit = new GradeEdit();
        private readonly GradeUndo m_Undo = new GradeUndo();
        private readonly GradeObstacles m_Obstacles = new GradeObstacles();
        private GradeParams m_Params = new GradeParams();
        private GradePath m_Path;
        private GradeProfile m_Prof;
        private GradeRaster m_Raster;
        private GradeGuardKind m_Guard;
        private float2 m_GuardAt;
        private bool m_Dirty, m_ApplyRequested;
        private int m_GradesThisOpening;
        private float3 m_Hit;
        private bool m_HasHit;
        private int m_HoverPoint = -1, m_DragIndex = -1;
        private float2 m_DragTo;
        private string m_Notice;
        private int m_NoticeFrame;

        // Precomputed overlay (rebuilt per solve).
        private readonly List<Line3.Segment> m_DrawSeg = new List<Line3.Segment>();
        private readonly List<UnityEngine.Color> m_DrawColor = new List<UnityEngine.Color>();
        private readonly List<float> m_DrawWidth = new List<float>();

        // Probe follow-up (TickWatch, called every frame by CatTrackGradeHotkeySystem).
        private int m_ProbeCheckFrame = -1, m_ProbeWaterFrame = -1;
        private float2[] m_ProbeXz;
        private float[] m_ProbeY;
        internal string ProbeText { get; private set; } = "not run";

        // ── lifecycle ────────────────────────────────────────────────────────────────────────────

        protected override void OnCreate()
        {
            base.OnCreate();   // ToolBaseSystem.cs:292-316
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_WaterSystem = World.GetOrCreateSystemManaged<WaterSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_ObjectSearch = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_NetEdgeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<Game.Net.Curve>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_Writer = new HeightmapWriter(m_TerrainSystem, m_WaterSystem);
            Mod.Log.Info($"{nameof(CatTrackGradeToolSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = m_ErrorLogged = false;
            m_Undo.Clear();
            m_Writer.ResetGate();
            m_ProbeCheckFrame = m_ProbeWaterFrame = -1;
            ClearPlan("load");
            m_RunCatalog = World.GetExistingSystemManaged<SkiRunCatalogSystem>();
            if (mode != GameMode.Game)
            {
                Mod.Log.Info($"Grade [tool]: NOT ARMED (mode {mode})");
                return;
            }
            m_Armed = true;
            Mod.Log.Info($"Grade [tool]: ARMED — '{toolID}' (T139): click points up the mountain, drag a point to move it, right click " +
                         "steps back one action (with no point: undoes the last grade made since the tool opened; then closes), Enter " +
                         $"grades, Esc closes. {ReadParams().Describe()}; guards: lots, lift lines ({LiftClearanceM:F0} m), water " +
                         $"(> {WetDepthM:F1} m); trees on the flat top are removed; free; undo for this session (last {GradeUndo.MaxEntries})");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyMode = ApplyMode.None;
            ClearPlan("activated");
            m_GradesThisOpening = 0;
            m_Params = ReadParams();
            Mod.Log.Info($"Grade [tool]: running — {m_Params.Describe()}; {m_Undo.Count} grade(s) to undo this session");
        }

        protected override void OnStopRunning()
        {
            try
            {
                if (m_Edit.Count > 0)
                    Mod.Log.Info($"Grade [tool]: closed with {m_Edit.Count} point(s) — dropped (nothing graded)");
                ClearPlan("stopped");
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

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            try
            {
                UpdateCore();
            }
            catch (Exception e)
            {
                Fail(e, "OnUpdate");
                try { Deactivate(); } catch (Exception) { /* logged */ }
            }
            return inputDeps;
        }

        // ── public controls (hub, hotkeys, dev tab; main thread) ─────────────────────────────────

        public bool TryActivate(string why)
        {
            if (m_Disabled || !m_Armed)
            {
                Mod.Log.Info($"Grade [tool]: activation refused ({why}) — {(m_Disabled ? "disabled after an exception until the next load" : "not armed (not in a game)")}");
                return false;
            }
            if (IsActive)
                return true;
            m_ToolSystem.activeTool = this;   // ToolSystem.cs:84-98
            Mod.Log.Info($"Grade [tool]: activated ({why})");
            return true;
        }

        public void Deactivate()
        {
            if (m_ToolSystem != null && m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
                Mod.Log.Info("Grade [tool]: deactivated");
            }
        }

        /// <summary>The Build key: grade on the next tool frame.</summary>
        public void RequestApply()
        {
            if (!IsActive)
                return;
            if (m_Edit.Count < 2)
            {
                Say("Grade [apply]: nothing to grade — click at least two points", "Click at least two points first");
                return;
            }
            m_ApplyRequested = true;
        }

        // ── frame ───────────────────────────────────────────────────────────────────────────────

        private void UpdateCore()
        {
            m_Frame++;
            applyMode = ApplyMode.None;
            bool live = InputManager.instance.controlOverWorld;
            m_HasHit = live && GetRaycastResult(out ControlPoint cp, out bool _) && TakeHit(cp);
            SetActions(live, false, live);

            GradeParams p = ReadParams();
            if (!Same(p, m_Params))
            {
                m_Params = p;
                m_Dirty = true;
                Say($"Grade [settings]: {p.Describe()} — re-solving");
            }

            if (cancelAction.WasPressedThisFrame())
                StepBack();
            else if (m_ApplyRequested)
            {
                m_ApplyRequested = false;
                if (m_Dirty)
                    Solve("before apply");
                ApplyFromTool();
            }
            else
                UpdateEdit();

            if (m_Dirty && !Dragging)
                Solve("edit");
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

        private void StepBack()
        {
            GradeBack back = GradeInput.RightClick(Step, Dragging, m_Edit.UndoDepth, CanUndoGrade ? m_GradesThisOpening : 0);
            switch (back)
            {
                case GradeBack.CancelDrag:
                    m_DragIndex = -1;
                    Say("Grade [back]: move canceled (right click)");
                    return;
                case GradeBack.UndoEdit:
                    m_Edit.Undo();
                    m_Dirty = true;
                    Say($"Grade [back]: undo (right click) — {m_Edit.Count} point(s) left, {m_Edit.UndoDepth} edit(s) to undo");
                    return;
                case GradeBack.UndoGrade:
                    if (UndoLastGrade("right click"))
                        m_GradesThisOpening--;
                    return;
                case GradeBack.CloseTool:
                    Say("Grade [back]: nothing left — tool closed (right click)");
                    Deactivate();
                    return;
            }
        }

        private void UpdateEdit()
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
                    {
                        m_Dirty = true;
                        Say($"Grade [edit]: point {i + 1} moved to ({m_DragTo.x:F0}, {m_DragTo.y:F0})");
                    }
                }
                return;
            }
            if (!m_HasHit || !applyAction.WasPressedThisFrame())
                return;
            if (m_HoverPoint >= 0)
            {
                m_DragIndex = m_HoverPoint;
                m_DragTo = m_Edit.Points[m_HoverPoint];
                return;
            }
            int index = m_Edit.InsertIndex(hit, PickPathM);
            if (index < m_Edit.Count)
            {
                m_Edit.Insert(index, hit);
                Say($"Grade [edit]: point inserted as {index + 1} of {m_Edit.Count} at ({hit.x:F0}, {hit.y:F0})");
            }
            else
            {
                m_Edit.Add(hit);
                Say($"Grade [edit]: point {m_Edit.Count} at ({hit.x:F0}, {hit.y:F0}, ground {m_Hit.y:F1})");
            }
            m_Dirty = true;
        }

        // ── solve (on change only) ─────────────────────────────────────────────────────────────

        private void Solve(string why)
        {
            m_Dirty = false;
            m_Path = null;
            m_Prof = null;
            m_Raster = null;
            m_Guard = GradeGuardKind.None;
            m_DrawSeg.Clear();
            m_DrawColor.Clear();
            m_DrawWidth.Clear();
            if (m_Edit.Count < 2)
                return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            if (!hd.isCreated || !m_Writer.TryMap(out GradeMap map, out string gate))
            {
                Say("Grade [plan]: the terrain is not ready or the heightmap gate refused — no plan", "Grading is not available on this map (see the log)");
                return;
            }
            GradeParams p = m_Params;
            GradePath path = GradePath.Build(ToList(m_Edit.Points), p.SampleStepM);
            if (path == null)
                return;
            var ground = new float[path.Count];
            for (int i = 0; i < path.Count; i++)
                ground[i] = TerrainUtils.SampleHeight(ref hd, new float3(path.Xz[i].x, 0f, path.Xz[i].y));
            GradeProfile prof = GradeProfile.Solve(path, ground, p);
            double solveMs = sw.Elapsed.TotalMilliseconds;

            GradeRaster raster = null;
            double rasterMs = 0, guardMs = 0;
            if (GradeRaster.Bounds(map, path, prof, p, out int4 rect, out float reach))
            {
                raster = GradeRaster.Build(map, rect, path, prof, p, reach, (i, j) =>
                {
                    float2 c = map.TexelCenter(i, j);
                    return TerrainUtils.SampleHeight(ref hd, new float3(c.x, 0f, c.y));
                });
                rasterMs = sw.Elapsed.TotalMilliseconds - solveMs;
                m_Guard = CheckGuards(map, raster, out m_GuardAt);
                guardMs = sw.Elapsed.TotalMilliseconds - solveMs - rasterMs;
            }
            else
                m_Guard = GradeGuardKind.OffMap;
            m_Path = path;
            m_Prof = prof;
            m_Raster = raster;
            BuildDraw(path, prof, p);
            sw.Stop();
            LastPlanText = GradeInput.Describe(path, prof, raster, p);
            Say(string.Format(CultureInfo.InvariantCulture,
                "Grade [plan] #{0} ({1}): {2} point(s), {3} — {4}; ends ground {5:F1} -> {6:F1}; max cut {7:F1} m, max fill {8:F1} m on the " +
                "centerline; {9}; guard {10}{11}; {12} ({13:F2} ms: solve {14:F2}, raster {15:F1}, guards {16:F1})",
                m_Edit.Revision, why, m_Edit.Count, prof.Feasible ? "feasible" : $"INFEASIBLE (shortfall {prof.ShortfallM:F0} m)",
                LastPlanText, ground[0], ground[path.Count - 1], prof.MaxCutM, prof.MaxFillM,
                raster != null ? raster.Describe() : "no raster", m_Guard,
                m_Guard != GradeGuardKind.None ? $" at ({m_GuardAt.x:F0}, {m_GuardAt.y:F0})" : string.Empty, p.Describe(),
                sw.Elapsed.TotalMilliseconds, solveMs, rasterMs, guardMs));
        }

        private static List<float2> ToList(IReadOnlyList<float2> pts)
        {
            var l = new List<float2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
                l.Add(pts[i]);
            return l;
        }

        // ── guards (data from the world, rule in GradeGuards) ──────────────────────────────────

        private struct BoundsIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>, IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 m_Bounds;
            public BoundsMask m_Mask;   // 0 = any
            public NativeList<Entity> m_Results;

            public bool Intersect(QuadTreeBoundsXZ bounds) =>
                (m_Mask == 0 || (bounds.m_Mask & m_Mask) != 0) && MathUtils.Intersect(bounds.m_Bounds.xz, m_Bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (Intersect(bounds))
                    m_Results.Add(entity);
            }
        }

        private void SearchObjects(Bounds2 area, BoundsMask mask, NativeList<Entity> results)
        {
            // Synchronous read (the glade tool's CensusTrees idiom, SearchSystem.cs:421-425).
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = m_ObjectSearch.GetStaticSearchTree(readOnly: true, out JobHandle deps);
            deps.Complete();
            var it = new BoundsIterator { m_Bounds = area, m_Mask = mask, m_Results = results };
            tree.Iterate(ref it);
        }

        private GradeGuardKind CheckGuards(GradeMap map, GradeRaster r, out float2 where)
        {
            m_Obstacles.Clear();
            Bounds2 area = HeightmapWriter.WorldBounds(map, new int4(r.X0, r.Z0, r.W, r.H));

            // Lots.
            var found = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                SearchObjects(area, 0, found);
                for (int k = 0; k < found.Length; k++)
                {
                    Entity e = found[k];
                    if (EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e))
                        continue;
                    if (!EntityManager.HasComponent<Game.Buildings.Building>(e) && !EntityManager.HasComponent<Game.Buildings.Extension>(e))
                        continue;
                    if (!EntityManager.TryGetComponent(e, out PrefabRef pr) || !EntityManager.TryGetComponent(pr.m_Prefab, out BuildingData bd) ||
                        !EntityManager.TryGetComponent(e, out Game.Objects.Transform tr))
                        continue;
                    float3 right = math.mul(tr.m_Rotation, new float3(1f, 0f, 0f));
                    float3 fwd = math.mul(tr.m_Rotation, new float3(0f, 0f, 1f));
                    m_Obstacles.Lots.Add(new GradeObstacles.Lot
                    {
                        Center = tr.m_Position.xz,
                        Right = math.normalizesafe(right.xz, new float2(1f, 0f)),
                        Forward = math.normalizesafe(fwd.xz, new float2(0f, 1f)),
                        Half = (float2)bd.m_LotSize * 4f,   // BuildingUtils.CalculateCorners: lot cells of 8 m, centered
                    });
                }
            }
            finally
            {
                found.Dispose();
            }

            // Lift lines.
            using (var edges = m_NetEdgeQuery.ToEntityArray(Allocator.Temp))
            using (var prefabs = m_NetEdgeQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int k = 0; k < edges.Length; k++)
                {
                    if (!m_Catalog.LiftLineTypes.ContainsKey(prefabs[k].m_Prefab))
                        continue;
                    Bezier4x3 c = EntityManager.GetComponentData<Game.Net.Curve>(edges[k]).m_Bezier;
                    m_Obstacles.Lines.Add(new GradeObstacles.Line { A = c.a.xz, B = c.d.xz, Clearance = LiftClearanceM });
                }
            }

            // Water on the texels the grade touches.
            WaterSurfaceData<SurfaceWater> water = m_WaterSystem.GetSurfaceData(out JobHandle wdeps);
            wdeps.Complete();
            if (water.depths.IsCreated)
            {
                for (int j = 0; j < r.H; j++)
                {
                    for (int i = 0; i < r.W; i++)
                    {
                        if (r.Zone[j * r.W + i] == 0)
                            continue;
                        float2 c = map.TexelCenter(r.X0 + i, r.Z0 + j);
                        if (WaterUtils.SampleDepth(ref water, new float3(c.x, 0f, c.y)) > WetDepthM)
                            m_Obstacles.WetTexels.Add(new int2(r.X0 + i, r.Z0 + j));
                    }
                }
            }
            return GradeGuards.Check(map, r, m_Obstacles, out where);
        }

        // ── apply / undo ────────────────────────────────────────────────────────────────────────

        private void ApplyFromTool()
        {
            if (m_Path == null || m_Prof == null)
            {
                Say("Grade [apply]: no plan", "Click at least two points first");
                return;
            }
            if (!m_Prof.Feasible)
            {
                Say($"Grade [apply]: REFUSED — too steep for the {m_Params.MaxGrade * 100f:F0} % limit (max {m_Prof.MaxGradeAbs * 100f:F0} %, " +
                    $"shortfall {m_Prof.ShortfallM:F0} m)",
                    $"Too steep: the track needs {math.ceil(m_Prof.ShortfallM):F0} m more length (add a switchback)");
                return;
            }
            if (ApplyCore("tool", m_Path, m_Prof, m_Params))
            {
                m_GradesThisOpening++;
                int points = m_Edit.Count;
                m_Edit.Clear();
                ClearPlan(null);
                Say($"Grade [apply]: done — {points} point(s) cleared; right click undoes this grade", "Graded. Right click: undo");
            }
        }

        /// <summary>Read the base rect, re-rasterize on it, guard, write, clear trees, push undo, bump the run catalog. False with the
        /// reason in the notice when refused.</summary>
        private bool ApplyCore(string label, GradePath path, GradeProfile prof, GradeParams p)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!m_Writer.TryMap(out GradeMap map, out string gate))
            {
                Say($"Grade [apply:{label}]: REFUSED — heightmap gate: {gate}", "Grading is not available on this map (see the log)");
                return false;
            }
            if (!GradeRaster.Bounds(map, path, prof, p, out int4 rect, out float reach))
            {
                Say($"Grade [apply:{label}]: REFUSED — off the map", GradeGuards.Reason(GradeGuardKind.OffMap));
                return false;
            }
            ushort[] before = m_Writer.Read(rect, label + " pre-image");
            if (before == null)
            {
                Say($"Grade [apply:{label}]: REFUSED — the base heightmap could not be read", "The terrain could not be read (see the log)");
                return false;
            }
            GradeRaster raster = GradeRaster.Build(map, rect, path, prof, p, reach, (i, j) => map.ToHeight(before[(j - rect.y) * rect.z + (i - rect.x)]));
            GradeGuardKind guard = CheckGuards(map, raster, out float2 at);
            if (guard != GradeGuardKind.None)
            {
                m_Guard = guard;
                m_GuardAt = at;
                Say($"Grade [apply:{label}]: REFUSED — guard {guard} at ({at.x:F0}, {at.y:F0})", GradeGuards.Reason(guard));
                return false;
            }
            var after = new ushort[before.Length];
            int changed = 0;
            for (int t = 0; t < after.Length; t++)
            {
                after[t] = raster.Zone[t] != 0 && raster.After[t] != raster.Before[t] ? map.ToU16(raster.After[t]) : before[t];
                if (after[t] != before[t])
                    changed++;
            }
            if (changed == 0)
            {
                Say($"Grade [apply:{label}]: nothing to change — the ground already fits", "The ground already fits this track");
                return false;
            }
            bool ok = m_Writer.Write(map, rect, after, label, out int mismatches);
            if (!ok)
            {
                if (mismatches > 0)
                {
                    m_Writer.Write(map, rect, before, label + " restore after a failed write", out int _);
                    Say($"Grade [apply:{label}]: FAILED — the write did not land ({mismatches} texel(s)); the pre-image was written back",
                        "The terrain write failed (see the log)");
                    return false;
                }
                Mod.Log.Warn($"Grade [apply:{label}]: the verify readback failed; the write is assumed to have landed (undo kept)");
            }
            int trees = ClearTrees(path, p);
            int dropped = m_Undo.Push(new GradeUndoEntry { X0 = rect.x, Z0 = rect.y, W = rect.z, H = rect.w, Before = before, After = after, Label = label });
            m_RunCatalog?.NoteTerrainChanged("cat-track grade (" + label + ")");
            sw.Stop();
            LastApplyText = string.Format(CultureInfo.InvariantCulture, "{0}: {1} texels, cut {2:F0} m³, fill {3:F0} m³, {4} tree(s), {5:F0} ms",
                label, changed, raster.CutM3, raster.FillM3, trees, sw.Elapsed.TotalMilliseconds);
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "Grade [apply:{0}]: GRADED — {1:F0} m track, {2}; {3} texel(s) written; {4} tree(s) on the flat top removed; undo stack {5} " +
                "({6:F1} MB{7}); run catalog re-rate scheduled; {8:F1} ms total; {9}", label, path.LengthM, raster.Describe(), changed, trees,
                m_Undo.Count, m_Undo.Bytes / 1048576f, dropped > 0 ? $", {dropped} oldest dropped" : string.Empty, sw.Elapsed.TotalMilliseconds,
                p.Describe()));
            return true;
        }

        /// <summary>Delete the free-standing trees on the flat top (+ TreeMarginM), the way a road clears its lane: plain Deleted with
        /// Applied / Created / Updated stripped (the SkiGladeCleanupSystem idiom: CleanUpSystem destroys it at frame end). Through
        /// EntityManager on the main thread, NOT ToolOutputBarrier: that barrier is a SafeCommandBufferSystem whose CreateCommandBuffer
        /// throws outside its window (Game/SafeCommandBufferSystem.cs:14-21, disallowed from its own OnUpdate), and the probe runs from
        /// a dev-tab callback.</summary>
        private int ClearTrees(GradePath path, GradeParams p)
        {
            float clear = 0.5f * p.TopWidthM + TreeMarginM;
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            for (int i = 0; i < path.Count; i++)
            {
                mn = math.min(mn, path.Xz[i]);
                mx = math.max(mx, path.Xz[i]);
            }
            var found = new NativeList<Entity>(64, Allocator.Temp);
            var doomed = new List<Entity>();
            try
            {
                SearchObjects(new Bounds2(mn - clear, mx + clear), BoundsMask.IsTree, found);
                for (int k = 0; k < found.Length; k++)
                {
                    Entity e = found[k];
                    if (!EntityManager.HasComponent<Game.Objects.Tree>(e) || EntityManager.HasComponent<Owner>(e) ||
                        EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e) ||
                        !EntityManager.TryGetComponent(e, out Game.Objects.Transform tr))
                        continue;
                    if (path.Nearest(tr.m_Position.xz, out float _, out int _) <= clear)
                        doomed.Add(e);
                }
            }
            finally
            {
                found.Dispose();
            }
            foreach (Entity e in doomed)   // structural changes after the search tree read is done
            {
                if (EntityManager.HasComponent<Applied>(e))
                    EntityManager.RemoveComponent<Applied>(e);
                if (EntityManager.HasComponent<Created>(e))
                    EntityManager.RemoveComponent<Created>(e);
                if (EntityManager.HasComponent<Updated>(e))
                    EntityManager.RemoveComponent<Updated>(e);
                EntityManager.AddComponent<Deleted>(e);
            }
            return doomed.Count;
        }

        /// <summary>Undo the newest grade of the session (right click in the tool; the dev tab). False when nothing to undo or refused.</summary>
        internal bool UndoLastGrade(string why)
        {
            GradeUndoEntry e = m_Undo.Peek();
            if (e == null)
            {
                Say($"Grade [undo]: nothing to undo ({why})", "No grade to undo");
                return false;
            }
            if (!m_Writer.TryMap(out GradeMap map, out string gate))
            {
                Say($"Grade [undo]: REFUSED — heightmap gate: {gate}", "Grading is not available on this map (see the log)");
                return false;
            }
            var rect = new int4(e.X0, e.Z0, e.W, e.H);
            if (rect.x + rect.z > map.Res.x || rect.y + rect.w > map.Res.y)
            {
                m_Undo.Pop();
                Say("Grade [undo]: REFUSED — the map changed size since the grade; entry dropped", "That grade can no longer be undone");
                return false;
            }
            ushort[] current = m_Writer.Read(rect, "undo " + e.Label);
            if (current == null)
            {
                Say("Grade [undo]: REFUSED — the base heightmap could not be read", "The terrain could not be read (see the log)");
                return false;
            }
            ushort[] restored = GradeUndo.Restore(e, current, out int kept);
            m_Writer.Write(map, rect, restored, "undo " + e.Label, out int mismatches);
            m_Undo.Pop();
            m_RunCatalog?.NoteTerrainChanged("cat-track grade undone");
            Say($"Grade [undo]: UNDONE ({why}) — {e.Label} grade at {HeightmapWriter.Rect(rect)}; {kept} texel(s) changed since by something " +
                $"else were kept; verify {(mismatches == 0 ? "OK" : mismatches + " mismatch(es)")}; removed trees do not come back; " +
                $"{m_Undo.Count} grade(s) left to undo", "Grade undone");
            return true;
        }

        // ── the probe (T139a, dev tab) ───────────────────────────────────────────────────────────

        /// <summary>Grade a fixed ProbeLengthM strip in front of the camera through the full apply path (constant grade, the current
        /// width and side slopes, the guards on; steeper than the limit is allowed here). Then TickWatch checks the CPU heights after
        /// 30 frames and the water sim after 30 / 120 frames. One line each.</summary>
        internal void Probe()
        {
            if (!m_Armed || m_Disabled)
            {
                ProbeText = "refused: not armed / disabled";
                Mod.Log.Info($"Grade [probe]: {ProbeText}");
                return;
            }
            try
            {
                var cam = m_Camera.activeCameraController;
                if (cam == null)
                {
                    ProbeText = "refused: no camera controller";
                    Mod.Log.Info($"Grade [probe]: {ProbeText}");
                    return;
                }
                UnityEngine.Vector3 pv = cam.pivot;
                float2 dir = math.normalizesafe(m_Camera.direction.xz, new float2(0f, 1f));
                float2 a = new float2(pv.x, pv.z) + dir * 5f, b = a + dir * ProbeLengthM;
                GradeParams p = ReadParams().Clone();
                p.Mode = GradeProfileMode.ConstantGrade;
                TerrainHeightData hd = m_TerrainSystem.GetHeightData();
                if (!hd.isCreated)
                {
                    ProbeText = "refused: terrain not ready";
                    Mod.Log.Info($"Grade [probe]: {ProbeText}");
                    return;
                }
                GradePath path = GradePath.Build(new List<float2> { a, b }, p.SampleStepM);
                var ground = new float[path.Count];
                for (int i = 0; i < path.Count; i++)
                    ground[i] = TerrainUtils.SampleHeight(ref hd, new float3(path.Xz[i].x, 0f, path.Xz[i].y));
                GradeProfile prof = GradeProfile.Solve(path, ground, p);
                // A visible change on any slope: a constant ramp 1.5 m below the ground at the middle (a cut), ends on the ground.
                for (int i = 0; i < path.Count; i++)
                    prof.Y[i] -= 1.5f * math.sin(math.PI * path.S[i] / path.LengthM);
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "Grade [probe]: START — {0:F0} m strip from ({1:F0}, {2:F0}) toward ({3:F0}, {4:F0}), ground {5:F1} -> {6:F1}, a ramp with a 1.5 m " +
                    "cut at its middle; WaterSimSpeed {7}; {8}", path.LengthM, a.x, a.y, b.x, b.y, ground[0], ground[path.Count - 1],
                    m_Writer.WaterSimSpeed, p.Describe()));
                if (!ApplyCore("probe", path, prof, p))
                {
                    ProbeText = "refused: " + (Notice ?? "see the log");
                    return;
                }
                m_ProbeXz = path.Xz;
                m_ProbeY = prof.Y;
                m_ProbeCheckFrame = m_Frame + 30;
                m_ProbeWaterFrame = m_Frame + 120;
                ProbeText = "written: " + m_Writer.LastLine + "; CPU check in 30 frames";
                Mod.Log.Info("Grade [probe]: WRITTEN — now check: the terrain dips along the strip, trees / props on it follow the ground, " +
                             "water nearby resumes; then save, load, and look again (the dip must stay). Undo: dev tab \"Undo last grade\"");
            }
            catch (Exception e)
            {
                Fail(e, "Probe");
            }
        }

        /// <summary>Every frame (CatTrackGradeHotkeySystem, UIUpdate): the probe's follow-up checks. Field reads unless a check is due.</summary>
        internal void TickWatch()
        {
            if (!IsActive)
                m_Frame++;   // the tool's own frame counter only advances while it runs
            if (m_ProbeCheckFrame >= 0 && m_Frame >= m_ProbeCheckFrame)
            {
                m_ProbeCheckFrame = -1;
                float worst = 0f;
                int n = 0;
                for (int i = 0; m_ProbeXz != null && i < m_ProbeXz.Length; i += 2)
                {
                    float h = m_Writer.CpuHeight(m_ProbeXz[i]);
                    if (float.IsNaN(h))
                        continue;
                    worst = math.max(worst, math.abs(h - m_ProbeY[i]));
                    n++;
                }
                ProbeText = string.Format(CultureInfo.InvariantCulture, "CPU heights vs written profile: max |diff| {0:F2} m over {1} points " +
                    "(<= 0.5 m expected: texel sampling + any net / lot overlay); WaterSimSpeed {2}", worst, n, m_Writer.WaterSimSpeed);
                if (worst <= 0.5f)
                    Mod.Log.Info("Grade [probe]: CHECK OK — " + ProbeText);
                else
                    Mod.Log.Warn("Grade [probe]: CHECK — " + ProbeText + " (the cascade / CPU heights did not follow the base map?)");
            }
            if (m_ProbeWaterFrame >= 0 && m_Frame >= m_ProbeWaterFrame)
            {
                m_ProbeWaterFrame = -1;
                int speed = m_Writer.WaterSimSpeed;
                Mod.Log.Info($"Grade [probe]: water 120 frames after the write: WaterSimSpeed {speed} " +
                             $"({(speed > 0 ? "running again" : "STILL PAUSED (0): game paused? else please send the log")})");
            }
        }

        // ── preview ─────────────────────────────────────────────────────────────────────────────

        private void BuildDraw(GradePath path, GradeProfile prof, GradeParams p)
        {
            float hw = 0.5f * p.TopWidthM;
            const int Run = 4;   // centerline in runs of 4 samples (~8 m) of one grade class
            for (int i = 0; i < path.Count - 1; i += Run)
            {
                int j = math.min(i + Run, path.Count - 1);
                int cls = 0;
                for (int k = i; k < j; k++)
                    cls = math.max(cls, GradeProfile.GradeClass(math.abs(prof.SegGrade[k]), p.MaxGrade));
                Add(P(path, prof, i, 0f, hw), P(path, prof, j, 0f, hw), GradeColors[cls], 1.2f);
                Add(P(path, prof, i, hw, hw), P(path, prof, j, hw, hw), EdgeColor, 0.35f);
                Add(P(path, prof, i, -hw, hw), P(path, prof, j, -hw, hw), EdgeColor, 0.35f);
            }
            // Cut / fill ticks at both shoulders every 3 samples (~6 m), length by depth.
            for (int i = 0; i < path.Count; i += 3)
            {
                float depth = prof.Ground[i] - prof.Y[i];
                if (math.abs(depth) < 0.25f)
                    continue;
                bool cut = depth > 0f;
                float len = math.clamp(math.abs(depth) * (cut ? p.CutRun : p.FillRun), 1f, 12f);
                UnityEngine.Color c = cut ? CutColor : FillColor;
                Add(P(path, prof, i, hw, hw), P(path, prof, i, hw + len, hw), c, 0.6f);
                Add(P(path, prof, i, -hw, hw), P(path, prof, i, -hw - len, hw), c, 0.6f);
            }
        }

        /// <summary>Point at sample i, offset sideways by <paramref name="lateral"/> (left positive), at the sample's ground height.</summary>
        private static float3 P(GradePath path, GradeProfile prof, int i, float lateral, float hw)
        {
            int a = math.max(0, i - 1), b = math.min(path.Count - 1, i + 1);
            float2 t = math.normalizesafe(path.Xz[b] - path.Xz[a], new float2(1f, 0f));
            float2 left = new float2(-t.y, t.x);
            float2 xz = path.Xz[i] + left * lateral;
            return new float3(xz.x, prof.Ground[i], xz.y);
        }

        private void Add(float3 a, float3 b, UnityEngine.Color c, float w)
        {
            m_DrawSeg.Add(new Line3.Segment(a, b));
            m_DrawColor.Add(c);
            m_DrawWidth.Add(w);
        }

        private void Draw()
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            var projected = OverlayRenderSystem.StyleFlags.Projected;
            var round = new float2(1f, 1f);
            var ring = new float2(0f, 1f);
            if (!Dragging)
            {
                for (int i = 0; i < m_DrawSeg.Count; i++)
                    buffer.DrawLine(m_DrawColor[i], m_DrawColor[i], 0f, projected, m_DrawSeg[i], m_DrawWidth[i], round);
            }
            // Points (and, while dragging or with one point, the chords through them).
            IReadOnlyList<float2> pts = m_Edit.Points;
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            for (int i = 0; i < pts.Count; i++)
            {
                float2 xz = i == m_DragIndex ? m_DragTo : pts[i];
                bool hot = i == m_HoverPoint || i == m_DragIndex;
                UnityEngine.Color c = hot ? HoverColor : PointColor;
                buffer.DrawCircle(c, c, 0f, projected, ring, Ground(ref hd, xz), hot ? 8f : 6f);
                if ((Dragging || pts.Count == 1) && i > 0)
                {
                    float2 prev = i - 1 == m_DragIndex ? m_DragTo : pts[i - 1];
                    buffer.DrawLine(HoverColor, HoverColor, 0f, projected, new Line3.Segment(Ground(ref hd, prev), Ground(ref hd, xz)), 0.8f, round);
                }
            }
            if (pts.Count >= 1 && m_HasHit && !Dragging && m_HoverPoint < 0)
            {
                float2 last = pts[pts.Count - 1];
                if (pts.Count == 1)
                    buffer.DrawLine(EdgeColor, EdgeColor, 0f, projected, new Line3.Segment(Ground(ref hd, last), m_Hit), 0.6f, round);
                buffer.DrawCircle(HoverColor, HoverColor, 0f, projected, ring, m_Hit, 3f);
            }
            if (m_Guard != GradeGuardKind.None && m_Guard != GradeGuardKind.OffMap)
                buffer.DrawCircle(GuardColor, GuardColor, 0f, projected, ring, Ground(ref hd, m_GuardAt), 10f);
        }

        private static float3 Ground(ref TerrainHeightData hd, float2 xz)
        {
            var p = new float3(xz.x, 0f, xz.y);
            if (hd.isCreated)
                p.y = TerrainUtils.SampleHeight(ref hd, p);
            return p;
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────────

        /// <summary>The grading settings (Options ▸ Modular Ski Resort ▸ Runs), clamped; defaults when settings are absent.</summary>
        internal static GradeParams ReadParams()
        {
            var s = Mod.Settings;
            var p = new GradeParams();
            if (s == null)
                return p;
            p.MaxGrade = math.clamp(s.CatTrackGradeMaxPct, 5, 25) / 100f;
            p.TopWidthM = math.clamp(s.CatTrackGradeWidthM, 4, 16);
            p.CutRun = math.clamp(s.CatTrackGradeCutRun, 1f, 3f);
            p.FillRun = math.clamp(s.CatTrackGradeFillRun, 1f, 4f);
            p.Mode = s.CatTrackGradeConstant ? GradeProfileMode.ConstantGrade : GradeProfileMode.FollowGround;
            return p;
        }

        private static bool Same(GradeParams a, GradeParams b) =>
            a.MaxGrade == b.MaxGrade && a.TopWidthM == b.TopWidthM && a.CutRun == b.CutRun && a.FillRun == b.FillRun && a.Mode == b.Mode;

        private void ClearPlan(string why)
        {
            if (why != null)
                m_Edit.Clear();
            m_Path = null;
            m_Prof = null;
            m_Raster = null;
            m_Guard = GradeGuardKind.None;
            m_Dirty = m_ApplyRequested = false;
            m_HoverPoint = m_DragIndex = -1;
            m_DrawSeg.Clear();
            m_DrawColor.Clear();
            m_DrawWidth.Clear();
        }

        private void Say(string line, string notice = null)
        {
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
                Mod.Log.Error($"!!! {nameof(CatTrackGradeToolSystem)} EXCEPTION in {where} — the grading tool is disabled until the next game " +
                              $"load. Please report this stack:\n{e}");
            }
        }
    }
}
