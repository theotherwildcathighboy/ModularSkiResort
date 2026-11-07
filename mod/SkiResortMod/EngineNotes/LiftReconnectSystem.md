# LiftReconnectSystem: why placing a station needs a poke, what gets poked, and where it runs

`LiftReconnectSystem` makes the line-first build order work (draw the line, then place the station).

## The problem
A station may snap onto the end of an existing lift line when the player places it. The net-level connection then succeeds, but the cables do not draw.
The game builds or rebuilds lanes only for owners that match LaneSystem's trigger query - All{SubLane} + Any{Updated, Deleted}
(LaneSystem.OnCreate, m_OwnerQuery). Placing a building marks neither the line's node nor its edges Updated. So the game never searches again for anchors that did not
exist at edge creation. The fix is to mark the affected net entities Updated on station placement. The game's own systems then rebuild lanes and
re-run FindAnchors, as if the player had modified the net. GenerateEdgesSystem itself pokes affected entities the same way, with AddComponent<Updated>.

## Merge vs joined (GenerateEdgesSystem.TryGetNode, ~1244)
A course end that lands on an existing node reuses that node entity.
- stations-first: the drawn line reuses the owned stub node. FindAnchors(node) then walks the station prefab's ObjectSubLanes.
- line-first: the stub course reuses the unowned line node. Owner is only added to created course entities (GenerateEdgesSystem ~1550).
  The node is not in the station's SubNet buffer. Anchors there come only from the node's sub-object (the line tower, whose prefab carries
  the ObjectSubLanes wire stubs). The poke can reach the reused node only through the stub edge's m_Start/m_End. A SubNet walk alone
  would miss it.

## What gets poked
The system builds a dedup set, per station. It does this only when a foreign edge (one on a stub node that this station does not own) is attached. The set holds:
- every subnet entry whose net prefab carries ElectricityConnectionData (the SkiLiftMarker stub)
- for stub edges, both end nodes
- every edge in each such node's ConnectedEdge buffer, plus those edges' far nodes (the redraw-equivalent set)

The system does not tag chairs directly. Chairs carry no Secondary flag (SubObjectSystem.cs:2824-2827) and are in their owner edge's SubObject buffer
(SubObjectReferencesSystem.cs:122-138). The re-pin goes to `ChairPinSystem.RequestFullPass` at Modification4B. It must land after
LaneSystem rebuilt the poked lanes this frame. With no foreign edge, the system does nothing and writes no log.

## Scheduling (load-bearing, and why not a barrier command buffer)
`UpdateBefore<LiftReconnectSystem, Game.Net.LaneSystem>` at Modification4.
- SubNetReferencesSystem fills the station's SubNet buffer at Modification4 (SystemOrder line 147). The buffer does not exist before
  this phase (this rules out 2B).
- LaneSystem runs later in the same phase (line 155). Our immediate EntityManager writes are exactly what its m_OwnerQuery consumes in the same
  frame. A barrier buffer would defer the tags past LaneSystem, and the end-of-frame CleanUpSystem sweep would remove them.
- Downstream consumers still see the tags: Mod4B ChairPin/PowerBridge/EBGS, Mod5 SearchSystem, PreCulling.
- GeometrySystem/SubObjectSystem ran earlier, harmlessly. The node was reused in place, so only lanes need rebuilding.
- The system is idempotent and event-driven only (station Created/Updated, including Move It nudges).

## Episodes
The pure `LiftReconnectEpisodes` decides, per station. A Move It keyboard nudge of a connected top station changes the Transform in the poke
signature on every moved frame. Without episodes, that would poke and log every frame.
- A signature change happens when the foreign-edge set or the Transform differs from the last seen one. If this happens while no episode is open,
  the system opens an episode and pokes at once. If the foreign-edge set changed (a line attached), the system also calls RequestFullPass at once.
  That keeps the same-frame closure of a line drawn before its station.
- Further changes: at most one poke per PokeIntervalFrames. A line attached mid-episode still pokes + full pass at once.
- After the signature is stable for SettleFrames, the system sends a trailing poke + RequestFullPass + one line, e.g. `LiftReconnect [reconnect]: 'SkiTopStationBuilding' 119398:67 -
  station moved 34x over 4.6 s; 3 node(s) + 2 edge(s), 5 poke(s); chair re-pin requested`.
- The episode tick runs every frame, also when the station query is empty. An early return would starve the trailing poke.
- The system prunes the last-seen signatures of Deleted stations (it logs and closes their open episode).
  It flushes open episodes (logged, no poke) in OnGameLoadingComplete and in the catch.
- A poke's own echo has an unchanged signature, so it is no change. The station comes back Updated next frame through ownership propagation.

Frames are this system's update count (Modification4 runs every frame, paused included).
