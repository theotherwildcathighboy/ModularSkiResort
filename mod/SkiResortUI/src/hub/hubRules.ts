/*
 * src/hub/hubRules.ts - pure display rules of hub v3: joins, the problem band, the dot, the footer,
 * the variants that pick one localized string. No React, no bindings, no cs2 value import, so
 * tools/test-hub-rules.js (gate rule R18) runs it in Node with literal oracles.
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6;
 * docs/research/econ-round/spec/ch6-hub-ui.md section 5, the checked reference
 * spec/ch6-hub-ui-ref/src/hub/hubRules.ts, copied unchanged below this header).
 * Rules:
 *   - A function here never returns a placeholder LocEntry (gate R13: placeholder entries are
 *     referenced only as <LocText entry={HUB_FMT.x}>; deviation U3). It returns a variant name; the
 *     component switches on it and renders the one <LocText> of that branch.
 *   - Pure: values only from ../bindings (constants, sameEntity, entityId), types from cs2/utils and
 *     ../bindings. tools/test-hub-rules.js throws on any other import.
 *   - Total: every function answers any input without throwing.
 *   - A new rule adds its literal rows to tools/test-hub-rules.js (76 checks in Round E1; 80 after the E1 fixer: T8.7-T8.10).
 */
import type { Entity } from "cs2/utils";
import {
  BUILD_ITEM_IDS, LIFT_STATE, LIFT_STATUS, NEXT_STEP, PROBLEM, RESORT_STATUS, RUN_KIND, TICKET_GATE,
  entityId, sameEntity,
} from "../bindings";
import type { BuildItem, BuildItemId, LiftRow, ProblemRow, ResortLive, ResortRow, RunKind, RunLive } from "../bindings";

// ---- joins --------------------------------------------------------------------------------------

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

// ---- problems band ------------------------------------------------------------------------------

/** Band rows of an area, in C#'s rank order: severity >= 1, not a Factor row, and a code the band knows
 *  (problemVariant != null; second pass: was "0..15", which would have dropped RunsFlat = 17). */
export function bandRowsOf(problems: readonly ProblemRow[] | null | undefined, sign: Entity | null | undefined): ProblemRow[] {
  const out: ProblemRow[] = [];
  for (const p of problems ?? []) {
    if (!p || !sameEntity(p.sign, sign)) continue;
    if (p.severity >= 1 && p.code !== PROBLEM.Factor && problemVariant(p) !== null) out.push(p);
  }
  return out;
}

/** Factor rows of an area (code Factor), C#'s order (worst first). */
export function factorRowsOf(problems: readonly ProblemRow[] | null | undefined, sign: Entity | null | undefined): ProblemRow[] {
  const out: ProblemRow[] = [];
  for (const p of problems ?? []) if (p && sameEntity(p.sign, sign) && p.code === PROBLEM.Factor) out.push(p);
  return out;
}

export function isBuildItemId(id: string | null | undefined): id is BuildItemId {
  return !!id && (BUILD_ITEM_IDS as readonly string[]).indexOf(id) >= 0;
}

/** D37 / design 7.4: the one Build-strip dot. No sign in the city -> "sign"; else the buildItem of the
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
  | "runsUnserved" | "runsUnrateable" | "runsRating" | "freeLodges" | "runsFlat";

/** Which one string a band row renders; null = not a band row. */
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
    default: return null;
  }
}

/** The notification icon of the worst efficiency factor (design 5.3); undefined = none. */
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

/** The icon of a band row (design 5.3); `null` row = the None line. */
export function problemIcon(p: ProblemRow | null): ProblemIcon {
  if (!p) return { kind: "glyph", src: GLYPH_CHECK, tone: "ok" };
  if (p.code === PROBLEM.Efficiency && FACTOR_ICON[p.factor]) return { kind: "img", src: FACTOR_ICON[p.factor] };
  if (p.code === PROBLEM.LiftNoPower) return { kind: "img", src: ICON_NO_POWER };
  if (p.code === PROBLEM.LodgesAllOff || p.code === PROBLEM.LodgesSomeOff) return { kind: "img", src: ICON_TURNED_OFF };
  if (p.severity >= 2) return { kind: "glyph", src: GLYPH_WARNING, tone: "warn" };
  return { kind: "glyph", src: GLYPH_INFO, tone: "info" };
}

export type ProblemClick = "sign" | "lift" | "liftsView" | "runsView" | null;

/** What a click on a band row does (design 7.2). */
export function problemClick(p: ProblemRow): ProblemClick {
  switch (p.code) {
    case PROBLEM.NoLodge: case PROBLEM.LodgesAllOff: case PROBLEM.LodgesSomeOff: case PROBLEM.Efficiency:
      return "sign";
    case PROBLEM.LiftNoPower: case PROBLEM.LiftSectionMissing: case PROBLEM.LiftUnfinished:
    case PROBLEM.StationsOnly: case PROBLEM.LiftChairsStopped:
      return "lift";
    case PROBLEM.NoLift: return "liftsView";
    case PROBLEM.NoRuns: case PROBLEM.RunsUnserved: case PROBLEM.RunsUnrateable: case PROBLEM.RunsRating:
    case PROBLEM.RunsFlat:
      return "runsView";
    default: return null;
  }
}

/** The three band slots and the "+N" count (design 3.1: slot 3 shows the third row plus "+N"). */
export function bandSlots(rows: readonly ProblemRow[]): { slots: Array<ProblemRow | null>; more: number } {
  return { slots: [rows[0] ?? null, rows[1] ?? null, rows[2] ?? null], more: Math.max(0, rows.length - 3) };
}

// ---- status line --------------------------------------------------------------------------------

export type StatusVariant = "unlinked" | "unknown" | "noRuns" | "line" | "lineEst" | "one" | "oneEst"
  | "partly" | "partlyEst" | "partlyOne" | "partlyOneEst";

export function statusVariant(row: ResortRow | null, live: ResortLive | null): StatusVariant {
  if (row && row.kind === 1) return "unlinked";
  if (!row || !live) return "unknown";
  if ((row.runs | 0) <= 0 || live.status === RESORT_STATUS.NoRuns) return "noRuns";
  const one = (row.runs | 0) === 1;
  // Second pass: no run fully open but some partly open -> count the partly open ones, so the word
  // ("Partly open") and the number never contradict each other ("Partly open · 0 of 7 runs open").
  if ((live.runsOpen | 0) <= 0 && (live.runsPartly | 0) > 0) {
    if (live.snowEstimated) return one ? "partlyOneEst" : "partlyEst";
    return one ? "partlyOne" : "partly";
  }
  if (live.snowEstimated) return one ? "oneEst" : "lineEst";
  return one ? "one" : "line";
}

export type ResultVariant = "result" | "noLodge";

/** Overview receipt row 11 label (second pass): no active lodge -> "Attraction (no lodge: 0)", so the
 *  receipt says why lodge base 0 + runs and lifts gives 0. lodgesActive -1 (no result) -> "result". */
export function resultVariant(live: ResortLive | null): ResultVariant {
  return live && live.lodgesActive === 0 ? "noLodge" : "result";
}

export type StatusWord = "open" | "partly" | "closed";

/** RESORT_STATUS -> the word of the sentence (NoRuns never reaches here). */
export function statusWord(status: number): StatusWord {
  return status === RESORT_STATUS.Open ? "open" : status === RESORT_STATUS.Partly ? "partly" : "closed";
}

/** Closed runs = runs - runsOpen - runsPartly, never below 0. */
export function closedRuns(row: ResortRow, live: ResortLive): number {
  return Math.max(0, (row.runs | 0) - Math.max(0, live.runsOpen | 0) - Math.max(0, live.runsPartly | 0));
}

// ---- footer -------------------------------------------------------------------------------------

export type NextVariant = "sign" | "unlinked" | "lodge" | "lift" | "run" | "done";

/** Footer idle text (design 6.10): no sign anywhere -> sign; Not linked shown -> unlinked; else
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

export const RUN_ITEMS: ReadonlySet<BuildItemId> = new Set<BuildItemId>(["runNatural", "runGroomed", "runSnowmaking", "runNight"]);

/** Footer line 1 of a tile (design 6.10): runs with a base fee and a per-ha fee -> run; the lift line
 *  with a per-km cost -> lift; anything with a cost -> cost; else the name alone. */
export function line1Variant(id: BuildItemId, item: BuildItem): Line1Variant {
  if (RUN_ITEMS.has(id)) return known(item.price) && known(item.pricePerHa) ? "run" : "name";
  if (id === "lift") return known(item.costPerKm) ? "lift" : "name";
  return known(item.cost) ? "cost" : "name";
}

export type FooterState = "hover" | "follow" | "active" | "locked" | "empty" | "idle";

/** Footer priority (design 6.10): hover > follow notice > active tool > locked click > idle. */
export function footerState(hasItems: boolean, hovered: boolean, followOn: boolean, active: boolean, lockedClick: boolean): FooterState {
  if (!hasItems) return "empty";
  if (hovered) return "hover";
  if (followOn) return "follow";
  if (active) return "active";
  if (lockedClick) return "locked";
  return "idle";
}

/** The run kind of a run item, else undefined. */
export function runKindOfItem(id: BuildItemId): RunKind | undefined {
  switch (id) {
    case "runNatural": return RUN_KIND.Natural;
    case "runGroomed": return RUN_KIND.Groomed;
    case "runSnowmaking": return RUN_KIND.Snowmaking;
    case "runNight": return RUN_KIND.Night;
    default: return undefined;
  }
}

// ---- guests and money ---------------------------------------------------------------------------

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

/** The Money view's tickets row (design 6.9). */
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

/** Buildings + wages a month, or -1 when either is unknown. */
export function buildingsAndWages(live: ResortLive): number {
  return live.buildingsMonth < 0 || live.wagesMonth < 0 ? -1 : live.buildingsMonth + live.wagesMonth;
}

/** Hectares from square metres (display; one decimal by the unit). */
export function hectares(m2: number): number {
  return Number.isFinite(m2) && m2 > 0 ? m2 / 10000 : 0;
}

// ---- bars ---------------------------------------------------------------------------------------

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

// ---- lifts --------------------------------------------------------------------------------------

export type LiftLine2 = "detail" | "detailOne" | "detailNone" | "sectionMissing" | "unfinished"
  | "stationsPair" | "stationsBaseOnly" | "stationsTopOnly";

/** Line 2 of a lift row (design 6.7). `partnerPlaced` (E1 fixer, e1-spec 8.9 F9): a lone station of the
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

/** Lone stations of an area's lift rows (E1 fixer, F9): a row of state Unfinished or StationsOnly with
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

/** Whether `row` is a lone station whose partner kind is placed in the area (liftLine2's second argument). */
export function partnerPlaced(row: LiftRow, lone: { base: boolean; top: boolean }): boolean {
  if (row.hasBase && !row.hasTop) return lone.top;
  if (row.hasTop && !row.hasBase) return lone.base;
  return false;
}

export type LiftTone = "normal" | "warn" | "dim";

/** Mark shape and colour of a lift status (design 5.3). */
export function liftMark(status: number): { ring: boolean; tone: LiftTone } {
  switch (status) {
    case LIFT_STATUS.Running: return { ring: false, tone: "normal" };
    case LIFT_STATUS.NoPower: return { ring: false, tone: "warn" };
    case LIFT_STATUS.ChairsStopped: return { ring: false, tone: "dim" };
    case LIFT_STATUS.SectionMissing: return { ring: true, tone: "warn" };
    default: return { ring: true, tone: "dim" };
  }
}

/** Unfinished lines and lone stations of an area: lift rows minus complete lifts (never below 0).
 *  E1 fixer (e1-spec 8.9 F11): 0 when the live row does not know the complete count (no live row, or
 *  liftsComplete -1: the Not linked entry, or a sign without a score result) - no number is shown
 *  unless it is real. */
export function unfinishedLifts(row: ResortRow, live: ResortLive | null): number {
  if (!live || !(live.liftsComplete >= 0)) return 0;
  return Math.max(0, (row.liftRows | 0) - live.liftsComplete);
}
