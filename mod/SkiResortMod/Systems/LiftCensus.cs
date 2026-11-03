// The PURE lift model: one row per lift, built from net topology. LiftCensusSystem harvests the ECS data into the plain
// Edge / Node / Station lists and calls Build on events; the hub (SkiResortStatsSystem / SkiResortHubUISystem) and the lift
// logs read the rows. tests/LiftBatch links this file into a net8 host and runs synthetic graphs.
// Pure file: System, System.Collections.Generic, Unity.Mathematics + SkiCatalog only (EntityRef.cs stands in for Entity).
// A station connection node is a TERMINAL, never a connector; a station's IsBase is by PREFAB, never by height.
// Revision increments whenever the row content changed; events are produced only from the second build after Reset.
// Also linked: LiftAlignMath (the plan bend at a tower) and TowerNumberTiles.Wrap (the tower number a bend row names).
// Engine notes: EngineNotes/LiftCensus.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The pure lift census.</summary>
    public sealed class LiftCensus
    {
        public enum State : byte { Complete, Open, Unfinished, StationsOnly }

        /// <summary>An edge or station the harvest could not type (defensive
        /// only: the harvest passes catalog lines and catalog stations). Never a SkiCatalog.Lifts index.</summary>
        public const byte NoType = 255;

        public struct Edge
        {
            public EntityRef Id, Start, End;
            public float3 StartPos, EndPos;
            public float LengthM;
            public int Chairs;
            public byte Type;  // SkiCatalog.Lifts index of the edge's line prefab (NoType = ignored)
        }

        /// <summary>Station: index into the station list, or −1.</summary>
        public struct Node
        {
            public EntityRef Id;
            public int Station;
            public bool HasTower;
            public float3 Position;
            // Degrees the tower's rope asks past what its sheave train follows (TrainPitchSystem: LiftAlignMath.TrainOverDeg over
            // TrainWarnDeg, the last pass); 0 = it follows, or no tower / no rope on its anchors.
            public float TrainOverDeg;
        }

        public struct Station
        {
            public EntityRef Id;
            public bool IsBase;
            public float3 Position, StubTip;
            public float2 Axis;
            public byte Type;  // StationDef.LiftTypeIndex (NoType when unknown)
        }

        public struct Row
        {
            public EntityRef Id, Base, Top;                   // Id = Base, else Top, else the lowest-index edge
            public State State;
            public int Sections, Towers, Chairs, Gaps, TowersRemoved;
            public float LengthM, RiseM, HorizontalM;         // LengthM = sum of edge Curve lengths (NOT the loop)
            public float3 FocusCenter, GapAt;
            public float FocusRadius;
            public bool HasGap;
            public bool AlignKnown;
            public float AlignOffsetM, AlignYawDeg;
            // The row's lift type (SkiCatalog.Lifts index; NoType when
            // nothing typed it) and whether its component spans two or more of our lift types (then State Unfinished).
            public byte Type;
            public bool Mixed;
            // How many split nodes (a node shared with a line of another lift type, counted apart) this row's
            // components touch. Census/DUMP only, hashed; never bound to the hub.
            public int SplitNodes;
            // The sharpest plan bend at a line tower of the row (MeasureBends): LiftAlignMath.PlanBendDeg at a node with a tower between
            // two of the row's edges, never a station connection node. BendTower = that tower's number as the tower number tiles show it
            // (from the base station; a fragment without the base counts from its dead end). 0 / Null when the row has no such tower.
            // The hub's LiftBend row reads BendWarnTenths(BendDeg); its click flies to BendAt.
            public float BendDeg;
            public int BendTower;
            public EntityRef BendNode;
            public float3 BendAt;
            // The line tower of the row (MeasureBends' walk, same numbering) whose rope goes furthest past what its sheave train follows
            // (Node.TrainOverDeg > 0): TrainOverDeg its degrees, TrainTower its number, TrainNode / TrainAt its node. 0 / Null when every
            // train follows its rope. The hub's LiftTrainLimit row reads it; its click flies to TrainAt.
            public float TrainOverDeg;
            public int TrainTower;
            public EntityRef TrainNode;
            public float3 TrainAt;
        }

        /// <summary>The hub's number for a bend: tenths of a degree, rounded UP (a bend just over LiftAlignMath.BendWarnDeg never reads
        /// 5.0); 0 when the bend is at or under BendWarnDeg (no warning: the owner's verdict is warn, never refuse).</summary>
        public static int BendWarnTenths(float bendDeg)
        {
            if (!(bendDeg > LiftAlignMath.BendWarnDeg))
                return 0;
            int floor = (int)math.round(LiftAlignMath.BendWarnDeg * 10f) + 1;
            return math.max(floor, (int)math.ceil(bendDeg * 10f - 1e-3f));
        }

        public enum EventKind : byte { SectionsDeleted, Opened, Closed, NewLine, Removed }

        public struct Event
        {
            public EventKind Kind;
            public int Row;        // index into Rows after the build; −1 = the row was dropped
            public int Count;
            public float LengthM;
            public int PrevRow;    // ADDITIVE: index into PreviousRows; −1 = the row is new
        }

        /// <summary>Smallest focus radius (a station-only row, a one-section lift), meters.</summary>
        public const float MinFocusRadiusM = 40f;

        private struct Key : IEquatable<Key>
        {
            public EntityRef Base, Top, Anchor;

            public bool Equals(Key o) => Base == o.Base && Top == o.Top && Anchor == o.Anchor;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Base.GetHashCode();
                    h = h * 31 + Top.GetHashCode();
                    h = h * 31 + Anchor.GetHashCode();
                    return h;
                }
            }
            public bool HasPair => !Base.IsNull && !Top.IsNull;
        }

        /// <summary>(node, lift type) - the union key; edges of different types at one node do not union.</summary>
        private struct NodeType : IEquatable<NodeType>
        {
            public EntityRef Node;
            public byte Type;

            public bool Equals(NodeType o) => Node == o.Node && Type == o.Type;
            public override bool Equals(object obj) => obj is NodeType k && Equals(k);
            public override int GetHashCode() => Node.GetHashCode() * 31 + Type;
        }

        /// <summary>One connected component of line edges (pooled between builds).</summary>
        private sealed class Comp
        {
            public readonly List<int> Edges = new List<int>();
            public readonly List<int> Stations = new List<int>();   // distinct station indices touched
            public int Bases, Tops;
            public EntityRef LowestEdge;
            public Key Key;
            public bool Complex;
            public uint TypeMask;  // bit Type of every typed edge and touched station
            public bool Mixed;  // popcount(TypeMask) > 1
            public int SplitNodes;  // distinct split nodes (shared with another lift type) its edges end at

            public void Reset()
            {
                Edges.Clear();
                Stations.Clear();
                Bases = Tops = 0;
                LowestEdge = default;
                Key = default;
                Complex = false;
                TypeMask = 0u;
                Mixed = false;
                SplitNodes = 0;
            }
        }

        // current build
        private readonly List<Row> m_Rows = new List<Row>();
        private readonly List<Key> m_Keys = new List<Key>();
        private Dictionary<EntityRef, int> m_EdgeRow = new Dictionary<EntityRef, int>();
        private Dictionary<EntityRef, Key> m_EdgeKey = new Dictionary<EntityRef, Key>();
        private Dictionary<EntityRef, float> m_EdgeLength = new Dictionary<EntityRef, float>();

        // previous build (swapped in at the start of Build)
        private readonly List<Row> m_PrevRows = new List<Row>();
        private readonly List<Key> m_PrevKeys = new List<Key>();
        private readonly Dictionary<Key, int> m_PrevKeyIndex = new Dictionary<Key, int>();
        private Dictionary<EntityRef, int> m_PrevEdgeRow = new Dictionary<EntityRef, int>();
        private Dictionary<EntityRef, Key> m_PrevEdgeKey = new Dictionary<EntityRef, Key>();
        private Dictionary<EntityRef, float> m_PrevEdgeLength = new Dictionary<EntityRef, float>();

        // scratch (reused)
        private readonly Dictionary<EntityRef, int> m_NodeIndex = new Dictionary<EntityRef, int>();
        private readonly Dictionary<EntityRef, int> m_StationIndex = new Dictionary<EntityRef, int>();
        private readonly Dictionary<NodeType, int> m_FirstEdgeAtNode = new Dictionary<NodeType, int>();  // per (node, type)
        private readonly Dictionary<EntityRef, uint> m_NodeTypeMask = new Dictionary<EntityRef, uint>();  // edge-type bits per non-station node
        private readonly List<EntityRef> m_SplitNodeList = new List<EntityRef>();  // nodes with > 1 edge type, sorted
        private readonly HashSet<EntityRef> m_SplitNodeSet = new HashSet<EntityRef>();
        private readonly HashSet<EntityRef> m_LoggedSplits = new HashSet<EntityRef>();  // announced since Reset
        private readonly HashSet<EntityRef> m_CompSplitScratch = new HashSet<EntityRef>();
        private readonly Dictionary<EntityRef, int> m_Degree = new Dictionary<EntityRef, int>();
        private readonly List<int> m_Parent = new List<int>();
        private readonly List<int> m_CompOfRoot = new List<int>();
        private readonly List<Comp> m_CompPool = new List<Comp>();
        private readonly List<Comp> m_Comps = new List<Comp>();
        private readonly Dictionary<Key, int> m_KeySlot = new Dictionary<Key, int>();
        private readonly List<List<Comp>> m_SlotComps = new List<List<Comp>>();
        private readonly List<Key> m_SlotKeys = new List<Key>();
        private readonly List<Row> m_SlotRows = new List<Row>();
        private readonly List<int> m_Order = new List<int>();
        private readonly HashSet<EntityRef> m_RowNodes = new HashSet<EntityRef>();
        private readonly HashSet<EntityRef> m_CurrentEdges = new HashSet<EntityRef>();
        private readonly HashSet<int> m_StationInRow = new HashSet<int>();
        private readonly HashSet<int> m_StationTouched = new HashSet<int>();
        private readonly Dictionary<Key, int> m_LostCount = new Dictionary<Key, int>();
        private readonly Dictionary<Key, float> m_LostLength = new Dictionary<Key, float>();
        private readonly List<Key> m_LostKeys = new List<Key>();

        private Comparison<int> m_CompareSlots;
        private Comparison<Key> m_ComparePrevOrder;

        private bool m_HasBaseline;
        private int m_LastRowHash;
        private int m_LastTopoHash;
        private bool m_HashValid;

        /// <summary>++ whenever the row content changed.</summary>
        public int Revision { get; private set; }

        public IReadOnlyList<Row> Rows => m_Rows;

        /// <summary>ADDITIVE: the rows of the build before the last one (Event.PrevRow indexes it).</summary>
        public IReadOnlyList<Row> PreviousRows => m_PrevRows;

        /// <summary>ADDITIVE: edges, node roles or row keys changed in the last Build.</summary>
        public bool LastBuildChangedTopology { get; private set; }

        /// <summary>ADDITIVE: a baseline build exists since the last Reset (events are produced).</summary>
        public bool HasBaseline => m_HasBaseline;

        /// <summary>ADDITIVE: build notes of the last Build (odd topologies; dev tab / dump only).</summary>
        public readonly List<string> Notes = new List<string>();

        /// <summary>ADDITIVE: one note per split node of the last Build ("lift line joined to another lift's node at
        /// (x, z): the lines render as one chain but are counted apart"). DUMP only; not part of Notes so it is not
        /// re-logged as a build note.</summary>
        public readonly List<string> SplitNotes = new List<string>();

        /// <summary>ADDITIVE: "split at node N: types A|B" for each split node not yet announced since Reset (the
        /// system logs each once per load, [census] INFO). Cleared at the start of every Build.</summary>
        public readonly List<string> NewSplits = new List<string>();

        /// <summary>ADDITIVE: forgets all history (per game load). The next Build is a silent baseline.
        /// Revision keeps counting so consumers see a change.</summary>
        public void Reset()
        {
            m_Rows.Clear();
            m_Keys.Clear();
            m_EdgeRow.Clear();
            m_EdgeKey.Clear();
            m_EdgeLength.Clear();
            m_PrevRows.Clear();
            m_PrevKeys.Clear();
            m_PrevKeyIndex.Clear();
            m_PrevEdgeRow.Clear();
            m_PrevEdgeKey.Clear();
            m_PrevEdgeLength.Clear();
            Notes.Clear();
            SplitNotes.Clear();
            NewSplits.Clear();
            m_LoggedSplits.Clear();
            m_HasBaseline = false;
            m_HashValid = false;
            LastBuildChangedTopology = false;
        }

        public bool TryGetRowOfEdge(EntityRef edge, out int row)
        {
            return m_EdgeRow.TryGetValue(edge, out row);
        }

        /// <summary>Appends the index of every row whose Base or Top is <paramref name="station"/>
        /// (row order). True when at least one was appended.</summary>
        public bool TryGetRowsOfStation(EntityRef station, List<int> rows)
        {
            bool any = false;
            if (station.IsNull)
                return false;
            for (int i = 0; i < m_Rows.Count; i++)
            {
                if (m_Rows[i].Base == station || m_Rows[i].Top == station)
                {
                    rows.Add(i);
                    any = true;
                }
            }
            return any;
        }


        public void Build(List<Edge> edges, List<Node> nodes, List<Station> stations, List<Event> events)
        {
            events.Clear();
            Notes.Clear();
            SplitNotes.Clear();
            NewSplits.Clear();
            bool baseline = m_HasBaseline;
            SwapToPrevious();

            // 1. Indices
            m_NodeIndex.Clear();
            for (int i = 0; i < nodes.Count; i++)
                m_NodeIndex[nodes[i].Id] = i;
            m_StationIndex.Clear();
            for (int i = 0; i < stations.Count; i++)
                m_StationIndex[stations[i].Id] = i;
            m_CurrentEdges.Clear();
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Type != NoType)  // an untyped edge is not a lift edge (engine notes)
                    m_CurrentEdges.Add(edges[i].Id);
            }

            // 2. Union-find through non-station nodes; degree per node. An edge with Type == NoType is never linked (it never connects
            // two components) and adds no degree (it is ignored, so it cannot make a dead end look connected).
            m_Parent.Clear();
            for (int i = 0; i < edges.Count; i++)
                m_Parent.Add(i);
            m_FirstEdgeAtNode.Clear();
            m_Degree.Clear();
            m_NodeTypeMask.Clear();
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Type == NoType)
                    continue;
                LinkEndpoint(i, edges[i].Start, edges[i].Type, nodes, stations.Count);
                LinkEndpoint(i, edges[i].End, edges[i].Type, nodes, stations.Count);
            }

            // A non-station node carrying edges of two or more lift types is a SPLIT point (LinkEndpoint kept the
            // types apart). Sorted by entity so notes and logs are deterministic.
            m_SplitNodeList.Clear();
            m_SplitNodeSet.Clear();
            foreach (KeyValuePair<EntityRef, uint> kv in m_NodeTypeMask)
            {
                if (math.countbits(kv.Value) > 1)
                    m_SplitNodeList.Add(kv.Key);
            }
            m_SplitNodeList.Sort();
            for (int i = 0; i < m_SplitNodeList.Count; i++)
            {
                EntityRef sn = m_SplitNodeList[i];
                m_SplitNodeSet.Add(sn);
                string pos = "";
                if (m_NodeIndex.TryGetValue(sn, out int sni))
                    pos = " at (" + nodes[sni].Position.x.ToString("F0") + ", " + nodes[sni].Position.z.ToString("F0") + ")";
                string types = TypeIds(m_NodeTypeMask[sn], "|");
                SplitNotes.Add("lift line joined to another lift's node " + sn + pos + " (" + types.Replace("|", " and ") +
                               "): the lines render as one chain but are counted apart");
                if (m_LoggedSplits.Add(sn))
                    NewSplits.Add("split at node " + sn + ": types " + types);
            }

            // 3. Components
            m_Comps.Clear();
            m_CompOfRoot.Clear();
            for (int i = 0; i < edges.Count; i++)
                m_CompOfRoot.Add(-1);
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Type == NoType)
                {
                    // Defensive (the harvest only passes catalog lines); one note per such edge.
                    Notes.Add("edge " + edges[i].Id + " has no lift type — ignored");
                    continue;
                }
                int root = Find(i);
                int ci = m_CompOfRoot[root];
                Comp comp;
                if (ci < 0)
                {
                    comp = RentComp(m_Comps.Count);
                    m_CompOfRoot[root] = m_Comps.Count;
                    m_Comps.Add(comp);
                }
                else
                {
                    comp = m_Comps[ci];
                }
                comp.Edges.Add(i);
                if (comp.LowestEdge.IsNull || edges[i].Id.CompareTo(comp.LowestEdge) < 0)
                    comp.LowestEdge = edges[i].Id;
                comp.TypeMask |= TypeBit(edges[i].Type);
                TouchStation(comp, StationOf(edges[i].Start, nodes, stations.Count), stations);
                TouchStation(comp, StationOf(edges[i].End, nodes, stations.Count), stations);
            }

            // Per component, the distinct split nodes its edges end at (Row.SplitNodes).
            if (m_SplitNodeSet.Count > 0)
            {
                for (int c = 0; c < m_Comps.Count; c++)
                {
                    Comp comp = m_Comps[c];
                    m_CompSplitScratch.Clear();
                    for (int k = 0; k < comp.Edges.Count; k++)
                    {
                        Edge e = edges[comp.Edges[k]];
                        if (m_SplitNodeSet.Contains(e.Start)) m_CompSplitScratch.Add(e.Start);
                        if (m_SplitNodeSet.Contains(e.End)) m_CompSplitScratch.Add(e.End);
                    }
                    comp.SplitNodes = m_CompSplitScratch.Count;
                }
            }

            // 3b. A component whose edges and touched stations span two or more of our lift types is MIXED. Its EDGES are one type
            // (split nodes), so this fires only when a touched station's type differs from the edges' type or from another touched
            // station's: Complex (→ Unfinished in MakeRow), one note, every row built from it Mixed.
            for (int c = 0; c < m_Comps.Count; c++)
            {
                Comp comp = m_Comps[c];
                if (math.countbits(comp.TypeMask) <= 1)
                    continue;
                comp.Mixed = true;
                comp.Complex = true;
                Notes.Add("component of " + comp.Edges.Count + " edge(s) from " + comp.LowestEdge + " mixes lift types (" +
                          TypeIds(comp.TypeMask) + ") — Unfinished");
            }

            // 4. Attribution (rules a-d)
            m_KeySlot.Clear();
            m_SlotKeys.Clear();
            for (int s = 0; s < m_SlotComps.Count; s++)
                m_SlotComps[s].Clear();
            for (int c = 0; c < m_Comps.Count; c++)
            {
                Comp comp = m_Comps[c];
                comp.Key = Attribute(comp, edges, stations);
                if (!m_KeySlot.TryGetValue(comp.Key, out int slot))
                {
                    slot = m_SlotKeys.Count;
                    m_KeySlot[comp.Key] = slot;
                    m_SlotKeys.Add(comp.Key);
                    if (m_SlotComps.Count <= slot)
                        m_SlotComps.Add(new List<Comp>());
                }
                m_SlotComps[slot].Add(comp);
            }

            // 5. Rows
            // Slots [0, compSlots) carry components; StationsOnly slots are appended after them.
            int compSlots = m_SlotKeys.Count;
            m_SlotRows.Clear();
            m_StationInRow.Clear();
            for (int s = 0; s < compSlots; s++)
            {
                Key key = m_SlotKeys[s];
                if (!key.Base.IsNull && m_StationIndex.TryGetValue(key.Base, out int bi)) m_StationInRow.Add(bi);
                if (!key.Top.IsNull && m_StationIndex.TryGetValue(key.Top, out int ti)) m_StationInRow.Add(ti);
                m_SlotRows.Add(MakeRow(key, m_SlotComps[s], edges, nodes, stations));
            }
            // StationsOnly: a station touched by no component and in no row's key.
            m_StationTouched.Clear();
            for (int c = 0; c < m_Comps.Count; c++)
                for (int k = 0; k < m_Comps[c].Stations.Count; k++)
                    m_StationTouched.Add(m_Comps[c].Stations[k]);
            for (int i = 0; i < stations.Count; i++)
            {
                if (m_StationTouched.Contains(i) || m_StationInRow.Contains(i))
                    continue;
                Station st = stations[i];
                var key = new Key { Base = st.IsBase ? st.Id : default, Top = st.IsBase ? default : st.Id };
                if (m_KeySlot.ContainsKey(key))
                    continue;   // defensive: cannot collide (anchor-less single-station keys are only made here)
                m_KeySlot[key] = m_SlotKeys.Count;
                m_SlotKeys.Add(key);
                m_SlotRows.Add(new Row
                {
                    Id = st.Id,
                    Base = key.Base,
                    Top = key.Top,
                    State = State.StationsOnly,
                    FocusCenter = st.Position,
                    GapAt = st.Position,
                    FocusRadius = MinFocusRadiusM,
                    Type = st.Type,  // a StationsOnly row's type is its station's
                });
            }

            // 6. Stable order: Id, then Top, then anchor
            m_Order.Clear();
            for (int i = 0; i < m_SlotRows.Count; i++)
                m_Order.Add(i);
            if (m_CompareSlots == null)
                m_CompareSlots = CompareSlots;   // cached: a method-group conversion allocates
            m_Order.Sort(m_CompareSlots);
            m_Rows.Clear();
            m_Keys.Clear();
            m_EdgeRow.Clear();
            m_EdgeKey.Clear();
            m_EdgeLength.Clear();
            for (int r = 0; r < m_Order.Count; r++)
            {
                int slot = m_Order[r];
                m_Rows.Add(m_SlotRows[slot]);
                m_Keys.Add(m_SlotKeys[slot]);
                if (slot < compSlots)
                {
                    List<Comp> comps = m_SlotComps[slot];
                    for (int c = 0; c < comps.Count; c++)
                    {
                        for (int k = 0; k < comps[c].Edges.Count; k++)
                        {
                            Edge e = edges[comps[c].Edges[k]];
                            m_EdgeRow[e.Id] = r;
                            m_EdgeKey[e.Id] = m_SlotKeys[slot];
                            m_EdgeLength[e.Id] = e.LengthM;
                        }
                    }
                }
            }

            // 7. Events (never on the baseline build)
            if (baseline)
                DiffEvents(events);

            // 8. Revision + topology
            int rowHash = HashRows();
            int topoHash = HashTopology(edges, nodes);
            if (!m_HashValid || rowHash != m_LastRowHash)
                Revision++;
            LastBuildChangedTopology = !m_HashValid || topoHash != m_LastTopoHash;
            m_LastRowHash = rowHash;
            m_LastTopoHash = topoHash;
            m_HashValid = true;
            m_HasBaseline = true;
        }

        private void SwapToPrevious()
        {
            m_PrevRows.Clear();
            m_PrevRows.AddRange(m_Rows);
            m_PrevKeys.Clear();
            m_PrevKeys.AddRange(m_Keys);
            m_PrevKeyIndex.Clear();
            for (int i = 0; i < m_PrevKeys.Count; i++)
                m_PrevKeyIndex[m_PrevKeys[i]] = i;
            (m_PrevEdgeRow, m_EdgeRow) = (m_EdgeRow, m_PrevEdgeRow);
            (m_PrevEdgeKey, m_EdgeKey) = (m_EdgeKey, m_PrevEdgeKey);
            (m_PrevEdgeLength, m_EdgeLength) = (m_EdgeLength, m_PrevEdgeLength);
            m_EdgeRow.Clear();
            m_EdgeKey.Clear();
            m_EdgeLength.Clear();
        }

        private Comp RentComp(int index)
        {
            Comp comp;
            if (index < m_CompPool.Count)
            {
                comp = m_CompPool[index];
            }
            else
            {
                comp = new Comp();
                m_CompPool.Add(comp);
            }
            comp.Reset();
            return comp;
        }

        private int Find(int i)
        {
            while (m_Parent[i] != i)
            {
                m_Parent[i] = m_Parent[m_Parent[i]];
                i = m_Parent[i];
            }
            return i;
        }

        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb)
                m_Parent[math.max(ra, rb)] = math.min(ra, rb);
        }

        private void LinkEndpoint(int edge, EntityRef node, byte type, List<Node> nodes, int stationCount)
        {
            m_Degree.TryGetValue(node, out int deg);
            m_Degree[node] = deg + 1;
            if (StationOf(node, nodes, stationCount) >= 0)
                return;   // a station connection node is a terminal, never a connector
            // Only edges of the SAME type union at a node; another type's edges there are a separate chain.
            m_NodeTypeMask.TryGetValue(node, out uint mask);
            m_NodeTypeMask[node] = mask | TypeBit(type);
            var key = new NodeType { Node = node, Type = type };
            if (m_FirstEdgeAtNode.TryGetValue(key, out int first))
                Union(edge, first);
            else
                m_FirstEdgeAtNode[key] = edge;
        }

        private int StationOf(EntityRef node, List<Node> nodes, int stationCount)
        {
            if (m_NodeIndex.TryGetValue(node, out int ni))
            {
                int s = nodes[ni].Station;
                return s >= 0 && s < stationCount ? s : -1;
            }
            return -1;
        }

        private static void TouchStation(Comp comp, int station, List<Station> stations)
        {
            if (station < 0 || comp.Stations.Contains(station))
                return;
            comp.Stations.Add(station);
            if (stations[station].IsBase) comp.Bases++;
            else comp.Tops++;
            comp.TypeMask |= TypeBit(stations[station].Type);  // stations count for Mixed
        }

        /// <summary>Bit <paramref name="type"/> of a component's TypeMask; 0 for NoType (and any index
        /// past 31 — SkiCatalog.Lifts holds 4 rows, APPEND ONLY).</summary>
        private static uint TypeBit(byte type)
        {
            return type < 32 ? 1u << type : 0u;
        }

        /// <summary>"fixedGrip+detachable" — the type ids of a mask in index order (SkiCatalog
        /// LiftTypeDef.Id; "type N" for an index the catalog does not know). Allocates: build notes only.</summary>
        private static string TypeIds(uint mask, string sep = "+")
        {
            string ids = "";
            for (int t = 0; t < 32; t++)
            {
                if ((mask & (1u << t)) == 0)
                    continue;
                LiftTypeDef def = SkiCatalog.LiftByIndex(t);
                ids += (ids.Length > 0 ? sep : "") + (def != null ? def.Id : "type " + t);
            }
            return ids;
        }

        private Key Attribute(Comp comp, List<Edge> edges, List<Station> stations)
        {
            // a. exactly one base and one top.
            if (comp.Bases == 1 && comp.Tops == 1)
            {
                EntityRef b = default, t = default;
                for (int k = 0; k < comp.Stations.Count; k++)
                {
                    Station st = stations[comp.Stations[k]];
                    if (st.IsBase) b = st.Id;
                    else t = st.Id;
                }
                return new Key { Base = b, Top = t };
            }

            // > 2 stations, or two of one kind: Unfinished with a build note.
            if (comp.Bases > 1 || comp.Tops > 1)
            {
                comp.Complex = true;
                Notes.Add("component of " + comp.Edges.Count + " edge(s) from " + comp.LowestEdge +
                          " touches " + comp.Bases + " base and " + comp.Tops + " top station(s) — Unfinished");
                return new Key { Anchor = comp.LowestEdge };
            }

            // b. the surviving edges carried exactly one key covering the stations touched — even when one
            //    of its stations is gone (engine notes rule b).
            if (TryCarriedKey(comp, edges, out Key carried) && TouchedWithin(comp, carried, stations))
                return carried;

            // c. one station S, and an OPEN previous row with S and a living partner.
            if (comp.Stations.Count == 1 && TryRedrawKey(stations[comp.Stations[0]].Id, out Key redraw))
                return redraw;

            // d. a new row.
            if (comp.Stations.Count == 1)
            {
                Station st = stations[comp.Stations[0]];
                return st.IsBase
                    ? new Key { Base = st.Id, Anchor = comp.LowestEdge }
                    : new Key { Top = st.Id, Anchor = comp.LowestEdge };
            }
            return new Key { Anchor = comp.LowestEdge };
        }

        private bool TryCarriedKey(Comp comp, List<Edge> edges, out Key key)
        {
            key = default;
            bool found = false;
            for (int k = 0; k < comp.Edges.Count; k++)
            {
                if (!m_PrevEdgeKey.TryGetValue(edges[comp.Edges[k]].Id, out Key prev))
                    continue;
                if (!found)
                {
                    key = prev;
                    found = true;
                }
                else if (!prev.Equals(key))
                {
                    return false;   // two keys: not a fragment of one row
                }
            }
            return found;
        }

        private static bool TouchedWithin(Comp comp, Key key, List<Station> stations)
        {
            for (int k = 0; k < comp.Stations.Count; k++)
            {
                EntityRef id = stations[comp.Stations[k]].Id;
                if (id != key.Base && id != key.Top)
                    return false;
            }
            return true;
        }

        private bool TryRedrawKey(EntityRef station, out Key key)
        {
            for (int i = 0; i < m_PrevKeys.Count; i++)
            {
                Key k = m_PrevKeys[i];
                if (!k.HasPair || m_PrevRows[i].State != State.Open)
                    continue;
                EntityRef other;
                if (k.Base == station) other = k.Top;
                else if (k.Top == station) other = k.Base;
                else continue;
                if (!m_StationIndex.ContainsKey(other))
                    continue;
                key = k;
                return true;
            }
            key = default;
            return false;
        }

        private Row MakeRow(Key key, List<Comp> comps, List<Edge> edges, List<Node> nodes, List<Station> stations)
        {
            var row = new Row { Base = key.Base, Top = key.Top };
            int baseIdx = -1, topIdx = -1;
            bool hasBase = !key.Base.IsNull && m_StationIndex.TryGetValue(key.Base, out baseIdx);
            bool hasTop = !key.Top.IsNull && m_StationIndex.TryGetValue(key.Top, out topIdx);

            bool complex = false, touchedBase = false, touchedTop = false, mixed = false;
            int bothTouching = 0, splitNodes = 0;
            EntityRef lowest = default;
            byte lowestType = NoType;  // the lowest-index edge's type (Row.Type's last fallback)
            float3 bmin = new float3(float.MaxValue), bmax = new float3(float.MinValue);
            float chordXZ = 0f;
            m_RowNodes.Clear();

            for (int c = 0; c < comps.Count; c++)
            {
                Comp comp = comps[c];
                complex |= comp.Complex;
                mixed |= comp.Mixed;
                splitNodes += comp.SplitNodes;
                bool tb = hasBase && comp.Stations.Contains(baseIdx);
                bool tt = hasTop && comp.Stations.Contains(topIdx);
                touchedBase |= tb;
                touchedTop |= tt;
                if (tb && tt) bothTouching++;
                for (int k = 0; k < comp.Edges.Count; k++)
                {
                    Edge e = edges[comp.Edges[k]];
                    row.Sections++;
                    row.LengthM += e.LengthM;
                    row.Chairs += e.Chairs;
                    chordXZ += math.length(e.EndPos.xz - e.StartPos.xz);
                    if (lowest.IsNull || e.Id.CompareTo(lowest) < 0)
                    {
                        lowest = e.Id;
                        lowestType = e.Type;
                    }
                    Grow(ref bmin, ref bmax, e.StartPos);
                    Grow(ref bmin, ref bmax, e.EndPos);
                    CountNode(ref row, e.Start, nodes);
                    CountNode(ref row, e.End, nodes);
                }
            }
            if (hasBase) Grow(ref bmin, ref bmax, stations[baseIdx].StubTip);
            if (hasTop) Grow(ref bmin, ref bmax, stations[topIdx].StubTip);

            int fragments = comps.Count;
            if (bothTouching > 1)
            {
                complex = true;
                Notes.Add(bothTouching + " complete lines between " + key.Base + " and " + key.Top + " — Unfinished");
            }

            // State
            if (complex)
                row.State = State.Unfinished;
            else if (key.HasPair)
                row.State = fragments == 1 && touchedBase && touchedTop ? State.Complete : State.Open;
            else
                row.State = State.Unfinished;

            // Gaps
            if (row.State != State.Complete)
            {
                int gaps = fragments - 1;
                if (!key.Base.IsNull && !touchedBase) gaps++;
                if (!key.Top.IsNull && !touchedTop) gaps++;
                row.Gaps = math.max(0, gaps);
            }
            row.HasGap = row.Gaps > 0;

            // Id: the live base, else the live top, else the lowest edge (engine notes; a key may hold a
            // removed station).
            row.Id = hasBase ? key.Base : hasTop ? key.Top : lowest;

            // The row's type follows its Id's rule — the live base station's
            // type, else the live top's, else the lowest-index edge's; Mixed when any of its components is.
            row.Type = hasBase ? stations[baseIdx].Type : hasTop ? stations[topIdx].Type : lowestType;
            row.Mixed = mixed;
            row.SplitNodes = splitNodes;

            // Focus
            if (bmin.x <= bmax.x)
            {
                row.FocusCenter = (bmin + bmax) * 0.5f;
                row.FocusRadius = math.max(MinFocusRadiusM, math.length(bmax - bmin) * 0.5f);
            }
            else
            {
                row.FocusCenter = hasBase ? stations[baseIdx].Position : hasTop ? stations[topIdx].Position : float3.zero;
                row.FocusRadius = MinFocusRadiusM;
            }

            // GapAt: the dead end nearest the base (else the top, else the lowest edge's start).
            float3 reference = hasBase ? stations[baseIdx].StubTip
                : hasTop ? stations[topIdx].StubTip
                : FirstEdgeStart(comps, edges);
            row.GapAt = row.FocusCenter;
            float best = float.MaxValue;
            EntityRef bestNode = default;
            foreach (EntityRef n in m_RowNodes)
            {
                if (!m_NodeIndex.TryGetValue(n, out int ni) || nodes[ni].Station >= 0)
                    continue;
                if (!m_Degree.TryGetValue(n, out int deg) || deg != 1)
                    continue;
                float d = math.distancesq(nodes[ni].Position.xz, reference.xz);
                // Ties broken by entity order: HashSet iteration order is not part of the contract.
                if (d < best || (d == best && n.CompareTo(bestNode) < 0))
                {
                    best = d;
                    bestNode = n;
                    row.GapAt = nodes[ni].Position;
                }
            }

            // Plan bends at the line towers, per component (the census row decides; the hub only reads the number).
            for (int c = 0; c < comps.Count; c++)
                MeasureBends(comps[c], edges, nodes, stations.Count, hasBase ? baseIdx : -1, reference, ref row);

            // Rise / horizontal / alignment
            if (hasBase && hasTop)
            {
                Station b = stations[baseIdx], t = stations[topIdx];
                row.RiseM = t.StubTip.y - b.StubTip.y;
                row.HorizontalM = math.length(t.StubTip.xz - b.StubTip.xz);
                StationAlign.Result r = StationAlign.Measure(b.StubTip, b.Axis, t.StubTip, t.Axis);
                row.AlignKnown = r.Valid;
                row.AlignOffsetM = r.LateralM;
                row.AlignYawDeg = r.YawDeg;
            }
            else
            {
                row.RiseM = bmin.x <= bmax.x ? bmax.y - bmin.y : 0f;
                row.HorizontalM = chordXZ;
            }
            return row;
        }

        private struct NodeEdges
        {
            public int E0, E1, Count;   // the first two of the component's edges at the node, and how many there are
            public float3 Pos;          // the node's position as an edge end
        }

        private readonly Dictionary<EntityRef, NodeEdges> m_BendAdj = new Dictionary<EntityRef, NodeEdges>();

        /// <summary>Walks one component of a row in the tower number tiles' order (TowerNumberTileSystem.NumberPath) and keeps its sharpest
        /// plan bend at a line tower in <paramref name="row"/> when it beats the row's so far (ties: the first found). Start: the base
        /// station's connection node; a component without the base starts at its dead end (a non-station node with one edge) nearest
        /// <paramref name="reference"/>, so a fragment holding the top counts from its far end like the tiles. A node with three or more of
        /// the component's edges ends the walk (a lift line is a chain; nodes past a branch are not measured). Bend = LiftAlignMath.PlanBendDeg
        /// of the previous node, the tower node and the next node, measured only at a non-station node with a tower and exactly two of the
        /// component's edges; BendTower = TowerNumberTiles.Wrap(rank), rank = the towers walked so far. No allocation once grown.</summary>
        private void MeasureBends(Comp comp, List<Edge> edges, List<Node> nodes, int stationCount, int baseIdx, float3 reference, ref Row row)
        {
            m_BendAdj.Clear();
            for (int k = 0; k < comp.Edges.Count; k++)
            {
                int ei = comp.Edges[k];
                AddBendEnd(edges[ei].Start, ei, edges[ei].StartPos);
                AddBendEnd(edges[ei].End, ei, edges[ei].EndPos);
            }

            // The start node.
            EntityRef start = default;
            bool found = false, startIsBase = false;
            float best = float.MaxValue;
            foreach (KeyValuePair<EntityRef, NodeEdges> kv in m_BendAdj)
            {
                int st = StationOf(kv.Key, nodes, stationCount);
                bool isBase = baseIdx >= 0 && st == baseIdx;
                if (isBase)
                {
                    // The base's connection node wins over any dead end (lowest entity if the base has several).
                    if (!startIsBase || kv.Key.CompareTo(start) < 0)
                    {
                        start = kv.Key;
                        startIsBase = true;
                        found = true;
                    }
                    continue;
                }
                if (startIsBase || st >= 0 || kv.Value.Count != 1)
                    continue;
                float d = math.distancesq(kv.Value.Pos.xz, reference.xz);
                if (!found || d < best || (d == best && kv.Key.CompareTo(start) < 0))
                {
                    best = d;
                    start = kv.Key;
                    found = true;
                }
            }
            if (!found)
                return;   // a closed ring of line edges with no station and no dead end: nothing to number from

            EntityRef cur = start;
            float3 prevPos = default;
            int prevEdge = -1, rank = 0;
            for (int step = 0; step <= comp.Edges.Count; step++)
            {
                if (!m_BendAdj.TryGetValue(cur, out NodeEdges ne))
                    break;
                if (prevEdge >= 0 && ne.Count > 2)
                    break;   // a branch
                int next = ne.E0 != prevEdge ? ne.E0 : ne.Count >= 2 ? ne.E1 : -1;
                if (next < 0 || next == prevEdge)
                    break;   // the far end
                EntityRef nextNode = edges[next].Start == cur ? edges[next].End : edges[next].Start;
                if (nextNode == cur || !m_BendAdj.TryGetValue(nextNode, out NodeEdges nn))
                    break;
                if (prevEdge >= 0 && ne.Count == 2 && m_NodeIndex.TryGetValue(cur, out int ni) && nodes[ni].Station < 0 &&
                    nodes[ni].HasTower)
                {
                    rank++;
                    float over = nodes[ni].TrainOverDeg;
                    if (over > row.TrainOverDeg)
                    {
                        row.TrainOverDeg = over;
                        row.TrainTower = TowerNumberTiles.Wrap(rank);
                        row.TrainNode = cur;
                        row.TrainAt = ne.Pos;
                    }
                    float bend = LiftAlignMath.PlanBendDeg(prevPos, ne.Pos, nn.Pos);
                    if (bend > row.BendDeg)
                    {
                        row.BendDeg = bend;
                        row.BendTower = TowerNumberTiles.Wrap(rank);
                        row.BendNode = cur;
                        row.BendAt = ne.Pos;
                    }
                }
                prevPos = ne.Pos;
                prevEdge = next;
                cur = nextNode;
            }
        }

        private void AddBendEnd(EntityRef node, int edge, float3 pos)
        {
            if (m_BendAdj.TryGetValue(node, out NodeEdges ne))
            {
                if (ne.Count == 1)
                    ne.E1 = edge;
                ne.Count++;
            }
            else
            {
                ne = new NodeEdges { E0 = edge, E1 = -1, Count = 1, Pos = pos };
            }
            m_BendAdj[node] = ne;
        }

        private void CountNode(ref Row row, EntityRef node, List<Node> nodes)
        {
            if (!m_RowNodes.Add(node))
                return;
            if (!m_NodeIndex.TryGetValue(node, out int ni))
                return;
            Node n = nodes[ni];
            if (n.HasTower)
                row.Towers++;
            else if (n.Station < 0)
                row.TowersRemoved++;
        }

        private static float3 FirstEdgeStart(List<Comp> comps, List<Edge> edges)
        {
            EntityRef lowest = default;
            float3 pos = float3.zero;
            for (int c = 0; c < comps.Count; c++)
                for (int k = 0; k < comps[c].Edges.Count; k++)
                {
                    Edge e = edges[comps[c].Edges[k]];
                    if (lowest.IsNull || e.Id.CompareTo(lowest) < 0)
                    {
                        lowest = e.Id;
                        pos = e.StartPos;
                    }
                }
            return pos;
        }

        private static void Grow(ref float3 bmin, ref float3 bmax, float3 p)
        {
            bmin = math.min(bmin, p);
            bmax = math.max(bmax, p);
        }

        private int CompareSlots(int a, int b)
        {
            Row ra = m_SlotRows[a], rb = m_SlotRows[b];
            int c = ra.Id.CompareTo(rb.Id);
            if (c != 0) return c;
            c = ra.Top.CompareTo(rb.Top);
            if (c != 0) return c;
            c = m_SlotKeys[a].Anchor.CompareTo(m_SlotKeys[b].Anchor);
            if (c != 0) return c;
            return a.CompareTo(b);
        }

        private void DiffEvents(List<Event> events)
        {
            // SectionsDeleted: previous edges that are gone, per previous key.
            m_LostCount.Clear();
            m_LostLength.Clear();
            m_LostKeys.Clear();
            foreach (var kv in m_PrevEdgeKey)
            {
                if (m_CurrentEdges.Contains(kv.Key))
                    continue;
                if (!m_LostCount.TryGetValue(kv.Value, out int n))
                {
                    n = 0;
                    m_LostKeys.Add(kv.Value);
                }
                m_LostCount[kv.Value] = n + 1;
                m_PrevEdgeLength.TryGetValue(kv.Key, out float len);
                m_LostLength.TryGetValue(kv.Value, out float sum);
                m_LostLength[kv.Value] = sum + len;
            }
            // Deterministic order: previous row order (cached delegate, see m_CompareSlots).
            if (m_ComparePrevOrder == null)
                m_ComparePrevOrder = (x, y) => PrevIndexOf(x).CompareTo(PrevIndexOf(y));
            m_LostKeys.Sort(m_ComparePrevOrder);
            for (int i = 0; i < m_LostKeys.Count; i++)
            {
                Key k = m_LostKeys[i];
                int now = IndexOfKey(k);
                if (now >= 0 ? GainedEdges(now) > 0 : ReplacedByNewRow(k))
                    continue;   // an edit that replaced edges (redraw), not a deletion
                events.Add(new Event
                {
                    Kind = EventKind.SectionsDeleted,
                    Row = now,
                    PrevRow = PrevIndexOf(k),
                    Count = m_LostCount[k],
                    LengthM = m_LostLength[k],
                });
            }

            // Opened / Closed / NewLine, in row order.
            for (int r = 0; r < m_Rows.Count; r++)
            {
                Row row = m_Rows[r];
                int p = PrevIndexOf(m_Keys[r]);
                bool wasComplete = p >= 0 && m_PrevRows[p].State == State.Complete;
                if (row.State == State.Complete && !wasComplete)
                {
                    events.Add(new Event
                    {
                        Kind = EventKind.Closed,
                        Row = r,
                        PrevRow = p,
                        Count = p >= 0 && m_PrevRows[p].State == State.Open ? 1 : 0,
                        LengthM = row.LengthM,
                    });
                }
                else if (row.State == State.Open && wasComplete)
                {
                    events.Add(new Event { Kind = EventKind.Opened, Row = r, PrevRow = p, Count = row.Gaps, LengthM = row.LengthM });
                }
                else if (row.State == State.Unfinished && p < 0 && row.Sections > 0)
                {
                    events.Add(new Event { Kind = EventKind.NewLine, Row = r, PrevRow = -1, Count = row.Sections, LengthM = row.LengthM });
                }
            }
        }

        private int PrevIndexOf(Key k)
        {
            return m_PrevKeyIndex.TryGetValue(k, out int i) ? i : -1;
        }

        private int IndexOfKey(Key k)
        {
            for (int i = 0; i < m_Keys.Count; i++)
                if (m_Keys[i].Equals(k))
                    return i;
            return -1;
        }

        /// <summary>Edges of row <paramref name="row"/> that did not exist at the previous build.</summary>
        private int GainedEdges(int row)
        {
            int n = 0;
            foreach (var kv in m_EdgeRow)
            {
                if (kv.Value == row && !m_PrevEdgeKey.ContainsKey(kv.Key))
                    n++;
            }
            return n;
        }

        /// <summary>A dropped key whose stations now belong to a NEW row made of new edges: the line was
        /// replaced (e.g. an unfinished line closing onto its top while the game recreated its edges),
        /// not deleted.</summary>
        private bool ReplacedByNewRow(Key dropped)
        {
            for (int r = 0; r < m_Rows.Count; r++)
            {
                if (PrevIndexOf(m_Keys[r]) >= 0)
                    continue;
                Row row = m_Rows[r];
                bool shares = (!dropped.Base.IsNull && (row.Base == dropped.Base || row.Top == dropped.Base)) ||
                              (!dropped.Top.IsNull && (row.Base == dropped.Top || row.Top == dropped.Top));
                if (shares && GainedEdges(r) > 0)
                    return true;
            }
            return false;
        }

        // Hashes: FNV-1a over 32-bit words; order-independent sum for topology
        private int HashRows()
        {
            uint h = 2166136261u;
            h = Mix(h, (uint)m_Rows.Count);
            for (int i = 0; i < m_Rows.Count; i++)
            {
                Row r = m_Rows[i];
                h = Mix(h, (uint)r.Id.Index); h = Mix(h, (uint)r.Id.Version);
                h = Mix(h, (uint)r.Base.Index); h = Mix(h, (uint)r.Base.Version);
                h = Mix(h, (uint)r.Top.Index); h = Mix(h, (uint)r.Top.Version);
                h = Mix(h, (uint)r.State);
                h = Mix(h, (uint)r.Sections); h = Mix(h, (uint)r.Towers); h = Mix(h, (uint)r.Chairs);
                h = Mix(h, (uint)r.Gaps); h = Mix(h, (uint)r.TowersRemoved);
                h = Mix(h, Bits(r.LengthM)); h = Mix(h, Bits(r.RiseM)); h = Mix(h, Bits(r.HorizontalM));
                h = Mix(h, Bits(r.FocusCenter.x)); h = Mix(h, Bits(r.FocusCenter.y)); h = Mix(h, Bits(r.FocusCenter.z));
                h = Mix(h, Bits(r.GapAt.x)); h = Mix(h, Bits(r.GapAt.y)); h = Mix(h, Bits(r.GapAt.z));
                h = Mix(h, Bits(r.FocusRadius));
                h = Mix(h, r.HasGap ? 1u : 0u); h = Mix(h, r.AlignKnown ? 1u : 0u);
                h = Mix(h, Bits(r.AlignOffsetM)); h = Mix(h, Bits(r.AlignYawDeg));
                h = Mix(h, r.Type); h = Mix(h, r.Mixed ? 1u : 0u);
                h = Mix(h, (uint)r.SplitNodes);
                h = Mix(h, Bits(r.BendDeg)); h = Mix(h, (uint)r.BendTower);
                h = Mix(h, (uint)r.BendNode.Index); h = Mix(h, (uint)r.BendNode.Version);
                h = Mix(h, Bits(r.BendAt.x)); h = Mix(h, Bits(r.BendAt.y)); h = Mix(h, Bits(r.BendAt.z));
                h = Mix(h, Bits(r.TrainOverDeg)); h = Mix(h, (uint)r.TrainTower);
                h = Mix(h, (uint)r.TrainNode.Index); h = Mix(h, (uint)r.TrainNode.Version);
                h = Mix(h, Bits(r.TrainAt.x)); h = Mix(h, Bits(r.TrainAt.y)); h = Mix(h, Bits(r.TrainAt.z));
            }
            return (int)h;
        }

        private int HashTopology(List<Edge> edges, List<Node> nodes)
        {
            unchecked
            {
                uint sum = (uint)edges.Count * 2654435761u;
                for (int i = 0; i < edges.Count; i++)
                {
                    uint h = 2166136261u;
                    h = Mix(h, (uint)edges[i].Id.Index); h = Mix(h, (uint)edges[i].Id.Version);
                    h = Mix(h, (uint)edges[i].Start.Index); h = Mix(h, (uint)edges[i].Start.Version);
                    h = Mix(h, (uint)edges[i].End.Index); h = Mix(h, (uint)edges[i].End.Version);
                    h = Mix(h, edges[i].Type);  // a type change is a topology change
                    sum += h;
                }
                for (int i = 0; i < nodes.Count; i++)
                {
                    uint h = 0x811C9DC5u ^ 0x5bd1e995u;
                    h = Mix(h, (uint)nodes[i].Id.Index); h = Mix(h, (uint)nodes[i].Id.Version);
                    h = Mix(h, (uint)nodes[i].Station);
                    sum += h * 31u;
                }
                for (int i = 0; i < m_Keys.Count; i++)
                {
                    Key k = m_Keys[i];
                    uint h = 0xC2B2AE35u;
                    h = Mix(h, (uint)k.Base.Index); h = Mix(h, (uint)k.Base.Version);
                    h = Mix(h, (uint)k.Top.Index); h = Mix(h, (uint)k.Top.Version);
                    h = Mix(h, (uint)k.Anchor.Index); h = Mix(h, (uint)k.Anchor.Version);
                    h = Mix(h, (uint)m_Rows[i].State);
                    sum += h * 17u;
                }
                return (int)sum;
            }
        }

        private static uint Mix(uint h, uint v)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    h ^= v & 0xFFu;
                    h *= 16777619u;
                    v >>= 8;
                }
                return h;
            }
        }

        private static uint Bits(float f)
        {
            return math.asuint(f);
        }
    }
}
