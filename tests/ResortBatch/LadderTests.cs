// Tests for the lodge ladder, school, patrol and per-type lift rows of the pure score, and the lodge-spend ledger; where-tags `ladder-...`.
// Oracle rows use K 4, efficiency 1, backcountry 40. Sections: A ladder rows (LX adds several luxury lodges to one area, only the extras cap
// of 24 limits), B per-type lifts (winter / summer), C school and patrol, D order independence (every permutation of LD7 and of a summer set,
// plus 2,000 random lists against an independent reference ladder in this file), E A-frame closed form (explicit A-frame lists equal the
// Lodges == null path bit for bit; summer with no lodge), F ledger lodge spend (create, ignore negatives, roll, jump, prune, sum).
// `public static class` with `Run(Func<bool, string, string, bool> check)`; Main passes its Check.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Score = SkiResortMod.Systems.SkiResortScore;

namespace ResortBatch
{
    public static class LadderTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private const byte Gr = Score.KindGroomed, Sm = Score.KindSnowmaking, Ni = Score.KindNight, Na = Score.KindNatural;
        private const byte Green = Score.BandGreen, Blue = Score.BandBlue, Black = Score.BandBlack, DBlack = Score.BandDoubleBlack;

        // Catalog row indices are read from the catalog, never literals
        private static int AFrame => SkiCatalog.LodgeByPrefab(SkiCatalog.Names.LodgeAFrame).Index;
        private static int Hut => SkiCatalog.LodgeByPrefab(SkiCatalog.Names.LodgeHut).Index;
        private static int Chalet => SkiCatalog.LodgeByPrefab(SkiCatalog.Names.LodgeMid).Index;
        private static int Summit => SkiCatalog.LodgeByPrefab(SkiCatalog.Names.LodgeSummit).Index;
        private static int Grand => SkiCatalog.LodgeByPrefab(SkiCatalog.Names.LodgeGrand).Index;
        private static int FG => SkiCatalog.LiftById("fixedGrip").Index;
        private static int DQ => SkiCatalog.LiftById("detachable").Index;
        private static int GD => SkiCatalog.LiftById("gondola").Index;
        private static int TR => SkiCatalog.LiftById("tram").Index;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            LadderRows();
            LiftTypeRows();
            SchoolPatrolRows();
            OrderFree();
            ClosedForm();
            LedgerSpend();
        }

        private static bool Check(bool cond, string where, string msg)
        {
            return s_Check(cond, where, msg);
        }

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        private static string R(double x)
        {
            return x.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Compositions (copied from ScoreTests)

        private static Score.RunInput Run(byte kind, byte band, double areaM2, double topM, double bottomM)
        {
            return new Score.RunInput
            {
                Kind = kind,
                State = Score.BandState.Rated,
                Band = band,
                AreaM2 = areaM2,
                Served = true,
                OpenFactor = 1.0,
                TopM = topM,
                BottomM = bottomM,
            };
        }

        private static void Add(List<Score.RunInput> runs, int count, Score.RunInput run)
        {
            for (int i = 0; i < count; i++)
                runs.Add(run);
        }

        private static List<Score.RunInput> Small()
        {
            return new List<Score.RunInput>
            {
                Run(Gr, Green, 20000, 1150, 1000),
                Run(Gr, Blue, 25000, 1250, 1000),
                Run(Gr, Blue, 20000, 1250, 1050),
            };
        }

        private static List<Score.RunInput> Medium()
        {
            var runs = new List<Score.RunInput>();
            Add(runs, 3, Run(Gr, Green, 25000, 1250, 1000));   // 0-2
            Add(runs, 3, Run(Gr, Blue, 30000, 1400, 1000));    // 3-5
            Add(runs, 1, Run(Sm, Blue, 30000, 1400, 1000));    // 6
            Add(runs, 1, Run(Ni, Blue, 25000, 1300, 1000));    // 7
            Add(runs, 2, Run(Na, Black, 30000, 1500, 1100));   // 8-9
            return runs;
        }

        private static List<Score.RunInput> Large()
        {
            var runs = new List<Score.RunInput>();
            Add(runs, 6, Run(Gr, Green, 30000, 1300, 1000));
            Add(runs, 5, Run(Gr, Blue, 40000, 1500, 1000));
            Add(runs, 3, Run(Sm, Blue, 40000, 1500, 1000));
            Add(runs, 2, Run(Ni, Blue, 30000, 1300, 1000));
            Add(runs, 5, Run(Na, Black, 40000, 1800, 1200));
            Add(runs, 1, Run(Sm, Black, 40000, 1700, 1100));
            Add(runs, 3, Run(Na, DBlack, 40000, 1800, 1300));
            return runs;
        }

        /// <summary>Summer: every open factor 0 (no counted run).</summary>
        private static List<Score.RunInput> Summer(List<Score.RunInput> runs)
        {
            var copy = new List<Score.RunInput>(runs.Count);
            for (int i = 0; i < runs.Count; i++)
            {
                Score.RunInput r = runs[i];
                r.OpenFactor = 0;
                copy.Add(r);
            }
            return copy;
        }

        private static List<Score.RunInput> With(List<Score.RunInput> runs, bool? served, bool? patrolled, params int[] indices)
        {
            var copy = new List<Score.RunInput>(runs);
            for (int i = 0; i < indices.Length; i++)
            {
                Score.RunInput r = copy[indices[i]];
                if (served.HasValue)
                    r.Served = served.Value;
                if (patrolled.HasValue)
                    r.Patrolled = patrolled.Value;
                copy[indices[i]] = r;
            }
            return copy;
        }

        private static List<Score.LiftInput> Lifts(params int[] types)
        {
            var lifts = new List<Score.LiftInput>();
            for (int i = 0; i < types.Length; i++)
                lifts.Add(new Score.LiftInput { Complete = true, Powered = true, TypeIndex = types[i] });
            return lifts;
        }

        private static Score.LodgeInput L(int index, bool served = false)
        {
            return new Score.LodgeInput { LodgeIndex = index, SummitServed = served };
        }

        private static List<Score.LodgeInput> Lodges(params Score.LodgeInput[] lodges)
        {
            return new List<Score.LodgeInput>(lodges);
        }

        private static List<Score.LodgeInput> AFrames(int n)
        {
            var list = new List<Score.LodgeInput>();
            for (int i = 0; i < n; i++)
                list.Add(L(AFrame));
            return list;
        }

        private static Score.AreaInput Area(List<Score.LodgeInput> lodges, bool school = false, int backcountry = 40)
        {
            return new Score.AreaInput
            {
                ActiveLodges = lodges != null ? lodges.Count : 0,
                InactiveLodges = 0,
                Efficiency = 1.0,
                Settings = new Score.ScoreSettings { K = 4, BackcountryPercent = backcountry, SnowGating = true },
                Lodges = lodges,
                SchoolActive = school,
            };
        }

        /// <summary>One row: S and AOpen within 1e-6; LodgeBase, LodgeApplied, A, full and PerLodge exact (efficiency 1).</summary>
        private static Score.Result Row(string id, Score.AreaInput area, List<Score.RunInput> runs, List<Score.LiftInput> lifts,
                                        double s, double lodgeBase, double applied, double aOpen, int a, int perLodge,
                                        double appliedTol = 0.0)
        {
            string w = "ladder-row " + id;
            Score.Result r = Score.Compute(area, runs, lifts, new List<Score.RunPart>());
            Check(Near(r.S, s, 1e-6), w, $"S {R(r.S)}, expected {s}");
            Check(r.LodgeBase == lodgeBase, w, $"LodgeBase {R(r.LodgeBase)}, expected {lodgeBase}");
            Check(Math.Abs(r.LodgeApplied - applied) <= appliedTol, w, $"LodgeApplied {R(r.LodgeApplied)}, expected {applied}");
            Check(Near(r.AOpen, aOpen, 1e-6), w, $"AOpen {R(r.AOpen)}, expected {aOpen}");
            Check(r.Attraction == a && r.AttractionFullEfficiency == a, w, $"A {r.Attraction} (full {r.AttractionFullEfficiency}), expected {a}");
            Check(r.PerLodge == perLodge, w, $"PerLodge {r.PerLodge}, expected {perLodge}");
            Check(r.ActiveLodges == (area.Lodges != null ? area.Lodges.Count : area.ActiveLodges), w, $"ActiveLodges {r.ActiveLodges}");
            double share = r.LodgeBase > 0 ? r.LodgeApplied / r.LodgeBase : (r.RunsCounted == 0 ? 0.5 : 1.0);
            Check(r.LodgeShare == share && Near(r.LodgeApplied + r.K * Math.Sqrt(r.S), r.AOpen, 0.0), w,
                  $"LodgeShare {R(r.LodgeShare)} = applied / base, AOpen = applied + K √S exactly");
            return r;
        }

        private static void LadderRows()
        {
            const string w = "ladder-row";
            // L5 (5 A-frames: 20 + 16) and the LodgeBaseOf extension
            Row("L5", Area(AFrames(5)), Small(), Lifts(FG), 52.138032112, 36, 36, 64.882668052, 65, 29);
            Check(Score.LodgeBaseOf(5) == 36 && Score.LodgeBaseOf(7) == 44 && Score.LodgeBaseOf(13) == 44 && Score.LodgeBaseOf(6) == 40,
                  w, "LodgeBaseOf 5 / 6 / 7 / 13 = 36 / 40 / 44 / 44");

            Score.Result ld1 = Row("LD1", Area(Lodges(L(Chalet))), Small(), Lifts(FG), 52.138032112, 30, 30, 58.882668052, 59, 59);
            Check(ld1.LodgeBestIndex == Chalet && ld1.PriceTolerance == 20 && ld1.LodgeExtraApplied == 0, w + " LD1", "best Chalet, tolerance 20, extras 0");
            Score.Result ld2 = Row("LD2", Area(Lodges(L(Grand))), Small(), Lifts(FG), 52.138032112, 40, 40, 68.882668052, 69, 69);
            Check(ld2.LodgeBestIndex == Grand && ld2.PriceTolerance == 40, w + " LD2", "best Grand, tolerance 40 (Grand area)");
            Row("LD3", Area(Lodges(L(Chalet), L(AFrame))), Medium(), Lifts(FG, FG, FG), 320.284085025, 34, 34, 105.585929905, 106, 75);
            Score.Result ld4 = Row("LD4", Area(Lodges(L(Grand), L(AFrame))), Medium(), Lifts(FG, FG, FG), 320.284085025, 44, 44, 115.585929905, 116, 82);
            Check(ld4.LodgeExtraApplied == 4 && ld4.PriceTolerance == 40, w + " LD4", "extras 4, tolerance 40");

            // LD5: Grand + Summit both count; they tie in winter on B·μ 40 and E 12, the Grand's capacity 1,080 wins and the
            // Summit adds its extra 12 → 52.
            List<Score.LodgeInput> ld5Lodges = Lodges(L(Grand), L(Summit));
            Score.Result ld5 = Row("LD5", Area(ld5Lodges), Medium(), Lifts(FG, FG, FG), 320.284085025, 52, 52, 123.585929905, 124, 88);
            Check(ld5.LodgeBestIndex == Grand && ld5.PriceTolerance == 40 && ld5.LodgeExtraApplied == 12 && ld5.ActiveLodges == 2, w + " LD5",
                  "winter keeps the Grand (capacity tie-break), the Summit adds 12, tolerance 40");

            Row("LD6", Area(Lodges(L(Grand), L(AFrame), L(AFrame))), Large(), Lifts(FG, FG, FG, FG, FG, FG), 1086.546347399, 48, 48,
                179.851209924, 180, 104);
            List<Score.LodgeInput> ld7Lodges = Lodges(L(Grand), L(Summit), L(Chalet), L(AFrame));
            Score.Result ld7 = Row("LD7", Area(ld7Lodges), Large(), Lifts(FG, FG, FG, FG, FG, FG), 1086.546347399, 64, 64,
                                   195.851209924, 196, 98);
            Check(ld7.LodgeBestIndex == Grand && ld7.LodgeExtraApplied == 24 && ld7.PriceTolerance == 40, w + " LD7",
                  "best Grand, extras Summit 12 + Chalet 8 + A-frame 4 = 24 (exactly the cap), tolerance 40");
            Score.Result ld8 = Row("LD8", Area(Lodges(L(Hut))), Small(), Lifts(FG), 52.138032112, 16, 16, 44.882668052, 45, 45);
            Check(ld8.LodgeBestIndex == Hut && ld8.PriceTolerance == 0, w + " LD8", "best Hut, tolerance 0");

            // Summer (Medium with every open factor 0: S = 3 lifts × 1.5 = 4.5). Two selections: LodgeBase is winter's,
            // LodgeApplied / LodgeBestIndex are the season's.
            List<Score.RunInput> ms = Summer(Medium());
            Score.Result ls1 = Row("LS1", Area(Lodges(L(Summit, true))), ms, Lifts(FG, FG, FG), 4.5, 40, 40, 48.485281374, 48, 48);
            Check(ls1.LodgeShare == 1.0 && ls1.LodgeBestIndex == Summit, w + " LS1", "summit-served summit lodge: μ 1, share 1");
            Score.Result ls2 = Row("LS2", Area(Lodges(L(Summit, false))), ms, Lifts(FG, FG, FG), 4.5, 40, 20, 28.485281374, 28, 28);
            Check(ls2.LodgeShare == 0.5, w + " LS2", "not summit-served: μ 0.5");
            Score.Result ls3 = Row("LS3", Area(Lodges(L(Grand), L(AFrame))), ms, Lifts(FG, FG, FG), 4.5, 44, 26, 34.485281374, 34, 24);
            Check(ls3.LodgeExtraApplied == 2 && ls3.LodgeBestIndex == Grand, w + " LS3", "Grand 40 × 0.6 = 24 + A-frame 4 × 0.5 = 2");
            Score.Result ls4 = Row("LS4", Area(Lodges(L(Hut, true), L(AFrame))), ms, Lifts(FG, FG, FG), 4.5, 24, 14, 22.485281374, 22, 16);
            Check(ls4.LodgeBestIndex == Hut, w + " LS4", "summer best = the served Hut (16 × 0.75 = 12 > 10); winter base from the A-frame (24)");
            Score.Result ls5 = Row("LS5", Area(Lodges(L(Grand), L(Summit, true))), ms, Lifts(FG, FG, FG), 4.5, 52, 47.2, 55.685281374, 56, 40);
            Check(ls5.LodgeBestIndex == Summit && ls5.PriceTolerance == 40 && Near(ls5.LodgeExtraApplied, 7.2, 1e-12), w + " LS5",
                  "summer: the served Summit is best (40 × 1 > 40 × 0.6), the Grand adds 12 × 0.6 = 7.2 (was ignored: applied 40, A 48); tolerance 40");
            Score.Result ls6 = Row("LS6", Area(AFrames(13)), Summer(Small()), Lifts(FG), 1.5, 44, 22, 26.898979486, 27, 7);
            Check(ls6.LodgeExtraApplied == 12 && ls6.LodgeShare == 0.5, w + " LS6", "13 A-frames in summer: extras 24 × 0.5 = 12 exactly, share 0.5");

            // LX1-LX6: no limit on luxury lodges. Expected values from an independent hand/JS ladder
            // (winter: B(best) + min(Σ E(others), 24); summer: B·μ(best) + the others' E·μ, scaled by 24 / Σ E past 24).
            // Three Grands: 40 + 12 + 12 = 64, the cap reached; a fourth, fifth … adds nothing and is not refused.
            Score.Result lx1 = Row("LX1", Area(Lodges(L(Grand), L(Grand), L(Grand))), Medium(), Lifts(FG, FG, FG), 320.284085025, 64, 64,
                                   135.585929905, 136, 79);
            Check(lx1.LodgeBestIndex == Grand && lx1.LodgeExtraApplied == 24 && lx1.ActiveLodges == 3 && lx1.PriceTolerance == 40, w + " LX1",
                  "three Grands all count: best Grand, extras 12 + 12 = 24, tolerance 40");
            Row("LX1b", Area(Lodges(L(Grand), L(Grand))), Medium(), Lifts(FG, FG, FG), 320.284085025, 52, 52, 123.585929905, 124, 88);
            Score.Result lx2 = Row("LX2", Area(Lodges(L(Summit), L(Summit), L(Summit))), Medium(), Lifts(FG, FG, FG), 320.284085025, 64, 64,
                                   135.585929905, 136, 79);
            Check(lx2.LodgeBestIndex == Summit && lx2.LodgeExtraApplied == 24, w + " LX2", "three Summits all count: best Summit, extras 24");
            Score.Result lx3 = Row("LX3", Area(Lodges(L(Grand), L(Grand), L(Grand), L(Grand), L(Grand), L(Summit), L(Summit), L(Summit))),
                                   Large(), Lifts(FG, FG, FG, FG, FG, FG), 1086.546347399, 64, 64, 195.851209924, 196, 69);
            Check(lx3.ActiveLodges == 8 && lx3.LodgeExtraApplied == 24, w + " LX3", "five Grands and three Summits: eight lodges, the extras cap 24 holds");
            Score.Result lx4 = Row("LX4", Area(Lodges(L(Grand), L(Grand), L(Grand))), ms, Lifts(FG, FG, FG), 4.5, 64, 38.4, 46.885281374, 47, 27,
                                   appliedTol: 1e-9);
            Check(lx4.LodgeBestIndex == Grand && Near(lx4.LodgeExtraApplied, 14.4, 1e-9), w + " LX4",
                  "three Grands in summer: 40 × 0.6 = 24 + 2 × 12 × 0.6 = 14.4 (Σ E = 24, not scaled)");
            Row("LX5", Area(Lodges(L(Summit, true), L(Summit, true))), ms, Lifts(FG, FG, FG), 4.5, 52, 52, 60.485281374, 60, 42);
            Score.Result lx6 = Row("LX6", Area(Lodges(L(Grand), L(Grand), L(Grand), L(Grand), L(Summit, true), L(Summit, true))), ms,
                                   Lifts(FG, FG, FG), 4.5, 64, 56.32, 64.805281374, 65, 27, appliedTol: 1e-9);
            Check(lx6.LodgeBestIndex == Summit && Near(lx6.LodgeExtraApplied, 16.32, 1e-9), w + " LX6",
                  "summer, four Grands + two served Summits: best a served Summit (40), Σ E 60 > 24, Σ E·μ 40.8 scaled to 16.32");
        }

        private static void LiftTypeRows()
        {
            const string w = "ladder-lift";
            List<Score.LodgeInput> two = AFrames(2);
            Score.Result pt1 = Row("PT1", Area(two), Medium(), Lifts(FG, FG, DQ), 339.259085025, 24, 24, 97.675948317, 98, 69);
            Check(pt1.LiftsValue == 27 && pt1.LiftsApplied == 27 && pt1.LiftShare == 1.0 && pt1.LiftsCounted == 3, w + " PT1",
                  $"LiftsValue / applied 27 (6 + 6 + 15), share 1 (got {R(pt1.LiftsValue)} / {R(pt1.LiftsApplied)} / {R(pt1.LiftShare)})");
            Row("PT2", Area(two), Medium(), Lifts(FG, FG, GD), 360.342418359, 24, 24, 99.930749330, 100, 71);
            Score.Result pt3 = Row("PT3", Area(two), Medium(), Lifts(DQ, GD, TR), 429.917418359, 24, 24, 106.937800150, 107, 76);
            Check(pt3.LiftsValue == 70, w + " PT3", "LiftsValue 15 + 25 + 30 = 70");

            List<Score.RunInput> ms = Summer(Medium());
            Score.Result ps1 = Row("PS1", Area(two), ms, Lifts(GD, FG, FG), 9.25, 24, 12, 24.165525061, 24, 17);
            Check(ps1.LiftsValue == 37 && ps1.LiftsApplied == 9.25 && ps1.LiftShare == 9.25 / 37.0, w + " PS1",
                  $"gondola λ 0.25 (DECISIONS 376): applied 6.25 + 1.5 + 1.5 = 9.25 of 37 (got {R(ps1.LiftsApplied)})");
            Score.Result ps2 = Row("PS2", Area(two), ms, Lifts(TR, FG, FG), 33, 24, 12, 34.978250586, 35, 25);
            Check(ps2.LiftsApplied == 33, w + " PS2", "tram λ 1: 30 + 1.5 + 1.5 = 33");
            Row("PS3", Area(two), ms, Lifts(DQ, FG, FG), 6.75, 24, 12, 22.392304845, 22, 16);

            // Fixed-grip only: the per-type sums equal 6 × n and 0.25 × 6 × n exactly
            Score.Result fgW = Score.Compute(Area(two), ms, Lifts(FG, FG, FG), null);
            Check(fgW.LiftsValue == 18 && fgW.LiftsApplied == 4.5 && fgW.LiftShare == 0.25, w + " FG", "summer FG: 18 / 4.5 / 0.25 exactly");
            // An unknown type index (the census's NoType 255) reads the fixed-grip row.
            Score.Result unknown = Score.Compute(Area(two), Medium(), Lifts(255, 255, 255), null);
            Score.Result t2 = Score.Compute(Area(two), Medium(), Lifts(FG, FG, FG), null);
            Check(unknown.Equals(t2), w + " NoType", "TypeIndex 255 ≡ fixed-grip");
            // A lift that is not complete or not powered adds nothing of its type.
            var mixed = Lifts(FG, FG);
            mixed.Add(new Score.LiftInput { Complete = true, Powered = false, TypeIndex = TR });
            mixed.Add(new Score.LiftInput { Complete = false, Powered = true, TypeIndex = GD });
            Score.Result mx = Score.Compute(Area(two), Medium(), mixed, null);
            Check(mx.LiftsValue == 12 && mx.LiftsUnpowered == 1 && mx.LiftsOpenSection == 1, w + " uncounted", "an unpowered tram / unfinished gondola add 0");
        }

        private static void SchoolPatrolRows()
        {
            const string w = "ladder-civic";
            Score.Result sc1 = Row("SC1", Area(AFrames(1), school: true), Small(), Lifts(FG), 69.138032112, 20, 20, 53.259713074, 53, 53);
            Check(sc1.SchoolActive && sc1.GreenCounted && sc1.SchoolValue == SchoolRule.ScoreValue, w + " SC1", "school on, green counted, +12");
            Row("SC2", Area(AFrames(2), school: true), Medium(), Lifts(FG, FG, FG), 345.584085025, 24, 24, 98.359568049, 98, 69);
            List<Score.RunInput> noGreen = new List<Score.RunInput>(Small());
            noGreen[0] = Run(Gr, Blue, 20000, 1150, 1000);
            Score.Result sc3 = Row("SC3", Area(AFrames(1), school: true), noGreen, Lifts(FG), 60.638032112, 20, 20, 51.148170312, 51, 51);
            Check(sc3.SchoolActive && !sc3.GreenCounted && sc3.SchoolValue == 0, w + " SC3", "school on, no counted green: +0");
            Score.Result sc4 = Row("SC4", Area(AFrames(2), school: true), Summer(Medium()), Lifts(FG, FG, FG), 4.5, 24, 12, 20.485281374, 20, 14);
            Check(sc4.SchoolValue == 0 && !sc4.GreenCounted, w + " SC4", "summer: no counted green, +0");
            Score.Result noSchool = Score.Compute(Area(AFrames(1)), Small(), Lifts(FG), null);
            Check(!noSchool.SchoolActive && noSchool.SchoolValue == 0 && noSchool.GreenCounted, w + " SC0", "no school: +0, green still counted");

            // Patrol (Medium; runs 8-9 are the natural blacks).
            Score.Result pa1 = Row("PA1", Area(AFrames(2)), With(Medium(), null, true, 8, 9), Lifts(FG, FG, FG), 334.472825111, 24, 24,
                                   97.154392908, 97, 69);
            Check(Near(pa1.RunsValue, 140.643237207, 1e-6) && pa1.RunsPatrolled == 2, w + " PA1", $"RunsValue {R(pa1.RunsValue)} (blacks × 1.15), 2 patrolled");
            Score.Result pa2 = Row("PA2", Area(AFrames(2)), With(Medium(), false, null, 8, 9), Lifts(FG, FG, FG), 263.529124684, 24, 24,
                                   88.934320624, 89, 63);
            Check(Near(pa2.RunsValue, 106.994051234, 1e-6) && pa2.RunsPatrolled == 0, w + " PA2", "= B1 (unserved, not patrolled)");
            Score.Result pa3 = Row("PA3", Area(AFrames(2)), With(Medium(), false, true, 8, 9), Lifts(FG, FG, FG), 301.838722915, 24, 24,
                                   93.494025402, 93, 66);
            Check(Near(pa3.RunsValue, 125.164611659, 1e-6), w + " PA3", $"RunsValue {R(pa3.RunsValue)} (served ≥ 0.7, × 1.15)");
            Score.Result pa4 = Row("PA4", Area(AFrames(2)), With(Medium(), null, true, 0, 1, 2), Lifts(FG, FG, FG), 320.284085025, 24, 24,
                                   95.585929905, 96, 68);
            Check(pa4.RunsPatrolled == 3 && pa4.RunsValue == Score.Compute(Area(AFrames(2)), Medium(), Lifts(FG, FG, FG), null).RunsValue,
                  w + " PA4", "patrolled greens: no boost (RunsValue = T2 bit for bit), 3 patrolled");
            Score.Result pa5 = Row("PA5", Area(AFrames(2), backcountry: 100), With(Medium(), false, true, 8, 9), Lifts(FG, FG, FG),
                                   334.472825111, 24, 24, 97.154392908, 97, 69);
            Check(Near(pa5.RunsValue, pa1.RunsValue, 1e-9), w + " PA5", "backcountry 100: max(1.0, 0.7) = served → = PA1");
            // Patrol never touches a night run's factor or the open factor
            List<Score.RunInput> night = With(Medium(), null, true, 7);
            var parts = new List<Score.RunPart>();
            Score.Compute(Area(AFrames(2)), night, Lifts(FG, FG, FG), parts);
            Check(parts[7].TypeFactor == Score.NightFactor && parts[7].ServedFactor == 1.0 && parts[7].OpenFactor == 1.0, w + " night",
                  "a patrolled night blue keeps type 1.25, served 1, open 1");
        }

        private static void OrderFree()
        {
            const string w = "ladder-order";
            // LD7 every order (winter, Large, 6 FG): 24 permutations.
            List<Score.LodgeInput> ld7 = Lodges(L(Grand), L(Summit), L(Chalet), L(AFrame));
            int fails = Permutations(ld7, Large(), Lifts(FG, FG, FG, FG, FG, FG), out int count);
            Check(fails == 0 && count == 24, w + " LD7", $"{count} orders, {fails} differ (Result.Equals)");
            // SUMMER twin: LS5's Grand + served Summit inside a five-lodge set with a served Hut, the Chalet and an A-frame.
            List<Score.LodgeInput> summer = Lodges(L(Grand), L(Summit, true), L(Hut, true), L(Chalet), L(AFrame));
            fails = Permutations(summer, Summer(Medium()), Lifts(FG, GD, DQ), out count);
            Check(fails == 0 && count == 120, w + " summer", $"{count} orders, {fails} differ (Result.Equals)");
            fails = Permutations(Lodges(L(Grand), L(Summit, true)), Summer(Medium()), Lifts(FG, FG, FG), out count);
            Check(fails == 0 && count == 2, w + " LS5", $"LS5 both orders, {fails} differ");

            // Which luxury lodge is the best follows the season, whatever the order; both always count
            double bw = Score.LodgeBase(Lodges(L(Summit, true), L(Grand)), false, out double aw, out int bestW, out double exW);
            Check(bestW == Grand && bw == 52 && aw == 52 && exW == 12, w + " best", "winter: the Grand is best (capacity), the Summit adds 12");
            double bs = Score.LodgeBase(Lodges(L(Summit, true), L(Grand)), true, out double asu, out int bestS, out double exS);
            Check(bestS == Summit && bs == 52 && Near(asu, 47.2, 1e-9) && Near(exS, 7.2, 1e-9), w + " best",
                  "summer: the served Summit is best (40), the Grand adds 12 × 0.6 = 7.2");

            // 2,000 random lodge lists (0-7 lodges, any tier, random summit-served), winter and summer: reversed and one
            // shuffle give the same Result bit for bit.
            var rng = new Random(20261011);
            int orderFails = 0, n = 0;
            for (int k = 0; k < 2000; k++)
            {
                var lodges = new List<Score.LodgeInput>();
                int m = rng.Next(8);
                for (int i = 0; i < m; i++)
                    lodges.Add(L(rng.Next(SkiCatalog.Lodges.Count), rng.Next(2) == 0));
                List<Score.RunInput> runs = rng.Next(2) == 0 ? Medium() : Summer(Medium());
                Score.Result a = Score.Compute(Area(lodges), runs, Lifts(FG, DQ), null);
                var rev = new List<Score.LodgeInput>(lodges);
                rev.Reverse();
                var shuf = new List<Score.LodgeInput>(lodges);
                for (int i = shuf.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    Score.LodgeInput t = shuf[i];
                    shuf[i] = shuf[j];
                    shuf[j] = t;
                }
                bool ok = Score.Compute(Area(rev), runs, Lifts(DQ, FG), null).Equals(a) && Score.Compute(Area(shuf), runs, Lifts(FG, DQ), null).Equals(a);
                if (!ok)
                    orderFails++;
                n++;
                // Bounds: applied ≤ base in winter (=), applied ≥ 0, extras ≤ cap, best within the catalog.
                if (!(a.LodgeApplied >= 0 && a.LodgeExtraApplied <= Score.LodgeExtraCap + 1e-9 && a.LodgeBestIndex < SkiCatalog.Lodges.Count &&
                      (a.RunsCounted == 0 || a.LodgeApplied == a.LodgeBase)))
                    orderFails++;
                // No lodge is dropped (no luxury cap): the Result equals an independent reference ladder over the whole list.
                bool summerSeason = a.RunsCounted == 0;
                RefLadder(lodges, false, out double refBase, out double _);
                RefLadder(lodges, summerSeason, out double refApplied, out double refExtraSeason);
                int refTolerance = 0;
                for (int i = 0; i < lodges.Count; i++)
                    refTolerance = Math.Max(refTolerance, SkiCatalog.Lodges[lodges[i].LodgeIndex].PriceTolerance);
                if (m > 0 && !(Near(a.LodgeBase, refBase, 1e-9) && Near(a.LodgeApplied, refApplied, 1e-9) &&
                               Near(a.LodgeExtraApplied, refExtraSeason, 1e-9) && a.PriceTolerance == refTolerance && a.ActiveLodges == m))
                    orderFails++;
                if (m == 0 && !(a.LodgeBase == 0 && a.LodgeApplied == 0 && a.PriceTolerance == 0))
                    orderFails++;
            }
            Check(orderFails == 0, w + " random", $"{n} random lodge lists: {orderFails} order, bound or reference-ladder failure(s)");
        }

        /// <summary>An independent reference of the ladder (not the code under test): the season's best by B·μ (then E, then
        /// capacity, then the lower catalog index), B·μ + the others' E·μ, the sum scaled by 24 / Σ E past 24. Winter: μ = 1.
        /// Returns the applied value (winter: LodgeBase) and the extras.</summary>
        private static void RefLadder(List<Score.LodgeInput> lodges, bool summer, out double applied, out double extras)
        {
            applied = 0;
            extras = 0;
            if (lodges.Count == 0)
                return;
            int best = -1;
            double bestV = 0;
            for (int i = 0; i < lodges.Count; i++)
            {
                LodgeDef d = SkiCatalog.Lodges[lodges[i].LodgeIndex];
                double v = d.B * RefMu(d, summer, lodges[i].SummitServed);
                LodgeDef b = best < 0 ? null : SkiCatalog.Lodges[lodges[best].LodgeIndex];
                if (best < 0 || v > bestV || (v == bestV && (d.E > b.E || (d.E == b.E && (d.Capacity > b.Capacity ||
                                                                                       (d.Capacity == b.Capacity && d.Index < b.Index))))))
                {
                    best = i;
                    bestV = v;
                }
            }
            double sumE = 0, sumEMu = 0;
            for (int i = 0; i < lodges.Count; i++)
            {
                if (i == best)
                    continue;
                LodgeDef d = SkiCatalog.Lodges[lodges[i].LodgeIndex];
                sumE += d.E;
                sumEMu += d.E * RefMu(d, summer, lodges[i].SummitServed);
            }
            extras = summer ? (sumE > 24.0 ? sumEMu * 24.0 / sumE : sumEMu) : Math.Min(sumE, 24.0);
            applied = bestV + extras;
        }

        private static double RefMu(LodgeDef d, bool summer, bool served)
        {
            if (!summer)
                return 1.0;
            return served && d.MuSummit > d.Mu ? d.MuSummit : d.Mu;
        }

        /// <summary>Every permutation of <paramref name="lodges"/> (Heap's algorithm) against the first order's Result.</summary>
        private static int Permutations(List<Score.LodgeInput> lodges, List<Score.RunInput> runs, List<Score.LiftInput> lifts, out int count)
        {
            Score.Result first = Score.Compute(Area(new List<Score.LodgeInput>(lodges)), runs, lifts, null);
            var a = new List<Score.LodgeInput>(lodges);
            int n = a.Count;
            var c = new int[n];
            int fails = 0;
            count = 1;
            int i = 0;
            while (i < n)
            {
                if (c[i] < i)
                {
                    int j = (i & 1) == 0 ? 0 : c[i];
                    Score.LodgeInput t = a[j];
                    a[j] = a[i];
                    a[i] = t;
                    count++;
                    if (!Score.Compute(Area(new List<Score.LodgeInput>(a)), runs, lifts, null).Equals(first))
                        fails++;
                    c[i]++;
                    i = 0;
                }
                else
                {
                    c[i] = 0;
                    i++;
                }
            }
            return fails;
        }

        private static void ClosedForm()
        {
            const string w = "ladder-closed";
            int fails = 0;
            for (int n = 0; n <= 30; n++)
            {
                for (int season = 0; season < 2; season++)
                {
                    List<Score.RunInput> runs = season == 0 ? Medium() : Summer(Medium());
                    Score.AreaInput nullPath = Area(null);
                    nullPath.ActiveLodges = n;
                    Score.Result a = Score.Compute(nullPath, runs, Lifts(FG, FG, FG), null);
                    Score.Result b = Score.Compute(Area(AFrames(n)), runs, Lifts(FG, FG, FG), null);
                    if (!a.Equals(b))
                    {
                        fails++;
                        Check(false, w, $"{n} A-frame(s), {(season == 0 ? "winter" : "summer")}: list ≠ closed form (base {R(b.LodgeBase)} / {R(a.LodgeBase)}, " +
                                        $"applied {R(b.LodgeApplied)} / {R(a.LodgeApplied)}, extras {R(b.LodgeExtraApplied)} / {R(a.LodgeExtraApplied)})");
                    }
                    double expectBase = Score.LodgeBaseOf(n);
                    if (a.LodgeBase != expectBase || a.LodgeApplied != (season == 0 ? expectBase : expectBase * 0.5))
                        fails++;
                }
            }
            Check(fails == 0, w, $"n = 0..30 A-frames, winter and summer: explicit list ≡ Lodges == null bit for bit ({fails} failure(s))");

            // The public ladder on null / empty lists.
            double b0 = Score.LodgeBase(null, false, out double ap0, out int best0, out double ex0);
            double b1 = Score.LodgeBase(new List<Score.LodgeInput>(), true, out double ap1, out int best1, out double ex1);
            Check(b0 == 0 && ap0 == 0 && best0 == -1 && ex0 == 0 && b1 == 0 && ap1 == 0 && best1 == -1 && ex1 == 0,
                  w + " empty", "null / empty list: base 0, applied 0, best −1, extras 0");
            // An out-of-range LodgeIndex reads the A-frame row (never thrown).
            Score.Result oob = Score.Compute(Area(Lodges(L(99), L(-3))), Medium(), Lifts(FG, FG, FG), null);
            Score.Result two = Score.Compute(Area(AFrames(2)), Medium(), Lifts(FG, FG, FG), null);
            Check(oob.Equals(two), w + " range", "LodgeIndex 99 / −3 ≡ two A-frames");
            // Luxury lodges only: the best is one of them (the ladder no longer drops a luxury lodge, so there is no "no best").
            double bl = Score.LodgeBase(Lodges(L(Grand), L(Summit)), false, out double apl, out int bestl, out double exl);
            Check(bl == 52 && apl == 52 && bestl == Grand && exl == 12, w + " luxury only", "a Grand and a Summit: base 40 + 12 = 52, best the Grand");

            // A summer area with no lodge keeps the season share 0.5, so the hub's AttractionWithLodges(r, 1) is
            // today's AOpen + 20 × 0.5 (W2's runs with no lodge: 4 × √4.5 + 10 = 18.485 → 18).
            Score.Result nl = Score.Compute(Area(Lodges()), Summer(Medium()), Lifts(FG, FG, FG), null);
            Score.AreaInput nlNull = Area(null);
            Score.Result nl0 = Score.Compute(nlNull, Summer(Medium()), Lifts(FG, FG, FG), null);
            Check(nl.LodgeShare == 0.5 && nl0.LodgeShare == 0.5 && nl.Equals(nl0) && (nl.Flags & Score.ScoreFlags.NoLodge) != 0, w + " MUST1",
                  $"summer, 0 lodges: LodgeShare {R(nl.LodgeShare)} (want 0.5), empty list ≡ null");
            Check(HubRules.AttractionWithLodges(in nl, 1) == 18, w + " MUST1", $"AttractionWithLodges(summer, 0 lodges, 1) = {HubRules.AttractionWithLodges(in nl, 1)}, want 18 (today's)");
            Score.Result wl = Score.Compute(Area(Lodges()), Medium(), Lifts(FG, FG, FG), null);
            Check(wl.LodgeShare == 1.0, w + " MUST1", "winter, 0 lodges: LodgeShare 1");
        }

        private static void LedgerSpend()
        {
            const string w = "ladder-ledger";
            var ledger = new SkiResortLedger();
            ledger.Reset();
            ledger.Advance(100);
            EntityRef a = new EntityRef(7, 1), b = new EntityRef(9, 2);
            int rev = ledger.Revision;
            ledger.AddLodgeSpend(a, 0, 0);
            ledger.AddLodgeSpend(a, -3, -50);
            Check(ledger.Count == 0 && ledger.Revision == rev, w, "all-zero and negative calls create nothing and keep Revision");
            ledger.AddLodgeSpend(a, 4, 120);
            Check(ledger.Count == 1 && ledger.Revision == rev + 1 && ledger.TryGet(a, out SkiResortLedger.Entry ea) &&
                  ea.This.LodgeSpends == 4 && ea.This.LodgeSpendIncome == 120, w, "the first call creates the entry and moves Revision");
            ledger.AddLodgeSpend(a, -1, 30);   // −1 reads 0
            ledger.TryGet(a, out ea);
            Check(ea.This.LodgeSpends == 4 && ea.This.LodgeSpendIncome == 150 && ledger.Revision == rev + 2, w, "a negative count reads 0, the income adds");
            ledger.AddTickets(b, 3, 1, 0, 90);
            ledger.AddLodgeSpend(b, 3, 45);
            SkiResortLedger.Month total = ledger.Total(false);
            Check(total.LodgeSpends == 7 && total.LodgeSpendIncome == 195 && total.TicketIncome == 90, w, "Total sums the lodge spend");

            // Roll: Last = This (the spend moves with the tickets), This = 0
            ledger.Advance(101);
            ledger.TryGet(b, out SkiResortLedger.Entry eb);
            Check(eb.Last.LodgeSpends == 3 && eb.Last.LodgeSpendIncome == 45 && eb.Last.TicketsPaid == 3 && eb.This.LodgeSpends == 0 &&
                  eb.This.LodgeSpendIncome == 0, w, "a roll moves the spend to Last with the tickets");
            // Jump: everything cleared.
            ledger.AddLodgeSpend(b, 2, 20);
            ledger.Advance(140);
            ledger.TryGet(b, out eb);
            Check(eb.Last.LodgeSpends == 0 && eb.This.LodgeSpends == 0 && eb.Last.LodgeSpendIncome == 0, w, "a jump clears the spend with the tickets");
            // Prune keeps the spend of a live sign and drops the rest.
            ledger.AddLodgeSpend(a, 1, 10);
            ledger.AddLodgeSpend(b, 1, 11);
            ledger.Prune(new HashSet<EntityRef> { b });
            Check(ledger.Count == 1 && ledger.TryGet(b, out eb) && eb.This.LodgeSpendIncome == 11 && !ledger.TryGet(a, out ea), w,
                  "Prune drops the spend of a sign that left");

            // A tickets-only month is today's: the two new fields stay 0 and every other field is as before.
            var t = new SkiResortLedger();
            t.Reset();
            t.Advance(5);
            t.AddTickets(a, 10, 2, 1, 400);
            t.AddArrival(a, true, 5);
            t.AddLiftCharge(a, 12.5);
            t.TryGet(a, out SkiResortLedger.Entry et);
            SkiResortLedger.Month m = et.This;
            Check(m.TicketsPaid == 10 && m.TicketsUnpaid == 2 && m.TicketsFree == 1 && m.TicketIncome == 400 && m.TouristGuests == 5 &&
                  m.TouristParties == 1 && m.LiftCharged == 12.5 && m.LodgeSpends == 0 && m.LodgeSpendIncome == 0, w,
                  "a tickets-only month: the Phase A fields as booked, LodgeSpends / LodgeSpendIncome 0");
        }
    }
}
