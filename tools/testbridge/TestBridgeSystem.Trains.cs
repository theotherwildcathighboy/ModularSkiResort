// Gate G8 for the pitched sheave trains (lift alignment Phase 2, docs lift-alignment-2026-11-02. Articulated halves, owner verdict
// 2026-11-02 option C). For each line tower whose mesh has bones named "TrainFront*" / "TrainBack*", the gate compares two values.
// The first is the pitch each bone shows (rotation relative to its rest, about x, nose up along the tower's +z). The second is an
// independent expectation. It is the analytic hanging-lane end slopes (chord -/+ 4h, LaneSystem.cs:4128-4138) of the rope lanes
// ending on the tower's anchors, averaged per side. Lanes leaving toward +z feed the front half, and lanes leaving toward -z feed
// the back. If one side is missing, it takes the other side's value. The gate clamps each averaged value to maxDeg and the
// half-break to halfBreakDeg. This code is written apart from the mod's TrainPitchSystem (which reads the lane control points), so
// a sign or side error in either shows. Dev bridge only.
// Op: trains {x, z, r (default 400), maxDeg (optional clamp), halfBreakDeg (default 20)} -> rows {id, prefab, pos, frontDeg, backDeg,
// ends {front, back}, bones [{name, expectDeg, boneDeg | null, diffDeg | null}]}.

using System.Collections.Generic;
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
        private struct TrainBoneRef
        {
            public string Name;
            public bool Front;
            public int Mesh, Bone;
            public quaternion Rest;
        }

        private JObject TrainsCommand(JObject args)
        {
            float2 at = new float2((float?)args["x"] ?? 0f, (float?)args["z"] ?? 0f);
            float r = (float?)args["r"] ?? 400f;
            float max = math.radians((float?)args["maxDeg"] ?? 90f);
            float halfBreak = math.radians((float?)args["halfBreakDeg"] ?? 20f);
            EntityQuery q = GetEntityQuery(ComponentType.ReadOnly<Game.Rendering.Skeleton>(), ComponentType.ReadOnly<Owner>(),
                                           ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<PrefabRef>());
            NativeArray<Entity> all = q.ToEntityArray(Allocator.Temp);
            var rows = new JArray();
            var rig = new List<TrainBoneRef>();
            int withBones = 0, allocated = 0, bonesChecked = 0;
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
                    rig.Clear();
                    if (!EntityManager.HasComponent<Node>(node) || !TrainBones(prefab, rig))
                        continue;
                    withBones++;
                    ExpectedPitches(node, prefab, t, out float sumF, out int nF, out float sumB, out int nB);
                    float front = nF > 0 ? sumF / nF : nB > 0 ? sumB / nB : 0f;
                    float back = nB > 0 ? sumB / nB : front;
                    front = math.clamp(front, -max, max);
                    back = math.clamp(back, -max, max);
                    float mid = 0.5f * (front + back), half = 0.5f * (back - front);
                    if (math.abs(half) > halfBreak)
                    {
                        front = mid - math.sign(half) * halfBreak;
                        back = mid + math.sign(half) * halfBreak;
                    }
                    front = math.clamp(front, -max, max);
                    back = math.clamp(back, -max, max);
                    bool skelOk = EntityManager.TryGetBuffer(e, true, out DynamicBuffer<Game.Rendering.Skeleton> skel);
                    bool any = false;
                    var boneRows = new JArray();
                    foreach (TrainBoneRef rb in rig)
                    {
                        float expect = rb.Front ? front : back;
                        JToken boneDeg = JValue.CreateNull(), diff = JValue.CreateNull();
                        if (skelOk && rb.Mesh < skel.Length && !skel[rb.Mesh].m_BufferAllocation.Empty)
                        {
                            any = true;
                            bonesChecked++;
                            Game.Rendering.Bone b = EntityManager.GetBuffer<Game.Rendering.Bone>(e, true)[skel[rb.Mesh].m_BoneOffset + rb.Bone];
                            quaternion rel = math.mul(math.inverse(rb.Rest), b.m_Rotation);
                            float pitch = -2f * math.atan2(rel.value.x, rel.value.w);   // RotateX(-a) = (sin(-a/2), 0, 0, cos(a/2))
                            boneDeg = math.degrees(pitch);
                            float d = math.abs(math.degrees(pitch - expect));
                            diff = d;
                            worst = math.max(worst, d);
                        }
                        boneRows.Add(new JObject { ["name"] = rb.Name, ["expectDeg"] = math.degrees(expect), ["boneDeg"] = boneDeg, ["diffDeg"] = diff });
                    }
                    if (any)
                        allocated++;
                    rows.Add(new JObject { ["id"] = U1Probe.E(e), ["prefab"] = m_Prefabs.GetPrefabName(prefab), ["pos"] = U1Probe.V(t.m_Position),
                        ["frontDeg"] = math.degrees(front), ["backDeg"] = math.degrees(back),
                        ["ends"] = new JObject { ["front"] = nF, ["back"] = nB }, ["bones"] = boneRows });
                }
            }
            finally
            {
                all.Dispose();
            }
            return new JObject { ["ok"] = true, ["towers"] = withBones, ["allocated"] = allocated, ["bonesChecked"] = bonesChecked,
                ["worstDiffDeg"] = worst, ["rows"] = rows };
        }

        /// <summary>Every TrainFront* / TrainBack* bone of the prefab's meshes, through ProceduralBone.m_BindIndex (the buffer is sorted by
        /// depth, MeshSystem.cs:162-164, :237).</summary>
        private bool TrainBones(Entity prefab, List<TrainBoneRef> rig)
        {
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
                for (int k = 0; k < props.m_Bones.Length; k++)
                {
                    string name = props.m_Bones[k].name ?? string.Empty;
                    bool front = name.StartsWith("TrainFront", System.StringComparison.Ordinal);
                    if (!front && !name.StartsWith("TrainBack", System.StringComparison.Ordinal))
                        continue;
                    for (int b = 0; b < pb.Length; b++)
                        if (pb[b].m_BindIndex == k)
                        {
                            rig.Add(new TrainBoneRef { Name = name, Front = front, Mesh = j, Bone = b, Rest = pb[b].m_Rotation });
                            break;
                        }
                }
            }
            return rig.Count > 0;
        }

        /// <summary>Per side, over the HV lanes ending on the tower's anchors, the sum and count of the analytic end slope angles, nose up
        /// along the tower's +z: front = lanes leaving the anchor toward +z, back = toward -z.</summary>
        private void ExpectedPitches(Entity node, Entity prefab, Transform t, out float sumF, out int nF, out float sumB, out int nB)
        {
            sumF = sumB = 0f;
            nF = nB = 0;
            if (!EntityManager.TryGetBuffer(prefab, true, out DynamicBuffer<Game.Prefabs.SubLane> anchors) ||
                !EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
                return;
            var pts = new List<float3>();
            for (int i = 0; i < anchors.Length; i++)
            {
                Bezier4x3 w = ObjectUtils.LocalToWorld(t.m_Position, t.m_Rotation, anchors[i].m_Curve);
                pts.Add(w.a);
                pts.Add(w.d);
            }
            float3 fwd = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
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
                    // leaving a toward d: slope (d - a) / L - 4h, and leaving d back toward a: (a - d) / L - 4h (the parabola is symmetric)
                    if (Near(pts, a))
                        Side(math.atan((d.y - a.y) / len - 4f * ud.m_Hanging), d.xz - a.xz, fwd, ref sumF, ref nF, ref sumB, ref nB);
                    if (Near(pts, d))
                        Side(math.atan((a.y - d.y) / len - 4f * ud.m_Hanging), a.xz - d.xz, fwd, ref sumF, ref nF, ref sumB, ref nB);
                }
            }
        }

        private static void Side(float climb, float2 dir, float3 fwd, ref float sumF, ref int nF, ref float sumB, ref int nB)
        {
            if (math.dot(dir, fwd.xz) >= 0f)
            {
                sumF += climb;            // climbing away toward +z: nose up along +z
                nF++;
            }
            else
            {
                sumB -= climb;            // climbing away toward -z: the rope comes down toward +z
                nB++;
            }
        }

        private static bool Near(List<float3> pts, float3 p)
        {
            foreach (float3 x in pts)
                if (math.distance(x, p) < 0.25f)
                    return true;
            return false;
        }
    }
}
