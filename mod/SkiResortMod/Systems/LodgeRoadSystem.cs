// LodgeRoadSystem — lets the A-Frame Day Lodge connect to the road at its front, so guests can walk to it
// (Round F7, items B2-B3; docs/research/field-test-7-fixes.md §1 B; route B "trimmed" of
// docs/research/econ-round/locals/reach-verdict.md §7 as corrected by the Verification of
// docs/research/field/2026-10-04/findings/ped-access.md; mechanism in locals/reach-route-flag.md).
// Created 2026-10-04 (Round F7, implementer 1). Route P (two pedestrian markers in the lodge prefab) is
// retired: the game built one sign-owned connection lane between them that can never connect, hence the
// unconditional "No pedestrian access" (ped-access.md Verification V1); the prefab is reverted (B1).
//
// THE PREMISE (decompile 1.6.0f1; every file cited here is byte-identical in 1.6.2f1).
//   * BuildingInitializeSystem (PrefabUpdate, SystemOrder.cs:1018 [1.6.2 :1019]) gives every ServiceUpgrade
//     building prefab BuildingFlags.NoRoadConnection (BuildingInitializeSystem.cs:156-170) and removes it only
//     for an upgrade with CoverageData or a vanilla Gate owner (:176-189). Our lodge has neither, so
//     RoadConnectionSystem never looks for its road (RoadConnectionSystem.cs:73-76 for a changed road nearby,
//     :456 for the lodge itself), Building.m_RoadEdge stays null and PathfindTargetSeeker's front-road
//     fallback finds no target (PathfindTargetSeeker.cs:467-481): no trip can end at a lodge.
//   * For those vanilla upgrades the game's own branch (:177-188) clears NoRoadConnection, sets RequireRoad
//     unless CanBeOnRoad | CanBeOnRoadArea is set (:180-183), and clears PlacementFlags.OwnerSide on the
//     prefab's PlaceableObjectData (:184-188). That is the state written here.
//
// THE WRITES (W1, W2), at OnGameLoadingComplete, Game mode, on OUR lodge prefab entity only
// (PrefabID("BuildingPrefab", "SkiAFrameDayLodge"), name-scan fallback as SkiRunOutlineSystem.Resolve):
//   W1 BuildingData.m_Flags: clear NoRoadConnection 0x2; set RequireRoad 0x1 under the game's condition
//      (BuildingFlags.cs: RequireRoad 1, NoRoadConnection 2, CanBeOnRoad 0x4000, CanBeOnRoadArea 0x8000).
//   W2 PlaceableObjectData.m_Flags: clear OwnerSide 0x4 (Game.Objects/PlacementFlags.cs). The lodge keeps
//      RoadSide, so it snaps to roads instead of behind the sign (reach-route-flag.md §4.3; ped-access.md V3).
//   Written only when the game had set NoRoadConnection, or when this system wrote the same prefab entity
//   earlier in the session (a second load: "already" line). Otherwise one WARN: the game's rule changed.
//   Expected value: 0x23062 -> 0x23061 (reach-route-flag.md §1.3; any old value with 0x2 set is fine,
//   ped-access.md V3 "FC1 is too strict").
//
// WHY THE WRITE LASTS. BuildingInitializeSystem runs only on prefab entities tagged Created and completes its
// flag job inside that one update (reach-verdict.md §2.1); prefab entities live for the app run and only
// prefab editing re-creates one. The readers take the prefab's value at every use, never a copy:
// RoadConnectionSystem.cs:72-73 and :454-456 (m_PrefabBuildingData[prefabRef.m_Prefab]),
// Game.Buildings/ValidationHelpers.cs:16-35 (the yellow "No road access" placement warning, severity Warning,
// never a block), ObjectToolSystem GetAvailableSnapMask (1.6.0 :3574-3583, 1.6.2 :3633-3642) for OwnerSide.
// The precedent is CursorAimSystem (a prefab component of our own net written at every load, field-confirmed,
// DECISIONS 13). A save carries no prefab flags (prefab entities read from a save are mapped and destroyed).
//
// EDITOR (F7 fixer). Because the write lasts for the app run, an Editor load after a game in the same run would
// keep the route-B flags (RequireRoad, no NoRoadConnection, no OwnerSide): the lodge would snap to roads and show
// the "No road access" warning there, unlike a fresh start. So an Editor load (mode has GameMode.Editor) writes the
// game's own values back (m_Written's GameFlags / GamePlacement), only when the prefab still holds exactly what
// Apply wrote, and forgets the entry: the next game load writes again (first-write line). The main menu keeps the
// flags (nothing is placed there), so a game -> menu -> game cycle logs the "already" line.
//
// NOT DONE (contract B2): no sweep of existing lodges (an Updated tag per lodge would only migrate saves made
// before this round; no-save-compat rule), no poll. New lodges connect when they are created; a lodge placed
// away from roads connects when a road is later built within 8.4 m of its front (RoadConnectionSystem.cs:64-76).
//
// LOGS: "created"; per load in Game mode one of: the first-write line
//   LodgeRoad: prefab SkiAFrameDayLodge BuildingData 0x{old:X} -> 0x{new:X} (NoRoadConnection off, RequireRoad
//   on|already on|off: can be on road); placement 0x{oldP:X} -> 0x{newP:X} (OwnerSide off)
// the "already" line on a later load of the same session, or a WARN (prefab missing / rule changed); and a
// one-shot "!!!" if the write throws (lodges then stay unreachable this load, as before this round; the hub's
// LodgeNoRoad tip "move it" cannot help then — accepted, the "!!!" line and the DUMP's "prefab EXCEPTION" state
// say why). In the Editor after a game: "LodgeRoad: restored the game's flags on prefab … for the Editor (mode
// Editor): BuildingData 0x… -> 0x…; placement 0x… -> 0x…", or one "nothing restored" line, or a "!!!".
//
// DUMP (B3, called by the hub DUMP, contract D7): per lodge and per Welcome Sign the road edge (entity and
// prefab name) or "none", and "Lodge roads: n lodge(s), c with a road". Reads only.
//
// WRITE SITES (acceptance gate A11): EntityManager.SetComponentData of vanilla BuildingData and
// PlaceableObjectData on the lodge PREFAB entity (Apply; RestoreForEditor writes the game's values back). No component is added to or removed from any entity, nothing
// is serialized by us, no prefab JSON change, no Harmony, no reflection.

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using BuildingFlags = Game.Prefabs.BuildingFlags;   // not Game.Buildings.BuildingFlags (the instance flags)

namespace SkiResortMod.Systems
{
    public sealed partial class LodgeRoadSystem : GameSystemBase
    {
        /// <summary>Our lodge BuildingPrefab (the same name as SkiResortAttractivenessSystem.LodgePrefabNames).</summary>
        private const string LodgePrefabName = "SkiAFrameDayLodge";

        /// <summary>The Welcome Sign BuildingPrefab (SkiRunOwnershipSystem.GateNames) — DUMP only.</summary>
        private const string SignPrefabName = "SkiResortGate";

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_BuildingQuery;    // committed buildings (DUMP)
        private EntityQuery m_PrefabScanQuery;  // name-resolution fallback

        /// <summary>What this system wrote, per prefab entity, this session (prefab entities live for the app run).</summary>
        private struct Written
        {
            public BuildingFlags GameFlags, Flags;
            public Game.Objects.PlacementFlags GamePlacement, Placement;
            public bool HasPlacement;
        }

        private readonly Dictionary<Entity, Written> m_Written = new Dictionary<Entity, Written>();

        // Per-load state — reset at OnGameLoadingComplete. (F7 fixer: the unread PrefabApplied property and its
        // m_Applied field are gone; the DUMP's "prefab …" state says whether the flags were written this load.)
        private Entity m_LodgePrefab = Entity.Null;
        private Entity m_SignPrefab = Entity.Null;
        private string m_State = "not loaded";

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_BuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            Enabled = false;   // nothing per frame; OnGameLoadingComplete still fires (CursorAimSystem precedent)
            Mod.Log.Info("LodgeRoad: system created (the lodge prefab may connect to the road at its front: " +
                         "written at every game load; no sweep, no poll)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_LodgePrefab = Entity.Null;
            m_SignPrefab = Entity.Null;
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
                        Mod.Log.Error("!!! LodgeRoad EXCEPTION — the game's flags were not restored on the lodge prefab for " +
                                      $"the Editor (it keeps the road flags of the last game until the app restarts):\n{e}");
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
                Mod.Log.Error("!!! LodgeRoad EXCEPTION — lodge road connection not applied until the next load (lodges " +
                              $"cannot be reached by guests this load):\n{e}");
            }
        }

        protected override void OnUpdate()
        {
        }

        /// <summary>W1 + W2 on the lodge prefab entity (header THE WRITES).</summary>
        private void Apply()
        {
            m_SignPrefab = Resolve(SignPrefabName);
            Entity prefab = Resolve(LodgePrefabName);
            m_LodgePrefab = prefab;
            if (prefab == Entity.Null || !EntityManager.TryGetComponent(prefab, out BuildingData building))
            {
                m_State = "lodge prefab missing";
                Mod.Log.Warn($"LodgeRoad: lodge prefab {LodgePrefabName} not found or without BuildingData — lodges " +
                             "cannot be reached this load");
                return;
            }
            bool hasPlacement = EntityManager.TryGetComponent(prefab, out PlaceableObjectData placeable);

            BuildingFlags old = building.m_Flags;
            bool gameSet = (old & BuildingFlags.NoRoadConnection) != 0;
            bool ours = m_Written.TryGetValue(prefab, out Written earlier);
            if (!gameSet && !ours)
            {
                m_State = $"not written: the game did not mark it NoRoadConnection (0x{(uint)old:X})";
                Mod.Log.Warn($"LodgeRoad: the game did not mark the lodge prefab NoRoadConnection (0x{(uint)old:X}) — " +
                             "nothing written; re-check BuildingInitializeSystem after this patch");
                return;
            }

            // W1 — the game's own branch for upgrades allowed a road (BuildingInitializeSystem.cs:179-183).
            BuildingFlags flags = old & ~BuildingFlags.NoRoadConnection;
            bool canBeOnRoad = (flags & (BuildingFlags.CanBeOnRoad | BuildingFlags.CanBeOnRoadArea)) != 0;
            if (!canBeOnRoad)
                flags |= BuildingFlags.RequireRoad;
            // W2 — :184-188.
            Game.Objects.PlacementFlags oldPlacement = hasPlacement ? placeable.m_Flags : Game.Objects.PlacementFlags.None;
            Game.Objects.PlacementFlags placement = oldPlacement & ~Game.Objects.PlacementFlags.OwnerSide;

            bool changed = false;
            if (flags != old)
            {
                building.m_Flags = flags;
                EntityManager.SetComponentData(prefab, building);
                changed = true;
            }
            if (hasPlacement && placement != oldPlacement)
            {
                placeable.m_Flags = placement;
                EntityManager.SetComponentData(prefab, placeable);
                changed = true;
            }

            m_Written[prefab] = new Written
            {
                GameFlags = ours ? earlier.GameFlags : old,
                Flags = flags,
                GamePlacement = ours ? earlier.GamePlacement : oldPlacement,
                Placement = placement,
                HasPlacement = hasPlacement,
            };
            string placementText = hasPlacement
                ? $"0x{(int)oldPlacement:X} -> 0x{(int)placement:X} (OwnerSide off)"
                : "n/a (no PlaceableObjectData)";
            if (changed)
            {
                string requireRoad = canBeOnRoad ? "off: can be on road"
                    : (old & BuildingFlags.RequireRoad) != 0 ? "already on" : "on";
                m_State = $"written 0x{(uint)old:X} -> 0x{(uint)flags:X}; placement {placementText}";
                Mod.Log.Info($"LodgeRoad: prefab {LodgePrefabName} BuildingData 0x{(uint)old:X} -> 0x{(uint)flags:X} " +
                             $"(NoRoadConnection off, RequireRoad {requireRoad}); placement {placementText}");
            }
            else
            {
                m_State = $"already 0x{(uint)flags:X} / {(hasPlacement ? "0x" + ((int)placement).ToString("X") : "n/a")} " +
                          "(written earlier this session)";
                Mod.Log.Info($"LodgeRoad: prefab {LodgePrefabName} {m_State}");
            }
        }

        /// <summary>F7 fixer (header EDITOR): an Editor load after a game in the same app run gets the lodge prefab's
        /// vanilla flags back, but only when the prefab still holds exactly what Apply wrote (else one line, nothing
        /// written). No entry in m_Written (no game this session, or already restored): nothing, no line.</summary>
        private void RestoreForEditor(GameMode mode)
        {
            if (m_Written.Count == 0)
                return;
            Entity prefab = Resolve(LodgePrefabName);
            if (prefab == Entity.Null || !m_Written.TryGetValue(prefab, out Written w))
            {
                m_State = $"not a game (mode {mode}); the lodge prefab entity is not the one written earlier — nothing restored";
                Mod.Log.Info($"LodgeRoad: {m_State}");
                return;
            }
            if (!EntityManager.TryGetComponent(prefab, out BuildingData building))
                return;
            bool hasPlacement = EntityManager.TryGetComponent(prefab, out PlaceableObjectData placeable);
            bool placementOurs = !w.HasPlacement || (hasPlacement && placeable.m_Flags == w.Placement);
            if (building.m_Flags != w.Flags || !placementOurs)
            {
                m_State = $"not a game (mode {mode}); flags changed since the game load (0x{(uint)building.m_Flags:X}) — " +
                          "nothing restored";
                Mod.Log.Info($"LodgeRoad: prefab {LodgePrefabName} {m_State}");
                return;
            }
            building.m_Flags = w.GameFlags;
            EntityManager.SetComponentData(prefab, building);
            if (w.HasPlacement && hasPlacement)
            {
                placeable.m_Flags = w.GamePlacement;
                EntityManager.SetComponentData(prefab, placeable);
            }
            m_Written.Remove(prefab);   // the next game load writes again (first-write line)
            string placementText = w.HasPlacement && hasPlacement
                ? $"0x{(int)w.Placement:X} -> 0x{(int)w.GamePlacement:X}"
                : "n/a";
            m_State = $"not a game (mode {mode}); the game's flags restored";
            Mod.Log.Info($"LodgeRoad: restored the game's flags on prefab {LodgePrefabName} for the Editor (mode {mode}): " +
                         $"BuildingData 0x{(uint)w.Flags:X} -> 0x{(uint)w.GameFlags:X}; placement {placementText}");
        }

        // ── DUMP (B3) ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>The hub DUMP's lodge-road lines (contract F7 B3, D7): "Lodge roads: n lodge(s), c with a road",
        /// then per lodge and per Welcome Sign its road edge (entity and prefab name) or "none". Main thread,
        /// event time only. Appends only; never throws.</summary>
        public void AppendDump(StringBuilder sb)
        {
            try
            {
                if (m_LodgePrefab == Entity.Null && m_SignPrefab == Entity.Null)
                {
                    sb.Append("  Lodge roads: 0 lodge(s), 0 with a road; prefab ").Append(m_State)
                      .Append(" (prefabs not resolved this load)\n");
                    return;
                }
                var lodges = new List<Entity>(8);
                var signs = new List<Entity>(4);
                EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
                using (var entities = m_BuildingQuery.ToEntityArray(Allocator.Temp))
                using (var prefabRefs = m_BuildingQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity prefab = prefabRefs[i].m_Prefab;
                        if (prefab == Entity.Null)
                            continue;
                        if (prefab == m_LodgePrefab)
                            lodges.Add(entities[i]);
                        else if (prefab == m_SignPrefab)
                            signs.Add(entities[i]);
                    }
                }
                int withRoad = 0;
                for (int i = 0; i < lodges.Count; i++)
                {
                    if (RoadOf(lodges[i]) != Entity.Null)
                        withRoad++;
                }
                sb.Append("  Lodge roads: ").Append(lodges.Count).Append(" lodge(s), ").Append(withRoad).Append(" with a road; prefab ")
                  .Append(m_State).Append('\n');
                for (int i = 0; i < lodges.Count; i++)
                {
                    Entity lodge = lodges[i];
                    Entity sign = EntityManager.TryGetComponent(lodge, out Owner owner) ? owner.m_Owner : Entity.Null;
                    sb.Append("    lodge ").Append(Ent(lodge)).Append(" (sign ").Append(Ent(sign)).Append("): road ");
                    AppendRoad(sb, RoadOf(lodge));
                    sb.Append('\n');
                }
                for (int i = 0; i < signs.Count; i++)
                {
                    sb.Append("    sign ").Append(Ent(signs[i])).Append(": road ");
                    AppendRoad(sb, RoadOf(signs[i]));
                    sb.Append('\n');
                }
            }
            catch (Exception e)
            {
                sb.Append("  Lodge roads: DUMP failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        private Entity RoadOf(Entity building)
        {
            return EntityManager.TryGetComponent(building, out Building data) ? data.m_RoadEdge : Entity.Null;
        }

        private void AppendRoad(StringBuilder sb, Entity road)
        {
            if (road == Entity.Null)
            {
                sb.Append("none");
                return;
            }
            sb.Append(Ent(road)).Append(" '");
            if (EntityManager.Exists(road) && EntityManager.TryGetComponent(road, out PrefabRef prefabRef))
                sb.Append(m_PrefabSystem.GetPrefabName(prefabRef.m_Prefab));
            else
                sb.Append('?');
            sb.Append('\'');
        }

        // ── prefab resolution ──────────────────────────────────────────────────────────────────────────

        /// <summary>House pattern (SkiRunOutlineSystem.Resolve): fast PrefabID path + name-scan fallback;
        /// Entity.Null when missing (pre-import states must not throw).</summary>
        private Entity Resolve(string name)
        {
            Entity prefabEntity = Entity.Null;
            if (m_PrefabSystem.TryGetPrefab(new PrefabID("BuildingPrefab", name), out PrefabBase prefabBase))
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

        private static string Ent(Entity e)
        {
            return e == Entity.Null ? "null" : e.Index + ":" + e.Version;
        }
    }
}
