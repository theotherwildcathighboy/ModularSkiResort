# SkiLiftTicketSystem.Presale: how the pre-season pass sale pools households and why a sale tick drains no arrivals

The pre-season pass sale is the ticket system's second job. Once per (ski area, season key), at the first ticket tick in which that
area's gate is Open in a new season (`SkiMoneyRules.SeasonKeyOf`), one Burst job rates every resident household of the area's pool
and sells passes in bulk; the first-arrival sale (SkiLiftTicketSystem.cs, see SkiLiftTicketSystem.md, "Season passes") then
applies only to households that were not in the pool (new residents, or out of reach when the sale ran).

## The pool

SkiDaySystem keeps no household-level pool (an area snapshot plus a citizen walk), so this job reuses the snapshot through
`SkiDaySystem.TryCopyPresaleInputs` (attraction, school factor, price tolerance, class confidences, knobs, xz of the target
lodges) and the same SkiDayRules functions (WithinReach, ClassOf, Chance), and walks resident households itself (own query:
Household + PropertyRenter + HouseholdCitizen; not tourist, commuter, homeless or moving away). Pool = such a household with no
SkiSeasonPass standing this season (`SkiMoneyRules.PassStandingOf`) whose home is within ski-day reach of a target lodge of this
area.

Per pool household: d = straight line home to nearest target lodge, class = ClassOf(d, adult, owns a vehicle), Chance(resident,
attraction, d, openShare 1, powder 0, ticket price now, tolerance, school factor, confidence, knobs, pass holder), p =
`SkiMoneyRules.PresaleProbability(PassShare, chance)`, one deterministic roll (`SkiMoneyRules.PresaleUniform`: household index +
season key; differs from the first-arrival roll). A buyer pays PassPrice(multiplier, ticket now) if its money covers it (never
below 0; else CannotPay), booked exactly like tickets (a FeeEvent per pass). Every pool
household gets a standing: Bought, or Declined (rolled out and could not pay both store Declined).

## Ordering

The job writes household money and PlayerMoney like TicketJob, and its standings are components applied on the main thread at the
next tick's step 1 (batch structural change). So a tick that starts a sale does not drain arrivals (they stay in the counter's
pending list; nothing is lost or repeated): a ticket job in the same window could not see the standings and would sell the same
household a pass again. Scheduling happens only with no ticket job in flight (right after step 1).

## Cost and logs

One job per ski area per season, O(resident households) in Burst over a chunk list; the main-thread schedule and apply are timed
and written once in the `Passes [season sale]` line. Unverified in game.

Logs: `Passes [season sale]` once per area per season (also says when an area had no target lodge); ARMED clause
(PassArmedText); DUMP. Known limits: a load mid-season runs the check again, but only households with no standing are pooled, so
nothing is sold twice; a save made between the sale job and the next tick's step 1 loses the standings and those households are
decided again.
