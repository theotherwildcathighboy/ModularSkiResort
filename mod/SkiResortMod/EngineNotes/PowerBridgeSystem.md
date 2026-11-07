# PowerBridgeSystem: why station stubs need a valve bootstrap to join the electricity graph

## The problem

Our stations connect the lift line at a marker-net subnet stub that is a real 3 m edge (needed so line attachment passes
validation). `Game.Simulation.ElectricityBuildingGraphSystem` (EBGS) registers a building's electricity connection only for subnet
nodes that pass `FindMarkerNodes`' gate:

    m_ElectricityValveConnections.HasComponent(subNet) || IsOrphan(subNet)

`IsOrphan` returns false when any connected edge has an Owner, so our owned stub edge disqualifies both of its nodes. Result: the
line attaches and no power flows. On every station update, EBGS's else-branch deletes any `ElectricityBuildingConnection` the
station has (`Execute: result.Length == 0` -> `DeleteBuildingNodes`).

## The fix

The gate is an OR, so a node that carries `ElectricityValveConnection` passes regardless of orphan-ness. `PowerBridgeSystem` only
bootstraps an `ElectricityValveConnection` onto our stations' qualifying stub nodes. It adds a fresh valve flow node from
`ElectricityFlowSystem.nodeArchetype`. It does this before EBGS runs in the same frame. From then on vanilla does all the work and
maintenance:

- `CreateOrUpdateMarkerNode` does four things:
  - It reuses our valve (TryGetComponent-then-create).
  - It creates or reuses the node's `ElectricityNodeConnection`.
  - It wires valve to marker with the stub prefab's `ElectricityConnectionData`.
  - It wires transformer to valve.

  `EnsureMarkerNodeEdgeConnections` wires the marker to attached lift-line edges (dedup via `HasAnyFlowEdge`).
- `CreateOrUpdateBuildingNodes` builds the building side. This is the station itself with its own transformer node (EBGS's
  Building+Transformer arm). Only the base drive terminal keeps a Transformer, and `BridgeStations` skips a transformer-less station.
  The stub edge is station-owned, so `IsOrphan` still fails and the valve bootstrap stays load-bearing. If stations were gate
  ServiceUpgrades, EBGS would exclude them from every building arm (`None{ServiceUpgrade}`, EBGS:574-578) and register the gate. The
  gate's `InstalledUpgrade` recursion (EBGS:189-199) reaches the station's SubNet and the same gate. `BridgeStations`' tag target is
  still dual-mode for that case.
- `ElectricityEdgeGraphSystem` (Modification2B) self-wires newly drawn line edges to any node carrying `ElectricityNodeConnection`
  (`GetOrCreateNetNodeConnection`). So later line draws and deletes need nothing from us.
- Deletion is fully vanilla. `ElectricityGraphDeleteSystem` (Modification1) reacts to Deleted on anything carrying
  Node/Valve/BuildingConnection components. It cascades Deleted onto the flow nodes and their `ConnectedFlowEdge` entities. We need
  no delete path and create no entity that vanilla cannot clean up. Its `DeleteValveNodesJob` strips valves only from Owner-less updated
  nodes. Our stub nodes are station-owned, so it never touches them.

## Which nodes

The system bridges all of the station's subnet nodes whose net prefab carries `ElectricityConnectionData` (both ends of the stub edge).
Vanilla buildings with several marker stubs register every one of them against the same transformer (EBGS's foreach over
`FindMarkerNodes`' result list). So multiple registrations per building are normal. The max-flow solver treats a second
transformer-valve-marker path as a parallel path. Registering both ends also means we never identify "the outer node the line
attaches at". Whichever node the line lands on is registered.

## Scheduling

The system is anchored `UpdateBefore<PowerBridgeSystem, EBGS>` at Modification4B, EBGS's own phase (SystemOrder line 185). Our
EntityManager writes are immediate. So EBGS's job (scheduled after us, same frame) sees the valve and takes its maintain path instead of
its delete path. A freshly placed station carries Created / Updated, so the update pass bridges it before EBGS processes it the same
frame. On the repair/load pass, we tag the station itself Updated, or its Owner when it has one (EBGS cannot see ServiceUpgrade
carriers). This is the dual-mode block in `BridgeStations`. Saves reload the whole flow graph natively. This pass only heals valve loss,
and it is idempotent.

## Near station prefab resolution

Station names live in the pure `SkiCatalog.cs` (Stations). `SkiPrefabCatalogSystem` resolves them per load and revalidates them at
the first catalog call of each frame. `GetResolvedStationPrefabs` returns the catalog's live set (`LiftReconnectSystem` reads it each
frame). On each Game / Editor load, `OnGameLoadingComplete` writes this system's own lines:

- a WARN per station prefab not found
- a WARN when none resolved (bridging inactive this load)
- else one Info line naming the stations
