/*
 * src/hub/TreeControls.tsx - the glade (tree) tool's controls. The rows sit at the top of the Runs view while the tool is on.
 * There, the footer's Tree chip is the on/off (TreeRows toggle={false}, hub redesign 2026-11-07).
 * The same rows, with their own on/off, sit in a foldout on the contract-mismatch panel (TreesFoldout).
 * Row A: an on/off Button and the density slider (r3/sliders, 8 stops with ticks, "4 · Open glade"; it was a - / + stepper). The Button is
 * disabled unless available or active, so an active tool can always be stopped. The slider spans 1..8. The density hotkey wraps 8 -> 1.
 * The slider has no tooltip: its readout names the level, and there is nothing to add.
 * Row B [Run interior | Tree line]: each Button sits in its own Tip. One Tip around two focusable Buttons puts two children under
 * one vanilla Tooltip FocusBoundary. A PassThroughFocusController hosts one child ("can only host a single child" on every toggle).
 * It reads only the core `glade` binding. Content area: default focus for every Button.
 */
import { useState } from "react";
import { useValue } from "cs2/api";
import { Button, PanelFoldout } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
import { GLADE_LEVEL_DEFAULT, GLADE_LEVEL_MAX, GLADE_LEVEL_MIN, clampGladeLevel, glade$, hubTriggers } from "../bindings";
import { useT } from "../l10n";
import { treesOpen$ } from "./hubState";
import { HUB_LOC, gladeLevelEntry, sentKeyEntry } from "./hubText";
import { Tip, cx } from "./parts";
import { GLADE_STOPS } from "./hubRules";
import { Slider } from "./Slider";
import styles from "./hub.module.scss";

export function TreeRows({ toggle = true }: { toggle?: boolean } = {}): JSX.Element {
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
        {toggle && <Tip tip={toolTip}>
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
        </Tip>}
        {!toggle && <div className={styles.quickCaption}>{t(HUB_LOC.quickTreeDensity)}</div>}
        <Slider ticks stops={GLADE_STOPS} value={level} disabled={settingsDisabled} format={label} samples={samples}
          onCommit={(v) => hubTriggers.gladeSetLevel(v)} />
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
