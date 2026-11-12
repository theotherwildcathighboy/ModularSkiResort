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

The bridge route (DECISIONS 655) replaces the hand selection. It runs the game's own package code, never an upload:

1. Start the game in the Editor with the bridge active (`--startEditor --developerMode --uiDeveloperMode`, `tools/testbridge/README.md`).
   Dry run first, through the gameface MCP (`game_eval`):

   ```js
   engine.call("skitest.cmd", JSON.stringify({ op: "package", list: "<repo>\\tools\\shipped-prefabs.json", name: "ModularSkiResort" })).then(r => window.__pk = r)
   ```

   Expected: `found` 189 of 189, `missing` and `cidMismatch` empty, `modDependencies` and `dlcDependencies` empty, `byDatabase` only
   `user`, and `extraPrefabsInClosure` only `... Mesh` RenderPrefabs. The call blocks the game for a few minutes (the game walks every
   prefab), so the CDP call times out: keep the promise on `window` and read it when the game responds again. Then run it again with
   `dry: false` and poll `{op: "package", status: true}` (or wait for `package: done` in `Logs\SkiTestBridge.log`).
   `name` names the package and the `.cok` without renaming any file: the op hands it to the game's own `PackageHelper.BuildPackage`,
   which uses the name only for its staging folder, its backup folder and `AssetDatabase.packages.AddAsset` (decompile 1.6.4f1
   `Game.UI.Editor/PackageHelper.cs:73-121`). Without `name`, the package takes the main asset's name, as in the Editor.
2. Quit the game. The package is `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\.packages\ModularSkiResort.cok`.
   Move it here before the next game start (a `.cok` left in `.packages` loads as a local package beside ImportedData). Keep no other
   `.cok` here. Only the `.cok` ships: the game's own Editor upload copies just the `.cok` too
   (`Game.UI.Menu/PdxAssetUploadHandle.cs:337-344`, 1.6.4f1), so a `.cok.cid` beside it is not needed. The `.ModularSkiResort_Backup` folder
   next to it is the game's copy of the source files (`PackageHelper.cs:83-85`). It is not needed for the upload.

Fallback, the manual route: in the Editor's package step, select only the prefabs of `tools/shipped-prefabs.json`. Include the prefabs that
have no menu entry (towers, chairs, cabins, number tiles, markers: only code reaches them). Never select the whole
`ImportedData\c030ee3ff256df91` library. Never select the vanilla `ExampleBakedAlphaFence` that sits in it. Never use the Editor's upload
itself, because it would create a second, asset-only Paradox Mods item. The package is then named after the first asset you added.

Check it (PowerShell, repo root):

```powershell
$cok = (Get-Item mod\SkiResortMod\Package\*.cok).FullName; node tools\check-cok.js $cok; (Get-Item $cok).Length / 1GB
```

Expected: `PASS` (`missing 0, cid differs 0, extra 320`: the extras are the `... Mesh` RenderPrefabs of the shipped prefabs), then about 0.3.
Measured 2026-11-12: 509 prefabs, 1,550 source assets (884 MB) packed into 312,639,541 bytes (0.29 GB). The package step converts 312 of the
334 textures to virtual texturing (284 VTTexture entries) and keeps 22 as plain textures. A value near 3 means the whole library was
packaged (`tools/release-check.ps1` fails anything from 2 GB up). check-cok finds SkiLiftChair4 and SkiLiftTowerFG2 by their frozen
`file` names, because their library files carry a hash suffix and the package step names each entry after its source file.

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
