/*
 * src/hub/CatTrackControls.tsx - the Runs tab's Delete cat tracks chip (contract 19, field test 2026-10-26b) and Ski way chip (contract 44).
 * BuildStrip draws it beside the Quick runs chip, on the ok panel only (it reads the non-CORE catTrackDeleteMode binding).
 * Turning the chip on opens the cat track tool in its Delete mode. C# answers through catTrackDeleteMode$, which follows the tool:
 * closing it or picking another tool shows it off. Turning the chip off leaves the mode.
 * The chip is always drawn, so nothing moves under the mouse.
 * It is a Button with focusKey={FOCUS_DISABLED} (rule R15).
 */
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { catTrackDeleteMode$, catTrackSkiWay$, hubTriggers } from "../bindings";
import type { Translate } from "../l10n";
import { HUB_LOC } from "./hubText";
import { Tip, cx } from "./parts";
import styles from "./hub.module.scss";

export function CatTrackDeleteChip({ t }: { t: Translate }): JSX.Element {
  const on = !!useValue(catTrackDeleteMode$);
  return (
    <div className={cx(styles.pickerCell, styles.pickerCellNext)}>
      <Tip tip={on ? null : t(HUB_LOC.catTrackDeleteTip)}>
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

/**
 * The Runs tab's Ski way chip (contract 44, T194), beside Delete cat tracks. It shows the cat track tool's profile (catTrackSkiWay$,
 * C# owns it: it stays on across closing the tool until switched off or the next load). On switches to the Ski way and opens the tool
 * out of Delete mode. Off goes back to the standard track and leaves the tool as it is. Always drawn; focusKey={FOCUS_DISABLED} (R15).
 */
export function CatTrackSkiWayChip({ t }: { t: Translate }): JSX.Element {
  const on = !!useValue(catTrackSkiWay$);
  return (
    <div className={cx(styles.pickerCell, styles.pickerCellNext)}>
      <Tip tip={t(HUB_LOC.catTrackSkiWayTip)}>
        <Button
          focusKey={FOCUS_DISABLED}
          theme={{ button: cx(styles.pickerChip, on && styles.pickerChipOn) }}
          selected={on}
          onSelect={() => hubTriggers.setCatTrackSkiWay(!on)}
        >
          <div className={styles.pickerLabel}>{t(HUB_LOC.catTrackSkiWay)}</div>
        </Button>
      </Tip>
    </div>
  );
}
