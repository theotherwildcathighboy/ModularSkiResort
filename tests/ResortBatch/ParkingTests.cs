// ParkingTests — Round S1 test file for tests/ResortBatch (ski-days spec ch3-counting-parking.md §7.2, P1 and P3-P6:
// the pure SkiParkingRules). Owner: stream C (docs/research/ski-days/s1-spec.md §4.3). Created empty by stage 0
// (2026-10-05, s1-spec §4.2 step 9): the e1-spec R9 convention — `public static class ParkingTests` with
// `public static void Run(Func<bool, string, string, bool> check)`; Program.Main calls ParkingTests.Run(Check).
// Oracles are literal hand values (ch3 §7.2). Stream C (2026-10-05) wrote P1, P3-P6; P2 is dropped with the
// parking-side lodge state (s1-spec ch3-R10: the census uses SkiDayRules.CheckLodge, tested in SkiDayTests C).
// Fixer pass (2026-10-05, review TL-X1): two-fault rows in P4 / P5 pin the whole verdict order.
//
// Not testable headless (ECS): the census job, the lane walk, the garage walk — covered by the `Parking [census]`
// lines, the DUMP and the field rows of ch3 §3.7.

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
        }

        // ── P1 constants and literal enum values ────────────────────────────────────────────────
        private static void Constants(Func<bool, string, string, bool> check)
        {
            const string w = "parking P1";
            check(SkiParkingRules.RadiusM == 300f, w, "RadiusM " + SkiParkingRules.RadiusM);
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

        // ── P3 RootOf and IsRestricted ──────────────────────────────────────────────────────────
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

        // ── P4 ClassifyLane ─────────────────────────────────────────────────────────────────────
        private static void Lanes(Func<bool, string, string, bool> check)
        {
            const string w = "parking P4";
            LaneCase(check, w, 0x40u, 1, 5.5f, 120f, false, V.Count, "right-side street lane");
            LaneCase(check, w, 0x40u, 1, 5.5f, 300f, false, V.Count, "bound inclusive");
            LaneCase(check, w, 0x40u, 1, 5.5f, 300.01f, false, V.TooFar, "beyond 300 m");
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
            // Fixer pass (review TL-X1): one two-fault row per neighbouring pair, so every swap of the order fails
            // (only the DUMP's not-counted buckets depend on it; car spaces do not).
            LaneCase(check, w, 0x200u, 1, 5.5f, 300.01f, false, V.TooFar, "too far before virtual");
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

        // ── P5 ClassifyGarage ───────────────────────────────────────────────────────────────────
        private static void Garages(Func<bool, string, string, bool> check)
        {
            const string w = "parking P5";
            GarageCase(check, w, 0x100u, 1, 40, 250f, false, V.Count, "parking hall in reach");
            GarageCase(check, w, 0x100u, 1, 40, 301f, false, V.TooFar, "beyond 300 m");
            GarageCase(check, w, 0x100u, 0x10, 40, 10f, false, V.NotCar, "bicycle only");
            GarageCase(check, w, 0x100u, 1, 40, 10f, true, V.Private, "restricted");
            GarageCase(check, w, 0x4100u, 1, 40, 10f, false, V.Closed, "disabled");
            GarageCase(check, w, 0x100u, 1, 0, 10f, false, V.NoSlots, "capacity 0");
            // Fixer pass (review TL-X1): the order, one row per neighbouring pair.
            GarageCase(check, w, 0x4100u, 0x10, 0, 301f, true, V.TooFar, "too far before everything");
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

        // ── P6 LodgeLacksParking (hub row 21) ───────────────────────────────────────────────────
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
