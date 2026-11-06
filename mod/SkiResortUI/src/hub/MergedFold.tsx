/*
 * src/hub/MergedFold.tsx - the Overview's "Merged buildings" fold (contract 35): one row per merged building of the shown area (or, in the
 * Not linked view, those in no area): its name, then its tier word while counted or its state word otherwise; the tier, the state and the
 * monthly fee while billed are in the row's tooltip. A click focuses the building (focusBuilding). The name is the building's own (a
 * player may have typed it, 64 characters): it shrinks with an ellipsis under lodgeNameLabel like the Guests view's lodge names.
 * Contract 36: the fold opens on the merge overlay chip (armMergeOverlay; C# answers through mergeOverlay$, and Escape, a merge or a load
 * turn it off), its tooltip counting the outlined buildings while on. In an area view the fold is always drawn (the chip needs it, the city
 * has a sign); in the Not linked view only while it has rows. Panel file: subscribes mergedLodges$ and mergeOverlay$ (no Demand of its own).
 * Engine notes: EngineNotes/SkiMergeSystem.md
 */
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { hubTriggers, isNullEntity, mergeOverlay$, mergedLodges$, sameEntity } from "../bindings";
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

/** The merge overlay chip: on arms C#'s SkiMergeOverlaySystem, off disarms it. Always drawn while the fold is open. */
function OverlayChip({ t }: { t: Translate }): JSX.Element {
  const o = useValue(mergeOverlay$);
  const on = !!o?.armed;
  return (
    <div className={styles.mergeOverlayRow}>
      <div className={styles.pickerCell}>
        <Tip tip={<TipLines lines={[
          t(HUB_LOC.mergeOverlayTip),
          on && { label: t(HUB_LOC.mergeOverlayAllowed), value: <Num value={o.allowed} unit={Unit.Integer} /> },
          on && { label: t(HUB_LOC.mergeOverlayTooSmall), value: <Num value={o.tooSmall} unit={Unit.Integer} /> },
        ]} />}>
          <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.pickerChip, on && styles.pickerChipOn) }} selected={on}
            onSelect={() => hubTriggers.armMergeOverlay(!on)}>
            <div className={styles.pickerLabel}>{t(HUB_LOC.mergeOverlay)}</div>
          </Button>
        </Tip>
      </div>
    </div>
  );
}
