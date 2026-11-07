/*
 * src/hub/MergedFold.tsx - the Overview's "Merged buildings" fold (contract 35): one row per merged building of the shown area (or, in the
 * Not linked view, those in no area): its name, then its tier word while counted or its state word otherwise; the tier, the state and the
 * monthly fee while billed are in the row's tooltip. A click focuses the building (focusBuilding). The name is the building's own (a
 * player may have typed it, 64 characters): it shrinks with an ellipsis under lodgeNameLabel like the Guests view's lodge names.
 * Contract 36: the fold opens on the merge overlay chip (armMergeOverlay; C# answers through mergeOverlay$, and Escape, a merge or a load
 * turn it off). Contract 37: the green / gray legend sits beside the chip (MergeLegend, also the Buildings tab footer's line while the
 * overlay is on), with the outlined counts while on; the Buildings tab's Merge tile fires the same trigger. In an area view the fold is always drawn (the chip needs it, the city
 * has a sign); in the Not linked view only while it has rows. Panel file: subscribes mergedLodges$ and mergeOverlay$ (no Demand of its own).
 * Engine notes: EngineNotes/SkiMergeSystem.md
 */
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { hubTriggers, isNullEntity, mergeOverlay$, mergedLodges$, sameEntity } from "../bindings";
import type { MergeOverlayState } from "../bindings";
import type { Entity } from "cs2/utils";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import styles from "./hub.module.scss";
import { Fold } from "./fit";
import { HUB_LOC, LODGE_TIER } from "./hubText";
import { MERGE_STATE, MERGE_TIER_ITEM } from "./MergeCard";
import { Num, Tip, TipLines, cx, numArg } from "./parts";

const STATE_COUNTED = 0;

export function MergedFold({ sign }: { sign: Entity | null }): JSX.Element | null {
  const t = useT();
  const all = useValue(mergedLodges$);
  const rows = (all ?? []).filter((r) => (sign ? sameEntity(r.sign, sign) : isNullEntity(r.sign)));
  if (rows.length === 0 && !sign) return null;
  return (
    <Fold view="overview" id="merged" header label={t(HUB_LOC.mergedFold)} value={numArg(rows.length, Unit.Integer)} tip={t(HUB_LOC.mergedFoldTip)}>
      <OverlayChip t={t} />
      {rows.map((r) => {
        const tierEntry = LODGE_TIER[MERGE_TIER_ITEM[r.tier]];
        const tier = tierEntry ? t(tierEntry) : "";
        const stateEntry = MERGE_STATE[r.state];
        const state = stateEntry ? t(stateEntry) : "";
        const counted = r.state === STATE_COUNTED;
        return (
          <Tip key={`${r.building.index}:${r.building.version}`} tip={<TipLines lines={[
            r.name,
            { label: t(HUB_LOC.mergeCardTier), value: tier },
            { label: t(HUB_LOC.mergeCardStatus), value: state },
            { label: t(HUB_LOC.mergeCardMonthly), value: <Num value={r.monthly} unit={Unit.MoneyPerMonth} /> },
          ]} />}>
            <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.infoLine, styles.foldRow, styles.lodgeRow) }}
              onSelect={() => hubTriggers.focusBuilding(r.building)}>
              <div className={styles.lodgeNameLabel}>{r.name}</div>
              <div className={cx(styles.infoValue, !counted && styles.flagTextDim)}>{counted ? tier : state}</div>
            </Button>
          </Tip>
        );
      })}
    </Fold>
  );
}

/** The overlay's legend: a green and a gray swatch with their words, each with its count of outlined buildings while the overlay is on.
 *  Host divs only (no Tip inside), so it can sit in a fold row or in the Build strip's status line. */
export function MergeLegend({ overlay, t }: { overlay: MergeOverlayState | null | undefined; t: Translate }): JSX.Element {
  const on = !!overlay?.armed;
  const item = (swatch: string, n: number, label: string, next: boolean): JSX.Element => (
    <div className={cx(styles.mergeLegendItem, next && styles.mergeLegendItemNext)}>
      <div className={cx(styles.mergeSwatch, swatch)} />
      {on && <div className={styles.mergeLegendCount}><Num value={n} unit={Unit.Integer} /></div>}
      <div className={styles.mergeLegendLabel}>{label}</div>
    </div>
  );
  return (
    <div className={styles.mergeLegend}>
      {item(styles.mergeSwatchFits, overlay?.allowed ?? 0, t(HUB_LOC.mergeLegendFits), false)}
      {item(styles.mergeSwatchTooSmall, overlay?.tooSmall ?? 0, t(HUB_LOC.mergeLegendTooSmall), true)}
    </div>
  );
}

/** The merge overlay chip and its legend: on arms C#'s SkiMergeOverlaySystem, off disarms it. Always drawn while the fold is open. */
function OverlayChip({ t }: { t: Translate }): JSX.Element {
  const o = useValue(mergeOverlay$);
  const on = !!o?.armed;
  return (
    <div className={styles.mergeOverlayRow}>
      <div className={styles.pickerCell}>
        <Tip tip={<TipLines lines={[t(HUB_LOC.mergeOverlay), t(HUB_LOC.mergeOverlayTip)]} />}>
          <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.pickerChip, on && styles.pickerChipOn) }} selected={on}
            onSelect={() => hubTriggers.armMergeOverlay(!on)}>
            <div className={styles.pickerLabel}>{t(HUB_LOC.mergeOverlay)}</div>
          </Button>
        </Tip>
      </div>
      <MergeLegend overlay={o} t={t} />
    </div>
  );
}
