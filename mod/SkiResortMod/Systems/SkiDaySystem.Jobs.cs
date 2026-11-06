// The Burst jobs of SkiDaySystem and their blittable structs: SiteJob, ResolveJob, ClaimJob, CensusJob, NearJob. The system side
// (snapshot, scheduling, apply, logs, DUMP) is SkiDaySystem.cs; every demand, capacity, powder, confidence and gate rule is
// SkiDayRules', every time helper SkiDayTiming's. Vanilla writes: ClaimJob's Leisure.m_TargetAgent + m_LastPossibleFrame, one
// TripNeeded and the car-reserve queue; ResolveJob's deadline pull (Leisure.m_LastPossibleFrame). No structural change, no command buffer.
// Burst rules: no managed type, string or exception. Every component read is a TryGetComponent / HasComponent / TryGetBuffer (a
// destroyed lodge or a gone citizen is a normal input: Release builds run without collection checks, so an unguarded lookup reads a
// dead chunk slot). Every index into a snapshot array is range-checked. One access mode per type per job; the jobs run on one chain.
// Engine notes: EngineNotes/SkiDaySystem.Jobs.md

using Colossal.Entities;   // ComponentLookup.TryGetEnabledComponent (the reserve job's own call, CitizenBehaviorSystem.cs:67)
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Events;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiDaySystem
    {
        /// <summary>One tracked ski day: the in-memory "citizen → claim" record, compacted every pass. Stores the sign, not an area
        /// index (indices change at a rebuild).
        /// State: 0 on the way, 1 on site, 2 visit over and still inside (watched until the guest leaves the lodge, at most
        /// SkiDayTiming.ExitWatchFrames), 3 expired and watched for a late arrival (at most SkiDayTiming.LateWatchFrames), 4 followed
        /// after leaving the lodge for home, the map exit or with no trip purpose (at most SkiDayTiming.FollowWatchFrames). States 2,
        /// 3 and 4 are measure only: never in OnTheWay, OnSite, ActiveNow or the hub view; a late arrival feeds only the all-outcomes
        /// speed, never confidence. Sequential layout, field order as written (128 bytes; 32,768 at the cap: 4.0 MiB); measure fields
        /// are valid when Flags / FollowFlags say so (frames 0 otherwise). ClaimJob starts RemoveWhy, MoveReason, ExitAt at 255.</summary>
        internal struct SkiDayClaim
        {
            public Entity Citizen, Lodge, Sign, Origin;
            public uint ClaimFrame, Deadline, ArrivalFrame;
            public uint DepartFrame, RideFirstFrame, RideLastFrame, EndFrame, Near500Frame;   // Near500Frame 0 = never
            public uint MoveFrame;                     // first pass the walker moved off (no Divert{PathFailed}); 0 = not seen
            public uint ExitFrame, FollowRideFirst;    // the exit from the lodge; first pass seen riding while followed (0 = never)
            public float DistanceM, ClosestM;          // ClosestM: +∞ until a position was read
            public float2 LodgeXz;                     // the lodge's xz at the claim; NaN if it had none (never: NearestTarget)
            public ushort ClaimMinute, RidePasses;     // minute of the day at the claim; passes seen in a vehicle
            public ushort WaitPasses, WaitNearPasses, FailPasses, FollowRidePasses;   // saturating: while followed, passes queued at a stop (of them within NearLodgeM of the lodge), with a failed path, riding
            public byte Class /*TravelClass*/, Kind /*GuestKind*/, State /*0..4, above*/, CarEnqueued;
            public byte Legs /*M3 SkiDayRules.Leg… bits*/, Kinds /*M3 SkiDayRules.Kind… bits*/, Flags /*ClaimFlag*/, Flags2 /*ClaimFlag2*/;
            public byte EndCause /*M2 SkiDayTiming.EndCause*/, TurnPurpose /*first non-Leisure TravelPurpose seen on the way*/,
                        GoneWhy /*F3 SkiDayTiming.GoneWhy*/, Place /*M1 SkiDayTiming.Place at the failure (state 3 keeps it)*/;
            public byte RemoveWhy /*Round M2 D4 SkiDayTiming.RemoveWhy at the visit end; 255 = not read*/,
                        EndLive /*Round M2 D4 bits: 1 work time now, 2 bedtime now, 4 not checked (0 = no visit end)*/,
                        MoveReason /*Round M2 D4 the household's Game.Agents.MoveAwayReason; 255 = none*/,
                        ExitAt /*Round M2 U16 SkiDayTiming.NoPurposeAt at a no-purpose exit; 255 = n/a*/;
            public byte Exit /*Round M2 D5 SkiDayTiming.ExitWay at the exit*/, FollowLegs /*Leg… bits while followed*/,
                        FollowKinds /*Kind… bits while followed*/, FollowFlags /*SkiDayTiming.FollowFlag…*/;
            public byte FollowResult /*SkiDayTiming.FollowResult when the follow ended*/, StillOutState /*SkiDayTiming.StillOut at the last look*/;
        }

        /// <summary>SkiDayClaim.Flags bits (the values of SkiDayTiming.Flag…).</summary>
        private static class ClaimFlag
        {
            public const byte Departed = SkiDayTiming.FlagDeparted;                     // 1: left the origin (SkiDayTiming.DepartedNow)
            public const byte HeldAtHome = SkiDayTiming.FlagHeldAtHome;                 // 2: kept for the layout, never set (every queued car claim had left home at our first look)
            public const byte HeldEver = SkiDayTiming.FlagHeldEver;                     // 4: car held at some pass (queued car claims only)
            public const byte Rode = SkiDayTiming.FlagRode;                             // 8: seen in a vehicle
            public const byte PathFailedAtStart = SkiDayTiming.FlagPathFailedAtStart;   // 16: the walker carried Divert{PathFailed}
            public const byte DeadlineIsClosing = SkiDayTiming.FlagDeadlineIsClosing;   // 32: the claim's deadline is the lifts' closing
            public const byte LateArrived = SkiDayTiming.FlagLateArrived;               // 64: arrived during the late watch (state 3)
            public const byte ToDepot = SkiDayTiming.FlagToDepot;                       // 128: the claim's target is a bus depot
        }

        /// <summary>SkiDayClaim.Flags2 bits (the values of SkiDayTiming.Flag2…).</summary>
        private static class ClaimFlag2
        {
            public const byte TurnedBack = SkiDayTiming.Flag2TurnedBack;                // 1: a non-Leisure TravelPurpose away from the origin
            public const byte WalkerLost = SkiDayTiming.Flag2WalkerLost;                // 2: in no building with no live walker
            public const byte WalkerPathFailed = SkiDayTiming.Flag2WalkerPathFailed;    // 4: walker PathOwner Failed or Stuck (transient)
            public const byte VehiclePathFailed = SkiDayTiming.Flag2VehiclePathFailed;  // 8: lead vehicle PathOwner Failed or Stuck (transient)
            // Not markers: HasMarker reads only 1, 2, 4, 8
            // 16 and 32 (SkiDayTiming.Flag2NeedOpen / Flag2NeedOpenBefore) are the household need bits ShiftNeed writes
            public const byte DeadlinePulled = SkiDayTiming.Flag2DeadlinePulled;        // 64: the deadline was pulled in (PullDeadline)
        }

        /// <summary>One ended ski day, written by ResolveJob when its claim leaves the list (Gone and Retargeted at once, Expired at
        /// the end of its late watch, an arrived guest at its exit or at the end of its follow) into the 40-slot ring m_Ring. 108
        /// bytes. Frames 0 when the step did not happen. Every measure field is copied from the claim.</summary>
        internal struct ClaimRecord
        {
            public Entity Citizen, Sign;
            public uint ClaimFrame, Deadline, DepartFrame, RideFirstFrame, RideLastFrame, ArrivalFrame, EndFrame, ExitFrame, Near500Frame;
            public uint MoveFrame /*Round M2 D1*/, FollowEndFrame /*Round M2 D5: the pass the follow ended; 0 = not followed*/,
                        FollowRideFirst /*Round M2 D5*/;
            public float DistanceM, ClosestM;
            public ushort ClaimMinute, RidePasses;
            public ushort WaitPasses, WaitNearPasses, FailPasses, FollowRidePasses;
            public byte Class, Kind, Outcome /*0 Arrived, 1 Gone, 2 Retargeted, 3 Expired*/, Place /*255 arrived*/, EndCause /*255 failed*/,
                        Exit /*SkiDayTiming.ExitWay; 255 failed*/, Legs, Kinds, Flags, Flags2, GoneWhy, TurnPurpose,
                        LateResult /*SkiDayTiming.LateResult; 255 = not an expired claim*/;
            public byte RemoveWhy, EndLive, MoveReason, ExitAt;                       // the claim's
            public byte FollowResult, FollowFlags, FollowLegs, FollowKinds, StillOutState;   // the claim's
        }

        /// <summary>A resident's ski-day arrival (Kind 0) or visit end (Kind 1, with the guest-scan verdict),
        /// for the ledger check (city-wide; residents only).</summary>
        internal struct ResidentNote
        {
            public Entity Citizen;
            public uint ArrivalFrame;
            public byte Kind /*0 arrival, 1 visit end*/, Verdict /*SkiDayTiming.ScanVerdict at a visit end*/;
        }

        /// <summary>Range-checked writes into the measure rows (Release builds run Burst without collection checks, so an index past
        /// a row would write into a neighbor's memory). Burst-callable.</summary>
        private static class MeasureWrite
        {
            public static void Bump(NativeArray<int> a, int i)
            {
                if ((uint)i < (uint)a.Length)
                    a[i]++;
            }

            /// <summary>Adds <paramref name="hours"/> at <paramref name="hoursIndex"/> and counts one sample at
            /// <paramref name="countIndex"/>; nothing when either index is out of range.</summary>
            public static void AddHours(NativeArray<double> h, NativeArray<int> n, int countIndex, int hoursIndex, double hours)
            {
                if ((uint)hoursIndex >= (uint)h.Length || (uint)countIndex >= (uint)n.Length)
                    return;
                h[hoursIndex] += hours;
                n[countIndex]++;
            }
        }

        /// <summary>ResolveJob measure row (m_ResolveMeasure, one row per area + the orphan row): block bases, literal so a reader
        /// can check them. cls = travel class 0..2, kind 0..1, o = outcome (0 gone, 1 retargeted, 2 expired), band =
        /// SkiDayRules.DistanceBand, mode = SkiDayRules.ArrivalMode. Every sizing site uses RM.Count; nothing else moves.</summary>
        private static class RM
        {
            public const int Fail = 0;               // 108: ((o × 6 + place) × 3 + cls) × 2 + kind
            public const int FailMarker = 108;       // 15: marker × 3 + cls (failures)
            public const int ArrivedMarker = 123;    // 15: marker × 3 + cls (arrivals)
            public const int GoneWhy = 138;          // 54: (o × 9 + why) × 3 + cls, o 0 gone, 1 retargeted
            public const int FailClock = 192;        // 36: o × 12 + ClockBin
            public const int ExpiredBound = 228;     // 6: cls × 2 + (deadline is closing ? 1 : 0)
            public const int Closest = 234;          // 12: cls × 4 + ClosestBand (failures)
            public const int LateWatch = 246;        // 12: cls × 4 + LateResult
            public const int CarReserve = 258;       // 25: bucket × 5 + mode
            public const int PhaseN = 283;           // 39: phase × 3 + cls (counts; hours in RH)
            public const int ArrivalClock = 322;     // 12: ClockBin at arrival (on time)
            public const int EndCause = 334;         // 9: EndCause (counts; mean stay hours in RH.EndStay)
            public const int Exit = 343;             // 99: endCause × 11 + ExitWay
            public const int StayBin = 442;          // 18: cls × 6 + StayBin
            public const int ModeArrived = 460;      // 15: cls × 5 + MainMode
            public const int TransitKind = 475;      // 6: bit index of Kinds (arrivals)
            public const int ModeFailed = 481;       // 15: cls × 5 + MainMode (departed failures only)
            public const int NeverLeftFailed = 496;  // 3: cls (failures that never departed)
            public const int ArrivedBand = 499;      // 15: cls × 5 + band
            public const int FailedBand = 514;       // 15: cls × 5 + band
            public const int Scan = 529;             // 6: kind × 3 + ScanVerdict
            // 535..539: 5 unused slots (never bumped, never printed); reserved so the later bases do not move
            public const int DoorN = 540;            // 6: group × 3 + cls; group 0 arrivals with a door sample, 1 departed failures that moved off (hours in RH.Door)
            public const int DoorUnseen = 546;       // 3: cls: arrivals that left earlier but whose walker was never seen moving off
            public const int NeverMovedFailed = 549; // 3: cls: departed failures that never moved off
            public const int LateClock = 552;        // 12: ClockBin at a late arrival (state 3, ArrivedLate)
            public const int RemoveWhy = 564;        // 20: kind × 10 + SkiDayTiming.RemoveWhy (counts; hours in RH.RemoveStay per why)
            public const int RemoveFast = 584;       // 20: kind × 10 + RemoveWhy, stays under SkiDayTiming.FastStayHours
            public const int LiveEnd = 604;          // 18: endCause × 2 + (0 work time now, 1 bedtime now)
            public const int LiveUnknown = 622;      // 1: visit ends whose clock checks could not run (no economy, time or population data)
            public const int NoPurposeAt = 623;      // 6: SkiDayTiming.NoPurposeAt
            public const int FollowResult = 629;     // 36: SkiDayTiming.FollowSlot(kind, result, placed)
            public const int FollowAtOnce = 665;     // 9: SkiDayTiming.AtOnceSlot(kind, result), placed at the first sample
            public const int FollowHomeMode = 674;   // 5: MainMode(FollowLegs) of Home, not placed
            public const int FollowStillOut = 679;   // 5: SkiDayTiming.StillOut at the watch's end
            public const int FollowHomeN = 684;      // 1: count for RH.FollowHomeH
            public const int FollowWaitN = 685;      // 1: count for RH.FollowWaitH
            public const int FollowWaitNearN = 686;  // 1: count for RH.FollowWaitNearH
            public const int FollowFailSeen = 687;   // 1: followed guests with a failed-path sample
            public const int FollowSkipped = 688;    // 1: not followed: the FollowCap was reached
            public const int SpeedLearn = 689;       // 15: cls × 5 + SpeedKind (0 arrival, 1 late arrival, 2 bound lowered it, 3 bound already met, 4 no information)
            public const int Count = 704;
        }

        /// <summary>ResolveJob hour sums (m_ResolveHours, same rows as RM): phase × 3 + cls (counts in RM.PhaseN), then the mean-stay
        /// sums per EndCause (counts in RM.EndCause), then the blocks below.</summary>
        private static class RH
        {
            public const int EndStay = 39;           // 9: EndCause
            public const int Door = 48;              // 6: as RM.DoorN
            public const int RemoveStay = 54;        // 10: RemoveWhy; mean divisor = residents + tourists of that why in RM.RemoveWhy
            public const int FollowHomeH = 64;       // 1: hours from the exit to home, not placed (count RM.FollowHomeN)
            public const int FollowWaitH = 65;       // 1: hours queued at a stop while followed (count RM.FollowWaitN)
            public const int FollowWaitNearH = 66;   // 1: … of them within NearLodgeM of the lodge (count RM.FollowWaitNearN)
            public const int Count = 67;
        }

        /// <summary>The kind index of RM.SpeedLearn (cls × Count + kind).</summary>
        private static class SpeedKind
        {
            public const int Arrival = 0, LateArrival = 1, BoundLowered = 2, BoundMet = 3, NoInformation = 4;
            public const int Count = 5;
        }

        /// <summary>ClaimJob measure row (m_ClaimMeasure, one row per area).</summary>
        private static class CM
        {
            public const int SentBand = 0;          // 15: cls × 5 + band
            public const int TooLateClosing = 15;   // 3: cls
            public const int TooLateOwn = 18;       // 3: cls
            public const int Count = 21;
        }

        /// <summary>One ski area of the snapshot, in CopySigns order.</summary>
        private struct AreaJob
        {
            public Entity Sign;
            public EntityRef SignRef;
            public int LodgeStart, LodgeCount, RunStart, RunCount, StationStart, StationCount, Attraction, RunsCounted;
            public byte ScoreKnown, WriteHeld, NightRunOpen;
            public double Efficiency, RunsCapacity, OpenShare, Powder;   // Powder NaN = unknown (0 in the formulas)
            public double SchoolFactor;   // SchoolRule.DemandFactorOf(Result.SchoolActive, GreenCounted); 1 = none
        }

        /// <summary>One active lodge of the snapshot (the sign itself excluded); lodges of one area are contiguous.
        /// Its tier's numbers, copied on the main thread.</summary>
        private struct LodgeJob
        {
            public Entity Lodge;
            public int Area;
            public int Capacity;          // SkiCatalog.Lodges[i].Capacity (skiers a day; A-frame 360)
            public int PriceTolerance;    // SkiCatalog.Lodges[i].PriceTolerance
            public byte NoRoadNeeded;     // 1 = LodgeSupplyRule.NoRoadNeeded (Hut, Summit, or off-road on a cat track chain: passes without a road, never a claim target)
            public byte IsDepot;          // 1 = a bus depot: after the area's lodges, Capacity 0, tolerance 0, LodgeIndex -1, CheckDepot's verdict
            public short LodgeIndex;      // SkiCatalog.Lodges index (logs and DUMP only)
            public byte Merged;           // 1 = a merged building (SkiMergedLodge): holds skiers, never a claim target, passes the leisure tag check
        }

        /// <summary>SiteJob's per-lodge result, aligned with the lodge list.</summary>
        private struct SiteOut
        {
            public float2 Xz;
            public float NearestRunOrStationM;   // +∞ without a position or a member
            public byte HasPosition;
        }

        /// <summary>ClaimJob's per-area result (step A values, step B counts, the first claim of the pass).</summary>
        private struct ClaimOut
        {
            public int Pool, PoolTourists, Sent, SentWalk, SentCar, SentNoCar, SentTourists, CarsEnqueued, SentOffDay, SentDepot,
                       CapacityPeople, TargetLodges;
            public byte State /*AreaState*/, Limit /*SkiDayLimit*/, ClaimHours, HasFirst, FirstClass, FirstKind;
            public uint ClosingFrame;
            public double FirstChance;
            public float FirstDistM;
            public Entity FirstLodge;
            // The sum of the chances of the decisions that entered the pool (mean = / Pool), and of the chances the decisions refused as
            // too late to fit would have had here ("expected ski days lost").
            public double PoolChanceSum, TooLateChanceSum;
            // Step A's max PriceTolerance over passing lodges (the chance's price factor) and Σ Capacity over them (the lodge side of the
            // capacity; DUMP).
            public int PriceTolerance, LodgeCapacity;
        }

        /// <summary>ResolveJob's per-area outcome counters (one extra row for orphans).</summary>
        private struct ResolveOut
        {
            public int ArrivedDepot;   // of the arrivals, those whose claim target was a bus depot
            public int OnTheWay, OnSite, ArrivedWalk, ArrivedCar, ArrivedNoCar, ArrivedTourists, Gone,
                       Retargeted, RetargetedAtOrigin, Expired, Left, ArrivedNoLeisure;
            public double HoursWalk, HoursCar, HoursNoCar, FirstHours;
            public byte HasFirst, FirstClass, FirstKind;
            // Per-pass snapshots (not sums): on the way and not departed, on the way and in a vehicle this pass, state 2 (visit over, still
            // inside), state 3 (expired, watched). A claim that enters state 2, 3 or 4 in a pass is counted in that bucket the same pass.
            public int NotLeft, Riding, Leaving, Watching;
            // AtDoor, Following: per-pass snapshots (on the way with the walker standing at the door; state 4). FollowSkipped, Pulled:
            // per-pass counts (exits not followed at the cap; deadlines pulled in). PulledTo / PullReason: the last pull of the pass
            // (SkiDayTiming.Pull…).
            public int AtDoor, Following, FollowSkipped, Pulled;
            public uint PulledTo;
            public byte PullReason;
        }

        /// <summary>NearJob's per-lodge result, aligned with the lodge list. Walkers =
        /// every counted walker within SkiDayTiming.NearLodgeM (walking = Walkers − Waiting − FailedPath − Hangaround);
        /// NotDrawn = of them Unspawned; Ours* = ski-day guests among them (OursWaitingFollowed: waiting, holding a state-4
        /// claim); WaitN / WaitTimerSum / WaitTimerMax = Resident.m_Timer of the waiting ones (SkiDayTiming.WaitHoursOfTimer);
        /// StopCount / StopPoints = the game's WaitingPassengers.m_Count summed over the line stop points (one per line
        /// per stop; taxi stands carry no Game.Routes.Position, so they are not seen) within the radius;
        /// NearestStopM = the nearest line stop point (+∞ when none).</summary>
        private struct NearOut
        {
            public int Walkers, Waiting, FailedPath, Hangaround, NotDrawn, Ours, OursWaiting, OursWaitingFollowed, OursFailed,
                       WaitN, WaitTimerMax, StopCount, StopPoints;
            public long WaitTimerSum;
            public float NearestStopM;
            public byte HasPosition;
        }

        /// <summary>An attraction outing Traveling to a snapshot sign or lodge (CensusJob list b).</summary>
        private struct TravelingOuting
        {
            public Entity Meeting, Target;
        }

        /// <summary>m_PassStats indices ("chunks, skipped LS slice, skipped CBS slice, citizens walked,
        /// Reject counts (9), fresh locals at home, fresh tourists in a hotel, fresh with a car, shopping need,
        /// tourists leaving today, claims, cap reached, walk ran"). Fresh / shopping / leaving count a decision once:
        /// on its last unskipped sighting (SkiDayTiming.IsLastSighting), or at its claim — and only on a pass that
        /// walked (a closed, knobs-off or capped pass counts nothing for the slice whose last sighting it is).</summary>
        private static class Stat
        {
            public const int Chunks = 0, SkippedLeisure = 1, SkippedBehavior = 2, Walked = 3;
            public const int RejectBase = 4;   // + (int)SkiDayRules.Reject, 0..8 (slot 4 = passed)
            public const int FreshLocals = 13, FreshTourists = 14, FreshWithCar = 15, ShoppingNeed = 16, LeavingToday = 17,
                             Claimed = 18, CapReached = 19, WalkRan = 20;
            public const int Count = 24;
        }

        /// <summary>m_Census indices (the DUMP's tourists line).</summary>
        private static class Census
        {
            public const int Parties = 0, People = 1, AtEdge = 2, Hotel = 3, InHotel = 4, OtherLodging = 5, NoHotel = 6,
                             OutingUsed = 7, WithCar = 8, LodgingSeekers = 9, MovingAway = 10, Ran = 11;
            public const int Count = 12;
        }

        /// <summary>Occupant kinds, m_Occupants[lodge × Kinds + kind] (first match in this order).</summary>
        private static class Occupant
        {
            public const int Worker = 0, ResidentOnLeisure = 1, TouristOnLeisure = 2, OutingMember = 3, Other = 4;
            public const int Kinds = 5;
        }

        /// <summary>Per lodge: position and the distance to the nearest run outline or lift station of its own ski
        /// area. A run's box distance is never larger than its polygon distance, so a run whose box is not
        /// nearer than the current nearest is skipped exactly; a run without a Node buffer (k &lt; 3) uses its box.</summary>
        [BurstCompile]
        private struct SiteJob : IJob
        {
            [ReadOnly] public ComponentLookup<Game.Objects.Transform> Transforms;
            [ReadOnly] public ComponentLookup<Game.Areas.Geometry> Geometries;
            [ReadOnly] public BufferLookup<Game.Areas.Node> Nodes;
            [ReadOnly] public NativeArray<AreaJob> Areas;
            [ReadOnly] public NativeArray<LodgeJob> Lodges;
            [ReadOnly] public NativeArray<Entity> Runs;
            [ReadOnly] public NativeArray<Entity> Stations;
            public NativeArray<SiteOut> Out;   // aligned with Lodges

            public void Execute()
            {
                int n = math.min(Lodges.Length, Out.Length);
                for (int li = 0; li < n; li++)
                {
                    LodgeJob lj = Lodges[li];
                    SiteOut so = new SiteOut { NearestRunOrStationM = float.PositiveInfinity };
                    if (Transforms.TryGetComponent(lj.Lodge, out Game.Objects.Transform t) && lj.Area >= 0 && lj.Area < Areas.Length)
                    {
                        so.HasPosition = 1;
                        so.Xz = t.m_Position.xz;
                        so.NearestRunOrStationM = Nearest(Areas[lj.Area], so.Xz);
                    }
                    Out[li] = so;
                }
            }

            private float Nearest(AreaJob aj, float2 p)
            {
                float nearest = float.PositiveInfinity;
                int runEnd = math.min(aj.RunStart + aj.RunCount, Runs.Length);
                for (int r = math.max(0, aj.RunStart); r < runEnd; r++)
                {
                    Entity run = Runs[r];
                    if (!Geometries.TryGetComponent(run, out Game.Areas.Geometry g))
                        continue;
                    float box = SkiDayRules.DistanceToBoundsXZ(p, g.m_Bounds.min.xz, g.m_Bounds.max.xz);
                    if (box >= nearest)
                        continue;   // exact: the box is never farther than the polygon
                    float d = box;
                    if (Nodes.TryGetBuffer(run, out DynamicBuffer<Game.Areas.Node> nb) && nb.Length >= 3)
                    {
                        // The run's outline (closed polygon, xz; float2.y = world z): even-odd inside test and the
                        // nearest edge.
                        int k = nb.Length;
                        bool inside = false;
                        float edge = float.PositiveInfinity;
                        for (int i = 0; i < k; i++)
                        {
                            float2 a = nb[i].m_Position.xz;
                            float2 b = nb[i + 1 < k ? i + 1 : 0].m_Position.xz;
                            if (SkiDayRules.CrossesRayXZ(p, a, b))
                                inside = !inside;
                            edge = SkiDayRules.NearestOf(edge, SkiDayRules.DistanceToSegmentXZ(p, a, b));
                        }
                        d = inside ? 0f : edge;
                    }
                    nearest = SkiDayRules.NearestOf(nearest, d);
                }
                int stationEnd = math.min(aj.StationStart + aj.StationCount, Stations.Length);
                for (int s = math.max(0, aj.StationStart); s < stationEnd; s++)
                {
                    if (Transforms.TryGetComponent(Stations[s], out Game.Objects.Transform st))
                        nearest = SkiDayRules.NearestOf(nearest, SkiDayRules.DistanceXZ(p, st.m_Position.xz));
                }
                return nearest;
            }
        }

        /// <summary>Every tracked claim, every pass: outcome, counters, learning, compaction; then the claims on the way + on site per
        /// area (ActiveNow, the room count). Every index checked.
        /// Per claim, in this order: gone first (the citizen entity no longer exists or is being deleted); a state-2 claim goes to Exit
        /// and nothing else; a state-4 claim goes to Follow and nothing else; a state-0 or state-3 claim that is not gone is sampled
        /// (Sample); a state-3 claim goes to the late check and nothing else; then Resolve and the outcome switch, plus the measure
        /// rows, the ring of ended ski days and the resident notes. The learning and every count the hub or a guest rule reads ignore
        /// states 2, 3 and 4. After the switch, the deadline pull for a guest on site or arriving: the job's one write to vanilla state.</summary>
        [BurstCompile]
        private struct ResolveJob : IJob
        {
            // Read-write: the deadline pull writes Leisure.m_LastPossibleFrame of the claim's own citizen; the only RW lookup in the job
            // (engine notes).
            public ComponentLookup<Leisure> Leisures;
            [ReadOnly] public ComponentLookup<CurrentBuilding> CurrentBuildings;
            [ReadOnly] public NativeParallelHashMap<Entity, int> AreaOfSign;
            [ReadOnly] public NativeParallelHashMap<Entity, int> LodgeIndex;
            // Read-only lookups, every read a TryGet / Has. Types are fully qualified (Game.Prefabs and Game.Vehicles both have Taxi and
            // PublicTransport).
            [ReadOnly] public ComponentLookup<Citizen> Citizens;
            [ReadOnly] public ComponentLookup<Deleted> Deleteds;
            [ReadOnly] public ComponentLookup<CarKeeper> CarKeepers;   // enableable: TryGetEnabledComponent (the reserve job's read)
            [ReadOnly] public ComponentLookup<Game.Vehicles.PersonalCar> PersonalCars;
            [ReadOnly] public ComponentLookup<Game.Objects.Transform> Transforms;
            [ReadOnly] public ComponentLookup<MovingAway> MovingAways;
            [ReadOnly] public ComponentLookup<TravelPurpose> TravelPurposes;
            [ReadOnly] public ComponentLookup<Game.Citizens.CurrentTransport> CurrentTransports;
            [ReadOnly] public ComponentLookup<Game.Creatures.CurrentVehicle> CurrentVehicles;
            [ReadOnly] public ComponentLookup<Game.Creatures.Divert> Diverts;
            [ReadOnly] public ComponentLookup<Game.Pathfind.PathOwner> PathOwners;
            [ReadOnly] public ComponentLookup<Game.Prefabs.PrefabRef> PrefabRefs;
            [ReadOnly] public ComponentLookup<Game.Vehicles.Controller> Controllers;
            [ReadOnly] public ComponentLookup<Game.Vehicles.Bicycle> Bicycles;
            [ReadOnly] public ComponentLookup<Game.Vehicles.Taxi> Taxis;
            [ReadOnly] public ComponentLookup<Game.Vehicles.PublicTransport> PublicTransports;
            [ReadOnly] public ComponentLookup<Game.Prefabs.PublicTransportVehicleData> TransportVehicleDatas;
            [ReadOnly] public ComponentLookup<HealthProblem> HealthProblems;
            [ReadOnly] public ComponentLookup<HouseholdMember> HouseholdMembers;
            [ReadOnly] public ComponentLookup<Game.Companies.ResourceBuyer> ResourceBuyers;
            [ReadOnly] public BufferLookup<TripNeeded> TripNeededs;   // RO here; ClaimJob's RW type handle runs after this job
            // Read-only inputs (every read a TryGet / Has). Areas: the same m_Areas snapshot ClaimJob gets this pass (the deadline pull
            // reads ScoreKnown, WriteHeld, RunsCounted, NightRunOpen). Workers … Populations, Economy, TimeData, PopulationEntity,
            // LiveKnown: the clock checks and traces (LiveKnown 0 = the economy, time or population singleton is missing: "not checked").
            // Residents, OutsideConnections, FollowingLast, FollowCap: the follow (FollowingLast = last pass's state-4 total). TripKnobs:
            // the fit check's speed.
            [ReadOnly] public NativeArray<AreaJob> Areas;
            [ReadOnly] public ComponentLookup<Worker> Workers;
            [ReadOnly] public ComponentLookup<Game.Citizens.Student> Students;   // qualified: Game.Buildings has Student too
            [ReadOnly] public ComponentLookup<HouseholdNeed> HouseholdNeeds;
            [ReadOnly] public ComponentLookup<Building> Buildings;
            [ReadOnly] public ComponentLookup<Game.Objects.OutsideConnection> OutsideConnections;
            [ReadOnly] public ComponentLookup<Game.City.Population> Populations;
            [ReadOnly] public ComponentLookup<Game.Creatures.Resident> Residents;
            public Entity PopulationEntity;
            public byte LiveKnown;
            public int FollowingLast, FollowCap;
            public SkiDayRules.TripKnobs TripKnobs;
            public NativeList<SkiDayClaim> Claims;
            public NativeArray<SkiDayRules.ClassTrack> Tracks;   // areas × 3
            public NativeArray<ResolveOut> Out;                  // areas + 1: the last row is the orphan row
            public NativeArray<int> ActiveNow;                   // areas
            public NativeArray<int> LodgeCarFails;               // lodges (this pass)
            // Writable outputs (the only ones besides the above): measure rows (zeroed here), the ring of ended ski days and its state
            // ([0] next slot, [1] records this load; cleared at load), resident notes (cleared by the system before scheduling).
            public NativeArray<int> Measure;                     // (areas + 1) × RM.Count
            public NativeArray<double> Hours;                    // (areas + 1) × RH.Count
            public NativeArray<ClaimRecord> Ring;                // Tuning.RingSize
            public NativeArray<int> RingState;                   // 2
            public NativeList<ResidentNote> Notes;
            public uint Now;
            public float TimeOfDay;                              // TimeSystem.normalizedTime (as ClaimJob gets it)
            public byte ScanKnown;                               // the guest counter is armed and has scanned this load
            public uint ScanFrame, ScanInterval, PassInterval;   // its last leisure scan, its scan interval, ours
            // Vanilla structs held by value go last: a game patch that resizes one shifts only these two (DECISIONS 581 e).
            public Game.Prefabs.EconomyParameterData Economy;
            public TimeData TimeData;

            /// <summary>No measure row (the measure arrays are smaller than one row): every index built on it is negative
            /// and MeasureWrite skips it.</summary>
            private const int NoRow = int.MinValue / 2;

            public void Execute()
            {
                for (int i = 0; i < Out.Length; i++)
                    Out[i] = default;
                for (int i = 0; i < ActiveNow.Length; i++)
                    ActiveNow[i] = 0;
                for (int i = 0; i < LodgeCarFails.Length; i++)
                    LodgeCarFails[i] = 0;
                for (int i = 0; i < Measure.Length; i++)
                    Measure[i] = 0;
                for (int i = 0; i < Hours.Length; i++)
                    Hours[i] = 0.0;
                if (Out.Length == 0)
                    return;   // never: the system always sizes the orphan row
                int orphan = Out.Length - 1;
                int measureRows = Measure.Length / RM.Count, hourRows = Hours.Length / RH.Count;

                int w = 0;
                int started = 0;   // follows begun this pass (FollowingLast + started stays under FollowCap)
                for (int i = 0; i < Claims.Length; i++)
                {
                    SkiDayClaim c = Claims[i];
                    int a = AreaOfSign.TryGetValue(c.Sign, out int ai) && ai >= 0 && ai < orphan ? ai : -1;   // −1 → orphan
                    int ti = SkiDayTiming.TrackIndex(a, c.Class);
                    if (ti >= Tracks.Length)
                        ti = -1;
                    int r = a >= 0 ? a : orphan;   // outcome counters only
                    int mb = measureRows > 0 ? math.min(r, measureRows - 1) * RM.Count : NoRow;
                    int hb = hourRows > 0 ? math.min(r, hourRows - 1) * RH.Count : NoRow;
                    int cls = math.min((int)c.Class, 2), kind = math.min((int)c.Kind, 1);

                    bool gone = !Citizens.HasComponent(c.Citizen) || Deleteds.HasComponent(c.Citizen);
                    bool hasL = Leisures.TryGetComponent(c.Citizen, out Leisure l);
                    bool hasCb = CurrentBuildings.TryGetComponent(c.Citizen, out CurrentBuilding cb);
                    bool at = hasCb && cb.m_CurrentBuilding == c.Lodge;
                    bool inBuilding = hasCb && cb.m_CurrentBuilding != Entity.Null;
                    bool atOrigin = inBuilding && cb.m_CurrentBuilding == c.Origin;
                    Entity building = inBuilding ? cb.m_CurrentBuilding : Entity.Null;   // exit place and follow
                    bool hasTp = TravelPurposes.TryGetComponent(c.Citizen, out TravelPurpose tp);
                    byte purpose = hasTp ? (byte)tp.m_Purpose : (byte)0;
                    ResolveOut ro = Out[r];

                    if (c.State == 2)
                    {
                        // Exit watch: the visit is over; is the guest out of the lodge yet? 0 keep watching, 1 recorded and dropped, 2 now followed
                        // (state 4, kept).
                        byte x = Exit(ref c, ref ro, ref started, mb, hb, cls, gone, at, inBuilding, building, atOrigin, hasTp, purpose);
                        if (x == 1)
                        {
                            Out[r] = ro;
                            continue;   // recorded and dropped
                        }
                        if (x == 2)
                            ro.Following++;
                        else
                            ro.Leaving++;
                        Out[r] = ro;
                        Claims[w++] = c;
                        continue;
                    }
                    if (c.State == 4)
                    {
                        // Followed after leaving the lodge (measure only; the ring record is written at its end).
                        if (Follow(ref c, mb, hb, false, gone, inBuilding, building, at, atOrigin))
                        {
                            Out[r] = ro;
                            continue;   // recorded and dropped
                        }
                        ro.Following++;
                        Out[r] = ro;
                        Claims[w++] = c;
                        continue;
                    }
                    bool riding = false, atDoor = false;   // atDoor: false unless Sample says so
                    if (!gone && (c.State == 0 || c.State == 3))
                        riding = Sample(ref c, mb, hb, cls, inBuilding, atOrigin, at, hasTp, purpose, out atDoor);   // never for a gone citizen
                    if (c.State == 3)
                    {
                        // Late watch: an expired claim; confidence booked it at the expiry. A late arrival's clock bin and the all-outcomes speed
                        // (exact or a bound) come BEFORE the record and the `continue`.
                        if (SkiDayTiming.TryLate(gone, at, hasL, hasL ? l.m_LastPossibleFrame : 0u, c.Deadline, Now, out SkiDayTiming.LateResult late))
                        {
                            MeasureWrite.Bump(Measure, mb + RM.LateWatch + cls * SkiDayTiming.LateResultCount + (int)late);
                            if (late == SkiDayTiming.LateResult.ArrivedLate)
                            {
                                c.Flags |= ClaimFlag.LateArrived;
                                c.ArrivalFrame = Now;
                                MeasureWrite.Bump(Measure, mb + RM.LateClock + SkiDayTiming.ClockBin(TimeOfDay));
                            }
                            if (ti >= 0)
                            {
                                // The place is read NOW, as FailureEvidence reads it at its failure. c.Place is
                                // the expiry's place, up to LateWatchFrames earlier; a GaveUp guest is in a building by
                                // construction (CitizenBehaviorSystem removes or replaces Leisure only for a citizen in a
                                // building with a Transform, CitizenBehaviorSystem.cs:651-659 (1.6.2 :675-684)), so it reads
                                // "no information".
                                SkiDayTiming.Place lateAt = SkiDayTiming.PlaceOf(gone, inBuilding, atOrigin, at, (c.Flags & ClaimFlag.Departed) != 0);
                                LearnAtLateEnd(ti, mb, cls, c, late, lateAt);
                            }
                            Record(c, 3, c.Place, 255, Now, (byte)late);
                            Out[r] = ro;
                            continue;   // dropped
                        }
                        ro.Watching++;
                        Out[r] = ro;
                        Claims[w++] = c;
                        continue;
                    }

                    bool leisureAtLodge = hasL && l.m_TargetAgent == c.Lodge;
                    SkiDayTiming.ClaimOutcome o = SkiDayTiming.Resolve(c.State == 1, hasL, leisureAtLodge, at, Now, c.Deadline);
                    bool keep = false, failure = false;
                    int failO = -1;   // the failure's outcome index (0 gone, 1 retargeted, 2 expired)
                    switch (o)
                    {
                        case SkiDayTiming.ClaimOutcome.OnTheWay:
                            keep = true;
                            ro.OnTheWay++;
                            if ((c.Flags & ClaimFlag.Departed) == 0)
                                ro.NotLeft++;
                            if (riding)
                                ro.Riding++;
                            if (atDoor)
                                ro.AtDoor++;
                            c.Flags2 = SkiDayTiming.ShiftNeed(c.Flags2, NeedOpenNow(c.Citizen));   // need register
                            break;
                        case SkiDayTiming.ClaimOutcome.Arrived:
                            keep = true;
                            c.State = 1;
                            ro.OnSite++;
                            BookArrival(ref c, ref ro, ti, mb, hb, cls);
                            c.Flags2 = SkiDayTiming.ShiftNeed(c.Flags2, NeedOpenNow(c.Citizen));   // need register
                            break;
                        case SkiDayTiming.ClaimOutcome.OnSite:
                            keep = true;
                            ro.OnSite++;
                            c.Flags2 = SkiDayTiming.ShiftNeed(c.Flags2, NeedOpenNow(c.Citizen));   // need register
                            break;
                        case SkiDayTiming.ClaimOutcome.Left:
                        {
                            ro.Left++;   // a finished day, not an outcome
                            // Why the visit ended; then the exit at once (a guest already outside) or state 2; a guest already outside for home, the map
                            // exit or with no trip purpose is followed (state 4).
                            byte x = VisitEnd(ref c, ref ro, ref started, mb, hb, cls, kind, gone, hasL, leisureAtLodge, at, inBuilding,
                                              building, atOrigin, hasTp, purpose, false);
                            if (x == 0)
                            {
                                c.State = 2;
                                keep = true;
                                ro.Leaving++;   // counted in its new bucket this pass
                            }
                            else if (x == 2)
                            {
                                keep = true;
                                ro.Following++;
                            }
                            break;
                        }
                        case SkiDayTiming.ClaimOutcome.Gone:
                            if (SkiDayTiming.ArrivedThenRemoved(o, gone, at))
                            {
                                // In the lodge with the outing gone within one pass: an arrival (learning included) whose visit ended at once. Not a failure:
                                // no ro.Gone, no RecordOutcome(false), no LodgeCarFails; not on the hill: no ro.OnSite, no ActiveNow. No GoneWhy: the "why" is
                                // RemoveWhyNow's in VisitEnd. ArrivedNoLeisure counts only these (!gone) cases: a deleted citizen in the lodge stays a
                                // failure, place "deleted".
                                ro.ArrivedNoLeisure++;
                                ro.Left++;   // a finished day, as the Left case
                                c.State = 1;
                                BookArrival(ref c, ref ro, ti, mb, hb, cls);
                                byte x = VisitEnd(ref c, ref ro, ref started, mb, hb, cls, kind, gone, false, false, at, inBuilding,
                                                  building, atOrigin, hasTp, purpose, true);
                                if (x == 0)
                                {
                                    c.State = 2;   // the normal exit watch follows the guest out
                                    keep = true;
                                    ro.Leaving++;
                                }
                                else if (x == 2)
                                {
                                    keep = true;
                                    ro.Following++;
                                }
                                break;
                            }
                            failure = true;
                            failO = 0;
                            ro.Gone++;
                            Failure(ref c, mb, hb, cls, kind, 0, gone, inBuilding, atOrigin, at, hasTp, purpose);
                            break;
                        case SkiDayTiming.ClaimOutcome.Retargeted:
                            failure = true;
                            failO = 1;
                            ro.Retargeted++;
                            if (hasCb && cb.m_CurrentBuilding == c.Origin)
                                ro.RetargetedAtOrigin++;
                            Failure(ref c, mb, hb, cls, kind, 1, gone, inBuilding, atOrigin, at, hasTp, purpose);
                            break;
                        default:   // Expired
                            failure = true;
                            failO = 2;
                            ro.Expired++;
                            Failure(ref c, mb, hb, cls, kind, 2, gone, inBuilding, atOrigin, at, hasTp, purpose);
                            c.State = 3;   // watched for a late arrival (measure only; the ring record is written at its end)
                            keep = true;
                            ro.Watching++;   // counted in its new bucket this pass
                            break;
                    }

                    // The deadline pull, the job's one write to vanilla state. A guest on site, or arriving this pass (an on-the-way guest is
                    // pulled at the pass it arrives), at a hill that closed early leaves at the new closing, at least OnSiteGraceFrames from now:
                    // the field ClaimJob wrote at the claim (LeisureSystem.cs:398-405, 1.6.2 :392-399), lowered only (PullDeadline), and only while
                    // our value is still in place (vanilla writes m_LastPossibleFrame only at Leisure creation, CitizenBehaviorSystem.cs:494,
                    // 1.6.2 :493). LeisureSystem then ends the visit at the new deadline (LeisureSystem.cs:314-317, 1.6.2 :308-311) and Left
                    // reads "lifts closing" (DeadlineIsClosing set). c.Deadline moves with the field, so c.Deadline == m_LastPossibleFrame
                    // holds. Accepted: BookArrival ran before the pull, so the hours left at an arrival pulled in the same pass are recorded
                    // against the old deadline. A write lost for any reason is retried next pass (idempotent).
                    if ((o == SkiDayTiming.ClaimOutcome.OnSite || o == SkiDayTiming.ClaimOutcome.Arrived) && a >= 0 && a < Areas.Length &&
                        hasL && l.m_TargetAgent == c.Lodge && at && l.m_LastPossibleFrame == c.Deadline)
                    {
                        AreaJob aj = Areas[a];
                        if (SkiDayTiming.PullDeadline(aj.ScoreKnown != 0, aj.WriteHeld != 0, aj.RunsCounted, aj.NightRunOpen != 0,
                                                      (c.Flags & ClaimFlag.DeadlineIsClosing) != 0, Now, TimeOfDay, c.Deadline,
                                                      out uint pulled, out byte why))
                        {
                            l.m_LastPossibleFrame = pulled;
                            Leisures[c.Citizen] = l;
                            c.Deadline = pulled;
                            c.Flags |= ClaimFlag.DeadlineIsClosing;
                            c.Flags2 |= ClaimFlag2.DeadlinePulled;
                            ro.Pulled++;
                            ro.PulledTo = pulled;
                            ro.PullReason = why;
                        }
                    }
                    Out[r] = ro;

                    if (failure)
                    {
                        // A claim whose Leisure lost the lodge without arriving, or whose deadline passed. A Gone or Retargeted failure may also bound
                        // the all-outcomes speed, here inside the failure block only, so an arrival booked on removal never learns twice; an expiry is
                        // booked when its late watch ends (LearnAtLateEnd).
                        if (ti >= 0)
                        {
                            SkiDayRules.ClassTrack t = Tracks[ti];
                            SkiDayRules.RecordOutcome(ref t, false);
                            if (failO == 0 || failO == 1)
                            {
                                int k = SpeedKind.NoInformation;
                                SkiDayTiming.SpeedEvidence ev = SkiDayTiming.FailureEvidence(failO, (c.Flags & ClaimFlag.Departed) != 0,
                                                                                             (SkiDayTiming.Place)c.Place,
                                                                                             (c.Flags2 & ClaimFlag2.TurnedBack) != 0);
                                if (ev == SkiDayTiming.SpeedEvidence.Bound)
                                    k = BoundKind(SkiDayRules.LearnSpeedBound(ref t, (SkiDayRules.TravelClass)c.Class, c.DistanceM, c.ClaimFrame,
                                                                              Now, TripKnobs));
                                MeasureWrite.Bump(Measure, mb + RM.SpeedLearn + cls * SpeedKind.Count + k);
                            }
                            Tracks[ti] = t;
                        }
                        if (c.Class == (byte)SkiDayRules.TravelClass.Car && LodgeIndex.TryGetValue(c.Lodge, out int li) &&
                            li >= 0 && li < LodgeCarFails.Length)
                            LodgeCarFails[li]++;
                    }
                    if (keep)
                        Claims[w++] = c;
                }
                Claims.ResizeUninitialized(w);

                int areas = math.min(ActiveNow.Length, orphan);
                for (int a = 0; a < areas; a++)
                    ActiveNow[a] = Out[a].OnTheWay + Out[a].OnSite;
            }

            /// <summary>The per-pass sample of a claim on the way (state 0) or watched after its expiry (state 3); never for a gone
            /// citizen. Returns whether the guest sits in a vehicle this pass.
            /// 1 car reserve (queued car claims only); 2 turned back (a non-Leisure TravelPurpose away from the origin); 3 departure
            /// (SkiDayTiming.DepartedNow); 4 walker and vehicle, read ONLY while the citizen is in no building (after an arrival the
            /// creature can live on and be reused for the next trip, 1.6.2 ResidentAISystem.cs:2287-2309); the ride (CurrentVehicle with
            /// Ready); the closest approach to the lodge. The walker's Divert{PathFailed} is the door wait: <paramref name="atDoor"/> = the
            /// mark this pass (false on every early return); the first pass a live walker carries no mark sets MoveFrame
            /// (SkiDayTiming.MovedOffNow). A move seen during the late watch (state 3) is kept for the history only, like DepartFrame.</summary>
            private bool Sample(ref SkiDayClaim c, int mb, int hb, int cls, bool inBuilding, bool atOrigin, bool at, bool hasTp, byte purpose,
                                out bool atDoor)
            {
                atDoor = false;
                if (c.Class == (byte)SkiDayRules.TravelClass.Car && c.CarEnqueued == 1 && (c.Flags & ClaimFlag.HeldEver) == 0 &&
                    CarKeepers.TryGetEnabledComponent(c.Citizen, out CarKeeper k) && k.m_Car != Entity.Null)
                    c.Flags |= ClaimFlag.HeldEver;
                if (!atOrigin && hasTp && purpose != SkiDayTiming.PurposeLeisure && c.TurnPurpose == 0)
                {
                    c.Flags2 |= ClaimFlag2.TurnedBack;
                    c.TurnPurpose = purpose;
                }
                if ((c.Flags & ClaimFlag.Departed) == 0 && SkiDayTiming.DepartedNow(inBuilding, atOrigin, at, hasTp, purpose))
                {
                    c.Flags |= ClaimFlag.Departed;
                    c.DepartFrame = Now;
                    if (c.State == 0)   // a departure seen during the late watch is kept for the history only
                        PhaseHours(mb, hb, SkiDayTiming.Phase.ClaimToLeaveAll, cls, Now, c.ClaimFrame);
                }
                if (inBuilding)
                    return false;

                Entity walker = CurrentTransports.TryGetComponent(c.Citizen, out Game.Citizens.CurrentTransport ct) ? ct.m_CurrentTransport : Entity.Null;
                if (walker == Entity.Null || !Transforms.HasComponent(walker) || Deleteds.HasComponent(walker))
                {
                    c.Flags2 |= ClaimFlag2.WalkerLost;
                    return false;
                }
                bool mark = Diverts.TryGetComponent(walker, out Game.Creatures.Divert divert) &&
                            divert.m_Purpose == Game.Citizens.Purpose.PathFailed;   // TripNeededSystem.cs:681-686 (=)
                if (mark)
                    c.Flags |= ClaimFlag.PathFailedAtStart;
                atDoor = mark;   // standing at the door this pass
                if (c.MoveFrame == 0u && SkiDayTiming.MovedOffNow((c.Flags & ClaimFlag.Departed) != 0, false, true, mark))
                    c.MoveFrame = Now == 0u ? 1u : Now;   // 0 means "not seen"
                if (PathOwners.TryGetComponent(walker, out Game.Pathfind.PathOwner walkerPath) && PathBad(walkerPath))
                    c.Flags2 |= ClaimFlag2.WalkerPathFailed;

                Entity seen = walker;
                bool riding = false;
                if (CurrentVehicles.TryGetComponent(walker, out Game.Creatures.CurrentVehicle cv) && cv.m_Vehicle != Entity.Null &&
                    (cv.m_Flags & Game.Creatures.CreatureVehicleFlags.Ready) != 0)
                {
                    riding = true;
                    Entity v = cv.m_Vehicle;
                    Entity lead = Controllers.TryGetComponent(v, out Game.Vehicles.Controller co) && co.m_Controller != Entity.Null ? co.m_Controller : v;
                    seen = lead;
                    if ((c.Flags & ClaimFlag.Rode) == 0)
                    {
                        c.Flags |= ClaimFlag.Rode;
                        c.RideFirstFrame = Now;
                    }
                    c.RideLastFrame = Now;
                    if (c.RidePasses < ushort.MaxValue)
                        c.RidePasses++;
                    bool transit = PublicTransports.TryGetComponent(lead, out Game.Vehicles.PublicTransport pt) &&
                                   (pt.m_State & (Game.Vehicles.PublicTransportFlags.Evacuating | Game.Vehicles.PublicTransportFlags.PrisonerTransport)) == 0;
                    byte leg = SkiDayRules.LegOf(PersonalCars.HasComponent(lead), Bicycles.HasComponent(lead), Taxis.HasComponent(lead), transit);
                    c.Legs |= leg;
                    if (leg == SkiDayRules.LegTransit)
                        c.Kinds |= SkiDayRules.KindBitOf(TransportTypeOf(v, lead));
                    if (PathOwners.TryGetComponent(lead, out Game.Pathfind.PathOwner vehiclePath) && PathBad(vehiclePath))
                        c.Flags2 |= ClaimFlag2.VehiclePathFailed;   // PersonalCarAISystem.cs:297-300 (1.6.2)
                }
                if (math.all(math.isfinite(c.LodgeXz)) && Transforms.TryGetComponent(seen, out Game.Objects.Transform t))
                {
                    float d = math.distance(t.m_Position.xz, c.LodgeXz);
                    c.ClosestM = SkiDayRules.NearestOf(c.ClosestM, d);   // NaN never wins
                    if (d < 500f && c.Near500Frame == 0u)
                        c.Near500Frame = Now == 0u ? 1u : Now;
                }
                return riding;
            }

            /// <summary>Failed or Stuck (1.6.2 CreatureUtils.cs:68-71, VehicleUtils.cs:129-132).</summary>
            private static bool PathBad(Game.Pathfind.PathOwner po)
            {
                return (po.m_State & (Game.Pathfind.PathFlags.Failed | Game.Pathfind.PathFlags.Stuck)) != 0;
            }

            /// <summary>Transit kind: the boarded car's prefab TransportType, else the Controller's prefab (a modded
            /// transit car without the data), else −1 (Other).</summary>
            private int TransportTypeOf(Entity vehicle, Entity lead)
            {
                if (PrefabRefs.TryGetComponent(vehicle, out Game.Prefabs.PrefabRef pr) &&
                    TransportVehicleDatas.TryGetComponent(pr.m_Prefab, out Game.Prefabs.PublicTransportVehicleData d))
                    return (int)d.m_TransportType;
                if (lead != vehicle && PrefabRefs.TryGetComponent(lead, out Game.Prefabs.PrefabRef lp) &&
                    TransportVehicleDatas.TryGetComponent(lp.m_Prefab, out Game.Prefabs.PublicTransportVehicleData d2))
                    return (int)d2.m_TransportType;
                return -1;
            }

            /// <summary>The arrival booking shared by the Arrived case and the on-arrival branch: ArrivalFrame = Now; the per-class arrival
            /// counter and claim → arrival hours; tourists; the first-arrival fields; with a track, RecordOutcome(true), the arrival-only
            /// speed (SkiDayRules.LearnSpeed with TripKnobs) and the all-outcomes speed (LearnSpeedAll; RM.SpeedLearn kind Arrival, or
            /// NoInformation when a guard skipped it); then the arrival measures (Arrival). A 0-stay arrival is a fair speed sample: the
            /// stay is not an input.</summary>
            private void BookArrival(ref SkiDayClaim c, ref ResolveOut ro, int ti, int mb, int hb, int cls)
            {
                c.ArrivalFrame = Now;
                double hours = SkiDayTiming.HoursFor(Now - c.ClaimFrame);
                if (c.Class == (byte)SkiDayRules.TravelClass.Walk)
                {
                    ro.ArrivedWalk++;
                    ro.HoursWalk += hours;
                }
                else if (c.Class == (byte)SkiDayRules.TravelClass.Car)
                {
                    ro.ArrivedCar++;
                    ro.HoursCar += hours;
                }
                else
                {
                    ro.ArrivedNoCar++;
                    ro.HoursNoCar += hours;
                }
                if (c.Kind == (byte)SkiDayRules.GuestKind.Tourist)
                    ro.ArrivedTourists++;
                if ((c.Flags & ClaimFlag.ToDepot) != 0)
                    ro.ArrivedDepot++;
                if (ro.HasFirst == 0)
                {
                    ro.HasFirst = 1;
                    ro.FirstClass = c.Class;
                    ro.FirstKind = c.Kind;
                    ro.FirstHours = hours;
                }
                if (ti >= 0)
                {
                    SkiDayRules.ClassTrack t = Tracks[ti];
                    SkiDayRules.RecordOutcome(ref t, true);
                    SkiDayRules.TravelClass tc = (SkiDayRules.TravelClass)c.Class;
                    SkiDayRules.LearnSpeed(ref t, tc, c.DistanceM, c.ClaimFrame, Now, TripKnobs);
                    bool all = SkiDayRules.LearnSpeedAll(ref t, tc, c.DistanceM, c.ClaimFrame, Now, TripKnobs);
                    MeasureWrite.Bump(Measure, mb + RM.SpeedLearn + cls * SpeedKind.Count + (all ? SpeedKind.Arrival : SpeedKind.NoInformation));
                    Tracks[ti] = t;
                }
                Arrival(ref c, mb, hb, cls);   // measure only
            }

            /// <summary>The end of a late watch (state 3, ti ≥ 0): SkiDayTiming.LateEvidence with the place at this pass
            /// (<paramref name="placeNow"/>, PlaceOf at the late end; not the claim's Place kept from the expiry). Exact (a late arrival,
            /// now − claim) → LearnSpeedAll (kind LateArrival, or NoInformation when a guard skipped it); Bound → LearnSpeedBound to this
            /// pass (BoundKind); None → NoInformation. Confidence untouched (the expiry already booked RecordOutcome(false)).</summary>
            private void LearnAtLateEnd(int ti, int mb, int cls, in SkiDayClaim c, SkiDayTiming.LateResult late, SkiDayTiming.Place placeNow)
            {
                SkiDayRules.ClassTrack t = Tracks[ti];
                SkiDayRules.TravelClass tc = (SkiDayRules.TravelClass)c.Class;
                int k = SpeedKind.NoInformation;
                SkiDayTiming.SpeedEvidence ev = SkiDayTiming.LateEvidence(late, (c.Flags & ClaimFlag.Departed) != 0, placeNow,
                                                                         (c.Flags2 & ClaimFlag2.TurnedBack) != 0);
                if (ev == SkiDayTiming.SpeedEvidence.Exact)
                    k = SkiDayRules.LearnSpeedAll(ref t, tc, c.DistanceM, c.ClaimFrame, Now, TripKnobs) ? SpeedKind.LateArrival : SpeedKind.NoInformation;
                else if (ev == SkiDayTiming.SpeedEvidence.Bound)
                    k = BoundKind(SkiDayRules.LearnSpeedBound(ref t, tc, c.DistanceM, c.ClaimFrame, Now, TripKnobs));
                MeasureWrite.Bump(Measure, mb + RM.SpeedLearn + cls * SpeedKind.Count + k);
                Tracks[ti] = t;
            }

            /// <summary>The RM.SpeedLearn kind of an upper-bound update (Lowered 2, AlreadyMet 3, NoInformation 4).</summary>
            private static int BoundKind(SkiDayRules.BoundLearn b)
            {
                switch (b)
                {
                    case SkiDayRules.BoundLearn.Lowered: return SpeedKind.BoundLowered;
                    case SkiDayRules.BoundLearn.AlreadyMet: return SpeedKind.BoundMet;
                    default: return SpeedKind.NoInformation;
                }
            }

            /// <summary>Measures at an arrival (after the existing counters and the learning): the departure if the sample missed it (a
            /// trip shorter than a pass), the trip phases, the hours left before the deadline, the arrival clock, the markers, the mode seen
            /// and transit kinds, the distance band, the car reserve, and a resident note for the ledger check. A departure booked at this
            /// arrival (a trip shorter than a pass) sets MoveFrame = Now and adds a 0.0 h door sample; else a walker never seen moving off
            /// sets MoveFrame = DepartFrame and counts DoorUnseen; else the door hours (DepartFrame → MoveFrame) are sampled. So door
            /// samples (group 0) + DoorUnseen = arrivals. LeaveToRide ("door to the vehicle") runs from MoveFrame.</summary>
            private void Arrival(ref SkiDayClaim c, int mb, int hb, int cls)
            {
                if ((c.Flags & ClaimFlag.Departed) == 0)
                {
                    c.Flags |= ClaimFlag.Departed;
                    c.DepartFrame = Now;
                    PhaseHours(mb, hb, SkiDayTiming.Phase.ClaimToLeaveAll, cls, Now, c.ClaimFrame);
                    c.MoveFrame = Now;   // no door wait seen
                    MeasureWrite.AddHours(Hours, Measure, mb + RM.DoorN + cls, hb + RH.Door + cls, 0.0);
                }
                else if (c.MoveFrame == 0u)
                {
                    c.MoveFrame = c.DepartFrame;   // never seen moving off
                    MeasureWrite.Bump(Measure, mb + RM.DoorUnseen + cls);
                }
                else
                {
                    MeasureWrite.AddHours(Hours, Measure, mb + RM.DoorN + cls, hb + RH.Door + cls, HoursBetween(c.MoveFrame, c.DepartFrame));
                }
                PhaseHours(mb, hb, SkiDayTiming.Phase.ClaimToLeaveArrived, cls, c.DepartFrame, c.ClaimFrame);
                PhaseHours(mb, hb, SkiDayTiming.Phase.LeaveToArrive, cls, Now, c.DepartFrame);
                if ((c.Flags & ClaimFlag.Rode) != 0)
                {
                    PhaseHours(mb, hb, SkiDayTiming.Phase.LeaveToRide, cls, c.RideFirstFrame, c.MoveFrame);   // door to the vehicle
                    PhaseHours(mb, hb, SkiDayTiming.Phase.RideSpan, cls, c.RideLastFrame, c.RideFirstFrame);
                    PhaseHours(mb, hb, SkiDayTiming.Phase.RideToArrive, cls, Now, c.RideLastFrame);
                    int seen = SkiDayTiming.Phase.RideSeen * 3 + cls;
                    MeasureWrite.AddHours(Hours, Measure, mb + RM.PhaseN + seen, hb + seen, SkiDayTiming.HoursFor((uint)c.RidePasses * PassInterval));
                }
                PhaseHours(mb, hb, SkiDayTiming.Phase.HoursLeftAtArrival, cls, c.Deadline, Now);   // 0 when past
                MeasureWrite.Bump(Measure, mb + RM.ArrivalClock + SkiDayTiming.ClockBin(TimeOfDay));
                Markers(mb + RM.ArrivedMarker, cls, c);
                MeasureWrite.Bump(Measure, mb + RM.ModeArrived + cls * SkiDayRules.ArrivalModes + (int)SkiDayRules.MainMode(c.Legs));
                for (int bit = 0; bit < SkiDayRules.KindBits; bit++)
                {
                    if ((c.Kinds & (1 << bit)) != 0)
                        MeasureWrite.Bump(Measure, mb + RM.TransitKind + bit);
                }
                MeasureWrite.Bump(Measure, mb + RM.ArrivedBand + cls * SkiDayRules.DistanceBands + SkiDayRules.DistanceBand(c.DistanceM));
                CarReserveAtEnd(c, mb);
                if (c.Kind == (byte)SkiDayRules.GuestKind.Resident)
                    Notes.Add(new ResidentNote { Citizen = c.Citizen, ArrivalFrame = Now, Kind = 0, Verdict = 255 });
            }

            /// <summary>Measures at the Left verdict (ro.Left is counted by the caller): why the visit ended (EndCauseOf), the stay (mean per
            /// class and per cause, histogram), the guest-scan verdict (the counter's "there" test is ours,
            /// SkiGuestCounterSystem.cs:1237-1255) and a resident note; then the exit check at once. Returns Exit's byte: 0 keep watching
            /// (state 2), 1 recorded (drop the claim), 2 now followed (state 4, keep). <paramref name="onArrival"/> forces
            /// EndCause.RemovedOnArrival (stay 0.0 h; its scan verdict is Missed or Unsure, never Seen: the arrival frame lies after the
            /// last-there frame). The game's clock checks run for every cause (LiveChecks → EndLive, RM.LiveEnd / LiveUnknown); for need
            /// met, removed and removed on arrival, why the Leisure went (RemoveWhyNow → RM.RemoveWhy, RH.RemoveStay per why, RM.RemoveFast
            /// for stays under FastStayHours).</summary>
            private byte VisitEnd(ref SkiDayClaim c, ref ResolveOut ro, ref int started, int mb, int hb, int cls, int kind, bool gone,
                                  bool hasL, bool leisureAtLodge, bool at, bool inBuilding, Entity building, bool atOrigin,
                                  bool hasTp, byte purpose, bool onArrival)
            {
                c.EndFrame = Now;
                bool hasZ = Citizens.TryGetComponent(c.Citizen, out Citizen z);
                int counter = hasZ ? z.m_LeisureCounter : 0;
                SkiDayTiming.EndCause cause = onArrival
                    ? SkiDayTiming.EndCause.RemovedOnArrival
                    : SkiDayTiming.EndCauseOf(gone, hasL, leisureAtLodge, at, Now, c.Deadline, (c.Flags & ClaimFlag.DeadlineIsClosing) != 0, counter);
                c.EndCause = (byte)cause;
                double stay = HoursBetween(Now, c.ArrivalFrame);
                MeasureWrite.AddHours(Hours, Measure, mb + RM.EndCause + (int)cause, hb + RH.EndStay + (int)cause, stay);
                int st = SkiDayTiming.Phase.Stay * 3 + cls;
                MeasureWrite.AddHours(Hours, Measure, mb + RM.PhaseN + st, hb + st, stay);
                MeasureWrite.Bump(Measure, mb + RM.StayBin + cls * 6 + SkiDayTiming.StayBin(stay));

                // The game's own work-time / bedtime checks at this frame, for every cause.
                byte live = LiveChecks(c.Citizen, hasZ, z, out bool workNow, out bool bedNow);
                c.EndLive = live;
                if ((live & EndLiveNotChecked) != 0)
                    MeasureWrite.Bump(Measure, mb + RM.LiveUnknown);
                else
                {
                    if (workNow)
                        MeasureWrite.Bump(Measure, mb + RM.LiveEnd + (int)cause * 2);
                    if (bedNow)
                        MeasureWrite.Bump(Measure, mb + RM.LiveEnd + (int)cause * 2 + 1);
                }
                // Why the Leisure went before the deadline (the three causes where it did).
                c.RemoveWhy = 255;
                c.MoveReason = 255;
                if (cause == SkiDayTiming.EndCause.NeedFilled || cause == SkiDayTiming.EndCause.Removed ||
                    cause == SkiDayTiming.EndCause.RemovedOnArrival)
                {
                    SkiDayTiming.RemoveWhy why = RemoveWhyNow(ref c, hasZ, z, at, hasTp, purpose, counter, workNow, bedNow);
                    c.RemoveWhy = (byte)why;
                    int k = kind * SkiDayTiming.RemoveWhyCount + (int)why;
                    MeasureWrite.AddHours(Hours, Measure, mb + RM.RemoveWhy + k, hb + RH.RemoveStay + (int)why, stay);
                    if (SkiDayTiming.IsFastStay(stay))
                        MeasureWrite.Bump(Measure, mb + RM.RemoveFast + k);
                }

                uint lastThere = cause == SkiDayTiming.EndCause.PastGrace ? Now : Now - PassInterval;
                SkiDayTiming.ScanVerdict v = SkiDayTiming.GuestScanSaw(c.ArrivalFrame, lastThere, Now, PassInterval, ScanKnown != 0, ScanFrame, ScanInterval);
                MeasureWrite.Bump(Measure, mb + RM.Scan + kind * SkiDayTiming.ScanVerdictCount + (int)v);
                if (c.Kind == (byte)SkiDayRules.GuestKind.Resident)
                    Notes.Add(new ResidentNote { Citizen = c.Citizen, ArrivalFrame = c.ArrivalFrame, Kind = 1, Verdict = (byte)v });
                return Exit(ref c, ref ro, ref started, mb, hb, cls, gone, at, inBuilding, building, atOrigin, hasTp, purpose);
            }

            /// <summary>SkiDayClaim.EndLive bits.</summary>
            private const byte EndLiveWork = 1, EndLiveBed = 2, EndLiveNotChecked = 4;

            /// <summary>CitizenBehaviorSystem's own clock tests at this frame — work or school time: the test at
            /// CitizenBehaviorSystem.cs:743 (1.6.2 :771) with WorkerSystem.IsTodayOffDay / IsTimeToWork (WorkerSystem.cs
            /// :350, :361 (=)) and StudentSystem.IsTimeToStudy (StudentSystem.cs:316 (=)); bedtime:
            /// CitizenBehaviorSystem.IsSleepTime, called with its 1.6.2 signature (:1108; 1.6.0 :1076 takes ComponentLookups:
            /// we build against the installed game). The game calls these statics from its own Burst jobs. CBS's bedtime
            /// branch also needs a home (1.6.2 :331); claimed guests always have one (claims only at home or hotel), so it is
            /// not re-tested. Returns the EndLive bits; EndLiveNotChecked when a singleton is missing (LiveKnown 0) or the
            /// citizen is gone.</summary>
            private byte LiveChecks(Entity citizen, bool hasCitizen, Citizen z, out bool work, out bool bed)
            {
                work = false;
                bed = false;
                if (LiveKnown == 0 || !hasCitizen)
                    return EndLiveNotChecked;
                int population = Populations.TryGetComponent(PopulationEntity, out Game.City.Population p) ? p.m_Population : 0;
                bool isWorker = Workers.TryGetComponent(citizen, out Worker wk);
                bool isStudent = Students.TryGetComponent(citizen, out Game.Citizens.Student st);
                work = (isWorker && !WorkerSystem.IsTodayOffDay(z, ref Economy, Now, TimeData, population) &&
                        WorkerSystem.IsTimeToWork(z, wk, ref Economy, TimeOfDay)) ||
                       (isStudent && StudentSystem.IsTimeToStudy(z, st, ref Economy, TimeOfDay, Now, TimeData, population));
                bed = CitizenBehaviorSystem.IsSleepTime(citizen, z, ref Economy, TimeOfDay, isWorker, wk, isStudent, st);
                return (byte)((work ? EndLiveWork : 0) | (bed ? EndLiveBed : 0));
            }

            /// <summary>The game's traces at a visit end where the Leisure went before the deadline, read in
            /// CitizenBehaviorSystem's order by SkiDayTiming.RemoveWhyOf: the household's MovingAway and its reason (stored in
            /// c.MoveReason; the tourist hotel money check is TouristNoMoney, TouristLeaveSystem.cs:68 (=)), HealthProblem,
            /// the clock checks, the first non-Leisure trip (FirstOtherTrip), ResourceBuyer, the household need taken since
            /// the last two passes by an adult or elderly guest (CitizenBehaviorSystem 1.6.0 :759, 1.6.2 :788; the register
            /// in Flags2), the lodge switched off (LeisureSystem.cs:291, 1.6.2 :285), the counter and the place.</summary>
            private SkiDayTiming.RemoveWhy RemoveWhyNow(ref SkiDayClaim c, bool hasZ, Citizen z, bool at, bool hasTp, byte purpose,
                                                        int counter, bool workNow, bool bedNow)
            {
                bool hasHm = HouseholdMembers.TryGetComponent(c.Citizen, out HouseholdMember hm);
                MovingAway ma = default;
                bool moving = hasHm && MovingAways.TryGetComponent(hm.m_Household, out ma);
                c.MoveReason = moving ? (byte)ma.m_Reason : (byte)255;
                bool money = moving && ma.m_Reason == MoveAwayReason.TouristNoMoney;
                bool hasTrip = FirstOtherTrip(c.Citizen, out byte trip);
                CitizenAge age = hasZ ? z.GetAge() : CitizenAge.Child;
                bool adult = age == CitizenAge.Adult || age == CitizenAge.Elderly;
                bool openNow = hasHm && HouseholdNeeds.TryGetComponent(hm.m_Household, out HouseholdNeed n) && n.m_Resource != Resource.NoResource;
                bool needTaken = adult && SkiDayTiming.NeedTaken(c.Flags2, openNow);
                bool venueOff = Buildings.TryGetComponent(c.Lodge, out Building b) && BuildingUtils.CheckOption(b, BuildingOption.Inactive);
                return SkiDayTiming.RemoveWhyOf(moving, money, HealthProblems.HasComponent(c.Citizen), workNow, bedNow, hasTp, purpose,
                                                hasTrip, trip, ResourceBuyers.HasComponent(c.Citizen), needTaken, venueOff, counter, at);
            }

            /// <summary>Is the citizen's household need open now (HouseholdNeed.m_Resource set)? The register
            /// input (SkiDayTiming.ShiftNeed).</summary>
            private bool NeedOpenNow(Entity citizen)
            {
                return HouseholdMembers.TryGetComponent(citizen, out HouseholdMember hm) &&
                       HouseholdNeeds.TryGetComponent(hm.m_Household, out HouseholdNeed n) && n.m_Resource != Resource.NoResource;
            }

            /// <summary>The first trip that is not Leisure: a claimed citizen who never left still holds our own Leisure trip at index 0, so
            /// a work, shopping or home trip queued after it would be missed. Shared by Failure and RemoveWhyNow. False when the citizen has
            /// no trip.</summary>
            private bool FirstOtherTrip(Entity citizen, out byte trip)
            {
                trip = 0;
                if (!TripNeededs.TryGetBuffer(citizen, out DynamicBuffer<TripNeeded> trips) || trips.Length == 0)
                    return false;
                trip = (byte)trips[0].m_Purpose;
                for (int t = 1; trip == SkiDayTiming.PurposeLeisure && t < trips.Length; t++)
                    trip = (byte)trips[t].m_Purpose;
                return true;
            }

            /// <summary>Exit (state 2, and once at the visit end): out of the lodge (the TravelPurpose of the trip out), deleted, or still
            /// inside after ExitWatchFrames → Exit[endCause × 11 + way], the hours from visit end to exit, and the ring record. Returns a
            /// byte: 0 keep watching (state 2), 1 recorded and dropped, 2 now followed (state 4, keep). A no-purpose exit books where the
            /// guest stands (NoPurposeAtOf, "placed" = m_PenaltyCounter ≥ PlacedMarkMin) into c.ExitAt and RM.NoPurposeAt. An exit for home,
            /// with no trip purpose or moving away is followed while FollowingLast + started &lt; FollowCap (the first sample in this pass;
            /// the ring record is written when the follow ends); at the cap it is recorded at once as "not followed" (RM.FollowSkipped,
            /// ro.FollowSkipped); other exits are recorded at once.</summary>
            private byte Exit(ref SkiDayClaim c, ref ResolveOut ro, ref int started, int mb, int hb, int cls, bool gone, bool at,
                              bool inBuilding, Entity building, bool atOrigin, bool hasTp, byte purpose)
            {
                if (!SkiDayTiming.TryExit(gone, at, hasTp, purpose, Now, c.EndFrame, out SkiDayTiming.ExitWay way))
                    return 0;
                int cause = math.min((int)c.EndCause, SkiDayTiming.EndCauseCount - 1);
                MeasureWrite.Bump(Measure, mb + RM.Exit + cause * SkiDayTiming.ExitWayCount + (int)way);
                if (way != SkiDayTiming.ExitWay.StillInside && way != SkiDayTiming.ExitWay.Deleted)
                    PhaseHours(mb, hb, SkiDayTiming.Phase.EndToExit, cls, Now, c.EndFrame);
                if (way == SkiDayTiming.ExitWay.NoPurpose)
                {
                    int penalty = Citizens.TryGetComponent(c.Citizen, out Citizen z) ? z.m_PenaltyCounter : 0;
                    SkiDayTiming.NoPurposeAt np = SkiDayTiming.NoPurposeAtOf(inBuilding, inBuilding && building == c.Origin,
                                                                             inBuilding && OutsideConnections.HasComponent(building), penalty);
                    c.ExitAt = (byte)np;
                    MeasureWrite.Bump(Measure, mb + RM.NoPurposeAt + (int)np);
                }
                c.Exit = (byte)way;
                c.ExitFrame = Now;
                if (SkiDayTiming.FollowExitKind(c.Exit) >= 0)
                {
                    if (FollowingLast + started < FollowCap)
                    {
                        c.State = 4;
                        c.FollowFlags = SkiDayTiming.FollowFlagFollowed;
                        started++;
                        return Follow(ref c, mb, hb, true, gone, inBuilding, building, at, atOrigin) ? (byte)1 : (byte)2;
                    }
                    c.FollowFlags = SkiDayTiming.FollowFlagNotFollowedCap;
                    MeasureWrite.Bump(Measure, mb + RM.FollowSkipped);
                    ro.FollowSkipped++;
                }
                Record(c, 0, 255, c.Exit, Now, 255);
                return 1;
            }

            /// <summary>One follow sample of a guest who left the lodge for home, the map exit or with no trip purpose (state 4;
            /// <paramref name="first"/> at the exit pass). The walker read (FollowWalker) while the citizen is in no building; then
            /// SkiDayTiming.TryFollow (home = the origin, home or hotel; edge = an outside connection; "placed" per NoPurposeAtOf's rule).
            /// False: keep following. True: book the result once (RM.FollowResult, placed / at once (RM.FollowAtOnce), home not placed
            /// (hours RH.FollowHomeH, mode RM.FollowHomeMode), still out (RM.FollowStillOut), the stop waits (RH.FollowWaitH /
            /// FollowWaitNearH), a failed path seen), then the ring record with the exit frame; the caller drops the claim.</summary>
            private bool Follow(ref SkiDayClaim c, int mb, int hb, bool first, bool gone, bool inBuilding, Entity building, bool at, bool atOrigin)
            {
                byte penalty = Citizens.TryGetComponent(c.Citizen, out Citizen z) ? z.m_PenaltyCounter : (byte)0;
                bool atEdge = inBuilding && OutsideConnections.HasComponent(building);
                if (!gone && !inBuilding)
                    FollowWalker(ref c);
                if (!SkiDayTiming.TryFollow(gone, inBuilding, atOrigin, atEdge, at, first, penalty, Now, c.ExitFrame,
                                            out SkiDayTiming.FollowResult res, out bool placed))
                    return false;
                int kind = SkiDayTiming.FollowExitKind(c.Exit);
                int slot = SkiDayTiming.FollowSlot(kind, (int)res, placed);
                if (slot >= 0)
                    MeasureWrite.Bump(Measure, mb + RM.FollowResult + slot);
                if (placed)
                {
                    c.FollowFlags |= SkiDayTiming.FollowFlagPlaced;
                    if (first)
                    {
                        c.FollowFlags |= SkiDayTiming.FollowFlagAtOnce;
                        int once = SkiDayTiming.AtOnceSlot(kind, (int)res);
                        if (once >= 0)
                            MeasureWrite.Bump(Measure, mb + RM.FollowAtOnce + once);
                    }
                }
                if (res == SkiDayTiming.FollowResult.Home && !placed)
                {
                    MeasureWrite.AddHours(Hours, Measure, mb + RM.FollowHomeN, hb + RH.FollowHomeH, HoursBetween(Now, c.ExitFrame));
                    MeasureWrite.Bump(Measure, mb + RM.FollowHomeMode + (int)SkiDayRules.MainMode(c.FollowLegs));
                }
                if (res == SkiDayTiming.FollowResult.StillOut)
                    MeasureWrite.Bump(Measure, mb + RM.FollowStillOut + math.min((int)c.StillOutState, SkiDayTiming.StillOutCount - 1));
                if (c.WaitPasses > 0)
                    MeasureWrite.AddHours(Hours, Measure, mb + RM.FollowWaitN, hb + RH.FollowWaitH, SkiDayTiming.HoursFor((uint)c.WaitPasses * PassInterval));
                if (c.WaitNearPasses > 0)
                    MeasureWrite.AddHours(Hours, Measure, mb + RM.FollowWaitNearN, hb + RH.FollowWaitNearH,
                                          SkiDayTiming.HoursFor((uint)c.WaitNearPasses * PassInterval));
                if (c.FailPasses > 0)
                    MeasureWrite.Bump(Measure, mb + RM.FollowFailSeen);
                c.FollowResult = (byte)res;
                Record(c, 0, 255, c.Exit, c.ExitFrame, 255);
                return true;
            }

            /// <summary>The walker read of a followed guest in no building — Sample's walker part without the
            /// claim-time parts. No live walker → WalkerLost, StillOut NoWalker. Else WalkerSeen; a ride (CurrentVehicle
            /// with Ready) → FollowRidePasses, FollowRideFirst once, the leg and transit kind; not riding: queued at a stop
            /// (Resident WaitingTransport, ResidentAISystem.cs:1517-1535 (=)) → WaitPasses, and within NearLodgeM of the
            /// lodge → WaitNearPasses; Divert{PathFailed} or a Failed / Stuck path → FailPasses; then the StillOut state.
            /// Every counter saturates.</summary>
            private void FollowWalker(ref SkiDayClaim c)
            {
                Entity walker = CurrentTransports.TryGetComponent(c.Citizen, out Game.Citizens.CurrentTransport ct) ? ct.m_CurrentTransport : Entity.Null;
                if (walker == Entity.Null || !Transforms.HasComponent(walker) || Deleteds.HasComponent(walker))
                {
                    c.FollowFlags |= SkiDayTiming.FollowFlagWalkerLost;
                    c.StillOutState = (byte)SkiDayTiming.StillOut.NoWalker;
                    return;
                }
                c.FollowFlags |= SkiDayTiming.FollowFlagWalkerSeen;
                bool riding = false, waiting = false;
                if (CurrentVehicles.TryGetComponent(walker, out Game.Creatures.CurrentVehicle cv) && cv.m_Vehicle != Entity.Null &&
                    (cv.m_Flags & Game.Creatures.CreatureVehicleFlags.Ready) != 0)
                {
                    riding = true;
                    Entity v = cv.m_Vehicle;
                    Entity lead = Controllers.TryGetComponent(v, out Game.Vehicles.Controller co) && co.m_Controller != Entity.Null ? co.m_Controller : v;
                    if (c.FollowRidePasses < ushort.MaxValue)
                        c.FollowRidePasses++;
                    if (c.FollowRideFirst == 0u)
                        c.FollowRideFirst = Now == 0u ? 1u : Now;
                    bool transit = PublicTransports.TryGetComponent(lead, out Game.Vehicles.PublicTransport pt) &&
                                   (pt.m_State & (Game.Vehicles.PublicTransportFlags.Evacuating | Game.Vehicles.PublicTransportFlags.PrisonerTransport)) == 0;
                    byte leg = SkiDayRules.LegOf(PersonalCars.HasComponent(lead), Bicycles.HasComponent(lead), Taxis.HasComponent(lead), transit);
                    c.FollowLegs |= leg;
                    if (leg == SkiDayRules.LegTransit)
                        c.FollowKinds |= SkiDayRules.KindBitOf(TransportTypeOf(v, lead));
                }
                else
                {
                    waiting = Residents.TryGetComponent(walker, out Game.Creatures.Resident resident) &&
                              ((uint)resident.m_Flags & SkiDayTiming.ResidentWaitingTransport) != 0u;
                    if (waiting)
                    {
                        if (c.WaitPasses < ushort.MaxValue)
                            c.WaitPasses++;
                        if (c.WaitNearPasses < ushort.MaxValue && Transforms.TryGetComponent(walker, out Game.Objects.Transform wt) &&
                            SkiDayTiming.WithinXZ(wt.m_Position.xz, c.LodgeXz, SkiDayTiming.NearLodgeM))
                            c.WaitNearPasses++;
                    }
                }
                bool failed = (Diverts.TryGetComponent(walker, out Game.Creatures.Divert divert) && divert.m_Purpose == Game.Citizens.Purpose.PathFailed) ||
                              (PathOwners.TryGetComponent(walker, out Game.Pathfind.PathOwner walkerPath) && PathBad(walkerPath));
                if (failed && c.FailPasses < ushort.MaxValue)
                    c.FailPasses++;
                c.StillOutState = (byte)SkiDayTiming.StillOutOf(true, riding, waiting, failed);
            }

            /// <summary>Measures at a failure (o 0 gone, 1 retargeted, 2 expired; after the existing counters, before the learning): where
            /// the citizen stood, the markers, why Leisure went (CitizenBehaviorSystem removes Leisure only for a citizen in a building
            /// without TravelPurpose, 1.6.2 :1149, and leaves these traces: HealthProblem :756-760, household MovingAway :698-703,
            /// TravelPurpose Sleeping :329-341, ResourceBuyer :417-432; LeisureSystem and DeathCheckSystem are the other removers), the
            /// clock bin, the bound of an expiry, the closest approach, the hours on the road and the mode seen (departed) or "never left",
            /// the car reserve and the distance band. Gone and Retargeted are recorded at once; an expiry is recorded when its late watch
            /// ends. A departed failure books its door wait (RM.DoorN group 1 / RH.Door, DepartFrame → MoveFrame) or RM.NeverMovedFailed,
            /// so group-1 samples + NeverMovedFailed = departed failures. The first non-Leisure trip is FirstOtherTrip.</summary>
            private void Failure(ref SkiDayClaim c, int mb, int hb, int cls, int kind, int o, bool gone, bool inBuilding, bool atOrigin, bool at,
                                 bool hasTp, byte purpose)
            {
                bool departed = (c.Flags & ClaimFlag.Departed) != 0;
                SkiDayTiming.Place place = SkiDayTiming.PlaceOf(gone, inBuilding, atOrigin, at, departed);
                c.Place = (byte)place;
                c.EndFrame = Now;
                MeasureWrite.Bump(Measure, mb + RM.Fail + ((o * SkiDayTiming.PlaceCount + (int)place) * 3 + cls) * 2 + kind);
                Markers(mb + RM.FailMarker, cls, c);
                if (o < 2)
                {
                    bool hasTrip = FirstOtherTrip(c.Citizen, out byte trip);
                    bool movingAway = HouseholdMembers.TryGetComponent(c.Citizen, out HouseholdMember hm) && MovingAways.HasComponent(hm.m_Household);
                    SkiDayTiming.GoneWhy why = SkiDayTiming.WhyOf(gone, HealthProblems.HasComponent(c.Citizen), movingAway, hasTp, purpose, hasTrip, trip,
                                                                  ResourceBuyers.HasComponent(c.Citizen));
                    c.GoneWhy = (byte)why;
                    MeasureWrite.Bump(Measure, mb + RM.GoneWhy + (o * SkiDayTiming.GoneWhyCount + (int)why) * 3 + cls);
                }
                MeasureWrite.Bump(Measure, mb + RM.FailClock + o * 12 + SkiDayTiming.ClockBin(TimeOfDay));
                if (o == 2)
                    MeasureWrite.Bump(Measure, mb + RM.ExpiredBound + cls * 2 + ((c.Flags & ClaimFlag.DeadlineIsClosing) != 0 ? 1 : 0));
                MeasureWrite.Bump(Measure, mb + RM.Closest + cls * 4 + SkiDayTiming.ClosestBand(c.ClosestM));
                if (departed)
                {
                    PhaseHours(mb, hb, SkiDayTiming.Phase.RoadGone + o, cls, Now, c.DepartFrame);
                    MeasureWrite.Bump(Measure, mb + RM.ModeFailed + cls * SkiDayRules.ArrivalModes + (int)SkiDayRules.MainMode(c.Legs));
                    if (c.MoveFrame != 0u)   // group 1, departed failures that moved off
                        MeasureWrite.AddHours(Hours, Measure, mb + RM.DoorN + 3 + cls, hb + RH.Door + 3 + cls, HoursBetween(c.MoveFrame, c.DepartFrame));
                    else
                        MeasureWrite.Bump(Measure, mb + RM.NeverMovedFailed + cls);
                }
                else
                {
                    MeasureWrite.Bump(Measure, mb + RM.NeverLeftFailed + cls);
                }
                CarReserveAtEnd(c, mb);
                MeasureWrite.Bump(Measure, mb + RM.FailedBand + cls * SkiDayRules.DistanceBands + SkiDayRules.DistanceBand(c.DistanceM));
                if (o < 2)
                    Record(c, (byte)(o + 1), (byte)place, 255, 0u, 255);
            }

            /// <summary>For each marker set on the claim, one count at base + marker × 3 + cls.</summary>
            private void Markers(int baseIndex, int cls, in SkiDayClaim c)
            {
                for (int m = 0; m < SkiDayTiming.MarkerCount; m++)
                {
                    if (SkiDayTiming.HasMarker(c.Flags, c.Flags2, m))
                        MeasureWrite.Bump(Measure, baseIndex + m * 3 + cls);
                }
            }

            /// <summary>Car reserve × mode, counted once when a QUEUED car claim resolves (arrival or failure). Invariant: the block's sum =
            /// the queued car claims that resolved this month (not CarsEnqueued, which counts claims made). HeldAtHome is never set (bucket
            /// 0 cannot fill) and the unused block at RM 535 is never bumped; both stay in the layout.</summary>
            private void CarReserveAtEnd(in SkiDayClaim c, int mb)
            {
                if (c.Class != (byte)SkiDayRules.TravelClass.Car || c.CarEnqueued != 1)
                    return;
                bool departed = (c.Flags & ClaimFlag.Departed) != 0;
                SkiDayTiming.CarReserve bucket = SkiDayTiming.CarReserveOf(departed, (c.Flags & ClaimFlag.HeldEver) != 0,
                                                                          (c.Flags & ClaimFlag.HeldAtHome) != 0);
                MeasureWrite.Bump(Measure, mb + RM.CarReserve + (int)bucket * SkiDayRules.ArrivalModes + (int)SkiDayRules.MainMode(c.Legs));   // bucket × 5 + mode
            }

            /// <summary>One phase sample: the hours from <paramref name="earlier"/> to <paramref name="later"/> (0 when not
            /// later), summed into the hour row and counted in RM.PhaseN.</summary>
            private void PhaseHours(int mb, int hb, int phase, int cls, uint later, uint earlier)
            {
                int i = phase * 3 + cls;
                MeasureWrite.AddHours(Hours, Measure, mb + RM.PhaseN + i, hb + i, HoursBetween(later, earlier));
            }

            private static double HoursBetween(uint later, uint earlier)
            {
                uint d = later - earlier;
                return (int)d > 0 ? SkiDayTiming.HoursFor(d) : 0.0;
            }

            /// <summary>One ended ski day into the ring (guards: a ring and its state exist, the slot is in range). Every field is copied
            /// from the claim, and FollowEndFrame is this pass for a followed guest (0 otherwise).</summary>
            private void Record(in SkiDayClaim c, byte outcome, byte place, byte exit, uint exitFrame, byte lateResult)
            {
                if (Ring.Length == 0 || RingState.Length < 2)
                    return;
                int slot = RingState[0];
                if ((uint)slot >= (uint)Ring.Length)
                    slot = 0;
                Ring[slot] = new ClaimRecord
                {
                    Citizen = c.Citizen,
                    Sign = c.Sign,
                    ClaimFrame = c.ClaimFrame,
                    Deadline = c.Deadline,
                    DepartFrame = c.DepartFrame,
                    RideFirstFrame = c.RideFirstFrame,
                    RideLastFrame = c.RideLastFrame,
                    ArrivalFrame = c.ArrivalFrame,
                    EndFrame = c.EndFrame,
                    ExitFrame = exitFrame,
                    Near500Frame = c.Near500Frame,
                    DistanceM = c.DistanceM,
                    ClosestM = c.ClosestM,
                    ClaimMinute = c.ClaimMinute,
                    RidePasses = c.RidePasses,
                    Class = c.Class,
                    Kind = c.Kind,
                    Outcome = outcome,
                    Place = place,
                    EndCause = outcome == 0 ? c.EndCause : (byte)255,
                    Exit = exit,
                    Legs = c.Legs,
                    Kinds = c.Kinds,
                    Flags = c.Flags,
                    Flags2 = c.Flags2,
                    GoneWhy = c.GoneWhy,
                    TurnPurpose = c.TurnPurpose,
                    LateResult = lateResult,
                    MoveFrame = c.MoveFrame,
                    FollowEndFrame = (c.FollowFlags & SkiDayTiming.FollowFlagFollowed) != 0 ? Now : 0u,
                    FollowRideFirst = c.FollowRideFirst,
                    WaitPasses = c.WaitPasses,
                    WaitNearPasses = c.WaitNearPasses,
                    FailPasses = c.FailPasses,
                    FollowRidePasses = c.FollowRidePasses,
                    RemoveWhy = c.RemoveWhy,
                    EndLive = c.EndLive,
                    MoveReason = c.MoveReason,
                    ExitAt = c.ExitAt,
                    FollowResult = c.FollowResult,
                    FollowFlags = c.FollowFlags,
                    FollowLegs = c.FollowLegs,
                    FollowKinds = c.FollowKinds,
                    StillOutState = c.StillOutState,
                };
                RingState[0] = (slot + 1) % Ring.Length;
                RingState[1] = RingState[1] + 1;
            }
        }

        /// <summary>Step A (always): lodge verdicts, capacity, area state. Step B (an area Open, a knob on, under
        /// ClaimsCap): the candidate walk over m_CandidateQuery's chunks and the claim.</summary>
        [BurstCompile]
        private struct ClaimJob : IJob
        {
            // m_CandidateQuery's chunks and type handles. Leisure and TripNeeded are the claim's two writes.
            [ReadOnly] public NativeList<ArchetypeChunk> Chunks;
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public ComponentTypeHandle<Citizen> CitizenType;
            public ComponentTypeHandle<Leisure> LeisureType;
            public BufferTypeHandle<TripNeeded> TripType;
            [ReadOnly] public ComponentTypeHandle<CurrentBuilding> CurrentBuildingType;
            [ReadOnly] public ComponentTypeHandle<HouseholdMember> HouseholdMemberType;
            [ReadOnly] public SharedComponentTypeHandle<UpdateFrame> UpdateFrameType;

            [ReadOnly] public ComponentLookup<Household> Households;
            [ReadOnly] public ComponentLookup<PropertyRenter> PropertyRenters;
            [ReadOnly] public ComponentLookup<TouristHousehold> TouristHouseholds;
            [ReadOnly] public ComponentLookup<CommuterHousehold> CommuterHouseholds;
            [ReadOnly] public ComponentLookup<HomelessHousehold> HomelessHouseholds;
            [ReadOnly] public ComponentLookup<MovingAway> MovingAways;
            [ReadOnly] public ComponentLookup<AttendingEvent> AttendingEvents;
            [ReadOnly] public ComponentLookup<LodgingSeeker> LodgingSeekers;
            [ReadOnly] public ComponentLookup<LodgingProvider> LodgingProviders;
            [ReadOnly] public ComponentLookup<HouseholdNeed> HouseholdNeeds;
            [ReadOnly] public ComponentLookup<CarKeeper> CarKeepers;
            [ReadOnly] public ComponentLookup<BicycleOwner> BicycleOwners;
            [ReadOnly] public ComponentLookup<Game.Vehicles.PersonalCar> PersonalCars;
            [ReadOnly] public ComponentLookup<Game.Vehicles.ParkedCar> ParkedCars;
            [ReadOnly] public BufferLookup<Game.Vehicles.OwnedVehicle> OwnedVehicles;
            [ReadOnly] public ComponentLookup<Game.Objects.Transform> Transforms;
            [ReadOnly] public ComponentLookup<Building> Buildings;
            [ReadOnly] public ComponentLookup<Destroyed> Destroyeds;
            [ReadOnly] public ComponentLookup<Deleted> Deleteds;
            [ReadOnly] public ComponentLookup<Game.Buildings.LeisureProvider> LeisureProviders;
            [ReadOnly] public ComponentLookup<SkiSeasonPass> SeasonPasses;   // a holder gets P = 1 (Candidate)
            public int SeasonKey;                                           // SkiMoneyRules.SeasonKeyOfDate at the schedule

            [ReadOnly] public NativeArray<AreaJob> Areas;
            [ReadOnly] public NativeArray<LodgeJob> Lodges;
            [ReadOnly] public NativeArray<SiteOut> Sites;   // SiteJob's result, aligned with Lodges
            [ReadOnly] public NativeArray<SkiDayRules.ClassTrack> Tracks;
            [ReadOnly] public NativeArray<int> ActiveNow;
            public NativeArray<byte> LodgeReject;   // SkiDayRules.LodgeReject, aligned with Lodges
            public NativeArray<ClaimOut> Out;       // areas
            public NativeList<SkiDayClaim> Claims;
            public NativeArray<int> PassStats;      // zeroed by the system before scheduling
            public NativeArray<int> Measure;        // areas × CM.Count, zeroed by the system before scheduling
            public NativeQueue<Entity>.ParallelWriter CarReserve;

            // pass inputs
            public uint Now;
            public float TimeOfDay;
            public int DayKey;
            public SkiDayRules.Knobs Knobs;
            public int Price;
            public int LeisureInterval, BehaviorInterval, BehaviorOffset;
            public SkiDayRules.TripKnobs TripKnobs;   // the fit check's speed (FitSpeed) and stay reserve
            // OwnDeadline's inputs, the same as ResolveJob's clock checks (LiveKnown 0: vanilla's deadline stands).
            [ReadOnly] public ComponentLookup<Worker> Workers;
            [ReadOnly] public ComponentLookup<Game.Citizens.Student> Students;
            [ReadOnly] public ComponentLookup<Game.City.Population> Populations;
            public Entity PopulationEntity;
            public byte LiveKnown;
            // Vanilla structs held by value go last: a game patch that resizes one shifts only these two (DECISIONS 581 e).
            public Game.Prefabs.EconomyParameterData Economy;
            public TimeData TimeData;

            public void Execute()
            {
                int areaCount = math.min(Areas.Length, Out.Length);
                bool anyOpen = StepA(areaCount);
                if (!anyOpen || (Knobs.LocalsPct <= 0 && Knobs.TouristsPct <= 0))
                    return;
                if (Claims.Length >= Tuning.ClaimsCap)
                {
                    PassStats[Stat.CapReached] = 1;
                    return;
                }
                PassStats[Stat.WalkRan] = 1;
                var active = new NativeArray<int>(areaCount, Allocator.Temp);
                for (int a = 0; a < areaCount; a++)
                    active[a] = a < ActiveNow.Length ? ActiveNow[a] : 0;
                var promised = new NativeParallelHashMap<Entity, int>(16, Allocator.Temp);   // household → cars enqueued this pass
                StepB(areaCount, active, promised);
                promised.Dispose();
                active.Dispose();
            }

            /// <summary>Step A: lodges (every read guarded, live state), then per area the target lodges, capacity, claim hours and closing
            /// and the area state. Returns whether any area is Open.</summary>
            private bool StepA(int areaCount)
            {
                int lodges = math.min(Lodges.Length, LodgeReject.Length);
                // A lodge whose only failing check is NoLeisureTag (Tourism Overhaul removed its LeisureProvider: the park gate at max(6,
                // cells) people) stays in TargetLodges and Capacity and is skipped only as a claim target (LodgeReject keeps NoLeisureTag, so
                // NearestTarget and the presale copy still skip it). Without this a busy gated lodge would flap the area to NoLodge every 512
                // frames.
                var gated = new NativeArray<byte>(lodges, Allocator.Temp);
                // The real lodges first, then the bus depots: a depot passes only with a passing hill lodge in its area (a mountain lodge
                // counts; a gated lodge counts, as it stays in capacity).
                var hillLodges = new NativeArray<int>(areaCount, Allocator.Temp);
                for (int li = 0; li < lodges; li++)
                {
                    if (Lodges[li].IsDepot != 0)
                        continue;
                    Entity l = Lodges[li].Lodge;
                    bool hasB = Buildings.TryGetComponent(l, out Building b);
                    bool hasSite = li < Sites.Length && Sites[li].HasPosition != 0;
                    var facts = new SkiDayRules.LodgeFacts
                    {
                        IsLodgePrefab = true,   // both from CopyActiveLodgeInfo (lodge prefab, linked to a live sign)
                        OnLiveSign = true,
                        Active = hasB && !BuildingUtils.CheckOption(b, BuildingOption.Inactive),   // BuildingUtils.cs:351 (=)
                        Destroyed = Destroyeds.HasComponent(l) || Deleteds.HasComponent(l) || !hasB,
                        // the searchable tag a TO gate removes; a merged building is no guest destination in v1, so it never needs one
                        LeisureTagged = Lodges[li].Merged != 0 || LeisureProviders.HasComponent(l),
                        HasRoad = hasB && b.m_RoadEdge != Entity.Null,
                        NearestRunOrStationM = hasSite ? Sites[li].NearestRunOrStationM : float.PositiveInfinity,
                        NoRoadNeeded = Lodges[li].NoRoadNeeded != 0,   // a mountain lodge needs no road
                    };
                    SkiDayRules.LodgeReject verdict = SkiDayRules.CheckLodge(facts);
                    LodgeReject[li] = (byte)verdict;
                    if (verdict == SkiDayRules.LodgeReject.NoLeisureTag)
                    {
                        facts.LeisureTagged = true;   // would every OTHER check pass? (NoRoad, NotSlopeside still reject)
                        gated[li] = (byte)(SkiDayRules.CheckLodge(facts) == SkiDayRules.LodgeReject.None ? 1 : 0);
                    }
                    int hillArea = Lodges[li].Area;
                    if ((verdict == SkiDayRules.LodgeReject.None || gated[li] != 0) && hillArea >= 0 && hillArea < areaCount)
                        hillLodges[hillArea]++;
                }
                for (int li = 0; li < lodges; li++)
                {
                    if (Lodges[li].IsDepot == 0)
                        continue;
                    Entity l = Lodges[li].Lodge;
                    bool hasB = Buildings.TryGetComponent(l, out Building b);
                    int hillArea = Lodges[li].Area;
                    var facts = new SkiDayRules.LodgeFacts
                    {
                        IsLodgePrefab = true,   // from CopyActiveDepots (depot prefab, linked to a live sign)
                        OnLiveSign = true,
                        Active = hasB && !BuildingUtils.CheckOption(b, BuildingOption.Inactive),
                        Destroyed = Destroyeds.HasComponent(l) || Deleteds.HasComponent(l) || !hasB,
                        LeisureTagged = LeisureProviders.HasComponent(l),
                        HasRoad = hasB && b.m_RoadEdge != Entity.Null,
                    };
                    int hills = hillArea >= 0 && hillArea < areaCount ? hillLodges[hillArea] : 0;
                    SkiDayRules.LodgeReject verdict = SkiDayRules.CheckDepot(facts, hills);
                    LodgeReject[li] = (byte)verdict;
                    if (verdict == SkiDayRules.LodgeReject.NoLeisureTag)
                    {
                        facts.LeisureTagged = true;
                        gated[li] = (byte)(SkiDayRules.CheckDepot(facts, hills) == SkiDayRules.LodgeReject.None ? 1 : 0);
                    }
                }
                hillLodges.Dispose();

                bool anyOpen = false;
                for (int a = 0; a < areaCount; a++)
                {
                    AreaJob aj = Areas[a];
                    // Targets = passing Base-road lodges and passing bus depots; capacity and tolerance = passing lodges (every one holds
                    // skiers; a depot's Capacity and PriceTolerance are 0, so it adds to neither: capacity stays min(runs, hill lodges)).
                    int target = 0, tolerance = 0, claimable = 0;
                    long lodgeCapacity = 0;
                    int end = math.min(aj.LodgeStart + aj.LodgeCount, lodges);
                    for (int li = math.max(0, aj.LodgeStart); li < end; li++)
                    {
                        bool isGated = gated[li] != 0;   // NoLeisureTag only; counted, never claimed
                        if (LodgeReject[li] != (byte)SkiDayRules.LodgeReject.None && !isGated)
                            continue;
                        LodgeJob lj = Lodges[li];
                        if (lj.NoRoadNeeded == 0)
                        {
                            target++;
                            if (!isGated)
                                claimable++;
                        }
                        lodgeCapacity += math.max(0, lj.Capacity);
                        if (!isGated)
                            tolerance = math.max(tolerance, lj.PriceTolerance);   // = TryCopyPresaleInputs (passing lodges only)
                    }
                    bool night = aj.NightRunOpen != 0;
                    double capacity = SkiDayRules.Capacity(aj.RunsCapacity, (double)lodgeCapacity, target, aj.Powder, out SkiDayLimit limit);
                    int capacityPeople = SkiDayRules.CapacityPeople(capacity);
                    bool hours = SkiDayRules.InClaimWindow(TimeOfDay, night);
                    bool hasClosing = SkiDayRules.TryClosingFrame(Now, TimeOfDay, night, out uint closing);
                    int act = a < ActiveNow.Length ? ActiveNow[a] : 0;
                    var facts = new SkiDayRules.AreaFacts
                    {
                        ScoreKnown = aj.ScoreKnown != 0,
                        WriteHeld = aj.WriteHeld != 0,
                        Efficiency = aj.Efficiency,
                        RunsCounted = aj.RunsCounted,
                        SkiDayLodges = target,
                    };
                    SkiDayRules.AreaState state = SkiDayRules.AreaStateOf(facts, TimeOfDay, night, act, capacityPeople);
                    if (state == SkiDayRules.AreaState.Open && target > 0 && claimable == 0)
                        state = SkiDayRules.AreaState.Full;   // every target lodge is gated: no room to send a guest (not NoLodge)
                    Out[a] = new ClaimOut
                    {
                        CapacityPeople = capacityPeople,
                        TargetLodges = target,
                        State = (byte)state,
                        Limit = (byte)limit,
                        ClaimHours = (byte)(hours ? 1 : 0),
                        ClosingFrame = hasClosing ? closing : 0u,
                        PriceTolerance = tolerance,
                        LodgeCapacity = (int)math.min(lodgeCapacity, (long)int.MaxValue),
                    };
                    if (state == SkiDayRules.AreaState.Open)
                        anyOpen = true;
                }
                gated.Dispose();
                return anyOpen;
            }

            /// <summary>Step B: chunks not skipped (SkiDayTiming.SkipChunk); per citizen the cheap tests first (CheckCitizen), shopping, the
            /// origin, the car facts, the best area, one roll, the claim and the room countdown.</summary>
            private void StepB(int areaCount, NativeArray<int> active, NativeParallelHashMap<Entity, int> promised)
            {
                uint leisureSlice = SkiDayRules.SliceOf(Now, LeisureInterval);
                // A decision is counted in the pools once: at its claim or at its LAST UNSKIPPED sighting before LeisureSystem takes it. The
                // pass one interval before that visit can itself skip the slice (the recent CBS window): then an earlier pass is the last one
                // that sees it (SkiDayTiming.IsLastSighting). Once per pass for the 16 slices, not per chunk.
                uint lastMask = 0u;
                for (uint s = 0; s < 16u; s++)
                {
                    if (SkiDayTiming.IsLastSighting(s, Now, LeisureInterval, BehaviorInterval, BehaviorOffset))
                        lastMask |= 1u << (int)s;
                }
                for (int c = 0; c < Chunks.Length; c++)
                {
                    ArchetypeChunk chunk = Chunks[c];
                    PassStats[Stat.Chunks]++;
                    uint slice = chunk.GetSharedComponent(UpdateFrameType).m_Index;
                    if (SkiDayTiming.SkipChunk(slice, Now, LeisureInterval, BehaviorInterval, BehaviorOffset))
                    {
                        PassStats[slice == leisureSlice ? Stat.SkippedLeisure : Stat.SkippedBehavior]++;
                        continue;
                    }
                    bool last = slice < 16u && ((lastMask >> (int)slice) & 1u) != 0;
                    uint decisionFrame = SkiDayTiming.NextLeisureVisit(slice, Now, LeisureInterval);   // roll key
                    NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                    NativeArray<Citizen> citizens = chunk.GetNativeArray(ref CitizenType);
                    NativeArray<Leisure> leisures = chunk.GetNativeArray(ref LeisureType);
                    BufferAccessor<TripNeeded> trips = chunk.GetBufferAccessor(ref TripType);
                    NativeArray<CurrentBuilding> buildings = chunk.GetNativeArray(ref CurrentBuildingType);
                    NativeArray<HouseholdMember> members = chunk.GetNativeArray(ref HouseholdMemberType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        PassStats[Stat.Walked]++;
                        if (Candidate(areaCount, active, promised, last, decisionFrame, entities[i], citizens[i], i, leisures,
                                      trips[i], buildings[i].m_CurrentBuilding, members[i].m_Household))
                            return;   // ClaimsCap reached
                    }
                }
            }

            /// <summary>One citizen. Returns true when ClaimsCap was reached (stop the walk).</summary>
            private bool Candidate(int areaCount, NativeArray<int> active, NativeParallelHashMap<Entity, int> promised, bool last,
                                   uint decisionFrame, Entity e, Citizen citizen, int index, NativeArray<Leisure> leisures,
                                   DynamicBuffer<TripNeeded> trip, Entity cb, Entity hh)
            {
                Leisure leisure = leisures[index];
                // Test 1 (cheap, chunk arrays). Busy (PathInformation, Target, TravelPurpose, ResourceBuyer,
                // AttendingMeeting) and Ill (HealthProblem) are archetype exclusions of m_CandidateQuery:
                // false here. The household facts are read only when the cheap ones pass; CheckCitizen tests the cheap
                // ones first, so its verdict is the same either way.
                var facts = new SkiDayRules.CitizenFacts
                {
                    HasLeisure = true,
                    LeisureTargetNull = leisure.m_TargetAgent == Entity.Null,
                    TripCount = trip.Length,
                    HasCurrentBuilding = cb != Entity.Null,
                };
                bool tourist = false;
                TouristHousehold th = default;
                if (facts.LeisureTargetNull && facts.TripCount == 0 && facts.HasCurrentBuilding)
                {
                    // Test 2: household lookups; AtHome = the home rule (CitizenBehaviorSystem.cs:704-711, 1.6.2
                    // :722-733); a tourist only in a real hotel: LodgingProvider AND a PropertyRenter whose property is
                    // this building (cruise terminals rent nothing).
                    tourist = TouristHouseholds.TryGetComponent(hh, out th);
                    facts.HouseholdTourist = tourist;
                    facts.HouseholdCommuter = CommuterHouseholds.HasComponent(hh) ||
                                              (Households.TryGetComponent(hh, out Household household) &&
                                               (household.m_Flags & HouseholdFlags.Commuter) != 0) ||
                                              (citizen.m_State & CitizenFlags.Commuter) != 0;
                    facts.HouseholdHomeless = HomelessHouseholds.HasComponent(hh);
                    facts.HouseholdMovingAway = MovingAways.HasComponent(hh);
                    facts.HouseholdLodgingSeeker = LodgingSeekers.HasComponent(hh);
                    facts.HouseholdAttendingEvent = AttendingEvents.HasComponent(hh);
                    if (tourist)
                    {
                        facts.AtHome = th.m_Hotel != Entity.Null && LodgingProviders.HasComponent(th.m_Hotel) &&
                                       PropertyRenters.TryGetComponent(th.m_Hotel, out PropertyRenter hotel) && hotel.m_Property == cb;
                    }
                    else
                    {
                        facts.AtHome = PropertyRenters.TryGetComponent(hh, out PropertyRenter home) && home.m_Property == cb;
                    }
                }
                SkiDayRules.Reject reject = SkiDayRules.CheckCitizen(facts, out SkiDayRules.GuestKind who);
                int rejectSlot = Stat.RejectBase + (int)reject;
                if (rejectSlot >= Stat.RejectBase && rejectSlot <= Stat.RejectBase + 8)
                    PassStats[rejectSlot]++;
                if (reject != SkiDayRules.Reject.None)
                    return false;

                // Test 3: adults and elderly shop first when the household has a need (CitizenBehaviorSystem.cs
                // :759-773, 1.6.2 :788-801).
                CitizenAge age = citizen.GetAge();
                if ((age == CitizenAge.Adult || age == CitizenAge.Elderly) && HouseholdNeeds.TryGetComponent(hh, out HouseholdNeed need) &&
                    need.m_Resource != Resource.NoResource)
                {
                    if (last)
                        PassStats[Stat.ShoppingNeed]++;
                    return false;
                }
                // Test 4: the origin's position (home or hotel building).
                if (!Transforms.TryGetComponent(cb, out Game.Objects.Transform originT))
                    return false;
                float2 origin = originT.m_Position.xz;

                // Car facts. FreeCars = HouseholdBehaviorSystem.GetFreeCar's test (:502-518, 1.6.2 :504-520)
                // counted; the skip rules are the reserve job's (CitizenBehaviorSystem.cs:67, 1.6.2 :68).
                bool child = age == CitizenAge.Child;
                bool keeper = CarKeepers.TryGetEnabledComponent(e, out CarKeeper k);
                bool holdsParkedCar = keeper && k.m_Car != Entity.Null && ParkedCars.HasComponent(k.m_Car);
                bool bikeAway = BicycleOwners.TryGetEnabledComponent(e, out BicycleOwner bo) &&
                                PersonalCars.TryGetComponent(bo.m_Bicycle, out Game.Vehicles.PersonalCar bike) &&
                                (bike.m_State & Game.Vehicles.PersonalCarFlags.HomeTarget) == 0;
                bool householdFreeCar = false;
                if (!keeper && !child && !bikeAway)
                {
                    promised.TryGetValue(hh, out int already);
                    householdFreeCar = FreeCars(hh) - already > 0;
                }
                bool withCar = holdsParkedCar || householdFreeCar;
                if (last)
                    CountFresh(who, withCar);

                // A resident household that holds this season's pass is not deterred by the ticket price (P = 1 in the chance: the pass is
                // sunk cost). Tourists never hold one; a Declined standing or a pass of an older season is a plain price-sensitive household.
                bool passHolder = who == SkiDayRules.GuestKind.Resident && SeasonPasses.TryGetComponent(hh, out SkiSeasonPass pass) &&
                                  SkiMoneyRules.PassStandingOf(true, pass.m_SeasonKey, pass.m_Flags, SeasonKey) == SkiMoneyRules.PassStanding.Holder;

                uint ownDeadline = OwnDeadline(e, citizen, leisure.m_LastPossibleFrame);

                // The best area: one roll against the best area's chance only.
                int bestA = -1, bestLi = -1;
                byte bestCls = 0;
                double bestP = 0.0, bestD = double.PositiveInfinity;
                EntityRef bestSign = default;
                bool bestOffDayOnly = false;   // the winner fits only with the off-day deadline (the measure)
                bool leaving = false;
                for (int a = 0; a < areaCount; a++)
                {
                    ClaimOut co = Out[a];
                    if (co.State != (byte)SkiDayRules.AreaState.Open)
                        continue;
                    if ((who == SkiDayRules.GuestKind.Resident ? Knobs.LocalsPct : Knobs.TouristsPct) <= 0)
                        continue;   // a knob at 0 turns its kind off
                    if (tourist && th.m_LeavingTime != 0 && th.m_LeavingTime <= co.ClosingFrame)
                    {
                        leaving = true;   // a Tourism Overhaul stay that ends before closing
                        continue;
                    }
                    AreaJob aj = Areas[a];
                    int li = NearestTarget(aj, origin, out float d);
                    if (li < 0 || !SkiDayRules.WithinReach(d, Knobs.ReachKm))
                        continue;
                    if (!SkiDayRules.HasRoom(active[a], co.CapacityPeople))
                        continue;
                    SkiDayRules.TravelClass cls = SkiDayRules.ClassOf(d, child, holdsParkedCar, householdFreeCar);
                    if (cls == SkiDayRules.TravelClass.None)
                        continue;
                    int ti = SkiDayTiming.TrackIndex(a, (byte)cls);
                    SkiDayRules.ClassTrack t = ti >= 0 && ti < Tracks.Length ? Tracks[ti] : SkiDayRules.NewTrack(cls);
                    // The speed the fit check reads (arrivals only while FitUsesAllOutcomes is false: the same
                    // double as t.SpeedKmPerHour) and the stay reserve from TripKnobs (2 h by default: the same expression).
                    double hours = SkiDayRules.TravelHours(d, SkiDayRules.FitSpeed(t, TripKnobs));
                    if (!SkiDayRules.ClaimFits(Now, ownDeadline, co.ClosingFrame, hours, TripKnobs.StayHours))
                    {
                        if (last)
                            TooLate(a, cls, who, aj, d, hours, ownDeadline, co.ClosingFrame, t, passHolder);   // measure only
                        continue;
                    }
                    // The area's price tolerance and school factor.
                    double p = SkiDayRules.Chance(who, aj.Attraction, d / 1000.0, aj.OpenShare, aj.Powder, Price,
                                                  co.PriceTolerance, aj.SchoolFactor, SkiDayRules.Confidence(cls, t), Knobs, passHolder);
                    if (!SkiDayRules.IsBetter(p, d, aj.SignRef, bestP, bestD, bestSign))
                        continue;
                    bestA = a;
                    bestLi = li;
                    bestCls = (byte)cls;
                    bestP = p;
                    bestD = d;
                    bestSign = aj.SignRef;
                    bestOffDayOnly = !SkiDayRules.ClaimFits(Now, leisure.m_LastPossibleFrame, co.ClosingFrame, hours, TripKnobs.StayHours);
                }
                if (leaving && last)
                    PassStats[Stat.LeavingToday]++;
                if (bestA < 0 || bestLi < 0 || bestLi >= Lodges.Length)
                    return false;

                // One roll per decision: residents keyed on the LeisureSystem visit that takes this decision
                // tourists once per party per game day.
                double roll = who == SkiDayRules.GuestKind.Resident
                    ? SkiDayRules.ResidentRoll(e.Index, e.Version, decisionFrame)
                    : SkiDayRules.TouristRoll(hh.Index, hh.Version, DayKey);
                ClaimOut won = Out[bestA];
                if (!SkiDayRules.Hit(roll, bestP))
                {
                    if (last)
                    {
                        won.Pool++;   // a decision enters a pool once: at its claim or at its last sighting
                        won.PoolChanceSum += bestP;   // the pool's mean chance
                        if (tourist)
                            won.PoolTourists++;
                        Out[bestA] = won;
                    }
                    return false;
                }

                // The claim: LeisureSystem's own two writes (LeisureSystem.cs:398-405, 1.6.2 :392-399), the
                // deadline = min(own, closing), own being vanilla's or bedtime on a day off; nothing else; no structural change.
                Entity lodge = Lodges[bestLi].Lodge;
                bool toDepot = Lodges[bestLi].IsDepot != 0;
                if (toDepot)
                    won.SentDepot++;
                if (bestOffDayOnly)
                    won.SentOffDay++;
                uint deadline = SkiDayRules.ClaimDeadline(ownDeadline, won.ClosingFrame);
                leisure.m_TargetAgent = lodge;
                leisure.m_LastPossibleFrame = deadline;
                leisures[index] = leisure;
                trip.Add(new TripNeeded { m_TargetAgent = lodge, m_Purpose = Game.Citizens.Purpose.Leisure, m_Priority = 128 });
                // The car-reserve queue only for a Car-class claim without a parked car (WorkerSystem's handshake,
                // WorkerSystem.cs:450-454; the reserve job re-checks child / keeper / bicycle, CitizenBehaviorSystem.cs:67).
                bool enqueue = bestCls == (byte)SkiDayRules.TravelClass.Car && !holdsParkedCar;
                if (enqueue)
                {
                    CarReserve.Enqueue(e);
                    promised.TryGetValue(hh, out int n);
                    promised[hh] = n + 1;
                    won.CarsEnqueued++;
                }
                if (bestA < active.Length)
                    active[bestA]++;   // in-job countdown: one pass cannot overshoot
                Claims.Add(new SkiDayClaim
                {
                    Citizen = e,
                    Lodge = lodge,
                    Sign = Areas[bestA].Sign,
                    Origin = cb,
                    ClaimFrame = Now,
                    Deadline = deadline,
                    ArrivalFrame = 0u,
                    DistanceM = (float)bestD,
                    Class = bestCls,
                    Kind = (byte)who,
                    State = 0,
                    CarEnqueued = (byte)(enqueue ? 1 : 0),
                    // The clock at the claim, the lodge's position for the closest approach (NearestTarget only picks a lodge with one, so NaN is
                    // never stored in practice), and what bound the deadline (a tie counts as closing). Every other new field starts at 0.
                    ClaimMinute = (ushort)SkiDayTiming.MinuteOfDay(TimeOfDay),
                    ClosestM = float.PositiveInfinity,
                    LodgeXz = bestLi < Sites.Length && Sites[bestLi].HasPosition != 0 ? Sites[bestLi].Xz : new float2(float.NaN, float.NaN),
                    Flags = (byte)((deadline == won.ClosingFrame ? ClaimFlag.DeadlineIsClosing : 0) | (toDepot ? ClaimFlag.ToDepot : 0)),
                    // "not read" / "none" / "n/a"; every other new field starts at 0.
                    RemoveWhy = 255,
                    MoveReason = 255,
                    ExitAt = 255,
                });
                won.Pool++;
                won.PoolChanceSum += bestP;
                MeasureWrite.Bump(Measure, bestA * CM.Count + CM.SentBand + bestCls * SkiDayRules.DistanceBands + SkiDayRules.DistanceBand(bestD));
                if (tourist)
                {
                    won.PoolTourists++;
                    won.SentTourists++;
                }
                won.Sent++;
                if (bestCls == (byte)SkiDayRules.TravelClass.Walk)
                    won.SentWalk++;
                else if (bestCls == (byte)SkiDayRules.TravelClass.Car)
                    won.SentCar++;
                else
                    won.SentNoCar++;
                if (won.HasFirst == 0)
                {
                    won.HasFirst = 1;
                    won.FirstClass = bestCls;
                    won.FirstKind = (byte)who;
                    won.FirstChance = bestP;
                    won.FirstDistM = (float)bestD;
                    won.FirstLodge = lodge;
                }
                Out[bestA] = won;
                if (!last)
                    CountFresh(who, withCar);   // a decision claimed before its last sighting is never seen there again
                PassStats[Stat.Claimed]++;
                if (Claims.Length >= Tuning.ClaimsCap)
                {
                    PassStats[Stat.CapReached] = 1;
                    return true;
                }
                return false;
            }

            private void CountFresh(SkiDayRules.GuestKind who, bool withCar)
            {
                PassStats[who == SkiDayRules.GuestKind.Tourist ? Stat.FreshTourists : Stat.FreshLocals]++;
                if (withCar)
                    PassStats[Stat.FreshWithCar]++;
            }

            /// <summary>Measure only (the decision is refused exactly as before): a decision that passed reach, room and class at area
            /// <paramref name="a"/> but whose trip + the stay reserve (TripKnobs.StayHours, 2 h by default) does not fit, counted once at
            /// its last sighting (as the pool counts it) by the deadline that is too early (FitVerdict) and class, with the chance it would
            /// have had here (the same Chance call as an accepted decision). A decision refused here and sent to another area counts here
            /// too, so "expected ski days lost" is an upper bound.</summary>
            /// <summary>The deadline the fit check and the claim read: vanilla's, raised to bedtime for a worker or student on a day off
            /// (SkiDayRules.OffDayDeadline). IsTodayOffDay is also the student coin (StudentSystem.cs:316); worker first, as vanilla reads them.</summary>
            private uint OwnDeadline(Entity e, Citizen z, uint vanilla)
            {
                if (LiveKnown == 0)
                    return vanilla;
                bool isWorker = Workers.TryGetComponent(e, out Worker wk);
                bool isStudent = Students.TryGetComponent(e, out Game.Citizens.Student st);
                if (!isWorker && !isStudent)
                    return vanilla;
                int population = Populations.TryGetComponent(PopulationEntity, out Game.City.Population p) ? p.m_Population : 0;
                if (!WorkerSystem.IsTodayOffDay(z, ref Economy, Now, TimeData, population))
                    return vanilla;
                float2 sleep = CitizenBehaviorSystem.GetSleepTime(e, z, ref Economy, isWorker, wk, isStudent, st);
                return SkiDayRules.OffDayDeadline(vanilla, Now, TimeOfDay, sleep.x);
            }

            private void TooLate(int a, SkiDayRules.TravelClass cls, SkiDayRules.GuestKind who, in AreaJob aj, float d, double hours,
                                 uint vanillaDeadline, uint closingFrame, in SkiDayRules.ClassTrack t, bool passHolder)
            {
                SkiDayRules.Fit fit = SkiDayRules.FitVerdict(Now, vanillaDeadline, closingFrame, hours, TripKnobs.StayHours);
                MeasureWrite.Bump(Measure, a * CM.Count + (fit == SkiDayRules.Fit.NoTimeBeforeOwnDeadline ? CM.TooLateOwn : CM.TooLateClosing) + (int)cls);
                ClaimOut co = Out[a];
                co.TooLateChanceSum += SkiDayRules.Chance(who, aj.Attraction, d / 1000.0, aj.OpenShare, aj.Powder, Price,
                                                          co.PriceTolerance, aj.SchoolFactor, SkiDayRules.Confidence(cls, t), Knobs, passHolder);
                Out[a] = co;
            }

            /// <summary>The household's cars without a keeper (OwnedVehicle entries whose PersonalCar.m_Keeper is
            /// Null: HouseholdBehaviorSystem.GetFreeCar's test, counted).</summary>
            private int FreeCars(Entity hh)
            {
                if (!OwnedVehicles.TryGetBuffer(hh, out DynamicBuffer<Game.Vehicles.OwnedVehicle> owned))
                    return 0;
                int free = 0;
                for (int j = 0; j < owned.Length; j++)
                {
                    if (PersonalCars.TryGetComponent(owned[j].m_Vehicle, out Game.Vehicles.PersonalCar car) && car.m_Keeper == Entity.Null)
                        free++;
                }
                return free;
            }

            /// <summary>The nearest target lodge (LodgeReject.None, a Base road rule: a mountain lodge is
            /// never a claim target — with a position) of the area from the origin (straight line, xz); −1 if none.</summary>
            private int NearestTarget(AreaJob aj, float2 origin, out float distance)
            {
                distance = float.PositiveInfinity;
                int best = -1;
                int end = math.min(aj.LodgeStart + aj.LodgeCount, math.min(LodgeReject.Length, math.min(Sites.Length, Lodges.Length)));
                for (int li = math.max(0, aj.LodgeStart); li < end; li++)
                {
                    if (LodgeReject[li] != (byte)SkiDayRules.LodgeReject.None)
                        continue;
                    if (Lodges[li].NoRoadNeeded != 0)
                        continue;   // targets = Base road lodges
                    SiteOut so = Sites[li];
                    if (so.HasPosition == 0)
                        continue;
                    float d = SkiDayRules.DistanceXZ(origin, so.Xz);
                    if (d < distance)
                    {
                        distance = d;
                        best = li;
                    }
                }
                return best;
            }
        }

        /// <summary>Hourly measure-only census: tourist households, lodge occupants by kind, outings Traveling to
        /// a snapshot sign or lodge. Writes only its own containers.</summary>
        [BurstCompile]
        private struct CensusJob : IJob
        {
            [ReadOnly] public NativeList<ArchetypeChunk> TouristChunks;
            [ReadOnly] public NativeList<ArchetypeChunk> OccupantChunks;
            [ReadOnly] public NativeList<ArchetypeChunk> MeetingChunks;
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public ComponentTypeHandle<TouristHousehold> TouristHouseholdType;
            [ReadOnly] public BufferTypeHandle<HouseholdCitizen> HouseholdCitizenType;
            [ReadOnly] public ComponentTypeHandle<CurrentBuilding> CurrentBuildingType;
            [ReadOnly] public ComponentTypeHandle<HouseholdMember> HouseholdMemberType;
            [ReadOnly] public ComponentTypeHandle<CoordinatedMeeting> MeetingType;

            [ReadOnly] public ComponentLookup<CurrentBuilding> CurrentBuildings;
            [ReadOnly] public ComponentLookup<Game.Objects.OutsideConnection> OutsideConnections;
            [ReadOnly] public ComponentLookup<PropertyRenter> PropertyRenters;
            [ReadOnly] public ComponentLookup<AttendingEvent> AttendingEvents;
            [ReadOnly] public ComponentLookup<LodgingSeeker> LodgingSeekers;
            [ReadOnly] public ComponentLookup<MovingAway> MovingAways;
            [ReadOnly] public BufferLookup<Game.Vehicles.OwnedVehicle> OwnedVehicles;
            [ReadOnly] public ComponentLookup<Worker> Workers;
            [ReadOnly] public ComponentLookup<Leisure> Leisures;
            [ReadOnly] public ComponentLookup<AttendingMeeting> AttendingMeetings;
            [ReadOnly] public ComponentLookup<TouristHousehold> TouristHouseholds;

            [ReadOnly] public NativeArray<AreaJob> Areas;
            [ReadOnly] public NativeArray<LodgeJob> Lodges;
            [ReadOnly] public NativeParallelHashMap<Entity, int> AreaOfSign;
            [ReadOnly] public NativeParallelHashMap<Entity, int> LodgeIndex;
            public NativeArray<int> Counts;                 // Census.Count
            public NativeArray<int> Occupants;              // lodges × Occupant.Kinds (zeroed by the system before each census)
            public NativeList<TravelingOuting> Traveling;   // cleared by the system

            public void Execute()
            {
                for (int i = 0; i < Counts.Length; i++)
                    Counts[i] = 0;
                if (Counts.Length < Census.Count)
                    return;
                Counts[Census.Ran] = 1;
                Tourists();
                Occupancy();
                Outings();
            }

            /// <summary>Per tourist household: parties, people, at a map-edge connection, hotel (in it now), other
            /// lodging (m_Hotel without PropertyRenter: cruise terminals), none, outing used, car, lodging seeker,
            /// moving away.</summary>
            private void Tourists()
            {
                for (int c = 0; c < TouristChunks.Length; c++)
                {
                    ArchetypeChunk chunk = TouristChunks[c];
                    NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                    NativeArray<TouristHousehold> households = chunk.GetNativeArray(ref TouristHouseholdType);
                    BufferAccessor<HouseholdCitizen> citizens = chunk.GetBufferAccessor(ref HouseholdCitizenType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity hh = entities[i];
                        TouristHousehold th = households[i];
                        DynamicBuffer<HouseholdCitizen> members = citizens[i];
                        Counts[Census.Parties]++;
                        Counts[Census.People] += members.Length;
                        PropertyRenter pr = default;
                        bool hotelRenter = th.m_Hotel != Entity.Null && PropertyRenters.TryGetComponent(th.m_Hotel, out pr);
                        Entity hotelBuilding = hotelRenter ? pr.m_Property : Entity.Null;
                        bool edge = false, inHotel = false;
                        for (int m = 0; m < members.Length; m++)
                        {
                            if (!CurrentBuildings.TryGetComponent(members[m].m_Citizen, out CurrentBuilding cb))
                                continue;
                            if (OutsideConnections.HasComponent(cb.m_CurrentBuilding))
                                edge = true;
                            if (hotelRenter && cb.m_CurrentBuilding != Entity.Null && cb.m_CurrentBuilding == hotelBuilding)
                                inHotel = true;
                        }
                        if (edge)
                            Counts[Census.AtEdge]++;
                        if (th.m_Hotel == Entity.Null)
                            Counts[Census.NoHotel]++;
                        else if (hotelRenter)
                        {
                            Counts[Census.Hotel]++;
                            if (inHotel)
                                Counts[Census.InHotel]++;
                        }
                        else
                            Counts[Census.OtherLodging]++;
                        if (AttendingEvents.HasComponent(hh))
                            Counts[Census.OutingUsed]++;
                        if (OwnedVehicles.TryGetBuffer(hh, out DynamicBuffer<Game.Vehicles.OwnedVehicle> owned) && owned.Length > 0)
                            Counts[Census.WithCar]++;
                        if (LodgingSeekers.HasComponent(hh))
                            Counts[Census.LodgingSeekers]++;
                        if (MovingAways.HasComponent(hh))
                            Counts[Census.MovingAway]++;
                    }
                }
            }

            /// <summary>Every citizen standing in a snapshot lodge, first match of worker (Worker.m_Workplace
            /// = the lodge or its sign), resident / tourist on leisure (Leisure aimed at the lodge, split by
            /// TouristHousehold), outing member (AttendingMeeting), other.</summary>
            private void Occupancy()
            {
                int lodges = math.min(Lodges.Length, Occupants.Length / Occupant.Kinds);
                if (lodges == 0)
                    return;   // a ski area without a lodge yet: no citizen walk for nothing
                for (int c = 0; c < OccupantChunks.Length; c++)
                {
                    ArchetypeChunk chunk = OccupantChunks[c];
                    NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                    NativeArray<CurrentBuilding> buildings = chunk.GetNativeArray(ref CurrentBuildingType);
                    NativeArray<HouseholdMember> members = chunk.GetNativeArray(ref HouseholdMemberType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity building = buildings[i].m_CurrentBuilding;
                        if (building == Entity.Null || !LodgeIndex.TryGetValue(building, out int li) || li < 0 || li >= lodges)
                            continue;
                        Entity e = entities[i];
                        int area = Lodges[li].Area;
                        Entity sign = area >= 0 && area < Areas.Length ? Areas[area].Sign : Entity.Null;
                        int kind;
                        if (Workers.TryGetComponent(e, out Worker w) && w.m_Workplace != Entity.Null &&
                            (w.m_Workplace == building || w.m_Workplace == sign))
                            kind = Occupant.Worker;
                        else if (Leisures.TryGetComponent(e, out Leisure l) && l.m_TargetAgent == building)
                            kind = TouristHouseholds.HasComponent(members[i].m_Household) ? Occupant.TouristOnLeisure : Occupant.ResidentOnLeisure;
                        else if (AttendingMeetings.HasComponent(e))
                            kind = Occupant.OutingMember;
                        else
                            kind = Occupant.Other;
                        Occupants[li * Occupant.Kinds + kind]++;
                    }
                }
            }

            /// <summary>Meetings Traveling (MeetingStatus.cs (=)) to a snapshot sign or lodge. Their age is
            /// kept on the main thread (CoordinatedMeeting has no start time, CoordinatedMeeting.cs:8-14 (=)).</summary>
            private void Outings()
            {
                for (int c = 0; c < MeetingChunks.Length; c++)
                {
                    ArchetypeChunk chunk = MeetingChunks[c];
                    NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                    NativeArray<CoordinatedMeeting> meetings = chunk.GetNativeArray(ref MeetingType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        CoordinatedMeeting m = meetings[i];
                        if (m.m_Status != MeetingStatus.Traveling || m.m_Target == Entity.Null)
                            continue;
                        if (!AreaOfSign.ContainsKey(m.m_Target) && !LodgeIndex.ContainsKey(m.m_Target))
                            continue;
                        Traveling.Add(new TravelingOuting { Meeting = entities[i], Target = m.m_Target });
                    }
                }
            }
        }

        /// <summary>Measure only: per lodge, the walkers within SkiDayTiming.NearLodgeM and the game's own waiting count at the line
        /// stop points within that radius. A chunk scan, not a search-tree query: the Objects moving tree holds a creature only while its
        /// lane has no LaneObject buffer, and pedestrian lanes have one (Game.Creatures/ReferencesSystem.cs:125-138, PedestrianLane.cs:36
        /// (=)), so a radius query would miss every walker on a sidewalk. Walker kind: SkiDayTiming.WalkerKindOf; not drawn = Unspawned
        /// (a connection lane, or dead / carried: CreatureUtils.cs:1250-1265 (=)). Stop points: WaitingPassengers.m_Count, recounted by
        /// the game every 256 frames per line waypoint (WaitingPassengersSystem.cs:46-154, :252-254 (=)), at its Game.Routes.Position
        /// (taxi stands have none). "Ours": the citizen holds a claim (bit 1) or a state-4 claim (bit 2); a citizen can hold two claims,
        /// so the map is written with TryGetValue then the indexer, never Add. Scheduled after ClaimJob (it appends to Claims).</summary>
        [BurstCompile]
        private struct NearJob : IJob
        {
            [ReadOnly] public NativeList<ArchetypeChunk> WalkerChunks;   // m_WalkerQuery: Resident + Transform, None Deleted, Temp
            [ReadOnly] public NativeList<ArchetypeChunk> StopChunks;     // m_StopPointQuery: WaitingPassengers + Game.Routes.Position, None Deleted, Temp
            [ReadOnly] public ComponentTypeHandle<Game.Creatures.Resident> ResidentType;
            [ReadOnly] public ComponentTypeHandle<Game.Objects.Transform> TransformType;
            [ReadOnly] public ComponentTypeHandle<Game.Creatures.CurrentVehicle> CurrentVehicleType;
            [ReadOnly] public ComponentTypeHandle<Game.Creatures.Divert> DivertType;
            [ReadOnly] public ComponentTypeHandle<Game.Pathfind.PathOwner> PathOwnerType;
            [ReadOnly] public ComponentTypeHandle<Game.Objects.Unspawned> UnspawnedType;
            [ReadOnly] public ComponentTypeHandle<Game.Routes.WaitingPassengers> WaitingPassengersType;
            [ReadOnly] public ComponentTypeHandle<Game.Routes.Position> RoutePositionType;
            [ReadOnly] public NativeArray<LodgeJob> Lodges;
            [ReadOnly] public NativeArray<SiteOut> Sites;               // lodge xz from SiteJob, aligned with Lodges
            [ReadOnly] public NativeList<SkiDayClaim> Claims;           // scheduled after ClaimJob
            public NativeArray<NearOut> Out;                            // aligned with Lodges; zeroed by the job
            public NativeArray<int> Totals;                             // [0] walkers scanned, [1] line stop points scanned

            /// <summary>The "ours" map's value bits.</summary>
            private const byte OursClaim = 1, OursFollowed = 2;

            public void Execute()
            {
                int n = math.min(Lodges.Length, math.min(Sites.Length, Out.Length));
                for (int i = 0; i < Out.Length; i++)
                    Out[i] = new NearOut { NearestStopM = float.PositiveInfinity, HasPosition = (byte)(i < n && Sites[i].HasPosition != 0 ? 1 : 0) };
                for (int i = 0; i < Totals.Length; i++)
                    Totals[i] = 0;

                // The xz box of every lodge with a position, grown by the radius: a walker outside it is near no lodge.
                bool any = false;
                float2 lo = new float2(float.PositiveInfinity, float.PositiveInfinity);
                float2 hi = new float2(float.NegativeInfinity, float.NegativeInfinity);
                for (int li = 0; li < n; li++)
                {
                    if (Sites[li].HasPosition == 0)
                        continue;
                    lo = math.min(lo, Sites[li].Xz);
                    hi = math.max(hi, Sites[li].Xz);
                    any = true;
                }
                lo -= SkiDayTiming.NearLodgeM;
                hi += SkiDayTiming.NearLodgeM;

                NativeParallelHashMap<Entity, byte> ours = default;   // allocated at the first near walker, disposed below
                bool oursBuilt = false;
                for (int c = 0; c < WalkerChunks.Length; c++)
                {
                    ArchetypeChunk chunk = WalkerChunks[c];
                    if (Totals.Length > 0)
                        Totals[0] += chunk.Count;
                    if (!any)
                        continue;   // no lodge with a position: count only
                    NativeArray<Game.Creatures.Resident> residents = chunk.GetNativeArray(ref ResidentType);
                    NativeArray<Game.Objects.Transform> transforms = chunk.GetNativeArray(ref TransformType);
                    bool hasVehicle = chunk.Has(ref CurrentVehicleType);
                    bool hasDivert = chunk.Has(ref DivertType);
                    bool hasPath = chunk.Has(ref PathOwnerType);
                    bool notDrawn = chunk.Has(ref UnspawnedType);   // a tag: per chunk
                    NativeArray<Game.Creatures.CurrentVehicle> vehicles = hasVehicle ? chunk.GetNativeArray(ref CurrentVehicleType) : default;
                    NativeArray<Game.Creatures.Divert> diverts = hasDivert ? chunk.GetNativeArray(ref DivertType) : default;
                    NativeArray<Game.Pathfind.PathOwner> paths = hasPath ? chunk.GetNativeArray(ref PathOwnerType) : default;
                    for (int e = 0; e < residents.Length; e++)
                    {
                        float2 p = transforms[e].m_Position.xz;
                        if (!(p.x >= lo.x && p.x <= hi.x && p.y >= lo.y && p.y <= hi.y))
                            continue;   // also drops NaN
                        bool aboard = hasVehicle && vehicles[e].m_Vehicle != Entity.Null &&
                                      (vehicles[e].m_Flags & Game.Creatures.CreatureVehicleFlags.Ready) != 0;
                        bool divertFailed = hasDivert && diverts[e].m_Purpose == Game.Citizens.Purpose.PathFailed;
                        bool pathBad = hasPath && (paths[e].m_State & (Game.Pathfind.PathFlags.Failed | Game.Pathfind.PathFlags.Stuck)) != 0;
                        Game.Creatures.Resident resident = residents[e];
                        SkiDayTiming.WalkerKind kind = SkiDayTiming.WalkerKindOf(aboard, (uint)resident.m_Flags, divertFailed, pathBad);
                        if (kind == SkiDayTiming.WalkerKind.Skip)
                            continue;
                        byte ourBits = 0;
                        bool ourRead = false;
                        for (int li = 0; li < n; li++)
                        {
                            if (Sites[li].HasPosition == 0 || !SkiDayTiming.WithinXZ(p, Sites[li].Xz, SkiDayTiming.NearLodgeM))
                                continue;
                            if (!ourRead)
                            {
                                if (!oursBuilt)
                                {
                                    ours = new NativeParallelHashMap<Entity, byte>(math.max(1, Claims.Length), Allocator.Temp);
                                    BuildOurs(ref ours);
                                    oursBuilt = true;
                                }
                                ours.TryGetValue(resident.m_Citizen, out ourBits);
                                ourRead = true;
                            }
                            NearOut o = Out[li];
                            o.Walkers++;
                            if (notDrawn)
                                o.NotDrawn++;
                            if (kind == SkiDayTiming.WalkerKind.Waiting)
                            {
                                o.Waiting++;
                                o.WaitN++;
                                o.WaitTimerSum += resident.m_Timer;
                                o.WaitTimerMax = math.max(o.WaitTimerMax, resident.m_Timer);
                            }
                            else if (kind == SkiDayTiming.WalkerKind.FailedPath)
                                o.FailedPath++;
                            else if (kind == SkiDayTiming.WalkerKind.Hangaround)
                                o.Hangaround++;
                            if ((ourBits & OursClaim) != 0)
                            {
                                o.Ours++;
                                if (kind == SkiDayTiming.WalkerKind.Waiting)
                                {
                                    o.OursWaiting++;
                                    if ((ourBits & OursFollowed) != 0)
                                        o.OursWaitingFollowed++;
                                }
                                else if (kind == SkiDayTiming.WalkerKind.FailedPath)
                                    o.OursFailed++;
                            }
                            Out[li] = o;
                        }
                    }
                }
                if (oursBuilt)
                    ours.Dispose();

                for (int c = 0; c < StopChunks.Length; c++)
                {
                    ArchetypeChunk chunk = StopChunks[c];
                    if (Totals.Length > 1)
                        Totals[1] += chunk.Count;
                    if (!any)
                        continue;
                    NativeArray<Game.Routes.Position> positions = chunk.GetNativeArray(ref RoutePositionType);
                    NativeArray<Game.Routes.WaitingPassengers> waiting = chunk.GetNativeArray(ref WaitingPassengersType);
                    for (int e = 0; e < positions.Length; e++)
                    {
                        float2 p = positions[e].m_Position.xz;
                        for (int li = 0; li < n; li++)
                        {
                            if (Sites[li].HasPosition == 0)
                                continue;
                            NearOut o = Out[li];
                            o.NearestStopM = SkiDayRules.NearestOf(o.NearestStopM, SkiDayRules.DistanceXZ(p, Sites[li].Xz));   // NaN never wins
                            if (SkiDayTiming.WithinXZ(p, Sites[li].Xz, SkiDayTiming.NearLodgeM))
                            {
                                o.StopCount += waiting[e].m_Count;
                                o.StopPoints++;
                            }
                            Out[li] = o;
                        }
                    }
                }
            }

            /// <summary>Citizen → bits of every tracked claim (OursClaim; OursFollowed for state 4). Built at the first near
            /// walker only, so a scan with nobody near the lodges never walks the claims.</summary>
            private void BuildOurs(ref NativeParallelHashMap<Entity, byte> ours)
            {
                for (int i = 0; i < Claims.Length; i++)
                {
                    SkiDayClaim c = Claims[i];
                    byte bits = (byte)(OursClaim | (c.State == 4 ? OursFollowed : 0));
                    if (ours.TryGetValue(c.Citizen, out byte old))
                        bits |= old;
                    ours[c.Citizen] = bits;
                }
            }
        }
    }
}
