// RunOutlineTests — Round F7 test file for tests/ResortBatch (docs/research/field-test-7-fixes.md item C1: "Tests:
// RunOutlineRules (width and dot size from size, Temp and dot eligibility, colour order, closing side) with literal
// values"). Written 2026-10-04 (Round F7, implementer 2). The e1-spec R9 convention: `public static class
// RunOutlineTests` with `public static void Run(Func<bool, string, string, bool> check)` and its own Near;
// Program.Main calls RunOutlineTests.Run(Check).
//
// ORACLES are literal hand values read off Game.Rendering/AreaBorderRenderSystem.cs (1.6.0f1, identical in 1.6.2f1),
// never a second run of the same code:
//   A  sizes: the clamp 0..16, on / off, line width and dot diameter 0.3 × size (8 → 2.4 m, 16 → 4.8 m);
//   B  the flag mirrors: the literal TempFlags / AreaFlags values (Game.Tools/TempFlags.cs, Game.Areas/AreaFlags.cs;
//      the mod also compares them with the enums at OnCreate) and the two composite masks;
//   C  Outlined (:157-187, :388-437): every Temp flag alone in a Temp-only chunk, Hidden, no Temp, Error and Warning
//      chunks (no Temp filter there), Slave always out;
//   D  Dots (:427-432): Create / Modify only, never without a Temp;
//   E  ColourOf (:388-426): all eight (Error, Warning, Parent) combinations — Error > Warning > Parent > hovered;
//   F  ClosingSide (:243, :487) and the shared-edge keys AddKey / DrawKey (:221-262, :457-498) for both windings;
//   G  the walks AddEdges / DrawArea on hand-built shapes, with the exact draw sequence: an open 3-node Temp (dot,
//      side, dot, side, dot), the same closed, a 2-node line, a 1-node cursor, an empty buffer, no dots for a
//      Delete copy, and two 10 m squares sharing a side (both clockwise; then the neighbour stored counter-clockwise
//      with its flag) — the shared side drawn by neither, its two corners dotted once; a square that only touches a
//      corner keeps all its sides.
//   H  (F7 fixer) SizeChangeDue, the size-change log rule: a switch to 0 is due once a frame would draw; a clamped
//      equal value is not; the review's sequence 8 → 0 → 16 → 8 gives exactly three lines with the right old values;
//      a slider drag gives one line.
// Tolerance ±1e-5 for the float widths.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace ResortBatch
{
    public static class RunOutlineTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            SectionA();
            SectionB();
            SectionC();
            SectionD();
            SectionE();
            SectionF();
            SectionG();
            SectionH();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        // ================================================================== A. sizes

        private static void SectionA()
        {
            const string w = "outline A";
            Check(RunOutlineRules.ClampSize(int.MinValue) == 0, w, "ClampSize(int.MinValue) != 0");
            Check(RunOutlineRules.ClampSize(-5) == 0, w, "ClampSize(-5) != 0");
            Check(RunOutlineRules.ClampSize(0) == 0, w, "ClampSize(0) != 0");
            Check(RunOutlineRules.ClampSize(1) == 1, w, "ClampSize(1) != 1");
            Check(RunOutlineRules.ClampSize(8) == 8, w, "ClampSize(8) != 8");
            Check(RunOutlineRules.ClampSize(16) == 16, w, "ClampSize(16) != 16");
            Check(RunOutlineRules.ClampSize(17) == 16, w, "ClampSize(17) != 16");
            Check(RunOutlineRules.ClampSize(int.MaxValue) == 16, w, "ClampSize(int.MaxValue) != 16");

            Check(!RunOutlineRules.IsOn(0), w, "IsOn(0): size 0 must draw nothing");
            Check(!RunOutlineRules.IsOn(-1), w, "IsOn(-1)");
            Check(RunOutlineRules.IsOn(1), w, "IsOn(1)");
            Check(RunOutlineRules.IsOn(99), w, "IsOn(99) (clamped to 16, on)");

            double[,] table =
            {
                // size, line width = dot diameter (m)
                { -3, 0.0 }, { 0, 0.0 }, { 1, 0.3 }, { 2, 0.6 }, { 3, 0.9 }, { 8, 2.4 }, { 16, 4.8 }, { 40, 4.8 },
            };
            for (int r = 0; r < table.GetLength(0); r++)
            {
                int size = (int)table[r, 0];
                double want = table[r, 1];
                float line = RunOutlineRules.LineWidth(size), dot = RunOutlineRules.DotDiameter(size);
                Check(Near(line, want, 1e-5), w, $"LineWidth({size}) = {line}, want {want}");
                Check(Near(dot, want, 1e-5), w, $"DotDiameter({size}) = {dot}, want {want}");
                Check(line == dot, w, $"size {size}: line {line} != dot {dot} (the game uses 0.3 for both, :517 / :531)");
            }
        }

        // ================================================================== B. flag mirrors

        private static void SectionB()
        {
            const string w = "outline B";
            Check(RunOutlineRules.MaxSize == 16, w, $"MaxSize {RunOutlineRules.MaxSize} (the Options slider max is 16)");
            Check(RunOutlineRules.WidthPerSize == 0.3f, w, $"WidthPerSize {RunOutlineRules.WidthPerSize} (AreaBorderRenderSystem.cs:517, :531)");
            Check(RunOutlineRules.TempCreate == 0x1u, w, "TempFlags.Create = 1");
            Check(RunOutlineRules.TempDelete == 0x2u, w, "TempFlags.Delete = 2");
            Check(RunOutlineRules.TempSelect == 0x20u, w, "TempFlags.Select = 0x20");
            Check(RunOutlineRules.TempModify == 0x40u, w, "TempFlags.Modify = 0x40");
            Check(RunOutlineRules.TempReplace == 0x100u, w, "TempFlags.Replace = 0x100");
            Check(RunOutlineRules.TempHidden == 0x400u, w, "TempFlags.Hidden = 0x400");
            Check(RunOutlineRules.TempParent == 0x800u, w, "TempFlags.Parent = 0x800");
            Check(RunOutlineRules.TempShown == 0x963u, w, $"TempShown 0x{RunOutlineRules.TempShown:X} != 0x963 (Create|Delete|Select|Modify|Replace|Parent)");
            Check(RunOutlineRules.TempDots == 0x41u, w, $"TempDots 0x{RunOutlineRules.TempDots:X} != 0x41 (Create|Modify)");
            Check(RunOutlineRules.AreaComplete == 1, w, "AreaFlags.Complete = 1");
            Check(RunOutlineRules.AreaCounterClockwise == 2, w, "AreaFlags.CounterClockwise = 2");
            Check(RunOutlineRules.AreaSlave == 8, w, "AreaFlags.Slave = 8");
            Check((byte)OutlineColour.Hovered == 0 && (byte)OutlineColour.Owner == 1 && (byte)OutlineColour.Warning == 2 &&
                  (byte)OutlineColour.Error == 3, w, "OutlineColour values");
        }

        // ================================================================== C. Outlined

        private static void SectionC()
        {
            const string w = "outline C";
            // Temp-only chunk: each TempFlags value alone (Game.Tools/TempFlags.cs, all 18).
            uint[] shown = { 0x1u, 0x2u, 0x20u, 0x40u, 0x100u, 0x800u };   // Create Delete Select Modify Replace Parent
            uint[] notShown = { 0x0u, 0x4u, 0x8u, 0x10u, 0x80u, 0x200u, 0x400u, 0x1000u, 0x2000u, 0x4000u, 0x8000u, 0x10000u, 0x20000u };
            foreach (uint f in shown)
                Check(RunOutlineRules.Outlined(false, false, true, f, 0), w, $"Temp-only, flags 0x{f:X} must be outlined");
            foreach (uint f in notShown)
                Check(!RunOutlineRules.Outlined(false, false, true, f, 0), w, $"Temp-only, flags 0x{f:X} must not be outlined");
            // Hidden vetoes in a Temp-only chunk (:178, :418).
            Check(!RunOutlineRules.Outlined(false, false, true, 0x1u | 0x400u, 0), w, "Create|Hidden in a Temp-only chunk");
            Check(!RunOutlineRules.Outlined(false, false, true, 0x40u | 0x400u, 1), w, "Modify|Hidden, complete");
            Check(RunOutlineRules.Outlined(false, false, true, 0x1u | 0x4u | 0x10u, 0), w, "Create|IsLast|Dragging (Create counts)");
            // No Temp, no flag: nothing (the chunk returns, :400-403).
            Check(!RunOutlineRules.Outlined(false, false, false, 0x1u, 0), w, "no Temp and no Error/Warning (tempFlags ignored)");
            // Error / Warning chunks: no Temp filter (:157-164, :388-397).
            Check(RunOutlineRules.Outlined(true, false, false, 0u, 0), w, "Error, permanent area");
            Check(RunOutlineRules.Outlined(false, true, false, 0u, 1), w, "Warning, permanent complete area");
            Check(RunOutlineRules.Outlined(true, false, true, 0x400u, 0), w, "Error chunk, Temp Hidden: still outlined (no Temp filter there)");
            Check(RunOutlineRules.Outlined(false, true, true, 0u, 0), w, "Warning chunk, Temp with no flag: still outlined");
            Check(RunOutlineRules.Outlined(true, true, true, 0x2u, 3), w, "Error + Warning, Delete, complete CCW");
            // Slave: never (:184-187, :434-437).
            Check(!RunOutlineRules.Outlined(true, false, false, 0u, 8), w, "Slave with Error");
            Check(!RunOutlineRules.Outlined(false, false, true, 0x1u, 8), w, "Slave Temp Create");
            Check(!RunOutlineRules.Outlined(false, true, true, 0x40u, 9), w, "Slave, complete, Warning");
            // Winding and completeness do not matter.
            Check(RunOutlineRules.Outlined(false, false, true, 0x1u, 2), w, "CCW Temp Create");
            Check(RunOutlineRules.Outlined(false, false, true, 0x1u, 3), w, "complete CCW Temp Create");
        }

        // ================================================================== D. Dots

        private static void SectionD()
        {
            const string w = "outline D";
            Check(RunOutlineRules.Dots(true, 0x1u), w, "Create → dots");
            Check(RunOutlineRules.Dots(true, 0x40u), w, "Modify → dots");
            Check(RunOutlineRules.Dots(true, 0x41u), w, "Create|Modify → dots");
            Check(RunOutlineRules.Dots(true, 0x1u | 0x400u), w, "Create|Hidden → dots (the game tests only Create|Modify, :427)");
            Check(!RunOutlineRules.Dots(true, 0x2u), w, "Delete (the bulldozer's copy) → no dots");
            Check(!RunOutlineRules.Dots(true, 0x20u), w, "Select → no dots");
            Check(!RunOutlineRules.Dots(true, 0x100u), w, "Replace → no dots");
            Check(!RunOutlineRules.Dots(true, 0x800u), w, "Parent → no dots");
            Check(!RunOutlineRules.Dots(true, 0u), w, "no flag → no dots");
            Check(!RunOutlineRules.Dots(false, 0x1u), w, "no Temp → no dots (:429-432)");
        }

        // ================================================================== E. ColourOf

        private static void SectionE()
        {
            const string w = "outline E";
            for (int m = 0; m < 8; m++)
            {
                bool e = (m & 1) != 0, wa = (m & 2) != 0, p = (m & 4) != 0;
                uint flags = p ? 0x800u | 0x1u : 0x1u;
                OutlineColour want = e ? OutlineColour.Error : wa ? OutlineColour.Warning : p ? OutlineColour.Owner : OutlineColour.Hovered;
                OutlineColour got = RunOutlineRules.ColourOf(e, wa, flags);
                Check(got == want, w, $"error {e} warning {wa} parent {p}: {got}, want {want}");
            }
            Check(RunOutlineRules.ColourOf(false, false, 0x800u) == OutlineColour.Owner, w, "Parent alone → Owner");
            Check(RunOutlineRules.ColourOf(false, false, 0x40u | 0x400u) == OutlineColour.Hovered, w, "Modify|Hidden → Hovered (colour ignores Hidden)");
            Check(RunOutlineRules.ColourOf(false, false, 0u) == OutlineColour.Hovered, w, "no flag → Hovered");
        }

        // ================================================================== F. closing side and keys

        private static void SectionF()
        {
            const string w = "outline F";
            byte[] closed = { 1, 3, 9, 11 };
            byte[] open = { 0, 2, 8, 10 };
            foreach (byte f in closed)
                Check(RunOutlineRules.ClosingSide(f), w, $"area flags {f}: Complete → closing side");
            foreach (byte f in open)
                Check(!RunOutlineRules.ClosingSide(f), w, $"area flags {f}: open → no closing side");

            var p = new float3(1f, 2f, 3f);
            var q = new float3(4f, 5f, 6f);
            RunOutlineRules.AddKey(p, q, 0, out float3 s, out float3 e);
            Check(s.Equals(p) && e.Equals(q), w, "AddKey clockwise = (prev, cur)");
            RunOutlineRules.AddKey(p, q, 1, out s, out e);
            Check(s.Equals(p) && e.Equals(q), w, "AddKey complete clockwise = (prev, cur)");
            RunOutlineRules.AddKey(p, q, 2, out s, out e);
            Check(s.Equals(q) && e.Equals(p), w, "AddKey CounterClockwise = (cur, prev)");
            RunOutlineRules.DrawKey(p, q, 0, out s, out e);
            Check(s.Equals(q) && e.Equals(p), w, "DrawKey clockwise = (cur, prev), the reverse of AddKey");
            RunOutlineRules.DrawKey(p, q, 3, out s, out e);
            Check(s.Equals(p) && e.Equals(q), w, "DrawKey CounterClockwise = (prev, cur)");
        }

        // ================================================================== G. walks

        private struct TestNodes : IOutlineNodes
        {
            public float3[] P;
            public int Count => P == null ? 0 : P.Length;
            public float3 Get(int index) => P[index];
        }

        private struct TestSink : IOutlineSink
        {
            public HashSet<(float3, float3)> Borders;
            public HashSet<float3> DotSet;
            public List<string> Ops;

            public void AddBorder(float3 start, float3 end) => Borders.Add((start, end));
            public bool HasBorder(float3 start, float3 end) => Borders.Contains((start, end));
            public bool AddDot(float3 position) => DotSet.Add(position);
            public void Line(float3 a, float3 b) => Ops.Add("L " + Xz(a) + ">" + Xz(b));
            public void Dot(float3 position) => Ops.Add("D " + Xz(position));
        }

        private static TestSink NewSink()
        {
            return new TestSink { Borders = new HashSet<(float3, float3)>(), DotSet = new HashSet<float3>(), Ops = new List<string>() };
        }

        private static string Xz(float3 v) => ((int)v.x).ToString() + "," + ((int)v.z).ToString();

        private static TestNodes Shape(params int[] xz)
        {
            var p = new float3[xz.Length / 2];
            for (int i = 0; i < p.Length; i++)
                p[i] = new float3(xz[2 * i], 0f, xz[2 * i + 1]);
            return new TestNodes { P = p };
        }

        private static string Join(List<string> ops) => string.Join(" | ", ops);

        private static int CountOps(List<string> ops, char kind)
        {
            int n = 0;
            foreach (string o in ops)
                if (o[0] == kind)
                    n++;
            return n;
        }

        /// <summary>The job's two passes over a set of outlined areas: file every area's sides, then draw.</summary>
        private static TestSink Frame(TestNodes[] areas, byte[] flags, bool[] dots)
        {
            TestSink sink = NewSink();
            for (int i = 0; i < areas.Length; i++)
                RunOutlineRules.AddEdges(ref areas[i], ref sink, flags[i]);
            for (int i = 0; i < areas.Length; i++)
                RunOutlineRules.DrawArea(ref areas[i], ref sink, flags[i], dots[i]);
            return sink;
        }

        private static void SectionG()
        {
            const string w = "outline G";

            // Open 3-node Temp (a run being drawn): dot, side, dot, side, dot — the game's order (:466-484).
            TestSink s1 = Frame(new[] { Shape(0, 0, 0, 10, 10, 10) }, new byte[] { 0 }, new[] { true });
            Check(Join(s1.Ops) == "D 0,0 | L 0,0>0,10 | D 0,10 | L 0,10>10,10 | D 10,10", w, "open 3-node: " + Join(s1.Ops));
            Check(s1.Borders.Count == 2, w, $"open 3-node files {s1.Borders.Count} sides, want 2 (no closing side)");

            // The same, complete: + the closing side 10,10 > 0,0, no extra dot (:487-511).
            TestSink s2 = Frame(new[] { Shape(0, 0, 0, 10, 10, 10) }, new byte[] { 1 }, new[] { true });
            Check(Join(s2.Ops) == "D 0,0 | L 0,0>0,10 | D 0,10 | L 0,10>10,10 | D 10,10 | L 10,10>0,0", w, "closed 3-node: " + Join(s2.Ops));

            // Complete and counter-clockwise: the same lines in node order (only the keys turn).
            TestSink s2b = Frame(new[] { Shape(0, 0, 10, 10, 0, 10) }, new byte[] { 3 }, new[] { true });
            Check(CountOps(s2b.Ops, 'L') == 3 && CountOps(s2b.Ops, 'D') == 3, w, "closed CCW 3-node: " + Join(s2b.Ops));

            // A 2-node line: one side, two dots.
            TestSink s3 = Frame(new[] { Shape(0, 0, 30, 0) }, new byte[] { 0 }, new[] { true });
            Check(Join(s3.Ops) == "D 0,0 | L 0,0>30,0 | D 30,0", w, "2-node line: " + Join(s3.Ops));

            // A 1-node Temp (the cursor before the first click): one dot, no side (:446-452).
            TestSink s4 = Frame(new[] { Shape(5, 7) }, new byte[] { 0 }, new[] { true });
            Check(Join(s4.Ops) == "D 5,7" && s4.Borders.Count == 0, w, "1-node: " + Join(s4.Ops));
            TestSink s4b = Frame(new[] { Shape(5, 7) }, new byte[] { 0 }, new[] { false });
            Check(s4b.Ops.Count == 0, w, "1-node without dots draws nothing: " + Join(s4b.Ops));

            // An empty buffer: nothing filed, nothing drawn (the game would index node 0).
            TestSink s5 = Frame(new[] { new TestNodes { P = new float3[0] } }, new byte[] { 1 }, new[] { true });
            Check(s5.Ops.Count == 0 && s5.Borders.Count == 0, w, "0 nodes: " + Join(s5.Ops));

            // The bulldozer's Delete copy: no dots, every side.
            TestSink s6 = Frame(new[] { Shape(0, 0, 0, 10, 10, 10, 10, 0) }, new byte[] { 1 }, new[] { false });
            Check(Join(s6.Ops) == "L 0,0>0,10 | L 0,10>10,10 | L 10,10>10,0 | L 10,0>0,0", w, "Delete copy: " + Join(s6.Ops));

            // Two clockwise 10 m squares sharing the side x = 10 (A walks it 10,10 > 10,0; B walks it 10,0 > 10,10):
            // the game draws that side for neither (Verification fact 16), and the shared corners once.
            TestNodes a = Shape(0, 0, 0, 10, 10, 10, 10, 0);
            TestNodes b = Shape(10, 0, 10, 10, 20, 10, 20, 0);
            TestSink s7 = Frame(new[] { a, b }, new byte[] { 1, 1 }, new[] { true, true });
            Check(!s7.Ops.Contains("L 10,10>10,0") && !s7.Ops.Contains("L 10,0>10,10"), w, "shared side drawn: " + Join(s7.Ops));
            Check(CountOps(s7.Ops, 'L') == 6, w, $"two squares sharing a side: {CountOps(s7.Ops, 'L')} lines, want 6");
            Check(CountOps(s7.Ops, 'D') == 6, w, $"two squares sharing a side: {CountOps(s7.Ops, 'D')} dots, want 6 (corners 10,0 and 10,10 once)");
            Check(s7.Borders.Count == 8, w, $"filed sides {s7.Borders.Count}, want 8");

            // The neighbour stored counter-clockwise (nodes reversed, CounterClockwise flag): the same result.
            TestNodes bCcw = Shape(10, 0, 20, 0, 20, 10, 10, 10);
            TestSink s8 = Frame(new[] { a, bCcw }, new byte[] { 1, 3 }, new[] { true, true });
            Check(!s8.Ops.Contains("L 10,10>10,0") && !s8.Ops.Contains("L 10,0>10,10"), w, "CCW neighbour, shared side drawn: " + Join(s8.Ops));
            Check(CountOps(s8.Ops, 'L') == 6 && CountOps(s8.Ops, 'D') == 6, w, "CCW neighbour: " + Join(s8.Ops));

            // A neighbour stored counter-clockwise WITHOUT its flag walks the side the same way as A: not a shared
            // key, both draw it (the game's rule keys on the flag, not on geometry).
            TestSink s9 = Frame(new[] { a, bCcw }, new byte[] { 1, 1 }, new[] { true, true });
            Check(CountOps(s9.Ops, 'L') == 8, w, $"unflagged reversed neighbour: {CountOps(s9.Ops, 'L')} lines, want 8");

            // An open Temp whose last side lies on a complete neighbour's side, opposite sense: dropped while open
            // too (the game files open areas' sides, :221-242).
            TestNodes open = Shape(0, 20, 10, 10, 10, 0);   // its side 10,10 > 10,0 is A's
            TestSink s10 = Frame(new[] { a, open }, new byte[] { 1, 0 }, new[] { false, true });
            // A's side 10,10 > 10,0 and the open run's side 10,10 > 10,0 walk the SAME way: kept by both.
            Check(CountOps(s10.Ops, 'L') == 6, w, $"same-sense overlap: {CountOps(s10.Ops, 'L')} lines, want 6 (4 + 2)");
            TestNodes openRev = Shape(0, 20, 10, 0, 10, 10);   // its side 10,0 > 10,10 is A's reversed
            TestSink s11 = Frame(new[] { a, openRev }, new byte[] { 1, 0 }, new[] { false, true });
            Check(!s11.Ops.Contains("L 10,0>10,10") && !s11.Ops.Contains("L 10,10>10,0"), w, "open run on a neighbour's side: " + Join(s11.Ops));
            Check(CountOps(s11.Ops, 'L') == 4 && CountOps(s11.Ops, 'D') == 3, w, "open run on a neighbour's side: " + Join(s11.Ops));

            // A square touching A only at the corner 10,10: no shared side; the corner dotted once.
            TestNodes c = Shape(10, 10, 10, 20, 20, 20, 20, 10);
            TestSink s12 = Frame(new[] { a, c }, new byte[] { 1, 1 }, new[] { true, true });
            Check(CountOps(s12.Ops, 'L') == 8 && CountOps(s12.Ops, 'D') == 7, w, "corner-touching squares: " + Join(s12.Ops));

            // Dots are per frame across areas: a second 1-node Temp at the same point draws no second dot.
            TestSink s13 = Frame(new[] { Shape(5, 7), Shape(5, 7) }, new byte[] { 0, 0 }, new[] { true, true });
            Check(Join(s13.Ops) == "D 5,7", w, "two cursors on one point: " + Join(s13.Ops));
        }

        // ================================================================== H. the size-change log rule (F7 fixer)

        private static void SectionH()
        {
            const string w = "outline H";
            Check(!RunOutlineRules.SizeChangeDue(8, 8, true), w, "8 -> 8 with a draw: no line");
            Check(RunOutlineRules.SizeChangeDue(8, 0, true), w, "8 -> 0 with a surface: due (the switch-off was silent)");
            Check(!RunOutlineRules.SizeChangeDue(8, 0, false), w, "8 -> 0, nothing to draw: waits");
            Check(RunOutlineRules.SizeChangeDue(0, 16, true), w, "0 -> 16 with a draw: due");
            Check(!RunOutlineRules.SizeChangeDue(16, 20, true), w, "16 -> 20 (clamped 16): no line");
            Check(!RunOutlineRules.SizeChangeDue(0, -3, true), w, "0 -> -3 (clamped 0): no line");
            Check(RunOutlineRules.SizeChangeDue(-1, 8, true), w, "never logged (-1) -> 8: due");
            Check(!RunOutlineRules.SizeChangeDue(-1, 8, false), w, "never logged, nothing to draw: waits");

            // The finding's sequence (notes-impl2 step 6): 0, then 16, then back to 8, from 8; each setting held for
            // a few frames, the first frame of each without a surface. Three lines, each with the right old value.
            int logged = 8;
            var lines = new List<string>();
            int[] settings = { 0, 0, 0, 16, 16, 16, 8, 8, 8 };
            bool[] surface = { false, true, true, false, true, true, false, true, true };
            for (int f = 0; f < settings.Length; f++)
            {
                if (RunOutlineRules.SizeChangeDue(logged, settings[f], surface[f]))
                {
                    int now = RunOutlineRules.ClampSize(settings[f]);
                    lines.Add(logged + "->" + now);
                    logged = now;
                }
            }
            Check(string.Join(" ", lines) == "8->0 0->16 16->8", w, "8, 0, 16, 8: lines " + string.Join(" ", lines) +
                  ", want 8->0 0->16 16->8");
            // A slider drag (8 -> 9 -> 10 … 14 between frames without a surface, then one frame with): one line.
            logged = 8;
            lines.Clear();
            for (int s = 9; s <= 14; s++)
            {
                if (RunOutlineRules.SizeChangeDue(logged, s, s == 14))
                {
                    lines.Add(logged + "->" + s);
                    logged = s;
                }
            }
            Check(string.Join(" ", lines) == "8->14", w, "slider drag: lines " + string.Join(" ", lines) + ", want one 8->14");
        }
    }
}
