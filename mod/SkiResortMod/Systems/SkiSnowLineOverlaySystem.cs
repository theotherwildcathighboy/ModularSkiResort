// SkiSnowLineOverlaySystem — the WINTER snow line on the ground while a run or the Welcome Sign is being placed
// (ticket T32 item 2, 2026-10-16; docs/research/field/2026-10-15/build/T32-snowline.md). A summer builder has no
// snow to read the line from: this draws where the climate's mean winter temperature, lapsed with elevation,
// crosses 0 °C (a pale contour) and, fainter and lower, where our snow guns start working (+SnowmakingMargin °C).
// The tooltip (SkiSnowLineTooltipSystem, same file) names both elevations once.
//
// THE NUMBERS (once per load, OnGameLoadingComplete; recomputed lazily if the climate was not ready, and the guns
// line follows the Snowmaking margin option):
//   winter mean T  = mean of hourly ClimateSystem.SampleClimate(ClimatePrefab, t).temperature over the climate's
//                    winter season. The prefab overload ignores the temperature override and the current date
//                    (ClimateSystem.cs:607-623), so the answer is the same in August and in January. The curve is
//                    ClimatePrefab.m_Temperature (:628), built from ClimatePrefab.m_Seasons[].m_TempNightDay with its
//                    Gaussian day-to-day noise (ClimatePrefab.RebuildTemperatureCurves :980-998). The window is the
//                    SeasonInfo whose name is "SeasonWinter" (ClimateSystem.SeasonInfo.name :73-80) from its
//                    m_StartTime to the next season's start (vanilla 0.9167 → 1.1667 = December-February,
//                    docs/research/snow-climate.md §3); no such season → December-February by the calendar.
//   snow line      = temperatureBaseHeight + (T_winter − freezingTemperature) / snowTemperatureHeightScale
//   guns line      = temperatureBaseHeight + (T_winter − freezingTemperature − margin) / snowTemperatureHeightScale
// i.e. the game's own lapse T_local(h) = T − (h − base) × 0.01 (SnowSystem.SnowTransfer, snow-climate.md §1.2) solved
// for the thresholds SnowLineSystem.GetSnowLineElevation and the injection's gates use (RunSnowRules.ThresholdC).
// "Sea level" in the ticket is the lapse's reference elevation temperatureBaseHeight (ClimateSystem.cs:226).
//
// WHEN IT SHOWS (the same family of triggers as SkiRunContourSystem and the run tooltip, plus the sign): Game mode,
// option ShowSnowLineWhilePlacing on, ownership prefab sets resolved, and the active tool is
//   * the area tool holding one of our run prefabs (drawing, editing; the hub's run buttons start the same tool), or
//   * the object tool placing the Ski Area Welcome Sign (ToolBaseSystem.GetPrefab is null in Upgrade/Move mode,
//     ObjectToolSystem.cs:2836-2847, so a sign upgrade does not show it).
// SkiPlacementInfoviewSystem is NOT the trigger: it only reacts to our lift LINES (it closes their Electricity view).
//
// THE DRAW. A world-anchored 96 × 96 grid at 40 m (3.8 km square, 9,216 height samples, ≤ 10 k) around the camera
// pivot (IGameCameraController.pivot, the CameraPoint rule of the hub), sampled with TerrainUtils.SampleHeight on the
// main thread (GetHeightData, no reader registration: the glade tool's precedent, SkiGladeToolSystem.cs:2306), cut
// into straight segments by SnowLineRules.MarchingSquares for the two levels (≤ 1,536 each). Rebuilt only when the
// tool becomes active, the pivot moved more than 100 m, or an elevation changed; every other frame it only re-adds
// the cached segments. Segments go into OverlayRenderSystem.Buffer.DrawLine with StyleFlags.Projected (drapes on the
// terrain, OverlayRenderSystem.cs:99-104, :207-211) on the main thread, as the glade tool does (:3560-3567): the
// writes complete the buffer's earlier writers first (GetBuffer + Complete, :603-634) and need no AddBufferWriter.
// Width = base × PisteMapRules.WidthScale(zoom), so the line stays readable when the camera is high.
//
// PHASE: Rendering, anchored UpdateBefore<SkiSnowLineOverlaySystem, Game.Rendering.OverlayRenderSystem> (Mod.cs;
// load-bearing: OverlayRenderSystem uploads and clears its lists in the same frame, :738-741, :781, :790).
// COST: idle (tool not ours, option off, not armed) = a settings bool and two reference compares; active and
// unchanged = two cached counts and ≤ 3,072 DrawLine calls (UNVERIFIED, expected ≪ 1 ms: the glade outline draws
// hundreds the same way); a rebuild = 9,216 bilinear samples + two marching passes (expected 0.3-1 ms, UNVERIFIED;
// a build over SlowBuildWarnMs is warned once per load; both times are in StateText).
//
// LOGS: `SkiSnowLineOverlaySystem created`; ONE INFO line per load (ARMED) with the elevations and the climate inputs
// ("SnowLine: ARMED — winter mean … snow line … guns line …"); one WARN when the climate is missing, once per load, one
// WARN for a slow build; a one-shot `!!!` switches it off until the next load. Nothing per frame.
// SCOPE: Game mode only. Defensive contract per house rules (created line, ARMED line, per-load reset, one-shot catch).

using System;
using System.Diagnostics;
using System.Globalization;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.Prefabs.Climate;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiSnowLineOverlaySystem : GameSystemBase
    {
        private static class Tuning
        {
            public const float SnowLineWidthM = 3f;      // plan-view width at zoom ≤ PisteMapRules.RefZoomM (× WidthScale)
            public const float GunsLineWidthM = 2f;
            public const float SlowBuildWarnMs = 3f;     // one cache build
            public const string WinterSeasonName = "SeasonWinter";   // ClimateSystem.SeasonInfo.name (the vanilla prefab name)
        }

        // Pale ice for the snow line, the same hue fainter for the guns line (sRGB / 255: OverlayRenderSystem converts
        // with Color.linear, as SkiPisteMapSystem does).
        private static readonly UnityEngine.Color s_SnowColour = new UnityEngine.Color(190f / 255f, 235f / 255f, 1f, 0.95f);
        private static readonly UnityEngine.Color s_GunsColour = new UnityEngine.Color(190f / 255f, 235f / 255f, 1f, 0.40f);

        private ToolSystem m_ToolSystem;
        private AreaToolSystem m_AreaTool;
        private ObjectToolSystem m_ObjectTool;
        private PrefabSystem m_PrefabSystem;
        private SkiRunOwnershipSystem m_Ownership;
        private SnowLineSystem m_SnowLine;
        private ClimateSystem m_Climate;
        private TimeSystem m_Time;
        private TerrainSystem m_Terrain;
        private OverlayRenderSystem m_Overlay;
        private CameraUpdateSystem m_Camera;

        // tool prefab reference → "ours" caches (re-resolved only when the reference changes).
        private PrefabBase m_AreaPrefabRef, m_ObjectPrefabRef;
        private bool m_AreaPrefabIsRun, m_ObjectPrefabIsGate;

        // The winter numbers (valid once the climate prefab was read).
        private bool m_WinterValid, m_WinterTried;
        private float m_WinterMeanC, m_WinterMinC, m_WinterMaxC, m_WinterStart, m_WinterEnd;
        private int m_WinterSamples;
        private float m_MarginC;   // T09 (2026-10-16): the margin is a float setting now (default 3.3 °C); an int cast drew the guns line ~30 m low
        private float m_BaseHeight, m_Lapse, m_Freezing;
        private float m_SnowLineM = float.NaN, m_GunsLineM = float.NaN;
        private string m_ClimateName = "-", m_WinterSource = "-";

        // The cache.
        private readonly float[] m_Grid = new float[SnowLineRules.GridSide * SnowLineRules.GridSide];
        private readonly SnowLineRules.Segment[] m_SnowSegments = new SnowLineRules.Segment[SnowLineRules.MaxSegmentsPerContour];
        private readonly SnowLineRules.Segment[] m_GunsSegments = new SnowLineRules.Segment[SnowLineRules.MaxSegmentsPerContour];
        private int m_SnowCount, m_GunsCount;
        private bool m_Built, m_WasActive, m_Dirty;
        private float m_BuiltX, m_BuiltZ;

        // Per-load state.
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_SlowBuildLogged, m_NoClimateLogged;

        /// <summary>Read by SkiSnowLineTooltipSystem in the same frame's UITooltip phase (written at Rendering of the
        /// previous frame: one frame of lag, as the run tooltip's readout). True while the contours are drawn and the
        /// elevations are valid.</summary>
        public bool Showing { get; private set; }

        /// <summary>Rounded world elevations of the two lines for the tooltip (valid while <see cref="Showing"/>).</summary>
        public int SnowLineRounded { get; private set; }
        public int GunsLineRounded { get; private set; }

        /// <summary>Builds and draws this load, last/peak build ms (dev tab, DUMP).</summary>
        public int Builds, Draws;
        public float LastBuildMs, MaxBuildMs;

        public float WinterMeanC => m_WinterMeanC;
        public float SnowLineM => m_SnowLineM;
        public float GunsLineM => m_GunsLineM;

        /// <summary>Dev tab / DUMP text (field reads only).</summary>
        public string StateText()
        {
            var ci = CultureInfo.InvariantCulture;
            return $"armed {m_Armed}, disabled {m_Disabled}, showing {Showing}, winter mean {m_WinterMeanC.ToString("F1", ci)} C " +
                   $"({m_WinterSource}), snow line {m_SnowLineM.ToString("F0", ci)} m, guns line {m_GunsLineM.ToString("F0", ci)} m " +
                   $"(margin {m_MarginC.ToString("F1", ci)} C), segments {m_SnowCount}+{m_GunsCount}, builds {Builds} (last {LastBuildMs.ToString("F2", ci)} " +
                   $"ms, max {MaxBuildMs.ToString("F2", ci)}), draws {Draws}";
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_AreaTool = World.GetOrCreateSystemManaged<AreaToolSystem>();
            m_ObjectTool = World.GetOrCreateSystemManaged<ObjectToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_SnowLine = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_Climate = World.GetOrCreateSystemManaged<ClimateSystem>();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_Terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            Mod.Log.Info($"{nameof(SkiSnowLineOverlaySystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_SlowBuildLogged = false;
            m_NoClimateLogged = false;
            m_Built = false;
            m_WasActive = false;
            m_Dirty = true;
            m_SnowCount = m_GunsCount = 0;
            m_AreaPrefabRef = m_ObjectPrefabRef = null;
            m_AreaPrefabIsRun = m_ObjectPrefabIsGate = false;
            m_WinterValid = false;
            m_WinterTried = false;
            m_SnowLineM = m_GunsLineM = float.NaN;
            Showing = false;
            Builds = Draws = 0;
            LastBuildMs = MaxBuildMs = 0f;
            if (mode != GameMode.Game)
                return;
            m_Armed = true;
            try
            {
                ComputeWinter(logArmed: true);
            }
            catch (Exception e)
            {
                HandleException(e, "OnGameLoadingComplete");
            }
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Armed)
                return;
            try
            {
                if (!WantsOverlay())
                {
                    m_WasActive = false;
                    Showing = false;
                    return;
                }
                if (!m_WasActive)
                {
                    m_WasActive = true;
                    m_Dirty = true;                       // the tool changed
                    if (!m_WinterValid && !m_WinterTried) // the climate was not ready at load: one more try per activation
                        ComputeWinter(logArmed: false);
                }
                if (!m_WinterValid)
                {
                    Showing = false;
                    return;
                }
                // The guns line follows the option (a slider; read here at the activation rate of a frame).
                float margin = m_SnowLine.GetSnowmakingMarginC();
                if (margin != m_MarginC)
                    ApplyMargin(margin);

                float3 pivot = CameraPoint();
                float dx = pivot.x - m_BuiltX, dz = pivot.z - m_BuiltZ;
                if (m_Dirty || !m_Built || dx * dx + dz * dz > SnowLineRules.RebuildMoveM * SnowLineRules.RebuildMoveM)
                    Build(pivot);

                Draw();
                Showing = true;
                SnowLineRounded = (int)math.round(m_SnowLineM);
                GunsLineRounded = (int)math.round(m_GunsLineM);
            }
            catch (Exception e)
            {
                HandleException(e, "OnUpdate");
            }
        }

        // ── the trigger ───────────────────────────────────────────────────────────────────────────────────

        private bool WantsOverlay()
        {
            SkiResortMod.Settings.SkiResortSettings s = Mod.Settings;
            if (s != null && !s.ShowSnowLineWhilePlacing)
                return false;
            if (!m_Ownership.PrefabSetsReady)
                return false;
            ToolBaseSystem tool = m_ToolSystem.activeTool;                    // ToolSystem.cs:84-99
            if (tool == m_AreaTool)
            {
                PrefabBase prefab = m_AreaTool.prefab;                          // AreaToolSystem.cs:2637
                if (!ReferenceEquals(prefab, m_AreaPrefabRef))
                {
                    m_AreaPrefabRef = prefab;
                    m_AreaPrefabIsRun = !ReferenceEquals(prefab, null) &&
                                        m_PrefabSystem.TryGetEntity(prefab, out Entity e) && m_Ownership.IsRunPrefab(e);
                }
                return m_AreaPrefabIsRun;
            }
            if (tool == m_ObjectTool)
            {
                PrefabBase prefab = m_ObjectTool.GetPrefab();                   // null in Upgrade / Move (ObjectToolSystem.cs:2836-2847)
                if (!ReferenceEquals(prefab, m_ObjectPrefabRef))
                {
                    m_ObjectPrefabRef = prefab;
                    m_ObjectPrefabIsGate = !ReferenceEquals(prefab, null) &&
                                           m_PrefabSystem.TryGetEntity(prefab, out Entity e) && m_Ownership.IsGatePrefab(e);
                }
                return m_ObjectPrefabIsGate;
            }
            return false;
        }

        // ── the climate numbers ───────────────────────────────────────────────────────────────────────────

        /// <summary>Reads the winter off the current ClimatePrefab and sets the two elevations. Once per load (and one
        /// retry at the first activation when the climate was not ready). Logs the one INFO line per load when asked.</summary>
        private void ComputeWinter(bool logArmed)
        {
            m_WinterTried = true;
            m_WinterValid = false;
            Entity climate = m_Climate.currentClimate;                          // ClimateSystem.cs:205-224
            ClimatePrefab prefab = climate != Entity.Null ? m_PrefabSystem.GetPrefab<ClimatePrefab>(climate) : null;
            if (prefab == null || prefab.m_Temperature == null)
            {
                if (!m_NoClimateLogged)
                {
                    m_NoClimateLogged = true;
                    Mod.Log.Warn($"SnowLine: no climate prefab ({(climate == Entity.Null ? "currentClimate is null" : "no curve")}) — " +
                                 "the winter snow line is not drawn until a climate is present");
                }
                if (logArmed)
                    LogArmed("no climate prefab");
                return;
            }

            // The winter window from the prefab's seasons (SeasonInfo.name, ClimateSystem.cs:73-80), else December-February.
            ClimateSystem.SeasonInfo[] seasons = prefab.m_Seasons;
            int winter = -1;
            float[] starts = null;
            if (seasons != null && seasons.Length > 0)
            {
                starts = new float[seasons.Length];
                for (int i = 0; i < seasons.Length; i++)
                {
                    starts[i] = seasons[i] != null ? seasons[i].m_StartTime : 0f;
                    if (winter < 0 && seasons[i] != null && seasons[i].name == Tuning.WinterSeasonName)
                        winter = i;
                }
            }
            SnowLineRules.WinterWindow(starts, winter, out m_WinterStart, out m_WinterEnd);
            m_WinterSource = winter >= 0 ? "season " + Tuning.WinterSeasonName : "calendar Dec-Feb (no season named " + Tuning.WinterSeasonName + ")";

            int days = m_Time != null ? m_Time.daysPerYear : 12;
            m_WinterMeanC = SnowLineRules.MeanOverWindow(t => m_Climate.SampleClimate(prefab, t).temperature,
                m_WinterStart, m_WinterEnd, days, out m_WinterMinC, out m_WinterMaxC, out m_WinterSamples);
            m_BaseHeight = m_Climate.temperatureBaseHeight;                     // ClimateSystem.cs:226
            m_Lapse = m_Climate.snowTemperatureHeightScale;                     // 0.01 °C / m, ClimateSystem.cs:228
            m_Freezing = m_Climate.freezingTemperature;
            m_ClimateName = prefab.name;
            ApplyMargin(m_SnowLine.GetSnowmakingMarginC());
            m_WinterValid = !float.IsNaN(m_SnowLineM);
            if (logArmed)
                LogArmed(m_WinterValid ? null : "no usable winter mean");
        }

        private void ApplyMargin(float margin)
        {
            m_MarginC = margin;
            m_SnowLineM = SnowLineRules.ElevationAt(m_BaseHeight, m_WinterMeanC, m_Freezing, m_Lapse);
            m_GunsLineM = SnowLineRules.ElevationAt(m_BaseHeight, m_WinterMeanC, m_Freezing + margin, m_Lapse);
            m_Dirty = true;
        }

        private void LogArmed(string problem)
        {
            var ci = CultureInfo.InvariantCulture;
            Mod.Log.Info("SnowLine: ARMED — while the area tool holds one of our run prefabs or the object tool places the Ski Area " +
                         "Welcome Sign (option ShowSnowLineWhilePlacing), draws the winter snow line and the snowmaking line on the " +
                         $"terrain (OverlayRenderSystem, Projected); climate '{m_ClimateName}': winter window {m_WinterStart.ToString("F4", ci)}-" +
                         $"{m_WinterEnd.ToString("F4", ci)} of the year ({m_WinterSource}, {m_WinterSamples} hourly samples of " +
                         $"ClimatePrefab.m_Temperature), winter mean {m_WinterMeanC.ToString("F1", ci)} C (range {m_WinterMinC.ToString("F1", ci)}.." +
                         $"{m_WinterMaxC.ToString("F1", ci)}), temperatureBaseHeight {m_BaseHeight.ToString("F1", ci)} m, lapse " +
                         $"{m_Lapse.ToString("F3", ci)} C/m, freezing {m_Freezing.ToString("F1", ci)} C, snowmaking margin {m_MarginC.ToString("F1", ci)} C → " +
                         $"winter snow line {m_SnowLineM.ToString("F0", ci)} m, guns work above {m_GunsLineM.ToString("F0", ci)} m; grid " +
                         $"{SnowLineRules.GridSide}x{SnowLineRules.GridSide} @ {SnowLineRules.CellM:0} m ({SnowLineRules.GridSide * SnowLineRules.GridSide} " +
                         $"samples), rebuild beyond {SnowLineRules.RebuildMoveM:0} m of pivot movement" +
                         (problem != null ? $"; PROBLEM: {problem}" : ""));
        }

        // ── the cache and the draw ────────────────────────────────────────────────────────────────────────

        private void Build(float3 pivot)
        {
            long t0 = Stopwatch.GetTimestamp();
            TerrainHeightData hd = m_Terrain.GetHeightData();                   // fresh, TerrainSystem.cs:2603
            int side = SnowLineRules.GridSide;
            float cell = SnowLineRules.CellM;
            float ox = SnowLineRules.Origin(pivot.x, side, cell);
            float oz = SnowLineRules.Origin(pivot.z, side, cell);
            for (int j = 0; j < side; j++)
            {
                for (int i = 0; i < side; i++)
                    m_Grid[j * side + i] = TerrainUtils.SampleHeight(ref hd, new float3(ox + i * cell, 0f, oz + j * cell));
            }
            m_SnowCount = SnowLineRules.MarchingSquares(m_Grid, side, ox, oz, cell, m_SnowLineM, m_SnowSegments);
            m_GunsCount = SnowLineRules.MarchingSquares(m_Grid, side, ox, oz, cell, m_GunsLineM, m_GunsSegments);
            m_Built = true;
            m_Dirty = false;
            m_BuiltX = pivot.x;
            m_BuiltZ = pivot.z;
            float ms = (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            Builds++;
            LastBuildMs = ms;
            if (ms > MaxBuildMs)
                MaxBuildMs = ms;
            if (ms > Tuning.SlowBuildWarnMs && !m_SlowBuildLogged)
            {
                m_SlowBuildLogged = true;
                Mod.Log.Warn($"SnowLine [cost]: one grid build took {ms:F2} ms (> {Tuning.SlowBuildWarnMs:0} ms) — " +
                             $"{side * side} samples, {m_SnowCount}+{m_GunsCount} segment(s) (once per load)");
            }
        }

        private void Draw()
        {
            if (m_SnowCount == 0 && m_GunsCount == 0)
                return;
            float scale = PisteMapRules.WidthScale(ReadZoom());
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);   // :603-634
            deps.Complete();                                                               // main-thread write, no AddBufferWriter
            var flags = OverlayRenderSystem.StyleFlags.Projected;                          // drape on the terrain (:99-104)
            var round = new float2(1f, 1f);
            float guns = m_GunsLineM, snow = m_SnowLineM;
            for (int i = 0; i < m_GunsCount; i++)
            {
                SnowLineRules.Segment g = m_GunsSegments[i];
                buffer.DrawLine(s_GunsColour, s_GunsColour, 0f, flags,
                    new Colossal.Mathematics.Line3.Segment(new float3(g.AX, guns, g.AZ), new float3(g.BX, guns, g.BZ)),
                    Tuning.GunsLineWidthM * scale, round);                                  // :207-211
            }
            for (int i = 0; i < m_SnowCount; i++)
            {
                SnowLineRules.Segment g = m_SnowSegments[i];
                buffer.DrawLine(s_SnowColour, s_SnowColour, 0f, flags,
                    new Colossal.Mathematics.Line3.Segment(new float3(g.AX, snow, g.AZ), new float3(g.BX, snow, g.BZ)),
                    Tuning.SnowLineWidthM * scale, round);
            }
            Draws++;
        }

        /// <summary>Where the camera looks (the controller's pivot), else the camera position (the hub's CameraPoint rule).</summary>
        private float3 CameraPoint()
        {
            try
            {
                IGameCameraController controller = m_Camera.activeCameraController;
                if (controller != null)
                {
                    UnityEngine.Vector3 p = controller.pivot;
                    return new float3(p.x, p.y, p.z);
                }
            }
            catch (Exception)
            {
                // a controller being torn down: fall back to the camera position
            }
            return m_Camera.position;
        }

        private float ReadZoom()
        {
            try
            {
                IGameCameraController controller = m_Camera.activeCameraController;
                return controller != null ? controller.zoom : 0f;
            }
            catch (Exception)
            {
                return 0f;   // a controller being torn down: width × 1
            }
        }

        private void HandleException(Exception e, string where)
        {
            m_Disabled = true;
            Showing = false;
            if (m_ErrorLogged)
                return;
            m_ErrorLogged = true;
            Mod.Log.Error($"!!! {nameof(SkiSnowLineOverlaySystem)} EXCEPTION in {where} — the winter snow line is off until the " +
                          $"next game load:\n{e}");
        }
    }

    /// <summary>The one tooltip line of the overlay: "Winter snow line ≈ N m · guns work above ≈ M m" (locale key
    /// SkiResort.Tooltip.SNOW_LINE with {SNOW_LINE} / {GUN_LINE} as integers). UITooltip phase, anchored after the run
    /// tooltip (Mod.cs). Built like SkiRunTooltipSystem: one StringTooltip widget as a field, its LocalizedString
    /// rebuilt only when a rounded elevation changes (never per frame: LocalizedString.Id(key, args) builds a Dictionary).</summary>
    public sealed partial class SkiSnowLineTooltipSystem : TooltipSystemBase
    {
        public const string KeySnowLine = "SkiResort.Tooltip.SNOW_LINE";

        private SkiSnowLineOverlaySystem m_Overlay;
        private StringTooltip m_Line;
        private int m_ShownSnow = int.MinValue, m_ShownGuns = int.MinValue;
        private bool m_Disabled, m_ErrorLogged;

        protected override void OnCreate()
        {
            base.OnCreate();   // caches TooltipUISystem (TooltipSystemBase.cs:14-18)
            m_Overlay = World.GetOrCreateSystemManaged<SkiSnowLineOverlaySystem>();
            m_Line = new StringTooltip
            {
                path = "skiSnowLine",
                color = TooltipColor.Info,
            };
            Mod.Log.Info($"{nameof(SkiSnowLineTooltipSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = false;
            m_ErrorLogged = false;
            m_ShownSnow = m_ShownGuns = int.MinValue;
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Overlay.Showing)
                return;
            try
            {
                int snow = m_Overlay.SnowLineRounded, guns = m_Overlay.GunsLineRounded;
                if (snow != m_ShownSnow || guns != m_ShownGuns)
                {
                    m_ShownSnow = snow;
                    m_ShownGuns = guns;
                    m_Line.value = LocalizedString.Id(KeySnowLine,
                        ("SNOW_LINE", new LocalizedNumber<int>(snow, "integer")),
                        ("GUN_LINE", new LocalizedNumber<int>(guns, "integer")));
                }
                AddMouseTooltip(m_Line);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiSnowLineTooltipSystem)} EXCEPTION — the snow-line tooltip is off until the next " +
                                  $"game load:\n{e}");
                }
            }
        }
    }
}
