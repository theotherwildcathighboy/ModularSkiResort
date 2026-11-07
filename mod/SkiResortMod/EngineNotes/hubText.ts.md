# hubText.ts: the hub's text contract (keys, placeholders, glyphs, reused keys)

`src/hub/hubText.ts` holds every player-facing string of the hub panel as a locale key plus its English fallback. `LocaleEN.cs` holds the
same texts for the game's dictionary (see `LocaleEN.md`).

## Wording rules
- Every text with "TIP" in its key is at most 25 words (`tools/test-hub-rules.js` T18.4). The two lift-money tips have their own caps of
  42 / 44 words (CatalogGuardTests CG-T8 (g)).
- "Patchy" is the Runs view's run state word, never "Partly" (the area's STATUS_WORD uses that).
- Some texts bake in catalog numbers (+12, 20 %, 1 km, 15 %, 70 %, up to 24, 6 / 15 / 25, a quarter). They are SkiCatalog's, and
  tests/ResortBatch CatalogGuardTests derive them from the catalog and fail on a difference.
- Snow is always two numbers, depth and run coverage. "≈" marks an estimate (`tools/test-hub-rules.js` T18 guards it).
- No text says a building joins or goes near the Welcome Sign. US spelling only (`tools/check-us-english.js`).
- Run names in the hub are RUN_TYPE (sentence case), never `Assets.NAME` (the run assets are Title Case, "Natural Run").

## Contract with the build gate
- Every entry is a literal `[key, english]` tuple. `tools/check-bundle.js` rule R4 fails the build unless each key exists in
  `mod/SkiResortMod/LocaleEN.cs` with identical text. It also warns about a `SkiResort.Hub.*` key there that neither TS nor C# uses. That is
  why the runtime-built keys (level names, keys C# sends, run types) are spelled out as tuples.
- R5 / R13: an entry whose English contains "{" lives in HUB_FMT only and is referenced only as `<LocText entry={HUB_FMT.x}>`
  (cs2/l10n `LocalizedString`). Every other map holds no placeholder (a map with one placeholder entry could no longer be indexed). Helpers
  return entries of placeholder-free maps only (`hubRules.ts` returns variant names).
- Numbers are never baked into a text: each is an argument built by `parts.tsx` `numArg`.
- Glyphs: printable ASCII plus the safe list (`← → ↑ ↓ … · – × ° ² ≈` and NBSP, `tools/check-bundle.js` SAFE_GLYPHS). No "¢", "−" or "√":
  money and signs come from `LocalizedNumber`.

## Reused keys
These keys are reused: `Assets.NAME[<prefab>]` (non-run items), `SkiResort.SlopeBand[*]` (the drawing tooltip's band names), `SkiResort.Tooltip.*`,
`SkiResort.LiftTooltip.*`, `SkiResort.GladeTooltip.*`. The vanilla key `SelectedInfoPanel.EFFICIENCY_FACTORS[*]` is reused too (`factorEntry`: not ours, not in
`LocaleEN.cs`).

## Depth text
`RUN_DEPTH` carries no baked open depth: the threshold is C#'s and would need its own placeholder passed by `RunList.tsx`. `{DEPTH}` is
`RunLive.snowDepthCm / 100` formatted as a length (m or ft by the player's unit setting).
