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
2. Lifts. A lift is clear only when its tower plan is sound in the sense of the lift tool (`LiftTowerPlan.Sound`). Every span
   must clear. No tower can be over the cap of its sheave train, too steep, or past its carrier clearance. A lift that is not
   clear stays in the plan. It gets the flags `TowersUnchecked` and `HandFit`. The lift tool does not build it without the player.
3. Pistes. A run goes before the runs that start off it or end onto it. A cycle uses the list order.
4. Cat tracks. First the tree chains of each lift, then the graded runs under the blue line, then the links.
5. Groups and the graph.

## Element types

- A plain piste uses the width of its planned band. The march stops each side at the gap to other areas.
- The rater can give a plain piste a band that is harder than the planned band. Then the realizer tries the ring again at 75 %
  and at 50 % of its half widths, but never under 7.5 m. It keeps the widest ring with the planned band. If no ring has the planned
  band, the full-width ring stays with its own band. The stored band is always the band from the rater. The centerline does not move.
- Fair share applies where another run goes alongside: under 45 degrees, with no crossing within 60 m. There, each side stops at
  half the distance between the two centerlines, minus half the gap. The floor is 7.5 m. At a crossing, the earlier run keeps its width.
- The grade does not cut a centerline. The planner already checked the line. The grade ceiling only limits the lateral march, and
  never under 7.5 m.
- Near the region edge and the map margin, the free tests use the exact edge, not the 3.5 m cells. So the realizer clips a stretch
  and does not drop a ring that goes outside.
- A ring that crosses itself gets one more try at a constant 7.5 m each side.
- The rater can answer with a mask that is not connected. Then it uses its P90 band, and the game shows that band. The realizer
  keeps the piece.
- A graded run is a graded piste when its grade along the path is 25 % or more and it drops. It is 15 m wide.
  The rater can answer `ShortRun` for it, because a diagonal has few full 20 m windows. The realizer accepts that answer for a graded
  piste. The game shows that band.
- A cut piece with a drop of less than 50 m is a remnant. The realizer keeps a remnant when both ends connect to something. An
  end connects at a junction, at the end of the source, near a lift bottom, or in the bottom band. An end within 20 m
  of the next piece of the same line also connects. A remnant with a free end is a stub. The realizer drops it. The report counts the two cases.
- All other connectors are cat tracks: 10 m wide, then 8 m when the ribbon crosses itself.

## Junctions

The partners of a piste are the pistes that the planner relates to it and the pistes that its line enters. Two more types of piste
are partners. The first type is a piste that the line comes near with no relation. Two fall lines converge there. The piece ends with
a junction on that piste, and the line continues past it as a new segment. The second type is an earlier segment of the same piste.

A piste end that is more than 20 m from its partner gets a straight extension toward it. The extension is at most 40 m. A blocked
cell or the gap of another area stops it. Then the report marks the end as unlinked.

Tracks butt against tracks. Tracks cross pistes. The planner can relate an original track end to a piste or a track. If that end
is not a game link, it gets a straight extension toward that element. It can go into a piste. It stops at the link. A new piste crosses an existing cat track and gets the flag `CrossesCatTrack`.

After the track pass, one more pass examines each relation of a kept piste end that is not a game link yet. The piste end gets a
straight extension toward the related track or piste. The extension is at most 40 m and 7.5 m each side. The kept widths do not
change. The extended ring gets the exact check and the rating again. If the extension comes near a piste with no relation, that
piste becomes a partner.

## Two tracks in one corridor

The game does not let two cat tracks overlap. So two chains in one corridor are one track. The corridor of an accepted track is
its outline plus 7 m. A stretch of a later chain in that corridor belongs to the accepted track. The realizer builds the stretch
once, as part of the accepted track. The report counts its length as carried. The later chain keeps its own stretches. Each own
stretch butts onto the accepted track at the split.

A chain with no own piece merges into the tracks that hold its corridor. These are its hosts. At least half of the chain must be
in a corridor, and no own piece can have failed. A merged chain adds no area and is not a drop. Its element takes the areas of its
hosts. Thus a piste or a track that the planner relates to the merged chain reaches the hosts and links to them. The relation
gate also tests the relations of the merged chain itself. Each one must be a game link between a host and the related element.

A piece can still cross itself after the split at its sharpest bend. Then the legs of a switchback are closer than the track
width plus the gap. The realizer accepts the longest prefix that is clear. The rest of the piece is tested against the lattice
with that prefix in it. The stretch beside the prefix is lost. The free stretches after it become pieces that butt onto the
prefix. A chain gets at most eight of these splits.

A relation of a merged chain can stay without a game link to any host. Then the realizer tries a spur. A spur is a short straight
track from the nearest host toward the related element. It starts in the gap of the host and stops at the link. A blocked cell or
the gap of another track stops it, and then no spur is built. One track between the host and the related element is also a link
for a merged chain. A reached end can make the ribbon of a piece cross itself. Then the piece goes without the reached end, and
that end gets a spur.

## Determinism

No clock and no random number has an effect on a decision. Every order is the list order, then the index. `RunsFirstRealizer.Hash`
covers every output bit. The host gate compares two runs.

## Test host

- `RealizerTests.cs` runs in the default host run. It checks the synthetic regions and fifteen hand fixtures.
- `rf-realize <out dir> [regions file]` prints the report for each region. It also prints the score of the raw plan and of the
  realized plan.
