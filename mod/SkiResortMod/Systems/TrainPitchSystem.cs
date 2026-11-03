// Phase 2 of the lift alignment fix (docs: lift-alignment-2026-11-02, DECISIONS 570): a line tower's sheave trains pitch with the rope.
// CS2 draws each span's rope straight to the tower's anchor, so the rope corners there; a rigid train cannot seat its wheels on a corner.
// The trains are articulated (owner verdict 2026-11-02, option C): two half-trains hinged on the main pin, which lies on the anchor, each
// pitched to its own span's rope. The tower mesh is skinned with bones "TrainFront*" (the half on the tower's +z side) and "TrainBack*"
// (-z); each bone's origin is the anchor its rope corners at (a second rope on the same half, the tram's haul rope, gets its own bone,
// e.g. "TrainFrontHaul"). The pitch is visual only: the tower Transform stays yaw-only (AlignSystem resets it on every node update) and the
// rope lanes keep anchoring on the prefab's sub-lane ends, which the bones turn about.
//   TrainPitchSystem (Modification4B, after LiftCensusSystem): per tower with train bones, the pitches from the rope lanes the engine built
//     (LaneSystem runs at Modification4): every lift-edge rope lane end on the tower's anchors gives its angle along the tower's +z
//     (LiftAlignMath.LaneEndPitch); the lanes leaving toward +z average to the front pitch, those toward -z to the back pitch
//     (LiftAlignMath.HalfPitches: each clamped to TrainPitchLimit, the type's steepest rope but at most 30 deg; the half-break to
//     MaxHalfBreakDeg). Kept in TrainPitch and the TrainBonePose buffer (not saved: the first census of a load recomputes every tower).
//   TrainBoneSystem (Rendering, after InitializeBonesSystem): writes the bones. Skeletons exist only near the camera and come back at rest
//     on every (re)allocation (InitializeBonesSystem.cs:57-70, :161-171), and vanilla never writes a None bone (ObjectInterpolateSystem
//     AnimateStaticBone has no case for it), so a Burst job re-applies the rotations each frame where they differ.
// The bone indices come from the mesh's ProceduralAnimationProperties by name (bone names are not kept at runtime). The ProceduralBone
// buffer is built in m_Bones order with m_BindIndex = that index (MeshSystem.cs:549-562), then sorted by hierarchy depth, parent and
// source (InitializeMeshJob, MeshSystem.cs:162-164, :237): sibling order is not kept, so the slot is the one whose m_BindIndex matches. The
// Bone buffer follows the sorted slots (ProceduralSkeletonSystem.GetSkinMatrices). The Skeleton index is the prefab SubMesh index
// (ObjectInterpolateSystem.cs:344-351).
// Never: Temp or Deleted towers (a preview stays level), towers whose prefab has no train bone (unskinned art: nothing changes), Editor.
// Log discipline: one line per rigged tower prefab per load, one line per pass that changed anything (with the towers whose break was
// clamped), a one-shot "!!!" catch.
// Mod.cs: UpdateAfter<TrainPitchSystem, LiftCensusSystem>(Modification4B); UpdateAfter<TrainBoneSystem, InitializeBonesSystem>(Rendering).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using SubLane = Game.Net.SubLane;
using SubObject = Game.Objects.SubObject;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    /// <summary>A rigged line tower's half-train pitches (runtime only, rebuilt from the rope lanes; the bone rotations are in its
    /// TrainBonePose buffer).</summary>
    public struct TrainPitch : IComponentData
    {
        public float m_Front;           // radians, nose up along the tower's +z, of the +z half (TrainFront bones)
        public float m_Back;            // the same for the -z half (TrainBack bones)
        public bool m_Clamped;          // the half-break was over LiftAlignMath.MaxHalfBreakDeg (diagnostics)
    }

    /// <summary>One train bone's local rotation: rest turned by its half's pitch (LiftAlignMath.TrainBoneRotation).</summary>
    [InternalBufferCapacity(4)]
    public struct TrainBonePose : IBufferElementData
    {
        public quaternion m_Rotation;
        public int m_SubMesh;           // prefab SubMesh index = Skeleton index of the mesh holding the bone
        public int m_Bone;              // bone index in that mesh (the sorted ProceduralBone slot)
    }

    public sealed partial class TrainPitchSystem : GameSystemBase
    {
        /// <summary>A lane end this close to one of the tower's anchors is that anchor's rope (FindAnchor snaps exactly, U1: 0.0000 m).</summary>
        internal const float AnchorMatchM = 0.25f;

        /// <summary>Below this change, radians, a pass leaves a tower's pose alone (no rewrite churn on re-runs).</summary>
        internal const float PitchEpsilon = 1e-4f;

        internal const string FrontPrefix = "TrainFront", BackPrefix = "TrainBack";

        private struct RigBone
        {
            public int SubMesh, Bone;
            public quaternion Rest;
            public bool Front;
        }

        private struct Pending
        {
            public Entity Tower;
            public TrainPitch Pitch;
            public List<RigBone> Rig;
            public bool Add;
        }

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_UpdatedEdges;
        private bool m_Armed, m_Disabled, m_Dirty;
        private string m_DirtyWhy = "load";
        private readonly List<LiftCensus.Edge> m_Edges = new List<LiftCensus.Edge>();
        private readonly Dictionary<Entity, List<RigBone>> m_Rigs = new Dictionary<Entity, List<RigBone>>();   // null: no train bone
        private readonly HashSet<Entity> m_SeenNodes = new HashSet<Entity>();
        private readonly List<float3> m_Anchors = new List<float3>(8);
        private readonly List<Pending> m_Pending = new List<Pending>();
        private int m_Passes;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_UpdatedEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            Mod.Log.Info($"{nameof(TrainPitchSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_Dirty = false;
            m_Edges.Clear();
            m_Rigs.Clear();
            m_Passes = 0;
        }

        /// <summary>Called by LiftCensusSystem after each build with its line edges (main thread, no ECS read here).</summary>
        internal void OnCensusBuilt(List<LiftCensus.Edge> edges)
        {
            if (!m_Armed || m_Disabled)
                return;
            m_Edges.Clear();
            m_Edges.AddRange(edges);
            Arm("census");
        }

        private void Arm(string why)
        {
            if (!m_Dirty)
                m_DirtyWhy = why;
            m_Dirty = true;
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                // A tower move or a span edit updates the lift edges (their lanes are rebuilt this frame at Modification4); the census
                // only rebuilds on Created / Deleted edges.
                if (!m_Dirty && m_Edges.Count > 0 && !m_UpdatedEdges.IsEmptyIgnoreFilter && LiftEdgeUpdated())
                    Arm("edge updated");
                if (!m_Dirty)
                    return;
                m_Dirty = false;
                Pass(m_DirtyWhy);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! Towers [trains]: {e.GetType().Name}: {e.Message} — disabled for this load (trains keep their last pose)\n{e}");
            }
        }

        private bool LiftEdgeUpdated()
        {
            IReadOnlyDictionary<Entity, LiftTypeDef> lines = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>().LiftLineTypes;
            NativeArray<Entity> upd = m_UpdatedEdges.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < upd.Length; i++)
                    if (lines.ContainsKey(EntityManager.GetComponentData<PrefabRef>(upd[i]).m_Prefab))
                        return true;
                return false;
            }
            finally
            {
                upd.Dispose();
            }
        }

        private void Pass(string why)
        {
            long t0 = Stopwatch.GetTimestamp();
            m_Passes++;
            m_SeenNodes.Clear();
            m_Pending.Clear();
            int towers = 0, noRope = 0, clamped = 0;
            float maxDeg = 0f, maxHalfBreakDeg = 0f;
            // Read everything first; the component adds below are structural changes, which would invalidate the node buffers read here.
            for (int i = 0; i < m_Edges.Count; i++)
            {
                LiftCensus.Edge ce = m_Edges[i];
                Entity edge = LiftCensusSystem.Ent(ce.Id);
                if (ce.Type == LiftCensus.NoType || ce.Type >= SkiCatalog.Lifts.Count || !EntityManager.Exists(edge) ||
                    EntityManager.HasComponent<Deleted>(edge) || !EntityManager.TryGetComponent(edge, out Edge e))
                    continue;                             // a newer census build carries the replacement
                float maxPitch = LiftAlignMath.TrainPitchLimit(SkiCatalog.Lifts[ce.Type].Envelope.SlopeMax);
                for (int end = 0; end < 2; end++)
                {
                    Entity node = end == 0 ? e.m_Start : e.m_End;
                    if (!m_SeenNodes.Add(node) || !EntityManager.TryGetBuffer(node, true, out DynamicBuffer<SubObject> subs))
                        continue;
                    for (int s = 0; s < subs.Length; s++)
                    {
                        Entity tower = subs[s].m_SubObject;
                        if (EntityManager.HasComponent<Temp>(tower) || EntityManager.HasComponent<Deleted>(tower) ||
                            !EntityManager.TryGetComponent(tower, out PrefabRef pr) || !EntityManager.TryGetComponent(tower, out Transform t))
                            continue;
                        List<RigBone> rig = RigOf(pr.m_Prefab);
                        if (rig == null)
                            continue;
                        towers++;
                        if (!PitchesAt(node, pr.m_Prefab, t, maxPitch, out float front, out float back, out bool wasClamped))
                        {
                            noRope++;
                            continue;
                        }
                        if (wasClamped)
                            clamped++;
                        maxDeg = math.max(maxDeg, math.degrees(math.max(math.abs(front), math.abs(back))));
                        maxHalfBreakDeg = math.max(maxHalfBreakDeg, math.degrees(math.abs(back - front) * 0.5f));
                        var pitch = new TrainPitch { m_Front = front, m_Back = back, m_Clamped = wasClamped };
                        bool add = !EntityManager.TryGetComponent(tower, out TrainPitch old);
                        if (!add && math.abs(old.m_Front - front) < PitchEpsilon && math.abs(old.m_Back - back) < PitchEpsilon)
                            continue;
                        m_Pending.Add(new Pending { Tower = tower, Pitch = pitch, Rig = rig, Add = add });
                    }
                }
            }

            int added = 0;
            for (int i = 0; i < m_Pending.Count; i++)
            {
                Pending p = m_Pending[i];
                DynamicBuffer<TrainBonePose> poses;
                if (p.Add)
                {
                    EntityManager.AddComponentData(p.Tower, p.Pitch);
                    poses = EntityManager.AddBuffer<TrainBonePose>(p.Tower);
                    added++;
                }
                else
                {
                    EntityManager.SetComponentData(p.Tower, p.Pitch);
                    poses = EntityManager.GetBuffer<TrainBonePose>(p.Tower);
                }
                poses.Clear();
                foreach (RigBone b in p.Rig)
                    poses.Add(new TrainBonePose
                    {
                        m_Rotation = LiftAlignMath.TrainBoneRotation(b.Rest, b.Front ? p.Pitch.m_Front : p.Pitch.m_Back),
                        m_SubMesh = b.SubMesh,
                        m_Bone = b.Bone,
                    });
            }

            if (m_Pending.Count > 0 || noRope > 0)
            {
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                Mod.Log.Info($"Towers [trains]: pass {m_Passes} ({why}) rigged towers {towers}, posed {m_Pending.Count} (new {added}), max |pitch| " +
                             $"{maxDeg:F1} deg, max half-break {maxHalfBreakDeg:F1} deg" +
                             (clamped > 0 ? $", {clamped} clamped to the {LiftAlignMath.MaxHalfBreakDeg:F0} deg half-break (rope off the inner wheels)" : string.Empty) +
                             (noRope > 0 ? $", {noRope} without a rope lane on their anchors (level)" : string.Empty) + $"; {ms:F2} ms");
            }
            m_Pending.Clear();
        }

        /// <summary>The half-train pitches from the rope lane ends on the tower's anchors (LiftAlignMath.HalfPitches); false when no lane end
        /// sits on an anchor.</summary>
        private bool PitchesAt(Entity node, Entity towerPrefab, Transform t, float maxPitch, out float front, out float back, out bool clamped)
        {
            front = back = 0f;
            clamped = false;
            m_Anchors.Clear();
            if (!EntityManager.TryGetBuffer(towerPrefab, true, out DynamicBuffer<Game.Prefabs.SubLane> anchors) ||
                !EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
                return false;
            for (int i = 0; i < anchors.Length; i++)
            {
                Colossal.Mathematics.Bezier4x3 w = Game.Objects.ObjectUtils.LocalToWorld(t.m_Position, t.m_Rotation, anchors[i].m_Curve);
                m_Anchors.Add(w.a);
                m_Anchors.Add(w.d);
            }
            float3 forward = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
            float sumF = 0f, sumB = 0f;
            int nF = 0, nB = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                if (!EntityManager.TryGetBuffer(edges[i].m_Edge, true, out DynamicBuffer<SubLane> lanes))
                    continue;
                for (int l = 0; l < lanes.Length; l++)
                {
                    Entity lane = lanes[l].m_SubLane;
                    if (!EntityManager.TryGetComponent(lane, out PrefabRef lp) || !CableLanePrefabs.IsHighVoltage(EntityManager, lp.m_Prefab) ||
                        !EntityManager.TryGetComponent(lane, out Curve c))
                        continue;
                    if (OnAnchor(c.m_Bezier.a))
                        AddEnd(c.m_Bezier.a, c.m_Bezier.b, forward, ref sumF, ref nF, ref sumB, ref nB);
                    if (OnAnchor(c.m_Bezier.d))
                        AddEnd(c.m_Bezier.d, c.m_Bezier.c, forward, ref sumF, ref nF, ref sumB, ref nB);
                }
            }
            if (nF + nB == 0)
                return false;
            clamped = LiftAlignMath.HalfPitches(nF > 0 ? sumF / nF : 0f, nF, nB > 0 ? sumB / nB : 0f, nB, maxPitch, out front, out back);
            return true;
        }

        private static void AddEnd(float3 end, float3 inner, float3 forward, ref float sumF, ref int nF, ref float sumB, ref int nB)
        {
            float pitch = LiftAlignMath.LaneEndPitch(end, inner, forward);
            if (LiftAlignMath.LaneEndSide(end, inner, forward) > 0)
            {
                sumF += pitch;
                nF++;
            }
            else
            {
                sumB += pitch;
                nB++;
            }
        }

        private bool OnAnchor(float3 p)
        {
            for (int i = 0; i < m_Anchors.Count; i++)
                if (math.distancesq(m_Anchors[i], p) < AnchorMatchM * AnchorMatchM)
                    return true;
            return false;
        }

        /// <summary>The prefab's train bones (TrainFront* / TrainBack*), resolved once per prefab per load from the mesh prefabs (main
        /// thread); null when it has none.</summary>
        private List<RigBone> RigOf(Entity prefab)
        {
            if (m_Rigs.TryGetValue(prefab, out List<RigBone> rig))
                return rig;
            rig = null;
            int front = 0, back = 0;
            if (EntityManager.TryGetBuffer(prefab, true, out DynamicBuffer<SubMesh> meshes))
                for (int j = 0; j < meshes.Length; j++)
                {
                    Entity mesh = meshes[j].m_SubMesh;
                    if (!m_PrefabSystem.TryGetPrefab(mesh, out RenderPrefab render) ||
                        !EntityManager.TryGetBuffer(mesh, true, out DynamicBuffer<ProceduralBone> bones))
                        continue;
                    ProceduralAnimationProperties props = render.GetComponent<ProceduralAnimationProperties>();
                    if (props == null || props.m_Bones == null || props.m_Bones.Length != bones.Length)
                        continue;
                    for (int k = 0; k < props.m_Bones.Length; k++)
                    {
                        string name = props.m_Bones[k].name;
                        bool isFront = name != null && name.StartsWith(FrontPrefix, StringComparison.Ordinal);
                        if (!isFront && (name == null || !name.StartsWith(BackPrefix, StringComparison.Ordinal)))
                            continue;
                        for (int b = 0; b < bones.Length; b++)
                            if (bones[b].m_BindIndex == k)    // m_Bones index k -> the sorted buffer slot
                            {
                                (rig ??= new List<RigBone>()).Add(new RigBone { SubMesh = j, Bone = b, Rest = bones[b].m_Rotation, Front = isFront });
                                if (isFront)
                                    front++;
                                else
                                    back++;
                                break;
                            }
                    }
                }
            m_Rigs[prefab] = rig;
            if (rig != null)
                Mod.Log.Info($"Towers [trains]: {m_PrefabSystem.GetPrefabName(prefab)} has train bones ({front} front, {back} back); its half-trains " +
                             "pitch with the rope" + (front == 0 || back == 0 ? " — !!! one half has no bone, that half stays level" : string.Empty));
            return rig;
        }
    }

    public sealed partial class TrainBoneSystem : GameSystemBase
    {
        [BurstCompile]
        private struct WriteBonesJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<TrainBonePose> PoseType;
            public BufferTypeHandle<Skeleton> SkeletonType;
            public BufferTypeHandle<Bone> BonesType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                BufferAccessor<TrainBonePose> poses = chunk.GetBufferAccessor(ref PoseType);
                BufferAccessor<Skeleton> skeletons = chunk.GetBufferAccessor(ref SkeletonType);
                BufferAccessor<Bone> bones = chunk.GetBufferAccessor(ref BonesType);
                for (int i = 0; i < chunk.Count; i++)
                {
                    DynamicBuffer<TrainBonePose> pose = poses[i];
                    DynamicBuffer<Skeleton> skel = skeletons[i];
                    DynamicBuffer<Bone> bone = bones[i];
                    for (int k = 0; k < pose.Length; k++)
                    {
                        TrainBonePose p = pose[k];
                        if (p.m_SubMesh >= skel.Length)
                            continue;                     // not near the camera: no skeleton (InitializeBonesSystem Remove)
                        ref Skeleton s = ref skel.ElementAt(p.m_SubMesh);
                        if (s.m_BufferAllocation.Empty || s.m_BoneOffset < 0 || s.m_BoneOffset + p.m_Bone >= bone.Length)
                            continue;
                        ref Bone b = ref bone.ElementAt(s.m_BoneOffset + p.m_Bone);
                        if (b.m_Rotation.Equals(p.m_Rotation))
                            continue;
                        b.m_Rotation = p.m_Rotation;
                        s.m_CurrentUpdated = true;        // ProceduralUploadSystem uploads it this frame
                    }
                }
            }
        }

        private EntityQuery m_Query;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TrainBonePose>(), ComponentType.ReadWrite<Skeleton>(), ComponentType.ReadWrite<Bone>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            RequireForUpdate(m_Query);
            // Job reflection data and JIT before the first Schedule (the PrewarmJit precedent). Schedules nothing.
            JobChunkExtensions.EarlyJobInit<WriteBonesJob>();
            Mod.Log.Info($"{nameof(TrainBoneSystem)} created");
        }

        protected override void OnUpdate()
        {
            Dependency = new WriteBonesJob
            {
                PoseType = GetBufferTypeHandle<TrainBonePose>(true),
                SkeletonType = GetBufferTypeHandle<Skeleton>(false),
                BonesType = GetBufferTypeHandle<Bone>(false),
            }.Schedule(m_Query, Dependency);
        }
    }
}
