/*
 * src/hub/OverviewView.tsx - the Overview (Attraction tile): the score receipt and the mountain.
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/
 * spec/ch6-hub-ui.md section 6.7, design hub-v3-design.md 3.1, 3.7, 6.5). The difficulty bar and
 * legend moved here from the contract-2 ResortDashboard.tsx (deleted).
 * Contract:
 *   - A sign, in this order: ATTRACTION header with "A of 200"; the 0-200 score bar (fill = the
 *     score, ghost = up to the full-efficiency score, integer flex-grow parts: hubRules.scoreBarParts)
 *     with ticks at 50 and 120 and their labels; the receipt (lodge base, runs and lifts with its
 *     parts and open share, Welcome Sign efficiency, the result, the share of the city's draw); then
 *     the MOUNTAIN section. Not linked: the MOUNTAIN section and the UNLINKED_NOTE (2 XS lines).
 *   - THE RECEIPT MULTIPLIES NOTHING (design 8.5, D10): every number is a ResortLive field of C#'s
 *     model Result; -1 or a missing live row renders "–" (numArg).
 *   - Held first write (second pass, e1-spec H4: no number the game is not using): while
 *     `attractionHeld` the result row shows "…" (with TILE_TIP_HELD in its tooltip), and - S6
 *     interpretation, ch6 section 6.7 names only the tile and the result row - so does the header's
 *     "A of 200" (the same number twice). E1 fixer (e1-spec 8.9 F12): the score bar too - an empty
 *     track and "…" as its tooltip's NOW, so no fill or digit claims the held Result.
 *   - Lodge-base label: "(no lodge)" when lodgesActive == 0 (S6: `== 0`, like hubRules.resultVariant,
 *     so -1 = no result does not claim "no lodge"; ch6 writes "<= 0"), "(half until a run counts)" in
 *     summer (R3: no counted run; Round F7 D6 wording, hub-ux Verification correction 2: S 220.3 /
 *     359.2 / 451.7 rem, fits the label room), else plain.
 *   - No lodge (second pass, e1-spec H3): the result row reads "Attraction (no lodge: 0)" and a
 *     SubLine "With a lodge, the attraction would be {A}." follows, A = the ifFixed of the area's
 *     NoLodge problem row (all lodges switched off: the LodgesAllOff row with PROBLEM_GAIN[LodgesOn]);
 *     no such row with ifFixed >= 0 -> no SubLine.
 *   - Fixed heights only (InfoLine 1.3 em + 10 rem of S, SubLine of XS, bar 0.5 em, ticks 5 rem):
 *     a value change never changes layout (D33).
 *   - Panel file (gate R11): subscribes resorts$, selectedResort$, resortLive$, runs$ (the
 *     unrateable count), problems$ (factor rows, gains), city$.
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { BAND, PROBLEM, city$, problems$, resortLive$, resorts$, runs$, sameEntity, selectedResort$ } from "../bindings";
import type { CityStats, ProblemRow, ResortLive, ResortRow, RunRow } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import { factorRowsOf, hectares, liveOf, resultVariant, scoreBarParts } from "./hubRules";
import { BAND_NAME, HUB_FMT, HUB_LOC, factorEntry } from "./hubText";
import {
  BandMarker, Divider, FlexBar, InfoLine, LocText, Num, StableWidth, SubLine, Tip, TipLines, cx, numArg,
} from "./parts";
import { currentResort, resortEntries, runsOf } from "./resortData";
import styles from "./hub.module.scss";

const DASH = "–";

// ---- difficulty (moved from the contract-2 ResortDashboard) --------------------------------------

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

/** Marker + count per band; a "being rated" item and a "cannot be rated" item only while one exists
 *  (Round F7 D6: at count 0 the dimmed "being rated" ring looked exactly like Flat's, hub-ux §5 item 8).
 *  The legend has no label of its own (deviation U16: the MOUNTAIN header names the block). */
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

/** MOUNTAIN: difficulty bar + legend, vertical, skiable area, snow now (sign and Not linked). */
function MountainSection({ row, live, city, unrateable, t }: {
  row: ResortRow; live: ResortLive | null; city: CityStats; unrateable: number; t: Translate;
}): JSX.Element {
  const counts = bandCounts(row);
  const pending = pendingCount(row, unrateable);
  const diffTip = (
    <TipLines
      lines={[
        t(HUB_LOC.difficulty),
        ...counts.map((n, b) => ({ label: t(BAND_NAME[b]), value: <Num value={n} unit={Unit.Integer} /> })),
        pending > 0 && <LocText key="p" entry={HUB_FMT.diffTipPending} args={{ N: numArg(pending, Unit.Integer) }} />,
        unrateable > 0 && <LocText key="u" entry={HUB_FMT.diffTipUnrateable} args={{ N: numArg(unrateable, Unit.Integer) }} />,
      ]}
    />
  );
  const snowPct = live ? live.snowPct : -1;
  const snowLabel = snowPct < 0 ? HUB_LOC.mountainSnow : live && live.snowEstimated ? HUB_LOC.mountainSnowEstimated : HUB_LOC.mountainSnowMeasured;
  const snowTip = (
    <TipLines
      lines={[
        t(HUB_LOC.mountainTipSnow),
        live && live.snowDepthCm >= 0 && <LocText key="d" entry={HUB_FMT.snowDepth} args={{ CM: numArg(live.snowDepthCm, Unit.Integer) }} />,
        city.snowVisualsOff && t(HUB_LOC.statusTipVisuals),
      ]}
    />
  );
  return (
    <>
      <InfoLine header label={t(HUB_LOC.sectionMountain)} />
      <DifficultyBar row={row} unrateable={unrateable} tip={diffTip} />
      <DifficultyLegend row={row} unrateable={unrateable} t={t} />
      <InfoLine
        label={t(HUB_LOC.mountainVertical)}
        value={row.verticalM >= 0 ? <Num value={row.verticalM} unit={Unit.Height} /> : DASH}
        mark={row.verticalM >= 0 && row.verticalApprox ? "≈" : null}
        tip={t(HUB_LOC.mountainTipVertical)}
      />
      <InfoLine
        label={t(HUB_LOC.area)}
        value={<LocText entry={HUB_FMT.areaHa} args={{ HA: numArg(hectares(row.areaM2), Unit.FloatSingleFraction) }} />}
        tip={t(HUB_LOC.areaTip)}
      />
      <InfoLine
        label={t(snowLabel)}
        value={snowPct >= 0 ? <Num value={snowPct} unit={Unit.Percentage} /> : DASH}
        tip={snowTip}
      />
    </>
  );
}

// ---- the receipt ----------------------------------------------------------------------------------

/** First problem row of an area with this code, or null. */
function problemOf(problems: readonly ProblemRow[] | null | undefined, sign: ResortRow["sign"], code: number): ProblemRow | null {
  for (const p of problems ?? []) if (p && p.code === code && sameEntity(p.sign, sign)) return p;
  return null;
}

function Receipt({ row, l, city, problems, t }: {
  row: ResortRow; l: ResortLive | null; city: CityStats; problems: readonly ProblemRow[]; t: Translate;
}): JSX.Element {
  const held = !!l && l.attractionHeld;
  const attraction = l ? l.attraction : -1;
  const full = l ? l.attractionFull : -1;
  // E1 fixer (e1-spec 8.9 F12, H4): while the first write is held the game keeps the SAVED score, so
  // neither the bar's fill nor its tooltip's "now" claims this Result: an empty track and "…".
  const [fill, ghost, rest] = scoreBarParts(held ? -1 : attraction, full);
  const factors = factorRowsOf(problems, row.sign);

  const lodgeLabel = l && l.lodgesActive === 0 ? HUB_LOC.receiptLodgeBaseNone
    : l && l.summer ? HUB_LOC.receiptLodgeBaseHalf : HUB_LOC.receiptLodgeBase;
  const variant = resultVariant(l);
  const noLodgeRow = variant === "noLodge" ? problemOf(problems, row.sign, PROBLEM.NoLodge) : null;
  const allOffRow = variant === "noLodge" && !noLodgeRow ? problemOf(problems, row.sign, PROBLEM.LodgesAllOff) : null;

  return (
    <>
      <InfoLine
        header
        label={t(HUB_LOC.sectionAttraction)}
        value={held ? "…" : <LocText entry={HUB_FMT.scoreOf} args={{ A: numArg(attraction, Unit.Integer) }} />}
      />
      <FlexBar
        className={styles.scoreBar}
        tip={<LocText entry={HUB_FMT.scoreBarTip} args={{ NOW: held ? "…" : numArg(attraction, Unit.Integer), FULL: numArg(full, Unit.Integer) }} />}
        parts={[
          { grow: fill, className: styles.scoreFill },
          { grow: ghost, className: styles.scoreGhost },
          { grow: rest, className: styles.barRest },
        ]}
      />
      <div className={styles.scoreTicks}>
        <div className={styles.tickSpace} style={{ flexGrow: 50 }} />
        <div className={styles.tick} />
        <div className={styles.tickSpace} style={{ flexGrow: 70 }} />
        <div className={styles.tick} />
        <div className={styles.tickSpace} style={{ flexGrow: 80 }} />
      </div>
      <div className={styles.scoreLabels}>
        <div className={styles.scoreLabel} style={{ flexGrow: 50 }} />
        <div className={styles.scoreLabel} style={{ flexGrow: 70 }}>{t(HUB_LOC.scoreTickAttraction)}</div>
        <div className={styles.scoreLabel} style={{ flexGrow: 80 }}>{t(HUB_LOC.scoreTickLandmark)}</div>
      </div>
      <InfoLine
        label={t(lodgeLabel)}
        value={numArg(l ? l.lodgePart : -1, Unit.FloatSingleFraction)}
        tip={<TipLines lines={[
          t(HUB_LOC.receiptLodgeBase), t(HUB_LOC.receiptTipLodgeBase),
          l && l.lodgesActive >= 0 && <LocText key="n" entry={HUB_FMT.receiptTipLodges} args={{ N: numArg(l.lodgesActive, Unit.Integer) }} />,
        ]} />}
      />
      <InfoLine
        label={t(HUB_LOC.receiptTerrain)}
        value={l && l.terrain >= 0 ? numArg(l.terrain, Unit.FloatSingleFraction, true) : DASH}
        tip={<LocText entry={HUB_FMT.receiptTipTerrain} args={{ K: numArg(city.scaleK, Unit.Integer) }} />}
      />
      <SubLine
        tip={<TipLines lines={[
          t(HUB_LOC.receiptTipRuns), t(HUB_LOC.receiptTipLifts), t(HUB_LOC.receiptTipVariety),
          <LocText key="v" entry={HUB_FMT.receiptTipVertical} args={{ M: numArg(l ? l.scoreVerticalM : -1, Unit.Height) }} />,
        ]} />}
      >
        <LocText entry={HUB_FMT.receiptParts} args={{
          RUNS: numArg(l ? l.runsValue : -1, Unit.FloatSingleFraction),
          LIFTS: numArg(l ? l.liftsValue : -1, Unit.FloatSingleFraction),
          VARIETY: numArg(l ? l.varietyMultiplier : -1, Unit.FloatTwoFractions),
          VERTICAL: numArg(l ? l.verticalMultiplier : -1, Unit.FloatTwoFractions),
        }} />
      </SubLine>
      <SubLine tip={<LocText entry={HUB_FMT.receiptTipOpen} args={{ FULL: numArg(l ? l.runsValueFull : -1, Unit.FloatSingleFraction) }} />}>
        {l && l.openSharePct >= 0
          ? <LocText entry={HUB_FMT.receiptOpen} args={{ PCT: numArg(l.openSharePct, Unit.Percentage) }} />
          : t(HUB_LOC.receiptOpenNone)}
      </SubLine>
      <InfoLine
        label={t(HUB_LOC.receiptEfficiency)}
        value={l && l.efficiencyPct >= 0 ? <LocText entry={HUB_FMT.receiptTimes} args={{ PCT: numArg(l.efficiencyPct, Unit.Percentage) }} /> : DASH}
        tip={<TipLines lines={[
          t(HUB_LOC.receiptEfficiency), t(HUB_LOC.receiptTipEfficiency),
          ...factors.map((f) => ({ label: t(factorEntry(f.factor)), value: numArg(f.pct, Unit.Percentage, true, false) })),
          l && l.jobs >= 0 && <LocText key="s" entry={HUB_FMT.staffLine} args={{ FILLED: numArg(l.jobsFilled, Unit.Integer), JOBS: numArg(l.jobs, Unit.Integer) }} />,
        ]} />}
      />
      <Divider />
      <InfoLine
        label={variant === "noLodge" ? t(HUB_LOC.receiptResultNoLodge) : t(HUB_LOC.receiptResult)}
        mark={null}
        value={held ? "…" : numArg(attraction, Unit.Integer)}
        tip={<TipLines lines={[t(HUB_LOC.receiptResult), t(HUB_LOC.receiptTipResult), held && t(HUB_LOC.tileTipHeld)]} />}
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
        <Divider />
        <div className={styles.noteArea}>
          <div className={styles.note2}>{t(HUB_LOC.unlinkedNote)}</div>
        </div>
      </>
    );
  }
  return (
    <>
      <Receipt row={row} l={l} city={city} problems={problems} t={t} />
      <Divider />
      <MountainSection row={row} live={l} city={city} unrateable={unrateable} t={t} />
    </>
  );
}
