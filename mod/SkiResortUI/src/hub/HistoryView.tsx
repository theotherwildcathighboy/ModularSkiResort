/*
 * src/hub/HistoryView.tsx - the History view (History tile, after Finances): guests and money month by month, by ski season for
 * long ranges (T78 build 2, contract 10).
 *
 * Written for this repo (MIT). Created 2026-10-18 (T78 build 2; docs/research/ui/hub-history-build-2-2026-10-18.md section 3; plan
 * docs/research/ui/hub-history-chart-plan-2026-10-18.md section 4 with Richard's six answers; PIPELINE section 0 "Hub UI design rule").
 * Contract:
 *   - Top level (PIPELINE section 0: the chart and the number that matters): two control rows, then per chart ONE headline row - the
 *     last whole month's guests (Guests) or net (Finances) with its change on the same month a year earlier ("–" without one) -
 *     the chart, its axis and its legend. One layer down (tooltips): the headline's year-earlier figure, each control's meaning,
 *     and the hovered month's (or season's) numbers.
 *   - Controls (local values in hubState.ts, nothing sent): scope This ski area / Whole city (historyScope$; the city is the sum of
 *     the areas `history` carries, the live ones: a deleted area's history went with it, HISTORY_TIP[Scope]); charts Guests /
 *     Finances / Both (historyCharts$, default Both); range 12 months / This season vs last / By season (historyRange$, default 12 months;
 *     an option without enough months is disabled, historyRules.rangeEnabled, and the drawn range falls back to 12 months); the
 *     Attraction strip (historyAttraction$, off by default: Richard's answer 2). Snow is NOT drawn: the store has run coverage but no
 *     depth, and the hub never shows snow as one number (PIPELINE section 0; T18) - build note open question 1.
 *   - Guests chart: tourists + residents stacked, ski days as a line on the same scale. Finances chart: see B2 below. Buckets from
 *     historyRules.windowOf. The month in progress is the last bar, faded with a dashed outline; partial months faded.
 *   - Hover: one index shared by every chart of the view (the bars line up); each chart's tooltip lists its own series for it.
 *   - Not linked selected with scope "area": HISTORY[Unlinked] (the Not linked row has no sign, so no history); no data: HISTORY[Empty].
 *   - Panel file (gate R11): subscribes history$ (contract 10: ONLY this view, so C# builds `history` only while it is shown),
 *     resorts$, selectedResort$; local historyScope$, historyRange$, historyCharts$, historyAttraction$.
 * T82 UI round (2026-10-19; docs/research/ui/hub-design-review-2026-10-18.md B2, B3, B4, B7a; Richard "all seven, go"):
 *   - B2 Finances chart: income as ONE blue series above zero, costs as ONE red series below, the net as a line; the hover tooltip
 *     keeps every line (Income, its four parts, Costs, its three parts, Net). Legend: Income, Costs, Net.
 *   - B3: lines end at the last whole month (historyRules.linePoints); gap months hatched; the scale in a left gutter with "0" on
 *     the zero line (HistoryChart); the Net headline shows the change in money, the Guests headline the %, both followed by
 *     "vs <short month> <year>" (HISTORY_VS) naming the month they compare with.
 *   - B4 ranges: 12 months · This season vs last (this season's 12 months July-June in colour, last season's same months grey
 *     behind them, compareOf; the hover tip adds last season's figure) · By season (one bar per season = the average month over
 *     its whole months; tip: "{N} of 12 months · the average month" and the Season total). "5 years" and "All" are gone.
 *   - B7a: Whole city has no Attraction checkbox and no strip (each ski area has its own score; HISTORY_TIP[Scope] says so). In area
 *     scope the checkbox sits under the charts as the strip's header (beside the three ranges it wrapped to a third control row).
 *   - Headlines "Guests, January" / "Net, January" (HISTORY_HEAD: "Month of" dropped so the label, the value, the change and "vs Jan
 *     2026" fit one 365 rem row).
 */
import { useMemo, useState } from "react";
import type { ReactNode } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { Unit } from "cs2/l10n";
import { history$, resorts$, sameEntity, selectedResort$ } from "../bindings";
import type { HistoryArea } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry, Translate } from "../l10n";
import { BarChart, HISTORY_COLOR, Legend, LineStrip } from "./HistoryChart";
import type { ChartBehind, ChartSeries, ChartSlot } from "./HistoryChart";
import {
  HISTORY_CHARTS, HISTORY_RANGES, compareOf, effectiveRange, labelStep, linePoints, monthOfKey, netChange, niceMax, pctChange,
  rangeEnabled, sumAreas, windowOf, yearOfKey, yearOnYear,
} from "./historyRules";
import type { HistoryBucket, HistoryCharts, HistoryRange } from "./historyRules";
import { historyAttraction$, historyCharts$, historyRange$, historyScope$ } from "./hubState";
import { HUB_FMT, HUB_LOC, monthName, monthShortName } from "./hubText";
import { CheckBox, InfoLine, LocText, Num, SignedNum, StableWidth, Tip, TipLines, cx, numArg, useNumberSamples } from "./parts";
import type { NumberSamples, TipLine } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import styles from "./hub.module.scss";

const RANGE_LABEL: Readonly<Record<HistoryRange, LocEntry>> = {
  months12: HUB_LOC.historyRange12,
  seasonVs: HUB_LOC.historyRangeSeasons,
  bySeason: HUB_LOC.historyRangeBySeason,
};

const RANGE_TIP: Readonly<Record<HistoryRange, LocEntry>> = {
  months12: HUB_LOC.historyTipRange12,
  seasonVs: HUB_LOC.historyTipRangeSeasons,
  bySeason: HUB_LOC.historyTipRangeBySeason,
};

const CHARTS_LABEL: Readonly<Record<HistoryCharts, LocEntry>> = {
  guests: HUB_LOC.historyGuests,
  money: HUB_LOC.historyFinances,
  both: HUB_LOC.historyBoth,
};

/** Plot heights in rem: one chart alone, each of two, the attraction strip. Both charts + controls = 336 rem at 100 %, inside the
 *  341 rem content area the harness measured for Ash Flat with three problem lines (uiLayout); taller made the panel scroll. */
const H_ONE = 130, H_TWO = 64, H_STRIP = 26;

function asRange(v: string): HistoryRange {
  return (HISTORY_RANGES as readonly string[]).includes(v) ? (v as HistoryRange) : "months12";
}

function asCharts(v: string): HistoryCharts {
  return (HISTORY_CHARTS as readonly string[]).includes(v) ? (v as HistoryCharts) : "both";
}

const pad2 = (n: number): string => String(((n % 100) + 100) % 100).padStart(2, "0");

/** The bucket's name: "Month of January 2027" or "Ski season 2026/27". */
function bucketTitle(b: HistoryBucket, t: Translate): JSX.Element {
  if (b.kind === "season") return <LocText entry={HUB_FMT.historySeason} args={{ FROM: String(b.key), TO: pad2(b.key + 1) }} />;
  return <LocText entry={HUB_FMT.historyMonthYear} args={{ MONTH: monthName(monthOfKey(b.key), t), YEAR: String(yearOfKey(b.key)) }} />;
}

/** The axis labels: a short month (the year on January when the range spans more than a year), or "25/26" for a season. */
function axisLabels(buckets: readonly HistoryBucket[], t: Translate): string[] {
  const n = buckets.length;
  if (n === 0) return [];
  const step = labelStep(n, buckets[0].kind);
  return buckets.map((b, i) => {
    if (b.kind === "season" ? (n - 1 - i) % step !== 0 : monthOfKey(b.key) % step !== 0) return "";
    if (b.kind === "season") return `${pad2(b.key)}/${pad2(b.key + 1)}`;
    const m = monthOfKey(b.key);
    return n > 12 && m === 0 ? String(yearOfKey(b.key)) : monthShortName(m, t);
  });
}

/** The bucket's state line for its tooltip (null for a whole bucket). */
function stateLine(b: HistoryBucket, t: Translate): string | null {
  if (b.none && !b.current) return t(HUB_LOC.historyGap);
  if (b.current) return t(b.kind === "season" ? HUB_LOC.historyCurrentSeason : HUB_LOC.historyCurrent);
  if (b.partial) return t(b.kind === "season" ? HUB_LOC.historyPartialSeason : HUB_LOC.historyPartial);
  return null;
}

/** "{N} of 12 months · the average month" for a season drawn as its average (B4), else nothing. */
function perMonthLine(b: HistoryBucket): TipLine {
  return b.perMonth > 0 ? <LocText key="pm" entry={HUB_FMT.historySeasonMonths} args={{ N: numArg(b.perMonth, Unit.Integer) }} /> : null;
}

function moneyCell(v: number, samples: NumberSamples, signed = true): JSX.Element {
  return <StableWidth samples={samples.money} align="end">{signed ? <SignedNum value={v} unit={Unit.Money} /> : <Num value={v} unit={Unit.Money} />}</StableWidth>;
}

/** One segmented control (the Trees tool's idiom): a flat Button per option, the chosen one `buttonOn`. */
function Segment<K extends string>({ options, value, label, tip, enabled, onSelect }: {
  options: readonly K[];
  value: K;
  label: (k: K) => string;
  tip: (k: K) => ReactNode;
  enabled?: (k: K) => boolean;
  onSelect: (k: K) => void;
}): JSX.Element {
  return (
    <div className={styles.segment}>
      {options.map((k, i) => {
        const on = k === value;
        const ok = enabled ? enabled(k) : true;
        return (
          <Tip key={k} tip={tip(k)}>
            <Button
              variant="flat"
              className={cx(styles.segButton, styles.histSeg, i > 0 && styles.histSegNext, on && styles.buttonOn)}
              disabled={!ok}
              selected={on}
              onSelect={() => { if (!on && ok) onSelect(k); }}
            >
              {label(k)}
            </Button>
          </Tip>
        );
      })}
    </div>
  );
}

/** B4: a season drawn as its average month is faded only while it runs (the average already makes a partial season comparable;
 *  its tooltip still says "Partial" and "{N} of 12 months"). */
function slotsOf(buckets: readonly HistoryBucket[]): ChartSlot[] {
  return buckets.map((b) => ({ none: b.none, partial: b.perMonth > 0 ? b.current : b.partial, current: b.current, gap: b.gap }));
}

const pos = (v: number): number => (v > 0 ? v : 0);
const round = (v: number): number => Math.round(v);

export function HistoryView(): JSX.Element {
  const t = useT();
  const samples = useNumberSamples();
  const history = useValue(history$);
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const scope = useValue(historyScope$);
  const rangeRaw = useValue(historyRange$);
  const chartsRaw = useValue(historyCharts$);
  const attractionOn = useValue(historyAttraction$);
  const [hover, setHover] = useState(-1);
  const cur = currentResort(resortEntries(resorts), selected);
  const signRow = cur.row && !cur.isUnlinked ? cur.row : null;
  const area: HistoryArea | null = useMemo(() => {
    if (scope === "city") return sumAreas(history);
    if (!signRow) return null;
    return history.find((h) => h && sameEntity(h.sign, signRow.sign)) ?? null;
  }, [history, scope, signRow]);
  const range = effectiveRange(area, asRange(rangeRaw));
  const charts = asCharts(chartsRaw);
  const buckets = useMemo(() => windowOf(area, range), [area, range]);
  const compare = useMemo(() => compareOf(area, range), [area, range]);
  const yoy = useMemo(() => yearOnYear(area), [area]);
  // B7a: the attraction is per ski area; Whole city has no checkbox and no strip.
  const attraction = attractionOn && scope === "area";

  const scopeRow = (
    <div className={styles.histControls}>
      <Segment
        options={["area", "city"] as const}
        value={scope}
        label={(k) => t(k === "area" ? HUB_LOC.historyScopeArea : HUB_LOC.historyScopeCity)}
        tip={() => t(HUB_LOC.historyTipScope)}
        onSelect={(k) => historyScope$.update(k)}
      />
      <div className={styles.histSpacer} />
      <Segment
        options={HISTORY_CHARTS}
        value={charts}
        label={(k) => t(CHARTS_LABEL[k])}
        tip={(k) => t(CHARTS_LABEL[k])}
        onSelect={(k) => historyCharts$.update(k)}
      />
    </div>
  );
  const rangeRow = (
    <div className={styles.histControls}>
      <Segment
        options={HISTORY_RANGES}
        value={range}
        label={(k) => t(RANGE_LABEL[k])}
        tip={(k) => rangeEnabled(area, k) ? t(RANGE_TIP[k]) : <TipLines lines={[t(RANGE_TIP[k]), t(HUB_LOC.historyTipRangeOff)]} />}
        enabled={(k) => rangeEnabled(area, k)}
        onSelect={(k) => historyRange$.update(k)}
      />
    </div>
  );
  // B7a: the Attraction toggle, area scope only, at the bottom as the strip's own header (the range row holds the three ranges;
  // with the toggle beside them it wrapped to a third control row).
  const attractionToggle = scope === "area" ? (
    <div className={styles.histControls}>
      <Tip tip={t(HUB_LOC.historyTipAttraction)}>
        <Button variant="flat" className={cx(styles.segButton, styles.histSeg, styles.histCheck)} selected={attractionOn} onSelect={() => historyAttraction$.update(!attractionOn)}>
          <CheckBox on={attractionOn} />
          <div className={styles.histCheckText}>{t(HUB_LOC.historyAttraction)}</div>
        </Button>
      </Tip>
    </div>
  ) : null;

  if (!area || buckets.length === 0) {
    const note = scope === "area" && cur.isUnlinked ? HUB_LOC.historyUnlinked : HUB_LOC.historyEmpty;
    return (
      <div className={styles.histView}>
        {scopeRow}
        {rangeRow}
        <div className={styles.histNote}>{t(note)}</div>
      </div>
    );
  }

  const two = charts === "both";
  const plotH = two ? H_TWO : H_ONE;
  const slots = slotsOf(buckets);
  const labels = axisLabels(buckets, t);
  const b = (i: number): HistoryBucket => buckets[i];
  const hovered = hover >= 0 && hover < buckets.length ? buckets[hover] : null;
  const prev = compare && hover >= 0 && hover < compare.length ? compare[hover] : null;
  const head = (lines: TipLine[]): JSX.Element => <TipLines lines={lines} />;
  const nowName = yoy.now ? monthName(monthOfKey(yoy.now.key), t) : "–";
  const beforeLine = (v: number | null, unit: Unit): TipLine => yoy.before
    ? <LocText key="b" entry={HUB_FMT.historyTipBefore} args={{
      MONTH: monthName(monthOfKey(yoy.before.key), t), YEAR: String(yearOfKey(yoy.before.key)), N: v === null ? "–" : numArg(v, unit, unit === Unit.Money),
    }} />
    : t(HUB_LOC.historyGap);
  // B4 "This season vs last": the hovered month's figure a season earlier (or "Nothing recorded").
  const prevLine = (v: (x: HistoryBucket) => number, unit: Unit): TipLine => {
    if (!prev) return null;
    if (prev.none) return <LocText key="ls" entry={HUB_FMT.historyTipBefore} args={{
      MONTH: monthName(monthOfKey(prev.key), t), YEAR: String(yearOfKey(prev.key)), N: t(HUB_LOC.historyGap),
    }} />;
    return <LocText key="ls" entry={HUB_FMT.historyTipBefore} args={{
      MONTH: monthName(monthOfKey(prev.key), t), YEAR: String(yearOfKey(prev.key)), N: numArg(round(v(prev)), unit, unit === Unit.Money),
    }} />;
  };
  // B3 (F9): the headline names the month it compares with, "vs Jan 2026" (a year before the last whole month).
  const vsCell = yoy.now ? (
    <div className={styles.histVs}>
      <LocText entry={HUB_FMT.historyVs} args={{ MONTH: monthShortName(monthOfKey(yoy.now.key - 12), t), YEAR: String(yearOfKey(yoy.now.key - 12)) }} />
    </div>
  ) : null;
  const pctCell = (p: number | null): JSX.Element => (
    <div className={styles.histPct}>
      <StableWidth samples={samples.percent} align="end">{p === null ? "–" : <SignedNum value={p} unit={Unit.Percentage} />}</StableWidth>
    </div>
  );
  const changeCell = (c: number | null): JSX.Element => (
    <div className={styles.histPct}>{moneyCell(c === null ? NaN : c, samples)}</div>
  );

  // ---- scales (shared gutter width: every label of the view sizes every gutter) --------------------
  let gMax = 0, mUp = 0, mDown = 0, aMax = 0;
  const all = compare ? [...buckets, ...compare] : buckets;
  for (const x of all) {
    if (x.none) continue;
    gMax = Math.max(gMax, x.guests, x.skiDays);
    mUp = Math.max(mUp, x.income, x.net);
    mDown = Math.max(mDown, x.costs, -x.net);
  }
  for (const x of buckets) aMax = Math.max(aMax, x.attraction);
  const gTop = niceMax(gMax);
  const mTop = mUp > 0 ? niceMax(mUp) : 0, mBottom = mDown > 0 ? niceMax(mDown) : 0;
  const aTop = niceMax(aMax);
  const gTopLabel = <Num value={gTop} unit={Unit.Integer} />;
  const mTopLabel = <Num value={mTop} unit={Unit.Money} />;
  const mBottomLabel = <SignedNum value={-mBottom} unit={Unit.Money} />;
  const aTopLabel = <Num value={aTop} unit={Unit.Integer} />;
  const sizers: ReactNode[] = [
    charts !== "money" && gTopLabel, charts !== "guests" && mTop > 0 && mTopLabel, charts !== "guests" && mBottom > 0 && mBottomLabel,
    attraction && aTopLabel, "0",
  ].filter((x) => !!x) as ReactNode[];

  // ---- Guests -------------------------------------------------------------------------------------
  const guestUp: ChartSeries[] = [
    { key: "t", color: HISTORY_COLOR.tourists, value: (i) => b(i).tourists },
    { key: "r", color: HISTORY_COLOR.residents, value: (i) => b(i).residents },
  ];
  const skiPick = (x: HistoryBucket): number => (x.skiDays >= 0 ? x.skiDays : NaN);
  const skiLine = { key: "s", color: HISTORY_COLOR.skiDays, points: linePoints(buckets, skiPick) };
  const guestBehind: ChartBehind | null = compare
    ? { up: compare.map((x) => (x.none ? NaN : x.guests)), down: [], line: { key: "ps", color: HISTORY_COLOR.lastSeason, points: linePoints(compare, skiPick) } }
    : null;
  const guestTip = hovered
    ? head([bucketTitle(hovered, t), stateLine(hovered, t), perMonthLine(hovered),
      !hovered.none && { label: t(HUB_LOC.historyGuests), value: <Num value={round(hovered.guests)} unit={Unit.Integer} /> },
      !hovered.none && { label: t(HUB_LOC.historyTourists), value: <Num value={round(hovered.tourists)} unit={Unit.Integer} /> },
      !hovered.none && { label: t(HUB_LOC.historyResidents), value: <Num value={round(hovered.residents)} unit={Unit.Integer} /> },
      !hovered.none && { label: t(HUB_LOC.historySkiDays), value: hovered.skiDays >= 0 ? <Num value={round(hovered.skiDays)} unit={Unit.Integer} /> : "–" },
      hovered.perMonth > 0 && { label: t(HUB_LOC.historySeasonTotal), value: <Num value={round(hovered.totalGuests)} unit={Unit.Integer} /> },
      prevLine((x) => x.guests, Unit.Integer),
    ])
    : t(HUB_LOC.historyTipChart);
  const guestsBlock = (
    <>
      <InfoLine
        header
        label={yoy.now ? <LocText entry={HUB_FMT.historyHeadGuests} args={{ MONTH: nowName }} /> : t(HUB_LOC.historyGuests)}
        value={
          <div className={styles.histHeadValue}>
            <StableWidth samples={samples.count} align="end">{yoy.now ? <Num value={yoy.now.guests} unit={Unit.Integer} /> : "–"}</StableWidth>
            {pctCell(yoy.now && yoy.before ? pctChange(yoy.now.guests, yoy.before.guests) : null)}
            {vsCell}
          </div>
        }
        tip={head([t(HUB_LOC.historyTipYoy), beforeLine(yoy.before ? yoy.before.guests : null, Unit.Integer)])}
      />
      <BarChart slots={slots} up={guestUp} down={[]} line={skiLine} behind={guestBehind} scale={{ top: gTop, bottom: 0 }} height={plotH}
        hover={hover} onHover={setHover} topLabel={gTopLabel} bottomLabel={null} sizers={sizers} axis={labels} tip={guestTip} />
      <Legend items={[
        { key: "t", color: HISTORY_COLOR.tourists, label: t(HUB_LOC.historyTourists) },
        { key: "r", color: HISTORY_COLOR.residents, label: t(HUB_LOC.historyResidents) },
        { key: "s", color: HISTORY_COLOR.skiDays, label: t(HUB_LOC.historySkiDays), line: true },
        ...(compare ? [{ key: "ls", color: HISTORY_COLOR.lastSeason, label: t(HUB_LOC.historyLastSeason) }] : []),
      ]} />
    </>
  );

  // ---- Finances (B2: income up, costs down, the net as a line; every part in the tooltip) ----------
  const incomeUp: ChartSeries[] = [{ key: "i", color: HISTORY_COLOR.income, value: (i) => pos(b(i).income) }];
  const costsDown: ChartSeries[] = [{ key: "c", color: HISTORY_COLOR.costs, value: (i) => pos(b(i).costs) }];
  const netPick = (x: HistoryBucket): number => x.net;
  const netLine = { key: "n", color: HISTORY_COLOR.net, points: linePoints(buckets, netPick) };
  const moneyBehind: ChartBehind | null = compare
    ? {
      up: compare.map((x) => (x.none ? NaN : pos(x.income))), down: compare.map((x) => (x.none ? NaN : pos(x.costs))),
      line: { key: "pn", color: HISTORY_COLOR.lastSeason, points: linePoints(compare, netPick) },
    }
    : null;
  const money = (label: LocEntry, v: number): TipLine => ({ label: t(label), value: <SignedNum value={round(v)} unit={Unit.Money} /> });
  const moneyTip = hovered
    ? head([bucketTitle(hovered, t), stateLine(hovered, t), perMonthLine(hovered),
      !hovered.none && money(HUB_LOC.historyIncome, hovered.income),
      !hovered.none && money(HUB_LOC.historyTickets, hovered.ticketIncome),
      !hovered.none && money(HUB_LOC.historyLodge, hovered.lodgeIncome),
      !hovered.none && money(HUB_LOC.historyPasses, hovered.passIncome),
      !hovered.none && money(HUB_LOC.historyLessons, hovered.lessonIncome),
      !hovered.none && money(HUB_LOC.historyCosts, -hovered.costs),
      !hovered.none && money(HUB_LOC.historyLifts, -hovered.liftCost),
      !hovered.none && money(HUB_LOC.historyRuns, -hovered.runCost),
      !hovered.none && { label: t(HUB_LOC.historyStaff), value: hovered.staffCost >= 0 ? <SignedNum value={-round(hovered.staffCost)} unit={Unit.Money} /> : "–" },
      !hovered.none && money(HUB_LOC.historyNet, hovered.net),
      hovered.perMonth > 0 && money(HUB_LOC.historySeasonTotal, hovered.totalNet),
      prevLine((x) => x.net, Unit.Money),
    ])
    : head([t(HUB_LOC.historyTipChart), t(HUB_LOC.historyTipStaff)]);
  const moneyBlock = (
    <>
      <InfoLine
        header
        label={yoy.now ? <LocText entry={HUB_FMT.historyHeadNet} args={{ MONTH: nowName }} /> : t(HUB_LOC.historyNet)}
        value={
          <div className={styles.histHeadValue}>
            {moneyCell(yoy.now ? yoy.now.net : NaN, samples)}
            {changeCell(netChange(yoy))}
            {vsCell}
          </div>
        }
        tip={head([t(HUB_LOC.historyTipYoy), beforeLine(yoy.before ? yoy.before.net : null, Unit.Money)])}
      />
      <BarChart slots={slots} up={incomeUp} down={costsDown} line={netLine} behind={moneyBehind} scale={{ top: mTop, bottom: mBottom }}
        height={plotH} hover={hover} onHover={setHover} topLabel={mTopLabel} bottomLabel={mBottomLabel} sizers={sizers} axis={labels} tip={moneyTip} />
      <Legend items={[
        { key: "i", color: HISTORY_COLOR.income, label: t(HUB_LOC.historyIncome) },
        { key: "c", color: HISTORY_COLOR.costs, label: t(HUB_LOC.historyCosts) },
        { key: "n", color: HISTORY_COLOR.net, label: t(HUB_LOC.historyNet), line: true },
        ...(compare ? [{ key: "ls", color: HISTORY_COLOR.lastSeason, label: t(HUB_LOC.historyLastSeason) }] : []),
      ]} />
    </>
  );

  // ---- Attraction strip (own scale: never a second axis on a chart; area scope only, B7a) ----------
  const attrTip = hovered
    ? head([bucketTitle(hovered, t), stateLine(hovered, t),
      !hovered.none && { label: t(HUB_LOC.historyAttraction), value: hovered.attraction >= 0 ? <Num value={round(hovered.attraction)} unit={Unit.Integer} /> : "–" }])
    : t(HUB_LOC.historyTipAttraction);
  const attractionBlock = attraction ? (
    <>
      <LineStrip slots={slots} line={{ key: "a", color: HISTORY_COLOR.attraction, points: linePoints(buckets, (x) => (x.attraction >= 0 ? x.attraction : NaN)) }}
        top={aTop} height={H_STRIP} hover={hover} onHover={setHover} topLabel={aTopLabel} sizers={sizers} tip={attrTip} />
    </>
  ) : null;

  // T114: flat rows (no wrapper), so the view pager (fit.tsx) can put the money chart on a second page at a large text scale.
  return (
    <>
      {scopeRow}
      {rangeRow}
      {charts !== "money" && guestsBlock}
      {charts !== "guests" && moneyBlock}
      {attractionToggle}
      {attractionBlock}
    </>
  );
}
