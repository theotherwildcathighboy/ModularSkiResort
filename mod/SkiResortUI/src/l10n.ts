/*
 * src/l10n.ts - player-facing text: locale keys + English fallbacks, and the translate hook.
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-A); amended
 * 2026-10-03 (Round U2, implementer I-3: CONNECTED and CONTRACT_MISMATCH retired - the first was
 * never rendered, the second is replaced by SkiResort.Hub.CONTRACT_MISMATCH_CORE, spec 4.5).
 * Rule (research 2.F.5): every player-facing string is a `SkiResort.Hub.*` key in the mod's root
 * LocaleEN.cs (C#); there is no TS dictionary. The TSX calls translate(key, englishFallback),
 * which resolves live against the active dictionary, so the English fallback shows only while a
 * key is missing.
 * Gate (tools/check-bundle.js, spec 4.7 rules 4-5):
 *   - every `[key, english]` tuple in src/ must exist in root LocaleEN.cs with IDENTICAL text;
 *   - an entry whose English contains "{" is a placeholder entry: render it ONLY with <LocText>
 *     (src/hub/parts.tsx, cs2/l10n LocalizedString). translate() / useT() never substitute
 *     placeholders (LocalizedString's renderer does: index.js@435324 renderLocalizedString,
 *     substitute @510010), so the gate FAILS a t(...) call on such an entry.
 * Numbers: <LocalizedNumber value unit={Unit.X} /> (src/hub/parts.tsx Num) beside the text, never
 * baked into a string.
 */
import { useCallback } from "react";
import { useLocalization } from "cs2/l10n";

/** [locale key, English fallback]. The C# side adds each key to root LocaleEN.cs. */
export type LocEntry = readonly [key: string, english: string];

/** Keys used by the always-mounted parts (launchers, root, error notice, panel frame). */
export const LOC = {
  title: ["SkiResort.Hub.TITLE", "Ski Resort"],
  launcherTooltip: ["SkiResort.Hub.LAUNCHER_TOOLTIP", "Ski Resort: runs, resort totals and every building tool"],
  notConnected: ["SkiResort.Hub.NOT_CONNECTED", "The mod is not answering: its hub system is missing or failed. See SkiResortMod.Mod.log."],
  stampDiffers: ["SkiResort.Hub.STAMP_DIFFERS", "The UI bundle comes from a different build than the DLL."],
  uiError: ["SkiResort.Hub.UI_ERROR", "The Ski Resort panel hit an error and was closed. Details are in the mod log."],
} as const; // (TypeScript 4.8: no `satisfies`; each entry is assignable to LocEntry)

export type Translate = (entry: LocEntry) => string;

/** translate(key, fallback) bound to the active dictionary; never returns null. */
export function useT(): Translate {
  const { translate } = useLocalization();
  return useCallback((entry: LocEntry) => translate(entry[0], entry[1]) ?? entry[1], [translate]);
}
