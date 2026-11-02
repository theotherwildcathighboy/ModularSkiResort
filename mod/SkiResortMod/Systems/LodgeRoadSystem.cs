// Road and access flags on our building prefab entities, written at every Game-mode load (decompile 1.6.0f1; every
// file cited here is byte-identical in 1.6.2f1). Write sites: EntityManager.SetComponentData of BuildingData
// only; no component added or removed, no prefab JSON change, no Harmony, no reflection.
// W4: Mountain and None rows (hut, summit lodge, patrol huts) demand nothing, but their Park component sets RequireAccess
// (BuildingInitializeSystem.cs:284-287), arming the pathfind-island warning on a lot with no road; W4 clears it. Park stays.
// RoadOptional rows (A-frame, Chalet, Grand) do demand utilities and workers, so the game sets RequireRoad too; W4 clears both and
// LodgeSupplySystem decides per building what each lodge consumes (at a road it puts back the mail and crime that flag would have given).
// The write lasts for the app run (prefab entities live that long), so an Editor load writes the game's own values back.
// (W3, the terrain-clip write on the SkiCatTrack probe net, went with that prefab: retired 2026-10-26.)
// Engine notes: EngineNotes/LodgeRoadSystem.md

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
        }

        private readonly Dictionary<Entity, Written> m_Written = new Dictionary<Entity, Written>();

        private const float AccessLimitM = 8.4f;   // RoadConnectionSystem.cs:88

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

        /// <summary>One building row of the road rule: a lodge, the school or a patrol hut.</summary>
        private struct RoadRow
        {
            public string Prefab;
            public LodgeRoadRule Rule;
            public string What;   // "lodge", "school", "patrol hut" (log words)
        }

        /// <summary>The rows in catalog order: every lodge, the bus depot, the school, the patrol huts.</summary>
        private static List<RoadRow> Rows()
        {
            var rows = new List<RoadRow>(SkiCatalog.Lodges.Count + SkiCatalog.Depots.Count + SkiCatalog.Schools.Count + SkiCatalog.Patrols.Count);
            for (int i = 0; i < SkiCatalog.Lodges.Count; i++)
                rows.Add(new RoadRow { Prefab = SkiCatalog.Lodges[i].Prefab, Rule = SkiCatalog.Lodges[i].RoadRule, What = "lodge" });
            for (int i = 0; i < SkiCatalog.Depots.Count; i++)
                rows.Add(new RoadRow { Prefab = SkiCatalog.Depots[i].Prefab, Rule = SkiCatalog.Depots[i].RoadRule, What = "bus depot" });
            for (int i = 0; i < SkiCatalog.Schools.Count; i++)
                rows.Add(new RoadRow { Prefab = SkiCatalog.Schools[i].Prefab, Rule = SkiCatalog.Schools[i].RoadRule, What = "school" });
            for (int i = 0; i < SkiCatalog.Patrols.Count; i++)
                rows.Add(new RoadRow { Prefab = SkiCatalog.Patrols[i].Prefab, Rule = SkiCatalog.Patrols[i].RoadRule, What = "patrol hut" });
            return rows;
        }

        /// <summary>W4 on every top-level Mountain / None / RoadOptional row (engine notes "W4"), the Base rows left to the game, an expected missing row's WARN,
        /// pending missing rows in one Info line. With several rows the DUMP state joins them "Prefab: state; ...".</summary>
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
                // The catalog's entity; Entity.Null when missing: this system's own WARN below, for an expected row only.
                Entity prefab = m_PrefabCatalog.EntityOf(row.Prefab);
                if (prefab == Entity.Null && !SkiCatalog.IsExpected(row.Prefab))
                {
                    pending.Add(row.Prefab);   // pending, not imported yet — one Info below, no WARN
                    states.Add(row.Prefab + ": pending (not imported yet)");
                    continue;
                }
                if (row.What == "lodge" && m_LodgePrefab == Entity.Null)
                    m_LodgePrefab = prefab;
                // A top-level row (no ServiceUpgradeData) is never marked NoRoadConnection: the game's own top-level branch sets RequireRoad
                // from its uncovered demands (BuildingInitializeSystem.cs:209-300). Base rows: nothing written; one Info line below.
                if (prefab != Entity.Null && !EntityManager.HasComponent<ServiceUpgradeData>(prefab))
                {
                    if (LodgeSupplyRule.WritesRoadFlags(row.Rule))
                    {
                        states.Add(row.Prefab + ": " + ApplyRoadless(row, prefab));
                        continue;
                    }
                    topLevel.Add(row.Prefab);
                    states.Add(row.Prefab + ": not written (top-level building: the game's own road rule, T100)");
                    continue;
                }
                if (prefab == Entity.Null)
                {
                    if (row.Rule == LodgeRoadRule.None)
                    {
                        states.Add(row.Prefab + ": missing");
                        continue;
                    }
                    Mod.Log.Warn($"LodgeRoad: lodge prefab {row.Prefab} not found or without BuildingData — lodges " +
                                 "cannot be reached this load");
                    states.Add(rows.Count > 1 ? row.Prefab + ": lodge prefab missing" : "lodge prefab missing");
                    continue;
                }
                // The mod ships no ServiceUpgrade building (tests/BuildingBatch P1); such a row would need the game's upgrade road rule.
                Mod.Log.Warn($"LodgeRoad: prefab {row.Prefab} ({row.What}) carries ServiceUpgradeData — not supported, nothing written");
                states.Add(row.Prefab + ": not written (ServiceUpgradeData: not supported)");
            }
            if (pending.Count > 0)
                Mod.Log.Info($"LodgeRoad: pending prefab(s) not imported yet — nothing written for them: '{string.Join("', '", pending.ToArray())}' " +
                             "(SkiCatalog names; their road rule applies at the first load after the import)");
            if (topLevel.Count > 0)
                Mod.Log.Info($"LodgeRoad: top-level prefab(s) — nothing written, the game's own road rule applies (RequireRoad from " +
                             $"their power / water / garbage / worker demands): '{string.Join("', '", topLevel.ToArray())}' (T100)");
            m_State = string.Join("; ", states.ToArray());
        }

        /// <summary>W4 (engine notes): on a top-level Mountain / None / RoadOptional row's prefab clear RequireAccess and RequireRoad from
        /// BuildingData.m_Flags (the warnings' only inputs: ConnectionWarningSystem.cs:612-631, the placement warning and road icon read
        /// RequireRoad). Recorded in m_Written (no placement write) so the Editor gets the game's value back. One line per row per load; a
        /// WARN when the game set RequireRoad on a row that should demand nothing (a RoadOptional row demands utilities and workers by design).</summary>
        private string ApplyRoadless(RoadRow row, Entity prefab)
        {
            if (!EntityManager.TryGetComponent(prefab, out BuildingData building))
                return "top-level, no BuildingData";
            BuildingFlags old = building.m_Flags;
            bool ours = m_Written.TryGetValue(prefab, out Written earlier);
            if (!ours && (old & BuildingFlags.RequireRoad) != 0 && row.Rule != LodgeRoadRule.RoadOptional)
                Mod.Log.Warn($"LodgeRoad: top-level {row.What} prefab {row.Prefab} has RequireRoad (0x{(uint)old:X}) — it still demands " +
                             "power / water / garbage / workers, so the game wants a road; cleared anyway, but the building will lack " +
                             "what it demands when roadless (T113 option A: demand nothing)");
            BuildingFlags flags = (BuildingFlags)LodgeSupplyRule.ClearRoadFlags((uint)old, (uint)BuildingFlags.RequireAccess, (uint)BuildingFlags.RequireRoad);
            if (flags != old)
            {
                building.m_Flags = flags;
                EntityManager.SetComponentData(prefab, building);
            }
            m_Written[prefab] = new Written
            {
                GameFlags = ours ? earlier.GameFlags : old,
                Flags = flags,
            };
            string state = flags != old
                ? $"written 0x{(uint)old:X} -> 0x{(uint)flags:X} (top-level roadless {row.What}: RequireAccess / RequireRoad off, T113)"
                : $"already 0x{(uint)flags:X} (top-level roadless {row.What}; written earlier this session)";
            Mod.Log.Info($"LodgeRoad: prefab {row.Prefab} BuildingData {state}");
            return state;
        }

        /// <summary>An Editor load after a game in the same app run gets the W4 prefabs' vanilla flags back, but only when the prefab
        /// still holds exactly what Apply wrote (else one line, nothing written). No entry in m_Written (no game this session, or already
        /// restored): nothing, no line (engine notes "Editor").</summary>
        private void RestoreForEditor(GameMode mode)
        {
            if (m_Written.Count == 0)
                return;
            // Every road row; a pending row that is not imported was never written and is skipped without a line.
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
            if (!LodgeSupplyRule.TryRestoreRoadFlags((uint)building.m_Flags, (uint)w.Flags, (uint)w.GameFlags, out uint restored))
            {
                m_State = $"not a game (mode {mode}); flags changed since the game load (0x{(uint)building.m_Flags:X}) — " +
                          "nothing restored";
                Mod.Log.Info($"LodgeRoad: prefab {lodgePrefabName} {m_State}");
                return;
            }
            building.m_Flags = (BuildingFlags)restored;
            EntityManager.SetComponentData(prefab, building);
            m_Written.Remove(prefab);   // the next game load writes again (first-write line)
            m_State = $"not a game (mode {mode}); the game's flags restored";
            Mod.Log.Info($"LodgeRoad: restored the game's flags on prefab {lodgePrefabName} for the Editor (mode {mode}): " +
                         $"BuildingData 0x{(uint)w.Flags:X} -> 0x{(uint)w.GameFlags:X}; placement n/a");
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


        /// <summary>One "Lodge [road]" engine notes (counts) and one line per lodge / school / patrol hut: the road edge
        /// Building.m_RoadEdge holds (entity, prefab name) and the distance of the building's front point to that edge's boundary
        /// and center curves. Reads only; main thread, event time.</summary>
        private void AppendRoadDetail(StringBuilder sb, List<Entity> lodges, List<Entity> others)
        {
            int viaRoad = 0, none = 0;
            var lines = new StringBuilder();
            for (int i = 0; i < lodges.Count; i++)
                RoadDetailLine(lines, lodges[i], "lodge", ref viaRoad, ref none);
            for (int i = 0; i < others.Count; i++)
                RoadDetailLine(lines, others[i], "school/patrol", ref viaRoad, ref none);
            sb.Append("  Lodge [road]: ").Append(lodges.Count + others.Count).Append(" building(s): ").Append(viaRoad)
              .Append(" via a road, ").Append(none).Append(" with no road edge").Append('\n');
            sb.Append(lines);
        }

        private void RoadDetailLine(StringBuilder sb, Entity building, string what, ref int viaRoad, ref int none)
        {
            Entity prefab = EntityManager.TryGetComponent(building, out PrefabRef pr) ? pr.m_Prefab : Entity.Null;
            sb.Append("    Lodge [road] ").Append(what).Append(' ').Append(Ent(building)).Append(" '")
              .Append(prefab != Entity.Null ? m_PrefabSystem.GetPrefabName(prefab) : "?").Append("': ");
            bool haveFront = TryFrontPoint(building, prefab, out float3 front);
            Entity edge = RoadOf(building);
            if (edge == Entity.Null)
            {
                none++;
                sb.Append("no road edge (Building.m_RoadEdge null)");
            }
            else
            {
                Entity edgePrefab = EntityManager.Exists(edge) && EntityManager.TryGetComponent(edge, out PrefabRef er) ? er.m_Prefab : Entity.Null;
                viaRoad++;
                sb.Append("satisfied by edge ").Append(Ent(edge)).Append(" '")
                  .Append(edgePrefab != Entity.Null ? m_PrefabSystem.GetPrefabName(edgePrefab) : "?").Append('\'');
                if (haveFront && EdgeDistances(edge, front, out float boundary, out float centre, out float t))
                {
                    sb.Append(": front->boundary ").Append(F(boundary)).Append(" m (limit ").Append(F(AccessLimitM))
                      .Append("), front->center ").Append(F(centre)).Append(" m, nearest center t ").Append(F(t));
                }
                else if (!haveFront)
                    sb.Append(": front point unavailable");
                if (EntityManager.TryGetComponent(edge, out Game.Net.Composition comp)
                    && EntityManager.TryGetComponent(comp.m_Edge, out Game.Prefabs.NetCompositionData cd))
                    sb.Append(", composition width ").Append(F(cd.m_Width)).Append(" m");
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
