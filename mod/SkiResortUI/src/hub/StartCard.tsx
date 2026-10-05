/*
 * src/hub/StartCard.tsx - the start card: the panel's content while the city has no live Welcome Sign.
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/
 * spec/ch6-hub-ui.md section 6.12, design hub-v3-design.md 3.7 / 6.11; replaces the contract-2
 * NO_SIGN block of ResortDashboard.tsx, deleted).
 * Contract:
 *   - Shown whenever the city has no live sign, even when unlinked runs or lifts exist (deviation U9:
 *     the player's next step is a sign either way; the header still names the Not linked entry then).
 *   - START A SKI AREA, then FIVE numbered steps (second pass, e1-spec H6: step 2 "Connect the sign:
 *     road, power, water and sewage" names the biggest lever, the sign's efficiency), each a fixed
 *     `.startStep` row (the vanilla InfoRow size); step 1 also holds the "Place Welcome Sign" button
 *     (cs2/ui Button, `.startButton` 1.6 em of S so it fits the row - E1 fixer F16, the U15 rule;
 *     launch("sign"); highlighted while the sign tool is active; a locked sign still
 *     launches - C# refuses and logs it, like the Build strip). While build[sign] is locked, its
 *     reason follows the steps (a state change). Round F7 (D6, findings/hub-ux.md §5 item 5): no
 *     START_NOTE any more - footer line 2 (NEXT2[Sign] = BUILD_HINT_SIGN) says the same sentence.
 *   - The header shows the plain title; the footer's dot sits on the sign tile (hubRules.dotItem
 *     with hasSign false).
 *   - Panel file (gate R11): subscribes build$ (CORE). Content focus (ch6 section 9).
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
  HUB_LOC.startStep1, HUB_LOC.startStep2, HUB_LOC.startStep3, HUB_LOC.startStep4, HUB_LOC.startStep5,
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
