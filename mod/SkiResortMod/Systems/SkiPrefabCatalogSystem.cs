// ECS side of the prefab catalog: resolves every name of the pure SkiCatalog.cs to its prefab entity through one typed
// resolver, replacing the per-system copy-pasted resolvers. Pending-name severity never changes behavior.
// Per-load lifetime: OnGamePreload (Game / Editor) invalidates, the first catalog query resolves lazily, every query revalidates
// the cached entities at most once per frame. Live views are one instance each for the app run: fetch a view once, before the
// chunk loop; TryGetValue / Contains inside it, never a per-entity member call.
// Log discipline: silence must be impossible (ChairPinSystem header); after an exception every query answers MISSING until the
// next load.
// Engine notes: EngineNotes/SkiPrefabCatalogSystem.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiPrefabCatalogSystem : GameSystemBase
    {
        /// <summary>"[re-resolve]" WARN lines per load; the rest are counted in Describe().</summary>
        internal const int ReResolveLinesMax = 8;

        /// <summary>|bounds.max.y − HangM| above this is a carrier contract drift (one WARN per carrier per load).</summary>
        internal const float HangToleranceM = 0.01f;

        private const string ResolverText =
            "resolver: PrefabID fast path, then one PrefabData scan (Deleted and obsolete excluded, PrefabID type preferred), " +
            "per load, revalidated at the first query of each frame";

        /// <summary>One (type, name) the resolver looks up: a catalog name (AllPrefabNames order) or an extra.</summary>
        private sealed class NameSlot
        {
            public string Name;
            public string TypeName;
            public CatalogKind Kind;
            public bool IsExtra;
            public bool Expected;          // the NAME's flag (extras: true)
            // result of the last resolution
            public Entity Found;           // Entity.Null = MISSING
            public PrefabBase Prefab;
            public string How;             // "id", "scan", "scan, type X ≠ Y"
            public bool TypeMismatch;
            public float GripTop;          // carriers: bounds.max.y, 0 when no usable bounds
            public bool NoBounds;          // carriers: FOUND but not usable
            public bool HangDrift;         // carriers: |GripTop − HangM| > HangToleranceM
            // scan scratch: the first candidate of any type
            public Entity Alt;
            public PrefabBase AltPrefab;
            public string AltType;

            public void Clear()
            {
                Found = Entity.Null;
                Prefab = null;
                How = null;
                TypeMismatch = false;
                GripTop = 0f;
                NoBounds = false;
                HangDrift = false;
                Alt = Entity.Null;
                AltPrefab = null;
                AltType = null;
            }
        }

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_ScanQuery;

        private readonly List<NameSlot> m_Slots = new List<NameSlot>(32);   // catalog names first, then extras
        private int m_CatalogSlots;
        private readonly Dictionary<string, int> m_SlotByName = new Dictionary<string, int>(StringComparer.Ordinal);  // catalog names
        private readonly Dictionary<string, int> m_ExtraByKey = new Dictionary<string, int>(StringComparer.Ordinal);  // "Type/Name"
        private readonly List<int> m_Gone = new List<int>(4);       // FindGone's slot indices (reused)
        private readonly List<int> m_AllSlots = new List<int>(32);  // 0..n-1 for a full resolution (reused)

        private readonly HashSet<Entity> m_LiftLines = new HashSet<Entity>();
        private readonly Dictionary<Entity, LiftTypeDef> m_LineTypes = new Dictionary<Entity, LiftTypeDef>();
        private readonly Dictionary<Entity, float> m_CarrierGripTops = new Dictionary<Entity, float>();
        private readonly Dictionary<Entity, LiftTypeDef> m_CarrierTypes = new Dictionary<Entity, LiftTypeDef>();
        private readonly HashSet<Entity> m_Stations = new HashSet<Entity>();
        private readonly Dictionary<Entity, StationDef> m_StationDefs = new Dictionary<Entity, StationDef>();
        private readonly HashSet<Entity> m_Lodges = new HashSet<Entity>();
        private readonly Dictionary<Entity, LodgeDef> m_LodgeDefs = new Dictionary<Entity, LodgeDef>();
        private readonly HashSet<Entity> m_Schools = new HashSet<Entity>();
        private readonly Dictionary<Entity, SchoolDef> m_SchoolDefs = new Dictionary<Entity, SchoolDef>();
        private readonly HashSet<Entity> m_Patrols = new HashSet<Entity>();
        private readonly Dictionary<Entity, PatrolDef> m_PatrolDefs = new Dictionary<Entity, PatrolDef>();
        private readonly HashSet<Entity> m_Signs = new HashSet<Entity>();
        private Entity m_Marker;

        private bool m_Dirty;                    // false until the first Game / Editor preload
        private bool m_FailedThisLoad;           // the last resolution threw: serve MISSING until the next load
        private bool m_ResolvedSinceLoad;        // a resolution ran since the last Game / Editor preload
        private bool m_IsResolved;               // resolved since the last invalidation, without an exception
        private int m_ValidatedFrame = -1;
        private bool m_ErrorLogged;              // one "!!! PrefabCatalog EXCEPTION" per load
        private int m_Generation;
        private int m_Loads;
        private int m_ReResolveLines;            // this load
        private int m_ReResolvesThisLoad;
        private int m_ReResolveSuppressed;       // this load
        private int m_ReResolvesTotal;           // this app run
        private long m_Validations;              // this app run
        private string m_LastText = "not resolved (no game or editor load yet)";

        private int m_ExpectedMissing;           // last resolution: MISSING names that are Expected
        private int m_PendingFound;              // last resolution: FOUND names that are pending
        private bool m_PendingFoundLogged;       // this load
        private readonly HashSet<string> m_NoBoundsWarned = new HashSet<string>(StringComparer.Ordinal);   // this load
        private readonly HashSet<string> m_HangWarned = new HashSet<string>(StringComparer.Ordinal);       // this load

        /// <summary>++ per resolution; consumers that cache derived state compare it.</summary>
        internal int Generation
        {
            get { EnsureFresh(); return m_Generation; }
        }

        /// <summary>++ per OnGamePreload whose mode is Game or Editor; printed "load N".</summary>
        internal int Loads
        {
            get { EnsureFresh(); return m_Loads; }
        }

        /// <summary>Resolved since the last invalidation, without an exception.</summary>
        internal bool IsResolved
        {
            get { EnsureFresh(); return m_IsResolved; }
        }

        /// <summary>Last resolution: MISSING names whose flag is Expected (0 in a healthy install).</summary>
        internal int ExpectedMissing
        {
            get { EnsureFresh(); return m_ExpectedMissing; }
        }

        /// <summary>Last resolution: FOUND names whose flag is pending (> 0 = an import happened; flip the names).</summary>
        internal int PendingFound
        {
            get { EnsureFresh(); return m_PendingFound; }
        }

        /// <summary>LIVE VIEW: every FOUND lift line prefab entity (an explicit whitelist; the marker is not in it).</summary>
        internal HashSet<Entity> LiftLinePrefabSet
        {
            get { EnsureFresh(); return m_LiftLines; }
        }

        /// <summary>LIVE VIEW: FOUND lift line prefab entity → its lift type row (LiftCensus Edge.Type,
        /// ChairPin's lane harvest). Fetch once before a chunk loop, TryGetValue inside.</summary>
        internal IReadOnlyDictionary<Entity, LiftTypeDef> LiftLineTypes
        {
            get { EnsureFresh(); return m_LineTypes; }
        }

        /// <summary>The lift type's line prefab entity; Entity.Null when MISSING.</summary>
        internal Entity LineEntity(LiftTypeDef def)
        {
            EnsureFresh();
            return def != null ? FoundOf(def.LinePrefab) : Entity.Null;
        }

        /// <summary>The lift type's line PrefabBase; null when MISSING (the infoview's reference compare).</summary>
        internal PrefabBase LinePrefabBase(LiftTypeDef def)
        {
            EnsureFresh();
            return def != null ? PrefabOf(def.LinePrefab) : null;
        }

        /// <summary>LIVE VIEW: carrier prefab entity → bounds.max.y (pivot to grip top), &gt; 0 only. Use TryGetValue.</summary>
        internal IReadOnlyDictionary<Entity, float> CarrierGripTops
        {
            get { EnsureFresh(); return m_CarrierGripTops; }
        }

        /// <summary>LIVE VIEW: carrier prefab entity (usable bounds only, the CarrierGripTops keys) → its lift
        /// type row (ChairPin's Shuttle filter and per-type ceiling).</summary>
        internal IReadOnlyDictionary<Entity, LiftTypeDef> CarrierTypes
        {
            get { EnsureFresh(); return m_CarrierTypes; }
        }

        /// <summary>The lift marker (stub) PowerLinePrefab entity; Entity.Null when MISSING.</summary>
        internal Entity MarkerEntity
        {
            get { EnsureFresh(); return m_Marker; }
        }

        internal bool TryGetStation(Entity stationPrefab, out StationDef def)
        {
            EnsureFresh();
            return m_StationDefs.TryGetValue(stationPrefab, out def);
        }

        internal bool IsStation(Entity prefab)
        {
            EnsureFresh();
            return m_Stations.Contains(prefab);
        }

        /// <summary>LIVE VIEW: every FOUND station prefab entity. PowerBridgeSystem.GetResolvedStationPrefabs returns this
        /// instance (LiftReconnectSystem.cs:222 reads it each frame).</summary>
        internal HashSet<Entity> StationPrefabSet
        {
            get { EnsureFresh(); return m_Stations; }
        }

        /// <summary>LIVE VIEW: station prefab entity → its StationDef. Fetch once before a loop over every
        /// building and use TryGetValue inside (instead of a TryGetStation call per entity).</summary>
        internal IReadOnlyDictionary<Entity, StationDef> StationDefs
        {
            get { EnsureFresh(); return m_StationDefs; }
        }

        internal bool IsLodge(Entity prefab)
        {
            EnsureFresh();
            return m_Lodges.Contains(prefab);
        }

        /// <summary>LIVE VIEW: every FOUND lodge prefab entity.</summary>
        internal HashSet<Entity> LodgePrefabSet
        {
            get { EnsureFresh(); return m_Lodges; }
        }

        /// <summary>LIVE VIEW: FOUND lodge prefab entity → its ladder row (the tier IS the PrefabRef).</summary>
        internal IReadOnlyDictionary<Entity, LodgeDef> LodgeDefs
        {
            get { EnsureFresh(); return m_LodgeDefs; }
        }

        /// <summary>FOUND lodge prefabs (0 = no lodge prefab present this load).</summary>
        internal int LodgeCount
        {
            get { EnsureFresh(); return m_LodgeDefs.Count; }
        }

        /// <summary>LIVE VIEW: every FOUND ski-school prefab entity.</summary>
        internal HashSet<Entity> SchoolPrefabSet
        {
            get { EnsureFresh(); return m_Schools; }
        }

        /// <summary>LIVE VIEW: FOUND school prefab entity → its row.</summary>
        internal IReadOnlyDictionary<Entity, SchoolDef> SchoolDefs
        {
            get { EnsureFresh(); return m_SchoolDefs; }
        }

        internal bool TryGetSchool(Entity prefab, out SchoolDef def)
        {
            EnsureFresh();
            return m_SchoolDefs.TryGetValue(prefab, out def);
        }

        /// <summary>LIVE VIEW: every FOUND patrol hut prefab entity.</summary>
        internal HashSet<Entity> PatrolPrefabSet
        {
            get { EnsureFresh(); return m_Patrols; }
        }

        /// <summary>LIVE VIEW: FOUND patrol prefab entity → its row.</summary>
        internal IReadOnlyDictionary<Entity, PatrolDef> PatrolDefs
        {
            get { EnsureFresh(); return m_PatrolDefs; }
        }

        /// <summary>A FOUND lodge, school or patrol prefab: every ServiceUpgrade of the sign the catalog knows. For
        /// event-time callers; a loop over every building fetches the three sets once instead.</summary>
        internal bool IsUpgradeBuilding(Entity prefab)
        {
            EnsureFresh();
            return m_Lodges.Contains(prefab) || m_Schools.Contains(prefab) || m_Patrols.Contains(prefab);
        }

        internal bool IsSign(Entity prefab)
        {
            EnsureFresh();
            return m_Signs.Contains(prefab);
        }

        /// <summary>Any NAMES entry; Entity.Null when MISSING or not a catalog name.</summary>
        internal Entity EntityOf(string catalogName)
        {
            EnsureFresh();
            return FoundOf(catalogName);
        }

        /// <summary>The hub's item lookups: same resolver, same per-load cache. A catalog name asked with its own
        /// PrefabID type is served from the catalog; any other (type, name) is an "extra": resolved on first ask with the
        /// same resolver, cached per load, re-resolved with the catalog on every full resolution (and alone when gone) and
        /// validated with it each frame, and listed in Describe().</summary>
        internal bool TryGetPrefab(string typeName, string name, out Entity entity, out PrefabBase prefab)
        {
            EnsureFresh();
            entity = Entity.Null;
            prefab = null;
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(name))
                return false;
            NameSlot slot;
            if (m_SlotByName.TryGetValue(name, out int i) && m_Slots[i].TypeName == typeName)
                slot = m_Slots[i];
            else
                slot = ExtraSlot(typeName, name);
            if (slot == null || slot.Found == Entity.Null)
                return false;
            entity = slot.Found;
            prefab = slot.Prefab;
            return true;
        }

        /// <summary>The last ARM / re-resolve text plus the counters (DUMPs, developer tabs).</summary>
        internal string Describe()
        {
            EnsureFresh();
            string state;
            if (m_Loads == 0 && m_Generation == 0)
                state = "not resolved (no game or editor load yet)";
            else if (m_FailedThisLoad)
                state = "FAILED this load (exception: every name reads MISSING until the next load)";
            else if (m_IsResolved)
                state = "resolved";
            else
                state = "invalidated (resolves at the next query)";
            return "PrefabCatalog: " + state +
                   "; load " + I(m_Loads) + ", generation " + I(m_Generation) +
                   "; expected MISSING " + I(m_ExpectedMissing) + ", pending FOUND " + I(m_PendingFound) +
                   "; validated frame " + I(m_ValidatedFrame) + ", " + m_Validations.ToString(CultureInfo.InvariantCulture) +
                   " validation(s) this app run; re-resolves " + I(m_ReResolvesThisLoad) + " this load (" +
                   I(m_ReResolveSuppressed) + " line(s) suppressed), " + I(m_ReResolvesTotal) + " this app run; extras " +
                   I(m_Slots.Count - m_CatalogSlots) + "; last: " + m_LastText;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ScanQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabData>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            Enabled = false;   // no per-frame work; the load hooks still fire (GameSystemBase.cs:27-29)

            IReadOnlyList<CatalogName> names = SkiCatalog.AllPrefabNames();
            for (int i = 0; i < names.Count; i++)
            {
                CatalogName n = names[i];
                var slot = new NameSlot { Name = n.Name, TypeName = n.TypeName, Kind = n.Kind, IsExtra = false, Expected = n.Expected };
                slot.Clear();
                m_Slots.Add(slot);
                if (!string.IsNullOrEmpty(n.Name) && !m_SlotByName.ContainsKey(n.Name))
                    m_SlotByName.Add(n.Name, i);
            }
            m_CatalogSlots = m_Slots.Count;

            // Pure file, no Game reference: the four PrefabID type names are asserted against the Game types here.
            AssertType("TypeLine", SkiCatalog.TypeLine, nameof(PowerLinePrefab));
            AssertType("TypeCarrier", SkiCatalog.TypeCarrier, nameof(StaticObjectPrefab));
            AssertType("TypeBuilding", SkiCatalog.TypeBuilding, nameof(BuildingPrefab));
            AssertType("TypeSurface", SkiCatalog.TypeSurface, nameof(SurfacePrefab));
            IReadOnlyList<string> problems = SkiCatalog.Problems;
            for (int i = 0; i < problems.Count; i++)
                Mod.Log.Error("!!! PrefabCatalog: SkiCatalog problem — " + problems[i] +
                              " (fix Systems/SkiCatalog.cs; tests/LiftBatch CatalogTests CT-1 fails on it)");

            Mod.Log.Info($"{nameof(SkiPrefabCatalogSystem)} created ({I(names.Count)} names: " +
                         Plural(SkiCatalog.Lifts.Count, "lift type", "lift types") + ", " +
                         Plural(SkiCatalog.Stations.Count, "station", "stations") + ", " +
                         Plural(SkiCatalog.Lodges.Count, "lodge", "lodges") + ", " +
                         Plural(SkiCatalog.Schools.Count, "school", "schools") + ", " +
                         Plural(SkiCatalog.Patrols.Count, "patrol hut", "patrol huts") + ", " +
                         Plural(SkiCatalog.Signs.Count, "sign", "signs") + ", " +
                         Plural(SkiCatalog.Runs.Count, "run", "runs") + ", the marker; " +
                         I(SkiCatalog.ExpectedNameCount) + " expected, " + I(SkiCatalog.PendingNameCount) + " pending; " +
                         Plural(SkiCatalog.BuildItems.Count, "build item", "build items") + "; resolved lazily per load)");
        }

        /// <summary>Game / Editor only: invalidate; the first query after it resolves. A MainMenu preload keeps the
        /// last load's views. Never throws: GameSystemBase.GamePreload DISABLES a system whose OnGamePreload throws
        /// (GameSystemBase.cs:85-96).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                if (mode != GameMode.Game && mode != GameMode.Editor)
                    return;
                m_Dirty = true;
                m_FailedThisLoad = false;
                m_ResolvedSinceLoad = false;
                m_IsResolved = false;
                m_ReResolveLines = 0;
                m_ReResolvesThisLoad = 0;
                m_ReResolveSuppressed = 0;
                m_ErrorLogged = false;
                m_PendingFoundLogged = false;
                m_NoBoundsWarned.Clear();
                m_HangWarned.Clear();
                m_Loads++;
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"PrefabCatalog: preload bookkeeping failed ({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        /// <summary>Exactly one ARM line per Game / Editor load, even when no consumer asked: resolve here if nothing has
        /// since this load's preload.</summary>
        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;
            try
            {
                if (!m_ResolvedSinceLoad)
                {
                    m_Dirty = true;   // normally already set by this load's preload
                    EnsureFresh();
                }
            }
            catch (Exception e)
            {
                Mod.Log.Error("!!! PrefabCatalog EXCEPTION at OnGameLoadingComplete — every catalog prefab may read as " +
                              "MISSING this load; each consumer logs its own missing line:\n" + e);
            }
        }

        protected override void OnUpdate()
        {
            // Never runs (Enabled = false): the catalog is pull-based.
        }

        /// <summary>Every member's first call. Serve: return. Validate: Exists && !Deleted for every FOUND entity, once
        /// per frame; a gone one re-resolves its slot. Resolve: the load's resolution (the ARM line).</summary>
        private void EnsureFresh()
        {
            int frame = UnityEngine.Time.frameCount;
            CatalogAction action = SkiCatalog.NextAction(m_Dirty, m_FailedThisLoad, m_ValidatedFrame, frame);
            if (action == CatalogAction.Serve)
                return;

            //cleared BEFORE the work, whatever its outcome — a throwing resolution must not re-run on every call.
            m_Dirty = false;
            m_ValidatedFrame = frame;
            try
            {
                string why = null;
                if (action == CatalogAction.Validate)
                {
                    m_Validations++;
                    why = FindGone();
                    if (why == null)
                        return;   // every cached prefab entity is live
                }
                m_ResolvedSinceLoad = true;
                m_Generation++;
                Resolve(why);   // why == null: the load's resolution ([arm]); else a [re-resolve] of m_Gone
            }
            catch (Exception e)
            {
                ClearAll();
                m_IsResolved = false;
                m_FailedThisLoad = true;
                m_LastText = "FAILED at generation " + I(m_Generation) + ": " + e.GetType().Name + ": " + e.Message;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error("!!! PrefabCatalog EXCEPTION — every catalog prefab reads as MISSING until the next load; " +
                                  "each consumer logs its own missing line:\n" + e);
                }
            }
        }

        /// <summary>null when every FOUND entity is live; else why the cache is stale (allocates only then). The gone
        /// slots' indices land in m_Gone.</summary>
        private string FindGone()
        {
            m_Gone.Clear();
            string why = null;
            for (int i = 0; i < m_Slots.Count; i++)
            {
                NameSlot s = m_Slots[i];
                Entity e = s.Found;
                if (e == Entity.Null)
                    continue;
                bool exists = EntityManager.Exists(e);
                if (exists && !EntityManager.HasComponent<Deleted>(e))
                    continue;
                m_Gone.Add(i);
                if (why == null)
                    why = "'" + s.Name + "' " + Ref(e) + (exists ? " replaced (Deleted)" : " gone (entity destroyed)");
            }
            if (m_Gone.Count > 1)
                why += " (+" + I(m_Gone.Count - 1) + " more)";
            return why;
        }

        private void Resolve(string why)
        {
            var sw = Stopwatch.StartNew();
            int fast, scanFor, scanned, slots;
            if (why == null)
            {
                ClearAll();
                m_AllSlots.Clear();
                for (int i = 0; i < m_Slots.Count; i++)
                    m_AllSlots.Add(i);
                ResolveSlots(m_AllSlots, out fast, out scanFor, out scanned);
                slots = m_Slots.Count;
            }
            else
            {
                // Only the gone slots, fast path first; the views are rebuilt from every slot
                ClearViews();
                ResolveSlots(m_Gone, out fast, out scanFor, out scanned);
                slots = m_Gone.Count;
            }
            bool carrierWarn = BuildViews();
            sw.Stop();
            m_IsResolved = true;

            var sb = new StringBuilder(2048);
            bool warn = AppendSlots(sb) || carrierWarn;
            string timing = "; took " + sw.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms (" +
                            (why != null ? "re-resolved " + I(slots) + " of " + I(m_Slots.Count) + " slot(s); " : "") +
                            "PrefabID fast path " + I(fast) + "/" + I(fast + scanFor) +
                            (scanFor > 0 ? ", one scan over " + I(scanned) + " prefab entities for " + I(scanFor) + ")" : ", no scan)");

            if (why == null)
            {
                sb.Insert(0, "PrefabCatalog [arm] load " + I(m_Loads) + ", generation " + I(m_Generation) + ": ");
                AppendLiftTypes(sb);
                sb.Append("; ").Append(ResolverText).Append(timing);
                m_LastText = sb.ToString();
                if (warn)
                    Mod.Log.Warn(m_LastText);
                else
                    Mod.Log.Info(m_LastText);
                LogPendingFound();
                return;
            }

            m_ReResolvesThisLoad++;
            m_ReResolvesTotal++;
            sb.Insert(0, "PrefabCatalog [re-resolve] (" + why + ") load " + I(m_Loads) + ", generation " + I(m_Generation) + ": ");
            sb.Append(timing);
            m_LastText = sb.ToString();
            if (m_ReResolveLines < ReResolveLinesMax)
            {
                m_ReResolveLines++;
                Mod.Log.Warn(m_ReResolveLines == ReResolveLinesMax
                    ? m_LastText + " — further re-resolve lines this load are counted in Describe() only"
                    : m_LastText);
            }
            else
            {
                m_ReResolveSuppressed++;
            }
            LogPendingFound();
        }

        /// <summary>one WARN per load naming every FOUND pending name (the rows work either way).</summary>
        private void LogPendingFound()
        {
            if (m_PendingFound == 0 || m_PendingFoundLogged)
                return;
            m_PendingFoundLogged = true;
            var sb = new StringBuilder(256);
            int n = 0;
            for (int i = 0; i < m_CatalogSlots; i++)
            {
                NameSlot s = m_Slots[i];
                if (s.Expected || s.Found == Entity.Null)
                    continue;
                sb.Append(n++ == 0 ? "" : ", ").Append('\'').Append(s.Name).Append("' (").Append(s.TypeName).Append(") ").Append(Ref(s.Found));
            }
            Mod.Log.Warn("PrefabCatalog [pending-found] " + I(n) + " pending name(s) exist: " + sb +
                         " — its SkiCatalog name is pending: set Expected = true and drop \", pending\" in Systems/SkiCatalog.cs " +
                         "for each (the rows work either way)");
        }

        /// <summary>Resolves the listed slots: the PrefabID fast path per slot, then ONE scan shared by every miss.</summary>
        private void ResolveSlots(List<int> indices, out int fast, out int scanFor, out int scanned)
        {
            fast = scanFor = scanned = 0;
            Dictionary<string, List<int>> wanted = null;
            for (int k = 0; k < indices.Count; k++)
            {
                int i = indices[k];
                NameSlot s = m_Slots[i];
                s.Clear();
                if (string.IsNullOrEmpty(s.Name) || string.IsNullOrEmpty(s.TypeName))
                    continue;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID(s.TypeName, s.Name), out PrefabBase byId) && byId != null &&
                    m_PrefabSystem.TryGetEntity(byId, out Entity e) && IsLive(e))
                {
                    s.Found = e;
                    s.Prefab = byId;
                    s.How = "id";
                    fast++;
                    continue;
                }
                if (wanted == null)
                    wanted = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                if (!wanted.TryGetValue(s.Name, out List<int> list))
                {
                    list = new List<int>(1);
                    wanted.Add(s.Name, list);
                }
                list.Add(i);
                scanFor++;
            }
            if (wanted == null)
                return;

            int typedOpen = scanFor;
            NativeArray<Entity> candidates = m_ScanQuery.ToEntityArray(Allocator.Temp);
            try
            {
                scanned = candidates.Length;
                for (int c = 0; c < candidates.Length && typedOpen > 0; c++)
                {
                    Entity e = candidates[c];
                    // PrefabData.m_Index >= 0 only: an obsolete placeholder never matches (PrefabSystem.cs:658-677, :979-990).
                    if (!m_PrefabSystem.TryGetPrefab(e, out PrefabBase pb) || pb == null)
                        continue;
                    string n = pb.name;
                    if (n == null || !wanted.TryGetValue(n, out List<int> slots))
                        continue;
                    string tn = pb.GetType().Name;
                    for (int k = 0; k < slots.Count; k++)
                    {
                        NameSlot s = m_Slots[slots[k]];
                        if (s.Found != Entity.Null)
                            continue;   // already has its typed hit
                        if (tn == s.TypeName)
                        {
                            s.Found = e;
                            s.Prefab = pb;
                            s.How = "scan";
                            typedOpen--;
                        }
                        else if (s.Alt == Entity.Null)
                        {
                            s.Alt = e;
                            s.AltPrefab = pb;
                            s.AltType = tn;
                        }
                    }
                }
            }
            finally
            {
                candidates.Dispose();
            }

            // No candidate of the PrefabID type: the first candidate of any type (still FOUND, the ARM line turns WARN).
            for (int k = 0; k < indices.Count; k++)
            {
                NameSlot s = m_Slots[indices[k]];
                if (s.Found == Entity.Null && s.Alt != Entity.Null)
                {
                    s.Found = s.Alt;
                    s.Prefab = s.AltPrefab;
                    s.How = "scan, type " + s.AltType + " ≠ " + s.TypeName;
                    s.TypeMismatch = true;
                }
                s.Alt = Entity.Null;
                s.AltPrefab = null;
                s.AltType = null;
            }
        }

        /// <summary>Fills the live views from the catalog slots. True when a FOUND carrier had no usable bounds or its
        /// grip-top differs from its row's HangM.</summary>
        private bool BuildViews()
        {
            bool carrierWarn = false;
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            for (int l = 0; l < lifts.Count; l++)
            {
                LiftTypeDef def = lifts[l];
                Entity line = FoundOf(def.LinePrefab);
                if (line != Entity.Null)
                {
                    m_LiftLines.Add(line);
                    if (!m_LineTypes.ContainsKey(line))
                        m_LineTypes.Add(line, def);
                }
                for (int c = 0; c < def.Carriers.Count; c++)
                {
                    CarrierDef carrier = def.Carriers[c];
                    NameSlot cs = SlotOf(carrier.Prefab);
                    if (cs == null || cs.Found == Entity.Null)
                        continue;
                    cs.GripTop = 0f;
                    cs.NoBounds = false;
                    cs.HangDrift = false;
                    // bounds.max.y = pivot → grip top (grip is the topmost geometry, pivot at the lowest point: the Blender contract,
                    // PIPELINE §1). Read at runtime so future carrier meshes re-derive automatically.
                    if (EntityManager.TryGetComponent(cs.Found, out ObjectGeometryData geometry) && geometry.m_Bounds.max.y > 0f)
                    {
                        cs.GripTop = geometry.m_Bounds.max.y;
                        if (!m_CarrierGripTops.ContainsKey(cs.Found))
                        {
                            m_CarrierGripTops.Add(cs.Found, cs.GripTop);
                            m_CarrierTypes.Add(cs.Found, def);
                        }
                        //the row is the contract (catalog/carriers.json hang_m), the runtime bounds stay the pin source.
                        if (math.abs(cs.GripTop - carrier.HangM) > HangToleranceM)
                        {
                            cs.HangDrift = true;
                            carrierWarn = true;
                            if (m_HangWarned.Add(cs.Name))
                                Mod.Log.Warn($"PrefabCatalog: carrier '{cs.Name}' grip-top {F(cs.GripTop, "0.000")} m differs from " +
                                             $"SkiCatalog HangM {F(carrier.HangM, "0.000")} (catalog/carriers.json row of lift type " +
                                             $"'{def.Id}') — it pins by its bounds; fix the mesh or the contract");
                        }
                    }
                    else
                    {
                        cs.NoBounds = true;
                        carrierWarn = true;
                        if (m_NoBoundsWarned.Add(cs.Name))   //once per carrier per load
                            Mod.Log.Warn($"PrefabCatalog: carrier prefab '{cs.Name}' has no usable ObjectGeometryData bounds — " +
                                         "skipped (cannot derive grip-top offset)");
                    }
                }
            }
            m_Marker = FoundOf(SkiCatalog.MarkerPrefab);

            IReadOnlyList<StationDef> stations = SkiCatalog.Stations;
            for (int i = 0; i < stations.Count; i++)
            {
                Entity e = FoundOf(stations[i].Prefab);
                if (e != Entity.Null && !m_StationDefs.ContainsKey(e))
                {
                    m_Stations.Add(e);
                    m_StationDefs.Add(e, stations[i]);
                }
            }
            IReadOnlyList<LodgeDef> lodges = SkiCatalog.Lodges;
            for (int i = 0; i < lodges.Count; i++)
            {
                Entity e = FoundOf(lodges[i].Prefab);
                if (e != Entity.Null && !m_LodgeDefs.ContainsKey(e))
                {
                    m_Lodges.Add(e);
                    m_LodgeDefs.Add(e, lodges[i]);
                }
            }
            IReadOnlyList<SchoolDef> schools = SkiCatalog.Schools;
            for (int i = 0; i < schools.Count; i++)
            {
                Entity e = FoundOf(schools[i].Prefab);
                if (e != Entity.Null && !m_SchoolDefs.ContainsKey(e))
                {
                    m_Schools.Add(e);
                    m_SchoolDefs.Add(e, schools[i]);
                }
            }
            IReadOnlyList<PatrolDef> patrols = SkiCatalog.Patrols;
            for (int i = 0; i < patrols.Count; i++)
            {
                Entity e = FoundOf(patrols[i].Prefab);
                if (e != Entity.Null && !m_PatrolDefs.ContainsKey(e))
                {
                    m_Patrols.Add(e);
                    m_PatrolDefs.Add(e, patrols[i]);
                }
            }
            IReadOnlyList<SignDef> signs = SkiCatalog.Signs;
            for (int i = 0; i < signs.Count; i++)
            {
                Entity e = FoundOf(signs[i].Prefab);
                if (e != Entity.Null)
                    m_Signs.Add(e);
            }
            return carrierWarn;
        }

        /// <summary>An extra (type, name): registered on first ask and resolved alone; afterwards re-resolved with the
        /// catalog. In a failed load it is registered but not resolved (every query answers MISSING until the next load).</summary>
        private NameSlot ExtraSlot(string typeName, string name)
        {
            string key = typeName + "/" + name;
            if (m_ExtraByKey.TryGetValue(key, out int index))
                return m_Slots[index];

            var slot = new NameSlot { Name = name, TypeName = typeName, Kind = CatalogKind.Line, IsExtra = true, Expected = true };
            slot.Clear();
            index = m_Slots.Count;
            m_Slots.Add(slot);
            m_ExtraByKey.Add(key, index);
            if (m_FailedThisLoad)
                return slot;
            try
            {
                ResolveSlots(new List<int>(1) { index }, out int _, out int _, out int scanned);
                if (slot.Found != Entity.Null)
                    Mod.Log.Info($"PrefabCatalog [extra] '{key}' (not a catalog name with this type) FOUND {Ref(slot.Found)} " +
                                 $"({slot.How}{(scanned > 0 ? ", scanned " + I(scanned) : "")}) — cached per load, validated with the catalog");
                else
                    Mod.Log.Warn($"PrefabCatalog [extra] '{key}' (not a catalog name with this type) MISSING — cached per load, " +
                                 "retried with the next resolution");
            }
            catch (Exception e)
            {
                slot.Clear();
                Mod.Log.Warn($"PrefabCatalog [extra] '{key}' resolution threw {e.GetType().Name}: {e.Message} — reads as MISSING");
            }
            return slot;
        }

        /// <summary>"E/30 expected FOUND, P/0 pending FOUND — … ; MISSING: … ; pending (not imported yet): …" into sb;
        /// sets m_ExpectedMissing / m_PendingFound. True when an EXPECTED name is MISSING or any FOUND name has another
        /// PrefabID type (pending MISSING names never raise it).</summary>
        private bool AppendSlots(StringBuilder sb)
        {
            int expectedTotal = 0, expectedFound = 0, pendingTotal = 0, pendingFound = 0;
            bool warn = false;
            var missing = new List<string>();
            var pending = new List<string>();
            var body = new StringBuilder(2048);
            for (int i = 0; i < m_CatalogSlots; i++)
            {
                NameSlot s = m_Slots[i];
                if (s.Expected)
                    expectedTotal++;
                else
                    pendingTotal++;
                if (s.Found != Entity.Null)
                {
                    if (s.Expected)
                        expectedFound++;
                    else
                        pendingFound++;
                    if (s.TypeMismatch)
                        warn = true;
                }
                else if (s.Expected)
                {
                    warn = true;
                    missing.Add(s.Name + " (" + s.TypeName + ")");
                }
                else
                {
                    pending.Add(s.Name + " (" + s.TypeName + ")");
                    continue;   // listed once, in the pending list
                }
                if (body.Length > 0)
                    body.Append(", ");
                AppendSlot(body, s, KindLabel(s.Kind));
            }
            m_ExpectedMissing = missing.Count;
            m_PendingFound = pendingFound;
            sb.Append(I(expectedFound)).Append('/').Append(I(expectedTotal)).Append(" expected FOUND, ")
              .Append(I(pendingFound)).Append('/').Append(I(pendingTotal)).Append(" pending FOUND — ").Append(body.ToString());
            sb.Append("; MISSING: ").Append(missing.Count == 0 ? "none" : string.Join(", ", missing.ToArray()));
            sb.Append("; pending (not imported yet): ").Append(pending.Count == 0 ? "none" : string.Join(", ", pending.ToArray()));
            if (m_Slots.Count > m_CatalogSlots)
            {
                sb.Append("; extras (not catalog names): ");
                for (int i = m_CatalogSlots; i < m_Slots.Count; i++)
                {
                    if (i > m_CatalogSlots)
                        sb.Append(", ");
                    AppendSlot(sb, m_Slots[i], "extra");
                }
            }
            return warn;
        }

        private static void AppendSlot(StringBuilder sb, NameSlot s, string label)
        {
            sb.Append(label).Append(' ').Append(s.Name).Append(' ');
            if (s.Found == Entity.Null)
            {
                sb.Append("MISSING (").Append(s.TypeName).Append(')');
                return;
            }
            sb.Append(Ref(s.Found)).Append(" (").Append(s.TypeName).Append(", ").Append(s.How);
            if (s.IsExtra)
            {
                sb.Append(')');
                return;
            }
            if (!s.Expected)
                sb.Append(", PENDING name FOUND");
            switch (s.Kind)
            {
                case CatalogKind.Line:
                    LiftTypeDef line = SkiCatalog.ByLinePrefab(s.Name);
                    if (line != null)
                        sb.Append("; ").Append(line.Id);
                    break;
                case CatalogKind.Carrier:
                    LiftTypeDef owner = SkiCatalog.ByCarrierPrefab(s.Name);
                    if (s.NoBounds)
                        sb.Append(", NO usable bounds — not a carrier");
                    else
                        sb.Append(", grip-top ").Append(F(s.GripTop, "0.000")).Append(" m");
                    if (owner != null)
                    {
                        sb.Append("; ").Append(owner.Id).Append(", HangM ").Append(F(owner.HangM, "0.000"));
                        if (s.HangDrift)
                            sb.Append(" — DIFFERS");
                    }
                    break;
                case CatalogKind.Station:
                    StationDef st = SkiCatalog.StationByPrefab(s.Name);
                    if (st != null)
                        sb.Append("; ").Append(st.Role.ToString()).Append(' ').Append(st.LiftTypeId)
                          .Append(", wheel ").Append(Wheel(st.WheelLocal))
                          .Append(", stub z ").Append(F(st.StubTipLocalZ, "0.###"))
                          .Append(", cost ").Append(I(st.BuildCost));
                    break;
                case CatalogKind.Lodge:
                    LodgeDef lodge = SkiCatalog.LodgeByPrefab(s.Name);
                    if (lodge != null)
                        sb.Append("; ").Append(lodge.Id).Append(": ")
                          .Append(lodge.SkiersPerDay.ToString("0.###", CultureInfo.InvariantCulture)).Append(" skiers a day, B ")
                          .Append(D(lodge.B)).Append(" / E ").Append(D(lodge.E)).Append(", μ ").Append(D(lodge.Mu))
                          .Append(lodge.MuSummit > lodge.Mu ? " (" + D(lodge.MuSummit) + " summit-served)" : "")
                          .Append(", tolerance ").Append(I(lodge.PriceTolerance))
                          .Append(lodge.Luxury ? ", luxury" : "")
                          .Append(", ").Append(RoadLabel(lodge.RoadRule));
                    break;
                case CatalogKind.School:
                    SchoolDef school = SkiCatalog.SchoolByPrefab(s.Name);
                    if (school != null)
                        sb.Append("; +").Append(D(school.ScoreValue)).Append(" with a counted green run, locals ×")
                          .Append(D(school.DemandFactor)).Append(", ").Append(RoadLabel(school.RoadRule));
                    break;
                case CatalogKind.Patrol:
                    PatrolDef patrol = SkiCatalog.PatrolByPrefab(s.Name);
                    if (patrol != null)
                        sb.Append("; experts within ").Append(F(patrol.RadiusM, "0")).Append(" m ×").Append(D(patrol.ExpertFactor))
                          .Append(", backcountry served ≥ ").Append(D(patrol.BackcountryServedMin)).Append(", ")
                          .Append(RoadLabel(patrol.RoadRule));
                    break;
                case CatalogKind.Sign:
                    for (int i = 0; i < SkiCatalog.Signs.Count; i++)
                    {
                        if (SkiCatalog.Signs[i].Prefab == s.Name)
                        {
                            sb.Append("; cost ").Append(I(SkiCatalog.Signs[i].BuildCost));
                            break;
                        }
                    }
                    break;
                case CatalogKind.Run:
                    RunDef run = SkiCatalog.RunByPrefab(s.Name);
                    if (run != null)
                        sb.Append("; ").Append(run.Kind.ToString());
                    break;
            }
            sb.Append(')');
        }

        /// <summary>"; lift types: fixedGrip (…), detachable (pending; …), …" — every number of every row.</summary>
        private static void AppendLiftTypes(StringBuilder sb)
        {
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            sb.Append("; lift types: ");
            for (int i = 0; i < lifts.Count; i++)
            {
                LiftTypeDef d = lifts[i];
                bool anyExpected = d.LineExpected;
                for (int c = 0; c < d.Carriers.Count; c++)
                    anyExpected |= d.Carriers[c].Expected;
                if (i > 0)
                    sb.Append(", ");
                sb.Append(d.Id).Append(" (")
                  .Append(d.Expected ? "" : anyExpected ? "partly pending; " : "pending; ")
                  .Append(I(d.SeatsPerCarrier)).Append(" seats, ")
                  .Append(F(d.SpeedMps, "0.###")).Append(" m/s, ")
                  .Append(d.Mover == MoverKind.Loop ? "loop" : "shuttle (Phase C)").Append(", aim ")
                  .Append(F(d.AimHeightM, "0.0")).Append(" m, spacing ")
                  .Append(F(d.SpacingM, "0.#")).Append(" m (design ").Append(F(d.DesignSpacingM, "0.#")).Append(" m), hang ")
                  .Append(F(d.HangM, "0.000")).Append(" m, gauge ±").Append(F(d.GaugeHalfM, "0.0#")).Append(" m, serve ")
                  .Append(d.ServeRule == ServeRuleKind.AlongLine ? "along " : "terminals ")
                  .Append(F(d.ServeRadiusM, "0.#")).Append(" / ")
                  .Append(F(d.TopMarginM, "0.#")).Append(" m, ")
                  .Append(F(d.FixedPerMonth, "0")).Append(" + ")
                  .Append(F(d.PerKmPerMonth, "0")).Append("/km a month, value ")
                  .Append(D(d.ScoreValue)).Append(" / summer ").Append(D(d.SummerShare)).Append(", line ")
                  .Append(I(d.LinePerKm)).Append("/km (hand copy: ").Append(I(d.LineBuildPer8m)).Append(" per 8 m piece × ")
                  .Append(I(SkiCatalog.PiecesPerKm)).Append("), stations ")
                  .Append(I(d.StationCostBase)).Append(" / ").Append(I(d.StationCostTop)).Append(", approach ceiling ")
                  .Append(F(d.ApproachPinCeilingM, "0.0")).Append(" m, ")
                  .Append(d.CloseElectricityView ? "closes the Electricity view" : "keeps the Electricity view")
                  .Append(')');
            }
            if (lifts.Count == 0)
                sb.Append("NO lift type in SkiCatalog");
        }

        /// <summary>Every slot and every view (a full resolution, an exception).</summary>
        private void ClearAll()
        {
            for (int i = 0; i < m_Slots.Count; i++)
                m_Slots[i].Clear();
            ClearViews();
        }

        /// <summary>The views only (a partial re-resolve keeps the live slots and rebuilds the views from all of them).</summary>
        private void ClearViews()
        {
            m_LiftLines.Clear();
            m_LineTypes.Clear();
            m_CarrierGripTops.Clear();
            m_CarrierTypes.Clear();
            m_Stations.Clear();
            m_StationDefs.Clear();
            m_Lodges.Clear();
            m_LodgeDefs.Clear();
            m_Schools.Clear();
            m_SchoolDefs.Clear();
            m_Patrols.Clear();
            m_PatrolDefs.Clear();
            m_Signs.Clear();
            m_Marker = Entity.Null;
        }

        private NameSlot SlotOf(string catalogName)
        {
            return !string.IsNullOrEmpty(catalogName) && m_SlotByName.TryGetValue(catalogName, out int i) ? m_Slots[i] : null;
        }

        private Entity FoundOf(string catalogName)
        {
            NameSlot s = SlotOf(catalogName);
            return s != null ? s.Found : Entity.Null;
        }

        private PrefabBase PrefabOf(string catalogName)
        {
            NameSlot s = SlotOf(catalogName);
            return s != null && s.Found != Entity.Null ? s.Prefab : null;
        }

        private bool IsLive(Entity e)
        {
            return e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e);
        }

        private static void AssertType(string member, string catalogValue, string gameName)
        {
            if (!string.Equals(catalogValue, gameName, StringComparison.Ordinal))
                Mod.Log.Error($"!!! PrefabCatalog: SkiCatalog.{member} '{catalogValue}' != nameof(Game.Prefabs.{gameName}) " +
                              $"'{gameName}' — every PrefabID fast path of that type misses and the scan prefers the wrong " +
                              "type; fix Systems/SkiCatalog.cs");
        }

        private static string KindLabel(CatalogKind kind)
        {
            switch (kind)
            {
                case CatalogKind.Line: return "line";
                case CatalogKind.Carrier: return "carrier";
                case CatalogKind.Marker: return "marker";
                case CatalogKind.Station: return "station";
                case CatalogKind.Lodge: return "lodge";
                case CatalogKind.Sign: return "sign";
                case CatalogKind.Run: return "run";
                case CatalogKind.School: return "school";
                case CatalogKind.Patrol: return "patrol";
                default: return kind.ToString();
            }
        }

        private static string RoadLabel(LodgeRoadRule rule)
        {
            switch (rule)
            {
                case LodgeRoadRule.Base: return "needs a road";
                case LodgeRoadRule.Mountain: return "mountain (no road needed)";
                case LodgeRoadRule.None: return "roadless";
                default: return rule.ToString();
            }
        }

        private static string Wheel(float3 w)
        {
            if (!math.all(math.isfinite(w)))
                return "NaN (no bullwheel arc: a Shuttle terminal, or unknown → plain semicircle)";
            return "(" + F(w.x, "0.###") + ", " + F(w.y, "0.###") + ", " + F(w.z, "0.###") + ")";
        }

        private static string Plural(int n, string one, string many)
        {
            return I(n) + " " + (n == 1 ? one : many);
        }

        private static string Ref(Entity e)
        {
            return I(e.Index) + ":" + I(e.Version);
        }

        private static string I(int v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        private static string F(float v, string format)
        {
            return v.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string D(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
