// Plan bends at lift towers (docs: lift-alignment-2026-11-02 C6 / F6, owner verdict 2026-11-02: warn, never refuse): the census row's
// sharpest tower bend (LiftCensus.MeasureBends: BendDeg / BendTower / BendNode / BendAt), the hub's number for it
// (LiftCensus.BendWarnTenths) and the lift tool's joint check (LiftLineInput.JoinBendWarnDeg). Where-tags `bend/...`, BT-1..BT-10.
// Pinned:
// - A straight line has no bend.
// - LiftCensus.MeasureBends measures the bend only at a line tower between two of the row's edges (never a station
//   connection node, never a node without a tower).
// - The tower number is the tile number (towers count from the base station. A fragment without the base counts from its dead end).
// - Edge direction does not matter.
// - A moved tower bumps Revision.
// - At or under LiftAlignMath.BendWarnDeg nothing warns. Just over it, the hub reads 5.1.

using System;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    internal static class BendTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private static bool Check(bool ok, string where, string msg) => s_Check(ok, where, msg);

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Bt1Straight();
            Bt2BentTower();
            Bt3Threshold();
            Bt4WarnTenths();
            Bt5NoTowerSkipped();
            Bt6EdgeDirection();
            Bt7OpenFragment();
            Bt8StationNodeNeverMeasured();
            Bt9Revision();
            Bt10ToolJoint();
            Bt11TrainLimit();
            Bt12LowSpan();
        }

        private static LiftCensus.Row Only(Program.World w, string where)
        {
            w.Build();
            Check(w.Census.Rows.Count == 1, where, "one row, got " + w.Census.Rows.Count);
            return w.Census.Rows.Count > 0 ? w.Census.Rows[0] : default;
        }

        /// <summary>The reference lift (Program.StraightLift) with tower <paramref name="node"/> moved <paramref name="dx"/> m sideways.</summary>
        private static Program.World Bent(int node, float dx)
        {
            Program.World w = Program.StraightLift();
            float3 a = w.Tip(1), b = w.Tip(2);
            int k = node - 200;
            w.Node(node, math.lerp(a, b, k / 5f) + new float3(dx, 8f, 0f));
            return w;
        }

        private static float3 TowerPos(Program.World w, int node, float dx)
        {
            float3 a = w.Tip(1), b = w.Tip(2);
            return math.lerp(a, b, (node - 200) / 5f) + new float3(dx, 8f, 0f);
        }

        private static void Bt1Straight()
        {
            const string W = "bend/BT-1 straight";
            LiftCensus.Row r = Only(Program.StraightLift(), W);
            Check(r.State == LiftCensus.State.Complete, W, "complete");
            Check(r.BendDeg < 1e-3f, W, "no bend on a straight line, got " + r.BendDeg);
            Check(LiftCensus.BendWarnTenths(r.BendDeg) == 0, W, "no warning");
        }

        private static void Bt2BentTower()
        {
            const string W = "bend/BT-2 bent tower 3";
            Program.World w = Bent(203, 10f);
            LiftCensus.Row r = Only(w, W);
            float want = LiftAlignMath.PlanBendDeg(TowerPos(w, 202, 0f), TowerPos(w, 203, 10f), TowerPos(w, 204, 0f));
            Check(math.abs(r.BendDeg - want) < 1e-3f, W, $"BendDeg {r.BendDeg:F3}, want {want:F3}");
            Check(want > 9.5f && want < 10.5f, W, $"the case turns about 10 deg ({want:F2})");
            Check(r.BendTower == 3, W, "tower 3 from the base, got " + r.BendTower);
            Check(r.BendNode == Program.R(203), W, "node 203, got " + r.BendNode);
            Check(math.distance(r.BendAt, TowerPos(w, 203, 10f)) < 1e-3f, W, "BendAt = the tower node");
            Check(r.State == LiftCensus.State.Complete, W, "a bend never changes the state");
            Check(LiftCensus.BendWarnTenths(r.BendDeg) == (int)math.ceil(want * 10f - 1e-3f), W, "hub tenths rounded up");
        }

        private static void Bt3Threshold()
        {
            const string W = "bend/BT-3 threshold";
            // Tower 3 moved d: its neighbors turn atan(d / 115.2) each, tower 3 twice that. 4.9 deg at tower 3 warns nowhere.
            float spacing = 576f / 5f;
            float dUnder = spacing * math.tan(math.radians(4.9f * 0.5f));
            LiftCensus.Row r = Only(Bent(203, dUnder), W);
            Check(r.BendDeg > 4.8f && r.BendDeg < 5f, W, $"4.9 deg case, got {r.BendDeg:F3}");
            Check(LiftCensus.BendWarnTenths(r.BendDeg) == 0, W, "at or under 5 deg: no warning");
            float dOver = spacing * math.tan(math.radians(5.2f * 0.5f));
            r = Only(Bent(203, dOver), W);
            Check(LiftCensus.BendWarnTenths(r.BendDeg) == 53 || LiftCensus.BendWarnTenths(r.BendDeg) == 52, W,
                $"5.2 deg reads 5.2 (rounded up), got {LiftCensus.BendWarnTenths(r.BendDeg)} for {r.BendDeg:F4}");
        }

        private static void Bt4WarnTenths()
        {
            const string W = "bend/BT-4 tenths";
            Check(LiftCensus.BendWarnTenths(0f) == 0 && LiftCensus.BendWarnTenths(5f) == 0, W, "0 and exactly 5: none");
            Check(LiftCensus.BendWarnTenths(5.0001f) == 51, W, "just over 5 reads 5.1, never 5.0");
            Check(LiftCensus.BendWarnTenths(6f) == 60, W, "6 reads 6.0");
            Check(LiftCensus.BendWarnTenths(12.34f) == 124, W, "12.34 reads 12.4");
            Check(LiftCensus.BendWarnTenths(float.NaN) == 0, W, "NaN: none");
            Check(LiftAlignMath.BendWarnDeg == 5f, W, "the owner's 5 deg");
        }

        private static void Bt5NoTowerSkipped()
        {
            const string W = "bend/BT-5 node without a tower";
            Program.World w = Bent(203, 10f);
            w.SetTower(202, false);
            LiftCensus.Row r = Only(w, W);
            Check(r.BendNode == Program.R(203), W, "the bend at 203 still counts, got " + r.BendNode);
            Check(r.BendTower == 2, W, "203 is the second tower once 202 has none, got " + r.BendTower);
            w.SetTower(203, false);
            r = Only(w, W);
            Check(r.BendNode != Program.R(203), W, "a node without a tower is never measured");
            Check(LiftCensus.BendWarnTenths(r.BendDeg) == 0, W, $"its neighbors turn under 5 deg ({r.BendDeg:F2})");
        }

        private static void Bt6EdgeDirection()
        {
            const string W = "bend/BT-6 edge direction";
            Program.World w = Bent(203, 10f);
            // Same line, every section drawn top to base.
            for (int k = 0; k < 5; k++)
                w.Edge(101 + k, 201 + k, 200 + k);
            LiftCensus.Row r = Only(w, W);
            Check(r.BendNode == Program.R(203) && r.BendTower == 3, W, $"tower 3 / node 203, got {r.BendTower} / {r.BendNode}");
        }

        private static void Bt7OpenFragment()
        {
            const string W = "bend/BT-7 open line";
            Program.World w = Bent(204, 10f);
            w.Build();           // the complete lift first, so the fragments keep its key (Open, not two new lines)
            w.RemoveEdge(103);   // 202-203 gone: the base fragment 200..202, the top fragment 203..205
            LiftCensus.Row r = Only(w, W);
            Check(r.State == LiftCensus.State.Open, W, "open, got " + r.State);
            Check(r.BendNode == Program.R(204), W, "the bend at 204, got " + r.BendNode);
            Check(r.BendTower == 1, W, "the top fragment counts from its dead end (203): 204 is its tower 1, got " + r.BendTower);
        }

        private static void Bt8StationNodeNeverMeasured()
        {
            const string W = "bend/BT-8 station node";
            // Tower 1 far to the side: the line turns hard at the base stub node 200, which is never measured. Node 201 is measured.
            Program.World w = Bent(201, 40f);
            LiftCensus.Row r = Only(w, W);
            Check(r.BendNode == Program.R(201), W, "the sharpest measured node is tower 1, got " + r.BendNode);
            float atTower1 = LiftAlignMath.PlanBendDeg(w.Tip(1), TowerPos(w, 201, 40f), TowerPos(w, 202, 0f));
            Check(math.abs(r.BendDeg - atTower1) < 1e-3f, W, $"BendDeg {r.BendDeg:F2} = tower 1's {atTower1:F2}");
        }

        private static void Bt11TrainLimit()
        {
            const string W = "bend/BT-11 train limit";
            // No tower past its train: nothing. Two past: the row names the one furthest past, numbered like the bend (tile order).
            Program.World w = Program.StraightLift();
            LiftCensus.Row r = Only(w, W);
            Check(r.TrainTower == 0 && r.TrainNode.IsNull && r.TrainOverDeg == 0f, W, "no train past its limits on a straight line");
            w.SetTrainOver(202, 3f);
            w.SetTrainOver(204, 7.5f);
            r = Only(w, W);
            Check(r.TrainTower == 4 && r.TrainNode == Program.R(204), W, $"tower 4 / node 204 (7.5 over), got {r.TrainTower} / {r.TrainNode}");
            Check(math.abs(r.TrainOverDeg - 7.5f) < 1e-6f, W, "TrainOverDeg 7.5, got " + r.TrainOverDeg);
            Check(math.distance(r.TrainAt, TowerPos(w, 204, 0f)) < 1e-3f, W, "TrainAt = the tower node");
            Check(r.State == LiftCensus.State.Complete && r.BendTower == 0 || r.BendDeg < 1e-3f, W, "a train limit changes neither state nor bend");
            // A station connection node is never a line tower, whatever its figure.
            Program.World s = Program.StraightLift();
            s.SetTrainOver(200, 9f);
            r = Only(s, W);
            Check(r.TrainTower == 0, W, "the base stub node counted, tower " + r.TrainTower);
            // The row's signature moves with the figure (the hub refreshes).
            Program.World a = Program.StraightLift();
            a.Build();
            int rev = a.Census.Revision;
            a.SetTrainOver(203, 2f);
            a.Build();
            Check(a.Census.Revision != rev, W, "a tower going past its train's limits does not bump Revision");
        }

        private static void Bt12LowSpan()
        {
            const string W = "bend/BT-12 low span";
            // Every span clears the floor: nothing. Two low spans: the row keeps the lowest, named by the towers at its ends (tile order).
            Program.World w = Program.StraightLift();
            LiftCensus.Row r = Only(w, W);
            Check(!r.LowSpan && r.LowEdge.IsNull && r.LowFrom == 0 && r.LowTo == 0, W, "no low span on a clear line");
            w.SetLow(102, 2.4f);   // towers 1-2
            w.SetLow(104, 1.2f);   // towers 3-4
            r = Only(w, W);
            Check(r.LowSpan && r.LowEdge == Program.R(104), W, $"edge 104 (1.2 m), got {r.LowEdge}");
            Check(r.LowFrom == 3 && r.LowTo == 4, W, $"between towers 3 and 4, got {r.LowFrom} and {r.LowTo}");
            Check(math.abs(r.LowClearM - 1.2f) < 1e-6f, W, "LowClearM 1.2, got " + r.LowClearM);
            Check(r.State == LiftCensus.State.Complete, W, "a low span does not change the state");
            // The station spans: base to tower 1 reads 0 / 1, tower 4 to the top reads 4 / 0.
            Program.World b = Program.StraightLift();
            b.SetLow(101, 0.5f);
            r = Only(b, W);
            Check(r.LowFrom == 0 && r.LowTo == 1, W, $"base span: 0 / 1, got {r.LowFrom} / {r.LowTo}");
            Program.World t = Program.StraightLift();
            t.SetLow(105, -2f);
            r = Only(t, W);
            Check(r.LowFrom == 4 && r.LowTo == 0, W, $"top span: 4 / 0, got {r.LowFrom} / {r.LowTo}");
            Check(LiftCensus.LowTenths(r.LowClearM) == 0, W, "below the ground reads 0 tenths");
            Check(LiftCensus.LowTenths(2.85f) == 28 && LiftCensus.LowTenths(1.2f) == 12 && LiftCensus.LowTenths(0.04f) == 0, W,
                  "tenths round down: 2.85 -> 28, 1.2 -> 12, 0.04 -> 0");
            // A missing tower between: the span still ends at the next tower with a number.
            Program.World m = Program.StraightLift();
            m.SetTower(202, false);
            m.SetLow(102, 1f);
            r = Only(m, W);
            Check(r.LowFrom == 1 && r.LowTo == 2, W, $"past a missing tower: 1 / 2 (the next numbered tower), got {r.LowFrom} / {r.LowTo}");
            // The row's signature moves with the figure (the hub refreshes).
            Program.World a = Program.StraightLift();
            a.Build();
            int rev = a.Census.Revision;
            a.SetLow(103, 2f);
            a.Build();
            Check(a.Census.Revision != rev, W, "a span going under the floor does not bump Revision");
        }

        private static void Bt9Revision()
        {
            const string W = "bend/BT-9 revision";
            Program.World w = Program.StraightLift();
            w.Build();
            int rev = w.Census.Revision;
            w.Build();
            Check(w.Census.Revision == rev, W, "same line: same revision");
            float3 a = w.Tip(1), b = w.Tip(2);
            w.Node(203, math.lerp(a, b, 0.6f) + new float3(10f, 8f, 0f));
            w.Build();
            Check(w.Census.Revision != rev, W, "a moved tower bumps the revision, so the hub rebuilds its rows");
            Check(w.Census.Rows.Count == 1 && LiftCensus.BendWarnTenths(w.Census.Rows[0].BendDeg) > 0, W, "and the new bend warns");
        }

        private static void Bt10ToolJoint()
        {
            const string W = "bend/BT-10 tool joint";
            float3 far = new float3(0f, 0f, 0f), joint = new float3(0f, 10f, 100f);
            Check(LiftLineInput.JoinBendWarnDeg(far, joint, new float3(0f, 30f, 300f)) == 0f, W, "straight on: none");
            float3 turn10 = joint + 200f * new float3(math.sin(math.radians(10f)), 0f, math.cos(math.radians(10f)));
            float got = LiftLineInput.JoinBendWarnDeg(far, joint, turn10);
            Check(math.abs(got - 10f) < 1e-2f, W, $"a 10 deg turn reads 10, got {got:F3}");
            float3 turn4 = joint + 200f * new float3(math.sin(math.radians(4f)), 0f, math.cos(math.radians(4f)));
            Check(LiftLineInput.JoinBendWarnDeg(far, joint, turn4) == 0f, W, "4 deg: no warning");
            Check(LiftLineInput.JoinBendWarnDeg(far, joint, joint) == 0f, W, "a zero-length leg: none");
        }
    }
}
