// Mast lean (DECISIONS 600; spike tilted-towers-spike-2026-11-04 option A, section 1.2): a line tower whose rope pitch is past what its
// head (or, on the tram, its saddle) follows leans its whole mast downhill about its foot, up to LiftAlignMath.MaxLeanDegOf (DQ / GD 20 deg, FG 25,
// tram 25). The lean is LiftAlignMath.LeanDeg (THE lean rule, one switch) of the tower's rope pitch, LiftAlignMath.ChordPitchDeg over its
// two neighbors' plumb rope points (the planner's own measure, so plan and game agree and the lean never feeds back into itself).
//   Where the pose comes from: SubObjectSystem (Modification2B) and AlignSystem (Modification4) re-derive a node tower's Transform as
//     yaw-only on every Updated (Game.Objects/SubObjectSystem.cs:1032-1034, :1757-1758; Game.Objects/AlignSystem.cs:153-160 writes it
//     when it differs), and nothing else writes it (spike section 1.1). This system runs right after AlignSystem, before LaneSystem
//     (Game.Common/SystemOrder.cs:153, :155), so a re-aligned tower is leaned again in the same frame (no plumb frame) and LaneSystem
//     hangs the ropes from the leaned anchors (U1 probe, DECISIONS 570 c: a rotation written after AlignSystem holds, ropes re-anchor).
//   The pivot is the tower's Transform position (the prefab origin, the foot): only the rotation changes. The tower's own sub-objects
//     (the GD beacon row, number tiles) are carried like AlignSystem.MoveObject does (:167-180).
//   A tower leaned or restored without a vanilla update gets Updated (search tree, culling, raycast follow the Transform; Objects
//     SearchSystem Modification5, PreCullingSystem.cs:718-723), and so do its node's lift edges, so LaneSystem rebuilds their ropes on
//     the moved anchors this frame. UNVERIFIED in game: that an Updated added here (after AlignSystem, before LaneSystem) is enough for
//     LaneSystem to re-anchor the edge's rope lanes; that the Standing leg box follows the rotation (nothing on a line collides).
// Padded face uphill (DECISIONS 600 e): every tower mesh carries its crash pad and TrainFront half on local +z, its ladder and the Hold /
//   Combo deck end on -z, but vanilla yaws a node tower along the draw direction (Game.Net/NodeAlignSystem.cs:85-111, :209-212). This
//   system turns each line tower, and each ground-end tower, by 0 or 180 deg about vertical so +z points toward its section's higher rope
//   end (LiftAlignMath.SectionUphill / PadUphillYaw; one call per section, so all its towers face the same way; a section whose ends differ
//   by less than PadFlatRiseM keeps vanilla's yaw), then leans it. The ropes do not move: every tower's anchor set is symmetric under the
//   turn and LaneSystem matches anchors by position (EngineNotes). An edit re-checks the whole section the edge lies on.
// Triggers: both end nodes of every Updated lift edge, the same nodes once more on the next frame (a station neighbor's rope point is
// read from its rope lane, which LaneSystem rebuilds after this system), and every lift node once after a load (the Transform is saved,
// so a rule change takes effect there). Event-driven: 0 work when no lift edge is updated.
// Never: Temp or Deleted towers (a preview stays plumb), stations, junctions, Editor; a ground end (one lift edge, no Owner) is turned,
// never leaned.
// Log discipline: one line per frame that leaned or restored a tower (with the steepest lean), a one-shot "!!!" catch.
// Mod.cs: UpdateAfter<TowerLeanSystem, Game.Objects.AlignSystem>(Modification4).
// Engine notes: EngineNotes/TowerLeanSystem.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using SubLane = Game.Net.SubLane;
using SubObject = Game.Objects.SubObject;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class TowerLeanSystem : GameSystemBase
    {
        /// <summary>A rotation within this (quaternion dot, about 0.03 deg) of the target is left alone.</summary>
        private const float SameRotationDot = 0.99999996f;

        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private EntityQuery m_UpdatedEdges;
        private EntityQuery m_AllEdges;
        private bool m_Armed, m_Disabled, m_Full;
        private readonly HashSet<Entity> m_Nodes = new HashSet<Entity>();
        private readonly HashSet<Entity> m_Fresh = new HashSet<Entity>();
        private HashSet<Entity> m_Again = new HashSet<Entity>();
        private readonly List<(Entity Entity, Transform Transform)> m_Writes = new List<(Entity, Transform)>();
        private readonly HashSet<Entity> m_Touch = new HashSet<Entity>();
        private readonly List<Entity> m_LiftEdges = new List<Entity>(4);
        private readonly List<float3> m_Ends = new List<float3>(8);
        private int m_Leaned, m_Restored, m_Turned;

        /// <summary>A walk along one section stops after this many nodes (a guard, never reached by a real line).</summary>
        private const int MaxSectionNodes = 4096;

        private readonly Dictionary<Entity, float2> m_Uphill = new Dictionary<Entity, float2>();   // node -> its padded face's xz (0 = vanilla)
        private readonly List<Entity> m_Seeds = new List<Entity>();
        private readonly List<Entity> m_Section = new List<Entity>();
        private readonly List<Entity> m_Half = new List<Entity>();
        private readonly List<float3> m_SectionPos = new List<float3>();
        private readonly List<float2> m_SectionUp = new List<float2>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_UpdatedEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_AllEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            Mod.Log.Info($"{nameof(TowerLeanSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_Full = m_Armed;
            m_Again.Clear();
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                IReadOnlyDictionary<Entity, LiftTypeDef> lines = m_PrefabCatalog.LiftLineTypes;
                if (lines == null || lines.Count == 0)
                    return;
                m_Fresh.Clear();
                string why = m_Full ? "load" : "edge updated";
                if (m_Full)
                {
                    m_Full = false;
                    CollectNodes(m_AllEdges, lines);
                }
                else if (!m_UpdatedEdges.IsEmptyIgnoreFilter)
                    CollectNodes(m_UpdatedEdges, lines);
                if (m_Fresh.Count == 0 && m_Again.Count == 0)
                    return;
                long t0 = Stopwatch.GetTimestamp();
                m_Nodes.Clear();
                m_Nodes.UnionWith(m_Fresh);
                m_Nodes.UnionWith(m_Again);
                m_Again.Clear();
                m_Again.UnionWith(m_Fresh);
                Sections(lines);
                Pass(lines, why, t0);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! Towers [lean]: {e.GetType().Name}: {e.Message} — disabled for this load (towers keep their last pose)\n{e}");
            }
        }

        private void CollectNodes(EntityQuery query, IReadOnlyDictionary<Entity, LiftTypeDef> lines)
        {
            NativeArray<Entity> edges = query.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    if (!lines.ContainsKey(EntityManager.GetComponentData<PrefabRef>(edges[i]).m_Prefab))
                        continue;
                    Edge e = EntityManager.GetComponentData<Edge>(edges[i]);
                    m_Fresh.Add(e.m_Start);
                    m_Fresh.Add(e.m_End);
                }
            }
            finally
            {
                edges.Dispose();
            }
        }

        private void Pass(IReadOnlyDictionary<Entity, LiftTypeDef> lines, string why, long t0)
        {
            // Read everything first: the Updated adds below are structural changes, which would invalidate the buffers read here.
            m_Writes.Clear();
            m_Touch.Clear();
            int leaned = 0, restored = 0, turned = 0;
            float maxLean = 0f;
            foreach (Entity node in m_Nodes)
            {
                if (!EntityManager.Exists(node) || EntityManager.HasComponent<Deleted>(node) || EntityManager.HasComponent<Temp>(node) ||
                    !EntityManager.TryGetComponent(node, out Node nodeData))
                    continue;
                Entity tower = TowerOf(node);
                if (tower == Entity.Null || !EntityManager.TryGetComponent(tower, out Transform t))
                    continue;
                float lean = 0f, pitchAlongZ = 0f;
                bool line = LiftEdgesOf(node, lines, out string typeId);
                if (!line && !GroundEndOf(node, lines))
                    continue;   // a station, junction or shared end: never ours (vanilla re-aligns a node's tower plumb on its update)
                // The padded face (+z) uphill (600 e): the section's direction, else (flat) vanilla's node yaw.
                float2 vanilla = math.mul(nodeData.m_Rotation, new float3(0f, 0f, 1f)).xz;
                if (!m_Uphill.TryGetValue(node, out float2 uphill) || math.lengthsq(uphill) < 1e-8f)
                    uphill = vanilla;
                quaternion yaw = LiftAlignMath.PadUphillYaw(YawOf(t.m_Rotation), uphill);
                float3 forward = math.mul(yaw, new float3(0f, 0f, 1f));
                if (line && PitchAt(node, tower, t, out float pitch, out float3 prev, out float3 next))
                {
                    lean = LiftAlignMath.LeanDeg(typeId, pitch);
                    pitchAlongZ = math.dot(forward.xz, next.xz - prev.xz) >= 0f ? pitch : -pitch;
                }
                quaternion target = LiftAlignMath.LeanRotation(yaw, pitchAlongZ, lean);
                if (math.abs(math.dot(target.value, t.m_Rotation.value)) >= SameRotationDot)
                    continue;
                if (math.dot(forward.xz, vanilla) < 0f)
                    turned++;
                var moved = new Transform(t.m_Position, target);
                m_Writes.Add((tower, moved));
                CarrySubObjects(tower, t, moved);
                m_Touch.Add(tower);
                for (int i = 0; i < m_LiftEdges.Count; i++)
                    m_Touch.Add(m_LiftEdges[i]);
                if (lean > 0f)
                {
                    leaned++;
                    maxLean = math.max(maxLean, lean);
                }
                else
                    restored++;
            }
            if (m_Writes.Count == 0)
                return;
            for (int i = 0; i < m_Writes.Count; i++)
                EntityManager.SetComponentData(m_Writes[i].Entity, m_Writes[i].Transform);
            foreach (Entity e in m_Touch)
                if (!EntityManager.HasComponent<Updated>(e))
                    EntityManager.AddComponent<Updated>(e);
            m_Leaned += leaned;
            m_Restored += restored;
            m_Turned += turned;
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            Mod.Log.Info($"Towers [lean]: ({why}) {m_Nodes.Count} node(s) checked, {leaned} tower(s) leaned (max {maxLean:F1} deg), {restored} " +
                         $"plumb, {turned} of them turned 180 to face uphill, {m_Writes.Count - leaned - restored} sub-object(s) carried; " +
                         $"load totals leaned {m_Leaned}, plumb {m_Restored}, turned {m_Turned}; {ms:F2} ms");
        }

        /// <summary>The padded face's direction for every node of every section a node to check lies on (600 e), into m_Uphill, and every
        /// node of those sections into m_Nodes: the section's higher end decides for all its towers, so an edit anywhere on it re-checks
        /// them all (a few buffer reads per tower; an unchanged pose is skipped by the rotation test).</summary>
        private void Sections(IReadOnlyDictionary<Entity, LiftTypeDef> lines)
        {
            m_Uphill.Clear();
            m_Seeds.Clear();
            m_Seeds.AddRange(m_Nodes);
            for (int i = 0; i < m_Seeds.Count; i++)
            {
                Entity seed = m_Seeds[i];
                if (m_Uphill.ContainsKey(seed) || !EntityManager.Exists(seed) || EntityManager.HasComponent<Deleted>(seed) ||
                    EntityManager.HasComponent<Temp>(seed))
                    continue;
                m_Section.Clear();
                Entity endA, edgeA, endB, edgeB;
                if (LineEdges(seed, lines, out Entity e0, out Entity e1, out LiftTypeDef type))
                {
                    if (!Walk(seed, e0, type, lines, m_Half, out endA, out edgeA))
                    {
                        Flat(m_Half, seed);   // a loop: no ends, keep vanilla
                        continue;
                    }
                    m_Section.Add(endA);
                    for (int k = m_Half.Count - 1; k >= 0; k--)
                        m_Section.Add(m_Half[k]);
                    m_Section.Add(seed);
                    Walk(seed, e1, type, lines, m_Half, out endB, out edgeB);
                    m_Section.AddRange(m_Half);
                    m_Section.Add(endB);
                }
                else if (SingleLiftEdge(seed, lines, out Entity e, out type))
                {
                    endA = seed;
                    edgeA = e;
                    m_Section.Add(seed);
                    Walk(seed, e, type, lines, m_Half, out endB, out edgeB);
                    m_Section.AddRange(m_Half);
                    m_Section.Add(endB);
                }
                else
                    continue;   // a junction or station with several lift edges: its sections are seeded by their own nodes
                bool ropes = RopePoint(endA, edgeA, out float3 ra) & RopePoint(endB, edgeB, out float3 rb);
                m_SectionPos.Clear();
                for (int k = 0; k < m_Section.Count; k++)
                    m_SectionPos.Add(EntityManager.TryGetComponent(m_Section[k], out Node sn) ? sn.m_Position : float3.zero);
                LiftAlignMath.SectionUphill(m_SectionPos, ropes ? ra.y : 0f, ropes ? rb.y : 0f, m_SectionUp);
                for (int k = 0; k < m_Section.Count; k++)
                {
                    m_Uphill[m_Section[k]] = m_SectionUp[k];
                    m_Nodes.Add(m_Section[k]);
                }
            }
        }

        private void Flat(List<Entity> nodes, Entity seed)
        {
            m_Uphill[seed] = float2.zero;
            for (int k = 0; k < nodes.Count; k++)
            {
                m_Uphill[nodes[k]] = float2.zero;
                m_Nodes.Add(nodes[k]);
            }
        }

        /// <summary>From <paramref name="from"/> along <paramref name="edge"/>: the line nodes (two lift edges of <paramref name="type"/>)
        /// passed, in order, into <paramref name="into"/>, and the node the section ends at with the edge that reaches it. False when the
        /// walk comes back to <paramref name="from"/> (a closed loop).</summary>
        private bool Walk(Entity from, Entity edge, LiftTypeDef type, IReadOnlyDictionary<Entity, LiftTypeDef> lines, List<Entity> into,
            out Entity end, out Entity endEdge)
        {
            into.Clear();
            Entity cur = Far(edge, from);
            for (int guard = 0; guard < MaxSectionNodes; guard++)
            {
                if (cur == from)
                {
                    end = endEdge = Entity.Null;
                    return false;
                }
                if (!LineEdges(cur, lines, out Entity a, out Entity b, out LiftTypeDef t) || t != type)
                    break;
                into.Add(cur);
                edge = a == edge ? b : a;
                cur = Far(edge, cur);
            }
            end = cur;
            endEdge = edge;
            return true;
        }

        /// <summary>The node's two lift edges of one type (a line node), without touching m_LiftEdges.</summary>
        private bool LineEdges(Entity node, IReadOnlyDictionary<Entity, LiftTypeDef> lines, out Entity a, out Entity b, out LiftTypeDef type)
        {
            a = b = Entity.Null;
            type = null;
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
                return false;
            int n = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                Entity edge = edges[i].m_Edge;
                if (EntityManager.HasComponent<Deleted>(edge) || EntityManager.HasComponent<Temp>(edge) ||
                    !EntityManager.TryGetComponent(edge, out PrefabRef pr) || !lines.TryGetValue(pr.m_Prefab, out LiftTypeDef def))
                    continue;
                if (type != null && def != type)
                    return false;
                type = def;
                if (n == 0)
                    a = edge;
                else
                    b = edge;
                n++;
            }
            return n == 2;
        }

        /// <summary>True when the node has exactly one live edge and it is a lift edge (a line's ground end, a station stub's far end).</summary>
        private bool SingleLiftEdge(Entity node, IReadOnlyDictionary<Entity, LiftTypeDef> lines, out Entity edge, out LiftTypeDef type)
        {
            edge = Entity.Null;
            type = null;
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
                return false;
            int live = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                Entity e = edges[i].m_Edge;
                if (EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e))
                    continue;
                live++;
                if (EntityManager.TryGetComponent(e, out PrefabRef pr) && lines.TryGetValue(pr.m_Prefab, out LiftTypeDef def))
                {
                    edge = e;
                    type = def;
                }
            }
            return live == 1 && type != null;
        }

        /// <summary>A line's ground end with its own end tower (one lift edge, nothing else, not a station's node): turned to face uphill
        /// like the line towers, never leaned. Its edge into m_LiftEdges.</summary>
        private bool GroundEndOf(Entity node, IReadOnlyDictionary<Entity, LiftTypeDef> lines)
        {
            m_LiftEdges.Clear();
            if (EntityManager.HasComponent<Owner>(node) || !SingleLiftEdge(node, lines, out Entity edge, out _))
                return false;
            m_LiftEdges.Add(edge);
            return true;
        }

        /// <summary>The node's lift edges into m_LiftEdges; true when there are exactly two of one lift type (a line tower; a line end,
        /// a station or a junction is never leaned).</summary>
        private bool LiftEdgesOf(Entity node, IReadOnlyDictionary<Entity, LiftTypeDef> lines, out string typeId)
        {
            typeId = null;
            m_LiftEdges.Clear();
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
                return false;
            LiftTypeDef type = null;
            for (int i = 0; i < edges.Length; i++)
            {
                Entity edge = edges[i].m_Edge;
                if (EntityManager.HasComponent<Deleted>(edge) || EntityManager.HasComponent<Temp>(edge) ||
                    !EntityManager.TryGetComponent(edge, out PrefabRef pr) || !lines.TryGetValue(pr.m_Prefab, out LiftTypeDef def))
                    continue;
                if (type != null && def != type)
                    return false;
                type = def;
                m_LiftEdges.Add(edge);
            }
            if (m_LiftEdges.Count != 2)
                return false;
            typeId = type.Id;
            return true;
        }

        /// <summary>The tower's rope pitch, degrees (ChordPitchDeg over the plumb rope points of the node's two lift neighbors and its
        /// own plumb anchor; + rises toward <paramref name="next"/>, the second edge's far end).</summary>
        private bool PitchAt(Entity node, Entity tower, Transform t, out float pitch, out float3 prev, out float3 next)
        {
            pitch = 0f;
            prev = next = default;
            if (!PlumbAnchor(tower, t, out float3 own))
                return false;
            Entity e0 = m_LiftEdges[0], e1 = m_LiftEdges[1];
            if (!RopePoint(Far(e0, node), e0, out prev) || !RopePoint(Far(e1, node), e1, out next))
                return false;
            float sP = -math.length(prev.xz - own.xz), sN = math.length(next.xz - own.xz);
            if (sP > -1e-3f || sN < 1e-3f)
                return false;
            pitch = LiftAlignMath.ChordPitchDeg(sP, prev.y, 0f, own.y, sN, next.y);
            return math.isfinite(pitch);
        }

        private Entity Far(Entity edge, Entity node)
        {
            Edge e = EntityManager.GetComponentData<Edge>(edge);
            return e.m_Start == node ? e.m_End : e.m_Start;
        }

        /// <summary>A neighbor node's rope point: its tower's plumb anchor, else (a station end) the rope lane ends of
        /// <paramref name="edge"/> at that node, else the node position.</summary>
        private bool RopePoint(Entity node, Entity edge, out float3 p)
        {
            p = default;
            Entity tower = TowerOf(node);
            if (tower != Entity.Null && EntityManager.TryGetComponent(tower, out Transform t) && PlumbAnchor(tower, t, out p))
                return true;
            if (!EntityManager.TryGetComponent(node, out Node n))
                return false;
            if (EntityManager.TryGetBuffer(edge, true, out DynamicBuffer<SubLane> lanes))
            {
                float3 sum = 0f;
                int count = 0;
                for (int l = 0; l < lanes.Length; l++)
                {
                    Entity lane = lanes[l].m_SubLane;
                    if (!EntityManager.TryGetComponent(lane, out PrefabRef lp) || !CableLanePrefabs.IsHighVoltage(EntityManager, lp.m_Prefab) ||
                        !EntityManager.TryGetComponent(lane, out Curve c))
                        continue;
                    float da = math.distancesq(c.m_Bezier.a.xz, n.m_Position.xz), dd = math.distancesq(c.m_Bezier.d.xz, n.m_Position.xz);
                    sum += da <= dd ? c.m_Bezier.a : c.m_Bezier.d;
                    count++;
                }
                if (count > 0)
                {
                    p = sum / count;
                    return true;
                }
            }
            p = n.m_Position;
            return true;
        }

        /// <summary>The tower's rope anchor as if plumb: the mean of its prefab's sub-lane ends (the rope anchors), placed with the
        /// tower's yaw only, at the foot's xz.</summary>
        private bool PlumbAnchor(Entity tower, Transform t, out float3 anchor)
        {
            anchor = default;
            if (!EntityManager.TryGetComponent(tower, out PrefabRef pr) ||
                !EntityManager.TryGetBuffer(pr.m_Prefab, true, out DynamicBuffer<Game.Prefabs.SubLane> anchors) || anchors.Length == 0)
                return false;
            quaternion yaw = YawOf(t.m_Rotation);
            m_Ends.Clear();
            float y = 0f;
            for (int i = 0; i < anchors.Length; i++)
            {
                Colossal.Mathematics.Bezier4x3 w = Game.Objects.ObjectUtils.LocalToWorld(t.m_Position, yaw, anchors[i].m_Curve);
                y += w.a.y + w.d.y;
            }
            anchor = new float3(t.m_Position.x, y / (2 * anchors.Length), t.m_Position.z);
            return true;
        }

        /// <summary>The yaw part of a (possibly leaned) tower rotation: LeanRotation turns about the tower's local x, so its forward stays in
        /// the vertical plane of the yaw.</summary>
        private static quaternion YawOf(quaternion rotation)
        {
            float3 f = math.mul(rotation, new float3(0f, 0f, 1f));
            float2 xz = f.xz;
            if (math.lengthsq(xz) < 1e-8f)
                return quaternion.identity;
            xz = math.normalize(xz);
            return quaternion.LookRotationSafe(new float3(xz.x, 0f, xz.y), new float3(0f, 1f, 0f));
        }

        /// <summary>The tower's own sub-objects (beacon, number tiles) moved with it, as AlignSystem.MoveObject carries them
        /// (Game.Objects/AlignSystem.cs:167-180): same pose relative to the tower.</summary>
        private void CarrySubObjects(Entity tower, Transform from, Transform to)
        {
            if (!EntityManager.TryGetBuffer(tower, true, out DynamicBuffer<SubObject> subs))
                return;
            quaternion inv = math.inverse(from.m_Rotation);
            for (int i = 0; i < subs.Length; i++)
            {
                Entity sub = subs[i].m_SubObject;
                if (EntityManager.HasComponent<Deleted>(sub) || !EntityManager.TryGetComponent(sub, out Owner o) || o.m_Owner != tower ||
                    !EntityManager.TryGetComponent(sub, out Transform st))
                    continue;
                float3 local = math.mul(inv, st.m_Position - from.m_Position);
                quaternion localRot = math.mul(inv, st.m_Rotation);
                m_Writes.Add((sub, new Transform(to.m_Position + math.mul(to.m_Rotation, local), math.normalize(math.mul(to.m_Rotation, localRot)))));
                m_Touch.Add(sub);
            }
        }

        /// <summary>The first non-carrier, non-deleted, non-Temp sub-object of the node (TowerNumberTileSystem.TowerOf's rule).</summary>
        private Entity TowerOf(Entity node)
        {
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<SubObject> subs))
                return Entity.Null;
            IReadOnlyDictionary<Entity, float> carriers = m_PrefabCatalog.CarrierGripTops;
            for (int i = 0; i < subs.Length; i++)
            {
                Entity sub = subs[i].m_SubObject;
                if (EntityManager.HasComponent<Deleted>(sub) || !EntityManager.TryGetComponent(sub, out PrefabRef sp) || carriers.ContainsKey(sp.m_Prefab))
                    continue;
                return EntityManager.HasComponent<Temp>(sub) ? Entity.Null : sub;
            }
            return Entity.Null;
        }
    }
}
