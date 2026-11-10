// Host mode "rf-realize <out dir> [regions file]": plans each region with the runs-first planner, realizes the plan
// (RunsFirstRealizer), and prints what the realizer kept and lost (RealizeReport), the scorecard of the raw plan and of the realized
// plan, and the time of both. No regions file = the built-in field test 5 regions (ft5-r1..r4). Report only: no checks, exit 0.
// Writes <out dir>/<region>.txt (every report line) and appends one row per region to <out dir>/realize.tsv.
// The regions file format is the rf mode's (RunsFirst.Run): "<id> [@Fixture] x,z x,z ... [hole x,z ...]".

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static class RealizerHost
    {
        private static float2 P(float x, float z) => new float2(x, z);

        // the field test 5 regions (a copy of RunsFirst.Regions, which is private)
        private static readonly (string Id, float2[] Ring)[] Ft5 =
        {
            ("ft5-r1", new[] { P(3372, -1943), P(3462, -3193), P(6386, -2246), P(6244, -1083) }),
            ("ft5-r2", new[] { P(2688, -1711), P(2768, -2269), P(2717, -2714), P(3317, -3708), P(3593, -3839), P(6312, -2292), P(7148, -1835),
                P(6951, -983), P(5793, -1107), P(4905, -1124), P(4047, -1423), P(3703, -1716), P(3358, -1858) }),
            ("ft5-r3", new[] { P(6276, -966), P(5976, 559), P(5020, 1226), P(2076, -686), P(3790, -756), P(4412, -411), P(4942, -362), P(5651, -1175) }),
            ("ft5-r4", new[] { P(-2941, -4929), P(-978, -5499), P(-339, -4233), P(-1945, -3261), P(-2580, -3672) }),
        };

        // Iteration 3, item 4: km by planned band kept at full / 75 % / 50 % / not, and the mean widths (m) as kept and at full width
        private static string Keep(RealizeReport r, int p) => $"{r.KeepKm[p, 0]:F2}/{r.KeepKm[p, 1]:F2}/{r.KeepKm[p, 2]:F2}/{r.KeepKm[p, 3]:F2}";

        private static string Width(RealizeReport r, int p, bool full) =>
            r.WidthLenM[p] > 0 ? ((full ? r.FullWidthM2[p] : r.WidthM2[p]) / r.WidthLenM[p]).ToString("F1", CultureInfo.InvariantCulture) : "-";

        private static string KeepLine(RealizeReport r) =>
            $"planned band kept at full/75/50 %/not (km) G {Keep(r, 1)} B {Keep(r, 2)} K {Keep(r, 3)} DB {Keep(r, 4)}; mean width G {Width(r, 1, false)} m " +
            $"(full {Width(r, 1, true)}), B {Width(r, 2, false)} m (full {Width(r, 2, true)}); band agreement {(r.PlainKm > 0 ? 100 * r.AgreeKmFull / r.PlainKm : 100):F0} % of km " +
            $"at full width, {(r.PlainKm > 0 ? 100 * r.AgreeKmKept / r.PlainKm : 100):F0} % as kept";

        public static int Run(string[] args)
        {
            string outDir = args.Length > 1 ? args[1] : "rf-realize";
            Directory.CreateDirectory(outDir);
            var regions = new List<(string Fx, string Id, float2[] Ring)>();
            if (args.Length > 2)
                regions.AddRange(ReadRegions(args[2]));
            else
                foreach (var (id, ring) in Ft5)
                    regions.Add(("MountainVillage", id, ring));
            var files = new Dictionary<string, HeightmapFile>();
            var rows = new List<Row>();
            RunsFirstOptions options = RunsFirst.HostOptions();
            foreach (var (fx, id, ring) in regions)
            {
                if (!files.TryGetValue(fx, out HeightmapFile hf))
                {
                    if (!Heightmap.TryOpen(fx, out hf, out string why))
                    {
                        Console.WriteLine($"SKIP rf-realize {id}: {why}");
                        continue;
                    }
                    files[fx] = hf;
                }
                rows.Add(RunRegion(id, ring, Heightmap.Snapshot(hf, ring), options, outDir));
            }
            if (rows.Count > 0)
            {
                Console.WriteLine($"REALIZE mean over {rows.Count} region(s): km kept {rows.Average(r => r.KeptShare) * 100:F1} %, unlinked {rows.Average(r => r.UnlinkedShare) * 100:F2} %, " +
                    $"score raw {rows.Average(r => r.Raw):F2} realized {rows.Average(r => r.Realized):F2} (delta {rows.Average(r => r.Realized - r.Raw):+0.00;-0.00}), as skied {rows.Average(r => r.Skied):F2} (delta {rows.Average(r => r.Skied - r.Raw):+0.00;-0.00}), " +
                    $"plan {rows.Average(r => r.PlanMs):F0} ms, realize {rows.Average(r => r.RealizeMs):F0} ms, layers {rows.Max(r => r.LayerMB):F1} MB max, " +
                    $"deterministic {rows.Count(r => r.Same)}/{rows.Count}");
            }
            return 0;
        }

        private sealed class Row
        {
            public double KeptShare, UnlinkedShare, Raw, Realized, Skied, PlanMs, RealizeMs, LayerMB;
            public bool Same;
        }

        private static IEnumerable<(string, string, float2[])> ReadRegions(string path)
        {
            foreach (string row in File.ReadAllLines(path))
            {
                var t = new List<string>(row.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                string fx = t.Count > 1 && t[1].StartsWith("@") ? t[1].Substring(1) : "MountainVillage";
                if (t.Count > 1 && t[1].StartsWith("@"))
                    t.RemoveAt(1);
                if (t.Count < 4)
                    continue;
                // "hole x,z ...": the keyhole ring the rf mode builds (RunsFirst.Run)
                var parts = new List<List<float2>> { new List<float2>() };
                for (int i = 1; i < t.Count; i++)
                {
                    if (t[i] == "hole")
                    {
                        parts.Add(new List<float2>());
                        continue;
                    }
                    string[] xz = t[i].Split(',');
                    parts[parts.Count - 1].Add(new float2(float.Parse(xz[0], CultureInfo.InvariantCulture), float.Parse(xz[1], CultureInfo.InvariantCulture)));
                }
                var ring = new List<float2>(parts[0]);
                foreach (List<float2> hole in parts.Skip(1).Where(h => h.Count >= 3))
                {
                    if (Math.Sign(Signed(hole)) == Math.Sign(Signed(parts[0])))
                        hole.Reverse();
                    ring.Add(parts[0][0]);
                    ring.AddRange(hole);
                    ring.Add(hole[0]);
                }
                yield return (fx, t[0], ring.ToArray());
            }
        }

        private static double Signed(List<float2> r)
        {
            double a = 0.0;
            for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
                a += (double)r[j].x * r[i].y - (double)r[i].x * r[j].y;
            return a;
        }

        /// <summary>The input the tool would pass: the region, its snapshot, Resort mode. The area cap is lifted so the big set runs.</summary>
        public static AutoPlanInput InputOf(HeightSnapshot snap, float2[] ring) => new AutoPlanInput
        {
            Snapshot = snap, Region = ring, Knobs = AutoPlanKnobs.Default(PlanGeometry.SeedOf(ring, 0)), Mode = AutoPlanMode.Resort, MaxAreaM2 = 64e6f,
        };

        /// <summary>The scorecard input of a realized plan: plain pistes as runs (Natural or Groomed), graded pistes and tracks as
        /// connectors, the planner's lifts (the realizer never moves one).</summary>
        public static ScorePlan ScoreOfRealized(AutoPlanResult res, RunsFirstResult plan) => ScoreOfRealized(res, plan, false);

        /// <summary>skied = the "as skied" feed: consecutive segments of one source run (a piste or a graded piste, never a track) are
        /// joined back into one polyline when the next segment starts within SkiedJoinM of the previous end, because a skier skis through
        /// the run that cut them. Nothing else is rejoined or restored.</summary>
        public static ScorePlan ScoreOfRealized(AutoPlanResult res, RunsFirstResult plan, bool skied)
        {
            var sp = new ScorePlan();
            var lines = new List<(AutoRun Run, List<float2> Pts, List<float2[]> Rings)>();
            foreach (AutoRun r in res.Runs)
            {
                if (skied && r.Kind != RunKind.CatTrack && r.Source >= 0 && lines.Count > 0)
                {
                    var (prev, prevPts, prevRings) = lines[lines.Count - 1];
                    if (prev.Kind != RunKind.CatTrack && prev.Source == r.Source && r.Segment == prev.Segment + 1 &&
                        math.distance(prevPts[prevPts.Count - 1], r.Centerline[0]) <= SkiedJoinM)
                    {
                        prevPts.AddRange(r.Centerline);
                        prevRings.Add(r.Ring);
                        lines[lines.Count - 1] = (r, prevPts, prevRings);
                        continue;
                    }
                }
                lines.Add((r, new List<float2>(r.Centerline), new List<float2[]> { r.Ring }));
            }
            foreach (var (r, raw, rings) in lines)
            {
                // the scorecard's windows count samples and assume the planner's 10 m step: a realized centerline (4 m) is fed at 10 m
                List<float2> pts = Resample(raw, FeedStepM);
                if (r.Graded)
                    sp.Connectors.Add(pts);
                else
                    sp.Runs.Add((pts, r.Kind == RunKind.Natural ? RunKind.Natural : RunKind.Groomed));
                // scorecard v7: the built outline(s) are rated, no ribbon (a joined run takes its hardest piece's band). A track's outline
                // is read only when its own grade makes it a graded diagonal (its ground band, grade mix)
                sp.Rings[pts] = rings.ToArray();
            }
            foreach (RunsFirstResult.Lift l in plan.Lifts)
                sp.Lifts.Add((l.Type, l.Bottom.xz, l.Top.xz));
            return sp;
        }

        /// <summary>The scorecard input of the raw plan, as RunsFirst.RunRegion builds it.</summary>
        public static ScorePlan ScoreOfRaw(RunsFirstResult plan)
        {
            var sp = new ScorePlan();
            foreach (RunsFirstResult.Run r in plan.Runs)
                if (r.Graded)
                    sp.Connectors.Add(r.Pts);
                else
                    sp.Runs.Add((r.Pts, r.Groomed ? RunKind.Groomed : RunKind.Natural));
            sp.Connectors.AddRange(plan.Connectors);
            foreach (RunsFirstResult.Lift l in plan.Lifts)
                sp.Lifts.Add((l.Type, l.Bottom.xz, l.Top.xz));
            return sp;
        }

        private const float SkiedJoinM = 60f, FeedStepM = 10f;

        /// <summary>A polyline at a fixed step along its length (first and last point kept).</summary>
        private static List<float2> Resample(List<float2> p, float step)
        {
            var o = new List<float2> { p[0] };
            float carry = 0f;
            for (int i = 1; i < p.Count; i++)
            {
                float2 a = p[i - 1], b = p[i];
                float len = math.distance(a, b);
                float t = step - carry;
                while (t <= len)
                {
                    o.Add(math.lerp(a, b, t / len));
                    t += step;
                }
                carry = len - (t - step);
            }
            if (math.distance(o[o.Count - 1], p[p.Count - 1]) > 0.5f)
                o.Add(p[p.Count - 1]);
            return o;
        }

        /// <summary>R2-y: the lifts where the planner's read of the way to the bottom and the realized plan's differ ("none").</summary>
        private static string BottomDiffer(RealizeReport rep)
        {
            var d = new List<string>();
            for (int l = 0; l < rep.LiftBottom.Length && l < rep.LiftPlanned.Length; l++)
                if ((rep.LiftPlanned[l] == RunsFirstBottom.Reaches) != rep.LiftBottom[l])
                    d.Add($"L{l + 1} planner {rep.LiftPlanned[l]}, realized {(rep.LiftBottom[l] ? "yes" : "no")}");
            return d.Count > 0 ? string.Join(", ", d) : "none";
        }

        private static Row RunRegion(string id, float2[] ring, HeightSnapshot snap, RunsFirstOptions options, string outDir)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            RunsFirstResult plan = RunsFirst.PlanRegion(snap, ring, options, id);
            double planMs = sw.Elapsed.TotalMilliseconds;
            AutoPlanInput input = InputOf(snap, ring);
            var rep = new RealizeReport();
            AutoPlanResult res = RunsFirstRealizer.Realize(plan, input, rep);
            ulong h1 = RunsFirstRealizer.Hash(res);
            ulong h2 = RunsFirstRealizer.Hash(RunsFirstRealizer.Realize(plan, InputOf(snap, ring), null));
            if (Environment.GetEnvironmentVariable("RF_RIBBONCMP") == "1")
                RibbonCompare(id, snap, plan, res, outDir);
            var scoreSw = System.Diagnostics.Stopwatch.StartNew();
            RunsFirstScore.Result raw = RunsFirstScore.Evaluate(snap, ring, ScoreOfRaw(plan));
            double scoreMs = scoreSw.Elapsed.TotalMilliseconds;
            RunsFirstScore.Result real = RunsFirstScore.Evaluate(snap, ring, ScoreOfRealized(res, plan));
            RunsFirstScore.Result skied = RunsFirstScore.Evaluate(snap, ring, ScoreOfRealized(res, plan, true));
            // the realized pistes with the raw plan connectors: which half of the plan the score loses
            ScorePlan mixed = ScoreOfRealized(res, plan);
            mixed.Connectors.Clear();
            foreach (AutoRun r in res.Runs)
                if (r.Graded && r.Kind != RunKind.CatTrack)
                    mixed.Connectors.Add(new List<float2>(r.Centerline));
            mixed.Connectors.AddRange(plan.Connectors);
            float mixT = RunsFirstScore.Evaluate(snap, ring, mixed).Total;

            int sources = rep.PlannerRuns + rep.GradedPisteSources;
            int dropped = rep.PisteDropCount;
            double kmPlan = rep.PisteKmPlanned + rep.GradedKmPlanned, kmKept = rep.PisteKmKept + rep.GradedKmKept;
            int agree = 0, rated = 0;
            double agreeKm = 0.0, ratedKm = 0.0;
            for (int p = 0; p < 5; p++)
                for (int b = 0; b < 5; b++)
                {
                    rated += rep.BandTable[p, b];
                    ratedKm += rep.BandKm[p, b];
                    if (p == b)
                    {
                        agree += rep.BandTable[p, b];
                        agreeKm += rep.BandKm[p, b];
                    }
                }
            int ends = 2 * (rep.PisteAreas + rep.GradedPisteAreas + rep.TrackAreas);
            var drops = new List<string>();
            for (int k = 1; k < (int)AutoReject.Count; k++)
                if (rep.PisteDrops[k] > 0)
                    drops.Add($"{(AutoReject)k} {rep.PisteDrops[k]}");
            var tdrops = new List<string>();
            for (int k = 1; k < (int)AutoReject.Count; k++)
                if (rep.TrackDrops[k] > 0)
                    tdrops.Add($"{(AutoReject)k} {rep.TrackDrops[k]}");
            float rawT = raw.Total, realT = real.Total, skiedT = skied.Total;
            var lines = new List<string>
            {
                $"REALIZE {id}: {res.Status}{(res.Warnings != AutoPlanWarning.None ? " (" + res.Warnings + ")" : "")}, {res.Runs.Count} areas",
                $"  pistes: {rep.PlannerRuns} plain + {rep.GradedPisteSources} graded sources, {rep.PisteAreas} + {rep.GradedPisteAreas} areas, " +
                    $"{sources - dropped} sources kept, {dropped} dropped ({(drops.Count > 0 ? string.Join(", ", drops) : "none")}), {rep.PiecesLost} pieces lost, " +
                    $"km {kmKept:F2} / {kmPlan:F2} ({(kmPlan > 0 ? kmKept / kmPlan * 100 : 100):F1} %), graded km {rep.GradedKmKept:F2} / {rep.GradedKmPlanned:F2}",
                $"  bands: planned = shown {agree}/{rated} areas ({(rated > 0 ? 100.0 * agree / rated : 100):F0} %), {agreeKm:F2}/{ratedKm:F2} km; graded shown G/B/K/DB " +
                    $"{rep.GradedShown[1]}/{rep.GradedShown[2]}/{rep.GradedShown[3]}/{rep.GradedShown[4]}; natural to groomed {rep.NaturalToGroomed}; rated by the fallback {rep.FallbackRated}; constant-width after Crosses {rep.CrossesFallback}; table {BandTable(rep)}",
                $"  widths: {KeepLine(rep)}",
                $"  tracks: {rep.TrackSources} sources, {rep.TrackChains} chains, {rep.TrackAreas} areas, {rep.TrackPiecesLost} pieces lost, dropped " +
                    $"({(tdrops.Count > 0 ? string.Join(", ", tdrops) : "none")}), km {rep.TrackKmKept:F2} / {rep.TrackKmPlanned:F2}, 30 m pitches over 25 % {rep.TrackPitchesOver}/{rep.TrackPitches}; " +
                    $"merged {rep.TrackMerged} chains ({rep.TrackKmMerged:F2} km), planned km built {rep.TrackKmOwn:F2} + carried by another track {rep.TrackKmShared:F2} " +
                    $"({(rep.TrackKmPlanned > 0 ? 100 * (rep.TrackKmOwn + rep.TrackKmShared) / rep.TrackKmPlanned : 100):F1} %), merged chain relations linked {rep.MergedLinks}/{rep.MergedRelations}, self-splits {rep.TrackSelfSplits}, spurs {rep.TrackSpurs}",
                $"  junctions: relations {rep.Relations}, linked {rep.RelationLinks} ({rep.RelationVia} through the area that cut the end), missed {rep.RelationMisses}; extensions {rep.Extensions}, track ends reached {rep.TrackReaches}, piste ends reached {rep.PisteReaches}, remnants kept {rep.RemnantsKept} dropped {rep.RemnantsDropped}, converging junctions {rep.ConvergeJunctions}; unlinked ends " +
                    $"{rep.UnlinkedEnds} ({rep.UnlinkedBlocked} blocked) of {ends} ({(ends > 0 ? 100.0 * rep.UnlinkedEnds / ends : 0):F2} %)",
                $"  graph: unserved {rep.UnservedAreas}, dead ends {rep.DeadEnds}, bottom reach {rep.BottomReach}/{rep.Lifts}, no way to bottom {rep.NoWayToBottom}; " +
                    $"way to the bottom: planner {plan.Lifts.Count(l => l.ToBottom == RunsFirstBottom.Reaches)}/{plan.Lifts.Count(l => l.ToBottom != RunsFirstBottom.NoSkiing)} " +
                    $"({string.Join(", ", plan.Lifts.Where(l => l.ToBottom != RunsFirstBottom.Reaches).Select(l => $"L{l.Id} {l.ToBottom}"))})",
                $"  bottom: lifts with a way down {rep.BottomWayDown}, reach the base band by the planner {rep.BottomPlanner}, realized {rep.BottomRealized}; " +
                    $"lost {rep.BottomLost}, gained {rep.BottomGained}; differ: {BottomDiffer(rep)}",
                $"  lifts: {rep.Lifts}, tower plan sound {rep.LiftsClear}, hand fit chair/detachable/gondola/tram {rep.HandFitByType[0]}/{rep.HandFitByType[1]}/{rep.HandFitByType[2]}/{rep.HandFitByType[3]} " +
                    $"(spans {rep.HandFitSpans}, over cap {rep.HandFitOverCap}, too steep {rep.HandFitSteep}, clearance {rep.HandFitClearance}, edge slope {rep.HandFitEdge}, miss {rep.HandFitMiss}), sunk ends {rep.SunkEnds}, lot conflicts {rep.LotConflicts}",
                $"  score: raw {rawT:F1}, as built {realT:F1} ({realT - rawT:+0.0;-0.0}), as skied {skiedT:F1} ({skiedT - rawT:+0.0;-0.0}), realized pistes with the raw connectors {mixT:F1}; " +
                    $"scorecard v{RunsFirstScore.Version}, raw scored in {scoreMs:F0} ms",
                "  " + RunsFirstScore.ShapeLine(id + " raw", raw),
                $"  time: plan {planMs:F0} ms, realize {rep.Ms:F0} ms (lattice {rep.LatticeMs:F0}, lifts {rep.LiftMs:F0}, pistes {rep.PisteMs:F0}, tracks {rep.TrackMs:F0}, graph {rep.GraphMs:F0}); " +
                    $"layers {rep.LayerBytes / 1048576.0:F1} MB; hash {h1:x16}{(h1 == h2 ? "" : " NOT DETERMINISTIC " + h2.ToString("x16"))}",
            };
            foreach (string l in lines)
                Console.WriteLine(l);
            if (Environment.GetEnvironmentVariable("RZ_DEBUG") == "1")
                foreach (AutoRun r in res.Runs)
                {
                    List<float2> src = r.Source >= 0 ? plan.Runs[r.Source].Pts : plan.Connectors[-1 - r.Source];
                    float2 a = r.Centerline[0], b = r.Centerline[r.Centerline.Length - 1];
                    Console.WriteLine($"  debug area src {r.Source} seg {r.Segment} {(r.Graded ? "graded" : "piste")} {r.Band} start {math.distance(a, src[0]):F0} m from the source start " +
                        $"end {math.distance(b, src[src.Count - 1]):F0} m from the source end, {r.LengthM:F0} m, top {r.TopY:F0} bottom {r.BottomY:F0}, " +
                        $"line y {snap.Sample(new float3(a.x, 0, a.y)):F0} -> {snap.Sample(new float3(b.x, 0, b.y)):F0}");
                }
            File.WriteAllLines(Path.Combine(outDir, id + ".txt"), lines.Concat(rep.Lines.Select(s => "  | " + s))
                .Concat(new[] { "", RunsFirstScore.Line(id + " raw", raw), RunsFirstScore.ShapeLine(id + " raw", raw), "", RunsFirstScore.Line(id + " realized", real), "", RunsFirstScore.Line(id + " skied", skied) })
                .Concat(raw.Notes.Select(s => "  raw note: " + s)).Concat(real.Notes.Select(s => "  realized note: " + s)));
            string tsv = Path.Combine(outDir, "realize.tsv");
            if (!File.Exists(tsv))
                File.WriteAllText(tsv, "region\tsources\tdropped\tdrops\tareas\tpiecesLost\tkmPlan\tkmKept\tgradedKmPlan\tgradedKmKept\trelations\tlinks\tvia\tmisses\textensions\t" +
                    "unlinked\tunlinkedBlocked\tends\tunserved\tdeadEnds\tbandAgree\tbandRated\tbandAgreeKm\tbandRatedKm\ttrackSources\ttrackChains\ttrackAreas\t" +
                    "trackPiecesLost\ttrackKmPlan\ttrackKmKept\ttrackDrops\tlifts\tliftsClear\tbottomReach\traw\trealized\tplanMs\trealizeMs\tlayerMB\tdeterministic\tfallback\tskied\tcrossesFallback\ttrackReaches\tpisteReaches\tremnantsKept\tremnantsDropped\tconverging\tagreeKmFull\tagreeKmKept\tplainKm\t" +
                    "greenKeep\tblueKeep\tblackKeep\tdbKeep\tgreenWidth\tblueWidth\tgreenFullWidth\tblueFullWidth\thandFitChair\thandFitDetachable\thandFitGondola\thandFitTram\thandFitSpans\thandFitOverCap\thandFitSteep\thandFitClearance\thandFitEdge\thandFitMiss\ttrackMerged\ttrackKmMerged\ttrackKmShared\tmergedRelations\tmergedLinks\ttrackKmOwn\tselfSplits\tspurs\tbottomWayDown\tbottomPlanner\tbottomRealized\tbottomLost\tbottomGained\n");
            File.AppendAllText(tsv, string.Join("\t", new object[]
            {
                id, sources, dropped, drops.Count > 0 ? string.Join(",", drops) : "-", res.Runs.Count, rep.PiecesLost, kmPlan.ToString("F2"), kmKept.ToString("F2"),
                rep.GradedKmPlanned.ToString("F2"), rep.GradedKmKept.ToString("F2"), rep.Relations, rep.RelationLinks, rep.RelationVia, rep.RelationMisses, rep.Extensions,
                rep.UnlinkedEnds, rep.UnlinkedBlocked, ends, rep.UnservedAreas, rep.DeadEnds, agree, rated, agreeKm.ToString("F2"), ratedKm.ToString("F2"),
                rep.TrackSources, rep.TrackChains, rep.TrackAreas, rep.TrackPiecesLost, rep.TrackKmPlanned.ToString("F2"), rep.TrackKmKept.ToString("F2"),
                tdrops.Count > 0 ? string.Join(",", tdrops) : "-", rep.Lifts, rep.LiftsClear, rep.BottomReach, rawT.ToString("F1"), realT.ToString("F1"),
                planMs.ToString("F0"), rep.Ms.ToString("F0"), (rep.LayerBytes / 1048576.0).ToString("F1"), h1 == h2 ? "yes" : "NO", rep.FallbackRated, skiedT.ToString("F1"), rep.CrossesFallback, rep.TrackReaches,
                rep.PisteReaches, rep.RemnantsKept, rep.RemnantsDropped, rep.ConvergeJunctions, rep.AgreeKmFull.ToString("F2"), rep.AgreeKmKept.ToString("F2"), rep.PlainKm.ToString("F2"),
                Keep(rep, 1), Keep(rep, 2), Keep(rep, 3), Keep(rep, 4), Width(rep, 1, false), Width(rep, 2, false), Width(rep, 1, true), Width(rep, 2, true),
                rep.HandFitByType[0], rep.HandFitByType[1], rep.HandFitByType[2], rep.HandFitByType[3], rep.HandFitSpans, rep.HandFitOverCap, rep.HandFitSteep,
                rep.HandFitClearance, rep.HandFitEdge, rep.HandFitMiss, rep.TrackMerged, rep.TrackKmMerged.ToString("F2"), rep.TrackKmShared.ToString("F2"),
                rep.MergedRelations, rep.MergedLinks, rep.TrackKmOwn.ToString("F2"), rep.TrackSelfSplits, rep.TrackSpurs,
                rep.BottomWayDown, rep.BottomPlanner, rep.BottomRealized, rep.BottomLost, rep.BottomGained,
            }.Select(o => Convert.ToString(o, CultureInfo.InvariantCulture))) + "\n");
            return new Row
            {
                KeptShare = kmPlan > 0 ? kmKept / kmPlan : 1.0, UnlinkedShare = ends > 0 ? (double)rep.UnlinkedEnds / ends : 0.0, Raw = rawT, Realized = realT, Skied = skiedT,
                PlanMs = planMs, RealizeMs = rep.Ms, LayerMB = rep.LayerBytes / 1048576.0, Same = h1 == h2,
            };
        }

        // Scorecard v7's ribbon choice (RF_RIBBONCMP=1): the half widths measured against the realized outlines
        private static readonly float[] CmpHalfWidths = { 7.5f, 10f, 12.5f, 15f, 17.5f, 20f };
        private const byte NotRated = 255;

        /// <summary>RF_RIBBONCMP=1: for every realized piste area, the band the rater gives its built outline (what the game shows)
        /// against the band each candidate rule gives the RAW plan's run it came from: the planner's own band, the v6 rule, a ribbon of
        /// each half width in CmpHalfWidths, and a ribbon at the half target width of the planner's band (PlanGeometry.TargetWidthM).
        /// The same half widths are also built on the area's own realized centerline (same extent, width only). Km of the areas,
        /// agreeing / candidate harder / candidate easier / not rated. Graded pistes: the v6 rule and GradedHalfWidthM ribbons on the
        /// raw run and on the realized centerline. Writes one row per region to ribbon.tsv and prints one line.</summary>
        private static void RibbonCompare(string id, HeightSnapshot snap, RunsFirstResult plan, AutoPlanResult res, string outDir)
        {
            var grid = new SlopeGrid();
            var rr = new SlopeResult();
            byte Rate(float2[] ring) => RunsFirstScore.RateRing(snap, ring, grid, rr, out SlopeBand b) ? (byte)b : NotRated;
            byte Shown(SlopeBand b) => (byte)(b == SlopeBand.Flat ? SlopeBand.Green : b);
            int nw = CmpHalfWidths.Length, nc = 3 + nw;   // planner, v6, widths..., target width
            var names = new List<string> { "planner", "v6" };
            names.AddRange(CmpHalfWidths.Select(w => "w" + w.ToString("0.#", CultureInfo.InvariantCulture)));
            names.Add("target");
            names.AddRange(CmpHalfWidths.Select(w => "local-w" + w.ToString("0.#", CultureInfo.InvariantCulture)));
            names.AddRange(new[] { "graded-v6", "graded-w7.5", "graded-local-w7.5" });
            int all = names.Count, g0 = nc + nw;
            var agree = new double[all];
            var harder = new double[all];
            var easier = new double[all];
            var unrated = new double[all];
            double km = 0.0, gkm = 0.0;
            int rerated = 0, rerateDiffers = 0;   // the scorecard's RateRing on the built ring against the realizer's AutoRun.Band
            var memo = new Dictionary<int, byte[]>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            void Count(int c, byte cand, byte shown, double k)
            {
                if (cand == NotRated)
                    unrated[c] += k;
                else if (cand == shown)
                    agree[c] += k;
                else if (cand > shown)
                    harder[c] += k;
                else
                    easier[c] += k;
            }
            foreach (AutoRun a in res.Runs)
            {
                if (a.Kind == RunKind.CatTrack || a.Source < 0 || a.Source >= plan.Runs.Count)
                    continue;
                RunsFirstResult.Run src = plan.Runs[a.Source];
                byte shown = Shown(a.Band);
                rerated++;
                if (Rate(a.Ring) != shown)
                    rerateDiffers++;
                double k = a.LengthM / 1000.0;
                if (!memo.TryGetValue(a.Source, out byte[] bands))
                {
                    byte v6 = Shown(RunsFirstScore.GroundBandV6(snap, RunsFirstScore.SamplesOf(snap, src.Pts)));
                    if (a.Graded)
                        bands = new[] { v6, Rate(RunsFirstScore.RibbonOf(src.Pts, RunsFirstScore.GradedHalfWidthM)) };
                    else
                    {
                        bands = new byte[nc];
                        bands[0] = Shown(src.Band);
                        bands[1] = v6;
                        for (int w = 0; w < nw; w++)
                            bands[2 + w] = Rate(RunsFirstScore.RibbonOf(src.Pts, CmpHalfWidths[w]));
                        int tb = Math.Max(0, Math.Min(3, (int)src.Band - 1));
                        bands[nc - 1] = Rate(RunsFirstScore.RibbonOf(src.Pts, 0.5f * PlanGeometry.TargetWidthM[tb]));
                    }
                    memo[a.Source] = bands;
                }
                if (a.Graded)
                {
                    gkm += k;
                    Count(g0, bands[0], shown, k);
                    Count(g0 + 1, bands[1], shown, k);
                    Count(g0 + 2, Rate(RunsFirstScore.RibbonOf(a.Centerline, RunsFirstScore.GradedHalfWidthM)), shown, k);
                    continue;
                }
                km += k;
                for (int c = 0; c < nc; c++)
                    Count(c, bands[c], shown, k);
                for (int w = 0; w < nw; w++)
                    Count(nc + w, Rate(RunsFirstScore.RibbonOf(a.Centerline, CmpHalfWidths[w])), shown, k);
            }
            string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
            var parts = new List<string>();
            for (int c = 0; c < all; c++)
            {
                double tot = c >= g0 ? gkm : km;
                parts.Add($"{names[c]} {(tot > 0 ? 100 * agree[c] / tot : 0):F0} %");
            }
            Console.WriteLine($"RIBBON {id}: {km:F2} km plain, {gkm:F2} km graded, agree {string.Join(", ", parts)}; " +
                $"built rings re-rated by the scorecard {rerated}, band differs {rerateDiffers}; {sw.Elapsed.TotalMilliseconds:F0} ms");
            string tsv = Path.Combine(outDir, "ribbon.tsv");
            if (!File.Exists(tsv))
                File.WriteAllText(tsv, "region\tplainKm\tgradedKm\t" + string.Join("\t", names.Select(n => $"{n}.agree\t{n}.harder\t{n}.easier\t{n}.unrated")) + "\n");
            File.AppendAllText(tsv, string.Join("\t", new[] { id, F(km), F(gkm) }.Concat(Enumerable.Range(0, all).Select(c => $"{F(agree[c])}\t{F(harder[c])}\t{F(easier[c])}\t{F(unrated[c])}"))) + "\n");
        }

        private static string BandTable(RealizeReport rep)
        {
            var parts = new List<string>();
            for (int p = 1; p < 5; p++)
            {
                var c = new List<string>();
                for (int b = 0; b < 5; b++)
                    c.Add(rep.BandTable[p, b].ToString(CultureInfo.InvariantCulture));
                parts.Add(((SlopeBand)p).ToString()[0] + ":" + string.Join("/", c));
            }
            return string.Join(" ", parts) + " (planned: shown F/G/B/K/DB)";
        }
    }
}
