# LiftLoopTopology: the pure loop topology pass and how it matches the original LiftLoopCache

The pure topology pass of `LiftLoopCache`: chaining, terminal classification, pairing, base / top, up lane, orientation and the terminal
gaps, over plain stand-ins (no Game, no Colossal, no Entities type). Linked into tests/LiftBatch (LoopCacheTests); the pure sibling
`StationAlign` supplies the up-lane rule.

## Loop mode: a transcription, not a redesign
tests/LiftBatch LoopCacheReference.cs holds a frozen copy of the original lines over the same stand-ins, and LC-REF runs 2,000 seeded
random graphs through both and demands identical chains, diagnostics, notes, counts, pairs and gaps bit for bit. The substitutions:
- PathNode becomes an int node id the caller interns (LiftLoopCache: Dictionary<PathNode, int>, ids in first-seen lane order). Equal ids
  <=> PathNode.Equals, which compares the full 64-bit m_SearchKey (Game.Pathfind/PathNode.cs:62-65; GetHashCode :77-80 hashes the same
  key), so the node map's candidate lists hold the same lanes in the same order as the PathNode-keyed map did.
- `cursor.GetOwnerIndex()` becomes the owner index carried with the lane end the cursor stands on (Lane.StartOwner / EndOwner =
  PathNode.GetOwnerIndex() of that end, PathNode.cs:113-116: bits 32-63 of the same search key, so two ends with one id always carry one
  owner).
- `MathUtils.Position / Tangent(bezier, 0f or 1f)` becomes the lane's pre-sampled P0 / P1 / T0 / T1 (LiftLoopCache fills them with the same
  calls): the same float bits.
- `List<Entity> stations` + `List<float3> stationPositions` become `List<Station>` (OwnerIndex = Entity.Index, Position).
- The pair notes ("need exactly 2", "closure failed", "loop closed") and every geometry note stay in LiftLoopCache, written in pair order
  from Pairs, so the note order is: chain notes first, then pair by pair.
- CommonType is evaluated for every two-chain pair (originally after a successful closure). Only a successful pair's TypeIndex is ever
  read, so nothing observable changes.

LC-REF cannot see the Game-side adapter (interning, lane fill, the geometry and its notes).

## Shuttle mode (the tram)
Shuttle mode shares the chaining, classification, pairing, up lane and type, and differs in four rules:
- Base = the station whose Role is Base (0) when exactly one of the pair has it (the cabins live on the Base prefab; a downhill tram keeps
  its base); else the Loop rule (lower terminal).
- Both chains are oriented base to top (no closure: a jig-back tram has two separate track ropes).
- Gaps = the distance between the two chains' ends at the base and at the top, accepted in [ShuttleGapMinM, ShuttleGapMaxM] (two track
  ropes 2 x GaugeHalfM apart: 11 m on the tram).
- Docks: at each station with two finite docks, the two chains take the two docks by the smaller summed distance (tie: A to DockA); a run
  longer than MaxDockRunM leaves that chain undocked (-1, run 0; the would-be run in the *RefusedM field for the caller's note).

Option `ShuttleDockReach` (off by default): a terminal within MaxDockRunM of a station's dock is that station's (the tram's stub-tip rope
is 29-30 m from the origin, past StationTerminalRadius).

Chain A is the up lane (right looking uphill, `StationAlign.UpLaneIsSecond`) in both modes.

## SortedStationOrder (the station list order)
`SortedStationOrder(stationIndex, stationVersion, order)` is the order that sorts a station list by entity (Index, then Version; equal keys
keep list order). TramShuttleSystem applies it to its harvest so the station indices in the pair notes and the rebuild signature do not
move with the chunk order (see TramShuttleSystem.md, "Station order and the watchdog signature"; LiftBatch SH-12). The topology itself
never sorts: its Station list is whatever order the caller gives, and pairing follows lane order.
