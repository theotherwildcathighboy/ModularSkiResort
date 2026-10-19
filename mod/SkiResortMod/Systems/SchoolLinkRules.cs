// SchoolLinkRules — which ski area a TOP-LEVEL ski school belongs to (T100, T10 D1 option A, DECISIONS 308;
// docs/research/lodges-top-level-spec.md (b), the school-first probe: docs/research/field/2026-10-19/school-top-level-t100.md).
// Created 2026-10-19.
//
// THE RULE (spec (b), the lift rule, SkiResortStatsSystem header MEMBERSHIP): a school building with no Owner (top-level,
// its prefab has no ServiceUpgrade component) belongs to the nearest live Welcome Sign within the tether
// (SkiRunOwnershipSystem.TetherMeters, 5000 m; SkiResortIndex.TryResolve: ties by distance, then Index, then Version;
// 5000.00 m in, 5000.01 m out; a non-finite position never links). None = NOT LINKED: the school counts for no area.
// Not sticky: a nearer new sign takes the school, as it takes lifts (runs stay sticky). Nothing is stored or serialized.
// A school that is still a ServiceUpgrade child keeps the buffer rule (the sign that lists it), so code reads union-safe
// while prefabs convert one at a time (spec (f)).
//
// ONE COUNT RULE for both kinds (Count): every school counted for an area adds 1 to its Schools; the area's SchoolActive
// is true when any of them is live and not Inactive. The child path passes its InstalledUpgrade entry's Inactive bit, the
// top-level path its own Building.m_OptionMask (BuildingPoliciesSystem mirrors the same bit into the entry for a child,
// BuildingPoliciesSystem.cs:100-130), so a linked top-level school yields the same AreaInput.SchoolActive as the child
// did, and every number downstream (SkiResortScore SchoolValue, SkiDaySystem SchoolFactor, SkiLiftTicketSystem lessons,
// HubProblemCode.SchoolNoGreenRun) follows from that one flag (tests/SchoolBatch).
//
// LOG DISCIPLINE (Diff): the system keeps the last published link per top-level school and writes ONE line per change:
// Linked (new school, or was not linked), Relinked (another sign is nearer now), Unlinked (no sign within the tether:
// sign bulldozed or school moved away), Removed (the building is gone). Stable order (school Index, Version).
//
// PURE FILE: System, System.Collections.Generic and Unity.Mathematics only (tests/SchoolBatch links it with EntityRef and
// SkiResortIndex).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SchoolLinkRules
    {
        /// <summary>One top-level school's link: its sign (EntityRef.Null = not linked) and the nearest sign's distance
        /// (+inf without a sign or with a non-finite position; set even when beyond the tether, for the log).</summary>
        public struct Link : IEquatable<Link>
        {
            public EntityRef Sign;
            public float Distance;

            public bool Linked => !Sign.IsNull;

            /// <summary>Same sign (the distance is log text only: a school moved closer to the same sign is no change).</summary>
            public bool Equals(Link other) => Sign == other.Sign;
            public override bool Equals(object obj) => obj is Link other && Equals(other);
            public override int GetHashCode() => Sign.GetHashCode();
        }

        public enum ChangeKind : byte { Linked = 0, Relinked = 1, Unlinked = 2, Removed = 3 }

        /// <summary>One log line's facts: the school, its link before (Null when new) and after.</summary>
        public struct Change
        {
            public ChangeKind Kind;
            public EntityRef School;
            public Link Before, After;
        }

        /// <summary>The attribution: nearest live sign of <paramref name="signs"/> within <paramref name="maxDistance"/>
        /// (SkiResortIndex.TryResolve, the lift rule).</summary>
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

        /// <summary>THE count rule (child entry or linked top-level school alike): Schools + 1; SchoolActive when this
        /// school is live (exists, not Deleted / Temp) and not Inactive.</summary>
        public static void Count(ref int schools, ref bool schoolActive, bool active, bool live)
        {
            schools++;
            if (active && live)
                schoolActive = true;
        }

        /// <summary>The log diff: <paramref name="before"/> = the last published links, <paramref name="after"/> = this
        /// recompute's (every live top-level school, linked or not). One Change per school whose sign differs, appears
        /// linked, or disappears; a NEW school that is not linked is Unlinked (its "not linked" state is logged once).
        /// <paramref name="output"/> is cleared, then filled in (Index, Version) order of the school.</summary>
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
    }
}
