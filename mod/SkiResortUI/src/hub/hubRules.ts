/*
 * src/hub/hubRules.ts - pure display rules of the hub: joins, the problem band, the dot, the footer, and the variants that pick
 * one localized string. No React, no bindings, no cs2 value import (values only from ../bindings, types from cs2/utils), so
 * tools/test-hub-rules.js runs it in Node with literal oracles and throws on any other import; a new rule adds its rows there.
 * A function here never returns a placeholder LocEntry: it returns a variant name and the component renders that branch's <LocText>.
 * Total: every function answers any input without throwing.
 */
import type { Entity } from "cs2/utils";
import {
  BUILD_ITEM_IDS, LIFT_STATE, QUICK_REJECT_BUCKETS, RUN_PROP_SPACING_MAX, RUN_PROP_SPACING_MIN, RUN_PROP_SPACING_OFF, LIFT_STATUS, NEXT_STEP, PROBLEM, RESORT_STATUS, RUN_KIND, SEASON_HINT, SKI_DAY_LIMIT, SKI_DAY_STATE,
  TICKET_GATE, TRIP_PLANNER_UNKNOWN, entityId, sameEntity,
} from "../bindings";
import type {
  BuildItem, BuildItemId, CityStats, LiftRow, ProblemRow, ResortLive, ResortRow, RunKind, RunLive,
} from "../bindings";


/** The live row of an area (Entity.Null = Not linked); same index first, else a scan; null = none. */
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


/** Band rows of an area, in C#'s rank order: severity >= 1, not a Factor row, and a code the band knows
 *  (problemVariant != null, not a fixed code range: that would drop RunsFlat = 17). */
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

/** The one Build-strip dot. No sign in the city -> "sign"; else the buildItem of the
 *  first band row of the shown area that names a known item; else null. */
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
  | "depotNoHillLodge" | "depotNoRoad";

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

/** What a click on a band row does. "sign" = fly to `subject` (a building: the sign, or for
 *  LodgeNoRoad the lodge; the lodge C# names for 20-22, the sign for 20 without one;
 *  the lowest summit lodge no lift reaches for 24, the subject C# names for 25; the top-level school for 25,
 *  the first school on no sign for 26, a road-less school for 19 when no lodge is). MixedLiftTypes (23)
 *  is a lift row: its click flies to the lift (to its gap while a section is missing), like LiftUnfinished. */
export function problemClick(p: ProblemRow): ProblemClick {
  switch (p.code) {
    case PROBLEM.NoLodge: case PROBLEM.LodgesAllOff: case PROBLEM.LodgesSomeOff: case PROBLEM.Efficiency:
    case PROBLEM.SignNoRoad: case PROBLEM.LodgeNoRoad:
    case PROBLEM.SkiDaysCannotReach: case PROBLEM.NoParkingNearLodge: case PROBLEM.LodgeNotSlopeside:
    case PROBLEM.SummitLodgeNotLiftServed: case PROBLEM.SchoolNoGreenRun: case PROBLEM.SchoolsNotLinked:
    case PROBLEM.SkiDaysNoGuestsNear: case PROBLEM.DepotNoHillLodge: case PROBLEM.DepotNoRoad:
      return "sign";
    case PROBLEM.LiftNoPower: case PROBLEM.LiftSectionMissing: case PROBLEM.LiftUnfinished:
    case PROBLEM.StationsOnly: case PROBLEM.LiftChairsStopped: case PROBLEM.MixedLiftTypes:
      return "lift";
    case PROBLEM.NoLift: return "liftsView";
    case PROBLEM.NoRuns: case PROBLEM.RunsUnserved: case PROBLEM.RunsUnrateable: case PROBLEM.RunsRating:
    case PROBLEM.RunsFlat:
      return "runsView";
    default: return null;
  }
}

export function bandSlots<T>(rows: readonly T[]): { slots: Array<T | null>; more: number } {
  return { slots: [rows[0] ?? null, rows[1] ?? null, rows[2] ?? null], more: Math.max(0, rows.length - 3) };
}


export const FOLDED_PROBLEM_CODES: readonly number[] = [PROBLEM.LiftUnfinished, PROBLEM.LiftNoPower, PROBLEM.LiftSectionMissing];

/** One band row as drawn: `row` (the first of its code, so the rank is C#'s) and `group` (every row it stands for, in C#'s
 *  order; length 1 for a plain row). */
export interface BandRow {
  row: ProblemRow;
  group: ProblemRow[];
}

/** Band rows after the fold: rows of a FOLDED_PROBLEM_CODES code that occurs two or more times become ONE row at the place of the
 *  first; every other row stays as it is. */
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

/** The names of a folded row as one list: "Lift 3, 4, 5" when every name is one shared prefix plus a number (the default
 *  names), else the names joined ("Meadow Quad, Lift 4"); an empty name reads "–". */
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


export type StatusVariant = "unlinked" | "unknown" | "noRuns" | "line" | "lineEst" | "one" | "oneEst"
  | "partly" | "partlyEst" | "partlyOne" | "partlyOneEst";

export function statusVariant(row: ResortRow | null, live: ResortLive | null): StatusVariant {
  if (row && row.kind === 1) return "unlinked";
  // runsOpen -1: not known yet (paused right after a load, or no result).
  if (!row || !live || live.runsOpen < 0) return "unknown";
  if ((row.runs | 0) <= 0 || live.status === RESORT_STATUS.NoRuns) return "noRuns";
  const one = (row.runs | 0) === 1;
  // No run fully open but some partly open -> count the partly open ones, so the word
  // ("Partly open") and the number never contradict each other ("Partly open · 0 of 7 runs open").
  if ((live.runsOpen | 0) <= 0 && (live.runsPartly | 0) > 0) {
    if (live.snowEstimated) return one ? "partlyOneEst" : "partlyEst";
    return one ? "partlyOne" : "partly";
  }
  if (live.snowEstimated) return one ? "oneEst" : "lineEst";
  return one ? "one" : "line";
}

export type ResultVariant = "result" | "noLodge";

/** Overview receipt result label: no active lodge -> "Attraction (no lodge: 0)", so the
 *  receipt says why lodge base 0 + runs and lifts gives 0. lodgesActive -1 (no result) -> "result". */
export function resultVariant(live: ResortLive | null): ResultVariant {
  return live && live.lodgesActive === 0 ? "noLodge" : "result";
}

export type StatusWord = "open" | "partly" | "closed";

export function statusWord(status: number): StatusWord {
  return status === RESORT_STATUS.Open ? "open" : status === RESORT_STATUS.Partly ? "partly" : "closed";
}

export function closedRuns(row: ResortRow, live: ResortLive): number {
  return Math.max(0, (row.runs | 0) - Math.max(0, live.runsOpen | 0) - Math.max(0, live.runsPartly | 0));
}

/** Powder >= 0.5 = SkiDayRules.PowderMarkAt × 100; parity tested in HubRulesTests (H16). Powder takes
 *  sixths, so the rounded percent never straddles the mark. */
export const POWDER_MARK_PCT = 50;

export type StatusHint = "powder" | "warn" | "snow" | "season" | "none";

/** The SEASON_HINT values that stay on the status line as a warning (runs closing or not opening); every other hint word
 *  moves into the status tooltip and the slot shows the snow. */
export const SEASON_HINT_WARN: readonly number[] = [SEASON_HINT.Thawing, SEASON_HINT.WaitingForSnow, SEASON_HINT.TooWarm];

/** The status line's hint slot: powder first ("Powder day" at the mark), then a warning hint (SEASON_HINT_WARN: "warn", the
 *  word stays), else the snow ("snow": depth cm · coverage %, the hint word in the tooltip) while either is known, else the
 *  hint word ("season"). Nothing for the Not linked row, a sign without runs or a missing live row (the slot then stays empty).
 *  Total also for a live row without the field (undefined >= 50 is false: "season"). */
export function statusHint(row: ResortRow | null, live: ResortLive | null): StatusHint {
  if (!row || row.kind !== 0 || (row.runs | 0) <= 0 || !live || live.runsOpen < 0) return "none";
  if (live.skiDaysPowderPct >= POWDER_MARK_PCT) return "powder";
  // Snow on the top level, "64 cm · 88 %"; a warning hint keeps the slot.
  if (SEASON_HINT_WARN.includes(live.seasonHint)) return "warn";
  return live.snowDepthCm >= 0 || live.snowPct >= 0 ? "snow" : "season";
}


export type NextVariant = "sign" | "unlinked" | "lodge" | "lift" | "run" | "done";

/** Footer idle text: no sign anywhere -> sign; Not linked shown -> unlinked; else
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

/** The Quick runs "N lines dropped: A, B, C" line: the total of QuickRunsState.rejects and its top three buckets (n > 0, most first, ties by
 *  bucket order). Fewer than 2 dropped lines say nothing worth a line: top is then empty. Any input answers (junk counts are 0). */
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

/** A run-making tool is the active tool: the run drawing tool or the cat track tool (a Build run item with active set) or the
 *  Quick runs tool. Their info (run rows, Quick runs controls) lives on the Runs view. */
export function runToolActive(items: readonly BuildItem[] | null | undefined, quickActive: boolean | null | undefined): boolean {
  if (quickActive === true) return true;
  for (const it of items ?? []) if (it && it.active === true && isBuildItemId(it.id) && RUN_ITEMS.has(it.id)) return true;
  return false;
}

/** WHICH tool is active: "quick" (Quick runs), "lift" (the lift line tool), else the id of the active Build item ("" = none). Quick runs
 *  and the lift tool win over a Build item, because their own tools run while the item that started them can still read active. */
export function activeToolKey(items: readonly BuildItem[] | null | undefined, quickActive: boolean | null | undefined,
  liftActive: boolean | null | undefined): string {
  if (quickActive === true) return "quick";
  if (liftActive === true) return "lift";
  for (const it of items ?? []) if (it && it.active === true && isBuildItemId(it.id)) return it.id;
  return "";
}

/** The Hub view a tool key shows its info on: run items, the cat track and Quick runs -> "runs"; lift line items and the lift tool ->
 *  "lifts"; anything else (buildings, stations, fence, none) has no view of its own (null). */
export function toolView(key: string): "runs" | "lifts" | null {
  if (key === "quick") return "runs";
  if (key === "lift") return "lifts";
  if (!isBuildItemId(key)) return null;
  if (RUN_ITEMS.has(key)) return "runs";
  return BUILD_LINE_ITEMS.has(key) ? "lifts" : null;
}

/** The tab follow: select the active tool's view only when the tool key CHANGES to one with a view (run tool -> other run tool, run tool
 *  -> lift tool). The same tool staying active selects nothing (the player may have left the view on purpose). `seen` = the key last
 *  seen; the caller keeps it. */
export function toolTabFollow(seen: string, key: string): { select: "runs" | "lifts" | null; seen: string } {
  return { select: key !== seen ? toolView(key) : null, seen: key };
}

/** Footer line 1 of a tile, by the item's KIND (buildItems.ts BuildItemMeta.kind): runs with
 *  a base fee and a per-ha fee -> run; a line of any lift type with a per-km cost -> lift; anything else with a cost
 *  (sign, lodge tiers, school, patrol huts, stations) -> cost; else the name alone (an unknown kind included). */
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
  /** Lift types (LiftTypeDef.Id) whose station AND line items are ALL sent (a half-imported family,
    *  stations first, is not offered, so it cannot invite a fixed-grip line between detachable stations). */
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

/** The choice in force: the remembered option while it is still offered, else the first option ("" = none). An
 *  option that disappears (a prefab gone after a re-resolution) therefore falls back to the first. */
export function effectiveSelection(sets: PickerSets, sel: PickerSelection | null | undefined): PickerSelection {
  const pick = (options: string[], want: string | undefined): string =>
    want && options.indexOf(want) >= 0 ? want : options.length > 0 ? options[0] : "";
  return { lodge: pick(sets.lodge, sel ? sel.lodge : ""), lift: pick(sets.lift, sel ? sel.lift : "") };
}

/** The ids the strip draws: every SENT id of the table except the station / line items of every lift type other than
 *  the chosen one (a half-imported type is never drawn). Every sent lodge tier is drawn (one Buildings tile each),
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

/** The picker option an item belongs to: a lodge tier is its own option, a station or line item its lift type; null
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

/** Where the ONE dot goes: on its tile when the tile is drawn; on the picker chip of its option
 *  when the item is an alternative that is not chosen (a chip exists only in a set with >= 2 options); else nowhere. */
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

/** The tab of an item KIND: stations and lines -> lifts; runs and the safety fence -> runs; the sign, lodge tiers, bus depot, school and patrol huts -> buildings
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

/** The tab in force: the remembered one while it is drawn, else the tab the idle NEXT line points at, else the first drawn
 *  ("" = none drawn). */
export function effectiveTab(drawn: readonly BuildTab[], stored: string | null | undefined, next: NextVariant): BuildTab | "" {
  if (stored && (drawn as readonly string[]).indexOf(stored) >= 0) return stored as BuildTab;
  const n = tabOfNext(next);
  if (n && drawn.indexOf(n) >= 0) return n;
  return drawn.length > 0 ? drawn[0] : "";
}

export type FooterState = "hover" | "follow" | "active" | "locked" | "empty" | "idle";

/** Footer priority: hover > follow notice > active tool > locked click > idle. */
export function footerState(hasItems: boolean, hovered: boolean, followOn: boolean, active: boolean, lockedClick: boolean): FooterState {
  if (!hasItems) return "empty";
  if (hovered) return "hover";
  if (followOn) return "follow";
  if (active) return "active";
  if (lockedClick) return "locked";
  return "idle";
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
 *  literal table; a change of the constants fails both and points at the hub texts. */
export const PRICE_FULL_AT = 240;
export const PRICE_SPAN = 200;
export const PRICE_MAX = 200;
/** The highest price with no effect on ski days (240 − 200 = 40; the default price is 80, C#
 *  SkiMoneyRules.TicketPriceDefault, where the factor is 0.8). */
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
 *  SkiDayRules.PriceFactor(price, tolerance); the counted lodges' price tolerance, ResortLive
 *  .priceTolerance - Luxury 40, Chalet 20, else 0; `tolerance` omitted = 0).
 *  -1 for an unknown price (negative or not finite). C# never sends one (ClampTicketPrice); C# would clamp a
 *  negative to 0 and answer 1 - deliberate, the hub shows "–" instead. */
export function priceFactor(price: number, tolerance = 0): number {
  if (typeof price !== "number" || !Number.isFinite(price) || price < 0) return -1;
  return Math.min(1, Math.max(0, (PRICE_FULL_AT + toleranceOf(tolerance) - Math.min(PRICE_MAX, price)) / PRICE_SPAN));
}

/** The factor as a whole percent for the hub (Gameface strips trailing zeros, so a percent reads cleanly). Integer
 *  math: (240 + tolerance − min(price, 200)) × 100 / 200, rounded half up (45 → 98); −1 for an unknown price. */
export function priceFactorPct(price: number, tolerance = 0): number {
  if (typeof price !== "number" || !Number.isFinite(price) || price < 0) return -1;
  return Math.round(Math.min(100, Math.max(0,
    (PRICE_FULL_AT + toleranceOf(tolerance) - Math.min(PRICE_MAX, price)) * 100 / PRICE_SPAN)));
}

export function priceFullWith(tolerance: number): number {
  return PRICE_NO_EFFECT_MAX + toleranceOf(tolerance);
}

/** The Overview's RECEIPT_PARTS line - "school" (RECEIPT_PARTS_SCHOOL, the school term shown) while the
 *  model counts a school value above 0, else "plain". */
export function receiptPartsVariant(l: ResortLive | null): "school" | "plain" {
  return l && l.schoolValue > 0 ? "school" : "plain";
}

/** The Money view's "Lodge spend" income row is drawn only while lodge spend earned money this month
 *  (lodgeSpendMonth > 0; -1 unknown and 0 = the setting off draw nothing). */
export function lodgeSpendShown(l: ResortLive | null): boolean {
  return !!l && l.lodgeSpendMonth > 0;
}

/** The Money view's pass rows and the Guests view's pass-holder row are drawn while passes are on (the
 *  Options share above 0) or one was sold / a holder skied. */
export function passesShown(l: ResortLive | null, city: CityStats): boolean {
  if (!l) return false;
  return city.passShare > 0 || l.passesSoldSeason > 0 || l.passHoldersThisMonth > 0 || l.passHoldersLastMonth > 0;
}

/** The Money view's lesson row and the Guests view's "took a lesson" row are drawn once a lesson was taken
 *  this month or last (a working ski school and the Options share above 0 are what make one happen). */
export function lessonsShown(l: ResortLive | null): boolean {
  return !!l && (l.lessonsThisMonth > 0 || l.lessonsLastMonth > 0);
}

/** The "sold before the season" pass row is drawn under the season's pass row once the pre-season sale sold a pass. */
export function presaleShown(l: ResortLive | null): boolean {
  return !!l && l.passesPreSeason > 0;
}

/** The city-income estimate row is drawn when C# sent a figure (>= 0) and Tourism Overhaul is not detected (the views show
 *  the TO note in its place, as for the tourist estimate). */
export function cityIncomeShown(l: ResortLive | null, city: CityStats): boolean {
  return !!l && !city.tourismOverhaul && l.cityIncomeEst >= 0;
}

/** The Net's eight rows of the month so far, in the
 *  Finances column's order, with their sign. C#'s invariant: Σ sign × max(0, value) over the eight = netMonth (an unknown row, -1, reads 0).
 *  The view draws them as sent; netRowsTotal is that sum for the tests (the UI never shows it: the Net row is netMonth as sent). */
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

/** Which of the Net's income rows the Finances view draws: tickets always; lodge spend, lessons and passes sold under their
 *  own conditions OR while the row holds money this month, so a hidden row is always 0 and the drawn rows still add up
 *  to the Net. (Lessons also show at 0 while there is no ski school; the view adds that.) */
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

/** A whole month counted -> "full"; a since-load month C# sent (>= 0 while !lastMonthFull)
 *  -> "partial" (the Guests view says "(partial)"); else "none" ("–", GUESTS_LAST_NA). */
export function lastMonthMark(l: ResortLive | null): LastMonthMark {
  if (l && l.lastMonthFull) return "full";
  return l && l.guestsLastMonth >= 0 ? "partial" : "none";
}

export type SkiDayLine = "unknown" | "off" | "signOff" | "noRun" | "noLodge" | "night" | "roomRuns" | "roomLodges" | "room" | "full";

/** The Guests view's one ski-day state line. Room splits by the limit that binds;
 *  an unknown state or a missing row is "unknown" ("–"). */
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
 *  (Room and Full); null = no tooltip (SignOff, NoRun, Night: the band or the line says why). */
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

/** Walk class = sent - car - far without a car (never below 0); -1 when sent is unknown. The three classes
 *  partition Sent, so this equals the simulation's walk count. */
export function skiDayWalk(l: ResortLive | null): number {
  if (!l || !(l.skiDaysSent >= 0)) return -1;
  return Math.max(0, l.skiDaysSent - Math.max(0, l.skiDaysSentCar | 0) - Math.max(0, l.skiDaysSentNoCar | 0));
}

export type TripPlannerNote = "none" | "known" | "knownOff" | "unknown" | "unknownOff";

/** The Guests view's trip-planner note. "" (the game's trip systems all on),
 *  null, a missing field (an older DLL) or a non-string -> "none" (no line); TRIP_PLANNER_UNKNOWN ("?": a system
 *  is off, no known mod loaded) -> "unknown"; any other label (a known mod's name) -> "known"; "Off" when
 *  tripPlannerBlocksSkiDays is exactly true (the game's LeisureSystem or CitizenBehaviorSystem is off: ski days
 *  do not arm, ArmVerdict.SystemOff). A label only: nothing else in the hub reads it. */
export function tripPlannerNote(city: CityStats | null | undefined): TripPlannerNote {
  const label = city ? city.tripPlanner : undefined;
  if (!city || typeof label !== "string" || label === "") return "none";
  const off = city.tripPlannerBlocksSkiDays === true;
  if (label === TRIP_PLANNER_UNKNOWN) return off ? "unknownOff" : "unknown";
  return off ? "knownOff" : "known";
}


/** Score bar (0-200): integer flex-grow of [fill, ghost, rest]; unknown attraction -> [0, 0, 200]. */
export function scoreBarParts(attraction: number, full: number): [number, number, number] {
  const clamp = (x: number): number => Math.min(200, Math.max(0, Math.round(x)));
  if (!(attraction >= 0)) return [0, 0, 200];
  const a = clamp(attraction);
  const f = Math.max(a, full >= 0 ? clamp(full) : a);
  return [a, f - a, 200 - f];
}

/** A guest bar on a shared scale: integer flex-grow [fill, rest]; value < 0 -> [0, 1] (empty track). */
export function guestBarParts(value: number, max: number): [number, number] {
  const m = Math.max(1, Math.round(max));
  if (!(value >= 0)) return [0, 1];
  const v = Math.min(m, Math.round(value));
  return [v, m - v];
}

/** The smallest full-bar scale, so 2 guests no longer fill the bar. */
export const GUEST_BAR_MIN_SCALE = 50;

/** Ladder 50, 100, 200, 500, 1000, … (× 2, × 2, × 2.5 repeating): the smallest step >= both months (an
 *  unknown, negative or non-finite month counts 0; capped near 1e9). */
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
 *  other kind is placed in the same area (loneStations) - the census never puts both stations on one
 *  StationsOnly row, so "both stations placed" is found across rows, as in C# HubProblems.Build. */
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

/** Why a lift is billed at 25 % (the lift tooltip's line; LiftTable.tsx maps it to LIFT_COST_REDUCED[*]); null
 *  while billed in full. Unfinished (a missing section is unfinished) is the status's own reason; any complete
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
 *  liftsComplete -1: the Not linked entry, or a sign without a score result) - no number is shown
 *  unless it is real. */
export function unfinishedLifts(row: ResortRow, live: ResortLive | null): number {
  if (!live || !(live.liftsComplete >= 0)) return 0;
  return Math.max(0, (row.liftRows | 0) - live.liftsComplete);
}


/** C# SkiResortHubUISystem.ReasonKeyAlreadyBuilt: build[].reason while the SHOWN sign's InstalledUpgrade buffer already holds the
 *  item's ForbidMultiple prefab (the school), switched off or not (UpgradeConflict, Deleted / Temp skipped). */
export const REASON_ALREADY_BUILT = "SkiResort.Hub.REASON[AlreadyBuilt]";

/** The shown sign has a ski school: the school tile carries the AlreadyBuilt lock (C# sends it for the shown sign only). */
export function schoolBuiltOf(build: readonly BuildItem[] | null | undefined): boolean {
  for (const b of build ?? []) if (b && b.id === "school" && b.locked && b.reason === REASON_ALREADY_BUILT) return true;
  return false;
}

/** The ski school of a sign, for the rows whose 0 has a cause: "on" = its value counts
 *  (schoolValue > 0); "noGreen" = runs count but no green one (the sign's PROBLEM.SchoolNoGreenRun row, C# HubProblemCode 25, sent
 *  only while RunsCounted > 0, SkiResortStatsSystem.cs:2700); "waiting" = built, no run counts at all (live.summer: SkiResortScore.cs
 *  :414 summer ⇔ RunsCounted == 0, so SchoolRule gives +0, SkiResortScore.cs:467); "off" = built, runs count, still 0 and no row 25:
 *  the only way is SchoolActive false, the upgrade switched off (BuildingOption.Inactive, SkiResortAttractivenessSystem.cs:1445);
 *  "none" = no school; "unknown" = no live row or schoolValue < 0. `built` = schoolBuiltOf(build) for the SAME shown sign. Reads only
 *  C#'s fields: no new contract field. A school on a sign with no runs must not read "No ski school" (no row 25 there). */
export type SchoolState = "on" | "noGreen" | "waiting" | "off" | "none" | "unknown";
export function schoolState(l: ResortLive | null, problems: readonly ProblemRow[] | null | undefined, sign: Entity | null | undefined,
  built = false): SchoolState {
  if (!l || !(l.schoolValue >= 0)) return "unknown";
  if (l.schoolValue > 0) return "on";
  for (const p of problems ?? []) if (p && p.code === PROBLEM.SchoolNoGreenRun && sameEntity(p.sign, sign)) return "noGreen";
  if (!built) return "none";
  return l.summer ? "waiting" : "off";
}

/** The School row's flag (HUB_LOC key name) by state; null = no flag ("on" / "unknown"). "waiting" is a state, not a fault: the
 *  row draws it dim (InfoLine flagDim). */
export function schoolFlagOf(s: SchoolState): "noSchool" | "schoolNoGreen" | "schoolOff" | "schoolWaiting" | null {
  switch (s) {
    case "none": return "noSchool";
    case "noGreen": return "schoolNoGreen";
    case "off": return "schoolOff";
    case "waiting": return "schoolWaiting";
    default: return null;
  }
}

/**
 * A prop row's next value (RunPropPlacerRules.ClampSpacing has the same range): stepping down under the minimum reads off, stepping up
 * from off lands on the minimum at the least, the maximum holds. The row shows C#'s value; this only picks what to send.
 */
export function runPropSpacingStep(value: number, delta: number): number {
  const next = value + delta;
  if (next < RUN_PROP_SPACING_MIN) return delta < 0 ? RUN_PROP_SPACING_OFF : RUN_PROP_SPACING_MIN;
  return Math.min(RUN_PROP_SPACING_MAX, next);
}
