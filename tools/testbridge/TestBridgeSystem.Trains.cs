// Gate G8 for the pitched sheave trains (lift alignment Phase 2, docs lift-alignment-2026-11-02): per line tower whose mesh has a bone
// named "Train", the pitch the bone shows (rotation relative to its rest, about x; nose up along the tower's +z) against an independent
// expectation: the analytic hanging-lane end slopes (chord -/+ 4h, LaneSystem.cs:4128-4138) of every rope lane ending on the tower's
// anchors, averaged, clamped to the lift type's limit when given. Written apart from the mod's TrainPitchSystem (which reads the lane
// control points), so a sign or side error in either shows. Dev bridge only, never in the product.
// Op: trains {x, z, r (default 400), maxDeg (optional clamp)} -> rows {id, prefab, pos, expectDeg, boneDeg | null, diffDeg, ends}.

using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using SubLane = Game.Net.SubLane;
using Transform = Game.Objects.Transform;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private JObject TrainsCommand(JObject args)
        {
            float2 at = new float2((float?)args["x"] ?? 0f, (float?)args["z"] ?? 0f);
            float r = (float?)args["r"] ?? 400f;
            float maxDeg = (float?)args["maxDeg"] ?? 90f;
            EntityQuery q = GetEntityQuery(ComponentType.ReadOnly<Game.Rendering.Skeleton>(), ComponentType.ReadOnly<Owner>(),
                                           ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<PrefabRef>());
            NativeArray<Entity> all = q.ToEntityArray(Allocator.Temp);
            var rows = new JArray();
            int withBone = 0, allocated = 0;
            float worst = 0f;
            try
            {
                for (int i = 0; i < all.Length; i++)
                {
                    Entity e = all[i];
                    Transform t = EntityManager.GetComponentData<Transform>(e);
                    if (math.distance(t.m_Position.xz, at) > r || EntityManager.HasComponent<Game.Tools.Temp>(e))
                        continue;
                    Entity node = EntityManager.GetComponentData<Owner>(e).m_Owner;
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!EntityManager.HasComponent<Node>(node) || !TrainBone(prefab, out int mesh, out int bone, out quaternion rest))
                        continue;
                    withBone++;
                    float expect = ExpectedPitch(e, node, prefab, t, out int ends);
                    expect = math.clamp(expect, -math.radians(maxDeg), math.radians(maxDeg));
                    JToken boneDeg = JValue.CreateNull();
                    JToken diff = JValue.CreateNull();
                    if (EntityManager.TryGetBuffer(e, true, out DynamicBuffer<Game.Rendering.Skeleton> skel) && mesh < skel.Length &&
                        !skel[mesh].m_BufferAllocation.Empty)
                    {
                        allocated++;
                        Game.Rendering.Bone b = EntityManager.GetBuffer<Game.Rendering.Bone>(e, true)[skel[mesh].m_BoneOffset + bone];
                        quaternion rel = math.mul(math.inverse(rest), b.m_Rotation);
                        float pitch = -2f * math.atan2(rel.value.x, rel.value.w);   // RotateX(-a) = (sin(-a/2), 0, 0, cos(a/2))
                        boneDeg = math.degrees(pitch);
                        float d = math.abs(math.degrees(pitch - expect));
                        diff = d;
                        worst = math.max(worst, d);
                    }
                    rows.Add(new JObject { ["id"] = U1Probe.E(e), ["prefab"] = m_Prefabs.GetPrefabName(prefab), ["pos"] = U1Probe.V(t.m_Position),
                        ["expectDeg"] = math.degrees(expect), ["boneDeg"] = boneDeg, ["diffDeg"] = diff, ["ends"] = ends });
                }
            }
            finally
            {
                all.Dispose();
            }
            return new JObject { ["ok"] = true, ["towers"] = withBone, ["allocated"] = allocated, ["worstDiffDeg"] = worst, ["rows"] = rows };
        }

        private bool TrainBone(Entity prefab, out int mesh, out int bone, out quaternion rest)
        {
            mesh = bone = -1;
            rest = quaternion.identity;
            if (!EntityManager.TryGetBuffer(prefab, true, out DynamicBuffer<SubMesh> meshes))
                return false;
            for (int j = 0; j < meshes.Length; j++)
            {
                if (!m_Prefabs.TryGetPrefab(meshes[j].m_SubMesh, out RenderPrefab render) ||
                    !EntityManager.TryGetBuffer(meshes[j].m_SubMesh, true, out DynamicBuffer<ProceduralBone> pb))
                    continue;
                ProceduralAnimationProperties props = render.GetComponent<ProceduralAnimationProperties>();
                if (props == null || props.m_Bones == null)
                    continue;
                for (int k = 0; k < props.m_Bones.Length && k < pb.Length; k++)
                    if (props.m_Bones[k].name == "Train")
                    {
                        mesh = j;
                        bone = k;
                        rest = pb[k].m_Rotation;
                        return true;
                    }
            }
            return false;
        }

        /// <summary>Mean, over the HV lanes ending on the tower's anchors, of the analytic end slope angle along the tower's +z.</summary>
        private float ExpectedPitch(Entity tower, Entity node, Entity prefab, Transform t, out int ends)
        {
            ends = 0;
            if (!EntityManager.TryGetBuffer(prefab, true, out DynamicBuffer<Game.Prefabs.SubLane> anchors) ||
                !EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
                return 0f;
            var pts = new System.Collections.Generic.List<float3>();
            for (int i = 0; i < anchors.Length; i++)
            {
                Bezier4x3 w = ObjectUtils.LocalToWorld(t.m_Position, t.m_Rotation, anchors[i].m_Curve);
                pts.Add(w.a);
                pts.Add(w.d);
            }
            float3 fwd = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
            float sum = 0f;
            for (int i = 0; i < edges.Length; i++)
            {
                if (!EntityManager.TryGetBuffer(edges[i].m_Edge, true, out DynamicBuffer<SubLane> lanes))
                    continue;
                for (int l = 0; l < lanes.Length; l++)
                {
                    Entity lane = lanes[l].m_SubLane;
                    if (!EntityManager.TryGetComponent(lane, out Curve c) || !EntityManager.TryGetComponent(lane, out PrefabRef lp) ||
                        !EntityManager.TryGetComponent(lp.m_Prefab, out UtilityLaneData ud) || (ud.m_UtilityTypes & UtilityTypes.HighVoltageLine) == 0)
                        continue;
                    float3 a = c.m_Bezier.a, d = c.m_Bezier.d;
                    float len = math.distance(a.xz, d.xz);
                    if (len < 1e-3f)
                        continue;
                    bool atA = Near(pts, a), atD = Near(pts, d);
                    if (atA)
                    {
                        // leaving the tower toward d: slope (d - a) / L - 4h, horizontal direction d - a
                        float ang = math.atan((d.y - a.y) / len - 4f * ud.m_Hanging);
                        sum += math.dot(d.xz - a.xz, fwd.xz) >= 0f ? ang : -ang;
                        ends++;
                    }
                    if (atD)
                    {
                        // leaving the tower back toward a: slope (a - d) / L - 4h by symmetry of the parabola
                        float ang = math.atan((a.y - d.y) / len - 4f * ud.m_Hanging);
                        sum += math.dot(a.xz - d.xz, fwd.xz) >= 0f ? ang : -ang;
                        ends++;
                    }
                }
            }
            return ends == 0 ? 0f : sum / ends;
        }

        private static bool Near(System.Collections.Generic.List<float3> pts, float3 p)
        {
            foreach (float3 x in pts)
                if (math.distance(x, p) < 0.25f)
                    return true;
            return false;
        }
    }
}
