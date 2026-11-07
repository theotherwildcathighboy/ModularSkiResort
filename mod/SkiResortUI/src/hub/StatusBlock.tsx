/*
 * Status line and problems band of the shown ski area.
 * Status line `div.statusRow` (fixed 1.6 em): ONE text node chosen by hubRules.statusVariant (one <LocText> per branch, gate R13), then a hint slot.
 * Problems band `div.problems`: always ONE `div.problemSlotRow` (1.45 em, hubRules.BAND_SLOTS, DECISIONS 613). It shows the worst row C# ranks first,
 * so its height never changes. It carries the "+N" button that opens the popover with the others. Two or more rows of one per-lift code
 * fold into one (hubRules.foldBandRows). A row is ONE text node. Clickable rows (hubRules.problemClick) are a cs2/ui Button, and inert rows are
 * a plain div. The row text is the tooltip's first line, so the ellipsis is the net.
 * Panel file (gate R11): subscribes resorts$, selectedResort$, resortLive$, problems$, city$.
 */
import type { RefObject } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { PROBLEM, city$, hubTriggers, lodgeParking$, problems$, resortLive$, resorts$, sameEntity, selectedResort$ } from "../bindings";
import type { BuildItemId, CityStats, ProblemRow, ResortLive, ResortRow } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import {
  BAND_SLOTS, bandRowsOf, bandSlots, closedRuns, factorRowsOf, foldBandRows, liftNameList, liveOf, problemClick, problemIcon, problemVariant,
  statusHint, statusVariant, statusWord,
} from "./hubRules";
import type { BandRow, ProblemClick, ProblemVariant } from "./hubRules";
import { problemsOpen$, view$ } from "./hubState";
import { HUB_FMT, HUB_LOC, LODGE_TIER, SEASON_HINT_TEXT, SEASON_TIP_TEXT, STATUS_WORD, factorEntry } from "./hubText";
import { Glyph, LocText, NotifIcon, Tip, TipLines, cx, depthValue, numArg, useInches } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import styles from "./hub.module.scss";

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
  if (!row || row.kind === 1 || !live || live.runsOpen < 0 || (row.runs | 0) <= 0) return undefined;
  const seasonTip = SEASON_TIP_TEXT[live.seasonHint] ?? null;
  // The hint word one layer down when the slot shows the snow. The snow pair here when the slot shows a word instead
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
        // Measured snow is the default and gets no line (T142). Only the exceptions say how the snow is read.
        !city.snowGating ? t(HUB_LOC.statusTipGating) : live.snowEstimated && t(HUB_LOC.statusTipEstimated),
        seasonWord && t(seasonWord),
        seasonTip && t(seasonTip),
        // The reading the season tip is about, right under it (T142: the tips no longer repeat "day average at the top").
        live.topTempKnown && <LocText key="t" entry={HUB_FMT.statusTipTemp} args={{ TEMP: numArg(live.topTempC, Unit.Temperature, false, false) }} />,
        snowHere && live.snowDepthCm >= 0 && { label: t(HUB_LOC.mountainSnowDepth), value: inches
          ? <LocText entry={HUB_FMT.snowDepthIn} args={{ DEPTH: numArg(depthValue(live.snowDepthCm, true), Unit.Integer) }} />
          : <LocText entry={HUB_FMT.snowDepth} args={{ DEPTH: numArg(live.snowDepthCm, Unit.Integer) }} /> },
        snowHere && live.snowPct >= 0 && { label: t(HUB_LOC.mountainSnowCover), value: numArg(live.snowPct, Unit.Percentage) },
        // The powder now (0-100, -1 unknown, 0 none: no line)
        live.skiDaysPowderPct > 0 && <LocText key="p" entry={HUB_FMT.statusTipPowder} args={{ PCT: numArg(live.skiDaysPowderPct, Unit.Percentage) }} />,
        city.snowVisualsOff && t(HUB_LOC.statusTipVisuals),
      ]}
    />
  );
}

/** One band row as ONE text node (one <LocText> per branch: gate R13). */
function ProblemText({ p, t }: { p: ProblemRow; t: Translate }): JSX.Element {
  const NAME = p.name || "–";
  const N = numArg(p.count, Unit.Integer);
  const PCT = numArg(p.pct, Unit.Percentage);
  const REASON = t(factorEntry(p.factor));
  switch (problemVariant(p)) {
    case "unlinked": return <>{t(HUB_LOC.problemUnlinked)}</>;
    case "noLodge": return <>{t(HUB_LOC.problemNoLodge)}</>;
    case "lodgesAllOff": return <>{t(HUB_LOC.problemLodgesAllOff)}</>;
    case "lodgesSomeOff": return <LocText entry={HUB_FMT.problemLodgesSomeOff} args={{ N }} />;
    // The other factors live in the tooltip (problemTip lists every one). A "+N" here read like the band's own "+N" beside it
    // (in game 2026-11-07: "... Not Enough Employees +3" next to "+3").
    case "efficiency": case "efficiencyMore": return <LocText entry={HUB_FMT.problemEfficiency} args={{ PCT, REASON }} />;
    case "notRunning": case "notRunningMore": return <LocText entry={HUB_FMT.problemNotRunning} args={{ REASON }} />;
    case "liftNoPower": return <LocText entry={HUB_FMT.problemLiftNoPower} args={{ NAME }} />;
    case "liftSectionMissing": return <LocText entry={HUB_FMT.problemLiftSectionMissing} args={{ NAME }} />;
    case "liftUnfinished": return <LocText entry={HUB_FMT.problemLiftUnfinished} args={{ NAME }} />;
    case "noLift": return <>{t(HUB_LOC.problemNoLift)}</>;   // no {PCT} on the line (the tooltip says it)
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
    case "skiDaysNoGuestsNear": return <LocText entry={HUB_FMT.problemSkiDaysNoGuestsNear} args={{ KM: numArg(p.detail / 10, Unit.FloatSingleFraction) }} />;
    case "noParkingNearLodge": return <ParkingProblemText p={p} t={t} />;
    case "lodgeNotSlopeside": return <>{t(HUB_LOC.problemLodgeNotSlopeside)}</>;
    case "depotNoHillLodge": return <>{t(HUB_LOC.problemDepotNoHillLodge)}</>;
    case "depotNoRoad": return <>{t(HUB_LOC.problemDepotNoRoad)}</>;
    // Codes 23-25
    case "mixedLiftTypes": return <LocText entry={HUB_FMT.problemMixedLiftTypes} args={{ NAME }} />;
    case "liftBend": return <LocText entry={HUB_FMT.problemLiftBend} args={{ NAME, DEG: numArg(p.detail / 10, Unit.FloatSingleFraction), N }} />;
    case "liftTrainLimit": return <LocText entry={HUB_FMT.problemLiftTrainLimit} args={{ NAME, N }} />;
    case "liftLowSpan": return <LowSpanText p={p} t={t} NAME={NAME} />;
    case "summitLodgeNotLiftServed": return <LocText entry={HUB_FMT.problemSummitLodgeNotLiftServed} args={{ N }} />;
    case "schoolNoGreenRun": return <>{t(HUB_LOC.problemSchoolNoGreenRun)}</>;
    case "schoolsNotLinked": return <LocText entry={HUB_FMT.problemSchoolsNotLinked} args={{ N }} />;
    default: return <>{"–"}</>;
  }
}

/** The LiftLowSpan row: "Lift 1: chairs pass 1.2 m above the ground". detail is the height in tenths (0 = the carriers touch the
 *  ground). pct 1 = cabins. The tip names the span (LowSpanTip). */
function LowSpanText({ p, t, NAME }: { p: ProblemRow; t: Translate; NAME: string }): JSX.Element {
  const CARRIERS = t(p.pct === 1 ? HUB_LOC.carriersCabins : HUB_LOC.carriersChairs);
  return p.detail > 0
    ? <LocText entry={HUB_FMT.problemLiftLowSpan} args={{ NAME, CARRIERS, M: numArg(p.detail / 10, Unit.FloatSingleFraction) }} />
    : <LocText entry={HUB_FMT.problemLiftLowSpanGround} args={{ NAME, CARRIERS }} />;
}

/** The LiftLowSpan tip: the span (count / more = its tower numbers, 0 = a station), then the planner's floor. */
function LowSpanTip({ p, t }: { p: ProblemRow; t: Translate }): JSX.Element {
  const WHERE = p.count > 0 && p.more > 0
    ? <LocText entry={HUB_FMT.lowSpanTowers} args={{ A: numArg(p.count, Unit.Integer), B: numArg(p.more, Unit.Integer) }} />
    : p.count > 0 || p.more > 0
      ? <LocText entry={HUB_FMT.lowSpanStation} args={{ N: numArg(Math.max(p.count, p.more), Unit.Integer) }} />
      : t(HUB_LOC.lowSpanStations);
  return <LocText entry={HUB_FMT.problemTipLiftLowSpan} args={{ WHERE }} />;
}

/** The parking row names its lodge (C#'s subject, found in the lodgeParking rows). It reads "No parking: Chalet 2", and "and {N} more"
 *  for the other lodges without parking. Until the rows arrive, the row shows the plain text. */
function ParkingProblemText({ p, t }: { p: ProblemRow; t: Translate }): JSX.Element {
  const rows = useValue(lodgeParking$);
  const r = rows.find((x) => sameEntity(x.lodge, p.subject));
  if (!r) return <>{t(HUB_LOC.problemNoParkingNearLodge)}</>;
  const tier = LODGE_TIER[r.item as BuildItemId];
  // GuestsView's lodge name, inline so gate R19 sees every {NAME} branch
  const NAME = r.name !== "" ? r.name
    : !tier ? "–"
      : r.number > 0 ? <LocText entry={HUB_FMT.lodgeTierNumbered} args={{ NAME: t(tier), N: numArg(r.number, Unit.Integer) }} />
        : t(tier);
  return p.count > 1
    ? <LocText entry={HUB_FMT.problemNoParkingAtMore} args={{ NAME, N: numArg(p.count - 1, Unit.Integer) }} />
    : <LocText entry={HUB_FMT.problemNoParkingAt} args={{ NAME }} />;
}

/** A folded band row's ONE text node ("3 lifts unfinished: Lift 3, 4, 5"). A plain row is ProblemText. */
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

/** A folded row opens the Lifts view (it names several lifts). A plain row clicks as before. */
function bandClick(b: BandRow): ProblemClick {
  return b.group.length >= 2 ? "liftsView" : problemClick(b.row);
}

function runClick(click: ProblemClick, p: ProblemRow): void {
  if (click === "sign") hubTriggers.focusBuilding(p.subject);
  else if (click === "lift") hubTriggers.focusLift(p.subject, p.subjectTop);
  else if (click === "liftsView") view$.update("lifts");
  else if (click === "runsView") view$.update("runs");
}

/** The single band rows whose row says it all: problemTip has no body for them, so a tooltip would only repeat the title. */
const TITLE_ONLY_TIP: ReadonlySet<ProblemVariant> = new Set<ProblemVariant>(["lodgeNoRoad", "unlinked", "noRuns", "runsFlat"]);

/** The tooltip of a band row. A folded row lists its lifts, one line each. A TITLE_ONLY_TIP row has none (null: Tip draws no
 *  balloon). */
function bandTip(b: BandRow, factors: readonly ProblemRow[], live: ResortLive | null, t: Translate): JSX.Element | null {
  if (b.group.length < 2) {
    const v = problemVariant(b.row);
    return v !== null && TITLE_ONLY_TIP.has(v) ? null : problemTip(b.row, factors, live, t);
  }
  return (
    <TipLines
      lines={[
        <BandText key="h" b={b} t={t} />,
        ...b.group.map((p, i) => <ProblemText key={i} p={p} t={t} />),
      ]}
    />
  );
}

/** The tooltip body of a band row. */
function problemTip(p: ProblemRow, factors: readonly ProblemRow[], live: ResortLive | null, t: Translate): JSX.Element {
  const PCT = numArg(p.pct, Unit.Percentage);
  const v = problemVariant(p);
  let body: string | JSX.Element | null = null;
  switch (v) {
    case "noLodge": body = t(HUB_LOC.problemTipNoLodge); break;
    case "lodgesAllOff": body = t(HUB_LOC.problemTipLodgesAllOff); break;
    case "lodgesSomeOff": body = t(HUB_LOC.problemTipLodgesSomeOff); break;
    case "efficiency": case "efficiencyMore": case "notRunning": case "notRunningMore": body = t(HUB_LOC.problemTipEfficiency); break;
    case "liftNoPower": body = t(HUB_LOC.problemTipLiftNoPower); break;
    case "liftSectionMissing": body = t(HUB_LOC.problemTipLiftSectionMissing); break;
    case "liftUnfinished": body = t(HUB_LOC.problemTipLiftUnfinished); break;
    case "noLift": body = <LocText entry={HUB_FMT.problemTipNoLift} args={{ PCT }} />; break;
    case "stationsPair": case "stationsBaseOnly": case "stationsTopOnly": body = t(HUB_LOC.problemTipStations); break;
    case "liftChairsStopped": body = t(HUB_LOC.problemTipLiftChairsStopped); break;
    case "runsUnserved": body = <LocText entry={HUB_FMT.problemTipRunsUnserved} args={{ PCT }} />; break;
    case "runsUnrateable": body = t(HUB_LOC.problemTipRunsUnrateable); break;
    case "runsRating": body = t(HUB_LOC.problemTipRunsRating); break;
    case "freeLodges": body = t(HUB_LOC.problemTipFreeLodges); break;
    case "skiDaysCannotReach": body = t(HUB_LOC.problemTipSkiDaysCannotReach); break;
    case "skiDaysNoGuestsNear": body = t(HUB_LOC.problemTipSkiDaysNoGuestsNear); break;
    case "noParkingNearLodge": body = t(HUB_LOC.problemTipNoParkingNearLodge); break;
    case "lodgeNotSlopeside": body = t(HUB_LOC.problemTipLodgeNotSlopeside); break;
    case "depotNoHillLodge": body = t(HUB_LOC.problemTipDepotNoHillLodge); break;
    case "depotNoRoad": body = t(HUB_LOC.problemTipDepotNoRoad); break;
    case "signNoRoad": body = t(HUB_LOC.problemTipSignNoRoad); break;
    case "mixedLiftTypes": body = t(HUB_LOC.problemTipMixedLiftTypes); break;
    case "liftBend": body = t(HUB_LOC.problemTipLiftBend); break;
    case "liftTrainLimit": body = t(HUB_LOC.problemTipLiftTrainLimit); break;
    case "liftLowSpan": body = <LowSpanTip p={p} t={t} />; break;
    case "summitLodgeNotLiftServed": body = t(HUB_LOC.problemTipSummitLodgeNotLiftServed); break;
    case "schoolNoGreenRun": body = t(HUB_LOC.problemTipSchoolNoGreenRun); break;
    case "schoolsNotLinked": body = t(HUB_LOC.problemTipSchoolsNotLinked); break;
    default: body = null;
  }
  const isEff = p.code === PROBLEM.Efficiency;
  const gain = p.ifFixed >= 0
    ? (isEff ? <LocText key="g" entry={HUB_FMT.problemGainEfficiency} args={{ FULL: numArg(p.ifFixed, Unit.Integer) }} />
      : p.code === PROBLEM.NoLodge ? <LocText key="g" entry={HUB_FMT.problemGainLodge} args={{ A: numArg(p.ifFixed, Unit.Integer) }} />
        : p.code === PROBLEM.LodgesAllOff ? <LocText key="g" entry={HUB_FMT.problemGainLodgesOn} args={{ A: numArg(p.ifFixed, Unit.Integer) }} />
          : null)
    : null;
  return (
    <TipLines
      lines={[
        <ProblemText key="h" p={p} t={t} />,
        body,
        ...(isEff ? factors.map((f) => ({ label: t(factorEntry(f.factor)), value: numArg(f.pct, Unit.Percentage, true, false) })) : []),
        // The staff numbers next to the game's reasons (ResortLive jobs / jobsFilled, -1 = unknown)
        isEff && live && live.jobs >= 0 && <LocText key="s" entry={HUB_FMT.staffLine} args={{ FILLED: numArg(live.jobsFilled, Unit.Integer), JOBS: numArg(live.jobs, Unit.Integer) }} />,
        gain,
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

/** A band slot: always a `.problemSlotRow` (1.45 em). The last slot carries the "+N" cell. */
function ProblemSlot({ b, slot, more, factors, live, t, noneLine, popOpen }: {
  b: BandRow | null; slot: number; more: number; factors: readonly ProblemRow[];
  live: ResortLive | null; t: Translate; noneLine: boolean; popOpen: boolean;
}): JSX.Element {
  // "+N" is a button that opens the popover under the band (no hover list: the click shows the same rows)
  const moreCell = slot !== BAND_SLOTS - 1 ? null : more > 0 ? (
    <div className={styles.problemMore}>
      <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.problemMoreButton, popOpen && styles.problemMoreOn) }}
        onSelect={() => problemsOpen$.update(!problemsOpen$.value)}>
        {more === 1 ? <LocText entry={HUB_FMT.problemMoreOne} args={{}} /> : <LocText entry={HUB_FMT.problemMore} args={{ N: numArg(more, Unit.Integer) }} />}
      </Button>
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
          <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.problemSlot, styles.problemClickable, p.severity <= 1 && styles.problemDim) }} onSelect={onSelect}>
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
  const inches = useInches();   // the player's units for snow depth

  const cur = currentResort(resortEntries(resorts), selected);
  const row = cur.row;
  const sign = row && row.kind === 1 ? null : row ? row.sign : null;
  const l = row ? liveOf(live, sign) : null;
  const band = foldBandRows(bandRowsOf(problems, sign));   // per-lift rows of one code fold into one
  const factors = factorRowsOf(problems, sign);
  const { slots, more } = bandSlots(band);
  const popOpen = useValue(problemsOpen$) && more > 0;
  // "Powder day" in the hint slot (normal color), else the SEASON_HINT word. Same slot, same height
  const hintKind = statusHint(row, l);
  const powder = hintKind === "powder";
  const warn = hintKind === "warn";
  const seasonHint = (hintKind === "season" || warn) && l ? SEASON_HINT_TEXT[l.seasonHint] ?? null : null;
  // The snow, depth cm · coverage % (always the two numbers), "≈" before it while estimated
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
          <ProblemSlot key={i} b={b} slot={i} more={i === BAND_SLOTS - 1 ? more : 0} factors={factors} live={l} t={t}
            noneLine={i === 0 && band.length === 0 && !(l && statusVariant(row, l) === "unknown")} popOpen={popOpen} />
        ))}
        {popOpen && (
          <div className={cx(styles.overlayCard, styles.overlayBelow)}>
            {band.slice(BAND_SLOTS).map((b, i) => {
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
                    <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.popoverRow, b.row.severity <= 1 && styles.problemDim) }}
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
