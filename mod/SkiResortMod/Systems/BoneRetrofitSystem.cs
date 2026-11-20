// T256 F1 (P1 field test, 2026-11-20): an old save's lift stations get the skeleton that their new mesh needs.
// Why: the game gives an object Skeleton + Bone + BoneHistory only when its prefab archetype is built from a mesh with
// ProceduralAnimationProperties (Game.Prefabs/ObjectGeometryPrefab.cs:92-95, 157-161, decompile 1.6.4f1). A loaded entity keeps the
// archetype of its save, and vanilla's RequiredComponentSystem adds only BoneHistory to entities that already have Bone
// (Game.Serialization/RequiredComponentSystem.cs:494, 1187-1190), never a skeleton. So a station placed with a mesh without bones
// (the FG O stations, ET62P) and loaded with a mesh that has bones (P, ET62Q) has no skeleton: the bullwheel and nose-train bones
// are never uploaded and the art stays in its rest pose. Measured on the 1.0.3 Ash Flat save: 4 / 4 FG stations and the hosted
// SkiLiftTerminalTowerTop had no Skeleton after the load.
// What: once per load, in the Deserialize phase right after RequiredComponentSystem (before the batch and culling post-deserialize
// steps, SystemOrder.cs:803, :865, :891), ONE structural call adds the three buffers to every object of RetrofitPrefabs that has no
// Skeleton, when that prefab's mesh now carries a bone one of our writers drives ("Bullwheel*", BullwheelBoneSystem; "NoseTrain*",
// "TrainFront*", "TrainBack*", "Head*", TrainBoneSystem). A prefab whose mesh has no driven bone gets nothing: no empty markers go
// into saves. The result is the state vanilla loads for a skeleton object from a save (all three are IEmptySerializable: the buffers
// load empty, InitializeBonesSystem sizes them near the camera, InitializeBonesSystem.cs:59, 129-146; critic-f1 checked the readers).
// Never in a running city: a run-time add (Skeleton + Bone without BoneHistory, from a bridge op at UIUpdate) exited the game in the
// P1 probe. Its exact path is UNPROVEN (the Updated tag of that op is gone before the next culling pass, critic-f1). A second reason
// holds without it: BatchDataSystem.cs:734-741 reads Skeleton[meshIndex] with no length check, so a skeleton added after the load and
// not yet sized is read out of range at the next full property update.
// Scope: the exact prefabs in RetrofitPrefabs (ours; ARCHSWAP fails a released prefab that gains bones and is not listed). No other
// object is touched. Safe to repeat: the query excludes entities that already have a Skeleton.
// Log discipline: one "Lifts [retrofit]:" line per Deserialize run (count, prefabs, scan ms). In the same update every added entity
// must show all three buffers, else one "!!!" line. BoneRetrofitVerifySystem checks them again VerifyAfterUpdates main-loop updates
// later: one OK line, or one WARN. Off switches: the option LiftStationRetrofit (Options > Lifts, read here at each load, logged when
// off) and an exception (one "!!!" line, nothing added, no check, until the next load). With either off, the stations keep their rest
// pose (the changelog's known-issue fallback); a city resaved with the markers loads the same with the option off.
// Mod.cs: UpdateAfter<BoneRetrofitSystem, Game.Serialization.RequiredComponentSystem>(Deserialize), UpdateAt<BoneRetrofitVerifySystem>
// (Modification1).

using System;
using System.Diagnostics;
using Colossal.Entities;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class BoneRetrofitSystem : GameSystemBase
    {
        /// <summary>The released prefabs whose station mesh gained driven bones in 1.0.4 (type name, prefab name). tools/check-archetype-
        /// swap.js reads this list: a released prefab whose meshes gain bones must be here.</summary>
        internal static readonly (string Type, string Name)[] RetrofitPrefabs =
        {
            (nameof(BuildingPrefab), "SkiBaseStationBuilding"),
            (nameof(BuildingPrefab), "SkiTopStationBuilding"),
            (nameof(StaticObjectPrefab), "SkiLiftTerminalTowerTop"),
        };

        private PrefabSystem m_PrefabSystem;
        private BoneRetrofitVerifySystem m_Verify;
        private EntityQuery m_Query;

        /// <summary>A bone name that one of our bone writers drives (BullwheelBoneSystem, TrainBoneSystem).</summary>
        internal static bool IsDrivenBone(string name)
        {
            return name != null && (BullwheelMath.IsWheelBone(name) ||
                                    name.StartsWith(TrainPitchSystem.NosePrefix, StringComparison.Ordinal) ||
                                    name.StartsWith(TrainPitchSystem.FrontPrefix, StringComparison.Ordinal) ||
                                    name.StartsWith(TrainPitchSystem.BackPrefix, StringComparison.Ordinal) ||
                                    name.StartsWith(TrainPitchSystem.HeadPrefix, StringComparison.Ordinal));
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Verify = World.GetOrCreateSystemManaged<BoneRetrofitVerifySystem>();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<ObjectGeometry>() },
                None = new[] { ComponentType.ReadOnly<Skeleton>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            });
            Mod.Log.Info($"{nameof(BoneRetrofitSystem)} created");
        }

        protected override void OnUpdate()
        {
            m_Verify.Disarm();
            if (!SkiResortSettings.LiftStationRetrofitOn)
            {
                Mod.Log.Info("Lifts [retrofit]: off (option \"Moving parts for older lift stations\"); stations placed before 1.0.4 keep a " +
                             "still bullwheel");
                return;
            }
            var targets = new NativeList<Entity>(RetrofitPrefabs.Length, Allocator.Temp);
            var fix = new NativeList<Entity>(Allocator.Temp);
            try
            {
                Stopwatch watch = Stopwatch.StartNew();
                for (int t = 0; t < RetrofitPrefabs.Length; t++)
                    if (m_PrefabSystem.TryGetPrefab(new PrefabID(RetrofitPrefabs[t].Type, RetrofitPrefabs[t].Name), out PrefabBase p) &&
                        p != null && m_PrefabSystem.TryGetEntity(p, out Entity e) && HasDrivenBones(e))
                        targets.Add(e);
                if (targets.Length == 0)
                {
                    Mod.Log.Info($"Lifts [retrofit]: 0 of {RetrofitPrefabs.Length} station prefabs carry driven bones; nothing scanned");
                    return;
                }

                var perTarget = new int[targets.Length];
                CompleteDependency();
                EntityTypeHandle entityHandle = GetEntityTypeHandle();
                ComponentTypeHandle<PrefabRef> prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                using (NativeArray<ArchetypeChunk> chunks = m_Query.ToArchetypeChunkArray(Allocator.Temp))
                {
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        NativeArray<PrefabRef> refs = chunks[c].GetNativeArray(ref prefabRefHandle);
                        NativeArray<Entity> entities = chunks[c].GetNativeArray(entityHandle);
                        for (int i = 0; i < refs.Length; i++)
                        {
                            Entity prefab = refs[i].m_Prefab;
                            for (int t = 0; t < targets.Length; t++)
                                if (targets[t] == prefab)
                                {
                                    fix.Add(entities[i]);
                                    perTarget[t]++;
                                    break;
                                }
                        }
                    }
                }

                if (fix.Length == 0)
                {
                    Mod.Log.Info($"Lifts [retrofit]: 0 objects needed a skeleton ({targets.Length} station prefab(s) with driven bones, " +
                                 $"scan {watch.Elapsed.TotalMilliseconds:F1} ms)");
                    return;
                }

                EntityManager.AddComponent(fix.AsArray(), new ComponentTypeSet(ComponentType.ReadWrite<Skeleton>(),
                    ComponentType.ReadWrite<Bone>(), ComponentType.ReadWrite<BoneHistory>()));
                int whole = 0;
                for (int i = 0; i < fix.Length; i++)
                    if (EntityManager.HasBuffer<Skeleton>(fix[i]) && EntityManager.HasBuffer<Bone>(fix[i]) &&
                        EntityManager.HasBuffer<BoneHistory>(fix[i]))
                        whole++;
                var parts = new string[targets.Length];
                for (int t = 0; t < targets.Length; t++)
                    parts[t] = $"{m_PrefabSystem.GetPrefabName(targets[t])} x{perTarget[t]}";
                if (whole != fix.Length)
                {
                    Mod.Log.Error($"!!! Lifts [retrofit]: only {whole} of {fix.Length} object(s) show Skeleton + Bone + BoneHistory right " +
                                  $"after the add ({string.Join(", ", parts)})");
                    return;
                }
                m_Verify.Arm(fix.AsArray());
                Mod.Log.Info($"Lifts [retrofit]: {fix.Length} object(s) got the skeleton their new mesh needs (Skeleton + Bone + " +
                             $"BoneHistory; placed before the mesh had driven bones): {string.Join(", ", parts)}; scan + add " +
                             $"{watch.Elapsed.TotalMilliseconds:F1} ms");
            }
            catch (Exception e)
            {
                m_Verify.Disarm();
                Mod.Log.Error($"!!! Lifts [retrofit]: {e.GetType().Name}: {e.Message} — the retrofit is off until the next load " +
                              $"(old stations keep their rest pose; new ones are not affected)\n{e}");
            }
            finally
            {
                fix.Dispose();
                targets.Dispose();
            }
        }

        /// <summary>The prefab has a sub-mesh with a driven bone. The bone names come from the mesh's ProceduralAnimationProperties (bone
        /// names are not kept at runtime; the TrainPitchSystem.RigOf idiom). A ProceduralBone buffer is the archetype rule
        /// (ProceduralAnimationProperties.cs:81).</summary>
        private bool HasDrivenBones(Entity prefab)
        {
            if (!EntityManager.TryGetBuffer(prefab, true, out DynamicBuffer<SubMesh> subMeshes))
                return false;
            for (int k = 0; k < subMeshes.Length; k++)
            {
                Entity mesh = subMeshes[k].m_SubMesh;
                if (!EntityManager.HasBuffer<ProceduralBone>(mesh) || !m_PrefabSystem.TryGetPrefab(mesh, out RenderPrefab render))
                    continue;
                ProceduralAnimationProperties props = render.GetComponent<ProceduralAnimationProperties>();
                if (props == null || props.m_Bones == null)
                    continue;
                for (int b = 0; b < props.m_Bones.Length; b++)
                    if (IsDrivenBone(props.m_Bones[b].name))
                        return true;
            }
            return false;
        }
    }

    /// <summary>The retrofit's post-load check (T256 F1): VerifyAfterUpdates main-loop updates after the load, every retrofitted object
    /// that still exists must have Skeleton + Bone + BoneHistory. One OK line or one WARN line per load, then it sleeps (Enabled = false)
    /// until the next retrofit arms it.</summary>
    public sealed partial class BoneRetrofitVerifySystem : GameSystemBase
    {
        internal const int VerifyAfterUpdates = 10;

        private Entity[] m_Targets = Array.Empty<Entity>();
        private int m_Wait;

        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
        }

        internal void Arm(NativeArray<Entity> targets)
        {
            m_Targets = targets.ToArray();
            m_Wait = VerifyAfterUpdates;
            Enabled = m_Targets.Length > 0;
        }

        internal void Disarm()
        {
            m_Targets = Array.Empty<Entity>();
            Enabled = false;
        }

        protected override void OnUpdate()
        {
            if (--m_Wait > 0)
                return;
            try
            {
                int ok = 0, gone = 0, bad = 0;
                string first = null;
                foreach (Entity e in m_Targets)
                {
                    if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e))
                    {
                        gone++;
                        continue;
                    }
                    bool sk = EntityManager.HasBuffer<Skeleton>(e), bo = EntityManager.HasBuffer<Bone>(e),
                         bh = EntityManager.HasBuffer<BoneHistory>(e);
                    if (sk && bo && bh)
                    {
                        ok++;
                        continue;
                    }
                    bad++;
                    first ??= $"{e} lacks {(sk ? "" : "Skeleton ")}{(bo ? "" : "Bone ")}{(bh ? "" : "BoneHistory")}".TrimEnd();
                }
                if (bad == 0)
                    Mod.Log.Info($"Lifts [retrofit]: check OK, {ok} of {m_Targets.Length} retrofitted object(s) keep Skeleton + Bone + " +
                                 $"BoneHistory ({gone} removed since the load)");
                else
                    Mod.Log.Warn($"Lifts [retrofit]: WARN {bad} of {m_Targets.Length} retrofitted object(s) lost part of the skeleton " +
                                 $"after the load (their wheels and trains may stand still); first: {first}");
            }
            catch (Exception ex)
            {
                Mod.Log.Error($"!!! Lifts [retrofit]: check {ex.GetType().Name}: {ex.Message} — no check this load\n{ex}");
            }
            Disarm();
        }
    }
}
