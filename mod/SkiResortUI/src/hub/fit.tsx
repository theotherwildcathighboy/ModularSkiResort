/*
 * src/hub/fit.tsx - "never a scrollbar" (T114): the fold rows and the fit-to-height list pager.
 *
 * Written for this repo (MIT). Created 2026-10-20 (T114 hub round; field report 2026-10-20 item 2, Richard: "ideally we wouldn't
 * have a scroll bar ever on our UI hub"; docs/research/ui/hub-round-t114-2026-10-20.md).
 * Contract:
 *   - The panel keeps its fixed height (HubPanel .panelFixed, 72vh, at most 780 rem); its content must fit it at UI text scale
 *     1.0 and 1.25. Two tools make that true by construction, and harness/layout-check.js + tools/test-hub-layout.js measure it:
 *       1. Fold: a section header row with a chevron that shows its rows only while open. ONE fold per view is open at a time
 *          (accordion, hubState.folds$ / toggleFold), so a view's height is its closed height plus its tallest fold, and the
 *          layout test opens every fold in turn. Closed by default (hubState.FOLD_DEFAULT). Its rows render flat (no wrapper), so
 *          the view pager (3.) can page them one by one.
 *       2. useFitPage + Pager: a list (runs, lifts) shows as many rows as fit and pages the rest ("‹ 1 / 3 ›" under it). The
 *          rows that fit are MEASURED after layout (two animation frames later: Gameface lays a new view out a frame late,
 *          layoutReport.ts Round F7): capacity = rows drawn + floor(slack / average row height), slack = the content box's
 *          inner height minus our body's height (negative while it overflows). The pager row is always drawn (empty on one
 *          page), so showing it never changes the height. A page that no longer exists (rows went away) clamps to the last.
 *       3. useViewPager: when a view (rows + an open fold) is still taller than the box (text scale 1.25 leaves room for about
 *          seven rows), its top-level rows are shown page by page with the same Pager under the view (HubPanel).
 *   - FitContext carries the panel body's ref (HubPanel); without it (a view rendered elsewhere) nothing is measured and the
 *     page size stays at its first value.
 *   - Buttons here are content-area buttons (not in the Panel header / footer), so R15's FOCUS_DISABLED does not apply.
 */
import { createContext, useContext, useEffect, useLayoutEffect, useRef, useState } from "react";
import type { ReactNode, RefObject } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { Unit } from "cs2/l10n";
import { useT } from "../l10n";
import { folds$, openFold, toggleFold } from "./hubState";
import { HUB_FMT, HUB_LOC } from "./hubText";
import { Glyph, LocText, Tip, cx, numArg } from "./parts";
import styles from "./hub.module.scss";

export const GLYPH_FOLD_CLOSED = "Media/Glyphs/ThickStrokeArrowRight.svg";
export const GLYPH_FOLD_OPEN = "Media/Glyphs/ThickStrokeArrowDown.svg";

/** The panel body (HubPanel's `.body`): what the pager measures against. */
export const FitContext = createContext<RefObject<HTMLDivElement> | null>(null);

/** A fold: a header row (label, optional value) with a chevron; its children only while it is the view's open fold. */
export function Fold({ view, id, label, value, tip, header, children }: {
  view: string;
  id: string;
  label: string | JSX.Element;
  value?: ReactNode;
  tip?: ReactNode;
  /** Section-title look (upper case), like InfoLine `header`. */
  header?: boolean;
  children: ReactNode;
}): JSX.Element {
  const folds = useValue(folds$);
  const open = openFold(folds, view) === id;
  return (
    <>
      <Tip tip={tip}>
        <Button theme={{ button: cx(styles.infoLine, styles.foldRow, header && styles.infoHeader, open && styles.foldRowOpen) }}
          onSelect={() => toggleFold(view, id)}>
          <Glyph src={open ? GLYPH_FOLD_OPEN : GLYPH_FOLD_CLOSED} className={styles.foldMark} />
          <div className={styles.infoLabel}>{label}</div>
          {value !== undefined && <div className={styles.infoValue}>{value}</div>}
        </Button>
      </Tip>
      {open && children}
    </>
  );
}

/** Last measured page size per list (module level), so a reopened view starts at the size that fitted last time. */
const lastSize = new Map<string, number>();
const FIRST_SIZE = 6;

export interface FitPage {
  /** First row index of the page and one past its last. */
  start: number;
  end: number;
  page: number;
  pages: number;
  setPage: (p: number) => void;
}

function innerHeight(el: HTMLElement): number {
  let pad = 0;
  try {
    const cs = getComputedStyle(el);
    pad = (parseFloat(cs.paddingTop) || 0) + (parseFloat(cs.paddingBottom) || 0);
  } catch { /* no computed style: 0 */ }
  return el.clientHeight - pad;
}

/**
 * The page of a list of `count` rows: as many rows as fit the panel (see the header). `listRef` = the element whose children are
 * the drawn rows; `key` names the list (runs / lifts) and resets the page when it changes (an area switch).
 */
export function useFitPage(key: string, count: number, listRef: RefObject<HTMLElement>): FitPage {
  const bodyRef = useContext(FitContext);
  const [size, setSize] = useState<number>(() => lastSize.get(key.split("|")[0]) ?? FIRST_SIZE);
  const [page, setPage] = useState(0);
  const keyRef = useRef(key);
  if (keyRef.current !== key) {
    keyRef.current = key;
    if (page !== 0) setPage(0);
  }
  const pages = Math.max(1, Math.ceil(count / Math.max(1, size)));
  const shownPage = Math.min(page, pages - 1);
  const start = shownPage * size;
  const end = Math.min(count, start + size);

  useEffect(() => {
    let frame = 0;
    let n = 0;
    const step = (): void => {
      frame = 0;
      try {
        if (++n < 2) { frame = requestAnimationFrame(step); return; }
        const body = bodyRef ? bodyRef.current : null;
        const list = listRef.current;
        const box = body ? body.parentElement : null;
        if (!body || !list || !box) return;
        const drawn = end - start;   // rows of this page (a row may draw more than one element)
        if (drawn <= 0) return;
        const rowH = list.getBoundingClientRect().height / drawn;
        if (!(rowH > 0)) return;
        const slack = innerHeight(box) - body.getBoundingClientRect().height;
        const fit = slack < 0 ? drawn - Math.ceil(-slack / rowH) : drawn + Math.floor(slack / rowH);
        const next = Math.max(1, fit);
        // The capacity estimate holds on a short last page too (same box, same row height).
        if (next !== size) {
          lastSize.set(key.split("|")[0], next);
          setSize(next);
        }
      } catch { /* measuring is best effort: keep the size */ }
    };
    try { frame = requestAnimationFrame(step); } catch { /* no rAF: keep the size */ }
    return () => { try { if (frame) cancelAnimationFrame(frame); } catch { /* ignore */ } };
  });

  // Re-measure on a viewport change (text scale changes re-render through the game's own update).
  const [, bump] = useState(0);
  useEffect(() => {
    const on = (): void => bump((x) => x + 1);
    try { window.addEventListener("resize", on); } catch { return undefined; }
    return () => { try { window.removeEventListener("resize", on); } catch { /* ignore */ } };
  }, []);

  return { start, end, page: shownPage, pages, setPage };
}

/** The pager row under a list: "‹ 2 / 3 ›", always drawn (empty on one page) so it never changes the view's height. */
export function Pager({ fit }: { fit: FitPage }): JSX.Element {
  const t = useT();
  const many = fit.pages > 1;
  return (
    <div className={styles.pagerRow}>
      {many && (
        <>
          <Tip tip={t(HUB_LOC.pagePrev)}>
            <Button theme={{ button: cx(styles.pagerButton, fit.page <= 0 && styles.pagerButtonOff) }}
              onSelect={() => { if (fit.page > 0) fit.setPage(fit.page - 1); }}>
              <Glyph src="Media/Glyphs/ThickStrokeArrowLeft.svg" className={styles.pagerGlyph} />
            </Button>
          </Tip>
          <div className={styles.pagerText}>
            <LocText entry={HUB_FMT.pagePosition} args={{ N: numArg(fit.page + 1, Unit.Integer), COUNT: numArg(fit.pages, Unit.Integer) }} />
          </div>
          <Tip tip={t(HUB_LOC.pageNext)}>
            <Button theme={{ button: cx(styles.pagerButton, fit.page >= fit.pages - 1 && styles.pagerButtonOff) }}
              onSelect={() => { if (fit.page < fit.pages - 1) fit.setPage(fit.page + 1); }}>
              <Glyph src="Media/Glyphs/ThickStrokeArrowRight.svg" className={styles.pagerGlyph} />
            </Button>
          </Tip>
        </>
      )}
    </div>
  );
}

// ---- the view pager (T114): a view whose rows do not fit shows them page by page ----------------------------------------------

/** How often the view pager re-checks while the panel is open (a leaf's data push can add a row without re-rendering the panel). */
const VIEW_PAGER_POLL_MS = 400;

/**
 * Pages the view slot's top-level rows (its DOM children: our views render fragments, so every InfoLine, fold header, fold row,
 * bar and chart block is one child) when the body is taller than the content box. Rows of other pages are hidden with
 * style.display = "none" (React never writes `style` on these row roots, so it stays), all in one synchronous pass: unhide,
 * measure, partition, hide, so nothing paints in between. A fold header is never the last row of a page (it moves to the next).
 * `enabled` false (the Runs / Lifts views, which page their own lists) unhides everything. `resetKey` (view + open fold) returns
 * to page 1.
 */
export function useViewPager(viewRef: RefObject<HTMLElement>, bodyRef: RefObject<HTMLElement>, enabled: boolean, resetKey: string,
  foldRowClass: string): FitPage {
  const [pages, setPages] = useState(1);
  const [page, setPage] = useState(0);
  const keyRef = useRef(resetKey);
  if (keyRef.current !== resetKey) {
    keyRef.current = resetKey;
    if (page !== 0) setPage(0);
  }
  const state = useRef({ page, pages, enabled });
  state.current = { page, pages, enabled };

  const layout = (): void => {
    try {
      const slot = viewRef.current, body = bodyRef.current;
      const box = body ? body.parentElement : null;
      if (!slot || !body || !box) return;
      const rows = Array.prototype.slice.call(slot.children) as HTMLElement[];
      for (const r of rows) if (r.style.display === "none") r.style.display = "";
      if (!state.current.enabled) { if (state.current.pages !== 1) setPages(1); return; }
      const inner = innerHeight(box);
      const bodyH = body.getBoundingClientRect().height;
      const slotRect = slot.getBoundingClientRect();
      const pagerOn = state.current.pages > 1;
      if (!pagerOn && bodyH <= inner + 0.5) { if (state.current.pages !== 1) setPages(1); return; }
      // Room for the view = the box minus everything in the body that is not the view (the pager row included once it is drawn).
      const tops = rows.map((r) => r.getBoundingClientRect().top);
      const heights = rows.map((r, i) => (i + 1 < rows.length ? tops[i + 1] : slotRect.bottom) - tops[i]);
      const first = rows.length > 0 ? heights[0] : 20;
      const room = inner - (bodyH - slotRect.height) - (pagerOn ? 0 : first);
      const pageOf: number[] = [];
      let p = 0, acc = 0, start = 0;
      for (let i = 0; i < rows.length; i++) {
        if (acc + heights[i] > room && i > start) {
          // keep a fold header with its first row
          if (i - 1 > start && rows[i - 1].className.indexOf(foldRowClass) >= 0) { pageOf[i - 1] = p + 1; acc = heights[i - 1]; } else acc = 0;
          p++; start = acc > 0 ? i - 1 : i;
        }
        pageOf[i] = p;
        acc += heights[i];
      }
      const n = p + 1;
      const shown = Math.min(state.current.page, n - 1);
      rows.forEach((r, i) => { if (pageOf[i] !== shown) r.style.display = "none"; });
      if (n !== state.current.pages) setPages(n);
    } catch { /* paging is best effort */ }
  };

  useLayoutEffect(layout);
  useEffect(() => {
    let id = 0;
    try { id = window.setInterval(layout, VIEW_PAGER_POLL_MS); } catch { return undefined; }
    return () => { try { window.clearInterval(id); } catch { /* ignore */ } };
  }, []);

  const shown = Math.min(page, pages - 1);
  return { start: 0, end: 0, page: shown, pages, setPage };
}
