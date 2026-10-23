# OffSeasonStaffSystem: how the half-staff write interacts with the game's workplace tick

Decompile baseline `Game.dll` 1.6.0f1. The pure rules and the wage/upkeep reasoning live in `OffSeasonRules.md`.

## Per poll

- Per Welcome Sign: `OffSeasonRules.Step` on its released score (`SkiResortAttractivenessSystem.TryGetAreaScore`: known and not
  `WriteHeld`; noOpenRun = `Result.RunsCounted == 0`). An area with no released score is not stepped and reads in season.
- Per top-level lodge / school with its own `WorkProvider`: `m_MaxWorkers = OffSeasonRules.Target(prefab WorkplaceData.m_MaxWorkers,
  area off, setting)`.
  - Lowering it lets the game's tick lay the surplus off (`WorkProviderSystem.cs:199-230`).
  - Raising it restarts vanilla's new-workplace grace (`m_EfficiencyCooldown = -m_ServiceBuildingEfficiencyGracePeriod`,
    `CityServiceWorkplaceInitializeSystem.cs:111`, :119) so the hiring weeks do not cut efficiency (`WorkProviderSystem.cs:259-263`
    saturates a negative cooldown to no penalty).
- The hut's and the summit lodge's jobs ride on the sign: `MountainJobsSystem` asks `IsHalf(sign)` and halves them.

## Lifecycle

- Nothing vanilla rewrites `m_MaxWorkers` of a city service building after its Created frame, so this system re-asserts on every poll.
- Turning the setting off restores every building on the next poll.
- The first poll after a load takes the area state at once (`WorkProvider` serializes).
- Upkeep is not halved, only the wage row (see `OffSeasonRules.md`).
