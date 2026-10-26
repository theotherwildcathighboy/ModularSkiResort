/*
 * src/hub/CatTrackControls.tsx - the Runs tab's Delete cat tracks chip (contract 19, field test 2026-10-26b), drawn by BuildStrip beside
 * the Quick runs chip on the ok panel only (it reads the non-CORE catTrackDeleteMode binding). On opens the cat track tool in its
 * Delete mode (C# answers through catTrackDeleteMode$, which follows the tool: closing it or picking another tool shows it off); off
 * leaves the mode. Always drawn, so nothing moves under the mouse. Button with focusKey={FOCUS_DISABLED} (rule R15).
 */
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { catTrackDeleteMode$, hubTriggers } from "../bindings";
import type { Translate } from "../l10n";
import { HUB_LOC } from "./hubText";
import { Tip, TipLines, cx } from "./parts";
import styles from "./hub.module.scss";

export function CatTrackDeleteChip({ t }: { t: Translate }): JSX.Element {
  const on = !!useValue(catTrackDeleteMode$);
  return (
    <div className={cx(styles.pickerCell, styles.pickerCellNext)}>
      <Tip tip={<TipLines lines={[t(HUB_LOC.catTrackDelete), t(on ? HUB_LOC.catTrackDeleteTipOn : HUB_LOC.catTrackDeleteTip)]} />}>
        <Button
          focusKey={FOCUS_DISABLED}
          theme={{ button: cx(styles.pickerChip, on && styles.pickerChipOn) }}
          selected={on}
          onSelect={() => hubTriggers.setCatTrackDeleteMode(!on)}
        >
          <div className={styles.pickerLabel}>{t(HUB_LOC.catTrackDelete)}</div>
        </Button>
      </Tip>
    </div>
  );
}
