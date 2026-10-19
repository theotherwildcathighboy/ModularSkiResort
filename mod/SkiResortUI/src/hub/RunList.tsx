/*
 * src/hub/RunList.tsx - one run row of the Runs view (RunLine) and the list header (RunHeader).
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-C); reworked
 * 2026-10-03 (Round U2, implementer I-3) for docs/research/hub-u2-spec.md section 4.2 "RunTable",
 * 4.4 J7, OD-15; rewritten 2026-10-04 for contract 3 (Round E1, stream S6; docs/research/econ-round/
 * spec/ch6-hub-ui.md section 6.8 "RunLine", design hub-v3-design.md 3.2, 5.2, 5.3, 6.6; deviation U7).
 * Contract:
 *   - Row `div.runRow` (FIXED 1.9 em of S; no scroll area of its own: the panel content scrolls as
 *     one, U1 fixer round tsx-ux-2): [fly part | pen]. The fly part is a cs2/ui Button (theme
 *     `runFly`) holding the state word (Round F7 D1, docs/research/field-test-7-fixes.md and
 *     findings/hub-ux.md §2.4: "Open" / "Patchy" / "Closed" of RunLive.openState in an XS StableWidth of
 *     the three translated words, so the name never moves; -1 = an empty slot of the same width; it
 *     replaced the 0.6 em open mark, which was 8 px at 1080p and unlabelled; a CLOSED row's fly part is
 *     dimmed to 55 % (`runRowClosed`; not while renaming — F7 fixer: the rename field is never dimmed),
 *     the pen is not; an estimated state no longer dims, because dim
 *     now means closed: the tooltip says "Estimated") · BandMarker (-1 being
 *     rated, -2 cannot be rated) · KindStripe (natural: two short bars) · the name (C#'s string, else
 *     the RUN_TYPE word) · the lift mark ("↑" while a running lift reaches the top) · Length · Drop
 *     (dimmed while estimated from the outline). The pen is a SIBLING Button at the right end, never
 *     nested (U7); while a row renames, its fly part is a plain `div.runFly` of the same layout with
 *     the RenameField in the name slot (an <input> is never inside a <button>). One row renames at a
 *     time; a view or area switch unmounts the field, which commits (RenameField contract).
 *   - Mouse enter sends hoverRun(row) through hubState.sendHoverRun (dedup: no per-move traffic;
 *     RunsView sends Null on leaving the list and on unmount).
 *   - Click -> onPick (RunsView: focused$ + focusRun; C# flies the camera, logs `Hub [focus]`).
 *   - Run tooltip (design 6.6), in order: name; "type · band"; the open line (Not linked: no snow
 *     state; gating off: counts as open; else open / partly / closed with the snow share); "estimated";
 *     served by <lift> / a running lift / no running lift reaches its top (counts the backcountry %);
 *     the natural type note (second pass, typeFactor < 1); score value now of full; its parts
 *     (difficulty × size × type × lift × open); the crux pitch and the steepest patch (today's rule);
 *     area; running cost (not for natural runs); RUN_ACTIONS.
 *   - Number columns (J7): every cell and the header label carry the width samples of
 *     useNumberSamples, so the columns line up and never jitter; the U1 em min-widths only if the
 *     samples cannot be rendered.
 *   - Memoised (ch6 6.15): a row re-renders only when one of its own RunRow / RunLive fields, its
 *     lift name, focus, renaming state, the gating / backcountry settings, `t`, the samples, the state
 *     words (RunsView memoises them on `t`) or a callback changed (the callbacks are stable
 *     useCallbacks of RunsView).
 *   - Width (Round F7, Overpass advances, design §2.3 method): the word slot is "Patchy" (the widest of
 *     the three) + 0.4 em of XS = 42.3 / 69.5 / 87.8 rem at 100 / 130 / 150 % text against the open
 *     mark's 14.0 / 22.8 / 28.7, so the name room goes 195.5 / 248.2 / 283.2 -> 167.2 / 201.5 / 224.1:
 *     every default name fits at 100 and 130 % ("Snowmaking run 12" 121.6 / 198.3, 3.2 rem spare at
 *     130 %); at 150 % "Snowmaking run 1" (231.8) and longer end in "…", the full name stays the first
 *     tooltip line.
 */
import { memo } from "react";
import type { MouseEvent } from "react";
import { Button } from "cs2/ui";
import { Unit } from "cs2/l10n";
import { OPEN_STATE, RUN_KIND } from "../bindings";
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

/** The header buttons' guard (HubHeader noDrag, the vanilla close button's own,
 *  index.js@686490): on the pen, preventDefault keeps the input focused, so a second click closes
 *  the field through the toggle (the unmount commits, RenameField header) instead of a blur that
 *  commits, re-renders and lets the click land elsewhere (U2 fixer round, finding u2-panel-1). */
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
  /** Round F7 D1: the three translated state words, index = OPEN_STATE (hubText RUN_STATE_TEXT); one
   *  array per `t` (RunsView useMemo), so the memo comparator checks one reference. */
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
  // T54 probe 1: a cat track rated blue or steeper says why it is shut (OPEN_STATE.Unsuitable, C# HubRules.CatTrackOpenState).
  if (live && live.openState === OPEN_STATE.Unsuitable) openLine = t(HUB_LOC.runUnsuitable);
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
        !!live && live.snowEstimated && t(HUB_LOC.runEstimated),
        servedLine,
        row.member && row.typeFactor < 1 && t(HUB_LOC.runTypeNote),
        row.kind === RUN_KIND.CatTrack && t(HUB_LOC.runCatTrackNote),   // T54 probe 1: listed, but not in the score or the capacity
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
        rated && row.gradePct >= 0 && { label: t(HUB_LOC.cruxPitch), value: <Num value={row.gradePct} unit={Unit.PercentageSingleFraction} /> },
        rated && row.steepestPct > row.gradePct && { label: t(HUB_LOC.steepestPatch), value: <Num value={row.steepestPct} unit={Unit.PercentageSingleFraction} /> },
        { label: t(HUB_LOC.colArea), value: <LocText entry={HUB_FMT.areaHa} args={{ HA: numArg(hectares(row.areaM2), Unit.FloatSingleFraction) }} /> },
        row.kind !== RUN_KIND.Natural && row.kind !== RUN_KIND.CatTrack && row.bound && {
          label: t(HUB_LOC.runCost),
          value: live && live.costMonth >= 0 ? <Num value={live.costMonth} unit={Unit.MoneyPerMonth} /> : "–",
        },
        t(HUB_LOC.runActions),
      ]}
    />
  );
}

/** Round F7 D1: the run's state word in a StableWidth of all three words (the name never moves); -1 (not
 *  linked, no live row) = the same width, empty. */
function RunState({ state, words }: { state: number; words: readonly string[] }): JSX.Element {
  const word = state >= 0 && state < words.length ? words[state] : "";
  return <StableWidth samples={words} align="start" className={styles.runState}>{word}</StableWidth>;
}

function RunLineImpl(p: RunLineProps): JSX.Element {
  const { row, live, t, samples, stateWords, focused, renaming, onPick, onPen, onRename, onRenameClose } = p;
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
        (state === OPEN_STATE.Closed || state === OPEN_STATE.Unsuitable) && !renaming && styles.runRowClosed)}
      onMouseEnter={() => sendHoverRun(row.entity)}
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
    a.snowEstimated === b.snowEstimated && a.valueNow === b.valueNow && a.costMonth === b.costMonth;
}

/** Re-render a row only when something it shows or uses changed (ch6 6.8 memo comparator). */
export const RunLine = memo(RunLineImpl, (a, b) =>
  a.t === b.t && a.samples === b.samples && a.stateWords === b.stateWords && a.focused === b.focused && a.renaming === b.renaming &&
  a.gating === b.gating && a.backcountryPct === b.backcountryPct && a.liftName === b.liftName &&
  a.onPick === b.onPick && a.onPen === b.onPen && a.onRename === b.onRename && a.onRenameClose === b.onRenameClose &&
  sameRunRow(a.row, b.row) && sameRunLive(a.live, b.live));

/** The list header: the piste-map toggle (left), then the Lift / Length / Drop column labels and the
 *  pen column's spacer, aligned with RunLine's cells (same padding, same NumCell blinds). */
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
      <div className={styles.penSpacer} />
    </div>
  );
}
