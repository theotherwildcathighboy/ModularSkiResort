# RunsFirstRealizer: from a runs-first plan to outlines the game accepts

The runs-first planner draws centerlines. The Quick runs tool needs areas. `RunsFirstRealizer.Realize(plan, input, report)` makes the
areas. It returns an `AutoPlanResult` with a `ResortPlan`. The spec is `docs/research/quickruns-v2/port/REALIZER-SPEC.md` (local only).

The realizer does no planning. It never moves a line to make it better. It can narrow a line, trim it, split it, extend a junction end
by a few meters, or drop it. The `RealizeReport` records each loss. The tool does not read the report. The test host prints it.

## Files

- `RunsFirstRealizer.cs`: the entry point, the report, the elements, the kinds, the stable ids, the hash.
- `RunsFirstRealizer.Lattice.cs`: one lattice on the snapshot texels. It holds the region, the obstacles, the grade, and the distance
  to the nearest accepted piste and track with its owner.
- `RunsFirstRealizer.Pistes.cs`: pistes and graded pistes.
- `RunsFirstRealizer.Tracks.cs`: cat tracks. It rebuilds the traverse trees as chains.
- `RunsFirstRealizer.Lifts.cs`: one `PlanLift` row for each planner lift, with the tower plan of the lift tool.
- `RunsFirstRealizer.Graph.cs`: groups, pads, clusters, the served rule, the way home, dead ends, the mix, the relation gate.

The files are pure C# 9 for net48. They use no part of the `SkiRunAutoPlanner` class. The parts they need are copies.

## Order

1. Lattice, then the first steps of the refusal ladder (bad input, area, blocked share, vertical).
2. Lifts. A lift with towers that do not clear stays in the plan. It gets the flag `TowersUnchecked`.
3. Pistes. A run goes before the runs that start off it or end onto it. A cycle uses the list order.
4. Cat tracks. First the tree chains of each lift, then the graded runs under the blue line, then the links.
5. Groups and the graph.

## Element types

- A plain piste uses the width of its planned band. The march stops each side at the gap to other areas.
- A graded run is a graded piste when its grade along the path is 25 % or more and it drops. It is 15 m wide.
  The rater can answer `ShortRun` for it, because a diagonal has few full 20 m windows. The realizer accepts that answer for a graded
  piste. The game shows that band.
- All other connectors are cat tracks: 10 m wide, then 8 m when the ribbon crosses itself.

## Junctions

A piste end that is more than 20 m from its partner gets a straight extension toward it. The extension is at most 40 m. A blocked
cell or the gap of another area stops it. Then the report marks the end as unlinked.

Tracks butt against tracks. Tracks cross pistes. A new piste crosses an existing cat track and gets the flag `CrossesCatTrack`.

## Determinism

No clock and no random number has an effect on a decision. Every order is the list order, then the index. `RunsFirstRealizer.Hash`
covers every output bit. The host gate compares two runs.

## Test host

- `RealizerTests.cs` runs in the default host run. It checks the synthetic regions and eleven hand fixtures.
- `rf-realize <out dir> [regions file]` prints the report for each region. It also prints the score of the raw plan and of the
  realized plan.
