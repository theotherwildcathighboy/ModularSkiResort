// CatalogGuardTests — the constants guard between the pure prefab catalog SkiCatalog.cs and the pure rule files that
// alias it. Catalog Phase A (docs/research/art/assets-2026-10/catalog-a/phase-a-contract.md §6.2) created CG-1, CG-2 and
// CG-T1..T4; Catalog Phase B (docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §11.2) updates them.
// Core builder, 2026-10-11. Where-tags `catalog-guard…`. The R9 convention: `public static class` with
// `public static void Run(Func<bool, string, string, bool> check)`; Main passes its Check.
//   CG-1  every alias == row == const: SkiResortScore.LiftValue, SkiMoneyRules.LiftFixedPerMonth / LiftPerKmPerMonth,
//         HubRules.ChairSpeedMps / LiftLinePerKm, LiftServedRule.ServeRadiusM / TopMarginM against Lifts[0] and
//         SkiCatalog.FixedGrip; SkiDayRules.LodgeSkiersPerDay against Lodges[0].Capacity and SkiCatalog.AFrameLodge.
//         Phase B: Lifts.Count == 4 and Lifts[0].Id == "fixedGrip" (replace "Count == 1"; the aliases stay the FG row's,
//         per-row numbers reach Burst jobs as struct fields, contract risk R-a); the seam consts of other groups —
//         SchoolRule (economy, §7.2), PatrolRule (economy, §7.2) and SkiMoneyRules.LodgeSpendDefault / Max / Step
//         (lift, seam S3) — == SkiCatalog.School / Patrol / LodgeSpend. They are read by REFLECTION over this test
//         assembly (the linked files compile into it), so the host builds before those groups land: a file that does
//         not exist yet (SchoolRule.cs / PatrolRule.cs) or a const not added yet prints one "INFO … not landed yet"
//         line; once present, any mismatch FAILs. The integrator confirms no "not landed yet" line remains.
//   CG-2  HubRules.RideTimeS(2045) == 2045 / 2 / Lifts[0].SpeedMps (409.0): the ride time uses the catalog speed.
//   CG-T  text guard over the root mod/SkiResortMod/LocaleEN.cs, single-line `{ "key", "value" },` entries (hubText.ts
//         equals it by gate R4, check-bundle.js, so one side is enough here). Every number is DERIVED from the catalog:
//         T1 Assets.NAME[SkiChairliftHV] == "Fixed-Grip Triple Chairlift" while SeatsPerCarrier == 3 (unchanged);
//         T2 SkiResort.Hub.LIFT_CAPACITY_TIP contains {SEATS} and {SPEED} and no digit (HUB_FMT since contract 6);
//         T3 SkiResort.Hub.LIFT_RIDE_TIP contains {SPEED} and no digit apart from the game-speed "1×";
//         T4 SkiResort.Hub.RECEIPT_TIP[Lifts] names every Loop row "<name> <ScoreValue:0>" in index order (fixed-grip 6,
//            detachable 15, gondola 25) and the summer-share words of every λ < 1 (0.25 "a quarter" for all three
//            chairlift-class types since DECISIONS 376);
//         T5 SkiResort.Hub.RECEIPT_TIP[LodgeBase] contains "up to <Ladder.ExtraCap>" (24);
//         T6 BUILD_TIP_SCHOOL "+<School.ScoreValue>" and "<(DemandFactor − 1) × 100> %" (+12, 20 %); BUILD_TIP_PATROL
//            "<RadiusM / 1000> km", "<(ExpertFactor − 1) × 100> %", "<BackcountryServedMin × 100> %" (1 km, 15 %, 70 %);
//            BUILD_TIP_LUXURY "<the luxury tiers' PriceTolerance>" (40); PROBLEM_TIP[SummitLodgeNotLiftServed]
//            "<Ladder.SummitServedRadiusM> m" (300 m).
//         T7 (Catalog Phase C, docs/research/art/assets-2026-10/code-round-3/round-3-contract.md C15 / §8.3, rules group)
//            SkiResort.Hub.LIFT_RIDE_TIP contains no "loop" and LIFT_CAPACITY_TIP no "spaced" (case-insensitive): both
//            tips are shown for a tram row too (T2 / T3 still hold for the reworded texts). Minors fix (contract review
//            note): the same for SkiResort.Hub.LIFTS_TOTAL_CAPACITY_TIP, the totals row that sums a tram too (no
//            "spaced"; it names the tram's "station stops").
//         T8 (Phase D, docs/research/art/assets-2026-10/code-round-4/round-4-contract.md §7.3 with round-4-contract-skeptic.md
//            MUST-1 / MUST-3, hub group; DECISIONS 171 rulings: an unpowered lift of every type is inert, power never
//            lowers its upkeep, its power is the base station's): case-insensitive —
//            (a) TILE_TIP[Lifts] has no "still move" and says the lift "stops";
//            (b) LIFT_CAPACITY_TIP and LIFTS_TOTAL_CAPACITY_TIP name "power" (an unpowered lift has capacity –);
//            (c) PROBLEM_TIP[LiftNoPower] says it "stops", names the "base station", says maintenance / upkeep is "still
//                charged" and does NOT say "full" (round-4 fixer: the kept NoOpenRun quarter reaches an unpowered lift whose
//                ski area counts no run, round-4-integration.md §6 L1; if Richard chooses "strict" billing, flip this check);
//            (d) each of those four is 25 words or fewer (whitespace-separated tokens, as `wc -w`; LocaleEN.cs:26-31 rule);
//            (e) MONEY_TIP[Lifts] and MONEY_TIP[LiftsTypes]: the "a quarter …" clause (to its full stop) names no power,
//                and the text says "in full" and "power" (ruling 2: power never lowers the bill);
//            (f) LIFT_COST_REDUCED[NoPower] names no power: that row is billed reduced only for NoOpenRun since Phase D
//                (SkiMoneyRules.LiftUpkeepBillingOf), while hubRules.ts liftCostReason still picks this key for it;
//            (g) round-4 fixer: MONEY_TIP[Lifts] has 42 words or fewer and MONEY_TIP[LiftsTypes] 44 or fewer (their Phase D
//                lengths; the LocaleEN.cs header exempts them from the 25-word rule because they carry live figures).
//   BY DESIGN (contract §12.2 step 1, Phase A's R-e precedent): between the core landing and the hub group landing the
//   texts, T2-T6 FAIL (the root LocaleEN.cs still holds contract 5's literal texts / lacks the keys); every other check
//   passes from core onward.
// Untouched oracles elsewhere: ScoreTests.cs (LiftValue == 6.0), HubRulesTests.cs, MoneyBatch.cs, SkiDayTests.cs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class CatalogGuardTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            AliasesEqualRows();   // CG-1
            SeamConsts();         // CG-1 (other groups' aliases, reflection)
            RideTimeSpeed();      // CG-2
            LocaleTexts();        // CG-T
            OffSeasonTexts();     // CG-T137
        }

        /// <summary>CG-T137 (DECISIONS 358): the hub texts of the T137 build. S2: the closed-gate note names the off-season half staff
        /// and vanilla's Inactive (a tenth); S1: the reduced lift texts no longer say "at a quarter" flat (the tram bills half).</summary>
        private static void OffSeasonTexts()
        {
            const string W = "catalog-guard-t137";
            string path = RepoFile("mod/SkiResortMod/LocaleEN.cs");
            if (!Check(path != null, W, "mod/SkiResortMod/LocaleEN.cs not found"))
                return;
            Dictionary<string, string> loc = SingleLineEntries(File.ReadAllLines(path));
            if (Check(loc.TryGetValue("SkiResort.Hub.TICKETS_NOTE[NothingOpen]", out string note), W, "TICKETS_NOTE[NothingOpen] not single-line"))
                Check(note.Contains("half staff") && note.Contains("Inactive") && note.Contains("a tenth of upkeep, no wages"), W,
                      "TICKETS_NOTE[NothingOpen] = \"" + note + "\" must name the off-season half staff and Inactive (a tenth of upkeep, no wages: CityServiceUpkeepSystem.cs:630-633) (T137 S2)");
            foreach (string key in new[] { "SkiResort.Hub.LIFT_COST_REDUCED[NoOpenRun]", "SkiResort.Hub.LIFT_COST_REDUCED[NoPower]",
                                           "SkiResort.Hub.MONEY_TIP[Lifts]", "SkiResort.Hub.MONEY_TIP[LiftsTypes]" })
            {
                if (Check(loc.TryGetValue(key, out string text), W, key + " not single-line"))
                    Check(text.Contains("a tram half") && SkiMoneyRules.ShuttleNoOpenRunShare == 0.5f, W,
                          key + " = \"" + text + "\" must say the tram bills half with no open run (T137 S1, ShuttleNoOpenRunShare 0.5)");
            }
        }

        private static bool Check(bool cond, string where, string msg)
        {
            return s_Check(cond, where, msg);
        }

        // Values (not consts) on purpose: comparing two consts folds at compile time and hides nothing, but passing
        // them through parameters keeps every comparison a real runtime check with a readable message.
        private static void Same(double alias, double row, double constant, string where, string what)
        {
            Check(alias == row && row == constant, where, what + ": alias " + R(alias) + ", row " + R(row) + ", const " + R(constant) +
                  " — the alias must read the catalog (Phase A D4)");
        }

        private static string R(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        // ================================================================== CG-1

        private static void AliasesEqualRows()
        {
            const string W = "catalog-guard-aliases";
            Check(SkiCatalog.Lifts.Count == 4, W, "Lifts.Count = " + SkiCatalog.Lifts.Count + ", want 4 (round-2-contract §1.3)");
            Check(SkiCatalog.Lifts.Count > 0 && SkiCatalog.Lifts[0].Id == "fixedGrip", W,
                  "Lifts[0] is not fixedGrip (APPEND ONLY: the aliases below are row 0's)");
            Check(SkiCatalog.Lodges.Count == 5 && SkiCatalog.Lodges[0].Prefab == SkiCatalog.Names.LodgeAFrame, W,
                  "Lodges.Count = " + SkiCatalog.Lodges.Count + " or Lodges[0] is not the A-frame (APPEND ONLY)");
            if (SkiCatalog.Lifts.Count >= 1)
            {
                LiftTypeDef r = SkiCatalog.Lifts[0];
                Same(SkiResortScore.LiftValue, r.ScoreValue, SkiCatalog.FixedGrip.ScoreValue, W, "SkiResortScore.LiftValue");
                Same(SkiMoneyRules.LiftFixedPerMonth, r.FixedPerMonth, SkiCatalog.FixedGrip.FixedPerMonth, W, "SkiMoneyRules.LiftFixedPerMonth");
                Same(SkiMoneyRules.LiftPerKmPerMonth, r.PerKmPerMonth, SkiCatalog.FixedGrip.PerKmPerMonth, W, "SkiMoneyRules.LiftPerKmPerMonth");
                Same(HubRules.ChairSpeedMps, r.SpeedMps, SkiCatalog.FixedGrip.SpeedMps, W, "HubRules.ChairSpeedMps");
                Same(HubRules.LiftLinePerKm, r.LinePerKm, SkiCatalog.FixedGrip.LinePerKm, W, "HubRules.LiftLinePerKm");
                Same(LiftServedRule.ServeRadiusM, r.ServeRadiusM, SkiCatalog.FixedGrip.ServeRadiusM, W, "LiftServedRule.ServeRadiusM");
                Same(LiftServedRule.TopMarginM, r.TopMarginM, SkiCatalog.FixedGrip.TopMarginM, W, "LiftServedRule.TopMarginM");
            }
            if (SkiCatalog.Lodges.Count >= 1)
                Same(SkiDayRules.LodgeSkiersPerDay, SkiCatalog.Lodges[0].Capacity, SkiCatalog.AFrameLodge.SkiersPerDay, W,
                     "SkiDayRules.LodgeSkiersPerDay vs Lodges[0].Capacity");
        }

        /// <summary>Other groups' const aliases of SkiCatalog (seams S3 and §7.2), read by reflection over this assembly.</summary>
        private static void SeamConsts()
        {
            const string W = "catalog-guard-seams";
            Assembly asm = typeof(CatalogGuardTests).Assembly;
            SeamFile(asm, W, "SkiResortMod.Systems.SchoolRule", "mod/SkiResortMod/Systems/SchoolRule.cs", "economy (round-2-contract §7.2)",
                     new[] { "ScoreValue", "DemandFactor" },
                     new double[] { SkiCatalog.School.ScoreValue, SkiCatalog.School.DemandFactor });
            SeamFile(asm, W, "SkiResortMod.Systems.PatrolRule", "mod/SkiResortMod/Systems/PatrolRule.cs", "economy (round-2-contract §7.2)",
                     new[] { "RadiusM", "ExpertFactor", "BackcountryServedMin" },
                     new double[] { SkiCatalog.Patrol.RadiusM, SkiCatalog.Patrol.ExpertFactor, SkiCatalog.Patrol.BackcountryServedMin });
            // SkiMoneyRules exists today; its lodge-spend consts land with the lift group (seam S3).
            string[] spend = { "LodgeSpendDefault", "LodgeSpendMax", "LodgeSpendStep" };
            double[] spendWant = { SkiCatalog.LodgeSpend.Default, SkiCatalog.LodgeSpend.Max, SkiCatalog.LodgeSpend.Step };
            Type money = typeof(SkiMoneyRules);
            var missing = new List<string>();
            for (int i = 0; i < spend.Length; i++)
            {
                if (!ConstOf(money, spend[i], out double got))
                {
                    missing.Add(spend[i]);
                    continue;
                }
                Check(got == spendWant[i], W, "SkiMoneyRules." + spend[i] + " = " + R(got) + ", SkiCatalog.LodgeSpend says " + R(spendWant[i]));
            }
            if (missing.Count == spend.Length)
                Console.WriteLine("INFO  " + W + ": SkiMoneyRules." + string.Join(" / ", missing) + " not landed yet (lift group, seam S3)");
            else
                Check(missing.Count == 0, W, "SkiMoneyRules lacks " + string.Join(", ", missing) + " (seam S3 wants all three)");
        }

        private static void SeamFile(Assembly asm, string w, string typeName, string file, string owner, string[] fields, double[] want)
        {
            Type t = asm.GetType(typeName, false);
            if (t == null)
            {
                // Not linked: the csproj links it only when the file exists (Condition Exists). A file that exists but did not
                // link is a build problem, not a pending group.
                bool exists = RepoFile(file) != null;
                if (exists)
                    Check(false, w, file + " exists but " + typeName + " is not in the test assembly (rebuild tests/ResortBatch)");
                else
                    Console.WriteLine("INFO  " + w + ": " + typeName + " not landed yet (" + file + ", " + owner + ")");
                return;
            }
            for (int i = 0; i < fields.Length; i++)
            {
                if (!Check(ConstOf(t, fields[i], out double got), w, typeName + "." + fields[i] + " is not a public const"))
                    continue;
                Check(got == want[i], w, typeName + "." + fields[i] + " = " + R(got) + ", SkiCatalog says " + R(want[i]) +
                      " — the alias must read the catalog");
            }
        }

        /// <summary>A public const (literal) field's value as double; false when absent or not a numeric const.</summary>
        private static bool ConstOf(Type t, string name, out double value)
        {
            value = double.NaN;
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (f == null || !f.IsLiteral)
                return false;
            object raw = f.GetRawConstantValue();
            if (raw is IConvertible c && !(raw is string) && !(raw is bool))
            {
                // float → double through the float itself, so 1000f compares as 1000.0 and 1.15f would show its drift
                value = raw is float fl ? fl : c.ToDouble(CultureInfo.InvariantCulture);
                return true;
            }
            return false;
        }

        // ================================================================== CG-2

        private static void RideTimeSpeed()
        {
            const string W = "catalog-guard-ride";
            if (SkiCatalog.Lifts.Count < 1)
                return;
            float got = HubRules.RideTimeS(2045f);
            float want = 2045f / 2f / SkiCatalog.Lifts[0].SpeedMps;
            Check(got == want && Math.Abs(got - 409.0) <= 1e-4, W, "RideTimeS(2045) = " + got.ToString("R", CultureInfo.InvariantCulture) +
                  ", want 2045 / 2 / " + SkiCatalog.Lifts[0].SpeedMps.ToString(CultureInfo.InvariantCulture) + " = 409");
        }

        // ================================================================== CG-T

        private static void LocaleTexts()
        {
            const string W = "catalog-guard-text";
            string path = RepoFile("mod/SkiResortMod/LocaleEN.cs");
            if (!Check(path != null, W, "mod/SkiResortMod/LocaleEN.cs not found from " + Directory.GetCurrentDirectory() +
                                        " or above " + AppContext.BaseDirectory))
                return;
            Dictionary<string, string> loc = SingleLineEntries(File.ReadAllLines(path));
            if (SkiCatalog.Lifts.Count < 1)
                return;
            LiftTypeDef r = SkiCatalog.Lifts[0];
            string seats = r.SeatsPerCarrier.ToString(CultureInfo.InvariantCulture);

            // T1: the name says "Triple" while the catalog says 3 seats (unchanged).
            string nameKey = "Assets.NAME[" + r.LinePrefab + "]";
            if (Check(loc.TryGetValue(nameKey, out string name), W + "-t1", nameKey + " not found as a single-line entry"))
            {
                Check(name == "Fixed-Grip Triple Chairlift", W + "-t1", nameKey + " = \"" + name + "\", want \"Fixed-Grip Triple Chairlift\" (ROUND-SPEC R2)");
                Check(r.SeatsPerCarrier == 3, W + "-t1", "SeatsPerCarrier = " + seats + " but the name says Triple: a seat change must revisit the name");
            }

            // T2: the capacity tip renders the row's seats and speed through placeholders (no baked number).
            const string capKey = "SkiResort.Hub.LIFT_CAPACITY_TIP";
            if (Check(loc.TryGetValue(capKey, out string cap), W + "-t2", capKey + " not found as a single-line entry"))
            {
                Check(cap.Contains("{SEATS}") && cap.Contains("{SPEED}"), W + "-t2", capKey + " = \"" + cap + "\", want {SEATS} and {SPEED}");
                Check(!HasDigit(cap), W + "-t2", capKey + " = \"" + cap + "\" bakes a number (every type has its own)");
            }

            // T3: the ride tip renders the row's speed (the game-speed "1×" is not a catalog number).
            const string rideKey = "SkiResort.Hub.LIFT_RIDE_TIP";
            if (Check(loc.TryGetValue(rideKey, out string ride), W + "-t3", rideKey + " not found as a single-line entry"))
            {
                Check(ride.Contains("{SPEED}"), W + "-t3", rideKey + " = \"" + ride + "\", want {SPEED}");
                Check(!HasDigit(ride.Replace("1×", "")), W + "-t3", rideKey + " = \"" + ride + "\" bakes a number besides \"1×\"");
            }

            // T7 (Catalog Phase C, round-3-contract C15 / §8.3): both tips hold for a Shuttle row too — the ride tip names
            // no "loop" (a tram cabin rides dock to dock) and the capacity tip no "spaced" (a tram counts its stops).
            if (Check(loc.TryGetValue(rideKey, out string ride7), W + "-t7", rideKey + " not found as a single-line entry"))
                Check(ride7.IndexOf("loop", StringComparison.OrdinalIgnoreCase) < 0, W + "-t7",
                      rideKey + " = \"" + ride7 + "\" says \"loop\": false for the tram (C15)");
            if (Check(loc.TryGetValue(capKey, out string cap7), W + "-t7", capKey + " not found as a single-line entry"))
                Check(cap7.IndexOf("spaced", StringComparison.OrdinalIgnoreCase) < 0, W + "-t7",
                      capKey + " = \"" + cap7 + "\" says \"spaced\": false for the tram (C15)");
            // The totals row (LiftsView) sums every complete lift, a tram's capacity included: same two words.
            const string totalKey = "SkiResort.Hub.LIFTS_TOTAL_CAPACITY_TIP";
            if (Check(loc.TryGetValue(totalKey, out string total7), W + "-t7", totalKey + " not found as a single-line entry"))
            {
                Check(total7.IndexOf("spaced", StringComparison.OrdinalIgnoreCase) < 0, W + "-t7",
                      totalKey + " = \"" + total7 + "\" says \"spaced\": false for the tram (C15)");
                Check(total7.IndexOf("station stops", StringComparison.OrdinalIgnoreCase) >= 0, W + "-t7",
                      totalKey + " = \"" + total7 + "\" does not say that a tram counts its station stops (like LIFT_CAPACITY_TIP)");
            }

            // T8 (Phase D, round-4-contract §7.3, skeptic MUST-1 / MUST-3): the unpowered-lift texts follow DECISIONS 171.
            UnpoweredTexts(loc, W + "-t8", capKey, totalKey);

            // T4: the receipt names every Loop type's value in index order and the summer words.
            const string receiptKey = "SkiResort.Hub.RECEIPT_TIP[Lifts]";
            if (Check(loc.TryGetValue(receiptKey, out string receipt), W + "-t4", receiptKey + " not found as a single-line entry"))
            {
                int last = -1;
                var words = new List<string>();
                for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
                {
                    LiftTypeDef d = SkiCatalog.Lifts[i];
                    if (d.Mover != MoverKind.Loop)
                        continue;
                    string want = TypeWord(d.Id) + " " + d.ScoreValue.ToString("0", CultureInfo.InvariantCulture);
                    int at = receipt.IndexOf(want, StringComparison.Ordinal);
                    Check(at >= 0, W + "-t4", receiptKey + " = \"" + receipt + "\" lacks \"" + want + "\"");
                    Check(at < 0 || at > last, W + "-t4", receiptKey + ": \"" + want + "\" is out of index order");
                    if (at >= 0)
                        last = at;
                    string word = ShareWord(d.SummerShare);
                    if (d.SummerShare < 1.0 && !words.Contains(word))
                        words.Add(word);
                }
                foreach (string word in words)
                    Check(word != null && receipt.Contains(word), W + "-t4", receiptKey + " = \"" + receipt + "\" lacks the summer share \"" + word + "\"");
            }

            // T5: the lodge base tip states the ladder's extras cap.
            const string lodgeKey = "SkiResort.Hub.RECEIPT_TIP[LodgeBase]";
            string capText = "up to " + SkiCatalog.Ladder.ExtraCap.ToString("0", CultureInfo.InvariantCulture);
            if (Check(loc.TryGetValue(lodgeKey, out string lodge), W + "-t5", lodgeKey + " not found as a single-line entry"))
                Check(lodge.Contains(capText), W + "-t5", lodgeKey + " = \"" + lodge + "\", want \"" + capText + "\"");

            // T6: the school, patrol, luxury and summit texts state the catalog's numbers.
            Contains(loc, "SkiResort.Hub.BUILD_TIP_SCHOOL", W + "-t6",
                     "+" + SkiCatalog.School.ScoreValue.ToString("0", CultureInfo.InvariantCulture),
                     Pct(SkiCatalog.School.DemandFactor - 1.0) + " %");
            Contains(loc, "SkiResort.Hub.BUILD_TIP_PATROL", W + "-t6",
                     (SkiCatalog.Patrol.RadiusM / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " km",
                     Pct(SkiCatalog.Patrol.ExpertFactor - 1.0) + " %",
                     Pct(SkiCatalog.Patrol.BackcountryServedMin) + " %");
            int luxuryTolerance = 0;
            foreach (LodgeDef d in SkiCatalog.Lodges)
            {
                if (d.Luxury)
                    luxuryTolerance = Math.Max(luxuryTolerance, d.PriceTolerance);
            }
            Contains(loc, "SkiResort.Hub.BUILD_TIP_LUXURY", W + "-t6", luxuryTolerance.ToString(CultureInfo.InvariantCulture));
            // T6b (2026-10-11, Richard: no limit on lodges): no text states the one-luxury-lodge-per-area rule, and its two
            // keys (REASON[OneLuxury], RECEIPT_TIP_LUXURY_IGNORED) are gone from the locale.
            Check(!loc.ContainsKey("SkiResort.Hub.REASON[OneLuxury]") && !loc.ContainsKey("SkiResort.Hub.RECEIPT_TIP_LUXURY_IGNORED"),
                  W + "-t6b", "REASON[OneLuxury] / RECEIPT_TIP_LUXURY_IGNORED are removed from the locale");
            string limitText = null;
            foreach (KeyValuePair<string, string> kv in loc)
            {
                bool luxury = kv.Value.IndexOf("luxury", StringComparison.OrdinalIgnoreCase) >= 0;
                bool perArea = kv.Value.IndexOf("per ski area", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               kv.Value.IndexOf("one per", StringComparison.OrdinalIgnoreCase) >= 0;
                if (luxury && perArea && limitText == null)
                    limitText = kv.Key + " = \"" + kv.Value + "\"";
            }
            Check(limitText == null, W + "-t6b", "no locale text states a per-area limit on luxury lodges: " + limitText);
            Contains(loc, "SkiResort.Hub.PROBLEM_TIP[SummitLodgeNotLiftServed]", W + "-t6",
                     SkiCatalog.Ladder.SummitServedRadiusM.ToString("0", CultureInfo.InvariantCulture) + " m");
        }

        /// <summary>CG-T8 (Phase D): DECISIONS 171 — an unpowered lift of every type stops, has no capacity and serves no run
        /// (ruling 1), power never lowers its upkeep (ruling 2), its power is its base station's (ruling 3); the four tooltips
        /// keep the 25-word rule (skeptic MUST-3) and the two money tips may not grow (round-4 fixer, (g)).</summary>
        private static void UnpoweredTexts(Dictionary<string, string> loc, string w, string capKey, string totalKey)
        {
            const string tileKey = "SkiResort.Hub.TILE_TIP[Lifts]";
            const string problemKey = "SkiResort.Hub.PROBLEM_TIP[LiftNoPower]";
            const string reducedKey = "SkiResort.Hub.LIFT_COST_REDUCED[NoPower]";
            string[] moneyKeys = { "SkiResort.Hub.MONEY_TIP[Lifts]", "SkiResort.Hub.MONEY_TIP[LiftsTypes]" };

            // (a) the tile tip: the carriers stop (ChairPinSystem hold, the tram Held).
            if (Check(loc.TryGetValue(tileKey, out string tile), w, tileKey + " not found as a single-line entry"))
            {
                Check(!Has(tile, "still move"), w, tileKey + " = \"" + tile + "\" says the chairs still move (Phase D stops them)");
                Check(Has(tile, "stops"), w, tileKey + " = \"" + tile + "\" does not say an unpowered lift stops");
            }
            // (b) both capacity tips: an unpowered lift counts none.
            foreach (string key in new[] { capKey, totalKey })
            {
                if (Check(loc.TryGetValue(key, out string text), w, key + " not found as a single-line entry"))
                    Check(Has(text, "power"), w, key + " = \"" + text + "\" does not name power (an unpowered lift shows capacity –)");
            }
            // (c) the problem tip: stops, base station (ruling 3), maintenance still charged (ruling 2), and no "full" (the
            // NoOpenRun quarter still reaches an unpowered lift whose area counts no run: round-4 fixer, integration §6 L1).
            if (Check(loc.TryGetValue(problemKey, out string problem), w, problemKey + " not found as a single-line entry"))
            {
                Check(Has(problem, "stops"), w, problemKey + " = \"" + problem + "\" does not say the lift stops");
                Check(Has(problem, "base station"), w, problemKey + " = \"" + problem + "\" does not name the base station (ruling 3)");
                Check(Has(problem, "still charged") && (Has(problem, "maintenance") || Has(problem, "upkeep")), w,
                      problemKey + " = \"" + problem + "\" does not say maintenance is still charged (ruling 2)");
                Check(!Has(problem, "full"), w,
                      problemKey + " = \"" + problem + "\" says \"full\": false while its ski area counts no run (NoOpenRun 25 %)");
            }
            // (d) the 25-word rule for the four reworded tooltips.
            foreach (string key in new[] { tileKey, capKey, totalKey, problemKey })
            {
                if (!loc.TryGetValue(key, out string text))
                    continue;   // reported above
                int words = Words(text);
                Check(words <= 25, w, key + " has " + words + " words (25 or fewer: skeptic MUST-3, LocaleEN.cs:26-31)");
            }
            // (e) the money tips: power is not a reduction any more.
            foreach (string key in moneyKeys)
            {
                if (!Check(loc.TryGetValue(key, out string money), w, key + " not found as a single-line entry"))
                    continue;
                int q = money.IndexOf("a quarter", StringComparison.OrdinalIgnoreCase);
                if (Check(q >= 0, w, key + " = \"" + money + "\" lost its \"a quarter\" clause"))
                {
                    int end = money.IndexOf('.', q);
                    string clause = end < 0 ? money.Substring(q) : money.Substring(q, end - q);
                    Check(!Has(clause, "power"), w, key + ": the reduced clause \"" + clause + "\" names power (ruling 2: full upkeep regardless of power)");
                }
                Check(Has(money, "in full") && Has(money, "power"), w,
                      key + " = \"" + money + "\" does not say a lift without power pays in full (ruling 2)");
            }
            // (f) the NoPower reason line: never power since Phase D.
            if (Check(loc.TryGetValue(reducedKey, out string reduced), w, reducedKey + " not found as a single-line entry"))
                Check(!Has(reduced, "power"), w, reducedKey + " = \"" + reduced + "\" blames power: a No power row is billed reduced only for NoOpenRun now");
            // (g) round-4 fixer: the two money tips are exempt from the 25-word rule (live figures) but may not grow.
            int[] moneyCaps = { 42, 44 };
            for (int i = 0; i < moneyKeys.Length; i++)
            {
                if (!loc.TryGetValue(moneyKeys[i], out string moneyText))
                    continue;   // reported in (e)
                int moneyWords = Words(moneyText);
                Check(moneyWords <= moneyCaps[i], w, moneyKeys[i] + " has " + moneyWords + " words (cap " + moneyCaps[i] +
                      ", its Phase D length: exempt from the 25-word rule, LocaleEN.cs header, but it may not grow)");
            }
        }

        private static bool Has(string text, string part)
        {
            return text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Whitespace-separated tokens (`wc -w`): "m/s," and "low-voltage" are one word each.</summary>
        private static int Words(string text)
        {
            return text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static void Contains(Dictionary<string, string> loc, string key, string w, params string[] parts)
        {
            if (!Check(loc.TryGetValue(key, out string text), w, key + " not found as a single-line entry"))
                return;
            foreach (string p in parts)
                Check(text.Contains(p), w, key + " = \"" + text + "\", want \"" + p + "\"");
        }

        /// <summary>(0.2 → "20"): a share as a whole percent, rounded (1.15 − 1 is 0.1499… in binary).</summary>
        private static string Pct(double share)
        {
            return Math.Round(share * 100.0).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string TypeWord(string id)
        {
            return id == "fixedGrip" ? "fixed-grip" : id;
        }

        private static string ShareWord(double share)
        {
            if (share == 0.25)
                return "a quarter";
            if (share == 0.5)
                return "half";
            if (share == 0.75)
                return "three quarters";
            return null;   // a new λ needs its word here (and in the text)
        }

        private static bool HasDigit(string s)
        {
            foreach (char c in s)
            {
                if (c >= '0' && c <= '9')
                    return true;
            }
            return false;
        }

        /// <summary>Every single-line `{ "key", "value" },` entry (first one wins), with \" and \\ unescaped. Multi-line
        /// entries (`"a" + "b"` over several lines) are not read: the guarded keys must stay single-line.</summary>
        private static Dictionary<string, string> SingleLineEntries(string[] lines)
        {
            var rx = new Regex(@"^\s*\{\s*""((?:[^""\\]|\\.)*)""\s*,\s*""((?:[^""\\]|\\.)*)""\s*\}\s*,?\s*$");
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < lines.Length; i++)
            {
                Match m = rx.Match(lines[i]);
                if (!m.Success)
                    continue;
                string key = Unescape(m.Groups[1].Value);
                if (!map.ContainsKey(key))
                    map.Add(key, Unescape(m.Groups[2].Value));
            }
            return map;
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0)
                return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        /// <summary>A repo file: the current directory first (the documented run is from the ModularSkiResort root), then
        /// walking up from AppContext.BaseDirectory (tests/ResortBatch/bin/&lt;cfg&gt;/net8.0/; the SlopeBatch
        /// Program.cs:4889-4898 precedent, skeptic S4). null when not found (the caller writes ONE FAIL).</summary>
        private static string RepoFile(string rel)
        {
            string native = rel.Replace('/', Path.DirectorySeparatorChar);
            string p = Path.Combine(Directory.GetCurrentDirectory(), native);
            if (File.Exists(p))
                return p;
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                p = Path.Combine(d.FullName, native);
                if (File.Exists(p))
                    return p;
            }
            return null;
        }
    }
}
