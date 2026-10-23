# LiftReconnectSystem: why placing a station needs a poke, what gets poked, and where it runs

`LiftReconnectSystem` makes the line-first build order work (draw the line, then place the station).

## The problem
When a station is placed snapping onto an existing lift line's end, the net-level connection succeeds but cables don't draw. Lanes are
(re)built only for owners matching LaneSystem's trigger query - All{SubLane} + Any{Updated, Deleted} (LaneSystem.OnCreate, m_OwnerQuery) -
and placing a building marks neither the line's node nor its edges Updated, so anchors that did not exist at edge creation are never
searched again. The fix is to mark the affected net entities Updated on station placement, so the game's own systems rebuild lanes and
re-run FindAnchors as if the player had modified the net (GenerateEdgesSystem itself pokes affected entities with AddComponent<Updated>
the same way).

## Merge vs joined (GenerateEdgesSystem.TryGetNode, ~1244)
A course end landing on an existing node reuses that node entity.
- stations-first: the drawn line reuses the owned stub node, so FindAnchors(node) walks the station prefab's ObjectSubLanes.
- line-first: the stub course reuses the unowned line node (Owner is only added to created course entities, GenerateEdgesSystem ~1550) and
  the node is not in the station's SubNet buffer. Anchors there come only from the node's sub-object (the line tower, whose prefab carries
  the ObjectSubLanes wire stubs). The reused node is reachable for the poke only through the stub edge's m_Start/m_End; a SubNet walk alone
  would miss it.

## What gets poked
Dedup set, per station, only when a foreign edge (one on a stub node not owned by this station) is attached:
- every subnet entry whose net prefab carries ElectricityConnectionData (the SkiLiftMarker stub);
- for stub edges, both end nodes;
- every edge in each such node's ConnectedEdge buffer plus those edges' far nodes (the redraw-equivalent set).

Chairs are not tagged directly (they carry no Secondary flag, SubObjectSystem.cs:2824-2827, and are in their owner edge's SubObject buffer,
SubObjectReferencesSystem.cs:122-138): the re-pin goes to `ChairPinSystem.RequestFullPass` at Modification4B, because it must land after
LaneSystem rebuilt the poked lanes this frame. No foreign edge: complete no-op, no log.

## Scheduling (load-bearing, and why not a barrier command buffer)
`UpdateBefore<LiftReconnectSystem, Game.Net.LaneSystem>` at Modification4.
- The station's SubNet buffer is populated by SubNetReferencesSystem at Modification4 (SystemOrder line 147), so it does not exist before
  this phase (rules out 2B).
- LaneSystem runs later in the same phase (line 155); our immediate EntityManager writes are exactly what its m_OwnerQuery consumes, same
  frame, whereas a barrier buffer would defer the tags past LaneSystem and lose them to the end-of-frame CleanUpSystem sweep.
- Downstream consumers (Mod4B ChairPin/PowerBridge/EBGS, Mod5 SearchSystem, PreCulling) still see the tags.
- GeometrySystem/SubObjectSystem ran earlier, harmlessly: the node was reused in place, so only lanes need rebuilding.
- Idempotent and event-driven only (station Created/Updated, including Move It nudges).

## Episodes
The pure `LiftReconnectEpisodes` decides, per station. Move It keyboard nudges of a connected top station change the Transform in the poke
signature every moved frame, which would poke and log every frame.
- A signature change (foreign-edge set or Transform differs from the last seen one) while no episode is open: open it and poke at once; if
  the foreign-edge set changed (a line attached) also RequestFullPass at once, which keeps the same-frame closure of a line drawn before its
  station.
- Further changes: at most one poke per PokeIntervalFrames (a line attached mid-episode still pokes + full pass at once).
- Stable for SettleFrames: trailing poke + RequestFullPass + one line, e.g. `LiftReconnect [reconnect]: 'SkiTopStationBuilding' 119398:67 -
  station moved 34x over 4.6 s; 3 node(s) + 2 edge(s), 5 poke(s); chair re-pin requested`.
- The episode tick runs every frame, also when the station query is empty (an early return would starve the trailing poke).
- Last-seen signatures of Deleted stations are pruned (their open episode is logged and closed); open episodes are flushed (logged, no
  poke) in OnGameLoadingComplete and in the catch. A poke's own echo (the station comes back Updated next frame through ownership
  propagation) has an unchanged signature and is no change.

Frames are this system's update count (Modification4 runs every frame, paused included).
