/*
 * src/hub/fit.tsx - "never a scrollbar": the fold rows and the view pager.
 * The panel keeps a fixed height (HubPanel .panelFixed); its content must fit at UI text scale 1.0 and 1.25 (harness/layout-check.js,
 * tools/test-hub-layout.js). Fold: a section header row with a chevron; one fold per view is open at a time (accordion), so a view's
 * height is its closed height plus its tallest fold; rows render flat so useViewPager can page them when a view is still taller than
 * the box. FitContext carries the panel body's ref (without it nothing is measured). These buttons are content-area buttons, so
 * FOCUS_DISABLED does not apply.
 */
import { createContext, useEffect, useLayoutEffect, useRef, useState } from "react";
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

/** The panel body (HubPanel's `.body`): what the pager measures against */
export const FitContext = createContext<RefObject<HTMLDivElement> | null>(null);

/** A fold: a header row (label, optional value) with a chevron; its children only while it is the view's open fold */
export function Fold({ view, id, label, value, tip, header, children }: {
  view: string;
  id: string;
  label: string | JSX.Element;
  value?: ReactNode;
  tip?: ReactNode;
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

export interface FitPage {
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


/** How often the view pager re-checks while the panel is open (a leaf's data push can add a row without re-rendering the panel) */
const VIEW_PAGER_POLL_MS = 400;

/**
 * Pages the view slot's top-level rows (its DOM children: our views render fragments, so every InfoLine, fold header, fold
 * row, bar and chart block is one child) when the body is taller than the content box. Rows of other pages are hidden with
 * style.display = "none" (React never writes `style` on these row roots, so it stays), all in one synchronous pass: unhide,
 * measure, partition, hide, so nothing paints in between. A fold header is never the last row of a page (it moves to the
 * next). `enabled` false (the Runs / Lifts views) unhides everything. `resetKey` (view + open fold) returns to page 1.
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
      // Room for the view = the box minus everything in the body that is not the view (the pager row included once it is drawn)
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
