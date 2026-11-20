// T253 slope overlay: while the ShowSlopeOverlay option is on, the ground within RadiusM of the cursor shows each 3.5 m cell's slope band
// in the run colors (green / blue / black / double black, or the European set). One source of truth: the cells are
// SkiSlopeClassifier.CellValue on the rating's world lattice (SkiSlopeOverlayTiles, SlopeBatch parity with ClassifyRing). The look (1.0.4):
// smooth band outlines over a fainter fill (SkiSlopeContours, built off the main thread by SkiSlopeContourBuilder, drawn by the Burst
// ContourDrawJob through OverlayRenderSystem with AddBufferWriter; the run heat-map uses the same renderer). Vanilla's infoview heat map
// has no mod slot (OverlayInfomodeSystem.cs:735-1026, 1.6.4f1).
// Inside the run being drawn the heat-map paints the same cells, so the overlay leaves them to it (SkiRunDrawFeedbackSystem.PaintedGrid).
// Cursor: our own terrain ray march along ToolRaycastSystem.CalculateRaycastLine (ToolRaycastSystem.cs:69-78), because the default tool
// raycasts objects only in its default state (DefaultToolSystem.cs:794-796, 1.6.4f1). Hidden while the pointer is over the UI.
// Per frame, main thread: ray march, Update (tiles nearest first under BudgetMs), Verify (VerifyNodesPerFrame stored heights, bit-exact).
// A build covers RadiusM + BuildMarginM around the cursor. It restarts when the cursor moved RebuildMoveM from the last build's center, a
// tile changed, the heat-map's grid or the grading scheme changed, and the builder is idle (the field is gathered on the main thread). The
// draw job clips the last build to the disk around the current cursor every frame.
// Toggle: the ToggleSlopeOverlay key (polled in UIUpdate by SkiSlopeOverlayKeySystem, like every hotkey poller of the mod) and the hub's
// Slope colors chip (RequestSet). Both save the settings.
// Phase: Rendering, UpdateBefore OverlayRenderSystem and UpdateAfter SkiRunDrawFeedbackSystem (Mod.cs, both load-bearing). Game mode only.
// Logs: ARMED once per load; one line per toggle; one [cost] line per CostLogSeconds while on (Measurement diagnostics only); a WARN once
// per load when the managed frame cost passes SlowFrameWarnMs; a one-shot `!!!` disables the system until the next load.
// Engine notes: EngineNotes/SkiSlopeOverlaySystem.md

using System;
using System.Collections.Generic;
using Colossal.Collections;             // NativeValue (BufferView)
using Colossal.Mathematics;              // Line3
using Colossal.Serialization.Entities;   // Purpose
using Game;
using Game.Input;
using Game.Rendering;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using SkiResortMod.Settings;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;  // UnsafeUtility (BufferView)
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiSlopeOverlaySystem : GameSystemBase
    {
        public static class Tuning
        {
            /// <summary>Overlay radius around the cursor (m). 300 m = a 600 m disk, about 23,000 cells at the 3.5 m texel.</summary>
            public const float RadiusM = 300f;
            /// <summary>Outer band of the disk whose alpha fades to the edge (m), in SkiSlopeOverlayTiles.FadeLevels steps.</summary>
            public const float FadeM = 15f;
            /// <summary>Main-thread budget per frame for computing missing tiles (ms, wall clock). At least one tile per frame.</summary>
            public const float BudgetMs = 1.5f;
            /// <summary>Stored heights re-sampled per frame (terrain change detection).</summary>
            public const int VerifyNodesPerFrame = 2048;
            /// <summary>A build covers RadiusM + BuildMarginM. A new one starts when the cursor moved RebuildMoveM from the last build's
            /// center (m). The margin is twice the move, so the drawn disk stays inside the built area while the next build runs.</summary>
            public const float BuildMarginM = 40f, RebuildMoveM = 20f;
            /// <summary>Caps of one build: fill strips and outline curves. Over MaxFill the builder uses fewer sub-rows (4, 2, 1).</summary>
            public const int MaxFill = 40000, MaxCurves = 8000;
            /// <summary>Sub-rows by camera distance to the cursor hit (game check 2026-11-20: 13k thin strips cost +2 ms at zoom 2,600, where a
            /// sub-row is under a pixel). Beyond SubRows2M: 2 sub-rows; beyond SubRows1M: 1. HysteresisM avoids rebuilds at a boundary.</summary>
            public const float SubRows2M = 300f, SubRows1M = 1600f, SubRowsHysteresisM = 50f;
            /// <summary>Tiles kept (a 600 m disk touches about 34-49; the rest is the recently visited ring).</summary>
            public const int TileCapacity = 128;
            /// <summary>Ray march: at most this many steps, then this many bisection steps.</summary>
            public const int MarchSteps = 384, BisectSteps = 14;
            public const float SlowFrameWarnMs = 4f;
            public const float CostLogSeconds = 30f;
            /// <summary>Draw box margin below and above the node heights of a strip (m): the rendered terrain LOD and the ground cover.</summary>
            public const float BoxMarginM = 8f;
        }

        /// <summary>The same fields in the same order as OverlayRenderSystem.Buffer (OverlayRenderSystem.cs:106-131, 1.6.4f1; the
        /// game-surface lock pins them). The draw job reads the buffer's lists through it to build a Buffer per strip with the strip's own
        /// box height: the public constructor takes positionY / scaleY (:122). Why: GetBuffer gives every projected curve a box from
        /// heightScaleOffset.y - 50 to heightScaleOffset.x + 100 (:633, FitBox :321-327), about 4 km tall. Every view ray then crosses every
        /// strip box between the camera and the ground (in game, 2026-11-19: 3,358 strips cost +12.8 ms at zoom 450). Used only when the
        /// sizes match (OnCreate); otherwise the job draws through the vanilla buffer.</summary>
#pragma warning disable CS0649   // filled only by UnsafeUtility.As from the vanilla Buffer
        internal struct BufferView
        {
            public NativeList<OverlayRenderSystem.CurveData> ProjectedCurves;
            public NativeList<OverlayRenderSystem.CurveData> AbsoluteCurves;
            public NativeList<OverlayRenderSystem.CustomMeshdData> CustomMeshes;
            public NativeList<OverlayRenderSystem.TextMeshData> TextMeshes;
            public NativeValue<OverlayRenderSystem.BoundsData> Bounds;
            public float PositionY, ScaleY;
        }
#pragma warning restore CS0649

        /// <summary>True when BufferView has the size of the vanilla Buffer (the tight boxes are safe). The run heat-map uses it too.</summary>
        internal static bool TightBoxesSupported =>
            UnsafeUtility.SizeOf<OverlayRenderSystem.Buffer>() == UnsafeUtility.SizeOf<BufferView>();

        /// <summary>Leaves the cells of the heat-map's painted grid to the heat-map (world lattice index -> its grid's Mask).</summary>
        private struct HeatMapSkip : ISlopeCellSkip
        {
            public SlopeGrid Grid;
            public double2 Origin;
            public double Texel;

            public bool Skip(int i, int j)
            {
                SlopeGrid g = Grid;
                if (g == null)
                    return false;
                double wx = Origin.x + i * Texel, wz = Origin.y + j * Texel;
                int gi = (int)math.floor((wx - g.Origin.x) / g.Step + 0.5) - g.I0;
                int gj = (int)math.floor((wz - g.Origin.y) / g.Step + 0.5) - g.J0;
                if (gi < 0 || gj < 0 || gi >= g.Nx || gj >= g.Nz || g.Mask == null)
                    return false;
                return g.Mask[gj * g.Nx + gi] != 0;
            }
        }

        private TerrainSystem m_Terrain;
        private OverlayRenderSystem m_Overlay;
        private CameraUpdateSystem m_Camera;
        private SkiRunDrawFeedbackSystem m_HeatMap;

        private readonly SkiSlopeOverlayTiles m_Tiles = new SkiSlopeOverlayTiles { Capacity = Tuning.TileCapacity };
        private readonly SkiSlopeContourBuilder m_Builder = new SkiSlopeContourBuilder { MaxFill = Tuning.MaxFill, MaxCurves = Tuning.MaxCurves };
        private NativeArray<ContourFill> m_Fill;
        private NativeArray<ContourCurve> m_Curves;
        private NativeArray<UnityEngine.Color> m_FillColors, m_LineColors;
        private int m_FillCount, m_CurveCount, m_Gen;
        private bool m_ColorsEu, m_ColorsSet;
        private JobHandle m_DrawHandle;
        private bool m_TightBoxes;

        private ProxyAction m_Toggle;
        private bool m_Game, m_Armed, m_Disabled, m_ErrorLogged, m_SlowLogged, m_KeyProbeLogged;
        private int m_KeyProbe = -1;
        private bool m_WasOn;

        // Rebuild key: the cursor, the tile generation, the heat-map grid, the scheme.
        private double2 m_BuiltAt = new double2(double.NaN, double.NaN);
        private int m_BuiltTilesComputed = -1, m_BuiltTilesInvalidated = -1;
        private SlopeGrid m_BuiltSkipGrid;
        private bool m_BuiltEu;
        private bool m_Request, m_RequestOn, m_RequestToggle;
        private string m_RequestSource;

        // Dev counters (the [cost] line, the dev tab).
        public bool Visible { get; private set; }
        public int StripsDrawn => m_FillCount;
        public int CurvesDrawn => m_CurveCount;
        public int SubRows { get; private set; } = 4;
        public bool CapHit { get; private set; }
        public double LastBuildMs { get; private set; }
        public double PeakBuildMs { get; private set; }
        public int TilesCached => m_Tiles.Count;
        public int TilesComputed => m_Tiles.TilesComputed;
        public int TilesMissing { get; private set; }
        public float LastFrameMs { get; private set; }
        public float PeakFrameMs { get; private set; }
        private double m_CostSum, m_CostBuildMs;
        private int m_CostFrames, m_CostRebuilds;
        private float m_CostPeak;
        private double m_CostSince;

        /// <summary>True when the option is on (the hub chip's state).</summary>
        public static bool IsOn => Mod.Settings != null && Mod.Settings.ShowSlopeOverlay;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_HeatMap = World.GetOrCreateSystemManaged<SkiRunDrawFeedbackSystem>();
            m_Fill = new NativeArray<ContourFill>(Tuning.MaxFill, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Curves = new NativeArray<ContourCurve>(Tuning.MaxCurves, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_FillColors = new NativeArray<UnityEngine.Color>(6, Allocator.Persistent);
            m_LineColors = new NativeArray<UnityEngine.Color>(6, Allocator.Persistent);
            int vanilla = UnsafeUtility.SizeOf<OverlayRenderSystem.Buffer>(), view = UnsafeUtility.SizeOf<BufferView>();
            m_TightBoxes = vanilla == view;
            if (m_TightBoxes)
                Mod.Log.Info($"{nameof(SkiSlopeOverlaySystem)} created, tight strip boxes");
            else
                Mod.Log.Warn($"{nameof(SkiSlopeOverlaySystem)} created: !!! OverlayRenderSystem.Buffer is {vanilla} bytes, BufferView {view}. " +
                             "Strips use the full-height vanilla boxes (slow on the GPU).");
        }

        protected override void OnDestroy()
        {
            CompleteDraw();
            if (m_Fill.IsCreated)
                m_Fill.Dispose();
            if (m_Curves.IsCreated)
                m_Curves.Dispose();
            if (m_FillColors.IsCreated)
                m_FillColors.Dispose();
            if (m_LineColors.IsCreated)
                m_LineColors.Dispose();
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            CompleteDraw();
            m_Game = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = m_SlowLogged = m_KeyProbeLogged = false;
            m_Armed = false;
            m_Tiles.Clear();
            m_FillCount = m_CurveCount = 0;
            m_Gen++;   // a build still running for the old world is dropped when it lands
            m_BuiltAt = new double2(double.NaN, double.NaN);
            m_BuiltTilesComputed = m_BuiltTilesInvalidated = -1;
            m_BuiltSkipGrid = null;
            m_Request = m_RequestToggle = false;
            PeakFrameMs = 0f;
            ResetCost();
            Visible = false;
            try
            {
                m_Toggle = Mod.Settings != null ? Mod.Settings.GetAction(SkiResortSettings.ActToggleSlopeOverlay) : null;   // ModSetting.cs:289-292
            }
            catch (Exception)
            {
                m_Toggle = null;
            }
            if (m_Toggle != null)
                m_Toggle.shouldBeEnabled = m_Game;
            if (!m_Game)
                return;
            m_Armed = true;
            m_KeyProbe = 120;
            m_WasOn = IsOn;
            Mod.Log.Info($"SlopeOverlay: ARMED — option ShowSlopeOverlay {(m_WasOn ? "ON" : "off")}; the ground within {Tuning.RadiusM:F0} m of the " +
                         $"cursor in the run colors (SkiSlopeClassifier.CellValue, {SkiSlopeOverlayTiles.TileCells}x{SkiSlopeOverlayTiles.TileCells}-cell tiles, " +
                         $"budget {Tuning.BudgetMs} ms/frame, verify {Tuning.VerifyNodesPerFrame} heights/frame, outlines over a fainter fill, built off the main thread over {Tuning.RadiusM + Tuning.BuildMarginM:F0} m (caps {Tuning.MaxFill} strips, {Tuning.MaxCurves} curves), " +
                         $"{Tuning.FadeM:F0} m edge fade); key {SkiResortSettings.ActToggleSlopeOverlay}" +
                         (m_Toggle == null ? " NOT REGISTERED (RegisterKeyBindings missing or failed)" : "") + ", hub chip Slope colors");
        }

        /// <summary>The hub's Slope colors chip (setSlopeOverlay). Applied in the next OnUpdate (saves the options file, one log line).</summary>
        public void RequestSet(bool on, string source)
        {
            m_Request = true;
            m_RequestOn = on;
            m_RequestSource = source;
        }

        /// <summary>The toggle key, from SkiSlopeOverlayKeySystem (UIUpdate). Applied in the next OnUpdate like RequestSet.</summary>
        public void RequestToggle(string source)
        {
            m_RequestToggle = true;
            m_RequestSource = source;
        }

        /// <summary>The armed toggle action for SkiSlopeOverlayKeySystem, null when not in a game.</summary>
        internal ProxyAction ToggleAction => m_Armed && !m_Disabled ? m_Toggle : null;

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Game || !m_Armed)
                return;
            try
            {
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;
                Keys();
                bool on = IsOn;
                if (on != m_WasOn)
                {
                    m_WasOn = on;
                    if (!on)
                        LogCost(true);
                    ResetCost();
                }
                if (!on)
                {
                    Visible = false;
                    return;
                }
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                bool drew = Frame();
                float ms = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                Visible = drew;
                LastFrameMs = ms;
                if (ms > PeakFrameMs)
                    PeakFrameMs = ms;
                m_CostSum += ms;
                m_CostFrames++;
                if (ms > m_CostPeak)
                    m_CostPeak = ms;
                if (ms > Tuning.SlowFrameWarnMs && !m_SlowLogged && m_Tiles.TilesComputed > 0 && TilesMissing == 0)
                {
                    m_SlowLogged = true;
                    Mod.Log.Warn($"SlopeOverlay: slow frame {ms:F2} ms with every tile cached ({m_Tiles.Count} tiles, {m_FillCount} strips, {m_CurveCount} curves, " +
                                 $"{SubRows} sub-rows) — logged once per load");
                }
                LogCost(false);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Visible = false;
                m_FillCount = m_CurveCount = 0;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiSlopeOverlaySystem)} EXCEPTION — the slope overlay is off until the next game load:\n{e}");
                }
            }
        }

        private void Keys()
        {
            // isSet: the action has a key (ProxyBinding.cs:600). The action is unbound by default, and an unbound action is no conflict.
            if (m_KeyProbe > 0 && --m_KeyProbe == 0 && m_Toggle != null && m_Toggle.isSet && !m_Toggle.enabled && !m_KeyProbeLogged)
            {
                m_KeyProbeLogged = true;
                Mod.Log.Warn($"SlopeOverlay [keys]: {SkiResortSettings.ActToggleSlopeOverlay} is not live 120 frames after the load (probable key " +
                             "conflict with a vanilla action, InputConflictResolution.cs:138-198) — rebind it; the hub chip still works");
            }
            bool toggle = m_RequestToggle;
            if (!toggle && !m_Request)
                return;
            SkiResortSettings s = Mod.Settings;
            if (s == null)
                return;
            bool want = toggle ? !s.ShowSlopeOverlay : m_RequestOn;
            string source = m_RequestSource ?? (toggle ? "key" : "hub");
            m_Request = m_RequestToggle = false;
            if (want == s.ShowSlopeOverlay)
                return;
            s.ShowSlopeOverlay = want;
            s.ApplyAndSave();
            Mod.Log.Info($"SlopeOverlay: {(s.ShowSlopeOverlay ? "ON" : "off")} ({source})");
        }

        /// <summary>One visible frame. Returns true when strips were drawn.</summary>
        private bool Frame()
        {
            InputManager input = InputManager.instance;
            if (input == null || input.mouseOverUI || !input.mouseOnScreen)
                return false;
            TerrainHeightData hd = m_Terrain.GetHeightData();   // fresh every frame (TerrainSystem.cs:2603), never cached
            if (!hd.isCreated || hd.resolution.x < 4096)
                return false;
            float texel = 1f / hd.scale.x;                                // TerrainSystem.cs:2619-2620
            var origin = new double2(-hd.offset.x, -hd.offset.z);         // texel centers, TerrainUtils.cs:35-38
            if (!m_Tiles.SetLattice(origin, texel))
                return false;
            if (!m_Camera.TryGetViewer(out Viewer viewer) || viewer.camera == null)
                return false;
            Line3.Segment ray = ToolRaycastSystem.CalculateRaycastLine(viewer.camera);
            if (!MarchTerrain(ref hd, ray, out float3 hit))
                return false;

            var sampler = new TerrainHeightSampler(hd);
            int frame = UnityEngine.Time.frameCount;
            TilesMissing = m_Tiles.Update(ref sampler, hit.x, hit.z, Tuning.RadiusM, frame, Tuning.BudgetMs);
            m_Tiles.Verify(ref sampler, frame, Tuning.VerifyNodesPerFrame);

            bool eu = DisplayState.Scheme == GradeScheme.European;
            SlopeGrid skipGrid = m_HeatMap.PaintedGrid;
            var at = new double2(hit.x, hit.z);
            TakeBuild();
            bool moved = !(math.distancesq(at, m_BuiltAt) < (double)Tuning.RebuildMoveM * Tuning.RebuildMoveM);
            float camDist = math.distance(viewer.position, hit);
            int lodSubs = LodSubRows(camDist, m_Builder.MaxSubRows);
            if ((moved || lodSubs != m_Builder.MaxSubRows || m_Tiles.TilesComputed != m_BuiltTilesComputed || m_Tiles.TilesInvalidated != m_BuiltTilesInvalidated ||
                 !ReferenceEquals(skipGrid, m_BuiltSkipGrid) || eu != m_BuiltEu) && m_Builder.Idle)
            {
                m_Builder.MaxSubRows = lodSubs;
                StartBuild(at, eu, skipGrid, origin, texel);
            }
            if (m_FillCount == 0 && m_CurveCount == 0)
                return false;
            Draw(new float2(hit.x, hit.z), camDist, eu);
            return true;
        }

        /// <summary>Most sub-rows for a camera distance, with hysteresis around the current value.</summary>
        private static int LodSubRows(float dist, int current)
        {
            float d2 = Tuning.SubRows2M + (current >= 4 ? Tuning.SubRowsHysteresisM : -Tuning.SubRowsHysteresisM);
            float d1 = Tuning.SubRows1M + (current >= 2 ? Tuning.SubRowsHysteresisM : -Tuning.SubRowsHysteresisM);
            return dist > d1 ? 1 : dist > d2 ? 2 : 4;
        }

        /// <summary>First crossing of the camera ray with the terrain: steps of 0.7 x the height above ground (at least 1 m, at most 400 m),
        /// then bisection. False when the ray never goes below the ground within MarchSteps (sky, beyond the map).</summary>
        private static bool MarchTerrain(ref TerrainHeightData hd, Line3.Segment ray, out float3 hit)
        {
            hit = default;
            float3 a = ray.a, d = ray.b - ray.a;
            float len = math.length(d);
            if (!(len > 1f))
                return false;
            d /= len;
            float t = 0f, prevT = 0f;
            float above = a.y - TerrainUtils.SampleHeight(ref hd, a);
            if (!(above > 0f))
                return false;   // camera under the ground or at it: nothing sensible to show
            for (int s = 0; s < Tuning.MarchSteps && t < len; s++)
            {
                prevT = t;
                t += math.clamp(0.7f * above, 1f, 400f);
                if (t > len)
                    t = len;
                float3 p = a + d * t;
                above = p.y - TerrainUtils.SampleHeight(ref hd, p);
                if (!math.isfinite(above))
                    return false;
                if (above <= 0f)
                {
                    float lo = prevT, hi = t;
                    for (int k = 0; k < Tuning.BisectSteps; k++)
                    {
                        float mid = 0.5f * (lo + hi);
                        float3 q = a + d * mid;
                        if (q.y - TerrainUtils.SampleHeight(ref hd, q) > 0f)
                            lo = mid;
                        else
                            hi = mid;
                    }
                    hit = a + d * hi;
                    return math.all(math.isfinite(hit));
                }
            }
            return false;
        }

        /// <summary>Gathers the field of RadiusM + BuildMarginM around `at` (the heat-map's painted cells NaN) and starts the builder.
        /// Only while the builder is idle.</summary>
        private void StartBuild(double2 at, bool eu, SlopeGrid skipGrid, double2 origin, float texel)
        {
            m_BuiltAt = at;
            m_BuiltTilesComputed = m_Tiles.TilesComputed;
            m_BuiltTilesInvalidated = m_Tiles.TilesInvalidated;
            m_BuiltSkipGrid = skipGrid;
            m_BuiltEu = eu;
            double reach = Tuning.RadiusM + Tuning.BuildMarginM;
            int i0 = m_Tiles.NodeOf(at.x - reach, origin.x), i1 = m_Tiles.NodeOf(at.x + reach, origin.x);
            int j0 = m_Tiles.NodeOf(at.y - reach, origin.y), j1 = m_Tiles.NodeOf(at.y + reach, origin.y);
            int nx = i1 - i0 + 1, nz = j1 - j0 + 1;
            SkiSlopeContourBuilder b = m_Builder;
            b.Ensure(nx * nz, 0);
            m_Tiles.Gather(i0, j0, nx, nz, b.O, b.H);
            double r2 = reach * reach;
            var skip = new HeatMapSkip { Grid = skipGrid, Origin = origin, Texel = texel };
            for (int j = 0; j < nz; j++)
            {
                double dz = origin.y + (j0 + j) * (double)texel - at.y;
                for (int i = 0; i < nx; i++)
                {
                    double dx = origin.x + (i0 + i) * (double)texel - at.x;
                    if (dx * dx + dz * dz > r2 || (skipGrid != null && skip.Skip(i0 + i, j0 + j)))
                        b.O[j * nx + i] = float.NaN;
                }
            }
            b.Nx = nx;
            b.Nz = nz;
            b.Ox = origin.x + i0 * (double)texel;
            b.Oz = origin.y + j0 * (double)texel;
            b.Step = texel;
            b.Eu = eu;
            b.RingCount = 0;
            b.Gen = m_Gen;
            b.Start(true);
        }

        /// <summary>A finished build: its strips and curves into the NativeArrays the draw job reads (after the last draw job finished).
        /// A build of an older world (m_Gen) is dropped.</summary>
        private void TakeBuild()
        {
            SkiSlopeContourBuilder b = m_Builder;
            if (!b.Done)
                return;
            if (b.Error != null)
            {
                Exception e = b.Error;
                b.Release();
                throw new InvalidOperationException("contour build failed on the worker thread", e);
            }
            if (b.Gen == m_Gen)
            {
                CompleteDraw();   // the only write to m_Fill / m_Curves, after the last job finished (heat-map OVERLAY safety (a))
                int nf = Math.Min(b.FillCount, m_Fill.Length), nc = Math.Min(b.CurveCount, m_Curves.Length);
                if (nf > 0)
                    NativeArray<ContourFill>.Copy(b.Fill, m_Fill, nf);
                if (nc > 0)
                    NativeArray<ContourCurve>.Copy(b.Curves, m_Curves, nc);
                m_FillCount = nf;
                m_CurveCount = nc;
                SubRows = b.SubRowsUsed;
                CapHit = b.Capped;
                LastBuildMs = b.BuildMs;
                if (b.BuildMs > PeakBuildMs)
                    PeakBuildMs = b.BuildMs;
                m_CostBuildMs += b.BuildMs;
                m_CostRebuilds++;
            }
            b.Release();
        }

        private void Draw(float2 center, float cameraDistance, bool eu)
        {
            if (!m_ColorsSet || eu != m_ColorsEu)
            {
                CompleteDraw();
                SkiSlopeContourLook.Colors(eu ? SkiRunDrawFeedbackSystem.BandColorsEu : SkiRunDrawFeedbackSystem.BandColors, m_FillColors, m_LineColors);
                m_ColorsSet = true;
                m_ColorsEu = eu;
            }
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);   // OverlayRenderSystem.cs:603-634
            var job = new ContourDrawJob
            {
                Fill = m_Fill,
                FillCount = m_FillCount,
                Curves = m_Curves,
                CurveCount = m_CurveCount,
                FillColors = m_FillColors,
                LineColors = m_LineColors,
                LineWidth = SkiSlopeContourLook.LineWidth(cameraDistance),
                Disk = true,
                Center = center,
                Radius = Tuning.RadiusM,
                FadeM = Tuning.FadeM,
                TightBoxes = m_TightBoxes,
                Overlay = buffer,
            };
            JobHandle handle = job.Schedule(deps);
            m_Overlay.AddBufferWriter(handle);   // :636-639, completed at OverlayRenderSystem :740
            m_DrawHandle = handle;
        }

        private void CompleteDraw()
        {
            m_DrawHandle.Complete();
            m_DrawHandle = default(JobHandle);
        }

        private void ResetCost()
        {
            m_CostSum = 0.0;
            m_CostFrames = 0;
            m_CostRebuilds = 0;
            m_CostBuildMs = 0.0;
            m_CostPeak = 0f;
            m_CostSince = UnityEngine.Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>The [cost] line (Measurement diagnostics only): mean / peak managed ms per visible frame since the last line.</summary>
        private void LogCost(bool final)
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (!final && now - m_CostSince < Tuning.CostLogSeconds)
                return;
            if (m_CostFrames > 0 && Mod.Settings != null && Mod.Settings.DiagnosticMeasurements)
                Mod.Log.Info($"SlopeOverlay [cost]: {m_CostFrames} frames, managed mean {m_CostSum / m_CostFrames:F3} ms peak {m_CostPeak:F2} ms, " +
                             $"{m_CostRebuilds} builds (worker mean {(m_CostRebuilds > 0 ? m_CostBuildMs / m_CostRebuilds : 0):F2} ms, peak " +
                             $"{PeakBuildMs:F2} ms), {m_FillCount} strips, {m_CurveCount} curves ({SubRows} sub-rows{(CapHit ? ", CAP HIT" : "")}), " +
                             $"{m_Tiles.Count} tiles cached, {m_Tiles.TilesComputed} computed, {m_Tiles.TilesInvalidated} invalidated, " +
                             $"{m_Tiles.TilesEvicted} evicted, {m_Tiles.NodesVerified} heights verified");
            ResetCost();
        }
    }
}
