// SkiRunCatalogSystem - the Ski Resort hub's in-memory list of our ski runs plus the resort totals, and the always-on rating
// service. The simulation scores runs by these ratings, so they exist with the hub closed. SkiResortHubUISystem, SkiResortStatsSystem
// and SkiResortAttractivenessSystem read it. It owns no binding and writes nothing to the world. Main thread only.
// Phase (Mod.cs): UpdateAt UIUpdate, registered before SkiResortStatsSystem and SkiResortHubUISystem. UIUpdate runs paused too and
// after ModificationSystem (SystemOrder.cs:58, 60, 67). A plain UpdateAt runs in add order (UpdateSystem.cs:158-161): catalog (reads
// the Demand the hub set last frame) -> stats -> hub.
// House contract:
// - "created" line.
// - One "Catalog [arm]" line per load.
// - A one-shot "!!!" catch stops the catalog until the next load.
// - No per-frame managed allocation.
// - One rating per frame at most.
// - Game mode only.
// Engine notes: EngineNotes/SkiRunCatalogSystem.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunCatalogSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Wall-clock heartbeat (s) while the hub listens</summary>
            public const float HeartbeatS = 2f;

            /// <summary>Rows written by the hub's DUMP (the census line carries the totals)</summary>
            public const int DumpRowsMax = 64;

            /// <summary>UIUpdate frames after a load before the first rating (plus the heights-ready check). Unverified as sufficient.</summary>
            public const int WarmupFrames = 120;

            /// <summary>Frames between leaving the terrain tool and the terrain epoch bump (tunable, unmeasured)</summary>
            public const int TerrainEpochDelayFrames = 30;

            /// <summary>Wall-clock seconds between backstop re-ratings of the oldest-rated run while the hub is open (see the engine notes)</summary>
            public const float BackstopS = 60f;

            /// <summary>Lattice cells above which the rating grid / result pair is replaced by fresh small instances after the rating (tunable)</summary>
            public const int ShrinkCells = 1 << 19;

            /// <summary>Bind / edit "[rate]" lines per window before the rest are only counted</summary>
            public const int RateLinesPerWindow = 8;
            public const float RateLineWindowS = 10f;

            /// <summary>Changed signature-driven "[rebuild]" lines per window before the rest are only counted (a terrain stroke must not
            /// write a line per frame)</summary>
            public const int RebuildLinesPerWindow = 6;
            public const float RebuildLineWindowS = 10f;

            /// <summary>A change-driven rebuild on one of our runs runs at most once per this many wall-clock s, panel closed and open</summary>
            public const float ClosedMinRebuildIntervalS = 0.5f;
            /// <summary>Panel-closed wall-clock heartbeat (s)</summary>
            public const float ClosedHeartbeatS = 10f;
            /// <summary>Panel-closed backstop: re-rate the oldest-rated run every this many wall-clock s (a safety net only: events, binds
            /// and the terrain epoch catch the real changes)</summary>
            public const float ClosedBackstopS = 600f;
            /// <summary>The one-time "Catalog [service]" cost line is written after this many panel-closed rebuilds that had at least one
            /// run to walk (a rebuild of an empty list measures nothing)</summary>
            public const int ServiceCostLogAfterRebuilds = 32;
        }

        /// <summary>RunRow.RateState values (= SkiRunRatingRules)</summary>
        public const byte RatePending = SkiRunRatingRules.RatePending, RateRated = SkiRunRatingRules.RateRated,
            RateNoTerrain = SkiRunRatingRules.RateNoTerrain, RateTooLarge = SkiRunRatingRules.RateTooLarge,
            RateFailed = SkiRunRatingRules.RateFailed;

        /// <summary>Why-text of a heartbeat rebuild (an unchanged heartbeat rebuild is not logged)</summary>
        private const string WhyHeartbeat = "heartbeat";

        /// <summary>Why-text of a coalesced panel-closed rebuild (a signature reason: unchanged results are counted, changed ones obey
        /// the RebuildLinesPerWindow window)</summary>
        private const string WhyRunChangedClosed = "run changed (panel closed)";

        /// <summary>Why-text of a coalesced panel-open rebuild on one of our bound runs (a signature reason, like the closed one)</summary>
        private const string WhyRunChanged = "run changed";

        /// <summary>One run of ours, as the hub sends it (mod/SkiResortUI/src/bindings.ts RunRow) plus the ordering keys. LengthM /
        /// DropM are the rated values when Approx is false.</summary>
        public struct RunRow
        {
            public Entity Entity;
            public string Name;
            public int Kind;             // the prefab kind (the look and the default name). Behavior reads Features
            public RunFeatures Features; // SkiRunPropSystem.FeaturesOf(Entity, Kind) at this rebuild. A slider or a Groomed toggle requests one
            public bool Bound;
            public bool CustomName;
            public int Ordinal;      // N of the default name, per (Gate, NameGroup), top height descending
            public int NameIndex;    // the trail name pick (SkiRunName). SkiRunNameRules.None = none (the pool full, not given yet)
            public byte NameForm;    // its form (SkiRunNameRules.Form*)
            public float LengthM;
            public float DropM;
            public float AreaM2;
            public float TopY;
            public float CenterX;
            public float CenterZ;

            public Entity Gate;
            public byte RateState;
            public sbyte Band;           // -1 pending, -2 cannot be rated (states 2-4), else SlopeBand 0..4 (SkiSlopeClassifier.cs:441-448)
            public float GradePct, SteepestPct;
            public float TopM, BottomM;
            public bool Approx;

            // Internal: the node estimates (the fallback) and the cache keys of this rebuild (SkiRunRatingRules: Key = node x/z + count,
            // YKey = node y + count)
            internal float NodeLengthM, NodeDropM, NodeTopM, NodeBottomM;
            internal ulong Key, YKey;
            internal float3 NodeTopPos;   // the highest outline node (first one on a tie), x/y/z: the lift-served test point
            internal float2[] OutlineXZ;  // every outline node (x, z), one array per run, reused across rebuilds while Key is unchanged
            internal float[] OutlineY;    // every outline node's y, same order, reused while Key and YKey are unchanged (the downhill links)
        }

        /// <summary>One cached rating</summary>
        private struct RunRating
        {
            public byte State;
            public sbyte Band;
            public float RatingGrade, SteepestGrade, MeanGrade;
            public bool Connected;
            public float DropM, TopM, BottomM, LengthM;
            public int StepMultiple;
            public float RateMs;
            public ulong Key, YKey;
            public int TerrainEpoch;
            public float RatedAt;    // realtimeSinceStartup (backstop: oldest first)
        }

        // Queue reasons: SkiRunRatingRules (pure, tested in tests/ResortBatch)
        private const byte ReasonWarmup = SkiRunRatingRules.ReasonWarmup, ReasonBind = SkiRunRatingRules.ReasonBind,
            ReasonEdit = SkiRunRatingRules.ReasonEdit, ReasonTerrain = SkiRunRatingRules.ReasonTerrain,
            ReasonBackstop = SkiRunRatingRules.ReasonBackstop;
        private static readonly string[] ReasonNames = { "?", "warm-up", "bind", "edit", "terrain", "backstop" };

        private struct QueueEntry
        {
            public Entity Run;
            public byte Reason;
        }

        // Kind tables, indexed by RunKind. The default names by name group (NameGroup: 0 piste, 1 cat track): a run shows its trail name
        // (SkiRunName, R3 DECISIONS 578), in its "Upper" / "Lower" form (FormKeys, by SkiRunNameRules.Form* - 1). It shows "Run {N}" or
        // "Cat track {N}" only without a trail name. N per (resort, group). m_Templates takes its length from DefaultNameKeys, so the tables
        // cannot drift apart.
        private static readonly string[] KindNames = { "Groomed", "Snowmaking", "Night", "Natural", "CatTrack" };
        private static readonly string[] BandNames = { "Flat", "Green", "Blue", "Black", "Double black" };
        private static readonly string[] DefaultNameKeys = { "SkiResort.Hub.RUN_DEFAULT[Run]", "SkiResort.Hub.RUN_DEFAULT[CatTrack]" };
        private static readonly string[] DefaultNameEnglish = { "Run {N}", "Cat track {N}" };
        private static readonly string[] FormKeys = { "SkiResort.Hub.RUN_NAME_FORM[Upper]", "SkiResort.Hub.RUN_NAME_FORM[Lower]" };
        private static readonly string[] FormEnglish = { "Upper {NAME}", "Lower {NAME}" };
        private const string OrdinalPlaceholder = "{N}", NamePlaceholder = "{NAME}";

        private SkiRunOwnershipSystem m_Ownership;
        private SkiRunPropSystem m_RunProps;
        private NameSystem m_NameSystem;
        private TerrainSystem m_TerrainSystem;
        private ToolSystem m_ToolSystem;

        private EntityQuery m_RunQuery;
        private EntityQuery m_BuildingQuery;
        private EntityQuery m_RunChangeQuery;  // our bound runs Created/Updated/Deleted this frame (both panel states)

        // m_Rows is what the hub reads. A rebuild fills m_Scratch and swaps (no allocation once both lists reach the city's run
        // count). m_RowIndex: entity -> index in m_Rows.
        private List<RunRow> m_Rows = new List<RunRow>(64);
        private List<RunRow> m_Scratch = new List<RunRow>(64);
        private readonly Dictionary<Entity, int> m_RowIndex = new Dictionary<Entity, int>();
        private readonly Stopwatch m_Timer = new Stopwatch();

        // Default-name templates (one per kind) and the formatted names, keyed (kind << 16) | N
        private readonly string[] m_Templates = new string[DefaultNameKeys.Length];
        private readonly string[] m_FormTemplates = new string[FormKeys.Length];
        private readonly string[] m_TrailNames = new string[SkiRunNameRules.Count * 3];   // by index * 3 + form. Resolved on first use, cleared with the templates
        private readonly Dictionary<int, string> m_NameCache = new Dictionary<int, string>();
        private int m_NamesGeneration;
        private int m_TemplatesGeneration = -1;
        private Action m_DictionaryChangedHandler;
        private Action<ToolBaseSystem> m_ToolChangedHandler;

        private readonly Dictionary<Entity, RunRating> m_Ratings = new Dictionary<Entity, RunRating>();
        // A re-emitted run's custom name until NameSystem has it (RekeyRun). The next rebuild that finds it there drops it.
        private readonly Dictionary<Entity, string> m_CarriedNames = new Dictionary<Entity, string>();
        private readonly Queue<QueueEntry> m_Front = new Queue<QueueEntry>();
        private readonly Queue<QueueEntry> m_Back = new Queue<QueueEntry>();
        private readonly Dictionary<Entity, byte> m_Queued = new Dictionary<Entity, byte>();
        private readonly RunAliases m_RunAliases = new RunAliases();   // re-emitted runs (SkiRunReemitSystem)
        private Action<Entity, Entity> m_MoveRunState;
        private readonly List<Entity> m_ScratchEntities = new List<Entity>();
        private SlopeGrid m_RateGrid = new SlopeGrid();
        private SlopeResult m_RateResult = new SlopeResult();
        private float3[] m_RateNodes = new float3[64];
        private int m_Frame;
        private int m_TerrainEpoch;
        private int m_TerrainEpochDueFrame = -1;
        private ToolBaseSystem m_LastTool;
        private bool m_WarmupSeeded;
        private bool m_WarmupLogged;
        private bool m_HeightsLogged;
        private int m_WarmupQueued, m_WarmupDone, m_WarmupStartFrame;
        private float m_WarmupTotalMs, m_WarmupMaxMs;
        private float m_NextBackgroundStart;
        private float m_NextBackstop;
        private float m_RateLineWindowStart;
        private int m_RateLinesInWindow, m_RateLinesSuppressed;
        private bool m_SlowRateWarned;

        // Per-load state
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_WasDemanded;
        private string m_RebuildRequest;
        private int m_SigBindGeneration = int.MinValue, m_SigNames = -1;
        private float m_NextHeartbeat;
        private ulong m_LastHash;
        private bool m_OpenChangePending;
        // Rebuilds this load by reason (literal reasons only: no allocation per rebuild after first sight)
        private readonly Dictionary<string, int> m_RebuildsByReason = new Dictionary<string, int>();
        // The last rebuild's time (change coalescing) and the changed-rebuild line window
        private float m_LastRebuildTime = float.NegativeInfinity;
        private float m_RebuildLineWindowStart = float.NegativeInfinity;
        private int m_RebuildLinesInWindow, m_RebuildLinesSuppressed;
        private bool m_ClosedChangePending;
        private float m_ArmedAt;
        private bool m_WarmupDeadlineHit;
        private ulong m_LastFactsHash;
        private int m_ClosedRebuilds, m_ClosedRatings;
        private float m_ClosedRebuildMsSum, m_ClosedRebuildMsMax;
        private float m_ClosedRatingMsSum, m_ClosedRatingMsMax;
        private float m_ClosedSince = -1f;
        private bool m_ServiceCostLogged;
        // The cost line's own record: closed rebuilds with >= 1 row
        private int m_CostRebuilds;
        private float m_CostRebuildMsSum, m_CostRebuildMsMax;
        private float m_CostSince = -1f;

        /// <summary>SkiResortHubUISystem sets this every frame: the hub has an observer on any of the bindings that need the catalog
        /// (resorts, runs, lifts, lodges, city, build). Plain field.</summary>
        public bool Demand;

        /// <summary>Bumped when a rebuild or a rating produced different rows or totals. The hub (and SkiResortStatsSystem) re-read
        /// when it differs from the revision they last saw</summary>
        public int Revision { get; private set; }

        /// <summary>Totals of the last rebuild. Plain fields.</summary>
        public int Signs, RunsBound, RunsUnbound;
        public float AreaM2;

        // Dev tab / DUMP (plain fields, written by a rebuild)
        public string LastRebuildWhy = "-";
        public float LastRebuildMs, MaxRebuildMs;
        public long Rebuilds, RebuildsChanged;

        /// <summary>Rows with RateState 1 (rated) in the current rows</summary>
        public int RatedCount { get; private set; }

        /// <summary>Ratings queued and not yet done (warm-up, bind, edit, terrain, backstop)</summary>
        public int RatingsPending => m_Queued.Count;

        /// <summary>Last / maximum rating time this load (ms)</summary>
        public float LastRateMs, MaxRateMs;

        /// <summary>The last rating's text ("-" before the first), logged or not. A property, not a field: it is formatted lazily on
        /// read (dev tab, DUMP). So warm-up bursts, up to one rating per frame, format nothing.</summary>
        public string LastRateLine
        {
            get
            {
                if (m_LastRateDirty)
                {
                    m_LastRateDirty = false;
                    m_LastRateLine = RateText(m_LastRateName, m_LastRateEntity, m_LastRating, m_LastRateDetail, m_LastRateReason);
                }
                return m_LastRateLine;
            }
        }

        private string m_LastRateLine = "-";
        private bool m_LastRateDirty;
        private string m_LastRateName;
        private Entity m_LastRateEntity;
        private RunRating m_LastRating;
        private RateDetail m_LastRateDetail;
        private string m_LastRateReason;

        /// <summary>Ratings done this load (all reasons) and the current terrain epoch (dev tab)</summary>
        public long Ratings;
        public int TerrainEpoch => m_TerrainEpoch;

        public bool IsArmed => m_Armed;
        public bool IsDisabled => m_Disabled;
        public int RowCount => m_Rows.Count;

        /// <summary>Row i of the last rebuild, in the order the UI shows them. Main thread.</summary>
        public RunRow GetRow(int i) => m_Rows[i];

        /// <summary>The row of <paramref name="run"/> in the last rebuild (dictionary lookup)</summary>
        public bool TryGetRow(Entity run, out RunRow row)
        {
            if (m_RowIndex.TryGetValue(run, out int i) && i >= 0 && i < m_Rows.Count && m_Rows[i].Entity == run)
            {
                row = m_Rows[i];
                return true;
            }
            row = default;
            return false;
        }

        /// <summary>A run was re-emitted (SkiRunReemitSystem). Its rating follows the new entity (keyed by the same nodes, so it stands).
        /// A rating still queued is redone for the new one. Its custom name stands in until the hub writes it to NameSystem at
        /// UIUpdate, so the Runs list never shows a default name in between. Main thread.</summary>
        internal void RekeyRun(Entity from, Entity to, string customName)
        {
            if (!string.IsNullOrEmpty(customName))
                m_CarriedNames[to] = customName;
            if (m_MoveRunState == null)
                m_MoveRunState = MoveRunState;
            m_RunAliases.Add(from, to);
            m_RunAliases.Apply(m_MoveRunState);
        }

        private void MoveRunState(Entity from, Entity to)
        {
            RunAliases.Move(m_Ratings, from, to);
        }

        /// <summary>Rebuild on the next OnUpdate even while nobody listens (dev tab, DUMP)</summary>
        public void RequestRebuild(string why)
        {
            m_RebuildRequest = why;
        }

        // Read API (see the engine notes). RatingServiceState and RunFacts are nested here. Main thread only.

        /// <summary>The rating service's state (= SkiRunRatingRules.Service*)</summary>
        public enum RatingServiceState : byte
        {
            Off = 0,        // not armed (menu, editor, before the first load): nothing to read
            WarmingUp = 1,  // armed, and this load's warm-up is not finished
            Ready = 2,      // warm-up finished, or WarmupDeadlineS passed (WARN written)
            Failed = 3,     // the one-shot "!!!" disabled the catalog until the next load
        }

        /// <summary>What the simulation may read about one of our runs</summary>
        public struct RunFacts
        {
            public Entity Run;
            public Entity Gate;
            public int Kind;
            public RunFeatures Features;
            public byte RateState;
            public sbyte Band;
            public float AreaM2;
            public float LengthM;
            public float DropM;
            public float TopM;
            public float BottomM;
            public float3 TopNode;    // the highest outline node (x, y, z), always valid: the lift-served test point
            public float2[] OutlineXZ; // every outline node (x, z) in ring order, read only (shared with the catalog row, same object until the outline moves)
            public float[] OutlineY;   // every outline node's y, same order, read only (same object until the outline moves or its nodes are re-snapped)
            public bool Approx;
        }

        /// <summary>The rating service's state (see the engine notes). Off outside Game mode and before the first load. WarmingUp from a load
        /// until the warm-up set is rated (or the 90 s deadline). Ready. Failed after the one-shot "!!!" until the next load.</summary>
        public RatingServiceState ServiceState { get; private set; }

        /// <summary>++ whenever any RunFacts field of any run changed, a run arrived or left, the service state changed, or a load
        /// happened. Compare for equality (it never resets).</summary>
        public int RatingRevision { get; private set; }

        /// <summary>The facts of one run from the last rebuild / rating (dictionary lookup)</summary>
        public bool TryGetRunFacts(Entity run, out RunFacts facts)
        {
            if (!TryGetRow(run, out RunRow row))
            {
                facts = default;
                return false;
            }
            facts = new RunFacts
            {
                Run = row.Entity,
                Gate = row.Gate,
                Kind = row.Kind,
                Features = row.Features,
                RateState = row.RateState,
                Band = row.Band,
                AreaM2 = row.AreaM2,
                LengthM = row.LengthM,
                DropM = row.DropM,
                TopM = row.TopM,
                BottomM = row.BottomM,
                TopNode = row.NodeTopPos,
                OutlineXZ = row.OutlineXZ,
                OutlineY = row.OutlineY,
                Approx = row.Approx,
            };
            return true;
        }

        /// <summary>True when the run has a current rating (state 1-4). Band = SlopeBand 0..4 when state 1, else -2. topM / bottomM =
        /// SlopeResult.MaxHeight / MinHeight when state 1, else NaN. False when the run is unknown or pending. Works whether or not the
        /// hub was ever opened. "Current" = the rating's outline key matched the row's key at the last rebuild (ApplyRating). The next rebuild
        /// sees an outline edited since that rebuild (hub open: that frame, closed: within ClosedMinRebuildIntervalS).</summary>
        public bool TryGetRating(Entity run, out byte state, out sbyte band, out float topM, out float bottomM)
        {
            if (TryGetRow(run, out RunRow row) && row.RateState != RatePending)
            {
                bool rated = row.RateState == RateRated;
                state = row.RateState;
                band = rated ? row.Band : SkiRunRatingRules.BandUnrateable;
                topM = rated ? row.TopM : float.NaN;
                bottomM = rated ? row.BottomM : float.NaN;
                return true;
            }
            state = RatePending;
            band = SkiRunRatingRules.BandPending;
            topM = float.NaN;
            bottomM = float.NaN;
            return false;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_RunProps = World.GetOrCreateSystemManaged<SkiRunPropSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();

            m_RunQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                    ComponentType.ReadOnly<Game.Areas.Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            m_BuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            // The panel-closed trigger (and the panel-open one too, coalesced the same way). The tags live until CleanUpSystem (Cleanup,
            // after UIUpdate), so UIUpdate sees this frame's tags (CleanUpSystem.cs:53, SystemOrder.cs:67). SkiRunBinding scopes the query to
            // our bound runs. Without it the query matched every SurfacePrefab area, and vanilla buildings carry Surface sub-areas (grass,
            // pavement, tiles placeholders in ObjectSubAreas). So every building spawn, upgrade or demolition in a growing city owed a
            // closed rebuild every 0.5 s for the whole session. A SkiRunBinding write binds a new run, and BindGeneration
            // below catches it. A Deleted run still carries its components this frame. An unbound run scores nothing. The
            // ClosedHeartbeatS heartbeat (or "hub opened") finds it.
            m_RunChangeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<SkiRunBinding>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });

            // Localized default names: drop the cache when the language changes (the handler only bumps a counter). The next rebuild
            // re-reads the templates.
            m_DictionaryChangedHandler = OnDictionaryChanged;
            try
            {
                GameManager.instance.localizationManager.onActiveDictionaryChanged += m_DictionaryChangedHandler;
            }
            catch (Exception e)
            {
                m_DictionaryChangedHandler = null;
                Mod.Log.Warn($"Catalog: could not subscribe to language changes ({e.GetType().Name}: {e.Message}) — " +
                             "default run names keep the language of their first rebuild until the next load");
            }

            // Terrain epoch: ToolSystem.EventToolChanged (public Action field, ToolSystem.cs:30). The activeTool setter invokes it only
            // when the tool changes (:84-98)
            m_ToolChangedHandler = OnToolChanged;
            m_ToolSystem.EventToolChanged += m_ToolChangedHandler;

            Mod.Log.Info($"{nameof(SkiRunCatalogSystem)} created");
        }

        protected override void OnDestroy()
        {
            try
            {
                if (m_DictionaryChangedHandler != null && GameManager.instance != null &&
                    GameManager.instance.localizationManager != null)
                    GameManager.instance.localizationManager.onActiveDictionaryChanged -= m_DictionaryChangedHandler;
                if (m_ToolChangedHandler != null && m_ToolSystem != null)
                    m_ToolSystem.EventToolChanged -= m_ToolChangedHandler;
            }
            catch (Exception)
            {
            }
            base.OnDestroy();
        }

        private void OnDictionaryChanged()
        {
            m_NamesGeneration++;
        }

        /// <summary>Leaving the terrain tool schedules a terrain epoch bump (applied TerrainEpochDelayFrames later, in OnUpdate).
        /// Field writes only. Never throws.</summary>
        private void OnToolChanged(ToolBaseSystem tool)
        {
            if (m_LastTool is TerrainToolSystem && !(tool is TerrainToolSystem))
                m_TerrainEpochDueFrame = m_Frame + Tuning.TerrainEpochDelayFrames;
            m_LastTool = tool;
        }

        /// <summary>Our own terrain write (CatTrackGradeToolSystem grade / undo) schedules the same terrain epoch bump as leaving the
        /// vanilla terrain tool, so every run is re-rated on the new ground. Field writes only. Never throws.</summary>
        public void NoteTerrainChanged(string why)
        {
            m_TerrainEpochDueFrame = m_Frame + Tuning.TerrainEpochDelayFrames;
            Mod.Log.Info($"{nameof(SkiRunCatalogSystem)}: terrain changed ({why}) — terrain epoch bump in {Tuning.TerrainEpochDelayFrames} frames");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_WasDemanded = false;
            m_RebuildRequest = null;
            m_SigBindGeneration = int.MinValue;
            m_SigNames = -1;
            m_NextHeartbeat = 0f;
            m_LastHash = 0;
            m_OpenChangePending = false;
            m_RebuildsByReason.Clear();
            m_LastRebuildTime = float.NegativeInfinity;
            m_RebuildLineWindowStart = float.NegativeInfinity;
            m_RebuildLinesInWindow = 0;
            m_RebuildLinesSuppressed = 0;
            m_Rows.Clear();
            m_Scratch.Clear();
            m_RowIndex.Clear();
            m_NameCache.Clear();
            m_TemplatesGeneration = -1;
            Signs = 0;
            RunsBound = 0;
            RunsUnbound = 0;
            AreaM2 = 0f;
            Revision++;   // the previous city's rows are gone: the hub re-sends
            LastRebuildWhy = "-";
            LastRebuildMs = 0f;
            MaxRebuildMs = 0f;
            Rebuilds = 0;
            RebuildsChanged = 0;
            Demand = false;
            // Nothing of the previous city survives
            m_Ratings.Clear();
            m_RunAliases.Clear();
            m_CarriedNames.Clear();
            m_Front.Clear();
            m_Back.Clear();
            m_Queued.Clear();
            m_RateGrid = new SlopeGrid();
            m_RateResult = new SlopeResult();
            m_Frame = 0;
            m_TerrainEpoch = 0;
            m_TerrainEpochDueFrame = -1;
            m_LastTool = m_ToolSystem.activeTool;
            m_WarmupSeeded = false;
            m_WarmupLogged = false;
            m_HeightsLogged = false;
            m_WarmupQueued = 0;
            m_WarmupDone = 0;
            m_WarmupStartFrame = 0;
            m_WarmupTotalMs = 0f;
            m_WarmupMaxMs = 0f;
            m_NextBackgroundStart = 0f;
            m_NextBackstop = 0f;
            m_RateLineWindowStart = 0f;
            m_RateLinesInWindow = 0;
            m_RateLinesSuppressed = 0;
            m_SlowRateWarned = false;
            RatedCount = 0;
            LastRateMs = 0f;
            MaxRateMs = 0f;
            m_LastRateLine = "-";
            m_LastRateDirty = false;
            Ratings = 0;
            // The always-on service starts Off. Game mode arms it below
            m_ClosedChangePending = false;
            m_ArmedAt = 0f;
            m_WarmupDeadlineHit = false;
            m_LastFactsHash = 0;
            m_ClosedRebuilds = 0;
            m_ClosedRatings = 0;
            m_ClosedRebuildMsSum = 0f;
            m_ClosedRebuildMsMax = 0f;
            m_ClosedRatingMsSum = 0f;
            m_ClosedRatingMsMax = 0f;
            m_ClosedSince = -1f;
            m_ServiceCostLogged = false;
            m_CostRebuilds = 0;
            m_CostRebuildMsSum = 0f;
            m_CostRebuildMsMax = 0f;
            m_CostSince = -1f;
            ServiceState = RatingServiceState.Off;

            if (mode != GameMode.Game)
            {
                RatingRevision++;   // a load happened (the rows are gone)
                return;
            }

            m_Armed = true;
            ServiceState = RatingServiceState.WarmingUp;
            m_ArmedAt = UnityEngine.Time.realtimeSinceStartup;
            m_RebuildRequest = "load";   // the first rebuild writes one "Catalog [rebuild] (load)" line, hub or no hub
            RatingRevision++;
            Mod.Log.Info("Catalog [arm] — run list, resort links and built-run ratings, ALWAYS ON (Round E1, D12: the " +
                         "simulation scores runs by these ratings). Panel open: rebuild at once when it opens, then on " +
                         "one of our bound runs Created/Updated/Deleted (coalesced to " +
                         $"{Tuning.ClosedMinRebuildIntervalS:0.##} s), BindGeneration, a language change, a " +
                         $"{Tuning.HeartbeatS:0.##} s heartbeat (wall clock; Round M2: no building, surface-area or area-edit " +
                         "trigger). Panel closed: rebuild when one of our bound runs is " +
                         "Created/Updated/Deleted or BindGeneration moves (at most every " +
                         $"ClosedMinRebuildIntervalS={Tuning.ClosedMinRebuildIntervalS:0.##} s) or every " +
                         $"ClosedHeartbeatS={Tuning.ClosedHeartbeatS:0.##} s; no sign count, no language trigger. Changed " +
                         $"signature rebuilds logged at most RebuildLinesPerWindow={Tuning.RebuildLinesPerWindow} per " +
                         $"RebuildLineWindowS={Tuning.RebuildLineWindowS:0.##} s (the rest counted). Per run: " +
                         "area = Geometry.m_SurfaceArea (plan), U1 estimates length = (P + sqrt(max(0, P² − 16A))) / 4 over the " +
                         "node ring and drop = max − min node height until rated; Gate = TryGetBoundGate; kinds " +
                         $"[{string.Join(", ", KindNames)}]; default names: a piste's trail name (SkiRunName, {SkiRunNameRules.Count} names), else " +
                         $"[{string.Join(", ", DefaultNameEnglish)}] numbered per (resort, piste / cat track) by top height (OD-3), custom names from NameSystem win; rows sent linked first, " +
                         "unlinked last. RATING CACHE (hub-u2-spec §3.5): SkiRunRater on the built node ring, stepCurrent 0, " +
                         "no height reuse; key = FNV-1a of node x/z + count (edit → front), y-key = node y + count (re-snap → " +
                         "background 'terrain'); warm-up entries never promoted by 'bind'; band -1 pending, -2 cannot be rated; " +
                         $"WarmupFrames={Tuning.WarmupFrames} (+ heights ready, MinHeightmapResolution=" +
                         $"{SkiRunDrawFeedbackSystem.Tuning.MinHeightmapResolution}); one rating per UIUpdate frame, paused " +
                         "included; bind/edit at once; warm-up/terrain/backstop duty factor " +
                         $"DutyFactorOpen={SkiRunRatingRules.DutyFactorOpen:0.##} open, " +
                         $"DutyFactorClosed={SkiRunRatingRules.DutyFactorClosed:0.##} and " +
                         $"ClosedMinGapS={SkiRunRatingRules.ClosedMinGapS:0.##} s closed; backstop " +
                         $"BackstopS={Tuning.BackstopS:0.##} s open, ClosedBackstopS={Tuning.ClosedBackstopS:0.##} s closed; " +
                         $"warm-up deadline WarmupDeadlineS={SkiRunRatingRules.WarmupDeadlineS:0.##} s; RatingRevision moves " +
                         "when a run fact the simulation reads changes (gate, kind, area, rate state, band, length, drop, top, " +
                         "bottom, top node); terrain epoch on leaving the terrain tool + " +
                         $"TerrainEpochDelayFrames={Tuning.TerrainEpochDelayFrames}; ShrinkCells={Tuning.ShrinkCells}; [rate] " +
                         $"lines for bind / edit, at most RateLinesPerWindow={Tuning.RateLinesPerWindow} per " +
                         $"RateLineWindowS={Tuning.RateLineWindowS:0.##} s; one slow-rating WARN per load above the drawing " +
                         $"tooltip's SlowRecomputeWarnMs={SkiRunDrawFeedbackSystem.Tuning.SlowRecomputeWarnMs:0.##} ms; " +
                         $"DumpRowsMax={Tuning.DumpRowsMax}");
        }

        protected override void OnUpdate()
        {
            try
            {
                m_Frame++;
                if (m_RunAliases.Any)
                    m_RunAliases.Apply(m_MoveRunState);
                // Terrain epoch: one int compare per frame, hub open or not
                if (m_TerrainEpochDueFrame >= 0 && m_Frame >= m_TerrainEpochDueFrame)
                {
                    m_TerrainEpochDueFrame = -1;
                    m_TerrainEpoch++;
                }

                if (m_Disabled || !m_Armed)
                    return;
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;

                // One service, two cadences: the hub's Demand picks the triggers, the sign count, the background throttle and the backstop interval
                bool demand = Demand;
                float now = UnityEngine.Time.realtimeSinceStartup;
                string why = m_RebuildRequest;
                m_RebuildRequest = null;
                if (demand && !m_WasDemanded && why == null)
                    why = "hub opened";
                m_WasDemanded = demand;   // read by LogWarmupDone (the factor in force) this frame
                if (why == null)
                    why = demand ? OpenSignatureWhy(now) : ClosedSignatureWhy(now);
                if (why != null)
                {
                    Rebuild(why, now, countSigns: demand);
                    m_LastRebuildTime = now;
                    m_NextHeartbeat = now + (demand ? Tuning.HeartbeatS : Tuning.ClosedHeartbeatS);
                }
                RateStep(now, demand);       // both modes: at most one rating per frame
                ServiceTick(now);            // warm-up deadline + the one-time cost line
            }
            catch (Exception e)
            {
                HandleException(e, "OnUpdate");
            }
        }

        /// <summary>The panel-open change signature (see the engine notes Contract): in order "run changed" (one of our bound runs Created /
        /// Updated / Deleted, coalesced to ClosedMinRebuildIntervalS like the closed path), "bind generation", "language", the heartbeat.
        /// Per frame: one IsEmptyIgnoreFilter, two int compares, float compares. No allocation. Null when nothing moved.</summary>
        private string OpenSignatureWhy(float now)
        {
            if (!m_RunChangeQuery.IsEmptyIgnoreFilter)
                m_OpenChangePending = true;
            if (m_OpenChangePending && now - m_LastRebuildTime >= Tuning.ClosedMinRebuildIntervalS)
                return WhyRunChanged;   // Rebuild clears the pending flag
            if (m_Ownership.BindGeneration != m_SigBindGeneration)
                return "bind generation";
            if (m_NamesGeneration != m_SigNames)
                return "language";
            return now >= m_NextHeartbeat ? WhyHeartbeat : null;
        }

        /// <summary>The panel-closed triggers. An event on one of our bound run areas (m_RunChangeQuery carries SkiRunBinding, so vanilla
        /// surface sub-areas of buildings do not match) or a binding write marks a rebuild owed. It runs at most once per
        /// ClosedMinRebuildIntervalS. Otherwise the heartbeat triggers it. Per frame: one IsEmptyIgnoreFilter, one int compare, two float compares.</summary>
        private string ClosedSignatureWhy(float now)
        {
            if (!m_RunChangeQuery.IsEmptyIgnoreFilter || m_Ownership.BindGeneration != m_SigBindGeneration)
                m_ClosedChangePending = true;
            if (m_ClosedChangePending && now - m_LastRebuildTime >= Tuning.ClosedMinRebuildIntervalS)
            {
                m_ClosedChangePending = false;
                return WhyRunChangedClosed;
            }
            return now >= m_NextHeartbeat ? WhyHeartbeat : null;
        }


        private void Rebuild(string why, float now, bool countSigns)
        {
            m_Timer.Reset();
            m_Timer.Start();

            // The signature this rebuild answers (read before the walk, so a change during it re-runs). Any rebuild answers a pending
            // panel-open run change (it walks every run).
            m_SigBindGeneration = m_Ownership.BindGeneration;
            m_SigNames = m_NamesGeneration;
            m_OpenChangePending = false;

            m_Scratch.Clear();
            int signs = 0;
            bool ready = m_Ownership.PrefabSetsReady;   // sign + run prefab sets resolved this load
            if (ready)
            {
                // The building walk runs only while the hub listens. When closed, Signs keeps its last value (it feeds only the hub, and "hub opened" recounts)
                signs = countSigns ? CountSigns() : Signs;
                CollectRuns();
            }

            // Ordinals: per (Gate, name group), top height descending
            m_Scratch.Sort(OrdinalOrder.Instance);
            Entity lastGate = new Entity { Index = -1, Version = -1 };
            int lastGroup = -1, n = 0;
            for (int r = 0; r < m_Scratch.Count; r++)
            {
                RunRow row = m_Scratch[r];
                if (NameGroup(in row) != lastGroup || row.Gate != lastGate)
                {
                    lastGroup = NameGroup(in row);
                    lastGate = row.Gate;
                    n = 0;
                }
                row.Ordinal = ++n;
                m_Scratch[r] = row;
            }

            // Names + the rating cache's view of each row
            RefreshTemplates();
            for (int r = 0; r < m_Scratch.Count; r++)
            {
                RunRow row = m_Scratch[r];
                if (m_NameSystem.TryGetCustomName(row.Entity, out string custom) && !string.IsNullOrEmpty(custom))
                {
                    row.Name = custom;
                    row.CustomName = true;
                    m_CarriedNames.Remove(row.Entity);
                }
                else if (m_CarriedNames.TryGetValue(row.Entity, out string carried))
                {
                    row.Name = carried;
                    row.CustomName = true;
                }
                else
                {
                    row.Name = DefaultName(in row);
                    row.CustomName = false;
                }
                ApplyRating(ref row);
                m_Scratch[r] = row;
            }

            // Display order: linked first, unlinked last. Highest top first
            m_Scratch.Sort(DisplayOrder.Instance);

            int bound = 0, unbound = 0;
            float area = 0f;
            for (int r = 0; r < m_Scratch.Count; r++)
            {
                RunRow row = m_Scratch[r];
                if (row.Bound)
                    bound++;
                else
                    unbound++;
                area += row.AreaM2;
            }
            ulong hash = ComputeHash(m_Scratch, signs, out int rated);

            bool changed = hash != m_LastHash || m_Scratch.Count != m_Rows.Count || Rebuilds == 0;
            List<RunRow> swap = m_Rows;
            m_Rows = m_Scratch;
            m_Scratch = swap;
            m_RowIndex.Clear();
            for (int r = 0; r < m_Rows.Count; r++)
                m_RowIndex[m_Rows[r].Entity] = r;
            Signs = signs;
            RunsBound = bound;
            RunsUnbound = unbound;
            AreaM2 = area;
            RatedCount = rated;
            m_LastHash = hash;
            if (changed)
            {
                Revision++;
                RebuildsChanged++;
            }
            Rebuilds++;
            // Per reason (literal why-texts). A key is added once, then only its count moves
            m_RebuildsByReason.TryGetValue(why, out int byWhy);
            m_RebuildsByReason[why] = byWhy + 1;

            // Queue what needs a rating, drop cache entries of runs that left
            UpdateQueue();
            // The simulation's facts (RatingRevision) after the rows and the queue settled
            UpdateFactsRevision();

            m_Timer.Stop();
            float ms = (float)m_Timer.Elapsed.TotalMilliseconds;
            LastRebuildMs = ms;
            if (ms > MaxRebuildMs)
                MaxRebuildMs = ms;
            LastRebuildWhy = why;
            if (!countSigns)
            {
                // The panel-closed rebuild cost ("Catalog [service]", once per load)
                if (m_ClosedRebuilds == 0)
                    m_ClosedSince = now;
                m_ClosedRebuilds++;
                m_ClosedRebuildMsSum += ms;
                if (ms > m_ClosedRebuildMsMax)
                    m_ClosedRebuildMsMax = ms;
                // The cost line's record counts only rebuilds that had a run to walk
                if (m_Rows.Count > 0)
                {
                    if (m_CostRebuilds == 0)
                        m_CostSince = now;
                    m_CostRebuilds++;
                    m_CostRebuildMsSum += ms;
                    if (ms > m_CostRebuildMsMax)
                        m_CostRebuildMsMax = ms;
                }
            }

            bool requested = !ReferenceEquals(why, WhyHeartbeat) && !IsSignatureWhy(why);
            bool log = requested;
            if (changed && !requested)
            {
                // Changed signature / heartbeat rebuilds are windowed
                if (now - m_RebuildLineWindowStart >= Tuning.RebuildLineWindowS)
                {
                    m_RebuildLineWindowStart = now;
                    m_RebuildLinesInWindow = 0;
                }
                if (m_RebuildLinesInWindow < Tuning.RebuildLinesPerWindow)
                {
                    m_RebuildLinesInWindow++;
                    log = true;
                }
                else
                {
                    m_RebuildLinesSuppressed++;
                }
            }
            else if (!log && m_RebuildLinesSuppressed > 0 && now - m_RebuildLineWindowStart >= Tuning.RebuildLineWindowS)
            {
                // Trailing line once the window passed: the final state is never silent
                m_RebuildLineWindowStart = now;
                m_RebuildLinesInWindow = 1;
                log = true;
            }
            if (log)
            {
                string suffix = m_RebuildLinesSuppressed > 0
                    ? $" (+{m_RebuildLinesSuppressed} earlier changed rebuild(s) not logged: more than " +
                      $"{Tuning.RebuildLinesPerWindow} per {Tuning.RebuildLineWindowS:0} s)"
                    : "";
                m_RebuildLinesSuppressed = 0;
                // With the hub closed the signs are not counted (Signs keeps its last value, 0 after a load). So the line says so. It does not
                // print a count that reads as "no sign".
                string signText = countSigns ? $"{signs} Welcome Sign(s)" : "Welcome Signs not counted (hub closed)";
                Mod.Log.Info($"Catalog [rebuild] ({why}): {m_Rows.Count} run(s) — {bound} linked to a sign, " +
                             $"{unbound} not linked; {signText}; area {area:F0} m²; rated {rated}, " +
                             $"{RatingsPending} rating(s) queued; {ms:F2} ms; " +
                             $"revision {Revision} ({(changed ? "changed" : "unchanged")})" +
                             (ready ? "" : " — ownership prefab sets not ready (pre-import?): empty list") + suffix);
            }
        }

        /// <summary>Signature-driven rebuilds with an unchanged result are counted, not logged</summary>
        private static bool IsSignatureWhy(string why)
        {
            return why == WhyRunChanged || why == "bind generation" || why == "language" || why == WhyRunChangedClosed;
        }

        /// <summary>RatingRevision++ when the hash of every RunFacts field moved</summary>
        private void UpdateFactsRevision()
        {
            ulong f = ComputeFactsHash(m_Rows);
            if (f != m_LastFactsHash)
            {
                m_LastFactsHash = f;
                RatingRevision++;
            }
        }

        /// <summary>FNV-1a (SkiRunRatingRules.Mix) over, per row in row order, Entity, Gate, Kind, RateState | Band &lt;&lt; 8 and the
        /// bits of AreaM2, LengthM, DropM, TopM, BottomM and NodeTopPos, then the row count. Names, ordinals, grades and the sign count
        /// are not inputs (a rename does not move RatingRevision, and the hub's Revision covers those).</summary>
        private static ulong ComputeFactsHash(List<RunRow> rows)
        {
            ulong hash = SkiRunRatingRules.FnvOffset;
            for (int r = 0; r < rows.Count; r++)
            {
                RunRow row = rows[r];
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Entity.Index);
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Entity.Version);
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Gate.Index);
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Gate.Version);
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Kind);
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Features.GetHashCode());   // a slider or a Groomed toggle is a facts change
                hash = SkiRunRatingRules.Mix(hash, (uint)row.RateState | ((uint)(byte)row.Band << 8));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.AreaM2));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.LengthM));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.DropM));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.TopM));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.BottomM));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.NodeTopPos.x));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.NodeTopPos.y));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.NodeTopPos.z));
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Key);          // Key/YKey cover every outline node x,z / y
                hash = SkiRunRatingRules.Mix(hash, (uint)(row.Key >> 32));
            }
            return SkiRunRatingRules.Mix(hash, (uint)rows.Count);
        }

        /// <summary>FNV-1a over every row field the hub or the stats can see, plus the sign count. Gate is included, because without it a free
        /// re-link to another sign would change nothing the hub sees. Ordinal, RateState, Band, GradePct, SteepestPct, TopM,
        /// BottomM and Approx are included too.</summary>
        private static ulong ComputeHash(List<RunRow> rows, int signs, out int rated)
        {
            rated = 0;
            ulong hash = 14695981039346656037UL;
            hash = Mix(hash, (uint)signs);
            for (int r = 0; r < rows.Count; r++)
            {
                RunRow row = rows[r];
                if (row.RateState == RateRated)
                    rated++;
                hash = Mix(hash, (uint)row.Entity.Index);
                hash = Mix(hash, (uint)row.Entity.Version);
                hash = Mix(hash, (uint)row.Kind);
                hash = Mix(hash, row.Bound ? 1u : 0u);
                hash = Mix(hash, math.asuint(row.LengthM));
                hash = Mix(hash, math.asuint(row.DropM));
                hash = Mix(hash, math.asuint(row.AreaM2));
                hash = Mix(hash, (uint)(row.Name == null ? 0 : row.Name.GetHashCode()));
                hash = Mix(hash, (uint)row.Gate.Index);
                hash = Mix(hash, (uint)row.Gate.Version);
                hash = Mix(hash, (uint)row.Ordinal);
                hash = Mix(hash, (uint)row.RateState | ((uint)(byte)row.Band << 8) | (row.Approx ? 1u << 16 : 0u));
                hash = Mix(hash, math.asuint(row.GradePct));
                hash = Mix(hash, math.asuint(row.SteepestPct));
                hash = Mix(hash, math.asuint(row.TopM));
                hash = Mix(hash, math.asuint(row.BottomM));
            }
            return Mix(hash, (uint)rows.Count);
        }

        /// <summary>Live Welcome Signs: a chunk walk of every building, PrefabRef checked against the ownership system's sign set (the
        /// same test as SkiRunOwnershipSystem.CollectGates). Chunk reads through type handles do not wait for running jobs (Entities
        /// semantics, unverified here), so the code completes the query's writers first.</summary>
        private int CountSigns()
        {
            if (m_BuildingQuery.IsEmptyIgnoreFilter)
                return 0;
            m_BuildingQuery.CompleteDependency();
            int count = 0;
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            using (var chunks = m_BuildingQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (m_Ownership.IsGatePrefab(prefabRefs[i].m_Prefab))
                            count++;
                    }
                }
            }
            return count;
        }

        /// <summary>Fills m_Scratch with every live run of ours. Two loops (the ownership pattern): the chunk walk copies entity, kind
        /// and geometry. The code reads the node buffers and the bound test after it releases the chunk array.</summary>
        private void CollectRuns()
        {
            if (m_RunQuery.IsEmptyIgnoreFilter)
                return;
            m_RunQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            var geometryHandle = GetComponentTypeHandle<Game.Areas.Geometry>(isReadOnly: true);
            using (var chunks = m_RunQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = chunks[c].GetNativeArray(ref geometryHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        int kind = m_Ownership.KindOf(prefabRefs[i].m_Prefab);
                        if (kind < 0)
                            continue;
                        Game.Areas.Geometry g = geometries[i];
                        m_Scratch.Add(new RunRow
                        {
                            Entity = entities[i],
                            Kind = kind,
                            Features = m_RunProps.FeaturesOf(entities[i], kind),
                            AreaM2 = Finite(g.m_SurfaceArea),
                            TopY = Finite(g.m_Bounds.max.y),
                            CenterX = Finite(g.m_CenterPosition.x),
                            CenterZ = Finite(g.m_CenterPosition.z),
                        });
                    }
                }
            }

            for (int r = 0; r < m_Scratch.Count; r++)
            {
                RunRow row = m_Scratch[r];
                SkiRunName pick = EntityManager.HasComponent<SkiRunName>(row.Entity)
                    ? EntityManager.GetComponentData<SkiRunName>(row.Entity) : new SkiRunName(SkiRunNameRules.None, SkiRunNameRules.FormNone);
                row.NameIndex = pick.m_Index;
                row.NameForm = pick.m_Form;
                if (EntityManager.HasBuffer<Game.Areas.Node>(row.Entity))
                {
                    DynamicBuffer<Game.Areas.Node> nodes =
                        EntityManager.GetBuffer<Game.Areas.Node>(row.Entity, isReadOnly: true);
                    RingMetrics(nodes, out row.NodeLengthM, out row.NodeDropM, out row.NodeTopM, out row.NodeBottomM,
                        out row.Key, out row.YKey, out row.NodeTopPos);
                    // The outline's xz for the served rule's run links. Same x/z Key and node count as this run's row of the last rebuild = the
                    // same outline, so the code keeps its array and the link graph's reference test sees "unchanged". Allocation happens only when
                    // an outline moved. m_RowIndex / m_Rows still hold the last rebuild here.
                    if (m_RowIndex.TryGetValue(row.Entity, out int prevIndex) && prevIndex >= 0 && prevIndex < m_Rows.Count &&
                        m_Rows[prevIndex].Entity == row.Entity && m_Rows[prevIndex].OutlineXZ != null &&
                        m_Rows[prevIndex].OutlineXZ.Length == nodes.Length && m_Rows[prevIndex].Key == row.Key)
                    {
                        row.OutlineXZ = m_Rows[prevIndex].OutlineXZ;
                    }
                    else
                    {
                        var xz = new float2[nodes.Length];
                        for (int k = 0; k < xz.Length; k++)
                            xz[k] = nodes[k].m_Position.xz;
                        row.OutlineXZ = xz;
                    }
                    // Their heights for the served rule's downhill links: the same reuse, keyed on YKey too. A GroundHeightSystem re-snap
                    // moves y only, so the xz array is kept and the height array is new. The link graph then rebuilds.
                    if (m_RowIndex.TryGetValue(row.Entity, out int prevY) && prevY >= 0 && prevY < m_Rows.Count &&
                        m_Rows[prevY].Entity == row.Entity && m_Rows[prevY].OutlineY != null &&
                        m_Rows[prevY].OutlineY.Length == nodes.Length && m_Rows[prevY].Key == row.Key && m_Rows[prevY].YKey == row.YKey)
                    {
                        row.OutlineY = m_Rows[prevY].OutlineY;
                    }
                    else
                    {
                        var y = new float[nodes.Length];
                        for (int k = 0; k < y.Length; k++)
                            y[k] = nodes[k].m_Position.y;
                        row.OutlineY = y;
                    }
                }
                // The bound test, returning the sign (the run's resort)
                row.Bound = m_Ownership.TryGetBoundGate(row.Entity, out row.Gate);
                m_Scratch[r] = row;
            }
        }

        /// <summary>Computes LengthRect (SkiSlopeClassifier.EquivalentRectLength, the same arithmetic in double), the node height span and
        /// extremes over an area's Node ring, and the rating cache keys. Key = FNV-1a over every node's x and z float bits, YKey over
        /// the y bits, each closed with the node count (SkiRunRatingRules: the same bytes in the same order as KeyOf). The ring is
        /// implicitly closed (the last node connects to the first). A duplicated vertex adds nothing to P or A. Non-finite results
        /// read 0. <paramref name="topNode"/> = the node whose y set the maximum (the first one on a tie), x/y/z as stored: the
        /// lift-served test point (RunFacts.TopNode). float3.zero for an empty ring.</summary>
        private static void RingMetrics(DynamicBuffer<Game.Areas.Node> nodes, out float length, out float drop,
            out float top, out float bottom, out ulong key, out ulong yKey, out float3 topNode)
        {
            length = 0f;
            drop = 0f;
            top = 0f;
            bottom = 0f;
            topNode = float3.zero;
            SkiRunRatingRules.Begin(out key, out yKey);
            int m = nodes.Length;
            if (m < 1)
            {
                SkiRunRatingRules.Finish(ref key, ref yKey, 0);
                return;
            }
            float minY = nodes[0].m_Position.y, maxY = minY;
            int topIndex = 0;
            double x0 = nodes[0].m_Position.x, z0 = nodes[0].m_Position.z;
            double per = 0.0, s2 = 0.0;
            for (int k = 0; k < m; k++)
            {
                float3 pk = nodes[k].m_Position;
                float3 pl = nodes[k + 1 < m ? k + 1 : 0].m_Position;
                SkiRunRatingRules.AddNode(ref key, ref yKey, pk);
                if (pk.y < minY) minY = pk.y;
                if (pk.y > maxY)
                {
                    maxY = pk.y;
                    topIndex = k;
                }
                double ax = pk.x - x0, az = pk.z - z0, bx = pl.x - x0, bz = pl.z - z0;
                double dx = bx - ax, dz = bz - az;
                per += Math.Sqrt(dx * dx + dz * dz);
                s2 += ax * bz - bx * az;
            }
            SkiRunRatingRules.Finish(ref key, ref yKey, m);
            double area = Math.Abs(0.5 * s2);
            double disc = per * per - 16.0 * area;
            if (!(disc > 0.0))
                disc = 0.0;
            double len = (per + Math.Sqrt(disc)) * 0.25;
            length = Finite((float)len);
            drop = Finite(maxY - minY);
            top = Finite(maxY);
            bottom = Finite(minY);
            topNode = nodes[topIndex].m_Position;
        }


        private static float Finite(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        }

        private static ulong Mix(ulong hash, uint value)
        {
            for (int b = 0; b < 4; b++)
            {
                hash ^= (value >> (8 * b)) & 0xFFu;
                hash *= 1099511628211UL;
            }
            return hash;
        }


        /// <summary>Copies the cached rating into a row when it belongs to the row's current outline (same x/z Key). Otherwise the row
        /// is pending and shows the node estimates. A rating from an older terrain epoch or older node heights (YKey) is still shown
        /// until its re-rate lands (engine notes Invalidation). A final "cannot be rated" state shows band -2 (engine notes Row band).</summary>
        private void ApplyRating(ref RunRow row)
        {
            if (m_Ratings.TryGetValue(row.Entity, out RunRating r) && r.Key == row.Key)
            {
                row.RateState = r.State;
                if (r.State == RateRated)
                {
                    row.Band = SkiRunRatingRules.RowBand(r.State, r.Band);
                    if (row.Kind == (int)RunKind.CatTrack)
                        row.Band = SkiRunRatingRules.CatTrackBand(row.Band);   // green by construction (the tool grades it under 25 %)
                    row.GradePct = r.RatingGrade * 100f;
                    row.SteepestPct = r.SteepestGrade * 100f;
                    row.TopM = r.TopM;
                    row.BottomM = r.BottomM;
                    row.LengthM = r.LengthM;
                    row.DropM = r.DropM;
                    row.Approx = false;
                    return;
                }
            }
            else
            {
                row.RateState = RatePending;
            }
            row.Band = SkiRunRatingRules.RowBand(row.RateState, -1);
            row.GradePct = -1f;
            row.SteepestPct = -1f;
            row.TopM = row.NodeTopM;
            row.BottomM = row.NodeBottomM;
            row.LengthM = row.NodeLengthM;
            row.DropM = row.NodeDropM;
            row.Approx = true;
        }

        /// <summary>Rebuild-time queue maintenance (engine notes Queue / Invalidation): drops cache and queue entries of runs that left, and
        /// queues every row that needs a (re-)rating. Before the warm-up gate opens nothing is queued (RateStep seeds the warm-up then).</summary>
        private void UpdateQueue()
        {
            // Rows that left: drop their cache entries (queued entries are skipped on dequeue)
            m_ScratchEntities.Clear();
            foreach (var pair in m_Ratings)
            {
                if (!m_RowIndex.ContainsKey(pair.Key))
                    m_ScratchEntities.Add(pair.Key);
            }
            for (int i = 0; i < m_ScratchEntities.Count; i++)
                m_Ratings.Remove(m_ScratchEntities[i]);
            m_ScratchEntities.Clear();
            foreach (var pair in m_Queued)
            {
                if (!m_RowIndex.ContainsKey(pair.Key))
                    m_ScratchEntities.Add(pair.Key);
            }
            for (int i = 0; i < m_ScratchEntities.Count; i++)
            {
                bool warmup = m_Queued.TryGetValue(m_ScratchEntities[i], out byte reason) && reason == ReasonWarmup;
                m_Queued.Remove(m_ScratchEntities[i]);
                if (warmup)
                    WarmupProgress(0f);   // a warm-up entry whose run is gone counts as done
            }
            if (!m_WarmupSeeded)
                return;
            for (int r = 0; r < m_Rows.Count; r++)
                QueueRowIfNeeded(m_Rows[r], ReasonBind);
        }

        /// <summary>Queues one row (SkiRunRatingRules.NeedsRating):
        /// - no cache entry -> <paramref name="missingReason"/> (bind after the warm-up, warm-up while seeding)
        /// - x/z Key mismatch -> edit (front)
        /// - YKey mismatch or older terrain epoch -> terrain (back)
        /// A row already queued keeps its place unless SkiRunRatingRules.Replaces says the new reason takes over (a front reason over a
        /// background one, never bind over warm-up). The dequeue skips the stale back entry.</summary>
        private void QueueRowIfNeeded(RunRow row, byte missingReason)
        {
            bool cached = m_Ratings.TryGetValue(row.Entity, out RunRating r);
            byte reason = SkiRunRatingRules.NeedsRating(cached, r.Key, r.YKey, r.TerrainEpoch, row.Key, row.YKey,
                m_TerrainEpoch, missingReason);
            if (reason == SkiRunRatingRules.ReasonNone)
                return;
            Enqueue(row.Entity, reason);
        }

        /// <summary><paramref name="requeue"/> = RateOne putting an entry back (heights not readable this frame): it was counted when
        /// first queued.</summary>
        private void Enqueue(Entity run, byte reason, bool requeue = false)
        {
            bool front = SkiRunRatingRules.IsFront(reason);
            bool promoted = false;
            if (m_Queued.TryGetValue(run, out byte queued))
            {
                if (!SkiRunRatingRules.Replaces(queued, reason))
                    return;
                promoted = queued == ReasonWarmup;
            }
            m_Queued[run] = reason;
            if (reason == ReasonWarmup && !requeue)
                m_WarmupQueued++;
            (front ? m_Front : m_Back).Enqueue(new QueueEntry { Run = run, Reason = reason });
            if (promoted)
                WarmupProgress(0f);   // promoted out of the warm-up set: counts as done there
        }

        /// <summary>One rating at most (engine notes Queue). Seeds the warm-up once the gate opens. Runs the backstop timer. Picks the front
        /// queue first, the back queue only within the duty cycle. Runs hub open or closed. <paramref name="open"/> picks the backstop
        /// interval and the duty factor.</summary>
        private void RateStep(float now, bool open)
        {
            if (m_Frame < Tuning.WarmupFrames)
                return;
            if (!m_WarmupSeeded)
            {
                if (!HeightsReady(out TerrainHeightData hd0))
                {
                    if (!m_HeightsLogged)
                    {
                        m_HeightsLogged = true;
                        Mod.Log.Info($"Catalog [rate] terrain heights not usable yet (created {hd0.isCreated}, resolution " +
                                     $"{hd0.resolution.x}, need {SkiRunDrawFeedbackSystem.Tuning.MinHeightmapResolution}) — runs " +
                                     "stay 'being rated' until they are; retried every frame, hub open or closed (logged once per load)");
                    }
                    return;
                }
                m_WarmupSeeded = true;
                m_WarmupStartFrame = m_Frame;
                for (int r = 0; r < m_Rows.Count; r++)
                    QueueRowIfNeeded(m_Rows[r], ReasonWarmup);
                if (m_WarmupQueued == 0)
                    LogWarmupDone();
            }

            // Backstop: the oldest-rated run, when nothing else is waiting. Every BackstopS with the hub open, every ClosedBackstopS with
            // it closed. A timer armed while closed is moved earlier when the hub opens, so the open cadence stays the same.
            float backstopS = open ? Tuning.BackstopS : Tuning.ClosedBackstopS;
            if (open && m_NextBackstop > now + Tuning.BackstopS)
                m_NextBackstop = now + Tuning.BackstopS;
            if (now >= m_NextBackstop)
            {
                m_NextBackstop = now + backstopS;
                if (m_Queued.Count == 0)
                    QueueOldestRated();
            }

            QueueEntry entry;
            bool background;
            if (!TryDequeue(m_Front, out entry))
            {
                if (now < m_NextBackgroundStart || !TryDequeue(m_Back, out entry))
                    return;
                background = true;
            }
            else
            {
                background = false;
            }
            RateOne(entry, background, now, open);
        }

        /// <summary>Next live entry of a queue: skips entries whose run left the rows or whose queued reason changed (promoted to the
        /// front). Removes the run from m_Queued on success.</summary>
        private bool TryDequeue(Queue<QueueEntry> queue, out QueueEntry entry)
        {
            while (queue.Count > 0)
            {
                entry = queue.Dequeue();
                if (!m_Queued.TryGetValue(entry.Run, out byte reason) || reason != entry.Reason)
                    continue;
                m_Queued.Remove(entry.Run);
                if (!m_RowIndex.ContainsKey(entry.Run))
                {
                    if (entry.Reason == ReasonWarmup)
                        WarmupProgress(0f);
                    continue;
                }
                return true;
            }
            entry = default;
            return false;
        }

        private void QueueOldestRated()
        {
            Entity oldest = Entity.Null;
            float oldestAt = float.MaxValue;
            foreach (var pair in m_Ratings)
            {
                if (pair.Value.RatedAt < oldestAt && m_RowIndex.ContainsKey(pair.Key))
                {
                    oldestAt = pair.Value.RatedAt;
                    oldest = pair.Key;
                }
            }
            if (oldest != Entity.Null)
                Enqueue(oldest, ReasonBackstop);
        }

        private bool HeightsReady(out TerrainHeightData hd)
        {
            hd = m_TerrainSystem.GetHeightData();   // fresh, never cached (feedback system engine notes, Terrain read)
            return hd.isCreated && hd.resolution.x >= SkiRunDrawFeedbackSystem.Tuning.MinHeightmapResolution;
        }

        /// <summary>Rates one run, stores the result, patches its row, logs (bind / edit) or counts (warm-up / terrain / backstop). The
        /// duty cycle and the closed-panel cost counters follow <paramref name="open"/>. RatingRevision follows the patched row.</summary>
        private void RateOne(QueueEntry entry, bool background, float now, bool open)
        {
            int idx = m_RowIndex[entry.Run];
            RunRow row = m_Rows[idx];
            if (!HeightsReady(out TerrainHeightData hd))
            {
                // Not readable this frame: put it back (at the end of its queue). The system retries later
                Enqueue(entry.Run, entry.Reason, requeue: true);
                return;
            }

            long t0 = Stopwatch.GetTimestamp();
            int n = 0;
            if (EntityManager.HasBuffer<Game.Areas.Node>(entry.Run))
            {
                DynamicBuffer<Game.Areas.Node> nodes = EntityManager.GetBuffer<Game.Areas.Node>(entry.Run, isReadOnly: true);
                n = nodes.Length;
                if (m_RateNodes.Length < n)
                    m_RateNodes = new float3[math.max(n, m_RateNodes.Length * 2)];
                for (int k = 0; k < n; k++)
                    m_RateNodes[k] = nodes[k].m_Position;
            }

            float texel = 1f / hd.scale.x;                            // TerrainSystem.cs:2619-2620
            var origin = new double2(-hd.offset.x, -hd.offset.z);    // texel centers, TerrainUtils.cs:35-38
            var sampler = new TerrainHeightSampler(hd);
            RateStatus status = SkiRunRater.Rate(m_RateNodes, n, origin, ref sampler, texel, 0, m_RateGrid, null, m_RateResult,
                out int m, out float _, out float _, out RateDetail detail);
            long t1 = Stopwatch.GetTimestamp();
            float ms = (float)((t1 - t0) * 1000.0 / Stopwatch.Frequency);

            SlopeResult res = m_RateResult;
            // The keys of the nodes actually rated. An edit since the last rebuild shows up as a mismatch at the next rebuild, and the system
            // re-rates it then, as "edit" or "terrain"
            SkiRunRatingRules.Keys(m_RateNodes, n, out ulong rateKey, out ulong rateYKey);
            var rating = new RunRating
            {
                Key = rateKey,
                YKey = rateYKey,
                TerrainEpoch = m_TerrainEpoch,
                StepMultiple = m,
                RateMs = ms,
                RatedAt = now,
                Band = -1,
            };
            switch (status)
            {
                case RateStatus.Rated:
                    rating.State = RateRated;
                    rating.Band = (sbyte)res.Band;
                    rating.RatingGrade = res.RatingGrade;
                    rating.SteepestGrade = res.SteepestGrade;   // = RatingGrade in polyline mode (Classify)
                    rating.MeanGrade = res.MeanGrade;
                    rating.Connected = res.Connected;
                    rating.DropM = res.Drop;
                    rating.TopM = res.MaxHeight;
                    rating.BottomM = res.MinHeight;
                    rating.LengthM = res.Length;
                    break;
                case RateStatus.NoTerrain:
                    rating.State = RateNoTerrain;
                    break;
                case RateStatus.TooLarge:
                    rating.State = RateTooLarge;
                    break;
                default:
                    rating.State = RateFailed;
                    break;
            }
            m_Ratings[entry.Run] = rating;
            Ratings++;
            LastRateMs = ms;
            if (ms > MaxRateMs)
                MaxRateMs = ms;
            if (background)
                m_NextBackgroundStart = SkiRunRatingRules.NextBackgroundStart(now, ms, open);   // open: = now + 8 x ms
            if (!open)
            {
                // The panel-closed rating cost ("Catalog [service]", once per load)
                m_ClosedRatings++;
                m_ClosedRatingMsSum += ms;
                if (ms > m_ClosedRatingMsMax)
                    m_ClosedRatingMsMax = ms;
            }

            // Once per load, with the numbers. Read before the shrink below
            if (ms > SkiRunDrawFeedbackSystem.Tuning.SlowRecomputeWarnMs && !m_SlowRateWarned)
            {
                m_SlowRateWarned = true;
                string reasonName = entry.Reason < ReasonNames.Length ? ReasonNames[entry.Reason] : "?";
                string lattice = SkiRunRater.IsPlanOk(detail.PlanStatus)
                    ? $"lattice {m_RateGrid.Nx}×{m_RateGrid.Nz} = {(long)m_RateGrid.Nx * m_RateGrid.Nz} cells"
                    : $"no lattice (plan {detail.PlanStatus})";
                Mod.Log.Warn($"Catalog [rate] slow rating {ms:F1} ms (above the drawing tooltip's " +
                             $"SlowRecomputeWarnMs={SkiRunDrawFeedbackSystem.Tuning.SlowRecomputeWarnMs} ms): run '{row.Name}' " +
                             $"{entry.Run.Index}:{entry.Run.Version} ({reasonName}{(background ? ", background" : "")}), " +
                             $"{n} node(s), {detail.AreaM2:F0} m², step {detail.StepM:F1} m (m={m}), {lattice}, " +
                             $"plan {detail.PlanMs:F2} ms, {detail.Sampled} height(s) sampled, status {detail.Status} — " +
                             "main thread, one rating per UIUpdate frame, hub open or closed (Round E1); the " +
                             $"backstop re-rates the oldest run every BackstopS={Tuning.BackstopS:0} s open, " +
                             $"ClosedBackstopS={Tuning.ClosedBackstopS:0} s closed; logged once per load");
            }

            // Memory (engine notes): a huge lattice is not kept around
            if ((long)m_RateGrid.Nx * m_RateGrid.Nz > Tuning.ShrinkCells)
            {
                m_RateGrid = new SlopeGrid();
                m_RateResult = new SlopeResult();
            }

            // Patch the row in place (no rebuild): rating fields only. The hash decides the revision
            ApplyRating(ref row);
            m_Rows[idx] = row;
            ulong hash = ComputeHash(m_Rows, Signs, out int rated);
            RatedCount = rated;
            if (hash != m_LastHash)
            {
                m_LastHash = hash;
                Revision++;
            }
            UpdateFactsRevision();   // the simulation's facts (state, band, heights) of this row

            // The text is formatted only when logged or read (LastRateLine is lazy)
            m_LastRateName = row.Name;
            m_LastRateEntity = row.Entity;
            m_LastRating = rating;
            m_LastRateDetail = detail;
            m_LastRateReason = entry.Reason < ReasonNames.Length ? ReasonNames[entry.Reason] : "?";
            m_LastRateDirty = true;
            if (entry.Reason == ReasonBind || entry.Reason == ReasonEdit)
                LogRateLine(now);
            if (entry.Reason == ReasonWarmup)
                WarmupProgress(ms);
        }

        private static string RateText(string name, Entity run, RunRating r, RateDetail detail, string reason)
        {
            string head = $"run '{name}' {run.Index}:{run.Version}: ";
            if (r.State != RateRated)
                return head + $"not rated — {detail.Status} (plan {detail.PlanStatus}), m={r.StepMultiple}, {r.RateMs:F1} ms ({reason})";
            string band = r.Band >= 0 && r.Band < BandNames.Length ? BandNames[r.Band] : "?";
            return head + $"{band} {r.RatingGrade * 100f:F1} % (steepest {r.SteepestGrade * 100f:F1} %), drop {r.DropM:F0} m, " +
                   $"length {r.LengthM:F0} m, m={r.StepMultiple}, {r.RateMs:F1} ms ({reason})" +
                   (r.Connected ? "" : " — outline broken, P90 fallback");
        }

        /// <summary>Writes LastRateLine as "Catalog [rate] ...", at most RateLinesPerWindow per RateLineWindowS window. The code counts suppressed
        /// lines and reports them with the next logged one</summary>
        private void LogRateLine(float now)
        {
            if (now - m_RateLineWindowStart >= Tuning.RateLineWindowS)
            {
                m_RateLineWindowStart = now;
                m_RateLinesInWindow = 0;
            }
            if (m_RateLinesInWindow >= Tuning.RateLinesPerWindow)
            {
                m_RateLinesSuppressed++;
                return;
            }
            m_RateLinesInWindow++;
            string suffix = m_RateLinesSuppressed > 0
                ? $" (+{m_RateLinesSuppressed} earlier bind/edit rating(s) not logged: more than {Tuning.RateLinesPerWindow} per {Tuning.RateLineWindowS:0} s)"
                : "";
            m_RateLinesSuppressed = 0;
            Mod.Log.Info("Catalog [rate] " + LastRateLine + suffix);
        }

        private void WarmupProgress(float ms)
        {
            m_WarmupDone++;
            m_WarmupTotalMs += ms;
            if (ms > m_WarmupMaxMs)
                m_WarmupMaxMs = ms;
            if (!m_WarmupLogged && m_WarmupDone >= m_WarmupQueued)
                LogWarmupDone();
        }

        /// <summary>The one "[rate-warmup]" line per load. The factor shown is the panel state's at the moment of writing
        /// (m_WasDemanded = this frame's Demand). The tail says the service is ready, or that the 90 s deadline had already turned it
        /// ready (ServiceTick).</summary>
        private void LogWarmupDone()
        {
            m_WarmupLogged = true;
            float factor = m_WasDemanded ? SkiRunRatingRules.DutyFactorOpen : SkiRunRatingRules.DutyFactorClosed;
            Mod.Log.Info($"Catalog [rate-warmup] {m_WarmupQueued} run(s) in {m_Frame - m_WarmupStartFrame} frames, total " +
                         $"{m_WarmupTotalMs:F1} ms, max {m_WarmupMaxMs:F1} ms (background duty cycle ×{factor:0.##}; " +
                         $"{RatedCount} of {m_Rows.Count} row(s) rated)" +
                         (m_WarmupDeadlineHit ? " — warm-up finished after the deadline" : " — rating service ready"));
        }

        /// <summary>The service state machine (SkiRunRatingRules.NextServiceState: warm-up done, or WarmupDeadlineS after the load with
        /// one WARN) and the one-time "Catalog [service]" cost line after ServiceCostLogAfterRebuilds panel-closed rebuilds. Per
        /// frame: one call, one byte compare, one int compare.</summary>
        private void ServiceTick(float now)
        {
            byte next = SkiRunRatingRules.NextServiceState((byte)ServiceState, m_WarmupLogged, now, m_ArmedAt);
            if (next != (byte)ServiceState)
            {
                if (!m_WarmupLogged)
                {
                    m_WarmupDeadlineHit = true;
                    bool heightsReady = HeightsReady(out TerrainHeightData _);
                    Mod.Log.Warn($"Catalog [rate-warmup] WARN: warm-up not finished {SkiRunRatingRules.WarmupDeadlineS:0} s " +
                                 $"after the load ({m_WarmupDone} of {m_WarmupQueued} run(s) rated, {RatingsPending} queued, " +
                                 $"heights ready {(heightsReady ? "true" : "false")}) — the rating service reports ready; ski " +
                                 "areas whose runs are still pending keep their saved value until the attraction model's own " +
                                 "cap (1024 ticks)");
                }
                ServiceState = (RatingServiceState)next;
                RatingRevision++;
            }
            if (!m_ServiceCostLogged && m_CostRebuilds >= Tuning.ServiceCostLogAfterRebuilds)
            {
                // Rebuilds with at least one run only
                m_ServiceCostLogged = true;
                float spanS = m_CostSince >= 0f ? now - m_CostSince : 0f;
                float rebuildMean = m_CostRebuilds > 0 ? m_CostRebuildMsSum / m_CostRebuilds : 0f;
                float ratingMean = m_ClosedRatings > 0 ? m_ClosedRatingMsSum / m_ClosedRatings : 0f;
                Mod.Log.Info($"Catalog [service] panel closed: {m_CostRebuilds} rebuild(s) with runs in {spanS:F0} s, mean " +
                             $"{rebuildMean:F2} ms, max {m_CostRebuildMsMax:F2} ms ({m_ClosedRebuilds - m_CostRebuilds} of an empty " +
                             $"list not counted); {m_ClosedRatings} rating(s) with the " +
                             $"panel closed, mean {ratingMean:F1} ms, max {m_ClosedRatingMsMax:F1} ms " +
                             $"(ClosedHeartbeatS={Tuning.ClosedHeartbeatS:0.##}, ClosedBackstopS={Tuning.ClosedBackstopS:0.##}, " +
                             $"DutyFactorClosed={SkiRunRatingRules.DutyFactorClosed:0.##}, " +
                             $"ClosedMinGapS={SkiRunRatingRules.ClosedMinGapS:0.##}) — logged once per load");
            }
        }


        private void RefreshTemplates()
        {
            if (m_TemplatesGeneration == m_NamesGeneration && m_Templates[0] != null)
                return;
            m_TemplatesGeneration = m_NamesGeneration;
            m_NameCache.Clear();
            Array.Clear(m_TrailNames, 0, m_TrailNames.Length);
            var dictionary = GameManager.instance?.localizationManager?.activeDictionary;
            for (int k = 0; k < m_Templates.Length; k++)
            {
                string template = null;
                if (dictionary != null && dictionary.TryGetValue(DefaultNameKeys[k], out string value) &&
                    !string.IsNullOrEmpty(value))
                    template = value;
                m_Templates[k] = template ?? DefaultNameEnglish[k];
            }
            for (int k = 0; k < m_FormTemplates.Length; k++)
            {
                string template = null;
                if (dictionary != null && dictionary.TryGetValue(FormKeys[k], out string value) &&
                    value != null && value.IndexOf(NamePlaceholder, StringComparison.Ordinal) >= 0)
                    template = value;
                m_FormTemplates[k] = template ?? FormEnglish[k];
            }
        }

        /// <summary>0 for a piste, 1 for a cat track: the default-name template and the ordinal's group.</summary>
        private static int NameGroup(in RunRow row) => row.Features.IsCatTrack ? 1 : 0;

        /// <summary>A run's trail name in its form (localized, re-resolved when the language changes), else the group's "{N}" template.</summary>
        private string DefaultName(in RunRow row)
        {
            int group = NameGroup(in row);
            if (row.NameIndex >= 0 && row.NameIndex < SkiRunNameRules.Count)
            {
                int form = row.NameForm <= SkiRunNameRules.FormLower ? row.NameForm : SkiRunNameRules.FormNone;
                int slot = row.NameIndex * 3 + form;
                string trail = m_TrailNames[slot];
                if (trail == null)
                {
                    var dictionary = GameManager.instance?.localizationManager?.activeDictionary;
                    trail = dictionary != null && dictionary.TryGetValue(SkiRunNameRules.Key(row.NameIndex), out string value) &&
                            !string.IsNullOrEmpty(value) ? value : "";
                    if (trail.Length > 0 && form != SkiRunNameRules.FormNone)
                        trail = m_FormTemplates[form - 1].Replace(NamePlaceholder, trail);
                    m_TrailNames[slot] = trail;
                }
                if (trail.Length > 0)
                    return trail;
            }
            return DefaultName(group, row.Ordinal);
        }

        private string DefaultName(int kind, int ordinal)
        {
            if (kind < 0 || kind >= m_Templates.Length)
                return "?";
            int key = (kind << 16) | (ordinal & 0xFFFF);
            if (m_NameCache.TryGetValue(key, out string cached))
                return cached;
            string number = ordinal.ToString(CultureInfo.InvariantCulture);
            string template = m_Templates[kind];
            string name = template.IndexOf(OrdinalPlaceholder, StringComparison.Ordinal) >= 0
                ? template.Replace(OrdinalPlaceholder, number)
                : template + " " + number;
            m_NameCache[key] = name;
            return name;
        }


        /// <summary>Top height descending, then center x, center z, entity index, version</summary>
        private static int Spatial(in RunRow a, in RunRow b)
        {
            int c = b.TopY.CompareTo(a.TopY);
            if (c != 0) return c;
            c = a.CenterX.CompareTo(b.CenterX);
            if (c != 0) return c;
            c = a.CenterZ.CompareTo(b.CenterZ);
            if (c != 0) return c;
            c = a.Entity.Index.CompareTo(b.Entity.Index);
            if (c != 0) return c;
            return a.Entity.Version.CompareTo(b.Entity.Version);
        }

        /// <summary>Gate (Index, Version, Null first), kind, then Spatial: the numbering order</summary>
        private sealed class OrdinalOrder : IComparer<RunRow>
        {
            public static readonly OrdinalOrder Instance = new OrdinalOrder();

            public int Compare(RunRow a, RunRow b)
            {
                if (a.Gate.Index != b.Gate.Index)
                    return a.Gate.Index < b.Gate.Index ? -1 : 1;
                if (a.Gate.Version != b.Gate.Version)
                    return a.Gate.Version < b.Gate.Version ? -1 : 1;
                int ga = NameGroup(in a), gb = NameGroup(in b);
                if (ga != gb)
                    return ga < gb ? -1 : 1;
                return Spatial(a, b);
            }
        }

        /// <summary>Linked first, then Spatial, then kind: the order the UI shows</summary>
        private sealed class DisplayOrder : IComparer<RunRow>
        {
            public static readonly DisplayOrder Instance = new DisplayOrder();

            public int Compare(RunRow a, RunRow b)
            {
                if (a.Bound != b.Bound)
                    return a.Bound ? -1 : 1;
                int c = Spatial(a, b);
                if (c != 0) return c;
                return a.Kind.CompareTo(b.Kind);
            }
        }


        /// <summary>Multi-line description for the hub's DUMP (event time only)</summary>
        public void AppendDump(StringBuilder sb)
        {
            sb.Append("  catalog: armed ").Append(m_Armed)
              .Append(", disabled ").Append(m_Disabled)
              .Append(", demand ").Append(Demand)
              .Append(", revision ").Append(Revision)
              .Append(", rebuilds ").Append(Rebuilds).Append(" (").Append(RebuildsChanged).Append(" changed; by reason:");
            // ", rebuilds {n} ({c} changed; by reason: {why} {k}, ...)"
            bool anyReason = false;
            foreach (var pair in m_RebuildsByReason)
            {
                sb.Append(anyReason ? ", " : " ").Append(pair.Key).Append(' ').Append(pair.Value);
                anyReason = true;
            }
            if (!anyReason)
                sb.Append(" none");
            sb.Append(')')
              .Append(", last '").Append(LastRebuildWhy).Append("' ").Append(LastRebuildMs.ToString("F2", CultureInfo.InvariantCulture))
              .Append(" ms (max ").Append(MaxRebuildMs.ToString("F2", CultureInfo.InvariantCulture)).Append(')')
              .Append("; signs ").Append(Signs)
              .Append(", runs ").Append(m_Rows.Count).Append(" (").Append(RunsBound).Append(" linked, ")
              .Append(RunsUnbound).Append(" not linked)")
              .Append(", area ").Append(AreaM2.ToString("F0", CultureInfo.InvariantCulture)).Append(" m²\n");
            sb.Append("  ratings: ").Append(RatedCount).Append(" rated of ").Append(m_Rows.Count)
              .Append(", queued ").Append(RatingsPending).Append(" (front ").Append(m_Front.Count).Append(", back ").Append(m_Back.Count).Append(')')
              .Append(", done ").Append(Ratings)
              .Append(", last ").Append(LastRateMs.ToString("F1", CultureInfo.InvariantCulture))
              .Append(" ms (max ").Append(MaxRateMs.ToString("F1", CultureInfo.InvariantCulture)).Append(')')
              .Append(", terrain epoch ").Append(m_TerrainEpoch)
              .Append(", warm-up ").Append(m_WarmupSeeded ? (m_WarmupLogged ? "done" : "running") : "not started")
              .Append(' ').Append(m_WarmupDone).Append('/').Append(m_WarmupQueued)
              .Append(", frame ").Append(m_Frame)
              .Append("; last: ").Append(LastRateLine).Append('\n');
            sb.Append("  rating service: ").Append(ServiceState)
              .Append(", rating revision ").Append(RatingRevision)
              .Append(", panel ").Append(Demand ? "open" : "closed")
              .Append(", closed rebuilds ").Append(m_ClosedRebuilds)
              .Append(" (mean ").Append((m_ClosedRebuilds > 0 ? m_ClosedRebuildMsSum / m_ClosedRebuilds : 0f).ToString("F2", CultureInfo.InvariantCulture))
              .Append(" ms, max ").Append(m_ClosedRebuildMsMax.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms)")
              .Append(", closed ratings ").Append(m_ClosedRatings)
              .Append(" (mean ").Append((m_ClosedRatings > 0 ? m_ClosedRatingMsSum / m_ClosedRatings : 0f).ToString("F1", CultureInfo.InvariantCulture))
              .Append(" ms, max ").Append(m_ClosedRatingMsMax.ToString("F1", CultureInfo.InvariantCulture)).Append(" ms)")
              .Append(", warm-up deadline ").Append(m_WarmupDeadlineHit ? "hit" : "not hit").Append('\n');
            int shown = Math.Min(m_Rows.Count, Tuning.DumpRowsMax);
            // The injection's per-run snow-made ledger and protection spell, one clause on the run's own row (DUMP time only)
            SkiSnowInjectionSystem snow = World.GetExistingSystemManaged<SkiSnowInjectionSystem>();
            for (int i = 0; i < shown; i++)
            {
                RunRow row = m_Rows[i];
                sb.Append("    ").Append(i + 1).Append(". \"").Append(row.Name).Append('"')
                  .Append(row.CustomName ? " (custom)" : "")
                  .Append(' ').Append(row.Kind >= 0 && row.Kind < KindNames.Length ? KindNames[row.Kind] : "?")
                  .Append(" #").Append(row.Ordinal)
                  .Append(", ").Append(row.Bound ? "sign " + row.Gate.Index + ":" + row.Gate.Version : "NOT linked")
                  .Append(", ").Append(row.RateState == RateRated
                      ? (row.Band >= 0 && row.Band < BandNames.Length ? BandNames[row.Band] : "?") + " " +
                        row.GradePct.ToString("F1", CultureInfo.InvariantCulture) + " % (steepest " +
                        row.SteepestPct.ToString("F1", CultureInfo.InvariantCulture) + " %)"
                      : "rate state " + row.RateState)
                  .Append(", length ").Append(row.LengthM.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(" m, drop ").Append(row.DropM.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(row.Approx ? " m (node estimates)" : " m")
                  .Append(", area ").Append(row.AreaM2.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(" m², top ").Append(row.TopM.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(" m, bottom ").Append(row.BottomM.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(" m, entity ").Append(row.Entity.Index).Append(':').Append(row.Entity.Version)
                  .Append(SnowClause(snow, row)).Append('\n');
            }
            if (m_Rows.Count > shown)
                sb.Append("    … ").Append(m_Rows.Count - shown).Append(" more row(s) not listed (DumpRowsMax)\n");
        }

        /// <summary>", snow made 840 m3 this season / 1200 m3 total (+ 60 / 90 m3 held by protection), protection active 12.5 h used"
        /// for a gun or groomed run that gained snow, else nothing. The hub tooltip has no text field for it (a bound field would be a
        /// contract bump), so this DUMP row and the Snow [made] log line carry the ledger. Since this load, not saved.</summary>
        private static string SnowClause(SkiSnowInjectionSystem snow, RunRow row)
        {
            if (snow == null)
                return "";
            var ci = CultureInfo.InvariantCulture;
            string text = "";
            if (snow.TryGetRunLedger(row.Entity, out RunSnowRules.RunLedger ledger))
                text += ", snow made " + ledger.MadeSeasonM3.ToString("F0", ci) + " m3 this season / " +
                        ledger.MadeTotalM3.ToString("F0", ci) + " m3 total (" + ledger.HeldSeasonM3.ToString("F0", ci) + " / " +
                        ledger.HeldTotalM3.ToString("F0", ci) + " m3 put back by protection)";
            if (snow.TryGetRunProtection(row.Entity, out RunSnowRules.ProtectRun spell) &&
                spell.State != RunSnowRules.ProtectState.Idle)
                text += ", protection " + spell.State + " (" + spell.HoursUsed.ToString("F1", ci) + " game h used)";
            // The guns (SnowGunPlumeRules): state, last measured pack, and whether they ran in the last pass (= plume)
            if (snow.TryGetRunGunState(row.Entity, out SnowGunPlumeRules.GunState guns, out float packM))
            {
                snow.TryGetRunGunsRunning(row.Entity, out bool running);
                text += ", guns " + guns + " (pack " + (float.IsNaN(packM) ? "not measured" : packM.ToString("F2", ci) + " m") +
                        ", stop " + RunSnowRules.GunStopDepthM.ToString("F2", ci) + " m), " +
                        (running ? "running now (plumes on above the snow line)" : "not running now (no plume)");
            }
            return text;
        }

        private void HandleException(Exception e, string where)
        {
            m_Disabled = true;
            if (ServiceState != RatingServiceState.Failed)
            {
                // A soft trigger for the attraction model, whose next recompute sees Failed and releases every held area (unrated runs count 0)
                ServiceState = RatingServiceState.Failed;
                RatingRevision++;
            }
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiRunCatalogSystem)} EXCEPTION in {where} — the run list, resort links and " +
                              "run ratings stop updating until the next game load (the hub keeps the last rows; the rating " +
                              "service reports Failed: the attraction model stops waiting and runs that were not rated " +
                              $"count 0):\n{e}");
            }
        }
    }
}
