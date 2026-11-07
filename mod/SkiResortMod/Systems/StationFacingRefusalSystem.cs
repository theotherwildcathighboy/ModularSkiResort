// Refuses a lift station preview that snaps onto the end of a built lift line facing away from it (FIX-LIST 54(b)).
// One of its stub nodes sits on the line's END node while the line's next node lies behind the stub. The line would then run back
// through the building.
// The vanilla object tool offers two snap candidates at a net node: the tool's own rotation, and the stub aligned to the edge.
// It ranks them by snap priority from the cursor distance alone (ObjectToolSystem.cs:689-700, NetIterator). So a reversed station
// snaps on. The field steep tram base did (yaw 128.7, the line leaving its lot front: 28.8 m above the predicted front).
// Either stub node can land on the end. The reversed snap that PLACES puts the stub's INNER node (3 m in from the tip) on it, merged
// as a SkiLiftMarker Temp copy of the end node (in-game check 2026-11-04). Vanilla already refuses the tip-on-node snap
// ("Invalid shape"). 7e52477 tested the tip only and missed the first case (the INNER node).
// Which ends: TRUE ends only (one lift-line edge at the node. An interior tower node has two and is never judged).
// It judges only the ends the preview touches: a Temp node whose Temp.m_Original is the end node (vanilla's copy of every node a
// preview connects to). The decision is the pure StationAlign.AttachesBackward (both stub nodes, the line's next node behind the
// stub) and StationAlign.LineEnds (LiftBatch EI-54b).
// Mechanism as PowerJoinRefusalSystem (its header has the engine refs).
// The Temp station gets ErrorSeverity.Error in ValidationSystem.Components.m_ErrorMap. The system writes it between
// ValidationSystem and Components (Mod.cs: UpdateBefore Components at ModificationEnd).
// So the preview turns red and the tool cannot apply (ToolBaseSystem.GetAllowApply).
// A frame without the map changes no Error, so the last verdict stands. The system touches nothing else: no net geometry,
// no tool state, no saved data.
// Cost: nothing unless the map exists AND a Temp catalog station exists (placing or relocating one). Then a main-thread pass over
// the Temp nodes runs. When one of them copies an existing node, a pass over the built lift-line edges runs too (tens to hundreds).
// It never runs while our lift tool is active.
// Logs: "created", ARMED per load, one INFO line per refusal episode (start and end), "!!!" once on a throw (then off until the next
// load. A reversed snap builds again, as before).

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
        private EntityQuery m_TempNodeQuery;

        private bool m_Armed, m_Disabled;
        private PowerJoinEpisode m_Episode;
        // Reused per evaluation: the built lift-line edges (start, end), LineEnds' scratch and result, the Temp copies by original, and
        // the true ends this frame's preview touches.
        private readonly List<KeyValuePair<Entity, Entity>> m_EdgePairs = new List<KeyValuePair<Entity, Entity>>(256);
        private readonly Dictionary<Entity, int> m_Degree = new Dictionary<Entity, int>(256);
        private readonly Dictionary<Entity, Entity> m_Next = new Dictionary<Entity, Entity>(256);
        private readonly Dictionary<Entity, Entity> m_Ends = new Dictionary<Entity, Entity>(64);
        private readonly Dictionary<Entity, Entity> m_TempCopies = new Dictionary<Entity, Entity>(16);
        private readonly List<SnappedEnd> m_SnappedEnds = new List<SnappedEnd>(4);
        private readonly List<Entity> m_Refused = new List<Entity>(2);

        private struct SnappedEnd
        {
            public Entity End, Copy;
            public float2 EndXz, NextXz;
        }

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
            m_TempNodeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Node>(),
                ComponentType.Exclude<Deleted>());
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
                ? "StationFacing: ARMED — a lift station preview snapped onto a built line's end node (either stub node) with the line behind " +
                  "its stub gets the game's Error (red preview, cannot be placed)"
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
                bool endsRead = false;
                for (int i = 0; i < objects.Length; i++)
                {
                    if (!m_PrefabCatalog.TryGetStation(prefabs[i].m_Prefab, out StationDef def) || def == null)
                        continue;
                    Entity s = objects[i];
                    if ((EntityManager.GetComponentData<Temp>(s).m_Flags & TempFlags.Delete) != 0)
                        continue;
                    if (!endsRead)
                    {
                        ReadSnappedEnds();
                        endsRead = true;
                    }
                    if (m_SnappedEnds.Count == 0)
                        break;   // no built line end is part of this frame's preview: nothing can be refused
                    Transform t = EntityManager.GetComponentData<Transform>(s);
                    float2 axis = StationAlign.Axis(t.m_Rotation);
                    float2 tip = StationAlign.StubTip(t.m_Position, t.m_Rotation, def.StubTipLocalZ, def.StubTipLocalX).xz;
                    float2 inner = StationAlign.StubInner(t.m_Position, t.m_Rotation, def.StubTipLocalZ, def.StubTipLocalX).xz;
                    for (int k = 0; k < m_SnappedEnds.Count; k++)
                    {
                        SnappedEnd e = m_SnappedEnds[k];
                        StationAlign.StubNode on = StationAlign.AttachesBackward(axis, tip, inner, e.EndXz, e.NextXz);
                        if (on == StationAlign.StubNode.None)
                            continue;
                        m_Refused.Add(s);
                        if (firstWhat == null)
                            firstWhat = $"{def.Prefab} at ({t.m_Position.x:F1}, {t.m_Position.z:F1}) yaw {YawDeg(axis):F1}, stub axis ({axis.x:F2}, " +
                                        $"{axis.y:F2}), its stub {(on == StationAlign.StubNode.Tip ? "tip" : "inner node")} on line end node " +
                                        $"{e.End.Index}:{e.End.Version} ({e.EndXz.x:F1}, {e.EndXz.y:F1}; Temp copy {e.Copy.Index}:{e.Copy.Version}), " +
                                        $"the line's next node ({e.NextXz.x:F1}, {e.NextXz.y:F1}) behind the stub";
                        break;
                    }
                }
            }

            if (m_Refused.Count > 0)
            {
                // ProcessValidationResultsJob writes the map. It must finish before the main thread touches it.
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

        /// <summary>The station's yaw in degrees as the Temp dump prints it (local -Z = the stub axis).</summary>
        private static float YawDeg(float2 axis) => math.degrees(math.atan2(-axis.x, -axis.y));

        /// <summary>The built lift lines' TRUE end nodes (one lift-line edge at the node, StationAlign.LineEnds. An interior tower node
        /// has two) that this frame's preview touches: a Temp node's Temp.m_Original is the end node. Vanilla gives a node a Temp copy
        /// (Temp.m_Original, the original Hidden: GenerateNodesSystem.cs:1226-1228, :1306) whenever a preview's net definition connects to
        /// it. The 54(a) probe saw that copy on every station snap, merged into the stub (SkiLiftMarker) or not. Edges: lift-line prefab,
        /// no Owner (so not a station's own stub sub-net), not Temp, not Deleted.</summary>
        private void ReadSnappedEnds()
        {
            m_SnappedEnds.Clear();
            m_TempCopies.Clear();
            using (NativeArray<Entity> nodes = m_TempNodeQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Temp> temps = m_TempNodeQuery.ToComponentDataArray<Temp>(Allocator.Temp))
            {
                for (int i = 0; i < nodes.Length; i++)
                    if (temps[i].m_Original != Entity.Null && (temps[i].m_Flags & TempFlags.Delete) == 0)
                        m_TempCopies[temps[i].m_Original] = nodes[i];
            }
            if (m_TempCopies.Count == 0)
                return;
            m_EdgePairs.Clear();
            IReadOnlyDictionary<Entity, LiftTypeDef> lines = m_PrefabCatalog.LiftLineTypes;
            using (NativeArray<Entity> edges = m_LineEdgeQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_LineEdgeQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    if (!lines.ContainsKey(prefabs[i].m_Prefab))
                        continue;
                    Edge edge = EntityManager.GetComponentData<Edge>(edges[i]);
                    m_EdgePairs.Add(new KeyValuePair<Entity, Entity>(edge.m_Start, edge.m_End));
                }
            }
            StationAlign.LineEnds(m_EdgePairs, m_Degree, m_Next, m_Ends);
            foreach (KeyValuePair<Entity, Entity> end in m_Ends)
            {
                if (!m_TempCopies.TryGetValue(end.Key, out Entity copy))
                    continue;
                if (!EntityManager.TryGetComponent(end.Key, out Node a) || !EntityManager.TryGetComponent(end.Value, out Node b))
                    continue;
                m_SnappedEnds.Add(new SnappedEnd { End = end.Key, Copy = copy, EndXz = a.m_Position.xz, NextXz = b.m_Position.xz });
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
                    Mod.Log.Info($"StationFacing: REFUSED — {what}; shown red, " +
                                 "cannot be placed (the station faces away from the line)");
                    break;
                case PowerJoinEpisode.Event.Ended:
                    Mod.Log.Info($"StationFacing: refusal episode ended after {m_Episode.Evaluations} refusing evaluation(s)");
                    break;
            }
        }
    }
}
