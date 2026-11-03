// Tests for the pure rules of the cat-track run type, RunKind.CatTrack = 4 (SkiRunCatTrackA); oracles are literal hand values. The ECS side
// (the AreaGeometryData write, the hub rows, the tooltip, the cat track tool) is not covered. Case tags CT54-1..CT54-7, CT139-M, CT139-G.
// Pinned: CatTrack is 4 and appended (0..3 unchanged); since 2026-10-24 it counts minimally toward the resort (a member: Runs / RunsOpen /
// RunsCounted, a quarter of a run its size in value and capacity, never the variety or school green, the vertical or the end of the
// summer mode; worth 0 unless rated green), is rated Green by construction once rated (SkiRunRatingRules.CatTrackBand: the cat
// track tool grades every track under 25 %), passes service DOWNHILL only (LiftServedRule.AssignLinked track mask, 2026-10-25:
// entered where it meets a served run, traveled either way while it climbs no more than ClimbToleranceM in all, serving the runs it
// meets that fall away below; a null / empty / all-false mask changes nothing), never gets snow injection, reads its open state
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
            SnowMeasure();
            LiveOpen();
            KeepBar();
            Props();
            Catalog();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }

        private static void Kinds()
        {
            const string W = "cattrack/kinds";
            Check((byte)RunKind.Groomed == 0 && (byte)RunKind.Snowmaking == 1 && (byte)RunKind.Night == 2 && (byte)RunKind.Natural == 3,
                  W, "RunKind 0..3 moved");
            Check((byte)RunKind.CatTrack == 4, W, "RunKind.CatTrack is " + (byte)RunKind.CatTrack + ", expected 4 (appended)");
            for (int k = 0; k <= 3; k++)
            {
                Check(RunKindRules.CountsTowardResort(k), W, "kind " + k + " must count toward the resort");
                Check(!RunKindRules.IsCatTrackLink(k), W, "kind " + k + " must not be a cat track link");
            }
            Check(RunKindRules.CountsTowardResort(4), W, "the cat track counts toward the resort (minimally, since 2026-10-24)");
            Check(Score.KindCatTrack == (byte)RunKind.CatTrack, W, "SkiResortScore.KindCatTrack == RunKind.CatTrack");
            Check(RunKindRules.IsCatTrackLink(4), W, "the cat track must be a cat track link");
            Check(!RunKindRules.CountsTowardResort(-1) && !RunKindRules.IsCatTrackLink(-1), W, "kind -1 (not ours) counts nothing and is no cat track link");
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

        /// <summary>The test mountain: y = 1000 - 0.5 z (a 50 % fall line toward +z; x is level).</summary>
        private static float Slope(float z) => 1000f - 0.5f * z;

        private static float[] SlopeHeights(float2[] ring)
        {
            var y = new float[ring.Length];
            for (int k = 0; k < ring.Length; k++)
                y[k] = Slope(ring[k].y);
            return y;
        }

        /// <summary>A cat track ribbon along x at z = zc (half width 5 m), a node pair every 50 m from x0 to x1: the near edge forward,
        /// the far edge back (the cat track tool's ring order). heightAt(x) gives both edges' height at x.</summary>
        private static float2[] RibbonX(float x0, float x1, float zc, Func<float, float> heightAt, out float[] heights)
        {
            int m = (int)Math.Round((x1 - x0) / 50f) + 1;
            var ring = new float2[2 * m];
            heights = new float[2 * m];
            for (int k = 0; k < m; k++)
            {
                float x = x0 + 50f * k;
                ring[k] = new float2(x, zc - 5f);
                ring[2 * m - 1 - k] = new float2(x, zc + 5f);
                heights[k] = heights[2 * m - 1 - k] = heightAt(x);
            }
            return ring;
        }

        /// <summary>A cat track straight down the fall line along z at x = xc (half width 5 m), a node pair every 20 m, on the slope.</summary>
        private static float2[] RibbonZ(float z0, float z1, float xc, out float[] heights, float step = 20f)
        {
            int m = (int)Math.Round((z1 - z0) / step) + 1;
            var ring = new float2[2 * m];
            heights = new float[2 * m];
            for (int k = 0; k < m; k++)
            {
                float z = z0 + step * k;
                ring[k] = new float2(xc - 5f, z);
                ring[2 * m - 1 - k] = new float2(xc + 5f, z);
                heights[k] = heights[2 * m - 1 - k] = Slope(z);
            }
            return ring;
        }

        private sealed class Scene
        {
            public readonly List<float3> Tops = new List<float3>();
            public readonly List<float2[]> Outlines = new List<float2[]>();
            public readonly List<float[]> Heights = new List<float[]>();
            public readonly List<int> Areas = new List<int>();
            public readonly List<bool> Tracks = new List<bool>();
            public readonly LiftServedRule.RunLinks Links = new LiftServedRule.RunLinks();
            public List<LiftServedRule.Lift> Lifts;
            public List<LiftServedRule.Chord> Chords;
            public readonly List<int> ServingLift = new List<int>(), ServedCount = new List<int>(), Root = new List<int>(), Via = new List<int>();
            public readonly List<float> Distance = new List<float>();

            public Scene()
            {
                Lift(out Lifts, out Chords);
            }

            public int Add(float3 top, float2[] outline, float[] heights = null, bool track = false)
            {
                Tops.Add(top);
                Outlines.Add(outline);
                Heights.Add(heights);
                Areas.Add(0);
                Tracks.Add(track);
                return Tops.Count - 1;
            }

            /// <summary>A run on the test slope (heights from Slope).</summary>
            public int AddRun(float3 top, float2[] outline) => Add(top, outline, SlopeHeights(outline));

            /// <summary>A plain run crossing a track along x at z 200: x c-10..c+10, z 100-400 on the slope.</summary>
            public int AddCrossing(float c) => AddRun(Far, Rect(c - 10f, 100f, c + 10f, 400f));

            public Scene Solve() => Solve(Tracks);

            public Scene Solve(List<bool> tracks)
            {
                Links.Build(Outlines, Heights, Areas);
                LiftServedRule.AssignLinked(Tops, Links, Lifts, Chords, ServingLift, Distance, ServedCount, Root, Via, tracks);
                return this;
            }

            public bool Served(int i) => Root[i] >= 0;
        }

        /// <summary>A: served directly, x 0-40, z 0-400 on the slope. C: a flat cat track (y 900, the slope's height at z 200) along x
        /// from -490 to 1010, entered where it crosses A. heightAt overrides the flat height.</summary>
        private static Scene FlatTrack(out int a, out int c, Func<float, float> heightAt = null)
        {
            var s = new Scene();
            a = s.AddRun(Served1, Rect(0f, 0f, 40f, 400f));
            float2[] ring = RibbonX(-490f, 1010f, 200f, heightAt ?? (x => 900f), out float[] y);
            c = s.Add(Far, ring, y, track: true);
            return s;
        }

        private static void Leaf()
        {
            const string W = "cattrack/downhill";
            Eq(3f, LiftServedRule.ClimbToleranceM, W, "ClimbToleranceM");

            // D-1 A flat track carries service both ways to the runs it meets (they all fall away below it).
            Scene s = FlatTrack(out int a, out int c);
            int east = s.AddCrossing(460f);
            int west = s.AddCrossing(-340f);
            s.Solve();
            Check(s.Served(a), W, "A is served directly");
            Check(s.Served(c) && s.Root[c] == a && s.Via[c] == a, W, "the track is served, entered from A (root " + s.Root[c] + ", via " + s.Via[c] + ")");
            Check(s.Served(east) && s.Via[east] == c && s.Root[east] == a, W, "the run east of A is served through the track");
            Check(s.Served(west) && s.Via[west] == c, W, "the run west of A is served through the track (either direction)");
            Eq(1, s.ServedCount[0], W, "the lift's direct count stays one run (A)");

            // D-2 Tolerance: a +2 m bump is crossed, a +4 m bump stops the track.
            s = FlatTrack(out a, out c, x => x == 310f ? 902f : 900f);
            int near = s.AddCrossing(210f);
            int past = s.AddCrossing(460f);
            s.Solve();
            Check(s.Served(near) && s.Served(past), W, "a 2 m bump (under ClimbToleranceM) does not stop the track");
            s = FlatTrack(out a, out c, x => x == 310f ? 904f : 900f);
            near = s.AddCrossing(210f);
            past = s.AddCrossing(460f);
            s.Solve();
            Check(s.Served(near), W, "a run before a 4 m bump is served");
            Check(!s.Served(past), W, "a run past a 4 m bump is NOT served (skiers cannot climb it)");
            // A cap, not a sum: two 2 m bumps (x 310, x 610) never take the skier 3 m above where they got on.
            s = FlatTrack(out a, out c, x => x == 310f || x == 610f ? 902f : 900f);
            int beyond = s.AddCrossing(810f);
            s.Solve();
            Check(s.Served(beyond), W, "past two separate 2 m bumps: served (never 3 m above the entry)");
            // A track rising 1 m per node pair east of x 10 (y 900 at x 10, 903 at x 160, 904 at x 210): the skiers from A get on at
            // about 900.6 (cap 903.6) and stop before x 210. R crosses the track at x 160 (903 m): served, and stepping from R back onto
            // the track does not raise the cap (no 3 m ratchet per crossing run), so S at x 260 stays unserved.
            s = FlatTrack(out a, out c, x => 900f + math.max(0f, (x - 10f) / 50f));
            int low = s.AddCrossing(85f);
            int r = s.AddCrossing(160f);
            int sAbove = s.AddCrossing(260f);
            s.Solve();
            Check(s.Served(low) && s.Served(r), W, "runs the rising track meets within 3 m of the entry are served");
            Check(!s.Served(sAbove), W, "a run the track meets 5 m above the entry is NOT served, crossing run R or not");

            // D-3 A track straight down the mountain, entered half way: it serves only runs below the entry.
            s = new Scene();
            float2[] ring = RibbonZ(0f, 1000f, 500f, out float[] ry);
            c = s.Add(Far, ring, ry, track: true);
            a = s.AddRun(Served1, Rect(400f, 480f, 490f, 520f));      // touches the track's west edge at z 480-520 (y 760-740)
            int upper = s.AddRun(Far, Rect(510f, 180f, 600f, 220f));  // meets the track at z 180-220, above the entry
            int lower = s.AddRun(Far, Rect(510f, 780f, 600f, 900f));  // meets it at z 780 (y 610), below the entry
            s.Solve();
            Check(s.Served(c) && s.Via[c] == a, W, "the mountain-long track is served, entered from A");
            Check(!s.Served(upper), W, "a run the track meets ABOVE the entry is NOT served");
            Check(s.Served(lower) && s.Via[lower] == c, W, "a run the track meets below the entry is served through it");

            // D-3b The same track as the cat track tool simplifies a straight one (GradeRibbon): four nodes, its two ends only. A crosses
            // it at z 300-340 (y 850-830): the skiers get on between the ends and reach the lower end; the upper run's crossing (z 100,
            // y 950) lies above them, the lower run's (z 700, y 650) below.
            s = new Scene();
            c = s.Add(Far, RibbonZ(0f, 1000f, 500f, out float[] sy, 1000f), sy, track: true);
            a = s.AddRun(Served1, Rect(450f, 300f, 550f, 340f));
            upper = s.AddRun(Far, Rect(450f, 100f, 550f, 140f));
            lower = s.AddRun(Far, Rect(450f, 700f, 550f, 740f));
            s.Solve();
            Eq(4, s.Outlines[c].Length, W, "the straight track has four nodes");
            Check(s.Served(c) && s.Via[c] == a, W, "the four-node track is entered mid-way from A");
            Check(!s.Served(upper), W, "four-node track: the run crossing above the entry is NOT served");
            Check(s.Served(lower) && s.Via[lower] == c, W, "four-node track: the run crossing below the entry is served through it");

            // D-4 Chains: A -> track 1 -> B -> track 2 -> track 3 -> D, every hop downhill or level.
            s = new Scene();
            a = s.AddRun(Served1, Rect(0f, 0f, 40f, 400f));
            int t1 = s.Add(Far, RibbonX(10f, 510f, 200f, x => 900f, out float[] y1), y1, track: true);
            int b = s.AddRun(Far, Rect(455f, 100f, 475f, 600f));     // crosses track 1 at y ~900, falls to 700
            int t2 = s.Add(Far, RibbonX(410f, 1010f, 500f, x => 750f, out float[] y2), y2, track: true);   // crosses B at y 750
            int t3 = s.Add(Far, RibbonX(1010f, 1510f, 500f, x => 750f, out float[] y3), y3, track: true);  // shares track 2's end nodes
            int d = s.AddRun(Far, Rect(1255f, 400f, 1275f, 700f));   // crosses track 3
            s.Solve();
            Check(s.Served(t1) && s.Served(b) && s.Served(t2) && s.Served(t3) && s.Served(d), W, "the chain A - T1 - B - T2 - T3 - D is served");
            Check(s.Via[b] == t1 && s.Via[t2] == b && s.Via[t3] == t2 && s.Via[d] == t3, W, "each hop's via (" + s.Via[b] + ", " + s.Via[t2] + ", " +
                  s.Via[t3] + ", " + s.Via[d] + ")");
            Eq(a, s.Root[d], W, "D's root");

            // D-5 A directly served track (its top within the lift's reach) carries service like an entered one.
            s = new Scene();
            c = s.Add(Served1, RibbonX(610f, 1010f, 200f, x => 900f, out float[] y5), y5, track: true);
            int r5 = s.AddCrossing(810f);
            s.Solve();
            Check(s.Served(c) && s.Root[c] == c && s.Via[c] == -1, W, "a directly served track is its own root");
            Check(s.Served(r5) && s.Via[r5] == c, W, "a directly served track serves the runs it meets");

            // D-6 The mask: null, empty and all-false are the same (no track); a track mask on a plain run changes what it passes on.
            Scene none = FlatTrack(out _, out _).Solve(null);
            Scene empty = FlatTrack(out _, out _).Solve(new List<bool>());
            Scene falses = FlatTrack(out _, out _).Solve(new List<bool> { false, false });
            Check(SameInts(none.Root, empty.Root) && SameInts(none.Via, empty.Via), W, "an empty mask differs from no mask");
            Check(SameInts(none.Root, falses.Root) && SameInts(none.Via, falses.Via), W, "an all-false mask differs from no mask");
            Check(SameInts(none.ServedCount, falses.ServedCount) && SameInts(none.ServingLift, falses.ServingLift), W, "the direct counts changed with a mask");
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

            // The estimate's threshold (EstimateThresholdC): the gun margin only while the guns make snow, in the gun season with the guns
            // on (DECISIONS 568, 571); else the natural threshold (freezing).
            Check(RunSnowRules.EstimateThresholdC(RunKind.CatTrack, 0f, 3.3f, true, true) == 3.3f, W, "in season, guns on: freezing + the gun margin");
            Check(RunSnowRules.EstimateThresholdC(RunKind.CatTrack, 0f, 3.3f, true, false) == 0f, W, "guns Off: the natural threshold, freezing");
            Check(RunSnowRules.EstimateThresholdC(RunKind.CatTrack, 0f, 3.3f, false, true) == 0f, W,
                  "out of the gun season, guns on: the natural threshold (the 2026-11-02 field case opened on the margin)");
            Check(RunSnowRules.EstimateThresholdC(RunKind.CatTrack, -1f, 2f, true, false) == RunSnowRules.ThresholdC(RunKind.Natural, -1f, 2f), W,
                  "guns Off equals a natural run's threshold");
            foreach (RunKind k in new[] { RunKind.Natural, RunKind.Groomed })
                foreach (bool season in new[] { true, false })
                    foreach (bool guns in new[] { true, false })
                        Check(RunSnowRules.EstimateThresholdC(k, 0f, 3.3f, season, guns) == RunSnowRules.ThresholdC(k, 0f, 3.3f), W,
                              k + ": no guns, so neither the season nor the switch changes it (season " + season + ", guns " + guns + ")");
            Check(!RunSnowRules.GunsMakeSnow(RunKind.Natural, true, true) && !RunSnowRules.GunsMakeSnow(RunKind.Groomed, true, true), W,
                  "natural and groomed runs make no gun snow");
            // At +2 C day mean (between freezing and freezing + margin): in season guns on open, guns Off closed. At -5 C: open either way.
            float on = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, 1000f, 1300f, 2f, 0f, 3.3f, 1000f, 0.0065f, true, true);
            float off = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, 1000f, 1300f, 2f, 0f, 3.3f, 1000f, 0.0065f, true, false);
            float offCold = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, 1000f, 1300f, -5f, 0f, 3.3f, 1000f, 0.0065f, true, false);
            Check(RunSnowRules.OpenMultiplier(on) == 1f && RunSnowRules.NextState(RunSnowState.Unknown, on) == RunSnowState.Open, W,
                  "+2 C, guns on: open (share " + on + ")");
            Check(RunSnowRules.OpenMultiplier(off) == 0f && RunSnowRules.NextState(RunSnowState.Unknown, off) == RunSnowState.Closed, W,
                  "+2 C, guns Off: closed, no natural snow (share " + off + ")");
            Check(RunSnowRules.OpenMultiplier(offCold) == 1f && RunSnowRules.NextState(RunSnowState.Unknown, offCold) == RunSnowState.Open, W,
                  "-5 C, guns Off: open on natural snow (share " + offCold + ")");
            SnowParity();
        }

        /// <summary>CT-PARITY (DECISIONS 571): a cat track opens and closes exactly as a run with snowmaking. Every input of the estimate
        /// (gun season x guns On / Off x the day-mean temperature from natural snow to warm x three run heights) gives the cat track the same
        /// share, multiplier, hub state and label as its run: guns On = a snowmaking run (the run path passes gunsOn true, its gun value is
        /// look only), guns Off = a run that makes no snow (natural). Both go through RunSnowRules.EstimatedRunShare, the one call
        /// SkiSnowInjectionSystem makes for a run it could not measure and for every cat track (GetRunSnow). Oracles pin the season rule.</summary>
        private static void SnowParity()
        {
            const string W = "cattrack/parity";
            const float freezing = 0f, margin = 3.3f, baseH = 1000f, lapse = 0.0065f;
            // -5: natural snow everywhere; 1: natural snow on the upper half; 2: none natural, the guns cover it; 4.3: the guns cover half;
            // 9.4: warm (the field day mean at date 0.885).
            float[] temps = { -5f, 1f, 2f, 4.3f, 9.4f };
            float[][] heights = { new[] { 1000f, 1300f }, new[] { 1400f, 1700f }, new[] { 800f, 1000f } };
            int cases = 0, same = 0;
            foreach (bool season in new[] { true, false })
                foreach (bool guns in new[] { true, false })
                    foreach (float t in temps)
                        foreach (float[] h in heights)
                        {
                            float track = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, h[0], h[1], t, freezing, margin, baseH, lapse, season, guns);
                            RunKind runKind = guns ? RunKind.Snowmaking : RunKind.Natural;
                            float run = RunSnowRules.EstimatedRunShare(runKind, h[0], h[1], t, freezing, margin, baseH, lapse, season, true);
                            float night = RunSnowRules.EstimatedRunShare(RunKind.Night, h[0], h[1], t, freezing, margin, baseH, lapse, season, true);
                            float mt = RunSnowRules.OpenMultiplier(track), mr = RunSnowRules.OpenMultiplier(run);
                            bool ok = track == run
                                && mt == mr
                                && HubRules.OpenStateOf(mt, true) == HubRules.OpenStateOf(mr, true)
                                && RunSnowRules.NextState(RunSnowState.Unknown, track) == RunSnowRules.NextState(RunSnowState.Unknown, run)
                                && RunSnowRules.NextState(RunSnowState.Open, track) == RunSnowRules.NextState(RunSnowState.Open, run)
                                && RunSnowRules.NextState(RunSnowState.Closed, track) == RunSnowRules.NextState(RunSnowState.Closed, run)
                                && (!guns || night == run);
                            cases++;
                            if (ok)
                                same++;
                            else
                                Check(false, W, "season " + season + ", guns " + guns + ", " + t + " C, " + h[0] + "-" + h[1] + " m: track " + track +
                                      " vs " + runKind + " " + run + (guns ? ", night " + night : ""));
                        }
            Check(cases == 60 && same == cases, W, same + " of " + cases + " cases: the cat track's share, multiplier, hub state and label equal its run's");

            // Since the run-types round the estimate is the track's floor, not its whole share: its table share is TrackShare(measured or
            // estimated, made), and its guns' made snow starts at the snowmaking run's own estimate (TrackGunShare). So from no made snow, one
            // refresh gives the track at least its run's share in every case, and exactly the run's when the guns cover nothing more.
            int floorCases = 0, floorOk = 0;
            foreach (bool season in new[] { true, false })
                foreach (bool guns in new[] { true, false })
                    foreach (float t in temps)
                        foreach (float[] h in heights)
                        {
                            float estimate = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, h[0], h[1], t, freezing, margin, baseH, lapse, season, guns);
                            RunKind runKind = guns ? RunKind.Snowmaking : RunKind.Natural;
                            float run = RunSnowRules.EstimatedRunShare(runKind, h[0], h[1], t, freezing, margin, baseH, lapse, season, true);
                            var made = new RunSnowRules.TrackMadeSnow();
                            float gun = RunSnowRules.TrackGunShare(season, guns, h[0], h[1], t, freezing, margin, baseH, lapse);
                            float cold = RunSnowRules.TrackColdShare(h[0], h[1], t, freezing, baseH, lapse);
                            RunSnowRules.StepTrackMadeSnow(ref made, 0f, gun, cold, t, false, out bool _);
                            float track = RunSnowRules.TrackShare(estimate, made.Share);
                            floorCases++;
                            if (track >= run && track == Math.Max(run, made.Share) && made.Share == gun)
                                floorOk++;
                            else
                                Check(false, W, "floor: season " + season + ", guns " + guns + ", " + t + " C, " + h[0] + "-" + h[1] + " m: track " +
                                      track + " vs run " + run + ", made " + made.Share + ", gun " + gun);
                        }
            Check(floorCases == 60 && floorOk == floorCases, W, floorOk + " of " + floorCases + " cases: a track's first refresh is at least its run's share");

            // Out of the gun season a snowmaking run holds natural snow only (its guns raise nothing, FoldRowsJob.GunGate): it equals a
            // natural run at every temperature.
            foreach (float t in temps)
                Check(RunSnowRules.EstimatedRunShare(RunKind.Snowmaking, 1000f, 1300f, t, freezing, margin, baseH, lapse, false, true) ==
                      RunSnowRules.EstimatedRunShare(RunKind.Natural, 1000f, 1300f, t, freezing, margin, baseH, lapse, false, true), W,
                      "out of season, " + t + " C: a snowmaking run's estimate equals a natural run's");

            // Oracles (hand values). The field case: a track high on the hill (1400-1700 m, base 300 m) at a 9.4 C day mean. Margin line
            // 300 + (9.4 - 3.3) / 0.0065 = 1238 m: all of it cold enough for the guns; natural line 300 + 9.4 / 0.0065 = 1746 m: none.
            float inSeason = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, 1400f, 1700f, 9.4f, freezing, margin, 300f, lapse, true, true);
            float outSeason = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, 1400f, 1700f, 9.4f, freezing, margin, 300f, lapse, false, true);
            Check(inSeason == 1f && HubRules.OpenStateOf(RunSnowRules.OpenMultiplier(inSeason), true) == RunOpenState.Open, W,
                  "field case in the gun season, guns on: open (share " + inSeason + ")");
            Check(outSeason == 0f && HubRules.OpenStateOf(RunSnowRules.OpenMultiplier(outSeason), true) == RunOpenState.Closed, W,
                  "field case out of the gun season, guns on: closed, was Open 100 % (share " + outSeason + ")");
            // 1000-1300 m at base 1000: +2 C open only in the season with the guns on; -5 C open in every case; 9.4 C closed in every case;
            // 4.3 C in the season with the guns on: patchy (line 1153.8 m, share 0.487, multiplier 0.436).
            foreach (bool season in new[] { true, false })
                foreach (bool guns in new[] { true, false })
                {
                    float s2 = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, 1000f, 1300f, 2f, freezing, margin, baseH, lapse, season, guns);
                    float sCold = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, 1000f, 1300f, -5f, freezing, margin, baseH, lapse, season, guns);
                    float sWarm = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, 1000f, 1300f, 9.4f, freezing, margin, baseH, lapse, season, guns);
                    string c = "season " + season + ", guns " + guns;
                    Check(s2 == (season && guns ? 1f : 0f), W, c + ", +2 C: " + (season && guns ? "open" : "closed") + " (share " + s2 + ")");
                    Check(sCold == 1f, W, c + ", -5 C: open on natural snow (share " + sCold + ")");
                    Check(sWarm == 0f, W, c + ", 9.4 C: closed (share " + sWarm + ")");
                }
            float half = RunSnowRules.EstimatedRunShare(RunKind.CatTrack, 1000f, 1300f, 4.3f, freezing, margin, baseH, lapse, true, true);
            Check(Math.Abs(half - 0.4872f) < 0.001f && HubRules.OpenStateOf(RunSnowRules.OpenMultiplier(half), true) == RunOpenState.Partly, W,
                  "4.3 C in the season, guns on: patchy (share " + half + ")");
        }

        /// <summary>CT-MEASURE (run-types round): a cat track's own snow. The snow map under it is measured as a natural run's is
        /// (SkiSnowInjectionSystem, not headless); its guns' snow is RunSnowRules.TrackMadeSnow, raised to the snowmaking estimate, held by the
        /// runs' protection machine at the largest run budget, melted at a run pack's speed after it (RunMeltOutHours). Oracles are hand
        /// values.</summary>
        private static void SnowMeasure()
        {
            const string W = "cattrack/measure";
            const float freezing = 0f, margin = 3.3f, baseH = 1000f, lapse = 0.0065f, lo = 1000f, hi = 1300f;
            // The pieces are the estimate's own: the guns' share is the snowmaking estimate while they make snow, else 0; the cold share is
            // the natural estimate.
            foreach (float t in new[] { -5f, 1f, 2f, 4.3f, 9.4f })
                foreach (bool season in new[] { true, false })
                    foreach (bool guns in new[] { true, false })
                    {
                        float gun = RunSnowRules.TrackGunShare(season, guns, lo, hi, t, freezing, margin, baseH, lapse);
                        float want = season && guns
                            ? RunSnowRules.EstimatedRunShare(RunKind.Snowmaking, lo, hi, t, freezing, margin, baseH, lapse, true, true) : 0f;
                        Check(gun == want, W, "gun share at " + t + " C, season " + season + ", guns " + guns + ": " + gun + " vs " + want);
                    }
                Check(RunSnowRules.TrackColdShare(lo, hi, 1f, freezing, baseH, lapse) ==
                      RunSnowRules.EstimatedRunShare(RunKind.Natural, lo, hi, 1f, freezing, margin, baseH, lapse, true, true), W,
                      "the cold share is a natural run's estimate");

            // Build: in the season with the guns on at +2 C the guns cover all of it (line 1000 + (2 - 3.3) / 0.0065 = 800 m).
            var track = new RunSnowRules.TrackMadeSnow();
            float g2 = RunSnowRules.TrackGunShare(true, true, lo, hi, 2f, freezing, margin, baseH, lapse);
            float c2 = RunSnowRules.TrackColdShare(lo, hi, 2f, freezing, baseH, lapse);
            RunSnowRules.StepTrackMadeSnow(ref track, 0f, g2, c2, 2f, false, out bool started);
            Check(track.Share == 1f && !started && track.Spell.State == RunSnowRules.ProtectState.Idle, W,
                  "+2 C, season, guns on: made 100 %, nothing warm, no spell (share " + track.Share + ")");

            // Thaw out of the season at +2 C: no gun, no cold share, so all of it is warm. The spell holds it for the largest budget (72 h,
            // a north face), then it melts as a run's pack would at +2 C (RunMeltOutHours, 3.0 h to the close line): open while >= 40 %.
            float hours = 0f, openHours = -1f;
            RunSnowState label = RunSnowState.Open;
            bool sawStart = false, sawSpent = false;
            for (int step = 0; step < 200; step++)
            {
                RunSnowRules.ProtectEnd end = RunSnowRules.StepTrackMadeSnow(ref track, 0.5f, 0f, c2, 2f, false, out bool s);
                hours += 0.5f;
                sawStart |= s;
                sawSpent |= end == RunSnowRules.ProtectEnd.BudgetSpent;
                label = RunSnowRules.NextState(label, RunSnowRules.TrackShare(c2, track.Share));
                if (label != RunSnowState.Open && openHours < 0f)
                    openHours = hours;
            }
            Check(sawStart && sawSpent && track.Share == 0f, W, "thaw: the spell starts, its budget is spent, the made snow melts out");
            Check(openHours > RunSnowRules.MaxBudgetHours(false) + RunSnowRules.RunMeltOutHours(2f) - 0.5f &&
                  openHours <= RunSnowRules.MaxBudgetHours(false) + RunSnowRules.RunMeltOutHours(2f) + 0.5f, W,
                  "thaw: open for the 72 h budget plus a run pack's melt to 1 ft at +2 C (closed after " + openHours + " h)");
            // At least as long as a run: the track's hold is the largest budget any run texel gets, every aspect, spring or not.
            foreach (bool spring in new[] { false, true })
                for (float aspect = RunSnowRules.ProtectSouthFactor; aspect <= RunSnowRules.ProtectNorthFactor + 1e-4f; aspect += 0.25f)
                    Check(RunSnowRules.MaxBudgetHours(spring) >= RunSnowRules.EffectiveBudgetHours(spring, aspect), W,
                          "the track's hold covers a run texel's budget (spring " + spring + ", aspect " + aspect + ")");

            // Spring halves the budget, as for a run: closed after 36 h + part of the melt.
            var springTrack = new RunSnowRules.TrackMadeSnow { Share = 1f };
            float springOpen = -1f, h2 = 0f;
            label = RunSnowState.Open;
            for (int step = 0; step < 120; step++)
            {
                RunSnowRules.StepTrackMadeSnow(ref springTrack, 0.5f, 0f, 0f, 2f, true, out bool _);
                h2 += 0.5f;
                label = RunSnowRules.NextState(label, springTrack.Share);
                if (label != RunSnowState.Open && springOpen < 0f)
                    springOpen = h2;
            }
            Check(springOpen > 36f + RunSnowRules.RunMeltOutHours(2f) - 0.5f && springOpen <= 36f + RunSnowRules.RunMeltOutHours(2f) + 0.5f, W,
                  "spring thaw: closed after the 36 h budget and the melt to the close line (" + springOpen + " h)");

            // Cold out of the season (-10 C): the cold keeps all of it, nothing is warm, it holds for any time (a run's made snow stays too).
            var cold = new RunSnowRules.TrackMadeSnow { Share = 1f };
            float cCold = RunSnowRules.TrackColdShare(lo, hi, -10f, freezing, baseH, lapse);
            for (int step = 0; step < 100; step++)
                RunSnowRules.StepTrackMadeSnow(ref cold, 6f, 0f, cCold, -10f, false, out bool _);
            Check(cold.Share == 1f && cold.Spell.State == RunSnowRules.ProtectState.Idle, W, "-10 C out of season: held 600 h, no spell");

            // Too warm (12 C at the track, above ProtectMaxLocalC): no spell; it melts as a run's pack would: at the close line (40 %) after
            // RunMeltOutHours(12) = 0.50 h, gone after 1 h.
            float m12 = RunSnowRules.RunMeltOutHours(12f);
            var warm = new RunSnowRules.TrackMadeSnow { Share = 1f };
            RunSnowRules.StepTrackMadeSnow(ref warm, m12, 0f, 0f, 12f, false, out bool warmStart);
            Check(!warmStart && Math.Abs(warm.Share - RunSnowRules.CloseBelowShare) < 1e-4f, W,
                  "12 C: no spell, at the close line after " + m12 + " h (" + warm.Share + ")");
            RunSnowRules.StepTrackMadeSnow(ref warm, 1f, 0f, 0f, 12f, false, out bool _);
            Check(warm.Share == 0f, W, "12 C: gone after the next hour");

            // Below freezing at the track nothing melts, even with the spell spent and the day-mean cold share under the made share.
            var frozen = new RunSnowRules.TrackMadeSnow { Share = 1f, Spell = new RunSnowRules.ProtectRun { State = RunSnowRules.ProtectState.Spent } };
            RunSnowRules.StepTrackMadeSnow(ref frozen, 6f, 0f, 0.2f, -1f, false, out bool _);
            Check(frozen.Share == 1f, W, "-1 C at the track, spell spent: no melt (" + frozen.Share + ")");

            // The melt-out speed: a run pack from the gun stop depth (0.96 m) to the keep bar (0.30 m), linear in the air temperature.
            Check(Math.Abs(RunSnowRules.RunMeltOutHours(1f) - 6.04f) < 0.01f && Math.Abs(RunSnowRules.RunMeltOutHours(7f) - 0.863f) < 0.01f &&
                  Math.Abs(RunSnowRules.RunMeltOutHours(12f) - 0.503f) < 0.01f, W,
                  "RunMeltOutHours 1 C " + RunSnowRules.RunMeltOutHours(1f) + ", 7 C " + RunSnowRules.RunMeltOutHours(7f) + ", 12 C " +
                  RunSnowRules.RunMeltOutHours(12f));
            Check(float.IsPositiveInfinity(RunSnowRules.RunMeltOutHours(0f)) && float.IsPositiveInfinity(RunSnowRules.RunMeltOutHours(-5f)) &&
                  float.IsPositiveInfinity(RunSnowRules.RunMeltOutHours(float.NaN)), W, "RunMeltOutHours: infinite at or below freezing and for NaN");

            // Closes last (DECISIONS 574): a run pack at the gun stop depth, held for its texel budget (none above 10 C) and then melting to the
            // keep bar, closes no later than a track beside it at the same air, for every aspect, spring or not.
            foreach (float airC in new[] { 0.5f, 1f, 2f, 4f, 7f, 9.9f, 12f })
                foreach (bool spring in new[] { false, true })
                    for (float aspect = RunSnowRules.ProtectSouthFactor; aspect <= RunSnowRules.ProtectNorthFactor + 1e-4f; aspect += 0.5f)
                    {
                        float runBudget = airC <= RunSnowRules.ProtectMaxLocalC ? RunSnowRules.EffectiveBudgetHours(spring, aspect) : 0f;
                        float runClose = runBudget + RunSnowRules.RunMeltOutHours(airC);
                        var t = new RunSnowRules.TrackMadeSnow { Share = 1f };
                        RunSnowState tl = RunSnowState.Open;
                        float th = 0f, trackClose = -1f;
                        for (int step = 0; step < 20000 && trackClose < 0f; step++)
                        {
                            RunSnowRules.StepTrackMadeSnow(ref t, 0.05f, 0f, 0f, airC, spring, out bool _);
                            th += 0.05f;
                            tl = RunSnowRules.NextState(tl, t.Share);
                            if (tl != RunSnowState.Open)
                                trackClose = th;
                        }
                        Check(trackClose >= runClose - 0.06f, W, "closes last at " + airC + " C, spring " + spring + ", aspect " + aspect +
                              ": track " + trackClose + " h, run " + runClose + " h");
                    }

            // Partly cold: made snow never melts below the cold share (the part at or below freezing), here 1 C: line 1153.8 m, 48.7 %.
            var part = new RunSnowRules.TrackMadeSnow { Share = 1f, Spell = new RunSnowRules.ProtectRun { State = RunSnowRules.ProtectState.Spent } };
            float c1 = RunSnowRules.TrackColdShare(lo, hi, 1f, freezing, baseH, lapse);
            for (int step = 0; step < 10; step++)
                RunSnowRules.StepTrackMadeSnow(ref part, 1f, 0f, c1, 1f, false, out bool _);
            Check(Math.Abs(part.Share - c1) < 1e-5f && Math.Abs(c1 - 0.4872f) < 0.001f, W, "spent spell at 1 C: melts down to the cold share " + c1);

            // Guns Off in the season at +2 C: the same hold as out of the season (no gun share).
            var off = new RunSnowRules.TrackMadeSnow { Share = 1f };
            float gOff = RunSnowRules.TrackGunShare(true, false, lo, hi, 2f, freezing, margin, baseH, lapse);
            RunSnowRules.StepTrackMadeSnow(ref off, 1f, gOff, c2, 2f, false, out bool offStart);
            Check(gOff == 0f && offStart && off.Share == 1f, W, "guns Off at +2 C: no gun share, the spell holds the made snow");

            // The guns come back: the share is raised at once and, all cold again for ProtectRearmColdHours, the spell is Idle.
            var back = new RunSnowRules.TrackMadeSnow { Share = 0.2f, Spell = new RunSnowRules.ProtectRun { State = RunSnowRules.ProtectState.Spent } };
            RunSnowRules.StepTrackMadeSnow(ref back, 3f, 1f, 0f, 2f, false, out bool _);
            Check(back.Share == 1f && back.Spell.State == RunSnowRules.ProtectState.Idle, W, "guns back: 100 % at once, the spell re-arms after 3 h cold");

            // TrackShare: the larger; a NaN snow share (no measurement and no estimate) gives the made share; bad values clamp.
            Check(RunSnowRules.TrackShare(0.3f, 0.7f) == 0.7f && RunSnowRules.TrackShare(0.8f, 0.1f) == 0.8f, W, "TrackShare is the larger");
            Check(RunSnowRules.TrackShare(float.NaN, 0.4f) == 0.4f && RunSnowRules.TrackShare(float.NaN, float.NaN) == 0f, W, "TrackShare with NaN");
            Check(RunSnowRules.TrackShare(1.5f, -1f) == 1f, W, "TrackShare clamps");
            var nan = new RunSnowRules.TrackMadeSnow { Share = float.NaN };
            RunSnowRules.StepTrackMadeSnow(ref nan, 1f, 0f, 0f, 2f, false, out bool _);
            Check(nan.Share == 0f, W, "a NaN made share (a hand-edited save) reads 0");

            // Hysteresis (b): a track in the table gets the run label: 55 % keeps Open and keeps Closed, the midpoint only from Unknown.
            Check(RunSnowRules.NextState(RunSnowState.Open, 0.55f) == RunSnowState.Open &&
                  RunSnowRules.NextState(RunSnowState.Closed, 0.55f) == RunSnowState.Closed &&
                  RunSnowRules.NextState(RunSnowState.Unknown, 0.55f) == RunSnowState.Open, W, "the label's hysteresis at 55 %");
        }

        /// <summary>KEEP (DECISIONS 574): a run opens at the open bar (3 ft on 60 %) and, once open, stays open while the keep bar (1 ft)
        /// covers 40 %; the label is RunSnowRules.MeasuredShare then NextState, as RefreshRunSnow applies them. Texels are uniform-weight here
        /// (the fold weighs by coverage). Also MadeSnowLeads, which hides a cat track's depth line.</summary>
        private static void KeepBar()
        {
            const string W = "snow/keep-bar";
            Check(RunSnowRules.KeepNorm < RunSnowRules.SkiableNorm && Math.Abs(RunSnowRules.KeepDepthM - 0.30f) < 1e-6f, W, "the keep bar is 0.30 m, under 3 ft");
            // One pass of the table over a pack of texel depths (m) from a label.
            RunSnowState Pass(RunSnowState from, float[] depthsM, out float share)
            {
                int open = 0, keep = 0;
                foreach (float d in depthsM)
                {
                    float dn = d / RunSnowRules.SnowHeightScaleM;
                    if (RunSnowRules.IsSkiable(dn, 0f, RunSnowRules.SkiableNorm)) open++;
                    if (RunSnowRules.IsSkiable(dn, 0f, RunSnowRules.KeepNorm)) keep++;
                }
                share = RunSnowRules.MeasuredShare(from, open / (float)depthsM.Length, keep / (float)depthsM.Length);
                return RunSnowRules.NextState(from, share);
            }
            float[] Uniform(float d) { var a = new float[100]; for (int i = 0; i < a.Length; i++) a[i] = d; return a; }

            // Build: closed until 3 ft, whatever lies under it.
            RunSnowState s = RunSnowState.Closed;
            float openedAt = -1f;
            for (float d = 0f; d <= 0.961f; d += 0.01f)
            {
                s = Pass(s, Uniform(d), out float _);
                if (s == RunSnowState.Open && openedAt < 0f)
                    openedAt = d;
            }
            Check(Math.Abs(openedAt - 0.91f) < 0.006f, W, "build: opens at 3 ft (" + openedAt + " m)");
            // Thaw: from the gun stop depth down, open on 0.59 m and 0.40 m (the field case), closed under 1 ft.
            float closedAt = -1f;
            bool openAt059 = false, openAt040 = false;
            for (float d = 0.96f; d >= -0.001f; d -= 0.01f)
            {
                s = Pass(s, Uniform(d), out float _);
                if (Math.Abs(d - 0.59f) < 0.004f) openAt059 = s == RunSnowState.Open;
                if (Math.Abs(d - 0.40f) < 0.004f) openAt040 = s == RunSnowState.Open;
                if (s == RunSnowState.Closed && closedAt < 0f)
                    closedAt = d;
            }
            Check(openAt059 && openAt040 && closedAt < 0.30f && closedAt > 0.28f, W,
                  "thaw: open at 0.59 m and 0.40 m, closed under 1 ft (" + closedAt + " m)");
            // Rebuild from 0.5 m while closed: still the 3 ft rule.
            s = RunSnowState.Closed;
            s = Pass(s, Uniform(0.5f), out float _);
            Check(s == RunSnowState.Closed && Pass(RunSnowState.Closed, Uniform(0.9f), out float _) == RunSnowState.Closed &&
                  Pass(RunSnowState.Closed, Uniform(0.91f), out float _) == RunSnowState.Open, W, "closed at 0.5 m and 0.9 m: opens again only at 3 ft");
            // Patchy texture: half the texels at 0.5 m, half bare: keep share 50 % keeps Open (>= 40 %), and opens nothing from Closed.
            var patchy = new float[100];
            for (int i = 0; i < 50; i++) patchy[i] = 0.5f;
            Check(Pass(RunSnowState.Open, patchy, out float pShare) == RunSnowState.Open && Math.Abs(pShare - 0.5f) < 1e-6f &&
                  Pass(RunSnowState.Closed, patchy, out float pc) == RunSnowState.Closed && pc == 0f, W, "half at 0.5 m: open stays open (50 %), closed stays closed (0 %)");
            // A load: the label is not saved; Unknown reads the keep share at the midpoint, so a settled open pack stays open.
            Check(Pass(RunSnowState.Unknown, Uniform(0.5f), out float _) == RunSnowState.Open &&
                  Pass(RunSnowState.Unknown, Uniform(0.25f), out float _) == RunSnowState.Closed, W, "after a load: 0.5 m open, 0.25 m closed");
            // The field case (Entry Point, spring thaw at 7 C): mean 0.59 m, 66 % at 3 ft before the pass: it now stays open.
            var field = new float[100];
            for (int i = 0; i < 100; i++) field[i] = i < 66 ? 0.62f : 0.53f;
            Check(Pass(RunSnowState.Open, field, out float fShare) == RunSnowState.Open && fShare == 1f, W, "Entry Point at 0.53-0.62 m: open, 100 % on 1 ft");

            // Random packs: the keep share is never below the open share, and a run that opens stays open on the same pack.
            var rng = new System.Random(573);
            int flips = 0;
            for (int n = 0; n < 2000; n++)
            {
                var pack = new float[40];
                for (int i = 0; i < pack.Length; i++) pack[i] = (float)rng.NextDouble() * 1.2f;
                int open = 0, keep = 0;
                foreach (float d in pack)
                {
                    if (RunSnowRules.IsSkiable(d / RunSnowRules.SnowHeightScaleM, 0f, RunSnowRules.SkiableNorm)) open++;
                    if (RunSnowRules.IsSkiable(d / RunSnowRules.SnowHeightScaleM, 0f, RunSnowRules.KeepNorm)) keep++;
                }
                if (keep < open) flips++;
                RunSnowState a = Pass(RunSnowState.Closed, pack, out float _);
                if (a == RunSnowState.Open && Pass(a, pack, out float _) != RunSnowState.Open) flips++;
            }
            Check(flips == 0, W, "2000 random packs: keep share >= open share, and no flip back the pass after opening (" + flips + ")");
            // A fringe texel counts at its own lower target for the keep bar too (the merge's fringe rule).
            Check(RunSnowRules.IsSkiable(0.2f / 8f, 0.2f / 8f, RunSnowRules.KeepNorm) && !RunSnowRules.IsSkiable(0.19f / 8f, 0.2f / 8f, RunSnowRules.KeepNorm), W,
                  "a 0.2 m fringe target is kept at 0.2 m");
            Check(float.IsNaN(RunSnowRules.MeasuredShare(RunSnowState.Open, 0.5f, float.NaN)) &&
                  float.IsNaN(RunSnowRules.MeasuredShare(RunSnowState.Closed, float.NaN, 0.5f)), W, "MeasuredShare keeps NaN");

            // The depth line of a cat track hides while its made snow leads (the owner: made snow is not measured).
            Check(RunSnowRules.MadeSnowLeads(0f, 1f) && RunSnowRules.MadeSnowLeads(float.NaN, 0.5f) && !RunSnowRules.MadeSnowLeads(0.8f, 0.8f) &&
                  !RunSnowRules.MadeSnowLeads(0.5f, float.NaN) && !RunSnowRules.MadeSnowLeads(0.8f, RunSnowRules.TrackShare(0.8f, 0.3f)), W,
                  "MadeSnowLeads: made above snow hides the depth, equal or snow-led shows it");
        }

        /// <summary>The hub's run state from the live snow read (DECISIONS 571 c): the clamp SkiResortScore applies to its open factor.</summary>
        private static void LiveOpen()
        {
            const string W = "hub/live-open";
            Check(HubRules.LiveOpenFactor(float.NaN) == 0.0 && HubRules.LiveOpenFactor(-0.2f) == 0.0 && HubRules.LiveOpenFactor(0f) == 0.0, W,
                  "NaN and <= 0: 0");
            Check(HubRules.LiveOpenFactor(0.5f) == 0.5 && HubRules.LiveOpenFactor(1f) == 1.0 && HubRules.LiveOpenFactor(1.3f) == 1.0, W, "0.5, 1, 1.3");
            Check(HubRules.OpenStateOf(HubRules.LiveOpenFactor(RunSnowRules.OpenMultiplier(0.5f)), true) == RunOpenState.Partly &&
                  HubRules.OpenStateOf(HubRules.LiveOpenFactor(RunSnowRules.OpenMultiplier(0.65f)), true) == RunOpenState.Open &&
                  HubRules.OpenStateOf(HubRules.LiveOpenFactor(RunSnowRules.OpenMultiplier(0.3f)), true) == RunOpenState.Closed, W,
                  "share 50 % Partly, 65 % Open, 30 % Closed");
        }

        // Guns and hydrants, masts only when lit (the full table: DensityTests; the walk: PlacerTests). Names read through ClassOf as the load sweep does.
        private static void Props()
        {
            const string W = "cattrack/props";
            int K = (int)RunKind.CatTrack;
            Check(RunPropDensityRules.PropClassAllowed(K, RunPropDensityRules.ClassOf("SkiHydrantBox", false), false), W, "SkiHydrantBox stands on a track");
            Check(RunPropDensityRules.PropClassAllowed(K, RunPropDensityRules.ClassOf("SkiHydrantBoxBare", false), false), W, "SkiHydrantBoxBare stands on a track");
            Check(RunPropDensityRules.PropClassAllowed(K, RunPropDensityRules.ClassOf("SkiSnowGunFan", false), false), W, "a gun stands on a track");
            Check(!RunPropDensityRules.PropClassAllowed(K, RunPropDensityRules.ClassOf("SkiPisteFloodlightTall", true), false), W, "no mast on a dark track");
            Check(RunPropDensityRules.PropClassAllowed(K, RunPropDensityRules.ClassOf("SkiPisteFloodlightTall", true), true), W, "a mast on a lit track");
            Check(!RunPropDensityRules.PropClassAllowed(K, RunPropDensityRules.ClassOf("SkiPropNone", false), true), W, "never the empty marker");
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
