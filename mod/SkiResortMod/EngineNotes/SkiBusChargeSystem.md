# SkiBusChargeSystem: the Ski Bus Depot, its claim path and its coach bill

The depot is a road-side building in town (`SkiBusDepot`, `SkiCatalog.Depots`, deliberately not a row of `SkiCatalog.Lodges`). It
is a ski-day claim target and nothing else: no capacity, no score weight, no lodge spend, no lodge count, no lodge problem line.

## Link

`SkiResortAttractivenessSystem.LinkTopLevel`, kind `SchoolLinkRules.KindDepot`: the nearest live Welcome Sign at any distance
(`SchoolLinkRules.DepotMaxDistance` = +inf). `SkiResortIndex.TryResolve` accepts it. No sign still means not linked. Active depots go in
`AreaSlot.DepotActive`, read through `CopyActiveDepots`. The list moves `LodgeSetRevision` and `Generation`. So the ski days and
the guest counter's map follow a placement, a removal and an out-of-service switch.

## Claim

`SkiDaySystem.RebuildSnapshot` appends the depots to the area's contiguous lodge range after its lodges (`LodgeJob.IsDepot`, Capacity 0,
PriceTolerance 0, NoRoadNeeded 0, LodgeIndex -1). So `NearestTarget` picks home to depot or home to lodge by straight line with no routing
code. Reach, distance factor, fit check, class, the presale and the reach census all measure home to depot through the same path.

Step A runs the real lodges first (`CheckLodge`), then the depots (`SkiDayRules.CheckDepot`). The depot check follows the lodge order
with these differences:

- The road is always required.
- `IsSlopeside` is skipped.
- The check rejects the depot as `NoHillLodge` while the area has no passing hill lodge.

A mountain lodge counts. A lodge gated only by the missing leisure tag counts too, since step A keeps it in capacity. A passing depot
adds to `target` (the area state, so a mountain-only hill with a depot can open). It does not add to `lodgeCapacity`. Capacity stays
min(runs, hill lodges).

A claim carries `SkiDayTiming.FlagToDepot` (claim Flags bit 128). `SentDepot` / `ArrivedDepot` sum in `SkiDayMonth` and print on the
`SkiDays [day]` line as `bus depots: sent N, arrived N`.

## Guests

`SkiGuestCounterSystem.RebuildMap` adds an active linked depot to the target map, outside the lodge counters (`Guests [targets]` prints
`N bus depot(s) counted`). Arrivals there count as the area's guests and pay tickets, lessons and passes through the usual ticket
pass. No lodge spend: `SkiLiftTicketSystem` reads spend only from `LodgeDefs`. The depot keeps its LeisureProvider. `CheckLodge`'s tag
rule wants it, and vanilla's `LeisureSystem` searches on it. So a vanilla leisure visitor who picks the depot also counts and pays
(DECISIONS 505).

## Bill

Every 1,024 sim frames (the lift charge's cadence) `Charge` reads `SkiDaySystem.CopyDepots`. Each depot whose verdict is `SkiBusRules.IsBilled` (None,
or NoLeisureTag: a temporary gate) bills `SkiBusRules.MonthlyBill(DistanceM)` = 1,500 + 300 per straight-line km to the area's nearest
hill lodge. Accrual, carry and the PlayerMoney debit follow `SkiLiftChargeSystem.Charge`. Shares go to `SkiResortLedger.AddBusCharge`.

Ski days off or disarmed: `CopyDepots` is empty, so there is no bill. A depot with `NoHillLodge` or `NoRoad` bills nothing and raises
the hub's own rows (`DepotNoHillLodge`, `DepotNoRoad`).

## Save

`SkiResortLedger.Month.BusCharged` is in the month in progress (`SkiResortMonthInProgress` v2, 150 bytes, SaveLayouts). A closed
month has no bus column: `SkiResortHistoryMonth.FromLedger` folds it into `m_LiftCost`, so the History net stays true and the row reads
"Lifts" there.

## Read API (the visual coach, EngineNotes/SkiCoachSystem.md)

`SkiDaySystem.CopyDepots(List<DepotView>)` returns these fields: depot entity, its sign, its chosen hill lodge, depot xz, straight-line
distance, verdict. The data is from the last applied pass, in area order. Nothing else is built for it yet.

## Asset

`SkiBusDepot.Prefab` is hand-written around its own mesh, `SkiBusDepotA` (`assets/blender/ski_bus_depot.py`), on a 2 x 1 lot that flattens its own pad. Its icon is
`SkiResortIcons/SkiBusDepot.png` (`assets/blender/ski_icons.py`). No vanilla bus stop is on it: `Integrated Bus Stop` is a real transport stop marker (it would join routes). It uses only
plain vanilla props (bench, trash bin, park light, trees, rocks).
