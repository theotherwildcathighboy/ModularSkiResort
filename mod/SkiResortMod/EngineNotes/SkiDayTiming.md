# SkiDayTiming: how a ski-day claim's end is told apart from the traces vanilla leaves

## Near EndCauseOf

At the Left verdict, the cases apply in this order:

1. Gone: `Deleted`.
2. Still "there": Leisure is aimed at the lodge and the citizen is in it. Left fires there only past the grace. Result: `PastGrace`.
3. Now >= deadline (the compare in `Resolve`): `DeadlineClosing` when the claim's deadline was the lifts' closing. Otherwise `DeadlineOwn`
   (sleep or work).
4. Leisure is aimed elsewhere: `NewOuting`.
5. No Leisure and a counter >= `NeedFilledCounter`: `NeedFilled` (inferred).
6. No Leisure: `Removed`. The causes in `CitizenBehaviorSystem` are moving away, illness and a shopping errand. `RemoveWhyOf` gives the reason.
   Bedtime and work time end at the claim's deadline. `DoLeisure` sets that deadline to the next sleep or work start (1.6.2 :472-494).
   The claim deadline is min(that, closing). So bedtime and work time read `DeadlineOwn` / `DeadlineClosing`.
7. Else (Leisure is at the lodge and the citizen is not in it): `WalkedOut`.

Not told apart: `LeisureSystem.SpendLeisure` also removes Leisure at a venue that is switched off (`BuildingOption.Inactive`), has
`ServiceAvailable <= 0` or has no output resource (1.6.2 `LeisureSystem.cs:285-310`). For our lodge, the cause is the player deactivating it.
That case lands in `Removed` (or `NeedFilled` with a high counter). It is rare, and the `[lodges]` line shows it.
