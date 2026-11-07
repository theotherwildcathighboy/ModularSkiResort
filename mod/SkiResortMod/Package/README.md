# Package/: the Editor's asset package, for publish builds only

Git-ignored except this README. At publish time this folder holds exactly ONE file. This file is the `.cok` that the Editor's package step
writes, the asset half of the one hybrid Paradox Mods item (DECISIONS 278). Normal builds never read it.

## Why the .cok lives here

Every build of `mod/SkiResortMod` runs the toolchain's DeployWIP, which deletes the deploy folder
(`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SkiResortMod`) and copies only the build output into it.
ModPublisher then uploads that folder. A `.cok` that you put into the deploy folder by hand is gone before the upload.

The target `SkiPublishPackage` (`../SkiPublish.targets`) copies the `.cok` from here into the deploy folder after DeployWIP. It does
that only in publish builds (`PublishNewMod`, `PublishNewVersion`) and in the dry run (`-p:SkiPublishDryRun=true`). Test deploys
therefore never put a `.cok` beside ImportedData.

## Making the .cok

1. In the Editor's package step, select only the prefabs of `tools/shipped-prefabs.json`. Include the prefabs that have no menu entry
   (towers, chairs, cabins, number tiles, markers: only code reaches them). Never select the whole
   `ImportedData\c030ee3ff256df91` library. Never select the vanilla `ExampleBakedAlphaFence` that sits in it.
2. Quit the game. The package is `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\.packages\<main asset>.cok`
   (decompile 1.6.2f1 `Game.UI.Editor/PackageHelper.cs:109`). Move it here. Keep no other `.cok` here. Only the `.cok` ships. The
   game's own Editor upload copies just the `.cok` too (`Game.UI.Menu/PdxAssetUploadHandle.cs:268-277`), so a `.cok.cid` beside
   it is not needed. Never use the Editor's upload itself, because it would create a second, asset-only Paradox Mods item.
3. Check it (PowerShell, repo root):

   ```powershell
   $cok = (Get-Item mod\SkiResortMod\Package\*.cok).FullName; node tools\check-cok.js $cok; (Get-Item $cok).Length / 1GB
   ```

   Expected: `PASS`, then about 0.8. The shipped closure is 785 MB. A value near 3 means the whole library was packaged
   (`tools/release-check.ps1` fails anything from 2 GB up). If `MISSING` lists exactly SkiLiftChair4 and SkiLiftTowerFG2, that may be
   the check's own name match (their library files carry a hash suffix), not a broken package.

## What a publish build does with it

`SkiPublishPackage` stops with an error when this folder holds no `.cok` or several, or when the deploy folder has fewer than 8
`Textures\*.png`. Otherwise it does four things:

- It copies the `.cok` and `assets/we/K45_WE/layouts/*.wedefault.xml` into the deploy folder.
- It removes the PDBs and the mac and linux Burst libraries, which carry the Windows profile path (the game loads only
  `SkiResortMod_win_x86_64.dll`).
- It cuts the PDB path inside the DLLs down to the bare file name.
- It runs `tools/release-check.ps1`. A FAIL there fails the build, so nothing is uploaded.

After a dry run or a publish, the local Mods folder holds the `.cok` beside ImportedData. How the game treats that is
unverified. Build once more without the dry-run flag before you start the game.
