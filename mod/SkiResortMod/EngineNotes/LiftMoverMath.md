# LiftMoverMath: chair loop position, far-loop pace (stride) and lane scope

Pure arithmetic of the chair mover's far-loop pace and lane scope. It uses no Game or Entities type (Unity.Mathematics and System
only). So tests/LiftBatch links it (LoopPaceTests.cs).

## 1. Loop position
A riding chair's place on its loop is a pure function of the render clock:

    s = (offset + speed x TimeS) % L        (ChairPinSystem: PhaseOf = speed x TimeS, then the modulo)

So a loop that skipped passes is exactly where an unthrottled loop is on the pass that writes it again. Nothing accumulates. There is
no per-chair state to drift. `LoopPosition` is that expression, bit for bit.

## 2. Far-loop pace
The mover writes a loop whose box is at least FarMinM from the camera every Stride-th moving pass. Stride is the largest k <= MaxStride
for which k passes of travel stay under MaxStepRadians x distance. A far chair never steps more than about one pixel. 0.0005 rad is
0.73 px at a 60 degree vertical field of view on 1,440 lines, and 1.2 px at 35. Travel per pass = speed x the render-clock delta of the
pass. It grows with sim speed and shrinks with fps, so the stride adapts to both. The mover writes near loops (Stride 1) every pass.
- A far loop counts passes since its last write (SinceWrite). The mover writes it when SinceWrite >= Stride. A rebuild seeds the
  countdown per loop (loop index x 3 mod MaxStride), so far loops do not all land on the same pass.
- On the pass where a loop comes back near, the mover writes it at once, at the exact unthrottled pose (item 1).
- Bounds refresh follows the pace.
  - A loop's chairs get one Updated tag when its own travel since its last tag reaches BoundsRefreshMeters x Stride (2 m near, at most
    16 m far). The tag happens only on a pass that writes the loop.
  - The refresh is per loop, with the loop's own speed. A single global cadence at the fastest loop's speed tagged a fixed-grip loop
    next to a gondola 2.2x too often.
  - A loop that comes back near is tagged on that very pass whenever its untagged travel is >= 2 m. So near the camera, CullingInfo /
    the search tree are never more than 2 m + one pass of travel stale.

## 3. Cost percentiles
For the "[cost] chair mover" line: p50 / p95 / max of a sample (nearest-rank, on a copy).

## 4. Lane scope
The cable lanes of the lifts hang off the lift-line edges and their end nodes (SubLane buffers). The owners to visit are the census's line
edges plus each edge's two nodes, each once (`OwnersOf`). The load-time watchdog compares the full city-wide scan with the scoped harvest
(`CompareHarvests`). If the scan kept a lane and the scoped harvest missed it, that is a WARN. The watchdog then switches that system
back to the full scan for the rest of the load.
