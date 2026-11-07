// Pure rules of the lodge parking census: which parking lanes and garages within RadiusM (600 m) of a lodge count as car spaces, the
// street / lot split of the dump, hub row 21 ("No parking near the lodge") and where the radius comes from (WalkCost). Consumers:
// SkiLodgeParkingSystem's census job, SkiDaySystem (LodgeLacksParking), SkiAreaRingOverlaySystem (the parking ring), tests/ResortBatch
// ParkingTests.
//
// The flag constants mirror Game.Net.ParkingLaneFlags / ConnectionLaneFlags / RoadTypes (1.6.2); SkiLodgeParkingSystem
// asserts them against the enums at arm time (a mismatch = one WARN, census off for the load).
//
// Pure file: System and Unity.Mathematics only. Every method static, allocation-free and Burst-callable.

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>A parking lane's or garage's verdict in the census (first match wins).</summary>
    public enum ParkingLaneVerdict : byte { Count = 0, TooFar = 1, Virtual = 2, Service = 3, NotCar = 4, Private = 5, Closed = 6, NoSlots = 7 }

    /// <summary>Where a counted lane hangs (DUMP street / lot split only).</summary>
    public enum ParkingRoot : byte { Street = 0, ParkingBuilding = 1, OtherBuilding = 2 }

    public static class SkiParkingRules
    {
        /// <summary>Straight line from the lodge to the nearest point of a parking lane, or to a garage's entrance lane. Vanilla has no
        /// parking radius: a car trip is ONE pathfind (drive, park, walk to the door) whose cost is capped, so the walk from the car is
        /// what the radius stands for. 600 m is the largest round 100 m whose walk on foot (x SkiDayRules.DetourFactor) costs a
        /// mid-weight tourist at most half the tourist trip budget (WalkCost, TouristMaxCost; the other half pays the drive in from the
        /// border); 700 m costs 51 %. Field: residents with no closer parking walked about 790 m from the car to the near hill (2.6 game
        /// hours at 0.30 km per hour, research far-resort-travel-2026-10-31.md section 2). Was 300 m.</summary>
        public const float RadiusM = 600f;
        /// <summary>A parking facility is looked at when its building center is within RadiusM + this: its garage entrance lanes are
        /// then measured one by one (a big hall's entrance can sit well off its center).</summary>
        public const float FacilityReachM = 250f;
        // The walk's pathfind cost, vanilla 1.6.0f1 (identical in 1.6.2f1). Cost = (length / speed x time weight + comfort cost x comfort
        // weight) x random 0.5-1 (PathUtils.cs:28-35; walking cost 0.01 comfort per m, PedestrianPathfind.cs:10, added by length at
        // PathUtils.cs:1304). Walk speed 6 km/h (HumanPrefab.cs:14, :43). Time weight 5 x (4 - 3.75 x leisure counter / 255), comfort
        // weight 1 + 2 x random (CitizenUtils.cs:81-89): "mid" = leisure counter 128, comfort 2, random 0.75. Tourist budget = base
        // 17,000 x tourist priority 64 / 128 (TripPriorityParametersPrefab.cs:12, :35; TripPriorityParametersData.cs:83-86; the trip
        // sets it at TripNeededSystem.cs:1403 and seeks attractions at :1441-1442). Class defaults: the shipped prefab values are unverified.
        public const float WalkSpeedMs = 6f / 3.6f;
        public const float WalkComfortPerM = 0.01f;
        public const float MidTimeWeight = 5f * (4f - 3.75f * 128f / 255f);   // 10.59
        public const float MidComfortWeight = 2f;
        public const float MeanRandom = 0.75f;
        public const float TouristMaxCost = 17000f * 64f / 128f;            // 8,500
        public const int BackstopTicks = 256;  // × 16 frames = 4,096
        public const int SignatureTicks = 16;              // parking-count check every 256 frames
        public const int MaxOwnerDepth = 8;
        // Mirrors Game.Net.ParkingLaneFlags / ConnectionLaneFlags / RoadTypes; asserted at arm time
        public const uint LaneFlagVirtual = 0x200u, LaneFlagParkingDisabled = 0x2000u, LaneFlagSpecialVehicles = 0x10000u,
                          LaneFlagAllowEnterExit = 0xC000u;          // AllowEnter 0x4000 | AllowExit 0x8000
        public const uint ConnFlagDisabled = 0x4000u, ConnFlagAllowEnterExit = 0x18000u;   // AllowEnter 0x8000 | AllowExit 0x10000
        public const byte RoadTypeCar = 1;

        /// <summary>Any owner on the chain has CarParkingFacility or CarParking → ParkingBuilding; else any has
        /// Building → OtherBuilding; else Street.</summary>
        public static ParkingRoot RootOf(bool parkingBuildingOnChain, bool buildingOnChain)
        {
            return parkingBuildingOnChain ? ParkingRoot.ParkingBuilding
                 : buildingOnChain ? ParkingRoot.OtherBuilding
                 : ParkingRoot.Street;
        }

        /// <summary>A street space is on the road by definition; a space in a lot, a garage or another building needs that building
        /// connected to a road, or no car reaches it.</summary>
        public static bool SpaceReachable(ParkingRoot root, bool buildingOnChainHasRoad)
        {
            return root == ParkingRoot.Street || buildingOnChainHasRoad;
        }

        /// <summary>A lane closed to outsiders: it carries an access restriction that is not our sign or lodge, and the game did not
        /// set both AllowEnter and AllowExit on it (ParkingLaneDataSystem.cs:383-398, :225-279; PathUtils.cs:1195, :1220-1225).
        /// ParkingLaneDataSystem sets the restriction to the owning building for EVERY building lane, so the two allow flags are
        /// what opens a lot to outsiders. One flag without the other reads restricted (conservative; the game's use of a one-way
        /// pair is unverified).</summary>
        public static bool IsRestricted(bool hasRestriction, bool restrictionIsOurs, uint flags, uint allowEnterExitMask)
            => hasRestriction && !restrictionIsOurs && (flags & allowEnterExitMask) != allowEnterExitMask;

        /// <summary>First match wins. The bound is inclusive and a NaN distance or interval fails its test (TooFar / NoSlots).
        /// Virtual comes before Closed: a boarding-only lane is never a car space (PathUtils.cs:1187-1219).</summary>
        public static ParkingLaneVerdict ClassifyLane(uint laneFlags, byte roadTypes, float slotInterval, float distanceM, bool restricted)
        {
            if (!(distanceM <= RadiusM))
                return ParkingLaneVerdict.TooFar;
            if ((laneFlags & LaneFlagVirtual) != 0u)
                return ParkingLaneVerdict.Virtual;
            if ((laneFlags & LaneFlagSpecialVehicles) != 0u)
                return ParkingLaneVerdict.Service;
            if ((roadTypes & RoadTypeCar) == 0)
                return ParkingLaneVerdict.NotCar;
            if (restricted)
                return ParkingLaneVerdict.Private;
            if ((laneFlags & LaneFlagParkingDisabled) != 0u)
                return ParkingLaneVerdict.Closed;
            if (!(slotInterval > 0f))
                return ParkingLaneVerdict.NoSlots;
            return ParkingLaneVerdict.Count;
        }

        /// <summary>Garage verdict: the same first-match order as ClassifyLane minus the lane-only tests.</summary>
        public static ParkingLaneVerdict ClassifyGarage(uint connectionFlags, byte roadTypes, int capacity, float distanceM, bool restricted)
        {
            if (!(distanceM <= RadiusM))
                return ParkingLaneVerdict.TooFar;
            if ((roadTypes & RoadTypeCar) == 0)
                return ParkingLaneVerdict.NotCar;
            if (restricted)
                return ParkingLaneVerdict.Private;
            if ((connectionFlags & ConnFlagDisabled) != 0u)
                return ParkingLaneVerdict.Closed;
            if (capacity <= 0)
                return ParkingLaneVerdict.NoSlots;
            return ParkingLaneVerdict.Count;
        }

        /// <summary>The mean pathfind cost of walking <paramref name="meters"/> on foot at the given weights (see the constants). NaN or
        /// a negative length → NaN.</summary>
        public static float WalkCost(float meters, float timeWeight, float comfortWeight)
        {
            if (!(meters >= 0f))
                return float.NaN;
            return (meters / WalkSpeedMs * timeWeight + meters * WalkComfortPerM * comfortWeight) * MeanRandom;
        }

        /// <summary>The walk a straight-line distance stands for, at the mid weights: on foot it is DetourFactor longer.</summary>
        public static float MidWalkCost(float straightM)
            => WalkCost(straightM * (float)SkiDayRules.DetourFactor, MidTimeWeight, MidComfortWeight);

        /// <summary>A parking facility whose building center is this far from the lodge has its garage lanes measured.</summary>
        public static bool FacilityNear(float centerM) => centerM <= RadiusM + FacilityReachM;

        /// <summary>A garage's distance: its entrance lane's curve when it has one, else the building center.</summary>
        public static float GarageDistance(bool hasCurve, float curveM, float centerM) => hasCurve ? curveM : centerM;

        /// <summary>Hub row 21 per lodge: ski days on (locals or tourists &gt; 0), a ski-day target, the census done, and no car
        /// space within RadiusM.</summary>
        public static bool LodgeLacksParking(bool skiDaysOn, bool skiDayTarget, bool censusDone, int carSpaces)
            => skiDaysOn && skiDayTarget && censusDone && carSpaces == 0;
    }
}
