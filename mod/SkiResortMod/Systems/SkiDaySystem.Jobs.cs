// SkiDaySystem.Jobs — the four Burst jobs of SkiDaySystem and their blittable structs (Round S1 "Ski days";
// docs/research/ski-days/s1-spec.md §2.5, §3.2; ski-days spec ch2-system.md §1, §5, §6). Created 2026-10-05 by
// stream B. The system side (snapshot, scheduling, apply, logs, DUMP) is SkiDaySystem.cs; every demand, capacity,
// powder, confidence and gate rule is SkiDayRules' (ch1), every time helper SkiDayTiming's.
//
//   SiteJob    (at a snapshot rebuild and on the 4,096-frame backstop) — per lodge: its xz and the distance to the
//              nearest run outline (Game.Areas.Node polygon, the Geometry box as an exact skip) or lift station of
//              its ski area (D10; ch1 §3.2, s1-spec N5). Transform / Geometry / Node read through RO lookups: a job
//              edge after the move systems, never a main-thread read (ch2 §4.3).
//   ResolveJob (every pass) — per tracked claim: SkiDayTiming.Resolve → keep or drop; arrivals, failures, learning
//              (SkiDayRules.RecordOutcome / LearnSpeed); the claims on the way + on site per area (D21).
//   ClaimJob   (every pass while a ski area exists) — step A: lodge verdicts (SkiDayRules.CheckLodge), capacity,
//              area state; step B (an area Open, a knob on, under the cap): the candidate walk and the claim (D2):
//              Leisure.m_TargetAgent + m_LastPossibleFrame, one TripNeeded, and for a Car-class claim without a
//              parked car the CitizenBehaviorSystem car-reserve queue. Nothing else is written; no structural
//              change; no command buffer.
//   CensusJob  (first pass after a load, then each game hour) — tourist households (DUMP) and D30: lodge
//              occupants by kind and attraction outings still Traveling to a sign or lodge. Measure only.
//
// BURST RULES (s1-spec §1.2 rule 1, ch2-RF2-1): no managed type, string or exception. Every component read is a
// TryGetComponent / HasComponent / TryGetBuffer (a destroyed lodge or a citizen gone since the claim is a normal
// input: Release builds run without collection checks, so an unguarded lookup reads a dead chunk slot). Every
// index into a snapshot array is range-checked; area −1 (an orphan claim: its sign left the snapshot) writes only
// the orphan row; tracks are copy-modify-write (no ref into a NativeArray / NativeList element).
// ONE ACCESS MODE PER TYPE PER JOB (code-map §2.3): ClaimJob holds Leisure / TripNeeded as RW type handles and
// no lookup of them; ResolveJob and CensusJob read Leisure through a RO lookup, so CensusJob is scheduled after
// ClaimJob (SkiDaySystem.Schedule). ResolveJob's RO TripNeeded lookup (Round M1) follows the same rule: ClaimJob,
// which writes it, is scheduled after ResolveJob.
//
// ROUND M1 "measure first" (docs/research/ski-days/measure/m1-spec.md §1-§2; builder A, 2026-10-05): MEASURE ONLY.
// SkiDayClaim grows to 96 bytes (departure, ride, end and closest-approach frames, markers, the mode seen) and gains
// states 2 (visit over, still inside) and 3 (expired, watched for a late arrival), neither ever counted as on the
// way or on site. ResolveJob samples each claim (Sample), books failures by place, cause and marker, trip phases,
// visit end and exit, modes, distance bands and the car reserve into a per-area measure row (RM / RH, written only
// through MeasureWrite's range checks, m1-spec E4), keeps a 40-slot ring of ended ski days (ClaimRecord) and notes
// resident arrivals for the ledger check (ResidentNote). ClaimJob counts the decisions refused as too late to fit,
// the pool's chances and the sent distance bands (CM). No guest rule, learning input, hub view or vanilla write
// changes; every new lookup is [ReadOnly]. Fixer pass (docs/research/ski-days/measure/fixer.md): the departure rule
// also counts "in no building" (M1-ECS-2), "why" reads the first non-Leisure trip (R5), and RM.CarLeftFirstLook
// counts the queued car claims already gone at our first look (R4).

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
        // ── job structs (ch2 §1; blittable) ───────────────────────────────────────────────────────

        /// <summary>One tracked ski day (ch2 §6.1): the in-memory "citizen → claim" record, compacted every pass.
        /// Stores the sign, not an area index (indices change at a rebuild).
        /// State: 0 on the way, 1 on site, 2 visit over and still inside (M2: watched until the guest leaves the
        /// lodge, at most SkiDayTiming.ExitWatchFrames), 3 expired and watched for a late arrival (M1 F7: at most
        /// SkiDayTiming.LateWatchFrames). States 2 and 3 are measure only: never in OnTheWay, OnSite, ActiveNow or the
        /// hub view, and a late arrival feeds no learning (m1-spec §1.1). Round M1 grew it from 52 to 96 bytes (32,768
        /// at the cap: 3.1 MB); the Round M1 fields are valid when Flags say so (frames 0 otherwise).</summary>
        internal struct SkiDayClaim
        {
            public Entity Citizen, Lodge, Sign, Origin;
            public uint ClaimFrame, Deadline, ArrivalFrame;
            public uint DepartFrame, RideFirstFrame, RideLastFrame, EndFrame, Near500Frame;   // Round M1 (Near500Frame 0 = never)
            public float DistanceM, ClosestM;          // ClosestM: +∞ until a position was read (M1, F7)
            public float2 LodgeXz;                     // the lodge's xz at the claim (F7); NaN if it had none (never: NearestTarget)
            public ushort ClaimMinute, RidePasses;     // minute of the day at the claim (M5); passes seen in a vehicle (F6)
            public byte Class /*TravelClass*/, Kind /*GuestKind*/, State /*0..3, above*/, CarEnqueued;
            public byte Legs /*M3 SkiDayRules.Leg… bits*/, Kinds /*M3 SkiDayRules.Kind… bits*/, Flags /*ClaimFlag*/, Flags2 /*ClaimFlag2*/;
            public byte EndCause /*M2 SkiDayTiming.EndCause*/, TurnPurpose /*first non-Leisure TravelPurpose seen on the way*/,
                        GoneWhy /*F3 SkiDayTiming.GoneWhy*/, Place /*M1 SkiDayTiming.Place at the failure (state 3 keeps it)*/;
        }

        /// <summary>SkiDayClaim.Flags bits (Round M1; the values of SkiDayTiming.Flag…).</summary>
        private static class ClaimFlag
        {
            public const byte Departed = SkiDayTiming.FlagDeparted;                     // 1: left the origin (F4; SkiDayTiming.DepartedNow)
            public const byte HeldAtHome = SkiDayTiming.FlagHeldAtHome;                 // 2: car first seen held while still in the origin (E3; not proof it came in time for the route, R4)
            public const byte HeldEver = SkiDayTiming.FlagHeldEver;                     // 4: car held at some pass (queued car claims only, E2)
            public const byte Rode = SkiDayTiming.FlagRode;                             // 8: seen in a vehicle
            public const byte PathFailedAtStart = SkiDayTiming.FlagPathFailedAtStart;   // 16: the walker carried Divert{PathFailed}
            public const byte DeadlineIsClosing = SkiDayTiming.FlagDeadlineIsClosing;   // 32: the claim's deadline is the lifts' closing (F2)
            public const byte LateArrived = SkiDayTiming.FlagLateArrived;               // 64: arrived during the late watch (state 3)
        }

        /// <summary>SkiDayClaim.Flags2 bits (Round M1, F1; the values of SkiDayTiming.Flag2…).</summary>
        private static class ClaimFlag2
        {
            public const byte TurnedBack = SkiDayTiming.Flag2TurnedBack;                // 1: a non-Leisure TravelPurpose away from the origin
            public const byte WalkerLost = SkiDayTiming.Flag2WalkerLost;                // 2: in no building with no live walker
            public const byte WalkerPathFailed = SkiDayTiming.Flag2WalkerPathFailed;    // 4: walker PathOwner Failed or Stuck (transient)
            public const byte VehiclePathFailed = SkiDayTiming.Flag2VehiclePathFailed;  // 8: lead vehicle PathOwner Failed or Stuck (transient)
        }

        /// <summary>M5: one ended ski day, written by ResolveJob when its claim leaves the list (Gone and Retargeted at
        /// once, Expired at the end of its late watch, an arrived guest at its exit) into the 40-slot ring m_Ring. 80
        /// bytes. Frames 0 when the step did not happen.</summary>
        internal struct ClaimRecord
        {
            public Entity Citizen, Sign;
            public uint ClaimFrame, Deadline, DepartFrame, RideFirstFrame, RideLastFrame, ArrivalFrame, EndFrame, ExitFrame, Near500Frame;
            public float DistanceM, ClosestM;
            public ushort ClaimMinute, RidePasses;
            public byte Class, Kind, Outcome /*0 Arrived, 1 Gone, 2 Retargeted, 3 Expired*/, Place /*255 arrived*/, EndCause /*255 failed*/,
                        Exit /*SkiDayTiming.ExitWay; 255 failed*/, Legs, Kinds, Flags, Flags2, GoneWhy, TurnPurpose,
                        LateResult /*SkiDayTiming.LateResult; 255 = not an expired claim*/;
        }

        /// <summary>M7(a2): a resident's ski-day arrival (Kind 0) or visit end (Kind 1, with the guest-scan verdict),
        /// for the ledger check (city-wide; residents only).</summary>
        internal struct ResidentNote
        {
            public Entity Citizen;
            public uint ArrivalFrame;
            public byte Kind /*0 arrival, 1 visit end*/, Verdict /*SkiDayTiming.ScanVerdict at a visit end*/;
        }

        /// <summary>Range-checked writes into the measure rows (m1-spec E4: Release builds run Burst without
        /// collection checks, so an index past a row would write into a neighbour's memory). Burst-callable.</summary>
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

        /// <summary>Round M1 ResolveJob measure row (m_ResolveMeasure, one row per area + the orphan row): block bases,
        /// literal so a reader can check them (m1-spec §1.3 table). cls = travel class 0..2, kind 0..1, o = outcome
        /// (0 gone, 1 retargeted, 2 expired), band = SkiDayRules.DistanceBand, mode = SkiDayRules.ArrivalMode.</summary>
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
            public const int ArrivalClock = 322;     // 12: ClockBin at arrival
            public const int EndCause = 334;         // 8: EndCause (counts; mean stay hours in RH.EndStay)
            public const int Exit = 342;             // 88: endCause × 11 + ExitWay
            public const int StayBin = 430;          // 18: cls × 6 + StayBin
            public const int ModeArrived = 448;      // 15: cls × 5 + MainMode
            public const int TransitKind = 463;      // 6: bit index of Kinds (arrivals)
            public const int ModeFailed = 469;       // 15: cls × 5 + MainMode (departed failures only)
            public const int NeverLeftFailed = 484;  // 3: cls (failures that never departed)
            public const int ArrivedBand = 487;      // 15: cls × 5 + band
            public const int FailedBand = 502;       // 15: cls × 5 + band
            public const int Scan = 517;             // 6: kind × 3 + ScanVerdict
            public const int CarLeftFirstLook = 523; // 5: CarReserve bucket of queued car claims already departed at our first look (fixer R4)
            public const int Count = 528;
        }

        /// <summary>Round M1 ResolveJob hour sums (m_ResolveHours, same rows as RM): phase × 3 + cls (counts in
        /// RM.PhaseN), then the mean-stay sums per EndCause (counts in RM.EndCause).</summary>
        private static class RH
        {
            public const int EndStay = 39;   // 8: EndCause
            public const int Count = 47;
        }

        /// <summary>Round M1 ClaimJob measure row (m_ClaimMeasure, one row per area).</summary>
        private static class CM
        {
            public const int SentBand = 0;          // 15: cls × 5 + band
            public const int TooLateClosing = 15;   // 3: cls
            public const int TooLateOwn = 18;       // 3: cls
            public const int Count = 21;
        }

        /// <summary>One ski area of the snapshot (ch2 §4.2), in CopySigns order.</summary>
        private struct AreaJob
        {
            public Entity Sign;
            public EntityRef SignRef;
            public int LodgeStart, LodgeCount, RunStart, RunCount, StationStart, StationCount, Attraction, RunsCounted;
            public byte ScoreKnown, WriteHeld, NightRunOpen;
            public double Efficiency, RunsCapacity, OpenShare, Powder;   // Powder NaN = unknown (0 in the formulas)
        }

        /// <summary>One active lodge of the snapshot (the sign itself excluded); lodges of one area are contiguous.</summary>
        private struct LodgeJob
        {
            public Entity Lodge;
            public int Area;
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
            public int Pool, PoolTourists, Sent, SentWalk, SentCar, SentNoCar, SentTourists, CarsEnqueued,
                       CapacityPeople, TargetLodges;
            public byte State /*AreaState*/, Limit /*SkiDayLimit*/, ClaimHours, HasFirst, FirstClass, FirstKind;
            public uint ClosingFrame;
            public double FirstChance;
            public float FirstDistM;
            public Entity FirstLodge;
            // Round M1 (M4, F11): the sum of the chances of the decisions that entered the pool (mean = / Pool), and of
            // the chances the decisions refused as too late to fit would have had here ("expected ski days lost").
            public double PoolChanceSum, TooLateChanceSum;
        }

        /// <summary>ResolveJob's per-area outcome counters (one extra row for orphans, ch2 §5.4).</summary>
        private struct ResolveOut
        {
            public int OnTheWay, OnSite, ArrivedWalk, ArrivedCar, ArrivedNoCar, ArrivedTourists, Gone,
                       Retargeted, RetargetedAtOrigin, Expired, Left, ArrivedNoLeisure;
            public double HoursWalk, HoursCar, HoursNoCar, FirstHours;
            public byte HasFirst, FirstClass, FirstKind;
            // Round M1 per-pass snapshots (not sums): on the way and not departed, on the way and in a vehicle this
            // pass, state 2 (visit over, still inside), state 3 (expired, watched).
            public int NotLeft, Riding, Leaving, Watching;
        }

        /// <summary>D30: an attraction outing Traveling to a snapshot sign or lodge (CensusJob list b).</summary>
        private struct TravelingOuting
        {
            public Entity Meeting, Target;
        }

        /// <summary>m_PassStats indices (ch2 §5.5: "chunks, skipped LS slice, skipped CBS slice, citizens walked,
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

        /// <summary>m_Census indices (ch2 §5.6 and the DUMP's tourists line).</summary>
        private static class Census
        {
            public const int Parties = 0, People = 1, AtEdge = 2, Hotel = 3, InHotel = 4, OtherLodging = 5, NoHotel = 6,
                             OutingUsed = 7, WithCar = 8, LodgingSeekers = 9, MovingAway = 10, Ran = 11;
            public const int Count = 12;
        }

        /// <summary>D30 occupant kinds, m_Occupants[lodge × Kinds + kind] (first match in this order).</summary>
        private static class Occupant
        {
            public const int Worker = 0, ResidentOnLeisure = 1, TouristOnLeisure = 2, OutingMember = 3, Other = 4;
            public const int Kinds = 5;
        }

        // ── SiteJob (ch2 §5.3) ────────────────────────────────────────────────────────────────────

        /// <summary>Per lodge: position and the distance to the nearest run outline or lift station of its own ski
        /// area (D10). A run's box distance is never larger than its polygon distance, so a run whose box is not
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
                        // nearest edge (ch1 §3.2; refutation ch1-R-3).
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

        // ── ResolveJob (ch2 §5.4, §6) ─────────────────────────────────────────────────────────────

        /// <summary>Every tracked claim, every pass: outcome, counters, learning, compaction; then the claims on
        /// the way + on site per area (ActiveNow, the room count of D21). Every index checked (ch2-RF2-1).
        /// Round M1 (m1-spec §2, measure only): per claim, in this order — gone first (the citizen entity no longer
        /// exists or is being deleted); a state-2 claim goes to the exit check and nothing else; a state-0 or state-3
        /// claim that is not gone is sampled (Sample: car reserve, turned back, departure, walker and vehicle, closest
        /// approach); a state-3 claim goes to the late check and nothing else; then Resolve and the outcome switch as
        /// before, plus the measure rows (failure place, markers, why, phases, stay, end cause, exit, modes, bands,
        /// car reserve, scan verdict), the ring of ended ski days and the resident notes. The learning (RecordOutcome,
        /// LearnSpeed, LodgeCarFails) and every count the hub or a guest rule reads are exactly as before: states 2 and
        /// 3 are never in OnTheWay, OnSite or ActiveNow.</summary>
        [BurstCompile]
        private struct ResolveJob : IJob
        {
            [ReadOnly] public ComponentLookup<Leisure> Leisures;
            [ReadOnly] public ComponentLookup<CurrentBuilding> CurrentBuildings;
            [ReadOnly] public NativeParallelHashMap<Entity, int> AreaOfSign;
            [ReadOnly] public NativeParallelHashMap<Entity, int> LodgeIndex;
            // Round M1 (m1-spec §1.2): read-only lookups, every read a TryGet / Has. A new type is fully qualified
            // (no new using: Game.Prefabs and Game.Vehicles both have Taxi and PublicTransport).
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
            public NativeList<SkiDayClaim> Claims;
            public NativeArray<SkiDayRules.ClassTrack> Tracks;   // areas × 3
            public NativeArray<ResolveOut> Out;                  // areas + 1: the last row is the orphan row
            public NativeArray<int> ActiveNow;                   // areas
            public NativeArray<int> LodgeCarFails;               // lodges (this pass)
            // Round M1 writable outputs (the only ones besides the above): measure rows (zeroed here), the ring of
            // ended ski days and its state ([0] next slot, [1] records this load; cleared at load), resident notes
            // (cleared by the system before scheduling).
            public NativeArray<int> Measure;                     // (areas + 1) × RM.Count
            public NativeArray<double> Hours;                    // (areas + 1) × RH.Count
            public NativeArray<ClaimRecord> Ring;                // Tuning.RingSize
            public NativeArray<int> RingState;                   // 2
            public NativeList<ResidentNote> Notes;
            public uint Now;
            public float TimeOfDay;                              // TimeSystem.normalizedTime (as ClaimJob gets it)
            public byte ScanKnown;                               // the guest counter is armed and has scanned this load
            public uint ScanFrame, ScanInterval, PassInterval;   // its last leisure scan, its scan interval, ours

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
                for (int i = 0; i < Claims.Length; i++)
                {
                    SkiDayClaim c = Claims[i];
                    int a = AreaOfSign.TryGetValue(c.Sign, out int ai) && ai >= 0 && ai < orphan ? ai : -1;   // −1 → orphan
                    int ti = SkiDayTiming.TrackIndex(a, c.Class);
                    if (ti >= Tracks.Length)
                        ti = -1;
                    int r = a >= 0 ? a : orphan;   // outcome counters only
                    int mb = measureRows > 0 ? math.min(r, measureRows - 1) * RM.Count : NoRow;   // E4
                    int hb = hourRows > 0 ? math.min(r, hourRows - 1) * RH.Count : NoRow;
                    int cls = math.min((int)c.Class, 2), kind = math.min((int)c.Kind, 1);

                    bool gone = !Citizens.HasComponent(c.Citizen) || Deleteds.HasComponent(c.Citizen);
                    bool hasL = Leisures.TryGetComponent(c.Citizen, out Leisure l);
                    bool hasCb = CurrentBuildings.TryGetComponent(c.Citizen, out CurrentBuilding cb);
                    bool at = hasCb && cb.m_CurrentBuilding == c.Lodge;
                    bool inBuilding = hasCb && cb.m_CurrentBuilding != Entity.Null;
                    bool atOrigin = inBuilding && cb.m_CurrentBuilding == c.Origin;
                    bool hasTp = TravelPurposes.TryGetComponent(c.Citizen, out TravelPurpose tp);
                    byte purpose = hasTp ? (byte)tp.m_Purpose : (byte)0;
                    ResolveOut ro = Out[r];

                    if (c.State == 2)
                    {
                        // M2 exit watch: the visit is over; is the guest out of the lodge yet?
                        if (Exit(ref c, mb, hb, cls, gone, at, hasTp, purpose))
                        {
                            Out[r] = ro;
                            continue;   // recorded and dropped
                        }
                        ro.Leaving++;
                        Out[r] = ro;
                        Claims[w++] = c;
                        continue;
                    }
                    bool riding = false;
                    if (!gone && (c.State == 0 || c.State == 3))
                        riding = Sample(ref c, mb, hb, cls, inBuilding, atOrigin, at, hasTp, purpose);   // E1: never for a gone citizen
                    if (c.State == 3)
                    {
                        // M1 F7 late watch: an expired claim, measure only (the learning booked it at the expiry).
                        if (SkiDayTiming.TryLate(gone, at, hasL, hasL ? l.m_LastPossibleFrame : 0u, c.Deadline, Now, out SkiDayTiming.LateResult late))
                        {
                            MeasureWrite.Bump(Measure, mb + RM.LateWatch + cls * SkiDayTiming.LateResultCount + (int)late);
                            if (late == SkiDayTiming.LateResult.ArrivedLate)
                            {
                                c.Flags |= ClaimFlag.LateArrived;
                                c.ArrivalFrame = Now;
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

                    SkiDayTiming.ClaimOutcome o = SkiDayTiming.Resolve(c.State == 1, hasL, hasL && l.m_TargetAgent == c.Lodge, at, Now, c.Deadline);
                    bool keep = false, failure = false;
                    switch (o)
                    {
                        case SkiDayTiming.ClaimOutcome.OnTheWay:
                            keep = true;
                            ro.OnTheWay++;
                            if ((c.Flags & ClaimFlag.Departed) == 0)
                                ro.NotLeft++;
                            if (riding)
                                ro.Riding++;
                            break;
                        case SkiDayTiming.ClaimOutcome.Arrived:
                        {
                            keep = true;
                            c.State = 1;
                            c.ArrivalFrame = Now;
                            ro.OnSite++;
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
                                SkiDayRules.LearnSpeed(ref t, (SkiDayRules.TravelClass)c.Class, c.DistanceM, c.ClaimFrame, Now);
                                Tracks[ti] = t;
                            }
                            Arrival(ref c, mb, hb, cls);   // Round M1, measure only
                            break;
                        }
                        case SkiDayTiming.ClaimOutcome.OnSite:
                            keep = true;
                            ro.OnSite++;
                            break;
                        case SkiDayTiming.ClaimOutcome.Left:
                            ro.Left++;   // a finished day, not an outcome
                            // M2: why the visit ended; then the exit at once (a guest already outside) or state 2.
                            if (!VisitEnd(ref c, mb, hb, cls, kind, gone, hasL, hasL && l.m_TargetAgent == c.Lodge, at, hasTp, purpose))
                            {
                                c.State = 2;
                                keep = true;
                            }
                            break;
                        case SkiDayTiming.ClaimOutcome.Gone:
                            failure = true;
                            ro.Gone++;
                            if (at)
                                ro.ArrivedNoLeisure++;
                            Failure(ref c, mb, hb, cls, kind, 0, gone, inBuilding, atOrigin, at, hasTp, purpose);
                            break;
                        case SkiDayTiming.ClaimOutcome.Retargeted:
                            failure = true;
                            ro.Retargeted++;
                            if (hasCb && cb.m_CurrentBuilding == c.Origin)
                                ro.RetargetedAtOrigin++;
                            Failure(ref c, mb, hb, cls, kind, 1, gone, inBuilding, atOrigin, at, hasTp, purpose);
                            break;
                        default:   // Expired
                            failure = true;
                            ro.Expired++;
                            Failure(ref c, mb, hb, cls, kind, 2, gone, inBuilding, atOrigin, at, hasTp, purpose);
                            c.State = 3;   // M1 F7: watched for a late arrival (measure only; the ring record is written at its end)
                            keep = true;
                            break;
                    }
                    Out[r] = ro;

                    if (failure)
                    {
                        // D15: a claim whose Leisure lost the lodge without arriving, or whose deadline passed.
                        if (ti >= 0)
                        {
                            SkiDayRules.ClassTrack t = Tracks[ti];
                            SkiDayRules.RecordOutcome(ref t, false);
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

            // ── Round M1 (m1-spec §2; measure only) ────────────────────────────────────────────────

            /// <summary>The per-pass sample of a claim on the way (state 0) or watched after its expiry (state 3); never
            /// for a gone citizen (E1). Returns whether the guest sits in a vehicle this pass.
            /// 1 car reserve (M4: queued car claims only, E2; "held while still at home" means held before the walker
            /// spawned, E3, NOT in time for the route: TripNeededSystem asks for a car route only if a held car is parked,
            /// about 16 frames after the claim, and our first look is 64 frames after it, arrival-modes.md §1 row 3; fixer
            /// R4); 2 turned back (F1a: a non-Leisure TravelPurpose away from the origin — GoingHome is
            /// ResidentAISystem's ReturnHome after a failed re-path, 1.6.2 :1344-1358); 3 departure (F4 and fixer
            /// M1-ECS-2, SkiDayTiming.DepartedNow: out of the origin and in no building, or with TravelPurpose Leisure, or
            /// in the lodge: a walker was spawned, path failed or not);
            /// 4 walker and vehicle, read ONLY while the citizen is in no building (E7: after an arrival the creature can
            /// live on and be reused for the next trip, 1.6.2 ResidentAISystem.cs:2287-2309): no live walker → walker
            /// lost (F1b: a failed car path leaves the citizen in no building with no walker, :1658-1661, :1284-1289);
            /// Divert{PathFailed} on the walker → path failed at the start (TripNeededSystem.cs:681-686); PathOwner
            /// Failed or Stuck on the walker or the lead vehicle → walker / vehicle path failed (transient, open point 4);
            /// the ride (CurrentVehicle with Ready: vehicles hold boarding until every passenger is Ready, so a 64-frame
            /// sample cannot miss a ride, arrival-modes Verification row 6) with its leg and transit kind (M3); the
            /// closest approach to the lodge (F7) from the lead vehicle or the walker.</summary>
            private bool Sample(ref SkiDayClaim c, int mb, int hb, int cls, bool inBuilding, bool atOrigin, bool at, bool hasTp, byte purpose)
            {
                if (c.Class == (byte)SkiDayRules.TravelClass.Car && c.CarEnqueued == 1 && (c.Flags & ClaimFlag.HeldEver) == 0 &&
                    CarKeepers.TryGetEnabledComponent(c.Citizen, out CarKeeper k) && k.m_Car != Entity.Null)
                {
                    c.Flags |= ClaimFlag.HeldEver;
                    if (atOrigin && (c.Flags & ClaimFlag.Departed) == 0)
                        c.Flags |= ClaimFlag.HeldAtHome;
                }
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
                if (Diverts.TryGetComponent(walker, out Game.Creatures.Divert divert) && divert.m_Purpose == Game.Citizens.Purpose.PathFailed)
                    c.Flags |= ClaimFlag.PathFailedAtStart;
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

            /// <summary>M3 transit kind: the boarded car's prefab TransportType, else the Controller's prefab (a modded
            /// transit car without the data; arrival-modes Verification Missed 5), else −1 (Other).</summary>
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

            /// <summary>M1-M4 at an arrival (after the existing counters and the learning): the departure if the sample
            /// missed it (a trip shorter than a pass), the trip phases (all over the same arrivals, F6), the hours left
            /// before the deadline (F2), the arrival clock, the markers, the mode seen and transit kinds, the distance
            /// band, the car reserve, and a resident note for the ledger check (M7 a2).</summary>
            private void Arrival(ref SkiDayClaim c, int mb, int hb, int cls)
            {
                if ((c.Flags & ClaimFlag.Departed) == 0)
                {
                    c.Flags |= ClaimFlag.Departed;
                    c.DepartFrame = Now;
                    PhaseHours(mb, hb, SkiDayTiming.Phase.ClaimToLeaveAll, cls, Now, c.ClaimFrame);
                }
                PhaseHours(mb, hb, SkiDayTiming.Phase.ClaimToLeaveArrived, cls, c.DepartFrame, c.ClaimFrame);
                PhaseHours(mb, hb, SkiDayTiming.Phase.LeaveToArrive, cls, Now, c.DepartFrame);
                if ((c.Flags & ClaimFlag.Rode) != 0)
                {
                    PhaseHours(mb, hb, SkiDayTiming.Phase.LeaveToRide, cls, c.RideFirstFrame, c.DepartFrame);
                    PhaseHours(mb, hb, SkiDayTiming.Phase.RideSpan, cls, c.RideLastFrame, c.RideFirstFrame);
                    PhaseHours(mb, hb, SkiDayTiming.Phase.RideToArrive, cls, Now, c.RideLastFrame);
                    int seen = SkiDayTiming.Phase.RideSeen * 3 + cls;
                    MeasureWrite.AddHours(Hours, Measure, mb + RM.PhaseN + seen, hb + seen, SkiDayTiming.HoursFor((uint)c.RidePasses * PassInterval));
                }
                PhaseHours(mb, hb, SkiDayTiming.Phase.HoursLeftAtArrival, cls, c.Deadline, Now);   // 0 when past (F2)
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

            /// <summary>M2 at the Left verdict (ro.Left is counted by the caller as before): why the visit ended
            /// (EndCauseOf; F2 split), the stay (mean per class and per cause, histogram, F5), the guest-scan verdict
            /// (M7a: the counter's "there" test is ours, SkiGuestCounterSystem.cs:1237-1255) and a resident note; then
            /// the exit check at once. Returns true when the guest is already out (recorded; drop the claim).</summary>
            private bool VisitEnd(ref SkiDayClaim c, int mb, int hb, int cls, int kind, bool gone, bool hasL, bool leisureAtLodge, bool at,
                                  bool hasTp, byte purpose)
            {
                c.EndFrame = Now;
                int counter = Citizens.TryGetComponent(c.Citizen, out Citizen z) ? z.m_LeisureCounter : 0;
                SkiDayTiming.EndCause cause = SkiDayTiming.EndCauseOf(gone, hasL, leisureAtLodge, at, Now, c.Deadline,
                                                                      (c.Flags & ClaimFlag.DeadlineIsClosing) != 0, counter);
                c.EndCause = (byte)cause;
                double stay = HoursBetween(Now, c.ArrivalFrame);
                MeasureWrite.AddHours(Hours, Measure, mb + RM.EndCause + (int)cause, hb + RH.EndStay + (int)cause, stay);
                int st = SkiDayTiming.Phase.Stay * 3 + cls;
                MeasureWrite.AddHours(Hours, Measure, mb + RM.PhaseN + st, hb + st, stay);
                MeasureWrite.Bump(Measure, mb + RM.StayBin + cls * 6 + SkiDayTiming.StayBin(stay));
                uint lastThere = cause == SkiDayTiming.EndCause.PastGrace ? Now : Now - PassInterval;
                SkiDayTiming.ScanVerdict v = SkiDayTiming.GuestScanSaw(c.ArrivalFrame, lastThere, Now, PassInterval, ScanKnown != 0, ScanFrame, ScanInterval);
                MeasureWrite.Bump(Measure, mb + RM.Scan + kind * SkiDayTiming.ScanVerdictCount + (int)v);
                if (c.Kind == (byte)SkiDayRules.GuestKind.Resident)
                    Notes.Add(new ResidentNote { Citizen = c.Citizen, ArrivalFrame = c.ArrivalFrame, Kind = 1, Verdict = (byte)v });
                return Exit(ref c, mb, hb, cls, gone, at, hasTp, purpose);
            }

            /// <summary>M2 exit (state 2, and once at the visit end): out of the lodge (the TravelPurpose of the trip
            /// out), deleted, or still inside after ExitWatchFrames → Exit[endCause × 11 + way] (F5: the pair), the
            /// hours from visit end to exit, and the ring record. False: keep watching.</summary>
            private bool Exit(ref SkiDayClaim c, int mb, int hb, int cls, bool gone, bool at, bool hasTp, byte purpose)
            {
                if (!SkiDayTiming.TryExit(gone, at, hasTp, purpose, Now, c.EndFrame, out SkiDayTiming.ExitWay way))
                    return false;
                int cause = math.min((int)c.EndCause, SkiDayTiming.EndCauseCount - 1);
                MeasureWrite.Bump(Measure, mb + RM.Exit + cause * SkiDayTiming.ExitWayCount + (int)way);
                if (way != SkiDayTiming.ExitWay.StillInside && way != SkiDayTiming.ExitWay.Deleted)
                    PhaseHours(mb, hb, SkiDayTiming.Phase.EndToExit, cls, Now, c.EndFrame);
                Record(c, 0, 255, (byte)way, Now, 255);
                return true;
            }

            /// <summary>M1-M4 at a failure (o 0 gone, 1 retargeted, 2 expired; after the existing counters, before the
            /// learning): where the citizen stood, the markers, why Leisure went (F3: CitizenBehaviorSystem removes
            /// Leisure only for a citizen in a building without TravelPurpose, 1.6.2 :1149, and leaves these traces:
            /// HealthProblem :756-760, household MovingAway :698-703, TravelPurpose Sleeping :329-341, ResourceBuyer
            /// :417-432; LeisureSystem and DeathCheckSystem are the other removers), the clock bin, the bound of an
            /// expiry (F2), the closest approach (F7), the hours on the road and the mode seen (departed) or "never left"
            /// (F12), the car reserve and the distance band. Gone and Retargeted are recorded at once; an expiry is
            /// recorded when its late watch ends.</summary>
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
                    // The first trip that is not Leisure (fixer R5): a claimed citizen who never left still holds our own
                    // Leisure trip at index 0, so a work, shopping or home trip queued after it would be missed.
                    bool hasTrip = TripNeededs.TryGetBuffer(c.Citizen, out DynamicBuffer<TripNeeded> trips) && trips.Length > 0;
                    byte trip = hasTrip ? (byte)trips[0].m_Purpose : (byte)0;
                    for (int t = 1; hasTrip && trip == SkiDayTiming.PurposeLeisure && t < trips.Length; t++)
                        trip = (byte)trips[t].m_Purpose;
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

            /// <summary>M4 car reserve × mode, counted once when a QUEUED car claim resolves (arrival or failure; E2).
            /// Invariant: the block's sum = the queued car claims that resolved this month (not CarsEnqueued, which counts
            /// claims made). Fixer R4: a claim that had already left at our first look (one pass after the claim) is
            /// counted again in RM.CarLeftFirstLook by bucket: for those the sample cannot tell "the car came in time" from
            /// "the car came too late", so the size of that ambiguous group is known.</summary>
            private void CarReserveAtEnd(in SkiDayClaim c, int mb)
            {
                if (c.Class != (byte)SkiDayRules.TravelClass.Car || c.CarEnqueued != 1)
                    return;
                bool departed = (c.Flags & ClaimFlag.Departed) != 0;
                SkiDayTiming.CarReserve bucket = SkiDayTiming.CarReserveOf(departed, (c.Flags & ClaimFlag.HeldEver) != 0,
                                                                          (c.Flags & ClaimFlag.HeldAtHome) != 0);
                MeasureWrite.Bump(Measure, mb + RM.CarReserve + (int)bucket * SkiDayRules.ArrivalModes + (int)SkiDayRules.MainMode(c.Legs));   // bucket × 5 + mode
                if (departed && c.DepartFrame - c.ClaimFrame <= PassInterval)
                    MeasureWrite.Bump(Measure, mb + RM.CarLeftFirstLook + (int)bucket);
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

            /// <summary>M5: one ended ski day into the ring (guards §1.3: a ring and its state exist, the slot is in range).</summary>
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
                };
                RingState[0] = (slot + 1) % Ring.Length;
                RingState[1] = RingState[1] + 1;
            }
        }

        // ── ClaimJob (ch2 §5.5) ───────────────────────────────────────────────────────────────────

        /// <summary>Step A (always): lodge verdicts, capacity, area state. Step B (an area Open, a knob on, under
        /// ClaimsCap): the candidate walk over m_CandidateQuery's chunks and the claim (D2).</summary>
        [BurstCompile]
        private struct ClaimJob : IJob
        {
            // m_CandidateQuery's chunks and type handles. Leisure and TripNeeded are the claim's two writes (D2).
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

            [ReadOnly] public NativeArray<AreaJob> Areas;
            [ReadOnly] public NativeArray<LodgeJob> Lodges;
            [ReadOnly] public NativeArray<SiteOut> Sites;   // SiteJob's result, aligned with Lodges
            [ReadOnly] public NativeArray<SkiDayRules.ClassTrack> Tracks;
            [ReadOnly] public NativeArray<int> ActiveNow;
            public NativeArray<byte> LodgeReject;   // SkiDayRules.LodgeReject, aligned with Lodges
            public NativeArray<ClaimOut> Out;       // areas
            public NativeList<SkiDayClaim> Claims;
            public NativeArray<int> PassStats;      // zeroed by the system before scheduling
            public NativeArray<int> Measure;        // Round M1: areas × CM.Count, zeroed by the system before scheduling
            public NativeQueue<Entity>.ParallelWriter CarReserve;

            // pass inputs (ch2 §4.5)
            public uint Now;
            public float TimeOfDay;
            public int DayKey;
            public SkiDayRules.Knobs Knobs;
            public int Price;
            public int LeisureInterval, BehaviorInterval, BehaviorOffset;

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

            /// <summary>Step A: lodges (D8, D10; ch2-RF2-1, RF2-15: every read guarded, live state), then per area
            /// the target lodges, capacity (D21), claim hours and closing (D20) and the area state (D9). Returns
            /// whether any area is Open.</summary>
            private bool StepA(int areaCount)
            {
                int lodges = math.min(Lodges.Length, LodgeReject.Length);
                for (int li = 0; li < lodges; li++)
                {
                    Entity l = Lodges[li].Lodge;
                    bool hasB = Buildings.TryGetComponent(l, out Building b);
                    bool hasSite = li < Sites.Length && Sites[li].HasPosition != 0;
                    var facts = new SkiDayRules.LodgeFacts
                    {
                        IsLodgePrefab = true,   // both from CopyActiveLodges (lodge prefab, installed on a live sign)
                        OnLiveSign = true,
                        Active = hasB && !BuildingUtils.CheckOption(b, BuildingOption.Inactive),   // BuildingUtils.cs:351 (=)
                        Destroyed = Destroyeds.HasComponent(l) || Deleteds.HasComponent(l) || !hasB,
                        LeisureTagged = LeisureProviders.HasComponent(l),   // the searchable tag a TO gate removes
                        HasRoad = hasB && b.m_RoadEdge != Entity.Null,
                        NearestRunOrStationM = hasSite ? Sites[li].NearestRunOrStationM : float.PositiveInfinity,
                    };
                    LodgeReject[li] = (byte)SkiDayRules.CheckLodge(facts);
                }

                bool anyOpen = false;
                for (int a = 0; a < areaCount; a++)
                {
                    AreaJob aj = Areas[a];
                    int target = 0;
                    int end = math.min(aj.LodgeStart + aj.LodgeCount, lodges);
                    for (int li = math.max(0, aj.LodgeStart); li < end; li++)
                    {
                        if (LodgeReject[li] == (byte)SkiDayRules.LodgeReject.None)
                            target++;
                    }
                    bool night = aj.NightRunOpen != 0;
                    double capacity = SkiDayRules.Capacity(aj.RunsCapacity, target, aj.Powder, out SkiDayLimit limit);
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
                    Out[a] = new ClaimOut
                    {
                        CapacityPeople = capacityPeople,
                        TargetLodges = target,
                        State = (byte)state,
                        Limit = (byte)limit,
                        ClaimHours = (byte)(hours ? 1 : 0),
                        ClosingFrame = hasClosing ? closing : 0u,
                    };
                    if (state == SkiDayRules.AreaState.Open)
                        anyOpen = true;
                }
                return anyOpen;
            }

            /// <summary>Step B: chunks not skipped (SkiDayTiming.SkipChunk, D3 + V2); per citizen the cheap tests
            /// first (CheckCitizen, D5-D7), shopping (V3), the origin, the car facts (D11-D13), the best area (D14,
            /// D16-D18, V4), one roll (D3, V7), the claim (D2) and the room countdown (D21).</summary>
            private void StepB(int areaCount, NativeArray<int> active, NativeParallelHashMap<Entity, int> promised)
            {
                uint leisureSlice = SkiDayRules.SliceOf(Now, LeisureInterval);
                // A decision is counted in the pools once: at its claim or at its LAST UNSKIPPED sighting before
                // LeisureSystem takes it. The pass one interval before that visit can itself skip the slice (the recent
                // CBS window, V2): then an earlier pass is the last one that sees it (fixer, review SIM-1;
                // SkiDayTiming.IsLastSighting). Once per pass for the 16 slices, not per chunk.
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
                    uint decisionFrame = SkiDayTiming.NextLeisureVisit(slice, Now, LeisureInterval);   // V7 roll key
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
                // AttendingMeeting) and Ill (HealthProblem) are archetype exclusions of m_CandidateQuery (ch2 §5.1):
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
                    // Test 2 (D6, D7): household lookups; AtHome = the home rule (CitizenBehaviorSystem.cs:704-711, 1.6.2
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

                // Test 3 (V3): adults and elderly shop first when the household has a need (CitizenBehaviorSystem.cs
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

                // Car facts (D11-D13). FreeCars = HouseholdBehaviorSystem.GetFreeCar's test (:502-518, 1.6.2 :504-520)
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

                // The best area (D18): one roll against the best area's chance only.
                int bestA = -1, bestLi = -1;
                byte bestCls = 0;
                double bestP = 0.0, bestD = double.PositiveInfinity;
                EntityRef bestSign = default;
                bool leaving = false;
                for (int a = 0; a < areaCount; a++)
                {
                    ClaimOut co = Out[a];
                    if (co.State != (byte)SkiDayRules.AreaState.Open)
                        continue;
                    if ((who == SkiDayRules.GuestKind.Resident ? Knobs.LocalsPct : Knobs.TouristsPct) <= 0)
                        continue;   // D18: a knob at 0 turns its kind off
                    if (tourist && th.m_LeavingTime != 0 && th.m_LeavingTime <= co.ClosingFrame)
                    {
                        leaving = true;   // V4: a Tourism Overhaul stay that ends before closing
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
                    double hours = SkiDayRules.TravelHours(d, t.SpeedKmPerHour);
                    if (!SkiDayRules.ClaimFits(Now, leisure.m_LastPossibleFrame, co.ClosingFrame, hours))
                    {
                        if (last)
                            TooLate(a, cls, who, aj, d, hours, leisure.m_LastPossibleFrame, co.ClosingFrame, t);   // Round M1 M4, measure only
                        continue;
                    }
                    double p = SkiDayRules.Chance(who, aj.Attraction, d / 1000.0, aj.OpenShare, aj.Powder, Price,
                                                  SkiDayRules.Confidence(cls, t), Knobs);
                    if (!SkiDayRules.IsBetter(p, d, aj.SignRef, bestP, bestD, bestSign))
                        continue;
                    bestA = a;
                    bestLi = li;
                    bestCls = (byte)cls;
                    bestP = p;
                    bestD = d;
                    bestSign = aj.SignRef;
                }
                if (leaving && last)
                    PassStats[Stat.LeavingToday]++;
                if (bestA < 0 || bestLi < 0 || bestLi >= Lodges.Length)
                    return false;

                // One roll per decision (D3): residents keyed on the LeisureSystem visit that takes this decision
                // (V7), tourists once per party per game day.
                double roll = who == SkiDayRules.GuestKind.Resident
                    ? SkiDayRules.ResidentRoll(e.Index, e.Version, decisionFrame)
                    : SkiDayRules.TouristRoll(hh.Index, hh.Version, DayKey);
                ClaimOut won = Out[bestA];
                if (!SkiDayRules.Hit(roll, bestP))
                {
                    if (last)
                    {
                        won.Pool++;   // a decision enters a pool once: at its claim or at its last sighting
                        won.PoolChanceSum += bestP;   // Round M1 M4: the pool's mean chance
                        if (tourist)
                            won.PoolTourists++;
                        Out[bestA] = won;
                    }
                    return false;
                }

                // The claim (D2): LeisureSystem's own two writes (LeisureSystem.cs:398-405, 1.6.2 :392-399), the
                // deadline = min(vanilla's, closing) (D20); nothing else; no structural change.
                Entity lodge = Lodges[bestLi].Lodge;
                uint deadline = SkiDayRules.ClaimDeadline(leisure.m_LastPossibleFrame, won.ClosingFrame);
                leisure.m_TargetAgent = lodge;
                leisure.m_LastPossibleFrame = deadline;
                leisures[index] = leisure;
                trip.Add(new TripNeeded { m_TargetAgent = lodge, m_Purpose = Game.Citizens.Purpose.Leisure, m_Priority = 128 });
                // V6: the car-reserve queue only for a Car-class claim without a parked car (WorkerSystem's handshake,
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
                    active[bestA]++;   // in-job countdown: one pass cannot overshoot (D21)
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
                    // Round M1 (m1-spec §1.1; every other new field starts at 0): the clock at the claim (M5), the lodge's
                    // position for the closest approach (F7; NearestTarget only picks a lodge with one, so NaN is never
                    // stored in practice), and what bound the deadline (F2; a tie counts as closing).
                    ClaimMinute = (ushort)SkiDayTiming.MinuteOfDay(TimeOfDay),
                    ClosestM = float.PositiveInfinity,
                    LodgeXz = bestLi < Sites.Length && Sites[bestLi].HasPosition != 0 ? Sites[bestLi].Xz : new float2(float.NaN, float.NaN),
                    Flags = deadline == won.ClosingFrame ? ClaimFlag.DeadlineIsClosing : (byte)0,
                });
                won.Pool++;
                won.PoolChanceSum += bestP;   // Round M1 M4
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

            /// <summary>Round M1 M4 (measure only; the decision is refused exactly as before): a decision that passed
            /// reach, room and class at area <paramref name="a"/> but whose trip + 2 hours does not fit, counted once at its
            /// last sighting (as the pool counts it) by the deadline that is too early (FitVerdict) and class, with the chance
            /// it would have had here (F11: the same Chance call as an accepted decision). A decision refused here and sent
            /// to another area counts here too, so "expected ski days lost" is an upper bound (open point 11).</summary>
            private void TooLate(int a, SkiDayRules.TravelClass cls, SkiDayRules.GuestKind who, in AreaJob aj, float d, double hours,
                                 uint vanillaDeadline, uint closingFrame, in SkiDayRules.ClassTrack t)
            {
                SkiDayRules.Fit fit = SkiDayRules.FitVerdict(Now, vanillaDeadline, closingFrame, hours);
                MeasureWrite.Bump(Measure, a * CM.Count + (fit == SkiDayRules.Fit.NoTimeBeforeOwnDeadline ? CM.TooLateOwn : CM.TooLateClosing) + (int)cls);
                ClaimOut co = Out[a];
                co.TooLateChanceSum += SkiDayRules.Chance(who, aj.Attraction, d / 1000.0, aj.OpenShare, aj.Powder, Price,
                                                          SkiDayRules.Confidence(cls, t), Knobs);
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

            /// <summary>The nearest target lodge (LodgeReject.None, with a position) of the area from the origin
            /// (straight line, xz); −1 if none.</summary>
            private int NearestTarget(AreaJob aj, float2 origin, out float distance)
            {
                distance = float.PositiveInfinity;
                int best = -1;
                int end = math.min(aj.LodgeStart + aj.LodgeCount, math.min(LodgeReject.Length, Sites.Length));
                for (int li = math.max(0, aj.LodgeStart); li < end; li++)
                {
                    if (LodgeReject[li] != (byte)SkiDayRules.LodgeReject.None)
                        continue;
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

        // ── CensusJob (ch2 §5.6; D30, s1-spec N11) ───────────────────────────────────────────────

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

            /// <summary>D30 (a): every citizen standing in a snapshot lodge, first match of worker (Worker.m_Workplace
            /// = the lodge or its sign), resident / tourist on leisure (Leisure aimed at the lodge, split by
            /// TouristHousehold), outing member (AttendingMeeting), other.</summary>
            private void Occupancy()
            {
                int lodges = math.min(Lodges.Length, Occupants.Length / Occupant.Kinds);
                if (lodges == 0)
                    return;   // a ski area without a lodge yet: no citizen walk for nothing (fixer, review JP-3)
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

            /// <summary>D30 (b): meetings Traveling (MeetingStatus.cs (=)) to a snapshot sign or lodge. Their age is
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
    }
}
