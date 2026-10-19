// ResortPresence — the one cheap "is there a resort of ours in this city" bit (code review 2026-10-19,
// DECISIONS 314 architecture ruling). Present := any Ski Area Welcome Sign, any run area of ours, or any
// lift of ours exists. Systems whose cost must be ~0 in a city without a resort read Present FIRST and
// return before any query, sync or allocation; "costs ~0 when unused" is then checkable with the one log
// line below instead of by profiling.
//
// WHO WRITES (the two systems that already count these things; nothing new is walked for the bit):
//   * SkiRunOwnershipSystem — signs (m_Gates.Count) and runs (m_Plan.Count) from its load pass and every
//     sweep (SweepNow on a drawn run / placed sign keeps this immediate; the 64-frame sweep catches the
//     rest). Also resets the whole record at its OnGameLoadingComplete and calls MarkUnknown() when it is
//     disabled by an exception (the bit then FAILS OPEN: Present stays true so no system goes dark).
//   * LiftCensusSystem — lifts (census rows) after every BuildCensus.
// WHO READS: SkiSnowInjectionSystem, SkiRunPropSystem, SkiNightLightSystem, SkiRunOwnershipSystem (sweep
// gating), TramShuttleSystem, SkiResortAttractivenessWatchSystem, SkiSnowGunPlumeSystem, SkiLiftChargeSystem,
// the daily INFO lines. All main thread, plain static fields (no Entity is kept: nothing to remap at a load).
//
// ORDER: ownership's load handler runs Reset() BEFORE it counts; the census sets its count at its first
// build (the first update after a load), so Lifts is 0 for the first frames of a load: a reader that must not
// miss a lifts-only city re-tests every pass (all of them do; none latches "absent").
//
// LOG (silence impossible): `Resort [present] no|yes (signs n, runs n, lifts n)` — the first refresh after a
// load, and again whenever the bit changes. EverSeen (anything of ours seen since this load) is what the
// daily INFO lines wait for (D6).

namespace SkiResortMod.Systems
{
    internal static class ResortPresence
    {
        public static int Signs { get; private set; }
        public static int Runs { get; private set; }
        public static int Lifts { get; private set; }

        /// <summary>True after the ownership sweep failed: callers behave as before this bit existed.</summary>
        public static bool Unknown { get; private set; }

        /// <summary>Anything of ours seen since this load (D6: the daily INFO lines stay silent until true).</summary>
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
