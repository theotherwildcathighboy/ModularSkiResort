// SkiDayRules — the PURE rules of Round S1 "Ski days" (docs/research/ski-days/s1-spec.md §2.2; ski-days spec
// ch1-rules.md §2-§3; decisions D1-D21, D29). Every number and rule of a ski day lives here: candidate test
// (D5-D7), lodge and area gates (D8-D10), travel class, reach and travel time (D11-D14), claim window and
// deadline (D14, D20), chances (D16, D17), best area (D18), rolls (D3), back-off and speed learning (D13-D15),
// powder (D19, D29), capacity (D21), slices and ARM (D1, D3), day key and month step. Consumers: SkiDaySystem
// and its Burst jobs (stream B, ch2), SkiLodgeParkingSystem (CheckLodge, stream C, ch3), the settings (the
// LocalsPct… / TouristsPct… / ReachKm… / PowderPct… constants, aliased there: s1-spec N8), HubRules tests
// (PowderOf / PowderMark parity, N10); tests/ResortBatch SkiDayTests (ch1 §7).
//
// SkiDayLimit is declared at top level (outside the class): gate rule R17 compares it with bindings.ts
// SKI_DAY_LIMIT (check-bundle ENUM_PAIRS; SkiResortMod.csproj lists this file in SkiUiInput), so its
// literal values never change.
//
// PURE FILE: `using System;` and `using Unity.Mathematics;` only, plus EntityRef (never Entity). Every method
// is static, allocation-free and Burst-callable (SkiDaySystem's jobs call it): no managed type, no string,
// no exception. net48 traps (the mod build is the API gate): no Math.Clamp, MathF, double.IsFinite, Span<T>
// — math.* stands in (math.isfinite, math.clamp, math.floor).
//
// Created 2026-10-05 (Round S1 stage 0: s1-spec §4.2 step 3 — types, constants and the slice / day-key /
// distance helpers in full; the other bodies stubbed); owned by stream A, which wrote the other bodies the
// same day, ch1 §3 rule for rule (tests/ResortBatch SkiDayTests, ch1 §7 rows A-Q).
// Round M1 "measure first" (docs/research/ski-days/measure/m1-spec.md §1.5, builder A, 2026-10-05): measure-only
// helpers at the end of the class (distance bands, the mode seen, why a decision did not fit, the hour date and
// month of the powder year table) and Advance's closedShort overload (D5: a jump that lands a month later closes a
// partial month); SkiDayTests section T.

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>What limits a ski area's ski-day capacity (D21). Literal values: gate rule R17 pair
    /// SKI_DAY_LIMIT (bindings.ts).</summary>
    public enum SkiDayLimit : byte { None = 0, Runs = 1, Lodges = 2 }

    /// <summary>Round S1 ski-day rules (ch1 §2-§3). Pure, static, Burst-callable.</summary>
    public static class SkiDayRules
    {
        // ── constants (ch1 §2.2 table, exact) ────────────────────────────────────────────────
        public const int TicksPerDay = 262144;                       // TimeSystem.kTicksPerDay (TimeSystem.cs:18)
        public const double FramesPerHour = TicksPerDay / 24.0;      // 10,922.667
        public const int SliceGroups = 16;                           // SimulationUtils.cs:153-156 callers
        public const int MaxFramesPerRenderFrame = 8;                // SimulationSystem.cs:255 (=): ≤ 8 sim frames per render frame (s1-spec N15)
        public const int MinLeisureToTripRatio = 4;                  // D1
        public const int NoMonth = int.MinValue;                     // = SkiResortLedger.NoMonth
        public const double ClaimStartHour = 5.0, LiftsOpenHour = 8.0, CloseHour = 16.5, NightCloseHour = 21.0;   // D20 (LiftsOpenHour: ARMED line only)
        public const double MinStayHours = 2.0;                      // D14
        public const double WalkMaxM = 900.0;                        // D11
        public const double NearKm = 1.0;                            // D16
        public const double DetourFactor = 1.3;                      // D14
        public const double StartSpeedWalk = 0.3, StartSpeedCar = 2.2, StartSpeedNoCar = 1.2;   // km per game hour; D14, NoCar ch1 §11 DV-3
        public const double MinSpeedWalk = 0.1, MaxSpeedWalk = 1.0;      // learning clamps
        public const double MinSpeedCar = 0.5, MaxSpeedCar = 8.0;
        public const double MinSpeedNoCar = 0.2, MaxSpeedNoCar = 8.0;
        public const double SpeedLearnRate = 0.25;
        public const double MinLearnDistanceM = 300.0, MinLearnHours = 0.05;
        public const double MinTravelSpeed = 0.01;                   // guard
        public const double ConfidenceMin = 0.1;                     // D15
        public const double PriorWeight = 4.0;                       // D13
        public const double PriorWalk = 1.0, PriorCar = 1.0, PriorNoCar = 0.1;   // D13 (no car starts at 1 in 10)
        public const double TrackKeep = 0.975;                       // = 1 − 1/40
        public const int CannotReachMinResolved = 20;                // D15
        public const double CannotReachBelow = 0.5, CannotReachClearAt = 0.6;    // D15 (+ F1-style hysteresis)
        public const double ResidentCap = 0.20, TouristCap = 0.30;   // D16, D17
        public const double TouristPowderShare = 0.25;               // D17
        public const int PriceFullAt = 240, PriceSpan = 200, PriceMax = 200;     // D16: P = (240 − price) / 200, exact in int
        public const int MaxAttraction = 200;                        // = SkiResortScore.MaxAttraction
        public const float SnowPrecipMin = 0.1f;                     // SnowSystem.cs:487
        public const int SnowHourSamples = 24;                       // D19
        public const int PowderStartHours = 4, PowderFullHours = 10; // D19
        public const double PowderMarkAt = 0.5;                      // D29
        public const double LodgeSkiersPerDay = 360.0, PowderCapacityBoost = 0.25;   // D21
        public const int MaxCapacityPeople = 1000000;                // guard
        public const float SlopesideMaxM = 800f;                     // D10
        // The settings' numbers (decisions §8; s1-spec §2.11 aliases them, N8):
        public const int LocalsPctDefault = 25, LocalsPctMin = 0, LocalsPctMax = 50;           // D16
        public const int TouristsPctDefault = 50, TouristsPctMin = 0, TouristsPctMax = 100;    // D17
        public const int ReachKmDefault = 7, ReachKmMin = 2, ReachKmMax = 12;                  // D14
        public const int PowderPctDefault = 100, PowderPctMin = 0, PowderPctMax = 200;         // D16
        public const ulong ResidentSalt = 0x534B494441595331UL;      // "SKIDAYS1" (D3)
        public const ulong TouristSalt = 0x534B494441595332UL;       // "SKIDAYS2" (D3)

        // ── nested enums (ch1 §2.3; literal values — the DUMP counters index by them) ───────
        public enum GuestKind : byte { Resident = 0, Tourist = 1 }
        public enum TravelClass : byte { Walk = 0, Car = 1, NoCarFar = 2, None = 3 }     // None = not claimable
        public enum Reject : byte
        {
            None = 0, NotDeciding = 1, Busy = 2, Ill = 3, TripsPending = 4, NotInBuilding = 5,
            HouseholdExcluded = 6, NotAtHome = 7, OutingNotSpent = 8,
        }
        public enum LodgeReject : byte
        {
            None = 0, NotLodge = 1, NotInstalled = 2, Inactive = 3, Destroyed = 4,
            NoLeisureTag = 5, NoRoad = 6, NotSlopeside = 7,
        }
        public enum AreaState : byte        // the simulation's gate; the hub maps its own HubSkiDayState
        {
            Open = 0, NoScore = 1, Held = 2, NoEfficiency = 3, NoOpenRun = 4, NoLodge = 5,
            OutOfHours = 6, Full = 7,
        }
        public enum MonthStep : byte { None = 0, Started = 1, Rolled = 2, Jumped = 3 }   // = SkiResortLedger.AdvanceResult values
        public enum ArmVerdict : byte { Ok = 0, BadInterval = 1, BadOffset = 2, LeisureTooFast = 3, BehaviorTooFast = 4 }

        // ── value structs (ch1 §2.4) ────────────────────────────────────────────────────────
        public struct Knobs { public int LocalsPct, TouristsPct, ReachKm, PowderPct; }

        public struct CitizenFacts                      // D5-D7, filled by the job from lookups (ch2)
        {
            public bool HasLeisure, LeisureTargetNull;
            public bool HasPathInformation, HasTarget, HasTravelPurpose, HasResourceBuyer, HasAttendingMeeting;
            public bool HasHealthProblem; public int TripCount; public bool HasCurrentBuilding;
            public bool HouseholdTourist, HouseholdCommuter, HouseholdHomeless, HouseholdMovingAway;
            public bool HouseholdLodgingSeeker, HouseholdAttendingEvent;
            public bool AtHome;   // resident: PropertyRenter(household).m_Property == CurrentBuilding; tourist: m_Hotel != Null
                                  // && m_Hotel has LodgingProvider && PropertyRenter(m_Hotel).m_Property == CurrentBuilding (D7; ch2 fills it)
        }

        public struct LodgeFacts                        // D8, D10 (ch2 fills: SiteJob / step A; ch3's census with nearest 0)
        {
            public bool IsLodgePrefab, OnLiveSign, Active, Destroyed, LeisureTagged, HasRoad;
            public float NearestRunOrStationM;
        }

        public struct AreaFacts                         // D9 + lodges passing CheckLodge
        {
            public bool ScoreKnown, WriteHeld; public double Efficiency; public int RunsCounted;
            public int SkiDayLodges;
        }

        public struct ClassTrack                        // per area × class, in memory
        {
            public double Arrived, Failed; public int Resolved; public double SpeedKmPerHour;
            public bool CannotReachShown;
        }

        public struct MonthFlags { public int Key; public bool ThisSinceLoad, LastKnown, LastSinceLoad; }

        // ── helpers ─────────────────────────────────────────────────────────────────────────

        /// <summary>Clamp to [0, 1]; NaN → 0.</summary>
        public static double Unit01(double x) => x > 0.0 ? (x < 1.0 ? x : 1.0) : 0.0;

        // ── candidate, lodge, area (ch1 §3.1-§3.3) ──────────────────────────────────────────

        /// <summary>D5-D7 (ch1 §3.1): the first failing test wins — common tests (deciding, busy, ill, trips,
        /// building), then the household rules of the guest's kind. Tourists ignore commuter / homeless, residents
        /// ignore lodging seeker / attending event.</summary>
        public static Reject CheckCitizen(in CitizenFacts c, out GuestKind who)
        {
            who = c.HouseholdTourist ? GuestKind.Tourist : GuestKind.Resident;
            if (!c.HasLeisure || !c.LeisureTargetNull)
                return Reject.NotDeciding;
            if (c.HasPathInformation || c.HasTarget || c.HasTravelPurpose || c.HasResourceBuyer || c.HasAttendingMeeting)
                return Reject.Busy;
            if (c.HasHealthProblem)
                return Reject.Ill;
            if (c.TripCount != 0)
                return Reject.TripsPending;
            if (!c.HasCurrentBuilding)
                return Reject.NotInBuilding;
            if (who == GuestKind.Tourist)
            {
                // D7: in the hotel's building, the one vanilla outing spent (AttendingEvent), no lodging search.
                if (c.HouseholdMovingAway || c.HouseholdLodgingSeeker)
                    return Reject.HouseholdExcluded;
                if (!c.AtHome)
                    return Reject.NotAtHome;
                return c.HouseholdAttendingEvent ? Reject.None : Reject.OutingNotSpent;
            }
            // D6: at home; not a commuter, homeless or moving-away household.
            if (c.HouseholdCommuter || c.HouseholdHomeless || c.HouseholdMovingAway)
                return Reject.HouseholdExcluded;
            return c.AtHome ? Reject.None : Reject.NotAtHome;
        }

        /// <summary>D8, D10 (ch1 §3.2), in this order: not a lodge (the Welcome Sign is never a target), not on a
        /// live sign, inactive, destroyed, no leisure tag (a Tourism Overhaul gate is respected), no road, not
        /// slope-side.</summary>
        public static LodgeReject CheckLodge(in LodgeFacts l)
        {
            if (!l.IsLodgePrefab)
                return LodgeReject.NotLodge;
            if (!l.OnLiveSign)
                return LodgeReject.NotInstalled;
            if (!l.Active)
                return LodgeReject.Inactive;
            if (l.Destroyed)
                return LodgeReject.Destroyed;
            if (!l.LeisureTagged)
                return LodgeReject.NoLeisureTag;
            if (!l.HasRoad)
                return LodgeReject.NoRoad;
            return IsSlopeside(l.NearestRunOrStationM) ? LodgeReject.None : LodgeReject.NotSlopeside;
        }

        /// <summary>xz distance from p to an axis box (Geometry.m_Bounds.xz); 0 inside. Never larger than the
        /// distance to any shape inside the box, so SiteJob skips a run exactly when it is ≥ the current nearest
        /// (ch1 §3.2). Any non-finite input → +∞ (Unity's math.max(NaN, 0) returns 0, so the guard is needed).</summary>
        public static float DistanceToBoundsXZ(float2 p, float2 boundsMin, float2 boundsMax)
        {
            if (!math.all(math.isfinite(p)) || !math.all(math.isfinite(boundsMin)) || !math.all(math.isfinite(boundsMax)))
                return float.PositiveInfinity;
            return math.length(math.max(math.max(boundsMin - p, p - boundsMax), 0f));
        }

        /// <summary>xz distance from p to segment ab; a degenerate segment is the point a. The formula of
        /// LiftServedRule.DistanceSqXZ, re-stated so this file stays self-contained (ch1-R-3).</summary>
        public static float DistanceToSegmentXZ(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len2 = math.dot(ab, ab);
            float t = len2 > 0f ? math.saturate(math.dot(p - a, ab) / len2) : 0f;
            return math.length(p - (a + t * ab));
        }

        /// <summary>Even-odd test (ch1-R-3): does the ray from p towards +x cross edge ab? (.y = world z.) The
        /// division only runs when the edge straddles p.y, so b.y ≠ a.y there; NaN → false.</summary>
        public static bool CrossesRayXZ(float2 p, float2 a, float2 b)
        {
            return (a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x;
        }

        /// <summary>xz distance (ch1 §3.2).</summary>
        public static float DistanceXZ(float2 a, float2 b) => math.distance(a, b);

        /// <summary>The smaller of the two; start at +∞; NaN never wins (ch1 §3.2).</summary>
        public static float NearestOf(float nearest, float d) => d < nearest ? d : nearest;

        /// <summary>D10: within 800 m (inclusive) of a member run or a lift station of its own area; NaN → false.</summary>
        public static bool IsSlopeside(float nearestM) => nearestM <= SlopesideMaxM;

        /// <summary>D9, D20, D21 (ch1 §3.3), in this order: score unknown, first-write hold, efficiency not &gt; 0
        /// (NaN too), no counted run (a powered lift alone is not enough), no lodge passing CheckLodge, outside the
        /// claim window, full. nightRunOpen = Result.NightRunsCounted &gt; 0.</summary>
        public static AreaState AreaStateOf(in AreaFacts a, float timeOfDay, bool nightRunOpen, int claimedActive, int capacityPeople)
        {
            if (!a.ScoreKnown)
                return AreaState.NoScore;
            if (a.WriteHeld)
                return AreaState.Held;
            if (!(a.Efficiency > 0.0))
                return AreaState.NoEfficiency;
            if (a.RunsCounted <= 0)
                return AreaState.NoOpenRun;
            if (a.SkiDayLodges <= 0)
                return AreaState.NoLodge;
            if (!InClaimWindow(timeOfDay, nightRunOpen))
                return AreaState.OutOfHours;
            return HasRoom(claimedActive, capacityPeople) ? AreaState.Open : AreaState.Full;
        }

        // ── travel class, reach, travel time (ch1 §3.4) ─────────────────────────────────────

        /// <summary>D11-D13: no valid distance → None; ≤ 900 m → Walk (anyone, children too); a child beyond →
        /// None; a parked car held or a free household car → Car; else NoCarFar. (Children get no car from the
        /// reserve job, CitizenBehaviorSystem.cs:67; 1.6.2 :68.)</summary>
        public static TravelClass ClassOf(double distanceM, bool isChild, bool holdsParkedCar, bool householdFreeCar)
        {
            if (!(distanceM >= 0.0))
                return TravelClass.None;
            if (distanceM <= WalkMaxM)
                return TravelClass.Walk;
            if (isChild)
                return TravelClass.None;
            return holdsParkedCar || householdFreeCar ? TravelClass.Car : TravelClass.NoCarFar;
        }

        /// <summary>D14: straight line ≤ the reach setting (clamped 2..12 km), inclusive; NaN → false.</summary>
        public static bool WithinReach(double distanceM, int reachKm) => distanceM <= math.clamp(reachKm, ReachKmMin, ReachKmMax) * 1000.0;

        /// <summary>D16 D: 1 up to 1 km, then a straight line to 0 at the reach (clamped 2..12 km); NaN or
        /// negative → 0.</summary>
        public static double DistanceFactor(double distanceKm, int reachKm)
        {
            if (!(distanceKm >= 0.0))
                return 0.0;
            int reach = math.clamp(reachKm, ReachKmMin, ReachKmMax);
            if (distanceKm <= NearKm)
                return 1.0;
            if (distanceKm >= reach)
                return 0.0;
            return (reach - distanceKm) / (reach - NearKm);
        }

        /// <summary>D14 start speed, km per game hour (None reads Walk's).</summary>
        public static double StartSpeedOf(TravelClass c)
        {
            switch (c)
            {
                case TravelClass.Car: return StartSpeedCar;
                case TravelClass.NoCarFar: return StartSpeedNoCar;
                default: return StartSpeedWalk;
            }
        }

        /// <summary>Lower learning clamp, km per game hour (None reads Walk's).</summary>
        public static double MinSpeedOf(TravelClass c)
        {
            switch (c)
            {
                case TravelClass.Car: return MinSpeedCar;
                case TravelClass.NoCarFar: return MinSpeedNoCar;
                default: return MinSpeedWalk;
            }
        }

        /// <summary>Upper learning clamp, km per game hour (None reads Walk's).</summary>
        public static double MaxSpeedOf(TravelClass c)
        {
            switch (c)
            {
                case TravelClass.Car: return MaxSpeedCar;
                case TravelClass.NoCarFar: return MaxSpeedNoCar;
                default: return MaxSpeedWalk;
            }
        }

        /// <summary>D14: game hours = km × 1.3 / speed. A negative, NaN or infinite distance → +∞ (never fits);
        /// the speed is floored at MinTravelSpeed (NaN → the floor).</summary>
        public static double TravelHours(double distanceM, double speedKmPerHour)
        {
            if (!(distanceM >= 0.0) || !math.isfinite(distanceM))
                return double.PositiveInfinity;
            double v = speedKmPerHour > MinTravelSpeed ? speedKmPerHour : MinTravelSpeed;
            return distanceM / 1000.0 * DetourFactor / v;
        }

        // ── hours and deadline (ch1 §3.5) ───────────────────────────────────────────────────

        /// <summary>Hour of the day, 24 × frac(t); t = TimeSystem.normalizedTime (ticks mod 262,144 over 262,144,
        /// TimeSystem.cs:109-112 (=)). NaN in, NaN out.</summary>
        public static double HourOf(float timeOfDay)
        {
            double t = timeOfDay;
            return 24.0 * (t - math.floor(t));
        }

        /// <summary>D20: lifts close at 16:30, at 21:00 while the area has an open night run.</summary>
        public static double CloseHourOf(bool nightRunOpen) => nightRunOpen ? NightCloseHour : CloseHour;

        /// <summary>D20: claims from 05:00 (inclusive) to closing (exclusive); NaN → false.</summary>
        public static bool InClaimWindow(float timeOfDay, bool nightRunOpen)
        {
            double h = HourOf(timeOfDay);
            return h >= ClaimStartHour && h < CloseHourOf(nightRunOpen);
        }

        /// <summary>D20: the sim frame of today's closing, now + floor((close / 24 − frac(t)) × 262,144); false
        /// (and 0) outside the claim window. Frames compare like vanilla's deadline (wrapping uint).</summary>
        public static bool TryClosingFrame(uint now, float timeOfDay, bool nightRunOpen, out uint closingFrame)
        {
            closingFrame = 0u;
            if (!InClaimWindow(timeOfDay, nightRunOpen))
                return false;
            double t = timeOfDay;
            double left = (CloseHourOf(nightRunOpen) / 24.0 - (t - math.floor(t))) * TicksPerDay;   // > 0 inside the window
            closingFrame = now + (uint)math.floor(left);
            return true;
        }

        /// <summary>D14, D20: a finite, non-negative travel time plus MinStayHours fits before both vanilla's
        /// deadline and closing; false when either is ≤ now. Frames as vanilla compares them (LeisureSystem.cs:314,
        /// 1.6.2 :308: frame ≥ m_LastPossibleFrame ends the visit; CitizenBehaviorSystem.cs:491-495, 1.6.2 :493).</summary>
        public static bool ClaimFits(uint now, uint vanillaDeadline, uint closingFrame, double travelHours)
        {
            if (!(travelHours >= 0.0) || !math.isfinite(travelHours))
                return false;
            if (vanillaDeadline <= now || closingFrame <= now)
                return false;
            double need = (travelHours + MinStayHours) * FramesPerHour;
            return need <= vanillaDeadline - now && need <= closingFrame - now;
        }

        /// <summary>D2, D20: the claim's m_LastPossibleFrame — closing, never later than vanilla's.</summary>
        public static uint ClaimDeadline(uint vanillaDeadline, uint closingFrame) => math.min(vanillaDeadline, closingFrame);

        // ── chance and best area (ch1 §3.6) ─────────────────────────────────────────────────

        /// <summary>The four settings at their defaults (decisions §8).</summary>
        public static Knobs DefaultKnobs()
        {
            return new Knobs
            {
                LocalsPct = LocalsPctDefault, TouristsPct = TouristsPctDefault,
                ReachKm = ReachKmDefault, PowderPct = PowderPctDefault,
            };
        }

        /// <summary>Each knob to its Min..Max. Defensive only: the settings readers already clamp with the same
        /// constants (s1-spec §2.11).</summary>
        public static Knobs ClampKnobs(in Knobs k)
        {
            return new Knobs
            {
                LocalsPct = math.clamp(k.LocalsPct, LocalsPctMin, LocalsPctMax),
                TouristsPct = math.clamp(k.TouristsPct, TouristsPctMin, TouristsPctMax),
                ReachKm = math.clamp(k.ReachKm, ReachKmMin, ReachKmMax),
                PowderPct = math.clamp(k.PowderPct, PowderPctMin, PowderPctMax),
            };
        }

        /// <summary>D16 P = clamp01((240 − clamp(price, 0, 200)) / 200): 1 up to ₵40, 0.7 at ₵100, 0.2 from ₵200.</summary>
        public static double PriceFactor(int ticketPrice)
            => Unit01((PriceFullAt - math.clamp(ticketPrice, 0, PriceMax)) / (double)PriceSpan);

        /// <summary>D16 open share, value-weighted (Result.RunsValue / Result.RunsValueFull = the hub's "open %",
        /// ch1 §11 N-3); 0 unless the full value is finite and &gt; 0.</summary>
        public static double OpenShare(double runsValue, double runsValueFull)
        {
            if (!math.isfinite(runsValueFull) || !(runsValueFull > 0.0))
                return 0.0;
            return Unit01(runsValue / runsValueFull);
        }

        /// <summary>D16 W / D17 Wt = open × (1 + s × powder × pull / 100), s = 1 residents, 0.25 tourists; pull
        /// clamped 0..200 (0 ignores powder).</summary>
        public static double SnowFactor(GuestKind who, double openShare, double powder, int powderPct)
        {
            double s = who == GuestKind.Tourist ? TouristPowderShare : 1.0;
            int pull = math.clamp(powderPct, PowderPctMin, PowderPctMax);
            return Unit01(openShare) * (1.0 + s * Unit01(powder) * pull / 100.0);
        }

        /// <summary>D16 / D17 (ch1 §3.6): base = min(cap, pct/100 × share × D), share A/100 residents, A/200
        /// tourists (ch1 §11 N-1); × W × P × confidence outside the cap (powder and price always act). Knobs
        /// clamped; every input sanitised, so the result is in [0, 0.6] residents, [0, 0.45] tourists.</summary>
        public static double Chance(GuestKind who, int attraction, double distanceKm, double openShare, double powder,
                                    int ticketPrice, double confidence, in Knobs knobs)
        {
            Knobs k = ClampKnobs(knobs);
            double d = DistanceFactor(distanceKm, k.ReachKm);
            int a = math.clamp(attraction, 0, MaxAttraction);
            double b = who == GuestKind.Tourist
                ? math.min(TouristCap, k.TouristsPct / 100.0 * a / 200.0 * d)
                : math.min(ResidentCap, k.LocalsPct / 100.0 * a / 100.0 * d);
            double p = b * SnowFactor(who, openShare, powder, k.PowderPct) * PriceFactor(ticketPrice) * Unit01(confidence);
            return Unit01(p);
        }

        /// <summary>D18 best area: false unless p &gt; 0; higher p wins (no best yet = 0); a tie goes to the
        /// shorter distance, then the lower sign (EntityRef.CompareTo: Index, then Version).</summary>
        public static bool IsBetter(double p, double distanceM, EntityRef sign, double bestP, double bestDistanceM, EntityRef bestSign)
        {
            if (!(p > 0.0))
                return false;
            if (!(bestP > 0.0) || p > bestP)
                return true;
            if (p < bestP)
                return false;
            if (distanceM != bestDistanceM)
                return distanceM < bestDistanceM;
            return sign.CompareTo(bestSign) < 0;
        }

        // ── rolls (ch1 §3.7; s1-spec N2) ────────────────────────────────────────────────────

        /// <summary>splitmix64 finalizer, the constants of RunSnowRules.Mix64 (re-stated: this file links alone).</summary>
        public static ulong Mix64(ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }

        /// <summary>An entity's (Index, Version) as one 64-bit key.</summary>
        public static ulong KeyOf(int index, int version) => ((ulong)(uint)index << 32) | (uint)version;

        /// <summary>The top 53 bits as a double in [0, 1), exact.</summary>
        public static double UnitOf(ulong h) => (h >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>D3 resident roll, one per decision (s1-spec N2, deviation V7): decisionFrame =
        /// SkiDayTiming.NextLeisureVisit(chunk slice, now, leisure interval), the frame LeisureSystem takes this
        /// decision — the same for its up to 15 sightings, new for the next decision. Not m_LastPossibleFrame:
        /// CBS sets it to day start + a per-citizen constant (CitizenBehaviorSystem.cs:434-441, :472-494; 1.6.2
        /// :473-493), one value for every decision of a day.</summary>
        public static double ResidentRoll(int citizenIndex, int citizenVersion, uint decisionFrame)
            => UnitOf(Mix64(Mix64(KeyOf(citizenIndex, citizenVersion) ^ ResidentSalt) ^ decisionFrame));

        /// <summary>D3 tourist roll: one per party (household) per game day (DayKey), so a party skis together.</summary>
        public static double TouristRoll(int householdIndex, int householdVersion, int dayKey)
            => UnitOf(Mix64(Mix64(KeyOf(householdIndex, householdVersion) ^ TouristSalt) ^ (uint)dayKey));

        /// <summary>roll &lt; p (a p of 0 or NaN never hits).</summary>
        public static bool Hit(double roll, double p) => roll < p;

        // ── back-off and speed learning (ch1 §3.8) ──────────────────────────────────────────

        /// <summary>D13 prior: Walk 1, Car 1, NoCarFar 0.1 (1 in 10 until such guests arrive), None 0.</summary>
        public static double PriorOf(TravelClass c)
        {
            switch (c)
            {
                case TravelClass.Walk: return PriorWalk;
                case TravelClass.Car: return PriorCar;
                case TravelClass.NoCarFar: return PriorNoCar;
                default: return 0.0;
            }
        }

        /// <summary>A fresh track (load, new area): no outcome yet, the class's start speed.</summary>
        public static ClassTrack NewTrack(TravelClass c) => new ClassTrack { SpeedKmPerHour = StartSpeedOf(c) };

        /// <summary>D15: clamp((A + 4 × prior) / (A + F + 4), 0.1, 1), A and F floored at 0 (NaN → 0); a NaN ratio
        /// reads the floor. Start: Walk / Car 1, NoCarFar 0.1.</summary>
        public static double Confidence(TravelClass c, in ClassTrack t)
        {
            double a = t.Arrived > 0.0 ? t.Arrived : 0.0;
            double f = t.Failed > 0.0 ? t.Failed : 0.0;
            double conf = (a + PriorWeight * PriorOf(c)) / (a + f + PriorWeight);
            return conf > 1.0 ? 1.0 : (conf >= ConfidenceMin ? conf : ConfidenceMin);
        }

        /// <summary>D15: both counts decay by 0.975 (a window of about 40 outcomes), the outcome adds 1 to its
        /// side; Resolved counts outcomes (saturating). A failure is a claim whose Leisure lost the lodge without
        /// arriving, or whose deadline passed (ch2 detects).</summary>
        public static void RecordOutcome(ref ClassTrack t, bool arrived)
        {
            t.Arrived = t.Arrived * TrackKeep + (arrived ? 1.0 : 0.0);
            t.Failed = t.Failed * TrackKeep + (arrived ? 0.0 : 1.0);
            if (t.Resolved < int.MaxValue)
                t.Resolved++;
        }

        /// <summary>D14 speed learning from claim → arrival: skipped for arrival ≤ claim, under 300 m (or NaN) or
        /// under 0.05 game hours; sample = clamp(km × 1.3 / hours, Min, Max); Speed moves a quarter of the way to
        /// it (an unset speed starts from the class's start speed).</summary>
        public static void LearnSpeed(ref ClassTrack t, TravelClass c, double distanceM, uint claimFrame, uint arrivalFrame)
        {
            if (arrivalFrame <= claimFrame || !(distanceM >= MinLearnDistanceM))
                return;
            double hours = (arrivalFrame - claimFrame) / FramesPerHour;
            if (hours < MinLearnHours)
                return;
            double sample = math.clamp(distanceM / 1000.0 * DetourFactor / hours, MinSpeedOf(c), MaxSpeedOf(c));
            double v = t.SpeedKmPerHour > 0.0 ? t.SpeedKmPerHour : StartSpeedOf(c);
            t.SpeedKmPerHour = v + SpeedLearnRate * (sample - v);
        }

        /// <summary>Hub row 20 (D15, ch1 §11 N-4): fewer than 20 resolved car claims → off; else on under 0.5
        /// confidence, off from 0.6, unchanged between (F1's lesson: no threshold without hysteresis). Stores and
        /// returns CannotReachShown.</summary>
        public static bool UpdateCannotReach(ref ClassTrack car)
        {
            if (car.Resolved < CannotReachMinResolved)
            {
                car.CannotReachShown = false;
                return false;
            }
            double conf = Confidence(TravelClass.Car, car);
            if (conf < CannotReachBelow)
                car.CannotReachShown = true;
            else if (conf >= CannotReachClearAt)
                car.CannotReachShown = false;
            return car.CannotReachShown;
        }

        // ── powder (ch1 §3.9) ───────────────────────────────────────────────────────────────

        /// <summary>D19: precipitation ≥ 0.1 (SnowSystem.cs:487) and the air at the area's top at or below
        /// freezing — ambient − (top − base) × lapse, the float expression of SnowLineSystem.GetLocalTemperature
        /// (SnowLineSystem.cs:102-107), bit for bit. NaN → false.</summary>
        public static bool IsSnowHour(float precipitation, float ambientC, float topM, float baseHeightM, float lapsePerM, float freezingC)
            => precipitation >= SnowPrecipMin && ambientC - (topM - baseHeightM) * lapsePerM <= freezingC;

        /// <summary>The k-th hourly date back from <paramref name="alignedDate"/>, wrapped into [0, 1): the date
        /// RunSnowRules.DayMeanC passes to its delegate, bit for bit (step = 1 / (24 × days), days ≥ 1).</summary>
        public static float SnowHourSampleDate(float alignedDate, int k, int daysPerYear)
        {
            int days = daysPerYear < 1 ? 1 : daysPerYear;
            double step = 1.0 / (24.0 * days);
            double t = alignedDate - k * step;
            t -= math.floor(t);
            return (float)t;
        }

        /// <summary>D19 powder = clamp01((snow-hours − 4) / 6) over the last 24 game hours: sixths only.</summary>
        public static double PowderOf(int snowHours)
        {
            if (snowHours <= PowderStartHours)
                return 0.0;
            if (snowHours >= PowderFullHours)
                return 1.0;
            return (snowHours - PowderStartHours) / (double)(PowderFullHours - PowderStartHours);
        }

        /// <summary>D29 "Powder day": powder ≥ 0.5 (7 or more snow-hours). The hub's POWDER_MARK_PCT = 50 on the
        /// rounded percent agrees at every reachable powder (HubRulesTests H16 parity, s1-spec N10).</summary>
        public static bool PowderMark(double powder) => powder >= PowderMarkAt;

        // ── capacity (ch1 §3.10) ────────────────────────────────────────────────────────────

        /// <summary>D21 room on the hill: min(runs, lodges) × (1 + 0.25 × powder). runs = Result.RunsCapacity
        /// (NaN / ≤ 0 → 0, capped at 1e6); lodges = 360 per lodge passing CheckLodge (ch1 §11 DV-1). The limit is
        /// Lodges when no lodge passes, else the smaller side (a tie names the runs).</summary>
        public static double Capacity(double runsCapacity, int skiDayLodges, double powder, out SkiDayLimit limit)
        {
            double runs = runsCapacity > 0.0 ? math.min(runsCapacity, (double)MaxCapacityPeople) : 0.0;
            double lodges = skiDayLodges > 0 ? skiDayLodges * LodgeSkiersPerDay : 0.0;
            limit = skiDayLodges <= 0 ? SkiDayLimit.Lodges : (runs <= lodges ? SkiDayLimit.Runs : SkiDayLimit.Lodges);
            return math.min(runs, lodges) * (1.0 + PowderCapacityBoost * Unit01(powder));
        }

        /// <summary>Capacity in whole people, rounded half up and capped at 1e6; 0 unless &gt; 0 (NaN too). 0 is
        /// a real capacity (Full, s1-spec N9).</summary>
        public static int CapacityPeople(double capacity)
        {
            if (!(capacity > 0.0))
                return 0;
            double c = capacity < MaxCapacityPeople ? capacity : MaxCapacityPeople;
            return (int)math.floor(c + 0.5);
        }

        /// <summary>claimedActive (this area's claims on the way and on site, this pass's included) &lt; capacity.</summary>
        public static bool HasRoom(int claimedActive, int capacityPeople) => claimedActive < capacityPeople;

        // ── slices and ARM (ch1 §3.11) ──────────────────────────────────────────────────────

        /// <summary>= SimulationUtils.GetUpdateFrameWithInterval(frame, interval, 16) (SimulationUtils.cs:153-156 (=))
        /// for a valid interval; 0 when interval ≤ 0 (no division by zero in Burst, ch1-R-9).</summary>
        public static uint SliceOf(uint frame, int interval) => interval <= 0 ? 0u : (frame / (uint)interval) & 15u;

        /// <summary>Does a system with this interval and offset run on GameSimulation update index
        /// <paramref name="frame"/>? (UpdateSystem.cs:241 (=); the index is SimulationSystem.frameIndex,
        /// SimulationSystem.cs:279-286.)</summary>
        public static bool RunsThisFrame(uint frame, int interval, int offset) => (frame & (uint)(interval - 1)) == (uint)offset;

        /// <summary>The exact-frame part of D3's skip rule (ch1 §3.11): the chunk's UpdateFrame index is
        /// LeisureSystem's slice this frame (LeisureSystem.cs:959, :322; 1.6.2 :956, :316), or
        /// CitizenBehaviorSystem runs this frame and the index is its slice (CitizenBehaviorSystem.cs:1138, :603;
        /// 1.6.2 :1170, :1174). The job calls SkiDayTiming.SkipChunk, which adds the recent-CBS-run window.</summary>
        public static bool SkipSlice(uint updateFrameIndex, uint frame, int leisureInterval, int behaviorInterval, int behaviorOffset)
            => updateFrameIndex == SliceOf(frame, leisureInterval)
               || (RunsThisFrame(frame, behaviorInterval, behaviorOffset) && updateFrameIndex == SliceOf(frame, behaviorInterval));

        /// <summary>x &gt; 0 and a power of two (UpdateSystem requires one, UpdateSystem.cs:303-306).</summary>
        public static bool IsPowerOfTwo(int x) => x > 0 && (x & (x - 1)) == 0;

        /// <summary>D1 ARM check on the intervals read at run time (LeisureSystem 64, LeisureSystem.cs:914-917, 1.6.2
        /// :911-914; TripNeededSystem 16, TripNeededSystem.cs:1939-1942; CitizenBehaviorSystem 16 / offset 11,
        /// CitizenBehaviorSystem.cs:1016-1024, 1.6.2 :1048-1056):
        /// an interval not a power of two → BadInterval; offset outside [0, behaviorInterval) → BadOffset; leisure
        /// &lt; 4 × trip → LeisureTooFast; behaviorInterval ≤ 8 → BehaviorTooFast (CBS could run twice in one render
        /// frame, which SkiDayTiming's recent-slice skip assumes it does not; s1-spec N15).</summary>
        public static ArmVerdict ArmCheck(int leisureInterval, int tripInterval, int behaviorInterval, int behaviorOffset)
        {
            if (!IsPowerOfTwo(leisureInterval) || !IsPowerOfTwo(tripInterval) || !IsPowerOfTwo(behaviorInterval))
                return ArmVerdict.BadInterval;
            if (behaviorOffset < 0 || behaviorOffset >= behaviorInterval)
                return ArmVerdict.BadOffset;
            if (leisureInterval < (long)MinLeisureToTripRatio * tripInterval)
                return ArmVerdict.LeisureTooFast;
            if (behaviorInterval <= MaxFramesPerRenderFrame)
                return ArmVerdict.BehaviorTooFast;
            return ArmVerdict.Ok;
        }

        // ── day key and month step (ch1 §3.12) ──────────────────────────────────────────────

        /// <summary>year × days + clamp(floor(date × days), 0, days − 1), days = max(1, daysPerYear); a non-finite
        /// date reads day 0. The same float arithmetic as SkiMoneyRules.MonthKey, so at 12 days a year the two are
        /// equal (the tourist roll key, D3). The one day key of the mod (s1-spec N3).</summary>
        public static int DayKey(int year, float normalizedDate, int daysPerYear)
        {
            int days = math.max(1, daysPerYear);
            int d = math.isfinite(normalizedDate) ? (int)math.floor(normalizedDate * days) : 0;
            return year * days + math.clamp(d, 0, days - 1);
        }

        /// <summary>Before the first month: Key = NoMonth, every flag false.</summary>
        public static MonthFlags NewMonthFlags() => new MonthFlags { Key = NoMonth };

        /// <summary>SkiResortLedger.Advance (:94-133) rule for rule, on the flags only: Started (first key:
        /// ThisSinceLoad), None (same key), Rolled (key = Key + 1, Key ≠ int.MaxValue: LastKnown, LastSinceLoad =
        /// ThisSinceLoad, ThisSinceLoad false), Jumped (any other key: LastKnown and LastSinceLoad false,
        /// ThisSinceLoad true — counting restarts as after a load). ch2 feeds it SkiMoneyRules.MonthKey.
        /// = Advance(ref f, key, closedShort: false).</summary>
        public static MonthStep Advance(ref MonthFlags f, int key) => Advance(ref f, key, false);

        /// <summary>Round M1 (M7c, D5): as Advance(ref f, key), except that a Rolled step also marks the closed month
        /// partial when <paramref name="closedShort"/> (too few of its frames were simulated: a developer time jump that
        /// landed exactly one month later, SkiMoneyRules.IsShortMonth): LastSinceLoad = ThisSinceLoad || closedShort.
        /// = SkiResortLedger.Advance(key, closedMonthShort). A short flag never changes Started, None or Jumped.</summary>
        public static MonthStep Advance(ref MonthFlags f, int key, bool closedShort)
        {
            if (f.Key == NoMonth)
            {
                f.Key = key;
                f.ThisSinceLoad = true;
                return MonthStep.Started;
            }
            if (key == f.Key)
                return MonthStep.None;
            bool rolled = f.Key != int.MaxValue && key == f.Key + 1;
            if (rolled)
            {
                f.LastKnown = true;
                f.LastSinceLoad = f.ThisSinceLoad || closedShort;
                f.ThisSinceLoad = false;
            }
            else
            {
                f.LastKnown = false;
                f.LastSinceLoad = false;
                f.ThisSinceLoad = true;
            }
            f.Key = key;
            return rolled ? MonthStep.Rolled : MonthStep.Jumped;
        }

        // ── Round M1 "measure first" (m1-spec §1.5; measure only, no rule reads these) ─────────────

        /// <summary>M4 distance band (straight line, metres): &lt; 1 km → 0; &lt; 3 → 1; &lt; 5 → 2; &lt; 7 → 3; else 4;
        /// NaN or negative → 0.</summary>
        public static int DistanceBand(double m)
        {
            if (!(m >= 0.0) || m < 1000.0)
                return 0;
            if (m < 3000.0)
                return 1;
            if (m < 5000.0)
                return 2;
            return m < 7000.0 ? 3 : 4;
        }
        public const int DistanceBands = 5;

        /// <summary>M3: the main mode of the vehicles a guest was seen in (docs/research/ski-days/next/arrival-modes.md
        /// §8 row 1 with its Verification corrections). Literal values: the measure rows index by them.</summary>
        public enum ArrivalMode : byte { Foot = 0, Car = 1, Transit = 2, Taxi = 3, Bike = 4 }
        public const int ArrivalModes = 5;
        // Leg bits (SkiDayClaim.Legs) and transit kind bits (SkiDayClaim.Kinds).
        public const byte LegCar = 1, LegTransit = 2, LegTaxi = 4, LegBike = 8, LegOther = 16;
        public const byte KindBus = 1, KindTram = 2, KindTrain = 4, KindSubway = 8, KindBoat = 16, KindOther = 32;
        public const int KindBits = 6;
        // Game.Prefabs.TransportType literals (1.6.2 Game.Prefabs/TransportType.cs:5-19, (=) 1.6.0): an int here so this
        // file needs no Game type; a rebuild against a changed Game.dll is the only way they move (SkiDayTests T rows).
        public const int TransportBus = 0, TransportTrain = 1, TransportTram = 3, TransportShip = 4, TransportSubway = 8, TransportFerry = 11;

        /// <summary>M3: the leg of one vehicle, from the LEAD vehicle's components (the Controller's, for a train or
        /// tram car): a personal car with the Bicycle tag → Bike (a bike is a PersonalCar vehicle, arrival-modes
        /// Verification row 10); a personal car → Car; a taxi → Taxi; public transport (not evacuating, not a prisoner
        /// transport: the caller tests) → Transit; anything else (police car, ambulance, hearse, a bicycle tag alone) →
        /// Other. No vehicle carries both Taxi and PublicTransport (row 7), so their order does not matter.</summary>
        public static byte LegOf(bool personalCar, bool bicycle, bool taxi, bool publicTransport)
        {
            if (personalCar)
                return bicycle ? LegBike : LegCar;
            if (taxi)
                return LegTaxi;
            return publicTransport ? LegTransit : LegOther;
        }

        /// <summary>M3: the kind bit of a public-transport vehicle's prefab TransportType (int, −1 unknown): Bus, Tram,
        /// Train, Subway as named; Ship or Ferry → Boat; anything else → Other.</summary>
        public static byte KindBitOf(int transportType)
        {
            switch (transportType)
            {
                case TransportBus: return KindBus;
                case TransportTram: return KindTram;
                case TransportTrain: return KindTrain;
                case TransportSubway: return KindSubway;
                case TransportShip:
                case TransportFerry: return KindBoat;
                default: return KindOther;
            }
        }

        /// <summary>M3 "mode seen": Car if any car leg; else Transit; else Taxi; else Bike; else Foot (an Other leg alone
        /// counts as on foot). Every legs byte maps to exactly one mode.</summary>
        public static ArrivalMode MainMode(byte legs)
        {
            if ((legs & LegCar) != 0)
                return ArrivalMode.Car;
            if ((legs & LegTransit) != 0)
                return ArrivalMode.Transit;
            if ((legs & LegTaxi) != 0)
                return ArrivalMode.Taxi;
            return (legs & LegBike) != 0 ? ArrivalMode.Bike : ArrivalMode.Foot;
        }

        /// <summary>M4: why a decision did not fit (ClaimFits false) at an area.</summary>
        public enum Fit : byte { Fits = 0, NoTimeBeforeClosing = 1, NoTimeBeforeOwnDeadline = 2, Invalid = 3 }

        /// <summary>M4: a negative, NaN or infinite travel time → Invalid; ClaimFits → Fits; else the earlier of the two
        /// deadlines is named: vanilla's strictly earlier → NoTimeBeforeOwnDeadline, else NoTimeBeforeClosing (frames
        /// compared as ClaimDeadline compares them). FitVerdict == Fits ⇔ ClaimFits (SkiDayTests T).</summary>
        public static Fit FitVerdict(uint now, uint vanillaDeadline, uint closingFrame, double travelHours)
        {
            if (!(travelHours >= 0.0) || !math.isfinite(travelHours))
                return Fit.Invalid;
            if (ClaimFits(now, vanillaDeadline, closingFrame, travelHours))
                return Fit.Fits;
            return vanillaDeadline < closingFrame ? Fit.NoTimeBeforeOwnDeadline : Fit.NoTimeBeforeClosing;
        }

        /// <summary>M9: the date of an hour of the year, (float)(hourKey / (24 × max(1, days))) — what
        /// RunSnowRules.HourAlignedDate returns for that hour key (RunSnowRules.cs:242-249), bit for bit.</summary>
        public static float HourDate(int hourKey, int daysPerYear)
        {
            int days = daysPerYear < 1 ? 1 : daysPerYear;
            return (float)(hourKey / (24.0 * days));
        }

        /// <summary>M9: the calendar month 0..11 of an hour of the year: clamp(floor(hourKey × 12 / (24 × max(1, days))),
        /// 0, 11) (at 12 days a year: hourKey / 24). Integer arithmetic, exact.</summary>
        public static int MonthOfHour(int hourKey, int daysPerYear)
        {
            int days = daysPerYear < 1 ? 1 : daysPerYear;
            long m = (long)hourKey * 12L / (24L * days);
            return m <= 0L ? 0 : (m >= 11L ? 11 : (int)m);
        }
    }
}
