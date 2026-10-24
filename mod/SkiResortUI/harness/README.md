# Hub browser harness (no game needed)

Mounts the built bundle `build/SkiResortMod.mjs` in a plain browser with stand-ins for the game's
`window` modules, so wording, layout and view flow can be worked on without launching Cities: Skylines II.
Created 2026-10-18. Deploys nothing; touches nothing under the game or `assets/import`.

## Run

```
cd mod/SkiResortUI
npm run build                 # plain webpack into build/ (deploys nothing; postbuild gate runs)
node harness/serve.js         # http://localhost:5173/harness/
```
From Claude Code: `.claude/launch.json` at the ClaudeProjects root has a `hub-harness` entry (preview_start).
Edit a component -> `npm run build` -> "Reload bundle" in the page.

## What the page gives you

- Left pane: scenario (not connected / contract mismatch / connected empty city / Ash Flat with runs, lifts,
  problems and 30 months of `history` / Ash Flat with 8 years of history, so "All" draws July-June seasons), C# connected toggle (contract 8 vs 0), DLL-stamp-differs toggle, top-left button toggle, a
  "set binding" box (`name = JSON`), UI scale, and the log of every trigger the UI sends (what C# would receive).
- Right: the "game viewport" with the three registrar targets mounted: `Game` (the hub root), `GameTopLeft`
  (floating button), `UniversalModMenu` (the square).

## Layout check and screenshots (T114, 2026-10-20)

`harness/layout-check.js` measures "never a scrollbar" in the page (`?layout=1`, or `window.SkiLayout.run()` in the console of a
VISIBLE tab: a hidden tab stalls animation frames). It uses the game's rem rule and sets the text scale the way the game does
(`--fontScale` and `--fontScaleChange`). `node tools/test-hub-layout.js` runs it in headless Chrome at 1920x1080, 2560x1440 and
3840x2160 and fails on any overflow; `--shots=<dir>` writes the screenshot list in that file (URL parameters `scenario`, `view`, `fs`,
`tab`, `fold`, `page`, `areas`, `problems`, `free`, `clean`: see the header of layout-check.js). Two scenarios exist for it: "Big resort"
(long name, 30 runs, 12 lifts with 5 unfinished, nine band rows) and "Field case" (one complete lift, four unfinished: the 2026-10-20
crop). Themed buttons get the page font here (`#stage button`), as cs2/ui Buttons get the game's font in game.

## What is faithful and what is not

Faithful: `cs2/api` semantics (`bindValue` fallback, `useValue`, `bindLocalValue`, `trigger`), `LocalizedString`
placeholder substitution, `LocalizedNumber` unit formats the hub relies on (src/hub/parts.tsx header), the props
the hub passes to `Panel` / `PanelFoldout` / `Button` / `FloatingButton` / `Tooltip` / `DropdownItem`, the `Dropdown` open / close
flow (T97 P1, 2026-10-19: the list in a portal under its toggle, a barrier click closes it, React events bubble through the portal as in
game; not its chrome, flip-up or focus), the game's
`:root` theme variables (its `index.css` is served read-only from the Steam folder as `/game/index.css`, with
`coui://` URLs rewritten), its `Media/` glyphs and Noto Sans fonts, and its rem rule (1 rem = 1 px at 1080p;
the page defaults to a fixed 1 rem = 1 px so a small window stays readable).

Not faithful: the game's own component chrome (vanilla panel frame, buttons, dropdown, sounds, focus and gamepad
hints), full number localization (currency symbol, unit system), and everything C# computes. The harness answers
only the trivial triggers (`toggleHub`, `setHubOpen`, `selectResort`, `setPisteMap`, `glade*`, `launch`, `rename`)
by editing the bindings; the rest is logged. Sample data in `scenarios.js` is plausible, not measured: do not read
sim numbers off it.

The gate (`tools/check-bundle.js`) does not look at this folder; nothing here ships.

**Known gap (2026-10-18 in game):** the game's own tooltip balloons wrap and its panel / button chrome has more padding than
these stand-ins, so the harness reads a little more crammed than the game ("in game it looked totally good"). Judge wording,
hierarchy, numbers and charts here; judge spacing and wrapping in game. The stand-in CSS was loosened (tooltips wrap at 320 rem,
buttons 6 x 12 rem padding) but it is still not the game's chrome.
