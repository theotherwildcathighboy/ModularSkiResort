# SkiResortUI: the Ski Resort hub (UI module)

The in-game "Ski Resort" hub of the Modular Ski Resort mod: a TypeScript/React module that the
game loads from `SkiResortMod.mjs` + `SkiResortMod.css` beside `SkiResortMod.dll`.
Created 2026-10-01 (Round U1). Authority:

- `docs/research/resort-hub-understand.md` section 5 (build contract) and section 2.A (toolchain facts).
- Since 2026-10-03 (contract 2, Rounds U2/M1): `docs/research/hub-u2-spec.md` sections 2 and 4.
- Since 2026-10-04 (contract 3, Round E1 "hub v3"): `docs/research/econ-round/spec/ch6-hub-ui.md` and
  `docs/research/econ-round/design/hub-v3-design.md`.

## Where the hub is

The player builds everything from the Ski Resort panel. To open it, use the Ski Resort button at the top left of the screen
(Settings ▸ Hub can hide it), or the mod's button in the game's mod panel. Start with a Ski Area
Welcome Sign. When you select a sign, the panel opens.

Since Round M1 (hub-u2-spec.md 6.1, OD-10), none of the mod's items is in the vanilla toolbar, and
the Ski Resort tab is gone. The panel is the only way to build. The sign's own upgrade strip holds
the lodge only. The top-left button is not rendered in the gamepad layout. Whether the
game's mod panel shows the hub there is unverified. This is accepted (DECISIONS 130: gamepad is not a design
constraint).

License: MIT, like the rest of the mod. We wrote every file here for this repo. We read the official
Colossal Order template (game install, `Cities2_Data/Content/Game/.ModdingToolchain/
npx-create-csii-ui-mod/template/`) only as a reference for the settings the game
requires. Each config file says in its header what it derives from. Nothing comes from
ParkManager (GPL-3.0). The build copies the `cs2/*` type declarations from the game install
into `types/`. We never commit them.

## Commands

| Command (in `mod/SkiResortUI`) | What it does |
|---|---|
| `npm ci` | Install the locked packages (fresh clone). `npm install` only to change a pin. |
| `npm run build` | sync-types, webpack into `build/`, then the `check-bundle` gate. |
| `npm run typecheck` | sync-types, then `tsc --noEmit` (fast type check, no bundle). |
| `npm run check` | Run the gate again on the current `build/` (rewrites or deletes `build/.gate-pass`). |
| `npm run contract:lock` | Rewrite `tools/contract.lock` (binding names and shape fields) after a deliberate `HUB_CONTRACT` bump. Review its diff (hub-u2-spec.md 4.7 rule 3). |
| `node tools/check-bundle.js --contract-only` | The source and contract checks (rules 1-7, 10-18: C#/TS names and shapes, the lock, locale parity, inputs, glyphs, panel-binding scope, hook order, placeholder entries, untranslated text, header/footer focus, build-item and enum parity with C#, the UI rules self-test) without a bundle. Never writes `build/.gate-pass`. `--src=<copy of src>`, `--cs-dir=`, `--hub-cs=`, `--locale=` and `--lock=` point it at fault copies. |
| `node tools/test-hub-layout.js` | T114 "never a scrollbar": after `npm run build`, headless Chrome / Edge over the harness at three window heights and text scale 1.0 / 1.25. FAILS on any content overflow (`--shots=<dir>` for screenshots). About 1 min on 12 parallel browsers; `--quick` = 1080 only (30 s); one run at a time per machine (a lock in %TEMP%). |
| `node tools/test-hub-rules.js` | Rule R18 alone: the literal oracles of `src/hub/hubRules.ts` (76 checks in Round E1, 80 after the E1 fixer). |
| `npm run dev` | sync-types, then `webpack --watch --env deploy`: every rebuild is gated, and only a passing bundle is copied into the Mods folder. Needs `SkiResortMod.dll` already deployed. For live reload, start the game with `--developerMode --uiDeveloperMode`. |

Normally you do not run npm yourself. `dotnet build` in `mod/SkiResortMod` (game closed) runs
`npm run build` whenever a UI input changed. It also compiles the bundle's build stamp into the DLL and
deploys both. `dotnet build -p:SkipUi=true` builds the DLL without the UI. Without
`node_modules` the DLL still builds and deploys, with one warning that the UI is absent.

## Browser harness (no game)

Run `node harness/serve.js`, then open http://localhost:5173/harness/. The page mounts `build/SkiResortMod.mjs` in a plain browser with
stand-ins for the game modules and sample scenarios. Wording, layout and view flow work without launching the game.
See `harness/README.md` for what is faithful and what is not. Nothing in `harness/` ships. The gate does not read it.

## Dependencies (pinned exactly; `package-lock.json` is committed)

| Package | Version | Why |
|---|---|---|
| react | 18.3.1 | Only for `react/jsx-runtime`, which is bundled. At runtime `react` itself is the game's `window.React` (18.3.1). |
| typescript | 4.8.4 | Type checking and TSX, the version the game's template pins. No TS 4.9+ syntax (`satisfies`). |
| webpack | 5.112.2 | Bundler: one ES module, window externals. Above the template's 5.97.1 for its audit fixes. |
| webpack-cli | 4.10.0 | The `webpack` command. |
| ts-loader | 9.5.1 | TS to JS inside webpack, with type checking on. |
| css-loader | 6.7.2 | CSS modules (class names `sr-<name>_<hash>`). |
| sass | 1.69.5 | SCSS compiler. Kept at 1.69.5, before the legacy-API deprecation warnings. |
| sass-loader | 13.2.0 | SCSS inside webpack. |
| mini-css-extract-plugin | 2.7.0 | Writes `SkiResortMod.css` next to the `.mjs`. |
| terser-webpack-plugin | 5.6.1 | Minifier. It also writes the manifest banner (see `webpack.config.js`). Above the template's 5.3.10 for its audit fixes. |
| @types/react | 18.0.26 | React typings. The game's `cs2/*` declarations import from `react`. Template pin. |
| @types/react-dom | 18.0.9 | Part of the game toolchain's set. We do not import react-dom. |

These are the game toolchain's pins (research 2.A.3), except webpack and terser-webpack-plugin. The
template's webpack 5.97.1 and terser-webpack-plugin 5.3.10 carried build-time advisories
(serialize-javascript, webpack buildHttp SSRF, fast-uri through ajv). We raised them in
2026-11 to clear them, and `npm audit` reports 0. The bundle gate (`check-bundle`: banner, scoping,
smoke) and the hub rule tests passed on the new versions.

## Layout

```
mod.json              id/author/version: read only by webpack, for the banner (the game never reads it)
webpack.config.js     the bundle, the banner, the build stamp, --env deploy
tsconfig.json         strict TS; `import type` is enforced for types
tools/sync-types.js   game install -> types/ (git-ignored), every build
tools/check-bundle.js THE build gate (what it checks: header of the file)
tools/test-hub-rules.js  rule R18: literal oracles of src/hub/hubRules.ts, run in Node (Round E1)
tools/contract.lock   the binding contract the gate last accepted (contract 3: 237 lines)
tools/user-env.js     reads CSII_* from the Windows User scope, like the C# build
src/index.tsx         the registrar: append-only, never throws
src/bindings.ts       THE C# <-> UI contract: group, names, types, fallbacks, triggers, enums, build ids
src/l10n.ts           locale keys + English fallbacks, useT()
src/cs2shim.ts        LocalizedNumber/String/Bounds/Fraction + DropdownItem (typings defect workaround)
src/globals.d.ts      __BUILD_STAMP__, *.module.scss
src/base.module.scss  always imported, so the .css always exists (load-bearing)
src/hub/HubRoot.tsx   always mounted at "Game": handshake, panel mount, panel error boundary
src/hub/HubLauncher.tsx  Universal Mod Panel square + top-left floating button
src/hub/hubState.ts   open/close (C# owns it; local fallback while C# is absent), connection state, the view, hoverRun
src/hub/ErrorBoundary.tsx, src/hub/report.ts   containment and reporting of UI errors
src/hub/HubPanel.tsx  the panel: draggable cs2/ui Panel (header / content / footer), body by link state, one ErrorBoundary per part
src/hub/HubHeader.tsx, RenameField.tsx   the title-bar ski-area switcher and the one <input>
src/hub/StatusBlock.tsx   the status line and the three-slot problems band (contract 3)
src/hub/ViewTiles.tsx     the five tiles = the navigation (Attraction, Runs, Lifts, Guests, Finances; T77 renamed the Money tile, the file and keys keep "Money")
src/hub/OverviewView.tsx  the score receipt and the mountain (difficulty bar and legend moved here)
src/hub/RunsView.tsx, RunList.tsx, TreeControls.tsx   tree tool rows, piste-map toggle, run rows
src/hub/LiftsView.tsx, LiftTable.tsx   two-line lift rows and the totals
src/hub/GuestsView.tsx, MoneyView.tsx   guests, tickets, running costs, the one net (MoneyView = the Finances view)
src/hub/StartCard.tsx     the panel's content while the city has no Welcome Sign
src/hub/hubRules.ts   pure display rules (joins, problem band, dot, footer, variants, bars); no React
src/hub/resortData.ts pure selection and filter helpers (which rows belong to the shown ski area)
src/hub/layoutReport.ts  the uiLayout dev aid (measured panel heights, each distinct text sent once)
src/hub/BuildStrip.tsx    the footer Build strip (nine tiles, two one-string lines, the one dot)
src/hub/hubText.ts    the panel's locale keys + English fallbacks (must equal root LocaleEN.cs)
src/hub/buildItems.ts build-item icons and ordering, run-kind map
src/hub/parts.tsx     Num / numArg (LocalizedNumber), Tip/TipLines (cs2/ui Tooltip), InfoLine, FlexBar, marks, cx
src/hub/fit.tsx       T114: Fold (one open per view), useFitPage + Pager (lists), useViewPager (a view taller than the panel pages)
src/hub/hub.module.scss  the panel's styles
```

Contract 3 (Round E1, 2026-10-04): `ResortDashboard.tsx` and `ResortLists.tsx` are
gone. The hub is a status line, a problems band, five tiles and the view of the selected tile.
The spec is `docs/research/econ-round/spec/ch6-hub-ui.md`. The design is
`docs/research/econ-round/design/hub-v3-design.md`.

## Rules for components (the gate enforces most of them)

- **Hooks**: append only, at `UniversalModMenu`, `GameTopLeft` and `Game`. Do not use
  `moduleRegistry.extend`/`override`. Do not use internal `game-ui/...` paths (OD3 is deferred).
- **cs2 imports**: import named runtime exports only. Import types with `import type`.
  The typings declare more names than the game exports, and a missing name is `undefined` in
  game. The gate checks every value import against the installed game's export table. For
  example, `Unit` comes from `cs2/l10n`, not `cs2/ui`, and `InfoRow`/`InfoSection` are
  `PanelSectionRow`/`PanelSection`. Import `LocalizedNumber`, `LocalizedString`,
  `LocalizedBounds`, `LocalizedFraction` and `DropdownItem` from `src/cs2shim.ts`. The game's
  typings hide those five behind same-named interfaces, so a direct import does not compile
  (see the shim's header). Runtime `cs2/ui` exports (1.6.2f1, 22): Button,
  ConfirmationDialog, DialogContext, DialogRenderer, DialogStack, Dropdown, DropdownItem,
  DropdownToggle, FloatingButton, FormattedParagraphs, FormattedText, Icon, MarkdownRenderer,
  MarkupRenderer, MenuButton, Panel, PanelFoldout, PanelSection, PanelSectionRow, Portal,
  Scrollable, Tooltip.
- **Bindings**: always go through `src/bindings.ts` (every binding has a fallback). Subscribe to
  everything except `hubOpen$`/`contract$`/`buildStamp$`/`showHubButton$` only in components that
  mount while the panel is open. C# computes the resort statistics only while one of `build`,
  `resorts`, `resortLive`, `runs`, `runLive`, `lifts`, `problems` or `city` is observed (contract 3
  removed `lodges`). `selectedResort`, `glade`, `pisteMap` and `follow` are panel bindings too
  (the last two without Demand). Only the leaf components that show their numbers read the live
  arrays (`resortLive`, `runLive`, `problems`), so a snow, guest or money push never re-renders the
  frame (ch6 6.15).
- **Appended components**: each is a plain, hook-free function that returns an `ErrorBoundary`
  element, and every `append` sits in its own try/catch. The gate's smoke test calls each one and
  runs the registrar with a throwing `append` (U1 fixer round).
- **CSS scope**: use only `*.module.scss` files. Every selector must be able to match only inside
  our own subtree. The rightmost part of the selector carries an `.sr-` class, or an `.sr-` class is followed by a
  descendant or child combinator. So `:global(div)`, game class names and `.sr-x ~ div` all fail
  the gate. `@keyframes`/`@font-face` names must start with `sr-`. Our CSS shares one document with
  the game and every mod, and no error boundary can contain a leaking rule (U1 fixer round).
- **Gameface 2.2.1**:
  - flex only (no grid, no `<table>`, no `<select>`), no `:not()`, no `space-evenly`, no auto
    margins
  - longhand properties whenever a value uses `var()`
  - `pointer-events: auto` on our own floating containers (the game screen root has `none`)
  - tooltips through cs2/ui `Tooltip` only. The Button `tooltipLabel` is an overflow tooltip:
    it shows only while the button's content overflows (installed 1.6.2f1 index.js: base Button
    @629394, overflow test `scrollWidth > clientWidth` then `E&&c&&(D=Tooltip…)` @630906), so it
    is not a general hint (corrected 2026-10-01 by U1-C, and the integrator re-checked the offsets).
- **Text**: `SkiResort.Hub.*` keys in the root `LocaleEN.cs`, shown through `useT()`/`translate`.
  Glyphs allowed: ASCII plus ← → ↑ ↓ … · – × ° ² ≈ and NBSP (research 2.F.2). We added ≈ in contract 2.
  It is present in the game's Overpass and Noto Sans. A text with `{X}` placeholders goes through
  `LocalizedString`, never `translate()` (gate rule 5).
- **Contract 2 rules (hub-u2-spec.md 4.7)**:
  - `<input>` only in `src/hub/RenameField.tsx` (with `stopPropagation` and a `useLayoutEffect` cleanup that calls `.blur()`)
  - no JSX `title=` (use `Tip`)
  - no CSS `gap` / `row-gap` / `column-gap`
  - any class whose name contains `blind` declares `height: 0` and `visibility: hidden`
  - every `Media/...` path must exist in the installed game
- **Fixer-round gate rules (2026-10-03, finding u2-panel-6, `lintFixerRules` in the gate)**:
  - R11: `resorts$`, `resortLive$`, `selectedResort$`, `runs$`, `runLive$`, `lifts$`, `problems$`,
    `city$`, `build$`, `glade$`, `pisteMap$`, `follow$` (contract 3) are referenced only in the
    panel-mounted files listed in `PANEL_FILES` (a new panel file is added there on purpose).
  - R12: no hook after an early return, inside a condition or a loop, or on the right of `&&`,
    `||`, `??`.
  - R13: an entry with `{X}` (HUB_FMT) appears only as `<LocText entry={HUB_FMT.x} />`. The map is
    never aliased or spread.
  - R14: no JSX text, JSX child string or visible prop string (`label`, `tip`, `tooltip`,
    `placeholder`, `alt`, ...) with a letter: every word goes through a locale key.
  - R6 / R7 also catch `createElement("input")`, `jsx(...)` and a `title` in a spread or props
    object literal.
- **Round F6 gate rule (2026-10-03, log-audit A1, `lintPanelEdgeFocus` in the gate)**:
  - R15: a focusable cs2/ui button (`Button`, `FloatingButton`, `MenuButton`, `DropdownToggle`,
    `Dropdown`) rendered in a cs2/ui `<Panel>` `header` or `footer` passes `focusKey={FOCUS_DISABLED}`
    imported from `cs2/input`. This holds when the button is rendered directly or through our own
    components. Those slots host ONE focus child, and each further button logs "Cannot register second focus key" to UI.log on
    every open and close (field test 6: 106 lines). The rule also fails when no header / footer slot is
    found at all.
  - R15, extended by the F6 fixer (2026-10-04): a spread placed after `focusKey` fails (it can
    override it). A cs2/ui `PanelSection`, `PanelFoldout` or `PanelSectionRow` in those slots needs
    a literal `disableFocus` (without it each is a FOCUS_AUTO focus div that registers like a
    button). A `PanelSectionRow` must also have no `link` and no spread. Helper functions are
    followed as well as components: one called in the slot (`{tile(...)}`) and one passed by name
    (`.map(tile)`), when declared in the same file or imported from a relative path. Still not
    followed: JSX kept in a variable outside the slot, a component passed as a prop value,
    `createElement` calls, and functions reached through a property (`parts.tile()`).
- **Round E1 gate rules (2026-10-04, econ-round spec ch6 §10, `checkBuildItems`, `checkEnumParity`,
  `checkHubRules` in the gate)**:
  - R16: C# `SkiResortHubUISystem.ItemIds` / `ItemPrefabNames` / item constants (`ItemSign`,
    `ItemLodge`, `ItemRunFirst..ItemRunLast`, `ItemLift`, `ItemBase`, `ItemTop`, `ItemCount`) agree
    with `BUILD_ITEM_IDS`, the `BuildItemId` union, `BUILD_ITEM_PREFAB` and `BUILD_GROUPS` (every id
    exactly once): an id only one side knows would be dropped silently. Keep them literal on both sides.
  - R17: eight TS const objects (`RUN_KIND`, `RESORT_STATUS`, `OPEN_STATE`, `SEASON_HINT`,
    `NEXT_STEP`, `LIFT_STATUS`, `PROBLEM`, `TICKET_GATE`) hold the same names and values as their C#
    enums (`EntityRef.cs`, `HubRules.cs`, `SkiMoneyRules.cs`), both directions. `BAND` and
    `LIFT_STATE` have no C# enum.
  - R18: `tools/test-hub-rules.js` runs `src/hub/hubRules.ts` in Node against literal oracles (76
    checks). `hubRules.ts` loads values only from `../bindings` (React-free, binding-free, pure).
  - Proven on scratch fault copies (`--contract-only --src= / --cs-dir= / --hub-cs=`). The results:
    - one R17 error for `HubProblemCode.Factor = 99`
    - two R16 errors for swapped `ItemPrefabNames[2..3]`
    - one for `ItemRunLast = 4`
    - two for swapped `ItemBase` / `ItemTop`
    - an R16 set error and an R17 `RunKind.Natural` error for contract-3 TS against the contract-2 C#
    - an R18 error for a wrong oracle and for a `cs2/api` value import in `hubRules.ts`
- **Tooltips on our components**: `Tip` (cs2/ui Tooltip) attaches its listeners through the game's
  DOMNodeModifier, which reaches a host element (`<div>`) or a cs2/ui component, never our own
  function components. A component that needs a tooltip takes a `tip` prop and puts the `Tip` around
  its own root div (`InfoLine`, `SubLine`, `FlexBar`).
- **Images**: in TSX, `Media/...` (game media) or `assetdb://...`. Our own png/svg imports land
  in `SkiResortMod-img/`, because `coui://ui-mods/` is one host shared by every mod.

## Build stamp

One stamp per UI build. `SkiResortMod.csproj` (target `SkiUiBuild`, which runs only when a UI
input changed) generates
`yyyyMMdd-HHmmss-xxxxxx` (UTC) and passes it to webpack in `SKI_BUILD_STAMP`. Webpack compiles it
in as `__BUILD_STAMP__` (`BUNDLE_STAMP` in `src/bindings.ts`), writes it into the banner's
`Build:` line, and writes it to `build/.ui-stamp`.

The target `SkiBuildStamp` compiles that file's value into `SkiResortMod.BuildStamp.Value`, which
is a generated file in `obj/`. So the DLL and the bundle deployed beside it always match. When
the UI is unchanged, the C# build skips npm and reuses the stamp of the bundle in `build/`.

The other cases:
- a DLL built without the UI carries `no-ui`
- a standalone `npm run build`/`npm run dev` bundle carries `ui-...`.

The handshake: `HubRoot` fires `uiHello(HUB_CONTRACT, BUNDLE_STAMP)` once per UI load, when the
C# bindings are present, and C# compares both with its own values.

A standalone `npm run build` whose gate PASSED leaves `build/.ui-stamp` and `build/.gate-pass`
newer than every input, so the next `dotnet build` skips `SkiUiBuild` and compiles that `ui-...`
stamp into the DLL. DLL and bundle still match. To get an MSBuild stamp again, delete `build/`.

## The gate marker (`build/.gate-pass`)

Added in the U1 fixer round (finding pipeline-safety-1). Before it, a failed gate blocked only
the build it failed in. Webpack had already written the rejected bundle and its `.ui-stamp`,
newer than every input. So the next `dotnet build`, or a publish (which rebuilds), skipped
`SkiUiBuild` and deployed the rejected bundle. The stamp handshake could not catch it, because
the DLL and the bundle carried the same stamp.

Now `build/.gate-pass` exists only while the bundle in `build/` has passed the gate:
- webpack deletes it whenever it emits a bundle
- `check-bundle.js` deletes it when it starts, and writes it (content = the stamp) only on PASS,
  so a FAIL, a crash or a killed run leaves none
- the csproj target `SkiUiBuildFailedHint` deletes it and `.ui-stamp` on any UI build failure.

The C# build reruns the UI build while the marker is missing (it is an `Output` of
`SkiUiBuild`). `SkiBuildStamp` and `SkiUiDeploy` refuse a bundle whose marker is missing or names
another stamp, and `npm run dev` checks it before every copy. `SkiUiDeploy` also refuses a
`build/` whose stamp differs from the one compiled into the DLL. An error there leaves the new DLL
deployed without a bundle (`UI module present: False` in the log), never with a wrong one.
