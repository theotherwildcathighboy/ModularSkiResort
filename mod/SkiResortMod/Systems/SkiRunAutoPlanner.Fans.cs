// Resort Planner (v2) stage A: run fans (spec 4.5). Resort mode only. Every hook falls back to v1 when no lift can take a fan.
// Sinks: the band passes (stage Route) start from each fan lift's bottom. The bottom is its BottomPad cells within SinkPadM of the
// station and a SinkDiscM disc around it. Pad cells within SinkTopKeepM of any lift top stay open, so a stacked lift's top seeds can
// ski off the shared bench. So every route, and every run, ends on a lift bottom instead of the base band.
// Seeds (stage Seeds): per lift the in-band cells in four tiers:
// Tier 0: within TopSeedM of its top.
// Tier 1: the upper third of its service corridor (the served rule: ServeRadiusM of the chord, at most TopMarginM over the top).
// Tier 2: the rest of the corridor.
// Tier 3: its own or an unserved pod above the lift's lower third whose route drains to this lift's bottom.
// The system sorts the seeds by tier, then height plus the v1 seeded jitter. It spaces them FanSeedSpacingM per pass, at most
// SeedsPerLiftPass. It pre-walks each seed along the pass's route (the MakeCandidate walk), so a seed that cannot make a run
// (too short, flat) is never proposed. The seeds are a reserve. Nothing about them depends on the density.
// Select: each pick unit materializes one candidate (MakeCandidate, v1) from the lift with the lowest fill. It uses the lift's top
// seeds first, until the lift has FanTopRunsMin runs from its top. Then it uses the v1 band-deficit order on the lift's own targets
// (v1 Targets on the lift's cap).
// Before a seed is proposed, the system walks it again against the accepted runs (m_DAcc). Inside an accepted gap it drops the seed
// (it would be TopTaken). But a tier 0-1 seed in the gap next to a run becomes a branch of that run instead. It also drops a line whose
// merge would leave a TooShort / MergeStub line (FanDropped counts both).
// A top run adds branch seeds BranchTopFromM-BranchFromM below its top at once. After a lift's second run, every run of that lift
// adds them BranchFromM-BranchToM below its top.
// A branch abuts its parent at the top. The gap tapers from MergeGapM over BranchTaperM, and Finish makes the pair a merge junction.
// A branch keeps the near gap and must leave it within BranchLeaveM.
// Gaps: centerline spacing (Sparse 140 / Normal 100 / Dense 75 m) minus the class width, at least the v1 Dense gap.
// Fall lines to one sink converge at a shallow angle. So a line that enters a run's gap merges where it reaches the near gap
// (the v1 gap, at least FanNearGapM), if that is within MergeApproachM (FanApproach).
// Widths: v1 TargetWidthM. Open bowls above a known tree line: up to BowlWidthM. The last MergeZoneM before a merge or a sink:
// MergeZoneWiden x (March still caps them).
// The system rejects a built ring that would take its pod past DevelopableShare x SkiableAreaM2 as PodFull.
// Reroute, build attempts, rating, junctions and kinds are v1.
// Pure and Entities-free (usings System, System.Collections.Generic, Unity.Mathematics only). Spec: docs/research/quickruns-v2/spec/stage-a-spec.md.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        // ---------------------------------------------------------------- constants (spec 4.5; [T] 9)
        /// <summary>Runs per lift by density (Sparse, Normal, Dense): the middle of [T] 9's 4-6 / 6-9 / 9-12.</summary>
        public static readonly int[] FanRunsPerLift = { 5, 7, 10 };
        /// <summary>Centerline spacing of neighbor runs by density ([T] 9: 100 m, 72-137).</summary>
        public static readonly float[] FanSpacingM = { 140f, 100f, 75f };
        /// <summary>Runs that start within LiftNearM of the lift top: at least / at most ([T] 9: 3, 2-4).</summary>
        public const int FanTopRunsMin = 2, FanTopRunsMax = 4;
        public const float TopSeedM = 100f, FanSeedSpacingM = 20f, PodSeedShare = 1f / 3f;
        public const int SeedsPerLiftPass = 80, FanOrdinalCap = 100, FanKeysPerUnit = 1500, FanWalksPerUnit = 40;   // P-1: a seed-list unit's work
        /// <summary>SinkDiscM = LinkMeters: a route that ends in the disc off the pad links to the lift bottom (Flow.cs, AR-1, was 40 m).</summary>
        public const float SinkPadM = 90f, SinkDiscM = LinkMeters, SinkTopKeepM = 60f;
        public const float BranchFromM = 100f, BranchToM = 400f, BranchStepM = 50f, BranchTaperM = 60f, BranchLeaveM = 150f, BranchSearchM = 60f;
        public const float MergeZoneM = 60f, MergeZoneWiden = 1.5f, BowlWidthM = 60f;
        public const float MergeApproachM = 200f, FanNearGapM = 30f, FanLenSlackM = 30f, BranchTopFromM = 20f;
        public const int BranchRunsPerUnit = 2;
        /// <summary>Pick preferences inside one band tier: a branch, a seed whose route ends on the lift's own bottom.</summary>
        public const double BranchBonus = 1.5, OtherSinkFactor = 0.8;

        // ---------------------------------------------------------------- state
        private struct FanLift
        {
            public int Id;                 // index in m_LiftList (= ResortPlan.Lifts = the slot's own index = AutoRun.Cluster)
            public float2 Top, Bottom;
            public float TopY, BottomY, Radius, TopMargin;
            public int TopPad, BottomPad, Pod, ChordFirst, ChordCount;
            public bool Terminals;         // TerminalsOnly serve rule (tram): the corridor is the disc around a terminal
            public bool Ok;                // finite ends: it gets seeds and a cap (else an empty slot, so the index is the lift id)
            public bool Sink;              // its bottom is on the lattice, open and out of the edge strip
            public float2 Lo, Hi;          // seed bbox (corridor + top disc)
        }

        private struct FanSeed
        {
            public int Cell, Lift, Pass, Ordinal, Tier, Parent;   // Parent: the accepted run a branch starts on, -1 = none
            public bool Used, OwnSink;
            public SlopeBand Expected;
            public double Score;
        }

        private readonly List<FanLift> m_FanLifts = new List<FanLift>();
        private readonly List<FanSeed> m_FanRes = new List<FanSeed>();
        private readonly HashSet<long> m_FanSeen = new HashSet<long>(), m_FanSeenBranch = new HashSet<long>();
        private readonly List<int> m_FanAccPod = new List<int>();
        private readonly List<int> m_FanBranched = new List<int>();   // per accepted run: 0 none, 1 top branches seeded, 2 all
        private List<long>[] m_FanKeys = new List<long>[0];
        private int[] m_FanOrd = new int[0], m_FanOrdBase = new int[0];
        private int[] m_FanCap = new int[0], m_FanCount = new int[0], m_FanTopHave = new int[0], m_FanTarget = new int[0], m_FanHave = new int[0];
        private float[] m_FanPodArea = new float[0];
        private readonly int[] m_FanT = new int[5];   // FanCaps: one lift's band targets
        private int[] m_FanPodCount = new int[0];      // FanPodOf: samples per pod
        private bool[] m_FanDone = new bool[0];        // FanBest: lifts with no usable seed left in this pick
        private bool m_FanActive;
        private readonly int[] m_FanDropWhy = new int[4];
        private int m_FanResBase, m_FanDropped, m_FanBranches, m_FanSinks;
        private int m_FanListK = -1, m_FanListN, m_FanListStart, m_FanListTaken;   // the (lift, pass) seed list in progress (-1 = none)

        /// <summary>Diagnostics (host): reserve seeds (with the branch seeds of this select), seeds dropped at the pick (inside an accepted
        /// gap, or a line the merge rules would reject), branch candidates proposed.</summary>
        public int FanReserve => m_FanRes.Count;
        public int FanDropped => m_FanDropped;
        /// <summary>Pick drops by reason:
        /// 0 inside an accepted gap (tier 2-3, inside a ring or too far out to link).
        /// 1 a tier 0-1 seed in a gap that cannot branch off.
        /// 2 a free line whose merge would be a stub.
        /// 3 a branch that no longer leaves its parent.</summary>
        public int FanDropWhy(int why) => m_FanDropWhy[why];
        public int FanBranchCandidates => m_FanBranches;

        /// <summary>One slot per lift of the skeleton (index = lift id). Ok = finite ends. Sink = its bottom is an open lattice cell out of
        /// the edge strip. True when at least one lift is a sink (else the v1 base band and seeding run).</summary>
        private bool BuildFanLifts()
        {
            m_FanLifts.Clear();
            m_FanSinks = 0;
            if (m_Mode != AutoPlanMode.Resort)
                return false;
            for (int i = 0; i < m_LiftList.Count; i++)
            {
                PlanLift l = m_LiftList[i];
                if (!Finite3(l.Top) || !Finite3(l.Bottom))
                {
                    m_FanLifts.Add(new FanLift { Id = i, Top = Finite3(l.Top) ? l.Top.xz : Finite3(l.Bottom) ? l.Bottom.xz : float2.zero, TopPad = -1, BottomPad = -1, Pod = l.Pod });
                    continue;
                }
                LiftTypeDef def = l.TypeIndex < SkiCatalog.Lifts.Count ? SkiCatalog.Lifts[l.TypeIndex] : SkiCatalog.Lifts[0];
                var f = new FanLift
                {
                    Id = i, Top = l.Top.xz, Bottom = l.Bottom.xz, TopY = l.Top.y, BottomY = l.Bottom.y, Radius = def.ServeRadiusM,
                    TopMargin = def.TopMarginM, TopPad = l.TopPad, BottomPad = l.BottomPad, Pod = l.Pod,
                    ChordFirst = i < m_LiftChordFirst.Count ? m_LiftChordFirst[i] : 0, ChordCount = i < m_LiftChordCount.Count ? m_LiftChordCount[i] : 0,
                    Terminals = def.ServeRule == ServeRuleKind.TerminalsOnly, Ok = true,
                };
                float reach = Math.Max(f.Radius, LiftNearM);
                f.Lo = math.min(f.Top, f.Bottom) - reach;
                f.Hi = math.max(f.Top, f.Bottom) + reach;
                f.Sink = Cell(f.Bottom, out int bc) && (m_Flags[bc] & (FOpen | FCliff)) == FOpen && !(m_DEdge[bc] < 0.5f * EdgeSeedMarginM);
                if (f.Sink)
                    m_FanSinks++;
                m_FanLifts.Add(f);
            }
            return m_FanSinks > 0;
        }

        /// <summary>Route-pass source rule in Resort mode (StartPassRows, one call per cell and pass, m_Pass is the pass). The sources are
        /// the fan lifts' bottoms: BottomPad cells within SinkPadM of the station but not within SinkTopKeepM of any lift top, and a
        /// SinkDiscM disc. With no fan lift: the v1 rule (base band below the pass's SourceTop). The lift table is rebuilt at the first cell
        /// of every pass.</summary>
        private bool IsResortSource(int c)
        {
            if (c == 0)
                BuildFanLifts();
            if (m_FanSinks == 0)
                return (m_Flags[c] & FBase) != 0 && m_H[c] <= SourceTop(m_Pass);
            float2 p = CellCenter(c);
            for (int k = 0; k < m_FanLifts.Count; k++)
            {
                FanLift f = m_FanLifts[k];
                if (!f.Sink || math.abs(p.x - f.Bottom.x) > SinkPadM || math.abs(p.y - f.Bottom.y) > SinkPadM)
                    continue;
                float d = math.distance(p, f.Bottom);
                if (d <= SinkDiscM)
                    return true;
                if (d <= SinkPadM && f.BottomPad >= 0 && m_PadOf[c] == f.BottomPad && !NearLiftTop(p))
                    return true;
            }
            return false;
        }

        private bool NearLiftTop(float2 p)
        {
            for (int k = 0; k < m_FanLifts.Count; k++)
                if (m_FanLifts[k].Ok && math.distance(p, m_FanLifts[k].Top) <= SinkTopKeepM)
                    return true;
            return false;
        }

        /// <summary>Gap between neighbor runs: the density's centerline spacing minus the class width, at least the v1 Dense gap.</summary>
        private float FanGap(int pass) => Math.Max(DenseGapM, FanSpacingM[(int)m_Knobs.Density] - TargetWidthM[pass]);

        /// <summary>The gap a fan line keeps on its last MergeApproachM before a merge: the v1 gap, at least FanNearGapM 30 m (DECISIONS 455).
        /// Neighbor centerlines stay at least the gap plus a pinch half width apart outside merge and split zones (F-5).</summary>
        private float FanLineGap(int pass, bool branch) => branch ? FanNearGap(pass) : FanGap(pass);
        private float FanNearGap(int pass) => Math.Max(GapOf(m_Knobs.Density, TargetWidthM[pass]), FanNearGapM);

        /// <summary>Merge approach (BeginAttempt, a fan line that enters an accepted run's fan gap at path index len).
        /// When the line reaches the merge stop within MergeApproachM, it merges there instead. The merge stop is FanMergeStopM: the v1 gap,
        /// capped so the junction forms.
        /// Fall lines to one sink converge at a shallow angle, so the fan gap alone would cut most lines to stubs.
        /// Approach = the path length from the fan-gap entry to the merge (BuildRing keeps the v1 gap on it). Else the merge stays at len.</summary>
        private int FanApproach(int start, int len, int pathLen, int k, out float approach)
        {
            float near = FanMergeStopM(k), run = 0f;
            approach = 0f;
            for (int q = len + 1; q < pathLen; q++)
            {
                run += PathLength(start + q - 1, 2);
                if (run > MergeApproachM)
                    break;
                if (m_DAcc[m_Paths[start + q]] < near)
                {
                    approach = run + m_Texel;
                    return q;
                }
            }
            return len;
        }

        /// <summary>The accepted-run chamfer reach in Resort mode: the widest fan gap plus the widest half width (merge zone, bowl).</summary>
        private float FanChamferM()
        {
            float g = 0f;
            for (int p = 0; p < Passes; p++)
                g = Math.Max(g, Math.Max(DenseGapM, FanSpacingM[0] - TargetWidthM[p]));
            return g + 0.5f * Math.Max(TargetWidthM[0] * MergeZoneWiden, BowlWidthM) + MinHalfWidthM + WidthMarginM;
        }

        /// <summary>True when the fans own the run seeding and selection (Resort mode with a fan lift).</summary>
        private bool FansOn => m_Mode == AutoPlanMode.Resort && m_FanActive;

        // ---------------------------------------------------------------- stage Seeds (m_Cursor2: 0 setup, 1 seed-cell rows, 2 one (lift, pass) list per unit)

        /// <summary>Seeds hook (UnitSeeds, Resort only, once per unit): true = the fan seeding handled this unit. False = run the v1 seeding
        /// (no fan lift). Case 0 repeats the v1 bookkeeping (m_FieldWarnings, ClearCandidates). One cluster per lift (Center = its top).</summary>
        private bool FanSeeds()
        {
            if (m_Cursor2 == 0)
            {
                m_FanActive = BuildFanLifts();
                if (!m_FanActive)
                    return false;
                m_FieldWarnings = m_Result.Warnings;
                ClearCandidates();
                m_FanRes.Clear();
                m_FanSeen.Clear();
                m_FanSeenBranch.Clear();
                int nl = m_FanLifts.Count, lists = nl * Passes;
                for (int k = 0; k < nl; k++)
                {
                    FanLift f = m_FanLifts[k];
                    m_Clusters.Add(new ClusterInfo { Cell = Cell(f.Top, out int tc) ? tc : 0, Center = f.Top, TopY = f.TopY });
                }
                if (m_FanKeys.Length < lists)
                {
                    var grown = new List<long>[lists];
                    Array.Copy(m_FanKeys, grown, m_FanKeys.Length);
                    m_FanKeys = grown;
                }
                for (int k = 0; k < lists; k++)
                {
                    if (m_FanKeys[k] == null)
                        m_FanKeys[k] = new List<long>();
                    m_FanKeys[k].Clear();
                }
                Grow(ref m_FanOrd, lists);
                Grow(ref m_FanOrdBase, lists);
                Array.Clear(m_FanOrd, 0, m_FanOrd.Length);
                Array.Clear(m_FanOrdBase, 0, m_FanOrdBase.Length);
                m_FanListK = -1;
                m_Cursor = m_RegionJ0;
                m_Cursor2 = 1;
                return true;
            }
            if (!m_FanActive)
                return false;
            if (m_Cursor2 == 1)
            {
                FanSeedRows();
                return true;
            }
            if (m_Cursor < m_FanLifts.Count * Passes && !FanSeedList(m_Cursor / Passes, m_Cursor % Passes))
                return true;   // the list continues in the next unit
            m_Cursor++;
            if (m_Cursor >= m_FanLifts.Count * Passes)
            {
                m_FanResBase = m_FanRes.Count;
                Array.Copy(m_FanOrd, m_FanOrdBase, m_FanOrd.Length);
                Next(Stage.Candidates);
            }
            return true;
        }

        /// <summary>Seed-cell rows on the v1 2-cell seed lattice. A cell joins when it is in band, usable, EdgeSeedMarginM inside and not a
        /// sink. It joins the list of every fan lift whose seed tiers hold it, per pass it is in band for. Key: tier, then height plus the
        /// seeded jitter (v1 SeedRows), the cell in the low 21 bits.</summary>
        private void FanSeedRows()
        {
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            int pods = m_PodList.Count;
            for (int j = m_Cursor; j < jEnd; j++)
            {
                if (((m_LJ0 + j) & 1) != 0)
                    continue;
                for (int i = m_RegionI0 + ((m_LI0 + m_RegionI0) & 1); i <= m_RegionI1; i += 2)
                {
                    int c = j * m_Nx + i;
                    byte bits = m_InBand[c];
                    if (bits == 0 || (m_Flags[c] & (FOpen | FCliff)) != FOpen || !(m_DHard[c] >= EdgeSeedMarginM))
                        continue;
                    float2 pc = CellCenter(c);
                    float h = m_H[c];
                    int podOf = m_PodOf[c] < pods ? m_PodOf[c] : -1;
                    for (int k = 0; k < m_FanLifts.Count; k++)
                    {
                        FanLift f = m_FanLifts[k];
                        if (!f.Ok || h > f.TopY + f.TopMargin)
                            continue;
                        bool inBox = math.all(pc >= f.Lo) && math.all(pc <= f.Hi);
                        bool podOk = podOf >= 0 && (podOf == f.Pod || m_PodLift[podOf] < 0);   // its own pod or an unserved one
                        if (!inBox && !podOk)
                            continue;
                        float rise = f.TopY - f.BottomY, dTop = math.distance(pc, f.Top);
                        int tier;
                        if (dTop <= TopSeedM)
                            tier = 0;
                        else if (inBox && InCorridor(f, pc, dTop))
                            tier = h >= f.BottomY + (2f / 3f) * rise ? 1 : 2;
                        else if (podOk && h >= f.BottomY + PodSeedShare * rise)
                            tier = 3;
                        else
                            continue;
                        for (int p = 0; p < Passes; p++)
                        {
                            if ((bits & (1 << p)) == 0 || m_Pred[p][c] == PredSource)
                                continue;
                            float key = h + SeedJitterM * Jitter(m_Knobs.Seed, c, p);
                            long q = (long)Math.Round((1.0e6 - key) * 1000.0) | ((long)tier << 40);
                            m_FanKeys[k * Passes + p].Add((q << 21) | (long)c);
                        }
                    }
                }
            }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            m_Cursor = 0;
            m_Cursor2 = 2;
        }

        /// <summary>The served rule's corridor (LiftServedRule.cs:3-6): within Radius of a chord (AlongLine), or of a terminal (TerminalsOnly).</summary>
        private bool InCorridor(FanLift f, float2 p, float dTop)
        {
            if (f.Terminals)
                return dTop <= f.Radius || math.distance(p, f.Bottom) <= f.Radius;
            if (f.ChordCount <= 0)
                return GradePath.SegDistance(p, f.Bottom, f.Top, out float _) <= f.Radius;
            for (int q = 0; q < f.ChordCount; q++)
            {
                int a = f.ChordFirst + 2 * q;
                if (a + 1 < m_LiftChords.Count && GradePath.SegDistance(p, m_LiftChords[a], m_LiftChords[a + 1], out float _) <= f.Radius)
                    return true;
            }
            return false;
        }

        /// <summary>One (lift, pass) list: sorted (tier, then height plus jitter), spaced FanSeedSpacingM, each seed pre-walked, at most
        /// SeedsPerLiftPass into the reserve. A unit looks at FanKeysPerUnit keys and walks FanWalksPerUnit of them (fixed work, P-1).
        /// Returns true when the list is done.</summary>
        private bool FanSeedList(int lift, int p)
        {
            if (m_FanListK < 0)
            {
                List<long> list = m_FanKeys[lift * Passes + p];
                m_FanListN = list.Count;
                Grow(ref m_SortKeys, Math.Max(1, m_FanListN));
                list.CopyTo(m_SortKeys);
                Array.Sort(m_SortKeys, 0, m_FanListN);
                m_FanListK = 0;
                m_FanListStart = m_FanRes.Count;
                m_FanListTaken = 0;
                return false;   // the sort is this unit's work
            }
            int keys = 0, walks = 0;
            for (; m_FanListK < m_FanListN && m_FanListTaken < SeedsPerLiftPass && keys < FanKeysPerUnit && walks < FanWalksPerUnit; m_FanListK++, keys++)
            {
                int c = (int)(m_SortKeys[m_FanListK] & ((1L << 21) - 1));
                int tier = (int)((m_SortKeys[m_FanListK] >> 61) & 3);
                float2 pc = CellCenter(c);
                bool near = false;
                for (int s = m_FanListStart; s < m_FanRes.Count && !near; s++)
                    near = math.distance(CellCenter(m_FanRes[s].Cell), pc) < FanSeedSpacingM;
                if (near)
                    continue;
                walks++;
                if (FanAdd(c, lift, p, tier, -1, false))
                    m_FanListTaken++;
            }
            if (m_FanListK < m_FanListN && m_FanListTaken < SeedsPerLiftPass)
                return false;
            m_FanListK = -1;
            return true;
        }

        /// <summary>Walks (cell, pass) along the route (MakeCandidate's walk). False when the line cannot be a run: flat, under MinLengthM
        /// (plus FanLenSlackM: the smoothed centerline is shorter than the cell walk) or MinDropM.
        /// With checkGap (Select only: m_DAcc holds the accepted runs' distance), the function also checks the line BeginAttempt would cut.
        /// A branch must leave its near gap within BranchLeaveM.
        /// The merge (after the merge approach, FanApproach) must keep MinLengthM and the MergeKeepShare / MergeKeepM rule (else the line
        /// would be a TooShort or MergeStub reject).</summary>
        private bool FanWalk(int c, int p, bool branch, bool checkGap, out float len, out float drop, out SlopeBand expected, out int last)
        {
            byte[] pred = m_Pred[p];
            float mergeLen = -1f, maxO = 0f, fanIn = -1f;
            float stopD = FanLineGap(p, branch) + MinHalfWidthM, near = FanMergeStopM(p), minLen = MinLengthM + FanLenSlackM;
            bool left = !branch;
            int cur = c, guard = m_N;
            len = 0f;
            last = c;
            while (guard-- > 0)
            {
                maxO = Math.Max(maxO, m_O[cur]);
                last = cur;
                if (checkGap && mergeLen < 0f && !left)
                {
                    if (m_DAcc[cur] >= stopD)
                        left = true;
                    else if (len > BranchLeaveM)
                        break;
                }
                else if (checkGap && mergeLen < 0f)
                {
                    if (fanIn < 0f && m_DAcc[cur] < stopD)
                        fanIn = len;
                    if (fanIn >= 0f && (m_DAcc[cur] < near || len - fanIn > MergeApproachM))
                        mergeLen = m_DAcc[cur] < near ? len : fanIn;   // the merge approach (FanApproach)
                }
                byte d = pred[cur];
                if (d == PredSource || d == PredNone)
                    break;
                int i = cur % m_Nx - DirX[d], j = cur / m_Nx - DirZ[d];
                len += d < 4 ? m_Texel : m_Texel * Sqrt2;
                cur = j * m_Nx + i;
                if (len > MaxLengthM)
                    break;
            }
            drop = m_H[c] - m_H[last];
            expected = SkiSlopeClassifier.BandOf(maxO);
            if (!left || expected == SlopeBand.Flat || len < minLen || drop < MinDropM)
                return false;
            if (mergeLen >= 0f && (mergeLen < minLen || (mergeLen < MergeKeepShare * len && mergeLen < MergeKeepM)))
                return false;
            return true;
        }

        /// <summary>Adds a reserve seed when (cell, pass) walks to a run candidate (FanWalk, a branch with the gap rules). Score =
        /// MakeCandidate's Score0 (the same size, band, drop and seeded jitter terms).</summary>
        private bool FanAdd(int c, int lift, int p, int tier, int parent, bool branch)
        {
            long dup = (long)c * Passes + p;
            if (m_FanSeen.Contains(dup) || m_FanSeenBranch.Contains(dup))
                return false;
            int ordSlot = lift * Passes + p;
            if (m_FanOrd[ordSlot] >= FanOrdinalCap)
                return false;
            if (!FanWalk(c, p, branch, branch, out float len, out float drop, out SlopeBand expected, out int last))
                return false;
            bool own = m_Pred[p][last] == PredSource && math.distance(CellCenter(last), m_FanLifts[lift].Bottom) <= SinkPadM;
            if (tier == 3 && !own)
                return false;   // a pod seed belongs to the lift whose bottom its line drains to
            float area = Math.Min(len * TargetWidthM[p], MaxRunAreaM2);
            double score = SkiResortScore.SizeFactor(area) * SkiResortScore.BandValueOf(SkiResortScore.BandState.Rated, (byte)expected) *
                           (1.0 + Math.Min(1.0, Math.Max(0.0, drop / SkiResortScore.VerticalRefM)));
            score *= 1.0 + ScoreJitter * (Jitter(m_Knobs.Seed, c, 16 + p) - 0.5);
            m_FanRes.Add(new FanSeed
            {
                Cell = c, Lift = lift, Pass = p, Ordinal = m_FanOrd[ordSlot]++, Parent = parent, Tier = tier, OwnSink = own, Expected = expected,
                Score = score,
            });
            (branch ? m_FanSeenBranch : m_FanSeen).Add(dup);
            return true;
        }

        // ---------------------------------------------------------------- stage Select

        /// <summary>Select hook (end of BeginSelect, Resort only). It sets per-lift caps (FanRunsPerLift by density, the run cap = their sum,
        /// at most the v1 cap) and per-lift band targets (v1 Targets on the lift's cap). It resets the reserve and the materialized
        /// candidates, so a Reselect at Select equals a fresh run.</summary>
        private void FanCaps()
        {
            if (!FansOn)
                return;
            m_FanRes.RemoveRange(m_FanResBase, m_FanRes.Count - m_FanResBase);
            for (int s = 0; s < m_FanRes.Count; s++)
            {
                FanSeed fs = m_FanRes[s];
                fs.Used = false;
                fs.Parent = -1;
                m_FanRes[s] = fs;
            }
            m_FanSeenBranch.Clear();
            Array.Copy(m_FanOrdBase, m_FanOrd, m_FanOrd.Length);
            m_Cands.Clear();
            m_Paths.Clear();
            m_PathHashes.Clear();
            m_Result.Candidates = 0;
            m_FanDropped = 0;
            Array.Clear(m_FanDropWhy, 0, 4);
            m_FanBranches = 0;
            m_FanAccPod.Clear();
            m_FanBranched.Clear();
            int nl = m_FanLifts.Count;
            Grow(ref m_FanCap, nl);
            Grow(ref m_FanCount, nl);
            Grow(ref m_FanTopHave, nl);
            Grow(ref m_FanTarget, nl * 5);
            Grow(ref m_FanHave, nl * 5);
            Grow(ref m_FanPodArea, Math.Max(1, m_PodList.Count));
            Array.Clear(m_FanCap, 0, nl);
            Array.Clear(m_FanCount, 0, nl);
            Array.Clear(m_FanTopHave, 0, nl);
            Array.Clear(m_FanTarget, 0, nl * 5);
            Array.Clear(m_FanHave, 0, nl * 5);
            Array.Clear(m_FanPodArea, 0, m_FanPodArea.Length);
            int per = FanRunsPerLift[(int)m_Knobs.Density], sum = 0;
            int[] t = m_FanT;
            Array.Clear(m_Target, 0, 5);
            for (int k = 0; k < nl; k++)
            {
                if (!m_FanLifts[k].Ok)
                    continue;
                m_FanCap[k] = per;
                sum += per;
                Targets(m_Knobs.Mix, per, t);
                for (int b = 0; b < 5; b++)
                {
                    m_FanTarget[k * 5 + b] = t[b];
                    m_Target[b] += t[b];
                }
            }
            m_RunCap = Math.Min(m_RunCap, Math.Min(sum, HardRunCap));
            m_MaxRatings = Math.Max(MaxRatings, RatingsPerRun * m_RunCap);
        }

        /// <summary>The pick unit in Resort mode (UnitSelect phase 0). It books the newest accepted run and adds the branch seeds of runs
        /// whose lift has two runs (BranchRunsPerUnit runs per unit). Then it materializes the best usable seed as a v1 candidate
        /// (MakeCandidate) and starts its attempt (BeginAttempt). It finishes when the cap, the rating cap or the reserve runs out.</summary>
        private void FanPick()
        {
            FanSync();
            if (m_Accepted.Count >= m_RunCap || m_Result.Rated >= m_MaxRatings)
            {
                Next(Stage.Finish);
                return;
            }
            int branched = 0;
            for (int a = 0; a < m_Accepted.Count; a++)
            {
                int lift = m_Accepted[a].Cand.Cluster, stage = m_FanBranched[a];
                bool top = math.distance(m_Accepted[a].Centerline[0], m_FanLifts[lift].Top) <= LiftNearM;
                int want = m_FanCount[lift] >= 2 ? 2 : top ? 1 : 0;   // top runs branch at once (runs leaving the top together)
                if (stage >= want)
                    continue;
                if (branched >= BranchRunsPerUnit)
                    return;   // more in the next unit (units are fixed work, so slicing never changes the plan)
                if (stage == 0 && top)
                    FanBranchSeeds(a, BranchTopFromM, BranchFromM - 1f);
                if (want == 2)
                    FanBranchSeeds(a, BranchFromM, BranchToM);
                m_FanBranched[a] = want;
                branched++;
            }
            int best;
            while (true)
            {
                best = FanBest();
                if (best < 0)
                {
                    Next(Stage.Finish);
                    return;
                }
                FanSeed s = m_FanRes[best];
                s.Used = true;
                bool usable = FanUsable(ref s);
                m_FanRes[best] = s;
                if (usable)
                    break;
                m_FanDropped++;
            }
            FanSeed pick = m_FanRes[best];
            int before = m_Cands.Count;
            MakeCandidate(new SeedRec { Cell = pick.Cell, Cluster = pick.Lift, Pass = pick.Pass, Ordinal = pick.Ordinal });
            if (m_Cands.Count == before)
                return;   // the walk is FanWalk's, so this does not happen. MakeCandidate counted the reject
            Cand cand = m_Cands[m_Cands.Count - 1];
            cand.BranchOf = pick.Parent;
            cand.Tried = true;
            if (pick.Parent >= 0)
                m_FanBranches++;
            m_Result.Candidates = m_Cands.Count;
            BeginAttempt(cand);
        }

        /// <summary>Books every accepted run not yet seen: its pod (FanPodOf over its centerline), the pod's run area, the lift's counts.</summary>
        private void FanSync()
        {
            for (int a = m_FanAccPod.Count; a < m_Accepted.Count; a++)
            {
                Accepted acc = m_Accepted[a];
                int pod = FanRunPod(acc.Centerline, acc.Centerline.Length, acc.Cand.Cluster);
                m_FanAccPod.Add(pod);
                m_FanBranched.Add(0);
                if (pod >= 0)
                    m_FanPodArea[pod] += acc.AreaM2;
                int lift = acc.Cand.Cluster;
                m_FanCount[lift]++;
                m_FanHave[lift * 5 + (int)acc.Band]++;
                if (math.distance(acc.Centerline[0], m_FanLifts[lift].Top) <= LiftNearM)
                    m_FanTopHave[lift]++;
            }
        }

        /// <summary>The next reserve seed. Among lifts under their cap, the system takes the lowest fill (count / cap, ties by lift). In that
        /// lift, it takes tier-0 seeds while the lift has fewer than FanTopRunsMin top runs (none once it has FanTopRunsMax). Otherwise it
        /// uses the v1 band-deficit order on the lift's own targets (Terrain: the score). Inside one tier, a branch is worth BranchBonus and
        /// a seed draining to another lift's bottom is worth OtherSinkFactor. -1 = nothing left.</summary>
        private int FanBest()
        {
            int nl = m_FanLifts.Count;
            Grow(ref m_FanDone, nl);
            bool[] done = m_FanDone;
            Array.Clear(done, 0, nl);
            while (true)
            {
                int lift = -1;
                double fill = double.PositiveInfinity;
                for (int k = 0; k < nl; k++)
                {
                    if (done[k] || m_FanCap[k] == 0 || m_FanCount[k] >= m_FanCap[k])
                        continue;
                    double f = m_FanCount[k] / (double)m_FanCap[k];
                    if (f < fill)
                    {
                        fill = f;
                        lift = k;
                    }
                }
                if (lift < 0)
                    return -1;
                bool wantTop = m_FanTopHave[lift] < FanTopRunsMin, tooManyTop = m_FanTopHave[lift] >= FanTopRunsMax;
                int best = -1;
                double bestW = 0.0;
                for (int round = wantTop ? 0 : 1; round < 2 && best < 0; round++)
                    for (int s = 0; s < m_FanRes.Count; s++)
                    {
                        FanSeed fs = m_FanRes[s];
                        if (fs.Used || fs.Lift != lift || (round == 0 && fs.Tier != 0) || (tooManyTop && fs.Tier == 0))
                            continue;
                        double w = FanWeight(fs, lift);
                        if (best < 0 || w > bestW || (w == bestW && FanOrder(fs) < FanOrder(m_FanRes[best])))
                        {
                            best = s;
                            bestW = w;
                        }
                    }
                if (best >= 0)
                    return best;
                done[lift] = true;
            }
        }

        private static long FanOrder(FanSeed s) => ((long)s.Lift * Passes + s.Pass) * 1000L + s.Ordinal;

        private double FanWeight(FanSeed s, int lift)
        {
            double pref = (s.Parent >= 0 ? BranchBonus : 1.0) * (s.OwnSink ? 1.0 : OtherSinkFactor);
            if (m_Knobs.Mix == AutoMix.Terrain)
                return s.Score * pref;
            double line = s.Score / SkiResortScore.BandValueOf(SkiResortScore.BandState.Rated, (byte)s.Expected) * pref;
            int b = (int)s.Expected;
            int target = m_FanTarget[lift * 5 + b], have = m_FanHave[lift * 5 + b];
            if (target <= have)
                return 0.1 * line;
            return 1000.0 * Math.Floor(1000.0 * (1.0 - have / (double)target)) + line;
        }

        /// <summary>The seed against the accepted runs, at the pick:
        /// A branch must still be outside every ring and walk as a branch.
        /// A plain seed outside every accepted gap must walk without a stub merge.
        /// A plain tier 0-1 seed inside a gap but outside the rings becomes a branch of the nearest accepted run when it walks as one
        /// (runs leaving the top together).
        /// Else the system drops it.</summary>
        private bool FanWhy(int why, bool ok)
        {
            if (!ok)
                m_FanDropWhy[why]++;
            return ok;
        }

        private bool FanUsable(ref FanSeed s)
        {
            float d = m_DAcc[s.Cell];
            float want = MergeGapM + 0.5f * TargetWidthM[s.Pass];
            if (s.Parent >= 0)
                return FanWhy(3, d >= want && FanWalk(s.Cell, s.Pass, true, true, out _, out _, out _, out _));
            if (!(d < FanGap(s.Pass) + MinHalfWidthM))
                return FanWhy(2, FanWalk(s.Cell, s.Pass, false, true, out _, out _, out _, out _));
            if (s.Tier > 1 || d < want || d > JunctionReachM - 5f + 0.5f * TargetWidthM[s.Pass])
                return FanWhy(0, false);   // a branch top must be linked to its parent (JunctionReachM)
            if (!FanWalk(s.Cell, s.Pass, true, true, out _, out _, out _, out _))
                return FanWhy(1, false);
            float2 pc = CellCenter(s.Cell);
            int parent = -1;
            float best = float.PositiveInfinity;
            for (int a = 0; a < m_Accepted.Count; a++)
            {
                Accepted acc = m_Accepted[a];
                if (math.any(acc.Hi < pc - best) || math.any(acc.Lo > pc + best))
                    continue;
                float da = PointRingDistance(acc.Ring, pc);
                if (da < best)
                {
                    best = da;
                    parent = a;
                }
            }
            s.Parent = parent;
            return parent >= 0;
        }

        /// <summary>Branch seeds of accepted run a. Positions: every BranchStepM from BranchFromM to BranchToM below its top, on both sides.
        /// Each seed is the first cell out along the normal whose distance to the accepted rings reaches MergeGapM + half the pass width + 2 m
        /// (the branch's centerline, its ring abutting the parent). The system adds them per pass it is in band for and not over the lift's
        /// top margin.</summary>
        private void FanBranchSeeds(int a, float fromM, float toM)
        {
            Accepted acc = m_Accepted[a];
            int lift = acc.Cand.Cluster;
            FanLift f = m_FanLifts[lift];
            float2[] cl = acc.Centerline;
            float s = 0f, next = fromM;
            for (int i = 1; i < cl.Length && next <= toM; i++)
            {
                s += math.distance(cl[i - 1], cl[i]);
                if (s < next)
                    continue;
                next += BranchStepM;
                float2 t = math.normalizesafe(cl[Math.Min(i + 1, cl.Length - 1)] - cl[i - 1], new float2(1f, 0f));
                float2 nl = new float2(-t.y, t.x);
                for (int side = 0; side < 2; side++)
                {
                    float2 dir = side == 0 ? nl : -nl;
                    for (int p = 0; p < Passes; p++)
                    {
                        float want = MergeGapM + 0.5f * TargetWidthM[p] + 2f;
                        for (float u = 1f; u <= BranchSearchM; u += 1f)
                        {
                            if (!Cell(cl[i] + dir * u, out int c))
                                break;
                            if (m_DAcc[c] < want)
                                continue;
                            if ((m_Flags[c] & (FOpen | FCliff)) == FOpen && InBand(p, c) && m_DHard[c] >= EdgeSeedMarginM &&
                                m_Pred[p][c] != PredSource && m_H[c] <= f.TopY + f.TopMargin)
                                FanAdd(c, lift, p, math.distance(CellCenter(c), f.Top) <= TopSeedM ? 0 : 1, a, true);
                            break;
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- build-time rules (BuildRing / AttemptStep)

        /// <summary>A branch's gap near its top: MergeGapM at the top, growing to the full gap over BranchTaperM.</summary>
        private static float BranchGap(float gap, float s) => s >= BranchTaperM ? gap : MergeGapM + (gap - MergeGapM) * s / BranchTaperM;

        /// <summary>Resort half width at a centerline sample. Open bowls above a known tree line: up to BowlWidthM. The last MergeZoneM before
        /// a merge or a sink: MergeZoneWiden x (March caps both at the free ground).</summary>
        private float FanHalfWidth(int pass, float half, float toEnd, bool widenEnd, float2 xz)
        {
            float hw = half;
            float tree = m_In.TreeLineY;
            if (!float.IsNaN(tree) && Cell(xz, out int c) && m_H[c] > tree)
                hw = Math.Max(hw, half * BowlWidthM / TargetWidthM[pass]);
            if (widenEnd && toEnd < MergeZoneM)
                hw = Math.Max(hw, half * MergeZoneWiden);
            return hw;
        }

        /// <summary>A run's pod: the pod holding most of its centerline samples, else its seeding lift's pod, else -1 (PodFull and RunPod).</summary>
        private int FanRunPod(float2[] pts, int n, int lift)
        {
            int pod = FanPodOf(pts, n);
            if (pod < 0 && lift >= 0 && lift < m_FanLifts.Count)
                pod = m_FanLifts[lift].Pod;
            return pod >= 0 && pod < m_PodList.Count ? pod : -1;
        }

        /// <summary>The pod holding most of the points (m_PodOf, ties to the lower id), -1 = none.</summary>
        private int FanPodOf(float2[] pts, int n)
        {
            int pods = m_PodList.Count;
            if (pods == 0 || m_PodOf == null)
                return -1;
            Grow(ref m_FanPodCount, pods);
            int[] count = m_FanPodCount;
            Array.Clear(count, 0, pods);
            for (int i = 0; i < n; i++)
                if (Cell(pts[i], out int c) && m_PodOf[c] < pods)
                    count[m_PodOf[c]]++;
            int best = -1;
            for (int k = 0; k < pods; k++)
                if (count[k] > 0 && (best < 0 || count[k] > count[best]))
                    best = k;
            return best;
        }

        /// <summary>The developable cap (spec 4.5): true when a built ring of this area would take its pod past DevelopableShare x
        /// SkiableAreaM2 (the reject PodFull).</summary>
        private bool FanPodFull(GradePath path, double area, int lift)
        {
            int pod = FanRunPod(path.Xz, path.Count, lift);
            if (pod < 0)
                return false;
            PlanPod pd = m_PodList[pod];
            return m_FanPodArea[pod] + area > pd.DevelopableShare * pd.SkiableAreaM2;
        }

        // ---------------------------------------------------------------- the plan

        /// <summary>RunLift (the lift whose fan seeded the run = its cluster, -1 without fans), RunPod (FanPodOf over the run's centerline,
        /// else the seeding lift's pod, else -1), PodRuns grouped by pod and each pod's RunFirst / RunCount. For FinishResort (A6) after
        /// LiftPlan. FinishResortUnit calls it when the plan does not have them yet.</summary>
        private void FanRunPlan(ResortPlan plan, AutoPlanResult res)
        {
            int n = res.Runs.Count, pods = plan.Pods.Length;
            plan.RunLift = new int[n];
            plan.RunPod = new int[n];
            var per = new int[pods];
            for (int i = 0; i < n; i++)
            {
                AutoRun run = res.Runs[i];
                int lift = FansOn && run.Cluster >= 0 && run.Cluster < plan.Lifts.Length ? run.Cluster : -1;
                int pod = run.Centerline != null ? FanRunPod(run.Centerline, run.Centerline.Length, lift) : lift >= 0 ? plan.Lifts[lift].Pod : -1;
                if (pod >= pods)
                    pod = -1;
                plan.RunLift[i] = lift;
                plan.RunPod[i] = pod;
                if (pod >= 0)
                    per[pod]++;
            }
            int at = 0;
            for (int k = 0; k < pods; k++)
            {
                plan.Pods[k].RunFirst = at;
                plan.Pods[k].RunCount = per[k];
                at += per[k];
            }
            plan.PodRuns = new int[at];
            var fill = new int[pods];
            for (int i = 0; i < n; i++)
            {
                int pod = plan.RunPod[i];
                if (pod >= 0)
                    plan.PodRuns[plan.Pods[pod].RunFirst + fill[pod]++] = i;
            }
        }
    }
}
