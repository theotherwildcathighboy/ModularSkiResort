// Code mod entry point. The DLL owns the dynamic behavior only (slope classification, snow gating, size-scaled attractiveness). No Harmony
// and no reflection into other mods. The mod uses its own ECS systems only. It registers them through the game's UpdateSystem extension
// point.
// Engine notes: EngineNotes/Mod.md

using System;
using System.IO;
using System.Text.RegularExpressions;
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
        /// Mod-wide logger (%CSII_USERDATAPATH%/Logs/SkiResortMod.Mod.log). SetShowsErrorsInUI(false) keeps our errors out of the
        /// player-facing error dialog. Flip it to true while debugging.
        /// </summary>
        public static readonly ILog Log =
            LogManager.GetLogger($"{nameof(SkiResortMod)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        /// <summary>Live settings instance. It is null until OnLoad has run.</summary>
        public static SkiResortSettings Settings { get; private set; }

        /// <summary>True when Meapy's CS2 Tourism Overhaul is loaded alongside us. We detect it by assembly name and only log it. We use no
        /// reflection into its types and change no behavior.</summary>
        public static bool TourismOverhaulDetected { get; private set; }

        /// <summary>Directory containing the deployed mod DLL (and its Textures payload). It is null when TryGetExecutableAsset failed.
        /// SkiRunDecalSystem and LiftLineIconAsset consume it.</summary>
        public static string ModRootPath { get; private set; }

        // The profile folder up to and including the account name. The name may hold spaces, so the match runs to the next separator.
        private static readonly Regex UserProfileFolder = new Regex(@"(?<![A-Za-z0-9_])(?:[A-Za-z]:)?[\\/](?:Users|home)[\\/][^\\/'""]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The text with the user-profile folder replaced by %USERPROFILE%. Players paste logs into bug reports, and the account
        /// name must not travel with them. Every log line that prints a path or an IO exception message goes through this.</summary>
        internal static string ScrubUserPath(string text) =>
            string.IsNullOrEmpty(text) ? text : UserProfileFolder.Replace(text, "%USERPROFILE%");

        /// <summary>True once OnDispose has started. The quit disposes mods before the world (GameManager.cs:792-793; 1.6.2 :802-803), so a
        /// system's OnDestroy can run after OnDispose. SkiResortStatsSystem reads this to skip its quiet summary.</summary>
        internal static bool Disposed { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Disposed = false;
            Log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                Log.Info($"Current mod asset at {ScrubUserPath(asset.path)}");
                ModRootPath = Path.GetDirectoryName(asset.path);
            }

            // The game registers every *.mjs in the mod folder as a UI module with no validity check (DefaultAssetFactory.cs:257;
            // ModManager.cs:461-483, 1.6.2 :464-486). The only C#-side evidence of the hub's UI is therefore the file beside this DLL.
            // ModRootPath is null when TryGetExecutableAsset failed. The code guards it and never passes it to Path.Combine.
            LogUiModulePresence();

            Settings = new SkiResortSettings(this);
            // RegisterKeyBindings builds our input map (named Settings.id) and calls InputManager.AddActions (ModSetting.cs:141-231, :210).
            // Until it runs, GetAction returns null (:289-292) and SkiGladeHotkeySystem stays inert. It uses the live instance only: the
            // defaults instance passed to LoadSettings below must not register. It runs before LoadSettings on purpose: the [AfterDecode]
            // ApplyKeyBindings hook (:269-277) then pushes the player's saved rebinds into the registered actions (the reverse order also
            // works). A guard makes a keybinding failure cost only the hotkeys, never the rest of OnLoad.
            try
            {
                Settings.RegisterKeyBindings();
            }
            catch (Exception e)
            {
                Log.Error($"!!! RegisterKeyBindings failed — glade hotkeys inert this session " +
                          $"(the 'Ski Glades' developer tab still works):\n{e}");
            }
            Settings.RegisterInOptionsUI();
            // Two en-US sources. The first holds the options-UI strings (Settings/LocaleEN.cs). The code qualifies its name because the
            // asset-name LocaleEN below shadows it inside this namespace. The second holds the asset display names/descriptions
            // (LocaleEN.cs). The manager merges same-locale sources.
            GameManager.instance.localizationManager.AddSource(
                "en-US", new SkiResortMod.Settings.LocaleEN(Settings));
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN());
            // Loads persisted values over the defaults (the third argument supplies first-run defaults). The file is under
            // ModsSettings/SkiResortMod (see [FileLocation]). The defaults instance's constructor overwrites ModSetting.instances[id]
            // (ModSetting.cs:42). Only the input-conflict notification reads that map, for the mod's display name/thumbnail
            // (InputManager.cs:1523-1530), so it is benign.
            AssetDatabase.global.LoadSettings(nameof(SkiResortMod), Settings, new SkiResortSettings(this));

            TourismOverhaulDetected = DetectTourismOverhaul();
            // "False" here is only the early check. Mods that load after ours are not visible yet.
            Log.Info($"Tourism Overhaul detected: {TourismOverhaulDetected} (early check at mod load: mods that load after " +
                     "ours are not visible yet; checked again at each city load)");

            // After vanilla AttractionSystem (same 16-frame cycle, inherited offset). It rewrites m_Attractiveness on that cycle, so our
            // write must land after it within the tick to win.
            updateSystem.UpdateAfter<SkiResortAttractivenessSystem, Game.Simulation.AttractionSystem>(
                SystemUpdatePhase.GameSimulation);

            // Snow-line / climate oracle: pull-based (Enabled = false, no per-tick work). Consumers pull snow and season gating, the
            // snowmaking margin, the ambient and the cached 24-hour day mean. It logs the climate constants once per load.
            updateSystem.UpdateAt<SnowLineSystem>(SystemUpdatePhase.GameSimulation);

            // Prefab catalog: resolves the SkiCatalog.cs prefab names lazily on the first query after each Game / Editor load. It is
            // disabled and does no per-frame work. Phase and registration order are not load-bearing (load hooks fire for a disabled
            // system, GameSystemBase.cs:27-29).
            updateSystem.UpdateAt<SkiPrefabCatalogSystem>(SystemUpdatePhase.GameSimulation);

            // Chair pinning. Phase is load-bearing. At Modification4B chairs (SubObjectSystem, 2B) and cable lanes (LaneSystem, 4) are
            // final. Our Transform write lands before Game.Objects.SearchSystem (Modification5) and PreCulling.
            updateSystem.UpdateAt<ChairPinSystem>(SystemUpdatePhase.Modification4B);

            // Snow groomers (a prop per bound cat track). Phase is load-bearing: a Deleted tag at 4B still reaches vanilla's Modification5
            // systems before CleanUpSystem. The Transform and CullingInfo write lands before PreCulling, which culls and interpolates from
            // them.
            updateSystem.UpdateAt<SkiGroomerSystem>(SystemUpdatePhase.Modification4B);

            // Power bridging must run BEFORE ElectricityBuildingGraphSystem in the same phase. Then the valve we add to the station stub is
            // visible the frame a station is created/updated (ElectricityBuildingGraphSystem's orphan gate otherwise deletes the connection).
            updateSystem.UpdateBefore<PowerBridgeSystem, Game.Simulation.ElectricityBuildingGraphSystem>(
                SystemUpdatePhase.Modification4B);

            // Line-first reconnection: marks the stub's net entities Updated on station events. It runs directly before LaneSystem inside
            // Modification4 (the SubNet buffer is filled there). LaneSystem consumes the tags later in that phase.
            updateSystem.UpdateBefore<LiftReconnectSystem, Game.Net.LaneSystem>(
                SystemUpdatePhase.Modification4);

            // Mast lean (DECISIONS 600). It runs AFTER Game.Objects.AlignSystem in Modification4. AlignSystem re-derives a node tower's
            // Transform yaw-only on every Updated (AlignSystem.cs:153-160). This also puts it before LaneSystem (SystemOrder.cs:153, :155),
            // which hangs the ropes from the leaned anchors in the same frame. Any other slot shows a plumb frame or ropes on the old
            // anchors.
            updateSystem.UpdateAfter<TowerLeanSystem, Game.Objects.AlignSystem>(SystemUpdatePhase.Modification4);

            // Cursor aim plane: rewrites the prefab's NetGeometryData.m_DefaultSurfaceHeight.max (the NetToolSystem ray offset) once per
            // load. It writes data only, is disabled, and runs in OnGameLoadingComplete only (like SnowLineSystem).
            updateSystem.UpdateAt<CursorAimSystem>(SystemUpdatePhase.GameSimulation);

            // Snow injection: GameSimulation is SnowSystem's phase (pause-gated, Game mode only). Anchor is load-bearing: the write must
            // land after the ping-pong flip so injected texels are _Previous for the NEXT AddSnow. It inherits SnowSystem's 4-frame offset.
            updateSystem.UpdateAfter<SkiSnowInjectionSystem, Game.Simulation.SnowSystem>(
                SystemUpdatePhase.GameSimulation);

            // Vertex-prop bridge: adds the SubObject buffer vanilla's lot-class-gated area-prop spawner needs on OUR run surfaces. Phase is
            // load-bearing twice. It runs directly before SecondaryObjectSystem (it consumes the Updated tag at Modification4B, and Cleanup
            // wipes it). It also writes the prop density and tags ChangedDistrict after a settled slider change. Both must land before SecondaryObjectSystem.
            // Game mode only.
            updateSystem.UpdateBefore<SkiRunPropSystem, Game.Objects.SecondaryObjectSystem>(
                SystemUpdatePhase.Modification4B);

            // Night-light refresh: stamps EffectsUpdated on run-owned props at every day/night flip. Anchor AND interval (2048) are
            // load-bearing: we read the flag EffectFlagSystem just wrote. Offset inheritance requires equal intervals.
            updateSystem.UpdateAfter<SkiNightLightSystem, Game.Effects.EffectFlagSystem>(
                SystemUpdatePhase.GameSimulation);

            // Snow-gun plumes. Phase is load-bearing: GameSimulation lets the tag live into the next frame's PreCulling, where
            // EffectControlSystem consumes it. The anchor after the injection is NOT load-bearing. Game mode only.
            updateSystem.UpdateAfter<SkiSnowGunPlumeSystem, SkiSnowInjectionSystem>(
                SystemUpdatePhase.GameSimulation);

            // Prop snow sync: gives run areas the Game.Objects.Surface weather struct. Anchor AND interval (256) are load-bearing:
            // BatchDataSystem re-reads wetness only when WetnessSystem's LastSystemVersion moves, so we write in its tick.
            updateSystem.UpdateAfter<SkiRunSurfaceSystem, Game.Simulation.WetnessSystem>(
                SystemUpdatePhase.GameSimulation);

            // Run operating costs (snowmaking > groomed > natural = free). In GameSimulation, pause stops the meter and the Editor never
            // bills. It runs after the injection, so a same-frame top-up publishes its thermal mask before the charge pass reads it.
            updateSystem.UpdateAfter<SkiRunUpkeepSystem, SkiSnowInjectionSystem>(
                SystemUpdatePhase.GameSimulation);

            // Guest counter. Anchor is load-bearing for freshness: the interval is the same as MeetingSystem's (16), so the counter
            // inherits its offset (UpdateSystem.cs:400-431) and reads the state MeetingSystem's update just wrote.
            updateSystem.UpdateAfter<SkiGuestCounterSystem, Game.Simulation.MeetingSystem>(SystemUpdatePhase.GameSimulation);
            // Lift tickets: drains the counter's arrivals. The anchor is a mod-to-mod anchor in the same phase (the SkiRunUpkeepSystem →
            // SkiSnowInjectionSystem precedent). Not load-bearing (drain semantics).
            updateSystem.UpdateAfter<SkiLiftTicketSystem, SkiGuestCounterSystem>(SystemUpdatePhase.GameSimulation);
            // Hub history. Anchor AND interval (16) are load-bearing: it runs in the counter's frame right after AdvanceMonth. The first
            // tick after a load therefore seeds the ledger before anything mirrors over the save. Pause stops it. The Editor never arms it.
            updateSystem.UpdateAfter<SkiResortHistorySystem, SkiGuestCounterSystem>(SystemUpdatePhase.GameSimulation);
            // Lift running charge: the run-upkeep pattern, next to it in the frame and the log. In GameSimulation, pause stops the meter
            // and the Editor never bills.
            updateSystem.UpdateAfter<SkiLiftChargeSystem, SkiRunUpkeepSystem>(SystemUpdatePhase.GameSimulation);
            // The bus depots' coach bill: the lift charge's pattern, next to it in the frame. Pause stops it. The Editor never bills.
            updateSystem.UpdateAfter<SkiBusChargeSystem, SkiLiftChargeSystem>(SystemUpdatePhase.GameSimulation);
            // Merged buildings (SkiMergedLodge): the monthly fee, the duplicate guard, tier lowering. The actions come from the hub. Pause
            // stops the meter. The Editor never arms it. Not anchored.
            updateSystem.UpdateAt<SkiMergeSystem>(SystemUpdatePhase.GameSimulation);
            // The depots' visual coach. Anchor AND interval (16) are load-bearing: it inherits the AI's offset and runs right before it,
            // after navigation. A turnaround therefore clears EndOfPath before the AI's delete check of the same frame
            // (EngineNotes/SkiCoachSystem.md). The Editor never arms it.
            updateSystem.UpdateBefore<SkiCoachSystem, Game.Simulation.TransportCarAISystem>(SystemUpdatePhase.GameSimulation);
            // Ski days. Anchor AND interval (64) are load-bearing: it follows LeisureSystem and inherits its offset
            // (UpdateSystem.cs:431-436). Every pass therefore runs on its frames, before TripNeededSystem. NEVER override GetUpdateOffset.
            // ONE registration only. It is registered before SkiResortStatsSystem, so this line creates it (UpdateSystem.cs:178-181). Pause
            // stops it. The Editor never arms it.
            updateSystem.UpdateAfter<SkiDaySystem, Game.Simulation.LeisureSystem>(SystemUpdatePhase.GameSimulation);
            // Homes and hotels near each ski area (hub row 27): a read of the household list. Not anchored.
            updateSystem.UpdateAt<SkiReachCensusSystem>(SystemUpdatePhase.GameSimulation);
            // Lodge parking census. It reads the attraction snapshot's lodge list and the lane search tree (reader handle). Not anchored.
            updateSystem.UpdateAt<SkiLodgeParkingSystem>(SystemUpdatePhase.GameSimulation);

            // Run-to-sign binding: runs carry no vanilla Owner (it lets the bulldozer walk from a run to the sign and delete the resort).
            // The link is our SkiRunBinding. It runs before the injection system, so a bind and the first snow can land in one 64-frame
            // window.
            updateSystem.UpdateBefore<SkiRunOwnershipSystem, SkiSnowInjectionSystem>(
                SystemUpdatePhase.GameSimulation);

            // Ski-run decal upgrade. It runs after AreaBatchSystem in PreCulling (every frame in Game AND Editor, paused included). A batch
            // clone created this frame is therefore already visible to our find-retry.
            updateSystem.UpdateAfter<SkiRunDecalSystem, Game.Rendering.AreaBatchSystem>(
                SystemUpdatePhase.PreCulling);

            // Glade tool: registration order between these four is not load-bearing (UpdateAt creates each system immediately,
            // UpdateSystem.cs:158-161). The tool goes first only so its "created" log line precedes its dependents'.

            // ToolUpdate: ToolSystem enables ONLY the active tool (ToolSystem.cs:312-327), so the tool cannot poll its own toggle key.
            updateSystem.UpdateAt<SkiGladeToolSystem>(SystemUpdatePhase.ToolUpdate);

            // Hotkey poller (Ctrl+Shift+G toggle, Shift+G tree density, Alt+G run interior / tree line, Ctrl+Alt+G dump). UIUpdate is
            // load-bearing: every frame, paused or not (SystemOrder.cs:67, UIUpdateSystem.cs:19). The tool's own phase ticks only while
            // active.
            updateSystem.UpdateAt<SkiGladeHotkeySystem>(SystemUpdatePhase.UIUpdate);

            // Member tagging of planted trees. Anchor is load-bearing: the tag must land on the Temp trees BEFORE ApplyObjectsSystem makes
            // them permanent in ApplyTool (it keeps the entity, ApplyObjectsSystem.cs:670-689).
            updateSystem.UpdateBefore<SkiGladeTagSystem, Game.Tools.ApplyObjectsSystem>(
                SystemUpdatePhase.ApplyTool);

            // Member cleanup when a run is deleted (directly or via SkiRunGateCascadeSystem). Modification2 is load-bearing: it runs paused or
            // not and sees the same frame's Deleted tags, after PostTool (ToolSystem.cs:257). No vanilla SubArea cascade reaches a run.
            updateSystem.UpdateAt<SkiGladeCleanupSystem>(SystemUpdatePhase.Modification2);

            // The Groomed toggle's re-emit (DECISIONS 577). Modification1 is load-bearing. The new run and the old one's Deleted tag must
            // stand before these systems of the same frame: SkiGladeCleanupSystem (Modification2, it would delete the retargeted trees
            // otherwise), the placer's cascade, the area geometry and the bind trigger (ModificationEnd). It runs paused too.
            updateSystem.UpdateAt<SkiRunReemitSystem>(SystemUpdatePhase.Modification1);

            // Sign-to-runs cascade: a Deleted Welcome Sign deletes every run bound to it (SkiRunBinding.m_Gate) in the same frame. Phase
            // and anchor are load-bearing: Modification2, UpdateBefore SkiGladeCleanupSystem, so the cascade from sign to runs to member
            // trees completes in one frame. If the anchor moves, the cascade silently never runs.
            updateSystem.UpdateBefore<SkiRunGateCascadeSystem, SkiGladeCleanupSystem>(
                SystemUpdatePhase.Modification2);

            // Options button "Delete all ski resort content". Anchor is load-bearing: PostTool BEFORE SubElementDeleteSystem. There a
            // bulldoze's Deleted stands after ApplyTool (SystemOrder.cs:712-724). Station sub-nets, the electricity graph (Modification1)
            // and the sub-objects therefore cascade the same frame. PostTool runs paused or not (ToolSystem.cs:251-257).
            updateSystem.UpdateBefore<SkiDeleteAllSystem, Game.Objects.SubElementDeleteSystem>(
                SystemUpdatePhase.PostTool);

            // The hub row's cat track delete. Same anchor, same reason. A Deleted tag added later in the frame never reaches
            // Game.Areas.SearchSystem (Modification5). A stale search tree entry crashes a Burst job (the system's header).
            updateSystem.UpdateBefore<SkiCatTrackDeleteSystem, Game.Objects.SubElementDeleteSystem>(
                SystemUpdatePhase.PostTool);

            // A removed station's stub node that only carries a player's power line becomes a plain node of it. The city therefore does not
            // need this mod for that node. Same anchor: its Updated tag has to reach the Modification phases.
            updateSystem.UpdateBefore<SkiStubCleanupSystem, Game.Objects.SubElementDeleteSystem>(
                SystemUpdatePhase.PostTool);

            // Every prefab of this mod open at each Game-mode load. Modification1 is load-bearing: vanilla's Unlock All phase, its Unlock
            // events flush through ModificationBarrier1 (SystemOrder.cs:86, :106). It runs paused or not.
            updateSystem.UpdateAt<SkiUnlockSystem>(SystemUpdatePhase.Modification1);

            // Run binding on draw + drawing feedback: each is gated per frame on its own setting and on the ownership system (trigger and
            // contours: IsArmed, drawing feedback: PrefabSetsReady). Registration order between the four is not load-bearing.

            // Bind trigger. ModificationEnd is load-bearing: MainLoop, paused or not (ModificationSystem.cs:19-26), unlike GameSimulation.
            // A plain UpdateAt sorts after every vanilla Modification system, so the run's geometry is final. The bind raises no tag.
            updateSystem.UpdateAt<SkiRunBindTriggerSystem>(SystemUpdatePhase.ModificationEnd);

            // Trail names: a new piste gets a stored random unused name (SkiRunName, R3 DECISIONS 578). ModificationEnd: MainLoop, paused
            // or not, with the frame's Created tag still on (after SkiRunReemitSystem's Modification1 copy, so a toggled run keeps its
            // pick). It is NOT load-bearing for other systems (the catalog rebuilds on request).
            updateSystem.UpdateAt<SkiRunNameSystem>(SystemUpdatePhase.ModificationEnd);

            // Lodge-score watcher. ModificationEnd is load-bearing: CleanUpSystem strips Created/Updated at Cleanup, BEFORE GameSimulation
            // (SystemOrder.cs:54, 74), so the attractiveness system never sees a placement. This system only sets its pending flag.
            updateSystem.UpdateAt<SkiResortAttractivenessWatchSystem>(SystemUpdatePhase.ModificationEnd);

            // Vanilla HV line joining a lift: refused. Anchor is load-bearing: UpdateBefore ValidationSystem.Components, which runs right
            // after ValidationSystem (SystemOrder.cs:264-265). Therefore we write ValidationSystem's error map after it is filled and
            // before Components turns it into Error components. Anything missing from the map loses its Error
            // (ValidationSystem.cs:450-531). An UpdateAt would sort after Components, and every Error we wrote would be stripped on the
            // next validation.
            updateSystem.UpdateBefore<PowerJoinRefusalSystem, Game.Tools.ValidationSystem.Components>(SystemUpdatePhase.ModificationEnd);
            // A lift station snapped onto a line end facing away from it: refused (FIX-LIST 54(b)). Anchor is load-bearing for the same
            // reason as PowerJoinRefusalSystem's. The error map must be written after ValidationSystem fills it and before Components reads
            // it.
            updateSystem.UpdateBefore<StationFacingRefusalSystem, Game.Tools.ValidationSystem.Components>(SystemUpdatePhase.ModificationEnd);

            // Slope heat-map overlay. Anchor is load-bearing: OverlayRenderSystem consumes and clears the overlay lists each frame
            // (OverlayRenderSystem.cs:781, 790), so our strips must be added BEFORE it in Rendering.
            updateSystem.UpdateBefore<SkiRunDrawFeedbackSystem, Game.Rendering.OverlayRenderSystem>(
                SystemUpdatePhase.Rendering);

            // Grade tooltip. Anchor is load-bearing: TooltipUISystem clears the mouse group each frame (TooltipUISystem.cs:48-55). Running
            // after the vanilla area-tool tooltip keeps our lines with it (a plain UpdateAt sorts after all vanilla UITooltip systems).
            updateSystem.UpdateAfter<SkiRunTooltipSystem, Game.UI.Tooltip.AreaToolTooltipSystem>(
                SystemUpdatePhase.UITooltip);

            // Glade-tool tooltip. Anchor is load-bearing: it runs after TempCostTooltipSystem (SystemOrder.cs:911), so our lines sit
            // directly below its cost line. The reason is the same as for the grade tooltip above.
            updateSystem.UpdateAfter<SkiGladeTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(
                SystemUpdatePhase.UITooltip);

            // Contour lines while drawing. Anchor is load-bearing: UndergroundViewSystem derives contourLinesOn from globalContourLinesOn
            // each frame (UndergroundViewSystem.cs:67, 120), so our write must land BEFORE it. PreCulling runs paused too.
            updateSystem.UpdateBefore<SkiRunContourSystem, Game.Rendering.UndergroundViewSystem>(
                SystemUpdatePhase.PreCulling);

            // Run outline. Before OverlayRenderSystem (same reason as the piste map below) so the Burst job is scheduled before it clears
            // the lists. ONE registration only: an UpdateAt as well would run it twice a frame (UpdateSystem.cs:158-176).
            updateSystem.UpdateBefore<SkiRunOutlineSystem, Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering);

            // Ski Resort hub. All four run at UIUpdate (every frame, paused included, UIUpdateSystem.cs:17-20). The ORDER of these lines is
            // load-bearing (plain UpdateAt runs in add order, UpdateSystem.cs:158-161). Sign names come first. SkiRunBindTriggerSystem
            // queues them, and NameSystem.SetCustomName needs EndFrameBarrier (re-allowed at SystemOrder.cs:62). Then come the catalog, the
            // stats and the hub, in that order. The hub's OnCreate runs here. userInterface.bindings exists by then (GameManager.cs:593,
            // :618).
            updateSystem.UpdateAt<SkiSignNameSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<SkiRunCatalogSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<SkiResortStatsSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<SkiResortHubUISystem>(SystemUpdatePhase.UIUpdate);

            // Lift census: BEFORE ChairPinSystem in Modification4B so its RequestImmediateRebuild("lift topology") is served in the same
            // frame. The anchor must stay at Modification4B or the census silently never runs.
            updateSystem.UpdateBefore<LiftCensusSystem, ChairPinSystem>(SystemUpdatePhase.Modification4B);

            // Tram shuttle mover (moves the two cabins of a tram base station along the track ropes, no save data). AFTER LiftCensusSystem
            // at Modification4B so the census rows are this frame's. The phase must stay Modification4B or the anchor is skipped.
            updateSystem.UpdateAfter<TramShuttleSystem, LiftCensusSystem>(SystemUpdatePhase.Modification4B);

            // Read-only tower diagnostic. AFTER LiftCensusSystem at Modification4B so a line's towers are in the node SubObject buffers.
            // The phase must stay Modification4B or the anchor is skipped and it silently never prints.
            updateSystem.UpdateAfter<LiftTowerDiagSystem, LiftCensusSystem>(SystemUpdatePhase.Modification4B);

            // Per-tower number tiles. AFTER the census at 4B and BEFORE Modification5, where SecondaryObjectReferencesSystem files a
            // Created tile into its tower's SubObject buffer (SystemOrder.cs:206). Writes nothing while the SkiNumTile prefabs are not loaded.
            updateSystem.UpdateAfter<TowerNumberTileSystem, LiftCensusSystem>(SystemUpdatePhase.Modification4B);

            // Sheave train pitch (lift alignment Phase 2). AFTER the census at 4B so the rope lanes LaneSystem built at Modification4 are
            // this frame's. The phase must stay Modification4B or the anchor is skipped. The bone writer runs at Rendering right AFTER
            // InitializeBonesSystem, which (re)allocates skeletons at rest, and before ProceduralUploadSystem uploads them
            // (SystemOrder.cs:653, :689).
            updateSystem.UpdateAfter<TrainPitchSystem, LiftCensusSystem>(SystemUpdatePhase.Modification4B);
            updateSystem.UpdateAfter<TrainBoneSystem, Game.Rendering.InitializeBonesSystem>(SystemUpdatePhase.Rendering);

            // Low spans (hub row LiftLowSpan). AFTER the census at 4B for the same reason as the trains: the rope lanes LaneSystem built at
            // Modification4 are this frame's. The phase must stay Modification4B or the anchor is skipped.
            updateSystem.UpdateAfter<LiftClearanceSystem, LiftCensusSystem>(SystemUpdatePhase.Modification4B);

            // Read-only overlay of the planned towers and rope. Before OverlayRenderSystem (same reason as the piste map below).
            // Gated by the LiftBuildAids setting inside the system.
            updateSystem.UpdateBefore<LiftTowerPlanOverlaySystem, Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering);

            // Tower fit. Phase is load-bearing. Modification1 is the chain's first phase. Therefore every consumer of the Updated tag it
            // writes (GroundHeightSystem, SubObjectSystem, LaneSystem, census, ChairPinSystem...) runs after it in the SAME frame, before
            // Cleanup wipes the tag. A write at 4B would lose it. Gated by the LiftTowerPlanner setting inside.
            updateSystem.UpdateAt<LiftTowerFitSystem>(SystemUpdatePhase.Modification1);

            // Lift-line tool. ToolUpdate is load-bearing: its CreationDefinition + NetCourse entities must exist before PostTool's
            // CourseSplitSystem (CourseSplitSystem.cs:4034) and Modification1/2 of the same frame. The hotkey poller runs at UIUpdate (an
            // inactive tool never ticks). The tooltip runs after the vanilla cost tooltip. Order between the three is not load-bearing.
            updateSystem.UpdateAt<LiftLineToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<LiftLineHotkeySystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<LiftLineTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(SystemUpdatePhase.UITooltip);

            // Cat track grading tool: the lift tool's skeleton and the same three phases for the same reasons. Apply writes the base
            // heightmap on the main thread in ToolUpdate. The hotkey poller also runs the dev-tab probe's follow-ups.
            updateSystem.UpdateAt<CatTrackGradeToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<CatTrackGradeHotkeySystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<CatTrackGradeTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(SystemUpdatePhase.UITooltip);

            // Quick runs tool (experimental): the cat track tool's three phases for the same reasons. ToolUpdate is load-bearing twice. Its
            // run and tree CreationDefinitions must exist before Modification1 of the same frame. Its tree Apply frame must be set before
            // SkiGladeTagSystem reads ApplyingNow in ApplyTool. The hotkey poller also runs the bind line after a build.
            updateSystem.UpdateAt<QuickRunsToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<QuickRunsHotkeySystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<QuickRunsTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(SystemUpdatePhase.UITooltip);

            // Station placer (P0 probe of the Resort Planner round): a scripted ObjectToolBaseSystem subclass. ToolUpdate is load-bearing:
            // its CreationDefinitions must exist before Modification1 of the same frame (GenerateObjectsSystem), and ToolSystem enables only
            // the active tool (ToolSystem.cs:312-327). Its dev trigger runs at UIUpdate (every frame, paused included), queues the call for
            // its own OnUpdate and ticks the placer's follow-up line while the tool is closed.
            updateSystem.UpdateAt<StationPlaceToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<StationPlaceProbeUISystem>(SystemUpdatePhase.UIUpdate);

            // Resort Planner lift side (R1-c): the plan sheet's owner and the lift build queue. UIUpdate is load-bearing: it runs every frame,
            // paused included, whatever tool is active, so the queue steps while the station placer or the lift-line tool is the active
            // tool (an inactive tool never ticks, ToolSystem.cs:312-327). It only queues work for those tools: their own ToolUpdate emits.
            updateSystem.UpdateAt<ResortLiftBuildSystem>(SystemUpdatePhase.UIUpdate);

            // Lift park emitter. EndFrameBarrier.CreateCommandBuffer throws at Modification4B (Game/SafeCommandBufferSystem.cs:16-23), so
            // the {Event, SubObjectsUpdated} entities are created at UIUpdate (after SystemOrder.cs:62). Without this line nothing is parked.
            updateSystem.UpdateAt<LiftParkSystem>(SystemUpdatePhase.UIUpdate);

            // Station alignment guide lines. Before OverlayRenderSystem (same reason as SkiRunDrawFeedbackSystem above).
            // Gated by the LiftBuildAids setting inside the system.
            updateSystem.UpdateBefore<SkiStationAlignSystem, Game.Rendering.OverlayRenderSystem>(
                SystemUpdatePhase.Rendering);

            // The plan sheet's lifts not built yet, while a lift tool is in hand (Resort Planner R1-c). Before OverlayRenderSystem (same
            // reason as SkiStationAlignSystem above). Reads the sheet ResortLiftBuildSystem writes at UIUpdate earlier in the frame.
            updateSystem.UpdateBefore<ResortPlanMarksSystem, Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering);

            // Piste-map overlay. Before OverlayRenderSystem (it completes every registered writer, then clears the lists, so the job must
            // be scheduled first). Reads the hub's state written at UIUpdate earlier in the frame (SystemOrder.cs:67-68).
            updateSystem.UpdateBefore<SkiPisteMapSystem, Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering);

            // Winter snow line while placing. Before OverlayRenderSystem (same reason as the piste map). ONE anchor only (each UpdateBefore
            // call adds an entry, UpdateSystem.cs:173-176). Gated by ShowSnowLineWhilePlacing inside the system.
            updateSystem.UpdateBefore<SkiSnowLineOverlaySystem, Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering);

            // Merge overlay (hub-armed, P2.6). Before OverlayRenderSystem (same reason as the snow line). ONE anchor only. Draws nothing
            // until the hub's armMergeOverlay trigger arms it. Game mode only inside.
            updateSystem.UpdateBefore<SkiMergeOverlaySystem, Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering);

            // Ski area ring (the radius option on the ground). Before OverlayRenderSystem (same reason as the snow line). ONE anchor only.
            // Gated by ShowSkiAreaRadius inside. Game mode only.
            updateSystem.UpdateBefore<SkiAreaRingOverlaySystem, Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering);

            // LV stub marker: where a base station takes power (its own LV stub, never its road). Before OverlayRenderSystem (same reason
            // as the snow line: the renderer uploads and clears its lists in the same frame). ONE anchor only. Gated by LiftBuildAids inside.
            // Game mode only. Writes nothing.
            updateSystem.UpdateBefore<StationLvMarkerSystem, Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering);

            // Run names on the hill: NO anchor to OverlayRenderSystem (labels are Graphics.DrawMesh calls, and nothing is written to the
            // overlay buffers). It must run after the catalog's UIUpdate rebuilds (Rendering does). Gated by ShowRunNamesOnTheHill inside.
            updateSystem.UpdateAt<SkiRunLabelSystem>(SystemUpdatePhase.Rendering);

            // Resort Planner plan labels (lifts and pods, stub until stage A4). Same as the run names: NO anchor to OverlayRenderSystem
            // (Graphics.DrawMesh labels, nothing written to the overlay buffers), plain UpdateAt at Rendering.
            updateSystem.UpdateAt<ResortPlanLabelSystem>(SystemUpdatePhase.Rendering);

            // Snow-line tooltip line. Anchor is load-bearing like SkiRunTooltipSystem's. It runs after the run tooltip, so it follows the
            // run's rows. It reads SkiSnowLineOverlaySystem.Showing from the previous frame's Rendering (one frame of lag).
            updateSystem.UpdateAfter<SkiSnowLineTooltipSystem, SkiRunTooltipSystem>(SystemUpdatePhase.UITooltip);

            // Alignment readout + bulldozer warning. Anchor is load-bearing: after TempCostTooltipSystem (SystemOrder.cs:911), as the
            // glade tooltip. Gated by the LiftBuildAids setting inside the system.
            updateSystem.UpdateAfter<SkiLiftTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(
                SystemUpdatePhase.UITooltip);

            // Power-line join refusal line. Anchor is load-bearing like the lift tooltip's: after TempCostTooltipSystem (SystemOrder.cs:911).
            updateSystem.UpdateAfter<PowerJoinTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(SystemUpdatePhase.UITooltip);
            // Reversed-station refusal line (FIX-LIST 54(b)). Anchor is load-bearing like the power-line join line's.
            updateSystem.UpdateAfter<StationFacingTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(SystemUpdatePhase.UITooltip);

            // Closes the Electricity infoview the lift LINE tool opens. It uses UpdateBefore ONLY (an UpdateAt as well runs it twice per
            // frame, UpdateSystem.cs:158-176). It runs BEFORE InfoviewsUISystem (UIUpdate), so the vanilla binding reflects our close in
            // the same frame.
            updateSystem.UpdateBefore<SkiPlacementInfoviewSystem, Game.UI.InGame.InfoviewsUISystem>(
                SystemUpdatePhase.UIUpdate);

            // Lodge road connection: clears NoRoadConnection and OwnerSide on the lodge PREFAB at every Game-mode load (the state of an
            // upgrade allowed a road, BuildingInitializeSystem.cs:177-188). Disabled, OnGameLoadingComplete only. Phase not load-bearing.
            updateSystem.UpdateAt<LodgeRoadSystem>(SystemUpdatePhase.GameSimulation);

            // Mountain jobs: the roadless hut / summit / patrol buildings' jobs on their linked Welcome Sign's WorkProvider (the
            // ServiceUpgrade runtime path, ServiceUpgradeSystem.cs:137-154). Polls every 64 frames. Phase NOT load-bearing.
            updateSystem.UpdateAt<MountainJobsSystem>(SystemUpdatePhase.GameSimulation);

            // Off-season staff: lodges and the ski school keep half their workplaces while their ski area has had no open run for 12 game
            // hours (OffSeasonRules). Polls every 256 frames. Phase NOT load-bearing (WorkProviderSystem reads m_MaxWorkers whenever it
            // runs).
            updateSystem.UpdateAt<OffSeasonStaffSystem>(SystemUpdatePhase.GameSimulation);

            // Lodge supply: the road-optional lodges' city utilities off on a cat track chain, back at a road (LodgeSupplyRule).
            // Modification1 is load-bearing. Its Updated tag must reach Game.Buildings.InitializeSystem in Modification2 of the same frame
            // (SystemOrder.cs:118, ModificationSystem.cs:19-26) before the cleanup collects it (SystemOrder.cs:50-54,
            // PrepareCleanUpSystem.cs:48-52). ModificationSystem runs in MainLoop (SystemOrder.cs:60), so this runs paused or not.
            updateSystem.UpdateAt<LodgeSupplySystem>(SystemUpdatePhase.Modification1);

            // Lodge numbers: same-tier lodges per ski area get a persisted number (SkiLodgeNumber, LodgeNumberRule) for the hub's car space
            // rows. Passes on a new attraction snapshot and every 256 frames. Phase NOT load-bearing (the hub keys its rows on Revision).
            updateSystem.UpdateAt<LodgeNumberSystem>(SystemUpdatePhase.GameSimulation);

            // Station LV icon: removes the vanilla LowVoltageNotConnected icon from a station whose LV stub has a wire (StationLvRule
            // engine notes). Phase NOT load-bearing: IconCommandSystem consumes the queued removes at ModificationEnd (SystemOrder.cs:271).
            updateSystem.UpdateAt<StationLvIconSystem>(SystemUpdatePhase.GameSimulation);

            // Lift line icon: our "needs a lift line" icon in place of the vanilla high-voltage icon. Anchor is load-bearing: AFTER
            // ConnectionWarningSystem in ModificationEnd (SystemOrder.cs:261) and so before IconCommandSystem (:271). Our IconCommandBuffer
            // is therefore created after ConnectionWarningSystem's buffer in the same frame, so our Remove of the vanilla icon wins at playback
            // (IconCommandSystem.cs:106-160). An UpdateAt would sort after IconCommandSystem: the vanilla icon would show for a frame each
            // time.
            updateSystem.UpdateAfter<LiftLineIconSystem, Game.Net.ConnectionWarningSystem>(SystemUpdatePhase.ModificationEnd);

            // Lift power cues: strips the vanilla transformer / bottleneck / top-station consumer icons. Anchor is load-bearing: AFTER
            // ElectricityStatusSystem in GameSimulation (SystemOrder.cs:448, after DispatchElectricitySystem :444). Our buffer therefore
            // follows both vanilla buffers on their frames (same playback rule as above).
            updateSystem.UpdateAfter<LiftPowerCueSystem, Game.Simulation.ElectricityStatusSystem>(SystemUpdatePhase.GameSimulation);

            // Cat track overlap: forges AreaGeometryData.m_Type from Surface to Space on the SkiRunCatTrackA prefab at every Game-mode
            // load. A cat track can therefore cross other runs (ValidationHelpers.cs:551 compares only the type). Disabled,
            // OnGameLoadingComplete only.
            updateSystem.UpdateAt<SkiRunAreaTypeSystem>(SystemUpdatePhase.GameSimulation);

            // Read-only lodge spawn-spot diagnostic ("Lodge [spots]"). Phase and order are NOT load-bearing: it only reads, and the
            // producers all run before GameSimulation in a frame or before OnGameLoadingComplete.
            updateSystem.UpdateAt<SkiLodgeSpotDiag>(SystemUpdatePhase.GameSimulation);
        }

        /// <summary>One line per load of the mod: "UI module present: True/False", with "!!!" when False. The line is cross-checked against
        /// the build: BuildStamp.UiBundled (generated into obj/ by the csproj target SkiBuildStamp) says whether this DLL's build deployed
        /// a bundle. It never throws.</summary>
        private static void LogUiModulePresence()
        {
            const string moduleFile = "SkiResortMod.mjs";
            try
            {
                bool bundled = BuildStamp.UiBundled;
                string stamp = BuildStamp.Value;
                if (ModRootPath == null)
                {
                    Log.Error($"!!! UI module present: unknown — ModRootPath is null (TryGetExecutableAsset failed), " +
                              $"so {moduleFile} cannot be checked; DLL stamp {stamp}, UI bundled at build: {bundled}");
                    return;
                }
                bool present = File.Exists(Path.Combine(ModRootPath, moduleFile));
                if (present && bundled)
                {
                    Log.Info($"UI module present: True ({moduleFile} beside the DLL; DLL stamp {stamp} — the hub's " +
                             "uiHello must report the same stamp)");
                }
                else if (present)
                {
                    Log.Warn($"UI module present: True — but this DLL was built WITHOUT the UI (stamp {stamp}): the " +
                             $"{moduleFile} beside it comes from another build; the hub handshake names both stamps");
                }
                else if (bundled)
                {
                    Log.Error($"!!! UI module present: False — this DLL was built with the UI (stamp {stamp}) but " +
                              $"{moduleFile} is missing from {ScrubUserPath(ModRootPath)}: no Ski Resort hub this session " +
                              "(redeploy with dotnet build, game closed)");
                }
                else
                {
                    Log.Error($"!!! UI module present: False — this DLL was built without the UI (stamp {stamp}: " +
                              "node_modules missing or -p:SkipUi=true): no Ski Resort hub this session. Run npm ci in " +
                              "mod/SkiResortUI and rebuild");
                }
            }
            catch (Exception e)
            {
                Log.Error($"!!! UI module presence check failed (non-fatal, the mod continues): {e.GetType().Name}: {e.Message}");
            }
        }

        public void OnDispose()
        {
            Disposed = true;   //the quit disposes mods before the world (GameManager.cs:792-793; 1.6.2 :802-803)
            Log.Info(nameof(OnDispose));
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }

        /// <summary>
        /// Re-runs the Tourism Overhaul scan after all mods have loaded. The OnLoad-time scan races mod load order (assemblies of mods
        /// loaded after ours are not yet in the AppDomain). It can therefore log "detected: False" while the mod is present. Called from
        /// SkiResortAttractivenessSystem.OnGameLoadingComplete. The first city-load check of the session is always logged, so the log never
        /// ends on the early check's answer. Later checks log only a change.
        /// </summary>
        public static void RefreshTourismOverhaulDetection()
        {
            bool detected = DetectTourismOverhaul();
            bool changed = detected != TourismOverhaulDetected;
            TourismOverhaulDetected = detected;
            if (s_RescanLogged && !changed)
                return;
            s_RescanLogged = true;
            Log.Info($"Tourism Overhaul detected (city-load check): {detected}{(changed ? " (changed)" : "")}");
        }

        /// <summary>The first city-load check of the session was logged.</summary>
        private static bool s_RescanLogged;

        /// <summary>
        /// Scans loaded assemblies for Tourism Overhaul by name only. Deliberately does not touch its types: no hard dependency,
        /// no assumption it is present.
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
