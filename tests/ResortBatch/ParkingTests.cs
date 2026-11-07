// Tests for the pure SkiParkingRules (P1, P3-P9). Oracles are literal hand values (the comments show the arithmetic).
//
// Not testable headless (ECS): the census job, the lane walk, the garage walk.

using System;
using SkiResortMod.Systems;
using V = SkiResortMod.Systems.ParkingLaneVerdict;

namespace ResortBatch
{
    public static class ParkingTests
    {
        public static void Run(Func<bool, string, string, bool> check)
        {
            Constants(check);
            RootAndRestriction(check);
            Lanes(check);
            Garages(check);
            LacksParking(check);
            Reachable(check);
            RadiusFromWalkCost(check);
            GarageMeasure(check);
        }

        // P8: the radius is the largest round 100 m whose walk (x 1.3 detour) costs a mid tourist at most half the tourist budget.
        private static void RadiusFromWalkCost(Func<bool, string, string, bool> check)
        {
            const string w = "parking P8 (radius from the walk cost)";
            check(Math.Abs(SkiParkingRules.WalkSpeedMs - 1.6666666f) < 1e-5f, w, "walk speed 6 km/h = 1.667 m/s, got " + SkiParkingRules.WalkSpeedMs);
            // 5 x (4 - 3.75 x 128 / 255) = 5 x (4 - 1.882353) = 10.588235
            check(Math.Abs(SkiParkingRules.MidTimeWeight - 10.588235f) < 1e-4f, w, "mid time weight 10.588, got " + SkiParkingRules.MidTimeWeight);
            check(SkiParkingRules.TouristMaxCost == 8500f, w, "tourist budget 17,000 x 64 / 128 = 8,500, got " + SkiParkingRules.TouristMaxCost);
            // 100 m at time 10, comfort 2: (100 / 1.6667 x 10 + 100 x 0.01 x 2) x 0.75 = (600 + 2) x 0.75 = 451.5
            float c100 = SkiParkingRules.WalkCost(100f, 10f, 2f);
            check(Math.Abs(c100 - 451.5f) < 0.05f, w, "WalkCost(100, 10, 2) = 451.5, got " + c100);
            check(SkiParkingRules.WalkCost(0f, 10f, 2f) == 0f, w, "WalkCost(0) = 0");
            check(float.IsNaN(SkiParkingRules.WalkCost(-1f, 10f, 2f)) && float.IsNaN(SkiParkingRules.WalkCost(float.NaN, 10f, 2f)), w,
                  "WalkCost of a negative or NaN length is NaN");
            // 600 m x 1.3 = 780 m: (780 / 1.6667 x 10.588 + 780 x 0.02) x 0.75 = (4955.3 + 15.6) x 0.75 = 3728.2
            float c600 = SkiParkingRules.MidWalkCost(SkiParkingRules.RadiusM);
            check(Math.Abs(c600 - 3728.2f) < 1f, w, "MidWalkCost(600) = 3,728, got " + c600);
            check(c600 <= SkiParkingRules.TouristMaxCost / 2f, w, $"the radius's walk {c600:F0} is at most half the budget");
            // 700 m x 1.3 = 910 m: (910 / 1.6667 x 10.588 + 18.2) x 0.75 = 4349.5 > 4,250
            float c700 = SkiParkingRules.MidWalkCost(SkiParkingRules.RadiusM + 100f);
            check(c700 > SkiParkingRules.TouristMaxCost / 2f, w, $"100 m more ({c700:F0}) is over half the budget: the radius is the largest round 100 m");
            check(SkiParkingRules.RadiusM % 100f == 0f, w, "the radius is a round 100 m");
        }

        // P9: a parking hall is looked at within RadiusM + FacilityReachM of its center; each garage lane is measured at its own curve.
        private static void GarageMeasure(Func<bool, string, string, bool> check)
        {
            const string w = "parking P9 (garage entrance distance)";
            check(SkiParkingRules.FacilityReachM == 250f, w, "FacilityReachM 250");
            check(SkiParkingRules.FacilityNear(850f), w, "center at 850 m: looked at (bound inclusive)");
            check(!SkiParkingRules.FacilityNear(850.01f), w, "center at 850.01 m: not looked at");
            check(!SkiParkingRules.FacilityNear(float.NaN), w, "NaN center: not looked at");
            check(SkiParkingRules.GarageDistance(true, 580f, 700f) == 580f, w, "entrance lane at 580 m counts, though the center is at 700 m");
            check(SkiParkingRules.GarageDistance(false, 0f, 700f) == 700f, w, "no curve: the center");
            check(SkiParkingRules.ClassifyGarage(0x100u, 1, 40, SkiParkingRules.GarageDistance(true, 580f, 700f), false) == V.Count, w,
                  "a big hall with its entrance inside the radius counts");
        }

        private static void Reachable(Func<bool, string, string, bool> check)
        {
            const string w = "parking P7 (a lot with no road)";
            check(SkiParkingRules.SpaceReachable(ParkingRoot.Street, false), w, "street, no building");
            check(SkiParkingRules.SpaceReachable(ParkingRoot.ParkingBuilding, true), w, "lot on a road");
            check(!SkiParkingRules.SpaceReachable(ParkingRoot.ParkingBuilding, false), w, "lot with no road");
            check(!SkiParkingRules.SpaceReachable(ParkingRoot.OtherBuilding, false), w, "other building with no road");
        }

        private static void Constants(Func<bool, string, string, bool> check)
        {
            const string w = "parking P1";
            check(SkiParkingRules.RadiusM == 600f, w, "RadiusM " + SkiParkingRules.RadiusM);
            check(SkiParkingRules.BackstopTicks == 256, w, "BackstopTicks " + SkiParkingRules.BackstopTicks);
            check(SkiParkingRules.SignatureTicks == 16, w, "SignatureTicks " + SkiParkingRules.SignatureTicks);
            check(SkiParkingRules.MaxOwnerDepth == 8, w, "MaxOwnerDepth " + SkiParkingRules.MaxOwnerDepth);
            check(SkiParkingRules.LaneFlagVirtual == 0x200u, w, "LaneFlagVirtual 0x" + SkiParkingRules.LaneFlagVirtual.ToString("X"));
            check(SkiParkingRules.LaneFlagParkingDisabled == 0x2000u, w,
                  "LaneFlagParkingDisabled 0x" + SkiParkingRules.LaneFlagParkingDisabled.ToString("X"));
            check(SkiParkingRules.LaneFlagSpecialVehicles == 0x10000u, w,
                  "LaneFlagSpecialVehicles 0x" + SkiParkingRules.LaneFlagSpecialVehicles.ToString("X"));
            check(SkiParkingRules.LaneFlagAllowEnterExit == 0xC000u, w,
                  "LaneFlagAllowEnterExit 0x" + SkiParkingRules.LaneFlagAllowEnterExit.ToString("X"));
            check(SkiParkingRules.ConnFlagDisabled == 0x4000u, w, "ConnFlagDisabled 0x" + SkiParkingRules.ConnFlagDisabled.ToString("X"));
            check(SkiParkingRules.ConnFlagAllowEnterExit == 0x18000u, w,
                  "ConnFlagAllowEnterExit 0x" + SkiParkingRules.ConnFlagAllowEnterExit.ToString("X"));
            check(SkiParkingRules.RoadTypeCar == 1, w, "RoadTypeCar " + SkiParkingRules.RoadTypeCar);

            check((int)V.Count == 0, w, "ParkingLaneVerdict.Count 0");
            check((int)V.TooFar == 1, w, "ParkingLaneVerdict.TooFar 1");
            check((int)V.Virtual == 2, w, "ParkingLaneVerdict.Virtual 2");
            check((int)V.Service == 3, w, "ParkingLaneVerdict.Service 3");
            check((int)V.NotCar == 4, w, "ParkingLaneVerdict.NotCar 4");
            check((int)V.Private == 5, w, "ParkingLaneVerdict.Private 5");
            check((int)V.Closed == 6, w, "ParkingLaneVerdict.Closed 6");
            check((int)V.NoSlots == 7, w, "ParkingLaneVerdict.NoSlots 7");
            check((int)ParkingRoot.Street == 0, w, "ParkingRoot.Street 0");
            check((int)ParkingRoot.ParkingBuilding == 1, w, "ParkingRoot.ParkingBuilding 1");
            check((int)ParkingRoot.OtherBuilding == 2, w, "ParkingRoot.OtherBuilding 2");
        }

        private static void RootAndRestriction(Func<bool, string, string, bool> check)
        {
            const string w = "parking P3";
            RootCase(check, w, false, false, ParkingRoot.Street);
            RootCase(check, w, true, true, ParkingRoot.ParkingBuilding);
            RootCase(check, w, true, false, ParkingRoot.ParkingBuilding);
            RootCase(check, w, false, true, ParkingRoot.OtherBuilding);

            RestrictedCase(check, w, false, false, 0x0u, 0xC000u, false, "street");
            RestrictedCase(check, w, true, true, 0x0u, 0xC000u, false, "our sign or lodge");
            RestrictedCase(check, w, true, false, 0xC000u, 0xC000u, false, "public lot: both flags");
            RestrictedCase(check, w, true, false, 0xC040u, 0xC000u, false, "other bits ignored");
            RestrictedCase(check, w, true, false, 0x4000u, 0xC000u, true, "enter only");
            RestrictedCase(check, w, true, false, 0x8000u, 0xC000u, true, "exit only");
            RestrictedCase(check, w, true, false, 0x0u, 0xC000u, true, "no allow flag");
            RestrictedCase(check, w, true, false, 0x18000u, 0x18000u, false, "garage, both flags");
            RestrictedCase(check, w, true, false, 0x8000u, 0x18000u, true, "garage, enter only");
        }

        private static void RootCase(Func<bool, string, string, bool> check, string w, bool parking, bool building, ParkingRoot expected)
        {
            ParkingRoot r = SkiParkingRules.RootOf(parking, building);
            check(r == expected, w, $"RootOf({parking}, {building}) = {r}, expected {expected}");
        }

        private static void RestrictedCase(Func<bool, string, string, bool> check, string w, bool has, bool ours, uint flags,
                                           uint mask, bool expected, string label)
        {
            bool r = SkiParkingRules.IsRestricted(has, ours, flags, mask);
            check(r == expected, w, $"IsRestricted({has}, {ours}, 0x{flags:X}, 0x{mask:X}) = {r}, expected {expected} ({label})");
        }

        private static void Lanes(Func<bool, string, string, bool> check)
        {
            const string w = "parking P4";
            LaneCase(check, w, 0x40u, 1, 5.5f, 120f, false, V.Count, "right-side street lane");
            LaneCase(check, w, 0x40u, 1, 5.5f, 600f, false, V.Count, "bound inclusive");
            LaneCase(check, w, 0x40u, 1, 5.5f, 450f, false, V.Count, "a lot down the road at 450 m (the old 300 m radius missed it)");
            LaneCase(check, w, 0x40u, 1, 5.5f, 600.01f, false, V.TooFar, "beyond 600 m");
            LaneCase(check, w, 0x40u, 1, 5.5f, float.NaN, false, V.TooFar, "NaN distance");
            LaneCase(check, w, 0x200u, 1, 0f, 10f, false, V.Virtual, "gravel / alley boarding lane");
            LaneCase(check, w, 0x2200u, 1, 5.5f, 10f, false, V.Virtual, "virtual before closed");
            LaneCase(check, w, 0x10000u, 1, 5.5f, 10f, false, V.Service, "special vehicles");
            LaneCase(check, w, 0x0u, 0x10, 1.2f, 10f, false, V.NotCar, "bicycle only");
            LaneCase(check, w, 0x0u, 0x11, 5.5f, 10f, false, V.Count, "car and bicycle");
            LaneCase(check, w, 0x0u, 1, 5.5f, 10f, true, V.Private, "restricted");
            LaneCase(check, w, 0x2000u, 1, 5.5f, 10f, true, V.Private, "private before closed");
            LaneCase(check, w, 0x2000u, 1, 5.5f, 10f, false, V.Closed, "parking disabled");
            LaneCase(check, w, 0x0u, 1, 0f, 10f, false, V.NoSlots, "slot interval 0");
            LaneCase(check, w, 0x0u, 1, float.NaN, 10f, false, V.NoSlots, "slot interval NaN");
            // One two-fault row per neighboring pair, so every swap of the order fails
            // (only the DUMP's not-counted buckets depend on it; car spaces do not)
            LaneCase(check, w, 0x200u, 1, 5.5f, 600.01f, false, V.TooFar, "too far before virtual");
            LaneCase(check, w, 0x10200u, 1, 5.5f, 10f, false, V.Virtual, "virtual before service");
            LaneCase(check, w, 0x10000u, 0x10, 5.5f, 10f, false, V.Service, "service before not-car");
            LaneCase(check, w, 0x0u, 0x10, 5.5f, 10f, true, V.NotCar, "not-car before private");
            LaneCase(check, w, 0x2000u, 1, 0f, 10f, false, V.Closed, "closed before no slots");
        }

        private static void LaneCase(Func<bool, string, string, bool> check, string w, uint flags, byte roadTypes, float interval,
                                     float d, bool restricted, V expected, string label)
        {
            V v = SkiParkingRules.ClassifyLane(flags, roadTypes, interval, d, restricted);
            check(v == expected, w, $"ClassifyLane(0x{flags:X}, 0x{roadTypes:X}, {interval}, {d}, {restricted}) = {v}, expected {expected} ({label})");
        }

        private static void Garages(Func<bool, string, string, bool> check)
        {
            const string w = "parking P5";
            GarageCase(check, w, 0x100u, 1, 40, 250f, false, V.Count, "parking hall in reach");
            GarageCase(check, w, 0x100u, 1, 40, 601f, false, V.TooFar, "beyond 600 m");
            GarageCase(check, w, 0x100u, 0x10, 40, 10f, false, V.NotCar, "bicycle only");
            GarageCase(check, w, 0x100u, 1, 40, 10f, true, V.Private, "restricted");
            GarageCase(check, w, 0x4100u, 1, 40, 10f, false, V.Closed, "disabled");
            GarageCase(check, w, 0x100u, 1, 0, 10f, false, V.NoSlots, "capacity 0");
            // The order, one row per neighboring pair
            GarageCase(check, w, 0x4100u, 0x10, 0, 601f, true, V.TooFar, "too far before everything");
            GarageCase(check, w, 0x4100u, 0x10, 0, 10f, true, V.NotCar, "not-car before private");
            GarageCase(check, w, 0x4100u, 1, 0, 10f, true, V.Private, "private before closed");
            GarageCase(check, w, 0x4100u, 1, 0, 10f, false, V.Closed, "closed before no slots");
        }

        private static void GarageCase(Func<bool, string, string, bool> check, string w, uint flags, byte roadTypes, int capacity,
                                       float d, bool restricted, V expected, string label)
        {
            V v = SkiParkingRules.ClassifyGarage(flags, roadTypes, capacity, d, restricted);
            check(v == expected, w, $"ClassifyGarage(0x{flags:X}, 0x{roadTypes:X}, {capacity}, {d}, {restricted}) = {v}, expected {expected} ({label})");
        }

        private static void LacksParking(Func<bool, string, string, bool> check)
        {
            const string w = "parking P6";
            LacksCase(check, w, true, true, true, 0, true);
            LacksCase(check, w, true, true, true, 1, false);
            LacksCase(check, w, true, true, false, 0, false);
            LacksCase(check, w, true, false, true, 0, false);
            LacksCase(check, w, false, true, true, 0, false);
        }

        private static void LacksCase(Func<bool, string, string, bool> check, string w, bool on, bool target, bool done, int spaces,
                                      bool expected)
        {
            bool r = SkiParkingRules.LodgeLacksParking(on, target, done, spaces);
            check(r == expected, w, $"LodgeLacksParking({on}, {target}, {done}, {spaces}) = {r}, expected {expected}");
        }
    }
}
