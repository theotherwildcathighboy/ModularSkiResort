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
//
// CATALOG PHASE A (2026-10-11, docs/research/art/assets-2026-10/catalog-a/phase-a-contract.md R6; extensibility.md
// §1 row 20): the drawn line names and the aim height come from SkiCatalog.Lifts (one row today: SkiChairliftHV,
// AimHeightM 8.0); the prefab entity from SkiPrefabCatalogSystem (one resolver, per load; its "PrefabCatalog [arm]"
// line lists every name FOUND / MISSING). This system's own WARN on a missing line stays. A mid-session prefab
// replacement loses the patch until the next load (unchanged from before; the catalog's [re-resolve] WARN now shows
// it — contract risk R-f).
//
// CATALOG PHASE B (2026-10-11, docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §6.4, lift group): the
// loop already covers every SkiCatalog.Lifts row with its own AimHeightM (fixed-grip 8.0, detachable 9.0, gondola 11.0,
// tram 26.25). A PENDING line (LiftTypeDef.LineExpected false) that is MISSING writes nothing and no WARN; one Info line
// lists the pending lines (silence must be impossible). An EXPECTED line missing keeps the WARN.

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
        // Catalog Phase A (ext row 20; was CursorAimSystem.cs:52 CursorAimHeight = 8.0f and :56 DrawnNetPrefabNames
        // { "SkiChairliftHV" }): the aim height is LiftTypeDef.AimHeightM (SkiCatalog.FixedGrip.AimHeightM: the tower
        // head, axle 7.04, comm 8.15 — 8.0 sits at the sheave assembly, the vanilla-pole "top of the pole at the
        // cursor" feel), the drawn line is LiftTypeDef.LinePrefab. Only drawn prefabs matter — the tool reads the
        // field from the prefab being drawn — so every SkiCatalog.Lifts row is a drawn line.

        // Catalog Phase A (ext row 20; was CursorAimSystem.cs:59 + :65 m_PrefabScanQuery and the :96-117 resolver).
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
            var already = new System.Text.StringBuilder();   // Catalog Phase B: "fixedGrip 8.0, …" (log only, once per load)
            var pending = new System.Text.StringBuilder();   // Catalog Phase B: pending lines MISSING (no WARN, listed)
            for (int l = 0; l < SkiCatalog.Lifts.Count; l++)
            {
                // Catalog Phase A (ext row 20; was CursorAimSystem.cs:96-117, the PrefabID + name-scan resolver copy):
                // the catalog's line entity (Entity.Null when MISSING → the WARN below, as before).
                LiftTypeDef def = SkiCatalog.Lifts[l];
                string name = def.LinePrefab;
                float aim = def.AimHeightM;
                Entity prefabEntity = m_PrefabCatalog.LineEntity(def);

                // Catalog Phase B (round-2-contract §6.4): a PENDING line (not imported yet; LiftTypeDef.LineExpected false)
                // that is MISSING writes nothing and no WARN — the catalog's [arm] line lists it; listed in the Info below.
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
                    lastAim = aim;   // Catalog Phase B: the already-patched line's own aim (was the loop's last row)
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
