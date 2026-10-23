/*
 * The uiLayout dev aid: measured panel heights (rem) sent to the mod log to check the estimated budget in the field. Any failure is
 * swallowed: a dev aid must never break the panel.
 * THE REM IS MEASURED with an out-of-flow probe element (hub.module.scss .remProbe), not parsed from the root font size: the game declares
 * that size in viewport units, so Gameface's computed string loses its unit. Measuring waits for the layout to settle after a view change;
 * a text is sent at most once per UI session, and C# logs `Hub [layout]` only when it differs from its last one.
 * Engine notes: EngineNotes/layoutReport.ts.md
 */
import { useEffect, useRef } from "react";
import type { RefObject } from "react";
import { hubTriggers } from "../bindings";

/** Width of the rem probe in rem (hub.module.scss .remProbe): 1 % precision where 1rem is 1-2 px. */
export const REM_PROBE_REM = 100;

/** How often the text-scale key is read while the panel is open (ms). */
const SCALE_POLL_MS = 1000;

/** Animation frames skipped before the first measure after a change (the new view's layout is not ready on the
 *  first one), and the most frames a measure may take to settle. */
const SKIP_FRAMES = 1;
const SETTLE_MAX_FRAMES = 8;

/** Texts already sent this UI session (a view switch changes the text, so a last-only check would re-send on
 *  every switch back and forth). Cleared at SENT_MAX. */
const sent = new Set<string>();
const SENT_MAX = 64;

export function rowBucket(rows: number): string {
  if (rows <= 0) return "0";
  if (rows < 5) return "1-4";
  if (rows < 10) return "5-9";
  if (rows < 20) return "10-19";
  return "20+";
}

function commonAncestor(a: Element, b: Element): Element | null {
  const seen: Element[] = [];
  for (let e: Element | null = a; e; e = e.parentElement) seen.push(e);
  for (let e: Element | null = b; e; e = e.parentElement) if (seen.indexOf(e) >= 0) return e;
  return null;
}

function boxUnder(el: Element, ancestor: Element): Element {
  let e: Element = el;
  while (e.parentElement && e.parentElement !== ancestor) e = e.parentElement;
  return e;
}

function heightOf(el: Element | null | undefined, unit: number): number {
  if (!el) return -1;
  return Math.round(el.getBoundingClientRect().height / unit);
}

/** px of 1rem from the probe, or 0 when it cannot be measured. */
function remPx(probe: Element | null): number {
  if (!probe) return 0;
  const w = probe.getBoundingClientRect().width / REM_PROBE_REM;
  return w > 0 && Number.isFinite(w) ? w : 0;
}

/** The root's --fontScale: the inline value the game writes (applyTextScale), else the computed one. */
function fontScale(): string {
  const root = document.documentElement;
  const inline = root.style.getPropertyValue("--fontScale").trim();
  if (inline) return inline;
  return getComputedStyle(root).getPropertyValue("--fontScale").trim();
}

/** What a re-measure depends on besides the rows: text scale, root font size, viewport. Reads no layout. */
function scaleKey(): string {
  const root = document.documentElement;
  return `${fontScale()}|${root.style.fontSize}|${window.innerWidth}x${window.innerHeight}`;
}

/** Our own blocks of the content, measured after the base fields. */
export interface LayoutRefs {
  header: RefObject<Element>;
  content: RefObject<Element>;
  footer: RefObject<Element>;
  /** The shown view's list (RunList / LiftList); null element = "list=-1". */
  list: RefObject<Element>;
  probe: RefObject<Element>;
  status: RefObject<Element>;
  problems: RefObject<Element>;
  tiles: RefObject<Element>;
  /** The view slot (the element that wraps the selected view). */
  view: RefObject<Element>;
}

function measure(
  header: Element | null, content: Element | null, footer: Element | null, list: Element | null,
  probe: Element | null, rows: number,
): string | null {
  if (!header || !content || !footer) return null;
  let unit = 1;
  let px = true;
  let scale = "?";
  try {
    const rem = remPx(probe);
    if (rem > 0) {
      unit = rem;
      px = false;
    }
    scale = fontScale() || "?";
  } catch {
    // keep px
  }
  const panel = commonAncestor(header, footer);
  const h = panel ? boxUnder(header, panel) : header;
  const c = panel ? boxUnder(content, panel) : content;
  const f = panel ? boxUnder(footer, panel) : footer;
  return `header=${heightOf(h, unit)} content=${heightOf(c, unit)} footer=${heightOf(f, unit)} ` +
    `list=${heightOf(list, unit)} rows=${rows} scale=${scale}${px ? " px" : ` rem=${unit.toFixed(2)}px`}`;
}

/** Sends the layout once after the first layout, on every row-bucket change, and on a text scale / root font size /
 * viewport change while the panel is open. */
/** The overflow of the Scrollable's content div (the content element's parent) in `unit`s; -1 without that element.
 *  A negative difference (content shorter than the box) reads 0. */
function overflowOf(content: Element | null | undefined, unit: number): number {
  const sc = content ? content.parentElement : null;
  if (!sc) return -1;
  return Math.max(0, Math.round((sc.scrollHeight - sc.offsetHeight) / unit));
}

/** The base text + ` status=<h> problems=<h> tiles=<h> view=<name> viewH=<h> over=<n>`. */
function measureV3(r: LayoutRefs, rows: number, view: string): string | null {
  const base = measure(r.header.current, r.content.current, r.footer.current, r.list.current, r.probe.current, rows);
  if (base === null) return null;
  let unit = 1;
  try { const rem = remPx(r.probe.current); if (rem > 0) unit = rem; } catch { /* px */ }
  return `${base} status=${heightOf(r.status.current, unit)} problems=${heightOf(r.problems.current, unit)} ` +
    `tiles=${heightOf(r.tiles.current, unit)} view=${view} viewH=${heightOf(r.view.current, unit)} ` +
    `over=${overflowOf(r.content.current, unit)}`;
}

export function useLayoutReport(r: LayoutRefs, rows: number, view: string): void {
  const key = `${view}|${rowBucket(rows)}`;
  const rowsRef = useRef(rows);
  rowsRef.current = rows;
  const viewRef = useRef(view);
  viewRef.current = view;
  const frameRef = useRef(0);
  const schedule = (): void => {
    try {
      if (frameRef.current) cancelAnimationFrame(frameRef.current);
      // Skip SKIP_FRAMES frames, then measure once a frame until two consecutive texts agree
      // (or SETTLE_MAX_FRAMES frames have passed) and send that text (EngineNotes/layoutReport.ts.md).
      let frame = 0;
      let last: string | null = null;
      const step = (): void => {
        frameRef.current = 0;
        try {
          frame++;
          if (frame <= SKIP_FRAMES) { frameRef.current = requestAnimationFrame(step); return; }
          const text = measureV3(r, rowsRef.current, viewRef.current);
          if (text !== null && text !== last && frame < SETTLE_MAX_FRAMES) {
            last = text;
            frameRef.current = requestAnimationFrame(step);
            return;
          }
          if (text && !sent.has(text)) {
            if (sent.size >= SENT_MAX) sent.clear();
            sent.add(text);
            hubTriggers.uiLayout(text);
          }
        } catch { /* a dev aid never breaks the panel */ }
      };
      frameRef.current = requestAnimationFrame(step);
    } catch { /* no requestAnimationFrame: no report */ }
  };
  useEffect(() => {
    schedule();
    return () => {
      try { if (frameRef.current) cancelAnimationFrame(frameRef.current); } catch { /* ignore */ }
      frameRef.current = 0;
    };
  }, [key]);
  useEffect(() => {
    let last = "";
    let timer = 0;
    try {
      last = scaleKey();
      timer = window.setInterval(() => {
        try { const k = scaleKey(); if (k !== last) { last = k; schedule(); } } catch { /* next tick */ }
      }, SCALE_POLL_MS);
    } catch { return undefined; }
    return () => { try { window.clearInterval(timer); } catch { /* ignore */ } };
  }, []);
}
