// Tests for the pure rules of the cat-track run type, RunKind.CatTrack = 4 (SkiRunCatTrackA). Oracles are literal hand
// values. The ECS side (the AreaGeometryData write, the hub rows, the tooltip) is not covered here.
//   CT54-1  RunKind.CatTrack is 4 and appended (0..3 unchanged); RunKindRules: counts toward the resort for kinds 0-3 only,
//           leaf for the cat track only, snow injection for groomed / snowmaking / night only; an unknown kind counts nothing.
//   CT54-2  Leaf rule (LiftServedRule.AssignLinked, optional leaf mask): a cat track crossing a served and an unserved run is
//           served through the first and does not serve the second; a directly served leaf serves nobody; a null, empty or
//           all-false mask is the old behavior bit for bit; a short mask reads false past its end.
//   CT54-3  Unsuitable rule: SkiRunRatingRules.CatTrackUnsuitable on the row band, and HubRules.CatTrackOpenState
//           (RunOpenState.Unsuitable beats any open factor).
//   CT54-4  Exclusions: the attraction path drops the cat track before RunInput (the same RunKindRules predicate the system
//           uses), so capacity, attraction, run counts and the green-variety flag equal the area without it; HubRules.Status of
//           an area whose only run is a cat track is NoRuns.
//   CT54-5  Snow: never injected (RunKindRules.HasSnowInjection); opens by the day-mean estimate with the gun margin
//           (RunSnowRules.ThresholdC = freezing + margin); the groomed threshold is unchanged.
//   CT54-6  Props: RunPropDensityRules.PropClassAllowed, a cat track carries hydrants only.
//   CT54-7  Catalog: SkiCatalog has the fifth RunDef (kind 4, prefab SkiRunCatTrackA, item runCatTrack) after the four.
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
            Unsuitable();
            Exclusions();
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
            Check(!RunKindRules.CountsTowardResort(4), W, "the cat track must not count toward the resort");
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
            Check(!s.Served(a2) && !s.Served(b2), W, "a directly served leaf must serve neither neighbour");
            s = new Scene();
            c2 = s.Add(Served1, Rect(90f, 190f, 700f, 210f));
            a2 = s.Add(Far, Rect(90f, 100f, 130f, 200f));
            b2 = s.Add(Far, Rect(580f, 100f, 620f, 300f));
            s.Solve(null);
            Check(s.Served(a2) && s.Served(b2), W, "the same scene without a mask serves both neighbours (the old behaviour)");

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
            // The leaf rule changes nothing about the lift's direct count (the system takes the cat track off it itself).
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

        private static void Unsuitable()
        {
            const string W = "cattrack/unsuitable";
            Check(SkiRunRatingRules.CatTrackFirstUnsuitableBand == 2, W, "the first unsuitable band is Blue (2)");
            Check(!SkiRunRatingRules.CatTrackUnsuitable(SkiRunRatingRules.BandUnrateable), W, "an unrateable track is not unsuitable");
            Check(!SkiRunRatingRules.CatTrackUnsuitable(SkiRunRatingRules.BandPending), W, "a pending track is not unsuitable");
            Check(!SkiRunRatingRules.CatTrackUnsuitable(0), W, "flat is fine");
            Check(!SkiRunRatingRules.CatTrackUnsuitable(1), W, "green is fine");
            Check(SkiRunRatingRules.CatTrackUnsuitable(2), W, "blue is unsuitable");
            Check(SkiRunRatingRules.CatTrackUnsuitable(3), W, "black is unsuitable");
            Check(SkiRunRatingRules.CatTrackUnsuitable(4), W, "double black is unsuitable");
            // RowBand feeds it: a rated state passes the band through, the others become -1 / -2.
            Check(!SkiRunRatingRules.CatTrackUnsuitable(SkiRunRatingRules.RowBand(SkiRunRatingRules.RatePending, 3)), W, "pending state: not unsuitable");
            Check(SkiRunRatingRules.CatTrackUnsuitable(SkiRunRatingRules.RowBand(SkiRunRatingRules.RateRated, 2)), W, "rated blue: unsuitable");
            Check(!SkiRunRatingRules.CatTrackUnsuitable(SkiRunRatingRules.RowBand(SkiRunRatingRules.RateFailed, 3)), W, "failed rating: not unsuitable");

            Check((int)RunOpenState.Unsuitable == 3 && (int)RunOpenState.Open == 2 && (int)RunOpenState.None == -1, W, "RunOpenState.Unsuitable is 3 (appended)");
            Check(HubRules.CatTrackOpenState(1.0, 1) == RunOpenState.Open, W, "green, factor 1: Open");
            Check(HubRules.CatTrackOpenState(0.5, 1) == RunOpenState.Partly, W, "green, factor 0.5: Partly");
            Check(HubRules.CatTrackOpenState(0.0, 0) == RunOpenState.Closed, W, "flat, factor 0: Closed");
            Check(HubRules.CatTrackOpenState(double.NaN, 1) == RunOpenState.Closed, W, "green, NaN factor: Closed");
            Check(HubRules.CatTrackOpenState(1.0, 2) == RunOpenState.Unsuitable, W, "blue beats an open factor: Unsuitable");
            Check(HubRules.CatTrackOpenState(0.0, 4) == RunOpenState.Unsuitable, W, "double black, factor 0: Unsuitable");
            Check(HubRules.CatTrackOpenState(1.0, -1) == RunOpenState.Open, W, "pending band: read by the factor (Open)");
            Check(HubRules.CatTrackOpenState(1.0, -2) == RunOpenState.Open, W, "unrateable band: read by the factor (Open)");
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

        private static void Exclusions()
        {
            const string W = "cattrack/exclusions";
            var withCat = new List<Score.RunInput>
            {
                Input(0, Score.BandBlue, 60000.0), Input(4, Score.BandGreen, 40000.0), Input(1, Score.BandBlack, 30000.0),
            };
            var without = new List<Score.RunInput> { withCat[0], withCat[2] };
            List<Score.RunInput> kept = Collect(withCat);
            Check(kept.Count == 2 && kept[0].Kind == 0 && kept[1].Kind == 1, W, "the filter must drop exactly the cat track");

            var lifts = new List<Score.LiftInput> { new Score.LiftInput { Complete = true, Powered = true, TypeIndex = 0 } };
            Score.Result filtered = Score.Compute(Area(), kept, lifts, new List<Score.RunPart>());
            Score.Result clean = Score.Compute(Area(), without, lifts, new List<Score.RunPart>());
            Check(filtered.Equals(clean), W, "the area with a filtered cat track must equal the area without it, bit for bit");
            Check(filtered.Runs == 2 && filtered.RunsOpen == 2 && filtered.RunsCounted == 2, W,
                  $"Runs {filtered.Runs} / RunsOpen {filtered.RunsOpen} / RunsCounted {filtered.RunsCounted}, expected 2 / 2 / 2");
            Check(!filtered.GreenCounted, W, "a cat track (green) must not make the variety flag true");
            Check(filtered.RunsCapacity > 0.0 && filtered.RunsCapacity == clean.RunsCapacity, W, "capacity must not include the cat track's hectares");

            // Why the filter matters: a kind-4 RunInput that leaked in is read as groomed and adds value, capacity and a green.
            Score.Result leaked = Score.Compute(Area(), new List<Score.RunInput>(withCat), lifts, new List<Score.RunPart>());
            Check(leaked.Runs == 3 && leaked.RunsCapacity > clean.RunsCapacity && leaked.RunsValue > clean.RunsValue, W,
                  "a leaked cat track would add a run, capacity and value; the filter is what keeps them out");
            Check(leaked.GreenCounted, W, "a leaked green cat track WOULD set the variety flag");

            // An area whose only run is a cat track has no runs for the hub (Status NoRuns, not Closed / Open).
            Check(HubRules.Status(Collect(new List<Score.RunInput> { Input(4, Score.BandGreen, 40000.0) }).Count, 0, 0) == ResortStatus.NoRuns,
                  W, "a lone cat track must leave the area at NoRuns");
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
