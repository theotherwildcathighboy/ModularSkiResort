// BindingBatch - headless decision-table tests for SkiRunBindingRules (the pure decision only).
// Run (from the ModularSkiResort root): dotnet run --project tests\BindingBatch\BindingBatch.csproj -c Release
// Prints `FAIL  where: msg` lines, report-only INFO lines and one SUMMARY line; Environment.ExitCode = failures.
// The ECS effects (Owner removal, SubArea entry removal, Charge) live in SkiRunOwnershipSystem; the test-local ApplyEffect restates
// the table's Effect column, and the idempotence and money assertions run against that model, not against the ECS code.
// Oracle: the decision table as data (required-true / required-false fact masks per row, first match wins), literal expected values
// and the exact number of combinations each row must win: a different encoding from Decide's if-chain.
// FeeTests: SkiRunFees (base + per hectare, half up to 10, area capped at 2,000 ha, -1 for an unknown kind) against a literal table.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;

namespace BindingBatch
{
    internal static class Program
    {
        private const int MaxPrinted = 250;

        // Fact bit positions in a combination mask (0..127).
        private const int HasBinding = 1 << 0;
        private const int GateNull = 1 << 1;
        private const int GateLive = 1 << 2;
        private const int HasOwner = 1 << 3;
        private const int OwnerIsOurSign = 1 << 4;
        private const int SignInTether = 1 << 5;
        private const int LoadPass = 1 << 6;
        private const int AllFacts = 1 << 7;

        private static int s_Failures;
        private static long s_Checks;
        private static int s_Printed;

        private struct Row
        {
            public int Must, MustNot;
            public BindAction Action;
            public int ExpectedWins;   // combinations of the 128 this row must win (literal)
            public Row(int must, int mustNot, BindAction action, int expectedWins)
            {
                Must = must;
                MustNot = mustNot;
                Action = action;
                ExpectedWins = expectedWins;
            }
        }

        // Decision table, order = priority. ExpectedWins = 2^(free facts) with every earlier row excluded:
        // row 1/2 fix 2 facts (32 each); row 3 fixes !HasOwner, HasBinding, GateLive (16);
        // rows 4/5 fix 4 (8 each); rows 6/7 fix !HasOwner, !HasBinding, SignInTether, LoadPass
        // (8 each); row 8 fixes !HasOwner, !HasBinding, !SignInTether (16). Sum = 128.
        private static readonly Row[] Table =
        {
            new Row(HasOwner | OwnerIsOurSign, 0, BindAction.MigrateLegacy, 32),
            new Row(HasOwner, OwnerIsOurSign, BindAction.LeaveForeign, 32),
            new Row(HasBinding | GateLive, 0, BindAction.KeepBound, 16),
            new Row(HasBinding | SignInTether, GateLive, BindAction.RebindFree, 8),
            new Row(HasBinding, GateLive, BindAction.Orphan, 8),
            new Row(SignInTether | LoadPass, HasBinding, BindAction.RestoreFree, 8),
            new Row(SignInTether, HasBinding, BindAction.BindCharge, 8),
            new Row(0, 0, BindAction.LeaveUnbound, 16),
        };

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            EnumTests();
            TableTests();
            SpotTests();
            InvariantTests();
            IndependenceTests();
            IdempotenceTests();
            ScenarioTests();
            FeeTests(); // SkiRunFees, the one-time run fee by plan area

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "SUMMARY  BindingBatch combinations={0} checks={1} failures={2}",
                AllFacts, s_Checks, s_Failures));
            Environment.ExitCode = s_Failures;
            return s_Failures;
        }

        // ================================================================== reporting

        private static void Fail(string where, string msg)
        {
            s_Failures++;
            if (s_Printed < MaxPrinted)
                Console.WriteLine("FAIL  " + where + ": " + msg);
            else if (s_Printed == MaxPrinted)
                Console.WriteLine("FAIL  (further FAIL lines suppressed; see SUMMARY count)");
            s_Printed++;
        }

        private static bool Check(bool cond, string where, string msg)
        {
            s_Checks++;
            if (!cond)
                Fail(where, msg);
            return cond;
        }

        // ================================================================== helpers

        private static BindFacts FactsOf(int mask)
        {
            return new BindFacts
            {
                HasBinding = (mask & HasBinding) != 0,
                GateNull = (mask & GateNull) != 0,
                GateLive = (mask & GateLive) != 0,
                HasOwner = (mask & HasOwner) != 0,
                OwnerIsOurSign = (mask & OwnerIsOurSign) != 0,
                SignInTether = (mask & SignInTether) != 0,
                LoadPass = (mask & LoadPass) != 0,
            };
        }

        private static BindAction Decide(int mask)
        {
            BindFacts f = FactsOf(mask);
            return SkiRunBindingRules.Decide(in f);
        }

        private static string Name(int mask)
        {
            var parts = new List<string>(7);
            if ((mask & HasBinding) != 0) parts.Add("HasBinding");
            if ((mask & GateNull) != 0) parts.Add("GateNull");
            if ((mask & GateLive) != 0) parts.Add("GateLive");
            if ((mask & HasOwner) != 0) parts.Add("HasOwner");
            if ((mask & OwnerIsOurSign) != 0) parts.Add("OwnerIsOurSign");
            if ((mask & SignInTether) != 0) parts.Add("SignInTether");
            if ((mask & LoadPass) != 0) parts.Add("LoadPass");
            return parts.Count == 0 ? "{}" : "{" + string.Join(",", parts) + "}";
        }

        private static int OracleRow(int mask)
        {
            for (int r = 0; r < Table.Length; r++)
            {
                if ((mask & Table[r].Must) == Table[r].Must && (mask & Table[r].MustNot) == 0)
                    return r;
            }
            return -1;
        }

        // ================================================================== tests

        /// <summary>Exactly the eight actions exist — in particular no "delete" action: the
        /// rules never delete a run on an inferred event.</summary>
        private static void EnumTests()
        {
            Array values = Enum.GetValues(typeof(BindAction));
            Check(values.Length == 8, "enum", "BindAction has " + values.Length + " values, expected 8");
            string[] expected =
            {
                "KeepBound", "MigrateLegacy", "LeaveForeign", "Orphan", "RebindFree", "RestoreFree",
                "BindCharge", "LeaveUnbound",
            };
            for (int i = 0; i < expected.Length; i++)
            {
                Check(Enum.IsDefined(typeof(BindAction), expected[i]), "enum",
                    "BindAction." + expected[i] + " missing (spec §2 name)");
            }
        }

        /// <summary>All 2^7 combinations against the table-as-data oracle, plus the exact number
        /// of combinations each row wins (priority order).</summary>
        private static void TableTests()
        {
            var wins = new int[Table.Length];
            for (int mask = 0; mask < AllFacts; mask++)
            {
                int row = OracleRow(mask);
                if (!Check(row >= 0, "table " + Name(mask), "no oracle row matched (table not total)"))
                    continue;
                wins[row]++;
                BindAction got = Decide(mask);
                Check(got == Table[row].Action, "table " + Name(mask),
                    "Decide=" + got + ", §3.3 row " + (row + 1) + " says " + Table[row].Action);
            }
            for (int r = 0; r < Table.Length; r++)
            {
                Check(wins[r] == Table[r].ExpectedWins, "priority row " + (r + 1),
                    Table[r].Action + " wins " + wins[r] + " combinations, expected " + Table[r].ExpectedWins);
            }
            Console.WriteLine("INFO  table: 128 combinations; row wins " + string.Join("/", wins) +
                              " (MigrateLegacy/LeaveForeign/KeepBound/RebindFree/Orphan/RestoreFree/BindCharge/LeaveUnbound)");
        }

        /// <summary>Literal expectations, written by hand, not from either encoding.</summary>
        private static void SpotTests()
        {
            Spot(0, BindAction.LeaveUnbound, "nothing at all");
            Spot(AllFacts - 1, BindAction.MigrateLegacy, "every fact true");
            Spot(HasBinding | GateLive | SignInTether | LoadPass, BindAction.KeepBound, "bound run at load");
            Spot(HasBinding | GateLive, BindAction.KeepBound, "bound run, sign far (relocated) — stays bound at any distance");
            Spot(HasBinding | GateNull | SignInTether, BindAction.RebindFree, "paid orphan next to a sign");
            Spot(HasBinding | SignInTether, BindAction.RebindFree, "sign died in-session, another sign near");
            Spot(HasBinding | GateNull, BindAction.Orphan, "paid orphan, no sign near");
            Spot(HasBinding, BindAction.Orphan, "sign died in-session, no sign near");
            Spot(HasBinding | GateNull | SignInTether | LoadPass, BindAction.RebindFree, "sign gone at save, another near, at load");
            Spot(SignInTether | LoadPass, BindAction.RestoreFree, "lost link at load (D6)");
            Spot(SignInTether, BindAction.BindCharge, "fresh draw in tether (sweep or paused SweepNow)");
            Spot(LoadPass, BindAction.LeaveUnbound, "unbound at load, no sign near");
            Spot(HasOwner | OwnerIsOurSign | LoadPass, BindAction.MigrateLegacy, "legacy Owner(sign) at load");
            Spot(HasOwner | OwnerIsOurSign | SignInTether, BindAction.MigrateLegacy, "legacy found by a sweep");
            Spot(HasOwner | OwnerIsOurSign | HasBinding | GateLive, BindAction.MigrateLegacy, "half-migrated");
            Spot(HasOwner | SignInTether, BindAction.LeaveForeign, "live non-sign owner, sign near");
            Spot(HasOwner | HasBinding | GateLive | SignInTether | LoadPass, BindAction.LeaveForeign, "foreign owner beats a live binding");
        }

        private static void Spot(int mask, BindAction expected, string what)
        {
            BindAction got = Decide(mask);
            Check(got == expected, "spot " + Name(mask), what + ": Decide=" + got + ", expected " + expected);
        }

        /// <summary>Money and ownership invariants over the whole space.</summary>
        private static void InvariantTests()
        {
            for (int mask = 0; mask < AllFacts; mask++)
            {
                BindAction a = Decide(mask);
                string where = "invariant " + Name(mask);
                bool hasBinding = (mask & HasBinding) != 0;
                bool hasOwner = (mask & HasOwner) != 0;
                bool tether = (mask & SignInTether) != 0;
                bool load = (mask & LoadPass) != 0;

                bool chargeShape = !hasOwner && !hasBinding && tether && !load;
                Check((a == BindAction.BindCharge) == chargeShape, where,
                    "BindCharge must occur exactly for {!HasOwner, !HasBinding, SignInTether, !LoadPass}; got " + a);
                Check(!(load && a == BindAction.BindCharge), where, "the load pass never charges");
                Check(!(hasBinding && (a == BindAction.BindCharge || a == BindAction.RestoreFree)), where,
                    "a run that already has a binding is never charged or restored; got " + a);
                Check(!hasOwner || a == BindAction.MigrateLegacy || a == BindAction.LeaveForeign, where,
                    "an owned run is only migrated or left foreign; got " + a);
                Check(a != BindAction.RestoreFree || (load && !hasBinding && !hasOwner && tether), where,
                    "RestoreFree only at load, for an unowned unbound run in tether");
                Check(tether || (a != BindAction.RebindFree && a != BindAction.RestoreFree && a != BindAction.BindCharge),
                    where, "no link without a sign in tether; got " + a);
                Check(a != BindAction.KeepBound || (hasBinding && (mask & GateLive) != 0 && !hasOwner), where,
                    "KeepBound only for an unowned run with a live gate");
            }
        }

        /// <summary>Facts the table does not read must not change the action.</summary>
        private static void IndependenceTests()
        {
            for (int mask = 0; mask < AllFacts; mask++)
            {
                BindAction a = Decide(mask);
                string where = "independence " + Name(mask);

                Check(Decide(mask ^ GateNull) == a, where,
                    "GateNull changed the action (it may only steer Orphan's effect)");
                if ((mask & HasOwner) == 0)
                    Check(Decide(mask ^ OwnerIsOurSign) == a, where, "OwnerIsOurSign mattered without an Owner");
                if ((mask & HasBinding) == 0)
                    Check(Decide(mask ^ GateLive) == a, where, "GateLive mattered without a binding");

                BindAction flipped = Decide(mask ^ LoadPass);
                bool pair = (a == BindAction.RestoreFree && flipped == BindAction.BindCharge) ||
                            (a == BindAction.BindCharge && flipped == BindAction.RestoreFree);
                Check(flipped == a || pair, where,
                    "LoadPass changed " + a + " into " + flipped + " (only RestoreFree <-> BindCharge may differ)");
            }
        }

        // ------------------------------------------------------------------ effect model

        private enum OwnerKind { None, LiveSign, DeadOwner, Foreign }
        private enum GateKind { None, Live, DeadNonNull, Null }

        private struct World
        {
            public OwnerKind Owner;
            public GateKind Gate;      // None = no binding
            public bool SignInTether;
        }

        private static BindFacts FactsOf(World w, bool loadPass)
        {
            return new BindFacts
            {
                HasBinding = w.Gate != GateKind.None,
                GateNull = w.Gate == GateKind.Null,
                GateLive = w.Gate == GateKind.Live,
                HasOwner = w.Owner != OwnerKind.None,
                OwnerIsOurSign = w.Owner == OwnerKind.LiveSign || w.Owner == OwnerKind.DeadOwner,
                SignInTether = w.SignInTether,
                LoadPass = loadPass,
            };
        }

        /// <summary>The table's Effect column, restated as a model (see file header).</summary>
        private static World ApplyEffect(World w, BindAction a)
        {
            switch (a)
            {
                case BindAction.MigrateLegacy:
                    // (a) SubArea entry, (b) RemoveComponent<Owner>, (c) binding only when absent.
                    if (w.Gate == GateKind.None)
                        w.Gate = w.Owner == OwnerKind.LiveSign ? GateKind.Live : GateKind.Null;
                    w.Owner = OwnerKind.None;
                    break;
                case BindAction.RebindFree:
                case BindAction.RestoreFree:
                case BindAction.BindCharge:
                    w.Gate = GateKind.Live;
                    break;
                case BindAction.Orphan:
                    w.Gate = GateKind.Null;
                    break;
                case BindAction.KeepBound:
                case BindAction.LeaveForeign:
                case BindAction.LeaveUnbound:
                    break;
            }
            return w;
        }

        private static string Name(World w, bool loadPass)
        {
            return "owner=" + w.Owner + " gate=" + w.Gate + " tether=" + w.SignInTether + " load=" + loadPass;
        }

        private static IEnumerable<World> AllWorlds()
        {
            foreach (OwnerKind o in (OwnerKind[])Enum.GetValues(typeof(OwnerKind)))
                foreach (GateKind g in (GateKind[])Enum.GetValues(typeof(GateKind)))
                    for (int t = 0; t < 2; t++)
                        yield return new World { Owner = o, Gate = g, SignInTether = t == 1 };
        }

        /// <summary>Apply the action's effect, decide again (a load pass followed by four
        /// sweeps, and a sweep followed by four sweeps): never BindCharge or RestoreFree for a
        /// run that already has a binding, never MigrateLegacy twice, at most one charge ever,
        /// none for a legacy or already-bound run, and a fixpoint within two steps.</summary>
        private static void IdempotenceTests()
        {
            int sequences = 0;
            foreach (World start in AllWorlds())
            {
                for (int lp = 0; lp < 2; lp++)
                {
                    bool firstIsLoad = lp == 1;
                    string where = "idempotence " + Name(start, firstIsLoad);
                    sequences++;

                    World w = start;
                    int charges = 0, migrations = 0, restores = 0, orphanTransitions = 0;
                    World prev = w;
                    int lastChange = -1;
                    for (int step = 0; step < 5; step++)
                    {
                        bool load = step == 0 && firstIsLoad;
                        BindFacts f = FactsOf(w, load);
                        BindAction a = SkiRunBindingRules.Decide(in f);

                        if (f.HasBinding)
                            Check(a != BindAction.BindCharge && a != BindAction.RestoreFree, where,
                                "step " + step + ": " + a + " on a run that already has a binding");
                        if (a == BindAction.BindCharge)
                            charges++;
                        if (a == BindAction.MigrateLegacy)
                            migrations++;
                        if (a == BindAction.RestoreFree)
                            restores++;
                        if (a == BindAction.Orphan && !f.GateNull)
                            orphanTransitions++;
                        if (load)
                            Check(a != BindAction.BindCharge, where, "the load pass charged");

                        w = ApplyEffect(w, a);
                        if (w.Owner != prev.Owner || w.Gate != prev.Gate)
                            lastChange = step;
                        prev = w;
                    }

                    Check(charges <= 1, where, charges + " charges over one run's life");
                    Check(migrations <= 1, where, "MigrateLegacy ran " + migrations + " times");
                    Check(restores <= 1, where, "RestoreFree ran " + restores + " times");
                    Check(orphanTransitions <= 1, where, "orphan WARN transition " + orphanTransitions + " times");
                    Check(lastChange <= 1, where, "state still changing at step " + lastChange + " (no fixpoint)");
                    bool legacyOrBound = start.Owner == OwnerKind.LiveSign || start.Owner == OwnerKind.DeadOwner ||
                                         start.Gate != GateKind.None;
                    if (legacyOrBound)
                        Check(charges == 0, where, "a legacy or already-bound run was charged");
                    if (start.Owner == OwnerKind.Foreign)
                        Check(charges == 0 && w.Owner == OwnerKind.Foreign && w.Gate == start.Gate, where,
                            "a foreign-owned run was touched");
                    if (firstIsLoad)
                    {
                        // Save + reload after the load pass: the derived state must read the same
                        // and a second load pass must neither restore nor migrate again.
                        BindFacts again = FactsOf(w, true);
                        BindAction a2 = SkiRunBindingRules.Decide(in again);
                        Check(a2 != BindAction.RestoreFree, where, "a second load pass restored again");
                        Check(a2 != BindAction.MigrateLegacy, where, "a second load pass migrated again");
                        Check(a2 != BindAction.BindCharge, where, "a second load pass charged");
                    }
                }
            }
            Console.WriteLine("INFO  idempotence: " + sequences + " consistent start states x {load, sweep} first, 5 decisions each");
        }

        /// <summary>The named scenarios.</summary>
        private static void ScenarioTests()
        {
            // "A paused SweepNow is not a load pass": unbound + sign in tether + LoadPass=false
            // -> BindCharge, never RestoreFree.
            Spot(SignInTether, BindAction.BindCharge, "paused SweepNow on a fresh draw");

            // Legacy in a sweep -> MigrateLegacy, never BindCharge, whatever else is true.
            for (int other = 0; other < AllFacts; other++)
            {
                int mask = (other | HasOwner | OwnerIsOurSign) & ~LoadPass;
                BindAction a = Decide(mask);
                Check(a == BindAction.MigrateLegacy, "scenario legacy-in-sweep " + Name(mask),
                    "legacy Owner(sign) in a sweep gave " + a);
            }

            // Uninstall -> save -> reinstall: restored free at load, then bound for good.
            World w = new World { Owner = OwnerKind.None, Gate = GateKind.None, SignInTether = true };
            int charges = Life(ref w, true, 4);
            Check(w.Gate == GateKind.Live && charges == 0, "scenario reinstall",
                "reinstall restore: gate " + w.Gate + ", charges " + charges);

            // Bound run whose sign dies without the cascade: no other sign -> paid orphan
            // (one WARN transition), kept; a sign placed later -> re-linked free.
            w = new World { Owner = OwnerKind.None, Gate = GateKind.DeadNonNull, SignInTether = false };
            charges = Life(ref w, false, 3);
            Check(w.Gate == GateKind.Null && charges == 0, "scenario orphan",
                "sign died: gate " + w.Gate + ", charges " + charges);
            w.SignInTether = true;
            charges = Life(ref w, false, 3);
            Check(w.Gate == GateKind.Live && charges == 0, "scenario orphan-relink",
                "sign placed near a paid orphan: gate " + w.Gate + ", charges " + charges);

            // A relocated sign: the bound run stays bound at any distance, no charge.
            w = new World { Owner = OwnerKind.None, Gate = GateKind.Live, SignInTether = false };
            charges = Life(ref w, false, 3);
            Check(w.Gate == GateKind.Live && charges == 0, "scenario relocate",
                "sign relocated far: gate " + w.Gate + ", charges " + charges);

            // A save from before run binding existed: Owner(live sign) at load -> migrated bound, never charged.
            w = new World { Owner = OwnerKind.LiveSign, Gate = GateKind.None, SignInTether = true };
            charges = Life(ref w, true, 4);
            Check(w.Owner == OwnerKind.None && w.Gate == GateKind.Live && charges == 0, "scenario pre-O1 save",
                "migration: owner " + w.Owner + ", gate " + w.Gate + ", charges " + charges);

            // A pre-binding save whose sign was gone at save time (Owner remapped to Null): paid
            // orphan, then re-linked free if a sign stands in tether.
            w = new World { Owner = OwnerKind.DeadOwner, Gate = GateKind.None, SignInTether = true };
            charges = Life(ref w, true, 4);
            Check(w.Owner == OwnerKind.None && w.Gate == GateKind.Live && charges == 0, "scenario dead-owner",
                "dead owner: owner " + w.Owner + ", gate " + w.Gate + ", charges " + charges);

            // Fresh draw, then many sweeps: exactly one charge.
            w = new World { Owner = OwnerKind.None, Gate = GateKind.None, SignInTether = true };
            charges = Life(ref w, false, 6);
            Check(w.Gate == GateKind.Live && charges == 1, "scenario fresh-draw",
                "fresh draw: gate " + w.Gate + ", charges " + charges + " (expected exactly 1)");
        }

        // ================================================================== run fees

        private static void FeeEq(int kind, float area, int expected, string what)
        {
            int got = SkiRunFees.Fee(kind, area);
            Check(got == expected, "fee " + what, "expected " + expected + ", got " + got);
        }

        /// <summary>Constants, kind equalities, the literal fee table and the sweep.</summary>
        private static void FeeTests()
        {
            const string W = "fee constants";
            Check(SkiRunFees.NaturalBase == 500, W, "NaturalBase != 500");
            Check(SkiRunFees.NaturalPerHa == 250, W, "NaturalPerHa != 250");
            Check(SkiRunFees.GroomedBase == 2000, W, "GroomedBase != 2000");
            Check(SkiRunFees.GroomedPerHa == 1000, W, "GroomedPerHa != 1000");
            Check(SkiRunFees.SnowmakingBase == 4000, W, "SnowmakingBase != 4000");
            Check(SkiRunFees.SnowmakingPerHa == 4000, W, "SnowmakingPerHa != 4000");
            Check(SkiRunFees.NightBase == 6000, W, "NightBase != 6000");
            Check(SkiRunFees.NightPerHa == 7000, W, "NightPerHa != 7000");
            Check(SkiRunFees.MaxAreaHa == 2000.0, W, "MaxAreaHa != 2000");
            Check(SkiRunFees.RoundTo == 10, W, "RoundTo != 10");

            // The fee kinds ARE RunKind (the bind and the tooltip pass KindOf straight in).
            Check(SkiRunFees.KindGroomed == (int)RunKind.Groomed && SkiRunFees.KindGroomed == 0, "fee kinds", "KindGroomed != RunKind.Groomed (0)");
            Check(SkiRunFees.KindSnowmaking == (int)RunKind.Snowmaking && SkiRunFees.KindSnowmaking == 1, "fee kinds", "KindSnowmaking != RunKind.Snowmaking (1)");
            Check(SkiRunFees.KindNight == (int)RunKind.Night && SkiRunFees.KindNight == 2, "fee kinds", "KindNight != RunKind.Night (2)");
            Check(SkiRunFees.KindNatural == (int)RunKind.Natural && SkiRunFees.KindNatural == 3, "fee kinds", "KindNatural != RunKind.Natural (3)");

            // Natural: 500 + 250/ha.
            FeeEq(SkiRunFees.KindNatural, 0f, 500, "natural 0 m²");
            FeeEq(SkiRunFees.KindNatural, 2000f, 550, "natural 2,000 m²");
            FeeEq(SkiRunFees.KindNatural, 4999f, 620, "natural 4,999 m² (624.975 rounds down)");
            FeeEq(SkiRunFees.KindNatural, 5000f, 630, "natural 5,000 m² (625 exactly: half up)");
            FeeEq(SkiRunFees.KindNatural, 12345f, 810, "natural 12,345 m²");
            FeeEq(SkiRunFees.KindNatural, 20000f, 1000, "natural 20,000 m²");
            FeeEq(SkiRunFees.KindNatural, 100000f, 3000, "natural 100,000 m²");
            // Groomed: 2000 + 1000/ha (2 ha = today's flat 4,000).
            FeeEq(SkiRunFees.KindGroomed, 0f, 2000, "groomed 0 m²");
            FeeEq(SkiRunFees.KindGroomed, 5000f, 2500, "groomed 5,000 m²");
            FeeEq(SkiRunFees.KindGroomed, 12345f, 3230, "groomed 12,345 m²");
            FeeEq(SkiRunFees.KindGroomed, 20000f, 4000, "groomed 20,000 m² (the old flat fee)");
            FeeEq(SkiRunFees.KindGroomed, 100000f, 12000, "groomed 100,000 m²");
            // Snowmaking: 4000 + 4000/ha (2 ha = 12,000).
            FeeEq(SkiRunFees.KindSnowmaking, 12345f, 8940, "snowmaking 12,345 m²");
            FeeEq(SkiRunFees.KindSnowmaking, 20000f, 12000, "snowmaking 20,000 m² (the old flat fee)");
            FeeEq(SkiRunFees.KindSnowmaking, 50000f, 24000, "snowmaking 50,000 m²");
            // Night: 6000 + 7000/ha (2 ha = 20,000).
            FeeEq(SkiRunFees.KindNight, 12345f, 14640, "night 12,345 m²");
            FeeEq(SkiRunFees.KindNight, 20000f, 20000, "night 20,000 m² (the old flat fee)");
            FeeEq(SkiRunFees.KindNight, 100000f, 76000, "night 100,000 m²");
            // Guards: NaN and negatives count as 0 ha; +inf and huge areas are capped at 2,000 ha.
            FeeEq(SkiRunFees.KindNatural, float.NaN, 500, "natural NaN");
            FeeEq(SkiRunFees.KindGroomed, -5f, 2000, "groomed -5 m²");
            FeeEq(SkiRunFees.KindNight, float.PositiveInfinity, 14006000, "night +inf (capped at 2,000 ha)");
            FeeEq(SkiRunFees.KindNight, 1e9f, 14006000, "night 1e9 m² (capped at 2,000 ha)");
            // Unknown kinds.
            // Kind 4 is the cat track and pays the groomed fee by plan area; 5 is the first unknown.
            Check(SkiRunFees.KindCatTrack == (int)RunKind.CatTrack && SkiRunFees.KindCatTrack == 4, "fee kinds", "KindCatTrack != RunKind.CatTrack (4)");
            FeeEq(SkiRunFees.KindCatTrack, 0f, 2000, "cat track 0 m²");
            FeeEq(SkiRunFees.KindCatTrack, 12345f, SkiRunFees.Fee(SkiRunFees.KindGroomed, 12345f), "cat track 12,345 m² = groomed's");
            Check(SkiRunFees.BaseFee(4) == SkiRunFees.GroomedBase && SkiRunFees.PerHaFee(4) == SkiRunFees.GroomedPerHa, "fee cat track", "cat track fee != groomed's");
            FeeEq(5, 12345f, -1, "kind 5");
            FeeEq(-1, 0f, -1, "kind -1");
            Check(SkiRunFees.BaseFee(5) == -1, "fee unknown", "BaseFee(5) != -1");
            Check(SkiRunFees.PerHaFee(-1) == -1, "fee unknown", "PerHaFee(-1) != -1");

            // Sweep: every kind, 0 to 199,400 m² in steps of 997 m² (201 areas): a multiple of ₵10 and
            // never below the previous step's fee (one Check per step).
            for (int kind = 0; kind <= 4; kind++)
            {
                int previous = int.MinValue;
                for (int step = 0; step <= 200; step++)
                {
                    float area = step * 997f;
                    int fee = SkiRunFees.Fee(kind, area);
                    Check(fee % 10 == 0 && fee >= previous, "fee sweep",
                        "kind " + kind + " area " + area.ToString("R") + " m²: fee " + fee + " (previous " + previous +
                        ") must be a multiple of 10 and not below the previous step");
                    previous = fee;
                }
            }
        }

        /// <summary>Runs <paramref name="steps"/> decisions (the first a load pass when asked)
        /// and returns the number of charges.</summary>
        private static int Life(ref World w, bool firstIsLoad, int steps)
        {
            int charges = 0;
            for (int step = 0; step < steps; step++)
            {
                BindFacts f = FactsOf(w, step == 0 && firstIsLoad);
                BindAction a = SkiRunBindingRules.Decide(in f);
                if (a == BindAction.BindCharge)
                    charges++;
                w = ApplyEffect(w, a);
            }
            return charges;
        }
    }
}
