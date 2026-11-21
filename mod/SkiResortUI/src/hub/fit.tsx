/*
 * src/hub/fit.tsx - "never a scrollbar": the fold rows and the view pager.
 * The panel keeps a fixed height (HubPanel .panelFixed). Its content must fit at UI text scale 1.0 and 1.25 (harness/layout-check.js,
 * tools/test-hub-layout.js).
 * Fold: a section header row with a chevron. One fold per view is open at a time (accordion), so a view's height is its closed
 * height plus its tallest fold. Rows render flat, so useViewPager can page them when a view is still taller than the box.
 * FitContext carries the panel body's ref. Without it nothing is measured.
 * The fold and pager buttons pass focusKey={FOCUS_DISABLED} like every hub control (rule R15). The hub is mouse-only and never
 * takes the game's key focus.
 */
import { createContext, useEffect, useLayoutEffect, useRef, useState } from "react";
import type { ReactNode, RefObject } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { useT } from "../l10n";
import { folds$, openFold, toggleFold } from "./hubState";
import { PAGER_DOTS_MAX, nextPageTitleRow } from "./hubRules";
import { HUB_FMT, HUB_LOC } from "./hubText";
import { Glyph, LocText, MarkSlot, Tip, cx, numArg } from "./parts";
import styles from "./hub.module.scss";
import { anchoredPage } from "./hubRules";

export const GLYPH_FOLD_CLOSED = "Media/Glyphs/ThickStrokeArrowRight.svg";
export const GLYPH_FOLD_OPEN = "Media/Glyphs/ThickStrokeArrowDown.svg";

/** The panel body (HubPanel's `.body`): what the pager measures against */
export const FitContext = createContext<RefObject<HTMLDivElement> | null>(null);

/** A fold: a header row (label, optional value) with a chevron. Its children render only while it is the view's open fold */
export function Fold({ view, id, label, value, mark, tip, header, children }: {
  view: string;
  id: string;
  label: string | JSX.Element;
  value?: ReactNode;
  /** The "≈" slot before the value, as InfoLine's (undefined = no slot) */
  mark?: "≈" | "…" | null;
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
          {mark !== undefined && <MarkSlot mark={mark} />}
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
  /** T283: the title of the next page ("" = none: the button reads "Next page") */
  nextTitle: string;
  /** T283: the pager row (it sits in the footer, outside the body: the paged pass reads whether it is drawn) */
  rowRef: RefObject<HTMLDivElement>;
}

/**
 * The height the body's box could still grow into. The game's box is only as tall as its content: the box under the body is the vanilla
 * Scrollable's content box (block, height auto), which sits inside the panel content (flex 1 1 auto, down to the footer). On a short page
 * the box shrinks to that page and the rest of the panel content is empty (live DOM 2026-11-18: box 151-922, panel content 151-1680,
 * 758 px free). A box read as the room makes the room the page itself: the next pass pages at that size, and the page count climbs
 * (field passes 2 and 3: History page 2 / 3 held the season header and one row, 1300 px free below).
 * The free height is the inner bottom of the panel child that holds the body (the child of `.panelFixed`: the panel content), plus any
 * gap down to the top of its next sibling (the footer), minus the box's bottom. 0 when the box fills the panel content (it is the limit), when the box
 * reaches past it (the Scrollable clips there), or when there is no panel (.panelFixed) to measure against.
 */
function freeHeight(body: HTMLElement, box: HTMLElement): number {
  try {
    const panel = body.closest("[class*=\"sr-panelFixed_\"]") as HTMLElement | null;
    if (!panel) return 0;
    let content: HTMLElement | null = body;
    while (content && content.parentElement !== panel) content = content.parentElement;
    if (!content) return 0;
    const next = content.nextElementSibling as HTMLElement | null;
    // The panel content's own bottom padding and border are not room: the Scrollable ends above them
    const cs = getComputedStyle(content), cb = content.getBoundingClientRect().bottom;
    const limit = cb - (parseFloat(cs.paddingBottom) || 0) - (parseFloat(cs.borderBottomWidth) || 0) + Math.max(0, (next ? next.getBoundingClientRect().top : cb) - cb);
    return Math.max(0, limit - box.getBoundingClientRect().bottom);
  } catch { return 0; }
}
function innerHeight(el: HTMLElement): number {
  let pad = 0;
  try {
    const cs = getComputedStyle(el);
    pad = (parseFloat(cs.paddingTop) || 0) + (parseFloat(cs.paddingBottom) || 0);
  } catch { /* no computed style: 0 */ }
  return el.clientHeight - pad;
}

/** The pager row, T283 (1.0.4, the page-turn corners): HubPanel draws it at the top of the footer, over the build strip's tab row, while
 *  a view has more than one page. Back at the bottom left (from page 2, "‹ Back"), one dot per page in the middle (the "N / COUNT" text
 *  above PAGER_DOTS_MAX pages), and at the bottom right a button that names the next page ("This season ›": the fixed name of the part
 *  the next page starts in, a PagePart marker, nextPageTitleRow; "Next page" when there is none). On the last page the next arrow is
 *  dimmed, as before.
 *  `data-page` / `data-pages` and `data-pager` carry the position and the two sides for the layout test (harness/layout-check.js). */
export function Pager({ fit }: { fit: FitPage }): JSX.Element {
  const t = useT();
  const hasNext = fit.page < fit.pages - 1;
  const dots: JSX.Element[] = [];
  if (fit.pages <= PAGER_DOTS_MAX) for (let i = 0; i < fit.pages; i++) dots.push(<div key={i} className={cx(styles.pagerDot, i === fit.page && styles.pagerDotOn)} />);
  return (
    <div ref={fit.rowRef} className={styles.pagerRow} data-page={fit.page + 1} data-pages={fit.pages}>
      <div className={styles.pagerSide} data-pager="prev">
        {fit.page > 0 && (
          <Tip tip={t(HUB_LOC.pagePrev)}>
            <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.pagerButton, styles.pagerButtonText) }}
              onSelect={() => { if (fit.page > 0) fit.setPage(fit.page - 1); }}>
              <Glyph src="Media/Glyphs/ThickStrokeArrowLeft.svg" className={cx(styles.pagerGlyph, styles.pagerGlyphNext, styles.pagerGlyphBefore)} />
              <div className={styles.pagerLabel}>{t(HUB_LOC.pageBack)}</div>
            </Button>
          </Tip>
        )}
      </div>
      {fit.pages <= PAGER_DOTS_MAX
        ? <div className={styles.pagerDots}>{dots}</div>
        : <div className={styles.pagerText}>
          <LocText entry={HUB_FMT.pagePosition} args={{ N: numArg(fit.page + 1, Unit.Integer), COUNT: numArg(fit.pages, Unit.Integer) }} />
        </div>}
      <div className={cx(styles.pagerSide, styles.pagerSideEnd)} data-pager="next">
        <Tip tip={t(HUB_LOC.pageNext)}>
          {hasNext
            ? <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.pagerButton, styles.pagerButtonText) }}
              onSelect={() => { if (fit.page < fit.pages - 1) fit.setPage(fit.page + 1); }}>
              <div className={cx(styles.pagerLabel, styles.pagerLabelNext)}>{fit.nextTitle || t(HUB_LOC.pageNext)}</div>
              <Glyph src="Media/Glyphs/ThickStrokeArrowRight.svg" className={cx(styles.pagerGlyph, styles.pagerGlyphNext, styles.pagerGlyphAfter)} />
            </Button>
            : <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.pagerButton, styles.pagerButtonOff) }} onSelect={() => undefined}>
              <Glyph src="Media/Glyphs/ThickStrokeArrowRight.svg" className={styles.pagerGlyph} />
            </Button>}
        </Tip>
      </div>
    </div>
  );
}

/** T283: a row that can name a page: a PagePart marker (its data-page-title: the part's fixed name, owner review 2026-11-21), or a row
 *  with one of `titleClasses`, named by its label's text as drawn (HubPanel passes none: a heading can hold a month). */
function rowTitle(r: HTMLElement, titleClasses: readonly string[], labelClasses: readonly string[]): string | null {
  const own = r.getAttribute("data-page-title");
  if (own) return own;
  if (!titleClasses.some((c) => c && r.className.indexOf(c) >= 0)) return null;
  for (const c of labelClasses) {
    if (!c) continue;
    const el = r.getElementsByClassName(c)[0] as HTMLElement | undefined;
    const text = el ? (el.textContent || "").trim() : "";
    if (text) return text;
  }
  return null;
}


/** How often the view pager re-checks while the panel is open (a leaf's data push can add a row without re-rendering the panel) */
const VIEW_PAGER_POLL_MS = 400;
/** How long after a fold toggle the pager keeps that fold's header on the shown page (until the player pages) */
const FOLD_FOCUS_MS = 1000;

/**
 * Pages the view slot's top-level rows when the body is taller than the content box. The rows are the slot's DOM children: our
 * views render fragments, so every InfoLine, fold header, fold row, bar and chart block is one child.
 * The pager hides rows of other pages with style.display = "none". React never writes `style` on these row roots, so the value
 * stays. A pass writes a row's display only when it changes, so a steady page writes nothing.
 * A fold header is never the last row of a page. It moves to the next page.
 * When `enabled` is false (the Runs / Lifts views), the pager unhides everything.
 * A change of `resetKey` (view + open fold) returns to page 1. One exception: a fold toggle (`foldSeq` changed) shows the page
 * that holds that fold's header (`openRowClass`) for FOLD_FOCUS_MS or until the player pages. Reason: opening a fold whose rows
 * push it past page 1 must not hide the header the player just clicked.
 * A change of `geometryKey` (what changes the box, the Buildings tab's taller footer) drops the measured room. It also keeps an
 * open fold's page in view.
 * 1.0.4 price edges: between a pass and the next, the shown page follows its first row (hubRules.anchoredPage) until the player pages.
 * A row that grows, appears or goes above the next page (an edit in the Finances view) re-cuts the pages, but the rows in view stay.
 *
 * Gameface does not reflow on a read: getBoundingClientRect / clientHeight return the last DRAWN frame. Measured in game
 * 2026-11-07: a div inserted from a plain task reads 0 in the next animation frame callback. Its real height shows only in the
 * callback after that. A display write made inside a frame callback shows in the next one.
 * So any DOM change (our display writes, a render, a leaf that adds or removes rows) marks the geometry stale until two animation
 * frames pass. Only a pass on fresh geometry measures the row heights and the room. The pager keeps the heights per row, so hidden
 * rows keep the height they had when drawn.
 * A stale pass still pages with the cached heights and room, so a page switch hides the old page at once.
 * Measuring stale frames was field bug r611 (the Attraction view with the Merged buildings fold open on the Buildings tab, about
 * 21 px over). Polls read "fits", dropped the pager and showed all rows. Then they read "overflows" and paged again, so the view
 * and the pager flickered. Also, a fold opened onto a page that was not shown, and the fold vanished until a view switch.
 */
export function useViewPager(viewRef: RefObject<HTMLElement>, bodyRef: RefObject<HTMLElement>, enabled: boolean, resetKey: string,
  foldRowClass: string, geometryKey = "", openRowClass = "", foldSeq = 0, keepTailClass = "",
  titleClasses: readonly string[] = [], labelClasses: readonly string[] = [], skipClass = ""): FitPage {
  const [pages, setPages] = useState(1);
  const [page, setPage] = useState(0);
  // T283: the next page's title (nextPageTitleRow over the rows' titles) and the pager row, drawn in the footer by HubPanel
  const [nextTitle, setNextTitle] = useState("");
  const rowRef = useRef<HTMLDivElement>(null);
  const keyRef = useRef(resetKey);
  if (keyRef.current !== resetKey) {
    keyRef.current = resetKey;
    if (page !== 0) setPage(0);
  }
  // A fold toggle within the view: show the toggled fold's page until FOLD_FOCUS_MS passes or the player pages
  const focusUntil = useRef(0);
  const lastOpenRow = useRef<HTMLElement | null>(null);
  const foldSeqRef = useRef(foldSeq);
  if (foldSeqRef.current !== foldSeq) {
    foldSeqRef.current = foldSeq;
    focusUntil.current = Date.now() + FOLD_FOCUS_MS;
  }
  const nextTitleRef = useRef(nextTitle);
  const state = useRef({ page, pages, enabled });
  state.current = { page, pages, enabled };
  // Row heights from fresh frames where the row was drawn, and the room for the view rows from the last fresh measurement
  const heightOf = useRef(new WeakMap<HTMLElement, number>());
  const roomRef = useRef<number | null>(null);
  // The first row of the shown page at the last fresh pass, its page, and the view it belongs to (resetKey): see anchoredPage
  const anchorRef = useRef<{ row: HTMLElement | null; page: number; key: string }>({ row: null, page: -1, key: "" });
  const geometryRef = useRef(geometryKey);
  if (geometryRef.current !== geometryKey) {
    // Another footer: re-measure, and keep an open fold in view (it was on screen before the switch)
    geometryRef.current = geometryKey;
    roomRef.current = null;
    focusUntil.current = Date.now() + FOLD_FOCUS_MS;
  }
  // Fresh = no DOM change since two animation frames ago (see the note above). Every change re-arms the wait
  const fresh = useRef(false);
  const staleSeq = useRef(0);
  const layoutRef = useRef<() => void>(() => undefined);
  const markStale = (): void => {
    fresh.current = false;
    const my = ++staleSeq.current;
    const done = (): void => { if (staleSeq.current === my) { fresh.current = true; layoutRef.current(); } };
    try { window.requestAnimationFrame(() => { try { window.requestAnimationFrame(done); } catch { done(); } }); } catch { window.setTimeout(done, 50); }
  };

  const layout = (): void => {
    try {
      const slot = viewRef.current, body = bodyRef.current;
      const box = body ? body.parentElement : null;
      if (!slot || !body || !box) return;
      const rows = Array.prototype.slice.call(slot.children) as HTMLElement[];
      let wrote = false;
      const setDisplay = (r: HTMLElement, want: string): void => { if (r.style.display !== want) { r.style.display = want; wrote = true; } };
      const finish = (): void => { if (wrote) markStale(); };
      if (!state.current.enabled) {
        for (const r of rows) setDisplay(r, "");
        roomRef.current = null;
        if (state.current.pages !== 1) setPages(1);
        finish();
        return;
      }
      const isFresh = fresh.current;
      const pagerOn = state.current.pages > 1;
      let room: number | null = roomRef.current;
      if (isFresh) {
        // The last drawn frame shows exactly the rows whose style is not "none"
        // What the body may fill: the box, plus the panel height it has shrunk away from (freeHeight)
        const inner = innerHeight(box) + freeHeight(body, box);
        const bodyH = body.getBoundingClientRect().height;
        const slotRect = slot.getBoundingClientRect();
        const tops = rows.map((r) => r.getBoundingClientRect().top);
        const drawn: number[] = [];
        for (let i = 0; i < rows.length; i++) if (rows[i].style.display !== "none") drawn.push(i);
        // A drawn row's height: down to the next drawn row when that is the next row, else to the slot's end (last drawn), else its box
        drawn.forEach((i, k) => {
          const next = k + 1 < drawn.length ? drawn[k + 1] : -1;
          const h = next === i + 1 ? tops[next] - tops[i] : next < 0 ? slotRect.bottom - tops[i] : rows[i].getBoundingClientRect().height;
          heightOf.current.set(rows[i], Math.max(0, h));
        });
        if (drawn.length === rows.length) {
          // Every row drawn: the geometry is the view's own
          if (!pagerOn && bodyH <= inner + 0.5) { roomRef.current = null; if (state.current.pages !== 1) setPages(1); return; }
          // Room for the view = the box minus everything in the body that is not the view (the pager row included once it is drawn)
          room = inner - (bodyH - slotRect.height) - (pagerOn ? 0 : (heightOf.current.get(rows[0]) ?? 20));
        } else {
          // Paged: the box may be shrunk to the shown page, so `inner` includes the free panel height. The room changes only while the body
          // is over it, with the pager drawn
          // T283: the pager row sits in the footer, so the box (and `inner`) already lost its height once it is drawn
          const pagerRow = rowRef.current;
          const pagerDrawn = pagerOn && pagerRow !== null && pagerRow.getBoundingClientRect().height > 0;
          // scrollHeight and clientHeight are whole pixels, so a body under 1 px too tall reads "fits": compare the body itself too
          const over = bodyH - inner;
          if (pagerDrawn && (over > 0.1 || (inner === innerHeight(box) && box.scrollHeight > box.clientHeight + 0.5))) {
            room = inner - (bodyH - slotRect.height);
            // The cached row heights can sum a hair under the slot's real height (margins), so the room above would not drop a row: the
            // shown rows must lose what the body is over (a body 0.7 px too tall was a 1 px scrollbar in the harness, Field case 1.25)
            if (over > 0.1) {
              let shownSum = 0;
              for (const r of rows) if (r.style.display !== "none") shownSum += heightOf.current.get(r) ?? 0;
              if (shownSum > 0) room = Math.min(room, shownSum - over);
            }
          }
        }
        roomRef.current = room;
      }
      if (room === null) {
        // No room measured yet (or the footer changed): show every row. The fresh pass after it is drawn measures
        for (const r of rows) setDisplay(r, "");
        if (state.current.pages !== 1) setPages(1);
        finish();
        return;
      }
      // Rows never drawn on a fresh frame (just added) count 0 until one is. The fresh pass two frames on corrects the split
      const heights = rows.map((r) => heightOf.current.get(r) ?? 0);
      // A row with keepTailClass starts a block that runs to the next such row (or the last row). A block stays on one page while it
      // fits a page alone (History: "This season so far" must not start page 2 at Net without its header, and the revenue chart under it
      // keeps its own block). Too tall for a page: it splits like any rows.
      const keepStarts: number[] = [];
      if (keepTailClass) rows.forEach((r, i) => { if (r.className.indexOf(keepTailClass) >= 0) keepStarts.push(i); });
      const keepBlockOf: number[] = rows.map(() => -1);   // row i -> the first row of its block when that block fits a page alone
      for (let k = 0; k < keepStarts.length; k++) {
        const from = keepStarts[k], to = k + 1 < keepStarts.length ? keepStarts[k + 1] : rows.length;
        let tail = 0;
        for (let i = from; i < to; i++) tail += heights[i];
        if (tail <= room) for (let i = from; i < to; i++) keepBlockOf[i] = from;
      }
      const pageOf: number[] = [];
      let p = 0, acc = 0, start = 0;
      for (let i = 0; i < rows.length; i++) {
        if (acc + heights[i] > room && i > start) {
          // Rows moved to the next page with row i: the whole keep block when i is inside it, else a fold header (kept with its first row)
          let from = i;
          if (keepBlockOf[i] >= 0 && i > keepBlockOf[i] && keepBlockOf[i] > start) from = keepBlockOf[i];
          else if (i - 1 > start && rows[i - 1].className.indexOf(foldRowClass) >= 0) from = i - 1;
          acc = 0;
          for (let j = from; j < i; j++) { pageOf[j] = p + 1; acc += heights[j]; }
          p++; start = from;
        }
        pageOf[i] = p;
        acc += heights[i];
      }
      const n = p + 1;
      const anchor = anchorRef.current.key === keyRef.current ? anchorRef.current : null;
      let shown = anchoredPage(pageOf, state.current.page, anchor && anchor.row ? rows.indexOf(anchor.row) : -1, anchor ? anchor.page : -1);
      // A fold was just toggled: show the page that holds its header (the open one, or after a close the header that was open)
      const openIdx = openRowClass ? rows.findIndex((r) => r.className.indexOf(openRowClass) >= 0) : -1;
      if (openIdx >= 0) lastOpenRow.current = rows[openIdx];
      if (Date.now() < focusUntil.current) {
        const target = openIdx >= 0 ? openIdx : lastOpenRow.current ? rows.indexOf(lastOpenRow.current) : -1;
        if (target >= 0 && pageOf[target] !== shown) shown = pageOf[target];
      }
      if (isFresh) anchorRef.current = { row: rows[pageOf.indexOf(shown)] ?? null, page: shown, key: keyRef.current };

      rows.forEach((r, i) => setDisplay(r, pageOf[i] === shown ? "" : "none"));
      // T283: name the next page by its first section (a hidden row keeps its text, so the title reads the same before it is shown)
      const ti = nextPageTitleRow(pageOf, rows.map((r) => rowTitle(r, titleClasses, labelClasses) !== null), shown,
        rows.map((r) => !!skipClass && r.className.indexOf(skipClass) >= 0));
      const title = ti >= 0 ? rowTitle(rows[ti], titleClasses, labelClasses) || "" : "";
      if (title !== nextTitleRef.current) { nextTitleRef.current = title; setNextTitle(title); }
      if (n !== state.current.pages) setPages(n);
      if (shown !== state.current.page) setPage(shown);
      finish();
    } catch { /* paging is best effort */ }
  };
  layoutRef.current = layout;

  // Every render may have changed the rows: page now with what is known, measure once the change is drawn
  useLayoutEffect(() => {
    markStale();
    layout();
  });
  // Rows a leaf adds or removes without re-rendering the panel (a data push) get the same treatment. The poll is the safety net
  useEffect(() => {
    let ob: MutationObserver | null = null;
    let id = 0;
    try {
      const slot = viewRef.current;
      if (slot) {
        ob = new MutationObserver(() => markStale());
        ob.observe(slot, { childList: true, subtree: true });
      }
    } catch { ob = null; }
    try { id = window.setInterval(() => layoutRef.current(), VIEW_PAGER_POLL_MS); } catch { /* the observer and renders page */ }
    return () => {
      try { if (ob) ob.disconnect(); } catch { /* ignore */ }
      try { window.clearInterval(id); } catch { /* ignore */ }
      staleSeq.current++;
    };
  }, []);

  const shown = Math.min(page, pages - 1);
  const pageTo = (p: number): void => { focusUntil.current = 0; setPage(p); };
  return { start: 0, end: 0, page: shown, pages, setPage: pageTo, nextTitle, rowRef };
}
