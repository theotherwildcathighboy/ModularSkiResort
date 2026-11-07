# Hub browser harness (no game needed)

The harness mounts the built bundle `build/SkiResortMod.mjs` in a plain browser. It supplies stand-ins for the game's
`window` modules. With it, you can work on wording, layout and view flow without launching Cities: Skylines II.
Created 2026-10-18. It deploys nothing. It touches nothing under the game or `assets/import`.

## Run

```
cd mod/SkiResortUI
npm run build                 # plain webpack into build/ (deploys nothing; postbuild gate runs)
node harness/serve.js         # http://localhost:5173/harness/
```
Edit a component -> `npm run build` -> "Reload bundle" in the page.

## What the page gives you

- Left pane. It has these controls:
  - the scenario (not connected / contract mismatch / connected empty city / Ash Flat with runs, lifts,
    problems and 30 months of `history` / Ash Flat with 8 years of history, so "All" draws July-June seasons)
  - the C# connected toggle (contract 8 vs 0)
  - the DLL-stamp-differs toggle
  - the top-left button toggle
  - a "set binding" box (`name = JSON`)
  - the UI scale
  - the log of every trigger the UI sends (what C# would receive)
- Right pane. The "game viewport" mounts the three registrar targets: `Game` (the hub root), `GameTopLeft`
  (floating button) and `UniversalModMenu` (the square).

## Layout check and screenshots (T114, 2026-10-20)

`harness/layout-check.js` measures "never a scrollbar" in the page. Start it with `?layout=1`, or with `window.SkiLayout.run()`
in the console of a VISIBLE tab (a hidden tab stalls animation frames). It uses the game's rem rule. It sets the text scale
the way the game does (`--fontScale` and `--fontScaleChange`).

`node tools/test-hub-layout.js` runs it in headless Chrome at 1920x1080, 2560x1440 and 3840x2160. It fails on any overflow.
`--shots=<dir>` writes the screenshot list in `test-hub-layout.js` (URL parameters `scenario`, `view`, `fs`, `tab`, `fold`,
`page`, `areas`, `problems`, `free`, `clean`: see the header of layout-check.js).

Two scenarios exist for it: "Big resort" (long name, 30 runs, 12 lifts with 5 unfinished, nine band rows) and "Field case"
(one complete lift, four unfinished: the 2026-10-20 crop). Themed buttons get the page font here (`#stage button`), as cs2/ui
Buttons get the game's font in game.

## What is faithful and what is not

Faithful:

- `cs2/api` semantics (`bindValue` fallback, `useValue`, `bindLocalValue`, `trigger`).
- `LocalizedString` placeholder substitution.
- The `LocalizedNumber` unit formats the hub relies on (src/hub/parts.tsx header).
- The props the hub passes to `Panel` / `PanelFoldout` / `Button` / `FloatingButton` / `Tooltip` / `DropdownItem`.
- The `Dropdown` open / close flow (T97 P1, 2026-10-19). The list is in a portal under its toggle. A barrier click closes it.
  React events bubble through the portal as in game. Its chrome, flip-up and focus are not faithful.
- The game's `:root` theme variables. Its `index.css` is served read-only from the Steam folder as `/game/index.css`,
  with `coui://` URLs rewritten.
- The game's `Media/` glyphs and Noto Sans fonts.
- The game's rem rule (1 rem = 1 px at 1080p). The page defaults to a fixed 1 rem = 1 px, so a small window stays readable.

Not faithful:

- The game's own component chrome (vanilla panel frame, buttons, dropdown, sounds, focus and gamepad hints).
- Full number localization (currency symbol, unit system).
- Everything C# computes.

The harness answers only the trivial triggers (`toggleHub`, `setHubOpen`, `selectResort`, `setPisteMap`, `glade*`, `launch`,
`rename`) by editing the bindings. It logs the rest. Sample data in `scenarios.js` is plausible, not measured. Do not read sim
numbers from it.

The gate (`tools/check-bundle.js`) does not look at this folder. Nothing here ships.

**Known gap (2026-10-18 in game):** the game's own tooltip balloons wrap, and its panel / button chrome has more padding than
these stand-ins. So the harness reads a little more crammed than the game ("in game it looked totally good"). Judge wording,
hierarchy, numbers and charts here. Judge spacing and wrapping in game. We loosened the stand-in CSS (tooltips wrap at 320 rem,
buttons 6 x 12 rem padding), but it is still not the game's chrome.
