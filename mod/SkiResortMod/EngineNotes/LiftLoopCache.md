# LiftLoopCache: how drawn cable lanes become closed travel loops and open shuttle paths

`LiftLoopCache` orders a drawn chairlift's cable lanes into closed travel loops (Loop mode) and builds open two-chain paths for shuttles
(Shuttle mode).

It is pure topology/geometry over plain data. `ChairPinSystem` hands in every HV cable lane owned by the chairlift net, plus the station entities.
The owners are edges AND nodes, because both carry PrefabRef to the net prefab. The topology (chaining, terminal classification, pairing,
base / top, up lane, orientation, gap check, common type) lives in the pure LiftLoopTopology.cs (see LiftLoopTopology.md). This class
is its Game-side adapter:
- It interns PathNodes to int ids (equal ids <=> PathNode.Equals: the 64-bit m_SearchKey, Game.Pathfind/PathNode.cs:62-65, owner =
  GetOwnerIndex :113-116).
- It pre-samples each lane's ends with MathUtils.Position / Tangent, runs the topology, and writes the pair notes and loops in pair order.
- tests/LiftBatch LC-REF proves the topology against a frozen transcription.

## What the topology relies on
1. Lanes chain end-to-end by exact PathNode equality. This is sound because LaneSystem's FindAnchor overwrites BOTH the endpoint position
   and the endpoint PathNode with the anchor's own (LaneSystem.cs:4049-4072 / 4117-4120). So two lanes meeting at a tower sheave or
   station bullwheel stub share a bitwise-identical PathNode. Chain direction is per-lane (m_Start/m_End orientation is NOT consistent
   along one drawn lift).
2. Station terminals are recognized by PathNode owner index == station entity index (the anchor PathNode is owned by the station
   building. PathNode.GetOwnerIndex is public). The walk STOPS at station anchors. Load-bearing: two lifts sharing a station also
   share that station's stub PathNodes. A walk that only classified terminals would run through the shared station. It would merge both
   lifts into one mega-loop (or un-close the first lift while the second is under construction).
3. The two chains that run between the same two stations (a lift's up-side and down-side cables) pair up. They close into a loop with two
   synthesized bullwheel arcs. The arcs are semicircles between the chains' terminal endpoints, bulging in the direction of travel (into
   the station, around the wheel). Arcs are analytic circles, not beziers, so constant-speed parametrization is exact
   (theta = distance / radius).
4. Chair offsets live in the caller (`ChairPinSystem`). The spacing is even. When the loop is unchanged, the offsets carry forward by
   entity across rebuilds (zero jump by construction). Otherwise the offsets use a plain deterministic grid.

The class reports back chairs whose owner edge is not part of any complete loop (unfinished lifts, dead-end lines, station missing) for
static pinning. This is per-lift graceful degradation, never an exception.

## Lengths
All lengths run in double (loop phase accumulates speed * frameIndex, and float32 would quantize at meter scale by frame ~1e7).
Per-segment lengths use the same metric as MathUtils.ClampLength (Curve.m_Length for lanes, exact pi*r for arcs), so sampling never runs
past a segment end.

## Data carried for callers
- `Loop.m_BaseStation` / `m_TopStation`: the two station entities of a closed loop. "Base" here is the loop cache's own height tie-break
  (lower terminal). The hub's base is by prefab, so `ChairPinSystem.TryGetLoop` matches the pair unordered.
- `ProjectOnOwner(loop, owner, position)`: the loop arc parameter of a point projected onto the lane segments of one owner (edge). So a
  chair parked on its own section starts riding from where it stands when the lift closes again.
- `CableLane.m_TypeIndex` = the `SkiCatalog.Lifts` index of the lane owner's line prefab (ChairPinSystem harvests the lanes of Loop-mover
  types only). `Loop.m_TypeIndex` = the common type of its member lanes, else -1 with a build note "loop between stations S and T mixes
  lift types - not moving". ChairPinSystem gives such a loop no roster: its chairs static-pin. ChairPinSystem sets `Loop.m_SpeedMps`
  from the row.
- Up lane: chain A (the one traveled base to top) is the cable on the RIGHT looking uphill. `StationAlign.UpLaneIsSecond` decides it on the two
  chains' midpoints, base to top by the stations' positions. A carrier faces its travel direction with its hanger at local +x, so the
  hanger stays outboard of the tower decks on both sides. Two chains that cannot be told apart keep the order found.
  `Loop.m_UpLaneSwapped` / `m_UpLaneDecided` say what happened (ChairPinSystem logs "[loop] up lane = right looking uphill" once per
  loop per load).
- Shuttle (open-chain) mode for the tram: `BuildShuttles` returns, per station pair, a Shuttle with two Paths. Chain A = the up lane,
  right looking uphill. Both paths are oriented base to top, and base = the Base-ROLE station. Each path = [straight run from the base
  dock] + the chain's lanes + [straight run to the top dock]. Its arc-length table is the Segment list (m_CumStart). Docks come from
  StationDef.DockLocalA / B through ObjectUtils.LocalToWorld(Transform, float3) (Game.Objects/ObjectUtils.cs:208-211). `Sample(Path, s)`
  returns the track-rope point and the BASE-to-TOP unit tangent at s in [0, m_Length] (allocation-free: the mover calls it every frame).
  Nothing in Loop mode reads any of it. `TramShuttleSystem` owns its own LiftLoopCache instance.
