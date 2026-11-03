# SkiPrefabCatalogSystem: how catalog names resolve to prefab entities, per load

The ECS side of the prefab catalog resolves every name of the pure `SkiCatalog.cs` to its prefab entity through one typed resolver,
replacing the per-system copy-pasted resolvers.

## Catalog contents and severity

- 30 names (4 lift types, 8 stations, 5 lodges, the school, 2 patrol huts, the sign, 4 runs, the marker); each name is Expected or
  pending (`SkiCatalog` header). Severity only, never behavior: a FOUND pending name works like an expected one; a MISSING pending
  name is listed in the [arm] line's "pending (not imported yet)" list and never turns it WARN; one "[pending-found]" WARN per load
  names every FOUND pending name so the import commit flips it.
- Carrier hang check: a FOUND carrier whose `bounds.max.y` differs from its row's `HangM` by more than 0.01 m writes one WARN per
  carrier per load (it still pins by its bounds) and turns the [arm] line WARN.

## Lifetime

A per-load resolver replaces the once-per-app lifetimes of `ChairPinSystem`, `PowerBridgeSystem` and
`SkiResortAttractivenessSystem`; proven by a second load in one session.

- Initial state: nothing resolved, `m_Dirty` = false, `m_ValidatedFrame` = -1. Before the first Game / Editor load every query
  serves empty views and writes no line.
- `OnGamePreload` with mode Game or Editor invalidates (dirty, Loads++). A MainMenu preload (exit to the menu, GameManager.cs:1095
  passes when the old mode is Game) keeps the last load's views, still revalidated per frame. `onGamePreload` fires inside
  `LoadSimulationData` (GameManager.cs:1023 in :1021-1028; 1.6.2 :1033) before deserialization, for every load except the startup
  main menu (:1095-1098; 1.6.2 :1105).
- The first catalog query after that resolves lazily. This system's `OnGameLoadingComplete` (:1126; 1.6.2 :1136) forces the
  resolution when nobody asked yet, so there is exactly one ARM line per load. Load hooks fire in subscription order = OnCreate
  order (GameSystemBase.cs:27-29, :60-70), so an earlier consumer may resolve first: the same resolution.
- Every query validates the cached entities at most once per frame (`UnityEngine.Time.frameCount`): Exists && !Deleted for each
  FOUND entity (<= 2 EntityManager reads per name). `PrefabSystem.UpdatePrefabs` gives an updated prefab a new entity, Deleted on
  the old one (PrefabSystem.cs:782), `PrefabData` copied (:799), `m_Entities[key]` re-pointed (:804); `RemovePrefab` tags Deleted
  too (:203); identical in 1.6.2. A stale cache would silently match nothing. A gone entity re-resolves that slot (a "[re-resolve]"
  WARN; only the gone slots, fast path first) and the views are rebuilt.
- `m_Dirty` is cleared and `m_ValidatedFrame` set before the resolution runs, whatever its outcome: a throwing resolution must
  never re-run on every call (one name scan costs ~15-20 ms in a large city). After an exception every query serves MISSING until
  the next load.
- Disabled (`Enabled = false`; OnUpdate never runs): load hooks still fire for a disabled system (GameSystemBase.cs:27-29
  subscribes in OnCreate; the CursorAimSystem.cs:66 / LodgeRoadSystem.cs:129 precedent).

## The one typed resolver

1. PrefabID fast path: `PrefabSystem.TryGetPrefab(new PrefabID(type, name))` + `TryGetEntity`, entity live -> "id". PrefabID
   equality includes the asset hash (PrefabID.cs:15-34, :38-52). It hits for our nets, objects and buildings; the run surfaces fall
   to the scan; the ARM line's id / scan records which.
2. One scan per resolution pass over {`PrefabData`, None `Deleted`}, shared by every fast-path miss: `pb.name` is read once per
   candidate (a Unity icall that allocates) and looked up in a wanted-name table. A candidate needs `PrefabData.m_Index` >= 0
   (`TryGetPrefab<PrefabBase>`, PrefabSystem.cs:658-677), so an obsolete placeholder (whose `GetPrefabName` returns the obsolete
   ID's name, :979-990) never matches. The first candidate whose `GetType().Name` equals the PrefabID type wins ("scan"); else the
   first candidate ("scan, type X != Y": still FOUND, but the ARM line turns WARN; the bug class at
   SkiSnowInjectionSystem.cs:359-362). A pending name that is not imported joins the scan the run surfaces already force.
3. None -> MISSING.

Carriers: a FOUND carrier with `ObjectGeometryData.m_Bounds.max.y` > 0 enters `CarrierGripTops` with that value; otherwise a WARN
and it is not a carrier.

## Live views

`LiftLinePrefabSet`, `LiftLineTypes`, `CarrierGripTops`, `CarrierTypes`, `StationPrefabSet`, `StationDefs`, `LodgePrefabSet`,
`LodgeDefs`, `SchoolPrefabSet`, `SchoolDefs`, `PatrolPrefabSet`, `PatrolDefs` are one instance each for the app run, cleared and
refilled in place by a resolution, so a cached reference stays current. Read-only by convention. A re-resolution can only happen at
the first catalog call of a frame, and every call that hands out a view is such a call, so an enumeration started from a view
cannot be invalidated within the frame. Consumer rule: fetch the view once, before the chunk loop; use `TryGetValue` / `Contains`
inside it, never a per-entity member call in a loop over every building.

`GripTypes` (the carrier grip round): grip prefab entity -> its LiftTypeDef, filled only for the grips FOUND this load. A type in it
is SPLIT: its carrier is a body (ChairPinSystem / TramShuttleSystem pose body + grip, EngineNotes/ChairPinSystem.Mover.md "Grips and
swing"), and its CarrierDrops / virtual grip top come from the catalog pivots (`CarrierPivots.RigidDrop`, + GripTopAboveRopeM),
not from the body mesh's bounds (the body alone is shorter than the one-piece carrier; the hang-drift WARN is skipped for it). One
INFO per type per load names the split. A grip is never in `CarrierGripTops` / `CarrierTypes`.

## Log contract

Silence must be impossible (ChairPinSystem header):

- "SkiPrefabCatalogSystem created (30 names: ...; 30 expected, 0 pending; 25 build items; ...)" once; "!!! PrefabCatalog: ..." at
  OnCreate when `SkiCatalog.Problems` is not empty or a `SkiCatalog.Type*` constant differs from `nameof(the Game type)`.
- One "PrefabCatalog [arm] load N, generation G: E/30 expected FOUND, P/0 pending FOUND - ...; MISSING: ...; pending (not imported
  yet): ...; lift types: ...; resolver ..." per Game / Editor load: Info when every expected name is FOUND with its own PrefabID
  type and every FOUND carrier has bounds within 0.01 m of its `HangM`, WARN otherwise (a FOUND pending name with a wrong type or
  bad bounds counts too). Written at the first query after the load's preload, at the latest by this system's
  `OnGameLoadingComplete`.
- "PrefabCatalog [pending-found] ..." WARN once per load when a pending name is FOUND.
- "PrefabCatalog [re-resolve] (why) ..." WARN when a cached prefab entity is gone or Deleted (PrefabSystem.cs:766-824), at most
  `ReResolveLinesMax` = 8 per load, the rest counted in `Describe()`.
- "PrefabCatalog: carrier prefab 'X' has no usable ObjectGeometryData bounds - skipped ..." WARN once per carrier per load.
- "PrefabCatalog: carrier 'X' grip-top ... differs from SkiCatalog HangM ..." WARN once per carrier per load.
- "PrefabCatalog [extra] ..." once per non-catalog (type, name) pair asked through `TryGetPrefab(type, name)` (none expected: every
  hub item is a catalog name).
- "!!! PrefabCatalog EXCEPTION ..." with stack, once per load; every query then answers MISSING until the next load, so each
  consumer's own missing-prefab line fires: never a silent half-state.
