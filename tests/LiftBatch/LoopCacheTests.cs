// LoopCacheTests - the headless host for LiftLoopCache's topology (the pure LiftLoopTopology over plain stand-ins; the Game-side
// geometry is not linkable here). Where-tags `loopcache/...`, LC-1..LC-10 and LC-REF.
// Pinned: a two-terminal FG loop and a Shuttle-mode tram give the right chains, pairs, base/top, A side (the cable on the right looking
// uphill, whichever chain is found first), docks, runs and gaps; the mirror image is bit-equal; shared stations, towers without node
// lanes, terminal classification strings, refusals (GapOutOfRange, NotTwoChains) and the downhill-shuttle base rule.
// LC-REF: 2,000 seeded random graphs through LoopCacheReference and LiftLoopTopology in Loop mode must give identical chains, notes, pairs.
// LIMIT: LC-REF shares its stand-ins, so it cannot see LiftLoopCache's Game-side adapter (PathNode interning, the lane fill, the
// geometry and its notes); those are checked by a body compare and by the FG loop-notes comparison on one save across the two builds.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;
using Random = System.Random;

namespace LiftBatch
{
    public static class LoopCacheTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Lc1TwoTerminalLoop();
            Lc2ShuttleTram();
            Lc3Mirror();
            Lc4TowerAnchors();
            Lc5SingleEdge();
            Lc6SharedBase();
            Lc7Classification();
            Lc8MixedType();
            Lc9DownhillShuttle();
            Lc10ShuttleRefusals();
            LcRef();
            s_Check = null;
        }

        // ================================================================== helpers

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }

        private static void Near(double expected, double actual, double tol, string where, string what)
        {
            Check(Math.Abs(expected - actual) <= tol, where, what + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture) +
                  " ± " + tol + ", got " + actual.ToString("R", CultureInfo.InvariantCulture));
        }

        private static bool SameBits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        private static bool SameBits(float3 a, float3 b) => SameBits(a.x, b.x) && SameBits(a.y, b.y) && SameBits(a.z, b.z);

        /// <summary>A synthetic lift world in the topology's stand-ins. Station i is owned by entity index 100 + i; nodes get
        /// ids 1, 2, … with one owner each (the PathNode rule); a straight lane's tangent is P1 − P0 at both ends (the
        /// derivative of the linear bezier LiftLoopCache.AppendLine builds).</summary>
        private sealed class Graph
        {
            public const int StationOwnerBase = 100;
            public readonly List<LiftLoopTopology.Lane> Lanes = new List<LiftLoopTopology.Lane>();
            public readonly List<LiftLoopTopology.Station> Stations = new List<LiftLoopTopology.Station>();
            /// <summary>The caller's map before Build: station children (sub-objects, sub-nets) → station index.</summary>
            public readonly Dictionary<int, int> Children = new Dictionary<int, int>();
            public readonly List<int> NodeIds = new List<int>();
            private readonly List<quaternion> m_Rotations = new List<quaternion>();
            private readonly Dictionary<int, float3> m_NodePos = new Dictionary<int, float3>();
            private readonly Dictionary<int, int> m_NodeOwner = new Dictionary<int, int>();
            private int m_NextNode = 1;

            public int Station(float3 position, float yawDeg, sbyte role = -1)
            {
                m_Rotations.Add(quaternion.RotateY(math.radians(yawDeg)));
                Stations.Add(new LiftLoopTopology.Station
                {
                    OwnerIndex = StationOwnerBase + Stations.Count,
                    Position = position,
                    Role = role,
                    DockA = new float3(float.NaN),
                    DockB = new float3(float.NaN),
                });
                return Stations.Count - 1;
            }

            /// <summary>World docks from prefab-local ones: position + mul(rotation, local) (= ObjectUtils.LocalToWorld,
            /// Game.Objects/ObjectUtils.cs:208-211, what LiftLoopCache.BuildShuttles calls). NaN stays NaN.</summary>
            public void Docks(int station, float3 localA, float3 localB)
            {
                LiftLoopTopology.Station s = Stations[station];
                s.DockA = math.all(math.isfinite(localA)) ? Local(station, localA) : new float3(float.NaN);
                s.DockB = math.all(math.isfinite(localB)) ? Local(station, localB) : new float3(float.NaN);
                Stations[station] = s;
            }

            public float3 Local(int station, float3 local) => Stations[station].Position + math.mul(m_Rotations[station], local);

            public int OwnerOf(int station) => Stations[station].OwnerIndex;

            public int Node(int owner, float3 position)
            {
                int id = m_NextNode++;
                m_NodePos[id] = position;
                m_NodeOwner[id] = owner;
                NodeIds.Add(id);
                return id;
            }

            public float3 PosOf(int node) => m_NodePos[node];

            public int NodeOwner(int node) => m_NodeOwner[node];

            public int Lane(int a, int b, int type = 0)
            {
                float3 p0 = m_NodePos[a], p1 = m_NodePos[b];
                float3 t = p1 - p0;
                Lanes.Add(new LiftLoopTopology.Lane
                {
                    StartNode = a,
                    EndNode = b,
                    StartOwner = m_NodeOwner[a],
                    EndOwner = m_NodeOwner[b],
                    P0 = p0,
                    P1 = p1,
                    T0 = t,
                    T1 = t,
                    TypeIndex = type,
                });
                return Lanes.Count - 1;
            }

            /// <summary>The same lane drawn the other way: ends swapped, tangents negated and swapped.</summary>
            public void Reverse(int lane)
            {
                LiftLoopTopology.Lane l = Lanes[lane];
                Lanes[lane] = new LiftLoopTopology.Lane
                {
                    StartNode = l.EndNode,
                    EndNode = l.StartNode,
                    StartOwner = l.EndOwner,
                    EndOwner = l.StartOwner,
                    P0 = l.P1,
                    P1 = l.P0,
                    T0 = -l.T1,
                    T1 = -l.T0,
                    TypeIndex = l.TypeIndex,
                };
            }

            /// <summary>One cable of a lift, base station → top station: stub anchors at the stations' local points (owned
            /// by the station unless an existing node is passed), `spans` lanes, towers between them owned by
            /// towerOwnerBase + k (two anchors and a short node lane per tower when nodeLanes). Returns the lane indices.</summary>
            public List<int> Side(int baseStation, float3 baseLocal, int topStation, float3 topLocal, int spans, bool nodeLanes,
                                  int towerOwnerBase, int type = 0, int baseNode = 0, int topNode = 0)
            {
                var made = new List<int>();
                int a = baseNode != 0 ? baseNode : Node(OwnerOf(baseStation), Local(baseStation, baseLocal));
                int b = topNode != 0 ? topNode : Node(OwnerOf(topStation), Local(topStation, topLocal));
                float3 pa = m_NodePos[a], pb = m_NodePos[b];
                float3 along = math.normalizesafe(pb - pa) * 0.5f;
                int prev = a;
                for (int k = 1; k < spans; k++)
                {
                    float3 p = math.lerp(pa, pb, k / (float)spans) + new float3(0f, 8f, 0f);
                    int owner = towerOwnerBase + k;
                    if (nodeLanes)
                    {
                        int nin = Node(owner, p);
                        int nout = Node(owner, p + along);
                        made.Add(Lane(prev, nin, type));
                        made.Add(Lane(nin, nout, type));
                        prev = nout;
                    }
                    else
                    {
                        int n = Node(owner, p);
                        made.Add(Lane(prev, n, type));
                        prev = n;
                    }
                }
                made.Add(Lane(prev, b, type));
                return made;
            }

            /// <summary>x → −x on every world point (lanes, stations, docks).</summary>
            public void Mirror()
            {
                for (int i = 0; i < Lanes.Count; i++)
                {
                    LiftLoopTopology.Lane l = Lanes[i];
                    l.P0.x = -l.P0.x;
                    l.P1.x = -l.P1.x;
                    l.T0.x = -l.T0.x;
                    l.T1.x = -l.T1.x;
                    Lanes[i] = l;
                }
                for (int i = 0; i < Stations.Count; i++)
                {
                    LiftLoopTopology.Station s = Stations[i];
                    s.Position.x = -s.Position.x;
                    s.DockA.x = -s.DockA.x;
                    s.DockB.x = -s.DockB.x;
                    Stations[i] = s;
                }
            }

            public LiftLoopTopology Build(LiftLoopTopology.Mode mode, out Dictionary<int, int> map, bool dockReach = false)
            {
                map = new Dictionary<int, int>(Children);
                var t = new LiftLoopTopology { ShuttleDockReach = dockReach };
                t.Build(Lanes, Stations, map, mode);
                return t;
            }

            public LiftLoopTopology Build(LiftLoopTopology.Mode mode) => Build(mode, out _);
        }

        /// <summary>The FG reference lift (LC-1): base 0 at the origin yaw 180, top 1 at (0, 100, 600) yaw 0, cables ±1.8 at
        /// rope height 7, stub tips local z −12 (world z 12 and 588), 5 sections, node lanes at the 4 towers. sideOrder: +1 =
        /// the world +x cable first in lane order.</summary>
        private static Graph FixedGrip(int firstSide, int spans = 5, bool nodeLanes = true, bool bothSides = true)
        {
            var g = new Graph();
            int b = g.Station(new float3(0f, 0f, 0f), 180f);
            int t = g.Station(new float3(0f, 100f, 600f), 0f);
            for (int k = 0; k < (bothSides ? 2 : 1); k++)
            {
                float s = k == 0 ? firstSide : -firstSide;   // +1 = world +x (base local −x under yaw 180, top local +x)
                g.Side(b, new float3(-s * 1.8f, 7f, -12f), t, new float3(s * 1.8f, 7f, -12f), spans, nodeLanes, 1000);
            }
            return g;
        }

        /// <summary>The tram (LC-2): base 0 (Role Base) yaw 180 with its LINE AXIS on world x 0, top 1 (Role Top) at
        /// (0, 400, 1000) yaw 0, track ropes ±halfGauge about each station's axis at y 16, stub tips local z −12, docks
        /// from the SkiCatalog tram rows, `spans` spans, lift type 3. The base's lot puts its axis at prefab x StubTipLocalX
        /// (+4.00; docks (9.50 / −1.50, 16, −5.50)), so the base origin stands at world
        /// x = StubTipLocalX (yaw 180 maps local x a to world origin.x − a): every world rope end, dock and run is the
        /// one an axis-0 layout gives (base docks world (±5.5, 16, 5.5)).</summary>
        private static Graph Tram(float halfGauge = 5.5f, int spans = 3, float baseY = 0f, float topY = 400f, bool roles = true)
        {
            var g = new Graph();
            StationDef tb = SkiCatalog.StationOf(3, StationRole.Base), tt = SkiCatalog.StationOf(3, StationRole.Top);
            float ax = tb.StubTipLocalX;
            int b = g.Station(new float3(ax, baseY, 0f), 180f, roles ? (sbyte)0 : (sbyte)-1);
            int t = g.Station(new float3(0f, topY, 1000f), 0f, roles ? (sbyte)1 : (sbyte)-1);
            g.Docks(b, tb.DockLocalA, tb.DockLocalB);
            g.Docks(t, tt.DockLocalA, tt.DockLocalB);
            for (int k = 0; k < 2; k++)
            {
                float s = k == 0 ? -1f : 1f;   // the world −x rope first in lane order
                g.Side(b, new float3(ax - s * halfGauge, 16f, -12f), t, new float3(tt.StubTipLocalX + s * halfGauge, 16f, -12f),
                       spans, false, 2000, 3);
            }
            return g;
        }

        /// <summary>+1 / −1: the world side of a chain (sign of its lanes' mean x).</summary>
        private static int SideOf(Graph g, LiftLoopTopology.Chain chain)
        {
            float sum = 0f;
            for (int i = 0; i < chain.Links.Count; i++)
            {
                LiftLoopTopology.Lane l = g.Lanes[chain.Links[i].Lane];
                sum += l.P0.x + l.P1.x;
            }
            return sum > 0f ? 1 : sum < 0f ? -1 : 0;
        }

        private static HashSet<int> LaneSet(LiftLoopTopology.Chain chain)
        {
            var set = new HashSet<int>();
            for (int i = 0; i < chain.Links.Count; i++)
                set.Add(chain.Links[i].Lane);
            return set;
        }

        // ================================================================== LC-1

        private static void Lc1TwoTerminalLoop()
        {
            const string W = "loopcache/LC-1";
            for (int order = 0; order < 2; order++)
            {
                int first = order == 0 ? -1 : 1;
                string at = first < 0 ? "(−x side found first) " : "(+x side found first) ";
                Graph g = FixedGrip(first);
                LiftLoopTopology t = g.Build(LiftLoopTopology.Mode.Loop, out Dictionary<int, int> map);
                Eq(2, t.Chains.Count, W, at + "chains");
                for (int c = 0; c < t.Diags.Count; c++)
                {
                    Eq(9, t.Diags[c].Links, W, at + "chain " + c + " links (5 edge + 4 node lanes)");
                    Eq("owner", t.Diags[c].StartHow, W, at + "chain " + c + " start how");
                    Eq("owner", t.Diags[c].EndHow, W, at + "chain " + c + " end how");
                }
                Eq(0, t.Notes.Count, W, at + "notes (" + string.Join(" | ", t.Notes) + ")");
                Eq(0, t.UnclosedChainCount, W, at + "unclosed");
                if (!Check(t.Pairs.Count == 1, W, at + "pairs " + t.Pairs.Count))
                    continue;
                LiftLoopTopology.Pair p = t.Pairs[0];
                Eq(LiftLoopTopology.PairStatus.Ok, p.Status, W, at + "status");
                Eq(0, p.StationLo, W, at + "key lo");
                Eq(1, p.StationHi, W, at + "key hi");
                Eq(0, p.Base, W, at + "base (lower terminal)");
                Eq(1, p.Top, W, at + "top");
                Eq(false, p.BaseByRole, W, at + "BaseByRole (Loop)");
                Eq(1, SideOf(g, p.A), W, at + "chain A world side (right looking uphill along +z = +x)");
                Eq(-1, SideOf(g, p.B), W, at + "chain B world side");
                Eq(true, p.UpLaneDecided, W, at + "UpLaneDecided");
                Eq(first < 0, p.UpLaneSwapped, W, at + "UpLaneSwapped");
                Check(p.A.StartStation == 0 && p.A.EndStation == 1, W, at + "A not base→top: " + p.A.StartStation + "→" + p.A.EndStation);
                Check(p.B.StartStation == 1 && p.B.EndStation == 0, W, at + "B not top→base: " + p.B.StartStation + "→" + p.B.EndStation);
                LiftLoopTopology.Endpoint(g.Lanes, p.A, false, out float3 a0, out float3 ta0);
                LiftLoopTopology.Endpoint(g.Lanes, p.A, true, out float3 a1, out _);
                Check(math.distance(a0, new float3(1.8f, 7f, 12f)) < 1e-3f, W, at + "A starts at " + a0 + ", want the base +x stub (1.8, 7, 12)");
                Check(math.distance(a1, new float3(1.8f, 107f, 588f)) < 1e-3f, W, at + "A ends at " + a1 + ", want the top +x stub (1.8, 107, 588)");
                Check(ta0.z > 0.9f && math.abs(math.length(ta0) - 1f) < 1e-5f, W, at + "A's start tangent " + ta0 + " is not a unit +z-ish travel tangent");
                Near(3.6, p.TopGapM, 1e-4, W, at + "TopGapM");
                Near(3.6, p.BaseGapM, 1e-4, W, at + "BaseGapM");
                Eq(0, p.TypeIndex, W, at + "TypeIndex");
                Check(map.TryGetValue(100, out int s0) && s0 == 0 && map.TryGetValue(101, out int s1) && s1 == 1, W,
                      at + "the station map lacks the stations' own indices (100 → 0, 101 → 1)");
                Eq(-1, (int)p.ABaseDock, W, at + "Loop mode assigns no docks");
            }
        }

        // ================================================================== LC-2

        private static void Lc2ShuttleTram()
        {
            const string W = "loopcache/LC-2";
            Graph g = Tram();
            LiftLoopTopology t = g.Build(LiftLoopTopology.Mode.Shuttle);
            Eq(2, t.Chains.Count, W, "chains");
            Eq(0, t.Notes.Count, W, "notes (" + string.Join(" | ", t.Notes) + ")");
            if (!Check(t.Pairs.Count == 1, W, "pairs " + t.Pairs.Count))
                return;
            LiftLoopTopology.Pair p = t.Pairs[0];
            Eq(LiftLoopTopology.PairStatus.Ok, p.Status, W, "status");
            Eq(true, p.BaseByRole, W, "BaseByRole");
            Eq(0, p.Base, W, "base");
            Eq(1, p.Top, W, "top");
            Check(p.A.StartStation == 0 && p.A.EndStation == 1, W, "A not base→top: " + p.A.StartStation + "→" + p.A.EndStation);
            Check(p.B.StartStation == 0 && p.B.EndStation == 1, W, "B not base→top: " + p.B.StartStation + "→" + p.B.EndStation);
            Eq(1, SideOf(g, p.A), W, "chain A world side (+x, right looking uphill)");
            Eq(-1, SideOf(g, p.B), W, "chain B world side");
            Eq(true, p.UpLaneDecided && p.UpLaneSwapped, W, "up lane decided and swapped (the −x rope is found first)");
            // Dock A is the +X bay (local 9.5), B the −X bay (−1.5): chain A (world +x) takes dock 1
            Eq(1, (int)p.ABaseDock, W, "ABaseDock (base local −X = world +x under yaw 180 = dock B, index 1)");
            Eq(0, (int)p.BBaseDock, W, "BBaseDock");
            Eq(1, (int)p.ATopDock, W, "ATopDock (top yaw 0: world +x = local +X — the dock index is per station)");
            Eq(0, (int)p.BTopDock, W, "BTopDock");
            Near(6.5, p.ABaseRunM, 1e-4, W, "ABaseRunM");
            Near(6.5, p.BBaseRunM, 1e-4, W, "BBaseRunM");
            Near(6.5, p.ATopRunM, 1e-4, W, "ATopRunM");
            Near(6.5, p.BTopRunM, 1e-4, W, "BTopRunM");
            Check(p.ABaseRefusedM == 0f && p.BBaseRefusedM == 0f && p.ATopRefusedM == 0f && p.BTopRefusedM == 0f, W, "a dock refused");
            Near(11.0, p.BaseGapM, 1e-4, W, "BaseGapM");
            Near(11.0, p.TopGapM, 1e-4, W, "TopGapM");
            Eq(3, p.TypeIndex, W, "TypeIndex (tram)");
            // The docks themselves: world (−5.5, 16, 5.5) / (5.5, 16, 5.5) at the base (A is the +X bay, local 9.5, which yaw 180 about x 4
            // puts at world −5.5), (−5.5, 416, 994.5) / (5.5, …) at the top.
            Check(math.distance(g.Stations[0].DockA, new float3(-5.5f, 16f, 5.5f)) < 1e-4f, W, "base DockA world " + g.Stations[0].DockA);
            Check(math.distance(g.Stations[1].DockA, new float3(-5.5f, 416f, 994.5f)) < 1e-4f, W, "top DockA world " + g.Stations[1].DockA);

            // The same ropes as a loop: 11 m is no bullwheel (ArcSane wants 1-10) — the tram can never close as a loop.
            LiftLoopTopology loop = g.Build(LiftLoopTopology.Mode.Loop);
            Check(loop.Pairs.Count == 1 && loop.Pairs[0].Status == LiftLoopTopology.PairStatus.GapOutOfRange, W,
                  "the tram graph in Loop mode is not GapOutOfRange");
        }

        // ================================================================== LC-3

        private static void Lc3Mirror()
        {
            const string W = "loopcache/LC-3";
            // FG loop.
            Graph g = FixedGrip(-1);
            LiftLoopTopology t = g.Build(LiftLoopTopology.Mode.Loop);
            HashSet<int> aLanes = LaneSet(t.Pairs[0].A), bLanes = LaneSet(t.Pairs[0].B);
            float topGap = t.Pairs[0].TopGapM, baseGap = t.Pairs[0].BaseGapM;
            Graph m = FixedGrip(-1);
            m.Mirror();
            LiftLoopTopology tm = m.Build(LiftLoopTopology.Mode.Loop);
            if (Check(tm.Pairs.Count == 1 && tm.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W, "FG mirror: no Ok pair"))
            {
                LiftLoopTopology.Pair p = tm.Pairs[0];
                Eq(1, SideOf(m, p.A), W, "FG mirror: A still world +x (right looking uphill)");
                Check(LaneSet(p.A).SetEquals(bLanes) && LaneSet(p.B).SetEquals(aLanes), W, "FG mirror: A is not the other cable");
                Check(SameBits(topGap, p.TopGapM) && SameBits(baseGap, p.BaseGapM), W, "FG mirror: gaps differ");
                Eq(false, p.UpLaneSwapped, W, "FG mirror: the first-found cable is now the up lane");
            }

            // Tram shuttle.
            Graph tr = Tram();
            LiftLoopTopology tt = tr.Build(LiftLoopTopology.Mode.Shuttle);
            LiftLoopTopology.Pair q = tt.Pairs[0];
            Graph trm = Tram();
            trm.Mirror();
            LiftLoopTopology ttm = trm.Build(LiftLoopTopology.Mode.Shuttle);
            if (!Check(ttm.Pairs.Count == 1 && ttm.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W, "tram mirror: no Ok pair"))
                return;
            LiftLoopTopology.Pair r = ttm.Pairs[0];
            Eq(1, SideOf(trm, r.A), W, "tram mirror: A still world +x");
            Check(LaneSet(r.A).SetEquals(LaneSet(q.B)), W, "tram mirror: A is not the other rope");
            Eq(1 - q.ABaseDock, (int)r.ABaseDock, W, "tram mirror: ABaseDock swaps");
            Eq(1 - q.BBaseDock, (int)r.BBaseDock, W, "tram mirror: BBaseDock swaps");
            Eq(1 - q.ATopDock, (int)r.ATopDock, W, "tram mirror: ATopDock swaps");
            Eq(1 - q.BTopDock, (int)r.BTopDock, W, "tram mirror: BTopDock swaps");
            // The mirrored A is the original B rope (and the dock it takes is the mirror of B's): its runs are B's, bit for bit.
            Check(SameBits(q.BBaseRunM, r.ABaseRunM) && SameBits(q.ABaseRunM, r.BBaseRunM) &&
                  SameBits(q.BTopRunM, r.ATopRunM) && SameBits(q.ATopRunM, r.BTopRunM), W, "tram mirror: runs differ");
            Check(SameBits(q.BaseGapM, r.BaseGapM) && SameBits(q.TopGapM, r.TopGapM), W, "tram mirror: gaps differ");
            Eq(true, r.BaseByRole, W, "tram mirror: base by role");
        }

        // ================================================================== LC-4

        private static void Lc4TowerAnchors()
        {
            const string W = "loopcache/LC-4";
            Graph g = FixedGrip(1, spans: 5, nodeLanes: false);
            LiftLoopTopology t = g.Build(LiftLoopTopology.Mode.Loop, out Dictionary<int, int> map);
            Eq(2, t.Chains.Count, W, "chains (one per side)");
            for (int c = 0; c < t.Chains.Count; c++)
            {
                LiftLoopTopology.Chain chain = t.Chains[c];
                Eq(5, chain.Links.Count, W, "chain " + c + " links");
                int towers = 0;
                for (int i = 0; i + 1 < chain.Links.Count; i++)
                {
                    LiftLoopTopology.Link link = chain.Links[i];
                    LiftLoopTopology.Lane lane = g.Lanes[link.Lane];
                    int exitNode = link.Reversed ? lane.StartNode : lane.EndNode;
                    int owner = link.Reversed ? lane.StartOwner : lane.EndOwner;
                    LiftLoopTopology.Link next = chain.Links[i + 1];
                    LiftLoopTopology.Lane nextLane = g.Lanes[next.Lane];
                    int entryNode = next.Reversed ? nextLane.EndNode : nextLane.StartNode;
                    Check(exitNode == entryNode, W, "chain " + c + " link " + i + " does not meet the next at one anchor");
                    if (owner >= 1000 && owner < 1100 && !map.ContainsKey(owner))
                        towers++;
                }
                Eq(4, towers, W, "chain " + c + " passes 4 tower-owned anchors");
                Check(chain.StartStation >= 0 && chain.EndStation >= 0 && chain.StartStation != chain.EndStation, W,
                      "chain " + c + " does not run station to station");
            }
            Check(t.Pairs.Count == 1 && t.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W, "pair not Ok");
        }

        // ================================================================== LC-5

        private static void Lc5SingleEdge()
        {
            const string W = "loopcache/LC-5";
            Graph g = FixedGrip(1, spans: 1, nodeLanes: false);
            LiftLoopTopology t = g.Build(LiftLoopTopology.Mode.Loop);
            Eq(2, t.Chains.Count, W, "chains");
            Check(t.Diags.Count == 2 && t.Diags[0].Links == 1 && t.Diags[1].Links == 1, W, "chains of 1 link");
            Check(t.Pairs.Count == 1 && t.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W, "single-edge loop not Ok");
            if (t.Pairs.Count == 1)
                Near(3.6, t.Pairs[0].BaseGapM, 1e-4, W, "BaseGapM");

            Graph one = FixedGrip(1, spans: 1, nodeLanes: false, bothSides: false);
            LiftLoopTopology u = one.Build(LiftLoopTopology.Mode.Loop);
            Eq(1, u.Chains.Count, W, "one side: chains");
            if (Check(u.Pairs.Count == 1, W, "one side: pairs " + u.Pairs.Count))
            {
                Eq(LiftLoopTopology.PairStatus.NotTwoChains, u.Pairs[0].Status, W, "one side: status");
                Eq(1, u.Pairs[0].ChainIndices.Count, W, "one side: the pair's chain count (LiftLoopCache adds it to unclosed)");
                Check(u.Pairs[0].A == null && u.Pairs[0].Base == -1, W, "one side: a NotTwoChains pair carries no orientation");
            }
            Eq(0, u.UnclosedChainCount, W, "one side: the topology's own unclosed count (chains with a missing terminal only)");
            Eq(0, u.Notes.Count, W, "one side: no chain note (the pair note is LiftLoopCache's)");
        }

        // ================================================================== LC-6

        private static void Lc6SharedBase()
        {
            const string W = "loopcache/LC-6";
            var g = new Graph();
            int b = g.Station(new float3(0f, 0f, 0f), 180f);
            int t1 = g.Station(new float3(0f, 100f, 600f), 0f);
            int t2 = g.Station(new float3(400f, 80f, 450f), math.degrees(math.atan2(400f, 450f)));
            int plus = g.Node(g.OwnerOf(b), g.Local(b, new float3(-1.8f, 7f, -12f)));    // world +x stub
            int minus = g.Node(g.OwnerOf(b), g.Local(b, new float3(1.8f, 7f, -12f)));
            var lift1 = new HashSet<int>();
            var lift2 = new HashSet<int>();
            lift1.UnionWith(g.Side(b, default, t1, new float3(1.8f, 7f, -12f), 5, true, 1000, baseNode: plus));
            lift1.UnionWith(g.Side(b, default, t1, new float3(-1.8f, 7f, -12f), 5, true, 1000, baseNode: minus));
            lift2.UnionWith(g.Side(b, default, t2, new float3(-1.8f, 7f, -12f), 3, false, 1100, baseNode: plus));
            lift2.UnionWith(g.Side(b, default, t2, new float3(1.8f, 7f, -12f), 3, false, 1100, baseNode: minus));
            LiftLoopTopology t = g.Build(LiftLoopTopology.Mode.Loop);
            Eq(4, t.Chains.Count, W, "chains");
            for (int c = 0; c < t.Chains.Count; c++)
            {
                HashSet<int> set = LaneSet(t.Chains[c]);
                Check(set.IsSubsetOf(lift1) || set.IsSubsetOf(lift2), W, "chain " + c + " crosses the shared base station");
            }
            if (!Check(t.Pairs.Count == 2, W, "pairs " + t.Pairs.Count))
                return;
            Check(t.Pairs[0].StationLo == 0 && t.Pairs[0].StationHi == 1, W, "pair 0 key (" + t.Pairs[0].StationLo + "," + t.Pairs[0].StationHi + "), want (0,1) — insertion order");
            Check(t.Pairs[1].StationLo == 0 && t.Pairs[1].StationHi == 2, W, "pair 1 key (" + t.Pairs[1].StationLo + "," + t.Pairs[1].StationHi + "), want (0,2)");
            Eq(LiftLoopTopology.PairStatus.Ok, t.Pairs[0].Status, W, "pair 0 status");
            Eq(LiftLoopTopology.PairStatus.Ok, t.Pairs[1].Status, W, "pair 1 status");
            Check(LaneSet(t.Pairs[1].A).IsSubsetOf(lift2) && LaneSet(t.Pairs[1].B).IsSubsetOf(lift2), W, "pair (0,2) holds lift 1's lanes");
        }

        // ================================================================== LC-7

        private static void Lc7Classification()
        {
            const string W = "loopcache/LC-7";
            // (a) "pos 9.9m": the +x cable's top terminal is owned by the line's own node, 9.9 m from the top station.
            //     (b) "child": the −x cable's top terminal is owned by a child of the top station (a sub-object).
            var g = new Graph();
            int b = g.Station(new float3(0f, 0f, 0f), 180f);
            int t = g.Station(new float3(0f, 100f, 600f), 0f);
            g.Children[555] = t;
            int near = g.Node(7000, g.Stations[t].Position + new float3(0f, 0f, -9.9f));
            int child = g.Node(555, g.Local(t, new float3(-1.8f, 7f, -12f)));
            g.Side(b, new float3(-1.8f, 7f, -12f), t, default, 5, true, 1000, topNode: near);
            g.Side(b, new float3(1.8f, 7f, -12f), t, default, 5, true, 1000, topNode: child);
            LiftLoopTopology lt = g.Build(LiftLoopTopology.Mode.Loop);
            Eq(2, lt.Diags.Count, W, "chains");
            if (lt.Diags.Count == 2)
            {
                LiftLoopTopology.ChainDiag d0 = lt.Diags[0], d1 = lt.Diags[1];
                Eq("owner", d0.StartHow, W, "(a) base terminal");
                Eq("pos 9.9m", d0.EndHow, W, "(a) the positional fallback at 9.9 m");
                Eq(1, d0.EndStation, W, "(a) classified to the top");
                Eq(7000, d0.EndOwner, W, "(a) its owner (the line's node)");
                Eq("child", d1.EndHow, W, "(b) a child-owned terminal");
                Eq(1, d1.EndStation, W, "(b) classified to the top");
            }
            Check(lt.Pairs.Count == 1 && lt.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W, "(a)/(b) pair not Ok");

            // (c) 20.6 m: beyond StationTerminalRadius (20, inclusive) → unclassified, the chain is unclosed. (Not 20.5: "F0" of an
            // exact midpoint is runtime-dependent — net8 prints "20", net48 Mono may print "21"; the classification is not, the text of a
            // log line is.)
            var h = new Graph();
            int hb = h.Station(new float3(0f, 0f, 0f), 180f);
            int ht = h.Station(new float3(0f, 100f, 600f), 0f);
            int far = h.Node(7000, h.Stations[ht].Position + new float3(0f, 0f, -20.6f));
            h.Side(hb, new float3(-1.8f, 7f, -12f), ht, default, 5, true, 1000, topNode: far);
            h.Side(hb, new float3(1.8f, 7f, -12f), ht, new float3(-1.8f, 7f, -12f), 5, true, 1000);
            LiftLoopTopology lh = h.Build(LiftLoopTopology.Mode.Loop);
            if (Check(lh.Diags.Count == 2, W, "(c) chains " + lh.Diags.Count))
            {
                Eq("none (owner 7000, nearest station 21m)", lh.Diags[0].EndHow, W, "(c) the refused fallback at 20.6 m");
                Eq(-1, lh.Diags[0].EndStation, W, "(c) unclassified");
            }
            Eq(1, lh.UnclosedChainCount, W, "(c) unclosed chains");
            Check(lh.Notes.Count == 1 && lh.Notes[0] == "chain 0: unclosed — terminals station 0 → -1 (owner / none (owner 7000, nearest station 21m))",
                  W, "(c) note: " + string.Join(" | ", lh.Notes));
            Check(lh.Pairs.Count == 1 && lh.Pairs[0].Status == LiftLoopTopology.PairStatus.NotTwoChains, W,
                  "(c) the remaining cable is not a NotTwoChains pair");
            // Exactly 20 m is still in (inclusive, LiftLoopCache.cs:370 "bestDist <= StationTerminalRadius").
            var e = new Graph();
            int eb = e.Station(new float3(0f, 0f, 0f), 180f);
            int et = e.Station(new float3(0f, 100f, 600f), 0f);
            int edge = e.Node(7000, e.Stations[et].Position + new float3(0f, 0f, -20f));
            e.Side(eb, new float3(-1.8f, 7f, -12f), et, default, 2, false, 1000, topNode: edge);
            LiftLoopTopology le = e.Build(LiftLoopTopology.Mode.Loop);
            Check(le.Diags.Count == 1 && le.Diags[0].EndHow == "pos 20.0m" && le.Diags[0].EndStation == 1, W,
                  "(d) 20.0 m is not classified (inclusive radius): " + (le.Diags.Count > 0 ? le.Diags[0].EndHow : "no chain"));
            // Nothing to chain: no lanes.
            var empty = new Graph();
            empty.Station(new float3(0f, 0f, 0f), 0f);
            LiftLoopTopology ln = empty.Build(LiftLoopTopology.Mode.Loop, out Dictionary<int, int> emptyMap);
            Check(ln.Notes.Count == 1 && ln.Notes[0] == "nothing to chain: 0 lane(s), 1 station(s)", W, "(e) nothing-to-chain note");
            Eq(0, emptyMap.Count, W, "(e) nothing to chain leaves the station map untouched (LiftLoopCache.cs:179-183 return first)");
        }

        // ================================================================== LC-8

        private static void Lc8MixedType()
        {
            const string W = "loopcache/LC-8";
            Graph g = FixedGrip(1);
            LiftLoopTopology.Lane l = g.Lanes[3];
            l.TypeIndex = 1;
            g.Lanes[3] = l;
            LiftLoopTopology t = g.Build(LiftLoopTopology.Mode.Loop);
            if (Check(t.Pairs.Count == 1, W, "pairs"))
            {
                Eq(LiftLoopTopology.PairStatus.Ok, t.Pairs[0].Status, W, "status (a mixed loop still closes)");
                Eq(-1, t.Pairs[0].TypeIndex, W, "TypeIndex (mixed)");
            }
            Graph n = FixedGrip(1);
            LiftLoopTopology.Lane m = n.Lanes[0];
            m.TypeIndex = -1;
            n.Lanes[0] = m;
            LiftLoopTopology tn = n.Build(LiftLoopTopology.Mode.Loop);
            Check(tn.Pairs.Count == 1 && tn.Pairs[0].TypeIndex == -1, W, "a negative lane type does not make the pair −1");
        }

        // ================================================================== LC-9

        private static LiftLoopTopology.Station SetRole(LiftLoopTopology.Station s, sbyte role)
        {
            s.Role = role;
            return s;
        }

        private static void Lc9DownhillShuttle()
        {
            const string W = "loopcache/LC-9";
            Graph g = Tram(baseY: 400f, topY: 0f);   // the Base-role station sits 400 m ABOVE the top
            LiftLoopTopology s = g.Build(LiftLoopTopology.Mode.Shuttle);
            if (Check(s.Pairs.Count == 1 && s.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W, "shuttle pair not Ok"))
            {
                LiftLoopTopology.Pair p = s.Pairs[0];
                Eq(0, p.Base, W, "Shuttle base = the Role-0 station (C1)");
                Eq(true, p.BaseByRole, W, "BaseByRole");
                Check(p.A.StartStation == 0 && p.B.StartStation == 0, W, "chains not oriented from the role base");
                Eq(1, SideOf(g, p.A), W, "A = right looking from the base toward the top (+z: +x)");
                Eq(1, (int)p.ABaseDock, W, "ABaseDock (T14 x-sign N, 2026-10-16: chain A takes the −X bay = dock B, index 1)");
            }
            LiftLoopTopology l =g.Build(LiftLoopTopology.Mode.Loop);
            if (Check(l.Pairs.Count == 1, W, "loop pairs"))
            {
                Eq(0, l.Pairs[0].Base, W, "Loop base = the Role-0 station, though it is the higher one");
                Eq(true, l.Pairs[0].BaseByRole, W, "Loop BaseByRole");
                Check(l.Pairs[0].A.StartStation == 0 && l.Pairs[0].B.EndStation == 0, W, "loop chains not oriented from the role base");
            }
            // A Loop pair with one role unknown keeps the height rule (a station with no catalog row says nothing about being the base).
            Graph half = Tram(baseY: 400f, topY: 0f);
            half.Stations[1] = SetRole(half.Stations[1], -1);
            LiftLoopTopology h = half.Build(LiftLoopTopology.Mode.Loop);
            if (Check(h.Pairs.Count == 1, W, "loop, one role unknown: pairs"))
            {
                Eq(1, h.Pairs[0].Base, W, "Loop, one role unknown: the lower station");
                Eq(false, h.Pairs[0].BaseByRole, W, "Loop, one role unknown: BaseByRole");
            }
            // Both stations the same role: no role decides, the height rule stands.
            Graph same = Tram(baseY: 400f, topY: 0f);
            same.Stations[1] = SetRole(same.Stations[1], 0);
            LiftLoopTopology sm = same.Build(LiftLoopTopology.Mode.Loop);
            if (Check(sm.Pairs.Count == 1, W, "loop, both Base: pairs"))
            {
                Eq(1, sm.Pairs[0].Base, W, "Loop, both Base: the lower station");
                Eq(false, sm.Pairs[0].BaseByRole, W, "Loop, both Base: BaseByRole");
            }
            Graph n = Tram(baseY: 400f, topY: 0f, roles: false);
            LiftLoopTopology r = n.Build(LiftLoopTopology.Mode.Shuttle);
            if (Check(r.Pairs.Count == 1, W, "roleless pairs"))
            {
                Eq(1, r.Pairs[0].Base, W, "no roles: the height rule");
                Eq(false, r.Pairs[0].BaseByRole, W, "no roles: BaseByRole");
            }
        }

        // ================================================================== LC-10

        private static void Lc10ShuttleRefusals()
        {
            const string W = "loopcache/LC-10";
            Graph narrow = Tram(halfGauge: 1.0f);
            LiftLoopTopology t = narrow.Build(LiftLoopTopology.Mode.Shuttle);
            if (Check(t.Pairs.Count == 1, W, "(a) pairs"))
            {
                Eq(LiftLoopTopology.PairStatus.GapOutOfRange, t.Pairs[0].Status, W, "(a) ropes 2.0 m apart");
                Near(2.0, t.Pairs[0].BaseGapM, 1e-4, W, "(a) BaseGapM");
                Eq(-1, (int)t.Pairs[0].ABaseDock, W, "(a) no docks on a refused pair");
            }

            Graph far = Tram();
            float bx = SkiCatalog.StationOf(3, StationRole.Base).StubTipLocalX; // the base's line axis (+4.00)
            far.Docks(0, new float3(bx - 5.5f, 16f, 29f), new float3(bx + 5.5f, 16f, -5.5f));   // DockA 41 m from rope A's end
            LiftLoopTopology f = far.Build(LiftLoopTopology.Mode.Shuttle);
            if (Check(f.Pairs.Count == 1 && f.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W, "(b) pair not Ok"))
            {
                LiftLoopTopology.Pair p = f.Pairs[0];
                Eq(-1, (int)p.ABaseDock, W, "(b) ABaseDock refused");
                Eq(0f, p.ABaseRunM, W, "(b) ABaseRunM");
                Near(41.0, p.ABaseRefusedM, 1e-3, W, "(b) ABaseRefusedM");
                Eq(1, (int)p.BBaseDock, W, "(b) BBaseDock kept");
                Near(6.5, p.BBaseRunM, 1e-4, W, "(b) BBaseRunM");
                Eq(1, (int)p.ATopDock, W, "(b) the top is unaffected");
            }

            Graph nan = Tram();
            nan.Docks(1, new float3(float.NaN), new float3(float.NaN));
            LiftLoopTopology n = nan.Build(LiftLoopTopology.Mode.Shuttle);
            if (Check(n.Pairs.Count == 1 && n.Pairs[0].Status == LiftLoopTopology.PairStatus.Ok, W, "(c) pair not Ok"))
            {
                LiftLoopTopology.Pair p = n.Pairs[0];
                Check(p.ATopDock == -1 && p.BTopDock == -1 && p.ATopRunM == 0f && p.BTopRunM == 0f && p.ATopRefusedM == 0f, W,
                      "(c) NaN docks assigned something");
                Eq(1, (int)p.ABaseDock, W, "(c) the base docks as usual (T14 x-sign N, 2026-10-16: A takes dock index 1)");
            }

            Check(LiftLoopTopology.ShuttleGapSane(4f) && LiftLoopTopology.ShuttleGapSane(24f), W, "(d) the window bounds are inclusive");
            Check(!LiftLoopTopology.ShuttleGapSane(3.999f) && !LiftLoopTopology.ShuttleGapSane(24.001f) &&
                  !LiftLoopTopology.ShuttleGapSane(float.NaN), W, "(d) outside / NaN accepted");
            // The Loop gap test's exclusive bounds (LiftLoopCache.cs:558-562 "d > 1f && d < 10f"): random graphs never land on
            // them exactly (a mutation to "<= 10" survived LC-REF), so they are pinned here, and on a whole lift at 10.0 m.
            Check(!LiftLoopTopology.ArcSane(new float3(0f), new float3(10f, 0f, 0f)) &&
                  !LiftLoopTopology.ArcSane(new float3(0f), new float3(1f, 0f, 0f)), W, "(d) ArcSane accepts 1.0 or 10.0 m (exclusive)");
            Check(LiftLoopTopology.ArcSane(new float3(0f), new float3(9.99f, 0f, 0f)) &&
                  LiftLoopTopology.ArcSane(new float3(0f), new float3(1.01f, 0f, 0f)), W, "(d) ArcSane refuses 1.01 or 9.99 m");
            var wide = new Graph();
            int wb = wide.Station(new float3(0f, 0f, 0f), 180f);
            int wt = wide.Station(new float3(0f, 100f, 600f), 0f);
            wide.Side(wb, new float3(-5f, 7f, -12f), wt, new float3(5f, 7f, -12f), 2, false, 1000);
            wide.Side(wb, new float3(5f, 7f, -12f), wt, new float3(-5f, 7f, -12f), 2, false, 1000);
            LiftLoopTopology lw = wide.Build(LiftLoopTopology.Mode.Loop);
            Check(lw.Pairs.Count == 1 && lw.Pairs[0].Status == LiftLoopTopology.PairStatus.GapOutOfRange && lw.Pairs[0].BaseGapM == 10f,
                  W, "(d) a loop with cables exactly 10.0 m apart is not refused");
            Eq(30f, LiftLoopTopology.MaxDockRunM, W, "(d) MaxDockRunM");
            Eq(20f, LiftLoopTopology.StationTerminalRadius, W, "(d) StationTerminalRadius (was LiftLoopCache.cs:130)");
        }

        // ================================================================== LC-REF

        private const int RefGraphs = 2000;

        private static void LcRef()
        {
            const string W = "loopcache/LC-REF";
            int differ = 0, ok = 0, gap = 0, notTwo = 0, unclosed = 0, swapped = 0, undecided = 0, mixed = 0, empty = 0;
            int howOwner = 0, howChild = 0, howPos = 0, howNone = 0, ties = 0, shuttleOk = 0, docked = 0, refused = 0, byRole = 0, reclassified = 0, loopByRole = 0;
            for (int seed = 1; seed <= RefGraphs; seed++)
            {
                Graph g = RandomGraph(new Random(seed), out bool tie);
                if (tie)
                    ties++;
                var stationIds = new List<int>();
                var positions = new List<float3>();
                var roles = new List<sbyte>();
                for (int i = 0; i < g.Stations.Count; i++)
                {
                    stationIds.Add(g.Stations[i].OwnerIndex);
                    positions.Add(g.Stations[i].Position);
                    roles.Add(g.Stations[i].Role);
                }
                var refMap = new Dictionary<int, int>(g.Children);
                var reference = new LoopCacheReference();
                try
                {
                    reference.Build(g.Lanes, stationIds, positions, refMap, roles);
                }
                catch (Exception e)
                {
                    Check(false, W, "seed " + seed + ": the reference threw " + e.GetType().Name + ": " + e.Message);
                    differ++;
                    continue;
                }
                LiftLoopTopology t = g.Build(LiftLoopTopology.Mode.Loop, out Dictionary<int, int> topoMap);
                string diff = Compare(reference, t, refMap, topoMap);
                if (!Check(diff == null, W, "seed " + seed + ": " + diff))
                    differ++;

                // Coverage counters (the generator must reach every branch).
                if (g.Lanes.Count == 0 || g.Stations.Count == 0)
                    empty++;
                unclosed += t.UnclosedChainCount;
                for (int c = 0; c < t.Diags.Count; c++)
                {
                    Count(t.Diags[c].StartHow, ref howOwner, ref howChild, ref howPos, ref howNone);
                    Count(t.Diags[c].EndHow, ref howOwner, ref howChild, ref howPos, ref howNone);
                }
                for (int p = 0; p < t.Pairs.Count; p++)
                {
                    LiftLoopTopology.Pair pair = t.Pairs[p];
                    if (pair.Status == LiftLoopTopology.PairStatus.NotTwoChains)
                    {
                        notTwo++;
                        continue;
                    }
                    if (pair.BaseByRole)
                        loopByRole++;
                    if (pair.UpLaneSwapped)
                        swapped++;
                    if (!pair.UpLaneDecided)
                        undecided++;
                    if (pair.Status == LiftLoopTopology.PairStatus.GapOutOfRange)
                        gap++;
                    else
                    {
                        ok++;
                        if (pair.TypeIndex < 0)
                            mixed++;
                    }
                }

                // Shuttle mode on the same graph: the chain pass is mode-independent; pairs obey the shuttle rules.
                LiftLoopTopology s = g.Build(LiftLoopTopology.Mode.Shuttle, out Dictionary<int, int> shuttleMap);
                string sdiff = ShuttleInvariants(g, t, s, topoMap, shuttleMap, ref shuttleOk, ref docked, ref refused, ref byRole);
                if (!Check(sdiff == null, W, "seed " + seed + " (Shuttle mode): " + sdiff))
                    differ++;

                // The same graph with ShuttleDockReach on = the flag-off chain pass, except terminals the dock reach newly owns.
                LiftLoopTopology sr = g.Build(LiftLoopTopology.Mode.Shuttle, out Dictionary<int, int> reachMap, dockReach: true);
                string rdiff = ReachInvariants(g, s, sr, shuttleMap, reachMap, ref reclassified);
                if (!Check(rdiff == null, W, "seed " + seed + " (Shuttle mode, dock reach): " + rdiff))
                    differ++;
            }
            Eq(0, differ, W, "graphs that differ from the reference / break a shuttle invariant");
            // Coverage floors: a generator change that stops exercising a branch fails here.
            Check(ok >= 300 && gap >= 100 && notTwo >= 100 && unclosed >= 300, W, "coverage: pairs Ok " + ok + " / gap " + gap +
                  " / not two " + notTwo + ", unclosed chains " + unclosed);
            Check(howOwner >= 1000 && howChild >= 100 && howPos >= 100 && howNone >= 100, W, "coverage: terminals owner " + howOwner +
                  " / child " + howChild + " / pos " + howPos + " / none " + howNone);
            Check(swapped >= 100 && undecided >= 5 && mixed >= 10 && empty >= 10 && ties >= 50, W, "coverage: swapped " + swapped +
                  ", undecided " + undecided + ", mixed " + mixed + ", nothing to chain " + empty + ", height ties " + ties);
            Check(loopByRole >= 30, W, "coverage (Loop): pairs with the base chosen by role " + loopByRole);
            Check(reclassified >= 5, W, "coverage (dock reach): terminals reclassified " + reclassified);
            Check(shuttleOk >= 50 && docked >= 50 && refused >= 5 && byRole >= 50, W, "coverage (Shuttle): Ok " + shuttleOk +
                  ", docked ends " + docked + ", refused docks " + refused + ", base by role " + byRole);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  {0}: {1} graphs; pairs Ok {2}, gap {3}, not two {4}; unclosed chains {5}; terminals owner {6} / child {7} / " +
                "pos {8} / none {9}; swapped {10}, undecided {11}, mixed {12}, empty {13}, height ties {14}; Shuttle Ok {15}, docked " +
                "ends {16}, refused {17}, by role {18}; Loop by role {19}",
                W, RefGraphs, ok, gap, notTwo, unclosed, howOwner, howChild, howPos, howNone, swapped, undecided, mixed, empty, ties,
                shuttleOk, docked, refused, byRole, loopByRole));
        }

        private static void Count(string how, ref int owner, ref int child, ref int pos, ref int none)
        {
            if (how == "owner")
                owner++;
            else if (how == "child")
                child++;
            else if (how.StartsWith("pos ", StringComparison.Ordinal))
                pos++;
            else
                none++;
        }

        /// <summary>null when the topology reproduces the reference exactly; else the first difference.</summary>
        private static string Compare(LoopCacheReference r, LiftLoopTopology t, Dictionary<int, int> refMap, Dictionary<int, int> topoMap)
        {
            if (r.m_ChainCount != t.Chains.Count || r.m_Chains.Count != t.Chains.Count || r.m_ChainDiags.Count != t.Diags.Count)
                return "chain count " + r.m_ChainCount + " vs " + t.Chains.Count;
            for (int c = 0; c < t.Chains.Count; c++)
            {
                LoopCacheReference.Chain rc = r.m_Chains[c];
                LiftLoopTopology.Chain tc = t.Chains[c];
                if (rc.m_Links.Count != tc.Links.Count)
                    return "chain " + c + " links " + rc.m_Links.Count + " vs " + tc.Links.Count;
                for (int i = 0; i < tc.Links.Count; i++)
                {
                    if (rc.m_Links[i].m_LaneIndex != tc.Links[i].Lane || rc.m_Links[i].m_Reversed != tc.Links[i].Reversed)
                        return "chain " + c + " link " + i + " (" + rc.m_Links[i].m_LaneIndex + "," + rc.m_Links[i].m_Reversed + ") vs (" +
                               tc.Links[i].Lane + "," + tc.Links[i].Reversed + ")";
                }
                if (rc.m_StartTerminal != tc.StartNode || rc.m_EndTerminal != tc.EndNode)
                    return "chain " + c + " terminals";
                if (rc.m_StartStation != tc.StartStation || rc.m_EndStation != tc.EndStation)
                    return "chain " + c + " stations " + rc.m_StartStation + "→" + rc.m_EndStation + " vs " + tc.StartStation + "→" + tc.EndStation;
                LoopCacheReference.ChainDiag rd = r.m_ChainDiags[c];
                LiftLoopTopology.ChainDiag td = t.Diags[c];
                if (rd.m_Links != td.Links || rd.m_StartOwner != td.StartOwner || rd.m_EndOwner != td.EndOwner ||
                    rd.m_StartStation != td.StartStation || rd.m_EndStation != td.EndStation)
                    return "chain " + c + " diag numbers";
                if (rd.m_StartHow != td.StartHow || rd.m_EndHow != td.EndHow)
                    return "chain " + c + " diag how '" + rd.m_StartHow + "'/'" + rd.m_EndHow + "' vs '" + td.StartHow + "'/'" + td.EndHow + "'";
                if (!SameBits(rd.m_StartPos, td.StartPos) || !SameBits(rd.m_EndPos, td.EndPos))
                    return "chain " + c + " diag positions (bits)";
            }

            int unclosed = t.UnclosedChainCount;
            var notes = new List<string>(t.Notes);
            for (int p = 0; p < t.Pairs.Count; p++)
            {
                // The pair notes LiftLoopCache.Build writes, in pair order (the same expressions; the integrator's body
                // compare holds the cache to them).
                LiftLoopTopology.Pair pair = t.Pairs[p];
                if (pair.Status == LiftLoopTopology.PairStatus.NotTwoChains)
                {
                    unclosed += pair.ChainIndices.Count;
                    notes.Add($"stations ({pair.StationLo},{pair.StationHi}): {pair.ChainIndices.Count} chain(s), need exactly 2");
                }
                else if (pair.Status == LiftLoopTopology.PairStatus.GapOutOfRange)
                {
                    unclosed += 2;
                    string gapWhy = $"terminal gap out of range (top {pair.TopGapM:F2} m, base {pair.BaseGapM:F2} m; want 1–10)";
                    notes.Add($"stations ({pair.StationLo},{pair.StationHi}): closure failed — {gapWhy}");
                }
            }
            if (unclosed != r.m_UnclosedChainCount)
                return "unclosed " + r.m_UnclosedChainCount + " vs " + unclosed;
            if (notes.Count != r.m_BuildNotes.Count)
                return "notes " + r.m_BuildNotes.Count + " vs " + notes.Count + " (" + string.Join(" | ", r.m_BuildNotes) + " ‖ " +
                       string.Join(" | ", notes) + ")";
            for (int n = 0; n < notes.Count; n++)
            {
                if (notes[n] != r.m_BuildNotes[n])
                    return "note " + n + " '" + r.m_BuildNotes[n] + "' vs '" + notes[n] + "'";
            }

            if (r.m_Pairs.Count != t.Pairs.Count)
                return "pairs " + r.m_Pairs.Count + " vs " + t.Pairs.Count;
            for (int p = 0; p < t.Pairs.Count; p++)
            {
                LoopCacheReference.PairResult rp = r.m_Pairs[p];
                LiftLoopTopology.Pair tp = t.Pairs[p];
                string at = "pair " + p + " (" + rp.Lo + "," + rp.Hi + ") ";
                if (rp.Lo != tp.StationLo || rp.Hi != tp.StationHi || rp.ChainCount != tp.ChainIndices.Count)
                    return at + "key / chain count vs (" + tp.StationLo + "," + tp.StationHi + ") " + tp.ChainIndices.Count;
                if (rp.TwoChains != (tp.Status != LiftLoopTopology.PairStatus.NotTwoChains))
                    return at + "two chains";
                if (!rp.TwoChains)
                    continue;
                if (rp.Base != tp.Base || rp.Top != tp.Top || rp.BaseByRole != tp.BaseByRole)
                    return at + "base / top " + rp.Base + "/" + rp.Top + " (by role " + rp.BaseByRole + ") vs " + tp.Base + "/" + tp.Top +
                           " (by role " + tp.BaseByRole + ")";
                if (rp.Decided != tp.UpLaneDecided || rp.Swapped != tp.UpLaneSwapped)
                    return at + "up lane decided / swapped";
                if (rp.ChainIndexA != t.Chains.IndexOf(tp.A) || rp.ChainIndexB != t.Chains.IndexOf(tp.B))
                    return at + "chain A / B identity";
                if (!SameBits(rp.TopGap, tp.TopGapM) || !SameBits(rp.BaseGap, tp.BaseGapM))
                    return at + "gaps (bits) " + rp.TopGap.ToString("R") + "/" + rp.BaseGap.ToString("R") + " vs " +
                           tp.TopGapM.ToString("R") + "/" + tp.BaseGapM.ToString("R");
                if (rp.Closed != (tp.Status == LiftLoopTopology.PairStatus.Ok))
                    return at + "closed " + rp.Closed + " vs " + tp.Status;
                if (rp.Closed && rp.TypeIndex != tp.TypeIndex)
                    return at + "type " + rp.TypeIndex + " vs " + tp.TypeIndex;
                if (tp.ABaseDock != -1 || tp.BTopDock != -1)
                    return at + "Loop mode assigned a dock";
            }

            if (refMap.Count != topoMap.Count)
                return "station map size " + refMap.Count + " vs " + topoMap.Count;
            foreach (KeyValuePair<int, int> kv in refMap)
            {
                if (!topoMap.TryGetValue(kv.Key, out int v) || v != kv.Value)
                    return "station map entry " + kv.Key;
            }
            return null;
        }

        /// <summary>Shuttle mode on a random graph: identical chain pass (diagnostics, chain notes, the map); pairs keyed and
        /// ordered as in Loop mode; Ok pairs oriented base → top from the role base, gaps in the window, docks distinct and
        /// within MaxDockRunM, refusals beyond it.</summary>
        private static string ShuttleInvariants(Graph g, LiftLoopTopology loop, LiftLoopTopology s, Dictionary<int, int> loopMap,
                                                Dictionary<int, int> shuttleMap, ref int ok, ref int docked, ref int refused, ref int byRole)
        {
            if (s.Diags.Count != loop.Diags.Count || s.UnclosedChainCount != loop.UnclosedChainCount || s.Notes.Count != loop.Notes.Count)
                return "chain pass differs from Loop mode";
            for (int c = 0; c < s.Diags.Count; c++)
            {
                LiftLoopTopology.ChainDiag a = s.Diags[c], b = loop.Diags[c];
                if (a.Links != b.Links || a.StartHow != b.StartHow || a.EndHow != b.EndHow || a.StartStation != b.StartStation ||
                    a.EndStation != b.EndStation || !SameBits(a.StartPos, b.StartPos) || !SameBits(a.EndPos, b.EndPos))
                    return "chain " + c + " diag differs from Loop mode";
            }
            for (int n = 0; n < s.Notes.Count; n++)
            {
                if (s.Notes[n] != loop.Notes[n])
                    return "note " + n + " differs from Loop mode";
            }
            if (s.Pairs.Count != loop.Pairs.Count || shuttleMap.Count != loopMap.Count)
                return "pair count / station map differs from Loop mode";
            return ShuttlePairInvariants(g, s, loop, ref ok, ref docked, ref refused, ref byRole);
        }

        /// <summary>The per-pair shuttle rules (roles, orientation, gap window, docks); loop = the Loop build of the same graph for the
        /// key / roleless-base comparisons, null when the chain pass moved (dock reach).</summary>
        private static string ShuttlePairInvariants(Graph g, LiftLoopTopology s, LiftLoopTopology loop, ref int ok, ref int docked, ref int refused, ref int byRole)
        {
            for (int p = 0; p < s.Pairs.Count; p++)
            {
                LiftLoopTopology.Pair sp = s.Pairs[p], lp = loop != null ? loop.Pairs[p] : null;
                string at = "pair " + p + " ";
                if (lp != null && (sp.StationLo != lp.StationLo || sp.StationHi != lp.StationHi || sp.ChainIndices.Count != lp.ChainIndices.Count))
                    return at + "key differs from Loop mode";
                if (sp.Status == LiftLoopTopology.PairStatus.NotTwoChains)
                    continue;
                sbyte loRole = g.Stations[sp.StationLo].Role, hiRole = g.Stations[sp.StationHi].Role;
                bool oneBase = (loRole == 0) != (hiRole == 0);
                if (sp.BaseByRole != oneBase)
                    return at + "BaseByRole " + sp.BaseByRole + " with roles " + loRole + "/" + hiRole;
                if (oneBase && g.Stations[sp.Base].Role != 0)
                    return at + "base is not the Role-0 station";
                if (lp != null && !oneBase && sp.Base != lp.Base)
                    return at + "roleless base differs from the Loop rule";
                if (sp.A.StartStation != sp.Base || sp.B.StartStation != sp.Base || sp.A.EndStation != sp.Top || sp.B.EndStation != sp.Top)
                    return at + "chains not oriented base → top";
                bool sane = LiftLoopTopology.ShuttleGapSane(sp.BaseGapM) && LiftLoopTopology.ShuttleGapSane(sp.TopGapM);
                if (sane != (sp.Status == LiftLoopTopology.PairStatus.Ok))
                    return at + "status " + sp.Status + " with gaps " + sp.BaseGapM + " / " + sp.TopGapM;
                if (sp.Status != LiftLoopTopology.PairStatus.Ok)
                    continue;
                ok++;
                if (sp.BaseByRole)
                    byRole++;
                string d = DockInvariant(g.Stations[sp.Base], sp.ABaseDock, sp.BBaseDock, sp.ABaseRunM, sp.BBaseRunM, sp.ABaseRefusedM,
                                         sp.BBaseRefusedM, ref docked, ref refused)
                           ?? DockInvariant(g.Stations[sp.Top], sp.ATopDock, sp.BTopDock, sp.ATopRunM, sp.BTopRunM, sp.ATopRefusedM,
                                            sp.BTopRefusedM, ref docked, ref refused);
                if (d != null)
                    return at + d;
            }
            return null;
        }

        /// <summary>Shuttle with ShuttleDockReach (on) against the same build without it (off): identical lanes, owners and
        /// positions; a terminal either keeps its classification bit for bit, or off had none / a position station and on has a
        /// "dock" station within MaxDockRunM of one of its docks (an owner / child terminal never moves). With no terminal moved
        /// the pairs, notes and counts are identical; with some moved, the shuttle's own invariants still hold on the new pairs.</summary>
        private static string ReachInvariants(Graph g, LiftLoopTopology off, LiftLoopTopology on, Dictionary<int, int> offMap,
                                              Dictionary<int, int> onMap, ref int reclassified)
        {
            if (on.Diags.Count != off.Diags.Count || on.Chains.Count != off.Chains.Count || onMap.Count != offMap.Count)
                return "chain count / map differs with dock reach";
            int moved = 0;
            for (int c = 0; c < on.Diags.Count; c++)
            {
                LiftLoopTopology.ChainDiag a = on.Diags[c], b = off.Diags[c];
                if (a.Links != b.Links || a.StartOwner != b.StartOwner || a.EndOwner != b.EndOwner || !SameBits(a.StartPos, b.StartPos) ||
                    !SameBits(a.EndPos, b.EndPos))
                    return "chain " + c + " walk differs with dock reach";
                for (int end = 0; end < 2; end++)
                {
                    string how = end == 0 ? a.StartHow : a.EndHow, howOff = end == 0 ? b.StartHow : b.EndHow;
                    int st = end == 0 ? a.StartStation : a.EndStation, stOff = end == 0 ? b.StartStation : b.EndStation;
                    float3 pos = end == 0 ? a.StartPos : a.EndPos;
                    if (st == stOff && (how == howOff || st < 0))   // both unclassified: only the "none (...)" text may differ
                        continue;
                    if (howOff == "owner" || howOff == "child" || !how.StartsWith("dock ") || st < 0)
                        return "chain " + c + " terminal " + end + " moved from '" + howOff + "' to '" + how + "'";
                    LiftLoopTopology.Station s = g.Stations[st];
                    float d = float.MaxValue;
                    if (math.all(math.isfinite(s.DockA)))
                        d = math.min(d, math.distance(pos, s.DockA));
                    if (math.all(math.isfinite(s.DockB)))
                        d = math.min(d, math.distance(pos, s.DockB));
                    if (!(d <= LiftLoopTopology.MaxDockRunM + 0.01f))
                        return "chain " + c + " terminal " + end + " dock station " + st + " is " + d + " m from its docks";
                    moved++;
                }
            }
            reclassified += moved;
            if (moved == 0)
            {
                if (on.UnclosedChainCount != off.UnclosedChainCount || on.Notes.Count != off.Notes.Count || on.Pairs.Count != off.Pairs.Count)
                    return "nothing moved but counts differ";
                for (int n = 0; n < on.Notes.Count; n++)
                    if (NormNone(on.Notes[n]) != NormNone(off.Notes[n]))
                        return "nothing moved but note " + n + " differs: " + on.Notes[n] + " | " + off.Notes[n];
                for (int p = 0; p < on.Pairs.Count; p++)
                    if (on.Pairs[p].Status != off.Pairs[p].Status || on.Pairs[p].StationLo != off.Pairs[p].StationLo ||
                        on.Pairs[p].StationHi != off.Pairs[p].StationHi || on.Pairs[p].Base != off.Pairs[p].Base)
                        return "nothing moved but pair " + p + " differs";
                return null;
            }
            int ok = 0, docked = 0, refused = 0, byRole = 0;
            return ShuttlePairInvariants(g, on, null, ref ok, ref docked, ref refused, ref byRole);
        }

        /// <summary>A note with the diagnostic tail of every unclassified terminal cut (dock reach adds ", nearest dock Ym" and may pick another nearest station).</summary>
        private static string NormNone(string note) =>
            System.Text.RegularExpressions.Regex.Replace(note, @"none [(]owner ([0-9]+), nearest station [0-9]+m(, nearest dock [0-9]+m)?[)]", "none (owner $1)");

        private static string DockInvariant(LiftLoopTopology.Station st, sbyte a, sbyte b, float ra, float rb, float xa, float xb,
                                            ref int docked, ref int refused)
        {
            bool finite = math.all(math.isfinite(st.DockA)) && math.all(math.isfinite(st.DockB));
            if (!finite)
                return a == -1 && b == -1 && ra == 0f && rb == 0f && xa == 0f && xb == 0f ? null : "docks assigned without docks";
            if (a >= 0 && b >= 0 && a == b)
                return "both chains on one dock";
            if ((a >= 0 && !(ra <= LiftLoopTopology.MaxDockRunM)) || (b >= 0 && !(rb <= LiftLoopTopology.MaxDockRunM)))
                return "a dock run over MaxDockRunM";
            if ((a < 0 && !(xa > LiftLoopTopology.MaxDockRunM)) || (b < 0 && !(xb > LiftLoopTopology.MaxDockRunM)))
                return "a dock refused within MaxDockRunM";
            docked += (a >= 0 ? 1 : 0) + (b >= 0 ? 1 : 0);
            refused += (a < 0 ? 1 : 0) + (b < 0 ? 1 : 0);
            return null;
        }

        private static float R(Random rng, double lo, double hi) => (float)(lo + rng.NextDouble() * (hi - lo));

        private static float3 RandomUnit(Random rng)
        {
            float3 v = new float3(R(rng, -1, 1), R(rng, -0.3, 0.3), R(rng, -1, 1));
            return math.normalizesafe(v, new float3(1f, 0f, 0f));
        }

        /// <summary>One random lift world (deterministic per seed). Stations 1-4 (random position, yaw, role, 0-2 children,
        /// docks half the time); per station pair 0-2 lifts with 1-9 spans (0-8 towers), node lanes or not, gauge from the
        /// real set or random 0-6 m (gaps 0-12 m, both sides of ArcSane and the shuttle window); terminals owned by the
        /// station, a child, a shared stub anchor, or a line node near (≤ 19.9 m) or far (20.1-60 m) from the station;
        /// missing sides and lanes; stray lanes; per-lane type faults; random reversal and order; height ties.</summary>
        private static Graph RandomGraph(Random rng, out bool tie)
        {
            tie = false;
            var g = new Graph();
            int n = rng.Next(1, 5);
            var children = new List<int>[n];
            for (int s = 0; s < n; s++)
            {
                float3 pos = new float3(R(rng, -800, 800), R(rng, 0, 500), R(rng, -800, 800));
                if (s > 0 && rng.NextDouble() < 0.1)
                {
                    pos.y = g.Stations[rng.Next(s)].Position.y;
                    tie = true;
                }
                g.Station(pos, R(rng, 0, 360), (sbyte)rng.Next(-1, 2));
                if (rng.NextDouble() < 0.5)
                    g.Docks(s, new float3(-5.5f, 16f, -5.5f), new float3(R(rng, 3, 8), 16f, R(rng, -12, 30)));
                children[s] = new List<int>();
                int kids = rng.Next(0, 3);
                for (int k = 0; k < kids; k++)
                {
                    int id = 200 + s * 10 + k;
                    g.Children[id] = s;
                    children[s].Add(id);
                }
            }
            var stubs = new Dictionary<(int, int), int>();   // (station, side sign) → a shared stub anchor
            int towerOwner = 1000, lineOwner = 5000;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    int lifts = rng.Next(0, 3);
                    for (int l = 0; l < lifts; l++)
                    {
                        float gauge;
                        double pick = rng.NextDouble();
                        if (pick < 0.03)
                            gauge = 0f;
                        else if (pick < 0.5)
                            gauge = new[] { 1.8f, 2.6f, 3.0f, 5.5f }[rng.Next(4)];
                        else
                            gauge = R(rng, 0.2, 6.0);
                        int spans = rng.Next(1, 10);
                        bool nodeLanes = rng.Next(2) == 0;
                        int type = rng.NextDouble() < 0.9 ? 0 : rng.Next(-1, 4);
                        int bi = i, ti = j;
                        if (rng.Next(2) == 0)
                            (bi, ti) = (j, i);
                        float bh = R(rng, 4, 20), th = R(rng, 4, 20);
                        for (int side = 0; side < 2; side++)
                        {
                            if (rng.NextDouble() < 0.08)
                                continue;   // a missing side
                            float sgn = side == 0 ? 1f : -1f;
                            int a = Terminal(g, rng, bi, new float3(-sgn * gauge, bh, -12f), (int)sgn, children[bi], stubs, ref lineOwner);
                            int b = Terminal(g, rng, ti, new float3(sgn * gauge, th, -12f), (int)-sgn, children[ti], stubs, ref lineOwner);
                            int before = g.Lanes.Count;
                            g.Side(bi, default, ti, default, spans, nodeLanes, towerOwner, type, a, b);
                            if (rng.NextDouble() < 0.06 && g.Lanes.Count - before > 1)
                                g.Lanes.RemoveAt(before + rng.Next(g.Lanes.Count - before));   // a missing lane
                            for (int k = before; k < g.Lanes.Count; k++)
                            {
                                if (rng.NextDouble() < 0.02)
                                {
                                    LiftLoopTopology.Lane lane = g.Lanes[k];
                                    lane.TypeIndex = rng.Next(-1, 4);
                                    g.Lanes[k] = lane;
                                }
                            }
                        }
                        towerOwner += 20;
                    }
                }
            }
            int strays = rng.Next(0, 3);
            for (int k = 0; k < strays && g.NodeIds.Count > 0; k++)
            {
                int from = g.NodeIds[rng.Next(g.NodeIds.Count)];
                int to = rng.NextDouble() < 0.5
                    ? g.NodeIds[rng.Next(g.NodeIds.Count)]
                    : g.Node(lineOwner++, g.PosOf(from) + RandomUnit(rng) * R(rng, 5, 200));
                g.Lane(from, to, rng.Next(0, 2));
            }
            if (n >= 1 && rng.NextDouble() < 0.05)
            {
                // A line from a station back to itself (both terminals one station → unclosed).
                int s = rng.Next(n);
                int a = g.Node(g.OwnerOf(s), g.Local(s, new float3(1f, 7f, -12f)));
                int b = g.Node(g.OwnerOf(s), g.Local(s, new float3(-1f, 7f, -12f)));
                g.Lane(a, g.Node(lineOwner++, g.Local(s, new float3(0f, 9f, -80f))));
                g.Lane(g.NodeIds[g.NodeIds.Count - 1], b);
            }
            for (int k = 0; k < g.Lanes.Count; k++)
            {
                if (rng.Next(2) == 0)
                    g.Reverse(k);
            }
            for (int k = g.Lanes.Count - 1; k > 0; k--)
            {
                int m = rng.Next(k + 1);
                (g.Lanes[k], g.Lanes[m]) = (g.Lanes[m], g.Lanes[k]);
            }
            return g;
        }

        /// <summary>A lift terminal at a station: the station's own anchor (shared with any earlier lift on that side 30 %
        /// of the time), a child-owned anchor, or a line-owned node near (≤ 19.9 m) or far (20.1-60 m) from the station.</summary>
        private static int Terminal(Graph g, Random rng, int station, float3 local, int sideSign, List<int> children,
                                    Dictionary<(int, int), int> stubs, ref int lineOwner)
        {
            double r = rng.NextDouble();
            if (r < 0.55)
            {
                if (stubs.TryGetValue((station, sideSign), out int shared) && rng.NextDouble() < 0.3)
                    return shared;
                int node = g.Node(g.OwnerOf(station), g.Local(station, local));
                stubs[(station, sideSign)] = node;
                return node;
            }
            if (r < 0.75 && children.Count > 0)
                return g.Node(children[rng.Next(children.Count)], g.Local(station, local));
            if (r < 0.88)
                return g.Node(lineOwner++, g.Stations[station].Position + RandomUnit(rng) * R(rng, 0, 19.9));
            if (r < 0.96)
                return g.Node(lineOwner++, g.Stations[station].Position + RandomUnit(rng) * R(rng, 20.1, 60));
            return g.Node(g.OwnerOf(station), g.Local(station, local));
        }
    }
}
