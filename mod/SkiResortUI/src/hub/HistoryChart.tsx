/*
 * src/hub/HistoryChart.tsx - the History view's charts as inline SVG (T78 build 2, contract 10). React only: no chart.js
 * (plan docs/research/ui/hub-history-chart-plan-2026-10-18.md section 4.1: the harness stubs it, a game patch can change it,
 * the vanilla UI draws its small charts as React SVG too).
 *
 * Written for this repo (MIT). Created 2026-10-18 (T78 build 2; docs/research/ui/hub-history-build-2-2026-10-18.md section 3).
 * Contract:
 *   - Pure presentation of historyRules buckets: <BarChart> draws one slot per bucket. Stacked bars: `up` series above the zero line,
 *     `down` series below it (costs, shown positive in the data); one optional line (`line`: ski days or the net) through the
 *     slot centres, broken at buckets without a value. ONE axis (no dual scale): the line shares the bars' scale. Each <BarChart>
 *     is its own chart; the attraction strip is a separate <LineStrip> with its own scale.
 *   - Marks: bars 70 % of a slot (a slot gap of at least 2 rem between bars), a 2 rem surface gap between stacked segments, a
 *     line 2 rem wide. Partial buckets are drawn faded (fill opacity 0.45), the month in progress also with a dashed outline;
 *     empty buckets (`none`) draw nothing (a gap, never a zero). Colours: dataviz reference palette, dark steps, validated with
 *     the dataviz validator (2026-10-18): income blue / aqua / violet / green and guests blue / aqua pass every adjacent check;
 *     the costs are ONE red family in three lightness steps (three warm hues cannot pass the adjacent floors together).
 *   - SVG elements used: svg, rect, path, line (the ones vanilla renders through React; polyline is never used by vanilla).
 *     Coordinates are viewBox units = rem at 100 % (VIEW_W wide), preserveAspectRatio "none": the box fills the view's width.
 *     Text is HTML around the SVG (axis labels, the top value), never SVG <text>.
 *   - Hover: the plot's own div tracks the pointer (getBoundingClientRect), the bucket under it is highlighted and reported to
 *     onHover (-1 when the pointer leaves); the parent owns the tooltip.
 */
import type { ReactNode } from "react";
import { Tip, cx } from "./parts";
import styles from "./hub.module.scss";
import { bucketAt } from "./historyRules";

/** viewBox width (rem at 100 %: the view's inner width). */
export const VIEW_W = 400;

/** Series colours (dataviz reference palette, dark steps; header). */
export const HISTORY_COLOR = {
  tourists: "#3987e5",
  residents: "#199e70",
  skiDays: "#c98500",
  tickets: "#3987e5",
  lodge: "#199e70",
  passes: "#9085e9",
  lessons: "#008300",
  lifts: "#f08a8a",
  runs: "#d65a5a",
  staff: "#a33a3a",
  net: "#f0fbff",
  attraction: "#9085e9",
} as const;

export interface ChartSeries {
  key: string;
  color: string;
  /** The value of bucket i (>= 0; negative or non-finite draws nothing). */
  value: (i: number) => number;
}

export interface ChartSlot {
  none: boolean;
  partial: boolean;
  current: boolean;
}

/** The scale: the top value above zero and the bottom value below it, both >= 0 (one unit for both). */
export interface ChartScale {
  top: number;
  bottom: number;
}

function finite(v: number): boolean {
  return typeof v === "number" && Number.isFinite(v);
}

/** The pointer's bucket over a plot div (-1 outside). */
function pointerBucket(e: { clientX: number; currentTarget: EventTarget | null }, count: number): number {
  const el = e.currentTarget as HTMLElement | null;
  if (!el || typeof el.getBoundingClientRect !== "function") return -1;
  const r = el.getBoundingClientRect();
  if (!(r.width > 0)) return -1;
  return bucketAt((e.clientX - r.left) / r.width, count);
}

export function BarChart({ slots, up, down, line, scale, height, hover, onHover, topLabel, bottomLabel, tip }: {
  slots: readonly ChartSlot[];
  up: readonly ChartSeries[];
  down: readonly ChartSeries[];
  /** One line on the same scale (e.g. ski days, the net); null = none. Values below zero draw below the zero line. */
  line: ChartSeries | null;
  scale: ChartScale;
  /** Plot height in rem (viewBox units). */
  height: number;
  hover: number;
  onHover: (i: number) => void;
  /** The top / bottom value as text (HTML, dim, in the plot's corner). */
  topLabel: ReactNode;
  bottomLabel: ReactNode;
  /** The tooltip (the hovered bucket's numbers): on the plot's own div (parts.tsx Tip rule). */
  tip: ReactNode;
}): JSX.Element {
  const n = slots.length;
  const span = Math.max(1e-9, scale.top + scale.bottom);
  const zeroY = (scale.top / span) * height;
  const unit = height / span;
  const slotW = n > 0 ? VIEW_W / n : VIEW_W;
  const barW = Math.max(1, slotW * 0.7);
  const marks: JSX.Element[] = [];
  for (let i = 0; i < n; i++) {
    const s = slots[i];
    if (s.none) continue;
    const x = i * slotW + (slotW - barW) / 2;
    const opacity = s.partial || s.current ? 0.45 : 1;
    const dash = s.current ? "3 2" : undefined;
    const stack = (series: readonly ChartSeries[], dir: 1 | -1): void => {
      let edge = zeroY;
      for (const ser of series) {
        const v = ser.value(i);
        if (!finite(v) || v <= 0) continue;
        const h = v * unit;
        const gap = h > 3 ? 1 : 0;   // the 2 rem surface gap between segments, split over both sides
        const y = dir > 0 ? edge - h : edge;
        marks.push(
          <rect key={`${ser.key}${i}`} x={x} y={y + gap} width={barW} height={Math.max(0.5, h - 2 * gap)} fill={ser.color}
            fillOpacity={opacity} stroke={s.partial || s.current ? ser.color : "none"} strokeWidth={s.partial || s.current ? 1 : 0}
            strokeDasharray={dash} />,
        );
        edge = dir > 0 ? edge - h : edge + h;
      }
    };
    stack(up, 1);
    stack(down, -1);
  }
  let path = "";
  if (line) {
    let pen = false;
    for (let i = 0; i < n; i++) {
      const v = slots[i].none ? NaN : line.value(i);
      if (!finite(v)) { pen = false; continue; }
      const cx0 = i * slotW + slotW / 2;
      const y = zeroY - v * unit;
      path += `${pen ? "L" : "M"}${cx0.toFixed(1)} ${y.toFixed(1)} `;
      pen = true;
    }
  }
  const hoverX = hover >= 0 && hover < n ? hover * slotW : -1;
  return (
    <Tip tip={tip}>
    <div
      className={styles.histPlot}
      onMouseMove={(e) => onHover(pointerBucket(e, n))}
      onMouseLeave={() => onHover(-1)}
    >
      <svg className={styles.histSvg} viewBox={`0 0 ${VIEW_W} ${height}`} preserveAspectRatio="none" style={{ height: `${height}rem` }}>
        {hoverX >= 0 && <rect x={hoverX} y={0} width={slotW} height={height} fill="#ffffff" fillOpacity={0.08} />}
        <line x1={0} y1={0} x2={VIEW_W} y2={0} stroke="#ffffff" strokeOpacity={0.12} strokeWidth={1} />
        {scale.bottom > 0 && <line x1={0} y1={height} x2={VIEW_W} y2={height} stroke="#ffffff" strokeOpacity={0.12} strokeWidth={1} />}
        {marks}
        <line x1={0} y1={zeroY} x2={VIEW_W} y2={zeroY} stroke="#ffffff" strokeOpacity={0.45} strokeWidth={1} />
        {path !== "" && line && <path d={path} fill="none" stroke={line.color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />}
      </svg>
      <div className={styles.histTopLabel}>{topLabel}</div>
      {scale.bottom > 0 && <div className={styles.histBottomLabel}>{bottomLabel}</div>}
    </div>
    </Tip>
  );
}

/** A thin strip with one line on its own scale (0..top): the attraction overlay, a separate chart (no dual axis). */
export function LineStrip({ slots, series, top, height, hover, onHover, topLabel, tip }: {
  slots: readonly ChartSlot[];
  series: ChartSeries;
  top: number;
  height: number;
  hover: number;
  onHover: (i: number) => void;
  topLabel: ReactNode;
  tip: ReactNode;
}): JSX.Element {
  const n = slots.length;
  const slotW = n > 0 ? VIEW_W / n : VIEW_W;
  const unit = height / Math.max(1e-9, top);
  let path = "";
  let pen = false;
  const dots: JSX.Element[] = [];
  for (let i = 0; i < n; i++) {
    const v = slots[i].none ? NaN : series.value(i);
    if (!finite(v) || v < 0) { pen = false; continue; }
    const x = i * slotW + slotW / 2;
    const y = height - v * unit;
    path += `${pen ? "L" : "M"}${x.toFixed(1)} ${y.toFixed(1)} `;
    if (!pen) dots.push(<rect key={`d${i}`} x={x - 1.5} y={y - 1.5} width={3} height={3} fill={series.color} />);
    pen = true;
  }
  const hoverX = hover >= 0 && hover < n ? hover * slotW : -1;
  return (
    <Tip tip={tip}>
    <div className={cx(styles.histPlot, styles.histStrip)} onMouseMove={(e) => onHover(pointerBucket(e, n))} onMouseLeave={() => onHover(-1)}>
      <svg className={styles.histSvg} viewBox={`0 0 ${VIEW_W} ${height}`} preserveAspectRatio="none" style={{ height: `${height}rem` }}>
        {hoverX >= 0 && <rect x={hoverX} y={0} width={slotW} height={height} fill="#ffffff" fillOpacity={0.08} />}
        <line x1={0} y1={height} x2={VIEW_W} y2={height} stroke="#ffffff" strokeOpacity={0.45} strokeWidth={1} />
        {dots}
        {path !== "" && <path d={path} fill="none" stroke={series.color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />}
      </svg>
      <div className={styles.histTopLabel}>{topLabel}</div>
    </div>
    </Tip>
  );
}

/** The axis under a chart: one equal cell per bucket, a label every `step` buckets (counted back from the last). */
export function AxisRow({ labels }: { labels: ReadonlyArray<string> }): JSX.Element {
  return (
    <div className={styles.histAxis}>
      {labels.map((l, i) => <div key={i} className={styles.histAxisCell}>{l}</div>)}
    </div>
  );
}

/** The legend: a swatch and a name per series (identity never by colour alone). */
export function Legend({ items }: { items: ReadonlyArray<{ key: string; color: string; label: string; line?: boolean }> }): JSX.Element {
  return (
    <div className={styles.histLegend}>
      {items.map((it) => (
        <div key={it.key} className={styles.histLegendItem}>
          <div className={it.line ? styles.histSwatchLine : styles.histSwatch} style={{ backgroundColor: it.color }} />
          <div className={styles.histLegendText}>{it.label}</div>
        </div>
      ))}
    </div>
  );
}
