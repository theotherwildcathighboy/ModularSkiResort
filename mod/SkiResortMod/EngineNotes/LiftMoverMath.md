# LiftMoverMath: chair loop position, far-loop pace (stride) and lane scope

Pure arithmetic of the chair mover's far-loop pace and lane scope. No Game or Entities type (Unity.Mathematics and System only), so
tests/LiftBatch links it (LoopPaceTests.cs).

## 1. Loop position
A riding chair's place on its loop is a pure function of the render clock:

    s = (offset + speed x TimeS) % L        (ChairPinSystem: PhaseOf = speed x TimeS, then the modulo)

so a loop that skipped passes is exactly where an unthrottled loop is on the pass it is written again: nothing accumulates, there is no
per-chair state to drift. `LoopPosition` is that expression, bit for bit.

## 2. Far-loop pace
A loop whose box is at least FarMinM from the camera is written every Stride-th moving pass, where Stride is the largest k <= MaxStride
for which k passes of travel stay under MaxStepRadians x distance (a far chair never steps more than about one pixel: 0.0005 rad is
0.73 px at a 60 degree vertical field of view on 1,440 lines, 1.2 px at 35). Travel per pass = speed x the render-clock delta of the pass
(it grows with sim speed and shrinks with fps, so the stride adapts to both). Near loops (Stride 1) are written every pass.
- A far loop counts passes since its last write (SinceWrite) and is written when SinceWrite >= Stride; the countdown is seeded per loop at
  a rebuild (loop index x 3 mod MaxStride) so far loops do not all land on the same pass.
- The pass a loop comes back near writes it at once, at the exact unthrottled pose (item 1).
- Bounds refresh follows the pace: a loop's chairs get one Updated tag when its own travel since its last tag reaches BoundsRefreshMeters
  x Stride (2 m near, at most 16 m far), and only on a pass that writes it. It is per loop, with the loop's own speed (one global cadence
  at the fastest loop's speed tagged a fixed-grip loop next to a gondola 2.2x too often). A loop that comes back near is tagged on that
  very pass whenever its untagged travel is >= 2 m, so near the camera CullingInfo / the search tree are never more than 2 m + one pass
  of travel stale.

## 3. Cost percentiles
For the "[cost] chair mover" line: p50 / p95 / max of a sample (nearest-rank, on a copy).

## 4. Lane scope
The cable lanes of the lifts hang off the lift-line edges and their end nodes (SubLane buffers). The owners to visit are the census's line
edges plus each edge's two nodes, each once (`OwnersOf`). The load-time watchdog compares the full city-wide scan with the scoped harvest
(`CompareHarvests`): a lane the scan kept and the scoped harvest missed is a WARN and switches that system back to the full scan for the
rest of the load.
