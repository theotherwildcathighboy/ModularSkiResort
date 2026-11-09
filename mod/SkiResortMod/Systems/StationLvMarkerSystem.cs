// The LV stub marker: shows where a base station takes power. A base station takes it ONLY through its own LV stub, never from its
// road (PIPELINE §6, T117), and the stub is a 3 m marker edge only the electricity view shows. This system draws a ring around the
// stub and a bar along it (StationLvRule.MarkStyle picks the look, StationLvStubProbe reads the stub and its wire):
//   The LV power-line tool, or the electricity infoview   every base station: orange = no wire, faint green = wired.
//   A base station selected                               that station, full color (orange / green).
//   A base station being placed (object tool ghost)       yellow at the catalog's LV stub (StationDef.LvStubLocalA/B), so the
//                                                         player can turn the station toward a power line.
// The LV tool = ToolSystem.activeTool (ToolSystem.cs:84-99) is the NetToolSystem and its prefab (NetToolSystem.cs:5374) has
// ElectricityConnectionData.m_Voltage Low (Game.Prefabs/ElectricityConnection.cs:13, :27-38). The power view = ToolSystem.activeInfoview
// (ToolSystem.cs:133) has a NetStatusInfomodePrefab of NetStatusType.LowVoltageFlow (typed, not by name; that the vanilla Electricity
// view carries one is prefab data, UNVERIFIED until the field check).
// Cost: nothing runs unless a mode is on. The power view rescans the stations (the PowerBridgeSystem query, filtered by the catalog's
// station set) every RescanFrames frames while on and draws from the cached rows between. Selected probes one entity on the same
// cadence or at once on a new selection. Placing reads the Temp buildings (a handful) every frame, as SkiStationAlignSystem does.
// Draw: Buffer.DrawCircle / DrawLine with StyleFlags.Projected (OverlayRenderSystem.cs:99-104, :138-141, :207-211). A projected mark
// drapes on the terrain over the whole height range (GetBuffer, :603-634), so no terrain sampling is needed.
// Phase: Rendering, UpdateBefore<StationLvMarkerSystem, Game.Rendering.OverlayRenderSystem> (Mod.cs). Load-bearing: the renderer
// consumes and clears its lists in the same frame. Gated by Settings > Lifts > LiftBuildAids. Game mode only. Writes nothing.
// Logs: ARMED once per load, the first mark of each mode per load (with counts), a one-shot `!!!` that switches it off until the next
// load. Nothing per frame. DUMP: AppendDump.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class StationLvMarkerSystem : GameSystemBase
    {
        private static class Tuning
        {
            public const int RescanFrames = 30;        // the power view's station list and wire states refresh (0.5 s at 60 fps)
            public const float BarWidthM = 1.5f;       // the bar along the 3 m stub
            public const float OutlineWidthM = 1.2f;   // the ring's outline
        }

        private struct Row
        {
            public Entity Station;
            public Bezier4x3 Curve;
            public LvStubWire Wire;
        }

        // sRGB / 255. OverlayRenderSystem converts with Color.linear.
        private static readonly UnityEngine.Color s_Orange = new UnityEngine.Color(1f, 140f / 255f, 25f / 255f, 1f);
        private static readonly UnityEngine.Color s_Green = new UnityEngine.Color(90f / 255f, 215f / 255f, 100f / 255f, 1f);
        private static readonly UnityEngine.Color s_Yellow = new UnityEngine.Color(1f, 215f / 255f, 50f / 255f, 1f);

        private ToolSystem m_ToolSystem;
        private ObjectToolSystem m_ObjectTool;
        private NetToolSystem m_NetTool;
        private PrefabSystem m_PrefabSystem;
        private SkiPrefabCatalogSystem m_Catalog;
        private OverlayRenderSystem m_Overlay;
        private CameraUpdateSystem m_Camera;
        private EntityQuery m_StationQuery;       // every live building with sub-nets (PowerBridgeSystem's m_AllStationQuery)
        private EntityQuery m_TempStationQuery;   // the object tool's ghost (Temp building)

        private PrefabBase m_NetPrefabRef, m_ObjectPrefabRef;
        private InfoviewPrefab m_InfoviewRef;
        private bool m_NetIsLv, m_InfoviewIsPower;
        private Entity m_ObjectPrefabEntity;
        private StationDef m_ObjectBaseDef;       // the object tool's prefab when it is a base station with an LV stub, else null

        private readonly List<Row> m_Rows = new List<Row>(16);
        private LvMarkMode m_RowsMode;
        private Entity m_RowsSelected;
        private int m_LastScanFrame = int.MinValue;
        private bool m_Game, m_Disabled, m_ErrorLogged;
        private readonly bool[] m_ModeLogged = new bool[4];

        /// <summary>This frame's mode and the counters (DUMP).</summary>
        public LvMarkMode Mode { get; private set; }
        public int Draws, Scans;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_ObjectTool = World.GetOrCreateSystemManaged<ObjectToolSystem>();
            m_NetTool = World.GetOrCreateSystemManaged<NetToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_StationQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<Game.Net.SubNet>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_TempStationQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>());
            Mod.Log.Info($"{nameof(StationLvMarkerSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Game = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = false;
            m_NetPrefabRef = m_ObjectPrefabRef = null;
            m_InfoviewRef = null;
            m_NetIsLv = m_InfoviewIsPower = false;
            m_ObjectPrefabEntity = Entity.Null;
            m_ObjectBaseDef = null;
            m_Rows.Clear();
            m_RowsMode = LvMarkMode.None;
            m_RowsSelected = Entity.Null;
            m_LastScanFrame = int.MinValue;
            Array.Clear(m_ModeLogged, 0, m_ModeLogged.Length);
            Mode = LvMarkMode.None;
            Draws = Scans = 0;
            if (m_Game)
                Mod.Log.Info("StationLvMark: ARMED — marks each base station's LV stub (ring + bar): every base station while the LV " +
                             "power-line tool or the electricity view is on (orange = no wire, faint green = wired), a selected base station, " +
                             $"and the stub of a base station being placed (yellow); rescan every {Tuning.RescanFrames} frames while on; " +
                             "option LiftBuildAids");
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Game)
                return;
            try
            {
                Entity selected = SelectedBase();
                bool placing = PlacingBase();
                LvMarkMode mode = StationLvRule.Decide(Mod.Settings == null || Mod.Settings.LiftBuildAids, placing,
                                                       PowerViewOn(), selected != Entity.Null);
                Mode = mode;
                if (mode == LvMarkMode.None)
                {
                    m_RowsMode = LvMarkMode.None;
                    return;
                }

                float scale = StationLvRule.MarkScale(ReadZoom());
                if (mode == LvMarkMode.Placing)
                {
                    if (!TryGhost(out Transform t))
                        return;   // no preview this frame
                    var curve = new Bezier4x3(
                        Game.Objects.ObjectUtils.LocalToWorld(t, m_ObjectBaseDef.LvStubLocalA),
                        float3.zero, float3.zero,
                        Game.Objects.ObjectUtils.LocalToWorld(t, m_ObjectBaseDef.LvStubLocalB));
                    OverlayRenderSystem.Buffer gb = m_Overlay.GetBuffer(out JobHandle gdeps);   // OverlayRenderSystem.cs:603-634
                    gdeps.Complete();                                                            // main-thread write
                    DrawMark(ref gb, curve.a, curve.d, LvMarkStyle.Ghost, scale);
                    Draws++;
                    LogFirst(mode, $"ghost of {m_ObjectPrefabRef?.name} at ({curve.a.x:F1}, {curve.a.z:F1}) -> ({curve.d.x:F1}, {curve.d.z:F1})");
                    return;
                }

                Refresh(mode, selected);
                if (m_Rows.Count == 0)
                    return;
                OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);   // OverlayRenderSystem.cs:603-634
                deps.Complete();                                                               // main-thread write, no AddBufferWriter
                for (int i = 0; i < m_Rows.Count; i++)
                {
                    Row r = m_Rows[i];
                    LvMarkMode rowMode = r.Station == selected ? LvMarkMode.Selected : mode;
                    DrawMark(ref buffer, r.Curve.a, r.Curve.d, StationLvRule.MarkStyle(rowMode, r.Wire), scale);
                }
                Draws++;
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mode = LvMarkMode.None;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(StationLvMarkerSystem)} EXCEPTION — the LV stub marker is off until the next game load " +
                                  $"(power is unaffected). Please report this stack:\n{e}");
                }
            }
        }

        /// <summary>Rebuilds the rows on a mode change, a new selection, or every RescanFrames.</summary>
        private void Refresh(LvMarkMode mode, Entity selected)
        {
            int frame = UnityEngine.Time.frameCount;
            if (mode == m_RowsMode && selected == m_RowsSelected && frame - m_LastScanFrame < Tuning.RescanFrames)
                return;
            m_RowsMode = mode;
            m_RowsSelected = selected;
            m_LastScanFrame = frame;
            Scans++;
            m_Rows.Clear();
            if (mode == LvMarkMode.Selected)
            {
                AddRow(selected);
            }
            else if (!m_StationQuery.IsEmptyIgnoreFilter)
            {
                HashSet<Entity> stationPrefabs = m_Catalog.StationPrefabSet;
                using (NativeArray<ArchetypeChunk> chunks = m_StationQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    EntityTypeHandle entityHandle = GetEntityTypeHandle();
                    ComponentTypeHandle<PrefabRef> prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                    var stations = new List<Entity>(16);
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        NativeArray<Entity> entities = chunks[c].GetNativeArray(entityHandle);
                        NativeArray<PrefabRef> prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                        for (int i = 0; i < entities.Length; i++)
                        {
                            if (stationPrefabs.Contains(prefabs[i].m_Prefab))
                                stations.Add(entities[i]);
                        }
                    }
                    for (int i = 0; i < stations.Count; i++)
                        AddRow(stations[i]);
                }
            }
            if (!m_ModeLogged[(int)mode])
            {
                int unwired = 0;
                for (int i = 0; i < m_Rows.Count; i++)
                    if (m_Rows[i].Wire == LvStubWire.None)
                        unwired++;
                LogFirst(mode, $"{m_Rows.Count} base station stub(s), {unwired} without a wire");
            }
        }

        private void AddRow(Entity station)
        {
            if (station == Entity.Null || !EntityManager.Exists(station) || EntityManager.HasComponent<Deleted>(station))
                return;
            if (!StationLvStubProbe.TryProbe(EntityManager, station, out Bezier4x3 curve, out LvStubWire wire, out _, out _))
                return;   // a top station: no LV stub
            if (!math.all(math.isfinite(curve.a)) || !math.all(math.isfinite(curve.d)))
                return;
            m_Rows.Add(new Row { Station = station, Curve = curve, Wire = wire });
        }

        private void LogFirst(LvMarkMode mode, string what)
        {
            if (m_ModeLogged[(int)mode])
                return;
            m_ModeLogged[(int)mode] = true;
            Mod.Log.Info($"StationLvMark [{mode}]: first mark this load — {what}");
        }

        private static void DrawMark(ref OverlayRenderSystem.Buffer buffer, float3 a, float3 d, LvMarkStyle style, float scale)
        {
            UnityEngine.Color c;
            float alpha;
            switch (style)
            {
                case LvMarkStyle.Unwired: c = s_Orange; alpha = 0.95f; break;
                case LvMarkStyle.Wired: c = s_Green; alpha = 0.95f; break;
                case LvMarkStyle.WiredFaint: c = s_Green; alpha = 0.45f; break;
                case LvMarkStyle.Ghost: c = s_Yellow; alpha = 0.95f; break;
                default: return;
            }
            var outline = new UnityEngine.Color(c.r, c.g, c.b, alpha);
            var fill = new UnityEngine.Color(c.r, c.g, c.b, alpha * 0.25f);
            var flags = OverlayRenderSystem.StyleFlags.Projected;
            float3 mid = (a + d) * 0.5f;
            float2 dir = math.normalizesafe(d.xz - a.xz, new float2(0f, 1f));
            buffer.DrawCircle(outline, fill, Tuning.OutlineWidthM * scale, flags, dir, mid, StationLvRule.MarkDiameterM * scale);
            buffer.DrawLine(outline, outline, 0f, flags, new Line3.Segment(a, d), Tuning.BarWidthM * scale, new float2(1f, 1f));
        }

        /// <summary>The selected entity when it is a placed base station with an LV stub, else Entity.Null.</summary>
        private Entity SelectedBase()
        {
            Entity sel = m_ToolSystem.selected;                                 // ToolSystem.cs:101
            if (sel == Entity.Null || !EntityManager.Exists(sel) || EntityManager.HasComponent<Temp>(sel) ||
                !EntityManager.TryGetComponent(sel, out PrefabRef pr) ||
                !m_Catalog.TryGetStation(pr.m_Prefab, out StationDef def) || !def.HasLvStub)
                return Entity.Null;
            return sel;
        }

        /// <summary>The LV power-line tool or the electricity infoview is on. Classified once per prefab change.</summary>
        private bool PowerViewOn()
        {
            if (m_ToolSystem.activeTool == m_NetTool)                          // ToolSystem.cs:84-99
            {
                NetPrefab prefab = m_NetTool.prefab;                            // NetToolSystem.cs:5374
                if (!ReferenceEquals(prefab, m_NetPrefabRef))
                {
                    m_NetPrefabRef = prefab;
                    m_NetIsLv = !ReferenceEquals(prefab, null) && m_PrefabSystem.TryGetEntity(prefab, out Entity e) &&
                                EntityManager.TryGetComponent(e, out ElectricityConnectionData c) &&
                                c.m_Voltage == Game.Prefabs.ElectricityConnection.Voltage.Low;
                }
                if (m_NetIsLv)
                    return true;
            }
            InfoviewPrefab view = m_ToolSystem.activeInfoview;                 // ToolSystem.cs:133
            if (!ReferenceEquals(view, m_InfoviewRef))
            {
                m_InfoviewRef = view;
                m_InfoviewIsPower = false;
                if (!ReferenceEquals(view, null) && view.m_Infomodes != null)
                {
                    for (int i = 0; i < view.m_Infomodes.Length; i++)
                    {
                        if (view.m_Infomodes[i]?.m_Mode is NetStatusInfomodePrefab ns && ns.m_Type == NetStatusType.LowVoltageFlow)
                            m_InfoviewIsPower = true;
                    }
                }
            }
            return m_InfoviewIsPower;
        }

        /// <summary>The object tool is placing or moving a base station with an LV stub (m_ObjectBaseDef set).</summary>
        private bool PlacingBase()
        {
            if (m_ToolSystem.activeTool != m_ObjectTool)
                return false;
            ObjectToolSystem.Mode mode = m_ObjectTool.actualMode;
            if (mode != ObjectToolSystem.Mode.Create && mode != ObjectToolSystem.Mode.Move)
                return false;
            ObjectPrefab prefab = m_ObjectTool.prefab;
            if (!ReferenceEquals(prefab, m_ObjectPrefabRef))
            {
                m_ObjectPrefabRef = prefab;
                m_ObjectPrefabEntity = Entity.Null;
                m_ObjectBaseDef = null;
                if (!ReferenceEquals(prefab, null) && m_PrefabSystem.TryGetEntity(prefab, out Entity e) &&
                    m_Catalog.TryGetStation(e, out StationDef def) && def.HasLvStub)
                {
                    m_ObjectPrefabEntity = e;
                    m_ObjectBaseDef = def;
                }
            }
            return m_ObjectBaseDef != null;
        }

        /// <summary>The ghost of the base station being placed: the first Temp building of the tool's prefab not flagged Delete.</summary>
        private bool TryGhost(out Transform transform)
        {
            transform = default;
            if (m_ObjectPrefabEntity == Entity.Null || m_TempStationQuery.IsEmptyIgnoreFilter)
                return false;
            using (NativeArray<ArchetypeChunk> chunks = m_TempStationQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                ComponentTypeHandle<PrefabRef> prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                ComponentTypeHandle<Temp> tempHandle = GetComponentTypeHandle<Temp>(isReadOnly: true);
                ComponentTypeHandle<Transform> transformHandle = GetComponentTypeHandle<Transform>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    NativeArray<PrefabRef> prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    NativeArray<Temp> temps = chunks[c].GetNativeArray(ref tempHandle);
                    for (int i = 0; i < prefabs.Length; i++)
                    {
                        if (prefabs[i].m_Prefab != m_ObjectPrefabEntity || (temps[i].m_Flags & TempFlags.Delete) != 0)
                            continue;
                        transform = chunks[c].GetNativeArray(ref transformHandle)[i];
                        return math.all(math.isfinite(transform.m_Position));
                    }
                }
            }
            return false;
        }

        private float ReadZoom()
        {
            try
            {
                IGameCameraController controller = m_Camera.activeCameraController;
                return controller != null ? controller.zoom : 0f;
            }
            catch (Exception)
            {
                return 0f;   // a controller being torn down: scale 1
            }
        }

        /// <summary>DUMP: mode, rows of the last scan with their wire state, counters.</summary>
        internal void AppendDump(StringBuilder sb)
        {
            sb.Append("  station LV marker: game ").Append(m_Game).Append(", disabled ").Append(m_Disabled).Append(", mode ").Append(Mode)
              .Append(", scans ").Append(Scans).Append(", draws ").Append(Draws).Append(", last rows ").Append(m_Rows.Count).Append(';');
            for (int i = 0; i < m_Rows.Count; i++)
            {
                Row r = m_Rows[i];
                sb.Append(' ').Append(r.Station.Index.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(r.Station.Version.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(r.Wire).Append(';');
            }
            sb.Append('\n');
        }
    }
}
