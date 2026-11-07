# SkiSnowGunPlumeSystem: how snow-gun vapor plumes are driven through the engine's effect system

Decompile-verified. Citations are Game.dll 1.6.0f1. The Effects, BuildingLotRender, ValidationSystem, GenerateObjects and Events files
are identical in 1.6.2f1. ValidationHelpers, SystemOrder and GameManager changed there, and we re-read the cited lines.

## The mechanism

- The six gun object prefabs (SkiSnowGunTower / TowerLow / TowerTall / Fan / Lance / LanceTall) carry an `EffectSource` entry for
  OUR EffectPrefab `SkiSnowGunPlume`. It is a VFX component on the vanilla WaterVaporFactorySmall graph (UnityObjectsMap 0f7db876...),
  with its own m_MaxCount 256 and m_RequiredFlags = Disaster 0x800. `EffectSource` adds `EnabledEffect` to the archetype of the gun
  (EffectSource.cs:56-59), so `EffectControlSystem` owns the plume (EffectControlSystem.cs:1087-1097).
- "Disaster", for a gun (not a ServiceUpgrade, not an ExtractorFacility, so owner = top owner = the gun, EffectControlData.cs:702-731),
  is `EarlyDisasterWarningDuration.m_EndFrame > simulation frame` on the gun (EffectControlData.cs:419-424, the frame is
  SimulationSystem.frameIndex, EffectControlSystem.cs:1155). That component has exactly one other user in Game.dll:
  Game.Events/InitializeSystem.cs:565. It writes the component only to entities of m_EDWSBuildingQuery =
  All<Game.Buildings.EarlyDisasterWarningSystem> (:280). On a gun the component is inert: nothing else reads, writes or removes it (grep of
  1.6.0f1 and 1.6.2f1). No gun ever had the component before, so a gun without it shows no plume.

## Why not the vanilla WaterVaporFactorySmallVFX (Operational) + Game.Buildings.Extension

Rejected. The facts behind it:

- The Burst job of BuildingLotRenderSystem reads m_PrefabBuildingExtensionData[prefab] UNGUARDED for every chunk with Extension
  and Temp / Error / Warning (BuildingLotRenderSystem.cs DrawObjects, the `flag2` branch).
- Our gun prefabs have no BuildingExtensionData.
- ValidationSystem gives a PERMANENT colliding object Error while the player drags a road or a prop over a gun (Game.Objects/ValidationHelpers.cs
  CheckOverlap: m_PermanentEntity = the other object, severity Error by default. AddError in ValidationSystem).
- GenerateObjectsSystem copies Extension from an original to its Temp copy.

A missing-component lookup in a Burst job with safety checks off is undefined behavior (a crash class).

## Writing the state

The system sets the `EarlyDisasterWarningDuration` of the gun (added once, then SetComponentData). Only when the visible state flips, it
adds `Game.Common.EffectsUpdated`. The updated query of EffectControlSystem takes it (:1087-1097). The job of EffectControlSystem queues
CheckEnabled for a near-camera owner (:224-231). CleanUpSystem strips the tag at the end of the frame. The system stamps the tag in
GameSimulation (LateUpdate). The tag lives into the NEXT frame's PreCulling, where EffectControlSystem runs (SystemOrder.cs:636,
1.6.2 :637), and dies at the Cleanup of that frame (GameManager.cs:2391 / :2399 / :2407: MainLoop, Cleanup, LateUpdate. SkiNightLightSystem
uses the same pattern). The culling job disables an owner-culled plume of a gun off screen, and re-checks it when the gun comes near the
camera (:298-353). So a change made while off screen shows on arrival.

## Lease

(`SnowGunPlumeRules`.) The end frame of a plume is 1.5 game hours ahead. The system renews it while the plume runs and cuts it to 0 when
the plume stops. Without this system (assets kept, code removed), every plume runs out by itself at the next re-check.

## The rule

One predicate with the injection (SnowGunPlumeRules header). A gun plumes when all three conditions hold:

- The Options toggle is on.
- Its run's guns ran in the last pass of the injection (`SkiSnowInjectionSystem.TryGetRunGunsRunning`: hysteresis state Running AND a cold
  texel. Stopped at GunStopDepthM, natural snow that deep included. Warm-spell protection never counts).
- The own elevation of the gun is cold (`SnowLineSystem.GetLocalTemperature` <= freezing + the margin, the per-texel test of the fold).

FanIdle, hydrants and the folded lance carry no plume entry and are never touched.

## Scope and cadence

Game mode only. The system handles the placed guns of OUR snowmaking and night runs and cat tracks, by the `SkiRunPropMember` tag
(free-standing, EngineNotes/SkiRunPropMember.md). A cat track has no measured pack: its guns
run while `SkiSnowInjectionSystem.CatTrackGunsCanRun` says so. Nothing else on the map is written. The system runs every
`SnowGunPlumeRules.PlumeIntervalFrames` (64) sim frames, anchored after the injection (Mod.cs, not load-bearing: it reads the per-run flags
that the injection published, at most one window old). Cost: one pass over the guns of our runs (tens to a few hundred entities). It
writes only on a flip or a lease renewal.

## Logs

- One ARMED / inert line per load (prefabs found, plume entries found, the numbers of the rule).
- Per run, a line when its guns start or stop plumes (bounded by the hysteresis and the thermal line, a few per game day).
- A census line whenever the number of plumes changes, at most one per CensusWindowS of wall clock (a heartbeat when unchanged is not
  needed: the per-run lines carry the state).
- A WARN once per load when more guns want a plume than the m_MaxCount of the effect (VFXSystem.cs:224 drops the rest silently,
  near-camera only).
- The house one-shot "!!!" catch that disables the system for the load.

The run rows of the catalog carry the guns clause (SkiRunCatalogSystem.SnowClause).
