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
//   2. Recognises STATION TERMINALS by PathNode owner index == station entity index
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
// quantize at metre scale by frame ~1e7). Per-segment lengths use the same metric as
// MathUtils.ClampLength (Curve.m_Length for lanes, exact pi*r for arcs), so sampling
// never runs past a segment end.

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
            /// <summary>Owner entities (edges/nodes) of the member lanes — chairs belong to
            /// this loop when their Owner is in this set.</summary>
            public readonly HashSet<Entity> m_MemberOwners = new HashSet<Entity>();
            public readonly List<Entity> m_Chairs = new List<Entity>();
            public readonly List<float> m_GripTops = new List<float>();
            public readonly List<double> m_Offsets = new List<double>();
        }

        public readonly List<Loop> m_Loops = new List<Loop>();
        public int m_ChainCount;
        public int m_UnclosedChainCount;

        private struct ChainLink
        {
            public int m_LaneIndex;
            public bool m_Reversed;
        }

        private sealed class Chain
        {
            public readonly List<ChainLink> m_Links = new List<ChainLink>();
            public PathNode m_StartTerminal;   // PathNode at the chain's first endpoint
            public PathNode m_EndTerminal;
            public int m_StartStation = -1;    // index into the stations list, -1 = not a station
            public int m_EndStation = -1;
        }

        public void Clear()
        {
            m_Loops.Clear();
            m_ChainCount = 0;
            m_UnclosedChainCount = 0;
        }

        /// <summary>Builds loops from harvested lanes + station entities (with world
        /// positions, for base-first orientation). Never throws on bad topology — lifts
        /// that do not close simply produce no loop and count as unclosed chains.</summary>
        public void Build(List<CableLane> lanes, List<Entity> stations, List<float3> stationPositions)
        {
            Clear();
            if (lanes.Count == 0 || stations.Count == 0)
                return;

            // Endpoint index: PathNode -> lane indices sharing it (exact equality; PathNode
            // hashes on its full search key).
            var nodeMap = new Dictionary<PathNode, List<int>>(lanes.Count * 2);
            for (int i = 0; i < lanes.Count; i++)
            {
                AddEndpoint(nodeMap, lanes[i].m_Start, i);
                AddEndpoint(nodeMap, lanes[i].m_End, i);
            }

            // Station owner-index lookup (PathNode owner is stored as an entity INDEX).
            var stationByOwnerIndex = new Dictionary<int, int>(stations.Count);
            for (int i = 0; i < stations.Count; i++)
                stationByOwnerIndex[stations[i].Index] = i;

            // ── Chain building ─────────────────────────────────────────────────────────
            var chains = new List<Chain>();
            var visited = new bool[lanes.Count];
            for (int i = 0; i < lanes.Count; i++)
            {
                if (visited[i])
                    continue;

                // Walk backward from this lane's start to find the chain's true beginning,
                // then forward-collect. Loop guard: never revisit a lane.
                var chain = new Chain();
                visited[i] = true;

                // Backward extension (collect, then reverse). The walk STOPS at station
                // anchors — shared-station stub PathNodes must never bridge two lifts.
                var backLinks = new List<ChainLink>();
                PathNode cursor = lanes[i].m_Start;
                while (!stationByOwnerIndex.ContainsKey(cursor.GetOwnerIndex()) &&
                       TryTakeNext(nodeMap, lanes, visited, cursor, out int prevLane))
                {
                    // Entering prevLane at 'cursor': if cursor == its END the lane already
                    // points at us (travel = forward when later reversed as a whole).
                    bool enteredAtEnd = lanes[prevLane].m_End.Equals(cursor);
                    backLinks.Add(new ChainLink { m_LaneIndex = prevLane, m_Reversed = enteredAtEnd });
                    cursor = enteredAtEnd ? lanes[prevLane].m_Start : lanes[prevLane].m_End;
                }
                chain.m_StartTerminal = cursor;
                // backLinks were collected walking AWAY from lane i; in chain order they run
                // toward lane i, so reverse the list and flip each link's direction.
                for (int b = backLinks.Count - 1; b >= 0; b--)
                    chain.m_Links.Add(new ChainLink
                    {
                        m_LaneIndex = backLinks[b].m_LaneIndex,
                        m_Reversed = !backLinks[b].m_Reversed
                    });

                chain.m_Links.Add(new ChainLink { m_LaneIndex = i, m_Reversed = false });

                // Forward extension from lane i's end (same station stop).
                cursor = lanes[i].m_End;
                while (!stationByOwnerIndex.ContainsKey(cursor.GetOwnerIndex()) &&
                       TryTakeNext(nodeMap, lanes, visited, cursor, out int nextLane))
                {
                    bool enteredAtStart = lanes[nextLane].m_Start.Equals(cursor);
                    chain.m_Links.Add(new ChainLink { m_LaneIndex = nextLane, m_Reversed = !enteredAtStart });
                    cursor = enteredAtStart ? lanes[nextLane].m_End : lanes[nextLane].m_Start;
                }
                chain.m_EndTerminal = cursor;

                if (stationByOwnerIndex.TryGetValue(chain.m_StartTerminal.GetOwnerIndex(), out int s0))
                    chain.m_StartStation = s0;
                if (stationByOwnerIndex.TryGetValue(chain.m_EndTerminal.GetOwnerIndex(), out int s1))
                    chain.m_EndStation = s1;
                chains.Add(chain);
            }
            m_ChainCount = chains.Count;

            // ── Pair chains by station pair, close each pair into a loop ─────────────────
            var byPair = new Dictionary<(int, int), List<Chain>>();
            foreach (var chain in chains)
            {
                if (chain.m_StartStation < 0 || chain.m_EndStation < 0 ||
                    chain.m_StartStation == chain.m_EndStation)
                {
                    m_UnclosedChainCount++;
                    continue;
                }
                var key = chain.m_StartStation < chain.m_EndStation
                    ? (chain.m_StartStation, chain.m_EndStation)
                    : (chain.m_EndStation, chain.m_StartStation);
                if (!byPair.TryGetValue(key, out var list))
                    byPair[key] = list = new List<Chain>();
                list.Add(chain);
            }

            foreach (var kv in byPair)
            {
                if (kv.Value.Count != 2)
                {
                    m_UnclosedChainCount += kv.Value.Count;
                    continue;
                }
                // Base station = the lower of the two terminals (a ski lift climbs); loop
                // travel starts at the base going up chain A. Arbitrary but deterministic.
                int stationA = kv.Key.Item1, stationB = kv.Key.Item2;
                int baseStation = stationPositions[stationA].y <= stationPositions[stationB].y
                    ? stationA : stationB;
                int topStation = baseStation == stationA ? stationB : stationA;

                if (!TryCloseLoop(lanes, kv.Value[0], kv.Value[1], baseStation, topStation, out Loop loop))
                {
                    m_UnclosedChainCount += 2;
                    continue;
                }
                m_Loops.Add(loop);
            }
        }

        private static void AddEndpoint(Dictionary<PathNode, List<int>> nodeMap, PathNode node, int laneIndex)
        {
            if (!nodeMap.TryGetValue(node, out var list))
                nodeMap[node] = list = new List<int>(2);
            list.Add(laneIndex);
        }

        /// <summary>Finds one unvisited lane sharing this endpoint, marks it visited.</summary>
        private static bool TryTakeNext(Dictionary<PathNode, List<int>> nodeMap, List<CableLane> lanes,
            bool[] visited, PathNode node, out int laneIndex)
        {
            laneIndex = -1;
            if (!nodeMap.TryGetValue(node, out var candidates))
                return false;
            for (int i = 0; i < candidates.Count; i++)
            {
                int c = candidates[i];
                if (!visited[c])
                {
                    visited[c] = true;
                    laneIndex = c;
                    return true;
                }
            }
            return false;
        }

        // ── Loop closing ──────────────────────────────────────────────────────────────
        private bool TryCloseLoop(List<CableLane> lanes, Chain chainA, Chain chainB,
            int baseStation, int topStation, out Loop loop)
        {
            loop = new Loop();

            // Orient both chains: A travels base -> top, B travels top -> base.
            OrientChain(chainA, baseStation);
            OrientChain(chainB, topStation);

            // Terminal geometry for the two bullwheel arcs.
            GetChainEndpoint(lanes, chainA, atEnd: true, out float3 aTop, out float3 aTopTangent);
            GetChainEndpoint(lanes, chainB, atEnd: false, out float3 bTop, out _);
            GetChainEndpoint(lanes, chainB, atEnd: true, out float3 bBase, out float3 bBaseTangent);
            GetChainEndpoint(lanes, chainA, atEnd: false, out float3 aBase, out _);

            // Sanity: the arc diameter is the cable gauge (expect ~3.6 m). Wildly off means
            // the anchors are not where this model thinks — degrade to static pinning.
            if (!ArcSane(aTop, bTop) || !ArcSane(bBase, aBase))
                return false;

            double cum = 0.0;
            AppendChain(lanes, chainA, loop, ref cum);
            if (!AppendArc(aTop, bTop, aTopTangent, loop, ref cum))
                return false;
            AppendChain(lanes, chainB, loop, ref cum);
            if (!AppendArc(bBase, aBase, bBaseTangent, loop, ref cum))
                return false;

            loop.m_Length = cum;
            return loop.m_Length > 1.0;
        }

        private static bool ArcSane(float3 from, float3 to)
        {
            float d = math.distance(from, to);
            return d > 1f && d < 10f;
        }

        private static void OrientChain(Chain chain, int fromStation)
        {
            if (chain.m_StartStation == fromStation)
                return;
            // Reverse link order and per-link direction; swap terminals.
            chain.m_Links.Reverse();
            for (int i = 0; i < chain.m_Links.Count; i++)
            {
                var link = chain.m_Links[i];
                link.m_Reversed = !link.m_Reversed;
                chain.m_Links[i] = link;
            }
            (chain.m_StartTerminal, chain.m_EndTerminal) = (chain.m_EndTerminal, chain.m_StartTerminal);
            (chain.m_StartStation, chain.m_EndStation) = (chain.m_EndStation, chain.m_StartStation);
        }

        /// <summary>World position + unit travel tangent at a chain's first or last point.</summary>
        private static void GetChainEndpoint(List<CableLane> lanes, Chain chain, bool atEnd,
            out float3 position, out float3 travelTangent)
        {
            ChainLink link = chain.m_Links[atEnd ? chain.m_Links.Count - 1 : 0];
            CableLane lane = lanes[link.m_LaneIndex];
            // Travel enters a lane at t=0 (or t=1 when reversed) and exits at the other end.
            float tExit = link.m_Reversed ? 0f : 1f;
            float tEnter = 1f - tExit;
            float t = atEnd ? tExit : tEnter;
            position = MathUtils.Position(lane.m_Bezier, t);
            travelTangent = MathUtils.Tangent(lane.m_Bezier, t);
            if (link.m_Reversed)
                travelTangent = -travelTangent;
            travelTangent = math.normalizesafe(travelTangent, new float3(0f, 0f, 1f));
        }

        private void AppendChain(List<CableLane> lanes, Chain chain, Loop loop, ref double cum)
        {
            for (int i = 0; i < chain.m_Links.Count; i++)
            {
                ChainLink link = chain.m_Links[i];
                CableLane lane = lanes[link.m_LaneIndex];
                loop.m_Segments.Add(new Segment
                {
                    m_IsArc = false,
                    m_Owner = lane.m_Owner,
                    m_Bezier = lane.m_Bezier,
                    m_Reversed = link.m_Reversed,
                    m_Length = math.max(lane.m_Length, 0.01f),
                    m_CumStart = cum,
                });
                loop.m_MemberOwners.Add(lane.m_Owner);
                cum += math.max(lane.m_Length, 0.01f);
            }
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
            // Binary search the segment table (cumulative starts are sorted).
            var segments = loop.m_Segments;
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
                // d metres of travel from the t=1 end toward t=0.
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
    }
}
