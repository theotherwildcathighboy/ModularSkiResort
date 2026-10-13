/*
 * src/hub/BuildStrip.tsx - the Build strip: the panel's FOOTER, the icon tiles in build order plus
 * two fixed one-string status lines and the one dot; since contract 6 an optional picker row above
 * the tiles (lodge tier, lift type).
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-C: a 2-column tile
 * grid in the scroll area); rewritten 2026-10-03 (Round U2, implementer I-3) for
 * docs/research/hub-u2-spec.md section 4.2 ("footer -> BuildStrip"), 4.4 J2 / J4, 5.8; rewritten
 * 2026-10-04 for contract 3 (Round E1, stream S6; docs/research/econ-round/spec/ch6-hub-ui.md
 * section 6.13, design hub-v3-design.md 6.10 / 7.4 / 7.5, D17 / D36 / D37; code from the checked
 * reference spec/ch6-hub-ui-ref/src/hub/BuildStrip.tsx); contract 6 on 2026-10-11 (Catalog Phase B,
 * code round 2, hub group; docs/research/art/assets-2026-10/code-round-2/round-2-contract.md section
 * 8.5: 25 items in four groups, the PICKERS, texts by item kind).
 * Why a footer: cs2/ui Panel renders `footer` in its own div OUTSIDE the content Scrollable
 * (index.js@684600: header / content / footer divs, the content div holding the Scrollable,
 * @733600), so the strip never scrolls away and its tiles never move under the mouse.
 * Contract:
 *   - Groups [sign, lodge tier] | [school, patrolHut, patrolHQ] | [lift type: base "1", top "2", line
 *     "3"] | [runNatural, runGroomed, runSnowmaking, runNight] (src/hub/buildItems.ts). ONE nowrap row;
 *     tiles `flex: 1 1 0; min-width: 0`, each group's flex-grow = its DRAWN tile count, so all tiles are
 *     equal at any panel width and text scale (J4: no text in tiles, so text growth cannot reflow them).
 *     A group none of whose items C# sent is not drawn: a fixed-grip-only city before any import draws
 *     today's 2 + 3 + 4 tiles.
 *   - PICKERS (contract 6): the lodge tiers and the lift types are alternatives (hubRules.pickerSets: a
 *     lodge tier is an option when C# sent it; a lift type when C# sent ALL its station and line items).
 *     The strip draws the chosen tier and the chosen type's three tiles (hubRules.visibleIds). The choice
 *     is session state (`pick$`, a module-level local value: no C# observer; it survives closing the hub,
 *     a UI reload resets it); "" or an option that disappeared reads as the first option
 *     (hubRules.effectiveSelection); when an item becomes the ACTIVE tool (launched here, or the lodge
 *     tier from the sign's own upgrade menu) its option is chosen. ONE picker row (fixed height, above the
 *     tiles) is drawn only while a set has >= 2 options; it holds one segmented control per such set,
 *     lodge first (the left group), each as wide as its option count - DEVIATION from section 8.5's
 *     "over its group": the lodge group is two tiles wide and cannot hold five chips (440 rem panel,
 *     12 tiles of about 33 rem); the order still maps left to left. The footer therefore grows by one
 *     line the session a second tier or type is imported (risk R-n), never during play.
 *   - Icon-only tiles: the prefab icon; a step badge on the lift tiles; a kind stripe on the run
 *     tiles (natural: two short bars, the dashed outline's echo; groomed white, snowmaking cyan
 *     outline, night amber: the run list's colours); the ONE amber dot (D37, design 7.4) on the tile
 *     FooterInfo.dot names (hubRules.dotItem: "sign" with no sign, else the buildItem of the shown
 *     area's first band row) - never from build[].hint any more. Contract 6: a dot on an alternative
 *     that is not chosen shows on its picker chip (hubRules.dotTarget).
 *   - Locked: dimmed + Lock glyph, NOT `disabled` (whether a disabled <button> gets mouse events in
 *     Gameface is unverified, hub-u2-spec 7.2 item 10). A click shows the reason in line 2 AND still
 *     sends launch(id): C# refuses it and logs "refused - locked" (SkiResortHubUISystem ApplyLaunch),
 *     so the click is never silent. Active: highlighted; clicking it again returns to the default tool.
 *     Contract 6 reason: REASON[AlreadyBuilt] (the shown sign; C# re-checks the
 *     sign it picks at launch, MUST 6). The lodge tiers are not ForbidMultiple once the surgery sets m_ForbidMultiple false (the prefab flag decides); they still lock for Missing / Locked / NoSign.
 *   - TWO fixed lines (`div.buildStatusLine` x 2, each ONE text node, 1.3 em of XS, nowrap + ellipsis:
 *     D37, J2 - a FIXED height, never min-height, or the footer would grow and move the tiles under
 *     the mouse). Priority (hubRules.footerState): empty > hovered tile > the D36 "Now showing" notice
 *     > the active tile > the last locked click > idle (NEXT / NEXT2 by ResortLive.nextStep). Line 1
 *     of a tile is ONE <LocText> by hubRules.line1Variant on the item's KIND (name · fee + per ha; name ·
 *     about per km for a line of any lift type; name · cost; else the name alone - a contract-2 DLL sends
 *     no new field). Line 2: the locked reason, else C#'s hint key (line items: HINT[LiftNoPair] for the
 *     SHOWN area's rows of that type), else the short hint. Item names: RUN_TYPE for runs (D17, sentence
 *     case), Assets.NAME for the others (deviation U5).
 *   - Tile tooltip: name; "One-time" and "Each month" label / value rows (run: base fee + per ha;
 *     line: about per km; stations: part of the lift's running cost; sign, lodges, school and patrol:
 *     billed by the game; natural: nothing a month); the long hint (or BUILD_ACTIVE); the lift-sign line on
 *     stations and lines (Round F6 C4: never in the two lines, which would cut it at 130-150 %); runs:
 *     BUILD_TIP_LINK, BUILD_TIP_RESHAPE; natural: BUILD_TIP_NATURAL and the natural type factors
 *     (second pass); lodges: BUILD_TIP_LODGE (second pass: staff and utilities) and on the luxury tiers
 *     BUILD_TIP_LUXURY; school: BUILD_TIP_SCHOOL; patrol huts: BUILD_TIP_PATROL; the locked reason.
 *   - BuildStrip reads ONLY the CORE `build` binding (and its own local `pick$`), so it is also the
 *     contract-mismatch panel's footer (MISMATCH_FOOTER: no dot, idle NEXT[Done]; hub-u2-spec 4.5,
 *     deviation U11). Everything non-CORE comes in through `info` (FooterInfo), built by OkFooter (ok
 *     panel; a panel file for gate R11: resorts$, selectedResort$, resortLive$, problems$, follow$).
 *   - D36 notice (design 7.5): OkFooter remembers the `follow` value seen at mount; when C# moves it
 *     (an automatic area switch, selection rule 7), "Now showing <the area shown NOW>." holds line 1
 *     for FOLLOW_NOTICE_MS; a second follow restarts the timer; unmount clears it. No ordering
 *     assumption between the `follow` and `selectedResort` pushes. Nothing runs per frame.
 *   - FOCUS (Round F6, C1; field test 6 log-audit A1): every tile and every picker chip is a cs2/ui Button
 *     with focusKey={FOCUS_DISABLED}. The Panel wraps its footer in a FocusKeyOverride whose
 *     KeyFocusController hosts ONE child (installed index.js@684800 `Tp` around the footer div;
 *     `bp.registerChild` @578500 logs "Cannot register second focus key ... KeyFocusControllers can
 *     only host a single child"), and a Button registers itself by default (FOCUS_AUTO, button.tsx
 *     @629600 `Gg(e??(s?Ug:Bg))`). FOCUS_DISABLED is what the vanilla title bar's own close button
 *     passes (focusKey:Ug, index.js@686490). tools/check-bundle.js rule R15 FAILS a focusable cs2/ui
 *     Button rendered into a Panel header or footer without it (it follows OkFooter into BuildStrip).
 */
import { useEffect, useRef, useState } from "react";
import { bindLocalValue, useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { build$, follow$, hubTriggers, problems$, resortLive$, resorts$, selectedResort$ } from "../bindings";
import type { BuildItem, BuildItemId, RunKind } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry, Translate } from "../l10n";
import {
  BUILD_ITEMS, BUILD_ITEM_ICON, BUILD_RUN_KIND, BUILD_STEP, LUXURY_LODGES, buildMeta, groupBuildItems, stationRole,
} from "./buildItems";
import type { OrderedBuildItem } from "./buildItems";
import {
  NO_PICK, dotItem, dotTarget, effectiveSelection, footerState, line1Variant, liveOf, nextVariant, optionOf, pickerSets,
  visibleIds,
} from "./hubRules";
import type { DotTarget, NextVariant, PickerSelection, PickerSet } from "./hubRules";
import {
  ASSET_NAME, BUILD_SHORT_RUN, HUB_FMT, HUB_LOC, LIFT_TYPE, LODGE_TIER, NEXT_LINE1, NEXT_LINE2, RUN_TYPE, sentKeyEntry,
} from "./hubText";
import { AssetIcon, Glyph, LocText, Tip, TipLines, cx, numArg } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import styles from "./hub.module.scss";

/** What the footer needs beyond CORE `build`. */
export interface FooterInfo {
  /** The hub shows a ski area (upgrades go on its sign: F6 C4); false = Not linked / none / mismatch. */
  lodgeShown: boolean;
  /** The one dot (null = none). */
  dot: BuildItemId | null;
  /** Idle text. */
  next: NextVariant;
  /** D36 notice: the shown area's name while the 6 s notice runs, else null. */
  followName: string | null;
}

export const MISMATCH_FOOTER: FooterInfo = { lodgeShown: false, dot: null, next: "done", followName: null };

/** How long the D36 notice stays (design 7.5). */
export const FOLLOW_NOTICE_MS = 6000;

/** Contract 6: the pickers' choice for the UI session (local value: nothing sent to C#; a UI reload resets it). */
export const pick$ = bindLocalValue<PickerSelection>(NO_PICK);

const KIND_STRIPE = ["stripeGroomed", "stripeSnowmaking", "stripeNight"] as const;

/** The name entry of an item: RUN_TYPE for runs (D17), Assets.NAME for the others. */
function nameEntry(id: BuildItemId): LocEntry {
  const kind = BUILD_RUN_KIND[id];
  if (kind !== undefined) return RUN_TYPE[kind];
  return ASSET_NAME[id] ?? HUB_LOC.locked;
}

/** The C# kind of an item ("" for an id this bundle does not know: line1Variant then reads cost / name). */
function kindOf(id: BuildItemId): string {
  const m = buildMeta(id);
  return m ? m.kind : "";
}

/** Footer line 1 of a tile: ONE <LocText> per variant (R13), or the name alone. */
function Line1({ id, item, t }: { id: BuildItemId; item: BuildItem; t: Translate }): JSX.Element {
  const NAME = t(nameEntry(id));
  switch (line1Variant(kindOf(id), item)) {
    case "run": return <LocText entry={HUB_FMT.buildLineRun} args={{ NAME, FEE: numArg(item.price ?? -1, Unit.Money), PERHA: numArg(item.pricePerHa ?? -1, Unit.Money) }} />;
    case "lift": return <LocText entry={HUB_FMT.buildLineLift} args={{ NAME, PERKM: numArg(item.costPerKm ?? -1, Unit.Money) }} />;
    case "cost": return <LocText entry={HUB_FMT.buildLineCost} args={{ NAME, COST: numArg(item.cost ?? -1, Unit.Money) }} />;
    default: return <>{NAME}</>;
  }
}

/** The short line-2 hint of a tile (design 6.10; contract 6: by kind - every lift type's base / top / line reads the
 *  fixed-grip pair's hints, the school and patrol huts their own, all upgrades the "selected sign" hint without a
 *  shown area). */
function shortEntry(id: BuildItemId, lodgeShown: boolean): LocEntry {
  const kind = BUILD_RUN_KIND[id];
  if (kind !== undefined) return BUILD_SHORT_RUN[kind];
  switch (kindOf(id)) {
    case "sign": return HUB_LOC.buildHintSign;
    case "lodge": return lodgeShown ? HUB_LOC.buildShortLodge : HUB_LOC.buildHintLodgeSelected;
    case "school": return lodgeShown ? HUB_LOC.buildShortSchool : HUB_LOC.buildHintLodgeSelected;
    case "patrol": return lodgeShown ? HUB_LOC.buildShortPatrol : HUB_LOC.buildHintLodgeSelected;
    case "station": return stationRole(id) === "top" ? HUB_LOC.buildShortTop : HUB_LOC.buildHintBase;
    default: return HUB_LOC.buildShortLift;
  }
}

/** Line 2 text: locked reason > C#'s hint > the short hint. */
function line2Text(t: Translate, id: BuildItemId, item: BuildItem, lodgeShown: boolean): string {
  if (item.locked) return t(sentKeyEntry(item.reason, HUB_LOC.locked));
  if (item.hint) return t(sentKeyEntry(item.hint, shortEntry(id, lodgeShown)));
  return t(shortEntry(id, lodgeShown));
}

/** The long hint of a tile's tooltip (design 6.10). */
function longEntry(id: BuildItemId, lodgeShown: boolean): LocEntry {
  const kind = BUILD_RUN_KIND[id];
  if (kind !== undefined) return BUILD_SHORT_RUN[kind];
  switch (kindOf(id)) {
    case "sign": return HUB_LOC.buildHintSign;
    case "lodge": return lodgeShown ? HUB_LOC.buildHintLodge : HUB_LOC.buildHintLodgeSelected;
    case "school": return lodgeShown ? HUB_LOC.buildShortSchool : HUB_LOC.buildHintLodgeSelected;
    case "patrol": return lodgeShown ? HUB_LOC.buildShortPatrol : HUB_LOC.buildHintLodgeSelected;
    case "station": return stationRole(id) === "top" ? HUB_LOC.buildHintTop : HUB_LOC.buildHintBase;
    default: return HUB_LOC.buildHintLift;
  }
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
  if (kind === 3) return t(HUB_LOC.buildMonthNone);
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
  return t(HUB_LOC.buildMonthGame);
}

function tileTip(e: OrderedBuildItem, t: Translate, lodgeShown: boolean): JSX.Element {
  const run = BUILD_RUN_KIND[e.id] !== undefined;
  const k = kindOf(e.id);
  return (
    <TipLines
      lines={[
        t(nameEntry(e.id)),
        { label: t(HUB_LOC.buildTipOnce), value: onceValue(e.id, e.item) },
        { label: t(HUB_LOC.buildTipMonth), value: monthValue(e.id, e.item, t) },
        e.item.active ? t(HUB_LOC.buildActive) : t(longEntry(e.id, lodgeShown)),
        (k === "station" || k === "line") && t(HUB_LOC.buildHintLiftSign),
        run && t(HUB_LOC.buildTipLink),
        run && t(HUB_LOC.buildTipReshape),
        e.id === "runNatural" && t(HUB_LOC.buildTipNatural),
        e.id === "runNatural" && t(HUB_LOC.buildTipTypeNatural),   // second pass: the natural type factors
        k === "lodge" && t(HUB_LOC.buildTipLodge),                 // second pass: staff and utilities
        LUXURY_LODGES.has(e.id) && t(HUB_LOC.buildTipLuxury),      // contract 6: the luxury label and its price tolerance
        k === "school" && t(HUB_LOC.buildTipSchool),               // contract 6: R13
        k === "patrol" && t(HUB_LOC.buildTipPatrol),               // contract 6: R13
        e.item.locked && t(sentKeyEntry(e.item.reason, HUB_LOC.locked)),
      ]}
    />
  );
}

/** One picker chip's label: the lift type's word or the lodge tier's (an unknown option shows its raw id). */
function chipLabel(set: PickerSet, value: string, t: Translate): string {
  const e = set === "lift" ? LIFT_TYPE[value] : LODGE_TIER[value as BuildItemId];
  return e ? t(e) : value;
}

/** Contract 6: one segmented control (a set with >= 2 options). Chips are cs2/ui Buttons with FOCUS_DISABLED (R15). */
function PickerSetView({ set, options, chosen, dot, t, onPick }: {
  set: PickerSet; options: readonly string[]; chosen: string; dot: DotTarget; t: Translate;
  onPick: (set: PickerSet, value: string) => void;
}): JSX.Element {
  const tip = t(set === "lift" ? HUB_LOC.pickerTipLiftType : HUB_LOC.pickerTipLodgeTier);
  return (
    <div className={styles.pickerSet} style={{ flexGrow: options.length }}>
      {options.map((value, i) => {
        const label = chipLabel(set, value, t);
        const on = value === chosen;
        const dotHere = !!dot && dot.kind === "chip" && dot.set === set && dot.value === value;
        return (
          <div key={value} className={cx(styles.pickerCell, i > 0 && styles.pickerCellNext)}>
            <Tip tip={<TipLines lines={[label, tip]} />}>
              <Button
                focusKey={FOCUS_DISABLED}
                theme={{ button: cx(styles.pickerChip, on && styles.pickerChipOn) }}
                selected={on}
                onSelect={() => onPick(set, value)}
              >
                <div className={styles.pickerLabel}>{label}</div>
                {dotHere && <div className={styles.buildHintDot} />}
              </Button>
            </Tip>
          </div>
        );
      })}
    </div>
  );
}

export function BuildStrip({ info }: { info: FooterInfo }): JSX.Element {
  const t = useT();
  const items = useValue(build$);
  const sel = useValue(pick$);
  const [hovered, setHovered] = useState<BuildItemId | null>(null);
  const [lockedClick, setLockedClick] = useState<BuildItemId | null>(null);

  // Contract 6 pickers: options from what C# sent, the choice in force, the drawn tiles.
  const sets = pickerSets(items, BUILD_ITEMS);
  const eff = effectiveSelection(sets, sel);
  const visible = visibleIds(items, BUILD_ITEMS, sel);
  const all = groupBuildItems(items);
  const groups = all.map((g) => g.filter((e) => visible.has(e.id))).filter((g) => g.length > 0);
  const dot = dotTarget(info.dot, items, BUILD_ITEMS, sel);

  let hoveredEntry: OrderedBuildItem | null = null;
  let activeEntry: OrderedBuildItem | null = null;
  let lockedEntry: OrderedBuildItem | null = null;
  // The active item is looked up among ALL sent items: a lodge tier placed from the sign's own upgrade menu may not be
  // the chosen one yet (the effect below chooses it).
  for (const g of all) {
    for (const e of g) {
      if (e.item.active && !activeEntry) activeEntry = e;
      if (!visible.has(e.id)) continue;
      if (e.id === hovered) hoveredEntry = e;
      if (e.id === lockedClick && e.item.locked) lockedEntry = e;
    }
  }
  const activeId = activeEntry ? activeEntry.id : null;

  // Contract 6: when an item becomes the active tool, its option is chosen (an item of another tier or type).
  useEffect(() => {
    const opt = optionOf(activeId, BUILD_ITEMS);
    if (!opt) return;
    const cur = pick$.value ?? NO_PICK;
    if (opt.set === "lodge" && cur.lodge !== opt.value) pick$.update({ lodge: opt.value, lift: cur.lift });
    else if (opt.set === "lift" && cur.lift !== opt.value) pick$.update({ lodge: cur.lodge, lift: opt.value });
  }, [activeId]);

  const state = footerState(groups.length > 0, !!hoveredEntry, info.followName !== null, !!activeEntry, !!lockedEntry);

  let line1: JSX.Element;
  let line2: string;
  let line1Cls = styles.buildLineName;
  let line2Cls = styles.buildLineDim;
  if (state === "empty") {
    line1 = <>{t(HUB_LOC.buildEmpty)}</>; line2 = ""; line1Cls = styles.buildLineNormal;
  } else if (state === "hover" && hoveredEntry) {
    line1 = <Line1 id={hoveredEntry.id} item={hoveredEntry.item} t={t} />;
    line2 = line2Text(t, hoveredEntry.id, hoveredEntry.item, info.lodgeShown);
    if (hoveredEntry.item.locked) line2Cls = styles.buildLineLocked;
  } else if (state === "follow") {
    line1 = <LocText entry={HUB_FMT.follow} args={{ NAME: info.followName ?? "" }} />; line2 = ""; line1Cls = styles.buildLineNormal;
  } else if (state === "active" && activeEntry) {
    line1 = <Line1 id={activeEntry.id} item={activeEntry.item} t={t} />; line2 = t(HUB_LOC.buildActive);
  } else if (state === "locked" && lockedEntry) {
    line1 = <Line1 id={lockedEntry.id} item={lockedEntry.item} t={t} />;
    line2 = t(sentKeyEntry(lockedEntry.item.reason, HUB_LOC.locked)); line2Cls = styles.buildLineLocked;
  } else {
    line1 = <>{t(NEXT_LINE1[info.next])}</>; line2 = t(NEXT_LINE2[info.next]); line1Cls = styles.buildLineNormal;
  }

  const click = (e: OrderedBuildItem): void => {
    setLockedClick(e.item.locked ? e.id : null);
    hubTriggers.launch(e.id); // also when locked: C# refuses and logs it
  };
  const onPick = (set: PickerSet, value: string): void => {
    const cur = pick$.value ?? NO_PICK;
    pick$.update(set === "lodge" ? { lodge: value, lift: cur.lift } : { lodge: cur.lodge, lift: value });
  };

  const pickLodge = sets.lodge.length >= 2;
  const pickLift = sets.lift.length >= 2;

  return (
    <div className={styles.buildFooter}>
      {(pickLodge || pickLift) && (
        <div className={styles.pickerRow}>
          {pickLodge && <PickerSetView set="lodge" options={sets.lodge} chosen={eff.lodge} dot={dot} t={t} onPick={onPick} />}
          {pickLodge && pickLift && <div className={styles.pickerGap} />}
          {pickLift && <PickerSetView set="lift" options={sets.lift} chosen={eff.lift} dot={dot} t={t} onPick={onPick} />}
        </div>
      )}
      <div className={styles.buildRow}>
        {groups.map((g, gi) => (
          <div key={gi} className={cx(styles.buildGroup, gi > 0 && styles.buildGroupNext)} style={{ flexGrow: g.length }}>
            {g.map((e) => {
              const kind = BUILD_RUN_KIND[e.id];
              const step = BUILD_STEP[e.id];
              return (
                <div key={e.id} className={styles.buildCell}
                  onMouseEnter={() => setHovered(e.id)}
                  onMouseLeave={() => setHovered((h) => (h === e.id ? null : h))}>
                  <Tip tip={tileTip(e, t, info.lodgeShown)}>
                    <Button
                      focusKey={FOCUS_DISABLED}
                      theme={{ button: cx(styles.buildTile, e.item.active && styles.buildTileActive, e.item.locked && styles.buildTileLocked) }}
                      selected={e.item.active}
                      onSelect={() => click(e)}
                    >
                      <AssetIcon src={BUILD_ITEM_ICON[e.id]} className={styles.buildIcon} />
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
      <div className={cx(styles.buildStatusLine, line1Cls)}>{line1}</div>
      <div className={cx(styles.buildStatusLine, line2Cls)}>{line2}</div>
    </div>
  );
}

/** The ok panel's footer: computes FooterInfo from the non-CORE bindings (panel file, R11). */
export function OkFooter(): JSX.Element {
  const t = useT();
  const resorts = useValue(resorts$);
  const selected = useValue(selectedResort$);
  const live = useValue(resortLive$);
  const problems = useValue(problems$);
  const follow = useValue(follow$);
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
    lodgeShown: !!shown && !unlinked,
    dot: dotItem(problems, sign, hasSign),
    next: nextVariant(hasSign, unlinked, l),
    followName: noticeOn && shown ? (unlinked ? t(HUB_LOC.unlinkedEntry) : shown.name) : null,
  };
  return <BuildStrip info={info} />;
}
