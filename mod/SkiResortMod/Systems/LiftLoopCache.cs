// Orders a drawn chairlift's cable lanes into closed travel loops (Loop mode) and builds open two-chain paths for shuttles (Shuttle mode).
// The Game-side adapter of the pure LiftLoopTopology.cs. It interns PathNodes to int ids, pre-samples lane ends, runs the topology and
// writes the pair notes and loops in pair order.
// Lanes chain by exact PathNode equality, and the walk STOPS at station anchors. Two lifts sharing a station share its stub PathNodes, so a
// walk that only classified terminals would merge them into one loop.
// Chairs on edges outside any complete loop are reported back for static pinning, never an exception. Lengths run in double
// (loop phase accumulates speed * frameIndex). Loop.m_BaseStation is the height tie-break, not the hub's by-prefab base.
// Engine notes: EngineNotes/LiftLoopCache.md

using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Pathfind;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    internal sealed class LiftLoopCache
    {
        internal struct CableLane
        {
            public Entity m_Lane;
            public Entity m_Owner;      // edge or node entity of the chairlift net
            public Bezier4x3 m_Bezier;
            public PathNode m_Start;
            public PathNode m_End;
            public float m_Length;      // Curve.m_Length (vanilla metric)
            public int m_TypeIndex; // SkiCatalog.Lifts index of the owner's line prefab
        }

        internal struct Segment
        {
            public bool m_IsArc;
            public Entity m_Owner;      // lane segments: the lane's owner (edge/node). Arcs: Entity.Null
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
            /// <summary>The station entities this loop runs between (the pairing's lower terminal = "base" by height, the other = "top").
            /// Unordered for consumers.</summary>
            public Entity m_BaseStation, m_TopStation;
            /// <summary>Owner entities (edges/nodes) of the member lanes. Chairs belong to this loop when their Owner is in this
            /// set.</summary>
            public readonly HashSet<Entity> m_MemberOwners = new HashSet<Entity>();
            public readonly List<Entity> m_Chairs = new List<Entity>();
            public readonly List<float> m_Drops = new List<float>();
            public readonly List<double> m_Offsets = new List<double>();
            /// <summary>The common SkiCatalog.Lifts index of the member lanes. −1 = they span several lift types (no roster).</summary>
            public int m_TypeIndex = -1;
            /// <summary>Carrier speed along this loop, m/s (ChairPinSystem sets it from the row). 0 = not moving.</summary>
            public double m_SpeedMps;
            /// <summary>The up-lane rule decided this loop (false = the two chains could not be told apart: kept as found). The second
            /// field says whether it swapped the chain order found.</summary>
            public bool m_UpLaneDecided, m_UpLaneSwapped;
            /// <summary>ChairPinSystem's far-loop pace state (LiftMoverMath.LoopPace, seeded at each rebuild) and the box its chairs can
            /// occupy (segments + hang). The box is set when the mover's flat arrays are built. Mover data only: nothing in this file reads
            /// them.</summary>
            public LiftMoverMath.LoopPace m_Pace;
            public float3 m_BoxMin, m_BoxMax;
            public bool m_BoxKnown;
            /// <summary>The carrier grip round (ChairPinSystem.Grips.cs): per rostered chair (parallel to m_Chairs) its paired grip
            /// (Entity.Null = none: the body takes the rigid pose) and its sway phase. Also the count of paired grips and the wind state
            /// (kept per base station across rebuilds). Mover data only: nothing in this file reads them.</summary>
            public readonly List<Entity> m_Grips = new List<Entity>();
            public readonly List<float> m_SwayPhases = new List<float>();
            public int m_GripCount;
            public CarrierWind m_Wind;
        }

        public readonly List<Loop> m_Loops = new List<Loop>();
        public int m_ChainCount;
        public int m_UnclosedChainCount;


        internal sealed class Path
        {
            public readonly List<Segment> m_Segments = new List<Segment>();
            public readonly HashSet<Entity> m_MemberOwners = new HashSet<Entity>();
            public double m_Length;                      // Σ segment lengths
            public float3 m_BaseEnd, m_TopEnd;           // track-rope points where the path starts / ends (docks, else rope ends)
            public float m_BaseRunM, m_TopRunM;          // synthesized dock runs (0 = none)
            /// <summary>The station dock each end uses. 0 = DockA = hosted entry 0, 1 = DockB. The tram base's A is the +X bay, the top's A
            /// the −X bay. −1 = none: the path ends at the rope end.</summary>
            public sbyte m_BaseDock = -1, m_TopDock = -1;
        }

        /// <summary>One tram between two stations: chain A (the up lane, right looking uphill) and chain B, both base → top.
        /// Cabin A rides m_A, cabin B rides m_B.</summary>
        internal sealed class Shuttle
        {
            public readonly Path m_A = new Path(), m_B = new Path();
            public Entity m_BaseStation, m_TopStation; // base by role: the cabins' host
            public quaternion m_BaseRotation, m_TopRotation;   // station rotations (the dock frames: rope point + rotation)
            public int m_TypeIndex = -1;
            public bool m_BaseByRole, m_UpLaneDecided, m_UpLaneSwapped;
            public int m_BaseStationIndex = -1, m_TopStationIndex = -1;
            public double MeanLengthM => 0.5 * (m_A.m_Length + m_B.m_Length);
            /// <summary>MeanLengthM rounded to 1 cm: the length to pass to ShuttleProfile, so a float-noise rebuild cannot shift the
            /// cycle.</summary>
            public double ProfileLengthM => ShuttleProfile.QuantizeLengthM(MeanLengthM);
        }

        public readonly List<Shuttle> m_Shuttles = new List<Shuttle>();

        private readonly LiftLoopTopology m_Topology = new LiftLoopTopology();
        private readonly List<LiftLoopTopology.Lane> m_TopoLanes = new List<LiftLoopTopology.Lane>();
        private readonly List<LiftLoopTopology.Station> m_TopoStations = new List<LiftLoopTopology.Station>();
        private readonly Dictionary<PathNode, int> m_NodeIds = new Dictionary<PathNode, int>();

        // Build diagnostics: what the last Build saw, per chain, for the developer-tab dump. Plain data, rebuilt every Build, never
        // consulted by Sample.
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


        public void Clear()
        {
            m_Loops.Clear();
            m_ChainCount = 0;
            m_UnclosedChainCount = 0;
            m_ChainDiags.Clear();
            m_BuildNotes.Clear();
            m_Shuttles.Clear();
        }

        /// <summary>Builds loops from harvested lanes + station entities (with world positions, for base-first orientation).
        /// <paramref name="stationByOwnerIndex"/> maps entity INDEX to station list index for each station AND its children (sub-objects,
        /// sub-net nodes/edges). PathNode owners are stored as indices. Never throws on bad topology. Lifts that do not close produce no
        /// loop and count as unclosed chains. The reasons land in m_BuildNotes/m_ChainDiags. <paramref name="stationRoles"/>[i] is the
        /// catalog role of stations[i] (0 Base, 1 Top). Null or short = unknown, which keeps the lower-terminal rule.</summary>
        public void Build(List<CableLane> lanes, List<Entity> stations, List<float3> stationPositions,
            Dictionary<int, int> stationByOwnerIndex, List<float3> stationWheelCenters, List<sbyte> stationRoles = null)
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
                    Role = stationRoles != null && i < stationRoles.Count ? stationRoles[i] : (sbyte)-1,
                    DockA = new float3(float.NaN),
                    DockB = new float3(float.NaN),
                });
            }
            m_Topology.Build(m_TopoLanes, m_TopoStations, stationByOwnerIndex, LiftLoopTopology.Mode.Loop);
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
                    // Sanity (LiftLoopTopology.ArcSane on the same endpoints): the turn diameter is the cable gauge (expect ~3.6 m). Wildly
                    // off means the anchors are not where this model thinks. Degrade to static pinning.
                    string gapWhy = $"terminal gap out of range (top {pair.TopGapM:F2} m, base {pair.BaseGapM:F2} m; want 1–10)";
                    m_UnclosedChainCount += 2;
                    m_BuildNotes.Add($"stations ({stationA},{stationB}): closure failed — {gapWhy}");
                    continue;
                }
                // Base station = the Base-role station, else the lower of the two terminals (LiftLoopTopology). Chain A = the up lane,
                // oriented base → top. Chain B is oriented top → base.
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
                loop.m_BaseStation = stations[baseStation]; // pair identity
                loop.m_TopStation = stations[topStation];
                loop.m_UpLaneDecided = decided;
                loop.m_UpLaneSwapped = swap;

                // The loop's lift type = the common type of its member lanes.
                loop.m_TypeIndex = pair.TypeIndex;
                if (loop.m_TypeIndex < 0)
                    m_BuildNotes.Add($"loop between stations {stationA} and {stationB} mixes lift types — not moving");
                m_Loops.Add(loop);
                m_BuildNotes.Add($"stations ({stationA},{stationB}): loop closed, {loop.m_Length:F1} m, " +
                                 $"{loop.m_Segments.Count} segments (base = station {baseStation}; up lane = right looking " +
                                 $"uphill, {(decided ? swap ? "swapped" : "as found" : "undecided, as found")}; type {loop.m_TypeIndex})");
            }
        }

        /// <summary>The Shuttle-mode build (TramShuttleSystem). One Shuttle per station pair whose two chains pass the shuttle gap check
        /// and share one lift type. Both chains are oriented base → top (base = the Base-ROLE station). Each is extended by a straight run
        /// from / to its dock (StationDef.DockLocalA / B → world, ObjectUtils.cs:208-211). <paramref name="stationDefs"/>[i] is the catalog
        /// row of stations[i] (null = unknown: no role, no docks). Never throws on bad topology: refusals land in m_BuildNotes (one per
        /// pair).</summary>
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
            // The tram's terminal is its stub-tip track rope, 29-30 m from the station origin but ~12 m from its dock. Shuttle mode owns
            // terminals by dock reach too (LiftLoopTopology.ClassifyShuttleTerminal). Loop mode is untouched.
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

        /// <summary>The topology's stand-in lanes. Node ids are interned in first-seen lane order (start, then end). The ends are
        /// pre-sampled with MathUtils.Position / Tangent.</summary>
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

        /// <summary>One path = [base dock → rope start] + the chain's lanes + [rope end → top dock]. A dock run is synthesized when the
        /// dock is assigned (≥ 0) and the run exceeds 0.05 m (the bullwheel turn's threshold, AppendTerminalTurn). Otherwise the path
        /// starts / ends at the rope end.</summary>
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

        /// <summary>Shuttle-mode notes are formatted in the invariant culture, because TramShuttleSystem.WarnRefusals matches them by
        /// literal prefix (and logs numbers invariant). Shuttle mode only: the Loop notes keep their current-culture interpolation.</summary>
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

        /// <summary>LiftLoopTopology already oriented the chains (A base → top, B top → base) and passed the gap check (ArcSane). From
        /// "double cum" on, this is the geometry.</summary>
        private bool TryCloseLoop(List<CableLane> lanes, LiftLoopTopology.Chain chainA, LiftLoopTopology.Chain chainB,
            float3 baseWheel, float3 topWheel, out Loop loop, out string why)
        {
            loop = new Loop();
            why = null;

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

        /// <summary>Longest plausible straight run from a chain terminal to the station's bullwheel tangent point (the base terminal sits
        /// ~7 m out, a line-first top terminal ~10 m). Anything beyond is a mis-located wheel: treat it as no wheel.</summary>
        private const float MaxWheelRunMeters = 30f;

        /// <summary>The turnaround at a station. With a known bullwheel center W (rope height, station-local from the terminal generator,
        /// mapped to world), it has three parts. First, a straight run from the terminal to the wheel's tangent point. Second, a
        /// half-circle of the rope gauge around W bulging into the station. Third, a straight run back out to the other terminal. These
        /// parts are the rope's actual path (entry tower → dive → wheel → dive → entry tower), so chairs visibly round the bullwheel.
        /// Without a wheel (unknown station prefab, or implausible geometry), it is the semicircle between the two terminals.</summary>
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
            // Bulge = incoming travel direction, made perpendicular to u (they are ~90 deg apart by construction). Guard the degenerate
            // case anyway.
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

        /// <summary>Position + unit travel tangent at loop distance s (caller wraps s into
        /// [0, m_Length)). Constant-speed: lanes via MathUtils.ClampLength (arc-length cut,
        /// same 16-chord metric as Curve.m_Length), arcs analytically.</summary>
        public static void Sample(Loop loop, double s, out float3 position, out float3 travelTangent)
        {
            SampleSegments(loop.m_Segments, s, out position, out travelTangent);
        }

        /// <summary>Sample plus the swing ramp's inputs (the carrier grip round, ChairPinSystem's managed paths). The inputs are:
        ///   * Whether s lies on a guided segment (an arc or a synthesized terminal run, Owner == Entity.Null: LiftLoopSampler.Flatten's
        ///     rule).
        ///   * Otherwise, the distance to the nearest guided segment along the loop, both ways around the wrap (CarrierPose.NoGuidedM when
        ///     there is none).
        /// O(segments): for the managed paths only. The job reads FlatSegment.RunBefore / RunAfter.</summary>
        public static void Sample(Loop loop, double s, out float3 position, out float3 travelTangent, out bool guided, out float distance)
        {
            SampleSegments(loop.m_Segments, s, out position, out travelTangent);
            List<Segment> segs = loop.m_Segments;
            guided = false;
            distance = CarrierPose.NoGuidedM;
            int n = segs.Count;
            if (n == 0)
                return;
            int k = 0;
            while (k + 1 < n && segs[k + 1].m_CumStart <= s)
                k++;
            Segment seg = segs[k];
            if (seg.m_IsArc || seg.m_Owner == Entity.Null)
            {
                guided = true;
                distance = 0f;
                return;
            }
            float d = math.clamp((float)(s - seg.m_CumStart), 0f, seg.m_Length);
            float before = d, after = seg.m_Length - d;
            bool foundBefore = false, foundAfter = false;
            for (int j = 1; j < n && !foundBefore; j++)
            {
                Segment b = segs[((k - j) % n + n) % n];
                if (b.m_IsArc || b.m_Owner == Entity.Null) foundBefore = true;
                else before += b.m_Length;
            }
            for (int j = 1; j < n && !foundAfter; j++)
            {
                Segment a = segs[(k + j) % n];
                if (a.m_IsArc || a.m_Owner == Entity.Null) foundAfter = true;
                else after += a.m_Length;
            }
            if (foundBefore || foundAfter)
                distance = math.min(foundBefore ? before : CarrierPose.NoGuidedM, foundAfter ? after : CarrierPose.NoGuidedM);
        }

        /// <summary>Position + unit tangent in the BASE → TOP direction at
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

        private static void SampleSegments(List<Segment> segments, double s, out float3 position, out float3 travelTangent)
        {
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

        /// <summary>Restart seeding: the loop arc parameter s ∈ [0, m_Length) of <paramref name="position"/> projected onto the LANE
        /// segments whose owner is <paramref name="owner"/> (a chair's own section). Both of its cable lanes are in the loop, one per side,
        /// so the nearest one is the chair's own side. The inverse of Sample on that segment (forward travel measures
        /// MathUtils.Length(bezier, [0, t]), reversed travel [t, 1]). Arcs are never owned. Returns −1 when the owner has no lane segment
        /// in the loop (the caller keeps the entity-index grid as the fallback). The caller passes the chair's GRIP point (pivot + grip-top
        /// offset), which lies on the cable, not the pivot 2.4 m below it.</summary>
        internal static double ProjectOnOwner(Loop loop, Entity owner, float3 position)
        {
            return ProjectOnOwner(loop, owner, position, out _);
        }

        /// <summary>ProjectOnOwner with the distance from <paramref name="position"/> to the chosen lane point (float.MaxValue when the
        /// owner has no segment). It tells a chair standing on its own section (parked or just spawned) from one riding elsewhere on the
        /// loop (a loaded save).</summary>
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
