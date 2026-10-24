# Kagelok "Chair Lift" - archived original

| Field | Value |
|---|---|
| Title | Chair Lift |
| Author | Kagelok - https://sketchfab.com/Kagelok |
| Source | https://sketchfab.com/3d-models/chair-lift-afd305cdd4cb450f80b59f24b4b3e236 |
| License | CC BY 4.0 (CC Attribution) - https://creativecommons.org/licenses/by/4.0/ (verified via the Sketchfab API `v3/models/afd305cdd4cb450f80b59f24b4b3e236` and the download-page screenshot, `Desktop\ski resort assets reference images\3d model of detachable quad\Screenshot 2026-10-10 230048.png`) |
| Published | 2017-09-02 (the .blend is the uploaded file: its name stamp is 10 s before Sketchfab's `createdAt`) |
| Downloaded | 2026-10-10, by theotherwildcathighboy |
| Archived | 2026-10-11: copied (not moved) from `Desktop\ski resort assets reference images\3d model of detachable quad\` |
| What was changed | Nothing yet. Byte-identical copies of the download. |

## Files

| Path | Size | SHA-1 |
|---|---|---|
| `source/Sketchfab_2017_09_02_17_41_10.blend` | 1,265,512 B (Blender 2.79.1) | `71a2c6b434e749ce47528e3caee804fb013551b5` |
| `textures/` | 117 PNG, 84,068,797 B (30 sets for 30 of the 33 materials) | - |

The binaries are gitignored (`.gitignore`, "Third-party originals" block); this file is tracked.
On a fresh clone, re-download from the source URL above and place the files at these paths.

## Credit line (ref-models.md §3)

Required as soon as anything derived from this model ships (release/CREDITS.md, the listing and
in-game description, assets/LICENSE carve-out, README.md):

> "Chair Lift" by Kagelok (https://sketchfab.com/3d-models/chair-lift-afd305cdd4cb450f80b59f24b4b3e236),
> licensed under CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/). Modified by
> theotherwildcathighboy for Modular Ski Resort: re-scaled, re-topologised, re-UV'd and re-textured
> for Cities: Skylines II.

## Use in this repo

- Loader: `assets/blender/cs2_ingest.py` (`load_external`, `match_texture_sets`); test
  `assets/blender/tests/test_ingest.py`.
- Facts (scale 0.30 m/u, hidden objects, constraints, texture relink): `docs/research/art/assets-2026-10/understand/ref-models.md` §1.
- Route (ROUND-SPEC R3): IMPORT the line and entry tower head kits; TRACE chair, grip and housing; ship none of its textures.
