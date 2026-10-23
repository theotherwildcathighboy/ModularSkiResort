/*
 * src/hub/RunsView.tsx - the Runs view: the tree tool rows, the piste-map toggle and the run list of the shown ski area.
 *   - Structure: TreeRows (TreeControls.tsx) · Divider · list header (piste-map toggle, Lift / Length / Drop labels) · the
 *     rows (RunList.tsx RunLine, C#'s order) or RUNS_EMPTY. Every run is in one list, no pager; this view and the Lifts view
 *     are the two that may scroll (PIPELINE section 0 hub rule; harness/layout-check.js checks every row is drawn).
 *   - Piste map: a cs2/ui Button (theme `pisteToggle`) whose check box shows C#'s value (pisteMap$), never a guess; a click
 *     sends setPisteMap(!value) and C# answers by pushing the new value.
 *   - Hover: RunLine sends hoverRun on mouse enter (hubState.sendHoverRun dedups); leaving the list and unmounting this view
 *     send Null once. HubPanel covers the area switch and the panel's unmount.
 *   - Click a row: focused$ (highlight) + focusRun (C# flies the camera). One row renames at a time (local state, entityId);
 *     commit -> rename(run, text).
 *   - Joins: runLive by entityId (hubRules.runLiveMap, memoised on the array), lift names by LiftRow.entity (memoised on
 *     lifts$), so a live push re-renders only the rows that moved.
 *   - The three run state words (RUN_STATE_TEXT) are translated once per `t` (useMemo) and handed to every RunLine, which
 *     shows its run's word in their common width.
 *   - Panel file: subscribes selectedResort$, runs$, runLive$, lifts$, pisteMap$, city$ (+ glade$ in TreeRows); local focused$.
 */
import { useCallback, useEffect, useMemo, useState } from "react";
import type { RefObject } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { NULL_ENTITY, city$, entityId, hubTriggers, lifts$, pisteMap$, runLive$, runs$, selectedResort$ } from "../bindings";
import type { RunRow } from "../bindings";
import { useT } from "../l10n";
import { runLiveMap } from "./hubRules";
import { focused$, sendHoverRun } from "./hubState";
import { HUB_LOC, RUN_STATE_TEXT } from "./hubText";
import { CheckBox, Divider, Tip, cx, useNumberSamples } from "./parts";
import { RunHeader, RunLine } from "./RunList";
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
  const [renaming, setRenaming] = useState<string | null>(null);

  const liveMap = useMemo(() => runLiveMap(runLive), [runLive]);
  const stateWords = useMemo(() => RUN_STATE_TEXT.map((e) => t(e)), [t]);
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

  const toggle = (
    <Tip tip={t(HUB_LOC.pisteMapTip)}>
      <Button theme={{ button: styles.pisteToggle }} selected={pisteMap} onSelect={() => hubTriggers.setPisteMap(!pisteMap)}>
        <CheckBox on={pisteMap} />
        {t(HUB_LOC.pisteMap)}
      </Button>
    </Tip>
  );

  return (
    <>
      <TreeRows />
      <Divider />
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
                stateWords={stateWords}
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
