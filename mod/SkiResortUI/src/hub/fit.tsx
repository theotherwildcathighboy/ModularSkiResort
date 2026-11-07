/*
 * src/hub/fit.tsx - "never a scrollbar": the fold rows and the view pager.
 * The panel keeps a fixed height (HubPanel .panelFixed); its content must fit at UI text scale 1.0 and 1.25 (harness/layout-check.js,
 * tools/test-hub-layout.js). Fold: a section header row with a chevron; one fold per view is open at a time (accordion), so a view's
 * height is its closed height plus its tallest fold; rows render flat so useViewPager can page them when a view is still taller than
 * the box. FitContext carries the panel body's ref (without it nothing is measured). The fold and pager buttons pass
 * focusKey={FOCUS_DISABLED} like every hub control (rule R15): the hub is mouse-only and never takes the game's key focus.
 */
import { createContext, useEffect, useLayoutEffect, useRef, useState } from "react";
import type { ReactNode, RefObject } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
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
        <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.infoLine, styles.foldRow, header && styles.infoHeader, open && styles.foldRowOpen) }}
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
            <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.pagerButton, fit.page <= 0 && styles.pagerButtonOff) }}
              onSelect={() => { if (fit.page > 0) fit.setPage(fit.page - 1); }}>
              <Glyph src="Media/Glyphs/ThickStrokeArrowLeft.svg" className={styles.pagerGlyph} />
            </Button>
          </Tip>
          <div className={styles.pagerText}>
            <LocText entry={HUB_FMT.pagePosition} args={{ N: numArg(fit.page + 1, Unit.Integer), COUNT: numArg(fit.pages, Unit.Integer) }} />
          </div>
          <Tip tip={t(HUB_LOC.pageNext)}>
            <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.pagerButton, fit.page >= fit.pages - 1 && styles.pagerButtonOff) }}
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
 * style.display = "none" (React never writes `style` on these row roots, so it stays); a pass writes a row's display only when
 * it changes, so a steady page writes nothing. A fold header is never the last row of a page (it moves to the next). `enabled`
 * false (the Runs / Lifts views) unhides everything. `resetKey` (view + open fold) returns to page 1, except that a fold toggle
 * (`foldSeq` changed) shows the page holding that fold's header (`openRowClass`): opening a fold whose rows push it past page 1
 * must not hide the header the player just clicked. `geometryKey` (what changes the box, the Buildings tab's taller footer)
 * drops the measured room.
 *
 * Gameface does not reflow on a read: getBoundingClientRect / clientHeight after a style write return the last drawn frame (measured
 * in game 2026-11-06: a row set to display none still reads its old height in the same task). So a pass only trusts geometry when no
 * row was hidden in that frame. While the pager is on, rows hidden in the last frame read 0 and the box has shrunk to the shown
 * page; measuring them anyway made every 400 ms poll read "fits", drop the pager, show all rows, read "overflows", page again: the
 * view and the pager row flickered (field bug r611, the Attraction view with the Merged buildings fold open on the Buildings tab,
 * 21 px over). Paging now uses each row's height from the last frame it was drawn and the room from the last trusted pass.
 */
export function useViewPager(viewRef: RefObject<HTMLElement>, bodyRef: RefObject<HTMLElement>, enabled: boolean, resetKey: string,
  foldRowClass: string, geometryKey = "", openRowClass = "", foldSeq = 0): FitPage {
  const [pages, setPages] = useState(1);
  const [page, setPage] = useState(0);
  const keyRef = useRef(resetKey);
  if (keyRef.current !== resetKey) {
    keyRef.current = resetKey;
    if (page !== 0) setPage(0);
  }
  // foldSeq counts fold toggles within one view: the next passes (this render's two) show the toggled fold's page
  const focusOpen = useRef(0);
  const lastOpenRow = useRef<HTMLElement | null>(null);
  const foldSeqRef = useRef(foldSeq);
  if (foldSeqRef.current !== foldSeq) {
    foldSeqRef.current = foldSeq;
    focusOpen.current = 2;
  }
  const state = useRef({ page, pages, enabled });
  state.current = { page, pages, enabled };
  // Row heights from frames where the row was drawn, and the room for the view rows from the last pass with trusted geometry
  const heightOf = useRef(new WeakMap<HTMLElement, number>());
  const roomRef = useRef<number | null>(null);
  const geometryRef = useRef(geometryKey);
  if (geometryRef.current !== geometryKey) {
    geometryRef.current = geometryKey;
    roomRef.current = null;
  }

  const layout = (): void => {
    try {
      const slot = viewRef.current, body = bodyRef.current;
      const box = body ? body.parentElement : null;
      if (!slot || !body || !box) return;
      const rows = Array.prototype.slice.call(slot.children) as HTMLElement[];
      const wasHidden = rows.map((r) => r.style.display === "none");
      // Write a row's style only when it changes (a pass that keeps the page writes nothing)
      const showAll = (): void => { for (const r of rows) if (r.style.display === "none") r.style.display = ""; };
      if (!state.current.enabled) { showAll(); roomRef.current = null; if (state.current.pages !== 1) setPages(1); return; }
      // Everything below reads the last drawn frame (no reflow on a read)
      const inner = innerHeight(box);
      const bodyH = body.getBoundingClientRect().height;
      const slotRect = slot.getBoundingClientRect();
      const pagerOn = state.current.pages > 1;
      const tops = rows.map((r) => r.getBoundingClientRect().top);
      const drawn: number[] = [];
      for (let i = 0; i < rows.length; i++) if (!wasHidden[i]) drawn.push(i);
      // A drawn row's height: down to the next drawn row when that is the next row, else to the slot's end (last drawn), else its box
      drawn.forEach((i, k) => {
        const next = k + 1 < drawn.length ? drawn[k + 1] : -1;
        const h = next === i + 1 ? tops[next] - tops[i] : next < 0 ? slotRect.bottom - tops[i] : rows[i].getBoundingClientRect().height;
        if (h > 0) heightOf.current.set(rows[i], h);
      });
      const heights = rows.map((r) => heightOf.current.get(r) ?? r.getBoundingClientRect().height);
      const first = rows.length > 0 ? heights[0] : 20;
      let room: number;
      if (drawn.length === rows.length) {
        // Every row was drawn in the last frame: the geometry is the view's own
        if (!pagerOn && bodyH <= inner + 0.5) { showAll(); roomRef.current = null; if (state.current.pages !== 1) setPages(1); return; }
        // Room for the view = the box minus everything in the body that is not the view (the pager row included once it is drawn)
        room = inner - (bodyH - slotRect.height) - (pagerOn ? 0 : first);
        roomRef.current = room;
      } else if (roomRef.current !== null) {
        room = roomRef.current;
        // The box at its full height (still scrolling) with the pager row drawn: its room is current, take it
        const pagerRow = body.lastElementChild as HTMLElement | null;
        const pagerDrawn = pagerOn && pagerRow !== null && pagerRow !== slot && pagerRow.getBoundingClientRect().height > 0;
        if (pagerDrawn && box.scrollHeight > box.clientHeight + 0.5) {
          room = inner - (bodyH - slotRect.height);
          roomRef.current = room;
        }
      } else {
        // No trusted room (geometry changed while paged): show every row; the next frame measures them
        showAll();
        if (state.current.pages !== 1) setPages(1);
        try { window.requestAnimationFrame(() => layout()); } catch { /* the poll measures */ }
        return;
      }
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
      let shown = Math.min(state.current.page, n - 1);
      // A fold was just toggled: show the page that holds its header (the open one, or after a close the header that was open),
      // so the fold the player clicked never lands on a page that is not shown
      const openIdx = openRowClass ? rows.findIndex((r) => r.className.indexOf(openRowClass) >= 0) : -1;
      if (openIdx >= 0) lastOpenRow.current = rows[openIdx];
      if (focusOpen.current > 0) {
        focusOpen.current--;
        const target = openIdx >= 0 ? openIdx : lastOpenRow.current ? rows.indexOf(lastOpenRow.current) : -1;
        if (target >= 0 && pageOf[target] !== shown) { shown = pageOf[target]; setPage(shown); }
      }
      rows.forEach((r, i) => {
        const want = pageOf[i] === shown ? "" : "none";
        if (r.style.display !== want) r.style.display = want;
      });
      if (n !== state.current.pages) setPages(n);
    } catch { /* paging is best effort */ }
  };

  // The layout effect sees the frame before this render; one pass on the next frame measures the rows it drew
  useLayoutEffect(() => {
    layout();
    let id = 0;
    try { id = window.requestAnimationFrame(() => layout()); } catch { return undefined; }
    return () => { try { window.cancelAnimationFrame(id); } catch { /* ignore */ } };
  });
  useEffect(() => {
    let id = 0;
    try { id = window.setInterval(layout, VIEW_PAGER_POLL_MS); } catch { return undefined; }
    return () => { try { window.clearInterval(id); } catch { /* ignore */ } };
  }, []);

  const shown = Math.min(page, pages - 1);
  return { start: 0, end: 0, page: shown, pages, setPage };
}
