# SkiResortAttractivenessWatchSystem: why placement tags need a ModificationEnd watcher

The watcher tells `SkiResortAttractivenessSystem` that a lodge, lift station, Welcome Sign, ski school, patrol hut or ski run of
ours was placed, changed or removed. So scores follow a placement on that system's next 16-frame tick instead of its 4096-frame
backstop. The request is structural: `RecomputePending` means "a structural recompute is pending". So a placement is never held
behind a pending soft one.

## Why a separate system (decompile 1.6.0f1)

MainLoop runs through `UpdateWorld` (ToolSystem, then ModificationSystem, SystemOrder.cs:58, 60). After that, it runs Cleanup
(GameManager.cs:709 -> :2391, :711 -> :2399). `CleanUpSystem` (SystemOrder.cs:54) destroys the frame's Deleted entities and strips
Created / Updated / Applied (CleanUpSystem.cs:24-32, :52-53). GameSimulation runs only later in the frame (GameManager.cs:721 -> :2407,
SystemOrder.cs:74, SimulationSystem.cs:286). So a tool-made tag never reaches a GameSimulation system. This is the same fact as in
`SkiRunBindTriggerSystem` ("the GameSimulation sweep never sees it").

## Why ModificationEnd (registration in Mod.cs)

`ModificationSystem` runs ModificationEnd last (ModificationSystem.cs:19-26) inside MainLoop, paused or not, before Cleanup. A plain
`UpdateAt` sorts after every vanilla `UpdateAt` of the phase (mods register after `SystemOrder.Initialize`, UpdateSystem.cs:39-47,
158-161). So this frame's tool output (applied through the `ToolOutputBarrier` inside ToolUpdate) and the output of every Modification
phase are already present. Deleted entities still exist. The watcher sets no update interval. `ModificationSystem` calls the
non-indexed `UpdateSystem.Update(phase)`, which ignores any interval (UpdateSystem.cs:183-221).

## What it watches

The watcher uses two queries. Both use Any {Created, Updated, Deleted} and None {Temp}. The buildings query matches {Building, PrefabRef}.
The areas query matches {Area, PrefabRef}. Removals count too. Both queries match vanilla entities. So managed code filters the result
against the attractiveness system's prefab sets:

- `IsWatchedBuildingPrefab` = lodge, lift station, Welcome Sign, ski school and patrol huts, through `SkiPrefabCatalogSystem.IsUpgradeBuilding`.
- `IsWatchedAreaPrefab` = `SkiRunOwnershipSystem.IsRunPrefab`.

`IsEmptyIgnoreFilter` keeps idle frames free of any job sync. On an event frame, the only sync is PrefabRef's writers
(`ToComponentDataArray`, as in the bind trigger's `AnyRunPrefab`). Nothing is scanned while a structural recompute is already pending.

## What it calls

The watcher calls `SkiResortAttractivenessSystem.RequestRecompute(reason)`. While the game is paused (selectedSpeed 0, no GameSimulation
frame), this watcher runs the recompute at once through `RecomputeWhilePaused`. So a lodge placed while paused links to its sign
immediately. Bindings, lift census, ratings, run open states, settings, sign efficiency, lodge on/off and lift power raise no tag. The
attractiveness system compares them on its own tick.

## Logs

The watcher writes these log lines:

- "created"
- one ARMED line per load
- the first trigger of each reason per load, which `RequestRecompute` logs (later triggers are counted in the hub DUMP)
- a one-shot "!!!", which disables the watcher until the next load

The watcher runs in game mode only. It has a `GameManager.isGameLoading` guard.
