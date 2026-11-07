# SkiLiftTicketSystem.Presale: how the pre-season pass sale pools households and why a sale tick drains no arrivals

The pre-season pass sale is the ticket system's second job. It runs once per (ski area, season key), at the first ticket tick in which that
area's gate is Open in a new season (`SkiMoneyRules.SeasonKeyOf`). One Burst job rates every resident household of the area's pool
and sells passes in bulk. The first-arrival sale (SkiLiftTicketSystem.cs, see SkiLiftTicketSystem.md, "Season passes") then
applies only to households that were not in the pool. These are new residents, or households out of reach when the sale ran.

## The pool

SkiDaySystem keeps no household-level pool (an area snapshot plus a citizen walk). So this job reuses the snapshot through
`SkiDaySystem.TryCopyPresaleInputs` and the same SkiDayRules functions (WithinReach, ClassOf, Chance). The inputs are attraction, school
factor, price tolerance, class confidences, knobs and xz of the target lodges. It walks resident households itself (own query:
Household + PropertyRenter + HouseholdCitizen, not tourist, commuter, homeless or moving away). Pool = such a household with no
SkiSeasonPass standing this season (`SkiMoneyRules.PassStandingOf`) whose home is within ski-day reach of a target lodge of this
area.

Per pool household, the job computes:
- d = straight line home to nearest target lodge.
- class = ClassOf(d, adult, owns a vehicle).
- Chance(resident, attraction, d, openShare 1, powder 0, ticket price now, tolerance, school factor, confidence, knobs, pass holder).
- p = `SkiMoneyRules.PresaleProbability(PassShare, chance)`.
- One deterministic roll (`SkiMoneyRules.PresaleUniform`: household index + season key. It differs from the first-arrival roll).

A buyer pays PassPrice(multiplier, ticket now) if its money covers it (never below 0, else CannotPay). The job books the payment exactly
like tickets (a FeeEvent per pass). Every pool household gets a standing: Bought, or Declined (rolled out and could not pay both store
Declined).

## Ordering

The job writes household money and PlayerMoney like TicketJob. The main thread applies its standings as components at the next tick's
step 1 (batch structural change). So a tick that starts a sale does not drain arrivals (they stay in the counter's pending list, and
nothing is lost or repeated). A ticket job in the same window could not see the standings and would sell the same household a pass again.
The system schedules a sale only with no ticket job in flight (right after step 1).

## Cost and logs

One job per ski area per season, O(resident households) in Burst over a chunk list. The system times the main-thread schedule and apply,
and writes them once in the `Passes [season sale]` line. Unverified in game.

Logs: `Passes [season sale]` once per area per season (also says when an area had no target lodge), ARMED clause (PassArmedText), DUMP.
Known limits:
- A load mid-season runs the check again, but only households with no standing are pooled, so nothing is sold twice.
- A save made between the sale job and the next tick's step 1 loses the standings, and those households are decided again.
