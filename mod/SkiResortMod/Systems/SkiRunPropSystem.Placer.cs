// The run prop placer: walks a run's border by distance (RunPropPlacerRules) and stands guns, hydrants and masts as free-standing objects
// tagged SkiRunPropMember, never Secondary children vanilla would reap. Rebuilt when a run's outline or slider changes, after the settle
// (RunPropDensityRules.NextStable), diffed against what stands, and spread over frames (MaxOpsPerFrame). Removed in this same phase when the
// run carries Deleted: bulldoze, hub trash, delete mode, the sign cascade and delete-all all mark the area before Modification4B.
// Old saves: the load sweep deletes the run's vanilla corner props once (Secondary + Owner == run); idempotent, they cannot come back
// with the corner chances at 0. Game mode only. House contract: ARMED line, one-shot "!!!" catch, one [cost] line per rebuild burst.
// Engine notes: EngineNotes/SkiRunPropSystem.md, EngineNotes/SkiRunPropMember.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunPropSystem
    {
        /// <summary>Budgets per frame: objects created or deleted, and runs planned. Each run plan is a ring walk (microseconds); a 2 km run at
        /// the closest spacing is about 100 props, so a long run or a slider settle spreads over a few frames.</summary>
        private const int MaxOpsPerFrame = 48;
        private const int MaxPlansPerFrame = 8;

        private enum OpKind : byte { Delete, Create, Applied }

        private struct Op
        {
            public OpKind Kind;
            public Entity Run;
            public Entity Target;     // Delete
            public PlacedProp Prop;   // Create
            public int Key;           // Applied
        }

        private TerrainSystem m_TerrainSystem;
        private EntityQuery m_MemberQuery;
        private EntityQuery m_DeletedAreaQuery;
        private EntityQuery m_FreshRunQuery;
        private EntityQuery m_AllRunQuery;

        // prefab -> RunKind, the kinds the placer can stand props on (session cache, ResolveRunPrefabs).
        private readonly Dictionary<Entity, int> m_RunKindOf = new Dictionary<Entity, int>();

        // RunPropPlacerRules.Props index -> prefab; Null = not found (session cache).
        private Entity[] m_PropPrefabs;
        private readonly Dictionary<Entity, int> m_PropIndexOf = new Dictionary<Entity, int>();
        private bool m_PropsResolved;
        private int m_PropsFound;

        // Per load.
        private bool m_PlacerLive;
        private bool m_PlacerDisabled;
        private bool m_PlacerErrorLogged;
        private bool m_LoadSweepDue;
        private bool m_ChangedThisFrame;
        private int m_Stable;
        private readonly HashSet<Entity> m_Dirty = new HashSet<Entity>();
        private readonly Queue<Entity> m_PlanQueue = new Queue<Entity>();
        private readonly HashSet<Entity> m_PlanQueued = new HashSet<Entity>();
        private readonly Queue<Op> m_Ops = new Queue<Op>();

        // The burst the [cost] line reports: from the first queued run to the last op.
        private bool m_BurstOn;
        private int m_BurstRuns, m_BurstCreated, m_BurstDeleted, m_BurstKept, m_BurstFrames;
        private long m_BurstTicks, m_BurstMaxTicks;

        // Scratch (main thread, reused).
        private readonly List<PlacedProp> m_Plan = new List<PlacedProp>(128);
        private readonly List<StandingProp> m_Standing = new List<StandingProp>(128);
        private readonly List<Entity> m_StandingEntities = new List<Entity>(128);
        private readonly List<int> m_ToCreate = new List<int>(128);
        private readonly List<int> m_ToRemove = new List<int>(128);

        private void CreatePlacerQueries()
        {
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_MemberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SkiRunPropMember>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            // Every area deleted this frame (a vanilla one too, filtered by prefab): Deleted stands until CleanUp, and each delete path marks
            // the area before Modification4B (tool apply, PostTool, the sign cascade in Modification2).
            m_DeletedAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_FreshRunQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Node>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_AllRunQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Node>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
        }

        /// <summary>Per-load setup (own try/catch). Non-Game load: the corner chances go back to the prefab values; the placer stays off.</summary>
        private void LoadPlacer(GameMode mode)
        {
            m_PlacerLive = false;
            m_PlacerDisabled = false;
            m_PlacerErrorLogged = false;
            m_LoadSweepDue = false;
            m_ChangedThisFrame = false;
            m_Stable = 0;
            m_Dirty.Clear();
            m_PlanQueue.Clear();
            m_PlanQueued.Clear();
            m_Ops.Clear();
            m_BurstOn = false;

            try
            {
                if (mode != GameMode.Game)
                {
                    if (m_CornersStopped)
                    {
                        WriteCorners(false);
                        Mod.Log.Info($"Props: corner chances written back to the prefab values ({mode}: no run props outside a game; hygiene only)");
                    }
                    return;
                }

                RecordCorners();
                int stopped = WriteCorners(true);
                ResolveProps();
                if (m_RunKindOf.Count == 0 || m_PropsFound == 0)
                {
                    Mod.Log.Info("Props: placer inert — " + (m_RunKindOf.Count == 0 ? "no run prefab with props" : "no prop prefab found") +
                                 " (expected pre-import)");
                    return;
                }
                m_PlacerLive = true;
                m_LoadSweepDue = true;
                Mod.Log.Info($"Props: placer ARMED — walks {m_RunKindOf.Count} run kind(s) by distance, {m_PropsFound} of " +
                             $"{RunPropPlacerRules.Props.Length} prop prefab(s) found; per-run sliders Off or " +
                             $"{RunPropPlacerRules.SpacingMinM}..{RunPropPlacerRules.SpacingMaxM} m step {RunPropPlacerRules.SpacingStepM}; defaults guns " +
                             $"snowmaking {RunPropPlacerRules.GunSpacingSnowmakingM} m, night {RunPropPlacerRules.GunSpacingNightM} m, cat track " +
                             $"{RunPropPlacerRules.GunSpacingCatTrackM} m, lights night {RunPropPlacerRules.LightSpacingNightM} m, cat track off; " +
                             $"{m_CornerEntries.Count} vanilla corner entr(ies) at chance 0 ({stopped} changed); re-places {RunPropDensityRules.SettleFrames} " +
                             $"frames after a change, at most {MaxOpsPerFrame} objects and {MaxPlansPerFrame} plans per frame (look only: snow and upkeep " +
                             "never read a slider)");
            }
            catch (Exception e)
            {
                PlacerFailed(e);
            }
        }

        private void UpdatePlacer()
        {
            if (!m_PlacerLive || m_PlacerDisabled)
                return;
            try
            {
                long t0 = Stopwatch.GetTimestamp();
                CascadeDeletes();
                if (!ResortPresence.Present)
                    return;
                if (m_LoadSweepDue)
                    LoadSweep();
                NoteFreshRuns();
                Settle();
                StepQueue(t0);
            }
            catch (Exception e)
            {
                PlacerFailed(e);
            }
        }

        /// <summary>The placer's catch: one "!!!" per load, placer off until the next game load; standing props stay, the bridge keeps working.</summary>
        private void PlacerFailed(Exception e)
        {
            m_PlacerDisabled = true;
            m_Ops.Clear();
            m_PlanQueue.Clear();
            m_PlanQueued.Clear();
            if (m_PlacerErrorLogged)
                return;
            m_PlacerErrorLogged = true;
            Mod.Log.Error($"!!! {nameof(SkiRunPropSystem)} placer EXCEPTION — run props frozen until the next game load (runs keep the props " +
                          $"they have). Please report this stack:\n{e}");
        }

        // --- For the hub -----------------------------------------------------------------------------------------------------------

        /// <summary>A run's spacings for the hub's row: meters, 0 = off, -1 = this kind carries none. False when the run is not one of the
        /// kinds the placer walks.</summary>
        internal bool TryGetSpacing(Entity run, out int gunM, out int lightM)
        {
            gunM = lightM = -1;
            if (!EntityManager.Exists(run) || !EntityManager.HasComponent<PrefabRef>(run) ||
                !m_RunKindOf.TryGetValue(EntityManager.GetComponentData<PrefabRef>(run).m_Prefab, out int kind))
                return false;
            bool has = EntityManager.HasComponent<SkiRunPropSettings>(run);
            SkiRunPropSettings settings = has ? EntityManager.GetComponentData<SkiRunPropSettings>(run) : default;
            if (RunPropPlacerRules.CarriesGuns(kind))
                gunM = has ? settings.m_GunSpacingM : RunPropPlacerRules.DefaultGunSpacing(kind);
            if (RunPropPlacerRules.CarriesLights(kind))
                lightM = has ? settings.m_LightSpacingM : RunPropPlacerRules.DefaultLightSpacing(kind);
            return true;
        }

        /// <summary>A hub slider step. A plain component write (no structural change, nothing deleted): the placer re-places after the settle.
        /// False when the run or the kind cannot take it, or the placer is off.</summary>
        internal bool SetSpacing(Entity run, bool lights, int meters)
        {
            if (!m_PlacerLive || m_PlacerDisabled || !EntityManager.Exists(run) || EntityManager.HasComponent<Deleted>(run) ||
                !EntityManager.HasComponent<SkiRunPropSettings>(run) || !EntityManager.HasComponent<PrefabRef>(run) ||
                !m_RunKindOf.TryGetValue(EntityManager.GetComponentData<PrefabRef>(run).m_Prefab, out int kind))
                return false;
            if (lights ? !RunPropPlacerRules.CarriesLights(kind) : !RunPropPlacerRules.CarriesGuns(kind))
                return false;
            SkiRunPropSettings settings = EntityManager.GetComponentData<SkiRunPropSettings>(run);
            int value = RunPropPlacerRules.ClampSpacing(meters);
            if (lights)
                settings.m_LightSpacingM = value;
            else
                settings.m_GunSpacingM = value;
            EntityManager.SetComponentData(run, settings);
            NoteRun(run, kind, settings);
            return true;
        }

        // --- Deletes -------------------------------------------------------------------------------------------------------------------

        /// <summary>This frame's deleted runs take their props along: every delete path ends in a Deleted tag on the area, and props deleted
        /// here reach the search tree at Modification5 like vanilla's own (SecondaryObjectSystem deletes in this phase too).</summary>
        private void CascadeDeletes()
        {
            if (m_DeletedAreaQuery.IsEmptyIgnoreFilter)
                return;
            HashSet<Entity> gone = null;
            using (var areas = m_DeletedAreaQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < areas.Length; i++)
                {
                    if (!m_RunKindOf.ContainsKey(EntityManager.GetComponentData<PrefabRef>(areas[i]).m_Prefab))
                        continue;
                    (gone ??= new HashSet<Entity>()).Add(areas[i]);
                    m_Dirty.Remove(areas[i]);
                }
            }
            if (gone == null)
                return;
            int deleted = DeleteMembersWhere((run, exists) => gone.Contains(run));
            Mod.Log.Info($"Props: {gone.Count} run(s) deleted, {deleted} placed prop(s) with them");
        }

        private int DeleteMembersWhere(Func<Entity, bool, bool> match)
        {
            int deleted = 0;
            using (var members = m_MemberQuery.ToEntityArray(Allocator.Temp))
            using (var tags = m_MemberQuery.ToComponentDataArray<SkiRunPropMember>(Allocator.Temp))
            {
                for (int i = 0; i < members.Length; i++)
                {
                    Entity run = tags[i].m_Run;
                    if (!match(run, run != Entity.Null && EntityManager.Exists(run)))
                        continue;
                    RunAreaEmit.DeleteLikeBulldozer(EntityManager, members[i]);
                    deleted++;
                }
            }
            return deleted;
        }

        // --- Load sweep ----------------------------------------------------------------------------------------------------------------

        /// <summary>Once per load, on the first frame with a resort: an old save's corner props go; orphan tags go; every run of a walked kind
        /// gets its settings and is compared with what stands. Saved props whose key still matches stay untouched.</summary>
        private void LoadSweep()
        {
            m_LoadSweepDue = false;
            int runs = 0, convertedRuns = 0, guns = 0, hydrants = 0, masts = 0, empties = 0, others = 0, added = 0;
            var corner = new List<Entity>();
            var carried = new List<Entity>();
            using (var areas = m_AllRunQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < areas.Length; i++)
                {
                    Entity run = areas[i];
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(run).m_Prefab;
                    if (!m_RunPrefabs.Contains(prefab))
                        continue;
                    runs++;
                    if (m_RunKindOf.ContainsKey(prefab))
                        carried.Add(run);
                    if (!EntityManager.HasBuffer<Game.Objects.SubObject>(run))
                        continue;
                    DynamicBuffer<Game.Objects.SubObject> buffer = EntityManager.GetBuffer<Game.Objects.SubObject>(run, isReadOnly: true);
                    int before = corner.Count;
                    for (int k = 0; k < buffer.Length; k++)
                    {
                        Entity child = buffer[k].m_SubObject;
                        if (!EntityManager.Exists(child) || EntityManager.HasComponent<Deleted>(child) || EntityManager.HasComponent<Temp>(child) ||
                            !EntityManager.HasComponent<Game.Objects.Secondary>(child) || !EntityManager.HasComponent<Owner>(child) ||
                            EntityManager.GetComponentData<Owner>(child).m_Owner != run)
                            continue;
                        corner.Add(child);
                    }
                    if (corner.Count > before)
                        convertedRuns++;
                }
            }

            // Mutate after collecting: structural changes invalidate the buffers. Same phase and same shape as vanilla's own reap
            // (SecondaryObjectSystem.cs:1195-1206); SecondaryObjectReferencesSystem takes them out of the run's buffer at Modification5.
            for (int i = 0; i < corner.Count; i++)
            {
                Entity child = corner[i];
                string name = EntityManager.HasComponent<PrefabRef>(child)
                    ? m_PrefabSystem.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(child).m_Prefab)
                    : null;
                bool mast = name != null && name.StartsWith("SkiPisteFloodlight", StringComparison.Ordinal);
                switch (RunPropDensityRules.ClassOf(name, mast))
                {
                    case RunPropDensityRules.ClassGun: guns++; break;
                    case RunPropDensityRules.ClassHydrant: hydrants++; break;
                    case RunPropDensityRules.ClassMast: masts++; break;
                    case RunPropDensityRules.ClassEmpty: empties++; break;
                    default: others++; break;
                }
                RunAreaEmit.DeleteLikeBulldozer(EntityManager, child);
            }

            int orphans = DeleteMembersWhere((run, exists) => !exists || EntityManager.HasComponent<Deleted>(run));

            for (int i = 0; i < carried.Count; i++)
            {
                Entity run = carried[i];
                int kind = m_RunKindOf[EntityManager.GetComponentData<PrefabRef>(run).m_Prefab];
                if (!EntityManager.HasComponent<SkiRunPropSettings>(run))
                {
                    EntityManager.AddComponentData(run, new SkiRunPropSettings(RunPropPlacerRules.DefaultGunSpacing(kind), RunPropPlacerRules.DefaultLightSpacing(kind)));
                    added++;
                }
                NoteRun(run, kind, EntityManager.GetComponentData<SkiRunPropSettings>(run));
            }

            Mod.Log.Info($"Props: load sweep — {runs} run surface(s), {carried.Count} of a walked kind ({added} took default spacings); " +
                         (corner.Count > 0
                             ? $"converted old corner props on {convertedRuns} run(s): removed guns {guns}, hydrants {hydrants}, masts {masts}, empty markers {empties}, other {others}"
                             : "no old corner props") +
                         $"; {orphans} orphan placed prop(s) removed; {m_Dirty.Count} run(s) to re-place");
        }

        // --- Change detection ----------------------------------------------------------------------------------------------------------

        private void NoteFreshRuns()
        {
            if (m_FreshRunQuery.IsEmptyIgnoreFilter)
                return;
            List<Entity> fresh = null;
            using (var areas = m_FreshRunQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < areas.Length; i++)
                {
                    if (m_RunKindOf.ContainsKey(EntityManager.GetComponentData<PrefabRef>(areas[i]).m_Prefab))
                        (fresh ??= new List<Entity>()).Add(areas[i]);
                }
            }
            if (fresh == null)
                return;
            // Mutate after collecting.
            for (int i = 0; i < fresh.Count; i++)
            {
                Entity run = fresh[i];
                int kind = m_RunKindOf[EntityManager.GetComponentData<PrefabRef>(run).m_Prefab];
                if (!EntityManager.HasComponent<SkiRunPropSettings>(run))
                    EntityManager.AddComponentData(run, new SkiRunPropSettings(RunPropPlacerRules.DefaultGunSpacing(kind), RunPropPlacerRules.DefaultLightSpacing(kind)));
                NoteRun(run, kind, EntityManager.GetComponentData<SkiRunPropSettings>(run));
            }
        }

        /// <summary>A run is dirty when the key of what its ring and sliders ask for differs from the key of what stands.</summary>
        private void NoteRun(Entity run, int kind, SkiRunPropSettings settings)
        {
            PlacerRing ring = ReadRing(run);
            int key = unchecked((int)RunPropPlacerRules.RingKey(ring, kind, settings.m_GunSpacingM, settings.m_LightSpacingM));
            if (key == settings.m_AppliedKey)
            {
                m_Dirty.Remove(run);
                return;
            }
            m_Dirty.Add(run);
            m_ChangedThisFrame = true;
        }

        private PlacerRing ReadRing(Entity run)
        {
            DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(run, isReadOnly: true);
            int n = nodes.Length;
            var x = new float[n];
            var y = new float[n];
            var z = new float[n];
            for (int i = 0; i < n; i++)
            {
                float3 p = nodes[i].m_Position;
                x[i] = p.x;
                y[i] = p.y;
                z[i] = p.z;
            }
            return new PlacerRing(x, y, z, n);
        }

        /// <summary>The settle rule: dirty runs wait until nothing changed for SettleFrames updates, so a drag rebuilds once.</summary>
        private void Settle()
        {
            m_Stable = RunPropDensityRules.NextStable(m_Stable, m_ChangedThisFrame);
            m_ChangedThisFrame = false;
            if (m_Dirty.Count == 0 || m_Stable < RunPropDensityRules.SettleFrames)
                return;
            foreach (Entity run in m_Dirty)
            {
                if (m_PlanQueued.Add(run))
                    m_PlanQueue.Enqueue(run);
            }
            m_Dirty.Clear();
        }

        // --- Plan and apply ------------------------------------------------------------------------------------------------------------

        private void StepQueue(long t0)
        {
            if (m_PlanQueue.Count == 0 && m_Ops.Count == 0)
                return;
            if (!m_BurstOn)
            {
                m_BurstOn = true;
                m_BurstRuns = m_BurstCreated = m_BurstDeleted = m_BurstKept = m_BurstFrames = 0;
                m_BurstTicks = m_BurstMaxTicks = 0;
            }

            int ops = ApplyOps(0);
            if (m_Ops.Count == 0 && m_PlanQueue.Count > 0)
            {
                using (var members = m_MemberQuery.ToEntityArray(Allocator.Temp))
                using (var tags = m_MemberQuery.ToComponentDataArray<SkiRunPropMember>(Allocator.Temp))
                {
                    for (int plans = 0; plans < MaxPlansPerFrame && m_PlanQueue.Count > 0 && m_Ops.Count < MaxOpsPerFrame; plans++)
                    {
                        Entity run = m_PlanQueue.Dequeue();
                        m_PlanQueued.Remove(run);
                        PlanRun(run, members, tags);
                    }
                }
                ops = ApplyOps(ops);
            }

            long ticks = Stopwatch.GetTimestamp() - t0;
            m_BurstFrames++;
            m_BurstTicks += ticks;
            if (ticks > m_BurstMaxTicks)
                m_BurstMaxTicks = ticks;
            if (m_PlanQueue.Count == 0 && m_Ops.Count == 0)
                EndBurst();
        }

        private void PlanRun(Entity run, NativeArray<Entity> members, NativeArray<SkiRunPropMember> tags)
        {
            if (!EntityManager.Exists(run) || EntityManager.HasComponent<Deleted>(run) || !EntityManager.HasComponent<SkiRunPropSettings>(run) ||
                !EntityManager.HasBuffer<Node>(run) || !EntityManager.HasComponent<PrefabRef>(run) ||
                !m_RunKindOf.TryGetValue(EntityManager.GetComponentData<PrefabRef>(run).m_Prefab, out int kind))
                return;

            SkiRunPropSettings settings = EntityManager.GetComponentData<SkiRunPropSettings>(run);
            PlacerRing ring = ReadRing(run);
            RunPropPlacerRules.Plan(ring, kind, settings.m_GunSpacingM, settings.m_LightSpacingM, m_Plan);

            m_Standing.Clear();
            m_StandingEntities.Clear();
            for (int i = 0; i < members.Length; i++)
            {
                if (tags[i].m_Run != run)
                    continue;
                Entity prop = members[i];
                Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(prop);
                float3 fwd = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
                int index = m_PropIndexOf.TryGetValue(EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab, out int known) ? known : -1;
                m_Standing.Add(new StandingProp { Prop = index, X = t.m_Position.x, Z = t.m_Position.z, FwdX = fwd.x, FwdZ = fwd.z });
                m_StandingEntities.Add(prop);
            }

            RunPropPlacerRules.Diff(m_Plan, m_Standing, m_ToCreate, m_ToRemove);
            m_BurstRuns++;
            m_BurstKept += m_Plan.Count - m_ToCreate.Count;
            for (int i = 0; i < m_ToRemove.Count; i++)
                m_Ops.Enqueue(new Op { Kind = OpKind.Delete, Run = run, Target = m_StandingEntities[m_ToRemove[i]] });
            for (int i = 0; i < m_ToCreate.Count; i++)
                m_Ops.Enqueue(new Op { Kind = OpKind.Create, Run = run, Prop = m_Plan[m_ToCreate[i]] });
            // Last, so a save taken mid-rebuild keeps the old key and the next load finishes the job.
            m_Ops.Enqueue(new Op
            {
                Kind = OpKind.Applied,
                Run = run,
                Key = unchecked((int)RunPropPlacerRules.RingKey(ring, kind, settings.m_GunSpacingM, settings.m_LightSpacingM)),
            });
        }

        private int ApplyOps(int done)
        {
            while (done < MaxOpsPerFrame && m_Ops.Count > 0)
            {
                Op op = m_Ops.Dequeue();
                switch (op.Kind)
                {
                    case OpKind.Delete:
                        if (EntityManager.Exists(op.Target) && !EntityManager.HasComponent<Deleted>(op.Target))
                        {
                            RunAreaEmit.DeleteLikeBulldozer(EntityManager, op.Target);
                            m_BurstDeleted++;
                            done++;
                        }
                        break;
                    case OpKind.Create:
                        if (RunAlive(op.Run) && CreateProp(op.Run, op.Prop))
                        {
                            m_BurstCreated++;
                            done++;
                        }
                        break;
                    case OpKind.Applied:
                        if (RunAlive(op.Run) && EntityManager.HasComponent<SkiRunPropSettings>(op.Run))
                        {
                            SkiRunPropSettings settings = EntityManager.GetComponentData<SkiRunPropSettings>(op.Run);
                            settings.m_AppliedKey = op.Key;
                            EntityManager.SetComponentData(op.Run, settings);
                        }
                        break;
                }
            }
            return done;
        }

        private bool RunAlive(Entity run) => EntityManager.Exists(run) && !EntityManager.HasComponent<Deleted>(run);

        /// <summary>One free-standing prop, the entity shape of vanilla's own creation without the Secondary owner types
        /// (SecondaryObjectSystem.cs:3552-3560, :3638-3649) plus the Surface an unowned physical object gets (GenerateObjectsSystem.cs:1264-1274).
        /// The archetype brings Created + Updated (ObjectPrefab.RefreshArchetype), so the search tree and the renderer take it from here. No
        /// Elevation: on the ground, and GroundHeightSystem re-snaps it after a terrain edit (Overridable | Brushable props,
        /// ObjectInitializeSystem.cs:602-605; GroundHeightSystem.cs:229, :604).</summary>
        private bool CreateProp(Entity run, PlacedProp prop)
        {
            Entity prefab = m_PropPrefabs[prop.Prop];
            if (prefab == Entity.Null)
                return false;
            ObjectData objectData = EntityManager.GetComponentData<ObjectData>(prefab);
            if (!objectData.m_Archetype.Valid)
                return false;
            TerrainHeightData heights = m_TerrainSystem.GetHeightData();
            float3 position = RunAreaEmit.Ground(ref heights, new float2(prop.X, prop.Z));
            quaternion rotation = quaternion.LookRotationSafe(new float3(prop.FwdX, 0f, prop.FwdZ), math.up());

            Entity e;
            using (var created = new NativeArray<Entity>(1, Allocator.Temp))
            {
                EntityManager.CreateEntity(objectData.m_Archetype, created);   // the single-entity overload needs ReadOnlySpan, absent from this target
                e = created[0];
            }
            EntityManager.SetComponentData(e, new PrefabRef(prefab));
            EntityManager.SetComponentData(e, new Game.Objects.Transform(position, rotation));
            if (EntityManager.HasComponent<PseudoRandomSeed>(e))
                EntityManager.SetComponentData(e, new PseudoRandomSeed(PropSeed(prop)));
            if (EntityManager.HasComponent<ObjectGeometryData>(prefab) &&
                (EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Flags & Game.Objects.GeometryFlags.Physical) != 0)
            {
                // Copied from the run so a new prop does not pop until SkiRunSurfaceSystem's next pass.
                EntityManager.AddComponentData(e, EntityManager.HasComponent<Game.Objects.Surface>(run)
                    ? EntityManager.GetComponentData<Game.Objects.Surface>(run)
                    : default);
            }
            EntityManager.AddComponentData(e, new SkiRunPropMember(run, prop.Class));
            return true;
        }

        /// <summary>The object's variety seed, stable for the slot; 0 is a valid value but GetRandom clamps it away (HashSeed's note).</summary>
        private static ushort PropSeed(PlacedProp prop)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)(int)math.round(prop.X * 10f)) * 16777619u;
                h = (h ^ (uint)(int)math.round(prop.Z * 10f)) * 16777619u;
                h = (h ^ (uint)prop.Prop) * 16777619u;
                h ^= h >> 15;
                return (ushort)math.max(1u, h & 0xFFFFu);
            }
        }

        // --- Logs ----------------------------------------------------------------------------------------------------------------------

        private void EndBurst()
        {
            m_BurstOn = false;
            double toMs = 1000.0 / Stopwatch.Frequency;
            Mod.Log.Info($"Props [cost] placer: {m_BurstRuns} run(s) planned, {m_BurstCreated} created, {m_BurstDeleted} deleted, {m_BurstKept} kept over " +
                         $"{m_BurstFrames} frame(s): {m_BurstTicks * toMs:F2} ms total, {m_BurstMaxTicks * toMs:F2} ms in the worst frame " +
                         $"(budget {MaxOpsPerFrame} objects per frame)");
            LogCensus();
        }

        /// <summary>The census by the tag: per run kind, runs with a prop and the standing guns, hydrants and masts.</summary>
        private void LogCensus()
        {
            var runs = new Dictionary<int, HashSet<Entity>>();
            var counts = new Dictionary<int, int[]>();
            int orphans = 0;
            using (var members = m_MemberQuery.ToEntityArray(Allocator.Temp))
            using (var tags = m_MemberQuery.ToComponentDataArray<SkiRunPropMember>(Allocator.Temp))
            {
                for (int i = 0; i < members.Length; i++)
                {
                    Entity run = tags[i].m_Run;
                    if (run == Entity.Null || !EntityManager.Exists(run) || !EntityManager.HasComponent<PrefabRef>(run) ||
                        !m_RunKindOf.TryGetValue(EntityManager.GetComponentData<PrefabRef>(run).m_Prefab, out int kind))
                    {
                        orphans++;
                        continue;
                    }
                    if (!runs.TryGetValue(kind, out HashSet<Entity> set))
                    {
                        set = new HashSet<Entity>();
                        runs[kind] = set;
                        counts[kind] = new int[5];
                    }
                    set.Add(run);
                    byte cls = tags[i].m_Class;
                    counts[kind][cls < 5 ? cls : 0]++;
                }
            }
            Mod.Log.Info("Props: census — " + CensusPart("snowmaking", (int)RunKind.Snowmaking, runs, counts) + "; " +
                         CensusPart("night", (int)RunKind.Night, runs, counts) + "; " + CensusPart("cat track", (int)RunKind.CatTrack, runs, counts) +
                         (orphans > 0 ? $"; {orphans} with no live run" : ""));
        }

        private static string CensusPart(string label, int kind, Dictionary<int, HashSet<Entity>> runs, Dictionary<int, int[]> counts)
        {
            if (!runs.TryGetValue(kind, out HashSet<Entity> set))
                return $"{label} none";
            int[] c = counts[kind];
            return $"{label} {set.Count} run(s): guns {c[RunPropDensityRules.ClassGun]}, hydrants {c[RunPropDensityRules.ClassHydrant]}, masts {c[RunPropDensityRules.ClassMast]}";
        }

        // --- Prefabs -------------------------------------------------------------------------------------------------------------------

        /// <summary>The table's prefabs by name, not through any run prefab's sub-object list: a cat track needs no AreaSubObjects. Session
        /// cache; a missing one is tolerated with a line (a slot of it stays empty).</summary>
        private void ResolveProps()
        {
            if (m_PropsResolved)
                return;
            m_PropsResolved = true;
            PlacerProp[] table = RunPropPlacerRules.Props;
            m_PropPrefabs = new Entity[table.Length];
            m_PropsFound = 0;
            for (int i = 0; i < table.Length; i++)
            {
                Entity prefab = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID("StaticObjectPrefab", table[i].Name), out PrefabBase prefabBase))
                    m_PrefabSystem.TryGetEntity(prefabBase, out prefab);
                if (prefab == Entity.Null)
                {
                    Mod.Log.Info($"Props: prop prefab '{table[i].Name}' not found (tolerated: its slots stay empty)");
                    continue;
                }
                m_PropPrefabs[i] = prefab;
                m_PropIndexOf[prefab] = i;
                m_PropsFound++;
            }
        }
    }
}
