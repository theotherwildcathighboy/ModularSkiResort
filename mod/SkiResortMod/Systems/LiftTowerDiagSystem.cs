// Read-only diagnostic: what the engine actually did at every node of a lift line. It changes nothing: no command buffer, no
// component write, no prefab touch, no per-frame work (an int compare while nothing is pending).
// LiftCensusSystem.BuildCensus calls OnCensusBuilt(edges) after every build. A queued line prints once the census has been quiet for
// SettleFrames updates (a drag prints one block). It logs one "Towers [summary]" line per lift line (towers spawned, nodes without one,
// the highest node Elevation, the largest leg gap, the lowest chord clearance) and "Towers [plan]" (a re-plan by the pure LiftTowerPlanner
// on the real terrain, cached for LiftTowerPlanOverlaySystem). Both print only with the diagnostics option on (or in a hub DUMP). With the
// option off nothing is planned on an edit: the overlay asks for its plan on demand (RequestPlan). Opt pass 2026-11-14 lifts-01, lifts-08 b
// (the per-node dump and its engine placeholder-scoring replica were cut to the summary line).
// Mod.cs: UpdateAfter<LiftTowerDiagSystem, LiftCensusSystem>(Modification4B), the same anchor family as TramShuttleSystem.
// Engine notes: EngineNotes/LiftTowerDiagSystem.md

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftTowerDiagSystem : GameSystemBase
    {
        /// <summary>Census-quiet updates before a queued line is printed (towers exist by then. A drag keeps resetting it).</summary>
        internal const int SettleFrames = 30;
        internal const int MaxBlocksPerEmit = 12;
        internal const int MaxBlocksPerDump = 24;
        private static readonly float[] SampleT = { 0.2f, 0.4f, 0.6f, 0.8f };   // chord clearance samples per span

        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private PrefabSystem m_PrefabSystem;
        private TerrainSystem m_TerrainSystem;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Frame, m_LastQueueFrame;
        private readonly HashSet<uint> m_Logged = new HashSet<uint>();
        private readonly List<List<LiftCensus.Edge>> m_Pending = new List<List<LiftCensus.Edge>>();
        private readonly List<LiftCensus.Edge> m_LastEdges = new List<LiftCensus.Edge>();
        private int m_Blocks, m_Dumps;

        // The planner and the overlay cache
        internal const int MaxCachedEntities = 8192;
        private readonly LiftTowerPlanner m_Planner = new LiftTowerPlanner();
        private readonly Dictionary<Entity, LiftPlanDraw> m_Draws = new Dictionary<Entity, LiftPlanDraw>();
        private readonly HashSet<Entity> m_NotALine = new HashSet<Entity>();
        private Entity m_PlanRequest;
        private int m_Plans;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            Mod.Log.Info($"{nameof(LiftTowerDiagSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Frame = 0;
            m_LastQueueFrame = 0;
            m_Logged.Clear();
            m_Pending.Clear();
            m_LastEdges.Clear();
            m_Blocks = m_Dumps = 0;
            m_Draws.Clear();
            m_NotALine.Clear();
            m_PlanRequest = Entity.Null;
            m_Plans = 0;
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            m_Frame++;
            if (m_PlanRequest != Entity.Null)
            {
                Entity want = m_PlanRequest;
                m_PlanRequest = Entity.Null;
                try
                {
                    PlanSelected(want);
                }
                catch (Exception e)
                {
                    Fail(e);
                    return;
                }
            }
            if (m_Pending.Count == 0 || m_Frame - m_LastQueueFrame < SettleFrames)
                return;
            try
            {
                // The blocks are a developer dump. Without the diagnostics option nothing is planned here: the overlay, the only other reader,
                // asks for its plan on demand (RequestPlan, PlanSelected), so a line edit costs no planner run (opt pass lifts-01).
                if (!SkiResortMod.Settings.SkiResortSettings.DiagnosticsOn)
                {
                    m_Pending.Clear();
                    return;
                }

                int printed = 0;
                int skipped = 0;
                for (int i = 0; i < m_Pending.Count; i++)
                {
                    if (printed >= MaxBlocksPerEmit)
                    {
                        skipped++;
                        continue;
                    }
                    if (WriteLine(m_Pending[i], "new or changed", log: true))
                        printed++;
                }
                if (skipped > 0)
                    Mod.Log.Info($"Towers [summary]: {skipped} more new or changed line(s) not printed (cap {MaxBlocksPerEmit} per emission); the DUMP prints every line");
                m_Pending.Clear();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void Fail(Exception e)
        {
            m_Disabled = true;
            m_Pending.Clear();
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(LiftTowerDiagSystem)} EXCEPTION — tower diagnostics disabled until the next game load " +
                              $"(diagnostic only, nothing else is affected). Please report this stack:\n{e}");
            }
        }

        /// <summary>Called by LiftCensusSystem after each build with its harvested line edges (main thread, cheap: the lists are
        /// small, no ECS read). Queues every connected component whose signature was not printed yet.</summary>
        internal void OnCensusBuilt(List<LiftCensus.Edge> edges)
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                m_NotALine.Clear();
                m_LastEdges.Clear();
                m_LastEdges.AddRange(edges);
                List<List<LiftCensus.Edge>> comps = Components(m_LastEdges);
                for (int c = 0; c < comps.Count; c++)
                {
                    uint sig = Signature(comps[c]);
                    if (m_Logged.Add(sig))
                    {
                        m_Pending.Add(comps[c]);
                        m_LastQueueFrame = m_Frame;
                        // A new or changed line drops its cached plan: with diagnostics off nothing re-plans it here, so the overlay
                        // (Settings > Lifts > LiftBuildAids) re-asks on demand instead of drawing the old plan.
                        List<LiftCensus.Edge> comp = comps[c];
                        for (int i = 0; i < comp.Count; i++)
                        {
                            m_Draws.Remove(LiftCensusSystem.Ent(comp[i].Id));
                            m_Draws.Remove(LiftCensusSystem.Ent(comp[i].Start));
                            m_Draws.Remove(LiftCensusSystem.Ent(comp[i].End));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>On demand: every line of the last census build (dev-tab DUMP). Never throws.</summary>
        internal void DumpAll(string reason)
        {
            if (!m_Armed)
            {
                Mod.Log.Info($"Towers [summary]: DUMP ({reason}) — not armed (no game loaded)");
                return;
            }
            try
            {
                m_Dumps++;
                List<List<LiftCensus.Edge>> comps = Components(m_LastEdges);
                int printed = 0;
                for (int c = 0; c < comps.Count; c++)
                {
                    if (printed >= MaxBlocksPerDump)
                        break;
                    if (WriteLine(comps[c], "dump: " + reason, log: true))
                        printed++;
                }
                Mod.Log.Info($"Towers [summary]: DUMP ({reason}) — {printed} of {comps.Count} line(s) printed " +
                             $"(cap {MaxBlocksPerDump}); {m_Blocks} block(s) this load, {m_Dumps} dump(s), {m_Logged.Count} signature(s) seen, " +
                             $"{m_Pending.Count} queued{(m_Disabled ? "; DISABLED after an exception" : string.Empty)}");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Towers [summary]: DUMP ({reason}) failed ({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        internal static List<List<LiftCensus.Edge>> Components(List<LiftCensus.Edge> edges)
        {
            var result = new List<List<LiftCensus.Edge>>();
            int n = edges.Count;
            if (n == 0)
                return result;
            var parent = new int[n];
            for (int i = 0; i < n; i++)
                parent[i] = i;
            var firstAt = new Dictionary<EntityRef, int>();
            for (int i = 0; i < n; i++)
            {
                Join(firstAt, parent, edges[i].Start, i);
                Join(firstAt, parent, edges[i].End, i);
            }
            var byRoot = new Dictionary<int, List<LiftCensus.Edge>>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(parent, i);
                if (!byRoot.TryGetValue(r, out List<LiftCensus.Edge> list))
                {
                    list = new List<LiftCensus.Edge>();
                    byRoot[r] = list;
                    result.Add(list);
                }
                list.Add(edges[i]);
            }
            return result;
        }

        private static void Join(Dictionary<EntityRef, int> firstAt, int[] parent, EntityRef node, int edge)
        {
            if (node.IsNull)
                return;
            if (firstAt.TryGetValue(node, out int other))
                parent[Find(parent, edge)] = Find(parent, other);
            else
                firstAt[node] = edge;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        /// <summary>Order-independent hash of the component's edge ids and node positions (0.1 m): a redraw, a moved node or a moved
        /// station gives a new signature. An unrelated build does not.</summary>
        internal static uint Signature(List<LiftCensus.Edge> comp)
        {
            uint h = 0;
            for (int i = 0; i < comp.Count; i++)
            {
                LiftCensus.Edge e = comp[i];
                uint x = 2166136261u;
                x = Mix(x, (uint)e.Id.Index); x = Mix(x, (uint)e.Id.Version);
                x = Mix(x, (uint)e.Start.Index); x = Mix(x, (uint)e.End.Index);
                x = Mix(x, (uint)(int)math.round(e.StartPos.x * 10f)); x = Mix(x, (uint)(int)math.round(e.StartPos.y * 10f)); x = Mix(x, (uint)(int)math.round(e.StartPos.z * 10f));
                x = Mix(x, (uint)(int)math.round(e.EndPos.x * 10f)); x = Mix(x, (uint)(int)math.round(e.EndPos.y * 10f)); x = Mix(x, (uint)(int)math.round(e.EndPos.z * 10f));
                h += x * 2654435761u;
            }
            return h ^ (uint)comp.Count;
        }

        private static uint Mix(uint h, uint v)
        {
            unchecked { return (h ^ v) * 16777619u; }
        }

        internal struct Hop
        {
            public Entity Node;
            public Entity NextEdge;     // the edge to the following node in this path (Null at the path's end)
            public Entity NextNode;
        }

        /// <summary>Prints the [summary] line and the [plan] block of one line when <paramref name="log"/> is true. Always refreshes the plan
        /// cache.</summary>
        private bool WriteLine(List<LiftCensus.Edge> comp, string why, bool log)
        {
            // Stale (a deleted / replaced edge): a newer build queued the replacement.
            for (int i = 0; i < comp.Count; i++)
            {
                Entity edge = LiftCensusSystem.Ent(comp[i].Id);
                if (!EntityManager.Exists(edge) || EntityManager.HasComponent<Deleted>(edge))
                    return false;
            }
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            if (!hd.isCreated)
            {
                if (log)
                    Mod.Log.Info("Towers [summary]: terrain heightmap not ready — line skipped (DUMP again later)");
                return false;
            }

            LiftTypeDef typeDef = SkiCatalog.LiftByIndex(comp[0].Type);
            string typeName = typeDef != null ? typeDef.Id : "untyped";
            float totalLen = 0f;
            for (int i = 0; i < comp.Count; i++)
                totalLen += comp[i].LengthM;

            List<List<Hop>> paths = Paths(comp);
            int nodeTotal = 0;
            for (int p = 0; p < paths.Count; p++)
                nodeTotal += paths[p].Count;

            if (!log)
            {
                try
                {
                    WritePlans(comp, paths, typeDef, typeName, why, ref hd, log: false);
                }
                catch (Exception e)
                {
                    Mod.Log.Warn($"Towers [plan]: line {typeName} edge {Fmt(LiftCensusSystem.Ent(comp[0].Id))} — planner failed " +
                                 $"({e.GetType().Name}: {e.Message})\n{e}");
                }
                return true;
            }

            var towerCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int noTower = 0;
            float maxFloat = float.NegativeInfinity, minClr = float.PositiveInfinity, maxElev = 0f;
            IReadOnlyDictionary<Entity, float> carriers = m_PrefabCatalog.CarrierGripTops;
            for (int p = 0; p < paths.Count; p++)
                for (int i = 0; i < paths[p].Count; i++)
                    SummarizeNode(ref hd, paths[p][i], carriers, towerCounts, ref noTower, ref maxFloat, ref minClr, ref maxElev);

            var sb = new StringBuilder(512);
            sb.Append("Towers [summary]: line ").Append(typeName).Append(" edge ").Append(Fmt(LiftCensusSystem.Ent(comp[0].Id)))
              .Append(" (").Append(why).Append("): ").Append(nodeTotal).Append(" node(s), ").Append(comp.Count).Append(" span(s), ")
              .Append(F(totalLen, "F0")).Append(" m of curve");
            if (paths.Count > 1)
                sb.Append(", ").Append(paths.Count).Append(" paths (branches)");
            sb.Append("; towers {");
            bool first = true;
            foreach (KeyValuePair<string, int> kv in towerCounts)
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append(kv.Key).Append(" x").Append(kv.Value);
            }
            sb.Append(first ? "none" : string.Empty).Append('}');
            if (noTower > 0)
                sb.Append(", ").Append(noTower).Append(" node(s) without a tower");
            sb.Append("; max node Elevation ").Append(F(maxElev, "F1")).Append(" m")
              .Append("; max pivot-above-terrain (leg gap) ").Append(maxFloat > float.NegativeInfinity ? F(maxFloat, "F1") + " m" : "n/a")
              .Append("; min chord clearance ").Append(minClr < float.PositiveInfinity ? F(minClr, "F1") + " m" : "n/a");
            Mod.Log.Info(sb.ToString());
            m_Blocks++;

            // The planned line beside the actual one (its own try: a planner fault must not cost the [summary] line)
            try
            {
                WritePlans(comp, paths, typeDef, typeName, why, ref hd, log: true);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Towers [plan]: line {typeName} edge {Fmt(LiftCensusSystem.Ent(comp[0].Id))} — planner failed " +
                             $"({e.GetType().Name}: {e.Message}); the [summary] line above is unaffected\n{e}");
            }
            return true;
        }

        /// <summary>The overlay's lookup: the cached plan of the line that <paramref name="e"/> (edge, node) belongs to.</summary>
        internal bool TryGetPlanDraw(Entity e, out LiftPlanDraw draw) => m_Draws.TryGetValue(e, out draw);

        /// <summary>The overlay asks for a plan of a selected entity that has none cached. The request is served on the next update (at
        /// most one per update). An entity that is not on a line is remembered until the next census build.</summary>
        internal void RequestPlan(Entity e)
        {
            if (m_Armed && !m_Disabled && e != Entity.Null && !m_NotALine.Contains(e) && !m_Draws.ContainsKey(e))
                m_PlanRequest = e;
        }

        private void PlanSelected(Entity want)
        {
            List<List<LiftCensus.Edge>> comps = Components(m_LastEdges);
            for (int c = 0; c < comps.Count; c++)
            {
                List<LiftCensus.Edge> comp = comps[c];
                for (int i = 0; i < comp.Count; i++)
                {
                    if (LiftCensusSystem.Ent(comp[i].Id) == want || LiftCensusSystem.Ent(comp[i].Start) == want ||
                        LiftCensusSystem.Ent(comp[i].End) == want)
                    {
                        if (!WriteLine(comp, "selected (overlay)", SkiResortMod.Settings.SkiResortSettings.DiagnosticsOn))
                            m_NotALine.Add(want);
                        return;
                    }
                }
            }
            m_NotALine.Add(want);
        }

        private void WritePlans(List<LiftCensus.Edge> comp, List<List<Hop>> paths, LiftTypeDef typeDef, string typeName, string why,
                                ref TerrainHeightData hd, bool log)
        {
            string head = "line " + typeName + " edge " + Fmt(LiftCensusSystem.Ent(comp[0].Id));
            LiftTowerPlanConstants constants = LiftTowerPlanConstants.For(typeDef);
            if (constants == null)
            {
                if (log)
                    Mod.Log.Info($"Towers [plan]: {head} — no planner constants for lift type '{typeName}' (untyped line): not planned");
                return;
            }
            if (m_Draws.Count > MaxCachedEntities)
                m_Draws.Clear();   // bound: entities of lines long deleted. The overlay re-requests what it needs
            TerrainHeightData terrain = hd;
            for (int p = 0; p < paths.Count; p++)
            {
                List<Hop> path = paths[p];
                if (path.Count < 2)
                    continue;
                var pts = new float3[path.Count];
                for (int i = 0; i < path.Count; i++)
                    pts[i] = EntityManager.TryGetComponent<Game.Net.Node>(path[i].Node, out var n) ? n.m_Position : default;
                var cum = new float[path.Count];
                for (int i = 1; i < pts.Length; i++)
                    cum[i] = cum[i - 1] + math.distance(pts[i - 1].xz, pts[i].xz);
                float length = cum[cum.Length - 1];
                string pathHead = paths.Count > 1 ? head + " path " + p : head;
                if (length < 2f * constants.StepM)
                {
                    if (log)
                        Mod.Log.Info($"Towers [plan]: {pathHead} — {length:F1} m of line, too short to plan");
                    continue;
                }

                LiftTerrainSampler sampler = (along, lateral) =>
                {
                    float3 at = PolylineAt(pts, cum, along, lateral, out _);
                    return TerrainUtils.SampleHeight(ref terrain, at);
                };
                float ropeA = pts[0].y + RopeAboveNode(EntityManager, path[0].Node, constants);
                float ropeB = pts[pts.Length - 1].y + RopeAboveNode(EntityManager, path[path.Count - 1].Node, constants);
                LiftTowerPlan plan = m_Planner.Plan(sampler, length, ropeA, ropeB, constants);
                m_Plans++;

                if (log)
                {
                    var sb = new StringBuilder(2048);
                    plan.AppendLog(sb, pathHead + " (" + why + "; ends = the path's first / last node, rope at node y + " +
                                       F(ropeA - pts[0].y, "F2") + " / " + F(ropeB - pts[pts.Length - 1].y, "F2") + ") vs actual " + path.Count + " node(s), " +
                                       math.max(0, path.Count - 2) + " line tower(s)");
                    float maxAbove = float.NegativeInfinity;
                    int violated = 0;
                    for (int s = 0; s < plan.Spans.Length; s++)
                    {
                        maxAbove = math.max(maxAbove, plan.Spans[s].MaxRopeAboveM);
                        if (plan.Spans[s].FloorViolated || plan.Spans[s].HeightViolated)
                            violated++;
                    }
                    sb.Append("Towers [plan summary]: ").Append(pathHead).Append(": planned ").Append(plan.Nodes.Length).Append(" node(s) / ")
                      .Append(plan.LineTowers).Append(" line tower(s) vs actual ").Append(path.Count).Append(" / ").Append(math.max(0, path.Count - 2))
                      .Append("; members {").Append(plan.MemberCounts()).Append("}; min floor margin ").Append(Sg(plan.MinFloorMarginM))
                      .Append(" m; max rope above ground ").Append(F(maxAbove, "F1")).Append(" m; ")
                      .Append(violated == 0 ? "every span clear" : violated + " span(s) VIOLATED (no clear arrangement on this ground)")
                      .Append(plan.OverCapNote);   // FIX-LIST 55: the angles too
                    Mod.Log.Info(sb.ToString());
                }

                LiftPlanDraw draw = LiftPlanDraw.Build(plan, s => PolylineAt(pts, cum, s, 0f, out _), s =>
                {
                    PolylineAt(pts, cum, s, 0f, out float3 right);
                    return right;
                });
                for (int i = 0; i < path.Count; i++)
                {
                    m_Draws[path[i].Node] = draw;
                    if (path[i].NextEdge != Entity.Null)
                        m_Draws[path[i].NextEdge] = draw;
                }
            }
        }

        /// <summary>The rope height above <paramref name="node"/> when no rope lane anchor is found: the head the node carries
        /// (Game.Net.Elevation, read back with LiftTowerEdit.MemberOf as the fit does), so a twin head gives its member's lane height
        /// relative to the raised node. One helper for the lift tool's EndRope, the fit's end ropes and the diag plan.</summary>
        internal static float RopeAboveNode(EntityManager em, Entity node, LiftTowerPlanConstants c)
        {
            float e = em.Exists(node) && em.TryGetComponent<Game.Net.Elevation>(node, out var el) ? math.cmin(el.m_Elevation) : 0f;
            return LiftTowerEdit.RopeAboveNodeM(c, e);
        }

        /// <summary>The point <paramref name="along"/> meters (xz) along the node polyline, <paramref name="lateral"/> meters to the right
        /// of its local direction (y = 0). <paramref name="right"/> = that unit right vector.</summary>
        internal static float3 PolylineAt(float3[] pts, float[] cum, float along, float lateral, out float3 right)
        {
            int seg = 0;
            while (seg < pts.Length - 2 && cum[seg + 1] < along)
                seg++;
            float segLen = cum[seg + 1] - cum[seg];
            float u = segLen > 1e-4f ? math.saturate((along - cum[seg]) / segLen) : 0f;
            float2 a = pts[seg].xz, b = pts[seg + 1].xz;
            float2 dir = segLen > 1e-4f ? (b - a) / segLen : new float2(0f, 1f);
            float2 r = new float2(dir.y, -dir.x);   // right of the direction A to B in CS2's x-right / z-forward plane
            right = new float3(r.x, 0f, r.y);
            float2 xz = math.lerp(a, b, u) + r * lateral;
            return new float3(xz.x, 0f, xz.y);
        }

        /// <summary>Walk the component into paths of nodes (start at a degree-1 node, lowest y first. Branches restart at a visited node
        /// with an unvisited edge).</summary>
        internal List<List<Hop>> Paths(List<LiftCensus.Edge> comp)
        {
            var adj = new Dictionary<Entity, List<int>>();
            for (int i = 0; i < comp.Count; i++)
            {
                AddAdj(adj, LiftCensusSystem.Ent(comp[i].Start), i);
                AddAdj(adj, LiftCensusSystem.Ent(comp[i].End), i);
            }
            var usedEdge = new bool[comp.Count];
            var visited = new HashSet<Entity>();
            var paths = new List<List<Hop>>();
            int guard = 0;
            while (guard++ < 64)
            {
                // Start node: unvisited degree-1 node with the lowest y, else a visited node with an unvisited edge, else any unvisited node.
                Entity start = Entity.Null;
                float bestY = float.PositiveInfinity;
                foreach (KeyValuePair<Entity, List<int>> kv in adj)
                {
                    if (visited.Contains(kv.Key) || kv.Value.Count != 1 || usedEdge[kv.Value[0]])
                        continue;
                    float y = NodeY(kv.Key);
                    if (y < bestY)
                    {
                        bestY = y;
                        start = kv.Key;
                    }
                }
                if (start == Entity.Null)
                {
                    foreach (KeyValuePair<Entity, List<int>> kv in adj)
                    {
                        if (!visited.Contains(kv.Key) && !HasUnusedEdge(kv.Value, usedEdge))
                            continue;
                        if (HasUnusedEdge(kv.Value, usedEdge))
                        {
                            start = kv.Key;
                            break;
                        }
                    }
                }
                if (start == Entity.Null)
                    break;
                var path = new List<Hop>();
                Entity cur = start;
                visited.Add(cur);
                while (true)
                {
                    int next = -1;
                    List<int> edgesAt = adj[cur];
                    for (int k = 0; k < edgesAt.Count; k++)
                    {
                        if (!usedEdge[edgesAt[k]])
                        {
                            next = edgesAt[k];
                            break;
                        }
                    }
                    if (next < 0)
                    {
                        path.Add(new Hop { Node = cur });
                        break;
                    }
                    usedEdge[next] = true;
                    Entity a = LiftCensusSystem.Ent(comp[next].Start), b = LiftCensusSystem.Ent(comp[next].End);
                    Entity other = a == cur ? b : a;
                    path.Add(new Hop { Node = cur, NextEdge = LiftCensusSystem.Ent(comp[next].Id), NextNode = other });
                    visited.Add(other);
                    cur = other;
                }
                paths.Add(path);
            }
            return paths;
        }

        private static void AddAdj(Dictionary<Entity, List<int>> adj, Entity node, int edge)
        {
            if (!adj.TryGetValue(node, out List<int> list))
            {
                list = new List<int>();
                adj[node] = list;
            }
            list.Add(edge);
        }

        private static bool HasUnusedEdge(List<int> edges, bool[] used)
        {
            for (int i = 0; i < edges.Count; i++)
                if (!used[edges[i]])
                    return true;
            return false;
        }

        private float NodeY(Entity node)
        {
            return EntityManager.TryGetComponent<Game.Net.Node>(node, out var n) ? n.m_Position.y : 0f;
        }

        /// <summary>One node into the [summary] totals: its Elevation, the chord clearance of the span to the next node (SampleT), and the
        /// towers the engine spawned in its SubObject buffer (carriers and deleted skipped) with their pivot height above the terrain.</summary>
        private void SummarizeNode(ref TerrainHeightData hd, Hop hop, IReadOnlyDictionary<Entity, float> carriers,
                                   SortedDictionary<string, int> towerCounts, ref int noTower, ref float maxFloat, ref float minClr, ref float maxElev)
        {
            if (!EntityManager.TryGetComponent<Game.Net.Node>(hop.Node, out var node))
                return;
            float3 pos = node.m_Position;
            if (EntityManager.TryGetComponent<Game.Net.Elevation>(hop.Node, out var ne))
                maxElev = math.max(maxElev, math.cmin(ne.m_Elevation));
            if (hop.NextEdge != Entity.Null && EntityManager.TryGetComponent<Game.Net.Node>(hop.NextNode, out var nextNode))
            {
                for (int s = 0; s < SampleT.Length; s++)
                {
                    float3 q = math.lerp(pos, nextNode.m_Position, SampleT[s]);
                    minClr = math.min(minClr, q.y - TerrainUtils.SampleHeight(ref hd, q));
                }
            }
            int towers = 0;
            if (EntityManager.TryGetBuffer(hop.Node, true, out DynamicBuffer<Game.Objects.SubObject> subs))
            {
                for (int i = 0; i < subs.Length; i++)
                {
                    Entity sub = subs[i].m_SubObject;
                    if (EntityManager.HasComponent<Deleted>(sub) || !EntityManager.TryGetComponent<PrefabRef>(sub, out var subPrefab) ||
                        carriers.ContainsKey(subPrefab.m_Prefab))
                        continue;
                    towers++;
                    string name = m_PrefabSystem.GetPrefabName(subPrefab.m_Prefab);
                    towerCounts.TryGetValue(name, out int count);
                    towerCounts[name] = count + 1;
                    if (EntityManager.TryGetComponent<Game.Objects.Transform>(sub, out var tr))
                        maxFloat = math.max(maxFloat, tr.m_Position.y - TerrainUtils.SampleHeight(ref hd, tr.m_Position));
                }
            }
            if (towers == 0)
                noTower++;
        }

        private static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
        private static string Sg(float v) => (v >= 0f ? "+" : string.Empty) + v.ToString("F1", CultureInfo.InvariantCulture);
        private static string Fmt(Entity e) => e.Index + ":" + e.Version;
    }
}
