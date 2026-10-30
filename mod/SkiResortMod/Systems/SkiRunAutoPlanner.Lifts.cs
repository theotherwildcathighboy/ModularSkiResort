// Resort Planner (v2) stage A: the lift skeleton (spec 4.4). Stage Lifts, Resort mode only, between Pods and the band passes (Route).
// Pinned lifts first: each existing lift (AutoPlanInput.Pinned) becomes a PlanLift with its own chords and the pod that holds most of its
// 150 m corridor; that pod gets no proposal. Then per unserved pod the chord candidates (bottom = a station spot on the pod's lower pad,
// top = its upper pad, its top cell, or a lower top on the same line when the full chord breaks the type's rise / length norms) for the
// four lift types, filtered by the cheap rules (length and rise norms of [A], finite heights), ordered per pod by (ends that must sink,
// the type preference: cheapest first, detachable / gondola first for the top third of pod values, then the top kind). Selection is a lazy
// greedy over the pods' front candidates by score (pod value + stacking bonus - cost - fall-line crossings); a pick runs its tower plan
// (LiftTowerPlanner.Plan, the lift tool's own planner and constants, edge slope checked) in its own unit and is selected when every span
// clears the floor and the edge slopes stay under EdgeSlopeShare x the type cap; a failed pick moves its pod to its next candidate (at most
// PlansPerPod plans per pod, MaxTowerPlansOf per scan). On the main thread (AutoPlanInput.MainThread, the fallback setting) no tower
// plan runs: one plan is 10-60 ms CoreCLR for a detachable, several frames on Mono, so a pick is selected with its towers unchecked
// (PlanLift.TowersUnchecked; the lift tool plans the line when it is built). A station spot is a pad (DECISIONS 447: a graded site,
// never "needs flat ground"): a disc of the lot's half diagonal walked from the pad's Center toward the other end while it stays on the pad; an end with no
// pad that holds the lot is flagged Sunk (the station is graded / sunk into the slope: the C1 3b rule) and still proposed.
// Output: m_LiftList (pinned first, then proposals in selection order) and m_PodLift; FinishResort copies them into the plan (LiftPlan)
// and counts Serves / StartsNearTop / EndsAtBottom against the final runs. Selected lifts' BottomPad cells are the band passes' sources
// (A5, Fans.cs IsResortSource). Reads m_PadList / m_PodList / m_PadOf / m_PodOf / m_H (final after Pods), never the scratch layers.
// Pure and Entities-free (usings System, System.Collections.Generic, Unity.Mathematics only). Spec: docs/research/quickruns-v2/spec/stage-a-spec.md.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        // ---------------------------------------------------------------- constants (spec 4.4; [A] p10-p90, moonshot 5.2.3)
        /// <summary>Lift norms by SkiCatalog type index (fixedGrip, detachable, gondola, tram): length and rise (p10-p90 of [A]).</summary>
        public static readonly float[] LiftLengthMin = { 300f, 600f, 800f, 800f }, LiftLengthMax = { 1500f, 2200f, 2700f, 3000f };
        public static readonly float[] LiftRiseMin = { 95f, 95f, 96f, 220f }, LiftRiseMax = { 512f, 512f, 781f, 888f };
        /// <summary>A proposal's edge slopes stay under this share of the line prefab's cap (FG 0.8, DQ 0.9, GD 1.2, tram 2.5:
        /// DC/Game.Net/ValidationHelpers.cs:1456-1490; LiftTowerPlanConstants.MaxEdgeSlope).</summary>
        public const float LiftEdgeSlopeShare = 0.97f;
        public const float StackBonus = 0.2f, StackNearM = 300f, StackVerticalM = 80f, CrossingPenalty = 0.5f;
        /// <summary>Score units: skiers at once (ha x DevelopableShare x SkiersPerHa); one skier is worth this much build cost.</summary>
        public const double LiftCostWeight = 1.0e-3;
        public const int MaxLiftsKnob = 9, PlansPerPod = 3;
        public const float AutoKm2PerLift = 2f, AutoVerticalPerLiftM = 400f;
        /// <summary>A pad station spot walks from the pad's Center toward the other end in these steps, at most this far (and half the chord).</summary>
        public const float SpotWalkStepM = 6f, SpotWalkMaxM = 150f;
        /// <summary>Gate counters (F-2 / F-3): run tops / ends within this of the lift's Top / Bottom.</summary>
        public const float LiftNearM = 100f;
        /// <summary>The lowered top walks down the chord in these steps (a top under the type's max rise and length).</summary>
        public const float LowerTopStepM = 10f, LowerTopRiseShare = 0.95f;

        /// <summary>Tower plans at most per scan: 3 x MaxPodsOf (spec 4.4).</summary>
        public static int MaxTowerPlansOf(float areaM2) => 3 * MaxPodsOf(areaM2);

        /// <summary>The Auto lift count: clamp(max(round(km2 / 2), ceil(vertical / 400 m)), 1, 9) (moonshot 5.2.3, Q6).</summary>
        public static int AutoLiftsOf(float areaM2, float verticalM)
        {
            int byArea = (int)Math.Floor(areaM2 / 1.0e6f / AutoKm2PerLift + 0.5f);
            int byVertical = (int)Math.Ceiling(Math.Max(0f, verticalM) / AutoVerticalPerLiftM);
            return Math.Max(1, Math.Min(MaxLiftsKnob, Math.Max(byArea, byVertical)));
        }

        /// <summary>The rated band a pod class is skied as (SkiResortScore bands): Beginner and below green, Intermediate blue, Advanced
        /// black, Expert and Cliff double black.</summary>
        public static byte ScoreBandOf(TerrainBand c) =>
            c <= TerrainBand.Beginner ? SkiResortScore.BandGreen : c == TerrainBand.Intermediate ? SkiResortScore.BandBlue
            : c == TerrainBand.Advanced ? SkiResortScore.BandBlack : SkiResortScore.BandDoubleBlack;

        // ---------------------------------------------------------------- state
        private struct LiftCand
        {
            public int Pod, BottomPad, TopPad;
            public byte Type, TopKind;            // TopKind 0 upper pad, 1 top cell, 2 lowered top
            public float3 Bottom, Top;
            public float LengthM, RiseM;
            public bool BottomSunk, TopSunk;
            public long Cost;
            public int Crossings;
            public double Static;                 // value - cost x weight - crossings x penalty x value
        }

        private readonly List<LiftCand> m_LiftCands = new List<LiftCand>();
        private readonly List<PlanLift> m_LiftList = new List<PlanLift>();
        private readonly List<float2> m_LiftChords = new List<float2>();      // pinned chord ends in pairs, then nothing (proposals are one chord)
        private readonly List<int> m_LiftChordFirst = new List<int>();        // per m_LiftList entry: first chord end index, count of pairs
        private readonly List<int> m_LiftChordCount = new List<int>();
        private int[] m_PodLift = new int[16], m_PodCandFirst = new int[16], m_PodCandEnd = new int[16], m_PodCandAt = new int[16], m_PodPlans = new int[16];
        private double[] m_PodValue = new double[16];
        private int[] m_PinCount = new int[64];
        private float4[] m_PinBox = new float4[4];                            // per pinned lift: corridor bbox (min x, min z, max x, max z)
        private readonly LiftTowerPlanner m_TowerPlanner = new LiftTowerPlanner();
        private readonly LiftTowerPlanConstants[] m_TowerConsts = new LiftTowerPlanConstants[4];
        private readonly ChordSampler m_ChordSampler = new ChordSampler();
        private LiftTerrainSampler m_ChordSample;
        private int m_LiftWanted, m_LiftPinned, m_TowerPlans, m_TowerPlanCap, m_TowersUnchecked;
        private float m_TowerPlanMaxMs;
        private double m_TowerPlanMs;
        private readonly int[] m_LiftStat = new int[LiftStatCount];
        private readonly List<TowerPlanRecord> m_PlanLog = new List<TowerPlanRecord>();

        /// <summary>One tower plan of the last Lifts stage (diagnostics and the host REPORT): the pick, its line, the outcome, the time.</summary>
        public struct TowerPlanRecord
        {
            public int Pod;
            public byte Type, TopKind;
            public float LengthM, RiseM, Ms, MaxEdgeSlope;
            public int Towers;
            public bool Clear;
        }

        /// <summary>The tower plans of the last Lifts stage in run order.</summary>
        public IReadOnlyList<TowerPlanRecord> TowerPlanLog => m_PlanLog;

        /// <summary>Counters of the last Lifts stage (host REPORT): candidates, norm rejects, lowered tops, tower plans that failed, picks
        /// refused for a station / chord conflict, pods left unserved by the cap.</summary>
        public const int LiftStatCandidates = 0, LiftStatNorms = 1, LiftStatLowered = 2, LiftStatTowerFail = 3, LiftStatConflict = 4,
                         LiftStatCapped = 5, LiftStatCount = 6;

        /// <summary>Tower plans run by the last Lifts stage (P-5: at most MaxTowerPlansOf), their total and longest ms.</summary>
        public int TowerPlans => m_TowerPlans;
        /// <summary>Picks of the last Lifts stage selected without a tower plan (main thread only, AutoPlanInput.MainThread).</summary>
        public int TowersUncheckedCount => m_TowersUnchecked;
        public float TowerPlanMaxMs => m_TowerPlanMaxMs;
        public double TowerPlanMs => m_TowerPlanMs;
        /// <summary>The lift count the last Lifts stage aimed for (knob or Auto, pinned included for Auto).</summary>
        public int LiftsWanted => m_LiftWanted + m_LiftPinned;
        public int LiftStat(int k) => k >= 0 && k < LiftStatCount ? m_LiftStat[k] : 0;

        /// <summary>The ground along a candidate chord for LiftTowerPlanner (along from the bottom, lateral to its right). A miss (outside the
        /// snapshot) returns the last good height and is counted: a plan with a miss is refused.</summary>
        private sealed class ChordSampler
        {
            public HeightSnapshot Snap;
            public float2 A, Dir, Right;
            public float Last;
            public int Misses;

            public float Sample(float along, float lateral)
            {
                float2 p = A + Dir * along + Right * lateral;
                float h = Snap.Sample(new float3(p.x, 0f, p.y));
                if (float.IsNaN(h))
                {
                    Misses++;
                    return Last;
                }
                Last = h;
                return h;
            }
        }

        // ---------------------------------------------------------------- stage Lifts

        /// <summary>Stage Lifts (Resort only). m_Cursor2: 0 setup and pinned ends; 1 rows: the pinned corridors' pod counts; 2 one pod per
        /// unit: its candidates; 3 one selection pick per unit (at most one tower plan); then EnterRoute.</summary>
        private void UnitLifts()
        {
            switch (m_Cursor2)
            {
                case 0: LiftSetup(); break;
                case 1: LiftCorridorRows(); break;
                case 2: LiftCandidates(); break;
                default: LiftPick(); break;
            }
        }

        private void LiftSetup()
        {
            int pods = m_PodList.Count;
            m_LiftCands.Clear();
            m_LiftList.Clear();
            m_LiftChords.Clear();
            m_LiftChordFirst.Clear();
            m_LiftChordCount.Clear();
            Array.Clear(m_LiftStat, 0, LiftStatCount);
            m_TowerPlans = 0;
            m_TowersUnchecked = 0;
            m_PlanLog.Clear();
            m_TowerPlanMaxMs = 0f;
            m_TowerPlanMs = 0.0;
            m_TowerPlanCap = MaxTowerPlansOf(m_Result.RegionAreaM2);
            Grow(ref m_PodLift, pods);
            Grow(ref m_PodCandFirst, pods);
            Grow(ref m_PodCandEnd, pods);
            Grow(ref m_PodCandAt, pods);
            Grow(ref m_PodPlans, pods);
            Grow(ref m_PodValue, pods);
            for (int k = 0; k < pods; k++)
            {
                m_PodLift[k] = -1;
                m_PodCandFirst[k] = m_PodCandEnd[k] = m_PodCandAt[k] = m_PodPlans[k] = 0;
                PlanPod pod = m_PodList[k];
                m_PodValue[k] = pod.SkiableAreaM2 / 1.0e4 * pod.DevelopableShare *
                                SkiResortScore.SkiersPerHa(SkiResortScore.BandState.Rated, ScoreBandOf(pod.PrimaryClass));
            }
            if (m_ChordSample == null)
                m_ChordSample = m_ChordSampler.Sample;
            for (int t = 0; t < m_TowerConsts.Length; t++)
                if (m_TowerConsts[t] == null && t < SkiCatalog.Lifts.Count)
                    m_TowerConsts[t] = LiftTowerPlanConstants.For(SkiCatalog.Lifts[t]);

            // Pinned lifts: the input order (sorted by the tool: key, then xz); ends from the census, NaN ends from the chord ends
            List<PinnedLift> pinned = m_In.Pinned;
            int np = pinned?.Count ?? 0;
            m_LiftPinned = np;
            if (m_PinBox.Length < np)
                m_PinBox = new float4[np];
            Grow(ref m_PinCount, Math.Max(1, np * Math.Max(1, pods)));
            Array.Clear(m_PinCount, 0, m_PinCount.Length);
            for (int p = 0; p < np; p++)
                AddPinned(pinned[p], p);
            int wanted = m_Knobs.Lifts > 0 ? Math.Min(MaxLiftsKnob, m_Knobs.Lifts)
                : Math.Max(0, AutoLiftsOf(m_Result.RegionAreaM2, m_Result.VerticalM) - np);
            m_LiftWanted = wanted;
            m_Cursor = m_RegionJ0;
            m_Cursor2 = np > 0 && pods > 0 ? 1 : 2;
            if (m_Cursor2 == 2)
                LiftPinnedPods();
        }

        private void AddPinned(PinnedLift pl, int p)
        {
            int first = m_LiftChords.Count;
            if (pl.Chords != null)
                for (int i = 0; i + 1 < pl.Chords.Length; i += 2)
                    if (Finite(pl.Chords[i].x) && Finite(pl.Chords[i].y) && Finite(pl.Chords[i + 1].x) && Finite(pl.Chords[i + 1].y))
                    {
                        m_LiftChords.Add(pl.Chords[i]);
                        m_LiftChords.Add(pl.Chords[i + 1]);
                    }
            float3 bottom = pl.Bottom, top = pl.Top;
            PinnedEnds(first, ref bottom, ref top);
            if (m_LiftChords.Count == first && Finite3(bottom) && Finite3(top))
            {
                m_LiftChords.Add(bottom.xz);   // no chord in the census copy: the station chord stands in (corridor, conflicts, Serves)
                m_LiftChords.Add(top.xz);
            }
            int pairs = (m_LiftChords.Count - first) / 2;
            float length = 0f;
            float4 box = new float4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            for (int i = first; i < m_LiftChords.Count; i += 2)
            {
                length += math.distance(m_LiftChords[i], m_LiftChords[i + 1]);
                for (int e = 0; e < 2; e++)
                {
                    float2 q = m_LiftChords[i + e];
                    box = new float4(Math.Min(box.x, q.x), Math.Min(box.y, q.y), Math.Max(box.z, q.x), Math.Max(box.w, q.y));
                }
            }
            float r = LiftServedRule.ServeRadiusM;
            m_PinBox[p] = box + new float4(-r, -r, r, r);
            m_LiftChordFirst.Add(first);
            m_LiftChordCount.Add(pairs);
            m_LiftList.Add(new PlanLift
            {
                Id = m_LiftList.Count, Key = pl.Key, Origin = LiftOrigin.Pinned, TypeIndex = pl.TypeIndex,
                Bottom = bottom, Top = top, LengthM = length, RiseM = top.y - bottom.y, MaxEdgeSlope = 0f,
                Towers = -1, TowerXz = new float2[0], Pod = -1, BottomPad = PadAtXz(bottom.xz), TopPad = PadAtXz(top.xz),
                Home = HomeVerdict.None, HomeRun = -1,
            });
        }

        /// <summary>A pinned lift's missing station (NaN: not in the census's last build) from its chord ends: the polyline's free ends
        /// (an end used by one chord only; else the first and last chord ends); a known station keeps its end and the missing one takes the
        /// farther free end; with both missing the lower free end is the bottom. Heights from the snapshot (NaN outside it).</summary>
        private void PinnedEnds(int first, ref float3 bottom, ref float3 top)
        {
            bool okB = Finite3(bottom), okT = Finite3(top);
            int n = m_LiftChords.Count - first;
            if ((okB && okT) || n < 2)
                return;
            float2 e0 = m_LiftChords[first], e1 = m_LiftChords[m_LiftChords.Count - 1];
            int found = 0;
            for (int i = first; i < m_LiftChords.Count && found < 2; i++)
            {
                int uses = 0;
                for (int j = first; j < m_LiftChords.Count; j++)
                    if (m_LiftChords[j].x == m_LiftChords[i].x && m_LiftChords[j].y == m_LiftChords[i].y)
                        uses++;
                if (uses != 1)
                    continue;
                if (found++ == 0)
                    e0 = m_LiftChords[i];
                else
                    e1 = m_LiftChords[i];
            }
            float3 p0 = new float3(e0.x, SampleXz(e0), e0.y), p1 = new float3(e1.x, SampleXz(e1), e1.y);
            if (okB)
                top = math.distancesq(p0.xz, bottom.xz) >= math.distancesq(p1.xz, bottom.xz) ? p0 : p1;
            else if (okT)
                bottom = math.distancesq(p0.xz, top.xz) >= math.distancesq(p1.xz, top.xz) ? p0 : p1;
            else
            {
                bool lowFirst = !(p1.y < p0.y);
                bottom = lowFirst ? p0 : p1;
                top = lowFirst ? p1 : p0;
            }
        }

        // Phase 1, rows: per pinned lift, the pod cells within ServeRadiusM of its chords (the lift's pod = the pod with the most)
        private void LiftCorridorRows()
        {
            int pods = m_PodList.Count, np = m_LiftPinned;
            float r2 = LiftServedRule.ServeRadiusM * LiftServedRule.ServeRadiusM;
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    int pod = m_PodOf[c];
                    if (pod >= pods)
                        continue;
                    float2 xz = CellCenter(c);
                    for (int p = 0; p < np; p++)
                    {
                        float4 b = m_PinBox[p];
                        if (xz.x < b.x || xz.y < b.y || xz.x > b.z || xz.y > b.w)
                            continue;
                        int first = m_LiftChordFirst[p], end = first + 2 * m_LiftChordCount[p];
                        for (int e = first; e < end; e += 2)
                            if (LiftServedRule.DistanceSqXZ(xz, m_LiftChords[e], m_LiftChords[e + 1]) <= r2)
                            {
                                m_PinCount[p * pods + pod]++;
                                break;
                            }
                    }
                }
            m_Cursor = jEnd;
            if (m_Cursor > m_RegionJ1)
            {
                LiftPinnedPods();
                m_Cursor2 = 2;
                m_Cursor = 0;
            }
        }

        private void LiftPinnedPods()
        {
            int pods = m_PodList.Count;
            for (int p = 0; p < m_LiftPinned; p++)
            {
                int best = -1, bestN = 0;
                for (int k = 0; k < pods; k++)
                    if (m_PinCount[p * pods + k] > bestN)
                    {
                        bestN = m_PinCount[p * pods + k];
                        best = k;
                    }
                PlanLift l = m_LiftList[p];
                l.Pod = best;
                m_LiftList[p] = l;
                if (best >= 0 && m_PodLift[best] < 0)
                    m_PodLift[best] = p;   // the first pinned lift (input order) is the pod's lift; no proposal there either way
            }
            m_Cursor = 0;
        }

        // Phase 2, one pod per unit: the pod's candidates in preference order (an unserved pod only)
        private void LiftCandidates()
        {
            int pods = m_PodList.Count;
            if (m_Cursor >= pods)
            {
                m_Cursor2 = 3;
                m_Cursor = 0;
                return;
            }
            int k = m_Cursor++;
            m_PodCandFirst[k] = m_PodCandEnd[k] = m_PodCandAt[k] = m_LiftCands.Count;
            if (m_PodLift[k] >= 0)
                return;
            PlanPod pod = m_PodList[k];
            // Top third of the pod values (ties: the smaller id ranks higher): upgrade first
            int better = 0;
            for (int q = 0; q < pods; q++)
                if (m_PodValue[q] > m_PodValue[k] || (m_PodValue[q] == m_PodValue[k] && q < k))
                    better++;
            bool upgrade = better < (pods + 2) / 3;
            int start = m_LiftCands.Count;
            for (int t = 0; t < m_TowerConsts.Length; t++)
            {
                if (m_TowerConsts[t] == null)
                    continue;
                bool over = false;
                int before = m_LiftCands.Count;
                if (pod.UpperPad >= 0)
                    over |= TryCand(k, (byte)t, m_PadList[pod.UpperPad].Center, pod.UpperPad, 0);
                over |= TryCand(k, (byte)t, pod.TopCenter, PadAtXz(pod.TopCenter), 1);
                if (m_LiftCands.Count == before && over)
                    LoweredTop(k, (byte)t);
            }
            // Preference: fewest sunk ends, then the type order (cheapest first; detachable, gondola first for the top third), then the top kind
            m_LiftCands.Sort(start, m_LiftCands.Count - start, Comparer<LiftCand>.Create((a, b) =>
            {
                int s = Sunk(a).CompareTo(Sunk(b));
                if (s != 0)
                    return s;
                s = TypeRank(a.Type, upgrade).CompareTo(TypeRank(b.Type, upgrade));
                return s != 0 ? s : a.TopKind.CompareTo(b.TopKind);
            }));
            m_PodCandEnd[k] = m_LiftCands.Count;
        }

        private static int Sunk(in LiftCand c) => (c.BottomSunk ? 1 : 0) + (c.TopSunk ? 1 : 0);

        private static int TypeRank(byte t, bool upgrade) => !upgrade ? t : t == 1 ? 0 : t == 2 ? 1 : t == 0 ? 2 : 3;

        /// <summary>One candidate (pod k, type t, a top at topXz on topPad or -1): station spots, heights, the norms. Adds it when it passes;
        /// returns true when it broke the max rise or length only (a lowered top on the same line may fit).</summary>
        private bool TryCand(int k, byte t, float2 topXz, int topPad, byte topKind)
        {
            PlanPod pod = m_PodList[k];
            float hd = StationHalfDiagonalOf(t);
            int lower = pod.LowerPad;
            bool bottomCap = m_PadList[lower].InscribedM >= hd, topCap = topPad >= 0 && m_PadList[topPad].InscribedM >= hd;
            float2 b = bottomCap ? StationSpot(lower, hd, topXz) : m_PadList[lower].Center;
            float2 tp = topCap ? StationSpot(topPad, hd, b) : topXz;
            m_LiftStat[LiftStatCandidates]++;
            float by = SampleXz(b), ty = SampleXz(tp);
            float len = math.distance(b, tp), rise = ty - by;
            if (!Finite(by) || !Finite(ty))
            {
                m_LiftStat[LiftStatNorms]++;
                return false;
            }
            bool tooBig = len > LiftLengthMax[t] || rise > LiftRiseMax[t];
            if (len < LiftLengthMin[t] || rise < LiftRiseMin[t] || tooBig || len < 2f * m_TowerConsts[t].StepM)
            {
                m_LiftStat[LiftStatNorms]++;
                return tooBig && len >= LiftLengthMin[t] && rise >= LiftRiseMin[t];
            }
            AddCand(k, t, new float3(b.x, by, b.y), new float3(tp.x, ty, tp.y), lower, topPad, !bottomCap, !topCap, topKind);
            return false;
        }

        /// <summary>The full chord broke the type's max rise or length: walk the top down the line toward the bottom until the rise is
        /// under LowerTopRiseShare x the max and the length under the max (a mid-mountain top; the station is Sunk unless a pad holds it).</summary>
        private void LoweredTop(int k, byte t)
        {
            PlanPod pod = m_PodList[k];
            float hd = StationHalfDiagonalOf(t);
            int lower = pod.LowerPad;
            bool bottomCap = m_PadList[lower].InscribedM >= hd;
            float2 b = bottomCap ? StationSpot(lower, hd, pod.TopCenter) : m_PadList[lower].Center;
            float by = SampleXz(b);
            float len = math.distance(b, pod.TopCenter);
            if (!Finite(by) || len < 1f)
                return;
            float2 dir = (pod.TopCenter - b) / len;
            for (float s = Math.Min(len, LiftLengthMax[t]); s >= LiftLengthMin[t]; s -= LowerTopStepM)
            {
                float2 tp = b + dir * s;
                float ty = SampleXz(tp);
                if (!Finite(ty) || math.distance(b, tp) > LiftLengthMax[t] || !Cell(tp, out int c) || (m_Flags[c] & FRegion) == 0)
                    continue;   // the distance, not s: the float step can land a hair over the norm
                float rise = ty - by;
                if (rise > LowerTopRiseShare * LiftRiseMax[t])
                    continue;
                if (rise < LiftRiseMin[t])
                    return;
                int topPad = m_PadOf[c] == PadNone ? -1 : m_PadOf[c];
                bool topCap = topPad >= 0 && m_PadList[topPad].InscribedM >= hd;
                m_LiftStat[LiftStatLowered]++;
                AddCand(k, t, new float3(b.x, by, b.y), new float3(tp.x, ty, tp.y), lower, topPad, !bottomCap, !topCap, 2);
                return;
            }
        }

        private void AddCand(int k, byte t, float3 bottom, float3 top, int bottomPad, int topPad, bool bottomSunk, bool topSunk, byte topKind)
        {
            LiftTypeDef def = SkiCatalog.Lifts[t];
            float len = math.distance(bottom.xz, top.xz);
            long cost = def.StationCostBase + def.StationCostTop + (long)Math.Round(def.LinePerKm * (double)len / 1000.0);
            int crossings = 0;
            for (int q = 0; q < m_PodList.Count; q++)
            {
                if (q == k)
                    continue;
                PlanPod other = m_PodList[q];
                if (SegmentsCross(bottom.xz, top.xz, other.TopCenter, m_PadList[other.LowerPad].Center))
                    crossings++;
            }
            double value = m_PodValue[k];
            m_LiftCands.Add(new LiftCand
            {
                Pod = k, Type = t, TopKind = topKind, BottomPad = bottomPad, TopPad = topPad, Bottom = bottom, Top = top,
                LengthM = len, RiseM = top.y - bottom.y, BottomSunk = bottomSunk, TopSunk = topSunk, Cost = cost, Crossings = crossings,
                Static = value - cost * LiftCostWeight - CrossingPenalty * value * crossings,
            });
        }

        // Phase 3, one pick per unit: the best front candidate over the unserved pods (score with the stacking bonus against the lifts
        // selected so far; ties by pod key, then type); a conflict moves its pod on without a plan; a pick runs its tower plan here
        private void LiftPick()
        {
            int proposed = m_LiftList.Count - m_LiftPinned;
            while (proposed < m_LiftWanted && m_TowerPlans < m_TowerPlanCap)
            {
                int bestPod = -1;
                double bestScore = 0.0;
                for (int k = 0; k < m_PodList.Count; k++)
                {
                    if (m_PodLift[k] >= 0 || m_PodCandAt[k] >= m_PodCandEnd[k] || m_PodPlans[k] >= PlansPerPod)
                        continue;
                    LiftCand c = m_LiftCands[m_PodCandAt[k]];
                    double s = c.Static + (Stacked(c) ? StackBonus * m_PodValue[k] : 0.0);
                    if (bestPod < 0 || s > bestScore || (s == bestScore && m_PodList[k].Key < m_PodList[bestPod].Key))
                    {
                        bestPod = k;
                        bestScore = s;
                    }
                }
                if (bestPod < 0)
                    break;
                LiftCand pick = m_LiftCands[m_PodCandAt[bestPod]];
                if (Conflicts(pick))
                {
                    m_LiftStat[LiftStatConflict]++;
                    m_PodCandAt[bestPod]++;
                    continue;
                }
                m_PodPlans[bestPod]++;
                if (m_In.MainThread)
                {
                    m_TowersUnchecked++;
                    SelectCand(pick, null, bestScore);   // the main-thread fallback: no tower plan in a frame (AR-7)
                    return;
                }
                LiftTowerPlan plan = TowerPlan(pick, out bool clear);
                if (clear)
                    SelectCand(pick, plan, bestScore);
                else
                {
                    m_LiftStat[LiftStatTowerFail]++;
                    m_PodCandAt[bestPod]++;
                }
                return;   // one tower plan per unit
            }
            for (int k = 0; k < m_PodList.Count; k++)
                if (m_PodLift[k] < 0 && m_PodCandAt[k] < m_PodCandEnd[k] && m_PodPlans[k] < PlansPerPod)
                    m_LiftStat[LiftStatCapped]++;
            EnterRoute();
        }

        private bool Stacked(in LiftCand c)
        {
            for (int i = 0; i < m_LiftList.Count; i++)
            {
                PlanLift l = m_LiftList[i];
                if (c.BottomPad >= 0 && c.BottomPad == l.TopPad)
                    return true;
                if (Finite3(l.Top) && math.distance(c.Bottom.xz, l.Top.xz) <= StackNearM && Math.Abs(c.Bottom.y - l.Top.y) <= StackVerticalM)
                    return true;
            }
            return false;
        }

        /// <summary>A pick conflicts with a lift already in the list when a station lot of one overlaps a station lot of the other, or one's
        /// chord passes over the other's station lot (the spec's "crosses another selected chord's pads", measured on the station lots:
        /// a base pad of 100 ha may carry several lifts).</summary>
        private bool Conflicts(in LiftCand c)
        {
            float hd = StationHalfDiagonalOf(c.Type);
            for (int i = 0; i < m_LiftList.Count; i++)
            {
                PlanLift l = m_LiftList[i];
                float ho = l.TypeIndex < StationTypes ? StationHalfDiagonalOf(l.TypeIndex) : hd;
                for (int e = 0; e < 2; e++)
                {
                    float3 s = e == 0 ? l.Bottom : l.Top;
                    if (!Finite3(s))
                        continue;
                    if (math.distance(s.xz, c.Bottom.xz) < hd + ho || math.distance(s.xz, c.Top.xz) < hd + ho)
                        return true;
                    if (LiftServedRule.DistanceSqXZ(s.xz, c.Bottom.xz, c.Top.xz) < ho * ho)
                        return true;
                }
                int first = m_LiftChordFirst[i], end = first + 2 * m_LiftChordCount[i];
                for (int q = first; q < end; q += 2)
                    if (LiftServedRule.DistanceSqXZ(c.Bottom.xz, m_LiftChords[q], m_LiftChords[q + 1]) < hd * hd ||
                        LiftServedRule.DistanceSqXZ(c.Top.xz, m_LiftChords[q], m_LiftChords[q + 1]) < hd * hd)
                        return true;
            }
            return false;
        }

        /// <summary>The lift tool's planner on the candidate chord (bottom = station A): stations at both ends, the engine's edge slope rule
        /// with the end nodes at the snapshot heights (the station stub tip sits at the station's ground), ropes at node + the shortest
        /// member's rope height (the planner's fallback when no station lane anchor is known). Clear = every span clears the floor and
        /// the height cap, and the edge slopes stay under LiftEdgeSlopeShare x the type cap. Inputs are validated: Plan never throws.</summary>
        private LiftTowerPlan TowerPlan(in LiftCand c, out bool clear)
        {
            LiftTowerPlanConstants k = m_TowerConsts[c.Type];
            float len = math.distance(c.Bottom.xz, c.Top.xz);
            ChordSampler s = m_ChordSampler;
            s.Snap = m_In.Snapshot;
            s.A = c.Bottom.xz;
            s.Dir = (c.Top.xz - c.Bottom.xz) / len;
            s.Right = new float2(s.Dir.y, -s.Dir.x);
            s.Last = c.Bottom.y;
            s.Misses = 0;
            m_TowerPlanner.LegacySpacing = false;
            m_TowerPlanner.Forbidden = null;
            m_TowerPlanner.StationA = m_TowerPlanner.StationB = true;
            m_TowerPlanner.StationRule = true;
            m_TowerPlanner.CheckEdgeSlope = true;
            m_TowerPlanner.EndNodeYA = c.Bottom.y;
            m_TowerPlanner.EndNodeYB = c.Top.y;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            LiftTowerPlan plan = m_TowerPlanner.Plan(m_ChordSample, len, c.Bottom.y + k.RopeAboveBase(0), c.Top.y + k.RopeAboveBase(0), k);
            float ms = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            m_TowerPlans++;
            m_TowerPlanMs += ms;
            if (ms > m_TowerPlanMaxMs)
                m_TowerPlanMaxMs = ms;
            clear = s.Misses == 0 && plan != null && plan.Clear && plan.MaxEdgeSlope <= LiftEdgeSlopeShare * k.MaxEdgeSlope;
            m_PlanLog.Add(new TowerPlanRecord
            {
                Pod = c.Pod, Type = c.Type, TopKind = c.TopKind, LengthM = len, RiseM = c.RiseM, Ms = ms, MaxEdgeSlope = plan?.MaxEdgeSlope ?? 0f,
                Towers = plan?.LineTowers ?? -1, Clear = clear,
            });
            return plan;
        }

        private void SelectCand(in LiftCand c, LiftTowerPlan plan, double score)
        {
            int id = m_LiftList.Count;
            float len = c.LengthM;
            float2 dir = (c.Top.xz - c.Bottom.xz) / len;
            var towers = new float2[plan?.LineTowers ?? 0];
            for (int n = 1, w = 0; plan != null && n < plan.Nodes.Length - 1 && w < towers.Length; n++)
                if (!plan.Nodes[n].IsStation)
                    towers[w++] = c.Bottom.xz + dir * plan.Nodes[n].AlongM;
            m_LiftChordFirst.Add(m_LiftChords.Count);
            m_LiftChordCount.Add(1);
            m_LiftChords.Add(c.Bottom.xz);
            m_LiftChords.Add(c.Top.xz);
            PlanPod pod = m_PodList[c.Pod];
            m_LiftList.Add(new PlanLift
            {
                Id = id, Key = Fnv(pod.Key, c.Type), Origin = LiftOrigin.Proposed, TypeIndex = c.Type, Bottom = c.Bottom, Top = c.Top,
                LengthM = len, RiseM = c.RiseM, MaxEdgeSlope = plan?.MaxEdgeSlope ?? 0f, Towers = plan?.LineTowers ?? -1, TowerXz = towers,
                TowersUnchecked = plan == null,
                Pod = c.Pod, BottomPad = c.BottomPad, TopPad = c.TopPad, TopSunk = c.TopSunk, BottomSunk = c.BottomSunk, Cost = c.Cost,
                Home = HomeVerdict.None, HomeRun = -1, Score = score,
            });
            m_PodLift[c.Pod] = id;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>A station spot on a pad for a lot of half diagonal hd: from the pad's Center (its largest inscribed distance) toward
        /// <paramref name="toward"/> in SpotWalkStepM steps while a disc of radius hd stays on the pad (at most SpotWalkMaxM and half the
        /// way): the station sits at the pad's edge facing its line, so two lifts sharing a pad get different spots.</summary>
        private float2 StationSpot(int pad, float hd, float2 toward)
        {
            float2 c = m_PadList[pad].Center;
            float len = math.distance(c, toward);
            if (len < SpotWalkStepM)
                return c;
            float2 d = (toward - c) / len;
            float2 best = c;
            float max = Math.Min(SpotWalkMaxM, 0.5f * len);
            for (float s = SpotWalkStepM; s <= max; s += SpotWalkStepM)
            {
                float2 p = c + d * s;
                if (!DiscOnPad(p, hd, pad))
                    break;
                best = p;
            }
            return best;
        }

        // Center, 16 points at radius r and 8 at r / 2 on the pad's cells
        private bool DiscOnPad(float2 p, float r, int pad)
        {
            if (!OnPad(p, pad))
                return false;
            for (int a = 0; a < 16; a++)
            {
                float ang = a * (float)(Math.PI / 8.0);
                float2 u = new float2((float)Math.Cos(ang), (float)Math.Sin(ang));
                if (!OnPad(p + u * r, pad) || ((a & 1) == 0 && !OnPad(p + u * (0.5f * r), pad)))
                    return false;
            }
            return true;
        }

        private bool OnPad(float2 p, int pad) => Cell(p, out int c) && m_PadOf[c] == pad;

        private int PadAtXz(float2 p) => Finite(p.x) && Finite(p.y) && Cell(p, out int c) && m_PadOf[c] != PadNone ? m_PadOf[c] : -1;

        private float SampleXz(float2 p) => m_In.Snapshot.Sample(new float3(p.x, 0f, p.y));

        private static bool Finite3(float3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        /// <summary>Proper crossing of segments ab and cd in xz (touching ends do not count).</summary>
        private static bool SegmentsCross(float2 a, float2 b, float2 c, float2 d)
        {
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(float2 o, float2 a, float2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        // ---------------------------------------------------------------- the plan

        /// <summary>Copies the lift skeleton into the plan (fresh arrays; pinned first, then proposals in selection order), sets each pod's
        /// Lift, counts per lift the runs whose top LiftServedRule.Reaches accepts (the lift's own type rule, as if complete and powered),
        /// the run tops within LiftNearM of its Top and the run ends within LiftNearM of its Bottom, and flags NoLiftSite when proposals
        /// were wanted and none was feasible. Called by FinishResort after TerrainPlan.</summary>
        private void LiftPlan(ResortPlan plan, AutoPlanResult res)
        {
            plan.Lifts = m_LiftList.ToArray();
            for (int k = 0; k < plan.Pods.Length && k < m_PodList.Count; k++)
                plan.Pods[k].Lift = m_PodLift[k];
            var chords = new List<LiftServedRule.Chord>(m_LiftChords.Count / 2);
            for (int q = 0; q + 1 < m_LiftChords.Count; q += 2)
                chords.Add(new LiftServedRule.Chord { A = m_LiftChords[q], B = m_LiftChords[q + 1] });
            int proposed = 0;
            for (int i = 0; i < plan.Lifts.Length; i++)
            {
                ref PlanLift l = ref plan.Lifts[i];
                l.TowerXz = (float2[])l.TowerXz.Clone();
                if (l.Origin == LiftOrigin.Proposed)
                    proposed++;
                LiftTypeDef def = l.TypeIndex < SkiCatalog.Lifts.Count ? SkiCatalog.Lifts[l.TypeIndex] : SkiCatalog.Lifts[0];
                var serve = new LiftServedRule.Lift
                {
                    Complete = true, Power = LiftPower.Unknown, TopY = l.Top.y, FirstChord = m_LiftChordFirst[i] / 2,
                    ChordCount = m_LiftChordCount[i], Rule = (byte)def.ServeRule, RadiusM = def.ServeRadiusM, TopMarginM = def.TopMarginM,
                    TopXZ = l.Top.xz,
                };
                int serves = 0, nearTop = 0, atBottom = 0;
                for (int r = 0; r < res.Runs.Count; r++)
                {
                    AutoRun run = res.Runs[r];
                    if (run?.Nodes == null || run.Nodes.Length == 0 || run.Centerline == null || run.Centerline.Length == 0)
                        continue;
                    float3 top = run.Nodes[0];
                    for (int v = 1; v < run.Nodes.Length; v++)
                        if (run.Nodes[v].y > top.y)
                            top = run.Nodes[v];
                    if (LiftServedRule.Reaches(top, serve, chords, out float _))
                        serves++;
                    if (Finite3(l.Top) && math.distance(run.Centerline[0], l.Top.xz) <= LiftNearM)
                        nearTop++;
                    if (Finite3(l.Bottom) && math.distance(run.Centerline[run.Centerline.Length - 1], l.Bottom.xz) <= LiftNearM)
                        atBottom++;
                }
                l.Serves = serves;
                l.StartsNearTop = nearTop;
                l.EndsAtBottom = atBottom;
            }
            if (proposed == 0 && m_LiftWanted > 0)
                res.Warnings |= AutoPlanWarning.NoLiftSite;
        }
    }
}
