// Strips the vanilla electricity warning icons that lifts carry only because they are built as high-voltage lines and transformers
// (LiftLineIconRule.Strips): on every station the transformer cues ElectricityStatusSystem writes (transformer bottleneck, not enough
// production, bottleneck: BuildingNotificationJob, ElectricityStatusSystem.cs:131-134); on every lift line edge the net bottleneck cue
// (NetEdgeNotificationJob, :66); on a top station the consumer cues DispatchElectricitySystem writes (no electricity, bottleneck:
// :141-142), since its power comes through the line. Kept: the base station's own consumer cues and its low-voltage icon.
// Both vanilla systems run once every 128 simulation frames, DispatchElectricitySystem at offset 126 and ElectricityStatusSystem at
// 127 (DispatchElectricitySystem.cs:222-230, ElectricityStatusSystem.cs:195-203), in GameSimulation (SystemOrder.cs:444, :448), and
// queue their icons in IconCommandBuffers played back at ModificationEnd. This system runs every simulation frame right after
// ElectricityStatusSystem (Mod.cs: UpdateAfter, phase GameSimulation, load-bearing) and acts only on those two frames, plus once after
// each Game load (icons from a save): its buffer is created after theirs, so its Remove commands win at playback and the vanilla icon
// never shows (IconCommandSystem.cs:106-160; the same mechanism as LiftLineIconSystem's header).
// Logs: "created"; ARMED per Game load (which vanilla prefabs it strips); "!!!" once on a throw (then off until the next load).
// DUMP: AppendDump. Engine notes: EngineNotes/LiftLineIconSystem.md (one note for both systems).

using System;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftPowerCueSystem : GameSystemBase
    {
        internal const uint VanillaInterval = 128;
        internal const uint DispatchOffset = 126;     // DispatchElectricitySystem.GetUpdateOffset
        internal const uint StatusOffset = 127;       // ElectricityStatusSystem.GetUpdateOffset

        private SimulationSystem m_Simulation;
        private SkiPrefabCatalogSystem m_Catalog;
        private IconCommandSystem m_IconCommandSystem;
        private EntityQuery m_StationQuery;
        private EntityQuery m_EdgeQuery;
        private EntityQuery m_ElectricityConfigQuery;

        private ElectricityParameterData m_Icons;
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_FullPass;
        private int m_DispatchPasses, m_StatusPasses, m_StationRemoves, m_EdgeRemoves;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_IconCommandSystem = World.GetOrCreateSystemManaged<IconCommandSystem>();
            m_StationQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            // ElectricityStatusSystem's own edge query (ElectricityStatusSystem.cs:211).
            m_EdgeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<ElectricityNodeConnection>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            m_ElectricityConfigQuery = GetEntityQuery(ComponentType.ReadOnly<ElectricityParameterData>());
            Mod.Log.Info($"{nameof(LiftPowerCueSystem)} created (strips the vanilla transformer and high-voltage warnings from lifts)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = m_ErrorLogged = false;
            m_DispatchPasses = m_StatusPasses = m_StationRemoves = m_EdgeRemoves = 0;
            m_Armed = mode == GameMode.Game && !m_ElectricityConfigQuery.IsEmptyIgnoreFilter;
            m_FullPass = m_Armed;
            if (!m_Armed)
            {
                if (mode == GameMode.Game)
                    Mod.Log.Error("!!! LiftPowerCue: NOT ARMED (no ElectricityParameterData) — vanilla electricity warnings stay on lifts");
                return;
            }
            m_Icons = m_ElectricityConfigQuery.GetSingleton<ElectricityParameterData>();
            Mod.Log.Info("LiftPowerCue: ARMED — strips from stations the transformer, not-enough-production and bottleneck warnings, from " +
                         "top stations the no-electricity and building-bottleneck warnings, from lift lines the bottleneck warning, on " +
                         $"the frames DispatchElectricitySystem ({DispatchOffset}) and ElectricityStatusSystem ({StatusOffset}) of " +
                         $"{VanillaInterval} write them; the base station keeps its own power warnings and its low-voltage icon" +
                         (SameBottleneck ? "; the bottleneck prefabs are one prefab, so stations keep it (the base's consumer cue)" : ""));
        }

        /// <summary>When the net and the building bottleneck icons are one prefab, a station Remove would also take the base's own
        /// consumer cue: then only edges lose it.</summary>
        private bool SameBottleneck => m_Icons.m_BottleneckNotificationPrefab == m_Icons.m_BuildingBottleneckNotificationPrefab;

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                uint f = m_Simulation.frameIndex & (VanillaInterval - 1);
                bool dispatch = m_FullPass || f == DispatchOffset;
                bool status = m_FullPass || f == StatusOffset;
                if (!dispatch && !status)
                    return;
                if (!m_Catalog.IsResolved)
                    return;
                m_FullPass = false;
                // Created after the vanilla buffers of this frame: our commands sort after theirs (file header).
                IconCommandBuffer icb = m_IconCommandSystem.CreateCommandBuffer();
                if (dispatch)
                    m_DispatchPasses++;
                if (status)
                    m_StatusPasses++;
                Stations(icb, dispatch, status);
                if (status)
                    Edges(icb);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(LiftPowerCueSystem)} EXCEPTION — vanilla electricity warnings stay on lifts until the next " +
                                  $"game load. Please report this stack:\n{e}");
                }
            }
        }

        private void Stations(IconCommandBuffer icb, bool dispatch, bool status)
        {
            var defs = m_Catalog.StationDefs;
            using (NativeArray<Entity> entities = m_StationQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_StationQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!defs.TryGetValue(prefabs[i].m_Prefab, out StationDef def))
                        continue;
                    LiftIconOwner owner = def.Role == StationRole.Base ? LiftIconOwner.BaseStation : LiftIconOwner.TopStation;
                    Entity station = entities[i];
                    if (status)
                    {
                        Strip(icb, station, owner, VanillaPowerIcon.Transformer, m_Icons.m_TransformerNotificationPrefab);
                        Strip(icb, station, owner, VanillaPowerIcon.NotEnoughProduction, m_Icons.m_NotEnoughProductionNotificationPrefab);
                        if (!SameBottleneck)
                            Strip(icb, station, owner, VanillaPowerIcon.Bottleneck, m_Icons.m_BottleneckNotificationPrefab);
                    }
                    if (dispatch)
                    {
                        Strip(icb, station, owner, VanillaPowerIcon.NoElectricity, m_Icons.m_ElectricityNotificationPrefab);
                        Strip(icb, station, owner, VanillaPowerIcon.BuildingBottleneck, m_Icons.m_BuildingBottleneckNotificationPrefab);
                    }
                }
            }
        }

        private void Edges(IconCommandBuffer icb)
        {
            if (m_EdgeQuery.IsEmptyIgnoreFilter)
                return;
            var lines = m_Catalog.LiftLinePrefabSet;
            Entity marker = m_Catalog.MarkerEntity;
            using (NativeArray<Entity> entities = m_EdgeQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_EdgeQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity p = prefabs[i].m_Prefab;
                    if (!lines.Contains(p) && (marker == Entity.Null || p != marker))
                        continue;
                    if (Strip(icb, entities[i], LiftIconOwner.LiftEdge, VanillaPowerIcon.Bottleneck, m_Icons.m_BottleneckNotificationPrefab))
                        m_EdgeRemoves++;
                }
            }
        }

        private bool Strip(IconCommandBuffer icb, Entity owner, LiftIconOwner kind, VanillaPowerIcon icon, Entity prefab)
        {
            if (prefab == Entity.Null || !LiftLineIconRule.Strips(kind, icon))
                return false;
            icb.Remove(owner, prefab);
            if (kind != LiftIconOwner.LiftEdge)
                m_StationRemoves++;
            return true;
        }

        /// <summary>DUMP (main thread).</summary>
        internal void AppendDump(StringBuilder sb)
        {
            sb.Append("  lift power cues: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
              .Append("; dispatch passes ").Append(m_DispatchPasses).Append(", status passes ").Append(m_StatusPasses)
              .Append(", station removes queued ").Append(m_StationRemoves).Append(", edge removes queued ").Append(m_EdgeRemoves)
              .Append(m_Armed && SameBottleneck ? "; bottleneck prefabs shared (stations keep it)" : "").Append('\n');
        }
    }
}
