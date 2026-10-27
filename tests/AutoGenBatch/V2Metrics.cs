// V2 metrics (A1): every gate metric of stage-a-gates section 2 as a pure function over AutoPlanResult + ResortPlan. A function returns a
// Gate (id, pass, value, text); the host (V2Tests) decides FAIL (HARD gate, pass false) or REPORT (everything else). Nothing here reads
// the planner or the terrain: the inputs are the result objects, the region area, and for two gates a re-rate delegate (F-10, H-2: the
// host passes SkiRunRater.Rate over the snapshot, the unit tests pass run => run.Band). A gate with nothing to measure (no lift, no pod)
// is NOT a pass: it returns pass false with "n/a" text, so a stubbed planner can never turn a HARD gate green by emptiness.
// Suite V2MetricsTests: hand-made plans with known answers, one passing and one failing case per metric.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal readonly struct Gate
    {
        public readonly string Id;
        public readonly bool Pass;
        public readonly double Value;
        public readonly string Text;

        public Gate(string id, bool pass, double value, string text)
        {
            Id = id; Pass = pass; Value = value; Text = text;
        }

        public override string ToString() => $"{Id} {(Pass ? "ok" : "MISS")}: {Text}";
    }

    internal static class V2Metrics
    {
        // Station lot half diagonals (fixedGrip, detachable, gondola, tram: lots 16x24, 16x32, 24x32, 48x24 m, [C1] 3b)
        public static readonly float[] StationHalfDiagonalM = { 14.4f, 17.9f, 20.0f, 26.8f };
        // Lift norms by SkiCatalog type index (spec 4.4): length, rise p10-p90 of [A], line-prefab edge slope cap
        public static readonly float[] LengthMin = { 300f, 600f, 800f, 800f }, LengthMax = { 1500f, 2200f, 2700f, 3000f };
        public static readonly float[] RiseMin = { 95f, 95f, 96f, 220f }, RiseMax = { 512f, 512f, 781f, 888f };
        public static readonly float[] EdgeSlopeCap = { 0.8f, 0.9f, 1.2f, 2.5f };
        public const float EdgeSlopeShare = 0.97f, PadMinAreaM2 = 2000f, PadMaxGrade = 0.25f, NearM = 100f, LinkM = SkiRunAutoPlanner.LinkMeters;

        private static string F(double v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.InvariantCulture);

        private static Gate G(string id, bool pass, double value, string text) => new Gate(id, pass, value, text);

        private static Gate NA(string id, string what) => new Gate(id, false, double.NaN, "n/a: " + what);

        // ---------------------------------------------------------------- 2.2 structure

        /// <summary>S-1: every pad is at least 0.2 ha with a mean smoothed grade under 25 %; every proposed lift's station spot has
        /// InscribedM at least its lot half diagonal, or is flagged Sunk (no pad = the summit cells: must be Sunk).</summary>
        public static Gate S1Pads(ResortPlan p)
        {
            if (p.Pads.Length == 0)
                return NA("S-1", "no pads");
            int bad = 0;
            foreach (PlanPad pad in p.Pads)
                if (!(pad.AreaM2 >= PadMinAreaM2) || !(pad.MeanGrade < PadMaxGrade))
                    bad++;
            int badSpots = 0;
            foreach (PlanLift l in p.Lifts)
            {
                if (l.Origin == LiftOrigin.Pinned || l.TypeIndex >= StationHalfDiagonalM.Length)
                    continue;
                if (!SpotOk(p, l.BottomPad, l.BottomSunk, StationHalfDiagonalM[l.TypeIndex]))
                    badSpots++;
                if (!SpotOk(p, l.TopPad, l.TopSunk, StationHalfDiagonalM[l.TypeIndex]))
                    badSpots++;
            }
            return G("S-1", bad == 0 && badSpots == 0, bad + badSpots, $"{bad} of {p.Pads.Length} pads under 0.2 ha or over 25 % grade, {badSpots} station spots too small and not sunk (want 0)");
        }

        private static bool SpotOk(ResortPlan p, int pad, bool sunk, float halfDiagonal) =>
            sunk || (pad >= 0 && pad < p.Pads.Length && p.Pads[pad].InscribedM >= halfDiagonal);

        /// <summary>The band of the q-quantile of the skiable cells, from the per-band shares (cliff cells are not skiable: the share is
        /// normalized over what is listed).</summary>
        public static TerrainBand PercentileBand(float[] share, float q)
        {
            double total = 0.0;
            foreach (float s in share)
                total += s;
            double acc = 0.0;
            for (int i = 0; i < share.Length; i++)
            {
                acc += share[i];
                if (total > 0.0 && acc >= q * total - 1e-4)
                    return (TerrainBand)i;
            }
            return TerrainBand.Cliff;
        }

        /// <summary>S-2: PrimaryClass equals the band of the pod's 80th percentile grade, recomputed from the stored band shares.</summary>
        public static Gate S2PodClass(ResortPlan p)
        {
            if (p.Pods.Length == 0)
                return NA("S-2", "no pods");
            int bad = 0;
            foreach (PlanPod pod in p.Pods)
                if (pod.BandShare == null || pod.BandShare.Length != 7 || PercentileBand(pod.BandShare, 0.8f) != pod.PrimaryClass)
                    bad++;
            return G("S-2", bad == 0, bad, $"{bad} of {p.Pods.Length} pods with PrimaryClass off the 80th percentile band (want 0)");
        }

        /// <summary>S-3: at least 2 pods per 4 km2; with needBench at least one pod whose lower pad is a bench.</summary>
        public static Gate S3Pods(ResortPlan p, float areaKm2, bool needBench)
        {
            double per4 = p.Pods.Length / (areaKm2 / 4.0);
            bool bench = false;
            foreach (PlanPod pod in p.Pods)
                bench |= pod.LowerPad >= 0 && pod.LowerPad < p.Pads.Length && p.Pads[pod.LowerPad].IsBench;
            return G("S-3", per4 >= 2.0 - 1e-9 && (!needBench || bench), per4, $"{F(per4)} pods per 4 km2 (want 2+), bench pod {bench}{(needBench ? " (wanted)" : "")}");
        }

        /// <summary>S-4: every proposed lift inside its type's length and rise norms and under 0.97 x the line's edge slope cap.</summary>
        public static Gate S4Lifts(ResortPlan p)
        {
            int n = 0, bad = 0;
            string first = "";
            foreach (PlanLift l in p.Lifts)
            {
                if (l.Origin == LiftOrigin.Pinned)
                    continue;
                n++;
                int t = l.TypeIndex;
                bool ok = t < LengthMin.Length && l.LengthM >= LengthMin[t] && l.LengthM <= LengthMax[t] && l.RiseM >= RiseMin[t] && l.RiseM <= RiseMax[t] &&
                          l.MaxEdgeSlope <= EdgeSlopeShare * EdgeSlopeCap[t];
                if (!ok && bad++ == 0)
                    first = $" (first: lift {l.Id} type {t}, {F(l.LengthM, "0")} m, rise {F(l.RiseM, "0")} m, slope {F(l.MaxEdgeSlope)})";
            }
            return n == 0 ? NA("S-4", "no proposed lifts") : G("S-4", bad == 0, bad, $"{bad} of {n} proposed lifts outside the length / rise / edge slope norms{first}");
        }

        /// <summary>S-5: lift count. Knob 0 (Auto): 1-9 and inside the area band (1 km2 0-2, 4 km2 1-4, 9 km2 3-6, steep-compact 3-6);
        /// knob 1-9: at most the knob.</summary>
        public static Gate S5LiftCount(ResortPlan p, float areaKm2, bool steepCompact, int knobLifts)
        {
            int n = p.Lifts.Length;
            int lo, hi;
            if (knobLifts > 0)
            {
                lo = 1;
                hi = knobLifts;
            }
            else if (steepCompact)
            {
                lo = 3;
                hi = 6;
            }
            else if (areaKm2 < 2.5f)
            {
                lo = 1;
                hi = 2;
            }
            else if (areaKm2 < 6.5f)
            {
                lo = 1;
                hi = 4;
            }
            else
            {
                lo = 3;
                hi = 6;
            }
            hi = Math.Min(hi, 9);
            return G("S-5", n >= lo && n <= hi, n, $"{n} lifts (want {lo}-{hi})");
        }

        /// <summary>S-6: with needBench at least one lift whose bottom pad is a bench.</summary>
        public static Gate S6Stacking(ResortPlan p, bool needBench)
        {
            bool bench = false;
            foreach (PlanLift l in p.Lifts)
                bench |= l.BottomPad >= 0 && l.BottomPad < p.Pads.Length && p.Pads[l.BottomPad].IsBench;
            return needBench ? G("S-6", bench, bench ? 1 : 0, $"bench-bottom lift {bench} (wanted)") : G("S-6", true, bench ? 1 : 0, $"bench-bottom lift {bench} (not required)");
        }

        /// <summary>S-6 (SOFT): the median lift bottom sits at 0.15-0.50 of the plan's vertical (lowest pod bottom to highest pod top).</summary>
        public static Gate S6BottomHeight(ResortPlan p)
        {
            if (p.Lifts.Length == 0 || p.Pods.Length == 0)
                return NA("S-6 bottoms", "no lifts or pods");
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (PlanPod pod in p.Pods)
            {
                lo = Math.Min(lo, pod.BottomY);
                hi = Math.Max(hi, pod.TopY);
            }
            var fr = new List<double>();
            foreach (PlanLift l in p.Lifts)
                fr.Add((l.Bottom.y - lo) / Math.Max(1f, hi - lo));
            double med = Median(fr);
            return G("S-6 bottoms", med >= 0.15 && med <= 0.50, med, $"median bottom at {F(med)} of the vertical (want 0.15-0.50)");
        }

        /// <summary>S-7: each pinned lift is in Lifts with Origin Pinned, and no proposal shares its pod.</summary>
        public static Gate S7Pinned(ResortPlan p, IList<PinnedLift> pinned)
        {
            if (pinned == null || pinned.Count == 0)
                return NA("S-7", "no pinned lifts in the input");
            int bad = 0;
            foreach (PinnedLift pl in pinned)
            {
                int idx = -1;
                for (int i = 0; i < p.Lifts.Length; i++)
                    if (p.Lifts[i].Origin == LiftOrigin.Pinned && p.Lifts[i].Key == pl.Key)
                        idx = i;
                if (idx < 0)
                {
                    bad++;
                    continue;
                }
                int pod = p.Lifts[idx].Pod;
                foreach (PlanLift l in p.Lifts)
                    if (l.Origin == LiftOrigin.Proposed && pod >= 0 && l.Pod == pod)
                        bad++;
            }
            return G("S-7", bad == 0, bad, $"{bad} pinned lifts missing or sharing a pod with a proposal (want 0)");
        }

        // ---------------------------------------------------------------- 2.3 run fans

        private static List<int> RunsOf(AutoPlanResult r, ResortPlan p, int lift)
        {
            var list = new List<int>();
            for (int i = 0; i < r.Runs.Count && i < p.RunLift.Length; i++)
                if (p.RunLift[i] == lift)
                    list.Add(i);
            return list;
        }

        private static float2 Top(AutoRun run) => run.Centerline[0];

        private static float2 Bottom(AutoRun run) => run.Centerline[run.Centerline.Length - 1];

        /// <summary>F-1: runs per lift: median 5-10 (Sparse 4-6, Dense 9-12), every lift 3-14.</summary>
        public static Gate F1TrailsPerLift(AutoPlanResult r, ResortPlan p, AutoDensity density)
        {
            if (p.Lifts.Length == 0)
                return NA("F-1", "no lifts");
            var counts = new List<double>();
            int outside = 0;
            for (int i = 0; i < p.Lifts.Length; i++)
            {
                int c = RunsOf(r, p, i).Count;
                counts.Add(c);
                if (c < 3 || c > 14)
                    outside++;
            }
            double med = Median(counts);
            double lo = density == AutoDensity.Sparse ? 4 : density == AutoDensity.Dense ? 9 : 5, hi = density == AutoDensity.Sparse ? 6 : density == AutoDensity.Dense ? 12 : 10;
            return G("F-1", med >= lo && med <= hi && outside == 0, med, $"median {F(med)} runs per lift (want {lo}-{hi}), {outside} of {p.Lifts.Length} lifts outside 3-14");
        }

        /// <summary>F-2: per lift 2-4 run tops within 100 m of its Top; at most 10 % of lifts outside.</summary>
        public static Gate F2StartsNearTop(AutoPlanResult r, ResortPlan p)
        {
            if (p.Lifts.Length == 0)
                return NA("F-2", "no lifts");
            int outside = 0;
            for (int i = 0; i < p.Lifts.Length; i++)
            {
                float2 top = p.Lifts[i].Top.xz;
                int c = 0;
                foreach (int ri in RunsOf(r, p, i))
                    if (math.distance(Top(r.Runs[ri]), top) <= NearM)
                        c++;
                if (c < 2 || c > 4)
                    outside++;
            }
            double share = (double)outside / p.Lifts.Length;
            return G("F-2", share <= 0.10 + 1e-9, share, $"{outside} of {p.Lifts.Length} lifts without 2-4 run tops within 100 m of Top (want at most 10 %)");
        }

        /// <summary>F-3: per lift 1-4 run bottoms within 100 m of its Bottom.</summary>
        public static Gate F3EndsAtBottom(AutoPlanResult r, ResortPlan p)
        {
            if (p.Lifts.Length == 0)
                return NA("F-3", "no lifts");
            int outside = 0;
            for (int i = 0; i < p.Lifts.Length; i++)
            {
                float2 bot = p.Lifts[i].Bottom.xz;
                int c = 0;
                foreach (int ri in RunsOf(r, p, i))
                    if (math.distance(Bottom(r.Runs[ri]), bot) <= NearM)
                        c++;
                if (c < 1 || c > 4)
                    outside++;
            }
            return G("F-3", outside == 0, outside, $"{outside} of {p.Lifts.Length} lifts without 1-4 run bottoms within 100 m of Bottom (want 0)");
        }

        private static bool OnRun(AutoRun run, float2 pt) =>
            GradeRaster.Inside(run.Ring, pt) || SkiRunAutoPlanner.PointRingDistance(run.Ring, pt) <= LinkM;

        /// <summary>F-4: share of run tops that start on (inside or within 25 m of) another run: 30-65 % (SOFT target 40-60 %).</summary>
        public static Gate F4Branching(AutoPlanResult r)
        {
            if (r.Runs.Count < 2)
                return NA("F-4", "fewer than 2 runs");
            int on = 0;
            for (int i = 0; i < r.Runs.Count; i++)
                for (int j = 0; j < r.Runs.Count; j++)
                    if (j != i && OnRun(r.Runs[j], Top(r.Runs[i])))
                    {
                        on++;
                        break;
                    }
            double share = (double)on / r.Runs.Count;
            return G("F-4", share >= 0.30 && share <= 0.65, share, $"{F(share * 100, "0")} % of run tops start on another run (want 30-65, target 40-60)");
        }

        /// <summary>F-5: median lateral centerline spacing of neighbor runs in a pod 70-140 m; no pair under 40 m outside a merge zone
        /// (samples within 60 m of a junction of the pair are not counted).</summary>
        public static Gate F5Spacing(AutoPlanResult r, ResortPlan p)
        {
            var perRun = new List<double>();
            double minPair = double.PositiveInfinity;
            var samples = new float2[r.Runs.Count][];
            for (int i = 0; i < r.Runs.Count; i++)
                samples[i] = Resample(r.Runs[i].Centerline, 25f);
            for (int a = 0; a < r.Runs.Count; a++)
            {
                double best = double.PositiveInfinity;
                for (int b = 0; b < r.Runs.Count; b++)
                {
                    if (a == b || a >= p.RunPod.Length || b >= p.RunPod.Length || p.RunPod[a] != p.RunPod[b] || p.RunPod[a] < 0)
                        continue;
                    var near = new List<double>();
                    foreach (float2 s in samples[a])
                    {
                        double d = PolylineDistance(s, r.Runs[b].Centerline);
                        if (d > 250.0)
                            continue;
                        if (NearJunction(r, a, b, s))
                            continue;
                        near.Add(d);
                        minPair = Math.Min(minPair, d);
                    }
                    if (near.Count >= 3)
                        best = Math.Min(best, Median(near));
                }
                if (!double.IsInfinity(best))
                    perRun.Add(best);
            }
            if (perRun.Count == 0)
                return NA("F-5", "no neighbor runs inside a pod");
            double med = Median(perRun);
            return G("F-5", med >= 70.0 && med <= 140.0 && minPair >= 40.0, med, $"median spacing {F(med, "0")} m (want 70-140), closest pair {F(minPair, "0")} m outside merge zones (want 40+)");
        }

        private static bool NearJunction(AutoPlanResult r, int a, int b, float2 s)
        {
            foreach (AutoJunction j in r.Junctions)
            {
                if (j.Existing || !((j.RunA == a && j.RunB == b) || (j.RunA == b && j.RunB == a)))
                    continue;
                float2 na = r.Runs[j.RunA].Ring[j.NodeA], nb = r.Runs[j.RunB].Ring[j.NodeB];
                if (math.distance(s, na) <= 60f || math.distance(s, nb) <= 60f)
                    return true;
            }
            return false;
        }

        /// <summary>F-6: run area over pod area: median 13-28 %; no pod above its developable cap (DevelopableShare x SkiableAreaM2).</summary>
        public static Gate F6PodShare(AutoPlanResult r, ResortPlan p)
        {
            var shares = new List<double>();
            int over = 0;
            foreach (PlanPod pod in p.Pods)
            {
                if (pod.RunCount <= 0)
                    continue;
                double area = 0.0;
                for (int k = 0; k < pod.RunCount; k++)
                    area += r.Runs[p.PodRuns[pod.RunFirst + k]].AreaM2;
                if (pod.AreaM2 > 0f)
                    shares.Add(area / pod.AreaM2);
                if (area > pod.DevelopableShare * pod.SkiableAreaM2 + 1.0)
                    over++;
            }
            if (shares.Count == 0)
                return NA("F-6", "no pod with runs");
            double med = Median(shares);
            return G("F-6", med >= 0.13 && med <= 0.28 && over == 0, med, $"median run share of pod area {F(med * 100, "0")} % (want 13-28), {over} pods over their developable cap (want 0)");
        }

        /// <summary>F-7: run km per km2 of the hull of runs + lifts, grown 50 m, inside the area band (1 km2 4.9-8.9, 4 km2 3.4-6.7,
        /// 9 km2 2.7-5.6, steep-compact 3.0-4.9, Dense up to 8).</summary>
        public static Gate F7Density(AutoPlanResult r, ResortPlan p, float areaKm2, bool steepCompact, AutoDensity density)
        {
            if (r.Runs.Count == 0)
                return NA("F-7", "no runs");
            double km = 0.0;
            var pts = new List<float2>();
            foreach (AutoRun run in r.Runs)
            {
                km += run.LengthM / 1000.0;
                pts.AddRange(run.Ring);
            }
            foreach (PlanLift l in p.Lifts)
            {
                pts.Add(l.Bottom.xz);
                pts.Add(l.Top.xz);
            }
            double hull = HullKm2(pts, 50.0);
            double d = km / Math.Max(1e-6, hull);
            double lo, hi;
            if (steepCompact)
            {
                lo = 3.0;
                hi = 4.9;
            }
            else if (areaKm2 < 2.5f)
            {
                lo = 4.9;
                hi = 8.9;
            }
            else if (areaKm2 < 6.5f)
            {
                lo = 3.4;
                hi = 6.7;
            }
            else
            {
                lo = 2.7;
                hi = 5.6;
            }
            if (density == AutoDensity.Dense)
                hi = Math.Max(hi, 8.0);
            return G("F-7", d >= lo && d <= hi, d, $"{F(d)} run km per km2 of hull ({F(km)} km over {F(hull, "0.###")} km2; want {lo}-{hi})");
        }

        /// <summary>F-8 (SOFT): per rated tier the median length and drop inside the connected-trail p25-p75 envelope.</summary>
        public static Gate F8RunGeometry(AutoPlanResult r)
        {
            // green 177-820 m / 17-106 m, blue 250-949 / 38-177, black 291-768 / 92-242
            float[,] env = { { 177f, 820f, 17f, 106f }, { 250f, 949f, 38f, 177f }, { 291f, 768f, 92f, 242f } };
            int outside = 0, tiers = 0;
            string text = "";
            for (int t = 0; t < 3; t++)
            {
                var len = new List<double>();
                var drop = new List<double>();
                foreach (AutoRun run in r.Runs)
                    if ((int)run.Band == t + 1)
                    {
                        len.Add(run.LengthM);
                        drop.Add(run.DropM);
                    }
                if (len.Count == 0)
                    continue;
                tiers++;
                double ml = Median(len), md = Median(drop);
                bool ok = ml >= env[t, 0] && ml <= env[t, 1] && md >= env[t, 2] && md <= env[t, 3];
                if (!ok)
                    outside++;
                text += $" {(SlopeBand)(t + 1)} {F(ml, "0")} m / {F(md, "0")} m{(ok ? "" : " MISS")};";
            }
            return tiers == 0 ? NA("F-8", "no green, blue or black runs") : G("F-8", outside == 0, outside, $"median length / drop per tier:{text} (want inside the p25-p75 envelopes)");
        }

        /// <summary>F-9: kept share of candidates over 0.40, and TopTaken + TooShort under half of all rejects.</summary>
        public static Gate F9Collapse(AutoPlanResult r)
        {
            if (r.Candidates <= 0)
                return NA("F-9", "no candidates");
            double kept = (double)r.Runs.Count / r.Candidates;
            int all = 0;
            for (int i = 1; i < r.Rejects.Length; i++)
                all += r.Rejects[i];
            int tt = r.Rejects[(int)AutoReject.TopTaken] + r.Rejects[(int)AutoReject.TooShort];
            return G("F-9", kept > 0.40 && (all == 0 || tt * 2 < all), kept, $"kept {r.Runs.Count} of {r.Candidates} candidates = {F(kept * 100, "0")} % (want over 40), TopTaken + TooShort {tt} of {all} rejects (want under half)");
        }

        /// <summary>F-10: the reported MountainMix equals the re-rated runs' run-km shares by band.</summary>
        public static Gate F10MixHonest(AutoPlanResult r, ResortPlan p, Func<AutoRun, SlopeBand> rate)
        {
            var km = new double[5];
            double total = 0.0;
            foreach (AutoRun run in r.Runs)
            {
                km[(int)(rate != null ? rate(run) : run.Band)] += run.LengthM;
                total += run.LengthM;
            }
            double worst = 0.0;
            for (int b = 0; b < 5; b++)
                worst = Math.Max(worst, Math.Abs((total > 0.0 ? km[b] / total : 0.0) - p.MountainMix[b]));
            return G("F-10", worst <= 0.002, worst, $"largest MountainMix difference to the re-rated run-km shares {F(worst, "0.####")} (want 0.002 or less)");
        }

        // ---------------------------------------------------------------- 2.4 flow and verdicts

        /// <summary>H-1: every lift has a verdict (Home != None); lifts for which skip returns true (pinned, outside the region) are left out.</summary>
        public static Gate H1Verdicts(ResortPlan p, Func<PlanLift, bool> skip = null)
        {
            if (p.Lifts.Length == 0)
                return NA("H-1", "no lifts");
            int none = 0, n = 0;
            foreach (PlanLift l in p.Lifts)
            {
                if (skip != null && skip(l))
                    continue;
                n++;
                if (l.Home == HomeVerdict.None)
                    none++;
            }
            return G("H-1", none == 0, none, $"{none} of {n} lifts without a way-home verdict (want 0)");
        }

        /// <summary>The easiest band (1 Green .. 4 DoubleBlack, 0 = Flat counts as easy) of a downhill path of runs from the lift's Top to
        /// a base pad, the lift's own bottom pad or its bottom spot; 99 = no path. Start runs: tops within startM of Top; a run
        /// continues into another when its bottom is inside that run's outline or within 25 m of it.</summary>
        public static int HostHomeBand(AutoPlanResult r, ResortPlan p, int lift, int[] band, float startM = NearM)
        {
            for (int level = 0; level <= 4; level++)
                if (HostReaches(r, p, lift, band, level, startM))
                    return Math.Max(level, 1);
            return 99;
        }

        private static bool HostReaches(AutoPlanResult r, ResortPlan p, int lift, int[] band, int maxBand, float startM)
        {
            PlanLift l = p.Lifts[lift];
            var seen = new bool[r.Runs.Count];
            var queue = new Queue<int>();
            for (int i = 0; i < r.Runs.Count; i++)
                if (band[i] <= maxBand && math.distance(Top(r.Runs[i]), l.Top.xz) <= startM)
                {
                    seen[i] = true;
                    queue.Enqueue(i);
                }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                float2 bottom = Bottom(r.Runs[i]);
                if (AtTarget(p, l, bottom))
                    return true;
                for (int j = 0; j < r.Runs.Count; j++)
                    if (!seen[j] && band[j] <= maxBand && OnRun(r.Runs[j], bottom))
                    {
                        seen[j] = true;
                        queue.Enqueue(j);
                    }
            }
            return false;
        }

        private static bool AtTarget(ResortPlan p, PlanLift l, float2 bottom)
        {
            if (math.distance(bottom, l.Bottom.xz) <= LinkM)
                return true;
            for (int k = 0; k < p.Pads.Length; k++)
                if ((p.Pads[k].IsBase || k == l.BottomPad) && InPad(p.Pads[k], bottom))
                    return true;
            return false;
        }

        private static bool InPad(PlanPad pad, float2 pt) =>
            math.distance(pt, pad.Center) <= Math.Max(pad.InscribedM, (float)Math.Sqrt(pad.AreaM2 / Math.PI)) + LinkM;

        private static int[] Bands(AutoPlanResult r, Func<AutoRun, SlopeBand> rate)
        {
            var b = new int[r.Runs.Count];
            for (int i = 0; i < b.Length; i++)
                b[i] = (int)(rate != null ? rate(r.Runs[i]) : r.Runs[i].Band);
            return b;
        }

        /// <summary>H-2: Home = Green / Blue only if the host finds a downhill path of runs (re-rated, links within 25 m) of that band or
        /// easier from Top to a base pad or the lift's own bottom; Black / DoubleBlack likewise; Download only when no path at the
        /// wanted level exists (Wanted Green, Blue, or for Auto: Green when the bottom pad is a base pad, else Blue).</summary>
        public static Gate H2Honesty(AutoPlanResult r, ResortPlan p, Func<AutoRun, SlopeBand> rate)
        {
            if (p.Lifts.Length == 0)
                return NA("H-2", "no lifts");
            int[] band = Bands(r, rate);
            int bad = 0;
            string first = "";
            for (int i = 0; i < p.Lifts.Length; i++)
            {
                PlanLift l = p.Lifts[i];
                if (l.Home == HomeVerdict.None)
                    continue;   // H-1's job
                int host = HostHomeBand(r, p, i, band);
                int wanted = l.Wanted == HomeLevel.Green ? 1 : l.Wanted == HomeLevel.Blue ? 2 :
                             l.BottomPad >= 0 && l.BottomPad < p.Pads.Length && p.Pads[l.BottomPad].IsBase ? 1 : 2;
                bool ok;
                if (l.Home == HomeVerdict.Download)
                    ok = host > wanted;
                else
                    ok = host <= (int)l.Home;   // HomeVerdict Green..DoubleBlack = 1..4 = SlopeBand Green..DoubleBlack
                if (!ok && bad++ == 0)
                    first = $" (first: lift {l.Id} says {l.Home}, host easiest path band {(host == 99 ? "none" : ((SlopeBand)host).ToString())}, wanted band {wanted})";
            }
            return G("H-2", bad == 0, bad, $"{bad} of {p.Lifts.Length} verdicts the host cannot reproduce{first}");
        }

        /// <summary>H-3: no Green verdict on a pod whose MeanFanGrade is over 0.55.</summary>
        public static Gate H3NoFakeGreen(ResortPlan p)
        {
            if (p.Lifts.Length == 0)
                return NA("H-3", "no lifts");
            int bad = 0;
            foreach (PlanLift l in p.Lifts)
                if (l.Home == HomeVerdict.Green && l.Pod >= 0 && l.Pod < p.Pods.Length && p.Pods[l.Pod].MeanFanGrade > 0.55f)
                    bad++;
            return G("H-3", bad == 0, bad, $"{bad} green verdicts on pods steeper than 55 % mean fan grade (want 0)");
        }

        /// <summary>H-4: dead ends over run bottoms under 5 % (target 0).</summary>
        public static Gate H4DeadEnds(AutoPlanResult r, ResortPlan p)
        {
            double share = r.Runs.Count == 0 ? 0.0 : (double)p.DeadEnds / r.Runs.Count;
            return G("H-4", share < 0.05, p.DeadEnds, $"{p.DeadEnds} dead ends of {r.Runs.Count} run bottoms = {F(share * 100)} % (want under 5, target 0)");
        }

        /// <summary>H-5 (SOFT for pinned-only plans): every pod with runs is reachable from a base pad by lifts plus downhill runs.</summary>
        public static Gate H5Reachability(AutoPlanResult r, ResortPlan p)
        {
            int[] band = Bands(r, null);
            var padOk = new bool[p.Pads.Length];
            for (int i = 0; i < p.Pads.Length; i++)
                padOk[i] = p.Pads[i].IsBase;
            var liftOk = new bool[p.Lifts.Length];
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < p.Lifts.Length; i++)
                {
                    if (liftOk[i])
                        continue;
                    PlanLift l = p.Lifts[i];
                    bool at = l.BottomPad >= 0 && l.BottomPad < padOk.Length && padOk[l.BottomPad];
                    for (int k = 0; k < padOk.Length && !at; k++)
                        at = padOk[k] && InPad(p.Pads[k], l.Bottom.xz);
                    if (!at)
                        continue;
                    liftOk[i] = grew = true;
                    // every run chain from this Top: mark the pads its run bottoms reach
                    var seen = new bool[r.Runs.Count];
                    var queue = new Queue<int>();
                    for (int j = 0; j < r.Runs.Count; j++)
                        if (math.distance(Top(r.Runs[j]), l.Top.xz) <= NearM)
                        {
                            seen[j] = true;
                            queue.Enqueue(j);
                        }
                    while (queue.Count > 0)
                    {
                        int j = queue.Dequeue();
                        float2 bottom = Bottom(r.Runs[j]);
                        for (int k = 0; k < padOk.Length; k++)
                            if (!padOk[k] && InPad(p.Pads[k], bottom))
                                padOk[k] = true;
                        for (int m = 0; m < r.Runs.Count; m++)
                            if (!seen[m] && OnRun(r.Runs[m], bottom))
                            {
                                seen[m] = true;
                                queue.Enqueue(m);
                            }
                    }
                }
            }
            int pods = 0, bad = 0;
            foreach (PlanPod pod in p.Pods)
            {
                if (pod.RunCount <= 0)
                    continue;
                pods++;
                if (pod.Lift < 0 || pod.Lift >= liftOk.Length || !liftOk[pod.Lift])
                    bad++;
            }
            return pods == 0 ? NA("H-5", "no pod with runs") : G("H-5", bad == 0, bad, $"{bad} of {pods} pods with runs not reachable from a base pad (want 0)");
        }

        /// <summary>H-6 (REPORT): the share of pods with a Green way home against the [G] share for their fan steepness
        /// (48 / 29 / 16 / 10 % at 20 / 30 / 40 / 50 % mean fan grade, interpolated).</summary>
        public static Gate H6GreenShare(ResortPlan p)
        {
            if (p.Pods.Length == 0)
                return NA("H-6", "no pods");
            int green = 0;
            double expected = 0.0;
            foreach (PlanPod pod in p.Pods)
            {
                if (pod.Lift >= 0 && pod.Lift < p.Lifts.Length && p.Lifts[pod.Lift].Home == HomeVerdict.Green)
                    green++;
                expected += ExpectedGreenShare(pod.MeanFanGrade);
            }
            double obs = (double)green / p.Pods.Length, exp = expected / p.Pods.Length;
            return G("H-6", true, obs, $"{green} of {p.Pods.Length} pods with a green way home = {F(obs * 100, "0")} % (real pods of this steepness: {F(exp * 100, "0")} %)");
        }

        public static double ExpectedGreenShare(float fanGrade)
        {
            float[] g = { 0.20f, 0.30f, 0.40f, 0.50f };
            double[] s = { 0.48, 0.29, 0.16, 0.10 };
            if (fanGrade <= g[0])
                return s[0];
            for (int i = 1; i < g.Length; i++)
                if (fanGrade <= g[i])
                    return s[i - 1] + (s[i] - s[i - 1]) * (fanGrade - g[i - 1]) / (g[i] - g[i - 1]);
            return s[3];
        }

        // ---------------------------------------------------------------- 2.5 time, memory, determinism

        /// <summary>P-1: the longest planner unit under 4 ms (CoreCLR Release).</summary>
        public static Gate P1LongestUnit(AutoPlanResult r) => P1LongestUnit(r.MaxUnitMs);

        public static Gate P1LongestUnit(double ms) => G("P-1", ms < 4.0, ms, $"longest unit {F(ms)} ms (want under 4)");

        /// <summary>P-2 (about 4 km2: under 0.8 s) / P-3 (about 9 km2: under 1.5 s): the whole plan on one thread.</summary>
        public static Gate PWorkerTime(float areaKm2, double seconds)
        {
            bool big = areaKm2 >= 6.5f;
            double lim = big ? 1.5 : 0.8;
            return G(big ? "P-3" : "P-2", seconds < lim, seconds, $"plan of {F(areaKm2)} km2 took {F(seconds, "0.00")} s (want under {lim})");
        }

        /// <summary>P-4: planner buffers under 80 MB (the host sees the Resort layers exactly; the v1 layers are estimated by the caller).</summary>
        public static Gate P4Memory(long bytes, string what) => G("P-4", bytes < 80L * 1000 * 1000, bytes / 1.0e6, $"{F(bytes / 1.0e6, "0.0")} MB {what} (want under 80)");

        public static Gate DEqual(string id, string what, ulong want, ulong got) =>
            G(id, want == got, got == want ? 0 : 1, got == want ? what + " equal" : $"{what}: fingerprint {got:X16} differs from {want:X16}");

        // ---------------------------------------------------------------- helpers

        public static double Median(List<double> v)
        {
            if (v.Count == 0)
                return double.NaN;
            var s = new List<double>(v);
            s.Sort();
            int n = s.Count;
            return n % 2 == 1 ? s[n / 2] : 0.5 * (s[n / 2 - 1] + s[n / 2]);
        }

        public static float2[] Resample(float2[] pl, float step)
        {
            var o = new List<float2> { pl[0] };
            float carry = 0f;
            for (int i = 1; i < pl.Length; i++)
            {
                float len = math.distance(pl[i - 1], pl[i]);
                float pos = step - carry;
                while (pos <= len)
                {
                    o.Add(math.lerp(pl[i - 1], pl[i], pos / len));
                    pos += step;
                }
                carry = len - (pos - step);
            }
            if (math.distance(o[o.Count - 1], pl[pl.Length - 1]) > 1e-3f)
                o.Add(pl[pl.Length - 1]);
            return o.ToArray();
        }

        public static double PolylineDistance(float2 p, float2[] pl)
        {
            double best = double.PositiveInfinity;
            for (int i = 1; i < pl.Length; i++)
            {
                float2 a = pl[i - 1], b = pl[i], ab = b - a;
                float l2 = math.dot(ab, ab);
                float u = l2 > 0f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
                best = Math.Min(best, math.distance(p, a + ab * u));
            }
            return best;
        }

        /// <summary>Area (km2) of the convex hull of the points grown by grow meters (Steiner: area + perimeter x grow + pi x grow^2).</summary>
        public static double HullKm2(List<float2> pts, double grow)
        {
            var p = new List<float2>(pts);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (p.Count < 3)
                return Math.PI * grow * grow / 1.0e6;
            var h = new List<float2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    int k = pass == 0 ? i : p.Count - 1 - i;
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], p[k]) <= 0f)
                        h.RemoveAt(h.Count - 1);
                    h.Add(p[k]);
                }
                h.RemoveAt(h.Count - 1);
            }
            double area = 0.0, perim = 0.0;
            for (int i = 0, j = h.Count - 1; i < h.Count; j = i++)
            {
                area += (double)h[j].x * h[i].y - (double)h[i].x * h[j].y;
                perim += math.distance(h[j], h[i]);
            }
            return (Math.Abs(area) * 0.5 + perim * grow + Math.PI * grow * grow) / 1.0e6;
        }

        private static float Cross(float2 o, float2 a, float2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    // ---------------------------------------------------------------- suite V2Metrics: hand-made plans with known answers

    internal static partial class Program
    {
        private static void Gate_(Gate g, bool wantPass, string name)
        {
            Check(g.Pass == wantPass, "V2Metrics " + name, $"{g} (want pass {wantPass})");
        }

        /// <summary>A straight run top -> bottom, outline 'width' wide (a rectangle), stored band, drop from the heights.</summary>
        private static AutoRun HRun(float2 top, float2 bottom, SlopeBand band = SlopeBand.Green, float topY = 1500f, float botY = 1300f, float width = 30f)
        {
            float2 d = math.normalize(bottom - top), n = new float2(-d.y, d.x) * (width * 0.5f);
            float len = math.distance(top, bottom);
            return new AutoRun
            {
                Band = band, PassBand = band, Centerline = new[] { top, bottom }, Ring = new[] { top + n, bottom + n, bottom - n, top - n },
                LengthM = len, DropM = topY - botY, TopY = topY, BottomY = botY, AreaM2 = len * width,
            };
        }

        private static PlanPad HPad(float area, float grade, float inscribed, float2 center, bool isBase = false, bool isBench = false) =>
            new PlanPad { AreaM2 = area, MeanGrade = grade, InscribedM = inscribed, Center = center, IsBase = isBase, IsBench = isBench };

        private static PlanLift HLift(byte type, float2 bottom, float2 top, float rise, float slope, int pod = 0, int bottomPad = 0, int topPad = -1,
            HomeVerdict home = HomeVerdict.Green, LiftOrigin origin = LiftOrigin.Proposed, uint key = 0) =>
            new PlanLift
            {
                TypeIndex = type, Bottom = new float3(bottom.x, 1000f, bottom.y), Top = new float3(top.x, 1000f + rise, top.y), RiseM = rise,
                LengthM = math.sqrt(math.distancesq(bottom, top) + rise * rise), MaxEdgeSlope = slope, Pod = pod, BottomPad = bottomPad, TopPad = topPad,
                Home = home, Origin = origin, Key = key,
            };

        private static void V2MetricsTests()
        {
            // ---- S-1 pads: a 0.15 ha pad and a 30 % pad break it; a too-small station spot breaks it unless sunk
            var plan = new ResortPlan { Pads = new[] { HPad(5000f, 0.10f, 30f, float2.zero, true), HPad(3000f, 0.20f, 10f, new float2(500f, 0f), false, true) } };
            Gate_(V2Metrics.S1Pads(plan), true, "S-1 clean pads");
            plan.Pads[1].AreaM2 = 1500f;
            Gate_(V2Metrics.S1Pads(plan), false, "S-1 small pad");
            plan.Pads[1].AreaM2 = 3000f;
            plan.Pads[1].MeanGrade = 0.30f;
            Gate_(V2Metrics.S1Pads(plan), false, "S-1 steep pad");
            plan.Pads[1].MeanGrade = 0.20f;
            plan.Lifts = new[] { HLift(1, float2.zero, new float2(700f, 0f), 300f, 0.5f, 0, 0, 1) };   // top pad inscribed 10 < 17.9
            Gate_(V2Metrics.S1Pads(plan), false, "S-1 station spot too small");
            plan.Lifts[0].TopSunk = true;
            Gate_(V2Metrics.S1Pads(plan), true, "S-1 sunk station spot");
            plan.Lifts[0].TopPad = -1;
            Gate_(V2Metrics.S1Pads(plan), true, "S-1 summit top is sunk");
            plan.Lifts[0].TopSunk = false;
            Gate_(V2Metrics.S1Pads(plan), false, "S-1 summit top not sunk");
            Gate_(V2Metrics.S1Pads(new ResortPlan()), false, "S-1 empty plan is not a pass");

            // ---- S-2 pod class: shares 0.5 / 0.3 / 0.15 / 0.05 over Beginner..Expert: the 80th percentile is Intermediate
            float[] share = { 0f, 0f, 0.5f, 0.3f, 0.15f, 0.05f, 0f };
            Check(V2Metrics.PercentileBand(share, 0.8f) == TerrainBand.Intermediate, "V2Metrics S-2", "80th percentile band of 0.5 / 0.3 / 0.15 / 0.05 is not Intermediate");
            Check(V2Metrics.PercentileBand(new float[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f }, 0.8f) == TerrainBand.Cliff, "V2Metrics S-2", "all-cliff shares");
            plan.Pods = new[] { new PlanPod { BandShare = share, PrimaryClass = TerrainBand.Intermediate } };
            Gate_(V2Metrics.S2PodClass(plan), true, "S-2 class equals the percentile band");
            plan.Pods[0].PrimaryClass = TerrainBand.Advanced;
            Gate_(V2Metrics.S2PodClass(plan), false, "S-2 wrong class");
            plan.Pods[0].BandShare = null;
            Gate_(V2Metrics.S2PodClass(plan), false, "S-2 missing shares");

            // ---- S-3 pods per 4 km2 and the bench pod
            plan.Pods = new[] { new PlanPod { LowerPad = 0 }, new PlanPod { LowerPad = 1 } };
            Gate_(V2Metrics.S3Pods(plan, 4f, true), true, "S-3 two pods, bench pod");
            Gate_(V2Metrics.S3Pods(plan, 6f, false), false, "S-3 two pods over 6 km2 is 1.33 per 4 km2");
            plan.Pads[1].IsBench = false;
            Gate_(V2Metrics.S3Pods(plan, 4f, true), false, "S-3 no bench pod");
            Gate_(V2Metrics.S3Pods(plan, 4f, false), true, "S-3 bench not required");

            // ---- S-4 lift norms: detachable 600-2200 m, rise 95-512 m, edge slope under 0.97 x 0.9 = 0.873; pinned lifts are skipped
            var lifts = new[] { HLift(1, float2.zero, new float2(1000f, 0f), 300f, 0.5f) };
            plan.Lifts = lifts;
            Gate_(V2Metrics.S4Lifts(plan), true, "S-4 detachable in norms");
            plan.Lifts = new[] { HLift(1, float2.zero, new float2(400f, 0f), 300f, 0.5f) };
            Gate_(V2Metrics.S4Lifts(plan), false, "S-4 too short");
            plan.Lifts = new[] { HLift(1, float2.zero, new float2(1000f, 0f), 600f, 0.5f) };
            Gate_(V2Metrics.S4Lifts(plan), false, "S-4 rise over p90");
            plan.Lifts = new[] { HLift(1, float2.zero, new float2(1000f, 0f), 300f, 0.88f) };
            Gate_(V2Metrics.S4Lifts(plan), false, "S-4 edge slope over 0.97 x cap");
            plan.Lifts = new[] { HLift(1, float2.zero, new float2(10f, 0f), 5f, 3f, origin: LiftOrigin.Pinned) };
            Gate_(V2Metrics.S4Lifts(plan), false, "S-4 only pinned lifts = nothing to measure");

            // ---- S-5 lift count by area band
            LiftsOf(plan, 3);
            Gate_(V2Metrics.S5LiftCount(plan, 4f, false, 0), true, "S-5 3 lifts at 4 km2");
            LiftsOf(plan, 5);
            Gate_(V2Metrics.S5LiftCount(plan, 4f, false, 0), false, "S-5 5 lifts at 4 km2");
            Gate_(V2Metrics.S5LiftCount(plan, 9f, false, 0), true, "S-5 5 lifts at 9 km2");
            LiftsOf(plan, 2);
            Gate_(V2Metrics.S5LiftCount(plan, 9f, false, 0), false, "S-5 2 lifts at 9 km2");
            Gate_(V2Metrics.S5LiftCount(plan, 4f, true, 0), false, "S-5 steep-compact wants 3-6");
            Gate_(V2Metrics.S5LiftCount(plan, 4f, false, 2), true, "S-5 knob 2 with 2 lifts");
            Gate_(V2Metrics.S5LiftCount(plan, 4f, false, 1), false, "S-5 knob 1 with 2 lifts");
            LiftsOf(plan, 0);
            Gate_(V2Metrics.S5LiftCount(plan, 1f, false, 0), false, "S-5 Auto needs at least one lift");

            // ---- S-6 bench-bottom lift and the median bottom height
            plan.Pads[1].IsBench = true;
            plan.Lifts = new[] { HLift(1, float2.zero, new float2(1000f, 0f), 300f, 0.5f, 0, 1) };
            Gate_(V2Metrics.S6Stacking(plan, true), true, "S-6 bench-bottom lift");
            plan.Lifts[0].BottomPad = 0;
            Gate_(V2Metrics.S6Stacking(plan, true), false, "S-6 all bottoms on the base pad");
            Gate_(V2Metrics.S6Stacking(plan, false), true, "S-6 not required");
            plan.Pods = new[] { new PlanPod { BottomY = 1000f, TopY = 2000f } };
            plan.Lifts[0].Bottom = new float3(0f, 1300f, 0f);
            Gate_(V2Metrics.S6BottomHeight(plan), true, "S-6 bottom at 0.30 of the vertical");
            plan.Lifts[0].Bottom = new float3(0f, 1020f, 0f);
            Gate_(V2Metrics.S6BottomHeight(plan), false, "S-6 bottom at 0.02 of the vertical");

            // ---- S-7 pinned lifts honored
            var pin = new List<PinnedLift> { new PinnedLift { Key = 77u } };
            plan.Lifts = new[] { HLift(0, float2.zero, new float2(500f, 0f), 200f, 0.5f, 0, origin: LiftOrigin.Pinned, key: 77u), HLift(1, float2.zero, new float2(900f, 0f), 300f, 0.5f, 1) };
            Gate_(V2Metrics.S7Pinned(plan, pin), true, "S-7 pinned kept, proposal on another pod");
            plan.Lifts[1].Pod = 0;
            Gate_(V2Metrics.S7Pinned(plan, pin), false, "S-7 proposal shares the pinned lift's pod");
            plan.Lifts = new[] { HLift(1, float2.zero, new float2(900f, 0f), 300f, 0.5f, 1) };
            Gate_(V2Metrics.S7Pinned(plan, pin), false, "S-7 pinned lift missing");
            Gate_(V2Metrics.S7Pinned(plan, null), false, "S-7 nothing pinned = nothing to measure");

            FanMetricTests();
            FlowMetricTests();

            // ---- P / D
            var pr = new AutoPlanResult { MaxUnitMs = 3.9f };
            Gate_(V2Metrics.P1LongestUnit(pr), true, "P-1 3.9 ms");
            pr.MaxUnitMs = 4.2f;
            Gate_(V2Metrics.P1LongestUnit(pr), false, "P-1 4.2 ms");
            Gate g = V2Metrics.PWorkerTime(4f, 0.5);
            Check(g.Id == "P-2" && g.Pass, "V2Metrics P-2", g.ToString());
            g = V2Metrics.PWorkerTime(9f, 1.6);
            Check(g.Id == "P-3" && !g.Pass, "V2Metrics P-3", g.ToString());
            Gate_(V2Metrics.P4Memory(79_000_000L, "layers"), true, "P-4 79 MB");
            Gate_(V2Metrics.P4Memory(81_000_000L, "layers"), false, "P-4 81 MB");
            Gate_(V2Metrics.DEqual("D-1", "rerun", 5UL, 5UL), true, "D-1 equal");
            Gate_(V2Metrics.DEqual("D-1", "rerun", 5UL, 6UL), false, "D-1 differs");

            // ---- helpers with known answers
            Check(Math.Abs(V2Metrics.Median(new List<double> { 3, 1, 2 }) - 2) < 1e-9 && Math.Abs(V2Metrics.Median(new List<double> { 4, 1, 2, 3 }) - 2.5) < 1e-9,
                "V2Metrics median", "median of 3 and of 4 values");
            Check(V2Metrics.Resample(new[] { new float2(0, 0), new float2(100, 0) }, 25f).Length == 5, "V2Metrics resample", "100 m at 25 m is 5 points");
            Check(Math.Abs(V2Metrics.PolylineDistance(new float2(50, 30), new[] { new float2(0, 0), new float2(100, 0) }) - 30) < 1e-4 &&
                  Math.Abs(V2Metrics.PolylineDistance(new float2(-30, 40), new[] { new float2(0, 0), new float2(100, 0) }) - 50) < 1e-4, "V2Metrics polyline distance", "perpendicular / endpoint distance");
            double hull = V2Metrics.HullKm2(new List<float2> { new float2(0, 0), new float2(1000, 0), new float2(1000, 30), new float2(0, 30), new float2(500, 15) }, 50.0);
            Check(Math.Abs(hull - (30000.0 + 2060.0 * 50.0 + Math.PI * 2500.0) / 1.0e6) < 1e-6, "V2Metrics hull", $"hull of a 1000 x 30 m rectangle grown 50 m = {hull:F6} km2");
            Check(Math.Abs(V2Metrics.ExpectedGreenShare(0.35f) - 0.225) < 1e-6 && V2Metrics.ExpectedGreenShare(0.1f) == 0.48 && V2Metrics.ExpectedGreenShare(0.9f) == 0.10, "V2Metrics H-6",
                "interpolated [G] green share");
        }

        private static void LiftsOf(ResortPlan plan, int n)
        {
            plan.Lifts = new PlanLift[n];
            for (int i = 0; i < n; i++)
                plan.Lifts[i] = HLift(1, float2.zero, new float2(1000f, 0f), 300f, 0.5f, i);
        }

        private static void FanMetricTests()
        {
            // One lift: Top (700, 0), Bottom (0, 0). Runs 0 and 1 start at the top and end at the bottom, run 2 starts and ends far away.
            var r = new AutoPlanResult();
            r.Runs.Add(HRun(new float2(690, -50), new float2(20, -50)));
            r.Runs.Add(HRun(new float2(690, 50), new float2(20, 50)));
            r.Runs.Add(HRun(new float2(500, 150), new float2(150, 150)));
            var plan = new ResortPlan
            {
                Lifts = new[] { HLift(1, new float2(0, 0), new float2(700, 0), 300f, 0.5f) },
                RunLift = new[] { 0, 0, 0 }, RunPod = new[] { 0, 0, 0 },
            };
            Gate_(V2Metrics.F1TrailsPerLift(r, plan, AutoDensity.Normal), false, "F-1 three runs, median 3 under 5");
            Gate_(V2Metrics.F1TrailsPerLift(r, plan, AutoDensity.Sparse), false, "F-1 Sparse wants 4-6");
            for (int i = 0; i < 3; i++)
                r.Runs.Add(HRun(new float2(500, -150 - 50 * i), new float2(150, -150 - 50 * i)));
            plan.RunLift = new[] { 0, 0, 0, 0, 0, 0 };
            plan.RunPod = new[] { 0, 0, 0, 0, 0, 0 };
            Gate_(V2Metrics.F1TrailsPerLift(r, plan, AutoDensity.Normal), true, "F-1 six runs");
            Gate_(V2Metrics.F1TrailsPerLift(r, plan, AutoDensity.Dense), false, "F-1 Dense wants 9-12");
            Gate_(V2Metrics.F2StartsNearTop(r, plan), true, "F-2 two tops near the lift top");
            Gate_(V2Metrics.F3EndsAtBottom(r, plan), true, "F-3 two bottoms at the lift bottom");
            plan.Lifts[0].Top = new float3(-3000f, 1300f, 0f);
            Gate_(V2Metrics.F2StartsNearTop(r, plan), false, "F-2 no top near");
            plan.Lifts[0].Top = new float3(700f, 1300f, 0f);
            plan.Lifts[0].Bottom = new float3(3000f, 1000f, 0f);
            Gate_(V2Metrics.F3EndsAtBottom(r, plan), false, "F-3 no bottom near");

            // F-4: a run top 10 m beside another run's outline counts as branching, tops in empty ground do not
            var b = new AutoPlanResult();
            b.Runs.Add(HRun(new float2(600, 0), new float2(100, 0)));
            b.Runs.Add(HRun(new float2(400, 25), new float2(100, 120)));   // top 10 m off run 0's outline (|z| = 15)
            Gate_(V2Metrics.F4Branching(b), true, "F-4 one of two tops on a run = 50 %");
            b.Runs[1] = HRun(new float2(400, 500), new float2(100, 520));
            Gate_(V2Metrics.F4Branching(b), false, "F-4 no branching");

            // F-5: parallel runs 100 m apart
            var sp = new AutoPlanResult();
            for (int i = 0; i < 3; i++)
                sp.Runs.Add(HRun(new float2(600, -100 + 100 * i), new float2(100, -100 + 100 * i)));
            var spPlan = new ResortPlan { RunPod = new[] { 0, 0, 0 } };
            Gate_(V2Metrics.F5Spacing(sp, spPlan), true, "F-5 100 m apart");
            for (int i = 0; i < 3; i++)
                sp.Runs[i] = HRun(new float2(600, 30 * i), new float2(100, 30 * i));
            Gate_(V2Metrics.F5Spacing(sp, spPlan), false, "F-5 30 m apart");
            for (int i = 0; i < 3; i++)
                sp.Runs[i] = HRun(new float2(600, 130 * i), new float2(100, 130 * i));
            Gate_(V2Metrics.F5Spacing(sp, spPlan), true, "F-5 130 m apart");
            spPlan.RunPod = new[] { 0, 1, 2 };
            Gate_(V2Metrics.F5Spacing(sp, spPlan), false, "F-5 one run per pod = nothing to measure");

            // F-6: three 500 x 30 m runs = 45,000 m2 in a pod of 225,000 m2 = 20 %; over a 100,000 m2 pod = 45 %
            var sh = new AutoPlanResult();
            for (int i = 0; i < 3; i++)
                sh.Runs.Add(HRun(new float2(600, 100 * i), new float2(100, 100 * i)));
            var shPlan = new ResortPlan
            {
                Pods = new[] { new PlanPod { AreaM2 = 225000f, SkiableAreaM2 = 200000f, DevelopableShare = 0.5f, RunFirst = 0, RunCount = 3 } },
                PodRuns = new[] { 0, 1, 2 },
            };
            Gate_(V2Metrics.F6PodShare(sh, shPlan), true, "F-6 20 % of the pod");
            shPlan.Pods[0].AreaM2 = 100000f;
            Gate_(V2Metrics.F6PodShare(sh, shPlan), false, "F-6 45 % of the pod");
            shPlan.Pods[0].AreaM2 = 225000f;
            shPlan.Pods[0].DevelopableShare = 0.2f;   // cap 40,000 m2 < 45,000
            Gate_(V2Metrics.F6PodShare(sh, shPlan), false, "F-6 over the developable cap");

            // F-7: one 1000 m run, hull of its 1000 x 30 m outline grown 50 m = 0.14085 km2 -> 7.10 km per km2
            var one = new AutoPlanResult();
            one.Runs.Add(HRun(new float2(1100, 0), new float2(100, 0), SlopeBand.Blue));
            var onePlan = new ResortPlan();
            Gate g = V2Metrics.F7Density(one, onePlan, 1f, false, AutoDensity.Normal);
            Check(Math.Abs(g.Value - 1.0 / ((30000.0 + 2060.0 * 50.0 + Math.PI * 2500.0) / 1.0e6)) < 1e-3 && g.Pass, "V2Metrics F-7", g.ToString());
            Gate_(V2Metrics.F7Density(one, onePlan, 4f, false, AutoDensity.Normal), false, "F-7 7.1 km per km2 at 4 km2 is over 6.7");
            Gate_(V2Metrics.F7Density(one, onePlan, 4f, false, AutoDensity.Dense), true, "F-7 Dense may reach 8");

            // F-8: green 400 m / 50 m drop is inside the envelope; a 1000 m black is not
            var geo = new AutoPlanResult();
            geo.Runs.Add(HRun(new float2(500, 0), new float2(100, 0), SlopeBand.Green, 1050f, 1000f));
            Gate_(V2Metrics.F8RunGeometry(geo), true, "F-8 green inside");
            geo.Runs.Add(HRun(new float2(1200, 100), new float2(200, 100), SlopeBand.Black, 1400f, 1000f));
            Gate_(V2Metrics.F8RunGeometry(geo), false, "F-8 black 1000 m long");

            // F-9: kept share and the reject mix
            var col = new AutoPlanResult { Candidates = 100 };
            for (int i = 0; i < 50; i++)
                col.Runs.Add(HRun(new float2(0, i), new float2(300, i)));
            col.Rejects[(int)AutoReject.TopTaken] = 10;
            col.Rejects[(int)AutoReject.TooShort] = 5;
            col.Rejects[(int)AutoReject.Overlap] = 20;
            Gate_(V2Metrics.F9Collapse(col), true, "F-9 half kept");
            col.Rejects[(int)AutoReject.Overlap] = 5;   // 15 of 20 rejects are TopTaken + TooShort
            Gate_(V2Metrics.F9Collapse(col), false, "F-9 rejects dominated by TopTaken + TooShort");
            var v1 = new AutoPlanResult { Candidates = 478 };
            for (int i = 0; i < 26; i++)
                v1.Runs.Add(HRun(new float2(0, i), new float2(300, i)));
            Gate_(V2Metrics.F9Collapse(v1), false, "F-9 the v1 field collapse, 26 of 478");

            // F-10: green + black, 1000 m each = 50 / 50; the plan says so, a re-rate to blue disagrees
            var mix = new AutoPlanResult();
            mix.Runs.Add(HRun(new float2(1100, 0), new float2(100, 0), SlopeBand.Green));
            mix.Runs.Add(HRun(new float2(1100, 100), new float2(100, 100), SlopeBand.Black));
            var mixPlan = new ResortPlan();
            mixPlan.MountainMix[1] = 0.5f;
            mixPlan.MountainMix[3] = 0.5f;
            Gate_(V2Metrics.F10MixHonest(mix, mixPlan, null), true, "F-10 honest mix");
            Gate_(V2Metrics.F10MixHonest(mix, mixPlan, run => run.Band == SlopeBand.Green ? SlopeBand.Blue : run.Band), false, "F-10 re-rated mix differs");
        }

        private static void FlowMetricTests()
        {
            // H-1
            var plan = new ResortPlan { Lifts = new[] { HLift(1, float2.zero, new float2(700, 0), 300f, 0.5f), HLift(1, float2.zero, new float2(700, 0), 300f, 0.5f, home: HomeVerdict.None) } };
            Gate_(V2Metrics.H1Verdicts(plan), false, "H-1 one lift without a verdict");
            Gate_(V2Metrics.H1Verdicts(plan, l => l.Home == HomeVerdict.None), true, "H-1 skip predicate");

            // H-2: lift Top (700, 0), Bottom (0, 0), base pad at the bottom; one run from the top to 20 m from the bottom
            var r = new AutoPlanResult();
            r.Runs.Add(HRun(new float2(690, 0), new float2(20, 0), SlopeBand.Green));
            var basePad = HPad(5000f, 0.05f, 30f, float2.zero, true);
            ResortPlan Make(HomeVerdict home, HomeLevel wanted) => new ResortPlan
            {
                Pads = new[] { basePad }, Lifts = new[] { HLift(1, float2.zero, new float2(700, 0), 300f, 0.5f, 0, 0, -1, home) }, RunLift = new[] { 0 }, RunPod = new[] { 0 },
            };
            ResortPlan green = Make(HomeVerdict.Green, HomeLevel.Auto);
            green.Lifts[0].Wanted = HomeLevel.Auto;
            Gate_(V2Metrics.H2Honesty(r, green, null), true, "H-2 Green with a green path");
            Gate_(V2Metrics.H2Honesty(r, green, run => SlopeBand.Blue), false, "H-2 Green but the re-rate says blue");
            ResortPlan blue = Make(HomeVerdict.Blue, HomeLevel.Auto);
            Gate_(V2Metrics.H2Honesty(r, blue, run => SlopeBand.Blue), true, "H-2 Blue with a blue path");
            ResortPlan dl = Make(HomeVerdict.Download, HomeLevel.Auto);
            Gate_(V2Metrics.H2Honesty(r, dl, null), false, "H-2 Download although a green path exists");
            Gate_(V2Metrics.H2Honesty(r, dl, run => SlopeBand.Black), true, "H-2 Download when only black exists (wanted green at the base pad)");
            dl.Lifts[0].Wanted = HomeLevel.Blue;
            Gate_(V2Metrics.H2Honesty(r, dl, run => SlopeBand.Blue), false, "H-2 Download although a blue path exists at wanted blue");
            var far = new AutoPlanResult();
            far.Runs.Add(HRun(new float2(690, 0), new float2(300, 0), SlopeBand.Green));   // ends 300 m from the bottom: no path home
            Gate_(V2Metrics.H2Honesty(far, green, null), false, "H-2 Green without a path to the base pad");
            Gate_(V2Metrics.H2Honesty(far, Make(HomeVerdict.Download, HomeLevel.Auto), null), true, "H-2 Download with no path");
            // a chain: run A ends inside run B's outline, run B reaches the pad
            var chain = new AutoPlanResult();
            chain.Runs.Add(HRun(new float2(690, 0), new float2(400, 0), SlopeBand.Green));
            chain.Runs.Add(HRun(new float2(420, 0), new float2(20, 0), SlopeBand.Blue));
            Gate_(V2Metrics.H2Honesty(chain, Make(HomeVerdict.Blue, HomeLevel.Auto), null), true, "H-2 chained runs, easiest path is blue");
            Gate_(V2Metrics.H2Honesty(chain, Make(HomeVerdict.Green, HomeLevel.Auto), null), false, "H-2 chained runs are not green all the way");

            // H-3
            var fake = new ResortPlan { Pods = new[] { new PlanPod { MeanFanGrade = 0.60f } }, Lifts = new[] { HLift(1, float2.zero, new float2(700, 0), 300f, 0.5f) } };
            Gate_(V2Metrics.H3NoFakeGreen(fake), false, "H-3 green on a 60 % pod");
            fake.Pods[0].MeanFanGrade = 0.50f;
            Gate_(V2Metrics.H3NoFakeGreen(fake), true, "H-3 green on a 50 % pod");
            fake.Pods[0].MeanFanGrade = 0.60f;
            fake.Lifts[0].Home = HomeVerdict.Blue;
            Gate_(V2Metrics.H3NoFakeGreen(fake), true, "H-3 blue on a 60 % pod is fine");

            // H-4
            var dr = new AutoPlanResult();
            for (int i = 0; i < 10; i++)
                dr.Runs.Add(HRun(new float2(0, i), new float2(300, i)));
            var dp = new ResortPlan();
            Gate_(V2Metrics.H4DeadEnds(dr, dp), true, "H-4 none");
            dp.DeadEnds = 1;
            Gate_(V2Metrics.H4DeadEnds(dr, dp), false, "H-4 1 of 10 = 10 %");
            Gate_(V2Metrics.H4DeadEnds(new AutoPlanResult(), new ResortPlan()), true, "H-4 no runs");

            // H-5: lift 0 rides from the base pad, its run ends on a bench pad; lift 1 starts on that bench pad
            var rr = new AutoPlanResult();
            rr.Runs.Add(HRun(new float2(690, 0), new float2(520, 0), SlopeBand.Green));        // lift 0's run, ends on the bench pad at (500, 0)
            rr.Runs.Add(HRun(new float2(1190, 0), new float2(1000, 0), SlopeBand.Blue));       // lift 1's run (top at (1200, 0))
            var rp = new ResortPlan
            {
                Pads = new[] { basePad, HPad(3000f, 0.1f, 30f, new float2(500, 0), false, true), HPad(3000f, 0.1f, 30f, new float2(5000, 0), false, true) },
                Lifts = new[] { HLift(1, float2.zero, new float2(700, 0), 300f, 0.5f, 0, 0), HLift(1, new float2(500, 0), new float2(1200, 0), 300f, 0.5f, 1, 1) },
                Pods = new[] { new PlanPod { Lift = 0, RunFirst = 0, RunCount = 1 }, new PlanPod { Lift = 1, RunFirst = 1, RunCount = 1 } },
            };
            Gate_(V2Metrics.H5Reachability(rr, rp), true, "H-5 stacked pods reachable");
            rp.Lifts[1].BottomPad = 2;
            rp.Lifts[1].Bottom = new float3(5000f, 1000f, 0f);
            Gate_(V2Metrics.H5Reachability(rr, rp), false, "H-5 lift 1 starts on an unreachable pad");

            // H-6 is a report: the observed share is the green-home share of pods
            var gp = new ResortPlan { Pods = new[] { new PlanPod { Lift = 0, MeanFanGrade = 0.2f }, new PlanPod { Lift = 1, MeanFanGrade = 0.2f } },
                Lifts = new[] { HLift(1, float2.zero, new float2(700, 0), 300f, 0.5f), HLift(1, float2.zero, new float2(700, 0), 300f, 0.5f, home: HomeVerdict.Black) } };
            Gate g = V2Metrics.H6GreenShare(gp);
            Check(Math.Abs(g.Value - 0.5) < 1e-9, "V2Metrics H-6", g.ToString());
        }
    }
}
