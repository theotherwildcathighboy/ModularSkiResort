// The cat track tool (one action since 2026-10-24: the hub's Cat track tile, Ctrl+Shift+K). Click points up the mountain; the tool
// solves a grade-limited track through them (CatTrackGrade.cs, pure, tested in GradeBatch) and previews it as an overlay. On Enter it
// emits the cat-track run (SkiRunCatTrackA, outline = the graded flat top, GradeRibbon) as an area creation definition, waits for the
// game's Temp area and validation, then in ONE frame writes the corridor into the game's BASE heightmap (HeightmapWriter: terrain
// the player keeps, stored in the save by the game itself) and applies the run. A refusal by the game grades nothing.
// Skeleton is LiftLineToolSystem's (a prefab-less ToolBaseSystem, emit -> Temp -> Apply). Solve on change, not per frame.
// Logs (silence impossible): ARMED / NOT ARMED per load; one line per activation, edit, solve, apply, undo, refusal; an exception
// disables the tool.
// Engine notes: EngineNotes/CatTrackGradeToolSystem.md

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
        private const int TempTimeoutFrames = 30;   // frames to wait for the run definition's Temp area (LiftLineToolSystem's value)

        /// <summary>Edit: points and preview. Emitted: the run definition is out, waiting for its Temp area and the game's validation.
        /// Applying: the frame after Apply, checks that the run became permanent.</summary>
        private enum RunPhase : byte { Edit, Emitted, Applying }

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
        internal GradeStep Step => GradeInput.StepOf(m_Edit.Count, m_Phase != RunPhase.Edit);
        internal bool Dragging => m_DragIndex >= 0;
        internal bool CanUndoGrade => m_GradesThisOpening > 0 && m_Undo.Count > 0;
        internal string Notice => m_Notice != null && m_Frame - m_NoticeFrame < NoticeFrames ? m_Notice : null;
        internal string PlanLine => m_Path != null ? GradeInput.Describe(m_Path, m_Prof, m_Raster, m_Params) : null;
        internal bool PlanOk => m_Prof != null && m_Prof.Feasible && m_Guard == GradeGuardKind.None && !m_CrossingPending;
        internal string GuardLine => m_Guard != GradeGuardKind.None ? GradeGuards.Reason(m_Guard) : null;
        internal string BankLine => m_Raster != null && m_Raster.SteepBankTexels > 0
            ? string.Format(CultureInfo.InvariantCulture, "Steep bank: the hillside is steeper than the side slope (up to {0:F0} m)", m_Raster.MaxBankStepM)
            : null;
        internal string LastPlanText { get; private set; } = "-";
        internal string LastApplyText { get; private set; } = "-";

        private OverlayRenderSystem m_Overlay;
        private TerrainSystem m_TerrainSystem;
        private WaterSystem m_WaterSystem;
        private SkiPrefabCatalogSystem m_Catalog;
        private SkiRunCatalogSystem m_RunCatalog;
        private Game.Objects.SearchSystem m_ObjectSearch;
        private CameraUpdateSystem m_Camera;
        private ToolOutputBarrier m_Barrier;
        private EntityQuery m_NetEdgeQuery, m_DefinitionQuery, m_TempAreaQuery, m_RunAreaQuery;
        private SkiRunOwnershipSystem m_Ownership;
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
        private float2[] m_Ring;   // the run outline of the current plan (GradeRibbon), null when it fails
        private GradeParams m_PlanParams;   // the params the plan was built with (its inside allowance, DECISIONS 420)
        // F4: the allowance step of this plan revision (one per frame), its cached Before bands and the per-step log
        private int m_AllowanceStep, m_AllowanceRev = -1;
        private GradeParams m_AllowanceParams;
        private sbyte[] m_BandsBefore;
        private bool m_CrossingPending;
        private readonly StringBuilder m_StepText = new StringBuilder();
        private string m_BandText;          // the last crossed-run band comparison (log)
        private GradeGuardKind m_Guard;
        private float2 m_GuardAt;
        private bool m_Dirty, m_ApplyRequested;
        private RunPhase m_Phase;
        private int m_EmitFrame;
        private Entity m_RunPrefab, m_AppliedRun;
        private JobHandle m_Deps;
        private int m_GradesThisOpening;
        private float3 m_Hit;
        private bool m_HasHit;
        private int m_HoverPoint = -1, m_DragIndex = -1;
        private float2 m_DragTo;
        private string m_Notice;
        private int m_NoticeFrame;

        private readonly List<Line3.Segment> m_DrawSeg = new List<Line3.Segment>();
        private readonly List<UnityEngine.Color> m_DrawColor = new List<UnityEngine.Color>();
        private readonly List<float> m_DrawWidth = new List<float>();

        // Probe follow-up (TickWatch, called every frame by CatTrackGradeHotkeySystem)
        private int m_ProbeCheckFrame = -1, m_ProbeWaterFrame = -1;
        private float2[] m_ProbeXz;
        private float[] m_ProbeY;
        internal string ProbeText { get; private set; } = "not run";


        protected override void OnCreate()
        {
            base.OnCreate();
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
            m_Barrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_RunAreaQuery = GetEntityQuery(new EntityQueryDesc
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
            m_RunPrefab = m_Catalog.EntityOf(SkiCatalog.Names.RunCatTrack);
            Mod.Log.Info($"Grade [tool]: ARMED — '{toolID}' (T139, one action since 2026-10-24): click points up the mountain, drag a " +
                         "point to move it, right click steps back one action (with no point: undoes the last track built since the tool " +
                         "opened, terrain and run; then closes), Enter grades the corridor AND builds the cat-track run on its flat top " +
                         $"(run prefab {SkiCatalog.Names.RunCatTrack} {(m_RunPrefab != Entity.Null ? "FOUND" : "MISSING: Enter refuses")}), " +
                         $"Esc closes. {ReadParams().Describe()}; guards: lots, lift lines ({LiftClearanceM:F0} m), water " +
                         $"(> {WetDepthM:F1} m), run outline clearance {GradeRibbon.MinClearanceM:F1} m, crossed runs keep their ground (fade " +
                         $"{ReadParams().CrossBlendM:F0} m) and must be crossed under the max grade; trees on the flat top are " +
                         $"removed; grading free, the run pays its fee at bind; undo for this session (last {GradeUndo.MaxEntries})");
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
                    Mod.Log.Info($"Grade [tool]: closed with {m_Edit.Count} point(s){(m_Phase == RunPhase.Emitted ? " while the game checked the run" : string.Empty)} " +
                                 "— dropped (nothing graded, no run built)");
                if (m_Phase == RunPhase.Emitted)
                {
                    // ToolSystem reads the NEW tool's applyMode on a switch (ToolSystem.cs:147-156), so nothing clears our preview Temp: delete it
                    // here with plain Deleted, as ToolClearSystem does (ToolClearSystem.cs:101).
                    Entity temp = FindTempRun();
                    if (temp != Entity.Null)
                        EntityManager.AddComponent<Deleted>(temp);
                    Mod.Log.Info($"Grade [run]: closed while the game checked the run — the preview run {(temp != Entity.Null ? Describe(temp) + " deleted" : "was not there yet")}, " +
                                 "nothing graded");
                }
                else if (m_Phase == RunPhase.Applying)
                {
                    // Closed in the one frame after Apply: FinishApplied will not run, so say what is known (applied this frame, the run is permanent
                    // once ApplyAreasSystem has run; the terrain is graded and undo is on the stack).
                    Mod.Log.Info($"Grade [run]: closed in the frame after Apply — run {Describe(m_AppliedRun)} applied with the grade (exists " +
                                 $"{EntityManager.Exists(m_AppliedRun)}, Temp {EntityManager.Exists(m_AppliedRun) && EntityManager.HasComponent<Temp>(m_AppliedRun)})");
                }
                ClearPlan("stopped");
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
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;   // a click on an existing cat track starts a new track there (branching)
        }

        protected override bool GetAllowApply()
        {
            return base.GetAllowApply() && m_Phase == RunPhase.Emitted;   // ToolBaseSystem.cs:533-540: no Error on any Temp
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
            if (m_Disabled || !m_Armed)
            {
                Mod.Log.Info($"Grade [tool]: activation refused ({why}) — {(m_Disabled ? "disabled after an exception until the next load" : "not armed (not in a game)")}");
                return false;
            }
            if (IsActive)
                return true;
            m_ToolSystem.activeTool = this;
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

        public void RequestApply()
        {
            if (!IsActive || m_Phase != RunPhase.Edit)
                return;   // a build in flight: a second Enter would queue another one
            if (m_Edit.Count < 2)
            {
                Say("Grade [apply]: nothing to grade — click at least two points", "Click at least two points first");
                return;
            }
            m_ApplyRequested = true;
        }


        private void UpdateCore()
        {
            m_Frame++;
            applyMode = ApplyMode.None;
            bool live = InputManager.instance.controlOverWorld;
            m_HasHit = live && GetRaycastResult(out ControlPoint cp, out bool _) && TakeHit(cp);
            switch (m_Phase)
            {
                case RunPhase.Emitted:
                    SetActions(false, false, false);
                    UpdateEmitted();
                    Draw();
                    return;
                case RunPhase.Applying:
                    SetActions(false, false, false);
                    FinishApplied();
                    return;
            }
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
                if (m_Dirty || m_CrossingPending)
                    Solve("before apply", settle: true);
                BuildFromTool();
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


        private void Solve(string why, bool settle = false)
        {
            m_Dirty = false;
            m_BandText = null;
            m_Path = null;
            m_Prof = null;
            m_Raster = null;
            m_Ring = null;
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
            // The rect and the obstacles come BEFORE the profile: in a crossing's fade band the profile is held to the ground, inside a
            // crossed run within InsideMaxM of it (GradeProfile.Solve pinTol), so the plan meets each crossed run at its own ground.
            bool haveRect = GradeRaster.Bounds(map, path, null, p, out int4 rect, out float reach);
            bool tooLarge = haveRect && GradeRaster.TooLarge(rect);
            if (haveRect && !tooLarge)
                CollectObstacles(map, rect);
            GradeProfile prof = tooLarge || !haveRect ? GradeProfile.Solve(path, ground, p) : null;
            double solveMs = sw.Elapsed.TotalMilliseconds;

            GradeRaster raster = null;
            GradeParams pa = p;
            float allowance = p.InsideMaxM;
            double rasterMs = 0, guardMs = 0;
            if (haveRect)
            {
                if (tooLarge)
                {
                    m_Guard = GradeGuardKind.TooLarge;
                    m_Path = path;
                    m_Prof = prof;
                    BuildDraw(path, prof, p);
                    sw.Stop();
                    LastPlanText = GradeInput.Describe(path, prof, null, p);
                    Say($"Grade [plan] #{m_Edit.Revision} ({why}): REFUSED — the rect is {rect.z} x {rect.w} texels, over the " +
                        $"{GradeRaster.MaxTexels} texel limit; no raster built ({sw.Elapsed.TotalMilliseconds:F2} ms)");
                    return;
                }
                TerrainHeightData hdBase = hd;
                Func<int, int, float> baseHeight = (i, j) =>
                {
                    float2 c = map.TexelCenter(i, j);
                    return TerrainUtils.SampleHeight(ref hdBase, new float3(c.x, 0f, c.y));
                };
                // Inside crossed runs the allowance steps down (2 m, 1 m, 0.5 m, 0) until every crossed run keeps its rated band
                // (DECISIONS 420); none keeps it = guard CrossedRunBand. No crossing: one pass. F4 (round-2 critique): ONE step per frame
                // while editing (the plan re-solves the next frame with the next step, m_AllowanceStep), all steps at once only when Enter
                // needs a settled plan (settle); the Before ratings are cached per plan revision (they do not depend on the allowance).
                bool[] entered = GradeRaster.EnteredOutlines(path, m_Obstacles.Runs);
                bool crossing = false;
                for (int k = 0; entered != null && k < entered.Length; k++)
                    crossing |= entered[k];
                if (m_AllowanceRev != m_Edit.Revision || !ReferenceEquals(m_AllowanceParams, p))
                {
                    m_AllowanceRev = m_Edit.Revision;
                    m_AllowanceParams = p;
                    m_AllowanceStep = 0;
                    m_BandsBefore = null;
                    m_StepText.Clear();
                }
                float[] steps = GradeParams.InsideAllowanceSteps;
                bool BuildAt(float a)
                {
                    double t0 = sw.Elapsed.TotalMilliseconds;
                    pa = p.Clone();
                    pa.InsideMaxM = a;
                    prof = GradeProfile.Solve(path, ground, pa, GradeRaster.PinTolerance(path, m_Obstacles.Runs, pa));
                    raster = GradeRaster.Build(map, rect, path, prof, pa, reach, baseHeight, m_Obstacles.Runs);
                    if (!crossing)
                        return true;
                    if (m_BandsBefore == null)
                        m_BandsBefore = RateCrossed(map, raster, entered, false);
                    bool kept = SameBands(m_BandsBefore, RateCrossed(map, raster, entered, true), out m_BandText);
                    m_StepText.Append(m_StepText.Length > 0 ? "; " : string.Empty)
                              .AppendFormat(CultureInfo.InvariantCulture, "{0:F1} m {1} {2:F0} ms", a, kept ? "kept" : "changed", sw.Elapsed.TotalMilliseconds - t0);
                    return kept;
                }
                float chosen;
                m_CrossingPending = false;
                if (settle || !crossing)
                    chosen = GradeRaster.ChooseInsideAllowance(BuildAt);
                else if (BuildAt(steps[m_AllowanceStep]))
                    chosen = steps[m_AllowanceStep];
                else if (m_AllowanceStep + 1 < steps.Length)
                {
                    chosen = steps[m_AllowanceStep];
                    m_AllowanceStep++;
                    m_CrossingPending = true;   // try the next step next frame
                    m_Dirty = true;
                }
                else
                    chosen = -1f;
                allowance = chosen;
                rasterMs = sw.Elapsed.TotalMilliseconds - solveMs;
                m_Guard = CheckGuards(map, raster, out m_GuardAt);
                if (m_Guard == GradeGuardKind.None && CrossingSteep(map, path, raster, pa, out float2 steepAt))
                {
                    m_Guard = GradeGuardKind.CrossingSteep;
                    m_GuardAt = steepAt;
                }
                if (m_Guard == GradeGuardKind.None && chosen < 0f && !m_CrossingPending)
                {
                    m_Guard = GradeGuardKind.CrossedRunBand;
                    m_GuardAt = path.Xz[path.Count / 2];
                }
                guardMs = sw.Elapsed.TotalMilliseconds - solveMs - rasterMs;
            }
            else
                m_Guard = GradeGuardKind.OffMap;
            // The run outline (the flat top as one ring): a ring that touches itself is a guard like the others, so Enter refuses
            // before anything is emitted or graded.
            double ringMs = 0;
            if (m_Guard == GradeGuardKind.None)
            {
                double t0 = sw.Elapsed.TotalMilliseconds;
                m_Ring = GradeRibbon.Build(path, 0.5f * p.TopWidthM, out GradeRibbonFail ringFail, out float2 ringAt);
                ringMs = sw.Elapsed.TotalMilliseconds - t0;
                if (ringFail != GradeRibbonFail.None)
                {
                    m_Ring = null;
                    m_Guard = GradeGuardKind.RunShape;
                    m_GuardAt = ringAt;
                }
            }
            m_Path = path;
            m_Prof = prof;
            m_Raster = raster;
            m_PlanParams = pa;
            BuildDraw(path, prof, p);
            sw.Stop();
            LastPlanText = GradeInput.Describe(path, prof, raster, p);
            if (raster != null && (raster.InsideTexels > 0 || allowance < p.InsideMaxM || m_StepText.Length > 0))
                Say(string.Format(CultureInfo.InvariantCulture, "Grade [crossing] #{0}: inside allowance {1} (of {2:F1} m){3}; {4}; steps: {5}",
                    m_Edit.Revision, allowance < 0f ? "none keeps the bands" : allowance.ToString("F1", CultureInfo.InvariantCulture) + " m",
                    p.InsideMaxM, m_CrossingPending ? ", band changed: the next step runs next frame" : string.Empty, m_BandText ?? "bands not rated",
                    m_StepText.Length > 0 ? m_StepText.ToString() : "none"));
            Say(string.Format(CultureInfo.InvariantCulture,
                "Grade [plan] #{0} ({1}): {2} point(s), {3} — {4}; ends ground {5:F1} -> {6:F1}; max cut {7:F1} m, max fill {8:F1} m on the " +
                "centerline; {9}; run outline {17}; guard {10}{11}; {12} ({13:F2} ms: solve {14:F2}, raster {15:F1}, guards {16:F1}, " +
                "outline {18:F1})",
                m_Edit.Revision, why, m_Edit.Count, prof.Feasible ? "feasible" : $"INFEASIBLE (shortfall {prof.ShortfallM:F0} m)",
                LastPlanText, ground[0], ground[path.Count - 1], prof.MaxCutM, prof.MaxFillM,
                raster != null ? raster.Describe() : "no raster", m_Guard,
                m_Guard != GradeGuardKind.None ? $" at ({m_GuardAt.x:F0}, {m_GuardAt.y:F0})" : string.Empty, p.Describe(),
                sw.Elapsed.TotalMilliseconds, solveMs, rasterMs, guardMs,
                m_Ring != null ? m_Ring.Length + " node(s)" : "none", ringMs));
        }

        private static List<float2> ToList(IReadOnlyList<float2> pts)
        {
            var l = new List<float2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
                l.Add(pts[i]);
            return l;
        }


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

        /// <summary>The obstacles of the rect, before the raster: building lots, lift lines, and the outlines of the other runs a cat
        /// track may cross (the raster keeps their ground, GradeRaster.Build keep; GradeParams.CrossBlendM).</summary>
        private void CollectObstacles(GradeMap map, int4 rect)
        {
            m_Obstacles.Clear();
            Bounds2 area = HeightmapWriter.WorldBounds(map, rect);

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

            // Every run of ours but cat tracks (a cat track over a cat track is the game's OverlapExisting, ValidationHelpers.cs:566-582)
            using (var runs = m_RunAreaQuery.ToEntityArray(Allocator.Temp))
            using (var prefabs = m_RunAreaQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int k = 0; k < runs.Length; k++)
                {
                    int kind = m_Ownership.KindOf(prefabs[k].m_Prefab);
                    if (kind < 0 || kind == (int)RunKind.CatTrack)
                        continue;
                    float2[] outline = RunAreaEmit.ReadRing(EntityManager, runs[k], out Bounds2 b);
                    if (outline != null && MathUtils.Intersect(b, area))
                        m_Obstacles.Runs.Add(outline);
                }
            }
        }

        /// <summary>The crossing guard (m_Obstacles.Runs): the FINAL surface along the track steeper than the max grade + 1 pp where a crossed
        /// outline or its fade reaches (GradeRaster.AlongTrackSteepSample). Inside a run that surface is the run's ground plus the up-to-2 m
        /// correction, so it fires only where the correction cannot reach (DECISIONS 420); also the only protection of a constant grade.</summary>
        private bool CrossingSteep(GradeMap map, GradePath path, GradeRaster raster, GradeParams p, out float2 at)
        {
            at = default;
            if (m_Obstacles.Runs.Count == 0)
                return false;
            int i = raster.AlongTrackSteepSample(map, path, m_Obstacles.Runs, p.CrossBlendM, p.MaxGrade);
            if (i < 0)
                return false;
            at = path.Xz[i];
            return true;
        }

        /// <summary>The rated band of each entered crossed run on the raster's Before (or After) terrain, the catalog's own rater
        /// (SkiRunRater, the catalog's origin and texel); -3 for an outline the path does not enter or a rating that did not finish.</summary>
        private sbyte[] RateCrossed(GradeMap map, GradeRaster r, bool[] entered, bool after)
        {
            var bands = new sbyte[m_Obstacles.Runs.Count];
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            TerrainHeightData cpu = hd;
            var sampler = new GradeRaster.Sampler { Map = map, R = r, After = after, Outside = w => TerrainUtils.SampleHeight(ref cpu, w) };
            float texel = 1f / hd.scale.x;                            // as SkiRunCatalogSystem rates (TerrainSystem.cs:2619-2620)
            var origin = new double2(-hd.offset.x, -hd.offset.z);
            for (int k = 0; k < bands.Length; k++)
            {
                bands[k] = -3;
                float2[] poly = m_Obstacles.Runs[k];
                if (entered == null || !entered[k] || poly == null || poly.Length < 3)
                    continue;
                var nodes = new float3[poly.Length];
                for (int i = 0; i < poly.Length; i++)
                    nodes[i] = new float3(poly[i].x, sampler.Sample(new float3(poly[i].x, 0f, poly[i].y)), poly[i].y);
                var result = new SlopeResult();
                RateStatus st = SkiRunRater.Rate(nodes, nodes.Length, origin, ref sampler, texel, 0, new SlopeGrid(), null, result,
                    out int _, out float _, out float _);
                if (st == RateStatus.Rated)
                    bands[k] = (sbyte)result.Band;
            }
            return bands;
        }

        private static bool SameBands(sbyte[] before, sbyte[] after, out string text)
        {
            var sb = new StringBuilder();
            bool same = true;
            for (int k = 0; k < before.Length; k++)
            {
                if (before[k] == -3 || after[k] == -3)
                    continue;
                sb.Append(sb.Length > 0 ? ", " : string.Empty).Append("run ").Append(k).Append(' ').Append((SlopeBand)before[k]);
                if (before[k] != after[k])
                {
                    same = false;
                    sb.Append(" -> ").Append((SlopeBand)after[k]);
                }
                else
                    sb.Append(" kept");
            }
            text = sb.Length > 0 ? "crossed runs: " + sb : "no crossed run rated";
            return same;
        }

        /// <summary>Water on the changed texels, then GradeGuards.Check over everything CollectObstacles found.</summary>
        private GradeGuardKind CheckGuards(GradeMap map, GradeRaster r, out float2 where)
        {
            m_Obstacles.WetTexels.Clear();
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


        /// <summary>Enter: refuse an infeasible or guarded plan, else emit the cat-track run's definition (the plan's ring). The grade
        /// itself waits for the game's verdict on the run (UpdateEmitted).</summary>
        private void BuildFromTool()
        {
            if (m_Path == null || m_Prof == null)
            {
                Say("Grade [apply]: no plan", "Click at least two points first");
                return;
            }
            if (m_Guard != GradeGuardKind.None || m_Ring == null)
            {
                Say($"Grade [apply]: REFUSED — guard {m_Guard} at ({m_GuardAt.x:F0}, {m_GuardAt.y:F0})", GradeGuards.Reason(m_Guard));
                return;
            }
            if (!m_Prof.Feasible)
            {
                Say($"Grade [apply]: REFUSED — too steep for the {m_Params.MaxGrade * 100f:F0} % limit (max {m_Prof.MaxGradeAbs * 100f:F0} %, " +
                    $"shortfall {m_Prof.ShortfallM:F0} m)",
                    $"Too steep: the track needs {math.ceil(m_Prof.ShortfallM):F0} m more length (add a switchback)");
                return;
            }
            if (m_RunPrefab == Entity.Null)
                m_RunPrefab = m_Catalog.EntityOf(SkiCatalog.Names.RunCatTrack);
            if (m_RunPrefab == Entity.Null)
            {
                Say($"Grade [apply]: REFUSED — the run prefab {SkiCatalog.Names.RunCatTrack} is missing this load (asset pack missing?)",
                    KeyRunMissing);
                return;
            }
            EmitRun(m_Ring);
        }

        /// <summary>One area creation definition (RunAreaEmit.Emit, shared with the Quick runs tool).</summary>
        private void EmitRun(float2[] ring)
        {
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            RunAreaEmit.Emit(m_Barrier.CreateCommandBuffer(), m_RunPrefab, ring, ref hd);
            m_Phase = RunPhase.Emitted;
            m_EmitFrame = m_Frame;
            Say($"Grade [run]: definition emitted — {ring.Length} node(s), {m_Path.LengthM:F0} m x {m_Params.TopWidthM:F0} m; waiting for the " +
                "game's Temp area and validation");
        }

        /// <summary>Emitted: wait for our Temp area; the game's errors refuse it (nothing graded); else grade (ApplyCore on the base
        /// map) and Apply in the same frame. ApplyAreasSystem keeps the Temp entity as the permanent run (ApplyAreasSystem.cs:204-208).</summary>
        private void UpdateEmitted()
        {
            Entity temp = FindTempRun();
            if (temp == Entity.Null)
            {
                if (m_Frame - m_EmitFrame > TempTimeoutFrames)
                {
                    Mod.Log.Warn($"Grade [run]: no Temp area {TempTimeoutFrames} frames after the emission — nothing graded, no run built; back " +
                                 "to editing (please send the log)");
                    BackToEdit(KeyRunRefused);
                }
                return;
            }
            if (!GetAllowApply())
            {
                int errors = m_ErrorQuery.IsEmptyIgnoreFilter ? 0 : m_ErrorQuery.CalculateEntityCount();
                Say($"Grade [run]: REFUSED by the game — {errors} error(s) on the run preview ({Describe(temp)}); nothing graded, back to editing");
                BackToEdit(KeyRunRefused);
                return;
            }
            if (!ApplyCore("tool", m_Path, m_Prof, m_PlanParams ?? m_Params, temp))
            {
                Mod.Log.Info("Grade [run]: the grade was refused (see the line above) — the run is not built");
                BackToEdit(null);
                return;
            }
            applyMode = ApplyMode.Apply;   // ToolOutputSystem.cs:22-28 -> ApplyAreasSystem
            m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            m_AppliedRun = temp;
            m_Phase = RunPhase.Applying;
            m_GradesThisOpening++;
            int points = m_Edit.Count;
            m_Edit.Clear();
            ClearPlan(null);
            Say($"Grade [apply]: done — the cat-track run {Describe(temp)} applied with the grade; {points} point(s) cleared; right click " +
                "undoes both", "Cat track built. Right click: undo");
        }

        /// <summary>Applying (the frame after Apply): the run must be permanent now; one line either way.</summary>
        private void FinishApplied()
        {
            Entity run = m_AppliedRun;
            m_AppliedRun = Entity.Null;
            m_Phase = RunPhase.Edit;
            if (EntityManager.Exists(run) && !EntityManager.HasComponent<Temp>(run) && !EntityManager.HasComponent<Deleted>(run))
                Mod.Log.Info($"Grade [run]: BUILT — {Describe(run)} is permanent (it binds to the nearest Ski Area Welcome Sign like a drawn run)");
            else
                Mod.Log.Warn($"Grade [run]: the run {Describe(run)} is not permanent the frame after Apply (exists {EntityManager.Exists(run)}) — " +
                             "the terrain is graded; undo restores it (please send the log)");
        }

        private void BackToEdit(string noticeKey)
        {
            applyMode = ApplyMode.Clear;   // ToolClearSystem deletes our Temp (ToolClearSystem.cs:101)
            m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            m_Phase = RunPhase.Edit;
            if (noticeKey != null)
            {
                m_Notice = noticeKey;
                m_NoticeFrame = m_Frame;
            }
        }

        private Entity FindTempRun()
        {
            if (m_TempAreaQuery.IsEmptyIgnoreFilter)
                return Entity.Null;
            using (var areas = m_TempAreaQuery.ToEntityArray(Allocator.Temp))
            using (var prefabs = m_TempAreaQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < areas.Length; i++)
                    if (prefabs[i].m_Prefab == m_RunPrefab)
                        return areas[i];
            }
            return Entity.Null;
        }

        private static string Describe(Entity e) => $"{e.Index}:{e.Version}";

        /// <summary>Locale keys of the tool's fixed notices (LocaleEN; the tooltip resolves keys, GradeGuards.IsKey).</summary>
        private const string KeyRunRefused = "SkiResort.Tooltip.CAT_TRACK_REFUSED";
        private const string KeyRunMissing = "SkiResort.Tooltip.CAT_TRACK_MISSING";

        /// <summary>Read the base rect, re-rasterize on it, guard, write, clear trees, push undo (with <paramref name="run"/>, the run
        /// this grade builds, or Null), bump the run catalog. False when refused (reason in the notice).</summary>
        private bool ApplyCore(string label, GradePath path, GradeProfile prof, GradeParams p, Entity run)
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
            if (GradeRaster.TooLarge(rect))
            {
                Say($"Grade [apply:{label}]: REFUSED — the rect is {rect.z} x {rect.w} texels, over the {GradeRaster.MaxTexels} texel limit",
                    GradeGuards.Reason(GradeGuardKind.TooLarge));
                return false;
            }
            ushort[] before = m_Writer.Read(rect, label + " pre-image");
            if (before == null)
            {
                Say($"Grade [apply:{label}]: REFUSED — the base heightmap could not be read", "The terrain could not be read (see the log)");
                return false;
            }
            CollectObstacles(map, rect);
            GradeRaster raster = GradeRaster.Build(map, rect, path, prof, p, reach, (i, j) => map.ToHeight(before[(j - rect.y) * rect.z + (i - rect.x)]),
                                                   m_Obstacles.Runs);
            GradeGuardKind guard = CheckGuards(map, raster, out float2 at);
            if (guard == GradeGuardKind.None && m_Obstacles.Runs.Count > 0)
            {
                if (CrossingSteep(map, path, raster, p, out at))
                    guard = GradeGuardKind.CrossingSteep;
                bool[] entered = GradeRaster.EnteredOutlines(path, m_Obstacles.Runs);
                if (guard == GradeGuardKind.None && entered != null && Array.IndexOf(entered, true) >= 0 &&
                    !SameBands(RateCrossed(map, raster, entered, false), RateCrossed(map, raster, entered, true), out string bands))
                {
                    guard = GradeGuardKind.CrossedRunBand;
                    at = path.Xz[path.Count / 2];
                    Mod.Log.Info($"Grade [apply:{label}]: {bands}");
                }
            }
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
            int dropped = m_Undo.Push(new GradeUndoEntry
            {
                X0 = rect.x, Z0 = rect.y, W = rect.z, H = rect.w, Before = before, After = after, Label = label,
                Run = run != Entity.Null ? new int2(run.Index, run.Version) : default,
            });
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

        /// <summary>Delete the free-standing trees on the flat top (+ TreeMarginM) outside crossed runs, the way a road clears its lane: plain Deleted with
        /// Applied / Created / Updated stripped (CleanUpSystem destroys it at frame end). Through EntityManager on the main thread, NOT
        /// ToolOutputBarrier: that SafeCommandBufferSystem's CreateCommandBuffer throws outside its window (SafeCommandBufferSystem.cs:14-21),
        /// and the probe runs from a dev-tab callback.</summary>
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
                    if (path.Nearest(tr.m_Position.xz, out float _, out int _) <= clear && !InCrossedRun(tr.m_Position.xz))
                        doomed.Add(e);
                }
            }
            finally
            {
                found.Dispose();
            }
            foreach (Entity e in doomed)   // structural changes after the search tree read is done
                RunAreaEmit.DeleteLikeBulldozer(EntityManager, e);
            return doomed.Count;
        }

        /// <summary>Inside the outline of a run the track crosses (m_Obstacles.Runs of this apply): its trees (a glade) stay.</summary>
        private bool InCrossedRun(float2 xz)
        {
            for (int k = 0; k < m_Obstacles.Runs.Count; k++)
                if (GradeRaster.Inside(m_Obstacles.Runs[k], xz))
                    return true;
            return false;
        }

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
            string run = DeleteRun(e.Run);
            m_RunCatalog?.NoteTerrainChanged("cat-track grade undone");
            Say($"Grade [undo]: UNDONE ({why}) — {e.Label} grade at {HeightmapWriter.Rect(rect)}; {kept} texel(s) changed since by something " +
                $"else were kept; verify {(mismatches == 0 ? "OK" : mismatches + " mismatch(es)")}; run {run}; removed trees do not come " +
                $"back; {m_Undo.Count} grade(s) left to undo", "Cat track undone");
            return true;
        }

        private bool IsCatTrack(Entity e) =>
            e != Entity.Null && m_RunPrefab != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e) &&
            !EntityManager.HasComponent<Temp>(e) && EntityManager.TryGetComponent(e, out PrefabRef pr) && pr.m_Prefab == m_RunPrefab;

        /// <summary>Deletes a cat track (the hub row's trash button, DECISIONS 423b; the game-mode bulldozer cannot pick a Space area,
        /// BulldozeToolSystem.cs:1495-1500, RaycastJobs.cs:113): the bulldozer's end state, the ground stays as graded and no fee comes back.
        /// Main thread, from a system update only (the hub applies its queued request in OnUpdate, never in the binding callback). False when
        /// the entity is not a live cat track (already gone, or the index was reused).</summary>
        public bool DeleteTrack(Entity e, string why)
        {
            if (m_RunPrefab == Entity.Null && m_Catalog != null)
                m_RunPrefab = m_Catalog.EntityOf(SkiCatalog.Names.RunCatTrack);
            if (!IsCatTrack(e))
            {
                Mod.Log.Info($"Grade [delete]: {Describe(e)} is not a live cat track ({why}) — nothing deleted");
                return false;
            }
            RunAreaEmit.DeleteLikeBulldozer(EntityManager, e);
            Mod.Log.Info($"Grade [delete]: DELETED cat track {Describe(e)} ({why}); the ground stays graded, no refund");
            return true;
        }

        /// <summary>Deletes the cat-track run an undone grade built: plain Deleted with Applied / Created / Updated stripped, the
        /// SkiRunGateCascadeSystem idiom (no refund: the bulldozer refunds no run fee either). Skipped when the entity is gone, already
        /// deleted, or no longer a cat track (the index was reused). Returns the log word.</summary>
        private string DeleteRun(int2 id)
        {
            if (math.all(id == 0))
                return "none (a grade without a run)";
            var e = new Entity { Index = id.x, Version = id.y };
            if (!IsCatTrack(e))
                return $"{id.x}:{id.y} already gone (deleted?)";
            RunAreaEmit.DeleteLikeBulldozer(EntityManager, e);
            return $"{id.x}:{id.y} deleted";
        }


        /// <summary>Grades a fixed ProbeLengthM strip in front of the camera through the full apply path (constant grade, the current
        /// width and side slopes, guards on; steeper than the limit allowed). TickWatch then checks the CPU heights after 30 frames and
        /// the water sim after 30 / 120 frames.</summary>
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
                // A visible change on any slope: a constant ramp 1.5 m below the ground at the middle (a cut), ends on the ground
                for (int i = 0; i < path.Count; i++)
                    prof.Y[i] -= 1.5f * math.sin(math.PI * path.S[i] / path.LengthM);
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "Grade [probe]: START — {0:F0} m strip from ({1:F0}, {2:F0}) toward ({3:F0}, {4:F0}), ground {5:F1} -> {6:F1}, a ramp with a 1.5 m " +
                    "cut at its middle; WaterSimSpeed {7}; {8}", path.LengthM, a.x, a.y, b.x, b.y, ground[0], ground[path.Count - 1],
                    m_Writer.WaterSimSpeed, p.Describe()));
                if (!ApplyCore("probe", path, prof, p, Entity.Null))
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
            if (m_Guard != GradeGuardKind.None && m_Guard != GradeGuardKind.OffMap && m_Guard != GradeGuardKind.TooLarge)
                buffer.DrawCircle(GuardColor, GuardColor, 0f, projected, ring, Ground(ref hd, m_GuardAt), 10f);
        }

        private static float3 Ground(ref TerrainHeightData hd, float2 xz) => RunAreaEmit.Ground(ref hd, xz);


        internal static GradeParams ReadParams()
        {
            var s = Mod.Settings;
            var p = new GradeParams();
            if (s == null)
                return p;
            p.MaxGrade = math.clamp(s.CatTrackGradeMaxPct, 5, 25) / 100f;
            p.TopWidthM = math.clamp(s.CatTrackWidthM, GradeParams.MinTopWidthM, GradeParams.MaxTopWidthM);
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
            {
                m_Edit.Clear();
                m_Phase = RunPhase.Edit;
                m_AppliedRun = Entity.Null;
            }
            m_Path = null;
            m_Prof = null;
            m_Raster = null;
            m_Ring = null;
            m_Guard = GradeGuardKind.None;
            m_Dirty = m_ApplyRequested = m_CrossingPending = false;
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
