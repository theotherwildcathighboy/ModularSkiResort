// SkiDayTiming - the pure timing helpers of SkiDaySystem: the render-frame slice window (the skip rule beyond
// SkiDayRules.SkipSlice: a CitizenBehaviorSystem run up to 7 frames earlier in the same render frame), the last pass of a
// decision and its last unskipped sighting, the next LeisureSystem visit (the resident roll key), frames <-> game hours, a
// claim's outcome, the hour step of the powder ring, and bit-exact float / double compares. Every demand, capacity, powder,
// confidence and gate rule is SkiDayRules'; this file only does time. Consumers: SkiDaySystem and its Burst jobs;
// tests/ResortBatch SkiDayTimingTests.
//
// Pure file: `using System;` and `using Unity.Mathematics;` only. Every method is static, allocation-free and
// Burst-callable (no managed type, no string, no exception), with one exception: MarkHoursPerMonth takes managed arrays and
// runs on the main thread only (SkiDaySystem.PowderYearTick), never from a job. net48 traps: no Math.Clamp, MathF,
// double.IsFinite, Span<T>; math.* stands in.
//
// Measure-only helpers (nothing here changes a guest unless noted): where a failed guest stood (Place), the claim's
// markers, why Leisure went (GoneWhy, from the traces vanilla leaves), why a visit ended (EndCause) and how the guest left
// the lodge (ExitWay), late arrivals after expiry (TryLate), the car-reserve buckets, the guest-scan timing model
// (GuestScanSaw), clock and stay bins, the year's powder hours per month, the departure rule DepartedNow, ArrivedThenRemoved
// (EndCause.RemovedOnArrival = 8, EndCauseCount 9), MovedOffNow (the door wait ends when the game removes the walker's
// Divert{PathFailed}), RemoveWhy / RemoveWhyOf (why the Leisure went before the deadline, CitizenBehaviorSystem's order), the
// household-need register ShiftNeed / NeedTaken, the follow after leaving the lodge (FollowWatchFrames, FollowExitKind,
// PlacedNow, TryFollow, StillOutOf) and walkers near a lodge (WalkerKindOf, WaitHoursOfTimer, WithinXZ, FollowSlot,
// AtOnceSlot), and what an ended claim tells the all-outcomes speed (SpeedEvidence, FailureEvidence, LateEvidence: one
// predicate, departed + on the road + not turned back).
// PullDeadline (Flag2DeadlinePulled 64, PullMinFrames, PullNone / PullNoRun / PullNightClosed) is the only helper here that
// feeds a vanilla write (ResolveJob lowers Leisure.m_LastPossibleFrame).

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiDayTiming
    {
        /// <summary>At most this many simulation frames per render frame (SimulationSystem.cs:255); command buffers play back once
        /// per render frame (SystemOrder.cs:49), so a CitizenBehaviorSystem run fewer frames back than this may still have its
        /// command buffer pending</summary>
        public const int MaxFramesPerRenderFrame = SkiDayRules.MaxFramesPerRenderFrame;

        /// <summary>An arrived ski day stays "on site" this long past its deadline (= FramesFor(2), two game hours) before it reads as Left</summary>
        public const uint OnSiteGraceFrames = 21845;

        /// <summary>One claim's verdict at a pass. Literal values: the DUMP counters index by them.</summary>
        public enum ClaimOutcome : byte { OnTheWay = 0, Arrived = 1, OnSite = 2, Left = 3, Gone = 4, Retargeted = 5, Expired = 6 }

        /// <summary>Did CitizenBehaviorSystem run fewer than MaxFramesPerRenderFrame frames before (or at)
        /// <paramref name="frame"/>? Its last run is <c>frame - (frame - offset) mod interval</c> (it runs on
        /// <c>(frame &amp; (interval - 1)) == offset</c>, UpdateSystem.cs:241); <paramref name="slice"/> is that run's UpdateFrame
        /// slice (CitizenBehaviorSystem.cs:1138, 1.6.2 :1170). False when the interval is &lt;= 0 (no division by zero in Burst),
        /// the offset is negative, or the frame precedes the offset.</summary>
        public static bool RecentBehaviorSlice(uint frame, int behaviorInterval, int behaviorOffset, out uint slice)
        {
            slice = 0u;
            if (behaviorInterval <= 0 || behaviorOffset < 0 || frame < (uint)behaviorOffset)
                return false;
            uint last = frame - (frame - (uint)behaviorOffset) % (uint)behaviorInterval;
            if (frame - last >= (uint)MaxFramesPerRenderFrame)
                return false;
            slice = SkiDayRules.SliceOf(last, behaviorInterval);
            return true;
        }

        /// <summary>The skip rule for one chunk: LeisureSystem's slice this frame or CitizenBehaviorSystem's on its frames
        /// (SkiDayRules.SkipSlice, the exact-frame part), plus the slice of a CitizenBehaviorSystem run fewer than 8 frames back,
        /// whose command buffer may not have played back yet</summary>
        public static bool SkipChunk(uint chunkSlice, uint frame, int leisureInterval, int behaviorInterval, int behaviorOffset)
        {
            if (SkiDayRules.SkipSlice(chunkSlice, frame, leisureInterval, behaviorInterval, behaviorOffset))
                return true;
            return RecentBehaviorSlice(frame, behaviorInterval, behaviorOffset, out uint slice) && slice == chunkSlice;
        }

        /// <summary>True when LeisureSystem takes this chunk at its next run (frame + interval): the last pass at which a decision of
        /// this chunk can be seen (a decision enters a pool exactly once)</summary>
        public static bool IsLastPass(uint chunkSlice, uint frame, int leisureInterval)
        {
            return chunkSlice == ((SkiDayRules.SliceOf(frame, leisureInterval) + 1u) & 15u);
        }

        /// <summary>True when no later pass before LeisureSystem takes this chunk sees it unskipped: every pass at
        /// <c>frame + k x interval</c>, k = 1 ... d - 1 (d = intervals to LeisureSystem's visit), skips it (SkipChunk). Equals
        /// IsLastPass except where the recent-CBS window skips the slice on its last pass: then the pass before is the last sighting
        /// (otherwise 32 of the 64 LeisureSystem offsets lose one slice's unclaimed decisions). False for LeisureSystem's own slice
        /// this frame (d = 0) and for an interval &lt;= 0. At most 14 SkipChunk calls.</summary>
        public static bool IsLastSighting(uint chunkSlice, uint frame, int leisureInterval, int behaviorInterval, int behaviorOffset)
        {
            if (leisureInterval <= 0)
                return false;
            uint d = (chunkSlice - SkiDayRules.SliceOf(frame, leisureInterval)) & 15u;
            if (d == 0u)
                return false;
            for (uint k = 1u; k < d; k++)
            {
                if (!SkipChunk(chunkSlice, frame + k * (uint)leisureInterval, leisureInterval, behaviorInterval, behaviorOffset))
                    return false;
            }
            return true;
        }

        /// <summary>The frame at which LeisureSystem takes this chunk's decisions (LeisureSystem.cs:322, :959; 1.6.2 :316, :956):
        /// <c>frame + interval x ((chunkSlice - SliceOf(frame)) &amp; 15)</c>. Constant over the up to 15 passes that see one
        /// decision, new for every later one: the resident roll key. <paramref name="frame"/> itself for an interval &lt;= 0 (never
        /// armed then).</summary>
        public static uint NextLeisureVisit(uint chunkSlice, uint frame, int leisureInterval)
        {
            if (leisureInterval <= 0)
                return frame;
            return frame + (uint)leisureInterval * ((chunkSlice - SkiDayRules.SliceOf(frame, leisureInterval)) & 15u);
        }

        /// <summary>The track row of (area, travel class): area x 3 + class; -1 for an orphan (area &lt; 0) or a class that is not
        /// claimable (&gt; NoCarFar), so no job ever indexes before or past the list</summary>
        public static int TrackIndex(int area, byte travelClass)
        {
            return area < 0 || travelClass > 2 ? -1 : area * 3 + travelClass;
        }

        /// <summary>floor(hours x FramesPerHour); 0 for &lt;= 0 or NaN; uint.MaxValue at or above uint.MaxValue / FramesPerHour
        /// (393,215.9999 h) and for +infinity: an out-of-range double -> uint cast is unspecified in C#, and Burst and Mono may differ</summary>
        public static uint FramesFor(double hours)
        {
            if (!(hours > 0.0))
                return 0u;
            if (hours >= uint.MaxValue / SkiDayRules.FramesPerHour)
                return uint.MaxValue;
            return (uint)math.floor(hours * SkiDayRules.FramesPerHour);
        }

        /// <summary>frames / FramesPerHour (game hours)</summary>
        public static double HoursFor(uint frames)
        {
            return frames / SkiDayRules.FramesPerHour;
        }

        /// <summary>A claim's outcome at a pass. On the way (<paramref name="onSite"/> false): Arrived when it has Leisure aimed at
        /// the lodge and stands in it; else Expired at <c>now &gt;= deadline</c> (vanilla's own compare, LeisureSystem.cs:314, 1.6.2
        /// :308); else Gone without Leisure; else Retargeted (Leisure aims elsewhere); else OnTheWay. On site: OnSite while all three
        /// hold and now &lt; deadline + OnSiteGraceFrames (64-bit, no wrap), else Left (a finished day, not an outcome).</summary>
        public static ClaimOutcome Resolve(bool onSite, bool hasLeisure, bool leisureAtLodge, bool atLodge, uint now, uint deadline)
        {
            bool there = hasLeisure && leisureAtLodge && atLodge;
            if (!onSite)
            {
                if (there)
                    return ClaimOutcome.Arrived;
                if (now >= deadline)
                    return ClaimOutcome.Expired;
                if (!hasLeisure)
                    return ClaimOutcome.Gone;
                if (!leisureAtLodge)
                    return ClaimOutcome.Retargeted;
                return ClaimOutcome.OnTheWay;
            }
            return there && (ulong)now < (ulong)deadline + OnSiteGraceFrames ? ClaimOutcome.OnSite : ClaimOutcome.Left;
        }

        /// <summary>The powder ring's step: 24 (refill) when there is no previous hour or the hour jumped; 0 for the same hour; 1 when
        /// <paramref name="hourKey"/> is the next hour of the year (24 x max(1, days) hours, wrapping at the year end)</summary>
        public static int HourStep(int prevHourKey, int hourKey, int daysPerYear)
        {
            if (prevHourKey < 0)
                return 24;
            if (hourKey == prevHourKey)
                return 0;
            long hours = 24L * math.max(1, daysPerYear);
            return (prevHourKey + 1L) % hours == hourKey ? 1 : 24;
        }

        /// <summary>Bit-exact float compare (NaN equals NaN; 0 and -0 differ): SkiDayAreaView.Same</summary>
        public static bool SameBits(float a, float b)
        {
            return math.asint(a) == math.asint(b);
        }

        /// <summary>Bit-exact double compare (NaN equals NaN; 0 and -0 differ)</summary>
        public static bool SameBits(double a, double b)
        {
            return math.aslong(a) == math.aslong(b);
        }


        /// <summary>A visit that ended (Left) is watched this long for the guest to leave the lodge (state 2): 2 game hours = FramesFor(2)</summary>
        public const uint ExitWatchFrames = 21845;

        /// <summary>An expired claim is watched this long for a late arrival (state 3): 6 game hours = FramesFor(6). The game checks
        /// the deadline only at the venue.</summary>
        public const uint LateWatchFrames = 65536;

        /// <summary>LeisureSystem ends a visit when m_LeisureCounter &gt; 255 - efficiency / 5 (LeisureSystem.cs:314, 1.6.2 :308);
        /// our lodge has efficiency 10, so above 253. CitizenHappinessSystem lowers the counter by m_AmountLeisureCounterDecrease
        /// with a 2 % (resident) or 20 % (tourist) chance per update (CitizenHappinessSystem.cs:498-500, 1.6.2 :526-528), each
        /// citizen once per 256 frames; the shipped LeisureParameters asset sets the decrease to 1, so the counter falls at most 1
        /// per 256 frames and a counter at or above 250 when the visit ends is a sound "need filled" sign. Still inferred (we never
        /// see the removal itself), and every log text says "inferred".</summary>
        public const int NeedFilledCounter = 250;

        // The claim's flag bits (SkiDaySystem.Jobs.cs ClaimFlag / ClaimFlag2 use these values), repeated here so the tests can reach
        // them. FlagHeldAtHome is kept but never set (it never filled: every queued car claim had left home at our first look).
        public const byte FlagDeparted = 1, FlagHeldAtHome = 2, FlagHeldEver = 4, FlagRode = 8, FlagPathFailedAtStart = 16,
                          FlagDeadlineIsClosing = 32, FlagLateArrived = 64;
        public const byte Flag2TurnedBack = 1, Flag2WalkerLost = 2, Flag2WalkerPathFailed = 4, Flag2VehiclePathFailed = 8;
        /// <summary>The household-need register in Flags2: need open at the last pass / at the pass before (ShiftNeed). Not markers:
        /// HasMarker reads only bits 1, 2, 4, 8 of Flags2.</summary>
        public const byte Flag2NeedOpen = 16, Flag2NeedOpenBefore = 32;
        /// <summary>The claim's deadline was pulled in (PullDeadline). ShiftNeed keeps it.</summary>
        public const byte Flag2DeadlinePulled = 64;

        /// <summary>"Placed by the game" = Citizen.m_PenaltyCounter at or above this. The placement writes 255
        /// (TripNeededSystem.cs:1597; CitizenTravelPurposeSystem.cs:481, 1.6.2 :490) and nothing else raises it;
        /// CitizenHappinessSystem lowers it by 1 per citizen update (CitizenHappinessSystem.cs:502, 1.6.2 :530), once per 256 frames,
        /// so 250 marks a placement less than about 1,500 frames old.</summary>
        public const int PlacedMarkMin = 250;

        /// <summary>A visit shorter than this many game hours counts as "fast"</summary>
        public const double FastStayHours = 0.2;

        // Game.Citizens.Purpose literals (1.6.2 Game.Citizens/Purpose.cs, same in 1.6.0). Bytes, so this file needs no Game type;
        // only a rebuild against a changed Game.dll could move them.
        public const byte PurposeShopping = 1, PurposeLeisure = 2, PurposeGoingHome = 3, PurposeGoingToWork = 4, PurposeWorking = 5,
                          PurposeSleeping = 6, PurposeMovingAway = 8, PurposeStudying = 9, PurposeGoingToSchool = 10,
                          PurposeHospital = 12, PurposeInHospital = 24, PurposeDeathcare = 25, PurposeInDeathcare = 26,
                          PurposeTraveling = 30, PurposeRelaxing = 31, PurposeSightseeing = 32, PurposeVisitAttractions = 33,
                          PurposePathFailed = 38;

        /// <summary>Where the citizen stood when a claim failed. "Home" is home or hotel (the origin).</summary>
        public enum Place : byte { HomeNeverLeft = 0, HomeCameBack = 1, Road = 2, OtherBuilding = 3, Lodge = 4, Deleted = 5 }
        public const int PlaceCount = 6;

        /// <summary>gone -> Deleted; in no building -> Road; at the lodge -> Lodge; at the origin -> HomeCameBack when it had departed,
        /// else HomeNeverLeft; else OtherBuilding. A guest who left and came back between two passes reads HomeNeverLeft (with the
        /// TurnedBack marker when its trip home was seen).</summary>
        public static Place PlaceOf(bool gone, bool inBuilding, bool atOrigin, bool atLodge, bool departed)
        {
            if (gone)
                return Place.Deleted;
            if (!inBuilding)
                return Place.Road;
            if (atLodge)
                return Place.Lodge;
            if (atOrigin)
                return departed ? Place.HomeCameBack : Place.HomeNeverLeft;
            return Place.OtherBuilding;
        }

        /// <summary>The departure rule for a claim not yet marked departed: true when the citizen is out of the origin and (in no
        /// building, or TravelPurpose Leisure, or in the lodge). A claim is made only for a citizen in a building, and
        /// TripNeededSystem removes CurrentBuilding in the command buffer that spawns the walker (1.6.2
        /// TripNeededSystem.cs:1615-1627), so "in no building" alone proves the guest left: a car guest whose path failed and who
        /// was turned home before our next pass is on the road with GoingHome, and must read as departed (else a later failure at
        /// home books "never left"). In another building only TravelPurpose Leisure or the lodge counts (an errand building without
        /// our purpose is not our trip).</summary>
        public static bool DepartedNow(bool inBuilding, bool atOrigin, bool atLodge, bool hasPurpose, byte purpose)
        {
            if (atOrigin)
                return false;
            return !inBuilding || atLodge || (hasPurpose && purpose == PurposeLeisure);
        }

        /// <summary>The walker has moved off: departed, in no building, a live walker that no longer carries Divert{PathFailed}.
        /// TripNeededSystem marks a walker whose first route came back empty with that divert (TripNeededSystem.cs:681-686);
        /// ResidentAISystem keeps it waiting while the divert counter is at or below 250 and then clears the divert
        /// (ResidentAISystem.cs:2580-2590, RemoveComponent at :2790), so the mark's absence is the game's own end of the door wait.
        /// A walker with no mark at our first look moved off at once. The wait is at least 126 counter steps of 1-2 per 16-frame
        /// tick (2,016 frames, 31 passes), so a 64-frame pass cannot miss all of it.</summary>
        public static bool MovedOffNow(bool departed, bool inBuilding, bool walkerLive, bool pathFailedMark)
        {
            return departed && !inBuilding && walkerLive && !pathFailedMark;
        }

        /// <summary>Print "(under 0.5 km at ...)" only for a claim that started beyond 500 m: an origin inside 500 m is "near" at its
        /// first look (+0.0 h) and says nothing. NaN distance -> false.</summary>
        public static bool ShowNear500(float distanceM, uint near500Frame)
        {
            return near500Frame != 0u && distanceM > 500f;
        }

        // The five markers of a claim, read at our passes, so "not set" means "not seen", never "did not happen" (path flags last
        // only until the AI acts on them)
        public const int MarkerPathFailedAtStart = 0, MarkerWalkerPathFailed = 1, MarkerVehiclePathFailed = 2, MarkerWalkerLost = 3,
                         MarkerTurnedBack = 4, MarkerCount = 5;

        /// <summary>Is <paramref name="marker"/> (Marker...) set in the claim's flag bytes? False for any other value.</summary>
        public static bool HasMarker(byte flags, byte flags2, int marker)
        {
            switch (marker)
            {
                case MarkerPathFailedAtStart: return (flags & FlagPathFailedAtStart) != 0;
                case MarkerWalkerPathFailed: return (flags2 & Flag2WalkerPathFailed) != 0;
                case MarkerVehiclePathFailed: return (flags2 & Flag2VehiclePathFailed) != 0;
                case MarkerWalkerLost: return (flags2 & Flag2WalkerLost) != 0;
                case MarkerTurnedBack: return (flags2 & Flag2TurnedBack) != 0;
                default: return false;
            }
        }

        /// <summary>Why a claim's Leisure went (Gone) or moved (Retargeted), from the traces vanilla leaves</summary>
        public enum GoneWhy : byte
        {
            Unknown = 0, Ill = 1, MovingAway = 2, Bedtime = 3, WorkOrSchool = 4, Shopping = 5, HeadingHome = 6, OtherTrip = 7, Deleted = 8,
        }
        public const int GoneWhyCount = 9;

        /// <summary>First match: gone -> Deleted; ill (HealthProblem) -> Ill; household MovingAway -> MovingAway; TravelPurpose
        /// Sleeping -> Bedtime (CitizenBehaviorSystem's CheckSleep adds it in the pass that removes Leisure, 1.6.2 :329-341, :784;
        /// Sleeping only at home; away from home bedtime adds a GoingHome trip instead, :343-346, which reads HeadingHome here); a
        /// work or school purpose or first trip -> WorkOrSchool; ResourceBuyer or a shopping purpose or trip -> Shopping (GoShopping,
        /// :417-432, :798); GoingHome -> HeadingHome; any purpose or trip other than Leisure -> OtherTrip; else Unknown (work time
        /// removes Leisure before the trip exists, :771-777, so it often reads Unknown: unverified how often).</summary>
        public static GoneWhy WhyOf(bool gone, bool ill, bool movingAway, bool hasPurpose, byte purpose, bool hasTrip, byte tripPurpose,
                                    bool resourceBuyer)
        {
            if (gone)
                return GoneWhy.Deleted;
            if (ill)
                return GoneWhy.Ill;
            if (movingAway)
                return GoneWhy.MovingAway;
            if (hasPurpose && purpose == PurposeSleeping)
                return GoneWhy.Bedtime;
            if ((hasPurpose && IsWorkOrSchool(purpose)) || (hasTrip && IsWorkOrSchool(tripPurpose)))
                return GoneWhy.WorkOrSchool;
            if (resourceBuyer || (hasPurpose && purpose == PurposeShopping) || (hasTrip && tripPurpose == PurposeShopping))
                return GoneWhy.Shopping;
            if ((hasPurpose && purpose == PurposeGoingHome) || (hasTrip && tripPurpose == PurposeGoingHome))
                return GoneWhy.HeadingHome;
            if ((hasPurpose && purpose != PurposeLeisure) || (hasTrip && tripPurpose != PurposeLeisure))
                return GoneWhy.OtherTrip;
            return GoneWhy.Unknown;
        }

        private static bool IsWorkOrSchool(byte p)
        {
            return p == PurposeGoingToWork || p == PurposeGoingToSchool || p == PurposeWorking || p == PurposeStudying;
        }

        /// <summary>Why an arrived guest's visit ended (the deadline is split by what bound it). RemovedOnArrival: found in the lodge
        /// with Leisure gone within one pass of arriving; booked by ResolveJob's Gone branch (ArrivedThenRemoved), never returned by
        /// EndCauseOf, and forced whatever the leisure counter reads (an on-arrival counter-full removal by LeisureSystem is not told
        /// apart from CitizenBehaviorSystem's; the "why" is RemoveWhyOf's).</summary>
        public enum EndCause : byte
        {
            DeadlineOwn = 0, DeadlineClosing = 1, NeedFilled = 2, NewOuting = 3, Removed = 4, WalkedOut = 5, PastGrace = 6, Deleted = 7,
            RemovedOnArrival = 8,
        }
        public const int EndCauseCount = 9;

        /// <summary>At the Left verdict, in this order: gone -> Deleted; still "there" (Leisure aimed at the lodge and in it: Left
        /// fires there only past the grace) -> PastGrace; now >= deadline (Resolve's compare) -> DeadlineClosing when the claim's
        /// deadline was the lifts' closing, else DeadlineOwn (sleep or work); Leisure aimed elsewhere -> NewOuting; no Leisure and a
        /// counter >= NeedFilledCounter -> NeedFilled (inferred); no Leisure -> Removed (CitizenBehaviorSystem: moving away, illness,
        /// shopping errand; bedtime and work time end at the claim's deadline, since DoLeisure sets it to the next sleep or work
        /// start, 1.6.2 :472-494, and the claim deadline is min(that, closing), so they read DeadlineOwn / DeadlineClosing; why:
        /// RemoveWhyOf); else (Leisure at the lodge, not in it) WalkedOut. Not told apart: LeisureSystem.SpendLeisure also removes
        /// Leisure at a venue that is switched off (BuildingOption.Inactive), has ServiceAvailable &lt;= 0 or no output resource
        /// (1.6.2 LeisureSystem.cs:285-310); for our lodge that is the player deactivating it, which lands in Removed (or NeedFilled
        /// with a high counter). Rare, and visible in the `[lodges]` line.</summary>
        public static EndCause EndCauseOf(bool gone, bool hasLeisure, bool leisureAtLodge, bool atLodge, uint now, uint deadline,
                                          bool deadlineIsClosing, int leisureCounter)
        {
            if (gone)
                return EndCause.Deleted;
            if (hasLeisure && leisureAtLodge && atLodge)
                return EndCause.PastGrace;
            if (now >= deadline)
                return deadlineIsClosing ? EndCause.DeadlineClosing : EndCause.DeadlineOwn;
            if (hasLeisure && !leisureAtLodge)
                return EndCause.NewOuting;
            if (!hasLeisure)
                return leisureCounter >= NeedFilledCounter ? EndCause.NeedFilled : EndCause.Removed;
            return EndCause.WalkedOut;
        }


        /// <summary>A claim that Resolve calls Gone (on the way, before the deadline, no Leisure) whose citizen still exists and
        /// stands in the claim's lodge arrived and lost Leisure within one pass. Booked as an arrival whose visit ended
        /// RemovedOnArrival. CurrentBuilding is the game's own venue test (LeisureSystem.cs:342-345, 1.6.2 :336-339) and PlaceOf's
        /// "lodge". Expired is tested first by Resolve, so a guest found there past the deadline stays an expiry (late watch). A
        /// deleted citizen stays a failure (place "deleted").</summary>
        public static bool ArrivedThenRemoved(ClaimOutcome outcome, bool gone, bool atLodge)
        {
            return outcome == ClaimOutcome.Gone && !gone && atLodge;
        }


        /// <summary>Why the Leisure went before the deadline: the first trace in CitizenBehaviorSystem's order (1.6.0 :673-773, 1.6.2
        /// :698-802), then LeisureSystem's (1.6.0 :289-318, 1.6.2 :283-312), then the game's placement (TripNeededSystem.cs:1562-1599).
        /// Literal values: the measure rows and s_RemoveWhyWords index by them.</summary>
        public enum RemoveWhy : byte
        {
            Unknown = 0, MoneyCheck = 1, MovingAway = 2, Ill = 3, WorkTime = 4, Bedtime = 5, Shopping = 6, VenueOff = 7,
            NeedMet = 8, NoPurpose = 9,
        }
        public const int RemoveWhyCount = 10;

        /// <summary>First match: moving away (household marker, or a MovingAway purpose or trip) -> MoneyCheck when the household's
        /// reason is the tourist hotel money check (TouristLeaveSystem.cs:68), else MovingAway; ill; work time now or a work or school
        /// purpose or trip; bedtime now; ResourceBuyer, the household need just taken, or a Shopping purpose or trip; the lodge
        /// switched off (LeisureSystem.cs:291, 1.6.2 :285); counter >= NeedFilledCounter (inferred); out of the lodge with no
        /// TravelPurpose (placed by the game, TripNeededSystem.cs:1562-1599); else Unknown. Moving away comes before illness as in
        /// CitizenBehaviorSystem (1.6.2 :698 before :756). The money flag alone means nothing (it is read only with moving away).</summary>
        public static RemoveWhy RemoveWhyOf(bool movingAway, bool moneyCheck, bool ill, bool workTimeNow, bool bedtimeNow,
                                            bool hasPurpose, byte purpose, bool hasTrip, byte tripPurpose, bool resourceBuyer,
                                            bool needTaken, bool venueOff, int leisureCounter, bool atLodge)
        {
            if (movingAway || (hasPurpose && purpose == PurposeMovingAway) || (hasTrip && tripPurpose == PurposeMovingAway))
                return movingAway && moneyCheck ? RemoveWhy.MoneyCheck : RemoveWhy.MovingAway;
            if (ill)
                return RemoveWhy.Ill;
            if (workTimeNow || (hasPurpose && IsWorkOrSchool(purpose)) || (hasTrip && IsWorkOrSchool(tripPurpose)))
                return RemoveWhy.WorkTime;
            if (bedtimeNow)
                return RemoveWhy.Bedtime;
            if (resourceBuyer || needTaken || (hasPurpose && purpose == PurposeShopping) || (hasTrip && tripPurpose == PurposeShopping))
                return RemoveWhy.Shopping;
            if (venueOff)
                return RemoveWhy.VenueOff;
            if (leisureCounter >= NeedFilledCounter)
                return RemoveWhy.NeedMet;
            if (!atLodge && !hasPurpose)
                return RemoveWhy.NoPurpose;
            return RemoveWhy.Unknown;
        }

        /// <summary>Shift "household need open" into Flag2NeedOpen and the old value into Flag2NeedOpenBefore; every other bit of
        /// Flags2 (the markers 1-8, Flag2DeadlinePulled 64, 128) is kept.</summary>
        public static byte ShiftNeed(byte flags2, bool openNow)
        {
            int keep = flags2 & ~(Flag2NeedOpen | Flag2NeedOpenBefore);
            return (byte)(keep | ((flags2 & Flag2NeedOpen) != 0 ? Flag2NeedOpenBefore : 0) | (openNow ? Flag2NeedOpen : 0));
        }

        /// <summary>The need was open at one of the last two passes and is empty now. CitizenBehaviorSystem clears it by a direct write
        /// when it sends a member shopping (1.6.0 :765-766, 1.6.2 :794-795) while the Leisure removal goes through a command buffer
        /// (1.6.2 :798), so a pass can fall between the two: hence two bits. The caller adds "adult or elderly"
        /// (CitizenBehaviorSystem 1.6.0 :759, 1.6.2 :788).</summary>
        public static bool NeedTaken(byte flags2, bool openNow)
        {
            return (flags2 & (Flag2NeedOpen | Flag2NeedOpenBefore)) != 0 && !openNow;
        }

        /// <summary>A stay shorter than FastStayHours; NaN or negative -> false</summary>
        public static bool IsFastStay(double hours)
        {
            return hours >= 0.0 && hours < FastStayHours;
        }

        /// <summary>How a guest whose visit ended left the lodge: the TravelPurpose of the trip out, or the watch's end</summary>
        public enum ExitWay : byte
        {
            Home = 0, WorkOrSchool = 1, Shopping = 2, AnotherOuting = 3, MovingAway = 4, Health = 5, PathFailed = 6, Other = 7,
            NoPurpose = 8, StillInside = 9, Deleted = 10,
        }
        public const int ExitWayCount = 11;

        /// <summary>Exit check (state 2, and once at the visit end): gone -> Deleted; out of the lodge -> ExitOfPurpose; inside for
        /// ExitWatchFrames since the visit end -> StillInside; else false (keep watching).</summary>
        public static bool TryExit(bool gone, bool atLodge, bool hasPurpose, byte purpose, uint now, uint endFrame, out ExitWay way)
        {
            if (gone)
            {
                way = ExitWay.Deleted;
                return true;
            }
            if (!atLodge)
            {
                way = ExitOfPurpose(hasPurpose, purpose);
                return true;
            }
            if (now - endFrame >= ExitWatchFrames)
            {
                way = ExitWay.StillInside;
                return true;
            }
            way = ExitWay.Other;
            return false;
        }

        /// <summary>The exit way of a TravelPurpose: none -> NoPurpose; GoingHome -> Home; GoingToWork, GoingToSchool -> WorkOrSchool;
        /// Shopping; Leisure, Traveling, Relaxing, Sightseeing, VisitAttractions -> AnotherOuting; MovingAway; Hospital, InHospital,
        /// Deathcare, InDeathcare -> Health; PathFailed; anything else -> Other.</summary>
        public static ExitWay ExitOfPurpose(bool hasPurpose, byte purpose)
        {
            if (!hasPurpose)
                return ExitWay.NoPurpose;
            switch (purpose)
            {
                case PurposeGoingHome: return ExitWay.Home;
                case PurposeGoingToWork:
                case PurposeGoingToSchool: return ExitWay.WorkOrSchool;
                case PurposeShopping: return ExitWay.Shopping;
                case PurposeLeisure:
                case PurposeTraveling:
                case PurposeRelaxing:
                case PurposeSightseeing:
                case PurposeVisitAttractions: return ExitWay.AnotherOuting;
                case PurposeMovingAway: return ExitWay.MovingAway;
                case PurposeHospital:
                case PurposeInHospital:
                case PurposeDeathcare:
                case PurposeInDeathcare: return ExitWay.Health;
                case PurposePathFailed: return ExitWay.PathFailed;
                default: return ExitWay.Other;
            }
        }

        /// <summary>Where a guest who left the lodge with no TravelPurpose stands at the exit check. "Placed" = m_PenaltyCounter >=
        /// PlacedMarkMin (the game's teleport mark). "Home" is home or hotel (the origin). Literal values: the measure rows and
        /// s_NoPurposeAtWords index by them.</summary>
        public enum NoPurposeAt : byte { PlacedHome = 0, Home = 1, MapEdge = 2, PlacedElsewhere = 3, Elsewhere = 4, NoBuilding = 5 }
        public const int NoPurposeAtCount = 6;

        /// <summary>First match: in no building -> NoBuilding; at an outside connection -> MapEdge (a walk- or taxi-only route there
        /// is placed without the mark, TripNeededSystem.cs:1325-1339, :1535-1552); at the origin -> PlacedHome / Home; else
        /// PlacedElsewhere / Elsewhere. The empty-route placement (GoingHome at home, MovingAway at its target,
        /// TripNeededSystem.cs:1562-1599) sets the mark; CitizenTravelPurposeSystem then removes the TravelPurpose within 16 frames
        /// (:150-172, same lines in 1.6.2), hence "no trip purpose".</summary>
        public static NoPurposeAt NoPurposeAtOf(bool inBuilding, bool atOrigin, bool atOutsideConnection, int penaltyCounter)
        {
            if (!inBuilding)
                return NoPurposeAt.NoBuilding;
            if (atOutsideConnection)
                return NoPurposeAt.MapEdge;
            bool placed = penaltyCounter >= PlacedMarkMin;
            if (atOrigin)
                return placed ? NoPurposeAt.PlacedHome : NoPurposeAt.Home;
            return placed ? NoPurposeAt.PlacedElsewhere : NoPurposeAt.Elsewhere;
        }

        /// <summary>The phases of a ski-day trip (hours summed per class, counts beside them). Arrivals-only phases (1-7) are counted
        /// over the same group, so claim-to-leaving + leaving-to-arrival = the trip. LeaveToRide runs from moving off (the claim's
        /// MoveFrame) to the first ride, printed "door to the vehicle"; the door wait (DepartFrame -> MoveFrame) has its own measure
        /// block, so LeaveToArrive still runs from the departure.</summary>
        public static class Phase
        {
            public const int ClaimToLeaveAll = 0, ClaimToLeaveArrived = 1, LeaveToArrive = 2, LeaveToRide = 3, RideSpan = 4,
                             RideToArrive = 5, RideSeen = 6, HoursLeftAtArrival = 7, Stay = 8, EndToExit = 9, RoadGone = 10,
                             RoadRetargeted = 11, RoadExpired = 12;
            public const int Count = 13;
        }

        /// <summary>Minute of the day: floor(timeOfDay x 1440) clamped to 0..1439; NaN -> 0</summary>
        public static int MinuteOfDay(float timeOfDay)
        {
            if (math.isnan(timeOfDay))
                return 0;
            double m = math.floor((double)timeOfDay * 1440.0);
            return m <= 0.0 ? 0 : (m >= 1439.0 ? 1439 : (int)m);
        }

        /// <summary>The clock minute at <paramref name="frame"/> when <paramref name="startFrame"/> read
        /// <paramref name="startMinute"/>: start + (frame - startFrame) x 45 / 8192 (1440 / 262,144 minutes per frame), mod 1440,
        /// 64-bit; the frame difference wraps like the frame counter. Wrong across a developer time jump (the clock jumps, frames
        /// do not): accepted.</summary>
        public static int ClockMinuteAfter(int startMinute, uint startFrame, uint frame)
        {
            long minutes = startMinute + (long)(frame - startFrame) * 45L / 8192L;
            long m = minutes % 1440L;
            return (int)(m < 0 ? m + 1440L : m);
        }

        /// <summary>2-hour bin of the day: clamp((int)(timeOfDay x 12), 0, 11); NaN -> 0</summary>
        public static int ClockBin(float timeOfDay)
        {
            if (math.isnan(timeOfDay))
                return 0;
            double b = (double)timeOfDay * 12.0;
            return b <= 0.0 ? 0 : (b >= 11.0 ? 11 : (int)b);
        }

        /// <summary>Stay bins: &lt; 0.5 h -> 0; &lt; 1 -> 1; &lt; 2 -> 2; &lt; 4 -> 3; &lt; 6 -> 4; else 5; NaN or negative -> 0</summary>
        public static int StayBin(double hours)
        {
            if (!(hours >= 0.0))
                return 0;
            if (hours < 0.5)
                return 0;
            if (hours < 1.0)
                return 1;
            if (hours < 2.0)
                return 2;
            if (hours < 4.0)
                return 3;
            return hours < 6.0 ? 4 : 5;
        }

        /// <summary>Closest approach to the lodge: NaN or +infinity (never seen on the road) -> 3; &lt; 500 m -> 0; &lt; 2,000 m -> 1; else 2</summary>
        public static int ClosestBand(float metres)
        {
            if (!(metres < float.PositiveInfinity))
                return 3;
            if (metres < 500f)
                return 0;
            return metres < 2000f ? 1 : 2;
        }

        /// <summary>What became of an expired claim during its 6-hour watch</summary>
        public enum LateResult : byte { ArrivedLate = 0, GaveUp = 1, Deleted = 2, NotWithin6h = 3 }
        public const int LateResultCount = 4;

        /// <summary>Late watch (state 3): gone -> Deleted; at the lodge with no Leisure or with the claim's own deadline -> ArrivedLate;
        /// Leisure with another deadline (a new outing, or a second claim of the same citizen: never booked as our late arrival) or
        /// no Leisure -> GaveUp; watched LateWatchFrames since the deadline -> NotWithin6h; else false (keep watching).</summary>
        public static bool TryLate(bool gone, bool atLodge, bool hasLeisure, uint leisureDeadline, uint claimDeadline, uint now, out LateResult r)
        {
            if (gone)
            {
                r = LateResult.Deleted;
                return true;
            }
            if (atLodge && (!hasLeisure || leisureDeadline == claimDeadline))
            {
                r = LateResult.ArrivedLate;
                return true;
            }
            if (!hasLeisure || leisureDeadline != claimDeadline)
            {
                r = LateResult.GaveUp;
                return true;
            }
            if (now - claimDeadline >= LateWatchFrames)
            {
                r = LateResult.NotWithin6h;
                return true;
            }
            r = LateResult.NotWithin6h;
            return false;
        }


        /// <summary>A guest who left the lodge for home, the map exit or with no trip purpose is followed this long: one game hour =
        /// FramesFor(1)</summary>
        public const uint FollowWatchFrames = 10922;

        /// <summary>Walkers within this many meters (xz) of a lodge's center are counted</summary>
        public const float NearLodgeM = 100f;

        // Game.Creatures.ResidentFlags literals (ResidentFlags.cs:11, :13, :14, :22), so this file needs no Game type:
        // WaitingTransport is set while the walker queues at a line stop or a boarding vehicle (ResidentAISystem.cs:1517-1535);
        // Hangaround on a hang-around lane; InVehicle aboard; DummyTraffic pass-through traffic.
        public const uint ResidentWaitingTransport = 4u, ResidentHangaround = 16u, ResidentInVehicle = 32u, ResidentDummyTraffic = 8192u;

        /// <summary>How a follow ended. Literal values: the measure rows index by them.</summary>
        public enum FollowResult : byte { Home = 0, MapExit = 1, OtherBuilding = 2, BackInLodge = 3, Deleted = 4, StillOut = 5 }
        /// <summary>What a guest still out at the watch's end was doing at the last look</summary>
        public enum StillOut : byte { NoWalker = 0, Riding = 1, Waiting = 2, FailedPath = 3, OnFoot = 4 }
        /// <summary>A walker near a lodge (NearJob). Skip = aboard or pass-through, not counted.</summary>
        public enum WalkerKind : byte { Walking = 0, Waiting = 1, FailedPath = 2, Hangaround = 3, Skip = 4 }
        public const int FollowResultCount = 6, StillOutCount = 5, WalkerKindCount = 5;
        /// <summary>Followed exit kinds: 0 going home, 1 no trip purpose, 2 moving away (FollowExitKind)</summary>
        public const int FollowExitKinds = 3;

        // SkiDayClaim.FollowFlags / ClaimRecord.FollowFlags bits
        public const byte FollowFlagFollowed = 1, FollowFlagWalkerSeen = 2, FollowFlagWalkerLost = 4, FollowFlagPlaced = 8,
                          FollowFlagAtOnce = 16, FollowFlagNotFollowedCap = 32;

        /// <summary>The follow kind of an exit way: Home (0) -> 0, NoPurpose (8) -> 1, MovingAway (4) -> 2, anything else -> -1 (not followed)</summary>
        public static int FollowExitKind(byte exitWay)
        {
            switch (exitWay)
            {
                case (byte)ExitWay.Home: return 0;
                case (byte)ExitWay.NoPurpose: return 1;
                case (byte)ExitWay.MovingAway: return 2;
                default: return -1;
            }
        }

        /// <summary>The game placed the guest: at the first follow sample any building is a placement (one pass is about 1 s of
        /// movement, HumanNavigationHelpers.cs:95-96, so no walk or drive reaches a building between the exit and our first sample),
        /// later a penalty counter at or above PlacedMarkMin (the same predicate as NoPurposeAtOf). A walk- or taxi-only route to an
        /// outside connection is placed without the mark (TripNeededSystem.cs:1325-1339): after the first sample it reads "not
        /// placed".</summary>
        public static bool PlacedNow(bool firstSample, byte penaltyNow)
        {
            return firstSample || penaltyNow >= PlacedMarkMin;
        }

        /// <summary>First match: gone -> Deleted; in a building: at home (the origin) -> Home, at an outside connection -> MapExit, at
        /// the lodge -> BackInLodge, else OtherBuilding (placed = PlacedNow for Home, MapExit and OtherBuilding, false otherwise); in
        /// no building and now - exitFrame >= FollowWatchFrames (uint, wraps like the frame counter) -> StillOut; else false (keep
        /// following).</summary>
        public static bool TryFollow(bool gone, bool inBuilding, bool atHome, bool atEdge, bool atLodge, bool firstSample,
                                     byte penaltyNow, uint now, uint exitFrame, out FollowResult r, out bool placed)
        {
            placed = false;
            if (gone)
            {
                r = FollowResult.Deleted;
                return true;
            }
            if (inBuilding)
            {
                if (atHome)
                    r = FollowResult.Home;
                else if (atEdge)
                    r = FollowResult.MapExit;
                else if (atLodge)
                {
                    r = FollowResult.BackInLodge;
                    return true;
                }
                else
                    r = FollowResult.OtherBuilding;
                placed = PlacedNow(firstSample, penaltyNow);
                return true;
            }
            if (now - exitFrame >= FollowWatchFrames)
            {
                r = FollowResult.StillOut;
                return true;
            }
            r = FollowResult.StillOut;
            return false;
        }

        /// <summary>No live walker -> NoWalker; riding -> Riding; waiting at a stop -> Waiting; a failed path -> FailedPath; else OnFoot</summary>
        public static StillOut StillOutOf(bool walkerLive, bool riding, bool waiting, bool failed)
        {
            if (!walkerLive)
                return StillOut.NoWalker;
            if (riding)
                return StillOut.Riding;
            if (waiting)
                return StillOut.Waiting;
            return failed ? StillOut.FailedPath : StillOut.OnFoot;
        }

        /// <summary>Game hours a queued walker has waited, from Resident.m_Timer: +1 or +2 per 16-frame tick (ResidentAISystem.cs:1519,
        /// reset to 0 when WaitingTransport is set :1525), so 1.5 x 10,922.67 / 16 = 1,024 a game hour on average (a mean: the step
        /// is random). timer &lt;= 0 -> 0.</summary>
        public static double WaitHoursOfTimer(int timer)
        {
            return timer > 0 ? timer / 1024.0 : 0.0;
        }

        /// <summary>First match: aboard, or InVehicle / DummyTraffic -> Skip; WaitingTransport -> Waiting; Divert{PathFailed} or a
        /// Failed / Stuck path -> FailedPath; Hangaround -> Hangaround; else Walking. HumanFlags.Waiting is a mood flag, not a
        /// transport mark (ResidentAISystem.cs:1428-1440): not read.</summary>
        public static WalkerKind WalkerKindOf(bool aboard, uint residentFlags, bool divertPathFailed, bool pathBad)
        {
            if (aboard || (residentFlags & (ResidentInVehicle | ResidentDummyTraffic)) != 0u)
                return WalkerKind.Skip;
            if ((residentFlags & ResidentWaitingTransport) != 0u)
                return WalkerKind.Waiting;
            if (divertPathFailed || pathBad)
                return WalkerKind.FailedPath;
            return (residentFlags & ResidentHangaround) != 0u ? WalkerKind.Hangaround : WalkerKind.Walking;
        }

        /// <summary>|p - center|^2 &lt;= radius^2 in xz (float2.y = world z); false when either point is NaN (the compare is false)</summary>
        public static bool WithinXZ(float2 p, float2 centre, float radius)
        {
            return math.lengthsq(p - centre) <= radius * radius;
        }

        /// <summary>Measure slot of a follow: ((kind x 6 + result) x 2 + placed) for kind 0..2 and result 0..5, else -1 (the caller skips -1)</summary>
        public static int FollowSlot(int kind, int result, bool placed)
        {
            if (kind < 0 || kind >= FollowExitKinds || result < 0 || result >= FollowResultCount)
                return -1;
            return (kind * FollowResultCount + result) * 2 + (placed ? 1 : 0);
        }

        /// <summary>Measure slot of a placement at the first sample: kind x 3 + result for kind 0..2 and result 0..2 (Home, MapExit,
        /// OtherBuilding), else -1</summary>
        public static int AtOnceSlot(int kind, int result)
        {
            if (kind < 0 || kind >= FollowExitKinds || result < 0 || result > (int)FollowResult.OtherBuilding)
                return -1;
            return kind * 3 + result;
        }

        /// <summary>What a 64-frame sample saw of the car-reserve handshake</summary>
        public enum CarReserve : byte { HeldAtHome = 0, HeldAfterLeaving = 1, NeverHeldLeft = 2, HeldNeverLeft = 3, NeverHeldNeverLeft = 4 }
        public const int CarReserveCount = 5;

        /// <summary>!departed -> held ? HeldNeverLeft : NeverHeldNeverLeft; departed -> heldAtHome ? HeldAtHome : heldEver ?
        /// HeldAfterLeaving : NeverHeldLeft. heldAtHome implies held (it is set with heldEver).</summary>
        public static CarReserve CarReserveOf(bool departed, bool heldEver, bool heldAtHome)
        {
            if (!departed)
                return heldEver || heldAtHome ? CarReserve.HeldNeverLeft : CarReserve.NeverHeldNeverLeft;
            if (heldAtHome)
                return CarReserve.HeldAtHome;
            return heldEver ? CarReserve.HeldAfterLeaving : CarReserve.NeverHeldLeft;
        }


        /// <summary>What an ended claim tells the all-outcomes speed (SkiDayRules.LearnSpeedAll / LearnSpeedBound). Literal values.</summary>
        public enum SpeedEvidence : byte { None = 0, Exact = 1, Bound = 2 }

        /// <summary>The one "upper bound" predicate: the guest had left (departed), stood on the road (Place.Road) and was not seen
        /// turning back, so it traveled the whole time and the elapsed hours bound its speed from above. A guest who never left,
        /// came back home or sits in a building gives no information.</summary>
        private static bool TravelledThroughout(bool departed, Place place, bool turnedBack)
        {
            return departed && place == Place.Road && !turnedBack;
        }

        /// <summary>A Gone (0) or Retargeted (1) failure gives an upper bound only under TravelledThroughout; anything else gives
        /// none. An expiry (2) gives none here: it is booked when its late watch ends (LateEvidence).</summary>
        public static SpeedEvidence FailureEvidence(int o, bool departed, Place place, bool turnedBack)
        {
            return (o == 0 || o == 1) && TravelledThroughout(departed, place, turnedBack) ? SpeedEvidence.Bound : SpeedEvidence.None;
        }

        /// <summary>The end of a late watch: ArrivedLate -> Exact (arrival - claim); GaveUp, Deleted, NotWithin6h -> Bound only under
        /// the same predicate as FailureEvidence, else None; any other value -> None. The caller passes
        /// <paramref name="place"/> = PlaceOf at the late-end pass, not the claim's Place kept from the expiry (up to LateWatchFrames
        /// older): a Deleted guest then reads Place.Deleted, and a GaveUp guest is in a building by construction
        /// (CitizenBehaviorSystem removes or replaces Leisure only for a citizen in a building with a Transform,
        /// CitizenBehaviorSystem.cs:651-659, 1.6.2 :675-684), so both give None; only a NotWithin6h guest still on the road gives a bound.</summary>
        public static SpeedEvidence LateEvidence(LateResult r, bool departed, Place place, bool turnedBack)
        {
            switch (r)
            {
                case LateResult.ArrivedLate:
                    return SpeedEvidence.Exact;
                case LateResult.GaveUp:
                case LateResult.Deleted:
                case LateResult.NotWithin6h:
                    return TravelledThroughout(departed, place, turnedBack) ? SpeedEvidence.Bound : SpeedEvidence.None;
                default:
                    return SpeedEvidence.None;
            }
        }


        /// <summary>No pull for less than one ski-day pass (jitter guard)</summary>
        public const uint PullMinFrames = 64;

        /// <summary>Why a deadline was pulled in (ResolveOut.PullReason). The literals equal SkiDayRules.ClosingEdgeOf's edges 0 / 1 / 2.</summary>
        public const byte PullNone = 0, PullNoRun = 1, PullNightClosed = 2;

        /// <summary>The new deadline of an on-site ski day whose ski area's hill closed early. pulled = deadline, reason PullNone,
        /// false when the score is unknown or held. No counted run -> close = now, PullNoRun (every on-site claim, its own deadline
        /// included). Else the night-run close, only for a claim flagged DeadlineIsClosing whose deadline is today's 21:00 frame (the
        /// flap guard: one snapshot with the night run at 0 cannot pull a 16:30 or own deadline), from ClaimStartHour: close =
        /// today's closing by the current night state, or now when that has passed, PullNightClosed. target = max(close, now +
        /// OnSiteGraceFrames) (64-bit); false when target + PullMinFrames &gt; deadline (lowered only); else pulled = target.
        /// TryClosingFrame is exact while the clock offset is constant (normalizedTime = (ticks mod 262,144) / 262,144,
        /// TimeSystem.cs:109-112); a developer time jump moves every closing frame, and a pull it fires is accepted.</summary>
        public static bool PullDeadline(bool scoreKnown, bool writeHeld, int runsCounted, bool nightRunOpen, bool deadlineIsClosing,
                                        uint now, float timeOfDay, uint deadline, out uint pulled, out byte reason)
        {
            pulled = deadline;
            reason = PullNone;
            if (!scoreKnown || writeHeld)
                return false;
            uint close;
            byte why;
            if (runsCounted <= 0)
            {
                close = now;
                why = PullNoRun;
            }
            else
            {
                if (!deadlineIsClosing || !SkiDayRules.TryClosingFrame(now, timeOfDay, true, out uint night21) || deadline != night21)
                    return false;
                if (!(SkiDayRules.HourOf(timeOfDay) >= SkiDayRules.ClaimStartHour))
                    return false;
                if (!SkiDayRules.TryClosingFrame(now, timeOfDay, nightRunOpen, out close))
                    close = now;
                why = PullNightClosed;
            }
            ulong floor = (ulong)now + OnSiteGraceFrames;
            ulong target = close > floor ? close : floor;
            if (target + PullMinFrames > deadline)
                return false;
            pulled = (uint)target;
            reason = why;
            return true;
        }

        /// <summary>Did the guest counter's leisure scan see this visit?</summary>
        public enum ScanVerdict : byte { Seen = 0, Missed = 1, Unsure = 2 }
        public const int ScanVerdictCount = 3;

        /// <summary>Scans run at scanFrame + k x scanInterval. Seen iff a scan lies strictly between <paramref name="firstOnSite"/>
        /// and <paramref name="lastOnSite"/> (both passes saw the guest there); Missed iff no scan lies strictly between firstOnSite -
        /// passInterval (the guest arrived after the pass before) and <paramref name="endPass"/>; else Unsure. Not known, or an
        /// interval of 0 -> Unsure. Frame differences wrap like the frame counter (spans under 2^31 frames).</summary>
        public static ScanVerdict GuestScanSaw(uint firstOnSite, uint lastOnSite, uint endPass, uint passInterval, bool scanKnown,
                                               uint scanFrame, uint scanInterval)
        {
            if (!scanKnown || scanInterval == 0u)
                return ScanVerdict.Unsure;
            if (ScanBetween(firstOnSite, lastOnSite, scanFrame, scanInterval))
                return ScanVerdict.Seen;
            if (!ScanBetween(firstOnSite - passInterval, endPass, scanFrame, scanInterval))
                return ScanVerdict.Missed;
            return ScanVerdict.Unsure;
        }

        /// <summary>Is there a scan s with a &lt; s &lt; b? The next scan at or after a + 1 is (a + 1) + (scanFrame - (a + 1)) mod
        /// interval; it is inside when that offset &lt; b - (a + 1). Spans of 1 or less (or "negative") hold none.</summary>
        private static bool ScanBetween(uint a, uint b, uint scanFrame, uint scanInterval)
        {
            uint span = b - a;
            if ((int)span <= 1)
                return false;
            uint start = a + 1u;
            uint offset = (scanFrame - start) % scanInterval;
            return offset < span - 1u;
        }

        /// <summary>Main thread only (managed arrays; the one exception in this file). <paramref name="snowHour"/> holds one entry per
        /// hour of the year (24 x days, wrapping). For each hour h, the window is hours h - 23 ... h (wrapping):
        /// PowderMark(PowderOf(FreshSteps(the fresh snow in it, <paramref name="freshHour"/>))) ->
        /// <paramref name="markHours"/>[MonthOfHour(h)]++, and a snow hour h -> <paramref name="snowHours"/>[MonthOfHour(h)]++. Both
        /// outputs are cleared first. 24 adds per hour: O(hours).</summary>
        public static void MarkHoursPerMonth(bool[] snowHour, float[] freshHour, int daysPerYear, int[] markHours, int[] snowHours)
        {
            if (markHours != null)
                Array.Clear(markHours, 0, markHours.Length);
            if (snowHours != null)
                Array.Clear(snowHours, 0, snowHours.Length);
            if (snowHour == null || freshHour == null || markHours == null || snowHours == null)
                return;
            int days = math.max(1, daysPerYear);
            int n = math.min(math.min(snowHour.Length, freshHour.Length), 24 * days);
            if (n <= 0)
                return;
            for (int h = 0; h < n; h++)
            {
                // The window's fresh snow, summed afresh each hour (24 adds; no drift from a running float sum)
                double fresh = 0.0;
                for (int k = 0; k < 24; k++)
                    fresh += freshHour[((h - k) % n + n) % n];
                int m = SkiDayRules.MonthOfHour(h, days);
                if (SkiDayRules.PowderMark(SkiDayRules.PowderOf(SkiDayRules.FreshSteps(fresh))) && (uint)m < (uint)markHours.Length)
                    markHours[m]++;
                if (snowHour[h] && (uint)m < (uint)snowHours.Length)
                    snowHours[m]++;
            }
        }
    }
}
