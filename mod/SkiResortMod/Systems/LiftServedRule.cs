// The lift-served rule and the power predicate. Consumers: SkiResortAttractivenessSystem, SkiResortStatsSystem, SkiLiftChargeSystem
// and SkiLiftTicketSystem, via LiftCensusSystem.CopyServeLifts / ReadLiftPower; tests/LiftBatch and tests/ResortBatch link it.
// A run is served by lift L when L's row is Complete, IsPowered(L.Power), the run's top T has T.y <= TopY + TopMarginM and its xz
// distance to the nearest chord of L is <= ServeRadiusM (both bounds inclusive; the nearest lift serves, a tie goes to the lower
// EntityRef). A run a skier gets onto DOWNHILL from a served run is served too (AssignLinked: linked outlines, a reachable junction,
// a real part of the run below it; cat tracks carry service level or downhill). TerminalsOnly lifts (tram) measure to TopXZ instead.
// IsPowered is the only "powered" predicate in the mod; PowerGatesService = false turns power into status only. A lift is powered
// from its base station only. Pure file: System, System.Collections.Generic, Unity.Mathematics, EntityRef and SkiCatalog consts.
// Engine notes: EngineNotes/LiftServedRule.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>A lift's power from its base station's ElectricityConsumer (the 4-argument PowerOf).</summary>
    public enum LiftPower : byte
    {
        Unknown = 0,   // no base station in the build, not complete, or no ElectricityConsumer: treated as powered
        Powered = 1,   // no electricity warning, or at least half of the wanted power arrives (3/4 when coming back)
        NoPower = 2,   // the game's NoElectricity or Bottleneck warning and under half of the wanted power
        Pending = 3,   // no electricity dispatch has judged the consumer yet and the lift has no powered read this load: held, not
                       // powered (IsPowered false), shown as no outage (Power != NoPower); the caller fails it open after PendingMaxFrames
    }

    public static class LiftServedRule
    {
        // The lift type's numbers, one source in SkiCatalog.FixedGrip; tests/ResortBatch CatalogGuardTests CG-1 pins alias == row == const
        public const float ServeRadiusM = SkiCatalog.FixedGrip.ServeRadiusM;
        public const float TopMarginM = SkiCatalog.FixedGrip.TopMarginM;

        /// <summary>One-value reversal to "power is status only": false = a NoPower lift still serves. static readonly, not
        /// const, so the compiler folds nothing and warns about nothing.</summary>
        public static readonly bool PowerGatesService = true;

        /// <summary>= Game.Buildings.ElectricityConsumerFlags (ElectricityConsumerFlags.cs:5-12).</summary>
        public const byte FlagConnected = 1, FlagNoElectricityWarning = 2, FlagBottleneckWarning = 4;

        public struct Chord
        {
            public float2 A, B;   // xz
        }

        public struct Lift
        {
            public EntityRef Id;          // census Row.Id (the live base, else the live top, else an edge)
            public EntityRef Base, Top;   // census Row.Base / Row.Top
            public bool Complete;         // census State == Complete
            public LiftPower Power;       // Unknown unless Complete
            public float TopY;            // top station stub-tip y; NaN when the top station is not in the last build
            public int FirstChord;        // slice of the chord list
            public int ChordCount;
            // The row's lift type's rule
            public byte Rule;             // (byte)ServeRuleKind: 0 AlongLine (default), 1 TerminalsOnly
            public float RadiusM;         // ≤ 0 (struct default) = SkiCatalog.FixedGrip.ServeRadiusM
            public float TopMarginM;      // ≤ 0 (struct default) = SkiCatalog.FixedGrip.TopMarginM
            public float2 TopXZ;          // the top station's HV stub tip xz; NaN when the top is not in the last build

            public bool Serves =>
                Complete && ChordCount > 0 && !float.IsNaN(TopY) && IsPowered(Power);

            /// <summary>The radius the rule uses: RadiusM when &gt; 0, else the fixed-grip ServeRadiusM.</summary>
            public float EffectiveRadiusM => RadiusM > 0f ? RadiusM : ServeRadiusM;

            /// <summary>The top margin the rule uses: TopMarginM when &gt; 0, else the fixed-grip TopMarginM.</summary>
            public float EffectiveTopMarginM => TopMarginM > 0f ? TopMarginM : LiftServedRule.TopMarginM;
        }

        /// <summary>The game's electricity warning on the consumer ⇒ NoPower; else Powered.</summary>
        public static LiftPower PowerOf(byte consumerFlags)
        {
            return (consumerFlags & (FlagNoElectricityWarning | FlagBottleneckWarning)) != 0
                ? LiftPower.NoPower
                : LiftPower.Powered;
        }

        /// <summary>2 × fulfilled &lt; wanted → under half (NoPower); a NoPower lift is back at 4 × fulfilled ≥ 3 × wanted (hysteresis).</summary>
        public const int HalfPowerDivisor = 2, BackPowerNumerator = 3, BackPowerDenominator = 4;

        /// <summary>NoPower only when the game shows its electricity warning and under half of the wanted power arrives
        /// (2 × fulfilled &lt; wanted); a NoPower lift comes back when the warning clears or at least three quarters arrive
        /// (4 × fulfilled ≥ 3 × wanted). Exact in long; wanted ≤ 0 → Powered. Before electricity unlocks the game sets no warning
        /// (DispatchElectricitySystem.cs:61, :106-109) and fulfilled may be 0, so the warning test comes first. Fields
        /// ElectricityConsumer.m_WantedConsumption / m_FulfilledConsumption (ElectricityConsumer.cs:8-10); fulfilled = min(flow
        /// share × wanted, wanted) (DispatchElectricitySystem.cs:105). The caller passes the station's last value (Unknown on the
        /// first read). The 1-argument overload stays for tests/LiftBatch.</summary>
        public static LiftPower PowerOf(byte consumerFlags, int wantedConsumption, int fulfilledConsumption, LiftPower last)
        {
            if ((consumerFlags & (FlagNoElectricityWarning | FlagBottleneckWarning)) == 0)
                return LiftPower.Powered;
            if (wantedConsumption <= 0)
                return LiftPower.Powered;
            if (last == LiftPower.NoPower)
                return (long)fulfilledConsumption * BackPowerDenominator >= (long)wantedConsumption * BackPowerNumerator
                    ? LiftPower.Powered : LiftPower.NoPower;
            return (long)fulfilledConsumption * HalfPowerDivisor >= wantedConsumption ? LiftPower.Powered : LiftPower.NoPower;
        }

        /// <summary>The power read for a station with its consumer's cooldown counter (ElectricityConsumer.m_CooldownCounter). Fixes
        /// the fail-open start (DECISIONS 601 i): the game raises its no-power warning only on the SECOND short dispatch
        /// (DispatchElectricitySystem.kAlertCooldown = 2, :208; HandleCooldown :128-143), every 128 sim frames (offset 126, :222-230),
        /// and AdjustElectricityConsumptionSystem sets the wanted power at offset 0 (:385-392). So a fresh unpowered station showed no
        /// warning for up to ~380 frames and the warning-first rule read it Powered: its chairs ran, then stopped.
        /// Order: (1) a warning -> the 4-argument rule (half / three quarters); (2) Connected and fulfilled &gt;= wanted &gt; 0 ->
        /// Powered (a dispatch with wanted 0 sets Connected too, :115-122, and the wanted power arrives at the next offset 0:
        /// Connected alone is stale, LiftBatch PG-5 caught it); (3) a lift read Powered or NoPower before this load -> Powered (the
        /// game's cooldown absorbs one short dispatch, as before); (4) no powered history (last Unknown / Pending): dispatched short
        /// (cooldown &gt; 0, wanted &gt; 0) -> the half rule without waiting for the warning; else Pending (not dispatched yet, wanted
        /// still 0, or electricity locked: HandleCooldown never runs, :102-105, and the caller's ExpirePending fails it open).</summary>
        public static LiftPower PowerOf(byte consumerFlags, int wantedConsumption, int fulfilledConsumption, short cooldownCounter,
                                        LiftPower last)
        {
            if ((consumerFlags & (FlagNoElectricityWarning | FlagBottleneckWarning)) != 0)
                return PowerOf(consumerFlags, wantedConsumption, fulfilledConsumption, last);
            if ((consumerFlags & FlagConnected) != 0 && wantedConsumption > 0 && fulfilledConsumption >= wantedConsumption)
                return LiftPower.Powered;
            if (last == LiftPower.Powered || last == LiftPower.NoPower)
                return LiftPower.Powered;
            if (cooldownCounter > 0 && wantedConsumption > 0)
                return (long)fulfilledConsumption * HalfPowerDivisor >= wantedConsumption ? LiftPower.Powered : LiftPower.NoPower;
            return LiftPower.Pending;
        }

        /// <summary>Sim frames a station may read Pending before it fails open to Powered: 3 dispatch periods (384 frames, 6.4 s at
        /// 1x). A station built just after a dispatch gets its wanted power at the next offset 0 and its first judged dispatch at
        /// offset 126 (at most 254 frames later), so a powered lift starts within one or two dispatches (about 2-4 s at 1x) and
        /// only a city whose electricity is locked (no dispatch ever judges the consumer) waits the full window.</summary>
        public const uint PendingMaxFrames = 3u * 128u;

        /// <summary>The pending window of one read. raw Pending: the window starts on its first read (pendingSince = now) and after
        /// PendingMaxFrames the read is Powered (timedOut = true, pendingSince cleared: fail open). Any other raw clears the window
        /// and passes through. Unchecked uint subtraction (the frame counter may wrap). The caller keeps pendingSince per station
        /// (NoBackSince = none).</summary>
        public static LiftPower ExpirePending(LiftPower raw, ref uint pendingSince, uint nowFrame, out bool timedOut)
        {
            timedOut = false;
            if (raw != LiftPower.Pending)
            {
                pendingSince = NoBackSince;
                return raw;
            }
            if (pendingSince == NoBackSince)
                pendingSince = nowFrame;
            if (unchecked(nowFrame - pendingSince) >= PendingMaxFrames)
            {
                pendingSince = NoBackSince;
                timedOut = true;
                return LiftPower.Powered;
            }
            return LiftPower.Pending;
        }

        /// <summary>The "powered" predicate for service, lift value, lift billing and the lift charge's operating count. Unknown
        /// counts as powered; Pending (the first dispatch has not judged the station yet) does not, so its chairs wait instead of
        /// running unpowered for a few seconds. With PowerGatesService false, power is status only everywhere (one switch).</summary>
        public static bool IsPowered(LiftPower power)
        {
            return !PowerGatesService || (power != LiftPower.NoPower && power != LiftPower.Pending);
        }

        /// <summary>Sim frames of raw Powered reads before a NoPower lift counts as powered again: 4 dispatch periods
        /// (DispatchElectricitySystem.cs:222-230: every 128 frames, offset 126), 8.5 s at 1× (60 sim frames a second,
        /// SimulationSystem.cs:238), about 2.8 game minutes (262,144 frames a day, TimeSystem.cs:18). Absorbs the brownout pattern
        /// (the warning on for 128 of every 384 frames, DispatchElectricitySystem.cs:126-142).</summary>
        public const uint PowerBackHoldFrames = 512u;

        /// <summary>No Powered streak in progress (GuardPower's backSince). A streak that starts on frame 2³² − 1 itself (about
        /// 2.3 years of play at 60 frames a second) restarts at the next read: one read of delay.</summary>
        public const uint NoBackSince = uint.MaxValue;

        /// <summary>A streak whose station was not read for more than this many sim frames is stale (2 × PowerBackHoldFrames =
        /// 1,024). Every Complete row is read at least every 256 frames (the attraction's and the charge's power polls), so a gap
        /// this long means the row was not Complete, or not read, meanwhile.</summary>
        public const uint PowerStreakStaleFrames = 2u * PowerBackHoldFrames;

        /// <summary>The effective power after one read. raw = PowerOf(4 arguments) of this read, computed with the station's last
        /// effective value; lastEffective = that value. NoPower → Powered waits until raw has read Powered for PowerBackHoldFrames
        /// (backSince = the first frame of the current Powered streak; elapsed by unchecked uint subtraction, so the frame counter
        /// may wrap). Every other transition is immediate. raw NoPower ends a streak (recut = true when one was running); raw
        /// Unknown passes through and clears the streak. A station whose lastEffective is Powered or Unknown gets raw back unchanged.
        /// Allocation-free, Burst-safe, no state of its own (the caller keeps backSince per station).</summary>
        public static LiftPower GuardPower(LiftPower raw, LiftPower lastEffective, ref uint backSince, uint nowFrame, out bool recut)
        {
            recut = false;
            if (raw == LiftPower.NoPower)
            {
                recut = backSince != NoBackSince;
                backSince = NoBackSince;
                return LiftPower.NoPower;
            }
            if (raw != LiftPower.Powered || lastEffective != LiftPower.NoPower)
            {
                backSince = NoBackSince;
                return raw;
            }
            if (backSince == NoBackSince)
                backSince = nowFrame;
            if (unchecked(nowFrame - backSince) >= PowerBackHoldFrames)
            {
                backSince = NoBackSince;
                return LiftPower.Powered;
            }
            return LiftPower.NoPower;
        }

        /// <summary>backSince, or NoBackSince when the station was last read more than PowerStreakStaleFrames before
        /// <paramref name="nowFrame"/> (unchecked uint subtraction). Call it before GuardPower with the station's previous read
        /// frame; a caller that does not track read frames skips it (fail open: a streak that started before a section redraw may
        /// then count across the gap).</summary>
        public static uint ExpireStreak(uint backSince, uint lastReadFrame, uint nowFrame)
        {
            return backSince != NoBackSince && unchecked(nowFrame - lastReadFrame) > PowerStreakStaleFrames ? NoBackSince : backSince;
        }

        /// <summary>The run predicate: a lift carries guests (counts, moves, has capacity) when it is complete, powered (IsPowered:
        /// Unknown counts, PowerGatesService false ignores power) and not closed. = SkiMoneyRules.LiftOperating(complete,
        /// IsPowered(power)) &amp;&amp; !closed (ResortBatch HK-3), and Lift.Serves' power factor (LiftBatch PG-1).</summary>
        public static bool Carries(bool complete, LiftPower power, bool closed)
        {
            return complete && IsPowered(power) && !closed;
        }

        /// <summary>Carries for a caller that holds the status's bool (the hub: shownPowered = Power != NoPower, the attraction's
        /// LiftScoreView.Powered or the hub's own CopyServeLifts read).</summary>
        public static bool Carries(bool complete, bool shownPowered, bool closed)
        {
            return complete && (!PowerGatesService || shownPowered) && !closed;
        }

        /// <summary>Squared xz distance from p to segment ab (a degenerate segment is the point a).</summary>
        public static float DistanceSqXZ(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len2 = math.dot(ab, ab);
            float t = len2 > 0f ? math.saturate(math.dot(p - a, ab) / len2) : 0f;
            return math.lengthsq(p - (a + t * ab));
        }

        /// <summary>Does <paramref name="lift"/> serve a run whose highest outline node is
        /// <paramref name="runTop"/>? <paramref name="distanceM"/> = xz distance to its nearest chord
        /// (+inf when false).</summary>
        public static bool Reaches(float3 runTop, in Lift lift, List<Chord> chords, out float distanceM)
        {
            distanceM = float.PositiveInfinity;
            // The row's own margin and radius (the fixed-grip consts only for a struct-default Lift)
            float radius = lift.EffectiveRadiusM;
            if (!lift.Serves || !math.all(math.isfinite(runTop)) || runTop.y > lift.TopY + lift.EffectiveTopMarginM)
                return false;
            float2 p = runTop.xz;
            if (lift.Rule == (byte)ServeRuleKind.TerminalsOnly)
            {
                // A tram serves the run tops around its top station only (inclusive radius)
                if (!math.all(math.isfinite(lift.TopXZ)))
                    return false;
                float t2 = math.lengthsq(p - lift.TopXZ);
                if (!(t2 <= radius * radius))
                    return false;
                distanceM = math.sqrt(t2);
                return true;
            }
            float best = float.PositiveInfinity;
            int end = lift.FirstChord + lift.ChordCount;
            for (int c = lift.FirstChord; c < end; c++)
            {
                float d2 = DistanceSqXZ(p, chords[c].A, chords[c].B);
                if (d2 < best)
                    best = d2;
            }
            if (!(best <= radius * radius))
                return false;
            distanceM = math.sqrt(best);
            return true;
        }

        /// <summary>A lodge at <paramref name="lodgeXz"/> is summit-served when any lift of <paramref name="lifts"/> Serves
        /// (complete, powered, chords &gt; 0, TopY finite) and its TopXZ lies within <paramref name="radiusM"/> (inclusive) of the
        /// lodge. The caller passes the lifts of the lodge's ski area (SkiCatalog.Ladder.SummitServedRadiusM = 300). A non-finite
        /// lodge position, an empty or null list, a NaN TopXZ (top not in the build) or a radius that is not &gt;= 0 → false.
        /// Allocation-free.</summary>
        public static bool SummitServed(float2 lodgeXz, List<Lift> lifts, float radiusM)
        {
            if (lifts == null || !math.all(math.isfinite(lodgeXz)) || !(radiusM >= 0f))
                return false;
            float r2 = radiusM * radiusM;
            for (int l = 0; l < lifts.Count; l++)
            {
                Lift lift = lifts[l];
                if (!lift.Serves || !math.all(math.isfinite(lift.TopXZ)))
                    continue;
                if (math.lengthsq(lodgeXz - lift.TopXZ) <= r2)
                    return true;
            }
            return false;
        }

        /// <summary>A summit lodge also counts as reached when it stands within <paramref name="radiusM"/> (inclusive) of an edge of a
        /// served run's outline (the caller checks served; SkiCatalog.Ladder.SummitRunRadiusM = 100). A null or short outline, a non-finite
        /// position or a radius that is not &gt;= 0 → false. Allocation-free.</summary>
        public static bool OutlineWithin(float2 xz, float2[] outlineXZ, float radiusM)
        {
            if (outlineXZ == null || outlineXZ.Length < 2 || !math.all(math.isfinite(xz)) || !(radiusM >= 0f))
                return false;
            float r2 = radiusM * radiusM;
            for (int i = 0; i < outlineXZ.Length; i++)
            {
                float2 a = outlineXZ[i], ab = outlineXZ[(i + 1) % outlineXZ.Length] - a;
                float len2 = math.lengthsq(ab);
                float t = len2 > 0f ? math.saturate(math.dot(xz - a, ab) / len2) : 0f;
                if (math.lengthsq(xz - (a + ab * t)) <= r2)
                    return true;
            }
            return false;
        }

        /// <summary>Index of the serving lift (smallest distance, tie → lower Id), or −1.</summary>
        public static int ServingLift(float3 runTop, List<Lift> lifts, List<Chord> chords, out float distanceM)
        {
            int best = -1;
            distanceM = float.PositiveInfinity;
            for (int l = 0; l < lifts.Count; l++)
            {
                if (!Reaches(runTop, lifts[l], chords, out float d))
                    continue;
                if (best < 0 || d < distanceM || (d == distanceM && lifts[l].Id.CompareTo(lifts[best].Id) < 0))
                {
                    best = l;
                    distanceM = d;
                }
            }
            return best;
        }

        /// <summary>For every run top: its serving lift (or −1) and the distance; for every lift: how
        /// many runs it serves (a run within reach of two lifts counts for both — the "serves N runs"
        /// reverse map). Output lists are cleared and filled; allocation-free once grown.</summary>
        public static void Assign(List<float3> runTops, List<Lift> lifts, List<Chord> chords,
            List<int> servingLift, List<float> servingDistanceM, List<int> servedCount)
        {
            servingLift.Clear();
            servingDistanceM.Clear();
            servedCount.Clear();
            for (int l = 0; l < lifts.Count; l++)
                servedCount.Add(0);
            for (int r = 0; r < runTops.Count; r++)
            {
                int best = -1;
                float bestD = float.PositiveInfinity;
                for (int l = 0; l < lifts.Count; l++)
                {
                    if (!Reaches(runTops[r], lifts[l], chords, out float d))
                        continue;
                    servedCount[l]++;
                    if (best < 0 || d < bestD || (d == bestD && lifts[l].Id.CompareTo(lifts[best].Id) < 0))
                    {
                        best = l;
                        bestD = d;
                    }
                }
                servingLift.Add(best);
                servingDistanceM.Add(bestD);
            }
        }

        /// <summary>Two runs of the same ski area are linked when any outline node of one lies within this many meters (xz,
        /// inclusive) of the other's outline (a node or a point on an edge), or their outlines overlap (an edge crossing, or a node
        /// inside the other's ring). 25 m: a connector is drawn from a node placed on or beside the run it leaves, so the two
        /// outlines meet within a brush width or two; 26 m apart is not linked (ServedLinkTests). One const, no setting. A cat track's own
        /// nodes within this distance of each other are also joined for its climb walk (across the ribbon).</summary>
        public const float LinkMeters = 25f;

        /// <summary>Downhill linking: a run entered from a link is served only when at least min(MinBelowM, its drop /
        /// BelowDropDivisor) of it lies below the entry (BelowJunction).</summary>
        public const float MinBelowM = 30f, BelowDropDivisor = 3f;

        /// <summary>How far above the place where a skier got on a run or a cat track they can still get (the cap, AssignLinked): a
        /// junction or a track node higher than that is out of reach. 3 m covers node-height noise and a graded track's small rises;
        /// a real climb (a cat track up the mountain) stops them. A height cap, not a sum of climbs: a summed budget would restart
        /// at every run a track crosses (the skier steps onto the run and back), while the cap never grows along a chain.</summary>
        public const float ClimbToleranceM = 3f;

        /// <summary>Rule 1 of the downhill linking: is a real part of a run below the entry height <paramref name="entryY"/>?
        /// entryY - minY &gt;= min(MinBelowM, (maxY - minY) / BelowDropDivisor), inclusive; minY / maxY = the run's lowest / highest
        /// outline node. A flat outline (no heights) needs nothing. NaN fails.</summary>
        public static bool BelowJunction(float entryY, float minY, float maxY)
        {
            float need = math.min(MinBelowM, (maxY - minY) / BelowDropDivisor);
            return entryY - minY >= need;
        }

        /// <summary>One place where a skier can pass from one linked run to the other: FromY / ToY = the height of the place on each
        /// run. On each run the place is an outline node (Node2 = -1) or lies on the edge Node - Node2 (a crossing, or a node of the
        /// other run inside this one's ring: its nearest edge), the cat track walk's entry and exit: a track's outline is simplified
        /// (GradeRibbon), so a straight track has nodes only at its ends and the place must reach both ends of its edge.</summary>
        public struct Contact
        {
            public float FromY, ToY;
            public int FromNode, FromNode2, ToNode, ToNode2;
        }

        /// <summary>The run-to-run link graph of one recompute. Build(outlines, heights, areas) fills a CSR adjacency and, per
        /// direction of each link, its contacts (Contact). It is SELF-VALIDATING: when the per-run outline and height arrays are the
        /// very same objects as at the last Build (the catalog keeps one array per run until its outline moves or its nodes are
        /// re-snapped) and the area ids are equal, the graph is kept and no pair is tested, so the O(n^2 x nodes) pass runs only
        /// after an outline, a height, a binding or the run set changed, not on every soft recompute. Allocation-free once grown.
        /// Main thread, one instance per caller.</summary>
        public sealed class RunLinks
        {
            private readonly List<float2[]> m_Outlines = new List<float2[]>(32);
            private readonly List<float[]> m_HeightsIn = new List<float[]>(32);   // the caller's arrays (the reuse test)
            private readonly List<float[]> m_Heights = new List<float[]>(32);     // the ones read: null when absent or mismatched
            private readonly List<int> m_Areas = new List<int>(32);
            private readonly List<float4> m_Box = new List<float4>(32);   // minX, minZ, maxX, maxZ of each outline
            private readonly List<float2> m_YRange = new List<float2>(32);   // lowest, highest node y (0, 0 without heights)
            private readonly List<int> m_TopNode = new List<int>(32);        // the highest node (the first on a tie; 0 without heights)
            private readonly List<int> m_NodeStart = new List<int>(33);      // run -> first slot of its nodes in the per-node lists
            private readonly List<int> m_Start = new List<int>(33);
            private readonly List<int> m_Adj = new List<int>(64);
            private readonly List<int> m_SlotFirst = new List<int>(64);      // per adjacency slot: its contacts in m_Contacts
            private readonly List<int> m_SlotCount = new List<int>(64);
            private readonly List<Contact> m_Contacts = new List<Contact>(256);
            private readonly List<int> m_PairA = new List<int>(32);
            private readonly List<int> m_PairB = new List<int>(32);
            private readonly List<int> m_PairAB = new List<int>(32);         // per pair: first contact A -> B, count, first B -> A, count
            private readonly List<int> m_PairABn = new List<int>(32);
            private readonly List<int> m_PairBA = new List<int>(32);
            private readonly List<int> m_PairBAn = new List<int>(32);
            private readonly List<int> m_Fill = new List<int>(32);
            private readonly List<float> m_BestY = new List<float>(64);      // AddContacts scratch, per node of b: its best node of a
            private readonly List<float> m_BestBoard = new List<float>(64);
            private readonly List<int> m_BestI = new List<int>(64);
            private readonly List<Contact> m_Back = new List<Contact>(64);  // AddContacts: the B -> A contacts until A -> B is done
            // A cat track's walk graph (built on first use per Build): per node its neighbors' slice of m_WalkAdj
            private readonly List<int> m_WalkFirst = new List<int>(256);
            private readonly List<int> m_WalkCount = new List<int>(256);
            private readonly List<int> m_WalkAdj = new List<int>(1024);
            private readonly List<bool> m_WalkBuilt = new List<bool>(32);
            // AssignLinked's scratch
            internal readonly List<int> Queue = new List<int>(32);
            internal readonly List<bool> Queued = new List<bool>(32);
            internal readonly List<float> Cap = new List<float>(32);         // per run: the highest a skier on it gets (+inf = all of it)
            internal readonly List<bool> Dirty = new List<bool>(32);         // per run: a cat track with new entries
            internal readonly List<float> EntryCap = new List<float>(256);   // per node: the best cap a skier gets on with (cat tracks)
            internal readonly List<float> NodeCap = new List<float>(256);    // per node: the cap it is reached with, -inf unreached
            internal readonly List<int> Flood = new List<int>(64);
            private bool m_Valid;

            /// <summary>How many Builds recomputed the graph / kept it (diagnostics, tests).</summary>
            public int Rebuilds, Reuses;

            /// <summary>The run count of the last Build.</summary>
            public int Count => m_Areas.Count;

            /// <summary>Number of links the last Build found.</summary>
            public int LinkCount => m_PairA.Count;

            /// <summary>The runs linked to run <paramref name="run"/>: a slice of the adjacency (first slot and count).</summary>
            public void NeighboursOf(int run, out int first, out int count)
            {
                first = m_Start[run];
                count = m_Start[run + 1] - first;
            }

            /// <summary>The run at adjacency slot <paramref name="slot"/> (from NeighboursOf).</summary>
            public int Neighbour(int slot)
            {
                return m_Adj[slot];
            }

            /// <summary>The contacts of adjacency slot <paramref name="slot"/> (the run -> the run at that slot): first index and count.</summary>
            public void ContactsOf(int slot, out int first, out int count)
            {
                first = m_SlotFirst[slot];
                count = m_SlotCount[slot];
            }

            /// <summary>Contact <paramref name="index"/> (from ContactsOf).</summary>
            public Contact ContactAt(int index)
            {
                return m_Contacts[index];
            }

            /// <summary>Run <paramref name="run"/>'s lowest and highest outline node y (0, 0 without heights).</summary>
            public float2 YRange(int run)
            {
                return m_YRange[run];
            }

            internal int TopNode(int run) => m_TopNode[run];

            internal int NodeStart(int run) => m_NodeStart[run];

            internal int NodeCount(int run) => m_NodeStart[run + 1] - m_NodeStart[run];

            internal float NodeY(int run, int node)
            {
                float[] y = m_Heights[run];
                return y != null ? y[node] : 0f;
            }

            /// <summary>Builds (or keeps) the graph without heights: every node at y 0, i.e. flat terrain, where the downhill rule
            /// reduces to the plain link rule (every contact usable, every linked run served).</summary>
            public bool Build(List<float2[]> outlines, List<int> areas)
            {
                return Build(outlines, null, areas);
            }

            /// <summary>Builds (or keeps) the graph. outlines[i] = run i's outline nodes (xz; null or empty = no outline, never
            /// linked); heights[i] = their y in the same order (null list, null entry or another length = flat, y 0); areas[i] = run
            /// i's ski area id (any int; a negative id = no area, never linked). One entry per run. Returns true when the graph was
            /// recomputed.</summary>
            public bool Build(List<float2[]> outlines, List<float[]> heights, List<int> areas)
            {
                int n = outlines.Count;
                if (areas.Count != n || (heights != null && heights.Count != n))
                    throw new ArgumentException("areas, heights and outlines differ in length");
                if (m_Valid && SameInputs(outlines, heights, areas))
                {
                    Reuses++;
                    return false;
                }
                Rebuilds++;
                m_Outlines.Clear();
                m_HeightsIn.Clear();
                m_Heights.Clear();
                m_Areas.Clear();
                m_Box.Clear();
                m_YRange.Clear();
                m_TopNode.Clear();
                m_NodeStart.Clear();
                m_PairA.Clear();
                m_PairB.Clear();
                m_PairAB.Clear();
                m_PairABn.Clear();
                m_PairBA.Clear();
                m_PairBAn.Clear();
                m_Contacts.Clear();
                m_WalkBuilt.Clear();
                m_WalkAdj.Clear();
                int nodes = 0;
                for (int i = 0; i < n; i++)
                {
                    float2[] o = outlines[i];
                    float[] hy = heights != null ? heights[i] : null;
                    m_Outlines.Add(o);
                    m_HeightsIn.Add(hy);
                    if (o == null || hy == null || hy.Length != o.Length)
                        hy = null;
                    m_Heights.Add(hy);
                    m_Areas.Add(areas[i]);
                    m_Box.Add(BoxOf(o));
                    float lo = 0f, hi = 0f;
                    int top = 0;
                    if (hy != null && hy.Length > 0)
                    {
                        lo = hi = hy[0];
                        for (int k = 1; k < hy.Length; k++)
                        {
                            if (hy[k] < lo) lo = hy[k];
                            if (hy[k] > hi)
                            {
                                hi = hy[k];
                                top = k;
                            }
                        }
                    }
                    m_YRange.Add(new float2(lo, hi));
                    m_TopNode.Add(top);
                    m_NodeStart.Add(nodes);
                    nodes += o != null ? o.Length : 0;
                    m_WalkBuilt.Add(false);
                }
                m_NodeStart.Add(nodes);
                float margin = LinkMeters;
                for (int i = 0; i < n; i++)
                {
                    if (m_Areas[i] < 0 || m_Outlines[i] == null || m_Outlines[i].Length == 0)
                        continue;
                    float4 bi = m_Box[i];
                    for (int j = i + 1; j < n; j++)
                    {
                        if (m_Areas[j] != m_Areas[i] || m_Outlines[j] == null || m_Outlines[j].Length == 0)
                            continue;
                        float4 bj = m_Box[j];
                        if (bi.z + margin < bj.x || bj.z + margin < bi.x || bi.w + margin < bj.y || bj.w + margin < bi.y)
                            continue;   // boxes (grown by LinkMeters) apart: no node pair is within reach and no edges cross
                        if (OutlinesLinked(m_Outlines[i], m_Outlines[j]))
                        {
                            m_PairA.Add(i);
                            m_PairB.Add(j);
                            AddContacts(i, j);
                        }
                    }
                }
                // CSR: counts, prefix sums, fill. The order of a run's neighbors is a function of the inputs alone.
                m_Start.Clear();
                m_Fill.Clear();
                for (int i = 0; i <= n; i++)
                    m_Start.Add(0);
                for (int k = 0; k < m_PairA.Count; k++)
                {
                    m_Start[m_PairA[k] + 1]++;
                    m_Start[m_PairB[k] + 1]++;
                }
                for (int i = 0; i < n; i++)
                {
                    m_Start[i + 1] += m_Start[i];
                    m_Fill.Add(m_Start[i]);
                }
                m_Adj.Clear();
                m_SlotFirst.Clear();
                m_SlotCount.Clear();
                for (int k = 0; k < m_Start[n]; k++)
                {
                    m_Adj.Add(0);
                    m_SlotFirst.Add(0);
                    m_SlotCount.Add(0);
                }
                for (int k = 0; k < m_PairA.Count; k++)
                {
                    int a = m_PairA[k], b = m_PairB[k];
                    int sa = m_Fill[a]++, sb = m_Fill[b]++;
                    m_Adj[sa] = b;
                    m_SlotFirst[sa] = m_PairAB[k];
                    m_SlotCount[sa] = m_PairABn[k];
                    m_Adj[sb] = a;
                    m_SlotFirst[sb] = m_PairBA[k];
                    m_SlotCount[sb] = m_PairBAn[k];
                }
                m_Valid = true;
                return true;
            }

            /// <summary>The contacts of the linked pair (i, j), appended A -> B, then B -> A (one pass over the tests fills both). Per
            /// node of a source: the near node of the other run (within LinkMeters) a skier boards highest, i.e. the most of min(its y,
            /// the source node's y), a tie to the lower node (on a cat track the uphill neighbor would sit above the skier's cap); per
            /// edge crossing: the crossing point's height on each edge (linear between its nodes); per node inside the other's ring:
            /// that node's own height on both sides (the same ground); per node with no near node, outside the ring and within
            /// LinkMeters of an edge: its own height and the height of the nearest point on that edge. Only nodes inside the other
            /// outline's box grown by LinkMeters are tested (no other node is near it or inside it).</summary>
            private void AddContacts(int i, int j)
            {
                float2[] a = m_Outlines[i], b = m_Outlines[j];
                float4 ba = Grow(m_Box[i]), bb = Grow(m_Box[j]);
                float r2 = LinkMeters * LinkMeters;
                m_Back.Clear();
                m_BestI.Clear();
                m_BestY.Clear();
                m_BestBoard.Clear();
                for (int q = 0; q < b.Length; q++)
                {
                    m_BestI.Add(-1);
                    m_BestY.Add(0f);
                    m_BestBoard.Add(0f);
                }
                m_PairAB.Add(m_Contacts.Count);
                for (int p = 0; p < a.Length; p++)
                {
                    if (!InBox(a[p], bb))
                        continue;
                    int best = -1;
                    float ya = NodeY(i, p), bestY = 0f, bestBoard = 0f;
                    for (int q = 0; q < b.Length; q++)
                    {
                        if (math.lengthsq(a[p] - b[q]) > r2)
                            continue;
                        float yb = NodeY(j, q), board = math.min(ya, yb);
                        if (best < 0 || board > bestBoard || (board == bestBoard && yb < bestY))
                        {
                            best = q;
                            bestY = yb;
                            bestBoard = board;
                        }
                        if (m_BestI[q] < 0 || board > m_BestBoard[q] || (board == m_BestBoard[q] && ya < m_BestY[q]))
                        {
                            m_BestI[q] = p;
                            m_BestY[q] = ya;
                            m_BestBoard[q] = board;
                        }
                    }
                    if (best >= 0)
                        m_Contacts.Add(new Contact { FromY = ya, ToY = bestY, FromNode = p, FromNode2 = -1, ToNode = best, ToNode2 = -1 });
                }
                for (int q = 0; q < b.Length; q++)
                {
                    if (m_BestI[q] >= 0)
                        m_Back.Add(new Contact { FromY = NodeY(j, q), ToY = m_BestY[q], FromNode = q, FromNode2 = -1, ToNode = m_BestI[q], ToNode2 = -1 });
                }
                if (a.Length >= 3 && b.Length >= 3)
                {
                    for (int p = 0; p < a.Length; p++)
                    {
                        int p1 = p + 1 < a.Length ? p + 1 : 0;
                        if (!SpansBox(a[p], a[p1], m_Box[j]))
                            continue;
                        for (int q = 0; q < b.Length; q++)
                        {
                            int q1 = q + 1 < b.Length ? q + 1 : 0;
                            if (!CrossAt(a[p], a[p1], b[q], b[q1], out float s, out float t))
                                continue;
                            float ya = math.lerp(NodeY(i, p), NodeY(i, p1), s), yb = math.lerp(NodeY(j, q), NodeY(j, q1), t);
                            m_Contacts.Add(new Contact { FromY = ya, ToY = yb, FromNode = p, FromNode2 = p1, ToNode = q, ToNode2 = q1 });
                            m_Back.Add(new Contact { FromY = yb, ToY = ya, FromNode = q, FromNode2 = q1, ToNode = p, ToNode2 = p1 });
                        }
                    }
                    for (int p = 0; p < a.Length; p++)
                    {
                        if (!InBox(a[p], bb) || !InsideRing(a[p], b))
                            continue;
                        float y = NodeY(i, p);
                        NearestEdge(b, a[p], out int q0, out int q1);
                        m_Contacts.Add(new Contact { FromY = y, ToY = y, FromNode = p, FromNode2 = -1, ToNode = q0, ToNode2 = q1 });
                        m_Back.Add(new Contact { FromY = y, ToY = y, FromNode = q0, FromNode2 = q1, ToNode = p, ToNode2 = -1 });
                    }
                    for (int q = 0; q < b.Length; q++)
                    {
                        if (!InBox(b[q], ba) || !InsideRing(b[q], a))
                            continue;
                        float y = NodeY(j, q);
                        NearestEdge(a, b[q], out int p0, out int p1);
                        m_Contacts.Add(new Contact { FromY = y, ToY = y, FromNode = p0, FromNode2 = p1, ToNode = q, ToNode2 = -1 });
                        m_Back.Add(new Contact { FromY = y, ToY = y, FromNode = q, FromNode2 = -1, ToNode = p0, ToNode2 = p1 });
                    }
                    for (int p = 0; p < a.Length; p++)
                    {
                        if (!InBox(a[p], bb) || InsideRing(a[p], b) || NearAnyNode(a[p], b, r2))
                            continue;
                        NearestEdge(b, a[p], out int q0, out int q1);
                        if (DistanceSqXZ(a[p], b[q0], b[q1]) > r2)
                            continue;
                        float ya = NodeY(i, p), yb = math.lerp(NodeY(j, q0), NodeY(j, q1), EdgeParameter(a[p], b[q0], b[q1]));
                        m_Contacts.Add(new Contact { FromY = ya, ToY = yb, FromNode = p, FromNode2 = -1, ToNode = q0, ToNode2 = q1 });
                        m_Back.Add(new Contact { FromY = yb, ToY = ya, FromNode = q0, FromNode2 = q1, ToNode = p, ToNode2 = -1 });
                    }
                    for (int q = 0; q < b.Length; q++)
                    {
                        if (!InBox(b[q], ba) || InsideRing(b[q], a) || NearAnyNode(b[q], a, r2))
                            continue;
                        NearestEdge(a, b[q], out int p0, out int p1);
                        if (DistanceSqXZ(b[q], a[p0], a[p1]) > r2)
                            continue;
                        float yb = NodeY(j, q), ya = math.lerp(NodeY(i, p0), NodeY(i, p1), EdgeParameter(b[q], a[p0], a[p1]));
                        m_Contacts.Add(new Contact { FromY = ya, ToY = yb, FromNode = p0, FromNode2 = p1, ToNode = q, ToNode2 = -1 });
                        m_Back.Add(new Contact { FromY = yb, ToY = ya, FromNode = q, FromNode2 = -1, ToNode = p0, ToNode2 = p1 });
                    }
                }
                m_PairABn.Add(m_Contacts.Count - m_PairAB[m_PairAB.Count - 1]);
                m_PairBA.Add(m_Contacts.Count);
                m_Contacts.AddRange(m_Back);
                m_PairBAn.Add(m_Back.Count);
            }

            private static bool NearAnyNode(float2 p, float2[] ring, float r2)
            {
                for (int k = 0; k < ring.Length; k++)
                {
                    if (math.lengthsq(p - ring[k]) <= r2)
                        return true;
                }
                return false;
            }

            /// <summary>Where on segment a - b the point nearest to p lies, 0 to 1.</summary>
            private static float EdgeParameter(float2 p, float2 a, float2 b)
            {
                float2 ab = b - a;
                float len2 = math.dot(ab, ab);
                return len2 > 0f ? math.saturate(math.dot(p - a, ab) / len2) : 0f;
            }

            private static float4 Grow(float4 box)
            {
                return box + new float4(-LinkMeters, -LinkMeters, LinkMeters, LinkMeters);
            }

            private static bool InBox(float2 p, float4 box)
            {
                return p.x >= box.x && p.y >= box.y && p.x <= box.z && p.y <= box.w;
            }

            /// <summary>Does segment p0-p1's xz extent overlap the box (a crossing of the box's outline needs it)?</summary>
            private static bool SpansBox(float2 p0, float2 p1, float4 box)
            {
                float2 lo = math.min(p0, p1), hi = math.max(p0, p1);
                return !(hi.x < box.x || lo.x > box.z || hi.y < box.y || lo.y > box.w);
            }

            /// <summary>Builds run <paramref name="run"/>'s walk graph once per Build: each node joined to its two ring neighbors and
            /// to every other node of the same outline within LinkMeters (across a cat track's ribbon).</summary>
            internal void EnsureWalk(int run)
            {
                if (m_WalkBuilt[run])
                    return;
                m_WalkBuilt[run] = true;
                int g0 = m_NodeStart[run], m = NodeCount(run);
                while (m_WalkFirst.Count < m_NodeStart[m_NodeStart.Count - 1])
                {
                    m_WalkFirst.Add(0);
                    m_WalkCount.Add(0);
                }
                float2[] o = m_Outlines[run];
                float r2 = LinkMeters * LinkMeters;
                for (int k = 0; k < m; k++)
                {
                    m_WalkFirst[g0 + k] = m_WalkAdj.Count;
                    for (int l = 0; l < m; l++)
                    {
                        if (l == k)
                            continue;
                        bool ring = l == (k + 1) % m || k == (l + 1) % m;
                        if (ring || math.lengthsq(o[k] - o[l]) <= r2)
                            m_WalkAdj.Add(l);
                    }
                    m_WalkCount[g0 + k] = m_WalkAdj.Count - m_WalkFirst[g0 + k];
                }
            }

            internal void WalkOf(int run, int node, out int first, out int count)
            {
                first = m_WalkFirst[m_NodeStart[run] + node];
                count = m_WalkCount[m_NodeStart[run] + node];
            }

            internal int WalkAt(int index) => m_WalkAdj[index];

            private bool SameInputs(List<float2[]> outlines, List<float[]> heights, List<int> areas)
            {
                if (m_Areas.Count != outlines.Count)
                    return false;
                for (int i = 0; i < outlines.Count; i++)
                {
                    if (!ReferenceEquals(m_Outlines[i], outlines[i]) || m_Areas[i] != areas[i] ||
                        !ReferenceEquals(m_HeightsIn[i], heights != null ? heights[i] : null))
                        return false;
                }
                return true;
            }

            private static float4 BoxOf(float2[] outline)
            {
                if (outline == null || outline.Length == 0)
                    return new float4(0f);
                float2 lo = outline[0], hi = outline[0];
                for (int k = 1; k < outline.Length; k++)
                {
                    lo = math.min(lo, outline[k]);
                    hi = math.max(hi, outline[k]);
                }
                return new float4(lo.x, lo.y, hi.x, hi.y);
            }

            /// <summary>The ring edge k0 - k1 nearest to p (xz; the first on a tie).</summary>
            private static void NearestEdge(float2[] ring, float2 p, out int k0, out int k1)
            {
                k0 = 0;
                float bestD = float.PositiveInfinity;
                for (int k = 0; k < ring.Length; k++)
                {
                    float d = DistanceSqXZ(p, ring[k], ring[k + 1 < ring.Length ? k + 1 : 0]);
                    if (d < bestD)
                    {
                        bestD = d;
                        k0 = k;
                    }
                }
                k1 = k0 + 1 < ring.Length ? k0 + 1 : 0;
            }
        }

        /// <summary>Are two outlines linked? Any node of a within LinkMeters (xz, inclusive) of any node or edge of b (or
        /// the reverse), or an edge of a crosses an edge of b, or a node of a lies inside b's ring (or the reverse). Rings are implicitly closed
        /// (the last node connects to the first, as the catalog's RingMetrics reads them). Null or empty gives false.</summary>
        public static bool OutlinesLinked(float2[] a, float2[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0)
                return false;
            float r2 = LinkMeters * LinkMeters;
            for (int i = 0; i < a.Length; i++)
            {
                for (int j = 0; j < b.Length; j++)
                {
                    if (math.lengthsq(a[i] - b[j]) <= r2)
                        return true;
                }
            }
            if (a.Length < 3 || b.Length < 3)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                float2 a0 = a[i], a1 = a[i + 1 < a.Length ? i + 1 : 0];
                for (int j = 0; j < b.Length; j++)
                {
                    float2 b0 = b[j], b1 = b[j + 1 < b.Length ? j + 1 : 0];
                    if (SegmentsCross(a0, a1, b0, b1) || DistanceSqXZ(a0, b0, b1) <= r2 || DistanceSqXZ(b0, a0, a1) <= r2)
                        return true;
                }
            }
            return InsideRing(a[0], b) || InsideRing(b[0], a);
        }

        private static float Cross(float2 o, float2 p, float2 q)
        {
            return (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);
        }

        /// <summary>Proper crossing of segments p0-p1 and q0-q1 (touching is already caught by the node distance test).</summary>
        private static bool SegmentsCross(float2 p0, float2 p1, float2 q0, float2 q1)
        {
            float d1 = Cross(q0, q1, p0), d2 = Cross(q0, q1, p1), d3 = Cross(p0, p1, q0), d4 = Cross(p0, p1, q1);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        /// <summary>SegmentsCross with the crossing's parameters: the point is p0 + s (p1 - p0) = q0 + t (q1 - q0).</summary>
        private static bool CrossAt(float2 p0, float2 p1, float2 q0, float2 q1, out float s, out float t)
        {
            float d1 = Cross(q0, q1, p0), d2 = Cross(q0, q1, p1), d3 = Cross(p0, p1, q0), d4 = Cross(p0, p1, q1);
            s = t = 0f;
            if (!(((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))))
                return false;
            s = d1 / (d1 - d2);
            t = d3 / (d3 - d4);
            return true;
        }

        /// <summary>Even-odd point-in-ring test.</summary>
        private static bool InsideRing(float2 p, float2[] ring)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                float2 a = ring[i], b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>The served rule with downhill run-to-run links; the attraction and stats paths both call it, so they cannot
        /// disagree. Runs <c>Assign</c> first (servingLift / servingDistanceM / servedCount stay DIRECT), then a work-list pass over
        /// <paramref name="links"/> (Build it first; a graph of another size is ignored). Every served run carries a CAP: the highest
        /// a skier on it can get (+inf for a directly served run: its whole outline). Through a contact (ContactAt):
        /// - from a plain run R: usable when the contact's height on R is at most R's cap; from a cat track T
        ///   (<paramref name="catTracks"/>, null = none): usable at a node of T the skiers reach;
        /// - the skier gets on at p = min(height on the source, height on the target) and the target's cap is
        ///   min(source cap, p + ClimbToleranceM), so a cap never grows along a chain (no 3 m ratchet per hop);
        /// - a plain run is served by the contact only when BelowJunction(p, its lowest, its highest node y) holds (a real part of it
        ///   lies below); a cat track by any contact with a track node no higher than the cap (the place's node or an end of its
        ///   edge). A cat track's skiers reach every
        ///   node joined to an entry through nodes no higher than that entry's cap (either way along it, and across its ribbon:
        ///   RunLinks.EnsureWalk); a directly served track is entered at its top node with cap +inf.
        /// servedRoot = the directly served run of the chain that first reached it, servedVia = the run it was first reached through
        /// (breadth-first from the directly served runs in index order); unserved: both -1. Without heights (flat) every contact is
        /// usable and every linked run passes, i.e. the old link rule. Output lists are cleared and filled; allocation-free once
        /// grown. A mask shorter than the run count reads false for the missing indices. Details: EngineNotes/LiftServedRule.md,
        /// "Near AssignLinked".</summary>
        public static void AssignLinked(List<float3> runTops, RunLinks links, List<Lift> lifts, List<Chord> chords,
            List<int> servingLift, List<float> servingDistanceM, List<int> servedCount,
            List<int> servedRoot, List<int> servedVia, List<bool> catTracks = null)
        {
            Assign(runTops, lifts, chords, servingLift, servingDistanceM, servedCount);
            int n = runTops.Count;
            servedRoot.Clear();
            servedVia.Clear();
            for (int r = 0; r < n; r++)
            {
                servedVia.Add(-1);
                servedRoot.Add(servingLift[r] >= 0 ? r : -1);
            }
            if (links == null || links.Count != n)
                return;
            List<int> queue = links.Queue;
            List<bool> queued = links.Queued, dirty = links.Dirty;
            List<float> cap = links.Cap, entryCap = links.EntryCap, nodeCap = links.NodeCap;
            queue.Clear();
            queued.Clear();
            dirty.Clear();
            cap.Clear();
            entryCap.Clear();
            nodeCap.Clear();
            int nodes = links.NodeStart(n);
            for (int g = 0; g < nodes; g++)
            {
                entryCap.Add(float.NegativeInfinity);
                nodeCap.Add(float.NegativeInfinity);
            }
            for (int r = 0; r < n; r++)
            {
                bool direct = servingLift[r] >= 0;
                cap.Add(direct ? float.PositiveInfinity : float.NegativeInfinity);
                queued.Add(direct);
                dirty.Add(false);
                if (!direct)
                    continue;
                queue.Add(r);
                if (IsTrack(catTracks, r) && links.NodeCount(r) > 0)
                {
                    entryCap[links.NodeStart(r) + links.TopNode(r)] = float.PositiveInfinity;
                    dirty[r] = true;
                }
            }
            for (int head = 0; head < queue.Count; head++)
            {
                int r = queue[head];
                queued[r] = false;
                bool track = IsTrack(catTracks, r);
                if (track && dirty[r])
                {
                    dirty[r] = false;
                    Walk(links, r);
                }
                int g0 = links.NodeStart(r);
                links.NeighboursOf(r, out int first, out int count);
                for (int k = 0; k < count; k++)
                {
                    int slot = first + k, m = links.Neighbour(slot);
                    bool mTrack = IsTrack(catTracks, m);
                    float2 range = links.YRange(m);
                    links.ContactsOf(slot, out int c0, out int cn);
                    for (int c = c0; c < c0 + cn; c++)
                    {
                        Contact ct = links.ContactAt(c);
                        // The highest a skier at the place gets: a plain run's cap, or on a track the best cap of a reached node (the
                        // place's node or an end of its edge) that is no lower than the place
                        float skier = cap[r];
                        if (track)
                        {
                            skier = PlaceCap(nodeCap, g0 + ct.FromNode, ct.FromY);
                            if (ct.FromNode2 >= 0)
                                skier = math.max(skier, PlaceCap(nodeCap, g0 + ct.FromNode2, ct.FromY));
                        }
                        if (!(ct.FromY <= skier))
                            continue;   // the skiers do not reach this contact on the source
                        float p = math.min(ct.FromY, ct.ToY);
                        float next = math.min(skier, p + ClimbToleranceM);
                        bool changed;
                        if (mTrack)
                        {
                            // Onto the track at the place's node, or at either end of its edge no higher than the cap (the place lies
                            // between the two ends' heights, so the lower end qualifies whenever the place does)
                            changed = Enter(links, entryCap, m, ct.ToNode, next);
                            if (ct.ToNode2 >= 0)
                                changed |= Enter(links, entryCap, m, ct.ToNode2, next);
                            if (changed)
                                dirty[m] = true;
                        }
                        else
                        {
                            changed = next > cap[m] && BelowJunction(p, range.x, range.y);
                            if (changed)
                                cap[m] = next;
                        }
                        if (!changed)
                            continue;
                        if (servedRoot[m] < 0)
                        {
                            servedRoot[m] = servedRoot[r];
                            servedVia[m] = r;
                        }
                        if (!queued[m])
                        {
                            queued[m] = true;
                            queue.Add(m);
                        }
                    }
                }
            }
        }

        /// <summary>Reached track node g's cap when a place at height y is no higher than it, else -inf.</summary>
        private static float PlaceCap(List<float> nodeCap, int g, float y)
        {
            float c = nodeCap[g];
            return y <= c ? c : float.NegativeInfinity;
        }

        /// <summary>Records an entry at track m's node k with cap c when the node is no higher than c and c beats its last entry.</summary>
        private static bool Enter(RunLinks links, List<float> entryCap, int m, int k, float c)
        {
            int g = links.NodeStart(m) + k;
            if (!(c > entryCap[g]) || !(links.NodeY(m, k) <= c))
                return false;
            entryCap[g] = c;
            return true;
        }

        private static bool IsTrack(List<bool> catTracks, int r)
        {
            return catTracks != null && r < catTracks.Count && catTracks[r];
        }

        /// <summary>Cat track <paramref name="run"/>'s reached nodes from its entries (EntryCap): entries in descending cap order,
        /// each flooding its walk graph through nodes no higher than its cap; NodeCap = the cap a node was first reached with (the
        /// highest, by the order), -inf when unreached. A node reached from a higher cap is never re-flooded by a lower one: what the
        /// lower cap reaches from there lies inside the higher cap's flood.</summary>
        private static void Walk(RunLinks links, int run)
        {
            links.EnsureWalk(run);
            int g0 = links.NodeStart(run), m = links.NodeCount(run);
            List<float> entryCap = links.EntryCap, nodeCap = links.NodeCap;
            List<int> flood = links.Flood;
            for (int k = 0; k < m; k++)
                nodeCap[g0 + k] = float.NegativeInfinity;
            while (true)
            {
                int start = -1;
                float c = float.NegativeInfinity;
                for (int k = 0; k < m; k++)
                {
                    if (nodeCap[g0 + k] == float.NegativeInfinity && entryCap[g0 + k] > c && links.NodeY(run, k) <= entryCap[g0 + k])
                    {
                        c = entryCap[g0 + k];
                        start = k;
                    }
                }
                if (start < 0)
                    return;
                flood.Clear();
                flood.Add(start);
                nodeCap[g0 + start] = c;
                for (int h = 0; h < flood.Count; h++)
                {
                    links.WalkOf(run, flood[h], out int first, out int count);
                    for (int e = first; e < first + count; e++)
                    {
                        int v = links.WalkAt(e);
                        if (nodeCap[g0 + v] != float.NegativeInfinity || !(links.NodeY(run, v) <= c))
                            continue;
                        nodeCap[g0 + v] = c;
                        flood.Add(v);
                    }
                }
            }
        }
    }

    /// <summary>One ski area's "closed" state with its hysteresis. A plain value the caller keeps per area.</summary>
    public struct AreaClosedState
    {
        public bool Known;          // the area has been observed (settled) at least once
        public bool Closed;         // the published state: the area has no open run, held for LiftClosedRule.HoldFrames
        public bool Pending;        // the observed state differs from Closed and has kept differing since PendingSince
        public uint PendingSince;   // the sim frame of the first observation of the differing state
    }

    /// <summary>The "closed" signal of a lift: its ski area has no open run, the state SkiLiftChargeSystem bills "at 25 %: no
    /// open run in its ski area" (SkiMoneyRules.LiftAreaRunOpen). It feeds LiftServedRule.Carries' closed flag
    /// (ChairPinSystem.IsLoopClosed, TramShuttleSystem.IsClosed, the [state] lines). A lift with no ski area is never closed.
    /// Hysteresis: the published state flips only when the observed area state has held for HoldFrames sim frames (one game
    /// hour), so a run that thaws for a minute does not start and stop the chairs. The clock is the sim frame index the charge
    /// system already reads (SimulationSystem.frameIndex, 262,144 per game day: SkiMoneyRules.TicksPerDay), observed at each
    /// charge snapshot (a pass every 1,024 frames at most), so a flip lands between HoldFrames and HoldFrames + 1,024 frames
    /// after the area changed. The first settled observation after a load is taken at once. Pure; no allocation.</summary>
    public static class LiftClosedRule
    {
        /// <summary>One game hour in sim frames: 262,144 / 24 = 10,922.67, rounded up.</summary>
        public const uint HoldFrames = 10923u;

        /// <summary>Feed one observation: <paramref name="areaOpen"/> = the area has an open run; <paramref name="nowFrame"/> = the
        /// sim frame (wrap-safe). Returns the published closed state.</summary>
        public static bool Step(ref AreaClosedState s, bool areaOpen, uint nowFrame)
        {
            bool observedClosed = !areaOpen;
            if (!s.Known)
            {
                s.Known = true;
                s.Closed = observedClosed;
                s.Pending = false;
                return s.Closed;
            }
            if (observedClosed == s.Closed)
            {
                s.Pending = false;
                return s.Closed;
            }
            if (!s.Pending)
            {
                s.Pending = true;
                s.PendingSince = nowFrame;
            }
            if (unchecked(nowFrame - s.PendingSince) >= HoldFrames)
            {
                s.Closed = observedClosed;
                s.Pending = false;
            }
            return s.Closed;
        }

        /// <summary>The lift's closed flag: never for a lift with no ski area (<paramref name="linked"/> false); else the area's
        /// published state.</summary>
        public static bool ClosedFor(bool linked, in AreaClosedState s)
        {
            return linked && s.Known && s.Closed;
        }

        /// <summary>A Shuttle type (the tram) is exempt from the no-open-run hold, since trams keep running in summer as
        /// sightseeing; Loop types (chairlifts, gondolas) and untyped rows (null) keep it.</summary>
        public static bool HoldsWhenClosed(LiftTypeDef type)
        {
            return type == null || type.Mover != MoverKind.Shuttle;
        }

        /// <summary>The lift's closed flag after the exemption: the area's published closed state, never for a Shuttle type.
        /// SkiLiftChargeSystem stores it in LiftCharge.Closed and the closed pair set (TramShuttleSystem.IsClosed,
        /// ChairPinSystem.IsLoopClosed, the hub), so the held reason "closed" never names a tram.</summary>
        public static bool ClosedForLift(bool areaClosed, LiftTypeDef type)
        {
            return areaClosed && HoldsWhenClosed(type);
        }

        /// <summary>The billing's area state: every type bills NoOpenRun while its area has no open run; the tram's share of it is
        /// <see cref="NoOpenRunShare"/>. The type argument stays for the callers and the tests.</summary>
        public static bool AreaOpenForBilling(bool areaRunOpen, LiftTypeDef type)
        {
            return areaRunOpen;
        }

        /// <summary>The share of the full rate a lift bills while its area has no open run: a Shuttle type keeps running for
        /// sightseers and bills SkiMoneyRules.ShuttleNoOpenRunShare (50 %); a Loop type or an untyped row stands and bills
        /// SkiMoneyRules.LiftReducedShare (25 %).</summary>
        public static float NoOpenRunShare(LiftTypeDef type)
        {
            return HoldsWhenClosed(type) ? SkiMoneyRules.LiftReducedShare : SkiMoneyRules.ShuttleNoOpenRunShare;
        }
    }
}
