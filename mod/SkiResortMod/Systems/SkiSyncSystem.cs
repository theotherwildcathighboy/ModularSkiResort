// Sync Resort: brings objects built under older rules up to date (T229, DECISIONS 670; design
// docs/research/round2-2026-11-16/sync-resort/DESIGN.md). The step list and its pure rules are SkiSyncCatalog; the level an object was last
// checked at is its SkiRulesStamp (absent = 0, SkiRulesStampSystem). Each step has its own "needs it?" check: the stamp only says which steps
// were already checked, the check is the truth.
// After a load (once the stamp system's load census ran, and the area links are known or LinkWaitFrames passed), the intake checks every
// object of a kind some step acts on, IntakePerFrame per frame:
//   * no step needed: the object is stamped Current without a word (it is never checked again for these steps),
//   * an Auto step needed: it runs by itself (invisible fixes, 670),
//   * an Offer step needed: the object is listed under its ski area's "Sync resort (N)" button in the hub,
//   * a step that skips hand-edited lines, on a line edited by hand: listed as skipped, never synced,
//   * a stamp newer than this build: left alone.
// The hub's Sync card (SkiResortHubUISystem.Sync.cs) shows the area's list with a per-item skip, "Keep as is" per item (stamps it Current
// with the Kept bit) and one Sync button. Sync is free and has no undo (670). The run queue starts ApplyPerFrame objects per frame and holds
// while a tool other than the default tool is active (the player is editing). After each step's action the step's own check runs again
// VerifyFrames later: still needed = the object failed (one WARN line, it keeps its level, no retry until the next load); done = the next
// needed step runs, or the object is stamped Current.
// Step 1 (patrolParts): a Patrol Hut or the Patrol HQ lacks a part of its prefab (the 1.0.1 firewood pile). Action: an
// {Event, SubObjectsUpdated(building)} entity, vanilla's own way to rebuild one owner's parts (WetnessSystem.cs:93-94 in 1.6.4f1).
// Game.Objects.SubObjectSystem (Modification2B) then reuses every placed part of a matching prefab (nearest first, FindOldSubObject), adds
// what is missing and deletes what the prefab no longer has (SubObjectSystem.cs:1172-1260, :3307-3365). The event entity is destroyed at the
// frame's cleanup (PrepareCleanUpSystem), so it is created here, at Modification1, before Modification2B.
// Dev read: one "Sync [assets]" line per load: every building of ours whose placed parts miss rows of its prefab, by building and part prefab
// (the test for "does an asset change reach placed buildings by itself"). One "Sync [intake]" line per load, one "Sync [run]" line per Sync,
// one "Sync [done]" / WARN "Sync [failed]" line per object.
// Phase Modification1 (Mod.cs; load-bearing: the event must exist before Modification2B). Game mode only. Main thread: our objects only
// (tens to a few hundred), never an O(city) loop. One "!!!" disables the engine until the next load.
// Engine notes: EngineNotes/SkiSyncSystem.md

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

namespace SkiResortMod.Systems
{
    public sealed partial class SkiSyncSystem : GameSystemBase
    {
        /// <summary>The state of one listed object.</summary>
        internal enum ItemState : byte
        {
            Open = 0,       // waits for the player
            Queued = 1,     // in the run queue
            Working = 2,    // a step ran, its check is due
            Failed = 3,     // the check still said "needed" after the step; keeps its level until the next load
            HandEdited = 4, // the needed step skips lines edited by hand: listed, never synced
        }

        internal struct Item
        {
            public Entity Entity;
            public SkiRulesKind Kind;
            public int Step;          // the needed step (the first one the intake found)
            public ItemState State;
            public bool Include;      // the card's per-item check box: false = skip this one
            public int VerifyAt;      // frame of the step's check while Working
        }

        // Game.Prefabs.SubObjectFlags Require* bits (SubObjectFlags.cs, 1.6.4f1): a row placed only on a condition
        private const int RequireMask = 0x10 | 0x20 | 0x40 | 0x80;

        private SkiRulesStampSystem m_Stamp;
        private SkiPrefabCatalogSystem m_Catalog;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private ToolSystem m_ToolSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_Buildings, m_Edges, m_Areas;
        private EntityArchetype m_PartsEvent;

        private bool m_Armed, m_Disabled, m_ErrorLogged, m_IntakeDone, m_AssetsLogged;
        private int m_Frame, m_IntakeAt, m_Revision, m_HoldLogged;
        private readonly List<Entity> m_Intake = new List<Entity>(256);
        private readonly List<Item> m_Items = new List<Item>(32);
        private readonly List<Entity> m_Queue = new List<Entity>(32);
        private readonly int[] m_VerdictCount = new int[6];
        private readonly Dictionary<int, int> m_OfferByStep = new Dictionary<int, int>();
        private int m_RunDone, m_RunFailed, m_RunTotal;
        private readonly Dictionary<Entity, int> m_Rows = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, int> m_Placed = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, Entity> m_VariantOf = new Dictionary<Entity, Entity>();   // variant prefab -> its placeholder row

        /// <summary>Moves when the item list or an item's state changes (the hub pushes on it).</summary>
        internal int Revision => m_Revision;

        /// <summary>The intake finished for this load (the hub shows nothing before).</summary>
        internal bool IntakeDone => m_IntakeDone;

        /// <summary>A Sync is running: objects queued or waiting for their check.</summary>
        internal bool Running
        {
            get
            {
                if (m_Queue.Count > 0)
                    return true;
                foreach (Item it in m_Items)
                    if (it.State == ItemState.Working)
                        return true;
                return false;
            }
        }

        /// <summary>The last Sync's counts (this session): objects synced, failed, and started.</summary>
        internal int RunDone => m_RunDone;
        internal int RunFailed => m_RunFailed;
        internal int RunTotal => m_RunTotal;

        /// <summary>The listed objects. Read only; the hub filters by area.</summary>
        internal IReadOnlyList<Item> Items => m_Items;

        /// <summary>The hub's Sync card is open (the world markers draw only then) and the area it shows (Entity.Null = Not linked).</summary>
        internal bool CardOpen { get; private set; }
        internal Entity CardSign { get; private set; }

        internal void SetCard(bool open, Entity sign)
        {
            if (CardOpen == open && CardSign == sign)
                return;
            CardOpen = open;
            CardSign = open ? sign : Entity.Null;
            m_Revision++;
        }

        /// <summary>The ski area an object belongs to: the Welcome Sign a top-level building links to (the last attraction recompute), or
        /// Entity.Null (Not linked, or no link known yet).</summary>
        internal Entity AreaOf(Entity e)
        {
            return m_Attractiveness.TryGetTopLevelLink(e, out Entity sign) ? sign : Entity.Null;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Stamp = World.GetOrCreateSystemManaged<SkiRulesStampSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Buildings = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_Edges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
            m_Areas = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Areas.Area>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            // WetnessSystem.cs:161 (1.6.4f1): vanilla's archetype for "rebuild this owner's parts"
            m_PartsEvent = EntityManager.CreateArchetype(ComponentType.ReadWrite<Game.Common.Event>(), ComponentType.ReadWrite<SubObjectsUpdated>());
            Mod.Log.Info($"{nameof(SkiSyncSystem)} created (Sync Resort: {SkiSyncCatalog.Steps.Length} step(s), rules level " +
                         $"{SkiSyncCatalog.Current})");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = m_IntakeDone = m_AssetsLogged = false;
            m_Frame = m_IntakeAt = m_HoldLogged = 0;
            m_RunDone = m_RunFailed = m_RunTotal = 0;
            m_Intake.Clear();
            m_Items.Clear();
            m_Queue.Clear();
            CardOpen = false;
            CardSign = Entity.Null;
            m_Revision++;
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                m_Frame++;
                if (!m_IntakeDone)
                {
                    StepIntake();
                    return;
                }
                CheckDue();
                RunQueue();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                m_Queue.Clear();
                m_Revision++;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiSyncSystem)} EXCEPTION — Sync Resort is off until the next load (nothing else changes). " +
                                  $"Please report this stack:\n{e}");
                }
            }
        }

        // ---- intake --------------------------------------------------------------------------------------------------------------

        private void StepIntake()
        {
            if (m_Intake.Count == 0 && m_IntakeAt == 0)
            {
                if (!m_Stamp.LoadSettled || !m_Catalog.IsResolved)
                    return;
                if (!m_Attractiveness.HasSnapshot && m_Frame < SkiSyncCatalog.LinkWaitFrames)
                    return;   // the area links come with the first attraction recompute
                if (!m_AssetsLogged)
                {
                    m_AssetsLogged = true;
                    LogAssets();
                }
                CollectIntake();
                Array.Clear(m_VerdictCount, 0, m_VerdictCount.Length);
                m_OfferByStep.Clear();
                if (m_Intake.Count == 0)
                {
                    FinishIntake();
                    return;
                }
            }
            int end = Math.Min(m_Intake.Count, m_IntakeAt + SkiSyncCatalog.IntakePerFrame);
            for (; m_IntakeAt < end; m_IntakeAt++)
                IntakeOne(m_Intake[m_IntakeAt]);
            if (m_IntakeAt >= m_Intake.Count)
                FinishIntake();
        }

        private void CollectIntake()
        {
            m_Intake.Clear();
            int kinds = SkiSyncCatalog.KindsTargeted();
            const int buildingKinds = (1 << (int)SkiRulesKind.Station) | (1 << (int)SkiRulesKind.Lodge) | (1 << (int)SkiRulesKind.School) |
                                      (1 << (int)SkiRulesKind.Patrol) | (1 << (int)SkiRulesKind.Depot) | (1 << (int)SkiRulesKind.Sign);
            const int areaKinds = (1 << (int)SkiRulesKind.Run) | (1 << (int)SkiRulesKind.CatTrack);
            if ((kinds & buildingKinds) != 0)
                Collect(m_Buildings, kinds);
            if ((kinds & (1 << (int)SkiRulesKind.LiftEdge)) != 0)
                Collect(m_Edges, kinds);
            if ((kinds & areaKinds) != 0)
                Collect(m_Areas, kinds);
        }

        private void Collect(EntityQuery query, int kinds)
        {
            if (query.IsEmptyIgnoreFilter)
                return;
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    int kind = m_Stamp.KindOfEntity(entities[i]);
                    if (kind >= 0 && (kinds & (1 << kind)) != 0)
                        m_Intake.Add(entities[i]);
                }
            }
        }

        private void IntakeOne(Entity e)
        {
            if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e))
                return;
            int kind = m_Stamp.KindOfEntity(e);
            if (kind < 0)
                return;
            bool has = EntityManager.TryGetComponent(e, out SkiRulesStamp stamp);
            int level = SkiRulesLevel.LevelOf(has, stamp.m_Rules);
            byte flags = has ? stamp.m_Flags : (byte)0;
            SyncVerdict v = SkiSyncCatalog.Decide(level, flags, (SkiRulesKind)kind, n => Needs(n, e), out int step);
            m_VerdictCount[(int)v]++;
            switch (v)
            {
                case SyncVerdict.Silent:
                    SkiRulesStampSystem.StampCurrent(EntityManager, e, 0);
                    break;
                case SyncVerdict.Auto:
                    AddItem(e, (SkiRulesKind)kind, step, ItemState.Queued);
                    m_Queue.Add(e);
                    break;
                case SyncVerdict.Offer:
                    AddItem(e, (SkiRulesKind)kind, step, ItemState.Open);
                    m_OfferByStep[step] = (m_OfferByStep.TryGetValue(step, out int n0) ? n0 : 0) + 1;
                    break;
                case SyncVerdict.HandEdited:
                    AddItem(e, (SkiRulesKind)kind, step, ItemState.HandEdited);
                    break;
            }
        }

        private void AddItem(Entity e, SkiRulesKind kind, int step, ItemState state)
        {
            m_Items.Add(new Item { Entity = e, Kind = kind, Step = step, State = state, Include = state == ItemState.Open });
        }

        private void FinishIntake()
        {
            m_IntakeDone = true;
            m_Revision++;
            var sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in m_OfferByStep)
            {
                SkiSyncCatalog.TryGetStep(kv.Key, out SyncStep s);
                sb.Append(sb.Length > 0 ? ", " : string.Empty).Append("step ").Append(kv.Key).Append(' ').Append(s.Id).Append(": ").Append(kv.Value);
            }
            Mod.Log.Info($"Sync [intake]: {m_Intake.Count} object(s) checked at rules level {SkiSyncCatalog.Current} — " +
                         $"{m_VerdictCount[(int)SyncVerdict.Offer]} offered under Sync ({(sb.Length > 0 ? sb.ToString() : "none")}), " +
                         $"{m_VerdictCount[(int)SyncVerdict.Auto]} fixed by themselves, " +
                         $"{m_VerdictCount[(int)SyncVerdict.Silent]} already current (stamped), " +
                         $"{m_VerdictCount[(int)SyncVerdict.NothingPending]} with nothing to check, " +
                         $"{m_VerdictCount[(int)SyncVerdict.HandEdited]} edited by hand (skipped), " +
                         $"{m_VerdictCount[(int)SyncVerdict.Newer]} from a newer build (left alone)" +
                         (m_Attractiveness.HasSnapshot ? string.Empty : "; area links not known yet: items show under Not linked until they are"));
            m_Intake.Clear();
            m_IntakeAt = 0;
        }

        // ---- steps ---------------------------------------------------------------------------------------------------------------

        /// <summary>The step's own check: does this object still need step <paramref name="step"/>?</summary>
        private bool Needs(int step, Entity e)
        {
            switch (step)
            {
                case SkiSyncCatalog.StepPatrolParts:
                    return MissingParts(e, null) > 0;
                default:
                    return false;   // a retired or unknown step never acts
            }
        }

        /// <summary>The step's action. Returns false when it cannot start (the object then fails at once).</summary>
        private bool Apply(int step, Entity e)
        {
            switch (step)
            {
                case SkiSyncCatalog.StepPatrolParts:
                    Entity ev;
                    using (var created = new NativeArray<Entity>(1, Allocator.Temp))
                    {
                        EntityManager.CreateEntity(m_PartsEvent, created);   // the single-entity overload needs ReadOnlySpan, absent from this target
                        ev = created[0];
                    }
                    EntityManager.SetComponentData(ev, new SubObjectsUpdated(e));
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Parts the building lacks: for each part prefab, the rows vanilla always places (SkiSyncCatalog.RowCounts) minus the placed
        /// parts of that prefab owned by the building. <paramref name="detail"/> (optional) gets "prefab xN" per missing prefab.</summary>
        internal int MissingParts(Entity building, StringBuilder detail)
        {
            return CountParts(building, detail, out int _);
        }

        private int CountParts(Entity building, StringBuilder detail, out int extra)
        {
            extra = 0;
            if (!EntityManager.TryGetComponent(building, out PrefabRef pr) ||
                !EntityManager.TryGetBuffer(pr.m_Prefab, true, out DynamicBuffer<Game.Prefabs.SubObject> rows) || rows.Length == 0)
                return 0;
            m_Rows.Clear();
            m_VariantOf.Clear();
            for (int i = 0; i < rows.Length; i++)
            {
                Game.Prefabs.SubObject r = rows[i];
                Entity p = r.m_Prefab;
                if (p == Entity.Null)
                    continue;
                bool placeholder = EntityManager.TryGetBuffer(p, true, out DynamicBuffer<PlaceholderObjectElement> variants);
                bool requirement = EntityManager.HasBuffer<ObjectRequirementElement>(p);
                bool freeVariant = false;
                if (placeholder)
                    for (int v = 0; v < variants.Length; v++)
                        if (variants[v].m_Object != Entity.Null && !EntityManager.HasBuffer<ObjectRequirementElement>(variants[v].m_Object))
                            freeVariant = true;
                if (!SkiSyncCatalog.RowCounts(r.m_Probability, (int)r.m_Flags & RequireMask, placeholder, requirement, freeVariant))
                    continue;
                m_Rows[p] = (m_Rows.TryGetValue(p, out int n) ? n : 0) + 1;
                // A placed part of any variant satisfies the placeholder row (the hut's firewood row spawns FirewoodPile01)
                if (placeholder)
                    for (int v = 0; v < variants.Length; v++)
                        if (variants[v].m_Object != Entity.Null && !m_VariantOf.ContainsKey(variants[v].m_Object))
                            m_VariantOf[variants[v].m_Object] = p;
            }
            if (m_Rows.Count == 0)
                return 0;
            m_Placed.Clear();
            if (EntityManager.TryGetBuffer(building, true, out DynamicBuffer<Game.Objects.SubObject> parts))
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    Entity c = parts[i].m_SubObject;
                    if (!EntityManager.Exists(c) || EntityManager.HasComponent<Deleted>(c) || EntityManager.HasComponent<Temp>(c))
                        continue;
                    if (!EntityManager.TryGetComponent(c, out Owner o) || o.m_Owner != building)
                        continue;
                    if (EntityManager.HasComponent<Game.Buildings.Building>(c) || EntityManager.HasComponent<Game.Buildings.ServiceUpgrade>(c))
                        continue;
                    if (!EntityManager.TryGetComponent(c, out PrefabRef cp))
                        continue;
                    Entity key = m_Rows.ContainsKey(cp.m_Prefab) ? cp.m_Prefab : m_VariantOf.TryGetValue(cp.m_Prefab, out Entity ph) ? ph : Entity.Null;
                    if (key != Entity.Null)
                        m_Placed[key] = (m_Placed.TryGetValue(key, out int k) ? k : 0) + 1;
                }
            }
            int missing = 0;
            foreach (KeyValuePair<Entity, int> kv in m_Rows)
            {
                int placed = m_Placed.TryGetValue(kv.Key, out int k) ? k : 0;
                int miss = SkiSyncCatalog.Missing(kv.Value, placed);
                extra += SkiSyncCatalog.Extra(kv.Value, placed);
                if (miss == 0)
                    continue;
                missing += miss;
                detail?.Append(detail.Length > 0 ? ", " : string.Empty).Append(m_PrefabSystem.GetPrefabName(kv.Key)).Append(" x").Append(miss);
            }
            return missing;
        }

        // ---- run -----------------------------------------------------------------------------------------------------------------

        /// <summary>Queues the included Open (and Failed) items of <paramref name="sign"/>'s area (the hub decides the area). Returns how many.</summary>
        internal int RequestSync(Func<Entity, bool> inArea)
        {
            int n = 0;
            for (int i = 0; i < m_Items.Count; i++)
            {
                Item it = m_Items[i];
                if (!it.Include || (it.State != ItemState.Open && it.State != ItemState.Failed) || !inArea(it.Entity))
                    continue;
                it.State = ItemState.Queued;
                m_Items[i] = it;
                m_Queue.Add(it.Entity);
                n++;
            }
            if (n > 0)
            {
                m_RunDone = m_RunFailed = 0;
                m_RunTotal = n;
                m_Revision++;
                Mod.Log.Info($"Sync [run]: {n} object(s) queued ({SkiSyncCatalog.ApplyPerFrame} per frame; held while a tool is active)");
            }
            return n;
        }

        /// <summary>"Keep as is": stamps the object Current with the Kept bit and drops it from the list.</summary>
        internal bool Keep(Entity e)
        {
            int i = IndexOf(e);
            if (i < 0 || m_Items[i].State == ItemState.Queued || m_Items[i].State == ItemState.Working)
                return false;
            if (EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e))
                SkiRulesStampSystem.StampCurrent(EntityManager, e, SkiRulesLevel.FlagKept);
            m_Items.RemoveAt(i);
            m_Revision++;
            Mod.Log.Info($"Sync [keep]: {Ent(e)} {Name(e)} kept as is (stamped rules level {SkiRulesLevel.Current}, Kept)");
            return true;
        }

        /// <summary>The card's per-item check box.</summary>
        internal bool SetInclude(Entity e, bool include)
        {
            int i = IndexOf(e);
            if (i < 0 || m_Items[i].Include == include)
                return false;
            Item it = m_Items[i];
            it.Include = include;
            m_Items[i] = it;
            m_Revision++;
            return true;
        }

        private void RunQueue()
        {
            if (m_Queue.Count == 0)
                return;
            ToolBaseSystem tool = m_ToolSystem.activeTool;
            if (tool != null && !(tool is DefaultToolSystem))
            {
                if (m_HoldLogged++ == 0)
                    Mod.Log.Info($"Sync [run]: held while the {tool.toolID ?? tool.GetType().Name} tool is active ({m_Queue.Count} queued)");
                return;
            }
            m_HoldLogged = 0;
            int take = SkiSyncCatalog.Take(m_Queue.Count, SkiSyncCatalog.ApplyPerFrame);
            for (int q = 0; q < take; q++)
            {
                Entity e = m_Queue[0];
                m_Queue.RemoveAt(0);
                int i = IndexOf(e);
                if (i < 0)
                    continue;
                StartStep(i);
            }
            m_Revision++;
        }

        private void StartStep(int i)
        {
            Item it = m_Items[i];
            if (!EntityManager.Exists(it.Entity) || EntityManager.HasComponent<Deleted>(it.Entity))
            {
                m_Items.RemoveAt(i);   // bulldozed meanwhile
                return;
            }
            if (!Apply(it.Step, it.Entity))
            {
                Fail(i, "the step could not start");
                return;
            }
            it.State = ItemState.Working;
            it.VerifyAt = m_Frame + SkiSyncCatalog.VerifyFrames;
            m_Items[i] = it;
        }

        private void CheckDue()
        {
            for (int i = m_Items.Count - 1; i >= 0; i--)
            {
                Item it = m_Items[i];
                if (it.State != ItemState.Working || m_Frame < it.VerifyAt)
                    continue;
                if (!EntityManager.Exists(it.Entity) || EntityManager.HasComponent<Deleted>(it.Entity))
                {
                    m_Items.RemoveAt(i);
                    m_Revision++;
                    continue;
                }
                if (Needs(it.Step, it.Entity))
                {
                    var detail = new StringBuilder();
                    MissingParts(it.Entity, detail);
                    Fail(i, $"step {it.Step} still needed after its action ({(detail.Length > 0 ? detail.ToString() : "no detail")})");
                    continue;
                }
                // The step is done: the next needed step runs, or the object is current
                int level = it.Step;
                SyncVerdict next = SkiSyncCatalog.Decide(level, Flags(it.Entity), it.Kind, n => Needs(n, it.Entity), out int step);
                if (next == SyncVerdict.Auto || next == SyncVerdict.Offer)
                {
                    it.Step = step;
                    it.State = ItemState.Queued;
                    m_Items[i] = it;
                    m_Queue.Add(it.Entity);
                    m_Revision++;
                    continue;
                }
                CountParts(it.Entity, null, out int extra);
                m_RunDone++;
                m_Revision++;
                if (next == SyncVerdict.HandEdited)
                {
                    // A later step skips lines edited by hand: the object is current up to this step and stays listed as skipped
                    SkiRulesStampSystem.StampLevel(EntityManager, it.Entity, it.Step, 0);
                    it.Step = step;
                    it.State = ItemState.HandEdited;
                    it.Include = false;
                    m_Items[i] = it;
                }
                else
                {
                    SkiRulesStampSystem.StampCurrent(EntityManager, it.Entity, 0);
                    m_Items.RemoveAt(i);
                }
                Mod.Log.Info($"Sync [done]: {Ent(it.Entity)} {Name(it.Entity)} step {level} done, now at rules level " +
                             $"{(next == SyncVerdict.HandEdited ? level : SkiRulesLevel.Current)} (parts beyond the prefab's rows: {extra})" +
                             (next == SyncVerdict.HandEdited ? $"; step {step} skips it (edited by hand)" : string.Empty));
            }
        }

        private void Fail(int i, string why)
        {
            Item it = m_Items[i];
            it.State = ItemState.Failed;
            m_Items[i] = it;
            m_RunFailed++;
            m_Revision++;
            Mod.Log.Warn($"Sync [failed]: {Ent(it.Entity)} {Name(it.Entity)} — {why}. It keeps its rules level; no retry until the next load " +
                         "(Sync again tries once more)");
        }

        // ---- dev read: does an asset change reach placed buildings? ---------------------------------------------------------------

        /// <summary>One line per load: every building of ours whose placed parts miss rows of its prefab, grouped by building prefab.</summary>
        private void LogAssets()
        {
            if (m_Buildings.IsEmptyIgnoreFilter)
            {
                Mod.Log.Info("Sync [assets]: no buildings");
                return;
            }
            var byPrefab = new SortedDictionary<string, int[]>(StringComparer.Ordinal);   // name -> {checked, missing}
            var details = new SortedDictionary<string, string>(StringComparer.Ordinal);
            int checkedCount = 0, missingCount = 0;
            using (NativeArray<Entity> entities = m_Buildings.ToEntityArray(Allocator.Temp))
            {
                var detail = new StringBuilder();
                for (int i = 0; i < entities.Length; i++)
                {
                    int kind = m_Stamp.KindOfEntity(entities[i]);
                    if (kind < 0)
                        continue;
                    checkedCount++;
                    string name = PrefabName(entities[i]);
                    if (!byPrefab.TryGetValue(name, out int[] c))
                        byPrefab[name] = c = new int[2];
                    c[0]++;
                    detail.Clear();
                    if (MissingParts(entities[i], detail) == 0)
                        continue;
                    c[1]++;
                    missingCount++;
                    details[name] = detail.ToString();
                }
            }
            if (missingCount == 0)
            {
                Mod.Log.Info($"Sync [assets]: {checkedCount} building(s) of ours checked; every placed building has a part for each row of its prefab");
                return;
            }
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, int[]> kv in byPrefab)
            {
                if (kv.Value[1] == 0)
                    continue;
                sb.Append(sb.Length > 0 ? "; " : string.Empty).Append(kv.Key).Append(": ").Append(kv.Value[1]).Append(" of ")
                  .Append(kv.Value[0]).Append(" lack ").Append(details[kv.Key]);
            }
            Mod.Log.Info($"Sync [assets]: {checkedCount} building(s) of ours checked; {missingCount} lack a part their prefab has — {sb}. " +
                         "(A load keeps the saved parts only: a row added to the prefab later is not placed until the parts are rebuilt.)");
        }

        // ---- helpers -------------------------------------------------------------------------------------------------------------

        private int IndexOf(Entity e)
        {
            for (int i = 0; i < m_Items.Count; i++)
                if (m_Items[i].Entity == e)
                    return i;
            return -1;
        }

        private byte Flags(Entity e) => EntityManager.TryGetComponent(e, out SkiRulesStamp s) ? s.m_Flags : (byte)0;

        private string PrefabName(Entity e) =>
            EntityManager.TryGetComponent(e, out PrefabRef pr) ? m_PrefabSystem.GetPrefabName(pr.m_Prefab) : "?";

        private string Name(Entity e) => PrefabName(e);

        private static string Ent(Entity e) => e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);

        /// <summary>World position of an object for the card's markers (Transform for buildings). False when it has none.</summary>
        internal bool TryGetPosition(Entity e, out float3 position)
        {
            if (EntityManager.TryGetComponent(e, out Game.Objects.Transform t))
            {
                position = t.m_Position;
                return true;
            }
            position = default;
            return false;
        }
    }
}
