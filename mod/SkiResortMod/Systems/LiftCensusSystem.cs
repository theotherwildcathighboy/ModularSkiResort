// ECS side of the lift census: probe, snapshot, census, logs, park. The model itself is the pure LiftCensus. A lift is edited
// section by section: this system never deletes anything; it reads, logs, asks ChairPinSystem for a loop rebuild, and (via
// LiftParkSystem, same file) asks the GAME to re-seat the chairs of surviving sections of an opened lift.
// Registration (Mod.cs): UpdateBefore<LiftCensusSystem, ChairPinSystem>(Modification4B), a mod-to-mod anchor in the same phase.
// Frame counts are update counts: Modification phases run every frame, paused included (ModificationSystem.cs:19-26).
// ReadLiftPower is THE only reader of a lift station's ElectricityConsumer; callers pass a census row's Base-role station.
// A census exception disables the census until the next load (IsArmed false, so ChairPinSystem and the tram fail open).
// Engine notes: EngineNotes/LiftCensusSystem.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftCensusSystem : GameSystemBase
    {
        /// <summary>Coalesced (Updated-only) triggers build at most once per this many frames, plus a trailing build.</summary>
        internal const int CoalesceFrames = 10;
        /// <summary>A watchdog build runs when this many frames passed since the last build while any lift exists.</summary>
        internal const int WatchdogFrames = 4096;
        /// <summary>Frames after the park events are created when the re-seat is checked.</summary>
        internal const int ParkCheckFrames = 2;
        /// <summary>A park queue older than this many frames means LiftParkSystem is not running.</summary>
        internal const int ParkEmitterTimeoutFrames = 3;


        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private ChairPinSystem m_ChairPin;
        private Game.UI.NameSystem m_NameSystem;
        private LiftTowerFitSystem m_TowerFit;     // fits new / changed lines (writes at Modification1 of a later frame)
        private LiftTowerDiagSystem m_TowerDiag;   // read-only tower log; fed after each build, printed on DumpCensus
        private TowerNumberTileSystem m_TowerNumbers;   // per-tower number tiles; fed after each build, reconciled once quiet
        private TrainPitchSystem m_TrainPitch;          // line towers' sheave train pitch (lift alignment Phase 2); fed after each build
        private LiftClearanceSystem m_Clearance;        // low spans (hub row LiftLowSpan); fed after each build

        private EntityQuery m_EdgeProbe, m_NodeProbe, m_StationProbe, m_DeletedObjectProbe, m_DeletedLaneProbe;
        private EntityQuery m_EdgeQuery, m_StationQuery;


        private readonly LiftCensus m_Census = new LiftCensus();
        private readonly List<LiftCensus.Edge> m_Edges = new List<LiftCensus.Edge>();
        private readonly List<LiftCensus.Node> m_Nodes = new List<LiftCensus.Node>();
        private readonly List<LiftCensus.Station> m_Stations = new List<LiftCensus.Station>();
        private readonly List<Entity> m_StationEntities = new List<Entity>();
        private readonly List<LiftCensus.Event> m_Events = new List<LiftCensus.Event>();
        private readonly Dictionary<Entity, int> m_StationIndex = new Dictionary<Entity, int>();
        private readonly HashSet<Entity> m_NodeSeen = new HashSet<Entity>();

        private struct DeletedEdge
        {
            public Entity Edge, Start, End;
            public float LengthM;
            public float3 StartPos, EndPos;
            public bool StartGone, EndGone;
            public int PrevRow;
        }
        private readonly List<DeletedEdge> m_DeletedEdges = new List<DeletedEdge>();
        private readonly Dictionary<Entity, int> m_GoneChairsByOwner = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, int> m_GoneTowersByOwner = new Dictionary<Entity, int>();
        private readonly HashSet<Entity> m_DeletedMarkerNodes = new HashSet<Entity>();
        private readonly Dictionary<Entity, int> m_RemovedChairs = new Dictionary<Entity, int>();   // owner edge → chairs
        private readonly List<Entity> m_RemovedTowers = new List<Entity>();                        // owner nodes
        private readonly List<Entity> m_RemovedLanes = new List<Entity>();                         // owner edges
        private readonly HashSet<int> m_RowsWithRemoved = new HashSet<int>();

        private long m_Frame;
        private long m_LastBuildFrame;
        private bool m_InitialBuild;
        private bool m_CoalescePending;
        private string m_RequestedWhy;   // RequestBuild's reason, for the next coalesced build

        private readonly Dictionary<Entity, Transform> m_StationTransforms = new Dictionary<Entity, Transform>();

        private readonly List<Entity> m_ParkQueue = new List<Entity>();
        private readonly HashSet<Entity> m_Parked = new HashSet<Entity>();
        private readonly List<Entity> m_ParkCheckEdges = new List<Entity>();
        private readonly List<Entity> m_Scratch = new List<Entity>();
        private long m_ParkQueuedFrame = -1, m_ParkFlushedFrame = -1, m_ParkCheckFrame = -1;
        private int m_ParkRiders;
        private int m_ParkChairs;   // log only: every chair on the parked sections (riders or not)
        private bool m_ParkEmitterWarned;

        private struct RowCounts { public int Chairs, Towers; public uint EdgeSig; }
        private readonly Dictionary<long, RowCounts> m_RowCounts = new Dictionary<long, RowCounts>();
        private readonly List<uint> m_RowSig = new List<uint>();

        private readonly List<int> m_ServeCounts = new List<int>();                               // chords per row (reused)
        private readonly Dictionary<Entity, LiftPower> m_LastPower = new Dictionary<Entity, LiftPower>();   // per base station
        private readonly HashSet<Entity> m_NoConsumerWarned = new HashSet<Entity>();              // once per station per load
        private bool m_ServeLogged;                                                               // "LiftCensus [serve]" this load
        private readonly LogWindow m_PowerLog = new LogWindow(10f);                               // "Lift [power]" lines
        // Flicker guard's transient state per base station, cleared per load with m_LastPower. An entry exists only while a Powered
        // streak runs (m_BackSince, m_StreakReadFrame) or while a NoPower episode has absorbed a flicker (m_GuardRecuts).
        private readonly Dictionary<Entity, uint> m_BackSince = new Dictionary<Entity, uint>();       // streak start (sim frame)
        private readonly Dictionary<Entity, uint> m_StreakReadFrame = new Dictionary<Entity, uint>(); // last read in a streak
        private readonly Dictionary<Entity, int> m_GuardRecuts = new Dictionary<Entity, int>();       // flickers this episode
        private Game.Simulation.SimulationSystem m_Simulation;                                      // frameIndex (SimulationSystem.cs:68)

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private string m_LastError;

        /// <summary>++ whenever the row content changed (LiftCensus.Revision).</summary>
        public int Revision => m_Census.Revision;
        public string LastEvent = "-";
        public float LastBuildMs, MaxBuildMs;
        public int Builds;
        // The harvest walks every net edge in the city, so its cost grows with the city, not the resort: the first build of a
        // load and each new maximum above CostReportMs print one "[cost]" line (at most CostLinesMax per load) for field tests.
        private const float CostReportMs = 1f;
        private const int CostLinesMax = 4;
        private int m_CostLines;

        /// <summary>The station whose Transform last changed, and when (UnityEngine.Time.frameCount; see engine notes, Frames and time).</summary>
        internal Entity LastMovedStation { get; private set; }
        internal uint LastMovedFrame { get; private set; }
        /// <summary>realtimeSinceStartup of the last move.</summary>
        internal float LastMovedRealtime { get; private set; } = float.NegativeInfinity;

        /// <summary>Resolved per load (Entity.Null until then, or when MISSING): the prefab catalog's fixed-grip line entity.</summary>
        internal Entity ChairliftPrefab => m_PrefabCatalog.LineEntity(SkiCatalog.Lifts[0]);
        internal Entity MarkerPrefab => m_PrefabCatalog.MarkerEntity;
        internal bool IsArmed => m_Armed && !m_Disabled;

        /// <summary>Copies every row (row order) into <paramref name="into"/> (cleared first).</summary>
        internal int CopyRows(List<LiftCensus.Row> into)
        {
            into.Clear();
            var rows = m_Census.Rows;
            for (int i = 0; i < rows.Count; i++)
                into.Add(rows[i]);
            return rows.Count;
        }

        /// <summary>The lift-line edges of the last build and their end nodes, each once, edge order (LiftLaneScope.OwnersOf): the
        /// entities whose Game.Net.SubLane buffers hold every lift cable lane, for ChairPinSystem's and TramShuttleSystem's lane harvests.
        /// <paramref name="into"/> is cleared first. Returns -1 (nothing copied) when the census is not armed or has not built this load:
        /// the caller then scans the city. Main thread; allocation-free once the scratch has grown.</summary>
        internal int CopyLineOwners(List<Entity> into)
        {
            into.Clear();
            if (!IsArmed || Builds == 0)
                return -1;
            m_OwnerEdgeScratch.Clear();
            for (int i = 0; i < m_Edges.Count; i++)
            {
                LiftCensus.Edge e = m_Edges[i];
                m_OwnerEdgeScratch.Add(new LiftLaneScope.LineEdge<Entity> { Edge = Ent(e.Id), Start = Ent(e.Start), End = Ent(e.End) });
            }
            return LiftLaneScope.OwnersOf(m_OwnerEdgeScratch, into, m_OwnerSeen);
        }

        private readonly List<LiftLaneScope.LineEdge<Entity>> m_OwnerEdgeScratch = new List<LiftLaneScope.LineEdge<Entity>>();
        private readonly HashSet<Entity> m_OwnerSeen = new HashSet<Entity>();


        /// <summary>Every census row (row order = CopyRows) as a served-rule lift; its line chords (each lift-line edge's StartPos.xz ->
        /// EndPos.xz of the last build) appended to <paramref name="chords"/>. Both lists are cleared first. A Complete row's base-station
        /// power is read NOW (ReadLiftPower). Returns the lift count; 0 when the census is not armed. The first call per load writes the
        /// "LiftCensus [serve]" cost line.</summary>
        internal int CopyServeLifts(List<LiftServedRule.Lift> lifts, List<LiftServedRule.Chord> chords)
        {
            lifts.Clear();
            chords.Clear();
            if (!IsArmed)
                return 0;
            long t0 = Stopwatch.GetTimestamp();
            var rows = m_Census.Rows;

            // 1. Chords per row (an edge whose row is unknown or out of range is skipped).
            m_ServeCounts.Clear();
            for (int r = 0; r < rows.Count; r++)
                m_ServeCounts.Add(0);
            for (int i = 0; i < m_Edges.Count; i++)
            {
                if (m_Census.TryGetRowOfEdge(m_Edges[i].Id, out int r) && r >= 0 && r < rows.Count)
                    m_ServeCounts[r]++;
            }

            // 2. One Lift per row; its slice of the chord list starts at the running sum.
            int first = 0, reads = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                LiftCensus.Row row = rows[r];
                bool complete = row.State == LiftCensus.State.Complete;
                LiftPower power = LiftPower.Unknown;
                if (complete)
                {
                    power = ReadLiftPower(Ent(row.Base));
                    reads++;
                }
                // The row's lift type's serve rule; a Mixed or untyped row reads AlongLine with radius 0 (it is Unfinished, so Serves is false
                // whatever the radius)
                LiftTypeDef def = row.Mixed ? null : SkiCatalog.LiftByIndex(row.Type);
                lifts.Add(new LiftServedRule.Lift
                {
                    Id = row.Id,
                    Base = row.Base,
                    Top = row.Top,
                    Complete = complete,
                    Power = power,
                    TopY = TopYOf(row.Top),
                    FirstChord = first,
                    ChordCount = 0,
                    Rule = def != null ? (byte)def.ServeRule : (byte)ServeRuleKind.AlongLine,
                    RadiusM = def != null ? def.ServeRadiusM : 0f,
                    TopMarginM = def != null ? def.TopMarginM : 0f,
                    TopXZ = TopXZOf(row.Top),
                });
                first += m_ServeCounts[r];
            }

            // 3. The chords, written into each lift's slice in edge order.
            for (int c = 0; c < first; c++)
                chords.Add(default(LiftServedRule.Chord));
            for (int i = 0; i < m_Edges.Count; i++)
            {
                if (!m_Census.TryGetRowOfEdge(m_Edges[i].Id, out int r) || r < 0 || r >= rows.Count)
                    continue;
                LiftServedRule.Lift lift = lifts[r];
                chords[lift.FirstChord + lift.ChordCount] = new LiftServedRule.Chord
                {
                    A = m_Edges[i].StartPos.xz,
                    B = m_Edges[i].EndPos.xz,
                };
                lift.ChordCount++;
                lifts[r] = lift;
            }

            // Written at the first serve that has a lift row, so the line does not measure an empty city
            if (!m_ServeLogged && lifts.Count > 0)
            {
                m_ServeLogged = true;
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                Mod.Log.Info($"LiftCensus [serve] first CopyServeLifts with lifts this load: {lifts.Count} row(s), {chords.Count} " +
                             $"chord(s), {reads} power read(s), {ms:F2} ms");
            }
            return lifts.Count;
        }

        /// <summary>The power of the lift whose base station is <paramref name="baseStation"/>, read now from its ElectricityConsumer: the
        /// ONLY reader of a lift station's consumer in the mod. EntityManager.TryGetComponent is the Colossal.Entities extension
        /// HasComponent + GetComponentData (EntitiesExtensions.cs:76-96 in the Colossal.Core decompile), and GetComponentData completes the
        /// component's writing jobs (DispatchElectricitySystem): a sync point, taken per Complete row on a caller's recompute or poll, never
        /// per frame. Unknown when the entity is Null, gone, or has no consumer (logged once per station per load). A value that differs
        /// from the last one read for that station (or a first read that is NoPower) is offered to m_PowerLog as a "Lift [power]" line. The
        /// value returned and stored is the GUARDED one (LiftServedRule.GuardPower: off at once, back after PowerBackHoldFrames of power);
        /// <paramref name="baseStation"/> is always a census row's Base, never a top station.</summary>
        internal LiftPower ReadLiftPower(Entity baseStation)
        {
            if (baseStation == Entity.Null || !EntityManager.Exists(baseStation) ||
                EntityManager.HasComponent<Deleted>(baseStation))
                return LiftPower.Unknown;
            if (!EntityManager.TryGetComponent(baseStation, out Game.Buildings.ElectricityConsumer consumer))
            {
                if (m_NoConsumerWarned.Add(baseStation))
                    Mod.Log.Info($"LiftCensus [power] base station {Fmt(baseStation)} has no ElectricityConsumer — its lift " +
                                 "is treated as powered");
                return LiftPower.Unknown;
            }
            // The last value read for this station feeds the rule's hysteresis (NoPower under half, back at three quarters); a first read
            // passes Unknown
            bool known = m_LastPower.TryGetValue(baseStation, out LiftPower last);
            LiftPower raw = LiftServedRule.PowerOf((byte)consumer.m_Flags, consumer.m_WantedConsumption,
                                                   consumer.m_FulfilledConsumption, known ? last : LiftPower.Unknown);
            LiftPower power = raw;
            int flickers = 0;
            if (known)
            {
                // The flicker guard, with the station's last EFFECTIVE value. The first read of a station this load has no history and is not
                // guarded. A station that is not NoPower and not in a streak touches no dictionary but this one TryGetValue (the identity path).
                uint now = m_Simulation.frameIndex;
                uint since = m_BackSince.TryGetValue(baseStation, out uint started) ? started : LiftServedRule.NoBackSince;
                uint before = since;
                if (since != LiftServedRule.NoBackSince && m_StreakReadFrame.TryGetValue(baseStation, out uint lastRead))
                    since = LiftServedRule.ExpireStreak(since, lastRead, now);
                power = LiftServedRule.GuardPower(raw, last, ref since, now, out bool recut);
                if (since != before)
                {
                    if (since == LiftServedRule.NoBackSince)
                    {
                        m_BackSince.Remove(baseStation);
                        m_StreakReadFrame.Remove(baseStation);
                    }
                    else
                    {
                        m_BackSince[baseStation] = since;
                    }
                }
                if (since != LiftServedRule.NoBackSince)
                    m_StreakReadFrame[baseStation] = now;
                if (recut)
                    m_GuardRecuts[baseStation] = (m_GuardRecuts.TryGetValue(baseStation, out int n) ? n : 0) + 1;
                if (last == LiftPower.NoPower && power != LiftPower.NoPower && m_GuardRecuts.TryGetValue(baseStation, out flickers))
                    m_GuardRecuts.Remove(baseStation);   // the episode ended: its count goes into the power-back line
            }
            m_LastPower[baseStation] = power;
            if (known ? last != power : power == LiftPower.NoPower)
                OfferPowerLine(baseStation, power, consumer, flickers);
            return power;
        }

        /// <summary>FNV-1a of (base station Index, Version, LiftPower) over every Complete row, read now (ReadLiftPower, so a poll also logs
        /// a transition), then the row count. Power changes raise no entity event: callers poll this at their own cadence and compare for
        /// equality. 0 when the census is not armed.</summary>
        internal int ComputePowerSignature()
        {
            if (!IsArmed)
                return 0;
            ulong hash = SkiRunRatingRules.FnvOffset;
            var rows = m_Census.Rows;
            int complete = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].State != LiftCensus.State.Complete)
                    continue;
                complete++;
                LiftPower power = ReadLiftPower(Ent(rows[r].Base));
                hash = SkiRunRatingRules.Mix(hash, (uint)rows[r].Base.Index);
                hash = SkiRunRatingRules.Mix(hash, (uint)rows[r].Base.Version);
                hash = SkiRunRatingRules.Mix(hash, (uint)power);
            }
            hash = SkiRunRatingRules.Mix(hash, (uint)complete);
            return (int)(hash ^ (hash >> 32));
        }

        /// <summary>The top station's stub-tip y in the last build (the served rule's TopY); NaN when the row has no top or the top station
        /// is not in the last build.</summary>
        private float TopYOf(EntityRef top)
        {
            if (top.IsNull || !m_StationIndex.TryGetValue(Ent(top), out int s) || s < 0 || s >= m_Stations.Count)
                return float.NaN;
            return m_Stations[s].StubTip.y;
        }

        /// <summary>The top station's stub-tip xz in the last build (the TerminalsOnly rule's center and SummitServed's); NaN when the row
        /// has no top or it is not in the last build. No new Transform read: the census station's StubTip (per row StubTipLocalZ).</summary>
        private float2 TopXZOf(EntityRef top)
        {
            if (top.IsNull || !m_StationIndex.TryGetValue(Ent(top), out int s) || s < 0 || s >= m_Stations.Count)
                return new float2(float.NaN, float.NaN);
            return m_Stations[s].StubTip.xz;
        }

        /// <summary>A SkiCatalog index as the census byte; LiftCensus.NoType when out of 0..254.</summary>
        private static byte TypeByte(int liftTypeIndex)
        {
            return liftTypeIndex >= 0 && liftTypeIndex < LiftCensus.NoType ? (byte)liftTypeIndex : LiftCensus.NoType;
        }

        /// <summary>One "Lift [power]" transition through m_PowerLog (LogWindow 10 s): Plain -> the line now; Summary -> the fold line;
        /// Fold -> counted (slot 0 = to no power, slot 1 = back). <paramref name="flickers"/> = raw NoPower reads the guard absorbed this
        /// episode (named in the power-back line when &gt; 0).</summary>
        private void OfferPowerLine(Entity baseStation, LiftPower power, Game.Buildings.ElectricityConsumer consumer, int flickers)
        {
            bool toNoPower = power == LiftPower.NoPower;
            // The warning bracket only while a warning bit is still set
            bool warning = ((byte)consumer.m_Flags &
                            (LiftServedRule.FlagNoElectricityWarning | LiftServedRule.FlagBottleneckWarning)) != 0;
            // The lift stops, and the flicker guard is named in both lines (the effective value is the guarded one)
            string body = toNoPower
                ? $"{DescribeBase(baseStation)} — no power: under half its power arrives (wanted {consumer.m_WantedConsumption}, " +
                  $"fulfilled {consumer.m_FulfilledConsumption}, flags {consumer.m_Flags}); the lift stops, counts 0 and serves no " +
                  $"run until three quarters arrive or the warning clears, held {LiftServedRule.PowerBackHoldFrames} frames against flicker"
                : $"{DescribeBase(baseStation)} — power back (wanted {consumer.m_WantedConsumption}, fulfilled " +
                  $"{consumer.m_FulfilledConsumption}{(warning ? "; the game still shows its warning" : "")}) after " +
                  $"{LiftServedRule.PowerBackHoldFrames} frames of power{(flickers > 0 ? $"; {flickers} flicker(s) absorbed" : "")}; " +
                  "it runs and serves runs again";
            float now = UnityEngine.Time.realtimeSinceStartup;
            switch (m_PowerLog.Offer(now, false, body, toNoPower ? 1 : 0, toNoPower ? 0 : 1))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info("Lift [power]: " + body);
                    break;
                case LogWindow.Verdict.Summary:
                    WritePowerSummary(now);
                    break;
            }
        }

        /// <summary>The fold line of m_PowerLog; restarts its window.</summary>
        private void WritePowerSummary(float now)
        {
            Mod.Log.Info($"Lift [power] {m_PowerLog.Count} more power change(s) in {m_PowerLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={m_PowerLog.WindowS:0} s): {m_PowerLog.Sum(0):F0} to no power, " +
                         $"{m_PowerLog.Sum(1):F0} back to powered — last: {m_PowerLog.LastText}");
            m_PowerLog.Wrote(now);
        }

        /// <summary>Describe of the row whose BASE is <paramref name="baseStation"/> (else any row touching it), for the power lines;
        /// "lift (base i:v)" when no row names it.</summary>
        private string DescribeBase(Entity baseStation)
        {
            EntityRef s = Ref(baseStation);
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Base == s)
                    return Describe(rows[r]);
            }
            return TryGetRowOfStation(baseStation, out LiftCensus.Row row)
                ? Describe(row)
                : $"lift (base {Fmt(baseStation)})";
        }

        /// <summary>The row whose Id and Top are exactly these (the hub's (entity, top) pair).</summary>
        internal bool TryGetRow(Entity id, Entity top, out LiftCensus.Row row)
        {
            EntityRef i = Ref(id), t = Ref(top);
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Id == i && rows[r].Top == t)
                {
                    row = rows[r];
                    return true;
                }
            }
            row = default;
            return false;
        }

        /// <summary>The first row (row order) whose lowest span is <paramref name="edge"/> (LiftCensus.Row.LowEdge): the hub's LiftLowSpan
        /// row sends that edge to focusLift.</summary>
        internal bool TryGetRowOfLowEdge(Entity edge, out LiftCensus.Row row)
        {
            EntityRef e = Ref(edge);
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count && !e.IsNull; r++)
            {
                if (rows[r].LowSpan && rows[r].LowEdge == e)
                {
                    row = rows[r];
                    return true;
                }
            }
            row = default;
            return false;
        }

        /// <summary>The node is a station's connection node (the last build's stations; LiftClearanceSystem skips the station approach).</summary>
        internal bool IsStationNode(Entity node) => node != Entity.Null && StationOfNode(node) >= 0;

        /// <summary>The first row (row order) whose tower past its train's limits stands on <paramref name="node"/> (LiftCensus.Row.TrainNode):
        /// the hub's LiftTrainLimit row sends that node to focusLift.</summary>
        internal bool TryGetRowOfTrainNode(Entity node, out LiftCensus.Row row)
        {
            EntityRef n = Ref(node);
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count && !n.IsNull; r++)
            {
                if (rows[r].TrainNode == n)
                {
                    row = rows[r];
                    return true;
                }
            }
            row = default;
            return false;
        }

        /// <summary>The first row (row order) whose sharpest bent tower stands on <paramref name="node"/> (LiftCensus.Row.BendNode): the hub's
        /// LiftBend row sends that node to focusLift.</summary>
        internal bool TryGetRowOfBendNode(Entity node, out LiftCensus.Row row)
        {
            EntityRef n = Ref(node);
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count && !n.IsNull; r++)
            {
                if (rows[r].BendNode == n)
                {
                    row = rows[r];
                    return true;
                }
            }
            row = default;
            return false;
        }

        /// <summary>The first row (row order) whose Base or Top is this station.</summary>
        internal bool TryGetRowOfStation(Entity station, out LiftCensus.Row row)
        {
            EntityRef s = Ref(station);
            var rows = m_Census.Rows;
            if (!s.IsNull)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    if (rows[r].Base == s || rows[r].Top == s)
                    {
                        row = rows[r];
                        return true;
                    }
                }
            }
            row = default;
            return false;
        }

        /// <summary>The other station of the first row touching <paramref name="station"/> that has both stations and whose other station is
        /// still in the last build (a row may keep a removed station).</summary>
        internal bool TryGetOppositeStation(Entity station, out Entity other)
        {
            EntityRef s = Ref(station);
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count && !s.IsNull; r++)
            {
                LiftCensus.Row row = rows[r];
                if (row.Base.IsNull || row.Top.IsNull)
                    continue;
                EntityRef o = row.Base == s ? row.Top : row.Top == s ? row.Base : default;
                if (o.IsNull || !m_StationIndex.ContainsKey(Ent(o)))
                    continue;
                other = Ent(o);
                return true;
            }
            other = Entity.Null;
            return false;
        }

        /// <summary>The station is in the last build's station list (alive then).</summary>
        private bool StationInBuild(EntityRef station)
        {
            return !station.IsNull && m_StationIndex.ContainsKey(Ent(station));
        }

        /// <summary>The station is Base or Top of a Complete row.</summary>
        internal bool IsOnCompleteRow(Entity station)
        {
            EntityRef s = Ref(station);
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count; r++)
                if (rows[r].State == LiftCensus.State.Complete && (rows[r].Base == s || rows[r].Top == s))
                    return true;
            return false;
        }

        /// <summary>Stations of the last build (positions may lag a moving station by up to CoalesceFrames; the aid reads live Transforms
        /// for the pair it uses).</summary>
        internal int StationCount => m_StationEntities.Count;
        internal Entity StationEntityAt(int i) => m_StationEntities[i];
        internal LiftCensus.Station StationAt(int i) => m_Stations[i];

        /// <summary>Station prefab to base (true) / top (false), from the catalog row's Role. False for a non-station prefab.</summary>
        internal bool TryGetStationKind(Entity prefab, out bool isBase)
        {
            if (m_PrefabCatalog.TryGetStation(prefab, out StationDef def))
            {
                isBase = def.Role == StationRole.Base;
                return true;
            }
            isBase = false;
            return false;
        }

        /// <summary>Station prefab to its catalog row (role, lift type, stub tip, bullwheel) for the stats system and the alignment aid.
        /// False for a non-station prefab. Main thread; event-time callers (a loop over every building fetches
        /// SkiPrefabCatalogSystem.StationDefs once instead).</summary>
        internal bool TryGetStationDef(Entity stationPrefab, out StationDef def)
        {
            return m_PrefabCatalog.TryGetStation(stationPrefab, out def) && def != null;
        }

        internal int ParkQueueCount => m_ParkQueue.Count;
        internal Entity ParkQueueAt(int i) => m_ParkQueue[i];

        /// <summary>Called by LiftParkSystem (UIUpdate, same frame) after it created the events.</summary>
        /// <summary>A build within the coalesce window (TrainPitchSystem: the towers past their train's limits changed after the census
        /// that fed it, so the rows re-measure them).</summary>
        internal void RequestBuild(string why)
        {
            m_CoalescePending = true;
            m_RequestedWhy = why;
        }

        internal void OnParkFlushed(int created)
        {
            if (created > 0)
            {
                // Accumulate: a second park before the first check is checked with it.
                m_ParkCheckEdges.AddRange(m_ParkQueue);
                m_ParkFlushedFrame = m_Frame;
                m_ParkCheckFrame = m_Frame + ParkCheckFrames;
            }
            else if (m_ParkCheckFrame < 0)
            {
                m_ParkRiders = m_ParkChairs = 0;   // every queued section was deleted meanwhile and no check is pending
            }
            m_ParkQueue.Clear();
            m_ParkQueuedFrame = -1;
        }


        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_ChairPin = World.GetOrCreateSystemManaged<ChairPinSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            m_TowerDiag = World.GetOrCreateSystemManaged<LiftTowerDiagSystem>();
            m_TowerFit = World.GetOrCreateSystemManaged<LiftTowerFitSystem>();
            m_TowerNumbers = World.GetOrCreateSystemManaged<TowerNumberTileSystem>();
            m_TrainPitch = World.GetOrCreateSystemManaged<TrainPitchSystem>();
            m_Clearance = World.GetOrCreateSystemManaged<LiftClearanceSystem>();
            // The guard's clock: the simulation frame (SimulationSystem.cs:68)
            m_Simulation = World.GetOrCreateSystemManaged<Game.Simulation.SimulationSystem>();

            m_EdgeProbe = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_NodeProbe = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_StationProbe = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Transform>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            // Static keeps moving objects (vehicles, cims) out: they despawn with Deleted every frame.
            m_DeletedObjectProbe = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.Static>(), ComponentType.ReadOnly<Owner>(),
                    ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Deleted>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_DeletedLaneProbe = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Net.Lane>(), ComponentType.ReadOnly<Game.Net.UtilityLane>(),
                    ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Deleted>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_EdgeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Net.Edge>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Curve>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            m_StationQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            Mod.Log.Info($"{nameof(LiftCensusSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_LastError = null;
            m_Census.Reset();
            m_Frame = 0;
            m_LastBuildFrame = 0;
            m_CoalescePending = false;
            m_InitialBuild = false;
            m_StationTransforms.Clear();
            LastMovedStation = Entity.Null;
            LastMovedFrame = 0;
            LastMovedRealtime = float.NegativeInfinity;
            m_ParkQueue.Clear();
            m_Parked.Clear();
            m_ParkCheckEdges.Clear();
            m_ParkQueuedFrame = m_ParkFlushedFrame = m_ParkCheckFrame = -1;
            m_ParkRiders = m_ParkChairs = 0;
            m_ParkEmitterWarned = false;
            m_RowCounts.Clear();
            m_LastNotes = null;
            LastEvent = "-";
            LastBuildMs = MaxBuildMs = 0f;
            Builds = 0;
            m_CostLines = 0;
            ClearSnapshot();
            // Nothing of the previous city's power reads carries over (the fold was flushed in OnGamePreload)
            m_ServeCounts.Clear();
            m_LastPower.Clear();
            // The guard's state is transient: nothing carries over a load
            m_BackSince.Clear();
            m_StreakReadFrame.Clear();
            m_GuardRecuts.Clear();
            m_NoConsumerWarned.Clear();
            m_ServeLogged = false;
            m_PowerLog.Reset();
            if (mode != GameMode.Game)
                return;

            // The prefab catalog resolves per load (at the first catalog call after this load's preload); every use reads its views
            m_Armed = true;
            m_InitialBuild = true;   // the baseline build (and the ARM line) run at the first update
        }

        /// <summary>A load (or leaving to the menu) writes the folded "Lift [power]" changes of the city being left. Never throws:
        /// GameSystemBase.GamePreload DISABLES a system whose OnGamePreload throws (GameSystemBase.cs:85-96).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                if (m_PowerLog.Count > 0)
                    WritePowerSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"LiftCensus: folded power lines could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        /// <summary>Quit to desktop raises no onGamePreload, but the world's systems are destroyed while the logger still lives (LogWindow.cs
        /// engine notes), so the folded power lines are written here. Teardown must never throw: a failed write is dropped.</summary>
        protected override void OnDestroy()
        {
            try
            {
                if (m_PowerLog.Count > 0)
                    WritePowerSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            try
            {
                // The end of a "Lift [power]" burst is written at most LogWindowS after the last line, whoever read the power. Per frame: one int
                // compare while nothing is folded.
                if (m_PowerLog.Count > 0)
                {
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (m_PowerLog.TrailingDue(now))
                        WritePowerSummary(now);
                }
                if (!m_Armed || m_Disabled)
                    return;
                m_Frame++;
                // No lift line of the catalog resolved this load; the line names SkiCatalog.Lifts[*].LinePrefab
                if (m_PrefabCatalog.LiftLinePrefabSet.Count == 0)
                {
                    if (m_InitialBuild)
                    {
                        m_InitialBuild = false;
                        // The expected line(s) named; the pending ones listed (never `!!!` alone)
                        Mod.Log.Error($"!!! LiftCensus [arm]: net prefab '{LineNames()}' not found — the lift census, " +
                                      "section logs, park and the hub's lift rows are inactive this load (pending, not " +
                                      $"imported yet: {PendingLineNames()})");
                    }
                    return;
                }

                ParkFollowUps();

                bool immediate = false, coalesced = false;
                string why = null;
                ProbeNet(m_EdgeProbe, isEdge: true, ref immediate, ref coalesced, ref why);
                ProbeNet(m_NodeProbe, isEdge: false, ref immediate, ref coalesced, ref why);
                ProbeStations(ref immediate, ref coalesced, ref why);
                ProbeDeletedObjects(ref immediate, ref why);
                ProbeDeletedLanes(ref immediate, ref why);

                if (m_InitialBuild)
                {
                    m_InitialBuild = false;
                    BuildCensus("load", baseline: true);
                }
                else if (immediate)
                {
                    BuildCensus(why ?? "lift edited", baseline: false);
                }
                else
                {
                    if (coalesced)
                        m_CoalescePending = true;
                    if (m_CoalescePending && m_Frame - m_LastBuildFrame >= CoalesceFrames)
                        BuildCensus(why ?? m_RequestedWhy ?? "coalesced edits", baseline: false);
                    else if (m_Frame - m_LastBuildFrame >= WatchdogFrames && m_Census.Rows.Count > 0)
                        BuildCensus("watchdog", baseline: false);
                }
            }
            catch (Exception e)
            {
                m_Disabled = true;
                m_LastError = e.ToString();
                m_ParkQueue.Clear();
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(LiftCensusSystem)} EXCEPTION — lift census, section logs and park " +
                                  $"disabled until the next game load (chairs keep moving: ChairPinSystem is unaffected). " +
                                  $"Please report this stack:\n{e}");
                }
            }
            finally
            {
                ClearSnapshot();
            }
        }


        private void ProbeNet(EntityQuery probe, bool isEdge, ref bool immediate, ref bool coalesced, ref string why)
        {
            if (probe.IsEmptyIgnoreFilter)
                return;
            // The catalog's lift lines and marker, fetched ONCE before the chunk loop
            HashSet<Entity> lines = m_PrefabCatalog.LiftLinePrefabSet;
            Entity marker = m_PrefabCatalog.MarkerEntity;
            var chunks = probe.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var createdHandle = GetComponentTypeHandle<Created>(isReadOnly: true);
                var deletedHandle = GetComponentTypeHandle<Deleted>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    bool created = chunks[c].Has(ref createdHandle);
                    bool deleted = chunks[c].Has(ref deletedHandle);
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity prefab = prefabs[i].m_Prefab;
                        if (lines.Contains(prefab))
                        {
                            if (created || deleted)
                            {
                                immediate = true;
                                if (why == null)
                                    why = deleted ? "section deleted" : "section drawn";
                                if (deleted && isEdge)
                                    SnapshotDeletedEdge(entities[i]);
                            }
                            else
                            {
                                coalesced = true;
                            }
                        }
                        else if (isEdge && deleted && prefab == marker && marker != Entity.Null &&
                                 EntityManager.TryGetComponent<Game.Net.Edge>(entities[i], out var stub))
                        {
                            m_DeletedMarkerNodes.Add(stub.m_Start);
                            m_DeletedMarkerNodes.Add(stub.m_End);
                        }
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
        }

        private void SnapshotDeletedEdge(Entity edge)
        {
            var d = new DeletedEdge { Edge = edge, PrevRow = -1 };
            if (EntityManager.TryGetComponent<Game.Net.Edge>(edge, out var topo))
            {
                d.Start = topo.m_Start;
                d.End = topo.m_End;
                d.StartGone = !EntityManager.Exists(topo.m_Start) || EntityManager.HasComponent<Deleted>(topo.m_Start);
                d.EndGone = !EntityManager.Exists(topo.m_End) || EntityManager.HasComponent<Deleted>(topo.m_End);
                if (EntityManager.TryGetComponent<Game.Net.Node>(topo.m_Start, out var a)) d.StartPos = a.m_Position;
                if (EntityManager.TryGetComponent<Game.Net.Node>(topo.m_End, out var b)) d.EndPos = b.m_Position;
            }
            if (EntityManager.TryGetComponent<Curve>(edge, out var curve))
                d.LengthM = curve.m_Length;
            // Its row in the census as it stands BEFORE this frame's build (= PreviousRows after it).
            if (m_Census.TryGetRowOfEdge(Ref(edge), out int row))
                d.PrevRow = row;
            m_DeletedEdges.Add(d);
        }

        private void ProbeStations(ref bool immediate, ref bool coalesced, ref string why)
        {
            if (m_StationProbe.IsEmptyIgnoreFilter)
                return;
            // The catalog's station set, fetched ONCE before the chunk loop (every updated building passes through it)
            HashSet<Entity> stationPrefabs = m_PrefabCatalog.StationPrefabSet;
            if (stationPrefabs.Count == 0)
                return;
            var chunks = m_StationProbe.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var transformHandle = GetComponentTypeHandle<Transform>(isReadOnly: true);
                var createdHandle = GetComponentTypeHandle<Created>(isReadOnly: true);
                var deletedHandle = GetComponentTypeHandle<Deleted>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    bool created = chunks[c].Has(ref createdHandle);
                    bool deleted = chunks[c].Has(ref deletedHandle);
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    var transforms = chunks[c].GetNativeArray(ref transformHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!stationPrefabs.Contains(prefabs[i].m_Prefab))
                            continue;
                        Entity station = entities[i];
                        if (deleted)
                        {
                            m_StationTransforms.Remove(station);
                            immediate = true;
                            if (why == null) why = "station removed";
                            continue;
                        }
                        Transform t = transforms[i];
                        if (created)
                        {
                            m_StationTransforms[station] = t;
                            immediate = true;
                            if (why == null) why = "station placed";
                            continue;
                        }
                        if (!m_StationTransforms.TryGetValue(station, out Transform last))
                        {
                            m_StationTransforms[station] = t;   // first sight this load: not a move
                            continue;
                        }
                        if (last.m_Position.Equals(t.m_Position) && last.m_Rotation.Equals(t.m_Rotation))
                            continue;   // Updated without a move (e.g. the reconnect poke's echo)
                        m_StationTransforms[station] = t;
                        LastMovedStation = station;
                        LastMovedFrame = (uint)UnityEngine.Time.frameCount;
                        LastMovedRealtime = UnityEngine.Time.realtimeSinceStartup;
                        coalesced = true;
                        if (why == null) why = "station moved";
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
        }

        private void ProbeDeletedObjects(ref bool immediate, ref string why)
        {
            if (m_DeletedObjectProbe.IsEmptyIgnoreFilter)
                return;
            // The catalog's lift lines and carriers, fetched ONCE before the chunk loop
            HashSet<Entity> lines = m_PrefabCatalog.LiftLinePrefabSet;
            IReadOnlyDictionary<Entity, float> carriers = m_PrefabCatalog.CarrierGripTops;
            var chunks = m_DeletedObjectProbe.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var ownerHandle = GetComponentTypeHandle<Owner>(isReadOnly: true);
                var prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var owners = chunks[c].GetNativeArray(ref ownerHandle);
                    var prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    for (int i = 0; i < owners.Length; i++)
                    {
                        Entity owner = owners[i].m_Owner;
                        if (owner == Entity.Null || !EntityManager.Exists(owner) ||
                            !EntityManager.TryGetComponent<PrefabRef>(owner, out var ownerPrefab) ||
                            !lines.Contains(ownerPrefab.m_Prefab))
                            continue;   // not a chair / tower of our lift line
                        bool isChair = carriers.ContainsKey(prefabs[i].m_Prefab);
                        if (EntityManager.HasComponent<Deleted>(owner))
                        {
                            // Went WITH its section / node: the snapshot of the section-deleted line.
                            var map = isChair ? m_GoneChairsByOwner : m_GoneTowersByOwner;
                            map.TryGetValue(owner, out int n);
                            map[owner] = n + 1;
                            continue;
                        }
                        if (EntityManager.HasComponent<Created>(owner) || EntityManager.HasComponent<Updated>(owner))
                            continue;   // vanilla re-seat / regeneration of a section being edited
                        // Owner alive and untouched this frame: removed by another tool.
                        if (isChair)
                        {
                            m_RemovedChairs.TryGetValue(owner, out int n);
                            m_RemovedChairs[owner] = n + 1;
                        }
                        else
                        {
                            m_RemovedTowers.Add(owner);
                        }
                        immediate = true;
                        if (why == null) why = "removed by another tool";
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
        }

        private void ProbeDeletedLanes(ref bool immediate, ref string why)
        {
            if (m_DeletedLaneProbe.IsEmptyIgnoreFilter)
                return;
            // The catalog's lift lines, fetched ONCE
            HashSet<Entity> lines = m_PrefabCatalog.LiftLinePrefabSet;
            var chunks = m_DeletedLaneProbe.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var ownerHandle = GetComponentTypeHandle<Owner>(isReadOnly: true);
                var prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var owners = chunks[c].GetNativeArray(ref ownerHandle);
                    var prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    for (int i = 0; i < owners.Length; i++)
                    {
                        Entity owner = owners[i].m_Owner;
                        if (owner == Entity.Null || !EntityManager.Exists(owner) ||
                            EntityManager.HasComponent<Deleted>(owner) ||
                            EntityManager.HasComponent<Created>(owner) || EntityManager.HasComponent<Updated>(owner) ||
                            !EntityManager.HasComponent<Game.Net.Edge>(owner) ||
                            !EntityManager.TryGetComponent<PrefabRef>(owner, out var ownerPrefab) ||
                            !lines.Contains(ownerPrefab.m_Prefab))
                            continue;
                        // The cable = the HV lane (same structural test as ChairPinSystem's pin).
                        if (!EntityManager.TryGetComponent<UtilityLaneData>(prefabs[i].m_Prefab, out var utility) ||
                            (utility.m_UtilityTypes & UtilityTypes.HighVoltageLine) == 0)
                            continue;
                        if (!m_RemovedLanes.Contains(owner))
                            m_RemovedLanes.Add(owner);
                        immediate = true;
                        if (why == null) why = "removed by another tool";
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
        }

        private void ClearSnapshot()
        {
            m_DeletedEdges.Clear();
            m_GoneChairsByOwner.Clear();
            m_GoneTowersByOwner.Clear();
            m_DeletedMarkerNodes.Clear();
            m_RemovedChairs.Clear();
            m_RemovedTowers.Clear();
            m_RemovedLanes.Clear();
        }


        private void BuildCensus(string why, bool baseline)
        {
            long t0 = Stopwatch.GetTimestamp();
            Harvest();
            long t1 = Stopwatch.GetTimestamp();
            m_Census.Build(m_Edges, m_Nodes, m_Stations, m_Events);
            m_LastBuildFrame = m_Frame;
            m_CoalescePending = false;
            m_RequestedWhy = null;
            Builds++;
            LastBuildMs = (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            bool newMax = LastBuildMs > MaxBuildMs;
            if (newMax)
                MaxBuildMs = LastBuildMs;
            if (m_CostLines < CostLinesMax && (Builds == 1 || (newMax && LastBuildMs > CostReportMs)))
            {
                m_CostLines++;
                double harvestMs = (t1 - t0) * 1000.0 / Stopwatch.Frequency;
                Mod.Log.Info($"Census [cost] build {Builds} ({why}): {LastBuildMs:F2} ms, of it harvest {harvestMs:F2} ms over " +
                             $"{m_EdgeQuery.CalculateEntityCount()} city net edge(s) for {m_Edges.Count} lift edge(s); max this load " +
                             $"{MaxBuildMs:F2} ms" + (m_CostLines == CostLinesMax ? " (last [cost] line this load)" : ""));
            }

            ComputeRowSignatures();
            if (baseline)
            {
                LogArm();
            }
            else
            {
                AppendRemovedEvents();
                LogEvents(why);
                LogUnexplainedDrops(why);
                if (m_Census.LastBuildChangedTopology)
                    m_ChairPin.RequestImmediateRebuild("lift topology");
            }
            PruneParked();
            StoreRowCounts();
            ResortPresence.SetLifts(m_Census.Rows.Count);   // the resort-present bit (ResortPresence header): lifts of ours
            // Hand LiftTowerDiagSystem (read-only diagnostic) this build's line edges; it queues lines whose signature is new and prints them
            // after the census has been quiet. No ECS read here.
            m_TowerDiag?.OnCensusBuilt(m_Edges);
            // The same edges to LiftTowerFitSystem; it queues new signatures and fits them once the census is quiet
            m_TowerFit?.OnCensusBuilt(m_Edges);
            // The same edges to TowerNumberTileSystem; it renumbers and reconciles the tower number tiles once quiet
            m_TowerNumbers?.OnCensusBuilt(m_Edges);
            // The same edges to TrainPitchSystem; it pitches the rigged towers' trains to their rope lanes
            m_TrainPitch?.OnCensusBuilt(m_Edges);
            // The same edges to LiftClearanceSystem; it measures how high the carriers pass over the ground
            m_Clearance?.OnCensusBuilt(m_Edges);
        }

        private void Harvest()
        {
            m_Edges.Clear();
            m_Nodes.Clear();
            m_Stations.Clear();
            m_StationEntities.Clear();
            m_StationIndex.Clear();
            m_NodeSeen.Clear();

            // 1. Stations. The catalog's StationDefs view, fetched ONCE before the chunk loop over every live building; base / top = the row's
            //    Role.
            IReadOnlyDictionary<Entity, StationDef> stationDefs = m_PrefabCatalog.StationDefs;
            // Line prefab to lift type view
            IReadOnlyDictionary<Entity, LiftTypeDef> lineTypes = m_PrefabCatalog.LiftLineTypes;
            var chunks = m_StationQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var transformHandle = GetComponentTypeHandle<Transform>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    var transforms = chunks[c].GetNativeArray(ref transformHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!stationDefs.TryGetValue(prefabs[i].m_Prefab, out StationDef stationDef))
                            continue;
                        bool isBase = stationDef.Role == StationRole.Base;
                        Transform t = transforms[i];
                        // Seed the move detector, so the FIRST nudge after a load already counts as a move.
                        if (!m_StationTransforms.ContainsKey(entities[i]))
                            m_StationTransforms[entities[i]] = t;
                        m_StationIndex[entities[i]] = m_Stations.Count;
                        m_StationEntities.Add(entities[i]);
                        m_Stations.Add(new LiftCensus.Station
                        {
                            Id = Ref(entities[i]),
                            IsBase = isBase,
                            Position = t.m_Position,
                            // The row's own stub tip (-16 on DQ / gondola stations) and type, on its own line axis (StubTipLocalX +4.00 on the tram base's
                            // 6 x 3 lot)
                            StubTip = StationAlign.StubTip(t.m_Position, t.m_Rotation, stationDef.StubTipLocalZ,
                                                           stationDef.StubTipLocalX),
                            Axis = StationAlign.Axis(t.m_Rotation),
                            Type = TypeByte(stationDef.LiftTypeIndex),
                        });
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }

            // 2. Line edges (+ their nodes).
            chunks = m_EdgeQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var edgeHandle = GetComponentTypeHandle<Game.Net.Edge>(isReadOnly: true);
                var curveHandle = GetComponentTypeHandle<Curve>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    bool any = false;
                    for (int i = 0; i < prefabs.Length && !any; i++)
                        any = lineTypes.ContainsKey(prefabs[i].m_Prefab);
                    if (!any)
                        continue;
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var edges = chunks[c].GetNativeArray(ref edgeHandle);
                    var curves = chunks[c].GetNativeArray(ref curveHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!lineTypes.TryGetValue(prefabs[i].m_Prefab, out LiftTypeDef lineDef) || lineDef == null)
                            continue;
                        Game.Net.Edge topo = edges[i];
                        Curve curve = curves[i];
                        float3 a = EntityManager.TryGetComponent<Game.Net.Node>(topo.m_Start, out var na) ? na.m_Position : curve.m_Bezier.a;
                        float3 b = EntityManager.TryGetComponent<Game.Net.Node>(topo.m_End, out var nb) ? nb.m_Position : curve.m_Bezier.d;
                        LiftClearanceSystem.Low lowSpan = default;
                        bool low = m_Clearance != null && m_Clearance.LowEdges.TryGetValue(entities[i], out lowSpan);
                        m_Edges.Add(new LiftCensus.Edge
                        {
                            Id = Ref(entities[i]),
                            Start = Ref(topo.m_Start),
                            End = Ref(topo.m_End),
                            StartPos = a,
                            EndPos = b,
                            LengthM = curve.m_Length,
                            Chairs = CountChairs(entities[i]),
                            Type = TypeByte(lineDef.Index),
                            Low = low,
                            LowClearM = lowSpan.ClearM,
                            LowAt = lowSpan.At,
                        });
                        AddNode(topo.m_Start, a);
                        AddNode(topo.m_End, b);
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
        }

        private void AddNode(Entity node, float3 position)
        {
            if (node == Entity.Null || !m_NodeSeen.Add(node))
                return;
            m_Nodes.Add(new LiftCensus.Node
            {
                Id = Ref(node),
                Station = StationOfNode(node),
                HasTower = HasTower(node),
                Position = position,
                TrainOverDeg = m_TrainPitch != null && m_TrainPitch.TrainOver.TryGetValue(node, out float over) ? over : 0f,
            });
        }

        /// <summary>The IsStationConnectionNode rule (ChairPinSystem.cs, v1.2.2) extended to say WHICH
        /// station: stations-first, the line reuses the station-OWNED stub node (its Owner, or its
        /// owner's owner, is a station); line-first, the stub course reuses the line's UNOWNED node and
        /// the station's owned stub edge sits in the node's ConnectedEdge buffer.</summary>
        private int StationOfNode(Entity node)
        {
            if (EntityManager.TryGetComponent<Owner>(node, out var owner))
            {
                if (m_StationIndex.TryGetValue(owner.m_Owner, out int s))
                    return s;
                if (EntityManager.TryGetComponent<Owner>(owner.m_Owner, out var owner2) &&
                    m_StationIndex.TryGetValue(owner2.m_Owner, out s))
                    return s;
            }
            if (EntityManager.TryGetBuffer(node, isReadOnly: true, out DynamicBuffer<ConnectedEdge> connected))
            {
                for (int i = 0; i < connected.Length; i++)
                {
                    Entity edge = connected[i].m_Edge;
                    if (edge != Entity.Null && EntityManager.TryGetComponent<Owner>(edge, out var edgeOwner) &&
                        m_StationIndex.TryGetValue(edgeOwner.m_Owner, out int s))
                        return s;
                }
            }
            return -1;
        }

        /// <summary>Tower = any live non-chair entry of the node's SubObject buffer (towers are node
        /// sub-objects, SkiChairliftHV.Prefab:35-94, m_Placement 0).</summary>
        private bool HasTower(Entity node)
        {
            if (!EntityManager.TryGetBuffer(node, isReadOnly: true, out DynamicBuffer<Game.Objects.SubObject> subs))
                return false;
            // The carrier view, once
            IReadOnlyDictionary<Entity, float> carriers = m_PrefabCatalog.CarrierGripTops;
            for (int i = 0; i < subs.Length; i++)
            {
                Entity sub = subs[i].m_SubObject;
                if (!EntityManager.HasComponent<Deleted>(sub) &&
                    EntityManager.TryGetComponent<PrefabRef>(sub, out var prefab) &&
                    !carriers.ContainsKey(prefab.m_Prefab))
                    return true;
            }
            return false;
        }

        /// <summary>Chairs = chair-prefab entries of the edge's SubObject buffer (chairs ARE in it: SubObjectReferencesSystem.cs:122-138).</summary>
        private int CountChairs(Entity edge)
        {
            if (!EntityManager.TryGetBuffer(edge, isReadOnly: true, out DynamicBuffer<Game.Objects.SubObject> subs))
                return 0;
            // The carrier view, once
            IReadOnlyDictionary<Entity, float> carriers = m_PrefabCatalog.CarrierGripTops;
            int n = 0;
            for (int i = 0; i < subs.Length; i++)
            {
                Entity sub = subs[i].m_SubObject;
                if (!EntityManager.HasComponent<Deleted>(sub) &&
                    EntityManager.TryGetComponent<PrefabRef>(sub, out var prefab) &&
                    carriers.ContainsKey(prefab.m_Prefab))
                    n++;
            }
            return n;
        }


        /// <summary>At most this many "removed by another tool" lines per build; the rest are summed in
        /// one line (a mass removal must not flood the log).</summary>
        internal const int RemovedLinesPerBuild = 6;

        private void AppendRemovedEvents()
        {
            m_RowsWithRemoved.Clear();
            int lines = 0, suppressed = 0;
            foreach (var kv in m_RemovedChairs)
            {
                int row = RowOfEdge(kv.Key);
                m_RowsWithRemoved.Add(row);
                m_Events.Add(new LiftCensus.Event { Kind = LiftCensus.EventKind.Removed, Row = row, PrevRow = -1, Count = kv.Value });
                if (lines++ >= RemovedLinesPerBuild) { suppressed += kv.Value; continue; }
                string name = row >= 0 ? Describe(m_Census.Rows[row]) : "an unknown lift";
                LastEvent = "removed by another tool: chairs at update " + m_Frame;
                Mod.Log.Info($"Lift [removed by another tool]: {kv.Value} chair(s) of section {Fmt(kv.Key)} on {name} — " +
                             "an honest gap until that section is next edited");
            }
            for (int i = 0; i < m_RemovedTowers.Count; i++)
            {
                Entity node = m_RemovedTowers[i];
                int row = RowOfNode(node);
                m_RowsWithRemoved.Add(row);
                m_Events.Add(new LiftCensus.Event { Kind = LiftCensus.EventKind.Removed, Row = row, PrevRow = -1, Count = 1 });
                if (lines++ >= RemovedLinesPerBuild) { suppressed++; continue; }
                string name = row >= 0 ? Describe(m_Census.Rows[row]) : "an unknown lift";
                LastEvent = "removed by another tool: tower at update " + m_Frame;
                Mod.Log.Info($"Lift [removed by another tool]: 1 tower at node {Fmt(node)} on {name} — " +
                             "redraw a section next to it to restore it");
            }
            for (int i = 0; i < m_RemovedLanes.Count; i++)
            {
                Entity edge = m_RemovedLanes[i];
                int row = RowOfEdge(edge);
                m_RowsWithRemoved.Add(row);
                m_Events.Add(new LiftCensus.Event { Kind = LiftCensus.EventKind.Removed, Row = row, PrevRow = -1, Count = 1 });
                if (lines++ >= RemovedLinesPerBuild) { suppressed++; continue; }
                string name = row >= 0 ? Describe(m_Census.Rows[row]) : "an unknown lift";
                LastEvent = "removed by another tool: cable at update " + m_Frame;
                Mod.Log.Info($"Lift [removed by another tool]: the cable of section {Fmt(edge)} on {name} — redraw that section");
            }
            if (suppressed > 0)
                Mod.Log.Info($"Lift [removed by another tool]: … and {suppressed} more element(s) in the same frame " +
                             $"(lines capped at {RemovedLinesPerBuild} per census)");
        }

        private void LogEvents(string why)
        {
            var rows = m_Census.Rows;
            var prev = m_Census.PreviousRows;
            for (int e = 0; e < m_Events.Count; e++)
            {
                LiftCensus.Event ev = m_Events[e];
                switch (ev.Kind)
                {
                    case LiftCensus.EventKind.SectionsDeleted:
                    {
                        LiftCensus.Row named = ev.Row >= 0 ? rows[ev.Row] : ev.PrevRow >= 0 ? prev[ev.PrevRow] : default;
                        LogSectionsDeleted(ev, named);
                        break;
                    }
                    case LiftCensus.EventKind.Opened:
                        OnOpened(ev, rows[ev.Row]);
                        break;
                    case LiftCensus.EventKind.Closed:
                    {
                        LiftCensus.Row row = rows[ev.Row];
                        UnparkRow(ev.Row);
                        LastEvent = "line closed " + Fmt(row.Id) + " at update " + m_Frame;
                        Mod.Log.Info($"Lift [line closed]: {Describe(row)} complete{(ev.Count > 0 ? " again" : string.Empty)} — " +
                                     $"{row.LengthM:F0} m, rise {row.RiseM:F0} m, {row.Sections} section(s), {row.Chairs} chair(s)");
                        break;
                    }
                    case LiftCensus.EventKind.NewLine:
                    {
                        LiftCensus.Row row = rows[ev.Row];
                        LastEvent = "new line " + Fmt(row.Id) + " at update " + m_Frame;
                        string where = !row.Base.IsNull ? $"from base station {Fmt(row.Base)} — not at a top station yet"
                            : !row.Top.IsNull ? $"from top station {Fmt(row.Top)} — not at a base station yet"
                            : $"first section {Fmt(row.Id)} — not connected to a station yet";
                        Mod.Log.Info($"Lift [new line]: {where} ({row.Sections} section(s), {row.LengthM:F0} m)");
                        break;
                    }
                }
            }
            LogNotesIfChanged();
        }

        /// <summary>Build notes (odd topologies) are logged when the set changes, not on every build
        /// (a watchdog build of an unchanged odd lift stays silent).</summary>
        private void LogNotesIfChanged()
        {
            // One INFO line per load per node where two lift types share a node and are counted apart
            for (int n = 0; n < m_Census.NewSplits.Count; n++)
                Mod.Log.Info("[census] " + m_Census.NewSplits[n]);
            m_Census.NewSplits.Clear();   // announced: the DUMP path calls this again without a build
            string sig = m_Census.Notes.Count == 0 ? null : string.Join("\n", m_Census.Notes);
            if (sig == m_LastNotes)
                return;
            m_LastNotes = sig;
            for (int n = 0; n < m_Census.Notes.Count; n++)
                Mod.Log.Info("LiftCensus [note]: " + m_Census.Notes[n]);
        }

        private string m_LastNotes;

        private void LogSectionsDeleted(LiftCensus.Event ev, LiftCensus.Row named)
        {
            int chairs = 0, towers = 0, matched = 0;
            bool stub = false;
            var sb = new StringBuilder(96);
            m_Scratch.Clear();   // gone nodes already counted
            for (int i = 0; i < m_DeletedEdges.Count; i++)
            {
                DeletedEdge d = m_DeletedEdges[i];
                if (d.PrevRow != ev.PrevRow)
                    continue;
                matched++;
                m_GoneChairsByOwner.TryGetValue(d.Edge, out int c);
                chairs += c;
                towers += GoneTowersAt(d.Start, d.StartGone) + GoneTowersAt(d.End, d.EndGone);
                stub |= m_DeletedMarkerNodes.Contains(d.Start) || m_DeletedMarkerNodes.Contains(d.End);
                AppendDeadEnd(sb, d.Start, d.StartGone, d.StartPos);
                AppendDeadEnd(sb, d.End, d.EndGone, d.EndPos);
            }
            m_Scratch.Clear();
            string ends = sb.Length > 0 ? "; dead ends at " + sb : string.Empty;
            string went = matched > 0
                ? $"; {chairs} chair(s) and {towers} tower(s) went with it" + (stub ? " and the station's connection stub" : string.Empty)
                : "; chairs and towers not counted (the deletion was not seen in this frame)";
            LastEvent = "section deleted " + Fmt(named.Id) + " at update " + m_Frame;
            Mod.Log.Info($"Lift [section deleted]: {Describe(named)} lost {ev.Count} section(s) ({ev.LengthM:F0} m{ends}){went}");
        }

        private int GoneTowersAt(Entity node, bool gone)
        {
            if (!gone || node == Entity.Null || m_Scratch.Contains(node))
                return 0;
            m_Scratch.Add(node);
            return m_GoneTowersByOwner.TryGetValue(node, out int n) ? n : 0;
        }

        private void AppendDeadEnd(StringBuilder sb, Entity node, bool gone, float3 pos)
        {
            if (gone || node == Entity.Null || StationOfNode(node) >= 0)
                return;   // a deleted node is no dead end; a station connection node is not one either
            if (sb.Length > 0)
                sb.Append(" and ");
            sb.Append('(').Append(pos.x.ToString("F0")).Append(',').Append(pos.z.ToString("F0")).Append(')');
        }

        private void OnOpened(LiftCensus.Event ev, LiftCensus.Row row)
        {
            int sections = 0, chairs = 0, riders = 0;
            bool moving = ChairPinSystem.MovingChairsOn;
            if (moving)
            {
                for (int i = 0; i < m_Edges.Count; i++)
                {
                    if (!m_Census.TryGetRowOfEdge(m_Edges[i].Id, out int r) || r != ev.Row)
                        continue;
                    Entity edge = Ent(m_Edges[i].Id);
                    if (!m_Parked.Add(edge))
                        continue;   // once per open episode
                    m_ParkQueue.Add(edge);
                    sections++;
                    chairs += m_Edges[i].Chairs;
                    riders += CountRiders(edge);
                }
                if (sections > 0)
                {
                    if (m_ParkQueuedFrame < 0)
                        m_ParkQueuedFrame = m_Frame;
                    m_ParkRiders += riders;
                    m_ParkChairs += chairs;
                }
            }
            // A complete lift that lost a STATION keeps its key and opens too (LiftCensus rule b); the line says which station went instead of
            // "a gap"
            bool baseGone = !row.Base.IsNull && !StationInBuild(row.Base);
            bool topGone = !row.Top.IsNull && !StationInBuild(row.Top);
            string what = baseGone && topGone ? "both stations removed"
                : baseGone ? $"base station {Fmt(row.Base)} removed"
                : topGone ? $"top station {Fmt(row.Top)} removed"
                : $"{ev.Count} gap(s)";
            LastEvent = (baseGone || topGone ? "station removed " : "line opened ") + Fmt(row.Id) + " at update " + m_Frame;
            string until = baseGone || topGone ? "a station is placed and connected again" : "the gap is redrawn";
            // "parked" counts every chair on the surviving sections, the re-seat check counts the chairs that were riding; print both, they
            // differ by design
            string tail = moving
                ? $"{chairs} chair(s) parked ({riders} were riding) on {sections} section(s); chairs stop until {until}"
                : "moving chairs are off — chairs stay where they are";
            Mod.Log.Info($"Lift [{(baseGone || topGone ? "station removed" : "line opened")}]: {Describe(row)} — {what}; {tail}");
        }

        /// <summary>Chairs of an edge that carry InterpolatedTransform (they were riding a loop).</summary>
        private int CountRiders(Entity edge)
        {
            if (!EntityManager.TryGetBuffer(edge, isReadOnly: true, out DynamicBuffer<Game.Objects.SubObject> subs))
                return 0;
            // The carrier view, once
            IReadOnlyDictionary<Entity, float> carriers = m_PrefabCatalog.CarrierGripTops;
            int n = 0;
            for (int i = 0; i < subs.Length; i++)
            {
                Entity sub = subs[i].m_SubObject;
                if (EntityManager.HasComponent<InterpolatedTransform>(sub) &&
                    EntityManager.TryGetComponent<PrefabRef>(sub, out var prefab) &&
                    carriers.ContainsKey(prefab.m_Prefab))
                    n++;
            }
            return n;
        }

        private void UnparkRow(int row)
        {
            for (int i = 0; i < m_Edges.Count; i++)
            {
                if (m_Census.TryGetRowOfEdge(m_Edges[i].Id, out int r) && r == row)
                    m_Parked.Remove(Ent(m_Edges[i].Id));
            }
        }

        /// <summary>Drops parked edges that left the census or whose row is no longer Open.</summary>
        private void PruneParked()
        {
            if (m_Parked.Count == 0)
                return;
            m_Scratch.Clear();
            foreach (Entity edge in m_Parked)
            {
                if (!m_Census.TryGetRowOfEdge(Ref(edge), out int r) || m_Census.Rows[r].State != LiftCensus.State.Open)
                    m_Scratch.Add(edge);
            }
            for (int i = 0; i < m_Scratch.Count; i++)
                m_Parked.Remove(m_Scratch[i]);
            m_Scratch.Clear();
        }

        private void ParkFollowUps()
        {
            // The emitter (LiftParkSystem, UIUpdate) did not take the queue: not registered / disabled.
            if (m_ParkQueue.Count > 0 && m_ParkQueuedFrame >= 0 && m_Frame - m_ParkQueuedFrame >= ParkEmitterTimeoutFrames)
            {
                if (!m_ParkEmitterWarned)
                {
                    m_ParkEmitterWarned = true;
                    Mod.Log.Warn($"Lift [park]: {m_ParkQueue.Count} section(s) waited {m_Frame - m_ParkQueuedFrame} frames for " +
                                 $"{nameof(LiftParkSystem)} (UIUpdate) — it is not running (not registered, or disabled by an " +
                                 "exception): chairs of opened lifts stay where the mover left them (logged once per load)");
                }
                m_ParkQueue.Clear();
                m_ParkQueuedFrame = -1;
                m_ParkRiders = m_ParkChairs = 0;
            }

            // The frame after the events were created: SubObjectSystem (2B) has re-seated the chairs;
            // pin them onto their own lanes now instead of after the 30-frame cooldown.
            if (m_ParkFlushedFrame >= 0 && m_Frame == m_ParkFlushedFrame + 1 && ChairPinSystem.MovingChairsOn)
                m_ChairPin.RequestImmediateRebuild("park re-seat");

            if (m_ParkCheckFrame >= 0 && m_Frame >= m_ParkCheckFrame)
            {
                m_ParkCheckFrame = -1;
                m_ParkFlushedFrame = -1;
                int still = 0, sections = 0;
                for (int i = 0; i < m_ParkCheckEdges.Count; i++)
                {
                    Entity edge = m_ParkCheckEdges[i];
                    if (!EntityManager.Exists(edge) || EntityManager.HasComponent<Deleted>(edge))
                        continue;
                    sections++;
                    still += CountRiders(edge);
                }
                // "N chairs parked (R were riding)", the same pair as the line-opened line; the check itself is about the R riders (still = riders
                // left on the parked sections)
                if (m_ParkRiders > 0)
                {
                    if (still > 0)
                        Mod.Log.Warn($"Lift [park]: {still} chair(s) on parked sections were not re-seated " +
                                     $"({m_ParkChairs} chair(s) parked ({m_ParkRiders} were riding) when the line opened; " +
                                     "the SubObjectsUpdated event was not consumed?) — they stay where the mover left them");
                    else
                        Mod.Log.Info($"Lift [park]: re-seat confirmed — {m_ParkChairs} chair(s) parked ({m_ParkRiders} were " +
                                     $"riding) on {sections} section(s); the riders are back on their own sections");
                }
                m_ParkRiders = m_ParkChairs = 0;
                m_ParkCheckEdges.Clear();
            }
        }

        // ── unexplained drops (a removal whose Deleted tag we never saw) ──
        private void ComputeRowSignatures()
        {
            m_RowSig.Clear();
            for (int r = 0; r < m_Census.Rows.Count; r++)
                m_RowSig.Add(2166136261u);
            for (int i = 0; i < m_Edges.Count; i++)
            {
                if (!m_Census.TryGetRowOfEdge(m_Edges[i].Id, out int r))
                    continue;
                unchecked
                {
                    uint h = (uint)m_Edges[i].Id.Index * 2654435761u ^ (uint)m_Edges[i].Id.Version;
                    h ^= (uint)math.round(m_Edges[i].LengthM * 10f) * 40503u;
                    m_RowSig[r] += h;   // order-independent
                }
            }
        }

        private void LogUnexplainedDrops(string why)
        {
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count; r++)
            {
                LiftCensus.Row row = rows[r];
                if (row.Sections == 0 || m_RowsWithRemoved.Contains(r))
                    continue;
                if (!m_RowCounts.TryGetValue(RowKey(row), out RowCounts last) || last.EdgeSig != m_RowSig[r])
                    continue;   // the sections changed: any count change is explained by the edit
                int chairs = last.Chairs - row.Chairs, towers = last.Towers - row.Towers;
                if (chairs <= 0 && towers <= 0)
                    continue;
                LastEvent = "unexplained drop " + Fmt(row.Id) + " at update " + m_Frame;
                Mod.Log.Info($"Lift [removed by another tool]: {Describe(row)} has {math.max(0, chairs)} chair(s) and " +
                             $"{math.max(0, towers)} tower(s) fewer than at the last census with no deletion seen " +
                             $"(found by the '{why}' census){(towers > 0 ? " — redraw a section next to a missing tower to restore it" : string.Empty)}");
            }
            m_RowsWithRemoved.Clear();
        }

        private void StoreRowCounts()
        {
            m_RowCounts.Clear();
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count && r < m_RowSig.Count; r++)
                m_RowCounts[RowKey(rows[r])] = new RowCounts { Chairs = rows[r].Chairs, Towers = rows[r].Towers, EdgeSig = m_RowSig[r] };
        }

        private static long RowKey(LiftCensus.Row row)
        {
            unchecked
            {
                return ((long)row.Id.Index << 32 | (uint)row.Id.Version) * 1000003L ^
                       ((long)row.Top.Index << 32 | (uint)row.Top.Version);
            }
        }

        private int RowOfEdge(Entity edge)
        {
            return m_Census.TryGetRowOfEdge(Ref(edge), out int r) ? r : -1;
        }

        private int RowOfNode(Entity node)
        {
            if (EntityManager.TryGetBuffer(node, isReadOnly: true, out DynamicBuffer<ConnectedEdge> connected))
            {
                for (int i = 0; i < connected.Length; i++)
                {
                    int r = RowOfEdge(connected[i].m_Edge);
                    if (r >= 0)
                        return r;
                }
            }
            return -1;
        }


        private void LogArm()
        {
            int complete = 0, open = 0, unfinished = 0, stationsOnly = 0, towersRemoved = 0;
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count; r++)
            {
                switch (rows[r].State)
                {
                    case LiftCensus.State.Complete: complete++; break;
                    case LiftCensus.State.Open: open++; break;
                    case LiftCensus.State.Unfinished: unfinished++; break;
                    default: stationsOnly++; break;
                }
                towersRemoved += rows[r].TowersRemoved;
            }
            // The line(s) per lift type, the marker, the station count with the base name(s) of the Role Base rows ("; roles from SkiCatalog"
            // appended)
            Entity marker = m_PrefabCatalog.MarkerEntity;
            // An EXPECTED line MISSING keeps the `!!!` line (pending lines are listed in the ARM line, never `!!!`). The all-missing case is
            // OnUpdate's `!!!` (the census is inactive).
            IReadOnlyList<LiftTypeDef> types = SkiCatalog.Lifts;
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i].LineExpected && m_PrefabCatalog.LineEntity(types[i]) == Entity.Null)
                    Mod.Log.Error($"!!! LiftCensus [arm]: net prefab '{types[i].LinePrefab}' not found — {types[i].Id} lifts are " +
                                  "missing from the lift census, section logs, park and the hub's lift rows this load");
            }
            int mixedRows = 0;
            for (int r = 0; r < rows.Count; r++)
                if (rows[r].Mixed)
                    mixedRows++;
            Mod.Log.Info($"LiftCensus [arm]: lines: {ChairliftsText()}, stub " +
                         $"'{SkiCatalog.MarkerPrefab}' {(marker == Entity.Null ? "NOT FOUND (no stub note in section lines)" : Fmt(marker))}, " +
                         $"{m_PrefabCatalog.StationPrefabSet.Count} station prefab(s) (base = {BaseStationNames()}; roles and types from SkiCatalog); coalesce {CoalesceFrames} frames, " +
                         $"watchdog {WatchdogFrames} frames, park check +{ParkCheckFrames} frames, park emitter timeout " +
                         $"{ParkEmitterTimeoutFrames} frames, removed-line cap {RemovedLinesPerBuild}/census, " +
                         $"stub tip z per station row ({StubTipsText()}; fixed-grip {StationAlign.StubTipLocalZ} m), min focus radius " +
                         $"{LiftCensus.MinFocusRadiusM} m; moving chairs {(ChairPinSystem.MovingChairsOn ? "on (park on open)" : "off (no park)")}; " +
                         $"first census: {m_Edges.Count} section(s), {m_Nodes.Count} node(s), {m_Stations.Count} station(s) → " +
                         $"{rows.Count} row(s): {complete} complete / {open} open / {unfinished} unfinished / {stationsOnly} stations only" +
                         $"{(towersRemoved > 0 ? $", {towersRemoved} tower(s) missing" : string.Empty)}" +
                         $"{(mixedRows > 0 ? $", {mixedRows} mixing lift types" : string.Empty)}; {LastBuildMs:F2} ms" +
                         $"; served rule (E1, per lift type since Catalog Phase B): run top within ServeRadiusM={LiftServedRule.ServeRadiusM:0} m (xz) of a Complete " +
                         $"lift's line chords and at most TopMarginM={LiftServedRule.TopMarginM:0} m above its top station ({ServeRulesText()}); power " +
                         "from the base station's ElectricityConsumer: NoPower while it shows the game's warning and under half " +
                         "of its wanted power arrives; back at three quarters or when the warning clears (Round S1 F2; " +
                         $"PowerGatesService={(LiftServedRule.PowerGatesService ? "true" : "false")})" +
                         // The guard clause, plus the base-only power read and the stale-streak restart
                         $"; flicker guard (Phase D): a lift without power runs again after PowerBackHoldFrames=" +
                         $"{LiftServedRule.PowerBackHoldFrames} sim frames of power (LiftServedRule.GuardPower); THE run predicate " +
                         $"LiftServedRule.Carries; power read at the base station only; a power streak unread for over " +
                         $"PowerStreakStaleFrames={LiftServedRule.PowerStreakStaleFrames} frames restarts");
            m_LastNotes = null;
            LogNotesIfChanged();
        }

        /// <summary>"'Copper Express' (base 119459:77 → top 119398:67)" or "lift (base … → top …)".</summary>
        private string Describe(LiftCensus.Row row)
        {
            // The live base's name, else the live top's (a key may hold a removed station)
            string name = "lift";
            if (StationInBuild(row.Base) && m_NameSystem.TryGetCustomName(Ent(row.Base), out string custom) && !string.IsNullOrEmpty(custom))
                name = "'" + custom + "'";
            else if (StationInBuild(row.Top) && m_NameSystem.TryGetCustomName(Ent(row.Top), out string topName) && !string.IsNullOrEmpty(topName))
                name = "'" + topName + "'";
            string b = row.Base.IsNull ? "none" : Fmt(row.Base) + (StationInBuild(row.Base) ? "" : " (removed)");
            string t = row.Top.IsNull ? "none" : Fmt(row.Top) + (StationInBuild(row.Top) ? "" : " (removed)");
            return $"{name} (base {b} → top {t})";
        }


        /// <summary>"SkiChairliftHV" (several EXPECTED lift lines: "A', 'B"); the `!!!` line wraps it in quotes. Expected lines only (a
        /// pending line is never `!!!`); the pending ones are appended by the caller.</summary>
        private static string LineNames()
        {
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            var names = new List<string>(lifts.Count);
            for (int i = 0; i < lifts.Count; i++)
                if (lifts[i].LineExpected)
                    names.Add(lifts[i].LinePrefab);
            return string.Join("', '", names.ToArray());
        }

        /// <summary>The pending lift lines (not imported yet), or "none".</summary>
        private static string PendingLineNames()
        {
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            var names = new List<string>(lifts.Count);
            for (int i = 0; i < lifts.Count; i++)
                if (!lifts[i].LineExpected)
                    names.Add(lifts[i].LinePrefab);
            return names.Count > 0 ? string.Join(", ", names.ToArray()) : "none";
        }

        /// <summary>"fixedGrip 'SkiChairliftHV' 812:1, detachable 'SkiDetachableHV' pending, ..." per lift type; NOT FOUND for a missing
        /// EXPECTED line, "pending" for a missing pending one.</summary>
        private string ChairliftsText()
        {
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            var sb = new StringBuilder(128);
            for (int i = 0; i < lifts.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                Entity line = m_PrefabCatalog.LineEntity(lifts[i]);
                sb.Append(lifts[i].Id).Append(" '").Append(lifts[i].LinePrefab).Append("' ")
                  .Append(line != Entity.Null ? Fmt(line) : lifts[i].LineExpected ? "NOT FOUND" : "pending");
            }
            return sb.ToString();
        }

        /// <summary>"fixedGrip -12, detachable -16, ...": each lift type's stations' stub tip z.</summary>
        private static string StubTipsText()
        {
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            var sb = new StringBuilder(96);
            for (int i = 0; i < lifts.Count; i++)
            {
                StationDef b = SkiCatalog.StationOf(i, StationRole.Base);
                if (b == null)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(lifts[i].Id).Append(' ').Append(b.StubTipLocalZ.ToString("0.##"));
            }
            return sb.ToString();
        }

        /// <summary>"fixedGrip along 150 / 15 m, ..., tram terminals 300 / 15 m": each type's serve rule.</summary>
        private static string ServeRulesText()
        {
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            var sb = new StringBuilder(128);
            for (int i = 0; i < lifts.Count; i++)
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(lifts[i].Id).Append(lifts[i].ServeRule == ServeRuleKind.TerminalsOnly ? " terminals " : " along ")
                  .Append(lifts[i].ServeRadiusM.ToString("0")).Append(" / ").Append(lifts[i].TopMarginM.ToString("0")).Append(" m");
            }
            return sb.ToString();
        }

        /// <summary>"'SkiBaseStationBuilding'" — every catalog station with Role Base (", "-joined).</summary>
        private static string BaseStationNames()
        {
            IReadOnlyList<StationDef> stations = SkiCatalog.Stations;
            var sb = new StringBuilder(48);
            for (int i = 0; i < stations.Count; i++)
            {
                if (stations[i].Role != StationRole.Base)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append('\'').Append(stations[i].Prefab).Append('\'');
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        /// <summary>Every row and build note as one block in the mod log (developer tab button).</summary>
        internal void DumpCensus(string reason)
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine($"LiftCensus [dump: {reason}] armed={m_Armed} disabled={m_Disabled} revision={Revision} builds={Builds} " +
                          $"last {LastBuildMs:F2} ms max {MaxBuildMs:F2} ms; last event \"{LastEvent}\"; update {m_Frame}, last build {m_LastBuildFrame}; " +
                          $"{m_Edges.Count} section(s), {m_Nodes.Count} node(s), {m_Stations.Count} station(s); parked {m_Parked.Count}, " +
                          $"queue {m_ParkQueue.Count}; last moved {Fmt(LastMovedStation)} at frame {LastMovedFrame}");
            var rows = m_Census.Rows;
            for (int r = 0; r < rows.Count; r++)
            {
                LiftCensus.Row row = rows[r];
                LiftTypeDef rowDef = SkiCatalog.LiftByIndex(row.Type);   // the row's type (and Mixed)
                sb.AppendLine($"  row {r}: {Describe(row)} id {Fmt(row.Id)} {row.State} " +
                              $"[{(rowDef != null ? rowDef.Id : "untyped")}{(row.Mixed ? ", MIXES lift types" : string.Empty)}]; " +
                              $"{row.Sections} section(s), {row.Towers} tower(s) " +
                              (row.SplitNodes > 0 ? $"[touches {row.SplitNodes} split node(s)] " : string.Empty) +
                              $"({row.TowersRemoved} missing), {row.Chairs} chair(s), {row.Gaps} gap(s){(row.HasGap ? $" at ({row.GapAt.x:F0},{row.GapAt.z:F0})" : string.Empty)}; " +
                              $"{row.LengthM:F0} m, rise {row.RiseM:F0} m, horizontal {row.HorizontalM:F0} m" +
                              (row.AlignKnown ? $", offset {row.AlignOffsetM:F2} m, yaw {row.AlignYawDeg:F2}°" : string.Empty) +
                              (row.BendDeg > 0f ? $", sharpest bend {row.BendDeg:F1}° at tower {row.BendTower} (node {Fmt(row.BendNode)})" +
                                                  (LiftCensus.BendWarnTenths(row.BendDeg) > 0 ? " OVER the warning angle" : string.Empty) : string.Empty) +
                              (row.TrainTower > 0 ? $", tower {row.TrainTower} (node {Fmt(row.TrainNode)}) {row.TrainOverDeg:F1}° past its train's limits" : string.Empty) +
                              (row.LowSpan ? $", carriers {row.LowClearM:F2} m above the ground between {LowEnd(row.LowFrom)} and " +
                                             $"{LowEnd(row.LowTo)} (edge {Fmt(row.LowEdge)}) UNDER the floor" : string.Empty));
            }
            for (int n = 0; n < m_Census.Notes.Count; n++)
                sb.AppendLine("  note: " + m_Census.Notes[n]);
            for (int n = 0; n < m_Census.SplitNotes.Count; n++)
                sb.AppendLine("  note: " + m_Census.SplitNotes[n]);
            // Every base station inside the flicker guard (a Powered streak running while the effective value is NoPower), else "none"; then
            // the stations whose effective power is NoPower.
            uint dumpNow = m_Simulation != null ? m_Simulation.frameIndex : 0u;
            if (m_BackSince.Count == 0)
                sb.AppendLine("  power guard: none");
            foreach (KeyValuePair<Entity, uint> kv in m_BackSince)
            {
                int recuts = m_GuardRecuts.TryGetValue(kv.Key, out int r) ? r : 0;
                LiftPower effective = m_LastPower.TryGetValue(kv.Key, out LiftPower p) ? p : LiftPower.Unknown;
                sb.AppendLine($"  power guard: base {Fmt(kv.Key)} raw Powered, effective {effective} for " +
                              $"{unchecked(dumpNow - kv.Value)} / {LiftServedRule.PowerBackHoldFrames} frames ({recuts} re-cut(s))");
            }
            int noPower = 0;
            var noPowerText = new StringBuilder();
            foreach (KeyValuePair<Entity, LiftPower> kv in m_LastPower)
            {
                if (kv.Value != LiftPower.NoPower)
                    continue;
                noPower++;
                if (noPowerText.Length > 0)
                    noPowerText.Append(", ");
                noPowerText.Append(Fmt(kv.Key));
                if (m_GuardRecuts.TryGetValue(kv.Key, out int r) && r > 0)
                    noPowerText.Append(" (").Append(r).Append(" re-cut(s))");
            }
            sb.AppendLine($"  power: {m_LastPower.Count} base station(s) read this load, {noPower} without power" +
                          (noPower > 0 ? $" ({noPowerText})" : string.Empty) + $"; sim frame {dumpNow}");
            if (m_LastError != null)
                sb.AppendLine("  last error: " + m_LastError);
            Mod.Log.Info(sb.ToString().TrimEnd());
            m_TowerDiag?.DumpAll("census dump: " + reason);   // every line, per node
        }

        internal static EntityRef Ref(Entity e) => new EntityRef(e.Index, e.Version);
        private static string LowEnd(int tower) => tower > 0 ? "tower " + tower : "a station";

        internal static Entity Ent(EntityRef r) => new Entity { Index = r.Index, Version = r.Version };
        private static string Fmt(Entity e) => e.Index + ":" + e.Version;
        private static string Fmt(EntityRef r) => r.Index + ":" + r.Version;
    }

    // LiftParkSystem: the park emitter (why it exists: EngineNotes/LiftCensusSystem.md, "Near LiftParkSystem").
    // Mod.cs: UpdateAt<LiftParkSystem>(UIUpdate). Load-bearing: UIUpdate runs AFTER AllowBarrier<EndFrameBarrier> (SystemOrder.cs:62,
    // :67), so EndFrameBarrier.CreateCommandBuffer is allowed here (SafeCommandBufferSystem.cs:16-23); the same frame's 4B census has
    // already queued the edges. SubObjectSystem consumes the {Event, SubObjectsUpdated} entity at the next Modification2B (query
    // :3944-3951, event branch :369-381). Order within UIUpdate is not load-bearing.
    // House rules: "created" line; one ARM line per load; Game mode only; one-shot "!!!" catch that disables the emitter until the
    // next load. One command buffer per park (an event, not per frame).
    public sealed partial class LiftParkSystem : GameSystemBase
    {
        private LiftCensusSystem m_Census;
        private EndFrameBarrier m_EndFrameBarrier;
        private EntityArchetype m_EventArchetype;
        private bool m_Armed, m_Disabled, m_ErrorLogged;

        /// <summary>Events created this load (dev tab).</summary>
        public int EventsCreated;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_EventArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Game.Common.Event>(),
                ComponentType.ReadWrite<SubObjectsUpdated>());
            Mod.Log.Info($"{nameof(LiftParkSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_ErrorLogged = false;
            EventsCreated = 0;
            if (m_Armed)
                Mod.Log.Info("LiftPark: ARMED — parks the chairs of an opened lift: one {Event, SubObjectsUpdated} entity per " +
                             "surviving section from an EndFrameBarrier buffer at UIUpdate (consumed by SubObjectSystem at the " +
                             "next Modification2B); queue owned by LiftCensusSystem");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (!m_Armed || m_Disabled)
                    return;
                int queued = m_Census.ParkQueueCount;
                if (queued == 0)
                    return;
                EntityCommandBuffer ecb = m_EndFrameBarrier.CreateCommandBuffer();
                int created = 0;
                for (int i = 0; i < queued; i++)
                {
                    Entity edge = m_Census.ParkQueueAt(i);
                    if (!EntityManager.Exists(edge) || EntityManager.HasComponent<Deleted>(edge) ||
                        !EntityManager.HasBuffer<Game.Objects.SubObject>(edge))
                        continue;   // deleted since it was queued: nothing to re-seat
                    Entity ev = ecb.CreateEntity(m_EventArchetype);
                    ecb.SetComponent(ev, new SubObjectsUpdated(edge));
                    created++;
                }
                EventsCreated += created;
                m_Census.OnParkFlushed(created);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(LiftParkSystem)} EXCEPTION — chairs of opened lifts are no longer parked " +
                                  $"until the next game load. Please report this stack:\n{e}");
                }
            }
        }
    }
}
