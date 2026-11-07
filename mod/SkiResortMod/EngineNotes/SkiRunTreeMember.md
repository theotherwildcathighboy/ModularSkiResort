# SkiRunTreeMember: why glade trees carry a tag instead of Owner(run), and the save layout of that tag

This is a serializable member tag that binds a glade-tool tree to its ski run. It is a persisted component. Its type name and
`Serialize` layout are a save-format contract (see `SaveLayouts`). Citations are the `Game.dll` 1.6.0f1 decompile baseline.

## Why a tag and not Owner(run)

`Game.Objects.SubObjectSystem` reconciles an object with `Owner(area)` on ANY `Updated` of the area (SubObjectSystem.cs:3936-3944,
area branch :936 / :1041-1048 -> `RelocateSubObjects` :1360-1533). It moves every owned object that is outside the polygon, touches an
edge or overlaps a kept object. The move goes to a random interior spot, or the object is deleted (:1507-1517, :1227-1268). So a tree-line belt outside the run
is impossible, and every polygon edit scrambles dense glades.

Further problems with `Owner(run)` trees:
- A click on one walks the owner chain to the gate (RaycastJobs.cs:328-403).
- Owned trees get no `Game.Objects.Surface` (GenerateObjectsSystem.cs:1264-1274).
- Groomed runs carry no runtime `SubObject` buffer (SubObjectReferencesSystem.cs:72 indexes it unguarded).

So planted trees are free-standing and carry this tag. `SkiGladeTagSystem` adds the tag to the Temp tree just before
`ApplyObjectsSystem`, which keeps the same entity on create (ApplyObjectsSystem.cs:670-687). The run delete cascade is
`SkiGladeCleanupSystem`'s job.

## Variant and plan generation

`m_Variant` (0..7) and `m_PlanGen` (`SkiGladeCompose.PlanGeneration` when planted) tell a run's members apart by the plan that placed
them. The same (variant, generation) is matched against the plan. Anything else (another variant, an older generation, a v1 tag) is
replaced by the next apply of that layer.

## Serialization

The shape follows Game.Common/Owner.cs:15-23. `writer.Write(Entity)` is the vanilla Entity-reference path, so `m_Run` is remapped on
load like `Owner.m_Owner`. Mod component types reach the save serializer because `ModManager` registers every mod assembly with
`TypeManager.InitializeAdditionalTypes` and marks `SerializerSystem` dirty (ModManager.cs:146-150).

Layouts (little-endian, `Write(Entity)` = int32 saved index or -1):

* v1: byte version | Entity run | byte layer = 6 bytes
* v2 (current): byte version | Entity run | byte layer | byte variant | byte planGen = 8 bytes

`Serialize` always writes `CurrentVersion` and the v2 layout. `Deserialize` reads variant + planGen only when version >= 2. A v1 tag
loads as `m_Variant = VariantUnknown`, `m_PlanGen = 0`. After the 1.0 freeze, this layout never changes. No payload length is written,
so an older build reading a longer element reads the rest of the block shifted. New fields go in a new `ISerializable` type, which an
older build skips cleanly. The v1 -> v2 branch is the pre-release pattern (append + bump + branch).

Legacy test: use `m_PlanGen == 0` (or `VariantUnknown`), NOT `m_Version < 2`. A v1 tag re-saved by this build reads back as v2.

## Framing (Colossal.Core 1.6.2f1 decompile, member names only)

Component data is one size-framed block per (archetype, component type). `ComponentDataSerializer.DeserializeComponentDataJob` reads
every entity's value between `reader.Begin(out size)` and `reader.End(block)`. `BinaryReader.End` returns false when the bytes read
differ from the block size. This gives `ComponentSerializerException` "Data size mismatch when deserializing component".

* Downgrade hazard: an older build reading a v2 block reads 6 of 8 bytes per tree and throws "Data size mismatch". Its handling inside
  a Burst job is unverified. Never load a v2 save in an older build.
* Uninstall: a build that does not know the type logs `Debug.LogWarningFormat("Not serializable type: {0}")` to Unity Player.log and
  skips the block. Trees then load as plain vanilla trees.
* Never rename this type or move its namespace without `[FormerlySerializedAs]`: the type table resolves saved names through that
  attribute.

## LayerFiller (Quick runs tree filler)

`m_Layer = LayerFiller` (2) marks a filler tree. Filler trees are region-level: `m_Run = Entity.Null`. `SkiGladeCleanupSystem` does NOT
treat this as an orphan for this layer (no run delete reaches them, and the census line counts them as "filler"). The byte layout is
unchanged (layer is already a byte), so there is no layout version bump. The system handles a non-Null `m_Run` on a filler tag like any
other tag.
