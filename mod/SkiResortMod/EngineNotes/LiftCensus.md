# LiftCensus: how lift rows are built from net topology (components, row keys, typed census, split nodes)

`LiftCensus` is the pure lift model: one row per lift. `LiftCensusSystem` harvests the ECS data into the plain Edge / Node / Station
lists and calls `Build` on events. The hub and the lift logs read the rows. `tests/LiftBatch` links the file into a net8 host and runs
synthetic graphs.

## Why a census
`ChairPinSystem`'s loop cache exists only in moving mode (ChairPinSystem.cs:443-447). It measures the CIRCUIT (chain A + top turn + chain B
+ base turn, LiftLoopCache.cs:371-386). It is lane-based and up to 30 frames stale. The census is independent of moving chairs and is the
lift authority for the hub and the logs. The loop cache contributes only "chairs riding" per station pair (`ChairPinSystem.TryGetLoop`).

## Inputs (the ECS side is in LiftCensusSystem)
- Station stub edges (`SkiLiftMarker`) are NOT edges here (LengthM = sum of line edges only).
- A station connection node is one whose Owner is a station (stations-first) or whose connected edge is owned by a station (line-first).
- A station's IsBase is by PREFAB, never by height (the loop cache's "lower = base" is only its internal tie-break).

## Model
- Components: edges are unioned through shared nodes. But a station connection node is a TERMINAL, never a connector (the loop cache's
  "walk stops at station anchors", LiftLoopCache.cs:14-20). Two lifts sharing a station stay two components.
- Attribution: rows are keyed by the station pair (Base, Top). Rows with no complete pair also get an anchor edge, so two unfinished
  lines from one station stay two rows. The rules run in this order:
  1. A component touching exactly one base and one top station gets key (base, top).
  2. Else, if both of these conditions hold, the key is K (a fragment), with NO liveness condition on K's stations:
     - Its surviving edges carried exactly ONE key K at the previous build.
     - The stations it touches are a subset of {K.Base, K.Top}.

     Example: a complete lift lost a station (the
     player deletes and re-places a top station to straighten it). It must keep its key. Otherwise it gets a NEW key (NewLine instead of
     Opened), and its chairs are never parked: they freeze mid-air. With the key kept, it becomes Open with Gaps 1 (the dead terminal is
     untouched), and `DiffEvents` raises Opened, so the existing park runs. Liveness stays in rule 3 (a redraw must reach a living partner).
  3. Else, if both of these conditions hold, the key is K (a redrawn fragment whose edges are all new):
     - It touches exactly one station S.
     - A previous OPEN row K has S as a station, and its other station is alive.

     The rule excludes a COMPLETE previous row. Reason: a new line drawn from a station that already has a complete lift is a second
     lift, not a fragment of the first (shared-station case).
  4. Else a new key: (S, Null) when S is a base, (Null, S) when S is a top, (Null, Null) with no station. Each key has its lowest-index
     edge as anchor. A component touching more than 2 stations or two of one kind is rule 4 with a build note (Notes).

  A key with no component left is dropped. Its stations become StationsOnly rows. History is in memory only. After a reload mid-redraw,
  fragments come back as Unfinished rows (accepted).
- Row values:
  - Complete = exactly one component touching both stations. Open = a pair with any other shape. Unfinished = no pair (or a build note).
    StationsOnly = a station on no component and in no row.
  - Gaps = fragments - 1 + (base not touched) + (top not touched), 0 when Complete.
  - LengthM = sum of edge Curve lengths (NOT the loop).
  - TowersRemoved = non-station row nodes WITHOUT a tower. Vanilla keeps a tower on every line node while a section uses it
    (GenerateNodesSystem.cs:1250-1254), so another tool removed a missing one. It clears by itself once a section at that node
    is redrawn and the game regenerates the tower.
  - Id = the LIVE Base, else the live Top, else the lowest-index edge (a key may hold a removed station, rule 2). The hub addresses,
    renames and flies to the row id, so it must be a living entity.
  - Known: after a station is removed, its former connection node becomes a plain dead end with no tower of its own. So it counts as
    TowersRemoved = 1 unless vanilla regenerates a tower there. Unverified, no guard.
- Build appends events to the caller's list (the list is cleared first). It does this only from the SECOND build after Reset (the first
  build is the per-load baseline, never logged).
  - SectionsDeleted = edges of a previous row that are gone, when that row gained no new edge in the same build. A redraw that replaces
    edges is an edit, not a deletion.
  - Opened = Complete to Open (same key).
  - Closed = Complete now and not Complete under the same key before (Count 1 when the key existed as Open: "complete again").
  - NewLine = a new Unfinished row with at least one section.
  - Removed is never produced here. `LiftCensusSystem` appends it from its Deleted-tag snapshot (an element removed by another tool).
- Revision increments whenever the row CONTENT changed (every field, hashed), so the stats system can gate on it.
  `LastBuildChangedTopology` says whether edges, node roles or row keys changed (the system then asks `ChairPinSystem` for an immediate
  loop rebuild).
- Additive members beyond the base contract: Event.PrevRow, PreviousRows, Reset(), LastBuildChangedTopology, Notes, HasBaseline. They
  let the system name a lift whose row was dropped (its last section deleted) and keep the first build after a load silent.

## Cost
Build is event-driven (lift edits, coalesced station moves, a 4096-frame watchdog), O(edges + nodes + stations) with dictionary lookups.
It reuses its member collections, so a build allocates only when a collection grows or a build note is written.

## Typed census
- Edge.Type = the `SkiCatalog.Lifts` index of the edge's line prefab.
- Station.Type = its `StationDef.LiftTypeIndex` (NoType = 255 when the harvest could not type it, defensive only).
- In the union pass, the census IGNORES an edge with Type == NoType. It is never unioned, and never part of a component, a row or a
  degree count. It gets one build note "edge {id} has no lift type - ignored". The harvest passes catalog lines only, so a vanilla HV
  feeder never arrives here at all, though vanilla lets one feed a station stub.
- Every component carries TypeMask (bit Type of each typed edge and each touched station). Popcount > 1 = MIXED (a detachable line
  between fixed-grip terminals is as broken as two line types: gauge +-2.6 vs +-1.8 anchors). A mixed component is Complex (Unfinished
  through the existing MakeRow path). It gets a build note "component of N edge(s) from {edge} mixes lift types (fixedGrip+detachable) -
  Unfinished". Every row built from it gets Mixed = true.
- Attribution is unchanged. A mixed component touching one base and one top keeps that pair's key, so removing the stray edge returns the
  same row to Complete.
- Row.Type = the live base station's type, else the live top's, else the lowest-index edge's. For StationsOnly rows it is their station's.
  The row hash (Revision) covers Type and Mixed. The topology hash covers the edges' types.

## Split at a shared node
Two lines of DIFFERENT lift types can share a non-station node (the net tool snapped a new node onto another lift's tower node). Without a
split, a tram redrawn onto the gondola would read "mixes lift types" and leave BOTH Unfinished. So these lines do not union.
`LinkEndpoint` keys the first-edge-at-node map by (node, type), so each type keeps its own component and the node is a split point.
- A component's TypeMask therefore carries at most ONE bit from its edges. Code 23 (Mixed) still fires in two cases:
  - A component's edge type differs from a station it touches.
  - A component touches stations of two types (a DQ line drawn between a gondola base and a DQ top).
- The census records a split in three members:
  - Row.SplitNodes (this row's components touch N split nodes). It is hashed and NOT bound to the hub.
  - SplitNotes (the note text with the node position). DUMP only.
  - NewSplits (the splits not yet announced since Reset). The system logs one "[census] split at node N: types A|B" INFO line each.

## Plan bends at towers (2026-11-02, owner verdict: warn, never refuse)
The lift tool draws straight lines. Bends come from the vanilla net tool or a moved node (docs: lift-alignment-2026-11-02 C6 / F6).
`MeasureBends` runs per component inside `MakeRow` (so per build, event-driven, never per frame). It keeps the row's sharpest plan bend:
- The census measures only at a non-station node with a tower and exactly two of the component's edges. It calls
  `LiftAlignMath.PlanBendDeg(prev, node, next)` over node positions (the edges' StartPos / EndPos). It never measures a station connection
  node, a node without a tower or a branch node.
- The walk follows the tower number tiles (TowerNumberTileSystem.NumberPath). It starts from the base station's connection node. A component without the base starts from its dead end nearest the row's GapAt reference. A fragment holding the top counts from its far
  end, like the tiles. A branch ends the walk. `BendTower` = `TowerNumberTiles.Wrap(rank)`, rank = towers walked so far, so the number
  matches the tower's plate.
- Row fields `BendDeg`, `BendTower`, `BendNode`, `BendAt` are hashed (a moved tower bumps Revision, so the hub rebuilds). The state never
  depends on a bend. `BendWarnTenths(deg)` is the hub's number: 0 at or under `LiftAlignMath.BendWarnDeg` (5), else tenths rounded up
  (never reads 5.0). Tests: LiftBatch BendTests BT-1..BT-10.

## Train limits at towers (2026-11-03, owner: a fact in the hub, never a refusal)
`MeasureBends` keeps, in the same tower walk, the tower whose rope asks most past what its sheave train follows. `Node.TrainOverDeg` is
TrainPitchSystem's figure (`LiftAlignMath.TrainOverDeg`, kept only over `TrainWarnDeg` 0.5). LiftCensusSystem.AddNode reads it from
`TrainPitchSystem.TrainOver`. Row fields `TrainOverDeg`, `TrainTower` (the plate number, like `BendTower`), `TrainNode`, `TrainAt` are
hashed. A station connection node never counts. The census reads the figure of the last train pass. TrainPitchSystem runs after the census
and calls `RequestBuild("train limits")` when its set changes, so the row follows within one coalesce window. The state never depends on
it. Tests: LiftBatch BT-11 (and AL-11 for the figure).

## Low spans (2026-11-03, owner: a fact in the hub, never a refusal)
LiftClearanceSystem measures every lift edge's HV rope lanes (the carriers' lanes, whose Curve carries the sag) against the terrain:
- carrier = rope point - `CarrierDropM`, every `StepM`, skipping `StationClearM` from a station end.
- An edge whose lowest carrier passes more than 0.1 m under `FloorM` (3 m, tram 5 m) is `Edge.Low` with `LowClearM` / `LowAt`.

`MeasureBends` keeps the row's lowest such edge in the same walk. The row fields are `LowSpan`, `LowClearM`, `LowFrom` (the last tower
walked, 0 = the start station), `LowTo` (the next tower walked, 0 = the walk ends first), `LowEdge` and `LowAt`. They are hashed. The
system runs after the census: on census builds, on Updated lift edges and every 600 frames (raised terrain updates no lift entity). It
takes at most 256 samples a frame. A changed set calls `RequestBuild("low spans")`. Tests: LiftBatch BT-12.

## Pure file
The file uses only `using System;`, `System.Collections.Generic` and `Unity.Mathematics` (the test-host rule of tests/SlopeBatch).
EntityRef.cs is the pure stand-in for Entity. The file also uses SkiCatalog (pure, linked wherever this file is) for the type ids in the
"mixes lift types" note. For the plan bends, it uses the pure LiftAlignMath (PlanBendDeg, BendWarnDeg) and TowerNumberTiles (Wrap). All
three are linked into tests/LiftBatch.
