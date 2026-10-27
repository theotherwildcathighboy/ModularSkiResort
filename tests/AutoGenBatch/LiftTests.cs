// Lifts suite (A4): the lift skeleton (spec 4.4) on the synthetic fixtures. Norms per type and the station spots (S-4, S-1), the tower
// check re-planned independently with the lift tool's own planner (every span clear, edge slopes under 0.97 x the cap, the same tower
// count), lift invariants (ids, pods, keys, costs, no shared pod, no overlapping station lots), the Auto count (S-5) and the knob,
// stacking on Benched (S-6), pinned lifts honored (S-7: a proposal pinned back, NaN stations, a lift off the map, a garbage row), the
// tower-plan budget (P-5), determinism of the lifts (D-1 rerun / rotated / reversed, D-2 slices, D-3 Reselect of Lifts / Seed / Home),
// Serves recounted with LiftServedRule, NoLiftSite. ResortPlan.Fingerprint is A6's, so the lift checks compare LiftsHash, not the
// result fingerprint. LiftCase is the per-region check + REPORT for the `-- v2` matrix (V2Tests).

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static void LiftTests()
        {
            var cases = new (string, HeightFn, float2[], bool, bool)[]   // name, terrain, region, Auto count HARD (S-5), stacking wanted (S-6)
            {
                ("alpine1", Terrain.Alpine(1u), Terrain.AlpineRegion(), true, false),
                ("alpine2", Terrain.Alpine(2u), Terrain.AlpineRegion(), true, false),
                ("alpine3", Terrain.Alpine(3u), Terrain.AlpineRegion(), true, false),
                ("alpine lower", Terrain.Alpine(2u), Terrain.AlpineRegion(true), true, false),
                ("benched1", Terrain.Benched(1u), Terrain.AlpineRegion(), true, true),
                ("benched2", Terrain.Benched(2u), Terrain.AlpineRegion(), true, true),
                ("steep1", Terrain.Steep(1u), Terrain.Square(0f, 0f, 1250f), false, false),
                ("fbm 9 km2", Terrain.Fbm(21u, 0.30, 90.0), Terrain.Square(0f, 0f, 1499f), true, false),
            };
            foreach (var (name, f, region, countHard, stack) in cases)
            {
                string w = "Lifts " + name;
                AutoPlanInput input = ResortInput(Input(f, region));
                var planner = new SkiRunAutoPlanner();
                AutoPlanResult r = planner.Run(input);
                Check(r.Status == AutoPlanStatus.Ok && r.Resort != null, w, "no plan: " + r.Status);
                if (r.Resort == null)
                    continue;
                ResortPlan p = r.Resort;
                float km2 = r.RegionAreaM2 / 1.0e6f;
                LiftInvariants(w, input, planner, r);
                Gate s4 = V2Metrics.S4Lifts(p), s1 = V2Metrics.S1Pads(p), s5 = V2Metrics.S5LiftCount(p, km2, false, 0);
                Check(s4.Pass, w, s4.ToString());
                Check(s1.Pass, w, s1.ToString());
                if (countHard)
                    Check(s5.Pass, w, s5.ToString());
                else
                    Console.WriteLine($"REPORT Lifts {name}: {s5} (SOFT on Steep)");
                if (stack)
                {
                    Gate s6 = V2Metrics.S6Stacking(p, true);
                    Check(s6.Pass, w, s6.ToString());
                }
                Console.WriteLine($"INFO  Lifts {name}: {LiftLine(planner, r)}");

                // D-1 / D-2 on the lifts: rerun, rotated and reversed region, Step(4 ms) slices
                ulong want = LiftsHash(p);
                int n = region.Length;
                var rot = new float2[n];
                var rev = new float2[n];
                for (int i = 0; i < n; i++)
                {
                    rot[i] = region[(i + 1) % n];
                    rev[i] = region[n - 1 - i];
                }
                Check(LiftsHash(new SkiRunAutoPlanner().Run(input).Resort) == want, w, "D-1: a rerun planned other lifts");
                foreach (var (what, reg) in new[] { ("rotated", rot), ("reversed", rev) })
                {
                    AutoPlanInput ri = ResortInput(input);
                    ri.Region = reg;
                    AutoPlanResult rr = new SkiRunAutoPlanner().Run(ri);
                    Check(rr.Resort != null && LiftsHash(rr.Resort) == want, w, "D-1: the " + what + " region planned other lifts");
                }
                var sliced = new SkiRunAutoPlanner();
                sliced.Begin(input);
                while (!sliced.Step(4f))
                {
                }
                Check(sliced.Result.Resort != null && LiftsHash(sliced.Result.Resort) == want, w, "D-2: Step(4) slices planned other lifts");
            }

            LiftKnobTests();
            LiftPinnedTests();
            LiftNoSiteTest();
        }

        /// <summary>The lift invariants of one Resort result, checked on the plan alone plus an independent tower re-plan per proposal.</summary>
        private static void LiftInvariants(string w, AutoPlanInput input, SkiRunAutoPlanner planner, AutoPlanResult r, bool timeChecked = true)
        {
            ResortPlan p = r.Resort;
            int pinned = input.Pinned?.Count ?? 0;
            var keys = new HashSet<uint>();
            var podOf = new Dictionary<int, int>();
            for (int i = 0; i < p.Lifts.Length; i++)
            {
                PlanLift l = p.Lifts[i];
                string wl = w + " lift " + i;
                Check(l.Id == i, wl, "Id " + l.Id + " is not the index");
                Check(keys.Add(l.Key), wl, "duplicate Key " + l.Key);
                Check((i < pinned) == (l.Origin == LiftOrigin.Pinned), wl, "pinned lifts must come first (origin " + l.Origin + ")");
                Check(l.Pod >= -1 && l.Pod < p.Pods.Length, wl, "Pod " + l.Pod + " out of range");
                Check(l.HomeRun == -1 || l.HomeRun < r.Runs.Count, wl, "HomeRun out of range");
                Check(l.TowerXz != null && l.Serves >= 0 && l.StartsNearTop >= 0 && l.EndsAtBottom >= 0, wl, "counters / tower array");
                if (l.Origin == LiftOrigin.Pinned)
                {
                    Check(l.Towers == -1 && l.TowerXz.Length == 0 && l.Cost == 0, wl, "a pinned lift carries planned towers or a cost");
                    continue;
                }
                Check(l.Pod >= 0 && p.Pods[l.Pod].Lift == i, wl, "the pod does not point back at its lift");
                Check(!podOf.ContainsKey(l.Pod), wl, "two lifts on pod " + l.Pod);
                podOf[l.Pod] = i;
                Check(l.BottomPad == p.Pods[l.Pod].LowerPad, wl, "BottomPad " + l.BottomPad + " is not the pod's lower pad " + p.Pods[l.Pod].LowerPad);
                Check(l.TopPad >= -1 && l.TopPad < p.Pads.Length && l.BottomPad >= 0 && l.BottomPad < p.Pads.Length, wl, "pad ids out of range");
                Check(l.TypeIndex < 4 && l.Top.y > l.Bottom.y && Math.Abs(l.RiseM - (l.Top.y - l.Bottom.y)) < 0.01f &&
                      Math.Abs(l.LengthM - math.distance(l.Bottom.xz, l.Top.xz)) < 0.05f, wl, "geometry fields disagree");
                float hb = input.Snapshot.Sample(new float3(l.Bottom.x, 0f, l.Bottom.z)), ht = input.Snapshot.Sample(new float3(l.Top.x, 0f, l.Top.z));
                Check(Math.Abs(hb - l.Bottom.y) < 0.01f && Math.Abs(ht - l.Top.y) < 0.01f, wl, "station heights are not the snapshot's");
                LiftTypeDef def = SkiCatalog.Lifts[l.TypeIndex];
                long cost = def.StationCostBase + def.StationCostTop + (long)Math.Round(def.LinePerKm * (double)l.LengthM / 1000.0);
                Check(Math.Abs(l.Cost - cost) <= 1, wl, $"cost {l.Cost}, catalog {cost}");
                float hd = SkiRunAutoPlanner.StationHalfDiagonalM[l.TypeIndex];
                Check(l.BottomSunk == !(p.Pads[l.BottomPad].InscribedM >= hd) &&
                      l.TopSunk == !(l.TopPad >= 0 && p.Pads[l.TopPad].InscribedM >= hd), wl, "Sunk flags disagree with the pads");
                Check(l.Towers == l.TowerXz.Length && l.Towers >= 0, wl, "Towers " + l.Towers + " vs " + l.TowerXz.Length + " spots");
                // The tower check, independently: the lift tool's planner on the same chord and rules
                LiftTowerPlanConstants k = LiftTowerPlanConstants.For(def);
                float len = l.LengthM;
                float2 dir = (l.Top.xz - l.Bottom.xz) / len, right = new float2(dir.y, -dir.x);
                HeightSnapshot snap = input.Snapshot;
                float3 a = l.Bottom;
                int misses = 0;
                LiftTerrainSampler s = (along, lateral) =>
                {
                    float2 q = a.xz + dir * along + right * lateral;
                    float h = snap.Sample(new float3(q.x, 0f, q.y));
                    if (float.IsNaN(h))
                        misses++;
                    return float.IsNaN(h) ? a.y : h;
                };
                var tp = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = l.Bottom.y, EndNodeYB = l.Top.y };
                LiftTowerPlan plan = tp.Plan(s, len, l.Bottom.y + k.RopeAboveBase(0), l.Top.y + k.RopeAboveBase(0), k);
                Check(misses == 0 && plan.Clear && plan.MaxEdgeSlope <= SkiRunAutoPlanner.LiftEdgeSlopeShare * k.MaxEdgeSlope + 1e-5f, wl,
                      $"tower re-plan not clear (misses {misses}, clear {plan.Clear}, slope {plan.MaxEdgeSlope:F3} cap {k.MaxEdgeSlope})");
                Check(plan.LineTowers == l.Towers && Math.Abs(plan.MaxEdgeSlope - l.MaxEdgeSlope) < 1e-4f, wl,
                      $"tower re-plan {plan.LineTowers} towers / slope {plan.MaxEdgeSlope:F3} vs stored {l.Towers} / {l.MaxEdgeSlope:F3}");
                // No station lot overlaps another lift's
                for (int j = 0; j < i; j++)
                {
                    PlanLift o = p.Lifts[j];
                    float ho = SkiRunAutoPlanner.StationHalfDiagonalM[Math.Min(3, (int)o.TypeIndex)];
                    foreach (float3 x in new[] { l.Bottom, l.Top })
                        foreach (float3 y in new[] { o.Bottom, o.Top })
                            Check(!(math.distance(x.xz, y.xz) < hd + ho - 1e-3f), wl, $"station lot overlaps lift {j}'s");
                }
            }
            // Pods: Lift points at a lift serving that pod (proposal or pinned), else -1
            for (int k = 0; k < p.Pods.Length; k++)
            {
                int li = p.Pods[k].Lift;
                Check(li == -1 || (li >= 0 && li < p.Lifts.Length && p.Lifts[li].Pod == k), w + " pod " + k, "Lift " + li + " does not serve it");
            }
            int proposed = p.Lifts.Length - pinned;
            Check(proposed <= planner.LiftsWanted - (input.Knobs.Lifts > 0 ? 0 : pinned), w,
                  $"{proposed} proposals over the wanted {planner.LiftsWanted}");
            // P-5: the tower plan budget and time (a slow plan is re-measured once: one host GC pause)
            int cap = SkiRunAutoPlanner.MaxTowerPlansOf(r.RegionAreaM2);
            Check(planner.TowerPlans <= cap && planner.TowerPlans >= proposed, w, $"P-5: {planner.TowerPlans} tower plans (cap {cap}, {proposed} lifts)");
            float maxMs = planner.TowerPlanMaxMs;
            if (maxMs >= 50f && timeChecked && TimingChecked)
            {
                var again = new SkiRunAutoPlanner();
                again.Run(input);
                maxMs = Math.Min(maxMs, again.TowerPlanMaxMs);
            }
            if (TimingChecked && timeChecked)
                Check(maxMs < 50f, w, $"P-5: a tower plan took {maxMs:F1} ms (50 ms limit)");
            // Serves / near top / at bottom, recounted with the served rule
            for (int i = 0; i < p.Lifts.Length; i++)
            {
                PlanLift l = p.Lifts[i];
                var chords = new List<LiftServedRule.Chord>();
                if (l.Origin == LiftOrigin.Proposed)
                    chords.Add(new LiftServedRule.Chord { A = l.Bottom.xz, B = l.Top.xz });
                else if (input.Pinned[i].Chords != null)
                    for (int q = 0; q + 1 < input.Pinned[i].Chords.Length; q += 2)
                        chords.Add(new LiftServedRule.Chord { A = input.Pinned[i].Chords[q], B = input.Pinned[i].Chords[q + 1] });
                LiftTypeDef def = SkiCatalog.Lifts[Math.Min(3, (int)l.TypeIndex)];
                var lift = new LiftServedRule.Lift
                {
                    Complete = true, TopY = l.Top.y, FirstChord = 0, ChordCount = chords.Count, Rule = (byte)def.ServeRule, RadiusM = def.ServeRadiusM,
                    TopMarginM = def.TopMarginM, TopXZ = l.Top.xz,
                };
                int serves = 0, top = 0, bottom = 0;
                foreach (AutoRun run in r.Runs)
                {
                    float3 hi = run.Nodes[0];
                    foreach (float3 v in run.Nodes)
                        if (v.y > hi.y)
                            hi = v;
                    serves += LiftServedRule.Reaches(hi, lift, chords, out float _) ? 1 : 0;
                    top += math.distance(run.Centerline[0], l.Top.xz) <= SkiRunAutoPlanner.LiftNearM ? 1 : 0;
                    bottom += math.distance(run.Centerline[run.Centerline.Length - 1], l.Bottom.xz) <= SkiRunAutoPlanner.LiftNearM ? 1 : 0;
                }
                Check(serves == l.Serves && top == l.StartsNearTop && bottom == l.EndsAtBottom, w + " lift " + i,
                      $"Serves / near top / at bottom {l.Serves}/{l.StartsNearTop}/{l.EndsAtBottom}, recounted {serves}/{top}/{bottom}");
            }
        }

        // Auto count and the knob (S-5), greedy prefix, D-3 of Lifts / Seed / Home on the lifts
        private static void LiftKnobTests()
        {
            AutoPlanInput input = ResortInput(Input(Terrain.Alpine(1u), Terrain.AlpineRegion()));
            AutoPlanResult auto = new SkiRunAutoPlanner().Run(input);
            Check(auto.Resort != null && auto.Resort.Lifts.Length >= 2, "Lifts knob", "the Auto plan needs 2+ lifts for the knob checks");
            if (auto.Resort == null || auto.Resort.Lifts.Length < 2)
                return;
            Check(SkiRunAutoPlanner.AutoLiftsOf(1.0e6f, 0f) == 1 && SkiRunAutoPlanner.AutoLiftsOf(9.0e6f, 300f) == 5 &&
                  SkiRunAutoPlanner.AutoLiftsOf(4.0e6f, 1300f) == 4 && SkiRunAutoPlanner.AutoLiftsOf(25.0e6f, 5000f) == 9 &&
                  SkiRunAutoPlanner.AutoLiftsOf(1.0e6f, 401f) == 2, "Lifts knob", "AutoLiftsOf (round(km2 / 2), ceil(vertical / 400), 1-9)");
            foreach (int knob in new[] { 1, 2 })
            {
                AutoPlanInput ki = ResortInput(input);
                ki.Knobs.Lifts = knob;
                var kp = new SkiRunAutoPlanner();
                AutoPlanResult kr = kp.Run(ki);
                string w = "Lifts knob " + knob;
                Check(kr.Resort != null, w, "no plan");
                if (kr.Resort == null)
                    continue;
                Gate s5 = V2Metrics.S5LiftCount(kr.Resort, kr.RegionAreaM2 / 1.0e6f, false, knob);
                Check(s5.Pass && kr.Resort.Lifts.Length == Math.Min(knob, auto.Resort.Lifts.Length), w, s5.ToString());
                // The greedy picks do not depend on the cap: a capped plan is a prefix of the Auto plan
                for (int i = 0; i < kr.Resort.Lifts.Length && i < auto.Resort.Lifts.Length; i++)
                    Check(LiftHash(kr.Resort.Lifts[i], false) == LiftHash(auto.Resort.Lifts[i], false), w, "lift " + i + " differs from the Auto plan's");
                LiftInvariants(w, ki, kp, kr);
            }
            // D-3 on the lifts: Reselect Lifts 2 (restart at Lifts), back to Auto, Seed (lifts kept), Home (lifts kept)
            foreach (var (what, lifts, seed, home, keep) in new (string, int, uint, HomeLevel, bool)[]
                     {
                         ("Lifts 2", 2, input.Knobs.Seed, HomeLevel.Auto, false),
                         ("Seed", 0, SkiRunAutoPlanner.SeedOf(input.Region, 5), HomeLevel.Auto, true),
                         ("Home green", 0, input.Knobs.Seed, HomeLevel.Green, true),
                     })
            {
                string w = "Lifts D-3 " + what;
                var p = new SkiRunAutoPlanner();
                p.Run(input);
                AutoPlanKnobs k = input.Knobs;
                k.Lifts = lifts;
                k.Seed = seed;
                k.Home = home;
                Check(p.Reselect(k), w, "Reselect refused");
                while (!p.Step(float.PositiveInfinity))
                {
                }
                AutoPlanInput fi = ResortInput(input);
                fi.Knobs = k;
                AutoPlanResult fresh = new SkiRunAutoPlanner().Run(fi);
                Check(p.Result.Resort != null && fresh.Resort != null && LiftsHash(p.Result.Resort) == LiftsHash(fresh.Resort) &&
                      p.Result.Fingerprint == fresh.Fingerprint, w, "differs from a fresh run with the knobs");
                if (keep && p.Result.Resort != null)
                {
                    bool same = p.Result.Resort.Lifts.Length == auto.Resort.Lifts.Length;
                    for (int i = 0; same && i < auto.Resort.Lifts.Length; i++)
                        same = LiftHash(p.Result.Resort.Lifts[i], false) == LiftHash(auto.Resort.Lifts[i], false);
                    Check(same, w, "the lift skeleton moved (only Lifts re-plans lifts)");
                }
                // and back to Auto from the knob
                if (lifts > 0)
                {
                    k.Lifts = 0;
                    Check(p.Reselect(k), w + " back", "Reselect refused");
                    while (!p.Step(float.PositiveInfinity))
                    {
                    }
                    Check(p.Result.Resort != null && LiftsHash(p.Result.Resort) == LiftsHash(auto.Resort), w + " back", "back to Auto differs from the Auto plan");
                }
            }
        }

        // S-7 and the pinned-lift edge cases
        private static void LiftPinnedTests()
        {
            AutoPlanInput input = ResortInput(Input(Terrain.Alpine(1u), Terrain.AlpineRegion()));
            AutoPlanResult auto = new SkiRunAutoPlanner().Run(input);
            if (auto.Resort == null || auto.Resort.Lifts.Length == 0)
            {
                Check(false, "Lifts pinned", "the Auto plan has no lift to pin");
                return;
            }
            PlanLift src = auto.Resort.Lifts[0];
            float3 nan = new float3(float.NaN);
            var rows = new (string, PinnedLift, bool)[]   // name, row, stays on its pod
            {
                ("proposal pinned back", Pin(901u, src, src.Bottom, src.Top), true),
                ("bottom NaN", Pin(902u, src, nan, src.Top), true),
                ("both NaN", Pin(903u, src, nan, nan), true),
                ("off the map", new PinnedLift { Key = 904u, TypeIndex = 0, Bottom = new float3(6000f, 0f, 6000f), Top = new float3(6900f, 300f, 6900f),
                                                  Chords = new[] { new float2(6000f, 6000f), new float2(6900f, 6900f) }, Complete = true }, false),
                ("garbage row", new PinnedLift { Key = 905u, TypeIndex = 1, Bottom = nan, Top = nan, Chords = null }, false),
            };
            foreach (var (name, row, onPod) in rows)
            {
                string w = "Lifts pinned " + name;
                AutoPlanInput pi = ResortInput(input);
                pi.Pinned = new List<PinnedLift> { row };
                var p = new SkiRunAutoPlanner();
                AutoPlanResult r = p.Run(pi);
                Check(r.Resort != null, w, "no plan: " + r.Status);
                if (r.Resort == null)
                    continue;
                Gate s7 = V2Metrics.S7Pinned(r.Resort, pi.Pinned);
                Check(s7.Pass, w, s7.ToString());
                PlanLift l = r.Resort.Lifts[0];
                Check(l.Origin == LiftOrigin.Pinned && l.Key == row.Key, w, "the pinned lift is not lifts[0]");
                if (onPod)
                {
                    Check(l.Pod == src.Pod, w, $"pinned on pod {l.Pod}, the proposal was on pod {src.Pod}");
                    Check(math.distance(l.Bottom.xz, src.Bottom.xz) < 1f && math.distance(l.Top.xz, src.Top.xz) < 1f && Math.Abs(l.Bottom.y - src.Bottom.y) < 0.5f,
                          w, $"stations ({l.Bottom}) / ({l.Top}) are not the proposal's");
                }
                else
                    Check(l.Pod == -1, w, "a lift off the region got pod " + l.Pod);
                if (name == "proposal pinned back")
                    Check(r.Resort.Lifts.Length - 1 <= p.LiftsWanted - 1 && r.Resort.Lifts[0].Serves == auto.Resort.Lifts[0].Serves, w,
                          "the pinned lift serves differently from its proposal, or Auto did not count it");
                LiftInvariants(w, pi, p, r);
                // Reselect at Lifts with the pinned row equals a fresh run (D-3)
                AutoPlanKnobs k = pi.Knobs;
                k.Lifts = 2;
                Check(p.Reselect(k), w, "Reselect refused");
                while (!p.Step(float.PositiveInfinity))
                {
                }
                AutoPlanInput fi = ResortInput(pi);
                fi.Knobs = k;
                Check(p.Result.Resort != null && LiftsHash(p.Result.Resort) == LiftsHash(new SkiRunAutoPlanner().Run(fi).Resort), w, "D-3: Reselect Lifts 2 differs from a fresh run");
            }
        }

        private static PinnedLift Pin(uint key, PlanLift l, float3 bottom, float3 top) => new PinnedLift
        {
            Key = key, TypeIndex = l.TypeIndex, Bottom = bottom, Top = top, Chords = new[] { l.Bottom.xz, l.Top.xz }, Complete = true,
        };

        // A ground with no lift site: a 35 % plane has no pad, so no pod and no proposal, and the plan says NoLiftSite
        private static void LiftNoSiteTest()
        {
            AutoPlanInput input = ResortInput(Input(Terrain.Plane(0.35), Terrain.Square(0f, 0f, 400f)));
            var p = new SkiRunAutoPlanner();
            AutoPlanResult r = p.Run(input);
            if (r.Resort == null)
            {
                Console.WriteLine($"INFO  Lifts no site: refused {r.Status}");
                return;
            }
            int proposed = 0;
            foreach (PlanLift l in r.Resort.Lifts)
                proposed += l.Origin == LiftOrigin.Proposed ? 1 : 0;
            Check(proposed == 0 && r.Resort.Pods.Length == 0, "Lifts no site", $"the 35 % plane has {r.Resort.Pods.Length} pods and {proposed} proposals");
            Check((proposed == 0) == ((r.Warnings & AutoPlanWarning.NoLiftSite) != 0), "Lifts no site",
                  $"{proposed} proposals, NoLiftSite {(r.Warnings & AutoPlanWarning.NoLiftSite) != 0}");
            Console.WriteLine($"INFO  Lifts no site: {r.Status}, pods {r.Resort.Pods.Length}, proposals {proposed}, warnings {r.Warnings}");
        }

        // ---------------------------------------------------------------- the -- v2 matrix

        /// <summary>Per region of the v2 matrix (V2Tests): P-5 (tower plans per scan, ms per plan), S-7 on synthetic fixtures (the first
        /// proposal pinned back), the lift invariants, and one REPORT lifts line plus one line per lift (the handover's per-fixture table).</summary>
        private static void LiftCase(V2Case c, AutoPlanInput input, SkiRunAutoPlanner planner, AutoPlanResult r)
        {
            ResortPlan p = r.Resort;
            int cap = SkiRunAutoPlanner.MaxTowerPlansOf(r.RegionAreaM2);
            float maxMs = planner.TowerPlanMaxMs;
            if (maxMs >= 50f)
            {
                var again = new SkiRunAutoPlanner();   // one slow plan is re-measured once (a host GC pause, as P-1); the smaller figure counts
                again.Run(ResortInput(input));
                maxMs = Math.Min(maxMs, again.TowerPlanMaxMs);
            }
            // The plan cap is HARD everywhere; the 50 ms per plan is HARD on the synthetic fixtures and REPORT on the real heightmaps (as
            // P-1): the time is the shared tower planner's on terrain LiftBatch never saw, and A4 does not tune that planner
            bool countOk = planner.TowerPlans <= cap;
            V2Emit(c, new Gate("P-5", countOk && maxMs < 50f, maxMs,
                $"{planner.TowerPlans} tower plans (cap {cap}), longest {maxMs:F1} ms (want under 50), {planner.TowerPlanMs:F0} ms in all{SlowPlan(planner)}"),
                countOk && c.Kind != FixtureKind.Synthetic);
            LiftInvariants("Lifts " + c.Id, input, planner, r, false);   // P-5 time: the gate above
            Console.WriteLine($"REPORT lifts {c.Id}: {LiftLine(planner, r)}");
            foreach (PlanLift l in p.Lifts)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "REPORT lift {0} P{1}: {2} pod {3}, {4:F0} m, rise {5:F0} m, {6} towers, slope {7:F2}, bottom pad {8} ({9}{10}), top pad {11} ({12}{13}), " +
                    "cost {14:N0}, score {15:F0}, serves {16}, near top {17}, at bottom {18}",
                    c.Id, l.Id + 1, SkiCatalog.Lifts[Math.Min(3, (int)l.TypeIndex)].Id, l.Pod, l.LengthM, l.RiseM, l.Towers, l.MaxEdgeSlope,
                    l.BottomPad, PadText(p, l.BottomPad), l.BottomSunk ? ", sunk" : "", l.TopPad, PadText(p, l.TopPad), l.TopSunk ? ", sunk" : "",
                    l.Cost, l.Score, l.Serves, l.StartsNearTop, l.EndsAtBottom));
            if (c.Kind != FixtureKind.Synthetic || p.Lifts.Length == 0)
            {
                Tally("S-7")[3]++;   // pinned lifts need the census: on real fixtures the host plans without them
                return;
            }
            PlanLift src = p.Lifts[0];
            AutoPlanInput pi = ResortInput(input);
            pi.Pinned = new List<PinnedLift> { Pin(777u, src, src.Bottom, src.Top) };
            AutoPlanResult pr = new SkiRunAutoPlanner().Run(pi);
            V2Emit(c, pr.Resort != null ? V2Metrics.S7Pinned(pr.Resort, pi.Pinned) : new Gate("S-7", false, 1, "no plan with the pinned lift: " + pr.Status), false);
        }

        /// <summary>S-4 / S-5 SOFT where the terrain, not the selection, is the limit: fewer proposals than the Lifts stage wanted and no
        /// pod left unserved by the tower-plan cap (every pod without a lift had no chord inside the norms, no clear tower line or no lot
        /// room). S-4 is only softened when nothing was proposed (n/a); a proposal outside the norms stays HARD.</summary>
        private static bool LiftsTerrainLimited(AutoPlanResult r, bool countGate, SkiRunAutoPlanner planner)
        {
            if (r.Resort == null)
                return false;
            int proposed = 0;
            foreach (PlanLift l in r.Resort.Lifts)
                if (l.Origin != LiftOrigin.Pinned)
                    proposed++;
            bool limited = r.Resort.Lifts.Length < planner.LiftsWanted && planner.LiftStat(SkiRunAutoPlanner.LiftStatCapped) == 0;
            return limited && (countGate || proposed == 0);
        }

        /// <summary>P-1 without the Lifts stage: the longest unit over the v1 probe slots and the Resort slots other than Lifts (every unit
        /// belongs to one of them). A Lifts unit runs one tower plan (spec 4.4: 10-44 ms CoreCLR, on the worker); P-5 bounds it at 50 ms.</summary>
        private static double UnitMsWithoutTowerPlans(AutoPlanResult r)
        {
            double ms = 0.0;
            foreach (float v in r.StageMaxMs)
                ms = Math.Max(ms, v);
            if (r.Resort != null)
                for (int s = 0; s < r.Resort.StageMaxMs.Length; s++)
                    if (s != 3)
                        ms = Math.Max(ms, r.Resort.StageMaxMs[s]);
            return ms;
        }

        private static string SlowPlan(SkiRunAutoPlanner planner)
        {
            int at = -1;
            for (int i = 0; i < planner.TowerPlanLog.Count; i++)
                if (at < 0 || planner.TowerPlanLog[i].Ms > planner.TowerPlanLog[at].Ms)
                    at = i;
            if (at < 0)
                return "";
            SkiRunAutoPlanner.TowerPlanRecord t = planner.TowerPlanLog[at];
            return string.Format(CultureInfo.InvariantCulture, " (longest: pod {0}, {1}, {2:F0} m, rise {3:F0} m, {4} towers, {5})", t.Pod,
                SkiCatalog.Lifts[Math.Min(3, (int)t.Type)].Id, t.LengthM, t.RiseM, t.Towers, t.Clear ? "clear" : "not clear");
        }

        private static string PadText(ResortPlan p, int pad) =>
            pad < 0 ? "no pad" : string.Format(CultureInfo.InvariantCulture, "{0:F1} ha, inscribed {1:F0} m{2}", p.Pads[pad].AreaM2 / 1e4f,
                p.Pads[pad].InscribedM, p.Pads[pad].IsBase ? ", base" : p.Pads[pad].IsBench ? ", bench" : "");

        private static string LiftLine(SkiRunAutoPlanner planner, AutoPlanResult r)
        {
            ResortPlan p = r.Resort;
            int[] types = new int[4];
            int bench = 0, sunk = 0, served = 0;
            foreach (PlanLift l in p.Lifts)
            {
                types[Math.Min(3, (int)l.TypeIndex)]++;
                bench += l.BottomPad >= 0 && p.Pads[l.BottomPad].IsBench ? 1 : 0;
                sunk += (l.BottomSunk ? 1 : 0) + (l.TopSunk ? 1 : 0);
                served += l.Serves;
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0} pods, {1} lifts (wanted {2}; FG/DQ/GD/tram {3}/{4}/{5}/{6}), {7} bench bottoms, {8} sunk ends, serves {9} of {10} runs; " +
                "candidates {11}, norm rejects {12}, lowered tops {13}, tower fails {14}, conflicts {15}, capped pods {16}; tower plans {17} ({18:F0} ms, longest {19:F1})",
                p.Pods.Length, p.Lifts.Length, planner.LiftsWanted, types[0], types[1], types[2], types[3], bench, sunk, served, r.Runs.Count,
                planner.LiftStat(SkiRunAutoPlanner.LiftStatCandidates), planner.LiftStat(SkiRunAutoPlanner.LiftStatNorms),
                planner.LiftStat(SkiRunAutoPlanner.LiftStatLowered), planner.LiftStat(SkiRunAutoPlanner.LiftStatTowerFail),
                planner.LiftStat(SkiRunAutoPlanner.LiftStatConflict), planner.LiftStat(SkiRunAutoPlanner.LiftStatCapped), planner.TowerPlans,
                planner.TowerPlanMs, planner.TowerPlanMaxMs);
        }

        /// <summary>FNV-1a over every lift field the planner writes (ResortPlan.Fingerprint is A6's): the lift checks' determinism key.</summary>
        private static ulong LiftsHash(ResortPlan p)
        {
            ulong h = 14695981039346656037UL;
            if (p == null)
                return 0UL;
            foreach (PlanLift l in p.Lifts)
                h = (h ^ LiftHash(l, true)) * 1099511628211UL;
            for (int k = 0; k < p.Pods.Length; k++)
                h = (h ^ (ulong)(uint)p.Pods[k].Lift) * 1099511628211UL;
            return h;
        }

        private static ulong LiftHash(PlanLift l, bool counters)
        {
            ulong h = 14695981039346656037UL;
            void U(uint v) => h = (h ^ v) * 1099511628211UL;
            void F(float v) => U(math.asuint(v));
            U(l.Key); U((uint)l.Origin); U(l.TypeIndex); F(l.Bottom.x); F(l.Bottom.y); F(l.Bottom.z); F(l.Top.x); F(l.Top.y); F(l.Top.z);
            F(l.LengthM); F(l.RiseM); F(l.MaxEdgeSlope); U((uint)l.Towers); U((uint)l.Pod); U((uint)l.BottomPad); U((uint)l.TopPad);
            U(l.TopSunk ? 1u : 0u); U(l.BottomSunk ? 1u : 0u); U((uint)l.Cost); U((uint)(l.Cost >> 32));
            foreach (float2 t in l.TowerXz)
            {
                F(t.x);
                F(t.y);
            }
            if (counters)
            {
                U((uint)l.Serves);
                U((uint)l.StartsNearTop);
                U((uint)l.EndsAtBottom);
            }
            return h;
        }
    }
}
