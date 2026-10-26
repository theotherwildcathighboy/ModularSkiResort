// Quick runs planner, stages 5-9: top clusters and seeds, candidate centerlines with a cheap corridor score (pruning before any
// rating, skeptic finding 8), greedy selection that builds each corridor against the runs already accepted (gap or trail merge),
// rates it with SkiRunRater (the label the game will show), then junction node pairs (skeptic finding 1), kinds, clusters and
// warnings. Pure; see SkiRunAutoPlanner.cs for the contract.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        private struct SeedRec
        {
            public int Cell, Cluster, Pass, Ordinal;
        }

        private sealed class Cand
        {
            public int Id, Cluster, Pass, PathStart, PathLen;
            public SlopeBand Expected;
            public float LengthM, DropM;
            public double Score0;
            public bool Tried;
        }

        private sealed class ClusterInfo
        {
            public int Cell;
            public float2 Center;
            public float TopY;
            public bool Pod;
            public int PodPass = -1;
        }

        /// <summary>An accepted run before junction nodes (kept so a kinds-only Reselect re-runs Finish from the same rings).</summary>
        private sealed class Accepted
        {
            public Cand Cand;
            public float2[] Ring;
            public float2[] Centerline;
            public SlopeBand Band;
            public float RatingGrade, MeanGrade, SteepestGrade, DropM, AreaM2, LengthM, TopY, BottomY, GapM;
            public bool Merge, TouchesBase, OverWater, CrossesCatTrack;
            public int MergeInto;
            public double Score;
        }

        private readonly List<ClusterInfo> m_Clusters = new List<ClusterInfo>();
        private readonly List<int> m_PodPasses = new List<int>();
        private readonly List<SeedRec> m_Seeds = new List<SeedRec>();
        private readonly List<Cand> m_Cands = new List<Cand>();
        private readonly List<int> m_Paths = new List<int>();
        private readonly HashSet<ulong> m_PathHashes = new HashSet<ulong>();
        private readonly List<Accepted> m_Accepted = new List<Accepted>();
        private long[] m_SortKeys = new long[256];
        private AutoPlanWarning m_FieldWarnings;
        private readonly int[] m_Target = new int[5], m_Have = new int[5];
        private int m_RunCap;

        // Rating buffers (separate from m_Grid, which the rasterizer reuses)
        private readonly SlopeGrid m_RateGrid = new SlopeGrid();
        private readonly SlopeResult m_RateResult = new SlopeResult();
        private float3[] m_RateNodes = new float3[256];

        // Corridor scratch
        private readonly List<float2> m_PathPts = new List<float2>();
        private readonly List<float2> m_Left = new List<float2>(), m_Right = new List<float2>();
        private float[] m_HwL = new float[256], m_HwR = new float[256];

        private void ClearCandidates()
        {
            m_Clusters.Clear();
            m_Seeds.Clear();
            m_Cands.Clear();
            m_Paths.Clear();
            m_PathHashes.Clear();
            m_Accepted.Clear();
        }

        private void ResetForStage(Stage restart)
        {
            AutoPlanResult r = m_Result;
            r.Runs.Clear();
            r.Junctions.Clear();
            r.Clusters.Clear();
            r.Warnings = m_FieldWarnings;
            r.Status = AutoPlanStatus.Running;
            Array.Clear(r.BandCount, 0, 5);
            if (restart <= Stage.Seeds)
            {
                ClearCandidates();
                Array.Clear(r.Rejects, 0, r.Rejects.Length);
                r.Candidates = 0;
            }
            if (restart <= Stage.Select)
            {
                m_Accepted.Clear();
                for (int i = 0; i < m_Cands.Count; i++)
                    m_Cands[i].Tried = false;
                for (int i = (int)AutoReject.TopTaken; i < (int)AutoReject.Count; i++)
                    if (i != (int)AutoReject.FlatLine && i != (int)AutoReject.Duplicate)
                        r.Rejects[i] = 0;
                r.Rated = 0;
            }
        }

        // ---------------------------------------------------------------- stage 5a: clusters and seeds (one unit)

        private void UnitSeeds()
        {
            m_FieldWarnings = m_Result.Warnings;
            ClearCandidates();
            int dbCeil = Bucket(PassCeiling[Passes - 1]);
            float topLine = m_HMaxU - TopBandM;
            // Top cells: usable, within TopBandM of the highest usable ground, reaching the base below the double black ceiling
            int nTop = 0;
            for (int j = m_RegionJ0; j <= m_RegionJ1; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    if ((m_Flags[c] & (FOpen | FCliff)) == FOpen && m_H[c] >= topLine && m_M[c] < dbCeil)
                        m_Order[nTop++] = c;
                }
            // Pods (real-world-layout.md 5): a pass (green, blue, black) that no top cell reaches gets a slot of its own at the highest
            // ground that pass does reach: the beginner pod near the base, a blue mid-station under a steep summit. Their slots are
            // reserved before the summit picks, easiest pass first (a resort without greens is the worse failure).
            m_PodPasses.Clear();
            for (int p = 0; p < Passes - 1; p++)
            {
                bool topReaches = false;
                for (int k = 0; k < nTop && !topReaches; k++)
                    topReaches = Reachable(p, m_Order[k]);
                if (!topReaches && m_PodPasses.Count < MaxClusters - 1 && PodCell(p, false) >= 0)
                    m_PodPasses.Add(p);
            }
            // Cluster centers: the highest top cell, then farthest-point picks at least ClusterSpacingM apart
            int first = -1;
            for (int k = 0; k < nTop; k++)
            {
                int c = m_Order[k];
                if (first < 0 || m_H[c] > m_H[first])
                    first = c;
            }
            if (first >= 0)
            {
                AddCluster(first, -1);
                PickSummits(nTop, MaxClusters - m_PodPasses.Count);
            }
            foreach (int p in m_PodPasses)
            {
                int pod = PodCell(p, true);
                if (pod >= 0)
                    AddCluster(pod, p);
            }
            if (first >= 0)
                PickSummits(nTop, MaxClusters);   // a reserved pod that found no room gives its slot back
            // Seeds per cluster and pass: member cells ordered by height plus a seeded jitter, spaced one corridor plus a gap apart
            for (int cl = 0; cl < m_Clusters.Count; cl++)
            {
                ClusterInfo ci = m_Clusters[cl];
                for (int p = 0; p < Passes; p++)
                {
                    int n = 0;
                    int ri = (int)math.ceil(ClusterRadiusM / m_Texel);
                    int ci0 = ci.Cell % m_Nx, cj0 = ci.Cell / m_Nx;
                    Grow(ref m_SortKeys, (2 * ri + 1) * (2 * ri + 1));   // Grow does not copy: size it once, before the fill
                    for (int j = Math.Max(m_RegionJ0, cj0 - ri); j <= Math.Min(m_RegionJ1, cj0 + ri); j++)
                        for (int i = Math.Max(m_RegionI0, ci0 - ri); i <= Math.Min(m_RegionI1, ci0 + ri); i++)
                        {
                            int c = j * m_Nx + i;
                            if (!Reachable(p, c) || m_H[c] < ci.TopY - TopBandM)
                                continue;
                            float d = NearestCenter(CellCenter(c), out int owner);
                            if (owner != cl || d > ClusterRadiusM)
                                continue;
                            float key = m_H[c] + SeedJitterM * Jitter(m_Knobs.Seed, c, p);
                            long q = (long)Math.Round((1.0e6 - key) * 1000.0);
                            m_SortKeys[n++] = (q << 21) | (long)c;
                        }
                    Array.Sort(m_SortKeys, 0, n);
                    float spacing = TargetWidthM[p] + NormalGapM;
                    int start = m_Seeds.Count, ordinal = 0;
                    for (int k = 0; k < n && ordinal < SeedsPerClusterPass; k++)
                    {
                        int c = (int)(m_SortKeys[k] & ((1L << 21) - 1));
                        float2 pc = CellCenter(c);
                        bool near = false;
                        for (int s = start; s < m_Seeds.Count; s++)
                            if (math.distance(CellCenter(m_Seeds[s].Cell), pc) < spacing)
                            {
                                near = true;
                                break;
                            }
                        if (!near)
                            m_Seeds.Add(new SeedRec { Cell = c, Cluster = cl, Pass = p, Ordinal = ordinal++ });
                    }
                }
            }
            Next(Stage.Candidates);
        }

        /// <summary>Farthest-point summit picks among the m_Order[0, nTop) top cells, at least ClusterSpacingM from every cluster.</summary>
        private void PickSummits(int nTop, int limit)
        {
            while (m_Clusters.Count < limit)
            {
                int far = -1;
                float farD = -1f;
                for (int k = 0; k < nTop; k++)
                {
                    int c = m_Order[k];
                    float d = NearestCenter(CellCenter(c), out int _);
                    if (d > farD)
                    {
                        farD = d;
                        far = c;
                    }
                }
                if (far < 0 || farD < ClusterSpacingM)
                    return;
                AddCluster(far, -1);
            }
        }

        /// <summary>The highest cell pass p reaches with MinDropM above the base; with awayFromClusters, only cells outside every
        /// cluster's radius. -1 when there is none.</summary>
        private int PodCell(int p, bool awayFromClusters)
        {
            int best = -1;
            for (int j = m_RegionJ0; j <= m_RegionJ1; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    if (!Reachable(p, c) || m_H[c] - m_BaseTop < MinDropM || (best >= 0 && m_H[c] <= m_H[best]))
                        continue;
                    if (awayFromClusters && m_Clusters.Count > 0 && NearestCenter(CellCenter(c), out int _) < ClusterRadiusM)
                        continue;
                    best = c;
                }
            return best;
        }

        private void AddCluster(int c, int podPass)
        {
            m_Clusters.Add(new ClusterInfo { Cell = c, Center = CellCenter(c), TopY = m_H[c], Pod = podPass >= 0, PodPass = podPass });
        }

        private float NearestCenter(float2 p, out int owner)
        {
            owner = -1;
            float best = float.PositiveInfinity;
            for (int k = 0; k < m_Clusters.Count; k++)
            {
                float d = math.distance(p, m_Clusters[k].Center);
                if (d < best)
                {
                    best = d;
                    owner = k;
                }
            }
            return best;
        }

        private bool Reachable(int pass, int c)
        {
            byte p = m_Pred[pass][c];
            return p != PredNone && p != PredSource;
        }

        /// <summary>Uniform [0, 1) from (seed, cell, pass): a salted stream per draw, so the order cells are visited in never matters.</summary>
        private static float Jitter(uint seed, int cell, int pass)
        {
            uint h = Fnv(Fnv(seed, (uint)cell), (uint)pass * 0x9E3779B9u);
            var r = new Unity.Mathematics.Random(h | 1u);
            return r.NextFloat();
        }

        // ---------------------------------------------------------------- stage 5b: candidate centerlines, cheap score

        private void UnitCandidates()
        {
            int end = Math.Min(m_Seeds.Count, m_Cursor + 8);
            for (; m_Cursor < end; m_Cursor++)
                MakeCandidate(m_Seeds[m_Cursor]);
            if (m_Cursor >= m_Seeds.Count)
            {
                m_Result.Candidates = m_Cands.Count;
                Next(Stage.Select);
            }
        }

        private void MakeCandidate(SeedRec s)
        {
            byte[] pred = m_Pred[s.Pass];
            int start = m_Paths.Count;
            int c = s.Cell;
            float len = 0f, maxO = 0f;
            ulong hash = 14695981039346656037UL;
            int guard = m_N;
            while (guard-- > 0)
            {
                m_Paths.Add(c);
                hash = (hash ^ (ulong)(uint)c) * 1099511628211UL;
                maxO = Math.Max(maxO, m_O[c]);
                byte d = pred[c];
                if (d == PredSource || d == PredNone)
                    break;
                // pred[c] = d means the route reached c from c - Dir[d] (UnitRoute relaxes v = u + Dir[d]): step back toward the base
                int i = c % m_Nx - DirX[d], j = c / m_Nx - DirZ[d];
                len += d < 4 ? m_Texel : m_Texel * Sqrt2;
                c = j * m_Nx + i;
                if (len > MaxLengthM)
                    break;
            }
            int count = m_Paths.Count - start;
            float drop = m_H[s.Cell] - m_H[m_Paths[m_Paths.Count - 1]];
            AutoReject fate = AutoReject.Kept;
            SlopeBand expected = SkiSlopeClassifier.BandOf(maxO);
            if (!m_PathHashes.Add(hash))
                fate = AutoReject.Duplicate;
            else if (expected == SlopeBand.Flat)
                fate = AutoReject.FlatLine;
            else if (len < MinLengthM || drop < MinDropM)
                fate = AutoReject.TooShort;
            if (fate != AutoReject.Kept)
            {
                m_Paths.RemoveRange(start, count);
                m_Result.Rejects[(int)fate]++;
                return;
            }
            float area = Math.Min(len * TargetWidthM[s.Pass], MaxRunAreaM2);
            double score = SkiResortScore.SizeFactor(area) * SkiResortScore.BandValueOf(SkiResortScore.BandState.Rated, (byte)expected) *
                           (1.0 + Math.Min(1.0, Math.Max(0.0, drop / SkiResortScore.VerticalRefM)));
            m_Cands.Add(new Cand
            {
                Id = s.Cluster * 1000 + s.Pass * 100 + s.Ordinal,
                Cluster = s.Cluster, Pass = s.Pass, PathStart = start, PathLen = count, Expected = expected,
                LengthM = len, DropM = drop, Score0 = score,
            });
        }

        // ---------------------------------------------------------------- stage 7: greedy selection, one candidate per unit

        private void UnitSelect()
        {
            if (m_Cursor == 0)
            {
                BeginSelect();
                m_Cursor = 1;
            }
            if (m_Accepted.Count >= m_RunCap || m_Result.Rated >= MaxRatings)
            {
                if (m_Result.Rated >= MaxRatings)
                    for (int i = 0; i < m_Cands.Count; i++)
                        if (!m_Cands[i].Tried)
                            m_Result.Rejects[(int)AutoReject.RatingCap]++;
                Next(Stage.Finish);
                return;
            }
            Cand best = null;
            double bestW = 0.0;
            for (int i = 0; i < m_Cands.Count; i++)
            {
                Cand c = m_Cands[i];
                if (c.Tried)
                    continue;
                double w = c.Score0 * MixWeight(c.Expected);
                if (best == null || w > bestW || (w == bestW && c.Id < best.Id))
                {
                    best = c;
                    bestW = w;
                }
            }
            if (best == null)
            {
                Next(Stage.Finish);
                return;
            }
            best.Tried = true;
            AutoReject fate = Attempt(best);
            m_Result.Rejects[(int)fate]++;
        }

        private void BeginSelect()
        {
            int cap = m_Knobs.RunCap > 0 ? m_Knobs.RunCap
                : m_Knobs.Density == AutoDensity.Sparse ? 8 : m_Knobs.Density == AutoDensity.Dense ? 16 : 12;
            m_RunCap = Math.Min(cap, HardRunCap);
            Targets(m_Knobs.Mix, m_RunCap, m_Target);
            Array.Clear(m_Have, 0, 5);
            m_Accepted.Clear();
            for (int c = 0; c < m_N; c++)
                m_DAcc[c] = float.PositiveInfinity;
            m_Result.Rated = 0;
        }

        /// <summary>Run-count targets per band (largest remainder; green, blue and black get at least one when their share is
        /// positive and the cap allows, the variety bonus). Terrain = all zero.</summary>
        public static void Targets(AutoMix mix, int cap, int[] target)
        {
            Array.Clear(target, 0, 5);
            float[] share = MixShare[(int)mix];
            if (mix == AutoMix.Terrain || cap <= 0)
                return;
            int sum = 0;
            float[] rem = new float[4];
            for (int b = 0; b < 4; b++)
            {
                float x = share[b] * cap;
                target[b + 1] = (int)Math.Floor(x);
                rem[b] = x - target[b + 1];
                sum += target[b + 1];
            }
            while (sum < cap)
            {
                int pick = 0;
                for (int b = 1; b < 4; b++)
                    if (rem[b] > rem[pick])
                        pick = b;
                if (rem[pick] <= 0f)
                    break;
                target[pick + 1]++;
                rem[pick] = 0f;
                sum++;
            }
            for (int b = 1; b <= 3; b++)
            {
                if (target[b] > 0 || share[b - 1] <= 0f || cap < 3)
                    continue;
                int from = 2;
                for (int k = 1; k <= 4; k++)
                    if (target[k] > target[from])
                        from = k;
                if (target[from] > 1)
                {
                    target[from]--;
                    target[b]++;
                }
            }
        }

        private double MixWeight(SlopeBand band)
        {
            if (m_Knobs.Mix == AutoMix.Terrain)
                return 1.0;
            int b = (int)band;
            int deficit = m_Target[b] - m_Have[b];
            return deficit > 0 ? 1.0 + deficit : 0.1;
        }

        /// <summary>Builds, checks and rates one candidate against the accepted runs; Kept means accepted.</summary>
        private AutoReject Attempt(Cand cand)
        {
            int k = cand.Pass;
            float width = TargetWidthM[k], gap = GapOf(m_Knobs.Density, width);
            float stopD = gap + MinHalfWidthM;
            int seed = m_Paths[cand.PathStart];
            if (m_DAcc[seed] < stopD)
                return AutoReject.TopTaken;
            // Trail merge: stop where the centerline reaches an accepted run's gap; the end then tapers to MergeGapM
            int len = 1;
            bool merge = false;
            for (; len < cand.PathLen; len++)
                if (m_DAcc[m_Paths[cand.PathStart + len]] < stopD)
                {
                    merge = true;
                    break;
                }
            int last = m_Paths[cand.PathStart + len - 1];
            float pathLen = PathLength(cand.PathStart, len);
            if (pathLen < MinLengthM || m_H[seed] - m_H[last] < MinDropM)
                return AutoReject.TooShort;

            AutoReject fate = AutoReject.Crosses;
            float scale = 1f;
            int keep = len;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                fate = BuildRing(cand, keep, merge && keep == len, gap, scale, out float2[] ring, out GradePath path, out int mergeInto);
                if (fate == AutoReject.Kept)
                {
                    double area = PolygonArea(ring);
                    if (area > MaxRunAreaM2 || SkiSlopeClassifier.StepMultiple(area, m_Texel, 0) > 1)
                    {
                        keep = Math.Max(2, (int)(keep * Math.Min(0.95, MaxRunAreaM2 / area * 0.95)));
                        continue;
                    }
                    return Rate(cand, ring, path, merge && keep == len, mergeInto, keep == len, gap);
                }
                if (fate == AutoReject.Obstacle || fate == AutoReject.Overlap || fate == AutoReject.Region || fate == AutoReject.Crosses)
                {
                    scale *= 0.75f;
                    continue;
                }
                return fate;
            }
            return fate;
        }

        private float PathLength(int start, int count)
        {
            float len = 0f;
            for (int i = 1; i < count; i++)
            {
                int a = m_Paths[start + i - 1], b = m_Paths[start + i];
                len += (math.abs(a % m_Nx - b % m_Nx) + math.abs(a / m_Nx - b / m_Nx)) == 2 ? m_Texel * Sqrt2 : m_Texel;
            }
            return len;
        }

        private AutoReject BuildRing(Cand cand, int count, bool merge, float gap, float scale, out float2[] ring, out GradePath path,
            out int mergeInto)
        {
            ring = null;
            path = null;
            mergeInto = -1;
            int k = cand.Pass;
            float ceil = PassCeiling[k];
            // Centerline: decimated cells through GradePath's Catmull-Rom chain; the raw cells when smoothing leaves the corridor
            for (int pass = 0; pass < 2 && path == null; pass++)
            {
                int every = pass == 0 ? Math.Max(1, (int)Math.Round(DecimateM / m_Texel)) : 1;
                m_PathPts.Clear();
                for (int i = 0; i < count; i += every)
                    m_PathPts.Add(CellCenter(m_Paths[cand.PathStart + i]));
                if ((count - 1) % every != 0)
                    m_PathPts.Add(CellCenter(m_Paths[cand.PathStart + count - 1]));
                GradePath p = GradePath.Build(m_PathPts, SampleM);
                if (p == null)
                    return AutoReject.TooShort;
                bool ok = true;
                for (int i = 0; i < p.Count && ok; i++)
                    ok = Cell(p.Xz[i], out int c) && (m_Flags[c] & (FOpen | FCliff)) == FOpen && m_O[c] < ceil;
                if (ok || pass == 1)
                    path = p;
            }
            int n = path.Count;
            Grow(ref m_HwL, n);
            Grow(ref m_HwR, n);
            float half = 0.5f * TargetWidthM[k] * scale;
            int pinched = 0;
            for (int i = 0; i < n; i++)
            {
                float toEnd = path.LengthM - path.S[i];
                float g = merge && toEnd < MergeTaperM ? MergeGapM + (gap - MergeGapM) * toEnd / MergeTaperM : gap;
                float2 t = math.normalizesafe(path.Xz[Math.Min(i + 1, n - 1)] - path.Xz[Math.Max(i - 1, 0)], new float2(1f, 0f));
                float2 nl = new float2(-t.y, t.x);
                m_HwL[i] = March(path.Xz[i], nl, half, ceil, g);
                m_HwR[i] = March(path.Xz[i], -nl, half, ceil, g);
            }
            // Conservative smoothing: each half width is the minimum of itself and its two neighbors
            for (int side = 0; side < 2; side++)
            {
                float[] hw = side == 0 ? m_HwL : m_HwR;
                float prev = hw[0];
                for (int i = 0; i < n; i++)
                {
                    float cur = hw[i];
                    float next = i + 1 < n ? hw[i + 1] : cur;
                    hw[i] = Math.Min(cur, Math.Min(prev, next));
                    prev = cur;
                }
            }
            for (int i = 0; i < n; i++)
                if (m_HwL[i] + m_HwR[i] < MinWidthM * scale)
                    pinched++;
            if (pinched > PinchShare * n)
                return AutoReject.Pinched;
            // Outline: GradeRibbon.Build's tail (CutLoops, Simplify, Thin, Clear) on two variable-width edges
            m_Left.Clear();
            m_Right.Clear();
            float maxHw = 0f;
            for (int i = 0; i < n; i++)
            {
                float2 t = math.normalizesafe(path.Xz[Math.Min(i + 1, n - 1)] - path.Xz[Math.Max(i - 1, 0)], new float2(1f, 0f));
                float2 nl = new float2(-t.y, t.x);
                m_Left.Add(path.Xz[i] + nl * m_HwL[i]);
                m_Right.Add(path.Xz[i] - nl * m_HwR[i]);
                maxHw = Math.Max(maxHw, Math.Max(m_HwL[i], m_HwR[i]));
            }
            int window = (int)math.ceil((4f * maxHw + 20f) / Math.Max(path.StepM, 0.1f));
            List<float2> l = GradeRibbon.Thin(GradeRibbon.Simplify(GradeRibbon.CutLoops(m_Left, window), RingSimplifyM), GradeRibbon.MinSpacingM);
            List<float2> r = GradeRibbon.Thin(GradeRibbon.Simplify(GradeRibbon.CutLoops(m_Right, window), RingSimplifyM), GradeRibbon.MinSpacingM);
            if (l.Count < 2 || r.Count < 2)
                return AutoReject.Crosses;
            ring = new float2[l.Count + r.Count];
            for (int i = 0; i < l.Count; i++)
                ring[i] = l[i];
            for (int i = 0; i < r.Count; i++)
                ring[l.Count + i] = r[r.Count - 1 - i];
            if (!GradeRibbon.Clear(ring, GradeRibbon.MinClearanceM))
                return AutoReject.Crosses;
            if (merge)
            {
                float2 end = path.Xz[n - 1];
                float best = float.PositiveInfinity;
                for (int a = 0; a < m_Accepted.Count; a++)
                {
                    float d = PointRingDistance(m_Accepted[a].Ring, end);
                    if (d < best)
                    {
                        best = d;
                        mergeInto = a;
                    }
                }
            }
            return CheckRing(ring, gap, mergeInto);
        }

        /// <summary>Free half width along dir: steps of MarchStepM until a cell is not open, is cliff, is at or over the pass ceiling,
        /// or lies within gapM of an accepted run; minus WidthMarginM, at least 1 m.</summary>
        private float March(float2 p, float2 dir, float maxHw, float ceil, float gapM)
        {
            float free = 0f;
            for (float t = MarchStepM; t <= maxHw + WidthMarginM; t += MarchStepM)
            {
                if (!Cell(p + dir * t, out int c))
                    break;
                if ((m_Flags[c] & (FOpen | FCliff)) != FOpen || !(m_O[c] < ceil) || m_DAcc[c] < gapM)
                    break;
                free = t;
            }
            return Math.Max(1f, Math.Min(maxHw, free - WidthMarginM));
        }

        /// <summary>The authoritative pre-check (skeptic: the engine is not a safety net with Anarchy on): the ring lies inside the
        /// region and the map, keeps every hard obstacle's clearance, and keeps ExactGapShare of the gap from every accepted run
        /// (MergeGapM from the run it merges into).</summary>
        private AutoReject CheckRing(float2[] ring, float gap, int mergeInto)
        {
            HeightSnapshot snap = m_In.Snapshot;
            for (int i = 0; i < ring.Length; i++)
            {
                float2 p = ring[i];
                if (!GradeRaster.Inside(m_Region, p))
                    return AutoReject.Region;
                float hx = (p.x + snap.Offset.x) * snap.Scale.x, hz = (p.y + snap.Offset.z) * snap.Scale.z;
                if (hx < OffMapMarginTexels || hz < OffMapMarginTexels || hx > snap.ResX - 1 - OffMapMarginTexels || hz > snap.ResZ - 1 - OffMapMarginTexels)
                    return AutoReject.Region;
            }
            if (EdgeDistance(ring, m_Region, RegionClearanceM, out _, out _, out _, out _) < RegionClearanceM)
                return AutoReject.Region;
            if (!ObstaclesClear(ring, m_In.Obstacles, m_Knobs.Density))
                return AutoReject.Obstacle;
            for (int a = 0; a < m_Accepted.Count; a++)
            {
                Accepted acc = m_Accepted[a];
                float need = a == mergeInto ? MergeGapM : ExactGapShare * Math.Min(gap, acc.GapM);
                if (RingDistance(ring, acc.Ring, need, out _, out _, out _, out _) < need)
                    return AutoReject.Overlap;
            }
            return AutoReject.Kept;
        }

        /// <summary>True when the ring keeps every hard obstacle's clearance (ClearanceOf), containment included. Public: the host
        /// re-checks every output with it, and the tool re-checks on Enter after re-collecting obstacles (FEASIBILITY 4 row 12).</summary>
        public static bool ObstaclesClear(float2[] ring, List<AutoObstacle> obstacles, AutoDensity density)
        {
            if (obstacles == null)
                return true;
            BBox(ring, out float2 lo, out float2 hi);
            for (int o = 0; o < obstacles.Count; o++)
            {
                AutoObstacle ob = obstacles[o];
                if (ob == null || ob.Ring == null || ob.Ring.Length < 2 || ob.Kind == AutoObstacleKind.CatTrack)
                    continue;
                float clear = ClearanceOf(ob.Kind, density);
                BBox(ob.Ring, out float2 olo, out float2 ohi);
                if (math.any(ohi < lo - clear) || math.any(olo > hi + clear))
                    continue;
                if (ob.Ring.Length >= 3)
                {
                    if (RingDistance(ring, ob.Ring, clear, out _, out _, out _, out _) < clear)
                        return false;
                }
                else
                {
                    if (GradeRaster.Inside(ring, ob.Ring[0]) || GradeRaster.Inside(ring, ob.Ring[1]))
                        return false;
                    for (int i = 0; i < ring.Length; i++)
                        if (GradeRibbon.SegSegDistance(ring[i], ring[(i + 1) % ring.Length], ob.Ring[0], ob.Ring[1]) < clear)
                            return false;
                }
            }
            return true;
        }

        private AutoReject Rate(Cand cand, float2[] ring, GradePath path, bool merge, int mergeInto, bool reachesEnd, float gap)
        {
            int n = ring.Length;
            Grow(ref m_RateNodes, n);
            var sampler = new SnapshotSampler(m_In.Snapshot);
            for (int i = 0; i < n; i++)
            {
                var w = new float3(ring[i].x, 0f, ring[i].y);
                w.y = sampler.Sample(w);
                m_RateNodes[i] = w;
            }
            m_Result.Rated++;
            RateStatus rs = SkiRunRater.Rate(m_RateNodes, n, m_Origin, ref sampler, m_Texel, 0, m_RateGrid, null, m_RateResult,
                out int _, out float _, out float _);
            SlopeResult res = m_RateResult;
            if (rs != RateStatus.Rated || res.Status != SlopeStatus.Ok || !res.Connected || sampler.Misses > 0)
                return AutoReject.Unrated;
            if (res.Band == SlopeBand.Flat)
                return AutoReject.Flat;
            var acc = new Accepted
            {
                Cand = cand, Ring = ring, Band = res.Band, RatingGrade = res.RatingGrade, MeanGrade = res.MeanGrade,
                SteepestGrade = res.SteepestGrade, DropM = res.Drop, AreaM2 = (float)PolygonArea(ring), LengthM = res.Length, TopY = res.MaxHeight,
                BottomY = res.MinHeight, GapM = gap, Merge = merge, MergeInto = merge ? mergeInto : -1,
            };
            acc.Centerline = new float2[path.Count];
            Array.Copy(path.Xz, acc.Centerline, path.Count);
            // Base contact, water and cat tracks from the rating Mask (the run's own cells)
            if (Cell(path.Xz[path.Count - 1], out int endCell))
                acc.TouchesBase = !merge && reachesEnd && (m_Flags[endCell] & FBase) != 0;
            SlopeGrid g = m_RateGrid;
            float seedD = -0.5f * m_Texel;
            int mi0 = int.MaxValue, mj0 = int.MaxValue, mi1 = int.MinValue, mj1 = int.MinValue;
            for (int j = 0; j < g.Nz; j++)
            {
                int lj = g.J0 + j - m_LJ0;
                if (lj < 0 || lj >= m_Nz)
                    continue;
                for (int i = 0; i < g.Nx; i++)
                {
                    if (g.Mask[j * g.Nx + i] == 0)
                        continue;
                    int li = g.I0 + i - m_LI0;
                    if (li < 0 || li >= m_Nx)
                        continue;
                    int c = lj * m_Nx + li;
                    byte f = m_Flags[c];
                    if ((f & FWet) != 0)
                        acc.OverWater = true;
                    if ((f & FCat) != 0)
                        acc.CrossesCatTrack = true;
                    m_DAcc[c] = Math.Min(m_DAcc[c], seedD);
                    mi0 = Math.Min(mi0, li);
                    mi1 = Math.Max(mi1, li);
                    mj0 = Math.Min(mj0, lj);
                    mj1 = Math.Max(mj1, lj);
                }
            }
            for (int e = 0; e < n; e++)
            {
                float2 a = ring[e], b = ring[(e + 1) % n];
                int steps = Math.Max(1, (int)math.ceil(math.distance(a, b) / (0.5f * m_Texel)));
                for (int s = 0; s <= steps; s++)
                    if (Cell(math.lerp(a, b, s / (float)steps), out int c))
                    {
                        m_DAcc[c] = Math.Min(m_DAcc[c], seedD);
                        mi0 = Math.Min(mi0, c % m_Nx);
                        mi1 = Math.Max(mi1, c % m_Nx);
                        mj0 = Math.Min(mj0, c / m_Nx);
                        mj1 = Math.Max(mj1, c / m_Nx);
                    }
            }
            // Local chamfer: only distances under the widest gap plus the widest half width matter
            int r = (int)math.ceil((SparseGapM + 0.5f * TargetWidthM[0] + MinHalfWidthM + WidthMarginM) / m_Texel) + 2;
            int i0 = Math.Max(0, mi0 - r), i1 = Math.Min(m_Nx - 1, mi1 + r), j0 = Math.Max(0, mj0 - r), j1 = Math.Min(m_Nz - 1, mj1 + r);
            Chamfer(m_DAcc, m_Nx, i0, i1, j0, j1, j0, j1, true, m_Texel);
            Chamfer(m_DAcc, m_Nx, i0, i1, j0, j1, j0, j1, false, m_Texel);
            acc.Score = SkiResortScore.SizeFactor(acc.AreaM2) * SkiResortScore.BandValueOf(SkiResortScore.BandState.Rated, (byte)acc.Band) *
                        (1.0 + Math.Min(1.0, Math.Max(0.0, acc.DropM / SkiResortScore.VerticalRefM)));
            m_Accepted.Add(acc);
            m_Have[(int)acc.Band]++;
            return AutoReject.Kept;
        }

        // ---------------------------------------------------------------- stages 7b-9: junctions, kinds, clusters, warnings

        private void UnitFinish()
        {
            AutoPlanResult res = m_Result;
            res.Runs.Clear();
            res.Junctions.Clear();
            res.Clusters.Clear();
            res.Warnings = m_FieldWarnings;
            Array.Clear(res.BandCount, 0, 5);
            Array.Copy(m_Target, res.BandTarget, 5);
            int nRuns = m_Accepted.Count;
            var rings = new List<float2>[nRuns];
            for (int a = 0; a < nRuns; a++)
                rings[a] = new List<float2>(m_Accepted[a].Ring);
            var pairs = new List<JunctionDraft>();
            var sampler = new SnapshotSampler(m_In.Snapshot);
            // Run pairs within reach: the topmost close point pair (service flows down from there), plus the merge point
            for (int a = 0; a < nRuns; a++)
                for (int b = a + 1; b < nRuns; b++)
                {
                    float2[] ra = m_Accepted[a].Ring, rb = m_Accepted[b].Ring;
                    if (TopmostPair(ra, rb, ref sampler, out float2 pa, out int ea, out float2 pb, out int eb))
                        pairs.Add(new JunctionDraft { A = a, B = b, PA = pa, PB = pb, EA = ea, EB = eb });
                    int into = m_Accepted[b].MergeInto == a ? b : m_Accepted[a].MergeInto == b ? a : -1;
                    if (into >= 0 && RingDistance(ra, rb, JunctionReachM, out float2 qa, out float2 qb, out int fa, out int fb) <= JunctionReachM)
                    {
                        bool dup = pairs.Count > 0 && pairs[pairs.Count - 1].A == a && pairs[pairs.Count - 1].B == b &&
                                   math.distance(pairs[pairs.Count - 1].PA, qa) < 2f * GradeRibbon.MinSpacingM;
                        if (dup)
                            pairs[pairs.Count - 1] = WithMerge(pairs[pairs.Count - 1]);
                        else
                            pairs.Add(new JunctionDraft { A = a, B = b, PA = qa, PB = qb, EA = fa, EB = fb, Merge = true });
                    }
                }
            // Existing runs: our node at the closest point to their topmost node within reach
            var existing = new List<JunctionDraft>();
            List<AutoObstacle> obs = m_In.Obstacles;
            if (obs != null)
                for (int a = 0; a < nRuns; a++)
                    for (int o = 0; o < obs.Count; o++)
                    {
                        AutoObstacle ob = obs[o];
                        if (ob == null || ob.Kind != AutoObstacleKind.ExistingRun || ob.Ring == null || ob.Ring.Length < 3)
                            continue;
                        float2[] ra = m_Accepted[a].Ring;
                        int bestV = -1, bestE = -1;
                        float bestH = float.NegativeInfinity;
                        float2 bestP = default;
                        for (int v = 0; v < ob.Ring.Length; v++)
                        {
                            float2 q = ob.Ring[v];
                            for (int e = 0; e < ra.Length; e++)
                            {
                                float d = GradePath.SegDistance(q, ra[e], ra[(e + 1) % ra.Length], out float u);
                                if (d > JunctionReachM)
                                    continue;
                                float h = sampler.Sample(new float3(q.x, 0f, q.y));
                                if (h > bestH)
                                {
                                    bestH = h;
                                    bestV = v;
                                    bestE = e;
                                    bestP = math.lerp(ra[e], ra[(e + 1) % ra.Length], u);
                                }
                            }
                        }
                        if (bestV >= 0)
                            existing.Add(new JunctionDraft { A = a, B = o, PA = bestP, EA = bestE, NodeB = bestV, Existing = true });
                    }
            // Insert every node (by position, so later insertions cannot shift a recorded index), then resolve indices
            var placedA = new float2[pairs.Count + existing.Count];
            var placedB = new float2[pairs.Count];
            for (int p = 0; p < pairs.Count; p++)
            {
                JunctionDraft jd = pairs[p];
                placedA[p] = Place(rings[jd.A], jd.PA);
                placedB[p] = Place(rings[jd.B], jd.PB);
            }
            for (int p = 0; p < existing.Count; p++)
                placedA[pairs.Count + p] = Place(rings[existing[p].A], existing[p].PA);

            for (int a = 0; a < nRuns; a++)
            {
                Accepted acc = m_Accepted[a];
                var run = new AutoRun
                {
                    Id = acc.Cand.Id, Cluster = acc.Cand.Cluster, Band = acc.Band, PassBand = (SlopeBand)(acc.Cand.Pass + 1),
                    Ring = rings[a].ToArray(), Centerline = acc.Centerline, RatingGrade = acc.RatingGrade, MeanGrade = acc.MeanGrade,
                    SteepestGrade = acc.SteepestGrade, DropM = acc.DropM, AreaM2 = acc.AreaM2, LengthM = acc.LengthM, TopY = acc.TopY,
                    BottomY = acc.BottomY, Merge = acc.Merge, MergeInto = acc.MergeInto, TouchesBase = acc.TouchesBase,
                    OverWater = acc.OverWater, CrossesCatTrack = acc.CrossesCatTrack, Score = acc.Score,
                };
                run.Nodes = new float3[run.Ring.Length];
                for (int i = 0; i < run.Ring.Length; i++)
                {
                    var w = new float3(run.Ring[i].x, 0f, run.Ring[i].y);
                    w.y = sampler.Sample(w);
                    run.Nodes[i] = w;
                }
                res.Runs.Add(run);
                res.BandCount[(int)run.Band]++;
            }
            for (int p = 0; p < pairs.Count; p++)
            {
                JunctionDraft jd = pairs[p];
                int na = IndexOf(res.Runs[jd.A].Ring, placedA[p]), nb = IndexOf(res.Runs[jd.B].Ring, placedB[p]);
                res.Junctions.Add(new AutoJunction
                {
                    RunA = jd.A, NodeA = na, RunB = jd.B, NodeB = nb, Merge = jd.Merge,
                    DistanceM = math.distance(res.Runs[jd.A].Ring[na], res.Runs[jd.B].Ring[nb]),
                });
            }
            for (int p = 0; p < existing.Count; p++)
            {
                JunctionDraft jd = existing[p];
                int na = IndexOf(res.Runs[jd.A].Ring, placedA[pairs.Count + p]);
                res.Junctions.Add(new AutoJunction
                {
                    RunA = jd.A, NodeA = na, RunB = jd.B, NodeB = jd.NodeB, Existing = true,
                    DistanceM = math.distance(res.Runs[jd.A].Ring[na], obs[jd.B].Ring[jd.NodeB]),
                });
            }
            AssignKinds(res.Runs, m_Knobs.Kinds);
            BuildClusters(res);
            Warn(res);
            res.Status = res.Runs.Count > 0 ? AutoPlanStatus.Ok : AutoPlanStatus.NoRuns;
            res.Fingerprint = Fingerprint(res);
            m_Stage = Stage.Done;
        }

        private struct JunctionDraft
        {
            public int A, B, EA, EB, NodeB;
            public float2 PA, PB;
            public bool Merge, Existing;
        }

        private static JunctionDraft WithMerge(JunctionDraft j)
        {
            j.Merge = true;
            return j;
        }

        /// <summary>Among edge pairs of a and b within JunctionReachM, the closest-point pair with the highest terrain at its middle.</summary>
        private static bool TopmostPair(float2[] a, float2[] b, ref SnapshotSampler sampler, out float2 pa, out int ea, out float2 pb,
            out int eb)
        {
            pa = pb = default;
            ea = eb = -1;
            float bestH = float.NegativeInfinity;
            for (int i = 0; i < a.Length; i++)
            {
                float2 a0 = a[i], a1 = a[(i + 1) % a.Length];
                float2 alo = math.min(a0, a1) - JunctionReachM, ahi = math.max(a0, a1) + JunctionReachM;
                for (int k = 0; k < b.Length; k++)
                {
                    float2 b0 = b[k], b1 = b[(k + 1) % b.Length];
                    if (math.any(math.max(b0, b1) < alo) || math.any(math.min(b0, b1) > ahi))
                        continue;
                    if (SegClosest(a0, a1, b0, b1, out float2 p, out float2 q) > JunctionReachM)
                        continue;
                    float2 mid = 0.5f * (p + q);
                    float h = sampler.Sample(new float3(mid.x, 0f, mid.y));
                    if (h > bestH)
                    {
                        bestH = h;
                        pa = p;
                        pb = q;
                        ea = i;
                        eb = k;
                    }
                }
            }
            return ea >= 0;
        }

        /// <summary>Inserts p into the ring on its nearest edge (InsertNode) and returns the vertex actually used.</summary>
        private static float2 Place(List<float2> ring, float2 p)
        {
            int best = 0;
            float bd = float.PositiveInfinity;
            for (int e = 0; e < ring.Count; e++)
            {
                float d = GradePath.SegDistance(p, ring[e], ring[(e + 1) % ring.Count], out float _);
                if (d < bd)
                {
                    bd = d;
                    best = e;
                }
            }
            return ring[InsertNode(ring, best, p)];
        }

        private static int IndexOf(float2[] ring, float2 p)
        {
            int best = 0;
            float bd = float.PositiveInfinity;
            for (int i = 0; i < ring.Length; i++)
            {
                float d = math.distancesq(ring[i], p);
                if (d < bd)
                {
                    bd = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Stage 9 kinds among the allowed ones (design 3.1 row 9, owner rulings): groomed by default, natural for black and
        /// double black, snowmaking for greens that reach the base, night for at most two base runs (lowest tops first).</summary>
        public static void AssignKinds(List<AutoRun> runs, AutoKinds allowed)
        {
            if (allowed == AutoKinds.None)
                allowed = AutoKinds.Default;
            RunKind fallback = (allowed & AutoKinds.Groomed) != 0 ? RunKind.Groomed
                : (allowed & AutoKinds.Natural) != 0 ? RunKind.Natural
                : (allowed & AutoKinds.Snowmaking) != 0 ? RunKind.Snowmaking : RunKind.Night;
            for (int i = 0; i < runs.Count; i++)
            {
                AutoRun r = runs[i];
                r.Kind = fallback;
                if (r.Band >= SlopeBand.Black && (allowed & AutoKinds.Natural) != 0)
                    r.Kind = RunKind.Natural;
                else if (r.Band == SlopeBand.Green && r.TouchesBase && (allowed & AutoKinds.Snowmaking) != 0)
                    r.Kind = RunKind.Snowmaking;
            }
            if ((allowed & AutoKinds.Night) == 0)
                return;
            for (int pick = 0; pick < 2; pick++)
            {
                int best = -1;
                for (int i = 0; i < runs.Count; i++)
                {
                    AutoRun r = runs[i];
                    if (!r.TouchesBase || r.Band > SlopeBand.Blue || r.Kind == RunKind.Night)
                        continue;
                    if (best < 0 || r.TopY < runs[best].TopY || (r.TopY == runs[best].TopY && r.Id < runs[best].Id))
                        best = i;
                }
                if (best < 0)
                    break;
                runs[best].Kind = RunKind.Night;
            }
        }

        private void BuildClusters(AutoPlanResult res)
        {
            int n = res.Runs.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++)
                parent[i] = i;
            for (int j = 0; j < res.Junctions.Count; j++)
            {
                AutoJunction jn = res.Junctions[j];
                if (jn.Existing)
                    continue;
                int a = Root(parent, jn.RunA), b = Root(parent, jn.RunB);
                if (a != b)
                    parent[Math.Max(a, b)] = Math.Min(a, b);
            }
            for (int c = 0; c < m_Clusters.Count; c++)
            {
                ClusterInfo ci = m_Clusters[c];
                var cl = new AutoCluster { Center = ci.Center, TopY = ci.TopY, Pod = ci.Pod, PodBand = ci.Pod ? (SlopeBand)(ci.PodPass + 1) : SlopeBand.Flat };
                var size = new int[Math.Max(1, n)];
                for (int i = 0; i < n; i++)
                {
                    if (res.Runs[i].Cluster != c)
                        continue;
                    cl.Runs++;
                    if (res.Runs[i].Band == SlopeBand.Green)
                        cl.HasGreen = true;
                    int root = Root(parent, i);
                    size[root]++;
                    cl.Linked = Math.Max(cl.Linked, size[root]);
                }
                res.Clusters.Add(cl);
            }
        }

        private static int Root(int[] p, int i)
        {
            while (p[i] != i)
                i = p[i] = p[p[i]];
            return i;
        }

        private void Warn(AutoPlanResult res)
        {
            if (res.Runs.Count < 2)
                res.Warnings |= AutoPlanWarning.FewRuns;
            if (m_Target[1] > 0 && res.BandCount[1] == 0)
                res.Warnings |= AutoPlanWarning.NoGreen;
            if (m_Target[2] > 0 && res.BandCount[2] == 0)
                res.Warnings |= AutoPlanWarning.NoBlue;
            if (m_Target[3] > 0 && res.BandCount[3] == 0)
                res.Warnings |= AutoPlanWarning.NoBlack;
            for (int b = 1; b <= 4; b++)
                if (res.BandCount[b] < m_Target[b])
                    res.Warnings |= AutoPlanWarning.MixShort;
            for (int c = 0; c < res.Clusters.Count; c++)
                if (res.Clusters[c].Runs > 0 && !res.Clusters[c].HasGreen)
                    res.Warnings |= AutoPlanWarning.NoEasyWayHome;
            for (int i = 0; i < res.Runs.Count; i++)
            {
                if (res.Runs[i].OverWater)
                    res.Warnings |= AutoPlanWarning.OverWater;
                if (res.Runs[i].CrossesCatTrack)
                    res.Warnings |= AutoPlanWarning.CrossesCatTrack;
            }
        }

        /// <summary>FNV-1a over every output bit that matters (rings, bands, kinds, junctions, status, warnings).</summary>
        public static ulong Fingerprint(AutoPlanResult res)
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, (uint)res.Status);
            h = Mix(h, (uint)res.Warnings);
            for (int i = 0; i < res.Runs.Count; i++)
            {
                AutoRun r = res.Runs[i];
                h = Mix(h, (uint)r.Id);
                h = Mix(h, (uint)r.Band);
                h = Mix(h, (uint)r.Kind);
                for (int k = 0; k < r.Ring.Length; k++)
                {
                    h = Mix(h, math.asuint(r.Ring[k].x));
                    h = Mix(h, math.asuint(r.Ring[k].y));
                }
            }
            for (int j = 0; j < res.Junctions.Count; j++)
            {
                AutoJunction jn = res.Junctions[j];
                h = Mix(h, (uint)jn.RunA);
                h = Mix(h, (uint)jn.NodeA);
                h = Mix(h, (uint)jn.RunB);
                h = Mix(h, (uint)jn.NodeB);
            }
            return h;
        }

        private static ulong Mix(ulong h, uint v) => (h ^ v) * 1099511628211UL;
    }
}
