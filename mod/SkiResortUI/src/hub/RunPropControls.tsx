/*
 * src/hub/RunPropControls.tsx - the Runs view's feature rows for the focused run (run types R3, DECISIONS 578): Groomed (on / off, a re-emit),
 * then how far apart its snow guns and floodlights stand. Each spacing row is caption, Fewer, value, More (DECISIONS 601: the old -50 / -10 /
 * +10 / +50 read backwards, since a smaller spacing means more guns and the low end was Off). The run is the focused row of the list; the rows draw only while a run is picked (hub redesign 2026-11-07: one context area, nothing when idle), and a cat track's trash sits on its name line. The values
 * are C#'s (RunRow.gunSpacingM / lightSpacingM: 0 = off, -1 = it carries none, a natural run): a click sends the next value and C# answers by
 * pushing the row. The sliders are the Snowmaking and Lights features (DECISIONS 577): Off = no guns, no made snow, no bill. Each tip says
 * whether turning the feature on pays its one-time step or is free again (RunRow.paidSteps). A cat track has no Groomed row.
 * Panel file: no subscription of its own; RunsView passes the row.
 */
import { useState } from "react";
import { Unit } from "cs2/l10n";
import { RUN_KIND, RUN_PAID, RUN_PROP_SPACING_MIN, RUN_PROP_SPACING_OFF, hubTriggers } from "../bindings";
import type { DrawRun, RunRow } from "../bindings";
import { useT } from "../l10n";
import { runPropSpacingStep } from "./hubRules";
import { HUB_LOC } from "./hubText";
import { Tip, cx, numArg } from "./parts";
import { TrashButton } from "./RunList";
import { Act, Caption } from "./QuickRunsControls";
import styles from "./hub.module.scss";

function SpacingRow({ caption, tip, value, onSet }: { caption: string; tip: string; value: number; onSet: (meters: number) => void }): JSX.Element {
  const t = useT();
  const has = value >= 0;
  const off = value <= RUN_PROP_SPACING_OFF;
  const text = !has ? "–" : off ? t(HUB_LOC.runPropsOff) : numArg(value, Unit.Length);
  return (
    <div className={styles.quickRow}>
      <Caption text={caption} />
      <Act label={t(HUB_LOC.runPropsFewer)} tip={t(HUB_LOC.runPropsFewerTip)} disabled={!has || off} onSelect={() => onSet(runPropSpacingStep(value, false))} />
      <Tip tip={tip}>
        <div className={styles.stepValue}>{text}</div>
      </Tip>
      <Act label={t(HUB_LOC.runPropsMore)} tip={t(HUB_LOC.runPropsMoreTip)} disabled={!has || value === RUN_PROP_SPACING_MIN} onSelect={() => onSet(runPropSpacingStep(value, true))} />
    </div>
  );
}

/* The Run tile's option row while a run is drawn (contract 33): the same three rows, for the run about to be drawn. */
export function DrawRunRows({ draw }: { draw: DrawRun }): JSX.Element {
  const t = useT();
  const tip = (base: string): string => `${base} ${t(HUB_LOC.runPropsUnpaid)}`;
  const groomedTip = tip(t(HUB_LOC.runPropsGroomedTip));
  return (
    <>
      <div className={styles.quickLine}>{t(HUB_LOC.drawRunTitle)}</div>
      <div className={styles.quickRow}>
        <Caption text={t(HUB_LOC.runPropsGroomed)} />
        <Act label={t(HUB_LOC.runPropsOn)} tip={groomedTip} on={draw.groomed} onSelect={() => hubTriggers.setDrawRun("groomed", 1)} />
        <Act label={t(HUB_LOC.runPropsOff)} tip={groomedTip} next on={!draw.groomed} onSelect={() => hubTriggers.setDrawRun("groomed", 0)} />
      </div>
      <SpacingRow
        caption={t(HUB_LOC.runPropsGuns)}
        tip={draw.groomed ? tip(t(HUB_LOC.runPropsGunsTip)) : t(HUB_LOC.runPropsNeedsGroomed)}
        value={draw.groomed ? draw.gunSpacingM : -1}
        onSet={(m) => hubTriggers.setDrawRun("guns", m)}
      />
      <SpacingRow
        caption={t(HUB_LOC.runPropsLights)}
        tip={draw.groomed ? tip(t(HUB_LOC.runPropsLightsTip)) : t(HUB_LOC.runPropsNeedsGroomed)}
        value={draw.groomed ? draw.lightSpacingM : -1}
        onSet={(m) => hubTriggers.setDrawRun("lights", m)}
      />
    </>
  );
}

function GroomedRow({ row }: { row: RunRow | undefined }): JSX.Element {
  const t = useT();
  const piste = !!row && row.kind !== RUN_KIND.CatTrack;
  const groomed = piste && row.kind !== RUN_KIND.Natural;
  const tip = !row ? t(HUB_LOC.runPropsGroomedTip) : `${t(HUB_LOC.runPropsGroomedTip)} ${paidNote(t, row, RUN_PAID.Groomed)}`;
  return (
    <div className={styles.quickRow}>
      <Caption text={t(HUB_LOC.runPropsGroomed)} />
      <Act label={t(HUB_LOC.runPropsOn)} tip={tip} on={groomed} disabled={!piste}
        onSelect={() => { if (row) hubTriggers.setRunGroomed(row.entity, true); }} />
      <Act label={t(HUB_LOC.runPropsOff)} tip={tip} next on={piste && !groomed} disabled={!piste}
        onSelect={() => { if (row) hubTriggers.setRunGroomed(row.entity, false); }} />
    </div>
  );
}

/* The paid-once note of a feature: free again once its step is paid, else its first time on pays a step. A cat track pays its fee once at bind
 * and nothing per feature (SkiRunFees), so its sliders say nothing about fees. */
function paidNote(t: ReturnType<typeof useT>, row: RunRow, bit: number): string {
  return (row.paidSteps & bit) !== 0 ? t(HUB_LOC.runPropsPaid) : t(HUB_LOC.runPropsUnpaid);
}

export function RunPropRows({ row }: { row: RunRow }): JSX.Element {
  const t = useT();
  const [armed, setArmed] = useState(false);
  const guns = row.gunSpacingM;
  const lights = row.lightSpacingM;
  const catTrack = row.kind === RUN_KIND.CatTrack;
  const withNote = (base: string, bit: number): string => (!catTrack ? `${base} ${paidNote(t, row, bit)}` : base);
  return (
    <>
      <div className={cx(styles.quickLine, styles.quickLineRow)}>
        <div className={styles.quickLineText}>{row.name}</div>
        {catTrack && <TrashButton row={row} t={t} armed={armed} setArmed={setArmed} />}
      </div>
      {!catTrack && <GroomedRow row={row} />}
      <SpacingRow
        caption={t(HUB_LOC.runPropsGuns)}
        tip={guns < 0 ? t(HUB_LOC.runPropsNeedsGroomed)
          : catTrack ? t(HUB_LOC.runPropsGunsTipCatTrack) : withNote(t(HUB_LOC.runPropsGunsTip), RUN_PAID.Snowmaking)}
        value={guns}
        onSet={(m) => hubTriggers.setRunGunSpacing(row.entity, m)}
      />
      <SpacingRow
        caption={t(HUB_LOC.runPropsLights)}
        tip={lights < 0 ? t(HUB_LOC.runPropsNeedsGroomed)
          : catTrack ? t(HUB_LOC.runPropsLightsTipCatTrack) : withNote(t(HUB_LOC.runPropsLightsTip), RUN_PAID.Lights)}
        value={lights}
        onSet={(m) => hubTriggers.setRunLightSpacing(row.entity, m)}
      />
    </>
  );
}
