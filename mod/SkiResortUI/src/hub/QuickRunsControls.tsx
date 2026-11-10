/*
 * src/hub/QuickRunsControls.tsx - the Resort Planner's controls (code name Quick runs, experimental, C# QuickRunsToolSystem).
 * The file draws two things.
 * 1. The Build strip's Runs-tab chip (QuickRunsChip, the lift generator chip's pattern: grayed with the reason while unavailable).
 * 2. While the tool is open, the rows at the top of the Runs view, in place of the tree tool's (QuickRunsRows). From top to bottom:
 *    - Scan / Build / Undo, the Density (how many) and Kinds knobs. The Lifts, Mix, Shuffle and Way home knobs are gone (round R2-b, DECISIONS
 *      632): the runs-first planner ignores them.
 *    - The step's line (region size, scan progress, totals and mix, the site detail line), warnings and clusters. The totals count the pistes
 *      and the cat tracks apart.
 *    - The plan: the plan line, one toggle row per lift (the lift and the runs it starts are one group) with its band counts, its cat tracks,
 *      its way home and how its line builds, then "Other runs" (the group no lift starts). PlanRows draws it, only while a plan shows.
 *    - The tree filler (on / off, density, tree line, species group chips and the single-species stepper, Plant / Undo trees).
 * A build runs a few runs per frame, and Plant plants a slice of trees per apply. The progress lines ("Building runs 4 / 11",
 * "Planting trees 2,400 / 9,000") show while either is in flight. Stop sits beside Undo (building) or in place of Plant (planting).
 * The plan sheet's lifts block (R1-d, contract 41, LiftSheetBlock) shows the lifts the planner proposed with their build state, a Build button
 * per lift, Build lifts / Stop, the lifts total and Clear list. It reads `quickRuns.sheet`, so it stays after the run build and with the tool
 * closed (RunsView mounts LiftSheetRows then). Every action there is quickLiftsAct(action, key) and C# refuses a call it cannot take with a notice.
 * Contract 42: a Built row whose line has a tower over its sheave-train limit reads in the warning style with one fact line (tower, bend,
 * limit) and the tower's distance in its tooltip. A Proposed row and a lift row of the preview say how the line builds: with one tower over its
 * limit (flagged), or waiting for a hand fit in the lift tool.
 * Every knob is a button here, so nothing needs a hotkey.
 * It reads only the non-CORE quickRuns binding (ok panel only). Every action is quickRunsSet(key, value) and C# clamps it. Each Button sits
 * in its own Tip (one focusable child per Tooltip FocusBoundary, TreeControls.tsx).
 * Engine notes: EngineNotes/QuickRunsControls.tsx.md
 */
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { GLADE_LEVEL_MAX, GLADE_LEVEL_MIN, clampGladeLevel, hubTriggers, quickRuns$ } from "../bindings";
import { BAND } from "../bindings";
import type { QuickLiftsAction, QuickRunsKey, QuickRunsLift, QuickRunsPod, QuickRunsSheetRow, QuickRunsState } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry, Translate } from "../l10n";
import { rejectSummary } from "./hubRules";
import { HUB_FMT, HUB_LOC, LIFT_TYPE, QUICK_REJECT_TEXT, QUICK_SITE_TEXT, gladeLevelEntry, sentKeyEntry } from "./hubText";
import { BandMarker, CheckBox, LocText, Tip, TipLines, cx, numArg } from "./parts";
import styles from "./hub.module.scss";

/* QuickRunsRules.TreeLineStepM, and the coarse step. */
const TREE_LINE_STEP = 10;
const TREE_LINE_STEP_COARSE = 50;

const DENSITY: readonly LocEntry[] = [HUB_LOC.quickDensity0, HUB_LOC.quickDensity1, HUB_LOC.quickDensity2];
/* AutoKinds bits as feature chips (run types R3): Groomed (1) is always on. Snowmaking 4, Night 8, Natural 2. */
const KINDS: ReadonlyArray<[number, LocEntry, LocEntry]> = [
  [4, HUB_LOC.quickKindSnowmaking, HUB_LOC.quickKindSnowmakingTip], [8, HUB_LOC.quickKindNight, HUB_LOC.quickKindNightTip],
  [2, HUB_LOC.quickKindNatural, HUB_LOC.quickKindNaturalTip],
];
const GROUPS: readonly LocEntry[] = [HUB_LOC.quickGroup0, HUB_LOC.quickGroup1, HUB_LOC.quickGroup2, HUB_LOC.quickGroup3];

/* A vanilla tree species: the game's own name key for the prefab (vanilla ships one per tree). The raw prefab name is the fallback
   (C# sends the raw names). */
const treeNameEntry = (prefab: string): LocEntry => ["Assets.NAME[" + prefab + "]", prefab];

const set = (key: QuickRunsKey, value = 0): void => hubTriggers.quickRunsSet(key, value);
const liftsAct = (action: QuickLiftsAction, key = 0): void => hubTriggers.quickLiftsAct(action, key);

/* C# QuickRunsToolSystem.ClusterKey's pod keys (the rest is QUICK_CLUSTER). */
const POD_BEGINNER = "SkiResort.Tooltip.QUICK_POD[Beginner]";
const POD_MID = "SkiResort.Tooltip.QUICK_POD[MidStation]";
/* The reason a plan sheet row falls back to when C# sends none (the text is in SENT_KEYS). */
const LIFT_WHY_GAME = "SkiResort.Tooltip.QUICK_LIFT_WHY[GAME]";

/* A build in flight (QuickRunsToolSystem.Building): the tool builds runs a chunk at a time until the build is done or stopped. */
function building(q: QuickRunsState): boolean {
  return q.phase === "Emitted" || q.phase === "Retry" || q.phase === "Applying";
}

/* A tree job in flight: planting, or Undo trees (planting false with a progress total). */
function treeJob(q: QuickRunsState): boolean {
  return q.planting || q.treesTotal > 0;
}

/* C# is busy (a scan, a build or a tree job in flight): every knob waits. Only Stop answers. */
function busy(q: QuickRunsState): boolean {
  return q.phase === "Scanning" || building(q) || treeJob(q);
}

/* The Build strip's Runs-tab chip: "Resort Planner", grayed with the reason while unavailable. */
export function QuickRunsChip({ t }: { t: Translate }): JSX.Element {
  const q = useValue(quickRuns$);
  const available = !!q?.available;
  const active = !!q?.active;
  const tip = active ? null
    : !available ? t(sentKeyEntry(q?.reason, HUB_LOC.quickUnavailable))
      : t(HUB_LOC.quickToolTip);
  return (
    <div className={styles.pickerCell}>
      <Tip tip={tip}>
        <Button
          focusKey={FOCUS_DISABLED}
          disabled={!available && !active}
          theme={{ button: cx(styles.pickerChip, active && styles.pickerChipOn) }}
          selected={active}
          onSelect={() => hubTriggers.quickRunsToggle()}
        >
          <div className={styles.pickerLabel}>{t(HUB_LOC.quickTool)}</div>
        </Button>
      </Tip>
    </div>
  );
}

export function Act({ label, tip, on, disabled, onSelect, next }: {
  label: string; tip?: string; on?: boolean; disabled?: boolean; onSelect: () => void; next?: boolean;
}): JSX.Element {
  return (
    <Tip tip={tip}>
      <Button
        focusKey={FOCUS_DISABLED}
        variant="flat"
        className={cx(styles.segButton, next && styles.segButtonNext, on && styles.buttonOn)}
        disabled={!!disabled}
        selected={!!on}
        onSelect={onSelect}
      >
        {label}
      </Button>
    </Tip>
  );
}

export function Caption({ text }: { text: string }): JSX.Element {
  return <div className={styles.quickCaption}>{text}</div>;
}

/* The step's line: what the tool is doing and the numbers of the region or the preview. */
function StepLines({ q, t }: { q: QuickRunsState; t: Translate }): JSX.Element {
  const lines: JSX.Element[] = [];
  /* The site verdict: one plain line beside the mix line (Preview) or under the region line (Region step). There is none while the grade is unknown. */
  const siteEntry = q.siteTier >= 1 && q.siteTier <= QUICK_SITE_TEXT.length ? QUICK_SITE_TEXT[q.siteTier - 1] : null;
  const site = siteEntry ? <LocText key="site" entry={siteEntry} args={{ N: numArg(q.sitePercent, Unit.Integer) }} /> : null;
  if (q.phase === "Scanning") {
    lines.push(<LocText key="scan" entry={HUB_FMT.quickScanning} args={{ P: numArg(Math.round(q.progress * 100), Unit.Percentage) }} />);
  } else if (q.phase === "Preview") {
    lines.push(<LocText key="totals" entry={HUB_FMT.quickTotals} args={{
      N: numArg(q.enabled, Unit.Integer), M: numArg(q.runs, Unit.Integer), T: numArg(q.tracks, Unit.Integer), FEE: numArg(q.fee, Unit.Money),
      SKIERS: numArg(q.skiers, Unit.Integer),
    }} />);
    const c = (i: number): JSX.Element | string => numArg(q.count[i] ?? 0, Unit.Integer);
    const g = (i: number): JSX.Element | string => numArg(q.target[i] ?? 0, Unit.Integer);
    /* The runs-first planner gives no target (all zeros): the line then prints the counts alone, never "Green 5 of 0" */
    const hasTarget = (q.target[1] ?? 0) + (q.target[2] ?? 0) + (q.target[3] ?? 0) + (q.target[4] ?? 0) > 0;
    lines.push(hasTarget
      ? <LocText key="mix" entry={HUB_FMT.quickMixLine} args={{ G: c(1), B: c(2), K: c(3), D: c(4), TG: g(1), TB: g(2), TK: g(3), TD: g(4) }} />
      : <LocText key="mix" entry={HUB_FMT.quickMixCounts} args={{ G: c(1), B: c(2), K: c(3), D: c(4) }} />);
    if (site) lines.push(site);
    if (q.plan.sitePods > 0)
      lines.push(<LocText key="siteDetail" entry={HUB_FMT.quickSiteDetail} args={{
        G: numArg(q.plan.siteGreenHome, Unit.Integer), PODS: numArg(q.plan.sitePods, Unit.Integer), HA: numArg(q.plan.siteBenchHa, Unit.FloatSingleFraction),
      }} />);
    lines.push(<>{t(HUB_LOC.quickPreviewHint)}</>);
  } else if (building(q)) {
    if (q.buildTotal > 0)
      lines.push(<LocText key="build" entry={HUB_FMT.quickBuildProgress} args={{ N: numArg(q.buildDone, Unit.Integer), M: numArg(q.buildTotal, Unit.Integer) }} />);
    lines.push(<>{t(HUB_LOC.quickBuilding)}</>);
  } else if (q.corners < 3) {
    lines.push(<>{t(HUB_LOC.quickStart)}</>);
    if (q.corners === 0 && q.canPlanAround) lines.push(<>{t(HUB_LOC.quickAroundLift)}</>);
  } else {
    lines.push(<LocText key="region" entry={HUB_FMT.quickRegion} args={{
      N: numArg(q.corners, Unit.Integer), AREA: numArg(q.areaM2, Unit.Area), MAX: numArg(q.maxAreaM2, Unit.Area), V: numArg(q.verticalM, Unit.Length),
    }} />);
    if (site) lines.push(site);
  }
  const dropped = q.phase === "Preview" || (q.phase === "Region" && !!q.status) ? rejectSummary(q.rejects) : null;
  if (dropped && dropped.top.length > 0) {
    const list = dropped.top.map((r) => String(r.n) + " " + t(QUICK_REJECT_TEXT[r.bucket])).join(", ");
    lines.push(<LocText key="rejects" entry={HUB_FMT.quickRejects} args={{ N: numArg(dropped.total, Unit.Integer), LIST: list }} />);
  }
  if (q.phase === "Region" && q.batchRuns > 0)
    lines.push(<LocText key="batch" entry={HUB_FMT.quickBatch} args={{ N: numArg(q.batchRuns, Unit.Integer) }} />);
  return (
    <>
      {lines.map((l, i) => <div key={i} className={styles.quickLine}>{l}</div>)}
      {q.phase === "Region" && q.regionState === "Over" && <div className={cx(styles.quickLine, styles.warnText)}>{t(HUB_LOC.quickRegionOver)}</div>}
      {q.status && <div className={cx(styles.quickLine, styles.warnText)}>{t(sentKeyEntry(q.status, HUB_LOC.quickUnavailable))}</div>}
      {q.notice && <div className={styles.quickLine}>{t(sentKeyEntry(q.notice, HUB_LOC.quickUnavailable))}</div>}
      {q.warnings.filter((w) => w !== q.status).map((w) => <div key={w} className={cx(styles.quickLine, styles.warnText)}>{t(sentKeyEntry(w, HUB_LOC.quickUnavailable))}</div>)}
      {q.pods.length === 0 && q.clusters.map((c, i) => (
        <div key={"c" + i} className={styles.quickLine}>
          {c.label === POD_BEGINNER ? <LocText entry={HUB_FMT.quickPodBeginner} args={{ N: numArg(c.runs, Unit.Integer) }} />
            : c.label === POD_MID ? <LocText entry={HUB_FMT.quickPodMid} args={{ N: numArg(c.runs, Unit.Integer) }} />
              : <LocText entry={HUB_FMT.quickCluster} args={{ N: numArg(c.runs, Unit.Integer) }} />}
        </div>
      ))}
    </>
  );
}

/* The enabled runs of a pod by color: green, blue, black (double black counts with black) as the map's markers with a number. */
function BandCounts({ count }: { count: readonly number[] }): JSX.Element {
  const items: Array<[number, number]> = [[BAND.Green, count[BAND.Green] ?? 0], [BAND.Blue, count[BAND.Blue] ?? 0],
    [BAND.Black, (count[BAND.Black] ?? 0) + (count[BAND.DoubleBlack] ?? 0)]];
  return (
    <>
      {items.map(([band, n]) => (
        <span key={band} className={styles.planMixItem}>
          <BandMarker band={band} />
          {numArg(n, Unit.Integer)}
        </span>
      ))}
    </>
  );
}

/* The cat tracks of a group, beside its band counts (they are no green run: the counts above are pistes only). */
function TrackCount({ n }: { n: number }): JSX.Element {
  return <span className={styles.planMixItem}><LocText entry={HUB_FMT.quickTracksN} args={{ N: numArg(n, Unit.Integer) }} /></span>;
}

/* A toggle row: a check box and two lines, and a third in the warning style while the row has something to warn about (how its line builds).
   The whole row is the button (Tip around Button, one focusable child per Tooltip). */
function PlanRow({ on, tip, wait, onSelect, first, second, third }: {
  on: boolean; tip: string; wait: boolean; onSelect: () => void; first: JSX.Element; second: JSX.Element; third?: JSX.Element | null;
}): JSX.Element {
  return (
    <Tip tip={tip}>
      <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.planRow, !on && styles.planRowOff) }} disabled={wait} selected={on} onSelect={onSelect}>
        <CheckBox on={on} />
        <div className={styles.planRowText}>
          <div>{first}</div>
          <div className={styles.planRowSub}>{second}</div>
          {third && <div className={cx(styles.planRowSub, styles.warnText)}>{third}</div>}
        </div>
      </Button>
    </Tip>
  );
}

function verdictText(key: string, t: Translate): string {
  return key ? t(sentKeyEntry(key, HUB_LOC.quickUnavailable)) : "";
}

function LiftPlanRow({ l, pod, t, wait }: { l: QuickRunsLift; pod: QuickRunsPod | undefined; t: Translate; wait: boolean }): JSX.Element {
  const type = LIFT_TYPE[l.typeKey];
  const id = l.origin === 1 ? t(HUB_LOC.quickLiftIdPinned) : "P" + String(l.ordinal);
  const first = (
    <LocText entry={HUB_FMT.quickLiftLine} args={{
      ID: id, TYPE: type ? t(type) : l.typeKey, LENGTH: numArg(l.lengthM / 1000, Unit.FloatSingleFraction),
      RISE: numArg(Math.round(l.riseM), Unit.Integer), RUNS: numArg(l.serves, Unit.Integer),
    }} />
  );
  const second = (
    <>
      <span className={styles.planMixItem}>{verdictText(l.homeKey, t)}</span>
      {pod && <BandCounts count={pod.count} />}
      {pod && pod.tracks > 0 && <TrackCount n={pod.tracks} />}
    </>
  );
  /* How the line builds (a planned line, not a pinned lift): one tower over its limit still builds and flags the row, else it waits for the player */
  const third = l.origin === 1 ? null : l.flagged ? <>{t(HUB_LOC.quickLiftFlagged)}</> : l.waits ? <>{t(HUB_LOC.quickLiftWaits)}</> : null;
  return <PlanRow on={l.on} tip={t(HUB_LOC.quickLiftTip)} wait={wait} onSelect={() => set("lift", l.key)} first={first} second={second} third={third} />;
}

/* "Other runs": the group no lift starts (links between lifts, ways home, runs without a lift). */
function OtherRunsRow({ p, t, wait }: { p: QuickRunsPod; t: Translate; wait: boolean }): JSX.Element {
  const first = <LocText entry={HUB_FMT.quickOtherLine} args={{ RUNS: numArg(p.runs, Unit.Integer), TRACKS: numArg(p.tracks, Unit.Integer) }} />;
  return <PlanRow on={p.on} tip={t(HUB_LOC.quickPodTip)} wait={wait} onSelect={() => set("pod", p.key)} first={first} second={<BandCounts count={p.count} />} />;
}

/* The plan, top to bottom: the plan line, the lifts total (R1-d), the honest mix, the proposal note. Then one row per lift (with the band counts and
   cat tracks of the group it starts, and its way home). Then "Other runs". Up to 15 lift rows: the list scrolls in its own box. Nothing is drawn without a plan. */
function PlanRows({ q, t, wait }: { q: QuickRunsState; t: Translate; wait: boolean }): JSX.Element | null {
  if (q.phase !== "Preview" || (q.pods.length === 0 && q.planLifts.length === 0)) return null;
  const p = q.plan;
  const mix = p.mountainMix;
  const mixSum = (mix[1] ?? 0) + (mix[2] ?? 0) + (mix[3] ?? 0);
  return (
    <>
      <div className={styles.quickLine}>
        <LocText entry={HUB_FMT.quickPlanLine} args={{
          LIFTS: numArg(p.liftsProposed, Unit.Integer), KM: numArg(p.runKm, Unit.FloatSingleFraction), TKM: numArg(p.trackKm, Unit.FloatSingleFraction),
        }} />
      </div>
      <LiftsTotalLine q={q} />
      {mixSum > 0 && (
        <div className={styles.quickLine}>
          <LocText entry={HUB_FMT.quickMixHonest} args={{
            G: numArg(mix[1] ?? 0, Unit.Integer), B: numArg(mix[2] ?? 0, Unit.Integer), K: numArg(mix[3] ?? 0, Unit.Integer),
          }} />
        </div>
      )}
      {p.liftsProposed > 0 && <div className={styles.quickLine}>{t(HUB_LOC.quickProposalNote)}</div>}
      {q.planLifts.map((l) => (
        <LiftPlanRow key={"l" + l.id} l={l} pod={q.pods.find((x) => x.id === l.pod)} t={t} wait={wait} />
      ))}
      {q.pods.filter((x) => x.liftId < 0).map((x) => <OtherRunsRow key={"p" + x.id} p={x} t={t} wait={wait} />)}
    </>
  );
}

function TreeRowsQuick({ q, t, wait }: { q: QuickRunsState; t: Translate; wait: boolean }): JSX.Element {
  const level = clampGladeLevel(q.treeLevel);
  const none = q.treeLine < 0 || q.treeLine > q.groundMax;
  const line = none ? q.groundMax + TREE_LINE_STEP : q.treeLine;
  const nSpecies = q.speciesNames.length;
  const speciesName = q.species >= 0 && q.species < nSpecies ? t(treeNameEntry(q.speciesNames[q.species])) : t(HUB_LOC.quickSpeciesGroup);
  const step = (d: number): void => set("species", q.species + d < -1 ? nSpecies - 1 : q.species + d >= nSpecies ? -1 : q.species + d);
  return (
    <>
      <div className={styles.quickRow}>
        <Caption text={t(HUB_LOC.quickTreesCaption)} />
        <Act label={t(HUB_LOC.quickTrees)} tip={t(HUB_LOC.quickTreesTip)} on={q.trees} disabled={wait} onSelect={() => set("trees", q.trees ? 0 : 1)} />
        {q.trees && (
          <>
            {treeJob(q)
              ? <Act label={t(HUB_LOC.quickStop)} tip={t(HUB_LOC.quickStopTip)} next onSelect={() => set("stop")} />
              : <Act label={t(HUB_LOC.quickPlant)} tip={t(HUB_LOC.quickPlantTip)} next disabled={wait || (q.plant <= 0 && q.remove <= 0)} onSelect={() => set("plant")} />}
            <Act label={t(HUB_LOC.quickTreesUndo)} tip={t(HUB_LOC.quickTreesUndoTip)} next disabled={wait || q.treesUndo <= 0} onSelect={() => set("treesUndo")} />
          </>
        )}
      </div>
      {q.trees && (
        <>
          <div className={styles.quickRow}>
            <Caption text={t(HUB_LOC.quickTreeDensity)} />
            <Act label="–" disabled={wait || level <= GLADE_LEVEL_MIN} onSelect={() => set("level", level - 1)} />
            <div className={styles.stepValue}>{`${level} · ${t(gladeLevelEntry(level))}`}</div>
            <Act label="+" disabled={wait || level >= GLADE_LEVEL_MAX} onSelect={() => set("level", level + 1)} />
          </div>
          <div className={styles.quickRow}>
            <Caption text={t(HUB_LOC.quickTreeLine)} />
            <Act label="–50" tip={t(HUB_LOC.quickLower)} disabled={wait} onSelect={() => set("treeLine", line - TREE_LINE_STEP_COARSE)} />
            <Act label="–10" tip={t(HUB_LOC.quickLower)} next disabled={wait} onSelect={() => set("treeLine", line - TREE_LINE_STEP)} />
            <Tip tip={t(HUB_LOC.quickTreeLineTip)}>
              <div className={styles.stepValue}>{none ? t(HUB_LOC.quickTreeLineNone) : numArg(Math.round(q.treeLine), Unit.Length)}</div>
            </Tip>
            <Act label="+10" tip={t(HUB_LOC.quickHigher)} disabled={wait || none} onSelect={() => set("treeLine", line + TREE_LINE_STEP)} />
            <Act label="+50" tip={t(HUB_LOC.quickHigher)} next disabled={wait || none} onSelect={() => set("treeLine", line + TREE_LINE_STEP_COARSE)} />
          </div>
          <div className={styles.quickRow}>
            <Caption text={t(HUB_LOC.quickSpeciesCaption)} />
            {GROUPS.map((e, i) => (
              <Act key={i} label={t(e)} next={i > 0} on={q.species < 0 && q.group === i} disabled={wait}
                onSelect={() => set("group", i)} />
            ))}
          </div>
          <div className={styles.quickRow}>
            <Caption text="" />
            <Act label="←" tip={t(HUB_LOC.quickSpeciesTip)} disabled={wait || nSpecies === 0} onSelect={() => step(-1)} />
            <div className={cx(styles.stepValue, styles.quickSpecies)}>{speciesName}</div>
            <Act label="→" tip={t(HUB_LOC.quickSpeciesTip)} disabled={wait || nSpecies === 0} onSelect={() => step(1)} />
          </div>
          <div className={styles.quickLine}>
            {q.planting && q.treesTotal > 0
              ? <LocText entry={HUB_FMT.quickPlantProgress} args={{ N: numArg(q.treesDone, Unit.Integer), M: numArg(q.treesTotal, Unit.Integer) }} />
              : treeJob(q)
              ? <LocText entry={HUB_FMT.quickRemoveProgress} args={{ N: numArg(q.treesDone, Unit.Integer), M: numArg(q.treesTotal, Unit.Integer) }} />
              : q.treesCost > 0
              ? <LocText entry={HUB_FMT.quickTreeCountsCost} args={{ P: numArg(q.plant, Unit.Integer), R: numArg(q.remove, Unit.Integer), FEE: numArg(q.treesCost, Unit.Money) }} />
              : <LocText entry={HUB_FMT.quickTreeCounts} args={{ P: numArg(q.plant, Unit.Integer), R: numArg(q.remove, Unit.Integer) }} />}
          </div>
        </>
      )}
    </>
  );
}

/* ---- the plan sheet's lifts block (contract 41) ---------------------------------------------------------------------------------------- */

/* The planner has a scan, a preview or a build in progress (C# ResortLiftBuildSystem.HubAct refuses the lift calls then too): building a lift
   would take the tool from the planner and drop its preview. */
function plannerBusy(q: QuickRunsState): boolean {
  return q.phase !== "Region" || q.planting;
}

/* The block shows when the sheet has rows, except while the planner scans or previews: a preview has its own lift rows and a new scan replaces
   the sheet. The lifts total still shows in the preview (LiftsTotalLine, PlanRows). */
export function sheetShows(q: QuickRunsState | undefined): boolean {
  return !!q && q.sheet.rows.length > 0 && q.phase !== "Preview" && q.phase !== "Scanning";
}

/* The lifts total, apart from the runs total: the lifts switched on and not built out of those not built, and what they cost. */
function LiftsTotalLine({ q }: { q: QuickRunsState }): JSX.Element | null {
  const s = q.sheet;
  if (s.unbuilt > 0)
    return (
      <div className={styles.quickLine}>
        <LocText entry={HUB_FMT.quickLiftsTotal} args={{ N: numArg(s.toBuild, Unit.Integer), M: numArg(s.unbuilt, Unit.Integer), COST: numArg(s.cost, Unit.Money) }} />
      </div>
    );
  if (s.built > 0)
    return <div className={styles.quickLine}><LocText entry={HUB_FMT.quickLiftsAllBuilt} args={{ N: numArg(s.built, Unit.Integer) }} /></div>;
  return null;
}

/* A row's state as a line, and whether it is a caution (a refusal, a lift waiting for the player, or a built line with a flagged tower). */
function sheetState(r: QuickRunsSheetRow, t: Translate): { text: JSX.Element | string; warn: boolean } {
  const why = (): string => t(sentKeyEntry(r.reasonKey || LIFT_WHY_GAME, HUB_LOC.quickUnavailable));
  /* Task 2c-l: the over-limit tower's numbers (a flagged or waiting row, before the build from the plan, after it from the queue) */
  const towerArgs = () => ({
    TOWER: numArg(r.overLimitTower, Unit.Integer), BEND: numArg(Math.round(r.overLimitBendDeg), Unit.Integer), CAP: numArg(Math.round(r.overLimitCapDeg), Unit.Integer),
  });
  if (r.step === "building") return { text: t(HUB_LOC.quickLiftBuilding), warn: false };
  if (r.step === "queued") return { text: t(HUB_LOC.quickLiftQueued), warn: false };
  switch (r.state) {
    case "Built":
      if (r.overLimitTowers > 0)
        return {
          text: <LocText entry={HUB_FMT.quickLiftBuiltFlagged} args={{
            TOWER: numArg(r.overLimitTower, Unit.Integer), BEND: numArg(Math.round(r.overLimitBendDeg), Unit.Integer), CAP: numArg(Math.round(r.overLimitCapDeg), Unit.Integer),
          }} />,
          warn: true,
        };
      return { text: t(HUB_LOC.quickLiftBuilt), warn: false };
    case "Refused": return { text: <LocText entry={HUB_FMT.quickLiftRefused} args={{ WHY: why() }} />, warn: true };
    case "NeedsFitting":
      if (r.overLimitTowers > 0 && r.reasonKey.endsWith("[PLAN_TRAIN]")) return { text: <LocText entry={HUB_FMT.quickLiftFittingAt} args={towerArgs()} />, warn: true };
      return { text: <LocText entry={HUB_FMT.quickLiftFitting} args={{ WHY: why() }} />, warn: true };
    case "StationsPlaced":
      return r.reasonKey ? { text: <LocText entry={HUB_FMT.quickLiftPlacedWhy} args={{ WHY: why() }} />, warn: true }
        : { text: t(HUB_LOC.quickLiftPlaced), warn: false };
    default:
      /* Not built yet: say how the line will build when the plan says so. A lift switched off says only that */
      /* Task 2c-l: the reason and numbers before the build, the sentence the row shows after it */
      if (r.on && r.flagged)
        return r.overLimitTowers > 0 ? { text: <LocText entry={HUB_FMT.quickLiftFlaggedAt} args={towerArgs()} />, warn: true }
          : { text: t(HUB_LOC.quickLiftFlagged), warn: true };
      if (r.on && r.waits) {
        if (r.overLimitTowers > 0 && r.reasonKey.endsWith("[PLAN_TRAIN]")) return { text: <LocText entry={HUB_FMT.quickLiftFittingAt} args={towerArgs()} />, warn: true };
        if (r.reasonKey) return { text: <LocText entry={HUB_FMT.quickLiftFitting} args={{ WHY: why() }} />, warn: true };
        return { text: t(HUB_LOC.quickLiftWaits), warn: true };
      }
      return { text: t(r.on ? HUB_LOC.quickLiftProposed : HUB_LOC.quickLiftOff), warn: false };
  }
}

/* One lift of the sheet: the lift line with its cost, its state (and the reason), and its Build button while it can be built. A lift left out
   of the plan dims. The row is not a toggle: the plan's switches are in the preview (PlanRows). */
function SheetRow({ r, t, blocked }: { r: QuickRunsSheetRow; t: Translate; blocked: boolean }): JSX.Element {
  const type = LIFT_TYPE[r.typeKey];
  const state = sheetState(r, t);
  const canBuild = r.state !== "Built" && !r.step;
  const sub = <div className={cx(styles.planRowSub, state.warn && styles.warnText)}>{state.text}</div>;
  /* A flagged built line says where its tower stands (one text node: a <LocText>) */
  const flagTip = r.state === "Built" && r.overLimitTowers > 0 && !r.step
    ? <TipLines lines={[<LocText entry={HUB_FMT.quickLiftFlagTip} args={{ TOWER: numArg(r.overLimitTower, Unit.Integer), ALONG: numArg(Math.round(r.overLimitAlongM), Unit.Integer) }} />]} />
    : undefined;
  return (
    <div className={cx(styles.sheetRow, !r.on && r.state !== "Built" && styles.planRowOff)}>
      <div className={styles.planRowText}>
        <div>
          <LocText entry={HUB_FMT.quickSheetLine} args={{
            ID: r.name, TYPE: type ? t(type) : r.typeKey, LENGTH: numArg(r.lengthM / 1000, Unit.FloatSingleFraction),
            RISE: numArg(Math.round(r.riseM), Unit.Integer), COST: numArg(r.cost, Unit.Money),
          }} />
        </div>
        {flagTip ? <Tip tip={flagTip}>{sub}</Tip> : sub}
      </div>
      {canBuild && <Act label={t(HUB_LOC.quickLiftBuild)} tip={t(HUB_LOC.quickLiftBuildTip)} next disabled={blocked} onSelect={() => liftsAct("build", r.key)} />}
    </div>
  );
}

/* The block: Build lifts (Stop while the queue runs) and Clear list, the progress, the notice of a refused call, the lifts total, the rows. */
function LiftSheetBlock({ q, t }: { q: QuickRunsState; t: Translate }): JSX.Element | null {
  if (!sheetShows(q)) return null;
  const s = q.sheet;
  const blocked = plannerBusy(q);
  return (
    <>
      <div className={styles.quickRow}>
        {s.running
          ? <Act label={t(HUB_LOC.quickStop)} tip={t(HUB_LOC.quickLiftsStopTip)} disabled={s.stopping} onSelect={() => liftsAct("stop")} />
          : <Act label={t(HUB_LOC.quickLiftsBuild)} tip={t(HUB_LOC.quickLiftsBuildTip)} disabled={blocked || s.toBuild <= 0} onSelect={() => liftsAct("buildAll")} />}
        <Act label={t(HUB_LOC.quickLiftsClear)} tip={t(HUB_LOC.quickLiftsClearTip)} next disabled={blocked || s.running} onSelect={() => liftsAct("clear")} />
      </div>
      {s.running && s.total > 0 && (
        <div className={styles.quickLine}>
          {s.stopping
            ? <LocText entry={HUB_FMT.quickStoppingLift} args={{ N: numArg(s.index, Unit.Integer), M: numArg(s.total, Unit.Integer) }} />
            : <LocText entry={HUB_FMT.quickBuildingLift} args={{ N: numArg(s.index, Unit.Integer), M: numArg(s.total, Unit.Integer) }} />}
        </div>
      )}
      {s.notice && <div className={styles.quickLine}>{t(sentKeyEntry(s.notice, HUB_LOC.quickUnavailable))}</div>}
      <LiftsTotalLine q={q} />
      <div className={styles.sheetList}>
        {s.rows.map((r) => <SheetRow key={"s" + r.key} r={r} t={t} blocked={blocked} />)}
      </div>
    </>
  );
}

/* The block with the planner closed (RunsView draws it at the top of the Runs view when sheetShows). */
export function LiftSheetRows(): JSX.Element | null {
  const t = useT();
  const q = useValue(quickRuns$);
  return <LiftSheetBlock q={q} t={t} />;
}

/* The Runs view's top rows while the Quick runs tool is open (RunsView draws TreeRows otherwise). */
export function QuickRunsRows(): JSX.Element {
  const t = useT();
  const q = useValue(quickRuns$);
  const wait = busy(q);
  const preview = q.phase === "Preview";
  return (
    <>
      <div className={styles.quickRow}>
        <Button focusKey={FOCUS_DISABLED} variant="flat" className={cx(styles.treeToolButton, styles.buttonOn)} selected onSelect={() => hubTriggers.quickRunsToggle()}>
          {t(HUB_LOC.quickTool)}
        </Button>
        <div className={styles.treeRowSpacer} />
        <Act label={t(HUB_LOC.quickScan)} tip={t(HUB_LOC.quickScanTip)} disabled={wait || q.corners < 3 || q.regionState === "Over"} onSelect={() => set("scan")} />
        <Act label={t(HUB_LOC.quickBuild)} tip={t(HUB_LOC.quickBuildTip)} next disabled={!preview || q.enabled <= 0} onSelect={() => set("build")} />
        <Act label={t(HUB_LOC.quickUndo)} tip={t(HUB_LOC.quickUndoTip)} next disabled={wait || q.batchRuns <= 0} onSelect={() => set("undo")} />
        {building(q) && <Act label={t(HUB_LOC.quickStop)} tip={t(HUB_LOC.quickStopTip)} next onSelect={() => set("stop")} />}
      </div>
      <StepLines q={q} t={t} />
      <div className={styles.quickRow}>
        <Caption text={t(HUB_LOC.quickDensityCaption)} />
        {DENSITY.map((e, i) => (
          <Act key={i} label={t(e)} next={i > 0} on={q.density === i} disabled={wait} onSelect={() => set("density", i)} />
        ))}
      </div>
      <div className={styles.quickRow}>
        <Caption text={t(HUB_LOC.quickKindsCaption)} />
        {KINDS.map(([bit, e, tip], i) => (
          <Act key={bit} label={t(e)} tip={t(tip)} next={i > 0} on={(q.kinds & bit) !== 0} disabled={wait} onSelect={() => set("kind", bit)} />
        ))}
      </div>
      <TreeRowsQuick q={q} t={t} wait={wait} />
      <LiftSheetBlock q={q} t={t} />
      <div className={styles.planList}>
        <PlanRows q={q} t={t} wait={wait} />
      </div>
    </>
  );
}
