// Modular Ski Resort — code mod entry point.
//
// The mod is a hybrid (asset pack + this DLL). The DLL owns the *dynamic* behaviour only:
// slope classification, snow gating and size-scaled attractiveness (SPEC §6/§7).
// Architectural rule (SPEC §4.1): no Harmony, no reflection into other mods. Everything is
// done with our own ECS systems registered through the game's own UpdateSystem extension point.

using System;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using SkiResortMod.Settings;
using SkiResortMod.Systems;

namespace SkiResortMod
{
    public sealed class Mod : IMod
    {
        /// <summary>
        /// Mod-wide logger. Writes to %CSII_USERDATAPATH%\Logs\SkiResortMod.Mod.log.
        /// SetShowsErrorsInUI(false) keeps our errors out of the player-facing error dialog;
        /// flip to true while debugging if you want failures to be loud in-game.
        /// </summary>
        public static readonly ILog Log =
            LogManager.GetLogger($"{nameof(SkiResortMod)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        /// <summary>Live settings instance; null until OnLoad has run.</summary>
        public static SkiResortSettings Settings { get; private set; }

        /// <summary>True when Meapy's CS2 Tourism Overhaul is loaded alongside us (SPEC §9 rule 3:
        /// detect and log only — no reflection into its types, no behaviour change).</summary>
        public static bool TourismOverhaulDetected { get; private set; }

        /// <summary>Directory containing the deployed mod DLL (and its Textures\ payload);
        /// null when TryGetExecutableAsset failed. Consumed by SkiRunSplatSystem.</summary>
        public static string ModRootPath { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                Log.Info($"Current mod asset at {asset.path}");
                ModRootPath = Path.GetDirectoryName(asset.path);
            }

            // --- Settings + options UI + localization -------------------------------------
            Settings = new SkiResortSettings(this);
            Settings.RegisterInOptionsUI();
            // Two en-US sources: options-UI strings (Settings\LocaleEN.cs, qualified because
            // the asset-name LocaleEN below shadows it inside this namespace) and the asset
            // display names/descriptions (LocaleEN.cs). The manager merges same-locale sources.
            GameManager.instance.localizationManager.AddSource(
                "en-US", new SkiResortMod.Settings.LocaleEN(Settings));
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN());
            // Loads persisted values over the defaults; the third argument supplies defaults
            // for first run. File lands under ModsSettings\SkiResortMod\ (see [FileLocation]).
            AssetDatabase.global.LoadSettings(nameof(SkiResortMod), Settings, new SkiResortSettings(this));

            // --- Tourism Overhaul detection (SPEC §9 rule 3) ------------------------------
            TourismOverhaulDetected = DetectTourismOverhaul();
            Log.Info($"Tourism Overhaul detected: {TourismOverhaulDetected}");

            // --- System registration ------------------------------------------------------
            // SkiResortAttractivenessSystem MUST be anchored after the vanilla AttractionSystem:
            // AttractionSystem unconditionally rewrites AttractivenessProvider.m_Attractiveness
            // for EVERY building carrying the component, on a 16-frame cycle (1/16 of buildings
            // per tick, sliced by the UpdateFrame shared component). Writing "once per in-game
            // hour" as first drafted would therefore be clobbered within 16 frames. Instead we
            // mirror vanilla's cadence (interval 16, inherited offset) and use
            // UpdateAfter<TSystem, TAnchor> so, each tick, our write lands after vanilla's for
            // the same UpdateFrame slice and simply wins. Verified against decompiled
            // Game.Simulation.AttractionSystem and Game.UpdateSystem (see
            // docs/research/code-mod.md in the project repo).
            updateSystem.UpdateAfter<SkiResortAttractivenessSystem, Game.Simulation.AttractionSystem>(
                SystemUpdatePhase.GameSimulation);

            // Snow-line / climate gating. Stub for now: it logs the climate constants we need
            // to validate the SPEC §6.6 snow formula (Phase 2 gate) and does no per-tick work.
            updateSystem.UpdateAt<SnowLineSystem>(SystemUpdatePhase.GameSimulation);

            // Chair pinning (milestone 1: static). Phase choice is load-bearing:
            // SubObjectSystem spawns/moves chairs at Modification2B, LaneSystem finalizes the
            // cable-lane curves at Modification4 (ModificationBarrier4) — at Modification4B both
            // exist in their final state, and our Transform write lands before
            // Game.Objects.SearchSystem (Modification5) and PreCulling consume it.
            updateSystem.UpdateAt<ChairPinSystem>(SystemUpdatePhase.Modification4B);

            // Power bridging: our stations' owned 3 m connection stub fails vanilla
            // ElectricityBuildingGraphSystem's orphan gate, so that system never registers the
            // station's electricity connection (and actively deletes it on updates).
            // PowerBridgeSystem bootstraps ElectricityValveConnection onto the stub nodes so
            // the gate's OTHER arm passes; it must run BEFORE the vanilla system in the same
            // phase (Modification4B) so the valve is visible the very frame a station is
            // created/updated — anchored registration guarantees that ordering.
            updateSystem.UpdateBefore<PowerBridgeSystem, Game.Simulation.ElectricityBuildingGraphSystem>(
                SystemUpdatePhase.Modification4B);

            // Line-first reconnection: placing a station onto an existing lift line's end
            // never re-triggers lane building (LaneSystem only processes Updated owners), so
            // cables stayed undrawn. LiftReconnectSystem marks the connection stub's net
            // entities Updated on station events. Phase choice is load-bearing: the SubNet
            // buffer is populated by SubNetReferencesSystem at Modification4, and LaneSystem
            // consumes the tags later in that same phase — so we anchor directly before
            // LaneSystem inside Modification4.
            updateSystem.UpdateBefore<LiftReconnectSystem, Game.Net.LaneSystem>(
                SystemUpdatePhase.Modification4);

            // Cursor aim plane: NetToolSystem reads the drawn prefab's
            // NetGeometryData.m_DefaultSurfaceHeight.max as the ray offset; the prefab-side
            // value is hard-coupled to center(HeightRange) = 13 (terrain-carving.md §5).
            // CursorAimSystem rewrites the prefab field to the tower head once per load —
            // data-only, no per-frame work (registered like SnowLineSystem: disabled,
            // OnGameLoadingComplete only).
            updateSystem.UpdateAt<CursorAimSystem>(SystemUpdatePhase.GameSimulation);

            // Ski-run splat painting: mirrors run polygons into the terrain-material user
            // splatmap (docs/research/terrain-material-runs.md — "polygons are data, splat
            // is paint"). Phase choice is load-bearing: PreCulling runs every frame in Game
            // AND Editor, paused included — area edits arrive through tool phases that
            // ignore simulation pause, and the Created/Updated/Deleted tags live until
            // CleanUpSystem (Cleanup phase), so a per-frame phase reliably sees every
            // event. Anchored before the vanilla TerrainMaterialSystem so our ApplyBrush
            // dabs execute before the same frame's splat cascade update composes them.
            updateSystem.UpdateBefore<SkiRunSplatSystem, Game.Rendering.TerrainMaterialSystem>(
                SystemUpdatePhase.PreCulling);

            // Snow injection (run-rendering Layer 1): tops up run-shaped snow in SnowSystem's
            // own depth buffer. Phase choice is load-bearing: GameSimulation is SnowSystem's
            // own phase — it only ticks while the sim advances and only in Game action mode,
            // so pause-gating and editor-inertness come from the phase itself. The anchor is
            // load-bearing too: the write must land after the frame's ping-pong flip so
            // injected texels are consumed as _Previous by the NEXT AddSnow
            // (docs/research/snow-injection.md §2); anchored registration inherits
            // SnowSystem's 4-frame offset slot, and the system self-gates its 64-frame
            // top-up cadence off SimulationSystem.frameIndex.
            updateSystem.UpdateAfter<SkiSnowInjectionSystem, Game.Simulation.SnowSystem>(
                SystemUpdatePhase.GameSimulation);

            // Ski-run decal upgrade (run-rendering Layer 2): probes and mutates the LIVE
            // "Area batch (<run surface>)" material clones — the new alpha-coverage decal
            // texture pairs, the shader's built-in snow slots (winter look rides the game's
            // snow sim), conservative doc-safe scalars. Anchored AFTER the vanilla
            // AreaBatchSystem in PreCulling so the frame a batch clone is lazily created,
            // our find-retry can already see it; PreCulling runs every frame in Game AND
            // Editor, paused included, so the retry countdown and re-assert sentinel tick.
            updateSystem.UpdateAfter<SkiRunDecalSystem, Game.Rendering.AreaBatchSystem>(
                SystemUpdatePhase.PreCulling);
        }

        public void OnDispose()
        {
            Log.Info(nameof(OnDispose));
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }

        /// <summary>
        /// Re-runs the Tourism Overhaul scan after all mods have loaded. The OnLoad-time
        /// scan races mod load order — TourismOverhaul.dll is verifiably present in the pdx
        /// mods cache while OnLoad logged "detected: False" — because assemblies of mods
        /// loaded after ours are not yet in the AppDomain when our OnLoad runs. Called from
        /// SkiResortAttractivenessSystem.OnGameLoadingComplete, by which point every mod
        /// assembly is loaded.
        /// </summary>
        public static void RefreshTourismOverhaulDetection()
        {
            bool detected = DetectTourismOverhaul();
            if (detected != TourismOverhaulDetected)
            {
                TourismOverhaulDetected = detected;
                Log.Info($"Tourism Overhaul detected (post-load rescan): {detected}");
            }
        }

        /// <summary>
        /// Scans loaded assemblies for Tourism Overhaul by name only. Deliberately does not
        /// touch its types: no hard dependency, no assumption it is present (SPEC §9 rule 3).
        /// </summary>
        private static bool DetectTourismOverhaul()
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == "TourismOverhaul")
                        return true;
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Tourism Overhaul detection failed (non-fatal): {e.Message}");
            }
            return false;
        }
    }
}
