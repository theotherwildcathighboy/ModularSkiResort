/*
 * src/hub/RevenueChart.tsx - R3-A (1.0.3): the season's income and cost mix as ONE chart with the net and the passes' share of lift income.
 * Income bar and cost bar at ONE scale (the longer total fills the track), so the gap between their ends is the net: a dashed outline
 * on the income bar for a loss, on the cost bar for a profit. Variants A (two 100 % bars) and B (donuts) were dropped on the owner's pick (C).
 * Fed by historyRules.seasonSoFar (the season block already holds every number: no C# change, no contract field). The pass share is
 * passIncome / (ticketIncome + passIncome): the passes' part of the lift money (the PASS-3 "32 %" line).
 * Colors: the strongest part of each side is exactly the Finances chart legend's Income / Costs color (HISTORY_COLOR), the other parts
 * the same hue in fainter steps; the bar headers carry that legend swatch.
 * Gameface rules as the rest of the hub: flex only (no grid, no gap, no auto margins), HTML text, a CSS dashed border for the gap.
 * Rows render flat (fit.tsx pager).
 */
import type { ReactNode } from "react";
import { Unit } from "cs2/l10n";
import type { Translate } from "../l10n";
import { HISTORY_COLOR, HISTORY_RGB } from "./HistoryChart";
import { sharesPct } from "./historyRules";
import type { SeasonSoFar } from "./historyRules";
import { HUB_FMT, HUB_LOC } from "./hubText";
import { InfoLine, LocText, Num, SignedNum, Tip, TipLines, numArg } from "./parts";
import styles from "./hub.module.scss";

const SHADE = [0.8, 0.55, 0.38, 0.24] as const;   // the first = HISTORY_COLOR's alpha
const round = (v: number): number => Math.round(v);
const rgba = (rgb: string, a: number): string => `rgba(${rgb}, ${a})`;

interface Part { name: string; value: number; color: string; pct: number }
interface Mix { label: string; rgb: string; parts: Part[]; total: number }

function mixOf(label: string, rgb: string, items: ReadonlyArray<{ name: string; value: number }>): Mix {
  const vals = items.map((p) => (Number.isFinite(p.value) && p.value > 0 ? p.value : 0));
  const pct = sharesPct(vals);
  return {
    label, rgb, total: vals.reduce((a, b) => a + b, 0),
    parts: items.map((p, i) => ({ name: p.name, value: vals[i], color: rgba(rgb, SHADE[i] ?? 0.25), pct: pct[i] })),
  };
}

/** The passes' share of lift income (whole percent), or null without lift income: pass money / (pass money + ticket money). */
export function passSharePct(ticketIncome: number, passIncome: number): number | null {
  const t = Math.max(0, ticketIncome), p = Math.max(0, passIncome);
  return t + p > 0 ? Math.round((p * 100) / (t + p)) : null;
}

function partTip(m: Mix, p: Part): ReactNode {
  return <TipLines lines={[m.label, {
    label: p.name,
    value: <LocText entry={HUB_FMT.historyMixPart} args={{ AMOUNT: numArg(round(p.value), Unit.Money), PCT: numArg(p.pct, Unit.Percentage) }} />,
  }]} />;
}

/** One bar of parts. `widthPct` = the share of the track the filled part takes (the shared scale). */
function PartBar({ m, widthPct, children }: { m: Mix; widthPct: number; children?: ReactNode }): JSX.Element {
  return (
    <div className={styles.revTrack}>
      <div className={styles.revFilled} style={{ width: `${widthPct}%` }}>
        {m.parts.map((p) => (p.value > 0
          ? (
            <Tip key={p.name} tip={partTip(m, p)}>
              <div className={styles.revSeg} style={{ flexGrow: p.value, backgroundColor: p.color }}>
                {(p.pct * widthPct) / 100 >= 12 ? <Num value={p.pct} unit={Unit.Percentage} /> : null}   {/* a label only where it fits (share of the track) */}
              </div>
            </Tip>
          )
          : null))}
      </div>
      {children}
    </div>
  );
}

function PartLegend({ m }: { m: Mix }): JSX.Element {
  return (
    <div className={styles.revLegend}>
      {m.parts.map((p) => (
        <div key={p.name} className={styles.histLegendItem}>
          <div className={styles.histSwatch} style={{ backgroundColor: p.color }} />
          <div className={styles.histLegendText}>
            <LocText entry={HUB_FMT.historyMixTop} args={{ NAME: p.name, PCT: numArg(p.pct, Unit.Percentage) }} />
          </div>
        </div>
      ))}
    </div>
  );
}

/** A mix's header: the legend swatch of the Finances chart (the same color), its name and its total. */
function BarHead({ m, swatch }: { m: Mix; swatch: string }): JSX.Element {
  return (
    <div className={styles.revHead}>
      <div className={styles.histSwatch} style={{ backgroundColor: swatch }} />
      <div className={styles.revHeadLabel}>{m.label}</div>
      <div className={styles.revHeadValue}><Num value={round(m.total)} unit={Unit.Money} /></div>
    </div>
  );
}

/** The net as a line: "Net", its signed amount in the profit / loss color, and its share of income. */
function NetLine({ net, income, t }: { net: number; income: number; t: Translate }): JSX.Element {
  const loss = net < 0;
  const pct = income > 0 ? Math.round((net * 100) / income) : null;
  return (
    <div className={styles.revNet}>
      <div className={styles.revNetLabel}>{t(HUB_LOC.historyNet)}</div>
      <div className={styles.revNetValue} style={{ color: rgba(loss ? HISTORY_RGB.costs : HISTORY_RGB.income, 1) }}>
        <SignedNum value={round(net)} unit={Unit.Money} />
      </div>
      <div className={styles.revNetPct}>{pct === null ? "–" : <SignedNum value={pct} unit={Unit.Percentage} />}</div>
    </div>
  );
}

export function RevenueChart({ s, t }: { s: SeasonSoFar; t: Translate }): JSX.Element {
  const now = s.now;
  const income = mixOf(t(HUB_LOC.historyIncome), HISTORY_RGB.income, [
    { name: t(HUB_LOC.historyTickets), value: now.ticketIncome },
    { name: t(HUB_LOC.historyPasses), value: now.passIncome },
    { name: t(HUB_LOC.historyLodge), value: now.lodgeIncome },
    { name: t(HUB_LOC.historyLessons), value: now.lessonIncome },
  ]);
  const costs = mixOf(t(HUB_LOC.historyCosts), HISTORY_RGB.costs, [
    { name: t(HUB_LOC.historyLifts), value: now.liftCost },
    { name: t(HUB_LOC.historyRuns), value: now.runCost },
    { name: t(HUB_LOC.moneyRowBuildings), value: Math.max(0, s.buildings) },
    { name: t(HUB_LOC.moneyRowWages), value: Math.max(0, s.wages) },
  ]);
  const net = income.total - costs.total;
  const share = passSharePct(now.ticketIncome, now.passIncome);
  const none = income.total <= 0 && costs.total <= 0;
  const money = (v: number): JSX.Element => <Num value={round(Math.max(0, v))} unit={Unit.Money} />;

  const passLine = (
    <InfoLine
      label={share === null ? t(HUB_LOC.historyPasses) : <LocText entry={HUB_FMT.historyPassShare} args={{ PCT: numArg(share, Unit.Percentage) }} />}
      value={share === null ? "–" : undefined}
      tip={<TipLines lines={[
        t(HUB_LOC.historyPassShareTip),
        { label: t(HUB_LOC.historyPasses), value: money(now.passIncome) },
        { label: t(HUB_LOC.historyTickets), value: money(now.ticketIncome) },
      ]} />}
    />
  );

  // One scale: the longer total fills the track; the shorter bar ends early and the gap to the longer one is the net
  // (a dashed outline: the loss on the income bar, the profit on the cost bar).
  const scale = Math.max(income.total, costs.total);
  const wI = scale > 0 ? (income.total / scale) * 100 : 0, wC = scale > 0 ? (costs.total / scale) * 100 : 0;
  const netTip = (
    <TipLines lines={[
      t(HUB_LOC.historyNet),
      { label: t(HUB_LOC.historyIncome), value: money(income.total) },
      { label: t(HUB_LOC.historyCosts), value: <SignedNum value={-round(costs.total)} unit={Unit.Money} /> },
      { label: t(HUB_LOC.historyNet), value: <SignedNum value={round(net)} unit={Unit.Money} /> },
    ]} />
  );
  const gap = (side: "loss" | "profit"): JSX.Element | null => {
    const gw = side === "loss" ? 100 - wI : 100 - wC;
    if (!(gw > 0.5) || (side === "loss") !== (net < 0)) return null;
    const rgb = side === "loss" ? HISTORY_RGB.costs : HISTORY_RGB.income;
    return (
      <Tip tip={netTip}>
        <div className={styles.revGap} style={{ width: `${gw}%`, borderColor: rgba(rgb, 0.9), backgroundColor: rgba(rgb, 0.22) }}>
          <SignedNum value={round(net)} unit={Unit.Money} />
        </div>
      </Tip>
    );
  };

  return (
    <>
      <InfoLine header className={styles.pagerKeepTail} label={t(HUB_LOC.historyRevenueHead)} tip={t(HUB_LOC.historyRevenueTip)} />
      {none ? <div className={styles.histNote}>{"–"}</div> : (
        <>
          <div className={styles.revBlock}>
            <BarHead m={income} swatch={HISTORY_COLOR.income} />
            <PartBar m={income} widthPct={wI}>{gap("loss")}</PartBar>
            <PartLegend m={income} />
          </div>
          <div className={styles.revBlock}>
            <BarHead m={costs} swatch={HISTORY_COLOR.costs} />
            <PartBar m={costs} widthPct={wC}>{gap("profit")}</PartBar>
            <PartLegend m={costs} />
          </div>
          <NetLine net={net} income={income.total} t={t} />
        </>
      )}
      {passLine}
    </>
  );
}
