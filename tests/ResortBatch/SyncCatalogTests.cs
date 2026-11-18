// Tests for the Sync Resort catalog and its pure rules (SkiSyncCatalog.cs, T229, DECISIONS 670).
//   SY-1 the catalog: append-only numbering 1..n, Current = the last number = SkiRulesLevel.Current, step 1 = patrol parts, Offer
//   SY-2 the intake verdict: newer left alone, nothing pending, silent stamp, Offer, Auto, hand-edited lines skipped, order of checks
//   SY-3 "Keep as is" and the hand-edit flag
//   SY-4 the parts check: which rows count, missing and extra parts (the 1.0.0 Patrol Hut without its firewood pile), the variant
//        gate (a garbage bin on a building with no garbage is never placed)
//   SY-5 the run budget
//   SY-6 the census line with the hand-edit and kept counts
// Oracles are literal hand values.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using S = SkiResortMod.Systems.SkiSyncCatalog;
using L = SkiResortMod.Systems.SkiRulesLevel;

namespace ResortBatch
{
    public static class SyncCatalogTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Catalog();
            Verdicts();
            KeepAndHand();
            Parts();
            Budget();
            CensusLine();
        }

        private static void C(bool ok, string where, string msg) => s_Check(ok, where, msg);

        // SY-1
        private static void Catalog()
        {
            const string W = "sync SY-1 catalog";
            C(S.Steps.Length >= 1, W, "at least one step");
            bool numbered = true;
            for (int i = 0; i < S.Steps.Length; i++)
                numbered &= S.Steps[i].Number == i + 1;
            C(numbered, W, "steps are numbered 1, 2, 3 ... in order (append only, never renumbered)");
            C(S.Current == S.Steps.Length && S.Current == L.Current, W,
              "Current = the last step number = SkiRulesLevel.Current (what our tools stamp on new objects)");
            C(S.TryGetStep(1, out SyncStep one) && one.Id == "patrolParts" && one.Class == SyncClass.Offer &&
              one.KindMask == 1 << (int)SkiRulesKind.Patrol && !one.SkipsHandEdited && !one.Retired, W,
              "step 1 = patrol parts (the 1.0.1 firewood pile), Offer, patrol buildings only");
            C(S.StepPatrolParts == 1, W, "the step constant matches its number");
            C(S.KindsTargeted() == 1 << (int)SkiRulesKind.Patrol, W, "1.0.2 checks patrol buildings only");
            var ids = new HashSet<string>();
            bool unique = true;
            foreach (SyncStep s in S.Steps)
                unique &= !string.IsNullOrEmpty(s.Id) && ids.Add(s.Id);
            C(unique, W, "every step has its own id (the hub's sentence key)");
            C(!S.TryGetStep(0, out _) && !S.TryGetStep(S.Current + 1, out _), W, "no step 0, none above Current");
        }

        // SY-2
        private static void Verdicts()
        {
            const string W = "sync SY-2 verdict";
            int calls = 0;
            Func<int, bool> yes = n => { calls++; return true; };
            Func<int, bool> no = n => { calls++; return false; };

            calls = 0;
            C(S.Decide(0, 0, SkiRulesKind.Patrol, yes, out int st) == SyncVerdict.Offer && st == 1 && calls == 1, W,
              "a 1.0.0 hut (level 0) that lacks its firewood: Offer, step 1, one check");
            calls = 0;
            C(S.Decide(0, 0, SkiRulesKind.Patrol, no, out st) == SyncVerdict.Silent && st == 0 && calls == 1, W,
              "a level-0 hut that has every part: stamped current without a word");
            calls = 0;
            C(S.Decide(1, 0, SkiRulesKind.Patrol, yes, out st) == SyncVerdict.NothingPending && calls == 0, W,
              "a hut at level 1 (built on 1.0.2): no step pending, no check runs");
            calls = 0;
            C(S.Decide(0, 0, SkiRulesKind.Run, yes, out st) == SyncVerdict.NothingPending && calls == 0, W,
              "a run: step 1 does not act on runs, nothing to check");
            calls = 0;
            C(S.Decide(0, 0, SkiRulesKind.Station, yes, out st) == SyncVerdict.NothingPending && calls == 0, W,
              "a station: nothing pending in 1.0.2");
            calls = 0;
            C(S.Decide(2, 0, SkiRulesKind.Patrol, yes, out st) == SyncVerdict.Newer && calls == 0, W,
              "a stamp from a newer build: left alone, never checked");
            C(S.Decide(0, L.FlagHandEdited, SkiRulesKind.Patrol, yes, out st) == SyncVerdict.Offer, W,
              "the hand-edit flag matters only for a step that skips hand-edited lines");
            C(S.Decide(0, L.FlagKept, SkiRulesKind.Patrol, yes, out st) == SyncVerdict.Offer, W,
              "Kept at level 0 cannot happen (Keep stamps Current), and the flag alone silences nothing: the level does");
        }

        // SY-3
        private static void KeepAndHand()
        {
            const string W = "sync SY-3 keep / hand";
            C(S.KeptFlags(0) == L.FlagKept && S.KeptFlags(L.FlagHandEdited) == (L.FlagKept | L.FlagHandEdited), W,
              "Keep as is adds the Kept bit and keeps the hand-edit bit");
            C(S.Decide(L.Current, S.KeptFlags(0), SkiRulesKind.Patrol, n => true, out _) == SyncVerdict.NothingPending, W,
              "a kept hut (stamped Current) is not offered again for these steps");
            // A tower step that skips hand-edited lines, as a future catalog would add it (simulated over the same rule)
            var tower = new SyncStep { Number = 2, KindMask = 1 << (int)SkiRulesKind.LiftEdge, Class = SyncClass.Offer, Id = "towers", SkipsHandEdited = true };
            C(S.Pending(tower, 1, SkiRulesKind.LiftEdge) && !S.Pending(tower, 2, SkiRulesKind.LiftEdge) && !S.Pending(tower, 1, SkiRulesKind.Run), W,
              "a step is pending only above the object's level and only for its kinds");
            var retired = new SyncStep { Number = 3, KindMask = 1 << (int)SkiRulesKind.Patrol, Class = SyncClass.Auto, Id = "old", Retired = true };
            C(!S.Pending(retired, 0, SkiRulesKind.Patrol), W, "a retired step never acts");
        }

        // SY-4
        private static void Parts()
        {
            const string W = "sync SY-4 parts";
            C(S.RowCounts(100, 0, false, false, false), W, "a plain row (probability 100, no requirement) counts");
            C(!S.RowCounts(50, 0, false, false, false), W, "a random row does not count");
            C(!S.RowCounts(100, 0x10, false, false, false), W, "a row that requires an elevated spot does not count");
            C(S.RowCounts(100, 0, true, false, true), W,
              "a placeholder row with a free variant counts (the firewood pile row is a placeholder: game turn r2-sync Case A)");
            C(!S.RowCounts(100, 0, true, false, false), W, "a placeholder row whose variants are all gated by a requirement does not count");
            C(!S.RowCounts(100, 0, false, true, false), W, "a row gated by an object requirement (weather, theme) does not count");
            C(!S.RowCounts(100, 0, true, true, true), W, "a placeholder gated by its own requirement does not count");
            // The 1.0.0 hut: the prefab now has one firewood row, the hut has no firewood part
            C(S.Missing(1, 0) == 1 && S.Extra(1, 0) == 0, W, "a 1.0.0 hut lacks one firewood pile");
            C(S.Missing(1, 1) == 0 && S.Extra(1, 1) == 0, W, "after the rebuild: one row, one part, no duplicate");
            C(S.Missing(2, 3) == 0 && S.Extra(2, 3) == 1, W, "a part beyond its rows is counted as extra (the duplicate check)");
            C(S.Missing(3, 1) == 2, W, "two missing of three");
            // The variant gate (Corwen 2026-11-17: 10 stations and huts "lack TrashbinRandom01"). Trashbin01-04 are QuantityObjects of
            // Garbage; vanilla places them only on a GarbageProducer, and our roadless buildings have garbage accumulation 0.
            C(S.VariantPlaceable(false, true, false, true, false), W, "a variant with no QuantityObject is placeable (FirewoodPile01-03)");
            C(!S.VariantPlaceable(true, false, true, true, false), W,
              "a garbage bin variant on a building with no garbage is not placeable (a station's TrashbinRandom01 row: not missing)");
            C(S.VariantPlaceable(true, false, true, true, true), W, "a garbage bin variant on a garbage producer is placeable (Mid / Grand lodge)");
            C(S.VariantPlaceable(true, true, false, true, false), W, "a QuantityObject with no resource and no map feature is placeable");
            C(!S.VariantPlaceable(true, false, false, true, true), W, "another quantity resource is not modeled: not placeable, the row is not counted");
            C(!S.VariantPlaceable(true, true, false, false, true), W, "a map-feature variant (a work vehicle's) is not placeable");
            // The row rule over the gate: no placeable variant = the placeholder row does not count
            bool stationBins = S.RowCounts(100, 0, true, false, S.VariantPlaceable(true, false, true, true, false));
            bool lodgeBins = S.RowCounts(100, 0, true, false, S.VariantPlaceable(true, false, true, true, true));
            C(!stationBins && lodgeBins, W, "a station's bin row does not count, a garbage-producing lodge's does");
        }

        // SY-5
        private static void Budget()
        {
            const string W = "sync SY-5 budget";
            C(S.Take(0, S.ApplyPerFrame) == 0 && S.Take(3, S.ApplyPerFrame) == 3 && S.Take(100, S.ApplyPerFrame) == S.ApplyPerFrame, W,
              "a frame starts at most ApplyPerFrame objects");
            C(S.ApplyPerFrame == 8 && S.IntakePerFrame == 64 && S.VerifyFrames == 10, W, "budget constants (design 4.8)");
            C(S.Take(-1, 8) == 0, W, "never negative");
        }

        // SY-6
        private static void CensusLine()
        {
            const string W = "sync SY-6 census";
            var c = new L.Count();
            c.Add(0, L.FlagHandEdited);
            c.Add(1, 0);
            c.Add(1, L.FlagKept);
            C(c.Total == 3 && c.Level0 == 1 && c.Current == 2 && c.HandEdited == 1 && c.Kept == 1, W, "flags counted beside the levels");
            C(c.ToString() == "3 (1 at 0, 2 at 1, 1 edited by hand, 1 kept as is)", W, "the census text: " + c);
            var plain = new L.Count();
            plain.Add(0);
            C(plain.ToString() == "1 (1 at 0, 0 at 1)", W, "no flag, no extra words (the 1.0.2 load line stays as before)");
        }
    }
}
