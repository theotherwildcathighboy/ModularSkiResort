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

`npm run build` must pass before anyone merges anything. It fails on broken frontmatter and on an unknown
sidebar slug. It also fails on a root-relative link that lacks the base path or points at nothing
(`tools/check-base-links.js`, which runs after the build with the same `DOCS_BASE`).

**Links.** Write links in page content as plain root-relative paths, `[Lifts](/guides/lifts/)`. Never hard-code the
repository name. A remark-style mdast plugin in `astro.config.mjs` prefixes the configured base onto them at build time.
`src/routeMiddleware.ts` does the same for the home page hero buttons. To check a Pages build locally (PowerShell), run
`$env:DOCS_SITE='https://<owner>.github.io'; $env:DOCS_BASE='/<repo>'; npm run build` (the check runs inside the build).
To run the check alone against an existing `dist/`, run `npm run links:check`. Under Git Bash, set `MSYS_NO_PATHCONV=1`.
Otherwise the shell rewrites `/<repo>`.

`package.json` pins the versions exactly. Astro is pinned to **7.3.8**, not the newest. Starlight 0.42.6 needs
Astro 7.2.10 or newer. Astro 7.3.0 fails against Starlight. Astro 7.4.x wants Node 22.22, which is newer than some machines have.
Bump both together, in one commit, with a clean build.

## Deployment

`../.github/workflows/pages.yml` is the Pages workflow. It builds on every push to `main` that touches `docs-site/` and deploys
with the official Pages actions. It moved there 2026-10-19.
One-time setup: repository Settings > Pages > Source = GitHub Actions. The workflow sets `DOCS_SITE` and `DOCS_BASE` from
the repository (`https://<owner>.github.io` and `/<repo>`). Locally they default to the root. For a custom domain,
set `DOCS_SITE` to it, set `DOCS_BASE` to `/`, and add `public/CNAME`.

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
src/styles/custom.css           The only custom style (the look: palette tokens, fonts; the Shot box, inline icons, timeline refs)
astro.config.mjs                Title, sidebar (explicit, in this order), base path, the base-link plugin
src/routeMiddleware.ts          Prefixes the base onto home hero button links (Starlight route middleware)
tools/check-base-links.js       Post-build check: every root-relative URL in dist/ has the base and resolves
```

You write the sidebar by hand in `astro.config.mjs`, so a new page needs a line there.

## Writing rules

1. **Player voice.** Talk to someone building a resort, in plain sentences. Write "Place the top station facing the base
   station", not "the top station's rotation constraint". The guides never name an engine system, a component or a
   prefab. The Asset Pipeline and Timeline pages are the places that may, because modders read them.
2. **No jargon without the glossary.** If a term needs a definition (texel, LOD, prefab, ECS), either define it on the
   spot in one sentence or do not use it in a guide. The project glossary defines the terms (`docs/GLOSSARY.md`,
   private). Lift that sentence. Do not link to it.
3. **Every number is sourced.** A number on a page comes from the mod's catalog or rules (`SkiCatalog.cs`,
   `SkiResortScore.cs`, `SkiMoneyRules.cs`, `RunSnowRules.cs`), the in-game Options text, or the hub strings. Put the
   source in an MDX comment at the top of the page: `{/* src: ... */}` (readers do not see it). Do not write a number
   that you could not source. If it is unconfirmed in game, say so in an aside.
4. **Placeholders are honest.** Show a missing screenshot as a `<Shot label="..." />` box that says what the picture must
   show. Never use a mock-up. Never use a screenshot of the wrong build.
5. **Mark what is unverified.** Mark anything not yet read off the live game with a "(to confirm)" and a caution
   aside. Put it on the open list in the skeleton note. Remove the mark after you check the item.
6. **Nothing private ships.** Write no absolute local paths (a drive letter plus a user folder). Write no ledger or ticket
   numbers in visible text. Do not copy-paste from `docs/DECISIONS.md`. The ledger is private, and the storyline is a retelling.
7. **Use Starlight's pieces where they help**: `Steps` for procedures, `Tabs` for alternatives, `Aside` for a
   note, tip or caution. Prefer a table for numbers.

## Look

The author picked the hub's palette on 2026-10-19 from four options. All of it is in `src/styles/custom.css`.
The one `customCss` entry in `astro.config.mjs` loads it. There is no theme plugin.

- **Dark mode:** navy. Page `#121c2f`, header and sidebar `#1d2b44` (the logo navy), surfaces `#2a3753` (the hub panel),
  text snow `#f0fbff`.
- **Light mode:** snow. Page `#f4fbff`, header and sidebar white, text navy `#1d2b44`.
- **Accent:** ice blue. Dark `#3db4f0` / high `#6ecdff`. Light `#0b78b4` / high `#0a4f78` (deepened for contrast on white).
  Orange `#ffa42d` (the hub's warning color) marks the current page, table-header rules and the button hover.
- **Asides and badges** use the run difficulty colors: tip = green `#4caf50`, note = blue `#3478dc`, caution = orange,
  danger = black diamond (`#0e0e12`, light edge in dark mode).
- **Fonts:** headings, site title, sidebar group labels and the primary button use Barlow Condensed 600 / 700
  (SIL OFL 1.1, listed on the Credits page), self-hosted as latin-subset woff2 in `public/fonts/` with its `OFL.txt`. Body
  is the system sans stack. No request leaves the site for fonts.

To change the accent, edit the three `--sl-color-accent*` values in the dark block (`:root`) and in the light block
(`:root[data-theme='light']`) at the top of `custom.css`. Everything else follows. Change the palette the same way,
through the `--sl-color-*` tokens. The `@font-face` URLs are `/fonts/...`. Vite prefixes them with `DOCS_BASE` at build time.

## Status

The skeleton, the page list with its sources and what is still placeholder are in the project's
`docs/research/ui/docs-site-skeleton-2026-10-19.md`.

## Protected writing

The author's own text sits inside `<!-- LOCK -->` and `<!-- /LOCK -->` markers, and a page whose frontmatter has `locked: true` is locked whole. An AI session must not edit inside those markers or those pages.

- `npm run lock:check` (also the first step of `npm run build`) hashes each locked region and fails with the file path if one changed, went missing, or is new and not yet locked.
- `npm run lock:relock` rewrites `docs-lock.json`. Run it only when the author asks.
- Lock keys are `<path>#<n>` for the n-th region in a file and `<path>#page` for a locked page.
