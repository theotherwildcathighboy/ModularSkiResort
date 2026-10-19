# docs-site: the Modular Ski Resort docs

Guides, reference, asset pipeline and timeline for the mod, built with
[Astro Starlight](https://starlight.astro.build) and published to GitHub Pages. MIT, like the rest of the code.

## Run it

Needs Node 22 or newer. From this folder:

```bash
npm install          # once
npm run dev          # live site at http://localhost:4321/, reloads on save
npm run build        # static site into dist/ (also builds the search index)
npm run preview      # serve dist/ at http://localhost:4321/
```

`npm run build` must pass before anything is merged. It fails on broken frontmatter and on an unknown
sidebar slug. It does **not** check links; to check them, build and look for broken internal hrefs.

Versions are pinned exactly in `package.json`. Astro is pinned to **7.3.8**, not the newest: Starlight 0.42.6 needs
Astro 7.2.10 or newer, 7.3.0 fails against it, and 7.4.x wants Node 22.22, which is newer than some machines have.
Bump both together, one commit, with a clean build.

## Deployment

`../.github/workflows/pages.yml` is the Pages workflow: build on every push to `main` that touches `docs-site/`, deploy
with the official Pages actions. Moved there 2026-10-19.
One-time: repository Settings > Pages > Source = GitHub Actions. The workflow sets `DOCS_SITE` and `DOCS_BASE` from
the repository (`https://<owner>.github.io` and `/<repo>`); locally they default to the root. For a custom domain,
set `DOCS_SITE` to it, `DOCS_BASE` to `/`, and add `public/CNAME`.

## How the pages are organized

```text
src/content/docs/
  index.mdx                     Home: pitch, install, one hero screenshot
  quickstart.mdx                Resort quickstart: the fewest steps to a working resort
  guides/                       How-to, in the order a player meets things
  reference/                    Numbers: buildings, lifts, the attraction formula, settings
  asset-pipeline.mdx            For modelers: budgets, LODs, texel density, tile sheet, vanilla props, gates
  timeline.mdx                  Dated list of what was explored, built, changed, discarded, and why (ledger #n at row end)
  changelog.mdx                 Version history
  credits.mdx                   Credits and licenses
src/components/Shot.astro       Labeled gray box for a screenshot that does not exist yet
src/components/Icon.astro       One of the mod's own icons inline (public/icons/, copied from the game-side icon set)
src/assets/resort-logo-*.svg    The launcher glyph (peak and snowflake) for the header and home hero; white and navy
src/styles/custom.css           The only custom style (the Shot box, inline icons, timeline refs)
astro.config.mjs                Title, sidebar (explicit, in this order), base path
```

The sidebar is written by hand in `astro.config.mjs`, so a new page needs a line there.

## Writing rules

1. **Player voice.** Talk to someone building a resort, in plain sentences: "Place the top station facing the base
   station", not "the top station's rotation constraint". The guides never name an engine system, a component or a
   prefab. The Asset Pipeline and Timeline pages are the places that may, because they are written for modders.
2. **No jargon without the glossary.** If a term needs defining (texel, LOD, prefab, ECS), either define it on the
   spot in one sentence or do not use it in a guide. Terms are defined in the project glossary (`docs/GLOSSARY.md`,
   private); lift that sentence, do not link to it.
3. **Every number is sourced.** A number on a page comes from the mod's catalog or rules (`SkiCatalog.cs`,
   `SkiResortScore.cs`, `SkiMoneyRules.cs`, `RunSnowRules.cs`), the in-game Options text, or the hub strings. Put the
   source in an MDX comment at the top of the page: `{/* src: ... */}` (not shown to readers). A number you could
   not source is not written; if it is unconfirmed in game, say so in an aside.
4. **Placeholders are honest.** A missing screenshot is a `<Shot label="..." />` box saying what the picture must
   show. Never a mock-up, never a screenshot of the wrong build.
5. **Mark what is unverified.** Anything not yet read off the live game carries a "(to confirm)" and a caution
   aside, and goes on the open list in the skeleton note. Remove the mark when it has been checked.
6. **Nothing private ships.** No absolute `C:\Users\...` paths, no ledger or ticket numbers in visible text, no
   copy-paste from `docs/DECISIONS.md` (the ledger is private: the storyline is a retelling).
7. **Use Starlight's pieces where they help**: `Steps` for procedures, `Tabs` for alternatives, `Aside` for a
   note, tip or caution. Prefer a table for numbers.

## Look variants

Four presentation options sit beside the current look; none is on. Each is one CSS file in `src/styles/variants/`,
and the screenshots and contact sheets are in `shots/variants/` (git-ignored). The current look is
`customCss: ['./src/styles/custom.css']` with no plugin. Only presentation differs: no page content changes.
All four were built with `npm run build` (and the plain site) on 2026-10-19.

Switch: in `astro.config.mjs`, change the `customCss` line, and for variants 3 and 4 also add the import and the plugin.
Switch back by restoring `customCss: ['./src/styles/custom.css']` and removing the `plugins` line and import.
A theme plugin that is installed but not imported costs nothing; `starlight-theme-rapide` 0.6.0 and
`starlight-theme-nova` 0.12.3 are pinned in `package.json`.

1. **default-accent** (Starlight layout, hub accent):
   `customCss: ['./src/styles/custom.css', './src/styles/variants/default-accent.css'],`
2. **hub-palette** (navy / snow / orange, difficulty-color asides, Barlow Condensed headings from Google Fonts):
   `customCss: ['./src/styles/custom.css', './src/styles/variants/hub-palette.css'],`
3. **community-theme-a** (starlight-theme-rapide):
   add `import starlightThemeRapide from 'starlight-theme-rapide';` at the top, then
   `customCss: ['./src/styles/custom.css', './src/styles/variants/community-theme-a.css'],` and
   `plugins: [starlightThemeRapide()],` inside `starlight({ ... })`.
4. **community-theme-b** (starlight-theme-nova):
   add `import starlightThemeNova from 'starlight-theme-nova';`, then
   `customCss: ['./src/styles/custom.css', './src/styles/variants/community-theme-b.css'],` and
   `plugins: [starlightThemeNova()],`.

Once one is chosen, delete the other three CSS files and `npm uninstall` the unused theme package.

## Status

The skeleton, the page list with its sources and what is still placeholder are in the project's
`docs/research/ui/docs-site-skeleton-2026-10-19.md`.

## Protected writing

The author's own text sits inside `<!-- LOCK -->` and `<!-- /LOCK -->` markers, and a page whose frontmatter has `locked: true` is locked whole. An AI session must not edit inside those markers or those pages.

- `npm run lock:check` (also the first step of `npm run build`) hashes each locked region and fails with the file path if one changed, went missing, or is new and not yet locked.
- `npm run lock:relock` rewrites `docs-lock.json`. Run it only when the author asks.
- Lock keys are `<path>#<n>` for the n-th region in a file and `<path>#page` for a locked page.
