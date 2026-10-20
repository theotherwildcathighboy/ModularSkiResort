# SkiResortMod — code-mod half of Modular Ski Resort

C# code mod for Cities: Skylines II. This DLL owns the *dynamic* behavior of the hybrid mod
(SPEC §7): slope-classified trails, snow-gated operation, and size-scaled resort
attractiveness. The placeable pieces live in the asset pack, authored separately in the
in-game Editor.

Status: **compiling skeleton** (Phase 2 of the roadmap in `..\..\docs\SPEC.md` §13). Both
gameplay systems are registered but disabled stubs; their header comments carry the
implementation contract. The attractiveness patching research behind the scheduling decisions
is in `..\..\docs\research\code-mod.md` — read it before touching system registration.

## Layout

```
SkiResortMod/
├── SkiResortMod.csproj              official toolchain project (see notes below)
├── Mod.cs                           IMod entry point: logging, settings, system registration
├── Settings/
│   ├── SkiResortSettings.cs         ModSetting-derived options (Options → Mods in game)
│   └── LocaleEN.cs                  English strings for the options UI
├── Systems/
│   ├── SkiResortAttractivenessSystem.cs   core aggregation system (stub, disabled)
│   └── SnowLineSystem.cs                  climate gating (stub; logs climate constants)
└── Properties/
    ├── PublishConfiguration.xml     Paradox Mods metadata — used only by `dotnet publish`
    ├── Thumbnail.png                placeholder from the template
    └── PublishProfiles/             publish profiles from the template
```

## Build

```powershell
cd "%USERPROFILE%\Documents\ClaudeProjects\ModularSkiResort\mod\SkiResortMod"
dotnet build
```

That is the whole loop. The official `Mod.props`/`Mod.targets` (imported from
`%CSII_TOOLPATH%`, i.e. `...\Cities Skylines II\.cache\Modding\`) provide everything else:

1. **Compile** against the real game DLLs — references are resolved by simple name from
   `%CSII_MANAGEDPATH%` (the game's `Cities2_Data\Managed`), target `net48`, C# 9, with the
   Unity Entities 1.3.10 source generators wired in as analyzers.
2. **Post-process** — `ModPostProcessor.exe` runs on the built DLL (Burst etc., `-p Windows
   -p macOS -p Linux`).
3. **Deploy** — the `DeployWIP` target wipes and re-copies the output to
   `%CSII_LOCALMODSPATH%\SkiResortMod`
   (`...\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SkiResortMod`).
   No manual copying, ever.

Then launch the game and enable the mod under **Options → Mods**. Logs land in
`%CSII_USERDATAPATH%\Logs\` — our logger writes `SkiResortMod.Mod.log`. On loading a save,
`SnowLineSystem` logs the climate constants needed to validate the SPEC §6.6 snow formula.

There is no hot reload: rebuild, then restart the game.

### Machine-specific note: .NET 6 vs .NET 8

`ModPostProcessor.exe` targets the .NET 6 runtime, which is **not installed** on this machine
(only .NET 8 is), and its runtimeconfig carries no roll-forward policy — so a stock csproj
fails after the compile step with *"You must install or update .NET"*. The csproj here
overrides the `RunModPostProcessor` target (the customization mechanism documented at the top
of `Mod.targets`) to run the same command with `DOTNET_ROLL_FORWARD=LatestMajor`. On a machine
that has .NET 6 the variable is a no-op. The override also pins `BeforeTargets="DeployWIP"` so
the post-processed DLL — not the raw one — is what gets deployed.

## Do NOT run `dotnet publish`

Publish invokes `ModPublisher.exe` against `Properties\PublishConfiguration.xml` and uploads
to Paradox Mods. The configuration is placeholder-grade and the mod is nowhere near
releasable. (SPEC §16 has the details.)

## Editing rules that keep this project healthy

- Add game references by **simple name** (`<Reference Include="Colossal.Whatever">`), never by
  path — `Mod.props` resolves them via `AssemblySearchPaths`.
- Systems subclass `Game.GameSystemBase` and are declared `partial` (the Entities source
  generators emit the other half).
- Registration order for `SkiResortAttractivenessSystem` is load-bearing — it must stay
  `UpdateAfter<SkiResortAttractivenessSystem, Game.Simulation.AttractionSystem>` at
  `GameSimulation` with `GetUpdateInterval` = 16. The reasons are in the system's header
  comment and `..\..\docs\research\code-mod.md`.
- No Harmony, no reflection into other mods (SPEC §4.1/§9). The research doc explains why we
  don't need it.
