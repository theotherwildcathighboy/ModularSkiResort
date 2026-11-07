/*
 * src/hub/HubPanel.tsx - the Ski Resort hub panel: frame, link states, layout.
 * HubRoot mounts this only while the hub is open, so every useValue() below unsubscribes on close and C# stops computing panel data.
 * Link states (useHubConnection): absent -> "not connected" notice; mismatch -> plain title and core bindings only (C# never gates triggers
 * on the contract); ok -> switcher, status block, tiles, view, BuildStrip footer. OkPanel subscribes only the slow bindings; leaf components
 * subscribe the live ones. The panel height is FIXED (.panelFixed) and the Scrollable's gutter is zeroed: see the note.
 * Engine notes: EngineNotes/HubPanel.tsx.md
 */
import { useEffect, useRef } from "react";
import { useValue } from "cs2/api";
import { Panel } from "cs2/ui";
import { HUB_CONTRACT, NULL_ENTITY, build$, liftLine$, lifts$, quickRuns$, resorts$, runReplaced$, runs$, sameEntity, selectedResort$ } from "../bindings";
import { LOC, useT } from "../l10n";
import { BuildStrip, MISMATCH_FOOTER, OkFooter } from "./BuildStrip";
import { ErrorBoundary } from "./ErrorBoundary";
import { GuestsView } from "./GuestsView";
import { HistoryView } from "./HistoryView";
import { AreaList, HubHeader } from "./HubHeader";
import { NO_FOCUS, areaListOpen$, buildTab$, focused$, folds$, openFold, problemsOpen$, resetHoverRun, runToolSeen, sendHoverRun, useHubConnection, view$ } from "./hubState";
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
import { activeToolKey, toolTabFollow } from "./hubRules";
import { currentResort, liftsOf, resortEntries, runsOf } from "./resortData";
import styles from "./hub.module.scss";

export interface HubPanelProps {
  onClose: () => void;
}

/** Right side, near the top */
export const HUB_PANEL_INITIAL_POSITION = { x: 0.98, y: 0.15 };

/** The backdrop of the two overlays (area list, problems popover): a click anywhere else in the content closes them */
function OverlayBackdrop(): JSX.Element | null {
  const areas = useValue(areaListOpen$);
  const probs = useValue(problemsOpen$);
  if (!areas && !probs) return null;
  return (
    <div className={styles.overlayBackdrop} onClick={() => { areaListOpen$.update(false); problemsOpen$.update(false); }} />
  );
}

/** The active tool CHANGING to a run tool (run drawing, cat track, Quick runs) selects the Runs view, to a lift tool (a lift line item, the
 *  lift tool) the Lifts view, where its info shows (hubRules.toolTabFollow). The same tool staying active selects nothing. Renders nothing;
 *  subscribes only here, so the panel body does not re-render on build updates. */
function RunsTabFollow(): null {
  const items = useValue(build$);
  const quick = useValue(quickRuns$);
  const lift = useValue(liftLine$);
  const key = activeToolKey(items, quick?.active, lift?.active);
  useEffect(() => {
    const step = toolTabFollow(runToolSeen.value, key);
    runToolSeen.value = step.seen;
    if (step.select && view$.value !== step.select) view$.update(step.select);
  }, [key]);
  useEffect(() => () => { runToolSeen.value = ""; }, []);
  return null;
}

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

  // Panel mount: forget the last hover (no send); unmount: clear it in C#
  useEffect(() => {
    resetHoverRun();
    areaListOpen$.update(false);
    problemsOpen$.update(false);
    return () => sendHoverRun(NULL_ENTITY);
  }, []);

  // Area switch: keep the view, drop a focused row of the old area, clear the hover
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
    problemsOpen$.update(false);   // the popover lists the old area's rows
  }, [areaKey]);

  // A re-emitted run (Groomed toggled: a new entity, DECISIONS 577): the focus follows it
  const replaced = useValue(runReplaced$);
  useEffect(() => {
    const f = focused$.value;
    if (f.kind === "run" && sameEntity(f.entity, replaced.from)) focused$.update({ kind: "run", entity: replaced.to, top: NULL_ENTITY });
  }, [replaced.seq]);

  // The view pager (fit.tsx): a view still taller than the box after its folds shows its rows page by page. Runs and Lifts
  // list every row and may scroll. A view switch, a fold toggle or an area switch returns to page 1.
  const folds = useValue(folds$);
  // The Buildings tab's footer is taller (two tile rows and the merge toggle): a tab switch re-renders the panel so the pager re-fits at
  // once (its layout effect), not on its next poll (a 400 ms scrollbar flash), and drops the room it measured for the other footer.
  const buildTab = useValue(buildTab$);
  const viewPager = useViewPager(viewRef, contentRef, hasSign && view !== "runs" && view !== "lifts",
    `${view}|${openFold(folds, view)}|${areaKey}`, styles.foldRow, buildTab);

  // Plain markup only: an error-boundary fallback must not be able to throw
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
      case "history": shownView = <HistoryView />; break;
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
          <RunsTabFollow />
          <ErrorBoundary where="panel.areas" fallback={failed}>
            <AreaList />
          </ErrorBoundary>
        </div>
      </FitContext.Provider>
    </Panel>
  );
}

/** contract 0 (absent) or a mismatch: core only */
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
