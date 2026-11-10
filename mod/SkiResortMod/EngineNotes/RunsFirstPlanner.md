# RunsFirstPlanner: the runs-first resort planner

## What it is

The planner reads the terrain the way a person does. It finds drainages and spurs, top to bottom. It rates each line and picks a spaced set.
It places lifts to serve the picked lines. Then it makes the plan a skier sees: runs, cat tracks, links and lifts.
The scorecard of the test host judges this plan (`tests/AutoGenBatch/RunsFirst.Score.cs`, frozen).

The code is pure C# 9 on `System` and `Unity.Mathematics`. It has no `Game.*` type, no console, no file and no environment access.
The mod compiles it for net48. The test host links the same files for net8.0.

Files: `Systems/RunsFirstPlanner.cs` (entry, grid, lines, the pick) and one part file per topic.
The parts are Lifts, Plan, Graph, Beginner, Network, Fill, Cover, Bridge, Chords, Junctions, Heads, Heap, Graded, WayHome, Regrade, Layout, Learning, Order, Compat and Result.
`Systems/RunsFirstOptions.cs` holds the options. `Systems/RunsFirstResult.cs` holds the plain plan object and its hash.

## The entry

`new RunsFirstPlanner().Plan(snapshot, ring, options, cancel)` returns a `RunsFirstResult`.
It returns null when the cancel token is set.

- One instance plans one region. A second call to `Plan` on the same instance throws.
- The instance holds all the state of its plan. No field is static unless it is a constant or an immutable table.
  So two instances can plan at the same time on two threads.
- Options null means the defaults. The defaults are the scored defaults.
- `ProgressVolatile` is the progress in per-mille (0 to 1000). Any thread can read it while `Plan` runs. It never decreases. It is 1000 when `Plan` returns a plan.
- `Log` holds the debug and timing lines that the options ask for (none by default). Then it holds the plan log. `RunsFirstResult.Log` holds the plan log too. The hash does not read the log.
- `PassTimes` is the time of each pass in milliseconds. It is a report. No pass reads the clock.

## Passes and progress

The planner has 34 named passes. Progress is the count of passes done, divided by 34.
Three passes are long: the reach scan of the lift tops, the serve pick and the regrade.
In these passes the progress also adds the fraction of the pass that is done.
A pass that an option turns off still counts.

The passes in order:

| Stage | Passes |
|---|---|
| Terrain and lines | grid, flow, lines, pick |
| Lifts | tops, cover lifts, spread, lifts |
| Runs and network | serve, links, spurs, pods, bridge, learning pods, way home, regrade, learning ways |
| Coverage | fill setup, face lines, fill |
| Lift lines | chords, nudge, chord tracks, chord drop, peak way, spread |
| Finish | merge, prune, straight tracks, junctions, heads, learning areas, plan, result |

The reach scan of the lift tops runs on `ScanThreads` threads (`Parallel.For`, default 4). Each thread writes its results by index. So the plan is the same on any thread count.

## Cancel

The planner polls the token at each pass boundary. It also polls at each progress step in the long passes and every few thousand steps of the long loops.
After a request, `Plan` stops at the next poll and returns null. The half-made state stays in the instance. Discard the instance.
On the largest test region (big-3, 25 km2) the test host measured the poll gaps and the time from the request to the return. The bound is 250 ms.

## The options object

`RunsFirstOptions` holds every knob of the planner, about 110 fields, with the scored defaults.
The planner reads only this object. The test host fills it from the `RF_*` environment switches (`RunsFirst.HostOptions`).
The mod must pass its own object and never read the environment.

## Inputs from the game

`Systems/RunsFirstPlanner.Inputs.cs` holds `RunsFirstInputs`. The tool sets it on the instance before `Plan` (`planner.Inputs`).
It has a list of blocked shapes, a water flag and the access points.

- A shape with 3 or more points is a ring. A shape with 2 points is a segment. A shape with 1 point is a point. Each shape has a clear distance.
- The grid pass removes each cell of a blocked shape from the region mask. With the water flag, it also removes each cell that has a wet texel.
- All passes read this mask, so no run, lift or track enters a blocked cell.
- The open area of the region does not include the blocked cells. The density caps use the open area.
- No pass reads the access points yet.
- The planner has no fixed lift. The tool blocks the stations and the line of each existing lift.

Null inputs or empty inputs change nothing. The golden hashes stay the same (`tests/AutoGenBatch/WorkerTests.cs` checks it).

## The golden

`RunsFirstResult.Hash()` is the hash of a plan. The test host checks two sets of hashes:

- `tests/AutoGenBatch/RunsFirstGolden.cs`: three synthetic regions in the default run. Each plan must give the same hash on a second run, on 1 and on 4 scan threads, and the recorded constant.
- `rf-golden`: the 29 real regions of the seven region sets. The recorded hashes are in the private docs (`port/r0c/step4/golden.txt`).

`tests/AutoGenBatch/RunsFirstPlannerTests.cs` checks the planner object. Two planners plan at the same time on two threads. A canceled plan returns null within the bound. The progress never decreases and ends at 1000.

A change to the planner logic changes the hashes. Make such a change in its own round, with a score table.

## What stays in the test host

- `tests/AutoGenBatch/RunsFirst.Host.cs`: the modes `rf`, `rf-golden`, `rf-holdout` and `rf-cancel`, the regions files, `HostOptions` and `PlanRegion`.
- `tests/AutoGenBatch/RunsFirst.Render.cs`: a part of `RunsFirstPlanner` that only the test host compiles. It holds `RunRegion` (the v2 plan of the same region, the tables, the score and the PNG) and the debug prints `V3Debug` and `TurnDebug`.
- The scorecard `RunsFirst.Score.cs`.

The debug prints in the planner files (the `Debug*` options) go to `Log`, or to the console in the test host.
