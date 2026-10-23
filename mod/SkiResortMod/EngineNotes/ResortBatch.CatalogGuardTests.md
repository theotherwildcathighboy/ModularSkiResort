# CatalogGuardTests: what ties the prefab catalog to the rule files and the hub tip texts

`CatalogGuardTests` keeps the pure prefab catalog `SkiCatalog.cs` and the pure rule files that alias it in step.

## CG-1: every alias equals its row equals its const

- `SkiResortScore.LiftValue`, `SkiMoneyRules.LiftFixedPerMonth` / `LiftPerKmPerMonth`, `HubRules.ChairSpeedMps` / `LiftLinePerKm` and
  `LiftServedRule.ServeRadiusM` / `TopMarginM` against `Lifts[0]` and `SkiCatalog.FixedGrip`.
- `SkiDayRules.LodgeSkiersPerDay` against `Lodges[0].Capacity` and `SkiCatalog.AFrameLodge`.
- `Lifts.Count == 4` and `Lifts[0].Id == "fixedGrip"`: the aliases stay the fixed-grip row's, because per-row numbers reach Burst jobs as
  struct fields.
- The consts of other rule files (`SchoolRule`, `PatrolRule`, `SkiMoneyRules.LodgeSpendDefault` / `Max` / `Step`) equal `SkiCatalog.School`
  / `Patrol` / `LodgeSpend`. They are read by reflection over this test assembly (the linked files compile into it), so the host builds
  when one of those files does not exist yet: that prints one "INFO ... not landed yet" line; once present, any mismatch fails.

## CG-2

`HubRules.RideTimeS(2045)` equals 2045 / 2 / `Lifts[0].SpeedMps` (409.0): the ride time uses the catalog speed.

## CG-T: text guard over `mod/SkiResortMod/LocaleEN.cs`

Single-line `{ "key", "value" },` entries are checked (`hubText.ts` is held equal to the locale by `check-bundle.js`, so one side is
enough here). Every number in the texts is derived from the catalog:

1. `Assets.NAME[SkiChairliftHV]` is "Fixed-Grip Triple Chairlift" while `SeatsPerCarrier == 3`.
2. `SkiResort.Hub.LIFT_CAPACITY_TIP` contains `{SEATS}` and `{SPEED}` and no digit.
3. `SkiResort.Hub.LIFT_RIDE_TIP` contains `{SPEED}` and no digit apart from the game-speed "1x".
4. `SkiResort.Hub.RECEIPT_TIP[Lifts]` names every Loop row "<name> <ScoreValue:0>" in index order (fixed-grip 6, detachable 15, gondola
   25) and the summer-share words of every lambda below 1 (0.25 "a quarter" for all three chairlift-class types).
5. `SkiResort.Hub.RECEIPT_TIP[LodgeBase]` contains "up to <Ladder.ExtraCap>" (24).
6. `BUILD_TIP_SCHOOL` has "+<School.ScoreValue>" and "<(DemandFactor - 1) x 100> %" (+12, 20 %); `BUILD_TIP_PATROL` has "<RadiusM / 1000>
   km", "<(ExpertFactor - 1) x 100> %" and "<BackcountryServedMin x 100> %" (1 km, 15 %, 70 %); `BUILD_TIP_LUXURY` has the luxury tiers'
   `PriceTolerance` (40); `PROBLEM_TIP[SummitLodgeNotLiftServed]` has "<Ladder.SummitServedRadiusM> m" (300 m).
7. Both `LIFT_RIDE_TIP` and `LIFT_CAPACITY_TIP` are shown for a tram row too, so `LIFT_RIDE_TIP` contains no "loop" and
   `LIFT_CAPACITY_TIP` no "spaced" (case-insensitive); the same for `LIFTS_TOTAL_CAPACITY_TIP`, the totals row that sums a tram too (no
   "spaced"; it names the tram's "station stops").
8. An unpowered lift of every type is inert, power never lowers its upkeep, and its power is the base station's. Case-insensitive checks:
   - `TILE_TIP[Lifts]` has no "still move" and says the lift "stops".
   - `LIFT_CAPACITY_TIP` and `LIFTS_TOTAL_CAPACITY_TIP` name "power" (an unpowered lift has no capacity shown).
   - `PROBLEM_TIP[LiftNoPower]` says it "stops", names the "base station", says maintenance / upkeep is "still charged" and does NOT say
     "full" (the kept NoOpenRun quarter reaches an unpowered lift whose ski area counts no run; if "strict" billing is chosen, flip this
     check).
   - Each of those four is 25 words or fewer (whitespace-separated tokens, as `wc -w`; the `LocaleEN.cs` rule).
   - `MONEY_TIP[Lifts]` and `MONEY_TIP[LiftsTypes]`: the "a quarter ..." clause (to its full stop) names no power, and the text says "in
     full" and "power" (power never lowers the bill).
   - `LIFT_COST_REDUCED[NoPower]` names no power: that row is billed reduced only for NoOpenRun (`SkiMoneyRules.LiftUpkeepBillingOf`),
     while `hubRules.ts` `liftCostReason` still picks this key for it.
   - `MONEY_TIP[Lifts]` has 42 words or fewer and `MONEY_TIP[LiftsTypes]` 44 or fewer (the `LocaleEN.cs` header exempts them from the
     25-word rule because they carry live figures).

Oracles that stay elsewhere: `ScoreTests.cs` (LiftValue == 6.0), `HubRulesTests.cs`, `MoneyBatch.cs`, `SkiDayTests.cs`.
