// Refuses a lift station preview that snaps onto the end of a built lift line facing away from it (FIX-LIST 54(b)): the stub tip sits
// on the line's end node while the line's next node lies behind the stub, so the line would run back through the building. The
// vanilla object tool offers two snap candidates at a net node, the tool's own rotation and the stub aligned to the edge, and ranks
// them by snap priority from the cursor distance alone (ObjectToolSystem.cs:689-700, NetIterator), so a reversed station snaps on; the field
// steep tram base did (yaw 128.7, the line leaving its lot front: 28.8 m above the predicted front). The rule is the pure
// StationAlign.AttachesBackward (LiftBatch EI-54b).
// Mechanism as PowerJoinRefusalSystem (its header has the engine refs): the Temp station gets ErrorSeverity.Error in
// ValidationSystem.Components.m_ErrorMap, written between ValidationSystem and Components (Mod.cs: UpdateBefore Components at
// ModificationEnd), so the preview turns red and the tool cannot apply (ToolBaseSystem.GetAllowApply). Frames without the map change no
// Error, so the last verdict stands. Nothing else is touched: no net geometry, no tool state, no saved data.
// Cost: nothing unless the map exists AND a Temp catalog station exists (placing or relocating one); then a main-thread pass over the
// built lift-line edges (tens to hundreds). Never runs while our lift tool is active.
// Logs: "created", ARMED per load, one INFO line per refusal episode (start and end), "!!!" once on a throw (then off until the next
// load; a reversed snap builds again, as before).

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class StationFacingRefusalSystem : GameSystemBase
    {
        private ToolSystem m_ToolSystem;
        private ValidationSystem.Components m_ValidationComponents;
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private EntityQuery m_TempObjectQuery;
        private EntityQuery m_LineEdgeQuery;

        private bool m_Armed, m_Disabled;
        private PowerJoinEpisode m_Episode;
        private readonly List<float4> m_LineEnds = new List<float4>(64);   // reused; per built lift-line edge: start xz, end xz
        private readonly List<Entity> m_Refused = new List<Entity>(2);

        /// <summary>The last evaluation refused a station preview (the tooltip reads it). Cleared when no Temp station is left.</summary>
        internal bool RefusingNow { get; private set; }

        /// <summary>Evaluations that refused since the load (dev / field aid).</summary>
        public long RefusingEvaluations;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_ValidationComponents = World.GetOrCreateSystemManaged<ValidationSystem.Components>();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_TempObjectQuery = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>());
            m_LineEdgeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Owner>() },
            });
            Mod.Log.Info($"{nameof(StationFacingRefusalSystem)} created (refuses a lift station that snaps onto a line end facing away from it)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_Episode.Reset();
            RefusingNow = false;
            RefusingEvaluations = 0;
            Mod.Log.Info(m_Armed
                ? "StationFacing: ARMED — a lift station preview snapped onto a built line end with the line behind its stub gets the " +
                  "game's Error (red preview, cannot be placed)"
                : $"StationFacing: NOT ARMED (mode {mode})");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            if (m_TempObjectQuery.IsEmptyIgnoreFilter || m_ToolSystem.activeTool is LiftLineToolSystem)
            {
                Settle(0, null);
                return;
            }
            if (!m_ValidationComponents.m_ErrorMap.IsCreated)
                return;   // no validation this frame: the Error components stay as the last evaluation left them
            try
            {
                Evaluate();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                RefusingNow = false;
                Mod.Log.Error($"!!! {nameof(StationFacingRefusalSystem)} EXCEPTION — the reversed-station refusal is off until the next game " +
                              $"load (a station can snap onto a line end facing away again). Please report this stack:\n{e}");
            }
        }

        private void Evaluate()
        {
            if (!m_PrefabCatalog.IsResolved)
                return;
            m_Refused.Clear();
            string firstWhat = null;
            using (NativeArray<Entity> objects = m_TempObjectQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_TempObjectQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                bool linesRead = false;
                for (int i = 0; i < objects.Length; i++)
                {
                    if (!m_PrefabCatalog.TryGetStation(prefabs[i].m_Prefab, out StationDef def) || def == null)
                        continue;
                    Entity s = objects[i];
                    if ((EntityManager.GetComponentData<Temp>(s).m_Flags & TempFlags.Delete) != 0)
                        continue;
                    if (!linesRead)
                    {
                        ReadLineEnds();
                        linesRead = true;
                    }
                    Transform t = EntityManager.GetComponentData<Transform>(s);
                    float2 axis = StationAlign.Axis(t.m_Rotation);
                    float2 tip = StationAlign.StubTip(t.m_Position, t.m_Rotation, def.StubTipLocalZ, def.StubTipLocalX).xz;
                    for (int k = 0; k < m_LineEnds.Count; k++)
                    {
                        float4 l = m_LineEnds[k];
                        if (!StationAlign.AttachesBackward(axis, tip, l.xy, l.zw) && !StationAlign.AttachesBackward(axis, tip, l.zw, l.xy))
                            continue;
                        m_Refused.Add(s);
                        if (firstWhat == null)
                            firstWhat = $"{def.Prefab} at ({t.m_Position.x:F1}, {t.m_Position.z:F1}), stub axis ({axis.x:F2}, {axis.y:F2})";
                        break;
                    }
                }
            }

            if (m_Refused.Count > 0)
            {
                // ProcessValidationResultsJob writes the map; it must be done before the main thread touches it.
                m_ValidationComponents.m_ErrorMapDeps.Complete();
                NativeHashMap<Entity, ErrorSeverity> map = m_ValidationComponents.m_ErrorMap;
                for (int i = 0; i < m_Refused.Count; i++)
                {
                    Entity e = m_Refused[i];
                    // AddError's rule (ValidationSystem.cs:1787-1798): keep a stronger severity (Cancel / CancelError).
                    if (map.TryGetValue(e, out ErrorSeverity have))
                    {
                        if (have < ErrorSeverity.Error)
                            map[e] = ErrorSeverity.Error;
                    }
                    else
                    {
                        map.Add(e, ErrorSeverity.Error);
                    }
                }
            }
            Settle(m_Refused.Count, firstWhat);
        }

        /// <summary>Both end nodes (xz) of every built lift-line edge (not a station's own stub sub-net: those carry an Owner).</summary>
        private void ReadLineEnds()
        {
            m_LineEnds.Clear();
            IReadOnlyDictionary<Entity, LiftTypeDef> lines = m_PrefabCatalog.LiftLineTypes;
            using (NativeArray<Entity> edges = m_LineEdgeQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_LineEdgeQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    if (!lines.ContainsKey(prefabs[i].m_Prefab))
                        continue;
                    Edge edge = EntityManager.GetComponentData<Edge>(edges[i]);
                    if (!EntityManager.TryGetComponent(edge.m_Start, out Node a) || !EntityManager.TryGetComponent(edge.m_End, out Node b))
                        continue;
                    m_LineEnds.Add(new float4(a.m_Position.xz, b.m_Position.xz));
                }
            }
        }

        private void Settle(int refused, string what)
        {
            RefusingNow = refused > 0;
            if (refused > 0)
                RefusingEvaluations++;
            switch (m_Episode.Step(refused > 0))
            {
                case PowerJoinEpisode.Event.Started:
                    Mod.Log.Info($"StationFacing: REFUSED — {what} snapped onto a lift line end with the line behind its stub; shown red, " +
                                 "cannot be placed (the station faces away from the line)");
                    break;
                case PowerJoinEpisode.Event.Ended:
                    Mod.Log.Info($"StationFacing: refusal episode ended after {m_Episode.Evaluations} refusing evaluation(s)");
                    break;
            }
        }
    }
}
