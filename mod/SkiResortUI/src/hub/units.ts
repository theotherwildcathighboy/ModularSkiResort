/*
 * src/hub/units.ts - the hub's display layer for slopes, units and run grading (post-release round 1, T160 T161 T162). The C# twin is
 * mod/SkiResortMod/Systems/DisplayUnits.cs: the same factors and rules (test-hub-rules.js T60 and ResortBatch DU-* pin both).
 * - Slopes: degrees with one decimal (vanilla Unit.Angle drops a trailing .0). Data stays in grade / percent; convert at display only.
 * - Units: the mod's Options, each with Auto = the game's setting, resolved in C# and read here from the binding displayUnits (UNITS
 *   bits, unpackUnits): Lengths and elevation (length, height, area, earthwork, line speed), Weather (temperature, snow depth), Slope
 *   (degrees or percent). Lengths, heights, areas and temperatures still go through vanilla's formatter (Unit.Length / Height / Area /
 *   Temperature), given the chosen unit system (unitSettingsFor, parts.tsx unitLoc). What vanilla has no format for (hectares, line speed,
 *   snow depth) converts here, with a metric and an imperial locale entry each.
 * - Grading: North American or European names, markers and icons (GRADE_SCHEME, binding gradeScheme). Display only. A European variant
 *   of a text is the same key + "@EU" (EU_SUFFIX) in LocaleEN.cs and in hubText.ts EU_TEXT; schemeEntry picks it. The European scheme
 *   has its own limits on the crux grade (euClassOf, T162 rev. 2): green, blue, red, black piste, ski route. Green and blue are one
 *   band (SlopeBand Green), so they score the same.
 * Pure: values only from ../bindings (no cs2/*), so test-hub-rules.js runs it under node.
 */
import { BAND, GRADE_SCHEME, UNITS } from "../bindings";
import type { LocEntry } from "../l10n";

/** Imperial factors (vanilla's feet / yards: installed index.js 3.28084 ft per m, 0.9144 m per yd). */
export const FEET_PER_METER = 3.28084;
export const ACRES_PER_HECTARE = 2.471054;
export const CM_PER_INCH = 2.54;
export const FEET_PER_MINUTE_PER_MPS = 196.8504;
export const METERS_PER_YARD = 0.9144;
export const METERS_PER_MILE = 1609.344;

/** Imperial lengths: whole yards under 500 yd, miles with one decimal from 500 yd (T215, C# DisplayUnits.ImperialMilesFromM). Vanilla's
 *  Unit.Length switches at 1,609 m (one mile), so parts.tsx formats the miles from here itself. */
export const IMPERIAL_MILES_FROM_M = 457.2;

/** The miles an imperial length shows when the mod formats it itself (500 yd up to vanilla's own mile switch), rounded to one decimal.
 *  NaN = vanilla's Unit.Length formats it (metric, under 500 yd, from 1,609 m on, or not finite). */
export function imperialMiles(m: number, imperial: boolean): number {
  if (!imperial || !Number.isFinite(m)) return NaN;
  const a = Math.abs(m);
  return a >= IMPERIAL_MILES_FROM_M && a < 1609 ? round1(m / METERS_PER_MILE) : NaN;
}

/** The key suffix of a European text variant. */
export const EU_SUFFIX = "@EU";

/** Rounds to one decimal, half away from zero (C# MidpointRounding.AwayFromZero). */
export function round1(v: number): number {
  return Math.sign(v) * Math.round(Math.abs(v) * 10) / 10;
}

/** Grade (rise / run, 0.25 = 25 %) to degrees. Not finite gives NaN. */
export function gradeToDeg(grade: number): number {
  return Number.isFinite(grade) ? Math.atan(grade) * 180 / Math.PI : NaN;
}

/** THE slope format: a grade as degrees with one decimal. */
export function deg1(grade: number): number {
  const d = gradeToDeg(grade);
  return Number.isFinite(d) ? round1(d) : NaN;
}

/** A percent grade (25 = 25 %) as the shown slope in degrees, one decimal. A negative value (unknown) stays -1. */
export function pctToDeg1(pct: number): number {
  return Number.isFinite(pct) && pct >= 0 ? deg1(pct / 100) : -1;
}

/** Hectares, or acres when imperial, one decimal. */
export function areaHa(ha: number, imperial: boolean): number {
  return round1(imperial ? ha * ACRES_PER_HECTARE : ha);
}

/** Lift line speed: m/s with one decimal, or whole ft/min when imperial (US lifts quote ft/min). */
export function speed(mps: number, imperial: boolean): number {
  return imperial ? Math.round(mps * FEET_PER_MINUTE_PER_MPS) : round1(mps);
}

/** Snow depth: whole cm, or whole inches when imperial. */
export function depth(cm: number, imperial: boolean): number {
  return Math.round(imperial ? cm / CM_PER_INCH : cm);
}

/** The resolved choices of the binding displayUnits (C# DisplayUnits.PackUnits). */
export interface DisplayUnits {
  /** Lengths, heights, areas, earthwork and line speed in imperial units. */
  lengthImperial: boolean;
  /** Snow depth in inches. */
  depthImperial: boolean;
  /** 0 °C, 1 °F, 2 K (cs2/l10n TemperatureUnit). */
  temperature: number;
  /** Slopes in whole percent grade instead of degrees. */
  slopePercent: boolean;
}

/** Decodes the binding's int. Anything not a finite number reads 0: metric, °C, degrees. */
export function unpackUnits(packed: number): DisplayUnits {
  const p = Number.isFinite(packed) ? packed | 0 : 0;
  const t = (p >> UNITS.TempShift) & UNITS.TempMask;
  return {
    lengthImperial: (p & UNITS.LengthImperial) !== 0,
    depthImperial: (p & UNITS.DepthImperial) !== 0,
    temperature: t <= 2 ? t : 0,
    slopePercent: (p & UNITS.SlopePercent) !== 0,
  };
}

/** The unit category of a vanilla unit name (cs2/l10n Unit values): which Option decides it. Anything else keeps the game's setting. */
export function unitCategory(unit: string): "length" | "temperature" | null {
  switch (unit) {
    case "length": case "height": case "area": case "netElevation": return "length";
    case "temperature": return "temperature";
    default: return null;
  }
}

/** The unit settings vanilla's formatter should read for one unit: the game's own, with the unit system (lengths) or the temperature
 *  unit (temperature) of the mod's choice. The same object when nothing changes, so a caller can skip the copy. */
export function unitSettingsFor<S extends { unitSystem: number; temperatureUnit: number }>(game: S, unit: string, u: DisplayUnits): S {
  const cat = unitCategory(unit);
  if (cat === "length") {
    const system = u.lengthImperial ? 1 : 0;
    return game.unitSystem === system ? game : { ...game, unitSystem: system };
  }
  if (cat === "temperature") return game.temperatureUnit === u.temperature ? game : { ...game, temperatureUnit: u.temperature };
  return game;
}

/** A percent grade (25 = 25 %) as the shown slope number: degrees with one decimal, or whole percent when `percent`. Unknown
 *  (negative, not finite) gives -1. */
export function slopeValue(pct: number, percent: boolean): number {
  if (!Number.isFinite(pct) || pct < 0) return -1;
  return percent ? Math.round(pct) : pctToDeg1(pct);
}

/** True for the European scheme value of the gradeScheme binding (anything else reads North American, the 1.0.0 look). */
export function isEuropean(scheme: number): boolean {
  return scheme === GRADE_SCHEME.European;
}

/** The entry a scheme shows: the European variant (key + EU_SUFFIX) when the scheme is European and the table has one, else the entry. */
export function schemeEntry(entry: LocEntry, scheme: number, euText: Readonly<Record<string, string>>): LocEntry {
  if (!isEuropean(scheme)) return entry;
  const key = entry[0] + EU_SUFFIX;
  return Object.prototype.hasOwnProperty.call(euText, key) ? [key, euText[key]] : entry;
}

/** A European piste class (C# DisplayUnits.EuClass, display only). */
export const EU_CLASS = { Flat: 0, Green: 1, Blue: 2, Red: 3, Black: 4, Route: 5 } as const;

/** The European green / blue boundary on the crux grade, % (C# DisplayUnits.EuGreenMaxPct). Sources:
 *  docs/research/post-release-round1/european-piste-limits.md. */
export const EU_GREEN_MAX_PCT = 15;

/** The European class of a rated band (0 Flat .. 4 DoubleBlack) and its crux grade in percent (RunRow.gradePct), as C# EuClassOf:
 *  only the Green band reads the grade (under 15 % or unknown green, else blue). Red, black and route ARE the Blue, Black and
 *  DoubleBlack bands. Leave `gradePct` out for a band with no grade: the Green band then reads blue (the hardest it can be). */
export function euClassOf(band: number, gradePct: number = EU_GREEN_MAX_PCT): number {
  switch (band) {
    case BAND.Green: return gradePct >= EU_GREEN_MAX_PCT ? EU_CLASS.Blue : EU_CLASS.Green;
    case BAND.Blue: return EU_CLASS.Red;
    case BAND.Black: return EU_CLASS.Black;
    case BAND.DoubleBlack: return EU_CLASS.Route;
    default: return EU_CLASS.Flat;
  }
}

/** Counts by European class (index = EU_CLASS) from counts by band (index = SlopeBand) and how many of the Green band show green
 *  (euGreen, C# ResortStats.EuGreen / QuickRunsShown.EuGreen). Clamped, so a stale euGreen never gives a negative blue. */
export function euCounts(byBand: readonly number[], euGreen: number): number[] {
  const n = (i: number): number => Math.max(0, byBand[i] | 0);
  const green = Math.min(n(BAND.Green), Math.max(0, euGreen | 0));
  return [n(BAND.Flat), green, n(BAND.Green) - green, n(BAND.Blue), n(BAND.Black), n(BAND.DoubleBlack)];
}

/** The marker shape of a band in a scheme: North American circle / square / diamond / two diamonds; European colored discs (green, blue,
 *  red, black) and the ski route's orange diamond, by euClassOf. A Green band with no grade (`gradePct` left out: a need, a count of the
 *  whole band) shows "euGreenBlue", a small green and a small blue disc: the band is both. Flat, pending and cannot-be-rated keep their
 *  own marks in both schemes. */
export type BandShape = "flat" | "green" | "blue" | "black" | "dblack" | "euGreen" | "euBlue" | "euGreenBlue" | "euRed" | "euBlack" | "euRoute"
  | "unrateable" | "pending";

const EU_SHAPE: readonly BandShape[] = ["flat", "euGreen", "euBlue", "euRed", "euBlack", "euRoute"];

export function bandShape(band: number, scheme: number, gradePct?: number): BandShape {
  if (band === BAND.Unrateable) return "unrateable";
  if (!(band >= BAND.Flat && band <= BAND.DoubleBlack)) return "pending";
  if (isEuropean(scheme)) return band === BAND.Green && gradePct === undefined ? "euGreenBlue" : EU_SHAPE[euClassOf(band, gradePct)];
  switch (band) {
    case BAND.Flat: return "flat";
    case BAND.Green: return "green";
    case BAND.Blue: return "blue";
    case BAND.Black: return "black";
    default: return "dblack";
  }
}

/** The marker shape of a European class (the legend and the bar show classes, not bands). */
export function euClassShape(euClass: number): BandShape {
  return EU_SHAPE[euClass] ?? "pending";
}

/** The European art a text's `<icon:NAME>` shows (hubIcons.ts HUB_ICON eu): the same bands, so a text never needs a variant for an icon.
 *  Green (the Green band) is a green and a blue disc ("green or blue"), blue the red piste, black the black piste, double black the ski
 *  route. Other names keep their art. */
export function schemeIconName(name: string, scheme: number): string {
  if (!isEuropean(scheme)) return name;
  switch (name) {
    case "green": return "euGreenBlue";
    case "blue": return "euRed";
    case "black": return "euBlack";
    case "dblack": return "euRoute";
    default: return name;
  }
}
