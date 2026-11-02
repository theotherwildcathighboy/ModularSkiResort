/*
 * src/hub/RunPropControls.tsx - the Runs view's two prop rows: how far apart the focused run's snow guns and floodlights stand. The same rows as the
 * tree line (QuickRunsControls TreeRowsQuick): caption, -50, -10, value, +10, +50. The run is the focused row of the list; the rows always draw, so
 * the view does not jump when a run is picked. The value is C#'s (RunRow.gunSpacingM / lightSpacingM: 0 = off, -1 = the kind carries none): a click
 * sends the next value and C# answers by pushing the row. Look only: snow and upkeep never read them (tools/check-run-props.js).
 * Panel file: no subscription of its own; RunsView passes the row.
 */
import { Unit } from "cs2/l10n";
import { RUN_PROP_SPACING_MAX, RUN_PROP_SPACING_OFF, RUN_PROP_SPACING_STEP, RUN_PROP_SPACING_STEP_COARSE, hubTriggers } from "../bindings";
import type { RunRow } from "../bindings";
import { useT } from "../l10n";
import { runPropSpacingStep } from "./hubRules";
import { HUB_LOC } from "./hubText";
import { Tip, numArg } from "./parts";
import { Act, Caption } from "./QuickRunsControls";
import styles from "./hub.module.scss";

function SpacingRow({ caption, tip, value, onSet }: { caption: string; tip: string; value: number; onSet: (meters: number) => void }): JSX.Element {
  const t = useT();
  const on = value >= 0;
  const lower = t(HUB_LOC.runPropsLower);
  const higher = t(HUB_LOC.runPropsHigher);
  const down = !on || value <= RUN_PROP_SPACING_OFF;
  const up = !on || value >= RUN_PROP_SPACING_MAX;
  const text = !on ? "–" : value <= RUN_PROP_SPACING_OFF ? t(HUB_LOC.runPropsOff) : numArg(value, Unit.Length);
  return (
    <div className={styles.quickRow}>
      <Caption text={caption} />
      <Act label="–50" tip={lower} disabled={down} onSelect={() => onSet(runPropSpacingStep(value, -RUN_PROP_SPACING_STEP_COARSE))} />
      <Act label="–10" tip={lower} next disabled={down} onSelect={() => onSet(runPropSpacingStep(value, -RUN_PROP_SPACING_STEP))} />
      <Tip tip={tip}>
        <div className={styles.stepValue}>{text}</div>
      </Tip>
      <Act label="+10" tip={higher} disabled={up} onSelect={() => onSet(runPropSpacingStep(value, RUN_PROP_SPACING_STEP))} />
      <Act label="+50" tip={higher} next disabled={up} onSelect={() => onSet(runPropSpacingStep(value, RUN_PROP_SPACING_STEP_COARSE))} />
    </div>
  );
}

export function RunPropRows({ row }: { row: RunRow | undefined }): JSX.Element {
  const t = useT();
  const guns = row ? row.gunSpacingM : -1;
  const lights = row ? row.lightSpacingM : -1;
  return (
    <>
      <div className={styles.quickLine}>{row ? row.name : t(HUB_LOC.runPropsNone)}</div>
      <SpacingRow
        caption={t(HUB_LOC.runPropsGuns)}
        tip={row && guns < 0 ? t(HUB_LOC.runPropsNoGuns) : t(HUB_LOC.runPropsGunsTip)}
        value={guns}
        onSet={(m) => { if (row) hubTriggers.setRunGunSpacing(row.entity, m); }}
      />
      <SpacingRow
        caption={t(HUB_LOC.runPropsLights)}
        tip={row && lights < 0 ? t(HUB_LOC.runPropsNoLights) : t(HUB_LOC.runPropsLightsTip)}
        value={lights}
        onSet={(m) => { if (row) hubTriggers.setRunLightSpacing(row.entity, m); }}
      />
    </>
  );
}
