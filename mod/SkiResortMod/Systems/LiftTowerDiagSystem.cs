// LiftTowerDiagSystem — READ-ONLY diagnostic: what the engine ACTUALLY did at every node of a lift line
// (docs/research/field/2026-10-15/build/T03-tower-diag.md; ticket T03 step 1, Richard 2026-10-16: "long span across a
// gully produced no taller towers, floating nodes got the same tower on stretched legs"). It changes nothing: no
// command buffer, no component write, no prefab touch, no per-frame work (an int compare while nothing is pending).
//
// TRIGGERS
//   * LiftCensusSystem.BuildCensus calls OnCensusBuilt(edges) after every build (load baseline included). Components of
//     lift-line edges are signed (edge ids + node positions); a component whose signature was not logged yet is QUEUED.
//   * A queued line is printed once the census has been quiet for SettleFrames updates (a Move It drag or a station nudge
//     keeps resetting the timer, so a drag prints one block, not one per build) — and at most MaxBlocksPerEmit blocks per
//     emission (the rest are counted in one line; DUMP prints them).
//   * DumpAll(reason): every line of the last census build, on demand (hub DUMP / census dump), MaxBlocksPerDump cap.
//
// OUTPUT (log tag "Towers [nodes]", one block per line, <= MaxNodes nodes; "Towers [summary]" one line per line):
//   per node: world y, terrain y under it, node clearance (y - terrain), net Elevation component (float2; the engine uses
//   cmin of it, SubObjectSystem.cs:980), per span to the next node the min clearance of the CHORD over 4 samples (t = .2 .4
//   .6 .8) and where, the largest droop of the Curve below/above the chord, the tower sub-object(s) in the node's SubObject
//   buffer (prefab name, entity, mesh top, ObjectGeometryData size / bounds / leg / flags, Elevation component, pivot height
//   above terrain), and the placeholder: every member with its m_Size.y and the score the engine formula gives, the formula
//   winner marked, plus MATCH / MISMATCH against the prefab actually spawned.
//
// ENGINE FORMULA REPLICATED (decompile 1.6.0f1, Game.Objects/SubObjectSystem.cs): per placeholder member (PillarData
// Standalone / Base / Vertical, :2173-2215) spare = (m_Size.y - PlacementOffset.y) - (ownerElevation + local-to-world dy of the
// spec position); score = 1 / (1 + (spare >= 0 ? 2*spare : -spare)) (:2182-2184, :2197-2199, :2212-2214); best score wins, equal
// scores break by probability-weighted random (:2336-2358). NOT replicated (UNVERIFIED effect): CheckRequirements per member
// (:2148), the +0.5 for a group already selected this update (:2331). ownerElevation = cmin(Game.Net.Elevation) (:980).
//
// PLAN (T48 step 2, docs/research/field/2026-10-19/tower-planner-build-1.md; read-only like the rest): after every "Towers [nodes]"
// block, each path of the line is re-planned by the pure LiftTowerPlanner on the REAL terrain (TerrainUtils.SampleHeight on the
// TerrainSystem.GetHeightData() heightmap, TerrainUtils.cs:82 / TerrainSystem.cs:2603, the same main-thread read the [nodes] block
// uses) along the path's node polyline (xz), from its first node to its last, both ends taken as stations with the rope at node y +
// the shortest member's rope height (the field lines' ends carry a shortest tower; a station stub's own lane height is an open item
// for step 3b). It logs "Towers [plan]" (planned vs actual node count, per planned node the member, rope and flags, per span the
// lowest carrier above ground and the max rope height) + one "Towers [plan summary]" line, and caches the planned geometry for
// LiftTowerPlanOverlaySystem, keyed by every edge and node entity of the line (so selecting a section, a node or a tower finds it).
// Selecting a line whose plan is not cached (e.g. one past the per-emission cap) queues it here: RequestPlan → next update.
//
// Mod.cs: UpdateAfter<LiftTowerDiagSystem, LiftCensusSystem>(Modification4B) — same anchor family as TramShuttleSystem.

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
        /// <summary>Census-quiet updates before a queued line is printed (towers exist by then; a drag keeps resetting it).</summary>
        internal const int SettleFrames = 30;
        internal const int MaxNodes = 64;
        internal const int MaxBlocksPerEmit = 12;
        internal const int MaxBlocksPerDump = 24;
        private static readonly float[] SampleT = { 0.2f, 0.4f, 0.6f, 0.8f };

        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private PrefabSystem m_PrefabSystem;
        private TerrainSystem m_TerrainSystem;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Frame, m_LastQueueFrame;
        private readonly HashSet<uint> m_Logged = new HashSet<uint>();
        private readonly List<List<LiftCensus.Edge>> m_Pending = new List<List<LiftCensus.Edge>>();
        private readonly List<LiftCensus.Edge> m_LastEdges = new List<LiftCensus.Edge>();
        private int m_Blocks, m_Dumps;

        // T48 step 2: the planner and the overlay cache.
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
                int printed = 0;
                int skipped = 0;
                for (int i = 0; i < m_Pending.Count; i++)
                {
                    if (printed >= MaxBlocksPerEmit)
                    {
                        skipped++;
                        continue;
                    }
                    if (WriteLine(m_Pending[i], "new or changed"))
                        printed++;
                }
                if (skipped > 0)
                    Mod.Log.Info($"Towers [nodes]: {skipped} more new or changed line(s) not printed (cap {MaxBlocksPerEmit} per emission); the DUMP prints every line");
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

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Triggers
        // ════════════════════════════════════════════════════════════════════════════════════════

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
                Mod.Log.Info($"Towers [nodes]: DUMP ({reason}) — not armed (no game loaded)");
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
                    if (WriteLine(comps[c], "dump: " + reason))
                        printed++;
                }
                Mod.Log.Info($"Towers [nodes]: DUMP ({reason}) — {printed} of {comps.Count} line(s) printed " +
                             $"(cap {MaxBlocksPerDump}); {m_Blocks} block(s) this load, {m_Dumps} dump(s), {m_Logged.Count} signature(s) seen, " +
                             $"{m_Pending.Count} queued{(m_Disabled ? "; DISABLED after an exception" : string.Empty)}");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Towers [nodes]: DUMP ({reason}) failed ({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Components and signature (pure data)
        // ════════════════════════════════════════════════════════════════════════════════════════

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

        /// <summary>Order-independent hash of the component's edge ids and node positions (0.1 m): a redraw, a moved node or
        /// a moved station gives a new signature; an unrelated build does not.</summary>
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

        // ════════════════════════════════════════════════════════════════════════════════════════
        // One line -> one block
        // ════════════════════════════════════════════════════════════════════════════════════════

        internal struct Hop
        {
            public Entity Node;
            public Entity NextEdge;     // the edge to the following node in this path (Null at the path's end)
            public Entity NextNode;
        }

        private bool WriteLine(List<LiftCensus.Edge> comp, string why)
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
                Mod.Log.Info("Towers [nodes]: terrain heightmap not ready — line skipped (DUMP again later)");
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

            var sb = new StringBuilder(2048);
            sb.Append("Towers [nodes]: line ").Append(typeName).Append(" edge ").Append(Fmt(LiftCensusSystem.Ent(comp[0].Id)))
              .Append(" (").Append(why).Append(") — ").Append(nodeTotal).Append(" node(s), ").Append(comp.Count)
              .Append(" span(s), ").Append(F(totalLen, "F0")).Append(" m of curve");
            if (paths.Count > 1)
                sb.Append(", ").Append(paths.Count).Append(" paths (branches)");
            sb.Append('\n');

            var towerCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int noTower = 0, mismatches = 0, ladderMissed = 0, shown = 0, nodesDone = 0;
            float maxFloat = float.NegativeInfinity, minClr = float.PositiveInfinity, maxElev = 0f;
            string minClrWhere = "-", maxFloatWhere = "-";

            for (int p = 0; p < paths.Count && shown < MaxNodes; p++)
            {
                if (paths.Count > 1)
                    sb.Append("  -- path ").Append(p).Append(" --\n");
                List<Hop> path = paths[p];
                for (int i = 0; i < path.Count; i++)
                {
                    nodesDone++;
                    Hop hop = path[i];
                    string label = "n" + nodesDone.ToString(CultureInfo.InvariantCulture);
                    if (shown >= MaxNodes)
                        break;
                    shown++;
                    DescribeNode(sb, ref hd, label, hop, towerCounts, ref noTower, ref mismatches, ref ladderMissed,
                                 ref maxFloat, ref maxFloatWhere, ref minClr, ref minClrWhere, ref maxElev);
                }
            }
            if (nodeTotal > shown)
                sb.Append("  … ").Append(nodeTotal - shown).Append(" more node(s) not printed (cap ").Append(MaxNodes).Append(")\n");

            sb.Append("Towers [summary]: line ").Append(typeName).Append(" edge ").Append(Fmt(LiftCensusSystem.Ent(comp[0].Id)))
              .Append(": ").Append(nodeTotal).Append(" node(s); towers {");
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
              .Append("; max pivot-above-terrain (leg gap) ").Append(maxFloat > float.NegativeInfinity ? F(maxFloat, "F1") + " m at " + maxFloatWhere : "n/a")
              .Append("; min chord clearance ").Append(minClr < float.PositiveInfinity ? F(minClr, "F1") + " m at " + minClrWhere : "n/a")
              .Append("; formula MISMATCH ").Append(mismatches).Append(" node(s); elevation>=4 but shortest member chosen ").Append(ladderMissed).Append(" node(s)");
            Mod.Log.Info(sb.ToString());
            m_Blocks++;

            // T48 step 2: the planned line beside the actual one (its own try: a planner fault must not cost the [nodes] block).
            try
            {
                WritePlans(comp, paths, typeDef, typeName, why, ref hd);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Towers [plan]: line {typeName} edge {Fmt(LiftCensusSystem.Ent(comp[0].Id))} — planner failed " +
                             $"({e.GetType().Name}: {e.Message}); the [nodes] block above is unaffected\n{e}");
            }
            return true;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // T48 step 2: plan + overlay cache
        // ════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>The overlay's lookup: the cached plan of the line that <paramref name="e"/> (edge, node) belongs to.</summary>
        internal bool TryGetPlanDraw(Entity e, out LiftPlanDraw draw) => m_Draws.TryGetValue(e, out draw);

        /// <summary>The overlay asks for a plan of a selected entity that has none cached; served on the next update (at most one
        /// per update, an entity that is not on a line is remembered until the next census build).</summary>
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
                        if (!WriteLine(comp, "selected (overlay)"))
                            m_NotALine.Add(want);
                        return;
                    }
                }
            }
            m_NotALine.Add(want);
        }

        private void WritePlans(List<LiftCensus.Edge> comp, List<List<Hop>> paths, LiftTypeDef typeDef, string typeName, string why,
                                ref TerrainHeightData hd)
        {
            string head = "line " + typeName + " edge " + Fmt(LiftCensusSystem.Ent(comp[0].Id));
            LiftTowerPlanConstants constants = LiftTowerPlanConstants.For(typeDef);
            if (constants == null)
            {
                Mod.Log.Info($"Towers [plan]: {head} — no planner constants for lift type '{typeName}' (untyped line): not planned");
                return;
            }
            if (m_Draws.Count > MaxCachedEntities)
                m_Draws.Clear();   // bound: entities of lines long deleted; the overlay re-requests what it needs
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
                    Mod.Log.Info($"Towers [plan]: {pathHead} — {length:F1} m of line, too short to plan");
                    continue;
                }

                LiftTerrainSampler sampler = (along, lateral) =>
                {
                    float3 at = PolylineAt(pts, cum, along, lateral, out _);
                    return TerrainUtils.SampleHeight(ref terrain, at);
                };
                float ropeA = pts[0].y + constants.RopeAboveBase(0);
                float ropeB = pts[pts.Length - 1].y + constants.RopeAboveBase(0);
                LiftTowerPlan plan = m_Planner.Plan(sampler, length, ropeA, ropeB, constants);
                m_Plans++;

                var sb = new StringBuilder(2048);
                plan.AppendLog(sb, pathHead + " (" + why + "; ends = the path's first / last node, rope at node y + " +
                                   F(constants.RopeAboveBase(0), "F2") + ") vs actual " + path.Count + " node(s), " +
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
                  .Append(violated == 0 ? "every span clear" : violated + " span(s) VIOLATED (no clear arrangement on this ground)");
                Mod.Log.Info(sb.ToString());

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

        /// <summary>The point <paramref name="along"/> metres (xz) along the node polyline, <paramref name="lateral"/> metres to the
        /// right of its local direction (y = 0); <paramref name="right"/> = that unit right vector.</summary>
        internal static float3 PolylineAt(float3[] pts, float[] cum, float along, float lateral, out float3 right)
        {
            int seg = 0;
            while (seg < pts.Length - 2 && cum[seg + 1] < along)
                seg++;
            float segLen = cum[seg + 1] - cum[seg];
            float u = segLen > 1e-4f ? math.saturate((along - cum[seg]) / segLen) : 0f;
            float2 a = pts[seg].xz, b = pts[seg + 1].xz;
            float2 dir = segLen > 1e-4f ? (b - a) / segLen : new float2(0f, 1f);
            float2 r = new float2(dir.y, -dir.x);   // right of A→B in CS2's x-right / z-forward plane
            right = new float3(r.x, 0f, r.y);
            float2 xz = math.lerp(a, b, u) + r * lateral;
            return new float3(xz.x, 0f, xz.y);
        }

        /// <summary>Walk the component into paths of nodes (start at a degree-1 node, lowest y first; branches restart at a
        /// visited node with an unvisited edge).</summary>
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

        // ════════════════════════════════════════════════════════════════════════════════════════
        // One node
        // ════════════════════════════════════════════════════════════════════════════════════════

        private void DescribeNode(StringBuilder sb, ref TerrainHeightData hd, string label, Hop hop,
                                  SortedDictionary<string, int> towerCounts, ref int noTower, ref int mismatches, ref int ladderMissed,
                                  ref float maxFloat, ref string maxFloatWhere, ref float minClr, ref string minClrWhere, ref float maxElev)
        {
            if (!EntityManager.TryGetComponent<Game.Net.Node>(hop.Node, out var node))
            {
                sb.Append("  ").Append(label).Append(' ').Append(Fmt(hop.Node)).Append(": no Node component\n");
                return;
            }
            float3 pos = node.m_Position;
            float terr = TerrainUtils.SampleHeight(ref hd, pos);
            float2 netElev = EntityManager.TryGetComponent<Game.Net.Elevation>(hop.Node, out var ne) ? ne.m_Elevation : default;
            float ownerElev = math.cmin(netElev);
            if (ownerElev > maxElev)
                maxElev = ownerElev;
            sb.Append("  ").Append(label).Append(' ').Append(Fmt(hop.Node))
              .Append(": y ").Append(F(pos.y, "F2")).Append(", terrain ").Append(F(terr, "F2"))
              .Append(", clearance ").Append(Sg(pos.y - terr)).Append(", net Elevation (").Append(F(netElev.x, "F2")).Append(", ")
              .Append(F(netElev.y, "F2")).Append(") -> engine uses ").Append(F(ownerElev, "F2")).Append('\n');

            // Span to the next node.
            if (hop.NextEdge != Entity.Null && EntityManager.TryGetComponent<Game.Net.Node>(hop.NextNode, out var nextNode))
            {
                float3 p1 = nextNode.m_Position;
                float minHere = float.PositiveInfinity, minAt = 0f;
                var samples = new StringBuilder();
                float spanXz = math.distance(pos.xz, p1.xz);
                float droop = 0f;
                bool haveCurve = EntityManager.TryGetComponent<Game.Net.Curve>(hop.NextEdge, out var curve);
                bool reversed = false;
                if (haveCurve && EntityManager.TryGetComponent<Game.Net.Edge>(hop.NextEdge, out var topo))
                    reversed = topo.m_End == hop.Node;
                for (int s = 0; s < SampleT.Length; s++)
                {
                    float t = SampleT[s];
                    float3 q = math.lerp(pos, p1, t);
                    float clr = q.y - TerrainUtils.SampleHeight(ref hd, q);
                    if (samples.Length > 0)
                        samples.Append(' ');
                    samples.Append(Sg(clr));
                    if (clr < minHere)
                    {
                        minHere = clr;
                        minAt = t;
                    }
                    if (haveCurve)
                    {
                        float tc = reversed ? 1f - t : t;
                        float3 c = MathUtils.Position(curve.m_Bezier, tc);
                        droop = math.max(droop, math.abs(c.y - q.y));
                    }
                }
                sb.Append("      span -> ").Append(Fmt(hop.NextNode)).Append(": ").Append(F(spanXz, "F0")).Append(" m xz, dy ")
                  .Append(Sg(p1.y - pos.y)).Append(", chord clearance samples at 20/40/60/80 %: ").Append(samples)
                  .Append(" -> min ").Append(Sg(minHere)).Append(" at ").Append(F(minAt * spanXz, "F0")).Append(" m (")
                  .Append(F(minAt * 100f, "F0")).Append(" %)");
                if (droop > 0.25f)
                    sb.Append("; curve differs from chord by up to ").Append(F(droop, "F1")).Append(" m");
                sb.Append('\n');
                if (minHere < minClr)
                {
                    minClr = minHere;
                    minClrWhere = "span after " + label + ", " + F(minAt * spanXz, "F0") + " m along";
                }
            }

            // The towers actually spawned at this node.
            EntityManager.TryGetComponent<PrefabRef>(hop.Node, out var nodePrefab);
            IReadOnlyDictionary<Entity, float> carriers = m_PrefabCatalog.CarrierGripTops;
            string spawnedName = null;
            Entity spawnedPrefab = Entity.Null;
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
                    spawnedName = name;
                    spawnedPrefab = subPrefab.m_Prefab;
                    towerCounts.TryGetValue(name, out int count);
                    towerCounts[name] = count + 1;
                    DescribeTower(sb, ref hd, sub, subPrefab.m_Prefab, name, pos, label, ref maxFloat, ref maxFloatWhere);
                }
            }
            if (towers == 0)
            {
                noTower++;
                sb.Append("      tower: none in the node's SubObject buffer\n");
            }

            DescribePlaceholder(sb, nodePrefab.m_Prefab, node, ownerElev, spawnedPrefab, spawnedName, ref mismatches, ref ladderMissed);
        }

        private void DescribeTower(StringBuilder sb, ref TerrainHeightData hd, Entity sub, Entity prefab, string name, float3 nodePos,
                                   string label, ref float maxFloat, ref string maxFloatWhere)
        {
            sb.Append("      tower: '").Append(name).Append("' ").Append(Fmt(sub)).Append(" prefab ").Append(Fmt(prefab));
            if (!EntityManager.TryGetComponent<Game.Objects.Transform>(sub, out var tr))
            {
                sb.Append(" (no Transform)\n");
                return;
            }
            float terrAt = TerrainUtils.SampleHeight(ref hd, tr.m_Position);
            float pivotAbove = tr.m_Position.y - terrAt;
            sb.Append(": pivot y ").Append(F(tr.m_Position.y, "F2")).Append(" (").Append(Sg(tr.m_Position.y - nodePos.y)).Append(" vs node), terrain there ")
              .Append(F(terrAt, "F2")).Append(", pivot above terrain ").Append(Sg(pivotAbove));
            if (pivotAbove > maxFloat)
            {
                maxFloat = pivotAbove;
                maxFloatWhere = label;
            }
            if (EntityManager.TryGetComponent<ObjectGeometryData>(prefab, out var geo))
            {
                sb.Append("; m_Size.y ").Append(F(geo.m_Size.y, "F2")).Append(", bounds y [").Append(F(geo.m_Bounds.min.y, "F2")).Append(", ")
                  .Append(F(geo.m_Bounds.max.y, "F2")).Append("], top above node ").Append(Sg(tr.m_Position.y + geo.m_Bounds.max.y - nodePos.y))
                  .Append(", m_LegSize (").Append(F(geo.m_LegSize.x, "F1")).Append(", ").Append(F(geo.m_LegSize.y, "F2")).Append(", ")
                  .Append(F(geo.m_LegSize.z, "F1")).Append(") flags ").Append(GeoFlags(geo.m_Flags));
            }
            else
            {
                sb.Append("; no ObjectGeometryData");
            }
            if (EntityManager.TryGetComponent<Game.Objects.Elevation>(sub, out var el))
                sb.Append("; tower Elevation ").Append(F(el.m_Elevation, "F2")).Append(" flags ").Append(el.m_Flags);
            else
                sb.Append("; tower has no Elevation component");
            if (EntityManager.TryGetComponent<Game.Objects.Stack>(sub, out var stack))
                sb.Append("; Stack range [").Append(F(stack.m_Range.min, "F2")).Append(", ").Append(F(stack.m_Range.max, "F2")).Append(']');
            if (EntityManager.TryGetComponent<PillarData>(prefab, out var pillar))
                sb.Append("; PillarData ").Append(pillar.m_Type);
            sb.Append('\n');
        }

        private void DescribePlaceholder(StringBuilder sb, Entity linePrefab, Game.Net.Node node, float ownerElev, Entity spawned,
                                         string spawnedName, ref int mismatches, ref int ladderMissed)
        {
            if (linePrefab == Entity.Null ||
                !EntityManager.TryGetBuffer(linePrefab, true, out DynamicBuffer<Game.Prefabs.SubObject> specs))
            {
                sb.Append("      placeholder: line prefab has no SubObject buffer\n");
                return;
            }
            bool any = false;
            for (int i = 0; i < specs.Length; i++)
            {
                Game.Prefabs.SubObject spec = specs[i];
                if ((spec.m_Flags & SubObjectFlags.EdgePlacement) != 0)
                    continue;
                if (!EntityManager.TryGetBuffer(spec.m_Prefab, true, out DynamicBuffer<PlaceholderObjectElement> members))
                    continue;
                any = true;
                string phName = m_PrefabSystem.GetPrefabName(spec.m_Prefab);
                float dy = math.rotate(node.m_Rotation, spec.m_Position).y;
                float required = ownerElev + dy;
                sb.Append("      placeholder '").Append(phName).Append("': spec y ").Append(F(spec.m_Position.y, "F3"))
                  .Append(", required = Elevation ").Append(F(ownerElev, "F2")).Append(" + world dy ").Append(F(dy, "F3"))
                  .Append(" = ").Append(F(required, "F2")).Append("; members (m_Size.y -> score):");
                float best = -1f, shortest = float.PositiveInfinity;
                Entity winner = Entity.Null, shortestPrefab = Entity.Null;
                string winnerName = "-";
                int ties = 0;
                var scores = new float[members.Length];
                var names = new string[members.Length];
                var sizes = new float[members.Length];
                for (int m = 0; m < members.Length; m++)
                {
                    Entity member = members[m].m_Object;
                    names[m] = m_PrefabSystem.GetPrefabName(member);
                    float sizeY = 0f, offY = 0f;
                    if (EntityManager.TryGetComponent<ObjectGeometryData>(member, out var g))
                        sizeY = g.m_Size.y;
                    if (EntityManager.TryGetComponent<PlaceableObjectData>(member, out var po))
                        offY = po.m_PlacementOffset.y;
                    sizes[m] = sizeY - offY;
                    float score = 0f;
                    if (EntityManager.TryGetComponent<PillarData>(member, out var pd) &&
                        (pd.m_Type == PillarType.Standalone || pd.m_Type == PillarType.Base || pd.m_Type == PillarType.Vertical))
                    {
                        float spare = sizes[m] - required;
                        score = 1f / (1f + (spare >= 0f ? 2f * spare : -spare));
                    }
                    scores[m] = score;
                    if (sizes[m] < shortest)
                    {
                        shortest = sizes[m];
                        shortestPrefab = member;
                    }
                    if (score > best + 1e-6f)
                    {
                        best = score;
                        winner = member;
                        winnerName = names[m];
                        ties = 1;
                    }
                    else if (math.abs(score - best) <= 1e-6f)
                    {
                        ties++;
                    }
                }
                for (int m = 0; m < members.Length; m++)
                {
                    sb.Append(' ').Append(names[m]).Append(' ').Append(F(sizes[m], "F2")).Append("->").Append(F(scores[m], "F3"))
                      .Append(members[m].m_Object == winner ? "*WIN" : string.Empty);
                }
                sb.Append("; formula winner ").Append(winnerName).Append(ties > 1 ? " (TIE of " + ties + ", engine picks by probability-weighted random)" : string.Empty);
                if (spawned != Entity.Null)
                {
                    bool match = spawned == winner;
                    sb.Append("; spawned ").Append(spawnedName).Append(match ? " = MATCH" : " = MISMATCH (formula predicted " + winnerName + ")");
                    if (!match && ties <= 1)
                        mismatches++;
                    if (ownerElev >= 4f && spawned == shortestPrefab)
                        ladderMissed++;
                }
                sb.Append('\n');
            }
            if (!any)
                sb.Append("      placeholder: none (the line prefab names no placeholder at node placement)\n");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Tiny helpers
        // ════════════════════════════════════════════════════════════════════════════════════════

        private static string GeoFlags(Game.Objects.GeometryFlags f)
        {
            var sb = new StringBuilder();
            if ((f & Game.Objects.GeometryFlags.Standing) != 0) sb.Append("Standing,");
            if ((f & Game.Objects.GeometryFlags.HasBase) != 0) sb.Append("HasBase,");
            if ((f & Game.Objects.GeometryFlags.CircularLeg) != 0) sb.Append("CircularLeg,");
            if ((f & Game.Objects.GeometryFlags.IgnoreSecondaryCollision) != 0) sb.Append("IgnoreSecondaryCollision,");
            if ((f & Game.Objects.GeometryFlags.BaseCollision) != 0) sb.Append("BaseCollision,");
            if (sb.Length == 0)
                return "(none)";
            sb.Length--;
            return sb.ToString();
        }

        private static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
        private static string Sg(float v) => (v >= 0f ? "+" : string.Empty) + v.ToString("F1", CultureInfo.InvariantCulture);
        private static string Fmt(Entity e) => e.Index + ":" + e.Version;
    }
}
