// Lets the lodges connect to the road at their front, so guests can walk to them. Mechanism: docs/research/econ-round/locals/reach-route-flag.md
// and the verification in docs/research/field/2026-10-04/findings/ped-access.md. (Two pedestrian markers in the lodge prefab were
// tried and retired: the game built one sign-owned connection lane between them that can never connect, hence the unconditional "No
// pedestrian access".)
//
// The premise (decompile 1.6.0f1; every file cited here is byte-identical in 1.6.2f1):
//   * BuildingInitializeSystem (PrefabUpdate, SystemOrder.cs:1018 [1.6.2 :1019]) gives every ServiceUpgrade building prefab
//     BuildingFlags.NoRoadConnection (BuildingInitializeSystem.cs:156-170) and removes it only for an upgrade with CoverageData or a
//     vanilla Gate owner (:176-189). Our lodge has neither, so RoadConnectionSystem never looks for its road
//     (RoadConnectionSystem.cs:73-76 for a changed road nearby, :456 for the lodge itself), Building.m_RoadEdge stays null and
//     PathfindTargetSeeker's front-road fallback finds no target (PathfindTargetSeeker.cs:467-481): no trip can end at a lodge.
//   * For those vanilla upgrades the game's own branch (:177-188) clears NoRoadConnection, sets RequireRoad unless CanBeOnRoad |
//     CanBeOnRoadArea is set (:180-183), and clears PlacementFlags.OwnerSide on the prefab's PlaceableObjectData (:184-188). That is
//     the state written here.
//
// The writes (W1, W2), at OnGameLoadingComplete, Game mode, on our lodge prefab entities only (every SkiCatalog.Lodges row, resolved
// by SkiPrefabCatalogSystem: PrefabID fast path, then its one name scan):
//   W1 BuildingData.m_Flags: clear NoRoadConnection 0x2; set RequireRoad 0x1 under the game's condition (BuildingFlags.cs:
//      RequireRoad 1, NoRoadConnection 2, CanBeOnRoad 0x4000, CanBeOnRoadArea 0x8000).
//   W2 PlaceableObjectData.m_Flags: clear OwnerSide 0x4 (Game.Objects/PlacementFlags.cs). The lodge keeps RoadSide, so it snaps to
//      roads instead of behind the sign (reach-route-flag.md section 4.3).
//   Written only when the game had set NoRoadConnection, or when this system wrote the same prefab entity earlier in the session (a
//   second load: "already" line). Otherwise one WARN: the game's rule changed.
//   Expected value: 0x23062 -> 0x23061 (reach-route-flag.md section 1.3; any old value with 0x2 set is fine).
//
// Why the write lasts: BuildingInitializeSystem runs only on prefab entities tagged Created and completes its flag job inside that
// one update; prefab entities live for the app run and only prefab editing re-creates one. The readers take the prefab's value at
// every use, never a copy: RoadConnectionSystem.cs:72-73 and :454-456 (m_PrefabBuildingData[prefabRef.m_Prefab]),
// Game.Buildings/ValidationHelpers.cs:16-35 (the yellow "No road access" placement warning, severity Warning, never a block),
// ObjectToolSystem GetAvailableSnapMask (1.6.0 :3574-3583, 1.6.2 :3633-3642) for OwnerSide. The precedent is CursorAimSystem (a
// prefab component of our own net written at every load). A save carries no prefab flags (prefab entities read from a save are mapped
// and destroyed).
//
// Editor: because the write lasts for the app run, an Editor load after a game in the same run would keep the written flags
// (RequireRoad, no NoRoadConnection, no OwnerSide): the lodge would snap to roads and show the "No road access" warning there, unlike
// a fresh start. So an Editor load (mode has GameMode.Editor) writes the game's own values back (m_Written's GameFlags /
// GamePlacement), only when the prefab still holds exactly what Apply wrote, and forgets the entry: the next game load writes again
// (first-write line). The main menu keeps the flags (nothing is placed there), so a game -> menu -> game cycle logs the "already" line.
//
// Not done: no sweep of existing lodges (an Updated tag per lodge would only migrate old saves), no poll. New lodges connect when
// they are created; a lodge placed away from roads connects when a road is later built within 8.4 m of its front
// (RoadConnectionSystem.cs:64-76).
//
// Logs: "created"; per load in Game mode one of: the first-write line
//   LodgeRoad: prefab SkiAFrameDayLodge BuildingData 0x{old:X} -> 0x{new:X} (NoRoadConnection off, RequireRoad on|already on|off: can
//   be on road); placement 0x{oldP:X} -> 0x{newP:X} (OwnerSide off)
// the "already" line on a later load of the same session, or a WARN (prefab missing / rule changed); and a one-shot "!!!" if the write
// throws (lodges then stay unreachable this load; the hub's LodgeNoRoad tip "move it" cannot help then: the "!!!" line and the
// DUMP's "prefab EXCEPTION" state say why). In the Editor after a game: "LodgeRoad: restored the game's flags on prefab ... for the
// Editor (mode Editor): BuildingData 0x... -> 0x...; placement 0x... -> 0x...", or one "nothing restored" line, or a "!!!".
//
// DUMP (called by the hub DUMP): per lodge and per Welcome Sign the road edge (entity and prefab name) or "none", and "Lodge roads: n
// lodge(s), c with a road". Reads only. A building is a lodge / sign when the prefab catalog says so (LodgePrefabSet / IsSign), not by
// entity equality.
//
// Rule per row: the rows are SkiCatalog.Lodges + SkiCatalog.Schools (+ Patrols for the guard only), each with its LodgeRoadRule:
//   Base     (A-frame, mid, grand, the school) = W1 + W2 as above (RequireRoad under the game's condition);
//   Mountain (hut, summit) = W1 WITHOUT RequireRoad (clear NoRoadConnection only) + W2. A cleared NoRoadConnection lets
//            RoadConnectionSystem search for a road at the front (:456-460, m_Required = RequireRoad), and RequireRoad alone drives the
//            yellow "No road access" placement warning (Game.Buildings/ValidationHelpers.cs:16-35) and the missing-road icon
//            (RoadConnectionSystem.cs:611-620: added only when m_Required), so a roadless mountain lodge shows neither (read from
//            code, unverified in game). All three files are byte-identical in 1.6.2f1;
//   None     (the patrol huts) = not written: they stay roadless (NoRoadConnection), as the game made them.
// Guard: a Mountain or None row whose prefab carries CoverageData gets one WARN per load: the game itself clears NoRoadConnection and
// sets RequireRoad for an upgrade with CoverageData (BuildingInitializeSystem.cs:165, :176-188), so that building would warn when
// roadless (no ServiceCoverage on mountain buildings). A pending row (not imported yet, SkiCatalog.IsExpected false) that is missing
// writes one Info line for all of them, never the per-prefab WARN; an expected row that is missing keeps the WARN. Editor restore: per
// written row. Lines name the rule:
//   LodgeRoad: prefab SkiLodgeHut BuildingData 0x... -> 0x... (NoRoadConnection off, RequireRoad off: mountain lodge); placement ...
//
// Top-level roadless: every row is top-level (no ServiceUpgradeData), so Base rows are not written (the game's own RequireRoad from
// their demands). The hut, the summit lodge and both patrol huts demand nothing (power / water / garbage 0, no Workplace), so the game
// sets no RequireRoad, but their Park component sets RequireAccess (BuildingInitializeSystem.cs:284-287), which arms the
// pathfind-island warning (ConnectionWarningSystem.cs:612-631) on a lot with no road and no lane. W4 clears RequireAccess (and
// RequireRoad, with a WARN, should a demand come back) on those prefab entities at every Game load; recorded in m_Written, so an
// Editor load restores the game's value (RestoreOne). Park stays (the attraction walk and Tourism Overhaul need it).
//
// Write sites: EntityManager.SetComponentData of vanilla BuildingData and PlaceableObjectData on the lodge prefab entity (Apply;
// RestoreForEditor writes the game's values back). No component is added to or removed from any entity, nothing is serialized by us,
// no prefab JSON change, no Harmony, no reflection.
//
// Cat track probe: SkiCatTrack is a RoadPrefab (JSON only, ImportedData\SkiCatTrack*) with a geometry-less piece, so the road draws
// nothing and the terrain shows (docs/research/cat-track-net.md section 6 risk 1, section 8). Two additions here:
//   W3 NetGeometryData.m_Flags on the SkiCatTrack prefab entity: clear GeometryFlags.ClipTerrain (Game.Net, 0x40000) at every Game-mode
//      load. NetInitializeSystem.cs:1943 ORs FlattenTerrain | ClipTerrain into every RoadPrefab; the terrain system turns ClipTerrain
//      into a +-0.3 m clip slab along each lane section (TerrainSystem.cs:1136-1139, GetFlags; GenerateClipDataJob :1698-1722) that
//      vanilla fills with the road mesh. A geometry-less piece would leave that slab see-through. FlattenTerrain is kept
//      (CatTrackClearFlatten = false): it makes the terrain follow the net cross-section (the ShiftTerrain lane flag, :1140-1143), so
//      the 3 m strip is a level bench and the lane curves sit on the ground; cleared, a cross-slope would leave the flat lane ribbon
//      hanging above/below the sloping ground by up to 1.5 m x slope, and GroundHeightSystem / LotHeightSystem stop treating the net as
//      a flat surface (they test the same flag, GroundHeightSystem.cs:322, LotHeightSystem.cs:286). If a bench scar proves
//      unacceptable, flip CatTrackClearFlatten and re-test (then lane Y follows the net's own heights: cars may float).
//      Read live by the terrain jobs (the prefab component is looked up at job time), so the write needs no Updated tag.
//      One line per load: "CatTrack: prefab SkiCatTrack NetGeometryData ...". A prefab without the flag the game should have set (and
//      no earlier write by us) writes nothing and logs one WARN. Not restored for the Editor (the net is a placeable probe road;
//      nothing in the Editor uses it).
//   DUMP "Lodge [road]": per lodge / school / patrol hut, which road edge Building.m_RoadEdge holds (entity, prefab name, "= CAT TRACK"
//      when it is SkiCatTrack), the distance from the building's front point to that edge's boundary curves (the
//      RoadConnectionSystem.CheckEdge test, limit 8.4 m, RoadConnectionSystem.cs:88, :353-385; edge geometry only, node geometry
//      ignored) and to its center curve, and, when the edge is not a cat track, the nearest cat-track edge in reach (so a lodge beside
//      a cat track that a vanilla road beat, or that the Raised/Lowered rule dropped, shows).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using BuildingFlags = Game.Prefabs.BuildingFlags;   // not Game.Buildings.BuildingFlags (the instance flags)
using NetGeometryFlags = Game.Net.GeometryFlags;     // not Game.Areas.GeometryFlags

namespace SkiResortMod.Systems
{
    public sealed partial class LodgeRoadSystem : GameSystemBase
    {
        // The lodges are SkiCatalog.Lodges, the sign SkiCatalog.Signs (DUMP only), their entities from the prefab catalog.

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

        private const string CatTrackType = "RoadPrefab";
        private const string CatTrackName = "SkiCatTrack";
        /// <summary>false = keep FlattenTerrain (cat-track-net.md risk 1, header W3); true = clear both flags (a different probe).</summary>
        private const bool CatTrackClearFlatten = false;
        private const float AccessLimitM = 8.4f;   // RoadConnectionSystem.cs:88

        private EntityQuery m_EdgeQuery;                                              // road edges (cat-track search in the DUMP)
        private Entity m_CatTrackPrefab = Entity.Null;                                // this load; Null = not imported / not found
        private string m_CatTrackState = "not loaded";
        /// <summary>The game's own NetGeometryData.m_Flags per prefab entity, kept for the session (prefab entities live for the
        /// app run): a second load of the same session finds the flag already cleared and says so instead of warning.</summary>
        private readonly Dictionary<Entity, NetGeometryFlags> m_CatTrackGameFlags = new Dictionary<Entity, NetGeometryFlags>();

        // Per-load state, reset at OnGameLoadingComplete. m_LodgePrefab = the first found catalog lodge, m_SignPrefab = the first found
        // catalog sign; both only gate the DUMP's "prefabs not resolved this load" line.
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
            m_EdgeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    ComponentType.ReadOnly<Game.Net.EdgeGeometry>(),
                    ComponentType.ReadOnly<Game.Net.Curve>(),
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
            m_CatTrackPrefab = Entity.Null;
            m_CatTrackState = "not a game (mode " + mode + ")";
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
            try
            {
                ApplyCatTrack();
            }
            catch (Exception e)
            {
                m_CatTrackState = "EXCEPTION: " + e.GetType().Name;
                Mod.Log.Error("!!! CatTrack EXCEPTION — the SkiCatTrack prefab keeps its terrain-clip flag this load (a placed " +
                              $"cat track may show a see-through strip):\n{e}");
            }
        }

        /// <summary>W3 (header CAT TRACK PROBE): clear ClipTerrain (and FlattenTerrain only if CatTrackClearFlatten) on the
        /// SkiCatTrack RoadPrefab entity's NetGeometryData. One line per load. The prefab is resolved by the prefab catalog's
        /// "extra" path (any type+name; no SkiCatalog row), so a game without the imported probe logs one Info line.</summary>
        private void ApplyCatTrack()
        {
            if (!m_PrefabCatalog.TryGetPrefab(CatTrackType, CatTrackName, out Entity prefab, out PrefabBase _))
            {
                m_CatTrackState = "prefab " + CatTrackName + " not found (the probe is not imported)";
                Mod.Log.Info($"CatTrack: {m_CatTrackState} — nothing written");
                return;
            }
            m_CatTrackPrefab = prefab;
            if (!EntityManager.TryGetComponent(prefab, out NetGeometryData geometry))
            {
                m_CatTrackState = "prefab " + CatTrackName + " has no NetGeometryData";
                Mod.Log.Warn($"CatTrack: {m_CatTrackState} (not a net prefab?) — nothing written");
                return;
            }
            NetGeometryFlags old = geometry.m_Flags;
            NetGeometryFlags clear = NetGeometryFlags.ClipTerrain | (CatTrackClearFlatten ? NetGeometryFlags.FlattenTerrain : 0);
            bool ours = m_CatTrackGameFlags.TryGetValue(prefab, out NetGeometryFlags gameFlags);
            if (!ours && (old & NetGeometryFlags.ClipTerrain) == 0)
            {
                m_CatTrackState = $"not written: the game did not set ClipTerrain (0x{(int)old:X})";
                Mod.Log.Warn($"CatTrack: prefab {CatTrackName} NetGeometryData.m_Flags 0x{(int)old:X} has no ClipTerrain (0x40000) — " +
                             "nothing written; re-check NetInitializeSystem.cs:1943 after this patch");
                return;
            }
            NetGeometryFlags flags = old & ~clear;
            if (!ours)
                m_CatTrackGameFlags[prefab] = old;
            string keep = CatTrackClearFlatten ? "FlattenTerrain off too" : "FlattenTerrain kept";
            string detail = $"width {geometry.m_DefaultWidth:0.##} m, max slope {geometry.m_MaxSlopeSteepness:0.##}" + RoadDetail(prefab);
            if (flags != old)
            {
                geometry.m_Flags = flags;
                EntityManager.SetComponentData(prefab, geometry);
                m_CatTrackState = $"written 0x{(int)old:X} -> 0x{(int)flags:X}";
                Mod.Log.Info($"CatTrack: prefab {CatTrackName} NetGeometryData.m_Flags 0x{(int)old:X} -> 0x{(int)flags:X} " +
                             $"(ClipTerrain off, {keep}); {detail}");
            }
            else
            {
                m_CatTrackState = $"already 0x{(int)flags:X} (game 0x{(int)gameFlags:X}; written earlier this session)";
                Mod.Log.Info($"CatTrack: prefab {CatTrackName} NetGeometryData.m_Flags already 0x{(int)flags:X} " +
                             $"(game set 0x{(int)gameFlags:X}; written earlier this session; {keep}); {detail}");
            }
        }

        /// <summary>"; speed … km/h, RoadData flags …" for the W3 line (RoadData.m_SpeedLimit is m/s; zoning off = no EnableZoning).</summary>
        private string RoadDetail(Entity prefab)
        {
            if (!EntityManager.TryGetComponent(prefab, out RoadData road))
                return "; no RoadData";
            return string.Format(CultureInfo.InvariantCulture, "; speed {0:0} km/h, RoadData flags {1}, zone block {2}",
                road.m_SpeedLimit * 3.6f, road.m_Flags, road.m_ZoneBlockPrefab == Entity.Null ? "none" : "SET");
        }

        protected override void OnUpdate()
        {
        }

        /// <summary>One building row of the road rule: a lodge, the school or a patrol hut.</summary>
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

        /// <summary>W1 + W2 on every catalog lodge prefab entity (header "The writes"): every road row by its LodgeRoadRule (header "Rule per
        /// row"), the CoverageData guard, pending missing rows in one Info line. With several rows the DUMP state joins them "Prefab: state;
        /// ...".</summary>
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
            var topLevel = new List<string>(rows.Count);
            for (int r = 0; r < rows.Count; r++)
            {
                RoadRow row = rows[r];
                // The catalog's entity; Entity.Null when missing: this system's own WARN in ApplyOne, for an expected row only.
                Entity prefab = m_PrefabCatalog.EntityOf(row.Prefab);
                if (prefab == Entity.Null && !SkiCatalog.IsExpected(row.Prefab))
                {
                    pending.Add(row.Prefab);   // pending, not imported yet — one Info below, no WARN
                    states.Add(row.Prefab + ": pending (not imported yet)");
                    continue;
                }
                if (row.What == "lodge" && m_LodgePrefab == Entity.Null)
                    m_LodgePrefab = prefab;
                // A top-level row (the prefab has no ServiceUpgradeData, e.g. SkiSchool) is never marked NoRoadConnection: the game's own top-level
                // branch sets RequireRoad from its uncovered demands (BuildingInitializeSystem.cs:156-170 is the upgrade branch, :209-300 the native
                // one). Nothing written, no "rule changed" WARN; one Info line below.
                if (prefab != Entity.Null && !EntityManager.HasComponent<ServiceUpgradeData>(prefab))
                {
                    topLevel.Add(row.Prefab);
                    // W4 (header "Top-level roadless"): a Mountain / None row demands nothing, so the game set no RequireRoad; its Park still sets
                    // RequireAccess (BuildingInitializeSystem.cs:284-287), cleared here.
                    states.Add(row.Rule == LodgeRoadRule.Base
                        ? row.Prefab + ": not written (top-level building: the game's own road rule, T100)"
                        : row.Prefab + ": " + ApplyRoadless(row, prefab));
                    continue;
                }
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
            if (topLevel.Count > 0)
                Mod.Log.Info($"LodgeRoad: top-level prefab(s) — nothing written, the game's own road rule applies (RequireRoad from " +
                             $"their power / water / garbage / worker demands): '{string.Join("', '", topLevel.ToArray())}' (T100)");
            m_State = string.Join("; ", states.ToArray());
        }

        /// <summary>W4 (header "Top-level roadless"): on a top-level Mountain / None row's prefab clear RequireAccess and RequireRoad from
        /// BuildingData.m_Flags (the warnings' only inputs: ConnectionWarningSystem.cs:612-631, the placement warning and road icon read
        /// RequireRoad). Recorded in m_Written (no placement write) so the Editor gets the game's value back. One line per row per load; a
        /// WARN when the game set RequireRoad (the prefab still demands something: a utility or workers).</summary>
        private string ApplyRoadless(RoadRow row, Entity prefab)
        {
            if (!EntityManager.TryGetComponent(prefab, out BuildingData building))
                return "top-level, no BuildingData";
            BuildingFlags old = building.m_Flags;
            bool ours = m_Written.TryGetValue(prefab, out Written earlier);
            if (!ours && (old & BuildingFlags.RequireRoad) != 0)
                Mod.Log.Warn($"LodgeRoad: top-level {row.What} prefab {row.Prefab} has RequireRoad (0x{(uint)old:X}) — it still demands " +
                             "power / water / garbage / workers, so the game wants a road; cleared anyway, but the building will lack " +
                             "what it demands when roadless (T113 option A: demand nothing)");
            BuildingFlags flags = old & ~(BuildingFlags.RequireAccess | BuildingFlags.RequireRoad);
            if (flags != old)
            {
                building.m_Flags = flags;
                EntityManager.SetComponentData(prefab, building);
            }
            m_Written[prefab] = new Written
            {
                GameFlags = ours ? earlier.GameFlags : old,
                Flags = flags,
                HasPlacement = false,
            };
            string state = flags != old
                ? $"written 0x{(uint)old:X} -> 0x{(uint)flags:X} (top-level roadless {row.What}: RequireAccess / RequireRoad off, T113)"
                : $"already 0x{(uint)flags:X} (top-level roadless {row.What}; written earlier this session)";
            Mod.Log.Info($"LodgeRoad: prefab {row.Prefab} BuildingData {state}");
            return state;
        }

        /// <summary>A Mountain / None row whose prefab has CoverageData would get NoRoadConnection cleared and RequireRoad set by the game
        /// itself (BuildingInitializeSystem.cs:165, :176-188), so it would warn when roadless: one WARN per row per load (called once per
        /// load from Apply).</summary>
        private void CoverageGuard(RoadRow row, Entity prefab)
        {
            if (prefab == Entity.Null || !EntityManager.HasComponent<CoverageData>(prefab))
                return;
            Mod.Log.Warn($"LodgeRoad: prefab {row.Prefab} ({row.What}, road rule {row.Rule}) has CoverageData — the game clears " +
                         "NoRoadConnection and sets RequireRoad for an upgrade with coverage (BuildingInitializeSystem.cs:165, :176-188), " +
                         "so it shows 'No road access' when roadless; remove its ServiceCoverage component (R9: none on mountain buildings)");
        }

        /// <summary>W1 + W2 on one road-row prefab entity; returns the DUMP state text and writes the lines described in the header.
        /// <paramref name="rule"/> Mountain clears NoRoadConnection only (no RequireRoad, header "Rule per row").</summary>
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

            // W1: the game's own branch for upgrades allowed a road (BuildingInitializeSystem.cs:179-183). A Mountain row clears NoRoadConnection
            // only, no RequireRoad (header "Rule per row").
            bool mountain = rule == LodgeRoadRule.Mountain;
            BuildingFlags flags = old & ~BuildingFlags.NoRoadConnection;
            bool canBeOnRoad = (flags & (BuildingFlags.CanBeOnRoad | BuildingFlags.CanBeOnRoadArea)) != 0;
            if (!canBeOnRoad && !mountain)
                flags |= BuildingFlags.RequireRoad;
            // W2: :184-188.
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

        /// <summary>An Editor load after a game in the same app run gets the lodge prefab's vanilla flags back, but only when the prefab
        /// still holds exactly what Apply wrote (else one line, nothing written). No entry in m_Written (no game this session, or already
        /// restored): nothing, no line (header "Editor").</summary>
        private void RestoreForEditor(GameMode mode)
        {
            if (m_Written.Count == 0)
                return;
            // Every written road row (Base and Mountain lodges, the school); a pending row that is not imported was never written and is skipped
            // without a line; None rows are never written.
            List<RoadRow> rows = Rows();
            for (int r = 0; r < rows.Count; r++)
            {
                RoadRow row = rows[r];
                Entity prefab = m_PrefabCatalog.EntityOf(row.Prefab);
                // a None row is restored only when W4 wrote it (a top-level patrol hut); never written otherwise.
                if (row.Rule == LodgeRoadRule.None && (prefab == Entity.Null || !m_Written.ContainsKey(prefab)))
                    continue;
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


        /// <summary>The hub DUMP's lodge-road lines: "Lodge roads: n lodge(s), c with a road", then per lodge and per Welcome Sign its road
        /// edge (entity and prefab name) or "none". Main thread, event time only. Appends only; never throws.</summary>
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
                var others = new List<Entity>(4);   // schools and patrol huts
                // The catalog's live lodge view, fetched once before the loop, and its sign test (event time, DUMP only).
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
                // the school (a Base road row) and the patrol huts (roadless by design).
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
                AppendRoadDetail(sb, lodges, others);
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


        /// <summary>One "Lodge [road]" header (counts, the W3 state, how many SkiCatTrack edges exist) and one line per lodge /
        /// school / patrol hut: the road edge Building.m_RoadEdge holds (entity, prefab name, "= CAT TRACK"), the distance of the
        /// building's front point to that edge's boundary and center curves, and the nearest cat-track edge when the holding
        /// edge is not one. Reads only; main thread, event time.</summary>
        private void AppendRoadDetail(StringBuilder sb, List<Entity> lodges, List<Entity> others)
        {
            var cat = new List<Entity>(16);
            if (m_CatTrackPrefab != Entity.Null)
            {
                EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
                using (var edges = m_EdgeQuery.ToEntityArray(Allocator.Temp))
                using (var refs = m_EdgeQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                {
                    for (int i = 0; i < edges.Length; i++)
                    {
                        if (refs[i].m_Prefab == m_CatTrackPrefab)
                            cat.Add(edges[i]);
                    }
                }
            }
            int viaCat = 0, viaOther = 0, none = 0;
            var lines = new StringBuilder();
            for (int i = 0; i < lodges.Count; i++)
                RoadDetailLine(lines, lodges[i], "lodge", cat, ref viaCat, ref viaOther, ref none);
            for (int i = 0; i < others.Count; i++)
                RoadDetailLine(lines, others[i], "school/patrol", cat, ref viaCat, ref viaOther, ref none);
            sb.Append("  Lodge [road]: ").Append(lodges.Count + others.Count).Append(" building(s): ").Append(viaCat)
              .Append(" via ").Append(CatTrackName).Append(", ").Append(viaOther).Append(" via other roads, ").Append(none)
              .Append(" with no road edge; ").Append(cat.Count).Append(' ').Append(CatTrackName).Append(" edge(s) in the city; W3: ")
              .Append(m_CatTrackState).Append('\n');
            sb.Append(lines);
        }

        private void RoadDetailLine(StringBuilder sb, Entity building, string what, List<Entity> cat,
                                    ref int viaCat, ref int viaOther, ref int none)
        {
            Entity prefab = EntityManager.TryGetComponent(building, out PrefabRef pr) ? pr.m_Prefab : Entity.Null;
            sb.Append("    Lodge [road] ").Append(what).Append(' ').Append(Ent(building)).Append(" '")
              .Append(prefab != Entity.Null ? m_PrefabSystem.GetPrefabName(prefab) : "?").Append("': ");
            bool haveFront = TryFrontPoint(building, prefab, out float3 front);
            Entity edge = RoadOf(building);
            bool isCat = false;
            if (edge == Entity.Null)
            {
                none++;
                sb.Append("no road edge (Building.m_RoadEdge null)");
            }
            else
            {
                Entity edgePrefab = EntityManager.Exists(edge) && EntityManager.TryGetComponent(edge, out PrefabRef er) ? er.m_Prefab : Entity.Null;
                isCat = edgePrefab != Entity.Null && edgePrefab == m_CatTrackPrefab;
                if (isCat)
                    viaCat++;
                else
                    viaOther++;
                sb.Append("satisfied by edge ").Append(Ent(edge)).Append(" '")
                  .Append(edgePrefab != Entity.Null ? m_PrefabSystem.GetPrefabName(edgePrefab) : "?").Append('\'');
                if (isCat)
                    sb.Append(" = CAT TRACK");
                if (haveFront && EdgeDistances(edge, front, out float boundary, out float centre, out float t))
                {
                    sb.Append(": front->boundary ").Append(F(boundary)).Append(" m (limit ").Append(F(AccessLimitM))
                      .Append("), front->centre ").Append(F(centre)).Append(" m, nearest centre t ").Append(F(t));
                }
                else if (!haveFront)
                    sb.Append(": front point unavailable");
                if (EntityManager.TryGetComponent(edge, out Game.Net.Composition comp)
                    && EntityManager.TryGetComponent(comp.m_Edge, out Game.Prefabs.NetCompositionData cd))
                    sb.Append(", composition width ").Append(F(cd.m_Width)).Append(" m");
            }
            if (!isCat && cat.Count > 0 && haveFront)
            {
                float best = float.MaxValue;
                Entity bestEdge = Entity.Null;
                for (int i = 0; i < cat.Count; i++)
                {
                    if (EdgeDistances(cat[i], front, out float b, out float _, out float _) && b < best)
                    {
                        best = b;
                        bestEdge = cat[i];
                    }
                }
                if (bestEdge != Entity.Null)
                    sb.Append("; nearest ").Append(CatTrackName).Append(" edge ").Append(Ent(bestEdge)).Append(": boundary ").Append(F(best))
                      .Append(best < AccessLimitM ? " m (IN reach but not the holder: another road is closer, or the Raised/Lowered side rule dropped it)" : " m (out of reach)");
            }
            sb.Append('\n');
        }

        /// <summary>The point RoadConnectionSystem measures from (RoadConnectionSystem.cs:456-474, :518): the lot front,
        /// or the building position for CanBeOnRoad(Area) without CanBeRoadSide.</summary>
        private bool TryFrontPoint(Entity building, Entity prefab, out float3 front)
        {
            front = default;
            if (prefab == Entity.Null
                || !EntityManager.TryGetComponent(building, out Game.Objects.Transform transform)
                || !EntityManager.TryGetComponent(prefab, out BuildingData data))
                return false;
            front = BuildingUtils.CalculateFrontPosition(transform, data.m_LotSize.y);
            bool onRoad = (data.m_Flags & (BuildingFlags.CanBeOnRoad | BuildingFlags.CanBeOnRoadArea)) != 0;
            bool roadSide = (data.m_Flags & BuildingFlags.CanBeRoadSide) != 0;
            if (onRoad && !roadSide)
                front = transform.m_Position;
            return true;
        }

        /// <summary>Distance (xz) from <paramref name="p"/> to the nearest of the edge geometry's four boundary curves (the
        /// CheckDistance rule without node geometry) and to its center curve (t = nearest parameter).</summary>
        private bool EdgeDistances(Entity edge, float3 p, out float boundary, out float centre, out float t)
        {
            boundary = 0f;
            centre = 0f;
            t = 0f;
            if (!EntityManager.Exists(edge)
                || !EntityManager.TryGetComponent(edge, out Game.Net.EdgeGeometry geometry)
                || !EntityManager.TryGetComponent(edge, out Game.Net.Curve curve))
                return false;
            float2 p2 = p.xz;
            float d1 = MathUtils.Distance(geometry.m_Start.m_Left.xz, p2, out float _);
            float d2 = MathUtils.Distance(geometry.m_Start.m_Right.xz, p2, out float _);
            float d3 = MathUtils.Distance(geometry.m_End.m_Left.xz, p2, out float _);
            float d4 = MathUtils.Distance(geometry.m_End.m_Right.xz, p2, out float _);
            boundary = math.min(math.min(d1, d2), math.min(d3, d4));
            centre = MathUtils.Distance(curve.m_Bezier.xz, p2, out t);
            return true;
        }

        private static string F(float v)
        {
            return v.ToString("0.0", CultureInfo.InvariantCulture);
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


        private static string Ent(Entity e)
        {
            return e == Entity.Null ? "null" : e.Index + ":" + e.Version;
        }
    }
}
