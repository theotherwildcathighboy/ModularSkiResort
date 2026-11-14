/*
 * src/hub/units.ts - the hub's display layer for slopes, units and run grading (post-release round 1, T160 T161 T162). The C# twin is
 * mod/SkiResortMod/Systems/DisplayUnits.cs: the same factors and rules (test-hub-rules.js T60 and ResortBatch DU-* pin both).
 * - Slopes: degrees with one decimal (vanilla Unit.Angle drops a trailing .0). Data stays in grade / percent; convert at display only.
 * - Units: the game's own setting (Options > Interface > Unit system). Lengths, heights and areas go through vanilla's formatter
 *   (Unit.Length / Height / Area). What vanilla has no format for (hectares, line speed, snow depth) converts here, with a metric and an
 *   imperial locale entry each.
 * - Grading: North American or European names, markers and icons (GRADE_SCHEME, binding gradeScheme). Display only. A European variant
 *   of a text is the same key + "@EU" (EU_SUFFIX) in LocaleEN.cs and in hubText.ts EU_TEXT; schemeEntry picks it.
 * Pure: values only from ../bindings (no cs2/*), so test-hub-rules.js runs it under node.
 */
import { BAND, GRADE_SCHEME } from "../bindings";
import type { LocEntry } from "../l10n";

/** Imperial factors (vanilla's feet / yards: installed index.js 3.28084 ft per m, 0.9144 m per yd). */
export const FEET_PER_METER = 3.28084;
export const ACRES_PER_HECTARE = 2.471054;
export const CM_PER_INCH = 2.54;
export const FEET_PER_MINUTE_PER_MPS = 196.8504;

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

/** The marker shape of a band in a scheme: North American circle / square / diamond / two diamonds; European colored discs
 *  (blue / red / black / two black) for the same bands. Flat, pending and cannot-be-rated keep their own marks in both. */
export type BandShape = "flat" | "green" | "blue" | "black" | "dblack" | "euBlue" | "euRed" | "euBlack" | "euDblack" | "unrateable" | "pending";

export function bandShape(band: number, scheme: number): BandShape {
  const eu = isEuropean(scheme);
  switch (band) {
    case BAND.Flat: return "flat";
    case BAND.Green: return eu ? "euBlue" : "green";
    case BAND.Blue: return eu ? "euRed" : "blue";
    case BAND.Black: return eu ? "euBlack" : "black";
    case BAND.DoubleBlack: return eu ? "euDblack" : "dblack";
    case BAND.Unrateable: return "unrateable";
    default: return "pending";
  }
}

/** The icon NAME a text's `<icon:NAME>` shows in a scheme: the four rating icons swap to the European discs. */
export function schemeIconName(name: string, scheme: number): string {
  if (!isEuropean(scheme)) return name;
  switch (name) {
    case "green": return "euBlue";
    case "blue": return "euRed";
    case "black": return "euBlack";
    case "dblack": return "euDblack";
    default: return name;
  }
}
