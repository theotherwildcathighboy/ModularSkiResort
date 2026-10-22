// LiftTowerFitSystem — T48 step 3: fits every new or changed lift line so its carriers keep the floor over the ground (ticket
// T48, DECISIONS 306 / 311; design + decompile trail docs/research/field/2026-10-19-evening/t48-step3-build.md). The planner is the
// pure LiftTowerPlanner (Fit); this system only reads the line, asks, writes the answer back and logs.
//
// WHAT IT CHANGES (the integration point, build note §1)
//   The engine splits a drawn line into equal spans <= MaxPylonDistance (CourseSplitSystem.cs:3485-3493) with every node on the ground,
//   so the placeholder ladder always spawns the shortest tower (analysis-towers.md §2). The node COUNT stays the engine's (adding a
//   node needs a new course: step 3c); per inner node this system sets
//     * its POSITION along the line: kept, or slid to the planner's spot when heights alone cannot clear the line and the line is
//       straight in xz (power-line edges are straight, NetInitializeSystem.cs:2195 StraightEdges);
//     * its HEIGHT: node y = center ground + E and Game.Net.Elevation = (E, E), E = TopM[member] - TopM[0] (LiftTowerEdit): the vanilla
//       ladder then scores the planned member exactly 1 (SubObjectSystem.cs:980, :2189-2199) and, top-anchored (:2464-2469,
//       :2477-2482), stands it on the ground; the rope lane rides node y + lane y (LaneSystem.cs:4112-4115). E = 0 removes the component (the engine adds
//       it only for a non-zero elevation, GenerateNodesSystem.cs:1497-1499);
//     * each touched edge's Curve = NetUtils.StraightCurve(start, end, net m_Hanging), the engine's own power-line curve
//       (NetUtils.cs:178-191, CourseSplitSystem.cs:1702-1704), m_Length = MathUtils.Length (as Curve.Deserialize does);
//   and tags the nodes and edges Updated. Nothing re-snaps them: GroundHeightSystem skips FlattenTerrain nets without a lot owner
//   (edges :661-667, nodes :747-749; every non-marker power line gets FlattenTerrain, NetInitializeSystem.cs:2184-2187). Station
//   stub nodes, branch nodes and owned nodes are never touched. A later edit by the net tool starts from these values
//   (GenerateEdgesSystem.cs:1421-1425 copies the original's Elevation).
//
// PHASE (Mod.cs): UpdateAt Modification1. Load-bearing: every consumer of the Updated tag runs later in the same frame —
// GroundHeightSystem (Modification2), SubObjectSystem (2B; owner query All SubObject + Any Updated, :3936-3943), CompositionSelect (3),
// Net.GeometrySystem and LaneSystem (4), the census and ChairPinSystem (4B) — and CleanUpSystem wipes the tag at the end of the frame
// (SystemOrder.cs:54), so a tag added at 4B would be lost.
//
// TRIGGER: LiftCensusSystem.BuildCensus -> OnCensusBuilt(edges) after every build (load baseline included: lines of an older build
// are fitted on load too). A component whose signature (LiftTowerDiagSystem.Signature: edge ids + node positions) is new is queued and
// fitted once the census has been quiet for SettleFrames updates (a drag keeps resetting the timer). The fit is a fixed point
// (LiftBatch LP-13): the build it causes queues the line again and the re-fit writes nothing ("already fitted"). Guard: a line written
// MaxWrites times within GuardFrames is left alone with a WARN.
//
// GROUND (T127, 2026-10-20 playthrough): the terrain sampler is LiftTowerEdit.UncutSampler — a sample at this line's own cut ceiling
// (edge y + composition height range min + terrain offset, TerrainSystem.AddSegment) is our trench, so the plane through the uncut
// ground beside it stands in; towers stood in the trenches of a vanilla line drawn on the ground and kept round pits around them.
// MOVED (T128): the free plan's own members travel with its nodes, spares get the shortest clearing member (LiftTowerPlanner.Fit);
// a standing line within LiftTowerPlanner.KeepTolM of the floor is kept (grid noise, not Short).
//
// TOOL LINES (T48 round 2; R-6 2026-10-22, docs/research/lifts/lift-tool-split-fix-2026-10-22.md): a line the lift-line tool built
// is fitted heights-only with the tool's members, never Moved. Exact match = LiftToolLines.Match (same node count); when the engine
// split the tool's courses into more edges (CourseSplitSystem elevation transitions), LiftToolLines.MatchSplit still recognizes it
// (every emitted node within 0.5 m, ends = the tool's, extras on the line) and gives each extra split node the member that keeps the
// rope on the planned chord (ChordMembers). One Info line per such match; a WARN when a tool line's pinned towers stay put on a
// Short line, or when a line holding a tool line's pinned nodes did NOT match and is Moved. Ordinary lines: unchanged.
//
// LOGS (silence impossible): one ARMED line per load; per fitted line a "Towers [fit]" block (per tower: position before -> after,
// member before -> after, elevation, rope; per span: length, lowest carrier over the high side before -> after and where, max rope
// above ground) + one "Towers [fit summary]"; "already fitted" one line; Short = WARN with the tower count the line needs; skips
// (branched / bent with a non-plain node / no constants) one Info line each; an exception disables the system until the next load.

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
    public sealed partial class LiftTowerFitSystem : GameSystemBase
    {
        internal const int SettleFrames = 30;
        internal const int MaxWrites = 3;
        internal const int GuardFrames = 600;
        private const float MoveEpsM = 0.05f;      // xz change that counts as a move
        // Height writes with the member unchanged: LiftTowerEdit.HeightWrite (one-sided, T115: below 0.25 m / above 1.0 m).
        private const float StraightTolM = 0.5f;   // inner node off the end-to-end line: the line is bent, heights only

        private LiftTowerDiagSystem m_Diag;
        private TerrainSystem m_TerrainSystem;
        private LiftLineToolSystem m_LineTool;
        private readonly LiftTowerPlanner m_Planner = new LiftTowerPlanner();

        private bool m_Armed, m_Disabled, m_ErrorLogged, m_OffLogged;
        private int m_Frame, m_LastQueueFrame;
        private readonly HashSet<uint> m_Seen = new HashSet<uint>();
        private readonly List<List<LiftCensus.Edge>> m_Pending = new List<List<LiftCensus.Edge>>();
        private readonly Dictionary<Entity, int2> m_Writes = new Dictionary<Entity, int2>();   // first inner node -> (count, first frame)
        private int m_Fitted, m_Written, m_Short;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Diag = World.GetOrCreateSystemManaged<LiftTowerDiagSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_LineTool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
            Mod.Log.Info($"{nameof(LiftTowerFitSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = m_OffLogged = false;
            m_Frame = m_LastQueueFrame = 0;
            m_Seen.Clear();
            m_Pending.Clear();
            m_Writes.Clear();
            m_Fitted = m_Written = m_Short = 0;
            if (m_Armed)
                Mod.Log.Info($"Towers [fit]: ARMED — fits new / changed lift lines {SettleFrames} quiet census updates after the edit " +
                             $"(heights by node elevation, positions along straight lines, the engine's node count); setting Plan lift towers = " +
                             $"{(Mod.Settings?.LiftTowerPlanner ?? true ? "on" : "OFF")}");
            else
                Mod.Log.Info($"Towers [fit]: NOT ARMED (mode {mode})");
        }

        /// <summary>Called by LiftCensusSystem after each build with its line edges (main thread, no ECS read here).</summary>
        internal void OnCensusBuilt(List<LiftCensus.Edge> edges)
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                List<List<LiftCensus.Edge>> comps = LiftTowerDiagSystem.Components(new List<LiftCensus.Edge>(edges));
                for (int c = 0; c < comps.Count; c++)
                {
                    if (m_Seen.Add(LiftTowerDiagSystem.Signature(comps[c])))
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

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            m_Frame++;
            if (m_Pending.Count == 0 || m_Frame - m_LastQueueFrame < SettleFrames)
                return;
            if (Mod.Settings != null && !Mod.Settings.LiftTowerPlanner)
            {
                if (!m_OffLogged)
                    Mod.Log.Info($"Towers [fit]: {m_Pending.Count} new or changed line(s) NOT fitted — setting Plan lift towers is off");
                m_OffLogged = true;
                m_Pending.Clear();
                return;
            }
            m_OffLogged = false;
            try
            {
                TerrainHeightData hd = m_TerrainSystem.GetHeightData();
                if (!hd.isCreated)
                    return;   // keep the queue; the heightmap comes with the first frames of a load
                for (int i = 0; i < m_Pending.Count; i++)
                    FitLine(m_Pending[i], ref hd);
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
                Mod.Log.Error($"!!! {nameof(LiftTowerFitSystem)} EXCEPTION — lift lines are no longer fitted until the next game load " +
                              $"(lines already fitted keep their towers). Please report this stack:\n{e}");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // One line
        // ════════════════════════════════════════════════════════════════════════════════════════

        private struct Target
        {
            public Entity Node;
            public float3 Old, New;
            public int OldMember, NewMember;
            public float Elevation;   // LiftTowerEdit.ElevationM(NewMember): node y - center ground, written as Elevation (E, E)
            public bool Write;
        }

        private void FitLine(List<LiftCensus.Edge> comp, ref TerrainHeightData hd)
        {
            for (int i = 0; i < comp.Count; i++)
            {
                Entity edge = LiftCensusSystem.Ent(comp[i].Id);
                if (!EntityManager.Exists(edge) || EntityManager.HasComponent<Deleted>(edge))
                    return;   // stale: a newer build queued the replacement
            }
            LiftTypeDef typeDef = SkiCatalog.LiftByIndex(comp[0].Type);
            string typeName = typeDef != null ? typeDef.Id : "untyped";
            Entity firstEdge = LiftCensusSystem.Ent(comp[0].Id);
            string head = "line " + typeName + " edge " + Fmt(firstEdge);
            LiftTowerPlanConstants c = LiftTowerPlanConstants.For(typeDef);
            if (c == null)
            {
                Mod.Log.Info($"Towers [fit]: {head} — no planner constants for lift type '{typeName}': not fitted");
                return;
            }
            List<List<LiftTowerDiagSystem.Hop>> paths = m_Diag.Paths(comp);
            if (paths.Count != 1)
            {
                Mod.Log.Info($"Towers [fit]: {head} — {paths.Count} paths (a branched line): not fitted (one straight run per line only)");
                return;
            }
            List<LiftTowerDiagSystem.Hop> path = paths[0];
            int n = path.Count;
            if (n < 3)
            {
                Mod.Log.Info($"Towers [fit]: {head} — {n} node(s), no line tower to fit");
                return;
            }
            if (!EntityManager.TryGetComponent<PrefabRef>(firstEdge, out var linePrefab) ||
                !EntityManager.TryGetComponent<NetGeometryData>(linePrefab.m_Prefab, out var netGeo))
            {
                Mod.Log.Info($"Towers [fit]: {head} — no NetGeometryData on the line prefab: not fitted");
                return;
            }
            // Inner nodes must be plain nodes of this line: the line prefab, no Owner (a station's sub-net), exactly two edges.
            for (int i = 1; i < n - 1; i++)
            {
                Entity node = path[i].Node;
                bool plain = EntityManager.TryGetComponent<PrefabRef>(node, out var np) && np.m_Prefab == linePrefab.m_Prefab &&
                             !EntityManager.HasComponent<Owner>(node) &&
                             EntityManager.TryGetBuffer(node, true, out DynamicBuffer<Game.Net.ConnectedEdge> ce) && ce.Length == 2;
                if (!plain)
                {
                    Mod.Log.Info($"Towers [fit]: {head} — node {Fmt(node)} (n{i + 1}) is not a plain line node (station sub-net, junction " +
                                 "or another prefab): not fitted");
                    return;
                }
            }

            // Geometry along the node polyline (xz), from the first node.
            var pts = new float3[n];
            for (int i = 0; i < n; i++)
                pts[i] = EntityManager.GetComponentData<Game.Net.Node>(path[i].Node).m_Position;
            var cum = new float[n];
            for (int i = 1; i < n; i++)
                cum[i] = cum[i - 1] + math.distance(pts[i - 1].xz, pts[i].xz);
            float length = cum[n - 1];
            if (length < 2f * c.StepM)
            {
                Mod.Log.Info($"Towers [fit]: {head} — {length:F1} m of line, too short to fit");
                return;
            }
            bool straight = Straight(pts, out float offLine);
            TerrainHeightData terrain = hd;
            LiftTerrainSampler raw = (along, lateral) =>
                TerrainUtils.SampleHeight(ref terrain, LiftTowerDiagSystem.PolylineAt(pts, cum, along, lateral, out _));
            // T127: the ground the player made. A sample at this line's own cut ceiling (edge y + height range min + terrain
            // offset, LiftTowerEdit) is our trench, not the ground: the plane through the uncut ground beside it stands in.
            var cutAt = new HashSet<int>();
            LiftTerrainSampler sampler = LiftTowerEdit.UncutSampler(raw, pts, cum, netGeo.m_Hanging, CutOffsets(path, netGeo),
                c.HalfWidthM + LiftTowerEdit.CutFarMarginM, along => cutAt.Add((int)math.round(along)));

            float ropeA = StationRope(path[0].Node, path[0].NextEdge, pts[0], pts[0].y + c.RopeAboveBase(0), out bool laneA);
            float ropeB = StationRope(path[n - 1].Node, path[n - 2].NextEdge, pts[n - 1], pts[n - 1].y + c.RopeAboveBase(0), out bool laneB);

            var inner = new float[n - 2];
            var oldMembers = new int[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                inner[i - 1] = cum[i];
                float e = EntityManager.TryGetComponent<Game.Net.Elevation>(path[i].Node, out var el) ? math.cmin(el.m_Elevation) : 0f;
                oldMembers[i - 1] = MemberOf(c, e);
            }
            float engineMax = netGeo.m_EdgeLengthRange.max;
            // T48 round 2: a line the lift-line tool built (every inner node a registered tool node, LiftToolLines.Match) is a TOOL
            // LINE: heights only with the tool's members — never Moved, so a pinned node is never snapped.
            var innerPts = new List<float3>(n - 2);
            for (int i = 1; i < n - 1; i++)
                innerPts.Add(pts[i]);
            LiftToolLines.Line toolLine = m_LineTool.ToolLines.Match(innerPts, out int[] toolMembers, out _);
            // R-6 (DECISIONS 368-369; docs/research/lifts/lift-tool-split-fix-2026-10-22.md): the engine built the tool's courses as
            // MORE edges (CourseSplitSystem elevation-transition splits, CourseSplitSystem.cs:2884-2942). Still a tool line when every
            // emitted node has its own path node within 0.5 m: the extras get the member that keeps the rope on the planned chord, and
            // nothing is moved.
            LiftToolMatch split = null;
            string splitNote = string.Empty;
            if (toolLine == null)
            {
                split = m_LineTool.ToolLines.MatchSplit(pts);
                if (split != null)
                {
                    toolLine = split.Line;
                    toolMembers = (int[])split.Members.Clone();
                    var ground = new float[n - 2];
                    for (int i = 0; i < n - 2; i++)
                        ground[i] = sampler(inner[i], 0f);
                    LiftToolLines.ChordMembers(c, inner, ground, toolMembers, oldMembers, length, ropeA, ropeB);
                    var at = new StringBuilder();
                    for (int i = 0; i < n - 2; i++)
                        if (split.Extra[i])
                            at.Append(at.Length > 0 ? ", " : string.Empty).Append(F(inner[i], "F0")).Append(" m ")
                              .Append(Short(c.Ladder[toolMembers[i]].Name));
                    splitNote = $"; {split.Extras} extra engine split node(s) at {at} (members nearest the planned chord; an extra that breaks the clearance is re-filled)";
                    Mod.Log.Info($"Towers [fit]: {head} — TOOL LINE matched despite engine splits: {split.Registered} tool node(s) " +
                                 $"({split.Pins} pinned) found within {LiftToolLines.MatchTolM:F1} m among {n - 2} line node(s), " +
                                 $"{split.Extras} extra split node(s){(split.Reversed ? ", path runs top to base" : string.Empty)}; " +
                                 "heights only, never moved, the tool's members kept" + splitNote);
                }
            }
            LiftToolLines.Line partial = null;
            int partialFound = 0, partialPins = 0;
            if (toolLine == null)
                partial = m_LineTool.ToolLines.BestPartial(pts, out partialFound, out partialPins);
            LiftTowerPlan before = m_Planner.PlanFixed(sampler, length, ropeA, ropeB, c, inner, oldMembers, null, assign: false);
            // T115: the line's own members are passed in, so a line that clears as it stands is Kept unchanged (hysteresis).
            LiftTowerFit fit = m_Planner.Fit(sampler, length, ropeA, ropeB, c, inner, straight && toolLine == null, engineMax,
                                             toolLine != null ? toolMembers : oldMembers, split?.Extra);
            LiftTowerPlan plan = fit.Plan;
            m_Fitted++;
            // R-6: a pinned tower must never move silently.
            int toolPins = split != null ? split.Pins : toolLine != null ? toolLine.Pins : 0;
            if (toolLine != null && toolPins > 0 && fit.Mode == LiftFitMode.Short)
                Mod.Log.Warn($"Towers [fit]: {head} — tool line with {toolPins} pinned tower(s) is not clear with heights alone " +
                             $"(min floor margin {Sg(plan.MinFloorMarginM)} m): the pinned towers stay where they were placed; a clear line " +
                             "would need them moved (open the lift tool on that section, move them, build again)");
            if (partial != null && partialPins > 0 && fit.Mode == LiftFitMode.Moved)
                Mod.Log.Warn($"Towers [fit]: {head} — {partialFound} of {partial.Inner.Length} node(s) of a tool line ({partialPins} " +
                             $"pinned) are on this line but it did not match the tool's plan (ends or a node more than " +
                             $"{LiftToolLines.MatchTolM:F1} m off): fitted as an ordinary line, so the PINNED towers MOVE (please send the log)");
            if (plan.Nodes.Length != n)
            {
                Mod.Log.Warn($"Towers [fit]: {head} — the fit returned {plan.Nodes.Length} nodes for {n} (two nodes in one {c.StepM:F0} m sample?): " +
                             "not written");
                return;
            }

            // Targets.
            var targets = new Target[n - 2];
            int writes = 0;
            for (int i = 1; i < n - 1; i++)
            {
                LiftPlanNode pn = plan.Nodes[i];
                float3 xz = LiftTowerDiagSystem.PolylineAt(pts, cum, pn.AlongM, 0f, out _);
                if (!straight)
                    xz = new float3(pts[i].x, 0f, pts[i].z);   // bent line: positions are kept (the fit did not move them)
                float elevation = LiftTowerEdit.ElevationM(c, pn.Member);
                float3 np = new float3(xz.x, pn.GroundM + elevation, xz.z);
                bool moved = math.distance(np.xz, pts[i].xz) > MoveEpsM;
                bool reMembered = pn.Member != oldMembers[i - 1];
                bool drift = LiftTowerEdit.HeightWrite(pts[i].y, np.y);
                // The node's Elevation must be exactly (E, E) (the ladder rule, LiftTowerEdit): an engine-derived value (the tool's
                // fresh nodes, CourseSplitSystem.CalculateElevation; a one-sided (7.93, 0)) is rewritten once.
                bool elevOff = ElevationOff(path[i].Node, elevation);
                if (elevOff && !moved && !reMembered && !drift)
                    np.y = pts[i].y;   // only the component is off: keep the node where it stands (no carve chase, T115)
                Target t = new Target { Node = path[i].Node, Old = pts[i], New = np, OldMember = oldMembers[i - 1], NewMember = pn.Member,
                                        Elevation = elevation, Write = moved || reMembered || drift || elevOff };
                if (t.Write)
                    writes++;
                targets[i - 1] = t;
            }

            string cutNote = cutAt.Count > 0
                ? $"; own cut: ground at {cutAt.Count} sample spot(s) taken from beside this line's own trench (T127)"
                : string.Empty;
            string clearText = plan.Clear ? "every span clear"
                : fit.Mode == LiftFitMode.Kept ? $"within {LiftTowerPlanner.KeepTolM:F1} m of the floor, kept as it stands"
                : "VIOLATED";
            if (writes == 0)
            {
                Mod.Log.Info($"Towers [fit]: {head} — already fitted ({fit.Mode}; {plan.LineTowers} line tower(s) {{{plan.MemberCounts()}}}; " +
                             $"min floor margin {Sg(plan.MinFloorMarginM)} m; {clearText}{cutNote}): nothing written");
                return;
            }
            Entity guardKey = path[1].Node;
            if (m_Writes.TryGetValue(guardKey, out int2 w) && m_Frame - w.y < GuardFrames && w.x >= MaxWrites)
            {
                Mod.Log.Warn($"Towers [fit]: {head} — written {w.x} times in {m_Frame - w.y} updates without settling: left as it is " +
                             "(fit loop guard; please send the log)");
                return;
            }
            m_Writes[guardKey] = m_Writes.TryGetValue(guardKey, out w) && m_Frame - w.y < GuardFrames ? new int2(w.x + 1, w.y) : new int2(1, m_Frame);

            // Log first (the write cannot fail silently after it), then write.
            var sb = new StringBuilder(2048);
            sb.Append("Towers [fit]: ").Append(head).Append(" — ").Append(fit.Mode).Append(": ").Append(n - 2).Append(" line tower(s), ")
              .Append(F(length, "F0")).Append(" m xz, ").Append(straight ? "straight" : "bent (" + F(offLine, "F1") + " m off the end-to-end line; heights only)")
              .Append("; engine max span ").Append(F(engineMax, "F0")).Append(" m; station ropes y ").Append(F(ropeA, "F2"))
              .Append(laneA ? " (rope lane)" : " (node + " + F(c.RopeAboveBase(0), "F2") + ")").Append(" / ").Append(F(ropeB, "F2"))
              .Append(laneB ? " (rope lane)" : " (node + " + F(c.RopeAboveBase(0), "F2") + ")").Append("; rules floor ").Append(F(c.FloorM, "F1"))
              .Append(" + drop ").Append(F(c.CarrierDropM, "F1")).Append(" over the high side (±").Append(F(c.HalfWidthM, "F1"))
              .Append(" m), max rope ").Append(c.MaxRopeAboveGroundM > 0f ? F(c.MaxRopeAboveGroundM, "F0") : "none")
              .Append(toolLine != null ? $"; TOOL LINE (lift-line tool, {toolPins} pinned): heights only with the tool's members, never moved{splitNote}" : string.Empty)
              .Append(cutNote).Append('\n');
            for (int i = 0; i < n; i++)
            {
                LiftPlanNode pn = plan.Nodes[i];
                if (i == 0 || i == n - 1)
                {
                    sb.Append("  t").Append(i + 1).Append(' ').Append(Fmt(path[i].Node)).Append(" at ").Append(F(pn.AlongM, "F0"))
                      .Append(" m: station end, rope y ").Append(F(pn.RopeM, "F2")).Append('\n');
                }
                else
                {
                    Target t = targets[i - 1];
                    sb.Append("  t").Append(i + 1).Append(' ').Append(Fmt(t.Node)).Append(" at ").Append(F(before.Nodes[i].AlongM, "F0"))
                      .Append(" -> ").Append(F(pn.AlongM, "F0")).Append(" m: ").Append(Short(c.Ladder[t.OldMember].Name)).Append(" -> ")
                      .Append(Short(c.Ladder[t.NewMember].Name)).Append(", elevation ").Append(F(LiftTowerEdit.ElevationM(c, t.NewMember), "F2"))
                      .Append(", node y ").Append(F(t.Old.y, "F2")).Append(" -> ").Append(F(t.New.y, "F2")).Append(" (ground ").Append(F(pn.GroundM, "F2"))
                      .Append(", high side ").Append(Sg(pn.HighSideM - pn.GroundM)).Append("), rope y ").Append(F(pn.RopeM, "F2"))
                      .Append(" [").Append(LiftTowerPlan.FlagText(pn.Flags)).Append(']').Append(t.Write ? string.Empty : " (unchanged)").Append('\n');
                }
                if (i < n - 1)
                {
                    LiftPlanSpan s = plan.Spans[i], s0 = before.Spans[i];
                    sb.Append("      span -> t").Append(i + 2).Append(": ").Append(F(s.LengthM, "F0")).Append(" m, lowest carrier over the high side ")
                      .Append(Sg(s0.MinChairAboveGroundM)).Append(" -> ").Append(Sg(s.MinChairAboveGroundM)).Append(" m at ")
                      .Append(F(s.MinFloorMarginAtM, "F0")).Append(" m, rope max ").Append(Sg(s.MaxRopeAboveM)).Append(" above ground -> ")
                      .Append(s.FloorViolated ? "FLOOR VIOLATED" : s.HeightViolated ? "MAX HEIGHT VIOLATED" : "clear").Append('\n');
                }
            }
            int violatedBefore = 0;
            for (int s = 0; s < before.Spans.Length; s++)
                if (before.Spans[s].FloorViolated || before.Spans[s].HeightViolated)
                    violatedBefore++;
            sb.Append("Towers [fit summary]: ").Append(head).Append(": ").Append(fit.Mode).Append("; min floor margin ")
              .Append(Sg(before.MinFloorMarginM)).Append(" m (").Append(violatedBefore).Append(" span(s) violated) -> ")
              .Append(Sg(plan.MinFloorMarginM)).Append(" m (").Append(clearText).Append("); members {")
              .Append(before.MemberCounts()).Append("} -> {").Append(plan.MemberCounts()).Append("}; free plan ").Append(fit.Needed)
              .Append(" line tower(s); writing ").Append(writes).Append(" node(s)");
            if (fit.Mode == LiftFitMode.Short)
                Mod.Log.Warn(sb.ToString() + $"\nTowers [fit]: {head} — no clear arrangement with {n - 2} line tower(s) (the planner needs " +
                             $"{fit.Needed}{(straight ? string.Empty : " and a straight line to move them")}): the best one is built. Click points " +
                             "along the line while drawing (every click is a node) or redraw it in shorter sections.");
            else
                Mod.Log.Info(sb.ToString());
            if (fit.Mode == LiftFitMode.Short)
                m_Short++;

            Write(path, targets, netGeo.m_Hanging);
            m_Written++;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Writes
        // ════════════════════════════════════════════════════════════════════════════════════════

        private void Write(List<LiftTowerDiagSystem.Hop> path, Target[] targets, float hanging)
        {
            var touched = new HashSet<Entity>();
            for (int i = 0; i < targets.Length; i++)
            {
                Target t = targets[i];
                if (!t.Write)
                    continue;
                touched.Add(t.Node);
                Game.Net.Node node = EntityManager.GetComponentData<Game.Net.Node>(t.Node);
                node.m_Position = t.New;
                EntityManager.SetComponentData(t.Node, node);
                float e = t.Elevation;
                if (e > 0f)
                {
                    var elev = new Game.Net.Elevation(new float2(e, e));
                    if (EntityManager.HasComponent<Game.Net.Elevation>(t.Node))
                        EntityManager.SetComponentData(t.Node, elev);
                    else
                        EntityManager.AddComponentData(t.Node, elev);
                }
                else if (EntityManager.HasComponent<Game.Net.Elevation>(t.Node))
                {
                    EntityManager.RemoveComponent<Game.Net.Elevation>(t.Node);
                }
                EntityManager.AddComponent<Updated>(t.Node);
            }
            // Every edge of the path with a written end: the engine's power-line curve between the (new) node points.
            for (int i = 0; i < path.Count - 1; i++)
            {
                Entity edgeE = path[i].NextEdge;
                if (edgeE == Entity.Null || !EntityManager.TryGetComponent<Game.Net.Edge>(edgeE, out var edge) ||
                    !(touched.Contains(edge.m_Start) || touched.Contains(edge.m_End)))
                    continue;
                float3 a = EntityManager.GetComponentData<Game.Net.Node>(edge.m_Start).m_Position;
                float3 d = EntityManager.GetComponentData<Game.Net.Node>(edge.m_End).m_Position;
                LiftTowerEdit.HangingCurve(a, d, hanging, out float3 b, out float3 c);
                var curve = new Game.Net.Curve { m_Bezier = new Bezier4x3(a, b, c, d) };
                curve.m_Length = MathUtils.Length(curve.m_Bezier);
                EntityManager.SetComponentData(edgeE, curve);
                EntityManager.AddComponent<Updated>(edgeE);
                // Both end nodes, as GroundHeightSystem does for a re-curved edge (:678-680): the station stub node's geometry
                // follows the edge.
                if (!touched.Contains(edge.m_Start))
                    EntityManager.AddComponent<Updated>(edge.m_Start);
                if (!touched.Contains(edge.m_End))
                    EntityManager.AddComponent<Updated>(edge.m_End);
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>True when every inner node lies within StraightTolM (xz) of the first-to-last segment: then sliding a node along
        /// the polyline keeps the line's shape. <paramref name="worst"/> = the largest offset.</summary>
        private static bool Straight(float3[] pts, out float worst)
        {
            worst = 0f;
            float2 a = pts[0].xz, b = pts[pts.Length - 1].xz;
            float2 ab = b - a;
            float len = math.length(ab);
            if (len < 1e-3f)
                return false;
            float2 dir = ab / len;
            for (int i = 1; i < pts.Length - 1; i++)
            {
                float2 ap = pts[i].xz - a;
                worst = math.max(worst, math.abs(ap.x * dir.y - ap.y * dir.x));
            }
            return worst <= StraightTolM;
        }

        /// <summary>The rope height at a station end: the lowest end point (at the station) of the HV rope lanes of the end edge — the
        /// lane LaneSystem anchored to the station (FindAnchor, LaneSystem.cs:4116-4121); <paramref name="fallback"/> (node y + the
        /// shortest tower's rope) when the edge has none. The HV test is CableLanePrefabs' (UtilityTypes.HighVoltageLine = 0x40: every
        /// carrier rope of the four lines; the tram's LV haul rope carries no cabin).</summary>
        private float StationRope(Entity endNode, Entity edge, float3 nodePos, float fallback, out bool fromLane)
        {
            fromLane = false;
            if (edge == Entity.Null || !EntityManager.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return fallback;
            float best = float.PositiveInfinity;
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (!EntityManager.HasComponent<Game.Net.UtilityLane>(lane) || !EntityManager.TryGetComponent<PrefabRef>(lane, out var lp) ||
                    !CableLanePrefabs.IsHighVoltage(EntityManager, lp.m_Prefab) ||
                    !EntityManager.TryGetComponent<Game.Net.Curve>(lane, out var lc))
                    continue;
                float3 p = math.distancesq(lc.m_Bezier.a.xz, nodePos.xz) <= math.distancesq(lc.m_Bezier.d.xz, nodePos.xz) ? lc.m_Bezier.a : lc.m_Bezier.d;
                if (math.distance(p.xz, nodePos.xz) > 30f)
                    continue;   // not this end
                best = math.min(best, p.y);
            }
            if (float.IsPositiveInfinity(best))
                return fallback;
            fromLane = true;
            return best;
        }

        /// <summary>T127: per path span, the offset of this line's own cut ceiling above the edge curve: the edge composition's
        /// m_HeightRange.min (NetCompositionData) + its TerrainComposition m_MinHeightOffset (middle), the sum TerrainSystem.AddSegment
        /// uses (TerrainSystem.cs:1397-1516, terrain-carving.md §2); the prefab's default range and the pieces' -2 when the edge has
        /// no composition yet.</summary>
        private float[] CutOffsets(List<LiftTowerDiagSystem.Hop> path, NetGeometryData netGeo)
        {
            var off = new float[math.max(1, path.Count - 1)];
            for (int i = 0; i < path.Count - 1; i++)
            {
                float min = netGeo.m_DefaultHeightRange.min;
                float terr = LiftTowerEdit.PieceMinHeightOffsetM;
                Entity e = path[i].NextEdge;
                if (e != Entity.Null && EntityManager.TryGetComponent<Game.Net.Composition>(e, out var comp) &&
                    EntityManager.TryGetComponent<NetCompositionData>(comp.m_Edge, out var ncd))
                {
                    min = ncd.m_HeightRange.min;
                    if (EntityManager.TryGetComponent<TerrainComposition>(comp.m_Edge, out var tc))
                        terr = tc.m_MinHeightOffset.y;
                }
                off[i] = min + terr;
            }
            return off;
        }

        /// <summary>True when the node's Game.Net.Elevation is not exactly (E, E) for E = <paramref name="e"/> (0 = no component or
        /// (0, 0)), within 0.05 m.</summary>
        private bool ElevationOff(Entity node, float e)
        {
            bool has = EntityManager.TryGetComponent<Game.Net.Elevation>(node, out var el);
            if (e <= 0f)
                return has && math.any(math.abs(el.m_Elevation) > 0.05f);
            return !has || math.any(math.abs(el.m_Elevation - e) > 0.05f);
        }

        /// <summary>The ladder member whose elevation (TopM - TopM[0]) matches <paramref name="elevationM"/> (0.05 m), else the
        /// tallest member not above it (a node the engine elevated by itself, e.g. a node drawn above the ground).</summary>
        private static int MemberOf(LiftTowerPlanConstants c, float elevationM)
        {
            int best = 0;
            for (int m = 0; m < c.Ladder.Length; m++)
                if (LiftTowerEdit.ElevationM(c, m) <= elevationM + 0.05f)
                    best = m;
            return best;
        }

        private static string Short(string member) => member.Replace("SkiLiftTower", string.Empty).Replace("SkiTramTower", "T");
        private static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
        private static string Sg(float v) => (v >= 0f ? "+" : string.Empty) + v.ToString("F1", CultureInfo.InvariantCulture);
        private static string Fmt(Entity e) => e.Index + ":" + e.Version;
    }
}
