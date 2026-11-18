/*
 * src/hub/RunPropControls.tsx - the Runs view's feature rows for the focused run (run types R3, DECISIONS 578).
 * The rows are Groomed (on / off, a re-emit), then how far apart its snow guns and floodlights stand.
 * Each spacing row is caption, slider, readout (r3/sliders; it was caption, Fewer, value, More, DECISIONS 601). Off is the left end
 * and the right end is the densest spacing, so right is always more.
 * The run is the focused row of the list. The rows draw only while a run is picked (hub redesign 2026-11-07: one context area,
 * nothing when idle). A cat track's trash sits on its name line.
 * The values are C#'s (RunRow.gunSpacingM / lightSpacingM: 0 = off, -1 = it carries none, a natural run).
 * A slider release sends the new value, and C# answers by pushing the row.
 * The sliders are the Snowmaking and Lights features (DECISIONS 577): Off = no guns, no made snow, no bill. Each tip says
 * whether turning the feature on pays its one-time step or is free again (RunRow.paidSteps). A cat track has no Groomed row.
 * T170: a piste's name line carries Reshape. It opens the Run tool (launch of the Run tile) and names the run in reshapeRun$, so the view
 * keeps these rows while the tool is on. A second click closes the tool (the tile's own toggle). A cat track has no Reshape (tool-built).
 * Panel file: no subscription of its own. RunsView passes the row and whether this run is being reshaped.
 */
import { useState } from "react";
import type { ReactNode } from "react";
import { Unit } from "cs2/l10n";
import { NULL_ENTITY, RUN_KIND, RUN_PAID, RUN_PROP_SPACING_OFF, hubTriggers } from "../bindings";
import type { DrawRun, RunRow } from "../bindings";
import { useT } from "../l10n";
import { RUN_PROP_SPACING_STOPS } from "./hubRules";
import { HUB_LOC } from "./hubText";
import { TipLines, cx, numArg } from "./parts";
import { Slider } from "./Slider";
import { reshapeRun$ } from "./hubState";
import { TrashButton } from "./RunList";
import { Act, Caption } from "./QuickRunsControls";
import styles from "./hub.module.scss";

/** A spacing row (r3/sliders): caption, slider, readout. Left to right: Off, 200 m ... 20 m (RUN_PROP_SPACING_STOPS), so right is more
 *  guns or lights. A kind's default between two rungs (90 m) sits between them until the player moves it. C# gets one trigger, on
 *  release: a change re-emits the run, so dragging sends nothing. -1 = no row value (the run carries none): disabled, "–". */
function SpacingRow({ caption, tip, value, onSet }: { caption: string; tip: ReactNode; value: number; onSet: (meters: number) => void }): JSX.Element {
  const t = useT();
  const off = t(HUB_LOC.runPropsOff);
  const fmt = (v: number): ReactNode => (v <= RUN_PROP_SPACING_OFF ? off : numArg(v, Unit.Length));
  return (
    <div className={styles.quickRow}>
      <Caption text={caption} />
      <Slider ticks stops={RUN_PROP_SPACING_STOPS} value={value} format={fmt} samples={SPACING_SAMPLES.map(fmt)} tip={tip}
        onCommit={onSet} />
    </div>
  );
}
const SPACING_SAMPLES: readonly number[] = [RUN_PROP_SPACING_OFF, 200];

/* The feature tooltip, one idea per line (r3/sliders): what the spacing is on, what it does, what Off saves, and the one-time fee. */
function spacingTip(t: ReturnType<typeof useT>, what: "guns" | "lights", catTrack: boolean, fee: string | null): JSX.Element {
  const lines = what === "guns"
    ? catTrack ? [t(HUB_LOC.runPropsSpacingTipCatTrack), t(HUB_LOC.runPropsGunsCatTrack), t(HUB_LOC.runPropsGunsOffCatTrack)]
      : [t(HUB_LOC.runPropsSpacingTip), t(HUB_LOC.runPropsGunsOff)]
    : catTrack ? [t(HUB_LOC.runPropsSpacingTipCatTrack), t(HUB_LOC.runPropsLightsCatTrack), t(HUB_LOC.runPropsLightsOff)]
      : [t(HUB_LOC.runPropsSpacingTip), t(HUB_LOC.runPropsLightsEvening), t(HUB_LOC.runPropsLightsOff)];
  return <TipLines lines={[...lines, fee]} />;
}

/* The Run tile's option row while a run is drawn (contract 33): the same three rows, for the run about to be drawn. */
export function DrawRunRows({ draw }: { draw: DrawRun }): JSX.Element {
  const t = useT();
  const groomedTip = `${t(HUB_LOC.runPropsGroomedTip)} ${t(HUB_LOC.runPropsUnpaid)}`;
  const fee = t(HUB_LOC.runPropsFeeFirst);
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
        tip={draw.groomed ? spacingTip(t, "guns", false, fee) : t(HUB_LOC.runPropsNeedsGroomed)}
        value={draw.groomed ? draw.gunSpacingM : -1}
        onSet={(m) => hubTriggers.setDrawRun("guns", m)}
      />
      <SpacingRow
        caption={t(HUB_LOC.runPropsLights)}
        tip={draw.groomed ? spacingTip(t, "lights", false, fee) : t(HUB_LOC.runPropsNeedsGroomed)}
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

/* The paid-once note of a feature. The feature is free again once its step is paid. Otherwise its first time on pays a step.
 * A cat track pays its fee once at bind and nothing per feature (SkiRunFees), so its sliders say nothing about fees. */
function paidNote(t: ReturnType<typeof useT>, row: RunRow, bit: number): string {
  return (row.paidSteps & bit) !== 0 ? t(HUB_LOC.runPropsPaid) : t(HUB_LOC.runPropsUnpaid);
}

export function RunPropRows({ row, reshaping }: { row: RunRow; reshaping: boolean }): JSX.Element {
  const t = useT();
  const [armed, setArmed] = useState(false);
  const guns = row.gunSpacingM;
  const lights = row.lightSpacingM;
  const catTrack = row.kind === RUN_KIND.CatTrack;
  const feeLine = (bit: number): string | null => (catTrack ? null : (row.paidSteps & bit) !== 0 ? t(HUB_LOC.runPropsFeePaid) : t(HUB_LOC.runPropsFeeFirst));
  return (
    <>
      <div className={cx(styles.quickLine, styles.quickLineRow)}>
        <div className={styles.quickLineText}>{row.name}</div>
        {catTrack && <TrashButton row={row} t={t} armed={armed} setArmed={setArmed} />}
        {!catTrack && (
          <Act label={t(HUB_LOC.runPropsReshape)} tip={t(HUB_LOC.runPropsReshapeTip)} on={reshaping}
            onSelect={() => {
              reshapeRun$.update(reshaping ? NULL_ENTITY : row.entity);
              hubTriggers.launch("runGroomed");
            }} />
        )}
      </div>
      {!catTrack && <GroomedRow row={row} />}
      <SpacingRow
        caption={t(HUB_LOC.runPropsGuns)}
        tip={guns < 0 ? t(HUB_LOC.runPropsNeedsGroomed) : spacingTip(t, "guns", catTrack, feeLine(RUN_PAID.Snowmaking))}
        value={guns}
        onSet={(m) => hubTriggers.setRunGunSpacing(row.entity, m)}
      />
      <SpacingRow
        caption={t(HUB_LOC.runPropsLights)}
        tip={lights < 0 ? t(HUB_LOC.runPropsNeedsGroomed) : spacingTip(t, "lights", catTrack, feeLine(RUN_PAID.Lights))}
        value={lights}
        onSet={(m) => hubTriggers.setRunLightSpacing(row.entity, m)}
      />
    </>
  );
}
