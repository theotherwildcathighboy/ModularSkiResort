# SkiLiftTicketSystem.Presale: how the pre-season pass sale pools households and why a sale tick drains no arrivals

The pre-season pass sale is the ticket system's second job. It runs once per (ski area, season key). It starts at the first ticket
tick from the season's second month on (`SkiMoneyRules.PresaleDue`: August for a December winter). The gate does not need to be open.
A household buys for the coming season. So the sale rates the area as if fully open (`SkiResortAttractivenessSystem.ComputePresale`:
snow gating off, every installed lodge and the school active, efficiency 1). It pools by every lodge that passes, or that would
pass if it were active. A lodge closed when the snapshot was taken is not in the ski days lodge list. It comes from
`SkiResortAttractivenessSystem.CopyInactiveLodgeInfo` (road, not destroyed, no slope-side test). A lodge closed later comes from
`ClaimJob.LodgePresale`. The 1.0.2 release test showed why. At the first open-gate tick, the lodges closed for the off-season were
still shut. The area had no target, and the season was marked done with no sale. The bridge check of d9eae884 then found that the
closed lodges were missing from the lodge list. One Burst job rates every resident
household of the area's pool and sells passes in bulk. The first-arrival sale (SkiLiftTicketSystem.cs, see SkiLiftTicketSystem.md,
"Season passes") then applies only to households that were not in the pool. These are new residents, or households out of reach when the sale ran.

## The pool

SkiDaySystem keeps no household-level pool (an area snapshot plus a citizen walk). So this job reuses the snapshot through
`SkiDaySystem.TryCopyPresaleInputs` and the same SkiDayRules functions (WithinReach, ClassOf, Chance). The inputs are the as-if-open attraction and school
factor, price tolerance, class confidences, knobs and xz of the target lodges. It walks resident households itself (own query:
Household + PropertyRenter + HouseholdCitizen, not tourist, commuter, homeless or moving away). Pool = such a household with no
SkiSeasonPass standing this season (`SkiMoneyRules.PassStandingOf`) whose home is within ski-day reach of a target lodge of this
area.

Per pool household, the job computes:
- d = straight line home to nearest target lodge.
- class = ClassOf(d, adult, owns a vehicle).
- Chance(resident, attraction, d, openShare 1, powder 0, ticket price now, tolerance, school factor, confidence, knobs, pass holder).
- p = `SkiMoneyRules.PresaleProbability(PassShare, chance, PassFactor)` (1.0.2: the pass demand factor, set once per schedule from
  `PassFactor(pass price, ticket now)`).
- One deterministic roll (`SkiMoneyRules.PresaleUniform`: household index + season key. It differs from the first-arrival roll).

A buyer pays `SalePassPrice(SeasonPassPrice, ticket now)` if its money covers it (never below 0, else CannotPay). The job books the payment exactly
like tickets (a FeeEvent per pass). Every pool household gets a standing: Bought, or Declined (rolled out and could not pay both store
Declined).

### Per-area sale (1.0.4, T284)

Each area sells at its own plan: `AreaPrice.SalePrice`, phase and window position (`SkiPassPlanRules`). An area with no sale this hour
(SalePrice 0) is skipped. `m_SaleApplied[sign]` holds the window position of the last applied run per area, and a NoTarget area
still sets it. `CopyAllTargets(hour)` caches every area's target lodges once per game hour. A household is pooled only at its NEAREST
area: the SaleJob skips it when another area's lodge is closer (`if (other < d) continue`). So a household that lives between two
areas is rolled once, at the nearer one. Each bought pass writes SkiSeasonPassArea: the selling sign, and AreaFlagPool when the area is
on the default plan. Demand is `SkiPassPlanRules.Demand(price, ticket, value left)`.

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
