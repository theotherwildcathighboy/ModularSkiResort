/*
 * src/hub/buildItems.ts - display helpers for the `build` binding and the run kinds.
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-C); amended
 * 2026-10-03 (Round U2, implementer I-3: BUILD_GROUPS in build order, step badges, run-kind
 * stripes; docs/research/hub-u2-spec.md section 4.2 "footer -> BuildStrip" and 5.8); amended
 * 2026-10-04 (Round E1, stream S6; econ-round spec ch6 section 6.14: nine items, runNatural (D16 /
 * D17), the night run's own icon (D20, chapter 2 section 7.2), groups 2 / 3 / 4).
 * Contract 6 (2026-10-11, Catalog Phase B, code round 2, hub group; docs/research/art/assets-2026-10/
 * code-round-2/round-2-contract.md sections 1.6, 8.5, 8.8): BUILD_ITEMS mirrors the ONE C# table
 * (mod/SkiResortMod/Systems/SkiCatalog.cs, the BUILD-ITEMS block) row by row - id, kind (the C#
 * BuildItemKind name in lower camel case), liftType (LiftTypeDef.Id or "") and runKind (RunKind or -1);
 * gate rule R16 FAILS any difference, a missing icon and a group that does not hold every id once.
 * Contract:
 *   - C# owns WHAT each build id launches and WHICH ids it sends (bindings.ts BuildItem; a pending prefab
 *     that is not imported and every item of a Shuttle lift type are not sent); this file only maps an id
 *     to what the panel draws: its icon, its group and place in the strip, its badge.
 *   - An id this bundle does not know is ignored (bindings.ts BuildItem.id: "treat an unknown
 *     id as not for this bundle"); a known id C# did not send is not drawn.
 *   - BUILD ORDER (spec 5.8; DECISIONS 130 "base station -> top station -> lift line"; D17 run
 *     order): groups [sign, lodge tiers] | [school, patrolHut, patrolHQ] | [each lift type's base
 *     "1", top "2", line "3"] | [runNatural, runGroomed, runSnowmaking, runNight]. The lodge tiers
 *     and the lift types are ALTERNATIVES (contract 6 pickers, hubRules.pickerSets): the strip draws
 *     the selected tier and the selected type's three tiles only, so a city with every prefab shows
 *     2 + 3 + 3 + 4 tiles and an FG-only city today's 2 + 3 + 4. The numbers are the stations-first
 *     order; nothing is ever blocked by it (line-first still works through the reconnect, DECISIONS 40).
 *     Keep BUILD_ITEMS, BUILD_GROUPS and BUILD_ITEM_ICON literal arrays / objects of literals (R16).
 *   - Icons are the prefabs' own UIObject icons (assetdb://global/<id> copied from the prefab
 *     JSONs, gamedata-mirror/ImportedData/<Prefab>/<Prefab>.Prefab "m_Icon", lines below). Round E1
 *     (D20) gives the natural and the night run their own icons (art awaiting Richard's verdict,
 *     chapter 2 section 7.3 is the two-line revert); run tiles keep a kind stripe in the run list's
 *     marker colours (hub.module.scss .stripe*, the natural run's two short bars). If an icon fails
 *     to load the tile draws a CSS square instead (AssetIcon, src/hub/parts.tsx). Round M1 deletes
 *     the SkiResortCategory prefab but keeps SkiResortTab.png (aed39078...), which is no longer
 *     drawn: the launchers use SkiLauncherGlyph.svg since S1 F9 (HubLauncher.tsx HUB_GLYPH; HUB_ICON is
 *     the rollback).
 *   - Icons, style E (2026-10-04; econ-round e1-spec.md section 8): all nine icons are redrawn by
 *     assets/blender/ski_icons.py (same file names, so the same ids) and the lift tile gets its own
 *     SkiChairliftLine.png (890dda43..., SkiChairliftHV.Prefab m_Icon) instead of SkiResortTab.png.
 *   - Icons round 2 (2026-10-11; docs/research/art/assets-2026-10/build/icons-round-2.md; Richard's verdict B2):
 *     the 16 contract-6 placeholders (section 8.8) are replaced by their own style-E tiles from
 *     assets/blender/ski_icons.py, PNG + hand-minted .png.cid in gamedata-mirror/ImportedData/SkiResortIcons/
 *     (file = the prefab name; a line tile is <Type>Line.png). The prefab-surgery round sets each new prefab's
 *     UIObject m_Icon to the same assetdb://global/<cid> (the table in icons-round-2.md) and installs the PNGs
 *     game-side; until then those prefabs do not exist, C# does not send their ids, and no tile asks for them.
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
  // Icons round 2 (2026-10-11): SkiResortIcons/<file>.png.cid; the surgery round writes the same id into each
  // prefab's m_Icon (docs/research/art/assets-2026-10/build/icons-round-2.md).
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
  baseStationTram: "assetdb://global/346a857e6774be57ac9b39faae5eb43e", // SkiTramBaseStationBuilding.png (not sent until Phase C)
  topStationTram: "assetdb://global/9988b8f6ee556ce5159e1ff361b8b2c8", // SkiTramTopStationBuilding.png (not sent until Phase C)
  liftTram: "assetdb://global/84b357124bf9365a0b29833f2ea85ed5", // SkiTramLine.png (SkiTramHV; not sent until Phase C)
  runNatural: "assetdb://global/6cf0402a558e436be4459d740b432f98", // SkiRunNaturalA.Prefab:65 (D20; art, owner's verdict)
  runGroomed: "assetdb://global/092ca9d440ccbca7cf9783c4da1790f5", // SkiRunGroomedA.Prefab:65
  runSnowmaking: "assetdb://global/bc121f42eed0fd6829eacccc5b69b4f8", // SkiRunSnowmakingA.Prefab:65
  runNight: "assetdb://global/a239eef8d124c3ec467a5d8afaa7e447", // SkiRunNightA.Prefab:65 (D20: own icon; was snowmaking's)
  runCatTrack: "assetdb://global/746a4b7f2c2ea595edf43d1ecc8a8520", // SkiRunCatTrackA.Prefab:65 (T38: SkiRunCatTrackA.png, own icon; was the groomed icon in T54 probe 1)
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

/** The luxury lodge tiers: C# SkiCatalog.Lodges rows with Luxury true (summit, grand; round-2-contract section 1.5).
 *  Display only (the tile tooltip's BUILD_TIP_LUXURY line, the price tolerance): no rule hangs on it. There is NO limit
 *  on how many lodges of any tier a player builds (Richard, 2026-10-11; the one-per-ski-area lock D3 was removed).
 *  Keep in step with SkiCatalog.cs by hand. */
export const LUXURY_LODGES: ReadonlySet<BuildItemId> = new Set<BuildItemId>(["lodgeSummit", "lodgeGrand"]);

/** The run kind of a run tile (for its stripe and its RUN_TYPE name), else undefined. */
export const BUILD_RUN_KIND: Readonly<Partial<Record<BuildItemId, RunKind>>> = {
  runNatural: RUN_KIND.Natural,
  runGroomed: RUN_KIND.Groomed,
  runSnowmaking: RUN_KIND.Snowmaking,
  runNight: RUN_KIND.Night,
  runCatTrack: RUN_KIND.CatTrack,
};

/** The build id whose prefab is a run of this kind. */
export const RUN_KIND_ITEM: Readonly<Record<RunKind, BuildItemId>> = {
  [RUN_KIND.Groomed]: "runGroomed",
  [RUN_KIND.Snowmaking]: "runSnowmaking",
  [RUN_KIND.Night]: "runNight",
  [RUN_KIND.Natural]: "runNatural",
  [RUN_KIND.CatTrack]: "runCatTrack",
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

/** "base" / "top" for a station item (the ids follow baseStation{Suffix} / topStation{Suffix}: LiftBatch CT-14),
 *  else null. */
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
