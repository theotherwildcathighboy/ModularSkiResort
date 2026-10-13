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
// THE WRITES (W1, W2), at OnGameLoadingComplete, Game mode, on OUR lodge prefab entities only (every
// SkiCatalog.Lodges row, SkiAFrameDayLodge today, resolved by SkiPrefabCatalogSystem: PrefabID fast path, then its
// one name scan — Catalog Phase A R2, docs/research/art/assets-2026-10/catalog-a/phase-a-contract.md; F5 / F10; was
// this file's own PrefabID("BuildingPrefab", …) + name-scan Resolve copy):
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
// prefab name) or "none", and "Lodge roads: n lodge(s), c with a road". Reads only. Since Catalog Phase A a
// building is a lodge / sign when the prefab catalog says so (LodgePrefabSet / IsSign), not by entity equality.
//
// CATALOG PHASE B (2026-10-11, economy group; docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §7.4,
// ROUND-SPEC R9; code-economy-skeptic miss 3). The rows are SkiCatalog.Lodges + SkiCatalog.Schools (+ Patrols for the
// guard only), each with its LodgeRoadRule:
//   Base     (A-frame, mid, grand, the school) = W1 + W2 as above (RequireRoad under the game's condition);
//   Mountain (hut, summit) = W1 WITHOUT RequireRoad (clear NoRoadConnection only) + W2. A cleared NoRoadConnection lets
//            RoadConnectionSystem search for a road at the front (:456-460, m_Required = RequireRoad), and RequireRoad
//            alone drives the yellow "No road access" placement warning (Game.Buildings/ValidationHelpers.cs:16-35) and
//            the missing-road icon (RoadConnectionSystem.cs:611-620: added only when m_Required) — so a roadless mountain
//            lodge shows neither (read from code, unverified in game: RS §9, contract risk R-h). All three files are
//            byte-identical in 1.6.2f1;
//   None     (the patrol huts) = not written: they stay roadless (NoRoadConnection), as the game made them.
// GUARD: a Mountain or None row whose prefab carries CoverageData gets one WARN per load — the game itself clears
// NoRoadConnection and sets RequireRoad for an upgrade with CoverageData (BuildingInitializeSystem.cs:165, :176-188), so
// that building WOULD warn when roadless (R9: no ServiceCoverage on mountain buildings). A pending row (not imported yet,
// SkiCatalog.IsExpected false) that is MISSING writes one Info line for all of them, never the per-prefab WARN; an
// EXPECTED row that is missing keeps the WARN. Editor restore: per written row, as before. Lines name the rule:
//   LodgeRoad: prefab SkiLodgeHut BuildingData 0x… -> 0x… (NoRoadConnection off, RequireRoad off: mountain lodge); placement …
// The Phase A A-frame line is unchanged word for word.
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
        // Catalog Phase A (F5 / F10; was LodgeRoadSystem.cs:85-89 LodgePrefabName "SkiAFrameDayLodge" / SignPrefabName
        // "SkiResortGate", :93 + :128 m_PrefabScanQuery and the :374-397 Resolve copy): the lodges are SkiCatalog.Lodges,
        // the sign SkiCatalog.Signs (DUMP only), their entities from the prefab catalog.

        private PrefabSystem m_PrefabSystem;    // road prefab names in the DUMP
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private EntityQuery m_BuildingQuery;    // committed buildings (DUMP)

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
        // Catalog Phase A: m_LodgePrefab = the first FOUND catalog lodge, m_SignPrefab = the first FOUND catalog sign;
        // both only gate the DUMP's "prefabs not resolved this load" line (skeptic S6).
        private Entity m_LodgePrefab = Entity.Null;
        private Entity m_SignPrefab = Entity.Null;
        private string m_State = "not loaded";

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
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

        /// <summary>One building row of the road rule (Catalog Phase B): a lodge, the school or a patrol hut.</summary>
        private struct RoadRow
        {
            public string Prefab;
            public LodgeRoadRule Rule;
            public string What;   // "lodge", "school", "patrol hut" (log words)
        }

        /// <summary>The rows in catalog order: every lodge, the school, the patrol huts (header CATALOG PHASE B).</summary>
        private static List<RoadRow> Rows()
        {
            var rows = new List<RoadRow>(SkiCatalog.Lodges.Count + SkiCatalog.Schools.Count + SkiCatalog.Patrols.Count);
            for (int i = 0; i < SkiCatalog.Lodges.Count; i++)
                rows.Add(new RoadRow { Prefab = SkiCatalog.Lodges[i].Prefab, Rule = SkiCatalog.Lodges[i].RoadRule, What = "lodge" });
            for (int i = 0; i < SkiCatalog.Schools.Count; i++)
                rows.Add(new RoadRow { Prefab = SkiCatalog.Schools[i].Prefab, Rule = SkiCatalog.Schools[i].RoadRule, What = "school" });
            for (int i = 0; i < SkiCatalog.Patrols.Count; i++)
                rows.Add(new RoadRow { Prefab = SkiCatalog.Patrols[i].Prefab, Rule = SkiCatalog.Patrols[i].RoadRule, What = "patrol hut" });
            return rows;
        }

        /// <summary>W1 + W2 on every catalog lodge prefab entity (header THE WRITES). Catalog Phase A (F10; was
        /// LodgeRoadSystem.cs:176-178, one name through Resolve): loops SkiCatalog.Lodges — one row today, identical
        /// output; with several rows the DUMP state joins them "Prefab: state; …". Catalog Phase B (round-2-contract
        /// §7.4; was the SkiCatalog.Lodges loop with one rule): every road row by its LodgeRoadRule (header CATALOG
        /// PHASE B), the CoverageData guard, pending MISSING rows in one Info line.</summary>
        private void Apply()
        {
            m_SignPrefab = Entity.Null;
            for (int s = 0; s < SkiCatalog.Signs.Count && m_SignPrefab == Entity.Null; s++)
                m_SignPrefab = m_PrefabCatalog.EntityOf(SkiCatalog.Signs[s].Prefab);
            m_LodgePrefab = Entity.Null;
            if (SkiCatalog.Lodges.Count == 0)
            {
                m_State = "no lodge in SkiCatalog";
                Mod.Log.Warn("LodgeRoad: SkiCatalog lists no lodge prefab — nothing written; lodges cannot be reached this load");
                return;
            }
            List<RoadRow> rows = Rows();
            var states = new List<string>(rows.Count);
            var pending = new List<string>(rows.Count);
            for (int r = 0; r < rows.Count; r++)
            {
                RoadRow row = rows[r];
                // Catalog Phase A (F10; was LodgeRoadSystem.cs:177 Resolve(LodgePrefabName)): the catalog's entity,
                // Entity.Null when MISSING → this system's own WARN in ApplyOne, as before — for an EXPECTED row only.
                Entity prefab = m_PrefabCatalog.EntityOf(row.Prefab);
                if (prefab == Entity.Null && !SkiCatalog.IsExpected(row.Prefab))
                {
                    pending.Add(row.Prefab);   // Catalog Phase B (D5): pending, not imported yet — one Info below, no WARN
                    states.Add(row.Prefab + ": pending (not imported yet)");
                    continue;
                }
                if (row.What == "lodge" && m_LodgePrefab == Entity.Null)
                    m_LodgePrefab = prefab;
                if (row.Rule != LodgeRoadRule.Base)
                    CoverageGuard(row, prefab);
                if (row.Rule == LodgeRoadRule.None)
                {
                    states.Add(row.Prefab + ": " + (prefab == Entity.Null ? "missing" : "not written (roadless " + row.What + ", R9)"));
                    continue;
                }
                string state = ApplyOne(row.Prefab, prefab, row.Rule);
                states.Add(rows.Count > 1 ? row.Prefab + ": " + state : state);
            }
            if (pending.Count > 0)
                Mod.Log.Info($"LodgeRoad: pending prefab(s) not imported yet — nothing written for them: '{string.Join("', '", pending.ToArray())}' " +
                             "(SkiCatalog names; their road rule applies at the first load after the import)");
            m_State = string.Join("; ", states.ToArray());
        }

        /// <summary>Catalog Phase B (code-economy-skeptic miss 3): a Mountain / None row whose prefab has CoverageData
        /// would get NoRoadConnection cleared and RequireRoad set by the game itself (BuildingInitializeSystem.cs:165,
        /// :176-188), so it would warn when roadless — one WARN per row per load (called once per load from Apply).</summary>
        private void CoverageGuard(RoadRow row, Entity prefab)
        {
            if (prefab == Entity.Null || !EntityManager.HasComponent<CoverageData>(prefab))
                return;
            Mod.Log.Warn($"LodgeRoad: prefab {row.Prefab} ({row.What}, road rule {row.Rule}) has CoverageData — the game clears " +
                         "NoRoadConnection and sets RequireRoad for an upgrade with coverage (BuildingInitializeSystem.cs:165, :176-188), " +
                         "so it shows 'No road access' when roadless; remove its ServiceCoverage component (R9: none on mountain buildings)");
        }

        /// <summary>W1 + W2 on one road-row prefab entity; returns the DUMP state text (the pre-catalog m_State, word for
        /// word) and writes the same lines as before. Catalog Phase B: <paramref name="rule"/> Mountain clears
        /// NoRoadConnection only (no RequireRoad, header CATALOG PHASE B); Base is the Phase A write, unchanged.</summary>
        private string ApplyOne(string lodgePrefabName, Entity prefab, LodgeRoadRule rule)
        {
            string state;
            if (prefab == Entity.Null || !EntityManager.TryGetComponent(prefab, out BuildingData building))
            {
                state = "lodge prefab missing";
                Mod.Log.Warn($"LodgeRoad: lodge prefab {lodgePrefabName} not found or without BuildingData — lodges " +
                             "cannot be reached this load");
                return state;
            }
            bool hasPlacement = EntityManager.TryGetComponent(prefab, out PlaceableObjectData placeable);

            BuildingFlags old = building.m_Flags;
            bool gameSet = (old & BuildingFlags.NoRoadConnection) != 0;
            bool ours = m_Written.TryGetValue(prefab, out Written earlier);
            if (!gameSet && !ours)
            {
                state = $"not written: the game did not mark it NoRoadConnection (0x{(uint)old:X})";
                Mod.Log.Warn($"LodgeRoad: the game did not mark the lodge prefab NoRoadConnection (0x{(uint)old:X}) — " +
                             "nothing written; re-check BuildingInitializeSystem after this patch");
                return state;
            }

            // W1 — the game's own branch for upgrades allowed a road (BuildingInitializeSystem.cs:179-183).
            // Catalog Phase B (R9): a Mountain row clears NoRoadConnection only — no RequireRoad (header CATALOG PHASE B).
            bool mountain = rule == LodgeRoadRule.Mountain;
            BuildingFlags flags = old & ~BuildingFlags.NoRoadConnection;
            bool canBeOnRoad = (flags & (BuildingFlags.CanBeOnRoad | BuildingFlags.CanBeOnRoadArea)) != 0;
            if (!canBeOnRoad && !mountain)
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
                string requireRoad = mountain
                    ? ((old & BuildingFlags.RequireRoad) != 0 ? "already on (left as the game set it)" : "off: mountain lodge")
                    : canBeOnRoad ? "off: can be on road"
                    : (old & BuildingFlags.RequireRoad) != 0 ? "already on" : "on";
                state = $"written 0x{(uint)old:X} -> 0x{(uint)flags:X}; placement {placementText}";
                Mod.Log.Info($"LodgeRoad: prefab {lodgePrefabName} BuildingData 0x{(uint)old:X} -> 0x{(uint)flags:X} " +
                             $"(NoRoadConnection off, RequireRoad {requireRoad}); placement {placementText}");
            }
            else
            {
                state = $"already 0x{(uint)flags:X} / {(hasPlacement ? "0x" + ((int)placement).ToString("X") : "n/a")} " +
                        "(written earlier this session)";
                Mod.Log.Info($"LodgeRoad: prefab {lodgePrefabName} {state}");
            }
            return state;
        }

        /// <summary>F7 fixer (header EDITOR): an Editor load after a game in the same app run gets the lodge prefab's
        /// vanilla flags back, but only when the prefab still holds exactly what Apply wrote (else one line, nothing
        /// written). No entry in m_Written (no game this session, or already restored): nothing, no line.</summary>
        private void RestoreForEditor(GameMode mode)
        {
            if (m_Written.Count == 0)
                return;
            // Catalog Phase A (F10; was LodgeRoadSystem.cs:256 Resolve(LodgePrefabName)): every catalog lodge (one today).
            // Catalog Phase B: every WRITTEN road row (Base and Mountain lodges, the school); a pending row that is not
            // imported was never written and is skipped without a line; None rows are never written.
            List<RoadRow> rows = Rows();
            for (int r = 0; r < rows.Count; r++)
            {
                RoadRow row = rows[r];
                if (row.Rule == LodgeRoadRule.None)
                    continue;
                Entity prefab = m_PrefabCatalog.EntityOf(row.Prefab);
                if (prefab == Entity.Null && !SkiCatalog.IsExpected(row.Prefab))
                    continue;
                RestoreOne(mode, row.Prefab, prefab);
            }
        }

        private void RestoreOne(GameMode mode, string lodgePrefabName, Entity prefab)
        {
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
                Mod.Log.Info($"LodgeRoad: prefab {lodgePrefabName} {m_State}");
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
            Mod.Log.Info($"LodgeRoad: restored the game's flags on prefab {lodgePrefabName} for the Editor (mode {mode}): " +
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
                var others = new List<Entity>(4);   // Catalog Phase B: schools and patrol huts
                // Catalog Phase A (F10; was prefab == m_LodgePrefab / == m_SignPrefab): the catalog's live lodge view,
                // fetched once before the loop (M4), and its sign test (event time, DUMP only).
                HashSet<Entity> lodgePrefabs = m_PrefabCatalog.LodgePrefabSet;
                EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
                using (var entities = m_BuildingQuery.ToEntityArray(Allocator.Temp))
                using (var prefabRefs = m_BuildingQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity prefab = prefabRefs[i].m_Prefab;
                        if (prefab == Entity.Null)
                            continue;
                        if (lodgePrefabs.Contains(prefab))
                            lodges.Add(entities[i]);
                        else if (m_PrefabCatalog.IsSign(prefab))
                            signs.Add(entities[i]);
                        else if (m_PrefabCatalog.IsUpgradeBuilding(prefab))
                            others.Add(entities[i]);
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
                // Catalog Phase B: the school (a Base road row) and the patrol huts (roadless by design, R9).
                for (int i = 0; i < others.Count; i++)
                {
                    Entity building = others[i];
                    Entity prefab = EntityManager.TryGetComponent(building, out PrefabRef pr) ? pr.m_Prefab : Entity.Null;
                    string what = m_PrefabCatalog.TryGetSchool(prefab, out SchoolDef _) ? "school" : "patrol hut";
                    Entity sign = EntityManager.TryGetComponent(building, out Owner owner) ? owner.m_Owner : Entity.Null;
                    sb.Append("    ").Append(what).Append(' ').Append(Ent(building)).Append(" (sign ").Append(Ent(sign)).Append("): road ");
                    AppendRoad(sb, RoadOf(building));
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

        // ── prefab resolution: Catalog Phase A (F5; was LodgeRoadSystem.cs:374-397 Resolve, the PrefabID + name-scan
        //    copy) — SkiPrefabCatalogSystem.EntityOf / LodgePrefabSet / IsSign; its ARM line names the lodge FOUND or
        //    MISSING, and Apply's own WARN stays. ─────────────────────────────────────────────────────────────

        private static string Ent(Entity e)
        {
            return e == Entity.Null ? "null" : e.Index + ":" + e.Version;
        }
    }
}
