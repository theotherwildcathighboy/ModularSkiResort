# SkiResortMod — code-mod half of Modular Ski Resort

C# code mod for Cities: Skylines II. This DLL owns the *dynamic* behavior of the hybrid mod
(SPEC §7): slope-classified trails, snow-gated operation, and size-scaled resort
attractiveness. The placeable pieces live in the asset pack, authored separately in the
in-game Editor.

Status: **feature-complete for 1.0**. Every gameplay system (lift network, runs and snow, buildings, attractiveness and
tourism, ski days, the hub's C# side) is a `GameSystemBase` / `ToolBaseSystem` / `UISystemBase` in `Systems/`. `Mod.cs` registers each one with a phase / anchor comment per system. The attractiveness patching research behind the scheduling decisions
is in `..\..\docs\research\code-mod.md`. Read it before you touch system registration.

## Layout

```
SkiResortMod/
├── SkiResortMod.csproj              official toolchain project (see notes below)
├── Mod.cs                           IMod entry point: logging, settings, system registration (load-bearing order)
├── LocaleEN.cs                      English asset names, hub and tooltip texts (the hub's hubText.ts tuples must equal it)
├── Settings/
│   ├── SkiResortSettings.cs         ModSetting-derived options (Options → Mods in game)
│   └── LocaleEN.cs                  English strings for the options UI
├── Systems/                         every system, its pure rule classes (*Rules.cs) and its Debug partials
├── Textures/                        run decal and terrain textures loaded at runtime
└── Properties/
    ├── PublishConfiguration.xml     Paradox Mods metadata — used only by `dotnet publish`
    ├── Thumbnail.png                listing thumbnail
    └── PublishProfiles/             publish profiles from the template
```

## Build

```powershell
cd <repo>\mod\SkiResortMod        # <repo> = your checkout of ModularSkiResort
dotnet build
```

That is the whole loop. The official `Mod.props`/`Mod.targets` (imported from
`%CSII_TOOLPATH%`, i.e. `...\Cities Skylines II\.cache\Modding\`) provide everything else:

1. **Compile** against the real game DLLs. The build resolves references by simple name from
   `%CSII_MANAGEDPATH%` (the game's `Cities2_Data\Managed`). The target is `net48`, C# 9, with the
   Unity Entities 1.3.10 source generators wired in as analyzers.
2. **Post-process** — `ModPostProcessor.exe` runs on the built DLL (Burst etc., `-p Windows
   -p macOS -p Linux`).
3. **Deploy** — the `DeployWIP` target wipes and re-copies the output to
   `%CSII_LOCALMODSPATH%\SkiResortMod`
   (`...\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SkiResortMod`).
   No manual copying, ever.

Then launch the game and enable the mod under **Options → Mods**. Logs land in
`%CSII_USERDATAPATH%\Logs\`. Our logger writes `SkiResortMod.Mod.log`.

A compile check that deploys nothing is exactly `dotnet build -t:Compile`. Use no other flags: `--no-incremental` or
`-t:Rebuild` fire `DeployWIP` and overwrite the installed mod.

There is no hot reload: rebuild, then restart the game.

### Machine-specific note: .NET 6 vs .NET 8

`ModPostProcessor.exe` targets the .NET 6 runtime, which is **not installed** on this machine
(only .NET 8 is). Its runtimeconfig carries no roll-forward policy, so a stock csproj
fails after the compile step with *"You must install or update .NET"*. The csproj here
overrides the `RunModPostProcessor` target (the customization mechanism documented at the top
of `Mod.targets`) to run the same command with `DOTNET_ROLL_FORWARD=LatestMajor`. On a machine
that has .NET 6, the variable is a no-op. The override also pins `BeforeTargets="DeployWIP"`, so
the build deploys the post-processed DLL, not the raw one.

## Do NOT run `dotnet publish`

Publish invokes `ModPublisher.exe` against `Properties\PublishConfiguration.xml` and uploads
to Paradox Mods. Publishing is a release-day step owned by the project owner. It is never part of a
build or test loop.

## Editing rules that keep this project healthy

- Add game references by **simple name** (`<Reference Include="Colossal.Whatever">`), never by
  path. `Mod.props` resolves them via `AssemblySearchPaths`.
- Systems subclass `Game.GameSystemBase` and are declared `partial` (the Entities source
  generators emit the other half).
- Registration order for `SkiResortAttractivenessSystem` is load-bearing. It must stay
  `UpdateAfter<SkiResortAttractivenessSystem, Game.Simulation.AttractionSystem>` at
  `GameSimulation` with `GetUpdateInterval` = 16. The reasons are in the system's header
  comment and `..\..\docs\research\code-mod.md`.
- No Harmony, no reflection into other mods (SPEC §4.1/§9). The research doc explains why we
  do not need it.
