/*
 * src/hub/OverviewView.tsx - the Overview (Attraction tile): the score receipt and the mountain.
 * The receipt multiplies nothing: every number is a ResortLive field of C#'s model Result. -1 or a missing live row renders "–" (numArg).
 * The total's tooltip spells out the equation "(runs + lifts [+ school]) × variety × vertical" (SkiResortScore.cs:468 S) and
 * HubRules.Terrain = K × sqrt(S). Operators sit in fixed cells ("√" is not on the Gameface font safe list). While `attractionHeld` the game
 * keeps the saved score, so the result row and the bar show "…". The view does not read ResortLive.luxuryIgnored (always 0, kept on the
 * wire for the contract).
 * Layers (DECISIONS 613): the view is the receipt's terms and the result. A term's breakdown is its fold. The formula is in the tooltip of
 * the number it explains. The score shows once here, in the result row (the tile above names it). The snow pair shows once, in the status line.
 * Fixed heights only, so a value change never changes layout. Panel file: subscribes resorts$, selectedResort$, resortLive$, runs$, problems$, city$.
 * Its MergedFold subscribes mergedLodges$ (contract 35).
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { BAND, PROBLEM, build$, city$, problems$, resortLive$, resorts$, runs$, sameEntity, selectedResort$ } from "../bindings";
import type { BuildItem, CityStats, ProblemRow, ResortLive, ResortRow, RunRow } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import {
  efficiencyAtFloor, factorRowsOf, hectares, liftsMissing, liveOf, lodgeExtraShort, receiptPartsVariant, resultVariant, runsMissing, schoolBuiltOf,
  schoolFlagOf, schoolMissingOf, schoolState, scoreBarParts, varietyMissing, verticalShort,
} from "./hubRules";
import type { VarietyNeed } from "./hubRules";
import { IconText, MissingLine } from "./iconText";
import { ASSET_NAME, BAND_NAME, HUB_FMT, HUB_LOC, factorEntry } from "./hubText";
import type { BuildItemId } from "../bindings";
import {
  BandMarker, Divider, FlexBar, InfoLine, LocText, Num, StableWidth, SubLine, Tip, TipLines, cx, depthValue, numArg, useInches,
} from "./parts";
import { currentResort, resortEntries, runsOf } from "./resortData";
import { Fold } from "./fit";
import { MergedFold } from "./MergedFold";
import styles from "./hub.module.scss";

const DASH = "–";


const BAND_SEGMENT = ["segFlat", "segGreen", "segBlue", "segBlack", "segDoubleBlack"] as const;

function bandCounts(row: ResortRow): number[] {
  return [row.flat | 0, row.green | 0, row.blue | 0, row.black | 0, row.doubleBlack | 0];
}

/** Runs of the shown area whose rating can never finish (band -2). */
function unrateableCount(runs: readonly RunRow[]): number {
  let n = 0;
  for (const r of runs) if (r.band === BAND.Unrateable) n++;
  return n;
}

/** "Being rated" = runs - rated - unrateable (never negative). */
function pendingCount(row: ResortRow, unrateable: number): number {
  return Math.max(0, (row.runs | 0) - (row.rated | 0) - unrateable);
}

/** One segment per non-zero band (flex-grow = count), then being rated, then cannot be rated.
 *  The tooltip sits on the bar's own root div (a Tip around this component would not attach). */
function DifficultyBar({ row, unrateable, tip }: { row: ResortRow; unrateable: number; tip: JSX.Element }): JSX.Element {
  const counts = bandCounts(row);
  const pending = pendingCount(row, unrateable);
  return (
    <Tip tip={tip}>
      <div className={styles.diffBar}>
        {counts.map((n, band) =>
          n > 0 ? <div key={band} className={cx(styles.diffSeg, styles[BAND_SEGMENT[band]])} style={{ flexGrow: n }} /> : null)}
        {pending > 0 && <div className={cx(styles.diffSeg, styles.segPending)} style={{ flexGrow: pending }} />}
        {unrateable > 0 && <div className={cx(styles.diffSeg, styles.segUnrateable)} style={{ flexGrow: unrateable }} />}
      </div>
    </Tip>
  );
}

/** Marker + count per band. A "being rated" item and a "cannot be rated" item show only while one exists.
 *  At count 0 the dimmed "being rated" ring looks exactly like Flat's. The legend has no label of its own: the MOUNTAIN header names the block. */
function DifficultyLegend({ row, unrateable, t }: { row: ResortRow; unrateable: number; t: Translate }): JSX.Element {
  const counts = bandCounts(row);
  const pending = pendingCount(row, unrateable);
  const item = (key: string, band: number, n: number, tip: string): JSX.Element => (
    <Tip key={key} tip={tip}>
      <div className={cx(styles.legendItem, n === 0 && styles.legendZero)}>
        <BandMarker band={band} />
        <StableWidth samples={["00"]} align="start" className={styles.legendCount}>
          <Num value={n} unit={Unit.Integer} />
        </StableWidth>
      </div>
    </Tip>
  );
  return (
    <div className={styles.legend}>
      {counts.map((n, band) => item(`b${band}`, band, n, t(BAND_NAME[band])))}
      {pending > 0 && item("pending", BAND.Pending, pending, t(HUB_LOC.pending))}
      {unrateable > 0 && item("unrateable", BAND.Unrateable, unrateable, t(HUB_LOC.unrateable))}
    </div>
  );
}

/** MOUNTAIN: difficulty bar + legend, vertical, skiable area, snow (sign and Not linked). */
function MountainSection({ row, live, city, unrateable, t, folded }: {
  row: ResortRow; live: ResortLive | null; city: CityStats; unrateable: number; t: Translate;
  /** Under the receipt the section is a fold (closed by default). Not linked shows it open, because it is the whole view there. */
  folded?: boolean;
}): JSX.Element {
  const pending = pendingCount(row, unrateable);
  const diffTip = (
    <TipLines
      lines={[
        t(HUB_LOC.difficulty),   // the counts are the legend's, right below
        pending > 0 && <LocText key="p" entry={HUB_FMT.diffTipPending} args={{ N: numArg(pending, Unit.Integer) }} />,
        unrateable > 0 && <LocText key="u" entry={HUB_FMT.diffTipUnrateable} args={{ N: numArg(unrateable, Unit.Integer) }} />,
      ]}
    />
  );
  // Snow is always two numbers: depth in cm and run coverage in %. The mark slot shows "≈" when estimated, and the tooltip says
  // measured or estimated. tools/test-hub-rules.js guards it.
  const snowPct = live ? live.snowPct : -1;
  const depthCm = live ? live.snowDepthCm : -1;
  const inches = useInches(); // depth in the player's units
  const snowMark = live && live.snowEstimated && (snowPct >= 0 || depthCm >= 0) ? "≈" : null;
  const how = live && live.snowEstimated && t(HUB_LOC.runEstimated);   // measured is the default: no line (T142)
  const snowTip = (head: string, body: string): JSX.Element => (
    <TipLines lines={[head, body, how, city.snowVisualsOff && t(HUB_LOC.statusTipVisuals)]} />
  );
  const vertical = row.verticalM >= 0 ? <Num value={row.verticalM} unit={Unit.Height} /> : DASH;
  const verticalMark = row.verticalM >= 0 && row.verticalApprox ? "≈" : null;
  const rows = (
    <>
      <DifficultyBar row={row} unrateable={unrateable} tip={diffTip} />
      <DifficultyLegend row={row} unrateable={unrateable} t={t} />
      <InfoLine
        label={t(HUB_LOC.area)}
        value={<LocText entry={HUB_FMT.areaHa} args={{ HA: numArg(hectares(row.areaM2), Unit.FloatSingleFraction) }} />}
        tip={t(HUB_LOC.areaTip)}
      />
      {!folded && <InfoLine
        label={t(HUB_LOC.mountainSnowDepth)}
        mark={snowMark}
        value={depthCm < 0 ? DASH : inches
          ? <LocText entry={HUB_FMT.snowDepthIn} args={{ DEPTH: numArg(depthValue(depthCm, true), Unit.Integer) }} />
          : <LocText entry={HUB_FMT.snowDepth} args={{ DEPTH: numArg(depthCm, Unit.Integer) }} />}
        tip={snowTip(t(HUB_LOC.mountainSnowDepth), t(HUB_LOC.mountainTipDepth))}
      />}
      {!folded && <InfoLine
        label={t(HUB_LOC.mountainSnowCover)}
        mark={snowMark}
        value={snowPct >= 0 ? <Num value={snowPct} unit={Unit.Percentage} /> : DASH}
        tip={snowTip(t(HUB_LOC.mountainSnowCover), t(HUB_LOC.mountainTipSnow))}
      />}
    </>
  );
  if (folded) {
    // The header row carries the vertical (the section's headline number). The snow is on the status line above.
    return (
      <Fold view="overview" id="mountain" header label={t(HUB_LOC.sectionMountain)} value={vertical} mark={verticalMark}
        tip={t(HUB_LOC.mountainTipVertical)}>
        {rows}
      </Fold>
    );
  }
  return (
    <>
      <InfoLine header label={t(HUB_LOC.sectionMountain)} value={vertical} mark={verticalMark} tip={t(HUB_LOC.mountainTipVertical)} />
      {rows}
    </>
  );
}


/** First problem row of an area with this code, or null. */
function problemOf(problems: readonly ProblemRow[] | null | undefined, sign: ResortRow["sign"], code: number): ProblemRow | null {
  for (const p of problems ?? []) if (p && p.code === code && sameEntity(p.sign, sign)) return p;
  return null;
}

/** The Variety row's flag shapes: the missing ratings as the Mountain legend draws them (black or harder: both diamonds). */
const NEED_BANDS: Record<VarietyNeed, readonly number[]> = { green: [BAND.Green], blue: [BAND.Blue], black: [BAND.Black, BAND.DoubleBlack] };

/** The Variety row's missing line: "Missing:" and one part per need. */
function varietyMissingLine(needs: readonly VarietyNeed[]): JSX.Element {
  return (
    <MissingLine key="m" head={HUB_LOC.receiptMissingVariety} parts={[
      needs.includes("green") && <IconText key="g" entry={HUB_LOC.varietyNeedGreen} line />,
      needs.includes("blue") && <IconText key="b" entry={HUB_LOC.varietyNeedBlue} line />,
      needs.includes("black") && <IconText key="k" entry={HUB_LOC.varietyNeedBlack} line />,
    ]} />
  );
}

function Receipt({ row, l, city, problems, build, areaRuns, t }: {
  row: ResortRow; l: ResortLive | null; city: CityStats; problems: readonly ProblemRow[]; build: readonly BuildItem[];
  areaRuns: readonly RunRow[]; t: Translate;
}): JSX.Element {
  const held = !!l && l.attractionHeld;
  const attraction = l ? l.attraction : -1;
  const full = l ? l.attractionFull : -1;
  // While the first write is held, the game keeps the SAVED score. So neither the bar's fill nor its tooltip's "now" claims this
  // Result. The bar shows an empty track and "…".
  const [fill, ghost, rest] = scoreBarParts(held ? -1 : attraction, full);
  const factors = factorRowsOf(problems, row.sign);

  const lodgeLabel = l && l.lodgesActive === 0 ? HUB_LOC.receiptLodgeBaseNone
    : l && l.summer ? HUB_LOC.receiptLodgeBaseHalf : HUB_LOC.receiptLodgeBase;
  const variant = resultVariant(l);
  // The best lodge's name (an unknown id shows nothing) and the school variant of the parts line.
  const bestEntry = l && typeof l.lodgeBest === "string" && l.lodgeBest !== "" ? ASSET_NAME[l.lodgeBest as BuildItemId] : undefined;
  const parts = {
    RUNS: numArg(l ? l.runsValue : -1, Unit.FloatSingleFraction),
    LIFTS: numArg(l ? l.liftsValue : -1, Unit.FloatSingleFraction),
    VARIETY: numArg(l ? l.varietyMultiplier : -1, Unit.FloatTwoFractions),
    VERTICAL: numArg(l ? l.verticalMultiplier : -1, Unit.FloatTwoFractions),
  };
  const school = receiptPartsVariant(l) === "school";
  // The School row's flag. Built-but-0 has its own cause (the build strip's AlreadyBuilt lock = this shown sign has one).
  const schoolNow = schoolState(l, problems, row.sign, schoolBuiltOf(build));
  const schoolFlag = schoolFlagOf(schoolNow);
  // The missing lines (icon scheme, contract 40): one caution line under a row's rule, only while something is missing. Every number is C#'s.
  const schoolMiss = schoolMissingOf(schoolNow);
  const needs = varietyMissing(l);
  const vShort = verticalShort(l);
  const liftsMiss = liftsMissing(row, l);
  const runsMiss = runsMissing(row, l, areaRuns);
  const lodgeShort = lodgeExtraShort(l);
  const K = numArg(city.scaleK, Unit.Integer);
  const noLodgeRow = variant === "noLodge" ? problemOf(problems, row.sign, PROBLEM.NoLodge) : null;
  const allOffRow = variant === "noLodge" && !noLodgeRow ? problemOf(problems, row.sign, PROBLEM.LodgesAllOff) : null;
  const barTip = <LocText entry={HUB_FMT.scoreBarTip} args={{ NOW: held ? "…" : numArg(attraction, Unit.Integer), FULL: numArg(full, Unit.Integer) }} />;

  return (
    <>
      {/* The score itself is the result row below and the tile above (DECISIONS 613: no header row repeating it). The bar's ticks (park 10,
          attraction 50, landmark 120) give the scale. The cap is in its tooltip (T82 B6: never "44 of 200", because it read as 22 %). */}
      <FlexBar
        className={styles.scoreBar}
        tip={barTip}
        parts={[
          { grow: fill, className: styles.scoreFill },
          { grow: ghost, className: styles.scoreGhost },
          { grow: rest, className: styles.barRest },
        ]}
      />
      <div className={styles.scoreTicks}>
        <div className={styles.tickSpace} style={{ flexGrow: 10 }} />
        <div className={styles.tick} />
        <div className={styles.tickSpace} style={{ flexGrow: 40 }} />
        <div className={styles.tick} />
        <div className={styles.tickSpace} style={{ flexGrow: 70 }} />
        <div className={styles.tick} />
        <div className={styles.tickSpace} style={{ flexGrow: 80 }} />
      </div>
      <div className={styles.scoreLabels}>
        <div className={styles.scoreLabel} style={{ flexGrow: 10 }} />
        <div className={styles.scoreLabel} style={{ flexGrow: 40 }}>{t(HUB_LOC.scoreTickPark)}</div>
        <div className={styles.scoreLabel} style={{ flexGrow: 70 }}>{t(HUB_LOC.scoreTickAttraction)}</div>
        <div className={styles.scoreLabel} style={{ flexGrow: 80 }}>{t(HUB_LOC.scoreTickLandmark)}</div>
      </div>
      <InfoLine
        label={t(lodgeLabel)}
        value={numArg(l ? l.lodgePart : -1, Unit.FloatSingleFraction)}
        tip={<TipLines lines={[
          t(HUB_LOC.receiptTipLodgeBase),
          l && l.lodgesActive >= 0 && <LocText key="n" entry={HUB_FMT.receiptTipLodges} args={{ N: numArg(l.lodgesActive, Unit.Integer) }} />,
          l && bestEntry && <LocText key="b" entry={HUB_FMT.receiptTipLodgeBest} args={{ NAME: t(bestEntry), E: numArg(l.lodgeExtra, Unit.FloatSingleFraction) }} />,
          lodgeShort >= 0 && <IconText key="m" entry={HUB_FMT.receiptMissingLodges} args={{ E: numArg(lodgeShort, Unit.FloatSingleFraction) }} line missing />,
        ]} />}
      />
      {/* T80 (item 4): the section total. Its full equation is one layer down (the header's tooltip: RECEIPT_PARTS[_SCHOOL] +
          RECEIPT_ROOT, T77's two lines). Then one labeled row per term. Every number is C#'s (D10). */}
      <Fold
        view="overview"
        id="terrain"
        label={t(HUB_LOC.receiptTerrain)}
        value={l && l.terrain >= 0 ? numArg(l.terrain, Unit.FloatSingleFraction, true) : DASH}
        tip={<TipLines lines={[
          school && l
            ? <LocText key="p" entry={HUB_FMT.receiptPartsSchool} args={{ ...parts, SCHOOL: numArg(l.schoolValue, Unit.FloatSingleFraction) }} />
            : <LocText key="p" entry={HUB_FMT.receiptParts} args={parts} />,
          <LocText key="r" entry={HUB_FMT.receiptRoot} args={{ K, TERRAIN: l && l.terrain >= 0 ? numArg(l.terrain, Unit.FloatSingleFraction) : DASH }} />,
          <LocText key="k" entry={HUB_FMT.receiptTipTerrain} args={{ K }} />,
        ]} />}
      >
      {/* The open share (why the runs term drops in a thaw) is in the Runs row's tooltip with the all-open value (DECISIONS 613). */}
      <InfoLine sub op="+" label={t(HUB_LOC.receiptRowRuns)} value={parts.RUNS}
        tip={<TipLines lines={[
          t(HUB_LOC.receiptTipRuns),
          l && l.openSharePct >= 0
            ? <LocText key="o" entry={HUB_FMT.receiptOpen} args={{ PCT: numArg(l.openSharePct, Unit.Percentage) }} />
            : t(HUB_LOC.receiptOpenNone),
          <LocText key="f" entry={HUB_FMT.receiptTipOpen} args={{ FULL: numArg(l ? l.runsValueFull : -1, Unit.FloatSingleFraction) }} />,
          runsMiss && <MissingLine key="m" head={HUB_LOC.receiptMissingRuns} parts={[
            runsMiss.closed > 0 && <IconText key="c" entry={HUB_FMT.runsMissingClosed} args={{ N: numArg(runsMiss.closed, Unit.Integer) }} line />,
            runsMiss.patchy > 0 && <IconText key="p" entry={HUB_FMT.runsMissingPatchy} args={{ N: numArg(runsMiss.patchy, Unit.Integer) }} line />,
            runsMiss.noLift > 0 && <IconText key="u" entry={HUB_FMT.runsMissingNoLift} args={{ N: numArg(runsMiss.noLift, Unit.Integer) }} line />,
          ]} />,
        ]} />} />
      <InfoLine sub op="+" label={t(HUB_LOC.receiptRowLifts)} value={parts.LIFTS}
        tip={<TipLines lines={[
          t(HUB_LOC.receiptTipLifts),
          liftsMiss && <MissingLine key="m" head={HUB_LOC.receiptMissingLifts} parts={[
            liftsMiss.noPower > 0 && <IconText key="p" entry={HUB_FMT.liftsMissingNoPower} args={{ N: numArg(liftsMiss.noPower, Unit.Integer) }} line />,
            liftsMiss.unfinished > 0 && <IconText key="u" entry={HUB_FMT.liftsMissingUnfinished} args={{ N: numArg(liftsMiss.unfinished, Unit.Integer) }} line />,
          ]} />,
        ]} />} />
      {/* Item 3: no school / no green run is a 0 with the warning glyph and the cause, not a tooltip sentence. */}
      <InfoLine sub op="+" label={t(HUB_LOC.receiptRowSchool)}
        flag={schoolFlag ? t(HUB_LOC[schoolFlag]) : undefined} flagDim={schoolNow === "waiting"}
        value={l && l.schoolValue >= 0 ? numArg(l.schoolValue, Unit.FloatSingleFraction, false, false) : DASH}
        tip={<TipLines lines={[
          <IconText key="r" entry={HUB_LOC.receiptTipSchool} />,
          schoolMiss && <IconText key="m" entry={HUB_LOC[schoolMiss]} line missing />,
        ]} />} />
      {/* The Variety row's flag at a glance (like the School row's "No green run"): the Warning glyph and the missing ratings' shapes. */}
      <InfoLine sub op="×" label={t(HUB_LOC.receiptRowVariety)}
        flagShapes={needs.length > 0 ? needs.flatMap((n) => NEED_BANDS[n].map((b) => <BandMarker key={`${n}-${b}`} band={b} />)) : undefined}
        value={parts.VARIETY}
        tip={<TipLines lines={[<IconText key="r" entry={HUB_LOC.receiptTipVariety} />, needs.length > 0 && varietyMissingLine(needs)]} />} />
      <InfoLine sub op="×" label={t(HUB_LOC.receiptRowVertical)}
        value={parts.VERTICAL}
        tip={<TipLines lines={[
          <IconText key="v" entry={HUB_FMT.receiptTipVertical} args={{ M: numArg(l ? l.scoreVerticalM : -1, Unit.Height) }} />,
          vShort >= 0 && <IconText key="m" entry={HUB_FMT.receiptMissingVertical} args={{ M: numArg(vShort, Unit.Height) }} line missing />,
        ]} />} />
      </Fold>
      <InfoLine
        label={t(HUB_LOC.receiptEfficiency)}
        value={l && l.efficiencyPct >= 0 ? <LocText entry={HUB_FMT.receiptTimes} args={{ PCT: numArg(l.efficiencyPct, Unit.Percentage) }} /> : DASH}
        tip={<TipLines lines={[
          t(HUB_LOC.receiptTipEfficiency),
          ...factors.map((f) => ({ label: t(factorEntry(f.factor)), value: numArg(f.pct, Unit.Percentage, true, false) })),
          l && l.jobs >= 0 && <LocText key="s" entry={HUB_FMT.staffLine} args={{ FILLED: numArg(l.jobsFilled, Unit.Integer), JOBS: numArg(l.jobs, Unit.Integer) }} />,
          efficiencyAtFloor(l) && <IconText key="m" entry={HUB_LOC.receiptMissingEfficiency} line missing />,
        ]} />}
      />
      <Divider />
      <InfoLine
        label={variant === "noLodge" ? t(HUB_LOC.receiptResultNoLodge) : t(HUB_LOC.receiptResult)}
        mark={null}
        value={held ? "…" : numArg(attraction, Unit.Integer)}
        tip={<TipLines lines={[t(HUB_LOC.receiptTipResult), held && t(HUB_LOC.tileTipHeld)]} />}
      />
      {noLodgeRow && noLodgeRow.ifFixed >= 0 && (
        <SubLine><LocText entry={HUB_FMT.problemGainLodge} args={{ A: numArg(noLodgeRow.ifFixed, Unit.Integer) }} /></SubLine>
      )}
      {allOffRow && allOffRow.ifFixed >= 0 && (
        <SubLine><LocText entry={HUB_FMT.problemGainLodgesOn} args={{ A: numArg(allOffRow.ifFixed, Unit.Integer) }} /></SubLine>
      )}
      <InfoLine
        label={t(HUB_LOC.detailShare)}
        value={l && l.sharePct >= 0 ? <Num value={l.sharePct} unit={Unit.PercentageSingleFraction} /> : DASH}
        tip={t(HUB_LOC.shareTip)}
      />
    </>
  );
}

export function OverviewView(): JSX.Element {
  const t = useT();
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const live = useValue(resortLive$);
  const runs = useValue(runs$);
  const problems = useValue(problems$);
  const city = useValue(city$);
  const build = useValue(build$); // the school tile's AlreadyBuilt lock (hubRules.schoolBuiltOf)

  const cur = currentResort(resortEntries(resorts), selected);
  const row = cur.row;
  if (!row) return <div className={styles.dimLine}>{DASH}</div>;
  const unlinked = cur.isUnlinked;
  const sign = unlinked ? null : row.sign;
  const l = liveOf(live, sign);
  const unrateable = unrateableCount(runsOf(runs, sign));

  if (unlinked) {
    return (
      <>
        <MountainSection row={row} live={l} city={city} unrateable={unrateable} t={t} />
        <MergedFold sign={null} />
        <Divider />
        <div className={styles.noteArea}>
          <div className={styles.note2}>{t(HUB_LOC.unlinkedNote)}</div>
        </div>
      </>
    );
  }
  return (
    <>
      <Receipt row={row} l={l} city={city} problems={problems} build={build} areaRuns={runsOf(runs, sign)} t={t} />
      <Divider />
      <MountainSection row={row} live={l} city={city} unrateable={unrateable} t={t} folded />
      <MergedFold sign={row.sign} />
    </>
  );
}
