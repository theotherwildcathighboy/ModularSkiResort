/*
 * src/hub/RunsView.tsx - the Runs view (Runs tile): the tree tool rows, the piste-map toggle and the
 * run list of the shown ski area.
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/
 * spec/ch6-hub-ui.md section 6.8, design hub-v3-design.md 3.2, 7.2, 7.3; replaces the Runs tab and
 * focus strip of the contract-2 ResortLists.tsx, deleted).
 * Contract:
 *   - Structure: TreeRows (TreeControls.tsx) · Divider · the list header (piste-map toggle, then the
 *     Lift / Length / Drop labels) · the rows (RunList.tsx RunLine, C#'s order) or RUNS_EMPTY.
 *   - Piste map (D19): a cs2/ui Button (theme `pisteToggle`) with a check box that shows C#'s value
 *     (pisteMap$), never a guess (design 7.2); a click sends setPisteMap(!value) and C# answers by
 *     pushing the new value (and logs `Hub [piste] map on|off`).
 *   - Hover (D19, design 7.3): RunLine sends hoverRun on mouse enter (hubState.sendHoverRun dedups);
 *     leaving the list and unmounting this view (a view switch) send Null once. HubPanel covers the
 *     area switch and the panel's unmount.
 *   - Click a row: focused$ (highlight) + focusRun (C# flies the camera). Pen: one row renames at a
 *     time (local state, entityId); commit -> rename(run, text).
 *   - Joins (ch6 4.3): runLive by entityId (hubRules.runLiveMap, memoised on the array), lift names by
 *     LiftRow.entity (memoised on lifts$), so a live push re-renders only the rows that moved.
 *   - Panel file (gate R11): subscribes selectedResort$, runs$, runLive$, lifts$, pisteMap$, city$
 *     (+ glade$ in TreeRows); local focused$.
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
import { HUB_LOC } from "./hubText";
import { CheckBox, Divider, Tip, useNumberSamples } from "./parts";
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
  const liftNames = useMemo(() => {
    const m = new Map<string, string>();
    for (const l of lifts ?? []) if (l) m.set(entityId(l.entity), l.name);
    return m;
  }, [lifts]);

  // A view switch unmounts this view: clear the hover in C# once (design 7.3).
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
        <div ref={listRef} className={styles.list} onMouseLeave={() => sendHoverRun(NULL_ENTITY)}>
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
