// Tests for PowerJoinRule (the vanilla HV join refusal) and PowerJoinEpisode (one log line per episode). Where-tags `power-join/...`,
// PJ-1..PJ-4. Pinned:
// - The rule refuses only a new vanilla HV edge touching a lift.
// - The rule refuses a replace of a lift section.
// - The rule never refuses LV, our lift lines and marker, our station's sub-nets, deletes and untouched copies.
// - The node touch order is node > split > middle.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;

namespace LiftBatch
{
    public static class PowerJoinTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Pj1Classify();
            Pj2Refuses();
            Pj3NeverRefused();
            Pj4Episode();
            s_Check = null;
        }

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            s_Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }

        private static PowerJoinFacts NewHv(PowerJoinTouch start, PowerJoinTouch end)
        {
            return new PowerJoinFacts { HighVoltage = true, Creates = true, Start = start, End = end };
        }

        private static void Pj1Classify()
        {
            const string w = "power-join/PJ-1";
            for (int m = 0; m < 8; m++)
            {
                bool node = (m & 1) != 0, split = (m & 2) != 0, mid = (m & 4) != 0;
                PowerJoinTouch expected = node ? PowerJoinTouch.LiftNode : split ? PowerJoinTouch.LiftSplit
                    : mid ? PowerJoinTouch.LiftMidEdge : PowerJoinTouch.None;
                Eq(expected, PowerJoinRule.ClassifyNode(node, split, mid), w, $"node {node} split {split} mid {mid}");
            }
        }

        private static void Pj2Refuses()
        {
            const string w = "power-join/PJ-2";
            var touches = (PowerJoinTouch[])Enum.GetValues(typeof(PowerJoinTouch));
            foreach (PowerJoinTouch a in touches)
                foreach (PowerJoinTouch b in touches)
                {
                    PowerJoinVerdict v = PowerJoinRule.Decide(NewHv(a, b));
                    bool touch = a != PowerJoinTouch.None || b != PowerJoinTouch.None;
                    Eq(touch ? PowerJoinVerdict.RefuseJoin : PowerJoinVerdict.Allow, v, w, $"new HV edge {a} / {b}");
                    Eq(touch, PowerJoinRule.Refuses(v), w, $"Refuses {a} / {b}");
                }
            // Replace mode: a vanilla HV Temp whose original is a lift section, with or without Create, free ends.
            var replace = new PowerJoinFacts { HighVoltage = true, OriginalLiftSide = true };
            Eq(PowerJoinVerdict.RefuseReplace, PowerJoinRule.Decide(replace), w, "replace, no Create");
            replace.Creates = true;
            Eq(PowerJoinVerdict.RefuseReplace, PowerJoinRule.Decide(replace), w, "replace, Create");
            Eq(true, PowerJoinRule.Refuses(PowerJoinVerdict.RefuseReplace), w, "Refuses RefuseReplace");
            Eq(false, PowerJoinRule.Refuses(PowerJoinVerdict.Skip), w, "Refuses Skip");
            Eq(false, PowerJoinRule.Refuses(PowerJoinVerdict.Allow), w, "Refuses Allow");
        }

        private static void Pj3NeverRefused()
        {
            const string w = "power-join/PJ-3";
            PowerJoinTouch j = PowerJoinTouch.LiftNode;

            PowerJoinFacts lv = NewHv(j, j);
            lv.HighVoltage = false;   // LV wire (or any non-HV net): CanConnect already keeps it off our nets
            Eq(PowerJoinVerdict.Skip, PowerJoinRule.Decide(lv), w, "LV wire at a lift node");

            PowerJoinFacts ours = NewHv(j, j);
            ours.LiftSide = true;     // our lift tool's own Temps, the HV stub marker
            ours.OriginalLiftSide = true;
            Eq(PowerJoinVerdict.Skip, PowerJoinRule.Decide(ours), w, "lift-side edge");

            PowerJoinFacts station = NewHv(j, j);
            station.OwnedByOurStation = true;
            Eq(PowerJoinVerdict.Skip, PowerJoinRule.Decide(station), w, "our station's sub-net");

            PowerJoinFacts bulldoze = NewHv(j, j);
            bulldoze.Deleting = true;
            bulldoze.OriginalLiftSide = true;
            Eq(PowerJoinVerdict.Skip, PowerJoinRule.Decide(bulldoze), w, "delete of a wrong join (how it is undone)");

            PowerJoinFacts copy = NewHv(j, j);
            copy.Creates = false;     // untouched Temp copy of an existing vanilla edge
            Eq(PowerJoinVerdict.Skip, PowerJoinRule.Decide(copy), w, "unchanged copy");

            Eq(PowerJoinVerdict.Allow, PowerJoinRule.Decide(NewHv(PowerJoinTouch.None, PowerJoinTouch.None)), w, "HV line elsewhere");
            Eq("free", PowerJoinRule.Describe(PowerJoinTouch.None), w, "Describe None");
        }

        private static void Pj4Episode()
        {
            const string w = "power-join/PJ-4";
            var ep = new PowerJoinEpisode();
            Eq(PowerJoinEpisode.Event.None, ep.Step(false), w, "idle");
            Eq(PowerJoinEpisode.Event.Started, ep.Step(true), w, "first refusal");
            for (int i = 0; i < 100; i++)
                Eq(PowerJoinEpisode.Event.None, ep.Step(true), w, "held refusal logs nothing");
            Eq(101, ep.Evaluations, w, "evaluations counted");
            Eq(PowerJoinEpisode.Event.Ended, ep.Step(false), w, "cleared");
            Eq(PowerJoinEpisode.Event.None, ep.Step(false), w, "idle again");
            Eq(101, ep.Evaluations, w, "the ended episode's count stays readable");
            Eq(PowerJoinEpisode.Event.Started, ep.Step(true), w, "second episode");
            Eq(1, ep.Evaluations, w, "a new episode counts from 1");
            ep.Reset();
            Eq(false, ep.Active, w, "reset clears");
            Eq(PowerJoinEpisode.Event.None, ep.Step(false), w, "reset is silent");
        }
    }
}
