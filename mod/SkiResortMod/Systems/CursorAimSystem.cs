// Puts the net tool's aim plane at the tower head for our lift lines (prefab data alone cannot provide this): after prefab init it
// rewrites NetGeometryData.m_DefaultSurfaceHeight on the line prefabs to the lift's AimHeightM (NetToolSystem line 5893 reads it).
// Also clears GeometryFlags.FlattenTerrain on the safety net fence's NetGeometryData (PatchFenceTerrain: the fence must not grade the terrain).
// Idempotent, once per load; pure data write on the prefab entity; no per-frame work (Enabled = false, empty OnUpdate).
// Line names and aim heights come from SkiCatalog.Lifts (LiftTypeDef.AimHeightM per row), the prefab entity from SkiPrefabCatalogSystem.
// A mid-session prefab replacement loses the patch until the next load (the catalog's [re-resolve] WARN shows it).
// A pending line (LiftTypeDef.LineExpected false) that is MISSING writes nothing and no WARN; one Info line lists the pending lines.
// An expected line missing keeps the WARN.
// Engine notes: EngineNotes/CursorAimSystem.md

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
                PatchFenceTerrain();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"!!! {nameof(CursorAimSystem)} EXCEPTION — cursor aim patch skipped this load:\n{e}");
            }
        }

        protected override void OnUpdate()
        {
        }

        /// <summary>The safety net fence must not grade the terrain: NetInitializeSystem sets GeometryFlags.FlattenTerrain on EVERY
        /// NetFencePrefab (NetInitializeSystem.cs:2244; vanilla fences stand on flat lots), so a fence drawn up a slope cut a bench into it
        /// (field test 3). Cleared on SkiSafetyFence's NetGeometryData at every load; idempotent, one log line. With no grading a long
        /// span cuts through the hill, so m_EdgeLengthRange.max (200 m, NetInitializeSystem.cs:871) comes down to FenceMaxEdgeM: the tool
        /// then splits the drawn course and samples the terrain for each piece (CourseSplitSystem.cs:3487-3512).</summary>
        private void PatchFenceTerrain()
        {
            Entity fence = m_PrefabCatalog.EntityOf(SkiCatalog.Names.SafetyFence);
            if (fence == Entity.Null || !EntityManager.TryGetComponent(fence, out NetGeometryData geo))
            {
                Mod.Log.Warn($"CursorAim: fence prefab '{SkiCatalog.Names.SafetyFence}' not found or has no NetGeometryData — its " +
                             "FlattenTerrain stays (the fence grades the terrain it crosses)");
                return;
            }
            if ((geo.m_Flags & Game.Net.GeometryFlags.FlattenTerrain) == 0 && geo.m_EdgeLengthRange.max == FenceMaxEdgeM)
            {
                Mod.Log.Info($"CursorAim: fence '{SkiCatalog.Names.SafetyFence}' already has FlattenTerrain cleared and {FenceMaxEdgeM:0} m spans");
                return;
            }
            float oldMax = geo.m_EdgeLengthRange.max;
            geo.m_Flags &= ~Game.Net.GeometryFlags.FlattenTerrain;
            geo.m_EdgeLengthRange.max = FenceMaxEdgeM;
            geo.m_ElevatedLength = FenceMaxEdgeM;
            EntityManager.SetComponentData(fence, geo);
            Mod.Log.Info($"CursorAim: fence '{SkiCatalog.Names.SafetyFence}' FlattenTerrain cleared (flags now {geo.m_Flags}), span max " +
                         $"{oldMax:0} -> {FenceMaxEdgeM:0} m (min {geo.m_EdgeLengthRange.min:0.#} m): the fence follows the ground without grading it " +
                         "(UNVERIFIED in game until drawn up a slope; fences drawn before this keep their long spans)");
        }

        private const float FenceMaxEdgeM = 16f;

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
