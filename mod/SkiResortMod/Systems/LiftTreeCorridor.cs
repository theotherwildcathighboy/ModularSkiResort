// The lift tree corridor (DECISIONS 601 e, owner hand-test note 8): a built lift line section fells the trees in its corridor, as real
// lifts cut one. Before, the lines felled nothing; vanilla only hides an Overridable tree under the net itself
// (ValidationHelpers.cs:691-715) and the moving carriers re-ran that every 2 m (ChairPinSystem Updated), so trees beside the rope
// popped in and out as chairs passed. The rule, in plan (xz): a tree falls when its trunk stands within HalfWidthM of the rope line
// (GaugeHalfM, the haul-rope lanes at +-x, + CarrierHalfWidthM + MarginM), or when its crown reaches into the carrier envelope
// (trunk distance - crown radius <= GaugeHalfM + CarrierHalfWidthM + CrownClearM). The line = the planned nodes (ground ends,
// towers, station stub nodes) plus the station's own stub edges; the distance is to the nearest segment, so the ends are round.
// Pure file: System.Collections.Generic, Unity.Mathematics, SkiCatalog (LiftTypeDef). Linked into tests/LiftBatch (TreeCorridorTests).
// The tool side (query, filter, the vanilla delete definition): LiftLineToolSystem.Trees.cs. Engine notes: EngineNotes/LiftTreeCorridor.md

using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class LiftTreeCorridor
    {
        /// <summary>Clearance beyond the carrier envelope for the trunk test (m): about a vanilla conifer's crown radius, so a crown
        /// next to the corridor stays clear of a carrier swinging 8 deg (FG 0.33 m, gondola 0.63 m at its hang).</summary>
        public const float MarginM = 3f;

        /// <summary>A crown closer than this to the carrier envelope falls whatever its trunk distance (m).</summary>
        public const float CrownClearM = 0.5f;

        /// <summary>The crown radius the rule believes at most (m): a prefab with wider bounds is read as this.</summary>
        public const float MaxCrownM = 8f;

        /// <summary>Delete definitions per build at most (the glade tool's apply is sized for 4,000-8,000 Temps); more are logged as
        /// left standing.</summary>
        public const int MaxFellPerBuild = 5000;

        /// <summary>Rope gauge + half the carrier's width (m): the carrier envelope's half width.</summary>
        public static float EnvelopeHalfM(LiftTypeDef d) => d.GaugeHalfM + d.CarrierHalfWidthM;

        /// <summary>The corridor's half width for the trunk test (m).</summary>
        public static float HalfWidthM(LiftTypeDef d) => EnvelopeHalfM(d) + MarginM;

        /// <summary>The corridor width the log names (m).</summary>
        public static float WidthM(LiftTypeDef d) => 2f * HalfWidthM(d);

        /// <summary>How far from a segment the tree search reaches (m): the larger of the trunk and the crown test.</summary>
        public static float QueryReachM(LiftTypeDef d) => EnvelopeHalfM(d) + math.max(MarginM, MaxCrownM + CrownClearM);

        /// <summary>One straight piece of the rope line in plan.</summary>
        public struct Segment
        {
            public float2 A, B;

            public Segment(float2 a, float2 b)
            {
                A = a;
                B = b;
            }
        }

        /// <summary>The xz distance from p to the nearest segment; +inf when there is none (a degenerate segment is its point).</summary>
        public static float DistanceM(float2 p, IReadOnlyList<Segment> line)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < line.Count; i++)
                best = math.min(best, LiftServedRule.DistanceSqXZ(p, line[i].A, line[i].B));
            return math.sqrt(best);
        }

        /// <summary>The rule: trunk within the corridor, or the crown (radius capped at MaxCrownM, negative read as 0) within
        /// CrownClearM of the carrier envelope.</summary>
        public static bool Fells(float distanceM, float crownRadiusM, float envelopeHalfM)
        {
            if (!(distanceM >= 0f) || float.IsInfinity(distanceM))
                return false;
            if (distanceM <= envelopeHalfM + MarginM)
                return true;
            float crown = math.clamp(crownRadiusM, 0f, MaxCrownM);
            return distanceM - crown <= envelopeHalfM + CrownClearM;
        }

        /// <summary>Fells for a tree at p against a line of lift type d.</summary>
        public static bool Fells(float2 p, float crownRadiusM, IReadOnlyList<Segment> line, LiftTypeDef d)
        {
            return Fells(DistanceM(p, line), crownRadiusM, EnvelopeHalfM(d));
        }
    }
}
