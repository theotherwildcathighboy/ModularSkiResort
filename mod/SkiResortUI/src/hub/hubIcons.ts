/*
 * src/hub/hubIcons.ts - the icon table of hub text (icon scheme, hub contract 40, DECISIONS 620).
 * A locale string names an icon by its short NAME (`<icon:green>`, `<mark:open>`), never by a path, so art and paths can change without
 * a locale change. Each NAME has its word (SkiResort.Hub.ICON[NAME]): the word form that renderString, the fallback and check-ste use.
 * v1 holds the 10-icon core: 4 run ratings, 4 run states, power and snow. The ratings and states are our own flat SVGs (src/hub/icons/,
 * the shapes the hub already draws in CSS). Power and snow are vanilla icons. Every icon is a pre-colored <img>: inside Gameface's inline
 * layout a mask or a CSS shape does not draw (no Glyph, no BandMarker), so the color is in the file. Shape plus color, never color alone.
 * `wide` = a 24 x 16 box (two shapes side by side), else 16 x 16. The box is fixed, so a tooltip never resizes when the image loads.
 * Gates: tools/test-hub-rules.js T42 (every NAME used in text is here, every word key exists, the SVG colors equal the tokens) and
 * check-bundle R10 (every Media/ path exists in the installed game).
 */
import type { LocEntry } from "../l10n";
import { HUB_LOC } from "./hubText";
import iconGreen from "./icons/green.svg";
import iconBlue from "./icons/blue.svg";
import iconBlack from "./icons/black.svg";
import iconDblack from "./icons/dblack.svg";
import iconOpen from "./icons/open.svg";
import iconPatchy from "./icons/patchy.svg";
import iconClosed from "./icons/closed.svg";
import iconOpenpatchy from "./icons/openpatchy.svg";

export interface HubIcon {
  /** The image URL: a webpack asset (coui://ui-mods/SkiResortMod-img/...) or a vanilla Media/ path. */
  src: string;
  /** A 24 x 16 box (two shapes side by side). */
  wide: boolean;
  /** The icon's word (its word form). */
  word: LocEntry;
}

export type HubIconName = "green" | "blue" | "black" | "dblack" | "open" | "patchy" | "closed" | "openpatchy" | "power" | "snow";

export const HUB_ICON: Readonly<Record<HubIconName, HubIcon>> = {
  green: { src: iconGreen, wide: false, word: HUB_LOC.iconGreen },
  blue: { src: iconBlue, wide: false, word: HUB_LOC.iconBlue },
  black: { src: iconBlack, wide: false, word: HUB_LOC.iconBlack },
  dblack: { src: iconDblack, wide: true, word: HUB_LOC.iconDblack },
  open: { src: iconOpen, wide: false, word: HUB_LOC.iconOpen },
  patchy: { src: iconPatchy, wide: false, word: HUB_LOC.iconPatchy },
  closed: { src: iconClosed, wide: false, word: HUB_LOC.iconClosed },
  openpatchy: { src: iconOpenpatchy, wide: true, word: HUB_LOC.iconOpenpatchy },
  power: { src: "Media/Game/Icons/Electricity.svg", wide: false, word: HUB_LOC.iconPower },
  snow: { src: "Media/Game/Climate/Snow.svg", wide: false, word: HUB_LOC.iconSnow },
};

/** The header legend's groups, in order (icon | word rows). */
export const HUB_ICON_GROUPS: ReadonlyArray<{ title: LocEntry; names: readonly HubIconName[] }> = [
  { title: HUB_LOC.iconLegendRatings, names: ["green", "blue", "black", "dblack"] },
  { title: HUB_LOC.iconLegendStates, names: ["open", "patchy", "closed", "openpatchy"] },
  { title: HUB_LOC.iconLegendCity, names: ["power", "snow"] },
];

/** The table entry of a NAME, or null (unknown: the renderer shows the word form and reports it once). A NAME with "/" is a vanilla
 *  path, as in vanilla text: a translator may reuse a vanilla icon. Its word is the file name. */
export function hubIconOf(name: string): HubIcon | null {
  if (Object.prototype.hasOwnProperty.call(HUB_ICON, name)) return HUB_ICON[name as HubIconName];
  return null;
}

/**
 * The S0 switch (the in-game spike decides it). Three modes:
 * - "inline": everything on. Icons inside a wrapping sentence use Gameface's inline layout (`<p cohinline>`), unproven inside a cs2/ui
 *   Tooltip portal. Hanging marks and one-line structured rows (the missing lines, the legend) are plain flex rows.
 * - "flex": the inline path off. A sentence with icons shows its word form (today's text); marks and structured rows keep their icons.
 * - "words": every icon off, everything in word form.
 * ICON_MODE is the build default. The spike flips it live without a rebuild: in the game's UI console (gameface game_eval)
 * `window.skiResortIconMode = "flex"`, then hover again (a tooltip renders fresh on each hover).
 * S0 verdict (2026-11-08, field/2026-11-08/icons-pass/RESULTS.md): "flex". Inline layout is fine on size, baseline and wrap, but
 * Gameface drops the leading ASCII space of a text run after an inline element ("andone", "runis"). A NBSP pass could rescue it.
 */
export type IconMode = "inline" | "flex" | "words";
export const ICON_MODE: IconMode = "flex";

export function iconMode(): IconMode {
  try {
    const v = (window as unknown as Record<string, unknown>).skiResortIconMode;
    return v === "inline" || v === "flex" || v === "words" ? v : ICON_MODE;
  } catch {
    return ICON_MODE;
  }
}
