// EntityRef and RunKind: pure stand-ins so pure files and the net8 test hosts (tests/ResortBatch, tests/LiftBatch),
// which cannot see Unity.Entities, can carry entities as {Index, Version}. Convert back at the system boundary
// with `new Entity { Index = r.Index, Version = r.Version }`.
//   * IsNull is Index == 0 && Version == 0, exactly Entity.Null; a merely nonexistent Entity is not null here either.
//   * Equality is Index and Version (a recycled index with a new version differs); CompareTo orders by Index, then Version.
//   * RunKind values equal SkiRunOwnershipSystem.KindOf and mod/SkiResortUI/src/bindings.ts RunKind. The snow
//     injection system's private kind order (0 night / 1 snowmaking / 2 groomed) never leaves it; never cast between them.
// Pure file: `using System;` only (the test-host rule), no allocation, no state.

using System;

namespace SkiResortMod.Systems
{
    /// <summary>Pure stand-in for Unity.Entities.Entity.</summary>
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

        /// <summary>Index, then Version.</summary>
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

        /// <summary>"Index:Version", the log form of Entity. Allocates: logs and dumps only.</summary>
        public override string ToString()
        {
            return Index + ":" + Version;
        }
    }

    /// <summary>Run kind; values match SkiRunOwnershipSystem.KindOf and bindings.ts RunKind.</summary>
    public enum RunKind : byte
    {
        Groomed = 0,
        Snowmaking = 1,
        Night = 2,
        Natural = 3,   // Appended: never renumber (the test hosts assert 0-3)
        CatTrack = 4,  // Appended: a drawable green track that may cross other runs
    }

    /// <summary>Per-kind rules of the cat track in one pure place. Since 2026-10-24 a cat track counts minimally: a member run of its
    /// resort (Runs, RunsOpen, the difficulty bar, the "no open run" lift rule) rated green by construction (the cat track tool grades it under
    /// 25 %, SkiRunRatingRules.CatTrackBand), worth a quarter of a run its size in the score and the capacity
    /// (SkiResortScore.CatTrackFactor / CatTrackSkiersFactor), and nothing else: no variety or school green, no vertical, it does
    /// not end the summer mode (SkiResortScore.Compute).</summary>
    public static class RunKindRules
    {
        /// <summary>Whether a run of this kind is a member of its resort's numbers (RunInput: RunsCapacity, the attraction score, Runs /
        /// RunsOpen / RunsPartly, so HubRules.Status, and the "no open run" lift-closed rule). A whitelist: an unknown kind counts false.
        /// The cat track's reduced weight lives in SkiResortScore.</summary>
        public static bool CountsTowardResort(int kind)
        {
            return kind >= (int)RunKind.Groomed && kind <= (int)RunKind.CatTrack;
        }

        /// <summary>LiftServedRule.AssignLinked's leaf flag: a cat track may be served but never passes service on.</summary>
        public static bool IsServedLeaf(int kind)
        {
            return kind == (int)RunKind.CatTrack;
        }

        /// <summary>True when the snow system injects, measures or bills a run of this kind. The cat track is none of these:
        /// it opens by the day-mean estimate alone (RunSnowRules.ThresholdC) and is never rasterized.</summary>
        public static bool HasSnowInjection(int kind)
        {
            return kind == (int)RunKind.Groomed || kind == (int)RunKind.Snowmaking || kind == (int)RunKind.Night;
        }
    }
}
