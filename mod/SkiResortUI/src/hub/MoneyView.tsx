/*
 * The Money ("Finances") view: what the resort earns and costs this month, and the one net.
 * It has three rows like the game's budget panel (DECISIONS 613): INCOME (fold) / RUNNING COSTS (fold) / Net. Every number is C#'s (HubRules.IncomeMonth / CostsMonth / NetMonth, contract
 * 38). The UI sums nothing. The folds hold ONLY the Net's nine rows (four incomes, five running costs). The rows sum to their header.
 * An income row is hidden only while it holds no money (hubRules.netRowShown).
 * Costs arrive positive and the view shows them negated. A negative (unknown) cost shows "–", never "+". MONEY_PRICE_LINE shows the
 * ticket price and the ski-day share (priceFactorPct mirrors C# SkiDayRules.PriceFactor). RUNNING COSTS and ALSO are Folds, one open at a time.
 * Panel file (rule R11): subscribes resorts$, selectedResort$, resortLive$, city$, build$, runs$.
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { RUN_KIND, build$, city$, problems$, resortLive$, resorts$, runs$, selectedResort$ } from "../bindings";
import type { BuildItem, BuildItemId, CityStats, ProblemRow, ResortLive, ResortRow, RunRow } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import { BUILD_ITEMS, findBuildItem } from "./buildItems";
import {
  PRICE_EXAMPLES, PRICE_NO_EFFECT_MAX, cityIncomeShown, hubPrice, liveOf, netRowShown, passesShown, presaleShown, priceFactorPct,
  priceFullWith, schoolBuiltOf, schoolState, ticketsRow,
} from "./hubRules";
import type { SchoolState } from "./hubRules";
import { HUB_FMT, HUB_LOC, LIFT_TYPE, monthName } from "./hubText";
import { Divider, InfoLine, LocText, Num, SignedNum, StableWidth, SubLine, TipLines, numArg, useNumberSamples } from "./parts";
import type { NumberSamples } from "./parts";
import { currentResort, resortEntries, runsOf } from "./resortData";
import { Fold } from "./fit";
import styles from "./hub.module.scss";

const DASH = "–";

/** A rate of a build item for a tooltip argument ("–" when absent or unknown). */
function rate(build: readonly BuildItem[], id: BuildItemId, field: "monthlyPerHa" | "monthly" | "monthlyPerKm"): JSX.Element | string {
  const item = findBuildItem(build, id);
  const v = item ? item[field] : undefined;
  return typeof v === "number" ? numArg(v, Unit.Money) : DASH;
}

/** A rate that bills now (a row shows even when no run lists the feature, e.g. a run that just changed). */
function billed(v: number | undefined): boolean {
  return typeof v === "number" && v > 0;
}

/** A money value cell (StableWidth of the shared samples). */
function Cell({ samples, children }: { samples: NumberSamples; children: JSX.Element | string }): JSX.Element {
  return <StableWidth samples={samples.money} align="end">{children}</StableWidth>;
}

/** A cost, shown negated. Unknown (negative) -> "–". */
function costValue(v: number): JSX.Element | string {
  return Number.isFinite(v) && v >= 0 ? <SignedNum value={-v} unit={Unit.Money} /> : DASH;
}

/** An income row's value as sent. Unknown (negative) -> "–". */
function incomeValue(v: number): JSX.Element | string {
  return Number.isFinite(v) && v >= 0 ? <SignedNum value={v} unit={Unit.Money} /> : DASH;
}

/** The features this area's runs have: natural, groomed, guns on, lights on, cat track with guns on.
 *  The Runs tooltip lists only their rows (R3). */
interface RunFeatureSet { natural: boolean; groomed: boolean; guns: boolean; lights: boolean; tracks: boolean }

function featuresOf(runs: readonly RunRow[]): RunFeatureSet {
  const f: RunFeatureSet = { natural: false, groomed: false, guns: false, lights: false, tracks: false };
  for (const r of runs) {
    if (!r) continue;
    if (r.kind === RUN_KIND.Natural) f.natural = true;
    else if (r.kind === RUN_KIND.CatTrack) f.tracks ||= r.gunSpacingM > 0;
    else {
      f.groomed = true;
      f.guns ||= r.gunSpacingM > 0;
      f.lights ||= r.lightSpacingM > 0;
    }
  }
  return f;
}

/** The FINANCES header with its Info glyph: the column is "<name> so far", "(partial)" while counted since a load. */
function MoneyHeader({ l, month, samples, t }: { l: ResortLive | null; month: string; samples: NumberSamples; t: Translate }): JSX.Element {
  return (
    <InfoLine
      header
      info
      label={t(HUB_LOC.sectionMoney)}
      value={<Cell samples={samples}>
        {l && l.thisMonthSinceLoad
          ? <LocText entry={HUB_FMT.moneyColMonthPartial} args={{ MONTH: month }} />
          : <LocText entry={HUB_FMT.moneyColMonth} args={{ MONTH: month }} />}
      </Cell>}
      tip={<TipLines lines={[t(HUB_LOC.moneyNotePanel), l && l.netEstimated && t(HUB_LOC.moneyNoteEst)]} />}
    />
  );
}

/** The line items C# sent (every lift type that has a line tile), table order. */
function sentLines(build: readonly BuildItem[]): Array<{ id: BuildItemId; liftType: string }> {
  const out: Array<{ id: BuildItemId; liftType: string }> = [];
  for (const m of BUILD_ITEMS) if (m.kind === "line" && findBuildItem(build, m.id)) out.push({ id: m.id, liftType: m.liftType });
  return out;
}

/** The lifts row: the lift charge booked this month. Its tooltip has today's rate and the per-type rates. */
function LiftsRow({ row, l, build, samples, t }: {
  row: ResortRow; l: ResortLive | null; build: readonly BuildItem[]; samples: NumberSamples; t: Translate;
}): JSX.Element {
  const billed = l ? l.liftsBilled : -1;
  const reduced = l ? l.liftsBilledReduced : -1;
  const FULL = billed >= 0 && reduced >= 0 ? numArg(billed - reduced, Unit.Integer) : DASH;
  const REDUCED = numArg(reduced, Unit.Integer);
  const lines = sentLines(build);
  const booked = <LocText key="r" entry={HUB_FMT.moneyTipLiftsRate} args={{ RATE: numArg(l ? l.liftCostMonth : -1, Unit.Money) }} />;
  // One type -> today's text with its rates. Two or more -> the per-type lines.
  const tip = lines.length >= 2
    ? <TipLines lines={[
      booked,
      <LocText key="l" entry={HUB_FMT.moneyTipLiftsTypes} args={{ FULL, REDUCED }} />,
      ...lines.map((x) => (
        <LocText key={x.id} entry={HUB_FMT.moneyTipLiftRate} args={{
          TYPE: LIFT_TYPE[x.liftType] ? t(LIFT_TYPE[x.liftType]) : x.liftType,
          BASE: rate(build, x.id, "monthly"), PERKM: rate(build, x.id, "monthlyPerKm"),
        }} />
      )),
    ]} />
    : <TipLines lines={[
      booked,
      <LocText key="l" entry={HUB_FMT.moneyTipLifts} args={{
        BASE: rate(build, lines.length === 1 ? lines[0].id : "lift", "monthly"),
        PERKM: rate(build, lines.length === 1 ? lines[0].id : "lift", "monthlyPerKm"),
        FULL, REDUCED,
      }} />,
    ]} />;
  return (
    <InfoLine
      sub
      label={<LocText entry={HUB_FMT.moneyRowLifts} args={{ N: numArg(billed, Unit.Integer), LEN: numArg(row.liftLengthM, Unit.Length) }} />}
      value={<Cell samples={samples}>{costValue(l ? l.liftChargedMonth : -1)}</Cell>}
      tip={tip}
    />
  );
}

/** The coach bill booked this month, and the depots' fact row under it (netRowShown: a depot stands or the row holds money). */
function BusRow({ l, city, samples, t }: { l: ResortLive | null; city: CityStats; samples: NumberSamples; t: Translate }): JSX.Element | null {
  if (!l || !netRowShown("bus", l, city)) return null;
  return (
    <>
      <InfoLine
        sub
        label={t(HUB_LOC.moneyRowBus)}
        value={<Cell samples={samples}>{costValue(l.busChargedMonth)}</Cell>}
        tip={t(HUB_LOC.moneyTipBusBooked)}
      />
      <SubLine>
        <LocText entry={HUB_FMT.moneyFactBus} args={{
          N: numArg(l.depots, Unit.Integer),
          GUESTS: l.busGuestsMonth >= 0 ? numArg(l.busGuestsMonth, Unit.Integer) : DASH,
          RATE: numArg(l.busBillMonth, Unit.MoneyPerMonth),
        }} />
      </SubLine>
    </>
  );
}

/** Run upkeep booked this month. The tooltip lists today's rate per month for each feature the area's runs have (or that bills now). */
function RunsRow({ l, feats, samples, t }: { l: ResortLive | null; feats: RunFeatureSet; samples: NumberSamples; t: Translate }): JSX.Element {
  const ha = (v: number | undefined): JSX.Element | string => numArg(v ?? -1, Unit.FloatSingleFraction);
  return (
    <InfoLine
      sub
      label={t(HUB_LOC.moneyRowRuns)}
      value={<Cell samples={samples}>{costValue(l ? l.runCostMonth : -1)}</Cell>}
      tip={<TipLines lines={[
        t(HUB_LOC.moneyTipRunsBooked),
        feats.natural && { label: <LocText entry={HUB_FMT.moneyRowNatural} args={{ HA: ha(l?.haNatural) }} />, value: <Num value={0} unit={Unit.Money} /> },
        (feats.groomed || billed(l?.costGroomingMonth)) && { label: <LocText entry={HUB_FMT.moneyRowGrooming} args={{ HA: ha(l?.haGroomed) }} />, value: costValue(l ? l.costGroomingMonth : -1) },
        (feats.guns || billed(l?.costSnowmakingMonth)) && { label: <LocText entry={HUB_FMT.moneyRowSnowmaking} args={{ HA: ha(l?.haSnowmaking) }} />, value: costValue(l ? l.costSnowmakingMonth : -1) },
        (feats.lights || billed(l?.costLightsMonth)) && { label: <LocText entry={HUB_FMT.moneyRowLights} args={{ HA: ha(l?.haLit) }} />, value: costValue(l ? l.costLightsMonth : -1) },
        (feats.tracks || billed(l?.costCatTracksMonth)) && { label: <LocText entry={HUB_FMT.moneyRowCatTracks} args={{ HA: ha(l?.haCatTracks) }} />, value: costValue(l ? l.costCatTracksMonth : -1) },
      ]} />}
    />
  );
}

/** Buildings / wages so far = today's rate × the share of the month simulated (C#'s rows, nothing multiplied here). */
function SoFarRow({ label, value, rateNow, l, rwh, samples, t, extra }: {
  label: string; value: number; rateNow: number; l: ResortLive | null; rwh: boolean; samples: NumberSamples; t: Translate; extra?: string;
}): JSX.Element {
  return (
    <InfoLine
      sub
      label={label}
      mark={value < 0 ? "≈" : null}
      value={<Cell samples={samples}>{costValue(value)}</Cell>}
      tip={<TipLines lines={[
        <LocText key="s" entry={HUB_FMT.moneyTipSoFar} args={{
          RATE: numArg(rateNow, Unit.Money), SHARE: l && l.monthShare >= 0 ? numArg(Math.round(l.monthShare * 100), Unit.Percentage) : DASH,
        }} />,
        <LocText key="b" entry={HUB_FMT.moneyTipBuildings} args={{
          UPKEEP: numArg(l ? l.buildingsMonth : -1, Unit.Money), WAGES: numArg(l ? l.wagesMonth : -1, Unit.Money),
          FILLED: numArg(l ? l.jobsFilled : -1, Unit.Integer), JOBS: numArg(l ? l.jobs : -1, Unit.Integer),
        }} />,
        rwh && t(HUB_LOC.moneyTipRwh),
        extra,
      ]} />}
    />
  );
}

/** Lodge spend booked this month (netRowShown: the setting is on or it holds money). */
function LodgeSpendRow({ l, city, samples, t }: { l: ResortLive | null; city: CityStats; samples: NumberSamples; t: Translate }): JSX.Element | null {
  if (!l || !netRowShown("lodge", l, city)) return null;
  return (
    <InfoLine
      sub
      label={t(HUB_LOC.moneyRowLodgeSpend)}
      value={<Cell samples={samples}>{incomeValue(l.lodgeIncomeMonth)}</Cell>}
      tip={t(HUB_LOC.moneyTipLodgeSpend)}
    />
  );
}

/** Season passes sold this month (pre-season sales land in their sale month). The tooltip shows the pre-season part. */
function PassesMonthRow({ l, city, samples, t }: { l: ResortLive | null; city: CityStats; samples: NumberSamples; t: Translate }): JSX.Element | null {
  if (!l || !netRowShown("passes", l, city)) return null;
  return (
    <InfoLine
      sub
      label={t(HUB_LOC.moneyRowPassesMonth)}
      value={<Cell samples={samples}>{incomeValue(l.passIncomeMonth)}</Cell>}
      tip={<TipLines lines={[
        <LocText key="p" entry={HUB_FMT.moneyTipPassesMonth} args={{ PRE: numArg(l.passIncomePreMonth, Unit.Money) }} />]} />}
    />
  );
}

/** The "Also" block's pass rows: the season's pass sales, the pre-season part, the pass holders skiing. */
function PassRows({ l, month, samples, t }: { l: ResortLive | null; month: string; samples: NumberSamples; t: Translate }): JSX.Element {
  const sold = l ? l.passesSoldSeason : -1;
  const holders = l ? l.passHoldersThisMonth : -1;
  return (
    <>
      <InfoLine
        label={<LocText entry={HUB_FMT.moneyRowPasses} args={{ N: numArg(sold, Unit.Integer) }} />}
        value={<Cell samples={samples}>{incomeValue(l ? l.passIncomeSeason : -1)}</Cell>}
        tip={t(HUB_LOC.moneyTipPasses)}
      />
      {presaleShown(l) && l && (
        <InfoLine
          sub
          label={<LocText entry={HUB_FMT.moneyRowPassesPre} args={{ N: numArg(l.passesPreSeason, Unit.Integer) }} />}
          value={<Cell samples={samples}>{incomeValue(l.passIncomePreSeason)}</Cell>}
          tip={t(HUB_LOC.moneyTipPassesPre)}
        />
      )}
      <InfoLine
        sub
        label={<LocText entry={HUB_FMT.moneyRowPassHolders} args={{ MONTH: month }} />}
        value={<Cell samples={samples}>{holders >= 0 ? <Num value={holders} unit={Unit.Integer} /> : DASH}</Cell>}
        tip={t(HUB_LOC.moneyTipPassHolders)}
      />
    </>
  );
}

/** The lessons booked this month, drawn once a lesson was taken or while there is no ski school (0 + flag). */
function LessonRow({ l, city, school, samples, t }: {
  l: ResortLive | null; city: CityStats; school: SchoolState; samples: NumberSamples; t: Translate;
}): JSX.Element | null {
  // Lessons need an installed, switched-on school (no green run): "none" and "off" are the 0s with a cause.
  if (!l || (!netRowShown("lessons", l, city) && school !== "none" && school !== "off")) return null;
  return (
    <InfoLine
      sub
      flag={school === "none" ? t(HUB_LOC.noSchool) : school === "off" ? t(HUB_LOC.schoolOff) : undefined}
      label={<LocText entry={HUB_FMT.moneyRowLessons} args={{ N: numArg(Math.max(0, l.lessonsThisMonth), Unit.Integer) }} />}
      value={<Cell samples={samples}>{incomeValue(l.lessonIncomeMonth)}</Cell>}
      tip={t(HUB_LOC.moneyTipLessons)}
    />
  );
}

/** The resort's share of the city's tourist draw x the game's TouristIncome statistic. It is an estimate in the "Also" block (not
 *  resort income, not in the net). Under Tourism Overhaul the same Detected note as the tourist estimate replaces the row.
 *  The label carries no "≈": the mark slot shows it next to the value, as on the net row (DECISIONS 568). */
function CityIncomeRow({ l, city, samples, t }: { l: ResortLive | null; city: CityStats; samples: NumberSamples; t: Translate }): JSX.Element | null {
  if (city.tourismOverhaul) return <SubLine tip={t(HUB_LOC.estToTip)}>{t(HUB_LOC.estToNote)}</SubLine>;
  if (!l || !cityIncomeShown(l, city)) return null;
  return (
    <InfoLine
      label={t(HUB_LOC.moneyRowCityIncome)}
      mark="≈"
      value={<Cell samples={samples}><SignedNum value={l.cityIncomeEst} unit={Unit.Money} /></Cell>}
      tip={t(HUB_LOC.moneyTipCityIncome)}
    />
  );
}

function NetRow({ l, samples, t }: { l: ResortLive | null; samples: NumberSamples; t: Translate }): JSX.Element {
  return (
    <InfoLine
      label={t(HUB_LOC.moneyNet)}
      mark={l && l.netEstimated ? "≈" : null}
      value={<Cell samples={samples}>{l ? <SignedNum value={l.netMonth} unit={Unit.Money} /> : DASH}</Cell>}
      tip={t(HUB_LOC.moneyTipNet)}
    />
  );
}

/** The lift tickets booked this month (ticketsThisMonth, the Net's first row). "off" keeps its label. */
function TicketsRow({ l, price, samples, t }: { l: ResortLive | null; price: number; samples: NumberSamples; t: Translate }): JSX.Element {
  const off = l ? ticketsRow(l, price) === "off" : false;
  return (
    <InfoLine
      sub
      label={off ? t(HUB_LOC.moneyTicketsOff) : t(HUB_LOC.moneyRowTickets)}
      value={<Cell samples={samples}>{incomeValue(l ? l.ticketsThisMonth : -1)}</Cell>}
      tip={<TipLines lines={[
        t(HUB_LOC.moneyTicketsTip),
        t(HUB_LOC.guestsTipTickets),   // where the money goes (was the Guests view's Ticket money row, DECISIONS 613)
        <LocText key="p" entry={HUB_FMT.moneyTicketsPrice} args={{ PRICE: numArg(price, Unit.Money) }} />,
      ]} />}
    />
  );
}

/** Under the tickets row, the Options price and what it does to ski days (the factor as a whole percent,
 *  hubRules.priceFactorPct). The line is read-only. The tooltip says where to change it.
 *  `price` = hubRules.hubPrice(city): -1 ("–") until C# sends the city. One text node (check-bundle rule R13). */
function PriceLine({ price, tolerance, t }: { price: number; tolerance: number; t: Translate }): JSX.Element {
  // The area's lodges' price tolerance (ResortLive.priceTolerance, -1 unknown reads as 0).
  const tol = tolerance > 0 ? tolerance : 0;
  const pct = priceFactorPct(price, tol);
  const ex = (i: number): { P: JSX.Element | string; F: JSX.Element | string } => ({
    P: numArg(PRICE_EXAMPLES[i], Unit.Money), F: numArg(priceFactorPct(PRICE_EXAMPLES[i]), Unit.Percentage),
  });
  const [a, b, c] = [ex(0), ex(1), ex(2)];
  return (
    <SubLine tip={<TipLines lines={[
      t(HUB_LOC.moneyPriceTitle),
      t(HUB_LOC.moneyPriceTipWhere),
      <LocText key="c" entry={HUB_FMT.moneyPriceTipCurve} args={{
        FULL: numArg(PRICE_NO_EFFECT_MAX, Unit.Money), P1: a.P, F1: a.F, P2: b.P, F2: b.F, P3: c.P, F3: c.F,
      }} />,
      tol > 0 && <LocText key="t" entry={HUB_FMT.moneyPriceTipTolerance} args={{ FULL: numArg(priceFullWith(tol), Unit.Money) }} />,
      t(HUB_LOC.moneyPriceTipWho),
    ]} />}>
      {pct < 0 ? DASH : <LocText entry={HUB_FMT.moneyPriceLine} args={{ PRICE: numArg(price, Unit.Money), PCT: numArg(pct, Unit.Percentage) }} />}
    </SubLine>
  );
}

/** The "Also" block under the Net (its own header): money that is not in this month's net. Nothing to show -> no block. */
function AlsoBlock({ l, city, samples, t }: { l: ResortLive | null; city: CityStats; samples: NumberSamples; t: Translate }): JSX.Element | null {
  const last = !!l && ticketsRow(l, city.ticketPrice) === "last";
  const passes = passesShown(l, city);
  const cityRow = city.tourismOverhaul || cityIncomeShown(l, city);
  if (!last && !passes && !cityRow) return null;
  return (
    <Fold view="money" id="also" header label={t(HUB_LOC.sectionAlso)}>
      {last && l && (
        <InfoLine
          label={<LocText entry={HUB_FMT.moneyTicketsLast} args={{ MONTH: monthName(city.month, t, 1), N: numArg(l.ticketPaidMonth, Unit.Integer) }} />}
          value={<Cell samples={samples}>{incomeValue(l.ticketIncomeMonth)}</Cell>}
        />
      )}
      {passes && <PassRows l={l} month={monthName(city.month, t)} samples={samples} t={t} />}
      <CityIncomeRow l={l} city={city} samples={samples} t={t} />
    </Fold>
  );
}

function SignMoney({ row, l, city, build, feats, problems, samples, t }: {
  row: ResortRow; l: ResortLive | null; city: CityStats; build: readonly BuildItem[]; feats: RunFeatureSet;
  problems: readonly ProblemRow[]; samples: NumberSamples; t: Translate;
}): JSX.Element {
  const month = monthName(city.month, t);
  return (
    <>
      <MoneyHeader l={l} month={month} samples={samples} t={t} />
      <Fold view="money" id="income" header label={t(HUB_LOC.sectionIncome)}
        value={<Cell samples={samples}>{incomeValue(l ? l.incomeMonth ?? -1 : -1)}</Cell>}>
      <TicketsRow l={l} price={city.ticketPrice} samples={samples} t={t} />
      <PriceLine price={hubPrice(city)} tolerance={l ? l.priceTolerance : -1} t={t} />
      <LodgeSpendRow l={l} city={city} samples={samples} t={t} />
      <LessonRow l={l} city={city} school={schoolState(l, problems, row.sign, schoolBuiltOf(build))} samples={samples} t={t} />
      <PassesMonthRow l={l} city={city} samples={samples} t={t} />
      </Fold>
      <Fold view="money" id="costs" header label={t(HUB_LOC.sectionCosts)}
        value={<Cell samples={samples}>{costValue(l ? l.costsMonth ?? -1 : -1)}</Cell>}>
      <LiftsRow row={row} l={l} build={build} samples={samples} t={t} />
      <RunsRow l={l} feats={feats} samples={samples} t={t} />
      <BusRow l={l} city={city} samples={samples} t={t} />
      <SoFarRow label={t(HUB_LOC.moneyRowBuildings)} value={l ? l.buildingsSoFar : -1} rateNow={l ? l.buildingsMonth : -1} l={l}
        rwh={city.realisticWorkplaces} samples={samples} t={t} />
      <SoFarRow label={t(HUB_LOC.moneyRowWages)} value={l ? l.wagesSoFar : -1} rateNow={l ? l.wagesMonth : -1} l={l}
        rwh={city.realisticWorkplaces} samples={samples} t={t} extra={t(HUB_LOC.moneyTipOffSeason)} />
      {city.realisticWorkplaces && <SubLine tip={t(HUB_LOC.moneyTipRwh)}>{t(HUB_LOC.moneyNoteRwh)}</SubLine>}
      </Fold>
      <Divider />
      <NetRow l={l} samples={samples} t={t} />
      <AlsoBlock l={l} city={city} samples={samples} t={t} />
    </>
  );
}

/** Not linked: Net = -(lift charge + run upkeep booked for Not linked). The Runs row shows only while it holds money (so the rows
 *  still sum to the Net). */
function UnlinkedMoney({ row, l, city, build, samples, t }: {
  row: ResortRow; l: ResortLive | null; city: CityStats; build: readonly BuildItem[]; samples: NumberSamples; t: Translate;
}): JSX.Element {
  return (
    <>
      <MoneyHeader l={l} month={monthName(city.month, t)} samples={samples} t={t} />
      <LiftsRow row={row} l={l} build={build} samples={samples} t={t} />
      {l && l.runCostMonth > 0 && <RunsRow l={l} feats={featuresOf([])} samples={samples} t={t} />}
      <Divider />
      <NetRow l={l} samples={samples} t={t} />
      <Divider />
      <div className={styles.noteArea}>
        <div className={styles.note2}>{t(HUB_LOC.unlinkedMoney)}</div>
      </div>
    </>
  );
}

export function MoneyView(): JSX.Element {
  const t = useT();
  const samples = useNumberSamples();
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const live = useValue(resortLive$);
  const city = useValue(city$);
  const build = useValue(build$);
  const runs = useValue(runs$);
  const problems = useValue(problems$);  // the school's state (hubRules.schoolState) for the Lessons row

  const cur = currentResort(resortEntries(resorts), selected);
  const row = cur.row;
  if (!row) return <div className={styles.dimLine}>{DASH}</div>;
  const l = liveOf(live, cur.isUnlinked ? null : row.sign);
  if (cur.isUnlinked) return <UnlinkedMoney row={row} l={l} city={city} build={build} samples={samples} t={t} />;
  return <SignMoney row={row} l={l} city={city} build={build} feats={featuresOf(runsOf(runs, row.sign))} problems={problems} samples={samples} t={t} />;
}
