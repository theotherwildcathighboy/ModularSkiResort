# Wilin_Amaurea "Ski gongola and lift station detailed drafts" - archived original

| Field | Value |
|---|---|
| Title | Ski gongola and lift station detailed drafts (sic) |
| Author | Wilin_Amaurea - https://sketchfab.com/wilinamaurea |
| Source | https://sketchfab.com/3d-models/ski-gongola-and-lift-station-detailed-drafts-a8fabd3e955643a2a9e9f09bd46635db |
| License | CC BY 4.0 (CC Attribution) - https://creativecommons.org/licenses/by/4.0/ (Sketchfab API `v3/models/a8fabd3e955643a2a9e9f09bd46635db`; Richard confirmed Sketchfab CC BY on 2026-10-11, ROUND-SPEC finding 12 / Q8) |
| Published | 2020-06-09 (faceCount 36,557 = this file's triangle count; tag `xyz-school-draftpunk-3`) |
| Downloaded | 2026-10-10, by theotherwildcathighboy |
| Archived | 2026-10-11: copied (not moved) from `Desktop\ski resort assets reference images\gondolas\source\` |
| What was changed | Nothing yet. Byte-identical copy of the download. |

Caveat (ref-models-skeptic verdict 12): the FBX header says it was written 2020-06-09 14:56 GMT
as `gondola_last.fbx`, 46 min after the Sketchfab publish, so it is a later export than the
original upload. Same geometry is likely; the exact download page is Richard's answer (Q8).

## Files

| Path | Size | SHA-1 |
|---|---|---|
| `source/gondola.fbx` | 7,206,439 B (ASCII FBX 7.5, Maya 2018; only `bpy.ops.wm.fbx_import` reads it) | `9399f7b9aea16b8213cc0069449c865f974f05f0` |

The binary is gitignored (`.gitignore`, "Third-party originals" block); this file is tracked.
On a fresh clone, re-download from the source URL above and place the file at this path.

## Credit line (ref-models.md §3)

Required as soon as anything derived from this model ships (release/CREDITS.md, the listing and
in-game description, assets/LICENSE carve-out, README.md):

> "Ski gongola and lift station detailed drafts" by Wilin_Amaurea
> (https://sketchfab.com/3d-models/ski-gongola-and-lift-station-detailed-drafts-a8fabd3e955643a2a9e9f09bd46635db),
> licensed under CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/). Modified by
> theotherwildcathighboy for Modular Ski Resort: re-scaled, re-topologised, re-UV'd and re-textured
> for Cities: Skylines II.

## Use in this repo

- Loader: `assets/blender/cs2_ingest.py` (`load_external`, `colour_sets`); test
  `assets/blender/tests/test_ingest.py`.
- Facts (scale about 27 x imported meters = 0.27 m per file unit, +-10 %; groups and tri
  counts): `docs/research/art/assets-2026-10/understand/ref-models.md` §2.
- Route (ROUND-SPEC finding 12): reuse the cabin shell (739 tris); rebuild hanger, yoke and
  carriage; the `mechanism` station is layout reference only.
