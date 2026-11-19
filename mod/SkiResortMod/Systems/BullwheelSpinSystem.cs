// T256: a station's bullwheel turns with its lift's rope. The wheel is a bone of the station mesh named "Bullwheel*" (bone type None,
// rest rotation identity, local Y = the axle). Vanilla never writes a None bone (Game.Rendering/ObjectInterpolateSystem.cs:796-1056
// has no case for it), so the pose is ours, exactly like the tower trains (TrainPitchSystem / TrainBoneSystem, DECISIONS 570).
//   BullwheelSpinSystem (Modification4B, after ChairPinSystem): at every loop cache build it reads the loops' station wheels
//     (ChairPinSystem.CollectWheelDrives: radius and turn sense from the loop's own arc), resolves the wheel bones of each station
//     prefab by name and keeps one slot per station. Each frame it advances every slot's angle by the rope's travel on the mover's
//     clock (BullwheelMath.Advance): rim speed = rope speed, a held loop's wheel stands (no power, closed), a paused game stands, a
//     resume goes on from the same angle. The angles go to a native array. No ECS write per frame.
//   BullwheelBoneSystem (Rendering, after InitializeBonesSystem): a Burst job writes rest x RotateY(angle) to the bones of every
//     station whose skeleton is allocated (stations in draw distance: InitializeBonesSystem.cs:52-86 clears the buffers of the rest)
//     and flags the skeleton for upload (ProceduralUploadSystem.cs:129-142).
// A station mesh without a Bullwheel bone (art before T256, the tram until its round) is skipped: nothing changes for it.
// Runtime data only: BullwheelBone is not serialized, the first loop build of a load adds it again.
// Never: Editor (no loop cache there), Temp stations (not in a loop).
// Log discipline: one line per station prefab with wheel bones per load, one line per build whose wheel counts changed, a one-shot
// "!!!" catch that disables the system for the load (the wheels keep their last angle).
// Mod.cs: UpdateAfter<BullwheelSpinSystem, ChairPinSystem>(Modification4B).
// Mod.cs: UpdateAfter<BullwheelBoneSystem, InitializeBonesSystem>(Rendering).
// Engine notes: EngineNotes/BullwheelSpinSystem.md

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>One wheel bone of a station (runtime only: rebuilt from the loop cache at each build).</summary>
    [InternalBufferCapacity(2)]
    public struct BullwheelBone : IBufferElementData
    {
        public quaternion m_Rest;   // the bone's rest rotation (ProceduralBone.m_Rotation)
        public int m_SubMesh;       // prefab SubMesh index = Skeleton index of the mesh holding the bone
        public int m_Bone;          // bone index in that mesh (the sorted ProceduralBone slot)
        public int m_Slot;          // index into BullwheelSpinSystem's angles, -1 = not driven (the wheel keeps its pose)
    }

    public sealed partial class BullwheelSpinSystem : GameSystemBase
    {
        private struct RigBone
        {
            public int SubMesh, Bone;
            public quaternion Rest;
        }

        private struct Slot
        {
            public Entity Station;
            public int Loop;
            public float RadiusM;
            public int Sign;
            public double Angle;
        }

        private PrefabSystem m_PrefabSystem;
        private ChairPinSystem m_ChairPin;
        private BullwheelBoneSystem m_BoneSystem;
        private bool m_Armed, m_Disabled;
        private int m_SeenBuilds = -1;
        private double m_LastClockS = -1.0;
        private readonly List<ChairPinSystem.WheelDrive> m_Drives = new List<ChairPinSystem.WheelDrive>();
        private readonly List<Slot> m_Slots = new List<Slot>();
        private readonly Dictionary<Entity, List<RigBone>> m_Rigs = new Dictionary<Entity, List<RigBone>>();   // null: no wheel bone
        private readonly Dictionary<Entity, double> m_AngleByStation = new Dictionary<Entity, double>();
        private readonly HashSet<Entity> m_Driven = new HashSet<Entity>();      // stations that carry a BullwheelBone buffer of ours
        private readonly HashSet<Entity> m_DrivenNow = new HashSet<Entity>();
        private NativeArray<float> m_Angles;
        private int m_LoggedWheels = -1, m_LoggedBare = -1;

        /// <summary>The slots' angles, radians, for BullwheelBoneSystem's job (read only there).</summary>
        internal NativeArray<float> Angles => m_Angles;

        /// <summary>Wheels turning now (the dev dump and the tests' in-game twin).</summary>
        internal int WheelCount => m_Slots.Count;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ChairPin = World.GetOrCreateSystemManaged<ChairPinSystem>();
            m_BoneSystem = World.GetOrCreateSystemManaged<BullwheelBoneSystem>();
            m_Angles = new NativeArray<float>(0, Allocator.Persistent);
            Mod.Log.Info($"{nameof(BullwheelSpinSystem)} created");
        }

        protected override void OnDestroy()
        {
            m_BoneSystem?.CompleteWrite();
            if (m_Angles.IsCreated)
                m_Angles.Dispose();
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_BoneSystem?.CompleteWrite();
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_SeenBuilds = -1;
            m_LastClockS = -1.0;
            m_Drives.Clear();
            m_Slots.Clear();
            m_Rigs.Clear();
            m_AngleByStation.Clear();
            m_Driven.Clear();
            m_LoggedWheels = m_LoggedBare = -1;
            Resize(0);
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                // The job of the last frame reads m_Angles: complete it before any write or resize (PIPELINE "per-frame work").
                m_BoneSystem.CompleteWrite();
                if (m_ChairPin.WheelBuilds != m_SeenBuilds)
                {
                    m_SeenBuilds = m_ChairPin.WheelBuilds;
                    Rebuild();
                }
                double clock = m_ChairPin.WheelClockS;
                double dt = m_LastClockS >= 0.0 ? clock - m_LastClockS : 0.0;
                m_LastClockS = clock;
                for (int i = 0; i < m_Slots.Count; i++)
                {
                    Slot s = m_Slots[i];
                    s.Angle = BullwheelMath.Advance(s.Angle, m_ChairPin.WheelSpeedMps(s.Loop), dt, s.RadiusM, s.Sign);
                    m_Slots[i] = s;
                    m_Angles[i] = (float)s.Angle;
                }
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! Lifts [wheels]: {e.GetType().Name}: {e.Message} — disabled for this load (bullwheels keep their last " +
                              $"angle)\n{e}");
            }
        }

        /// <summary>Reads the wheels of the new loop cache. Structural changes (the BullwheelBone buffers) happen here only, never per
        /// frame. A station keeps its angle across builds.</summary>
        private void Rebuild()
        {
            for (int i = 0; i < m_Slots.Count; i++)
                m_AngleByStation[m_Slots[i].Station] = m_Slots[i].Angle;
            m_Slots.Clear();
            m_DrivenNow.Clear();
            m_ChairPin.CollectWheelDrives(m_Drives);
            int bare = 0;
            for (int i = 0; i < m_Drives.Count; i++)
            {
                ChairPinSystem.WheelDrive d = m_Drives[i];
                if (!EntityManager.Exists(d.Station) || EntityManager.HasComponent<Deleted>(d.Station) ||
                    !EntityManager.TryGetComponent(d.Station, out PrefabRef prefab))
                    continue;
                List<RigBone> rig = RigOf(prefab.m_Prefab);
                if (rig == null)
                {
                    bare++;
                    continue;
                }
                if (!m_DrivenNow.Add(d.Station))
                    continue;   // a station in two loops (never by construction): the first loop drives its wheel
                int slot = m_Slots.Count;
                m_Slots.Add(new Slot
                {
                    Station = d.Station, Loop = d.Loop, RadiusM = d.RadiusM, Sign = d.Sign,
                    Angle = m_AngleByStation.TryGetValue(d.Station, out double a) ? a : 0.0,
                });
                WriteBones(d.Station, rig, slot);
            }
            // A station that left every loop (its line was removed): its bones are no longer driven
            foreach (Entity station in m_Driven)
            {
                if (m_DrivenNow.Contains(station) || !EntityManager.Exists(station) ||
                    !EntityManager.TryGetBuffer(station, false, out DynamicBuffer<BullwheelBone> old))
                    continue;
                for (int k = 0; k < old.Length; k++)
                {
                    BullwheelBone b = old[k];
                    b.m_Slot = -1;
                    old[k] = b;
                }
            }
            m_Driven.Clear();
            foreach (Entity station in m_DrivenNow)
                m_Driven.Add(station);
            Resize(m_Slots.Count);
            for (int i = 0; i < m_Slots.Count; i++)
                m_Angles[i] = (float)m_Slots[i].Angle;

            if (m_Slots.Count != m_LoggedWheels || bare != m_LoggedBare)
            {
                m_LoggedWheels = m_Slots.Count;
                m_LoggedBare = bare;
                Mod.Log.Info($"Lifts [wheels]: build {m_SeenBuilds}: {m_Slots.Count} bullwheel(s) turn with their rope" +
                             (bare > 0 ? $", {bare} station wheel(s) without a Bullwheel bone in the mesh (they stand)" : string.Empty));
            }
        }

        private void WriteBones(Entity station, List<RigBone> rig, int slot)
        {
            DynamicBuffer<BullwheelBone> bones = EntityManager.HasBuffer<BullwheelBone>(station)
                ? EntityManager.GetBuffer<BullwheelBone>(station)
                : EntityManager.AddBuffer<BullwheelBone>(station);
            bones.Clear();
            for (int k = 0; k < rig.Count; k++)
                bones.Add(new BullwheelBone { m_Rest = rig[k].Rest, m_SubMesh = rig[k].SubMesh, m_Bone = rig[k].Bone, m_Slot = slot });
        }

        private void Resize(int count)
        {
            if (m_Angles.IsCreated && m_Angles.Length == count)
                return;
            if (m_Angles.IsCreated)
                m_Angles.Dispose();
            m_Angles = new NativeArray<float>(count, Allocator.Persistent);
        }

        /// <summary>The prefab's wheel bones ("Bullwheel*"), resolved once per prefab per load from the mesh prefabs (main thread). Null
        /// when it has none. The engine sorts the ProceduralBone buffer (MeshSystem.cs:162-164, :237), so the slot is the one whose
        /// m_BindIndex is the bone's index in ProceduralAnimationProperties.m_Bones (the TrainPitchSystem.RigOf idiom).</summary>
        private List<RigBone> RigOf(Entity prefab)
        {
            if (m_Rigs.TryGetValue(prefab, out List<RigBone> rig))
                return rig;
            rig = null;
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
                        if (!BullwheelMath.IsWheelBone(props.m_Bones[k].name))
                            continue;
                        for (int b = 0; b < bones.Length; b++)
                            if (bones[b].m_BindIndex == k)
                            {
                                (rig ??= new List<RigBone>()).Add(new RigBone { SubMesh = j, Bone = b, Rest = bones[b].m_Rotation });
                                break;
                            }
                    }
                }
            m_Rigs[prefab] = rig;
            if (rig != null)
                Mod.Log.Info($"Lifts [wheels]: {m_PrefabSystem.GetPrefabName(prefab)} has {rig.Count} bullwheel bone(s); they turn with " +
                             "the rope");
            return rig;
        }
    }

    public sealed partial class BullwheelBoneSystem : GameSystemBase
    {
        [BurstCompile]
        private struct WriteWheelsJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<BullwheelBone> WheelType;
            [ReadOnly] public NativeArray<float> Angles;
            public BufferTypeHandle<Skeleton> SkeletonType;
            public BufferTypeHandle<Bone> BonesType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                BufferAccessor<BullwheelBone> wheels = chunk.GetBufferAccessor(ref WheelType);
                BufferAccessor<Skeleton> skeletons = chunk.GetBufferAccessor(ref SkeletonType);
                BufferAccessor<Bone> bones = chunk.GetBufferAccessor(ref BonesType);
                for (int i = 0; i < chunk.Count; i++)
                {
                    DynamicBuffer<BullwheelBone> wheel = wheels[i];
                    DynamicBuffer<Skeleton> skel = skeletons[i];
                    DynamicBuffer<Bone> bone = bones[i];
                    for (int k = 0; k < wheel.Length; k++)
                    {
                        BullwheelBone w = wheel[k];
                        if (w.m_Slot < 0 || w.m_Slot >= Angles.Length || w.m_SubMesh >= skel.Length)
                            continue;                     // not driven, or not in draw distance: no skeleton
                        ref Skeleton s = ref skel.ElementAt(w.m_SubMesh);
                        if (s.m_BufferAllocation.Empty || s.m_BoneOffset < 0 || s.m_BoneOffset + w.m_Bone >= bone.Length)
                            continue;
                        quaternion rotation = BullwheelMath.BoneRotation(w.m_Rest, Angles[w.m_Slot]);
                        ref Bone b = ref bone.ElementAt(s.m_BoneOffset + w.m_Bone);
                        if (b.m_Rotation.Equals(rotation))
                            continue;                     // a standing wheel uploads nothing
                        b.m_Rotation = rotation;
                        s.m_CurrentUpdated = true;        // ProceduralUploadSystem uploads it this frame
                    }
                }
            }
        }

        private EntityQuery m_Query;
        private BullwheelSpinSystem m_Spin;
        private JobHandle m_Write;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<BullwheelBone>(), ComponentType.ReadWrite<Skeleton>(), ComponentType.ReadWrite<Bone>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            RequireForUpdate(m_Query);
            m_Spin = World.GetOrCreateSystemManaged<BullwheelSpinSystem>();
            // Job reflection data and JIT before the first Schedule (the PrewarmJit precedent). Schedules nothing.
            JobChunkExtensions.EarlyJobInit<WriteWheelsJob>();
            Mod.Log.Info($"{nameof(BullwheelBoneSystem)} created");
        }

        /// <summary>Completes the bone job of the last update. BullwheelSpinSystem calls it before it writes or resizes the angles.</summary>
        internal void CompleteWrite()
        {
            m_Write.Complete();
            m_Write = default;
        }

        protected override void OnUpdate()
        {
            NativeArray<float> angles = m_Spin.Angles;
            if (!angles.IsCreated || angles.Length == 0)
                return;
            Dependency = new WriteWheelsJob
            {
                WheelType = GetBufferTypeHandle<BullwheelBone>(true),
                Angles = angles,
                SkeletonType = GetBufferTypeHandle<Skeleton>(false),
                BonesType = GetBufferTypeHandle<Bone>(false),
            }.Schedule(m_Query, Dependency);
            m_Write = Dependency;
        }
    }
}
