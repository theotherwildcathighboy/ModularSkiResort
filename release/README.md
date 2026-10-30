# Release staging — Paradox Mods

Everything that feeds the listing lives here; nothing in this folder is loaded by the game. The mod ships as ONE hybrid
Paradox Mods item (DECISIONS 278: the DLL and the `.cok` share one item id for the life of the mod; never split them), and
one publish build of `mod/SkiResortMod` assembles and uploads all of it.

## What the upload holds

The publisher uploads the toolchain's deploy folder
(`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SkiResortMod`) as a publish build leaves it:

- the code: `SkiResortMod.dll` and the Burst library `SkiResortMod_win_x86_64.dll`;
- the hub UI: `SkiResortMod.mjs`, `SkiResortMod.css` and `SkiResortMod-img\` (built from `mod/SkiResortUI`);
- the 8 run textures, `Textures\*.png`, from the git-ignored `mod/SkiResortMod/Textures`;
- the asset package: one `.cok` (stations, towers, chairs, cabins, buildings, the lift network) from the git-ignored
  `mod/SkiResortMod/Package/`;
- the 6 Write Everywhere layouts, `K45_WE\layouts\*.wedefault.xml`, from `assets/we/K45_WE/layouts`.

The PDBs and the mac and linux Burst libraries stay out: they carry the Windows profile path, and the game loads only the
Windows library. Listing text, tags, thumbnail and screenshots come from `mod/SkiResortMod/Properties/PublishConfiguration.xml`,
which draws on the files here.

## The route

PowerShell at the repo root, game closed except for step 1.

1. **Asset package.** In the Editor's package step (never its upload, which would create a second, asset-only item), select
   only the prefabs of `tools/shipped-prefabs.json`, including the ones with no menu entry. Never select the whole
   `ImportedData\c030ee3ff256df91` library (about 3 GB, mostly unreferenced older imports) or the vanilla
   `ExampleBakedAlphaFence` in it. Quit the game, move the package into place and check it:

   ```powershell
   Move-Item "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II\.packages\*.cok" mod\SkiResortMod\Package\
   $cok = (Get-Item mod\SkiResortMod\Package\*.cok).FullName; node tools\check-cok.js $cok; (Get-Item $cok).Length / 1GB
   ```

   Expected: `PASS` (every hand-made prefab is inside), then about 0.8 (the shipped closure is 785 MB). Near 3 means the whole
   library was packaged: package again. Details: `mod/SkiResortMod/Package/README.md`.
2. **Dry run** (no upload): `cd mod\SkiResortMod; dotnet build -c Release -p:SkiPublishDryRun=true` does everything the
   publish build does except the upload. Expected near the end: `SUMMARY: ALL 7 CHECKS PASS`. Do not play on that deploy (a
   `.cok` beside ImportedData is untested): a plain `dotnet build` restores the normal deploy.
3. **Publish**: `dotnet publish -c Release -p:PublishProfile=PublishNewMod` in `mod/SkiResortMod` (later versions:
   `PublishNewVersion`). The build compiles and deploys; DeployWIP empties the deploy folder first, so nothing put there by hand
   survives. Then `SkiPublishPackage` (`mod/SkiResortMod/SkiPublish.targets`):
   - stops with an error, before anything is uploaded, when `Package/` holds no `.cok` or several, or when the deploy folder
     has fewer than 8 textures;
   - copies the `.cok` and the layouts in, removes the PDBs and the mac and linux libraries, and cuts the PDB path inside the
     DLLs down to the bare file name (`tools/strip-pdb-path.js`);
   - runs `tools/release-check.ps1` on the folder. Any FAIL fails the build, so the publisher never runs.

   Afterwards the local Mods folder holds the same layout: run a plain `dotnet build` before playing, or move
   `Mods\SkiResortMod` out of the Mods folder to test the subscribed item. The steps around the publish (ModId, proofreading,
   the public flip) are in docs/RELEASE-CHECKLIST.md section 7.

Normal builds (`dotnet build`) deploy what they always did: no `.cok`, no layouts, the PDBs kept.

## Contents

- `DESCRIPTION.md` — listing copy draft (short + long description). Keep in sync with feature state.
- `LISTING-NOTES-2026-10-22.md` — every claim in the listing copy with its source, what is unverified, open questions.
- `CREDITS.md` — attribution (REQUIRED before upload, per project commitment).
- `CHANGELOG.md` — player-facing version history; seeded during beta.
- `BESTPRACTICES.md` — the player guide (build order, workflow rules). Draft from
  PHASE3 §12 workflow notes + DECISIONS.md ⚖️ entries.
- `screenshots/` — curated gallery shots (Paradox listing takes several; thumbnail needed
  too, 1:1 crop recommended).
- `saves-by-release/` — per-release test saves, kept on this machine only (its README is tracked).

## Pre-upload checklist (draft)

- [ ] Fresh-profile test: mod pack on a clean save, no other mods, zero boot errors
- [ ] Best-practices guide finished and linked in the description
- [ ] Credits present in listing AND in-game description
- [ ] Version + changelog set; thumbnail + ≥4 screenshots
- [ ] Code mod: PublishConfiguration.xml matches DESCRIPTION.md
- [ ] Packaging: `node tools\check-cok.js` PASS on the `.cok` in `mod/SkiResortMod/Package/` (hand-made prefabs inside),
      about 0.8 GB; after the upload, the same check on the downloaded `.cok`
- [ ] Dry run: `tools/release-check.ps1` prints `SUMMARY: ALL 7 CHECKS PASS` (the publish build runs it again)
