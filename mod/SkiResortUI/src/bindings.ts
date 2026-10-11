/*
 * src/bindings.ts - THE single source of truth for the C# <-> UI contract of the Ski Resort hub.
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-A); rewritten for
 * contract 2 on 2026-10-03 (Round U2, implementer I-3); contract 3 on 2026-10-04 (Round E1, stream
 * S6: docs/research/econ-round/spec/ch6-hub-ui.md section 4, design
 * docs/research/econ-round/design/hub-v3-design.md section 8; e1-spec.md I-25); contract 4 on
 * 2026-10-05 (Round S1 "Ski days", stream E: docs/research/ski-days/s1-spec.md section 2.10,
 * spec/ch4-hub.md section A: ten ResortLive fields appended, SKI_DAY_STATE, SKI_DAY_LIMIT, PROBLEM
 * 20-22; the six last-month guest fields are now also sent for a partial month, F7).
 * Authority before contract 3: docs/research/hub-u2-spec.md section 2.2 (binding table) and 2.3
 * (shapes); contract 1 was resort-hub-understand.md section 5.3. The C# side (SkiResortHubUISystem,
 * group "skiResort") must register EXACTLY these names with these shapes (ch5 section 4.4 writes the
 * same fields in the same names). Change a name, a type or a field here and in C# together, bump
 * HUB_CONTRACT in both, then run `npm run contract:lock` and review the diff of tools/contract.lock.
 *
 * WHAT THE BUILD GATE READS FROM THIS FILE (tools/check-bundle.js, spec section 4.7; keep these
 * shapes literal so the TypeScript AST can read them):
 *   - HUB_CONTRACT = <integer literal>  (compared with C# `HubContract`);
 *   - BINDING / TRIGGER = { name: "name", ... } (every name must be registered in C# with a string
 *     literal: `new ValueBinding<T>(Group, "name", ...)`, `new RawValueBinding(Group, "name", ...)`,
 *     `new TriggerBinding<...>(Group, "name", ...)`, and nothing else may be registered);
 *   - CORE_NAMES = [ "...", ... ] (string literals): the names frozen since contract 1. A CORE name
 *     may only gain OPTIONAL fields in its shape; it is never renamed, retyped or removed;
 *   - every `export interface X` below: the field list of `writer.TypeBegin("SkiResortMod.X")` in C#;
 *   - every `bindValue<T>(GROUP, BINDING.x, FALLBACK.x)`: the TS type of binding x;
 *   - every `hubTriggers` entry `(a: A, b: B): void => fire(TRIGGER.x, ...)`: the argument list of x;
 *   - (Round E1) rule R16: BUILD_ITEM_IDS, the BuildItemId union and BUILD_ITEM_PREFAB (with
 *     src/hub/buildItems.ts BUILD_GROUPS) against C# ItemIds / ItemPrefabNames / the item constants;
 *   - (Round E1) rule R17: the const objects RUN_KIND, RESORT_STATUS, OPEN_STATE, SEASON_HINT,
 *     NEXT_STEP, LIFT_STATUS, PROBLEM, TICKET_GATE (literal numbers) against their C# enums
 *     (EntityRef.cs RunKind, HubRules.cs, SkiMoneyRules.TicketGate). BAND and LIFT_STATE have no
 *     C# enum (literal ints in C#) and are not compared. Round S1 (contract 4) adds SKI_DAY_STATE
 *     (HubRules.cs HubSkiDayState) and SKI_DAY_LIMIT (SkiDayRules.cs SkiDayLimit, a top-level enum).
 *
 * Rules every consumer relies on (verified in the installed 1.6.2f1 Cities2_Data/Content/Game/UI/
 * index.js unless marked):
 *   - Every bindValue() below passes a fallback that is NOT undefined. bindValue(group, name, fb)
 *     builds a binding whose starting value is fb (class `ml`, index.js@419651); connect() throws
 *     "'<group>.<name>.update' was not called after subscribe!" only while the value is still
 *     undefined after subscribing (index.js@421257). So a missing or not yet registered C#
 *     binding renders the fallback instead of throwing (check-bundle's smoke test enforces it).
 *     A DLL of an older contract therefore shows the fallbacks for every new name; the panel reads
 *     them only when `contract` is HUB_CONTRACT (spec 4.5: on a mismatch only CORE is read).
 *   - C# pushes a value as soon as JS subscribes (Colossal.UI.Binding ValueBinding/RawValueBinding,
 *     research 2.B.1), so `contract$` reads the DLL's contract synchronously when the hub system
 *     exists and stays CONTRACT_ABSENT (0) when it does not.
 *   - A JS binding connects on its first subscriber and disconnects with its last
 *     (index.js@421257 subscribe/dispose), so C#'s observerCount counts binding objects, not
 *     components. Subscribe to every panel binding (contract 3, ch6 section 10.1: build$, glade$,
 *     resorts$, resortLive$, selectedResort$, runs$, runLive$, lifts$, problems$, city$, pisteMap$,
 *     follow$) ONLY in components that mount while the panel is open (research 2.F.6; gate rule R11):
 *     closing the panel unsubscribes them, and C# stops computing (the stats system's Demand follows
 *     the observers of build, resorts, resortLive, runs, runLive, lifts, problems and city, ch5 4.3;
 *     pisteMap and follow are panel bindings without Demand). hubOpen$, contract$, buildStamp$ and
 *     showHubButton$ are subscribed always (launchers, root).
 *   - Triggers fire the cohtml event "<group>.<name>" (cs2/api trigger). An exception in a C#
 *     trigger body is caught and logged to the vanilla UI log, not ours (research 2.B.1), so C#
 *     wraps every body in try/catch and writes its own "!!!" line.
 *   - Entities travel as { index, version } both ways (research 2.B.1, UnityWriters/UnityReaders);
 *     Entity.Null is { index: 0, version: 0 } (spec 2.1 EntityRef.IsNull).
 *   - Rows written by C# with JsonWriter.TypeBegin carry an extra "__Type" field; ignore it. Every
 *     TypeBegin name is "SkiResortMod.<TS interface name>" (spec 2.2 push rules).
 *   - Contract 3 conventions (ch6 section 4.3): -1 = unknown (rendered "–" by numArg / NumOrDash);
 *     the two arrays of a pair (resorts / resortLive, runs / runLive) are joined by key (`sign`,
 *     `entity`), never by index alone (src/hub/hubRules.ts liveOf, runLiveMap); costs arrive
 *     positive; ProblemRow.factor is an EfficiencyFactor enum name the UI translates;
 *     BuildItem.hint is a locale key that feeds footer line 2 only.
 *
 * Patch day (research 2.A.5): grep the new index.js for every name in VANILLA_NAMES_USED.
 */
import { bindValue, trigger } from "cs2/api";
import type { ValueBinding } from "cs2/api";
import type { Entity } from "cs2/utils";

// ---- identity -----------------------------------------------------------------------------------

/** Binding group of every value and trigger in this file. */
export const GROUP = "skiResort";

/**
 * Contract 4 (Round S1, ski days: docs/research/ski-days/spec/ch4-hub.md section A; contract 3 was
 * Round E1, D32). C#: SkiResortHubUISystem.HubContract.
 * Bump both whenever a binding, trigger, field or meaning below changes (tools/check-bundle.js
 * fails a change without a bump, against tools/contract.lock). The panel renders the resort views
 * only when C#'s `contract` equals this; on a mismatch it shows the Build strip and the tree
 * controls (CORE only) and says why (spec 4.5).
 */
export const HUB_CONTRACT = 4;

/** The `contract` value while the C# binding is absent (the fallback). Real contracts start at 1. */
export const CONTRACT_ABSENT = 0;

/**
 * This bundle's build stamp (webpack DefinePlugin). Compare with buildStamp$ (the DLL's
 * SkiResortMod.BuildStamp.Value): equal = the DLL and this bundle came out of the same C# build.
 * A standalone UI build (`npm run build` / `npm run dev`) has a "ui-" stamp and never matches.
 */
export const BUNDLE_STAMP: string = __BUILD_STAMP__;

/** Prefix of a stamp produced by a UI-only build (webpack.config.js newStandaloneStamp). */
export const STANDALONE_STAMP_PREFIX = "ui-";

// ---- entities -----------------------------------------------------------------------------------

/** Entity.Null as it travels: { index: 0, version: 0 }. */
export const NULL_ENTITY: Entity = { index: 0, version: 0 };

/** true for Entity.Null, and for a missing / malformed value (treated as Null). */
export function isNullEntity(e: Entity | null | undefined): boolean {
  return !e || (e.index === 0 && e.version === 0);
}

/** Entity equality with null / undefined read as Entity.Null (cs2/utils entityEquals does not:
 *  index.js@414492 `e===t||null!=e&&null!=t&&...`). */
export function sameEntity(a: Entity | null | undefined, b: Entity | null | undefined): boolean {
  const ai = a ? a.index : 0, av = a ? a.version : 0;
  const bi = b ? b.index : 0, bv = b ? b.version : 0;
  return ai === bi && av === bv;
}

/** A stable React key / map key for an entity. */
export function entityId(e: Entity | null | undefined): string {
  return e ? `${e.index}:${e.version}` : "0:0";
}

function wire(e: Entity | null | undefined): Entity {
  return e ? { index: e.index, version: e.version } : { index: 0, version: 0 };
}

// ---- enumerations (C# enums of the same values; gate rule R17 compares them) ---------------------

/** Run type. C# RunKind (EntityRef.cs): Natural = 3 appended in Round E1 (D16), never renumbered. */
export type RunKind = 0 | 1 | 2 | 3;
export const RUN_KIND = { Groomed: 0, Snowmaking: 1, Night: 2, Natural: 3 } as const;

/** RunRow.band (unchanged): -2 cannot be rated (final for the current outline and terrain), -1 not
 *  rated yet, 0 Flat .. 4 DoubleBlack (C#: SkiRunRatingRules.RowBand; literal ints, no enum). */
export const BAND = { Unrateable: -2, Pending: -1, Flat: 0, Green: 1, Blue: 2, Black: 3, DoubleBlack: 4 } as const;

/** LiftRow.state (stats rules, unchanged; literal ints in C#, no enum). */
export const LIFT_STATE = { Running: 0, Static: 1, Open: 2, Unfinished: 3, StationsOnly: 4 } as const;

/** ResortLive.status. C# HubRules ResortStatus. */
export const RESORT_STATUS = { NoRuns: 0, Closed: 1, Partly: 2, Open: 3 } as const;

/** RunLive.openState. C# HubRules RunOpenState. */
export const OPEN_STATE = { None: -1, Closed: 0, Partly: 1, Open: 2 } as const;

/** ResortLive.seasonHint. C# HubRules SeasonHint. */
export const SEASON_HINT = { None: 0, SnowSeason: 1, SnowGunsOnly: 2, Thawing: 3, WaitingForSnow: 4, TooWarm: 5, GatingOff: 6 } as const;

/** ResortLive.nextStep. C# HubRules NextStepKind. */
export const NEXT_STEP = { Done: 0, Lodge: 1, Lift: 2, Run: 3 } as const;

/** LiftRow.status. C# HubRules HubLiftStatus. */
export const LIFT_STATUS = { Running: 0, NoPower: 1, ChairsStopped: 2, SectionMissing: 3, Unfinished: 4 } as const;

/** ResortLive.ticketGate. C# SkiMoneyRules.TicketGate (chapter 4). */
export const TICKET_GATE = { Open: 0, Off: 1, NoEfficiency: 2, NothingOpen: 3, NotKnownYet: 4 } as const;

/** ProblemRow.code. C# HubRules HubProblemCode (append only). */
export const PROBLEM = {
  Unlinked: 0, NoLodge: 1, LodgesAllOff: 2, LodgesSomeOff: 3, Efficiency: 4, LiftNoPower: 5,
  LiftSectionMissing: 6, LiftUnfinished: 7, NoLift: 8, NoRuns: 9, StationsOnly: 10, LiftChairsStopped: 11,
  RunsUnserved: 12, RunsUnrateable: 13, RunsRating: 14, FreeLodges: 15, Factor: 16,
  RunsFlat: 17, // second pass: appended after Factor (append only; C# HubProblemCode.RunsFlat)
  SignNoRoad: 18, // Round F7 D5: the Welcome Sign's Building.m_RoadEdge is null (C# HubProblemCode.SignNoRoad)
  LodgeNoRoad: 19, // Round F7 D5: a lodge of the area has Building.m_RoadEdge null; subject = the first such lodge
  SkiDaysCannotReach: 20, // Round S1 D28: most car ski-day guests never arrived (sev 2); subject = that lodge, else the sign
  NoParkingNearLodge: 21, // Round S1 D28 (D26 census): ski-day lodges with 0 car spaces within 300 m (sev 1); count
  LodgeNotSlopeside: 22, // Round S1 D28 (D10): active lodges more than 800 m from the runs and stations (sev 1); count
} as const;

/** ResortLive.skiDaysState. C# HubRules HubSkiDayState (Round S1, contract 4). */
export const SKI_DAY_STATE = { Unknown: 0, Off: 1, SignOff: 2, NoRun: 3, NoLodge: 4, Night: 5, Room: 6, Full: 7 } as const;

/** ResortLive.skiDaysLimit. C# SkiDayRules SkiDayLimit (ch1): which D21 term sets the capacity. */
export const SKI_DAY_LIMIT = { None: 0, Runs: 1, Lodges: 2 } as const;

/** Build-strip ids. C# SkiResortHubUISystem.ItemIds holds the same set (gate rule R16). C# owns
 *  what each id launches; this order is only a fallback (the strip draws BUILD_GROUPS of
 *  src/hub/buildItems.ts, D17 build order). */
export type BuildItemId =
  | "sign" | "lodge" | "runNatural" | "runGroomed" | "runSnowmaking" | "runNight" | "lift" | "baseStation" | "topStation";
export const BUILD_ITEM_IDS: readonly BuildItemId[] = [
  "sign", "lodge", "baseStation", "topStation", "lift", "runNatural", "runGroomed", "runSnowmaking", "runNight",
];

/** Display only: the prefab behind each id (rule R16 compares it with C# ItemPrefabNames). */
export const BUILD_ITEM_PREFAB: Readonly<Record<BuildItemId, string>> = {
  sign: "SkiResortGate",
  lodge: "SkiAFrameDayLodge",
  runNatural: "SkiRunNaturalA",
  runGroomed: "SkiRunGroomedA",
  runSnowmaking: "SkiRunSnowmakingA",
  runNight: "SkiRunNightA",
  lift: "SkiChairliftHV",
  baseStation: "SkiBaseStationBuilding",
  topStation: "SkiTopStationBuilding",
};

// ---- value shapes (C# -> UI); C# structs mirror them field for field ----------------------------

/** One Build-strip entry. CORE: only optional fields may be added (contract 3 adds six). */
export interface BuildItem {
  /** One of BUILD_ITEM_IDS (typed as string: treat an unknown id as "not for this bundle"). */
  id: string;
  /** Locked by progression (or no sign for the lodge); `reason` says why. */
  locked: boolean;
  /** This item's tool is the active tool (clicking it again returns to the default tool). */
  active: boolean;
  /** A locale key explaining `locked`, or "". */
  reason: string;
  /** Run items: D21 base fee (SkiRunFees.BaseFee); -1 others. */
  price?: number;
  /** A locale key or "" (lift item: HINT[LiftNoPair] for the SHOWN area, D37). */
  hint?: string;
  /** Run items: D21 fee per hectare (SkiRunFees.PerHaFee); -1 others. */
  pricePerHa?: number;
  /** Sign, base, top: construction cost; lodge: upgrade cost; -1 unknown. */
  cost?: number;
  /** Lift item: line construction per km (D24); -1 others. */
  costPerKm?: number;
  /** Lift item: fixed running cost per complete lift a month (D23); -1 others. */
  monthly?: number;
  /** Run items: running cost per active hectare a month (D22); -1 others. */
  monthlyPerHa?: number;
  /** Lift item: running cost per km of line a month (D23); -1 others. */
  monthlyPerKm?: number;
}

/** `glade`: the tree tool (SkiGladeToolSystem; density dial). CORE since contract 1, unchanged. */
export interface GladeState {
  /** The tool is armed and can be activated. */
  available: boolean;
  /** The tool is the active tool. */
  active: boolean;
  /** Tree density level, GLADE_LEVEL_MIN..GLADE_LEVEL_MAX. */
  level: number;
  /** false = run interior, true = tree line. */
  treeLine: boolean;
  /** A locale key explaining `available == false`, or "". */
  reason: string;
}

export const GLADE_LEVEL_MIN = 1;
export const GLADE_LEVEL_MAX = 8;
/** SkiGladeCompose.DefaultLevel. */
export const GLADE_LEVEL_DEFAULT = 4;

/** One ski area (a live Welcome Sign) or the Not linked row (always last): identity and slow facts. */
export interface ResortRow {
  /** The sign; Entity.Null on the Not linked row. */
  sign: Entity;
  /** 0 = a sign; 1 = the "Not linked" row: always sent, always last. */
  kind: 0 | 1;
  /** The sign's custom name, else "Ski area N" (C#). The UI ignores it on the Not linked row. */
  name: string;
  customName: boolean;
  runs: number;
  /** Runs with a rating (band >= 0); "being rated" = runs - rated - unrateable (counted from runs). */
  rated: number;
  flat: number;
  green: number;
  blue: number;
  black: number;
  doubleBlack: number;
  /** m^2, plan area of the runs. */
  areaM2: number;
  /** Highest run top - lowest run bottom (stations no longer count); -1 = no run. */
  verticalM: number;
  /** true while any run of the area is still being rated (node estimates). */
  verticalApprox: boolean;
  lodges: number;
  /** Lodge-prefab buildings on no live sign (Not linked row). */
  freeLodges: number;
  /** Census rows of the area (complete lifts, unfinished lines, lone stations). */
  liftRows: number;
  liftLengthM: number;
  /** Design capacity of the complete lifts, riders/h at 1x; -1 none. */
  capacityPerHour: number;
}

/** Per-area values that move (same order and count as `resorts`, joined by `sign`). Score fields
 *  are the model's Result (D10: never recomputed in the UI); -1 = unknown or no result. */
export interface ResortLive {
  sign: Entity;
  /** RESORT_STATUS. */
  status: number;
  runsOpen: number;
  runsPartly: number;
  snowEstimated: boolean;
  snowPct: number;
  snowDepthCm: number;
  /** SEASON_HINT. */
  seasonHint: number;
  topTempKnown: boolean;
  topTempC: number;
  attraction: number;
  attractionFull: number;
  /** The first provider write after a load is held: the game keeps the saved score (show "…"). */
  attractionHeld: boolean;
  lodgesActive: number;
  summer: boolean;
  lodgePart: number;
  terrain: number;
  runsValue: number;
  runsValueFull: number;
  openSharePct: number;
  liftsValue: number;
  liftsComplete: number;
  liftsCounted: number;
  varietyMultiplier: number;
  verticalMultiplier: number;
  scoreVerticalM: number;
  efficiencyPct: number;
  sharePct: number;
  estKnown: boolean;
  estTourists: number;
  /** NEXT_STEP. */
  nextStep: number;
  thisMonthSinceLoad: boolean;
  /** A whole month was counted. Last-month fields are -1 only when no month has rolled since counting
   *  started; with lastMonthFull false they hold a partial month (shown with "(partial)"; Round S1 F7). */
  lastMonthFull: boolean;
  guestsThisMonth: number;
  touristsThisMonth: number;
  touristGroupsThisMonth: number;
  residentsThisMonth: number;
  paidThisMonth: number;
  unpaidThisMonth: number;
  freeThisMonth: number;
  ticketsThisMonth: number;
  guestsLastMonth: number;
  touristsLastMonth: number;
  touristGroupsLastMonth: number;
  residentsLastMonth: number;
  paidLastMonth: number;
  ticketsLastMonth: number;
  /** TICKET_GATE. */
  ticketGate: number;
  ticketIncomeMonth: number;
  ticketPaidMonth: number;
  ticketIncomeEstimated: boolean;
  costGroomedMonth: number;
  costSnowmakingMonth: number;
  costNightMonth: number;
  haNatural: number;
  haGroomed: number;
  haSnowmaking: number;
  haNight: number;
  liftCostMonth: number;
  liftsBilled: number;
  liftsBilledReduced: number;
  buildingsMonth: number;
  wagesMonth: number;
  jobs: number;
  jobsFilled: number;
  /** The one net (ch5 HubRules.NetMonth); Not linked: -liftCostMonth. Used as sent. */
  netMonth: number;
  netEstimated: boolean;
  // Contract 4 (Round S1, ski days; ch4 section A.1): appended in this order, as in C# ResortLiveStats.
  /** SKI_DAY_STATE (0 Unknown; also on the Not linked row). */
  skiDaysState: number;
  /** Ski-day guests who reached a lodge this month; -1 unknown. */
  skiDaysThisMonth: number;
  /** The same, last month; -1 until a month rolled since counting started (partial when !lastMonthFull). */
  skiDaysLastMonth: number;
  /** Ski-day claims this month, all classes; -1 unknown. */
  skiDaysSent: number;
  /** Of those, class Car (farther than walking, had a car, D12); -1 unknown. */
  skiDaysSentCar: number;
  /** Of those, class "far, no car" (D13); -1 unknown. Walk = sent - car - no car (hubRules.skiDayWalk). */
  skiDaysSentNoCar: number;
  /** Claimed guests on the way plus on site now (D21); -1 unknown. */
  skiDaysNow: number;
  /** Capacity at once (D21 Cap incl. powder, whole skiers; 0 is real and reads Full); -1 unknown. */
  skiDaysCap: number;
  /** SKI_DAY_LIMIT: which D21 term binds (0 None). */
  skiDaysLimit: number;
  /** Powder 0-100 (HubRules.Percent of the area's powder, D19); -1 unknown. */
  skiDaysPowderPct: number;
}

/** One run of ours: identity, rating, service, value parts. */
export interface RunRow {
  entity: Entity;
  /** Custom name, or "Groomed run N" etc. (C#; numbered per area); empty -> the type name. */
  name: string;
  kind: RunKind;
  bound: boolean;
  /** m, horizontal. */
  lengthM: number;
  /** m, true drop when rated; max - min node height when approx. */
  dropM: number;
  /** m^2, plan area. */
  areaM2: number;
  /** The sign the run is bound to; Entity.Null = not linked. */
  sign: Entity;
  ordinal: number;
  customName: boolean;
  /** BAND. */
  band: number;
  /** Hardest unavoidable pitch, %; -1 not rated. */
  gradePct: number;
  /** Steepest patch, %; -1 not rated. */
  steepestPct: number;
  /** true = lengthM / dropM are node estimates. */
  approx: boolean;
  /** Counted by its area's score (bound to a live sign). */
  member: boolean;
  /** A complete, powered lift reaches its top (the served rule, e1-spec I-04). */
  served: boolean;
  /** The serving lift's row entity (LiftRow.entity), else Entity.Null. */
  servedBy: Entity;
  valueFull: number;
  bandValue: number;
  sizeFactor: number;
  typeFactor: number;
  servedFactor: number;
}

/** Per-run values that move (same order and count as `runs`, joined by `entity`). */
export interface RunLive {
  entity: Entity;
  /** OPEN_STATE. */
  openState: number;
  openFactor: number;
  snowPct: number;
  snowEstimated: boolean;
  valueNow: number;
  costMonth: number;
}

/** One lift (LiftCensus row). */
export interface LiftRow {
  /** Row id: the base station, else the top station, else the lowest-index line edge. */
  entity: Entity;
  /** The top station; Entity.Null when none. */
  top: Entity;
  /** The area: nearest sign within 5 km of the base station; Entity.Null = not linked. */
  sign: Entity;
  name: string;
  customName: boolean;
  /** LIFT_STATE. */
  state: number;
  sections: number;
  towers: number;
  chairs: number;
  chairsRiding: number;
  gaps: number;
  towersRemoved: number;
  /** m, sum of the line's sections. */
  lengthM: number;
  /** m, top station - base station. */
  riseM: number;
  horizontalM: number;
  alignKnown: boolean;
  alignOffsetM: number;
  alignYawDeg: number;
  /** Riders per hour at 1× game speed; -1 unknown. */
  capacityPerHour: number;
  hasBase: boolean;
  hasTop: boolean;
  /** Raw LiftPower != NoPower (the status mark); `counted` follows LiftServedRule.IsPowered (R1). */
  powered: boolean;
  counted: boolean;
  /** LIFT_STATUS. */
  status: number;
  rideTimeS: number;
  runsServed: number;
  costMonth: number;
  billedReduced: boolean;
}

/** One problem of one area (band rows ranked by C#, then Factor rows). */
export interface ProblemRow {
  sign: Entity;
  /** PROBLEM. */
  code: number;
  severity: number;
  subject: Entity;
  subjectTop: Entity;
  name: string;
  count: number;
  pct: number;
  /** An EfficiencyFactor enum name (the UI reads the game's own text for it). */
  factor: string;
  more: number;
  detail: number;
  ifFixed: number;
  /** A BuildItemId (the footer dot) or "". */
  buildItem: string;
}

/** City-wide settings and session facts. */
export interface CityStats {
  known: boolean;
  daysPerYear: number;
  scaleK: number;
  backcountryPct: number;
  ticketPrice: number;
  snowGating: boolean;
  snowmakingMarginC: number;
  snowVisualsOff: boolean;
  tourismOverhaul: boolean;
  realisticWorkplaces: boolean;
}

/** Locale key of a level's name (root LocaleEN.cs: "Clear" ... "Thick forest"). */
export function gladeLevelNameKey(level: number): string {
  return `SkiResort.GladeTooltip.LEVEL_NAME[${clampGladeLevel(level)}]`;
}

export function clampGladeLevel(level: number): number {
  const n = Math.round(Number.isFinite(level) ? level : GLADE_LEVEL_DEFAULT);
  return Math.min(GLADE_LEVEL_MAX, Math.max(GLADE_LEVEL_MIN, n));
}

// ---- names --------------------------------------------------------------------------------------

/** Value binding names (C# -> UI): 16 in contract 3 (`lodges` removed). */
export const BINDING = {
  hubOpen: "hubOpen",
  contract: "contract",
  buildStamp: "buildStamp",
  showHubButton: "showHubButton",
  build: "build",
  glade: "glade",
  resorts: "resorts",
  resortLive: "resortLive",
  selectedResort: "selectedResort",
  runs: "runs",
  runLive: "runLive",
  lifts: "lifts",
  problems: "problems",
  city: "city",
  pisteMap: "pisteMap",
  follow: "follow",
} as const;

/** Trigger names (UI -> C#): 17 in contract 3. */
export const TRIGGER = {
  toggleHub: "toggleHub",
  setHubOpen: "setHubOpen",
  uiHello: "uiHello",
  uiError: "uiError",
  launch: "launch",
  focusRun: "focusRun",
  gladeToggle: "gladeToggle",
  gladeSetLevel: "gladeSetLevel",
  gladeSetTreeLine: "gladeSetTreeLine",
  dumpHub: "dumpHub",
  selectResort: "selectResort",
  rename: "rename",
  focusLift: "focusLift",
  focusBuilding: "focusBuilding",
  uiLayout: "uiLayout",
  setPisteMap: "setPisteMap",
  hoverRun: "hoverRun",
} as const;

/**
 * CORE = frozen since contract 1 (spec 2.2): unchanged in contract 3. These names may only gain
 * OPTIONAL fields; they are never renamed, retyped or removed. The ONE machine-readable list:
 * tools/check-bundle.js reads it (string literals only) and checks it against tools/contract.lock.
 * On a contract mismatch the panel reads only these (spec 4.5).
 */
export const CORE_NAMES: readonly string[] = [
  "hubOpen", "contract", "buildStamp", "showHubButton", "build", "glade",
  "toggleHub", "setHubOpen", "uiHello", "uiError", "launch", "focusRun",
  "gladeToggle", "gladeSetLevel", "gladeSetTreeLine", "dumpHub",
];

/** Vanilla binding / trigger names used ("group.name"): none (the vanilla LOCALE keys we read are
 *  listed in src/hub/hubText.ts EFFICIENCY_FACTOR_FALLBACK, not here). */
export const VANILLA_NAMES_USED: readonly string[] = [];

// ---- fallbacks (what the UI shows while C# has not registered a binding) ------------------------

const EMPTY_BUILD: readonly BuildItem[] = [];
const EMPTY_RESORTS: readonly ResortRow[] = [];
const EMPTY_RESORT_LIVE: readonly ResortLive[] = [];
const EMPTY_RUNS: readonly RunRow[] = [];
const EMPTY_RUN_LIVE: readonly RunLive[] = [];
const EMPTY_LIFTS: readonly LiftRow[] = [];
const EMPTY_PROBLEMS: readonly ProblemRow[] = [];

export const FALLBACK = {
  hubOpen: false,
  contract: CONTRACT_ABSENT,
  buildStamp: "",
  /** The setting's default is true; without C# the button opens the "not connected" notice. */
  showHubButton: true,
  build: EMPTY_BUILD,
  glade: { available: false, active: false, level: GLADE_LEVEL_DEFAULT, treeLine: false, reason: "" } as GladeState,
  resorts: EMPTY_RESORTS,
  resortLive: EMPTY_RESORT_LIVE,
  selectedResort: NULL_ENTITY,
  runs: EMPTY_RUNS,
  runLive: EMPTY_RUN_LIVE,
  lifts: EMPTY_LIFTS,
  problems: EMPTY_PROBLEMS,
  city: {
    known: false, daysPerYear: 0, scaleK: 4, backcountryPct: 40, ticketPrice: 0, snowGating: true,
    snowmakingMarginC: 2, snowVisualsOff: false, tourismOverhaul: false, realisticWorkplaces: false,
  } as CityStats,
  /** D19: the piste map is on by default (C# owns the value). */
  pisteMap: true,
  follow: 0,
} as const;

// ---- value bindings -----------------------------------------------------------------------------

/** Is the hub panel open. C# owns it (launchers toggle, Escape/X close, selecting a sign opens). */
export const hubOpen$: ValueBinding<boolean> = bindValue<boolean>(GROUP, BINDING.hubOpen, FALLBACK.hubOpen);
/** C#'s contract number (HUB_CONTRACT on a match; CONTRACT_ABSENT when C# lacks the binding, or
 *  after the C# hub system's one-shot exception until the next load). */
export const contract$: ValueBinding<number> = bindValue<number>(GROUP, BINDING.contract, FALLBACK.contract);
/** The DLL's build stamp (SkiResortMod.BuildStamp.Value). */
export const buildStamp$: ValueBinding<string> = bindValue<string>(GROUP, BINDING.buildStamp, FALLBACK.buildStamp);
/** Setting ShowHubButton: show the top-left launcher. */
export const showHubButton$: ValueBinding<boolean> = bindValue<boolean>(GROUP, BINDING.showHubButton, FALLBACK.showHubButton);
/** The Build strip. Subscribe only while the panel is open. CORE. */
export const build$: ValueBinding<readonly BuildItem[]> = bindValue<readonly BuildItem[]>(GROUP, BINDING.build, FALLBACK.build);
/** The tree tool. Subscribe only while the panel is open. CORE. */
export const glade$: ValueBinding<GladeState> = bindValue<GladeState>(GROUP, BINDING.glade, FALLBACK.glade);
/** One row per sign, then the Not linked row (always last). Panel only. */
export const resorts$: ValueBinding<readonly ResortRow[]> = bindValue<readonly ResortRow[]>(GROUP, BINDING.resorts, FALLBACK.resorts);
/** The moving values of each `resorts` row (contract 3). Panel only: read by leaf components only. */
export const resortLive$: ValueBinding<readonly ResortLive[]> = bindValue<readonly ResortLive[]>(GROUP, BINDING.resortLive, FALLBACK.resortLive);
/** The area the hub shows: a sign, or Entity.Null = the Not linked view. Panel only. */
export const selectedResort$: ValueBinding<Entity> = bindValue<Entity>(GROUP, BINDING.selectedResort, FALLBACK.selectedResort);
/** One row per run of ours. Panel only (never read on a contract mismatch). */
export const runs$: ValueBinding<readonly RunRow[]> = bindValue<readonly RunRow[]>(GROUP, BINDING.runs, FALLBACK.runs);
/** The moving values of each `runs` row (contract 3). Panel only. */
export const runLive$: ValueBinding<readonly RunLive[]> = bindValue<readonly RunLive[]>(GROUP, BINDING.runLive, FALLBACK.runLive);
/** One row per lift. Panel only. */
export const lifts$: ValueBinding<readonly LiftRow[]> = bindValue<readonly LiftRow[]>(GROUP, BINDING.lifts, FALLBACK.lifts);
/** The problems band and the efficiency factors of every area (contract 3). Panel only. */
export const problems$: ValueBinding<readonly ProblemRow[]> = bindValue<readonly ProblemRow[]>(GROUP, BINDING.problems, FALLBACK.problems);
/** City-wide settings and session facts. Panel only. */
export const city$: ValueBinding<CityStats> = bindValue<CityStats>(GROUP, BINDING.city, FALLBACK.city);
/** D19: the piste map toggle as C# holds it (contract 3). Panel only. */
export const pisteMap$: ValueBinding<boolean> = bindValue<boolean>(GROUP, BINDING.pisteMap, FALLBACK.pisteMap);
/** D36: a counter C# moves on each automatic area switch (selection rule 7). Panel only. */
export const follow$: ValueBinding<number> = bindValue<number>(GROUP, BINDING.follow, FALLBACK.follow);

// ---- triggers -----------------------------------------------------------------------------------

function fire(name: string, ...args: unknown[]): void {
  try {
    trigger(GROUP, name, ...args);
  } catch (e) {
    // Never let a trigger failure escape into React or the registrar.
    console.error(`SkiResortMod UI: trigger ${GROUP}.${name} failed`, e);
  }
}

/** Every UI -> C# call. C# applies each one in OnUpdate and writes one log line per call. */
export const hubTriggers = {
  /** Flip hubOpen. */
  toggleHub: (): void => fire(TRIGGER.toggleHub),
  /** Set hubOpen (the panel's X / Escape send false). */
  setHubOpen: (open: boolean): void => fire(TRIGGER.setHubOpen, open),
  /** Handshake, fired by the always-mounted HubRoot; C# logs "UI hub connected" or the mismatch. */
  uiHello: (contract: number, bundleStamp: string): void => fire(TRIGGER.uiHello, contract, bundleStamp),
  /** From the error boundary (src/hub/report.ts dedupes per message); C# logs once per message. */
  uiError: (message: string): void => fire(TRIGGER.uiError, message),
  /** Start the tool of a Build item (or return to the default tool when it is already active).
   *  Sent for locked items too: C# refuses and logs "refused - locked" (spec 4.2). */
  launch: (id: BuildItemId): void => fire(TRIGGER.launch, id),
  /** Fly the camera to a run (zoom to fit). */
  focusRun: (run: Entity): void => fire(TRIGGER.focusRun, wire(run)),
  /** Tree tool on/off. */
  gladeToggle: (): void => fire(TRIGGER.gladeToggle),
  /** Tree density level, clamped 1..8 here and again in C#. */
  gladeSetLevel: (level: number): void => fire(TRIGGER.gladeSetLevel, clampGladeLevel(level)),
  /** false = run interior, true = tree line. */
  gladeSetTreeLine: (treeLine: boolean): void => fire(TRIGGER.gladeSetTreeLine, treeLine),
  /** Dev aid: C# dumps the hub state to the mod log. */
  dumpHub: (): void => fire(TRIGGER.dumpHub),
  /** Show an area: a live sign, or Entity.Null = the Not linked view. Never selects the sign in
   *  the world (OD-14). */
  selectResort: (sign: Entity): void => fire(TRIGGER.selectResort, wire(sign)),
  /** Rename our sign, run or station (a lift row: its base station, else its top station).
   *  Trimmed and cut to 64 here and again in C#; "" restores the default name. */
  rename: (entity: Entity, name: string): void => fire(TRIGGER.rename, wire(entity), name),
  /** Fly to a lift: (row `entity`, row `top`); C# flies to the gap when a section is missing. */
  focusLift: (lift: Entity, top: Entity): void => fire(TRIGGER.focusLift, wire(lift), wire(top)),
  /** Fly to a building (sign, lodge or station) without selecting it. */
  focusBuilding: (building: Entity): void => fire(TRIGGER.focusBuilding, wire(building)),
  /** Dev aid: measured panel heights; C# logs `Hub [layout] ...` once per distinct text. */
  uiLayout: (text: string): void => fire(TRIGGER.uiLayout, text),
  /** D19: set the piste-map toggle; C# owns the value (pisteMap$) and logs `Hub [piste] map on|off`. */
  setPisteMap: (on: boolean): void => fire(TRIGGER.setPisteMap, on),
  /** D19: the hovered run (Null clears). Send through hubState.ts sendHoverRun only (dedup). */
  hoverRun: (run: Entity): void => fire(TRIGGER.hoverRun, wire(run)),
} as const;
