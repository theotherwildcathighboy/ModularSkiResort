# SkiSnowGunPlumeSystem: how snow-gun vapor plumes are driven through the engine's effect system

Decompile-verified; citations are Game.dll 1.6.0f1. The Effects, BuildingLotRender, ValidationSystem, GenerateObjects and Events files
are identical in 1.6.2f1; ValidationHelpers, SystemOrder and GameManager changed there, and the cited lines were re-read.

## The mechanism

- The six gun object prefabs (SkiSnowGunTower / TowerLow / TowerTall / Fan / Lance / LanceTall) carry an `EffectSource` entry for
  OUR EffectPrefab `SkiSnowGunPlume`: a VFX component on the vanilla WaterVaporFactorySmall graph (UnityObjectsMap 0f7db876...),
  with its own m_MaxCount 256 and m_RequiredFlags = Disaster 0x800. `EffectSource` adds `EnabledEffect` to the gun's archetype
  (EffectSource.cs:56-59), so `EffectControlSystem` owns the plume (EffectControlSystem.cs:1087-1097).
- "Disaster", for a gun (not a ServiceUpgrade, not an ExtractorFacility, so owner = top owner = the gun, EffectControlData.cs:702-731),
  is `EarlyDisasterWarningDuration.m_EndFrame > simulation frame` on the gun (EffectControlData.cs:419-424; the frame is
  SimulationSystem.frameIndex, EffectControlSystem.cs:1155). That component has exactly one other user in Game.dll:
  Game.Events/InitializeSystem.cs:565, which writes it only to entities of m_EDWSBuildingQuery =
  All<Game.Buildings.EarlyDisasterWarningSystem> (:280). On a gun it is inert: nothing else reads, writes or removes it (grep of 1.6.0f1
  and 1.6.2f1). No gun ever had it before, so a gun without it shows no plume.

## Why not the vanilla WaterVaporFactorySmallVFX (Operational) + Game.Buildings.Extension

Rejected. BuildingLotRenderSystem's Burst job reads m_PrefabBuildingExtensionData[prefab] UNGUARDED for every chunk with Extension
and Temp / Error / Warning (BuildingLotRenderSystem.cs DrawObjects, the `flag2` branch). Our gun prefabs have no BuildingExtensionData,
and ValidationSystem gives a PERMANENT colliding object Error (Game.Objects/ValidationHelpers.cs CheckOverlap: m_PermanentEntity = the
other object, severity Error by default; AddError in ValidationSystem) while the player drags a road or a prop over a gun, and
GenerateObjectsSystem copies Extension from an original to its Temp copy. A missing-component lookup in a Burst job with safety checks
off is undefined behavior (a crash class).

## Writing the state

Set the gun's `EarlyDisasterWarningDuration` (added once, then SetComponentData) and, only when the visible state flips, add
`Game.Common.EffectsUpdated`. EffectControlSystem's updated query takes it (:1087-1097), its job queues CheckEnabled for a near-camera
owner (:224-231), and CleanUpSystem strips the tag at the end of the frame. Stamped in GameSimulation (LateUpdate), the tag lives into
the NEXT frame's PreCulling, where EffectControlSystem runs (SystemOrder.cs:636, 1.6.2 :637), and dies at that frame's Cleanup
(GameManager.cs:2391 / :2399 / :2407: MainLoop, Cleanup, LateUpdate; SkiNightLightSystem uses the same pattern). An owner-culled plume
of a gun off screen is disabled by the culling job and re-checked when the gun comes near the camera (:298-353), so a change made while
off screen shows on arrival.

## Lease

(`SnowGunPlumeRules`.) A plume's end frame is 1.5 game hours ahead, renewed while it runs, cut to 0 when it stops. Without this system
(assets kept, code removed) every plume runs out by itself at the next re-check.

## The rule

One predicate with the injection (SnowGunPlumeRules header). A gun plumes when the Options toggle is on, its run's guns ran in the
injection's last pass (`SkiSnowInjectionSystem.TryGetRunGunsRunning`: hysteresis state Running AND a cold texel; stopped at
GunStopDepthM, natural snow that deep included; warm-spell protection never counts) and the gun's own elevation is cold
(`SnowLineSystem.GetLocalTemperature` <= freezing + the margin, the fold's per-texel test). FanIdle, hydrants and the folded lance carry
no plume entry and are never touched.

## Scope and cadence

Game mode only; the placed guns of OUR snowmaking and night runs and cat tracks, by the `SkiRunPropMember` tag (free-standing, EngineNotes/SkiRunPropMember.md). A cat track has no measured pack: its guns
run while `SkiSnowInjectionSystem.CatTrackGunsCanRun` says so. Nothing else on the map is written. Every
`SnowGunPlumeRules.PlumeIntervalFrames` (64) sim frames, anchored after the injection (Mod.cs; not load-bearing: the per-run flags it
published are read, at most one window old). Cost: one pass over the guns of our runs (tens to a few hundred entities); writes only on a
flip or a lease renewal.

## Logs

One ARMED / inert line per load (prefabs found, plume entries found, the rule's numbers); per run, a line when its guns start or stop
plumes (bounded by the hysteresis and the thermal line, a few per game day); a census line whenever the number of plumes changes, at
most one per CensusWindowS of wall clock (a heartbeat when unchanged is not needed: the per-run lines carry the state); a WARN once per
load when more guns want a plume than the effect's m_MaxCount (VFXSystem.cs:224 drops the rest silently, near-camera only); the house
one-shot "!!!" catch that disables the system for the load. The catalog's run rows carry the guns clause
(SkiRunCatalogSystem.SnowClause).
