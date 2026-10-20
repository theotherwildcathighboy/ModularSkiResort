// SkiTripSystems — Round M2 R9 (D15; docs/research/round-m2/understand/enabled-flags.md C1 with its check,
// build-contract.md §5.11 and §6.11; builder B2, 2026-10-10): the names and texts of the game's six trip systems whose
// ComponentSystemBase.Enabled SkiDaySystem reads at every Game load and every armed pass, and of the two known
// trip-planner swaps, detected by assembly name only (SkiOtherMods.Refresh ORs BitOfAssembly over every loaded
// assembly; DECISIONS 150: a label and log lines, never a behavior). The one behavior, ArmVerdict.SystemOff, is
// SkiDayRules' and reads vanilla facts only (the Enabled bits), never this file's planner mask.
//
// Bits of a "found" / "on" mask are SkiDayRules' (TripLeisure 1, TripBehavior 2, TripNeeded 4, TripResidentAI 8,
// TripResidentActions 16, TripResourceBuyer 32). A "planners" mask: bit 1 Realistic PathFinding (assembly
// RealisticPathFinding: switches off TripNeededSystem, ResidentAISystem, ResidentAISystem.Actions and
// ResourceBuyerSystem in its IMod.OnLoad), bit 2 Realistic Trips (assembly Time2Work, inferred from its csproj: switches
// off LeisureSystem, CitizenBehaviorSystem and others; GitHub master, the shipped build unverified).
//
// Strings live here, not in SkiDayRules, because that file is PURE (no managed type, no string). Main thread only,
// never called from a job; `using System;` only. The UI's TRIP_PLANNER_UNKNOWN (bindings.ts) mirrors UnknownLabel by
// hand: no gate rule compares the two strings, so each side names the other in a comment.
// Tests: tests/ResortBatch SkiDayTests section X (literal oracles, enabled-flags.md §3).

using System;

namespace SkiResortMod.Systems
{
    /// <summary>Round M2 R9: names, labels and log texts of the game's trip systems and the known trip-planner swaps.</summary>
    public static class SkiTripSystems
    {
        /// <summary>Realistic PathFinding's assembly (bit 1 of a planners mask).</summary>
        public const string RpfAssembly = "RealisticPathFinding";
        public const string RpfName = "Realistic PathFinding";
        /// <summary>Realistic Trips' assembly (bit 2): the csproj sets no AssemblyName, so the project name (medium).</summary>
        public const string T2wAssembly = "Time2Work";
        public const string T2wName = "Realistic Trips";
        public const string LookedFor = "looked for assemblies RealisticPathFinding, Time2Work";
        /// <summary>The hub label when a trip system is off and no known swap is loaded. Mirrored by hand in
        /// bindings.ts TRIP_PLANNER_UNKNOWN (no gate rule compares the two).</summary>
        // = mod/SkiResortUI/src/bindings.ts TRIP_PLANNER_UNKNOWN (manual parity, no gate rule; tools/test-hub-rules.js T11.8 tests the TS literal).
        public const string UnknownLabel = "?";

        private const int PlannerRpf = 1, PlannerT2w = 2;

        /// <summary>The planner bit of an assembly name: exact, case-sensitive (as SkiOtherMods' RWH match and Mod.cs's
        /// Tourism Overhaul match); 1 RealisticPathFinding, 2 Time2Work, else 0 (null → 0).</summary>
        public static int BitOfAssembly(string name)
        {
            if (name == null)
                return 0;
            if (string.Equals(name, RpfAssembly, StringComparison.Ordinal))
                return PlannerRpf;
            if (string.Equals(name, T2wAssembly, StringComparison.Ordinal))
                return PlannerT2w;
            return 0;
        }

        /// <summary>The known planners of a mask in bit order, joined ", " ("Realistic PathFinding (assembly
        /// RealisticPathFinding)" with the assembly); unknown bits ignored; 0 → "".</summary>
        public static string Names(int planners, bool withAssembly)
        {
            string s = "";
            if ((planners & PlannerRpf) != 0)
                s = withAssembly ? RpfName + " (assembly " + RpfAssembly + ")" : RpfName;
            if ((planners & PlannerT2w) != 0)
                s += (s.Length > 0 ? ", " : "") + (withAssembly ? T2wName + " (assembly " + T2wAssembly + ")" : T2wName);
            return s;
        }

        /// <summary>The game system of one trip bit: 1 LeisureSystem, 2 CitizenBehaviorSystem, 4 TripNeededSystem, 8
        /// ResidentAISystem, 16 ResidentAISystem.Actions, 32 ResourceBuyerSystem; anything else "?".</summary>
        public static string SystemName(int bit)
        {
            switch (bit)
            {
                case SkiDayRules.TripLeisure: return "LeisureSystem";
                case SkiDayRules.TripBehavior: return "CitizenBehaviorSystem";
                case SkiDayRules.TripNeeded: return "TripNeededSystem";
                case SkiDayRules.TripResidentAI: return "ResidentAISystem";
                case SkiDayRules.TripResidentActions: return "ResidentAISystem.Actions";
                case SkiDayRules.TripResourceBuyer: return "ResourceBuyerSystem";
                default: return "?";
            }
        }

        /// <summary>Every trip system in bit order: "&lt;name&gt; on", "&lt;name&gt; OFF" (found, switched off) or
        /// "&lt;name&gt; missing" (not found), joined ", ".</summary>
        public static string Long(int found, int on)
        {
            string s = "";
            for (int bit = 1; bit <= SkiDayRules.TripAll; bit <<= 1)
            {
                string state = (found & bit) == 0 ? " missing" : (on & bit) != 0 ? " on" : " OFF";
                s += (s.Length > 0 ? ", " : "") + SystemName(bit) + state;
            }
            return s;
        }

        /// <summary>"all on" when all six are found and on; else "OFF: " + every system not found and on (a missing one
        /// with " (missing)"), joined ", ", then ", replaced by " + Names(planners, false), or ", no known replacement"
        /// when no known planner is loaded.</summary>
        public static string Short(int found, int on, int planners)
        {
            if (AllOn(found, on))
                return "all on";
            string s = "OFF: ";
            bool first = true;
            for (int bit = 1; bit <= SkiDayRules.TripAll; bit <<= 1)
            {
                if ((found & bit) != 0 && (on & bit) != 0)
                    continue;
                s += (first ? "" : ", ") + SystemName(bit) + ((found & bit) == 0 ? " (missing)" : "");
                first = false;
            }
            string names = Names(planners, false);
            return s + (names.Length > 0 ? ", replaced by " + names : ", no known replacement");
        }

        /// <summary>The trip systems line of a Game load (INFO when all on, WARN otherwise; SkiDaySystem writes it).</summary>
        public static string LoadLine(int found, int on, int planners)
        {
            string tail;
            if (AllOn(found, on))
                tail = "the game's own trip planner runs";
            else if (Names(planners, true).Length > 0)
                tail = "a mod has replaced the game's trip planner: " + Names(planners, true) +
                       "; the intervals SkiDays prints are read from the game's own systems, not from the replacement";
            else
                tail = "a system is switched off and no known replacement is loaded (" + LookedFor + "): another mod, or the game " +
                       "after an error in that system (see the game's logs); the intervals SkiDays prints are read from the game's own systems";
            return "SkiDays: trip systems: " + Long(found, on) + " — " + tail + " (read at city load, after every mod loaded)";
        }

        /// <summary>A change of the trip systems during play (WARN, at most Tuning.TripChangeLines per load;
        /// <paramref name="last"/> on the last one written).</summary>
        public static string ChangeLine(int oldFound, int oldOn, int found, int on, int planners, bool disarms, bool last)
        {
            return "SkiDays: trip systems changed during play: " + Long(found, on) + " (was: " + Short(oldFound, oldOn, planners) + "); " +
                   (disarms
                       ? "ski days need the game's own LeisureSystem and CitizenBehaviorSystem: NOT ARMED from now on this load (claims " +
                         "in flight finish as vanilla leisure trips, as at a load)"
                       : "the arm verdict stands until the next city load") +
                   (last ? " (further changes this load are only counted in the Dump)" : "");
        }

        /// <summary>The hub label (CityStats.tripPlanner): "" when all on; else the known planners' names, or
        /// UnknownLabel when none is loaded.</summary>
        public static string Label(int found, int on, int planners)
        {
            if (AllOn(found, on))
                return "";
            string names = Names(planners, false);
            return names.Length > 0 ? names : UnknownLabel;
        }

        /// <summary>Ski days cannot arm with these systems on (= ArmCheck's SystemOff test): LeisureSystem or
        /// CitizenBehaviorSystem off (CityStats.tripPlannerBlocksSkiDays).</summary>
        public static bool BlocksSkiDays(int on) => (on & SkiDayRules.TripNeededToArm) != SkiDayRules.TripNeededToArm;

        private static bool AllOn(int found, int on)
            => (found & SkiDayRules.TripAll) == SkiDayRules.TripAll && (on & SkiDayRules.TripAll) == SkiDayRules.TripAll;
    }
}
