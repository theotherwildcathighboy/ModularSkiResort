// Every lift line tower shows its own number. Pure rules, tables and the per-owner diff: TowerNumberTiles.cs.
// Per numbered tower and plate face, 1-3 one-quad tile objects cover the baked height numeral with the tower's order number. The
// tiles are vanilla object entities: Owner = the tower, Secondary, Elevation(local y, flags 0). The baked plate is the fallback:
// with the tile prefabs absent this system writes nothing and logs one line per load.
// Never: Temp or Deleted towers; towers whose prefab has no plate row (counted in the order, not tiled); Editor mode.
// Log discipline: one ARMED / OFF line per load, one line per pass that changed anything, a one-shot "!!!" catch.
// Mod.cs: UpdateAfter<TowerNumberTileSystem, LiftCensusSystem>(Modification4B), before Modification5 (the references system).
// Engine notes: EngineNotes/TowerNumberTileSystem.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using SubObject = Game.Objects.SubObject;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class TowerNumberTileSystem : GameSystemBase
    {
        internal const int SettleFrames = 20;

        private PrefabSystem m_PrefabSystem;
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private LiftTowerDiagSystem m_Diag;
        private EntityQuery m_UpdatedOwned;
        private EntityQuery m_TilePrefabScan;

        private bool m_Armed, m_Disabled, m_Dirty;
        private int m_Frame, m_DirtyFrame;
        private string m_DirtyWhy = "load";
        private readonly List<LiftCensus.Edge> m_Edges = new List<LiftCensus.Edge>();

        // Tile prefabs, resolved lazily once per load: [cls * 11 + glyph + 1] (TowerNumberTiles.Key).
        private bool m_Resolved;
        private Entity[] m_Tile = new Entity[0];
        private bool[] m_ClassReady = new bool[0];
        private readonly Dictionary<Entity, int> m_KeyByTilePrefab = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, int> m_PlateByTowerPrefab = new Dictionary<Entity, int>();   // -1 = no plate row

        private readonly List<TowerNumberTiles.TileSpec> m_Desired = new List<TowerNumberTiles.TileSpec>();
        private readonly List<TowerNumberTiles.Slot> m_Slots = new List<TowerNumberTiles.Slot>();
        private readonly List<TowerNumberTiles.Existing> m_Existing = new List<TowerNumberTiles.Existing>();
        private readonly List<Entity> m_ExistingEntities = new List<Entity>();
        private readonly List<int> m_Del = new List<int>(), m_Add = new List<int>();
        private int m_BuffersAdded, m_Passes;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Diag = World.GetOrCreateSystemManaged<LiftTowerDiagSystem>();
            m_UpdatedOwned = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Objects.Object>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Updated>(),
                              ComponentType.ReadOnly<SubObject>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Secondary>() },
            });
            m_TilePrefabScan = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<ObjectData>());
            Mod.Log.Info($"{nameof(TowerNumberTileSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_Dirty = false;
            m_Frame = 0;
            m_DirtyFrame = 0;
            m_Edges.Clear();
            m_Resolved = false;
            m_KeyByTilePrefab.Clear();
            m_PlateByTowerPrefab.Clear();
            m_BuffersAdded = 0;
            m_Passes = 0;
            if (!m_Armed)
                Mod.Log.Info($"Towers [numbers]: NOT ARMED (mode {mode})");
        }

        /// <summary>Called by LiftCensusSystem after each build with its line edges (main thread, no ECS read here).</summary>
        internal void OnCensusBuilt(List<LiftCensus.Edge> edges)
        {
            if (!m_Armed || m_Disabled)
                return;
            m_Edges.Clear();
            m_Edges.AddRange(edges);
            Arm("census");
        }

        private void Arm(string why)
        {
            if (!m_Dirty)
                m_DirtyWhy = why;
            m_Dirty = true;
            m_DirtyFrame = m_Frame;
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            m_Frame++;
            try
            {
                if (m_Resolved && m_KeyByTilePrefab.Count > 0 && !m_UpdatedOwned.IsEmptyIgnoreFilter && PlatedTowerUpdated())
                    Arm("tower updated");
                if (!m_Dirty || m_Frame - m_DirtyFrame < SettleFrames)
                    return;
                m_Dirty = false;
                Pass(m_DirtyWhy);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! Towers [numbers]: {e.GetType().Name}: {e.Message} — disabled for this load (baked plates stay)\n{e}");
            }
        }

        private bool PlatedTowerUpdated()
        {
            NativeArray<Entity> upd = m_UpdatedOwned.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < upd.Length; i++)
                    if (PlateOf(EntityManager.GetComponentData<PrefabRef>(upd[i]).m_Prefab) >= 0)
                        return true;
                return false;
            }
            finally
            {
                upd.Dispose();
            }
        }


        private void Resolve()
        {
            if (m_Resolved)
                return;
            m_Resolved = true;
            int nc = TowerNumberTiles.Classes.Length;
            m_Tile = new Entity[nc * TowerNumberTiles.GlyphsPerClass];
            m_ClassReady = new bool[nc];
            var missing = new List<string>();
            Dictionary<string, Entity> scan = null;
            for (int c = 0; c < nc; c++)
            {
                bool ready = true;
                for (int g = TowerNumberTiles.Blank; g <= 9; g++)
                {
                    string name = TowerNumberTiles.PrefabName(c, g);
                    Entity pe = Entity.Null;
                    if (m_PrefabSystem.TryGetPrefab(new PrefabID("StaticObjectPrefab", name), out PrefabBase pb))
                        m_PrefabSystem.TryGetEntity(pb, out pe);
                    if (pe == Entity.Null)
                    {
                        scan = scan ?? ScanObjectPrefabs();
                        scan.TryGetValue(name, out pe);
                    }
                    if (pe == Entity.Null || !EntityManager.TryGetComponent(pe, out ObjectData od) || !od.m_Archetype.Valid)
                    {
                        ready = false;
                        missing.Add(name);
                        continue;
                    }
                    m_Tile[TowerNumberTiles.Key(c, g)] = pe;
                }
                m_ClassReady[c] = ready;
                if (ready)
                    for (int g = TowerNumberTiles.Blank; g <= 9; g++)
                        m_KeyByTilePrefab[m_Tile[TowerNumberTiles.Key(c, g)]] = TowerNumberTiles.Key(c, g);
            }
            var ready1 = new List<string>();
            for (int c = 0; c < nc; c++)
                if (m_ClassReady[c])
                    ready1.Add(TowerNumberTiles.Classes[c].Id);
            if (ready1.Count == 0)
                Mod.Log.Info($"Towers [numbers]: OFF — no tile prefab set is loaded ({missing.Count} of {nc * TowerNumberTiles.GlyphsPerClass} " +
                             $"missing, e.g. {missing[0]}; import pending): the towers show their baked plates, nothing is written");
            else
                Mod.Log.Info($"Towers [numbers]: ARMED — classes {string.Join(", ", ready1)} (tiles 1..{TowerNumberTiles.MaxNumber}, wrap)" +
                             (missing.Count > 0 ? $"; {missing.Count} prefab(s) missing, e.g. {missing[0]}: that class keeps its baked plates" : string.Empty));
        }

        private Dictionary<string, Entity> ScanObjectPrefabs()
        {
            var map = new Dictionary<string, Entity>(StringComparer.Ordinal);
            NativeArray<Entity> all = m_TilePrefabScan.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < all.Length; i++)
                {
                    string n = m_PrefabSystem.GetPrefabName(all[i]);
                    if (n != null && n.StartsWith("SkiNumTile", StringComparison.Ordinal) && !map.ContainsKey(n))
                        map[n] = all[i];
                }
            }
            finally
            {
                all.Dispose();
            }
            return map;
        }

        private int PlateOf(Entity towerPrefab)
        {
            if (m_PlateByTowerPrefab.TryGetValue(towerPrefab, out int idx))
                return idx;
            idx = -1;
            string name = m_PrefabSystem.GetPrefabName(towerPrefab);
            for (int i = 0; i < TowerNumberTiles.Plates.Length; i++)
                if (string.Equals(TowerNumberTiles.Plates[i].Prefab, name, StringComparison.Ordinal))
                    idx = i;
            m_PlateByTowerPrefab[towerPrefab] = idx;
            return idx;
        }


        private void Pass(string why)
        {
            Resolve();
            if (m_KeyByTilePrefab.Count == 0)
                return;                                   // OFF (logged once by Resolve)
            long t0 = Stopwatch.GetTimestamp();
            m_Passes++;
            int lines = 0, numbered = 0, created = 0, deleted = 0, skippedLines = 0, untiled = 0;
            var seen = new HashSet<Entity>();
            List<List<LiftCensus.Edge>> comps = LiftTowerDiagSystem.Components(m_Edges);
            for (int c = 0; c < comps.Count; c++)
            {
                List<LiftCensus.Edge> comp = comps[c];
                bool stale = false;
                for (int i = 0; i < comp.Count && !stale; i++)
                {
                    Entity edge = LiftCensusSystem.Ent(comp[i].Id);
                    stale = !EntityManager.Exists(edge) || EntityManager.HasComponent<Deleted>(edge);
                }
                if (stale)
                    continue;                             // a newer census build carries the replacement
                List<List<LiftTowerDiagSystem.Hop>> paths = m_Diag.Paths(comp);
                bool any = false;
                for (int p = 0; p < paths.Count; p++)
                {
                    int r = NumberPath(paths[p], seen, ref numbered, ref created, ref deleted, ref untiled);
                    if (r < 0)
                        skippedLines++;
                    else
                        any = true;
                }
                if (any)
                    lines++;
            }
            float ms = (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            if (created + deleted > 0 || m_Passes == 1 || skippedLines > 0)
                Mod.Log.Info($"Towers [numbers]: pass {m_Passes} ({why}): {lines} line(s), {numbered} tower(s) numbered, tiles +{created} / -{deleted}" +
                             (untiled > 0 ? $", {untiled} tower(s) without a plate row (not tiled)" : string.Empty) +
                             (skippedLines > 0 ? $", {skippedLines} path(s) with no station role at either end (left as they are)" : string.Empty) +
                             (m_BuffersAdded > 0 ? $", SubObject buffers added so far {m_BuffersAdded}" : string.Empty) +
                             $" ({ms:F2} ms)");
        }

        /// <summary>Numbers one path's towers and reconciles their tiles. -1 = no base known (path left alone).</summary>
        private int NumberPath(List<LiftTowerDiagSystem.Hop> path, HashSet<Entity> seen, ref int numbered, ref int created, ref int deleted,
                               ref int untiled)
        {
            int n = path.Count;
            if (n < 2)
                return 0;
            int roleA = StationRoleOf(path[0].Node), roleB = StationRoleOf(path[n - 1].Node);
            if (roleA < 0 && roleB < 0)
                return -1;
            bool baseFirst = !(roleA == (int)StationRole.Top || roleB == (int)StationRole.Base);
            var along = new List<float>();
            var towers = new List<Entity>();
            float s = 0f;
            float3 prev = default;
            for (int i = 0; i < n; i++)
            {
                if (!EntityManager.TryGetComponent<Game.Net.Node>(path[i].Node, out var node))
                    continue;
                if (i > 0)
                    s += math.distance(prev.xz, node.m_Position.xz);
                prev = node.m_Position;
                if (i == 0 || i == n - 1)
                    continue;                             // station stub nodes: not line towers
                Entity tower = TowerOf(path[i].Node);
                if (tower == Entity.Null || !seen.Add(tower))
                    continue;
                along.Add(s);
                towers.Add(tower);
            }
            if (!baseFirst)
                for (int i = 0; i < along.Count; i++)
                    along[i] = s - along[i];
            int[] num = TowerNumberTiles.DisplayNumbers(along);
            for (int i = 0; i < towers.Count; i++)
            {
                int r = ReconcileTower(towers[i], num[i], ref created, ref deleted);
                if (r > 0)
                    numbered++;
                else if (r < 0)
                    untiled++;
            }
            return towers.Count;
        }

        /// <summary>1 = numbered, 0 = skipped (Temp / class not loaded), -1 = no plate row.</summary>
        private int ReconcileTower(Entity tower, int number, ref int created, ref int deleted)
        {
            if (EntityManager.HasComponent<Temp>(tower) || EntityManager.HasComponent<Deleted>(tower) ||
                !EntityManager.TryGetComponent(tower, out PrefabRef tp) || !EntityManager.TryGetComponent(tower, out Transform tt))
                return 0;
            int pi = PlateOf(tp.m_Prefab);
            if (pi < 0)
                return -1;
            TowerNumberTiles.TowerPlate plate = TowerNumberTiles.Plates[pi];
            if (!m_ClassReady[plate.Class])
                return 0;
            TowerNumberTiles.Desired(plate, number, m_Desired, m_Slots);

            m_Existing.Clear();
            m_ExistingEntities.Clear();
            quaternion inv = math.inverse(tt.m_Rotation);
            if (EntityManager.TryGetBuffer(tower, true, out DynamicBuffer<SubObject> subs))
            {
                for (int i = 0; i < subs.Length; i++)
                {
                    Entity e = subs[i].m_SubObject;
                    if (EntityManager.HasComponent<Deleted>(e) || !EntityManager.TryGetComponent(e, out PrefabRef ep) ||
                        !m_KeyByTilePrefab.TryGetValue(ep.m_Prefab, out int key) || !EntityManager.TryGetComponent(e, out Transform et))
                        continue;
                    m_Existing.Add(new TowerNumberTiles.Existing { Key = key, LocalPos = math.mul(inv, et.m_Position - tt.m_Position) });
                    m_ExistingEntities.Add(e);
                }
            }
            TowerNumberTiles.Reconcile(m_Existing, m_Desired, m_Del, m_Add);
            for (int i = 0; i < m_Del.Count; i++)
                EntityManager.AddComponent<Deleted>(m_ExistingEntities[m_Del[i]]);
            deleted += m_Del.Count;
            if (m_Add.Count > 0 && !EntityManager.HasBuffer<SubObject>(tower))
            {
                EntityManager.AddBuffer<SubObject>(tower);   // before the first Created child (references system, Modification5)
                m_BuffersAdded++;
            }
            for (int i = 0; i < m_Add.Count; i++)
            {
                TowerNumberTiles.TileSpec spec = m_Desired[m_Add[i]];
                Entity prefab = m_Tile[spec.Key];
                EntityArchetype arch = EntityManager.GetComponentData<ObjectData>(prefab).m_Archetype;
                var one = new NativeArray<Entity>(1, Allocator.Temp);   // the (archetype, array) overload: PowerBridgeSystem's idiom
                EntityManager.CreateEntity(arch, one);
                Entity e = one[0];
                one.Dispose();
                EntityManager.AddComponentData(e, new Owner(tower));
                EntityManager.SetComponentData(e, new PrefabRef(prefab));
                EntityManager.SetComponentData(e, ObjectUtils.LocalToWorld(tt, spec.LocalPos, spec.LocalRot));
                EntityManager.AddComponent<Secondary>(e);
                EntityManager.AddComponentData(e, new Elevation(spec.LocalPos.y, (ElevationFlags)0));
                if (EntityManager.HasComponent<PseudoRandomSeed>(e))
                    EntityManager.SetComponentData(e, new PseudoRandomSeed((ushort)math.max(1u, math.hash(new int3(tower.Index, spec.Key, (int)(spec.LocalPos.x * 1000f))) & 0xFFFFu)));
            }
            created += m_Add.Count;
            return 1;
        }

        /// <summary>The first non-carrier, non-deleted sub-object of the node (LiftTowerDiagSystem.TowerName's rule).</summary>
        private Entity TowerOf(Entity node)
        {
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<SubObject> subs))
                return Entity.Null;
            IReadOnlyDictionary<Entity, float> carriers = m_PrefabCatalog.CarrierGripTops;
            for (int i = 0; i < subs.Length; i++)
            {
                Entity sub = subs[i].m_SubObject;
                if (EntityManager.HasComponent<Deleted>(sub) || !EntityManager.TryGetComponent(sub, out PrefabRef sp) || carriers.ContainsKey(sp.m_Prefab))
                    continue;
                return sub;
            }
            return Entity.Null;
        }

        /// <summary>StationRole of the station whose sub-net owns <paramref name="node"/> (-1 = not a catalog station's node).</summary>
        private int StationRoleOf(Entity node)
        {
            if (!EntityManager.TryGetComponent(node, out Owner owner) || !EntityManager.TryGetComponent(owner.m_Owner, out PrefabRef op))
                return -1;
            StationDef st = SkiCatalog.StationByPrefab(m_PrefabSystem.GetPrefabName(op.m_Prefab));
            return st != null ? (int)st.Role : -1;
        }
    }
}
