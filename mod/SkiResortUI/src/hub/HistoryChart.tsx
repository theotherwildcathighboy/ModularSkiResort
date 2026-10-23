/*
 * The History view's charts as inline SVG. React only, no chart.js (the harness stubs it, a game patch can change it, the
 * vanilla UI draws its small charts as React SVG too).
 *
 * Contract:
 *   - Pure presentation of historyRules buckets: <BarChart> draws one slot per bucket. Stacked bars: `up` series above the
 *     zero line, `down` series below it (costs, shown positive in the data); one optional line (`line`: ski days or the
 *     net) through the slot centers, broken where historyRules.linePoints has no point. ONE axis (no dual scale): the line
 *     shares the bars' scale. Each <BarChart> is its own chart; the attraction strip is a separate <LineStrip> with its
 *     own scale.
 *   - Marks: bars 70 % of a slot (a slot gap of at least 2 rem between bars), a 2 rem surface gap between stacked
 *     segments, a line 2 rem wide. Partial buckets are drawn faded (fill opacity 0.45), the month in progress also with a
 *     dashed outline.
 *   - SVG elements used: svg, rect, path, line (the ones vanilla renders through React; polyline, pattern and defs are
 *     never used). Coordinates are viewBox units (VIEW_W wide), preserveAspectRatio "none": the box fills the chart
 *     column's width. Text is HTML around the SVG (axis labels, the scale), never SVG <text>.
 *   - Hover: the plot's own div tracks the pointer (getBoundingClientRect), the bucket under it is highlighted and
 *     reported to onHover (-1 when the pointer leaves); the parent owns the tooltip.
 *   - Scale gutter: every chart is a row [gutter | plot + axis]. The gutter holds the top value, "0" on the zero line and
 *     the bottom value, right-aligned, never over a mark; a top / bottom value closer than LABEL_ROOM to the zero line is
 *     dropped (the "0" stays). Its width is set by `sizers` (every scale label of the view, laid out invisibly), so the
 *     gutters of the view's charts are equally wide and their slots line up (the shared hover index).
 *   - Gaps: a slot with `gap` gets a faint band hatched at 45 deg (the dataviz texture channel), no bar. Empty slots
 *     without `gap` (before the area existed, the rest of this season) stay blank.
 *   - Line ends: lines take historyRules.linePoints (null = no point), so they end at the last whole month.
 *   - "This season vs last": `behind` draws last season's same months gray (HISTORY_COLOR.lastSeason) as wide bars behind
 *     this season's narrower colored ones (totals only: one gray bar up, one down), plus its line in gray.
 *   - Colors: the Finances chart is income (blue) above zero, costs (red) below, the net as a near-white line. Validated
 *     with the dataviz validator against the panel surface #2a3753 (dark): income / costs and the guests set blue / aqua /
 *     yellow pass every check. The net line #f0fbff and the last-season gray #646c7e are neutrals, not categorical hues
 *     (band / chroma "FAIL" by design); gray vs red CVD dE 8.3, contrast 2.25:1 (WARN: relief = the "Last season" legend
 *     label and its number in the tooltip).
 */
import type { ReactNode } from "react";
import { Tip, cx } from "./parts";
import styles from "./hub.module.scss";
import { bucketAt } from "./historyRules";

/** viewBox width (rem at 100 %, about the chart column's width). */
export const VIEW_W = 400;

/** Series colors (dataviz reference palette, dark steps). */
export const HISTORY_COLOR = {
  tourists: "#3987e5",
  residents: "#199e70",
  skiDays: "#c98500",
  income: "#3987e5",
  costs: "#e34948",
  net: "#f0fbff",
  attraction: "#9085e9",
  lastSeason: "#646c7e",
} as const;

/** Rem a top / bottom scale value needs from the zero line to stay legible (one XS line). */
const LABEL_ROOM = 14;

export interface ChartSeries {
  key: string;
  color: string;
  /** The value of bucket i (>= 0; negative or non-finite draws nothing). */
  value: (i: number) => number;
}

/** A line: its color and one point per slot (null = no point; historyRules.linePoints). */
export interface ChartLine {
  key: string;
  color: string;
  points: ReadonlyArray<number | null>;
}

export interface ChartSlot {
  none: boolean;
  partial: boolean;
  current: boolean;
  /** Recorded nothing inside the area's range: the hatched band. */
  gap: boolean;
}

/** The scale: the top value above zero and the bottom value below it, both >= 0 (one unit for both). */
export interface ChartScale {
  top: number;
  bottom: number;
}

/** Last season behind this one ("seasonVs"): one total up, one down per slot (NaN / <= 0 = none), and its line. */
export interface ChartBehind {
  up: ReadonlyArray<number>;
  down: ReadonlyArray<number>;
  line: ChartLine | null;
}

function finite(v: number | null | undefined): v is number {
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

/** 45-degree hatch lines over [x0, x0 + w] x [0, h], `step` apart, clipped to the box (one path). */
function hatchPath(x0: number, w: number, h: number, step: number): string {
  let d = "";
  for (let c = step / 2; c < w + h; c += step) {
    // the line x + y = x0 + c inside the box
    const xa = Math.max(x0, x0 + c - h), xb = Math.min(x0 + w, x0 + c);
    if (xb <= xa) continue;
    d += `M${xa.toFixed(1)} ${(x0 + c - xa).toFixed(1)} L${xb.toFixed(1)} ${(x0 + c - xb).toFixed(1)} `;
  }
  return d;
}

/** A line path through the slot centers, broken at null points. */
function linePath(points: ReadonlyArray<number | null>, slotW: number, y: (v: number) => number): string {
  let path = "";
  let pen = false;
  points.forEach((v, i) => {
    if (!finite(v)) { pen = false; return; }
    path += `${pen ? "L" : "M"}${(i * slotW + slotW / 2).toFixed(1)} ${y(v).toFixed(1)} `;
    pen = true;
  });
  return path;
}

/** The gap bands of a plot: a faint fill and the 45-degree hatch per gap slot. */
function gapMarks(slots: readonly ChartSlot[], slotW: number, height: number): JSX.Element[] {
  const out: JSX.Element[] = [];
  slots.forEach((s, i) => {
    if (!s.gap) return;
    const x = i * slotW + 1;
    const w = Math.max(1, slotW - 2);
    out.push(<rect key={`gb${i}`} x={x} y={0} width={w} height={height} fill="#ffffff" fillOpacity={0.04} />);
    out.push(<path key={`gh${i}`} d={hatchPath(x, w, height, 5)} fill="none" stroke="#ffffff" strokeOpacity={0.22} strokeWidth={1} />);
  });
  return out;
}

/** The scale gutter: invisible sizers set its width; the labels sit at their y (rem from the plot's top), right-aligned. */
function Gutter({ sizers, height, labels }: {
  sizers: readonly ReactNode[];
  height: number;
  labels: ReadonlyArray<{ key: string; y: number; align: "top" | "middle" | "bottom"; node: ReactNode }>;
}): JSX.Element {
  return (
    <div className={styles.histGutter}>
      <div className={styles.histGutterSizer}>{sizers.map((n, i) => <div key={i} className={styles.histGutterSize}>{n}</div>)}</div>
      <div className={styles.histGutterPlot} style={{ height: `${height}rem` }}>
        {labels.map((l) => (
          <div key={l.key} className={cx(styles.histScaleLabel, l.align === "middle" && styles.histScaleMid, l.align === "bottom" && styles.histScaleBottom)}
            style={{ top: `${l.y}rem` }}>{l.node}</div>
        ))}
      </div>
    </div>
  );
}

/** The scale labels of a plot with its zero line at zeroY: top value, "0", bottom value, none closer than LABEL_ROOM. */
function scaleLabels(scale: ChartScale, height: number, zeroY: number, topLabel: ReactNode, bottomLabel: ReactNode) {
  const zeroAlign: "top" | "middle" | "bottom" = zeroY >= height - LABEL_ROOM / 2 ? "bottom" : zeroY <= LABEL_ROOM / 2 ? "top" : "middle";
  const out: Array<{ key: string; y: number; align: "top" | "middle" | "bottom"; node: ReactNode }> = [];
  if (scale.top > 0 && zeroY >= LABEL_ROOM) out.push({ key: "t", y: 0, align: "top", node: topLabel });
  out.push({ key: "z", y: zeroY, align: zeroAlign, node: "0" });
  if (scale.bottom > 0 && height - zeroY >= LABEL_ROOM) out.push({ key: "b", y: height, align: "bottom", node: bottomLabel });
  return out;
}

export function BarChart({ slots, up, down, line, behind, scale, height, hover, onHover, topLabel, bottomLabel, sizers, axis, tip }: {
  slots: readonly ChartSlot[];
  up: readonly ChartSeries[];
  down: readonly ChartSeries[];
  /** One line on the same scale (e.g. ski days, the net); null = none. Values below zero draw below the zero line. */
  line: ChartLine | null;
  /** Last season, gray behind; null = none. */
  behind: ChartBehind | null;
  scale: ChartScale;
  /** Plot height in rem (viewBox units). */
  height: number;
  hover: number;
  onHover: (i: number) => void;
  /** The top / bottom value as text (HTML, in the gutter). */
  topLabel: ReactNode;
  bottomLabel: ReactNode;
  /** Every scale label of the view (sets the gutter width, the same for every chart). */
  sizers: readonly ReactNode[];
  /** The axis labels under the plot (one per slot). */
  axis: ReadonlyArray<string>;
  /** The tooltip (the hovered bucket's numbers): on the plot's own div (parts.tsx Tip rule). */
  tip: ReactNode;
}): JSX.Element {
  const n = slots.length;
  const span = Math.max(1e-9, scale.top + scale.bottom);
  const zeroY = (scale.top / span) * height;
  const unit = height / span;
  const slotW = n > 0 ? VIEW_W / n : VIEW_W;
  const barW = Math.max(1, slotW * (behind ? 0.5 : 0.7));
  const backW = Math.max(1, slotW * 0.86);
  const marks: JSX.Element[] = [];
  if (behind) {
    for (let i = 0; i < n; i++) {
      const x = i * slotW + (slotW - backW) / 2;
      const u = behind.up[i], d = behind.down[i];
      if (finite(u) && u > 0) marks.push(<rect key={`bu${i}`} x={x} y={zeroY - u * unit} width={backW} height={Math.max(0.5, u * unit)} fill={HISTORY_COLOR.lastSeason} />);
      if (finite(d) && d > 0) marks.push(<rect key={`bd${i}`} x={x} y={zeroY} width={backW} height={Math.max(0.5, d * unit)} fill={HISTORY_COLOR.lastSeason} />);
    }
  }
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
  const yOf = (v: number): number => zeroY - v * unit;
  const path = line ? linePath(line.points, slotW, yOf) : "";
  const backPath = behind && behind.line ? linePath(behind.line.points, slotW, yOf) : "";
  const hoverX = hover >= 0 && hover < n ? hover * slotW : -1;
  return (
    <div className={styles.histChart}>
      <Gutter sizers={sizers} height={height} labels={scaleLabels(scale, height, zeroY, topLabel, bottomLabel)} />
      <div className={styles.histChartMain}>
        <Tip tip={tip}>
          <div className={styles.histPlot} onMouseMove={(e) => onHover(pointerBucket(e, n))} onMouseLeave={() => onHover(-1)}>
            <svg className={styles.histSvg} viewBox={`0 0 ${VIEW_W} ${height}`} preserveAspectRatio="none" style={{ height: `${height}rem` }}>
              {hoverX >= 0 && <rect x={hoverX} y={0} width={slotW} height={height} fill="#ffffff" fillOpacity={0.08} />}
              {gapMarks(slots, slotW, height)}
              <line x1={0} y1={0} x2={VIEW_W} y2={0} stroke="#ffffff" strokeOpacity={0.12} strokeWidth={1} />
              {scale.bottom > 0 && <line x1={0} y1={height} x2={VIEW_W} y2={height} stroke="#ffffff" strokeOpacity={0.12} strokeWidth={1} />}
              {marks}
              <line x1={0} y1={zeroY} x2={VIEW_W} y2={zeroY} stroke="#ffffff" strokeOpacity={0.45} strokeWidth={1} />
              {backPath !== "" && <path d={backPath} fill="none" stroke={HISTORY_COLOR.lastSeason} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />}
              {path !== "" && line && <path d={path} fill="none" stroke={line.color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />}
            </svg>
          </div>
        </Tip>
        <AxisRow labels={axis} />
      </div>
    </div>
  );
}

/** A thin strip with one line on its own scale (0..top): the attraction overlay, a separate chart (no dual axis). */
export function LineStrip({ slots, line, top, height, hover, onHover, topLabel, sizers, tip }: {
  slots: readonly ChartSlot[];
  line: ChartLine;
  top: number;
  height: number;
  hover: number;
  onHover: (i: number) => void;
  topLabel: ReactNode;
  sizers: readonly ReactNode[];
  tip: ReactNode;
}): JSX.Element {
  const n = slots.length;
  const slotW = n > 0 ? VIEW_W / n : VIEW_W;
  const unit = height / Math.max(1e-9, top);
  const yOf = (v: number): number => height - Math.max(0, v) * unit;
  const path = linePath(line.points, slotW, yOf);
  const dots: JSX.Element[] = [];
  line.points.forEach((v, i) => {
    if (!finite(v) || (i > 0 && finite(line.points[i - 1]))) return;   // a dot where a run of points starts (a lone month shows)
    dots.push(<rect key={`d${i}`} x={i * slotW + slotW / 2 - 1.5} y={yOf(v) - 1.5} width={3} height={3} fill={line.color} />);
  });
  const hoverX = hover >= 0 && hover < n ? hover * slotW : -1;
  return (
    <div className={cx(styles.histChart, styles.histStrip)}>
      <Gutter sizers={sizers} height={height} labels={scaleLabels({ top, bottom: 0 }, height, height, topLabel, null)} />
      <div className={styles.histChartMain}>
        <Tip tip={tip}>
          <div className={styles.histPlot} onMouseMove={(e) => onHover(pointerBucket(e, n))} onMouseLeave={() => onHover(-1)}>
            <svg className={styles.histSvg} viewBox={`0 0 ${VIEW_W} ${height}`} preserveAspectRatio="none" style={{ height: `${height}rem` }}>
              {hoverX >= 0 && <rect x={hoverX} y={0} width={slotW} height={height} fill="#ffffff" fillOpacity={0.08} />}
              {gapMarks(slots, slotW, height)}
              <line x1={0} y1={height} x2={VIEW_W} y2={height} stroke="#ffffff" strokeOpacity={0.45} strokeWidth={1} />
              {dots}
              {path !== "" && <path d={path} fill="none" stroke={line.color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />}
            </svg>
          </div>
        </Tip>
      </div>
    </div>
  );
}

/** The axis under a chart: one equal cell per bucket (labels from HistoryView.axisLabels). */
export function AxisRow({ labels }: { labels: ReadonlyArray<string> }): JSX.Element {
  return (
    <div className={styles.histAxis}>
      {labels.map((l, i) => <div key={i} className={styles.histAxisCell}>{l}</div>)}
    </div>
  );
}

/** The legend: a swatch and a name per series (identity never by color alone). */
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
