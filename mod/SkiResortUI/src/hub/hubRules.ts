/*
 * src/hub/hubRules.ts - pure display rules of the hub: joins, the problem band, the dot, the footer, and the variants that pick
 * one localized string. No React, no bindings, no cs2 value import (values only from ../bindings, types from cs2/utils), so
 * tools/test-hub-rules.js runs it in Node with literal oracles. That tool throws on any other import. A new rule adds its rows there.
 * A function here never returns a placeholder LocEntry. It returns a variant name, and the component renders that branch's <LocText>.
 * Total: every function answers any input without throwing.
 */
import type { Entity } from "cs2/utils";
import {
  BUILD_ITEM_IDS, GLADE_LEVEL_MAX, GLADE_LEVEL_MIN, LIFT_STATE, QUICK_REJECT_BUCKETS, VARIETY_BAND, RUN_PROP_SPACING_OFF, LIFT_STATUS, NEXT_STEP, PROBLEM, RESORT_STATUS, RUN_KIND, SEASON_HINT, SKI_DAY_LIMIT, SKI_DAY_STATE,
  TICKET_GATE, TRIP_PLANNER_UNKNOWN, entityId, sameEntity,
} from "../bindings";
import type {
  BuildItem, BuildItemId, CityStats, LiftRow, ProblemRow, ResortLive, ResortRow, RunKind, RunLive, RunRow,
} from "../bindings";


/** The live row of an area (Entity.Null = Not linked). The same index first, else a scan. null = none. */
export function liveOf(live: readonly ResortLive[] | null | undefined, sign: Entity | null | undefined,
  index = -1): ResortLive | null {
  if (!live) return null;
  if (index >= 0 && index < live.length && live[index] && sameEntity(live[index].sign, sign)) return live[index];
  for (const l of live) if (l && sameEntity(l.sign, sign)) return l;
  return null;
}

/** RunLive by entityId(entity). Build once per runLive array (useMemo on the array). */
export function runLiveMap(live: readonly RunLive[] | null | undefined): ReadonlyMap<string, RunLive> {
  const m = new Map<string, RunLive>();
  for (const l of live ?? []) if (l) m.set(entityId(l.entity), l);
  return m;
}


/** Band rows of an area, in C#'s rank order. A band row has severity >= 1, is not a Factor row, and has a code that the band knows.
 *  The test is problemVariant != null, not a fixed code range: a range would drop RunsFlat = 17. */
export function bandRowsOf(problems: readonly ProblemRow[] | null | undefined, sign: Entity | null | undefined): ProblemRow[] {
  const out: ProblemRow[] = [];
  for (const p of problems ?? []) {
    if (!p || !sameEntity(p.sign, sign)) continue;
    if (p.severity >= 1 && p.code !== PROBLEM.Factor && problemVariant(p) !== null) out.push(p);
  }
  return out;
}

export function factorRowsOf(problems: readonly ProblemRow[] | null | undefined, sign: Entity | null | undefined): ProblemRow[] {
  const out: ProblemRow[] = [];
  for (const p of problems ?? []) if (p && sameEntity(p.sign, sign) && p.code === PROBLEM.Factor) out.push(p);
  return out;
}

export function isBuildItemId(id: string | null | undefined): id is BuildItemId {
  return !!id && (BUILD_ITEM_IDS as readonly string[]).indexOf(id) >= 0;
}

/** The one Build-strip dot. No sign in the city: "sign". Otherwise the buildItem of the
 *  first band row of the shown area that names a known item. Otherwise null. */
export function dotItem(problems: readonly ProblemRow[] | null | undefined, sign: Entity | null | undefined,
  hasSign: boolean): BuildItemId | null {
  if (!hasSign) return "sign";
  for (const p of bandRowsOf(problems, sign)) if (isBuildItemId(p.buildItem)) return p.buildItem;
  return null;
}

export type ProblemVariant =
  | "unlinked" | "noLodge" | "lodgesAllOff" | "lodgesSomeOff"
  | "efficiency" | "efficiencyMore" | "notRunning" | "notRunningMore"
  | "liftNoPower" | "liftSectionMissing" | "liftUnfinished" | "noLift" | "noRuns"
  | "stationsPair" | "stationsBaseOnly" | "stationsTopOnly" | "liftChairsStopped"
  | "runsUnserved" | "runsUnrateable" | "runsRating" | "freeLodges" | "runsFlat"
  | "signNoRoad" | "lodgeNoRoad"
  | "skiDaysCannotReach" | "skiDaysNoGuestsNear" | "noParkingNearLodge" | "lodgeNotSlopeside"
  | "mixedLiftTypes" | "summitLodgeNotLiftServed" | "schoolNoGreenRun" | "schoolsNotLinked"
  | "depotNoHillLodge" | "depotNoRoad" | "liftBend" | "liftTrainLimit" | "liftLowSpan";

export function problemVariant(p: ProblemRow): ProblemVariant | null {
  switch (p.code) {
    case PROBLEM.Unlinked: return "unlinked";
    case PROBLEM.NoLodge: return "noLodge";
    case PROBLEM.LodgesAllOff: return "lodgesAllOff";
    case PROBLEM.LodgesSomeOff: return "lodgesSomeOff";
    case PROBLEM.Efficiency:
      if (p.pct === 0) return p.more > 0 ? "notRunningMore" : "notRunning";
      return p.more > 0 ? "efficiencyMore" : "efficiency";
    case PROBLEM.LiftNoPower: return "liftNoPower";
    case PROBLEM.LiftSectionMissing: return "liftSectionMissing";
    case PROBLEM.LiftUnfinished: return "liftUnfinished";
    case PROBLEM.NoLift: return "noLift";
    case PROBLEM.NoRuns: return "noRuns";
    case PROBLEM.StationsOnly: return p.detail === 1 ? "stationsBaseOnly" : p.detail === 2 ? "stationsTopOnly" : "stationsPair";
    case PROBLEM.LiftChairsStopped: return "liftChairsStopped";
    case PROBLEM.RunsUnserved: return "runsUnserved";
    case PROBLEM.RunsUnrateable: return "runsUnrateable";
    case PROBLEM.RunsRating: return "runsRating";
    case PROBLEM.FreeLodges: return "freeLodges";
    case PROBLEM.RunsFlat: return "runsFlat";
    case PROBLEM.SignNoRoad: return "signNoRoad";
    case PROBLEM.LodgeNoRoad: return "lodgeNoRoad";
    case PROBLEM.SkiDaysCannotReach: return "skiDaysCannotReach";
    case PROBLEM.NoParkingNearLodge: return "noParkingNearLodge";
    case PROBLEM.LodgeNotSlopeside: return "lodgeNotSlopeside";
    case PROBLEM.MixedLiftTypes: return "mixedLiftTypes";
    case PROBLEM.SummitLodgeNotLiftServed: return "summitLodgeNotLiftServed";
    case PROBLEM.SchoolNoGreenRun: return "schoolNoGreenRun";
    case PROBLEM.SchoolsNotLinked: return "schoolsNotLinked";
    case PROBLEM.SkiDaysNoGuestsNear: return "skiDaysNoGuestsNear";
    case PROBLEM.DepotNoHillLodge: return "depotNoHillLodge";
    case PROBLEM.DepotNoRoad: return "depotNoRoad";
    case PROBLEM.LiftBend: return "liftBend";
    case PROBLEM.LiftTrainLimit: return "liftTrainLimit";
    case PROBLEM.LiftLowSpan: return "liftLowSpan";
    default: return null;
  }
}

export const FACTOR_ICON: Readonly<Record<string, string>> = {
  ElectricitySupply: "Media/Game/Notifications/NotEnoughElectricity.svg",
  WaterSupply: "Media/Game/Notifications/NoRunningWater.svg",
  SewageHandling: "Media/Game/Notifications/Sewage.svg",
  NotEnoughEmployees: "Media/Game/Notifications/NoWorkers.svg",
  Garbage: "Media/Game/Notifications/TooMuchGarbage.svg",
  DirtyWater: "Media/Game/Notifications/ContaminatedWater.svg",
  Disabled: "Media/Game/Notifications/TurnedOff.svg",
};

export type ProblemIcon =
  | { kind: "img"; src: string }
  | { kind: "glyph"; src: string; tone: "warn" | "info" | "ok" };

export const GLYPH_WARNING = "Media/Glyphs/Warning.svg";
export const GLYPH_INFO = "Media/Glyphs/Info.svg";
export const GLYPH_CHECK = "Media/Glyphs/Checkmark.svg";
export const ICON_NO_POWER = "Media/Game/Notifications/NotEnoughElectricity.svg";
export const ICON_TURNED_OFF = "Media/Game/Notifications/TurnedOff.svg";
/* vanilla's "Road required" notification icon, its Parking info icon and its "cannot reach" icon (pathfinding failed) */
export const ICON_NO_ROAD = "Media/Game/Notifications/RoadNotConnected.svg";
export const ICON_PARKING = "Media/Game/Icons/Parking.svg";
export const ICON_CANNOT_REACH = "Media/Game/Notifications/PathfindFailed.svg";

export function problemIcon(p: ProblemRow | null): ProblemIcon {
  if (!p) return { kind: "glyph", src: GLYPH_CHECK, tone: "ok" };
  if (p.code === PROBLEM.Efficiency && FACTOR_ICON[p.factor]) return { kind: "img", src: FACTOR_ICON[p.factor] };
  if (p.code === PROBLEM.LiftNoPower) return { kind: "img", src: ICON_NO_POWER };
  if (p.code === PROBLEM.LodgesAllOff || p.code === PROBLEM.LodgesSomeOff) return { kind: "img", src: ICON_TURNED_OFF };
  if (p.code === PROBLEM.SignNoRoad || p.code === PROBLEM.LodgeNoRoad || p.code === PROBLEM.DepotNoRoad) return { kind: "img", src: ICON_NO_ROAD };
  if (p.code === PROBLEM.NoParkingNearLodge) return { kind: "img", src: ICON_PARKING };
  if (p.code === PROBLEM.SkiDaysCannotReach) return { kind: "img", src: ICON_CANNOT_REACH };
  if (p.severity >= 2) return { kind: "glyph", src: GLYPH_WARNING, tone: "warn" };
  return { kind: "glyph", src: GLYPH_INFO, tone: "info" };
}

export type ProblemClick = "sign" | "lift" | "liftsView" | "runsView" | null;

/** What a click on a band row does. "sign" = fly to `subject`. The subject is a building: the sign, or for
 *  LodgeNoRoad the lodge. By code:
 *  - 20-22: the lodge C# names. 20 without one: the sign.
 *  - 24: the lowest summit lodge no lift reaches.
 *  - 25: the subject C# names (the top-level school).
 *  - 26: the first school on no sign.
 *  - 19: a road-less school, when no lodge is.
 *  MixedLiftTypes (23) is a lift row. Its click flies to the lift (to its gap while a section is missing), like LiftUnfinished.
 *  LiftBend (30) sends the bent tower's node as its subject, with a null subjectTop. focusLift flies to that tower.
 *  LiftTrainLimit (31) does the same with its tower.
 *  LiftLowSpan (32) sends the low span's edge. focusLift flies to its lowest carrier point. */
export function problemClick(p: ProblemRow): ProblemClick {
  switch (p.code) {
    case PROBLEM.NoLodge: case PROBLEM.LodgesAllOff: case PROBLEM.LodgesSomeOff: case PROBLEM.Efficiency:
    case PROBLEM.SignNoRoad: case PROBLEM.LodgeNoRoad:
    case PROBLEM.SkiDaysCannotReach: case PROBLEM.NoParkingNearLodge: case PROBLEM.LodgeNotSlopeside:
    case PROBLEM.SummitLodgeNotLiftServed: case PROBLEM.SchoolNoGreenRun: case PROBLEM.SchoolsNotLinked:
    case PROBLEM.SkiDaysNoGuestsNear: case PROBLEM.DepotNoHillLodge: case PROBLEM.DepotNoRoad:
      return "sign";
    case PROBLEM.LiftNoPower: case PROBLEM.LiftSectionMissing: case PROBLEM.LiftUnfinished:
    case PROBLEM.StationsOnly: case PROBLEM.LiftChairsStopped: case PROBLEM.MixedLiftTypes: case PROBLEM.LiftBend:
    case PROBLEM.LiftTrainLimit: case PROBLEM.LiftLowSpan:
      return "lift";
    case PROBLEM.NoLift: return "liftsView";
    case PROBLEM.NoRuns: case PROBLEM.RunsUnserved: case PROBLEM.RunsUnrateable: case PROBLEM.RunsRating:
    case PROBLEM.RunsFlat:
      return "runsView";
    default: return null;
  }
}

/** The band shows ONE row, the worst (C# ranks them). The rest open from "+N" (2026-11-07, DECISIONS 613: three fixed rows cost two
 *  lines on every view). */
export const BAND_SLOTS = 1;

export function bandSlots<T>(rows: readonly T[]): { slots: Array<T | null>; more: number } {
  const slots: Array<T | null> = [];
  for (let i = 0; i < BAND_SLOTS; i++) slots.push(rows[i] ?? null);
  return { slots, more: Math.max(0, rows.length - BAND_SLOTS) };
}


export const FOLDED_PROBLEM_CODES: readonly number[] = [PROBLEM.LiftUnfinished, PROBLEM.LiftNoPower, PROBLEM.LiftSectionMissing];

/** One band row as drawn. `row` is the first row of its code, so the rank is C#'s. `group` holds every row it stands for, in C#'s
 *  order (length 1 for a plain row). */
export interface BandRow {
  row: ProblemRow;
  group: ProblemRow[];
}

/** Band rows after the fold. Rows of a FOLDED_PROBLEM_CODES code that occurs two or more times become ONE row at the place of the
 *  first. Every other row stays as it is. */
export function foldBandRows(rows: readonly ProblemRow[]): BandRow[] {
  const byCode = new Map<number, ProblemRow[]>();
  for (const p of rows) {
    if (FOLDED_PROBLEM_CODES.indexOf(p.code) < 0) continue;
    const g = byCode.get(p.code);
    if (g) g.push(p); else byCode.set(p.code, [p]);
  }
  const out: BandRow[] = [];
  const done = new Set<number>();
  for (const p of rows) {
    const g = byCode.get(p.code);
    if (g && g.length >= 2) {
      if (done.has(p.code)) continue;
      done.add(p.code);
      out.push({ row: p, group: g.slice() });
    } else {
      out.push({ row: p, group: [p] });
    }
  }
  return out;
}

/** The names of a folded row as one list. It reads "Lift 3, 4, 5" when every name is one shared prefix plus a number (the default
 *  names). Otherwise it joins the names ("Meadow Quad, Lift 4"). An empty name reads "–". */
export function liftNameList(names: readonly string[]): string {
  const clean = names.map((n) => (n && n.trim() !== "" ? n.trim() : "–"));
  if (clean.length === 0) return "–";
  const re = /^(.*\D)(\d+)$/;
  const first = re.exec(clean[0]);
  if (first && clean.length >= 2) {
    const nums: string[] = [];
    for (const n of clean) {
      const m = re.exec(n);
      if (!m || m[1] !== first[1]) { nums.length = 0; break; }
      nums.push(m[2]);
    }
    if (nums.length === clean.length) return first[1] + nums.join(", ");
  }
  return clean.join(", ");
}


/** The counts an area's selector item shows: runs, and lodges (the Not linked row adds the lodges on no live sign). */
export function resortListCounts(row: ResortRow | null | undefined): { runs: number; lodges: number } {
  if (!row) return { runs: 0, lodges: 0 };
  const runs = Math.max(0, row.runs | 0);
  const lodges = Math.max(0, row.lodges | 0) + (row.kind === 1 ? Math.max(0, row.freeLodges | 0) : 0);
  return { runs, lodges };
}

export type ResortCountsVariant = "many" | "runOne" | "lodgeOne" | "one";

/** Which RESORT_COUNTS text: "{RUNS} runs · {LODGES} lodges" or one of its singular forms (0 reads plural: "0 runs"). */
export function resortCountsVariant(runs: number, lodges: number): ResortCountsVariant {
  const r1 = (runs | 0) === 1, l1 = (lodges | 0) === 1;
  return r1 && l1 ? "one" : r1 ? "runOne" : l1 ? "lodgeOne" : "many";
}


export type StatusVariant = "unlinked" | "unknown" | "settling" | "noRuns" | "line" | "lineEst" | "one" | "oneEst"
  | "partly" | "partlyEst" | "partlyOne" | "partlyOneEst";

export function statusVariant(row: ResortRow | null, live: ResortLive | null): StatusVariant {
  if (row && row.kind === 1) return "unlinked";
  if (!row || !live) return "unknown";
  // runsOpen -1 on a real row: the numbers are not known yet (a city loaded paused, HubRules.LoadSettleFrames). T209: one sentence, not dashes.
  if (live.runsOpen < 0) return "settling";
  if ((row.runs | 0) <= 0 || live.status === RESORT_STATUS.NoRuns) return "noRuns";
  const one = (row.runs | 0) === 1;
  // No run fully open but some partly open -> count the partly open ones. That way the word
  // ("Partly open") and the number never contradict each other ("Partly open · 0 of 7 runs open").
  if ((live.runsOpen | 0) <= 0 && (live.runsPartly | 0) > 0) {
    if (live.snowEstimated) return one ? "partlyOneEst" : "partlyEst";
    return one ? "partlyOne" : "partly";
  }
  if (live.snowEstimated) return one ? "oneEst" : "lineEst";
  return one ? "one" : "line";
}

export type ResultVariant = "result" | "noLodge";

/** Overview receipt result label: no active lodge -> "Attraction (no lodge: 0)". That way the
 *  receipt says why lodge base 0 + runs and lifts gives 0. lodgesActive -1 (no result) -> "result". Lodges the resort closed for the
 *  off-season (live.lodgesOffSeason, T237) are lodges: the area has them, so it never reads "no lodge". */
export function resultVariant(live: ResortLive | null): ResultVariant {
  return live && live.lodgesActive === 0 && !(live.lodgesOffSeason > 0) ? "noLodge" : "result";
}

/** The lodge base row's label key (HUB_LOC): none active -> "no lodge", or "lodges closed" while the resort closed them for the
 *  off-season; the summer share otherwise. */
export function lodgeBaseLabelKey(live: ResortLive | null): "receiptLodgeBaseNone" | "receiptLodgeBaseClosed" | "receiptLodgeBaseHalf" | "receiptLodgeBase" {
  if (live && live.lodgesActive === 0) return live.lodgesOffSeason > 0 ? "receiptLodgeBaseClosed" : "receiptLodgeBaseNone";
  return live && live.summer ? "receiptLodgeBaseHalf" : "receiptLodgeBase";
}

export type StatusWord = "open" | "partly" | "closed";

export function statusWord(status: number): StatusWord {
  return status === RESORT_STATUS.Open ? "open" : status === RESORT_STATUS.Partly ? "partly" : "closed";
}

export function closedRuns(row: ResortRow, live: ResortLive): number {
  return Math.max(0, (row.runs | 0) - Math.max(0, live.runsOpen | 0) - Math.max(0, live.runsPartly | 0));
}

/** Powder >= 0.5 = SkiDayRules.PowderMarkAt × 100. HubRulesTests tests the parity (H16). Powder takes
 *  sixths, so the rounded percent never straddles the mark. */
export const POWDER_MARK_PCT = 50;

/** The run snow bars in cm: a closed run opens at SNOW_OPEN_CM (RunSnowRules.SkiableDepthM), an open run stays open on SNOW_KEEP_CM
 *  (RunSnowRules.KeepDepthM). The run tooltip's {OPEN} / {KEEP}. test-hub-rules T29.5 ties both to the C# constants. */
export const SNOW_OPEN_CM = 91;
export const SNOW_KEEP_CM = 30;

export type StatusHint = "powder" | "warn" | "snow" | "season" | "none";

/** The SEASON_HINT values that stay on the status line as a warning (runs closing or not opening). ClosedForSummer (T236) is a plain state,
 *  not a warning: it shows its word in the slot (statusHint) but does not paint it as one. Every other hint word
 *  moves into the status tooltip and the slot shows the snow. */
export const SEASON_HINT_WARN: readonly number[] = [SEASON_HINT.Thawing, SEASON_HINT.WaitingForSnow, SEASON_HINT.TooWarm];

/** The status line's hint slot. The order of choice:
 *  1. Powder ("Powder day" at the mark).
 *  2. A warning hint (SEASON_HINT_WARN: "warn", the word stays).
 *  3. The snow ("snow": depth cm · coverage %, the hint word in the tooltip), while either is known.
 *  4. The hint word ("season").
 *  Nothing for the Not linked row, a sign without runs or a missing live row (the slot then stays empty).
 *  Total also for a live row without the field (undefined >= 50 is false: "season"). */
export function statusHint(row: ResortRow | null, live: ResortLive | null): StatusHint {
  if (!row || row.kind !== 0 || (row.runs | 0) <= 0 || !live || live.runsOpen < 0) return "none";
  if (live.skiDaysPowderPct >= POWDER_MARK_PCT) return "powder";
  // Snow on the top level, "64 cm · 88 %". A warning hint keeps the slot.
  if (SEASON_HINT_WARN.includes(live.seasonHint)) return "warn";
  if (live.seasonHint === SEASON_HINT.ClosedForSummer) return "season";   // T236: the word, not "0 cm · 0 %"
  return live.snowDepthCm >= 0 || live.snowPct >= 0 ? "snow" : "season";
}


export type NextVariant = "sign" | "unlinked" | "lodge" | "lift" | "run" | "done";

/** Footer idle text: no sign anywhere -> sign. Not linked shown -> unlinked. Otherwise
 *  ResortLive.nextStep (a missing live row reads "done"). */
export function nextVariant(hasSign: boolean, unlinkedShown: boolean, live: ResortLive | null): NextVariant {
  if (!hasSign) return "sign";
  if (unlinkedShown) return "unlinked";
  switch (live ? live.nextStep : NEXT_STEP.Done) {
    case NEXT_STEP.Lodge: return "lodge";
    case NEXT_STEP.Lift: return "lift";
    case NEXT_STEP.Run: return "run";
    default: return "done";
  }
}

export type Line1Variant = "run" | "lift" | "cost" | "name";

function known(n: number | undefined): n is number {
  return typeof n === "number" && Number.isFinite(n) && n >= 0;
}

export const RUN_ITEMS: ReadonlySet<BuildItemId> = new Set<BuildItemId>(["runNatural", "runGroomed", "runSnowmaking", "runNight", "runCatTrack"]);
/** The Build items that start the lift line tool (kind "line", buildItems.ts), one per lift type. */
const BUILD_LINE_ITEMS: ReadonlySet<BuildItemId> = new Set<BuildItemId>(["lift", "liftDetachable", "liftGondola", "liftTram"]);

/** The Quick runs "N lines dropped: A, B, C" line. It holds the total of QuickRunsState.rejects and its top three buckets (n > 0, most first,
 *  ties by bucket order). Fewer than 2 dropped lines say nothing worth a line: top is then empty. Any input answers (junk counts are 0). */
export function rejectSummary(rejects: readonly number[] | null | undefined): { total: number; top: Array<{ bucket: number; n: number }> } {
  const rows: Array<{ bucket: number; n: number }> = [];
  let total = 0;
  for (let i = 0; i < QUICK_REJECT_BUCKETS; i++) {
    const v = rejects ? rejects[i] : 0;
    const n = typeof v === "number" && Number.isFinite(v) && v > 0 ? Math.floor(v) : 0;
    total += n;
    if (n > 0) rows.push({ bucket: i, n });
  }
  if (total < 2) return { total, top: [] };
  rows.sort((a, b) => b.n - a.n || a.bucket - b.bucket);
  return { total, top: rows.slice(0, 3) };
}

/** A run-making tool is the active tool. That is the run drawing tool, the cat track tool (a Build run item with active set) or the
 *  Quick runs tool. Their info (run rows, Quick runs controls) lives on the Runs view. */
export function runToolActive(items: readonly BuildItem[] | null | undefined, quickActive: boolean | null | undefined): boolean {
  if (quickActive === true) return true;
  for (const it of items ?? []) if (it && it.active === true && isBuildItemId(it.id) && RUN_ITEMS.has(it.id)) return true;
  return false;
}

/** T167 (post-release round 1): the Resort Planner's steps in order. Trees come before Lifts: a lift line fells the trees in its corridor
 *  when it builds (LiftLineToolSystem.Trees.cs), and the tree filler leaves only runs free, so trees planted after the lifts grow back
 *  into the lift lines. */
export const PLANNER_STEPS = ["region", "scan", "build", "trees", "lifts"] as const;
export type PlannerStep = typeof PLANNER_STEPS[number];

/** What the step strip reads (QuickRunsState and its sheet, flattened so the rule stays pure). */
export interface PlannerStepInput {
  phase: string;
  corners: number;
  /** Runs of the planner's last build that Undo removes (QuickRunsState.batchRuns). */
  batchRuns: number;
  treesPlanted: boolean;
  treeJob: boolean;
  liftsToBuild: number;
  liftsRunning: boolean;
  liftsStarted: boolean;
}

/** The index in PLANNER_STEPS of the step to do next (the strip highlights it; the steps before it read as done).
 *  PLANNER_STEPS.length = every step done. A scan or a build in flight is its own step. In the Region phase: the lift queue running is
 *  Lifts. A region that holds the run build (its Undo is open, or the planner came back from the lift build with its region: 3 corners and
 *  a lift started; the Undo ended with that close) goes on with Trees (while a tree job runs, and until trees were planted or a lift was started), then Lifts while lifts
 *  are left to build, else done. A region without a build: under 3 corners Region, else Scan. */
export function plannerStepIndex(s: PlannerStepInput): number {
  const at = (step: PlannerStep): number => PLANNER_STEPS.indexOf(step);
  if (s.phase === "Scanning") return at("scan");
  if (s.phase === "Preview" || s.phase === "Emitted" || s.phase === "Retry" || s.phase === "Applying") return at("build");
  if (s.liftsRunning) return at("lifts");
  if (s.batchRuns > 0 || (s.corners >= 3 && s.liftsStarted)) {
    if (s.treeJob || (!s.treesPlanted && !s.liftsStarted)) return at("trees");
    return s.liftsToBuild > 0 ? at("lifts") : PLANNER_STEPS.length;
  }
  return s.corners < 3 ? at("region") : at("scan");
}

/** WHICH tool is active: "quick" (Quick runs), "lift" (the lift line tool), else the id of the active Build item ("" = none). Quick runs
 *  and the lift tool win over a Build item. Their own tools run while the item that started them can still read active. */
export function activeToolKey(items: readonly BuildItem[] | null | undefined, quickActive: boolean | null | undefined,
  liftActive: boolean | null | undefined): string {
  if (quickActive === true) return "quick";
  if (liftActive === true) return "lift";
  for (const it of items ?? []) if (it && it.active === true && isBuildItemId(it.id)) return it.id;
  return "";
}

/** The Hub view a tool key shows its info on: run items, the cat track and Quick runs -> "runs". Lift line items and the lift tool ->
 *  "lifts". Anything else (buildings, stations, fence, none) has no view of its own (null). */
export function toolView(key: string): "runs" | "lifts" | null {
  if (key === "quick") return "runs";
  if (key === "lift") return "lifts";
  if (!isBuildItemId(key)) return null;
  if (RUN_ITEMS.has(key)) return "runs";
  return BUILD_LINE_ITEMS.has(key) ? "lifts" : null;
}

/** The tab follow selects the active tool's view only when the tool key CHANGES to one with a view. Examples: run tool -> other run tool,
 *  run tool -> lift tool. The same tool staying active selects nothing (the player may have left the view on purpose). `seen` = the key last
 *  seen. The caller keeps it.
 *  `hold` (the lift build queue runs, R1-f): the queue opens the lift tool for each line itself, and the Resort Planner's lift block (progress
 *  line, Stop) is in the Runs view, so a change to the lift tool selects nothing and is NOT remembered. When the hold ends and that tool
 *  is still active (a line waits for the player), the follow runs then. A change to a tool without a view or to none is remembered as ever. */
export function toolTabFollow(seen: string, key: string, hold = false): { select: "runs" | "lifts" | null; seen: string } {
  if (hold && key !== seen && toolView(key) === "lifts") return { select: null, seen };
  return { select: key !== seen ? toolView(key) : null, seen: key };
}

/** Footer line 1 of a tile, by the item's KIND (buildItems.ts BuildItemMeta.kind). Runs with
 *  a base fee and a per-ha fee -> run. A line of any lift type with a per-km cost -> lift. Anything else with a cost
 *  (sign, lodge tiers, school, patrol huts, stations) -> cost. Otherwise the name alone (an unknown kind included). */
export function line1Variant(kind: string | undefined, item: BuildItem): Line1Variant {
  if (kind === "run") return known(item.price) && known(item.pricePerHa) ? "run" : "name";
  if (kind === "line") return known(item.costPerKm) ? "lift" : "name";
  return known(item.cost) ? "cost" : "name";
}


/** One build-table row as the pickers read it: buildItems.ts BuildItemMeta (BUILD_ITEMS) is passed in, because this
 *  file imports values only from ../bindings. */
export interface PickerMeta {
  id: string;
  kind: string;
  liftType: string;
}

export type PickerSet = "lodge" | "lift";

/** The options of the two alternative sets, in table order, from the items C# SENT. */
export interface PickerSets {
  /** Lodge tiers: the ids of the sent lodge items ("lodge", "lodgeHut", ...). */
  lodge: string[];
  /** Lift types (LiftTypeDef.Id) whose station AND line items are ALL sent. A half-imported family
    *  (stations first) is not offered, so it cannot invite a fixed-grip line between detachable stations. */
  lift: string[];
}

/** The remembered choice of each set ("" = none yet: the first option). */
export interface PickerSelection {
  lodge: string;
  lift: string;
}

export const NO_PICK: PickerSelection = { lodge: "", lift: "" };

function sentIdSet(items: readonly BuildItem[] | null | undefined): Set<string> {
  const s = new Set<string>();
  for (const it of items ?? []) if (it && typeof it.id === "string") s.add(it.id);
  return s;
}

function isLiftKind(kind: string): boolean {
  return kind === "station" || kind === "line";
}

/** The two sets' options: a picker row is drawn only for a set with >= 2 options, so a
 *  fixed-grip-only city before any import draws none. */
export function pickerSets(items: readonly BuildItem[] | null | undefined, meta: readonly PickerMeta[]): PickerSets {
  const sent = sentIdSet(items);
  const lodge: string[] = [];
  const order: string[] = [];
  const count = new Map<string, { total: number; sent: number }>();
  for (const m of meta ?? []) {
    if (!m) continue;
    if (m.kind === "lodge") {
      if (sent.has(m.id)) lodge.push(m.id);
    } else if (isLiftKind(m.kind) && m.liftType) {
      let c = count.get(m.liftType);
      if (!c) { c = { total: 0, sent: 0 }; count.set(m.liftType, c); order.push(m.liftType); }
      c.total++;
      if (sent.has(m.id)) c.sent++;
    }
  }
  const lift = order.filter((t) => { const c = count.get(t); return !!c && c.total > 0 && c.sent === c.total; });
  return { lodge, lift };
}

/** The choice in force: the remembered option while it is still offered, else the first option ("" = none). If an
 *  option disappears (a prefab gone after a re-resolution), the choice therefore becomes the first option. */
export function effectiveSelection(sets: PickerSets, sel: PickerSelection | null | undefined): PickerSelection {
  const pick = (options: string[], want: string | undefined): string =>
    want && options.indexOf(want) >= 0 ? want : options.length > 0 ? options[0] : "";
  return { lodge: pick(sets.lodge, sel ? sel.lodge : ""), lift: pick(sets.lift, sel ? sel.lift : "") };
}

/** The ids the strip draws: every SENT id of the table, except the station / line items of every lift type other than
 *  the chosen one. A half-imported type is never drawn. Every sent lodge tier is drawn (one Buildings tile each),
 *  so the lodge selection filters nothing. */
export function visibleIds(items: readonly BuildItem[] | null | undefined, meta: readonly PickerMeta[],
  sel: PickerSelection | null | undefined): Set<string> {
  const sent = sentIdSet(items);
  const eff = effectiveSelection(pickerSets(items, meta), sel);
  const out = new Set<string>();
  for (const m of meta ?? []) {
    if (!m || !sent.has(m.id)) continue;
    if (isLiftKind(m.kind) && m.liftType !== eff.lift) continue;
    out.add(m.id);
  }
  return out;
}

/** The picker option an item belongs to: a lodge tier is its own option, a station or line item its lift type. It is null
 *  for every other item (BuildStrip selects it when the item becomes the active tool). */
export function optionOf(id: string | null | undefined, meta: readonly PickerMeta[]): { set: PickerSet; value: string } | null {
  if (!id) return null;
  for (const m of meta ?? []) {
    if (!m || m.id !== id) continue;
    if (m.kind === "lodge") return { set: "lodge", value: m.id };
    if (isLiftKind(m.kind) && m.liftType) return { set: "lift", value: m.liftType };
    return null;
  }
  return null;
}

export type DotTarget = { kind: "tile"; id: string } | { kind: "chip"; set: PickerSet; value: string } | null;

/** Where the ONE dot goes. On its tile, when the tile is drawn. On the picker chip of its option,
 *  when the item is an alternative that is not chosen. A chip exists only in a set with >= 2 options. Otherwise nowhere. */
export function dotTarget(dot: string | null | undefined, items: readonly BuildItem[] | null | undefined,
  meta: readonly PickerMeta[], sel: PickerSelection | null | undefined): DotTarget {
  if (!dot) return null;
  if (visibleIds(items, meta, sel).has(dot)) return { kind: "tile", id: dot };
  const opt = optionOf(dot, meta);
  if (!opt) return null;
  const options = pickerSets(items, meta)[opt.set];
  return options.length >= 2 && options.indexOf(opt.value) >= 0 ? { kind: "chip", set: opt.set, value: opt.value } : null;
}


export type BuildTab = "lifts" | "runs" | "buildings";

export const BUILD_TABS: readonly BuildTab[] = ["lifts", "runs", "buildings"];

/** The tab of an item KIND: stations and lines -> lifts. Runs and the safety fence -> runs. The sign, lodge tiers, bus depot, school and patrol huts -> buildings
 *  (null for a kind this bundle does not know). */
export function tabOfKind(kind: string | undefined): BuildTab | null {
  switch (kind) {
    case "station": case "line": return "lifts";
    case "run": case "fence": return "runs";
    case "sign": case "lodge": case "depot": case "school": case "patrol": return "buildings";
    default: return null;
  }
}

export function tabOfItem(id: string | null | undefined, meta: readonly PickerMeta[]): BuildTab | null {
  if (!id) return null;
  for (const m of meta ?? []) if (m && m.id === id) return tabOfKind(m.kind);
  return null;
}

/** The tab an idle NEXT line points at (null = "done" / "unlinked": no opinion). */
export function tabOfNext(next: NextVariant): BuildTab | null {
  switch (next) {
    case "sign": case "lodge": return "buildings";
    case "lift": return "lifts";
    case "run": return "runs";
    default: return null;
  }
}

/** The tab that holds the ONE dot (null = no dot): a tile's own tab, or the tab of the picker the chip belongs to. */
export function tabOfDot(target: DotTarget, meta: readonly PickerMeta[]): BuildTab | null {
  if (!target) return null;
  if (target.kind === "tile") return tabOfItem(target.id, meta);
  return target.set === "lodge" ? "buildings" : "lifts";
}

export function drawnTabs(visible: ReadonlySet<string>, meta: readonly PickerMeta[]): BuildTab[] {
  const have = new Set<BuildTab>();
  visible.forEach((id) => { const t = tabOfItem(id, meta); if (t) have.add(t); });
  return BUILD_TABS.filter((t) => have.has(t));
}

/** The tab in force: the remembered one while it is drawn. Otherwise the tab the idle NEXT line points at. Otherwise the first drawn
 *  ("" = none drawn). */
export function effectiveTab(drawn: readonly BuildTab[], stored: string | null | undefined, next: NextVariant): BuildTab | "" {
  if (stored && (drawn as readonly string[]).indexOf(stored) >= 0) return stored as BuildTab;
  const n = tabOfNext(next);
  if (n && drawn.indexOf(n) >= 0) return n;
  return drawn.length > 0 ? drawn[0] : "";
}

/** The merge card's gate for a building that is not merged yet (contract 39). A merged building changes tier freely ("open"). With no run
 *  outline and no lift station anywhere nothing is near ("noGeometry": the card warns, Merge is off). Out of reach of existing geometry
 *  ("far": Merge is off, the status line says it). Otherwise "open". A merge past this gate would be billed and never count. */
export type MergeGate = "open" | "noGeometry" | "far";
export function mergeGate(merged: boolean, geometry: boolean, near: boolean): MergeGate {
  if (merged) return "open";
  if (!geometry) return "noGeometry";
  return near ? "open" : "far";
}

/** Contract 53 (T231): the city can pay this tier's fee now (MergeCardState.affordMask bit t). A tier the card has no bit for is not payable. */
export function mergeAffordable(affordMask: number, tier: number): boolean {
  return tier >= 0 && tier < 31 && (affordMask & (1 << tier)) !== 0;
}

export type FooterState = "follow" | "active" | "locked" | "empty" | "idle";

/** Footer status line priority: follow notice > active tool > locked click > idle. A hovered tile changes nothing: its tooltip
 *  names it, prices it and gives its hint (2026-11-07, DECISIONS 613: the line used to repeat the tooltip). */
export function footerState(hasItems: boolean, followOn: boolean, active: boolean, lockedClick: boolean): FooterState {
  if (!hasItems) return "empty";
  if (followOn) return "follow";
  if (active) return "active";
  if (lockedClick) return "locked";
  return "idle";
}

/** The Build strip opens like the game's toolbar (2026-11-07, DECISIONS 613). It shows only the tab row until the player clicks a tab.
 *  It is always open while something it started is engaged. That is a build tool, the tree tool, the planner, the lift generator, cat track delete
 *  or the merge overlay. A footer that cannot collapse (the contract-mismatch panel) is always open. */
export function footerOpen(stored: boolean, engaged: boolean, collapsible: boolean): boolean {
  return !collapsible || engaged || stored;
}

/** A tab click (T246 Design A, 2026-11-17): it shows that tab and never closes the strip. A closed strip opens on that tab.
 *  Only the chevron at the end of the tab row closes it (chevronClick; engaged, it cancels the tool first). Before T246 a click on the open tab closed the strip, and
 *  the view above re-fitted: the "tabs jump" of the Corwen playtest. */
export function tabClick(clicked: BuildTab): { open: boolean; tab: BuildTab } {
  return { open: true, tab: clicked };
}

/** The chevron at the end of the tab row: it opens or closes the strip. While a mode the strip started is engaged it cancels that mode
 *  (chevronCancel) and closes the strip (T262, 2026-11-20; before it, the chevron was dead then, T246). */
export function chevronClick(open: boolean, engaged: boolean): boolean {
  return engaged ? false : !open;
}

/** What is engaged right now, as the strip reads it. */
export interface EngagedModes {
  /** The id of the lit build tile (a build tool, the lift tool with a type, the cat track tool), or null. */
  activeId: string | null;
  tree: boolean;
  quickRuns: boolean;
  liftTool: boolean;
  catDelete: boolean;
  merge: boolean;
}

/** T262: the actions a chevron click sends to end every engaged mode: the lit tile's launch (it returns to the default tool), the tree
 *  tool's and the Quick runs tool's toggles, and the merge overlay off. The lift tool lights its type's tile and the cat track tool lights the
 *  Cat track tile (C# ActiveItemIndex), so their own toggles go only when no tile is lit: a toggle after the launch would open the tool again. */
export type ChevronCancel = "launch" | "tree" | "quickRuns" | "liftTool" | "catDelete" | "merge";
export function chevronCancel(m: EngagedModes): ChevronCancel[] {
  const out: ChevronCancel[] = [];
  if (m.activeId) out.push("launch");
  if (m.tree) out.push("tree");
  if (m.quickRuns) out.push("quickRuns");
  if (!m.activeId && m.liftTool) out.push("liftTool");
  if (!m.activeId && m.catDelete) out.push("catDelete");
  if (m.merge) out.push("merge");
  return out;
}

/** One tab's (or Buildings family's) tile row: its tile count and its tile group count. */
export interface StripRowShape { tiles: number; groups: number }

/** T246: the strip's fixed shape across every tab and family. slots = the most tiles any row draws, gaps = the most group gaps.
 *  Every row pads to `slots` cells and `gaps` gaps, so a tile has the same width on every tab and a tab change moves nothing. */
export function stripSlots(rows: readonly StripRowShape[]): { slots: number; gaps: number } {
  let slots = 1, gaps = 0;
  for (const r of rows) {
    if (r.tiles > slots) slots = r.tiles;
    if (r.groups - 1 > gaps) gaps = r.groups - 1;
  }
  return { slots, gaps };
}

export type BuildingFamily = "lodges" | "services";

/** The Buildings tab shows one family at a time (one tile row on every tab). The families are the sign and the lodge tiers, or the services
 *  (bus depot, ski school, patrol huts). */
export function buildingFamily(kind: string | undefined): BuildingFamily {
  return kind === "depot" || kind === "school" || kind === "patrol" ? "services" : "lodges";
}

export function runKindOfItem(id: BuildItemId): RunKind | undefined {
  switch (id) {
    case "runNatural": return RUN_KIND.Natural;
    case "runGroomed": return RUN_KIND.Groomed;
    case "runSnowmaking": return RUN_KIND.Snowmaking;
    case "runNight": return RUN_KIND.Night;
    case "runCatTrack": return RUN_KIND.CatTrack;
    default: return undefined;
  }
}


export type TicketNote = "open" | "off" | "noEfficiency" | "nothingOpen" | "notKnownYet";

export function ticketNote(gate: number, price: number): TicketNote {
  if (!(price > 0) || gate === TICKET_GATE.Off) return "off";
  switch (gate) {
    case TICKET_GATE.NoEfficiency: return "noEfficiency";
    case TICKET_GATE.NothingOpen: return "nothingOpen";
    case TICKET_GATE.NotKnownYet: return "notKnownYet";
    default: return "open";
  }
}

export type TicketsRow = "off" | "last" | "est" | "unknown";

export function ticketsRow(live: ResortLive, price: number): TicketsRow {
  if (!(price > 0) || live.ticketGate === TICKET_GATE.Off) return "off";
  if (live.ticketIncomeMonth < 0) return "unknown";
  return live.ticketIncomeEstimated ? "est" : "last";
}

export type IncomeNote = "est" | "unknown" | null;

export function incomeNote(live: ResortLive, price: number): IncomeNote {
  const r = ticketsRow(live, price);
  return r === "est" ? "est" : r === "unknown" ? "unknown" : null;
}


/** The price factor (C# SkiDayRules.PriceFullAt /
 *  PriceSpan / PriceMax). Parity: tools/test-hub-rules.js T10.1 and ResortBatch HubRulesTests H17 hold the same
 *  literal table. A change of the constants fails both and points at the hub texts. */
export const PRICE_FULL_AT = 240;
export const PRICE_SPAN = 200;
export const PRICE_MAX = 200;
/** The highest price with no effect on ski days (240 − 200 = 40). The default price is 100 (C#
 *  SkiMoneyRules.TicketPriceDefault), where the factor is 0.7. */
export const PRICE_NO_EFFECT_MAX = PRICE_FULL_AT - PRICE_SPAN;
/** The prices whose factor the price line's tooltip shows (MONEY_PRICE_TIP[Curve] P1..P3). */
export const PRICE_EXAMPLES: readonly number[] = [80, 120, PRICE_MAX];

/** The price the hub shows: city.ticketPrice once C# has sent the city (known), else -1 (unknown, "–").
 *  C# sends SkiMoneyRules.ClampTicketPrice of the Options value (0-200). */
export function hubPrice(city: CityStats | null | undefined): number {
  if (!city || !city.known) return -1;
  const p = city.ticketPrice;
  return typeof p === "number" && Number.isFinite(p) && p >= 0 ? p : -1;
}

/** A price tolerance as the factor reads it: max(0, t), 0 when not a finite number (C# math.max(0, tolerance)). */
function toleranceOf(tolerance: number | undefined): number {
  return typeof tolerance === "number" && Number.isFinite(tolerance) && tolerance > 0 ? tolerance : 0;
}

/** P = min(1, (240 + tolerance − min(price, 200)) / 200): the factor on every ski-day chance (C#
 *  SkiDayRules.PriceFactor(price, tolerance)). The tolerance is the counted lodges' price tolerance, ResortLive
 *  .priceTolerance: Luxury 40, Chalet 20, else 0. `tolerance` omitted = 0.
 *  -1 for an unknown price (negative or not finite). C# never sends one (ClampTicketPrice). C# would clamp a
 *  negative to 0 and answer 1. The difference is deliberate: the hub shows "–" instead. */
export function priceFactor(price: number, tolerance = 0): number {
  if (typeof price !== "number" || !Number.isFinite(price) || price < 0) return -1;
  return Math.min(1, Math.max(0, (PRICE_FULL_AT + toleranceOf(tolerance) - Math.min(PRICE_MAX, price)) / PRICE_SPAN));
}

/** The factor as a whole percent for the hub (Gameface strips trailing zeros, so a percent reads cleanly). Integer
 *  math: (240 + tolerance − min(price, 200)) × 100 / 200, rounded half up (45 → 98). −1 for an unknown price. */
export function priceFactorPct(price: number, tolerance = 0): number {
  if (typeof price !== "number" || !Number.isFinite(price) || price < 0) return -1;
  return Math.round(Math.min(100, Math.max(0,
    (PRICE_FULL_AT + toleranceOf(tolerance) - Math.min(PRICE_MAX, price)) * 100 / PRICE_SPAN)));
}

export function priceFullWith(tolerance: number): number {
  return PRICE_NO_EFFECT_MAX + toleranceOf(tolerance);
}

/** The Options slider's step for the lift ticket price (C# SkiMoneyRules.TicketPriceStep). */
export const PRICE_STEP = 5;

/** T240: a price as the Options slider stores it: clamped to 0-PRICE_MAX and snapped to the nearest PRICE_STEP (half up). Mirrors
 *  C# SkiMoneyRules.SnapTicketPrice, which C# applies to the setTicketPrice trigger. */
export function snapTicketPrice(price: number): number {
  const p = Math.min(PRICE_MAX, Math.max(0, Math.round(Number.isFinite(price) ? price : 0)));
  return Math.min(PRICE_MAX, Math.floor((p + PRICE_STEP / 2) / PRICE_STEP) * PRICE_STEP);
}

/** r3/sliders: the price slider's stops, 0 to PRICE_MAX in PRICE_STEP (41 stops, as the Options slider). */
export const PRICE_STOPS: readonly number[] = Array.from({ length: PRICE_MAX / PRICE_STEP + 1 }, (_, i) => i * PRICE_STEP);

/** r3/sliders (DECISIONS 677): the season pass price in money (C# SkiMoneyRules.PassPriceMin / Max / Step / Default), 100 stops. */
export const PASS_PRICE_MIN = 20;
export const PASS_PRICE_MAX = 2000;
export const PASS_PRICE_STEP = 20;
export const PASS_PRICE_DEFAULT = 400;
export const PASS_PRICE_STOPS: readonly number[] =
  Array.from({ length: (PASS_PRICE_MAX - PASS_PRICE_MIN) / PASS_PRICE_STEP + 1 }, (_, i) => PASS_PRICE_MIN + i * PASS_PRICE_STEP);

/** r3/sliders: the Money view's season pass row is drawn while passes are on (share above 0) and a ticket has a price (a pass is judged
 *  against day tickets: C# SkiMoneyRules.SalePassPrice is 0 without one). A control with nothing to act on is not drawn. */
export function passSliderShown(city: CityStats): boolean {
  return city.passShare > 0 && city.ticketPrice > 0;
}

/** r3/sliders (DECISIONS 677): the hub's one word for a demand factor (a whole percent: priceFactorPct, passFactorPct). The exact numbers
 *  live in the docs. Bands, lowest percent of each: unaffected 100, high 85, fair 60, low 40, very low below. ("unaffected", not "full":
 *  "Demand: full" read as "the resort is full".) null = unknown (negative). */
export type DemandWord = "unaffected" | "high" | "fair" | "low" | "veryLow";
export const DEMAND_BANDS: ReadonlyArray<readonly [number, DemandWord]> = [[100, "unaffected"], [85, "high"], [60, "fair"], [40, "low"], [0, "veryLow"]];
export function demandWord(pct: number): DemandWord | null {
  if (!(pct >= 0)) return null;
  for (const [min, w] of DEMAND_BANDS) if (pct >= min) return w;
  return "veryLow";
}

/** r3/sliders: the demand track's color at a factor percent, as [amber, red] opacities over the good green: green at 100, amber at 60
 *  (the top of "fair"), red at 20 (the ticket curve's floor at 200 without a better lodge). Unknown = green. */
export function demandHeat(pct: number): readonly [number, number] {
  const drop = pct >= 0 ? Math.min(100, 100 - Math.min(100, pct)) : 0;
  return [Math.min(1, drop / 40), Math.max(0, Math.min(1, (drop - 40) / 40))];
}

/** r3/sliders: the tooltip's example line shows only while the factor percent is under this: at 95-99 % "about 5 % fewer" adds nothing. */
export const DEMAND_EXAMPLE_BELOW = 95;

/** r3/sliders (DECISIONS 677): "about N % fewer" for a factor 0..1: round((1 − factor) × 100) to the nearest 5 (half up). −1 unknown. */
export function fewerPct(factor: number): number {
  if (!(factor >= 0)) return -1;
  return Math.round((100 - Math.min(1, factor) * 100) / 5) * 5;
}

/** r3/sliders (DECISIONS 677): pass demand, mirror of C# SkiMoneyRules.PassFactor. A pass is judged against day tickets: every
 *  household that would buy at the share buys up to PASS_FULL_AT × the ticket price; above it each further ticket's worth takes 1/PASS_SPAN
 *  off: clamp01((12 t − p) / 8 t). 0 with no ticket price or no pass price. −1 for an unknown (not finite) input. */
export const PASS_FULL_AT = 4;
export const PASS_SPAN = 8;
export function passFactor(passPrice: number, ticketPrice: number): number {
  if (!Number.isFinite(passPrice) || !Number.isFinite(ticketPrice)) return -1;
  if (ticketPrice <= 0 || passPrice <= 0) return 0;
  return Math.min(1, Math.max(0, ((PASS_FULL_AT + PASS_SPAN) * ticketPrice - passPrice) / (PASS_SPAN * ticketPrice)));
}
export function passFactorPct(passPrice: number, ticketPrice: number): number {
  const f = passFactor(passPrice, ticketPrice);
  return f < 0 ? -1 : Math.round(f * 100);
}
/** The pass price up to which every would-be buyer buys (PASS_FULL_AT × the ticket price). */
export function passFullPrice(ticketPrice: number): number {
  return PASS_FULL_AT * Math.max(0, ticketPrice);
}

/** 1.0.3 R3-B: a pass price as C# stores it (SkiMoneyRules.ClampPassPrice): snapped to PASS_PRICE_STEP (half up), clamped to MIN..MAX. */
export function clampPassPrice(v: number): number {
  const p = Number.isFinite(v) && v > 0 ? Math.floor(v) : 0;
  return Math.min(PASS_PRICE_MAX, Math.max(PASS_PRICE_MIN, Math.floor((p + PASS_PRICE_STEP / 2) / PASS_PRICE_STEP) * PASS_PRICE_STEP));
}

/** 1.0.3 R3-B: the destination pass of tourist parties (C# SkiPassRules). The day fee per person is a quarter of the pass price (DEST_VISITS =
 *  PASS_FULL_AT, the residents' break-even), half up, at least 1, never above the ticket. 0 with tickets off. */
export const DEST_VISITS = PASS_FULL_AT;
export function destFee(passPrice: number, ticketPrice: number): number {
  if (!Number.isFinite(ticketPrice) || ticketPrice <= 0) return 0;
  const quarter = Math.floor((clampPassPrice(passPrice) + Math.floor(DEST_VISITS / 2)) / DEST_VISITS);
  return Math.min(ticketPrice, Math.max(1, quarter));
}

/** The share of tourist parties on a destination pass, 0..1 (C# SkiPassRules.DestShare with SkiDayRules.PriceFactor(fee, tolerance)):
 *  share/100 × passFactor(pass, ticket) × priceFactor(fee, tolerance). 0 with passes off, tickets off or no demand. -1 unknown input. */
export function destShare(sharePct: number, passPrice: number, ticketPrice: number, tolerance = 0): number {
  if (!Number.isFinite(sharePct) || !Number.isFinite(passPrice) || !Number.isFinite(ticketPrice)) return -1;
  const share = Math.min(100, Math.max(0, Math.round(sharePct)));
  if (share <= 0 || ticketPrice <= 0) return 0;
  const fee = priceFactor(destFee(passPrice, ticketPrice), tolerance);
  const demand = passFactor(clampPassPrice(passPrice), ticketPrice);
  if (!(fee > 0) || !(demand > 0)) return 0;
  return Math.min(1, share / 100 * demand * Math.min(1, fee));
}
/** destShare as a whole percent for the hub, -1 unknown. */
export function destSharePct(sharePct: number, passPrice: number, ticketPrice: number, tolerance = 0): number {
  const s = destShare(sharePct, passPrice, ticketPrice, tolerance);
  return s < 0 ? -1 : Math.round(s * 100);
}

/** 1.0.3 R3-B: the residents' pass sale window (C# SkiPassRules). Season months, 0 = the season's first month: 0 before, 1-2 opening at the
 *  slider price, 3-4 stepped (+SALE_STEP_PCT %, snapped to the 20 step), 5-11 closed (the winter: first arrivals pay day tickets). */
export const SALE_FIRST_MONTH = 1;
export const SALE_MONTHS = 4;
export const SALE_STEP_MONTH = 3;
export const SALE_STEP_PCT = 15;
export type SalePhase = "before" | "opening" | "stepped" | "closed";

/** The stepped pass price (C# SkiPassRules.SteppedPrice): clampPassPrice(p) × (100 + SALE_STEP_PCT) / 100, half up, then clampPassPrice. */
export function steppedPassPrice(passPrice: number): number {
  return clampPassPrice(Math.floor((clampPassPrice(passPrice) * (100 + SALE_STEP_PCT) + 50) / 100));
}

/** The season month 0..11 of a calendar month index 0..11 (CityStats.month) for a season starting in seasonFirstMonth 1..12
 *  (CityStats.seasonFirstMonth). -1 when either is not known. */
export function seasonMonthOf(month: number, seasonFirstMonth: number): number {
  if (!Number.isInteger(month) || month < 0 || month > 11) return -1;
  if (!Number.isInteger(seasonFirstMonth) || seasonFirstMonth < 1 || seasonFirstMonth > 12) return -1;
  return (month - (seasonFirstMonth - 1) + 12) % 12;
}

/** The sale phase of a season month (C# SkiPassRules.PhaseOf of its floor). null unknown (negative). */
export function salePhaseOf(seasonMonth: number): SalePhase | null {
  if (!Number.isInteger(seasonMonth) || seasonMonth < 0) return null;
  if (seasonMonth < SALE_FIRST_MONTH) return "before";
  if (seasonMonth < SALE_STEP_MONTH) return "opening";
  if (seasonMonth < SALE_FIRST_MONTH + SALE_MONTHS) return "stepped";
  return "closed";
}

/** The Money view's sale line: the phase now and the calendar month indexes 0..11 it names: `open` the window's first month, `stepAt` the
 *  last month at the opening price, `last` the last month of sales. Prices: `price` the opening (slider) price, `stepped` the stepped price.
 *  null while the month or the season is not known. */
export interface SaleWindow { phase: SalePhase; open: number; stepAt: number; last: number; price: number; stepped: number; }
export function saleWindow(month: number, seasonFirstMonth: number, passPrice: number): SaleWindow | null {
  const phase = salePhaseOf(seasonMonthOf(month, seasonFirstMonth));
  if (!phase) return null;
  const cal = (m0: number): number => (seasonFirstMonth - 1 + m0) % 12;
  return {
    phase, open: cal(SALE_FIRST_MONTH), stepAt: cal(SALE_STEP_MONTH - 1), last: cal(SALE_FIRST_MONTH + SALE_MONTHS - 1),
    price: clampPassPrice(passPrice), stepped: steppedPassPrice(passPrice),
  };
}

/** T265: the rise of the stepped price over the early price in whole percent (the 20 step snap and the 2,000 cap make it differ from
 *  SALE_STEP_PCT at most stops: 100 -> 120 is +20, 2,000 does not rise). 0 when it does not rise. */
export function stepRisePct(win: SaleWindow): number {
  return win.price > 0 && win.stepped > win.price ? Math.round(((win.stepped - win.price) * 100) / win.price) : 0;
}

/** T265: the pass price a resident pays NOW for a slider (early) price: the stepped price while the sale is in its stepped phase (C#
 *  SkiPassRules.SalePrice), else the slider price itself (opening, and before / after the window, where the next sale opens at it). The
 *  Demand word judges this price (the sale's own demand uses it: SkiLiftTicketSystem.TryStartSale). `win` null (season unknown) = the slider price. */
export function passNowPrice(win: SaleWindow | null, price: number): number {
  return win && win.phase === "stepped" ? steppedPassPrice(price) : clampPassPrice(price);
}

// ---- T284 (contract 62): per-area prices. Mirror of C# SkiPassPlanRules (gate test-hub-rules T284). ----

/** A pass plan: the early price, the rise per step, the season months on sale (one unbroken run) and the months a rise starts (bits 0..11,
 *  0 = the season's first month). */
export interface PassPlan { early: number; risePct: number; saleMask: number; riseMask: number }
export const PLAN_MONTHS = 12;
export const PLAN_MONTH_MASK = 0xfff;
export const RISE_MAX_PCT = 50;
export const RISE_STEP_PCT = 5;
export const MAX_RISES = 3;
/** The 1.0.3 calendar: on sale in season months 1-4, one rise of SALE_STEP_PCT from season month 3. */
export const DEFAULT_SALE_MASK = 0x1e;
export const DEFAULT_RISE_MASK = 1 << SALE_STEP_MONTH;
/** The rise slider's stops: 0 to 50 in steps of 5. */
export const RISE_STOPS: readonly number[] = Array.from({ length: RISE_MAX_PCT / RISE_STEP_PCT + 1 }, (_, i) => i * RISE_STEP_PCT);

/** C# SkiPassPlanRules.ClampRisePct: clamped to 0-50, snapped to the nearest 5 (half up). */
export function clampRisePct(pct: number): number {
  const p = Math.min(RISE_MAX_PCT, Math.max(0, Number.isFinite(pct) ? Math.trunc(pct) : 0));
  return Math.min(RISE_MAX_PCT, Math.floor((p + Math.floor(RISE_STEP_PCT / 2)) / RISE_STEP_PCT) * RISE_STEP_PCT);
}
/** The lowest / highest set bit of a mask (0..11), 0 for an empty mask. */
export function firstBit(mask: number): number {
  for (let m = 0; m < PLAN_MONTHS; m++) if (mask & (1 << m)) return m;
  return 0;
}
export function lastBit(mask: number): number {
  for (let m = PLAN_MONTHS - 1; m >= 0; m--) if (mask & (1 << m)) return m;
  return 0;
}
export function bitCount(mask: number): number {
  let n = 0;
  for (let m = 0; m < PLAN_MONTHS; m++) if (mask & (1 << m)) n++;
  return n;
}
/** C# SkiPassPlanRules.Sanitize: prices clamped; the sale months filled to one run (the default run when none); the rises kept inside
 *  the run, not on its first month, the first MAX_RISES of them. */
export function sanitizePlan(p: PassPlan): PassPlan {
  let sale = p.saleMask & PLAN_MONTH_MASK;
  if (sale === 0) sale = DEFAULT_SALE_MASK;
  const first = firstBit(sale), last = lastBit(sale);
  const run = ((1 << (last + 1)) - 1) & ~((1 << first) - 1);
  const rise = p.riseMask & run & ~(1 << first);
  let kept = 0, count = 0;
  for (let m = 0; m < PLAN_MONTHS && count < MAX_RISES; m++) {
    if (rise & (1 << m)) { kept |= 1 << m; count++; }
  }
  return { early: clampPassPrice(p.early), risePct: clampRisePct(p.risePct), saleMask: run, riseMask: kept };
}
export function samePlan(a: PassPlan, b: PassPlan): boolean {
  return a.early === b.early && a.risePct === b.risePct && a.saleMask === b.saleMask && a.riseMask === b.riseMask;
}
/** The Options default plan (C# DefaultPlan). */
export function defaultPlan(optionsPassPrice: number): PassPlan {
  return { early: clampPassPrice(optionsPassPrice), risePct: SALE_STEP_PCT, saleMask: DEFAULT_SALE_MASK, riseMask: DEFAULT_RISE_MASK };
}
/** The rises started at or before season month `month`. */
export function risesBy(p: PassPlan, month: number): number {
  let n = 0;
  for (let m = 0; m <= month && m < PLAN_MONTHS; m++) if (p.riseMask & (1 << m)) n++;
  return n;
}
/** C# PriceAfter: each rise × (100 + rise) / 100, half up, snapped to the 20 step and capped (clampPassPrice). */
export function priceAfter(p: PassPlan, rises: number): number {
  let price = clampPassPrice(p.early);
  const r = Math.min(RISE_MAX_PCT, Math.max(0, p.risePct));
  for (let k = 0; k < rises; k++) price = clampPassPrice(Math.floor((price * (100 + r) + 50) / 100));
  return price;
}
export function priceInMonth(p: PassPlan, month: number): number {
  return priceAfter(p, risesBy(p, month));
}
/** C# PhaseAt for a whole season month: before the run, opening in it before the first rise, stepped from the first rise, closed after. */
export function planPhaseAt(p: PassPlan, month: number): SalePhase {
  if (month < firstBit(p.saleMask)) return "before";
  if (month > lastBit(p.saleMask)) return "closed";
  return risesBy(p, month) > 0 ? "stepped" : "opening";
}

/** The month strip's state of one season month. */
export type MonthChip = "off" | "sale" | "rise";
export function monthChip(p: PassPlan, month: number): MonthChip {
  if (p.riseMask & (1 << month)) return "rise";
  return p.saleMask & (1 << month) ? "sale" : "off";
}
/** One click on a month of the strip, sanitized (the run stays unbroken):
 *  - off: the month goes on sale (the run grows to it);
 *  - on sale: a rise starts there (not on the run's first month, at most MAX_RISES), else an end of the run leaves the run;
 *  - a rise: the rise goes; at an end of the run the month also leaves the run.
 *  A one-month run keeps its month. */
export function cycleMonth(p: PassPlan, month: number): PassPlan {
  const s = sanitizePlan(p);
  const bit = 1 << month;
  const first = firstBit(s.saleMask), last = lastBit(s.saleMask);
  const atEnd = (month === first || month === last) && first !== last;
  const chip = monthChip(s, month);
  if (chip === "off") return sanitizePlan({ ...s, saleMask: s.saleMask | bit });
  if (chip === "sale") {
    if (month !== first && bitCount(s.riseMask) < MAX_RISES) return sanitizePlan({ ...s, riseMask: s.riseMask | bit });
    return atEnd ? sanitizePlan({ ...s, saleMask: s.saleMask & ~bit }) : s;
  }
  return sanitizePlan({ ...s, riseMask: s.riseMask & ~bit, saleMask: atEnd ? s.saleMask & ~bit : s.saleMask });
}

/** The setAreaPassMonths argument: saleMask | riseMask << 12 (C# SkiLiftTicketSystem.EditArea splits it). */
export function planMonthsArg(p: PassPlan): number {
  return (p.saleMask & PLAN_MONTH_MASK) | ((p.riseMask & PLAN_MONTH_MASK) << PLAN_MONTHS);
}

/** The plan C# sent for an area (`next` = the next plan, else the plan in force). null while unknown (no row, or a field below 0). */
export function planOf(l: ResortLive | null | undefined, next: boolean): PassPlan | null {
  if (!l) return null;
  const p = next
    ? { early: l.passNextEarly, risePct: l.passNextRisePct, saleMask: l.passNextSaleMask, riseMask: l.passNextRiseMask }
    : { early: l.passEarly, risePct: l.passRisePct, saleMask: l.passSaleMask, riseMask: l.passRiseMask };
  if (!(p.early > 0) || !(p.risePct >= 0) || !(p.saleMask > 0) || !(p.riseMask >= 0)) return null;
  return p;
}
/** The area's day ticket for the hub: ResortLive.areaTicket once C# sent it (0 or more), else the Options price (hubPrice, -1 unknown). */
export function areaTicketOf(l: ResortLive | null | undefined, city: CityStats | null | undefined): number {
  if (l && typeof l.areaTicket === "number" && Number.isFinite(l.areaTicket) && l.areaTicket >= 0) return l.areaTicket;
  return hubPrice(city);
}
/** The calendar month index 0..11 of a season month for a season starting in calendar month seasonFirstMonth (1..12). */
export function calendarMonthOf(seasonMonth: number, seasonFirstMonth: number): number {
  return (seasonFirstMonth - 1 + seasonMonth + 12 * 4) % 12;
}
/** The season months (bits 0..11) that overlap the climate winter [winterStart, winterEnd) in season positions. 0 unknown. */
export function winterMonthMask(winterStart: number, winterEnd: number): number {
  if (!Number.isFinite(winterStart) || !Number.isFinite(winterEnd) || !(winterEnd > winterStart)) return 0;
  let mask = 0;
  for (let m = 0; m < PLAN_MONTHS; m++) if (m + 1 > winterStart && m < winterEnd) mask |= 1 << m;
  return mask;
}

/** C# SkiPassPlanRules.Demand with value left s 0..1: PassFactor at s = 1; else the price over s against the ticket, × s. */
export function planDemand(passPrice: number, ticketPrice: number, valueLeft: number): number {
  if (!Number.isFinite(valueLeft)) return 0;
  if (valueLeft >= 1) return passFactor(passPrice, ticketPrice);
  if (!(valueLeft > 0) || !(ticketPrice > 0) || !(passPrice > 0)) return 0;
  const ts = ticketPrice * valueLeft;
  const f = ((PASS_FULL_AT + PASS_SPAN) * ts - passPrice) / (PASS_SPAN * ts);
  return Math.min(1, Math.max(0, f)) * valueLeft;
}

/** The Money view's sale line of a plan: the phase in this season month, the calendar months it names (`open` the run's first month,
 *  `through` the last month at the current price, `last` the run's last month), `price` the price now (the early price before the run),
 *  `next` the price after the next rise (0 none) and `risePct` the price now over the early price in whole percent. */
export interface PlanWindow { phase: SalePhase; open: number; through: number; last: number; price: number; next: number; risePct: number }
export function planWindow(p: PassPlan, seasonMonth: number, seasonFirstMonth: number): PlanWindow | null {
  if (!Number.isInteger(seasonMonth) || seasonMonth < 0 || seasonMonth >= PLAN_MONTHS) return null;
  if (!Number.isInteger(seasonFirstMonth) || seasonFirstMonth < 1 || seasonFirstMonth > 12) return null;
  const phase = planPhaseAt(p, seasonMonth);
  const first = firstBit(p.saleMask), last = lastBit(p.saleMask);
  const at = Math.min(last, Math.max(first, seasonMonth));
  const price = priceInMonth(p, at);
  let through = last, next = 0;
  for (let m = at + 1; m <= last; m++) {
    if (p.riseMask & (1 << m)) { through = m - 1; next = priceInMonth(p, m); break; }
  }
  const early = clampPassPrice(p.early);
  const cal = (m: number): number => calendarMonthOf(m, seasonFirstMonth);
  return {
    phase, open: cal(first), through: cal(through), last: cal(last), price, next: next > price ? next : 0,
    risePct: price > early ? Math.round(((price - early) * 100) / early) : 0,
  };
}

/** r3/sliders: where a value sits on a slider track, 0..1, for stops in screen order (evenly spaced). A value on a stop sits on it. A
 *  value between two neighbor stops (a kind's default such as 90 m between 100 and 80) sits in between, by its value: the tightest pair
 *  that holds it (on the spacing list 90 also lies between Off and 200 m). −1 = not on the track (unknown). */
export function sliderPos(stops: readonly number[], value: number): number {
  const n = stops.length;
  if (n < 2 || !Number.isFinite(value)) return -1;
  for (let i = 0; i < n; i++) if (stops[i] === value) return i / (n - 1);
  let best = -1, span = Infinity;
  for (let i = 0; i + 1 < n; i++) {
    const a = stops[i], b = stops[i + 1];
    if ((value - a) * (value - b) < 0 && Math.abs(b - a) < span) { best = i; span = Math.abs(b - a); }
  }
  return best < 0 ? -1 : (best + (value - stops[best]) / (stops[best + 1] - stops[best])) / (n - 1);
}

/** r3/sliders: the stop nearest to a track fraction 0..1 (clamped; not finite = 0). */
export function sliderStopAt(stops: readonly number[], frac: number): number {
  const n = stops.length;
  const f = Math.min(1, Math.max(0, Number.isFinite(frac) ? frac : 0));
  return stops[Math.round(f * (n - 1))];
}

/** The Overview's RECEIPT_PARTS line - "school" (RECEIPT_PARTS_SCHOOL, the school term shown) while the
 *  model counts a school value above 0, else "plain". */
export function receiptPartsVariant(l: ResortLive | null): "school" | "plain" {
  return l && l.schoolValue > 0 ? "school" : "plain";
}

/** The Money view's "Lodge spend" income row is drawn only while lodge spend earned money this month
 *  (lodgeSpendMonth > 0). -1 = unknown and 0 = the setting off: both draw nothing. */
export function lodgeSpendShown(l: ResortLive | null): boolean {
  return !!l && l.lodgeSpendMonth > 0;
}

/** The Money view's pass rows and the Guests view's pass-holder row are drawn while passes are on (the
 *  Options share above 0). They are also drawn when a pass was sold or a holder skied. */
export function passesShown(l: ResortLive | null, city: CityStats): boolean {
  if (!l) return false;
  return city.passShare > 0 || l.passesSoldSeason > 0 || l.passHoldersThisMonth > 0 || l.passHoldersLastMonth > 0;
}

/** The Money view's lesson row and the Guests view's "took a lesson" row are drawn once a lesson was taken
 *  this month or last. A working ski school and the Options share above 0 make a lesson happen. */
export function lessonsShown(l: ResortLive | null): boolean {
  return !!l && (l.lessonsThisMonth > 0 || l.lessonsLastMonth > 0);
}

/** 1.0.3 T258: the "Turned away by price" row once a game-sent party walked away this month or last. */
export function priceTurnedShown(l: ResortLive | null): boolean {
  return !!l && (l.priceTurnedThisMonth > 0 || l.priceTurnedLastMonth > 0);
}

/** 1.0.3 T258: the party price check's full-demand price (C# SkiDayRules.PartyPriceFullAt − PriceSpan + tolerance): the default ticket
 *  100 plus the best lodge's tolerance. */
export const PARTY_PRICE_FULL_AT = 300;
export function partyPriceFullWith(tolerance: number): number {
  return PARTY_PRICE_FULL_AT - PRICE_SPAN + Math.max(0, tolerance);
}

/** 1.0.3 T258: the share of game-sent parties that pay instead of walking away, 0..1 (C# SkiDayRules.PartyPriceFactor). */
export function partyPriceFactor(price: number, tolerance = 0): number {
  if (!(price >= 0)) return -1;
  return Math.min(1, Math.max(0, (PARTY_PRICE_FULL_AT + Math.max(0, tolerance) - Math.min(PRICE_MAX, price)) / PRICE_SPAN));
}

/** The "sold before the season" pass row is drawn under the season's pass row once the pre-season sale sold a pass. */
export function presaleShown(l: ResortLive | null): boolean {
  return !!l && l.passesPreSeason > 0;
}

/** The city-income estimate row is drawn when C# sent a figure (>= 0). Also under Tourism Overhaul: the game's TouristIncome
 *  statistic keeps counting there (its tourists still start and leave through the vanilla household systems, measured 2026-11-16).
 *  `city` stays in the signature for the callers. */
export function cityIncomeShown(l: ResortLive | null, _city: CityStats): boolean {
  return !!l && l.cityIncomeEst >= 0;
}

/** T226 / T225 / the value line (contract 50, DECISIONS 666, 668). */

/** "Tourist spending drawn" (Overview): the area's share of the game's TouristIncome statistic, the Money view's estimate row. That
 *  statistic is money tourists bring minus money they take home (HouseholdInitializeSystem :242, HouseholdMoveAwaySystem :173), so it
 *  is their spending at city businesses, not city money: it is never added to the resort's net (round 2 r2-tourism REPORT, D4).
 *  null when the Money row is not drawn (no figure). */
export function touristSpendingDrawn(l: ResortLive | null, city: CityStats): number | null {
  if (!l || !cityIncomeShown(l, city)) return null;
  return l.cityIncomeEst;
}

/** The "Staying at resort hotels" row: drawn when the area has a merged hotel (C# sends -1 without one). */
export function hotelShown(l: ResortLive | null): boolean {
  return !!l && typeof l.hotelGuests === "number" && l.hotelGuests >= 0;
}

/** The areas' sum is over the Options cap (the tip says so). False while the bonus is off. */
export function bonusCapped(city: CityStats): boolean {
  return city.tourismBonusCapPct > 0 && city.tourismBonusSumPct > city.tourismBonusCapPct + 0.005;
}

/** The area's bonus parts for the tip, in percent: base (2 when the area qualifies, else 0), lift lines x 0.25, lodge points x 0.25, and
 *  the open share 0..100. null without a score (-1). The numbers are TourismBonusRules' (ResortBatch TB-1). */
export function bonusParts(l: ResortLive | null): { base: number; lifts: number; lodges: number; openPct: number } | null {
  if (!l || !(l.bonusPct >= 0) || !(l.bonusSizePct >= 0)) return null;
  const qualifies = l.bonusSizePct > 0;
  return {
    base: qualifies ? 2 : 0,
    lifts: qualifies ? 0.25 * l.bonusLiftLines : 0,
    lodges: qualifies ? 0.25 * l.bonusTierPoints : 0,
    openPct: l.bonusOpenSharePct,
  };
}

/** The Net's eight rows of the month so far, in the
 *  Finances column's order, with their sign. C#'s invariant: Σ sign × max(0, value) over the eight = netMonth (an unknown row, -1, reads 0).
 *  The view draws them as sent. netRowsTotal is that sum for the tests (the UI never shows it: the Net row is netMonth as sent). */
export type NetRowId = "tickets" | "lodge" | "lessons" | "passes" | "lifts" | "runs" | "bus" | "buildings" | "wages";
export const NET_ROW_IDS: readonly NetRowId[] = ["tickets", "lodge", "lessons", "passes", "lifts", "runs", "bus", "buildings", "wages"];

export function netRows(l: ResortLive): Array<{ id: NetRowId; value: number; sign: 1 | -1 }> {
  return [
    { id: "tickets", value: l.ticketsThisMonth, sign: 1 },
    { id: "lodge", value: l.lodgeIncomeMonth, sign: 1 },
    { id: "lessons", value: l.lessonIncomeMonth, sign: 1 },
    { id: "passes", value: l.passIncomeMonth, sign: 1 },
    { id: "lifts", value: l.liftChargedMonth, sign: -1 },
    { id: "runs", value: l.runCostMonth, sign: -1 },
    { id: "bus", value: l.busChargedMonth, sign: -1 },
    { id: "buildings", value: l.buildingsSoFar, sign: -1 },
    { id: "wages", value: l.wagesSoFar, sign: -1 },
  ];
}

export function netRowsTotal(l: ResortLive): number {
  let s = 0;
  for (const r of netRows(l)) s += r.sign * (Number.isFinite(r.value) && r.value > 0 ? r.value : 0);
  return s;
}

/** Which of the Net's income rows the Finances view draws. Tickets: always. Lodge spend, lessons and passes sold: under their
 *  own conditions OR while the row holds money this month. So a hidden row is always 0 and the drawn rows still add up
 *  to the Net. (Lessons also show at 0 while there is no ski school. The view adds that.) */
export function netRowShown(id: NetRowId, l: ResortLive, city: CityStats): boolean {
  switch (id) {
    case "lodge": return lodgeSpendShown(l) || l.lodgeIncomeMonth > 0;
    case "lessons": return lessonsShown(l) || l.lessonIncomeMonth > 0;
    case "passes": return passesShown(l, city) || l.passIncomeMonth > 0;
    case "bus": return l.depots > 0 || l.busChargedMonth > 0;
    default: return true;
  }
}

export function buildingsAndWages(live: ResortLive): number {
  return live.buildingsMonth < 0 || live.wagesMonth < 0 ? -1 : live.buildingsMonth + live.wagesMonth;
}

export function hectares(m2: number): number {
  return Number.isFinite(m2) && m2 > 0 ? m2 / 10000 : 0;
}

export type LastMonthMark = "full" | "partial" | "none";

/** A whole month counted -> "full". A since-load month C# sent (>= 0 while !lastMonthFull)
 *  -> "partial" (the Guests view says "(partial)"). Otherwise "none" ("–", GUESTS_LAST_NA). */
export function lastMonthMark(l: ResortLive | null): LastMonthMark {
  if (l && l.lastMonthFull) return "full";
  return l && l.guestsLastMonth >= 0 ? "partial" : "none";
}

export type SkiDayLine = "unknown" | "off" | "signOff" | "noRun" | "noLodge" | "night" | "roomRuns" | "roomLodges" | "room" | "full";

/** The Guests view's one ski-day state line. Room splits by the limit that binds.
 *  An unknown state or a missing row is "unknown" ("–"). */
export function skiDayLine(l: ResortLive | null): SkiDayLine {
  if (!l) return "unknown";
  switch (l.skiDaysState) {
    case SKI_DAY_STATE.Off: return "off";
    case SKI_DAY_STATE.SignOff: return "signOff";
    case SKI_DAY_STATE.NoRun: return "noRun";
    case SKI_DAY_STATE.NoLodge: return "noLodge";
    case SKI_DAY_STATE.Night: return "night";
    case SKI_DAY_STATE.Full: return "full";
    case SKI_DAY_STATE.Room:
      return l.skiDaysLimit === SKI_DAY_LIMIT.Runs ? "roomRuns" : l.skiDaysLimit === SKI_DAY_LIMIT.Lodges ? "roomLodges" : "room";
    default: return "unknown";
  }
}

export type SkiDayTip = "off" | "noLodge" | "runs" | "lodges" | null;

/** The state line's tooltip: why it is off, what a ski-day lodge needs, or which limit binds
 *  (Room and Full). null = no tooltip (SignOff, NoRun, Night: the band or the line says why). */
export function skiDayTip(l: ResortLive | null): SkiDayTip {
  if (!l) return null;
  switch (l.skiDaysState) {
    case SKI_DAY_STATE.Off: return "off";
    case SKI_DAY_STATE.NoLodge: return "noLodge";
    case SKI_DAY_STATE.Room: case SKI_DAY_STATE.Full:
      return l.skiDaysLimit === SKI_DAY_LIMIT.Runs ? "runs" : l.skiDaysLimit === SKI_DAY_LIMIT.Lodges ? "lodges" : null;
    default: return null;
  }
}

/** Walk class = sent - car - far without a car (never below 0). -1 when sent is unknown. The three classes
 *  partition Sent, so this equals the simulation's walk count. */
export function skiDayWalk(l: ResortLive | null): number {
  if (!l || !(l.skiDaysSent >= 0)) return -1;
  return Math.max(0, l.skiDaysSent - Math.max(0, l.skiDaysSentCar | 0) - Math.max(0, l.skiDaysSentNoCar | 0));
}

export type TripPlannerNote = "none" | "known" | "knownOff" | "unknown" | "unknownOff";

/** The Guests view's trip-planner note. Rules:
 *  - "" (the game's trip systems all on), null, a missing field (an older DLL) or a non-string -> "none" (no line).
 *  - TRIP_PLANNER_UNKNOWN ("?": a system is off, no known mod loaded) -> "unknown".
 *  - Any other label (a known mod's name) -> "known".
 *  - "Off" when tripPlannerBlocksSkiDays is exactly true (the game's LeisureSystem or CitizenBehaviorSystem is off: ski days
 *    do not arm, ArmVerdict.SystemOff).
 *  A label only: nothing else in the hub reads it. */
export function tripPlannerNote(city: CityStats | null | undefined): TripPlannerNote {
  const label = city ? city.tripPlanner : undefined;
  if (!city || typeof label !== "string" || label === "") return "none";
  const off = city.tripPlannerBlocksSkiDays === true;
  if (label === TRIP_PLANNER_UNKNOWN) return off ? "unknownOff" : "unknown";
  return off ? "knownOff" : "known";
}


/** Score bar (0-200): integer flex-grow of [fill, ghost, rest]. Unknown attraction -> [0, 0, 200]. */
export function scoreBarParts(attraction: number, full: number): [number, number, number] {
  const clamp = (x: number): number => Math.min(200, Math.max(0, Math.round(x)));
  if (!(attraction >= 0)) return [0, 0, 200];
  const a = clamp(attraction);
  const f = Math.max(a, full >= 0 ? clamp(full) : a);
  return [a, f - a, 200 - f];
}

/** A guest bar on a shared scale: integer flex-grow [fill, rest]. value < 0 -> [0, 1] (empty track). */
export function guestBarParts(value: number, max: number): [number, number] {
  const m = Math.max(1, Math.round(max));
  if (!(value >= 0)) return [0, 1];
  const v = Math.min(m, Math.round(value));
  return [v, m - v];
}

/** The smallest full-bar scale, so 2 guests no longer fill the bar. */
export const GUEST_BAR_MIN_SCALE = 50;

/** Ladder 50, 100, 200, 500, 1000, … (× 2, × 2, × 2.5 repeating). The result is the smallest step >= both months (an
 *  unknown, negative or non-finite month counts 0). It is capped near 1e9. */
export function guestBarScale(thisMonth: number, lastMonth: number): number {
  const a = Number.isFinite(thisMonth) && thisMonth > 0 ? thisMonth : 0;
  const b = Number.isFinite(lastMonth) && lastMonth > 0 ? lastMonth : 0;
  const m = Math.max(a, b);
  let s = GUEST_BAR_MIN_SCALE, k = 0;
  while (s < m && s < 1e9) { s = k === 2 ? s * 2.5 : s * 2; k = (k + 1) % 3; }
  return s;
}


export type LiftLine2 = "detail" | "detailOne" | "detailNone" | "sectionMissing" | "unfinished"
  | "stationsPair" | "stationsBaseOnly" | "stationsTopOnly";

/** Line 2 of a lift row. `partnerPlaced`: a lone station of the
 *  other kind is placed in the same area (loneStations). The census never puts both stations on one
 *  StationsOnly row, so the code finds "both stations placed" across rows, as in C# HubProblems.Build. */
export function liftLine2(row: LiftRow, partnerPlaced = false): LiftLine2 {
  if (row.status === LIFT_STATUS.SectionMissing) return "sectionMissing";
  if (row.status === LIFT_STATUS.Unfinished) {
    if (row.state !== LIFT_STATE.StationsOnly) return "unfinished";
    if (row.hasBase && row.hasTop) return "stationsPair";
    if ((row.hasBase || row.hasTop) && partnerPlaced) return "stationsPair";
    return row.hasBase ? "stationsBaseOnly" : row.hasTop ? "stationsTopOnly" : "unfinished";
  }
  const n = row.runsServed | 0;
  return n <= 0 ? "detailNone" : n === 1 ? "detailOne" : "detail";
}

/** Lone stations of an area's lift rows: a row of state Unfinished or StationsOnly with
 *  exactly one of hasBase / hasTop. The C# LiftNoPair / HubProblems pairing reads the same rows. */
export function loneStations(rows: readonly LiftRow[] | null | undefined): { base: boolean; top: boolean } {
  let base = false, top = false;
  for (const r of rows ?? []) {
    if (!r || (r.state !== LIFT_STATE.StationsOnly && r.state !== LIFT_STATE.Unfinished)) continue;
    if (r.hasBase && !r.hasTop) base = true;
    else if (r.hasTop && !r.hasBase) top = true;
  }
  return { base, top };
}

/** The distinct lift types (LiftRow.type, "" and non-strings skipped) of some rows, first-seen order. The
 *  Lifts view shows a type word per row only when an area's rows span two or more. */
export function liftTypesOf(rows: readonly LiftRow[] | null | undefined): string[] {
  const out: string[] = [];
  for (const r of rows ?? []) {
    if (!r || typeof r.type !== "string" || r.type === "") continue;
    if (out.indexOf(r.type) < 0) out.push(r.type);
  }
  return out;
}

export function partnerPlaced(row: LiftRow, lone: { base: boolean; top: boolean }): boolean {
  if (row.hasBase && !row.hasTop) return lone.top;
  if (row.hasTop && !row.hasBase) return lone.base;
  return false;
}

export type LiftCostReason = "noPower" | "sectionMissing" | "unfinished" | "noOpenRun" | null;

/** Why a lift is billed at 25 % (the lift tooltip's line, which LiftTable.tsx maps to LIFT_COST_REDUCED[*]). null
 *  while billed in full. Unfinished (a missing section is unfinished) is the status's own reason. Any complete
 *  lift is billed reduced only for no counted run in its ski area (LiftBilling.NoOpenRun).
 *  Power does not lower the bill (C# SkiMoneyRules.LiftUpkeepBillingOf), so a No power row billed reduced is billed
 *  for no counted run as well. It keeps "noPower", and LIFT_COST_REDUCED[NoPower] names the no-open-run reason. */
export function liftCostReason(row: LiftRow): LiftCostReason {
  if (!row || !row.billedReduced) return null;
  if (row.status === LIFT_STATUS.NoPower) return "noPower";
  if (row.status === LIFT_STATUS.SectionMissing) return "sectionMissing";
  if (row.status === LIFT_STATUS.Unfinished) return "unfinished";
  return "noOpenRun";
}

export type LiftTone = "normal" | "warn" | "dim";

export function liftMark(status: number): { ring: boolean; tone: LiftTone } {
  switch (status) {
    case LIFT_STATUS.Running: return { ring: false, tone: "normal" };
    case LIFT_STATUS.NoPower: return { ring: false, tone: "warn" };
    case LIFT_STATUS.ChairsStopped: return { ring: false, tone: "dim" };
    case LIFT_STATUS.Closed: return { ring: false, tone: "dim" }; // held closed (no open run), like chairs stopped
    case LIFT_STATUS.SectionMissing: return { ring: true, tone: "warn" };
    default: return { ring: true, tone: "dim" };
  }
}

/** The Lifts tile is built / drawn: liftsComplete of every census
 *  row of the area (complete lifts, unfinished lines, lone stations). null = unknown (no live row, or liftsComplete -1). */
export function liftsTile(row: ResortRow, live: ResortLive | null): { built: number; drawn: number } | null {
  if (!live || !(live.liftsComplete >= 0)) return null;
  const drawn = Math.max(row.liftRows | 0, live.liftsComplete);
  return { built: live.liftsComplete, drawn };
}

/** Unfinished lines and lone stations of an area: lift rows minus complete lifts (never below 0).
 *  0 when the live row does not know the complete count (no live row, or
 *  liftsComplete -1: the Not linked entry, or a sign without a score result). The hub shows no number
 *  unless it is real. */
export function unfinishedLifts(row: ResortRow, live: ResortLive | null): number {
  if (!live || !(live.liftsComplete >= 0)) return 0;
  return Math.max(0, (row.liftRows | 0) - live.liftsComplete);
}


/** C# SkiResortHubUISystem.ReasonKeyAlreadyBuilt: build[].reason. C# sends it while the SHOWN sign's InstalledUpgrade buffer already holds the
 *  item's ForbidMultiple prefab (the school), switched off or not (UpgradeConflict, Deleted / Temp skipped). */
export const REASON_ALREADY_BUILT = "SkiResort.Hub.REASON[AlreadyBuilt]";

/** The shown sign has a ski school: the school tile carries the AlreadyBuilt lock (C# sends it for the shown sign only). */
export function schoolBuiltOf(build: readonly BuildItem[] | null | undefined): boolean {
  for (const b of build ?? []) if (b && b.id === "school" && b.locked && b.reason === REASON_ALREADY_BUILT) return true;
  return false;
}

/** The ski school of a sign, for the rows whose 0 has a cause. The states:
 *  - "on" = its value counts (schoolValue > 0).
 *  - "noGreen" = runs count but no green one and no cat track (the sign's PROBLEM.SchoolNoGreenRun row, C# HubProblemCode 25, sent
 *    only while RunsCounted > 0, SkiResortStatsSystem.cs:2700; a counted cat track serves the school since T211).
 *  - "noOpenGreen" = the same, while a green run or a cat track that would count exists but none is open (T212: row 25's detail 1, or
 *    without the row ResortLive.bandsWhenOpen).
 *  - "waiting" = built, no run counts at all (live.summer: SkiResortScore.cs
 *    :414 summer ⇔ RunsCounted == 0, so SchoolRule gives +0, SkiResortScore.cs:467).
 *    Row 25 is a dim band row: the band keeps at most 8 (HubRules.MaxBandRows), so it can be cut. The score's own green bit
 *    (varietyBands & VARIETY_BAND green, C# Result.VarietyBands = the GreenCounted flag, SkiResortScore.cs:416) decides too (T186: a
 *    full band made a school with no counted green run read "Deactivated"). Without row 25 the fallback cannot tell a switched-off
 *    school beside an open cat track from a missing run (rare: only a full band cuts the row).
 *  - "off" = built, runs count, a green run counts, still 0. The only way is SchoolActive false, the building switched off
 *    (BuildingOption.Inactive, SkiResortAttractivenessSystem.cs:1445). varietyBands unknown (< 0) falls back to "off".
 *  - "closed" = built and closed by the resort for the off-season (live.schoolOffSeason, contract 53, T238). Dim, never a fault.
 *  - "none" = no school.
 *  - "unknown" = no live row or schoolValue < 0.
 *  `built` = schoolBuiltOf(build) for the SAME shown sign. It reads only C#'s fields: no new contract field.
 *  A school on a sign with no runs must not read "No ski school" (no row 25 there). */
export type SchoolState = "on" | "noGreen" | "noOpenGreen" | "waiting" | "off" | "closed" | "none" | "unknown";
export function schoolState(l: ResortLive | null, problems: readonly ProblemRow[] | null | undefined, sign: Entity | null | undefined,
  built = false): SchoolState {
  if (!l || !(l.schoolValue >= 0)) return "unknown";
  if (l.schoolValue > 0) return "on";
  for (const p of problems ?? []) {
    if (p && p.code === PROBLEM.SchoolNoGreenRun && sameEntity(p.sign, sign)) return p.detail === 1 ? "noOpenGreen" : "noGreen";
  }
  if (!built) return "none";
  if (l.schoolOffSeason === true) return "closed";   // T238: the resort closed it for the off-season: not a fault
  if (l.summer) return "waiting";
  const bands = typeof l.varietyBands === "number" ? l.varietyBands : -1;
  if (!(bands >= 0) || (bands & VARIETY_BAND.Green) !== 0) return "off";
  const whenOpen = typeof l.bandsWhenOpen === "number" ? l.bandsWhenOpen : -1;
  return whenOpen >= 0 && (whenOpen & (VARIETY_BAND.Green | VARIETY_BAND.CatTrack)) !== 0 ? "noOpenGreen" : "noGreen";
}

/** The School row's flag (HUB_LOC key name) by state. null = no flag ("on" / "unknown"). "waiting" is a state, not a fault: the
 *  row draws it dim (InfoLine flagDim). */
export function schoolFlagOf(s: SchoolState): "noSchool" | "schoolNoGreen" | "schoolNoOpenGreen" | "schoolOff" | "schoolWaiting" | "schoolClosed" | null {
  switch (s) {
    case "none": return "noSchool";
    case "noGreen": return "schoolNoGreen";
    case "noOpenGreen": return "schoolNoOpenGreen";
    case "off": return "schoolOff";
    case "waiting": return "schoolWaiting";
    case "closed": return "schoolClosed";
    default: return null;
  }
}

/**
 * The spacing ladder of a prop row (DECISIONS 601): the snow gun and light sliders' rungs (r3/sliders: RUN_PROP_SPACING_STOPS, Off at the left,
 * then sparse to dense). A value between rungs (a kind's default such as 90 m) sits between them on the track until the player moves it.
 * Range = RunPropPlacerRules.ClampSpacing (20-200 m).
 */
export const RUN_PROP_SPACING_LADDER = [20, 30, 40, 50, 60, 80, 100, 130, 160, 200];

/** r3/sliders: the tree density slider's stops, level 1..8 (bindings GLADE_LEVEL_MIN..MAX). */
export const GLADE_STOPS: readonly number[] = Array.from({ length: GLADE_LEVEL_MAX - GLADE_LEVEL_MIN + 1 }, (_, i) => GLADE_LEVEL_MIN + i);

/** r3/sliders: a spacing slider's stops, left to right: Off, then the ladder from sparse to dense, so right is always "more". */
export const RUN_PROP_SPACING_STOPS: readonly number[] = [RUN_PROP_SPACING_OFF, ...RUN_PROP_SPACING_LADDER.slice().reverse()];

/* ---- icon tags in hub text (icon scheme, hub contract 40) ----------------------------------------------------------------------
 * `<icon:NAME>` stands for a word: its word form is SkiResort.Hub.ICON[NAME] ("green", "open or patchy").
 * `<mark:NAME>` sits beside a word that stays, at the start of the text or of a sentence. It opens a hanging row (icon | text), so it
 * needs no inline layout. Its word form is nothing.
 * NAME is a HUB_ICON key (hubIcons.ts) or, as in vanilla text, a Media/ path. The rules below are pure: iconText.tsx renders them,
 * tools/test-hub-rules.js T42 holds the literal oracles. */
export type IconToken = { kind: "text"; text: string } | { kind: "icon" | "mark"; name: string };

/** The tag grammar (vanilla's own `<icon:Media/...>` form). check-bundle R-icon and check-ste read the same pattern. */
export const ICON_TAG_SOURCE = "<(icon|mark):([A-Za-z0-9_./-]+)>";

/** The text split into text runs and tags, in order. Empty runs are dropped. Not a string gives []. */
export function iconTokens(s: string): IconToken[] {
  const out: IconToken[] = [];
  if (typeof s !== "string") return out;
  const re = new RegExp(ICON_TAG_SOURCE, "g");
  let last = 0;
  let m: RegExpExecArray | null;
  while ((m = re.exec(s)) !== null) {
    if (m.index > last) out.push({ kind: "text", text: s.slice(last, m.index) });
    out.push({ kind: m[1] === "mark" ? "mark" : "icon", name: m[2] });
    last = re.lastIndex;
  }
  if (last < s.length) out.push({ kind: "text", text: s.slice(last) });
  return out;
}

/** The word form: every icon tag becomes its word, every mark tag goes (with the spaces after it). What renderString, the logs and
 *  check-ste see, and what a tooltip shows while the icons are off. */
export function iconWordForm(s: string, word: (name: string) => string): string {
  let out = "";
  let dropSpace = false;
  for (const tk of iconTokens(s)) {
    if (tk.kind === "text") {
      out += dropSpace ? tk.text.replace(/^\s+/, "") : tk.text;
      dropSpace = false;
    } else if (tk.kind === "icon") {
      out += word(tk.name);
      dropSpace = false;
    } else {
      dropSpace = true;
    }
  }
  return out;
}

/** One hanging row: the mark that opens it (null = the text before the first mark) and its tokens, trimmed at both ends. */
export interface IconRow { mark: string | null; tokens: IconToken[] }

function textOf(tk: IconToken): string {
  return tk.kind === "text" ? tk.text : "";
}

function trimTokens(tokens: readonly IconToken[]): IconToken[] {
  const out = tokens.slice();
  while (out.length && out[0].kind === "text" && !textOf(out[0]).trim()) out.shift();
  while (out.length && out[out.length - 1].kind === "text" && !textOf(out[out.length - 1]).trim()) out.pop();
  if (out.length && out[0].kind === "text") out[0] = { kind: "text", text: textOf(out[0]).replace(/^\s+/, "") };
  const n = out.length - 1;
  if (n >= 0 && out[n].kind === "text") out[n] = { kind: "text", text: textOf(out[n]).replace(/\s+$/, "") };
  return out;
}

/** The text cut at every mark. Without a mark: one row with mark null. A blank lead before the first mark is dropped. */
export function iconRows(tokens: readonly IconToken[]): IconRow[] {
  const rows: IconRow[] = [];
  let cur: IconRow = { mark: null, tokens: [] };
  for (const tk of tokens) {
    if (tk.kind === "mark") {
      const lead = trimTokens(cur.tokens);
      if (cur.mark !== null || lead.length) rows.push({ mark: cur.mark, tokens: lead });
      cur = { mark: tk.name, tokens: [] };
    } else cur.tokens.push(tk);
  }
  const rest = trimTokens(cur.tokens);
  if (cur.mark !== null || rest.length) rows.push({ mark: cur.mark, tokens: rest });
  return rows;
}

/** A piece of a wrapping paragraph: free text, or a glued run that must not break (an icon, its neighbor icons, and the next word or
 *  punctuation), so an icon never starts a line alone and "●." never splits. */
export type IconPiece = { kind: "text"; text: string } | { kind: "glue"; items: IconToken[] };

export function iconPieces(tokens: readonly IconToken[]): IconPiece[] {
  const out: IconPiece[] = [];
  let i = 0;
  while (i < tokens.length) {
    const tk = tokens[i];
    if (tk.kind === "text") {
      out.push({ kind: "text", text: tk.text });
      i++;
      continue;
    }
    const items: IconToken[] = [];
    while (i < tokens.length && tokens[i].kind !== "text") items.push(tokens[i++]);
    // ASCII spaces only: a U+00A0 of unitNbsp ("376 m") keeps a number and its unit inside the one glued word.
    const m = i < tokens.length ? /^[ \t\r\n]*[^ \t\r\n]+/.exec(textOf(tokens[i])) : null;
    if (m) {
      const text = textOf(tokens[i]);
      items.push({ kind: "text", text: m[0] });
      out.push({ kind: "glue", items });
      if (text.length > m[0].length) out.push({ kind: "text", text: text.slice(m[0].length) });
      i++;
    } else out.push({ kind: "glue", items });
  }
  return out;
}

/** The view pager's page after a re-page (fit.tsx useViewPager, 1.0.4 price edges: an edit made a row grow, appear or go, and the
 *  same page index then showed other rows, "page jumps around a lot"). `pageOf` = each row's page (rising), `asked` = the page the
 *  state holds, `anchorRow` = the row that was first on the shown page at the last pass (-1 = none), `anchorPage` = the page it was
 *  first on. While the player has not paged since (asked = anchorPage), the view shows the page that holds that row now, so the rows
 *  in view stay in view. Else (the player paged, or no anchor) the asked page, within the count. */
export function anchoredPage(pageOf: readonly number[], asked: number, anchorRow: number, anchorPage: number): number {
  const n = pageOf.length > 0 ? pageOf[pageOf.length - 1] + 1 : 1;
  if (anchorRow >= 0 && anchorRow < pageOf.length && anchorPage === asked) return pageOf[anchorRow];
  return Math.min(Math.max(0, asked), n - 1);
}

/** U+00A0, the no-break space of the inline rescue (iconInlinePieces). */
export const NBSP = " ";

/**
 * iconPieces for Gameface's inline layout (`<p cohinline>`), at render time only: the locale string, check-ste and the word form keep
 * their ASCII spaces. Gameface drops the leading ASCII space of a text run that follows an inline element (spike S0,
 * field/2026-11-08/icons-pass/RESULTS.md: "andone", "runis", "[green]runis", "or" glued to the next diamond). A trailing space before an
 * inline element survives (the S0 probe replaced only the leading spaces and read right), so it stays ASCII and stays a break point.
 * - Inside a glue run, a text item after an icon starts with one U+00A0 instead of its spaces (the run is nowrap anyway).
 * - A text piece after a glue run gives its leading spaces to the END of that run, as one U+00A0. Gameface may break a line at the element
 *   boundary (the probe showed " Otherwise" with a visible lead space at text scale 1.25 when the NBSP led the next run), so the space now
 *   ends the line it belongs to, where it shows nothing. A text piece of spaces only (between two glue runs) disappears.
 */
export function iconInlinePieces(tokens: readonly IconToken[]): IconPiece[] {
  const out: IconPiece[] = [];
  for (const pc of iconPieces(tokens)) {
    if (pc.kind === "glue") {
      const items: IconToken[] = [];
      for (const it of pc.items) {
        const prev = items.length ? items[items.length - 1] : null;
        items.push(it.kind === "text" && prev !== null && prev.kind !== "text" ? { kind: "text", text: it.text.replace(/^ +/, NBSP) } : it);
      }
      out.push({ kind: "glue", items });
      continue;
    }
    const last = out.length ? out[out.length - 1] : null;
    if (last !== null && last.kind === "glue" && /^ /.test(pc.text)) {
      const n = last.items.length - 1;
      const tail = last.items[n];
      if (tail.kind === "text") last.items[n] = { kind: "text", text: tail.text + NBSP };
      else last.items.push({ kind: "text", text: NBSP });
      const rest = pc.text.replace(/^ +/, "");
      if (rest) out.push({ kind: "text", text: rest });
    } else out.push(pc);
  }
  return out;
}

/**
 * The units the hub writes after a number: in locale strings ("600 m", "+1 %", "{RADIUS} km", "3.3 °C (6 °F)") and in vanilla's
 * LocalizedNumber output, which puts an ASCII space between the number and the unit ("376 m", "1.2 km", "3 ha"; installed
 * index.js@503140 turns only a digit-space-digit into U+00A0). Longer forms first, so "km²" wins over "km" and "°C" over "°".
 */
export const UNIT_WORDS: readonly string[] = ["km²", "km", "cm", "m²", "m/s", "m", "ha", "%", "ft", "mi", "in", "°C", "°F", "°"];

/** The number-unit space: a digit, ASCII spaces, then a UNIT_WORDS entry that no letter or digit continues ("2 months" is no unit). */
export const UNIT_GAP_SOURCE = `(\\d)[ ]+(?=(?:${UNIT_WORDS.map((u) => u.replace(/[/\\^$*+?.()|[\]{}]/g, "\\$&")).join("|")})(?![A-Za-z0-9²]))`;
const UNIT_GAP = new RegExp(UNIT_GAP_SOURCE, "g");

/**
 * Numbers stay with their units (round 624, field/2026-11-09/icons2-pass "Unexpected": at text scale 1.25 the Vertical tooltip broke
 * "600 / m" and "376 / m."). At render time only, the space between a number and its unit becomes U+00A0, the no-break space vanilla
 * itself uses inside numbers (index.js@503140). Every hub text passes here: useT (l10n.ts), LocText, Num / SignedNum (parts.tsx) and
 * IconText (iconText.tsx). Locale strings, check-ste, export-locale and the word form keep ASCII spaces: never write U+00A0 into a string.
 * Idempotent. A value that is not a string comes back unchanged.
 */
export function unitNbsp(s: string): string {
  return typeof s === "string" && s.indexOf(" ") >= 0 ? s.replace(UNIT_GAP, `$1${NBSP}`) : s;
}

/** An item of a one-line structured row (flex, never wraps). `gap` = a space stood before it in the text (a left margin of one
 *  space: a flex text box drops its edge spaces). */
export type IconLineItem = { kind: "text"; text: string; gap: boolean } | { kind: "icon"; name: string; gap: boolean };

export function iconLineItems(tokens: readonly IconToken[]): IconLineItem[] {
  const out: IconLineItem[] = [];
  let space = false;
  for (const tk of tokens) {
    if (tk.kind === "text") {
      const text = tk.text.trim();
      if (text) {
        out.push({ kind: "text", text, gap: out.length > 0 && (space || /^\s/.test(tk.text)) });
        space = /\s$/.test(tk.text);
      } else space = space || tk.text.length > 0;
    } else {
      out.push({ kind: "icon", name: tk.name, gap: out.length > 0 && space });
      space = false;
    }
  }
  return out;
}

/* ---- the receipt's missing lines (hub contract 40). Every number is C#'s (D10): the rules only pick what to show. ------------- */

/** The variety needs not met (VARIETY_BAND bits cleared), in the order green, blue, black. [] = nothing to say: the bonus applies,
 *  the bits are unknown (-1 or an older contract), or no run counts at all (summer: the Runs row already says why). */
export type VarietyNeed = "green" | "blue" | "black";
export function varietyMissing(l: ResortLive | null | undefined): VarietyNeed[] {
  if (!l || !(l.varietyBands >= 0) || l.summer) return [];
  const b = l.varietyBands | 0;
  if ((b & VARIETY_BAND.All) === VARIETY_BAND.All) return [];
  const out: VarietyNeed[] = [];
  if (!(b & VARIETY_BAND.Green)) out.push("green");
  if (!(b & VARIETY_BAND.Blue)) out.push("blue");
  if (!(b & VARIETY_BAND.Black)) out.push("black");
  return out;
}

/** T212: a need of varietyMissing whose runs exist (they count while open, ResortLive.bandsWhenOpen) but none is open: the line says
 *  "open green run". False when bandsWhenOpen is unknown (-1). */
export function varietyClosed(l: ResortLive | null | undefined, need: VarietyNeed): boolean {
  if (!l || !(l.bandsWhenOpen >= 0)) return false;
  const bit = need === "green" ? VARIETY_BAND.Green : need === "blue" ? VARIETY_BAND.Blue : VARIETY_BAND.Black;
  return ((l.bandsWhenOpen | 0) & bit) !== 0 && ((l.varietyBands | 0) & bit) === 0;
}

/** Meters to the full vertical bonus, or -1 = no line (unknown, or under half a meter: the bonus is full). */
export function verticalShort(l: ResortLive | null | undefined): number {
  if (!l || !(l.verticalShortM >= 0.5)) return -1;
  return l.verticalShortM;
}

/** The Lifts row's missing counts: complete lifts without power and unfinished lines. null = nothing missing or unknown. */
export function liftsMissing(row: ResortRow, l: ResortLive | null | undefined): { noPower: number; unfinished: number } | null {
  if (!l || !(l.liftsComplete >= 0) || !(l.liftsCounted >= 0)) return null;
  const noPower = Math.max(0, (l.liftsComplete | 0) - (l.liftsCounted | 0));
  const unfinished = unfinishedLifts(row, l);
  return noPower + unfinished > 0 ? { noPower, unfinished } : null;
}

/** The Runs row's missing counts: closed, patchy, and member runs no lift serves. null = every run open and served, or unknown. */
export function runsMissing(row: ResortRow, l: ResortLive | null | undefined, areaRuns: readonly RunRow[] | null | undefined):
  { closed: number; patchy: number; noLift: number } | null {
  if (!l || !(l.runsOpen >= 0) || !(l.runsPartly >= 0)) return null;
  let noLift = 0;
  for (const r of areaRuns ?? []) if (r && r.member && !r.served) noLift++;
  const closed = closedRuns(row, l);
  const patchy = Math.max(0, l.runsPartly | 0);
  return closed + patchy + noLift > 0 ? { closed, patchy, noLift } : null;
}

/** The cap of the other lodges' extras in winter (C# SkiResortScore extra cap, pinned in RECEIPT_TIP[LodgeBase] by CG-T5). */
export const LODGE_EXTRA_CAP = 24;

/** The Lodge base row's extras below the cap: the extras, or -1 = no line (no lodge, summer, unknown, or at the cap). */
export function lodgeExtraShort(l: ResortLive | null | undefined): number {
  if (!l || l.summer || !(l.lodgesActive >= 1) || !(l.lodgeExtra >= 0) || l.lodgeExtra >= LODGE_EXTRA_CAP - 0.05) return -1;
  return l.lodgeExtra;
}

/** The Efficiency row sits on its floor (C# holds the score's efficiency between 25 % and 100 %). */
export const EFFICIENCY_FLOOR_PCT = 25;
export function efficiencyAtFloor(l: ResortLive | null | undefined): boolean {
  return !!l && l.efficiencyPct >= 0 && l.efficiencyPct <= EFFICIENCY_FLOOR_PCT;
}

/** DECISIONS 676 / 677: an Attraction receipt row whose lever the player has maxed. Its tooltip then says so (RECEIPT_TIP_MAX[...]) instead
 *  of the "To <goal>, <action>." line. Unknown fields (-1) are never maxed.
 *  - lodgeBase: the best lodge is a top tier (summit or grand, B 40 in C# SkiCatalog.Lodges) AND the other lodges' extras are at the
 *    cap (LODGE_EXTRA_CAP). Winter only: the summer extras are scaled per lodge (SkiResortScore LodgeLadder), so they cannot show the cap.
 *  - variety: all three VARIETY_BAND bits (green, blue, black or harder).
 *  - vertical: under half a meter to the full bonus (the line verticalShort draws nothing there).
 *  - school: the school's term at its score value (C# SchoolRule.ScoreValueOf: 0 or SkiCatalog.School.ScoreValue 12, one per area).
 *  - efficiency: 100 % (the display rounds, so from 99.5). */
export type ReceiptLever = "lodgeBase" | "variety" | "vertical" | "school" | "efficiency";
export const TOP_LODGE_TIERS: readonly string[] = ["lodgeSummit", "lodgeGrand"];
export const SCHOOL_SCORE = 12;
export function receiptMaxed(l: ResortLive | null | undefined, row: ReceiptLever): boolean {
  if (!l) return false;
  switch (row) {
    case "lodgeBase": return !l.summer && TOP_LODGE_TIERS.indexOf(l.lodgeBest) >= 0 && l.lodgeExtra >= LODGE_EXTRA_CAP - 0.05;
    case "variety": return l.varietyBands >= 0 && ((l.varietyBands | 0) & VARIETY_BAND.All) === VARIETY_BAND.All;
    case "vertical": return l.verticalShortM >= 0 && l.verticalShortM < 0.5;
    case "school": return l.schoolValue >= SCHOOL_SCORE - 0.05;
    case "efficiency": return l.efficiencyPct >= 99.5;
    default: return false;
  }
}

/** The School row's missing line (HUB_LOC key name) by state. null = no line. */
export function schoolMissingOf(s: SchoolState): "receiptMissingSchoolNone" | "receiptMissingSchoolGreen" | "receiptMissingSchoolWaiting" | null {
  switch (s) {
    case "none": case "off": return "receiptMissingSchoolNone";
    case "noGreen": case "noOpenGreen": return "receiptMissingSchoolGreen";
    case "waiting": return "receiptMissingSchoolWaiting";
    default: return null;   // "closed" too: a state, not a missing part
  }
}

/** One IconText of a tooltip, as the tooltip key sees it: its text with tags (the English fallback: tags are the same in every locale)
 *  and whether it is a one-line row (`line`, a missing-line part). */
export interface IconKeySource { text: string; line: boolean }

/** At most this many icon | word pairs per tooltip key row (the key never needs flex-wrap, which Gameface lacks). */
export const ICON_KEY_PER_ROW = 3;

/**
 * The tooltip key (legend at the point of use, DECISIONS 620): the `<icon:NAME>` names a tooltip shows as images, once each, in legend
 * order, cut into rows of ICON_KEY_PER_ROW. Marks are left out (their row text already says the word). Mode "words" shows no image, so
 * no key. Mode "flex" shows images only in one-line rows, so only those count. A name without a legend row is left out (T42 forbids it
 * in text anyway).
 */
export function iconKeyRows(sources: readonly IconKeySource[], mode: "inline" | "flex" | "words", order: readonly string[]): string[][] {
  if (mode === "words") return [];
  const seen = new Set<string>();
  for (const src of sources) {
    if (!src || (mode === "flex" && !src.line)) continue;
    for (const tk of iconTokens(src.text)) if (tk.kind === "icon" && order.indexOf(tk.name) >= 0) seen.add(tk.name);
  }
  const names = order.filter((n) => seen.has(n));
  const rows: string[][] = [];
  for (let i = 0; i < names.length; i += ICON_KEY_PER_ROW) rows.push(names.slice(i, i + ICON_KEY_PER_ROW));
  return rows;
}

/* ---- the key row width budget (round 624). At text scale 1.25 the Variety key row 2 (double-black, open or patchy) measured
 * scrollWidth 730 px in a 720 px box (4K, field/2026-11-09/icons2-pass B4). Gameface has no flex-wrap, and a measure-then-repack
 * would lag a drawn frame and change the balloon after it opened. So the key packs its pairs by an ESTIMATE, at mount, from the
 * text scale alone: no size read, no second render. The estimate leans wide (B4 row 2 at 1.25: 740 px estimated, 730 px measured), so a row
 * breaks a little early rather than late. ------------------------------------------------------------------------------------- */

/** Overpass-Regular advance widths in em: the game's --fontFamily is Overpass, then Noto Sans (index.css :root); the files ship in
 *  Cities2_Data/Content/Game/UI/Fonts (hmtx / unitsPerEm 1000). Lowercase and the punctuation of the icon words. */
const GLYPH_EM: Readonly<Record<string, number>> = {
  a: 0.53, b: 0.56, c: 0.5, d: 0.56, e: 0.54, f: 0.33, g: 0.56, h: 0.56, i: 0.25, j: 0.25, k: 0.53, l: 0.26, m: 0.85, n: 0.56, o: 0.56,
  p: 0.56, q: 0.56, r: 0.39, s: 0.47, t: 0.37, u: 0.56, v: 0.51, w: 0.72, x: 0.52, y: 0.53, z: 0.51, " ": 0.24, "-": 0.4, ".": 0.23,
  ",": 0.23, "(": 0.34, ")": 0.34, "%": 0.87, "/": 0.49, "'": 0.21,
};
/** Any other character (capitals, digits, other scripts): wider than most lowercase, so the estimate stays on the safe side. */
const GLYPH_EM_OTHER = 0.8;

/** The estimated width of a one-line text in em of its own font. */
export function textEm(s: string): number {
  let em = 0;
  for (const ch of String(s ?? "")) em += GLYPH_EM[ch] ?? GLYPH_EM_OTHER;
  return em;
}

/** .tipLines max-width (hub.module.scss): the box a tooltip key row must fit, in rem. */
export const TIP_MAX_REM = 360;
/** The share of TIP_MAX_REM a key row may fill by the estimate. */
export const ICON_KEY_FILL = 0.98;
/** .iconKeyGap (0.8em between pairs) and .iconGap (0.3em between an icon and its word). */
export const ICON_KEY_GAP_EM = 0.8;
const ICON_WORD_GAP_EM = 0.3;

/** The tooltip font in rem: vanilla --fontSizeS of index.css :root, calc(14rem * var(--fontScale) + var(--fontScaleChange) * 1.1 * 14rem)
 *  (.tooltip-base sets font-size: var(--fontSizeS); the 16rem form belongs to .menu-theme, the main menu). */
export function tipFontRem(scale: number): number {
  const s = Number.isFinite(scale) && scale > 0 ? scale : 1;
  return 14 * s + (s - 1) * 1.1 * 14;
}

/** One key pair in em: the icon (.iconImg 1.3em, .iconImgWide 1.95em, damped by the text scale) + 0.3em + its word. */
export function iconKeyPairEm(word: string, wide: boolean, scale: number): number {
  const s = Number.isFinite(scale) && scale > 0 ? scale : 1;
  return (wide ? 1.95 : 1.3) * (1 - (s - 1) / 2) + ICON_WORD_GAP_EM + textEm(word);
}

/** The em a key row may take at a text scale. */
export function iconKeyBudgetEm(scale: number): number {
  return TIP_MAX_REM * ICON_KEY_FILL / tipFontRem(scale);
}

/**
 * The key rows re-packed to fit: the names in the same order, at most ICON_KEY_PER_ROW per row, and a new row before a pair that would
 * take the row past `budgetEm`. A pair wider than the budget alone gets its own row (its word never wraps). `pairEm` gives a pair's
 * width in em. Pure: the same names and scale always give the same rows, so the key never re-lays out after it opened.
 */
export function iconKeyFit(rows: readonly (readonly string[])[], pairEm: (name: string) => number, budgetEm: number): string[][] {
  const out: string[][] = [];
  let cur: string[] = [];
  let width = 0;
  for (const name of rows.reduce<string[]>((a, r) => a.concat(r), [])) {
    const w = pairEm(name);
    const next = cur.length ? width + ICON_KEY_GAP_EM + w : w;
    if (cur.length && (cur.length >= ICON_KEY_PER_ROW || next > budgetEm)) {
      out.push(cur);
      cur = [name];
      width = w;
    } else {
      cur.push(name);
      width = next;
    }
  }
  if (cur.length) out.push(cur);
  return out;
}

/** T177: the Mountain fold's fact sheet, the numbers a resort's web page or paper trail map lists. Inputs are one area's rows (runsOf /
 *  liftsOf of the same sign). Pistes = runs that are not cat tracks. Longest = the longest piste by lengthM (horizontal; ties: the first in
 *  C#'s order), "" and -1 without one. Snowmaking / night = the pistes whose feature is on (gunSpacingM / lightSpacingM > 0); the share is of
 *  the piste plan area (-1 without piste area). Lifts = complete rows only (LIFT_STATE Running or Static), counted per type in
 *  LIFT_TYPE_IDS order (an unknown type counts in `lifts` only). Nothing is recomputed that C# sends as a total (vertical, area, capacity). */
export interface MountainFacts {
  pistes: number;
  catTracks: number;
  longestM: number;
  longestName: string;
  snowmakingPct: number;
  nightRuns: number;
  lifts: number;
  liftsByType: Array<{ type: string; count: number }>;
}
export function mountainFacts(runs: readonly RunRow[] | null | undefined, lifts: readonly LiftRow[] | null | undefined,
  typeIds: readonly string[]): MountainFacts {
  let pistes = 0, catTracks = 0, longestM = -1, longestName = "", nightRuns = 0, area = 0, gunArea = 0;
  for (const r of runs ?? []) {
    if (!r) continue;
    if (r.kind === RUN_KIND.CatTrack) { catTracks++; continue; }
    pistes++;
    const a = r.areaM2 > 0 ? r.areaM2 : 0;
    area += a;
    if (r.gunSpacingM > 0) gunArea += a;
    if (r.lightSpacingM > 0) nightRuns++;
    if (r.lengthM > longestM) { longestM = r.lengthM; longestName = r.name ?? ""; }
  }
  const counts = new Map<string, number>();
  let complete = 0;
  for (const l of lifts ?? []) {
    if (!l || (l.state !== LIFT_STATE.Running && l.state !== LIFT_STATE.Static)) continue;
    complete++;
    counts.set(l.type, (counts.get(l.type) ?? 0) + 1);
  }
  const liftsByType: Array<{ type: string; count: number }> = [];
  for (const id of typeIds) { const n = counts.get(id) ?? 0; if (n > 0) liftsByType.push({ type: id, count: n }); }
  return {
    pistes, catTracks, longestM, longestName, nightRuns, lifts: complete, liftsByType,
    snowmakingPct: area > 0 ? Math.round((100 * gunArea) / area) : -1,
  };
}
