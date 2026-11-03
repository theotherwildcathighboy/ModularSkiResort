// The constants guard between the pure prefab catalog SkiCatalog.cs and the pure rule files that alias it. `public static class` with
// `Run(Func<bool, string, string, bool> check)`; Main passes its Check. Where-tags `catalog-guard...`.
// CG-1 every alias == row == const against Lifts[0], SkiCatalog.FixedGrip and the other catalog rows (the aliases stay the FG row's; per-row
// numbers reach Burst jobs as struct fields). The consts of other rule files are read by REFLECTION over this test assembly, so the host
// builds when one does not exist yet (one "INFO ... not landed yet" line; once present, any mismatch FAILs). CG-2 RideTimeS uses the catalog speed.
// CG-T is a text guard over mod/SkiResortMod/LocaleEN.cs: every number in the hub tips is DERIVED from the catalog, and the tip wording
// must follow the lift rules (a tram row shares the tips; an unpowered lift is inert). Oracles elsewhere: ScoreTests, HubRulesTests, MoneyBatch.
// Engine notes: EngineNotes/ResortBatch.CatalogGuardTests.md

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
            AliasesEqualRows();
            SeamConsts();         // other groups' aliases, reflection
            RideTimeSpeed();
            LocaleTexts();
            OffSeasonTexts();
        }

        /// <summary>The texts of the off-season staffing: the Staff wages tooltip line names the off-season half staff and its Options setting,
        /// the setting's description names a deactivated lodge's cost (a tenth of upkeep, no wages; the note carried it until DECISIONS 566);
        /// the reduced lift texts no longer say "at a quarter" flat (the tram bills half).</summary>
        private static void OffSeasonTexts()
        {
            const string W = "catalog-guard-t137";
            string path = RepoFile("mod/SkiResortMod/LocaleEN.cs");
            if (!Check(path != null, W, "mod/SkiResortMod/LocaleEN.cs not found"))
                return;
            Dictionary<string, string> loc = SingleLineEntries(File.ReadAllLines(path));
            if (Check(loc.TryGetValue("SkiResort.Hub.MONEY_TIP[OffSeason]", out string note), W, "MONEY_TIP[OffSeason] not single-line"))
                Check(note.Contains("half their staff") && note.Contains("(Options)"), W,
                      "MONEY_TIP[OffSeason] = \"" + note + "\" must name the off-season half staff and its Options setting (T137 S2, DECISIONS 566)");
            string settings = RepoFile("mod/SkiResortMod/Settings/LocaleEN.cs");
            if (Check(settings != null, W, "mod/SkiResortMod/Settings/LocaleEN.cs not found"))
            {
                // The description is a concatenation over several lines: join the pieces before the search.
                string joined = Regex.Replace(File.ReadAllText(settings), "\"\\s*\\+\\s*\"", "");
                Match desc = Regex.Match(joined, "GetOptionDescLocaleID\\(nameof\\(SkiResortSettings\\.OffSeasonHalfStaff\\)\\),\\s*\"([^\"]*)\"");
                Check(desc.Success && desc.Groups[1].Value.Contains("deactivated") && desc.Groups[1].Value.Contains("a tenth of its upkeep and no wages"), W,
                      "the OffSeasonHalfStaff description must name a deactivated lodge's cost (a tenth of its upkeep and no wages: " +
                      "CityServiceUpkeepSystem.cs:630-633) (T137 S2, DECISIONS 566); got \"" + (desc.Success ? desc.Groups[1].Value : "(not found)") + "\"");
            }
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

        /// <summary>Other rule files' const aliases of SkiCatalog, read by reflection over this assembly.</summary>
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
            // SkiMoneyRules exists today; its lodge-spend consts land with the lift group
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

            // Both tips hold for a Shuttle row too: the ride tip names no "loop" (a tram cabin rides dock to dock) and the capacity tip no
            // "spaced" (a tram counts its stops).
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

            // T8: the unpowered-lift texts
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
            // T6c: the road-optional lodge text states the cat track link distance, the parking texts the census radius (per lodge).
            Contains(loc, "SkiResort.Hub.BUILD_TIP_ROAD_OPTIONAL", W + "-t6c",
                     LiftServedRule.LinkMeters.ToString("0", CultureInfo.InvariantCulture) + " m");
            string parkingRadius = SkiParkingRules.RadiusM.ToString("0", CultureInfo.InvariantCulture) + " m";
            Contains(loc, "SkiResort.Hub.GUESTS_TIP[CarSpaces]", W + "-t6c", parkingRadius);
            Contains(loc, "SkiResort.Hub.PROBLEM_TIP[NoParkingNearLodge]", W + "-t6c", parkingRadius);
            // T6b: no text states the one-luxury-lodge-per-area rule (there is no limit on lodges), and its two keys (REASON[OneLuxury],
            // RECEIPT_TIP_LUXURY_IGNORED) are gone from the locale.
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

        /// <summary>CG-T8: an unpowered lift of every type stops, has no capacity and serves no run, power never lowers its upkeep, its
        /// power is its base station's; the four tooltips keep the 25-word rule and the two money tips may not grow.</summary>
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
            // (c) the problem tip: stops, base station, maintenance still charged, and no "full" (the NoOpenRun quarter still reaches an
            // unpowered lift whose area counts no run)
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
            // (f) the NoPower reason line: never power
            if (Check(loc.TryGetValue(reducedKey, out string reduced), w, reducedKey + " not found as a single-line entry"))
                Check(!Has(reduced, "power"), w, reducedKey + " = \"" + reduced + "\" blames power: a No power row is billed reduced only for NoOpenRun now");
            // (g) the two money tips are exempt from the 25-word rule (live figures) but may not grow
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
        internal static Dictionary<string, string> SingleLineEntries(string[] lines)
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

        /// <summary>A repo file: the current directory first (the documented run is from the ModularSkiResort root), then walking up
        /// from AppContext.BaseDirectory (tests/ResortBatch/bin/&lt;cfg&gt;/net8.0/). null when not found (the caller writes ONE
        /// FAIL).</summary>
        internal static string RepoFile(string rel)
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
