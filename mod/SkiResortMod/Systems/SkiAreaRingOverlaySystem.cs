// The ski area radius on the ground: a ring of SkiRunOwnershipSystem.TetherMeters (the "Ski area radius" option) around Welcome Signs.
//   Placing a Welcome Sign  a bright ring around the cursor (where the new area would reach) and a faint ring around every existing sign.
//   A sign selected         a bright ring around it, faint rings around the others.
//   Drawing a run, the lift tool, placing a lodge / school / patrol hut / depot / station: faint rings around every sign, so it shows
//                           whether the thing lands in an area. Lodges, lifts and runs link to the nearest sign within the ring.
// The lodge parking radius (SkiParkingRules.RadiusM) in parking blue, the second layer, independent of the first:
//   Placing a road lodge    a bright ring around the ghost lodge (where the new lodge would count car spaces) and a faint ring around
//                           every road lodge of the last parking census.
//   A road lodge selected   a bright ring around it (it has a census row), faint rings around the other road lodges.
//                           "Road lodge" = a lodge whose road rule takes arrivals at a road (LodgeSupplyRule.IsRoadLodge): the A-frame,
//                           Chalet and Grand, never the Hut or Summit.
// Gated by the ShowSnowLineWhilePlacing option's sibling ShowSkiAreaRadius (both layers). Game mode only.
// Geometry: SkiAreaRadiusRules.RingSegments chords per ring, StyleFlags.Projected. A projected overlay covers the whole terrain height
// range (OverlayRenderSystem.GetBuffer passes heightScaleOffset.y - 50 .. heightScaleOffset.x + 100, OverlayRenderSystem.cs:633 in
// 1.6.2f1). So a chord drapes over a ridge between its ends and no terrain sampling is needed.
// Sign positions: SkiRunOwnershipSystem.CollectGatePositions (a managed scan of every building) on activation and every RescanFrames.
// Phase: Rendering, anchored UpdateBefore<SkiAreaRingOverlaySystem, Game.Rendering.OverlayRenderSystem> (Mod.cs, load-bearing like the snow
// line's: the renderer uploads and clears its lists in the same frame).
// Logs: one INFO line per load (ARMED). A one-shot `!!!` switches it off until the next load. Nothing per frame.
// Engine notes: EngineNotes/SkiAreaRadius.md (both layers), EngineNotes/SkiLodgeParkingSystem.md (the parking radius)

using System;
using System.Collections.Generic;
using Colossal.Entities;
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

namespace SkiResortMod.Systems
{
    public sealed partial class SkiAreaRingOverlaySystem : GameSystemBase
    {
        private static class Tuning
        {
            public const float BrightWidthM = 5f;     // plan-view width at zoom <= PisteMapRules.RefZoomM (x the zoom scale)
            public const float FaintWidthM = 3f;
            public const int RescanFrames = 60;       // sign positions refresh while active
            public const float MaxWidthScale = 10f;   // a whole 10 km ring is in view only far out: wider than the piste map's x6 cap
        }

        public enum RingMode : byte { None = 0, PlacingSign = 1, SignSelected = 2, PlacingLinked = 3 }

        public enum ParkingRingMode : byte { None = 0, PlacingLodge = 1, LodgeSelected = 2 }

        // Warm amber, distinct from the snow line's pale ice (sRGB / 255). OverlayRenderSystem converts with Color.linear.
        private static readonly UnityEngine.Color s_Bright = new UnityEngine.Color(1f, 200f / 255f, 80f / 255f, 0.95f);
        private static readonly UnityEngine.Color s_Faint = new UnityEngine.Color(1f, 200f / 255f, 80f / 255f, 0.30f);
        // Parking blue (the parking sign's), distinct from the amber area ring.
        private static readonly UnityEngine.Color s_ParkBright = new UnityEngine.Color(90f / 255f, 170f / 255f, 1f, 0.95f);
        private static readonly UnityEngine.Color s_ParkFaint = new UnityEngine.Color(90f / 255f, 170f / 255f, 1f, 0.30f);

        private ToolSystem m_ToolSystem;
        private AreaToolSystem m_AreaTool;
        private ObjectToolSystem m_ObjectTool;
        private LiftLineToolSystem m_LiftTool;
        private ToolRaycastSystem m_Raycast;
        private PrefabSystem m_PrefabSystem;
        private SkiRunOwnershipSystem m_Ownership;
        private SkiPrefabCatalogSystem m_Catalog;
        private OverlayRenderSystem m_Overlay;
        private CameraUpdateSystem m_Camera;
        private SkiLodgeParkingSystem m_Parking;
        private EntityQuery m_TempBuildingQuery;   // the ghost of the object tool (Temp building)

        private PrefabBase m_AreaPrefabRef, m_ObjectPrefabRef;
        private Entity m_ObjectPrefabEntity;
        private bool m_AreaPrefabIsRun, m_ObjectIsSign, m_ObjectIsLinked, m_ObjectIsRoadLodge;
        private readonly List<Entity> m_ParkLodges = new List<Entity>(8);
        private readonly List<float2> m_ParkPositions = new List<float2>(8);

        private readonly List<float2> m_Signs = new List<float2>(8);
        private bool m_Active;
        private int m_LastScanFrame = int.MinValue;
        private bool m_Game, m_Disabled, m_ErrorLogged;

        /// <summary>The modes drawn this frame and the counters (dev tab / DUMP).</summary>
        public RingMode Mode { get; private set; }
        public ParkingRingMode ParkingMode { get; private set; }
        public int Draws, Scans, ParkingDraws;

        public string StateText()
        {
            return $"game {m_Game}, disabled {m_Disabled}, mode {Mode}, signs {m_Signs.Count}, radius {SkiRunOwnershipSystem.TetherMeters:F0} m, " +
                   $"scans {Scans}, draws {Draws}; parking mode {ParkingMode}, lodges {m_ParkLodges.Count}, radius {SkiParkingRules.RadiusM:F0} m, " +
                   $"draws {ParkingDraws}";
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_AreaTool = World.GetOrCreateSystemManaged<AreaToolSystem>();
            m_ObjectTool = World.GetOrCreateSystemManaged<ObjectToolSystem>();
            m_LiftTool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
            m_Raycast = World.GetOrCreateSystemManaged<ToolRaycastSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_Parking = World.GetOrCreateSystemManaged<SkiLodgeParkingSystem>();
            m_TempBuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            Mod.Log.Info($"{nameof(SkiAreaRingOverlaySystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Game = mode == GameMode.Game;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Active = false;
            m_Signs.Clear();
            m_LastScanFrame = int.MinValue;
            m_AreaPrefabRef = m_ObjectPrefabRef = null;
            m_ObjectPrefabEntity = Entity.Null;
            m_AreaPrefabIsRun = m_ObjectIsSign = m_ObjectIsLinked = m_ObjectIsRoadLodge = false;
            m_ParkLodges.Clear();
            m_ParkPositions.Clear();
            Mode = RingMode.None;
            ParkingMode = ParkingRingMode.None;
            Draws = Scans = ParkingDraws = 0;
            if (m_Game)
                Mod.Log.Info($"SkiAreaRing: ARMED — draws the ski area radius ({SkiRunOwnershipSystem.TetherMeters:F0} m, option " +
                             "SkiAreaRadiusKm) as a ring: bright around the cursor while a Welcome Sign is placed and around a selected sign, " +
                             "faint around every sign while a sign, run, lift or resort building is placed (option ShowSkiAreaRadius); " +
                             $"{SkiAreaRadiusRules.RingSegments} projected chords per ring, sign scan every {Tuning.RescanFrames} frames; " +
                             $"the lodge parking radius ({SkiParkingRules.RadiusM:F0} m) in blue: bright around a road lodge being placed or " +
                             "selected, faint around the other road lodges of the parking census");
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Game)
                return;
            try
            {
                RingMode mode = Decide(out float2 selected);
                ParkingRingMode parking = DecideParking(out float2 parkAt, out Entity parkLodge);
                Mode = mode;
                ParkingMode = parking;
                if (mode == RingMode.None)
                    m_Active = false;
                if (mode == RingMode.None && parking == ParkingRingMode.None)
                    return;

                float zoom = ReadZoom();
                float scale = zoom > 0f ? math.clamp(zoom / PisteMapRules.RefZoomM, 1f, Tuning.MaxWidthScale) : 1f;
                OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);   // OverlayRenderSystem.cs:603-634
                deps.Complete();                                                               // main-thread write, no AddBufferWriter
                if (mode != RingMode.None)
                    DrawAreaRings(ref buffer, mode, selected, scale);
                if (parking != ParkingRingMode.None)
                    DrawParkingRings(ref buffer, parkAt, parkLodge, scale);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mode = RingMode.None;
                ParkingMode = ParkingRingMode.None;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiAreaRingOverlaySystem)} EXCEPTION — the ski area and parking rings are off until the next game load:\n{e}");
                }
            }
        }

        /// <summary>The ski area rings (the first layer).</summary>
        private void DrawAreaRings(ref OverlayRenderSystem.Buffer buffer, RingMode mode, float2 selected, float scale)
        {
            int frame = UnityEngine.Time.frameCount;
            if (!m_Active || frame - m_LastScanFrame >= Tuning.RescanFrames)
            {
                m_Ownership.CollectGatePositions(m_Signs);
                m_LastScanFrame = frame;
                Scans++;
            }
            m_Active = true;

            float radius = SkiRunOwnershipSystem.TetherMeters;
            bool cursor = false;
            float2 at = default;
            if (mode == RingMode.PlacingSign && m_Raycast.GetRaycastResult(out RaycastResult hit))
            {
                float3 p = hit.m_Hit.m_HitPosition;
                if (math.all(math.isfinite(p)))
                {
                    cursor = true;
                    at = p.xz;
                }
            }
            for (int i = 0; i < m_Signs.Count; i++)
            {
                bool bright = mode == RingMode.SignSelected && math.distancesq(m_Signs[i], selected) < 1f;
                DrawRing(ref buffer, m_Signs[i], radius, bright ? s_Bright : s_Faint, (bright ? Tuning.BrightWidthM : Tuning.FaintWidthM) * scale);
            }
            if (cursor)
                DrawRing(ref buffer, at, radius, s_Bright, Tuning.BrightWidthM * scale);
            Draws++;
        }

        /// <summary>The lodge parking rings (the second layer): faint around every road lodge of the last census except
        /// <paramref name="brightLodge"/>, bright at <paramref name="brightAt"/> (skipped when not finite).</summary>
        private void DrawParkingRings(ref OverlayRenderSystem.Buffer buffer, float2 brightAt, Entity brightLodge, float scale)
        {
            float radius = SkiParkingRules.RadiusM;
            m_Parking.CopyLodgePositions(m_ParkLodges, m_ParkPositions);   // managed copies, a handful of lodges
            for (int i = 0; i < m_ParkLodges.Count; i++)
            {
                if (m_ParkLodges[i] == brightLodge)
                    continue;
                DrawRing(ref buffer, m_ParkPositions[i], radius, s_ParkFaint, Tuning.FaintWidthM * scale);
            }
            if (math.all(math.isfinite(brightAt)))
                DrawRing(ref buffer, brightAt, radius, s_ParkBright, Tuning.BrightWidthM * scale);
            ParkingDraws++;
        }

        /// <summary>What the parking layer draws this frame. <paramref name="at"/>: the bright ring's center (the ghost lodge, else the
        /// cursor, or the selected lodge), NaN for none. <paramref name="lodge"/>: the selected lodge (no faint ring for it), else
        /// Entity.Null.</summary>
        private ParkingRingMode DecideParking(out float2 at, out Entity lodge)
        {
            at = new float2(float.NaN, float.NaN);
            lodge = Entity.Null;
            SkiResortMod.Settings.SkiResortSettings s = Mod.Settings;
            if (s != null && !s.ShowSkiAreaRadius)
                return ParkingRingMode.None;
            if (!m_Ownership.PrefabSetsReady)
                return ParkingRingMode.None;
            if (m_ToolSystem.activeTool == m_ObjectTool)                       // ToolSystem.cs:84-99
            {
                RefreshObjectPrefab();
                if (m_ObjectIsRoadLodge)
                {
                    if (TryGhostPosition(m_ObjectPrefabEntity, out float2 ghost))
                        at = ghost;
                    else if (m_Raycast.GetRaycastResult(out RaycastResult hit) && math.all(math.isfinite(hit.m_Hit.m_HitPosition)))
                        at = hit.m_Hit.m_HitPosition.xz;   // no ghost this frame: the cursor
                    return ParkingRingMode.PlacingLodge;
                }
            }
            // A selected road lodge with a census row (default tool / info panel).
            Entity sel = m_ToolSystem.selected;                                 // ToolSystem.cs:101
            if (sel != Entity.Null && m_Parking.TryGetParking(sel, out LodgeParking p) && p.Done)
            {
                lodge = sel;
                at = EntityManager.Exists(sel) && EntityManager.TryGetComponent(sel, out Game.Objects.Transform t) ? t.m_Position.xz : p.Position;
                return ParkingRingMode.LodgeSelected;
            }
            return ParkingRingMode.None;
        }

        /// <summary>The object tool's ghost of <paramref name="prefab"/> (a Temp building): its plan position. False when there is none.</summary>
        private bool TryGhostPosition(Entity prefab, out float2 position)
        {
            position = default;
            if (prefab == Entity.Null || m_TempBuildingQuery.IsEmptyIgnoreFilter)
                return false;
            using (NativeArray<Entity> temps = m_TempBuildingQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < temps.Length; i++)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(temps[i]).m_Prefab != prefab)
                        continue;
                    float3 pos = EntityManager.GetComponentData<Game.Objects.Transform>(temps[i]).m_Position;
                    if (!math.all(math.isfinite(pos)))
                        continue;
                    position = pos.xz;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The object tool's prefab, classified once per change: a Welcome Sign, a linked building or station, a road lodge.</summary>
        private void RefreshObjectPrefab()
        {
            PrefabBase prefab = m_ObjectTool.GetPrefab();                       // null in Upgrade / Move (ObjectToolSystem.cs:2836-2847)
            if (ReferenceEquals(prefab, m_ObjectPrefabRef))
                return;
            m_ObjectPrefabRef = prefab;
            m_ObjectPrefabEntity = Entity.Null;
            m_ObjectIsSign = m_ObjectIsLinked = m_ObjectIsRoadLodge = false;
            if (ReferenceEquals(prefab, null) || !m_PrefabSystem.TryGetEntity(prefab, out Entity e))
                return;
            m_ObjectPrefabEntity = e;
            m_ObjectIsSign = m_Ownership.IsGatePrefab(e);
            m_ObjectIsLinked = !m_ObjectIsSign && (m_Catalog.IsUpgradeBuilding(e) || m_Catalog.IsStation(e));
            m_ObjectIsRoadLodge = m_Catalog.LodgeDefs.TryGetValue(e, out LodgeDef def) && LodgeSupplyRule.IsRoadLodge(def.RoadRule, true);
        }

        private static void DrawRing(ref OverlayRenderSystem.Buffer buffer, float2 c, float radius, UnityEngine.Color color, float width)
        {
            int n = SkiAreaRadiusRules.RingSegments;
            var flags = OverlayRenderSystem.StyleFlags.Projected;
            var round = new float2(1f, 1f);
            SkiAreaRadiusRules.RingPoint(c.x, c.y, radius, 0, n, out float ax, out float az);
            for (int i = 1; i <= n; i++)
            {
                SkiAreaRadiusRules.RingPoint(c.x, c.y, radius, i, n, out float bx, out float bz);
                buffer.DrawLine(color, color, 0f, flags,
                    new Colossal.Mathematics.Line3.Segment(new float3(ax, 0f, az), new float3(bx, 0f, bz)), width, round);
                ax = bx;
                az = bz;
            }
        }

        /// <summary>What to draw this frame. <paramref name="selected"/> is the selected sign's plan position in SignSelected.</summary>
        private RingMode Decide(out float2 selected)
        {
            selected = default;
            SkiResortMod.Settings.SkiResortSettings s = Mod.Settings;
            if (s != null && !s.ShowSkiAreaRadius)
                return RingMode.None;
            if (!m_Ownership.PrefabSetsReady)
                return RingMode.None;
            ToolBaseSystem tool = m_ToolSystem.activeTool;                    // ToolSystem.cs:84-99
            if (tool == m_ObjectTool)
            {
                RefreshObjectPrefab();
                if (m_ObjectIsSign)
                    return RingMode.PlacingSign;
                if (m_ObjectIsLinked)
                    return RingMode.PlacingLinked;
            }
            else if (tool == m_AreaTool)
            {
                PrefabBase prefab = m_AreaTool.prefab;                          // AreaToolSystem.cs:2637
                if (!ReferenceEquals(prefab, m_AreaPrefabRef))
                {
                    m_AreaPrefabRef = prefab;
                    m_AreaPrefabIsRun = !ReferenceEquals(prefab, null) &&
                                        m_PrefabSystem.TryGetEntity(prefab, out Entity e) && m_Ownership.IsRunPrefab(e);
                }
                if (m_AreaPrefabIsRun)
                    return RingMode.PlacingLinked;
            }
            else if (tool == m_LiftTool)
            {
                return RingMode.PlacingLinked;
            }

            // A selected Welcome Sign (default tool / info panel).
            Entity sel = m_ToolSystem.selected;                                 // ToolSystem.cs:101
            if (sel != Entity.Null && EntityManager.Exists(sel) &&
                EntityManager.TryGetComponent(sel, out PrefabRef pr) && m_Ownership.IsGatePrefab(pr.m_Prefab) &&
                EntityManager.TryGetComponent(sel, out Game.Objects.Transform t))
            {
                selected = t.m_Position.xz;
                return RingMode.SignSelected;
            }
            return RingMode.None;
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
                return 0f;   // a controller being torn down: width x 1
            }
        }
    }
}
