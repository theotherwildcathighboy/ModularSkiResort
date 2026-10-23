// The lift-served rule and the power predicate. Consumers: SkiResortAttractivenessSystem, SkiResortStatsSystem, SkiLiftChargeSystem
// and SkiLiftTicketSystem, via LiftCensusSystem.CopyServeLifts / ReadLiftPower; tests/LiftBatch and tests/ResortBatch link it.
// A run is served by lift L when L's row is Complete, IsPowered(L.Power), the run's top T has T.y <= TopY + TopMarginM and its xz
// distance to the nearest chord of L is <= ServeRadiusM (both bounds inclusive; the nearest lift serves, a tie goes to the lower
// EntityRef). A run linked to a served run is served too (AssignLinked). TerminalsOnly lifts (tram) measure to TopXZ instead.
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

        /// <summary>The "powered" predicate for service, lift value, lift billing and the lift charge's operating count. Unknown
        /// counts as powered. With PowerGatesService false, power is status only everywhere (one switch).</summary>
        public static bool IsPowered(LiftPower power)
        {
            return !PowerGatesService || power != LiftPower.NoPower;
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
        /// inclusive) of any outline node of the other, or their outlines overlap (an edge crossing, or a node inside the other's
        /// ring). 25 m: a connector is drawn from a node placed on or beside the run it leaves, so the nodes of the two outlines
        /// meet within a brush width or two; 26 m apart is not linked (ServedLinkTests). One const, no setting.</summary>
        public const float LinkMeters = 25f;

        /// <summary>The run-to-run link graph of one recompute. Build(outlines, areas) fills a CSR adjacency; it is
        /// SELF-VALIDATING: when the per-run outline arrays are the very same objects as at the last Build (the catalog keeps
        /// one float2[] per run until its outline moves) and the area ids are equal, the graph is kept and no pair is tested,
        /// so the O(n^2 x nodes) pass runs only after an outline, a binding or the run set changed, not on every soft
        /// recompute. Allocation-free once grown. Main thread, one instance per caller.</summary>
        public sealed class RunLinks
        {
            private readonly List<float2[]> m_Outlines = new List<float2[]>(32);
            private readonly List<int> m_Areas = new List<int>(32);
            private readonly List<float4> m_Box = new List<float4>(32);   // minX, minZ, maxX, maxZ of each outline
            private readonly List<int> m_Start = new List<int>(33);
            private readonly List<int> m_Adj = new List<int>(64);
            private readonly List<int> m_PairA = new List<int>(32);
            private readonly List<int> m_PairB = new List<int>(32);
            private readonly List<int> m_Fill = new List<int>(32);
            internal readonly List<int> Queue = new List<int>(32);        // AssignLinked's BFS scratch
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

            /// <summary>Builds (or keeps) the graph. outlines[i] = run i's outline nodes (xz; null or empty = no outline, never
            /// linked); areas[i] = run i's ski area id (any int; a negative id = no area, never linked). Both lists have one
            /// entry per run. Returns true when the graph was recomputed.</summary>
            public bool Build(List<float2[]> outlines, List<int> areas)
            {
                int n = outlines.Count;
                if (areas.Count != n)
                    throw new ArgumentException("areas and outlines differ in length");
                if (m_Valid && SameInputs(outlines, areas))
                {
                    Reuses++;
                    return false;
                }
                Rebuilds++;
                m_Outlines.Clear();
                m_Areas.Clear();
                m_Box.Clear();
                m_PairA.Clear();
                m_PairB.Clear();
                for (int i = 0; i < n; i++)
                {
                    m_Outlines.Add(outlines[i]);
                    m_Areas.Add(areas[i]);
                    m_Box.Add(BoxOf(outlines[i]));
                }
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
                for (int k = 0; k < m_Start[n]; k++)
                    m_Adj.Add(0);
                for (int k = 0; k < m_PairA.Count; k++)
                {
                    int a = m_PairA[k], b = m_PairB[k];
                    m_Adj[m_Fill[a]++] = b;
                    m_Adj[m_Fill[b]++] = a;
                }
                m_Valid = true;
                return true;
            }

            private bool SameInputs(List<float2[]> outlines, List<int> areas)
            {
                if (m_Areas.Count != outlines.Count)
                    return false;
                for (int i = 0; i < outlines.Count; i++)
                {
                    if (!ReferenceEquals(m_Outlines[i], outlines[i]) || m_Areas[i] != areas[i])
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
        }

        /// <summary>Are two outlines linked? Any node of a within LinkMeters (xz, inclusive) of any node of b, or an
        /// edge of a crosses an edge of b, or a node of a lies inside b's ring (or the reverse). Rings are implicitly closed
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
                    if (SegmentsCross(a0, a1, b[j], b[j + 1 < b.Length ? j + 1 : 0]))
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

        /// <summary>The served rule with run-to-run links; the attraction and stats paths both call it, so they cannot disagree.
        /// Runs <c>Assign</c> first (servingLift / servingDistanceM / servedCount stay DIRECT), then a breadth-first pass over
        /// <paramref name="links"/> (Build it first; a graph of another size is ignored): a run is SERVED when its own top passes
        /// (servedRoot[i] = i, servedVia[i] = -1) or a chain of linked runs reaches a directly served one (root = the nearest by
        /// link count, ties to the lower index; via = the run it was reached through). Unserved: both -1. Output lists are cleared
        /// and filled. <paramref name="leafRuns"/> (null = none) marks LEAF runs by index: a leaf may be served but never passes
        /// service on, so a cat track crossing a black run does not "serve" it. A short list reads false for missing indices.
        /// Details: EngineNotes/LiftServedRule.md, "Near AssignLinked".</summary>
        public static void AssignLinked(List<float3> runTops, RunLinks links, List<Lift> lifts, List<Chord> chords,
            List<int> servingLift, List<float> servingDistanceM, List<int> servedCount,
            List<int> servedRoot, List<int> servedVia, List<bool> leafRuns = null)
        {
            Assign(runTops, lifts, chords, servingLift, servingDistanceM, servedCount);
            int n = runTops.Count;
            servedRoot.Clear();
            servedVia.Clear();
            List<int> queue = links != null ? links.Queue : new List<int>(n);
            queue.Clear();
            for (int r = 0; r < n; r++)
            {
                servedVia.Add(-1);
                if (servingLift[r] >= 0)
                {
                    servedRoot.Add(r);
                    queue.Add(r);
                }
                else
                {
                    servedRoot.Add(-1);
                }
            }
            if (links == null || links.Count != n)
                return;
            for (int head = 0; head < queue.Count; head++)
            {
                int r = queue[head];
                if (leafRuns != null && r < leafRuns.Count && leafRuns[r])
                    continue;   // a leaf is served at most; it never serves its neighbors
                links.NeighboursOf(r, out int first, out int count);
                for (int k = 0; k < count; k++)
                {
                    int m = links.Neighbour(first + k);
                    if (servedRoot[m] >= 0)
                        continue;
                    servedRoot[m] = servedRoot[r];
                    servedVia[m] = r;
                    queue.Add(m);
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
