// OffSeasonRules — the PURE rules of the off-season half staffing (T137, Richard's ruling DECISIONS 358 (4): "Lodges can also run at
// half in the offseason, if that can be a thing"). Build report docs/research/economy/balance-build-2026-10-20.md. Created 2026-10-20.
//
// THE RULE. A ski area is OFF-SEASON once its released score has counted no open run for HoldToOffFrames (12 game hours); it is in
// season again once a run has counted for HoldToOnFrames (one game hour, LiftClosedRule.HoldFrames). While off-season and the setting
// is on, each lodge and the ski school of that area keeps HalfJobs(full) workplaces: the A-frame 4 -> 2, the chalet 12 -> 6, the grand
// lodge 18 -> 9, the school 4 -> 2, and the jobs the hut (4 -> 2) and the summit lodge (8 -> 4) put on the Welcome Sign. Patrol stays
// full (rescue is a year-round job). Not linked / no score yet / score held: in season (full staff, the old behavior).
//
// WHY STAFF AND NOT UPKEEP (decompile 1.6.0f1). The monthly bill of a city service building is (upkeep + the wage of every EMPLOYEE)
// x budget % (CityServiceBudgetSystem.cs:392-421; wages CityServiceUpkeepSystem.GetUpkeepOfEmployeeWage :628-643 sum the Employee
// buffer). Lowering WorkProvider.m_MaxWorkers lays the surplus off in the game's own tick (WorkProviderSystem.RefreshFreeWorkplace
// :199-230, LayOffReason.TooMany) — the same lever vanilla pulls for schools every tick (UpdateSchoolMaxWorkers :190-197) — so the
// wage part halves in the budget panel, the building's Upkeep section (:226-233 reads the same Employee buffer) and the hub. The
// UPKEEP part cannot be halved honestly: our prefabs bill it through ServiceConsumption.m_Upkeep, which BuildingInitializeSystem
// turns into a ServiceUpkeepData with m_ScaleWithUsage = false (:1183-1191); moving it to CityServiceBuilding.m_Upkeeps with
// m_ScaleWithUsage and a ServiceUsage of 0.5 would halve the budget line, but the selected building's Upkeep section applies the
// usage only to non-money resources (UpkeepSection.cs:183-198 vs :206-211), so it would show the full amount: a lie. Upkeep stays
// full; vanilla's Inactive (upkeep x0.1 and NO wage in the bill: CityServiceBudgetSystem.cs:396-411 with
// CityServiceUpkeepSystem.GetUpkeepOfEmployeeWage :630-633 returning 0 for a disabled building) remains the player's deep cut.
//
// PURE FILE: System only. Linked into tests/BuildingBatch (OS1-OS6).

using System;

namespace SkiResortMod.Systems
{
    /// <summary>One ski area's off-season state with its hysteresis. A plain value the caller keeps per area.</summary>
    public struct OffSeasonState
    {
        public bool Known;          // observed (released score) at least once since the load
        public bool Off;            // the published state: off-season
        public bool Pending;        // the observation differs from Off and has kept differing since PendingSince
        public uint PendingSince;   // the sim frame of the first differing observation
    }

    public static class OffSeasonRules
    {
        /// <summary>12 game hours in sim frames (262,144 per day / 2): a mid-winter thaw shorter than this keeps the staff.</summary>
        public const uint HoldToOffFrames = 131072u;
        /// <summary>One game hour (= LiftClosedRule.HoldFrames): the season opens as soon as the chairs would run again.</summary>
        public const uint HoldToOnFrames = 10923u;
        /// <summary>The setting's default (SkiResortSettings.OffSeasonHalfStaff).</summary>
        public const bool DefaultEnabled = true;

        /// <summary>Feed one observation: <paramref name="noOpenRun"/> = the area's released score counts no run. The first
        /// observation after a load is taken at once (nothing to debounce: the saved staff already match it). Returns Off.</summary>
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

        /// <summary>Half the workplaces, rounded up (a staffed building never drops to 0); 0 stays 0, a negative reads 0.</summary>
        public static int HalfJobs(int full)
        {
            return full <= 0 ? 0 : (full + 1) / 2;
        }

        /// <summary>The workplaces a seasonal building keeps: HalfJobs while its area is off-season and the setting is on, else full.</summary>
        public static int Target(int full, bool areaOff, bool enabled)
        {
            return enabled && areaOff ? HalfJobs(full) : Math.Max(0, full);
        }

        /// <summary>Lodges and the ski school are seasonal; patrol and everything else keep their staff all year.</summary>
        public static bool IsSeasonal(bool lodge, bool school)
        {
            return lodge || school;
        }
    }
}
