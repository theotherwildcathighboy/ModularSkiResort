/*
 * src/hub/HubPanel.tsx - the Ski Resort hub panel: frame, link states, layout.
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-A: placeholder;
 * replaced by the real panel the same day, implementer U1-C); reworked 2026-10-03 (Round U2,
 * implementer I-3) for docs/research/hub-u2-spec.md sections 4.1, 4.2, 4.4 J6 / J8, 4.5, 4.6;
 * rewritten 2026-10-04 for contract 3 (Round E1, stream S6; docs/research/econ-round/spec/
 * ch6-hub-ui.md sections 6.1-6.2, design hub-v3-design.md 4 / 7.1; code from the checked reference
 * spec/ch6-hub-ui-ref/src/hub/HubPanel.tsx).
 * Contract:
 *   - HubRoot (src/hub/HubRoot.tsx) mounts this ONLY while the hub is open, inside its own
 *     ErrorBoundary; every useValue() below therefore unsubscribes when the hub closes and C#
 *     stops computing panel data (research 2.F.6; the stats system's Demand, spec 3.6).
 *   - props.onClose = the X button and Escape: cs2/ui Panel binds onClose to its close button and
 *     to the Close/Back input action (research 2.F.4). HubRoot routes it to C# setHubOpen(false).
 *   - LINK STATES (spec 4.5), from useHubConnection (src/hub/hubState.ts):
 *       absent (contract 0)  -> the U1 "not connected" notice, nothing else (no subscription);
 *       mismatch (!= HUB_CONTRACT, 4 since Round S1)
 *                            -> plain title; content = the CONTRACT_MISMATCH_CORE line + the Trees
 *                               foldout; footer = the Build strip with MISMATCH_FOOTER. ONLY CORE
 *                               bindings are read (build, glade): building still works, because C#
 *                               never gates triggers on the contract. LocalizedString fills {UI}/{DLL}
 *                               in the English fallback even when an old DLL lacks the key
 *                               (index.js@435324);
 *       ok (= HUB_CONTRACT)  -> header = the ski-area switcher; content = the start card while the
 *                               city has no live sign (deviation U9: even when unlinked runs or lifts
 *                               exist), else the status line + problems band (StatusBlock), the six
 *                               tiles (ViewTiles) and the selected view (Overview, Runs, Lifts, Guests,
 *                               Money = Finances, History since contract 10); footer = OkFooter -> BuildStrip.
 *     Each part sits in its own ErrorBoundary: a crash in one leaves the others usable; the view's
 *     boundary is keyed by the view, so a crashed view recovers on a switch.
 *   - RENDER COST (ch6 section 6.15, design 8.1 "why the split"): OkPanel subscribes ONLY the slow
 *     bindings (resorts$, selectedResort$, runs$, lifts$) and the local view$, so a snow, guest or
 *     money push (resortLive$, runLive$, problems$) never re-renders the frame, the header or the
 *     difficulty bar: the leaf components that show those numbers subscribe them.
 *   - T114 (2026-10-20, docs/research/ui/hub-round-t114-2026-10-20.md): the body is the anchor (.bodyAnchor, position relative) of
 *     two overlays drawn over the top of the content: the header's ski-area list (HubHeader AreaList) and the problems band's +N
 *     popover (StatusBlock ProblemsPopover, anchored under the band); ONE dimming backdrop (OverlayBackdrop) closes either; both
 *     close on panel mount and on an area switch. The body's ref is FitContext for the
 *     list pager (fit.tsx useFitPage). The panel's content must fit without the Scrollable's bar (harness/layout-check.js).
 *   - LAYOUT (spec 4.1). cs2/ui Panel renders header, content and footer as three divs, the
 *     content div holding the Scrollable (index.js@684600; Panel wrapper `uS` @733600), so the
 *     footer never scrolls. DraggablePanel places itself at top = y * (viewport - panel height)
 *     (`Gj`, index.js@720659), so a content-sized panel moves its header, X and footer whenever its
 *     content changes height (J6). Fix: `.panelFixed { height: 72vh; max-height: 780rem }` (T114: 78vh / 845rem),
 *     applied while a live sign, a run row or a lift row exists; otherwise the U1 `max-height: 72vh`
 *     (the start card and the mismatch state are short panels; the one change of height happens at
 *     a state change). Width formula unchanged (U1 fixer round, resort-hub-understand.md 5.6 item 5).
 *   - J8: the Scrollable reserves a 16rem gutter when its content overflows
 *     (index.css@29300 `.scrollable_DXr.track-visible-y_RCA .content_gqa{padding-right:
 *     var(--scrollbarPadding)}`, --scrollbarPadding = --scrollbarSize 16rem @28600), which reflows
 *     every row the moment the list grows past the panel. hub.module.scss sets
 *     `--scrollbarPadding: 0rem` on the Scrollable (the content div's child), as vanilla does on
 *     its own Scrollables (index.css@133050, @255350), and keeps a constant right padding of its
 *     own [likely: that the Scrollable is the content div's direct child].
 *   - Placement: draggable, opening on the right (initialPosition {0.98, 0.15}); a dragged
 *     position resets when the panel remounts (index.js@720659). Nothing writes a scroll position
 *     (design 7.1).
 *   - uiLayout (src/hub/layoutReport.ts): refs on the header, content, footer, the rem probe (out of
 *     flow, never drawn, in the header slot), the status row, the problems band, the tile row, the
 *     view slot and the shown list; rows = the shown area's run rows (Runs view), lift rows (Lifts
 *     view), else 0.
 *   - hoverRun (D19, design 7.3, deviation U18): mount forgets the last hover without sending;
 *     unmount sends Null; an area switch drops a focused row of the old area and sends Null. The
 *     view is kept across an area switch (design 7.1).
 */
import { useEffect, useRef } from "react";
import { useValue } from "cs2/api";
import { Panel } from "cs2/ui";
import { HUB_CONTRACT, NULL_ENTITY, lifts$, resorts$, runs$, sameEntity, selectedResort$ } from "../bindings";
import { LOC, useT } from "../l10n";
import { BuildStrip, MISMATCH_FOOTER, OkFooter } from "./BuildStrip";
import { ErrorBoundary } from "./ErrorBoundary";
import { GuestsView } from "./GuestsView";
import { HistoryView } from "./HistoryView";
import { AreaList, HubHeader } from "./HubHeader";
import { NO_FOCUS, areaListOpen$, focused$, folds$, openFold, problemsOpen$, resetHoverRun, sendHoverRun, useHubConnection, view$ } from "./hubState";
import { FitContext, Pager, useViewPager } from "./fit";
import type { HubConnection } from "./hubState";
import { HUB_FMT, HUB_LOC } from "./hubText";
import { useLayoutReport } from "./layoutReport";
import { LiftsView } from "./LiftsView";
import { MoneyView } from "./MoneyView";
import { OverviewView } from "./OverviewView";
import { LocText, cx } from "./parts";
import { RunsView } from "./RunsView";
import { StartCard } from "./StartCard";
import { StatusBlock } from "./StatusBlock";
import { TreesFoldout } from "./TreeControls";
import { ViewTiles } from "./ViewTiles";
import { currentResort, liftsOf, resortEntries, runsOf } from "./resortData";
import styles from "./hub.module.scss";

export interface HubPanelProps {
  /** Close the hub (X button / Escape). */
  onClose: () => void;
}

/** Right side, near the top (research 2.F.8). */
export const HUB_PANEL_INITIAL_POSITION = { x: 0.98, y: 0.15 };

/** T114: the backdrop of the two overlays (area list, problems popover): a click anywhere else in the content closes them. */
function OverlayBackdrop(): JSX.Element | null {
  const areas = useValue(areaListOpen$);
  const probs = useValue(problemsOpen$);
  if (!areas && !probs) return null;
  return (
    <div className={styles.overlayBackdrop} onClick={() => { areaListOpen$.update(false); problemsOpen$.update(false); }} />
  );
}

/** contract = HUB_CONTRACT (3 in Round E1, 4 since Round S1): everything. */
function OkPanel({ onClose, stampNote }: HubPanelProps & { stampNote: string | null }): JSX.Element {
  const t = useT();
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const runs = useValue(runs$);
  const lifts = useValue(lifts$);
  const view = useValue(view$);
  const headerRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const footerRef = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const probeRef = useRef<HTMLDivElement>(null);
  const statusRef = useRef<HTMLDivElement>(null);
  const problemsRef = useRef<HTMLDivElement>(null);
  const tilesRef = useRef<HTMLDivElement>(null);
  const viewRef = useRef<HTMLDivElement>(null);

  const entries = resortEntries(resorts);
  const hasSign = entries.signs.length > 0;
  const cur = currentResort(entries, selected);
  const shownSign = cur.row && !cur.isUnlinked ? cur.row.sign : NULL_ENTITY;
  const fixed = hasSign || (runs?.length ?? 0) > 0 || (lifts?.length ?? 0) > 0;
  const rows = view === "runs" ? runsOf(runs, shownSign).length : view === "lifts" ? liftsOf(lifts, shownSign).length : 0;
  useLayoutReport({
    header: headerRef, content: contentRef, footer: footerRef, list: listRef, probe: probeRef,
    status: statusRef, problems: problemsRef, tiles: tilesRef, view: viewRef,
  }, rows, view);

  // Panel mount: forget the last hover (no send); unmount: clear it in C# (design 7.3).
  useEffect(() => {
    resetHoverRun();
    areaListOpen$.update(false);   // T114: the overlays start closed
    problemsOpen$.update(false);
    return () => sendHoverRun(NULL_ENTITY);
  }, []);

  // Area switch: keep the view, drop a focused row of the old area, clear the hover (design 7.1).
  const areaKey = `${shownSign.index}:${shownSign.version}:${cur.isUnlinked ? 1 : 0}`;
  useEffect(() => {
    const f = focused$.value;
    if (f.kind !== "none") {
      const still = f.kind === "run"
        ? runsOf(runs, shownSign).some((r) => sameEntity(r.entity, f.entity))
        : liftsOf(lifts, shownSign).some((l) => sameEntity(l.entity, f.entity) && sameEntity(l.top, f.top));
      if (!still) focused$.update(NO_FOCUS);
    }
    sendHoverRun(NULL_ENTITY);
    problemsOpen$.update(false);   // T114: the popover lists the old area's rows
  }, [areaKey]);

  // T114: the view pager (fit.tsx): a view still taller than the box after its folds shows its rows page by page. Runs and Lifts page
  // their own lists. A view switch, a fold toggle or an area switch returns to page 1.
  const folds = useValue(folds$);
  const viewPager = useViewPager(viewRef, contentRef, hasSign && view !== "runs" && view !== "lifts",
    `${view}|${openFold(folds, view)}|${areaKey}`, styles.foldRow);

  // Plain markup only: an error-boundary fallback must not be able to throw.
  const title = t(LOC.title);
  const failed = <div className={styles.sectionFailed}>{t(HUB_LOC.sectionFailed)}</div>;

  let body: JSX.Element;
  if (!hasSign) {
    body = (
      <ErrorBoundary where="panel.start" fallback={failed}>
        <StartCard />
      </ErrorBoundary>
    );
  } else {
    let shownView: JSX.Element;
    switch (view) {
      case "runs": shownView = <RunsView listRef={listRef} />; break;
      case "lifts": shownView = <LiftsView listRef={listRef} />; break;
      case "guests": shownView = <GuestsView />; break;
      case "money": shownView = <MoneyView />; break;
      case "history": shownView = <HistoryView />; break;   // T78 (contract 10)
      default: shownView = <OverviewView />; break;
    }
    body = (
      <>
        <ErrorBoundary where="panel.status" fallback={failed}>
          <StatusBlock statusRef={statusRef} problemsRef={problemsRef} />
        </ErrorBoundary>
        <ErrorBoundary where="panel.tiles" fallback={failed}>
          <ViewTiles tilesRef={tilesRef} />
        </ErrorBoundary>
        <div ref={viewRef} className={styles.viewSlot}>
          <ErrorBoundary key={view} where={`panel.${view}`} fallback={failed}>
            {shownView}
          </ErrorBoundary>
        </div>
        {viewPager.pages > 1 && <Pager fit={viewPager} />}
      </>
    );
  }

  return (
    <Panel
      draggable
      header={
        <div ref={headerRef} className={styles.headerSlot}>
          <div ref={probeRef} className={styles.remProbe} />
          <ErrorBoundary where="panel.header" fallback={<span className={styles.plainTitle}>{title}</span>}>
            <HubHeader />
          </ErrorBoundary>
        </div>
      }
      footer={
        <div ref={footerRef} className={styles.footerSlot}>
          <ErrorBoundary where="panel.build" fallback={failed}>
            <OkFooter />
          </ErrorBoundary>
        </div>
      }
      onClose={onClose}
      initialPosition={HUB_PANEL_INITIAL_POSITION}
      className={cx(styles.panel, fixed && styles.panelFixed)}
      contentClassName={styles.panelContent}
    >
      <FitContext.Provider value={contentRef}>
        <div ref={contentRef} className={cx(styles.body, styles.bodyAnchor)}>
          {body}
          {stampNote && <div className={styles.stampNote}>{stampNote}</div>}
          <OverlayBackdrop />
          <ErrorBoundary where="panel.areas" fallback={failed}>
            <AreaList />
          </ErrorBoundary>
        </div>
      </FitContext.Provider>
    </Panel>
  );
}

/** contract 0 (absent) or a mismatch: CORE only (spec 4.5). */
function LinkPanel({ onClose, c }: HubPanelProps & { c: HubConnection }): JSX.Element {
  const t = useT();
  const failed = <div className={styles.sectionFailed}>{t(HUB_LOC.sectionFailed)}</div>;
  if (c.state === "absent") {
    return (
      <Panel
        draggable
        header={t(LOC.title)}
        onClose={onClose}
        initialPosition={HUB_PANEL_INITIAL_POSITION}
        className={styles.panel}
        contentClassName={styles.panelContent}
      >
        <div className={styles.notice}>{t(LOC.notConnected)}</div>
      </Panel>
    );
  }
  return (
    <Panel
      draggable
      header={t(LOC.title)}
      footer={
        <div className={styles.footerSlot}>
          <ErrorBoundary where="panel.build" fallback={failed}>
            <BuildStrip info={MISMATCH_FOOTER} />
          </ErrorBoundary>
        </div>
      }
      onClose={onClose}
      initialPosition={HUB_PANEL_INITIAL_POSITION}
      className={styles.panel}
      contentClassName={styles.panelContent}
    >
      <div className={styles.body}>
        <div className={styles.notice}>
          <LocText entry={HUB_FMT.contractMismatchCore} args={{ UI: String(HUB_CONTRACT), DLL: String(c.dllContract) }} />
        </div>
        <ErrorBoundary where="panel.trees" fallback={failed}>
          <TreesFoldout />
        </ErrorBoundary>
      </div>
    </Panel>
  );
}

export function HubPanel({ onClose }: HubPanelProps): JSX.Element {
  const t = useT();
  const c = useHubConnection();
  if (c.state !== "ok") return <LinkPanel onClose={onClose} c={c} />;
  const stampNote = !c.stampMatches && !c.standaloneBundle ? t(LOC.stampDiffers) : null;
  return <OkPanel onClose={onClose} stampNote={stampNote} />;
}
