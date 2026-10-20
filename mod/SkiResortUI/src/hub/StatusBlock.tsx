/*
 * src/hub/StatusBlock.tsx - the status line and the problems band of the shown ski area (hub v3).
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/
 * spec/ch6-hub-ui.md section 6.5, design hub-v3-design.md 3.1, 5.2, 6.2, 6.3, 7.2; code from the
 * checked reference spec/ch6-hub-ui-ref/src/hub/StatusBlock.tsx).
 * Contract:
 *   - Status line `div.statusRow` (ref statusRef, fixed 1.6 em of S): the open-mark shape of
 *     ResortLive.status (3 full, 2 partly, 1/0 closed dash; estimated -> 55 % opacity; Not linked:
 *     the Warning glyph; no live row: an empty box) . ONE text node by hubRules.statusVariant (one
 *     <LocText> per branch, gate R13; second pass: "partly open only" counts the partly open runs, so
 *     "Partly open · 0 of 7 runs open" cannot happen) . a spacer . the SEASON_HINT word (only for a
 *     sign row with runs). Tooltip (sign with runs and a live row): STATUS_TIP_COUNTS, the open rule,
 *     gating off / estimated / measured, SEASON_TIP of the hint, STATUS_TIP_TEMP when known,
 *     STATUS_TIP_VISUALS when the game's snow visuals are off (ch3 E1-S3).
 *   - T82 UI round (2026-10-19; docs/research/ui/hub-design-review-2026-10-18.md B5a, F10a): the hint slot shows the snow,
 *     STATUS_SNOW "64 cm · 88 %" (ResortLive.snowDepthCm / snowPct, "–" for an unknown part; "≈" in a cell before it while
 *     snowEstimated, normal color), where it said "Snow season"; hubRules.statusHint "snow". The SEASON_HINT word moves into the tooltip (above its
 *     SEASON_TIP) unless it is a warning (SEASON_HINT_WARN: Thawing, Waiting for snow, Too warm: "warn", the word stays, warning
 *     color) or "Powder day" holds the slot (D29); then the tooltip carries the snow pair (Snow depth, Run coverage).
 *   - Problems band `div.problems` (ref problemsRef): ALWAYS three `div.problemSlotRow` of 1.45 em,
 *     so a value or problem change never changes its height (D33). Slot i shows bandSlots(rows)
 *     .slots[i] (C# ranks and caps the rows; hubRules.bandRowsOf keeps every code the band knows,
 *     Factor rows excluded); slot 3 also carries the fixed-width ".problemMore" cell ("+N" with the
 *     hidden rows in its tooltip, else empty). With no band row, slot 1 is the inert None line.
 *     (Deviation from the reference file, which rendered an empty slot as a bare .problemSlot outside
 *     a .problemSlotRow: ch6 section 6.5 says "always three div.problemSlotRow", and a flex-basis-0
 *     slot directly in the column could resolve shorter than 1.45 em.)
 *   - A row is ONE text node (ProblemText) with its icon (hubRules.problemIcon: the worst factor's
 *     notification icon for Efficiency, the plug for LiftNoPower, TurnedOff for lodges off, else the
 *     Warning / Info glyph). Clickable rows (hubRules.problemClick) are a cs2/ui Button: fly to the
 *     sign or the lift (focusBuilding / focusLift), or open the Lifts / Runs view; inert rows are a
 *     plain div. Severity <= 1 is dim. Focus: content area, default focus (ch6 section 9, F17).
 *   - Row tooltip: the row's text, its PROBLEM_TIP, for Efficiency the game's factor lines (signed %)
 *     and STAFF_LINE, the PROBLEM_GAIN line when C# sent ifFixed >= 0, and what a click does.
 *   - Panel file (gate R11): subscribes resorts$, selectedResort$, resortLive$, problems$, city$.
 *   - Round F7 (2026-10-04, implementer 4; docs/research/field-test-7-fixes.md D4-D6, findings/hub-ux.md
 *     §3.2 / §5 with its Verification):
 *       - the band's dark background is drawn per FILLED slot (`.problemRowOn`), not on the whole band,
 *         so one problem no longer sits on a box with two empty slots; the band keeps its three-slot
 *         height (D33) and its side padding moved into each slot row, so the text room is unchanged;
 *       - the efficiency rows name the sign ("Welcome Sign at {PCT}: {REASON}", "... and {N} more": "+N"
 *         stays the slot-3 cell's "more problems"); their tooltip no longer tells the player to connect a
 *         sign that is connected (PROBLEM_TIP[Efficiency]), the game's factor lines follow it. Width in S
 *         (Overpass advances; 100 / 130 / 150 %) against the slot room — slots 1-2 387 / 551 / 661 rem,
 *         slot 3 354 / 497 / 592 (beside the "+N" cell). F7 fixer, corrected: "Welcome Sign at 11 %:
 *         Backed up sewer and 3 more" (field test 7's row) 319.6 / 520.4 / 655.1 fits slots 1-2, but in slot
 *         3 only at 100 %; "Welcome Sign at 12 %: Lack of electricity and 3 more" 328.9 / 535.7 / 674.3 is cut
 *         at 150 % in slots 1-2, at 130 and 150 % in slot 3; "Welcome Sign at 45 %: Not Enough Employees and
 *         3 more" 366.6 / 597.0 / 751.5 is cut at 130 and 150 % in slots 1-2 and at EVERY size in slot 3;
 *         "Welcome Sign not running: Not Enough Employees and 3 more" 390.5 / 635.9 / 800.5 is cut at every
 *         size in every slot. The road rows below are severity 3, ranked above a 1-90 % efficiency row
 *         (severity 2), so with a sign and a lodge off the road the efficiency row sits in slot 3. The row
 *         text is the tooltip's first line, the ellipsis is the net (design §2.3); a fixed "and {N} more"
 *         tail cell was not built (owner's call, field-test-7-fixes.md §4.4 item 9);
 *       - D5: SignNoRoad (18) "Welcome Sign is not at a road" (S 184.1 / 300.1 / 377.4) and LodgeNoRoad
 *         (19) "Lodge is not at a road: guests cannot reach it" (280.0 / 456.5 / 574.1) fit every slot;
 *         both fly to their `subject` (the sign; the first lodge without a road). C# ranks them
 *         (field-test-7-notes-impl3.md).
 *   - Round S1 "Ski days" (2026-10-05, stream E; docs/research/ski-days/spec/ch4-hub.md sections C.5, D):
 *       - D28: three band rows, plain text, flying to their `subject`: SkiDaysCannotReach (20, sev 2,
 *         Warning glyph) "Guests cannot reach the lodge", NoParkingNearLodge (21, dim) "No parking near the
 *         lodge", LodgeNotSlopeside (22, dim) "Lodge is far from the runs" (29, 25, 26 characters: shorter
 *         than LodgeNoRoad's 46, which fits every slot at every size); each tooltip is its PROBLEM_TIP and
 *         PROBLEM_CLICK_FLY;
 *       - D29: the hint slot shows POWDER_DAY "Powder day" (normal text color, `.statusHintOn`) when
 *         hubRules.statusHint says "powder" (skiDaysPowderPct >= 50), else the SEASON_HINT word as before;
 *         same slot, same height, and "Powder day" (10 characters) is shorter than "Too warm for snow"
 *         (17). The status tooltip adds STATUS_TIP_POWDER after the season tip while powder > 0.
 *   - T114 (2026-10-20, field report items 2 and 9: "Lift 3 / 4 / 5 is unfinished +5" stacked three slots;
 *     docs/research/ui/hub-round-t114-2026-10-20.md): the band shows hubRules.foldBandRows: two or more rows of one per-lift code
 *     (LiftUnfinished, LiftNoPower, LiftSectionMissing) are ONE row, PROBLEM_GROUP "3 lifts unfinished: Lift 3, 4, 5"
 *     (hubRules.liftNameList), its click opens the Lifts view, its tooltip lists every lift. The "+N" cell is a button: it opens
 *     a popover (hubState.problemsOpen$), a card hanging under the band (.problems is its anchor) with one clickable row per
 *     hidden band row (same click as in a slot, then it closes); the band keeps its three fixed slots, so nothing grows. The
 *     shared backdrop (HubPanel OverlayBackdrop) closes it.
 */
import type { RefObject } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { Unit } from "cs2/l10n";
import { PROBLEM, city$, hubTriggers, problems$, resortLive$, resorts$, selectedResort$ } from "../bindings";
import type { CityStats, ProblemRow, ResortLive, ResortRow } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import {
  bandRowsOf, bandSlots, closedRuns, factorRowsOf, foldBandRows, liftNameList, liveOf, problemClick, problemIcon, problemVariant,
  statusHint, statusVariant, statusWord,
} from "./hubRules";
import type { BandRow, ProblemClick } from "./hubRules";
import { problemsOpen$, view$ } from "./hubState";
import { HUB_FMT, HUB_LOC, SEASON_HINT_TEXT, SEASON_TIP_TEXT, STATUS_WORD, factorEntry } from "./hubText";
import { Glyph, LocText, NotifIcon, Tip, TipLines, cx, depthValue, numArg, useInches } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import styles from "./hub.module.scss";

// ---- status line ----------------------------------------------------------------------------------

function StatusSentence({ row, live, t }: { row: ResortRow | null; live: ResortLive | null; t: Translate }): JSX.Element {
  const v = statusVariant(row, live);
  if (v === "unlinked") return <>{t(HUB_LOC.statusUnlinked)}</>;
  if (v === "noRuns") return <>{t(HUB_LOC.statusNoRuns)}</>;
  if (v === "unknown" || !row || !live) return <>{"–"}</>;
  const STATE = t(STATUS_WORD[statusWord(live.status)]);
  const OPEN = numArg(live.runsOpen, Unit.Integer);
  const TOTAL = numArg(row.runs, Unit.Integer);
  const PARTLY = numArg(live.runsPartly, Unit.Integer);
  switch (v) {
    case "partly": return <LocText entry={HUB_FMT.statusLinePartly} args={{ STATE, PARTLY, TOTAL }} />;
    case "partlyEst": return <LocText entry={HUB_FMT.statusLinePartlyEst} args={{ STATE, PARTLY, TOTAL }} />;
    case "partlyOne": return <LocText entry={HUB_FMT.statusLinePartlyOne} args={{ STATE, PARTLY }} />;
    case "partlyOneEst": return <LocText entry={HUB_FMT.statusLinePartlyOneEst} args={{ STATE, PARTLY }} />;
    case "one": return <LocText entry={HUB_FMT.statusLineOne} args={{ STATE, OPEN }} />;
    case "oneEst": return <LocText entry={HUB_FMT.statusLineOneEst} args={{ STATE, OPEN }} />;
    case "lineEst": return <LocText entry={HUB_FMT.statusLineEst} args={{ STATE, OPEN, TOTAL }} />;
    default: return <LocText entry={HUB_FMT.statusLine} args={{ STATE, OPEN, TOTAL }} />;
  }
}

function StatusMark({ row, live }: { row: ResortRow | null; live: ResortLive | null }): JSX.Element {
  if (row && row.kind === 1) return <Glyph src="Media/Glyphs/Warning.svg" className={cx(styles.statusMark, styles.glyphWarnTone)} />;
  const s = live ? live.status : -1;
  const shape = s === 3 ? styles.openFull : s === 2 ? styles.openPartly : s >= 0 ? styles.openClosed : null;
  return (
    <div className={styles.statusMark}>
      {shape && <div className={cx(styles.openMark, shape, live && live.snowEstimated && styles.openEstimated)} />}
    </div>
  );
}

function statusTip(row: ResortRow | null, live: ResortLive | null, city: CityStats, t: Translate, inches: boolean): JSX.Element | undefined {
  if (!row || row.kind === 1 || !live || (row.runs | 0) <= 0) return undefined;
  const seasonTip = SEASON_TIP_TEXT[live.seasonHint] ?? null;
  // T82 B5a: the hint word one layer down when the slot shows the snow; the snow pair here when the slot shows a word instead.
  const hint = statusHint(row, live);
  const seasonWord = hint !== "warn" && hint !== "season" && seasonTip ? SEASON_HINT_TEXT[live.seasonHint] ?? null : null;
  const snowHere = hint === "powder" || hint === "warn";
  return (
    <TipLines
      lines={[
        <LocText key="c" entry={HUB_FMT.statusTipCounts} args={{
          OPEN: numArg(live.runsOpen, Unit.Integer), PARTLY: numArg(live.runsPartly, Unit.Integer),
          CLOSED: numArg(closedRuns(row, live), Unit.Integer),
        }} />,
        t(HUB_LOC.statusTipRule),
        !city.snowGating ? t(HUB_LOC.statusTipGating) : live.snowEstimated ? t(HUB_LOC.statusTipEstimated) : t(HUB_LOC.statusTipMeasured),
        seasonWord && t(seasonWord),
        seasonTip && t(seasonTip),
        snowHere && live.snowDepthCm >= 0 && { label: t(HUB_LOC.mountainSnowDepth), value: inches
          ? <LocText entry={HUB_FMT.snowDepthIn} args={{ DEPTH: numArg(depthValue(live.snowDepthCm, true), Unit.Integer) }} />
          : <LocText entry={HUB_FMT.snowDepth} args={{ DEPTH: numArg(live.snowDepthCm, Unit.Integer) }} /> },
        snowHere && live.snowPct >= 0 && { label: t(HUB_LOC.mountainSnowCover), value: numArg(live.snowPct, Unit.Percentage) },
        // Round S1 D29: the powder now (0-100; -1 unknown, 0 none: no line).
        live.skiDaysPowderPct > 0 && <LocText key="p" entry={HUB_FMT.statusTipPowder} args={{ PCT: numArg(live.skiDaysPowderPct, Unit.Percentage) }} />,
        live.topTempKnown && <LocText key="t" entry={HUB_FMT.statusTipTemp} args={{ TEMP: numArg(live.topTempC, Unit.Temperature, false, false) }} />,
        city.snowVisualsOff && t(HUB_LOC.statusTipVisuals),
      ]}
    />
  );
}

// ---- problems band ----------------------------------------------------------------------------------

/** One band row as ONE text node (one <LocText> per branch: gate R13). */
function ProblemText({ p, t }: { p: ProblemRow; t: Translate }): JSX.Element {
  const NAME = p.name || "–";
  const N = numArg(p.count, Unit.Integer);
  const PCT = numArg(p.pct, Unit.Percentage);
  const REASON = t(factorEntry(p.factor));
  const MORE = numArg(p.more, Unit.Integer);
  switch (problemVariant(p)) {
    case "unlinked": return <>{t(HUB_LOC.problemUnlinked)}</>;
    case "noLodge": return <>{t(HUB_LOC.problemNoLodge)}</>;
    case "lodgesAllOff": return <>{t(HUB_LOC.problemLodgesAllOff)}</>;
    case "lodgesSomeOff": return <LocText entry={HUB_FMT.problemLodgesSomeOff} args={{ N }} />;
    case "efficiency": return <LocText entry={HUB_FMT.problemEfficiency} args={{ PCT, REASON }} />;
    case "efficiencyMore": return <LocText entry={HUB_FMT.problemEfficiencyMore} args={{ PCT, REASON, N: MORE }} />;
    case "notRunning": return <LocText entry={HUB_FMT.problemNotRunning} args={{ REASON }} />;
    case "notRunningMore": return <LocText entry={HUB_FMT.problemNotRunningMore} args={{ REASON, N: MORE }} />;
    case "liftNoPower": return <LocText entry={HUB_FMT.problemLiftNoPower} args={{ NAME }} />;
    case "liftSectionMissing": return <LocText entry={HUB_FMT.problemLiftSectionMissing} args={{ NAME }} />;
    case "liftUnfinished": return <LocText entry={HUB_FMT.problemLiftUnfinished} args={{ NAME }} />;
    case "noLift": return <>{t(HUB_LOC.problemNoLift)}</>;   // T77: no {PCT} on the line (the tooltip says it)
    case "noRuns": return <>{t(HUB_LOC.problemNoRuns)}</>;
    case "stationsPair": return <LocText entry={HUB_FMT.problemStationsPair} args={{ NAME }} />;
    case "stationsBaseOnly": return <LocText entry={HUB_FMT.problemStationsBaseOnly} args={{ NAME }} />;
    case "stationsTopOnly": return <LocText entry={HUB_FMT.problemStationsTopOnly} args={{ NAME }} />;
    case "liftChairsStopped": return <LocText entry={HUB_FMT.problemLiftChairsStopped} args={{ NAME }} />;
    case "runsUnserved": return <LocText entry={HUB_FMT.problemRunsUnserved} args={{ N }} />;
    case "runsUnrateable": return <LocText entry={HUB_FMT.problemRunsUnrateable} args={{ N }} />;
    case "runsRating": return <LocText entry={HUB_FMT.problemRunsRating} args={{ N }} />;
    case "freeLodges": return <LocText entry={HUB_FMT.problemFreeLodges} args={{ N }} />;
    case "runsFlat": return <LocText entry={HUB_FMT.problemRunsFlat} args={{ N }} />;
    case "signNoRoad": return <>{t(HUB_LOC.problemSignNoRoad)}</>;
    case "lodgeNoRoad": return <>{t(HUB_LOC.problemLodgeNoRoad)}</>;
    case "skiDaysCannotReach": return <>{t(HUB_LOC.problemSkiDaysCannotReach)}</>;
    case "noParkingNearLodge": return <>{t(HUB_LOC.problemNoParkingNearLodge)}</>;
    case "lodgeNotSlopeside": return <>{t(HUB_LOC.problemLodgeNotSlopeside)}</>;
    // Catalog Phase B (round-2-contract §8.5, codes 23-25; integrator, patch from build-hub.md §6)
    case "mixedLiftTypes": return <LocText entry={HUB_FMT.problemMixedLiftTypes} args={{ NAME }} />;
    case "summitLodgeNotLiftServed": return <LocText entry={HUB_FMT.problemSummitLodgeNotLiftServed} args={{ N }} />;
    case "schoolNoGreenRun": return <>{t(HUB_LOC.problemSchoolNoGreenRun)}</>;
    case "schoolsNotLinked": return <LocText entry={HUB_FMT.problemSchoolsNotLinked} args={{ N }} />;   // T100
    default: return <>{"–"}</>;
  }
}

/** T114: a folded band row's ONE text node ("3 lifts unfinished: Lift 3, 4, 5"); a plain row is ProblemText. */
function BandText({ b, t }: { b: BandRow; t: Translate }): JSX.Element {
  if (b.group.length < 2) return <ProblemText p={b.row} t={t} />;
  const N = numArg(b.group.length, Unit.Integer);
  const NAMES = liftNameList(b.group.map((p) => p.name));
  switch (b.row.code) {
    case PROBLEM.LiftUnfinished: return <LocText entry={HUB_FMT.problemGroupLiftUnfinished} args={{ N, NAMES }} />;
    case PROBLEM.LiftNoPower: return <LocText entry={HUB_FMT.problemGroupLiftNoPower} args={{ N, NAMES }} />;
    default: return <LocText entry={HUB_FMT.problemGroupLiftSectionMissing} args={{ N, NAMES }} />;
  }
}

/** T114: a folded row opens the Lifts view (it names several lifts); a plain row clicks as before. */
function bandClick(b: BandRow): ProblemClick {
  return b.group.length >= 2 ? "liftsView" : problemClick(b.row);
}

function runClick(click: ProblemClick, p: ProblemRow): void {
  if (click === "sign") hubTriggers.focusBuilding(p.subject);
  else if (click === "lift") hubTriggers.focusLift(p.subject, p.subjectTop);
  else if (click === "liftsView") view$.update("lifts");
  else if (click === "runsView") view$.update("runs");
}

/** The tooltip of a band row; a folded row lists its lifts, one line each, then what a click does. */
function bandTip(b: BandRow, factors: readonly ProblemRow[], live: ResortLive | null, t: Translate): JSX.Element {
  if (b.group.length < 2) return problemTip(b.row, factors, live, t);
  return (
    <TipLines
      lines={[
        <BandText key="h" b={b} t={t} />,
        ...b.group.map((p, i) => <ProblemText key={i} p={p} t={t} />),
        t(HUB_LOC.problemClickList),
      ]}
    />
  );
}

/** The tooltip body of a band row (design 6.3). */
function problemTip(p: ProblemRow, factors: readonly ProblemRow[], live: ResortLive | null, t: Translate): JSX.Element {
  const PCT = numArg(p.pct, Unit.Percentage);
  const v = problemVariant(p);
  let body: string | JSX.Element | null = null;
  switch (v) {
    case "unlinked": body = t(HUB_LOC.unlinkedHint); break;
    case "noLodge": body = t(HUB_LOC.problemTipNoLodge); break;
    case "lodgesAllOff": body = t(HUB_LOC.problemTipLodgesAllOff); break;
    case "lodgesSomeOff": body = t(HUB_LOC.problemTipLodgesSomeOff); break;
    case "efficiency": case "efficiencyMore": case "notRunning": case "notRunningMore": body = t(HUB_LOC.problemTipEfficiency); break;
    case "liftNoPower": body = t(HUB_LOC.problemTipLiftNoPower); break;
    case "liftSectionMissing": body = t(HUB_LOC.problemTipLiftSectionMissing); break;
    case "liftUnfinished": body = t(HUB_LOC.problemTipLiftUnfinished); break;
    case "noLift": body = <LocText entry={HUB_FMT.problemTipNoLift} args={{ PCT }} />; break;
    case "noRuns": body = t(HUB_LOC.problemTipNoRuns); break;
    case "stationsPair": case "stationsBaseOnly": case "stationsTopOnly": body = t(HUB_LOC.problemTipStations); break;
    case "liftChairsStopped": body = t(HUB_LOC.problemTipLiftChairsStopped); break;
    case "runsUnserved": body = <LocText entry={HUB_FMT.problemTipRunsUnserved} args={{ PCT }} />; break;
    case "runsUnrateable": body = t(HUB_LOC.problemTipRunsUnrateable); break;
    case "runsRating": body = t(HUB_LOC.problemTipRunsRating); break;
    case "freeLodges": body = t(HUB_LOC.problemTipFreeLodges); break;
    case "runsFlat": body = t(HUB_LOC.problemTipRunsFlat); break;
    case "signNoRoad": body = t(HUB_LOC.problemTipSignNoRoad); break;
    case "lodgeNoRoad": body = t(HUB_LOC.problemTipLodgeNoRoad); break;
    case "skiDaysCannotReach": body = t(HUB_LOC.problemTipSkiDaysCannotReach); break;
    case "noParkingNearLodge": body = t(HUB_LOC.problemTipNoParkingNearLodge); break;
    case "lodgeNotSlopeside": body = t(HUB_LOC.problemTipLodgeNotSlopeside); break;
    case "mixedLiftTypes": body = t(HUB_LOC.problemTipMixedLiftTypes); break;
    case "summitLodgeNotLiftServed": body = t(HUB_LOC.problemTipSummitLodgeNotLiftServed); break;
    case "schoolNoGreenRun": body = t(HUB_LOC.problemTipSchoolNoGreenRun); break;
    case "schoolsNotLinked": body = t(HUB_LOC.problemTipSchoolsNotLinked); break;   // T100
    default: body = null;
  }
  const isEff = p.code === PROBLEM.Efficiency;
  const gain = p.ifFixed >= 0
    ? (isEff ? <LocText key="g" entry={HUB_FMT.problemGainEfficiency} args={{ FULL: numArg(p.ifFixed, Unit.Integer) }} />
      : p.code === PROBLEM.NoLodge ? <LocText key="g" entry={HUB_FMT.problemGainLodge} args={{ A: numArg(p.ifFixed, Unit.Integer) }} />
        : p.code === PROBLEM.LodgesAllOff ? <LocText key="g" entry={HUB_FMT.problemGainLodgesOn} args={{ A: numArg(p.ifFixed, Unit.Integer) }} />
          : null)
    : null;
  const click = problemClick(p);
  return (
    <TipLines
      lines={[
        <ProblemText key="h" p={p} t={t} />,
        body,
        ...(isEff ? factors.map((f) => ({ label: t(factorEntry(f.factor)), value: numArg(f.pct, Unit.Percentage, true, false) })) : []),
        // second pass: the staff numbers next to the game's reasons (ResortLive jobs / jobsFilled; -1 = unknown)
        isEff && live && live.jobs >= 0 && <LocText key="s" entry={HUB_FMT.staffLine} args={{ FILLED: numArg(live.jobsFilled, Unit.Integer), JOBS: numArg(live.jobs, Unit.Integer) }} />,
        gain,
        click === "sign" || click === "lift" ? t(HUB_LOC.problemClickFly) : click ? t(HUB_LOC.problemClickList) : null,
      ]}
    />
  );
}

function ProblemIconEl({ p }: { p: ProblemRow | null }): JSX.Element {
  const icon = problemIcon(p);
  if (icon.kind === "img") return <NotifIcon src={icon.src} className={styles.problemIcon} />;
  const tone = icon.tone === "warn" ? styles.glyphWarnTone : icon.tone === "info" ? styles.glyphDimTone : styles.glyphDimmerTone;
  return <Glyph src={icon.src} className={cx(styles.problemIcon, tone)} />;
}

/** One of the three fixed slots: always a `.problemSlotRow` (1.45 em); slot 3 carries the "+N" cell. */
function ProblemSlot({ b, slot, more, hidden, factors, live, t, noneLine, popOpen }: {
  b: BandRow | null; slot: number; more: number; hidden: readonly BandRow[]; factors: readonly ProblemRow[];
  live: ResortLive | null; t: Translate; noneLine: boolean; popOpen: boolean;
}): JSX.Element {
  // T114: "+N" is a button that opens the popover under the band (its tooltip still names the hidden rows).
  const moreCell = slot !== 2 ? null : more > 0 ? (
    <div className={styles.problemMore}>
      <Tip tip={popOpen ? undefined : <TipLines lines={[t(HUB_LOC.problemMoreTip), ...hidden.map((h, i) => <BandText key={i} b={h} t={t} />),
        t(HUB_LOC.problemMoreClick)]} />}>
        <Button theme={{ button: cx(styles.problemMoreButton, popOpen && styles.problemMoreOn) }}
          onSelect={() => problemsOpen$.update(!problemsOpen$.value)}>
          <LocText entry={HUB_FMT.problemMore} args={{ N: numArg(more, Unit.Integer) }} />
        </Button>
      </Tip>
    </div>
  ) : <div className={styles.problemMore} />;
  const p = b ? b.row : null;
  if (!b || !p) {
    // An empty slot, or the None line in slot 1 (inert, dimmer check mark).
    return (
      <div className={styles.problemSlotRow}>
        <div className={cx(styles.problemSlot, styles.problemDim)}>
          {noneLine && <ProblemIconEl p={null} />}
          <div className={styles.problemText}>{noneLine ? t(HUB_LOC.problemNone) : ""}</div>
        </div>
        {moreCell}
      </div>
    );
  }
  const click = bandClick(b);
  const content = (
    <>
      <ProblemIconEl p={p} />
      <div className={styles.problemText}><BandText b={b} t={t} /></div>
    </>
  );
  const onSelect = (): void => runClick(click, p);
  return (
    <div className={cx(styles.problemSlotRow, styles.problemRowOn)}>
      <Tip tip={bandTip(b, factors, live, t)}>
        {click ? (
          <Button theme={{ button: cx(styles.problemSlot, styles.problemClickable, p.severity <= 1 && styles.problemDim) }} onSelect={onSelect}>
            {content}
          </Button>
        ) : (
          <div className={cx(styles.problemSlot, p.severity <= 1 && styles.problemDim)}>{content}</div>
        )}
      </Tip>
      {moreCell}
    </div>
  );
}

export function StatusBlock({ statusRef, problemsRef }: {
  statusRef: RefObject<HTMLDivElement>; problemsRef: RefObject<HTMLDivElement>;
}): JSX.Element {
  const t = useT();
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const live = useValue(resortLive$);
  const problems = useValue(problems$);
  const city = useValue(city$);
  const inches = useInches();   // T130: snow depth in the player's units

  const cur = currentResort(resortEntries(resorts), selected);
  const row = cur.row;
  const sign = row && row.kind === 1 ? null : row ? row.sign : null;
  const l = row ? liveOf(live, sign) : null;
  const band = foldBandRows(bandRowsOf(problems, sign));   // T114: per-lift rows of one code fold into one
  const factors = factorRowsOf(problems, sign);
  const { slots, more } = bandSlots(band);
  const popOpen = useValue(problemsOpen$) && more > 0;
  // Round S1 D29: "Powder day" in the hint slot (normal color), else the SEASON_HINT word; same slot, same height.
  const hintKind = statusHint(row, l);
  const powder = hintKind === "powder";
  const warn = hintKind === "warn";
  const seasonHint = (hintKind === "season" || warn) && l ? SEASON_HINT_TEXT[l.seasonHint] ?? null : null;
  // T82 B5a: the snow, depth cm · coverage % (PIPELINE section 0: always the two numbers), "≈" before it while estimated.
  const snow = hintKind === "snow" && l;

  return (
    <>
      <Tip tip={statusTip(row, l, city, t, inches)}>
        <div ref={statusRef} className={styles.statusRow}>
          <StatusMark row={row} live={l} />
          <div className={styles.statusText}><StatusSentence row={row} live={l} t={t} /></div>
          <div className={styles.statusSpacer} />
          {snow && l.snowEstimated && <div className={styles.statusSnowMark}>{"≈"}</div>}
          <div className={cx(styles.statusHint, (powder || snow) && styles.statusHintOn, warn && styles.statusHintWarn)}>
            {snow
              ? (inches
                ? <LocText entry={HUB_FMT.statusSnowIn} args={{ DEPTH: numArg(depthValue(l.snowDepthCm, true), Unit.Integer), PCT: numArg(l.snowPct, Unit.Percentage) }} />
                : <LocText entry={HUB_FMT.statusSnow} args={{ DEPTH: numArg(l.snowDepthCm, Unit.Integer), PCT: numArg(l.snowPct, Unit.Percentage) }} />)
              : powder ? t(HUB_LOC.powderDay) : seasonHint ? t(seasonHint) : ""}
          </div>
        </div>
      </Tip>
      <div ref={problemsRef} className={cx(styles.problems, styles.problemsAnchor)}>
        {slots.map((b, i) => (
          <ProblemSlot key={i} b={b} slot={i} more={i === 2 ? more : 0} hidden={band.slice(3)} factors={factors} live={l} t={t}
            noneLine={i === 0 && band.length === 0} popOpen={popOpen} />
        ))}
        {popOpen && (
          <div className={cx(styles.overlayCard, styles.overlayBelow)}>
            <div className={styles.overlayTitle}>{t(HUB_LOC.problemMoreTip)}</div>
            {band.slice(3).map((b, i) => {
              const click = bandClick(b);
              const row = (
                <>
                  <ProblemIconEl p={b.row} />
                  <div className={styles.problemText}><BandText b={b} t={t} /></div>
                </>
              );
              return (
                <Tip key={i} tip={bandTip(b, factors, l, t)}>
                  {click ? (
                    <Button theme={{ button: cx(styles.popoverRow, b.row.severity <= 1 && styles.problemDim) }}
                      onSelect={() => { problemsOpen$.update(false); runClick(click, b.row); }}>
                      {row}
                    </Button>
                  ) : (
                    <div className={cx(styles.popoverRow, b.row.severity <= 1 && styles.problemDim)}>{row}</div>
                  )}
                </Tip>
              );
            })}
          </div>
        )}
      </div>
    </>
  );
}
