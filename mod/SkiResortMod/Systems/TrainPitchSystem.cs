// Phase 2 of the lift alignment fix (docs: lift-alignment-2026-11-02, DECISIONS 570): a line tower's sheave trains pitch with the rope.
// The tower mesh is skinned with a "Train" bone whose origin lies on the line through both rope points, so the pitch is visual only: the
// tower Transform stays yaw-only (AlignSystem resets it on every node update) and the rope lanes keep anchoring on the prefab's sub-lane
// ends, which the bone axis passes through.
//   TrainPitchSystem (Modification4B, after LiftCensusSystem): per tower with a Train bone, the pitch from the rope lanes the engine built
//     (LaneSystem runs at Modification4): every lift-edge rope lane end on the tower's anchors gives its angle along the tower's +z, and the
//     mean is the bisector of the arriving and leaving rope (LiftAlignMath.TrainPitch on the engine's own curves), clamped to the lift
//     type's steepest rope. Kept in TrainPitch (not saved: the first census of a load recomputes every tower).
//   TrainBoneSystem (Rendering, after InitializeBonesSystem): writes the bone. Skeletons exist only near the camera and come back at rest on
//     every (re)allocation (InitializeBonesSystem.cs:57-70, :161-171), and vanilla never writes a None bone (ObjectInterpolateSystem
//     AnimateStaticBone has no case for it), so a Burst job re-applies the rotation each frame where it differs.
// The bone index comes from the mesh's ProceduralAnimationProperties by name (bone names are not kept at runtime; m_Bones order is the
// ProceduralBone order, MeshSystem.cs:505-510); the Skeleton index is the prefab SubMesh index (ObjectInterpolateSystem.cs:344-351).
// Never: Temp or Deleted towers (a preview stays level), towers whose prefab has no Train bone (unskinned art: nothing changes), Editor.
// Log discipline: one line per load naming the rigged tower prefabs, one line per pass that changed anything, a one-shot "!!!" catch.
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
    /// <summary>A rigged line tower's Train bone pose (runtime only, rebuilt from the rope lanes).</summary>
    public struct TrainPitch : IComponentData
    {
        public quaternion m_Rotation;   // the bone's local rotation: rest turned by the pitch (LiftAlignMath.TrainBoneRotation)
        public int m_SubMesh;           // prefab SubMesh index = Skeleton index of the mesh holding the bone
        public int m_Bone;              // bone index in that mesh
        public float m_Pitch;           // radians, nose up along the tower's +z (diagnostics)
    }

    public sealed partial class TrainPitchSystem : GameSystemBase
    {
        /// <summary>A lane end this close to one of the tower's anchors is that anchor's rope (FindAnchor snaps exactly, U1: 0.0000 m).</summary>
        internal const float AnchorMatchM = 0.25f;

        /// <summary>Below this change, radians, a pass leaves a tower's pose alone (no rewrite churn on re-runs).</summary>
        internal const float PitchEpsilon = 1e-4f;

        private struct Rig
        {
            public int SubMesh, Bone;   // SubMesh −1: the prefab has no Train bone
            public quaternion Rest;
        }

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_UpdatedEdges;
        private bool m_Armed, m_Disabled, m_Dirty;
        private string m_DirtyWhy = "load";
        private readonly List<LiftCensus.Edge> m_Edges = new List<LiftCensus.Edge>();
        private readonly Dictionary<Entity, Rig> m_Rigs = new Dictionary<Entity, Rig>();
        private readonly HashSet<Entity> m_SeenNodes = new HashSet<Entity>();
        private readonly List<float3> m_Anchors = new List<float3>(8);
        private bool m_RigLogged;
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
            m_RigLogged = false;
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
            int towers = 0, posed = 0, added = 0, noRope = 0;
            float maxDeg = 0f;
            for (int i = 0; i < m_Edges.Count; i++)
            {
                LiftCensus.Edge ce = m_Edges[i];
                Entity edge = LiftCensusSystem.Ent(ce.Id);
                if (ce.Type == LiftCensus.NoType || ce.Type >= SkiCatalog.Lifts.Count || !EntityManager.Exists(edge) ||
                    EntityManager.HasComponent<Deleted>(edge) || !EntityManager.TryGetComponent(edge, out Edge e))
                    continue;                             // a newer census build carries the replacement
                float maxPitch = LiftAlignMath.MaxTrainPitch(SkiCatalog.Lifts[ce.Type].Envelope.SlopeMax);
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
                        Rig rig = RigOf(pr.m_Prefab);
                        if (rig.SubMesh < 0)
                            continue;
                        towers++;
                        if (!PitchAt(node, pr.m_Prefab, t, out float pitch))
                        {
                            noRope++;
                            continue;
                        }
                        pitch = math.clamp(pitch, -maxPitch, maxPitch);
                        maxDeg = math.max(maxDeg, math.abs(math.degrees(pitch)));
                        var pose = new TrainPitch
                        {
                            m_Rotation = LiftAlignMath.TrainBoneRotation(rig.Rest, pitch),
                            m_SubMesh = rig.SubMesh,
                            m_Bone = rig.Bone,
                            m_Pitch = pitch,
                        };
                        if (EntityManager.TryGetComponent(tower, out TrainPitch old))
                        {
                            if (math.abs(old.m_Pitch - pitch) < PitchEpsilon)
                                continue;
                            EntityManager.SetComponentData(tower, pose);
                        }
                        else
                        {
                            EntityManager.AddComponentData(tower, pose);
                            added++;
                        }
                        posed++;
                    }
                }
            }
            if (posed > 0 || noRope > 0)
            {
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                Mod.Log.Info($"Towers [trains]: pass {m_Passes} ({why}) rigged towers {towers}, posed {posed} (new {added}), max |pitch| " +
                             $"{maxDeg:F1} deg" + (noRope > 0 ? $", {noRope} without a rope lane on their anchors (level)" : string.Empty) +
                             $"; {ms:F2} ms");
            }
        }

        /// <summary>Mean lane-end pitch over the rope lane ends on the tower's anchors; false when no lane end sits on an anchor.</summary>
        private bool PitchAt(Entity node, Entity towerPrefab, Transform t, out float pitch)
        {
            pitch = 0f;
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
            float sum = 0f;
            int n = 0;
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
                    {
                        sum += LiftAlignMath.LaneEndPitch(c.m_Bezier.a, c.m_Bezier.b, forward);
                        n++;
                    }
                    if (OnAnchor(c.m_Bezier.d))
                    {
                        sum += LiftAlignMath.LaneEndPitch(c.m_Bezier.d, c.m_Bezier.c, forward);
                        n++;
                    }
                }
            }
            if (n == 0)
                return false;
            pitch = sum / n;
            return true;
        }

        private bool OnAnchor(float3 p)
        {
            for (int i = 0; i < m_Anchors.Count; i++)
                if (math.distancesq(m_Anchors[i], p) < AnchorMatchM * AnchorMatchM)
                    return true;
            return false;
        }

        /// <summary>The prefab's Train bone, resolved once per prefab per load from the mesh prefabs (main thread).</summary>
        private Rig RigOf(Entity prefab)
        {
            if (m_Rigs.TryGetValue(prefab, out Rig rig))
                return rig;
            rig = new Rig { SubMesh = -1, Bone = -1, Rest = quaternion.identity };
            if (EntityManager.TryGetBuffer(prefab, true, out DynamicBuffer<SubMesh> meshes))
                for (int j = 0; j < meshes.Length && rig.SubMesh < 0; j++)
                {
                    Entity mesh = meshes[j].m_SubMesh;
                    if (!m_PrefabSystem.TryGetPrefab(mesh, out RenderPrefab render) ||
                        !EntityManager.TryGetBuffer(mesh, true, out DynamicBuffer<ProceduralBone> bones))
                        continue;
                    ProceduralAnimationProperties props = render.GetComponent<ProceduralAnimationProperties>();
                    if (props == null || props.m_Bones == null || props.m_Bones.Length != bones.Length)
                        continue;
                    for (int k = 0; k < props.m_Bones.Length; k++)
                        if (string.Equals(props.m_Bones[k].name, "Train", StringComparison.Ordinal))
                        {
                            rig = new Rig { SubMesh = j, Bone = k, Rest = bones[k].m_Rotation };
                            break;
                        }
                }
            m_Rigs[prefab] = rig;
            if (rig.SubMesh >= 0)
            {
                Mod.Log.Info($"Towers [trains]: {m_PrefabSystem.GetPrefabName(prefab)} has a Train bone (sub-mesh {rig.SubMesh}, bone {rig.Bone})" +
                             (m_RigLogged ? string.Empty : "; its trains pitch with the rope"));
                m_RigLogged = true;
            }
            return rig;
        }
    }

    public sealed partial class TrainBoneSystem : GameSystemBase
    {
        [BurstCompile]
        private struct WriteBonesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<TrainPitch> PitchType;
            public BufferTypeHandle<Skeleton> SkeletonType;
            public BufferTypeHandle<Bone> BonesType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<TrainPitch> pitches = chunk.GetNativeArray(ref PitchType);
                BufferAccessor<Skeleton> skeletons = chunk.GetBufferAccessor(ref SkeletonType);
                BufferAccessor<Bone> bones = chunk.GetBufferAccessor(ref BonesType);
                for (int i = 0; i < chunk.Count; i++)
                {
                    TrainPitch p = pitches[i];
                    DynamicBuffer<Skeleton> skel = skeletons[i];
                    if (p.m_SubMesh >= skel.Length)
                        continue;                         // not near the camera: no skeleton (InitializeBonesSystem Remove)
                    ref Skeleton s = ref skel.ElementAt(p.m_SubMesh);
                    if (s.m_BufferAllocation.Empty || s.m_BoneOffset < 0 || s.m_BoneOffset + p.m_Bone >= bones[i].Length)
                        continue;
                    ref Bone b = ref bones[i].ElementAt(s.m_BoneOffset + p.m_Bone);
                    if (b.m_Rotation.Equals(p.m_Rotation))
                        continue;
                    b.m_Rotation = p.m_Rotation;
                    s.m_CurrentUpdated = true;            // ProceduralUploadSystem uploads it this frame
                }
            }
        }

        private EntityQuery m_Query;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TrainPitch>(), ComponentType.ReadWrite<Skeleton>(), ComponentType.ReadWrite<Bone>() },
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
                PitchType = GetComponentTypeHandle<TrainPitch>(true),
                SkeletonType = GetBufferTypeHandle<Skeleton>(false),
                BonesType = GetBufferTypeHandle<Bone>(false),
            }.Schedule(m_Query, Dependency);
        }
    }
}
