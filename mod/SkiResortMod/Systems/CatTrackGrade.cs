// Pure half of the Cat Track Grading tool: Unity.Mathematics only, so tests/GradeBatch links this file and proves every rule headless.
// The engine half is CatTrackGradeToolSystem (input, overlay, guards' data, apply) and HeightmapWriter (the base heightmap read / write).
// One solve per change, never per frame. The order is: GradePath.Build, then GradeProfile.Solve, then GradeRaster.Build, then
// GradeGuards.Check. GradeRibbon.Build adds the flat top's outline (= the cat-track run the same action creates). GradeUndo holds the
// pre-images.
// Every profile step stays G-limited (|dy/ds| <= G).
// Height encoding: the base heightmap is R16_UNorm. GradeMap holds the mapping between world and texel.
// Engine notes: EngineNotes/CatTrackGrade.md

using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public enum GradeProfileMode : byte
    {
        FollowGround,
        ConstantGrade,
    }

    /// <summary>The solve's knobs (settings-adjustable).</summary>
    public sealed class GradeParams
    {
        public const float DefaultMaxGrade = 0.25f;
        /// <summary>20 m = 2.5x the 8 m of the first build. The 8 m graded visibly but was too narrow in the field (2026-10-24). The flat
        /// top is also the cat-track run's outline (GradeRibbon), so this is the run's width.</summary>
        public const float DefaultTopWidthM = 20f;
        public const float MinTopWidthM = 8f, MaxTopWidthM = 32f;
        public const float DefaultCutRun = 1.5f;
        public const float DefaultFillRun = 2f;

        /// <summary>The narrow full-cut profile (<see cref="NarrowFullCut"/>, DECISIONS 443): a ski-way bench cut into a steep face. The
        /// default top width is 10 m (a caller may ask for NarrowMinTopWidthM..NarrowMaxTopWidthM). The cut bank is 1:1 and there is no
        /// fill.</summary>
        public const float NarrowTopWidthM = 10f;
        public const float NarrowMinTopWidthM = 8f, NarrowMaxTopWidthM = 12f;
        public const float NarrowCutRun = 1f;

        public float MaxGrade = DefaultMaxGrade;
        public float TopWidthM = DefaultTopWidthM;
        public float CutRun = DefaultCutRun;
        public float FillRun = DefaultFillRun;
        public GradeProfileMode Mode = GradeProfileMode.FollowGround;
        /// <summary>Full cut, no fill (the narrow profile). The profile is the highest G-limited line at or below the ground (a cut-only
        /// bench). The flat top is cut where the ground is above it. The flat top is left at the ground where the ground falls away (the
        /// tread then ends at the natural edge, and GradeRaster.UnfilledTopTexels says how much of the top is missing). The downhill side
        /// slope is the ground itself. Nothing is ever raised. A caller solves a full-cut bench against GradeProfile.LowEdgeGround (the
        /// lowest ground across the top). This keeps the whole top in cut: the bench sinks into the hill instead of being filled. Default
        /// false: every other profile is exactly as before. Meaningful with FollowGround. A ConstantGrade ramp stays one straight line but
        /// still raises nothing.</summary>
        public bool NoFill;
        public float SampleStepM = 2f;
        public float SmoothM = 6f;
        /// <summary>The side slopes reach at most this far beyond the flat top. Where a slope has not met the ground by then, the last
        /// BlendTexels of the reach blend into the ground. This happens on a hillside steeper than the slope, or in a very deep cut / fill.
        /// The result is a steep bank, counted and logged, never a vertical step.</summary>
        public float MaxSideM = 40f;
        /// <summary>Crossings: outside another run's outline (GradeObstacles.Runs), the bench fades into the ground over this distance
        /// before the outline. So a crossed run is met at its own ground. 15 m. A bench step of up to ~5 m (a 20 m top across a 45 %
        /// hillside) eases in at about 1:3.</summary>
        public float CrossBlendM = 15f;
        /// <summary>Inside a crossed run (DECISIONS 420, 2026-10-26): no bench. The ground keeps its own cross slope. The tool applies only
        /// the along-track correction (profile minus the ground under the centerline). The correction is clamped to InsideMaxM. It fades in
        /// from the run's outline over InsideFadeM, and it fades out sideways beyond the flat top over InsideSideFadeM. The tool lowers
        /// InsideMaxM along InsideAllowanceSteps until every crossed run keeps its rated band.</summary>
        public float InsideMaxM = 2f;
        public float InsideFadeM = 10f;
        public float InsideSideFadeM = 10f;
        public static readonly float[] InsideAllowanceSteps = { 2f, 1f, 0.5f, 0f };
        public const int BlendTexels = 3;

        /// <summary>The narrow full-cut profile for the planner's ski-ways (DECISIONS 443). The top width is <paramref name="topWidthM"/>
        /// (clamped to NarrowMinTopWidthM..NarrowMaxTopWidthM). The cut bank is 1:1, there is no fill, and the same max grade rule applies.
        /// Every other knob keeps its default.</summary>
        public static GradeParams NarrowFullCut(float maxGrade = DefaultMaxGrade, float topWidthM = NarrowTopWidthM) => new GradeParams
        {
            MaxGrade = maxGrade,
            TopWidthM = math.clamp(topWidthM, NarrowMinTopWidthM, NarrowMaxTopWidthM),
            CutRun = NarrowCutRun,
            NoFill = true,
        };

        public GradeParams Clone() => (GradeParams)MemberwiseClone();

        public string Describe() =>
            $"max grade {MaxGrade * 100f:F0} %, top {TopWidthM:F1} m, cut 1:{CutRun:F1}, " + (NoFill ? "no fill, " : $"fill 1:{FillRun:F1}, ") +
            (Mode == GradeProfileMode.FollowGround ? "follow ground" : "constant grade");
    }

    /// <summary>Base heightmap texel / world mapping and the R16 height encoding (engine notes: height encoding).</summary>
    public struct GradeMap
    {
        public float2 Offset; // TerrainSystem.playableOffset
        public float2 Area; // TerrainSystem.playableArea
        public int2 Res; // heightmap width / height
        public float HeightScale; // heightScaleOffset.x
        public float HeightOffset; // heightScaleOffset.y

        public float2 Cell => Area / (float2)Res;
        public float StepM => HeightScale / 65535f;

        public float2 TexelCenter(int i, int j) => Offset + (new float2(i, j) + 0.5f) * Cell;

        /// <summary>Continuous texel coordinates (integer = a texel center).</summary>
        public float2 ToTexel(float2 xz) => (xz - Offset) / Cell - 0.5f;

        public float ToHeight(ushort v) => HeightOffset + v * (HeightScale / 65535f);

        public ushort ToU16(float h)
        {
            float v = math.round((h - HeightOffset) / HeightScale * 65535f);
            return (ushort)math.clamp(v, 0f, 65535f);
        }

        public bool Valid => Res.x > 0 && Res.y > 0 && Area.x > 0f && Area.y > 0f && HeightScale > 0f;
    }

    /// <summary>The resampled centerline (pipeline 1).</summary>
    public sealed class GradePath
    {
        public float2[] Xz;
        public float[] S;
        public float LengthM;
        public float StepM;

        public int Count => Xz.Length;

        /// <summary>Builds the chain through <paramref name="points"/> (>= 2 points, consecutive duplicates dropped). Returns null when
        /// there are fewer than two distinct points or a zero-length path.</summary>
        public static GradePath Build(IList<float2> points, float step)
        {
            var pts = new List<float2>();
            for (int i = 0; i < points.Count; i++)
                if (pts.Count == 0 || math.distancesq(pts[pts.Count - 1], points[i]) > 0.01f)
                    pts.Add(points[i]);
            if (pts.Count < 2 || step <= 0f)
                return null;

            const int Sub = 24;
            var dense = new List<float2>(pts.Count * Sub);
            dense.Add(pts[0]);
            for (int k = 0; k < pts.Count - 1; k++)
            {
                float2 p0 = pts[math.max(k - 1, 0)], p1 = pts[k], p2 = pts[k + 1], p3 = pts[math.min(k + 2, pts.Count - 1)];
                float2 c1 = p1 + (p2 - p0) / 6f, c2 = p2 - (p3 - p1) / 6f;
                for (int t = 1; t <= Sub; t++)
                    dense.Add(Bezier(p1, c1, c2, p2, t / (float)Sub));
            }
            var cum = new float[dense.Count];
            for (int i = 1; i < dense.Count; i++)
                cum[i] = cum[i - 1] + math.distance(dense[i - 1], dense[i]);
            float length = cum[cum.Length - 1];
            if (length < 0.5f)
                return null;

            int n = math.max(1, (int)math.ceil(length / step));
            float ds = length / n;
            var path = new GradePath { Xz = new float2[n + 1], S = new float[n + 1], LengthM = length, StepM = ds };
            int j = 0;
            for (int i = 0; i <= n; i++)
            {
                float s = i == n ? length : i * ds;
                while (j < cum.Length - 2 && cum[j + 1] < s)
                    j++;
                float seg = cum[j + 1] - cum[j];
                float u = seg > 1e-6f ? math.saturate((s - cum[j]) / seg) : 0f;
                path.Xz[i] = math.lerp(dense[j], dense[j + 1], u);
                path.S[i] = s;
            }
            return path;
        }

        private static float2 Bezier(float2 a, float2 b, float2 c, float2 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        /// <summary>Distance (xz) from <paramref name="p"/> to the centerline and the arc length of the nearest point. Brute force:
        /// for a handful of queries (picking, tree clearing), not for rasterizing.</summary>
        public float Nearest(float2 p, out float along, out int segment)
        {
            float best = float.PositiveInfinity;
            along = 0f;
            segment = 0;
            for (int i = 0; i < Xz.Length - 1; i++)
            {
                float d = SegDistance(p, Xz[i], Xz[i + 1], out float u);
                if (d < best)
                {
                    best = d;
                    along = math.lerp(S[i], S[i + 1], u);
                    segment = i;
                }
            }
            return best;
        }

        public static float SegDistance(float2 p, float2 a, float2 b, out float u)
        {
            float2 ab = b - a;
            float l2 = math.lengthsq(ab);
            u = l2 > 1e-8f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
            return math.distance(p, a + u * ab);
        }
    }

    /// <summary>The solved profile (pipeline 2).</summary>
    public sealed class GradeProfile
    {
        public float[] Ground;
        public float[] Y;
        /// <summary>Grade of each sample step (Y[i+1] - Y[i]) / ds. Length = samples - 1.</summary>
        public float[] SegGrade;
        public float MaxGradeAbs;
        public float AvgGradeAbs;
        public float MaxCutM, MaxFillM;
        public bool Feasible;
        /// <summary>Extra path length the grade limit needs between the two ends (0 when feasible).</summary>
        public float ShortfallM;

        /// <summary><paramref name="pinTol"/> (null = none): per sample, NaN = free. Otherwise the profile stays within that many meters of
        /// the ground there. 0 = held to the ground like the two ends: a crossing's fade band. The tolerance goes up to InsideMaxM inside a
        /// crossed run (GradeRaster.PinTolerance). FollowGround only: a constant grade is one straight ramp by definition. The
        /// final-surface guard (GradeRaster.AlongTrackSteepSample) is its only protection.</summary>
        public static GradeProfile Solve(GradePath path, float[] ground, GradeParams p, float[] pinTol = null)
        {
            int n = path.Count;
            if (ground == null || ground.Length != n)
                throw new ArgumentException("ground must have one height per path sample");
            float g = math.max(1e-4f, p.MaxGrade);
            float ds = path.StepM;
            float L = path.LengthM;
            float g0 = ground[0], gn = ground[n - 1];
            var y = new float[n];

            if (p.Mode == GradeProfileMode.ConstantGrade)
            {
                for (int i = 0; i < n; i++)
                    y[i] = g0 + (gn - g0) * (L > 0f ? path.S[i] / L : 0f);
            }
            else
            {
                float lip = g * ds;
                var up = new float[n];
                var lo = new float[n];
                for (int i = 0; i < n; i++)
                {
                    up[i] = i == 0 ? ground[i] : math.max(ground[i], up[i - 1] - lip);
                    lo[i] = i == 0 ? ground[i] : math.min(ground[i], lo[i - 1] + lip);
                }
                for (int i = n - 2; i >= 0; i--)
                {
                    up[i] = math.max(up[i], up[i + 1] - lip);
                    lo[i] = math.min(lo[i], lo[i + 1] + lip);
                }
                // NoFill: the highest G-limited line at or below the ground (lo), not the midpoint of the two envelopes.
                for (int i = 0; i < n; i++)
                    y[i] = p.NoFill ? lo[i] : 0.5f * (up[i] + lo[i]);

                int k =(int)math.floor(p.SmoothM / math.max(ds, 1e-3f));
                if (k > 0)
                {
                    // Moving average, the profile reflected point-wise about each end (y(-t) = 2 y(0) - y(t)): a straight ramp stays exact, and
                    // every term is a G-limited profile, so the average is too.
                    var sm = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        float acc = 0f;
                        for (int t = -k; t <= k; t++)
                            acc += Reflected(y, i + t);
                        sm[i] = acc / (2 * k + 1);
                    }
                    y = sm;
                    // NoFill: the average of lo can rise above the ground. The min with lo (G-limited, at or below the ground) stays
                    // G-limited.
                    if (p.NoFill)
                        for (int i = 0; i < n; i++)
                            y[i] = math.min(y[i], lo[i]);
                }

                // Pin both ends to the ground with slope cones: slope G, or the slope the two ends force when that is steeper. The steeper
                // case is infeasible: both ends still meet the ground and the steep part shows red.
                float gc = math.max(g, L > 0f ? math.abs(gn - g0) / L * 1.0001f : g);
                for (int i = 0; i < n; i++)
                {
                    float s = path.S[i];
                    y[i] = math.clamp(y[i], g0 - gc * s, g0 + gc * s);
                    y[i] = math.clamp(y[i], gn - gc * (L - s), gn + gc * (L - s));
                }

                // Held samples (crossings): cones of slope G from each band [ground - tol, ground + tol]. hi / lo are min / max of
                // G-Lipschitz cones (two sweeps, O(n)), so clamping a G-limited profile between them keeps it G-limited. Where two bands
                // disagree by more than the grade allows (lo > hi), the midpoint is taken and the surface guard refuses.
                if (pinTol != null && pinTol.Length == n)
                {
                    var pinHi = new float[n];
                    var pinLo = new float[n];
                    bool any = false;
                    for (int i = 0; i < n; i++)
                    {
                        bool held = !float.IsNaN(pinTol[i]);
                        float tol = held ? math.max(0f, pinTol[i]) : 0f;
                        pinHi[i] = held ? ground[i] + (p.NoFill ? 0f : tol) : float.PositiveInfinity;   // NoFill: a held sample may go down, never up
                        pinLo[i] = held ? ground[i] - tol : float.NegativeInfinity;
                        any |= held;
                    }
                    if (any)
                    {
                        for (int i = 1; i < n; i++)
                        {
                            float d = g * (path.S[i] - path.S[i - 1]);
                            pinHi[i] = math.min(pinHi[i], pinHi[i - 1] + d);
                            pinLo[i] = math.max(pinLo[i], pinLo[i - 1] - d);
                        }
                        for (int i = n - 2; i >= 0; i--)
                        {
                            float d = g * (path.S[i + 1] - path.S[i]);
                            pinHi[i] = math.min(pinHi[i], pinHi[i + 1] + d);
                            pinLo[i] = math.max(pinLo[i], pinLo[i + 1] - d);
                        }
                        for (int i = 0; i < n; i++)
                            y[i] = pinLo[i] > pinHi[i] ? 0.5f * (pinLo[i] + pinHi[i]) : math.clamp(y[i], pinLo[i], pinHi[i]);
                    }
                }
            }

            var prof = new GradeProfile { Ground = ground, Y = y, SegGrade = new float[n - 1] };
            float sum = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                float d = path.S[i + 1] - path.S[i];
                float gr = d > 1e-6f ? (y[i + 1] - y[i]) / d : 0f;
                prof.SegGrade[i] = gr;
                prof.MaxGradeAbs = math.max(prof.MaxGradeAbs, math.abs(gr));
                sum += math.abs(gr);
            }
            prof.AvgGradeAbs = n > 1 ? sum / (n - 1) : 0f;
            for (int i = 0; i < n; i++)
            {
                prof.MaxCutM = math.max(prof.MaxCutM, ground[i] - y[i]);
                prof.MaxFillM = math.max(prof.MaxFillM, y[i] - ground[i]);
            }
            float need = math.abs(gn - g0) / g;
            prof.ShortfallM = math.max(0f, need - L);
            prof.Feasible = prof.ShortfallM <= 0f && prof.MaxGradeAbs <= g + 1e-3f;
            return prof;
        }

        /// <summary>The ground a NoFill (full-cut) bench is solved against: per path sample, the LOWEST ground across the flat top (the
        /// centerline and the offsets at +-0.5 and +-1 half widths). A bench at or below it is in cut across its whole width. So on a cross
        /// slope it sinks into the hill by up to slope x width instead of being filled on the downhill side. Pass the result as
        /// <c>ground</c> to <see cref="Solve"/> together with <see cref="GradeParams.NarrowFullCut"/>. The profile is then the highest
        /// G-limited line under that envelope, so the max grade rule holds on the bench itself.</summary>
        public static float[] LowEdgeGround(GradePath path, Func<float2, float> groundAt, float halfWidth)
        {
            int n = path.Count;
            var g = new float[n];
            for (int i = 0; i < n; i++)
            {
                float2 t = math.normalizesafe(path.Xz[math.min(i + 1, n - 1)] - path.Xz[math.max(i - 1, 0)], new float2(1f, 0f));
                float2 side = new float2(-t.y, t.x) * halfWidth;
                float low = groundAt(path.Xz[i]);
                low = math.min(low, groundAt(path.Xz[i] + side));
                low = math.min(low, groundAt(path.Xz[i] - side));
                low = math.min(low, groundAt(path.Xz[i] + 0.5f * side));
                low = math.min(low, groundAt(path.Xz[i] - 0.5f * side));
                g[i] = low;
            }
            return g;
        }

        private static float Reflected(float[] y, int i)
        {
            int n = y.Length;
            if (i < 0)
                return 2f * y[0] - y[math.min(-i, n - 1)];
            if (i > n - 1)
                return 2f * y[n - 1] - y[math.max(2 * (n - 1) - i, 0)];
            return y[i];
        }

        /// <summary>The grade class of a step for the overlay: 0 within the limit, 1 up to 5 points over, 2 beyond.</summary>
        public static int GradeClass(float gradeAbs, float maxGrade) =>
            gradeAbs <= maxGrade + 0.005f ? 0 : gradeAbs <= maxGrade + 0.05f ? 1 : 2;
    }

    /// <summary>The rasterized corridor over one heightmap rect (pipeline 3). Heights are world meters (floats). The engine half encodes
    /// them (GradeMap.ToU16), and only texels whose code changes are counted / written.</summary>
    public sealed class GradeRaster
    {
        /// <summary>Largest rect (texels) the tool will rasterize. Build holds 17 bytes per texel (four float arrays and the zone byte).
        /// The main thread samples the terrain once per texel on every edit. 2^20 texels (a 3.6 km square at the vanilla 3.5 m texel) is
        /// about 18 MB and a fraction of a second. 2048 x 2048 would be 71 MB before the apply's readbacks.</summary>
        public const int MaxTexels = 1 << 20;

        /// <summary>True when the rect is over <see cref="MaxTexels"/>. The long product avoids int overflow on a degenerate
        /// rect.</summary>
        public static bool TooLarge(int4 rect) => (long)rect.z * rect.w > MaxTexels;

        public int X0, Z0, W, H;
        public float[] Before;
        public float[] After;
        /// <summary>Per texel: 0 untouched, 1 side slope, 2 flat top.</summary>
        public byte[] Zone;
        public int Changed;
        public float CutM3, FillM3;
        public float MaxCutM, MaxFillM;
        public float TexelAreaM2;
        /// <summary>Texels in the blend band that still moved more than 1 m (the side slope did not meet the ground: a steep bank).</summary>
        public int SteepBankTexels;
        /// <summary>Texels inside a crossed run's outline left as they were, and those given the inside correction (GradeParams.InsideMaxM)
        /// with its largest size.</summary>
        public int KeptTexels, InsideTexels;
        public float MaxInsideM;
        public float MaxBankStepM;
        /// <summary>Texels within the flat top's half width of the centerline, and (NoFill only) those of them left at the ground because
        /// it lies more than half a height step below the bench, with the largest such drop. UnfilledTopTexels / TopTexels is the part of
        /// the top that is not tread. The planner offsets the centerline into the hill until it is near 0.</summary>
        public int TopTexels, UnfilledTopTexels;
        public float MaxUnfilledM;

        public int Index(int i, int j) => (j - Z0) * W + (i - X0);

        /// <summary>The rect the corridor can touch: the samples' box grown by the flat top and MaxSideM (the side slopes' reach), clamped
        /// to the map. A sidehill needs the full reach even where the centerline sits on the ground. False when it misses the
        /// map.</summary>
        public static bool Bounds(GradeMap map, GradePath path, GradeProfile prof, GradeParams p, out int4 rect, out float reach)
        {
            float hw = 0.5f * p.TopWidthM;
            reach = hw + p.MaxSideM;
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            for (int i = 0; i < path.Count; i++)
            {
                mn = math.min(mn, path.Xz[i]);
                mx = math.max(mx, path.Xz[i]);
            }
            float2 a = map.ToTexel(mn - reach), b = map.ToTexel(mx + reach);
            int x0 = math.max(0, (int)math.floor(a.x)), z0 = math.max(0, (int)math.floor(a.y));
            int x1 = math.min(map.Res.x - 1, (int)math.ceil(b.x)), z1 = math.min(map.Res.y - 1, (int)math.ceil(b.y));
            rect = new int4(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            return x1 >= x0 && z1 >= z0;
        }

        /// <summary>Rasterize. <paramref name="baseHeight"/> = the base height of texel (i, j) in world meters (the engine half reads the
        /// base rect, and tests pass a function). <paramref name="keep"/> = the outlines of other runs the track may cross (null = none).
        /// Inside one, the ground is kept. Within CrossBlendM outside it, the template fades into the ground (KeepWeight).</summary>
        public static GradeRaster Build(GradeMap map, int4 rect, GradePath path, GradeProfile prof, GradeParams p, float reach,
                                        Func<int, int, float> baseHeight, IList<float2[]> keep = null)
        {
            float4[] keepBox = KeepBoxes(keep, p.CrossBlendM);
            bool[] keepFade = EnteredOutlines(path, keep);
            var r = new GradeRaster { X0 = rect.x, Z0 = rect.y, W = rect.z, H = rect.w };
            int count = r.W * r.H;
            r.Before = new float[count];
            r.After = new float[count];
            r.Zone = new byte[count];
            r.TexelAreaM2 = map.Cell.x * map.Cell.y;
            var bestD2 = new float[count];
            var bestY = new float[count];
            var bestG = new float[count];   // the ground under the nearest centerline point (the inside correction is bestY - bestG)
            for (int t = 0; t < count; t++)
                bestD2[t] = float.PositiveInfinity;
            for (int j = 0; j < r.H; j++)
                for (int i = 0; i < r.W; i++)
                    r.Before[j * r.W + i] = baseHeight(r.X0 + i, r.Z0 + j);

            float reach2 = reach * reach;
            for (int s = 0; s < path.Count - 1; s++)
            {
                float2 a = path.Xz[s], b = path.Xz[s + 1];
                float2 lo = map.ToTexel(math.min(a, b) - reach), hi = map.ToTexel(math.max(a, b) + reach);
                int i0 = math.max(r.X0, (int)math.floor(lo.x)), i1 = math.min(r.X0 + r.W - 1, (int)math.ceil(hi.x));
                int j0 = math.max(r.Z0, (int)math.floor(lo.y)), j1 = math.min(r.Z0 + r.H - 1, (int)math.ceil(hi.y));
                float ya = prof.Y[s], yb = prof.Y[s + 1], ga = prof.Ground[s], gb = prof.Ground[s + 1];
                for (int j = j0; j <= j1; j++)
                {
                    for (int i = i0; i <= i1; i++)
                    {
                        float2 c = map.TexelCenter(i, j);
                        float d = GradePath.SegDistance(c, a, b, out float u);
                        float d2 = d * d;
                        if (d2 > reach2)
                            continue;
                        int t = (j - r.Z0) * r.W + (i - r.X0);
                        if (d2 < bestD2[t])
                        {
                            bestD2[t] = d2;
                            bestY[t] = math.lerp(ya, yb, u);
                            bestG[t] = math.lerp(ga, gb, u);
                        }
                    }
                }
            }

            float hw = 0.5f * p.TopWidthM;
            float step = map.StepM;
            float blendM = GradeParams.BlendTexels * math.cmax(map.Cell);
            float blendStart = math.max(hw, reach - blendM);
            for (int t = 0; t < count; t++)
            {
                float before = r.Before[t];
                if (float.IsInfinity(bestD2[t]))
                {
                    r.After[t] = before;
                    continue;
                }
                float d = math.sqrt(bestD2[t]);
                float y = bestY[t];
                float h;
                byte zone;
                if (d <= hw)
                {
                    r.TopTexels++;
                    h = p.NoFill ? math.min(before, y) : y;   // NoFill: cut where the ground is above the bench, the ground itself where it falls away
                    if (p.NoFill && before - y < -0.5f * step)
                    {
                        r.UnfilledTopTexels++;
                        r.MaxUnfilledM = math.max(r.MaxUnfilledM, y - before);
                    }
                    zone = 2;
                }
                else
                {
                    float out_ = d - hw;
                    h = before > y ? math.min(before, y + out_ / p.CutRun) : (p.NoFill ? before : math.max(before, y - out_ / p.FillRun));
                    zone = 1;
                    if (d > blendStart)
                    {
                        // The slope has not met the ground this close to the reach: blend into the ground (MaxSideM).
                        float k = math.saturate((d - blendStart) / math.max(1e-3f, reach - blendStart));
                        float bank = math.abs(h - before);
                        if (bank > 1f)
                        {
                            r.SteepBankTexels++;
                            r.MaxBankStepM = math.max(r.MaxBankStepM, bank);
                        }
                        h = math.lerp(h, before, k);
                    }
                }
                if (keepBox != null)
                {
                    float2 c = map.TexelCenter(r.X0 + t % r.W, r.Z0 + t / r.W);
                    float w = KeepWeight(c, keep, keepBox, p.CrossBlendM, keepFade);
                    if (w >= 1f)
                    {
                        // Inside a crossed run: no bench. Its ground plus the along-track correction, clamped and faded (InsideMaxM).
                        float depth = InsideDepth(c, keep, keepBox);
                        float side = d <= hw ? 1f : math.saturate(1f - (d - hw) / math.max(1e-3f, p.InsideSideFadeM));
                        float fadeIn = math.saturate(depth / math.max(1e-3f, p.InsideFadeM));
                        float corr = math.clamp(y - bestG[t], -p.InsideMaxM, p.InsideMaxM) * fadeIn * side;
                        if (p.NoFill)
                            corr = math.min(corr, 0f);
                        if (math.abs(corr) < 0.5f * step)
                        {
                            r.After[t] = before;   // untouched (zone 0: no guard, no count)
                            r.KeptTexels++;
                            continue;
                        }
                        h = before + corr;
                        zone = d <= hw ? (byte)2 : (byte)1;
                        r.InsideTexels++;
                        r.MaxInsideM = math.max(r.MaxInsideM, math.abs(corr));
                    }
                    else
                        h = math.lerp(h, before, w);
                }
                float dh = h - before;
                if (math.abs(dh) < 0.5f * step)
                {
                    r.After[t] = before;
                    if (zone == 2)
                        r.Zone[t] = 2;
                    continue;
                }
                r.After[t] = h;
                r.Zone[t] = zone;
                r.Changed++;
                if (dh < 0f)
                {
                    r.CutM3 += -dh * r.TexelAreaM2;
                    r.MaxCutM = math.max(r.MaxCutM, -dh);
                }
                else
                {
                    r.FillM3 += dh * r.TexelAreaM2;
                    r.MaxFillM = math.max(r.MaxFillM, dh);
                }
            }
            return r;
        }

        /// <summary>Per keep outline its box grown by <paramref name="blendM"/> (x0, z0, x1, z1). Null when there is none.</summary>
        public static float4[] KeepBoxes(IList<float2[]> keep, float blendM)
        {
            if (keep == null || keep.Count == 0)
                return null;
            var boxes = new float4[keep.Count];
            for (int k = 0; k < keep.Count; k++)
            {
                float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
                float2[] poly = keep[k];
                for (int i = 0; poly != null && i < poly.Length; i++)
                {
                    mn = math.min(mn, poly[i]);
                    mx = math.max(mx, poly[i]);
                }
                float g = math.max(0f, blendM);
                boxes[k] = poly != null && poly.Length >= 3 ? new float4(mn - g, mx + g) : new float4(1f, 1f, -1f, -1f);   // empty box
            }
            return boxes;
        }

        /// <summary>Per keep outline: does the path enter it (any sample inside)? Only those outlines fade the bench into the ground. A run
        /// the track merely runs beside keeps its own ground but never tilts the flat top (review fix, 2026-10-25). Null for no
        /// keep.</summary>
        public static bool[] EnteredOutlines(GradePath path, IList<float2[]> keep)
        {
            if (keep == null || keep.Count == 0 || path == null)
                return null;
            var entered = new bool[keep.Count];
            for (int k = 0; k < keep.Count; k++)
            {
                float2[] poly = keep[k];
                for (int i = 0; poly != null && poly.Length >= 3 && i < path.Count && !entered[k]; i++)
                    entered[k] = Inside(poly, path.Xz[i]);
            }
            return entered;
        }

        /// <summary>The profile's tolerance per path sample (null = no crossing). NaN = free. 0 in the fade band before an entered outline
        /// (the template equals the ground wherever the fade acts, so the surface there is the ground's own and the G-limited solver
        /// carries the approach). Inside an outline: InsideMaxM x the inward fade (InsideDepth / InsideFadeM), matching the raster's
        /// correction.</summary>
        public static float[] PinTolerance(GradePath path, IList<float2[]> keep, GradeParams p)
        {
            if (keep == null || keep.Count == 0 || path == null)
                return null;
            float4[] boxes = KeepBoxes(keep, p.CrossBlendM);
            bool[] fade = EnteredOutlines(path, keep);
            var tol = new float[path.Count];
            bool any = false;
            for (int i = 0; i < path.Count; i++)
            {
                float w = KeepWeight(path.Xz[i], keep, boxes, p.CrossBlendM, fade);
                if (w <= 0f)
                {
                    tol[i] = float.NaN;
                    continue;
                }
                any = true;
                tol[i] = w >= 1f
                    ? p.InsideMaxM * math.saturate(InsideDepth(path.Xz[i], keep, boxes) / math.max(1e-3f, p.InsideFadeM))
                    : 0f;
            }
            return any ? tol : null;
        }

        /// <summary>How deep <paramref name="c"/> lies inside the keep outlines: the largest distance to the edge of an outline containing
        /// it, or -1 outside all.</summary>
        public static float InsideDepth(float2 c, IList<float2[]> keep, float4[] boxes)
        {
            float depth = -1f;
            for (int k = 0; k < keep.Count; k++)
            {
                float4 b = boxes[k];
                if (c.x < b.x || c.y < b.y || c.x > b.z || c.y > b.w)
                    continue;
                float2[] poly = keep[k];
                if (poly == null || poly.Length < 3 || !Inside(poly, c))
                    continue;
                float d = float.PositiveInfinity;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    d = math.min(d, GradePath.SegDistance(c, poly[j], poly[i], out float _));
                depth = math.max(depth, d);
            }
            return depth;
        }

        /// <summary>The largest inside allowance (GradeParams.InsideAllowanceSteps, 2 m down to 0) whose plan keeps every crossed run's
        /// band, or -1 when none does. <paramref name="keepsBands"/> builds and rates the plan at that allowance.</summary>
        public static float ChooseInsideAllowance(Func<float, bool> keepsBands)
        {
            foreach (float a in GradeParams.InsideAllowanceSteps)
                if (keepsBands(a))
                    return a;
            return -1f;
        }

        /// <summary>How much of the ground a texel at <paramref name="c"/> keeps. It is 1 inside any keep outline. Else it is 1 - d /
        /// blendM for the nearest outline edge within blendM (a linear fade) of an outline flagged in <paramref name="fade"/> (null = all
        /// fade). Else it is 0.</summary>
        public static float KeepWeight(float2 c, IList<float2[]> keep, float4[] boxes, float blendM, bool[] fade = null)
        {
            float w = 0f;
            for (int k = 0; k < keep.Count; k++)
            {
                float4 b = boxes[k];
                if (c.x < b.x || c.y < b.y || c.x > b.z || c.y > b.w)
                    continue;
                float2[] poly = keep[k];
                if (Inside(poly, c))
                    return 1f;
                if (blendM > 0f && (fade == null || fade[k]))
                {
                    float d = float.PositiveInfinity;
                    for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                        d = math.min(d, GradePath.SegDistance(c, poly[j], poly[i], out float _));
                    w = math.max(w, math.saturate(1f - d / blendM));
                }
            }
            return w;
        }

        /// <summary>Even-odd point in polygon.</summary>
        public static bool Inside(float2[] poly, float2 c)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > c.y) != (poly[j].y > c.y) &&
                    c.x < (poly[j].x - poly[i].x) * (c.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Margin of the along-track surface guard over the max grade: 1 pp (the bilinear read of the raster at the path samples
        /// shows up to 0.7 pp of noise, GradeBatch GR-21).</summary>
        public const float AlongTrackSlack = 0.01f;

        /// <summary>The terrain a crossed run is rated on (SkiRunRater.IHeightSampler). It is the raster's Before or After, bilinear, where
        /// the raster covers the point (all four texels), and <see cref="Outside"/> elsewhere. The engine half uses the CPU heights.
        /// GradeBatch uses the test ground. R null = Outside everywhere. The tool's crossed-run band check and the GradeBatch rating cases
        /// share it.</summary>
        public struct Sampler : IHeightSampler
        {
            public GradeMap Map;
            public GradeRaster R;
            public bool After;
            public Func<float3, float> Outside;

            public float Sample(float3 world)
            {
                if (R != null)
                {
                    float2 t = Map.ToTexel(world.xz);
                    int i = (int)math.floor(t.x), j = (int)math.floor(t.y);
                    if (i >= R.X0 && j >= R.Z0 && i + 1 < R.X0 + R.W && j + 1 < R.Z0 + R.H)
                        return R.SampleAt(Map, After ? R.After : R.Before, world.xz);
                }
                return Outside(world);
            }
        }

        /// <summary>The bilinear height of <paramref name="a"/> (a raster-sized array, Before or After) at the world point.</summary>
        public float SampleAt(GradeMap map, float[] a, float2 xz)
        {
            float2 t = map.ToTexel(xz);
            int i = (int)math.floor(t.x), j = (int)math.floor(t.y);
            float fx = t.x - i, fz = t.y - j;
            int i1 = math.min(i + 1, X0 + W - 1), j1 = math.min(j + 1, Z0 + H - 1);
            i = math.clamp(i, X0, X0 + W - 1);
            j = math.clamp(j, Z0, Z0 + H - 1);
            return math.lerp(math.lerp(a[Index(i, j)], a[Index(i1, j)], fx), math.lerp(a[Index(i, j1)], a[Index(i1, j1)], fx), fz);
        }

        /// <summary>The safety net behind the pins (review fix, 2026-10-25). It returns the FIRST path sample whose step to the next is
        /// steeper than <paramref name="maxGrade"/> + AlongTrackSlack. The step is measured on the FINAL surface (After, read at the path
        /// samples). Only steps where either end lies inside a kept outline or its fade band count. It returns -1 when there is none. The
        /// solver and the ground check know nothing of the fade, so this is the only test of the surface the crossing really builds (a
        /// ConstantGrade ramp, two crossings too far apart in height, a steep hillside right outside a run).</summary>
        public int AlongTrackSteepSample(GradeMap map, GradePath path, IList<float2[]> keep, float blendM, float maxGrade)
        {
            if (keep == null || keep.Count == 0 || path == null)
                return -1;
            float4[] boxes = KeepBoxes(keep, blendM);
            bool[] fade = EnteredOutlines(path, keep);
            var h = new float[path.Count];
            var w = new float[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                h[i] = SampleAt(map, After, path.Xz[i]);
                w[i] = KeepWeight(path.Xz[i], keep, boxes, blendM, fade);
            }
            for (int i = 0; i < path.Count - 1; i++)
            {
                if (w[i] <= 0f && w[i + 1] <= 0f)
                    continue;
                float ds = path.S[i + 1] - path.S[i];
                if (ds > 1e-4f && math.abs(h[i + 1] - h[i]) / ds > maxGrade + AlongTrackSlack)
                    return i;
            }
            return -1;
        }

        public string Describe() =>
            string.Format(CultureInfo.InvariantCulture, "rect ({0}, {1}) {2} x {3}, {4} texel(s) changed, cut {5:F0} m3 (max {6:F1} m), " +
                          "fill {7:F0} m3 (max {8:F1} m){9}{10}", X0, Z0, W, H, Changed, CutM3, MaxCutM, FillM3, MaxFillM,
                          SteepBankTexels > 0
                              ? string.Format(CultureInfo.InvariantCulture, ", steep bank at {0} texel(s) (up to {1:F1} m blended over the last {2} texels)",
                                              SteepBankTexels, MaxBankStepM, GradeParams.BlendTexels)
                              : string.Empty,
                          (KeptTexels + InsideTexels > 0
                              ? string.Format(CultureInfo.InvariantCulture, ", crossed runs: {0} texel(s) kept, {1} corrected (max {2:F2} m)", KeptTexels, InsideTexels, MaxInsideM)
                              : string.Empty) +
                          (UnfilledTopTexels > 0
                              ? string.Format(CultureInfo.InvariantCulture, ", no fill: {0} of {1} top texel(s) left at the ground (up to {2:F1} m below the bench)", UnfilledTopTexels, TopTexels, MaxUnfilledM)
                              : string.Empty));
    }

    public enum GradeGuardKind : byte
    {
        None,
        Lot, // a building lot (lift station, lodge, any building)
        LiftLine,
        Water,
        OffMap,
        TooLarge, // the rect is over GradeRaster.MaxTexels: refused before any raster is built
        RunShape, // the flat top's outline touches itself (GradeRibbon): it cannot become the cat-track run
        CrossingSteep, // the final surface along the track, where a crossed run or its fade reaches, is steeper than the max grade
        CrossedRunBand, // even with no correction inside, the grade would change a crossed run's rated band
    }

    /// <summary>What the engine half found in the rect (pipeline 4).</summary>
    public sealed class GradeObstacles
    {
        public struct Lot
        {
            public float2 Center, Right, Forward, Half; // Right / Forward unit. Half = lot half extents (x, z)
            /// <summary>The ground the lot holds past its rectangle (local x along Right, y along Forward), when HasWindow: the lot's
            /// cut / fill window (LotWindow). A base station's runway reaches 12 m past the lot on the line side.</summary>
            public float2 WinMin, WinMax;
            public bool HasWindow;
        }

        /// <summary>The rectangle a lot holds at its plane (local xz), from the prefab's BuildingTerraformData, as
        /// BuildingUtils.CalculateLotInfo builds it (BuildingUtils.cs:571-617): the cut window = m_Smooth grown to the flat zone (the .y of
        /// m_FlatX0 / Z0 / X1 / Z1), emptied by m_DontLower; the fill window = m_Smooth, emptied by m_DontRaise. The cut window holds the
        /// fill window, so the result is the cut window when the lot cuts, else m_Smooth when it fills, else none (block A: both flags).
        /// The engine's 8 m apron past the window is not in it: the guard never held the apron around a lot.</summary>
        public static bool LotWindow(float4 smooth, float flatX0, float flatZ0, float flatX1, float flatZ1, bool dontRaise, bool dontLower,
                                     out float2 min, out float2 max)
        {
            if (!dontLower)
            {
                min = math.min(new float2(flatX0, flatZ0), smooth.xy);
                max = math.max(new float2(flatX1, flatZ1), smooth.zw);
                return true;
            }
            min = smooth.xy;
            max = smooth.zw;
            return !dontRaise;
        }

        public struct Line
        {
            public float2 A, B;
            public float Clearance;
        }

        public readonly List<Lot> Lots = new List<Lot>();
        public readonly List<Line> Lines = new List<Line>();
        /// <summary>Texels (global i, j) the water surface covers. Empty when none.</summary>
        public readonly HashSet<int2> WetTexels = new HashSet<int2>();
        /// <summary>The outlines (xz) of the other runs in the rect: crossings keep their ground (GradeRaster.Build keep).</summary>
        public readonly List<float2[]> Runs = new List<float2[]>();

        public void Clear()
        {
            Lots.Clear();
            Lines.Clear();
            WetTexels.Clear();
            Runs.Clear();
        }
    }

    public static class GradeGuards
    {
        /// <summary>The first guard the corridor trips: every texel the grade changes, and the flat top, against the lots (grown by
        /// half a texel), the lift lines (within their clearance) and the wet texels. <paramref name="where"/> = the texel center.</summary>
        public static GradeGuardKind Check(GradeMap map, GradeRaster r, GradeObstacles obs, out float2 where)
        {
            where = default;
            float halfTexel = 0.5f * math.cmax(map.Cell);
            for (int j = 0; j < r.H; j++)
            {
                for (int i = 0; i < r.W; i++)
                {
                    if (r.Zone[j * r.W + i] == 0)
                        continue;
                    int gi = r.X0 + i, gj = r.Z0 + j;
                    float2 c = map.TexelCenter(gi, gj);
                    if (obs.WetTexels.Contains(new int2(gi, gj)))
                    {
                        where = c;
                        return GradeGuardKind.Water;
                    }
                    for (int k = 0; k < obs.Lots.Count; k++)
                    {
                        GradeObstacles.Lot lot = obs.Lots[k];
                        float2 d = c - lot.Center;
                        float2 local = new float2(math.dot(d, lot.Right), math.dot(d, lot.Forward));
                        bool inLot = math.all(math.abs(local) <= lot.Half + halfTexel);
                        bool inWindow = lot.HasWindow && math.all(local >= lot.WinMin - halfTexel) && math.all(local <= lot.WinMax + halfTexel);
                        if (inLot || inWindow)
                        {
                            where = c;
                            return GradeGuardKind.Lot;
                        }
                    }
                    for (int k = 0; k < obs.Lines.Count; k++)
                    {
                        GradeObstacles.Line line = obs.Lines[k];
                        if (GradePath.SegDistance(c, line.A, line.B, out float _) <= line.Clearance)
                        {
                            where = c;
                            return GradeGuardKind.LiftLine;
                        }
                    }
                }
            }
            return GradeGuardKind.None;
        }

        /// <summary>The player text of a refusal (a LocaleEN SkiResort.CatTrack.* key; GradeMsg.Empty for none).</summary>
        public static GradeMsg Reason(GradeGuardKind k)
        {
            switch (k)
            {
                case GradeGuardKind.TooLarge: return GradeMsg.Of("GUARD_TOO_LARGE");
                case GradeGuardKind.RunShape: return GradeMsg.Of("GUARD_RUN_SHAPE");
                case GradeGuardKind.CrossingSteep: return GradeMsg.Of("GUARD_CROSSING_STEEP");
                case GradeGuardKind.CrossedRunBand: return GradeMsg.Of("GUARD_CROSSED_RUN_BAND");
                case GradeGuardKind.Lot: return GradeMsg.Of("GUARD_LOT");
                case GradeGuardKind.LiftLine: return GradeMsg.Of("GUARD_LIFT_LINE");
                case GradeGuardKind.Water: return GradeMsg.Of("GUARD_WATER");
                case GradeGuardKind.OffMap: return GradeMsg.Of("GUARD_OFF_MAP");
                default: return GradeMsg.Empty;
            }
        }
    }

    public enum GradeRibbonFail : byte
    {
        None,
        TooShort, // fewer than two samples per edge survive
        Crosses, // two non-adjacent outline edges cross or come closer than MinClearanceM (switchback legs too close for the width)
    }

    /// <summary>The cat-track run's outline (pipeline 6): the flat top's two edges as one closed ring, left edge forward, right edge back.
    /// So the run covers exactly the graded flat top. Each edge is the centerline offset by half the top width. An offset makes loops on
    /// the inside of a bend tighter than the half width. These loops are cut at their crossing. Then each edge is simplified
    /// (Douglas-Peucker) and thinned to MinSpacingM. The ring must be simple, with MinClearanceM between non-adjacent edges. The engine
    /// refuses a touching outline as InvalidShape (Game.Areas/ValidationHelpers.cs:887-943, min distance = snap / 2 = 0.5 m for Space,
    /// AreaUtils.cs:55-58). The ring has no closing duplicate. The engine half adds it (GenerateAreasSystem.cs:282-291).</summary>
    public static class GradeRibbon
    {
        public const float SimplifyM = 0.5f;
        public const float MinSpacingM = 2f;
        public const float MinClearanceM = 1.5f;

        public static float2[] Build(GradePath path, float halfWidth, out GradeRibbonFail fail) => Build(path, halfWidth, out fail, out float2 _);

        /// <summary><paramref name="at"/> = the middle of the first edge that fails the clearance (Crosses), else default.</summary>
        public static float2[] Build(GradePath path, float halfWidth, out GradeRibbonFail fail, out float2 at)
        {
            fail = GradeRibbonFail.TooShort;
            at = default;
            if (path == null || path.Count < 2 || !(halfWidth > 0f))
                return null;
            int n = path.Count;
            var left = new List<float2>(n);
            var right = new List<float2>(n);
            for (int i = 0; i < n; i++)
            {
                float2 t = math.normalizesafe(path.Xz[math.min(i + 1, n - 1)] - path.Xz[math.max(i - 1, 0)], new float2(1f, 0f));
                float2 nl = new float2(-t.y, t.x) * halfWidth;
                left.Add(path.Xz[i] + nl);
                right.Add(path.Xz[i] - nl);
            }
            // A loop on the inside of a bend spans at most about a half circle of the half width plus both legs' approach
            int window = (int)math.ceil((4f * halfWidth + 20f) / math.max(path.StepM, 0.1f));
            List<float2> l = Thin(Simplify(CutLoops(left, window), SimplifyM), MinSpacingM);
            List<float2> r = Thin(Simplify(CutLoops(right, window), SimplifyM), MinSpacingM);
            if (l.Count < 2 || r.Count < 2)
                return null;
            var ring = new float2[l.Count + r.Count];
            for (int i = 0; i < l.Count; i++)
                ring[i] = l[i];
            for (int i = 0; i < r.Count; i++)
                ring[l.Count + i] = r[r.Count - 1 - i];
            fail = Clear(ring, MinClearanceM, out at) ? GradeRibbonFail.None : GradeRibbonFail.Crosses;
            return ring;
        }

        /// <summary>Walks the polyline and, where the current segment crosses a later one within <paramref name="window"/> samples, moves
        /// to the crossing (the farthest crossing first, so nested loops go in one cut).</summary>
        public static List<float2> CutLoops(List<float2> p, int window)
        {
            var o = new List<float2>(p.Count) { p[0] };
            float2 a = p[0];
            int i = 0;
            while (i < p.Count - 1)
            {
                float2 b = p[i + 1];
                int hit = -1;
                float2 x = default;
                for (int j = math.min(p.Count - 2, i + window); j >= i + 2; j--)
                {
                    if (SegCross(a, b, p[j], p[j + 1], out x))
                    {
                        hit = j;
                        break;
                    }
                }
                if (hit < 0)
                {
                    o.Add(b);
                    a = b;
                    i++;
                }
                else
                {
                    o.Add(x);
                    a = x;
                    i = hit;
                }
            }
            return o;
        }

        public static List<float2> Simplify(List<float2> p, float tol)
        {
            if (p.Count < 3)
                return new List<float2>(p);
            var keep = new bool[p.Count];
            keep[0] = keep[p.Count - 1] = true;
            var stack = new Stack<int2>();
            stack.Push(new int2(0, p.Count - 1));
            while (stack.Count > 0)
            {
                int2 s = stack.Pop();
                float worst = tol;
                int at = -1;
                for (int k = s.x + 1; k < s.y; k++)
                {
                    float d = GradePath.SegDistance(p[k], p[s.x], p[s.y], out float _);
                    if (d > worst)
                    {
                        worst = d;
                        at = k;
                    }
                }
                if (at < 0)
                    continue;
                keep[at] = true;
                stack.Push(new int2(s.x, at));
                stack.Push(new int2(at, s.y));
            }
            var o = new List<float2>();
            for (int k = 0; k < p.Count; k++)
                if (keep[k])
                    o.Add(p[k]);
            return o;
        }

        /// <summary>Drops interior points closer than <paramref name="minM"/> to the last kept one (and the one before the end when
        /// it sits that close to the end); both ends stay.</summary>
        public static List<float2> Thin(List<float2> p, float minM)
        {
            if (p.Count < 3)
                return new List<float2>(p);
            float min2 = minM * minM;
            var o = new List<float2> { p[0] };
            for (int k = 1; k < p.Count - 1; k++)
                if (math.distancesq(p[k], o[o.Count - 1]) >= min2)
                    o.Add(p[k]);
            if (o.Count > 1 && math.distancesq(o[o.Count - 1], p[p.Count - 1]) < min2)
                o.RemoveAt(o.Count - 1);
            o.Add(p[p.Count - 1]);
            return o;
        }

        /// <summary>True when no two non-adjacent edges of the closed ring come within <paramref name="clearance"/> (crossing = 0).</summary>
        public static bool Clear(float2[] ring, float clearance) => Clear(ring, clearance, out float2 _);

        public static bool Clear(float2[] ring, float clearance, out float2 at)
        {
            at = default;
            int n = ring.Length;
            if (n < 3)
                return false;
            for (int a = 0; a < n; a++)
            {
                float2 a0 = ring[a], a1 = ring[(a + 1) % n];
                for (int b = a + 2; b < n; b++)
                {
                    if (a == 0 && b == n - 1)
                        continue;   // adjacent through the closing edge
                    float2 b0 = ring[b], b1 = ring[(b + 1) % n];
                    if (math.any(math.min(a0, a1) - clearance > math.max(b0, b1)) || math.any(math.min(b0, b1) - clearance > math.max(a0, a1)))
                        continue;   // boxes apart: cheap reject
                    if (SegSegDistance(a0, a1, b0, b1) < clearance)
                    {
                        at = 0.5f * (a0 + a1);
                        return false;
                    }
                }
            }
            return true;
        }

        public static bool SegCross(float2 a, float2 b, float2 c, float2 d, out float2 x)
        {
            x = default;
            float2 r = b - a, s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (math.abs(den) < 1e-9f)
                return false;
            float2 ca = c - a;
            float t = (ca.x * s.y - ca.y * s.x) / den;
            float u = (ca.x * r.y - ca.y * r.x) / den;
            if (t < 0f || t > 1f || u < 0f || u > 1f)
                return false;
            x = a + t * r;
            return true;
        }

        public static float SegSegDistance(float2 a, float2 b, float2 c, float2 d)
        {
            if (SegCross(a, b, c, d, out float2 _))
                return 0f;
            return math.min(math.min(GradePath.SegDistance(a, c, d, out float _), GradePath.SegDistance(b, c, d, out float _)),
                            math.min(GradePath.SegDistance(c, a, b, out float _), GradePath.SegDistance(d, a, b, out float _)));
        }
    }

    /// <summary>One applied grade, for undo (pipeline 5).</summary>
    public sealed class GradeUndoEntry
    {
        public int X0, Z0, W, H;
        public ushort[] Before;
        public ushort[] After;
        public string Label;
        public int Bytes => (Before.Length + After.Length) * 2;
    }

    public sealed class GradeUndo
    {
        public const int MaxEntries = 8;
        public const int MaxBytes = 64 * 1024 * 1024;

        private readonly List<GradeUndoEntry> m_Entries = new List<GradeUndoEntry>();

        public int Count => m_Entries.Count;
        public int Bytes
        {
            get
            {
                int b = 0;
                for (int i = 0; i < m_Entries.Count; i++)
                    b += m_Entries[i].Bytes;
                return b;
            }
        }

        public void Clear() => m_Entries.Clear();

        /// <summary>Push. The oldest entries go when over MaxEntries or MaxBytes (the newest always stays). Returns how many were dropped,
        /// or -1 (nothing stored) for an entry over GradeRaster.MaxTexels. Otherwise the newest-always-stays rule would hold it past
        /// MaxBytes. The tool refuses such a rect before it writes, so this is a backstop.</summary>
        public int Push(GradeUndoEntry e)
        {
            if (e.Before.Length > GradeRaster.MaxTexels || e.After.Length > GradeRaster.MaxTexels)
                return -1;
            m_Entries.Add(e);
            int dropped = 0;
            while (m_Entries.Count > 1 && (m_Entries.Count > MaxEntries || Bytes > MaxBytes))
            {
                m_Entries.RemoveAt(0);
                dropped++;
            }
            return dropped;
        }

        public GradeUndoEntry Peek() => m_Entries.Count > 0 ? m_Entries[m_Entries.Count - 1] : null;

        public GradeUndoEntry Pop()
        {
            if (m_Entries.Count == 0)
                return null;
            GradeUndoEntry e = m_Entries[m_Entries.Count - 1];
            m_Entries.RemoveAt(m_Entries.Count - 1);
            return e;
        }

        /// <summary>The image to write for undoing <paramref name="e"/> over the rect's current content: the pre-image where the texel
        /// still holds what the grade wrote, the current value elsewhere (a foreign edit since then survives).
        /// <paramref name="kept"/> = texels left as they are.</summary>
        public static ushort[] Restore(GradeUndoEntry e, ushort[] current, out int kept)
        {
            kept = 0;
            var result = new ushort[current.Length];
            for (int t = 0; t < current.Length; t++)
            {
                if (current[t] == e.After[t])
                    result[t] = e.Before[t];
                else
                {
                    result[t] = current[t];
                    kept++;
                }
            }
            return result;
        }
    }

    /// <summary>The tool's step (CatTrackGradeToolSystem).</summary>
    /// <summary>A player text of the cat track tool: a LocaleEN key SkiResort.CatTrack.<c>Key</c> with its whole-number {ARG} values
    /// (meters, percents, cubic meters). It is pure so GradeBatch can see which keys the tool can show. The tooltip turns it into a
    /// LocalizedString. The log keeps the detail. A message never carries ids or log syntax.</summary>
    public readonly struct GradeMsg
    {
        public const string Prefix = "SkiResort.CatTrack.";
        public static readonly GradeMsg Empty = default;

        public readonly string Key;
        public readonly (string Name, float Value)[] Args;

        private GradeMsg(string key, (string, float)[] args)
        {
            Key = key;
            Args = args;
        }

        public bool IsEmpty => Key == null;

        public static GradeMsg Of(string key, params (string, float)[] args) => new GradeMsg(key, args);

        /// <summary>Equal messages have equal signatures (the tooltip re-sets its text only when this changes).</summary>
        public string Signature
        {
            get
            {
                if (Key == null)
                    return string.Empty;
                var sb = new System.Text.StringBuilder(Key);
                for (int i = 0; Args != null && i < Args.Length; i++)
                    sb.Append('|').Append(Args[i].Name).Append('=').Append(((int)Math.Round(Args[i].Value)).ToString(CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }

    public enum GradeStep : byte
    {
        Empty,
        OnePoint,
        Path, // two or more points: the corridor is solved and previewed
        Applying,
    }

    /// <summary>What one right click does (GradeInput.RightClick).</summary>
    public enum GradeBack : byte
    {
        None,
        CancelDrag, // a point is being dragged: it goes back where it was
        UndoEdit, // the last add / insert / move is undone (removing the last point included)
        CloseTool, // nothing left: back to the default tool
        LeaveDeleteMode, // in Delete mode: the mode goes off (nothing is deleted, the points stay)
    }

    /// <summary>The edit model: the clicked points and an undo stack of point snapshots (pure, the LiftLineEdit pattern).</summary>
    public sealed class GradeEdit
    {
        private readonly List<float2> m_Points = new List<float2>();
        private readonly List<float2[]> m_Undo = new List<float2[]>();

        public IReadOnlyList<float2> Points => m_Points;
        public int Count => m_Points.Count;
        public int UndoDepth => m_Undo.Count;
        public int Revision { get; private set; }

        public void Clear()
        {
            m_Points.Clear();
            m_Undo.Clear();
            Revision++;
        }

        private void Save()
        {
            m_Undo.Add(m_Points.ToArray());
            Revision++;
        }

        public void Add(float2 p)
        {
            Save();
            m_Points.Add(p);
        }

        public void Insert(int index, float2 p)
        {
            Save();
            m_Points.Insert(math.clamp(index, 0, m_Points.Count), p);
        }

        /// <summary>Move point <paramref name="i"/>. False (nothing pushed) when it is not there or did not move.</summary>
        public bool Move(int i, float2 p)
        {
            if (i < 0 || i >= m_Points.Count || math.distancesq(m_Points[i], p) < 0.01f)
                return false;
            Save();
            m_Points[i] = p;
            return true;
        }

        public bool Undo()
        {
            if (m_Undo.Count == 0)
                return false;
            float2[] snap = m_Undo[m_Undo.Count - 1];
            m_Undo.RemoveAt(m_Undo.Count - 1);
            m_Points.Clear();
            m_Points.AddRange(snap);
            Revision++;
            return true;
        }

        /// <summary>The point within <paramref name="radius"/> of <paramref name="p"/> (nearest), or -1.</summary>
        public int PointNear(float2 p, float radius)
        {
            int best = -1;
            float bd = radius * radius;
            for (int i = 0; i < m_Points.Count; i++)
            {
                float d = math.distancesq(m_Points[i], p);
                if (d <= bd)
                {
                    bd = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Where a left click away from every point goes: between the two clicked points of the path span it is
        /// within <paramref name="radius"/> of (insert index), else after the last point (append). The path is the chain through the points.
        /// The span test uses the straight chords, which are good enough at pick distance.</summary>
        public int InsertIndex(float2 p, float radius)
        {
            float best = radius;
            int index = m_Points.Count;
            for (int i = 0; i < m_Points.Count - 1; i++)
            {
                float d = GradePath.SegDistance(p, m_Points[i], m_Points[i + 1], out float _);
                if (d <= best)
                {
                    best = d;
                    index = i + 1;
                }
            }
            return index;
        }
    }

    /// <summary>The input rules (pure, LiftBatch's LiftLineInput pattern). A left click adds a point (on the path, it inserts the point). A
    /// press-drag moves one. A right click steps back one action and closes the tool only when nothing is left. The Build key (Enter)
    /// applies. Esc closes.</summary>
    public static class GradeInput
    {
        public static GradeStep StepOf(int points, bool applying) =>
            applying ? GradeStep.Applying : points == 0 ? GradeStep.Empty : points == 1 ? GradeStep.OnePoint : GradeStep.Path;

        /// <summary>Right click or Esc (both reach the tool's Cancel action while it is live, and the field log of 2026-10-26b shows Esc
        /// presses logged as right clicks). It does the first of these that applies: leave Delete mode, cancel a drag, undo the last point
        /// edit, or close. It never touches a built cat track (field test 2026-10-26b, the Quick runs rule, DECISIONS 417).</summary>
        public static GradeBack RightClick(GradeStep step, bool dragging, int undoDepth, bool deleteMode = false)
        {
            if (step == GradeStep.Applying)
                return GradeBack.None;
            if (deleteMode)
                return GradeBack.LeaveDeleteMode;
            if (dragging)
                return GradeBack.CancelDrag;
            return undoDepth > 0 ? GradeBack.UndoEdit : GradeBack.CloseTool;
        }

        /// <summary>The step hint: always a non-empty line naming what the next click does in this step.</summary>
        public static string PromptKey(GradeStep step, bool dragging)
        {
            if (dragging)
                return "PROMPT_DRAG";
            switch (step)
            {
                case GradeStep.Empty:
                    return "PROMPT_EMPTY";
                case GradeStep.OnePoint:
                    return "PROMPT_ONE";
                case GradeStep.Path:
                    return "PROMPT_PATH";
                default:
                    return "PROMPT_BUILDING";
            }
        }

        /// <summary>The plan line: length, steepest part against the limit, earthwork, or what is wrong in words. Whole numbers only.</summary>
        public static GradeMsg PlanMsg(GradePath path, GradeProfile prof, GradeRaster raster, GradeParams p)
        {
            if (path == null || prof == null)
                return GradeMsg.Empty;
            float max = prof.MaxGradeAbs * 100f, limit = p.MaxGrade * 100f;
            if (prof.ShortfallM > 0f)
                return GradeMsg.Of("PLAN_STEEP", ("MAX", max), ("LIMIT", limit), ("SHORT", (float)Math.Ceiling(prof.ShortfallM)));
            if (raster == null)
                return GradeMsg.Of("PLAN_LENGTH", ("LENGTH", path.LengthM), ("MAX", max), ("LIMIT", limit));
            return GradeMsg.Of("PLAN", ("LENGTH", path.LengthM), ("MAX", max), ("LIMIT", limit), ("CUT", raster.CutM3), ("FILL", raster.FillM3));
        }

        /// <summary>The profile line of the tooltip.</summary>
        public static string Describe(GradePath path, GradeProfile prof, GradeRaster raster, GradeParams p)
        {
            if (path == null || prof == null)
                return string.Empty;
            string s = string.Format(CultureInfo.InvariantCulture, "{0:F0} m · grade avg {1:F0} %, max {2:F0} % (limit {3:F0} %)",
                path.LengthM, prof.AvgGradeAbs * 100f, prof.MaxGradeAbs * 100f, p.MaxGrade * 100f);
            if (raster != null)
                s += string.Format(CultureInfo.InvariantCulture, " · cut {0:N0} m³, fill {1:N0} m³", raster.CutM3, raster.FillM3);
            if (prof.ShortfallM > 0f)
                s += string.Format(CultureInfo.InvariantCulture, " · too steep: needs {0:F0} m more track (a switchback)", math.ceil(prof.ShortfallM));
            return s;
        }
    }

    /// <summary>What one left click does in the tool's Delete mode (GradeDeleteInput.Click).</summary>
    public enum GradeDeleteClick : byte
    {
        None, // off every cat track, nothing armed
        Arm, // on a cat track that is not armed: it turns red and waits for the confirming click
        Delete, // on the armed cat track: the confirm, the track is deleted (the hub row trash's path)
        Disarm, // off every cat track while one is armed
    }

    /// <summary>The Delete mode rules (pure, field test 2026-10-26b): a two-click confirm on the hovered cat track. Right click / Esc only
    /// leave the mode (GradeBack.LeaveDeleteMode via RightClick) and never delete. Tracks are indexes into the picked ring list.</summary>
    public static class GradeDeleteInput
    {
        public static GradeDeleteClick Click(int hovered, int armed)
        {
            if (hovered < 0)
                return armed >= 0 ? GradeDeleteClick.Disarm : GradeDeleteClick.None;
            return hovered == armed ? GradeDeleteClick.Delete : GradeDeleteClick.Arm;
        }

        /// <summary>The cat track under <paramref name="p"/>: the first ring that contains it (cat tracks cannot overlap, the game's
        /// OverlapExisting), else -1. The tool feeds it its terrain hit: no area raycast.</summary>
        public static int Pick(IList<float2[]> rings, float2 p)
        {
            for (int k = 0; rings != null && k < rings.Count; k++)
            {
                float2[] ring = rings[k];
                if (ring != null && ring.Length >= 3 && GradeRaster.Inside(ring, p))
                    return k;
            }
            return -1;
        }

        public static string PromptKey(int hovered, int armed)
        {
            if (armed >= 0 && hovered == armed)
                return "PROMPT_DEL_ARMED";
            if (hovered >= 0)
                return "PROMPT_DEL_HOVER";
            return "PROMPT_DEL_NONE";
        }
    }
}
