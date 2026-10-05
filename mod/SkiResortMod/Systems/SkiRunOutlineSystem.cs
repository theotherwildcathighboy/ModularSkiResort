// SkiRunOutlineSystem — thicker run outlines while drawing (Round E1 addendum A1,
// docs/research/econ-round/addendum-a1-run-outline.md; Richard: "districts … have much thicker edge
// lines and larger circles for the nodes").
//
// THE MECHANISM (decompile-verified; 1.6.0f1 citations, every file identical in 1.6.2f1):
//   * The area tool's outline and corner dots are drawn by AreaBorderRenderSystem with widths that are
//     constant multiples of the prefab's AreaGeometryData.m_SnapDistance: node DrawCircle × 0.3 (:517),
//     solid edge DrawLine × 0.3 (:531), dashed edge × 0.2 (:527).
//   * m_SnapDistance is not prefab JSON: AreaInitializeSystem writes it per AREA TYPE at prefab
//     initialisation (:336-344) from AreaUtils.GetMinNodeDistance(AreaType) (Game.Areas/AreaUtils.cs:
//     410-421): Lot 8, District 32, MapTile 64, Space 1, Surface 0.75. Our runs are Surface areas, so
//     their outline is 0.225 m wide; a district's is 9.6 m.
//   * The same number is the area tool's snapping radius (AreaToolSystem, e.g. :353, :792-888) and, halved,
//     the minimum distance between two corners and between a corner and another edge
//     (AreaUtils.GetMinNodeDistance(AreaGeometryData) = m_SnapDistance × 0.5, AreaUtils.cs:55-58, used by
//     ValidationHelpers.cs:667-817); guide circles and sizing (GuideLinesSystem.cs:977-1101), a water
//     sampling tolerance (ValidationHelpers.cs:1081) and the area buffer's vertical bounds
//     (AreaBufferSystem.cs:322-323) read it too. GeometrySystem (:148) and SurfaceExpandSystem (:133) use
//     the per-TYPE constant, not the component: triangulation and rendering of the run are unchanged.
//   So the setting "Run outline size" (SkiResortSettings.RunOutlineSize, 0-16, default 8 = the game's lot
//   value: outline 2.4 m, snapping 8 m, corners at least 4 m apart) is written into the AreaGeometryData of
//   OUR four run prefab entities after the game has initialised them; 0 restores the game's own value,
//   remembered per prefab entity the first time this system sees it. Never a prefab that is not ours.
//
// WHEN: OnGameLoadingComplete (Game and Editor; prefab entities live for the whole app run, so a second
// load in the same session rewrites the same value) and after every settings apply
// (Setting.onSettingsApplied, Game.Settings/Setting.cs:24, :157-160; the Options sliders call
// ApplyAndSave on every change, Game.UI.Menu/AutomaticSettings.cs:1296-1300). The apply callback only sets
// Enabled = true; the write happens in the next OnUpdate (UIUpdate, main thread, paused included), which
// sets Enabled = false again. So nothing runs per frame: the system is disabled except for the one frame
// after a settings apply (the CursorAimSystem pattern plus that one frame).
//
// LOGS: one line per apply — at every load, and after a settings apply only when RunOutlineSize changed:
//   RunOutline: snap distance {old} -> {new} on {n} run prefab(s) (outline about {w:F1} m wide, corners at
//   least {d:F1} m apart)
// and one WARN per load if a run prefab entity or its AreaGeometryData is missing. House contract:
// "created" line; one-shot "!!!" with stack, inert until the next load.
//
// NOTHING ELSE CHANGES: no serialized state, no prefab JSON edit, no component added; the rating and the
// binding read no snap distance (grep of mod/SkiResortMod for m_SnapDistance / GetMinNodeDistance /
// 0.75 / 0.375, 2026-10-04: no consumer).
//
// Created 2026-10-04 (Round E1 stage 0, S0).

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunOutlineSystem : GameSystemBase
    {
        /// <summary>Our four run SurfacePrefabs (house practice: the table is duplicated per system; the
        /// same names as SkiRunOwnershipSystem's *SurfaceNames).</summary>
        private static readonly string[] RunSurfaceNames =
            { "SkiRunGroomedA", "SkiRunSnowmakingA", "SkiRunNightA", "SkiRunNaturalA" };

        /// <summary>The slider's range (SkiResortSettings.RunOutlineSize).</summary>
        private const int MaxOutlineSize = 16;

        /// <summary>AreaBorderRenderSystem.cs:531 (solid edge width) and AreaUtils.cs:57 (min node distance).</summary>
        private const float OutlineWidthPerSnap = 0.3f;
        private const float MinCornerPerSnap = 0.5f;

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_PrefabScanQuery;
        private SkiResortSettings m_SubscribedSettings;

        /// <summary>The game's own m_SnapDistance per run prefab entity, remembered the first time it is
        /// read (prefab entities live for the app run).</summary>
        private readonly Dictionary<Entity, float> m_GameValue = new Dictionary<Entity, float>();

        private bool m_InCity;                       // the last load was Game or Editor
        private bool m_Disabled;                     // one-shot "!!!" this load
        private bool m_ErrorLogged;
        private bool m_MissingWarned;                // one WARN per load
        private int m_AppliedSetting = int.MinValue; // the setting value last written

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            Enabled = false;   // nothing per frame; OnGameLoadingComplete still fires (CursorAimSystem precedent)
            m_SubscribedSettings = Mod.Settings;
            if (m_SubscribedSettings != null)
                m_SubscribedSettings.onSettingsApplied += OnSettingsApplied;
            Mod.Log.Info($"{nameof(SkiRunOutlineSystem)} created (run outline size: at load and on a settings " +
                         $"apply; settings {(m_SubscribedSettings != null ? "watched" : "NOT loaded — load-time apply only")})");
        }

        protected override void OnDestroy()
        {
            if (m_SubscribedSettings != null)
            {
                m_SubscribedSettings.onSettingsApplied -= OnSettingsApplied;
                m_SubscribedSettings = null;
            }
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = false;
            m_ErrorLogged = false;
            m_MissingWarned = false;
            m_InCity = mode == GameMode.Game || mode == GameMode.Editor;
            if (!m_InCity)
                return;
            try
            {
                Apply(true);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>Setting.onSettingsApplied (UI callback, main thread): only schedule the write for the
        /// next OnUpdate. Never throws.</summary>
        private void OnSettingsApplied(Game.Settings.Setting setting)
        {
            if (m_InCity && !m_Disabled)
                Enabled = true;
        }

        protected override void OnUpdate()
        {
            Enabled = false;   // one frame per settings apply (header WHEN)
            if (m_Disabled || !m_InCity)
                return;
            try
            {
                Apply(false);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void Fail(Exception e)
        {
            m_Disabled = true;
            Enabled = false;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiRunOutlineSystem)} EXCEPTION — run outline size not applied " +
                              $"until the next game load (the outline keeps its current size):\n{e}");
            }
        }

        /// <summary>Writes the target snap distance onto every run prefab found. <paramref name="always"/>:
        /// log even when the setting did not change (the load line).</summary>
        private void Apply(bool always)
        {
            SkiResortSettings settings = Mod.Settings;
            int setting = settings != null ? settings.RunOutlineSize : SkiResortSettings.DefaultRunOutlineSize;
            if (setting < 0)
                setting = 0;
            else if (setting > MaxOutlineSize)
                setting = MaxOutlineSize;
            if (!always && setting == m_AppliedSetting)
                return;   // another option was applied; ours is unchanged

            int written = 0, missing = 0;
            float oldMin = float.PositiveInfinity, oldMax = float.NegativeInfinity, newFirst = float.NaN;
            string missingNames = null;
            foreach (string name in RunSurfaceNames)
            {
                Entity prefab = Resolve(name);
                if (prefab == Entity.Null ||
                    !EntityManager.TryGetComponent<AreaGeometryData>(prefab, out AreaGeometryData geometry))
                {
                    missing++;
                    missingNames = missingNames == null ? name : missingNames + ", " + name;
                    continue;
                }
                if (!m_GameValue.TryGetValue(prefab, out float gameValue))
                {
                    gameValue = geometry.m_SnapDistance;
                    m_GameValue[prefab] = gameValue;
                }
                float target = setting > 0 ? setting : gameValue;
                if (geometry.m_SnapDistance < oldMin)
                    oldMin = geometry.m_SnapDistance;
                if (geometry.m_SnapDistance > oldMax)
                    oldMax = geometry.m_SnapDistance;
                if (float.IsNaN(newFirst))
                    newFirst = target;
                if (geometry.m_SnapDistance != target)
                {
                    geometry.m_SnapDistance = target;
                    EntityManager.SetComponentData(prefab, geometry);
                }
                written++;
            }
            m_AppliedSetting = setting;

            if (written > 0)
            {
                string oldText = oldMin == oldMax ? $"{oldMin:0.##}" : $"{oldMin:0.##}-{oldMax:0.##}";
                // Exact text: addendum A1 (the {old} slot prints "min-max" only if the prefabs differed).
                Mod.Log.Info($"RunOutline: snap distance {oldText} -> {newFirst:0.##} on {written} run prefab(s) " +
                             $"(outline about {newFirst * OutlineWidthPerSnap:F1} m wide, corners at least " +
                             $"{newFirst * MinCornerPerSnap:F1} m apart)");
            }
            if (missing > 0 && !m_MissingWarned)
            {
                m_MissingWarned = true;
                Mod.Log.Warn($"RunOutline: {missing} run prefab(s) missing or without AreaGeometryData " +
                             $"({missingNames}) — their outline keeps the game's size (warned once per load)");
            }
        }

        /// <summary>House pattern (SkiRunOwnershipSystem.ResolveSet): fast PrefabID path + name-scan
        /// fallback; Entity.Null when missing (pre-import states must not throw).</summary>
        private Entity Resolve(string name)
        {
            Entity prefabEntity = Entity.Null;
            if (m_PrefabSystem.TryGetPrefab(new PrefabID("SurfacePrefab", name), out PrefabBase prefabBase))
                m_PrefabSystem.TryGetEntity(prefabBase, out prefabEntity);
            if (prefabEntity != Entity.Null)
                return prefabEntity;

            var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                        return candidates[i];
                }
            }
            finally
            {
                candidates.Dispose();
            }
            return Entity.Null;
        }
    }
}
