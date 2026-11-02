// U1 probe (lift alignment Phase 2, docs lift-alignment-2026-11-02 risk U1): are the node sub-objects SubObjectSystem creates or moves
// at Modification2B visible to a Modification3 system in the same frame, does Game.Objects.AlignSystem (Modification4) reset a pitch
// written before it, and does LaneSystem (Modification4) read a pitch written after AlignSystem? Stand-in for the future sheave
// train: the line towers themselves (node rows, Aligned, they carry the rope anchors today). Dev bridge only, never in the product.
// Op: u1probe {on, prefab: "SkiChairliftHV", tilt: 5, frames: 400}. Log lines start with [U1] in SkiTestBridge.log.

using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiTestBridge
{
    /// <summary>This Entities version has no EntityManager.TryGetComponent / TryGetBuffer.</summary>
    internal static class U1EntityManagerExt
    {
        public static bool TryGetComponent<T>(this EntityManager em, Entity e, out T value) where T : unmanaged, IComponentData
        {
            value = default;
            if (!em.HasComponent<T>(e))
                return false;
            value = em.GetComponentData<T>(e);
            return true;
        }

        public static bool TryGetBuffer<T>(this EntityManager em, Entity e, bool readOnly, out DynamicBuffer<T> buffer) where T : unmanaged, IBufferElementData
        {
            buffer = default;
            if (!em.HasBuffer<T>(e))
                return false;
            buffer = em.GetBuffer<T>(e, readOnly);
            return true;
        }
    }

    internal static class U1Probe
    {
        public static bool On;
        public static string Prefab = "SkiChairliftHV";
        public static float TiltDeg = 5f;
        public static int LinesLeft;
        // towers written at M3 this frame (M4 re-writes them and tags their edges); edges to read at M4B; towers to re-read later
        public static readonly HashSet<Entity> WroteM3 = new HashSet<Entity>();
        public static readonly HashSet<Entity> EdgesToRead = new HashSet<Entity>();
        public static readonly Dictionary<Entity, int> Watch = new Dictionary<Entity, int>();   // tower -> frame written

        public static int Frame => UnityEngine.Time.frameCount;

        public static void Line(string s)
        {
            if (LinesLeft <= 0)
                return;
            LinesLeft--;
            Mod.Log.Info("[U1] f=" + Frame + " " + s);
        }

        public static string E(Entity e) => e.Index + ":" + e.Version;

        public static string V(float3 v) => "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")";

        public static float TiltOf(quaternion r)
        {
            float3 up = math.mul(r, new float3(0f, 1f, 0f));
            return math.degrees(math.acos(math.clamp(up.y, -1f, 1f)));
        }

        /// <summary>The tower's node if it is a node of the probed line prefab.</summary>
        public static bool LineNodeOf(EntityManager em, PrefabSystem prefabs, Entity obj, out Entity node)
        {
            node = Entity.Null;
            if (!em.TryGetComponent(obj, out Owner owner) || !em.HasComponent<Node>(owner.m_Owner) ||
                !em.TryGetComponent(owner.m_Owner, out PrefabRef pr))
                return false;
            if (!prefabs.TryGetPrefab(pr.m_Prefab, out PrefabBase p) || p.name != Prefab)
                return false;
            node = owner.m_Owner;
            return true;
        }

        public static void Tilt(EntityManager em, Entity tower)
        {
            Transform t = em.GetComponentData<Transform>(tower);
            float3 fwd = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
            quaternion yaw = quaternion.LookRotationSafe(math.normalizesafe(new float3(fwd.x, 0f, fwd.z), new float3(0f, 0f, 1f)), new float3(0f, 1f, 0f));
            t.m_Rotation = math.mul(yaw, quaternion.RotateX(math.radians(TiltDeg)));
            em.SetComponentData(tower, t);
        }
    }

    public partial class TestBridgeSystem
    {
        private JObject U1ProbeCommand(JObject args)
        {
            U1Probe.On = (bool?)args["on"] ?? true;
            U1Probe.Prefab = (string)args["prefab"] ?? U1Probe.Prefab;
            U1Probe.TiltDeg = (float?)args["tilt"] ?? U1Probe.TiltDeg;
            U1Probe.LinesLeft = (int?)args["frames"] ?? 400;
            U1Probe.WroteM3.Clear();
            U1Probe.EdgesToRead.Clear();
            U1Probe.Watch.Clear();
            Mod.Log.Info($"[U1] armed={U1Probe.On} prefab={U1Probe.Prefab} tilt={U1Probe.TiltDeg} lines={U1Probe.LinesLeft}");
            return new JObject { ["ok"] = true, ["on"] = U1Probe.On, ["prefab"] = U1Probe.Prefab };
        }

        /// <summary>bone {id: "i:v", mesh: 0, bone: 2, rotX: deg, rotY: deg}: writes one procedural bone of an object our way (rest
        /// rotation x RotateY x RotateX, rest position) and flags the skeleton for upload, as a None-type bone writer would. Without
        /// rot args it only lists the bones (rest and current). The object must be near the camera (bone buffers exist only then).</summary>
        private JObject BoneCommand(JObject args)
        {
            string[] p = ((string)args["id"]).Split(':');
            Entity e = new Entity { Index = int.Parse(p[0]), Version = int.Parse(p[1]) };
            int mesh = (int?)args["mesh"] ?? 0;
            if (!EntityManager.Exists(e) || !EntityManager.TryGetComponent(e, out PrefabRef pr))
                return new JObject { ["ok"] = false, ["error"] = "no entity" };
            if (!EntityManager.TryGetBuffer(e, false, out DynamicBuffer<Game.Rendering.Skeleton> skel) || mesh >= skel.Length)
                return new JObject { ["ok"] = false, ["error"] = "no skeleton (not near the camera, or no bones)" };
            DynamicBuffer<Game.Prefabs.SubMesh> subs = EntityManager.GetBuffer<Game.Prefabs.SubMesh>(pr.m_Prefab, true);
            DynamicBuffer<ProceduralBone> pb = EntityManager.GetBuffer<ProceduralBone>(subs[mesh].m_SubMesh, true);
            DynamicBuffer<Game.Rendering.Bone> bones = EntityManager.GetBuffer<Game.Rendering.Bone>(e, false);
            Game.Rendering.Skeleton sk = skel[mesh];
            if (sk.m_BufferAllocation.Empty)
                return new JObject { ["ok"] = false, ["error"] = "skeleton not allocated (far from the camera)" };
            var list = new JArray();
            for (int k = 0; k < pb.Length; k++)
            {
                Game.Rendering.Bone b = bones[sk.m_BoneOffset + k];
                list.Add(new JObject { ["k"] = k, ["type"] = pb[k].m_Type.ToString(), ["parent"] = pb[k].m_ParentIndex,
                    ["restPos"] = U1Probe.V(pb[k].m_Position), ["restRot"] = pb[k].m_Rotation.value.ToString(),
                    ["pos"] = U1Probe.V(b.m_Position), ["rot"] = b.m_Rotation.value.ToString() });
            }
            if (args["bone"] != null && (args["rotX"] != null || args["rotY"] != null))
            {
                int k = (int)args["bone"];
                Game.Rendering.Bone b = bones[sk.m_BoneOffset + k];
                b.m_Position = pb[k].m_Position;
                b.m_Rotation = math.mul(pb[k].m_Rotation, math.mul(quaternion.RotateY(math.radians((float?)args["rotY"] ?? 0f)),
                                                                   quaternion.RotateX(math.radians((float?)args["rotX"] ?? 0f))));
                bones[sk.m_BoneOffset + k] = b;
                sk.m_CurrentUpdated = true;
                skel[mesh] = sk;
            }
            return new JObject { ["ok"] = true, ["bones"] = list, ["skeletons"] = skel.Length };
        }
    }

    /// <summary>Modification3, after SubObjectReferencesSystem: sees the 2B results? writes the tilt.</summary>
    public partial class U1ProbeM3System : GameSystemBase
    {
        private PrefabSystem m_Prefabs;
        private EntityQuery m_Updated;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Updated = GetEntityQuery(ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Transform>(),
                                       ComponentType.ReadOnly<Updated>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
        }

        protected override void OnUpdate()
        {
            U1Probe.WroteM3.Clear();
            if (!U1Probe.On || m_Updated.IsEmpty)
                return;
            using (NativeArray<Entity> objs = m_Updated.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity o in objs)
                {
                    if (!EntityManager.HasComponent<Game.Objects.Object>(o) || !U1Probe.LineNodeOf(EntityManager, m_Prefabs, o, out Entity node))
                        continue;
                    bool created = EntityManager.HasComponent<Created>(o);
                    bool inBuf = false;
                    if (EntityManager.TryGetBuffer(node, true, out DynamicBuffer<Game.Objects.SubObject> subs))
                        for (int i = 0; i < subs.Length; i++)
                            inBuf |= subs[i].m_SubObject == o;
                    Transform t = EntityManager.GetComponentData<Transform>(o);
                    Node n = EntityManager.GetComponentData<Node>(node);
                    U1Probe.Line($"M3 tower={U1Probe.E(o)} node={U1Probe.E(node)} created={(created ? 1 : 0)} updated=1 inNodeBuf={(inBuf ? 1 : 0)} " +
                                 $"y={t.m_Position.y:F3} nodeY={n.m_Position.y:F3} tiltDeg={U1Probe.TiltOf(t.m_Rotation):F2}");
                    U1Probe.Tilt(EntityManager, o);
                    U1Probe.WroteM3.Add(o);
                    U1Probe.Line($"M3 WROTE tilt={U1Probe.TiltDeg:F2} tower={U1Probe.E(o)}");
                }
            }
        }
    }

    /// <summary>Modification4, after AlignSystem and before LaneSystem: did Align reset it? write again, tag the edges.</summary>
    public partial class U1ProbeM4System : GameSystemBase
    {
        protected override void OnUpdate()
        {
            if (!U1Probe.On || U1Probe.WroteM3.Count == 0)
                return;
            foreach (Entity o in U1Probe.WroteM3)
            {
                if (!EntityManager.Exists(o) || !EntityManager.TryGetComponent(o, out Transform t))
                    continue;
                Entity node = EntityManager.GetComponentData<Owner>(o).m_Owner;
                float geoY = EntityManager.TryGetComponent(node, out NodeGeometry g) ? g.m_Position : float.NaN;
                U1Probe.Line($"M4.afterAlign tower={U1Probe.E(o)} tiltDeg={U1Probe.TiltOf(t.m_Rotation):F2} y={t.m_Position.y:F3} nodeGeoY={geoY:F3}");
                U1Probe.Tilt(EntityManager, o);
                int already = 0, tagged = 0;
                if (EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
                {
                    var list = new List<Entity>();
                    for (int i = 0; i < edges.Length; i++)
                        list.Add(edges[i].m_Edge);
                    foreach (Entity e in list)
                    {
                        if (EntityManager.HasComponent<Updated>(e)) already++;
                        else { EntityManager.AddComponent<Updated>(e); tagged++; }
                        U1Probe.EdgesToRead.Add(e);
                    }
                }
                U1Probe.Watch[o] = U1Probe.Frame;
                U1Probe.Line($"M4.afterAlign WROTE tilt={U1Probe.TiltDeg:F2} tower={U1Probe.E(o)} edgesAlreadyUpdated={already} edgesTagged={tagged}");
            }
        }
    }

    /// <summary>Modification4B: did LaneSystem anchor the ropes on the tilted pose? does the tilt hold later?</summary>
    public partial class U1ProbeM4BSystem : GameSystemBase
    {
        private static readonly int[] kLater = { 1, 2, 30, 300 };

        protected override void OnUpdate()
        {
            if (!U1Probe.On)
                return;
            int frame = U1Probe.Frame;
            if (U1Probe.EdgesToRead.Count > 0)
            {
                // expected anchor points: every watched tower's prefab sub-lane ends, through its CURRENT transform
                var expect = new List<float3>();
                foreach (var kv in U1Probe.Watch)
                {
                    if (!EntityManager.Exists(kv.Key) || !EntityManager.TryGetComponent(kv.Key, out Transform t) ||
                        !EntityManager.TryGetComponent(kv.Key, out PrefabRef pr) ||
                        !EntityManager.TryGetBuffer(pr.m_Prefab, true, out DynamicBuffer<Game.Prefabs.SubLane> sl))
                        continue;
                    for (int i = 0; i < sl.Length; i++)
                    {
                        Bezier4x3 w = ObjectUtils.LocalToWorld(t.m_Position, t.m_Rotation, sl[i].m_Curve);
                        expect.Add(w.a);
                        expect.Add(w.d);
                    }
                }
                foreach (Entity e in U1Probe.EdgesToRead)
                {
                    if (!EntityManager.Exists(e) || !EntityManager.TryGetBuffer(e, true, out DynamicBuffer<Game.Net.SubLane> lanes))
                        continue;
                    for (int i = 0; i < lanes.Length; i++)
                    {
                        Entity lane = lanes[i].m_SubLane;
                        if (!EntityManager.TryGetComponent(lane, out Curve c) || !EntityManager.TryGetComponent(lane, out PrefabRef lp) ||
                            !EntityManager.TryGetComponent(lp.m_Prefab, out UtilityLaneData ud) ||
                            (ud.m_UtilityTypes & UtilityTypes.HighVoltageLine) == 0)
                            continue;
                        foreach (var end in new[] { ("a", c.m_Bezier.a), ("d", c.m_Bezier.d) })
                        {
                            float best = float.MaxValue; float3 bp = default;
                            foreach (float3 x in expect)
                            {
                                float dd = math.distance(x, end.Item2);
                                if (dd < best) { best = dd; bp = x; }
                            }
                            if (best < 3f)
                                U1Probe.Line($"M4B edge={U1Probe.E(e)} lane={U1Probe.E(lane)} end={end.Item1} pos={U1Probe.V(end.Item2)} expect={U1Probe.V(bp)} err={best:F4}");
                        }
                    }
                }
                U1Probe.EdgesToRead.Clear();
            }
            var gone = new List<Entity>();
            foreach (var kv in U1Probe.Watch)
            {
                int age = frame - kv.Value;
                if (!EntityManager.Exists(kv.Key) || !EntityManager.TryGetComponent(kv.Key, out Transform t))
                {
                    gone.Add(kv.Key);
                    continue;
                }
                if (age == 0 || System.Array.IndexOf(kLater, age) >= 0)
                    U1Probe.Line($"M4B +{age} tower={U1Probe.E(kv.Key)} tiltDeg={U1Probe.TiltOf(t.m_Rotation):F2} updated={(EntityManager.HasComponent<Updated>(kv.Key) ? 1 : 0)}");
                if (age > 300)
                    gone.Add(kv.Key);
            }
            foreach (Entity g in gone)
                U1Probe.Watch.Remove(g);
        }
    }
}
