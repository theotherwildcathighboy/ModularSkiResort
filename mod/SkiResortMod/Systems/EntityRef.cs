// EntityRef + RunKind — the shared PURE stand-ins of docs/research/hub-u2-spec.md §2.1 ("shared
// pure (I-1, day one)"). Pure files (SkiResortIndex, SkiResortAggregate, LiftCensus, StationAlign,
// LiftReconnectEpisodes, …) and the net8 test hosts that link them (tests/ResortBatch,
// tests/LiftBatch) cannot see Unity.Entities, so an entity crosses into pure code as an EntityRef
// {Index, Version} and back with `new Entity { Index = r.Index, Version = r.Version }` at the
// system boundary.
//
// CONTRACT (§2.1, names and shape exact — other implementers link this file):
//   * EntityRef(int index, int version); fields Index, Version (public, mutable, as Entity's).
//   * IsNull ⇔ Index == 0 && Version == 0, which is exactly Unity.Entities.Entity.Null
//     (Entity.Null = default(Entity), both fields 0). An Entity that merely does not exist is NOT
//     null here, as for Entity.
//   * Equality is Index AND Version (Entity's own equality), so a recycled index with a new
//     version is a different reference. Ordering (CompareTo) is Index, then Version — the "stable
//     order" §2.1 asks of SkiResortIndex ("sorted by Index, then Version").
//   * RunKind: 0 groomed / 1 snowmaking / 2 night / 3 natural (appended in E1) =
//     SkiRunOwnershipSystem.KindOf's return values = mod/SkiResortUI/src/bindings.ts `RunKind`. The snow
//     injection system's PRIVATE kind order (0 night / 1 snowmaking / 2 groomed) never leaves that
//     system; nothing may cast between the two.
//
// PURE FILE: `using System;` only (the test-host rule of tests/SlopeBatch: System + Unity.Mathematics).
// No allocation, no state.
//
// Created 2026-10-03 (Round U2, implementer I-1).

using System;

namespace SkiResortMod.Systems
{
    /// <summary>Pure stand-in for Unity.Entities.Entity (hub-u2-spec.md §2.1).</summary>
    public struct EntityRef : IEquatable<EntityRef>, IComparable<EntityRef>
    {
        public int Index, Version;

        public EntityRef(int index, int version)
        {
            Index = index;
            Version = version;
        }

        /// <summary>Index == 0 &amp;&amp; Version == 0 (= Entity.Null).</summary>
        public bool IsNull => Index == 0 && Version == 0;

        public static EntityRef Null => default(EntityRef);

        public bool Equals(EntityRef other)
        {
            return Index == other.Index && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is EntityRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Index * 397 ^ Version;
        }

        /// <summary>Index, then Version (the stable order of §2.1).</summary>
        public int CompareTo(EntityRef other)
        {
            if (Index != other.Index)
                return Index < other.Index ? -1 : 1;
            if (Version != other.Version)
                return Version < other.Version ? -1 : 1;
            return 0;
        }

        public static bool operator ==(EntityRef a, EntityRef b)
        {
            return a.Index == b.Index && a.Version == b.Version;
        }

        public static bool operator !=(EntityRef a, EntityRef b)
        {
            return a.Index != b.Index || a.Version != b.Version;
        }

        /// <summary>"Index:Version", the log form of Entity used across the mod. Allocates: logs
        /// and dumps only, never per frame.</summary>
        public override string ToString()
        {
            return Index + ":" + Version;
        }
    }

    /// <summary>Run kind = SkiRunOwnershipSystem.KindOf = bindings.ts RunKind (hub-u2-spec.md §2.1).
    /// The snow injection system's private order (0 night / 1 snowmaking / 2 groomed) never leaves it.</summary>
    public enum RunKind : byte
    {
        Groomed = 0,
        Snowmaking = 1,
        Night = 2,
        Natural = 3,   // Round E1 (D16): APPENDED — never renumber (ResortBatch / LiftBatch assert 0/1/2/3)
        CatTrack = 4,  // T54 probe 1 (cat-track-run.md §2): APPENDED — a drawable green track that may cross other runs
    }

    /// <summary>T54 probe 1 (docs/research/cat-track-run.md §1 D2, §2, §4): the per-kind rules of the cat track, in one pure
    /// place so the attraction path, the hub stats and the tests read the same answer. A cat track is a RUN for the map and the
    /// catalog (listed, numbered, bound, fee) but not for the resort's numbers.</summary>
    public static class RunKindRules
    {
        /// <summary>Does a run of this kind count toward RunsCapacity, the attraction score, Runs / RunsOpen / RunsPartly (and
        /// so HubRules.Status), the green-variety flag and the "no open run" lift-closed rule? False for the cat track only.
        /// A whitelist of the four scoring kinds: an unknown kind (-1, or one added later and not registered here) counts false.</summary>
        public static bool CountsTowardResort(int kind)
        {
            return kind >= (int)RunKind.Groomed && kind <= (int)RunKind.Natural;
        }

        /// <summary>LiftServedRule.AssignLinked's leaf flag: a cat track may be served but never passes service on (D2).</summary>
        public static bool IsServedLeaf(int kind)
        {
            return kind == (int)RunKind.CatTrack;
        }

        /// <summary>True when a run of this kind is injected, measured or billed by the snow system. The cat track is none of
        /// these: it opens by the day-mean estimate alone (RunSnowRules.ThresholdC) and is never rasterised.</summary>
        public static bool HasSnowInjection(int kind)
        {
            return kind == (int)RunKind.Groomed || kind == (int)RunKind.Snowmaking || kind == (int)RunKind.Night;
        }
    }
}
