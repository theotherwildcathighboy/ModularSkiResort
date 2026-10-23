// Which ski area a top-level resort building (school, lodges, patrol huts) belongs to.
// Rule (same as lifts): a building with no Owner belongs to the nearest live Welcome Sign within the tether
// (SkiRunOwnershipSystem.TetherMeters, 5000 m; SkiResortIndex.TryResolve breaks ties by distance, Index, Version; 5000.00 m
// in, 5000.01 m out; a non-finite position never links). None = not linked, counts for no area. Not sticky: a nearer new sign
// takes the building. Nothing is stored or serialized. A school that is still a ServiceUpgrade child keeps the buffer rule.
// One count rule (Count): every school counted for an area adds 1 to Schools; SchoolActive is true when any is live and not
// Inactive. The child path passes its InstalledUpgrade entry's Inactive bit, the top-level path its own Building.m_OptionMask
// (BuildingPoliciesSystem.cs:100-130 mirrors the same bit into the entry), so every downstream number follows one flag.
// Log discipline (Diff): the system keeps the last published link per building and logs ONE line per change: Linked, Relinked
// (another sign is nearer), Unlinked (sign bulldozed or building moved away), Removed. Stable order (Index, Version).
// Kind* tags the log line and the count path. AreaEfficiency: once lodges carry their own staffing and utilities, the area's
// efficiency = the sign's efficiency x the mean of its active top-level lodges' efficiency weighted by tier base.
// Pure file: System, System.Collections.Generic and Unity.Mathematics only (tests/BuildingBatch links it).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SchoolLinkRules
    {
        /// <summary>One top-level building's link: its sign (EntityRef.Null = not linked) and the nearest sign's distance (+inf
        /// without a sign or with a non-finite position; set even beyond the tether, for the log).</summary>
        public struct Link : IEquatable<Link>
        {
            public EntityRef Sign;
            public float Distance;

            public bool Linked => !Sign.IsNull;

            /// <summary>Same sign; the distance is log text only.</summary>
            public bool Equals(Link other) => Sign == other.Sign;
            public override bool Equals(object obj) => obj is Link other && Equals(other);
            public override int GetHashCode() => Sign.GetHashCode();
        }

        /// <summary>Kind of a top-level resort building (log words and count path).</summary>
        public const byte KindLodge = 0, KindSchool = 1, KindPatrol = 2;

        public enum ChangeKind : byte { Linked = 0, Relinked = 1, Unlinked = 2, Removed = 3 }

        public struct Change
        {
            public ChangeKind Kind;
            public EntityRef School;
            public Link Before, After;
        }

        public static Link Resolve(SkiResortIndex signs, float2 xz, float maxDistance)
        {
            Link link;
            if (signs == null)
            {
                link.Sign = EntityRef.Null;
                link.Distance = float.PositiveInfinity;
                return link;
            }
            signs.TryResolve(xz, maxDistance, out link.Sign, out link.Distance);
            return link;
        }

        /// <summary>The count rule for a child entry or a linked top-level school: Schools + 1; SchoolActive when live (exists, not
        /// Deleted / Temp) and not Inactive.</summary>
        public static void Count(ref int schools, ref bool schoolActive, bool active, bool live)
        {
            schools++;
            if (active && live)
                schoolActive = true;
        }

        /// <summary>Log diff of the last published links against this recompute's (every live top-level school, linked or not): one
        /// Change per school whose sign differs, appears linked, or disappears; a new unlinked school is logged once as Unlinked.
        /// <paramref name="output"/> is cleared and filled in (Index, Version) order.</summary>
        public static void Diff(IReadOnlyDictionary<EntityRef, Link> before, IReadOnlyDictionary<EntityRef, Link> after,
                                List<Change> output)
        {
            output.Clear();
            foreach (KeyValuePair<EntityRef, Link> pair in after)
            {
                bool had = before.TryGetValue(pair.Key, out Link old);
                Link now = pair.Value;
                if (had && old.Equals(now))
                    continue;
                ChangeKind kind;
                if (!now.Linked)
                    kind = ChangeKind.Unlinked;
                else if (had && old.Linked)
                    kind = ChangeKind.Relinked;
                else
                    kind = ChangeKind.Linked;
                output.Add(new Change { Kind = kind, School = pair.Key, Before = had ? old : default(Link), After = now });
            }
            foreach (KeyValuePair<EntityRef, Link> pair in before)
            {
                if (!after.ContainsKey(pair.Key))
                    output.Add(new Change { Kind = ChangeKind.Removed, School = pair.Key, Before = pair.Value, After = default(Link) });
            }
            output.Sort((a, b) => a.School.CompareTo(b.School));
        }

        /// <summary>The area efficiency the score's staffing factor reads. With <paramref name="weightSum"/> &lt;= 0 (no active
        /// top-level lodge) returns <paramref name="signEfficiency"/> unchanged (the child path: lodge shortfalls already sit on the
        /// sign). Else sign x (weightedSum / weightSum), rounded to 0.01 as vanilla's GetEfficiency, floored at 0.01 unless 0. Non-finite -> 0.</summary>
        public static float AreaEfficiency(float signEfficiency, double weightedSum, double weightSum)
        {
            if (!(weightSum > 0.0))
                return signEfficiency;
            double mean = weightedSum / weightSum;
            double product = (double)signEfficiency * mean;
            if (!(product > 0.0) || double.IsInfinity(product))
                return 0f;
            float rounded = (float)Math.Round(100.0 * product) / 100f;
            return rounded > 0.01f ? rounded : 0.01f;
        }
    }
}
