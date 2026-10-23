// Pure half of the Cat Track Grading tool: Unity.Mathematics only, so tests/GradeBatch links this file and proves every rule
// headless. The engine half is CatTrackGradeToolSystem (input, overlay, guards' data, apply) and HeightmapWriter (the base
// heightmap read / write).
// One solve per change, never per frame: GradePath.Build -> GradeProfile.Solve -> GradeRaster.Build -> GradeGuards.Check, with
// GradeUndo holding the pre-images. Every profile step stays G-limited (|dy/ds| <= G).
// Height encoding: the base heightmap is R16_UNorm; GradeMap holds the world <-> texel mapping.
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
        public const float DefaultTopWidthM = 8f; // the cat-track run default width
        public const float DefaultCutRun = 1.5f;
        public const float DefaultFillRun = 2f;

        public float MaxGrade = DefaultMaxGrade;
        public float TopWidthM = DefaultTopWidthM;
        public float CutRun = DefaultCutRun;
        public float FillRun = DefaultFillRun;
        public GradeProfileMode Mode = GradeProfileMode.FollowGround;
        public float SampleStepM = 2f;
        public float SmoothM = 6f;
        /// <summary>The side slopes reach at most this far beyond the flat top. Where a slope has not met the ground by then (a
        /// hillside steeper than the slope, or a very deep cut / fill) the last BlendTexels of the reach blend into the ground: a steep
        /// bank, counted and logged, never a vertical step.</summary>
        public float MaxSideM = 40f;
        public const int BlendTexels = 2;

        public GradeParams Clone() => (GradeParams)MemberwiseClone();

        public string Describe() =>
            $"max grade {MaxGrade * 100f:F0} %, top {TopWidthM:F1} m, cut 1:{CutRun:F1}, fill 1:{FillRun:F1}, " +
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

        /// <summary>Builds the chain through <paramref name="points"/> (>= 2, consecutive duplicates dropped); null when fewer than
        /// two distinct points or a zero-length path.</summary>
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
        /// <summary>Grade of each sample step (Y[i+1] - Y[i]) / ds; Length = samples - 1.</summary>
        public float[] SegGrade;
        public float MaxGradeAbs;
        public float AvgGradeAbs;
        public float MaxCutM, MaxFillM;
        public bool Feasible;
        /// <summary>Extra path length the grade limit needs between the two ends (0 when feasible).</summary>
        public float ShortfallM;

        public static GradeProfile Solve(GradePath path, float[] ground, GradeParams p)
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
                for (int i = 0; i < n; i++)
                    y[i] = 0.5f * (up[i] + lo[i]);

                int k = (int)math.floor(p.SmoothM / math.max(ds, 1e-3f));
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
                }

                // Pin both ends to the ground with slope cones: slope G, or the slope the two ends force when that is steeper (infeasible:
                // both ends still meet the ground and the steep part shows red).
                float gc = math.max(g, L > 0f ? math.abs(gn - g0) / L * 1.0001f : g);
                for (int i = 0; i < n; i++)
                {
                    float s = path.S[i];
                    y[i] = math.clamp(y[i], g0 - gc * s, g0 + gc * s);
                    y[i] = math.clamp(y[i], gn - gc * (L - s), gn + gc * (L - s));
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

    /// <summary>The rasterized corridor over one heightmap rect (pipeline 3). Heights are world meters (floats); the engine half
    /// encodes them (GradeMap.ToU16) and only texels whose code changes are counted / written.</summary>
    public sealed class GradeRaster
    {
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
        public float MaxBankStepM;

        public int Index(int i, int j) => (j - Z0) * W + (i - X0);

        /// <summary>The rect the corridor can touch: the samples' box grown by the flat top and MaxSideM (the side slopes' reach; a
        /// sidehill needs the full reach even where the centerline sits on the ground); clamped to the map. False when it misses the
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

        /// <summary>Rasterize. <paramref name="baseHeight"/> = the base height of texel (i, j) in world meters (the engine half reads
        /// the base rect; tests pass a function).</summary>
        public static GradeRaster Build(GradeMap map, int4 rect, GradePath path, GradeProfile prof, GradeParams p, float reach,
                                        Func<int, int, float> baseHeight)
        {
            var r = new GradeRaster { X0 = rect.x, Z0 = rect.y, W = rect.z, H = rect.w };
            int count = r.W * r.H;
            r.Before = new float[count];
            r.After = new float[count];
            r.Zone = new byte[count];
            r.TexelAreaM2 = map.Cell.x * map.Cell.y;
            var bestD2 = new float[count];
            var bestY = new float[count];
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
                float ya = prof.Y[s], yb = prof.Y[s + 1];
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
                    h = y;
                    zone = 2;
                }
                else
                {
                    float out_ = d - hw;
                    h = before > y ? math.min(before, y + out_ / p.CutRun) : math.max(before, y - out_ / p.FillRun);
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

        public string Describe() =>
            string.Format(CultureInfo.InvariantCulture, "rect ({0}, {1}) {2} x {3}, {4} texel(s) changed, cut {5:F0} m3 (max {6:F1} m), " +
                          "fill {7:F0} m3 (max {8:F1} m){9}", X0, Z0, W, H, Changed, CutM3, MaxCutM, FillM3, MaxFillM,
                          SteepBankTexels > 0
                              ? string.Format(CultureInfo.InvariantCulture, ", steep bank at {0} texel(s) (up to {1:F1} m blended over the last {2} texels)",
                                              SteepBankTexels, MaxBankStepM, GradeParams.BlendTexels)
                              : string.Empty);
    }

    public enum GradeGuardKind : byte
    {
        None,
        Lot, // a building lot (lift station, lodge, any building)
        LiftLine,
        Water,
        OffMap,
    }

    /// <summary>What the engine half found in the rect (pipeline 4).</summary>
    public sealed class GradeObstacles
    {
        public struct Lot
        {
            public float2 Center, Right, Forward, Half; // Right / Forward unit; Half = lot half extents (x, z)
        }

        public struct Line
        {
            public float2 A, B;
            public float Clearance;
        }

        public readonly List<Lot> Lots = new List<Lot>();
        public readonly List<Line> Lines = new List<Line>();
        /// <summary>Texels (global i, j) the water surface covers; empty when none.</summary>
        public readonly HashSet<int2> WetTexels = new HashSet<int2>();

        public void Clear()
        {
            Lots.Clear();
            Lines.Clear();
            WetTexels.Clear();
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
                        if (math.abs(math.dot(d, lot.Right)) <= lot.Half.x + halfTexel &&
                            math.abs(math.dot(d, lot.Forward)) <= lot.Half.y + halfTexel)
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

        /// <summary>The player text of a refusal.</summary>
        public static string Reason(GradeGuardKind k)
        {
            switch (k)
            {
                case GradeGuardKind.Lot: return "The track would grade under a building or station lot: move it clear";
                case GradeGuardKind.LiftLine: return "The track would grade under a lift line: move it clear";
                case GradeGuardKind.Water: return "The track would grade into water: move it clear";
                case GradeGuardKind.OffMap: return "The track leaves the map";
                default: return string.Empty;
            }
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

        /// <summary>Push; the oldest entries go when over MaxEntries or MaxBytes (the newest always stays). Returns how many were
        /// dropped.</summary>
        public int Push(GradeUndoEntry e)
        {
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
        UndoGrade, // no point left and a grade was applied since the tool opened: the terrain goes back
        CloseTool, // nothing left: back to the default tool
    }

    /// <summary>The edit model: the clicked points and an undo stack of point snapshots (pure; the LiftLineEdit pattern).</summary>
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

        /// <summary>Move point <paramref name="i"/>; false (nothing pushed) when it is not there or did not move.</summary>
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

        /// <summary>Where a left click away from every point goes: between the two clicked points of the path span it is within
        /// <paramref name="radius"/> of (insert index), else after the last point (append). The path is the chain through the points;
        /// the span test uses the straight chords, good enough at pick distance.</summary>
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

    /// <summary>The input rules (pure; LiftBatch's LiftLineInput pattern): left click adds a point (on the path: inserts it),
    /// press-drag moves one, right click steps back one action and closes the tool only when nothing is left, the Build key (Enter)
    /// applies, Esc closes.</summary>
    public static class GradeInput
    {
        public static GradeStep StepOf(int points, bool applying) =>
            applying ? GradeStep.Applying : points == 0 ? GradeStep.Empty : points == 1 ? GradeStep.OnePoint : GradeStep.Path;

        public static GradeBack RightClick(GradeStep step, bool dragging, int undoDepth, int gradesThisOpening)
        {
            if (step == GradeStep.Applying)
                return GradeBack.None;
            if (dragging)
                return GradeBack.CancelDrag;
            if (undoDepth > 0)
                return GradeBack.UndoEdit;
            return gradesThisOpening > 0 ? GradeBack.UndoGrade : GradeBack.CloseTool;
        }

        /// <summary>The step hint: always a non-empty line naming what the next click does in this step.</summary>
        public static string Prompt(GradeStep step, bool dragging, bool canUndoGrade)
        {
            if (dragging)
                return "Release: put the point here · right click: cancel the move";
            switch (step)
            {
                case GradeStep.Empty:
                    return canUndoGrade
                        ? "Grade cat track: click where the track starts · right click: undo the last grade · Esc: close"
                        : "Grade cat track: click where the track starts · right click: close";
                case GradeStep.OnePoint:
                    return "Click the next point of the track · right click: back";
                case GradeStep.Path:
                    return "Click: add a point (on the track: insert) · drag a point: move it · right click: undo · Enter: grade · Esc: close";
                default:
                    return "Grading…";
            }
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
}
