// Which other mods are loaded, for hub labels and log lines only. Detection is by assembly name (AppDomain, GetName().Name); no
// type of another mod is touched and no behavior depends on it. Detects Realistic Workplaces And Households (assembly "RWH"), Anarchy (assembly "Anarchy"),
// Meapy's Tourism Overhaul (Mod.DetectTourismOverhaul) and the known trip-planner swaps (SkiTripSystems.BitOfAssembly);
// TripPlannerMask ORs the bits over every loaded assembly, so there is no early break.
// Static class, not a system: Refresh() runs from SkiGuestCounterSystem.OnGameLoadingComplete, when every mod assembly is loaded
// and before SkiDaySystem's load handler (reads TripPlannerMask), because the counter is created first in Mod.cs. Each of its two
// log lines is written on the first scan and whenever its value changes; never throws (a failed scan is one WARN). Main thread.

using System;

namespace SkiResortMod.Systems
{
    internal static class SkiOtherMods
    {
        public const string RealisticWorkplacesAssembly = "RWH";
        public const string AnarchyAssembly = "Anarchy";

        /// <summary>True when an assembly named <see cref="RealisticWorkplacesAssembly"/> is loaded
        /// (last Refresh). Labels only.</summary>
        public static bool RealisticWorkplacesDetected { get; private set; }

        /// <summary>True when an assembly named <see cref="AnarchyAssembly"/> is loaded (last Refresh). Labels only: whether its toggle is on
        /// cannot be read without touching its types.</summary>
        public static bool AnarchyDetected { get; private set; }

        public static bool TourismOverhaulDetected => Mod.TourismOverhaulDetected;

        /// <summary>Known trip-planner mods loaded at the last Refresh, as SkiTripSystems planner bits (1 Realistic
        /// PathFinding, 2 Realistic Trips; 0 = none).</summary>
        public static int TripPlannerMask { get; private set; }

        private static bool s_Scanned;

        /// <summary>Scans AppDomain assemblies by name only. Never throws.</summary>
        public static void Refresh()
        {
            try
            {
                bool found = false, anarchy = false;
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
                        continue;
                    }
                    if (name == RealisticWorkplacesAssembly)
                        found = true;
                    if (name == AnarchyAssembly)
                        anarchy = true;
                    planners |= SkiTripSystems.BitOfAssembly(name);
                }
                bool first = !s_Scanned;
                s_Scanned = true;
                if (first || anarchy != AnarchyDetected)
                {
                    AnarchyDetected = anarchy;
                    Mod.Log.Info($"Other mods: Anarchy detected: {anarchy} (assembly {AnarchyAssembly}; labels only)");
                }
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
