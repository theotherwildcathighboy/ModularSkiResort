/*
 * src/hub/TreeControls.tsx - the glade (tree) tool's controls: two fixed rows at the top of the Runs view (TreeRows), and the same rows in a
 * foldout on the contract-mismatch panel (TreesFoldout). Row A: on/off Button (disabled unless available or active, so an active tool can always
 * be stopped) and the density stepper (clamps 1..8, no wrap; the Shift+G hotkey wraps 8 -> 1). Row B [Run interior | Tree line]: each Button in its
 * own Tip, because one Tip around two focusable Buttons puts two children under one vanilla Tooltip FocusBoundary (a PassThroughFocusController hosts
 * one child: "can only host a single child" on every toggle). Reads only the core `glade` binding. Content area: default focus for every Button.
 */
import { useState } from "react";
import { useValue } from "cs2/api";
import { Button, PanelFoldout } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
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

  const toolTip = active ? undefined
    : !available ? t(sentKeyEntry(g?.reason, HUB_LOC.treesUnavailable))
      : t(HUB_LOC.treeToolTip);

  return (
    <>
      <div className={styles.treeRow}>
        <Tip tip={toolTip}>
          <Button
            focusKey={FOCUS_DISABLED}
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
              focusKey={FOCUS_DISABLED}
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
              focusKey={FOCUS_DISABLED}
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
                focusKey={FOCUS_DISABLED}
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
                focusKey={FOCUS_DISABLED}
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
