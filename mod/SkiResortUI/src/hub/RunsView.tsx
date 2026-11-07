/*
 * src/hub/RunsView.tsx - the Runs view: one context area, the piste-map toggle and the run list of the shown ski area.
 * The context area holds the Quick runs, draw, tree tool or picked run's rows. It is empty when idle.
 * One list, no pager (this view and Lifts are the two that may scroll).
 * The piste-map check box shows C#'s value (pisteMap$), never a guess: a click sends setPisteMap(!value), and C# answers by pushing it.
 * RunLine sends hoverRun on enter (dedup). Leaving the list and unmounting send Null once (HubPanel covers the area switch and the panel unmount).
 * Joins: runLive by entityId (hubRules.runLiveMap), lift names by LiftRow.entity. The code memoizes both, so a live push re-renders only the
 * rows that moved. Panel file: subscribes selectedResort$, runs$, runLive$, lifts$, pisteMap$, city$, quickRuns$ and glade$, and the
 * local focused$. The prop rows (RunPropRows) act on the focused run: a click on its row picks it.
 */
import { useCallback, useEffect, useMemo, useState } from "react";
import type { RefObject } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { NULL_ENTITY, city$, drawRun$, entityId, glade$, hubTriggers, lifts$, pisteMap$, quickRuns$, runLive$, runs$, selectedResort$ } from "../bindings";
import type { RunRow } from "../bindings";
import { useT } from "../l10n";
import { runLiveMap } from "./hubRules";
import { focused$, sendHoverRun } from "./hubState";
import { HUB_LOC } from "./hubText";
import { CheckBox, Divider, Tip, cx, useNumberSamples } from "./parts";
import { RunHeader, RunLine } from "./RunList";
import { QuickRunsRows } from "./QuickRunsControls";
import { DrawRunRows, RunPropRows } from "./RunPropControls";
import { TreeRows } from "./TreeControls";
import { runsOf } from "./resortData";
import styles from "./hub.module.scss";

export function RunsView({ listRef }: { listRef: RefObject<HTMLDivElement> }): JSX.Element {
  const t = useT();
  const samples = useNumberSamples();
  const selected = useValue(selectedResort$);
  const runs = useValue(runs$);
  const runLive = useValue(runLive$);
  const lifts = useValue(lifts$);
  const pisteMap = useValue(pisteMap$);
  const city = useValue(city$);
  const focused = useValue(focused$);
  const quick = useValue(quickRuns$);
  const draw = useValue(drawRun$);
  const glade = useValue(glade$);
  const [renaming, setRenaming] = useState<string | null>(null);

  const liveMap = useMemo(() => runLiveMap(runLive), [runLive]);
  const liftNames = useMemo(() => {
    const m = new Map<string, string>();
    for (const l of lifts ?? []) if (l) m.set(entityId(l.entity), l.name);
    return m;
  }, [lifts]);

  // A view switch unmounts this view: clear the hover in C# once.
  useEffect(() => () => sendHoverRun(NULL_ENTITY), []);

  const onPick = useCallback((row: RunRow) => {
    focused$.update({ kind: "run", entity: row.entity, top: NULL_ENTITY });
    hubTriggers.focusRun(row.entity);
  }, []);
  const onPen = useCallback((row: RunRow) => {
    const id = entityId(row.entity);
    setRenaming((r) => (r === id ? null : id));
  }, []);
  const onRename = useCallback((row: RunRow, text: string) => {
    setRenaming(null);
    hubTriggers.rename(row.entity, text);
  }, []);
  const onRenameClose = useCallback(() => setRenaming(null), []);

  const rows = runsOf(runs, selected);
  const focusedId = focused.kind === "run" ? entityId(focused.entity) : "";
  const gating = city.snowGating;
  const backcountryPct = city.backcountryPct;

  // One context area above the list (hub redesign 2026-11-07): the Quick runs rows, the draw rows, the tree rows while the tree tool is on,
  // the picked run's rows. The area shows nothing (and no divider) when idle.
  const focusedRow = focusedId ? rows.find((r) => r.entity && entityId(r.entity) === focusedId) : undefined;
  const contextRows = quick?.active ? <QuickRunsRows />
    : draw.active ? <DrawRunRows draw={draw} />
      : glade?.active ? <TreeRows toggle={false} />
        : focusedRow ? <RunPropRows key={focusedId} row={focusedRow} /> : null;
  const context = contextRows && <>{contextRows}<Divider /></>;

  const toggle = (
    <Tip tip={t(HUB_LOC.pisteMapTip)}>
      <Button focusKey={FOCUS_DISABLED} theme={{ button: styles.pisteToggle }} selected={pisteMap} onSelect={() => hubTriggers.setPisteMap(!pisteMap)}>
        <CheckBox on={pisteMap} />
        {t(HUB_LOC.pisteMap)}
      </Button>
    </Tip>
  );

  return (
    <>
      {context}
      <RunHeader t={t} samples={samples} pisteMap={toggle} />
      {rows.length === 0 ? (
        <div className={styles.dimLine}>{t(HUB_LOC.runsEmpty)}</div>
      ) : (
        <div ref={listRef} className={cx(styles.list, styles.listAll)} onMouseLeave={() => sendHoverRun(NULL_ENTITY)}>
          {rows.map((row, i) => {
            const id = row.entity ? entityId(row.entity) : `row-${i}`;
            return (
              <RunLine
                key={id}
                row={row}
                live={liveMap.get(id)}
                liftName={row.served ? liftNames.get(entityId(row.servedBy)) : undefined}
                t={t}
                samples={samples}
                focused={id === focusedId}
                renaming={id === renaming}
                gating={gating}
                backcountryPct={backcountryPct}
                onPick={onPick}
                onPen={onPen}
                onRename={onRename}
                onRenameClose={onRenameClose}
              />
            );
          })}
        </div>
      )}
    </>
  );
}
