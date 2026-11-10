# RunsFirstPlanner: the runs-first resort planner

## What it is

The planner reads the terrain the way a person does. It finds drainages and spurs, top to bottom. It rates each line and picks a spaced set.
It places lifts to serve the picked lines. Then it makes the plan a skier sees: runs, cat tracks, links and lifts.
The scorecard of the test host judges this plan (`tests/AutoGenBatch/RunsFirst.Score.cs`, frozen).

The code is pure C# 9 on `System` and `Unity.Mathematics`. It has no `Game.*` type, no console, no file and no environment access.
The mod compiles it for net48. The test host links the same files for net8.0.

Files: `Systems/RunsFirstPlanner.cs` (entry, grid, lines, the pick) and one part file per topic.
The parts are Lifts, Plan, Graph, Beginner, Network, Fill, Cover, Bridge, Chords, Junctions and Heads.
More parts are Heap, Graded, WayHome, Bottom, Regrade, Layout, Learning, Order, Compat, Rater, Towers and Result.
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

The planner has 37 named passes. Progress is the count of passes done, divided by 37.
Four passes are long: the reach scan of the lift tops, the serve pick, the regrade and the towers.
In these passes the progress also adds the fraction of the pass that is done.
A pass that an option turns off still counts.

The passes in order:

| Stage | Passes |
|---|---|
| Terrain and lines | grid, flow, lines, pick |
| Lifts | tops, cover lifts, spread, lifts |
| Runs and network | serve, links, spurs, pods, bridge, learning pods, way home, way to bottom, regrade, learning ways |
| Coverage | fill setup, face lines, fill |
| Lift lines | chords, nudge, chord tracks, chord drop, peak way, spread |
| Finish | merge, prune, straight tracks, junctions, heads, learning areas, plan, towers, bottom, result |

The reach scan of the lift tops and the tower plans of the towers pass run on `ScanThreads` threads (`Parallel.For`, default 4). Each thread writes its results by index. So the plan is the same on any thread count.

## Cancel

The planner polls the token at each pass boundary. It also polls at each progress step in the long passes and every few thousand steps of the long loops.
After a request, `Plan` stops at the next poll and returns null. The half-made state stays in the instance. Discard the instance.
On the largest test region (big-3, 25 km2) the test host measured the poll gaps and the time from the request to the return. The bound is 250 ms.

## The options object

`RunsFirstOptions` holds every knob of the planner, about 110 fields, with the scored defaults.
The planner reads only this object. The test host fills it from the `RF_*` environment switches (`RunsFirst.HostOptions`).
The mod must pass its own object and never read the environment.

## Bands

The game shows the band that the mod's rater gives the outline of a run. Scorecard v7 reads the same band.
`Systems/RunsFirstPlanner.Rater.cs` holds `RaterBand`. It builds a ribbon around the points of a run and rates the ribbon with `SkiRunRater`.
The ribbon is the scorecard's ribbon. It follows the path at a 4 m step and has a constant half width of 15 m.
A ribbon that the rater cannot rate is double black. A ribbon with a mask that is not connected is double black too.
`RaterBand` copies the rule of the scorecard line for line. So the planner and the scorecard give the same band to the same points.
`RaterBand` keeps each band in a cache, by the points of the run. Each new rating polls the cancel token.

The option `RaterBands` tells which decisions read the rater. Each decision has one bit. The value 0 gives the plans from before the rater.
The default is 131. It has three bits:

- 1: the band of each plain run in the result. The realizer plans the width of a run for this band.
- 2: the ski graph. The way home, the beginner terrain, the learning km and the checks of each cut read it.
- 128: the more green runs of a learning chair.

The other bits lowered a set mean, so they stay off. They are green ways down and pods (4), the serve value (8) and link runs (16).
They are also chords (32), the line pick (64) and the first run of a learning chair (256).
Graded tracks and diagonals keep their band along the path. Natural marks keep the ground band, because the rater's double black would mark about twice the km natural.

## Towers

The realizer runs the mod's tower planner (`LiftTowerPlanner`) on each planned lift. The lift queue builds a line in two cases:

- The tower plan is Sound.
- Exactly one tower is over its sheave-train limit and nothing else is wrong. The queue builds the line and flags it.

Any other line waits for a fit by hand.

`Systems/RunsFirstPlanner.Towers.cs` makes the same tower plan for each planned lift. The call is a copy of the realizer's call. For a lift that is not Sound, the pass tries these steps in this order:

1. Move one station, or the whole line across, by 10 m to 30 m. The pass takes the smallest move that makes the plan Sound.
2. Change the type on the same line: a fixed grip to a detachable or a gondola, a detachable to a gondola. A gondola must stay within the aerial allowance of the scorecard.
3. Keep a flagged plan.

Each change must keep all of these:

- The rules of the line (`LineFits`).
- The runs of the lift, by the served rule of the game.
- The runs that come home to its bottom, by the rule of the realizer.
- The ski graph.
- The share of the line on runs.
- The terminals, the stations under a rope, and the parallel and stacked pairs of the scorecard.

A lift that still needs a fit by hand stays. The plan log marks it. The pass caches each tower plan by its line, and polls the cancel token. The option `Towers` (default on) turns the pass on.

The way home has a search budget of its own (`WayHomeBudget`, a count of cells). So the regrade and the learning ways keep what the shared budget has left.

## Way to the bottom

From the top of every lift with skiing, a run of any rating or a cat track goes to the bottom of the region.
The bottom is the base band: the lowest 15 % of the relief of the open region cells, 10 m to 60 m. The realizer uses the same band.
A skier gets there on runs and cat tracks only. A lift ride does not count, and a download lift does not count.

`Systems/RunsFirstPlanner.Bottom.cs` holds the pass. It runs after the way home. The steps:

1. The ski graph marks each element that ends in the band, or that leads to such an element. A lift reaches the bottom when an element from its top is marked.
2. For each lift that does not reach the bottom, lowest bottom station first, the pass searches uphill. The search starts at the bottom landing and at each marked element. It uses the blue limits first, then the green limits. A hit becomes a green cat track or a blue diagonal, with the rules of the way home.
3. If the search finds nothing, the pass follows the drainage from the low end of the skiing of the lift. If the run rules hold down to the band, this line becomes a plain run (kind `bottom`, band from the rater).
   If the drainage fails, the pass searches uphill once more under the run rules instead of the track limits. A hit becomes a plain run of any rating (kind `bottom`).
4. If that fails too, the lift stays. The plan log names the lift and the cause.

The bottom landing is the first class that has cells:

1. Lift bottoms in the band.
2. Flat band ground (grade under 8 %) within 150 m of an access point (`RunsFirstInputs.Access`).
3. Other flat band ground.
4. All band ground.

The pass has a search budget of its own (`BottomBudget`, a count of cells). The option `WayToBottom` (default on) turns the pass on. The prune does not remove a run of kind `bottom`.

After the towers pass, the `bottom` pass reads the finished plan again. It sets `RunsFirstResult.Lift.ToBottom` for each lift (the enum `RunsFirstBottom`, in the hash):

| Value | Meaning |
|---|---|
| Reaches | The skiing from the top gets to the band. |
| NoSkiing | Nothing leaves the top. |
| Cliff | The drainage below the skiing has a pitch over the run rules. |
| Water | The drainage meets a wet cell that the mask removed. |
| Hole | The drainage meets other removed ground, with region below it. |
| Edge | The drainage leaves the region above the band. |
| Basin | The drainage climbs out of a basin, more than the run rules allow. |
| Other | None of the above, for example a search past its budget. |

The realizer sets the warning `NoWayToBottom` when a lift has a value other than Reaches or NoSkiing.

## Inputs from the game

`Systems/RunsFirstPlanner.Inputs.cs` holds `RunsFirstInputs`. The tool sets it on the instance before `Plan` (`planner.Inputs`).
It has a list of blocked shapes, a water flag and the access points.

- A shape with 3 or more points is a ring. A shape with 2 points is a segment. A shape with 1 point is a point. Each shape has a clear distance.
- The grid pass removes each cell of a blocked shape from the region mask. With the water flag, it also removes each cell that has a wet texel.
- All passes read this mask, so no run, lift or track enters a blocked cell.
- The open area of the region does not include the blocked cells. The density caps use the open area.
- The way to the bottom reads the access points (landing class 2). No other pass reads them.
- The planner has no fixed lift. The tool blocks the stations and the line of each existing lift.

Null inputs or empty inputs change nothing. Access points alone can change only the landing of the way to the bottom. The golden hashes stay the same (`tests/AutoGenBatch/WorkerTests.cs` checks it).

## The golden

`RunsFirstResult.Hash()` is the hash of a plan. The test host checks two sets of hashes:

- `tests/AutoGenBatch/RunsFirstGolden.cs`: three synthetic regions in the default run. Each plan must give the same hash on a second run, on 1 and on 4 scan threads, and the recorded constant. Each plain run of the plan must also carry the band that the scorecard gives it.
- `rf-golden`: the 29 real regions of the seven region sets. The recorded hashes are in the private docs (`port/r2c/golden.txt`).

`tests/AutoGenBatch/RunsFirstPlannerTests.cs` checks the planner object. Two planners plan at the same time on two threads. A canceled plan returns null within the bound. The progress never decreases and ends at 1000.

A change to the planner logic changes the hashes. Make such a change in its own round, with a score table.

## What stays in the test host

- `tests/AutoGenBatch/RunsFirst.Host.cs`: the modes `rf`, `rf-golden`, `rf-holdout` and `rf-cancel`, the regions files, `HostOptions` and `PlanRegion`.
- `tests/AutoGenBatch/RunsFirst.Render.cs`: a part of `RunsFirstPlanner` that only the test host compiles. It holds `RunRegion` (the v2 plan of the same region, the tables, the score and the PNG) and the debug prints `V3Debug` and `TurnDebug`.
- The scorecard `RunsFirst.Score.cs`.

The debug prints in the planner files (the `Debug*` options) go to `Log`, or to the console in the test host.
