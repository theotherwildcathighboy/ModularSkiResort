// OffSeasonRules - the pure rules of the off-season half staffing. Linked into tests/BuildingBatch.
//
// A ski area is off-season once its released score has counted no open run for HoldToOffFrames (12 game hours). It is in season again once
// a run has counted for HoldToOnFrames (one game hour, LiftClosedRule.HoldFrames). While off-season and the setting is on, each lodge and
// the ski school of that area keeps HalfJobs(full) workplaces (A-frame 4 to 2, chalet 12 to 6, grand lodge 18 to 9, school 4 to 2, plus the
// hut's 4 to 2 and the summit lodge's 8 to 4 on the Welcome Sign). Patrol stays full (rescue is a year-round job). Not linked / no score
// yet / score held: in season. Staff is halved, upkeep is not.
// Engine notes: EngineNotes/OffSeasonRules.md

using System;

namespace SkiResortMod.Systems
{
    /// <summary>One ski area's off-season state with its hysteresis. It is a plain value the caller keeps per area</summary>
    public struct OffSeasonState
    {
        public bool Known;          // observed (released score) at least once since the load
        public bool Off;            // the published state: off-season
        public bool Pending;        // the observation differs from Off and has kept differing since PendingSince
        public uint PendingSince;   // the sim frame of the first differing observation
    }

    public static class OffSeasonRules
    {
        /// <summary>12 game hours in sim frames (262,144 per day / 2): a mid-winter thaw shorter than this keeps the staff</summary>
        public const uint HoldToOffFrames = 131072u;
        /// <summary>One game hour (= LiftClosedRule.HoldFrames): the season opens as soon as the chairs would run again</summary>
        public const uint HoldToOnFrames = 10923u;
        public const bool DefaultEnabled = true;

        /// <summary>Feed one observation: <paramref name="noOpenRun"/> = the area's released score counts no run. The first
        /// observation after a load is taken at once (the saved staff already match it). Returns Off.</summary>
        public static bool Step(ref OffSeasonState s, bool noOpenRun, uint nowFrame)
        {
            if (!s.Known)
            {
                s.Known = true;
                s.Off = noOpenRun;
                s.Pending = false;
                return s.Off;
            }
            if (noOpenRun == s.Off)
            {
                s.Pending = false;
                return s.Off;
            }
            if (!s.Pending)
            {
                s.Pending = true;
                s.PendingSince = nowFrame;
            }
            uint hold = noOpenRun ? HoldToOffFrames : HoldToOnFrames;
            if (unchecked(nowFrame - s.PendingSince) >= hold)
            {
                s.Off = noOpenRun;
                s.Pending = false;
            }
            return s.Off;
        }

        /// <summary>Half the workplaces, rounded up (a staffed building never drops to 0). 0 stays 0, a negative reads 0</summary>
        public static int HalfJobs(int full)
        {
            return full <= 0 ? 0 : (full + 1) / 2;
        }

        public static int Target(int full, bool areaOff, bool enabled)
        {
            return enabled && areaOff ? HalfJobs(full) : Math.Max(0, full);
        }

        /// <summary>Do not change the workplaces of a building linked to this area for now: the area is in the attractiveness snapshot but
        /// this load has not observed it yet (its first score waits for ratings and snow). An unobserved area reads in season, so a write
        /// would raise the saved half staff and dismiss them again seconds later. Step takes the first observation at once because the
        /// saved staff already match it.</summary>
        public static bool AwaitsFirstObservation(bool linked, bool areaInSnapshot, bool observed)
        {
            return linked && areaInSnapshot && !observed;
        }

        /// <summary>Do not change a lodge of this area on this poll: it still awaits the first observation, or this poll took it. A
        /// carrier's carried sum is MountainJobsSystem's last poll, taken while the area still read in season (hut, summit and chained
        /// off-road lodges at full jobs). So a write on the observing poll would raise the saved staff and dismiss them again one poll
        /// later.</summary>
        public static bool HoldsLodgeWrite(bool awaitsFirstObservation, bool observedThisPoll)
        {
            return awaitsFirstObservation || observedThisPoll;
        }

        /// <summary>Lodges and the ski school are seasonal. Patrol and everything else keep their staff all year</summary>
        public static bool IsSeasonal(bool lodge, bool school)
        {
            return lodge || school;
        }
    }
}
