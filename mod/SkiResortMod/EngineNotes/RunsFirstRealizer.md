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
- `RunsFirstRealizer.Close.cs`: the closed ends. It moves each end cap onto the outline that the end meets.

The files are pure C# 9 for net48. The parts they took from the v1 / v2 planner are copies. That planner was deleted in port round R2-d.

## Order

1. Lattice, then the first steps of the refusal ladder (bad input, area, blocked share, vertical).
2. Lifts. A lift is clear only when its tower plan is sound in the sense of the lift tool (`LiftTowerPlan.Sound`). Every span
   must clear. No tower can be over the cap of its sheave train, too steep, or past its carrier clearance. A lift that is not
   clear stays in the plan. It gets the flags `TowersUnchecked` and `HandFit`. The lift tool does not build it without the player.
3. Pistes. A run goes before the runs that start off it or end onto it. A cycle uses the list order.
4. Cat tracks. First the tree chains of each lift, then the graded runs under the blue line, then the links. The closed ends are the
   last step of this pass (see "Closed ends").
5. Groups and the graph. The planner sets `ToBottom` on each lift (the way to the bottom). The realizer measures the way to the
   bottom again on its own descent graph (the game's link rule, no lift ride). A lift reaches the bottom when a descent from its top
   ends on an area with its end in the base band. The band is the same as in `RunsFirstPlanner.Bottom.cs` (`m_BottomTop`).
   A lift has a way down when the game serves an area from its top or the planner gives a value other than `NoSkiing`. The realizer sets the warning
   `NoWayToBottom` when a lift with a way down does not reach the bottom on the realized plan. The report adds one line for each
   such lift, with the planner's value (`RealizeReport.LiftPlanned`). It also adds one line for each lift that reaches the bottom
   where the planner does not.
   `BottomCauses` (report only) names the cause for each lift where the two differ. The causes are:
   - a top that the game does not serve.
   - a planned piece that is dropped or lost (head, tail).
   - a link that the game does not make (the gap, or the junction rule of the target piste).

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
The exact check tests each pair of areas by the rule that accepted the pair. A piste that was accepted after the extended piste
uses its own partners and its own gap. Such a piste can converge on the extended piste as a partner, with only the merge gap.
The rule of the extended piste would call that an overlap, far from the extension.

## Closed ends

The passes above stop an end when the game links it. The game links two outlines that are 25 m apart (`LiftServedRule.LinkMeters`).
So a piste ended as far as 20 m from the piste it joins, and a track piece ended 7 m or more from the track that cuts it. A player
saw runs that end before they connect, and cat tracks in pieces (post-release round 1, T172 and T176).

`CloseEnds` runs after all pistes and tracks are accepted. It looks at each end of each area:

1. It finds the cap: the outline edge across the end of the centerline.
2. It puts a node on the cap every 2.5 m (`CloseNodeM`).
3. It moves each node along the direction of the end, at most 40 m (`CloseMaxM`). A node stops 3 m (`CloseGapM`) from an outline
   of its own kind. The game refuses two areas of one kind that touch. A node stops just inside an outline of the other kind. A piste
   and a cat track can overlap.
4. A node that meets nothing moves as far as the nearest node that met an outline. The cap stays one edge.
5. The new outline gets the same exact checks as the first outline. A piste must also keep its band. If a check fails, the pass tries
   the nodes that met an outline alone, then a straight cap moved to the first contact. If all fail, the end keeps its outline, and the
   report has one line.

Only the cap moves. The centerline, the half widths and the other nodes do not change.

A piste closes onto its partners only. Two pistes with no relation keep the gap of the density. A piste closes onto each track. A
track closes onto each piste and each track.

The report counts `PisteEndsClosed`, `TrackEndsClosed` and `CloseFailed`. The test host prints the result as a player sees it
(the SEEN line of `rf-realize`, `tests/AutoGenBatch/RealizerHost.Seen.cs`).

## The edge of the region

A cell within 2 m of the edge of the region is blocked. The cells are 3.5 m. A line that runs 2 to 5 m inside the edge passed
through blocked cells at some samples. Each such sample cut the run. The pieces were too short, and the realizer dropped them.
The upper part of the run then ended in the middle of the slope (T172).

The free tests (`FreePiste`, `FreeTrack`) now use `RegionOpen`. For a cell near the edge, it tests the sample itself: the
sample must be inside the region and clear of its edge. For each other cell, it reads the flag.

The planner's mask is its 10 m cells, so a planned line can run 1 to 3 m inside the edge. No outline fits there: the edge
clearance is 2 m, and a half width is 1 m or more. `OffEdge` moves each point of such a line straight in, to the clearance plus
1.5 m (`EdgeNudgeM`). A cat track needs its half width more. The move is small and happens only at the edge of the region.
A point outside the region does not move. The contact samples of the planner keep the planned line. The report counts the
lines in `EdgeNudged`.

## A piste that doubles back

The planner can ski a spur as the start of a run: the line goes out from the lift top, turns back, and passes its own start. One
outline cannot hold both legs, so the ring crossed itself, and the whole run was dropped (`Crosses`). The runs that ended onto it
then ended nowhere (T172).

`BendSplit` cuts such a stretch at its sharpest bend (90 degrees or more). The cut widens until its two ends are 19 m apart
(`BendOpenM`). If the line comes back within 60 m of its own start (`BendBackM`), the run starts there, and the way out is left
out. If not, both sides are pieces, and the second piece takes the first as a junction. A stretch is split at most twice.

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

- `RealizerTests.cs` runs in the default host run. It checks the synthetic regions and sixteen hand fixtures.
  It also checks six fixtures of the way to the bottom (`BottomFixtures`, through `RunsFirstPlanner.BottomProbe`). The checks on the
  synthetic regions and these fixtures are HARD. Each lift that the planner gives `Reaches` must reach the bottom on the realized
  plan. The warning must agree with the realized reach. A lift that reaches the bottom only on the realized plan is a REPORT line.
  The test does not use B-1: B-1 counts pads that touch the base band through the planner's run continuity, not the game's links.
- `rf-realize <out dir> [regions file]` prints the report for each region. It also prints the score of the raw plan and of the
  realized plan.
