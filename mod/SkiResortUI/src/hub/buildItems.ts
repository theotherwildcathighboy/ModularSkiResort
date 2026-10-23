/*
 * Display helpers for the `build` binding and the run kinds.
 *
 * BUILD_ITEMS mirrors the one C# table (mod/SkiResortMod/Systems/SkiCatalog.cs, the BUILD-ITEMS block) row by row:
 * id, kind (the C# BuildItemKind name in lower camel case), liftType (LiftTypeDef.Id or "") and runKind (RunKind or -1).
 * Gate rule R16 fails any difference, a missing icon, and a group that does not hold every id once; keep BUILD_ITEMS,
 * BUILD_GROUPS and BUILD_ITEM_ICON literal arrays / objects of literals.
 *   - C# owns what each build id launches and which ids it sends (a pending prefab that is not imported and every item
 *     of a Shuttle lift type are not sent); this file only maps an id to its icon, group, place in the strip and badge.
 *   - An id this bundle does not know is ignored; a known id C# did not send is not drawn.
 *   - Build order: [sign, lodge tiers] | [school, patrolHut, patrolHQ] | [each lift type's base "1", top "2", line "3"] |
 *     [runNatural, runGroomed, runSnowmaking, runNight, runCatTrack]. Lodge tiers and lift types are alternatives
 *     (hubRules.pickerSets): the strip draws the selected tier and the selected type's three tiles only. The step
 *     numbers suggest stations first; nothing is blocked by it (line-first works through the reconnect).
 *   - Icons are the prefabs' own UIObject icons (assetdb://global/<id> copied from the prefab JSONs,
 *     gamedata-mirror/ImportedData/<Prefab>/<Prefab>.Prefab "m_Icon"; the line numbers below). Run tiles keep a kind
 *     stripe in the run list's marker colors (hub.module.scss .stripe*). If an icon fails to load the tile draws a CSS
 *     square instead (AssetIcon, src/hub/parts.tsx). The icons are drawn by assets/blender/ski_icons.py (same file
 *     names, so the same ids); the .png.cid files for the SkiResortIcons set are in gamedata-mirror/ImportedData/
 *     SkiResortIcons/ (docs/research/art/assets-2026-10/build/icons-round-2.md). A prefab that does not exist yet is
 *     not sent by C#, so no tile asks for its icon.
 */
import { BUILD_ITEM_IDS, RUN_KIND } from "../bindings";
import type { BuildItem, BuildItemId, RunKind } from "../bindings";

/** C# BuildItemKind (SkiCatalog.cs) in lower camel case: Sign, Lodge, School, Patrol, Station, Line, Run. */
export type BuildItemKind = "sign" | "lodge" | "school" | "patrol" | "station" | "line" | "run";

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
];

const ICON_SIGN = "assetdb://global/b0f0f5932ab14cacca5ab8afef91f6dc"; // SkiResortGate.Prefab:48
const ICON_LODGE = "assetdb://global/68de897b1138f9c779cff55258956cde"; // SkiAFrameDayLodge.Prefab:86
const ICON_LINE = "assetdb://global/890dda430068b39807412dc4ce0e9af8"; // SkiChairliftHV.Prefab:202 (SkiChairliftLine.png; style E)
const ICON_BASE = "assetdb://global/c019a1e22083fd0285b4b6cf21f98f3c"; // SkiBaseStationBuilding.Prefab:50
const ICON_TOP = "assetdb://global/3eb0d7b98fc9ee162816d9bb77056956"; // SkiTopStationBuilding.Prefab:44

export const BUILD_ITEM_ICON: Readonly<Record<BuildItemId, string>> = {
  sign: ICON_SIGN,
  lodge: ICON_LODGE,
  // SkiResortIcons/<file>.png.cid; each prefab's m_Icon carries the same id (docs/research/art/assets-2026-10/build/icons-round-2.md)
  lodgeHut: "assetdb://global/a5cbeffe37ecc4a08dba75e5f41721a8", // SkiLodgeHut.png
  lodgeMid: "assetdb://global/d3df64753c68a935d57aff51fc52815b", // SkiLodgeMid.png
  lodgeSummit: "assetdb://global/1d6966e5716f604d124d7da9130493cd", // SkiLodgeSummit.png
  lodgeGrand: "assetdb://global/25a71e28e84f3c9ab4e7140a389daf95", // SkiLodgeGrand.png
  school: "assetdb://global/91cfab697c3e967769f87e74ef15d585", // SkiSchool.png
  patrolHut: "assetdb://global/c5c30e4a00c458311ceb5944a68e5628", // SkiPatrolHut.png
  patrolHQ: "assetdb://global/c0fb5f466715cb377e38bf2ea57f75f8", // SkiPatrolHQ.png
  baseStation: ICON_BASE,
  topStation: ICON_TOP,
  lift: ICON_LINE,
  baseStationDetachable: "assetdb://global/8ec27f87a6a893c66a4874eb0ea37ade", // SkiDQBaseStationBuilding.png
  topStationDetachable: "assetdb://global/c88f224955a093d4cc6f65ed3aef3f13", // SkiDQTopStationBuilding.png
  liftDetachable: "assetdb://global/f9f84b7904b1e6eadedf2dd84621ccb9", // SkiDetachableLine.png (SkiDetachableHV)
  baseStationGondola: "assetdb://global/6ec56586a0f75099238aed5b6fc78db2", // SkiGondolaBaseStationBuilding.png
  topStationGondola: "assetdb://global/eb97c9e8b0bef4a6f1466b33d2368459", // SkiGondolaTopStationBuilding.png
  liftGondola: "assetdb://global/bb635c9ad1e17621e3fffec16bc80256", // SkiGondolaLine.png (SkiGondolaHV)
  baseStationTram: "assetdb://global/346a857e6774be57ac9b39faae5eb43e", // SkiTramBaseStationBuilding.png
  topStationTram: "assetdb://global/9988b8f6ee556ce5159e1ff361b8b2c8", // SkiTramTopStationBuilding.png
  liftTram: "assetdb://global/84b357124bf9365a0b29833f2ea85ed5", // SkiTramLine.png (SkiTramHV)
  runNatural: "assetdb://global/6cf0402a558e436be4459d740b432f98", // SkiRunNaturalA.Prefab:65
  runGroomed: "assetdb://global/092ca9d440ccbca7cf9783c4da1790f5", // SkiRunGroomedA.Prefab:65
  runSnowmaking: "assetdb://global/bc121f42eed0fd6829eacccc5b69b4f8", // SkiRunSnowmakingA.Prefab:65
  runNight: "assetdb://global/a239eef8d124c3ec467a5d8afaa7e447", // SkiRunNightA.Prefab:65
  runCatTrack: "assetdb://global/746a4b7f2c2ea595edf43d1ecc8a8520", // SkiRunCatTrackA.Prefab:65 (SkiRunCatTrackA.png)
};

/** Four groups in build order (gate R16 checks every id appears exactly once). Group 1's lodge tiers and group 3's
 *  lift types are alternatives (hubRules.pickerSets): only the selected one is drawn. */
export const BUILD_GROUPS: ReadonlyArray<readonly BuildItemId[]> = [
  ["sign", "lodge", "lodgeHut", "lodgeMid", "lodgeSummit", "lodgeGrand"],
  ["school", "patrolHut", "patrolHQ"],
  [
    "baseStation", "topStation", "lift",
    "baseStationDetachable", "topStationDetachable", "liftDetachable",
    "baseStationGondola", "topStationGondola", "liftGondola",
    "baseStationTram", "topStationTram", "liftTram",
  ],
  ["runNatural", "runGroomed", "runSnowmaking", "runNight", "runCatTrack"],
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
 *  BUILD_TIP_LUXURY line, the price tolerance): no rule hangs on it, and there is no limit on lodges of any tier.
 *  Keep in step with SkiCatalog.cs by hand. */
export const LUXURY_LODGES: ReadonlySet<BuildItemId> = new Set<BuildItemId>(["lodgeSummit", "lodgeGrand"]);

/** What a placed building draws a month, and how it meets the ground. */
export interface BuildingDraw {
  /** The prefab (gamedata-mirror/ImportedData/<prefab>/<prefab>.Prefab), for the gate. */
  prefab: string;
  /** ServiceConsumption.m_Upkeep, money per month at a 100 % budget; 0 = not billed (a station has no CityServiceBuilding, so the game
   *  never bills it: CityServiceBuilding.cs:23 adds ServiceUpkeepData only there, BuildingInitializeSystem.cs:1166-1191; the lift's
   *  running cost covers it). */
  upkeep: number;
  /** ServiceConsumption.m_ElectricityConsumption / m_WaterConsumption / m_GarbageAccumulation, the asset panel's units (Power,
   *  VolumePerMonth, WeightPerMonth; PrefabUISystem.cs:552-559 writes them CeilToInt). */
  power: number;
  water: number;
  garbage: number;
  /** Jobs: Workplace.m_Workplaces, or for a roadless building SkiCatalog's SignJobs, which the linked sign carries. */
  workers: number;
  /** BuildingTerraformOverride m_DontRaise AND m_DontLower: the lot keeps the terrain (LotHeightSystem.cs:253 levels it unless both
   *  are set); false = the lot is leveled (no override block, or not both flags). */
  keepsSlope: boolean;
}

/** One row per building tile, from the prefabs (gate: tools/test-hub-rules.js T22 reads every prefab and SkiCatalog's SignJobs
 *  and fails on a difference). A value 0 draws no row (as vanilla's asset panel). Lines and runs are not buildings: no row. */
export const BUILDING_DRAW: Readonly<Partial<Record<BuildItemId, BuildingDraw>>> = {
  sign: { prefab: "SkiResortGate", upkeep: 0, power: 0, water: 0, garbage: 0, workers: 0, keepsSlope: true },
  lodge: { prefab: "SkiAFrameDayLodge", upkeep: 3000, power: 1500, water: 1000, garbage: 80, workers: 4, keepsSlope: false },
  lodgeHut: { prefab: "SkiLodgeHut", upkeep: 3000, power: 0, water: 0, garbage: 0, workers: 4, keepsSlope: true },
  lodgeMid: { prefab: "SkiLodgeMid", upkeep: 8000, power: 3500, water: 2300, garbage: 190, workers: 12, keepsSlope: true },
  lodgeSummit: { prefab: "SkiLodgeSummit", upkeep: 18000, power: 0, water: 0, garbage: 0, workers: 8, keepsSlope: true },
  lodgeGrand: { prefab: "SkiLodgeGrand", upkeep: 20000, power: 3500, water: 5000, garbage: 430, workers: 18, keepsSlope: false },
  school: { prefab: "SkiSchool", upkeep: 3000, power: 2500, water: 1700, garbage: 130, workers: 4, keepsSlope: true },
  patrolHut: { prefab: "SkiPatrolHut", upkeep: 1500, power: 0, water: 0, garbage: 0, workers: 2, keepsSlope: true },
  patrolHQ: { prefab: "SkiPatrolHQ", upkeep: 1500, power: 0, water: 0, garbage: 0, workers: 2, keepsSlope: true },
  baseStation: { prefab: "SkiBaseStationBuilding", upkeep: 0, power: 1500, water: 0, garbage: 0, workers: 0, keepsSlope: true },
  topStation: { prefab: "SkiTopStationBuilding", upkeep: 0, power: 0, water: 0, garbage: 0, workers: 0, keepsSlope: true },
  baseStationDetachable: { prefab: "SkiDQBaseStationBuilding", upkeep: 0, power: 1500, water: 0, garbage: 0, workers: 0, keepsSlope: true },
  topStationDetachable: { prefab: "SkiDQTopStationBuilding", upkeep: 0, power: 0, water: 0, garbage: 0, workers: 0, keepsSlope: true },
  baseStationGondola: { prefab: "SkiGondolaBaseStationBuilding", upkeep: 0, power: 1500, water: 0, garbage: 0, workers: 0, keepsSlope: true },
  topStationGondola: { prefab: "SkiGondolaTopStationBuilding", upkeep: 0, power: 0, water: 0, garbage: 0, workers: 0, keepsSlope: true },
  baseStationTram: { prefab: "SkiTramBaseStationBuilding", upkeep: 0, power: 1500, water: 0, garbage: 0, workers: 0, keepsSlope: false },
  topStationTram: { prefab: "SkiTramTopStationBuilding", upkeep: 0, power: 0, water: 0, garbage: 0, workers: 0, keepsSlope: true },
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

/** The groups with the items C# sent, in build order; unknown ids dropped, duplicates keep the
 *  first; a group with no item is dropped. The picker filter (hubRules.visibleIds) applies after this. */
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
