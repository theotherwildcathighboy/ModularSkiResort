/*
 * Display helpers for the `build` binding and the run kinds.
 * BUILD_ITEMS mirrors the C# table (mod/SkiResortMod/Systems/SkiCatalog.cs, BUILD-ITEMS block) row by row. Gate rule R16 fails
 * any difference, a missing icon, or a group that does not hold every id once. So keep BUILD_ITEMS, BUILD_GROUPS and
 * BUILD_ITEM_ICON literal. C# owns what each id launches and which ids it sends. This file only maps an id to icon, group,
 * strip position and badge. This bundle ignores an id it does not know. It does not draw a known id that C# did not send.
 * Icons (T210, 1.0.2): the hub keeps the style-E card, its own set gamedata-mirror/ImportedData/SkiResortIcons/<Name>Card.png
 * (assetdb://global/<the .png.cid>). The prefabs' UIObject m_Icon is <Name>.png, the bare vanilla-menu look (gate T17.13).
 */
import { BUILD_ITEM_IDS, RUN_KIND } from "../bindings";
import type { BuildItem, BuildItemId, RunKind } from "../bindings";

/** C# BuildItemKind (SkiCatalog.cs) in lower camel case: Sign, Lodge, School, Patrol, Station, Line, Run, Fence. */
export type BuildItemKind = "sign" | "lodge" | "school" | "patrol" | "station" | "line" | "run" | "fence" | "depot";

/** One row of the ONE build-item table, as C# holds it (gate R16 compares row i with C# row i). */
export interface BuildItemMeta {
  id: BuildItemId;
  kind: BuildItemKind;
  /** LiftTypeDef.Id for station and line items, else "". */
  liftType: string;
  /** RunKind for run items, else -1. */
  runKind: number;
}

/** The C# BUILD-ITEMS table, row by row (strip order). */
export const BUILD_ITEMS: readonly BuildItemMeta[] = [
  { id: "sign", kind: "sign", liftType: "", runKind: -1 },
  { id: "lodge", kind: "lodge", liftType: "", runKind: -1 },
  { id: "lodgeHut", kind: "lodge", liftType: "", runKind: -1 },
  { id: "lodgeMid", kind: "lodge", liftType: "", runKind: -1 },
  { id: "lodgeSummit", kind: "lodge", liftType: "", runKind: -1 },
  { id: "lodgeGrand", kind: "lodge", liftType: "", runKind: -1 },
  { id: "busDepot", kind: "depot", liftType: "", runKind: -1 },
  { id: "school", kind: "school", liftType: "", runKind: -1 },
  { id: "patrolHut", kind: "patrol", liftType: "", runKind: -1 },
  { id: "patrolHQ", kind: "patrol", liftType: "", runKind: -1 },
  { id: "baseStation", kind: "station", liftType: "fixedGrip", runKind: -1 },
  { id: "topStation", kind: "station", liftType: "fixedGrip", runKind: -1 },
  { id: "lift", kind: "line", liftType: "fixedGrip", runKind: -1 },
  { id: "baseStationDetachable", kind: "station", liftType: "detachable", runKind: -1 },
  { id: "topStationDetachable", kind: "station", liftType: "detachable", runKind: -1 },
  { id: "liftDetachable", kind: "line", liftType: "detachable", runKind: -1 },
  { id: "baseStationGondola", kind: "station", liftType: "gondola", runKind: -1 },
  { id: "topStationGondola", kind: "station", liftType: "gondola", runKind: -1 },
  { id: "liftGondola", kind: "line", liftType: "gondola", runKind: -1 },
  { id: "baseStationTram", kind: "station", liftType: "tram", runKind: -1 },
  { id: "topStationTram", kind: "station", liftType: "tram", runKind: -1 },
  { id: "liftTram", kind: "line", liftType: "tram", runKind: -1 },
  { id: "runNatural", kind: "run", liftType: "", runKind: 3 },
  { id: "runGroomed", kind: "run", liftType: "", runKind: 0 },
  { id: "runSnowmaking", kind: "run", liftType: "", runKind: 1 },
  { id: "runNight", kind: "run", liftType: "", runKind: 2 },
  { id: "runCatTrack", kind: "run", liftType: "", runKind: 4 },
  { id: "safetyFence", kind: "fence", liftType: "", runKind: -1 },
];

const ICON_SIGN = "assetdb://global/18e4c114588746cdda8369362705a36a"; // SkiResortGateCard.png
const ICON_LODGE = "assetdb://global/aa8a888e96f1187be9f6a6f6a4b836d2"; // SkiAFrameDayLodgeCard.png
const ICON_LINE = "assetdb://global/57c96252de1426ed9c156a6540f0402c"; // SkiChairliftLineCard.png
const ICON_BASE = "assetdb://global/71e7c5f91d3f6206a688f1a3b822c8e0"; // SkiBaseStationBuildingCard.png
const ICON_TOP = "assetdb://global/0590f468d0542b9d808e8bbb4f9c28c9"; // SkiTopStationBuildingCard.png

export const BUILD_ITEM_ICON: Readonly<Record<BuildItemId, string>> = {
  sign: ICON_SIGN,
  lodge: ICON_LODGE,
  // SkiResortIcons/<file>Card.png.cid (gamedata-mirror/ImportedData/SkiResortIcons/). The prefab m_Icon is <file>.png (bare)
  lodgeHut: "assetdb://global/691484d394075ff16442b7922b43792e", // SkiLodgeHutCard.png
  lodgeMid: "assetdb://global/9612a3336f94f95e3a319c1edfc0c060", // SkiLodgeMidCard.png
  lodgeSummit: "assetdb://global/5788df57f9c120d48cd9b722f841ad41", // SkiLodgeSummitCard.png
  lodgeGrand: "assetdb://global/6bb60ccd3e6b85985840ca7a1e89d522", // SkiLodgeGrandCard.png
  busDepot: "assetdb://global/5744dbb5fcc1a9b5a062d5d33d7b988a", // SkiBusDepotCard.png
  school: "assetdb://global/67484b450f60a3b395c203af90404157", // SkiSchoolCard.png
  patrolHut: "assetdb://global/66e373cc6884cd6b423109da234c205a", // SkiPatrolHutCard.png
  patrolHQ: "assetdb://global/0b9ff4fc9e536390ca687590edcaa1f4", // SkiPatrolHQCard.png
  baseStation: ICON_BASE,
  topStation: ICON_TOP,
  lift: ICON_LINE,
  baseStationDetachable: "assetdb://global/4d2f785beba173c1a40d64170e76795c", // SkiDQBaseStationBuildingCard.png
  topStationDetachable: "assetdb://global/b0db82a0cb4af09c5765ece43b90476a", // SkiDQTopStationBuildingCard.png
  liftDetachable: "assetdb://global/8c151f93e6073afda7201f3828902f22", // SkiDetachableLineCard.png
  baseStationGondola: "assetdb://global/198dcaf0c7ce1d7b5e29d5502b687104", // SkiGondolaBaseStationBuildingCard.png
  topStationGondola: "assetdb://global/b87f8c72a75e6db3a81f065113fc8810", // SkiGondolaTopStationBuildingCard.png
  liftGondola: "assetdb://global/07a86bcf6968feffe19b93a5bb5fa472", // SkiGondolaLineCard.png
  baseStationTram: "assetdb://global/7b8170111918fd0120ad29d95e062ed0", // SkiTramBaseStationBuildingCard.png
  topStationTram: "assetdb://global/b87f9a333891d2a5e720c7c6a33e2278", // SkiTramTopStationBuildingCard.png
  liftTram: "assetdb://global/3e078fee5f882252a00a10562f1521c8", // SkiTramLineCard.png
  runNatural: "assetdb://global/a47d45b182fdb2d96054e43a07a12567", // SkiRunNaturalACard.png
  runGroomed: "assetdb://global/39e0ea0cc6f2afc51df7d862cb4dc483", // SkiRunGroomedACard.png
  runSnowmaking: "assetdb://global/6ba85d68b4edba124b061e1aba603418", // SkiRunSnowmakingACard.png
  runNight: "assetdb://global/acbe49545a195c954b5d2d3cbe8e68b0", // SkiRunNightACard.png
  runCatTrack: "assetdb://global/731f946165b587fbc604cabe42137d25", // SkiRunCatTrackACard.png
  safetyFence: "assetdb://global/0e897d5b08f9709ec85c012e572319b5", // SkiSafetyFenceCard.png
};

/** Four groups in build order (gate R16 checks every id appears exactly once). Group 1's lodge tiers and group 3's
 *  lift types are alternatives (hubRules.pickerSets): the hub draws only the selected one. */
export const BUILD_GROUPS: ReadonlyArray<readonly BuildItemId[]> = [
  ["sign", "lodge", "lodgeHut", "lodgeMid", "lodgeSummit", "lodgeGrand", "busDepot"],
  ["school", "patrolHut", "patrolHQ"],
  [
    "baseStation", "topStation", "lift",
    "baseStationDetachable", "topStationDetachable", "liftDetachable",
    "baseStationGondola", "topStationGondola", "liftGondola",
    "baseStationTram", "topStationTram", "liftTram",
  ],
  ["runNatural", "runGroomed", "runSnowmaking", "runNight", "runCatTrack"],
  ["safetyFence"],   // the Runs tab's decor: the safety net fence (a net prefab drawn like a path)
];

/** The step number drawn on the lift tiles (stations first, then the line), for every lift type. */
export const BUILD_STEP: Readonly<Partial<Record<BuildItemId, string>>> = {
  baseStation: "1",
  topStation: "2",
  lift: "3",
  baseStationDetachable: "1",
  topStationDetachable: "2",
  liftDetachable: "3",
  baseStationGondola: "1",
  topStationGondola: "2",
  liftGondola: "3",
  baseStationTram: "1",
  topStationTram: "2",
  liftTram: "3",
};

/** The luxury lodge tiers: C# SkiCatalog.Lodges rows with Luxury true (summit, grand). Display only (the tile tooltip's
 *  BUILD_TIP_LUXURY line, the price tolerance). No rule hangs on it, and there is no limit on lodges of any tier.
 *  Keep in step with SkiCatalog.cs by hand. */
export const LUXURY_LODGES: ReadonlySet<BuildItemId> = new Set<BuildItemId>(["lodgeSummit", "lodgeGrand"]);

/** The luxury tiers' PriceTolerance (C# SkiCatalog.Lodges summit and grand: 40). The tile tooltip says full ski days up to priceFullWith(this) = 80
 *  (T239). Keep in step with SkiCatalog.cs by hand (CatalogGuardTests T6 holds it). */
export const LUXURY_PRICE_TOLERANCE = 40;

/** Skier room per lodge tier: C# SkiCatalog.Lodges Capacity (the lodge side of the ski-day room is their sum over the passing lodges,
 *  SkiDaySystem.Jobs.cs, SkiDayRules.Capacity), smallest first. The Guests view's "Lodges limit it" tooltip lists them (SKI_DAYS_TIP[Lodges],
 *  DECISIONS 677). test-hub-rules T43 holds it against SkiCatalog.cs. */
export const LODGE_SKIER_ROOM: ReadonlyArray<readonly [BuildItemId, number]> = [
  ["lodgeHut", 200], ["lodge", 360], ["lodgeSummit", 480], ["lodgeMid", 720], ["lodgeGrand", 1080],
];

/** The road-optional lodge tiers: C# SkiCatalog.Lodges rows with LodgeRoadRule.RoadOptional (A-frame, Chalet, Grand). Display only (the
 *  tile tooltip's BUILD_TIP_ROAD_OPTIONAL line). Keep in step with SkiCatalog.cs by hand. */
export const ROAD_OPTIONAL_LODGES: ReadonlySet<BuildItemId> = new Set<BuildItemId>(["lodge", "lodgeMid", "lodgeGrand"]);

/** What a placed building draws a month, and how it meets the ground. */
export interface BuildingDraw {
  /** The prefab (gamedata-mirror/ImportedData/<prefab>/<prefab>.Prefab), for the gate. */
  prefab: string;
  /** ServiceConsumption.m_Upkeep, money per month at a 100 % budget. 0 = not billed. A station has no CityServiceBuilding, so the
   *  game never bills it (CityServiceBuilding.cs:23 adds ServiceUpkeepData only there, BuildingInitializeSystem.cs:1166-1191).
   *  The lift's running cost covers it. */
  upkeep: number;
  /** ServiceConsumption.m_ElectricityConsumption / m_WaterConsumption / m_GarbageAccumulation, the asset panel's units (Power,
   *  VolumePerMonth, WeightPerMonth). PrefabUISystem.cs:552-559 writes them CeilToInt. */
  power: number;
  water: number;
  garbage: number;
  /** Jobs: Workplace.m_Workplaces, or for a roadless building SkiCatalog's SignJobs, which the area's carrier holds (a road lodge, else the sign). */
  workers: number;
  /** How the lot meets the ground, from its BuildingTerraformOverride flags. The flags empty the lot's windows
   *  (BuildingUtils.cs:610-617): m_DontRaise empties the fill window, m_DontLower the cut window. */
  terrain: TerrainFit;
}

/** "level" = no override block or neither flag: the lot cuts and fills to its plane. "cut" = m_DontRaise only: the lot cuts the
 *  ground above its plane to a level pad and fills nothing (the chair / gondola stations except the fixed-grip top, 2026-11-09). "slope" = both flags:
 *  the terrain stays as it is (block A). A prefab with m_DontLower only has no state here and fails gate T29.2. */
export type TerrainFit = "level" | "cut" | "slope";

/** One row per building tile, from the prefabs (gate: tools/test-hub-rules.js T22 reads every prefab and SkiCatalog's SignJobs
 *  and fails on a difference). A value 0 draws no row (as vanilla's asset panel). Lines and runs are not buildings and have no row. */
export const BUILDING_DRAW: Readonly<Partial<Record<BuildItemId, BuildingDraw>>> = {
  sign: { prefab: "SkiResortGate", upkeep: 0, power: 0, water: 0, garbage: 0, workers: 0, terrain: "level" },
  lodge: { prefab: "SkiAFrameDayLodge", upkeep: 3000, power: 1500, water: 1000, garbage: 80, workers: 4, terrain: "level" },
  lodgeHut: { prefab: "SkiLodgeHut", upkeep: 3000, power: 0, water: 0, garbage: 0, workers: 4, terrain: "slope" },
  lodgeMid: { prefab: "SkiLodgeMid", upkeep: 8000, power: 3500, water: 2300, garbage: 190, workers: 12, terrain: "slope" },
  lodgeSummit: { prefab: "SkiLodgeSummit", upkeep: 18000, power: 0, water: 0, garbage: 0, workers: 8, terrain: "slope" },
  lodgeGrand: { prefab: "SkiLodgeGrand", upkeep: 20000, power: 3500, water: 5000, garbage: 430, workers: 18, terrain: "level" },
  busDepot: { prefab: "SkiBusDepot", upkeep: 2000, power: 1000, water: 600, garbage: 50, workers: 0, terrain: "level" },
  school: { prefab: "SkiSchool", upkeep: 3000, power: 2500, water: 1700, garbage: 130, workers: 4, terrain: "slope" },
  patrolHut: { prefab: "SkiPatrolHut", upkeep: 1500, power: 0, water: 0, garbage: 0, workers: 2, terrain: "slope" },
  patrolHQ: { prefab: "SkiPatrolHQ", upkeep: 1500, power: 0, water: 0, garbage: 0, workers: 2, terrain: "slope" },
  baseStation: { prefab: "SkiBaseStationBuilding", upkeep: 0, power: 1500, water: 0, garbage: 0, workers: 0, terrain: "cut" },
  topStation: { prefab: "SkiTopStationBuilding", upkeep: 0, power: 0, water: 0, garbage: 0, workers: 0, terrain: "slope" },
  baseStationDetachable: { prefab: "SkiDQBaseStationBuilding", upkeep: 0, power: 1500, water: 0, garbage: 0, workers: 0, terrain: "cut" },
  topStationDetachable: { prefab: "SkiDQTopStationBuilding", upkeep: 0, power: 0, water: 0, garbage: 0, workers: 0, terrain: "cut" },
  baseStationGondola: { prefab: "SkiGondolaBaseStationBuilding", upkeep: 0, power: 1500, water: 0, garbage: 0, workers: 0, terrain: "cut" },
  topStationGondola: { prefab: "SkiGondolaTopStationBuilding", upkeep: 0, power: 0, water: 0, garbage: 0, workers: 0, terrain: "cut" },
  baseStationTram: { prefab: "SkiTramBaseStationBuilding", upkeep: 0, power: 1500, water: 0, garbage: 0, workers: 0, terrain: "level" },
  topStationTram: { prefab: "SkiTramTopStationBuilding", upkeep: 0, power: 0, water: 0, garbage: 0, workers: 0, terrain: "slope" },
};

/** The run kind of a run tile (for its stripe and its RUN_TYPE name), else undefined. */
export const BUILD_RUN_KIND: Readonly<Partial<Record<BuildItemId, RunKind>>> = {
  runNatural: RUN_KIND.Natural,
  runGroomed: RUN_KIND.Groomed,
  runSnowmaking: RUN_KIND.Snowmaking,
  runNight: RUN_KIND.Night,
  runCatTrack: RUN_KIND.CatTrack,
};

export function isBuildItemId(id: string): id is BuildItemId {
  return (BUILD_ITEM_IDS as readonly string[]).indexOf(id) >= 0;
}

export function isRunKind(kind: number): kind is RunKind {
  return kind === RUN_KIND.Groomed || kind === RUN_KIND.Snowmaking || kind === RUN_KIND.Night || kind === RUN_KIND.Natural ||
    kind === RUN_KIND.CatTrack;
}

/** The table row of an id (undefined for an id this bundle does not know). */
export function buildMeta(id: string): BuildItemMeta | undefined {
  for (const m of BUILD_ITEMS) if (m.id === id) return m;
  return undefined;
}

/** "base" / "top" for a station item (the ids follow baseStation{Suffix} / topStation{Suffix}), else null. */
export function stationRole(id: string): "base" | "top" | null {
  const m = buildMeta(id);
  if (!m || m.kind !== "station") return null;
  return id.indexOf("baseStation") === 0 ? "base" : id.indexOf("topStation") === 0 ? "top" : null;
}

/** The entry C# sent for `id`, or undefined. */
export function findBuildItem(items: readonly BuildItem[] | null | undefined, id: BuildItemId): BuildItem | undefined {
  if (!items) return undefined;
  for (const item of items) if (item && item.id === id) return item;
  return undefined;
}

export interface OrderedBuildItem {
  id: BuildItemId;
  item: BuildItem;
}

/** The groups with the items C# sent, in build order. The function drops unknown ids, keeps the first of duplicate ids, and
 *  drops a group with no item. The picker filter (hubRules.visibleIds) applies after this. */
export function groupBuildItems(items: readonly BuildItem[] | null | undefined): OrderedBuildItem[][] {
  const out: OrderedBuildItem[][] = [];
  for (const group of BUILD_GROUPS) {
    const g: OrderedBuildItem[] = [];
    for (const id of group) {
      const item = findBuildItem(items, id);
      if (item) g.push({ id, item });
    }
    if (g.length) out.push(g);
  }
  return out;
}
