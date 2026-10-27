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
            public float2 Lo, Hi;   // ring bounding box (prefilters CheckRing and the junction pairs)
        }

        private readonly List<ClusterInfo> m_Clusters = new List<ClusterInfo>();
        private readonly List<int> m_PodPasses = new List<int>();
        private readonly List<SeedRec> m_Seeds = new List<SeedRec>();
        private readonly List<Cand> m_Cands = new List<Cand>();
        private readonly List<int> m_Paths = new List<int>();
        private readonly HashSet<ulong> m_PathHashes = new HashSet<ulong>();
        private readonly List<Accepted> m_Accepted = new List<Accepted>();
        private readonly DijkstraQueue m_AHeap = new DijkstraQueue();
        private readonly List<int> m_Touched = new List<int>();
        private long[] m_SortKeys = new long[256];
        private AutoPlanWarning m_FieldWarnings;
        private readonly int[] m_Target = new int[5], m_Have = new int[5];
        private int m_RunCap, m_MaxRatings;

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
            r.Resort = null;
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

        // ---------------------------------------------------------------- stage 5a: clusters and seeds (sliced)
        // m_Cursor2 phases: 0 setup, 1 top-cell rows, 2 one summit pick per unit, 3 pod rows (one pod after another), 4 seed-cell rows
        // (every cluster at once), 5 one (cluster, pass) seed list per unit (sort and space).

        private int m_NTop, m_SummitCap, m_PodIdx, m_PodBest;
        private readonly bool[] m_TopReach = new bool[Passes];
        private float[] m_TopD = new float[256];
        private float m_SeedBand;
        private List<long>[] m_SeedLists = new List<long>[0];

        private void UnitSeeds()
        {
            // Resort hook (A5, Fans.cs): true = the fan seeding handled this unit (it owns m_Cursor2 / Next(Stage.Candidates) and does the
            // case 0 bookkeeping below: m_FieldWarnings, ClearCandidates); false = run the v1 seeding
            if (m_Mode == AutoPlanMode.Resort && FanSeeds())
                return;
            switch (m_Cursor2)
            {
                case 0:
                    m_FieldWarnings = m_Result.Warnings;
                    ClearCandidates();
                    m_NTop = 0;
                    m_SummitCap = ClusterCap(m_Result.RegionAreaM2);
                    Array.Clear(m_TopReach, 0, Passes);
                    m_Cursor = m_RegionJ0;
                    m_Cursor2 = 1;
                    return;
                case 1:
                    TopRows();
                    return;
                case 2:
                    if (!PickSummit())
                    {
                        m_PodIdx = 0;
                        m_PodBest = -1;
                        m_Cursor = m_RegionJ0;
                        m_Cursor2 = m_PodPasses.Count > 0 ? 3 : 4;
                        if (m_Cursor2 == 4)
                            BeginSeedRows();
                    }
                    return;
                case 3:
                    PodRows();
                    return;
                case 4:
                    SeedRows();
                    return;
                default:
                    if (m_Cursor < m_Clusters.Count * Passes)
                        ClusterSeeds(m_Cursor / Passes, m_Cursor % Passes);
                    m_Cursor++;
                    if (m_Cursor >= m_Clusters.Count * Passes)
                        Next(Stage.Candidates);
                    return;
            }
        }

        /// <summary>Top cells (usable, in the upper TopShare of the skiable vertical, at least TopBandM, reaching the base below the
        /// double black ceiling) into m_Order, plus per pass whether a top cell keeps that pass's band to the base. The top line hangs
        /// from m_HReach, the highest ground that reaches the base (A: a steep summit cap above it is advisory, never a refusal). Row
        /// chunks over the region bbox.</summary>
        private void TopRows()
        {
            int dbCeil = Bucket(PassCeiling[Passes - 1]);
            float topLine = m_HReach - Math.Max(TopBandM, TopShare * (m_HReach - m_HMinU));
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    bool top = (m_Flags[c] & (FOpen | FCliff)) == FOpen && m_H[c] >= topLine && m_M[c] < dbCeil;
                    if (!top)
                        continue;
                    m_Order[m_NTop++] = c;
                    for (int p = 0; p < Passes - 1; p++)
                        if (InBand(p, c))
                            m_TopReach[p] = true;
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            // E, pods (real-world-layout.md 5): green and blue always get up to MaxPodsPerPass pods each at the highest ground that pass
            // keeps its band to the base (the beginner pods on the gentle lower slope, blue mid-stations), black only when no top cell
            // keeps the black band. They come on top of the summits.
            m_PodPasses.Clear();
            int perPass = MaxPodsPerPass(m_Result.RegionAreaM2);
            for (int p = 0; p < Passes - 1; p++)
                if (p < 2 || !m_TopReach[p])
                    for (int k = 0; k < perPass; k++)
                        m_PodPasses.Add(p);
            Grow(ref m_TopD, Math.Max(1, m_NTop));
            for (int k = 0; k < m_NTop; k++)
                m_TopD[k] = float.PositiveInfinity;
            m_Cursor2 = 2;
        }

        /// <summary>One summit: the highest top cell at least ClusterSpacingM from every cluster (a local top across the upper slope).
        /// After the first (the true summit) the height carries a seeded jitter of SummitJitterM, so Shuffle moves the other lift tops.
        /// m_TopD keeps each top cell's distance to the nearest cluster, updated with the newest one: one pass over the top cells per
        /// pick. False when the cap is reached or no cell is far enough.</summary>
        private bool PickSummit()
        {
            ClusterInfo last = m_Clusters.Count > 0 ? m_Clusters[m_Clusters.Count - 1] : null;
            if (m_Clusters.Count >= m_SummitCap)
                return false;
            int best = -1;
            float bestKey = float.NegativeInfinity;
            bool first = m_Clusters.Count == 0;
            for (int k = 0; k < m_NTop; k++)
            {
                int c = m_Order[k];
                if (last != null)
                    m_TopD[k] = Math.Min(m_TopD[k], math.distance(CellCenter(c), last.Center));
                if (m_TopD[k] < ClusterSpacingM)
                    continue;
                float key = first ? m_H[c] : m_H[c] + SummitJitterM * Jitter(m_Knobs.Seed, c, 7);
                if (key > bestKey)
                {
                    bestKey = key;
                    best = c;
                }
            }
            if (best < 0)
                return false;
            AddCluster(best, -1);
            return true;
        }

        /// <summary>The current pod's cell: the highest cell its pass keeps the band from to the base, MinDropM above the base,
        /// EdgeSeedMarginM inside the region and ClusterRadiusM (half the summit spacing) from every cluster, summits and pods (row
        /// chunks; the pods are placed one after another, each away from the ones before). A pass that finds no cell skips its
        /// remaining pods.</summary>
        private void PodRows()
        {
            int p = m_PodPasses[m_PodIdx];
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    if (!InBand(p, c) || m_H[c] - SourceTop(p) < MinDropM || (m_PodBest >= 0 && m_H[c] <= m_H[m_PodBest]) ||
                        !(m_DHard[c] >= EdgeSeedMarginM))
                        continue;
                    if (m_Clusters.Count > 0 && NearestCenter(CellCenter(c), out int _) < ClusterRadiusM)
                        continue;
                    m_PodBest = c;
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            if (m_PodBest >= 0)
                AddCluster(m_PodBest, p);
            else
                while (m_PodIdx + 1 < m_PodPasses.Count && m_PodPasses[m_PodIdx + 1] == p)
                    m_PodIdx++;
            m_PodBest = -1;
            m_Cursor = m_RegionJ0;
            if (++m_PodIdx >= m_PodPasses.Count)
            {
                m_Cursor2 = 4;
                BeginSeedRows();
            }
        }

        // D: seeds across each cluster's top band, not a disc. Seed-cell rows (m_Cursor2 4) walk the region once for every cluster:
        // a usable cell on the 2-cell seed lattice, EdgeSeedMarginM inside the region, joins the list of the nearest cluster k whose
        // band [TopY_k - m_SeedBand, TopY_k] holds its height (BandOwner, review F6) for every pass that keeps its band
        // from there. Then one (cluster, pass) list per unit is sorted by height plus a seeded jitter and spaced (ClusterSeeds).
        private void BeginSeedRows()
        {
            m_SeedBand = Math.Max(TopBandM, 0.5f * TopShare * (m_HReach - m_HMinU));
            int lists = m_Clusters.Count * Passes;
            if (m_SeedLists.Length < lists)
            {
                var grown = new List<long>[lists];
                Array.Copy(m_SeedLists, grown, m_SeedLists.Length);
                m_SeedLists = grown;
            }
            for (int k = 0; k < lists; k++)
            {
                if (m_SeedLists[k] == null)
                    m_SeedLists[k] = new List<long>();
                m_SeedLists[k].Clear();
            }
            m_Cursor = m_RegionJ0;
        }

        private void SeedRows()
        {
            float lowest = float.PositiveInfinity;
            for (int k = 0; k < m_Clusters.Count; k++)
                lowest = Math.Min(lowest, m_Clusters[k].TopY - m_SeedBand);
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
            {
                if (((m_LJ0 + j) & 1) != 0)
                    continue;
                for (int i = m_RegionI0 + ((m_LI0 + m_RegionI0) & 1); i <= m_RegionI1; i += 2)
                {
                    int c = j * m_Nx + i;
                    byte bits = m_InBand[c];
                    if (bits == 0 || m_H[c] < lowest || !(m_DHard[c] >= EdgeSeedMarginM))
                        continue;
                    int owner = BandOwner(CellCenter(c), m_H[c]);
                    if (owner < 0)
                        continue;
                    for (int p = 0; p < Passes; p++)
                    {
                        if ((bits & (1 << p)) == 0)
                            continue;
                        float key = m_H[c] + SeedJitterM * Jitter(m_Knobs.Seed, c, p);
                        long q = (long)Math.Round((1.0e6 - key) * 1000.0);
                        m_SeedLists[owner * Passes + p].Add((q << 21) | (long)c);
                    }
                }
            }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            m_Cursor = 0;
            m_Cursor2 = 5;
        }

        /// <summary>Seeds of one cluster and pass: its seed cells ordered by height plus a seeded jitter of SeedJitterM, spaced one
        /// corridor plus NormalGapM apart (density-free on purpose: a Density re-select restarts at Select), at most SeedsPerClusterPass.</summary>
        private void ClusterSeeds(int cl, int p)
        {
            List<long> list = m_SeedLists[cl * Passes + p];
            int n = list.Count;
            Grow(ref m_SortKeys, Math.Max(1, n));   // Grow does not copy: size it once, before the fill
            list.CopyTo(m_SortKeys);
            Array.Sort(m_SortKeys, 0, n);           // keys are unique (the cell is in the low bits): a deterministic order
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

        private bool InBand(int pass, int c) => (m_InBand[c] & (1 << pass)) != 0;

        /// <summary>Pods per pass for a region of this area: round(2 x AreaScale) (2 at 1 km2, 6 at 9 km2).</summary>
        public static int MaxPodsPerPass(float areaM2) => (int)Math.Round(2f * AreaScale(areaM2));

        /// <summary>Most pods a region of this area can get: green, blue and black, MaxPodsPerPass each.</summary>
        public static int PodCap(float areaM2) => (Passes - 1) * MaxPodsPerPass(areaM2);

        /// <summary>Summit clusters for a region of this area: 3 per km2 of side, sqrt(km2) clamped to [1, 3] (the v1 ruling's 3 top
        /// clusters at 1 km2, 9 at the 9 km2 cap). Pods come on top (PodCap).</summary>
        public static int ClusterCap(float areaM2) => (int)Math.Round(MaxClusters * AreaScale(areaM2));

        /// <summary>sqrt(area in km2) clamped to [1, 3]: how many 1 km2 slopes fit across the region (caps scale with the width).</summary>
        public static float AreaScale(float areaM2) => math.clamp(math.sqrt(Math.Max(0f, areaM2) / 1.0e6f), 1f, 3f);

        /// <summary>The run cap: the knob's cap, else the density's per km2 (8 / 12 / 16) times the area in km2 (AreaScale squared:
        /// at least 1 km2; the cap used to grow with the side only, which held a 4 km2 mountain to 6 runs per km2 on Normal), at most
        /// HardRunCap.</summary>
        public static int RunCapOf(AutoPlanKnobs knobs, float areaM2)
        {
            float km2 = AreaScale(areaM2) * AreaScale(areaM2);
            int cap = knobs.RunCap > 0 ? knobs.RunCap
                : (int)Math.Round((knobs.Density == AutoDensity.Sparse ? 8 : knobs.Density == AutoDensity.Dense ? 16 : 12) * km2);
            return Math.Min(cap, HardRunCap);
        }

        private void AddCluster(int c, int podPass)
        {
            m_Clusters.Add(new ClusterInfo { Cell = c, Center = CellCenter(c), TopY = m_H[c], Pod = podPass >= 0, PodPass = podPass });
        }

        /// <summary>The nearest cluster whose seed band [TopY - m_SeedBand, TopY] holds height h, or -1. Review F6: the nearest center
        /// among ALL clusters gave a summit's band cells to a pod 150 m away, whose band lies lower, and nobody seeded there.</summary>
        private int BandOwner(float2 p, float h)
        {
            int owner = -1;
            float best = float.PositiveInfinity;
            for (int k = 0; k < m_Clusters.Count; k++)
            {
                ClusterInfo ci = m_Clusters[k];
                if (h > ci.TopY || h < ci.TopY - m_SeedBand)
                    continue;
                float d = math.distance(p, ci.Center);
                if (d < best)
                {
                    best = d;
                    owner = k;
                }
            }
            return owner;
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
            float len = 0f;
            ulong hash = 14695981039346656037UL;
            int guard = m_N;
            while (guard-- > 0)
            {
                m_Paths.Add(c);
                hash = (hash ^ (ulong)(uint)c) * 1099511628211UL;
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
            // C: the expected band is the band of the line's steepest cell (a seed is in band, so it is at most the pass's band). The
            // percentile tried first (P90) mislabeled half the blue lines: the rater's crux is a minimax.
            float maxO = 0f;
            for (int k = 0; k < count; k++)
                maxO = Math.Max(maxO, m_O[m_Paths[start + k]]);
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
            // Shuffle (P2): a seeded factor in [1 - ScoreJitter / 2, 1 + ScoreJitter / 2] changes which lines win, not only ties
            score *= 1.0 + ScoreJitter * (Jitter(m_Knobs.Seed, s.Cell, 16 + s.Pass) - 0.5);
            m_Cands.Add(new Cand
            {
                Id = s.Cluster * 1000 + s.Pass * 100 + s.Ordinal,
                Cluster = s.Cluster, Pass = s.Pass, PathStart = start, PathLen = count, Expected = expected,
                LengthM = len, DropM = drop, Score0 = score,
            });
        }

        // ---------------------------------------------------------------- stage 7: greedy selection
        // Per candidate (H: no unit over ~1 ms CoreCLR): a pick unit, reroute units (RerouteUnitPops each), one unit per build attempt
        // (BuildAttempts: area shrink or narrower corridor), rating units (Plan, Need-height row chunks, Classify + accept), chamfer
        // units for an accepted run. m_SelPhase: 0 pick, 1 reroute, 2 build, 3 rate, 4 / 5 chamfer.

        private Cand m_SelCand;
        private float2[] m_PendRing;
        private GradePath m_PendPath;
        private bool m_PendMerge, m_PendEnd;
        private int m_PendInto;
        private int m_SelPhase, m_SelStart, m_SelLen, m_SelRerouted = -1;
        private int m_ChI0, m_ChI1, m_ChJ0, m_ChJ1, m_ChRow;
        private bool m_ChPending;
        private bool m_SelMerge;
        private float m_SelGap, m_SelStopD;

        private void UnitSelect()
        {
            if (m_Cursor == 0)
            {
                // Targets, then the accepted-run distance and the reroute's cost scratch reset in row chunks (m_Cursor2 = row)
                if (m_Cursor2 == 0)
                    BeginSelect();
                int jEnd = Math.Min(m_Nz, m_Cursor2 + RowsPerUnit);
                for (int c = m_Cursor2 * m_Nx; c < jEnd * m_Nx; c++)
                {
                    m_DAcc[c] = float.PositiveInfinity;
                    m_AG[c] = float.PositiveInfinity;
                }
                m_Cursor2 = jEnd;
                if (m_Cursor2 >= m_Nz)
                    m_Cursor = 1;
                return;
            }
            if (m_SelPhase == 1)
            {
                if (RerouteStep())
                    AfterReroute();
                return;
            }
            if (m_SelPhase == 2)
            {
                EndAttempt();
                return;
            }
            if (m_SelPhase == 3)
            {
                RatePending();
                return;
            }
            if (m_SelPhase >= 4)
            {
                ChamferStep();
                return;
            }
            if (m_Accepted.Count >= m_RunCap || m_Result.Rated >= m_MaxRatings)
            {
                if (m_Result.Rated >= m_MaxRatings)
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
                double w = SelectWeight(c);
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
            BeginAttempt(best);
        }

        private void BeginSelect()
        {
            DropReroute();   // a Reselect may have cut a candidate short
            m_SelPhase = 0;
            m_RStep = 0;
            m_PendRing = null;
            m_PendPath = null;
            m_ChPending = false;
            m_Touched.Clear();
            m_RunCap = RunCapOf(m_Knobs, m_Result.RegionAreaM2);
            m_MaxRatings = Math.Max(MaxRatings, RatingsPerRun * m_RunCap);
            Targets(m_Knobs.Mix, m_RunCap, m_Target);
            Array.Clear(m_Have, 0, 5);
            m_Accepted.Clear();
            m_Result.Rated = 0;
            if (m_Mode == AutoPlanMode.Resort)
                FanCaps();   // per-lift caps and the developable cap (A5, Fans.cs)
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

        /// <summary>Greedy order: with a mix, the mix picks the band and the score the line. The band under target with the lowest
        /// fill share goes first (tiers of 1/1000 of the share, each worth 1000), then the line score Score0 / BandValue (the band's
        /// own value, a black is worth three greens, leaves the score; the rest is SizeFactor x at most 2, far under one tier).
        /// Bands at target come last, by line score. The old 1 + deficit weight let the summit blacks fill the cap once review F6
        /// gave summits their own seeds (alpine 2 bowl: 5 greens of 14). Terrain: the score alone.</summary>
        private double SelectWeight(Cand c)
        {
            if (m_Knobs.Mix == AutoMix.Terrain)
                return c.Score0;
            double line = c.Score0 / SkiResortScore.BandValueOf(SkiResortScore.BandState.Rated, (byte)c.Expected);
            int b = (int)c.Expected;
            if (m_Target[b] <= m_Have[b])
                return 0.1 * line;
            return 1000.0 * Math.Floor(1000.0 * (1.0 - m_Have[b] / (double)m_Target[b])) + line;
        }

        /// <summary>Phase 0 of a candidate: TopTaken, the free line's merge, and either the build (same unit) or a reroute (next units).</summary>
        private void BeginAttempt(Cand cand)
        {
            int k = cand.Pass;
            float gap = GapOf(m_Knobs.Density, TargetWidthM[k]);
            float stopD = gap + MinHalfWidthM;
            int seed = m_Paths[cand.PathStart];
            if (m_DAcc[seed] < stopD)
            {
                m_Result.Rejects[(int)AutoReject.TopTaken]++;
                return;
            }
            // Trail merge: stop where the centerline reaches an accepted run's gap; the end then tapers to MergeGapM
            int start = cand.PathStart, len = 1;
            bool merge = false;
            for (; len < cand.PathLen; len++)
                if (m_DAcc[m_Paths[start + len]] < stopD)
                {
                    merge = true;
                    break;
                }
            m_SelCand = cand;
            m_SelGap = gap;
            m_SelStopD = stopD;
            m_SelStart = start;
            m_SelLen = len;
            m_SelMerge = merge;
            m_SelRerouted = -1;
            // Reroute (P2): when the merge would cut the line short (the converging fall lines of natural terrain), find the pass's
            // cheapest line around the accepted runs instead: to the base with a tree island between, or beside a run to a lower merge
            if (merge && PathLength(start, len) < RerouteShare * cand.LengthM)
            {
                RerouteBegin(seed, k, stopD, m_Paths[start + len]);
                m_SelPhase = 1;
                return;
            }
            BeginBuild();
        }

        /// <summary>Phase 1 done: take the rerouted line when it is longer than the free line's merge stub.</summary>
        private void AfterReroute()
        {
            int at = RerouteEnd();
            if (at >= 0)
            {
                int n = m_Paths.Count - at;
                bool merges = m_DAcc[m_Paths[m_Paths.Count - 1]] < m_SelStopD;
                int keep = merges ? n - 1 : n;
                m_SelRerouted = at;
                if (PathLength(at, keep) > PathLength(m_SelStart, m_SelLen))
                {
                    m_SelStart = at;
                    m_SelLen = keep;
                    m_SelMerge = merges;
                }
            }
            BeginBuild();
        }

        /// <summary>Start of phase 2 (the line is final): the length checks, then one build attempt per unit (H).</summary>
        private void BeginBuild()
        {
            m_BAttempt = 0;
            m_BScale = 1f;
            m_BKeep = m_SelLen;
            m_SelPhase = 2;
            AutoReject fate = LineChecks(m_SelCand, m_SelStart, m_SelLen, m_SelMerge);
            if (fate != AutoReject.Kept)
                EndBuild(fate);
        }

        /// <summary>Phase 2: one build attempt (area shrink or narrower corridor between attempts); a kept ring waits for its rating
        /// units (phase 3); the rerouted cells are dropped when the build ends.</summary>
        private void EndAttempt()
        {
            AutoReject fate = AttemptStep(m_SelCand, m_SelStart, m_SelLen, m_SelMerge, m_SelGap, out bool done);
            if (done)
                EndBuild(fate);
        }

        private void EndBuild(AutoReject fate)
        {
            DropReroute();
            if (fate == AutoReject.Kept)
            {
                m_SelPhase = 3;
                m_RStep = 0;
                return;
            }
            m_Result.Rejects[(int)fate]++;
            m_SelPhase = 0;
        }

        /// <summary>Phase 3: rate the built ring in units (H): m_RStep 0 = node heights and the classifier's Plan, 1 = the Need
        /// heights in row chunks, 2 = Classify and the accept; back to phase 0 (or the chamfer).</summary>
        private void RatePending()
        {
            AutoReject fate;
            if (m_RStep == 0)
            {
                if (RatePlan(m_PendRing))
                {
                    m_RStep = 1;
                    return;
                }
                fate = AutoReject.Unrated;
            }
            else if (m_RStep == 1)
            {
                if (!RateFill())
                    return;
                m_RStep = 2;
                return;
            }
            else
            {
                fate = Rate(m_SelCand, m_PendRing, m_PendPath, m_PendMerge, m_PendInto, m_PendEnd, m_SelGap);
            }
            m_PendRing = null;
            m_PendPath = null;
            m_RStep = 0;
            m_Result.Rejects[(int)fate]++;
            m_SelPhase = m_ChPending ? 4 : 0;
            m_ChPending = false;
        }

        // The rating, split (H): exactly SkiRunRater.Rate's steps on the public classifier entries (StepMultiple of ShoelaceArea, the
        // DiagonalRule for that multiple, SkiSlopeClassifier.Plan, SkiRunRater.FillHeights' sampling without a reuse source,
        // SkiSlopeClassifier.Classify, SkiRunRater.ToRateStatus), so the band is bit for bit the catalog's (AutoGenBatch re-rates
        // every run with SkiRunRater.Rate and compares).
        private int m_RStep, m_RRow, m_RNodes;
        private SlopeStatus m_RPlanStatus;
        private SnapshotSampler m_RSampler;

        private bool RatePlan(float2[] ring)
        {
            int n = ring.Length;
            Grow(ref m_RateNodes, n);
            m_RSampler = new SnapshotSampler(m_In.Snapshot);
            for (int i = 0; i < n; i++)
            {
                var w = new float3(ring[i].x, 0f, ring[i].y);
                w.y = m_RSampler.Sample(w);
                m_RateNodes[i] = w;
            }
            m_RNodes = n;
            m_Result.Rated++;
            int m = Math.Max(1, SkiSlopeClassifier.StepMultiple(SkiRunRater.ShoelaceArea(m_RateNodes, n), m_Texel, 0));
            m_RateGrid.DiagonalRule = m >= 2;
            m_RPlanStatus = SkiSlopeClassifier.Plan(m_RateNodes, n, m_Origin, m * m_Texel, m_RateGrid);
            m_RRow = 0;
            if (!SkiRunRater.IsPlanOk(m_RPlanStatus))
                return false;
            SlopeGrid g = m_RateGrid;
            int cells = g.Nx * g.Nz;
            if (g.Heights == null || g.Heights.Length < cells)
                g.Heights = new float[Math.Max(cells, g.Heights == null ? 0 : g.Heights.Length * 2)];
            return true;
        }

        /// <summary>FillHeights' sampling (no reuse source) over a row chunk of the rating grid; true when every row is filled.</summary>
        private bool RateFill()
        {
            SlopeGrid g = m_RateGrid;
            int nx = g.Nx;
            byte[] need = g.Need;
            float[] heights = g.Heights;
            double step = g.Step, ox = g.Origin.x, oz = g.Origin.y;
            int jEnd = Math.Min(g.Nz, m_RRow + Math.Max(1, RateFillCells / Math.Max(1, nx)));
            for (int j = m_RRow; j < jEnd; j++)
            {
                float wz = (float)(oz + (g.J0 + j) * step);
                int row = j * nx;
                for (int i = 0; i < nx; i++)
                {
                    if (need[row + i] == 0)
                        continue;
                    float wx = (float)(ox + (g.I0 + i) * step);
                    heights[row + i] = m_RSampler.Sample(new float3(wx, 0f, wz));
                }
            }
            m_RRow = jEnd;
            return m_RRow >= g.Nz;
        }

        private const int RateFillCells = 65536;

        /// <summary>Phases 4 and 5: the accepted run's local chamfer, forward then backward, in row chunks of the box (one pass over
        /// consecutive rows is the same as the whole box in one call); back to phase 0.</summary>
        private void ChamferStep()
        {
            int rows = Math.Max(1, 65536 / (m_ChI1 - m_ChI0 + 1));
            if (m_SelPhase == 4)
            {
                int end = Math.Min(m_ChJ1, m_ChRow + rows - 1);
                Chamfer(m_DAcc, m_Nx, m_ChI0, m_ChI1, m_ChJ0, m_ChJ1, m_ChRow, end, true, m_Texel);
                m_ChRow = end + 1;
                if (m_ChRow > m_ChJ1)
                {
                    m_ChRow = m_ChJ1;
                    m_SelPhase = 5;
                }
                return;
            }
            int lo = Math.Max(m_ChJ0, m_ChRow - rows + 1);
            Chamfer(m_DAcc, m_Nx, m_ChI0, m_ChI1, m_ChJ0, m_ChJ1, lo, m_ChRow, false, m_Texel);
            m_ChRow = lo - 1;
            if (m_ChRow < m_ChJ0)
                m_SelPhase = 0;
        }

        private void DropReroute()
        {
            if (m_SelRerouted >= 0 && m_SelRerouted <= m_Paths.Count)
                m_Paths.RemoveRange(m_SelRerouted, m_Paths.Count - m_SelRerouted);
            m_SelRerouted = -1;
        }

        /// <summary>The line's own checks before any build: MinLengthM and MinDropM, and (F) a merged line must keep MergeKeepShare of
        /// its free line or MergeKeepM, else it is a stub (MergeStub, its own code since review F12 so a test can see it fire).</summary>
        private AutoReject LineChecks(Cand cand, int start, int len, bool merge)
        {
            int seed = m_Paths[start], last = m_Paths[start + len - 1];
            float pathLen = PathLength(start, len);
            if (pathLen < MinLengthM || m_H[seed] - m_H[last] < MinDropM)
                return AutoReject.TooShort;
            if (merge && pathLen < MergeKeepShare * cand.LengthM && pathLen < MergeKeepM)
                return AutoReject.MergeStub;
            return AutoReject.Kept;
        }

        private int m_BAttempt, m_BKeep;
        private float m_BScale;
        private const int BuildAttempts = 4;

        /// <summary>One build attempt of a candidate's line against the accepted runs (H: each area shrink or narrower corridor is its
        /// own Select unit). done = the build ended: Kept means a ring waits in m_Pend* for the rating, anything else is the reject.</summary>
        private AutoReject AttemptStep(Cand cand, int start, int len, bool merge, float gap, out bool done)
        {
            done = true;
            int keep = m_BKeep;
            AutoReject fate = BuildRing(cand, start, keep, merge && keep == len, gap, m_BScale, out float2[] ring, out GradePath path, out int mergeInto);
            bool retry = false;
            if (fate == AutoReject.Kept)
            {
                double area = PolygonArea(ring);
                if (area > MaxRunAreaM2 || SkiSlopeClassifier.StepMultiple(area, m_Texel, 0) > 1)
                {
                    m_BKeep = Math.Max(2, (int)(keep * Math.Min(0.95, MaxRunAreaM2 / area * 0.95)));
                    fate = AutoReject.Crosses;   // reported only when the attempts run out still over the cap
                    retry = true;
                    if (PathLength(start, m_BKeep) < MinLengthM)
                        return AutoReject.TooShort;   // F: a cut-back line is still at least MinLengthM
                }
                else
                {
                    m_PendRing = ring;
                    m_PendPath = path;
                    m_PendMerge = merge && keep == len && mergeInto >= 0;
                    m_PendInto = mergeInto;
                    m_PendEnd = keep == len;
                    return AutoReject.Kept;   // built; the rating units decide
                }
            }
            else if (fate == AutoReject.Obstacle || fate == AutoReject.Overlap || fate == AutoReject.Region || fate == AutoReject.Crosses)
            {
                m_BScale *= 0.75f;
                retry = true;
            }
            if (retry && ++m_BAttempt < BuildAttempts)
                done = false;
            return fate;
        }

        // Reroute: A* on pass k's graph from the seed down to the base. The heuristic is the pass's own route cost to the base (exact on
        // the free graph; StepCost is shared, so it is consistent). A cell within stopD of an accepted run ends the line (a merge there)
        // at an extra MergePenalty times the free route it leaves unskied. The search runs RerouteUnitPops pops per unit (its state lives
        // in these fields) up to RerouteMaxPops in all; the free line's own merge cost bounds it. Cost scratch: m_AG (reset to infinity by
        // Select's first units; every touched cell is reset again in RerouteEnd). m_DHard is no scratch any more: seeds and StepCost read it.
        private int m_ASeed, m_AK, m_APops, m_AGoal;
        private float m_AStopD, m_ALimit;

        private void RerouteBegin(int seed, int k, float stopD, int mergeCell)
        {
            float[] dist = m_Dist[k];
            m_ASeed = seed;
            m_AK = k;
            m_AStopD = stopD;
            m_ALimit = (dist[seed] + MergePenalty * dist[mergeCell]) * 1.0001f + 0.01f;   // the free line's own merge: an upper bound
            m_APops = 0;
            m_AGoal = -1;
            m_AHeap.Clear();
            m_Touched.Clear();
            m_AG[seed] = 0f;
            m_Touched.Add(seed);
            m_AHeap.Push(dist[seed], seed);
        }

        /// <summary>Up to RerouteUnitPops pops; true when the search ended (goal found, heap empty, bound or pop budget hit).</summary>
        private bool RerouteStep()
        {
            int k = m_AK;
            float[] dist = m_Dist[k], g = m_AG;
            byte[] pred = m_Pred[k];
            float floor = PassFloor[k], ceil = PassCeiling[k], hard = PassHardCeiling[k], t = m_Texel, inv2t = 1f / (2f * m_Texel), stopD = m_AStopD;
            for (int budget = RerouteUnitPops; budget > 0; budget--)
            {
                if (!m_AHeap.Pop(out float f, out int v) || f > m_ALimit || ++m_APops > RerouteMaxPops)
                    return true;
                float gv = g[v];
                if (f > gv + dist[v])
                    continue;   // stale
                if (pred[v] == PredSource || m_DAcc[v] < stopD)
                {
                    m_AGoal = v;
                    return true;
                }
                int vi = v % m_Nx, vj = v / m_Nx;
                if (vi < 1 || vj < 1 || vi >= m_Nx - 1 || vj >= m_Nz - 1)
                    continue;
                for (int d = 0; d < 8; d++)
                {
                    int u = (vj - DirZ[d]) * m_Nx + vi - DirX[d];
                    if (!(dist[u] < float.PositiveInfinity))
                        continue;
                    float w = StepCost(u, v, d, floor, ceil, hard, t, inv2t);
                    if (!(w >= 0f))
                        continue;
                    float gu = gv + w + (m_DAcc[u] < stopD ? MergePenalty * dist[u] : 0f);
                    if (gu < g[u])
                    {
                        if (float.IsPositiveInfinity(g[u]))
                            m_Touched.Add(u);
                        g[u] = gu;
                        m_APar[u] = (byte)d;
                        m_AHeap.Push(gu + dist[u], u);
                    }
                }
            }
            return false;
        }

        /// <summary>Appends the found line (seed first) to m_Paths and returns its start; -1 when none was found or it is a traverse
        /// (skis over DetourFactor times the free route between the same two heights). Resets the touched cost cells.</summary>
        private int RerouteEnd()
        {
            float[] dist = m_Dist[m_AK], g = m_AG;
            int goal = m_AGoal, seed = m_ASeed, at = -1;
            if (goal >= 0)
            {
                float travel = g[goal] - (m_DAcc[goal] < m_AStopD ? MergePenalty * dist[goal] : 0f);
                if (travel > DetourFactor * (dist[seed] - dist[goal]) + m_Texel)
                    goal = -1;
            }
            if (goal >= 0)
            {
                at = m_Paths.Count;
                for (int c = goal; ; c += DirZ[m_APar[c]] * m_Nx + DirX[m_APar[c]])
                {
                    m_Paths.Add(c);
                    if (c == seed)
                        break;
                }
                m_Paths.Reverse(at, m_Paths.Count - at);
            }
            for (int i = 0; i < m_Touched.Count; i++)
                g[m_Touched[i]] = float.PositiveInfinity;
            m_Touched.Clear();
            return at;
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

        private AutoReject BuildRing(Cand cand, int start, int count, bool merge, float gap, float scale, out float2[] ring, out GradePath path,
            out int mergeInto)
        {
            ring = null;
            path = null;
            mergeInto = -1;
            int k = cand.Pass;
            float ceil = PassHardCeiling[k];   // C: the corridor and the smoothed centerline stop at the hard ceiling
            // Centerline: decimated cells (DecimateM) through GradePath's Catmull-Rom chain; the raw cells when smoothing leaves the corridor
            for (int pass = 0; pass < 2 && path == null; pass++)
            {
                int every = pass == 0 ? Math.Max(1, (int)Math.Round(DecimateM / m_Texel)) : 1;
                m_PathPts.Clear();
                for (int i = 0; i < count; i += every)
                    m_PathPts.Add(CellCenter(m_Paths[start + i]));
                if ((count - 1) % every != 0)
                    m_PathPts.Add(CellCenter(m_Paths[start + count - 1]));
                GradePath p = GradePath.Build(m_PathPts, SampleM);
                if (p == null)
                    return AutoReject.TooShort;
                // The smoothed line cuts corners (DecimateM 25 m): it must keep what March needs at the center, or a 2 m neck breaks the
                // rating mask (Unrated)
                bool ok = true;
                for (int i = 0; i < p.Count && ok; i++)
                {
                    float toEnd = p.LengthM - p.S[i];
                    float g = merge && toEnd < MergeTaperM ? MergeGapM : gap;
                    ok = Cell(p.Xz[i], out int c) && (m_Flags[c] & (FOpen | FCliff)) == FOpen && m_O[c] < ceil && !(m_DAcc[c] < g) &&
                         !(m_DEdge[c] < 0.5f * EdgeSeedMarginM);
                }
                if (ok || pass == 1)
                    path = p;
            }
            // F: the smoothed centerline (the run's own line) is at least MinLengthM, not only the cell path behind it
            float lineM = 0f;
            for (int i = 1; i < path.Count; i++)
                lineM += math.distance(path.Xz[i - 1], path.Xz[i]);
            if (lineM < MinLengthM)
                return AutoReject.TooShort;
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
                // A merge is a junction: the line stops a gap plus a half width short of the run (stopD), so a narrow ring can end
                // farther than JunctionReachM from it. Then it is no merge and keeps the full gap (P2: a Sparse ring passed at 22.7 m).
                if (mergeInto >= 0 && RingDistance(ring, m_Accepted[mergeInto].Ring, JunctionReachM, out _, out _, out _, out _) > JunctionReachM)
                    mergeInto = -1;
            }
            return CheckRing(ring, gap, mergeInto);
        }

        /// <summary>Free half width along dir: steps of MarchStepM until a cell is not open, is cliff, is at or over the pass's hard
        /// ceiling, lies within gapM of an accepted run or within EdgeSeedMarginM / 2 of the region edge (G); minus WidthMarginM, at
        /// least 1 m.</summary>
        private float March(float2 p, float2 dir, float maxHw, float ceil, float gapM)
        {
            float free = 0f;
            for (float t = MarchStepM; t <= maxHw + WidthMarginM; t += MarchStepM)
            {
                if (!Cell(p + dir * t, out int c))
                    break;
                if ((m_Flags[c] & (FOpen | FCliff)) != FOpen || !(m_O[c] < ceil) || m_DAcc[c] < gapM || m_DEdge[c] < 0.5f * EdgeSeedMarginM)
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
            BBox(ring, out float2 lo, out float2 hi);
            for (int a = 0; a < m_Accepted.Count; a++)
            {
                Accepted acc = m_Accepted[a];
                float need = a == mergeInto ? MergeGapM : ExactGapShare * Math.Min(gap, acc.GapM);
                if (math.any(acc.Hi < lo - need) || math.any(acc.Lo > hi + need))
                    continue;
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
            // Classify the planned and filled grid (RatePlan, RateFill): the last step of SkiRunRater.Rate
            SlopeStatus cs = SkiSlopeClassifier.Classify(m_RateGrid, m_RateResult);
            RateStatus rs = SkiRunRater.ToRateStatus(m_RPlanStatus, cs);
            SlopeResult res = m_RateResult;
            if (rs != RateStatus.Rated || res.Status != SlopeStatus.Ok || !res.Connected || m_RSampler.Misses > 0)
                return AutoReject.Unrated;
            if (res.Band == SlopeBand.Flat)
                return AutoReject.Flat;
            var acc = new Accepted
            {
                Cand = cand, Ring = ring, Band = res.Band, RatingGrade = res.RatingGrade, MeanGrade = res.MeanGrade,
                SteepestGrade = res.SteepestGrade, DropM = res.Drop, AreaM2 = (float)PolygonArea(ring), LengthM = res.Length, TopY = res.MaxHeight,
                BottomY = res.MinHeight, GapM = gap, Merge = merge, MergeInto = merge ? mergeInto : -1,
            };
            BBox(ring, out acc.Lo, out acc.Hi);
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
            // The passes run in row chunks after this unit (m_SelPhase 4 and 5): a long diagonal run's box nears the lattice at 9 km2
            m_ChI0 = Math.Max(0, mi0 - r);
            m_ChI1 = Math.Min(m_Nx - 1, mi1 + r);
            m_ChJ0 = Math.Max(0, mj0 - r);
            m_ChJ1 = Math.Min(m_Nz - 1, mj1 + r);
            m_ChRow = m_ChJ0;
            m_ChPending = true;
            acc.Score = SkiResortScore.SizeFactor(acc.AreaM2) * SkiResortScore.BandValueOf(SkiResortScore.BandState.Rated, (byte)acc.Band) *
                        (1.0 + Math.Min(1.0, Math.Max(0.0, acc.DropM / SkiResortScore.VerticalRefM)));
            m_Accepted.Add(acc);
            m_Have[(int)acc.Band]++;
            return AutoReject.Kept;
        }

        // ---------------------------------------------------------------- stages 7b-9: junctions, kinds, clusters, warnings

        // Sliced (P2: junction pairs grow with the square of the run count): m_Cursor2 0 setup, 1 one run's pairs per unit, 2 runs and
        // junctions, 3 kinds, clusters, warnings, fingerprint.
        private readonly List<JunctionDraft> m_Pairs = new List<JunctionDraft>(), m_ExistingPairs = new List<JunctionDraft>();
        private int m_FinB = -1;
        private const int FinishPairWork = 40000;   // ring edge pairs per unit (TopmostPair is |a| x |b|)

        private void UnitFinish()
        {
            AutoPlanResult res = m_Result;
            int nRuns = m_Accepted.Count;
            if (m_Cursor2 == 0)
            {
                res.Runs.Clear();
                res.Junctions.Clear();
                res.Clusters.Clear();
                res.Warnings = m_FieldWarnings;
                res.Resort = null;
                Array.Clear(res.BandCount, 0, 5);
                Array.Copy(m_Target, res.BandTarget, 5);
                m_Pairs.Clear();
                m_ExistingPairs.Clear();
                m_FinB = -1;
                m_Cursor = 0;
                m_Cursor2 = 1;
                return;
            }
            var sampler = new SnapshotSampler(m_In.Snapshot);
            if (m_Cursor2 == 1)
            {
                if (m_Cursor < nRuns && FinishPairs(m_Cursor, ref sampler))
                {
                    m_Cursor++;
                    m_FinB = -1;
                }
                if (m_Cursor >= nRuns)
                    m_Cursor2 = 2;
                return;
            }
            if (m_Cursor2 == 3)
            {
                AssignKinds(res.Runs, m_Knobs.Kinds);
                BuildClusters(res);
                Warn(res);
                if (m_Mode == AutoPlanMode.Resort)
                    FinishResortUnit(res);
                res.Status = res.Runs.Count > 0 ? AutoPlanStatus.Ok : AutoPlanStatus.NoRuns;
                res.Fingerprint = Fingerprint(res);
                m_Stage = Stage.Done;
                return;
            }
            var rings = new List<float2>[nRuns];
            for (int a = 0; a < nRuns; a++)
                rings[a] = new List<float2>(m_Accepted[a].Ring);
            List<JunctionDraft> pairs = m_Pairs, existing = m_ExistingPairs;
            List<AutoObstacle> obs = m_In.Obstacles;
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
            m_Cursor2 = 3;
        }

        /// <summary>Junction drafts of run a: with every later run within reach, the topmost close point pair (service flows down from
        /// there) plus the merge point; with existing runs, our node at the closest point to their topmost node within reach. Ring
        /// bounding boxes farther apart than JunctionReachM are skipped (no pair can be within reach). Resumable: stops after
        /// FinishPairWork edge pairs (m_FinB = the next b) and returns false; true when run a is done.</summary>
        private bool FinishPairs(int a, ref SnapshotSampler sampler)
        {
            Accepted accA = m_Accepted[a];
            float2[] ra = accA.Ring;
            int work = 0;
            for (int b = m_FinB < 0 ? a + 1 : m_FinB; b < m_Accepted.Count; b++)
            {
                Accepted accB = m_Accepted[b];
                if (math.any(accB.Hi < accA.Lo - JunctionReachM) || math.any(accB.Lo > accA.Hi + JunctionReachM))
                    continue;
                if (work > 0 && work + ra.Length * accB.Ring.Length > FinishPairWork)
                {
                    m_FinB = b;
                    return false;
                }
                float2[] rb = accB.Ring;
                work += ra.Length * rb.Length;
                if (TopmostPair(ra, rb, ref sampler, out float2 pa, out int ea, out float2 pb, out int eb))
                    m_Pairs.Add(new JunctionDraft { A = a, B = b, PA = pa, PB = pb, EA = ea, EB = eb });
                int into = accB.MergeInto == a ? b : accA.MergeInto == b ? a : -1;
                if (into >= 0 && RingDistance(ra, rb, JunctionReachM, out float2 qa, out float2 qb, out int fa, out int fb) <= JunctionReachM)
                {
                    bool dup = m_Pairs.Count > 0 && m_Pairs[m_Pairs.Count - 1].A == a && m_Pairs[m_Pairs.Count - 1].B == b &&
                               math.distance(m_Pairs[m_Pairs.Count - 1].PA, qa) < 2f * GradeRibbon.MinSpacingM;
                    if (dup)
                        m_Pairs[m_Pairs.Count - 1] = WithMerge(m_Pairs[m_Pairs.Count - 1]);
                    else
                        m_Pairs.Add(new JunctionDraft { A = a, B = b, PA = qa, PB = qb, EA = fa, EB = fb, Merge = true });
                }
            }
            List<AutoObstacle> obs = m_In.Obstacles;
            if (obs == null)
                return true;
            for (int o = 0; o < obs.Count; o++)
            {
                AutoObstacle ob = obs[o];
                if (ob == null || ob.Kind != AutoObstacleKind.ExistingRun || ob.Ring == null || ob.Ring.Length < 3)
                    continue;
                BBox(ob.Ring, out float2 olo, out float2 ohi);
                if (math.any(ohi < accA.Lo - JunctionReachM) || math.any(olo > accA.Hi + JunctionReachM))
                    continue;
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
                    m_ExistingPairs.Add(new JunctionDraft { A = a, B = o, PA = bestP, EA = bestE, NodeB = bestV, Existing = true });
            }
            return true;
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

        /// <summary>Resort end of Finish: FinishResort (Flow.cs, A6) builds RunPod / RunLift, the graph, verdicts, mix, site verdict, pod
        /// clusters and the three warnings; the probe slots are copied in here so the plan always carries them.</summary>
        private void FinishResortUnit(AutoPlanResult res)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            FinishResort(res);
            AddResortUnit(SlotFlow, (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
            if (res.Resort == null)
                res.Resort = new ResortPlan();
            Array.Copy(m_RStageUnits, res.Resort.StageUnits, ResortStageCount);
            Array.Copy(m_RStageMs, res.Resort.StageMs, ResortStageCount);
            Array.Copy(m_RStageMaxMs, res.Resort.StageMaxMs, ResortStageCount);
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
            // Resort: the plan folds in once it has content (the A0 stubs leave 0, so the stubbed Resort fingerprint equals the v1 one)
            if (res.Resort != null && res.Resort.Fingerprint != 0UL)
            {
                h = Mix(h, (uint)res.Resort.Fingerprint);
                h = Mix(h, (uint)(res.Resort.Fingerprint >> 32));
            }
            return h;
        }

        private static ulong Mix(ulong h, uint v) => (h ^ v) * 1099511628211UL;
    }
}
