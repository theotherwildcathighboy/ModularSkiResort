/*
 * src/hub/LiftsView.tsx - the Lifts view (Lifts tile): the shown area's lifts and their totals.
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/
 * spec/ch6-hub-ui.md section 6.9, design hub-v3-design.md 3.3; replaces the Lifts tab and focus strip
 * of the contract-2 ResortLists.tsx, deleted).
 * Contract:
 *   - Rows: the census rows whose `sign` is the shown area (Entity.Null = Not linked), C#'s order,
 *     each a LiftLine2 (LiftTable.tsx); none -> LIFTS_EMPTY. Then a divider and two totals that are
 *     always shown (design 3.3): total lift length and the design capacity of all complete lifts.
 *   - React key: liftId (entity/top) with "#n" for a repeated id (U2 fixer round, finding u2-panel-2:
 *     two unfinished lines from ONE station are two census rows with the same (entity, top); interim
 *     until the census anchor is in LiftRow).
 *   - Click a row: focused$ + focusLift(entity, top) (C# flies to the gap while a section is missing).
 *     Pen: rename(row.entity, text) - C# renames the base station, else the top station, and refuses
 *     (with a log line) a row with no station; one row renames at a time.
 *   - Panel file (gate R11): subscribes resorts$, selectedResort$, lifts$; local focused$.
 */
import { useCallback, useMemo, useState } from "react";
import type { RefObject } from "react";
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { entityId, hubTriggers, lifts$, resorts$, selectedResort$ } from "../bindings";
import type { LiftRow } from "../bindings";
import { useT } from "../l10n";
import { loneStations, partnerPlaced } from "./hubRules";
import { focused$ } from "./hubState";
import { HUB_FMT, HUB_LOC, LIFT_STATUS_TEXT } from "./hubText";
import { LiftLine2, liftId } from "./LiftTable";
import { Divider, InfoLine, LocText, Num, numArg } from "./parts";
import { currentResort, liftsOf, resortEntries } from "./resortData";
import styles from "./hub.module.scss";

export function LiftsView({ listRef }: { listRef: RefObject<HTMLDivElement> }): JSX.Element {
  const t = useT();
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const liftRows = useValue(lifts$);
  const focused = useValue(focused$);
  const [renaming, setRenaming] = useState<string | null>(null);

  const words = useMemo(() => LIFT_STATUS_TEXT.map((e) => t(e)), [t]);
  const onPick = useCallback((row: LiftRow) => {
    focused$.update({ kind: "lift", entity: row.entity, top: row.top });
    hubTriggers.focusLift(row.entity, row.top);
  }, []);
  const onPen = useCallback((row: LiftRow) => {
    const id = liftId(row);
    setRenaming((r) => (r === id ? null : id));
  }, []);
  const onRename = useCallback((row: LiftRow, text: string) => {
    setRenaming(null);
    hubTriggers.rename(row.entity, text);
  }, []);
  const onRenameClose = useCallback(() => setRenaming(null), []);

  const row = currentResort(resortEntries(resorts), selected).row;
  const lifts = liftsOf(liftRows, selected);
  // E1 fixer (F9): lone stations pair across rows (the census never puts both on one row).
  const lone = loneStations(lifts);
  const focusedId = focused.kind === "lift" ? `${entityId(focused.entity)}/${entityId(focused.top)}` : "";
  const keyCount = new Map<string, number>();

  return (
    <>
      {lifts.length === 0 ? (
        <div className={styles.dimLine}>{t(HUB_LOC.liftsEmpty)}</div>
      ) : (
        <div ref={listRef} className={styles.list}>
          {lifts.map((l) => {
            const id = liftId(l);
            const seen = keyCount.get(id) ?? 0;
            keyCount.set(id, seen + 1);
            return (
              <LiftLine2
                key={seen === 0 ? id : `${id}#${seen}`}
                row={l}
                partner={partnerPlaced(l, lone)}
                t={t}
                words={words}
                focused={id === focusedId}
                renaming={seen === 0 && id === renaming}
                onPick={onPick}
                onPen={onPen}
                onRename={onRename}
                onRenameClose={onRenameClose}
              />
            );
          })}
        </div>
      )}
      <Divider />
      <InfoLine
        label={t(HUB_LOC.liftsTotalLength)}
        value={row ? <Num value={row.liftLengthM} unit={Unit.Length} /> : "–"}
      />
      <InfoLine
        label={t(HUB_LOC.liftsTotalCapacity)}
        value={row && row.capacityPerHour >= 0 ? <LocText entry={HUB_FMT.capacityValue} args={{ CAP: numArg(row.capacityPerHour, Unit.Integer) }} /> : "–"}
        tip={t(HUB_LOC.liftCapacityTip)}
      />
    </>
  );
}
