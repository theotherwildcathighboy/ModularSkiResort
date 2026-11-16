// Tests for the pure rules in SkiRulesLevel.cs (the 1.0.2 per-object rules stamp, T229, DECISIONS 670). The save side (layout, release
// lines, reload) is tests/SaveBatch RulesStampTests.
//   RS-1 levels: absent = 0, the current level, newer-than-me, the carry-over of a re-emitted run
//   RS-2 the stamp-new decision: created by a tool, never on a load, never on a preview
//   RS-3 the census: one old save (all at 0), then one lift and one run built, then a reload (the test pass's sequence, as counts)
// Oracles are literal hand values.

using System;
using SkiResortMod.Systems;
using L = SkiResortMod.Systems.SkiRulesLevel;

namespace ResortBatch
{
    public static class RulesStampTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Levels();
            StampNew();
            Census();
        }

        private static void C(bool ok, string where, string msg) => s_Check(ok, where, msg);

        // RS-1
        private static void Levels()
        {
            const string W = "stamp RS-1 levels";
            C(L.Current == 1 && L.Unknown == 0, W, "1.0.2 stamps level 1; 0 = unknown (no stamp)");
            C(L.LevelOf(false, 7) == 0 && L.LevelOf(true, 0) == 0 && L.LevelOf(true, 1) == 1 && L.LevelOf(true, 4) == 4, W,
              "absent = 0 whatever the field holds; present = the saved level");
            C(L.IsNewer(2) && !L.IsNewer(1) && !L.IsNewer(0), W, "only a level above Current is from a newer build");
            C(L.CarriedLevel(false, 1) == 0 && L.CarriedLevel(true, 0) == 0 && L.CarriedLevel(true, 1) == 1 && L.CarriedLevel(true, 3) == 3, W,
              "a re-emit carries the source level (absent -> explicit 0), never Current");
            C(L.FlagKept == 1 && L.FlagHandEdited == 2, W, "flag bits 1 and 2 (reserved for the sync engine)");
            C(L.KindCount == Enum.GetValues(typeof(SkiRulesKind)).Length, W, "KindCount covers every kind");
        }

        // RS-2
        private static void StampNew()
        {
            const string W = "stamp RS-2 stamp new";
            C(L.StampsNew(true, false, false, false, true), W, "a tool-created object is stamped");
            C(!L.StampsNew(false, false, false, false, true), W, "a loaded object (no Created) is not");
            C(!L.StampsNew(true, false, false, false, false), W, "nothing in the load guard frames");
            C(!L.StampsNew(true, true, false, false, true), W, "not a Temp preview");
            C(!L.StampsNew(true, false, true, false, true), W, "not a deleted object");
            C(!L.StampsNew(true, false, false, true, true), W, "an object with a stamp keeps it (a re-emitted run)");
        }

        // RS-3
        private static void Census()
        {
            const string W = "stamp RS-3 census";
            var counts = new L.Count[L.KindCount];
            C(L.Describe(counts) == "no ski objects", W, "an empty city");

            // an old save: 6 lift sections, 2 stations, 3 runs, 1 cat track, no stamp
            for (int i = 0; i < 6; i++) counts[(int)SkiRulesKind.LiftEdge].Add(L.LevelOf(false, 0));
            for (int i = 0; i < 2; i++) counts[(int)SkiRulesKind.Station].Add(L.LevelOf(false, 0));
            for (int i = 0; i < 3; i++) counts[(int)SkiRulesKind.Run].Add(L.LevelOf(false, 0));
            counts[(int)SkiRulesKind.CatTrack].Add(L.LevelOf(false, 0));
            string load = L.Describe(counts);
            C(load == "lift sections 6 (6 at 0, 0 at 1), stations 2 (2 at 0, 0 at 1), runs 3 (3 at 0, 0 at 1), cat tracks 1 (1 at 0, 0 at 1)", W,
              "old save, all at 0: " + load);

            // one lift (2 sections, 2 stations) and one run built, then a reload: the new ones at 1, the old ones still 0
            counts[(int)SkiRulesKind.LiftEdge].Add(L.LevelOf(true, L.Current));
            counts[(int)SkiRulesKind.LiftEdge].Add(L.LevelOf(true, L.Current));
            counts[(int)SkiRulesKind.Station].Add(L.LevelOf(true, L.Current));
            counts[(int)SkiRulesKind.Station].Add(L.LevelOf(true, L.Current));
            counts[(int)SkiRulesKind.Run].Add(L.LevelOf(true, L.Current));
            counts[(int)SkiRulesKind.Run].Add(L.LevelOf(true, 5));   // a newer build's run
            string after = L.Describe(counts);
            C(after == "lift sections 8 (6 at 0, 2 at 1), stations 4 (2 at 0, 2 at 1), runs 5 (3 at 0, 1 at 1, 1 other), cat tracks 1 (1 at 0, 0 at 1)", W,
              "after building and a reload: " + after);
            C(counts[(int)SkiRulesKind.Run].Total == 5 && counts[(int)SkiRulesKind.Run].Other == 1, W, "a level that is neither 0 nor Current counts as other");
            C(L.Label(SkiRulesKind.Merged) == "merged buildings" && L.Label((SkiRulesKind)200) == "?", W, "labels");
        }
    }
}
