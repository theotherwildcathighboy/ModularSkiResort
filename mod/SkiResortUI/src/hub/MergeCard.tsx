/*
 * src/hub/MergeCard.tsx - the merge card beside the game's selected-info panel (contract 35), for a merged building or one that could be
 * merged. Always mounted at the "Game" hook like the Ski Bus card, so it reads only mergeCard$, never a panel binding. The tier chips pick a
 * tier the lot allows; the button fires mergeBuilding, C# charges at once and says the outcome on the next push. Its fees, states and
 * refusals are C#'s numbers (SkiMergeSystem, MergeRules); the card decides nothing.
 * Engine notes: EngineNotes/SkiMergeSystem.md
 */
import { useEffect, useState } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { Unit } from "cs2/l10n";
import { hubTriggers, mergeCard$ } from "../bindings";
import type { BuildItemId, MergeCardState } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry } from "../l10n";
import styles from "./hub.module.scss";
import { HUB_LOC, LODGE_TIER } from "./hubText";
import { InfoLine, Num, Tip, cx } from "./parts";

/** SkiCatalog.Lodges order: tier index -> its build item id (the LODGE_TIER word). */
export const MERGE_TIER_ITEM: readonly BuildItemId[] = ["lodge", "lodgeHut", "lodgeMid", "lodgeSummit", "lodgeGrand"];

/** MergeRules.State -> its word. */
export const MERGE_STATE: readonly LocEntry[] = [
  HUB_LOC.mergeStateCounted, HUB_LOC.mergeStateNoArea, HUB_LOC.mergeStateTooFar, HUB_LOC.mergeStateAssetMissing,
  HUB_LOC.mergeStateInactive, HUB_LOC.mergeStateDestroyed, HUB_LOC.mergeStateUnknown,
];

const REFUSAL_TOO_SMALL = 9;
const OUTCOME_NO_MONEY = 5;

function allowedTiers(card: MergeCardState): number[] {
  const out: number[] = [];
  for (let t = 0; t < MERGE_TIER_ITEM.length; t++) if ((card.allowedMask & (1 << t)) !== 0) out.push(t);
  return out;
}

export function MergeCard(): JSX.Element | null {
  const t = useT();
  const card = useValue(mergeCard$);
  const [pick, setPick] = useState(-1);
  const key = card.shown ? `${card.building.index}:${card.building.version}:${card.tier}` : "";
  useEffect(() => setPick(-1), [key]);   // a new building or a new tier drops the local pick
  if (!card.shown) return null;
  const tiers = allowedTiers(card);
  const merged = card.tier >= 0;
  const chosen = pick >= 0 ? pick : merged ? card.tier : tiers.length > 0 ? tiers[tiers.length - 1] : -1;
  const state = merged ? MERGE_STATE[card.state] : undefined;
  const tooSmall = card.refusal === REFUSAL_TOO_SMALL || tiers.length === 0;
  const charge = chosen >= 0 ? card.charge[chosen] ?? 0 : 0;
  const monthly = chosen >= 0 ? card.monthly[chosen] ?? 0 : 0;
  const canApply = chosen >= 0 && chosen !== card.tier && !tooSmall;
  return (
    <div className={cx(styles.busCard, styles.mergeCard)}>
      <Tip tip={t(HUB_LOC.mergeCardTip)}>
        <div className={styles.busCardTitle}>{t(HUB_LOC.mergeCardTitle)}</div>
      </Tip>
      {card.area !== "" && <InfoLine label={t(HUB_LOC.resortCaption)} value={card.area} />}
      {state && <InfoLine label={t(HUB_LOC.mergeCardStatus)} value={t(state)} />}
      {tooSmall && !merged && <InfoLine label={t(HUB_LOC.mergeCardStatus)} value={t(HUB_LOC.mergeCardTooSmall)} />}
      {tiers.length > 0 && (
        <>
          <InfoLine label={t(HUB_LOC.mergeCardTier)} />
          <div className={styles.mergeTiers}>
            {tiers.map((tier) => {
              const entry = LODGE_TIER[MERGE_TIER_ITEM[tier]];
              const on = tier === chosen;
              return (
                <div key={tier} className={styles.mergeTierCell}>
                  <Button focusKey={FOCUS_DISABLED} theme={{ button: cx(styles.pickerChip, on && styles.pickerChipOn) }} selected={on}
                    onSelect={() => setPick(tier)}>
                    <div className={styles.pickerLabel}>{entry ? t(entry) : String(tier)}</div>
                  </Button>
                </div>
              );
            })}
          </div>
          <InfoLine label={t(HUB_LOC.mergeCardOnce)} value={<Num value={charge} unit={Unit.Money} />} />
          <InfoLine label={t(HUB_LOC.mergeCardMonthly)} value={<Num value={monthly} unit={Unit.MoneyPerMonth} />} />
        </>
      )}
      {card.outcome === OUTCOME_NO_MONEY && <InfoLine label={t(HUB_LOC.mergeCardStatus)} value={t(HUB_LOC.mergeCardNoMoney)} />}
      <div className={styles.mergeActions}>
        {canApply && (
          <Button focusKey={FOCUS_DISABLED} theme={{ button: styles.pickerChip }} onSelect={() => hubTriggers.mergeBuilding(card.building, chosen)}>
            <div className={styles.pickerLabel}>{t(merged ? HUB_LOC.mergeCardChange : HUB_LOC.mergeCardMerge)}</div>
          </Button>
        )}
        {merged && (
          <Tip tip={t(HUB_LOC.mergeCardUnmergeTip)}>
            <Button focusKey={FOCUS_DISABLED} theme={{ button: styles.pickerChip }} onSelect={() => hubTriggers.unmergeBuilding(card.building)}>
              <div className={styles.pickerLabel}>{t(HUB_LOC.mergeCardUnmerge)}</div>
            </Button>
          </Tip>
        )}
      </div>
    </div>
  );
}
