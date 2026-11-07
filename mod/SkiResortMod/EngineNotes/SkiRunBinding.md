# SkiRunBinding: the persisted run-to-sign link and its save-format contract

The persisted run->sign link. Save-format contract: never change the type's name, namespace or Serialize layout.

## Why runs carry no Owner

Run areas carry no Owner(sign). In game mode the bulldozer walks an owned area's Owner up to the sign
(Game.Areas/RaycastJobs.cs:80-93). So clicking one run deleted the sign and, through its SubArea buffer, every run. A border prop is a
second path to the same result (Game.Objects/RaycastJobs.cs:328-442). Without Owner, both walks stop at the run. The sign->runs cascade
is `SkiRunGateCascadeSystem`, keyed on `m_Gate`.

## Serialization

We checked this against the installed 1.6.2f1 Colossal.Core.dll, which is not in tools/decompile. The citations are member names, not file:line.

- The shape copies Game.Common/Owner.cs:15-23 (ISerializable, `writer.Write(Entity)` / `reader.Read(out Entity)`) with the version byte
  first, like `SkiRunTreeMember`. Mod component types reach the save serializer because `ComponentSerializerLibrary.Initialize` adds
  every ISerializable component from an assembly other than Game.dll to the set. SerializerSystem's query is built from that set
  (SerializerSystem.cs:194-209).
- Entity remap: `BinaryWriter.Write(Entity)` writes the saved index only when the live entity is in the save's entity table with a
  matching Version. Otherwise it writes -1. `BinaryReader.Read(out Entity)` maps -1 or an out-of-range index to Entity.Null. The table excludes Temp and Deleted
  (SerializerSystem.cs:198-208). So `m_Gate == Null` after a load means the sign was gone when the city was saved (a "paid orphan"),
  never corruption.
- Framing: component data is one size-framed block per (archetype, component type). `DeserializeComponentDataJob` throws "Data size
  mismatch when deserializing component" when the bytes read differ from the block size. A build that does not know the type
  (`ComponentSerializer.DeserializeType`: Type.GetType fails and no `[FormerlySerializedAs]` matches) logs "Not serializable type: {0}"
  to Unity Player.log, not the mod log, and skips the block with `ObsoleteComponentSerializer`.
- The saved type name is `GetComponentType().AssemblyQualifiedName` (`ComponentSerializer.SerializeType`). Never rename this type, move
  its namespace or rename the SkiResortMod assembly without `[FormerlySerializedAs]`. The type table resolves old names only through
  that attribute (`ComponentSerializerLibrary.UpdateTypeTable`). It is unverified whether Mono's Type.GetType tolerates a changed AssemblyVersion of this
  non-strong-named assembly. The csproj pins AssemblyVersion 1.0.0.0, and the type carries a self-alias as a fallback
  (ComponentSerializer.cs:51-65). This is inferred from the decompile, not seen in game.

## Consequences

1. Layout v1 is frozen: byte version | Entity gate (int32 index) | byte flags | int32 paid = 10 bytes per run, little-endian. After
   1.0 it never changes. The format writes no payload length, so an older build that reads a longer element reads the rest of the block shifted.
   New fields go in a new ISerializable type (companion component), which an older build skips as an unknown type. Before the 1.0
   freeze, an append + `CurrentVersion` bump (branch on `m_Version` in Deserialize) is still allowed.
2. Downgrade hazard: a build without this type loads the save without error (it skips the block). Then its sweep re-binds and re-charges
   every run (4,000-20,000 each) and re-adds Owner + the sign's SubArea buffer (the one-run-bulldoze trap). Never open a save made by
   this build in an older one. A v1 build that reads a future v2 block throws "Data size mismatch" (game outcome unverified).
3. Uninstall leaves plain run surfaces with no Owner and no binding. Reinstalling re-links them free at the next load.

## Flags

- `FlagPaid`: the one-time bind fee is settled for this run. Never charge it again, also not after the sign dies and the run re-links.
- `FlagRestored`: the load pass linked the run without a charge (lost link). `m_PaidAmount` is then 0.
- `m_PaidAmount` keeps what the run paid, so a refund can be added without a save-format change.
