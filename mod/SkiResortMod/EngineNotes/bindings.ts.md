# bindings.ts: the C# <-> UI binding contract, what the build gate reads, and the UI bundle rules consumers rely on

`bindings.ts` is the single source of truth for the contract between the Ski Resort hub UI and `SkiResortHubUISystem` (group
`skiResort`). C# must register exactly these names with these shapes. Change a name, a type or a field here and in C# together, bump
`HUB_CONTRACT` in both, run `npm run contract:lock` and review the diff of `tools/contract.lock`.

## What the build gate reads from the file

`tools/check-bundle.js` parses the TypeScript AST, so these shapes must stay literal:

- `HUB_CONTRACT = <integer literal>`, compared with C# `HubContract`.
- `BINDING` / `TRIGGER = { name: "name", ... }`. Every name must be registered in C# with a string literal:
  `new ValueBinding<T>(Group, "name", ...)`, `new RawValueBinding(Group, "name", ...)`, `new TriggerBinding<...>(Group, "name", ...)`,
  and nothing else may be registered.
- `CORE_NAMES = [ "...", ... ]` (string literals): the names frozen since contract 1. A CORE name may only gain OPTIONAL fields in its
  shape; it is never renamed, retyped or removed.
- Every `export interface X` below: the field list of `writer.TypeBegin("SkiResortMod.X")` in C#.
- Every `bindValue<T>(GROUP, BINDING.x, FALLBACK.x)`: the TS type of binding x.
- Every `hubTriggers` entry `(a: A, b: B): void => fire(TRIGGER.x, ...)`: the argument list of x.
- Rule R16: `BUILD_ITEM_IDS`, the `BuildItemId` union and `BUILD_ITEM_PREFAB` (with `src/hub/buildItems.ts` `BUILD_ITEMS`,
  `BUILD_GROUPS` and `BUILD_ITEM_ICON`) against C# `Systems/SkiCatalog.cs` (the NAMES and BUILD-ITEMS blocks).
- Rule R17: the const objects `RUN_KIND`, `RESORT_STATUS`, `OPEN_STATE`, `SEASON_HINT`, `NEXT_STEP`, `LIFT_STATUS`, `PROBLEM`,
  `TICKET_GATE`, `SKI_DAY_STATE` (literal numbers) against their C# enums (`EntityRef.cs` `RunKind`, `HubRules.cs`,
  `SkiMoneyRules.TicketGate`) and `SKI_DAY_LIMIT` (`SkiDayRules.cs` `SkiDayLimit`, a top-level enum). `BAND` and `LIFT_STATE` have no
  C# enum (literal ints in C#) and are not compared.

## Rules every consumer relies on

Verified in the installed 1.6.2f1 `Cities2_Data/Content/Game/UI/index.js` unless marked.

- Every `bindValue()` passes a fallback that is NOT undefined. `bindValue(group, name, fb)` builds a binding whose starting value is
  `fb` (class `ml`, `index.js@419651`); `connect()` throws "'<group>.<name>.update' was not called after subscribe!" only while the
  value is still undefined after subscribing (`index.js@421257`). So a missing or not yet registered C# binding renders the fallback
  instead of throwing (check-bundle's smoke test enforces it). A DLL of an older contract therefore shows the fallbacks for every new
  name; the panel reads them only when `contract` is `HUB_CONTRACT` (on a mismatch only CORE is read).
- C# pushes a value as soon as JS subscribes (`Colossal.UI.Binding` `ValueBinding` / `RawValueBinding`), so `contract$` reads the DLL's
  contract synchronously when the hub system exists and stays `CONTRACT_ABSENT` (0) when it does not.
- A JS binding connects on its first subscriber and disconnects with its last (`index.js@421257` subscribe/dispose), so C#'s
  `observerCount` counts binding objects, not components. Subscribe to every panel binding (`build$`, `glade$`, `resorts$`,
  `resortLive$`, `selectedResort$`, `runs$`, `runLive$`, `lifts$`, `problems$`, `city$`, `pisteMap$`, `follow$`, `history$` without
  Demand) ONLY in components that mount while the panel is open (gate rule R11): closing the panel unsubscribes them, and C# stops
  computing. The stats system's Demand follows the observers of build, resorts, resortLive, runs, runLive, lifts, problems and city;
  pisteMap and follow are panel bindings without Demand. `hubOpen$`, `contract$`, `buildStamp$` and `showHubButton$` are subscribed
  always (launchers, root).
- Triggers fire the cohtml event "<group>.<name>" (cs2/api trigger). An exception in a C# trigger body is caught and logged to the
  vanilla UI log, not ours, so C# wraps every body in try/catch and writes its own "!!!" line.
- Entities travel as `{ index, version }` both ways (`UnityWriters` / `UnityReaders`); `Entity.Null` is `{ index: 0, version: 0 }`
  (`EntityRef.IsNull`).
- Rows written by C# with `JsonWriter.TypeBegin` carry an extra `__Type` field; ignore it. Every `TypeBegin` name is
  `SkiResortMod.<TS interface name>`.
- Conventions: -1 = unknown (rendered "-" by `numArg`); the two arrays of a pair (`resorts` / `resortLive`, `runs` / `runLive`) are
  joined by key (`sign`, `entity`), never by index alone (`src/hub/hubRules.ts` `liveOf`, `runLiveMap`); costs arrive positive;
  `ProblemRow.factor` is an `EfficiencyFactor` enum name the UI translates; `BuildItem.hint` is a locale key that feeds footer line 2
  only.

## Patch day

Grep the new `index.js` for every name in `VANILLA_NAMES_USED`.
