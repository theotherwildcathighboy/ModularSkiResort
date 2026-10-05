/*
 * src/hub/LiftTable.tsx - one lift of the Lifts view: a two-line row (LiftLine2).
 *
 * Written for this repo (MIT). Created 2026-10-03 (Round U2, implementer I-3); rewritten 2026-10-04
 * for contract 3 (Round E1, stream S6; docs/research/econ-round/spec/ch6-hub-ui.md section 6.9
 * "LiftLine2", design hub-v3-design.md 3.3, 5.2, 5.3, 6.7).
 * Authority before contract 3: docs/research/hub-u2-spec.md section 4.2 "LiftTable", 3.7, 5.2, OD-1.
 * Contract:
 *   - `div.liftRow2`: line 1 (1.4 em of S) = [fly part | pen]; line 2 (1.35 em of XS, ONE text node,
 *     inert, nowrap + ellipsis). The fly part is a cs2/ui Button (theme `liftFly`): the status mark
 *     (hubRules.liftMark: Running filled, No power filled warning, Chairs stopped filled dimmer,
 *     Section missing ring warning, Unfinished ring dimmer; design 5.3) · the name · the status word
 *     in a StableWidth of the five LIFT_STATUS words (so the name never moves; NoPower and
 *     SectionMissing in the warning colour, ChairsStopped and Unfinished dim). While renaming, the fly
 *     part is a plain `div.liftFly` with the RenameField in the name slot (U7). The pen is a sibling.
 *   - Line 2 by hubRules.liftLine2: "length · rise · ride min · capacity/h design · N runs" (one / no
 *     runs variants; E1 fixer F9: with the `partner` prop, a lone station whose other kind is placed
 *     elsewhere in the area reads "both stations placed" - the census never pairs them in one row) for a complete lift; else the section-missing / unfinished / stations-only
 *     sentence. LiftRow.powered is the raw LiftPower (status NoPower); `counted` follows the one
 *     predicate LiftServedRule.IsPowered (e1-spec R1) - both arrive from C#, the UI decides nothing.
 *   - Tooltip (design 6.7): name; status word; the line-2 sentence; ride time + its basis; capacity +
 *     its basis; runs it serves; running cost (+ "billed at 25 %" while reduced: no power, a section
 *     missing, or unfinished); sections, towers, chairs (riding / total when complete),
 *     straightness or offset / angle (OD-9 tolerance 0.5 m / 0.5°), tower removed (today's rules);
 *     LIFT_SIGN_TIP; LIFT_ACTIONS (C# flies to the gap while a section is missing).
 *   - Rise is shown signed by the game's number format (a negative rise is printed, not hidden as
 *     unknown: minusIsUnknown false; S6 detail).
 *   - Memoised on every LiftRow field it shows (today's list + hasBase, hasTop, powered, counted,
 *     status, rideTimeS, runsServed, costMonth, billedReduced), focus, renaming, `t` and the callbacks.
 */
import { memo } from "react";
import { Button } from "cs2/ui";
import { Unit } from "cs2/l10n";
import { LocalizedFraction } from "../cs2shim";
import { LIFT_STATE, LIFT_STATUS, entityId } from "../bindings";
import type { LiftRow } from "../bindings";
import type { Translate } from "../l10n";
import { liftLine2, liftMark } from "./hubRules";
import { HUB_FMT, HUB_LOC, liftStatusEntry } from "./hubText";
import { LocText, Num, StableWidth, Tip, TipLines, cx, numArg } from "./parts";
import { RenameField } from "./RenameField";
import { keepFocus } from "./RunList";
import styles from "./hub.module.scss";

/** OD-9 tolerance of the alignment aid (spec 5.5): within it the stations count as straight. */
export const ALIGN_TOLERANCE_M = 0.5;
export const ALIGN_TOLERANCE_DEG = 0.5;

/** Row identity of a lift: (entity, top), the key focusLift sends. */
export function liftId(row: LiftRow): string {
  return `${entityId(row.entity)}/${entityId(row.top)}`;
}

/** Line 2 as ONE text node (one <LocText> per variant: gate R13). */
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

function liftTip(row: LiftRow, partner: boolean, t: Translate): JSX.Element {
  const straight = row.alignKnown &&
    Math.abs(row.alignOffsetM) <= ALIGN_TOLERANCE_M && Math.abs(row.alignYawDeg) <= ALIGN_TOLERANCE_DEG;
  const complete = row.state === LIFT_STATE.Running || row.state === LIFT_STATE.Static;
  const reduced = !row.billedReduced ? null
    : row.status === LIFT_STATUS.NoPower ? HUB_LOC.liftCostReducedNoPower
      : row.status === LIFT_STATUS.SectionMissing ? HUB_LOC.liftCostReducedSectionMissing
        : HUB_LOC.liftCostReducedUnfinished;
  return (
    <TipLines
      lines={[
        row.name,
        t(liftStatusEntry(row.status)),
        <Line2Text key="l2" row={row} partner={partner} t={t} />,
        {
          label: t(HUB_LOC.liftRide),
          value: row.rideTimeS >= 0
            ? <LocText entry={HUB_FMT.liftRideMin} args={{ MIN: numArg(row.rideTimeS / 60, Unit.FloatSingleFraction) }} /> : "–",
        },
        t(HUB_LOC.liftRideTip),
        { label: t(HUB_LOC.liftCapacity), value: row.capacityPerHour >= 0 ? <Num value={row.capacityPerHour} unit={Unit.Integer} /> : "–" },
        t(HUB_LOC.liftCapacityTip),
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
        t(HUB_LOC.liftActions),
      ]}
    />
  );
}

export interface LiftLineProps {
  row: LiftRow;
  /** E1 fixer (F9): a lone station of the other kind is placed in this area (hubRules.partnerPlaced). */
  partner: boolean;
  t: Translate;
  /** The five status words, translated (the word cell's StableWidth blinds). */
  words: readonly string[];
  focused: boolean;
  renaming: boolean;
  onPick: (row: LiftRow) => void;
  onPen: (row: LiftRow) => void;
  onRename: (row: LiftRow, text: string) => void;
  onRenameClose: () => void;
}

function LiftLine2Impl({ row, partner, t, words, focused, renaming, onPick, onPen, onRename, onRenameClose }: LiftLineProps): JSX.Element {
  const mark = liftMark(row.status);
  const markCls = mark.ring
    ? cx(styles.liftDot, styles.liftDotRing, mark.tone === "warn" ? styles.liftRingWarn : styles.liftRingDim)
    : cx(styles.liftDot, mark.tone === "warn" ? styles.liftDotWarn : mark.tone === "dim" ? styles.liftDotDim : styles.liftDotNormal);
  const wordTone = row.status === LIFT_STATUS.NoPower || row.status === LIFT_STATUS.SectionMissing ? styles.liftWordWarn
    : row.status === LIFT_STATUS.ChairsStopped || row.status === LIFT_STATUS.Unfinished ? styles.liftWordDim : null;
  const inner = (
    <>
      <div className={markCls} />
      <div className={styles.rowName}>
        {renaming ? (
          <RenameField key="rename" initial={row.name} onCommit={(text) => onRename(row, text)} onClose={onRenameClose} />
        ) : row.name}
      </div>
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

/** Re-render a row only when something it shows changed. */
export const LiftLine2 = memo(LiftLine2Impl, (a, b) =>
  a.t === b.t && a.partner === b.partner && a.words === b.words && a.focused === b.focused && a.renaming === b.renaming &&
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
