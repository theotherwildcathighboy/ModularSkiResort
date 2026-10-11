// SkiParkingRules — the PURE rules of the lodge parking census (Round S1 "Ski days", decision D26;
// docs/research/ski-days/s1-spec.md §2.7; ski-days spec ch3-counting-parking.md §3.2): which parking lanes and
// garages within 300 m of a lodge count as car spaces, the street / lot split of the DUMP, and hub row 21
// ("No parking near the lodge"). Consumers: SkiLodgeParkingSystem's census job (stream C), SkiDaySystem
// (LodgeLacksParking, row 21 per target lodge, every pass: ch3 §3.6, s1-spec N13); tests/ResortBatch
// ParkingTests (ch3 §7.2).
//
// The flag constants mirror Game.Net.ParkingLaneFlags / ConnectionLaneFlags / RoadTypes (ParkingLaneFlags.cs,
// ConnectionLaneFlags.cs, RoadTypes.cs: (=) in 1.6.2); SkiLodgeParkingSystem asserts them against the enums at
// arm time (a mismatch = one WARN, census off for the load).
//
// PURE FILE: `using System;` and `using Unity.Mathematics;` only. Every method static, allocation-free and
// Burst-callable (the census job calls it).
//
// Created 2026-10-05 (Round S1 stage 0: s1-spec §4.2 step 5 — enums, constants and LodgeLacksParking in full,
// the other bodies stubbed); owned by stream C, which wrote the bodies (ch3 §3.2 as amended by refutation R4).

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>A parking lane's or garage's verdict in the census (first match wins, ch3 §3.2).</summary>
    public enum ParkingLaneVerdict : byte { Count = 0, TooFar = 1, Virtual = 2, Service = 3, NotCar = 4, Private = 5, Closed = 6, NoSlots = 7 }

    /// <summary>Where a counted lane hangs (DUMP street / lot split only).</summary>
    public enum ParkingRoot : byte { Street = 0, ParkingBuilding = 1, OtherBuilding = 2 }

    public static class SkiParkingRules
    {
        public const float RadiusM = 300f;                 // D26
        public const int BackstopTicks = 256;              // × 16 frames = 4,096
        public const int SignatureTicks = 16;              // parking-count check every 256 frames
        public const int MaxOwnerDepth = 8;
        // = Game.Net.ParkingLaneFlags / ConnectionLaneFlags / RoadTypes (files (=) in 1.6.2); asserted at arm time
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

        /// <summary>A lane closed to outsiders: it carries an access restriction that is not our sign or lodge, and
        /// the game did not set both AllowEnter and AllowExit on it (ParkingLaneDataSystem.cs:383-398, :225-279;
        /// PathUtils.cs:1195, :1220-1225). ParkingLaneDataSystem sets the restriction to the owning building for
        /// EVERY building lane, so the two allow flags are what opens a park's, shop's or parking facility's lot to
        /// outsiders. AllowEnter without AllowExit (or the reverse) reads restricted: the conservative side (the
        /// game's exact use of a one-way pair is unverified, ch3 §3.2).</summary>
        public static bool IsRestricted(bool hasRestriction, bool restrictionIsOurs, uint flags, uint allowEnterExitMask)
            => hasRestriction && !restrictionIsOurs && (flags & allowEnterExitMask) != allowEnterExitMask;

        /// <summary>First match: !(distance ≤ 300) TooFar; VirtualLane; SpecialVehicles → Service; !(roadTypes &amp;
        /// Car) NotCar; restricted → Private; ParkingDisabled → Closed; !(slotInterval &gt; 0) NoSlots; else Count.
        /// The bound is inclusive and a NaN distance or interval fails its test (TooFar / NoSlots). Virtual comes
        /// before Closed: a boarding-only lane is never a car space, whatever else it says (PathUtils.cs:1187-1219).</summary>
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

        /// <summary>First match: TooFar; NotCar; restricted → Private; Disabled → Closed; capacity ≤ 0 → NoSlots;
        /// else Count.</summary>
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

        /// <summary>Hub row 21 per lodge (ch3 §3.6): ski days on (locals or tourists &gt; 0), a ski-day target, the
        /// census done, and no car space within 300 m.</summary>
        public static bool LodgeLacksParking(bool skiDaysOn, bool skiDayTarget, bool censusDone, int carSpaces)
            => skiDaysOn && skiDayTarget && censusDone && carSpaces == 0;
    }
}
