// LiftToolInputTests — T125 (one lift tool; docs/research/field/2026-10-20/playthrough/lift-tool-build.md): the lift tool's input
// state machine (LiftLineInput) and the edit model's undo stack (LiftLineEdit.Undo), headless. TI-1..TI-8.
//   TI-1 right click table: every step x dragging x undo depth -> the one action it does (vanilla "Undo Net Control Point").
//   TI-2 a full right-click walk back: two ends + three edits -> undo x3 -> drop the second end -> drop the first -> close.
//   TI-3 undo restores the exact pins / removed spots after add, move, remove (free and pinned) and clear; refusals push nothing.
//   TI-4 the step hint: never empty, different per step, names right click / Enter / Esc where they act, US English.
//   TI-5 drag off the line removes (threshold), on the line moves.
//   TI-6 minimum line length: station-to-station keeps 2 x MinSpanM; a section between loose / ground ends needs 2 x StepM, and
//        the planner plans such a short section clear with no tower.
//   TI-7 Enter on the default plan: the emitted line is the planner's own (stations first / last, every tower Free), so the tool
//        with no edit builds what the old quick-place built, with the planner's towers.
//   TI-8 the type names the hub tiles pass.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    internal static class LiftToolInputTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private static void Check(bool ok, string where, string msg) => s_Check(ok, where, msg);

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            RightClickTable();
            WalkBack();
            UndoRestores();
            Prompts();
            DragOff();
            MinLength();
            DefaultPlan();
            TypeNames();
        }

        private static LiftLineEdit Flat(string type, float length, float ground = 1000f)
        {
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For(type);
            var a = new float3(0f, ground, 0f);
            var b = new float3(length, ground, 0f);
            var e = new LiftLineEdit(c, a, b, ground + c.RopeAboveBase(0), ground + c.RopeAboveBase(0));
            e.Replan((s, l) => ground);
            return e;
        }

        private static readonly LiftTerrainSampler FlatGround = (s, l) => 1000f;

        // TI-1
        private static void RightClickTable()
        {
            const string W = "tool/TI-1";
            Check(LiftLineInput.RightClick(LiftLineStep.PickBase, false, 0) == LiftLineBack.CloseTool, W, "PickBase must close the tool");
            Check(LiftLineInput.RightClick(LiftLineStep.PickTop, false, 0) == LiftLineBack.DropBase, W, "PickTop must drop the first end");
            Check(LiftLineInput.RightClick(LiftLineStep.Edit, false, 0) == LiftLineBack.DropTop, W, "Edit, no edits, must drop the second end");
            Check(LiftLineInput.RightClick(LiftLineStep.Edit, false, 3) == LiftLineBack.UndoEdit, W, "Edit with edits must undo");
            Check(LiftLineInput.RightClick(LiftLineStep.Edit, true, 3) == LiftLineBack.CancelDrag, W, "a drag must be cancelled first");
            Check(LiftLineInput.RightClick(LiftLineStep.Edit, true, 0) == LiftLineBack.CancelDrag, W, "a drag must be cancelled first (no edits)");
            Check(LiftLineInput.RightClick(LiftLineStep.Building, false, 2) == LiftLineBack.None, W, "building must ignore right click");
            // Never the old behavior: no single right click drops a line that has edits.
            foreach (LiftLineStep s in (LiftLineStep[])Enum.GetValues(typeof(LiftLineStep)))
                for (int d = 1; d < 4; d++)
                    Check(LiftLineInput.RightClick(s, false, d) != LiftLineBack.DropTop &&
                          LiftLineInput.RightClick(s, false, d) != LiftLineBack.DropBase || s == LiftLineStep.PickTop,
                          W, $"step {s} depth {d}: a right click dropped an end while edits remain");
            Check(LiftLineInput.After(LiftLineStep.Edit, LiftLineBack.DropTop) == LiftLineStep.PickTop, W, "DropTop -> PickTop");
            Check(LiftLineInput.After(LiftLineStep.PickTop, LiftLineBack.DropBase) == LiftLineStep.PickBase, W, "DropBase -> PickBase");
            Check(LiftLineInput.After(LiftLineStep.Edit, LiftLineBack.UndoEdit) == LiftLineStep.Edit, W, "undo stays in Edit");
        }

        // TI-2: the tool's sequence, driven only through LiftLineInput and LiftLineEdit (what LiftLineToolSystem.StepBack does).
        private static void WalkBack()
        {
            const string W = "tool/TI-2";
            LiftLineEdit e = Flat("detachable", 900f);
            LiftLineStep step = LiftLineStep.Edit;   // two ends clicked
            int towers0 = e.Plan.LineTowers;
            Check(e.AddPin(200f) == LiftLineEditResult.Done, W, "add 200");
            e.Replan(FlatGround);
            Check(e.AddPin(450f) == LiftLineEditResult.Done, W, "add 450");
            e.Replan(FlatGround);
            Check(e.MoveTower(450f, 500f) == LiftLineEditResult.Done, W, "move 450 -> 500");
            e.Replan(FlatGround);
            Check(e.UndoDepth == 3, W, "undo depth after three edits: " + e.UndoDepth);
            var seen = new List<LiftLineBack>();
            bool closed = false;
            for (int guard = 0; guard < 10 && !closed; guard++)
            {
                LiftLineBack back = LiftLineInput.RightClick(step, false, step == LiftLineStep.Edit ? e.UndoDepth : 0);
                seen.Add(back);
                if (back == LiftLineBack.UndoEdit)
                {
                    e.Undo();
                    e.Replan(FlatGround);
                }
                closed = back == LiftLineBack.CloseTool;
                step = LiftLineInput.After(step, back);
            }
            string got = string.Join(",", seen);
            Check(got == "UndoEdit,UndoEdit,UndoEdit,DropTop,DropBase,CloseTool", W, "walk back: " + got);
            Check(e.Pins.Count == 0 && e.Plan.LineTowers == towers0, W, "after three undos the line is the planner's own again");
        }

        private static string State(LiftLineEdit e) => string.Join("/", e.Pins) + "|" + string.Join("/", e.Removed);

        // TI-3
        private static void UndoRestores()
        {
            const string W = "tool/TI-3";
            LiftLineEdit e = Flat("gondola", 1400f);
            string s0 = State(e);
            Check(!e.Undo(), W, "undo with nothing to undo must return false");

            Check(e.AddPin(300f) == LiftLineEditResult.Done, W, "add");
            e.Replan(FlatGround);
            string s1 = State(e);
            Check(e.AddPin(e.C.MinSpanM * 0.5f) == LiftLineEditResult.TooCloseToStation && e.UndoDepth == 1, W,
                  "a refused add pushed an undo step");
            Check(e.AddPin(300f) == LiftLineEditResult.Unchanged && e.UndoDepth == 1, W, "an unchanged add pushed an undo step");

            Check(e.MoveTower(300f, 400f) == LiftLineEditResult.Done, W, "move");
            e.Replan(FlatGround);
            string s2 = State(e);

            // Remove a FREE tower: every other tower gets pinned; one undo must give back exactly s2.
            float free = float.NaN;
            for (int i = 1; i < e.Plan.Nodes.Length - 1; i++)
                if (!e.IsPinned(e.Plan.Nodes[i].AlongM)) { free = e.Plan.Nodes[i].AlongM; break; }
            Check(!float.IsNaN(free), W, "no free tower to remove on the gondola line");
            if (!float.IsNaN(free))
            {
                Check(e.RemoveTower(free) == LiftLineEditResult.Done, W, "remove free");
                Check(e.Removed.Count == 1, W, "remove free did not record the spot");
                e.Replan(FlatGround);
                e.ClearPins();
                Check(e.Pins.Count == 0 && e.Removed.Count == 0, W, "clear");
                Check(e.Undo() && State(e) != s2 && e.Removed.Count == 1, W, "undo of clear did not restore the pins and the removed spot");
                Check(e.Undo() && State(e) == s2, W, "undo of the free-tower remove: " + State(e) + " vs " + s2);
            }
            Check(e.Undo() && State(e) == s1, W, "undo of the move: " + State(e) + " vs " + s1);
            Check(e.Undo() && State(e) == s0, W, "undo of the add: " + State(e) + " vs " + s0);
            Check(e.UndoDepth == 0 && !e.Undo(), W, "undo stack not empty at the start state");

            // Unpin (remove a PINNED tower) is one step too.
            Check(e.AddPin(600f) == LiftLineEditResult.Done, W, "add 600");
            e.Replan(FlatGround);
            Check(e.RemoveTower(600f) == LiftLineEditResult.Done && e.Pins.Count == 0, W, "unpin");
            Check(e.Undo() && e.IsPinned(600f), W, "undo of the unpin");
        }

        // TI-4
        private static void Prompts()
        {
            const string W = "tool/TI-4";
            string[] types = { "", "fixedGrip", "detachable", "gondola", "tram" };
            string[] british = { "colour", "metre", "centre", "grey", "behaviour", "cancelled" };
            foreach (string t in types)
            {
                string pb = LiftLineInput.Prompt(LiftLineStep.PickBase, t, false);
                string pt = LiftLineInput.Prompt(LiftLineStep.PickTop, t, false);
                string ed = LiftLineInput.Prompt(LiftLineStep.Edit, t, false);
                string dr = LiftLineInput.Prompt(LiftLineStep.Edit, t, true);
                string bu = LiftLineInput.Prompt(LiftLineStep.Building, t, false);
                foreach (string p in new[] { pb, pt, ed, dr, bu })
                {
                    Check(!string.IsNullOrWhiteSpace(p), W, $"type '{t}': an empty hint");
                    foreach (string b in british)
                        Check(p.IndexOf(b, StringComparison.OrdinalIgnoreCase) < 0, W, $"British spelling '{b}' in \"{p}\"");
                }
                Check(new HashSet<string> { pb, pt, ed, dr, bu }.Count == 5, W, $"type '{t}': two steps share a hint");
                Check(pb.Contains("right click: close"), W, "PickBase hint must say right click closes: " + pb);
                Check(pt.Contains("right click: back"), W, "PickTop hint must say right click steps back: " + pt);
                Check(ed.Contains("right click: undo") && ed.Contains("Enter: build") && ed.Contains("Esc"), W, "Edit hint: " + ed);
                Check(dr.Contains("right click"), W, "drag hint must say how to cancel: " + dr);
                Check(pb.Contains("open ground") == (t.Length > 0), W, $"open ground offered only with a type ('{t}'): {pb}");
            }
        }

        // TI-5
        private static void DragOff()
        {
            const string W = "tool/TI-5";
            Check(!LiftLineInput.DragRemoves(0f, 15f) && !LiftLineInput.DragRemoves(15f, 15f) && !LiftLineInput.DragRemoves(-14.9f, 15f),
                  W, "on the line must move");
            Check(LiftLineInput.DragRemoves(15.1f, 15f) && LiftLineInput.DragRemoves(-40f, 15f), W, "off the line must remove");
        }

        // TI-6
        private static void MinLength()
        {
            const string W = "tool/TI-6";
            foreach (string t in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                LiftTowerPlanConstants c = LiftTowerPlanConstants.For(t);
                Check(Math.Abs(LiftLineInput.MinLengthM(c, true) - 2f * c.MinSpanM) < 1e-4f, W, t + ": station-to-station minimum changed");
                float sec = LiftLineInput.MinLengthM(c, false);
                Check(Math.Abs(sec - 2f * c.StepM) < 1e-4f, W, t + ": section minimum is not 2 x StepM");
                // A short section between two loose ends (tower nodes) plans clear with no tower.
                float len = math.max(sec, 3f * c.StepM);
                LiftLineEdit e = Flat(t, len);
                Check(e.Plan != null && e.Plan.Nodes.Length >= 2 && e.Plan.Clear, W, $"{t}: a {len:F0} m section did not plan clear: {e.Describe()}");
                Check(e.Plan.LineTowers == 0, W, $"{t}: a {len:F0} m flat section got {e.Plan.LineTowers} tower(s)");
            }
        }

        // TI-7
        private static void DefaultPlan()
        {
            const string W = "tool/TI-7";
            foreach (string t in new[] { "fixedGrip", "detachable", "gondola", "tram" })
            {
                LiftLineEdit e = Flat(t, t == "tram" ? 1800f : t == "fixedGrip" ? 500f : 1200f);
                List<LiftLineNode> n = e.Emit();
                Check(n.Count == e.Plan.Nodes.Length, W, t + ": emitted node count != plan");
                Check(n[0].Kind == LiftLineNodeKind.Station && n[n.Count - 1].Kind == LiftLineNodeKind.Station, W, t + ": ends are not stations");
                bool allFree = true;
                for (int i = 1; i < n.Count - 1; i++)
                    allFree &= n[i].Kind == LiftLineNodeKind.Free;
                Check(allFree && e.UndoDepth == 0 && e.Pins.Count == 0, W, t + ": the default plan carries edits");
                for (int s = 0; s < e.Plan.Spans.Length; s++)
                    Check(e.Plan.Spans[s].LengthM <= e.C.MaxSpanM + 1e-3f, W, $"{t}: span {s} {e.Plan.Spans[s].LengthM:F0} m over the max");
            }
        }

        // TI-8
        private static void TypeNames()
        {
            const string W = "tool/TI-8";
            Check(LiftLineInput.TypeName("fixedGrip") == "Fixed-grip" && LiftLineInput.TypeName("detachable") == "Detachable" &&
                  LiftLineInput.TypeName("gondola") == "Gondola" && LiftLineInput.TypeName("tram") == "Tram", W, "type names");
            Check(LiftLineInput.TypeName("nope") == "Lift" && LiftLineInput.TypeName(null) == "Lift", W, "unknown type name");
        }
    }
}
