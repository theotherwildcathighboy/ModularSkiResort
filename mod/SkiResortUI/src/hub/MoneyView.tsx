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
 * 15 text lines in 17 slot lines -> 10 text lines in 10.
 * Contract:
 *   - A sign, in this order: MONEY header with the Info glyph right after the label and "A month" in
 *     the value column (tooltip: MONEY_NOTE[Panel]; MONEY_NOTE[Est] while tickets are projected); the
 *     tickets row (label by hubRules.ticketsRow: off / last month's / estimated / counted after a quarter
 *     month - that label is why MONEY_NOTE[Unknown] is retired; "≈" while projected or not known; the
 *     label carries no "× price", the price now is a tooltip line); RUNNING COSTS with one row per run
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
 *   - Panel file (gate R11): subscribes resorts$, selectedResort$, resortLive$, city$, build$, runs$
 *     (Round F7: the run kinds of the area).
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { RUN_KIND, build$, city$, resortLive$, resorts$, runs$, selectedResort$ } from "../bindings";
import type { BuildItem, BuildItemId, CityStats, ResortLive, ResortRow, RunRow } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import { findBuildItem } from "./buildItems";
import { buildingsAndWages, incomeNote, liveOf, ticketsRow } from "./hubRules";
import type { IncomeNote } from "./hubRules";
import { HUB_FMT, HUB_LOC } from "./hubText";
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

function LiftsRow({ row, l, build, samples }: {
  row: ResortRow; l: ResortLive | null; build: readonly BuildItem[]; samples: NumberSamples;
}): JSX.Element {
  const billed = l ? l.liftsBilled : -1;
  const reduced = l ? l.liftsBilledReduced : -1;
  return (
    <InfoLine
      sub
      label={<LocText entry={HUB_FMT.moneyRowLifts} args={{ N: numArg(billed, Unit.Integer), LEN: numArg(row.liftLengthM, Unit.Length) }} />}
      value={<Cell samples={samples}>{costValue(l ? l.liftCostMonth : -1)}</Cell>}
      tip={<TipLines lines={[
        <LocText key="l" entry={HUB_FMT.moneyTipLifts} args={{
          BASE: rate(build, "lift", "monthly"), PERKM: rate(build, "lift", "monthlyPerKm"),
          FULL: billed >= 0 && reduced >= 0 ? numArg(billed - reduced, Unit.Integer) : DASH,
          REDUCED: numArg(reduced, Unit.Integer),
        }} />,
      ]} />}
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
      <LiftsRow row={row} l={l} build={build} samples={samples} />
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
      <LiftsRow row={row} l={l} build={build} samples={samples} />
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
