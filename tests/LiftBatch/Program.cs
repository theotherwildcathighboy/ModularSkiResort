// LiftBatch - headless tests for the pure lift files (LiftCensus, StationAlign, StationMoveEpisodes, LiftReconnectEpisodes,
// LiftServedRule, the prefab catalog, the loop-cache topology, the loop hold, the power gate, the tower planner).
// Run (from the ModularSkiResort root): dotnet run --project tests/LiftBatch/LiftBatch.csproj -c Release
// Prints `FAIL  where: msg` lines and one SUMMARY line; Environment.ExitCode = failures.
// Only the pure logic is proven: the ECS harvest (which entities become Edge / Node / Station, the Deleted-tag snapshot, park events,
// the overlay) and the loop cache's Game-side geometry are not exercised. The synthetic World models a drawn lift the way the census
// sees it (station connection nodes carry their station index, interior nodes a tower); it is internal with a BeforeBuild hook so
// LiftTypeTests can stamp Edge / Station types onto the census input before Build.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    internal static class Program
    {
        private const int MaxPrinted = 250;
        private static int s_Failures;
        private static long s_Checks;
        private static int s_Printed;

        private static int Main()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            // Dev shortcut: LIFTBATCH_ONLY=eishain runs the EI tests alone (seconds instead of the whole host); CI runs everything.
            if (Environment.GetEnvironmentVariable("LIFTBATCH_ONLY") == "eishain")
            {
                EishainTests.Run(Check);
                foreach (string line in EishainTests.Report)
                    Console.WriteLine("EISH  " + line);
                Console.WriteLine("SUMMARY  LiftBatch (EI only) checks=" + s_Checks + " failures=" + s_Failures);
                return s_Failures;
            }

            CensusStraightLift();
            CensusMiddleSectionDeletedAndRedrawn();
            CensusApproachSectionDeleted();
            CensusTowerDeleted();
            CensusRedrawWithAllNewEdges();
            CensusSharedBaseStation();
            CensusLineWithoutStation();
            CensusLineRemovedEntirely();
            CensusNewLineThenClose();
            CensusTowerRemovedByAnotherTool();
            CensusRevisionAndQueries();
            CensusComplexComponent();
            CensusRedrawnFragmentRuleC();
            CensusSecondLiftFromSharedStation();
            CensusResetIsSilent();
            CensusTopStationRemovedApproachSurvives();
            CensusTopStationRemovedWithApproach();
            CensusBaseStationRemoved();
            CensusBothStationsRemoved();
            CensusNewLineFromBaseOfOpenedLift();

            AlignStubTipAndAxis();
            AlignLateralSign();
            AlignYawSign();
            AlignThresholds();
            AlignBehindNeverAligned();
            AlignSymmetry();
            AlignFacesAwayBoundaries();
            AlignClassifyBoundaries();
            AlignClassifyFieldCases();
            AlignReadoutPlace();

            MoveEpisodeTopStationSettle();
            MoveEpisodeDragIsOneMove();
            MoveEpisodeGapBoundary();
            MoveEpisodeRepeatsAndNull();
            MoveEpisodeTwoStationsAlternate();
            MoveEpisodeFrameGate();
            MoveEpisodeQuietThenAgain();
            MoveEpisodeReset();

            EpisodesDrag34();
            EpisodesAttachImmediate();
            EpisodesNoLineNoEpisode();
            EpisodesAttachMidEpisode();
            EpisodesDetachNoPoke();
            EpisodesFlushAndRemove();

            ServedRuleTests();
            ServedAssignTests();
            PowerRuleTests();

            CatalogTests.Run(Check);
            LiftTypeTests.Run(Check);
            LoopCacheTests.Run(Check);
            ShuttleTests.Run(Check);
            PowerGateTests.Run(Check);
            LoopHoldTests.Run(Check);
            ServedLinkTests.Run(Check);
            LiftPlannerTests.Run(Check);
            foreach (string line in LiftPlannerTests.Report)
                Console.WriteLine("PLAN  " + line);
            LiftToolInputTests.Run(Check);
            LiftPassTests.Run(Check);
            foreach (string line in LiftPassTests.Report)
                Console.WriteLine("PASS  " + line);
            TowerNumberTests.Run(Check);
            LoopPaceTests.Run(Check);
            AlignTests.Run(Check);
            CarrierPoseTests.Run(Check);
            BendTests.Run(Check);
            PowerJoinTests.Run(Check);
            LiftIconTests.Run(Check);
            RopePickTests.Run(Check);
            BreakoverTests.Run(Check);
            foreach (string line in BreakoverTests.Report)
                Console.WriteLine("ANGLE " + line);
            DirectionTests.Run(Check);
            foreach (string line in DirectionTests.Report)
                Console.WriteLine("DIR   " + line);
            FallbackTests.Run(Check);
            foreach (string line in FallbackTests.Report)
                Console.WriteLine("FALLB " + line);
            EishainTests.Run(Check);
            foreach (string line in EishainTests.Report)
                Console.WriteLine("EISH  " + line);

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "SUMMARY  LiftBatch checks={0} failures={1}", s_Checks, s_Failures));
            Environment.ExitCode = s_Failures;
            return s_Failures;
        }


        private static void Fail(string where, string msg)
        {
            s_Failures++;
            if (s_Printed < MaxPrinted)
                Console.WriteLine("FAIL  " + where + ": " + msg);
            else if (s_Printed == MaxPrinted)
                Console.WriteLine("FAIL  (further FAIL lines suppressed; see SUMMARY count)");
            s_Printed++;
        }

        private static bool Check(bool cond, string where, string msg)
        {
            s_Checks++;
            if (!cond)
                Fail(where, msg);
            return cond;
        }

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), where,
                what + ": expected " + expected + ", got " + actual);
        }

        private static void Near(float expected, float actual, float tol, string where, string what)
        {
            Check(math.abs(expected - actual) <= tol, where,
                what + ": expected " + expected.ToString("R") + " ± " + tol + ", got " + actual.ToString("R"));
        }


        /// <summary>A tiny editable lift world, materialized into the census input lists</summary>
        internal sealed class World
        {
            private struct St { public bool IsBase; public float3 Pos; public quaternion Rot; }
            private struct Nd { public float3 Pos; public int Station; public bool Tower; public float TrainOver; }
            private struct Ed { public int A, B, Chairs; public bool Low; public float LowClear; }

            private readonly SortedDictionary<int, St> m_Stations = new SortedDictionary<int, St>();
            private readonly SortedDictionary<int, Nd> m_Nodes = new SortedDictionary<int, Nd>();
            private readonly SortedDictionary<int, Ed> m_Edges = new SortedDictionary<int, Ed>();

            public readonly LiftCensus Census = new LiftCensus();
            /// <summary>Called with the materialized census input right before Census.Build, so a test can set per-edge /
            /// per-station fields (lift types) the World does not model</summary>
            public Action<List<LiftCensus.Edge>, List<LiftCensus.Node>, List<LiftCensus.Station>> BeforeBuild { get; set; }
            public readonly List<LiftCensus.Event> Events = new List<LiftCensus.Event>();

            public void Station(int id, bool isBase, float3 pos, float yawDeg)
            {
                m_Stations[id] = new St { IsBase = isBase, Pos = pos, Rot = quaternion.RotateY(math.radians(yawDeg)) };
            }

            public float3 Tip(int station)
            {
                St s = m_Stations[station];
                return StationAlign.StubTip(s.Pos, s.Rot);
            }

            public void Node(int id, float3 pos, int station = 0, bool tower = true)
            {
                m_Nodes[id] = new Nd { Pos = pos, Station = station, Tower = station == 0 && tower };
            }

            /// <summary>The degrees node <paramref name="node"/>'s tower rope asks past its train (TrainPitchSystem's figure, LiftCensus.Node.TrainOverDeg).</summary>
            public void SetTrainOver(int node, float deg)
            {
                Nd n = m_Nodes[node];
                n.TrainOver = deg;
                m_Nodes[node] = n;
            }

            public void SetTower(int node, bool tower)
            {
                Nd n = m_Nodes[node];
                n.Tower = tower;
                m_Nodes[node] = n;
            }

            public void Edge(int id, int a, int b, int chairs = 10)
            {
                m_Edges[id] = new Ed { A = a, B = b, Chairs = chairs };
            }

            /// <summary>Edge <paramref name="id"/>'s carriers pass <paramref name="clearM"/> above the ground, under the floor (LiftClearanceSystem's
            /// figure, LiftCensus.Edge.Low*); the low point is the edge's midpoint.</summary>
            public void SetLow(int id, float clearM)
            {
                Ed e = m_Edges[id];
                e.Low = true;
                e.LowClear = clearM;
                m_Edges[id] = e;
            }

            public void RemoveEdge(int id) => m_Edges.Remove(id);

            public void RemoveNode(int id)
            {
                m_Nodes.Remove(id);
                var gone = new List<int>();
                foreach (var kv in m_Edges)
                    if (kv.Value.A == id || kv.Value.B == id)
                        gone.Add(kv.Key);
                foreach (int e in gone)
                    m_Edges.Remove(e);
            }

            public void RemoveStation(int id) => m_Stations.Remove(id);

            public float EdgeLength(int id)
            {
                Ed e = m_Edges[id];
                return math.distance(m_Nodes[e.A].Pos, m_Nodes[e.B].Pos);
            }

            public void Build()
            {
                var stations = new List<LiftCensus.Station>();
                var stationIndex = new Dictionary<int, int>();
                foreach (var kv in m_Stations)
                {
                    stationIndex[kv.Key] = stations.Count;
                    stations.Add(new LiftCensus.Station
                    {
                        Id = new EntityRef(kv.Key, 1),
                        IsBase = kv.Value.IsBase,
                        Position = kv.Value.Pos,
                        StubTip = StationAlign.StubTip(kv.Value.Pos, kv.Value.Rot),
                        Axis = StationAlign.Axis(kv.Value.Rot),
                    });
                }
                var nodes = new List<LiftCensus.Node>();
                var edges = new List<LiftCensus.Edge>();
                var seen = new HashSet<int>();
                foreach (var kv in m_Edges)
                {
                    Ed e = kv.Value;
                    Nd a = m_Nodes[e.A], b = m_Nodes[e.B];
                    edges.Add(new LiftCensus.Edge
                    {
                        Id = new EntityRef(kv.Key, 1),
                        Start = new EntityRef(e.A, 1),
                        End = new EntityRef(e.B, 1),
                        StartPos = a.Pos,
                        EndPos = b.Pos,
                        LengthM = math.distance(a.Pos, b.Pos),
                        Chairs = e.Chairs,
                        Low = e.Low,
                        LowClearM = e.LowClear,
                        LowAt = e.Low ? math.lerp(a.Pos, b.Pos, 0.5f) : default,
                    });
                    foreach (int n in new[] { e.A, e.B })
                    {
                        if (!seen.Add(n))
                            continue;
                        Nd nd = m_Nodes[n];
                        int si = nd.Station != 0 && stationIndex.TryGetValue(nd.Station, out int idx) ? idx : -1;
                        nodes.Add(new LiftCensus.Node
                        {
                            Id = new EntityRef(n, 1),
                            Station = si,
                            HasTower = nd.Tower,
                            Position = nd.Pos,
                            TrainOverDeg = nd.TrainOver,
                        });
                    }
                }
                BeforeBuild?.Invoke(edges, nodes, stations);
                Census.Build(edges, nodes, stations, Events);
            }

            public int CountEvents(LiftCensus.EventKind kind)
            {
                int n = 0;
                foreach (var e in Events)
                    if (e.Kind == kind)
                        n++;
                return n;
            }

            public bool TryEvent(LiftCensus.EventKind kind, out LiftCensus.Event ev)
            {
                foreach (var e in Events)
                {
                    if (e.Kind == kind)
                    {
                        ev = e;
                        return true;
                    }
                }
                ev = default;
                return false;
            }
        }

        internal static EntityRef R(int index) => new EntityRef(index, 1);

        /// <summary>The reference lift: base 1 at the origin facing north (+z), top 2 at
        /// (0, 100, 600) facing south; tip nodes 200 (base) and 205 (top), towers 201-204,
        /// sections 101-105 (101 = base approach, 105 = top approach). Tips at z = 12 and 588.</summary>
        internal static World StraightLift()
        {
            var w = new World();
            w.Station(1, true, new float3(0f, 0f, 0f), 180f);    // -Z rotated to +z
            w.Station(2, false, new float3(0f, 100f, 600f), 0f); // -Z = south
            float3 a = w.Tip(1), b = w.Tip(2);
            w.Node(200, a, station: 1);
            for (int k = 1; k <= 4; k++)
                w.Node(200 + k, math.lerp(a, b, k / 5f) + new float3(0f, 8f, 0f));
            w.Node(205, b, station: 2);
            for (int k = 0; k < 5; k++)
                w.Edge(101 + k, 200 + k, 201 + k);
            return w;
        }


        private static void CensusStraightLift()
        {
            const string W = "census/straight";
            var w = StraightLift();
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows");
            if (rows.Count != 1) return;
            var r = rows[0];
            Eq(LiftCensus.State.Complete, r.State, W, "state");
            Eq(R(1), r.Id, W, "Id = base");
            Eq(R(1), r.Base, W, "base");
            Eq(R(2), r.Top, W, "top");
            Eq(5, r.Sections, W, "sections");
            Eq(4, r.Towers, W, "towers (interior nodes)");
            Eq(0, r.TowersRemoved, W, "towers removed");
            Eq(50, r.Chairs, W, "chairs");
            Eq(0, r.Gaps, W, "gaps");
            Check(!r.HasGap, W, "no gap expected");
            float len = 0f;
            for (int k = 0; k < 5; k++) len += w.EdgeLength(101 + k);
            Near(len, r.LengthM, 1e-2f, W, "LengthM = sum of sections");
            Near(100f, r.RiseM, 1e-3f, W, "rise = tip y difference");
            Near(576f, r.HorizontalM, 1e-2f, W, "horizontal = tip distance");
            Check(r.AlignKnown, W, "alignment known with both stations");
            Near(0f, r.AlignOffsetM, 1e-3f, W, "offset");
            Near(0f, r.AlignYawDeg, 1e-3f, W, "yaw");
            Eq(0, w.Events.Count, W, "baseline build raises no events");
            Check(w.Census.HasBaseline, W, "baseline set");
        }

        private static void CensusMiddleSectionDeletedAndRedrawn()
        {
            const string W = "census/middle";
            var w = StraightLift();
            w.Build();
            float lost = w.EdgeLength(103);
            w.RemoveEdge(103);   // section between towers 202 and 203
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows after deleting a middle section");
            if (rows.Count != 1) return;
            var r = rows[0];
            Eq(LiftCensus.State.Open, r.State, W, "state");
            Eq(R(1), r.Id, W, "same row id");
            Eq(1, r.Gaps, W, "gaps");
            Check(r.HasGap, W, "HasGap");
            Eq(4, r.Sections, W, "sections");
            float3 n202 = math.lerp(w.Tip(1), w.Tip(2), 2 / 5f) + new float3(0f, 8f, 0f);
            Near(n202.z, r.GapAt.z, 1e-3f, W, "GapAt at the base-side dead end (node 202)");
            Eq(2, w.Events.Count, W, "events");
            Check(w.Events.Count > 0 && w.Events[0].Kind == LiftCensus.EventKind.SectionsDeleted, W,
                "SectionsDeleted comes first");
            if (w.TryEvent(LiftCensus.EventKind.SectionsDeleted, out var del))
            {
                Eq(1, del.Count, W, "sections deleted");
                Near(lost, del.LengthM, 1e-2f, W, "length deleted");
                Eq(0, del.Row, W, "deleted row index now");
                Eq(0, del.PrevRow, W, "deleted row index before");
            }
            if (Check(w.TryEvent(LiftCensus.EventKind.Opened, out var op), W, "Opened event"))
            {
                Eq(1, op.Count, W, "Opened carries the gaps");
                Eq(0, op.Row, W, "Opened row");
            }
            Check(w.Census.LastBuildChangedTopology, W, "topology changed");

            // Redraw only the gap with a new edge id: Closed again, same key
            w.Edge(113, 202, 203);
            w.Build();
            r = w.Census.Rows[0];
            Eq(LiftCensus.State.Complete, r.State, W, "state after redraw");
            Eq(R(1), r.Base, W, "same key base");
            Eq(R(2), r.Top, W, "same key top");
            Eq(1, w.Events.Count, W, "one event on redraw");
            if (Check(w.TryEvent(LiftCensus.EventKind.Closed, out var cl), W, "Closed event"))
                Eq(1, cl.Count, W, "Closed says 'again'");
            Eq(0, w.CountEvents(LiftCensus.EventKind.SectionsDeleted), W, "no deletion on redraw");
        }

        private static void CensusApproachSectionDeleted()
        {
            const string W = "census/approach";
            var w = StraightLift();
            w.Build();
            w.RemoveEdge(101);   // base approach: base tip 200 → tower 201
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows (the base stays in the row, no StationsOnly row)");
            if (rows.Count != 1) return;
            var r = rows[0];
            Eq(LiftCensus.State.Open, r.State, W, "state");
            Eq(R(1), r.Id, W, "id stays the base");
            Eq(1, r.Gaps, W, "a deleted approach section = 1 gap");
            float3 n201 = math.lerp(w.Tip(1), w.Tip(2), 1 / 5f);
            Near(n201.z, r.GapAt.z, 1e-3f, W, "GapAt at tower 201");
            Eq(1, w.CountEvents(LiftCensus.EventKind.SectionsDeleted), W, "SectionsDeleted");
            Eq(1, w.CountEvents(LiftCensus.EventKind.Opened), W, "Opened");
        }

        private static void CensusTowerDeleted()
        {
            const string W = "census/tower";
            var w = StraightLift();
            w.Build();
            w.RemoveNode(202);   // a tower click: removes sections 102 and 103 with the node
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows");
            if (rows.Count != 1) return;
            var r = rows[0];
            Eq(LiftCensus.State.Open, r.State, W, "state");
            Eq(3, r.Sections, W, "sections left");
            Eq(1, r.Gaps, W, "one gap (two fragments)");
            float3 n201 = math.lerp(w.Tip(1), w.Tip(2), 1 / 5f);
            Near(n201.z, r.GapAt.z, 1e-3f, W, "GapAt at the base-side dead end (tower 201)");
            if (Check(w.TryEvent(LiftCensus.EventKind.SectionsDeleted, out var del), W, "SectionsDeleted"))
                Eq(2, del.Count, W, "2 sections gone with the tower");
            Eq(1, w.CountEvents(LiftCensus.EventKind.Opened), W, "Opened");
        }

        private static void CensusRedrawWithAllNewEdges()
        {
            const string W = "census/redraw-all-new";
            var w = StraightLift();
            w.Build();
            w.RemoveNode(202);
            w.Build();
            // The game re-creates every section: all ids new
            w.Node(202, math.lerp(w.Tip(1), w.Tip(2), 2 / 5f));
            for (int k = 0; k < 5; k++)
                w.RemoveEdge(101 + k);
            for (int k = 0; k < 5; k++)
                w.Edge(301 + k, 200 + k, 201 + k);
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Complete, rows[0].State, W, "state");
            Eq(R(1), rows[0].Base, W, "same key base");
            Eq(R(2), rows[0].Top, W, "same key top");
            if (Check(w.TryEvent(LiftCensus.EventKind.Closed, out var cl), W, "Closed event"))
                Eq(1, cl.Count, W, "again");
            Eq(0, w.CountEvents(LiftCensus.EventKind.SectionsDeleted), W, "replaced edges are an edit, not a deletion");
        }

        private static void CensusSharedBaseStation()
        {
            const string W = "census/shared-base";
            var w = new World();
            w.Station(1, true, new float3(0f, 0f, 0f), 180f);
            w.Station(2, false, new float3(0f, 100f, 400f), 0f);
            w.Station(3, false, new float3(300f, 120f, 300f), -135f);
            float3 tip = w.Tip(1);
            w.Node(200, tip, station: 1);                     // one connection node shared by both lifts
            w.Node(201, new float3(0f, 10f, 200f));
            w.Node(202, w.Tip(2), station: 2);
            w.Node(211, new float3(150f, 10f, 150f));
            w.Node(212, w.Tip(3), station: 3);
            w.Edge(101, 200, 201); w.Edge(102, 201, 202);
            w.Edge(111, 200, 211); w.Edge(112, 211, 212);
            w.Build();
            var rows = w.Census.Rows;
            Eq(2, rows.Count, W, "two lifts sharing a base station = 2 rows");
            if (rows.Count != 2) return;
            Eq(R(1), rows[0].Base, W, "row 0 base");
            Eq(R(2), rows[0].Top, W, "row 0 top (sorted by top)");
            Eq(R(3), rows[1].Top, W, "row 1 top");
            Eq(LiftCensus.State.Complete, rows[0].State, W, "row 0 complete");
            Eq(LiftCensus.State.Complete, rows[1].State, W, "row 1 complete");
            var list = new List<int>();
            Check(w.Census.TryGetRowsOfStation(R(1), list) && list.Count == 2, W, "the base touches both rows");

            // Delete lift 1's approach at the shared base: lift 1 opens, lift 2 is untouched.
            w.RemoveEdge(101);
            w.Build();
            rows = w.Census.Rows;
            Eq(2, rows.Count, W, "rows after deleting lift 1's approach");
            if (rows.Count != 2) return;
            Eq(LiftCensus.State.Open, rows[0].State, W, "lift 1 open");
            Eq(LiftCensus.State.Complete, rows[1].State, W, "lift 2 still complete");
            Eq(1, w.CountEvents(LiftCensus.EventKind.Opened), W, "one Opened");
            if (w.TryEvent(LiftCensus.EventKind.Opened, out var op))
                Eq(0, op.Row, W, "Opened on lift 1");
        }

        private static void CensusLineWithoutStation()
        {
            const string W = "census/no-station";
            var w = new World();
            w.Build();   // empty baseline
            w.Node(201, new float3(0f, 0f, 0f));
            w.Node(202, new float3(0f, 0f, 100f));
            w.Node(203, new float3(0f, 0f, 200f));
            w.Edge(107, 202, 203);
            w.Edge(105, 201, 202);
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Unfinished, rows[0].State, W, "state");
            Eq(R(105), rows[0].Id, W, "keyed by its lowest-index edge");
            Check(rows[0].Base.IsNull && rows[0].Top.IsNull, W, "no stations");
            Eq(1, w.CountEvents(LiftCensus.EventKind.NewLine), W, "NewLine event");
            Check(!rows[0].AlignKnown, W, "no alignment without stations");
            Near(200f, rows[0].HorizontalM, 1e-3f, W, "horizontal = sum of chords");
        }

        private static void CensusLineRemovedEntirely()
        {
            const string W = "census/line-removed";
            var w = StraightLift();
            w.Build();
            for (int k = 0; k < 5; k++)
                w.RemoveEdge(101 + k);
            w.Build();
            var rows = w.Census.Rows;
            Eq(2, rows.Count, W, "row dropped, 2 StationsOnly rows");
            if (rows.Count != 2) return;
            Eq(LiftCensus.State.StationsOnly, rows[0].State, W, "row 0 state");
            Eq(LiftCensus.State.StationsOnly, rows[1].State, W, "row 1 state");
            Eq(R(1), rows[0].Base, W, "base station row");
            Check(rows[0].Top.IsNull, W, "base station row has no top");
            Eq(R(2), rows[1].Top, W, "top station row");
            Check(rows[1].Base.IsNull, W, "top station row has no base");
            if (Check(w.TryEvent(LiftCensus.EventKind.SectionsDeleted, out var del), W, "SectionsDeleted"))
            {
                Eq(5, del.Count, W, "all 5 sections");
                Eq(-1, del.Row, W, "row dropped");
                Eq(0, del.PrevRow, W, "previous row index");
                Eq(R(1), w.Census.PreviousRows[del.PrevRow].Base, W, "previous row names the base");
            }
            Eq(0, w.CountEvents(LiftCensus.EventKind.Opened), W, "no Opened for a dropped row");
        }

        private static void CensusNewLineThenClose()
        {
            const string W = "census/new-line";
            var w = StraightLift();
            for (int k = 0; k < 5; k++)
                w.RemoveEdge(101 + k);
            w.Build();   // baseline: 2 StationsOnly
            w.Edge(101, 200, 201);
            w.Edge(102, 201, 202);
            w.Build();
            var rows = w.Census.Rows;
            Eq(2, rows.Count, W, "rows (unfinished from base + top StationsOnly)");
            if (rows.Count != 2) return;
            Eq(LiftCensus.State.Unfinished, rows[0].State, W, "state");
            Eq(R(1), rows[0].Id, W, "id = base");
            Eq(R(1), rows[0].Base, W, "base");
            Check(rows[0].Top.IsNull, W, "no top yet");
            Eq(1, w.CountEvents(LiftCensus.EventKind.NewLine), W, "NewLine");
            Eq(LiftCensus.State.StationsOnly, rows[1].State, W, "top is StationsOnly");

            // Extend the line: same unfinished row, no new event
            w.Edge(103, 202, 203);
            w.Build();
            Eq(2, w.Census.Rows.Count, W, "rows while extending");
            Eq(0, w.Events.Count, W, "no event while extending");

            // Reach the top: Complete, Closed (new lift = "complete", Count 0)
            w.Edge(104, 203, 204);
            w.Edge(105, 204, 205);
            w.Build();
            rows = w.Census.Rows;
            Eq(1, rows.Count, W, "one complete row");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Complete, rows[0].State, W, "complete");
            if (Check(w.TryEvent(LiftCensus.EventKind.Closed, out var cl), W, "Closed"))
                Eq(0, cl.Count, W, "a new lift is 'complete', not 'again'");
            Eq(0, w.CountEvents(LiftCensus.EventKind.SectionsDeleted), W, "no deletion");
        }

        private static void CensusTowerRemovedByAnotherTool()
        {
            const string W = "census/tower-removed";
            var w = StraightLift();
            w.Build();
            w.SetTower(203, false);   // Better Bulldozer removed the tower, the node stays
            w.Build();
            var r = w.Census.Rows[0];
            Eq(LiftCensus.State.Complete, r.State, W, "still complete");
            Eq(1, r.TowersRemoved, W, "TowersRemoved");
            Eq(3, r.Towers, W, "towers");
            Eq(0, w.Events.Count, W, "no topology event (Removed is the system's)");
            w.SetTower(203, true);   // a section at the node redrawn: the game restores it
            w.Build();
            Eq(0, w.Census.Rows[0].TowersRemoved, W, "TowersRemoved clears by itself");
        }

        private static void CensusRevisionAndQueries()
        {
            const string W = "census/revision";
            var w = StraightLift();
            w.Build();
            int rev = w.Census.Revision;
            w.Build();
            Eq(rev, w.Census.Revision, W, "identical rebuild keeps the revision");
            Check(!w.Census.LastBuildChangedTopology, W, "identical rebuild: no topology change");
            w.RemoveEdge(103);
            w.Build();
            Check(w.Census.Revision != rev, W, "a deletion moves the revision");
            Check(w.Census.TryGetRowOfEdge(R(104), out int row) && row == 0, W, "edge 104 → row 0");
            Check(!w.Census.TryGetRowOfEdge(R(103), out _), W, "deleted edge has no row");
            var list = new List<int>();
            Check(w.Census.TryGetRowsOfStation(R(2), list) && list.Count == 1 && list[0] == 0, W, "top → row 0");
            list.Clear();
            Check(!w.Census.TryGetRowsOfStation(R(99), list) && list.Count == 0, W, "unknown station → none");
        }

        private static void CensusComplexComponent()
        {
            const string W = "census/complex";
            var w = new World();
            w.Station(1, true, new float3(0f, 0f, 0f), 180f);
            w.Station(3, true, new float3(200f, 0f, 0f), 180f);
            w.Node(200, w.Tip(1), station: 1);
            w.Node(210, w.Tip(3), station: 3);
            w.Node(201, new float3(100f, 0f, 100f));
            w.Edge(101, 200, 201);
            w.Edge(102, 201, 210);
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "one row for a base-to-base line");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Unfinished, rows[0].State, W, "two bases → Unfinished");
            Check(w.Census.Notes.Count > 0, W, "build note written");
            Eq(R(101), rows[0].Id, W, "keyed by its lowest edge");
        }

        private static void CensusRedrawnFragmentRuleC()
        {
            const string W = "census/rule-c";
            var w = StraightLift();
            w.Build();
            w.RemoveEdge(101);
            w.Build();   // open: the base approach is missing
            // A new section from the base that does not reach tower 201 yet (all-new edge).
            w.Node(250, new float3(0f, 5f, 40f));
            w.Edge(150, 200, 250);
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "the new fragment joins the open lift (rule c)");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Open, rows[0].State, W, "still open");
            Eq(1, rows[0].Gaps, W, "2 fragments touching both stations = 1 gap");
            Eq(0, w.CountEvents(LiftCensus.EventKind.NewLine), W, "no NewLine");
        }

        private static void CensusSecondLiftFromSharedStation()
        {
            const string W = "census/second-lift";
            var w = StraightLift();
            w.Build();
            // A new line from the base of a COMPLETE lift: a second lift, not a fragment.
            w.Node(260, new float3(50f, 5f, 60f));
            w.Edge(160, 200, 260);
            w.Build();
            var rows = w.Census.Rows;
            Eq(2, rows.Count, W, "rows");
            if (rows.Count != 2) return;
            // Sort order is Id, then Top: both rows have Id = base 1; the new row's Top is Null (0:0)
            Eq(LiftCensus.State.Unfinished, rows[0].State, W, "the new line is its own row (sorted first)");
            Check(rows[0].Top.IsNull, W, "the new row has no top");
            Eq(R(1), rows[0].Base, W, "new row from the shared base");
            Eq(LiftCensus.State.Complete, rows[1].State, W, "the first lift stays complete");
            Eq(R(2), rows[1].Top, W, "the first lift keeps its top");
            Eq(1, w.CountEvents(LiftCensus.EventKind.NewLine), W, "NewLine");
            Eq(0, w.CountEvents(LiftCensus.EventKind.Opened), W, "the complete lift did not open");
        }

        private static void CensusResetIsSilent()
        {
            const string W = "census/reset";
            var w = StraightLift();
            w.Build();
            w.RemoveEdge(103);
            w.Census.Reset();
            w.Build();
            Eq(0, w.Events.Count, W, "the first build after Reset is a silent baseline");
            // History is in memory only: after a reload mid-redraw the two fragments come back as Unfinished rows keyed by the
            // station each touches
            var rows = w.Census.Rows;
            Eq(2, rows.Count, W, "two Unfinished fragments after a reset mid-redraw");
            if (rows.Count == 2)
            {
                Eq(LiftCensus.State.Unfinished, rows[0].State, W, "fragment 0 state");
                Eq(LiftCensus.State.Unfinished, rows[1].State, W, "fragment 1 state");
            }
            w.Edge(113, 202, 203);
            w.Build();
            Eq(1, w.Census.Rows.Count, W, "the redraw joins them into one row");
            Eq(LiftCensus.State.Complete, w.Census.Rows[0].State, W, "complete after the redraw");
            if (Check(w.TryEvent(LiftCensus.EventKind.Closed, out var cl), W, "events resume after the baseline"))
                Eq(0, cl.Count, W, "no history of an Open row: 'complete'");
        }


        // A complete lift that loses a station keeps its key (no liveness condition), becomes Open and raises exactly one
        // Opened (the event that parks the chairs), not NewLine. The removed station stays in the key; the row id is the live one.

        private static void CensusTopStationRemovedApproachSurvives()
        {
            const string W = "census/top-removed (approach survives)";
            var w = StraightLift();
            w.Build();
            w.RemoveStation(2);   // GenerateNodesSystem keeps node 205 (edge 105 still uses it); it is a plain dead end now
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows");
            if (rows.Count != 1) return;
            var r = rows[0];
            Eq(LiftCensus.State.Open, r.State, W, "state");
            Eq(R(1), r.Base, W, "same key: base");
            Eq(R(2), r.Top, W, "same key: the removed top stays in the key");
            Eq(R(1), r.Id, W, "id = the live base");
            Eq(1, r.Gaps, W, "gaps (the dead terminal)");
            Eq(5, r.Sections, W, "sections untouched");
            Check(!r.AlignKnown, W, "no alignment without the top");
            Eq(1, w.CountEvents(LiftCensus.EventKind.Opened), W, "exactly one Opened (the park)");
            Eq(0, w.CountEvents(LiftCensus.EventKind.NewLine), W, "no NewLine");
            Eq(0, w.CountEvents(LiftCensus.EventKind.SectionsDeleted), W, "no section deleted");
            float3 tip2 = new float3(0f, 100f, 588f);
            Near(tip2.z, r.GapAt.z, 1e-3f, W, "GapAt at the dead terminal node");
            // (TowersRemoved for the orphaned connection node is held, unverified in the field: not asserted)

            // A new top station placed on the dead end and connected: a new key, "complete"
            w.Station(3, false, new float3(0f, 100f, 600f), 0f);
            w.Node(205, w.Tip(3), station: 3);
            w.Build();
            rows = w.Census.Rows;
            Eq(1, rows.Count, W, "one row after the new top");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Complete, rows[0].State, W, "complete again");
            Eq(R(3), rows[0].Top, W, "the new top");
            if (Check(w.TryEvent(LiftCensus.EventKind.Closed, out var cl), W, "Closed"))
                Eq(0, cl.Count, W, "a new key: 'complete', not 'again'");
            Eq(0, w.CountEvents(LiftCensus.EventKind.SectionsDeleted), W, "the old key's edges were not lost");
        }

        private static void CensusTopStationRemovedWithApproach()
        {
            const string W = "census/top-removed (approach goes too)";
            var w = StraightLift();
            w.Build();
            w.RemoveStation(2);
            w.RemoveNode(205);    // the connection node and the approach section 105 go with the station
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Open, rows[0].State, W, "state");
            Eq(R(2), rows[0].Top, W, "key kept");
            Eq(4, rows[0].Sections, W, "sections");
            Eq(1, rows[0].Gaps, W, "gaps");
            Eq(1, w.CountEvents(LiftCensus.EventKind.Opened), W, "Opened");
            Eq(0, w.CountEvents(LiftCensus.EventKind.NewLine), W, "no NewLine");
            if (Check(w.TryEvent(LiftCensus.EventKind.SectionsDeleted, out var del), W, "SectionsDeleted for the approach"))
            {
                Eq(1, del.Count, W, "1 section");
                Eq(0, del.Row, W, "the row survives");
            }
        }

        private static void CensusBaseStationRemoved()
        {
            const string W = "census/base-removed";
            var w = StraightLift();
            w.Build();
            w.RemoveStation(1);
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows");
            if (rows.Count != 1) return;
            var r = rows[0];
            Eq(LiftCensus.State.Open, r.State, W, "state");
            Eq(R(1), r.Base, W, "the removed base stays in the key");
            Eq(R(2), r.Top, W, "top");
            Eq(R(2), r.Id, W, "id = the live top (the hub addresses, renames and flies to it)");
            Eq(1, r.Gaps, W, "gaps");
            Eq(1, w.CountEvents(LiftCensus.EventKind.Opened), W, "Opened");
            Eq(0, w.CountEvents(LiftCensus.EventKind.NewLine), W, "no NewLine");
        }

        private static void CensusBothStationsRemoved()
        {
            const string W = "census/both-removed";
            var w = StraightLift();
            w.Build();
            w.RemoveStation(1);
            w.RemoveStation(2);
            w.Build();
            var rows = w.Census.Rows;
            Eq(1, rows.Count, W, "rows");
            if (rows.Count != 1) return;
            Eq(LiftCensus.State.Open, rows[0].State, W, "state");
            Eq(R(101), rows[0].Id, W, "id = the lowest edge (no live station)");
            Eq(2, rows[0].Gaps, W, "gaps");
            Eq(1, w.CountEvents(LiftCensus.EventKind.Opened), W, "Opened (chairs park)");
        }

        private static void CensusNewLineFromBaseOfOpenedLift()
        {
            const string W = "census/new-line-after-top-removed";
            var w = StraightLift();
            w.Build();
            w.RemoveStation(2);
            w.Build();   // (1, removed 2) Open
            // A new line from the base: all-new edges, one station. It must not attach to the opened lift (its other station is
            // gone): a new Unfinished row, NewLine.
            w.Node(260, new float3(50f, 5f, 60f));
            w.Edge(160, 200, 260);
            w.Build();
            var rows = w.Census.Rows;
            Eq(2, rows.Count, W, "rows");
            Eq(1, w.CountEvents(LiftCensus.EventKind.NewLine), W, "NewLine");
            Eq(0, w.CountEvents(LiftCensus.EventKind.Opened), W, "no second Opened");
            int open = 0, unfinished = 0;
            foreach (var r in rows)
            {
                if (r.State == LiftCensus.State.Open) open++;
                if (r.State == LiftCensus.State.Unfinished) unfinished++;
            }
            Check(open == 1 && unfinished == 1, W, $"open {open} unfinished {unfinished} (want 1 / 1)");
        }


        private static void AlignStubTipAndAxis()
        {
            const string W = "align/tip-axis";
            quaternion north = quaternion.RotateY(math.radians(180f));   // local −Z → world +z
            float3 tip = StationAlign.StubTip(new float3(10f, 5f, 20f), north);
            Near(10f, tip.x, 1e-4f, W, "tip x");
            Near(5f, tip.y, 1e-4f, W, "tip y");
            Near(32f, tip.z, 1e-4f, W, "tip z (12 m out along +z)");
            float2 ax = StationAlign.Axis(north);
            Near(0f, ax.x, 1e-5f, W, "axis x");
            Near(1f, ax.y, 1e-5f, W, "axis z");
            float2 south = StationAlign.Axis(quaternion.identity);
            Near(-1f, south.y, 1e-5f, W, "identity rotation leaves along −z");
        }

        private static void AlignLateralSign()
        {
            const string W = "align/lateral";
            float2 fp = new float2(0f, 1f);   // partner looks north
            float2 fs = new float2(0f, -1f);  // subject points back south
            var right = StationAlign.Measure(float3.zero, fp, new float3(0.3f, 50f, 600f), fs);
            Near(-0.3f, right.LateralM, 1e-4f, W, "+x of a north axis is RIGHT = negative");
            Near(600f, right.AlongM, 1e-3f, W, "along");
            Near(50f, right.RiseM, 1e-4f, W, "rise");
            Near(600f, right.DistanceM, 1e-2f, W, "distance");
            var left = StationAlign.Measure(float3.zero, fp, new float3(-0.3f, 0f, 600f), fs);
            Near(0.3f, left.LateralM, 1e-4f, W, "−x of a north axis is LEFT = positive");
            var abeam = StationAlign.Measure(float3.zero, fp, new float3(100f, 0f, 0f), fs);
            Near(90f, abeam.BearingErrorDeg, 1e-3f, W, "abeam bearing error 90°");
        }

        private static void AlignYawSign()
        {
            const string W = "align/yaw";
            float2 fp = new float2(0f, 1f);
            // Rotate -f_p = (0,-1) counter-clockwise (standard 2D rotation on (x, z)) by +2 degrees
            float th = math.radians(2f);
            float2 back = -fp;
            float2 ccw = new float2(back.x * math.cos(th) - back.y * math.sin(th), back.x * math.sin(th) + back.y * math.cos(th));
            var r = StationAlign.Measure(float3.zero, fp, new float3(0f, 0f, 600f), ccw);
            Near(2f, r.YawDeg, 1e-3f, W, "counter-clockwise turn is positive yaw");
            float2 cw = new float2(back.x * math.cos(-th) - back.y * math.sin(-th), back.x * math.sin(-th) + back.y * math.cos(-th));
            r = StationAlign.Measure(float3.zero, fp, new float3(0f, 0f, 600f), cw);
            Near(-2f, r.YawDeg, 1e-3f, W, "clockwise turn is negative yaw");
            r = StationAlign.Measure(float3.zero, fp, new float3(0f, 0f, 600f), fp);
            Near(180f, math.abs(r.YawDeg), 1e-3f, W, "stub pointing away = 180° off");
            Eq(180f, StationAlign.SignedAngleDeg(new float2(1f, 0f), new float2(-1f, 0f)), W, "±180 maps to +180");
        }

        private static void AlignThresholds()
        {
            const string W = "align/thresholds";
            float2 fp = new float2(0f, 1f), fs = new float2(0f, -1f);
            Check(StationAlign.Measure(float3.zero, fp, new float3(0.5f, 0f, 600f), fs).Aligned, W, "0.5 m is aligned (inclusive)");
            Check(!StationAlign.Measure(float3.zero, fp, new float3(0.51f, 0f, 600f), fs).Aligned, W, "0.51 m is not");
            Check(StationAlign.IsAligned(0f, 0.5f, 10f), W, "0.5° is aligned (inclusive)");
            Check(!StationAlign.IsAligned(0f, 0.51f, 10f), W, "0.51° is not");
            Check(!StationAlign.IsAligned(0f, -0.51f, 10f), W, "−0.51° is not");
            Check(StationAlign.IsAligned(-0.5f, -0.5f, 0.01f), W, "both at −tolerance, barely in front");
            Eq(0.5f, StationAlign.AlignedOffsetM, W, "offset tolerance constant");
            Eq(0.5f, StationAlign.AlignedYawDeg, W, "yaw tolerance constant");
        }

        private static void AlignBehindNeverAligned()
        {
            const string W = "align/behind";
            float2 fp = new float2(0f, 1f);
            var behind = StationAlign.Measure(float3.zero, fp, new float3(0f, 0f, -600f), new float2(0f, -1f));
            Check(behind.AlongM < 0f, W, "along < 0 behind the stub");
            Check(!behind.Aligned, W, "behind is never aligned");
            Check(!StationAlign.IsAligned(0f, 0f, 0f), W, "along = 0 is never aligned");
            Check(!StationAlign.IsAligned(0f, 0f, -1f), W, "along < 0 is never aligned");
            var degenerate = StationAlign.Measure(float3.zero, float2.zero, new float3(0f, 0f, 600f), new float2(0f, -1f));
            Check(!degenerate.Valid && !degenerate.Aligned, W, "degenerate partner axis is invalid, never aligned");
        }

        private static void AlignSymmetry()
        {
            const string W = "align/symmetry";
            quaternion baseRot = quaternion.RotateY(math.radians(180f + 30f));
            float3 basePos = new float3(100f, 10f, -50f);
            float3 baseTip = StationAlign.StubTip(basePos, baseRot);
            float2 baseAxis = StationAlign.Axis(baseRot);
            // Put the top tip 700 m out along the base axis, stub pointing back
            float3 topTip = baseTip + new float3(baseAxis.x, 0f, baseAxis.y) * 700f + new float3(0f, 150f, 0f);
            float2 topAxis = -baseAxis;
            var a = StationAlign.Measure(baseTip, baseAxis, topTip, topAxis);
            var b = StationAlign.Measure(topTip, topAxis, baseTip, baseAxis);
            Check(a.Aligned && b.Aligned, W, "a perfect line is aligned whichever station is the subject");
            Near(150f, a.RiseM, 1e-2f, W, "rise base→top");
            Near(-150f, b.RiseM, 1e-2f, W, "rise top→base");
            Near(700f, a.DistanceM, 1e-2f, W, "distance");
        }


        private static void AlignFacesAwayBoundaries()
        {
            const string W = "align/faces-away";
            Eq(90f, StationAlign.FacingAwayDeg, W, "threshold constant");
            Check(!StationAlign.FacesAway(0f), W, "0° is not facing away");
            Check(!StationAlign.FacesAway(89.9f), W, "89.9° is not facing away");
            Check(!StationAlign.FacesAway(90f), W, "90° is not facing away (exclusive)");
            Check(!StationAlign.FacesAway(-90f), W, "−90° is not facing away (exclusive)");
            Check(StationAlign.FacesAway(90.1f), W, "90.1° is facing away");
            Check(StationAlign.FacesAway(-90.1f), W, "−90.1° is facing away");
            Check(StationAlign.FacesAway(180f), W, "180° is facing away");
            Check(StationAlign.FacesAway(-180f), W, "−180° is facing away");
            Check(StationAlign.FacesAway(-179.6f), W, "−179.6° (the field's '−180 °') is facing away");
            Check(!StationAlign.FacesAway(float.NaN), W, "NaN is not facing away");
        }

        private static StationAlign.Result Res(bool valid, float along, float yaw, float lateral = 0f)
        {
            return new StationAlign.Result
            {
                Valid = valid,
                AlongM = along,
                YawDeg = yaw,
                LateralM = lateral,
                Aligned = valid && StationAlign.IsAligned(lateral, yaw, along),
            };
        }

        private static void AlignClassifyBoundaries()
        {
            const string W = "align/classify";
            Eq(StationAlign.Readout.Straight, StationAlign.Classify(Res(true, 600f, 0.5f, 0.5f)), W, "aligned at both tolerances");
            Eq(StationAlign.Readout.Numbers, StationAlign.Classify(Res(true, 600f, 0.51f)), W, "0.51° in front: numbers");
            Eq(StationAlign.Readout.Numbers, StationAlign.Classify(Res(true, 600f, 89.9f)), W, "89.9° in front: numbers");
            Eq(StationAlign.Readout.Numbers, StationAlign.Classify(Res(true, 600f, 90f)), W, "90° in front: numbers");
            Eq(StationAlign.Readout.FacingAway, StationAlign.Classify(Res(true, 600f, 90.1f)), W, "90.1° in front: facing away");
            Eq(StationAlign.Readout.FacingAway, StationAlign.Classify(Res(true, 600f, -90.1f)), W, "−90.1° in front: facing away");
            Eq(StationAlign.Readout.FacingAway, StationAlign.Classify(Res(true, 600f, 180f)), W, "180° in front: facing away");
            Eq(StationAlign.Readout.FacingAway, StationAlign.Classify(Res(true, 600f, -180f)), W, "−180° in front: facing away");
            Eq(StationAlign.Readout.PartnerFacingAway, StationAlign.Classify(Res(true, 0f, 0f)), W, "along = 0 (abeam): the other station faces away");
            Eq(StationAlign.Readout.PartnerFacingAway, StationAlign.Classify(Res(true, -600f, 0f)), W, "behind, yaw 0");
            Eq(StationAlign.Readout.PartnerFacingAway, StationAlign.Classify(Res(true, -600f, 180f)), W, "behind, yaw 180: the partner rule wins");
            Eq(StationAlign.Readout.Numbers, StationAlign.Classify(Res(false, 0f, 0f)), W, "invalid (degenerate axis): numbers, as before");
            Eq(StationAlign.Readout.Numbers, StationAlign.Classify(Res(true, float.NaN, 0f)), W, "NaN along: numbers");
            Eq(0, (int)StationAlign.Readout.Numbers, W, "layout values are the tooltip's");
            Eq(1, (int)StationAlign.Readout.Straight, W, "layout value Straight");
        }

        private static void AlignClassifyFieldCases()
        {
            const string W = "align/classify-field";
            // Base at the origin facing north (+z) toward the top; top 600 m north
            float2 baseAxis = new float2(0f, 1f);
            float3 baseTip = new float3(0f, 0f, 12f);
            float3 topTip = new float3(0f, 100f, 588f);
            // (a) The top placed facing away (its stub points north, away from the base): measured against the base it is in
            //     front, yaw 180 -> FacingAway.
            var topAway = StationAlign.Measure(baseTip, baseAxis, topTip, new float2(0f, 1f));
            Near(180f, math.abs(topAway.YawDeg), 1e-3f, W, "top facing away: |yaw| 180");
            Check(topAway.AlongM > 0f, W, "top facing away: it is in front of the base");
            Eq(StationAlign.Readout.FacingAway, StationAlign.Classify(topAway), W, "top facing away → 'turn it around'");
            // (b) The same lift seen from the base (selected): the partner (top) faces away, so the base sits behind the top's
            //     stub; yaw is 180 too, but turning the base would be wrong advice.
            var baseSeen = StationAlign.Measure(topTip, new float2(0f, 1f), baseTip, baseAxis);
            Check(baseSeen.AlongM < 0f, W, "base behind the back-facing top");
            Near(180f, math.abs(baseSeen.YawDeg), 1e-3f, W, "base: |yaw| 180 as well");
            Eq(StationAlign.Readout.PartnerFacingAway, StationAlign.Classify(baseSeen), W, "base → 'the other station faces away'");
            // (c) Correct lift: both classes are Straight.
            var good = StationAlign.Measure(baseTip, baseAxis, topTip, new float2(0f, -1f));
            Eq(StationAlign.Readout.Straight, StationAlign.Classify(good), W, "facing each other → Straight");
            // (d) The top turned 45° off: numbers (fine-tuning view).
            float th = math.radians(45f);
            var off45 = StationAlign.Measure(baseTip, baseAxis, topTip, new float2(math.sin(th), -math.cos(th)));
            Eq(StationAlign.Readout.Numbers, StationAlign.Classify(off45), W, "45° off → numbers");
            // (e) Behind with tiny offset and yaw (a relocated top dropped behind the base): never Straight, and says why
            //     instead of showing small numbers.
            var behind = StationAlign.Measure(baseTip, baseAxis, new float3(0.1f, 0f, -588f), new float2(0f, -1f));
            Eq(StationAlign.Readout.PartnerFacingAway, StationAlign.Classify(behind), W, "behind, small offset/yaw → partner faces away");
        }

        /// <summary>Where the readout goes for every (preview, just moved, default tool) combination</summary>
        private static void AlignReadoutPlace()
        {
            const string W = "align/place";
            var mouse = StationAlign.ReadoutPlace.Mouse;
            var world = StationAlign.ReadoutPlace.World;
            var none = StationAlign.ReadoutPlace.None;
            // The object tool's preview: the mouse, whatever else holds (the object tool is not the default)
            Eq(mouse, StationAlign.PlaceOf(true, false, false), W, "preview, object tool → mouse");
            Eq(mouse, StationAlign.PlaceOf(true, true, false), W, "preview (moved flag ignored) → mouse");
            Eq(mouse, StationAlign.PlaceOf(true, false, true), W, "preview with the default tool (defensive) → mouse");
            Eq(mouse, StationAlign.PlaceOf(true, true, true), W, "preview, all flags → mouse");
            // The default tool: the world group at the stub tip, selected or just moved
            Eq(world, StationAlign.PlaceOf(false, false, true), W, "selected station, default tool → world");
            Eq(world, StationAlign.PlaceOf(false, true, true), W, "just-moved station, default tool → world");
            // Any other tool: a Move It nudge reaches the mouse; a station only left selected shows nothing (no rows under the
            // run brush, the Net Tool, the bulldozer, the tree tool)
            Eq(mouse, StationAlign.PlaceOf(false, true, false), W, "just-moved station, other tool (Move It) → mouse");
            Eq(none, StationAlign.PlaceOf(false, false, false), W, "selected station, other tool (run brush) → nothing");
            Eq(0, (int)none, W, "None = 0");
            Eq(1, (int)mouse, W, "Mouse = 1");
            Eq(2, (int)world, W, "World = 2");
        }


        private static void MoveEpisodeTopStationSettle()
        {
            const string W = "move-episode/top-settle";
            // 18 settle lines for one top station ~0.17-0.2 s apart. Model: 18 single-frame moves 20 frames apart at 100 fps
            // (frames 0, 20, ... 340; t = frame / 100).
            var eps = new StationMoveEpisodes();
            EntityRef st = R(82656);
            int lines = 0;
            StationMoveEpisodes.Episode closed = default;
            for (long f = 0; f <= 700; f++)
            {
                if (f % 20 == 0 && f <= 340)
                    eps.Observe(st, f, f / 100.0);
                while (eps.TryCloseOne(f, f / 100.0, out var ep))
                {
                    lines++;
                    closed = ep;
                    Eq(540L, f, W, "closes 2.0 s after the last move (3.4 s + 2.0 s at 100 fps)");
                }
            }
            Eq(1, lines, W, "one line for the whole episode (was 18)");
            Eq(18, closed.Moves, W, "18 moves");
            Eq(18, closed.MoveFrames, W, "18 move frames");
            Near(3.4f, (float)closed.SpanSeconds, 1e-6f, W, "span 3.4 s");
            Check(closed.Station == st, W, "the station");
            Eq(0, eps.OpenCount, W, "nothing left open");
        }

        private static void MoveEpisodeDragIsOneMove()
        {
            const string W = "move-episode/drag";
            var eps = new StationMoveEpisodes();
            for (long f = 0; f < 60; f++)
                eps.Observe(R(5), f, f / 60.0);
            Check(!eps.TryCloseOne(60, 60 / 60.0, out _), W, "not closed right after the drag");
            Check(!eps.TryCloseOne(170, 170 / 60.0, out _), W, "not closed 1.85 s after the last move");
            Check(eps.TryCloseOne(190, 190 / 60.0, out var ep), W, "closed 2.18 s after the last move");
            Eq(1, ep.Moves, W, "a 60-frame drag is ONE move");
            Eq(60, ep.MoveFrames, W, "60 move frames");
        }

        private static void MoveEpisodeGapBoundary()
        {
            const string W = "move-episode/gap";
            Eq(15, StationMoveEpisodes.MoveGapFrames, W, "gap constant");
            Eq(2.0, StationMoveEpisodes.QuietSeconds, W, "quiet constant");
            var eps = new StationMoveEpisodes();
            eps.Observe(R(7), 100, 1.0);
            eps.Observe(R(7), 114, 1.14);   // 14 frames: the same move
            eps.Observe(R(7), 129, 1.29);   // 15 frames after 114: a new move
            Check(eps.TryCloseOne(1000, 10.0, out var ep), W, "closes when quiet");
            Eq(2, ep.Moves, W, "14 frames apart = same move, 15 = new move");
            Eq(3, ep.MoveFrames, W, "three move frames");
        }

        private static void MoveEpisodeRepeatsAndNull()
        {
            const string W = "move-episode/repeats";
            var eps = new StationMoveEpisodes();
            eps.Observe(EntityRef.Null, 10, 0.1);
            Eq(0, eps.OpenCount, W, "a null station opens nothing");
            for (int i = 0; i < 5; i++)
                eps.Observe(R(9), 10, 0.1);   // the census reports the same last move every frame
            Check(eps.TryCloseOne(500, 5.0, out var ep), W, "closes");
            Eq(1, ep.MoveFrames, W, "a repeated (station, frame) is one observation");
            Eq(1, ep.Moves, W, "one move");
            for (int i = 0; i < 5; i++)
                eps.Observe(R(9), 10, 0.1);   // still the same report after the line was written
            Eq(0, eps.OpenCount, W, "the stale report does not reopen the episode");
        }

        private static void MoveEpisodeTwoStationsAlternate()
        {
            const string W = "move-episode/alternate";
            // The flood guard: should the census's last-moved station alternate between two stations every frame, each keeps one
            // episode (a single-episode tracker would log every frame).
            var eps = new StationMoveEpisodes();
            int lines = 0;
            for (long f = 0; f <= 400; f++)
            {
                if (f < 60)
                    eps.Observe(f % 2 == 0 ? R(1) : R(2), f, f / 60.0);
                while (eps.TryCloseOne(f, f / 60.0, out var ep))
                {
                    lines++;
                    Eq(1, ep.Moves, W, "frames 2 apart: one move each");
                    Eq(30, ep.MoveFrames, W, "30 move frames each");
                }
                if (f == 59)
                    Eq(2, eps.OpenCount, W, "two open episodes while both move");
            }
            Eq(2, lines, W, "two lines in all, not one per frame");
        }

        private static void MoveEpisodeFrameGate()
        {
            const string W = "move-episode/frame-gate";
            // A very slow frame rate: 2 s of wall clock pass in fewer than MoveGapFrames frames
            var eps = new StationMoveEpisodes();
            eps.Observe(R(3), 100, 0.0);
            Check(!eps.TryCloseOne(110, 3.0, out _), W, "3 s but only 10 frames: still open");
            Check(eps.TryCloseOne(115, 3.0, out _), W, "15 frames and 3 s: closed");
            eps.Observe(R(4), 200, 10.0);
            Check(!eps.TryCloseOne(400, 11.99, out _), W, "200 frames but 1.99 s: still open");
            Check(eps.TryCloseOne(400, 12.0, out _), W, "2.0 s (inclusive): closed");
        }

        private static void MoveEpisodeQuietThenAgain()
        {
            const string W = "move-episode/again";
            var eps = new StationMoveEpisodes();
            int lines = 0;
            for (long f = 0; f <= 1000; f++)
            {
                if (f == 0 || f == 10 || f == 600 || f == 620)
                    eps.Observe(R(8), f, f / 100.0);
                while (eps.TryCloseOne(f, f / 100.0, out _))
                    lines++;
            }
            Eq(2, lines, W, "a station moved again after the quiet time is a second episode");
        }

        private static void MoveEpisodeReset()
        {
            const string W = "move-episode/reset";
            var eps = new StationMoveEpisodes();
            eps.Observe(R(6), 1, 0.0);
            eps.Reset();
            Eq(0, eps.OpenCount, W, "reset drops the open episode (game load)");
            eps.Observe(R(6), 1, 0.0);
            Eq(1, eps.OpenCount, W, "after a reset the same report opens a new episode (new world)");
        }


        private static void EpisodesDrag34()
        {
            const string W = "episodes/drag34";
            var eps = new LiftReconnectEpisodes();
            var settled = new List<LiftReconnectEpisodes.Episode>();
            EntityRef st = R(77);
            var pokeFrames = new List<long>();
            int fullPasses = 0, lines = 0, trailing = 0;
            LiftReconnectEpisodes.Episode closed = default;
            for (long f = 0; f <= 100; f++)
            {
                if (f <= 33)
                {
                    var d = eps.OnChange(st, f, f / 60.0, moved: true, foreignSetChanged: false, hasForeignEdge: true);
                    if (d.Poke) pokeFrames.Add(f);
                    if (d.FullPass) fullPasses++;
                    if (f == 0) Check(d.Opened, W, "first change opens the episode");
                    else Check(!d.Opened, W, "later changes do not reopen (frame " + f + ")");
                }
                settled.Clear();
                eps.Tick(f, settled);
                for (int i = 0; i < settled.Count; i++)
                {
                    // The system's trailing step: poke + full pass + one line
                    pokeFrames.Add(f);
                    trailing++;
                    fullPasses++;
                    lines++;
                    closed = settled[i];
                    Eq(48L, f, W, "settles 15 frames after the last change");
                }
            }
            Eq(1, lines, W, "one line per episode");
            Eq(1, trailing, W, "one trailing poke");
            Eq(1, fullPasses, W, "the full pass comes with the trailing poke only");
            Check(pokeFrames.Count > 0 && pokeFrames[0] == 0, W, "leading poke at once");
            for (int i = 1; i < pokeFrames.Count; i++)
                Check(pokeFrames[i] - pokeFrames[i - 1] >= LiftReconnectEpisodes.PokeIntervalFrames, W,
                    "pokes ≥ 10 frames apart (" + pokeFrames[i - 1] + " → " + pokeFrames[i] + ")");
            Eq("0,10,20,30,48", string.Join(",", pokeFrames), W, "poke frames");
            Eq(34, closed.Changes, W, "34 changes counted");
            Eq(34, closed.Moves, W, "34 moves counted");
            Eq(4, closed.Pokes, W, "pokes decided by OnChange (the trailing one is the caller's)");
            Near(33f / 60f, (float)closed.SpanSeconds, 1e-4f, W, "span seconds");
            Eq(0, eps.OpenCount, W, "nothing left open");
        }

        private static void EpisodesAttachImmediate()
        {
            const string W = "episodes/attach";
            var eps = new LiftReconnectEpisodes();
            var d = eps.OnChange(R(5), 100, 1.0, moved: false, foreignSetChanged: true, hasForeignEdge: true);
            Check(d.Poke, W, "attach pokes at once");
            Check(d.FullPass, W, "attach requests the full pass at once (same-frame closure)");
            Check(d.Opened, W, "attach opens an episode");
            var settled = new List<LiftReconnectEpisodes.Episode>();
            eps.Tick(114, settled);
            Eq(0, settled.Count, W, "not settled at 14 frames");
            eps.Tick(115, settled);
            Eq(1, settled.Count, W, "settled at 15 frames");
            if (settled.Count == 1)
                Eq(1, settled[0].Attaches, W, "the episode remembers the attach");
        }

        private static void EpisodesNoLineNoEpisode()
        {
            const string W = "episodes/no-line";
            var eps = new LiftReconnectEpisodes();
            for (long f = 0; f < 20; f++)
            {
                var d = eps.OnChange(R(9), f, f / 60.0, moved: true, foreignSetChanged: false, hasForeignEdge: false);
                Check(!d.Poke && !d.FullPass && !d.Opened, W, "a station without a line never pokes (frame " + f + ")");
            }
            Eq(0, eps.OpenCount, W, "no episode for a station without a line");
        }

        private static void EpisodesAttachMidEpisode()
        {
            const string W = "episodes/attach-mid";
            var eps = new LiftReconnectEpisodes();
            eps.OnChange(R(3), 0, 0.0, true, false, true);
            var d = eps.OnChange(R(3), 2, 0.03, false, true, true);
            Check(d.Poke && d.FullPass, W, "an attach inside the 10-frame window still pokes + full pass");
            var d2 = eps.OnChange(R(3), 3, 0.05, true, false, true);
            Check(!d2.Poke, W, "the next plain move is rate-limited again");
        }

        private static void EpisodesDetachNoPoke()
        {
            const string W = "episodes/detach";
            var eps = new LiftReconnectEpisodes();
            eps.OnChange(R(4), 0, 0.0, true, false, true);
            var d = eps.OnChange(R(4), 20, 0.3, false, true, false);
            Check(!d.Poke && !d.FullPass, W, "a detach never pokes");
            Check(eps.TryGetOpen(R(4), out var ep) && ep.Detaches == 1, W, "the detach is counted");
        }

        private static void EpisodesFlushAndRemove()
        {
            const string W = "episodes/flush";
            var eps = new LiftReconnectEpisodes();
            eps.OnChange(R(1), 0, 0.0, true, false, true);
            eps.OnChange(R(2), 0, 0.0, true, false, true);
            Check(eps.Remove(R(1), out var removed) && removed.Station == R(1), W, "remove one");
            Check(!eps.Remove(R(1), out _), W, "remove twice is false");
            var closed = new List<LiftReconnectEpisodes.Episode>();
            eps.FlushAll(closed);
            Eq(1, closed.Count, W, "flush hands back the rest");
            Eq(0, eps.OpenCount, W, "nothing open after flush");
            eps.NotePokeSet(R(2), 3, 2);   // no-op on a closed episode, must not throw
        }

        // Served rule: LiftServedRule (150 m in xz of a chord, run top at most 15 m above the top station, both bounds
        // inclusive, tie -> lower EntityRef), Assign's reverse map and the one power predicate. Every oracle is a literal
        // hand value. Fixture:
        //   L1: Id (101,1), Complete, Powered, TopY 300, chords (0,0)-(0,500), (0,500)-(0,1000)
        //   L2: Id (55,1),  Complete, Powered, TopY 400, chord  (200,0)-(200,1000)
        // Points are (x, y, z); chords are xz.

        private static List<LiftServedRule.Chord> ServedChords()
        {
            return new List<LiftServedRule.Chord>
            {
                new LiftServedRule.Chord { A = new float2(0f, 0f), B = new float2(0f, 500f) },      // L1
                new LiftServedRule.Chord { A = new float2(0f, 500f), B = new float2(0f, 1000f) },   // L1
                new LiftServedRule.Chord { A = new float2(200f, 0f), B = new float2(200f, 1000f) }, // L2
            };
        }

        private static LiftServedRule.Lift ServedL1()
        {
            return new LiftServedRule.Lift
            {
                Id = new EntityRef(101, 1), Base = new EntityRef(101, 1), Top = new EntityRef(102, 1),
                Complete = true, Power = LiftPower.Powered, TopY = 300f, FirstChord = 0, ChordCount = 2,
            };
        }

        private static LiftServedRule.Lift ServedL2()
        {
            return new LiftServedRule.Lift
            {
                Id = new EntityRef(55, 1), Base = new EntityRef(55, 1), Top = new EntityRef(56, 1),
                Complete = true, Power = LiftPower.Powered, TopY = 400f, FirstChord = 2, ChordCount = 1,
            };
        }

        /// <summary>Reaches(top, lift) with its expected result; when it should reach, the distance is checked against
        /// <paramref name="d"/> (+-1e-3), else it must be +infinity</summary>
        private static void ReachesCase(float3 top, LiftServedRule.Lift lift, List<LiftServedRule.Chord> chords,
            bool expect, float d, string where)
        {
            bool got = LiftServedRule.Reaches(top, lift, chords, out float dist);
            Check(got == expect, where, "Reaches " + top + ": expected " + expect + ", got " + got);
            if (expect)
                Near(d, dist, 1e-3f, where, "distance");
            else
                Check(float.IsPositiveInfinity(dist), where, "distance must be +inf when not reached, got " + dist.ToString("R"));
        }

        /// <summary>Constants, DistanceSqXZ, Reaches (bounds, beyond the tip, each Serves condition, a non-finite top)</summary>
        private static void ServedRuleTests()
        {
            const string W = "served/rule";
            List<LiftServedRule.Chord> chords = ServedChords();

            Check(LiftServedRule.ServeRadiusM == 150f, W, "ServeRadiusM != 150");
            Check(LiftServedRule.TopMarginM == 15f, W, "TopMarginM != 15");
            Check(LiftServedRule.PowerGatesService, W, "PowerGatesService must ship true");

            Near(10000f, LiftServedRule.DistanceSqXZ(new float2(100f, 500f), new float2(0f, 0f), new float2(0f, 1000f)), 1e-3f, W, "row 2 perpendicular");
            Near(10000f, LiftServedRule.DistanceSqXZ(new float2(0f, 1100f), new float2(0f, 0f), new float2(0f, 1000f)), 1e-3f, W, "row 3 clamped to the end");
            Near(5000f, LiftServedRule.DistanceSqXZ(new float2(50f, -50f), new float2(0f, 0f), new float2(0f, 0f)), 1e-3f, W, "row 4 degenerate segment");
            Near(16f, LiftServedRule.DistanceSqXZ(new float2(3f, 4f), new float2(0f, 0f), new float2(10f, 0f)), 1e-4f, W, "row 5");

            LiftServedRule.Lift l1 = ServedL1();
            ReachesCase(new float3(100f, 250f, 500f), l1, chords, true, 100f, W + " row 6");
            ReachesCase(new float3(150f, 315f, 500f), l1, chords, true, 150f, W + " row 7 (both bounds inclusive)");
            ReachesCase(new float3(150.01f, 250f, 500f), l1, chords, false, 0f, W + " row 8 (just beyond 150 m)");
            ReachesCase(new float3(100f, 315.01f, 500f), l1, chords, false, 0f, W + " row 9 (above top + 15 m)");
            ReachesCase(new float3(0f, 250f, 1100f), l1, chords, true, 100f, W + " row 10 (beyond the top tip)");
            ReachesCase(new float3(0f, 250f, 1200f), l1, chords, false, 0f, W + " row 11 (200 m)");

            float3 onLine = new float3(0f, 250f, 500f);
            LiftServedRule.Lift l = ServedL1();
            l.Complete = false;
            ReachesCase(onLine, l, chords, false, 0f, W + " row 12 (not complete)");
            l = ServedL1();
            l.Power = LiftPower.NoPower;
            ReachesCase(onLine, l, chords, false, 0f, W + " row 13 (no power)");
            l = ServedL1();
            l.Power = LiftPower.Unknown;
            ReachesCase(onLine, l, chords, true, 0f, W + " row 14 (power unknown counts as powered)");
            l = ServedL1();
            l.TopY = float.NaN;
            Check(!LiftServedRule.Reaches(onLine, l, chords, out float _), W + " row 15", "TopY NaN must not reach");
            Check(!l.Serves, W + " row 15", "TopY NaN: Serves must be false");
            l = ServedL1();
            l.ChordCount = 0;
            Check(!LiftServedRule.Reaches(onLine, l, chords, out float _), W + " row 16", "no chords must not reach");
            Check(!l.Serves, W + " row 16", "no chords: Serves must be false");

            Check(!LiftServedRule.Reaches(new float3(float.NaN, 250f, 500f), l1, chords, out float _), W + " row 17",
                "a NaN run top must not reach");
        }

        /// <summary>ServingLift (tie -> lower EntityRef, nearest, none) and Assign</summary>
        private static void ServedAssignTests()
        {
            const string W = "served/assign";
            List<LiftServedRule.Chord> chords = ServedChords();
            var lifts = new List<LiftServedRule.Lift> { ServedL1(), ServedL2() };

            int s = LiftServedRule.ServingLift(new float3(100f, 250f, 500f), lifts, chords, out float d);
            Eq(1, s, W + " row 18", "tie at 100 m goes to the lower Id (55,1)");
            Near(100f, d, 1e-3f, W + " row 18", "distance");
            s = LiftServedRule.ServingLift(new float3(120f, 250f, 500f), lifts, chords, out d);
            Eq(1, s, W + " row 19", "nearest lift");
            Near(80f, d, 1e-3f, W + " row 19", "distance");
            s = LiftServedRule.ServingLift(new float3(60f, 250f, 500f), lifts, chords, out d);
            Eq(0, s, W + " row 20", "nearest lift");
            Near(60f, d, 1e-3f, W + " row 20", "distance");
            s = LiftServedRule.ServingLift(new float3(400f, 250f, 500f), lifts, chords, out d);
            Eq(-1, s, W + " row 21", "no lift within 150 m");
            Check(float.IsPositiveInfinity(d), W + " row 21", "distance must be +inf, got " + d.ToString("R"));

            // Assign: serving lift per run, its distance, and runs per lift (a run within reach of two lifts counts for both)
            var tops = new List<float3> { new float3(100f, 250f, 500f), new float3(60f, 250f, 500f), new float3(400f, 250f, 500f) };
            var serving = new List<int> { 7, 7 };          // stale content: Assign must clear
            var dist = new List<float> { 1f };
            var served = new List<int> { 9, 9, 9 };
            LiftServedRule.Assign(tops, lifts, chords, serving, dist, served);
            Eq(3, serving.Count, W + " row 22", "servingLift count");
            Eq(3, dist.Count, W + " row 22", "servingDistanceM count");
            Eq(2, served.Count, W + " row 22", "servedCount count");
            if (serving.Count == 3 && dist.Count == 3 && served.Count == 2)
            {
                Eq(1, serving[0], W + " row 22", "run 0 serving lift");
                Eq(0, serving[1], W + " row 22", "run 1 serving lift");
                Eq(-1, serving[2], W + " row 22", "run 2 serving lift");
                Near(100f, dist[0], 1e-3f, W + " row 22", "run 0 distance");
                Near(60f, dist[1], 1e-3f, W + " row 22", "run 1 distance");
                Check(float.IsPositiveInfinity(dist[2]), W + " row 22", "run 2 distance must be +inf, got " + dist[2].ToString("R"));
                Eq(2, served[0], W + " row 22", "L1 serves runs 0 and 1");
                Eq(2, served[1], W + " row 22", "L2 serves runs 0 and 1 (run 1 at 140 m)");
            }
        }

        /// <summary>PowerOf over the consumer flags, RunKind.Natural, IsPowered</summary>
        private static void PowerRuleTests()
        {
            const string W = "served/power";
            Eq(LiftPower.Powered, LiftServedRule.PowerOf(0), W + " row 23", "flags 0 (no flag)");
            Eq(LiftPower.Powered, LiftServedRule.PowerOf(1), W + " row 23", "flags 1 (Connected)");
            Eq(LiftPower.NoPower, LiftServedRule.PowerOf(2), W + " row 24", "flags 2 (NoElectricityWarning)");
            Eq(LiftPower.NoPower, LiftServedRule.PowerOf(4), W + " row 24", "flags 4 (BottleneckWarning)");
            Eq(LiftPower.NoPower, LiftServedRule.PowerOf(3), W + " row 24", "flags 3 (Connected + warning)");
            Eq(LiftPower.NoPower, LiftServedRule.PowerOf(5), W + " row 24", "flags 5 (Connected + bottleneck)");
            Eq(LiftPower.NoPower, LiftServedRule.PowerOf(6), W + " row 24", "flags 6 (both warnings)");
            Eq((byte)3, (byte)RunKind.Natural, W + " row 25", "RunKind.Natural (appended, never renumber)");
            Check(LiftServedRule.IsPowered(LiftPower.Unknown), W + " row 26", "Unknown counts as powered");
            Check(LiftServedRule.IsPowered(LiftPower.Powered), W + " row 26", "Powered is powered");
            Check(!LiftServedRule.IsPowered(LiftPower.NoPower), W + " row 26", "NoPower is not powered");
        }
    }
}
