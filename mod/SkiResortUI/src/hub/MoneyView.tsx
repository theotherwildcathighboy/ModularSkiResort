/*
 * src/hub/MoneyView.tsx - the Money view (Money tile): ticket income, running costs, the game's bill
 * for buildings and wages, and the one net a month.
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/
 * spec/ch6-hub-ui.md section 6.11, design hub-v3-design.md 3.5, 6.9; D22-D26, D34 honest labels;
 * the net is C#'s HubRules.NetMonth, e1-spec R7 - the UI adds nothing up). Round F7 (2026-10-04,
 * implementer 4; docs/research/field-test-7-fixes.md D2, D3; findings/hub-ux.md §3.2, §4 with its
 * Verification): the note area is gone - its sentences moved into the header's Info tooltip - the
 * Realistic Workplaces line reads as a detection under the row it qualifies, and run-type rows show only
 * for the kinds the area has. Field test 7 state (RWH detected, tickets not known yet, no night run):
 * 15 text lines in 17 slot lines -> 10 text lines in 10. Round M2 (2026-10-10, builder B4; ruling R7,
 * docs/research/round-m2/understand/hub-price.md section 2 item 2, build-contract.md section 8.2): the
 * price line under the tickets row -> 11 text lines in 11.
 * Contract:
 *   - A sign, in this order: MONEY header with the Info glyph right after the label and "A month" in
 *     the value column (tooltip: MONEY_NOTE[Panel]; MONEY_NOTE[Est] while tickets are projected); the
 *     tickets row (label by hubRules.ticketsRow: off / last month's / estimated / counted after a quarter
 *     month - that label is why MONEY_NOTE[Unknown] is retired; "≈" while projected or not known; the
 *     label carries no "× price", the price now is a tooltip line); Round M2 R7: under it ONE dim SubLine
 *     MONEY_PRICE_LINE "Lift ticket {PRICE} · ski days {PCT}" (PRICE = hubRules.hubPrice(city), Money;
 *     PCT = hubRules.priceFactorPct, Percentage: D16's factor as a whole percent, "Lift ticket ¢40 · ski
 *     days 100 %"; "–" while the city is not known), read-only, always present for a sign (D33), its
 *     tooltip MONEY_PRICE_TIP[Title|Where|Curve|Who] (where to change it in Options, the curve at
 *     PRICE_EXAMPLES, who reacts); Not linked has none (it sells no tickets); RUNNING COSTS with one row per run
 *     kind the area has at least one run of (natural ¢0 dim, groomed, snowmaking, night; runs$ by kind:
 *     a row appears or vanishes only when a run is drawn or deleted, a state change, D33), the lifts row
 *     and the buildings-and-wages row ("–" and "≈" while the game's bill is not known); while Realistic
 *     Workplaces is detected (a session state) one dim SubLine under it, MONEY_NOTE[Rwh] (tooltip
 *     MONEY_TIP[Rwh], also a line of the buildings row's tooltip); a divider; the net ("≈" while
 *     estimated).
 *   - Not linked: the header (Info tooltip: MONEY_NOTE[Panel]), the lifts row, the net (C#:
 *     -liftCostMonth) and UNLINKED_MONEY (a 2-line note slot).
 *   - Every value cell is a StableWidth with the shared signed-money samples (aligned columns, no
 *     jitter); every row that can be estimated renders a MarkSlot (fixed width, empty keeps it).
 *     Costs arrive positive and are shown negated; a negative (unknown) cost shows "–", never "+".
 *   - Rates in the tooltips come from the CORE `build` items (runs: monthlyPerHa; lift: monthly,
 *     monthlyPerKm), "–" when a contract-2 DLL sends none.
 *   - Width (Overpass advances, design §2.3 method): "Detected: Realistic Workplaces. The real bill can
 *     differ." is XS 290.2 / 477.4 / 602.1 rem against the SubLine room W - 32 = 390 / 568 / 687: fits.
 *     The price line's widest form "Lift ticket ¢200 · ski days 20 %" (32 characters) is about 16 em XS
 *     by the 0.5 em estimate (GuestsView.tsx header): about 192 / 316 / 398 rem, fits (an estimate; the
 *     SubLine's ellipsis is the net).
 *   - Contract 8 (2026-10-15, econ-2 round 2; docs/research/econ-2/build/round-2.md): after the pass rows, LessonRow (MONEY_ROW[Lessons]
 *     "Lessons (this month) · {N}" with the lesson income so far as its value, only once a lesson was taken, hubRules.lessonsShown), a
 *     sub row MONEY_ROW[PassesPre] "Season passes sold before the season · {N}" under the season's passes (hubRules.presaleShown) and
 *     CityIncomeRow: "City income the resort brings (estimate)" = l.cityIncomeEst, marked "≈", NOT in the net; under Tourism Overhaul
 *     the row is replaced by the SubLine EST_TO_NOTE / EST_TO_TIP (the tourist estimate's pattern, GuestsView). The lesson and
 *     lodge-spend money is booked like tickets, so MONEY_NOTE[Fees] / MONEY_NOTE[Panel] now say "Resort income" / name all four.
 *   - Contract 7 (2026-10-14, season passes v1; docs/research/econ-2/build/season-pass-v1.md): under the tickets row, while city.bookTicketsAsFees
 *     (stage 1a), ONE dim SubLine MONEY_NOTE[Fees] "Resort income shows in the Economy panel under public transportation fees."; after the lodge-spend
 *     slot, only while hubRules.passesShown (the Options pass share above 0, or a pass sold / a holder skied): the income row MONEY_ROW[Passes]
 *     "Season passes sold (this season) · {N}" with the season's pass income as its value (MONEY_TIP[Passes]) and the count row
 *     MONEY_ROW[PassHolders] "Pass holders skiing (this month)" (MONEY_TIP[PassHolders]). The net is still C#'s tickets + lodge spend: the
 *     pass income is a season figure and is not in the monthly net.
 *   - Panel file (gate R11): subscribes resorts$, selectedResort$, resortLive$, city$, build$, runs$
 *     (Round F7: the run kinds of the area).
 *   - Contract 6 (2026-10-11, Catalog Phase B, code round 2, hub group; round-2-contract.md section 8.5): the price
 *     line's PCT is priceFactorPct(price, ResortLive.priceTolerance) (C# SkiDayRules.PriceFactor(price, tolerance):
 *     the counted lodges keep ski days full up to 40 + tolerance) and its tooltip adds MONEY_PRICE_TIP[Tolerance]
 *     while priceTolerance > 0; a "Lodge spend" income row (MONEY_ROW[LodgeSpend], tooltip MONEY_TIP[LodgeSpend])
 *     sits under it only while lodgeSpendMonth > 0 (hubRules.lodgeSpendShown: the default setting 0 draws nothing,
 *     so the view stays today's); the lifts row's tooltip lists each offered lift type's rates
 *     (MONEY_TIP[LiftsTypes] + MONEY_TIP_LIFT_RATE per line item C# sent) when two or more line items are sent, else
 *     today's MONEY_TIP[Lifts] with the fixed-grip line's rates. The net is still C#'s (tickets + lodge spend).
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { RUN_KIND, build$, city$, resortLive$, resorts$, runs$, selectedResort$ } from "../bindings";
import type { BuildItem, BuildItemId, CityStats, ResortLive, ResortRow, RunRow } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import { BUILD_ITEMS, findBuildItem } from "./buildItems";
import {
  PRICE_EXAMPLES, PRICE_NO_EFFECT_MAX, buildingsAndWages, cityIncomeShown, hubPrice, incomeNote, lessonsShown, liveOf, lodgeSpendShown,
  passesShown, presaleShown, priceFactorPct, priceFullWith, ticketsRow,
} from "./hubRules";
import type { IncomeNote } from "./hubRules";
import { HUB_FMT, HUB_LOC, LIFT_TYPE } from "./hubText";
import { Divider, InfoLine, LocText, Num, SignedNum, StableWidth, SubLine, TipLines, numArg, useNumberSamples } from "./parts";
import type { NumberSamples } from "./parts";
import { currentResort, resortEntries, runsOf } from "./resortData";
import styles from "./hub.module.scss";

const DASH = "–";

/** A rate of a build item for a tooltip argument ("–" when absent or unknown). */
function rate(build: readonly BuildItem[], id: BuildItemId, field: "monthlyPerHa" | "monthly" | "monthlyPerKm"): JSX.Element | string {
  const item = findBuildItem(build, id);
  const v = item ? item[field] : undefined;
  return typeof v === "number" ? numArg(v, Unit.Money) : DASH;
}

/** A money value cell (StableWidth of the shared samples). */
function Cell({ samples, children }: { samples: NumberSamples; children: JSX.Element | string }): JSX.Element {
  return <StableWidth samples={samples.money} align="end">{children}</StableWidth>;
}

/** A cost a month, shown negated; unknown (negative) -> "–". */
function costValue(v: number): JSX.Element | string {
  return Number.isFinite(v) && v >= 0 ? <SignedNum value={-v} unit={Unit.Money} /> : DASH;
}

/** Round F7 D2: the run kinds this area has at least one run of (Money shows only their rows). */
function kindsOf(runs: readonly RunRow[]): ReadonlySet<number> {
  const s = new Set<number>();
  for (const r of runs) if (r) s.add(r.kind);
  return s;
}

/** Round F7 D2: the MONEY header with its Info glyph; the moved notes are its tooltip. */
function MoneyHeader({ note, samples, t }: { note: IncomeNote; samples: NumberSamples; t: Translate }): JSX.Element {
  return (
    <InfoLine
      header
      info
      label={t(HUB_LOC.sectionMoney)}
      value={<Cell samples={samples}>{t(HUB_LOC.moneyCol)}</Cell>}
      tip={<TipLines lines={[t(HUB_LOC.sectionMoney), t(HUB_LOC.moneyNotePanel), note === "est" && t(HUB_LOC.moneyNoteEst)]} />}
    />
  );
}

/** Contract 6: the line items C# sent (every lift type that has a line tile), table order. */
function sentLines(build: readonly BuildItem[]): Array<{ id: BuildItemId; liftType: string }> {
  const out: Array<{ id: BuildItemId; liftType: string }> = [];
  for (const m of BUILD_ITEMS) if (m.kind === "line" && findBuildItem(build, m.id)) out.push({ id: m.id, liftType: m.liftType });
  return out;
}

function LiftsRow({ row, l, build, samples, t }: {
  row: ResortRow; l: ResortLive | null; build: readonly BuildItem[]; samples: NumberSamples; t: Translate;
}): JSX.Element {
  const billed = l ? l.liftsBilled : -1;
  const reduced = l ? l.liftsBilledReduced : -1;
  const FULL = billed >= 0 && reduced >= 0 ? numArg(billed - reduced, Unit.Integer) : DASH;
  const REDUCED = numArg(reduced, Unit.Integer);
  const lines = sentLines(build);
  // Contract 6: one type -> today's text with its rates; two or more -> the per-type lines.
  const tip = lines.length >= 2
    ? <TipLines lines={[
      <LocText key="l" entry={HUB_FMT.moneyTipLiftsTypes} args={{ FULL, REDUCED }} />,
      ...lines.map((x) => (
        <LocText key={x.id} entry={HUB_FMT.moneyTipLiftRate} args={{
          TYPE: LIFT_TYPE[x.liftType] ? t(LIFT_TYPE[x.liftType]) : x.liftType,
          BASE: rate(build, x.id, "monthly"), PERKM: rate(build, x.id, "monthlyPerKm"),
        }} />
      )),
    ]} />
    : <TipLines lines={[
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
      value={<Cell samples={samples}>{costValue(l ? l.liftCostMonth : -1)}</Cell>}
      tip={tip}
    />
  );
}

/** Contract 6 (D15): the lodge-spend income row, only while lodge spend earned money this month (lodgeSpendShown). */
function LodgeSpendRow({ l, samples, t }: { l: ResortLive | null; samples: NumberSamples; t: Translate }): JSX.Element | null {
  if (!l || !lodgeSpendShown(l)) return null;
  return (
    <InfoLine
      label={t(HUB_LOC.moneyRowLodgeSpend)}
      value={<Cell samples={samples}><SignedNum value={l.lodgeSpendMonth} unit={Unit.Money} /></Cell>}
      tip={t(HUB_LOC.moneyTipLodgeSpend)}
    />
  );
}

/** Contract 7 (season passes v1): the season's pass sales with their income, and the pass holders who skied this month. */
function PassRows({ l, samples, t }: { l: ResortLive | null; samples: NumberSamples; t: Translate }): JSX.Element {
  const sold = l ? l.passesSoldSeason : -1;
  const holders = l ? l.passHoldersThisMonth : -1;
  return (
    <>
      <InfoLine
        label={<LocText entry={HUB_FMT.moneyRowPasses} args={{ N: numArg(sold, Unit.Integer) }} />}
        value={<Cell samples={samples}>{l && l.passIncomeSeason >= 0 ? <SignedNum value={l.passIncomeSeason} unit={Unit.Money} /> : DASH}</Cell>}
        tip={t(HUB_LOC.moneyTipPasses)}
      />
      {presaleShown(l) && l && (
        <InfoLine
          sub
          label={<LocText entry={HUB_FMT.moneyRowPassesPre} args={{ N: numArg(l.passesPreSeason, Unit.Integer) }} />}
          value={<Cell samples={samples}>{l.passIncomePreSeason >= 0 ? <SignedNum value={l.passIncomePreSeason} unit={Unit.Money} /> : DASH}</Cell>}
          tip={t(HUB_LOC.moneyTipPassesPre)}
        />
      )}
      <InfoLine
        sub
        label={t(HUB_LOC.moneyRowPassHolders)}
        value={<Cell samples={samples}>{holders >= 0 ? <Num value={holders} unit={Unit.Integer} /> : DASH}</Cell>}
        tip={t(HUB_LOC.moneyTipPassHolders)}
      />
    </>
  );
}

/** Contract 8 (econ-2 round 2): the lessons this month with what they paid, drawn once a lesson was taken (lessonsShown). */
function LessonRow({ l, samples, t }: { l: ResortLive | null; samples: NumberSamples; t: Translate }): JSX.Element | null {
  if (!l || !lessonsShown(l)) return null;
  return (
    <InfoLine
      label={<LocText entry={HUB_FMT.moneyRowLessons} args={{ N: numArg(l.lessonsThisMonth, Unit.Integer) }} />}
      value={<Cell samples={samples}>{l.lessonIncomeMonth >= 0 ? <SignedNum value={l.lessonIncomeMonth} unit={Unit.Money} /> : DASH}</Cell>}
      tip={t(HUB_LOC.moneyTipLessons)}
    />
  );
}

/** Contract 8: the resort's share of the city's tourist draw x the game's TouristIncome statistic, labelled an estimate and kept out of the
 *  net; under Tourism Overhaul the row is replaced by the same Detected note as the tourist estimate (EST_TO_NOTE / EST_TO_TIP). */
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

function TicketsRow({ l, price, samples, t }: { l: ResortLive | null; price: number; samples: NumberSamples; t: Translate }): JSX.Element {
  const r = l ? ticketsRow(l, price) : "unknown";
  const N = numArg(l ? l.ticketPaidMonth : -1, Unit.Integer);
  let label: string | JSX.Element;
  switch (r) {
    case "off": label = t(HUB_LOC.moneyTicketsOff); break;
    case "last": label = <LocText entry={HUB_FMT.moneyTicketsLast} args={{ N }} />; break;
    case "est": label = <LocText entry={HUB_FMT.moneyTicketsEst} args={{ N }} />; break;
    default: label = t(HUB_LOC.moneyTicketsUnknown); break;
  }
  const value = r === "off" || r === "unknown" || !l ? DASH : <SignedNum value={l.ticketIncomeMonth} unit={Unit.Money} />;
  return (
    <InfoLine
      label={label}
      mark={r === "est" || r === "unknown" ? "≈" : null}
      value={<Cell samples={samples}>{value}</Cell>}
      tip={<TipLines lines={[
        t(HUB_LOC.moneyTicketsTip),
        <LocText key="p" entry={HUB_FMT.moneyTicketsPrice} args={{ PRICE: numArg(price, Unit.Money) }} />,
      ]} />}
    />
  );
}

/** Round M2 R7 (hub-price.md section 2 item 2): under the tickets row, the Options price and what it does to ski
 *  days (D16's factor as a whole percent, hubRules.priceFactorPct). Read-only; the tooltip says where to change it.
 *  `price` = hubRules.hubPrice(city): -1 ("–") until C# has sent the city. One text node (gate R13). */
function PriceLine({ price, tolerance, t }: { price: number; tolerance: number; t: Translate }): JSX.Element {
  // Contract 6: the area's lodges' price tolerance (ResortLive.priceTolerance; -1 unknown reads as 0).
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

function SignMoney({ row, l, city, build, kinds, samples, t }: {
  row: ResortRow; l: ResortLive | null; city: CityStats; build: readonly BuildItem[]; kinds: ReadonlySet<number>;
  samples: NumberSamples; t: Translate;
}): JSX.Element {
  const bw = l ? buildingsAndWages(l) : -1;
  const note = l ? incomeNote(l, city.ticketPrice) : null;
  const ha = (v: number | undefined): JSX.Element | string => numArg(v ?? -1, Unit.FloatSingleFraction);
  return (
    <>
      <MoneyHeader note={note} samples={samples} t={t} />
      <TicketsRow l={l} price={city.ticketPrice} samples={samples} t={t} />
      {city.bookTicketsAsFees && <SubLine>{t(HUB_LOC.moneyNoteFees)}</SubLine>}
      <PriceLine price={hubPrice(city)} tolerance={l ? l.priceTolerance : -1} t={t} />
      <LodgeSpendRow l={l} samples={samples} t={t} />
      {passesShown(l, city) && <PassRows l={l} samples={samples} t={t} />}
      <LessonRow l={l} samples={samples} t={t} />
      <CityIncomeRow l={l} city={city} samples={samples} t={t} />
      <InfoLine header label={t(HUB_LOC.sectionCosts)} />
      {kinds.has(RUN_KIND.Natural) && (
        <InfoLine
          sub
          label={<LocText entry={HUB_FMT.moneyRowNatural} args={{ HA: ha(l?.haNatural) }} />}
          value={<Cell samples={samples}><span className={styles.dimValue}><Num value={0} unit={Unit.Money} /></span></Cell>}
          tip={t(HUB_LOC.moneyTipNatural)}
        />
      )}
      {kinds.has(RUN_KIND.Groomed) && (
        <InfoLine
          sub
          label={<LocText entry={HUB_FMT.moneyRowGroomed} args={{ HA: ha(l?.haGroomed) }} />}
          value={<Cell samples={samples}>{costValue(l ? l.costGroomedMonth : -1)}</Cell>}
          tip={<LocText entry={HUB_FMT.moneyTipRuns} args={{ RATE: rate(build, "runGroomed", "monthlyPerHa") }} />}
        />
      )}
      {kinds.has(RUN_KIND.Snowmaking) && (
        <InfoLine
          sub
          label={<LocText entry={HUB_FMT.moneyRowSnowmaking} args={{ HA: ha(l?.haSnowmaking) }} />}
          value={<Cell samples={samples}>{costValue(l ? l.costSnowmakingMonth : -1)}</Cell>}
          tip={<LocText entry={HUB_FMT.moneyTipRuns} args={{ RATE: rate(build, "runSnowmaking", "monthlyPerHa") }} />}
        />
      )}
      {kinds.has(RUN_KIND.Night) && (
        <InfoLine
          sub
          label={<LocText entry={HUB_FMT.moneyRowNight} args={{ HA: ha(l?.haNight) }} />}
          value={<Cell samples={samples}>{costValue(l ? l.costNightMonth : -1)}</Cell>}
          tip={<LocText entry={HUB_FMT.moneyTipRuns} args={{ RATE: rate(build, "runNight", "monthlyPerHa") }} />}
        />
      )}
      <LiftsRow row={row} l={l} build={build} samples={samples} t={t} />
      <InfoLine
        sub
        label={t(HUB_LOC.moneyRowBuildings)}
        mark={bw < 0 ? "≈" : null}
        value={<Cell samples={samples}>{bw >= 0 ? <SignedNum value={-bw} unit={Unit.Money} /> : DASH}</Cell>}
        tip={<TipLines lines={[
          <LocText key="b" entry={HUB_FMT.moneyTipBuildings} args={{
            UPKEEP: numArg(l ? l.buildingsMonth : -1, Unit.Money), WAGES: numArg(l ? l.wagesMonth : -1, Unit.Money),
            FILLED: numArg(l ? l.jobsFilled : -1, Unit.Integer), JOBS: numArg(l ? l.jobs : -1, Unit.Integer),
          }} />,
          city.realisticWorkplaces && t(HUB_LOC.moneyTipRwh),
        ]} />}
      />
      {city.realisticWorkplaces && <SubLine tip={t(HUB_LOC.moneyTipRwh)}>{t(HUB_LOC.moneyNoteRwh)}</SubLine>}
      <Divider />
      <NetRow l={l} samples={samples} t={t} />
    </>
  );
}

function UnlinkedMoney({ row, l, build, samples, t }: {
  row: ResortRow; l: ResortLive | null; build: readonly BuildItem[]; samples: NumberSamples; t: Translate;
}): JSX.Element {
  return (
    <>
      <MoneyHeader note={null} samples={samples} t={t} />
      <LiftsRow row={row} l={l} build={build} samples={samples} t={t} />
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

  const cur = currentResort(resortEntries(resorts), selected);
  const row = cur.row;
  if (!row) return <div className={styles.dimLine}>{DASH}</div>;
  const l = liveOf(live, cur.isUnlinked ? null : row.sign);
  if (cur.isUnlinked) return <UnlinkedMoney row={row} l={l} build={build} samples={samples} t={t} />;
  return <SignMoney row={row} l={l} city={city} build={build} kinds={kindsOf(runsOf(runs, row.sign))} samples={samples} t={t} />;
}
