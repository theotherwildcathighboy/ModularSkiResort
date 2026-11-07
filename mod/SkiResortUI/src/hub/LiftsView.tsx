/*
 * src/hub/LiftsView.tsx - the Lifts view: the shown area's lifts and their totals.
 * Rows: census rows whose `sign` is the shown area (Entity.Null = Not linked), in C#'s order. Each row is a LiftLine2 (LiftTable.tsx).
 * Two always-shown totals follow. React key = liftId (entity/top) plus "#n" for a repeat: two unfinished lines from one station are
 * two census rows with the same (entity, top).
 * Click -> focused$ + focusLift (C# flies to the gap while a section is missing).
 * Pen -> rename(row.entity, text). C# renames the base station, else the top.
 * One list, no pager: this view and the Runs view may scroll. Panel file (gate R11): subscribes resorts$, selectedResort$, lifts$.
 */
import { useCallback, useMemo, useState } from "react";
import type { RefObject } from "react";
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { entityId, hubTriggers, lifts$, resorts$, selectedResort$ } from "../bindings";
import type { LiftRow } from "../bindings";
import { useT } from "../l10n";
import { liftTypesOf, loneStations, partnerPlaced } from "./hubRules";
import { focused$ } from "./hubState";
import { HUB_FMT, HUB_LOC, LIFT_STATUS_TEXT, LIFT_TYPE } from "./hubText";
import { LiftLine2, liftId } from "./LiftTable";
import { Divider, InfoLine, LocText, Num, TipLines, cx, numArg } from "./parts";
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
  // StableWidth samples for the type words: every known type, so a row's cell never changes width
  const allTypeWords = useMemo(() => Object.keys(LIFT_TYPE).map((k) => t(LIFT_TYPE[k])), [t]);
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
  // Lone stations pair across rows (the census never puts both on one row)
  const lone = loneStations(lifts);
  const typeWords = liftTypesOf(lifts).length >= 2 ? allTypeWords : null;
  const focusedId = focused.kind === "lift" ? `${entityId(focused.entity)}/${entityId(focused.top)}` : "";
  const keyCount = new Map<string, number>();

  return (
    <>
      {lifts.length === 0 ? (
        <div className={styles.dimLine}>{t(HUB_LOC.liftsEmpty)}</div>
      ) : (
        <div ref={listRef} className={cx(styles.list, styles.listAll)}>
          {lifts.map((l) => {
            const id = liftId(l);
            const seen = keyCount.get(id) ?? 0;
            keyCount.set(id, seen + 1);
            return { l, id, seen };
          }).map(({ l, id, seen }) => {
            return (
              <LiftLine2
                key={seen === 0 ? id : `${id}#${seen}`}
                row={l}
                partner={partnerPlaced(l, lone)}
                t={t}
                words={words}
                typeWords={typeWords}
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
      {/* One total on the surface (DECISIONS 613). The total length is a row of its tooltip (Finances' Lifts row names it too). */}
      <InfoLine
        label={t(HUB_LOC.liftsTotalCapacity)}
        value={row && row.capacityPerHour >= 0 ? <LocText entry={HUB_FMT.capacityValue} args={{ CAP: numArg(row.capacityPerHour, Unit.Integer) }} /> : "–"}
        tip={<TipLines lines={[
          t(HUB_LOC.liftsTotalCapacityTip),
          { label: t(HUB_LOC.liftsTotalLength), value: row ? <Num value={row.liftLengthM} unit={Unit.Length} /> : "–" },
        ]} />}
      />
    </>
  );
}
