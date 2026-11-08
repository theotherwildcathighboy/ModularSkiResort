/*
 * src/bindings.ts - THE single source of truth for the C# <-> UI contract of the Ski Resort hub.
 * C# (SkiResortHubUISystem, group "skiResort") must register EXACTLY these names with these shapes. To change a name, a type or a
 * field, change it here and in C# together. Then bump HUB_CONTRACT in both files, run `npm run contract:lock` and review the diff of
 * tools/contract.lock. tools/check-bundle.js reads the literal shapes of this file (HUB_CONTRACT, BINDING / TRIGGER, CORE_NAMES,
 * interfaces, R16 / R17). Keep them literal.
 * Engine notes: EngineNotes/bindings.ts.md
 */
import { bindValue, trigger } from "cs2/api";
import type { ValueBinding } from "cs2/api";
import type { Entity } from "cs2/utils";

// ---- identity -----------------------------------------------------------------------------------

/** Binding group of every value and trigger in this file. */
export const GROUP = "skiResort";

/**
 * Bump both this and C# SkiResortHubUISystem.HubContract whenever a binding, trigger, field or meaning below changes.
 * tools/check-bundle.js compares against tools/contract.lock and fails a change without a bump. The panel renders the resort views
 * only when C#'s `contract` equals this. On a mismatch, the panel shows the Build strip and the tree controls (CORE only) and
 * says why.
 */
export const HUB_CONTRACT = 40;

/** The `contract` value while the C# binding is absent (the fallback). Real contracts start at 1. */
export const CONTRACT_ABSENT = 0;

/**
 * This bundle's build stamp (webpack DefinePlugin). Compare with buildStamp$ (the DLL's
 * SkiResortMod.BuildStamp.Value). Equal stamps mean the DLL and this bundle came from the same C# build.
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

// ---- enumerations (C# enums of the same values. Gate rule R17 compares them) ---------------------

/** Run type. C# RunKind (EntityRef.cs): append only, never renumbered. */
export type RunKind = 0 | 1 | 2 | 3 | 4;
export const RUN_KIND = { Groomed: 0, Snowmaking: 1, Night: 2, Natural: 3, CatTrack: 4 } as const;

/** RunRow.paidSteps bits (C#: SkiRunFees.PaidGroomed / PaidSnowmaking / PaidLights, contract 33). */
export const RUN_PAID = { Groomed: 4, Snowmaking: 8, Lights: 16 } as const;

/** RunRow.band: -2 cannot be rated (final for the current outline and terrain), -1 not
 *  rated yet, 0 Flat .. 4 DoubleBlack (C#: SkiRunRatingRules.RowBand). The values are literal ints, with no enum. */
export const BAND = { Unrateable: -2, Pending: -1, Flat: 0, Green: 1, Blue: 2, Black: 3, DoubleBlack: 4 } as const;

/** ResortLive.varietyBands bits (contract 40). C#: SkiResortScore.VarietyGreen / VarietyBlue / VarietyBlack / VarietyAll (literal ints,
 *  ResortBatch guards the values). Black covers double-black: either one sets it. */
export const VARIETY_BAND = { Green: 1, Blue: 2, Black: 4, All: 7 } as const;

/** LiftRow.state (stats rules). The values are literal ints in C#, with no enum. */
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
export const LIFT_STATUS = { Running: 0, NoPower: 1, ChairsStopped: 2, SectionMissing: 3, Unfinished: 4, Closed: 5 } as const;  // Closed: a held lift, no open run

/** RunLive.gunWhy. C# HubRules RunGunWhy. */
export const RUN_GUN_WHY = { None: 0, Making: 1, DepthReached: 2, TooWarm: 3, OutOfSeason: 4, Waiting: 5 } as const;

/** ResortLive.ticketGate. C# SkiMoneyRules.TicketGate (chapter 4). */
export const TICKET_GATE = { Open: 0, Off: 1, NoEfficiency: 2, NothingOpen: 3, NotKnownYet: 4 } as const;

/** ProblemRow.code. C# HubRules HubProblemCode (append only). */
export const PROBLEM = {
  Unlinked: 0, NoLodge: 1, LodgesAllOff: 2, LodgesSomeOff: 3, Efficiency: 4, LiftNoPower: 5,
  LiftSectionMissing: 6, LiftUnfinished: 7, NoLift: 8, NoRuns: 9, StationsOnly: 10, LiftChairsStopped: 11,
  RunsUnserved: 12, RunsUnrateable: 13, RunsRating: 14, FreeLodges: 15, Factor: 16,
  RunsFlat: 17,
  SignNoRoad: 18,  // the Welcome Sign's Building.m_RoadEdge is null (C# HubProblemCode.SignNoRoad)
  LodgeNoRoad: 19,  // a lodge of the area has Building.m_RoadEdge null. `subject` = the first such lodge
  SkiDaysCannotReach: 20,  // most car ski-day guests never arrived (sev 2). `subject` = that lodge, else the sign
  NoParkingNearLodge: 21,  // ski-day lodges with 0 car spaces within 600 m (sev 1). Uses `count`
  LodgeNotSlopeside: 22,  // active lodges more than 800 m from the runs and stations (sev 1). Uses `count`
  MixedLiftTypes: 23,  // a lift's line and stations span two of our lift types (sev 2). Counts are 0
  SummitLodgeNotLiftServed: 24,  // summit-bonus lodges no working lift's top reaches (sev 1). Uses `count`
  SchoolNoGreenRun: 25,  // the ski school is on but no green run counts (sev 1). `subject` = the top-level school
  SchoolsNotLinked: 26,  // top-level ski schools on no sign, Not linked entry (sev 1). Uses `count`. `subject` = the first
  SkiDaysNoGuestsNear: 27,  // an open hill with almost no homes or hotels within a ski day's drive (sev 2). `detail` = the nearest in tenths of a km
  DepotNoHillLodge: 28,  // a bus depot's area has no passing hill lodge (sev 1). Uses `count`. `subject` = the first such depot
  DepotNoRoad: 29,  // a bus depot has no road (sev 2). Uses `count`. `subject` = the first such depot
  LiftBend: 30,  // a complete or open lift's sharpest tower bend in plan is over 5 deg (sev 1). `count` = the tower number. `detail` = tenths of a degree. `subject` = the tower's net node, `subjectTop` null
  LiftTrainLimit: 31,  // a complete or open lift has a tower whose rope goes past what its sheave train follows (sev 1). `count` = the tower number. `subject` = the tower's net node, `subjectTop` null
  LiftLowSpan: 32,  // a complete or open lift has a span whose carriers pass under the floor (sev 1). `count` / `more` = the tower numbers at its ends (0 = a station). `detail` = their height above the ground in tenths (0 = at or below it). `pct` = 1 cabins / 0 chairs. `subject` = the span's net edge, `subjectTop` null
} as const;

/** ResortLive.skiDaysState. C# HubRules HubSkiDayState. */
export const SKI_DAY_STATE = { Unknown: 0, Off: 1, SignOff: 2, NoRun: 3, NoLodge: 4, Night: 5, Room: 6, Full: 7 } as const;

/** ResortLive.skiDaysLimit. C# SkiDayRules SkiDayLimit: which term sets the capacity. */
export const SKI_DAY_LIMIT = { None: 0, Runs: 1, Lodges: 2 } as const;

/** Build-strip ids: the 26 ids of the ONE C# table, SkiCatalog.cs BUILD-ITEMS, in its order. Gate
 *  rule R16 compares the union, this list, BUILD_ITEM_PREFAB and buildItems.ts BUILD_ITEMS / BUILD_GROUPS /
 *  BUILD_ITEM_ICON with that table. C# owns what each id launches and which ids it SENDS. C# does not send a pending prefab
 *  that is not imported, and it does not send any item of a Shuttle lift type. The strip draws
 *  BUILD_GROUPS. */
export type BuildItemId =
  | "sign" | "lodge" | "lodgeHut" | "lodgeMid" | "lodgeSummit" | "lodgeGrand" | "busDepot"
  | "school" | "patrolHut" | "patrolHQ"
  | "baseStation" | "topStation" | "lift"
  | "baseStationDetachable" | "topStationDetachable" | "liftDetachable"
  | "baseStationGondola" | "topStationGondola" | "liftGondola"
  | "baseStationTram" | "topStationTram" | "liftTram"
  | "runNatural" | "runGroomed" | "runSnowmaking" | "runNight" | "runCatTrack"
  | "safetyFence";
export const BUILD_ITEM_IDS: readonly BuildItemId[] = [
  "sign", "lodge", "lodgeHut", "lodgeMid", "lodgeSummit", "lodgeGrand", "busDepot",
  "school", "patrolHut", "patrolHQ",
  "baseStation", "topStation", "lift",
  "baseStationDetachable", "topStationDetachable", "liftDetachable",
  "baseStationGondola", "topStationGondola", "liftGondola",
  "baseStationTram", "topStationTram", "liftTram",
  "runNatural", "runGroomed", "runSnowmaking", "runNight", "runCatTrack",
  "safetyFence",
];

/** Display only: the prefab behind each id (rule R16 compares it with the C# table row's Names.X value). */
export const BUILD_ITEM_PREFAB: Readonly<Record<BuildItemId, string>> = {
  sign: "SkiResortGate",
  safetyFence: "SkiSafetyFence",
  lodge: "SkiAFrameDayLodge",
  lodgeHut: "SkiLodgeHut",
  lodgeMid: "SkiLodgeMid",
  lodgeSummit: "SkiLodgeSummit",
  lodgeGrand: "SkiLodgeGrand",
  busDepot: "SkiBusDepot",
  school: "SkiSchool",
  patrolHut: "SkiPatrolHut",
  patrolHQ: "SkiPatrolHQ",
  baseStation: "SkiBaseStationBuilding",
  topStation: "SkiTopStationBuilding",
  lift: "SkiChairliftHV",
  baseStationDetachable: "SkiDQBaseStationBuilding",
  topStationDetachable: "SkiDQTopStationBuilding",
  liftDetachable: "SkiDetachableHV",
  baseStationGondola: "SkiGondolaBaseStationBuilding",
  topStationGondola: "SkiGondolaTopStationBuilding",
  liftGondola: "SkiGondolaHV",
  baseStationTram: "SkiTramBaseStationBuilding",
  topStationTram: "SkiTramTopStationBuilding",
  liftTram: "SkiTramHV",
  runNatural: "SkiRunNaturalA",
  runGroomed: "SkiRunGroomedA",
  runSnowmaking: "SkiRunSnowmakingA",
  runNight: "SkiRunNightA",
  runCatTrack: "SkiRunCatTrackA",
};

/** LiftRow.type: C# SkiCatalog.Lifts[i].Id, APPEND ONLY (Lifts[0] = fixedGrip forever). "" = unknown. Display only:
 *  the hub maps it to LIFT_TYPE[...] (hubText.ts). BUILD_ITEMS rows carry the same ids (gate R16). */
export const LIFT_TYPE_IDS: readonly string[] = ["fixedGrip", "detachable", "gondola", "tram"];

// ---- value shapes (C# -> UI). C# structs mirror them field for field -----------------------------

/** One Build-strip entry. CORE: only optional fields may be added. */
export interface BuildItem {
  /** One of BUILD_ITEM_IDS (typed as string: treat an unknown id as "not for this bundle"). */
  id: string;
  /** Locked by progression, a missing prefab, no sign for an upgrade item, or already on the shown
   *  sign (ForbidMultiple applies to the school. It applies to a lodge tier only if its prefab still has m_ForbidMultiple.
   *  The prefab flag decides until the surgery sets it false). `reason` says why. */
  locked: boolean;
  /** This item's tool is the active tool (clicking it again returns to the default tool). */
  active: boolean;
  /** A locale key explaining `locked`, or "". */
  reason: string;
  /** Run items: base fee (SkiRunFees.BaseFee). -1 for other items. */
  price?: number;
  /** A locale key or "" (line items: HINT[LiftNoPair] for the SHOWN area's rows of that lift type). */
  hint?: string;
  /** Run items: fee per hectare (SkiRunFees.PerHaFee). -1 for other items. */
  pricePerHa?: number;
  /** Sign and stations: construction cost. Lodges, school, patrol huts: upgrade cost. -1 unknown. */
  cost?: number;
  /** Line items: line construction per km (the type's row). -1 for other items. */
  costPerKm?: number;
  /** Line items: fixed running cost per complete lift a month (the type's row). -1 for other items. */
  monthly?: number;
  /** Run items: running cost per active hectare a month. -1 for other items. */
  monthlyPerHa?: number;
  /** Line items: running cost per km of line a month (the type's row). -1 for other items. */
  monthlyPerKm?: number;
  /** The Run tile only (contract 33). Groomed off: the run pays the natural fee and nothing a month. Snow guns and lights each add a one-time
   *  step (base + per ha) and an upkeep step per ha a month. -1 on every other item. C# writes every field on every BuildItem:
   *  one shape per __Type, see SkiResortHubUISystem. */
  naturalPrice?: number;
  naturalPerHa?: number;
  gunsPrice?: number;
  gunsPerHa?: number;
  gunsMonthlyPerHa?: number;
  lightsPrice?: number;
  lightsPerHa?: number;
  lightsMonthlyPerHa?: number;
}

/** `glade`: the tree tool (SkiGladeToolSystem, density dial). CORE. */
/** The coach's state on the Ski Bus card (C# BusCardRunning and its neighbors). */
export type BusCardStateName = "running" | "noRoad" | "noHillLodge" | "off";

/** The Ski Bus card beside the game's selected-info panel. Sent while a coach or a depot is selected. */
export interface BusCardState {
  shown: boolean;
  /** The ski area's name, "" until the stats rows hold it or when the depot serves none. */
  area: string;
  /** Ski days that arrived at the area's depots this month, -1 = unknown. */
  guestsMonth: number;
  /** This depot's coach bill per month at today's rate, 0 while it is not billed. */
  billMonth: number;
  /** Straight line to the hill lodge, what the bill counts, 0 = unknown. The road route is not kept: a coach's own PathInformation reads 0 once it drives. */
  distanceM: number;
  state: BusCardStateName;
}

/** Contract 32: a run re-emitted under a new entity (Groomed toggled, DECISIONS 577). seq counts replacements this load. 0 = none. */
export interface RunReplaced {
  seq: number;
  from: Entity;
  to: Entity;
}

/** Contract 33: the Run tile's option row (run types R3). Active while the area tool draws one of our pistes. `groomed` picks the look it
 *  draws. The spacings are the sliders a new groomed run starts with (0 = off). */
export interface DrawRun {
  active: boolean;
  groomed: boolean;
  gunSpacingM: number;
  lightSpacingM: number;
}

/** Contract 35: the merge card beside the game's selected-info panel. Sent while the city has a ski area and one of these is selected:
 *  a merged building, or a building that could be merged (a lot too small included). Numbers: C# MergeRules.Refusal / State,
 *  SkiMergeSystem.Outcome. */
export interface MergeCardState {
  shown: boolean;
  building: Entity;
  /** The building's name as the game shows it. */
  name: string;
  /** The ski area it counts for, "" when none. */
  area: string;
  /** SkiCatalog.Lodges index of its tier. -1 = not merged. */
  tier: number;
  /** Bit t = tier t fits the lot. */
  allowedMask: number;
  /** MergeRules.Refusal: 0 none, 9 lot too small (the only refusal the card is sent for). */
  refusal: number;
  /** MergeRules.State: 0 counted, 1 no area, 2 too far from the runs, 3 asset changed, 4 switched off, 5 destroyed, 6 unknown. */
  state: number;
  /** The last action on this building: 0 none, 1 merged, 2 tier changed, 3 unmerged, 4 refused, 5 not enough money, 6 lot too small. */
  outcome: number;
  /** Per tier: what merging (or changing) to it costs now. */
  charge: readonly number[];
  /** Per tier: its monthly fee. */
  monthly: readonly number[];
  /** Contract 37: within reach of any ski area's runs or lift stations now (C# NearAnySlopeside). A merged building's state says it. */
  near: boolean;
  /** Contract 39: some ski area has a run outline or a lift station (C# TryGetSlopesideBounds). False in a fresh ski area with only a sign:
   *  nothing is near, and the card warns and turns Merge off. */
  geometry: boolean;
  /** Contract 39: C# SkiDayRules.SlopesideMaxM in whole meters, the {M} of the warning text. */
  reachM: number;
}

/** Contract 36: the merge overlay (C# SkiMergeOverlaySystem). While armed, the overlay outlines the buildings near a ski area that
 *  could be merged. It outlines a building green when a lodge tier fits the lot and gray when the lot is too small for every tier. */
export interface MergeOverlayState {
  armed: boolean;
  /** Outlined green (a tier fits). 0 while disarmed. */
  allowed: number;
  /** Outlined gray (lot too small). 0 while disarmed. */
  tooSmall: number;
  /** Contract 39: as MergeCardState.geometry. False: the overlay has nothing to outline, and the chip says so. */
  geometry: boolean;
  /** Contract 39: as MergeCardState.reachM. */
  reachM: number;
}

/** Contract 35: one merged building, any state (the Overview's Merged buildings fold). */
export interface MergedLodgeRow {
  /** Its ski area's sign, Entity.Null = none. */
  sign: Entity;
  building: Entity;
  name: string;
  tier: number;
  /** MergeRules.State, as MergeCardState.state. */
  state: number;
  /** Its monthly fee while billed, else 0. */
  monthly: number;
}

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

/** `liftLine`: the lift-line generator (LiftLineToolSystem, the lift planner as a tool). Shortcut: Ctrl+Shift+L. Panel only. */
export interface LiftLineState {
  /** A city is loaded and the tool exists (it may still refuse: not armed, or disabled after an exception. C# logs why). */
  available: boolean;
  /** The tool is the active tool. */
  active: boolean;
  /** LiftLineToolSystem.ToolState name: PickBase, PickTop, Edit, Emitted, Applying ("" while unknown). */
  state: string;
}

/** One top cluster of the Quick runs preview: `label` is a locale key (QUICK_POD[Beginner], QUICK_POD[MidStation], QUICK_CLUSTER). */
export interface QuickRunsCluster {
  label: string;
  /** Runs the cluster's top serves. */
  runs: number;
}

/** One pod of the Resort Planner's plan (SkiRunAutoPlanner.Plan.cs PlanPod + the hub's totals over its runs). Empty list unless a plan shows. */
export interface QuickRunsPod {
  /** Index in the plan. The UI letter is `letter` (A, B, ...). */
  id: number;
  /** The stable toggle key (a signed int: the plan's unsigned FNV key, sent back as is in quickRunsSet("pod", key)). */
  key: number;
  letter: string;
  /** A locale key: the pod's Ecosign class (SkiResort.Tooltip.QUICK_CLASS[*]). */
  classKey: string;
  /** Runs in the pod, and how many are switched on (the pod, its lift and single runs count). */
  runs: number;
  enabledRuns: number;
  /** Enabled runs by SlopeBand (0 Flat, 1 Green, 2 Blue, 3 Black, 4 Double black). */
  count: number[];
  skiers: number;
  fee: number;
  /** Neither the pod nor its lift is switched off. */
  on: boolean;
  /** PlanLift.Id of the lift serving it, -1 = unserved. */
  liftId: number;
  /** A locale key: the lift's way-home verdict (SkiResort.Tooltip.QUICK_VERDICT[*]), "" = none / unserved. */
  homeKey: string;
}

/** One lift of the plan: proposed (origin 0) or an existing one planned around (origin 1, pinned). */
export interface QuickRunsLift {
  id: number;
  /** The stable toggle key (signed int, as QuickRunsPod.key). Sent in quickRunsSet("lift", key). */
  key: number;
  /** LiftOrigin: 0 Proposed, 1 Pinned. */
  origin: number;
  /** 1-based place among the lifts of its origin: "P1" for a proposal, "Existing lift" for a pinned one (no number). */
  ordinal: number;
  /** The lift type id (SkiCatalog LiftTypeDef.Id: fixedGrip, detachable, gondola, tram) for LIFT_TYPE. "" when unknown. */
  typeKey: string;
  /** Plan pod index the lift serves (-1 none). */
  pod: number;
  lengthM: number;
  riseM: number;
  /** Stations plus line (SkiCatalog), 0 for a pinned lift. */
  cost: number;
  serves: number;
  startsNearTop: number;
  /** Locale key of the way-home verdict (SkiResort.Tooltip.QUICK_VERDICT[*]), "" = none. */
  homeKey: string;
  /** Locale key of the level the Way home knob asked for here (SkiResort.Hub.QUICK_HOME_GREEN / BLUE), "" = unresolved. */
  wantedKey: string;
  /** The lift is switched on (off = it and the runs of its pod are left out). */
  on: boolean;
}

/** The plan's summary line numbers. All 0 without a plan. */
export interface QuickRunsPlan {
  pods: number;
  liftsProposed: number;
  liftsPinned: number;
  runKm: number;
  runKmPerKm2: number;
  deadEnds: number;
  /** The terrain's honest mix in whole percents of the run km. [0] Flat, [1] Green, [2] Blue, [3] Black and Double black together,
   *  [4] always 0. [1..3] are the shares the hub and the tooltip print (one rounded value each). [3] = the rated total minus [1] and [2]. */
  mountainMix: number[];
  /** The site verdict's detail line (sitePercent / siteTier stay the line). The next three fields hold: pods a lift serves, how many of
   *  them have their way home (the verdict meets the level asked for), and the largest bench in ha (one decimal). The field keeps its
   *  stage A name. */
  sitePods: number;
  siteGreenHome: number;
  siteBenchHa: number;
}

/** `quickRuns`: the Resort Planner tool (code name Quick runs, QuickRunsToolSystem). Experimental, Ctrl+Shift+Q. Panel only. Every count,
 *  total, the cluster list and the plan fields are 0 / empty unless `phase` is Preview with a plan. */
export interface QuickRunsState {
  /** The tool can open: the option is on, it is armed and not disabled. */
  available: boolean;
  /** The tool is the active tool. */
  active: boolean;
  /** A locale key explaining `available == false` (SkiResort.Hub.QUICK_REASON[*]), or "". */
  reason: string;
  /** QuickRunsToolSystem.ToolPhase name: Region, Scanning, Preview, Emitted, Retry, Applying. */
  phase: string;
  /** Region corners placed. */
  corners: number;
  /** The census knows a lift station: a click on one plans runs around it (the Region step's hint is true only then). */
  canPlanAround: boolean;
  areaM2: number;
  /** The area cap (9 km2, SkiRunAutoPlanner.V1MaxAreaM2). */
  maxAreaM2: number;
  /** Top to bottom of the region. */
  verticalM: number;
  /** QuickRegionState name: Open, TooSmall, Ok, Near, Over. */
  regionState: string;
  /** Scan progress 0..1 while Scanning. */
  progress: number;
  /** AutoMix: 0 Balanced, 1 Beginner, 2 Expert, 3 As the terrain allows. */
  mix: number;
  /** AutoDensity: 0 Sparse, 1 Normal, 2 Dense. */
  density: number;
  /** AutoKinds flags: 1 Groomed, 2 Natural, 4 Snowmaking, 8 Night. */
  kinds: number;
  /** Candidates in the plan. */
  runs: number;
  /** Candidates switched on. */
  enabled: number;
  /** Enabled runs by SlopeBand (0 Flat, 1 Green, 2 Blue, 3 Black, 4 Double black). */
  count: number[];
  /** The mix target by SlopeBand. */
  target: number[];
  /** Total construction fee of the enabled runs. */
  fee: number;
  /** Skiers a day the enabled runs hold once served. */
  skiers: number;
  /** The enabled runs meet the mix target. */
  variety: boolean;
  /** A locale key: why the last scan found nothing (QUICK_STATUS[*]), or "". */
  status: string;
  /** Locale keys (QUICK_WARN[*]). */
  warnings: string[];
  clusters: QuickRunsCluster[];
  /** Runs of the last build that Undo removes (with a refund). */
  batchRuns: number;
  /** A locale key for the latest notice (QUICK_*), or "". */
  notice: string;
  /** The tree filler is on. */
  trees: boolean;
  /** Tree density level, GLADE_LEVEL_MIN..GLADE_LEVEL_MAX. */
  treeLevel: number;
  /** Tree line height in meters. -1 = none (trees everywhere). */
  treeLine: number;
  /** The region's lowest and highest ground, the tree line slider's range. */
  groundMin: number;
  groundMax: number;
  /** Species group chip: 0 Conifer, 1 Deciduous, 2 Palm, 3 All trees (in force while `species` is -1). */
  group: number;
  /** One species: an index into speciesNames. -1 = the group. */
  species: number;
  /** Every tree prefab with wood, sorted. */
  speciesNames: string[];
  /** Each species' SkiGladeSpecies.TreeGroup (0 Conifer, 1 Deciduous, 2 Palm, 3 Bush, 4 Ungrouped). */
  speciesGroups: number[];
  /** Trees still to plant for the whole slope (not one slice), and trees the filler removes now. */
  plant: number;
  remove: number;
  /** Planted trees Undo removes. */
  treesUndo: number;
  /** "Building runs N / M": runs built by the build in flight / runs it builds. buildTotal is 0 when no build runs. */
  buildDone: number;
  buildTotal: number;
  /** Plant pressed: slices apply until the slope is complete or Stop. */
  planting: boolean;
  /** "Planting trees N / M" (planting) or "Removing trees N / M" (Undo trees in flight: planting false, treesTotal > 0). treesTotal is 0
   *  when neither runs. */
  treesDone: number;
  treesTotal: number;
  /** What Plant will cost (money): the ready preview's Temp cost per tree times `plant` (exact up to one slice). 0 without a
   *  ready preview. */
  treesCost: number;
  /** Why the scan dropped lines (contract 20). The QUICK_REJECT_BUCKETS counts follow this order (QuickRunsRules.RejectBuckets): too short,
   *  overlap other runs, too close to the region edge, blocked by other areas, too narrow, too twisted, too flat, other. All 0 unless the
   *  scan has a result (a preview, or the refusal shown in the Region step). */
  rejects: number[];
  /** The site verdict (contract 21): the region's mean terrain grade as a whole percent (the corner phase's estimate, then the scan's
   *  figure), -1 = unknown. `siteTier` is its tier (QuickRunsRules.SiteTier: 0 unknown, 1 Gentle, 2 Steep, 3 Very steep, 4 Beyond any real area). */
  sitePercent: number;
  siteTier: number;
  /** The Lifts knob: 0 Auto, 1-9 a cap on the proposed lifts. */
  liftsKnob: number;
  /** The Way home knob (HomeLevel): 0 Auto (green at the base, blue above), 1 Green, 2 Blue. */
  home: number;
  /** The plan, only while a Resort preview shows. */
  pods: QuickRunsPod[];
  planLifts: QuickRunsLift[];
  plan: QuickRunsPlan;
}

/** The length of QuickRunsState.rejects (QuickRunsRules.RejectBuckets). */
export const QUICK_REJECT_BUCKETS = 8;

/** The quickRunsSet keys (QuickRunsToolSystem.HubSet). */
export type QuickRunsKey =
  | "mix" | "density" | "kind" | "shuffle" | "scan" | "build" | "undo"
  | "trees" | "plant" | "treesUndo" | "treeLine" | "group" | "species" | "level" | "stop"
  | "lifts" | "home" | "pod" | "lift";

/** RunPropPlacerRules: the Runs view's prop rows, 0 = off, then min..max (tools/check-run-props.js compares them). */
export const RUN_PROP_SPACING_OFF = 0;
export const RUN_PROP_SPACING_MIN = 20;
export const RUN_PROP_SPACING_MAX = 200;
export const RUN_PROP_SPACING_STEP = 10;
export const RUN_PROP_SPACING_STEP_COARSE = 50;

export const GLADE_LEVEL_MIN = 1;
export const GLADE_LEVEL_MAX = 8;
/** SkiGladeCompose.DefaultLevel. */
export const GLADE_LEVEL_DEFAULT = 4;

/** One ski area (a live Welcome Sign) or the Not linked row (always last): identity and slow facts. */
export interface ResortRow {
  /** The sign. Entity.Null on the Not linked row. */
  sign: Entity;
  /** 0 = a sign. 1 = the "Not linked" row: always sent, always last. */
  kind: 0 | 1;
  /** The sign's custom name, else "Ski area N" (C#). The UI ignores it on the Not linked row. */
  name: string;
  customName: boolean;
  runs: number;
  /** Runs with a rating (band >= 0). "being rated" = runs - rated - unrateable (counted from runs). */
  rated: number;
  flat: number;
  green: number;
  blue: number;
  black: number;
  doubleBlack: number;
  /** m^2, plan area of the runs. */
  areaM2: number;
  /** Highest run top - lowest run bottom (stations no longer count). -1 = no run. */
  verticalM: number;
  /** true while any run of the area is still being rated (node estimates). */
  verticalApprox: boolean;
  lodges: number;
  /** Lodge-prefab buildings on no live sign (Not linked row). */
  freeLodges: number;
  /** Census rows of the area (complete lifts, unfinished lines, lone stations). */
  liftRows: number;
  liftLengthM: number;
  /** Design capacity of the complete lifts, riders/h at 1x. -1 none. */
  capacityPerHour: number;
}

/** Per-area values that move (same order and count as `resorts`, joined by `sign`). Score fields
 *  are the model's Result (never recomputed in the UI). -1 = unknown or no result. */
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
  /** The first provider write after a load is held: the game keeps the saved score (show "..."). */
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
  /** A whole month was counted. Last-month fields are -1 only when no month rolled since counting
   *  started. With lastMonthFull false, they hold a partial month (shown with "(partial)"). */
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
  /** Run upkeep a month by feature at today's rate (contract 33, SkiRunUpkeepRates.PisteUpkeep). Grooming and lights: a groomed run's
   *  hectares with snow (a gun run's at depth too). Snowmaking: only where its guns make snow. Cat tracks: by plan area while their guns
   *  run. */
  costGroomingMonth: number;
  costSnowmakingMonth: number;
  costLightsMonth: number;
  costCatTracksMonth: number;
  /** Their hectares. Natural runs: by plan area (billed nothing). */
  haNatural: number;
  haGroomed: number;
  haSnowmaking: number;
  haLit: number;
  haCatTracks: number;
  liftCostMonth: number;
  liftsBilled: number;
  liftsBilledReduced: number;
  buildingsMonth: number;
  wagesMonth: number;
  jobs: number;
  jobsFilled: number;
  /** The one net (C# HubRules.NetMonth): this month's booked income - costs, so far. The nine rows that add up to
   *  it: ticketsThisMonth + lodgeIncomeMonth + lessonIncomeMonth + passIncomeMonth - liftChargedMonth - runCostMonth - buildingsSoFar -
   *  wagesSoFar - busChargedMonth (each max(0, row)). Not linked: -(lift charge + run upkeep booked for Not linked). Used as sent. */
  netMonth: number;
  /** Some term of the net is unknown (read as 0). */
  netEstimated: boolean;
  // Ski days: appended in this order, as in C# ResortLiveStats.
  /** SKI_DAY_STATE (0 Unknown, also on the Not linked row). */
  skiDaysState: number;
  /** Ski-day guests who reached a lodge this month. -1 unknown. */
  skiDaysThisMonth: number;
  /** The same, last month. -1 until a month rolled since counting started (partial when !lastMonthFull). */
  skiDaysLastMonth: number;
  /** Ski-day claims this month, all classes. -1 unknown. */
  skiDaysSent: number;
  /** Of those, class Car (farther than walking, had a car). -1 unknown. */
  skiDaysSentCar: number;
  /** Of those, class "far, no car". -1 unknown. Walk = sent - car - no car (hubRules.skiDayWalk). */
  skiDaysSentNoCar: number;
  /** Claimed guests on the way plus on site now. -1 unknown. */
  skiDaysNow: number;
  /** Capacity at once (Cap incl. powder, whole skiers). 0 is real and reads Full. -1 unknown. */
  skiDaysCap: number;
  /** SKI_DAY_LIMIT: which term binds (0 None). */
  skiDaysLimit: number;
  /** Powder 0-100 (HubRules.Percent of the area's powder). -1 unknown. */
  skiDaysPowderPct: number;
  // Lodge-ladder fields (the model's Result, never recomputed here): appended in this order, as in C# ResortLiveStats.
  /** The best counted lodge of the season's selection, as its BuildItemId ("lodge", "lodgeGrand", ...). "" none. */
  lodgeBest: string;
  /** The other lodges' extras as applied (after the extras cap, ExtraCap 24). -1 unknown. */
  lodgeExtra: number;
  /** RETAINED FOR THE WIRE ONLY, always 0 (was: luxury lodges the one-per-area cap did not count).
   *  The cap was removed: no limit on lodges. Kept so the contract and tools/contract.lock do not change. No view reads it. */
  luxuryIgnored: number;
  /** The ski school's term in the terrain sum (0 while no green run counts or no school). -1 unknown. */
  schoolValue: number;
  /** Runs a ski patrol hut covers. -1 unknown. */
  patrolledRuns: number;
  /** The highest counted lodge's ticket price tolerance (Luxury 40, Chalet 20, else 0). -1 unknown. */
  priceTolerance: number;
  /** Lodge spend income a month, the tickets' month rule. -1 unknown, 0 while the setting is 0. */
  lodgeSpendMonth: number;
  // Season passes: appended in this order, as in C# ResortLiveStats.
  /** Season passes sold at this area in the current ski season (July to June), counted since the game was loaded. -1 unknown. */
  passesSoldSeason: number;
  /** What those passes paid. -1 unknown. */
  passIncomeSeason: number;
  /** Guests who arrived on a season pass this month (a subset of guestsThisMonth). -1 unknown. */
  passHoldersThisMonth: number;
  /** The same, last month. -1 until a month rolled since counting started. */
  passHoldersLastMonth: number;
  // Lessons and city income: appended in this order, as in C# ResortLiveStats.
  /** People who took a lesson at this area this month (one lesson each). -1 unknown. */
  lessonsThisMonth: number;
  /** The same, last month. -1 until a month rolled since counting started. */
  lessonsLastMonth: number;
  /** What this month's lessons paid so far (the ledger's raw month figure, no projection). -1 unknown. */
  lessonIncomeMonth: number;
  /** The part of passesSoldSeason sold in the pre-season sale (once per ski area per season). -1 unknown. */
  passesPreSeason: number;
  /** What those pre-season passes paid. -1 unknown. */
  passIncomePreSeason: number;
  /** The resort's share of the city's tourist draw (sharePct) x the game's TouristIncome statistic, in money. -1 unknown, no share, or
   *  Tourism Overhaul detected. C# SkiResortStatsSystem reads CityStatisticsSystem.GetStatisticValue(StatisticType.TouristIncome)
   *  (decompile 1.6.0f1 Game.Simulation/CityStatisticsSystem.cs:574). The value is an int in money, the last statistics entry's total.
   *  The game samples it kUpdatesPerDay = 32 times a game day (:363). BudgetSystem.cs:172-174, 280 reads it the same way. An ESTIMATE:
   *  whether that entry is a cumulative or a daily window is prefab data, not in the decompile. */
  cityIncomeEst: number;
  // The one Net's money rows: appended in this order, as in C# WriteResortLive.
  // The Net's rows of the month in progress not sent above (ticketsThisMonth and lessonIncomeMonth are the other two). -1 unknown.
  /** Season passes sold at this area this month, pre-season sales included (the Net's pass row). */
  passIncomeMonth: number;
  /** The pre-season-sale part of passIncomeMonth (a subset, for a tooltip). Never add it again. */
  passIncomePreMonth: number;
  /** Lodge spend booked this month (the Net's lodge row). lodgeSpendMonth keeps the last-month / projected rule. */
  lodgeIncomeMonth: number;
  /** Lift charge booked this month (the Net's lift row). liftCostMonth stays the rate. */
  liftChargedMonth: number;
  /** Run upkeep booked this month for this area (the Net's run row). costGroomed/Snowmaking/NightMonth stay the rates per kind. */
  runCostMonth: number;
  /** buildingsMonth x monthShare, rounded (the Net's buildings row). */
  buildingsSoFar: number;
  /** wagesMonth x monthShare, rounded (the Net's wages row). */
  wagesSoFar: number;
  /** The share of the month simulated, 0..1. -1 unknown (Not linked, counter not armed). */
  monthShare: number;
  /** The bus depots' coach bill booked this month (the Net's ninth row). busBillMonth is the rate now. */
  busChargedMonth: number;
  /** Active bus depots linked to this area. */
  depots: number;
  /** Ski days that arrived at a depot this month. -1 while ski days are off. */
  busGuestsMonth: number;
  /** The coach bill's rate per month now, over the depots that work. */
  busBillMonth: number;
  /** Contract 38: the Net's two sides (C# HubRules.IncomeMonth / CostsMonth over the same terms). incomeMonth - costsMonth = netMonth. */
  incomeMonth: number;
  costsMonth: number;
  /** Contract 40 (icon scheme): the score's variety flags as bits (VARIETY_BAND: green 1, blue 2, black or harder 4), a band set when a
   *  counted, Rated run of it exists. 7 = the bonus applies. The receipt's Variety line names the cleared bits. -1 without a score. */
  varietyBands: number;
  /** Contract 40: meters to the full vertical bonus (C# HubRules.VerticalShortM, 600 m minus the score's vertical, never below 0). -1 without a score. */
  verticalShortM: number;
}

/** One run of ours: identity, rating, service, value parts. */
export interface RunRow {
  entity: Entity;
  /** Custom name, or "Groomed run N" etc. (C#, numbered per area). Empty -> the type name. */
  name: string;
  kind: RunKind;
  bound: boolean;
  /** m, horizontal. */
  lengthM: number;
  /** m, true drop when rated. Max - min node height when approx. */
  dropM: number;
  /** m^2, plan area. */
  areaM2: number;
  /** The sign the run is bound to. Entity.Null = not linked. */
  sign: Entity;
  ordinal: number;
  customName: boolean;
  /** BAND. */
  band: number;
  /** Hardest unavoidable pitch, %. -1 not rated. */
  gradePct: number;
  /** Steepest patch, %. -1 not rated. */
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
  /** Meters between this run's snow guns and hydrants. 0 = off (no guns, no made snow, no bill). -1 = it carries none (a natural run:
   *  snow guns need Groomed). Contract 28 (the Snowmaking feature since R2). */
  gunSpacingM: number;
  /** Meters between this run's floodlight masts. 0 = off, -1 = it carries none (a natural run). Contract 28 (the Lights feature since R2). */
  lightSpacingM: number;
  /** The one-time steps already paid for this run (RUN_PAID bits, contract 33). Turning a paid feature on again is free. */
  paidSteps: number;
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
  /** Coverage-weighted mean snow depth, cm. -1 not measured. */
  snowDepthCm: number;
  /** RUN_GUN_WHY, the snow guns of a snowmaking or night run now. */
  gunWhy: number;
}

/** One lift (LiftCensus row). */
export interface LiftRow {
  /** Row id: the base station, else the top station, else the lowest-index line edge. */
  entity: Entity;
  /** The top station. Entity.Null when none. */
  top: Entity;
  /** The area: nearest sign within 5 km of the base station. Entity.Null = not linked. */
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
  /** Riders per hour at 1x game speed. -1 unknown. */
  capacityPerHour: number;
  hasBase: boolean;
  hasTop: boolean;
  /** Raw LiftPower != NoPower (the status mark). `counted` follows LiftServedRule.IsPowered (R1). */
  powered: boolean;
  counted: boolean;
  /** LIFT_STATUS. */
  status: number;
  rideTimeS: number;
  runsServed: number;
  costMonth: number;
  billedReduced: boolean;
  // Appended in this order, as in C# LiftStats.
  /** The row's lift type: SkiCatalog.Lifts[row.Type].Id (LIFT_TYPE_IDS). "" = unknown. */
  type: string;
  /** Seats per carrier of that type (the capacity tooltip's {SEATS}). 0 unknown. */
  seats: number;
  /** Cable speed of that type, m/s (the capacity and ride tooltips' {SPEED}). 0 unknown. */
  speedMps: number;
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

/** One active road lodge and its car spaces (contract 29, the Guests view). Rows come per sign in `resorts` order, a sign's lodges by
 *  entity. Only lodges with a road edge that take arrivals: the Hut, the Summit and an off-road lodge have no row. */
export interface LodgeParkingRow {
  sign: Entity;
  lodge: Entity;
  /** The lodge's BuildItemId (its LODGE_BUILDING name: own lodges only). */
  item: string;
  /** Street, lot and garage spaces within 600 m of this lodge (SkiLodgeParkingSystem). -1 = not counted yet. */
  carSpaces: number;
  /** Contract 30: its number among the area's lodges of its tier (LodgeNumberSystem, persisted). 0 = the only one, no number shown. */
  number: number;
  /** Contract 31: the name the player gave the lodge in the game. See NameSystem and LodgeNumberRule.DisplayName (one line, at most
   *  64 UTF-16 units, a cut name ends in "…"). "" = none, the tier word and number show. The UI shows it instead of them.
   *  It is a plain string argument. */
  name: string;
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
  /** Which known mod replaced the game's trip systems, read at each city load
   *  (C# SkiDaySystem.TripPlannerLabel = SkiTripSystems.Label). "" = the game's six trip systems all on.
   *  The other values are "Realistic PathFinding", "Realistic Trips", both joined ", ", or TRIP_PLANNER_UNKNOWN when a system
   *  is off and no known replacement is loaded. A label only: the UI decides nothing on it
   *  except the Guests view's note (hubRules.tripPlannerNote). */
  tripPlanner: string;
  /** The game's LeisureSystem or CitizenBehaviorSystem is switched off, so ski days do not arm
   *  this load (C# ArmVerdict.SystemOff, SkiDaySystem.TripPlannerBlocksSkiDays = SkiTripSystems.BlocksSkiDays). */
  tripPlannerBlocksSkiDays: boolean;
  /** The Options setting Season pass share, 0-100 (0 = passes off). */
  passShare: number;
  /** The calendar month of the game date, 0 = January .. 11 = December (C# SkiMoneyRules.MonthKey, the month the guest
   *  ledger counts). -1 = not known. The views name it through vanilla Common.MONTH:<n> (hubText.monthEntry). */
  month: number;
}

/**
 * HistoryArea.flags bits. 1..64 are the STORED bits of C# HistoryFlags (Systems/SkiResortHistory.cs). tools/test-hub-rules.js
 * compares them literally. Current = 128 is binding only (C# SkiResortHubUISystem.HistoryFlagCurrent): the month in progress, always the
 * last entry. Partial: counting started partway (a load not seeded, a calendar jump). Short: under 95 % of the month simulated.
 * CostsEstimated: reserved (never set in v1). SnapshotMissing: buildings / wages / attraction / snow unknown. Gap: no data for that month
 * (flows 0, unknowns -1). SkiDaysPartial: the ski-day count is since-load while the rest is whole. First: the area's first month (it may
 * have been placed partway through it).
 */
export const HISTORY_FLAG = {
  Partial: 1, Short: 2, CostsEstimated: 4, SnapshotMissing: 8, Gap: 16, SkiDaysPartial: 32, First: 64, Current: 128,
} as const;

/**
 * `history`: one row per live ski area, in `resorts` order. Not linked has none. Months run oldest -> newest. C# densifies it.
 * Index i is month firstMonthKey + i. Every array has the same length n >= 1. The last entry is the month in progress (flags & Current).
 * Money in whole credits, costs positive. Rates and point values (buildingsCost, wagesCost, attraction, snowPct, snowDepthCm) are snapshots at the
 * month's roll. For the month in progress, buildings / wages are the rate x the share simulated so far. -1 = unknown in skiDays,
 * buildingsCost, wagesCost, attraction, snowPct, snowDepthCm. The view sums areas for "whole city" and buckets long ranges into seasons
 * (src/hub/historyRules.ts). C#: SkiResortHubUISystem WriteHistory (EngineNotes/SkiResortHubUISystem.md, History binding).
 */
export interface HistoryArea {
  sign: Entity;
  /** Month key of entry 0: year x 12 + calendar month 0..11 (C# SkiMoneyRules.MonthKey). */
  firstMonthKey: number;
  /** The ski season's first calendar month 0..11 on this map: 5 months before the climate's winter (C# SkiMoneyRules.SeasonFirstMonthOf). */
  seasonFirstMonth0: number;
  /** HISTORY_FLAG bits per month. */
  flags: number[];
  tourists: number[];
  residents: number[];
  /** Ski-day guests who reached a lodge. -1 unknown. */
  skiDays: number[];
  ticketsPaid: number[];
  ticketIncome: number[];
  lodgeIncome: number[];
  passIncome: number[];
  lessonIncome: number[];
  liftCost: number[];
  runCost: number[];
  /** -1 unknown. */
  buildingsCost: number[];
  /** -1 unknown. */
  wagesCost: number[];
  /** -1 unknown. */
  attraction: number[];
  /** Run snow coverage 0-100 at the roll. -1 unknown. */
  snowPct: number[];
  /** Mean snow depth on the runs at the roll, whole cm. -1 unknown (gap months, months stored before contract 15). */
  snowDepthCm: number[];
}

/** CityStats.tripPlanner when a trip system is off and no known replacement is loaded. Manual parity with
 *  C# SkiTripSystems.UnknownLabel (no gate rule compares string constants. Both sides test the literal "?":
 *  tools/test-hub-rules.js T11.8 and ResortBatch SkiDayTests section X). */
export const TRIP_PLANNER_UNKNOWN = "?";

export function clampGladeLevel(level: number): number {
  const n = Math.round(Number.isFinite(level) ? level : GLADE_LEVEL_DEFAULT);
  return Math.min(GLADE_LEVEL_MAX, Math.max(GLADE_LEVEL_MIN, n));
}

// ---- names --------------------------------------------------------------------------------------

/** Value binding names (C# -> UI). */
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
  history: "history",
  liftLine: "liftLine",  // the lift-line generator
  quickRuns: "quickRuns",  // the Quick runs tool (experimental)
  catTrackDeleteMode: "catTrackDeleteMode",  // contract 19: the cat track tool is open and in Delete mode
  busCard: "busCard",  // contract 26: the Ski Bus card
  lodgeParking: "lodgeParking",  // contract 29: car spaces per road lodge
  runReplaced: "runReplaced",  // contract 32: a re-emitted run's old and new entity
  drawRun: "drawRun",  // contract 33: the Run tile's option row
  mergeCard: "mergeCard",  // contract 35: the merge card
  mergedLodges: "mergedLodges",  // contract 35: the merged buildings
  mergeOverlay: "mergeOverlay",  // contract 36: the merge overlay's armed state
} as const;

/** Trigger names (UI -> C#). */
export const TRIGGER = {
  toggleHub: "toggleHub",
  setHubOpen: "setHubOpen",
  uiHello: "uiHello",
  uiError: "uiError",
  launch: "launch",
  focusRun: "focusRun",
  deleteCatTrack: "deleteCatTrack",  // contract 18: the cat track row's trash button
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
  liftLineToggle: "liftLineToggle",  // the lift-line generator
  quickRunsToggle: "quickRunsToggle",  // the Quick runs tool
  quickRunsSet: "quickRunsSet",
  setCatTrackDeleteMode: "setCatTrackDeleteMode",  // contract 19: the Runs tab's Delete cat tracks chip
  setRunGunSpacing: "setRunGunSpacing",  // contract 28: the Runs view's prop rows
  setRunLightSpacing: "setRunLightSpacing",
  setRunGroomed: "setRunGroomed",  // contract 32: the Groomed feature (a re-emit)
  setDrawRun: "setDrawRun",  // contract 33: the Run tile's option row (key, value)
  mergeBuilding: "mergeBuilding",  // contract 35: the merge card's tier buttons
  unmergeBuilding: "unmergeBuilding",
  armMergeOverlay: "armMergeOverlay",  // contract 36: the Merged buildings fold's overlay chip
} as const;

/**
 * CORE = frozen since contract 1. These names may only gain
 * OPTIONAL fields. Never rename, retype or remove them. This is the ONE machine-readable list:
 * tools/check-bundle.js reads it (string literals only) and checks it against tools/contract.lock.
 * On a contract mismatch the panel reads only these names (spec 4.5).
 */
export const CORE_NAMES: readonly string[] = [
  "hubOpen", "contract", "buildStamp", "showHubButton", "build", "glade",
  "toggleHub", "setHubOpen", "uiHello", "uiError", "launch", "focusRun",
  "gladeToggle", "gladeSetLevel", "gladeSetTreeLine", "dumpHub",
];

/** Vanilla binding / trigger names used ("group.name"): none (the vanilla LOCALE keys we read are
 *  listed in src/hub/hubText.ts EFFICIENCY_FACTOR_FALLBACK, not here). */
export const VANILLA_NAMES_USED: readonly string[] = [];

// ---- fallbacks (what the UI shows while a binding is not registered in C#) -----------------------

const EMPTY_BUILD: readonly BuildItem[] = [];
const EMPTY_RESORTS: readonly ResortRow[] = [];
const EMPTY_RESORT_LIVE: readonly ResortLive[] = [];
const EMPTY_RUNS: readonly RunRow[] = [];
const EMPTY_RUN_LIVE: readonly RunLive[] = [];
const EMPTY_LIFTS: readonly LiftRow[] = [];
const EMPTY_PROBLEMS: readonly ProblemRow[] = [];
const EMPTY_HISTORY: readonly HistoryArea[] = [];
const EMPTY_LODGE_PARKING: readonly LodgeParkingRow[] = [];
const EMPTY_MERGED: readonly MergedLodgeRow[] = [];
const EMPTY_QUICK_RUNS: QuickRunsState = {
  available: false, active: false, reason: "", phase: "", corners: 0, canPlanAround: false, areaM2: 0, maxAreaM2: 1000000, verticalM: 0, regionState: "Open",
  progress: 0, mix: 0, density: 1, kinds: 3, runs: 0, enabled: 0, count: [0, 0, 0, 0, 0], target: [0, 0, 0, 0, 0], fee: 0, skiers: 0,
  variety: false, status: "", warnings: [], clusters: [], batchRuns: 0, notice: "", trees: false, treeLevel: GLADE_LEVEL_DEFAULT,
  treeLine: -1, groundMin: 0, groundMax: 0, group: 0, species: 0, speciesNames: [], speciesGroups: [], plant: 0, remove: 0, treesUndo: 0,
  buildDone: 0, buildTotal: 0, planting: false, treesDone: 0, treesTotal: 0, treesCost: 0, rejects: [0, 0, 0, 0, 0, 0, 0, 0],
  sitePercent: -1, siteTier: 0, liftsKnob: 0, home: 0, pods: [], planLifts: [],
  plan: { pods: 0, liftsProposed: 0, liftsPinned: 0, runKm: 0, runKmPerKm2: 0, deadEnds: 0, mountainMix: [0, 0, 0, 0, 0], sitePods: 0, siteGreenHome: 0, siteBenchHa: 0 },
};

export const FALLBACK = {
  hubOpen: false,
  contract: CONTRACT_ABSENT,
  buildStamp: "",
  /** The setting's default is true. Without C#, the button opens the "not connected" notice. */
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
    tripPlanner: "", tripPlannerBlocksSkiDays: false, passShare: 50, month: -1,
  } as CityStats,
  /** The piste map is on by default (C# owns the value). */
  pisteMap: true,
  follow: 0,
  history: EMPTY_HISTORY,
  liftLine: { available: false, active: false, state: "" } as LiftLineState,
  quickRuns: EMPTY_QUICK_RUNS,
  catTrackDeleteMode: false,
  busCard: { shown: false, area: "", guestsMonth: -1, billMonth: 0, distanceM: 0, state: "off" } as BusCardState,
  lodgeParking: EMPTY_LODGE_PARKING,
  runReplaced: { seq: 0, from: NULL_ENTITY, to: NULL_ENTITY } as RunReplaced,
  drawRun: { active: false, groomed: true, gunSpacingM: 0, lightSpacingM: 0 } as DrawRun,
  mergeCard: { shown: false, building: { index: 0, version: 0 }, name: "", area: "", tier: -1, allowedMask: 0, refusal: 0, state: 6, outcome: 0, charge: [], monthly: [], near: false, geometry: true, reachM: 0 } as MergeCardState,
  mergedLodges: EMPTY_MERGED,
  mergeOverlay: { armed: false, allowed: 0, tooSmall: 0, geometry: true, reachM: 0 } as MergeOverlayState,
} as const;

// ---- value bindings -----------------------------------------------------------------------------

/** Is the hub panel open. C# owns it (launchers toggle, X closes, selecting a sign opens). */
export const hubOpen$: ValueBinding<boolean> = bindValue<boolean>(GROUP, BINDING.hubOpen, FALLBACK.hubOpen);
/** C#'s contract number (HUB_CONTRACT on a match. CONTRACT_ABSENT when C# lacks the binding, or
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
/** The lift-line generator. Panel only, the Build strip's Lifts tab (never on a mismatch). */
export const liftLine$: ValueBinding<LiftLineState> = bindValue<LiftLineState>(GROUP, BINDING.liftLine, FALLBACK.liftLine);
/** The Quick runs tool. Panel only, the Build strip's Runs tab and RunsView (never on a mismatch). */
export const quickRuns$: ValueBinding<QuickRunsState> = bindValue<QuickRunsState>(GROUP, BINDING.quickRuns, FALLBACK.quickRuns);
/** The cat track tool is open AND in Delete mode (C# owns it: closing the tool or picking another shows false). Panel only, the
 *  Build strip's Runs tab (never on a mismatch). */
export const catTrackDeleteMode$: ValueBinding<boolean> = bindValue<boolean>(GROUP, BINDING.catTrackDeleteMode, FALLBACK.catTrackDeleteMode);
/** The Ski Bus card. Always mounted: C# sends it only while a coach or a depot is selected. */
export const busCard$: ValueBinding<BusCardState> = bindValue<BusCardState>(GROUP, BINDING.busCard, FALLBACK.busCard);
/** The merge card. Always mounted: C# sends it only while a building it applies to is selected. */
export const mergeCard$: ValueBinding<MergeCardState> = bindValue<MergeCardState>(GROUP, BINDING.mergeCard, FALLBACK.mergeCard);
/** Every merged building, any state. Panel only, and only the Overview subscribes it (no Demand). */
export const mergedLodges$: ValueBinding<readonly MergedLodgeRow[]> = bindValue<readonly MergedLodgeRow[]>(GROUP, BINDING.mergedLodges, FALLBACK.mergedLodges);
/** The merge overlay's armed state. Panel only, the Overview's Merged buildings fold (no Demand). */
export const mergeOverlay$: ValueBinding<MergeOverlayState> = bindValue<MergeOverlayState>(GROUP, BINDING.mergeOverlay, FALLBACK.mergeOverlay);
/** One row per sign, then the Not linked row (always last). Panel only. */
export const resorts$: ValueBinding<readonly ResortRow[]> = bindValue<readonly ResortRow[]>(GROUP, BINDING.resorts, FALLBACK.resorts);
/** The moving values of each `resorts` row. Panel only: read by leaf components only. */
export const resortLive$: ValueBinding<readonly ResortLive[]> = bindValue<readonly ResortLive[]>(GROUP, BINDING.resortLive, FALLBACK.resortLive);
/** The area the hub shows: a sign, or Entity.Null = the Not linked view. Panel only. */
export const selectedResort$: ValueBinding<Entity> = bindValue<Entity>(GROUP, BINDING.selectedResort, FALLBACK.selectedResort);
/** One row per run of ours. Panel only (never read on a contract mismatch). */
export const runs$: ValueBinding<readonly RunRow[]> = bindValue<readonly RunRow[]>(GROUP, BINDING.runs, FALLBACK.runs);
/** The moving values of each `runs` row. Panel only. */
export const runLive$: ValueBinding<readonly RunLive[]> = bindValue<readonly RunLive[]>(GROUP, BINDING.runLive, FALLBACK.runLive);
/** One row per lift. Panel only. */
export const lifts$: ValueBinding<readonly LiftRow[]> = bindValue<readonly LiftRow[]>(GROUP, BINDING.lifts, FALLBACK.lifts);
/** The problems band and the efficiency factors of every area. Panel only. */
export const problems$: ValueBinding<readonly ProblemRow[]> = bindValue<readonly ProblemRow[]>(GROUP, BINDING.problems, FALLBACK.problems);
/** The car spaces of every active road lodge. Panel only, and only the Guests view subscribes it (no Demand). */
export const lodgeParking$: ValueBinding<readonly LodgeParkingRow[]> = bindValue<readonly LodgeParkingRow[]>(GROUP, BINDING.lodgeParking, FALLBACK.lodgeParking);
/** Contract 32: the last re-emitted run (HubPanel moves the focus to the new entity). */
export const runReplaced$: ValueBinding<RunReplaced> = bindValue<RunReplaced>(GROUP, BINDING.runReplaced, FALLBACK.runReplaced);
/** Contract 33: the Run tile's option row (the Runs view shows it while a run is drawn). */
export const drawRun$: ValueBinding<DrawRun> = bindValue<DrawRun>(GROUP, BINDING.drawRun, FALLBACK.drawRun);
/** City-wide settings and session facts. Panel only. */
export const city$: ValueBinding<CityStats> = bindValue<CityStats>(GROUP, BINDING.city, FALLBACK.city);
/** The piste map toggle as C# holds it. Panel only. */
export const pisteMap$: ValueBinding<boolean> = bindValue<boolean>(GROUP, BINDING.pisteMap, FALLBACK.pisteMap);
/** A counter C# moves on each automatic area switch (selection rule 7). Panel only. */
export const follow$: ValueBinding<number> = bindValue<number>(GROUP, BINDING.follow, FALLBACK.follow);
/** The monthly history of every live ski area. Panel only, and only the History view subscribes it (no Demand).
 *  C# builds and sends it only while observed. The triggers are a stored month, an area change, or the month in progress (at most every 5 s). */
export const history$: ValueBinding<readonly HistoryArea[]> = bindValue<readonly HistoryArea[]>(GROUP, BINDING.history, FALLBACK.history);

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
  /** Set hubOpen (the panel's X sends false). */
  setHubOpen: (open: boolean): void => fire(TRIGGER.setHubOpen, open),
  /** Handshake, fired by the always-mounted HubRoot. C# logs "UI hub connected" or the mismatch. */
  uiHello: (contract: number, bundleStamp: string): void => fire(TRIGGER.uiHello, contract, bundleStamp),
  /** From the error boundary (src/hub/report.ts dedupes per message). C# logs once per message. */
  uiError: (message: string): void => fire(TRIGGER.uiError, message),
  /** Start the tool of a Build item (or return to the default tool when it is already active).
   *  Sent for locked items too: C# refuses and logs "refused - locked" (spec 4.2). */
  launch: (id: BuildItemId): void => fire(TRIGGER.launch, id),
  /** Fly the camera to a run (zoom to fit). */
  focusRun: (run: Entity): void => fire(TRIGGER.focusRun, wire(run)),
  /** Delete a cat track (its Runs row's trash button, after the second click). C# queues it. CatTrackGradeToolSystem.DeleteTrack
   *  runs in the hub's update (never in the callback). The ground stays graded, no refund. */
  deleteCatTrack: (run: Entity): void => fire(TRIGGER.deleteCatTrack, wire(run)),
  /** Tree tool on/off. */
  gladeToggle: (): void => fire(TRIGGER.gladeToggle),
  /** Lift-line generator on / off: C# LiftLineToolSystem.TryActivate("hub") / Deactivate(). */
  liftLineToggle: (): void => fire(TRIGGER.liftLineToggle),
  /** Quick runs tool on / off: C# QuickRunsToolSystem.TryActivate("hub") / Deactivate(). C# refuses (and logs) while the tool is unavailable. */
  quickRunsToggle: (): void => fire(TRIGGER.quickRunsToggle),
  /** A Quick runs knob, button or tree setting (QuickRunsToolSystem.HubSet). C# clamps every value. */
  quickRunsSet: (key: QuickRunsKey, value: number): void => fire(TRIGGER.quickRunsSet, key, Math.round(value)),
  /** The cat track tool's Delete mode. On: opens the tool if needed (C# TryActivate, then SetDeleteMode). Off: leaves the tool open. */
  setCatTrackDeleteMode: (on: boolean): void => fire(TRIGGER.setCatTrackDeleteMode, on),
  /** A run's prop spacing in meters. C# clamps to Off or 20..200 and re-places after its settle. Look only. */
  setRunGunSpacing: (run: Entity, meters: number): void => fire(TRIGGER.setRunGunSpacing, wire(run), Math.round(meters)),
  setRunLightSpacing: (run: Entity, meters: number): void => fire(TRIGGER.setRunLightSpacing, wire(run), Math.round(meters)),
  /** Contract 32: Groomed on or off. C# re-emits the run under a new entity (runReplaced). */
  setRunGroomed: (run: Entity, groomed: boolean): void => fire(TRIGGER.setRunGroomed, wire(run), groomed),
  /** Contract 33: the Run tile's option row: "groomed" 0 / 1, "guns" and "lights" meters (0 = off). */
  setDrawRun: (key: "groomed" | "guns" | "lights", value: number): void => fire(TRIGGER.setDrawRun, key, value),
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
   *  Trimmed and cut to 64 here and again in C#. "" restores the default name. */
  rename: (entity: Entity, name: string): void => fire(TRIGGER.rename, wire(entity), name),
  /** Fly to a lift: (row `entity`, row `top`). C# flies to the gap when a section is missing. */
  focusLift: (lift: Entity, top: Entity): void => fire(TRIGGER.focusLift, wire(lift), wire(top)),
  /** Fly to a building (sign, lodge or station) without selecting it. */
  focusBuilding: (building: Entity): void => fire(TRIGGER.focusBuilding, wire(building)),
  /** Dev aid: measured panel heights. C# logs `Hub [layout] ...` once per distinct text. */
  uiLayout: (text: string): void => fire(TRIGGER.uiLayout, text),
  /** Set the piste-map toggle. C# owns the value (pisteMap$) and logs `Hub [piste] map on|off`. */
  setPisteMap: (on: boolean): void => fire(TRIGGER.setPisteMap, on),
  /** The hovered run (Null clears). Send through hubState.ts sendHoverRun only (dedup). */
  hoverRun: (run: Entity): void => fire(TRIGGER.hoverRun, wire(run)),
  /** Merge a building as tier `tier`, or change a merged building's tier (C# SkiMergeSystem.Merge charges at once). Refusals are logged. */
  mergeBuilding: (building: Entity, tier: number): void => fire(TRIGGER.mergeBuilding, wire(building), Math.round(tier)),
  /** Unmerge: the building goes back to plain. No refund. */
  unmergeBuilding: (building: Entity): void => fire(TRIGGER.unmergeBuilding, wire(building)),
  /** Arm (true) or disarm (false) the merge overlay. C# answers through mergeOverlay$ (Escape, a merge and a load disarm it too). */
  armMergeOverlay: (on: boolean): void => fire(TRIGGER.armMergeOverlay, on),
} as const;
