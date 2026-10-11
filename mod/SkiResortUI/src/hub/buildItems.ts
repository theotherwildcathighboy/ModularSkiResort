/*
 * src/hub/buildItems.ts - display helpers for the `build` binding and the run kinds.
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-C); amended
 * 2026-10-03 (Round U2, implementer I-3: BUILD_GROUPS in build order, step badges, run-kind
 * stripes; docs/research/hub-u2-spec.md section 4.2 "footer -> BuildStrip" and 5.8); amended
 * 2026-10-04 (Round E1, stream S6; econ-round spec ch6 section 6.14: nine items, runNatural (D16 /
 * D17), the night run's own icon (D20, chapter 2 section 7.2), groups 2 / 3 / 4).
 * Contract:
 *   - C# owns WHAT each build id launches (bindings.ts BuildItem); this file only maps an id to
 *     what the panel draws: its icon, its group and place in the strip, its badge.
 *   - An id this bundle does not know is ignored (bindings.ts BuildItem.id: "treat an unknown
 *     id as not for this bundle"); a known id C# did not send is not drawn.
 *   - BUILD ORDER (spec 5.8; DECISIONS 130 "base station -> top station -> lift line"; D17 run
 *     order): groups [sign, lodge] | [baseStation "1", topStation "2", lift "3"] | [runNatural,
 *     runGroomed, runSnowmaking, runNight]. The numbers are the stations-first order; nothing is
 *     ever blocked by it (line-first still works through the reconnect, DECISIONS 40).
 *     tools/check-bundle.js rule R16 checks that BUILD_GROUPS holds every BUILD_ITEM_IDS id exactly
 *     once (and that the ids equal C#'s ItemIds): keep it a literal array of string literals.
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
 */
import { BUILD_ITEM_IDS, RUN_KIND } from "../bindings";
import type { BuildItem, BuildItemId, RunKind } from "../bindings";

export const BUILD_ITEM_ICON: Readonly<Record<BuildItemId, string>> = {
  sign: "assetdb://global/b0f0f5932ab14cacca5ab8afef91f6dc", // SkiResortGate.Prefab:48
  lodge: "assetdb://global/68de897b1138f9c779cff55258956cde", // SkiAFrameDayLodge.Prefab:86
  runNatural: "assetdb://global/6cf0402a558e436be4459d740b432f98", // SkiRunNaturalA.Prefab:65 (D20; art, owner's verdict)
  runGroomed: "assetdb://global/092ca9d440ccbca7cf9783c4da1790f5", // SkiRunGroomedA.Prefab:65
  runSnowmaking: "assetdb://global/bc121f42eed0fd6829eacccc5b69b4f8", // SkiRunSnowmakingA.Prefab:65
  runNight: "assetdb://global/a239eef8d124c3ec467a5d8afaa7e447", // SkiRunNightA.Prefab:65 (D20: own icon; was snowmaking's)
  lift: "assetdb://global/890dda430068b39807412dc4ce0e9af8", // SkiChairliftHV.Prefab:202 (SkiChairliftLine.png; style E, was SkiResortTab.png)
  baseStation: "assetdb://global/c019a1e22083fd0285b4b6cf21f98f3c", // SkiBaseStationBuilding.Prefab:50
  topStation: "assetdb://global/3eb0d7b98fc9ee162816d9bb77056956", // SkiTopStationBuilding.Prefab:44
};

/** Three groups in build order (design 4: 2 / 3 / 4 tiles; gate R16 checks every id appears once). */
export const BUILD_GROUPS: ReadonlyArray<readonly BuildItemId[]> = [
  ["sign", "lodge"],
  ["baseStation", "topStation", "lift"],
  ["runNatural", "runGroomed", "runSnowmaking", "runNight"],
];

/** The step number drawn on the lift tiles (stations first, then the line). */
export const BUILD_STEP: Readonly<Partial<Record<BuildItemId, string>>> = {
  baseStation: "1",
  topStation: "2",
  lift: "3",
};

/** The run kind of a run tile (for its stripe and its RUN_TYPE name), else undefined. */
export const BUILD_RUN_KIND: Readonly<Partial<Record<BuildItemId, RunKind>>> = {
  runNatural: RUN_KIND.Natural,
  runGroomed: RUN_KIND.Groomed,
  runSnowmaking: RUN_KIND.Snowmaking,
  runNight: RUN_KIND.Night,
};

/** The build id whose prefab is a run of this kind. */
export const RUN_KIND_ITEM: Readonly<Record<RunKind, BuildItemId>> = {
  [RUN_KIND.Groomed]: "runGroomed",
  [RUN_KIND.Snowmaking]: "runSnowmaking",
  [RUN_KIND.Night]: "runNight",
  [RUN_KIND.Natural]: "runNatural",
};

export function isBuildItemId(id: string): id is BuildItemId {
  return (BUILD_ITEM_IDS as readonly string[]).indexOf(id) >= 0;
}

export function isRunKind(kind: number): kind is RunKind {
  return kind === RUN_KIND.Groomed || kind === RUN_KIND.Snowmaking || kind === RUN_KIND.Night || kind === RUN_KIND.Natural;
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
 *  first; a group with no item is dropped. */
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
