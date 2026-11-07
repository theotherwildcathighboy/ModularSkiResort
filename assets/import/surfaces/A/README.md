# Ski run surfaces — generation A — import & surgery handoff

Generated 2026-08-10 by `assets/blender/ski_surface.py` (re-run:
`blender --background --factory-startup --python ski_surface.py` — textures + previews.
Seeds are fixed, so the output is deterministic). Research basis: `docs/research/trails-surfaces-glades.md`
§1.5/§7a + `docs/research/golfpack-dissection.md` §2.

## Contents

- `SkiRunGroomedA/` — 3×1024² PNG: `_BaseColor` (sRGB), `_Normal` (OpenGL, linear),
  `_MaskMap` (surface layout: R=Metallic 0, G=MetallicMask 1, B=NormalMask 1,
  A=Glossiness ~0.30 + sparkle glints). Corduroy ~17 cm pitch at m_UVScale 0.06.
- `SkiRunSnowmakingA/` — brighter/denser variant (gloss ~0.42, fainter corduroy, finer grain).
- `staging-prefabs/` — hand-made SurfacePrefab JSONs + pre-minted `.cid`s. **NOT installed**
  — texture refs are `CID:REPLACE_*` placeholders. Do not copy to ImportedData before surgery.
- Previews (user verdict happens on these): `assets/preview/surfaces/<Name>_{three_quarter,detail,tiling}.png`
  (`_tiling` is the 2×2 seam proof).

## USER import steps (Editor, per the official surface path — wiki route A)

Surfaces do NOT use the mesh folder contract. Per asset:

1. In the Editor, find **Grass Surface 01**. Select **Duplicate**. Rename the copy to **SkiRunGroomedA**
   (exact name, no underscores). Save it into our project.
2. Open its **Rendered Area** component. Import into the three texture slots
   (each slot has its own importer):
   - BaseColor slot ← `assets/import/surfaces/A/SkiRunGroomedA/SkiRunGroomedA_BaseColor.png`
   - Normal slot ← `..._Normal.png`
   - Mask slot ← `..._MaskMap.png`
3. Save. Repeat 1–3 for **SkiRunSnowmakingA**.
4. You need no other Editor settings. Surgery applies the UVScale/priority/edge values.

## RESOLVED — import + surgery completed Aug 10

The Editor path won. The user set the panel-exposed values at import time (UVScale,
Roundness, edge params, priority, deleted SpawnableArea). Surgery patched only the
JSON-only fields (overlay colors → snow white-blue, UIObject priority 30/31, group
CID→`UnityGUID:60c0e17c…`, icon `Media/Game/Icons/SurfaceGrass01.svg` — golf-pack forms).
**staging-prefabs/ are SUPERSEDED — never install them.** The pre-minted CIDs there are
dead. Live CIDs (for future trail pieces' `ObjectSubAreas`):

- SkiRunGroomedA `62784c6bbc29053b714d82ac110eac9f` — RendererPriority **10**
- SkiRunSnowmakingA `47b3d84a30bebe0372180fff7e04259b` — RendererPriority **11**

Priorities moved 1/2 → 10/11 during surgery. GolfFairway ships priority 1 (vanilla
surfaces sit at −100…−95) and a tie is undefined draw order. 10/11 keeps snow over
fairway with headroom.

## Post-import surgery (coordinator — as originally planned)

1. Locate the Editor-produced assets under
   `...\Cities Skylines II\ImportedData\` (expect a folder per surface with the duplicated
   prefab + three `.Texture` assets, each with a `.cid`).
2. Read the three texture `.cid` values per surface. Fill the six placeholders in
   `staging-prefabs/*.Prefab` (`CID:REPLACE_GROOMED_BASECOLOR_CID` etc.).
3. Choose one of two routes:
   - EITHER adopt the staged prefabs. Replace the Editor-duplicated prefab JSON with the
     staged file. Keep OUR pre-minted prefab `.cid`: future trail pieces'
     `ObjectSubAreas` reference `09f6454973ff71a4d85c6d99c6295b33` (groomed) /
     `17c957b5d3f83df8f48f80598d750fee` (snowmaking).
   - OR keep the Editor prefab and splice our field values in (UVScale 0.06, RendererPriority 1/2, Roundness 0.8,
     UIObject group `60c0e17c40a5f1b41bf9d2032670b81d` = Landscaping▸Surfaces
     [vanilla-confirmed], ServiceObject `5930db6811522d34495a9e16c4fd4c18`, overlay colors).

   Prefer whichever keeps the .cid story simplest after you see what the Editor emits.
   Surfaces have never been through our pipeline before (first-time unknowns below).
4. Run `tools/validate_prefab.ps1` on both. Check for UTF-8 with no BOM.
5. Test in game. Draw both on a ≥40% A-Basin pitch. Check corduroy visibility, tiling,
   sparkle at low sun, overlap priority (snowmaking over groomed), and FPS with a big area.

## Known unknowns (first surface import round)

- Whether the Editor's per-slot importer accepts our PNGs silently (Normal must be read
  as linear. The wiki says each slot importer applies correct settings).
- Whether the Editor duplicate emits `SpawnableArea`/`Unlockable` we did not stage
  (harvest or delete during surgery — staged files intentionally omit both).
- Icon is the grass placeholder svg for v1. Custom `assetdb://` PNG icons are a
  follow-up from golfpack-dissection.md §1, together with the Ski Resort category.
- `m_Material UnityObjectsMap:a1b9ab64…` is the shared area material (golf + vanilla both
  reference it). It should resolve from a mod context, because the golf pack ships exactly
  this reference.
