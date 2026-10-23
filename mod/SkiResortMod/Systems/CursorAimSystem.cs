// Puts the net tool's aim plane at the tower head for our lift lines (prefab data alone cannot provide this).
// The aim plane does not read the runtime composition at raycast time: NetToolSystem line 5893 reads the drawn PREFAB's
// NetGeometryData directly:
//
//     m_PrefabSystem.TryGetComponentData<NetGeometryData>(m_Prefab, out var component4);
//     m_ToolRaycastSystem.rayOffset = new float3(0, -component4.m_DefaultSurfaceHeight.max - elevation, 0);
// i.e. the cursor aims at a plane m_DefaultSurfaceHeight.max above the terrain hit. m_DefaultSurfaceHeight is written once at
// prefab init (NetInitializeSystem line 139, copying the flags=0 composition's m_SurfaceHeight, which for surface-less pieces is
// center(m_HeightRange), NetCompositionHelpers ~986). Our band {6,20} would aim at 13 m; this system rewrites the prefab field to
// the lift's AimHeightM after init, and the aim plane rides the tower head.
//
// Consumers of NetGeometryData.m_DefaultSurfaceHeight (grep across all namespaces):
//   * NetToolSystem 5893 (rayOffset) and 5946/5947/5954 (over/underground camera hints): the intended change;
//   * IconCommandSystem 541: notification icon hover height above net nodes (icons drop closer to the hardware), cosmetic;
//   * SelectedInfoUISystem 725/738: camera focus height for a selected net, cosmetic;
//   * NetCompositionSystem 154: seeds m_SurfaceHeight only for piece-less compositions (marker-net fallback; our drawn
//     compositions have pieces and take center(m_HeightRange) from NetCompositionHelpers, unaffected).
// Not affected: terrain carving (TerrainSystem.AddSegment reads composition m_HeightRange for our surface-less class) and
// collision (HasSurface / piece-driven).
//
// Idempotent, once per load; pure data write on the prefab entity; no per-frame work (Enabled = false, empty OnUpdate).
// Line names and aim heights come from SkiCatalog.Lifts (LiftTypeDef.AimHeightM per row), the prefab entity from
// SkiPrefabCatalogSystem. A mid-session prefab replacement loses the patch until the next load (the catalog's [re-resolve] WARN
// shows it). A pending line (LiftTypeDef.LineExpected false) that is MISSING writes nothing and no WARN; one Info line lists the
// pending lines. An expected line missing keeps the WARN.

using System;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class CursorAimSystem : GameSystemBase
    {
        // The aim height is LiftTypeDef.AimHeightM (FixedGrip: tower head, axle 7.04, comm 8.15; 8.0 sits at the sheave assembly, the
        // vanilla-pole "top of the pole at the cursor" feel), the drawn line is LiftTypeDef.LinePrefab. Only drawn prefabs matter (the
        // tool reads the field from the prefab being drawn), so every SkiCatalog.Lifts row is a drawn line.

        private SkiPrefabCatalogSystem m_PrefabCatalog;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
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
            float lastAim = SkiCatalog.FixedGrip.AimHeightM;
            var already = new System.Text.StringBuilder(); // "fixedGrip 8.0, …" (log only, once per load)
            var pending = new System.Text.StringBuilder(); // pending lines MISSING (no WARN, listed)
            for (int l = 0; l < SkiCatalog.Lifts.Count; l++)
            {
                // The catalog's line entity (Entity.Null when MISSING → the WARN below).
                LiftTypeDef def = SkiCatalog.Lifts[l];
                string name = def.LinePrefab;
                float aim = def.AimHeightM;
                Entity prefabEntity = m_PrefabCatalog.LineEntity(def);

                // A pending line (not imported yet; LiftTypeDef.LineExpected false) that is MISSING writes nothing and no WARN; the Info
                // line below lists it.
                if (prefabEntity == Entity.Null && !def.LineExpected)
                {
                    if (pending.Length > 0)
                        pending.Append(", ");
                    pending.Append(name);
                    continue;
                }

                if (prefabEntity == Entity.Null ||
                    !EntityManager.TryGetComponent<NetGeometryData>(prefabEntity, out var geometryData))
                {
                    Mod.Log.Warn($"{nameof(CursorAimSystem)}: net prefab '{name}' not found or has no " +
                                 "NetGeometryData — aim plane left at the vanilla-derived value");
                    continue;
                }

                if (geometryData.m_DefaultSurfaceHeight.max == aim &&
                    geometryData.m_DefaultSurfaceHeight.min == aim)
                {
                    alreadyPatched++;
                    lastAim = aim; // the already-patched line's own aim
                    if (already.Length > 0)
                        already.Append(", ");
                    already.Append(def.Id).Append(' ').Append(aim.ToString("F2"));
                    continue;
                }

                float oldAim = geometryData.m_DefaultSurfaceHeight.max;
                geometryData.m_DefaultSurfaceHeight = new Bounds1(aim, aim);
                EntityManager.SetComponentData(prefabEntity, geometryData);
                patched++;
                Mod.Log.Info($"CursorAim: patched '{name}' DefaultSurfaceHeight {oldAim:F1} → " +
                             $"{aim:F1} — aim plane now rides the tower head ({def.Id})");
            }

            if (patched == 0 && alreadyPatched > 0)
                Mod.Log.Info($"CursorAim: {alreadyPatched} net prefab(s) already patched (aim plane {(alreadyPatched == 1 ? lastAim.ToString("F1") : already.ToString())})");
            if (pending.Length > 0)
                Mod.Log.Info($"CursorAim: pending lift line(s) not imported yet, no aim plane to patch: {pending} (Catalog Phase B: " +
                             "no WARN for a pending name; the PrefabCatalog [arm] line lists it)");
            if (SkiCatalog.Lifts.Count == 0)
                Mod.Log.Warn($"{nameof(CursorAimSystem)}: SkiCatalog lists no lift type — no aim plane patched " +
                             "(see the PrefabCatalog lines)");
        }
    }
}
