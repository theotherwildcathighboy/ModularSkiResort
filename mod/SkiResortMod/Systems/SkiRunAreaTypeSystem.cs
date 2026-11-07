// Sets the cat track prefab's AreaGeometryData.m_Type to Space at every Game load. So a drawn cat track may cross other runs
// (decompile 1.6.0f1). All our runs are Surface areas. Drawing a Surface across a Surface fails validation
// (ValidationHelpers.cs:551-582). A different type is exempt.
// The system applies it again at every load: AreaInitializeSystem fills AreaGeometryData on the Created tag only, and a save carries
// no prefab data.
// Editor: the system writes the game's value back, only when the prefab still holds exactly what we wrote.
// Logs: ONE "RunAreaType: prefab ..." line per load (or the "already Space" line, or a WARN). A missing prefab logs ERROR.
// The system only calls SetComponentData on vanilla AreaGeometryData of the one prefab entity. It uses no Harmony and no reflection.
// Engine notes: EngineNotes/SkiRunAreaTypeSystem.md

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
        private const string CatTrackPrefabName = SkiCatalog.Names.RunCatTrack;

        private SkiPrefabCatalogSystem m_PrefabCatalog;

        private struct Written
        {
            public AreaType GameType;
            public AreaType Type;
        }

        private readonly System.Collections.Generic.Dictionary<Entity, Written> m_Written =
            new System.Collections.Generic.Dictionary<Entity, Written>();

        // Per-load state, reset at OnGameLoadingComplete. It is for logs and tests
        private string m_State = "not loaded";

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            Enabled = false;   // nothing per frame. OnGameLoadingComplete still fires
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

        /// <summary>Editor load after a game in the same run: restores the game's type, only when the prefab still holds what Apply wrote.</summary>
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

        internal string State => m_State;
    }
}
