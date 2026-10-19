# docs-site: the Modular Ski Resort docs

Player guides, reference and the "making of" story for the mod, built with
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

## How the pages are organised

```text
src/content/docs/
  index.mdx                     Home: pitch, install, one hero screenshot
  getting-started/              Your first resort in 10 minutes
  guides/                       How-to, in the order a player meets things
  reference/                    Numbers: buildings, lifts, the attraction formula, settings
  making-of/index.mdx           The storyline, one section per chapter (stubs for now)
  changelog.mdx                 Version history
  credits.mdx                   Credits and licences
src/components/Shot.astro       Labelled grey box for a screenshot that does not exist yet
src/styles/custom.css           The only custom style (the Shot box)
astro.config.mjs                Title, sidebar (explicit, in this order), base path
```

The sidebar is written by hand in `astro.config.mjs`, so a new page needs a line there.

## Writing rules

1. **Player voice.** Talk to someone building a resort, in plain sentences: "Place the top station facing the base
   station", not "the top station's rotation constraint". The guides never name an engine system, a component or a
   prefab. The *making of* section is the one place that may, because it is written for modders.
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

## Status

The skeleton, the page list with its sources and what is still placeholder are in the project's
`docs/research/ui/docs-site-skeleton-2026-10-19.md`.
