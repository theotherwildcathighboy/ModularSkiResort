# OffSeasonRules: why off-season halves staff and not upkeep

Decompile baseline `Game.dll` 1.6.0f1.

## Why staff and not upkeep

A city service building's monthly bill is (upkeep + the wage of every employee) x budget % (`CityServiceBudgetSystem.cs:392-421`;
wages `CityServiceUpkeepSystem.GetUpkeepOfEmployeeWage` :628-643).

Lowering `WorkProvider.m_MaxWorkers` lays the surplus off in the game's own tick (`WorkProviderSystem.RefreshFreeWorkplace`
:199-230, `LayOffReason.TooMany`), so the wage part halves in the budget panel, the building's Upkeep section (:226-233) and the hub.

The upkeep part cannot be halved honestly: our prefabs bill it through `ServiceConsumption.m_Upkeep`, which
`BuildingInitializeSystem` turns into a `ServiceUpkeepData` with `m_ScaleWithUsage = false` (:1183-1191), and the selected
building's Upkeep section applies usage only to non-money resources (`UpkeepSection.cs:183-198` vs :206-211), so a halved budget
line would show the full amount. Upkeep stays full; vanilla's Inactive (upkeep x0.1 and no wage: `CityServiceBudgetSystem.cs:396-411`)
remains the player's deep cut.
