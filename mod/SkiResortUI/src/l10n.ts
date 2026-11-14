/*
 * src/l10n.ts - player-facing text: locale keys + English fallbacks, and the translate hook.
 * Every player-facing string is a `SkiResort.Hub.*` key in the mod's root LocaleEN.cs. There is no TS dictionary. translate(key, englishFallback)
 * resolves live against the active dictionary, so the fallback shows only while a key is missing. Gate (tools/check-bundle.js): every
 * `[key, english]` tuple in src/ must exist in LocaleEN.cs with identical text. An entry whose English contains "{" is a placeholder entry.
 * Render it only with <LocText> (parts.tsx). translate() / useT() never substitute placeholders (index.js@435324, @510010), so the gate fails
 * a t(...) call on it. Numbers: put <LocalizedNumber value unit={Unit.X} /> (parts.tsx Num) beside the text. Never bake a number into a string.
 */
import { useCallback } from "react";
import { useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { gradeScheme$ } from "./bindings";
import { unitNbsp } from "./hub/hubRules";
import { EU_TEXT } from "./hub/hubText";
import { schemeEntry } from "./hub/units";

/** [locale key, English fallback]. */
export type LocEntry = readonly [key: string, english: string];

/** Keys used by the always-mounted parts (launchers, root, error notice, panel frame). */
export const LOC = {
  title: ["SkiResort.Hub.TITLE", "Ski Resort"],
  launcherTooltip: ["SkiResort.Hub.LAUNCHER_TOOLTIP", "Ski Resorts"],
  notConnected: ["SkiResort.Hub.NOT_CONNECTED", "The mod is not answering: its hub system is missing or failed. See SkiResortMod.Mod.log."],
  stampDiffers: ["SkiResort.Hub.STAMP_DIFFERS", "The UI bundle comes from a different build than the DLL."],
  uiError: ["SkiResort.Hub.UI_ERROR", "The Ski Resort hub hit an error and was closed. Details are in the mod log."],
} as const; // TypeScript 4.8: no `satisfies`

export type Translate = (entry: LocEntry) => string;

/** translate(key, fallback) bound to the active dictionary. A number keeps its unit on one line (hubRules unitNbsp, render time only).
 *  The run grading (T162) picks an entry's European variant (key + "@EU", hubText EU_TEXT) while the scheme is European. */
export function useT(): Translate {
  const { translate } = useLocalization();
  const scheme = useValue(gradeScheme$);
  return useCallback((entry: LocEntry) => {
    const e = schemeEntry(entry, scheme, EU_TEXT);
    return unitNbsp(translate(e[0], e[1]) ?? e[1]);
  }, [translate, scheme]);
}
