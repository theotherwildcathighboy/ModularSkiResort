// SkiRunAreaTypeSystem — forges the cat track's AreaType to Space at every Game load, so a drawn cat track may CROSS
// other runs (T54 probe 1, the OVERLAP PROBE; docs/research/cat-track-run.md §1 option A, §5 step 0).
// Created 2026-10-16 (T54 probe 1). Decompile 1.6.0f1 citations (the file is byte-identical in 1.6.2f1 for the lines used
// unless stated; re-grep after a patch).
//
// THE PROBLEM. Two runs of ours are both Game.Areas.Surface areas whose prefabs have AreaGeometryData.m_Type == Surface.
// Drawing one across the other fails validation: Game.Areas/ValidationHelpers.cs:551 compares ONLY the two prefabs'
// m_Type; the same type with both areas top-level has no exemption (the early return at :564-570 needs
// m_TopLevelEntity != m_AreaEntity, false for a drawn run), so the triangles are intersected (existing one shrunk 0.1 m,
// :575) and an overlap becomes ErrorType.OverlapExisting at severity Error (:582). The Error tag refuses the click
// (ValidationSystem.cs:1657-1660, ToolBaseSystem.cs:533-540). Which PREFAB a run is makes no difference, only its type.
//
// THE WRITE (W1 of this system). AreaGeometryData.m_Type on the SkiRunCatTrackA PREFAB entity: Surface (4) -> Space (3).
// A DIFFERENT type is exempt unless the existing area has PhysicalGeometry or ProtectedArea (:553-566); Surface and Space
// prefabs get flags 0 (Game.Prefabs/AreaInitializeSystem.cs:343) and AreaUtils.SetCollisionFlags adds Physical only in the
// editor (AreaUtils.cs:679-689), so in Game mode a cat track neither collides with a groomed / snowmaking / night /
// natural run nor is collided with by one. Set at the same place the engine sets it: AreaInitializeSystem.cs:308-345
// (AreaGeometryData.m_Type = Surface for a SurfacePrefab, Space for a SpacePrefab) on the Created prefab entity.
// WHY NOT A SpacePrefab: GeometrySystem gives Space a flat height range (GeometrySystem.cs:134, :372-403) and every
// `Surface` query of ours would need `Space` too (cat-track-run.md §1 option B). WHY NOT AreaType.None: the area batch
// indexes m_AreaTypeData[(int)type] (Game.Rendering/AreaBufferSystem.cs:795; the table is [5]: Lot, District, MapTile,
// Space, Surface at :652-657), so -1 would throw. Space (3) is a valid index.
//
// WHY IT LASTS. Prefab entities live for the app run and only prefab editing re-creates one; AreaInitializeSystem fills
// AreaGeometryData on the Created tag only, so nothing rewrites it. The readers take the prefab's value at every use
// (ValidationHelpers.cs:549-551; AreaToolSystem.cs:171 and :322 compare the snap candidate's prefab type with the tool
// prefab's). A save carries no prefab data (prefab entities read from a save are mapped and destroyed): the write is made
// again at every load, exactly like LodgeRoadSystem W1 and CursorAimSystem (field-confirmed, DECISIONS 13).
//
// WHAT CHANGES FOR THE PLAYER (all UNVERIFIED until the probe is played; the note T54-probe1-overlap.md lists them):
//   * the area tool snaps only to areas of the SAME type (AreaToolSystem.cs:171, :322), so a cat-track corner will not
//     snap to a groomed run's node; it snaps to other cat tracks;
//   * the tool's Space snap set adds Snap.ObjectSurface (AreaToolSystem.cs:3282-3286) and a height weight of 1 for Space
//     (:372, :514, :537, :603, :669);
//   * the raycast type mask becomes Space (:3053), so other runs are not hover targets while drawing.
//
// EDITOR. A map-editor load after a game in the same app run would keep the Space type on the prefab. The type is only
// ever consulted for our prefab, so there is nothing to corrupt, but the game's own value is written back anyway (only
// when the prefab still holds exactly what we wrote), like LodgeRoadSystem.RestoreForEditor.
//
// LOGS. "created"; per Game load ONE line:
//   RunAreaType: prefab SkiRunCatTrackA AreaGeometryData.m_Type Surface (4) -> Space (3) (flags 0x0, snap 0.75): ...
//   or the "already Space ... (written earlier this session)" line, or a WARN when the game set something else.
// A MISSING prefab logs ERROR "RunAreaType: prefab SkiRunCatTrackA is MISSING ..." and nothing is written (the cat track
// then cannot be drawn at all; the catalog's missing-prefab WARN says the same from its side). A throw logs one "!!!".
//
// WRITE SITES: EntityManager.SetComponentData of vanilla AreaGeometryData on the ONE prefab entity. No component is added
// or removed, nothing is serialized by us, no prefab JSON change, no Harmony, no reflection.

using System;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Prefabs;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAreaTypeSystem : GameSystemBase
    {
        /// <summary>The prefab this system forges (= SkiCatalog.Names.RunCatTrack).</summary>
        private const string CatTrackPrefabName = SkiCatalog.Names.RunCatTrack;

        private SkiPrefabCatalogSystem m_PrefabCatalog;

        /// <summary>What was written on the prefab entity this session (prefab entities live for the app run).</summary>
        private struct Written
        {
            public AreaType GameType;
            public AreaType Type;
        }

        private readonly System.Collections.Generic.Dictionary<Entity, Written> m_Written =
            new System.Collections.Generic.Dictionary<Entity, Written>();

        // Per-load state (reset at OnGameLoadingComplete); the last-load outcome, for logs and tests in the field only.
        private string m_State = "not loaded";

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            Enabled = false;   // nothing per frame; OnGameLoadingComplete still fires (CursorAimSystem / LodgeRoadSystem precedent)
            Mod.Log.Info("RunAreaType: system created (the cat track's AreaType is forged to Space at every game load " +
                         "so it can cross other runs; no sweep, no poll)");
        }

        protected override void OnUpdate()
        {
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game)
            {
                m_State = "not a game (mode " + mode + ")";
                if ((mode & GameMode.Editor) != 0)
                {
                    try
                    {
                        RestoreForEditor(mode);
                    }
                    catch (Exception e)
                    {
                        m_State = "EXCEPTION (Editor restore): " + e.GetType().Name;
                        Mod.Log.Error("!!! RunAreaType EXCEPTION — the game's AreaType was not restored on the cat track prefab " +
                                      $"for the Editor (it keeps Space until the app restarts; harmless):\n{e}");
                    }
                }
                return;
            }
            try
            {
                Apply();
            }
            catch (Exception e)
            {
                m_State = "EXCEPTION: " + e.GetType().Name;
                Mod.Log.Error("!!! RunAreaType EXCEPTION — the cat track's AreaType was not forged this load (it cannot cross " +
                              $"other runs until the next load):\n{e}");
            }
        }

        /// <summary>W1: Surface -> Space on the cat track prefab entity. One log line per load.</summary>
        private void Apply()
        {
            Entity prefab = m_PrefabCatalog.EntityOf(CatTrackPrefabName);
            if (prefab == Entity.Null || !EntityManager.TryGetComponent(prefab, out AreaGeometryData data))
            {
                m_State = "prefab missing";
                Mod.Log.Error($"RunAreaType: prefab {CatTrackPrefabName} is MISSING or has no AreaGeometryData — nothing written; " +
                              "the cat track cannot be drawn across other runs this load (is SkiRunCatTrackA in ImportedData?)");
                return;
            }

            AreaType old = data.m_Type;
            bool ours = m_Written.TryGetValue(prefab, out Written earlier);
            if (old != AreaType.Surface && !ours)
            {
                m_State = $"not written: the game did not make it a Surface ({old} {(int)old})";
                Mod.Log.Warn($"RunAreaType: prefab {CatTrackPrefabName} AreaGeometryData.m_Type is {old} ({(int)old}), not Surface (4) — " +
                             "nothing written; re-check AreaInitializeSystem after this patch");
                return;
            }

            if (old != AreaType.Space)
            {
                data.m_Type = AreaType.Space;
                EntityManager.SetComponentData(prefab, data);
            }
            m_Written[prefab] = new Written { GameType = ours ? earlier.GameType : old, Type = AreaType.Space };

            if (old != AreaType.Space)
            {
                m_State = $"written {old} ({(int)old}) -> Space ({(int)AreaType.Space})";
                Mod.Log.Info($"RunAreaType: prefab {CatTrackPrefabName} AreaGeometryData.m_Type {old} ({(int)old}) -> " +
                             $"Space ({(int)AreaType.Space}) (flags 0x{(uint)data.m_Flags:X}, snap {data.m_SnapDistance:0.00}): a cat track " +
                             "no longer overlaps another run's type in ValidationHelpers (UNVERIFIED in game until drawn)");
            }
            else
            {
                m_State = "already Space (written earlier this session)";
                Mod.Log.Info($"RunAreaType: prefab {CatTrackPrefabName} AreaGeometryData.m_Type already Space ({(int)AreaType.Space}) " +
                             "(written earlier this session)");
            }
        }

        /// <summary>Editor load after a game in the same run: the game's type goes back, but only when the prefab still holds
        /// exactly what Apply wrote. Nothing written earlier: no line.</summary>
        private void RestoreForEditor(GameMode mode)
        {
            if (m_Written.Count == 0)
                return;
            Entity prefab = m_PrefabCatalog.EntityOf(CatTrackPrefabName);
            if (prefab == Entity.Null || !m_Written.TryGetValue(prefab, out Written w) ||
                !EntityManager.TryGetComponent(prefab, out AreaGeometryData data))
                return;
            if (data.m_Type != w.Type)
            {
                Mod.Log.Info($"RunAreaType: prefab {CatTrackPrefabName} type changed since the game load ({data.m_Type}) — " +
                             $"nothing restored for the Editor (mode {mode})");
                return;
            }
            data.m_Type = w.GameType;
            EntityManager.SetComponentData(prefab, data);
            m_Written.Remove(prefab);   // the next game load writes again (first-write line)
            m_State = $"not a game (mode {mode}); the game's type restored";
            Mod.Log.Info($"RunAreaType: restored the game's type on prefab {CatTrackPrefabName} for the Editor (mode {mode}): " +
                         $"{w.Type} -> {w.GameType}");
        }

        /// <summary>The last load's outcome (field-test aid; plain read).</summary>
        internal string State => m_State;
    }
}
