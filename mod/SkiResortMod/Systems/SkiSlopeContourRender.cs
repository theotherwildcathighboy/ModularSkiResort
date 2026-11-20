// T253 (1.0.4): the game side of the smooth band outlines (SkiSlopeContours), shared by the slope overlay and the run heat-map.
// - SkiSlopeContourBuilder runs SkiSlopeContours.Build on a ThreadPool thread. The build costs about 2.5 ms per 300 m disk on CoreCLR
//   (SlopeBatch TIME line), several times that under Mono: too much for the main thread on every cursor move. The main thread fills the
//   input only while the builder is idle and reads the output only after it is done (one Volatile state word, no locks).
// - ContourDrawJob (Burst) writes the fill strips (DrawLine) and the outline curves (DrawCurve, OverlayRenderSystem.cs:251, 1.6.4f1) into
//   the overlay buffer. With a disk it clips every strip to the circle on its own line and fades the outer FadeM in FadeLevels steps, so
//   the cursor can move between two builds without a rebuild. Each strip and curve gets its own tight draw box (the 1.0.3 GPU fix,
//   SkiSlopeOverlaySystem.BufferView).
// Look (T253 1.0.4 look call, the A2 variant): fill = KeyAlpha x FillFactor (black x 1.0), outline = the steeper band's color x LineDarken
// at LineAlpha, width from the camera distance (WidthPerM, clamped to MinWidthM .. MaxWidthM).
// Engine notes: EngineNotes/SkiSlopeContours.md

using System;
using System.Threading;
using Colossal.Mathematics;
using Game.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public struct ContourFill
    {
        public float X0, X1, Z, Width, Y, YLo, YHi;
        public byte Key;
    }

    public struct ContourCurve
    {
        public float3 P0, P1, P2, P3;
        public float YLo, YHi;
        public byte Key;   // the steeper band's strip key
    }

    public static class SkiSlopeContourLook
    {
        /// <summary>Fill alpha factor on Tuning.KeyAlpha (the black band keeps 1.0: thinner, it reads olive on grass).</summary>
        public const float FillFactor = 0.75f;
        /// <summary>Outline: the steeper band's color x LineDarken, at LineAlpha.</summary>
        public const float LineDarken = 0.72f;
        public const float LineAlpha = 0.9f;
        /// <summary>Outline width (m) = WidthPerM x the camera's distance to the cursor, clamped. 1 m is the floor: the fill's sub-row
        /// steps reach up to 0.44 m past a band edge, and the half width covers them.</summary>
        public const float WidthPerM = 0.003f, MinWidthM = 1f, MaxWidthM = 6f;

        public static float LineWidth(float cameraDistanceM) =>
            math.clamp(WidthPerM * (math.isfinite(cameraDistanceM) ? cameraDistanceM : 0f), MinWidthM, MaxWidthM);

        /// <summary>Fills the per-key fill and line colors (index = strip key) from a band color table.</summary>
        public static void Colors(UnityEngine.Color[] band, NativeArray<UnityEngine.Color> fill, NativeArray<UnityEngine.Color> line)
        {
            for (int k = 0; k < fill.Length; k++)
            {
                UnityEngine.Color c = k < band.Length ? band[k] : new UnityEngine.Color(0f, 0f, 0f, 0f);
                UnityEngine.Color f = c;
                f.a = k < band.Length ? c.a * (k == 3 ? 1f : FillFactor) : 0f;
                fill[k] = f;
                line[k] = k < band.Length
                    ? new UnityEngine.Color(c.r * LineDarken, c.g * LineDarken, c.b * LineDarken, LineAlpha)
                    : new UnityEngine.Color(0f, 0f, 0f, 0f);
            }
        }
    }

    /// <summary>Runs SkiSlopeContours.Build off the main thread. Main thread: Ensure + fill the inputs while Idle, Start, then poll Done, read
    /// the outputs, Release. Worker: only Run.</summary>
    internal sealed class SkiSlopeContourBuilder
    {
        private readonly SkiSlopeContours m_Contours = new SkiSlopeContours();
        private int m_State;   // 0 idle, 1 running, 2 done (Volatile)

        // Inputs (main thread, while Idle).
        public float[] O = new float[0], H = new float[0];
        public int Nx, Nz;
        public double Ox, Oz, Step;
        public bool Eu;
        public double2[] Ring = new double2[0];
        public int RingCount;
        public int MaxFill, MaxCurves;
        public int Gen;

        // Outputs (main thread, once Done).
        public ContourFill[] Fill = new ContourFill[0];
        public ContourCurve[] Curves = new ContourCurve[0];
        public int FillCount, CurveCount, SubRowsUsed;
        public bool Capped;
        public double BuildMs;
        public Exception Error;

        public bool Idle => Volatile.Read(ref m_State) == 0;
        public bool Done => Volatile.Read(ref m_State) == 2;

        public void Ensure(int cells, int ringPoints)
        {
            if (O.Length < cells)
            {
                O = new float[cells];
                H = new float[cells];
            }
            if (Ring.Length < ringPoints)
                Ring = new double2[ringPoints];
        }

        /// <summary>Starts the build: on a ThreadPool thread, or on this thread when async is false (the load's JIT pre-warm).</summary>
        public void Start(bool async)
        {
            Volatile.Write(ref m_State, 1);
            if (!async || !ThreadPool.QueueUserWorkItem(s_Run, this))
                Run();
        }

        public void Release() => Volatile.Write(ref m_State, 0);

        private static readonly WaitCallback s_Run = o => ((SkiSlopeContourBuilder)o).Run();

        private void Run()
        {
            try
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                SkiSlopeContours c = m_Contours;
                c.Disk = false;
                c.ClipRing = RingCount >= 3 ? Ring : null;
                c.ClipCount = RingCount;
                // Thin strips first; over MaxFill, fewer sub-rows (the edge steps grow, the outline still covers 2 sub-rows).
                for (int subs = 4; ; subs /= 2)
                {
                    c.SubRows = subs;
                    c.Build(O, H, Nx, Nz, Ox, Oz, Step, Eu);
                    if (c.Fill.Count <= MaxFill || subs <= 1)
                        break;
                }
                SubRowsUsed = c.SubRows;
                int nf = Math.Min(c.Fill.Count, MaxFill), nc = Math.Min(c.Curves.Count, MaxCurves);
                Capped = nf < c.Fill.Count || nc < c.Curves.Count;
                if (Fill.Length < nf)
                    Fill = new ContourFill[Math.Max(nf, 2 * Fill.Length)];
                if (Curves.Length < nc)
                    Curves = new ContourCurve[Math.Max(nc, 2 * Curves.Length)];
                for (int k = 0; k < nf; k++)
                {
                    SkiSlopeContours.FillRun r = c.Fill[k];
                    Fill[k] = new ContourFill
                    {
                        X0 = (float)r.X0, X1 = (float)r.X1, Z = (float)r.Z, Width = r.Width, Y = r.Y, YLo = r.YLo, YHi = r.YHi, Key = r.Key,
                    };
                }
                for (int k = 0; k < nc; k++)
                {
                    SkiSlopeContours.Curve v = c.Curves[k];
                    Curves[k] = new ContourCurve { P0 = v.P0, P1 = v.P1, P2 = v.P2, P3 = v.P3, YLo = v.YLo, YHi = v.YHi, Key = v.KeyHi };
                }
                FillCount = nf;
                CurveCount = nc;
                Error = null;
                BuildMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
            catch (Exception e)
            {
                Error = e;
                FillCount = CurveCount = 0;
            }
            Volatile.Write(ref m_State, 2);
        }
    }

    /// <summary>Writes the contour fill and outlines into the overlay buffer (Burst). Reads only our own NativeArrays.</summary>
    [BurstCompile]
    internal struct ContourDrawJob : IJob
    {
        public const int FadeLevels = 4;

        [ReadOnly] public NativeArray<ContourFill> Fill;
        public int FillCount;
        [ReadOnly] public NativeArray<ContourCurve> Curves;
        public int CurveCount;
        [ReadOnly] public NativeArray<UnityEngine.Color> FillColors, LineColors;
        public float LineWidth;
        public bool Disk;
        public float2 Center;
        public float Radius, FadeM;
        public bool TightBoxes;
        public OverlayRenderSystem.Buffer Overlay;

        private float m_Lo, m_Hi;

        public void Execute()
        {
            m_Lo = float.MaxValue;
            m_Hi = float.MinValue;
            int nf = math.min(FillCount, Fill.Length), nc = math.min(CurveCount, Curves.Length);
            for (int k = 0; k < nf; k++)
            {
                ContourFill f = Fill[k];
                if (f.Key >= FillColors.Length)
                    continue;
                if (!Disk)
                {
                    Strip(f, f.X0, f.X1, FadeLevels);
                    continue;
                }
                float dz = f.Z - Center.y, r2 = Radius * Radius - dz * dz;
                if (r2 <= 0f)
                    continue;
                // Ring boundaries left to right: outer rim .. innermost fade ring, then mirrored. Interval i has fade level 1, 2, 3, 4, 3, 2, 1.
                float x0 = math.max(f.X0, Center.x - math.sqrt(r2)), x1 = math.min(f.X1, Center.x + math.sqrt(r2));
                if (!(x1 - x0 > 1e-3f))
                    continue;
                float prev = Center.x - math.sqrt(r2);
                for (int i = 1; i <= 2 * FadeLevels - 1; i++)
                {
                    float b;
                    if (i < FadeLevels)
                        b = Center.x - HalfWidth(Radius - FadeM * i / FadeLevels, dz);
                    else if (i == FadeLevels)
                        b = Center.x + HalfWidth(Radius - FadeM * (FadeLevels - 1) / FadeLevels, dz);
                    else
                        b = Center.x + HalfWidth(Radius - FadeM * (2 * FadeLevels - 1 - i) / FadeLevels, dz);
                    if (i == 2 * FadeLevels - 1)
                        b = Center.x + math.sqrt(r2);
                    int level = i <= FadeLevels ? i : 2 * FadeLevels - i;
                    float a = math.max(prev, x0), e = math.min(b, x1);
                    if (e - a > 1e-3f)
                        Strip(f, a, e, level);
                    prev = math.max(prev, b);
                }
            }
            for (int k = 0; k < nc; k++)
            {
                ContourCurve c = Curves[k];
                if (c.Key >= LineColors.Length)
                    continue;
                int level = FadeLevels;
                if (Disk)
                {
                    float d = math.distance(0.5f * (c.P0.xz + c.P3.xz), Center);
                    if (d > Radius)
                        continue;
                    level = FadeLevel(Radius - d);
                }
                UnityEngine.Color col = LineColors[c.Key];
                col.a *= (float)level / FadeLevels;
                var curve = new Bezier4x3(c.P0, c.P1, c.P2, c.P3);
                var flags = OverlayRenderSystem.StyleFlags.Projected;
                if (TightBoxes && c.YHi >= c.YLo)
                {
                    BufferView(c.YLo, c.YHi).DrawCurve(col, col, 0f, flags, curve, LineWidth, new float2(1f, 1f));
                    m_Lo = math.min(m_Lo, c.YLo);
                    m_Hi = math.max(m_Hi, c.YHi);
                }
                else
                    Overlay.DrawCurve(col, col, 0f, flags, curve, LineWidth, new float2(1f, 1f));
            }
            // FitBox sets bounds.max.y = scaleY, the height and not the top (SkiSlopeOverlaySystem.StripDrawJob): add the real top.
            if (TightBoxes && m_Hi >= m_Lo)
            {
                SkiSlopeOverlaySystem.BufferView v = UnsafeUtility.As<OverlayRenderSystem.Buffer, SkiSlopeOverlaySystem.BufferView>(ref Overlay);
                OverlayRenderSystem.BoundsData bd = v.Bounds.value;
                bd.m_CurveBounds.min.y = math.min(bd.m_CurveBounds.min.y, m_Lo);
                bd.m_CurveBounds.max.y = math.max(bd.m_CurveBounds.max.y, m_Hi);
                v.Bounds.value = bd;
            }
        }

        private static float HalfWidth(float r, float dz)
        {
            float q = r * r - dz * dz;
            return q > 0f ? math.sqrt(q) : 0f;
        }

        private int FadeLevel(float edgeM)
        {
            if (!(FadeM > 0f) || edgeM >= FadeM)
                return FadeLevels;
            int l = (int)math.floor(edgeM / FadeM * FadeLevels) + 1;
            return math.clamp(l, 1, FadeLevels);
        }

        private OverlayRenderSystem.Buffer BufferView(float yLo, float yHi)
        {
            SkiSlopeOverlaySystem.BufferView v = UnsafeUtility.As<OverlayRenderSystem.Buffer, SkiSlopeOverlaySystem.BufferView>(ref Overlay);
            return new OverlayRenderSystem.Buffer(v.ProjectedCurves, v.AbsoluteCurves, v.CustomMeshes, v.TextMeshes, v.Bounds, yLo, yHi - yLo);
        }

        private void Strip(ContourFill f, float x0, float x1, int level)
        {
            UnityEngine.Color col = FillColors[f.Key];
            col.a *= (float)level / FadeLevels;
            var seg = new Line3.Segment(new float3(x0, f.Y, f.Z), new float3(x1, f.Y, f.Z));
            var flags = OverlayRenderSystem.StyleFlags.Projected;
            if (TightBoxes && f.YHi >= f.YLo)
            {
                BufferView(f.YLo, f.YHi).DrawLine(col, col, 0f, flags, seg, f.Width, new float2(0f, 0f));
                m_Lo = math.min(m_Lo, f.YLo);
                m_Hi = math.max(m_Hi, f.YHi);
            }
            else
                Overlay.DrawLine(col, col, 0f, flags, seg, f.Width, new float2(0f, 0f));
        }
    }
}
