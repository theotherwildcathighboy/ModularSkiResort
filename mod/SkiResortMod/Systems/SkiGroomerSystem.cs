// One snow groomer per bound cat track, driving back and forth along the track's centerline while the track is open. A prop, not a vehicle:
// no pathfinding, no lanes, no traffic, no effect on snow, rating or upkeep. Never more than one per track.
// The trip is a pure function of the render clock (GroomerRules), the path comes from the run's outline (GroomerCenterline), the ground from
// the terrain's CPU heights. Poses are computed here on the main thread and written by one Burst job on Dependency (Transform + CullingInfo).
// The entity is the prefab's archetype minus Static, plus Stopped, InterpolatedTransform and LivePath: no search tree (not clickable, no panel,
// no bulldoze, no overlap with anything), and the saver skips it. Respawned from the catalog after every load; nothing stays in a save.
// Log discipline: one ARMED line per load, one [prefab] line (or one WARN and off), one [no path] line per track per outline, one [state] line
// per pass that changed anything, one [lights] line per day/night flip, one [cost] line per load, a one-shot "!!!" catch. The Editor never arms it.
// Engine notes: EngineNotes/SkiGroomerSystem.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Effects;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Routes;
using Game.Simulation;
using SkiResortMod.Settings;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiGroomerSystem : GameSystemBase
    {
        /// <summary>The [cost] window: passes that wrote a pose (about 10 s at 60 fps).</summary>
        private const int CostPasses = 600;

        /// <summary>Light passes after a day/night flip, the same settle as SkiNightLightSystem: 8 x 2048 sim frames outlast EffectFlagSystem's 15000-tick stagger.</summary>
        private const int LightSettlePasses = 8;
        private const double LightPassS = 2048.0 / 60.0;

        /// <summary>One bound cat track. Prop is its one slot: a track can never hold two groomers.</summary>
        private sealed class Track
        {
            public Entity Run;
            public string Name;
            public int Kind;
            public float BottomM, TopM;
            public float2[] Outline;     // the catalog's array: the same object until the outline moves
            public bool PathOwed;
            public GroomerPath Path;
            public double PhaseS;
            public int PivotSign;
            public RunSnowState Season;
            public bool SeasonOpen;
            public double NextSeasonS;
            public Entity Prop;
            public GroomerGround Ground;
            public double LastWriteS;
            public int SinceWrite;
            public float3 LastPosition;
            public bool WriteNow;        // a new path: write this pass even with the clock paused
            public bool Seen;
        }

        private struct PoseWrite
        {
            public Entity Prop;
            public float3 Position;
            public quaternion Rotation;
        }

        /// <summary>Writes the poses. A job on Dependency because a main-thread write of Transform would wait for every vehicle job in the city. The
        /// bounds go straight into CullingInfo: the Updated tag vanilla would use is a structural change per groomer per frame, and the only
        /// reader of a moving object's bounds is the culling that follows (EngineNotes/SkiGroomerSystem.md, "Culling").</summary>
        [BurstCompile]
        private struct WritePosesJob : IJob
        {
            [ReadOnly] public NativeArray<PoseWrite> Writes;
            [ReadOnly] public EntityStorageInfoLookup Storage;
            [ReadOnly] public ComponentLookup<Deleted> Deleted;
            public ComponentLookup<Transform> Transforms;
            public ComponentLookup<CullingInfo> Culling;
            public Bounds3 LocalBounds;
            /// <summary>[0] = 1 when the job ran as managed code (MarkManaged is [BurstDiscard]: a Burst-compiled job leaves it 0).</summary>
            public NativeArray<int> ManagedProbe;

            [BurstDiscard]
            private static void MarkManaged(NativeArray<int> probe)
            {
                probe[0] = 1;
            }

            public void Execute()
            {
                MarkManaged(ManagedProbe);
                for (int i = 0; i < Writes.Length; i++)
                {
                    PoseWrite w = Writes[i];
                    if (!Storage.Exists(w.Prop) || Deleted.HasComponent(w.Prop) || !Transforms.HasComponent(w.Prop) || !Culling.TryGetComponent(w.Prop, out CullingInfo culling))
                        continue;
                    Transforms[w.Prop] = new Transform(w.Position, w.Rotation);
                    culling.m_Bounds = ObjectUtils.CalculateBounds(w.Position, w.Rotation, LocalBounds);
                    Culling[w.Prop] = culling;
                }
            }
        }

        private RenderingSystem m_Rendering;
        private TerrainSystem m_Terrain;
        private CameraUpdateSystem m_Camera;
        private PrefabSystem m_PrefabSystem;
        private SkiRunCatalogSystem m_Catalog;
        private SkiSnowInjectionSystem m_Injection;
        private EffectFlagSystem m_EffectFlags;
        private EntityQuery m_PrefabQuery;

        private ComponentLookup<Transform> m_TransformLookup;
        private ComponentLookup<CullingInfo> m_CullingLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private EntityStorageInfoLookup m_StorageLookup;
        private NativeList<PoseWrite> m_Writes;
        private NativeArray<int> m_ManagedProbe;
        private JobHandle m_JobHandle;
        private bool m_JobScheduled;
        private bool m_Allocated;

        private readonly List<Track> m_Tracks = new List<Track>();
        private readonly Dictionary<Entity, Track> m_TrackByRun = new Dictionary<Entity, Track>();
        private readonly float[] m_Heights = new float[GroomerRules.FootprintSamples];

        private bool m_Armed;
        private bool m_Disabled;
        private bool m_OptionOn;
        private bool m_PrefabResolved;
        private Entity m_Prefab;
        private EntityArchetype m_Archetype;
        private Bounds3 m_LocalBounds;
        private float m_ForwardYawRad;
        private long m_SyncedRebuilds;
        private bool m_AnyPathOwed;
        private int m_Groomers;
        private double m_LastTimeS;
        private bool m_CapLogged;
        private bool m_HaveNight;
        private bool m_LastNight;
        private int m_LightPasses;
        private double m_NextLightS;

        // [cost] window and path counters, per load
        private readonly float[] m_CostRing = new float[CostPasses];
        private readonly List<float> m_CostScratch = new List<float>(CostPasses);
        private int m_CostCount;
        private bool m_CostLogged;
        private long m_PosesWritten;
        private int m_PathBuilds;
        private float m_PathSlowestMs;
        private int m_BurstProbe;   // 0 = not probed this load, 1 = in flight, 2 = runs Burst, 3 = managed fallback

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Rendering = World.GetOrCreateSystemManaged<RenderingSystem>();
            m_Terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_Injection = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_EffectFlags = World.GetOrCreateSystemManaged<EffectFlagSystem>();
            m_PrefabQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<ObjectData>());
            m_TransformLookup = GetComponentLookup<Transform>(isReadOnly: false);
            m_CullingLookup = GetComponentLookup<CullingInfo>(isReadOnly: false);
            m_DeletedLookup = GetComponentLookup<Deleted>(isReadOnly: true);
            m_StorageLookup = GetEntityStorageInfoLookup();
            m_Writes = new NativeList<PoseWrite>(16, Allocator.Persistent);
            m_ManagedProbe = new NativeArray<int>(1, Allocator.Persistent);
            m_Allocated = true;
        }

        protected override void OnDestroy()
        {
            try
            {
                m_JobHandle.Complete();
                m_JobScheduled = false;
                if (m_Allocated)
                {
                    m_Allocated = false;
                    m_Writes.Dispose();
                    m_ManagedProbe.Dispose();
                }
            }
            catch (Exception)
            {
                // teardown never throws
            }
            base.OnDestroy();
        }

        /// <summary>Never throws: GameSystemBase.cs:85-96 disables a system whose OnGamePreload throws. ClearSystem destroys every PrefabRef entity on a
        /// load (LivePath does not exempt one), so forgetting the props here leaves nothing behind.</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            m_Armed = false;
            try
            {
                CompleteJob();
            }
            catch (Exception)
            {
                // the next load re-arms
            }
            m_Tracks.Clear();
            m_TrackByRun.Clear();
            m_Groomers = 0;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            CompleteJob();
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_Tracks.Clear();
            m_TrackByRun.Clear();
            m_Groomers = 0;
            m_OptionOn = Mod.Settings?.Groomers ?? SkiResortSettings.DefaultGroomers;
            m_PrefabResolved = false;
            m_Prefab = Entity.Null;
            m_SyncedRebuilds = -1L;
            m_AnyPathOwed = false;
            m_LastTimeS = -1.0;
            m_CapLogged = false;
            m_HaveNight = false;
            m_LightPasses = 0;
            m_CostCount = 0;
            m_CostLogged = false;
            m_PosesWritten = 0L;
            m_PathBuilds = 0;
            m_PathSlowestMs = 0f;
            m_BurstProbe = 0;
            if (m_Armed)
            {
                Mod.Log.Info("Groomers: ARMED, one snow groomer per bound cat track while its snow share is open " +
                             $"(option {(m_OptionOn ? "on" : "off")}; {GroomerRules.SpeedMps:0.##} m/s on the render clock, stop {GroomerRules.StopS:0.#} s, " +
                             $"half turn {GroomerRules.PivotS:0.#} s, footprint {GroomerRules.FootprintLengthM:0.#} x {GroomerRules.FootprintWidthM:0.#} m, " +
                             $"cap {GroomerRules.MaxGroomers}; the saver skips the props, they respawn from the catalog after a load)");
            }
        }

        /// <summary>The chairs' clock: frozen on pause (RenderingSystem.cs:187-190), smooth at any speed (:193-222).</summary>
        private double TimeS()
        {
            return ((double)m_Rendering.frameIndex + m_Rendering.frameTime) / 60.0;
        }

        private void CompleteJob()
        {
            if (!m_JobScheduled)
                return;
            m_JobScheduled = false;
            m_JobHandle.Complete();
            if (m_BurstProbe == 1)
            {
                m_BurstProbe = m_ManagedProbe[0] != 0 ? 3 : 2;
                if (m_BurstProbe == 3)
                    Mod.Log.Warn("Groomers: the pose job runs WITHOUT Burst (managed fallback: the mod's Burst library is missing or did not compile the job); correct, but slower");
            }
        }

        protected override void OnUpdate()
        {
            CompleteJob();
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                Pass();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! Groomers: {e.GetType().Name}: {e.Message}; disabled until the next load\n{e}");
                try
                {
                    RemoveAllProps();
                }
                catch (Exception)
                {
                    // the load clears them
                }
            }
        }

        private void Pass()
        {
            long start = Stopwatch.GetTimestamp();
            bool on = Mod.Settings?.Groomers ?? SkiResortSettings.DefaultGroomers;
            if (on != m_OptionOn)
            {
                m_OptionOn = on;
                Mod.Log.Info($"Groomers [option]: {(on ? "on" : "off")}; {m_Groomers} on the tracks now");
            }
            if (!on)
            {
                if (m_Groomers > 0)
                    RemoveAllProps();
                return;
            }
            if (!m_PrefabResolved)
                ResolvePrefab();
            if (m_Prefab == Entity.Null)
                return;

            double timeS = TimeS();
            if (m_Catalog.Rebuilds != m_SyncedRebuilds)
                SyncWithCatalog();
            DropDeadTracks();
            BuildOnePath();
            PollSeason(timeS);
            Reconcile(timeS);
            RefreshLights(timeS);
            MoveProps(timeS, start);
            m_LastTimeS = timeS;
        }

        /// <summary>A machine's Night-gated effects are evaluated at spawn and on EffectsUpdated, nothing else (EffectControlSystem.cs:1087-1097): without this, one
        /// spawned by day stays dark all night. The tag is structural, so it runs here before the job, and only for the passes after a flip.</summary>
        private void RefreshLights(double timeS)
        {
            bool night = m_EffectFlags.GetData().m_IsNightTime;
            if (!m_HaveNight || night != m_LastNight)
            {
                if (m_HaveNight)
                    Mod.Log.Info($"Groomers [lights]: {(night ? "night" : "day")}; refreshing {m_Groomers} machine(s)");
                m_HaveNight = true;
                m_LastNight = night;
                m_LightPasses = LightSettlePasses;
                m_NextLightS = timeS;
            }
            if (m_LightPasses <= 0 || timeS < m_NextLightS)
                return;
            m_LightPasses--;
            m_NextLightS = timeS + LightPassS;
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                if (m_Tracks[i].Prop != Entity.Null)
                    EntityManager.AddComponent<EffectsUpdated>(m_Tracks[i].Prop);
            }
        }

        // ---- the prefab ---------------------------------------------------------------------------------------------------------------

        private void ResolvePrefab()
        {
            m_PrefabResolved = true;
            bool standIn = false;
            Entity prefab = FindPrefab(GroomerRules.PrefabName);
            if (prefab == Entity.Null)
            {
                standIn = true;
                prefab = FindPrefab(GroomerRules.StandInPrefabName);
            }
            if (prefab == Entity.Null)
            {
                Mod.Log.Warn($"Groomers: OFF for this load, neither {GroomerRules.PrefabName} nor the stand-in {GroomerRules.StandInPrefabName} is loaded");
                return;
            }
            ObjectData data = EntityManager.GetComponentData<ObjectData>(prefab);
            ObjectGeometryData geometry = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
            m_Archetype = data.m_Archetype;
            m_LocalBounds = ObjectUtils.GetBounds(geometry);
            m_ForwardYawRad = math.radians(standIn ? GroomerRules.StandInForwardYawDeg : GroomerRules.LocalForwardYawDeg);
            m_Prefab = prefab;
            Mod.Log.Info($"Groomers [prefab]: {(standIn ? $"stand-in {GroomerRules.StandInPrefabName} ({GroomerRules.PrefabName} is not loaded)" : GroomerRules.PrefabName)}, " +
                         $"mesh {math.length(m_LocalBounds.max - m_LocalBounds.min):0.#} m across");
        }

        private Entity FindPrefab(string name)
        {
            if (m_PrefabSystem.TryGetPrefab(new PrefabID("StaticObjectPrefab", name), out PrefabBase prefabBase) &&
                m_PrefabSystem.TryGetEntity(prefabBase, out Entity direct) && Usable(direct))
            {
                return direct;
            }
            using (NativeArray<Entity> entities = m_PrefabQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (m_PrefabSystem.GetPrefabName(entities[i]) == name && Usable(entities[i]))
                        return entities[i];
                }
            }
            return Entity.Null;
        }

        private bool Usable(Entity prefab)
        {
            return EntityManager.HasComponent<ObjectGeometryData>(prefab) &&
                   EntityManager.HasComponent<ObjectData>(prefab) &&
                   EntityManager.GetComponentData<ObjectData>(prefab).m_Archetype.Valid;
        }

        // ---- the tracks ---------------------------------------------------------------------------------------------------------------

        /// <summary>Runs at the catalog's cadence, not per frame. A track the catalog no longer lists (gone, or unbound) loses its groomer; a new array
        /// reference means the outline moved.</summary>
        private void SyncWithCatalog()
        {
            m_SyncedRebuilds = m_Catalog.Rebuilds;
            for (int i = 0; i < m_Tracks.Count; i++)
                m_Tracks[i].Seen = false;
            int rows = m_Catalog.RowCount;
            for (int r = 0; r < rows; r++)
            {
                SkiRunCatalogSystem.RunRow row = m_Catalog.GetRow(r);
                if (!row.Bound || GroomerRules.SourceFor(row.Kind) == null)
                    continue;
                if (!m_TrackByRun.TryGetValue(row.Entity, out Track track))
                {
                    track = new Track { Run = row.Entity, Kind = row.Kind };
                    m_Tracks.Add(track);
                    m_TrackByRun.Add(row.Entity, track);
                }
                track.Seen = true;
                track.Name = row.Name;
                track.BottomM = row.NodeBottomM;
                track.TopM = row.NodeTopM;
                if (!ReferenceEquals(track.Outline, row.OutlineXZ))
                {
                    track.Outline = row.OutlineXZ;
                    track.PathOwed = true;
                    m_AnyPathOwed = true;
                }
            }
            int removed = 0;
            for (int i = m_Tracks.Count - 1; i >= 0; i--)
            {
                if (!m_Tracks[i].Seen && DropTrack(i))
                    removed++;
            }
            if (removed > 0)
                LogState($"removed {removed} with their tracks");
        }

        /// <summary>Every delete route (bulldoze, hub trash, delete mode, delete-all) ends in a Deleted tag on the run or its absence: seen here on the same
        /// frame, without waiting for the catalog. EntityManager.Exists and HasComponent read the archetype, so they wait for no job.</summary>
        private void DropDeadTracks()
        {
            int removed = 0;
            for (int i = m_Tracks.Count - 1; i >= 0; i--)
            {
                Track track = m_Tracks[i];
                if (!EntityManager.Exists(track.Run) || EntityManager.HasComponent<Deleted>(track.Run))
                {
                    if (DropTrack(i))
                        removed++;
                    continue;
                }
                if (track.Prop != Entity.Null && !EntityManager.Exists(track.Prop))
                {
                    Mod.Log.Warn($"Groomers: the prop of \"{track.Name}\" vanished without this system removing it; respawning");
                    track.Prop = Entity.Null;
                    track.Ground = default;
                    m_Groomers--;
                }
            }
            if (removed > 0)
                LogState($"removed {removed} with their tracks");
        }

        /// <summary>True when the track had a groomer.</summary>
        private bool DropTrack(int index)
        {
            Track track = m_Tracks[index];
            bool hadProp = track.Prop != Entity.Null;
            RemoveProp(track);
            m_TrackByRun.Remove(track.Run);
            m_Tracks.RemoveAt(index);
            return hadProp;
        }

        /// <summary>At most one path per frame: a build is rare but not free (EngineNotes/GroomerCenterline.md, "Cost"), and a load can owe many.</summary>
        private void BuildOnePath()
        {
            if (!m_AnyPathOwed)
                return;
            m_AnyPathOwed = false;
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                Track track = m_Tracks[i];
                if (!track.PathOwed)
                    continue;
                long t0 = Stopwatch.GetTimestamp();
                GroomerPath path = GroomerRules.SourceFor(track.Kind).Build(track.Outline, out GroomerPathFail fail);
                float ms = (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
                m_PathBuilds++;
                m_PathSlowestMs = math.max(m_PathSlowestMs, ms);
                track.PathOwed = false;
                track.Path = path;
                track.Ground = default;
                track.WriteNow = true;
                if (path != null)
                {
                    uint seed = track.Outline.Length > 0 ? math.hash(track.Outline[0]) : 0u;
                    track.PhaseS = GroomerRules.PhaseFor(seed, GroomerRules.PeriodSeconds(path.LengthM));
                    track.PivotSign = GroomerRules.PivotSignFor(seed);
                }
                else
                {
                    Mod.Log.Info($"Groomers [no path]: cat track \"{track.Name}\" ({track.Outline?.Length ?? 0} outline nodes): {fail}; no groomer on it until its outline changes");
                }
                for (int k = i + 1; k < m_Tracks.Count; k++)
                {
                    if (m_Tracks[k].PathOwed)
                    {
                        m_AnyPathOwed = true;
                        break;
                    }
                }
                return;
            }
        }

        private void PollSeason(double timeS)
        {
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                Track track = m_Tracks[i];
                if (timeS < track.NextSeasonS)
                    continue;
                track.NextSeasonS = timeS + GroomerRules.SeasonPollS;
                RunSnow snow = m_Injection.GetRunSnow(track.Run, (RunKind)track.Kind, track.BottomM, track.TopM);
                track.SeasonOpen = GroomerRules.SeasonStep(ref track.Season, snow.Share);
            }
        }

        // ---- spawn and remove (the only structural changes, all before the job) -------------------------------------------------------

        private void Reconcile(double timeS)
        {
            int spawned = 0, removed = 0;
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                Track track = m_Tracks[i];
                bool wanted = GroomerRules.Wanted(m_OptionOn, true, track.SeasonOpen, track.Path != null);
                switch (GroomerRules.Decide(wanted, track.Prop != Entity.Null, m_Groomers))
                {
                    case GroomerAction.Spawn:
                        if (Spawn(track, timeS))
                            spawned++;
                        break;
                    case GroomerAction.Remove:
                        RemoveProp(track);
                        removed++;
                        break;
                    default:
                        if (wanted && track.Prop == Entity.Null && !m_CapLogged)
                        {
                            m_CapLogged = true;
                            Mod.Log.Info($"Groomers: the cap of {GroomerRules.MaxGroomers} is reached; further cat tracks stay without one");
                        }
                        break;
                }
            }
            if (spawned > 0 || removed > 0)
                LogState($"spawned {spawned}, removed {removed}");
        }

        private bool Spawn(Track track, double timeS)
        {
            TerrainHeightData heights = m_Terrain.GetHeightData();
            if (!heights.isCreated)
                return false;
            track.Ground = default;
            if (!TryPose(track, timeS, 0.0, ref heights, out float3 position, out quaternion rotation))
                return false;
            var transform = new Transform(position, rotation);
            using (var one = new NativeArray<Entity>(1, Allocator.Temp))
            {
                // The prefab's archetype (Created and Updated come with it and give the first culling pass) minus Static, plus Stopped (the culling
                // query takes Moving or Stopped, PreCullingSystem.cs:2859-2896), InterpolatedTransform (the per-frame copy to the render,
                // ObjectInterpolateSystem.cs:248-263) and LivePath (the saver's skip, SerializerSystem.cs:171-209). Spawns are rare, so four
                // structural calls cost nothing that matters.
                EntityManager.CreateEntity(m_Archetype, one);
                Entity prop = one[0];
                EntityManager.RemoveComponent<Static>(prop);
                EntityManager.AddComponent<Stopped>(prop);
                EntityManager.AddComponent<InterpolatedTransform>(prop);
                EntityManager.AddComponent<LivePath>(prop);
                EntityManager.SetComponentData(prop, new PrefabRef(m_Prefab));
                EntityManager.SetComponentData(prop, transform);
                EntityManager.SetComponentData(prop, new InterpolatedTransform(transform));
                if (EntityManager.HasComponent<PseudoRandomSeed>(prop))
                    EntityManager.SetComponentData(prop, new PseudoRandomSeed((ushort)math.max(1u, math.hash(new int2(track.Run.Index, track.Run.Version)) & 0xFFFFu)));
                track.Prop = prop;
            }
            track.LastWriteS = timeS;
            track.LastPosition = position;
            track.SinceWrite = 0;
            track.WriteNow = false;
            m_Groomers++;
            return true;
        }

        private void RemoveProp(Track track)
        {
            if (track.Prop == Entity.Null)
                return;
            if (EntityManager.Exists(track.Prop) && !EntityManager.HasComponent<Deleted>(track.Prop))
                EntityManager.AddComponent<Deleted>(track.Prop);
            track.Prop = Entity.Null;
            track.Ground = default;
            m_Groomers--;
        }

        private void RemoveAllProps()
        {
            int removed = m_Groomers;
            for (int i = 0; i < m_Tracks.Count; i++)
                RemoveProp(m_Tracks[i]);
            LogState($"removed {removed}");
        }

        private void LogState(string what)
        {
            int noPath = 0, closed = 0;
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                if (m_Tracks[i].Path == null && !m_Tracks[i].PathOwed)
                    noPath++;
                else if (!m_Tracks[i].SeasonOpen)
                    closed++;
            }
            Mod.Log.Info($"Groomers [state]: {what}; {m_Groomers} running on {m_Tracks.Count} bound cat track(s), {closed} closed, {noPath} without a path");
        }

        // ---- the move -----------------------------------------------------------------------------------------------------------------

        private void MoveProps(double timeS, long start)
        {
            bool clockMoved = timeS != m_LastTimeS;
            if (m_Groomers == 0)
                return;
            double passDtS = m_LastTimeS >= 0.0 ? timeS - m_LastTimeS : 0.0;
            bool haveCamera = m_Camera != null && m_Camera.TryGetViewer(out _);
            float3 camera = haveCamera ? m_Camera.position : float3.zero;
            TerrainHeightData heights = m_Terrain.GetHeightData();
            if (!heights.isCreated)
                return;

            m_Writes.Clear();
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                Track track = m_Tracks[i];
                if (track.Prop == Entity.Null || track.Path == null || (!clockMoved && !track.WriteNow))
                    continue;
                // Far away a machine at 3.33 m/s moves under half a milliradian per frame: the chairs' pace, every few frames.
                float distance = haveCamera ? math.distance(camera, track.LastPosition) : 0f;
                int stride = LiftMoverMath.StrideFor(distance, GroomerRules.SpeedMps, passDtS, LiftMoverMath.PaceSettings.Default);
                track.SinceWrite++;
                if (track.SinceWrite < stride && !track.WriteNow)
                    continue;
                if (!TryPose(track, timeS, timeS - track.LastWriteS, ref heights, out float3 position, out quaternion rotation))
                    continue;
                track.SinceWrite = 0;
                track.WriteNow = false;
                track.LastWriteS = timeS;
                track.LastPosition = position;
                m_Writes.Add(new PoseWrite { Prop = track.Prop, Position = position, Rotation = rotation });
            }
            if (m_Writes.Length == 0)
                return;

            m_TransformLookup.Update(this);
            m_CullingLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_StorageLookup.Update(this);
            if (m_BurstProbe == 0)
            {
                m_ManagedProbe[0] = 0;   // the last job is complete (OnUpdate's first line)
                m_BurstProbe = 1;
            }
            var job = new WritePosesJob
            {
                Writes = m_Writes.AsArray(),
                Storage = m_StorageLookup,
                Deleted = m_DeletedLookup,
                Transforms = m_TransformLookup,
                Culling = m_CullingLookup,
                LocalBounds = m_LocalBounds,
                ManagedProbe = m_ManagedProbe,
            };
            m_JobHandle = job.Schedule(Dependency);
            Dependency = m_JobHandle;
            m_JobScheduled = true;
            m_PosesWritten += m_Writes.Length;
            Cost(start);
        }

        /// <summary>One machine's pose at this clock: the trip, the heading, the plane under the footprint, the smoothing. False when the terrain gave nothing yet.</summary>
        private bool TryPose(Track track, double timeS, double dtS, ref TerrainHeightData heights, out float3 position, out quaternion rotation)
        {
            GroomerTrip trip = GroomerRules.TripAt(timeS + track.PhaseS, track.Path.LengthM, track.PivotSign);
            float2 xz = track.Path.At(trip.S);
            float yaw = GroomerRules.YawOf(track.Path.Direction(trip.S, GroomerRules.HeadingChordHalfM)) + trip.TurnRad;
            for (int i = 0; i < GroomerRules.FootprintSamplesLong; i++)
            {
                for (int j = 0; j < GroomerRules.FootprintSamplesWide; j++)
                {
                    GroomerRules.FootprintOffset(i, j, out float u, out float v);
                    float2 p = GroomerRules.FootprintPoint(xz, yaw, u, v);
                    m_Heights[i * GroomerRules.FootprintSamplesWide + j] = TerrainUtils.SampleHeight(ref heights, new float3(p.x, 0f, p.y));
                }
            }
            if (GroomerRules.FitPlane(m_Heights, out float height, out float slopeLong, out float slopeWide))
                GroomerRules.StepGround(ref track.Ground, height, slopeLong, slopeWide, dtS);
            position = new float3(xz.x, track.Ground.Height + GroomerRules.GroundOffsetM, xz.y);
            rotation = GroomerRules.PoseRotation(yaw, track.Ground.SlopeLong, track.Ground.SlopeWide, m_ForwardYawRad);
            return track.Ground.Known && math.all(math.isfinite(position));
        }

        private void Cost(long start)
        {
            if (m_CostLogged)
                return;
            m_CostRing[m_CostCount++] = (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            if (m_CostCount < CostPasses)
                return;
            m_CostLogged = true;
            LiftMoverMath.Percentiles(m_CostRing, m_CostCount, m_CostScratch, out float p50, out float p95, out float max);
            string burst = m_BurstProbe == 2 ? "yes" : m_BurstProbe == 3 ? "NO, managed fallback" : "not probed";
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "Groomers [cost]: main thread p50 {0:0.000} ms, p95 {1:0.000} ms, max {2:0.000} ms over {3} passes; {4} groomer(s), {5:0.0} pose(s) per pass, " +
                "{6} terrain samples per pose; job Burst: {7}; {8} path build(s) this load, slowest {9:0.00} ms",
                p50, p95, max, m_CostCount, m_Groomers, (double)m_PosesWritten / m_CostCount, GroomerRules.FootprintSamples, burst, m_PathBuilds, m_PathSlowestMs));
        }
    }
}
