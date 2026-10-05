// SkiRunTreeMember — the serializable member tag that binds a glade-tool tree to its ski
// run (docs/research/glade-tool-spec.md §2, §6; layout v2: run-binding-glade-spec.md §2, §4.5).
// Our FIRST persisted component: its type name and Serialize layout are a save-format contract
// from the day it shipped. Created 2026-09-30 (G1, layout v1); layout v2 2026-10-01 (Round G2,
// implementer I-2).
//
// WHY A TAG AND NOT Owner(run) (spec §6, all 1.6.0f1 decompile):
//   * An object with Owner(area) is reconciled by Game.Objects.SubObjectSystem on ANY
//     Updated of the area (m_UpdateQuery Game.Objects/SubObjectSystem.cs:3936-3944, area
//     branch :936 / :1041-1048 -> RelocateSubObjects :1360-1533): every owned object that
//     lies outside the polygon, touches an edge or overlaps an already-kept object is
//     moved to a RANDOM interior spot or deleted (:1507-1517, :1227-1268). A tree-line belt
//     outside the run is therefore impossible, and dense glades are scrambled by every
//     polygon edit. (1.6.2f1: same code, +3 lines.)
//   * A click on an Owner(run) tree walks the owner chain to the GATE
//     (Game.Objects/RaycastJobs.cs:328-403), owned trees get no Game.Objects.Surface of
//     their own (Game.Tools/GenerateObjectsSystem.cs:1264-1274), and groomed runs carry no
//     runtime SubObject buffer (SubObjectReferencesSystem.cs:72 indexes it unguarded).
//   So planted trees are free-standing (no Owner) and carry this tag instead. The tag is
//   added to the Temp tree by SkiGladeTagSystem just before ApplyObjectsSystem, which keeps
//   the same entity on create (Game.Tools/ApplyObjectsSystem.cs:670-687), and the run
//   delete cascade is SkiGladeCleanupSystem's job.
//
// WHY v2 (Round G2, spec §4.2-§4.5). An apply makes a run's layer match a plan that never sees
// the run's own members of that layer; members are told apart by the plan that placed them:
// m_Variant (0..7) and m_PlanGen (SkiGladeCompose.PlanGeneration when planted). Same (variant,
// generation) = matched against the plan; anything else (another variant, an older generation,
// a v1 tag) is replaced by the next apply of that layer (D9: G1 test trees are replaced).
//
// SERIALIZATION. Same shape as Game.Common/Owner.cs:15-23 (ISerializable from
// Colossal.Serialization.Entities, Colossal.Core.dll): writer.Write(Entity) is the vanilla
// Entity-reference path, so m_Run is remapped on load exactly like Owner.m_Owner. Mod component
// types are known to ECS and to the save serializer because ModManager registers every mod
// assembly with TypeManager.InitializeAdditionalTypes and then marks SerializerSystem dirty
// (Game.Modding/ModManager.cs:146-150 [1.6.2f1 :149]).
//   LAYOUTS (little-endian; Write(Entity) = int32 saved index or -1, Write(byte) = 1 byte):
//     v1 (G1, FROZEN):  byte version | Entity run | byte layer                     = 6 bytes
//     v2 (G2, current): byte version | Entity run | byte layer | byte variant | byte planGen
//                                                                                  = 8 bytes
//   Serialize always writes CurrentVersion and the v2 layout: the version byte describes the
//   bytes being written now, whatever version the tag was loaded from. Deserialize reads the
//   version first and reads variant + planGen only when version >= 2; a v1 tag loads as
//   m_Variant = VariantUnknown, m_PlanGen = 0. A future v3 may only APPEND and must branch here.
//   LEGACY TEST: use m_PlanGen == 0 (or m_Variant == VariantUnknown), NOT m_Version < 2 — a v1
//   tag re-saved by this build reads back as version 2 with PlanGen 0 / VariantUnknown.
//
// FRAMING — verified 2026-10-01 against the INSTALLED 1.6.2f1 Colossal.Core.dll (ilspycmd 9.1
// decompile into a scratch folder; Colossal.Core is not in tools/decompile, so member names, not
// file:line; this corrects the G1 header, which called the framing unverified):
//   * Component data is one size-framed block per (archetype, component type):
//     EntityDeserializer.DeserializeComponents calls each serializer's DeserializeData with the
//     archetype's entities; ComponentDataSerializer.DeserializeComponentDataJob reads every
//     entity's value between reader.Begin(out size) and reader.End(block), and BinaryReader.End
//     returns false (after jumping to the block end) when the bytes read differ from the block
//     size → ComponentSerializerException "Data size mismatch when deserializing component …".
//   * DOWNGRADE HAZARD: a pre-G2 build reading a v2 block reads 6 of 8 bytes per tree and
//     THROWS "Data size mismatch" (the game's handling of that exception, thrown inside a Burst
//     job, is unverified). NEVER load a save made by a G2 build in an older build (release
//     notes; the same hazard as SkiRunBinding, spec §3.2 (b)).
//   * UNINSTALL: a build that does not know the type logs Debug.LogWarningFormat("Not
//     serializable type: {0}") — Unity Player.log, not the mod log — and skips the block with
//     the obsolete serializer (no exception). Trees then load as plain vanilla trees.
//   NEVER rename this type (or move its namespace) without [FormerlySerializedAs] — the type
//   table resolves saved names through that attribute.

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public struct SkiRunTreeMember : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build (see header). v1 is frozen; bump
        /// only when APPENDING a field, and branch on it in Deserialize.</summary>
        public const byte CurrentVersion = 2;

        /// <summary>m_Layer values: interior glade tree / tree-line belt tree (unchanged since
        /// v1; equal to SkiGladeCompose.LayerGlade / LayerBelt, checked at compile time in
        /// SkiGladeTagSystem).</summary>
        public const byte LayerGlade = 0;
        public const byte LayerBelt = 1;

        /// <summary>m_Variant of a v1 tag (no recorded variant).</summary>
        public const byte VariantUnknown = 0xFF;

        /// <summary>The run area this tree belongs to (no Owner — see header). Null or a
        /// dead entity marks an orphan; SkiGladeCleanupSystem strips the tag and keeps the
        /// tree.</summary>
        public Entity m_Run;

        /// <summary>LayerGlade or LayerBelt.</summary>
        public byte m_Layer;

        /// <summary>v2: the plan variant 0..7 that placed this tree; VariantUnknown for a v1
        /// tag.</summary>
        public byte m_Variant;

        /// <summary>v2: SkiGladeCompose.PlanGeneration when the tree was planted; 0 = v1 tag.</summary>
        public byte m_PlanGen;

        /// <summary>Layout version this tag was created with or loaded from (NOT written:
        /// Serialize always writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiRunTreeMember(Entity run, byte layer, byte variant, byte planGen)
        {
            m_Run = run;
            m_Layer = layer;
            m_Variant = variant;
            m_PlanGen = planGen;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // v2 layout, byte for byte (header). Version FIRST, always the CURRENT layout's
            // version. Typed locals/fields pin the IWriter.Write(byte) overload.
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_Run);      // Entity reference, remapped on load (Owner.cs:17)
            writer.Write(m_Layer);
            writer.Write(m_Variant);  // v2
            writer.Write(m_PlanGen);  // v2
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_Run);   // Owner.cs:22 shape; -1 / out of range -> Entity.Null
            reader.Read(out m_Layer);
            if (m_Version >= 2)
            {
                reader.Read(out m_Variant);
                reader.Read(out m_PlanGen);
            }
            else
            {
                // v1 tag (G1): no recorded plan. Read as "Other" by every G2 compose (D9).
                m_Variant = VariantUnknown;
                m_PlanGen = 0;
            }
        }
    }
}
