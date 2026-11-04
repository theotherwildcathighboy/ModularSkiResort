// Fits every new or changed lift line so its carriers keep the floor over the ground. The planner is the pure LiftTowerPlanner
// (Fit); this system reads the line, asks, writes the answer back and logs. Per inner node it sets a position along the line (slid
// only when heights cannot clear a straight line), a height (node y = center ground + E, Elevation = (E, E), E = TopM[member] -
// TopM[0]) and a straight Curve on each touched edge. Station stub, branch and owned nodes are never touched.
// Phase (Mod.cs): UpdateAt Modification1: every consumer of the Updated tag runs later in the frame, CleanUp wipes it at frame end.
// Triggered by LiftCensusSystem.OnCensusBuilt after a quiet period; a fixed point, with a write guard. Tool lines (LiftToolLines)
// are fitted heights-only. Logs: silence is impossible; an exception disables the system until the next load.
// Engine notes: EngineNotes/LiftTowerFitSystem.md

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
        // Height writes with the member unchanged: LiftTowerEdit.HeightWrite (one-sided: below 0.25 m / above 1.0 m)
        private const float StraightTolM = 0.5f;   // inner node off the end-to-end line: the line is bent, heights only

        private LiftTowerDiagSystem m_Diag;
        private TerrainSystem m_TerrainSystem;
        private LiftLineToolSystem m_LineTool;
        private PrefabSystem m_PrefabSystem;
        private LiftCensusSystem m_Census;
        private readonly LiftTowerPlanner m_Planner = new LiftTowerPlanner();

        private bool m_Armed, m_Disabled, m_ErrorLogged, m_OffLogged;
        private int m_Frame, m_LastQueueFrame;
        private readonly HashSet<uint> m_Seen = new HashSet<uint>();
        private readonly HashSet<EntityRef> m_StationNodes = new HashSet<EntityRef>();   // the last census build's station connection nodes
        private readonly List<List<LiftCensus.Edge>> m_Pending = new List<List<LiftCensus.Edge>>();
        private readonly Dictionary<Entity, int2> m_Writes = new Dictionary<Entity, int2>();   // first inner node -> (count, first frame)
        // Each line tower's train kind as last classified (breakover-holddown-plan-2026-11-03, phase B1: computed and logged only, no
        // head change): the hysteresis input (LiftAlignMath.KindHystDeg) and the "Towers [angles]" change detector. In session only,
        // never saved; cleared per load.
        private readonly Dictionary<Entity, LiftTrainKind> m_Kinds = new Dictionary<Entity, LiftTrainKind>();
        private int m_Fitted, m_Written, m_Short;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Diag = World.GetOrCreateSystemManaged<LiftTowerDiagSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_LineTool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
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
            m_Kinds.Clear();
            m_Fitted = m_Written = m_Short = 0;
            LoadTwins();
            if (m_Armed)
                Mod.Log.Info($"Towers [fit]: ARMED — fits new / changed lift lines {SettleFrames} quiet census updates after the edit " +
                             $"(heights by node elevation, positions along straight lines, the engine's node count); setting Plan lift towers = " +
                             $"{(Mod.Settings?.LiftTowerPlanner ?? true ? "on" : "OFF")}");
            else
                Mod.Log.Info($"Towers [fit]: NOT ARMED (mode {mode})");
        }

        /// <summary>The twin heads the planner may place this load (LiftTowerPlanConstants.TwinFilter): the twin prefabs the game has
        /// loaded. A build without them plans support heads only, so no node is raised by a twin's E with the support member spawned
        /// above the ground. One log line per load.</summary>
        private void LoadTwins()
        {
            var found = new HashSet<string>();
            var missing = new List<string>();
            int all = 0;
            foreach (LiftTowerTwin t in LiftTowerPlanConstants.AllTwins())
            {
                all++;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), t.Name), out PrefabBase p) && p != null)
                    found.Add(t.Name);
                else
                    missing.Add(t.Name);
            }
            LiftTowerPlanConstants.TwinFilter = found.Contains;
            if (missing.Count == 0)
                Mod.Log.Info($"Towers [twins]: {found.Count} of {all} twin heads loaded (hold-down / push-pull / breakover): the planner places them");
            else
                Mod.Log.Warn($"Towers [twins]: {found.Count} of {all} twin heads loaded; missing {string.Join(", ", missing)}: those towers keep " +
                             "their support head");
        }

        /// <summary>Called by LiftCensusSystem after each build with its line edges and nodes (main thread, no ECS read here). The key
        /// of a line is its edges and node positions plus which of its nodes are station connections (LiftCensus.Node.Station): a
        /// station attached line-first to a built line changes no edge or node, but changes the line's ends, so it is re-fitted in
        /// session as after a load (FIX-LIST 52).</summary>
        internal void OnCensusBuilt(List<LiftCensus.Edge> edges, List<LiftCensus.Node> nodes)
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                m_StationNodes.Clear();
                for (int i = 0; i < nodes.Count; i++)
                    if (nodes[i].Station >= 0)
                        m_StationNodes.Add(nodes[i].Id);
                List<List<LiftCensus.Edge>> comps = LiftTowerDiagSystem.Components(new List<LiftCensus.Edge>(edges));
                for (int c = 0; c < comps.Count; c++)
                {
                    if (m_Seen.Add(LiftTowerDiagSystem.Signature(comps[c]) ^ StationKey(comps[c])))
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

        /// <summary>The station ends of a line, hashed (0 = none): the node index of every station connection node it touches.</summary>
        private uint StationKey(List<LiftCensus.Edge> comp)
        {
            uint k = 0;
            for (int i = 0; i < comp.Count; i++)
            {
                if (m_StationNodes.Contains(comp[i].Start))
                    k += ((uint)comp[i].Start.Index + 1u) * 2246822519u;
                if (m_StationNodes.Contains(comp[i].End))
                    k += ((uint)comp[i].End.Index + 1u) * 3266489917u;
            }
            return k;
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


        private struct Target
        {
            public Entity Node;
            public float3 Old, New;
            public int OldMember, NewMember;
            public LiftTowerHead OldHead, NewHead;
            public float Elevation;   // LiftTowerEdit.ElevationM(NewMember, NewHead): node y - center ground, written as Elevation (E, E)
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
            // Inner nodes must be plain nodes of this line: the line prefab, no Owner (a station's sub-net), exactly two edges
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
            // The ground the player made: a sample at this line's own cut ceiling (edge y + height range min + terrain offset,
            // LiftTowerEdit) is our trench, not the ground; the plane through the uncut ground beside it stands in.
            var cutAt = new HashSet<int>();
            LiftTerrainSampler sampler = LiftTowerEdit.UncutSampler(raw, pts, cum, netGeo.m_Hanging, CutOffsets(path, netGeo),
                c.HalfWidthM + LiftTowerEdit.CutFarMarginM, along => cutAt.Add((int)math.round(along)));

            float ropeA = StationRope(path[0].Node, path[0].NextEdge, pts[0],
                pts[0].y + LiftTowerDiagSystem.RopeAboveNode(EntityManager, path[0].Node, c), out bool laneA);
            float ropeB = StationRope(path[n - 1].Node, path[n - 2].NextEdge, pts[n - 1],
                pts[n - 1].y + LiftTowerDiagSystem.RopeAboveNode(EntityManager, path[n - 1].Node, c), out bool laneB);

            var inner = new float[n - 2];
            var oldMembers = new int[n - 2];
            var oldHeads = new LiftTowerHead[n - 2];
            var oldKinds = new LiftTrainKind[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                inner[i - 1] = cum[i];
                float e = EntityManager.TryGetComponent<Game.Net.Elevation>(path[i].Node, out var el) ? math.cmin(el.m_Elevation) : 0f;
                oldMembers[i - 1] = LiftTowerEdit.MemberOf(c, e, out oldHeads[i - 1]);
                // the hysteresis input: the kind last classified this session, else the one the node's head implies (planner Previous)
                oldKinds[i - 1] = m_Kinds.TryGetValue(path[i].Node, out LiftTrainKind k) ? k : LiftTrainKind.None;
            }
            float engineMax = netGeo.m_EdgeLengthRange.max;
            // A line the lift-line tool built (every inner node a registered tool node, LiftToolLines.Match) is a tool line: heights only
            // with the tool's members, never Moved, so a pinned node is never snapped.
            var innerPts = new List<float3>(n - 2);
            for (int i = 1; i < n - 1; i++)
                innerPts.Add(pts[i]);
            LiftToolLines.Line toolLine = m_LineTool.ToolLines.Match(innerPts, out int[] toolMembers, out _);
            // The engine built the tool's courses as MORE edges (CourseSplitSystem elevation-transition splits, CourseSplitSystem.cs:2884-2942).
            // Still a tool line when every emitted node has its own path node within 0.5 m: the extras get the member that keeps the
            // rope on the planned chord, and nothing is moved.
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
            // A station end gets the approach floor; a loose end or plain node is a tower: the full floor. Every line edge has HV rope
            // lanes ending at both its nodes, so a rope lane (laneA / laneB) says only where the rope height came from.
            // The census rule (LiftCensusSystem.StationOfNode), so a line-first station (its stub course reuses the line's UNOWNED end
            // node) is a station end in session as after a load (FIX-LIST 52: Owner alone read a ground end in session, a station after it).
            m_Planner.StationA = IsStationEnd(path[0].Node);
            m_Planner.StationB = IsStationEnd(path[n - 1].Node);
            // Kinds and heads with hysteresis: every plan at the line's own node positions starts from the kinds and heads it carries.
            m_Planner.PreviousKinds = oldKinds;
            m_Planner.PreviousHeads = oldHeads;
            LiftTowerPlan before;
            LiftTowerFit fit;
            try
            {
                m_Planner.HeadsAsGiven = true;   // the line as it stands: its own heads
                before = m_Planner.PlanFixed(sampler, length, ropeA, ropeB, c, inner, oldMembers, null, assign: false);
                m_Planner.HeadsAsGiven = false;
                // The line's own members are passed in, so a line that clears as it stands is Kept unchanged (hysteresis)
                fit = m_Planner.Fit(sampler, length, ropeA, ropeB, c, inner, straight && toolLine == null, engineMax,
                                    toolLine != null ? toolMembers : oldMembers, split?.Extra);
            }
            finally
            {
                m_Planner.HeadsAsGiven = false;
                m_Planner.PreviousKinds = null;
                m_Planner.PreviousHeads = null;
            }
            LiftTowerPlan plan = fit.Plan;
            m_Fitted++;
            // A pinned tower must never move silently
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
            // Never a worse plan over a built line (FIX-LIST 52): a line whose every inner node carries a fit's exact elevation is the
            // line as built (before = its own members and heads, read back by MemberOf); an unsound fit worse on any axis is not written.
            bool built = Built(path, c, oldMembers, oldHeads);
            if (LiftTowerPlanner.ToWrite(before, fit, built, out string worse) == null)
            {
                before.KindCounts(out _, out _, out _, out _, out int overB);
                plan.KindCounts(out _, out _, out _, out _, out int overP);
                Mod.Log.Warn($"Towers [fit]: {head} — KEPT AS BUILT: the line as it stands has min floor margin {Sg(before.MinFloorMarginM)} m " +
                             $"({before.ViolatedSpans} span(s) violated, {overB} tower(s) over cap; station ends {(m_Planner.StationA ? "base" : "-")}/" +
                             $"{(m_Planner.StationB ? "top" : "-")}, ropes y {F(ropeA, "F2")} / {F(ropeB, "F2")}); the best fit ({fit.Mode}: " +
                             $"{Sg(plan.MinFloorMarginM)} m, {overP} over cap, members {{{plan.MemberCounts()}}}, twin heads {{{plan.HeadCounts()}}}) " +
                             $"is worse on {worse}: nothing written");
                return;
            }
            LogKinds(head, path, plan, c, oldHeads);

            var targets = new Target[n - 2];
            int writes = 0;
            for (int i = 1; i < n - 1; i++)
            {
                LiftPlanNode pn = plan.Nodes[i];
                float3 xz = LiftTowerDiagSystem.PolylineAt(pts, cum, pn.AlongM, 0f, out _);
                if (!straight)
                    xz = new float3(pts[i].x, 0f, pts[i].z);   // bent line: positions are kept (the fit did not move them)
                float elevation = LiftTowerEdit.ElevationM(c, pn.Member, pn.Head);
                float3 np = new float3(xz.x, pn.GroundM + elevation + pn.FloatM, xz.z);   // + a tram tower's pedestal float (DECISIONS 600 a)
                bool moved = math.distance(np.xz, pts[i].xz) > MoveEpsM;
                bool reMembered = pn.Member != oldMembers[i - 1] || pn.Head != oldHeads[i - 1];
                bool drift = LiftTowerEdit.HeightWrite(pts[i].y, np.y);
                // The node's Elevation must be exactly (E, E) (the ladder rule, LiftTowerEdit): an engine-derived value (the tool's
                // fresh nodes, CourseSplitSystem.CalculateElevation; a one-sided (7.93, 0)) is rewritten once.
                bool elevOff = ElevationOff(path[i].Node, elevation);
                if (elevOff && !moved && !reMembered && !drift)
                    np.y = pts[i].y;   // only the component is off: keep the node where it stands (no carve chase)
                Target t = new Target { Node = path[i].Node, Old = pts[i], New = np, OldMember = oldMembers[i - 1], NewMember = pn.Member,
                                        OldHead = oldHeads[i - 1], NewHead = pn.Head, Elevation = elevation,
                                        Write = moved || reMembered || drift || elevOff };
                if (t.Write)
                    writes++;
                targets[i - 1] = t;
            }

            string cutNote = cutAt.Count > 0
                ? $"; own cut: ground at {cutAt.Count} sample spot(s) taken from beside this line's own trench (T127)"
                : string.Empty;
            string clearText = plan.Clear ? "every span clear" + plan.OverCapNote   // FIX-LIST 55: never "clear" alone with a tower over its cap
                : fit.Mode == LiftFitMode.Kept ? $"within {LiftTowerPlanner.KeepTolM:F1} m of the floor, kept as it stands" + plan.OverCapNote
                : "VIOLATED";
            if (writes == 0)
            {
                Mod.Log.Info($"Towers [fit]: {head} — already fitted ({fit.Mode}; {plan.LineTowers} line tower(s) {{{plan.MemberCounts()}}}, twin heads {{{plan.HeadCounts()}}}; " +
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
              .Append(laneA ? " (rope lane)" : " (node + " + F(ropeA - pts[0].y, "F2") + ")").Append(" / ").Append(F(ropeB, "F2"))
              .Append(laneB ? " (rope lane)" : " (node + " + F(ropeB - pts[n - 1].y, "F2") + ")").Append("; rules floor ").Append(F(c.FloorM, "F1"))
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
                      .Append(" -> ").Append(F(pn.AlongM, "F0")).Append(" m: ").Append(Short(c.NameOf(t.OldMember, t.OldHead))).Append(" -> ")
                      .Append(Short(c.NameOf(t.NewMember, t.NewHead))).Append(", elevation ").Append(F(t.Elevation, "F2"))
                      .Append(", node y ").Append(F(t.Old.y, "F2")).Append(" -> ").Append(F(t.New.y, "F2")).Append(" (ground ").Append(F(pn.GroundM, "F2"))
                      .Append(", high side ").Append(Sg(pn.HighSideM - pn.GroundM)).Append("), rope y ").Append(F(pn.RopeM, "F2"))
                      .Append(" [").Append(LiftTowerPlan.FlagText(pn.Flags)).Append(']').Append(t.Write ? string.Empty : " (unchanged)")
                      .Append(", ").Append(plan.AngleText(i)).Append('\n');
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
              .Append(before.MemberCounts()).Append("} -> {").Append(plan.MemberCounts()).Append("}, twin heads {").Append(plan.HeadCounts())
              .Append("}; free plan ").Append(fit.Needed)
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


        /// <summary>The line towers' train kinds and heads (support / hold-down / push-pull from the two load cases with the kind each had
        /// before as the hysteresis input, LiftTowerPlanner.PreviousKinds; the head the planner placed for it, LiftTowerPlanner.AssignHeads):
        /// one "Towers [angles]" line when any kind or head is new or changed. The heads are written with the node heights (E of the twin).
        /// The tram's saddles get their deflections and the bare-rope breakover check (14.4.4), logged only.</summary>
        private void LogKinds(string head, List<LiftTowerDiagSystem.Hop> path, LiftTowerPlan plan, LiftTowerPlanConstants c,
                              LiftTowerHead[] oldHeads)
        {
            int n = path.Count;
            int changed = 0, s = 0, h = 0, p = 0, over = 0, under = 0, twins = 0;
            var sb = new StringBuilder(512);
            for (int i = 1; i < n - 1; i++)
            {
                LiftPlanNode pn = plan.Nodes[i];
                Entity node = path[i].Node;
                bool had = m_Kinds.TryGetValue(node, out LiftTrainKind before);
                LiftTrainKind kind = pn.HasAngles && c.HasTrains ? pn.Kind : LiftTrainKind.None;
                LiftTowerHead was = oldHeads[i - 1];
                if (!had || kind != before || pn.Head != was)
                    changed++;
                m_Kinds[node] = kind;
                if (kind == LiftTrainKind.Support) s++;
                else if (kind == LiftTrainKind.HoldDown) h++;
                else if (kind == LiftTrainKind.PushPull) p++;
                if (pn.Head != LiftTowerHead.Line) twins++;
                if (pn.HasAngles && math.abs(pn.DeflectionDeg) > pn.CapDeg + LiftTowerPlan.AngleTolDeg) over++;   // the tram: its saddle cap (600 a)
                if (pn.HasAngles && !c.HasTrains && pn.BareDeg < LiftAlignMath.SaddleMinBreakDeg) under++;
                sb.Append(i == 1 ? string.Empty : ", ").Append('t').Append(i + 1).Append(' ').Append(F(pn.AlongM, "F0")).Append(" m ")
                  .Append(Short(c.NameOf(pn.Member, pn.Head))).Append(" d ").Append(Sg(pn.DeflectionDeg));
                if (c.HasTrains)
                    sb.Append(" (").Append(Sg(pn.TightDeg)).Append(" / ").Append(Sg(pn.HeavyDeg)).Append(") ").Append(LiftAlignMath.KindLetter(kind))
                      .Append(had && kind != before ? " (was " + LiftAlignMath.KindLetter(before) + ")" : string.Empty)
                      .Append(pn.Head != was ? " head " + was + " -> " + pn.Head : string.Empty);
                else
                    sb.Append(" (bare ").Append(Sg(pn.BareDeg)).Append(')');
            }
            if (changed == 0)
                return;
            string counts = c.HasTrains
                ? $"support {s}, hold-down {h}, push-pull {p}, twin heads {twins} ({c.Twins.Length} twin(s) loaded for {c.TypeId}), over cap {over}"
                : $"tram saddles: over the saddle cap ({2f * c.SaddleHalfCapDeg:F0} deg bend) {over}, bare-rope breakover under {LiftAlignMath.SaddleMinBreakDeg:F2} deg at {under} tower(s)";
            Mod.Log.Info($"Towers [angles]: {head} — {changed} new or changed kind(s) / head(s); {counts}; " +
                         $"d = in - out (tight / heavy load case), S / H / P: {sb}");
            for (int i = 0; i < plan.AngleNotes.Count; i++)
                Mod.Log.Info($"Towers [angles]: {head} — {plan.AngleNotes[i]}");
        }

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

        /// <summary>Per path span, the offset of this line's own cut ceiling above the edge curve: the edge composition's
        /// m_HeightRange.min (NetCompositionData) + its TerrainComposition m_MinHeightOffset (middle), the sum TerrainSystem.AddSegment
        /// uses (TerrainSystem.cs:1397-1516); the prefab's default range and the pieces' -2 when the edge has no composition yet.</summary>
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

        /// <summary>The line as a fit built it (FIX-LIST 52): every inner node's Game.Net.Elevation is exactly (E, E) of the member and
        /// head MemberOf reads from it, and the evidence is not all zero (a node raised to a member or twin, or every node classified
        /// this session): an engine-drawn line on the ground (no Elevation) or the tool's fresh nodes (engine elevations such as
        /// (7.93, 0)) are not built and always take the fit.</summary>
        private bool Built(List<LiftTowerDiagSystem.Hop> path, LiftTowerPlanConstants c, int[] members, LiftTowerHead[] heads)
        {
            bool raised = false, known = true;
            for (int i = 1; i < path.Count - 1; i++)
            {
                float e = LiftTowerEdit.ElevationM(c, members[i - 1], heads[i - 1]);
                if (ElevationOff(path[i].Node, e))
                    return false;
                raised |= e > 0f;
                known &= m_Kinds.ContainsKey(path[i].Node);
            }
            return raised || known;
        }

        /// <summary>A station's connection node by the census rule (LiftCensusSystem.IsStationNode: stations-first the station-owned
        /// stub node, line-first the line's own node with the station's stub edge on it), else any owned node.</summary>
        private bool IsStationEnd(Entity node) =>
            (m_Census != null && m_Census.IsStationNode(node)) || EntityManager.HasComponent<Owner>(node);

        /// <summary>True when the node's Game.Net.Elevation is not exactly (E, E) for E = <paramref name="e"/> (0 = no component or
        /// (0, 0)), within 0.05 m.</summary>
        private bool ElevationOff(Entity node, float e)
        {
            bool has = EntityManager.TryGetComponent<Game.Net.Elevation>(node, out var el);
            if (e <= 0f)
                return has && math.any(math.abs(el.m_Elevation) > 0.05f);
            return !has || math.any(math.abs(el.m_Elevation - e) > 0.05f);
        }

        private static string Short(string member) => member.Replace("SkiLiftTower", string.Empty).Replace("SkiTramTower", "T");
        private static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
        private static string Sg(float v) => (v >= 0f ? "+" : string.Empty) + v.ToString("F1", CultureInfo.InvariantCulture);
        private static string Fmt(Entity e) => e.Index + ":" + e.Version;
    }
}
