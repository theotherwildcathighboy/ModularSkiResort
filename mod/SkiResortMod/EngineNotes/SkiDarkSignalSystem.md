# SkiDarkSignalSystem: one "dark" signal, and our lights on it (T250)

Citations: decompile 1.6.4f1. The cited files are byte-identical in 1.6.2f1.

## The problem

- Our run and groomer lights used the effect Night flag. `EffectFlagSystem.OnUpdate` sets it from the clock only:
  `normalizedTime >= 0.75 || < 0.25`, so 18:00 to 06:00 in every season (EffectFlagSystem.cs:155).
- Vanilla street lights use the sun. `StreetLightSystem` compares `LightingSystem.dayLightBrightness x 1000` with a per-light random
  200-300 (StreetLightSystem.cs:89, :109, :140, :368). So roads light at real dusk. Our runs lit at 18:00.
- `dayLightBrightness` is `saturate(sun intensity / 110000)`, written every frame in PreCulling (LightingSystem.cs:230, SystemOrder.cs:645).

## The signal (DarkSignalRules)

- Dark below 0.20. Light again from 0.30. The band is the hysteresis. The first look of a load uses the midpoint 0.25.
- Day/night visual cycle off: the game pins the sun at 14:30 (PlanetarySystem.cs:422-429). The brightness never says dark.
  Then the clock decides: dark from 17:00 to 07:00 (`TimeSystem.normalizedTime`).
- `Known` waits 30 frames after a load, so the lighting has run on the new city. `Ready` is also true after a failure. Then
  `DarkNow` is the clock.

## The spike: can a Night-gated effect light before 18:00 without Harmony? Yes

- The enable check reads the conditions from the effect prefab entity every time: `CheckConditions` takes
  `EffectData.m_Flags` of the effect (EffectControlData.cs:614-621). `CheckTriggers` walks the required and forbidden bits
  (:590-611). The Night bit goes to `EffectFlagSystem.IsEnabled` (:431-436).
- `EffectData` is a plain prefab component. `EffectPrefab.LateInitialize` copies `m_Conditions` into it once (EffectPrefab.cs:31-45).
  No vanilla system writes it again.
- Our four light effects are our own `EffectPrefab`s: `SkiPisteFloodSource`, `SkiSnowGunBeaconSource`, `SkiGroomerFloodSource`,
  `SkiGroomerBeaconSource` (tools/shipped-prefabs.json). Each requires Night (8192). No vanilla object uses them.
- So this system rewrites their `m_RequiredFlags` at dusk and dawn:
  - On: Night removed. No condition is left, so the light is on.
  - Off: Night removed, `Occurring` required. `Occurring` is true only when the top owner has `WeatherPhenomenonData`
    (EffectControlData.cs:425-430). Our props never do, so the light is off.
  - Vanilla: the authored value, read from the managed prefab (`EffectPrefab.m_Conditions`). Used with the clock fallback, on
    every load, in the Editor and after a failure.
- Other readers of the required flags test other bits only: `EffectTransformSystem` (MainLights, ExtraLights, WarningLights,
  :198-200), `AudioGroupingSystem` (Cold, :129, :166), `BuildingInitializeSystem` (Collapsing, OnFire, :326, :435).
- The rewrite changes nothing by itself. A prop re-reads its conditions only on `Updated`, `EffectsUpdated` or `BatchesUpdated`
  (EffectControlSystem.cs:1087-1097). `SkiNightLightSystem` and `SkiGroomerSystem` stamp `EffectsUpdated` when `LightsOn` flips,
  as they did for the Night flag.
- All our lights flip together. Vanilla street lights flip over a 0.20-0.30 spread, one random threshold per light. A per-light
  spread would need a per-prop condition. That is not possible on a shared prefab. Accepted.
- `SkiNightLightSystem` runs every 2048 sim frames. A run prop can see the flip up to about 11 game minutes late. Groomers check
  every frame.

## Registration

- `UpdateBefore<SkiDarkSignalSystem, SkiGroomerSystem>(Modification4B)`. It runs every frame, paused too. The groomer reads
  this frame's value. The prefab write lands before PreCulling, where `EffectControlSystem` reads it.
- The write is `EntityManager.SetComponentData` on a prefab entity. It syncs on the effect jobs that read `EffectData`. It happens
  only when the gate changes: twice per game day, plus once per load.

## Logs

- One `DarkSignal: ARMED` line per Game load, with the band and the number of effects found.
- One `DarkSignal [dark]` line per dusk and dawn: clock, brightness, source, and the light gate.
- One WARN per missing effect prefab. One `!!!` line on an exception. Then the clock and the game's Night flag stand in until the
  next load.
