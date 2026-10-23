/*
 * src/hub/TreeControls.tsx - the glade (tree) tool's controls: two fixed rows at the top of the Runs view (TreeRows) and the
 * same rows in a foldout on the contract-mismatch panel (TreesFoldout).
 *   - Row A: the tool's on/off Button with one constant label "Tree tool" (`.treeToolButton`, 1.6 em: `.actionButton`'s
 *     30 rem x fontScale is taller than a 1.9 em row at 100 %), highlighted while active. Its tooltip says what the tool does
 *     and how to stop it, or while unavailable C#'s reason. Disabled unless available or active, so an active tool can always
 *     be stopped. Then a spacer and the density stepper (- / value / +): the value is a StableWidth whose blinds are the 8
 *     labels "<level> · <name>", so - / + never move.
 *   - Row B: [Run interior | Tree line], each Button in its own Tip. One Tip around both put two focusable Buttons under one
 *     vanilla Tooltip, whose FocusBoundary (a PassThroughFocusController) hosts one child ("PassThroughFocusControllers can
 *     only host a single child" on every toggle). Renders only while the tool is active (a state change may change layout), so
 *     with the tool off the Runs view gains one run row.
 *   - On/off: gladeToggle(); C# calls SkiGladeToolSystem.TryActivate("hub") or deactivates. The stepper clamps to 1..8 and
 *     does not wrap (the Shift+G hotkey wraps 8 -> 1); each click sends gladeSetLevel(level +/- 1). Target:
 *     gladeSetTreeLine(false / true). glade.available == false disables every control except the button that stops an active
 *     tool; level and target are tool settings, changeable while the tool is off.
 *   - Reads only the core `glade` binding, so TreesFoldout also works on a contract mismatch. Mounted only while the panel is
 *     open. Content area: default focus for every Button.
 */
import { useState } from "react";
import { useValue } from "cs2/api";
import { Button, PanelFoldout } from "cs2/ui";
import { GLADE_LEVEL_DEFAULT, GLADE_LEVEL_MAX, GLADE_LEVEL_MIN, clampGladeLevel, glade$, hubTriggers } from "../bindings";
import { useT } from "../l10n";
import { treesOpen$ } from "./hubState";
import { HUB_LOC, gladeLevelEntry, sentKeyEntry } from "./hubText";
import { StableWidth, Tip, cx } from "./parts";
import styles from "./hub.module.scss";

export function TreeRows(): JSX.Element {
  const t = useT();
  const g = useValue(glade$);
  const available = !!g?.available;
  const active = !!g?.active;
  const level = clampGladeLevel(g?.level ?? GLADE_LEVEL_DEFAULT);
  const treeLine = !!g?.treeLine;
  const settingsDisabled = !available;

  const label = (l: number): string => `${l} · ${t(gladeLevelEntry(l))}`;
  const samples: string[] = [];
  for (let l = GLADE_LEVEL_MIN; l <= GLADE_LEVEL_MAX; l++) samples.push(label(l));

  const toolTip = active ? t(HUB_LOC.treeToolTipOn)
    : !available ? t(sentKeyEntry(g?.reason, HUB_LOC.treesUnavailable))
      : t(HUB_LOC.treeToolTip);

  return (
    <>
      <div className={styles.treeRow}>
        <Tip tip={toolTip}>
          <Button
            variant="flat"
            className={cx(styles.treeToolButton, active && styles.buttonOn)}
            disabled={!available && !active}
            selected={active}
            onSelect={() => hubTriggers.gladeToggle()}
          >
            {t(HUB_LOC.treeTool)}
          </Button>
        </Tip>
        <div className={styles.treeRowSpacer} />
        <div className={styles.stepper}>
          <Tip tip={t(HUB_LOC.treesFewer)}>
            <Button
              variant="flat"
              className={styles.stepButton}
              disabled={settingsDisabled || level <= GLADE_LEVEL_MIN}
              onSelect={() => hubTriggers.gladeSetLevel(level - 1)}
            >
              {"–"}
            </Button>
          </Tip>
          <Tip tip={t(HUB_LOC.gladeLevel)}>
            <div className={styles.stepValue}>
              <StableWidth samples={samples} align="center">
                {label(level)}
              </StableWidth>
            </div>
          </Tip>
          <Tip tip={t(HUB_LOC.treesMore)}>
            <Button
              variant="flat"
              className={styles.stepButton}
              disabled={settingsDisabled || level >= GLADE_LEVEL_MAX}
              onSelect={() => hubTriggers.gladeSetLevel(level + 1)}
            >
              {"+"}
            </Button>
          </Tip>
        </div>
      </div>
      {active && (
        <div className={styles.treeRow}>
          <div className={styles.segment}>
            <Tip tip={t(HUB_LOC.treesApplyTo)}>
              <Button
                variant="flat"
                className={cx(styles.segButton, !treeLine && styles.buttonOn)}
                disabled={settingsDisabled}
                selected={!treeLine}
                onSelect={() => hubTriggers.gladeSetTreeLine(false)}
              >
                {t(HUB_LOC.gladeTargetInterior)}
              </Button>
            </Tip>
            <Tip tip={t(HUB_LOC.treesApplyTo)}>
              <Button
                variant="flat"
                className={cx(styles.segButton, styles.segButtonNext, treeLine && styles.buttonOn)}
                disabled={settingsDisabled}
                selected={treeLine}
                onSelect={() => hubTriggers.gladeSetTreeLine(true)}
              >
                {t(HUB_LOC.gladeTargetTreeLine)}
              </Button>
            </Tip>
          </div>
        </div>
      )}
    </>
  );
}

/* The mismatch panel's Trees section as a foldout (expanded by default, remembered). */
export function TreesFoldout(): JSX.Element {
  const t = useT();
  const [open] = useState(() => treesOpen$.value);
  return (
    <PanelFoldout
      header={<div className={styles.foldoutTitle}>{t(HUB_LOC.sectionTrees)}</div>}
      initialExpanded={open}
      onToggleExpanded={(v: boolean) => treesOpen$.update(v)}
      disableFocus
      className={styles.foldout}
    >
      <TreeRows />
    </PanelFoldout>
  );
}
