// Breakover pairs and train kinds, phase B1 (docs: breakover-holddown-plan-2026-11-03, sections 2 and 4.2). Where-tags `angles/...`.
// AL-12 LiftAlignMath.RopeAngles / DeflectionDeg against hand-computed values (crest +, dip -, straight = the sag kink); the bisector is
// TrainPitch. AL-13 the train kind from the two load cases (the plan's band edges per type) and its hysteresis. AL-14 the caps per type.
// BO-1..BO-8 the planner's phase (d) on synthetic profiles: a crest past the cap gets a pair 10-40 m apart that brings both under it; a
// crest within the cap gets nothing; pins and removed spots are respected; a junction end is judged with the built neighbor's rope
// point; a crest no pair can fix stays single, flagged, with a reason; a dip past the hold-down cap gets a pair too; determinism and
// Plan(Plan) a fixed point. HD-1 / HD-2 / HD-5 kinds on every plan (hold-down in a strong dip, push-pull in a moderate one) and the fit
// never adds a tower. CO-1 the cost counter (deterministic work, never wall clock) under a computed bound; wall time is reported only.
// B2: the twins. FG judges its support trains at the Break cap (32, the 8-wheel Break twin on every member), so the FG pair cases run
// on constants without twins (WithoutTwins: the planner with the twin prefabs not loaded) and BO-9 shows the Break head taking the crest
// instead. HD-1 / HD-2 the Hold / Combo head on the member (E = Top(twin) - Top0, rope unchanged); HD-3 a member without the twin ->
// the nearest member with it, or the support head + a note; HD-4 MemberOf round trip, the engine winner over the twins and the per-load
// twin filter; HD-5 the fit keeps the node count and the heads it carries (hysteresis); HD-6 Emit writes E of the placed head; HD-7 a loose
// end (or any end node) without a lane anchor has the rope the lane anchor would give, at every head (LiftTowerEdit.RopeAboveNodeM).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class BreakoverTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Al12RopeAngles();
            Al13Kinds();
            Al14Caps();
            Bo1CrestPair();
            Bo2WithinCap();
            Bo3PinnedCrest();
            Bo4RemovedPartner();
            Bo5Junction();
            Bo6NoRepair();
            Bo7DipPair();
            Bo8Determinism();
            Bo9BreakHead();
            Hd1Hd2Kinds();
            Hd3NoTwin();
            Hd4MemberOf();
            Hd5FitNeverAdds();
            Hd6Emit();
            Hd7LooseEndRope();
            Co1Cost();
            Table();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);
        private static string F(double v, string f = "F2") => v.ToString(f, CultureInfo.InvariantCulture);

        private static readonly string[] ChairTypes = { "fixedGrip", "detachable", "gondola" };

        /// <summary>The type's constants with no twin loaded (LiftTowerPlanConstants.TwinFilter rejecting every twin): support heads only,
        /// FG judged at its line cap 16 (the B1 pair cases).</summary>
        internal static LiftTowerPlanConstants WithoutTwins(string id) => LiftTowerPlanConstants.For(id).With(twins: new LiftTowerTwin[0]);

        /// <summary>The constants without the pitch cap (DECISIONS 600): the bend tests on faces steeper than the cap.</summary>
        internal static LiftTowerPlanConstants NoPitchCap(LiftTowerPlanConstants c) =>
            c.With(pitchCapDeg: float.PositiveInfinity, pitchSoftDeg: float.PositiveInfinity);

        // ── AL-12: RopeAngles ─────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Al12RopeAngles()
        {
            const string W = "angles/AL-12";
            // Level, equal neighbors: d = 2 atan(4h), the engine's sag kink (hand values: FG 0.02 -> 9.1488, DQ / GD 0.025 -> 11.4212,
            // tram 0.04 -> 18.1806 deg).
            var kink = new Dictionary<string, double> { { "fixedGrip", 9.1488 }, { "detachable", 11.4212 }, { "gondola", 11.4212 }, { "tram", 18.1806 } };
            foreach (var kv in kink)
            {
                float h = LiftTowerPlanConstants.For(kv.Key).SagRatio;
                float d = LiftAlignMath.DeflectionDeg(new float3(0f, 5f, 0f), new float3(0f, 5f, 60f), new float3(0f, 5f, 150f), h);
                Check(math.abs(d - kv.Value) < 0.01, W + " straight " + kv.Key, "level d " + F(d, "F4") + ", hand " + F(kv.Value, "F4"));
            }
            // Uniform slope g 0.5 at FG h 0.02: atan(0.58) - atan(0.42) = 30.1113 - 22.7830 = 7.3283 deg (less than the level kink).
            float u = LiftAlignMath.DeflectionDeg(new float3(0f, 0f, 0f), new float3(0f, 30f, 60f), new float3(0f, 75f, 150f), 0.02f);
            Check(math.abs(u - 7.3283f) < 0.01f, W + " uniform", "uniform 0.5 slope d " + F(u, "F4") + ", hand 7.3283");
            // Crest (FG): 18 m up over 60 m, then level: in = atan(0.3 + 0.08) = 20.8068, out = atan(0 - 0.08) = -4.5739 -> +25.3807.
            float3 p = new float3(0f, 0f, 0f), q = new float3(0f, 18f, 60f), r = new float3(0f, 18f, 120f);
            LiftAlignMath.RopeAngles(p, q, r, 0.02f, out float a, out float b);
            Check(math.abs(math.degrees(a) - 20.8068f) < 0.01f && math.abs(math.degrees(b) + 4.5739f) < 0.01f, W + " crest",
                  "in " + F(math.degrees(a), "F4") + " / out " + F(math.degrees(b), "F4") + ", hand 20.8068 / -4.5739");
            float dc = LiftAlignMath.DeflectionDeg(p, q, r, 0.02f);
            Check(math.abs(dc - 25.3807f) < 0.01f && dc > 0f, W + " crest", "crest d " + F(dc, "F4") + ", hand +25.3807");
            // Dip (FG): 18 m down over 60 m then 18 m up: in = atan(-0.3 + 0.08) = -12.4074, out = atan(0.3 - 0.08) = 12.4074 -> -24.8149.
            float dd = LiftAlignMath.DeflectionDeg(new float3(0f, 18f, 0f), new float3(0f, 0f, 60f), new float3(0f, 18f, 120f), 0.02f);
            Check(math.abs(dd + 24.8149f) < 0.01f && dd < 0f, W + " dip", "dip d " + F(dd, "F4") + ", hand -24.8149");
            // The plan's examples: a DQ crest 0.60 -> 0.10 is 35.0 deg on one tower; an FG crest 0.50 -> 0.25 is 20.5.
            float dq = LiftAlignMath.DeflectionDeg(new float3(0f, 0f, 0f), new float3(0f, 60f, 100f), new float3(0f, 70f, 200f), 0.025f);
            float fg = LiftAlignMath.DeflectionDeg(new float3(0f, 0f, 0f), new float3(0f, 50f, 100f), new float3(0f, 75f, 200f), 0.02f);
            Check(math.abs(dq - 34.99f) < 0.05f && math.abs(fg - 20.46f) < 0.05f, W + " plan examples", "DQ " + F(dq) + " (35.0), FG " + F(fg) + " (20.5)");
            // Either way along the line: the same deflection.
            Check(math.abs(LiftAlignMath.DeflectionDeg(r, q, p, 0.02f) - dc) < 1e-4f, W + " direction", "reversed crest d differs");
            // The bisector is TrainPitch (unclamped) on 1,000 random triples (deterministic generator).
            uint seed = 12345u;
            float worst = 0f;
            for (int i = 0; i < 1000; i++)
            {
                float3 pp = new float3(0f, Rnd(ref seed) * 50f, 0f);
                float3 qq = new float3(0f, Rnd(ref seed) * 50f, 20f + Rnd(ref seed) * 200f);
                float3 rr = new float3(0f, Rnd(ref seed) * 50f, qq.z + 20f + Rnd(ref seed) * 200f);
                float hh = Rnd(ref seed) * 0.05f;
                LiftAlignMath.RopeAngles(pp, qq, rr, hh, out float ia, out float oa);
                worst = math.max(worst, math.abs(0.5f * (ia + oa) - LiftAlignMath.TrainPitch(pp, qq, rr, hh, 10f)));
            }
            Check(worst < 1e-6f, W + " bisector", "TrainPitch differs from the RopeAngles bisector by " + worst + " rad");
        }

        private static float Rnd(ref uint s)
        {
            s = s * 1664525u + 1013904223u;
            return (s >> 8) / 16777216f;
        }

        // ── AL-13: kinds from the load cases, the plan's band edges, hysteresis ──────────────────────────────────────────────────
        /// <summary>Symmetric chords turning by <paramref name="chordDeg"/> at the tower (+ convex): s_in = tan(c / 2), s_out = -tan(c / 2).</summary>
        private static void Symmetric(float chordDeg, out float3 p, out float3 q, out float3 r)
        {
            float g = math.tan(math.radians(0.5f * chordDeg));
            p = new float3(0f, 0f, 0f);
            q = new float3(0f, 100f * g, 100f);
            r = new float3(0f, 0f, 200f);
        }

        private static LiftTrainKind KindOf(float chordDeg, float h, LiftTrainKind prev = LiftTrainKind.None)
        {
            Symmetric(chordDeg, out float3 p, out float3 q, out float3 r);
            LiftAlignMath.LoadCaseDeflections(p, q, r, h, out float t, out float hv);
            return LiftAlignMath.ClassifyTrain(t, hv, prev);
        }

        private static void Al13Kinds()
        {
            const string W = "angles/AL-13";
            // Band edges (symmetric chords): support while 2 atan(tan(c/2) + 4h/1.4) >= 1 deg, hold-down while 2 atan(tan(c/2) + 4 x 1.5625h)
            // <= -1 deg. The plan's table: FG -5.5 / -15.2, DQ and GD -7.2 / -18.7.
            var plan = new Dictionary<string, float[]> { { "fixedGrip", new[] { -5.5f, -15.2f } }, { "detachable", new[] { -7.2f, -18.7f } }, { "gondola", new[] { -7.2f, -18.7f } } };
            foreach (var kv in plan)
            {
                float h = LiftTowerPlanConstants.For(kv.Key).SagRatio;
                float tanP = math.tan(math.radians(0.5f * LiftAlignMath.MinPressDeg));
                float sEdge = 2f * math.degrees(math.atan(tanP - 4f * h * LiftAlignMath.TightSagScale));
                float hEdge = 2f * math.degrees(math.atan(-tanP - 4f * h * LiftAlignMath.HeavySagScale));
                Check(math.abs(sEdge - kv.Value[0]) < 0.06f && math.abs(hEdge - kv.Value[1]) < 0.06f, W + " " + kv.Key,
                      "edges " + F(sEdge) + " / " + F(hEdge) + ", plan " + kv.Value[0] + " / " + kv.Value[1]);
                Check(KindOf(sEdge + 0.1f, h) == LiftTrainKind.Support, W + " " + kv.Key, "0.1 deg above the support edge is not Support");
                Check(KindOf(sEdge - 0.1f, h) == LiftTrainKind.PushPull, W + " " + kv.Key, "0.1 deg below the support edge is not PushPull");
                Check(KindOf(hEdge + 0.1f, h) == LiftTrainKind.PushPull, W + " " + kv.Key, "0.1 deg above the hold-down edge is not PushPull");
                Check(KindOf(hEdge - 0.1f, h) == LiftTrainKind.HoldDown, W + " " + kv.Key, "0.1 deg below the hold-down edge is not HoldDown");
                Check(KindOf(20f, h) == LiftTrainKind.Support && KindOf(0f, h) == LiftTrainKind.Support && KindOf(-30f, h) == LiftTrainKind.HoldDown,
                      W + " " + kv.Key, "a crest / straight / deep dip misclassified");
            }
            // MinPress exactly: support; hysteresis: kept 0.2 deg past its edge, flipped 0.3 deg past.
            Check(LiftAlignMath.ClassifyTrain(1.0f, 5f) == LiftTrainKind.Support, W, "tight exactly MinPress is not Support");
            Check(LiftAlignMath.ClassifyTrain(0.8f, 5f, LiftTrainKind.Support) == LiftTrainKind.Support, W + " hyst", "Support not kept 0.2 deg past");
            Check(LiftAlignMath.ClassifyTrain(0.7f, 5f, LiftTrainKind.Support) == LiftTrainKind.PushPull, W + " hyst", "Support kept 0.3 deg past");
            Check(LiftAlignMath.ClassifyTrain(-5f, -0.8f, LiftTrainKind.HoldDown) == LiftTrainKind.HoldDown, W + " hyst", "HoldDown not kept 0.2 deg past");
            Check(LiftAlignMath.ClassifyTrain(-5f, -0.7f, LiftTrainKind.HoldDown) == LiftTrainKind.PushPull, W + " hyst", "HoldDown kept 0.3 deg past");
            Check(LiftAlignMath.ClassifyTrain(1.2f, 5f, LiftTrainKind.PushPull) == LiftTrainKind.PushPull, W + " hyst", "PushPull not kept 0.2 deg past (support side)");
            Check(LiftAlignMath.ClassifyTrain(1.3f, 5f, LiftTrainKind.PushPull) == LiftTrainKind.Support, W + " hyst", "PushPull kept 0.3 deg past (support side)");
            Check(LiftAlignMath.ClassifyTrain(-5f, -1.2f, LiftTrainKind.PushPull) == LiftTrainKind.PushPull, W + " hyst", "PushPull not kept 0.2 deg past (hold-down side)");
            Check(LiftAlignMath.ClassifyTrain(-5f, -1.3f, LiftTrainKind.PushPull) == LiftTrainKind.HoldDown, W + " hyst", "PushPull kept 0.3 deg past (hold-down side)");
            Check(LiftAlignMath.KindHystDeg == 0.25f && LiftAlignMath.MinPressDeg == 1f, W, "hysteresis / MinPress constants moved");
        }

        // ── AL-14: caps ─────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Al14Caps()
        {
            const string W = "angles/AL-14";
            Check(LiftAlignMath.PerWheelMaxDeg == 4f, W, "per-wheel cap moved from 0.07 rad (D.M. 8 March 1999, 3.18.3)");
            Check(LiftAlignMath.TrainCapDeg(4) == 16f && LiftAlignMath.TrainCapDeg(8) == 32f && LiftAlignMath.TrainCapDeg(10) == 40f &&
                  LiftAlignMath.TrainCapDeg(12) == 40f && float.IsPositiveInfinity(LiftAlignMath.TrainCapDeg(0)), W, "TrainCapDeg");
            // Planner caps (support / hold-down / push-pull) with the twins loaded: FG support at the Break train's 32 (8 wheels, every member
            // has a Break twin); the line member's own caps (LineCapDeg) and the caps without twins are B1's.
            var want = new Dictionary<string, float[]>
            {
                { "fixedGrip", new[] { 32f, 16f, 8f } }, { "detachable", new[] { 32f, 32f, 16f } }, { "gondola", new[] { 40f, 32f, 16f } },
            };
            var line = new Dictionary<string, float[]>
            {
                { "fixedGrip", new[] { 16f, 16f, 8f } }, { "detachable", new[] { 32f, 32f, 16f } }, { "gondola", new[] { 40f, 32f, 16f } },
            };
            foreach (var kv in want)
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(kv.Key);
                float s = c.CapDeg(LiftTrainKind.Support), h = c.CapDeg(LiftTrainKind.HoldDown), p = c.CapDeg(LiftTrainKind.PushPull);
                Check(s == kv.Value[0] && h == kv.Value[1] && p == kv.Value[2] && c.HasTrains, W + " " + kv.Key,
                      "caps " + s + " / " + h + " / " + p + ", want " + kv.Value[0] + " / " + kv.Value[1] + " / " + kv.Value[2]);
                float[] lw = line[kv.Key];
                Check(c.LineCapDeg(LiftTrainKind.Support) == lw[0] && c.LineCapDeg(LiftTrainKind.HoldDown) == lw[1] && c.LineCapDeg(LiftTrainKind.PushPull) == lw[2],
                      W + " " + kv.Key, "line caps moved");
                LiftTowerPlanConstants bare = WithoutTwins(kv.Key);
                Check(bare.CapDeg(LiftTrainKind.Support) == lw[0] && bare.Twins.Length == 0, W + " " + kv.Key, "without twins the support cap is " +
                      bare.CapDeg(LiftTrainKind.Support) + ", want the line cap " + lw[0]);
                // the head's own train: Hold / Combo / Break wheels; Line = the line member's cap for the kind
                Check(c.CapDeg(LiftTrainKind.HoldDown, LiftTowerHead.Hold) == h && c.CapDeg(LiftTrainKind.PushPull, LiftTowerHead.Combo) == p &&
                      c.CapDeg(LiftTrainKind.Support, LiftTowerHead.Line) == lw[0] &&
                      (kv.Key != "fixedGrip" || c.CapDeg(LiftTrainKind.Support, LiftTowerHead.Break) == 32f), W + " " + kv.Key, "per-head caps");
                // With() keeps the wheels and the twins (a test or replay copy must judge with the same caps)
                Check(c.With(sagRatio: 0.004f).CapDeg(LiftTrainKind.Support) == s && c.With(sagRatio: 0.004f).Twins.Length == c.Twins.Length,
                      W + " " + kv.Key, "With() dropped the wheels or the twins");
            }
            // The Break head: past the line cap (16 + 0.01), kept down to 16 - KindHystDeg when the tower had it; Hold / Combo by kind.
            LiftTowerPlanConstants fg = LiftTowerPlanConstants.For("fixedGrip");
            Check(fg.WantHead(LiftTrainKind.Support, 16.05f) == LiftTowerHead.Break && fg.WantHead(LiftTrainKind.Support, 15.99f) == LiftTowerHead.Line &&
                  fg.WantHead(LiftTrainKind.Support, 15.80f, LiftTowerHead.Break) == LiftTowerHead.Break &&
                  fg.WantHead(LiftTrainKind.Support, 15.70f, LiftTowerHead.Break) == LiftTowerHead.Line &&
                  fg.WantHead(LiftTrainKind.HoldDown, -3f) == LiftTowerHead.Hold && fg.WantHead(LiftTrainKind.PushPull, -1f) == LiftTowerHead.Combo,
                  W + " heads", "the Break hysteresis or the kind -> head map moved");
            Check(LiftTowerPlanConstants.For("detachable").WantHead(LiftTrainKind.Support, 35f) == LiftTowerHead.Line, W + " heads",
                  "DQ has no Break twin but wants one");
            LiftTowerPlanConstants tram = LiftTowerPlanConstants.For("tram");
            // the tram's saddle cap (DECISIONS 600 a): twice the half-break cap for every kind, no wheels
            Check(!tram.HasTrains && tram.CapDeg(LiftTrainKind.Support) == 2f * LiftAlignMath.TramBendHalfCapDeg &&
                  tram.CapDeg(LiftTrainKind.HoldDown) == tram.CapDeg(LiftTrainKind.Support), W + " tram", "the tram's saddle cap is not 2 x TramBendHalfCapDeg");
            Check(LiftTowerPlanConstants.PairMaxM == 40f && LiftTowerPlanConstants.For("detachable").PairMinM == 10f && LiftTowerPlanConstants.MaxGroup == 2,
                  W, "pair spacing 10-40 m / group 2 moved");
        }

        // ── profiles ────────────────────────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Piecewise linear ground through (s, y) points; flat beyond the ends; no cross slope.</summary>
        private static LiftTerrainSampler Ground(params float[] sy)
        {
            return (s, l) =>
            {
                if (s <= sy[0]) return sy[1];
                for (int i = 2; i < sy.Length; i += 2)
                    if (s <= sy[i])
                        return math.lerp(sy[i - 1], sy[i + 1], (s - sy[i - 2]) / math.max(sy[i] - sy[i - 2], 1e-6f));
                return sy[sy.Length - 1];
            };
        }

        /// <summary>A crest: up at <paramref name="g1"/> for <paramref name="up"/> m, then <paramref name="g2"/> for <paramref name="on"/> m.</summary>
        private static LiftTerrainSampler Crest(float g1, float up, float g2, float on) =>
            Ground(0f, 1000f, up, 1000f + g1 * up, up + on, 1000f + g1 * up + g2 * on);

        private sealed class Line
        {
            public string Label;
            public LiftTowerPlanConstants C;
            public LiftTerrainSampler T;
            public float Length;
            public float Break;   // a crest line's ground break (m along)
            public LiftLineEdit Edit(bool stationA = true, bool stationB = true)
            {
                float ya = T(0f, 0f), yb = T(Length, 0f);
                return new LiftLineEdit(C, new float3(0f, ya, 0f), new float3(Length, yb, 0f), ya + C.RopeAboveBase(0), yb + C.RopeAboveBase(0), stationA, stationB);
            }
        }

        /// <summary>The tool's planner without pairs (the same rules as LiftLineEdit.Replan).</summary>
        private static LiftTowerPlan Off(Line l)
        {
            float ya = l.T(0f, 0f), yb = l.T(l.Length, 0f);
            var pl = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = ya, EndNodeYB = yb, Pairs = false };
            return pl.Plan(l.T, l.Length, ya + l.C.RopeAboveBase(0), yb + l.C.RopeAboveBase(0), l.C);
        }

        private static readonly Dictionary<string, Line> s_Crests = new Dictionary<string, Line>();

        private static Line CrestLine(string id)
        {
            if (s_Crests.TryGetValue(id, out Line l))
                return l;
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
            // Chord changes past one train's reach and inside a pair's (plan 2.2: single / pair FG 6.9 / 13.9, DQ 21.2 / 43.0, GD 29.6 / 60.4).
            // FG reaches less than the plan's 13.9 in practice: its ladder steps 4 m (rope 9 / 13 / 17 / 21 m) and its towers stand
            // 65-90 m apart (max span 90), so a pair fixes a ground chord change of about 8.5 deg; 0.25 -> 0.05 (11.2) stays flagged.
            switch (id)
            {
                case "fixedGrip": l = new Line { Label = "FG crest 0.20 -> 0.05 (no twins)", C = WithoutTwins(id), T = Crest(0.20f, 200f, 0.05f, 200f), Length = 400f, Break = 200f }; break;
                case "detachable": l = new Line { Label = "DQ crest 0.60 -> 0.05", C = c, T = Crest(0.60f, 500f, 0.05f, 500f), Length = 1000f, Break = 500f }; break;
                // the 0.90 face (42 deg) is past the GD pitch cap (40, DECISIONS 600): the pitch repair is PC-1's; here the bend repair alone
                default: l = new Line { Label = "GD crest 0.90 -> 0.10", C = NoPitchCap(c), T = Crest(0.90f, 500f, 0.10f, 500f), Length = 1000f, Break = 500f }; break;
            }
            s_Crests[id] = l;
            return l;
        }

        private static float WorstOver(LiftTowerPlan p, out int at)
        {
            float worst = float.NegativeInfinity;
            at = -1;
            for (int i = 0; i < p.Nodes.Length; i++)
            {
                if (!p.Nodes[i].HasAngles) continue;
                float o = math.abs(p.Nodes[i].DeflectionDeg) - p.Nodes[i].CapDeg;
                if (o > worst) { worst = o; at = i; }
            }
            return worst;
        }

        /// <summary>The longest run of consecutive towers PairMaxM or nearer apart (station ends are not towers): pairs only, never 3.</summary>
        private static int Group(LiftTowerPlan p)
        {
            int best = 0, run = 0;
            float prev = float.NaN;
            foreach (LiftPlanNode n in p.Nodes)
            {
                if (n.IsStation && !n.HasAngles)
                {
                    prev = float.NaN;
                    run = 0;
                    continue;
                }
                run = !float.IsNaN(prev) && n.AlongM - prev <= LiftTowerPlanConstants.PairMaxM + 1e-3f ? run + 1 : 1;
                best = math.max(best, run);
                prev = n.AlongM;
            }
            return best;
        }

        private static List<int> PairNodes(LiftTowerPlan p)
        {
            var l = new List<int>();
            for (int i = 0; i < p.Nodes.Length; i++)
                if ((p.Nodes[i].Flags & LiftPlanNodeFlags.Pair) != 0)
                    l.Add(i);
            return l;
        }

        internal static string Angles(LiftTowerPlan p)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < p.Nodes.Length; i++)
                if (p.Nodes[i].HasAngles)
                    sb.Append(sb.Length > 0 ? ", " : string.Empty)
                      .Append(p.Nodes[i].Member < 0 ? "end" : p.Constants.Ladder[p.Nodes[i].Member].Name.Replace("SkiLiftTower", ""))
                      .Append('@').Append(F(p.Nodes[i].AlongM, "F0")).Append(' ').Append(LiftTowerPlan.Sg(p.Nodes[i].DeflectionDeg))
                      .Append(LiftAlignMath.KindLetter(p.Nodes[i].Kind)).Append((p.Nodes[i].Flags & LiftPlanNodeFlags.Pair) != 0 ? "*" : string.Empty)
                      .Append(p.Nodes[i].Head != LiftTowerHead.Line ? "/" + p.Nodes[i].Head : string.Empty);
            return sb.ToString();
        }

        // ── BO-1: a crest past the cap gets a pair 10-40 m apart, both under the cap ───────────────────────────────────────────
        private static void Bo1CrestPair()
        {
            foreach (string id in ChairTypes)
            {
                string W = "angles/BO-1 " + id;
                Line l = CrestLine(id);
                LiftTowerPlan off = Off(l);
                float overOff = WorstOver(off, out int atOff);
                Check(overOff > 0f, W, "precondition: the crest is within the cap without pairs (" + Angles(off) + ")");
                LiftTowerPlan on = l.Edit().Replan(l.T);
                List<int> pair = PairNodes(on);
                float overOn = WorstOver(on, out int atOn);
                bool adjacent = pair.Count == 2 && pair[1] == pair[0] + 1;
                float gap = adjacent ? on.Nodes[pair[1]].AlongM - on.Nodes[pair[0]].AlongM : -1f;
                Check(adjacent, W, "not one adjacent pair: " + pair.Count + " pair tower(s); " + Angles(on));
                Check(gap >= 10f - 1e-3f && gap <= 40f + 1e-3f, W, "pair spacing " + F(gap, "F1") + " m outside 10-40");
                Check(overOn <= LiftTowerPlan.AngleTolDeg, W, "a tower still past its cap: " + F(overOn) + " at node " + atOn + "; " + Angles(on));
                Check(on.Clear, W, "the pair plan is not clear: " + LiftPlannerTests.Describe(on));
                Check(Group(on) == 2, W, "not a group of two: " + Angles(on));
                Check(on.LineTowers == off.LineTowers + 1, W, "the repair added " + (on.LineTowers - off.LineTowers) + " tower(s), not 1");
                Check(on.AngleNotes.Count >= 1 && on.AngleNotes[0].Contains("PAIR"), W, "no PAIR note: " + string.Join(" | ", on.AngleNotes));
                s_Report.Add("BO-1 " + l.Label + " | a pair 10-40 m apart, both under the cap, clear, one tower more | without: " + Angles(off) +
                             " | with: " + Angles(on) + " | " + string.Join(" | ", on.AngleNotes));
            }
        }

        // ── BO-2: a crest within the cap gets no pair; a single shift / member fix ranks before a pair ─────────────────────────
        private static void Bo2WithinCap()
        {
            const string W = "angles/BO-2";
            foreach (string id in ChairTypes)
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                var l = new Line { Label = id + " gentle crest 0.10 -> 0.05", C = c, T = Crest(0.10f, 300f, 0.05f, 300f), Length = 600f };
                LiftTowerPlan off = Off(l), on = l.Edit().Replan(l.T);
                Check(WorstOver(off, out _) <= 0f, W + " " + id, "precondition: the gentle crest is past the cap: " + Angles(off));
                Check(PairNodes(on).Count == 0 && on.AngleNotes.Count == 0 && Same(off, on), W + " " + id,
                      "a crest within the cap changed: " + Angles(off) + " -> " + Angles(on));
            }
            // The sweep: crests from gentle to sharp on every chair type. Every result is within the caps or flagged OverCap with a note;
            // each branch binds at least once (a single fix, a pair, nothing needed).
            int singles = 0, pairs = 0, none = 0, flagged = 0, lines = 0;
            foreach (string id in ChairTypes)
            {
                // the bend sweep: the GD faces reach 1.1 (47.7 deg), past the GD pitch cap, whose repair is PC-1's to judge
                LiftTowerPlanConstants c = NoPitchCap(LiftTowerPlanConstants.For(id));
                float gmax = id == "fixedGrip" ? 0.4f : id == "detachable" ? 0.8f : 1.1f;
                for (float g1 = 0.05f; g1 <= gmax + 1e-3f; g1 += 0.05f)
                foreach (float g2 in new[] { -0.1f, 0f, 0.05f })
                foreach (float up in new[] { 150f, 230f, 310f })
                {
                    var l = new Line { Label = id, C = c, T = Crest(g1, up, g2, 600f - up), Length = 600f };
                    LiftTowerPlan off = Off(l), on = l.Edit().Replan(l.T);
                    if (!off.Clear)
                        continue;
                    lines++;
                    bool anyOver = WorstOver(off, out _) > LiftTowerPlan.AngleTolDeg;
                    bool hasPair = PairNodes(on).Count > 0;
                    bool hasFlag = false;
                    foreach (LiftPlanNode n in on.Nodes)
                        hasFlag |= (n.Flags & LiftPlanNodeFlags.OverCap) != 0;
                    if (!anyOver) none++;
                    else if (hasPair) pairs++;
                    else if (!hasFlag) singles++;
                    if (hasFlag) flagged++;
                    // every tower within its cap, or the flagged one with its note
                    for (int i = 0; i < on.Nodes.Length; i++)
                    {
                        LiftPlanNode n = on.Nodes[i];
                        if (!n.HasAngles || math.abs(n.DeflectionDeg) <= n.CapDeg + LiftTowerPlan.AngleTolDeg)
                            continue;
                        Check((n.Flags & LiftPlanNodeFlags.OverCap) != 0 || hasFlag, W + " sweep " + id + " g1 " + F(g1) + " g2 " + F(g2) + " up " + up,
                              "a tower past its cap, not flagged: " + Angles(on));
                    }
                    Check(on.Clear || !off.Clear, W + " sweep " + id, "the repair broke a clear line: " + LiftPlannerTests.Describe(on));
                    Check(Group(on) <= math.max(2, Group(off)), W + " sweep " + id, "the repair made a group of three: " + Angles(on));
                    Check(on.LineTowers <= off.LineTowers + PairCount(off), W + " sweep " + id, "more than one tower added per crest");
                }
            }
            s_Report.Add("BO-2 crest sweep (FG / DQ / GD, 600 m, g1 0.05..max, g2 -0.1 / 0 / 0.05) | every tower within its cap or flagged; each branch binds | " +
                         lines + " clear line(s): " + none + " within the caps, " + singles + " fixed by one shifted / re-membered tower, " + pairs +
                         " with a pair, " + flagged + " with a tower no repair fixes (flagged)");
            Check(singles > 0 && pairs > 0 && none > 0, W, "a branch never binds: singles " + singles + ", pairs " + pairs + ", none " + none);
        }

        private static int PairCount(LiftTowerPlan p)
        {
            int n = 0;
            foreach (LiftPlanNode x in p.Nodes)
                if (x.HasAngles && math.abs(x.DeflectionDeg) > x.CapDeg + LiftTowerPlan.AngleTolDeg)
                    n++;
            return n;
        }

        private static bool Same(LiftTowerPlan a, LiftTowerPlan b)
        {
            if (a.Nodes.Length != b.Nodes.Length)
                return false;
            for (int i = 0; i < a.Nodes.Length; i++)
                if (math.abs(a.Nodes[i].AlongM - b.Nodes[i].AlongM) > 1e-3f || a.Nodes[i].Member != b.Nodes[i].Member)
                    return false;
            return true;
        }

        // ── BO-3: a pinned crest tower keeps its spot and gets a free partner ─────────────────────────────────────────────────
        // The player pins a tower on the ground's break (FG 200 m, DQ / GD 500 m); the free planner builds around it, the pin is past
        // its cap, the repair keeps its spot (member may change) and adds one free partner 10-40 m beside it.
        private static void Bo3PinnedCrest()
        {
            foreach (string id in ChairTypes)
            {
                string W = "angles/BO-3 " + id;
                Line l = CrestLine(id);
                // FG pinned on the break itself (200 m) stays flagged, 1.4 deg past its cap: no partner fits between its 90 m max span
                // and the no-three rule; pinned 5 m before it, the partner fits. DQ and GD are pinned on the break.
                float crest = id == "fixedGrip" ? l.Break - 5f : l.Break;
                LiftLineEdit e = l.Edit();
                Check(e.AddPin(crest) == LiftLineEditResult.Done, W, "pin at the crest refused");
                LiftTowerPlan p = e.Replan(l.T);
                int pin = -1;
                for (int i = 1; i < p.Nodes.Length - 1; i++)
                    if (math.abs(p.Nodes[i].AlongM - crest) < 1e-3f)
                        pin = i;
                Check(pin > 0, W, "the pin moved: " + Angles(p));
                List<int> pair = PairNodes(p);
                bool partner = pin > 0 && pair.Contains(pin) && pair.Count == 2 && math.abs(pair[0] - pair[1]) == 1 &&
                               !e.IsPinned(p.Nodes[pair[0] == pin ? pair[1] : pair[0]].AlongM);
                Check(partner, W, "no free partner beside the pin: " + Angles(p) + " | " + string.Join(" | ", p.AngleNotes));
                Check(p.AngleNotes.Count >= 1 && p.AngleNotes[0].Contains("pinned / seed") && p.AngleNotes[0].Contains("PARTNER"), W,
                      "the pin was not the repaired tower: " + string.Join(" | ", p.AngleNotes));
                Check(Group(p) <= 2, W, "three towers within 40 m: " + Angles(p));
                Check(WorstOver(p, out _) <= LiftTowerPlan.AngleTolDeg && p.Clear, W, "past a cap or not clear: " + Angles(p));
                List<LiftLineNode> emitted = e.Emit();
                int free = 0;
                foreach (LiftLineNode n in emitted)
                    if (n.Kind == LiftLineNodeKind.Free && pair.Count == 2 && (math.abs(n.AlongM - p.Nodes[pair[0]].AlongM) < 1e-3f || math.abs(n.AlongM - p.Nodes[pair[1]].AlongM) < 1e-3f))
                        free++;
                Check(free == 1, W, "the partner is not emitted as a Free node (" + free + ")");
                s_Report.Add("BO-3 " + l.Label + ", pin at the crest " + F(crest, "F0") + " m | pin kept, a free partner 10-40 m beside it | " +
                             Angles(p) + " | " + string.Join(" | ", p.AngleNotes));
            }
        }

        // ── BO-4: removing a partner: the removed spot stays free, the pins stay; the other side or single + flag ───────────────
        private static void Bo4RemovedPartner()
        {
            foreach (string id in ChairTypes)
            {
                string W = "angles/BO-4 " + id;
                Line l = CrestLine(id);
                LiftLineEdit e = l.Edit();
                LiftTowerPlan p = e.Replan(l.T);
                List<int> pair = PairNodes(p);
                if (!Check(pair.Count == 2, W, "no pair to remove from: " + Angles(p)))
                    continue;
                float removed = p.Nodes[pair[1]].AlongM;
                var pinsBefore = new List<float>();
                for (int i = 1; i < p.Nodes.Length - 1; i++)
                    if (i != pair[1])
                        pinsBefore.Add(p.Nodes[i].AlongM);
                Check(e.RemoveTower(removed) == LiftLineEditResult.Done, W, "remove refused");
                LiftTowerPlan q = e.Replan(l.T);
                foreach (float s in pinsBefore)
                {
                    bool kept = false;
                    foreach (LiftPlanNode n in q.Nodes)
                        kept |= math.abs(n.AlongM - s) < 1e-3f;
                    Check(kept, W, "pin at " + F(s, "F0") + " m gone: " + Angles(q));
                }
                bool near = false;
                foreach (LiftPlanNode n in q.Nodes)
                    near |= !n.IsStation && math.abs(n.AlongM - removed) < e.RemovedRadiusM - 1e-3f;
                Check(!near, W, "a tower back within " + F(e.RemovedRadiusM, "F0") + " m of the removed spot " + F(removed, "F0") + ": " + Angles(q));
                bool flagged = false;
                foreach (LiftPlanNode n in q.Nodes)
                    flagged |= (n.Flags & LiftPlanNodeFlags.OverCap) != 0;
                bool ok = WorstOver(q, out _) <= LiftTowerPlan.AngleTolDeg || flagged;
                Check(ok, W, "past a cap and not flagged: " + Angles(q));
                // the free planner may need a filler where the partner closed a span (MaxSpanM); never a group of three
                Check(Group(q) <= 2, W, "three towers within 40 m: " + Angles(q));
                Check(q.LineTowers <= p.LineTowers + 1, W, "removing a partner added more than a filler: " + p.LineTowers + " -> " + q.LineTowers);
                s_Report.Add("BO-4 " + l.Label + ", partner at " + F(removed, "F0") + " m removed | pins kept, no tower within " + F(e.RemovedRadiusM, "F0") +
                             " m, never three in a row; a partner (other side, or a filler as the pin's partner) or single + flag | " + Angles(q) + " | " + string.Join(" | ", q.AngleNotes));
            }
        }

        // ── BO-5: a junction end is judged with the built neighbor's rope point and gets its partner inside the section ─────────
        private static void Bo5Junction()
        {
            foreach (string id in ChairTypes)
            {
                string W = "angles/BO-5 " + id;
                LiftTowerPlanConstants c = id == "fixedGrip" ? WithoutTwins(id) : LiftTowerPlanConstants.For(id);   // FG: the B1 case (cap 16)
                // The section starts at a loose end on a crest: the built line arrived climbing at g (its far node 80 m back, a tower of the
                // shortest member), this section runs on level ground. FG at 0.15 is past its cap by 1.5 deg and has no partner: one 4 m
                // ladder step on the partner bends the rope 20 deg toward the next tower 80 m on (the repair touches the pair only), so
                // it is flagged with its reason; DQ and GD get their partner.
                float g = id == "fixedGrip" ? 0.15f : id == "detachable" ? 0.6f : 0.9f;
                bool expectPartner = id != "fixedGrip";
                LiftTerrainSampler t = Ground(0f, 1000f, 400f, 1000f);
                float len = 400f, ya = 1000f;
                var e = new LiftLineEdit(c, new float3(0f, ya, 0f), new float3(len, ya, 0f), ya + c.RopeAboveBase(0), ya + c.RopeAboveBase(0), false, true);
                LiftTowerPlan without = e.Replan(t);
                e.OuterDistA = 80f;
                e.OuterRopeA = ya - g * 80f + c.RopeAboveBase(0);
                LiftTowerPlan p = e.Replan(t);
                Check(!without.Nodes[0].HasAngles && p.Nodes[0].HasAngles, W, "the junction end is not judged with its outer point (or judged without)");
                Check(p.Nodes[0].DeflectionDeg > p.Nodes[0].CapDeg || (p.Nodes[0].Flags & LiftPlanNodeFlags.Pair) != 0, W,
                      "precondition: the junction is within its cap: " + Angles(p));
                bool pair0 = (p.Nodes[0].Flags & LiftPlanNodeFlags.Pair) != 0 && p.Nodes.Length > 2 && (p.Nodes[1].Flags & LiftPlanNodeFlags.Pair) != 0;
                float gap = p.Nodes.Length > 2 ? p.Nodes[1].AlongM : 0f;
                if (expectPartner)
                {
                    Check(pair0 && gap >= 10f - 1e-3f && gap <= 40f + 1e-3f, W, "no partner 10-40 m inside the section: " + Angles(p) + " | " + string.Join(" | ", p.AngleNotes));
                    Check(WorstOver(p, out _) <= LiftTowerPlan.AngleTolDeg, W, "the junction or a tower past its cap: " + Angles(p));
                }
                else
                {
                    Check(!pair0 && (p.Nodes[0].Flags & LiftPlanNodeFlags.OverCap) != 0 && p.AngleNotes.Count == 1 && p.AngleNotes[0].Contains("NO REPAIR"),
                          W, "not flagged with its reason: " + Angles(p) + " | " + string.Join(" | ", p.AngleNotes));
                    Check(p.LineTowers == without.LineTowers, W, "the flagged junction changed the towers");
                }
                s_Report.Add("BO-5 " + id + " section from a loose end on a " + F(g) + " climb | the junction judged; " +
                             (expectPartner ? "its partner inside the section" : "flagged with its reason (no partner fits)") + " | without the outer point: " +
                             Angles(without) + " | with: " + Angles(p) + " | " + string.Join(" | ", p.AngleNotes));
            }
        }

        // ── BO-6: a crest no pair fixes stays single, flagged, with the reason ──────────────────────────────────────────────────
        private static void Bo6NoRepair()
        {
            const string W = "angles/BO-6";
            // FG over a 0.6 -> 0 crest: about 39 deg on one tower, about 24 on each of a pair (the cap is 16).
            var l = new Line { Label = "FG cliff crest 0.60 -> 0.00 (no twins)", C = WithoutTwins("fixedGrip"), T = Crest(0.6f, 150f, 0f, 250f), Length = 400f };
            LiftTowerPlan off = Off(l), on = l.Edit().Replan(l.T);
            int flagged = 0;
            foreach (LiftPlanNode n in on.Nodes)
                flagged += (n.Flags & LiftPlanNodeFlags.OverCap) != 0 ? 1 : 0;
            Check(WorstOver(off, out _) > 0f, W, "precondition: the cliff is within the cap: " + Angles(off));
            Check(flagged >= 1 && PairNodes(on).Count == 0, W, "not single + flag: " + Angles(on) + " | " + string.Join(" | ", on.AngleNotes));
            Check(on.LineTowers == off.LineTowers, W, "towers changed: " + off.LineTowers + " -> " + on.LineTowers);
            bool reason = false;
            foreach (string s in on.AngleNotes)
                reason |= s.Contains("NO REPAIR") && s.Contains("refused");
            Check(reason, W, "no reason logged: " + string.Join(" | ", on.AngleNotes));
            s_Report.Add("BO-6 " + l.Label + " | single, flagged OverCap, reason logged | " + Angles(on) + " | " + string.Join(" | ", on.AngleNotes));
        }

        // ── BO-7: a dip past the hold-down cap gets a (hold-down) pair ──────────────────────────────────────────────────────────
        private static void Bo7DipPair()
        {
            const string W = "angles/BO-7";
            // FG V valley, 0.5 down to 160 m and up: the free plan stands its shortest tower at the bottom, -45.6 deg (hold-down cap 16); one taller
            // tower there cannot take it, a pair can.
            var l = new Line { Label = "FG V valley 0.50", C = LiftTowerPlanConstants.For("fixedGrip"), T = Ground(0f, 1100f, 160f, 1020f, 400f, 1140f), Length = 400f };
            LiftTowerPlan off = Off(l), on = l.Edit().Replan(l.T);
            float worstOff = WorstOver(off, out int at);
            Check(worstOff > 0f && at >= 0 && off.Nodes[at].DeflectionDeg < 0f, W, "precondition: no concave tower past its cap: " + Angles(off));
            List<int> pair = PairNodes(on);
            Check(pair.Count == 2 && WorstOver(on, out _) <= LiftTowerPlan.AngleTolDeg && on.Clear && Group(on) == 2, W,
                  "no hold-down pair under the cap: " + Angles(on) + " | " + string.Join(" | ", on.AngleNotes));
            s_Report.Add("BO-7 " + l.Label + " | a pair in the dip, both under the cap | without: " + Angles(off) + " | with: " + Angles(on) + " | " + string.Join(" | ", on.AngleNotes));
        }

        // ── BO-8: determinism and Plan(Plan) a fixed point with pairs on ─────────────────────────────────────────────────────
        private static void Bo8Determinism()
        {
            const string W = "angles/BO-8";
            var lines = new List<Line>();
            foreach (string id in ChairTypes)
                lines.Add(CrestLine(id));
            lines.Add(new Line { Label = "FG V valley 0.50", C = LiftTowerPlanConstants.For("fixedGrip"), T = Ground(0f, 1100f, 160f, 1020f, 400f, 1140f), Length = 400f });
            foreach (Line l in lines)
            {
                float ya = l.T(0f, 0f), yb = l.T(l.Length, 0f);
                float ra = ya + l.C.RopeAboveBase(0), rb = yb + l.C.RopeAboveBase(0);
                var pl = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = ya, EndNodeYB = yb, Pairs = true };
                LiftTowerPlan a = pl.Plan(l.T, l.Length, ra, rb, l.C);
                LiftTowerPlan b = pl.Plan(l.T, l.Length, ra, rb, l.C);
                Check(Same(a, b) && a.AngleWork == b.AngleWork, W + " " + l.Label, "planning twice differs");
                LiftTowerPlan again = pl.Plan(l.T, l.Length, ra, rb, l.C, a.Nodes);
                Check(Same(a, again), W + " " + l.Label, "Plan(Plan) is not a fixed point: " + Angles(a) + " -> " + Angles(again) + " | " +
                      string.Join(" | ", again.AngleNotes));
            }
        }

        // ── HD-1 / HD-2: kinds on every plan (the fit's PlanFixed included) ──────────────────────────────────────────────────
        private static void Hd1Hd2Kinds()
        {
            // A tower at the bottom of a symmetric V (PlanFixed: the fit's path, kinds on every plan). FG: hold-down below a -15.2 deg
            // chord change, push-pull between -15.2 and -5.5; DQ / GD -18.7 / -7.2.
            foreach (string id in ChairTypes)
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                float strong = id == "fixedGrip" ? 0.16f : 0.19f, moderate = id == "fixedGrip" ? 0.09f : 0.11f;
                foreach (bool hold in new[] { true, false })
                {
                    string W = "angles/" + (hold ? "HD-1 " : "HD-2 ") + id;
                    float g = hold ? strong : moderate;
                    float half = id == "fixedGrip" ? 80f : 150f;
                    LiftTerrainSampler t = Ground(0f, 1000f + g * half, half, 1000f, 2f * half, 1000f + g * half);
                    float rope = 1000f + g * half + c.RopeAboveBase(0);
                    var pl = new LiftTowerPlanner();
                    LiftTowerPlan p = pl.PlanFixed(t, 2f * half, rope, rope, c, new[] { half }, new[] { 0 }, null, assign: false);
                    LiftPlanNode n = p.Nodes[1];
                    LiftTrainKind want = hold ? LiftTrainKind.HoldDown : LiftTrainKind.PushPull;
                    LiftTowerHead head = hold ? LiftTowerHead.Hold : LiftTowerHead.Combo;
                    Check(n.HasAngles && n.Kind == want, W, "kind " + n.Kind + " (d " + F(n.DeflectionDeg) + ", tight " + F(n.TightDeg) + ", heavy " +
                          F(n.HeavyDeg) + "), want " + want);
                    Check(n.Head == head && n.Member == 0, W, "head " + n.Head + " on member " + n.Member + ", want " + head + " on 0");
                    // E = Top(twin) - Top(ladder[0]) (the manifests: FG Hold 0.2 / Combo 0.35, DQ 0.5178 / 0.2589, GD 0.568 / 0.284)
                    float e = LiftTowerEdit.ElevationM(c, n.Member, n.Head);
                    float manifest = id == "fixedGrip" ? (hold ? 0.2f : 0.35f) : id == "detachable" ? (hold ? 0.5178f : 0.2589f) : (hold ? 0.568f : 0.284f);
                    Check(math.abs(e - manifest) < 1e-4f && math.abs(e - (c.TopOf(0, head) - c.Ladder[0].TopM)) < 1e-6f, W,
                          "E " + F(e, "F4") + ", manifest " + F(manifest, "F4"));
                    Check(n.RopeM == 1000f + c.RopeAboveBase(0), W, "the head moved the rope (a twin's lanes are its member's)");
                    Check(n.CapDeg == c.CapDeg(want, head), W, "the node's cap is not its head's train cap");
                    s_Report.Add((hold ? "HD-1 " : "HD-2 ") + id + " V " + F(g) + " | " + want + " -> " + c.NameOf(0, head) + ", E " + F(e, "F4") +
                                 ", rope unchanged | " + p.AngleText(1));
                }
            }
        }

        // ── BO-9: the FG Break head takes a crest a support train cannot (no pair) ───────────────────────────────────────────
        private static void Bo9BreakHead()
        {
            const string W = "angles/BO-9";
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("fixedGrip");
            var l = new Line { Label = "FG crest 0.20 -> 0.05", C = c, T = Crest(0.20f, 200f, 0.05f, 200f), Length = 400f };
            LiftTowerPlan off = Off(l), on = l.Edit().Replan(l.T), bare = Off(CrestLine("fixedGrip"));
            int breaks = 0;
            bool lineOver = false;
            foreach (LiftPlanNode n in on.Nodes)
            {
                if (!n.HasAngles || n.Member < 0)
                    continue;
                if (n.Head == LiftTowerHead.Break)
                {
                    breaks++;
                    Check(n.Kind == LiftTrainKind.Support && math.abs(n.DeflectionDeg) > c.LineCapDeg(LiftTrainKind.Support) - LiftAlignMath.KindHystDeg &&
                          n.CapDeg == 32f, W, "a Break head off its rule: " + Angles(on));
                }
                lineOver |= n.Head == LiftTowerHead.Line && n.Kind == LiftTrainKind.Support &&
                            math.abs(n.DeflectionDeg) > c.LineCapDeg(LiftTrainKind.Support) + LiftTowerPlan.AngleTolDeg;
            }
            Check(WorstOver(bare, out _) > 0f, W, "precondition: the crest is within the line cap 16 without twins: " + Angles(bare));
            Check(breaks >= 1 && !lineOver, W, "no Break head on the crest (or a support head past 16): " + Angles(on));
            Check(PairNodes(on).Count == 0 && on.LineTowers == off.LineTowers && on.Clear && WorstOver(on, out _) <= LiftTowerPlan.AngleTolDeg, W,
                  "the crest got a pair or a cap is exceeded: " + Angles(on) + " | " + string.Join(" | ", on.AngleNotes));
            s_Report.Add("BO-9 " + l.Label + " | the Break head (cap 32) instead of a pair; no tower more | without twins: " + Angles(CrestLine("fixedGrip").Edit().Replan(l.T)) +
                         " | with: " + Angles(on));
        }

        // ── HD-3: a member without the twin -> the nearest member with it, or the support head + a note ───────────────────────────
        private static void Hd3NoTwin()
        {
            const string W = "angles/HD-3";
            // DQ V dips (half 150 m, g 0.18-0.34, station ropes 15 m up) with the bottom tower given as DQ14 or DQ16 (no twins): a hold-down
            // tower takes the nearest member with a Hold twin that keeps both spans clear and still bends hold-down, else keeps its support
            // head with a note (the case after the sweep binds that branch for certain).
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For("detachable");
            const float half = 150f;
            int moved = 0, stayed = 0;
            string movedNote = null, stayedNote = null;
            LiftTerrainSampler t = null;
            float rope = 0f;
            for (float g = 0.18f; g <= 0.34f + 1e-4f; g += 0.02f)
            foreach (int given in new[] { 2, 3 })
            {
                t = Ground(0f, 1000f + g * half, half, 1000f, 2f * half, 1000f + g * half);
                rope = 1000f + g * half + 15f;   // stations with a high rope: the spans clear a short tower, the bottom one bends hold-down
                LiftTowerPlan p = new LiftTowerPlanner().PlanFixed(t, 2f * half, rope, rope, c, new[] { half }, new[] { given }, null, assign: false);
                LiftPlanNode n = p.Nodes[1];
                string where = W + " g " + F(g) + " " + c.Ladder[given].Name;
                if (n.Kind != LiftTrainKind.HoldDown)
                    continue;
                if (n.Member != given)
                {
                    moved++;
                    movedNote = movedNote ?? "g " + F(g) + ", " + Short(c.Ladder[given].Name) + " given: " + p.AngleNotes[0];
                    Check(n.Head == LiftTowerHead.Hold && c.HasTwin(n.Member, LiftTowerHead.Hold) && n.RopeM == 1000f + c.RopeAboveBase(n.Member) &&
                          p.Spans[0].MinFloorMarginM >= -0.011f && p.Spans[1].MinFloorMarginM >= -0.011f, where,
                          "the fallback " + c.NameOf(n.Member, n.Head) + " is not a clear Hold twin: " + LiftPlannerTests.Describe(p));
                    Check(math.abs(n.Member - given) >= NearestTwin(c, given, LiftTowerHead.Hold), where, "nearer than the nearest member with the twin");
                    Check(p.AngleNotes.Count == 1 && p.AngleNotes[0].Contains("has no hold-down twin ->"), where, "no fallback note: " + string.Join(" | ", p.AngleNotes));
                }
                else
                {
                    stayed++;
                    stayedNote = stayedNote ?? "g " + F(g) + ", " + Short(c.Ladder[given].Name) + " given: " + p.AngleNotes[0];
                    Check(n.Head == LiftTowerHead.Line && p.AngleNotes.Count == 1 && p.AngleNotes[0].Contains("the support head stays"), where,
                          "neither a fallback nor the support note: " + c.NameOf(n.Member, n.Head) + " | " + string.Join(" | ", p.AngleNotes));
                }
            }
            Check(moved > 0, W, "the fallback never binds: fallback " + moved + ", support + note " + stayed);
            s_Report.Add("HD-3 DQ V sweep, DQ14 / DQ16 given | the nearest member with a Hold twin, else the support head + a note | " + moved +
                         " fallback(s), " + stayed + " kept | " + movedNote + " | " + (stayedNote ?? "-"));
            t = Ground(0f, 1000f + 0.19f * half, half, 1000f, 2f * half, 1000f + 0.19f * half);
            rope = 1000f + 0.19f * half + c.RopeAboveBase(0);
            // No member with the twin is allowed (the only Hold twin is on a member past the max rope height): the support head stays,
            // with its note; nothing else changes.
            var only = new[] { new LiftTowerTwin(5, LiftTowerHead.Hold, "TestDQ20HoldA", 20.4678f) };
            LiftTowerPlanConstants c2 = c.With(twins: only, maxRopeAboveGroundM: 12f);
            LiftTowerPlan q = new LiftTowerPlanner().PlanFixed(t, 2f * half, rope, rope, c2, new[] { half }, new[] { 0 }, null, assign: false);
            LiftPlanNode m = q.Nodes[1];
            Check(m.Kind == LiftTrainKind.HoldDown && m.Head == LiftTowerHead.Line && m.Member == 0 && m.CapDeg == c2.LineCapDeg(LiftTrainKind.HoldDown), W,
                  "no allowed twin: " + c2.NameOf(m.Member, m.Head) + ", kind " + m.Kind);
            Check(q.AngleNotes.Count == 1 && q.AngleNotes[0].Contains("the support head stays"), W, "no support note: " + string.Join(" | ", q.AngleNotes));
            s_Report.Add("HD-3 DQ V, the only Hold twin on a member past the max rope height | the support head + a note | " + q.AngleNotes[0]);
        }

        private static int NearestTwin(LiftTowerPlanConstants c, int m, LiftTowerHead h)
        {
            for (int step = 1; step < c.Ladder.Length; step++)
                if ((m - step >= 0 && c.HasTwin(m - step, h)) || (m + step < c.Ladder.Length && c.HasTwin(m + step, h)))
                    return step;
            return -1;
        }

        private static string Short(string name) => name.Replace("SkiLiftTower", string.Empty);

        // ── HD-4: MemberOf round trip, the engine winner over the twins, the per-load filter ──────────────────────────────────
        private static void Hd4MemberOf()
        {
            const string W = "angles/HD-4";
            int pairs = 0;
            foreach (string id in ChairTypes)
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                var all = new List<(int m, LiftTowerHead h)>();
                for (int m = 0; m < c.Ladder.Length; m++)
                    all.Add((m, LiftTowerHead.Line));
                foreach (LiftTowerTwin tw in c.Twins)
                    all.Add((tw.Member, tw.Head));
                // tops unique by >= 0.1 m (MemberOf matches at 0.05; the engine's score has one winner)
                for (int a = 0; a < all.Count; a++)
                    for (int b = a + 1; b < all.Count; b++)
                        Check(math.abs(c.TopOf(all[a].m, all[a].h) - c.TopOf(all[b].m, all[b].h)) >= 0.1f - 1e-5f, W + " " + id,
                              c.NameOf(all[a].m, all[a].h) + " and " + c.NameOf(all[b].m, all[b].h) + " tops closer than 0.1 m");
                foreach (var (m, h) in all)
                {
                    float e = LiftTowerEdit.ElevationM(c, m, h);
                    int back = LiftTowerEdit.MemberOf(c, e, out LiftTowerHead hb);
                    Check(back == m && hb == h, W + " " + id, c.NameOf(m, h) + " E " + F(e, "F4") + " reads back as " + c.NameOf(back, hb));
                    // a few centimeters of drift (a carve, float rounding) still reads back
                    Check(LiftTowerEdit.MemberOf(c, e + 0.03f, out LiftTowerHead hd) == m && hd == h, W + " " + id, c.NameOf(m, h) + " + 3 cm misread");
                    int win = LiftTowerEdit.EngineWinner(c, e, out LiftTowerHead hw, out float score);
                    Check(win == m && hw == h && math.abs(score - 1f) < 1e-5f, W + " " + id, "the engine spawns " + (win < 0 ? "a tie" : c.NameOf(win, hw)) +
                          " (score " + F(score, "F3") + ") at E " + F(e, "F4") + ", planned " + c.NameOf(m, h));
                    Check(math.abs(LiftTowerEdit.EnginePivotAboveGround(c, m, h, e)) < 1e-4f && e <= c.EngineElevationCapM + 1e-4f, W + " " + id,
                          c.NameOf(m, h) + " pivot off the ground or E past m_ElevationRange.max");
                    pairs++;
                }
                // off the table: between the second member and its nearest twin -> the tallest support member not above it, Line
                float off = LiftTowerEdit.ElevationM(c, 1) + 0.12f;
                Check(LiftTowerEdit.MemberOf(c, off, out LiftTowerHead ho) == 1 && ho == LiftTowerHead.Line, W + " " + id, "off-table E " + F(off) + " misread");
                Check(LiftTowerEdit.MemberOf(c, -0.4f, out LiftTowerHead hn) == 0 && hn == LiftTowerHead.Line, W + " " + id, "a node below the ground misread");
            }
            // The per-load filter: no twin loaded -> no twins, FG back at the line cap; a partial set keeps only the loaded ones.
            try
            {
                LiftTowerPlanConstants.TwinFilter = _ => false;
                LiftTowerPlanConstants none = LiftTowerPlanConstants.For("fixedGrip");
                Check(none.Twins.Length == 0 && none.CapDeg(LiftTrainKind.Support) == 16f, W + " filter", "no twin loaded, still " + none.Twins.Length);
                LiftTowerPlanConstants.TwinFilter = name => name != "SkiLiftTowerFG20BreakA";
                LiftTowerPlanConstants part = LiftTowerPlanConstants.For("fixedGrip");
                Check(part.Twins.Length == 7 && !part.BreakEverywhere && part.CapDeg(LiftTrainKind.Support) == 16f && part.HasTwin(0, LiftTowerHead.Break),
                      W + " filter", "a missing FG20 Break did not drop the Break cap");
            }
            finally
            {
                LiftTowerPlanConstants.TwinFilter = null;
            }
            int total = 0;
            foreach (LiftTowerTwin _ in LiftTowerPlanConstants.AllTwins())
                total++;
            Check(total == 16, W, total + " twins in the tables, want the 16 manifests");
            s_Report.Add("HD-4 every (member, head) of FG / DQ / GD | MemberOf round trip, engine winner score 1 with the pivot on the ground, " +
                         "tops >= 0.1 m apart, off-table -> support member | " + pairs + " (member, head) pairs, " + total + " twins");
        }

        // ── HD-6: Emit writes E of the placed head ────────────────────────────────────────────────────────────────────────
        private static void Hd6Emit()
        {
            const string W = "angles/HD-6";
            var lines = new List<Line>
            {
                new Line { Label = "FG crest 0.20 -> 0.05", C = LiftTowerPlanConstants.For("fixedGrip"), T = Crest(0.20f, 200f, 0.05f, 200f), Length = 400f },
                new Line { Label = "FG V valley 0.50", C = LiftTowerPlanConstants.For("fixedGrip"), T = Ground(0f, 1100f, 160f, 1020f, 400f, 1140f), Length = 400f },
                new Line { Label = "DQ dip 0.30 -> 0.50", C = LiftTowerPlanConstants.For("detachable"), T = Crest(0.30f, 600f, 0.50f, 600f), Length = 1200f },
                new Line { Label = "GD dip 0.30 -> 0.50", C = LiftTowerPlanConstants.For("gondola"), T = Crest(0.30f, 600f, 0.50f, 600f), Length = 1200f },
            };
            var seen = new HashSet<LiftTowerHead>();
            var sb = new StringBuilder();
            foreach (Line l in lines)
            {
                LiftLineEdit e = l.Edit();
                LiftTowerPlan p = e.Replan(l.T);
                List<LiftLineNode> nodes = e.Emit();
                if (!Check(nodes.Count == p.Nodes.Length, W + " " + l.Label, "Emit count " + nodes.Count + " vs plan " + p.Nodes.Length))
                    continue;
                for (int i = 1; i < nodes.Count - 1; i++)
                {
                    LiftPlanNode pn = p.Nodes[i];
                    float want = LiftTowerEdit.ElevationM(l.C, pn.Member, pn.Head);
                    Check(nodes[i].Head == pn.Head && nodes[i].Member == pn.Member && math.abs(nodes[i].ElevationM - want) < 1e-5f &&
                          math.abs(nodes[i].Position.y - (pn.GroundM + want)) < 1e-3f, W + " " + l.Label,
                          "node " + (i + 1) + " " + l.C.NameOf(pn.Member, pn.Head) + " emitted at E " + F(nodes[i].ElevationM, "F4") + ", want " + F(want, "F4"));
                    seen.Add(pn.Head);
                }
                sb.Append(sb.Length > 0 ? " | " : string.Empty).Append(l.Label).Append(": ").Append(Angles(p));
            }
            Check(seen.Contains(LiftTowerHead.Hold) && seen.Contains(LiftTowerHead.Combo) && seen.Contains(LiftTowerHead.Break) && seen.Contains(LiftTowerHead.Line),
                  W, "not every head emitted (" + string.Join(", ", seen) + "): " + sb);
            s_Report.Add("HD-6 Emit | every tower at ground + E(member, head), the head carried | " + sb);
        }

        // ── HD-7: the lane-less end rope equals the lane-anchor rope at every head ───────────────────────────────────────────
        // The end node stands at ground + E(member, head), Game.Net.Elevation = (E, E). With a lane anchor the rope is the engine's own: the
        // winner the engine spawns at E (EngineWinner, not MemberOf), its pivot (EnginePivotAboveGround) plus the support member's lane y
        // (a twin's lanes are its member's, check-lift-align LADDER). Without one, the lift tool's EndRope, the fit's end ropes and the diag
        // plan use node y + RopeAboveNodeM(c, E) with E read back from the node (MemberOf). Both go through LiftLineInput.StationRope.
        private static void Hd7LooseEndRope()
        {
            const string W = "angles/HD-7";
            int heads = 0, twinHeads = 0;
            float worstOld = 0f, worstNew = 0f;
            foreach (string id in ChairTypes)
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(id);
                var all = new List<(int m, LiftTowerHead h)>();
                for (int m = 0; m < c.Ladder.Length; m++)
                    all.Add((m, LiftTowerHead.Line));
                foreach (LiftTowerTwin tw in c.Twins)
                    all.Add((tw.Member, tw.Head));
                foreach (var (m, h) in all)
                {
                    const float ground = 1000f;
                    float e = LiftTowerEdit.ElevationM(c, m, h);
                    var node = new float3(12f, ground + e, -7f);
                    // the lane-anchor path: what the engine anchors the rope to
                    int win = LiftTowerEdit.EngineWinner(c, e, out LiftTowerHead hw, out _);
                    float anchorY = ground + LiftTowerEdit.EnginePivotAboveGround(c, win, hw, e) + c.RopeAboveBase(win);
                    var anchors = new List<float3> { new float3(12f, anchorY, -7f) };
                    float viaLane = LiftLineInput.StationRope(anchors, null, node, float.NaN, out bool fromLane);
                    // the no-lane path: the shared fallback, reading the head back from the node's elevation
                    float viaNode = LiftLineInput.StationRope(null, null, node, node.y + LiftTowerEdit.RopeAboveNodeM(c, e), out bool fromNode);
                    Check(fromLane && !fromNode, W + " " + id, c.NameOf(m, h) + ": the anchor / fallback switch broke");
                    Check(math.abs(viaNode - viaLane) < 1e-4f, W + " " + id, c.NameOf(m, h) + " (E " + F(e, "F3") + "): no-lane rope " + F(viaNode, "F4") +
                          " vs lane rope " + F(viaLane, "F4") + " (" + F(viaNode - viaLane, "F4") + " m)");
                    Check(math.abs(viaLane - (ground + c.RopeAboveBase(m))) < 1e-4f, W + " " + id, c.NameOf(m, h) + ": the lane rope is not ground + RopeAboveBase(member)");
                    float old = node.y + c.RopeAboveBase(0);   // the retired fallback: the member's rope height
                    worstOld = math.max(worstOld, math.abs(old - viaLane));
                    worstNew = math.max(worstNew, math.abs(viaNode - viaLane));
                    if (h != LiftTowerHead.Line)
                    {
                        twinHeads++;
                        Check(math.abs(old - viaLane) >= 0.1f - 1e-4f, W + " " + id, c.NameOf(m, h) + ": the old fallback is not off at a twin, the case has no teeth");
                    }
                    else
                    {
                        Check(math.abs(old - viaLane) < 1e-4f, W + " " + id, c.NameOf(m, h) + ": a Line head moved off RopeAboveBase(0)");
                    }
                    heads++;
                }
                // ground and station-stub ends carry no head: elevation 0 is the shortest member, RopeAboveBase(0)
                Check(LiftTowerEdit.RopeAboveNodeM(c, 0f) == c.RopeAboveBase(0), W + " " + id, "a ground node is not RopeAboveBase(0)");
            }
            Check(twinHeads == 16, W, twinHeads + " twin heads checked, want 16");
            s_Report.Add("HD-7 loose-end rope | node + RopeAboveNodeM(E) equals the engine's lane anchor at every head of FG / DQ / GD | " + heads +
                         " heads (" + twinHeads + " twins), worst gap " + F(worstNew, "F5") + " m (the retired node + RopeAboveBase(0) was off by up to " + F(worstOld, "F3") + " m)");
        }

        // ── HD-5: the fit never adds a tower and its Needed is unchanged ────────────────────────────────────────────────────
        private static void Hd5FitNeverAdds()
        {
            const string W = "angles/HD-5";
            int lines = 0;
            foreach (string id in ChairTypes)
            {
                Line l = CrestLine(id);
                LiftTowerPlan off = Off(l);
                LiftTowerPlan on = l.Edit().Replan(l.T);
                float ya = l.T(0f, 0f), yb = l.T(l.Length, 0f);
                float ra = ya + l.C.RopeAboveBase(0), rb = yb + l.C.RopeAboveBase(0);
                foreach (LiftTowerPlan line in new[] { off, on })
                {
                    var inner = new List<float>();
                    var members = new List<int>();
                    for (int i = 1; i < line.Nodes.Length - 1; i++)
                    {
                        inner.Add(line.Nodes[i].AlongM);
                        members.Add(line.Nodes[i].Member);
                    }
                    var withPairs = new LiftTowerPlanner { Pairs = true };
                    var plain = new LiftTowerPlanner();
                    LiftTowerFit a = withPairs.Fit(l.T, l.Length, ra, rb, l.C, inner, true, 0f, members);
                    LiftTowerFit b = plain.Fit(l.T, l.Length, ra, rb, l.C, inner, true, 0f, members);
                    Check(a.Plan.Nodes.Length == inner.Count + 2, W + " " + id, "the fit changed the node count: " + inner.Count + " -> " + (a.Plan.Nodes.Length - 2));
                    Check(a.Needed == b.Needed && a.Mode == b.Mode && Same(a.Plan, b.Plan), W + " " + id, "Pairs changed the fit: needed " + a.Needed + " vs " + b.Needed);
                    Check(withPairs.Pairs, W + " " + id, "Fit did not restore the Pairs switch");
                    lines++;
                }
            }
            // The first fit after the tool builds: the line's nodes carry the plan's heads (MemberOf reads them back from E); with them as
            // the hysteresis input (no kinds known, as after a load) the fit keeps every member and head: nothing to write.
            int kept = 0;
            var heads = new StringBuilder();
            foreach (Line l in new[]
            {
                new Line { Label = "FG crest 0.20 -> 0.05", C = LiftTowerPlanConstants.For("fixedGrip"), T = Crest(0.20f, 200f, 0.05f, 200f), Length = 400f },
                new Line { Label = "FG V valley 0.50", C = LiftTowerPlanConstants.For("fixedGrip"), T = Ground(0f, 1100f, 160f, 1020f, 400f, 1140f), Length = 400f },
                CrestLine("detachable"), CrestLine("gondola"),
                new Line { Label = "DQ dip 0.30 -> 0.50", C = LiftTowerPlanConstants.For("detachable"), T = Crest(0.30f, 600f, 0.50f, 600f), Length = 1200f },
            })
            {
                LiftTowerPlan p = l.Edit().Replan(l.T);
                float ya = l.T(0f, 0f), yb = l.T(l.Length, 0f);
                var inner = new List<float>();
                var members = new List<int>();
                var hs = new List<LiftTowerHead>();
                var ks = new List<LiftTrainKind>();
                for (int i = 1; i < p.Nodes.Length - 1; i++)
                {
                    inner.Add(p.Nodes[i].AlongM);
                    int m = LiftTowerEdit.MemberOf(l.C, LiftTowerEdit.ElevationM(l.C, p.Nodes[i].Member, p.Nodes[i].Head), out LiftTowerHead h);
                    members.Add(m);
                    hs.Add(h);
                    ks.Add(LiftTrainKind.None);
                }
                var fitter = new LiftTowerPlanner { PreviousKinds = ks, PreviousHeads = hs };
                LiftTowerFit f = fitter.Fit(l.T, l.Length, ya + l.C.RopeAboveBase(0), yb + l.C.RopeAboveBase(0), l.C, inner, false, 0f, members);
                bool same = f.Plan.Nodes.Length == p.Nodes.Length;
                for (int i = 1; same && i < p.Nodes.Length - 1; i++)
                    same = f.Plan.Nodes[i].Member == p.Nodes[i].Member && f.Plan.Nodes[i].Head == p.Nodes[i].Head;
                Check(f.Mode == LiftFitMode.Kept && same, W + " " + l.Label, "the first fit after the tool changes the line: " + f.Mode + " | tool " + Angles(p) +
                      " | fit " + Angles(f.Plan));
                kept++;
                heads.Append(heads.Length > 0 ? "; " : string.Empty).Append(l.Label).Append(" {").Append(p.HeadCounts()).Append('}');
            }
            // A free plan with pairs off has as many towers as the fit's count (Fit forces Pairs off).
            s_Report.Add("HD-5 crest lines FG / DQ / GD, with and without the pair | the fit keeps the node count, Needed unchanged by Pairs | " + lines + " fit(s)");
            s_Report.Add("HD-5 the first fit after the tool | Kept, every member and head as built | " + kept + " line(s): " + heads);
        }

        // ── CO-1: cost: the deterministic work counter under a computed bound; wall time reported ─────────────────────────────
        private static void Co1Cost()
        {
            const string W = "angles/CO-1";
            LiftTowerPlanConstants dq = LiftTowerPlanConstants.For("detachable");
            var cases = new List<Line>
            {
                // LP-27's ordinary 1,200 m DQ at 30 %
                new Line { Label = "DQ 1200 m at 30% (LP-27)", C = dq, T = (s, l) => 1000f + 0.3f * s, Length = 1200f },
                // a 2 km DQ with three sharp crests (0.6 -> 0.05, three times)
                new Line { Label = "DQ 2000 m, three sharp crests", C = dq, Length = 2000f,
                           T = Ground(0f, 1000f, 300f, 1180f, 650f, 1197.5f, 950f, 1377.5f, 1300f, 1395f, 1600f, 1575f, 2000f, 1595f) },
                // the field DQ of LP-26 (1,024 m, uniform; LiftPassTests): top node 1237.33, base 1018.73
                new Line { Label = "field DQ 1024 m (LP-26)", C = dq, Length = 1024f, T = (s, l) => math.lerp(1237.33f, 1018.73f, math.saturate(s / 1024f)) },
            };
            foreach (Line l in cases)
            {
                float ya = l.T(0f, 0f), yb = l.T(l.Length, 0f);
                float ra = ya + l.C.RopeAboveBase(0), rb = yb + l.C.RopeAboveBase(0);
                var on = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = ya, EndNodeYB = yb, Pairs = true };
                var off = new LiftTowerPlanner { CheckEdgeSlope = true, EndNodeYA = ya, EndNodeYB = yb, Pairs = false };
                // warm up (JIT), then time 5 runs each
                LiftTowerPlan p = on.Plan(l.T, l.Length, ra, rb, l.C);
                off.Plan(l.T, l.Length, ra, rb, l.C);
                double msOn = Time(() => on.Plan(l.T, l.Length, ra, rb, l.C)), msOff = Time(() => off.Plan(l.T, l.Length, ra, rb, l.C));
                // Bound (rope-angle evaluations): at most one repair per tower; a repair judges at most the singles (the shift window,
                // a fixed neighbor's partner window included, x members) and the larger of the pairs (first spot x second spot x
                // members^2) and the pulls + partners + re-members, 4 evaluations each (the touched towers and their neighbors), plus 2 cached
                // neighbor values; the worst-first selection evaluates every tower once per round, at most 2 x count + 8 rounds.
                long count = p.Nodes.Length, L = l.C.Ladder.Length;
                long shiftS = (long)math.floor(LiftTowerPlanConstants.AngleShiftM / l.C.StepM + 1e-4f);
                long maxS = (long)math.floor(LiftTowerPlanConstants.PairMaxM / l.C.StepM + 1e-4f);
                long minS = math.max(1, (long)math.ceil(l.C.PairMinM / l.C.StepM - 1e-4f));
                long singles = (2 * maxS + 1) * L, pairs = (maxS + shiftS + 1) * (maxS - minS + 1) * L * L;
                long partners = 2 * 2 * (maxS - minS + 1) * (L + 1) * L + 2 * L * L;   // pulls and partners, both sides; re-members
                long bound = count * (4 * (singles + math.max(pairs, partners)) + 2) + count * (2 * count + 8);
                Check(p.AngleWork <= bound, W + " " + l.Label, "work " + p.AngleWork + " over the bound " + bound);
                s_Report.Add("CO-1 " + l.Label + " | work under the bound; wall time reported | work " + p.AngleWork + " / bound " + bound + "; plan " +
                             F(msOff) + " ms without pairs, " + F(msOn) + " ms with (angle pass " + F(p.AnglesMs, "F3") + " ms); " + Angles(p));
            }
        }

        private static double Time(Action a)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
                a();
            return sw.Elapsed.TotalMilliseconds / 5.0;
        }

        // ── the before / after table and the kind x member histogram (the art round's input) ────────────────────────────────
        private static void Table()
        {
            var hist = new Dictionary<string, int>();
            int lines = 0, pairLines = 0, pairTowers = 0, holdTowers = 0, comboTowers = 0;
            foreach (LiftTypeDef def in SkiCatalog.Lifts)
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(def);
                if (c == null)
                    continue;
                float L = def.Id == "fixedGrip" ? 400f : def.Id == "tram" ? 2000f : 1200f;
                var profiles = new List<Line>();
                foreach (float g in new[] { 0.15f, 0.30f, 0.45f })
                {
                    profiles.Add(new Line { Label = def.Id + " uniform " + F(g), C = c, Length = L, T = (s, l) => 1000f + g * s });
                    profiles.Add(new Line { Label = def.Id + " run-out " + F(g), C = c, Length = L, T = (s, l) => 1000f + g * math.max(0f, s - 100f) });
                    profiles.Add(new Line { Label = def.Id + " crest " + F(g), C = c, Length = L, T = Crest(g + 0.15f, 0.5f * L, g - 0.15f, 0.5f * L) });
                    profiles.Add(new Line { Label = def.Id + " dip " + F(g), C = c, Length = L, T = Crest(g, 0.5f * L, g + 0.2f, 0.5f * L) });
                }
                foreach (Line l in profiles)
                {
                    LiftTowerPlan off = Off(l), on = l.Edit().Replan(l.T);
                    lines++;
                    on.KindCounts(out int s, out int h, out int pp, out int pt, out int ov);
                    off.KindCounts(out int s0, out int h0, out int p0, out _, out int ov0);
                    if (pt > 0) pairLines++;
                    pairTowers += pt;
                    holdTowers += h;
                    comboTowers += pp;
                    for (int i = 0; i < on.Nodes.Length; i++)
                    {
                        LiftPlanNode n = on.Nodes[i];
                        if (!n.HasAngles || n.Member < 0)
                            continue;
                        string key = c.Ladder[n.Member].Name.Replace("SkiLiftTower", "").Replace("SkiTramTower", "T") + " " + LiftAlignMath.KindLetter(n.Kind);
                        hist[key] = hist.TryGetValue(key, out int v) ? v + 1 : 1;
                    }
                    if (pt > 0 || ov0 > 0 || h > 0 || pp > 0)
                        s_Report.Add("TABLE " + l.Label + " | towers " + off.LineTowers + " -> " + on.LineTowers + ", S/H/P " + s0 + "/" + h0 + "/" + p0 + " -> " + s + "/" + h + "/" + pp +
                                     ", over cap " + ov0 + " -> " + ov + ", pair towers " + pt + " | " + Angles(on));
                }
            }
            var keys = new List<string>(hist.Keys);
            keys.Sort(string.CompareOrdinal);
            var sb = new StringBuilder();
            foreach (string k in keys)
                sb.Append(sb.Length > 0 ? ", " : string.Empty).Append(k).Append(' ').Append(hist[k]);
            s_Report.Add("TABLE summary | " + lines + " synthetic line(s) (uniform / run-out / crest / dip at 15 / 30 / 45 %, every type) | " + pairLines +
                         " with a pair (" + pairTowers + " pair towers), hold-down towers " + holdTowers + ", push-pull towers " + comboTowers +
                         " | member x kind (S / H / P, '-' tram): " + sb);
        }
    }
}
