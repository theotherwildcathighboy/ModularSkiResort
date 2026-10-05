/*
 * src/hub/BuildStrip.tsx - the Build strip: the panel's FOOTER, nine icon tiles in build order plus
 * two fixed one-string status lines and the one dot.
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-C: a 2-column tile
 * grid in the scroll area); rewritten 2026-10-03 (Round U2, implementer I-3) for
 * docs/research/hub-u2-spec.md section 4.2 ("footer -> BuildStrip"), 4.4 J2 / J4, 5.8; rewritten
 * 2026-10-04 for contract 3 (Round E1, stream S6; docs/research/econ-round/spec/ch6-hub-ui.md
 * section 6.13, design hub-v3-design.md 6.10 / 7.4 / 7.5, D17 / D36 / D37; code from the checked
 * reference spec/ch6-hub-ui-ref/src/hub/BuildStrip.tsx).
 * Why a footer: cs2/ui Panel renders `footer` in its own div OUTSIDE the content Scrollable
 * (index.js@684600: header / content / footer divs, the content div holding the Scrollable,
 * @733600), so the strip never scrolls away and its tiles never move under the mouse.
 * Contract:
 *   - Groups [sign, lodge] | [baseStation "1", topStation "2", lift "3"] | [runNatural, runGroomed,
 *     runSnowmaking, runNight] (src/hub/buildItems.ts). ONE nowrap row; tiles `flex: 1 1 0;
 *     min-width: 0`, each group's flex-grow = its tile count (2 / 3 / 4), so all tiles are equal at
 *     any panel width and text scale (J4: no text in tiles, so text growth cannot reflow them).
 *   - Icon-only tiles: the prefab icon; a step badge on the lift tiles; a kind stripe on the run
 *     tiles (natural: two short bars, the dashed outline's echo; groomed white, snowmaking cyan
 *     outline, night amber: the run list's colours); the ONE amber dot (D37, design 7.4) on the tile
 *     FooterInfo.dot names (hubRules.dotItem: "sign" with no sign, else the buildItem of the shown
 *     area's first band row) - never from build[].hint any more.
 *   - Locked: dimmed + Lock glyph, NOT `disabled` (whether a disabled <button> gets mouse events in
 *     Gameface is unverified, hub-u2-spec 7.2 item 10). A click shows the reason in line 2 AND still
 *     sends launch(id): C# refuses it and logs "refused - locked" (SkiResortHubUISystem ApplyLaunch),
 *     so the click is never silent. Active: highlighted; clicking it again returns to the default tool.
 *   - TWO fixed lines (`div.buildStatusLine` x 2, each ONE text node, 1.3 em of XS, nowrap + ellipsis:
 *     D37, J2 - a FIXED height, never min-height, or the footer would grow and move the tiles under
 *     the mouse). Priority (hubRules.footerState): empty > hovered tile > the D36 "Now showing" notice
 *     > the active tile > the last locked click > idle (NEXT / NEXT2 by ResortLive.nextStep). Line 1
 *     of a tile is ONE <LocText> by hubRules.line1Variant (name · fee + per ha; name · about per km;
 *     name · cost; else the name alone - a contract-2 DLL sends no new field). Line 2: the locked
 *     reason, else C#'s hint key (lift: HINT[LiftNoPair] for the SHOWN area), else the short hint.
 *     Item names: RUN_TYPE for runs (D17, sentence case), Assets.NAME for the others (deviation U5).
 *   - Tile tooltip: name; "One-time" and "Each month" label / value rows (run: base fee + per ha;
 *     lift: about per km; stations: part of the lift's running cost; sign and lodge: billed by the
 *     game; natural: nothing a month); the long hint (or BUILD_ACTIVE); the lift-sign line on base,
 *     top and lift (Round F6 C4: never in the two lines, which would cut it at 130-150 %); runs:
 *     BUILD_TIP_LINK, BUILD_TIP_RESHAPE; natural: BUILD_TIP_NATURAL and the natural type factors
 *     (second pass); lodge: BUILD_TIP_LODGE (second pass: staff and utilities); the locked reason.
 *   - BuildStrip reads ONLY the CORE `build` binding, so it is also the contract-mismatch panel's
 *     footer (MISMATCH_FOOTER: no dot, idle NEXT[Done]; hub-u2-spec 4.5, deviation U11). Everything
 *     non-CORE comes in through `info` (FooterInfo), built by OkFooter (ok panel; a panel file for
 *     gate R11: resorts$, selectedResort$, resortLive$, problems$, follow$).
 *   - D36 notice (design 7.5): OkFooter remembers the `follow` value seen at mount; when C# moves it
 *     (an automatic area switch, selection rule 7), "Now showing <the area shown NOW>." holds line 1
 *     for FOLLOW_NOTICE_MS; a second follow restarts the timer; unmount clears it. No ordering
 *     assumption between the `follow` and `selectedResort` pushes. Nothing runs per frame.
 *   - FOCUS (Round F6, C1; field test 6 log-audit A1): every tile is a cs2/ui Button with
 *     focusKey={FOCUS_DISABLED}. The Panel wraps its footer in a FocusKeyOverride whose
 *     KeyFocusController hosts ONE child (installed index.js@684800 `Tp` around the footer div;
 *     `bp.registerChild` @578500 logs "Cannot register second focus key ... KeyFocusControllers can
 *     only host a single child"), and a Button registers itself by default (FOCUS_AUTO, button.tsx
 *     @629600 `Gg(e??(s?Ug:Bg))`). FOCUS_DISABLED is what the vanilla title bar's own close button
 *     passes (focusKey:Ug, index.js@686490). tools/check-bundle.js rule R15 FAILS a focusable cs2/ui
 *     Button rendered into a Panel header or footer without it (it follows OkFooter into BuildStrip).
 */
import { useEffect, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { build$, follow$, hubTriggers, problems$, resortLive$, resorts$, selectedResort$ } from "../bindings";
import type { BuildItem, BuildItemId, RunKind } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry, Translate } from "../l10n";
import { BUILD_ITEM_ICON, BUILD_RUN_KIND, BUILD_STEP, groupBuildItems } from "./buildItems";
import type { OrderedBuildItem } from "./buildItems";
import { dotItem, footerState, line1Variant, liveOf, nextVariant } from "./hubRules";
import type { NextVariant } from "./hubRules";
import {
  ASSET_NAME, BUILD_SHORT_RUN, HUB_FMT, HUB_LOC, NEXT_LINE1, NEXT_LINE2, RUN_TYPE, sentKeyEntry,
} from "./hubText";
import { AssetIcon, Glyph, LocText, Tip, TipLines, cx, numArg } from "./parts";
import { currentResort, resortEntries } from "./resortData";
import styles from "./hub.module.scss";

/** What the footer needs beyond CORE `build`. */
export interface FooterInfo {
  /** The hub shows a ski area (lodge goes on its sign: F6 C4); false = Not linked / none / mismatch. */
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

const KIND_STRIPE = ["stripeGroomed", "stripeSnowmaking", "stripeNight"] as const;

/** The name entry of an item: RUN_TYPE for runs (D17), Assets.NAME for the others. */
function nameEntry(id: BuildItemId): LocEntry {
  const kind = BUILD_RUN_KIND[id];
  if (kind !== undefined) return RUN_TYPE[kind];
  return ASSET_NAME[id] ?? HUB_LOC.locked;
}

/** Footer line 1 of a tile: ONE <LocText> per variant (R13), or the name alone. */
function Line1({ id, item, t }: { id: BuildItemId; item: BuildItem; t: Translate }): JSX.Element {
  const NAME = t(nameEntry(id));
  switch (line1Variant(id, item)) {
    case "run": return <LocText entry={HUB_FMT.buildLineRun} args={{ NAME, FEE: numArg(item.price ?? -1, Unit.Money), PERHA: numArg(item.pricePerHa ?? -1, Unit.Money) }} />;
    case "lift": return <LocText entry={HUB_FMT.buildLineLift} args={{ NAME, PERKM: numArg(item.costPerKm ?? -1, Unit.Money) }} />;
    case "cost": return <LocText entry={HUB_FMT.buildLineCost} args={{ NAME, COST: numArg(item.cost ?? -1, Unit.Money) }} />;
    default: return <>{NAME}</>;
  }
}

/** The short line-2 hint of a tile (design 6.10). */
function shortEntry(id: BuildItemId, lodgeShown: boolean): LocEntry {
  const kind = BUILD_RUN_KIND[id];
  if (kind !== undefined) return BUILD_SHORT_RUN[kind];
  switch (id) {
    case "sign": return HUB_LOC.buildHintSign;
    case "lodge": return lodgeShown ? HUB_LOC.buildShortLodge : HUB_LOC.buildHintLodgeSelected;
    case "baseStation": return HUB_LOC.buildHintBase;
    case "topStation": return HUB_LOC.buildShortTop;
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
  switch (id) {
    case "sign": return HUB_LOC.buildHintSign;
    case "lodge": return lodgeShown ? HUB_LOC.buildHintLodge : HUB_LOC.buildHintLodgeSelected;
    case "baseStation": return HUB_LOC.buildHintBase;
    case "topStation": return HUB_LOC.buildHintTop;
    default: return HUB_LOC.buildHintLift;
  }
}

function onceValue(id: BuildItemId, item: BuildItem): JSX.Element | string {
  switch (line1Variant(id, item)) {
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
  if (id === "lift") {
    return typeof item.monthly === "number" && item.monthly >= 0 && typeof item.monthlyPerKm === "number" && item.monthlyPerKm >= 0
      ? <LocText entry={HUB_FMT.buildMonthLift} args={{ BASE: numArg(item.monthly, Unit.Money), PERKM: numArg(item.monthlyPerKm, Unit.Money) }} /> : "–";
  }
  if (id === "baseStation" || id === "topStation") return t(HUB_LOC.buildMonthStation);
  return t(HUB_LOC.buildMonthGame);
}

function tileTip(e: OrderedBuildItem, t: Translate, lodgeShown: boolean): JSX.Element {
  const run = BUILD_RUN_KIND[e.id] !== undefined;
  return (
    <TipLines
      lines={[
        t(nameEntry(e.id)),
        { label: t(HUB_LOC.buildTipOnce), value: onceValue(e.id, e.item) },
        { label: t(HUB_LOC.buildTipMonth), value: monthValue(e.id, e.item, t) },
        e.item.active ? t(HUB_LOC.buildActive) : t(longEntry(e.id, lodgeShown)),
        (e.id === "baseStation" || e.id === "topStation" || e.id === "lift") && t(HUB_LOC.buildHintLiftSign),
        run && t(HUB_LOC.buildTipLink),
        run && t(HUB_LOC.buildTipReshape),
        e.id === "runNatural" && t(HUB_LOC.buildTipNatural),
        e.id === "runNatural" && t(HUB_LOC.buildTipTypeNatural),   // second pass: the natural type factors
        e.id === "lodge" && t(HUB_LOC.buildTipLodge),              // second pass: staff and utilities
        e.item.locked && t(sentKeyEntry(e.item.reason, HUB_LOC.locked)),
      ]}
    />
  );
}

export function BuildStrip({ info }: { info: FooterInfo }): JSX.Element {
  const t = useT();
  const items = useValue(build$);
  const groups = groupBuildItems(items);
  const [hovered, setHovered] = useState<BuildItemId | null>(null);
  const [lockedClick, setLockedClick] = useState<BuildItemId | null>(null);

  let hoveredEntry: OrderedBuildItem | null = null;
  let activeEntry: OrderedBuildItem | null = null;
  let lockedEntry: OrderedBuildItem | null = null;
  for (const g of groups) {
    for (const e of g) {
      if (e.id === hovered) hoveredEntry = e;
      if (e.item.active && !activeEntry) activeEntry = e;
      if (e.id === lockedClick && e.item.locked) lockedEntry = e;
    }
  }
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

  return (
    <div className={styles.buildFooter}>
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
                      {info.dot === e.id && !e.item.locked && <div className={styles.buildHintDot} />}
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
