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
 *   - Contract 6 (2026-10-11, Catalog Phase B, code round 2, hub group; round-2-contract.md section 8.5): the
 *     lodge-base tooltip adds RECEIPT_TIP_LODGE_BEST (the best lodge's Assets.NAME from
 *     ResortLive.lodgeBest, the others' extras lodgeExtra); the parts line becomes RECEIPT_PARTS_SCHOOL with the school
 *     term while schoolValue > 0 (hubRules.receiptPartsVariant), its tooltip RECEIPT_TIP[School]. Every number is still
 *     the model's Result (D10). ResortLive.luxuryIgnored is no longer read (always 0 since 2026-10-11, when the
 *     one-luxury-lodge cap went; the field stays on the wire so contract 6 does not change).
 *   - T77 (2026-10-18, Richard item 9): the parts line reads as the sum and the multipliers it is, "(runs + lifts [+ school]) × variety
 *     × vertical" (SkiResortScore.cs:468 S), and a second SubLine RECEIPT_ROOT "{K} × the square root of the line above = {TERRAIN}"
 *     closes the equation (HubRules.Terrain = K × sqrt(S); TERRAIN = ResortLive.terrain, the row's own value; nothing computed here).
 *   - T80 (2026-10-18, Richard round 2, PIPELINE section 0 "Hub UI design rule"): layered. "Runs, lifts and school" is the section
 *     total; its terms are labelled sub rows (Runs + the open-share SubLine, Lifts, Ski school, Variety × , Vertical × , Square root ×
 *     scale K); T77's two equation lines moved into the total's tooltip. The School row flags its 0 (hubRules.schoolState: "No ski
 *     school" / "No green run" with the Warning glyph). MOUNTAIN: snow is two rows, Snow depth (cm) and Run coverage (%), "≈" while
 *     estimated. Supersedes the T77 SubLines and the "Snow now" row above.
 *   - T82 UI round (2026-10-19; docs/research/ui/hub-design-review-2026-10-18.md B6, F11): each term row carries its operator in a
 *     fixed cell before the label ("+ Runs", "+ Lifts", "+ Ski school", "× Variety", "× Vertical", "× Scale, after the square root"; the review's "√" is not on
 *     the Gameface font safe list, check-bundle research 2.F.2), so the rows read as
 *     the equation the total's tooltip spells out, not as a sum that does not add; Variety and Vertical show the bare multiplier
 *     (the "×" moved to the operator cell). The header shows the score alone ("44"; supersedes "A of 200" above), the cap 200 in
 *     its tooltip (SCORE_BAR_TIP, also the bar's); the bar has a third tick, park 10. The UI still multiplies nothing (D10).
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { BAND, PROBLEM, build$, city$, problems$, resortLive$, resorts$, runs$, sameEntity, selectedResort$ } from "../bindings";
import type { BuildItem, CityStats, ProblemRow, ResortLive, ResortRow, RunRow } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import { factorRowsOf, hectares, liveOf, receiptPartsVariant, resultVariant, schoolBuiltOf, schoolFlagOf, schoolState, scoreBarParts } from "./hubRules";
import { ASSET_NAME, BAND_NAME, HUB_FMT, HUB_LOC, factorEntry } from "./hubText";
import type { BuildItemId } from "../bindings";
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
  // T80 (Richard item 6, PIPELINE section 0): snow is ALWAYS two numbers, depth in cm and run coverage in %; "≈" in the mark slot
  // when estimated, and the tooltip says measured or estimated. tools/test-hub-rules.js T18 guards it.
  const snowPct = live ? live.snowPct : -1;
  const depthCm = live ? live.snowDepthCm : -1;
  const snowMark = live && live.snowEstimated && (snowPct >= 0 || depthCm >= 0) ? "≈" : null;
  const how = live && (live.snowEstimated ? t(HUB_LOC.runEstimated) : t(HUB_LOC.statusTipMeasured));
  const snowTip = (head: string, body: string): JSX.Element => (
    <TipLines lines={[head, body, how, city.snowVisualsOff && t(HUB_LOC.statusTipVisuals)]} />
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
        label={t(HUB_LOC.mountainSnowDepth)}
        mark={snowMark}
        value={depthCm >= 0 ? <LocText entry={HUB_FMT.snowDepth} args={{ CM: numArg(depthCm, Unit.Integer) }} /> : DASH}
        tip={snowTip(t(HUB_LOC.mountainSnowDepth), t(HUB_LOC.mountainTipDepth))}
      />
      <InfoLine
        label={t(HUB_LOC.mountainSnowCover)}
        mark={snowMark}
        value={snowPct >= 0 ? <Num value={snowPct} unit={Unit.Percentage} /> : DASH}
        tip={snowTip(t(HUB_LOC.mountainSnowCover), t(HUB_LOC.mountainTipSnow))}
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

function Receipt({ row, l, city, problems, build, t }: {
  row: ResortRow; l: ResortLive | null; city: CityStats; problems: readonly ProblemRow[]; build: readonly BuildItem[]; t: Translate;
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
  // Contract 6: the best lodge's name (an unknown id shows nothing) and the school variant of the parts line.
  const bestEntry = l && typeof l.lodgeBest === "string" && l.lodgeBest !== "" ? ASSET_NAME[l.lodgeBest as BuildItemId] : undefined;
  const parts = {
    RUNS: numArg(l ? l.runsValue : -1, Unit.FloatSingleFraction),
    LIFTS: numArg(l ? l.liftsValue : -1, Unit.FloatSingleFraction),
    VARIETY: numArg(l ? l.varietyMultiplier : -1, Unit.FloatTwoFractions),
    VERTICAL: numArg(l ? l.verticalMultiplier : -1, Unit.FloatTwoFractions),
  };
  const school = receiptPartsVariant(l) === "school";
  // T80: the School row's flag; T86: built-but-0 has its own cause (the build strip's AlreadyBuilt lock = this shown sign has one).
  const schoolNow = schoolState(l, problems, row.sign, schoolBuiltOf(build));
  const schoolFlag = schoolFlagOf(schoolNow);
  const K = numArg(city.scaleK, Unit.Integer);
  const noLodgeRow = variant === "noLodge" ? problemOf(problems, row.sign, PROBLEM.NoLodge) : null;
  const allOffRow = variant === "noLodge" && !noLodgeRow ? problemOf(problems, row.sign, PROBLEM.LodgesAllOff) : null;
  const barTip = <LocText entry={HUB_FMT.scoreBarTip} args={{ NOW: held ? "…" : numArg(attraction, Unit.Integer), FULL: numArg(full, Unit.Integer) }} />;

  return (
    <>
      {/* T82 B6 (F11b): the header shows the score alone ("44", not "44 of 200": that read as 22 %, a failing grade); the cap and the
          ticks are in the tooltip, the bar's ticks (park 10, attraction 50, landmark 120) give the scale. */}
      <InfoLine
        header
        label={t(HUB_LOC.sectionAttraction)}
        value={held ? "…" : numArg(attraction, Unit.Integer)}
        tip={barTip}
      />
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
          t(HUB_LOC.receiptLodgeBase), t(HUB_LOC.receiptTipLodgeBase),
          l && l.lodgesActive >= 0 && <LocText key="n" entry={HUB_FMT.receiptTipLodges} args={{ N: numArg(l.lodgesActive, Unit.Integer) }} />,
          l && bestEntry && <LocText key="b" entry={HUB_FMT.receiptTipLodgeBest} args={{ NAME: t(bestEntry), E: numArg(l.lodgeExtra, Unit.FloatSingleFraction) }} />,
        ]} />}
      />
      {/* T80 (Richard item 4): the section total, its full equation one layer down (the header's tooltip: RECEIPT_PARTS[_SCHOOL] +
          RECEIPT_ROOT, T77's two lines), then one labelled row per term. Every number is C#'s (D10). */}
      <InfoLine
        label={t(HUB_LOC.receiptTerrain)}
        value={l && l.terrain >= 0 ? numArg(l.terrain, Unit.FloatSingleFraction, true) : DASH}
        tip={<TipLines lines={[
          t(HUB_LOC.receiptTerrain),
          school && l
            ? <LocText key="p" entry={HUB_FMT.receiptPartsSchool} args={{ ...parts, SCHOOL: numArg(l.schoolValue, Unit.FloatSingleFraction) }} />
            : <LocText key="p" entry={HUB_FMT.receiptParts} args={parts} />,
          <LocText key="r" entry={HUB_FMT.receiptRoot} args={{ K, TERRAIN: l && l.terrain >= 0 ? numArg(l.terrain, Unit.FloatSingleFraction) : DASH }} />,
          <LocText key="k" entry={HUB_FMT.receiptTipTerrain} args={{ K }} />,
        ]} />}
      />
      <InfoLine sub op="+" label={t(HUB_LOC.receiptRowRuns)} value={parts.RUNS}
        tip={<TipLines lines={[t(HUB_LOC.receiptRowRuns), t(HUB_LOC.receiptTipRuns)]} />} />
      <SubLine tip={<LocText entry={HUB_FMT.receiptTipOpen} args={{ FULL: numArg(l ? l.runsValueFull : -1, Unit.FloatSingleFraction) }} />}>
        {l && l.openSharePct >= 0
          ? <LocText entry={HUB_FMT.receiptOpen} args={{ PCT: numArg(l.openSharePct, Unit.Percentage) }} />
          : t(HUB_LOC.receiptOpenNone)}
      </SubLine>
      <InfoLine sub op="+" label={t(HUB_LOC.receiptRowLifts)} value={parts.LIFTS}
        tip={<TipLines lines={[t(HUB_LOC.receiptRowLifts), t(HUB_LOC.receiptTipLifts)]} />} />
      {/* Item 3: no school / no green run is a 0 with the warning glyph and the cause, not a tooltip sentence. */}
      <InfoLine sub op="+" label={t(HUB_LOC.receiptRowSchool)}
        flag={schoolFlag ? t(HUB_LOC[schoolFlag]) : undefined} flagDim={schoolNow === "waiting"}
        value={l && l.schoolValue >= 0 ? numArg(l.schoolValue, Unit.FloatSingleFraction, false, false) : DASH}
        tip={<TipLines lines={[t(HUB_LOC.receiptRowSchool), t(HUB_LOC.receiptTipSchool)]} />} />
      <InfoLine sub op="×" label={t(HUB_LOC.receiptRowVariety)}
        value={parts.VARIETY}
        tip={<TipLines lines={[t(HUB_LOC.receiptRowVariety), t(HUB_LOC.receiptTipVariety)]} />} />
      <InfoLine sub op="×" label={t(HUB_LOC.receiptRowVertical)}
        value={parts.VERTICAL}
        tip={<TipLines lines={[t(HUB_LOC.receiptRowVertical),
          <LocText key="v" entry={HUB_FMT.receiptTipVertical} args={{ M: numArg(l ? l.scoreVerticalM : -1, Unit.Height) }} />]} />} />
      <InfoLine sub op="×" label={t(HUB_LOC.receiptRowScale)} value={K}
        tip={<TipLines lines={[t(HUB_LOC.receiptRowScale), t(HUB_LOC.receiptTipScale)]} />} />
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
  const build = useValue(build$);   // T86: the school tile's AlreadyBuilt lock (hubRules.schoolBuiltOf)

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
      <Receipt row={row} l={l} city={city} problems={problems} build={build} t={t} />
      <Divider />
      <MountainSection row={row} live={l} city={city} unrateable={unrateable} t={t} />
    </>
  );
}
