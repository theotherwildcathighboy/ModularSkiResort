/*
 * src/hub/RunList.tsx - one run row of the Runs view (RunLine) and the list header (RunHeader).
 * Row `div.runRow` (fixed 1.9 em): [fly part | trash | pen]. The trash (cat tracks only; other rows keep an empty slot so the columns
 * line up) arms on the first click and deletes on the second (hubTriggers.deleteCatTrack); leaving the row or 4 s disarm it. The pen is a
 * sibling Button, never nested; while a row renames, the fly part is a plain div
 * with RenameField (an <input> is never inside a <button>). The state word sits in an XS StableWidth of the three translated words so the name never moves.
 * Mouse enter sends hoverRun through hubState.sendHoverRun (dedup); click -> onPick. Number columns carry the useNumberSamples widths (no jitter).
 * Memoised on the row's own RunRow / RunLive fields, lift name, focus, renaming, settings, `t`, samples, state words and the stable callbacks.
 * Width (Overpass advances): name room 167 / 201 / 224 rem at 100 / 130 / 150 % text; the ellipsis is the net, the full name is tooltip line 1.
 */
import { memo, useEffect, useState } from "react";
import type { MouseEvent } from "react";
import { Button } from "cs2/ui";
import { Unit } from "cs2/l10n";
import { OPEN_STATE, RUN_GUN_WHY, RUN_KIND, hubTriggers } from "../bindings";
import type { RunLive, RunRow } from "../bindings";
import type { Translate } from "../l10n";
import { isRunKind } from "./buildItems";
import { hectares } from "./hubRules";
import { sendHoverRun } from "./hubState";
import { HUB_FMT, HUB_LOC, RUN_TYPE, bandEntry } from "./hubText";
import { BandMarker, KindStripe, LocText, Num, NumCell, StableWidth, Tip, TipLines, cx, numArg } from "./parts";
import type { NumberSamples } from "./parts";
import { RenameField } from "./RenameField";
import styles from "./hub.module.scss";

/** The header buttons' guard (HubHeader noDrag, the vanilla close button's own, index.js@686490): on the pen,
*  preventDefault keeps the input focused, so a second click closes the field through the toggle (the unmount commits,
*  RenameField header) instead of a blur that commits, re-renders and lets the click land elsewhere. */
export function keepFocus(e: MouseEvent): void {
  e.preventDefault();
  e.stopPropagation();
}

export interface RunLineProps {
  row: RunRow;
  live: RunLive | undefined;
  /** Name of the lift that serves the run (LiftRow.name by RunRow.servedBy), or undefined. */
  liftName: string | undefined;
  t: Translate;
  samples: NumberSamples;
  /** The three translated state words, index = OPEN_STATE (hubText RUN_STATE_TEXT); one array per `t` (RunsView useMemo), so
  *  the memo comparator checks one reference. */
  stateWords: readonly string[];
  focused: boolean;
  renaming: boolean;
  gating: boolean;
  backcountryPct: number;
  onPick: (row: RunRow) => void;
  /** Toggle the rename field of this row (id = entityId(row.entity)). */
  onPen: (row: RunRow) => void;
  onRename: (row: RunRow, text: string) => void;
  onRenameClose: () => void;
}

function runTip(p: RunLineProps, name: string, typeName: string): JSX.Element {
  const { row, live, liftName, t, gating } = p;
  const rated = row.band >= 0;
  let openLine: string | JSX.Element | null = null;
  if (!row.bound) openLine = t(HUB_LOC.runOpenNone);
  else if (!gating) openLine = t(HUB_LOC.runOpenGating);
  else if (live && live.snowPct >= 0) {
    const PCT = numArg(live.snowPct, Unit.Percentage);
    openLine = live.openState === 2 ? <LocText key="o" entry={HUB_FMT.runOpenOpen} args={{ PCT }} />
      : live.openState === 1 ? <LocText key="o" entry={HUB_FMT.runOpenPartly} args={{ PCT }} />
        : live.openState === 0 ? <LocText key="o" entry={HUB_FMT.runOpenClosed} args={{ PCT }} />
          : null;
  }
  // Why it is open or closed (the measured depth against the 91 cm rule) and, on a gun run, what its snow guns do now
  const depthLine = row.bound && gating && live && live.snowDepthCm >= 0
    ? <LocText key="d" entry={HUB_FMT.runDepth} args={{ DEPTH: numArg(live.snowDepthCm / 100, Unit.Length) }} /> : null;
  const gunWhy = live ? live.gunWhy : RUN_GUN_WHY.None;
  const gunLine = gunWhy === RUN_GUN_WHY.Making ? t(HUB_LOC.runGunsMaking)
    : gunWhy === RUN_GUN_WHY.DepthReached ? t(HUB_LOC.runGunsDepth)
      : gunWhy === RUN_GUN_WHY.TooWarm ? t(HUB_LOC.runGunsWarm)
        : gunWhy === RUN_GUN_WHY.OutOfSeason ? t(HUB_LOC.runGunsSeason)
          : gunWhy === RUN_GUN_WHY.Waiting ? t(HUB_LOC.runGunsWaiting) : null;
  let servedLine: string | JSX.Element | null = null;
  if (row.served) {
    servedLine = liftName ? <LocText key="s" entry={HUB_FMT.runServed} args={{ NAME: liftName }} /> : t(HUB_LOC.runServedAnon);
  } else if (row.member) {
    servedLine = <LocText key="s" entry={HUB_FMT.runNotServed} args={{ PCT: numArg(Math.round(row.servedFactor * 100), Unit.Percentage) }} />;
  }
  return (
    <TipLines
      lines={[
        name,
        `${typeName} · ${t(bandEntry(row.band))}`,
        openLine,
        depthLine,
        gunLine,
        !!live && live.snowEstimated && t(HUB_LOC.runEstimated),
        servedLine,
        row.member && row.typeFactor < 1 && row.kind !== RUN_KIND.CatTrack && t(HUB_LOC.runTypeNote),
        row.kind === RUN_KIND.CatTrack && t(HUB_LOC.runCatTrackNote),   // counts a quarter in the score and the capacity; always green
        row.member && {
          label: t(HUB_LOC.runValue),
          value: <LocText entry={HUB_FMT.runValueOf} args={{
            NOW: numArg(live ? live.valueNow : -1, Unit.FloatSingleFraction),
            FULL: numArg(row.valueFull, Unit.FloatSingleFraction),
          }} />,
        },
        row.member && <LocText key="v" entry={HUB_FMT.runValueParts} args={{
          B: numArg(row.bandValue, Unit.FloatTwoFractions),
          S: numArg(row.sizeFactor, Unit.FloatTwoFractions),
          T: numArg(row.typeFactor, Unit.FloatTwoFractions),
          L: numArg(row.servedFactor, Unit.FloatTwoFractions),
          O: numArg(live ? live.openFactor : -1, Unit.FloatTwoFractions),
        }} />,
        // A cat track's green is the tool's (graded under 25 %), not the rater's: the rater's crux / steepest % read hairpins and crossings
        // higher (GradeBatch GR-20), so the rows stay off.
        rated && row.kind !== RUN_KIND.CatTrack && row.gradePct >= 0 && { label: t(HUB_LOC.cruxPitch), value: <Num value={row.gradePct} unit={Unit.PercentageSingleFraction} /> },
        rated && row.kind !== RUN_KIND.CatTrack && row.steepestPct > row.gradePct && { label: t(HUB_LOC.steepestPatch), value: <Num value={row.steepestPct} unit={Unit.PercentageSingleFraction} /> },
        { label: t(HUB_LOC.colArea), value: <LocText entry={HUB_FMT.areaHa} args={{ HA: numArg(hectares(row.areaM2), Unit.FloatSingleFraction) }} /> },
        row.kind !== RUN_KIND.Natural && row.bound && {
          label: t(HUB_LOC.runCost),
          value: live && live.costMonth >= 0 ? <Num value={live.costMonth} unit={Unit.MoneyPerMonth} /> : "–",
        },
      ]}
    />
  );
}

/** The run's state word in a StableWidth of all three words (the name never moves); -1 (not linked, no live row) = the same
*  width, empty. */
function RunState({ state, words }: { state: number; words: readonly string[] }): JSX.Element {
  const word = state >= 0 && state < words.length ? words[state] : "";
  return <StableWidth samples={words} align="start" className={styles.runState}>{word}</StableWidth>;
}

/** The cat track row's trash: the first click arms (tinted, the tooltip asks again), the second sends deleteCatTrack; 4 s or leaving
 *  the row disarms. A plain two-click confirm: the hub has no modal confirm. */
function TrashButton({ row, t, armed, setArmed }: { row: RunRow; t: Translate; armed: boolean; setArmed: (on: boolean) => void }): JSX.Element {
  useEffect(() => {
    if (!armed) return undefined;
    const id = setTimeout(() => setArmed(false), 4000);
    return () => clearTimeout(id);
  }, [armed, setArmed]);
  return (
    <Tip tip={<TipLines lines={[t(HUB_LOC.runDelete), armed ? t(HUB_LOC.runDeleteConfirm) : t(HUB_LOC.runDeleteTip)]} />}>
      <Button
        variant="icon"
        src="Media/Glyphs/Trash.svg"
        tinted
        className={cx(styles.rowPen, armed && styles.rowTrashArmed)}
        onMouseDown={keepFocus}
        onSelect={() => {
          if (!armed) {
            setArmed(true);
            return;
          }
          setArmed(false);
          hubTriggers.deleteCatTrack(row.entity);
        }}
      />
    </Tip>
  );
}

function RunLineImpl(p: RunLineProps): JSX.Element {
  const { row, live, t, samples, stateWords, focused, renaming, onPick, onPen, onRename, onRenameClose } = p;
  const [armed, setArmed] = useState(false);
  const typeName = isRunKind(row.kind) ? t(RUN_TYPE[row.kind]) : "";
  const name = row.name || typeName;
  const state = live ? live.openState : OPEN_STATE.None;
  const inner = (
    <>
      <RunState state={state} words={stateWords} />
      <BandMarker band={row.band} />
      <KindStripe kind={row.kind} />
      <div className={styles.rowName}>
        {renaming ? (
          <RenameField key="rename" initial={row.name} onCommit={(text) => onRename(row, text)} onClose={onRenameClose} />
        ) : name}
      </div>
      <div className={styles.liftMarkSlot}>{row.served ? "↑" : ""}</div>
      <NumCell samples={samples} kind="length" fallbackClass={styles.numLength} className={cx(row.approx && styles.numApprox)}>
        <Num value={row.lengthM} unit={Unit.Length} />
      </NumCell>
      <NumCell samples={samples} kind="height" fallbackClass={styles.numDrop} className={cx(row.approx && styles.numApprox)}>
        <Num value={row.dropM} unit={Unit.Height} />
      </NumCell>
    </>
  );
  return (
    <div
      className={cx(styles.runRow, focused && styles.runRowFocused, !row.bound && styles.listRowUnbound,
        state === OPEN_STATE.Closed && !renaming && styles.runRowClosed)}
      onMouseEnter={() => sendHoverRun(row.entity)}
      onMouseLeave={() => armed && setArmed(false)}
    >
      {renaming ? (
        <div className={styles.runFly}>{inner}</div>
      ) : (
        <Tip tip={runTip(p, name, typeName)}>
          <Button theme={{ button: styles.runFly }} onSelect={() => onPick(row)}>
            {inner}
          </Button>
        </Tip>
      )}
      {row.kind === RUN_KIND.CatTrack ? <TrashButton row={row} t={t} armed={armed} setArmed={setArmed} /> : <div className={styles.penSpacer} />}
      <Tip tip={<TipLines lines={[t(HUB_LOC.rename), t(HUB_LOC.renameTip)]} />}>
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
  );
}

function sameRunRow(a: RunRow, b: RunRow): boolean {
  return a.name === b.name && a.kind === b.kind && a.bound === b.bound && a.lengthM === b.lengthM &&
    a.dropM === b.dropM && a.areaM2 === b.areaM2 && a.band === b.band && a.gradePct === b.gradePct &&
    a.steepestPct === b.steepestPct && a.approx === b.approx && a.member === b.member && a.served === b.served &&
    a.servedBy?.index === b.servedBy?.index && a.servedBy?.version === b.servedBy?.version &&
    a.valueFull === b.valueFull && a.bandValue === b.bandValue && a.sizeFactor === b.sizeFactor &&
    a.typeFactor === b.typeFactor && a.servedFactor === b.servedFactor &&
    a.entity?.index === b.entity?.index && a.entity?.version === b.entity?.version;
}

function sameRunLive(a: RunLive | undefined, b: RunLive | undefined): boolean {
  if (a === b) return true;
  if (!a || !b) return false;
  return a.openState === b.openState && a.openFactor === b.openFactor && a.snowPct === b.snowPct &&
    a.snowEstimated === b.snowEstimated && a.valueNow === b.valueNow && a.costMonth === b.costMonth &&
    a.snowDepthCm === b.snowDepthCm && a.gunWhy === b.gunWhy;
}

/** Re-render a row only when something it shows or uses changed. */
export const RunLine = memo(RunLineImpl, (a, b) =>
  a.t === b.t && a.samples === b.samples && a.stateWords === b.stateWords && a.focused === b.focused && a.renaming === b.renaming &&
  a.gating === b.gating && a.backcountryPct === b.backcountryPct && a.liftName === b.liftName &&
  a.onPick === b.onPick && a.onPen === b.onPen && a.onRename === b.onRename && a.onRenameClose === b.onRenameClose &&
  sameRunRow(a.row, b.row) && sameRunLive(a.live, b.live));

/** The list header: the piste-map toggle (left), then the Lift / Length / Drop column labels and the pen column's spacer,
*  aligned with RunLine's cells (same padding, same NumCell blinds). */
export function RunHeader({ t, samples, pisteMap }: {
  t: Translate; samples: NumberSamples;
  /** The piste-map toggle (RunsView owns it: it reads pisteMap$). */
  pisteMap: JSX.Element;
}): JSX.Element {
  return (
    <div className={styles.listHeader}>
      {pisteMap}
      <Tip tip={t(HUB_LOC.colLiftTip)}>
        <div className={styles.liftMarkSlot}>{t(HUB_LOC.colLift)}</div>
      </Tip>
      <Tip tip={t(HUB_LOC.colLengthTip)}>
        <div className={styles.headerCell}>
          <NumCell samples={samples} kind="length" fallbackClass={styles.numLength}>
            <span className={styles.colLabel}>{t(HUB_LOC.colLength)}</span>
          </NumCell>
        </div>
      </Tip>
      <Tip tip={t(HUB_LOC.colDropTip)}>
        <div className={styles.headerCell}>
          <NumCell samples={samples} kind="height" fallbackClass={styles.numDrop}>
            <span className={styles.colLabel}>{t(HUB_LOC.colDrop)}</span>
          </NumCell>
        </div>
      </Tip>
      <div className={styles.penSpacer} />   {/* the trash column (cat tracks) */}
      <div className={styles.penSpacer} />
    </div>
  );
}
