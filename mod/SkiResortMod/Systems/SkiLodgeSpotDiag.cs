// SkiLodgeSpotDiag — READ-ONLY diagnostic: the spawn spots the ENGINE registered for every lodge ("Lodge [spots]").
// Ticket T51 step 0 (docs/research/asset-markers-spawners.md §c, §f; docs/research/field/2026-10-15/build/T51-markers-step1.md).
// It changes nothing: no command buffer, no component write, no prefab touch; one cheap poll (below); every read guarded.
//
// WHAT "REGISTERED" MEANS (decompile 1.6.0f1)
//   * The buffer is Game.Buildings.SpawnLocationElement {m_SpawnLocation, m_Type} on the BUILDING entity
//     (Game.Buildings/SpawnLocationElement.cs; added to every building archetype, Game.Prefabs/BuildingPrefab.cs:45).
//     Game.Buildings.ReferencesSystem (Modification4B, SystemOrder.cs:176) adds every Created, Owned, non-Secondary entity that
//     carries Game.Objects.SpawnLocation (type SpawnLocation), Game.Areas.HangaroundLocation (type HangaroundLocation) or
//     Game.Net.ParkingLane (type ParkingLane) to the buffer of its owner AND of every owner above it (ReferencesSystem.cs:51-117), so a
//     lodge's spots also sit in the Welcome Sign's buffer. On a save load Game.Serialization.SpawnLocationElementSystem
//     (Deserialize, SystemOrder.cs:818) rebuilds it the same way (the buffer is IEmptySerializable).
//   * PathfindTargetSeeker.FindTargets offers exactly the entries of this buffer as the building's targets (PathfindTargetSeeker.cs
//     :429-481, :530-740): a SpawnLocation by its prefab's SpawnLocationData (m_ConnectionType, m_ActivityMask: offered only when
//     the mask is 0 or intersects the visitor's, :653), a HangaroundLocation area by its Game.Net.SubLane connection lanes (the
//     branch after the SpawnLocation lookup fails, :700-720). An unlinked spot does not switch off the road-front door; a linked
//     one does (:720, :729).
//   * A prop prefab with Game.Prefabs.ActivityLocation (bench, chair, table set) becomes ITSELF a Game.Objects.SpawnLocation +
//     Game.Objects.ActivityLocation entity whose prefab SpawnLocationData.m_ActivityMask is the OR of its seats' masks
//     (Game.Prefabs/ActivityLocation.cs:60-67, :105-125); owned by the lodge (sub-object), it registers like a door marker.
//   * A Pedestrian Access Location marker is NOT a SpawnLocation (Game.Routes.TakeoffLocation): never in the buffer; it joins an
//     area's lanes to the street (Game.Areas/AreaConnectionSystem.cs, Modification4B, :179).
//
// OUTPUT (log tag "Lodge [spots]")
//   * INFO at every Game-mode load: ONE line, the registered count per lodge (spawn / hangaround / parking, and how many spawn
//     entries are seats and how many are linked to a lane), then the sign's totals.
//   * INFO when that signature changes later (a lodge placed, deleted, moved, a prefab sub-object re-created): the same one line,
//     at most MaxChangeLines per load. Polled every PollFrames simulation frames from the building query (an int compare on a
//     string signature; the building query is a few thousand 8-byte PrefabRef copies).
//   * The hub dev-tab DUMP (SkiResortHubUISystem.DumpToLog, after the Towers blocks): per lodge a header and per spot a line — kind,
//     prefab name, position in the LODGE'S FRAME (+Z = road side, the frame the prefab JSON rows use), owner depth, the prefab's
//     SpawnLocationData (connection type, activity mask as names, hangaround-on-lane, authorization), the instance's
//     Game.Objects.SpawnLocation (group, flags, ConnectedLane1/2 set), registered yes/no; hangaround areas with node count and
//     sub-lane / pedestrian connection-lane counts; Pedestrian Access Location markers (always "not a spawn spot"); the spot-bearing
//     sub-objects the walk found that the buffer does NOT hold (registered: NO); then the sign's own totals.
//   UNVERIFIED: that every spot the buffer holds is offered to a given visitor (needs the visitor's activity mask), and what the
//   area's connection lanes join on the street side (read the Pathfind Debug gizmo in game).
//
// Mod.cs: UpdateAt<SkiLodgeSpotDiag>(GameSimulation), not load-bearing (read-only; all producers ran earlier in the frame).

using System;
using System.Collections.Generic;
using System.Globalization;
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
using Unity.Mathematics;
using ObjActivityLocation = Game.Objects.ActivityLocation;
using ObjSpawnLocation = Game.Objects.SpawnLocation;
using ObjSubObject = Game.Objects.SubObject;
using ObjTransform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiLodgeSpotDiag : GameSystemBase
    {
        /// <summary>Simulation frames between signature polls (the load line and DUMP do not depend on it).</summary>
        internal const int PollFrames = 512;
        internal const int MaxChangeLines = 12;
        internal const int MaxSpotLinesPerLodge = 48;
        internal const int MaxWalkDepth = 3;
        internal const int MaxWalkObjects = 256;
        private static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

        private PrefabSystem m_PrefabSystem;
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private EntityQuery m_BuildingQuery;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Frame, m_ChangeLines;
        private string m_Signature = string.Empty;

        /// <summary>Registered entries of one building's SpawnLocationElement buffer.</summary>
        private struct Counts
        {
            public int Spawn, Hangaround, Parking, Seats, Linked, Stale;
            public bool HasBuffer;
        }

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
            Mod.Log.Info($"{nameof(SkiLodgeSpotDiag)} created (read-only: the spawn spots the engine registered per lodge)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Frame = 0;
            m_ChangeLines = 0;
            m_Signature = string.Empty;
            if (!m_Armed)
                return;
            try
            {
                string line = SummaryLine(out string signature);
                m_Signature = signature;
                Mod.Log.Info("Lodge [spots]: load - " + line);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            if (++m_Frame % PollFrames != 0)
                return;
            try
            {
                string line = SummaryLine(out string signature);
                if (signature == m_Signature)
                    return;
                m_Signature = signature;
                if (m_ChangeLines >= MaxChangeLines)
                    return;
                m_ChangeLines++;
                Mod.Log.Info("Lodge [spots]: changed - " + line + (m_ChangeLines == MaxChangeLines ? " (last change line this load; the DUMP still prints)" : string.Empty));
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void Fail(Exception e)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiLodgeSpotDiag)} EXCEPTION - lodge spot diagnostics disabled until the next game load " +
                              $"(diagnostic only, nothing else is affected). Please report this stack:\n{e}");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Entities
        // ════════════════════════════════════════════════════════════════════════════════════════

        private void CollectBuildings(List<Entity> lodges, List<Entity> signs)
        {
            HashSet<Entity> lodgePrefabs = m_PrefabCatalog.LodgePrefabSet;
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
                }
            }
        }

        /// <summary>The registered entries of the building's buffer by type (the engine's own list).</summary>
        private Counts CountSpots(Entity building)
        {
            var c = new Counts();
            if (!EntityManager.TryGetBuffer(building, isReadOnly: true, out DynamicBuffer<SpawnLocationElement> buffer))
                return c;
            c.HasBuffer = true;
            for (int i = 0; i < buffer.Length; i++)
            {
                SpawnLocationElement el = buffer[i];
                if (!EntityManager.Exists(el.m_SpawnLocation))
                {
                    c.Stale++;
                    continue;
                }
                switch (el.m_Type)
                {
                    case SpawnLocationType.SpawnLocation:
                        c.Spawn++;
                        if (EntityManager.HasComponent<ObjActivityLocation>(el.m_SpawnLocation))
                            c.Seats++;
                        if (EntityManager.TryGetComponent(el.m_SpawnLocation, out ObjSpawnLocation sl) && sl.m_ConnectedLane1 != Entity.Null)
                            c.Linked++;
                        break;
                    case SpawnLocationType.HangaroundLocation:
                        c.Hangaround++;
                        break;
                    case SpawnLocationType.ParkingLane:
                        c.Parking++;
                        break;
                }
            }
            return c;
        }

        /// <summary>The one-line summary and a signature that changes when any lodge's or sign's registered counts do.</summary>
        private string SummaryLine(out string signature)
        {
            var lodges = new List<Entity>(8);
            var signs = new List<Entity>(4);
            CollectBuildings(lodges, signs);
            var sb = new StringBuilder(256);
            var sig = new StringBuilder(128);
            sb.Append(lodges.Count).Append(" lodge(s)");
            for (int i = 0; i < lodges.Count; i++)
            {
                Entity lodge = lodges[i];
                Counts c = CountSpots(lodge);
                sb.Append("; ").Append(PrefabNameOf(lodge)).Append(' ').Append(Ent(lodge)).Append(": ");
                AppendCounts(sb, c);
                sig.Append(lodge.Index).Append(':').Append(lodge.Version).Append(':').Append(c.Spawn).Append('/').Append(c.Hangaround)
                   .Append('/').Append(c.Parking).Append('/').Append(c.Seats).Append('/').Append(c.Linked).Append('|');
            }
            for (int i = 0; i < signs.Count; i++)
            {
                Counts c = CountSpots(signs[i]);
                sb.Append("; sign ").Append(Ent(signs[i])).Append(" holds ");
                AppendCounts(sb, c);
                sig.Append('s').Append(signs[i].Index).Append(':').Append(c.Spawn).Append('/').Append(c.Hangaround).Append('/')
                   .Append(c.Parking).Append('|');
            }
            signature = sig.ToString();
            return sb.ToString();
        }

        private static void AppendCounts(StringBuilder sb, Counts c)
        {
            if (!c.HasBuffer)
            {
                sb.Append("no SpawnLocationElement buffer");
                return;
            }
            sb.Append(c.Spawn).Append(" spawn (").Append(c.Seats).Append(" seat(s), ").Append(c.Linked).Append(" linked to a lane), ")
              .Append(c.Hangaround).Append(" hangaround, ").Append(c.Parking).Append(" parking");
            if (c.Stale > 0)
                sb.Append(", ").Append(c.Stale).Append(" stale");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // DUMP
        // ════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>On demand (hub dev-tab DUMP). Main thread; never throws.</summary>
        internal void DumpAll(string reason)
        {
            if (!m_Armed)
            {
                Mod.Log.Info($"Lodge [spots]: DUMP ({reason}) - not armed (no game loaded)");
                return;
            }
            try
            {
                var lodges = new List<Entity>(8);
                var signs = new List<Entity>(4);
                CollectBuildings(lodges, signs);
                var sb = new StringBuilder(1024);
                sb.Append("Lodge [spots]: DUMP (").Append(reason).Append(") - ").Append(lodges.Count).Append(" lodge(s), ")
                  .Append(signs.Count).Append(" sign(s); ").Append(m_ChangeLines).Append(" change line(s) this load")
                  .Append(m_Disabled ? "; poll DISABLED after an exception" : string.Empty);
                for (int i = 0; i < lodges.Count; i++)
                {
                    sb.Append('\n');
                    DumpLodge(sb, lodges[i]);
                }
                for (int i = 0; i < signs.Count; i++)
                {
                    Counts c = CountSpots(signs[i]);
                    sb.Append("\n  sign ").Append(Ent(signs[i])).Append(": ");
                    AppendCounts(sb, c);
                    sb.Append(" (every owned upgrade's spots are copied up the owner chain, ReferencesSystem.cs:51-117)");
                }
                Mod.Log.Info(sb.ToString());
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Lodge [spots]: DUMP ({reason}) failed ({e.GetType().Name}: {e.Message}) - non-fatal");
            }
        }

        private void DumpLodge(StringBuilder sb, Entity lodge)
        {
            Counts c = CountSpots(lodge);
            bool hasT = EntityManager.TryGetComponent(lodge, out ObjTransform lodgeT);
            Entity sign = EntityManager.TryGetComponent(lodge, out Owner owner) ? owner.m_Owner : Entity.Null;
            sb.Append("  lodge ").Append(Ent(lodge)).Append(" '").Append(PrefabNameOf(lodge)).Append("' (sign ").Append(Ent(sign)).Append(") at ")
              .Append(hasT ? Fmt(lodgeT.m_Position) : "?").Append(": registered ");
            AppendCounts(sb, c);

            var registered = new HashSet<Entity>();
            var lines = new List<string>(32);
            int extra = 0;

            // 1) every entry of the engine's buffer
            if (EntityManager.TryGetBuffer(lodge, isReadOnly: true, out DynamicBuffer<SpawnLocationElement> buffer))
            {
                var elements = new List<SpawnLocationElement>(buffer.Length);
                for (int i = 0; i < buffer.Length; i++)
                    elements.Add(buffer[i]);
                for (int i = 0; i < elements.Count; i++)
                {
                    Entity e = elements[i].m_SpawnLocation;
                    registered.Add(e);
                    AddLine(lines, ref extra, DescribeRegistered(e, elements[i].m_Type, lodge, hasT, lodgeT));
                }
            }

            // 2) spot-bearing sub-objects the walk finds that the buffer does NOT hold, and the access markers (never in it)
            int scanned = 0;
            var seen = new HashSet<Entity>();
            WalkObjects(lodge, lodge, hasT, lodgeT, 1, registered, seen, lines, ref extra, ref scanned);

            // 3) sub-areas not registered (a walking area, or a hangaround area the buffer lacks)
            if (EntityManager.TryGetBuffer(lodge, isReadOnly: true, out DynamicBuffer<Game.Areas.SubArea> subAreas))
            {
                var areas = new List<Entity>(subAreas.Length);
                for (int i = 0; i < subAreas.Length; i++)
                    areas.Add(subAreas[i].m_Area);
                sb.Append("; sub-areas ").Append(areas.Count);
                for (int i = 0; i < areas.Count; i++)
                {
                    if (registered.Contains(areas[i]))
                        continue;
                    AddLine(lines, ref extra, DescribeArea(areas[i], false, hasT, lodgeT));
                }
            }
            sb.Append("; sub-objects scanned ").Append(scanned).Append(scanned >= MaxWalkObjects ? " (cap)" : string.Empty);
            for (int i = 0; i < lines.Count; i++)
                sb.Append("\n    ").Append(lines[i]);
            if (extra > 0)
                sb.Append("\n    ... ").Append(extra).Append(" more spot line(s) not printed (cap ").Append(MaxSpotLinesPerLodge).Append(" per lodge)");
        }

        private void AddLine(List<string> lines, ref int extra, string line)
        {
            if (lines.Count >= MaxSpotLinesPerLodge)
                extra++;
            else
                lines.Add(line);
        }

        private string DescribeRegistered(Entity e, SpawnLocationType type, Entity lodge, bool hasT, ObjTransform lodgeT)
        {
            if (!EntityManager.Exists(e))
                return "registered " + type + " " + Ent(e) + ": STALE (entity gone)";
            if (type == SpawnLocationType.HangaroundLocation)
                return DescribeArea(e, true, hasT, lodgeT);
            if (type == SpawnLocationType.ParkingLane)
                return "parking lane " + Ent(e) + " '" + PrefabNameOf(e) + "': registered yes";
            return DescribeSpawn(e, true, lodge, hasT, lodgeT);
        }

        private string DescribeSpawn(Entity e, bool isRegistered, Entity lodge, bool hasT, ObjTransform lodgeT)
        {
            var sb = new StringBuilder(160);
            bool seat = EntityManager.HasComponent<ObjActivityLocation>(e);
            sb.Append(seat ? "seat " : "spawn ").Append(Ent(e)).Append(" '").Append(PrefabNameOf(e)).Append("' at ")
              .Append(LocalOf(e, hasT, lodgeT)).Append(" depth ").Append(OwnerDepth(e, lodge));
            Entity prefab = EntityManager.TryGetComponent(e, out PrefabRef pr) ? pr.m_Prefab : Entity.Null;
            if (prefab != Entity.Null && EntityManager.TryGetComponent(prefab, out SpawnLocationData d))
            {
                sb.Append(": ").Append(d.m_ConnectionType).Append(", activity ").Append(MaskText(d.m_ActivityMask.m_Mask));
                if (d.m_HangaroundOnLane)
                    sb.Append(", hangaround-on-lane");
                if (d.m_RequireAuthorization)
                    sb.Append(", restricted");
            }
            else
                sb.Append(": no SpawnLocationData on the prefab");
            if (EntityManager.TryGetComponent(e, out ObjSpawnLocation sl))
            {
                sb.Append("; group ").Append(sl.m_GroupIndex).Append(", flags ").Append(sl.m_Flags)
                  .Append(", lane1 ").Append(sl.m_ConnectedLane1 != Entity.Null ? "linked" : "none")
                  .Append(", lane2 ").Append(sl.m_ConnectedLane2 != Entity.Null ? "linked" : "none");
            }
            sb.Append("; registered ").Append(isRegistered ? "yes" : "NO");
            return sb.ToString();
        }

        private string DescribeArea(Entity area, bool isRegistered, bool hasT, ObjTransform lodgeT)
        {
            var sb = new StringBuilder(160);
            bool hang = EntityManager.HasComponent<Game.Areas.HangaroundLocation>(area);
            sb.Append(hang ? "hangaround area " : "area ").Append(Ent(area)).Append(" '").Append(PrefabNameOf(area)).Append('\'');
            if (EntityManager.TryGetComponent(area, out Game.Areas.Geometry g))
                sb.Append(" centre ").Append(hasT ? Fmt(ToLocal(g.m_CenterPosition, lodgeT)) : Fmt(g.m_CenterPosition)).Append(", surface ")
                  .Append(g.m_SurfaceArea.ToString("F0", Ic)).Append(" m2");
            if (EntityManager.TryGetBuffer(area, isReadOnly: true, out DynamicBuffer<Game.Areas.Node> nodes))
                sb.Append(", ").Append(nodes.Length).Append(" node(s)");
            Entity prefab = EntityManager.TryGetComponent(area, out PrefabRef pr) ? pr.m_Prefab : Entity.Null;
            if (prefab != Entity.Null && EntityManager.TryGetComponent(prefab, out SpawnLocationData d))
                sb.Append(", ").Append(d.m_ConnectionType).Append(", activity ").Append(MaskText(d.m_ActivityMask.m_Mask));
            if (EntityManager.TryGetBuffer(area, isReadOnly: true, out DynamicBuffer<Game.Net.SubLane> lanes))
            {
                int ped = 0, total = lanes.Length;
                for (int i = 0; i < total; i++)
                {
                    if (EntityManager.TryGetComponent(lanes[i].m_SubLane, out Game.Net.ConnectionLane cl) &&
                        (cl.m_Flags & Game.Net.ConnectionLaneFlags.Pedestrian) != 0)
                        ped++;
                }
                sb.Append("; lanes ").Append(total).Append(" (").Append(ped).Append(" pedestrian connection)");
            }
            else
                sb.Append("; no lanes");
            sb.Append("; registered ").Append(isRegistered ? "yes" : (hang ? "NO" : "n/a (not a spawn type)"));
            return sb.ToString();
        }

        private void WalkObjects(Entity owner, Entity lodge, bool hasT, ObjTransform lodgeT, int depth, HashSet<Entity> registered,
                                 HashSet<Entity> seen, List<string> lines, ref int extra, ref int scanned)
        {
            if (depth > MaxWalkDepth || !EntityManager.TryGetBuffer(owner, isReadOnly: true, out DynamicBuffer<ObjSubObject> subs))
                return;
            var children = new List<Entity>(subs.Length);
            for (int i = 0; i < subs.Length; i++)
                children.Add(subs[i].m_SubObject);
            for (int i = 0; i < children.Count; i++)
            {
                Entity e = children[i];
                if (scanned >= MaxWalkObjects)
                    return;
                if (!EntityManager.Exists(e) || !seen.Add(e))
                    continue;
                scanned++;
                if (EntityManager.HasComponent<ObjSpawnLocation>(e) && !registered.Contains(e))
                    AddLine(lines, ref extra, DescribeSpawn(e, false, lodge, hasT, lodgeT));
                else if (EntityManager.TryGetComponent(e, out Game.Routes.TakeoffLocation take))
                {
                    AddLine(lines, ref extra, "access marker " + Ent(e) + " '" + PrefabNameOf(e) + "' at " + LocalOf(e, hasT, lodgeT) +
                                              " depth " + OwnerDepth(e, lodge) + ": TakeoffLocation flags " + take.m_Flags +
                                              " (not a spawn spot: never in the buffer)");
                }
                WalkObjects(e, lodge, hasT, lodgeT, depth + 1, registered, seen, lines, ref extra, ref scanned);
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Small helpers
        // ════════════════════════════════════════════════════════════════════════════════════════

        private string LocalOf(Entity e, bool hasT, ObjTransform lodgeT)
        {
            if (!EntityManager.TryGetComponent(e, out ObjTransform t))
                return "?";
            return hasT ? Fmt(ToLocal(t.m_Position, lodgeT)) : Fmt(t.m_Position);
        }

        private static float3 ToLocal(float3 world, ObjTransform lodgeT)
        {
            return math.mul(math.inverse(lodgeT.m_Rotation), world - lodgeT.m_Position);
        }

        private int OwnerDepth(Entity e, Entity lodge)
        {
            int depth = 0;
            Entity cur = e;
            while (depth < 8 && EntityManager.TryGetComponent(cur, out Owner o))
            {
                depth++;
                if (o.m_Owner == lodge)
                    return depth;
                cur = o.m_Owner;
            }
            return -1;
        }

        private string PrefabNameOf(Entity e)
        {
            try
            {
                if (EntityManager.TryGetComponent(e, out PrefabRef pr) && pr.m_Prefab != Entity.Null)
                    return m_PrefabSystem.GetPrefabName(pr.m_Prefab);
            }
            catch (Exception)
            {
            }
            return "?";
        }

        private static string MaskText(uint mask)
        {
            if (mask == 0u)
                return "none (0)";
            var sb = new StringBuilder(48);
            for (int bit = 0; bit < 32; bit++)
            {
                if ((mask & (1u << bit)) == 0u)
                    continue;
                if (sb.Length > 0)
                    sb.Append('|');
                sb.Append(((ActivityType)(bit + 1)).ToString());   // ActivityMask(type): 1 << (type - 1), ActivityMask.cs
            }
            return sb.ToString();
        }

        private static string Fmt(float3 p)
        {
            return "(" + p.x.ToString("F1", Ic) + ", " + p.y.ToString("F1", Ic) + ", " + p.z.ToString("F1", Ic) + ")";
        }

        private static string Ent(Entity e)
        {
            return e == Entity.Null ? "null" : e.Index + ":" + e.Version;
        }
    }
}
