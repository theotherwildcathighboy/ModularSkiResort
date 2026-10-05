// SkiOtherMods — which other mods are loaded, for hub LABELS only (Round E1, D34; docs/research/
// econ-round/spec/ch4-money.md §5.9, §6.5): Realistic Workplaces And Households (assembly "RWH", RWH.csproj
// <AssemblyName>, measurable-stats.md V5) and Meapy's Tourism Overhaul (Mod's existing scan,
// Mod.DetectTourismOverhaul). Assembly NAMES only (AppDomain, GetName().Name) — no type of another mod is
// touched (rule 1). No behaviour depends on it: it only switches hub notes (CityStats.RealisticWorkplaces,
// ch5 §5.4).
//
// A STATIC CLASS, not a system (e1-spec I-21): no update interval, no "created" line. Refresh() is
// called from SkiGuestCounterSystem.OnGameLoadingComplete (every mod assembly is loaded by then — the same
// reason Mod.RefreshTourismOverhaulDetection runs at load, Mod.cs) and logs
// `Other mods: Realistic Workplaces And Households detected: {True|False} (assembly RWH; labels only)` on
// the first scan of the session and on every change after it (silence impossible, no repeat per load);
// never throws (a failed scan is one WARN and keeps the last value). Main thread; one walk of the loaded
// assemblies per load.
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

        private static bool s_Scanned;

        /// <summary>Scans AppDomain assemblies by GetName().Name only (no types touched); logs on change. Never throws.</summary>
        public static void Refresh()
        {
            try
            {
                bool found = false;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name;
                    try
                    {
                        name = assembly.GetName().Name;
                    }
                    catch (Exception)
                    {
                        continue;   // an assembly that cannot report its name is not the one we look for
                    }
                    if (name == RealisticWorkplacesAssembly)
                    {
                        found = true;
                        break;
                    }
                }
                if (s_Scanned && found == RealisticWorkplacesDetected)
                    return;
                s_Scanned = true;
                RealisticWorkplacesDetected = found;
                Mod.Log.Info($"Other mods: Realistic Workplaces And Households detected: {found} " +
                             $"(assembly {RealisticWorkplacesAssembly}; labels only)");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Other mods: the assembly scan failed ({e.GetType().Name}: {e.Message}) — " +
                             $"Realistic Workplaces And Households detected stays {RealisticWorkplacesDetected} (labels only; non-fatal)");
            }
        }
    }
}
