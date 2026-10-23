// LoopCacheReference - a frozen transcription of the topology pass of LiftLoopCache.cs (before the topology moved into
// LiftLoopTopology.cs) at SHA-256 d3ae435b756f2e1b281617ef02cb260bb13a12bd8c16bfea1b963fcca2881b81. Lines transcribed:
// Build :175-345 up to the closure (the pairing loop's geometry and loop notes are not here), ClassifyTerminal :347-377,
// AddEndpoint :379-384, TryTakeNext :386-404, TryCloseLoop :407-430 (orientation + gap check; the geometry :432-453 is
// not here), CommonType :520-540, ArcSane :558-562, OrientChain :564-578, GetChainEndpoint :580-595.
//
// Do not edit to follow LiftLoopTopology: this file is the oracle. The only substitutions are the stand-ins the topology
// uses, so both run on identical input:
//   * CableLane -> LiftLoopTopology.Lane; PathNode -> its int id; PathNode.Equals -> ==; cursor.GetOwnerIndex() ->
//     OwnerOf(cursor), a node -> owner table built from the lanes (the generator gives every id one owner, as the
//     64-bit search key does: Game.Pathfind/PathNode.cs:113-116).
//   * MathUtils.Position / Tangent(lane.m_Bezier, t), t in {0, 1} -> the lane's P0 / P1 / T0 / T1.
//   * List<Entity> stations -> List<int> (Entity.Index).
// What the original did after a successful gap check (AppendChain, the two turns, "top turn ...", "loop closed ...", the
// loop-too-short refusal) needs Colossal.Mathematics and is not transcribed; such a pair is recorded as Closed with its
// CommonType (the original computed it right after the closure, :337).

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    internal sealed class LoopCacheReference
    {
        internal struct ChainDiag
        {
            public int m_Links;
            public int m_StartOwner, m_EndOwner;      // PathNode owner entity indices
            public int m_StartStation, m_EndStation;  // station list index or -1
            public string m_StartHow, m_EndHow;       // "owner" / "child" / "pos 8.3m" / "none (nearest 41m)"
            public float3 m_StartPos, m_EndPos;
        }

        internal struct ChainLink
        {
            public int m_LaneIndex;
            public bool m_Reversed;
        }

        internal sealed class Chain
        {
            public readonly List<ChainLink> m_Links = new List<ChainLink>();
            public int m_StartTerminal;   // PathNode at the chain's first endpoint
            public int m_EndTerminal;
            public int m_StartStation = -1;    // index into the stations list, -1 = not a station
            public int m_EndStation = -1;
        }

        /// <summary>What happened to one key of the pairing dictionary</summary>
        internal sealed class PairResult
        {
            public int Lo, Hi, ChainCount;
            public bool TwoChains, Closed;           // Closed = passed the gap check (the geometry is not transcribed)
            public int Base = -1, Top = -1;
            public bool Decided, Swapped;
            public Chain A, B;
            public int ChainIndexA = -1, ChainIndexB = -1;
            public float TopGap, BaseGap;
            public int TypeIndex = -1;               // only for Closed pairs (as the original)
        }

        internal readonly List<Chain> m_Chains = new List<Chain>();
        internal readonly List<PairResult> m_Pairs = new List<PairResult>();
        internal readonly List<ChainDiag> m_ChainDiags = new List<ChainDiag>();
        internal readonly List<string> m_BuildNotes = new List<string>();
        public int m_ChainCount;
        public int m_UnclosedChainCount;

        private const float StationTerminalRadius = 20f;

        private readonly Dictionary<int, int> m_OwnerOf = new Dictionary<int, int>();

        private int OwnerOf(int node)
        {
            return m_OwnerOf[node];
        }

        public void Build(List<LiftLoopTopology.Lane> lanes, List<int> stations, List<float3> stationPositions,
            Dictionary<int, int> stationByOwnerIndex)
        {
            m_Chains.Clear();
            m_Pairs.Clear();
            m_ChainDiags.Clear();
            m_BuildNotes.Clear();
            m_ChainCount = 0;
            m_UnclosedChainCount = 0;
            m_OwnerOf.Clear();
            for (int i = 0; i < lanes.Count; i++)
            {
                Own(lanes[i].StartNode, lanes[i].StartOwner);
                Own(lanes[i].EndNode, lanes[i].EndOwner);
            }

            if (lanes.Count == 0 || stations.Count == 0)
            {
                m_BuildNotes.Add($"nothing to chain: {lanes.Count} lane(s), {stations.Count} station(s)");
                return;
            }

            // Endpoint index: PathNode -> lane indices sharing it (exact equality; PathNode hashes on its full search key)
            var nodeMap = new Dictionary<int, List<int>>(lanes.Count * 2);
            for (int i = 0; i < lanes.Count; i++)
            {
                AddEndpoint(nodeMap, lanes[i].StartNode, i);
                AddEndpoint(nodeMap, lanes[i].EndNode, i);
            }
            for (int i = 0; i < stations.Count; i++)
                stationByOwnerIndex[stations[i]] = i; // the station itself, always

            var chains = m_Chains;
            var visited = new bool[lanes.Count];
            for (int i = 0; i < lanes.Count; i++)
            {
                if (visited[i])
                    continue;

                // Walk backward from this lane's start to find the chain's true beginning, then forward-collect. Loop guard: never
                // revisit a lane.
                var chain = new Chain();
                visited[i] = true;

                // Backward extension (collect, then reverse). The walk stops at station anchors: shared-station stub PathNodes must
                // never bridge two lifts.
                var backLinks = new List<ChainLink>();
                int cursor = lanes[i].StartNode;
                while (!stationByOwnerIndex.ContainsKey(OwnerOf(cursor)) &&
                       TryTakeNext(nodeMap, lanes, visited, cursor, out int prevLane))
                {
                    // Entering prevLane at 'cursor': if cursor == its END the lane already points at us (travel = forward when later
                    // reversed as a whole)
                    bool enteredAtEnd = lanes[prevLane].EndNode == cursor;
                    backLinks.Add(new ChainLink { m_LaneIndex = prevLane, m_Reversed = enteredAtEnd });
                    cursor = enteredAtEnd ? lanes[prevLane].StartNode : lanes[prevLane].EndNode;
                }
                chain.m_StartTerminal = cursor;
                // backLinks were collected walking away from lane i; in chain order they run toward lane i, so reverse the list and
                // flip each link's direction
                for (int b = backLinks.Count - 1; b >= 0; b--)
                    chain.m_Links.Add(new ChainLink
                    {
                        m_LaneIndex = backLinks[b].m_LaneIndex,
                        m_Reversed = !backLinks[b].m_Reversed
                    });

                chain.m_Links.Add(new ChainLink { m_LaneIndex = i, m_Reversed = false });

                // Forward extension from lane i's end (same station stop)
                cursor = lanes[i].EndNode;
                while (!stationByOwnerIndex.ContainsKey(OwnerOf(cursor)) &&
                       TryTakeNext(nodeMap, lanes, visited, cursor, out int nextLane))
                {
                    bool enteredAtStart = lanes[nextLane].StartNode == cursor;
                    chain.m_Links.Add(new ChainLink { m_LaneIndex = nextLane, m_Reversed = !enteredAtStart });
                    cursor = enteredAtStart ? lanes[nextLane].EndNode : lanes[nextLane].StartNode;
                }
                chain.m_EndTerminal = cursor;

                // Terminal classification: owner match first (station or any child of it), position fallback second. Terminals only.
                GetChainEndpoint(lanes, chain, atEnd: false, out float3 startPos, out _);
                GetChainEndpoint(lanes, chain, atEnd: true, out float3 endPos, out _);
                chain.m_StartStation = ClassifyTerminal(chain.m_StartTerminal, startPos, stations,
                    stationPositions, stationByOwnerIndex, out string startHow);
                chain.m_EndStation = ClassifyTerminal(chain.m_EndTerminal, endPos, stations,
                    stationPositions, stationByOwnerIndex, out string endHow);
                chains.Add(chain);
                m_ChainDiags.Add(new ChainDiag
                {
                    m_Links = chain.m_Links.Count,
                    m_StartOwner = OwnerOf(chain.m_StartTerminal),
                    m_EndOwner = OwnerOf(chain.m_EndTerminal),
                    m_StartStation = chain.m_StartStation,
                    m_EndStation = chain.m_EndStation,
                    m_StartHow = startHow,
                    m_EndHow = endHow,
                    m_StartPos = startPos,
                    m_EndPos = endPos,
                });
            }
            m_ChainCount = chains.Count;

            var byPair = new Dictionary<(int, int), List<Chain>>();
            for (int c = 0; c < chains.Count; c++)
            {
                Chain chain = chains[c];
                if (chain.m_StartStation < 0 || chain.m_EndStation < 0 ||
                    chain.m_StartStation == chain.m_EndStation)
                {
                    m_UnclosedChainCount++;
                    m_BuildNotes.Add($"chain {c}: unclosed — terminals station {chain.m_StartStation} → " +
                                     $"{chain.m_EndStation} ({m_ChainDiags[c].m_StartHow} / {m_ChainDiags[c].m_EndHow})");
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
                var result = new PairResult { Lo = kv.Key.Item1, Hi = kv.Key.Item2, ChainCount = kv.Value.Count };
                m_Pairs.Add(result);
                if (kv.Value.Count != 2)
                {
                    m_UnclosedChainCount += kv.Value.Count;
                    m_BuildNotes.Add($"stations ({kv.Key.Item1},{kv.Key.Item2}): {kv.Value.Count} chain(s), need exactly 2");
                    continue;
                }
                result.TwoChains = true;
                // Base station = the lower of the two terminals (a ski lift climbs); loop travel starts at the base going up chain A.
                // Arbitrary but deterministic.
                int stationA = kv.Key.Item1, stationB = kv.Key.Item2;
                int baseStation = stationPositions[stationA].y <= stationPositions[stationB].y
                    ? stationA : stationB;
                int topStation = baseStation == stationA ? stationB : stationA;

                // Chain A, the up lane, is the cable on the right looking uphill (was kv.Value[0] / kv.Value[1] as found, :286)
                Chain first = kv.Value[0], second = kv.Value[1];
                GetChainEndpoint(lanes, first, atEnd: false, out float3 f0, out _);
                GetChainEndpoint(lanes, first, atEnd: true, out float3 f1, out _);
                GetChainEndpoint(lanes, second, atEnd: false, out float3 s0, out _);
                GetChainEndpoint(lanes, second, atEnd: true, out float3 s1, out _);
                float2 baseXz = stationPositions[baseStation].xz, topXz = stationPositions[topStation].xz;
                float2 firstMid = (f0.xz + f1.xz) * 0.5f, secondMid = (s0.xz + s1.xz) * 0.5f;
                bool swap = StationAlign.UpLaneIsSecond(baseXz, topXz, firstMid, secondMid);
                bool decided = swap || StationAlign.UpLaneIsSecond(baseXz, topXz, secondMid, firstMid);
                if (swap)
                    (first, second) = (second, first);

                result.Base = baseStation;
                result.Top = topStation;
                result.Decided = decided;
                result.Swapped = swap;
                result.A = first;
                result.B = second;
                result.ChainIndexA = chains.IndexOf(first);
                result.ChainIndexB = chains.IndexOf(second);

                if (!TryCloseLoop(lanes, first, second, baseStation, topStation, out string why,
                        out result.TopGap, out result.BaseGap))
                {
                    m_UnclosedChainCount += 2;
                    m_BuildNotes.Add($"stations ({stationA},{stationB}): closure failed — {why}");
                    continue;
                }
                // (:331-344 - the loop's geometry, its notes and m_Loops - are not transcribed.)
                result.Closed = true;
                result.TypeIndex = CommonType(lanes, first, second);
            }
        }

        private void Own(int node, int owner)
        {
            if (m_OwnerOf.TryGetValue(node, out int known))
            {
                if (known != owner)
                    throw new InvalidOperationException("node " + node + " has owners " + known + " and " + owner +
                                                        " (a PathNode carries one owner)");
                return;
            }
            m_OwnerOf.Add(node, owner);
        }

        /// <summary>Which station a chain terminal belongs to: the PathNode owner is the station or one of its children, else
        /// the nearest station within StationTerminalRadius of the terminal's world position, else -1</summary>
        private int ClassifyTerminal(int terminal, float3 position, List<int> stations,
            List<float3> stationPositions, Dictionary<int, int> stationByOwnerIndex, out string how)
        {
            int ownerIndex = OwnerOf(terminal);
            if (stationByOwnerIndex.TryGetValue(ownerIndex, out int byOwner))
            {
                how = ownerIndex == stations[byOwner] ? "owner" : "child";
                return byOwner;
            }
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < stationPositions.Count; i++)
            {
                float d = math.distance(position, stationPositions[i]);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            if (best >= 0 && bestDist <= StationTerminalRadius)
            {
                how = $"pos {bestDist:F1}m";
                return best;
            }
            how = best >= 0 ? $"none (owner {ownerIndex}, nearest station {bestDist:F0}m)" : "none (no stations)";
            return -1;
        }

        private static void AddEndpoint(Dictionary<int, List<int>> nodeMap, int node, int laneIndex)
        {
            if (!nodeMap.TryGetValue(node, out var list))
                nodeMap[node] = list = new List<int>(2);
            list.Add(laneIndex);
        }

        /// <summary>Finds one unvisited lane sharing this endpoint, marks it visited</summary>
        private static bool TryTakeNext(Dictionary<int, List<int>> nodeMap, List<LiftLoopTopology.Lane> lanes,
            bool[] visited, int node, out int laneIndex)
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

        private static bool TryCloseLoop(List<LiftLoopTopology.Lane> lanes, Chain chainA, Chain chainB,
            int baseStation, int topStation, out string why, out float topGap, out float baseGap)
        {
            why = null;

            // Orient both chains: A travels base -> top, B travels top -> base
            OrientChain(chainA, baseStation);
            OrientChain(chainB, topStation);

            // Terminal geometry for the two bullwheel turns
            GetChainEndpoint(lanes, chainA, atEnd: true, out float3 aTop, out float3 aTopTangent);
            GetChainEndpoint(lanes, chainB, atEnd: false, out float3 bTop, out _);
            GetChainEndpoint(lanes, chainB, atEnd: true, out float3 bBase, out float3 bBaseTangent);
            GetChainEndpoint(lanes, chainA, atEnd: false, out float3 aBase, out _);

            // Sanity: the turn diameter is the cable gauge (expect ~3.6 m). Wildly off means the anchors are not where this model
            // thinks: degrade to static pinning.
            topGap = math.distance(aTop, bTop);
            baseGap = math.distance(bBase, aBase);
            if (!ArcSane(aTop, bTop) || !ArcSane(bBase, aBase))
            {
                why = $"terminal gap out of range (top {topGap:F2} m, base {baseGap:F2} m; want 1–10)";
                return false;
            }
            return true;
        }

        /// <summary>The SkiCatalog.Lifts index shared by every lane of both chains; -1 when they span several types (or a lane
        /// carries a negative index)</summary>
        private static int CommonType(List<LiftLoopTopology.Lane> lanes, Chain a, Chain b)
        {
            int type = int.MinValue;
            for (int pass = 0; pass < 2; pass++)
            {
                Chain chain = pass == 0 ? a : b;
                for (int i = 0; i < chain.m_Links.Count; i++)
                {
                    int t = lanes[chain.m_Links[i].m_LaneIndex].TypeIndex;
                    if (t < 0)
                        return -1;
                    if (type == int.MinValue)
                        type = t;
                    else if (t != type)
                        return -1;
                }
            }
            return type == int.MinValue ? -1 : type;
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
            // Reverse link order and per-link direction; swap terminals
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

        /// <summary>World position + unit travel tangent at a chain's first or last point</summary>
        private static void GetChainEndpoint(List<LiftLoopTopology.Lane> lanes, Chain chain, bool atEnd,
            out float3 position, out float3 travelTangent)
        {
            ChainLink link = chain.m_Links[atEnd ? chain.m_Links.Count - 1 : 0];
            LiftLoopTopology.Lane lane = lanes[link.m_LaneIndex];
            // Travel enters a lane at t=0 (or t=1 when reversed) and exits at the other end
            float tExit = link.m_Reversed ? 0f : 1f;
            float tEnter = 1f - tExit;
            float t = atEnd ? tExit : tEnter;
            position = Position(lane, t);
            travelTangent = Tangent(lane, t);
            if (link.m_Reversed)
                travelTangent = -travelTangent;
            travelTangent = math.normalizesafe(travelTangent, new float3(0f, 0f, 1f));
        }

        // Stand-ins for MathUtils.Position / Tangent(lane.m_Bezier, t) at t ∈ {0, 1} (the pre-sampled ends).
        private static float3 Position(LiftLoopTopology.Lane lane, float t)
        {
            if (t == 0f)
                return lane.P0;
            if (t == 1f)
                return lane.P1;
            throw new InvalidOperationException("t " + t + " is not an end");
        }

        private static float3 Tangent(LiftLoopTopology.Lane lane, float t)
        {
            if (t == 0f)
                return lane.T0;
            if (t == 1f)
                return lane.T1;
            throw new InvalidOperationException("t " + t + " is not an end");
        }
    }
}
