// T253 slope overlay: while the ShowSlopeOverlay option is on, the ground within RadiusM of the cursor shows each 3.5 m cell's slope band
// in the run colors (green / blue / black / double black, or the European set). One source of truth: the cells are
// SkiSlopeClassifier.CellValue on the rating's world lattice (SkiSlopeOverlayTiles, SlopeBatch parity with ClassifyRing). The renderer is
// the run heat-map's: Projected row strips through OverlayRenderSystem (OverlayRenderSystem.cs:207 DrawLine, 1.6.0f1), written by a Burst
// job registered with AddBufferWriter. Vanilla's infoview heat map has no mod slot (OverlayInfomodeSystem.cs:735-1026, 1.6.4f1).
// Inside the run being drawn the heat-map paints the same cells, so the overlay leaves them to it (SkiRunDrawFeedbackSystem.PaintedGrid).
// Cursor: our own terrain ray march along ToolRaycastSystem.CalculateRaycastLine (ToolRaycastSystem.cs:69-78), because the default tool
// raycasts objects only in its default state (DefaultToolSystem.cs:794-796, 1.6.4f1). Hidden while the pointer is over the UI.
// Per frame, main thread: ray march, Update (tiles nearest first under BudgetMs), Verify (VerifyNodesPerFrame stored heights, bit-exact),
// strips rebuilt only when the cursor moved RebuildMoveM, a tile changed, the heat-map's grid or the grading scheme changed.
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
            /// <summary>Strips are rebuilt when the cursor moved at least this far (m).</summary>
            public const float RebuildMoveM = 1.5f;
            /// <summary>Strip cap per frame. Above it the rows are decimated (stride 2..MaxRowStride, the heat-map's rule).</summary>
            public const int MaxStrips = 4500;
            /// <summary>Row runs gathered before MergeRows stacks them; the strip cap applies to the merged rectangles.</summary>
            public const int RunCap = 4 * MaxStrips;
            public const int MaxRowStride = 3;
            /// <summary>Tiles kept (a 600 m disk touches about 34-49; the rest is the recently visited ring).</summary>
            public const int TileCapacity = 128;
            /// <summary>Overlay alphas per strip key (DisplayUnits.StripKey 0..5): flat, green, blue band, black, double black, European blue.
            /// Softer than the heat-map's (T253 look review, option C); the heat-map keeps its own.</summary>
            public static readonly float[] Alpha = { 0.35f, 0.35f, 0.45f, 0.50f, 0.45f, 0.45f };
            /// <summary>Ray march: at most this many steps, then this many bisection steps.</summary>
            public const int MarchSteps = 384, BisectSteps = 14;
            public const float SlowFrameWarnMs = 4f;
            public const float CostLogSeconds = 30f;
            /// <summary>Draw box margin below and above the node heights of a strip (m): the rendered terrain LOD and the ground cover.</summary>
            public const float BoxMarginM = 8f;
        }

        public struct Strip
        {
            public float3 A, B;
            public float Width;
            public float YLo, YHi;   // draw box bottom and top (m)
            public UnityEngine.Color Color;
        }

        /// <summary>The same fields in the same order as OverlayRenderSystem.Buffer (OverlayRenderSystem.cs:106-131, 1.6.4f1; the
        /// game-surface lock pins them). The draw job reads the buffer's lists through it to build a Buffer per strip with the strip's own
        /// box height: the public constructor takes positionY / scaleY (:122). Why: GetBuffer gives every projected curve a box from
        /// heightScaleOffset.y - 50 to heightScaleOffset.x + 100 (:633, FitBox :321-327), about 4 km tall. Every view ray then crosses every
        /// strip box between the camera and the ground (in game, 2026-11-19: 3,358 strips cost +12.8 ms at zoom 450). Used only when the
        /// sizes match (OnCreate); otherwise the job draws through the vanilla buffer.</summary>
#pragma warning disable CS0649   // filled only by UnsafeUtility.As from the vanilla Buffer
        private struct BufferView
        {
            public NativeList<OverlayRenderSystem.CurveData> ProjectedCurves;
            public NativeList<OverlayRenderSystem.CurveData> AbsoluteCurves;
            public NativeList<OverlayRenderSystem.CustomMeshdData> CustomMeshes;
            public NativeList<OverlayRenderSystem.TextMeshData> TextMeshes;
            public NativeValue<OverlayRenderSystem.BoundsData> Bounds;
            public float PositionY, ScaleY;
        }
#pragma warning restore CS0649

        /// <summary>Writes the cached strips into the overlay buffer. Reads only our own NativeArray (no ECS data).</summary>
        [BurstCompile]
        private struct StripDrawJob : IJob
        {
            [ReadOnly] public NativeArray<Strip> Strips;
            public int Count;
            public bool TightBoxes;
            public OverlayRenderSystem.Buffer Overlay;

            public void Execute()
            {
                var flags = OverlayRenderSystem.StyleFlags.Projected;
                var roundness = new float2(0f, 0f);
                int n = Count < Strips.Length ? Count : Strips.Length;
                if (!TightBoxes)
                {
                    for (int s = 0; s < n; s++)
                    {
                        Strip st = Strips[s];
                        Overlay.DrawLine(st.Color, st.Color, 0f, flags, new Line3.Segment(st.A, st.B), st.Width, roundness);
                    }
                    return;
                }
                BufferView v = UnsafeUtility.As<OverlayRenderSystem.Buffer, BufferView>(ref Overlay);
                float lo = float.MaxValue, hi = float.MinValue;
                for (int s = 0; s < n; s++)
                {
                    Strip st = Strips[s];
                    // FitBox: box bottom = positionY, height = scaleY (box mesh y 0..1, OverlayRenderSystem.cs:1086-1096).
                    var b = new OverlayRenderSystem.Buffer(v.ProjectedCurves, v.AbsoluteCurves, v.CustomMeshes, v.TextMeshes, v.Bounds,
                        st.YLo, st.YHi - st.YLo);
                    b.DrawLine(st.Color, st.Color, 0f, flags, new Line3.Segment(st.A, st.B), st.Width, roundness);
                    lo = math.min(lo, st.YLo);
                    hi = math.max(hi, st.YHi);
                }
                // FitBox sets bounds.max.y = scaleY, the height and not the top. In vanilla both are the same, here they are not: add the
                // real top so the bounds of the instanced draw (:865) hold every strip.
                if (n > 0)
                {
                    OverlayRenderSystem.BoundsData bd = v.Bounds.value;
                    bd.m_CurveBounds.min.y = math.min(bd.m_CurveBounds.min.y, lo);
                    bd.m_CurveBounds.max.y = math.max(bd.m_CurveBounds.max.y, hi);
                    v.Bounds.value = bd;
                }
            }
        }

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
        private readonly List<SlopeOverlayRun> m_Runs = new List<SlopeOverlayRun>();
        private readonly List<SlopeOverlayRun> m_Rects = new List<SlopeOverlayRun>();
        private readonly Strip[] m_Stage = new Strip[Tuning.MaxStrips];
        private NativeArray<Strip> m_Strips;
        private int m_StripCount;
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
        public int StripsDrawn => m_StripCount;
        public int RowStride { get; private set; } = 1;
        public bool CapHit { get; private set; }
        public int TilesCached => m_Tiles.Count;
        public int TilesComputed => m_Tiles.TilesComputed;
        public int TilesMissing { get; private set; }
        public float LastFrameMs { get; private set; }
        public float PeakFrameMs { get; private set; }
        private double m_CostSum;
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
            m_Strips = new NativeArray<Strip>(Tuning.MaxStrips, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
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
            if (m_Strips.IsCreated)
                m_Strips.Dispose();
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
            m_StripCount = 0;
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
                         $"budget {Tuning.BudgetMs} ms/frame, verify {Tuning.VerifyNodesPerFrame} heights/frame, cap {Tuning.MaxStrips} strips, " +
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
                    Mod.Log.Warn($"SlopeOverlay: slow frame {ms:F2} ms with every tile cached ({m_Tiles.Count} tiles, {m_StripCount} strips, " +
                                 $"stride {RowStride}) — logged once per load");
                }
                LogCost(false);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Visible = false;
                m_StripCount = 0;
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
            bool moved = !(math.distancesq(at, m_BuiltAt) < (double)Tuning.RebuildMoveM * Tuning.RebuildMoveM);
            if (moved || m_Tiles.TilesComputed != m_BuiltTilesComputed || m_Tiles.TilesInvalidated != m_BuiltTilesInvalidated ||
                !ReferenceEquals(skipGrid, m_BuiltSkipGrid) || eu != m_BuiltEu)
            {
                Build(at, eu, skipGrid, origin, texel);
                m_CostRebuilds++;
            }
            if (m_StripCount == 0)
                return false;
            Draw();
            return true;
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

        private void Build(double2 at, bool eu, SlopeGrid skipGrid, double2 origin, float texel)
        {
            m_BuiltAt = at;
            m_BuiltTilesComputed = m_Tiles.TilesComputed;
            m_BuiltTilesInvalidated = m_Tiles.TilesInvalidated;
            m_BuiltSkipGrid = skipGrid;
            m_BuiltEu = eu;
            var skip = new HeatMapSkip { Grid = skipGrid, Origin = origin, Texel = texel };
            int stride = 1;
            bool fit;
            while (true)
            {
                bool rowsFit = m_Tiles.BuildRuns(at.x, at.y, Tuning.RadiusM, Tuning.FadeM, eu, stride, Tuning.RunCap, ref skip, m_Runs);
                m_Tiles.MergeRows(m_Runs, m_Rects);   // fewer strips, the same cells (the GPU cost grows with the strip count)
                fit = rowsFit && m_Rects.Count <= Tuning.MaxStrips;
                if (fit || stride >= Tuning.MaxRowStride)
                    break;
                stride++;
            }
            RowStride = stride;
            CapHit = !fit;

            UnityEngine.Color[] colors = eu ? SkiRunDrawFeedbackSystem.BandColorsEu : SkiRunDrawFeedbackSystem.BandColors;
            double half = 0.5 * texel;
            int n = 0;
            for (int k = 0; k < m_Rects.Count && n < m_Stage.Length; k++)
            {
                SlopeOverlayRun r = m_Rects[k];
                if (r.Key >= colors.Length)
                    continue;
                UnityEngine.Color c = colors[r.Key];
                c.a = Tuning.Alpha[r.Key] * r.Fade / SkiSlopeOverlayTiles.FadeLevels;
                float wz = (float)(origin.y + 0.5 * (r.JLo + r.JHi) * texel);
                m_Stage[n++] = new Strip
                {
                    A = new float3((float)(origin.x + r.I0 * (double)texel - half), r.Y, wz),
                    B = new float3((float)(origin.x + r.I1 * (double)texel + half), r.Y, wz),
                    Width = (r.JHi - r.JLo + 1) * texel,
                    YLo = r.YLo - Tuning.BoxMarginM,
                    YHi = r.YHi + Tuning.BoxMarginM,
                    Color = c,
                };
            }
            CompleteDraw();   // the only write to m_Strips, after the last job finished (heat-map OVERLAY safety (a))
            if (n > 0)
                NativeArray<Strip>.Copy(m_Stage, m_Strips, n);
            m_StripCount = n;
        }

        private void Draw()
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);   // OverlayRenderSystem.cs:603-634
            var job = new StripDrawJob { Strips = m_Strips, Count = m_StripCount, TightBoxes = m_TightBoxes, Overlay = buffer };
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
                             $"{m_CostRebuilds} strip rebuilds, {m_StripCount} strips (stride {RowStride}{(CapHit ? ", CAP HIT" : "")}), " +
                             $"{m_Tiles.Count} tiles cached, {m_Tiles.TilesComputed} computed, {m_Tiles.TilesInvalidated} invalidated, " +
                             $"{m_Tiles.TilesEvicted} evicted, {m_Tiles.NodesVerified} heights verified");
            ResetCost();
        }
    }
}
