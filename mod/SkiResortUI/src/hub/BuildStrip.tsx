/*
 * src/hub/BuildStrip.tsx - the Build strip, the panel's footer: tabs (Lifts | Runs | Buildings), a fixed-height family row, icon tiles,
 * two fixed one-string status lines, the one dot. Fixed heights and equal-width tiles: nothing may move under the mouse.
 * Locked tiles are dimmed with a Lock glyph, not `disabled`; a click shows the reason and still sends launch(id), C# refuses and logs it.
 * Reads only the core `build` binding (plus local pick$), so it is also the contract-mismatch panel's footer; the rest arrives as FooterInfo.
 * Every tile and picker chip is a Button with focusKey={FOCUS_DISABLED} (rule R15).
 * The Buildings tab draws two tile rows (BUILDING_ROW2_KINDS: the sign and the lodges, then the bus depot, the school and the patrol huts),
 * each with its caption row and each filling the strip, so every label fits whole (2026-11-06: one row of 12 cut them to "A-f...";
 * on a six-column grid "Bus depot" was still cut, so the services row stretches its four tiles).
 * Contract 37: on the ok panel with a Welcome Sign, a full-width "Mergeable buildings" toggle under the rows arms the merge overlay
 * (armMergeOverlay; a mode, not a building, so it is a toggle, not a tile); while armed it carries the green / gray legend with the
 * outlined counts (MergeLegend). Hovered, the status lines name it and say what can be merged. On the other tabs the status lines show the
 * legend while the overlay is on, until a tile is hovered.
 * Engine notes: EngineNotes/BuildStrip.tsx.md
 */
import { useEffect, useRef, useState } from "react";
import { bindLocalValue, useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { build$, follow$, glade$, hubTriggers, liftLine$, mergeOverlay$, problems$, resortLive$, resorts$, selectedResort$ } from "../bindings";
import type { BuildItem, BuildItemId, MergeOverlayState, RunKind } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry, Translate } from "../l10n";
import {
  BUILD_ITEMS, BUILD_ITEM_ICON, BUILD_RUN_KIND, BUILD_STEP, BUILDING_DRAW, LUXURY_LODGES, ROAD_OPTIONAL_LODGES, buildMeta, groupBuildItems, stationRole,
} from "./buildItems";
import type { OrderedBuildItem } from "./buildItems";
import {
  NO_PICK, dotItem, dotTarget, drawnTabs, effectiveSelection, effectiveTab, footerState, line1Variant, liveOf, nextVariant,
  optionOf, pickerSets, tabOfDot, tabOfItem, tabOfNext, visibleIds,
} from "./hubRules";
import type { BuildTab, DotTarget, NextVariant, PickerSelection, PickerSet } from "./hubRules";
import { buildTab$ } from "./hubState";
import {
  ASSET_NAME, BUILD_LABEL, BUILD_SHORT_RUN, BUILD_TAB, DRAW_LABEL, HUB_FMT, HUB_LOC, LIFT_TYPE, LODGE_TIER, NEXT_LINE1, NEXT_LINE2,
  RUN_TYPE, sentKeyEntry,
} from "./hubText";
import { AssetIcon, Glyph, LocText, Tip, TipLines, cx, numArg } from "./parts";
import { CatTrackDeleteChip } from "./CatTrackControls";
import { QuickRunsChip } from "./QuickRunsControls";
import { MergeLegend } from "./MergedFold";
import type { TipLine } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import styles from "./hub.module.scss";

/* What the footer needs beyond core `build`. */
export interface FooterInfo {
  /* The one dot (null = none). */
  dot: BuildItemId | null;
  next: NextVariant;
  /* The notice: the shown area's name while the 6 s notice runs, else null. */
  followName: string | null;
  /*
   * Draw the lift generator toggle (ok panel only; it reads the non-CORE liftLine binding). The cat track tool has no chip of
   * its own since 2026-10-24: the Cat track tile opens it (C# ApplyLaunch).
   */
  liftTool?: boolean;
  /* Draw the Quick runs chip on the Runs tab (ok panel only; it reads the non-CORE quickRuns binding). */
  quickRuns?: boolean;
  /* Contract 37: draw the Mergeable buildings toggle under the Buildings rows (ok panel with a Welcome Sign), and the overlay's state for it. */
  mergeTile?: boolean;
  mergeOverlay?: MergeOverlayState | null;
}

/* The merge toggle's icon: the game's building-upgrade icon (a merged building gains a lodge tier). */
const MERGE_TILE_ICON = "Media/Game/Icons/BuildingUpgrade.svg";

/* The Buildings tab's second tile row: the services (bus depot, school, patrol); the sign and the lodges are the first. */
const BUILDING_ROW2_KINDS: readonly string[] = ["depot", "school", "patrol"];


export const MISMATCH_FOOTER: FooterInfo = { dot: null, next: "done", followName: null };

export const FOLLOW_NOTICE_MS = 6000;

/* The Buildings-tab kinds that each draw as their own tile group. */
const SPLIT_KINDS: readonly string[] = ["sign", "lodge", "depot", "school", "patrol"];

/* The pickers' choice for the UI session (local value: nothing sent to C#; a UI reload resets it). */
export const pick$ = bindLocalValue<PickerSelection>(NO_PICK);

// Index = RunKind; 3 (natural) is drawn separately (two bars), 4 (cat track) is green.
const KIND_STRIPE = ["stripeGroomed", "stripeSnowmaking", "stripeNight", "stripeNight", "stripeCatTrack"] as const;

function nameEntry(id: BuildItemId): LocEntry {
  if (id === "runGroomed") return BUILD_LABEL.run0; // the Run tile draws every piste (R3)
  const kind = BUILD_RUN_KIND[id];
  if (kind !== undefined) return RUN_TYPE[kind];
  return ASSET_NAME[id] ?? HUB_LOC.locked;
}

/* The C# kind of an item ("" for an id this bundle does not know: line1Variant then reads cost / name). */
function kindOf(id: BuildItemId): string {
  const m = buildMeta(id);
  return m ? m.kind : "";
}

function Line1({ id, item, t }: { id: BuildItemId; item: BuildItem; t: Translate }): JSX.Element {
  const NAME = t(nameEntry(id));
  switch (line1Variant(kindOf(id), item)) {
    case "run": return <LocText entry={HUB_FMT.buildLineRun} args={{ NAME, FEE: numArg(item.price ?? -1, Unit.Money), PERHA: numArg(item.pricePerHa ?? -1, Unit.Money) }} />;
    case "lift": return <LocText entry={HUB_FMT.buildLineLift} args={{ NAME, PERKM: numArg(item.costPerKm ?? -1, Unit.Money) }} />;
    case "cost": return <LocText entry={HUB_FMT.buildLineCost} args={{ NAME, COST: numArg(item.cost ?? -1, Unit.Money) }} />;
    default: return <>{NAME}</>;
  }
}

/*
 * A tile's hint (footer line 2 and tooltip), by kind. Stations and lift lines have none: a hint only says what a player cannot
 * find by trying.
 */
function hintEntry(id: BuildItemId): LocEntry | null {
  const kind = BUILD_RUN_KIND[id];
  if (kind !== undefined) return BUILD_SHORT_RUN[kind];
  switch (kindOf(id)) {
    case "sign": return HUB_LOC.buildHintSign;
    case "fence": return HUB_LOC.buildHintFence;
    case "lodge": return HUB_LOC.buildHintLodge;
    case "depot": return HUB_LOC.buildShortDepot;
    case "school": return HUB_LOC.buildShortSchool;
    case "patrol": return HUB_LOC.buildShortPatrol;
    default: return null;
  }
}

function line2Text(t: Translate, id: BuildItemId, item: BuildItem): string {
  if (item.locked) return t(sentKeyEntry(item.reason, HUB_LOC.locked));
  const hint = hintEntry(id);
  if (item.hint) return t(sentKeyEntry(item.hint, hint ?? HUB_LOC.locked));
  return hint ? t(hint) : "";
}

function onceValue(id: BuildItemId, item: BuildItem): JSX.Element | string {
  switch (line1Variant(kindOf(id), item)) {
    case "run": return <LocText entry={HUB_FMT.buildOnceRun} args={{ FEE: numArg(item.price ?? -1, Unit.Money), PERHA: numArg(item.pricePerHa ?? -1, Unit.Money) }} />;
    case "lift": return <LocText entry={HUB_FMT.buildOnceLift} args={{ PERKM: numArg(item.costPerKm ?? -1, Unit.Money) }} />;
    case "cost": return numArg(item.cost ?? -1, Unit.Money);
    default: return "–";
  }
}

function monthValue(id: BuildItemId, item: BuildItem, t: Translate): JSX.Element | string {
  const kind: RunKind | undefined = BUILD_RUN_KIND[id];
  if (kind === 3 || kind === 4) return t(HUB_LOC.buildMonthNone); // natural; cat track (no snow bill)
  if (kind !== undefined) {
    return typeof item.monthlyPerHa === "number" && item.monthlyPerHa >= 0
      ? <LocText entry={HUB_FMT.buildMonthRun} args={{ RATE: numArg(item.monthlyPerHa, Unit.Money) }} /> : "–";
  }
  const k = kindOf(id);
  if (k === "line") {
    return typeof item.monthly === "number" && item.monthly >= 0 && typeof item.monthlyPerKm === "number" && item.monthlyPerKm >= 0
      ? <LocText entry={HUB_FMT.buildMonthLift} args={{ BASE: numArg(item.monthly, Unit.Money), PERKM: numArg(item.monthlyPerKm, Unit.Money) }} /> : "–";
  }
  if (k === "station") return t(HUB_LOC.buildMonthStation);
  return t(HUB_LOC.buildMonthNone); // a building with upkeep draws its Upkeep row instead (drawRows)
}

/*
 * A building's draw rows, vanilla's asset-panel rows (each only above 0) plus Workers, then its terrain line. The Upkeep row
 * replaces "Per month" on a building the game bills; a station keeps "Per month" (the lift's running cost covers it).
 */
function drawRows(id: BuildItemId, t: Translate): TipLine[] {
  const d = BUILDING_DRAW[id];
  if (!d) return [];
  return [
    d.power > 0 && { label: t(DRAW_LABEL.electricity), value: numArg(d.power, Unit.Power) },
    d.water > 0 && { label: t(DRAW_LABEL.water), value: numArg(d.water, Unit.VolumePerMonth) },
    d.garbage > 0 && { label: t(DRAW_LABEL.garbage), value: numArg(d.garbage, Unit.WeightPerMonth) },
    d.workers > 0 && { label: t(HUB_LOC.buildTipWorkers), value: numArg(d.workers, Unit.Integer) },
    t(d.keepsSlope ? HUB_LOC.terrainSlope : HUB_LOC.terrainLevel),
  ];
}

/* The Run tile's options (R3, DECISIONS 578): Groomed off, then the snow guns and lights steps, each paid once per run. */
function runStepRows(item: BuildItem, t: Translate): TipLine[] {
  const n = (v: number | undefined, unit: Unit) => numArg(v ?? -1, unit);
  if (!((item.naturalPrice ?? -1) >= 0)) return [];
  return [
    { label: t(HUB_LOC.buildTipGroomedOff), value: <LocText entry={HUB_FMT.buildStepNatural} args={{ FEE: n(item.naturalPrice, Unit.Money), PERHA: n(item.naturalPerHa, Unit.Money) }} /> },
    { label: t(HUB_LOC.runPropsGuns), value: <LocText entry={HUB_FMT.buildStepAdd} args={{ FEE: n(item.gunsPrice, Unit.Money), PERHA: n(item.gunsPerHa, Unit.Money), RATE: n(item.gunsMonthlyPerHa, Unit.Money) }} /> },
    { label: t(HUB_LOC.runPropsLights), value: <LocText entry={HUB_FMT.buildStepAdd} args={{ FEE: n(item.lightsPrice, Unit.Money), PERHA: n(item.lightsPerHa, Unit.Money), RATE: n(item.lightsMonthlyPerHa, Unit.Money) }} /> },
    t(HUB_LOC.buildTipStepsOnce),
  ];
}

function tileTip(e: OrderedBuildItem, t: Translate): JSX.Element {
  const run = BUILD_RUN_KIND[e.id] !== undefined;
  const k = kindOf(e.id);
  const draw = BUILDING_DRAW[e.id];
  const billed = !!draw && draw.upkeep > 0 && k !== "station";
  const hint = hintEntry(e.id);
  return (
    <TipLines
      lines={[
        t(nameEntry(e.id)),
        { label: t(HUB_LOC.buildTipOnce), value: onceValue(e.id, e.item) },
        billed && draw ? { label: t(DRAW_LABEL.upkeep), value: numArg(draw.upkeep, Unit.MoneyPerMonth) }
          : { label: t(HUB_LOC.buildTipMonth), value: monthValue(e.id, e.item, t) },
        ...drawRows(e.id, t),
        e.item.active ? t(HUB_LOC.buildActive) : hint && t(hint),
        run && t(HUB_LOC.buildTipReshape),
        ...runStepRows(e.item, t),
        ROAD_OPTIONAL_LODGES.has(e.id) && t(HUB_LOC.buildTipRoadOptional), // the cat track chain instead of city utilities
        LUXURY_LODGES.has(e.id) && t(HUB_LOC.buildTipLuxury), // the luxury label and its price tolerance
        k === "depot" && t(HUB_LOC.buildTipDepot),
        k === "school" && t(HUB_LOC.buildTipSchool),
        k === "patrol" && t(HUB_LOC.buildTipPatrol),
        e.item.locked && t(sentKeyEntry(e.item.reason, HUB_LOC.locked)),
      ]}
    />
  );
}

function chipLabel(set: PickerSet, value: string, t: Translate): string {
  const e = set === "lift" ? LIFT_TYPE[value] : LODGE_TIER[value as BuildItemId];
  return e ? t(e) : value;
}

/* One segmented control (a set with >= 2 options). Chips are cs2/ui Buttons with FOCUS_DISABLED (R15). */
function PickerSetView({ set, options, chosen, dot, t, onPick }: {
  set: PickerSet; options: readonly string[]; chosen: string; dot: DotTarget; t: Translate;
  onPick: (set: PickerSet, value: string) => void;
}): JSX.Element {
  return (
    <div className={styles.pickerSet} style={{ flexGrow: options.length }}>
      {options.map((value, i) => {
        const label = chipLabel(set, value, t);
        const on = value === chosen;
        const dotHere = !!dot && dot.kind === "chip" && dot.set === set && dot.value === value;
        return (
          <div key={value} className={cx(styles.pickerCell, i > 0 && styles.pickerCellNext)}>
            <Button
              focusKey={FOCUS_DISABLED}
              theme={{ button: cx(styles.pickerChip, on && styles.pickerChipOn) }}
              selected={on}
              onSelect={() => onPick(set, value)}
            >
              <div className={styles.pickerLabel}>{label}</div>
              {dotHere && <div className={styles.buildHintDot} />}
            </Button>
          </div>
        );
      })}
    </div>
  );
}

/* A tile's visible label (hubText BUILD_LABEL): by role for stations / lines, by kind or id for the rest, by run kind for runs. */
function labelEntry(id: BuildItemId): LocEntry {
  const rk = BUILD_RUN_KIND[id];
  if (rk !== undefined) return BUILD_LABEL["run" + rk] ?? nameEntry(id);
  switch (kindOf(id)) {
    case "station": return BUILD_LABEL[stationRole(id) === "top" ? "top" : "base"];
    case "line": return BUILD_LABEL.line;
    case "fence": return BUILD_LABEL.fence;
    case "sign": return BUILD_LABEL.sign;
    case "lodge": return LODGE_TIER[id] ?? BUILD_LABEL.lodge;
    case "depot": return BUILD_LABEL.depot;
    case "school": return BUILD_LABEL.school;
    case "patrol": return id === "patrolHQ" ? BUILD_LABEL.patrolHQ : BUILD_LABEL.patrolHut;
    default: return nameEntry(id);
  }
}

/*
 * The tab row. Buttons are cs2/ui Buttons with FOCUS_DISABLED (R15); the ONE dot shows on the tab holding it when that tab is
 * not the one shown.
 */
function TabBarView({ tabs, tab, dotTab, t, onTab }: {
  tabs: readonly BuildTab[]; tab: string; dotTab: BuildTab | null; t: Translate; onTab: (id: BuildTab) => void;
}): JSX.Element {
  return (
    <div className={styles.tabRow}>
      {tabs.map((id, i) => {
        const label = t(BUILD_TAB[id]);
        const on = id === tab;
        return (
          <div key={id} className={cx(styles.pickerCell, i > 0 && styles.pickerCellNext)}>
            <Button
              focusKey={FOCUS_DISABLED}
              theme={{ button: cx(styles.tabButton, on && styles.tabButtonOn) }}
              selected={on}
              onSelect={() => onTab(id)}
            >
              <div className={styles.tabLabel}>{label}</div>
              {dotTab === id && !on && <div className={styles.buildHintDot} />}
            </Button>
          </div>
        );
      })}
    </div>
  );
}

/*
 * The Buildings tab's family row: "Lodges" over the lodge group and "Ski patrol" over the two huts (same flex-grow as the tile
 * groups below), empty over the sign and the school, so the five lodge tiles read as one choice and the row keeps its fixed
 * height.
 */
function GroupCaptions({ groups, t }: { groups: readonly OrderedBuildItem[][]; t: Translate }): JSX.Element {
  return (
    <>
      {groups.map((g, gi) => {
        const k = g.length > 0 ? kindOf(g[0].id) : "";
        const caption = k === "lodge" ? t(HUB_LOC.lodgeGroup) : k === "patrol" && g.length > 1 ? t(HUB_LOC.patrolGroup) : "";
        return (
          <div key={gi} className={cx(styles.groupCaption, !caption && styles.groupCaptionNone, gi > 0 && styles.buildGroupNext)}
            style={{ flexGrow: g.length }}>
            {caption}
          </div>
        );
      })}
    </>
  );
}

/* The Lifts tab's lift generator toggle (the tree tool's pattern): on / off, refused by C# when the tool is not armed. */
function LiftToolSet({ t }: { t: Translate }): JSX.Element {
  const g = useValue(liftLine$);
  const available = !!g?.available;
  const active = !!g?.active;
  const tip = active ? null : !available ? t(HUB_LOC.liftToolUnavailable) : t(HUB_LOC.liftToolTip);
  return (
    // About one chip wide (the type chips keep their room; four types = 4 : 1.1).
    <div className={cx(styles.pickerSet, styles.pickerSetNext)} style={{ flexGrow: 1.1 }}>
      <div className={styles.pickerCell}>
        <Tip tip={<TipLines lines={[t(HUB_LOC.liftTool), tip]} />}>
          <Button
            focusKey={FOCUS_DISABLED}
            disabled={!available && !active}
            theme={{ button: cx(styles.pickerChip, active && styles.pickerChipOn) }}
            selected={active}
            onSelect={() => hubTriggers.liftLineToggle()}
          >
            <div className={styles.pickerLabel}>{t(HUB_LOC.liftTool)}</div>
          </Button>
        </Tip>
      </div>
    </div>
  );
}

/*
 * The Runs tab's family row: the glade (tree) tool's on / off, the same trigger as the Runs view's "Tree tool" button
 * (TreeControls.tsx; reads the CORE glade binding only), and on the ok panel the Quick runs chip in the slot beside it.
 */
function TreeToolSet({ t, quick }: { t: Translate; quick: boolean }): JSX.Element {
  const g = useValue(glade$);
  const available = !!g?.available;
  const active = !!g?.active;
  const tip = active ? null
    : !available ? t(sentKeyEntry(g?.reason, HUB_LOC.treesUnavailable))
      : t(HUB_LOC.treeToolTip);
  return (
    <>
      <div className={styles.pickerSet} style={{ flexGrow: 2 }}>
        <div className={styles.pickerCell}>
          <Tip tip={<TipLines lines={[t(HUB_LOC.treeTool), tip]} />}>
            <Button
              focusKey={FOCUS_DISABLED}
              disabled={!available && !active}
              theme={{ button: cx(styles.pickerChip, active && styles.pickerChipOn) }}
              selected={active}
              onSelect={() => hubTriggers.gladeToggle()}
            >
              <div className={styles.pickerLabel}>{t(HUB_LOC.treeTool)}</div>
            </Button>
          </Tip>
        </div>
      </div>
      <div className={cx(styles.pickerSet, styles.pickerSetNext)} style={{ flexGrow: 3 }}>
        {quick && <QuickRunsChip t={t} />}
        {quick && <CatTrackDeleteChip t={t} />}
      </div>
    </>
  );
}

export function BuildStrip({ info }: { info: FooterInfo }): JSX.Element {
  const t = useT();
  const items = useValue(build$);
  const sel = useValue(pick$);
  const storedTab = useValue(buildTab$);
  const [hovered, setHovered] = useState<BuildItemId | null>(null);
  const [lockedClick, setLockedClick] = useState<BuildItemId | null>(null);
  const [mergeHover, setMergeHover] = useState(false);
  const mergeTile = !!info.mergeTile;
  const mergeOn = mergeTile && !!info.mergeOverlay?.armed;

  const sets = pickerSets(items, BUILD_ITEMS);
  const eff = effectiveSelection(sets, sel);
  const visible = visibleIds(items, BUILD_ITEMS, sel);
  const all = groupBuildItems(items);
  const tabs = drawnTabs(visible, BUILD_ITEMS);
  const tab = effectiveTab(tabs, storedTab, info.next);
  // On the Buildings tab the sign, the five lodges, the school and the two patrol huts are four groups (captions "Lodges" and
  // "Ski patrol" over theirs); BUILD_GROUPS keeps its shape, the split is drawing only. Lift and run groups are unchanged.
  const groups: OrderedBuildItem[][] = [];
  const splitKey = (id: BuildItemId): string => { const k = kindOf(id); return SPLIT_KINDS.indexOf(k) >= 0 ? k : "rest"; };
  for (const g of all) {
    let cur: OrderedBuildItem[] = [];
    for (const e of g) {
      if (!visible.has(e.id) || tabOfItem(e.id, BUILD_ITEMS) !== tab) continue;
      if (cur.length > 0 && splitKey(cur[0].id) !== splitKey(e.id)) { groups.push(cur); cur = []; }
      cur.push(e);
    }
    if (cur.length > 0) groups.push(cur);
  }
  const dot = dotTarget(info.dot, items, BUILD_ITEMS, sel);
  const dotTab = tabOfDot(dot, BUILD_ITEMS);

  let hoveredEntry: OrderedBuildItem | null = null;
  let activeEntry: OrderedBuildItem | null = null;
  let lockedEntry: OrderedBuildItem | null = null;
  // The active item is looked up among ALL sent items: a lodge tier placed from the sign's own upgrade menu may not be the
  // chosen one yet (the effect below chooses it).
  for (const g of all) {
    for (const e of g) {
      if (e.item.active && !activeEntry) activeEntry = e;
      if (!visible.has(e.id) || tabOfItem(e.id, BUILD_ITEMS) !== tab) continue;
      if (e.id === hovered) hoveredEntry = e;
      if (e.id === lockedClick && e.item.locked) lockedEntry = e;
    }
  }
  const activeId = activeEntry ? activeEntry.id : null;

  // When an item becomes the active tool, its option is chosen (an item of another tier or type).
  useEffect(() => {
    const opt = optionOf(activeId, BUILD_ITEMS);
    if (!opt) return;
    const cur = pick$.value ?? NO_PICK;
    if (opt.set === "lodge" && cur.lodge !== opt.value) pick$.update({ lodge: opt.value, lift: cur.lift });
    else if (opt.set === "lift" && cur.lift !== opt.value) pick$.update({ lodge: cur.lodge, lift: opt.value });
  }, [activeId]);

  // The tab follows the active tool (a tool started elsewhere, e.g. the Start card's button) and the idle NEXT step when it
  // changes (not at mount, so the remembered tab survives closing and reopening the hub).
  useEffect(() => {
    const at = tabOfItem(activeId, BUILD_ITEMS);
    if (at && buildTab$.value !== at) buildTab$.update(at);
  }, [activeId]);
  const prevNext = useRef(info.next);
  useEffect(() => {
    if (prevNext.current === info.next) return;
    prevNext.current = info.next;
    const nt = tabOfNext(info.next);
    if (nt) buildTab$.update(nt);
  }, [info.next]);

  const state = footerState(tabs.length > 0, !!hoveredEntry, info.followName !== null, !!activeEntry, !!lockedEntry);

  let line1: JSX.Element;
  let line2: string;
  let line1Cls = styles.buildLineName;
  let line2Cls = styles.buildLineDim;
  if (state === "empty") {
    line1 = <>{t(HUB_LOC.buildEmpty)}</>; line2 = ""; line1Cls = styles.buildLineNormal;
  } else if (state === "hover" && hoveredEntry) {
    line1 = <Line1 id={hoveredEntry.id} item={hoveredEntry.item} t={t} />;
    line2 = line2Text(t, hoveredEntry.id, hoveredEntry.item);
    if (hoveredEntry.item.locked) line2Cls = styles.buildLineLocked;
  } else if (state === "follow") {
    line1 = <LocText entry={HUB_FMT.follow} args={{ NAME: info.followName ?? "" }} />; line2 = ""; line1Cls = styles.buildLineNormal;
  } else if (state === "active" && activeEntry) {
    line1 = <Line1 id={activeEntry.id} item={activeEntry.item} t={t} />;
    line2 = t(HUB_LOC.buildActive); // every building is a plain tool
  } else if (state === "locked" && lockedEntry) {
    line1 = <Line1 id={lockedEntry.id} item={lockedEntry.item} t={t} />;
    line2 = t(sentKeyEntry(lockedEntry.item.reason, HUB_LOC.locked)); line2Cls = styles.buildLineLocked;
  } else if (mergeOn && tab !== "buildings") {
    // The overlay is on: the legend and what the outlines mean, until a tile is hovered (below for the merge toggle itself).
    // On the Buildings tab the toggle carries the legend.
    line1 = <MergeLegend overlay={info.mergeOverlay} t={t} />; line2 = t(HUB_LOC.mergeTileArmed); line1Cls = styles.buildLineNormal;
  } else {
    const next1 = NEXT_LINE1[info.next];
    const next2 = NEXT_LINE2[info.next];
    line1 = <>{next1 ? t(next1) : ""}</>; line2 = next2 ? t(next2) : ""; line1Cls = styles.buildLineNormal;
  }

  if (mergeTile && mergeHover && tab === "buildings") {
    line1 = mergeOn ? <MergeLegend overlay={info.mergeOverlay} t={t} /> : <>{t(HUB_LOC.mergeTileName)}</>;
    line2 = t(mergeOn ? HUB_LOC.mergeTileArmed : HUB_LOC.mergeTileFact);
    line1Cls = mergeOn ? styles.buildLineNormal : styles.buildLineName;
    line2Cls = styles.buildLineDim;
  }

  const click = (e: OrderedBuildItem): void => {
    setLockedClick(e.item.locked ? e.id : null);
    hubTriggers.launch(e.id); // also when locked: C# refuses and logs it
  };
  const onPick = (set: PickerSet, value: string): void => {
    const cur = pick$.value ?? NO_PICK;
    pick$.update(set === "lodge" ? { lodge: value, lift: cur.lift } : { lodge: cur.lodge, lift: value });
  };

  const onTab = (id: BuildTab): void => {
    setHovered(null);
    setLockedClick(null);
    setMergeHover(false);
    buildTab$.update(id);
  };

  // The Buildings tab: two rows (lodges, services); every other tab: one row.
  const rows: OrderedBuildItem[][][] = tab === "buildings"
    ? [groups.filter((g) => BUILDING_ROW2_KINDS.indexOf(kindOf(g[0].id)) < 0), groups.filter((g) => BUILDING_ROW2_KINDS.indexOf(kindOf(g[0].id)) >= 0)]
        .filter((r) => r.length > 0)
    : [groups];

  const tileRow = (row: readonly OrderedBuildItem[][], ri: number): JSX.Element => (
    <div key={"row" + ri} className={styles.buildRow}>
      {row.map((g, gi) => (
        <div key={gi} className={cx(styles.buildGroup, gi > 0 && styles.buildGroupNext)} style={{ flexGrow: g.length }}>
          {g.map((e) => {
            const kind = BUILD_RUN_KIND[e.id];
            const step = BUILD_STEP[e.id];
            return (
              <div key={e.id} className={styles.buildCell}
                onMouseEnter={() => setHovered(e.id)}
                onMouseLeave={() => setHovered((h) => (h === e.id ? null : h))}>
                <Tip tip={tileTip(e, t)}>
                  <Button
                    focusKey={FOCUS_DISABLED}
                    theme={{ button: cx(styles.buildTile, e.item.active && styles.buildTileActive, e.item.locked && styles.buildTileLocked) }}
                    selected={e.item.active}
                    onSelect={() => click(e)}
                  >
                    <AssetIcon src={BUILD_ITEM_ICON[e.id]} className={styles.buildIcon} />
                    <div className={styles.buildTileLabel}>{t(labelEntry(e.id))}</div>
                    {kind === 3 && (
                      <div className={cx(styles.buildStripe, styles.stripeNatural)}>
                        <div className={styles.stripeNaturalPart} />
                        <div className={cx(styles.stripeNaturalPart, styles.stripeNaturalPartNext)} />
                      </div>
                    )}
                    {kind !== undefined && kind !== 3 && <div className={cx(styles.buildStripe, styles[KIND_STRIPE[kind]])} />}
                    {step && <div className={styles.buildStep}>{step}</div>}
                    {!!dot && dot.kind === "tile" && dot.id === e.id && !e.item.locked && <div className={styles.buildHintDot} />}
                    {e.item.locked && <Glyph src="Media/Glyphs/Lock.svg" className={styles.buildLock} />}
                  </Button>
                </Tip>
              </div>
            );
          })}
        </div>
      ))}
    </div>
  );

  return (
    <div className={styles.buildFooter}>
      {tabs.length > 0 && <TabBarView tabs={tabs} tab={tab} dotTab={dotTab} t={t} onTab={onTab} />}
      {tabs.length > 0 && (
        <div className={styles.pickerRow}>
          {tab === "lifts" && sets.lift.length > 0 && <PickerSetView set="lift" options={sets.lift} chosen={eff.lift} dot={dot} t={t} onPick={onPick} />}
          {tab === "lifts" && info.liftTool && <LiftToolSet t={t} />}
          {tab === "buildings" && rows.length > 0 && <GroupCaptions groups={rows[0]} t={t} />}
          {tab === "runs" && <TreeToolSet t={t} quick={!!info.quickRuns} />}
        </div>
      )}
      {rows.map((row, ri) => (ri === 0 ? tileRow(row, ri) : (
        <div key={"block" + ri} className={styles.buildRowBlock}>
          <div className={cx(styles.pickerRow, styles.pickerRowNext)}><GroupCaptions groups={row} t={t} /></div>
          {tileRow(row, ri)}
        </div>
      )))}
      {mergeTile && tab === "buildings" && (
        <div className={styles.mergeToggleRow} onMouseEnter={() => setMergeHover(true)} onMouseLeave={() => setMergeHover(false)}>
          <Tip tip={<TipLines lines={[t(HUB_LOC.mergeTileName), t(HUB_LOC.mergeOverlayTip)]} />}>
            <Button
              focusKey={FOCUS_DISABLED}
              theme={{ button: cx(styles.mergeToggle, mergeOn && styles.mergeToggleOn) }}
              selected={mergeOn}
              onSelect={() => hubTriggers.armMergeOverlay(!mergeOn)}
            >
              <AssetIcon src={MERGE_TILE_ICON} className={styles.mergeToggleIcon} />
              <div className={styles.mergeToggleLabel}>{t(HUB_LOC.mergeOverlay)}</div>
              {mergeOn && <MergeLegend overlay={info.mergeOverlay} t={t} />}
            </Button>
          </Tip>
        </div>
      )}
      <div className={cx(styles.buildStatusLine, line1Cls)}>{line1}</div>
      <div className={cx(styles.buildStatusLine, line2Cls)}>{line2}</div>
    </div>
  );
}

/* The ok panel's footer: computes FooterInfo from the non-CORE bindings (panel file). */
export function OkFooter(): JSX.Element {
  const t = useT();
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const live = useValue(resortLive$);
  const problems = useValue(problems$);
  const follow = useValue(follow$);
  const mergeOverlay = useValue(mergeOverlay$);   // contract 37: the merge toggle's on state and the legend's counts
  const [noticeOn, setNoticeOn] = useState(false);
  const baseline = useRef(follow);

  useEffect(() => {
    if (follow === baseline.current) return undefined;
    baseline.current = follow;
    setNoticeOn(true);
    const id = window.setTimeout(() => setNoticeOn(false), FOLLOW_NOTICE_MS);
    return () => window.clearTimeout(id);
  }, [follow]);

  const e = resortEntries(resorts);
  const cur = currentResort(e, selected);
  const hasSign = e.signs.length > 0;
  const shown = cur.row;
  const unlinked = !!shown && cur.isUnlinked;
  const sign = shown && !unlinked ? shown.sign : null;
  const l = shown ? liveOf(live, sign) : null;
  const info: FooterInfo = {
    dot: dotItem(problems, sign, hasSign),
    next: nextVariant(hasSign, unlinked, l),
    followName: noticeOn && shown ? (unlinked ? t(HUB_LOC.unlinkedEntry) : shown.name) : null,
    liftTool: true, // the ok panel draws the lift generator toggle
    quickRuns: true, // and the Quick runs chip
    mergeTile: hasSign, // and, with a Welcome Sign, the merge toggle (C# refuses to arm without one)
    mergeOverlay,
  };
  return <BuildStrip info={info} />;
}
