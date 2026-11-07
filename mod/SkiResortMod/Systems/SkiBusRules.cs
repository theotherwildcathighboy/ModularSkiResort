// The Ski Bus Depot's pure rules: the coach bill, the hill lodge a depot serves, what a claim verdict means for the bill and the hub.
// The depot itself is a claim target (SkiDayRules.CheckDepot). Nothing here moves a vehicle: the coach is a bill today, and a later visual
// bus reads SkiDaySystem.CopyDepots.
// Pure file: System, System.Collections.Generic and Unity.Mathematics only (tests/ResortBatch links it).
// Engine notes: EngineNotes/SkiBusChargeSystem.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiBusRules
    {
        /// <summary>The coach bill per depot per month: this much, plus <see cref="BillPerKmPerMonth"/> per straight-line km to the hill lodge.</summary>
        public const float BillFixedPerMonth = 1500f;
        public const float BillPerKmPerMonth = 300f;

        /// <summary>The monthly bill of one depot <paramref name="distanceM"/> from its hill lodge. A distance that is not finite or
        /// not above 0 bills the fixed part only.</summary>
        public static float MonthlyBill(float distanceM)
        {
            float km = distanceM > 0f && !float.IsInfinity(distanceM) ? distanceM / 1000f : 0f;
            return BillFixedPerMonth + BillPerKmPerMonth * km;
        }

        /// <summary>The coach runs while the depot passes its claim check. NoLeisureTag keeps it running: a Tourism Overhaul gate is the
        /// busy park's temporary state (SkiDaySystem step A keeps such a lodge counted too), not a depot that went away.</summary>
        public static bool IsBilled(SkiDayRules.LodgeReject verdict)
            => verdict == SkiDayRules.LodgeReject.None || verdict == SkiDayRules.LodgeReject.NoLeisureTag;

        /// <summary>The depot stands but can send nobody up: the hub's one depot problem row.</summary>
        public static bool CannotWork(SkiDayRules.LodgeReject verdict)
            => verdict == SkiDayRules.LodgeReject.NoHillLodge || verdict == SkiDayRules.LodgeReject.NoRoad;

        /// <summary>The index of the position nearest <paramref name="from"/> in straight xz distance. The first wins a tie. A position
        /// that is not finite never wins. The value is -1 for an empty list. <paramref name="distanceM"/> = its distance, +inf when
        /// -1.</summary>
        public static int NearestIndex(float2 from, IReadOnlyList<float2> positions, out float distanceM)
        {
            int best = -1;
            distanceM = float.PositiveInfinity;
            for (int i = 0; i < positions.Count; i++)
            {
                float d = SkiDayRules.DistanceXZ(from, positions[i]);
                if (d < distanceM)
                {
                    distanceM = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
