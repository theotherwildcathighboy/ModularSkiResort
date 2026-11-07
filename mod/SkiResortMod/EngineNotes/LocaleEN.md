# LocaleEN: how the English text source is keyed, gated and worded

`LocaleEN.cs` (root) is the `IDictionarySource` for the asset pack's prefab names and descriptions plus the hub, run-drawing, glade and
lift tooltip texts. `Settings/LocaleEN.cs` holds the options-UI strings (see `Settings.LocaleEN.md`). `Mod.OnLoad` registers both with
`GameManager.instance.localizationManager.AddSource("en-US", ...)`. The manager merges every source registered for the active locale.
More languages are further `IDictionarySource` implementations.

## Asset key pattern
Verified against `PrefabUISystem.GetTitleAndDescription` (PrefabUISystem.cs:1498-1528, 1.6.0f1):
- Plain assets: `Assets.NAME[<prefab name>]` / `Assets.DESCRIPTION[<prefab name>]`.
- `Assets.UPGRADE_NAME` / `UPGRADE_DESCRIPTION` are read only for a ServiceUpgrade prefab. The mod ships none.

## Rules the file keeps
- Every `SkiResort.Hub.*` text equals its `src/hub/hubText.ts` tuple. Rule R4 of `mod/SkiResortUI/tools/check-bundle.js` fails the UI
  build on a difference. The TS fallback shows only while a key is missing. Editing this file re-runs the UI build (it is an input of the
  csproj's `SkiUiBuild` target).
- Glyphs: a `SkiResort.Hub.*` value may use printable ASCII plus the allowlist `SAFE_GLYPHS` in `check-bundle.js` (`← → ↑ ↓ … · – × ° ² ≈`
  and NBSP, with no em dash). The list is the set that renders in every in-game font chain. Keys outside `SkiResort.Hub.*`
  (`SkiResort.LiftTooltip.*`, `SkiResort.Tooltip.*`) are not glyph-checked. `ALIGN_STRAIGHT`'s em dash renders correctly in game, so
  `ALIGN_FACING_AWAY` reuses it.
- Tooltips (`*TIP*`) are at most 25 words (`tools/test-hub-rules.js` T18.4). Exemption: `MONEY_TIP[Lifts]` and `MONEY_TIP[LiftsTypes]` carry
  live figures ({BASE} / {PERKM}, {FULL}, {REDUCED}) and the billing rule. Each has a cap at its current length (42 and 44 words, `wc -w`).
  `tests/ResortBatch` CatalogGuardTests CG-T8 (g) fails if either grows.
- The numbers in `BUILD_TIP_SCHOOL` / `PATROL` / `LUXURY`, `PROBLEM_TIP[SummitLodgeNotLiftServed]`, `RECEIPT_TIP[LodgeBase]` and
  `RECEIPT_TIP[Lifts]` are SkiCatalog's. CatalogGuardTests CG-T2..T6 derive them from the catalog. So each of those keys stays a
  single-line `{ "key", "value" },` entry.
- Keep every entry unique. A duplicate key throws inside `ReadEntries`, and `LocalizationManager.LoadLocaleSource` drops this whole source.

## Asset-name scheme
Find It lists every StaticObjectPrefab, pillar, service building, upgrade and surface. It uses the title that `GetTitleAndDescription`
picks. It falls back to the raw prefab name split at capitals ("Ski Lift Tower FG2"). So every prefab a player can meet has a NAME and a
DESCRIPTION here, deployed and to-import alike. A key whose prefab does not exist yet is inert. These items are never shown, so they get
no key: pieces, sections, effects, meshes.
- Order: family first, then size, then the variant in parentheses ("Gondola Tower 16 m", "Ski Rack, Freestanding, 12 pairs + 2 boards
  (Red)"). Test objects say so ("Ski Resort — import probe B: Sheet (delete after test)").
- Parts a player can meet put the variant first and drop the "Ski Resort — <kind>:" prefix. These parts are pickers, station towers, cable lanes and stubs, sign sets, fence lane, run-edge groups and the empty slot. Test objects keep the prefix.
- Station buildings say the lift type and the end ("Fixed-Grip Chairlift: Base Station", "Gondola: Base Station"). The tram says "Aerial
  Tram: Base Terminal" / "Top Terminal", never "Tram Station", because a stranger reads it as the city tram.
- `hubText.ts` ASSET_NAME follows these names (gate R4). Internal prefab names are untouched: deployed ones are save identity.
- The deployed `.loc` files of the base and top station carry different NAME and DESCRIPTION texts. This source loads after them and wins.

## Run drawing feedback keys (`SkiResort.Tooltip.*`, `SkiResort.SlopeBand[*]`)
- `SkiRunTooltipSystem` reads them through `LocalizedString.Id(key)` (LocalizedString.cs:39-42). Keys are free-form strings. The bracketed
  band suffix is the `SlopeBand` member name.
- Pitch, drop and patch labels sit in front of IntTooltip values. The UI formats their units ("percentage", "length"). So the labels
  carry no number or unit.
- The rating is the crux of the easiest line, so `CRUX_PITCH` ("Hardest unavoidable pitch") replaces `STEEPEST_PITCH`. `STEEPEST_PATCH`
  labels the old statistic when the UI shows it as an avoidable patch. `APPROX_PITCH` replaces `CRUX_PITCH` when the run outline falls
  apart (`Readout.Connected` false). The number is then the P90 fallback, not an unavoidable pitch.
- Band thresholds are 6 / 25 / 40 / 60 % (`SkiSlopeClassifier`). Flat does not say "unrated". Band -1 in the hub is "Being rated"
  (`SkiResort.Hub.PENDING`), so no two states share the word.
- `RUN_FEE` labels the one-time run fee row (a money IntTooltip value). `SLOPE_AVG_MAX*` are the live grade lines. The variants add the
  deciding factor of the difficulty layer (`SkiRunRater.NoteLine`). W is a "length" unit value that the UI converts.

## Glade tooltip keys (`SkiResort.GladeTooltip.*`)
- `SkiGladeTooltipSystem` reads them through `LocalizedString.Id(key)` and builds them once in OnCreate. Keys: `TARGET[Interior|TreeLine]`,
  `LEVEL`, `LEVEL_NAME[1..8]` (also `SkiGladeToolSystem.Tuning.LevelNames` for the log), `TREES_NOW`, `TREES_TARGET`.
- PLANT / FELL / REMOVE / BLOCKED_SPOTS / LEVEL / TREES_NOW / TREES_TARGET label IntTooltips (unit "integer"), so they carry no number.
  REMOVE is "Planted trees to remove". The tool removes members to lower the density. It replaces nothing.
- COMPLETE ("the run is already at this tree density", and level 1 plants nothing) shows only when Trees now = Target. COMPLETE_BLOCKED is a
  complete plan below its target (learned blocked spots used up the stream). COMPLETE_FIXED is one above it (trees left inside may not be
  felled).

## Hub keys (`SkiResort.Hub.*`)
- The UI module renders every key through `translate(key, englishFallback)`. The TS fallback carries the same text.
- LocalizedString in TS renders keys with {X} placeholders, never through `translate()` (gate rule 5). The exceptions are `RESORT_DEFAULT`
  and `LIFT_DEFAULT`. C# formats these (`SkiResortStatsSystem`, `SkiRunBindTriggerSystem`, "{N}" replaced) and sends them as a finished
  name.
- `RUN_DEFAULT[*]` are the default run names. `SkiRunCatalogSystem` reads them from the ACTIVE dictionary (so a translation applies). It
  replaces "{N}" with the run's number by top height. The UI receives the finished string as RunRow.name.
- `LIFT_COST_REDUCED[NoPower]`: power no longer lowers the bill (`SkiMoneyRules.LiftUpkeepBillingOf`). So the bill of a reduced row whose
  status is No power is reduced only because its ski area has no counted run (NoOpenRun). `hubRules.ts liftCostReason` still picks this
  key for that row. So the key keeps its slot and names the true reason.
- Road problem tips: the tooltip's first line is the row, so "it" names the building. The deck side is the lot front, and the game links a
  road within 8.4 m of the front point.

## Lift tooltip keys (`SkiResort.LiftTooltip.*`)
`SkiLiftTooltipSystem` reads them through `LocalizedString.Id(key)`. It builds them once and never calls `Id(key, args)` per frame.
ALIGN_OFFSET / ANGLE / DISTANCE / RISE and BULLDOZE_COUNT / LENGTH label FloatTooltip / IntTooltip values. The UI formats their units.
