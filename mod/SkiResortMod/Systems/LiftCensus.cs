// LiftCensus — the PURE lift model: one row per lift, built from net topology (docs/research/
// hub-u2-spec.md §5.2). Created 2026-10-03 (Round L1, implementer I-2). LiftCensusSystem harvests the
// ECS data into the plain Edge / Node / Station lists below and calls Build on events; the hub
// (SkiResortStatsSystem / SkiResortHubUISystem) and the lift logs read the rows. tests/LiftBatch
// links this file into a net8 host and runs §5.9's synthetic graphs.
//
// WHY A CENSUS (spec §8.1 "Lift data source"). ChairPinSystem's loop cache exists only in moving mode
// (ChairPinSystem.cs:443-447), measures the CIRCUIT (chain A + top turn + chain B + base turn,
// LiftLoopCache.cs:371-386), is lane-based and up to 30 frames stale. The census is independent of
// moving chairs and is the lift authority for the hub and the logs; the loop cache contributes only
// "chairs riding" per station pair (ChairPinSystem.TryGetLoop).
//
// INPUTS (spec §5.2 item 1; the ECS side is LiftCensusSystem's header):
//   Edge    — every live SkiChairliftHV edge: Start/End node, end positions, Curve length, chairs
//             (chair-prefab entries of its SubObject buffer). Station stub edges (SkiLiftMarker) are
//             NOT edges here (LengthM = Σ line edges only).
//   Node    — every endpoint of those edges: Station = index into the station list when the node is
//             a station connection node (its Owner is a station: stations-first; or a connected edge
//             owned by a station: line-first — the IsStationConnectionNode rule of ChairPinSystem
//             extended to say WHICH station), else −1; HasTower = any non-chair sub-object.
//   Station — every live station: IsBase by PREFAB (never by height; the loop cache's "lower = base"
//             is only its internal tie-break), position, HV stub tip and axis (StationAlign).
//
// MODEL (spec §5.2 items 2-4):
//   * Components: edges are unioned through shared nodes, but a station connection node is a
//     TERMINAL, never a connector (the loop cache's "walk stops at station anchors",
//     LiftLoopCache.cs:14-20) — two lifts sharing a station stay two components.
//   * Attribution (rows keyed by the station pair (Base, Top), plus an anchor edge for rows that have
//     no complete pair, so two unfinished lines from one station stay two rows):
//       a. a component touching exactly one base and one top station → key (base, top);
//       b. else, if its surviving edges carried exactly ONE key K at the previous build and the
//          stations it touches ⊆ {K.Base, K.Top} → K (a fragment) — spec §5.2.3 b as written, with NO
//          liveness condition on K's stations. FIXER ROUND (finding l1-lifts-1): I-2 had added "K's
//          stations are still alive" here, so a complete lift that lost a station (the player deletes
//          and re-places a top station to straighten it) got a NEW key — NewLine instead of Opened —
//          and its chairs were never parked: they froze mid-air. Now the lift keeps its key, becomes
//          Open with Gaps 1 (the dead terminal is untouched) and DiffEvents raises Opened, so the
//          existing park runs. Liveness stays in rule c (a redraw must reach a living partner);
//       c. else, if it touches exactly one station S and a previous OPEN row K has S as a station
//          and its other station is alive → K (a redrawn fragment whose edges are all new). The
//          spec says "a previous key K"; a COMPLETE previous row is excluded here, because a new
//          line drawn from a station that already has a complete lift is a second lift, not a
//          fragment of the first (shared-station case);
//       d. else a new key: (S, Null) when S is a base, (Null, S) when a top, (Null, Null) with no
//          station — each with its lowest-index edge as anchor. A component touching > 2 stations
//          or two of one kind is d with a build note (Notes).
//     A key with no component left is dropped; its stations become StationsOnly rows. History is in
//     memory only: after a reload mid-redraw, fragments come back as Unfinished rows (accepted).
//   * Row values: Complete = exactly one component touching both stations; Open = a pair with any
//     other shape; Unfinished = no pair (or a build note); StationsOnly = a station on no component
//     and in no row. Gaps = fragments − 1 + (base not touched) + (top not touched), 0 when Complete
//     (one deleted middle section = 1, a deleted approach section = 1). GapAt = the dead-end
//     (non-station, single-edge) node nearest the base (else the top, else the Id edge's start).
//     LengthM = Σ edge Curve lengths (NOT the loop). RiseM = top tip y − base tip y (else max − min
//     node y). HorizontalM = |top tip.xz − base tip.xz| (else Σ edge chord xz). Alignment from
//     StationAlign.Measure(partner = base, subject = top) when both stations exist. Towers = row
//     nodes with a tower; TowersRemoved = non-station row nodes WITHOUT one (vanilla keeps a tower
//     on every line node while a section uses it, GenerateNodesSystem.cs:1250-1254 per spec §5.1,
//     so a missing one was removed by another tool — OD-8 (a); it clears by itself once a section at
//     that node is redrawn and the game regenerates the tower). Id = the LIVE Base, else the live Top,
//     else the lowest-index edge (fixer round: a key may now hold a removed station, rule b; the row
//     id is what the hub addresses, renames and flies to, so it must be a living entity — for a key
//     whose stations are both alive this is spec §2.1's "Base, else Top" unchanged). KNOWN, HELD
//     (fixer-round review): after a station is removed, its former connection node becomes a plain
//     dead end with no tower of its own, so it counts as TowersRemoved = 1 unless vanilla regenerates
//     a tower there — unverified until the field shows which; no guard added.
//   * Events (spec §5.2 item 5), appended to the caller's list by Build (the list is cleared first),
//     only from the SECOND build after Reset (the first build is the per-load baseline, never logged):
//       SectionsDeleted — edges of a previous row that are gone, when that row gained no new edge in
//                         the same build (a pure deletion; a redraw that replaces edges is an edit,
//                         not a deletion). Row = the row's index now (−1 when dropped), PrevRow = its
//                         index in PreviousRows, Count = sections, LengthM = their length.
//       Opened          — Complete → Open (same key). Count = Gaps.
//       Closed          — Complete now and not Complete under the same key before. Count = 1 when the
//                         key existed as Open ("complete again"), 0 for a new lift ("complete").
//       NewLine         — a new Unfinished row with at least one section.
//       Removed         — never produced here: LiftCensusSystem appends it from its Deleted-tag
//                         snapshot (an element removed by another tool).
//   * Revision ++ whenever the row CONTENT changed (every field, hashed), so the stats system can
//     gate on it; LastBuildChangedTopology says whether edges, node roles or row keys changed (the
//     system then asks ChairPinSystem for an immediate loop rebuild).
//
// CONTRACT DEVIATIONS (additive only, spec §2.1 names unchanged): Event.PrevRow, PreviousRows, Reset(),
// LastBuildChangedTopology, Notes, HasBaseline. They let the system name a lift whose row was dropped
// (its last section deleted) and keep the first build after a load silent.
//
// COST. Build is event-driven (lift edits, coalesced station moves, a 4096-frame watchdog), O(edges +
// nodes + stations) with dictionary lookups; it reuses its member collections, so a build allocates
// only when a collection grows or a build note is written.
//
// CATALOG PHASE B (2026-10-11, docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §4.1, D18, lift
// group) — the TYPED census. Edge.Type = the SkiCatalog.Lifts index of the edge's line prefab, Station.Type = its
// StationDef.LiftTypeIndex (NoType = 255 when the harvest could not type it — defensive only). In the union pass an
// edge with Type == NoType is IGNORED: never unioned, never part of a component, a row or a degree count, one build
// note "edge {id} has no lift type — ignored" (the harvest passes catalog lines only, so a vanilla HV feeder never
// arrives here at all — DECISIONS 94 lets one feed a station stub; ext-chk X1). Every component carries TypeMask (bit
// Type of each typed edge and each touched station); popcount > 1 = MIXED (D18: a detachable line between fixed-grip
// terminals is as broken as two line types — gauge ±2.6 vs ±1.8 anchors): Complex (→ Unfinished through the existing
// MakeRow path), a build note "component of N edge(s) from {edge} mixes lift types (fixedGrip+detachable) —
// Unfinished", and every row built from it gets Mixed = true. Attribution is unchanged (a mixed component touching one
// base and one top keeps that pair's key, so removing the stray edge returns the same row to Complete). Row.Type = the
// live base station's type, else the live top's, else the lowest-index edge's; StationsOnly rows: their station's.
// The row hash (Revision) covers Type and Mixed; the topology hash covers the edges' types.
//
// PURE FILE: `using System;` + `using System.Collections.Generic;` + `using Unity.Mathematics;` only
// (the test-host rule of tests/SlopeBatch; EntityRef.cs is the pure stand-in for Entity), + SkiCatalog (pure, linked
// wherever this file is) for the type ids in the "mixes lift types" note.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The pure lift census (hub-u2-spec.md §5.2).</summary>
    public sealed class LiftCensus
    {
        public enum State : byte { Complete, Open, Unfinished, StationsOnly }

        /// <summary>Catalog Phase B (round-2-contract §4.1): an edge or station the harvest could not type (defensive
        /// only: the harvest passes catalog lines and catalog stations). Never a SkiCatalog.Lifts index.</summary>
        public const byte NoType = 255;

        public struct Edge
        {
            public EntityRef Id, Start, End;
            public float3 StartPos, EndPos;
            public float LengthM;
            public int Chairs;
            public byte Type;     // Catalog Phase B: SkiCatalog.Lifts index of the edge's line prefab (NoType = ignored)
        }

        /// <summary>Station: index into the station list, or −1.</summary>
        public struct Node
        {
            public EntityRef Id;
            public int Station;
            public bool HasTower;
            public float3 Position;
        }

        public struct Station
        {
            public EntityRef Id;
            public bool IsBase;
            public float3 Position, StubTip;
            public float2 Axis;
            public byte Type;     // Catalog Phase B: StationDef.LiftTypeIndex (NoType when unknown)
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
            // Catalog Phase B (round-2-contract §4.1, seam S1): the row's lift type (SkiCatalog.Lifts index; NoType when
            // nothing typed it) and whether its component spans two or more of our lift types (then State Unfinished).
            public byte Type;
            public bool Mixed;
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

        /// <summary>Smallest focus radius (a station-only row, a one-section lift), metres.</summary>
        public const float MinFocusRadiusM = 40f;

        // ── keys ───────────────────────────────────────────────────────────────────────────────
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

        /// <summary>One connected component of line edges (pooled between builds).</summary>
        private sealed class Comp
        {
            public readonly List<int> Edges = new List<int>();
            public readonly List<int> Stations = new List<int>();   // distinct station indices touched
            public int Bases, Tops;
            public EntityRef LowestEdge;
            public Key Key;
            public bool Complex;
            public uint TypeMask;        // Catalog Phase B: bit Type of every typed edge and touched station
            public bool Mixed;           // Catalog Phase B: popcount(TypeMask) > 1 (D18)

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
            }
        }

        // ── current build ──────────────────────────────────────────────────────────────────────
        private readonly List<Row> m_Rows = new List<Row>();
        private readonly List<Key> m_Keys = new List<Key>();
        private Dictionary<EntityRef, int> m_EdgeRow = new Dictionary<EntityRef, int>();
        private Dictionary<EntityRef, Key> m_EdgeKey = new Dictionary<EntityRef, Key>();
        private Dictionary<EntityRef, float> m_EdgeLength = new Dictionary<EntityRef, float>();

        // ── previous build (swapped in at the start of Build) ──────────────────────────────────
        private readonly List<Row> m_PrevRows = new List<Row>();
        private readonly List<Key> m_PrevKeys = new List<Key>();
        private readonly Dictionary<Key, int> m_PrevKeyIndex = new Dictionary<Key, int>();
        private Dictionary<EntityRef, int> m_PrevEdgeRow = new Dictionary<EntityRef, int>();
        private Dictionary<EntityRef, Key> m_PrevEdgeKey = new Dictionary<EntityRef, Key>();
        private Dictionary<EntityRef, float> m_PrevEdgeLength = new Dictionary<EntityRef, float>();

        // ── scratch (reused) ───────────────────────────────────────────────────────────────────
        private readonly Dictionary<EntityRef, int> m_NodeIndex = new Dictionary<EntityRef, int>();
        private readonly Dictionary<EntityRef, int> m_StationIndex = new Dictionary<EntityRef, int>();
        private readonly Dictionary<EntityRef, int> m_FirstEdgeAtNode = new Dictionary<EntityRef, int>();
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

        /// <summary>++ whenever the row content changed (spec §2.1).</summary>
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

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Build
        // ════════════════════════════════════════════════════════════════════════════════════════

        public void Build(List<Edge> edges, List<Node> nodes, List<Station> stations, List<Event> events)
        {
            events.Clear();
            Notes.Clear();
            bool baseline = m_HasBaseline;
            SwapToPrevious();

            // ── 1. indices ─────────────────────────────────────────────────────────────────────
            m_NodeIndex.Clear();
            for (int i = 0; i < nodes.Count; i++)
                m_NodeIndex[nodes[i].Id] = i;
            m_StationIndex.Clear();
            for (int i = 0; i < stations.Count; i++)
                m_StationIndex[stations[i].Id] = i;
            m_CurrentEdges.Clear();
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Type != NoType)   // Catalog Phase B: an untyped edge is not a lift edge (header)
                    m_CurrentEdges.Add(edges[i].Id);
            }

            // ── 2. union-find through non-station nodes; degree per node ─────────────────────
            // Catalog Phase B (round-2-contract §4.1): an edge with Type == NoType is never linked (it never connects
            // two components) and adds no degree (it is ignored, so it cannot make a dead end look connected).
            m_Parent.Clear();
            for (int i = 0; i < edges.Count; i++)
                m_Parent.Add(i);
            m_FirstEdgeAtNode.Clear();
            m_Degree.Clear();
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Type == NoType)
                    continue;
                LinkEndpoint(i, edges[i].Start, nodes, stations.Count);
                LinkEndpoint(i, edges[i].End, nodes, stations.Count);
            }

            // ── 3. components ──────────────────────────────────────────────────────────────────
            m_Comps.Clear();
            m_CompOfRoot.Clear();
            for (int i = 0; i < edges.Count; i++)
                m_CompOfRoot.Add(-1);
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Type == NoType)
                {
                    // Catalog Phase B: defensive (the harvest only passes catalog lines); one note per such edge.
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
                comp.TypeMask |= TypeBit(edges[i].Type);   // Catalog Phase B
                TouchStation(comp, StationOf(edges[i].Start, nodes, stations.Count), stations);
                TouchStation(comp, StationOf(edges[i].End, nodes, stations.Count), stations);
            }

            // ── 3b. Catalog Phase B (D18): a component whose edges and touched stations span two or more of our lift
            //        types is MIXED — Complex (→ Unfinished in MakeRow), one note, every row built from it Mixed. ──
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

            // ── 4. attribution (rules a-d) ─────────────────────────────────────────────────────
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

            // ── 5. rows ────────────────────────────────────────────────────────────────────────
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
                    Type = st.Type,   // Catalog Phase B: a StationsOnly row's type is its station's
                });
            }

            // ── 6. stable order: Id, then Top, then anchor ─────────────────────────────────────
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

            // ── 7. events (never on the baseline build) ───────────────────────────────────────
            if (baseline)
                DiffEvents(events);

            // ── 8. revision + topology ─────────────────────────────────────────────────────────
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

        // ── union-find ────────────────────────────────────────────────────────────────────────
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

        private void LinkEndpoint(int edge, EntityRef node, List<Node> nodes, int stationCount)
        {
            m_Degree.TryGetValue(node, out int deg);
            m_Degree[node] = deg + 1;
            if (StationOf(node, nodes, stationCount) >= 0)
                return;   // a station connection node is a terminal, never a connector
            if (m_FirstEdgeAtNode.TryGetValue(node, out int first))
                Union(edge, first);
            else
                m_FirstEdgeAtNode[node] = edge;
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
            comp.TypeMask |= TypeBit(stations[station].Type);   // Catalog Phase B (D18): stations count for Mixed
        }

        /// <summary>Catalog Phase B: bit <paramref name="type"/> of a component's TypeMask; 0 for NoType (and any index
        /// past 31 — SkiCatalog.Lifts holds 4 rows, APPEND ONLY).</summary>
        private static uint TypeBit(byte type)
        {
            return type < 32 ? 1u << type : 0u;
        }

        /// <summary>Catalog Phase B: "fixedGrip+detachable" — the type ids of a mask in index order (SkiCatalog
        /// LiftTypeDef.Id; "type N" for an index the catalog does not know). Allocates: build notes only.</summary>
        private static string TypeIds(uint mask)
        {
            string ids = "";
            for (int t = 0; t < 32; t++)
            {
                if ((mask & (1u << t)) == 0)
                    continue;
                LiftTypeDef def = SkiCatalog.LiftByIndex(t);
                ids += (ids.Length > 0 ? "+" : "") + (def != null ? def.Id : "type " + t);
            }
            return ids;
        }

        // ── attribution ───────────────────────────────────────────────────────────────────────
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
            //    of its stations is gone (header rule b; fixer round, finding l1-lifts-1).
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

        // ── row values ────────────────────────────────────────────────────────────────────────
        private Row MakeRow(Key key, List<Comp> comps, List<Edge> edges, List<Node> nodes, List<Station> stations)
        {
            var row = new Row { Base = key.Base, Top = key.Top };
            int baseIdx = -1, topIdx = -1;
            bool hasBase = !key.Base.IsNull && m_StationIndex.TryGetValue(key.Base, out baseIdx);
            bool hasTop = !key.Top.IsNull && m_StationIndex.TryGetValue(key.Top, out topIdx);

            bool complex = false, touchedBase = false, touchedTop = false, mixed = false;
            int bothTouching = 0;
            EntityRef lowest = default;
            byte lowestType = NoType;   // Catalog Phase B: the lowest-index edge's type (Row.Type's last fallback)
            float3 bmin = new float3(float.MaxValue), bmax = new float3(float.MinValue);
            float chordXZ = 0f;
            m_RowNodes.Clear();

            for (int c = 0; c < comps.Count; c++)
            {
                Comp comp = comps[c];
                complex |= comp.Complex;
                mixed |= comp.Mixed;
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

            // Id: the live base, else the live top, else the lowest edge (header; a key may hold a
            // removed station since the fixer round).
            row.Id = hasBase ? key.Base : hasTop ? key.Top : lowest;

            // Catalog Phase B (round-2-contract §4.1): the row's type follows its Id's rule — the live base station's
            // type, else the live top's, else the lowest-index edge's; Mixed when any of its components is.
            row.Type = hasBase ? stations[baseIdx].Type : hasTop ? stations[topIdx].Type : lowestType;
            row.Mixed = mixed;

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

        // ── events ────────────────────────────────────────────────────────────────────────────
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

        // ── hashes (FNV-1a over 32-bit words; order-independent sum for topology) ─────────────
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
                h = Mix(h, r.Type); h = Mix(h, r.Mixed ? 1u : 0u);   // Catalog Phase B (§4.1)
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
                    h = Mix(h, edges[i].Type);   // Catalog Phase B: a type change is a topology change
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
