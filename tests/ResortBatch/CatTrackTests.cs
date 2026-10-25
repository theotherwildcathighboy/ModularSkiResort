// Tests for the pure rules of the cat-track run type, RunKind.CatTrack = 4 (SkiRunCatTrackA); oracles are literal hand values. The ECS side
// (the AreaGeometryData write, the hub rows, the tooltip, the cat track tool) is not covered. Case tags CT54-1..CT54-7, CT139-M, CT139-G.
// Pinned: CatTrack is 4 and appended (0..3 unchanged); since 2026-10-24 it counts minimally toward the resort (a member: Runs / RunsOpen /
// RunsCounted, a quarter of a run its size in value and capacity, never the variety or school green, the vertical or the end of the
// summer mode; worth 0 unless rated green), is rated Green by construction once rated (SkiRunRatingRules.CatTrackBand: the cat
// track tool grades every track under 25 %), is a leaf for service (LiftServedRule.AssignLinked leaf mask: served through the first
// run it crosses, serves nobody; a null / empty / all-false mask changes nothing), never gets snow injection, reads its open state
// like any member run (no Unsuitable state since 2026-10-25), carries hydrants only, and is the fifth RunDef in SkiCatalog (kind 4,
// prefab SkiRunCatTrackA, item runCatTrack).
// Not tested headless: that GradeBlue (25 %, SkiSlopeClassifier.cs) is where band 2 begins; tests/SlopeBatch owns BandOf.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;
using Score = SkiResortMod.Systems.SkiResortScore;

namespace ResortBatch
{
    public static class CatTrackTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Kinds();
            Leaf();
            OpenState();
            Minimal();
            GreenRule();
            Snow();
            Props();
            Catalog();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static void Kinds()
        {
            const string W = "cattrack/kinds";
            Check((byte)RunKind.Groomed == 0 && (byte)RunKind.Snowmaking == 1 && (byte)RunKind.Night == 2 && (byte)RunKind.Natural == 3,
                  W, "RunKind 0..3 moved");
            Check((byte)RunKind.CatTrack == 4, W, "RunKind.CatTrack is " + (byte)RunKind.CatTrack + ", expected 4 (appended)");
            for (int k = 0; k <= 3; k++)
            {
                Check(RunKindRules.CountsTowardResort(k), W, "kind " + k + " must count toward the resort");
                Check(!RunKindRules.IsServedLeaf(k), W, "kind " + k + " must not be a served leaf");
            }
            Check(RunKindRules.CountsTowardResort(4), W, "the cat track counts toward the resort (minimally, since 2026-10-24)");
            Check(Score.KindCatTrack == (byte)RunKind.CatTrack, W, "SkiResortScore.KindCatTrack == RunKind.CatTrack");
            Check(RunKindRules.IsServedLeaf(4), W, "the cat track must be a served leaf");
            Check(!RunKindRules.CountsTowardResort(-1) && !RunKindRules.IsServedLeaf(-1), W, "kind -1 (not ours) counts nothing and is no leaf");
            Check(!RunKindRules.CountsTowardResort(5), W, "an unknown kind 5 counts nothing");
            Check(RunKindRules.HasSnowInjection(0) && RunKindRules.HasSnowInjection(1) && RunKindRules.HasSnowInjection(2), W,
                  "groomed / snowmaking / night are injected");
            Check(!RunKindRules.HasSnowInjection(3) && !RunKindRules.HasSnowInjection(4) && !RunKindRules.HasSnowInjection(-1), W,
                  "natural (measured only), the cat track and an unknown kind are not injected");
        }

        private static readonly float3 Served1 = new float3(100f, 250f, 200f); // as tests/LiftBatch ServedLinkTests: served by the fixture lift
        private static readonly float3 Far = new float3(1000f, 200f, 200f);

        private static void Lift(out List<LiftServedRule.Lift> lifts, out List<LiftServedRule.Chord> chords)
        {
            chords = new List<LiftServedRule.Chord> { new LiftServedRule.Chord { A = new float2(0f, 0f), B = new float2(0f, 500f) } };
            lifts = new List<LiftServedRule.Lift>
            {
                new LiftServedRule.Lift
                {
                    Id = new EntityRef(101, 1), Base = new EntityRef(101, 1), Top = new EntityRef(102, 1),
                    Complete = true, Power = LiftPower.Powered, TopY = 300f, FirstChord = 0, ChordCount = 1,
                },
            };
        }

        private static float2[] Rect(float x0, float z0, float x1, float z1)
        {
            return new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };
        }

        private sealed class Scene
        {
            public readonly List<float3> Tops = new List<float3>();
            public readonly List<float2[]> Outlines = new List<float2[]>();
            public readonly List<int> Areas = new List<int>();
            public readonly LiftServedRule.RunLinks Links = new LiftServedRule.RunLinks();
            public List<LiftServedRule.Lift> Lifts;
            public List<LiftServedRule.Chord> Chords;
            public readonly List<int> ServingLift = new List<int>(), ServedCount = new List<int>(), Root = new List<int>(), Via = new List<int>();
            public readonly List<float> Distance = new List<float>();

            public Scene()
            {
                Lift(out Lifts, out Chords);
            }

            public int Add(float3 top, float2[] outline)
            {
                Tops.Add(top);
                Outlines.Add(outline);
                Areas.Add(0);
                return Tops.Count - 1;
            }

            public Scene Solve(List<bool> leaf)
            {
                Links.Build(Outlines, Areas);
                LiftServedRule.AssignLinked(Tops, Links, Lifts, Chords, ServingLift, Distance, ServedCount, Root, Via, leaf);
                return this;
            }

            public bool Served(int i) => Root[i] >= 0;
        }

        /// <summary>A: served directly, x 90-130. C: the cat track, a long strip crossing A and B. B: far east, linked to C only.</summary>
        private static Scene Crossing(out int a, out int c, out int b)
        {
            var s = new Scene();
            a = s.Add(Served1, Rect(90f, 100f, 130f, 200f));
            c = s.Add(Far, Rect(90f, 190f, 700f, 210f)); // overlaps A (z 190-200) and reaches into B
            b = s.Add(Far, Rect(580f, 100f, 620f, 300f)); // x 580-620: more than 25 m from A, crossed by C
            return s;
        }

        private static void Leaf()
        {
            const string W = "cattrack/leaf";

            // The old behavior (no mask): the chain A -> C -> B serves all three.
            Scene s = Crossing(out int a, out int c, out int b).Solve(null);
            Check(s.Served(a) && s.Served(c) && s.Served(b), W, "without a mask the chain must serve A, C and B");
            Check(s.Root[a] == a && s.Root[c] == a && s.Root[b] == a, W, "without a mask every root must be A");
            Check(s.Via[c] == a && s.Via[b] == c, W, "without a mask C is reached through A and B through C");

            // C is a leaf: served through A, but B is not.
            var leafC = new List<bool> { false, true, false };
            s = Crossing(out a, out c, out b).Solve(leafC);
            Check(s.Served(a), W, "A is served directly");
            Check(s.Served(c) && s.Root[c] == a && s.Via[c] == a, W, "the leaf C is still served, through A (root " + s.Root[c] + ", via " + s.Via[c] + ")");
            Check(!s.Served(b) && s.Root[b] == -1 && s.Via[b] == -1, W, "B must NOT be served through the leaf (root " + s.Root[b] + ")");

            // A directly served leaf serves no neighbor: C is served directly, A is not, B is linked to C only.
            s = new Scene();
            int c2 = s.Add(Served1, Rect(90f, 190f, 700f, 210f));
            int a2 = s.Add(Far, Rect(90f, 100f, 130f, 200f)); // linked to C, not served itself
            int b2 = s.Add(Far, Rect(580f, 100f, 620f, 300f));
            s.Solve(new List<bool> { true, false, false });
            Check(s.Served(c2) && s.Root[c2] == c2 && s.Via[c2] == -1, W, "a directly served leaf is its own root");
            Check(!s.Served(a2) && !s.Served(b2), W, "a directly served leaf must serve neither neighbor");
            s = new Scene();
            c2 = s.Add(Served1, Rect(90f, 190f, 700f, 210f));
            a2 = s.Add(Far, Rect(90f, 100f, 130f, 200f));
            b2 = s.Add(Far, Rect(580f, 100f, 620f, 300f));
            s.Solve(null);
            Check(s.Served(a2) && s.Served(b2), W, "the same scene without a mask serves both neighbors (the old behavior)");

            Scene none = Crossing(out _, out _, out _).Solve(null);
            Scene empty = Crossing(out _, out _, out _).Solve(new List<bool>());
            Scene falses = Crossing(out _, out _, out _).Solve(new List<bool> { false, false, false });
            Check(SameInts(none.Root, empty.Root) && SameInts(none.Via, empty.Via), W, "an empty mask differs from no mask");
            Check(SameInts(none.Root, falses.Root) && SameInts(none.Via, falses.Via), W, "an all-false mask differs from no mask");
            Check(SameInts(none.ServedCount, falses.ServedCount) && SameInts(none.ServingLift, falses.ServingLift), W, "the direct counts changed with a mask");

            // A mask shorter than the runs reads false past its end: a one-entry mask marks A only (A served directly, a leaf: C and B
            // stay unserved).
            Scene shortMask = Crossing(out a, out c, out b).Solve(new List<bool> { true });
            Check(shortMask.Served(a) && !shortMask.Served(c) && !shortMask.Served(b), W, "a short mask {true} makes A a leaf; C and B are not reached");
            // The leaf rule changes nothing about the lift's direct count (a lift's "runs served" counts a cat track since 2026-10-24).
            Check(leafC.Count == 3 && none.ServedCount.Count == 1 && none.ServedCount[0] == 1, W,
                  "the direct count is Assign's, one run (A), whatever the mask");
        }

        private static bool SameInts(List<int> a, List<int> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        private static void OpenState()
        {
            const string W = "cattrack/openstate";
            Check((int)RunOpenState.None == -1 && (int)RunOpenState.Closed == 0 && (int)RunOpenState.Partly == 1 && (int)RunOpenState.Open == 2,
                  W, "RunOpenState values (no Unsuitable since 2026-10-25)");
            Check(Enum.GetValues(typeof(RunOpenState)).Length == 4, W, "four open states");
            // A cat track is a member, so its row reads the member rule like any run
            Check(HubRules.OpenStateOf(1.0, true) == RunOpenState.Open && HubRules.OpenStateOf(0.5, true) == RunOpenState.Partly &&
                  HubRules.OpenStateOf(0.0, true) == RunOpenState.Closed, W, "member open states");
        }

        private static Score.AreaInput Area()
        {
            return new Score.AreaInput
            {
                ActiveLodges = 1, InactiveLodges = 0, Efficiency = 1.0,
                Settings = new Score.ScoreSettings { K = 4, BackcountryPercent = 40, SnowGating = true },
            };
        }

        private static Score.RunInput Input(byte kind, byte band, double areaM2)
        {
            return new Score.RunInput
            {
                Kind = kind, State = Score.BandState.Rated, Band = band, AreaM2 = areaM2, Served = true,
                OpenFactor = 1.0, TopM = 1800.0, BottomM = 1600.0,
            };
        }

        /// <summary>The attraction path's loop, reduced to its filter (SkiResortAttractivenessSystem.CollectRuns).</summary>
        private static List<Score.RunInput> Collect(IList<Score.RunInput> all)
        {
            var kept = new List<Score.RunInput>();
            for (int i = 0; i < all.Count; i++)
            {
                if (RunKindRules.CountsTowardResort(all[i].Kind))
                    kept.Add(all[i]);
            }
            return kept;
        }

        /// <summary>CT139-M: the cat track's minimal weight. A blue groomed + a black snowmaking run, then the same with a 4 ha green cat
        /// track whose top is 600 m higher than theirs: one more run (Runs / RunsOpen / RunsCounted), exactly a quarter of a groomed green
        /// of 4 ha in value and in capacity, the same vertical, no variety / school green, no summer end on its own.</summary>
        private static void Minimal()
        {
            const string W = "cattrack/minimal";
            Check(Score.CatTrackFactor == 0.25 && Score.CatTrackSkiersFactor == 0.25, W, "the cat track weighs a quarter (score and capacity)");
            Score.RunInput cat = Input(4, Score.BandGreen, 40000.0);
            cat.TopM = 2400.0;   // a summit-to-base track: must not stretch the vertical
            var without = new List<Score.RunInput> { Input(0, Score.BandBlue, 60000.0), Input(1, Score.BandBlack, 30000.0) };
            var withCat = new List<Score.RunInput> { without[0], cat, without[1] };
            List<Score.RunInput> kept = Collect(withCat);
            Check(kept.Count == 3 && kept[1].Kind == 4, W, "the filter keeps the cat track");

            var lifts = new List<Score.LiftInput> { new Score.LiftInput { Complete = true, Powered = true, TypeIndex = 0 } };
            var parts = new List<Score.RunPart>();
            Score.Result r = Score.Compute(Area(), kept, lifts, parts);
            Score.Result clean = Score.Compute(Area(), without, lifts, new List<Score.RunPart>());
            Check(r.Runs == 3 && r.RunsOpen == 3 && r.RunsCounted == 3, W, $"Runs {r.Runs} / RunsOpen {r.RunsOpen} / RunsCounted {r.RunsCounted}, expected 3 / 3 / 3");
            var gParts = new List<Score.RunPart>();
            Score.Compute(Area(), new List<Score.RunInput> { Input(0, Score.BandGreen, 40000.0) }, lifts, gParts);
            Check(Math.Abs(parts[1].Value - 0.25 * gParts[0].Value) < 1e-12, W, $"cat track value {parts[1].Value} = a quarter of a groomed green {gParts[0].Value}");
            Check(Math.Abs(parts[1].TypeFactor - 0.25) < 1e-12 && parts[1].Counted, W, "type factor 0.25, counted");
            Check(Math.Abs(r.RunsCapacity - clean.RunsCapacity - 0.25 * 4.0 * Score.SkiersPerHaGreen) < 1e-9, W,
                  $"capacity +{r.RunsCapacity - clean.RunsCapacity:F2} skiers, expected 4 ha x 45 x 0.25 = 45");
            Check(r.RunsValue > clean.RunsValue, W, "the score grows");
            Check(!r.GreenCounted && !r.Variety && r.VarietyMultiplier == clean.VarietyMultiplier, W, "a green cat track sets no variety / school green");
            Check(r.VerticalM == clean.VerticalM && r.TopM == clean.TopM, W, $"vertical {r.VerticalM} / top {r.TopM} must ignore the cat track ({clean.VerticalM} / {clean.TopM})");
            Check(r.NightRunsCounted == clean.NightRunsCounted, W, "no night run");

            // A lone cat track: a run (the hub shows the area, not NoRuns) that counts, but the area stays in summer mode.
            var lone = Collect(new List<Score.RunInput> { cat });
            Score.Result l = Score.Compute(Area(), lone, lifts, new List<Score.RunPart>());
            Check(l.Runs == 1 && l.RunsCounted == 1 && l.RunsValue > 0.0 && l.RunsCapacity > 0.0, W, "a lone cat track counts");
            Check((l.Flags & Score.ScoreFlags.SummerMode) != 0 && (l.Flags & Score.ScoreFlags.NoRuns) == 0, W, "a lone cat track keeps the summer mode, no NoRuns flag");
            Check(HubRules.Status(lone.Count, 1, 0) != ResortStatus.NoRuns, W, "a lone cat track is a run for the hub status");

            // Worth nothing unless rated green (the score's own guard; the catalog sends a rated cat track as Green): blue, flat, unrateable,
            // pending; still a listed member run.
            foreach (Score.RunInput bad in new[]
            {
                Input(4, Score.BandBlue, 40000.0), Input(4, Score.BandFlat, 40000.0),
                new Score.RunInput { Kind = 4, State = Score.BandState.Unrateable, AreaM2 = 40000.0, Served = true, OpenFactor = 1.0 },
                new Score.RunInput { Kind = 4, State = Score.BandState.Pending, AreaM2 = 40000.0, Served = true, OpenFactor = 1.0 },
            })
            {
                var bp = new List<Score.RunPart>();
                Score.Result b = Score.Compute(Area(), new List<Score.RunInput> { bad }, lifts, bp);
                Check(b.Runs == 1 && b.RunsCounted == 0 && b.RunsValue == 0.0 && b.RunsCapacity == 0.0 && !bp[0].Counted, W,
                      $"a cat track {bad.State} band {bad.Band} must be worth 0 (counted {b.RunsCounted}, value {b.RunsValue})");
            }
        }

        /// <summary>CT139-G: the green rule. A rated cat track reads Green whatever band the classifier gave (the tool grades it under 25 %;
        /// the rater reads a hairpin or a crossing steeper than the track's own grade, GradeBatch GR-20); pending / unrateable pass through.</summary>
        private static void GreenRule()
        {
            const string W = "cattrack/green";
            Check(SkiRunRatingRules.CatTrackMaxGrade == 0.25f, W, "built under 25 %");
            Check(SkiRunRatingRules.BandGreenValue == Score.BandGreen, W, "BandGreenValue is Green (1)");
            for (sbyte band = 0; band <= 4; band++)
                Check(SkiRunRatingRules.CatTrackBand(band) == SkiRunRatingRules.BandGreenValue, W, "rated band " + band + " reads green");
            Check(SkiRunRatingRules.CatTrackBand(SkiRunRatingRules.BandPending) == SkiRunRatingRules.BandPending &&
                  SkiRunRatingRules.CatTrackBand(SkiRunRatingRules.BandUnrateable) == SkiRunRatingRules.BandUnrateable, W,
                  "pending / unrateable pass through");
        }

        private static void Snow()
        {
            const string W = "cattrack/snow";
            Check(RunSnowRules.ThresholdC(RunKind.CatTrack, 0f, 3.3f) == 3.3f, W, "the cat track opens at freezing + the gun margin (3.3)");
            Check(RunSnowRules.ThresholdC(RunKind.CatTrack, -1f, 2f) == 1f, W, "freezing -1 + margin 2 = 1");
            Check(RunSnowRules.ThresholdC(RunKind.Groomed, 0f, 3.3f) == 0f, W, "groomed is unchanged: freezing + 0");
            Check(RunSnowRules.ThresholdC(RunKind.Natural, 0f, 3.3f) == 0f, W, "natural is unchanged: freezing");
            Check(RunSnowRules.ThresholdC(RunKind.Snowmaking, 0f, 3.3f) == 3.3f && RunSnowRules.ThresholdC(RunKind.Night, 0f, 3.3f) == 3.3f, W,
                  "snowmaking and night are unchanged");
            // The estimate at 2 C day mean on a 300 m run starting at the base height: a cat track (threshold 3.3) is cold enough
            // everywhere (share 1, open), a groomed run (threshold 0) is not (share 0, closed).
            float cat = RunSnowRules.EstimatedShare(1000f, 1300f, 2f, RunSnowRules.ThresholdC(RunKind.CatTrack, 0f, 3.3f), 1000f, 0.0065f);
            float groomed = RunSnowRules.EstimatedShare(1000f, 1300f, 2f, RunSnowRules.ThresholdC(RunKind.Groomed, 0f, 3.3f), 1000f, 0.0065f);
            Check(cat >= 1f && RunSnowRules.OpenMultiplier(cat) == 1f, W, "at +2 C the cat track estimate is fully open (share " + cat + ")");
            Check(groomed <= 0f && RunSnowRules.OpenMultiplier(groomed) == 0f, W, "at +2 C a groomed run's estimate is closed (share " + groomed + ")");
        }

        private static void Props()
        {
            const string W = "cattrack/props";
            byte[] classes = { RunPropDensityRules.ClassOther, RunPropDensityRules.ClassGun, RunPropDensityRules.ClassHydrant,
                               RunPropDensityRules.ClassMast, RunPropDensityRules.ClassEmpty };
            foreach (byte cl in classes)
            {
                Check(RunPropDensityRules.PropClassAllowed((int)RunKind.CatTrack, cl) == (cl == RunPropDensityRules.ClassHydrant), W,
                      "cat track, class " + cl + ": only hydrants are allowed");
                for (int k = 0; k <= 3; k++)
                    Check(RunPropDensityRules.PropClassAllowed(k, cl), W, "kind " + k + ", class " + cl + " must stay allowed");
            }
            Check(RunPropDensityRules.PropClassAllowed(4, RunPropDensityRules.ClassOf("SkiHydrantBox", false)), W, "SkiHydrantBox reads as a hydrant");
            Check(RunPropDensityRules.PropClassAllowed(4, RunPropDensityRules.ClassOf("SkiHydrantBoxBare", false)), W, "SkiHydrantBoxBare reads as a hydrant");
            Check(!RunPropDensityRules.PropClassAllowed(4, RunPropDensityRules.ClassOf("SkiPisteFloodlightTall", true)), W, "a floodlight mast is not allowed");
            Check(!RunPropDensityRules.PropClassAllowed(4, RunPropDensityRules.ClassOf("SkiSnowFanGun", false)), W, "a gun is not allowed");
            Check(!RunPropDensityRules.PropClassAllowed(4, RunPropDensityRules.ClassOf("SkiPropNone", false)), W, "the empty marker is not allowed");
        }

        private static void Catalog()
        {
            const string W = "cattrack/catalog";
            Check(SkiCatalog.Names.RunCatTrack == "SkiRunCatTrackA", W, "Names.RunCatTrack");
            Check(SkiCatalog.Runs.Count == 5, W, "Runs.Count is " + SkiCatalog.Runs.Count + ", expected 5");
            RunDef last = SkiCatalog.Runs[SkiCatalog.Runs.Count - 1];
            Check(last.Kind == RunKind.CatTrack && last.Prefab == "SkiRunCatTrackA" && last.BuildItemId == "runCatTrack", W,
                  "the last RunDef must be the cat track (kind 4, SkiRunCatTrackA, runCatTrack)");
            Check(SkiCatalog.TryGetBuildItem("runCatTrack", out CatalogName n, out int rk) && n.Name == "SkiRunCatTrackA" && rk == 4, W,
                  "build item runCatTrack must resolve to SkiRunCatTrackA, run kind 4");
            Check(SkiCatalog.Problems.Count == 0, W, "SkiCatalog.Problems is not empty: " + string.Join("; ", new List<string>(SkiCatalog.Problems).ToArray()));
        }
    }
}
