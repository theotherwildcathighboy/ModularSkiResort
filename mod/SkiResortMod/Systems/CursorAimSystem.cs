// CursorAimSystem — puts the net tool's aim plane at the tower head for our lift line,
// the code-mod decouple that terrain-carving.md §5 established prefab data cannot provide.
//
// THE MECHANISM (decompile-verified, and simpler than the composition-surgery sketch):
// the tool's aim plane does NOT read the runtime composition at raycast time. NetToolSystem
// line 5893 reads the drawn PREFAB's NetGeometryData directly:
//
//     m_PrefabSystem.TryGetComponentData<NetGeometryData>(m_Prefab, out var component4);
//     m_ToolRaycastSystem.rayOffset = new float3(0, -component4.m_DefaultSurfaceHeight.max - elevation, 0);
//
// i.e. the cursor aims at a plane m_DefaultSurfaceHeight.max above the terrain hit — the
// "top of the pole at the cursor" feel of vanilla power lines. m_DefaultSurfaceHeight is
// written once at prefab init (NetInitializeSystem line 139, copying the flags=0
// composition's m_SurfaceHeight, which for surface-less pieces is center(m_HeightRange) —
// NetCompositionHelpers ~986). Our band {6,20} therefore aims at 13 m; this system
// rewrites the PREFAB field to 8.0 after init, and the aim plane rides the tower head.
//
// COMPLETE CONSUMER AUDIT of NetGeometryData.m_DefaultSurfaceHeight (grep across ALL
// namespaces, per the terrain-carving.md §6 lesson):
//   * NetToolSystem 5893 (rayOffset — the target) and 5946/5947/5954 (over/underground
//     camera hints for the net tool) — the intended change;
//   * IconCommandSystem 541 — notification icon hover height above net nodes: icons drop
//     from 13 m to 8 m above nodes, closer to the hardware — cosmetic improvement;
//   * SelectedInfoUISystem 725/738 — camera focus height for a selected net — cosmetic;
//   * NetCompositionSystem 154 — seeds m_SurfaceHeight ONLY for piece-LESS compositions
//     (the marker-net fallback path; our drawn line's compositions have pieces and get
//     center(m_HeightRange) from NetCompositionHelpers instead, unaffected by this patch).
// NOT in the list: terrain carving (TerrainSystem.AddSegment reads composition
// m_HeightRange for our surface-less class — terrain-carving.md §2, confirmed) and
// collision (HasSurface/piece-driven). The decouple is data-clean.
//
// Idempotent, once per load (prefab entities persist per app run, but a re-patch after
// any future prefab refresh costs nothing); pure data write on the prefab entity; no
// per-frame work (Enabled = false, empty OnUpdate — the SnowLineSystem pattern).

using System;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class CursorAimSystem : GameSystemBase
    {
        /// <summary>Aim-plane height above terrain: the tower head (axle 7.04, comm 8.15 —
        /// 8.0 sits at the sheave assembly, matching the vanilla-pole "top of the pole at
        /// the cursor" feel the user asked for).</summary>
        private const float CursorAimHeight = 8.0f;

        /// <summary>Player-drawn net prefabs whose aim plane gets the tower-head treatment.
        /// Only drawn prefabs matter — the tool reads the field from the prefab being drawn.</summary>
        private static readonly string[] DrawnNetPrefabNames = { "SkiChairliftHV" };

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_PrefabScanQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            Enabled = false; // no per-frame work; OnGameLoadingComplete still fires
            Mod.Log.Info($"{nameof(CursorAimSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;

            try
            {
                PatchAimPlanes();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"!!! {nameof(CursorAimSystem)} EXCEPTION — cursor aim patch skipped this load:\n{e}");
            }
        }

        protected override void OnUpdate()
        {
        }

        private void PatchAimPlanes()
        {
            int patched = 0, alreadyPatched = 0;
            foreach (string name in DrawnNetPrefabNames)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(PowerLinePrefab), name), out PrefabBase prefabBase))
                    m_PrefabSystem.TryGetEntity(prefabBase, out prefabEntity);

                if (prefabEntity == Entity.Null)
                {
                    var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                            {
                                prefabEntity = candidates[i];
                                break;
                            }
                        }
                    }
                    finally
                    {
                        candidates.Dispose();
                    }
                }

                if (prefabEntity == Entity.Null ||
                    !EntityManager.TryGetComponent<NetGeometryData>(prefabEntity, out var geometryData))
                {
                    Mod.Log.Warn($"{nameof(CursorAimSystem)}: net prefab '{name}' not found or has no " +
                                 "NetGeometryData — aim plane left at the vanilla-derived value");
                    continue;
                }

                if (geometryData.m_DefaultSurfaceHeight.max == CursorAimHeight &&
                    geometryData.m_DefaultSurfaceHeight.min == CursorAimHeight)
                {
                    alreadyPatched++;
                    continue;
                }

                float oldAim = geometryData.m_DefaultSurfaceHeight.max;
                geometryData.m_DefaultSurfaceHeight = new Bounds1(CursorAimHeight, CursorAimHeight);
                EntityManager.SetComponentData(prefabEntity, geometryData);
                patched++;
                Mod.Log.Info($"CursorAim: patched '{name}' DefaultSurfaceHeight {oldAim:F1} → " +
                             $"{CursorAimHeight:F1} — aim plane now rides the tower head");
            }

            if (patched == 0 && alreadyPatched > 0)
                Mod.Log.Info($"CursorAim: {alreadyPatched} net prefab(s) already patched (aim plane {CursorAimHeight:F1})");
        }
    }
}
