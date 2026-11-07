# OffSeasonStaffSystem: how the half-staff write interacts with the game's workplace tick

Decompile baseline `Game.dll` 1.6.0f1. The pure rules and the wage/upkeep reasoning live in `OffSeasonRules.md`.

## Per poll

- Per Welcome Sign: the system runs `OffSeasonRules.Step` on the sign's released score. The score is released when `SkiResortAttractivenessSystem.TryGetAreaScore`
  knows it and it is not `WriteHeld`. noOpenRun = `Result.RunsCounted == 0`. An area with no released score is not stepped and reads in season.
- Per top-level lodge / school with its own `WorkProvider`: `m_MaxWorkers = OffSeasonRules.Target(prefab WorkplaceData.m_MaxWorkers,
  area off, setting)`.
  - Lowering `m_MaxWorkers` lets the game's tick lay the surplus off (`WorkProviderSystem.cs:199-230`).
  - Raising it restarts vanilla's new-workplace grace (`m_EfficiencyCooldown = -m_ServiceBuildingEfficiencyGracePeriod`,
    `CityServiceWorkplaceInitializeSystem.cs:111`, :119). So the hiring weeks do not cut efficiency
    (`WorkProviderSystem.cs:259-263` saturates a negative cooldown to no penalty).
- The jobs of the hut and of the summit lodge ride on the carrier. `MountainJobsSystem` asks `IsHalf(sign)` and halves them.
- A carrier lodge: `m_MaxWorkers = CarrierTarget(own target, MountainJobsSystem.CarriedBy)`. The system writes no lodge before
  `MountainJobsSystem.TargetsReady`. A first poll with only the own target would lay the carried staff off at load.

## An off-road lodge on a cat track chain
Its own jobs leave (`LodgeSupplyRule.JobsLeave`, read through `SkiResortAttractivenessSystem.IsOffRoadSupplied`). The target is 0.
- The game's tick lays the staff off (RefreshFreeWorkplace, `WorkProviderSystem.cs:199-230`). With 0 workplaces, the tick also resets the
  staffing factors and removes the worker icons (:232-312).
- Once no staff is listed (`MountainJobsRules.CanDropProvider`), the system does these steps:
  1. It removes `WorkProvider` and `FreeWorkplaces`.
  2. It sets the factors NotEnoughEmployees / SickEmployees / EmployeeHappiness to 1.
  3. It removes the two worker icons (`WorkProviderParameterData` m_UneducatedNotificationPrefab, m_EducatedNotificationPrefab).
- The prototype measured that shape: no worker icons, efficiency 1, and it persists through a reload. `EmployeesSection.cs:89` hides the section
  without `WorkProvider`.
- The removal has two steps: the staff leave first, then `WorkProvider` goes. Removing it while staff are listed would orphan their Worker rows.
- Back at a road or off the chain: `WorkProvider` + an `Employee` buffer come back with vanilla's grace, as at Created
  (`CityServiceWorkplaceInitializeSystem.cs:103-121` adds them only then).

## Lifecycle

- Nothing vanilla rewrites `m_MaxWorkers` of a city service building after its Created frame, so this system re-asserts on every poll.
- Turning the setting off restores every building on the next poll.
- The first poll after a load takes the area state at once (`WorkProvider` serializes).
- Upkeep is not halved. Only the wage row is halved (see `OffSeasonRules.md`).
