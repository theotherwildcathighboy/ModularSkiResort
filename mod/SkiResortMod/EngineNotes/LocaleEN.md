# LocaleEN: how the English text source is keyed, gated and worded

`LocaleEN.cs` (root) is the `IDictionarySource` for the asset pack's prefab names and descriptions plus the hub, run-drawing, glade and
lift tooltip texts. `Settings/LocaleEN.cs` holds the options-UI strings (see `Settings.LocaleEN.md`). `Mod.OnLoad` registers both with
`GameManager.instance.localizationManager.AddSource("en-US", ...)`; the manager merges every source registered for the active locale.
More languages are further `IDictionarySource` implementations.

## Asset key pattern
Verified against `PrefabUISystem.GetTitleAndDescription` (PrefabUISystem.cs:1498-1528, 1.6.0f1):
- plain assets: `Assets.NAME[<prefab name>]` / `Assets.DESCRIPTION[<prefab name>]`;
- `Assets.UPGRADE_NAME` / `UPGRADE_DESCRIPTION` are read only for a ServiceUpgrade prefab; the mod ships none.

## Rules the file keeps
- Every `SkiResort.Hub.*` text equals its `src/hub/hubText.ts` tuple. Rule R4 of `mod/SkiResortUI/tools/check-bundle.js` fails the UI
  build on a difference. The TS fallback shows only while a key is missing. Editing this file re-runs the UI build (it is an input of the
  csproj's `SkiUiBuild` target).
- Glyphs: a `SkiResort.Hub.*` value may use printable ASCII plus the allowlist `SAFE_GLYPHS` in `check-bundle.js` (`← → ↑ ↓ … · – × ° ² ≈`
  and NBSP; no em dash). The list is the set that renders in every in-game font chain. Keys outside `SkiResort.Hub.*`
  (`SkiResort.LiftTooltip.*`, `SkiResort.Tooltip.*`) are not glyph-checked; `ALIGN_STRAIGHT`'s em dash renders correctly in game, so
  `ALIGN_FACING_AWAY` reuses it.
- Tooltips (`*TIP*`) are at most 25 words (`tools/test-hub-rules.js` T18.4). Exemption: `MONEY_TIP[Lifts]` and `MONEY_TIP[LiftsTypes]` carry
  live figures ({BASE} / {PERKM}, {FULL}, {REDUCED}) and the billing rule; each is capped at its length (42 and 44 words, `wc -w`) and
  `tests/ResortBatch` CatalogGuardTests CG-T8 (g) fails if either grows.
- The numbers baked into `BUILD_TIP_SCHOOL` / `PATROL` / `LUXURY`, `PROBLEM_TIP[SummitLodgeNotLiftServed]`, `RECEIPT_TIP[LodgeBase]` and
  `RECEIPT_TIP[Lifts]` are SkiCatalog's: CatalogGuardTests CG-T2..T6 derive them from the catalog, so each of those keys stays a
  single-line `{ "key", "value" },` entry.
- Keep every entry unique: a duplicate key throws inside `ReadEntries` and `LocalizationManager.LoadLocaleSource` drops this whole source.

## Asset-name scheme
Find It lists every StaticObjectPrefab, pillar, service building, upgrade and surface under the title `GetTitleAndDescription` picks, and
falls back to the raw prefab name split at capitals ("Ski Lift Tower FG2"). So every prefab a player can meet has a NAME and a DESCRIPTION
here, deployed and to-import alike (a key whose prefab does not exist yet is inert). Never shown, so no key: pieces, sections, effects,
meshes.
- Order: family first, then size, then the variant in parentheses ("Gondola Tower 16 m", "Ski Rack, Freestanding, 12 pairs + 2 boards
  (Red)"). Test objects say so ("Ski Resort — import probe B: Sheet (delete after test)").
- Parts a player can meet (pickers, station towers, cable lanes and stubs, sign sets, fence lane, run-edge groups, empty slot) put the
  variant first and drop the "Ski Resort — <kind>:" prefix; test objects keep it.
- Station buildings say the lift type and the end ("Fixed-Grip Chairlift: Base Station", "Gondola: Base Station"); the tram says "Aerial
  Tram: Base Terminal" / "Top Terminal", never "Tram Station" (a stranger reads the city tram).
- `hubText.ts` ASSET_NAME follows these names (gate R4). Internal prefab names are untouched: deployed ones are save identity.
- The deployed `.loc` files of the base and top station carry different NAME and DESCRIPTION texts; this source loads after them and wins.

## Run drawing feedback keys (`SkiResort.Tooltip.*`, `SkiResort.SlopeBand[*]`)
- Read by `SkiRunTooltipSystem` through `LocalizedString.Id(key)` (LocalizedString.cs:39-42); keys are free-form strings, the bracketed
  band suffix is the `SlopeBand` member name.
- Pitch, drop and patch labels sit in front of IntTooltip values whose units ("percentage", "length") the UI formats, so the labels carry
  no number or unit.
- The rating is the crux of the easiest line, so `CRUX_PITCH` ("Hardest unavoidable pitch") replaces `STEEPEST_PITCH`; `STEEPEST_PATCH`
  labels the old statistic when it is shown as an avoidable patch. `APPROX_PITCH` replaces `CRUX_PITCH` when the run outline falls apart
  (`Readout.Connected` false): the number is then the P90 fallback, not an unavoidable pitch.
- Band thresholds are 6 / 25 / 40 / 60 % (`SkiSlopeClassifier`). Flat does not say "unrated": band -1 in the hub is "Being rated"
  (`SkiResort.Hub.PENDING`), so no two states share the word.
- `RUN_FEE` labels the one-time run fee row (a money IntTooltip value). `SLOPE_AVG_MAX*` are the live grade lines; the variants add the
  deciding factor of the difficulty layer (`SkiRunRater.NoteLine`; W is a "length" unit value the UI converts).

## Glade tooltip keys (`SkiResort.GladeTooltip.*`)
- Read by `SkiGladeTooltipSystem` through `LocalizedString.Id(key)`, built once in OnCreate. Keys: `TARGET[Interior|TreeLine]`, `LEVEL`,
  `LEVEL_NAME[1..8]` (also `SkiGladeToolSystem.Tuning.LevelNames` for the log), `TREES_NOW`, `TREES_TARGET`.
- PLANT / FELL / REMOVE / BLOCKED_SPOTS / LEVEL / TREES_NOW / TREES_TARGET label IntTooltips (unit "integer"), so they carry no number.
  REMOVE is "Planted trees to remove": members are removed to lower the density, nothing is replaced.
- COMPLETE ("the run is already at this tree density"; level 1 plants nothing) shows only when Trees now = Target. COMPLETE_BLOCKED is a
  complete plan below its target (learned blocked spots used up the stream); COMPLETE_FIXED one above it (trees left inside may not be
  felled).

## Hub keys (`SkiResort.Hub.*`)
- The UI module renders every key through `translate(key, englishFallback)`; the TS fallback carries the same text.
- Keys with {X} placeholders are rendered by LocalizedString in TS, never through `translate()` (gate rule 5), except `RESORT_DEFAULT` and
  `LIFT_DEFAULT`, which C# formats (`SkiResortStatsSystem`, `SkiRunBindTriggerSystem`, "{N}" replaced) and sends as a finished name.
- `RUN_DEFAULT[*]` are the default run names: `SkiRunCatalogSystem` reads them from the ACTIVE dictionary (so a translation applies) and
  replaces "{N}" with the run's number by top height; the UI receives the finished string as RunRow.name.
- `LIFT_COST_REDUCED[NoPower]`: power no longer lowers the bill (`SkiMoneyRules.LiftUpkeepBillingOf`), so a reduced row whose status is
  No power is billed reduced only because its ski area has no counted run (NoOpenRun). `hubRules.ts liftCostReason` still picks this key
  for that row, so the key keeps its slot and names the true reason.
- Road problem tips: the tooltip's first line is the row, so "it" names the building. The deck side is the lot front, and the game links a
  road within 8.4 m of the front point.

## Lift tooltip keys (`SkiResort.LiftTooltip.*`)
Read by `SkiLiftTooltipSystem` through `LocalizedString.Id(key)`, built once, never `Id(key, args)` per frame. ALIGN_OFFSET / ANGLE /
DISTANCE / RISE and BULLDOZE_COUNT / LENGTH label FloatTooltip / IntTooltip values whose units the UI formats.
