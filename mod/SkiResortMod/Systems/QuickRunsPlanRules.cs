// Resort Planner (v2) hub side, pure rules (stage A, task A7, stage-a-spec.md 6.1-6.3). It uses no Entities and no Unity API beyond
// Unity.Mathematics, so the test host links it (tests/AutoGenBatch/QuickRunsTests.cs). It covers:
// - which runs a pod or lift toggle leaves out
// - the per-pod rows the hub shows (runs, enabled, band mix, skiers, fee)
// - the pod letter and lift id
// - the click / hover picks of a pod ring or a lift chord
// - the "plan around this lift" region (the lift's 150 m corridor plus 400 m beyond the bottom station along the fall line, shrunk until it
//   fits the area cap)
// Engine notes: EngineNotes/QuickRunsControls.tsx.md, EngineNotes/SkiResortHubUISystem.md (Resort Planner v2)

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>One pod row of the hub: the numbers of the pod's runs (indices by ResortPlan.RunPod) next to its lift.</summary>
    public sealed class QuickPodRow
    {
        public int Id;
        public uint Key;
        public int Runs, Kept;   // Kept = runs switched on
        public readonly int[] Count = new int[5];   // enabled runs by SlopeBand
        public double Skiers;
        public long Fee;
        public bool On;                              // neither the pod nor its lift is switched off
    }

    public static class QuickRunsPlanRules
    {
        public const float AroundHalfWidthM = 150f;   // LiftServedRule.ServeRadiusM
        public const float AroundBeyondM = 400f;
        public const float StationPickM = 30f;        // a click this close (xz) to a census station picks its lift
        public const float LiftPickM = 15f;           // a click this close to a proposed chord toggles the lift
        public const float PodPickM = 40f;            // the cluster ring's radius (QuickRunsToolSystem.ClusterPickM)
        private const float SimplifyM = 12f;
        private const float MinSideM = 20f;
        private const float EpsM = 1f;

        public const int MaxLifts = 9;

        public enum PlanPickKind { None, Lift, Run, Pod }

        /// <summary>The one pick hover and click share (AR-12): a proposed lift's chord (or a pinned lift's drawn station ring), else a
        /// run, else a pod ring. index = the lift / run / pod index. -1 with None.</summary>
        public static PlanPickKind PickAt(ResortPlan plan, IReadOnlyList<AutoRun> runs, float2 p, out int index)
        {
            index = NearestLift(plan, p, LiftPickM);
            if (index >= 0)
                return PlanPickKind.Lift;
            index = QuickRunsRules.Pick(p, runs);
            if (index >= 0)
                return PlanPickKind.Run;
            index = NearestPod(plan, p, PodPickM);
            return index >= 0 ? PlanPickKind.Pod : PlanPickKind.None;
        }

        /// <summary>The lift row reads on only when the lift AND its pod are on (a pod switched off on the map leaves its lift row off).</summary>
        public static bool LiftOn(ResortPlan plan, int lift, HashSet<uint> offPods, HashSet<uint> offLifts)
        {
            if (plan == null || lift < 0 || lift >= plan.Lifts.Length || offLifts.Contains(plan.Lifts[lift].Key))
                return false;
            int pod = plan.Lifts[lift].Pod;
            return !(pod >= 0 && pod < plan.Pods.Length && offPods.Contains(plan.Pods[pod].Key));
        }

        /// <summary>The honest mix as three whole percents shared by the hub and the tooltip. Green and blue are rounded on their own.
        /// Black (with double black) = the rated total (rounded once) minus those two, so the three always add up to the total.</summary>
        public static void MixPercents(float[] mix, out int green, out int blue, out int black)
        {
            green = blue = black = 0;
            if (mix == null || mix.Length < 5)
                return;
            green = (int)Math.Round(mix[1] * 100f);
            blue = (int)Math.Round(mix[2] * 100f);
            int rated = (int)Math.Round((mix[1] + mix[2] + mix[3] + mix[4]) * 100f);
            black = Math.Max(0, rated - green - blue);
        }

        /// <summary>The Lifts knob: 0 = Auto, 1..MaxLifts a cap (the hub sends ints, anything else clamps).</summary>
        public static int ClampLifts(int v) => v < 0 ? 0 : v > MaxLifts ? MaxLifts : v;

        /// <summary>The Home knob: 0 Auto, 1 Green, 2 Blue (anything else is Auto).</summary>
        public static HomeLevel ClampHome(int v) => v >= 0 && v <= (int)HomeLevel.Blue ? (HomeLevel)v : HomeLevel.Auto;

        /// <summary>"A", "B", ... by pod id (MaxPods is 12, and it wraps after Z). The one pod letter for the hub, tooltip and world
        /// labels.</summary>
        public static string Letter(int podId) => ((char)('A' + (podId < 0 ? 0 : podId % 26))).ToString();

        /// <summary>1-based place of the lift among the lifts of its origin, in lift order: "P1", "P2" ... for proposals, "Existing lift"
        /// (a locale key, no number) for pinned lifts. 0 for an index outside the plan.</summary>
        public static int Ordinal(ResortPlan plan, int lift)
        {
            if (plan == null || lift < 0 || lift >= plan.Lifts.Length)
                return 0;
            LiftOrigin origin = plan.Lifts[lift].Origin;
            int n = 0;
            for (int i = 0; i <= lift; i++)
                if (plan.Lifts[i].Origin == origin)
                    n++;
            return n;
        }

        /// <summary>The run is left out because its pod, its pod's lift, or the lift whose corridor seeded it is switched off.</summary>
        public static bool LeftOut(ResortPlan plan, int run, HashSet<uint> offPods, HashSet<uint> offLifts)
        {
            if (plan == null || run < 0 || (offPods.Count == 0 && offLifts.Count == 0))
                return false;
            int pod = run < plan.RunPod.Length ? plan.RunPod[run] : -1;
            if (pod >= 0 && pod < plan.Pods.Length)
            {
                if (offPods.Contains(plan.Pods[pod].Key))
                    return true;
                int l = plan.Pods[pod].Lift;
                if (l >= 0 && l < plan.Lifts.Length && offLifts.Contains(plan.Lifts[l].Key))
                    return true;
            }
            int seeded = run < plan.RunLift.Length ? plan.RunLift[run] : -1;
            return seeded >= 0 && seeded < plan.Lifts.Length && offLifts.Contains(plan.Lifts[seeded].Key);
        }

        /// <summary>One row per pod: totals over the pod's runs (enabled[i] for result.Runs[i]. A null or short array counts a run as
        /// enabled).</summary>
        public static QuickPodRow[] PodRows(AutoPlanResult result, bool[] enabled, HashSet<uint> offPods, HashSet<uint> offLifts)
        {
            ResortPlan plan = result?.Resort;
            if (plan == null)
                return new QuickPodRow[0];
            var rows = new QuickPodRow[plan.Pods.Length];
            for (int p = 0; p < rows.Length; p++)
            {
                int l = plan.Pods[p].Lift;
                bool liftOff = l >= 0 && l < plan.Lifts.Length && offLifts.Contains(plan.Lifts[l].Key);
                rows[p] = new QuickPodRow { Id = plan.Pods[p].Id, Key = plan.Pods[p].Key, On = !offPods.Contains(plan.Pods[p].Key) && !liftOff };
            }
            int n = Math.Min(result.Runs.Count, plan.RunPod.Length);
            for (int i = 0; i < n; i++)
            {
                int p = plan.RunPod[i];
                AutoRun run = result.Runs[i];
                if (p < 0 || p >= rows.Length || run == null)
                    continue;
                QuickPodRow row = rows[p];
                row.Runs++;
                if (enabled != null && i < enabled.Length && !enabled[i])
                    continue;
                row.Kept++;
                int band = (int)run.Band;
                if (band >= 0 && band < 5)
                    row.Count[band]++;
                row.Fee += QuickRunsRules.FeeOf(run.Kind, run.AreaM2);
                row.Skiers += QuickRunsRules.SkiersOf(run.Kind, run.Band, run.AreaM2);
            }
            return rows;
        }

        /// <summary>The pod whose top ring contains the point (nearest), or -1.</summary>
        public static int NearestPod(ResortPlan plan, float2 p, float radiusM)
        {
            int best = -1;
            float bd = radiusM;
            if (plan == null)
                return best;
            for (int i = 0; i < plan.Pods.Length; i++)
            {
                float d = math.distance(plan.Pods[i].TopCenter, p);
                if (d <= bd)
                {
                    bd = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The lift under the point, or -1. This is a proposed lift whose chord (Bottom -> Top, xz) passes within radiusM
        /// (nearest), or a pinned lift whose drawn station ring contains the point. The overlay draws only the rings for an existing lift,
        /// not its rope. Lifts without finite stations (a pinned lift outside the last census build) have nothing to hit.</summary>
        public static int NearestLift(ResortPlan plan, float2 p, float radiusM)
        {
            int best = -1;
            float bd = radiusM;
            if (plan == null)
                return best;
            for (int i = 0; i < plan.Lifts.Length; i++)
            {
                float2 a = plan.Lifts[i].Bottom.xz, b = plan.Lifts[i].Top.xz;
                if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)))
                    continue;
                float d;
                if (plan.Lifts[i].Origin == LiftOrigin.Pinned)
                {
                    float[] hds = SkiRunAutoPlanner.StationHalfDiagonalM;
                    float ring = hds[Math.Min((int)plan.Lifts[i].TypeIndex, hds.Length - 1)];   // half the drawn lot
                    d = Math.Min(math.distance(p, a), math.distance(p, b));
                    if (d > ring)
                        continue;
                    d = d / ring * radiusM;   // inside the ring: ranked on the pick radius scale
                }
                else
                    d = SegDistance(p, a, b);
                if (d <= bd)
                {
                    bd = d;
                    best = i;
                }
            }
            return best;
        }

        public static float SegDistance(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len2 = math.lengthsq(ab);
            float t = len2 <= 1e-6f ? 0f : math.saturate(math.dot(p - a, ab) / len2);
            return math.distance(p, a + ab * t);
        }

        // ---------------------------------------------------------------- plan around this lift

        /// <summary>The "plan around this lift" region: a polygon around <paramref name="line"/> (the lift's chord ends from the bottom to
        /// the top), AroundHalfWidthM to each side. AroundBeyondM is added past the bottom along <paramref name="down"/> (the fall line, or
        /// the lift's own direction when zero). The half width shrinks until the area fits <paramref name="maxAreaM2"/>. Null for fewer
        /// than two distinct points.</summary>
        public static float2[] AroundRegion(IList<float2> line, float2 down, float maxAreaM2)
        {
            var pts = new List<float2>();
            for (int i = 0; i < line.Count; i++)
                if (math.all(math.isfinite(line[i])) && (pts.Count == 0 || math.distance(pts[pts.Count - 1], line[i]) > EpsM))
                    pts.Add(line[i]);
            if (pts.Count < 2)
                return null;
            float2 d = math.normalizesafe(down, math.normalizesafe(pts[0] - pts[pts.Count - 1]));
            if (math.lengthsq(d) < 0.5f)
                return null;
            pts.Insert(0, pts[0] + d * AroundBeyondM);
            pts = Simplify(pts, SimplifyM);   // after the extension: a lift straight down the fall line stays a 4-corner rectangle
            float half = AroundHalfWidthM;
            float2[] poly = Offset(pts, half);
            while (Area(poly) > maxAreaM2 && half > MinSideM)
            {
                half = math.max(MinSideM, half * 0.85f);
                poly = Offset(pts, half);
            }
            return poly;
        }

        /// <summary>The left offset chain, then the right one reversed (a closed ring without a closing duplicate).</summary>
        private static float2[] Offset(List<float2> pts, float half)
        {
            int n = pts.Count;
            var poly = new float2[2 * n];
            for (int i = 0; i < n; i++)
            {
                float2 dir = math.normalizesafe(pts[Math.Min(i + 1, n - 1)] - pts[Math.Max(i - 1, 0)], new float2(1f, 0f));
                float2 nrm = new float2(-dir.y, dir.x);
                poly[i] = pts[i] + nrm * half;
                poly[2 * n - 1 - i] = pts[i] - nrm * half;
            }
            return poly;
        }

        public static float Area(float2[] ring)
        {
            if (ring == null || ring.Length < 3)
                return 0f;
            double a = 0.0;
            for (int i = 0; i < ring.Length; i++)
            {
                float2 p = ring[i], q = ring[(i + 1) % ring.Length];
                a += (double)p.x * q.y - (double)q.x * p.y;
            }
            return (float)(Math.Abs(a) * 0.5);
        }

        /// <summary>Douglas-Peucker (iterative): the lift's per-edge chords collapse to the few vertices of its bends.</summary>
        private static List<float2> Simplify(List<float2> pts, float tolM)
        {
            int n = pts.Count;
            if (n <= 2)
                return pts;
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, n - 1));
            while (stack.Count > 0)
            {
                (int a, int b) = stack.Pop();
                float far = 0f;
                int at = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float dist = SegDistance(pts[i], pts[a], pts[b]);
                    if (dist > far)
                    {
                        far = dist;
                        at = i;
                    }
                }
                if (at >= 0 && far > tolM)
                {
                    keep[at] = true;
                    stack.Push((a, at));
                    stack.Push((at, b));
                }
            }
            var result = new List<float2>();
            for (int i = 0; i < n; i++)
                if (keep[i])
                    result.Add(pts[i]);
            return result;
        }
    }
}
