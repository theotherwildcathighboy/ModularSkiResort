// SkiOtherMods — which other mods are loaded, for hub LABELS only (Round E1, D34; docs/research/
// econ-round/spec/ch4-money.md §5.9, §6.5): Realistic Workplaces And Households (assembly "RWH", RWH.csproj
// <AssemblyName>, measurable-stats.md V5) and Meapy's Tourism Overhaul (Mod's existing scan,
// Mod.DetectTourismOverhaul). Assembly NAMES only (AppDomain, GetName().Name) — no type of another mod is
// touched (rule 1). No behavior depends on it: it only switches hub notes (CityStats.RealisticWorkplaces,
// ch5 §5.4).
// Round M2 (D15 under R9, 2026-10-10, builder B3; docs/research/round-m2/decisions.md R9, build-contract.md §5.11,
// §6.11): also the known trip-planner swaps, by assembly name (SkiTripSystems.BitOfAssembly: RealisticPathFinding =
// Realistic PathFinding, Time2Work = Realistic Trips); TripPlannerMask = the OR of their bits over EVERY loaded
// assembly (the RWH `break` is gone so one walk sees every name). Labels and log only: SkiDaySystem names the mask
// in its trip-systems lines and the hub label; the ski-day arm verdict reads the game's own systems' Enabled flags,
// never this mask (DECISIONS 150).
//
// A STATIC CLASS, not a system (e1-spec I-21): no update interval, no "created" line. Refresh() is
// called from SkiGuestCounterSystem.OnGameLoadingComplete (every mod assembly is loaded by then — the same
// reason Mod.RefreshTourismOverhaulDetection runs at load, Mod.cs; it runs before SkiDaySystem's load handler,
// which reads TripPlannerMask: the counter is created first in Mod.cs, so its handler is subscribed first) and logs
// `Other mods: Realistic Workplaces And Households detected: {True|False} (assembly RWH; labels only)` and
// `Other mods: trip-planner mods detected: {names or none} (looked for assemblies RealisticPathFinding, Time2Work;
// labels only)` — each on the first scan of the session and whenever its own value changed after it (silence
// impossible, no repeat per load); never throws (a failed scan is one WARN and keeps the last values). Main thread;
// one walk of the loaded assemblies per load.
//
// Round E1 (2026-10-04): stage 0 (S0) laid down the shell; stream S4 wrote the scan.

using System;

namespace SkiResortMod.Systems
{
    internal static class SkiOtherMods
    {
        public const string RealisticWorkplacesAssembly = "RWH";

        /// <summary>True when an assembly named <see cref="RealisticWorkplacesAssembly"/> is loaded
        /// (last Refresh). Labels only.</summary>
        public static bool RealisticWorkplacesDetected { get; private set; }

        public static bool TourismOverhaulDetected => Mod.TourismOverhaulDetected;

        /// <summary>Round M2 (R9): the known trip-planner mods loaded at the last Refresh, as SkiTripSystems planner
        /// bits (1 Realistic PathFinding, 2 Realistic Trips; 0 = none). Labels and log only.</summary>
        public static int TripPlannerMask { get; private set; }

        private static bool s_Scanned;   // the first scan of the session logs both lines

        /// <summary>Scans AppDomain assemblies by GetName().Name only (no types touched); each line logs on the first
        /// scan of the session and on its own change. Never throws.</summary>
        public static void Refresh()
        {
            try
            {
                bool found = false;
                int planners = 0;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name;
                    try
                    {
                        name = assembly.GetName().Name;
                    }
                    catch (Exception)
                    {
                        continue;   // an assembly that cannot report its name is not one we look for
                    }
                    if (name == RealisticWorkplacesAssembly)
                        found = true;
                    planners |= SkiTripSystems.BitOfAssembly(name);   // Round M2: every assembly, no break
                }
                bool first = !s_Scanned;
                s_Scanned = true;
                if (first || found != RealisticWorkplacesDetected)
                {
                    RealisticWorkplacesDetected = found;
                    Mod.Log.Info($"Other mods: Realistic Workplaces And Households detected: {found} " +
                                 $"(assembly {RealisticWorkplacesAssembly}; labels only)");
                }
                if (first || planners != TripPlannerMask)
                {
                    TripPlannerMask = planners;
                    string names = SkiTripSystems.Names(planners, true);
                    Mod.Log.Info($"Other mods: trip-planner mods detected: {(names.Length > 0 ? names : "none")} " +
                                 $"({SkiTripSystems.LookedFor}; labels only)");
                }
            }
            catch (Exception e)
            {
                string stay = SkiTripSystems.Names(TripPlannerMask, false);
                Mod.Log.Warn($"Other mods: the assembly scan failed ({e.GetType().Name}: {e.Message}) — " +
                             $"Realistic Workplaces And Households detected stays {RealisticWorkplacesDetected}; " +
                             $"trip-planner mods stay {(stay.Length > 0 ? stay : "none")} (labels only; non-fatal)");
            }
        }
    }
}
