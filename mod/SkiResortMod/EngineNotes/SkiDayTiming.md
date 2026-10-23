# SkiDayTiming: how a ski-day claim's end is told apart from the traces vanilla leaves

## Near EndCauseOf

At the Left verdict, in this order:

1. gone: `Deleted`;
2. still "there" (Leisure aimed at the lodge and in it: Left fires there only past the grace): `PastGrace`;
3. now >= deadline (`Resolve`'s compare): `DeadlineClosing` when the claim's deadline was the lifts' closing, else `DeadlineOwn` (sleep
   or work);
4. Leisure aimed elsewhere: `NewOuting`;
5. no Leisure and a counter >= `NeedFilledCounter`: `NeedFilled` (inferred);
6. no Leisure: `Removed` (`CitizenBehaviorSystem`: moving away, illness, shopping errand; why: `RemoveWhyOf`). Bedtime and work time
   end at the claim's deadline, since `DoLeisure` sets it to the next sleep or work start (1.6.2 :472-494), and the claim deadline is
   min(that, closing), so they read `DeadlineOwn` / `DeadlineClosing`;
7. else (Leisure at the lodge, not in it): `WalkedOut`.

Not told apart: `LeisureSystem.SpendLeisure` also removes Leisure at a venue that is switched off (`BuildingOption.Inactive`), has
`ServiceAvailable <= 0` or no output resource (1.6.2 `LeisureSystem.cs:285-310`); for our lodge that is the player deactivating it,
which lands in `Removed` (or `NeedFilled` with a high counter). Rare, and visible in the `[lodges]` line.
