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
  problems), C# connected toggle (contract 8 vs 0), DLL-stamp-differs toggle, top-left button toggle, a
  "set binding" box (`name = JSON`), UI scale, and the log of every trigger the UI sends (what C# would receive).
- Right: the "game viewport" with the three registrar targets mounted: `Game` (the hub root), `GameTopLeft`
  (floating button), `UniversalModMenu` (the square).

## What is faithful and what is not

Faithful: `cs2/api` semantics (`bindValue` fallback, `useValue`, `bindLocalValue`, `trigger`), `LocalizedString`
placeholder substitution, `LocalizedNumber` unit formats the hub relies on (src/hub/parts.tsx header), the props
the hub passes to `Panel` / `PanelFoldout` / `Button` / `FloatingButton` / `Tooltip` / `DropdownItem`, the game's
`:root` theme variables (its `index.css` is served read-only from the Steam folder as `/game/index.css`, with
`coui://` URLs rewritten), its `Media/` glyphs and Noto Sans fonts, and its rem rule (1 rem = 1 px at 1080p;
the page defaults to a fixed 1 rem = 1 px so a small window stays readable).

Not faithful: the game's own component chrome (vanilla panel frame, buttons, dropdown, sounds, focus and gamepad
hints), full number localisation (currency symbol, unit system), and everything C# computes. The harness answers
only the trivial triggers (`toggleHub`, `setHubOpen`, `selectResort`, `setPisteMap`, `glade*`, `launch`, `rename`)
by editing the bindings; the rest is logged. Sample data in `scenarios.js` is plausible, not measured: do not read
sim numbers off it.

The gate (`tools/check-bundle.js`) does not look at this folder; nothing here ships.
