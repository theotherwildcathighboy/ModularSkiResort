/*
 * src/hub/layoutReport.ts - the uiLayout dev aid: measured panel heights sent to the mod log.
 *
 * Written for this repo (MIT). Created 2026-10-03 (Round U2, implementer I-3); rem measurement and
 * the text-scale re-measure reworked 2026-10-03 (Round F6, implementer C, item C2; field test 6
 * log-audit A6 and its Verification).
 * Authority: docs/research/hub-u2-spec.md section 4.6. The spec's budget (header ~48, footer ~114,
 * content ~616 rem at 1080p / 100 % text; ~7 rows visible with Trees open) is an ESTIMATE; this
 * report is how the field test (step 13) measures it.
 * Contract:
 *   - Text: `header=40 content=647 footer=90 list=109 rows=1 scale=1 rem=2.00px` - heights in rem,
 *     rounded; `scale` = the root's --fontScale (the game sets it, index.js@801715 applyTextScale);
 *     `rem` = the measured px of 1rem. If no rem can be measured the numbers are px and the text
 *     ends in " px".
 *   - THE REM (Round F6, C2). It is MEASURED: a probe element of our own, 100rem wide, out of flow
 *     and never drawn (hub.module.scss .remProbe, rendered by HubPanel in the header slot), divided
 *     by 100. Field test 6 logged values 38.4 times too large: the old code read
 *     parseFloat(getComputedStyle(html).fontSize), but the game declares the root size in viewport
 *     units (installed index.css `html{font-size:.0925926vh}`, and `.0520833vw` under
 *     `@media(min-height:56.25vw)`), Gameface most likely returned the declared "0.0520833vw", and
 *     parseFloat dropped the unit (log-audit Verification: 1 / 0.0520833 = 19.2, and 1 rem = 2 px at
 *     3840x2160). A probe also covers Interface Scaling off (the game sets the root to 1px,
 *     index.js@801713 applyInterfaceScalingEnabled) and screens taller than 16:9, where
 *     "viewport height / 1080" would be wrong.
 *   - CONTRACT 3 (2026-10-04, Round E1, stream S6; econ-round spec ch6 section 6.14, deviation U14):
 *     useLayoutReport takes one LayoutRefs object and the view's name; the text is today's fields
 *     followed by ` status=<h> problems=<h> tiles=<h> view=<name> viewH=<h>` (our own blocks of the
 *     content: the status row, the problems band, the tile row and the view slot; -1 for a missing
 *     element). Example at 100 %: `header=40 content=647 footer=90 list=-1 rows=0 scale=1
 *     rem=1.00px status=22 problems=67 tiles=42 view=overview viewH=406`.
 *   - Round S1 F (2026-10-05, stream E; docs/research/ski-days/spec/ch4-hub.md F.3): ` over=<n>` follows
 *     viewH: how far the Scrollable's content box overflows, in rem = max(0, round((scrollHeight -
 *     offsetHeight) / rem)) of the content element's parent (the Scrollable's content div; the Providers
 *     around our body render no DOM), -1 when there is no such parent. The vanilla Scrollable shows its bar
 *     when scrollHeight - 1 > offsetHeight (installed index.js@638418), so a short view that logs over=0
 *     shows no bar (field test 9: every view at 100 %). Same reads as the other fields, no extra pass.
 *   - WHEN IT MEASURES (Round F7, D6; field test 7 log-audit D12 and hub-ux §5 item 10 with its
 *     Verification claim 17: from L529 to L648 every `viewH` equalled the PREVIOUS view's height, and
 *     `list=0 rows=3` - one animation frame after the change, Gameface had not laid out the new view
 *     yet [cause unverified: Gameface computes layout after the frame's script, likely]). A measure now
 *     starts on the SECOND animation frame after the change and repeats once a frame until two
 *     consecutive texts agree (the layout has settled), at most SETTLE_MAX_FRAMES frames, then sends
 *     the last text. A handful of getBoundingClientRect calls per change, never per frame otherwise.
 *   - Sent after the panel's first layout, whenever the view or the visible
 *     row-count BUCKET changes (0, 1-4, 5-9, 10-19, 20+), and (Round F6) whenever the text scale, the
 *     root font size or the viewport changes while the panel stays open: a 1 s timer reads two root
 *     style strings and the viewport size (no layout, no measurement) and measures only when that
 *     key changed. Never per render, never per frame. A text is sent at most ONCE per UI session
 *     (contract 3: a module-level set of the texts already sent, at most 64, cleared when full -
 *     alternating views would otherwise send a line on every switch), and C# logs `Hub [layout]` only
 *     when the text differs from its last one, at most Tuning.LayoutLinesMax lines per load (64 in
 *     contract 3, ch5 C13): no flood either way.
 *   - The Panel's own header / content / footer boxes are not ours (cs2/ui Panel, index.js@684600),
 *     so they are found from our own elements: the lowest common ancestor of our header and footer
 *     roots is the panel box; each of our roots' ancestor directly under it is that part's box
 *     (confirmed by field test 6: the logged boxes / 19.2 equal the screenshot's pixel rows).
 *     Any failure (an API Gameface lacks) is swallowed: a dev aid must never break the panel.
 */
import { useEffect, useRef } from "react";
import type { RefObject } from "react";
import { hubTriggers } from "../bindings";

/** Width of the rem probe in rem (hub.module.scss .remProbe): 1 % precision where 1rem is 1-2 px. */
export const REM_PROBE_REM = 100;

/** How often the text-scale key is read while the panel is open (ms). */
const SCALE_POLL_MS = 1000;

/** Round F7: animation frames skipped before the first measure after a change (the new view's layout
 *  is not ready on the first one, field test 7), and the most frames a measure may take to settle. */
const SKIP_FRAMES = 1;
const SETTLE_MAX_FRAMES = 8;

/** Texts already sent this UI session (contract 3: a view switch changes the text, so a
 *  last-only check would re-send on every switch back and forth). Cleared at SENT_MAX. */
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

/** Round E1 (contract 3): our own blocks of the content, measured after today's fields. */
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

/**
 * Sends the layout once after the first layout, on every change of the row bucket, and on a change
 * of the text scale / root font size / viewport while the panel is open (see the header).
 */
/** Round S1 F: the overflow of the Scrollable's content div (the content element's parent) in `unit`s;
 *  -1 without that element. A negative difference (content shorter than the box) reads 0. */
function overflowOf(content: Element | null | undefined, unit: number): number {
  const sc = content ? content.parentElement : null;
  if (!sc) return -1;
  return Math.max(0, Math.round((sc.scrollHeight - sc.offsetHeight) / unit));
}

/** Contract 3: today's text + ` status=<h> problems=<h> tiles=<h> view=<name> viewH=<h>` (design 8.15);
 *  Round S1 F: + ` over=<n>`. */
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
      // Round F7: skip SKIP_FRAMES frames, then measure once a frame until two consecutive texts agree
      // (or SETTLE_MAX_FRAMES frames have passed) and send that text (see the header).
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
