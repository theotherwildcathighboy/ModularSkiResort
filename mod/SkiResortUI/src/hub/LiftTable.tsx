/*
 * src/hub/LiftTable.tsx - one lift of the Lifts view: a two-line row (LiftLine2).
 * Line 1 = [fly part | pen] (status mark, name, status word in a StableWidth of the five LIFT_STATUS words so the name never moves); line 2 is ONE
 * inert nowrap text node from hubRules.liftLine2. LiftRow.powered is the raw LiftPower; `counted` follows the one predicate LiftServedRule.IsPowered;
 * both arrive from C#, the UI decides nothing. While renaming, the fly part is a plain div (an <input> is never inside a <button>).
 * The "billed at 25 %" reason is hubRules.liftCostReason, mapped by liftCostReducedEntry. Rise is shown signed (a negative rise is printed, not
 * unknown). Memoised on every LiftRow field it shows plus focus, renaming, `t`, the callbacks, type, seats, speedMps and typeWords.
 */
import { memo } from "react";
import { Button } from "cs2/ui";
import { Unit } from "cs2/l10n";
import { LocalizedFraction } from "../cs2shim";
import { LIFT_STATE, LIFT_STATUS, entityId } from "../bindings";
import type { LiftRow } from "../bindings";
import type { LocEntry, Translate } from "../l10n";
import { liftCostReason, liftLine2, liftMark } from "./hubRules";
import type { LiftCostReason } from "./hubRules";
import { HUB_FMT, HUB_LOC, LIFT_TYPE, liftStatusEntry } from "./hubText";
import { LocText, Num, StableWidth, Tip, TipLines, cx, numArg } from "./parts";
import { RenameField } from "./RenameField";
import { keepFocus } from "./RunList";
import styles from "./hub.module.scss";

/** Tolerance of the alignment aid: within it the stations count as straight */
export const ALIGN_TOLERANCE_M = 0.5;
export const ALIGN_TOLERANCE_DEG = 0.5;

/** Row identity of a lift: (entity, top), the key focusLift sends */
export function liftId(row: LiftRow): string {
  return `${entityId(row.entity)}/${entityId(row.top)}`;
}

/** Line 2 as ONE text node (one <LocText> per variant: gate R13) */
function Line2Text({ row, partner, t }: { row: LiftRow; partner: boolean; t: Translate }): JSX.Element {
  const v = liftLine2(row, partner);
  if (v === "detail" || v === "detailOne" || v === "detailNone") {
    const LENGTH = numArg(row.lengthM, Unit.Length);
    const RISE = numArg(row.riseM, Unit.Height, false, false);
    const MIN = row.rideTimeS >= 0 ? numArg(row.rideTimeS / 60, Unit.FloatSingleFraction) : "–";
    const CAP = row.capacityPerHour >= 0 ? numArg(row.capacityPerHour, Unit.Integer) : "–";
    if (v === "detailOne") return <LocText entry={HUB_FMT.liftDetailOne} args={{ LENGTH, RISE, MIN, CAP }} />;
    if (v === "detailNone") return <LocText entry={HUB_FMT.liftDetailNone} args={{ LENGTH, RISE, MIN, CAP }} />;
    return <LocText entry={HUB_FMT.liftDetail} args={{ LENGTH, RISE, MIN, CAP, N: numArg(row.runsServed, Unit.Integer) }} />;
  }
  switch (v) {
    case "sectionMissing": return <>{t(HUB_LOC.liftLine2SectionMissing)}</>;
    case "stationsPair": return <>{t(HUB_LOC.liftLine2StationsPair)}</>;
    case "stationsBaseOnly": return <>{t(HUB_LOC.liftLine2StationsBaseOnly)}</>;
    case "stationsTopOnly": return <>{t(HUB_LOC.liftLine2StationsTopOnly)}</>;
    default: return <>{t(HUB_LOC.liftLine2Unfinished)}</>;
  }
}

/** The tooltip line of a reduced bill (hubRules.liftCostReason picks the reason); null = billed in full */
function liftCostReducedEntry(reason: LiftCostReason): LocEntry | null {
  switch (reason) {
    case "noPower": return HUB_LOC.liftCostReducedNoPower;
    case "sectionMissing": return HUB_LOC.liftCostReducedSectionMissing;
    case "unfinished": return HUB_LOC.liftCostReducedUnfinished;
    case "noOpenRun": return HUB_LOC.liftCostReducedNoOpenRun;
    default: return null;
  }
}

/** The type word of a row (LIFT_TYPE[row.type]); null for an unknown or missing type */
export function liftTypeWord(row: LiftRow, t: Translate): string | null {
  const e = row && typeof row.type === "string" ? LIFT_TYPE[row.type] : undefined;
  return e ? t(e) : null;
}

function liftTip(row: LiftRow, partner: boolean, t: Translate): JSX.Element {
  const straight = row.alignKnown &&
    Math.abs(row.alignOffsetM) <= ALIGN_TOLERANCE_M && Math.abs(row.alignYawDeg) <= ALIGN_TOLERANCE_DEG;
  const complete = row.state === LIFT_STATE.Running || row.state === LIFT_STATE.Static;
  const reduced = liftCostReducedEntry(liftCostReason(row));
  // Per-type numbers through placeholders; 0 (unknown) reads "–"
  const SPEED = row.speedMps > 0 ? numArg(row.speedMps, Unit.FloatSingleFraction) : "–";
  const SEATS = row.seats > 0 ? numArg(row.seats, Unit.Integer) : "–";
  return (
    <TipLines
      lines={[
        row.name,
        liftTypeWord(row, t),
        t(liftStatusEntry(row.status)),
        <Line2Text key="l2" row={row} partner={partner} t={t} />,
        {
          label: t(HUB_LOC.liftRide),
          value: row.rideTimeS >= 0
            ? <LocText entry={HUB_FMT.liftRideMin} args={{ MIN: numArg(row.rideTimeS / 60, Unit.FloatSingleFraction) }} /> : "–",
        },
        <LocText key="rt" entry={HUB_FMT.liftRideTip} args={{ SPEED }} />,
        // "Capacity  2,400 skiers/h", the unit the row's line 2 uses
        {
          label: t(HUB_LOC.liftCapacity),
          value: row.capacityPerHour >= 0
            ? <LocText entry={HUB_FMT.capacityValue} args={{ CAP: numArg(row.capacityPerHour, Unit.Integer) }} /> : "–",
        },
        <LocText key="ct" entry={HUB_FMT.liftCapacityTip} args={{ SEATS, SPEED }} />,
        { label: t(HUB_LOC.liftRuns), value: <Num value={row.runsServed} unit={Unit.Integer} /> },
        { label: t(HUB_LOC.liftCost), value: row.costMonth >= 0 ? <Num value={row.costMonth} unit={Unit.MoneyPerMonth} /> : "–" },
        reduced && t(reduced),
        { label: t(HUB_LOC.liftSections), value: <Num value={row.sections} unit={Unit.Integer} /> },
        { label: t(HUB_LOC.liftTowers), value: <Num value={row.towers} unit={Unit.Integer} /> },
        {
          label: t(HUB_LOC.liftChairs),
          value: complete
            ? <LocalizedFraction value={Math.max(0, row.chairsRiding | 0)} total={Math.max(0, row.chairs | 0)} unit={Unit.Integer} />
            : <Num value={row.chairs} unit={Unit.Integer} />,
        },
        straight && t(HUB_LOC.liftStraight),
        row.alignKnown && !straight && { label: t(HUB_LOC.alignOffset), value: <Num value={row.alignOffsetM} unit={Unit.Length} signed /> },
        row.alignKnown && !straight && { label: t(HUB_LOC.alignAngle), value: <Num value={row.alignYawDeg} unit={Unit.Angle} signed /> },
        row.towersRemoved > 0 && t(HUB_LOC.liftTowerRemoved),
        t(HUB_LOC.liftSignTip),
      ]}
    />
  );
}

export interface LiftLineProps {
  row: LiftRow;
  /** A lone station of the other kind is placed in this area (hubRules.partnerPlaced) */
  partner: boolean;
  t: Translate;
  /** The five status words, translated (the word cell's StableWidth blinds) */
  words: readonly string[];
  /** The lift-type words, translated (the type cell's StableWidth blinds), while the shown area's rows span two or more
   *  types; null = no type cell (a one-type area keeps the plain row) */
  typeWords: readonly string[] | null;
  focused: boolean;
  renaming: boolean;
  onPick: (row: LiftRow) => void;
  onPen: (row: LiftRow) => void;
  onRename: (row: LiftRow, text: string) => void;
  onRenameClose: () => void;
}

function LiftLine2Impl({ row, partner, t, words, typeWords, focused, renaming, onPick, onPen, onRename, onRenameClose }: LiftLineProps): JSX.Element {
  const mark = liftMark(row.status);
  const markCls = mark.ring
    ? cx(styles.liftDot, styles.liftDotRing, mark.tone === "warn" ? styles.liftRingWarn : styles.liftRingDim)
    : cx(styles.liftDot, mark.tone === "warn" ? styles.liftDotWarn : mark.tone === "dim" ? styles.liftDotDim : styles.liftDotNormal);
  const wordTone = row.status === LIFT_STATUS.NoPower || row.status === LIFT_STATUS.SectionMissing ? styles.liftWordWarn
    : row.status === LIFT_STATUS.ChairsStopped || row.status === LIFT_STATUS.Unfinished || row.status === LIFT_STATUS.Closed ? styles.liftWordDim : null;
  const inner = (
    <>
      <div className={markCls} />
      <div className={styles.rowName}>
        {renaming ? (
          <RenameField key="rename" initial={row.name} onCommit={(text) => onRename(row, text)} onClose={onRenameClose} />
        ) : row.name}
      </div>
      {typeWords && (
        <StableWidth samples={typeWords} align="end" className={styles.liftTypeWord}>
          {liftTypeWord(row, t) ?? ""}
        </StableWidth>
      )}
      <StableWidth samples={words} align="end" className={cx(styles.liftWord, wordTone)}>
        {t(liftStatusEntry(row.status))}
      </StableWidth>
    </>
  );
  return (
    <div className={cx(styles.liftRow2, focused && styles.runRowFocused)}>
      <div className={styles.liftLine1}>
        {renaming ? (
          <div className={styles.liftFly}>{inner}</div>
        ) : (
          <Tip tip={liftTip(row, partner, t)}>
            <Button theme={{ button: styles.liftFly }} onSelect={() => onPick(row)}>
              {inner}
            </Button>
          </Tip>
        )}
        <Tip tip={<TipLines lines={[t(HUB_LOC.rename), t(HUB_LOC.liftNameTip), t(HUB_LOC.renameTip)]} />}>
          <Button
            variant="icon"
            src="Media/Glyphs/Pen.svg"
            tinted
            className={styles.rowPen}
            onMouseDown={keepFocus}
            onSelect={() => onPen(row)}
          />
        </Tip>
      </div>
      <div className={styles.liftLine2}><Line2Text row={row} partner={partner} t={t} /></div>
    </div>
  );
}

/** Re-render a row only when something it shows changed */
export const LiftLine2 = memo(LiftLine2Impl, (a, b) =>
  a.t === b.t && a.partner === b.partner && a.words === b.words && a.typeWords === b.typeWords &&
  a.focused === b.focused && a.renaming === b.renaming &&
  a.row.type === b.row.type && a.row.seats === b.row.seats && a.row.speedMps === b.row.speedMps &&
  a.onPick === b.onPick && a.onPen === b.onPen && a.onRename === b.onRename && a.onRenameClose === b.onRenameClose &&
  a.row.name === b.row.name && a.row.state === b.row.state && a.row.sections === b.row.sections &&
  a.row.towers === b.row.towers && a.row.chairs === b.row.chairs && a.row.chairsRiding === b.row.chairsRiding &&
  a.row.gaps === b.row.gaps && a.row.towersRemoved === b.row.towersRemoved && a.row.lengthM === b.row.lengthM &&
  a.row.riseM === b.row.riseM && a.row.alignKnown === b.row.alignKnown && a.row.alignOffsetM === b.row.alignOffsetM &&
  a.row.alignYawDeg === b.row.alignYawDeg && a.row.capacityPerHour === b.row.capacityPerHour &&
  a.row.hasBase === b.row.hasBase && a.row.hasTop === b.row.hasTop && a.row.powered === b.row.powered &&
  a.row.counted === b.row.counted && a.row.status === b.row.status && a.row.rideTimeS === b.row.rideTimeS &&
  a.row.runsServed === b.row.runsServed && a.row.costMonth === b.row.costMonth && a.row.billedReduced === b.row.billedReduced &&
  a.row.entity?.index === b.row.entity?.index && a.row.entity?.version === b.row.entity?.version &&
  a.row.top?.index === b.row.top?.index && a.row.top?.version === b.row.top?.version);
