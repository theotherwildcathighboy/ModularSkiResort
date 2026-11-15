// Tests for the "Resort [present]" log lines (mod/SkiResortMod/Systems/ResortPresence.cs). A load runs Reset, then the ownership sweep's
// SetSignsRuns, then the census's SetLifts. The first SetLifts of a load must log its lift count even when the bit did not change, so a
// healthy load never shows only "lifts 0". The mod's Mod.Log is replaced here by a stub that records the lines.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;

namespace SkiResortMod
{
    /// <summary>Test stand-in for the mod's Mod.Log (ILog): records each Info line.</summary>
    internal static class Mod
    {
        public static readonly TestLog Log = new TestLog();
    }

    internal sealed class TestLog
    {
        public readonly List<string> Lines = new List<string>();
        public void Info(string line) => Lines.Add(line);
    }
}

namespace ResortBatch
{
    public static class ResortPresenceTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            HealthyLoad();
            EmptyCity();
            LiftsOnlyCity();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static List<string> Lines => SkiResortMod.Mod.Log.Lines;

        // Signs and runs first, then the census: two lines, the second with the lift count and "; census". Later counts log nothing.
        private static void HealthyLoad()
        {
            Lines.Clear();
            ResortPresence.Reset();
            ResortPresence.SetSignsRuns(1, 12);
            ResortPresence.SetLifts(4);
            ResortPresence.SetLifts(5);
            ResortPresence.SetSignsRuns(1, 13);
            Check(Lines.Count == 2, "presence", "healthy load logs " + Lines.Count + " lines, expected 2: " + string.Join(" | ", Lines));
            Check(Lines.Count > 0 && Lines[0] == "Resort [present] yes (signs 1, runs 12, lifts 0)", "presence", "first line " + At(0));
            Check(Lines.Count > 1 && Lines[1] == "Resort [present] yes (signs 1, runs 12, lifts 4; census)", "presence", "census line " + At(1));

            // The next load logs its census line again.
            Lines.Clear();
            ResortPresence.Reset();
            ResortPresence.SetSignsRuns(1, 12);
            ResortPresence.SetLifts(4);
            Check(Lines.Count == 2 && Lines[1].EndsWith("lifts 4; census)"), "presence", "second load census line " + At(1));
        }

        // No resort: "no", then the census confirms "no" with lifts 0. A lift placed later flips the bit: one "yes" line, no census mark.
        private static void EmptyCity()
        {
            Lines.Clear();
            ResortPresence.Reset();
            ResortPresence.SetSignsRuns(0, 0);
            ResortPresence.SetLifts(0);
            ResortPresence.SetLifts(0);
            ResortPresence.SetLifts(1);
            Check(Lines.Count == 3, "presence", "empty city logs " + Lines.Count + " lines, expected 3: " + string.Join(" | ", Lines));
            Check(At(1) == "Resort [present] no (signs 0, runs 0, lifts 0; census)", "presence", "empty census line " + At(1));
            Check(At(2) == "Resort [present] yes (signs 0, runs 0, lifts 1)", "presence", "lift placed line " + At(2));
            Check(ResortPresence.Present && ResortPresence.EverSeen, "presence", "a lift alone sets Present and EverSeen");
        }

        // Lifts only: the census flips the bit and marks the line in one go (one line, not two).
        private static void LiftsOnlyCity()
        {
            Lines.Clear();
            ResortPresence.Reset();
            ResortPresence.SetSignsRuns(0, 0);
            ResortPresence.SetLifts(3);
            Check(Lines.Count == 2 && At(1) == "Resort [present] yes (signs 0, runs 0, lifts 3; census)", "presence",
                "lifts-only census line " + string.Join(" | ", Lines));
        }

        private static string At(int i) => i < Lines.Count ? Lines[i] : "(none)";
    }
}
