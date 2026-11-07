// The one cheap "is there a resort of ours in this city" bit: any Ski Area Welcome Sign, run area or lift.
// Systems that must cost ~0 in a city without a resort read Present first and return before any query, sync or allocation.
// Writers: SkiRunOwnershipSystem (signs and runs, also Reset() at load, and MarkUnknown() when it is disabled by an exception, so Present
// fails open) and LiftCensusSystem (lifts, after every BuildCensus). Main thread, plain static fields.
// Order: ownership's load handler runs Reset() before it counts, and the census sets its count at its first build after load, so Lifts is 0
// for the first frames. Readers must re-test every pass, never latch "absent".
// Log: one "Resort [present]" line on the first refresh after a load and whenever the bit changes. EverSeen is what the daily
// INFO lines wait for.

namespace SkiResortMod.Systems
{
    internal static class ResortPresence
    {
        public static int Signs { get; private set; }
        public static int Runs { get; private set; }
        public static int Lifts { get; private set; }

        /// <summary>True after the ownership sweep failed: callers behave as before this bit existed.</summary>
        public static bool Unknown { get; private set; }

        /// <summary>Anything of ours seen since this load (the daily INFO lines stay silent until true).</summary>
        public static bool EverSeen { get; private set; }

        /// <summary>The bit. Fails open when the writers are unreliable.</summary>
        public static bool Present => Unknown || Signs > 0 || Runs > 0 || Lifts > 0;

        private static int s_Logged = -1;   // -1 nothing logged this load, 0 "no", 1 "yes"

        /// <summary>Per-load reset (SkiRunOwnershipSystem.OnGameLoadingComplete).</summary>
        public static void Reset()
        {
            Signs = Runs = Lifts = 0;
            Unknown = false;
            EverSeen = false;
            s_Logged = -1;
        }

        public static void SetSignsRuns(int signs, int runs)
        {
            Signs = signs;
            Runs = runs;
            Refresh();
        }

        public static void SetLifts(int lifts)
        {
            Lifts = lifts;
            Refresh();
        }

        /// <summary>The ownership sweep is disabled: fail open (Present true), logged.</summary>
        public static void MarkUnknown()
        {
            Unknown = true;
            EverSeen = true;
            Refresh();
        }

        private static void Refresh()
        {
            if (Signs > 0 || Runs > 0 || Lifts > 0)
                EverSeen = true;
            int now = Present ? 1 : 0;
            if (now == s_Logged)
                return;
            s_Logged = now;
            Mod.Log.Info($"Resort [present] {(now == 1 ? "yes" : "no")} (signs {Signs}, runs {Runs}, lifts {Lifts}" +
                         (Unknown ? "; ownership sweep disabled, bit held open" : "") + ")");
        }
    }
}
