/*
 * src/hub/QuickRunsControls.tsx - the Quick runs tool's controls (experimental; C# QuickRunsToolSystem): the Build strip's Runs-tab chip
 * (QuickRunsChip, the lift generator chip's pattern: grayed with the reason while unavailable) and, while the tool is open, the rows at the
 * top of the Runs view in place of the tree tool's (QuickRunsRows): Scan / Build / Undo, the Mix / Runs / Kinds knobs and Shuffle, the
 * step's line (region size, scan progress, totals and mix), warnings and clusters, then the tree filler (on / off, density, tree line,
 * species group chips and the single-species stepper, Plant / Undo trees). A build runs a few runs per frame and Plant a slice of trees
 * per apply: the progress lines ("Building runs 4 / 11", "Planting trees 2,400 / 9,000") show while either is in flight, with Stop beside Undo
 * (building) or in place of Plant (planting).
 * Every knob is a button here, so nothing needs a hotkey.
 * Reads only the non-CORE quickRuns binding (ok panel only); every action is quickRunsSet(key, value) and C# clamps it. Each Button sits
 * in its own Tip (one focusable child per Tooltip FocusBoundary, TreeControls.tsx).
 * Engine notes: EngineNotes/QuickRunsControls.tsx.md
 */
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { GLADE_LEVEL_MAX, GLADE_LEVEL_MIN, clampGladeLevel, hubTriggers, quickRuns$ } from "../bindings";
import type { QuickRunsKey, QuickRunsState } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry, Translate } from "../l10n";
import { rejectSummary } from "./hubRules";
import { HUB_FMT, HUB_LOC, QUICK_REJECT_TEXT, gladeLevelEntry, sentKeyEntry } from "./hubText";
import { LocText, Tip, TipLines, cx, numArg } from "./parts";
import styles from "./hub.module.scss";

/* QuickRunsRules.TreeLineStepM, and the coarse step. */
const TREE_LINE_STEP = 10;
const TREE_LINE_STEP_COARSE = 50;

const MIX: readonly LocEntry[] = [HUB_LOC.quickMix0, HUB_LOC.quickMix1, HUB_LOC.quickMix2, HUB_LOC.quickMix3];
const DENSITY: readonly LocEntry[] = [HUB_LOC.quickDensity0, HUB_LOC.quickDensity1, HUB_LOC.quickDensity2];
/* AutoKinds bits. */
const KINDS: ReadonlyArray<[number, LocEntry]> = [
  [1, HUB_LOC.quickKindGroomed], [2, HUB_LOC.quickKindNatural], [4, HUB_LOC.quickKindSnowmaking], [8, HUB_LOC.quickKindNight],
];
const GROUPS: readonly LocEntry[] = [HUB_LOC.quickGroup0, HUB_LOC.quickGroup1, HUB_LOC.quickGroup2, HUB_LOC.quickGroup3];

/* A vanilla tree species: the game's own name key for the prefab (vanilla ships one per tree), the raw prefab name as the fallback
   (C# sends the raw names). */
const treeNameEntry = (prefab: string): LocEntry => ["Assets.NAME[" + prefab + "]", prefab];

const set = (key: QuickRunsKey, value = 0): void => hubTriggers.quickRunsSet(key, value);

/* C# QuickRunsToolSystem.ClusterKey's pod keys (the rest is QUICK_CLUSTER). */
const POD_BEGINNER = "SkiResort.Tooltip.QUICK_POD[Beginner]";
const POD_MID = "SkiResort.Tooltip.QUICK_POD[MidStation]";

/* A build in flight (QuickRunsToolSystem.Building): runs are built a chunk at a time until done or stopped. */
function building(q: QuickRunsState): boolean {
  return q.phase === "Emitted" || q.phase === "Retry" || q.phase === "Applying";
}

/* A tree job in flight: planting, or Undo trees (planting false with a progress total). */
function treeJob(q: QuickRunsState): boolean {
  return q.planting || q.treesTotal > 0;
}

/* C# is busy (a scan, a build or a tree job in flight): every knob waits; only Stop answers. */
function busy(q: QuickRunsState): boolean {
  return q.phase === "Scanning" || building(q) || treeJob(q);
}

/* The Build strip's Runs-tab chip: "Quick runs (experimental)", grayed with the reason while unavailable. */
export function QuickRunsChip({ t }: { t: Translate }): JSX.Element {
  const q = useValue(quickRuns$);
  const available = !!q?.available;
  const active = !!q?.active;
  const tip = active ? t(HUB_LOC.quickToolTipOn)
    : !available ? t(sentKeyEntry(q?.reason, HUB_LOC.quickUnavailable))
      : t(HUB_LOC.quickToolTip);
  return (
    <div className={styles.pickerCell}>
      <Tip tip={<TipLines lines={[t(HUB_LOC.quickTool), tip]} />}>
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

function Act({ label, tip, on, disabled, onSelect, next }: {
  label: string; tip: string; on?: boolean; disabled?: boolean; onSelect: () => void; next?: boolean;
}): JSX.Element {
  return (
    <Tip tip={tip}>
      <Button
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

function Caption({ text }: { text: string }): JSX.Element {
  return <div className={styles.quickCaption}>{text}</div>;
}

/* The step's line: what the tool is doing and the numbers of the region or the preview. */
function StepLines({ q, t }: { q: QuickRunsState; t: Translate }): JSX.Element {
  const lines: JSX.Element[] = [];
  if (q.phase === "Scanning") {
    lines.push(<LocText key="scan" entry={HUB_FMT.quickScanning} args={{ P: numArg(Math.round(q.progress * 100), Unit.Percentage) }} />);
  } else if (q.phase === "Preview") {
    lines.push(<LocText key="totals" entry={HUB_FMT.quickTotals} args={{
      N: numArg(q.enabled, Unit.Integer), M: numArg(q.runs, Unit.Integer), FEE: numArg(q.fee, Unit.Money), SKIERS: numArg(q.skiers, Unit.Integer),
    }} />);
    const c = (i: number): JSX.Element | string => numArg(q.count[i] ?? 0, Unit.Integer);
    const g = (i: number): JSX.Element | string => numArg(q.target[i] ?? 0, Unit.Integer);
    lines.push(<LocText key="mix" entry={HUB_FMT.quickMixLine} args={{ G: c(1), B: c(2), K: c(3), D: c(4), TG: g(1), TB: g(2), TK: g(3), TD: g(4) }} />);
    lines.push(<>{t(HUB_LOC.quickPreviewHint)}</>);
  } else if (building(q)) {
    if (q.buildTotal > 0)
      lines.push(<LocText key="build" entry={HUB_FMT.quickBuildProgress} args={{ N: numArg(q.buildDone, Unit.Integer), M: numArg(q.buildTotal, Unit.Integer) }} />);
    lines.push(<>{t(HUB_LOC.quickBuilding)}</>);
  } else if (q.corners < 3) {
    lines.push(<>{t(HUB_LOC.quickStart)}</>);
  } else {
    lines.push(<LocText key="region" entry={HUB_FMT.quickRegion} args={{
      N: numArg(q.corners, Unit.Integer), AREA: numArg(q.areaM2, Unit.Area), MAX: numArg(q.maxAreaM2, Unit.Area), V: numArg(q.verticalM, Unit.Length),
    }} />);
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
      {q.warnings.map((w) => <div key={w} className={cx(styles.quickLine, styles.warnText)}>{t(sentKeyEntry(w, HUB_LOC.quickUnavailable))}</div>)}
      {q.clusters.map((c, i) => (
        <div key={"c" + i} className={styles.quickLine}>
          {c.label === POD_BEGINNER ? <LocText entry={HUB_FMT.quickPodBeginner} args={{ N: numArg(c.runs, Unit.Integer) }} />
            : c.label === POD_MID ? <LocText entry={HUB_FMT.quickPodMid} args={{ N: numArg(c.runs, Unit.Integer) }} />
              : <LocText entry={HUB_FMT.quickCluster} args={{ N: numArg(c.runs, Unit.Integer) }} />}
        </div>
      ))}
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
              : <Act label={t(HUB_LOC.quickPlant)} tip={t(HUB_LOC.quickPlantTip)} next disabled={wait || q.plant <= 0} onSelect={() => set("plant")} />}
            <Act label={t(HUB_LOC.quickTreesUndo)} tip={t(HUB_LOC.quickTreesUndoTip)} next disabled={wait || q.treesUndo <= 0} onSelect={() => set("treesUndo")} />
          </>
        )}
      </div>
      {q.trees && (
        <>
          <div className={styles.quickRow}>
            <Caption text={t(HUB_LOC.quickTreeDensity)} />
            <Act label="–" tip={t(HUB_LOC.quickDensityTrees)} disabled={wait || level <= GLADE_LEVEL_MIN} onSelect={() => set("level", level - 1)} />
            <div className={styles.stepValue}>{`${level} · ${t(gladeLevelEntry(level))}`}</div>
            <Act label="+" tip={t(HUB_LOC.quickDensityTrees)} disabled={wait || level >= GLADE_LEVEL_MAX} onSelect={() => set("level", level + 1)} />
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
              <Act key={i} label={t(e)} tip={t(HUB_LOC.quickGroupTip)} next={i > 0} on={q.species < 0 && q.group === i} disabled={wait}
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

/* The Runs view's top rows while the Quick runs tool is open (RunsView draws TreeRows otherwise). */
export function QuickRunsRows(): JSX.Element {
  const t = useT();
  const q = useValue(quickRuns$);
  const wait = busy(q);
  const preview = q.phase === "Preview";
  return (
    <>
      <div className={styles.quickRow}>
        <Tip tip={t(HUB_LOC.quickToolTipOn)}>
          <Button variant="flat" className={cx(styles.treeToolButton, styles.buttonOn)} selected onSelect={() => hubTriggers.quickRunsToggle()}>
            {t(HUB_LOC.quickTool)}
          </Button>
        </Tip>
        <div className={styles.treeRowSpacer} />
        <Act label={t(HUB_LOC.quickScan)} tip={t(HUB_LOC.quickScanTip)} disabled={wait || q.corners < 3 || q.regionState === "Over"} onSelect={() => set("scan")} />
        <Act label={t(HUB_LOC.quickBuild)} tip={t(HUB_LOC.quickBuildTip)} next disabled={!preview || q.enabled <= 0} onSelect={() => set("build")} />
        <Act label={t(HUB_LOC.quickUndo)} tip={t(HUB_LOC.quickUndoTip)} next disabled={wait || q.batchRuns <= 0} onSelect={() => set("undo")} />
        {building(q) && <Act label={t(HUB_LOC.quickStop)} tip={t(HUB_LOC.quickStopTip)} next onSelect={() => set("stop")} />}
      </div>
      <StepLines q={q} t={t} />
      <div className={styles.quickRow}>
        <Caption text={t(HUB_LOC.quickMixCaption)} />
        {MIX.map((e, i) => (
          <Act key={i} label={t(e)} tip={t(HUB_LOC.quickMixTip)} next={i > 0} on={q.mix === i} disabled={wait} onSelect={() => set("mix", i)} />
        ))}
      </div>
      <div className={styles.quickRow}>
        <Caption text={t(HUB_LOC.quickDensityCaption)} />
        {DENSITY.map((e, i) => (
          <Act key={i} label={t(e)} tip={t(HUB_LOC.quickDensityTip)} next={i > 0} on={q.density === i} disabled={wait} onSelect={() => set("density", i)} />
        ))}
        <div className={styles.treeRowSpacer} />
        <Act label={t(HUB_LOC.quickShuffle)} tip={t(HUB_LOC.quickShuffleTip)} disabled={wait || q.corners < 3} onSelect={() => set("shuffle")} />
      </div>
      <div className={styles.quickRow}>
        <Caption text={t(HUB_LOC.quickKindsCaption)} />
        {KINDS.map(([bit, e], i) => (
          <Act key={bit} label={t(e)} tip={t(HUB_LOC.quickKindsTip)} next={i > 0} on={(q.kinds & bit) !== 0} disabled={wait} onSelect={() => set("kind", bit)} />
        ))}
      </div>
      <TreeRowsQuick q={q} t={t} wait={wait} />
    </>
  );
}
