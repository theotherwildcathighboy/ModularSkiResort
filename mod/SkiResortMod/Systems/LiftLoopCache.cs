// LiftLoopCache — orders a drawn chairlift's cable lanes into closed travel loops.
//
// Pure topology/geometry over plain data (no EntityManager coupling beyond what the
// caller harvested): ChairPinSystem hands in every HV cable lane owned by the chairlift
// net (edges AND nodes — both carry PrefabRef to the net prefab) plus the station
// entities, and this class:
//
//   1. CHAINS lanes end-to-end by EXACT PathNode equality. Sound because LaneSystem's
//      FindAnchor overwrites BOTH the endpoint position and the endpoint PathNode with
//      the anchor's own (LaneSystem.cs:4049-4072 / 4117-4120, decompile) — two lanes
//      meeting at a tower sheave or station bullwheel stub share a bitwise-identical
//      PathNode. Chain direction is per-lane (m_Start/m_End orientation is NOT
//      consistent along one drawn lift — never assume node order).
//   2. Recognizes STATION TERMINALS by PathNode owner index == station entity index
//      (the anchor PathNode is owned by the station building; PathNode.GetOwnerIndex
//      is public) — and the walk STOPS at station anchors. Load-bearing (review
//      finding, entry 98): two lifts sharing a station also share that station's stub
//      PathNodes, so a walk that only classified terminals would run THROUGH the
//      shared station and merge both lifts into one mega-loop (or un-close the first
//      lift while the second is under construction).
//   3. Pairs the two chains that run between the same two stations (a lift's up-side
//      and down-side cables) and CLOSES the loop with two synthesized bullwheel arcs —
//      semicircles between the chains' terminal endpoints, bulging in the direction of
//      travel (into the station, around the wheel). Arcs are analytic circles, not
//      beziers: constant-speed parametrisation is exact (theta = distance / radius).
//   4. Chair offsets live in the caller (ChairPinSystem): even spacing, carried
//      forward BY ENTITY across rebuilds when the loop is unchanged (identity
//      rebuild — zero jump by construction), plain deterministic grid otherwise.
//
// Chairs whose owner edge is not part of any COMPLETE loop (unfinished lifts, dead-end
// lines, station missing) are reported back for static pinning — per-lift graceful
// degradation, never an exception.
//
// All lengths run in double (loop phase accumulates speed * frameIndex; float32 would
// quantize at meter scale by frame ~1e7). Per-segment lengths use the same metric as
// MathUtils.ClampLength (Curve.m_Length for lanes, exact pi*r for arcs), so sampling
// never runs past a segment end.
//
// ROUND L1 (2026-10-03, implementer I-2; docs/research/hub-u2-spec.md §2.1, §5.4) — data only,
// the chaining / pairing / closing algorithm is unchanged:
//   * Loop.m_BaseStation / m_TopStation: the two station ENTITIES of a closed loop, from the
//     pairing indices (stations[baseStation] / [topStation], Build below). "Base" here is still
//     the loop cache's own height tie-break (lower terminal); the hub's base is by PREFAB (the
//     LiftCensus), so ChairPinSystem.TryGetLoop matches the pair UNORDERED.
//   * ProjectOnOwner(loop, owner, position): the loop arc parameter of a point projected onto the
//     lane segments of one owner (edge) — OD-7 (a) restart seeding in ChairPinSystem: a chair
//     parked on its own section starts riding from where it stands when the lift closes again.
//
// CATALOG PHASE B (2026-10-11, docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §5.1 and its
// orchestrator addendum 1, lift group) — the chaining, the terminal classification and the arcs are unchanged:
//   * CableLane.m_TypeIndex = the SkiCatalog.Lifts index of the lane owner's line prefab (ChairPinSystem harvests the
//     lanes of Loop-mover types only). Loop.m_TypeIndex = the common type of its member lanes, else −1 with a build
//     note "loop between stations S and T mixes lift types — not moving" (ChairPinSystem gives such a loop no roster:
//     its chairs static-pin). Loop.m_SpeedMps is set by ChairPinSystem from the row (the mover's per-loop clock).
//   * UP LANE (addendum 1, dq-family-review-geometry.md F2): chain A — the one traveled base → top — is the cable on
//     the RIGHT looking uphill (StationAlign.UpLaneIsSecond on the two chains' midpoints, base → top by the stations'
//     positions), instead of "the first chain found" (was this file :286, kv.Value[0]). A carrier faces its travel
//     direction with its hanger at local +x, so the hanger stays OUTBOARD of the tower decks on both sides. Two chains
//     that cannot be told apart keep the order found. Loop.m_UpLaneSwapped / m_UpLaneDecided say what happened
//     (ChairPinSystem logs "[loop] up lane = right looking uphill" once per loop per load).
//
// CATALOG PHASE C (2026-10-11, docs/research/art/assets-2026-10/code-round-3/round-3-contract.md §2.3, loopcache group;
// extensibility.md F8 as corrected by extensibility-check.md F8):
//   * THE TOPOLOGY MOVED, UNCHANGED, into the PURE LiftLoopTopology.cs (chaining, terminal classification, pairing,
//     base / top, up lane, orientation, the gap check, the common type; was this file :175-322, :347-404, :413-430,
//     :520-540, :558-595 @ d3ae435b). This file keeps the Game side: it interns PathNodes to int ids (equal ids ⇔
//     PathNode.Equals: the 64-bit m_SearchKey, Game.Pathfind/PathNode.cs:62-65, owner = GetOwnerIndex :113-116, =1.6.2),
//     pre-samples each lane's ends with the exact MathUtils.Position / Tangent calls GetChainEndpoint made (was :590-591),
//     runs the topology, and writes the pair notes and loops in pair order from its Pairs — so Loop mode keeps today's
//     chains, notes, note order, counts, segments, lengths and Sample (tests/LiftBatch LC-REF proves the topology against
//     a frozen transcription; the geometry bodies below are unchanged but for the chain type's field names
//     (link.m_LaneIndex → link.Lane, link.m_Reversed → link.Reversed, chain.m_Links → chain.Links) and Sample's body
//     moved verbatim into SampleSegments).
//   * OPEN-CHAIN (SHUTTLE) MODE for the tram (code-economy §A.4: "LiftLoopCache returns the two chains UNCLOSED"):
//     BuildShuttles returns, per station pair, a Shuttle with two Paths (chain A = the up lane, right looking uphill;
//     both oriented base → top; base = the Base-ROLE station, C1), each = [straight run from the base dock] + the chain's
//     lanes + [straight run to the top dock], its arc-length table being the Segment list (m_CumStart). Docks come from
//     StationDef.DockLocalA / B through ObjectUtils.LocalToWorld(Transform, float3) (Game.Objects/ObjectUtils.cs:208-211,
//     =1.6.2). Sample(Path, s) returns the track-rope point and the BASE → TOP unit tangent at s ∈ [0, m_Length]
//     (allocation-free: the mover calls it every frame, round-3-contract-skeptic MUST 3). Nothing in Loop mode reads any
//     of it; TramShuttleSystem owns its own LiftLoopCache instance.

using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Pathfind;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    internal sealed class LiftLoopCache
    {
        /// <summary>One cable lane as harvested by ChairPinSystem (world space).</summary>
        internal struct CableLane
        {
            public Entity m_Lane;
            public Entity m_Owner;      // edge or node entity of the chairlift net
            public Bezier4x3 m_Bezier;
            public PathNode m_Start;
            public PathNode m_End;
            public float m_Length;      // Curve.m_Length (vanilla metric)
            public int m_TypeIndex;     // Catalog Phase B: SkiCatalog.Lifts index of the owner's line prefab
        }

        internal struct Segment
        {
            public bool m_IsArc;
            public Entity m_Owner;      // lane segments: the lane's owner (edge/node); arcs: Entity.Null
            public Bezier4x3 m_Bezier;  // lane segments only
            public bool m_Reversed;     // travel runs t 1 -> 0
            public float3 m_ArcCenter;  // arc segments only
            public float3 m_ArcU;       // unit: center -> arc entry point
            public float3 m_ArcB;       // unit: travel direction at arc entry (bulge side)
            public float m_ArcRadius;
            public float m_Length;
            public double m_CumStart;
        }

        internal sealed class Loop
        {
            public readonly List<Segment> m_Segments = new List<Segment>();
            public double m_Length;
            /// <summary>Round L1: the station entities this loop runs between (the pairing's
            /// lower terminal = "base" by height, the other = "top"); unordered for consumers.</summary>
            public Entity m_BaseStation, m_TopStation;
            /// <summary>Owner entities (edges/nodes) of the member lanes — chairs belong to
            /// this loop when their Owner is in this set.</summary>
            public readonly HashSet<Entity> m_MemberOwners = new HashSet<Entity>();
            public readonly List<Entity> m_Chairs = new List<Entity>();
            public readonly List<float> m_GripTops = new List<float>();
            public readonly List<double> m_Offsets = new List<double>();
            /// <summary>Catalog Phase B: the common SkiCatalog.Lifts index of the member lanes; −1 = they span several
            /// lift types (no roster, header CATALOG PHASE B).</summary>
            public int m_TypeIndex = -1;
            /// <summary>Catalog Phase B: carrier speed along this loop, m/s (ChairPinSystem sets it from the row; 0 =
            /// not moving).</summary>
            public double m_SpeedMps;
            /// <summary>Catalog Phase B (addendum 1): the up-lane rule decided this loop (false = the two chains could not
            /// be told apart: kept as found) and whether it swapped the chain order found.</summary>
            public bool m_UpLaneDecided, m_UpLaneSwapped;
        }

        public readonly List<Loop> m_Loops = new List<Loop>();
        public int m_ChainCount;
        public int m_UnclosedChainCount;

        // Catalog Phase C (round-3-contract §2.1): StationTerminalRadius (was :126-130, 20 m) moved to
        // LiftLoopTopology.StationTerminalRadius with ClassifyTerminal, value unchanged.

        /// <summary>Catalog Phase C (round-3-contract §2.3): one open chain of a shuttle, base dock → top dock.</summary>
        internal sealed class Path
        {
            /// <summary>[base dock run] + the chain's lanes + [top dock run]; m_CumStart is the arc-length table.</summary>
            public readonly List<Segment> m_Segments = new List<Segment>();
            public readonly HashSet<Entity> m_MemberOwners = new HashSet<Entity>();
            public double m_Length;                      // Σ segment lengths
            public float3 m_BaseEnd, m_TopEnd;           // track-rope points where the path starts / ends (docks, else rope ends)
            public float m_BaseRunM, m_TopRunM;          // synthesized dock runs (0 = none)
            /// <summary>Additive: the station dock each end uses (0 = DockA = hosted entry 0, 1 = DockB; T14 frame: the tram base's A is the +X bay, the top's A the −X bay; −1 = none: the
            /// path ends at the rope end).</summary>
            public sbyte m_BaseDock = -1, m_TopDock = -1;
        }

        /// <summary>Catalog Phase C: one tram between two stations — chain A (the up lane, right looking uphill) and chain
        /// B, both base → top. Cabin A rides m_A, cabin B rides m_B.</summary>
        internal sealed class Shuttle
        {
            public readonly Path m_A = new Path(), m_B = new Path();
            public Entity m_BaseStation, m_TopStation;   // base by role (C1): the cabins' host
            public quaternion m_BaseRotation, m_TopRotation;   // station rotations (the dock frames: rope point + rotation)
            public int m_TypeIndex = -1;
            public bool m_BaseByRole, m_UpLaneDecided, m_UpLaneSwapped;
            /// <summary>Additive: the stations' indices in the lists BuildShuttles was given.</summary>
            public int m_BaseStationIndex = -1, m_TopStationIndex = -1;
            public double MeanLengthM => 0.5 * (m_A.m_Length + m_B.m_Length);
            /// <summary>Additive (round-3-contract-skeptic F4): MeanLengthM rounded to 1 cm — the length to hand to
            /// ShuttleProfile, so a float-noise rebuild cannot shift the cycle.</summary>
            public double ProfileLengthM => ShuttleProfile.QuantizeLengthM(MeanLengthM);
        }

        /// <summary>Catalog Phase C: BuildShuttles' result, in pair order (empty after a Loop Build).</summary>
        public readonly List<Shuttle> m_Shuttles = new List<Shuttle>();

        // Catalog Phase C: the pure topology core and its stand-ins, reused across builds (rebuilds only, never per frame).
        private readonly LiftLoopTopology m_Topology = new LiftLoopTopology();
        private readonly List<LiftLoopTopology.Lane> m_TopoLanes = new List<LiftLoopTopology.Lane>();
        private readonly List<LiftLoopTopology.Station> m_TopoStations = new List<LiftLoopTopology.Station>();
        private readonly Dictionary<PathNode, int> m_NodeIds = new Dictionary<PathNode, int>();

        // ── Build diagnostics (entry 102): what the last Build saw, per chain, for the
        // developer-tab dump. Plain data, rebuilt every Build, never consulted by Sample.
        internal struct ChainDiag
        {
            public int m_Links;
            public int m_StartOwner, m_EndOwner;      // PathNode owner entity indices
            public int m_StartStation, m_EndStation;  // station list index or -1
            public string m_StartHow, m_EndHow;       // "owner" / "child" / "pos 8.3m" / "none (nearest 41m)"
            public float3 m_StartPos, m_EndPos;
        }
        internal readonly List<ChainDiag> m_ChainDiags = new List<ChainDiag>();
        internal readonly List<string> m_BuildNotes = new List<string>();

        // Catalog Phase C (round-3-contract §2.1): ChainLink / Chain (was :145-158) are LiftLoopTopology.Link / .Chain
        // (PathNode terminals → interned ids + owner indices).

        public void Clear()
        {
            m_Loops.Clear();
            m_ChainCount = 0;
            m_UnclosedChainCount = 0;
            m_ChainDiags.Clear();
            m_BuildNotes.Clear();
            m_Shuttles.Clear();   // Catalog Phase C
        }

        /// <summary>Builds loops from harvested lanes + station entities (with world
        /// positions, for base-first orientation). <paramref name="stationByOwnerIndex"/>
        /// maps entity INDEX → station list index for each station AND its children
        /// (sub-objects, sub-net nodes/edges) — PathNode owners are stored as indices.
        /// Never throws on bad topology — lifts that do not close simply produce no loop
        /// and count as unclosed chains; the reasons land in m_BuildNotes/m_ChainDiags.
        /// Catalog Phase C (round-3-contract §2.3): the chaining / classification / pairing / up-lane / orientation /
        /// gap pass runs in the pure LiftLoopTopology (Loop mode, transcribed); this method is its Game-side adapter and
        /// writes the pair notes and loops in the same order as before.</summary>
        public void Build(List<CableLane> lanes, List<Entity> stations, List<float3> stationPositions,
            Dictionary<int, int> stationByOwnerIndex, List<float3> stationWheelCenters)
        {
            Clear();
            FillTopologyLanes(lanes);
            m_TopoStations.Clear();
            for (int i = 0; i < stations.Count; i++)
            {
                m_TopoStations.Add(new LiftLoopTopology.Station
                {
                    OwnerIndex = stations[i].Index,
                    Position = stationPositions[i],
                    Role = -1,
                    DockA = new float3(float.NaN),
                    DockB = new float3(float.NaN),
                });
            }
            m_Topology.Build(m_TopoLanes, m_TopoStations, stationByOwnerIndex, LiftLoopTopology.Mode.Loop);
            CopyTopologyChains();

            // ── Pair chains by station pair, close each pair into a loop ─────────────────
            for (int p = 0; p < m_Topology.Pairs.Count; p++)
            {
                LiftLoopTopology.Pair pair = m_Topology.Pairs[p];
                int stationA = pair.StationLo, stationB = pair.StationHi;
                if (pair.Status == LiftLoopTopology.PairStatus.NotTwoChains)
                {
                    m_UnclosedChainCount += pair.ChainIndices.Count;
                    m_BuildNotes.Add($"stations ({stationA},{stationB}): {pair.ChainIndices.Count} chain(s), need exactly 2");
                    continue;
                }
                if (pair.Status == LiftLoopTopology.PairStatus.GapOutOfRange)
                {
                    // Sanity (was TryCloseLoop :423-430, now LiftLoopTopology.ArcSane on the same endpoints): the turn
                    // diameter is the cable gauge (expect ~3.6 m). Wildly off means the anchors are not where this
                    // model thinks — degrade to static pinning.
                    string gapWhy = $"terminal gap out of range (top {pair.TopGapM:F2} m, base {pair.BaseGapM:F2} m; want 1–10)";
                    m_UnclosedChainCount += 2;
                    m_BuildNotes.Add($"stations ({stationA},{stationB}): closure failed — {gapWhy}");
                    continue;
                }
                // Base station = the lower of the two terminals (LiftLoopTopology, Loop mode); chain A = the up lane,
                // oriented base → top, chain B top → base.
                int baseStation = pair.Base;
                int topStation = pair.Top;

                float3 baseWheel = stationWheelCenters != null && baseStation < stationWheelCenters.Count
                    ? stationWheelCenters[baseStation] : new float3(float.NaN);
                float3 topWheel = stationWheelCenters != null && topStation < stationWheelCenters.Count
                    ? stationWheelCenters[topStation] : new float3(float.NaN);

                bool swap = pair.UpLaneSwapped;
                bool decided = pair.UpLaneDecided;

                if (!TryCloseLoop(lanes, pair.A, pair.B, baseWheel, topWheel, out Loop loop, out string why))
                {
                    m_UnclosedChainCount += 2;
                    m_BuildNotes.Add($"stations ({stationA},{stationB}): closure failed — {why}");
                    continue;
                }
                loop.m_BaseStation = stations[baseStation];   // Round L1: pair identity (data only)
                loop.m_TopStation = stations[topStation];
                loop.m_UpLaneDecided = decided;
                loop.m_UpLaneSwapped = swap;

                // Catalog Phase B (round-2-contract §5.1): the loop's lift type = the common type of its member lanes.
                loop.m_TypeIndex = pair.TypeIndex;
                if (loop.m_TypeIndex < 0)
                    m_BuildNotes.Add($"loop between stations {stationA} and {stationB} mixes lift types — not moving");
                m_Loops.Add(loop);
                m_BuildNotes.Add($"stations ({stationA},{stationB}): loop closed, {loop.m_Length:F1} m, " +
                                 $"{loop.m_Segments.Count} segments (base = station {baseStation}; up lane = right looking " +
                                 $"uphill, {(decided ? swap ? "swapped" : "as found" : "undecided, as found")}; type {loop.m_TypeIndex})");
            }
        }

        /// <summary>Catalog Phase C (round-3-contract §2.3): the Shuttle-mode build (TramShuttleSystem). One Shuttle per
        /// station pair whose two chains pass the shuttle gap check and share one lift type: both chains oriented base →
        /// top (base = the Base-ROLE station, C1), each extended by a straight run from / to its dock (StationDef.DockLocalA
        /// / B → world, ObjectUtils.cs:208-211). <paramref name="stationDefs"/>[i] is the catalog row of stations[i] (null =
        /// unknown: no role, no docks). Never throws on bad topology: refusals land in m_BuildNotes (one per pair).</summary>
        public void BuildShuttles(List<CableLane> lanes, List<Entity> stations, List<Game.Objects.Transform> stationTransforms,
                                  List<StationDef> stationDefs, Dictionary<int, int> stationByOwnerIndex)
        {
            Clear();
            FillTopologyLanes(lanes);
            m_TopoStations.Clear();
            for (int i = 0; i < stations.Count; i++)
            {
                StationDef def = stationDefs != null && i < stationDefs.Count ? stationDefs[i] : null;
                Game.Objects.Transform transform = stationTransforms[i];
                m_TopoStations.Add(new LiftLoopTopology.Station
                {
                    OwnerIndex = stations[i].Index,
                    Position = transform.m_Position,
                    Role = def != null ? (sbyte)def.Role : (sbyte)-1,
                    DockA = DockWorld(transform, def != null ? def.DockLocalA : new float3(float.NaN)),
                    DockB = DockWorld(transform, def != null ? def.DockLocalB : new float3(float.NaN)),
                });
            }
            // T02 (2026-10-16): the tram's terminal is its stub-tip track rope, 29-30 m from the station origin but ~12 m from its
            // dock; Shuttle mode owns terminals by dock reach too (LiftLoopTopology.ClassifyShuttleTerminal). Loop mode is untouched.
            m_Topology.ShuttleDockReach = true;
            m_Topology.Build(m_TopoLanes, m_TopoStations, stationByOwnerIndex, LiftLoopTopology.Mode.Shuttle);
            CopyTopologyChains();

            for (int p = 0; p < m_Topology.Pairs.Count; p++)
            {
                LiftLoopTopology.Pair pair = m_Topology.Pairs[p];
                int stationA = pair.StationLo, stationB = pair.StationHi;
                if (pair.Status == LiftLoopTopology.PairStatus.NotTwoChains)
                {
                    m_UnclosedChainCount += pair.ChainIndices.Count;
                    m_BuildNotes.Add($"stations ({stationA},{stationB}): {pair.ChainIndices.Count} chain(s), need exactly 2");
                    continue;
                }
                if (pair.Status == LiftLoopTopology.PairStatus.GapOutOfRange)
                {
                    m_UnclosedChainCount += 2;
                    m_BuildNotes.Add(Inv($"stations ({stationA},{stationB}): track ropes {pair.BaseGapM:F1} m apart at the base / ") +
                                     Inv($"{pair.TopGapM:F1} m at the top (want {LiftLoopTopology.ShuttleGapMinM:F0}–") +
                                     Inv($"{LiftLoopTopology.ShuttleGapMaxM:F0}) — not a shuttle"));
                    continue;
                }
                if (pair.TypeIndex < 0)
                {
                    m_BuildNotes.Add($"shuttle between stations {stationA} and {stationB} mixes lift types — not moving");
                    continue;
                }

                LiftLoopTopology.Station baseStation = m_TopoStations[pair.Base], topStation = m_TopoStations[pair.Top];
                var shuttle = new Shuttle
                {
                    m_BaseStation = stations[pair.Base],
                    m_TopStation = stations[pair.Top],
                    m_BaseRotation = stationTransforms[pair.Base].m_Rotation,
                    m_TopRotation = stationTransforms[pair.Top].m_Rotation,
                    m_TypeIndex = pair.TypeIndex,
                    m_BaseByRole = pair.BaseByRole,
                    m_UpLaneDecided = pair.UpLaneDecided,
                    m_UpLaneSwapped = pair.UpLaneSwapped,
                    m_BaseStationIndex = pair.Base,
                    m_TopStationIndex = pair.Top,
                };
                BuildPath(lanes, pair.A, baseStation, pair.ABaseDock, pair.ABaseRunM, topStation, pair.ATopDock, pair.ATopRunM,
                          shuttle.m_A);
                BuildPath(lanes, pair.B, baseStation, pair.BBaseDock, pair.BBaseRunM, topStation, pair.BTopDock, pair.BTopRunM,
                          shuttle.m_B);
                NoteRefusedDock(stationA, stationB, "base", "A", pair.ABaseRefusedM);
                NoteRefusedDock(stationA, stationB, "base", "B", pair.BBaseRefusedM);
                NoteRefusedDock(stationA, stationB, "top", "A", pair.ATopRefusedM);
                NoteRefusedDock(stationA, stationB, "top", "B", pair.BTopRefusedM);
                if (shuttle.m_A.m_Length <= 1.0 || shuttle.m_B.m_Length <= 1.0)
                {
                    m_UnclosedChainCount += 2;
                    m_BuildNotes.Add(Inv($"stations ({stationA},{stationB}): shuttle refused — path too short ") +
                                     Inv($"(A {shuttle.m_A.m_Length:F2} m / B {shuttle.m_B.m_Length:F2} m)"));
                    continue;
                }
                m_Shuttles.Add(shuttle);
                m_BuildNotes.Add(Inv($"stations ({stationA},{stationB}): shuttle, path A {shuttle.m_A.m_Length:F1} m / B ") +
                                 Inv($"{shuttle.m_B.m_Length:F1} m, {shuttle.m_A.m_Segments.Count} / {shuttle.m_B.m_Segments.Count} ") +
                                 Inv($"segments (base = station {pair.Base}, {(pair.BaseByRole ? "by role" : "by height")}; up lane = ") +
                                 Inv($"right looking uphill, {(pair.UpLaneDecided ? pair.UpLaneSwapped ? "swapped" : "as found" : "undecided, as found")}; ") +
                                 Inv($"docks base {pair.ABaseRunM:F1} / {pair.BBaseRunM:F1} m, top {pair.ATopRunM:F1} / {pair.BTopRunM:F1} m; ") +
                                 Inv($"type {pair.TypeIndex})"));
            }
        }

        /// <summary>Catalog Phase C: the topology's stand-in lanes. Node ids are interned in first-seen lane order (start,
        /// then end); the ends are pre-sampled with exactly the calls GetChainEndpoint made (was :590-591).</summary>
        private void FillTopologyLanes(List<CableLane> lanes)
        {
            m_TopoLanes.Clear();
            m_NodeIds.Clear();
            for (int i = 0; i < lanes.Count; i++)
            {
                CableLane lane = lanes[i];
                int startNode = NodeId(lane.m_Start);
                int endNode = NodeId(lane.m_End);
                m_TopoLanes.Add(new LiftLoopTopology.Lane
                {
                    StartNode = startNode,
                    EndNode = endNode,
                    StartOwner = lane.m_Start.GetOwnerIndex(),
                    EndOwner = lane.m_End.GetOwnerIndex(),
                    P0 = MathUtils.Position(lane.m_Bezier, 0f),
                    P1 = MathUtils.Position(lane.m_Bezier, 1f),
                    T0 = MathUtils.Tangent(lane.m_Bezier, 0f),
                    T1 = MathUtils.Tangent(lane.m_Bezier, 1f),
                    TypeIndex = lane.m_TypeIndex,
                });
            }
        }

        private int NodeId(PathNode node)
        {
            if (!m_NodeIds.TryGetValue(node, out int id))
            {
                id = m_NodeIds.Count;
                m_NodeIds.Add(node, id);
            }
            return id;
        }

        /// <summary>Catalog Phase C: the chain-level results (count, unclosed chains, diagnostics, "nothing to chain" /
        /// "unclosed" notes) — written first, exactly as the chain loop wrote them (was :266-281).</summary>
        private void CopyTopologyChains()
        {
            m_ChainCount = m_Topology.Chains.Count;
            m_UnclosedChainCount = m_Topology.UnclosedChainCount;
            for (int c = 0; c < m_Topology.Diags.Count; c++)
            {
                LiftLoopTopology.ChainDiag d = m_Topology.Diags[c];
                m_ChainDiags.Add(new ChainDiag
                {
                    m_Links = d.Links,
                    m_StartOwner = d.StartOwner,
                    m_EndOwner = d.EndOwner,
                    m_StartStation = d.StartStation,
                    m_EndStation = d.EndStation,
                    m_StartHow = d.StartHow,
                    m_EndHow = d.EndHow,
                    m_StartPos = d.StartPos,
                    m_EndPos = d.EndPos,
                });
            }
            m_BuildNotes.AddRange(m_Topology.Notes);
        }

        private static float3 DockWorld(Game.Objects.Transform transform, float3 dockLocal)
        {
            return math.all(math.isfinite(dockLocal))
                ? Game.Objects.ObjectUtils.LocalToWorld(transform, dockLocal)
                : new float3(float.NaN);
        }

        /// <summary>Catalog Phase C: one path = [base dock → rope start] + the chain's lanes + [rope end → top dock]. A dock
        /// run is synthesized when the dock is assigned (≥ 0) and the run exceeds 0.05 m (the bullwheel turn's threshold,
        /// AppendTerminalTurn); otherwise the path starts / ends at the rope end.</summary>
        private void BuildPath(List<CableLane> lanes, LiftLoopTopology.Chain chain, LiftLoopTopology.Station baseStation,
            sbyte baseDock, float baseRunM, LiftLoopTopology.Station topStation, sbyte topDock, float topRunM, Path path)
        {
            LiftLoopTopology.Endpoint(m_TopoLanes, chain, atEnd: false, out float3 start, out _);
            LiftLoopTopology.Endpoint(m_TopoLanes, chain, atEnd: true, out float3 end, out _);
            double cum = 0.0;
            path.m_BaseEnd = start;
            path.m_TopEnd = end;
            path.m_BaseRunM = 0f;
            path.m_TopRunM = 0f;
            path.m_BaseDock = baseDock;
            path.m_TopDock = topDock;
            if (baseDock >= 0 && baseRunM > 0.05f)
            {
                float3 dock = baseDock == 0 ? baseStation.DockA : baseStation.DockB;
                AppendLine(dock, start, path, ref cum);
                path.m_BaseEnd = dock;
                path.m_BaseRunM = baseRunM;
            }
            AppendChain(lanes, chain, path, ref cum);
            if (topDock >= 0 && topRunM > 0.05f)
            {
                float3 dock = topDock == 0 ? topStation.DockA : topStation.DockB;
                AppendLine(end, dock, path, ref cum);
                path.m_TopEnd = dock;
                path.m_TopRunM = topRunM;
            }
            path.m_Length = cum;
        }

        /// <summary>Round-3 contract review note: the Shuttle-mode notes are formatted in the invariant culture, because
        /// TramShuttleSystem.WarnRefusals matches them by literal prefix (and logs numbers invariant, §4.9). Shuttle mode only:
        /// the Loop notes keep their current-culture interpolation (bit-identical to round 2).</summary>
        private static string Inv(System.FormattableString text)
        {
            return text.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private void NoteRefusedDock(int stationA, int stationB, string where, string chain, float refusedM)
        {
            if (refusedM > 0f)
                m_BuildNotes.Add(Inv($"stations ({stationA},{stationB}): {where} dock run {refusedM:F1} m over ") +
                                 Inv($"{LiftLoopTopology.MaxDockRunM:F0} m — cabin {chain} dwells at its rope end"));
        }

        // ── Loop closing ──────────────────────────────────────────────────────────────
        /// <summary>Catalog Phase C (round-3-contract §2.3): LiftLoopTopology already oriented the chains (A base → top,
        /// B top → base; was OrientChain here) and passed the gap check (was :423-430 here, now ArcSane in the topology on
        /// the same endpoints); from "double cum" on this is the geometry as before.</summary>
        private bool TryCloseLoop(List<CableLane> lanes, LiftLoopTopology.Chain chainA, LiftLoopTopology.Chain chainB,
            float3 baseWheel, float3 topWheel, out Loop loop, out string why)
        {
            loop = new Loop();
            why = null;

            // Terminal geometry for the two bullwheel turns.
            LiftLoopTopology.Endpoint(m_TopoLanes, chainA, atEnd: true, out float3 aTop, out float3 aTopTangent);
            LiftLoopTopology.Endpoint(m_TopoLanes, chainB, atEnd: false, out float3 bTop, out _);
            LiftLoopTopology.Endpoint(m_TopoLanes, chainB, atEnd: true, out float3 bBase, out float3 bBaseTangent);
            LiftLoopTopology.Endpoint(m_TopoLanes, chainA, atEnd: false, out float3 aBase, out _);

            double cum = 0.0;
            AppendChain(lanes, chainA, loop, ref cum);
            if (!AppendTerminalTurn(aTop, bTop, aTopTangent, topWheel, loop, ref cum, out string topNote))
            {
                why = "top turn degenerate: " + topNote;
                return false;
            }
            AppendChain(lanes, chainB, loop, ref cum);
            if (!AppendTerminalTurn(bBase, aBase, bBaseTangent, baseWheel, loop, ref cum, out string baseNote))
            {
                why = "base turn degenerate: " + baseNote;
                return false;
            }
            m_BuildNotes.Add($"top turn: {topNote}; base turn: {baseNote}");

            loop.m_Length = cum;
            if (loop.m_Length <= 1.0)
            {
                why = $"loop too short ({loop.m_Length:F2} m)";
                return false;
            }
            return true;
        }

        /// <summary>Longest plausible straight run from a chain terminal to the station's
        /// bullwheel tangent point (the base terminal sits ~7 m out, a line-first top
        /// terminal ~10 m; anything beyond is a mis-located wheel — fall back).</summary>
        private const float MaxWheelRunMeters = 30f;

        /// <summary>The turnaround at a station (entry 103). With a known bullwheel center W
        /// (rope height, station-local from the terminal generator, mapped to world): a
        /// straight run from the terminal to the wheel's tangent point, a half-circle of
        /// the rope gauge around W bulging into the station, and a straight run back out to
        /// the other terminal — the rope's actual path (entry tower → dive → wheel → dive
        /// → entry tower), so chairs visibly round the bullwheel. Without a wheel (unknown
        /// station prefab, or implausible geometry): the milestone-2 semicircle between
        /// the two terminals.</summary>
        private static bool AppendTerminalTurn(float3 from, float3 to, float3 inTangent, float3 wheel,
            Loop loop, ref double cum, out string note)
        {
            if (math.all(math.isfinite(wheel)))
            {
                float3 mid = (from + to) * 0.5f;
                float3 d = wheel - mid;
                d.y = 0f;                                   // approach direction, horizontal
                float r = math.max(0.5f, math.distance(from, to) * 0.5f);
                float3 l = from - to;
                if (math.lengthsq(d) > 0.01f)
                {
                    d = math.normalize(d);
                    l -= d * math.dot(l, d);                // lateral, perpendicular to the approach
                    if (math.lengthsq(l) > 1e-4f)
                    {
                        l = math.normalize(l);
                        float3 p1 = wheel + l * r;          // tangent point on the incoming side
                        float3 p2 = wheel - l * r;          // tangent point on the outgoing side
                        float run1 = math.distance(from, p1), run2 = math.distance(p2, to);
                        bool ahead = math.dot(p1 - from, d) > 0f;
                        if (ahead && run1 <= MaxWheelRunMeters && run2 <= MaxWheelRunMeters)
                        {
                            if (run1 > 0.05f) AppendLine(from, p1, loop, ref cum);
                            if (!AppendArc(p1, p2, d, loop, ref cum))
                            {
                                note = "wheel arc degenerate";
                                return false;
                            }
                            if (run2 > 0.05f) AppendLine(p2, to, loop, ref cum);
                            note = $"wheel at {Fmt(wheel)}, runs {run1:F1} m in / {run2:F1} m out, r {r:F2}";
                            return true;
                        }
                        note = $"wheel {Fmt(wheel)} implausible (ahead={ahead}, runs {run1:F1}/{run2:F1} m) — plain arc";
                    }
                    else note = "terminal gap parallel to the approach — plain arc";
                }
                else note = "wheel over the terminal midpoint — plain arc";
            }
            else note = "no wheel for this station — plain arc";

            if (!AppendArc(from, to, inTangent, loop, ref cum))
            {
                note += " (degenerate)";
                return false;
            }
            return true;
        }

        private static string Fmt(float3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";

        // Catalog Phase C (round-3-contract §2.1): CommonType (was :520-540) moved to LiftLoopTopology, unchanged.

        /// <summary>Straight segment as a linear bezier (same sampling path as lanes).</summary>
        private static void AppendLine(float3 a, float3 b, Loop loop, ref double cum)
        {
            float length = math.max(math.distance(a, b), 0.01f);
            loop.m_Segments.Add(new Segment
            {
                m_IsArc = false,
                m_Owner = Entity.Null,
                m_Bezier = new Bezier4x3(a, math.lerp(a, b, 1f / 3f), math.lerp(a, b, 2f / 3f), b),
                m_Reversed = false,
                m_Length = length,
                m_CumStart = cum,
            });
            cum += length;
        }

        // Catalog Phase C (round-3-contract §2.1): ArcSane (was :558-562), OrientChain (:564-578) and GetChainEndpoint
        // (:580-595) moved to LiftLoopTopology (ArcSane, OrientChain, Endpoint), unchanged but for the stand-ins.

        private void AppendChain(List<CableLane> lanes, LiftLoopTopology.Chain chain, Loop loop, ref double cum)
        {
            for (int i = 0; i < chain.Links.Count; i++)
            {
                LiftLoopTopology.Link link = chain.Links[i];
                CableLane lane = lanes[link.Lane];
                loop.m_Segments.Add(new Segment
                {
                    m_IsArc = false,
                    m_Owner = lane.m_Owner,
                    m_Bezier = lane.m_Bezier,
                    m_Reversed = link.Reversed,
                    m_Length = math.max(lane.m_Length, 0.01f),
                    m_CumStart = cum,
                });
                loop.m_MemberOwners.Add(lane.m_Owner);
                cum += math.max(lane.m_Length, 0.01f);
            }
        }

        /// <summary>Catalog Phase C: AppendChain for a shuttle Path (the same segments and owners).</summary>
        private void AppendChain(List<CableLane> lanes, LiftLoopTopology.Chain chain, Path path, ref double cum)
        {
            for (int i = 0; i < chain.Links.Count; i++)
            {
                LiftLoopTopology.Link link = chain.Links[i];
                CableLane lane = lanes[link.Lane];
                path.m_Segments.Add(new Segment
                {
                    m_IsArc = false,
                    m_Owner = lane.m_Owner,
                    m_Bezier = lane.m_Bezier,
                    m_Reversed = link.Reversed,
                    m_Length = math.max(lane.m_Length, 0.01f),
                    m_CumStart = cum,
                });
                path.m_MemberOwners.Add(lane.m_Owner);
                cum += math.max(lane.m_Length, 0.01f);
            }
        }

        /// <summary>Catalog Phase C: AppendLine for a shuttle Path (the same linear bezier, the same 0.01 m floor).</summary>
        private static void AppendLine(float3 a, float3 b, Path path, ref double cum)
        {
            float length = math.max(math.distance(a, b), 0.01f);
            path.m_Segments.Add(new Segment
            {
                m_IsArc = false,
                m_Owner = Entity.Null,
                m_Bezier = new Bezier4x3(a, math.lerp(a, b, 1f / 3f), math.lerp(a, b, 2f / 3f), b),
                m_Reversed = false,
                m_Length = length,
                m_CumStart = cum,
            });
            cum += length;
        }

        /// <summary>Semicircle from 'from' to 'to', bulging along the incoming travel
        /// tangent (into the station, around the bullwheel).</summary>
        private static bool AppendArc(float3 from, float3 to, float3 inTangent, Loop loop, ref double cum)
        {
            float3 center = (from + to) * 0.5f;
            float radius = math.distance(from, to) * 0.5f;
            float3 u = (from - center) / math.max(radius, 1e-3f);
            // Bulge = incoming travel direction, made perpendicular to u (they are ~90 deg
            // apart by construction; guard the degenerate case anyway).
            float3 b = inTangent - u * math.dot(inTangent, u);
            if (!math.all(math.isfinite(b)) || math.lengthsq(b) < 1e-6f)
                return false;
            b = math.normalize(b);

            float length = math.PI * radius;
            loop.m_Segments.Add(new Segment
            {
                m_IsArc = true,
                m_Owner = Entity.Null,
                m_ArcCenter = center,
                m_ArcU = u,
                m_ArcB = b,
                m_ArcRadius = radius,
                m_Length = length,
                m_CumStart = cum,
            });
            cum += length;
            return true;
        }

        // ── Sampling ──────────────────────────────────────────────────────────────────
        /// <summary>Position + unit travel tangent at loop distance s (caller wraps s into
        /// [0, m_Length)). Constant-speed: lanes via MathUtils.ClampLength (arc-length cut,
        /// same 16-chord metric as Curve.m_Length), arcs analytically.</summary>
        public static void Sample(Loop loop, double s, out float3 position, out float3 travelTangent)
        {
            SampleSegments(loop.m_Segments, s, out position, out travelTangent);
        }

        /// <summary>Catalog Phase C (round-3-contract §2.3): position + unit tangent in the BASE → TOP direction at
        /// distance s along a shuttle path, s clamped to [0, m_Length] (NaN → 0). Allocation-free (the mover calls it
        /// every frame). An empty path returns its base end and +z.</summary>
        public static void Sample(Path path, double s, out float3 position, out float3 baseToTopTangent)
        {
            if (path.m_Segments.Count == 0)
            {
                position = path.m_BaseEnd;
                baseToTopTangent = new float3(0f, 0f, 1f);
                return;
            }
            double clamped = s > 0.0 ? (s < path.m_Length ? s : path.m_Length) : 0.0;
            SampleSegments(path.m_Segments, clamped, out position, out baseToTopTangent);
        }

        /// <summary>Catalog Phase C (round-3-contract §2.3): Sample(Loop)'s body, moved verbatim (was :651-689) so a
        /// shuttle Path samples exactly as a loop does. The caller bounds s.</summary>
        private static void SampleSegments(List<Segment> segments, double s, out float3 position, out float3 travelTangent)
        {
            // Binary search the segment table (cumulative starts are sorted).
            int lo = 0, hi = segments.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (segments[mid].m_CumStart <= s) lo = mid;
                else hi = mid - 1;
            }
            Segment seg = segments[lo];
            float d = math.clamp((float)(s - seg.m_CumStart), 0f, seg.m_Length);

            if (seg.m_IsArc)
            {
                float theta = d / math.max(seg.m_ArcRadius, 1e-3f);
                math.sincos(theta, out float sin, out float cos);
                position = seg.m_ArcCenter + seg.m_ArcRadius * (seg.m_ArcU * cos + seg.m_ArcB * sin);
                travelTangent = seg.m_ArcB * cos - seg.m_ArcU * sin;
                return;
            }

            var t = new Bounds1(0f, 1f);
            if (seg.m_Reversed)
            {
                // d meters of travel from the t=1 end toward t=0.
                MathUtils.ClampLengthInverse(seg.m_Bezier, ref t, d);
                position = MathUtils.Position(seg.m_Bezier, t.min);
                travelTangent = -MathUtils.Tangent(seg.m_Bezier, t.min);
            }
            else
            {
                MathUtils.ClampLength(seg.m_Bezier, ref t, d);
                position = MathUtils.Position(seg.m_Bezier, t.max);
                travelTangent = MathUtils.Tangent(seg.m_Bezier, t.max);
            }
            travelTangent = math.normalizesafe(travelTangent, new float3(0f, 0f, 1f));
        }

        /// <summary>Round L1, OD-7 (a) restart seeding (hub-u2-spec.md §5.4): the loop arc parameter
        /// s ∈ [0, m_Length) of <paramref name="position"/> projected onto the LANE segments whose
        /// owner is <paramref name="owner"/> (a chair's own section; both of its cable lanes are in
        /// the loop, one per side, so the nearest one is the chair's own side — the side guard of
        /// ChairPinSystem's static pin, header note 3). The inverse of Sample on that segment:
        /// forward travel measures MathUtils.Length(bezier, [0, t]), reversed travel [t, 1] (the
        /// same metric ClampLength / ClampLengthInverse cut by in Sample). Arcs are never owned.
        /// Returns −1 when the owner has no lane segment in the loop (the caller keeps the
        /// entity-index grid as the fallback). The caller passes the chair's GRIP point (pivot +
        /// grip-top offset), which lies on the cable, not the pivot 2.4 m below it.</summary>
        internal static double ProjectOnOwner(Loop loop, Entity owner, float3 position)
        {
            return ProjectOnOwner(loop, owner, position, out _);
        }

        /// <summary>ProjectOnOwner with the distance from <paramref name="position"/> to the chosen
        /// lane point (float.MaxValue when the owner has no segment). ADDITIVE overload: the caller
        /// uses the distance to tell a chair standing on its own section (parked or just spawned)
        /// from one that is riding elsewhere on the loop (a loaded save).</summary>
        internal static double ProjectOnOwner(Loop loop, Entity owner, float3 position, out float distance)
        {
            double best = -1.0;
            float bestDistance = float.MaxValue;
            var segments = loop.m_Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                Segment seg = segments[i];
                if (seg.m_IsArc || seg.m_Owner != owner)
                    continue;
                float d = MathUtils.Distance(seg.m_Bezier, position, out float t);
                if (d >= bestDistance)
                    continue;
                bestDistance = d;
                float along = seg.m_Reversed
                    ? MathUtils.Length(seg.m_Bezier, new Bounds1(t, 1f))
                    : MathUtils.Length(seg.m_Bezier, new Bounds1(0f, t));
                best = seg.m_CumStart + math.clamp(along, 0f, seg.m_Length);
            }
            if (best >= loop.m_Length)
                best -= loop.m_Length;
            distance = bestDistance;
            return best;
        }
    }
}
