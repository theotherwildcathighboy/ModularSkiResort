/*
 * Start card: the panel content while the city has no live Welcome Sign, even when unlinked runs or
 * lifts exist. Five numbered steps in fixed-height rows; step 1 holds the "Place Welcome Sign" button.
 * A locked sign still launches (C# refuses and logs it); its lock reason follows the steps.
 * Panel file: subscribes build$ (CORE).
 */
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { build$, hubTriggers } from "../bindings";
import { useT } from "../l10n";
import type { LocEntry } from "../l10n";
import { findBuildItem } from "./buildItems";
import { HUB_LOC, sentKeyEntry } from "./hubText";
import { InfoLine, cx } from "./parts";
import styles from "./hub.module.scss";

const STEPS: readonly LocEntry[] = [
  HUB_LOC.startStep1, HUB_LOC.startStep2, HUB_LOC.startStep3, HUB_LOC.startStep4,
];

export function StartCard(): JSX.Element {
  const t = useT();
  const build = useValue(build$);
  const sign = findBuildItem(build, "sign");
  return (
    <div className={styles.startCard}>
      <InfoLine header label={t(HUB_LOC.startTitle)} />
      {STEPS.map((step, i) => (
        <div key={i} className={styles.startStep}>
          <div className={styles.startNum}>{String(i + 1)}</div>
          <div className={styles.startText}>{t(step)}</div>
          {i === 0 && (
            <Button
              variant="flat"
              className={cx(styles.actionButton, styles.startButton, styles.fitText, sign?.active && styles.buttonOn)}
              onSelect={() => hubTriggers.launch("sign")}
            >
              {t(HUB_LOC.placeSign)}
            </Button>
          )}
        </div>
      ))}
      {sign?.locked && <div className={styles.reasonLine}>{t(sentKeyEntry(sign.reason, HUB_LOC.locked))}</div>}
    </div>
  );
}
