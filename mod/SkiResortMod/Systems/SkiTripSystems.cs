// Names and texts of the game's six trip systems and of the two known trip-planner swaps. SkiDaySystem reads the Enabled state of the
// trip systems at every Game load and armed pass. The code detects the swaps by assembly name only (SkiOtherMods.Refresh ORs
// BitOfAssembly over every loaded assembly). These are labels and log lines, never behavior. The one behavior, ArmVerdict.SystemOff,
// belongs to SkiDayRules and reads the Enabled bits only.
// The Found / on mask bits belong to SkiDayRules (TripLeisure 1 ... TripResourceBuyer 32). Planners mask:
// - Bit 1: Realistic PathFinding (assembly RealisticPathFinding). Its IMod.OnLoad disables TripNeededSystem, ResidentAISystem,
//   ResidentAISystem.Actions and ResourceBuyerSystem.
// - Bit 2: Realistic Trips (assembly Time2Work, inferred from its csproj). The shipped build is unverified.
// Strings live here because SkiDayRules is pure (no strings). Main thread only. UnknownLabel is mirrored by hand in bindings.ts
// TRIP_PLANNER_UNKNOWN. Tests: tests/ResortBatch SkiDayTests section X.

using System;

namespace SkiResortMod.Systems
{
    /// <summary>Names, labels and log texts of the game's trip systems and the known trip-planner swaps.</summary>
    public static class SkiTripSystems
    {
        /// <summary>Realistic PathFinding's assembly (bit 1 of a planners mask).</summary>
        public const string RpfAssembly = "RealisticPathFinding";
        public const string RpfName = "Realistic PathFinding";
        /// <summary>Realistic Trips' assembly (bit 2): the csproj sets no AssemblyName, so this is the project name.</summary>
        public const string T2wAssembly = "Time2Work";
        public const string T2wName = "Realistic Trips";
        public const string LookedFor = "looked for assemblies RealisticPathFinding, Time2Work";
        /// <summary>Hub label when a trip system is off and no known swap is loaded. It mirrors bindings.ts TRIP_PLANNER_UNKNOWN
        /// (tools/test-hub-rules.js tests the TS literal).</summary>
        public const string UnknownLabel = "?";

        private const int PlannerRpf = 1, PlannerT2w = 2;

        /// <summary>The planner bit of an assembly name (exact, case-sensitive match): 1 RealisticPathFinding, 2 Time2Work, else 0.</summary>
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

        /// <summary>The known planners of a mask in bit order, joined ", " (with the assembly name if asked). A mask of 0 gives "".</summary>
        public static string Names(int planners, bool withAssembly)
        {
            string s = "";
            if ((planners & PlannerRpf) != 0)
                s = withAssembly ? RpfName + " (assembly " + RpfAssembly + ")" : RpfName;
            if ((planners & PlannerT2w) != 0)
                s += (s.Length > 0 ? ", " : "") + (withAssembly ? T2wName + " (assembly " + T2wAssembly + ")" : T2wName);
            return s;
        }

        /// <summary>The game system of one trip bit. Anything else gives "?".</summary>
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

        /// <summary>Every trip system in bit order as "name on", "name OFF" (found, switched off) or "name missing".</summary>
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

        /// <summary>"all on", else "OFF: " plus every system not found and on, then ", replaced by" the known planners or ", no known replacement".</summary>
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

        /// <summary>The trip-systems line of a Game load (INFO when all on, WARN otherwise).</summary>
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

        /// <summary>A change of the trip systems during play (WARN, at most Tuning.TripChangeLines per load).
        /// <paramref name="last"/> marks the last one written.</summary>
        public static string ChangeLine(int oldFound, int oldOn, int found, int on, int planners, bool disarms, bool last)
        {
            return "SkiDays: trip systems changed during play: " + Long(found, on) + " (was: " + Short(oldFound, oldOn, planners) + "); " +
                   (disarms
                       ? "ski days need the game's own LeisureSystem and CitizenBehaviorSystem: NOT ARMED from now on this load (claims " +
                         "in flight finish as vanilla leisure trips, as at a load)"
                       : "the arm verdict stands until the next city load") +
                   (last ? " (further changes this load are only counted in the Dump)" : "");
        }

        /// <summary>The hub label (CityStats.tripPlanner): "" when all on, else the known planners' names or UnknownLabel.</summary>
        public static string Label(int found, int on, int planners)
        {
            if (AllOn(found, on))
                return "";
            string names = Names(planners, false);
            return names.Length > 0 ? names : UnknownLabel;
        }

        /// <summary>True when ski days cannot arm (ArmCheck's SystemOff test): LeisureSystem or CitizenBehaviorSystem is off.</summary>
        public static bool BlocksSkiDays(int on) => (on & SkiDayRules.TripNeededToArm) != SkiDayRules.TripNeededToArm;

        private static bool AllOn(int found, int on)
            => (found & SkiDayRules.TripAll) == SkiDayRules.TripAll && (on & SkiDayRules.TripAll) == SkiDayRules.TripAll;
    }
}
