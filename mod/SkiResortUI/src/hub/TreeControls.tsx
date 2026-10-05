/*
 * src/hub/TreeControls.tsx - the glade (tree) tool's controls: two fixed rows at the top of the Runs
 * view (TreeRows), and the same rows in a foldout on the contract-mismatch panel (TreesFoldout).
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-C); reworked
 * 2026-10-03 (Round U2, implementer I-3: a PanelFoldout, the J1-J3 layout-jump fixes of
 * docs/research/hub-u2-spec.md section 4.4); reworked 2026-10-04 for contract 3 (Round E1, stream
 * S6; docs/research/econ-round/spec/ch6-hub-ui.md section 6.8 "TreeControls.tsx", design
 * hub-v3-design.md 3.2 / 5.2, deviations U12 and U15).
 * Contract:
 *   - Row A `div.treeRow` (1.9 em of S): the tool's on/off Button with ONE constant label "Tree tool"
 *     (U15: `.treeToolButton`, 1.6 em of S, because the old `.actionButton`'s 30 rem x fontScale is
 *     taller than a 1.9 em row at 100 %), highlighted while active; its tooltip says what the tool
 *     does, how to stop it, or - while it is unavailable - C#'s reason (U12: the old status line is
 *     gone; disabled unless available or active, so an ACTIVE tool can always be stopped); then a
 *     spacer and the density stepper (− / value / +; J1: the value is a StableWidth whose blinds are
 *     the 8 labels "<level> · <name>" of the active dictionary, so the − / + never move; tooltip:
 *     GladeTooltip.LEVEL).
 *   - Row B `div.treeRow`: [Run interior | Tree line] (Tip TREES_APPLY_TO). Second pass (e1-spec H8):
 *     row B renders ONLY while the tree tool is active - a state change may change layout (D33) - so
 *     with the tool off the Runs view gains one run row (3 instead of 2 at 150 % text).
 *   - On/off: gladeToggle(); C# calls SkiGladeToolSystem.TryActivate("hub") or deactivates it.
 *     Stepper: clamps to 1..8, does not wrap (the Shift+G hotkey wraps 8 -> 1); − disabled at 1,
 *     + at 8; each click sends gladeSetLevel(level ± 1). Target: gladeSetTreeLine(false / true).
 *     glade.available == false: every control is disabled except the button that stops an ACTIVE
 *     tool; level and target are tool settings, changeable while the tool is off.
 *   - Reads only the CORE `glade` binding, so TreesFoldout also works on a contract mismatch
 *     (hub-u2-spec 4.5). Mounted only while the panel is open (research 2.F.6; gate R11 panel file).
 *   - Content area: default focus for every Button (ch6 section 9, F17).
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

/** The tree tool's two rows (row B only while the tool is active). */
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
          <Tip tip={t(HUB_LOC.treesApplyTo)}>
            <div className={styles.segment}>
              <Button
                variant="flat"
                className={cx(styles.segButton, !treeLine && styles.buttonOn)}
                disabled={settingsDisabled}
                selected={!treeLine}
                onSelect={() => hubTriggers.gladeSetTreeLine(false)}
              >
                {t(HUB_LOC.gladeTargetInterior)}
              </Button>
              <Button
                variant="flat"
                className={cx(styles.segButton, treeLine && styles.buttonOn)}
                disabled={settingsDisabled}
                selected={treeLine}
                onSelect={() => hubTriggers.gladeSetTreeLine(true)}
              >
                {t(HUB_LOC.gladeTargetTreeLine)}
              </Button>
            </div>
          </Tip>
        </div>
      )}
    </>
  );
}

/** The mismatch panel's Trees section as a foldout (expanded by default, remembered: OD-15). */
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
