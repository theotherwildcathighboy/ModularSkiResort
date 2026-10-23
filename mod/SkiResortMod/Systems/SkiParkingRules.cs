// Pure rules of the lodge parking census: which parking lanes and garages within 300 m of a lodge count as car spaces, the
// street / lot split of the dump, and hub row 21 ("No parking near the lodge"). Consumers: SkiLodgeParkingSystem's census job,
// SkiDaySystem (LodgeLacksParking), tests/ResortBatch ParkingTests.
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
        public const float RadiusM = 300f;
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

        /// <summary>Hub row 21 per lodge: ski days on (locals or tourists &gt; 0), a ski-day target, the census done, and no car
        /// space within 300 m.</summary>
        public static bool LodgeLacksParking(bool skiDaysOn, bool skiDayTarget, bool censusDone, int carSpaces)
            => skiDaysOn && skiDayTarget && censusDone && carSpaces == 0;
    }
}
