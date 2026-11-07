# SkiCoachSystem: the depots' visual coach

Citations: `tools/decompile/1.6.2f1/`. Research and rejected options: docs/research/ski-bus-visual-2026-10-31.md.

Each working depot (`SkiBusRules.IsBilled`, plus a `DepotView.CoachLodge`) gets one `Bus01` with `PublicTransportFlags.DummyTraffic`. The bus
has real lanes, lights and road wear. It has no passengers, line, depot trip or city bill. The bus is a visual only. Guests, tickets and the
bill never read it.

## The end of the line

`DepotView.CoachLodge` is the nearest passing hill lodge with `Building.m_RoadEdge`. `HillLodge`, which the bill measures to, may be a
Mountain Hut or Swanky Summit Lodge with no road. If no such lodge exists, the depot has no coach. The depot still works.

## The turnaround (anchor and interval are load-bearing)

Vanilla deletes a dummy at its path end. `VehicleUtils.PathEndReached` needs `EndOfPath | EndReached` (`VehicleUtils.cs:134-137`). The delete
is at `TransportCarAISystem.cs:366-397`. The AI runs on interval 16, offset 1 (`:1634-1642`), every vehicle every tick. The order in the
phase is:

1. `CarNavigationSystem` (`SystemOrder.cs:311`)
2. this system
3. the AI (`:317`)

`UpdateBefore` with the same interval inherits the offset (`UpdateSystem.cs:431-436`). `Coach [armed]` prints the frame's value, which must be 1.

The system reads the vehicles in a Burst job on the system's `Dependency`. The job runs after that frame's navigation and before the AI. On the
main thread, a read of `CarCurrentLane` waits for navigation. A measurement showed 2 ms per rendered frame at 3x in a city of 355,000
(docs/research/field/2026-11-01/PERF-BIG-CITY.md). The spawns read the job's list of coaches on the next tick. So the first tick after a load
spawns nothing, and a saved coach gets no twin. `Coach [cost]` logs once per load.

When the current lane has `EndOfPath`, the system does these steps in the same frame:

1. Set the target to the other end with `VehicleUtils.SetTarget` (Obsolete).
2. Clear `EndOfPath`.

The AI then skips the delete. `RequireNewPath` (`:480`) calls `FindNewPath`. This keeps one entity, with no pop. `SetupPathfind` clears the
same flag (`VehicleUtils.cs:228`).

The normal path respawns a coach that was removed anyway (road cut `:352-363`, or a missed turnaround). The respawn starts from the target the
coach last had, so it reads as a stop. `Coach [spawn]` carries the turnaround count. If spawns grow while turnarounds do not, the window is
being missed.

## Spawn

Vanilla never makes a vehicle before its path (`RandomTrafficDispatchSystem.cs:223-310`). A probe entity (`PathInformation` + `PathElement` +
`LivePath`) gets the answer. `PathfindJobs.cs:1237-1266` writes the buffer, clears `Pending` and sets `m_Origin` (null = no road). Then the
system adds the components of `TransportVehicleSelectData.cs:168-177` plus `TrafficSpawnerAISystem.cs:368-397` through `EndFrameBarrier`. It
adds `Game.Routes.Color` for the color (`TransportCarAISystem.cs:1101-1104`).

## Save

The saver's query excludes `LivePath` (`SerializerSystem.cs:171-209`). So the game does not write a probe that is alive at save time (no save
row). A load does not clear a probe either. `ClearSystem` destroys only entities with one of its listed components, `PrefabRef` among them
(`ClearSystem.cs:23-51`), and a probe has none. So OnGamePreload destroys every pending probe
(`Coach [probe] N pending road probe(s) destroyed before the load`). The bus itself saves like a vanilla dummy (it has `PrefabRef`). The system finds it again by `Owner` -> depot prefab. The
system removes a dummy bus whose owner no longer exists.

## Failure modes, all silent to the player and logged

- `Coach [off]`: prefab missing. This session has no coach.
- `Coach [no road]`: the probe failed. It retries every 16384 frames.
- `Coach [probe]`: no answer in 2048 frames. The system retries.
- An empty depot list holds for 32 ticks before the system removes coaches. The list is empty after a load until the first ski-day pass.
