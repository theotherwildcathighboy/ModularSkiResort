# LiftTreeCorridor: the lift line's tree corridor

A built lift line section fells the trees in its corridor, as real lifts cut one (DECISIONS 601 e, owner hand-test note 8). Pure
rule: `LiftTreeCorridor.cs` (LiftBatch TC-1..TC-6); tool side: `LiftLineToolSystem.Trees.cs`.

## Why

Before, the lines felled nothing. Vanilla only hides an Overridable tree that collides with an object or net
(`Game.Objects.OverrideSystem`, `ValidationHelpers.cs:691-715`), and the moving carriers are Updated every 2 m (ChairPinSystem), so
OverrideSystem re-ran around them: trees beside the rope hid while a chair passed and came back after it (pop-in). DECISIONS 582 gives
the carriers GeometryFlags.Marker (an empty collision mask) while the lift-line tool is open, so in a tool session the chairs passed
through the crowns without hiding them; outside it they hid them. Felling the corridor makes both moot: no tree is left where a
carrier sweeps.

## The rule (plan, xz)

- Envelope half width = `GaugeHalfM` (the haul-rope lanes at +-x) + `CarrierHalfWidthM` (carriers.json: tr / dq bench_m / 2, gd8
  max |bounds_lat_m|, tram bounds_lat_half_m; LiftBatch carriers-json drift).
- A tree falls when its trunk is within envelope + `MarginM` (3 m) of the line, or its crown (the prefab ObjectGeometryData bounds'
  widest xz reach, capped at 8 m) comes within `CrownClearM` (0.5 m) of the envelope.
- Corridor widths: FG 11.4 m, DQ 13.6 m, gondola 14.2 m, tram 20.8 m.
- The line: the planned nodes (ground ends, towers, station stub nodes) plus each station end's own stub edges (ConnectedEdge at the
  stub node owned by the station, each curve in 4 pieces). Distance to the nearest segment, so the ends are round (a ground end is
  covered by the cap). A loose end adds nothing: its edge belongs to the neighboring section.
- Height is not considered: a tram span 80 m above a valley still cuts its strip (an idea for later: fell only where the carrier
  bottom comes within reach of the tree top).

## Seam into the engine

- Query: `Game.Objects.SearchSystem.GetStaticSearchTree(readOnly: true)`, writer deps completed (the glade census idiom,
  SearchSystem.cs:421-425), `SkiGladeToolSystem.TreeBoundsIterator` (IsTree mask) per segment over its bounds + QueryReachM.
- Filter (the glade tool's IsThinnable minus its run-member rule): not Deleted, no Owner (a building's or net's own tree; its delete
  can block the whole apply, ValidationSystem.cs:920-929), not OnFire, a TreeData prefab. Overridden trees are included.
- Fell: `SkiGladeToolSystem.EmitThinDefinition`, the vanilla bulldoze definition for an unowned static object
  (BulldozeToolSystem.AddEntity :731-751 / :812-875): CreationDefinition{m_Original = tree, Delete} + ObjectDefinition + Updated on the
  tool's ToolOutputBarrier, in the SAME emission as the line's courses.
- Ghost (Edit): GenerateObjectsSystem makes a Temp{Delete} and hides the original (:974-978), so the preview shows the cut; a re-emit
  or a clear shows the originals again (ToolClearSystem.cs:63-113).
- Build: ApplyObjectsSystem adds Deleted to each original (:342-352, 484-504) in the apply frame, with the line: saved, refunded (only
  a Recent tree) and validated like a vanilla bulldoze. A refused build keeps every tree.
- Cap: 5,000 definitions per build (the glade apply is sized for 4,000-8,000 Temps); more are logged as left standing.

## Logs

- `LiftLine [ghost]: preview #n ... ; N tree(s) of the W m corridor shown felled` (the ghost cadence: first 3, then every 25th).
- `LiftLine [trees]: N tree(s) in the W m corridor emitted to fell with the line (S segment(s), K station stub edge(s))` at Build.
- `LiftLine [trees]: felled N tree(s) in a W m corridor (type: rope gauge, carrier half width, margin; segments, stub edges)` the
  frame after the apply (originals gone or Deleted); WARN when a marked tree still stands (a canceled delete) or the cap was hit.
