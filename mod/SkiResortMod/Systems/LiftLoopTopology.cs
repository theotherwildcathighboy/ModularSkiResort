// LiftLoopTopology — the PURE topology pass of LiftLoopCache: chaining, terminal classification, pairing, base / top,
// up lane, orientation and the terminal gaps, over plain stand-ins (no Game, no Colossal, no Entities type).
//
// CATALOG PHASE C (2026-10-11, docs/research/art/assets-2026-10/code-round-3/round-3-contract.md §2.1-§2.2, loopcache
// group; the extensibility.md F8 fix as corrected by extensibility-check.md F8: "split the topology pass … behind plain
// stand-ins … the geometry pass keeps MathUtils"). PURE FILE: `using System;`, `System.Collections.Generic`,
// `Unity.Mathematics` only, plus the pure sibling StationAlign (its up-lane rule). Linked into tests/LiftBatch
// (LoopCacheTests LC-1..LC-10, LC-REF).
//
// LOOP MODE = LiftLoopCache.cs @ d3ae435b (the Phase B working tree) TRANSCRIBED, not redesigned: Build :175-345 (minus
// the geometry), ClassifyTerminal :347-377, AddEndpoint / TryTakeNext :379-404, TryCloseLoop's orientation and gap
// check :413-430, CommonType :520-540, ArcSane :558-562, OrientChain :564-578, GetChainEndpoint :580-595. The
// substitutions, and only these:
//   * PathNode → an int node id the caller interns (LiftLoopCache: Dictionary<PathNode, int>, ids in first-seen lane
//     order). Equal ids ⇔ PathNode.Equals, which compares the full 64-bit m_SearchKey (Game.Pathfind/PathNode.cs:62-65;
//     GetHashCode :77-80 hashes the same key; =1.6.2), so the node map's candidate lists hold the same lanes in the same
//     order as the PathNode-keyed map did.
//   * cursor.GetOwnerIndex() → the owner index carried with the lane end the cursor stands on (Lane.StartOwner /
//     EndOwner = PathNode.GetOwnerIndex() of that end, PathNode.cs:113-116 (=1.6.2): bits 32-63 of the same search key,
//     so two ends with one id always carry one owner).
//   * MathUtils.Position / Tangent(bezier, 0f or 1f) → the lane's pre-sampled P0 / P1 / T0 / T1 (LiftLoopCache fills them
//     with exactly the calls GetChainEndpoint made, :590-591): the same float bits.
//   * List<Entity> stations + List<float3> stationPositions → List<Station> (OwnerIndex = Entity.Index, Position).
//   * The pair notes ("need exactly 2", "closure failed — …", "loop closed …") and every geometry note stay in
//     LiftLoopCache, written in pair order from Pairs, so the note ORDER is today's: chain notes first, then pair by pair.
//   * CommonType is evaluated for every two-chain pair (today: after a successful closure). Only a successful pair's
//     TypeIndex is ever read, so nothing observable changes.
// tests/LiftBatch LoopCacheReference.cs holds a frozen transcription of those original lines over the same stand-ins;
// LC-REF runs 2,000 seeded random graphs through both and demands identical chains, diagnostics, notes, counts, pairs
// and gaps bit for bit. LC-REF cannot see the Game-side adapter (interning, lane fill, the geometry and its notes): the
// integrator compares those bodies mechanically (round-3-contract-skeptic MUST 2).
//
// SHUTTLE MODE (the tram, R29 / code-economy §A.4) shares the chaining, classification, pairing, up lane and type, and
// differs in four rules:
//   * Base = the station whose Role is Base (0) when exactly one of the pair has it (C1: the cabins live on the Base
//     prefab; a downhill tram keeps its base); else the Loop rule (lower terminal).
//   * Both chains are oriented base → top (no closure: a jig-back tram has two separate track ropes).
//   * Gaps = the distance between the two chains' ends at the base and at the top, accepted in [ShuttleGapMinM,
//     ShuttleGapMaxM] (two track ropes 2 × GaugeHalfM apart: 11 m on the tram, R28).
//   * Docks (C2): at each station with two finite docks, the two chains take the two docks by the smaller summed
//     distance (tie → A to DockA); a run longer than MaxDockRunM leaves that chain undocked (−1, run 0; the would-be run
//     in the *RefusedM field for the caller's note).
//   * T02 (2026-10-16, ShuttleDockReach, off by default): a terminal within MaxDockRunM of a station's dock is that
//     station's (the tram's stub-tip rope is 29-30 m from the origin, past StationTerminalRadius).
// Chain A is the up lane (right looking uphill, StationAlign.UpLaneIsSecond) in both modes.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    internal sealed class LiftLoopTopology
    {
        public enum Mode : byte { Loop = 0, Shuttle = 1 }

        public enum PairStatus : byte { Ok = 0, NotTwoChains = 1, GapOutOfRange = 2 }

        /// <summary>Chain terminals within this distance of a station count as that station's even when the terminal
        /// node is not station-owned (entry 102). Terminals only — the walk-stop rule stays owner-based.
        /// Catalog Phase C (round-3-contract §2.1; was LiftLoopCache.cs:130, private, value unchanged).</summary>
        public const float StationTerminalRadius = 20f;

        /// <summary>T02 (2026-10-16). When true, Shuttle mode also owns a chain terminal within MaxDockRunM of a station's
        /// dock (ClassifyShuttleTerminal). Loop mode, and Shuttle mode with this false, are the transcription bit for bit
        /// (LC-REF). Default false; LiftLoopCache.BuildShuttles sets it true (the only Shuttle-mode caller).</summary>
        public bool ShuttleDockReach;

        /// <summary>Shuttle gap window, meters (inclusive): two track ropes 2 × GaugeHalfM apart (tram 11 m, R28).</summary>
        public const float ShuttleGapMinM = 4f, ShuttleGapMaxM = 24f;

        /// <summary>Longest dock run, meters (= LiftLoopCache.MaxWheelRunMeters :459, the same reasoning: the tram's
        /// stub tip sits 6.5 m out from its dock; anything beyond 30 m is a mis-located dock).</summary>
        public const float MaxDockRunM = 30f;

        /// <summary>One cable lane, as LiftLoopCache harvested it (world space).</summary>
        public struct Lane
        {
            public int StartNode, EndNode;      // interned PathNode ids: equal ids ⇔ PathNode.Equals (PathNode.cs:62-65)
            public int StartOwner, EndOwner;    // PathNode.GetOwnerIndex() of each end (PathNode.cs:113-116)
            public float3 P0, P1, T0, T1;       // MathUtils.Position / MathUtils.Tangent of the lane bezier at t 0 and t 1
            public int TypeIndex;               // CableLane.m_TypeIndex (SkiCatalog.Lifts index; < 0 = unknown)
        }

        public struct Station
        {
            public int OwnerIndex;              // the station entity's Index
            public float3 Position;
            public sbyte Role;                  // 0 Base, 1 Top, −1 unknown (Shuttle mode only)
            public float3 DockA, DockB;         // world track-rope points of its two docks; NaN = none (Shuttle mode only)
        }

        public struct Link
        {
            public int Lane;
            public bool Reversed;               // travel runs t 1 → 0
        }

        public sealed class Chain
        {
            public readonly List<Link> Links = new List<Link>();
            public int StartNode, EndNode, StartStation = -1, EndStation = -1;
            /// <summary>Owner index of the start / end node (additive to the contract's §2.1 shape: the stand-in for
            /// PathNode.GetOwnerIndex() of the chain's terminals; swapped with them by the orientation).</summary>
            public int StartOwner, EndOwner;
        }

        /// <summary>= LiftLoopCache.ChainDiag (entry 102): what the build saw per chain, for the developer-tab dump.</summary>
        public struct ChainDiag
        {
            public int Links, StartOwner, EndOwner, StartStation, EndStation;
            public string StartHow, EndHow;     // "owner" / "child" / "pos 8.3m" / "none (owner 812, nearest station 41m)"
            public float3 StartPos, EndPos;
        }

        public sealed class Pair
        {
            public int StationLo, StationHi;            // the pairing key, in insertion order of the pairing dictionary
            public readonly List<int> ChainIndices = new List<int>();
            public PairStatus Status;
            public int Base = -1, Top = -1;             // station list indices
            public bool BaseByRole;                     // Shuttle: the Base role decided (C1); false = height tie-break
            public Chain A, B;                          // Loop: A base→top, B top→base; Shuttle: both base→top
            public bool UpLaneDecided, UpLaneSwapped;
            public int TypeIndex = -1;                  // common type of both chains' lanes, −1 mixed
            public float TopGapM, BaseGapM;             // terminal gaps after orientation
            public sbyte ABaseDock = -1, BBaseDock = -1, ATopDock = -1, BTopDock = -1;   // 0 = DockA, 1 = DockB, −1 none
            public float ABaseRunM, BBaseRunM, ATopRunM, BTopRunM;                       // dock-run lengths (0 when none)
            /// <summary>Additive: the run a dock was refused for (&gt; MaxDockRunM), 0 when not refused — the caller's
            /// "base dock run 41.3 m over 30 m" note.</summary>
            public float ABaseRefusedM, BBaseRefusedM, ATopRefusedM, BTopRefusedM;
        }

        public readonly List<Chain> Chains = new List<Chain>();
        public readonly List<ChainDiag> Diags = new List<ChainDiag>();
        public readonly List<Pair> Pairs = new List<Pair>();
        /// <summary>"nothing to chain" and the per-chain "unclosed" notes only (the caller writes the pair notes).</summary>
        public readonly List<string> Notes = new List<string>();
        /// <summary>Chains with a missing or identical terminal station (the caller adds the pairs' failures).</summary>
        public int UnclosedChainCount;

        public void Clear()
        {
            Chains.Clear();
            Diags.Clear();
            Pairs.Clear();
            Notes.Clear();
            UnclosedChainCount = 0;
        }

        /// <summary>Chains, classifies and pairs <paramref name="lanes"/>. <paramref name="stationByOwnerIndex"/> maps an
        /// owner index → station list index for each station AND its children (sub-objects, sub-net nodes / edges); like
        /// the original (LiftLoopCache.cs:193-194) every station's own index is (re)written into it, unless there is
        /// nothing to chain. Never throws on bad topology: the reasons land in Notes / Diags / Pairs.</summary>
        public void Build(List<Lane> lanes, List<Station> stations, Dictionary<int, int> stationByOwnerIndex, Mode mode)
        {
            Clear();
            if (lanes.Count == 0 || stations.Count == 0)
            {
                Notes.Add($"nothing to chain: {lanes.Count} lane(s), {stations.Count} station(s)");
                return;
            }

            // Endpoint index: node id -> lane indices sharing it (exact equality; = the PathNode-keyed map).
            var nodeMap = new Dictionary<int, List<int>>(lanes.Count * 2);
            for (int i = 0; i < lanes.Count; i++)
            {
                AddEndpoint(nodeMap, lanes[i].StartNode, i);
                AddEndpoint(nodeMap, lanes[i].EndNode, i);
            }
            for (int i = 0; i < stations.Count; i++)
                stationByOwnerIndex[stations[i].OwnerIndex] = i; // the station itself, always

            // ── Chain building (= LiftLoopCache.cs:196-266) ─────────────────────────────────────
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
                // anchors — shared-station stub nodes must never bridge two lifts.
                var backLinks = new List<Link>();
                int cursor = lanes[i].StartNode, cursorOwner = lanes[i].StartOwner;
                while (!stationByOwnerIndex.ContainsKey(cursorOwner) &&
                       TryTakeNext(nodeMap, visited, cursor, out int prevLane))
                {
                    // Entering prevLane at 'cursor': if cursor == its END the lane already
                    // points at us (travel = forward when later reversed as a whole).
                    bool enteredAtEnd = lanes[prevLane].EndNode == cursor;
                    backLinks.Add(new Link { Lane = prevLane, Reversed = enteredAtEnd });
                    if (enteredAtEnd)
                    {
                        cursor = lanes[prevLane].StartNode;
                        cursorOwner = lanes[prevLane].StartOwner;
                    }
                    else
                    {
                        cursor = lanes[prevLane].EndNode;
                        cursorOwner = lanes[prevLane].EndOwner;
                    }
                }
                chain.StartNode = cursor;
                chain.StartOwner = cursorOwner;
                // backLinks were collected walking AWAY from lane i; in chain order they run
                // toward lane i, so reverse the list and flip each link's direction.
                for (int b = backLinks.Count - 1; b >= 0; b--)
                    chain.Links.Add(new Link { Lane = backLinks[b].Lane, Reversed = !backLinks[b].Reversed });

                chain.Links.Add(new Link { Lane = i, Reversed = false });

                // Forward extension from lane i's end (same station stop).
                cursor = lanes[i].EndNode;
                cursorOwner = lanes[i].EndOwner;
                while (!stationByOwnerIndex.ContainsKey(cursorOwner) &&
                       TryTakeNext(nodeMap, visited, cursor, out int nextLane))
                {
                    bool enteredAtStart = lanes[nextLane].StartNode == cursor;
                    chain.Links.Add(new Link { Lane = nextLane, Reversed = !enteredAtStart });
                    if (enteredAtStart)
                    {
                        cursor = lanes[nextLane].EndNode;
                        cursorOwner = lanes[nextLane].EndOwner;
                    }
                    else
                    {
                        cursor = lanes[nextLane].StartNode;
                        cursorOwner = lanes[nextLane].StartOwner;
                    }
                }
                chain.EndNode = cursor;
                chain.EndOwner = cursorOwner;

                // Terminal classification: owner match first (station or any child of it),
                // position fallback second (entry 102). Terminals only.
                Endpoint(lanes, chain, atEnd: false, out float3 startPos, out _);
                Endpoint(lanes, chain, atEnd: true, out float3 endPos, out _);
                chain.StartStation = ClassifyTerminal(chain.StartOwner, startPos, stations, stationByOwnerIndex,
                    mode == Mode.Shuttle && ShuttleDockReach, out string startHow);
                chain.EndStation = ClassifyTerminal(chain.EndOwner, endPos, stations, stationByOwnerIndex,
                    mode == Mode.Shuttle && ShuttleDockReach, out string endHow);
                Chains.Add(chain);
                Diags.Add(new ChainDiag
                {
                    Links = chain.Links.Count,
                    StartOwner = chain.StartOwner,
                    EndOwner = chain.EndOwner,
                    StartStation = chain.StartStation,
                    EndStation = chain.EndStation,
                    StartHow = startHow,
                    EndHow = endHow,
                    StartPos = startPos,
                    EndPos = endPos,
                });
            }

            // ── Pair chains by station pair (= LiftLoopCache.cs:269-288) ──────────────────────────
            var byPair = new Dictionary<(int, int), List<int>>();
            for (int c = 0; c < Chains.Count; c++)
            {
                Chain chain = Chains[c];
                if (chain.StartStation < 0 || chain.EndStation < 0 ||
                    chain.StartStation == chain.EndStation)
                {
                    UnclosedChainCount++;
                    Notes.Add($"chain {c}: unclosed — terminals station {chain.StartStation} → " +
                              $"{chain.EndStation} ({Diags[c].StartHow} / {Diags[c].EndHow})");
                    continue;
                }
                var key = chain.StartStation < chain.EndStation
                    ? (chain.StartStation, chain.EndStation)
                    : (chain.EndStation, chain.StartStation);
                if (!byPair.TryGetValue(key, out var list))
                    byPair[key] = list = new List<int>();
                list.Add(c);
            }

            // ── Per pair, in the dictionary's insertion order (= LiftLoopCache.cs:290-322, :413-430) ──
            foreach (var kv in byPair)
            {
                var pair = new Pair { StationLo = kv.Key.Item1, StationHi = kv.Key.Item2 };
                pair.ChainIndices.AddRange(kv.Value);
                Pairs.Add(pair);
                if (kv.Value.Count != 2)
                {
                    pair.Status = PairStatus.NotTwoChains;
                    continue;
                }

                // Base station = the lower of the two terminals (a ski lift climbs); loop
                // travel starts at the base going up chain A. Arbitrary but deterministic.
                // Shuttle (C1): the Base-ROLE station when exactly one of the two has it.
                int stationA = kv.Key.Item1, stationB = kv.Key.Item2;
                int baseStation;
                bool aIsBase = stations[stationA].Role == 0, bIsBase = stations[stationB].Role == 0;
                if (mode == Mode.Shuttle && aIsBase != bIsBase)
                {
                    baseStation = aIsBase ? stationA : stationB;
                    pair.BaseByRole = true;
                }
                else
                {
                    baseStation = stations[stationA].Position.y <= stations[stationB].Position.y
                        ? stationA : stationB;
                }
                int topStation = baseStation == stationA ? stationB : stationA;
                pair.Base = baseStation;
                pair.Top = topStation;

                // Catalog Phase B (orchestrator addendum 1): chain A, the up lane, is the cable on the RIGHT looking
                // uphill — decided on the chains as found, before orientation (= LiftLoopCache.cs:310-322).
                Chain first = Chains[kv.Value[0]], second = Chains[kv.Value[1]];
                Endpoint(lanes, first, atEnd: false, out float3 f0, out _);
                Endpoint(lanes, first, atEnd: true, out float3 f1, out _);
                Endpoint(lanes, second, atEnd: false, out float3 s0, out _);
                Endpoint(lanes, second, atEnd: true, out float3 s1, out _);
                float2 baseXz = stations[baseStation].Position.xz, topXz = stations[topStation].Position.xz;
                float2 firstMid = (f0.xz + f1.xz) * 0.5f, secondMid = (s0.xz + s1.xz) * 0.5f;
                bool swap = StationAlign.UpLaneIsSecond(baseXz, topXz, firstMid, secondMid);
                bool decided = swap || StationAlign.UpLaneIsSecond(baseXz, topXz, secondMid, firstMid);
                if (swap)
                    (first, second) = (second, first);
                pair.UpLaneDecided = decided;
                pair.UpLaneSwapped = swap;

                // Orient both chains: A travels base -> top; B travels top -> base (Loop) or base -> top (Shuttle).
                OrientChain(first, baseStation);
                OrientChain(second, mode == Mode.Loop ? topStation : baseStation);
                pair.A = first;
                pair.B = second;
                pair.TypeIndex = CommonType(lanes, first, second);

                bool sane;
                if (mode == Mode.Loop)
                {
                    // Sanity (= LiftLoopCache.cs:417-430): the turn diameter is the cable gauge (expect ~3.6 m).
                    Endpoint(lanes, first, atEnd: true, out float3 aTop, out _);
                    Endpoint(lanes, second, atEnd: false, out float3 bTop, out _);
                    Endpoint(lanes, second, atEnd: true, out float3 bBase, out _);
                    Endpoint(lanes, first, atEnd: false, out float3 aBase, out _);
                    pair.TopGapM = math.distance(aTop, bTop);
                    pair.BaseGapM = math.distance(bBase, aBase);
                    sane = ArcSane(aTop, bTop) && ArcSane(bBase, aBase);
                }
                else
                {
                    Endpoint(lanes, first, atEnd: false, out float3 aBase, out _);
                    Endpoint(lanes, second, atEnd: false, out float3 bBase, out _);
                    Endpoint(lanes, first, atEnd: true, out float3 aTop, out _);
                    Endpoint(lanes, second, atEnd: true, out float3 bTop, out _);
                    pair.BaseGapM = math.distance(aBase, bBase);
                    pair.TopGapM = math.distance(aTop, bTop);
                    sane = ShuttleGapSane(pair.BaseGapM) && ShuttleGapSane(pair.TopGapM);
                }
                if (!sane)
                {
                    pair.Status = PairStatus.GapOutOfRange;
                    continue;
                }
                pair.Status = PairStatus.Ok;

                if (mode == Mode.Shuttle)
                {
                    Endpoint(lanes, first, atEnd: false, out float3 aBase, out _);
                    Endpoint(lanes, second, atEnd: false, out float3 bBase, out _);
                    Endpoint(lanes, first, atEnd: true, out float3 aTop, out _);
                    Endpoint(lanes, second, atEnd: true, out float3 bTop, out _);
                    AssignDocks(stations[baseStation], aBase, bBase, out pair.ABaseDock, out pair.BBaseDock,
                        out pair.ABaseRunM, out pair.BBaseRunM, out pair.ABaseRefusedM, out pair.BBaseRefusedM);
                    AssignDocks(stations[topStation], aTop, bTop, out pair.ATopDock, out pair.BTopDock,
                        out pair.ATopRunM, out pair.BTopRunM, out pair.ATopRefusedM, out pair.BTopRefusedM);
                }
            }
        }

        /// <summary>World position + unit travel tangent at a chain's first or last point (= LiftLoopCache.cs:580-595,
        /// GetChainEndpoint, on the pre-sampled lane ends).</summary>
        public static void Endpoint(List<Lane> lanes, Chain chain, bool atEnd, out float3 position, out float3 travelTangent)
        {
            Link link = chain.Links[atEnd ? chain.Links.Count - 1 : 0];
            Lane lane = lanes[link.Lane];
            // Travel enters a lane at t=0 (or t=1 when reversed) and exits at the other end:
            // t = atEnd ? tExit : tEnter with tExit = reversed ? 0 : 1, i.e. t == 1 exactly when atEnd != reversed.
            bool atOne = atEnd != link.Reversed;
            position = atOne ? lane.P1 : lane.P0;
            travelTangent = atOne ? lane.T1 : lane.T0;
            if (link.Reversed)
                travelTangent = -travelTangent;
            travelTangent = math.normalizesafe(travelTangent, new float3(0f, 0f, 1f));
        }

        /// <summary>A bullwheel turn's diameter is plausible: 1 &lt; d &lt; 10 m (= LiftLoopCache.cs:558-562).</summary>
        public static bool ArcSane(float3 from, float3 to)
        {
            float d = math.distance(from, to);
            return d > 1f && d < 10f;
        }

        /// <summary>Shuttle: two track ropes [ShuttleGapMinM, ShuttleGapMaxM] apart (inclusive; NaN → false).</summary>
        public static bool ShuttleGapSane(float gapM)
        {
            return gapM >= ShuttleGapMinM && gapM <= ShuttleGapMaxM;
        }

        /// <summary>Which station a chain terminal belongs to: the owner is the station or one of its children, else
        /// the nearest station within StationTerminalRadius of the terminal's world position, else -1
        /// (= LiftLoopCache.cs:347-377). Shuttle mode with ShuttleDockReach adds the dock reach (T02): a station with finite docks
        /// also owns a terminal within MaxDockRunM of one of them. Loop mode is the transcription, untouched.</summary>
        private static int ClassifyTerminal(int ownerIndex, float3 position, List<Station> stations,
            Dictionary<int, int> stationByOwnerIndex, bool dockReach, out string how)
        {
            if (stationByOwnerIndex.TryGetValue(ownerIndex, out int byOwner))
            {
                how = ownerIndex == stations[byOwner].OwnerIndex ? "owner" : "child";
                return byOwner;
            }
            if (dockReach)
                return ClassifyShuttleTerminal(ownerIndex, position, stations, out how);
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < stations.Count; i++)
            {
                float d = math.distance(position, stations[i].Position);
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

        /// <summary>T02 (2026-10-16, docs/research/field/2026-10-15/build/T02-tram-shuttle.md). Shuttle position fallback.
        /// The tram's line terminal is the HV stub tip's track rope: lateral ± GaugeHalfM off the line axis, at rope height
        /// (AxleM 26.25 m on the tower stubs), 12 m out - 28.9 / 30.4 m from the station ORIGIN, past the 20 m
        /// StationTerminalRadius that fits a chair stub (field 2026-10-15: "nearest station 30m"/"29m"), though it is a few
        /// meters from the station's own docks. A terminal belongs to a station when it is within StationTerminalRadius of
        /// the origin (as before) OR within MaxDockRunM of either finite dock: the same distance the dock step already
        /// tolerates (a longer run is refused there), and the docks come from the catalog row, so the reach follows the
        /// prefab (T14's x mirror moves stub and docks together). The nearest station by that reach wins; ties keep the
        /// lower index. A station without finite docks keeps the origin radius alone.</summary>
        private static int ClassifyShuttleTerminal(int ownerIndex, float3 position, List<Station> stations, out string how)
        {
            int best = -1;
            bool bestViaDock = false;
            float bestScore = float.MaxValue, bestDist = float.MaxValue, bestOriginDist = float.MaxValue;
            for (int i = 0; i < stations.Count; i++)
            {
                float dOrigin = math.distance(position, stations[i].Position);
                float dDock = DockReachM(stations[i], position);
                // Score 1 = exactly at the edge of the reach; the smaller of the two normalized distances.
                float scoreOrigin = dOrigin / StationTerminalRadius;
                float scoreDock = float.IsNaN(dDock) ? float.MaxValue : dDock / MaxDockRunM;
                bool viaDock = scoreDock < scoreOrigin;
                float score = viaDock ? scoreDock : scoreOrigin;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                    bestViaDock = viaDock;
                    bestDist = viaDock ? dDock : dOrigin;
                    bestOriginDist = dOrigin;
                }
            }
            if (best >= 0 && bestScore <= 1f)
            {
                how = bestViaDock ? $"dock {bestDist:F1}m" : $"pos {bestDist:F1}m";
                return best;
            }
            if (best < 0)
            {
                how = "none (no stations)";
                return -1;
            }
            float nearDock = DockReachM(stations[best], position);
            how = $"none (owner {ownerIndex}, nearest station {bestOriginDist:F0}m" +
                  (float.IsNaN(nearDock) ? ")" : $", nearest dock {nearDock:F0}m)");
            return -1;
        }

        /// <summary>Distance from a point to the nearer finite dock of a station; NaN when it has none.</summary>
        private static float DockReachM(Station station, float3 position)
        {
            float d = float.NaN;
            if (math.all(math.isfinite(station.DockA)))
                d = math.distance(position, station.DockA);
            if (math.all(math.isfinite(station.DockB)))
            {
                float db = math.distance(position, station.DockB);
                d = float.IsNaN(d) || db < d ? db : d;
            }
            return d;
        }

        private static void AddEndpoint(Dictionary<int, List<int>> nodeMap, int node, int laneIndex)
        {
            if (!nodeMap.TryGetValue(node, out var list))
                nodeMap[node] = list = new List<int>(2);
            list.Add(laneIndex);
        }

        /// <summary>Finds one unvisited lane sharing this endpoint, marks it visited (= LiftLoopCache.cs:386-404).</summary>
        private static bool TryTakeNext(Dictionary<int, List<int>> nodeMap, bool[] visited, int node, out int laneIndex)
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

        /// <summary>= LiftLoopCache.cs:564-578: reverse link order and per-link direction, swap the terminals.</summary>
        private static void OrientChain(Chain chain, int fromStation)
        {
            if (chain.StartStation == fromStation)
                return;
            chain.Links.Reverse();
            for (int i = 0; i < chain.Links.Count; i++)
            {
                var link = chain.Links[i];
                link.Reversed = !link.Reversed;
                chain.Links[i] = link;
            }
            (chain.StartNode, chain.EndNode) = (chain.EndNode, chain.StartNode);
            (chain.StartOwner, chain.EndOwner) = (chain.EndOwner, chain.StartOwner);
            (chain.StartStation, chain.EndStation) = (chain.EndStation, chain.StartStation);
        }

        /// <summary>Catalog Phase B (= LiftLoopCache.cs:520-540): the SkiCatalog.Lifts index shared by every lane of both
        /// chains; −1 when they span several types (or a lane carries a negative index).</summary>
        private static int CommonType(List<Lane> lanes, Chain a, Chain b)
        {
            int type = int.MinValue;
            for (int pass = 0; pass < 2; pass++)
            {
                Chain chain = pass == 0 ? a : b;
                for (int i = 0; i < chain.Links.Count; i++)
                {
                    int t = lanes[chain.Links[i].Lane].TypeIndex;
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

        /// <summary>Shuttle (C2, §2.2 step 8): chains A and B end at p and q at this station; they take the station's
        /// two docks by the smaller summed distance (tie → A to DockA). A run over MaxDockRunM refuses that dock (−1,
        /// run 0, refused = the run). A station without two finite docks docks nothing.</summary>
        private static void AssignDocks(Station station, float3 p, float3 q, out sbyte aDock, out sbyte bDock,
            out float aRun, out float bRun, out float aRefused, out float bRefused)
        {
            aDock = bDock = -1;
            aRun = bRun = aRefused = bRefused = 0f;
            if (!math.all(math.isfinite(station.DockA)) || !math.all(math.isfinite(station.DockB)))
                return;
            float cost0 = math.distance(p, station.DockA) + math.distance(q, station.DockB);
            float cost1 = math.distance(p, station.DockB) + math.distance(q, station.DockA);
            bool aTakesA = cost0 <= cost1;
            Dock(p, aTakesA ? station.DockA : station.DockB, aTakesA ? (sbyte)0 : (sbyte)1, out aDock, out aRun, out aRefused);
            Dock(q, aTakesA ? station.DockB : station.DockA, aTakesA ? (sbyte)1 : (sbyte)0, out bDock, out bRun, out bRefused);
        }

        private static void Dock(float3 end, float3 dock, sbyte index, out sbyte chosen, out float run, out float refused)
        {
            float d = math.distance(end, dock);
            if (d <= MaxDockRunM)
            {
                chosen = index;
                run = d;
                refused = 0f;
            }
            else
            {
                chosen = -1;
                run = 0f;
                refused = d;
            }
        }
    }
}
