// LiftTypeTests — Catalog Phase B (docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §11.1): the typed
// lift rows. SKELETON written by the core group (§12.1, §12.2 step 1) so LiftBatch builds and runs before the lift group
// lands; the BODY is the lift group's (LT-1 to LT-11, 2026-10-11). Where-tags `lifttype-…`.
//   LT-1  two lifts of different types, apart: two Complete rows, Type 0 and 1, Mixed false, no note.
//   LT-2  FG stations + FG edges + one DQ edge sharing a node: one Unfinished row, Mixed true, "mixes lift types
//         (fixedGrip+detachable)"; Revision moves when the DQ edge is removed and the row returns to Complete.
//   LT-3  DQ edges between FG base and FG top: Unfinished, Mixed (stations count, D18).
//   LT-4  vanilla feeder: an edge typed NoType never unions, the row stays Complete, note "has no lift type — ignored".
//   LT-5  shared FG base used by an FG lift and a DQ line: FG row Complete; DQ component Unfinished + Mixed.
//   LT-6  Row.Type: StationsOnly rows = station type; Mixed row = its base station's type; no-station row = lowest edge.
//   LT-7  LiftServedRule per type: AlongLine 150 and TerminalsOnly 300 (top 299 / 301 m; TopY margin both).
//   LT-8  SummitServed: 299.9 / 300.0 (inclusive) / 300.1 m; unpowered → false; Open (incomplete) → false.
//   LT-9  StationAlign.StubTip(pos, rot, −16) vs (pos, rot) (= −12) on a rotated transform; nitpick B2: the 4-argument tip
//         on the row's line axis x (the tram base −4.00).
//   LT-10 (ride time per speed lives in ResortBatch HubRulesTests section I.)
//   LT-11 (orchestrator addendum 1, dq-family-review-geometry.md F2) the up-lane rule on a MIRRORED two-chain graph:
//         chain A = the cable on the RIGHT looking uphill whichever chain is found first, for an uphill +z lift, its
//         mirror (uphill −z) and a 45° one; SideLookingUphill's signs; a carrier facing its travel with its hanger at
//         local +x keeps the hanger outboard on both chains (LiftLoopCache itself needs Game types: the rule is the pure
//         StationAlign.UpLaneIsSecond it calls).
//   LT-12 (Catalog Phase C, docs/research/art/assets-2026-10/code-round-3/round-3-contract.md §5 / §8.2, rules group; no
//         code change, C19) the tram through the census and the served rule: tram base + top (type 3) and three tram
//         edges (type 3, no carriers) → one Complete row, Type 3, Mixed false, Chairs 0; its served Lift built the way
//         LiftCensusSystem.CopyServeLifts builds it (LiftCensusSystem.cs:347-375, Game side, mirrored here): Rule
//         TerminalsOnly, 300 / 15 from the row type, TopXZ / TopY = the top station's StubTip(pos, rot, its row's
//         StubTipLocalZ −12); a run top 299 m from TopXZ served, 300 m served (inclusive), 301 m not; a run 20 m from the
//         mid-span chord but 500 m from the top not served (an AlongLine lift on the same chords serves it); 299 m away and
//         15 m above TopY served, 16 m above not; unpowered not served; tram edges between fixed-grip stations → Mixed,
//         Unfinished, note "mixes lift types (fixedGrip+tram)", served by nothing.
// Helpers: Program.World (internal; set World.BeforeBuild to stamp LiftCensus.Edge.Type / Station.Type onto the census
// input before Census.Build), Program.StraightLift(), Program.R(index). The R9 convention: `public static class` with
// `public static void Run(Func<bool, string, string, bool> check)`; Main passes its Check. Every oracle is a literal.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class LiftTypeTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private const byte FG = 0, DQ = 1, GD = 2, TR = 3;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Lt1TwoTypesApart();
            Lt2StrayEdgeMixes();
            Lt3LineBetweenOtherStations();
            Lt4UntypedFeeder();
            Lt5SharedBase();
            Lt6RowType();
            Lt7ServedPerType();
            Lt8SummitServed();
            Lt9StubTip();
            Lt11UpLane();
            Lt12Tram();
            s_Check = null;
        }

        // ================================================================== helpers

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }

        private static void Near(float expected, float actual, float tol, string where, string what)
        {
            Check(math.abs(expected - actual) <= tol, where, what + ": expected " + expected.ToString("R") + " ± " + tol +
                  ", got " + actual.ToString("R"));
        }

        /// <summary>Stamps lift types onto the census input by entity index (edges and stations; default = fixed-grip 0,
        /// the struct default, so an unstamped id stays fixed-grip).</summary>
        private static void Stamp(Program.World w, Dictionary<int, byte> edgeTypes, Dictionary<int, byte> stationTypes)
        {
            w.BeforeBuild = (edges, nodes, stations) =>
            {
                for (int i = 0; i < edges.Count; i++)
                {
                    if (edgeTypes != null && edgeTypes.TryGetValue(edges[i].Id.Index, out byte t))
                    {
                        LiftCensus.Edge e = edges[i];
                        e.Type = t;
                        edges[i] = e;
                    }
                }
                for (int i = 0; i < stations.Count; i++)
                {
                    if (stationTypes != null && stationTypes.TryGetValue(stations[i].Id.Index, out byte t))
                    {
                        LiftCensus.Station s = stations[i];
                        s.Type = t;
                        stations[i] = s;
                    }
                }
            };
        }

        /// <summary>A second straight lift, StraightLift's shape shifted by x: base station b, top t, tip nodes n and n+5,
        /// towers n+1..n+4, sections e..e+4.</summary>
        private static void AddLift(Program.World w, int b, int t, int n, int e, float x)
        {
            w.Station(b, true, new float3(x, 0f, 0f), 180f);
            w.Station(t, false, new float3(x, 100f, 600f), 0f);
            float3 a = w.Tip(b), c = w.Tip(t);
            w.Node(n, a, station: b);
            for (int k = 1; k <= 4; k++)
                w.Node(n + k, math.lerp(a, c, k / 5f) + new float3(0f, 8f, 0f));
            w.Node(n + 5, c, station: t);
            for (int k = 0; k < 5; k++)
                w.Edge(e + k, n + k, n + k + 1);
        }

        private static bool NoteContains(Program.World w, string text)
        {
            for (int i = 0; i < w.Census.Notes.Count; i++)
                if (w.Census.Notes[i].Contains(text))
                    return true;
            return false;
        }

        private static string Notes(Program.World w) => w.Census.Notes.Count == 0 ? "(none)" : string.Join(" | ", w.Census.Notes);

        private static Dictionary<int, byte> Types(params int[] idTypePairs)
        {
            var d = new Dictionary<int, byte>();
            for (int i = 0; i + 1 < idTypePairs.Length; i += 2)
                d[idTypePairs[i]] = (byte)idTypePairs[i + 1];
            return d;
        }

        // ================================================================== LT-1 .. LT-6: the typed census

        private static void Lt1TwoTypesApart()
        {
            const string W = "lifttype-1 two types apart";
            var w = Program.StraightLift();                  // FG: stations 1, 2; nodes 200-205; sections 101-105
            AddLift(w, 3, 4, 300, 111, 1000f);               // DQ: stations 3, 4; nodes 300-305; sections 111-115
            Stamp(w, Types(111, DQ, 112, DQ, 113, DQ, 114, DQ, 115, DQ), Types(3, DQ, 4, DQ));
            w.Build();
            var rows = w.Census.Rows;
            Eq(2, rows.Count, W, "rows");
            if (rows.Count != 2) return;
            Eq(LiftCensus.State.Complete, rows[0].State, W, "FG row complete");
            Eq(LiftCensus.State.Complete, rows[1].State, W, "DQ row complete");
            Eq(Program.R(1), rows[0].Base, W, "row 0 = the FG lift (base 1)");
            Eq(FG, rows[0].Type, W, "row 0 type");
            Eq(DQ, rows[1].Type, W, "row 1 type");
            Check(!rows[0].Mixed && !rows[1].Mixed, W, "neither row is mixed");
            Eq(0, w.Census.Notes.Count, W, "no build note: " + Notes(w));
        }

        private static void Lt2StrayEdgeMixes()
        {
            const string W = "lifttype-2 stray DQ edge";
            var w = Program.StraightLift();
            w.Node(210, new float3(40f, 20f, 240f));
            w.Edge(106, 202, 210);                           // a DQ spur off FG tower 202
            Stamp(w, Types(106, DQ), null);
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "one row (the spur joins the FG component through a tower node)");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Unfinished, rows[0].State, W, "mixed → Unfinished");
            Check(rows[0].Mixed, W, "Mixed");
            Eq(FG, rows[0].Type, W, "type = the base station's");
            Eq(Program.R(1), rows[0].Base, W, "the pair's key is kept (base 1)");
            Eq(6, rows[0].Sections, W, "sections incl. the spur");
            Check(NoteContains(w, "mixes lift types (fixedGrip+detachable)"), W, "note: " + Notes(w));
            int rev = w.Census.Revision;

            w.RemoveEdge(106);   // the node 210 stays in the World but no edge references it, so it is not harvested
            w.Build();
            rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows after the spur is removed");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Complete, rows[0].State, W, "back to Complete");
            Check(!rows[0].Mixed, W, "no longer mixed");
            Check(w.Census.Revision != rev, W, "Revision moved");
            Check(!NoteContains(w, "mixes lift types"), W, "no mixed note any more: " + Notes(w));
            Eq(1, w.CountEvents(LiftCensus.EventKind.Closed), W, "a Closed event (Unfinished → Complete)");
        }

        private static void Lt3LineBetweenOtherStations()
        {
            const string W = "lifttype-3 DQ line between FG stations";
            var w = Program.StraightLift();
            Stamp(w, Types(101, DQ, 102, DQ, 103, DQ, 104, DQ, 105, DQ), null);
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Unfinished, rows[0].State, W, "Unfinished (D18: the stations count)");
            Check(rows[0].Mixed, W, "Mixed");
            Eq(FG, rows[0].Type, W, "type = the base station's (fixed-grip)");
            Check(NoteContains(w, "mixes lift types (fixedGrip+detachable)"), W, "note: " + Notes(w));

            // The same line with DQ stations is a complete detachable lift.
            var w2 = Program.StraightLift();
            Stamp(w2, Types(101, DQ, 102, DQ, 103, DQ, 104, DQ, 105, DQ), Types(1, DQ, 2, DQ));
            w2.Build();
            Check(w2.Census.Rows.Count == 1 && w2.Census.Rows[0].State == LiftCensus.State.Complete &&
                  w2.Census.Rows[0].Type == DQ && !w2.Census.Rows[0].Mixed, W, "a DQ line between DQ stations: Complete, Type 1");
        }

        private static void Lt4UntypedFeeder()
        {
            const string W = "lifttype-4 untyped feeder";
            var w = Program.StraightLift();
            AddLift(w, 3, 4, 300, 111, 1000f);               // a second FG lift, apart
            w.Node(220, new float3(500f, 30f, 300f));
            w.Edge(150, 202, 220);                           // untyped: FG tower 202 → a pole
            w.Edge(151, 220, 302);                           // untyped: the pole → FG tower 302 of the second lift
            Stamp(w, Types(150, LiftCensus.NoType, 151, LiftCensus.NoType), null);
            w.Build();
            var rows = w.Census.Rows;
            Eq(2, rows.Count, W, "two rows (an untyped edge never unions)");
            if (rows.Count != 2) return;
            Eq(LiftCensus.State.Complete, rows[0].State, W, "lift 1 Complete");
            Eq(LiftCensus.State.Complete, rows[1].State, W, "lift 2 Complete");
            Eq(5, rows[0].Sections, W, "lift 1 sections unchanged");
            Eq(5, rows[1].Sections, W, "lift 2 sections unchanged");
            Check(!rows[0].Mixed && !rows[1].Mixed, W, "not mixed");
            Eq(0, rows[0].TowersRemoved, W, "the feeder's pole node is not a lift node");
            Check(NoteContains(w, "has no lift type — ignored"), W, "note: " + Notes(w));
            Check(!w.Census.TryGetRowOfEdge(Program.R(150), out int _), W, "the untyped edge belongs to no row");
        }

        private static void Lt5SharedBase()
        {
            const string W = "lifttype-5 shared FG base";
            var w = new Program.World();
            w.Station(1, true, new float3(0f, 0f, 0f), 180f);
            w.Station(2, false, new float3(0f, 100f, 400f), 0f);
            w.Station(3, false, new float3(300f, 120f, 300f), -135f);
            w.Node(200, w.Tip(1), station: 1);                // the shared connection node
            w.Node(201, new float3(0f, 10f, 200f));
            w.Node(202, w.Tip(2), station: 2);
            w.Node(211, new float3(150f, 10f, 150f));
            w.Node(212, w.Tip(3), station: 3);
            w.Edge(101, 200, 201); w.Edge(102, 201, 202);   // FG lift
            w.Edge(111, 200, 211); w.Edge(112, 211, 212);   // DQ line to a DQ top
            Stamp(w, Types(111, DQ, 112, DQ), Types(3, DQ));
            w.Build();
            var rows = w.Census.Rows;
            Eq(2, rows.Count, W, "two rows");
            if (rows.Count != 2) return;
            Eq(Program.R(2), rows[0].Top, W, "row 0 = the FG lift (top 2)");
            Eq(LiftCensus.State.Complete, rows[0].State, W, "FG row Complete");
            Check(!rows[0].Mixed, W, "FG row not mixed");
            Eq(FG, rows[0].Type, W, "FG row type");
            Eq(Program.R(3), rows[1].Top, W, "row 1 = the DQ line (top 3)");
            Eq(LiftCensus.State.Unfinished, rows[1].State, W, "DQ component Unfinished (FG base + DQ top)");
            Check(rows[1].Mixed, W, "DQ component Mixed");
            Eq(FG, rows[1].Type, W, "DQ component's row type = its live base station's (fixed-grip)");
        }

        private static void Lt6RowType()
        {
            const string W = "lifttype-6 Row.Type";
            // StationsOnly rows carry their station's type.
            var w = new Program.World();
            w.Station(7, true, new float3(0f, 0f, 0f), 0f);
            w.Station(8, false, new float3(500f, 50f, 0f), 0f);
            Stamp(w, null, Types(7, DQ, 8, GD));
            w.Build();
            var rows = w.Census.Rows;
            Eq(2, rows.Count, W, "two StationsOnly rows");
            for (int i = 0; i < rows.Count; i++)
            {
                Eq(LiftCensus.State.StationsOnly, rows[i].State, W, "row " + i + " StationsOnly");
                Eq(rows[i].Base.Index == 7 ? DQ : GD, rows[i].Type, W, "row " + i + " type = its station's");
            }

            // A line with no station: the lowest-index edge's type (and Mixed when its edges differ).
            var w2 = new Program.World();
            w2.Node(201, new float3(0f, 0f, 0f));
            w2.Node(202, new float3(0f, 0f, 100f));
            w2.Node(203, new float3(0f, 0f, 200f));
            w2.Edge(107, 202, 203);
            w2.Edge(105, 201, 202);
            Stamp(w2, Types(105, GD, 107, GD), null);
            w2.Build();
            Check(w2.Census.Rows.Count == 1 && w2.Census.Rows[0].Type == GD && !w2.Census.Rows[0].Mixed, W,
                  "a gondola line with no station: Type 2, not mixed");
            Stamp(w2, Types(105, TR, 107, DQ), null);
            w2.Build();
            Check(w2.Census.Rows.Count == 1 && w2.Census.Rows[0].Type == TR && w2.Census.Rows[0].Mixed &&
                  w2.Census.Rows[0].State == LiftCensus.State.Unfinished, W,
                  "edges tram (105) + detachable (107), no station: Type = the lowest edge's (3), Mixed, Unfinished");
            Check(NoteContains(w2, "mixes lift types (detachable+tram)"), W, "note in index order: " + Notes(w2));

            // A Mixed row with stations: the base station's type (LT-3 checks FG; here a DQ base with FG edges).
            var w3 = Program.StraightLift();
            Stamp(w3, null, Types(1, DQ, 2, DQ));
            w3.Build();
            Check(w3.Census.Rows.Count == 1 && w3.Census.Rows[0].Mixed && w3.Census.Rows[0].Type == DQ, W,
                  "FG edges between DQ stations: Mixed, Type = the base's (1)");
        }

        // ================================================================== LT-7, LT-8: the served rule per type

        private static List<LiftServedRule.Chord> Chords()
        {
            return new List<LiftServedRule.Chord>
            {
                new LiftServedRule.Chord { A = new float2(0f, 0f), B = new float2(0f, 500f) },
                new LiftServedRule.Chord { A = new float2(0f, 500f), B = new float2(0f, 1000f) },
            };
        }

        private static LiftServedRule.Lift Lift(ServeRuleKind rule, float radius, float margin)
        {
            return new LiftServedRule.Lift
            {
                Id = Program.R(101), Base = Program.R(101), Top = Program.R(102), Complete = true, Power = LiftPower.Powered,
                TopY = 300f, FirstChord = 0, ChordCount = 2, Rule = (byte)rule, RadiusM = radius, TopMarginM = margin,
                TopXZ = new float2(0f, 1000f),
            };
        }

        private static void Reach(LiftServedRule.Lift lift, float3 top, bool expect, float d, string where)
        {
            bool got = LiftServedRule.Reaches(top, lift, Chords(), out float dist);
            Check(got == expect, where, "Reaches " + top + ": expected " + expect + ", got " + got);
            if (expect)
                Near(d, dist, 1e-3f, where, "distance");
            else
                Check(float.IsPositiveInfinity(dist), where, "distance must be +inf when not reached, got " + dist.ToString("R"));
        }

        private static void Lt7ServedPerType()
        {
            const string W = "lifttype-7 served per type";
            // Struct default (every Phase A constructor): AlongLine at the fixed-grip 150 / 15.
            var def = new LiftServedRule.Lift();
            Eq((byte)ServeRuleKind.AlongLine, def.Rule, W, "default rule");
            Eq(150f, def.EffectiveRadiusM, W, "default radius = FixedGrip.ServeRadiusM");
            Eq(15f, def.EffectiveTopMarginM, W, "default margin = FixedGrip.TopMarginM");

            // AlongLine from the catalog rows (fixed-grip, detachable, gondola: 150 / 15) — the E1 oracles.
            for (int t = 0; t < 3; t++)
            {
                LiftTypeDef row = SkiCatalog.LiftByIndex(t);
                if (!Check(row != null && row.ServeRule == ServeRuleKind.AlongLine, W, "row " + t + " is AlongLine"))
                    continue;
                LiftServedRule.Lift l = Lift(row.ServeRule, row.ServeRadiusM, row.TopMarginM);
                Reach(l, new float3(100f, 250f, 500f), true, 100f, W + " " + row.Id + " 100 m");
                Reach(l, new float3(150f, 315f, 500f), true, 150f, W + " " + row.Id + " 150 m at the margin (both inclusive)");
                Reach(l, new float3(150.01f, 250f, 500f), false, 0f, W + " " + row.Id + " 150.01 m");
                Reach(l, new float3(100f, 315.01f, 500f), false, 0f, W + " " + row.Id + " above top + 15 m");
            }

            // TerminalsOnly 300 / 15 (the tram row): around the top station's stub tip only.
            LiftTypeDef tram = SkiCatalog.LiftByIndex(3);
            Check(tram != null && tram.ServeRule == ServeRuleKind.TerminalsOnly && tram.ServeRadiusM == 300f && tram.TopMarginM == 15f,
                  W, "the tram row is TerminalsOnly 300 / 15");
            LiftServedRule.Lift tl = Lift(ServeRuleKind.TerminalsOnly, 300f, 15f);
            Reach(tl, new float3(299f, 250f, 1000f), true, 299f, W + " tram top 299 m");
            Reach(tl, new float3(301f, 250f, 1000f), false, 0f, W + " tram top 301 m");
            Reach(tl, new float3(0f, 250f, 1300f), true, 300f, W + " tram top 300 m (inclusive)");
            Reach(tl, new float3(50f, 250f, 600f), false, 0f, W + " tram: 50 m from the chord, 403 m from the top");
            Reach(tl, new float3(0f, 315f, 1010f), true, 10f, W + " tram: at TopY + 15");
            Reach(tl, new float3(0f, 315.01f, 1010f), false, 0f, W + " tram: above TopY + 15");
            LiftServedRule.Lift tn = tl;
            tn.TopXZ = new float2(float.NaN, float.NaN);
            Reach(tn, new float3(0f, 250f, 1000f), false, 0f, W + " tram: top not in the build (NaN TopXZ)");
            tn = tl;
            tn.Power = LiftPower.NoPower;
            Reach(tn, new float3(0f, 250f, 1000f), false, 0f, W + " tram: no power");

            // ServingLift across types: a run near the tram's span but far from its top goes to the fixed-grip lift.
            var lifts = new List<LiftServedRule.Lift> { tl, Lift(ServeRuleKind.AlongLine, 150f, 15f) };
            var chords = Chords();
            LiftServedRule.Lift fg = lifts[1];
            fg.Id = Program.R(55);
            lifts[1] = fg;
            int s = LiftServedRule.ServingLift(new float3(50f, 250f, 600f), lifts, chords, out float sd);
            Eq(1, s, W, "ServingLift: only the AlongLine lift reaches mid-span");
            Near(50f, sd, 1e-3f, W, "its distance");
        }

        private static void Lt8SummitServed()
        {
            const string W = "lifttype-8 SummitServed";
            float radius = SkiCatalog.Ladder.SummitServedRadiusM;
            Eq(300f, radius, W, "Ladder.SummitServedRadiusM");
            var lifts = new List<LiftServedRule.Lift> { Lift(ServeRuleKind.AlongLine, 150f, 15f) };
            float2 top = lifts[0].TopXZ;
            Check(LiftServedRule.SummitServed(top + new float2(299.9f, 0f), lifts, radius), W, "299.9 m served");
            Check(LiftServedRule.SummitServed(top + new float2(300f, 0f), lifts, radius), W, "300.0 m served (inclusive)");
            Check(!LiftServedRule.SummitServed(top + new float2(300.1f, 0f), lifts, radius), W, "300.1 m not served");
            Check(LiftServedRule.SummitServed(top + new float2(0f, -250f), lifts, radius), W, "below the top along the line: served");

            LiftServedRule.Lift l = lifts[0];
            l.Power = LiftPower.NoPower;
            Check(!LiftServedRule.SummitServed(top, new List<LiftServedRule.Lift> { l }, radius), W, "unpowered → false");
            l = lifts[0];
            l.Complete = false;
            Check(!LiftServedRule.SummitServed(top, new List<LiftServedRule.Lift> { l }, radius), W, "Open (incomplete) → false");
            l = lifts[0];
            l.TopXZ = new float2(float.NaN, float.NaN);
            Check(!LiftServedRule.SummitServed(top, new List<LiftServedRule.Lift> { l }, radius), W, "top not in the build → false");
            Check(!LiftServedRule.SummitServed(top, new List<LiftServedRule.Lift>(), radius), W, "no lift → false");
            Check(!LiftServedRule.SummitServed(top, null, radius), W, "null list → false");
            Check(!LiftServedRule.SummitServed(new float2(float.NaN, 0f), lifts, radius), W, "NaN lodge position → false");
            l = lifts[0];
            l.Power = LiftPower.Unknown;
            Check(LiftServedRule.SummitServed(top, new List<LiftServedRule.Lift> { l }, radius), W, "power unknown counts as powered");
        }

        // ================================================================== LT-9: per-row stub tip

        private static void Lt9StubTip()
        {
            const string W = "lifttype-9 StubTip per row";
            float3 pos = new float3(10f, 5f, 20f);
            quaternion rot = quaternion.RotateY(math.radians(30f));   // (0, 0, 1) → (sin 30°, 0, cos 30°)
            float3 dq = StationAlign.StubTip(pos, rot, -16f);
            float3 fg = StationAlign.StubTip(pos, rot);
            Near(10f - 8f, dq.x, 1e-4f, W, "−16: x = 10 − 16 sin 30°");
            Near(5f, dq.y, 1e-5f, W, "−16: y");
            Near(20f - 13.856406f, dq.z, 1e-4f, W, "−16: z = 20 − 16 cos 30°");
            Near(10f - 6f, fg.x, 1e-4f, W, "2-arg (−12): x = 10 − 12 sin 30°");
            Near(20f - 10.392305f, fg.z, 1e-4f, W, "2-arg (−12): z = 20 − 12 cos 30°");
            float3 fg3 = StationAlign.StubTip(pos, rot, SkiCatalog.StubTipLocalZ);
            Check(math.all(fg3 == fg), W, "StubTip(pos, rot) == StubTip(pos, rot, StubTipLocalZ)");
            Near(4f, math.distance(dq, fg), 1e-4f, W, "the −16 tip lies 4 m further along the axis");
            float3 nan = StationAlign.StubTip(pos, rot, float.NaN);
            Check(math.all(nan == fg), W, "a NaN stub z falls back to the fixed-grip −12");
            StationDef dqBase = SkiCatalog.StationOf(1, StationRole.Base);
            Check(dqBase != null && dqBase.StubTipLocalZ == -16f, W, "the detachable base row's stub tip is −16");
            // Nitpick B2 (2026-10-13, build/nitpick-b2-tram-ramps.md): the 4-argument tip on the row's line axis x. The
            // 3-argument call is the x 0 case bit for bit; the tram base's tip (−4, 0, −12) under the 30° yaw is
            // pos + (−4 cos 30° − 12 sin 30°, 0, 4 sin 30° − 12 cos 30°); a NaN x counts as 0.
            Check(math.all(StationAlign.StubTip(pos, rot, -16f, 0f) == dq), W, "StubTip(pos, rot, z, 0) == StubTip(pos, rot, z)");
            float3 tb = StationAlign.StubTip(pos, rot, -12f, -4f);
            Near(10f - 3.4641016f - 6f, tb.x, 1e-4f, W, "x −4, z −12: x = 10 − 4 cos 30° − 12 sin 30°");
            Near(5f, tb.y, 1e-5f, W, "x −4, z −12: y");
            Near(20f + 2f - 10.392305f, tb.z, 1e-4f, W, "x −4, z −12: z = 20 + 4 sin 30° − 12 cos 30°");
            Near(4f, math.distance(tb, fg), 1e-4f, W, "the tram base's tip lies 4 m off the origin axis, abeam");
            Check(math.all(StationAlign.StubTip(pos, rot, -12f, float.NaN) == fg), W, "a NaN axis x counts as 0");
            StationDef tramBase = SkiCatalog.StationOf(3, StationRole.Base);
            Check(tramBase != null && tramBase.StubTipLocalX == -4f && tramBase.StubTipLocalZ == -12f, W,
                  "the tram base row's line axis is x −4.00 (its 6 x 3 lot), its stub tip z −12");
        }

        // ================================================================== LT-12: the tram (Catalog Phase C)

        /// <summary>LiftCensusSystem.CopyServeLifts (LiftCensusSystem.cs:347-375; Game side, not linked here) for one row:
        /// the row type's rule / radius / margin unless Mixed (then AlongLine radius 0: it is Unfinished, so Serves is
        /// false), TopY / TopXZ = the top station's stub tip (TopYOf / TopXZOf, :463-482), the row's chord slice.</summary>
        private static LiftServedRule.Lift ServeLiftOf(LiftCensus.Row row, float3 topStubTip, int firstChord, int chordCount,
                                                       LiftPower power)
        {
            LiftTypeDef def = row.Mixed ? null : SkiCatalog.LiftByIndex(row.Type);
            return new LiftServedRule.Lift
            {
                Id = row.Id, Base = row.Base, Top = row.Top, Complete = row.State == LiftCensus.State.Complete, Power = power,
                TopY = topStubTip.y, FirstChord = firstChord, ChordCount = chordCount,
                Rule = def != null ? (byte)def.ServeRule : (byte)ServeRuleKind.AlongLine,
                RadiusM = def != null ? def.ServeRadiusM : 0f,
                TopMarginM = def != null ? def.TopMarginM : 0f,
                TopXZ = topStubTip.xz,
            };
        }

        private static void Lt12Tram()
        {
            const string W = "lifttype-12 tram";
            LiftTypeDef tram = SkiCatalog.LiftByIndex(TR);
            StationDef tramBase = SkiCatalog.StationOf(TR, StationRole.Base);
            StationDef tramTop = SkiCatalog.StationOf(TR, StationRole.Top);
            if (!Check(tram != null && tram.Id == "tram" && tram.Mover == MoverKind.Shuttle && tramBase != null && tramTop != null,
                       W, "SkiCatalog row 3 is the tram (Shuttle) with a base and a top station row"))
                return;
            Check(tram.ServeRule == ServeRuleKind.TerminalsOnly && tram.ServeRadiusM == 300f && tram.TopMarginM == 15f, W,
                  "the tram row is TerminalsOnly 300 / 15");
            // The World stamps the fixed-grip −12 stub tip; the census system uses each station row's own (SkiCatalog.cs
            // tram rows −12, LiftCensusSystem.cs:1218): equal, so the World's tips are the tram's.
            Eq(-12f, tramBase.StubTipLocalZ, W, "tram base StubTipLocalZ");
            Eq(-12f, tramTop.StubTipLocalZ, W, "tram top StubTipLocalZ");

            // A 1 km tram: base at the origin facing +z (yaw 180), top 400 m up facing the base; two towers; three spans.
            var w = new Program.World();
            float3 basePos = new float3(0f, 0f, 0f), topPos = new float3(0f, 400f, 1000f);
            w.Station(1, true, basePos, 180f);
            w.Station(2, false, topPos, 0f);
            float3 a = w.Tip(1), c = w.Tip(2);
            float3 t1 = math.lerp(a, c, 1f / 3f) + new float3(0f, 30f, 0f), t2 = math.lerp(a, c, 2f / 3f) + new float3(0f, 30f, 0f);
            w.Node(200, a, station: 1);
            w.Node(201, t1);
            w.Node(202, t2);
            w.Node(203, c, station: 2);
            w.Edge(101, 200, 201, chairs: 0);   // a tram edge carries no carrier sub-objects (the cabins are hosted, R29)
            w.Edge(102, 201, 202, chairs: 0);
            w.Edge(103, 202, 203, chairs: 0);
            Stamp(w, Types(101, TR, 102, TR, 103, TR), Types(1, TR, 2, TR));
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows");
            if (rows.Count != 1) return;
            LiftCensus.Row row = rows[0];
            Eq(LiftCensus.State.Complete, row.State, W, "Complete");
            Eq(TR, row.Type, W, "Type 3 (tram)");
            Check(!row.Mixed, W, "not mixed");
            Eq(0, row.Chairs, W, "Chairs 0 (Stats overrides a Shuttle row with the cabins its base hosts, round-3 §6.2)");
            Eq(3, row.Sections, W, "sections");
            Eq(2, row.Towers, W, "towers");
            Eq(Program.R(1), row.Base, W, "base = station 1 (IsBase by prefab)");
            Eq(Program.R(2), row.Top, W, "top = station 2");
            Eq(0, w.Census.Notes.Count, W, "no build note: " + Notes(w));

            // The served Lift, as CopyServeLifts builds it.
            float3 topTip = StationAlign.StubTip(topPos, quaternion.RotateY(math.radians(0f)), tramTop.StubTipLocalZ);
            Check(math.all(topTip == c), W, "TopXZ source = the World's top tip (0, 400, 988): " + topTip);
            var chords = new List<LiftServedRule.Chord>
            {
                new LiftServedRule.Chord { A = a.xz, B = t1.xz },
                new LiftServedRule.Chord { A = t1.xz, B = t2.xz },
                new LiftServedRule.Chord { A = t2.xz, B = c.xz },
            };
            LiftServedRule.Lift tl = ServeLiftOf(row, topTip, 0, 3, LiftPower.Powered);
            Eq((byte)ServeRuleKind.TerminalsOnly, tl.Rule, W, "served Lift rule");
            Eq(300f, tl.EffectiveRadiusM, W, "served Lift radius");
            Eq(15f, tl.EffectiveTopMarginM, W, "served Lift top margin");
            Eq(400f, tl.TopY, W, "served Lift TopY (the top stub tip y)");
            Check(tl.Serves, W, "Complete, powered, with chords: serves");
            float dist;
            Check(LiftServedRule.Reaches(new float3(299f, 380f, 988f), tl, chords, out dist) && math.abs(dist - 299f) <= 1e-3f, W,
                  "a run top 299 m from TopXZ: served at 299 m (got " + dist.ToString("R") + ")");
            Check(LiftServedRule.Reaches(new float3(0f, 380f, 688f), tl, chords, out dist) && math.abs(dist - 300f) <= 1e-3f, W,
                  "a run top 300 m from TopXZ: served (inclusive; got " + dist.ToString("R") + ")");
            Check(!LiftServedRule.Reaches(new float3(301f, 380f, 988f), tl, chords, out dist) && float.IsPositiveInfinity(dist), W,
                  "a run top 301 m from TopXZ: not served");
            Check(!LiftServedRule.Reaches(new float3(20f, 200f, 488f), tl, chords, out dist), W,
                  "a run top 20 m from the mid-span chord, 500 m from the top: not served by the tram");
            LiftServedRule.Lift along = tl;
            along.Rule = (byte)ServeRuleKind.AlongLine;
            along.RadiusM = 150f;
            Check(LiftServedRule.Reaches(new float3(20f, 200f, 488f), along, chords, out dist) && math.abs(dist - 20f) <= 1e-3f, W,
                  "contrast: an AlongLine lift on the same chords serves that run at 20 m (got " + dist.ToString("R") + ")");
            Check(LiftServedRule.Reaches(new float3(299f, 415f, 988f), tl, chords, out dist), W,
                  "299 m away at TopY + 15: served (margin inclusive)");
            Check(!LiftServedRule.Reaches(new float3(299f, 416f, 988f), tl, chords, out dist), W,
                  "299 m away but 16 m above TopY: not served");
            LiftServedRule.Lift off = ServeLiftOf(row, topTip, 0, 3, LiftPower.NoPower);
            Check(!LiftServedRule.Reaches(new float3(0f, 380f, 988f), off, chords, out dist), W, "unpowered: serves nothing");
            int served = LiftServedRule.ServingLift(new float3(100f, 390f, 950f), new List<LiftServedRule.Lift> { tl }, chords,
                                                    out float sd);
            Check(served == 0 && math.abs(sd - math.sqrt(100f * 100f + 38f * 38f)) <= 1e-3f, W,
                  "ServingLift picks the tram for a run near its top (" + served + ", " + sd.ToString("R") + " m)");

            // Tram edges between fixed-grip stations: Mixed, Unfinished, served by nothing.
            var wm = new Program.World();
            wm.Station(1, true, basePos, 180f);
            wm.Station(2, false, topPos, 0f);
            wm.Node(200, wm.Tip(1), station: 1);
            wm.Node(201, t1);
            wm.Node(202, t2);
            wm.Node(203, wm.Tip(2), station: 2);
            wm.Edge(101, 200, 201, chairs: 0);
            wm.Edge(102, 201, 202, chairs: 0);
            wm.Edge(103, 202, 203, chairs: 0);
            Stamp(wm, Types(101, TR, 102, TR, 103, TR), null);   // stations stay fixed-grip (type 0)
            wm.Build();
            var mrows = wm.Census.Rows;
            Eq(1, mrows.Count, W, "mixed: rows");
            if (mrows.Count != 1) return;
            Eq(LiftCensus.State.Unfinished, mrows[0].State, W, "mixed: Unfinished");
            Check(mrows[0].Mixed, W, "mixed: Mixed");
            Eq(FG, mrows[0].Type, W, "mixed: type = the base station's (fixed-grip)");
            Check(NoteContains(wm, "mixes lift types (fixedGrip+tram)"), W, "mixed: note: " + Notes(wm));
            LiftServedRule.Lift ml = ServeLiftOf(mrows[0], topTip, 0, 3, LiftPower.Powered);
            Check(!ml.Serves && !LiftServedRule.Reaches(new float3(0f, 380f, 988f), ml, chords, out dist), W,
                  "mixed: not Complete, serves nothing (not even at the top tip)");
        }

        // ================================================================== LT-11: the up lane (orchestrator addendum 1)

        private static void Lt11UpLane()
        {
            const string W = "lifttype-11 up lane";
            float2 b = new float2(0f, 0f), t = new float2(0f, 600f);   // uphill = +z
            float2 right = new float2(2.6f, 300f), left = new float2(-2.6f, 300f);

            Eq(1, StationAlign.SideLookingUphill(b, t, right), W, "+x is right looking along +z (left-handed world)");
            Eq(-1, StationAlign.SideLookingUphill(b, t, left), W, "−x is left looking along +z");
            Eq(0, StationAlign.SideLookingUphill(b, t, new float2(0f, 300f)), W, "on the axis");
            Eq(0, StationAlign.SideLookingUphill(b, b, right), W, "degenerate axis");

            // The mirrored two-chain graph: whichever chain is found first, chain A ends up the right one.
            Check(StationAlign.UpLaneIsSecond(b, t, left, right), W, "first = left, second = right → swap");
            Check(!StationAlign.UpLaneIsSecond(b, t, right, left), W, "first = right → keep");
            // The mirrored lift (uphill −z): the right looking uphill is −x now.
            float2 b2 = new float2(0f, 600f), t2 = new float2(0f, 0f);
            Check(StationAlign.UpLaneIsSecond(b2, t2, right, left), W, "uphill −z: first = +x (left), second = −x (right) → swap");
            Check(!StationAlign.UpLaneIsSecond(b2, t2, left, right), W, "uphill −z: first = −x (right) → keep");
            // A 45° lift: base (0, 0) → top (424.26, 424.26); right-hand normal (0.7071, −0.7071).
            float2 b3 = new float2(0f, 0f), t3 = new float2(424.26f, 424.26f);
            float2 mid = new float2(212.13f, 212.13f), n = new float2(0.70710678f, -0.70710678f) * 2.6f;
            Check(StationAlign.UpLaneIsSecond(b3, t3, mid - n, mid + n), W, "45°: second on the right → swap");
            Check(!StationAlign.UpLaneIsSecond(b3, t3, mid + n, mid - n), W, "45°: first on the right → keep");
            // Undecided (both on the axis, or a degenerate axis): keep the order found, both ways.
            Check(!StationAlign.UpLaneIsSecond(b, t, new float2(0f, 100f), new float2(0f, 500f)) &&
                  !StationAlign.UpLaneIsSecond(b, t, new float2(0f, 500f), new float2(0f, 100f)), W, "both on the axis: undecided");
            Check(!StationAlign.UpLaneIsSecond(b, b, left, right), W, "degenerate axis: keep");

            // The reason for the rule: a carrier faces its travel direction with its hanger at local +x. Up on the right
            // lane facing uphill, down on the left lane facing downhill — the hanger lies outboard of its lane on both.
            float3 up3 = new float3(0f, 0f, 1f), y = new float3(0f, 1f, 0f);
            float3 hangerUp = math.mul(quaternion.LookRotationSafe(up3, y), new float3(1f, 0f, 0f));
            float3 hangerDown = math.mul(quaternion.LookRotationSafe(-up3, y), new float3(1f, 0f, 0f));
            Eq(1, StationAlign.SideLookingUphill(b, t, right + hangerUp.xz * 1.5f), W, "up chain (right lane): hanger to the right");
            Check(math.abs((right + hangerUp.xz * 1.5f).x) > math.abs(right.x), W, "up chain: the hanger is outboard");
            Eq(-1, StationAlign.SideLookingUphill(b, t, left + hangerDown.xz * 1.5f), W, "down chain (left lane): hanger to the left");
            Check(math.abs((left + hangerDown.xz * 1.5f).x) > math.abs(left.x), W, "down chain: the hanger is outboard");
        }
    }
}
