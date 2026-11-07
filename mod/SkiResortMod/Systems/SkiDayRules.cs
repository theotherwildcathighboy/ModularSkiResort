// The pure rules of a ski day. Every number and rule lives here: candidate test, lodge and area gates, travel class, reach and
// travel time, claim window and deadline, chances, best area, rolls, back-off and speed learning, powder, capacity, slices and
// ARM, day key and month step. Consumers: SkiDaySystem and its Burst jobs, SkiLodgeParkingSystem (CheckLodge), the settings (the
// LocalsPct... / TouristsPct... / ReachKm... / PowderPct... constants are aliased there), tests/ResortBatch SkiDayTests.
// SkiDayLimit is declared at top level: gate rule R17 compares it with bindings.ts SKI_DAY_LIMIT, so its literal values never change.
// Pure file, Burst-callable. It uses EntityRef (never Entity) and SkiCatalog consts only. Every method is static and allocation-free. No
// method uses a managed type, string or exception. The file does not use net48 Math.Clamp, MathF, double.IsFinite or Span<T> (math.* stands
// in for them).
// Engine notes: EngineNotes/SkiDayRules.md

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>What limits a ski area's ski-day capacity. Literal values: gate rule R17 pairs it with
    /// SKI_DAY_LIMIT (bindings.ts).</summary>
    public enum SkiDayLimit : byte { None = 0, Runs = 1, Lodges = 2 }

    /// <summary>The ski-day rules. Pure, static, Burst-callable.</summary>
    public static class SkiDayRules
    {
        public const int TicksPerDay = 262144;                       // TimeSystem.kTicksPerDay (TimeSystem.cs:18)
        public const double FramesPerHour = TicksPerDay / 24.0;      // 10,922.667
        public const int SliceGroups = 16;                           // SimulationUtils.cs:153-156 callers
        public const int MaxFramesPerRenderFrame = 8;                // SimulationSystem.cs:255 (=): ≤ 8 sim frames per render frame
        public const int MinLeisureToTripRatio = 4;
        public const int NoMonth = int.MinValue;                     // = SkiResortLedger.NoMonth
        public const double ClaimStartHour = 5.0, LiftsOpenHour = 8.0, CloseHour = 16.5, NightCloseHour = 21.0;   // (LiftsOpenHour: ARMED line only)
        public const double MinStayHours = 2.0;
        public const double WalkMaxM = 900.0;
        public const double NearKm = 1.0;
        public const double DetourFactor = 1.3;
        public const double StartSpeedWalk = 0.3, StartSpeedCar = 2.2, StartSpeedNoCar = 1.2;   // km per game hour
        public const double MinSpeedWalk = 0.1, MaxSpeedWalk = 1.0;      // learning clamps
        public const double MinSpeedCar = 0.5, MaxSpeedCar = 8.0;
        public const double MinSpeedNoCar = 0.2, MaxSpeedNoCar = 8.0;
        public const double SpeedLearnRate = 0.25;
        public const double MinLearnDistanceM = 300.0, MinLearnHours = 0.05;
        public const double MinTravelSpeed = 0.01;                   // guard
        public const double ConfidenceMin = 0.1;
        public const double PriorWeight = 4.0;
        public const double PriorWalk = 1.0, PriorCar = 1.0, PriorNoCar = 0.1;   // no car starts at 1 in 10
        public const double TrackKeep = 0.975;                       // = 1 − 1/40
        public const int CannotReachMinResolved = 20;
        public const double CannotReachBelow = 0.5, CannotReachClearAt = 0.6;    // plus hysteresis
        public const double ResidentCap = 0.20, TouristCap = 0.30;
        public const double TouristPowderShare = 0.25;
        public const int PriceFullAt = 240, PriceSpan = 200, PriceMax = 200;     // P = (240 − price) / 200, exact in int
        public const int MaxAttraction = 200;                        // = SkiResortScore.MaxAttraction
        public const float SnowPrecipMin = 0.1f;                     // SnowSystem.cs:487
        public const int SnowHourSamples = 24;
        // Powder follows the fresh snow of the last 24 game hours, not the count of snowy hours. Fresh is the sum of the precipitation
        // (ClimateSystem, 0..1) over the hours that snow at the area's top (IsSnowHour), in steps of FreshStep. Powder = clamp01((steps -
        // 6) / 6), so it starts above 3.0. The "Powder day" mark (0.5) needs 4.5. Powder is full at 6.0. A snow-hours count marked nearly
        // every winter day as powder. This marks one storm a year.
        public const double FreshStep = 0.5;                         // precipitation-hours per step
        public const int PowderStartSteps = 6, PowderFullSteps = 12; // 3.0 and 6.0 precipitation-hours
        public const double PowderMarkAt = 0.5;
        public const double LodgeSkiersPerDay = SkiCatalog.AFrameLodge.SkiersPerDay, PowderCapacityBoost = 0.25;   // Catalog const alias of the A-frame lodge's capacity
        public const int MaxCapacityPeople = 1000000;                // guard
        public const float SlopesideMaxM = 800f;
        // The settings' numbers:
        public const int LocalsPctDefault = 25, LocalsPctMin = 0, LocalsPctMax = 50;
        public const int TouristsPctDefault = 50, TouristsPctMin = 0, TouristsPctMax = 100;
        public const int ReachKmDefault = 7, ReachKmMin = 2, ReachKmMax = 12;
        public const int PowderPctDefault = 100, PowderPctMin = 0, PowderPctMax = 200;
        public const ulong ResidentSalt = 0x534B494441595331UL;      // "SKIDAYS1"
        public const ulong TouristSalt = 0x534B494441595332UL;       // "SKIDAYS2"

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
            NoHillLodge = 8,   // a bus depot in an area with no passing hill lodge (CheckDepot)
        }
        public enum AreaState : byte        // the simulation's gate. The hub maps its own HubSkiDayState
        {
            Open = 0, NoScore = 1, Held = 2, NoEfficiency = 3, NoOpenRun = 4, NoLodge = 5,
            OutOfHours = 6, Full = 7,
        }
        public enum MonthStep : byte { None = 0, Started = 1, Rolled = 2, Jumped = 3 }   // = SkiResortLedger.AdvanceResult values
        /// <summary>SystemOff: the game's own LeisureSystem or CitizenBehaviorSystem is switched off (ArmCheck's 5-argument overload). This
        /// is a vanilla fact, never a mod detection.</summary>
        public enum ArmVerdict : byte { Ok = 0, BadInterval = 1, BadOffset = 2, LeisureTooFast = 3, BehaviorTooFast = 4, SystemOff = 5 }

        public struct Knobs { public int LocalsPct, TouristsPct, ReachKm, PowderPct; }

        public struct CitizenFacts                      // filled by the job from lookups
        {
            public bool HasLeisure, LeisureTargetNull;
            public bool HasPathInformation, HasTarget, HasTravelPurpose, HasResourceBuyer, HasAttendingMeeting;
            public bool HasHealthProblem; public int TripCount; public bool HasCurrentBuilding;
            public bool HouseholdTourist, HouseholdCommuter, HouseholdHomeless, HouseholdMovingAway;
            public bool HouseholdLodgingSeeker, HouseholdAttendingEvent;
            public bool AtHome;   // resident: PropertyRenter(household).m_Property == CurrentBuilding. Tourist: m_Hotel != Null
                                  // && m_Hotel has LodgingProvider && PropertyRenter(m_Hotel).m_Property == CurrentBuilding
        }

        public struct LodgeFacts                        // filled by the SiteJob / step A. The census has nearest 0
        {
            public bool IsLodgePrefab, OnLiveSign, Active, Destroyed, LeisureTagged, HasRoad;
            public float NearestRunOrStationM;
            /// <summary>Needs no road and takes no arrivals. These are the Hut and Summit, or a road-optional lodge off-road on a cat track
            /// chain (LodgeSupplyRule.NoRoadNeeded, per building). false (the default) means a road is required.</summary>
            public bool NoRoadNeeded;
        }

        public struct AreaFacts                         // plus the lodges passing CheckLodge
        {
            public bool ScoreKnown, WriteHeld; public double Efficiency; public int RunsCounted;
            public int SkiDayLodges;
        }

        public struct ClassTrack                        // per area x class, in memory
        {
            public double Arrived, Failed; public int Resolved; public double SpeedKmPerHour;
            public bool CannotReachShown;
            /// <summary>The all-outcomes speed (arrivals, late arrivals, upper bounds from failures and expired claims). The fit check
            /// reads it only when TripKnobs.FitUsesAllOutcomes (FitSpeed).</summary>
            public double SpeedAllKmPerHour;
        }

        /// <summary>The trip-model numbers that were constants. One struct serves both jobs. The defaults equal today's constants
        /// (DefaultTripKnobs): stay 2 h, floors walk 0.1 / car 0.5 / no car 0.2 km per game hour. FitUsesAllOutcomes picks the speed the
        /// fit check reads (FitSpeed). false means shadow-only.</summary>
        public struct TripKnobs { public double StayHours, FloorWalk, FloorCar, FloorNoCar; public bool FitUsesAllOutcomes; }

        /// <summary>LearnSpeedBound's result. NoInformation: the bound is +inf (SpeedBound's guards). Lowered: the all-outcomes speed moved
        /// a quarter of the way down to the bound. AlreadyMet: the speed was at or below the clamped bound (unchanged).</summary>
        public enum BoundLearn : byte { NoInformation = 0, Lowered = 1, AlreadyMet = 2 }

        public struct MonthFlags { public int Key; public bool ThisSinceLoad, LastKnown, LastSinceLoad; }


        /// <summary>Clamp to [0, 1]. NaN gives 0.</summary>
        public static double Unit01(double x) => x > 0.0 ? (x < 1.0 ? x : 1.0) : 0.0;


        /// <summary>The first failing test wins: common tests (deciding, busy, ill, trips,
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
                // Required: in the hotel's building, the one vanilla outing spent (AttendingEvent), and no lodging search.
                if (c.HouseholdMovingAway || c.HouseholdLodgingSeeker)
                    return Reject.HouseholdExcluded;
                if (!c.AtHome)
                    return Reject.NotAtHome;
                return c.HouseholdAttendingEvent ? Reject.None : Reject.OutingNotSpent;
            }
            // Required: at home, and not a commuter, homeless or moving-away household.
            if (c.HouseholdCommuter || c.HouseholdHomeless || c.HouseholdMovingAway)
                return Reject.HouseholdExcluded;
            return c.AtHome ? Reject.None : Reject.NotAtHome;
        }

        /// <summary>In this order: not a lodge (the Welcome Sign is never a target), not on a
        /// live sign, inactive, destroyed, no leisure tag (a Tourism Overhaul gate is respected), no road (unless
        /// NoRoadNeeded: a mountain lodge), not slope-side.</summary>
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
            if (!l.HasRoad && !l.NoRoadNeeded)
                return LodgeReject.NoRoad;
            return IsSlopeside(l.NearestRunOrStationM) ? LodgeReject.None : LodgeReject.NotSlopeside;
        }

        /// <summary>A Ski Bus Depot's verdict. It uses CheckLodge's order with its road always required. IsSlopeside is replaced by "the
        /// area has a passing hill lodge". <paramref name="passingHillLodges"/> counts the area's real lodges passing CheckLodge. A
        /// mountain lodge counts. A lodge gated only by the missing leisure tag counts, as step A keeps it in capacity.</summary>
        public static LodgeReject CheckDepot(in LodgeFacts l, int passingHillLodges)
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
            return passingHillLodges > 0 ? LodgeReject.None : LodgeReject.NoHillLodge;
        }

        /// <summary>xz distance from p to an axis box (Geometry.m_Bounds.xz). The distance is 0 inside the box. It is never larger than the
        /// distance to any shape inside the box, so SiteJob skips a run exactly when it is &gt;= the current nearest. Any non-finite input
        /// gives +inf (Unity's math.max(NaN, 0) returns 0, so the guard is needed).</summary>
        public static float DistanceToBoundsXZ(float2 p, float2 boundsMin, float2 boundsMax)
        {
            if (!math.all(math.isfinite(p)) || !math.all(math.isfinite(boundsMin)) || !math.all(math.isfinite(boundsMax)))
                return float.PositiveInfinity;
            return math.length(math.max(math.max(boundsMin - p, p - boundsMax), 0f));
        }

        /// <summary>xz distance from p to segment ab. A degenerate segment is the point a. The formula of LiftServedRule.DistanceSqXZ,
        /// re-stated so this file stays self-contained.</summary>
        public static float DistanceToSegmentXZ(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len2 = math.dot(ab, ab);
            float t = len2 > 0f ? math.saturate(math.dot(p - a, ab) / len2) : 0f;
            return math.length(p - (a + t * ab));
        }

        /// <summary>Even-odd test: does the ray from p towards +x cross edge ab? (.y = world z.) The division only runs when the edge
        /// straddles p.y, so b.y ≠ a.y there. NaN gives false.</summary>
        public static bool CrossesRayXZ(float2 p, float2 a, float2 b)
        {
            return (a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x;
        }

        /// <summary>xz distance.</summary>
        public static float DistanceXZ(float2 a, float2 b) => math.distance(a, b);

        /// <summary>The smaller of the two. Start at +∞. NaN never wins.</summary>
        public static float NearestOf(float nearest, float d) => d < nearest ? d : nearest;

        /// <summary>Within 800 m (inclusive) of a member run or a lift station of its own area. NaN gives false.</summary>
        public static bool IsSlopeside(float nearestM) => nearestM <= SlopesideMaxM;

        /// <summary>In this order: score unknown, first-write hold, efficiency not &gt; 0
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


        /// <summary>Keys of the SkiDays `[area]` line (AreaMemory.StateKey). None means nothing written yet this load. AreaLogKey never
        /// returns it, so the first pass always writes.</summary>
        public const byte LogKeyNone = 0, LogKeyWaiting = 1, LogKeyNoEfficiency = 2, LogKeyNoOpenRun = 3, LogKeyNoLodge = 4,
                          LogKeyOpen = 5, LogKeyFull = 6;

        /// <summary>The `[area]` line's key of an area state. NoScore and Held read Waiting. Full has its own key (its own line).
        /// OutOfHours keeps Full when the area was Full (the claim window closing is not "room again"). Otherwise OutOfHours reads Open, as
        /// before. Any other value reads Open.</summary>
        public static byte AreaLogKey(AreaState state, byte previousKey)
        {
            switch (state)
            {
                case AreaState.NoScore:
                case AreaState.Held:
                    return LogKeyWaiting;
                case AreaState.NoEfficiency:
                    return LogKeyNoEfficiency;
                case AreaState.NoOpenRun:
                    return LogKeyNoOpenRun;
                case AreaState.NoLodge:
                    return LogKeyNoLodge;
                case AreaState.Full:
                    return LogKeyFull;
                case AreaState.OutOfHours:
                    return previousKey == LogKeyFull ? LogKeyFull : LogKeyOpen;
                default:
                    return LogKeyOpen;
            }
        }

        /// <summary>The hill counts as closed for "arrived after the hill closed" when the score is known, the area is not in the
        /// first-write hold, and no run is counted. This is AreaStateOf's NoOpenRun test with its earlier gates. It has no time-of-day test
        /// (closing hours are the deadline's business). An unknown score never counts as closed (its RunsCounted is 0 until the first
        /// score, SkiDaySystem.RebuildSnapshot).</summary>
        public static bool HillClosedForCount(bool scoreKnown, bool held, int runsCounted) => scoreKnown && !held && runsCounted <= 0;

        /// <summary>A closure edge of a ski area this pass. An unknown or held score gives 0 (never an edge: a load hold with 0 runs is not
        /// a closing). No counted run gives 1 when the hill was seen open (counted runs) since the load, else 0. The night run closed (it
        /// was seen open and now is not, runs still counted) gives 2. Any other case gives 0. The literals equal SkiDayTiming.PullNone /
        /// PullNoRun / PullNightClosed (this file does not name SkiDayTiming).</summary>
        public static byte ClosingEdgeOf(bool hillOpenSeen, bool nightOpenSeen, bool scoreKnown, bool held, int runsCounted, bool nightRunOpen)
        {
            if (!scoreKnown || held)
                return 0;
            if (runsCounted <= 0)
                return (byte)(hillOpenSeen ? 1 : 0);
            return (byte)(!nightRunOpen && nightOpenSeen ? 2 : 0);
        }


        /// <summary>No valid distance gives None. A distance of 900 m or less gives Walk (anyone, children too). A child beyond that gives
        /// None. A parked car held or a free household car gives Car. All other cases give NoCarFar. (Children get no car from the reserve
        /// job, CitizenBehaviorSystem.cs:67, 1.6.2 :68.)</summary>
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

        /// <summary>Straight line ≤ the reach setting (clamped 2..12 km), inclusive. NaN gives false.</summary>
        public static bool WithinReach(double distanceM, int reachKm) => distanceM <= math.clamp(reachKm, ReachKmMin, ReachKmMax) * 1000.0;

        /// <summary>A ski day's drive, for the hub's "nobody near" row: the reach setting, at most the default 7 km. Measured: at the 12 km
        /// reach a hill with homes 9.5 km and more away got 1 guest sent in a month. The reasons: the trip alone takes about 6 game hours
        /// (TravelHours at StartSpeedCar), and the chance falls with distance (DistanceFactor).</summary>
        public static double DayTripM(int reachKm) => math.min(math.clamp(reachKm, ReachKmMin, ReachKmMax), ReachKmDefault) * 1000.0;

        /// <summary>Fewer households than this within a ski day's drive read as nobody near.</summary>
        public const int NearGuestsMin = 10;

        /// <summary>An open hill can expect almost no guests: there are households in the city and hardly any near the hill. A city with no
        /// household yet says nothing (a resort built first).</summary>
        public static bool NoGuestsNear(int households, int near) => households > 0 && near < NearGuestsMin;

        /// <summary>A distance in tenths of a km, at least 1 (the hub's Detail: 0 means no row).</summary>
        public static int TenthsKm(float distanceM) => math.max(1, (int)math.round(distanceM / 100f));

        /// <summary>1 up to 1 km, then a straight line to 0 at the reach (clamped 2..12 km). NaN or negative gives 0.</summary>
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

        /// <summary>Start speed, km per game hour (None reads Walk's).</summary>
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

        /// <summary>Game hours = km × 1.3 / speed. A negative, NaN or infinite distance gives +∞ (never fits). The speed is floored at
        /// MinTravelSpeed (NaN gives the floor).</summary>
        public static double TravelHours(double distanceM, double speedKmPerHour)
        {
            if (!(distanceM >= 0.0) || !math.isfinite(distanceM))
                return double.PositiveInfinity;
            double v = speedKmPerHour > MinTravelSpeed ? speedKmPerHour : MinTravelSpeed;
            return distanceM / 1000.0 * DetourFactor / v;
        }


        /// <summary>Hour of the day, 24 × frac(t). t = TimeSystem.normalizedTime (ticks mod 262,144 over 262,144, TimeSystem.cs:109-112
        /// (=)). NaN in, NaN out.</summary>
        public static double HourOf(float timeOfDay)
        {
            double t = timeOfDay;
            return 24.0 * (t - math.floor(t));
        }

        /// <summary>Lifts close at 16:30, at 21:00 while the area has an open night run.</summary>
        public static double CloseHourOf(bool nightRunOpen) => nightRunOpen ? NightCloseHour : CloseHour;

        /// <summary>Claims from 05:00 (inclusive) to closing (exclusive). NaN gives false.</summary>
        public static bool InClaimWindow(float timeOfDay, bool nightRunOpen)
        {
            double h = HourOf(timeOfDay);
            return h >= ClaimStartHour && h < CloseHourOf(nightRunOpen);
        }

        /// <summary>The sim frame of today's closing, now + floor((close / 24 − frac(t)) × 262,144). The result is false (and 0) outside
        /// the claim window. Frames compare like vanilla's deadline (wrapping uint).</summary>
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

        /// <summary>A finite, non-negative travel time plus MinStayHours fits before both vanilla's deadline and closing. The result is
        /// false when either is ≤ now. Frames compare as vanilla compares them (LeisureSystem.cs:314, 1.6.2 :308: frame ≥
        /// m_LastPossibleFrame ends the visit. Also CitizenBehaviorSystem.cs:491-495, 1.6.2 :493). It equals ClaimFits(…, MinStayHours):
        /// the same expression, so every double is identical (SkiDayTests W3).</summary>
        public static bool ClaimFits(uint now, uint vanillaDeadline, uint closingFrame, double travelHours)
            => ClaimFits(now, vanillaDeadline, closingFrame, travelHours, MinStayHours);

        /// <summary>ClaimFits with the stay reserve as an input (TripKnobs.StayHours). A stay that is not finite or below 0 reads
        /// MinStayHours.</summary>
        public static bool ClaimFits(uint now, uint vanillaDeadline, uint closingFrame, double travelHours, double stayHours)
        {
            if (!(travelHours >= 0.0) || !math.isfinite(travelHours))
                return false;
            if (vanillaDeadline <= now || closingFrame <= now)
                return false;
            double stay = math.isfinite(stayHours) && stayHours >= 0.0 ? stayHours : MinStayHours;
            double need = (travelHours + stay) * FramesPerHour;
            return need <= vanillaDeadline - now && need <= closingFrame - now;
        }

        /// <summary>A worker's or student's deadline on a day off: bedtime alone. Vanilla's leisure deadline is the earlier of bedtime and
        /// the next work or school time with no off-day test (1.6.2 CitizenBehaviorSystem.cs:472-485). However, the game's own work and
        /// school checks skip an off day (WorkerSystem.cs:350, StudentSystem.cs:316: the same coin for both). The time left until
        /// sleepStart is as CitizenBehaviorSystem.GetTimeLeftUntilInterval computes it. The deadline is never earlier than
        /// vanilla's.</summary>
        public static uint OffDayDeadline(uint vanillaDeadline, uint now, float timeOfDay, float sleepStart)
        {
            float left = timeOfDay < sleepStart ? sleepStart - timeOfDay : 1f - timeOfDay + sleepStart;
            return math.max(vanillaDeadline, now + (uint)(left * TicksPerDay));
        }

        /// <summary>The claim's m_LastPossibleFrame: closing, never later than vanilla's.</summary>
        public static uint ClaimDeadline(uint vanillaDeadline, uint closingFrame) => math.min(vanillaDeadline, closingFrame);


        /// <summary>The four settings at their defaults.</summary>
        public static Knobs DefaultKnobs()
        {
            return new Knobs
            {
                LocalsPct = LocalsPctDefault, TouristsPct = TouristsPctDefault,
                ReachKm = ReachKmDefault, PowderPct = PowderPctDefault,
            };
        }

        /// <summary>The trip knobs at today's constants (stay MinStayHours, floors MinSpeedWalk / MinSpeedCar / MinSpeedNoCar), the fit
        /// check on arrivals only. These are code constants, printed in the ARMED line. They are not exposed in Options.</summary>
        public static TripKnobs DefaultTripKnobs()
        {
            return new TripKnobs
            {
                StayHours = MinStayHours, FloorWalk = MinSpeedWalk, FloorCar = MinSpeedCar, FloorNoCar = MinSpeedNoCar,
                FitUsesAllOutcomes = false,
            };
        }

        /// <summary>The class's learning floor from the knobs (None reads Walk's). A floor that is NaN, infinite or not &gt; 0 reads
        /// MinSpeedOf(c). At the defaults FloorOf(c, k) == MinSpeedOf(c) exactly.</summary>
        public static double FloorOf(TravelClass c, in TripKnobs k)
        {
            double f;
            switch (c)
            {
                case TravelClass.Car: f = k.FloorCar; break;
                case TravelClass.NoCarFar: f = k.FloorNoCar; break;
                default: f = k.FloorWalk; break;
            }
            return f > 0.0 && math.isfinite(f) ? f : MinSpeedOf(c);
        }

        /// <summary>Each knob to its Min..Max. Defensive only: the settings readers already clamp with the same
        /// constants.</summary>
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

        /// <summary>P = clamp01((240 − clamp(price, 0, 200)) / 200): 1 up to ₵40, 0.7 at ₵100, 0.2 from ₵200. It equals PriceFactor(price,
        /// 0) bit for bit.</summary>
        public static double PriceFactor(int ticketPrice)
            => PriceFactor(ticketPrice, 0);

        /// <summary>P = clamp01((240 + max(0, tolerance) − clamp(price, 0, 200)) / 200). The area's best lodge's PriceTolerance (A-frame /
        /// hut 0, mid 20, luxury 40) raises the full-price threshold by that much (tolerance 40: 1 up to ₵80, 0.4 from ₵200). The result is
        /// exact in int before the one division. The tolerance is clamped to 0..PriceMax (the contract's max(0, t), plus an overflow
        /// guard). From 160 on every price ≤ 200 already reads 1, so the upper clamp changes no result.</summary>
        public static double PriceFactor(int ticketPrice, int tolerance)
            => Unit01((PriceFullAt + math.clamp(tolerance, 0, PriceMax) - math.clamp(ticketPrice, 0, PriceMax)) / (double)PriceSpan);

        /// <summary>Open share, value-weighted (Result.RunsValue / Result.RunsValueFull = the hub's "open %"). The result is 0 unless the
        /// full value is finite and &gt; 0.</summary>
        public static double OpenShare(double runsValue, double runsValueFull)
        {
            if (!math.isfinite(runsValueFull) || !(runsValueFull > 0.0))
                return 0.0;
            return Unit01(runsValue / runsValueFull);
        }

        /// <summary>W / Wt = open × (1 + s × powder × pull / 100), s = 1 residents, 0.25 tourists. pull is clamped 0..200 (0 ignores
        /// powder).</summary>
        public static double SnowFactor(GuestKind who, double openShare, double powder, int powderPct)
        {
            double s = who == GuestKind.Tourist ? TouristPowderShare : 1.0;
            int pull = math.clamp(powderPct, PowderPctMin, PowderPctMax);
            return Unit01(openShare) * (1.0 + s * Unit01(powder) * pull / 100.0);
        }

        /// <summary>base = min(cap, pct/100 × share × distanceFactor), share A/100 residents, A/200 tourists. The factors × W × P ×
        /// confidence apply outside the cap (powder and price always act). Knobs are clamped and every input is sanitized, so the result is
        /// in [0, 0.6] residents, [0, 0.45] tourists. It equals Chance(…, ticketPrice, 0, 1.0, confidence, knobs) bit for bit.</summary>
        public static double Chance(GuestKind who, int attraction, double distanceKm, double openShare, double powder,
                                    int ticketPrice, double confidence, in Knobs knobs)
        {
            return Chance(who, attraction, distanceKm, openShare, powder, ticketPrice, 0, 1.0, confidence, knobs);
        }

        /// <summary>As the 8-argument Chance, with P = PriceFactor(ticketPrice, priceTolerance) (the area's best lodge's tolerance). For
        /// RESIDENTS only, the result also gets × schoolFactor after every other factor. It applies outside the cap, like W and P.
        /// SchoolRule.DemandFactorOf gives 1 or 1.2. A NaN or non-positive factor reads 1. Tourists ignore it. The result is clamped to [0,
        /// 1] (residents reach 0.72 at the most with the school, tourists 0.45).</summary>
        public static double Chance(GuestKind who, int attraction, double distanceKm, double openShare, double powder,
                                    int ticketPrice, int priceTolerance, double schoolFactor, double confidence, in Knobs knobs)
        {
            return Chance(who, attraction, distanceKm, openShare, powder, ticketPrice, priceTolerance, schoolFactor, confidence, knobs, false);
        }

        /// <summary>As the 10-argument Chance, except that a pass holder gets P = 1 in place of PriceFactor. A pass holder is the
        /// household's SkiSeasonPass for this season (residents only, the caller decides). The pass is sunk cost, so the ticket price no
        /// longer deters it. With <paramref name="passHolder"/> false this is the 10-argument Chance bit for bit.</summary>
        public static double Chance(GuestKind who, int attraction, double distanceKm, double openShare, double powder,
                                    int ticketPrice, int priceTolerance, double schoolFactor, double confidence, in Knobs knobs,
                                    bool passHolder)
        {
            Knobs k = ClampKnobs(knobs);
            double d = DistanceFactor(distanceKm, k.ReachKm);
            int a = math.clamp(attraction, 0, MaxAttraction);
            double b = who == GuestKind.Tourist
                ? math.min(TouristCap, k.TouristsPct / 100.0 * a / 200.0 * d)
                : math.min(ResidentCap, k.LocalsPct / 100.0 * a / 100.0 * d);
            double p = b * SnowFactor(who, openShare, powder, k.PowderPct) * (passHolder ? 1.0 : PriceFactor(ticketPrice, priceTolerance)) * Unit01(confidence);
            if (who == GuestKind.Resident && schoolFactor > 0.0 && schoolFactor != 1.0 && math.isfinite(schoolFactor))
                p *= schoolFactor;   // last: with 1.0 (no school) p is today's bit for bit
            return Unit01(p);
        }

        /// <summary>Best area: false unless p &gt; 0. Higher p wins (no best yet = 0). A tie goes to the shorter distance, then the lower
        /// sign (EntityRef.CompareTo: Index, then Version).</summary>
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

        /// <summary>Resident roll, one per decision. decisionFrame = SkiDayTiming.NextLeisureVisit(chunk slice, now, leisure interval). It
        /// is the frame LeisureSystem takes this decision. It is the same for its up to 15 sightings and new for the next decision. It is
        /// not m_LastPossibleFrame: CBS sets that to day start + a per-citizen constant (CitizenBehaviorSystem.cs:434-441, :472-494, 1.6.2
        /// :473-493), one value for every decision of a day.</summary>
        public static double ResidentRoll(int citizenIndex, int citizenVersion, uint decisionFrame)
            => UnitOf(Mix64(Mix64(KeyOf(citizenIndex, citizenVersion) ^ ResidentSalt) ^ decisionFrame));

        /// <summary>Tourist roll: one per party (household) per game day (DayKey), so a party skis together.</summary>
        public static double TouristRoll(int householdIndex, int householdVersion, int dayKey)
            => UnitOf(Mix64(Mix64(KeyOf(householdIndex, householdVersion) ^ TouristSalt) ^ (uint)dayKey));

        /// <summary>roll &lt; p (a p of 0 or NaN never hits).</summary>
        public static bool Hit(double roll, double p) => roll < p;


        /// <summary>Prior: Walk 1, Car 1, NoCarFar 0.1 (1 in 10 until such guests arrive), None 0.</summary>
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

        /// <summary>A fresh track (load, new area): no outcome yet, the class's start speed (both speeds).</summary>
        public static ClassTrack NewTrack(TravelClass c) => new ClassTrack { SpeedKmPerHour = StartSpeedOf(c), SpeedAllKmPerHour = StartSpeedOf(c) };

        /// <summary>clamp((A + 4 × prior) / (A + F + 4), 0.1, 1), A and F floored at 0 (NaN gives 0). A NaN ratio reads the floor. Start:
        /// Walk / Car 1, NoCarFar 0.1.</summary>
        public static double Confidence(TravelClass c, in ClassTrack t)
        {
            double a = t.Arrived > 0.0 ? t.Arrived : 0.0;
            double f = t.Failed > 0.0 ? t.Failed : 0.0;
            double conf = (a + PriorWeight * PriorOf(c)) / (a + f + PriorWeight);
            return conf > 1.0 ? 1.0 : (conf >= ConfidenceMin ? conf : ConfidenceMin);
        }

        /// <summary>Both counts decay by 0.975 (a window of about 40 outcomes). The outcome adds 1 to its side. Resolved counts outcomes
        /// (saturating). A failure is a claim whose Leisure lost the lodge without arriving, or whose deadline passed.</summary>
        public static void RecordOutcome(ref ClassTrack t, bool arrived)
        {
            t.Arrived = t.Arrived * TrackKeep + (arrived ? 1.0 : 0.0);
            t.Failed = t.Failed * TrackKeep + (arrived ? 0.0 : 1.0);
            if (t.Resolved < int.MaxValue)
                t.Resolved++;
        }

        /// <summary>Speed learning from claim to arrival. It is skipped for arrival ≤ claim, under 300 m (or NaN) or under 0.05 game hours.
        /// sample = clamp(km × 1.3 / hours, Min, Max). Speed moves a quarter of the way to the sample (an unset speed starts from the
        /// class's start speed). Returns whether it applied. It equals LearnSpeed(…, DefaultTripKnobs()), whose floors are MinSpeedOf(c)
        /// exactly (bit-identical, SkiDayTests W9).</summary>
        public static bool LearnSpeed(ref ClassTrack t, TravelClass c, double distanceM, uint claimFrame, uint arrivalFrame)
            => LearnSpeed(ref t, c, distanceM, claimFrame, arrivalFrame, DefaultTripKnobs());

        /// <summary>LearnSpeed with the floor from the knobs (FloorOf). Touches SpeedKmPerHour only (the arrival-only speed). Returns
        /// whether it applied.</summary>
        public static bool LearnSpeed(ref ClassTrack t, TravelClass c, double distanceM, uint claimFrame, uint arrivalFrame, in TripKnobs k)
        {
            if (!TrySpeedSample(c, distanceM, claimFrame, arrivalFrame, k, out double sample))
                return false;
            double v = t.SpeedKmPerHour > 0.0 ? t.SpeedKmPerHour : StartSpeedOf(c);
            t.SpeedKmPerHour = v + SpeedLearnRate * (sample - v);
            return true;
        }

        /// <summary>The same rule and guards on the all-outcomes speed (SpeedAllKmPerHour: 0 reads the start speed) for an exact sample: an
        /// arrival, or a late arrival after expiry (claim to arrival).</summary>
        public static bool LearnSpeedAll(ref ClassTrack t, TravelClass c, double distanceM, uint claimFrame, uint arrivalFrame, in TripKnobs k)
        {
            if (!TrySpeedSample(c, distanceM, claimFrame, arrivalFrame, k, out double sample))
                return false;
            double v = t.SpeedAllKmPerHour > 0.0 ? t.SpeedAllKmPerHour : StartSpeedOf(c);
            t.SpeedAllKmPerHour = v + SpeedLearnRate * (sample - v);
            return true;
        }

        /// <summary>LearnSpeed's guards and sample, shared so the two speeds read the same expression. Returns false for arrival ≤ claim
        /// (frames compared as uint, as before), a distance under 300 m or NaN, and under 0.05 game hours. sample = clamp(km × 1.3 / hours,
        /// FloorOf(c, k), MaxSpeedOf(c)).</summary>
        private static bool TrySpeedSample(TravelClass c, double distanceM, uint claimFrame, uint arrivalFrame, in TripKnobs k, out double sample)
        {
            sample = 0.0;
            if (arrivalFrame <= claimFrame || !(distanceM >= MinLearnDistanceM))
                return false;
            double hours = (arrivalFrame - claimFrame) / FramesPerHour;
            if (hours < MinLearnHours)
                return false;
            sample = math.clamp(distanceM / 1000.0 * DetourFactor / hours, FloorOf(c, k), MaxSpeedOf(c));
            return true;
        }

        /// <summary>An upper bound on the speed from a claim that did not arrive. It is km × 1.3 / hours from the claim to the last pass
        /// the guest was not there. Under LearnSpeed's guards the result is +∞ (no information): last ≤ claim, a distance under 300 m or
        /// NaN, under 0.05 game hours. The true speed is at or below the bound.</summary>
        public static double SpeedBound(double distanceM, uint claimFrame, uint lastNotThereFrame)
        {
            if (lastNotThereFrame <= claimFrame || !(distanceM >= MinLearnDistanceM))
                return double.PositiveInfinity;
            double hours = (lastNotThereFrame - claimFrame) / FramesPerHour;
            if (hours < MinLearnHours)
                return double.PositiveInfinity;
            return distanceM / 1000.0 * DetourFactor / hours;
        }

        /// <summary>The all-outcomes speed learns from an upper bound only when it is above the bound. b = SpeedBound (not finite gives
        /// NoInformation). sample = clamp(b, FloorOf(c, k), MaxSpeedOf(c)). v = SpeedAll (0 reads the start speed). If v &gt; sample, v
        /// becomes v + 0.25 × (sample − v) and the result is Lowered. Otherwise the result is AlreadyMet and v is unchanged. The rule never
        /// raises the speed and never moves past the sample. It uses the same rate SpeedLearnRate (the true speed is at or below the bound,
        /// so a quarter step toward it can only under-correct). Confidence is not touched.</summary>
        public static BoundLearn LearnSpeedBound(ref ClassTrack t, TravelClass c, double distanceM, uint claimFrame, uint lastNotThereFrame,
                                                 in TripKnobs k)
        {
            double b = SpeedBound(distanceM, claimFrame, lastNotThereFrame);
            if (!math.isfinite(b))
                return BoundLearn.NoInformation;
            double sample = math.clamp(b, FloorOf(c, k), MaxSpeedOf(c));
            double v = t.SpeedAllKmPerHour > 0.0 ? t.SpeedAllKmPerHour : StartSpeedOf(c);
            if (!(v > sample))
                return BoundLearn.AlreadyMet;
            t.SpeedAllKmPerHour = v + SpeedLearnRate * (sample - v);
            return BoundLearn.Lowered;
        }

        /// <summary>The speed the fit check reads (ClaimJob's TravelHours): the all-outcomes speed when FitUsesAllOutcomes and it is set
        /// (&gt; 0), else the arrival-only speed. With the defaults it is always the arrival-only speed, so sends are bit-identical
        /// (SkiDayTests W9).</summary>
        public static double FitSpeed(in ClassTrack t, in TripKnobs k)
            => k.FitUsesAllOutcomes && t.SpeedAllKmPerHour > 0.0 ? t.SpeedAllKmPerHour : t.SpeedKmPerHour;

        /// <summary>Hub row: with fewer than 20 resolved car claims the row is off. Otherwise the row is on under 0.5 confidence, off from
        /// 0.6, and unchanged between (no threshold without hysteresis). Stores and returns CannotReachShown.</summary>
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


        /// <summary>Precipitation ≥ 0.1 (SnowSystem.cs:487) and the air at the area's top at or below freezing. The air temperature is
        /// ambient − (top − base) × lapse, the float expression of SnowLineSystem.GetLocalTemperature (SnowLineSystem.cs:102-107), bit for
        /// bit. NaN gives false.</summary>
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

        /// <summary>One hour's fresh snow: its precipitation when it snows at the top (IsSnowHour), else 0. NaN gives 0.</summary>
        public static float FreshOfHour(float precipitation, bool snowHour)
            => snowHour && precipitation > 0f ? precipitation : 0f;

        /// <summary>Whole FreshStep steps in a 24-hour fresh-snow sum (rounded down, a 1e-6 guard against float sums just under a step).
        /// NaN or negative gives 0.</summary>
        public static int FreshSteps(double fresh)
        {
            if (!(fresh > 0.0))
                return 0;
            return (int)math.floor(fresh / FreshStep + 1e-6);
        }

        /// <summary>Powder = clamp01((fresh steps − 6) / 6) over the last 24 game hours: sixths only.</summary>
        public static double PowderOf(int freshSteps)
        {
            if (freshSteps <= PowderStartSteps)
                return 0.0;
            if (freshSteps >= PowderFullSteps)
                return 1.0;
            return (freshSteps - PowderStartSteps) / (double)(PowderFullSteps - PowderStartSteps);
        }

        /// <summary>"Powder day": powder ≥ 0.5 (9 or more fresh steps = 4.5 precipitation-hours of snow in 24 h). The hub's POWDER_MARK_PCT = 50 on the
        /// rounded percent agrees at every reachable powder (HubRulesTests parity).</summary>
        public static bool PowderMark(double powder) => powder >= PowderMarkAt;


        /// <summary>Room on the hill: min(runs, lodges) × (1 + 0.25 × powder). runs = Result.RunsCapacity (NaN / ≤ 0 gives 0, capped at
        /// 1e6). lodges = 360 per lodge passing CheckLodge. The limit is Lodges when no lodge passes, else the smaller side (a tie names
        /// the runs). It equals Capacity(runs, skiDayLodges × 360, skiDayLodges, powder) bit for bit.</summary>
        public static double Capacity(double runsCapacity, int skiDayLodges, double powder, out SkiDayLimit limit)
        {
            return Capacity(runsCapacity, skiDayLodges > 0 ? skiDayLodges * LodgeSkiersPerDay : 0.0, skiDayLodges, powder, out limit);
        }

        /// <summary>The lodge side is <paramref name="lodgeCapacity"/> = Σ Capacity over the lodges passing CheckLodge (every tier, any
        /// number, and a mountain lodge holds skiers without being a claim target). NaN / ≤ 0 gives 0. The limit is Lodges when no TARGET
        /// lodge passes (<paramref name="targetLodges"/> ≤ 0: nowhere to send a guest), else the smaller side (a tie names the
        /// runs).</summary>
        public static double Capacity(double runsCapacity, double lodgeCapacity, int targetLodges, double powder, out SkiDayLimit limit)
        {
            double runs = runsCapacity > 0.0 ? math.min(runsCapacity, (double)MaxCapacityPeople) : 0.0;
            double lodges = lodgeCapacity > 0.0 ? lodgeCapacity : 0.0;
            limit = targetLodges <= 0 ? SkiDayLimit.Lodges : (runs <= lodges ? SkiDayLimit.Runs : SkiDayLimit.Lodges);
            return math.min(runs, lodges) * (1.0 + PowderCapacityBoost * Unit01(powder));
        }

        /// <summary>Capacity in whole people, rounded half up and capped at 1e6. The result is 0 unless the capacity is &gt; 0 (NaN too). 0
        /// is a real capacity (Full).</summary>
        public static int CapacityPeople(double capacity)
        {
            if (!(capacity > 0.0))
                return 0;
            double c = capacity < MaxCapacityPeople ? capacity : MaxCapacityPeople;
            return (int)math.floor(c + 0.5);
        }

        /// <summary>claimedActive (this area's claims on the way and on site, this pass's included) &lt; capacity.</summary>
        public static bool HasRoom(int claimedActive, int capacityPeople) => claimedActive < capacityPeople;


        /// <summary>Equals SimulationUtils.GetUpdateFrameWithInterval(frame, interval, 16) (SimulationUtils.cs:153-156 (=)) for a valid
        /// interval. The result is 0 when interval ≤ 0 (no division by zero in Burst).</summary>
        public static uint SliceOf(uint frame, int interval) => interval <= 0 ? 0u : (frame / (uint)interval) & 15u;

        /// <summary>Does a system with this interval and offset run on GameSimulation update index <paramref name="frame"/>?
        /// (UpdateSystem.cs:241 (=). The index is SimulationSystem.frameIndex, SimulationSystem.cs:279-286.)</summary>
        public static bool RunsThisFrame(uint frame, int interval, int offset) => (frame & (uint)(interval - 1)) == (uint)offset;

        /// <summary>The exact-frame part of the skip rule. It holds when the chunk's UpdateFrame index is LeisureSystem's slice this frame
        /// (LeisureSystem.cs:959, :322, 1.6.2 :956, :316). It also holds when CitizenBehaviorSystem runs this frame and the index is its
        /// slice (CitizenBehaviorSystem.cs:1138, :603, 1.6.2 :1170, :1174). The job calls SkiDayTiming.SkipChunk, which adds the
        /// recent-CBS-run window.</summary>
        public static bool SkipSlice(uint updateFrameIndex, uint frame, int leisureInterval, int behaviorInterval, int behaviorOffset)
            => updateFrameIndex == SliceOf(frame, leisureInterval)
               || (RunsThisFrame(frame, behaviorInterval, behaviorOffset) && updateFrameIndex == SliceOf(frame, behaviorInterval));

        /// <summary>x &gt; 0 and a power of two (UpdateSystem requires one, UpdateSystem.cs:303-306).</summary>
        public static bool IsPowerOfTwo(int x) => x > 0 && (x & (x - 1)) == 0;

        /// <summary>ARM check on the intervals read at run time (LeisureSystem 64, LeisureSystem.cs:914-917, 1.6.2 :911-914,
        /// TripNeededSystem 16, TripNeededSystem.cs:1939-1942, CitizenBehaviorSystem 16 / offset 11, CitizenBehaviorSystem.cs:1016-1024,
        /// 1.6.2 :1048-1056). An interval not a power of two gives BadInterval. An offset outside [0, behaviorInterval) gives BadOffset.
        /// leisure &lt; 4 × trip gives LeisureTooFast. behaviorInterval ≤ 8 gives BehaviorTooFast (CBS could run twice in one render frame,
        /// which SkiDayTiming's recent-slice skip assumes it does not).</summary>
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

        /// <summary>Bits of the six game trip systems whose ComponentSystemBase.Enabled SkiDaySystem reads at each Game load and every
        /// armed pass. SkiTripSystems names them. A found system that is switched off reads "OFF". A missing one reads "missing".
        /// TripNeededToArm = LeisureSystem + CitizenBehaviorSystem. The claim uses LeisureSystem's decision and slice. Only
        /// CitizenBehaviorSystem.OnUpdate drains the car-reserve queue (CitizenBehaviorSystem.cs:1196-1210, drain :65, 1.6.2 :1235-1249,
        /// :66).</summary>
        public const int TripLeisure = 1, TripBehavior = 2, TripNeeded = 4, TripResidentAI = 8, TripResidentActions = 16,
                         TripResourceBuyer = 32, TripAll = 63, TripNeededToArm = 3;

        /// <summary>The game's own LeisureSystem and CitizenBehaviorSystem must be switched on. The claim uses LeisureSystem's decision and
        /// slice. Only CitizenBehaviorSystem.OnUpdate drains the car-reserve queue (CitizenBehaviorSystem.cs:1196-1210, 1.6.2 :1235-1249).
        /// The result is SystemOff when <paramref name="tripSystemsOn"/> lacks either bit. This test runs first (an off system's interval
        /// says nothing about what runs). Otherwise the 4-argument rule applies, unchanged. Bits beyond TripAll are ignored.</summary>
        public static ArmVerdict ArmCheck(int leisureInterval, int tripInterval, int behaviorInterval, int behaviorOffset, int tripSystemsOn)
        {
            if ((tripSystemsOn & TripNeededToArm) != TripNeededToArm)
                return ArmVerdict.SystemOff;
            return ArmCheck(leisureInterval, tripInterval, behaviorInterval, behaviorOffset);
        }


        /// <summary>year × days + clamp(floor(date × days), 0, days − 1), days = max(1, daysPerYear). A non-finite date reads day 0. The
        /// same float arithmetic as SkiMoneyRules.MonthKey, so at 12 days a year the two are equal (the tourist roll key). The one day key
        /// of the mod.</summary>
        public static int DayKey(int year, float normalizedDate, int daysPerYear)
        {
            int days = math.max(1, daysPerYear);
            int d = math.isfinite(normalizedDate) ? (int)math.floor(normalizedDate * days) : 0;
            return year * days + math.clamp(d, 0, days - 1);
        }

        /// <summary>A once-a-day measurement or WARN re-arms at the first tick at or after 12:00 of a day other than the one it was done on
        /// (SkiDaySystem.MaybeRearmCost). NaN time gives false. Also used by SkiResortAttractivenessSystem and
        /// SkiLiftTicketSystem.</summary>
        public static bool MiddayRearmDue(bool done, float timeOfDay, int day, int doneDay) => done && timeOfDay >= 0.5f && day != doneDay;

        /// <summary>The day a done measurement counts for. It is today when done at or after 12:00, else int.MinValue (none: it re-arms
        /// today at midday). NaN gives int.MinValue.</summary>
        public static int DoneDayOf(float timeOfDay, int day) => timeOfDay >= 0.5f ? day : int.MinValue;

        /// <summary>A midday-re-armed measurement that has not written its line when the game day changes
        /// is written partial: not logged, armed at a midday (not the load's measurement), and the day moved on.</summary>
        public static bool CostDayEnded(bool logged, bool armedAtMidday, int armDay, int dayNow) => !logged && armedAtMidday && dayNow != armDay;

        /// <summary>Before the first month: Key = NoMonth, every flag false.</summary>
        public static MonthFlags NewMonthFlags() => new MonthFlags { Key = NoMonth };

        /// <summary>SkiResortLedger.Advance (:94-133) rule for rule, on the flags only: Started (first key: ThisSinceLoad), None (same
        /// key), Rolled (key = Key + 1, Key ≠ int.MaxValue: LastKnown, LastSinceLoad = ThisSinceLoad, ThisSinceLoad false), Jumped (any
        /// other key: LastKnown and LastSinceLoad false, ThisSinceLoad true). Counting restarts as after a load. The key comes from
        /// SkiMoneyRules.MonthKey. It equals Advance(ref f, key, closedShort: false).</summary>
        public static MonthStep Advance(ref MonthFlags f, int key) => Advance(ref f, key, false);

        /// <summary>As Advance(ref f, key), except that a Rolled step also marks the closed month partial when
        /// <paramref name="closedShort"/> (too few of its frames were simulated: a developer time jump that landed exactly one month later,
        /// SkiMoneyRules.IsShortMonth). LastSinceLoad = ThisSinceLoad || closedShort. It equals SkiResortLedger.Advance(key,
        /// closedMonthShort). A short flag never changes Started, None or Jumped.</summary>
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


        /// <summary>Distance band (straight line, meters): &lt; 1 km gives 0, &lt; 3 gives 1, &lt; 5 gives 2, &lt; 7 gives 3, else 4. NaN
        /// or negative gives 0.</summary>
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

        /// <summary>The main mode of the vehicles a guest was seen in. Literal values: the measure rows index by them.</summary>
        public enum ArrivalMode : byte { Foot = 0, Car = 1, Transit = 2, Taxi = 3, Bike = 4 }
        public const int ArrivalModes = 5;
        // Leg bits (SkiDayClaim.Legs) and transit kind bits (SkiDayClaim.Kinds).
        public const byte LegCar = 1, LegTransit = 2, LegTaxi = 4, LegBike = 8, LegOther = 16;
        public const byte KindBus = 1, KindTram = 2, KindTrain = 4, KindSubway = 8, KindBoat = 16, KindOther = 32;
        public const int KindBits = 6;
        // Game.Prefabs.TransportType literals (1.6.2 Game.Prefabs/TransportType.cs:5-19, (=) 1.6.0). They are an int here so this file
        // needs no Game type. Only a rebuild against a changed Game.dll moves them (SkiDayTests T rows).
        public const int TransportBus = 0, TransportTrain = 1, TransportTram = 3, TransportShip = 4, TransportSubway = 8, TransportFerry = 11;

        /// <summary>The leg of one vehicle, from the LEAD vehicle's components (the Controller's, for a train or tram car). A personal car
        /// with the Bicycle tag gives Bike (a bike is a PersonalCar vehicle). A personal car gives Car. A taxi gives Taxi. Public transport
        /// (not evacuating, not a prisoner transport: the caller tests) gives Transit. Anything else (police car, ambulance, hearse, a
        /// bicycle tag alone) gives Other. No vehicle carries both Taxi and PublicTransport, so their order does not matter.</summary>
        public static byte LegOf(bool personalCar, bool bicycle, bool taxi, bool publicTransport)
        {
            if (personalCar)
                return bicycle ? LegBike : LegCar;
            if (taxi)
                return LegTaxi;
            return publicTransport ? LegTransit : LegOther;
        }

        /// <summary>The kind bit of a public-transport vehicle's prefab TransportType (int, −1 unknown): Bus, Tram, Train and Subway as
        /// named. Ship or Ferry gives Boat. Anything else gives Other.</summary>
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

        /// <summary>"Mode seen": Car if any car leg, else Transit, else Taxi, else Bike, else Foot (an Other leg alone counts as on foot).
        /// Every legs byte maps to exactly one mode.</summary>
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

        /// <summary>Why a decision did not fit (ClaimFits false) at an area.</summary>
        public enum Fit : byte { Fits = 0, NoTimeBeforeClosing = 1, NoTimeBeforeOwnDeadline = 2, Invalid = 3 }

        /// <summary>A negative, NaN or infinite travel time gives Invalid. If ClaimFits is true, the result is Fits. Otherwise the earlier
        /// of the two deadlines is named: vanilla's strictly earlier gives NoTimeBeforeOwnDeadline, else NoTimeBeforeClosing (frames
        /// compared as ClaimDeadline compares them). FitVerdict == Fits ⇔ ClaimFits (SkiDayTests T).</summary>
        public static Fit FitVerdict(uint now, uint vanillaDeadline, uint closingFrame, double travelHours)
            => FitVerdict(now, vanillaDeadline, closingFrame, travelHours, MinStayHours);

        /// <summary>FitVerdict with the stay reserve as an input (TripKnobs.StayHours). A stay that is not finite or below 0 reads
        /// MinStayHours, as ClaimFits does. FitVerdict(…, s) == Fits ⇔ ClaimFits(…, s).</summary>
        public static Fit FitVerdict(uint now, uint vanillaDeadline, uint closingFrame, double travelHours, double stayHours)
        {
            if (!(travelHours >= 0.0) || !math.isfinite(travelHours))
                return Fit.Invalid;
            if (ClaimFits(now, vanillaDeadline, closingFrame, travelHours, stayHours))
                return Fit.Fits;
            return vanillaDeadline < closingFrame ? Fit.NoTimeBeforeOwnDeadline : Fit.NoTimeBeforeClosing;
        }

        /// <summary>The date of an hour of the year, (float)(hourKey / (24 × max(1, days))). It is what RunSnowRules.HourAlignedDate
        /// returns for that hour key (RunSnowRules.cs:242-249), bit for bit.</summary>
        public static float HourDate(int hourKey, int daysPerYear)
        {
            int days = daysPerYear < 1 ? 1 : daysPerYear;
            return (float)(hourKey / (24.0 * days));
        }

        /// <summary>The calendar month 0..11 of an hour of the year: clamp(floor(hourKey × 12 / (24 × max(1, days))),
        /// 0, 11) (at 12 days a year: hourKey / 24). Integer arithmetic, exact.</summary>
        public static int MonthOfHour(int hourKey, int daysPerYear)
        {
            int days = daysPerYear < 1 ? 1 : daysPerYear;
            long m = (long)hourKey * 12L / (24L * days);
            return m <= 0L ? 0 : (m >= 11L ? 11 : (int)m);
        }
    }
}
