# SkiCoachSystem: the depots' visual coach

Citations: `tools/decompile/1.6.2f1/`. Research and rejected options: docs/research/ski-bus-visual-2026-10-31.md.

One `Bus01` per working depot (`SkiBusRules.IsBilled`, plus a `DepotView.CoachLodge`) with `PublicTransportFlags.DummyTraffic`: real lanes, lights and
road wear, no passengers, line, depot trip or city bill. A visual only: guests, tickets and the bill never read it.

## The end of the line

`DepotView.CoachLodge` is the nearest passing hill lodge with `Building.m_RoadEdge`. `HillLodge`, which the bill measures to, may be a Mountain Hut or
Swanky Summit Lodge with no road. No such lodge: no coach, the depot still works.

## The turnaround (anchor and interval are load-bearing)

Vanilla deletes a dummy at its path end: `VehicleUtils.PathEndReached` needs `EndOfPath | EndReached` (`VehicleUtils.cs:134-137`), the delete is
`TransportCarAISystem.cs:366-397`. The AI runs on interval 16, offset 1 (`:1634-1642`), every vehicle every tick. Order in the phase: `CarNavigationSystem`
(`SystemOrder.cs:311`), then this system, then the AI (`:317`). `UpdateBefore` with the same interval inherits the offset (`UpdateSystem.cs:431-436`);
`Coach [armed]` prints the frame's value, which must be 1.

The vehicles are read in a Burst job on the system's `Dependency`, which runs after that frame's navigation and before the AI. On the main thread, a read of
`CarCurrentLane` waits for navigation: measured 2 ms per rendered frame at 3x in a city of 355,000 (docs/research/field/2026-11-01/PERF-BIG-CITY.md). The spawns
read the job's list of coaches on the next tick, so the first tick after a load spawns nothing and a saved coach gets no twin. `Coach [cost]`, once per load.

So when the current lane has `EndOfPath`, in the same frame: `VehicleUtils.SetTarget` to the other end (Obsolete), clear `EndOfPath`. The AI then skips the delete and
`RequireNewPath` (`:480`) calls `FindNewPath`, one entity, no pop. `SetupPathfind` clears the same flag (`VehicleUtils.cs:228`).

A coach removed anyway (road cut `:352-363`, or a missed turnaround) is respawned by the normal path, from the target it last had, so it reads as a stop.
`Coach [spawn]` carries the turnaround count: spawns growing while turnarounds do not mean the window is being missed.

## Spawn

Vanilla never makes a vehicle before its path (`RandomTrafficDispatchSystem.cs:223-310`). A probe entity (`PathInformation` + `PathElement` + `LivePath`) gets the answer:
`PathfindJobs.cs:1237-1266` writes the buffer, clears `Pending` and sets `m_Origin` (null = no road). Then the components of
`TransportVehicleSelectData.cs:168-177` plus `TrafficSpawnerAISystem.cs:368-397` through `EndFrameBarrier`, and `Game.Routes.Color` for the color
(`TransportCarAISystem.cs:1101-1104`).

## Save

The saver's query excludes `LivePath` (`SerializerSystem.cs:171-209`), so a probe alive at save time is not written: no save row, no cleanup. The bus itself saves like a
vanilla dummy (it has `PrefabRef`) and is found again by `Owner` -> depot prefab. A dummy bus whose owner no longer exists is removed.

## Failure modes, all silent to the player and logged

- `Coach [off]`: prefab missing, this session has no coach.
- `Coach [no road]`: the probe failed; retries every 16384 frames.
- `Coach [probe]`: no answer in 2048 frames, retried.
- An empty depot list holds for 32 ticks before coaches are removed: it is empty after a load until the first ski-day pass.
