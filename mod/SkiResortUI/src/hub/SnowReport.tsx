/*
 * src/hub/SnowReport.tsx - the ski area snow report (contract 48, T200): the Welcome Sign card beside the game's selected-info panel
 * (always mounted at the "Game" hook, reads only snowCard$ and displayUnits$, never a panel binding) and the status tooltip's lines.
 * Base and summit are the lowest and highest run node. Depth is the snow map there. New snow and season snowfall come from the climate
 * (C# SkiSnowReportSystem, nothing saved). Units: Options > Weather (cm / in, °C / °F), elevations Options > Lengths.
 * A section inside the game's panel would need moduleRegistry.extend and an internal game-ui path, which the gate bans. Facts only:
 * pointer-events stay off, so the card never takes a click.
 */
import { useValue } from "cs2/api";
import { Unit } from "cs2/l10n";
import { snowCard$ } from "../bindings";
import type { SnowReport } from "../bindings";
import { useT } from "../l10n";
import type { Translate } from "../l10n";
import styles from "./hub.module.scss";
import { HUB_FMT, HUB_LOC, monthName } from "./hubText";
import { InfoLine, LocText, depthValue, numArg, useUnits } from "./parts";
import type { TipLine } from "./parts";

/** A depth or a snowfall in whole cm as one text node, cm or in. Unknown (negative) gives "–". */
function snowArg(cm: number, inches: boolean): JSX.Element | string {
  if (!(cm >= 0)) return "–";
  return inches
    ? <LocText entry={HUB_FMT.snowDepthIn} args={{ DEPTH: numArg(depthValue(cm, true), Unit.Integer) }} />
    : <LocText entry={HUB_FMT.snowDepth} args={{ DEPTH: numArg(cm, Unit.Integer) }} />;
}

/** "{SUMMIT} summit, {BASE} base" for one quantity. */
function pairText(summitCm: number, baseCm: number, inches: boolean): JSX.Element {
  return <LocText entry={HUB_FMT.snowReportPair} args={{ SUMMIT: snowArg(summitCm, inches), BASE: snowArg(baseCm, inches) }} />;
}

/** One elevation's head line value: the depth and the temperature, or the temperature alone while the depth is not measured. */
function atText(r: SnowReport, summit: boolean, inches: boolean): JSX.Element | string {
  const cm = summit ? r.summitDepthCm : r.baseDepthCm;
  const TEMP = r.tempKnown ? numArg(summit ? r.summitTempC : r.baseTempC, Unit.Temperature, false, false) : "–";
  return cm >= 0
    ? <LocText entry={HUB_FMT.snowReportAt} args={{ DEPTH: snowArg(cm, inches), TEMP }} />
    : <LocText entry={HUB_FMT.snowReportAtNoDepth} args={{ TEMP }} />;
}

function headLabel(r: SnowReport, summit: boolean): JSX.Element {
  const ELEV = numArg(summit ? r.summitM : r.baseM, Unit.Height, false, false);
  return summit
    ? <LocText entry={HUB_FMT.snowReportSummit} args={{ ELEV }} />
    : <LocText entry={HUB_FMT.snowReportBase} args={{ ELEV }} />;
}

/** The status tooltip's snow report lines (StatusBlock statusTip). None without a run outline. */
export function snowReportTipLines(r: SnowReport | undefined, t: Translate, inches: boolean): TipLine[] {
  if (!r || !r.known) return [];
  return [
    t(HUB_LOC.snowReportTitle),
    { label: headLabel(r, true), value: atText(r, true, inches) },
    { label: headLabel(r, false), value: atText(r, false, inches) },
    r.summitNew24Cm >= 0 && { label: t(HUB_LOC.snowReportNew24), value: pairText(r.summitNew24Cm, r.baseNew24Cm, inches) },
    r.summitSeasonCm >= 0 && { label: t(HUB_LOC.snowReportSeason), value: pairText(r.summitSeasonCm, r.baseSeasonCm, inches) },
  ];
}

/** The report's rows: summit and base, each with its depth, new snow and season snowfall, then the season note. No box and no title,
 *  so a host can place them: the Welcome Sign card now, a hub fold or a longer sign card later (T201 proposals 2 and 7). Leaf
 *  component: it reads only displayUnits$ (always observed) and takes the report as a prop. */
export function SnowReportRows({ report: r }: { report: SnowReport }): JSX.Element {
  const t = useT();
  const inches = useUnits().depthImperial;
  if (!r.known) return <div className={styles.snowCardNote}>{t(HUB_LOC.snowReportNoRuns)}</div>;
  const since = r.seasonFirstMonth >= 1 && r.seasonFirstMonth <= 12 ? monthName(r.seasonFirstMonth - 1, t) : null;
  return (
    <>
      <InfoLine label={headLabel(r, true)} value={r.tempKnown ? numArg(r.summitTempC, Unit.Temperature, false, false) : "–"} />
      <InfoLine sub label={t(HUB_LOC.snowReportDepth)} value={r.summitDepthCm >= 0 ? snowArg(r.summitDepthCm, inches) : t(HUB_LOC.snowReportNotMeasured)} />
      <InfoLine sub label={t(HUB_LOC.snowReportNew24)} value={snowArg(r.summitNew24Cm, inches)} />
      <InfoLine sub label={t(HUB_LOC.snowReportSeason)} value={snowArg(r.summitSeasonCm, inches)} />
      <InfoLine label={headLabel(r, false)} value={r.tempKnown ? numArg(r.baseTempC, Unit.Temperature, false, false) : "–"} />
      <InfoLine sub label={t(HUB_LOC.snowReportDepth)} value={r.baseDepthCm >= 0 ? snowArg(r.baseDepthCm, inches) : t(HUB_LOC.snowReportNotMeasured)} />
      <InfoLine sub label={t(HUB_LOC.snowReportNew24)} value={snowArg(r.baseNew24Cm, inches)} />
      <InfoLine sub label={t(HUB_LOC.snowReportSeason)} value={snowArg(r.baseSeasonCm, inches)} />
      {since && <div className={styles.snowCardNote}><LocText entry={HUB_FMT.snowReportSince} args={{ MONTH: since }} /></div>}
    </>
  );
}

/** The Welcome Sign card: a title, then the rows. Further blocks (the ski area's state, guests, net) go above the rows. */
export function SnowCard(): JSX.Element | null {
  const t = useT();
  const card = useValue(snowCard$);
  if (!card.shown) return null;
  return (
    <div className={styles.busCard}>
      <div className={styles.busCardTitle}>{t(HUB_LOC.snowReportTitle)}</div>
      <SnowReportRows report={card.report} />
    </div>
  );
}
