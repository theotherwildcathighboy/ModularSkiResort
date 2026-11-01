// Attraction model v2: ONE attraction score per ski area (one Welcome Sign), computed by the pure SkiResortScore from the area's
// own runs, lifts, active lodges and the sign's efficiency, and written onto its active lodges so the game's tourism sum sees it.
// Write path: vanilla AttractionSystem (interval 16, GameSimulation) rewrites AttractivenessProvider every tick; this system is
// UpdateAfter<..., AttractionSystem>(GameSimulation) with the same interval (anchored systems inherit the offset), so our write
// lands in the same tick, right after, and wins. Nothing is serialized.
// Recomputes are requested structurally (watcher, bindings, census, settings) or softly (ratings, snow, state poll), never per
// frame; Transform reads (sign / station positions) happen only when due, to avoid a job sync point.
// Engine notes: EngineNotes/SkiResortAttractivenessSystem.md

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static System.FormattableString;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiResortAttractivenessSystem : GameSystemBase
    {
        // Tuning: cadence and logging only (the model's constants live in SkiResortScore)
        private static class Tuning
        {
            public const int PeriodicRefreshTicks = 256;   // backstop: 256 × 16 = 4,096 frames
            public const int StatePollTicks = 16;  // 256 frames
            public const int MinSoftGapTicks = 16;         // a soft request runs at most every 256 frames
            public const int RatingHoldMaxTicks = 1024;  // 16,384 frames ≈ 4.6 min at 1×
            public const float AreaLogWindowS = 10f;  // per-area LogWindow
            public const int CostReportRecomputes = 32;    // the [cost] line, once per load
            public const double CostWarnMs = 2.0;          // one WARN per load above this
            public const int DampedTicksToLog = 16;        // TO damping: consecutive ticks before the line
            // An efficiency-only change writes its area line only when A moves
            // by this much, or when the area wrote no line for EfficiencyLineQuietS of wall clock.
            public const int EfficiencyLineMinDeltaA = 2;
            public const float EfficiencyLineQuietS = 60f;
            // The score uses the sign's efficiency averaged over about one game day (262,144 frames / 16 = 16,384 ticks), each
            // sample capped at 1.0, the result held to [0.25, 1.0]. Capped because a factor above 1 is a bonus the game gives
            // for something else, not a reason to amplify an attraction score (UNVERIFIED which factor gave the 1.16 seen in the
            // field: EmployeeHappiness, WorkProviderSystem.cs:269-270, or the ServiceBudget curve, CityServiceEfficiencySystem.cs:59).
            // The 0.25 floor: a sign swinging down to 44 % must not take the score to nothing in one sample.
            // Efficiency 0 (a Destroyed / Disabled sign: A = 0) is NOT smoothed or floored.
            // Engine notes: EngineNotes/SkiResortAttractivenessSystem.md (Near Tuning)
            public const double StaffingTauTicks = 16384.0;
            public const double StaffingFloor = 0.25;
            public const double StaffingCap = 1.0;
        }

        // Recompute reasons (exact texts). The watcher's three live in the watch system.
        internal const string ReasonLoad = "game load";
        internal const string ReasonTimer = "periodic refresh";
        internal const string ReasonBinding = "run binding changed";
        internal const string ReasonCensus = "lift census changed";
        internal const string ReasonSettings = "settings changed";
        internal const string ReasonHold = "rating hold expired";
        internal const string ReasonRating = "run rating changed";
        internal const string ReasonSnow = "run open state changed";
        internal const string ReasonSnowSettled = "snow settled";
        internal const string ReasonState = "sign efficiency, lodge on/off or lift power changed";

        // Prefab set — tolerant resolution (not yet imported ⇒ system stays inert). The run prefab sets are
        // the ownership system's (one registry, natural included). The lodges are SkiCatalog.Lodges, the stations
        // SkiCatalog.Stations, both resolved per load by SkiPrefabCatalogSystem (a lodge imported mid-session or a prefab
        // replaced is followed at the next load / frame).
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private SkiRunOwnershipSystem m_Ownership;       // sign set, run kinds, BindGeneration
        private SkiRunCatalogSystem m_Catalog;           // ratings, run tops, rating service
        private LiftCensusSystem m_Census;               // rows, serve lifts, power
        private SkiSnowInjectionSystem m_Injection;      // run open state, SnowSettled
        private SnowLineSystem m_SnowLine;               // the one snow gating read
        private Game.Simulation.TimeSystem m_Time;  // the [cost] WARN's midday re-arm and clocks

        private EntityQuery m_ParkQuery;       // signs, lodges (and other parks)
        private EntityQuery m_RunQuery;        // our bound runs

        private bool m_Disabled;
        private bool m_ErrorLogged;

        // The snapshot (double-buffered; Engine notes: Snapshot)

        /// <summary>One ski area as one recompute built it.</summary>
        private sealed class AreaSlot
        {
            public Entity Sign;
            public readonly List<SkiResortScore.RunInput> Runs = new List<SkiResortScore.RunInput>(16);
            public readonly List<Entity> RunEntities = new List<Entity>(16);
            public readonly List<Entity> ServedVia = new List<Entity>(16);  // parallel to Runs; the run this one is served through (Entity.Null = directly served, or unserved)
            public readonly List<SkiResortScore.RunPart> Parts = new List<SkiResortScore.RunPart>(16);
            public readonly List<SkiResortScore.LiftInput> Lifts = new List<SkiResortScore.LiftInput>(8);
            public readonly List<Entity> Active = new List<Entity>(4);
            public readonly List<Entity> Inactive = new List<Entity>(2);
            public readonly List<Efficiency> Factors = new List<Efficiency>(8);   // < 1, worst first
            public float EfficiencyValue;   // the sign's live efficiency (raw; the problem row and the DUMP read it)
            public int EfficiencyPct;
            public double StaffingUsed;  // the smoothed factor the score used (Result.Efficiency)
            public int StaffingUsedPct;
            public int OpenUnknown;
            public SkiResortScore.Result Result;
            public bool Held;
            // ActiveInfo is parallel to Active (same order); Lodges is the
            // score's AreaInput.Lodges built from it; ServeLifts = indices into m_ServeLifts of this area's lifts.
            public readonly List<LodgeInfo> ActiveInfo = new List<LodgeInfo>(4);
            public readonly List<SkiResortScore.LodgeInput> Lodges = new List<SkiResortScore.LodgeInput>(4);
            public readonly List<Entity> PatrolEntities = new List<Entity>(2);
            public readonly List<Entity> DepotActive = new List<Entity>(1);   // bus depots: never in Active / ActiveInfo, no score, no capacity
            public readonly List<float2> PatrolXz = new List<float2>(2);
            public readonly List<int> ServeLifts = new List<int>(8);
            public bool SchoolActive;
            public int Schools;   // school entries listed (active or not; DUMP)
            // (Engine notes: Top-level buildings) Σ tier weight × efficiency and Σ tier weight over the area's ACTIVE
            // top-level lodges (SchoolLinkRules.AreaEfficiency); 0 / 0 = no top-level lodge (the sign's efficiency alone).
            public double LodgeEffSum, LodgeWeightSum;
            public int SignEfficiencyPct;  // the sign's own efficiency (before the top-level lodges fold in), DUMP
            // (MountainJobsRules header EFFICIENCY) the sign's staffing product (NotEnoughEmployees x
            // SickEmployees x EmployeeHappiness, WorkProviderSystem.cs:279-281; the mountain buildings' jobs ride on the sign),
            // the sign's efficiency without it, and whether a mountain lodge took it into the top-level lodge mean.
            public float SignStaffing, SignNoStaffValue;
            public bool MountainInMean;

            public void Reset(Entity sign)
            {
                Sign = sign;
                Runs.Clear();
                RunEntities.Clear();
                ServedVia.Clear();
                Parts.Clear();
                Lifts.Clear();
                Active.Clear();
                Inactive.Clear();
                Factors.Clear();
                EfficiencyValue = 1f;
                EfficiencyPct = 100;
                StaffingUsed = 1.0;
                StaffingUsedPct = 100;
                OpenUnknown = 0;
                Result = default;
                Held = false;
                ActiveInfo.Clear();
                Lodges.Clear();
                PatrolEntities.Clear();
                DepotActive.Clear();
                PatrolXz.Clear();
                ServeLifts.Clear();
                SchoolActive = false;
                Schools = 0;
                LodgeEffSum = LodgeWeightSum = 0.0;
                SignEfficiencyPct = 100;
                SignStaffing = SignNoStaffValue = 1f;
                MountainInMean = false;
            }
        }

        private struct LiftKey : IEquatable<LiftKey>
        {
            public Entity Id, Top;

            public LiftKey(Entity id, Entity top)
            {
                Id = id;
                Top = top;
            }

            public bool Equals(LiftKey other)
            {
                return Id == other.Id && Top == other.Top;
            }

            public override bool Equals(object obj)
            {
                return obj is LiftKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return unchecked(Id.GetHashCode() * 397 ^ Top.GetHashCode());
            }
        }

        /// <summary>Everything one recompute publishes. m_Cur is read; m_Next is filled, then swapped.</summary>
        private sealed class Snapshot
        {
            public readonly List<AreaSlot> Slots = new List<AreaSlot>(4);
            public int SlotCount;
            public readonly Dictionary<Entity, int> SlotOfSign = new Dictionary<Entity, int>();
            public readonly Dictionary<Entity, RunScoreView> Runs = new Dictionary<Entity, RunScoreView>();
            public readonly Dictionary<LiftKey, LiftScoreView> Lifts = new Dictionary<LiftKey, LiftScoreView>();
            public readonly Dictionary<Entity, int> WriteValues = new Dictionary<Entity, int>();   // lodge → value written every tick
            public readonly Dictionary<Entity, Entity> LodgeArea = new Dictionary<Entity, Entity>(); // written lodge → its sign
            public int LiveLodges, Chords, LiftCount;

            public void Clear()
            {
                SlotCount = 0;
                SlotOfSign.Clear();
                Runs.Clear();
                Lifts.Clear();
                WriteValues.Clear();
                LodgeArea.Clear();
                LiveLodges = Chords = LiftCount = 0;
            }

            public AreaSlot AddSlot(Entity sign)
            {
                if (SlotCount == Slots.Count)
                    Slots.Add(new AreaSlot());
                AreaSlot slot = Slots[SlotCount];
                slot.Reset(sign);
                SlotOfSign[sign] = SlotCount;
                SlotCount++;
                return slot;
            }

            public bool TryGetSlot(Entity sign, out AreaSlot slot)
            {
                if (SlotOfSign.TryGetValue(sign, out int index))
                {
                    slot = Slots[index];
                    return true;
                }
                slot = null;
                return false;
            }
        }

        private Snapshot m_Cur = new Snapshot();
        private Snapshot m_Next = new Snapshot();
        private int m_Generation;

        // Per-load state

        /// <summary>First-write bookkeeping per area, kept across recomputes for one load.</summary>
        private sealed class AreaState
        {
            public bool FirstWriteDone;
            public int HoldTicks;
            public bool HoldWarned;

            // The smoothed staffing factor of this sign. Not serialized: a missing state (a load, a new sign) starts
            // from the live value, so the first score after a load is the unsmoothed one. The dictionary is cleared per load.
            private bool m_StaffingValid;
            private double m_StaffingEma, m_StaffingLastSample;
            private int m_StaffingLastTick;

            /// <summary>Advances the average over the ticks since the last call (the previous sample held over that
            /// time: the efficiency only changes between two recomputes, so this is exact for a step signal) and
            /// returns the factor for the score: the EMA held to [floor, cap] and rounded to 0.01 (the game's own
            /// precision, so the score does not move on noise). raw ≤ 0 returns 0 and forgets the average.</summary>
            public double StaffingFactor(float raw, int tick)
            {
                if (!(raw > 0f))
                {
                    m_StaffingValid = false;
                    return 0.0;
                }
                double sample = Math.Min((double)raw, Tuning.StaffingCap);
                if (!m_StaffingValid)
                {
                    m_StaffingEma = sample;
                    m_StaffingValid = true;
                }
                else if (tick > m_StaffingLastTick)
                {
                    double a = 1.0 - Math.Exp(-(tick - m_StaffingLastTick) / Tuning.StaffingTauTicks);
                    m_StaffingEma += a * (m_StaffingLastSample - m_StaffingEma);
                }
                m_StaffingLastTick = tick;
                m_StaffingLastSample = sample;
                double used = Math.Max(Tuning.StaffingFloor, Math.Min(Tuning.StaffingCap, m_StaffingEma));
                return Math.Round(used * 100.0) / 100.0;
            }
        }

        private struct LastRating
        {
            public byte State;
            public sbyte Band;
            public float TopM, BottomM;
        }

        private struct DropEntry
        {
            public Entity Lodge;
            public bool SwitchedOff;   // else: on no live Welcome Sign
        }

        private struct RunRef
        {
            public int Slot, Index;
        }

        /// <summary>Per area: the last logged key (A, counts, flags, held) and the fold window. Also the logged key without A and
        /// PerLodge (StructKey), the logged A / runs value / efficiency percent, and the last skipped key (a skip is counted once
        /// per change, not per recompute).</summary>
        private sealed class AreaLog
        {
            public readonly LogWindow Window = new LogWindow(Tuning.AreaLogWindowS);
            public bool HasKey;
            public ulong Key;
            public ulong StructKey;
            public ulong SkippedKey;
            public int LoggedA;
            public double LoggedRunsValue;
            public int LoggedEffPct;
        }

        private readonly Dictionary<Entity, AreaState> m_AreaStates = new Dictionary<Entity, AreaState>();
        private readonly Dictionary<Entity, int> m_HeldValues = new Dictionary<Entity, int>();  // lodge → provider value at load
        private readonly Dictionary<Entity, LastRating> m_LastRatings = new Dictionary<Entity, LastRating>();  // run → rating seen this load
        private readonly List<DropEntry> m_DropProvider = new List<DropEntry>();                     // consumed once by WriteProviders
        private readonly List<Entity> m_ZeroSigns = new List<Entity>();                              // signs carrying a provider: held at 0
        private readonly Dictionary<Entity, AreaLog> m_AreaLogs = new Dictionary<Entity, AreaLog>();
        private readonly HashSet<Entity> m_ProviderAddedLogged = new HashSet<Entity>();
        private readonly HashSet<Entity> m_ZeroSignLogged = new HashSet<Entity>();
        private readonly Dictionary<Entity, int> m_DampedTicks = new Dictionary<Entity, int>();
        private readonly HashSet<Entity> m_DampedLogged = new HashSet<Entity>();
        private bool m_CensusOpenLogged;
        private int m_HeldCount;   // areas held at the last recompute
        private long m_EfficiencyLinesSkipped;  // efficiency-only area lines not written this load (DUMP)

        // Recompute scratch (reused; no allocation once grown)
        // m_SignIndex and m_BaseXz are CACHES (Engine notes: Sync points): filled when positions
        // are due (a structural recompute, the backstop), reused by soft recomputes.
        private readonly SkiResortIndex m_SignIndex = new SkiResortIndex();
        private readonly Dictionary<Entity, float2> m_BaseXz = new Dictionary<Entity, float2>();
        // The same cache rule for the summit-served lodges and the patrol huts.
        private readonly Dictionary<Entity, float2> m_LodgeXz = new Dictionary<Entity, float2>();
        private readonly Dictionary<Entity, float2> m_PatrolXz = new Dictionary<Entity, float2>();
        // (Engine notes: Top-level buildings) the park walk's top-level resort buildings (no Owner: lodges, the school,
        // patrol huts) with their kind (parallel list), their cached xz (same rule as m_PatrolXz), the published links (building
        // -> sign or Null) and this recompute's, each building's kind for the log (kept until its Removed line), the log diff
        // scratch, and one lodge's efficiency factors (reused).
        private readonly List<Entity> m_TopLevel = new List<Entity>(8);
        private readonly List<byte> m_TopKind = new List<byte>(8);
        private readonly Dictionary<Entity, float2> m_TopXz = new Dictionary<Entity, float2>();
        private Dictionary<EntityRef, SchoolLinkRules.Link> m_TopLinks = new Dictionary<EntityRef, SchoolLinkRules.Link>();
        private Dictionary<EntityRef, SchoolLinkRules.Link> m_TopLinksNext = new Dictionary<EntityRef, SchoolLinkRules.Link>();
        private readonly Dictionary<EntityRef, byte> m_TopKindOf = new Dictionary<EntityRef, byte>();
        private readonly List<SchoolLinkRules.Change> m_TopLinkChanges = new List<SchoolLinkRules.Change>(4);
        private int m_TopLinkLines;   // this load (DUMP)
        private float[] m_LodgeFactorValues = new float[16];
        private readonly List<LiftServedRule.Lift> m_AreaServeLifts = new List<LiftServedRule.Lift>(8);   // one area's lifts (SummitServed)
        private bool m_PositionsReadThisRecompute;   // CollectAreas' verdict, for the lodge / patrol reads after it
        private bool m_PositionsDue = true;      // the backstop asks for a position read (Engine notes: Sync points)
        private int m_PositionReads, m_PositionFallbacks;   // this load (DUMP)
        private readonly HashSet<Entity> m_LiveLodges = new HashSet<Entity>();
        private readonly HashSet<Entity> m_LodgeClaimed = new HashSet<Entity>();
        private readonly List<float3> m_RunTops = new List<float3>(32);
        // (LiftServedRule.AssignLinked) per run the outline xz and node heights (the catalog row's arrays, by reference), its area
        // slot, and the link graph (self-validating: rebuilt only when an outline or height array, a slot or the run set changed).
        // ServedRoot / ServedVia: the directly served run a run is served through, and the neighbor it was reached through (-1 none).
        private readonly List<float2[]> m_RunOutlines = new List<float2[]>(32);
        private readonly List<float[]> m_RunHeights = new List<float[]>(32);
        private readonly List<int> m_RunAreas = new List<int>(32);
        private readonly LiftServedRule.RunLinks m_RunLinks = new LiftServedRule.RunLinks();
        private readonly List<int> m_ServedRoot = new List<int>(32);
        private readonly List<int> m_ServedVia = new List<int>(32);
        private readonly List<RunRef> m_RunRefs = new List<RunRef>(32);
        private readonly List<bool> m_TrackRuns = new List<bool>(32);  // per run ref, RunKindRules.IsCatTrackLink (the cat track mask)
        private readonly List<LiftServedRule.Lift> m_ServeLifts = new List<LiftServedRule.Lift>(8);
        private readonly List<LiftServedRule.Chord> m_Chords = new List<LiftServedRule.Chord>(64);
        private readonly List<int> m_ServingLift = new List<int>(32);
        private readonly List<float> m_ServingDistance = new List<float>(32);
        private readonly List<int> m_ServedCount = new List<int>(8);
        private readonly List<LiftCensus.Row> m_CensusRows = new List<LiftCensus.Row>(8);
        private readonly List<Entity> m_ScratchEntities = new List<Entity>(8);
        private float[] m_FactorValues = new float[32];
        private float[] m_NoStaffValues = new float[32];  // the sign's factors without its staffing three

        // Triggers (Engine notes: Cadence)
        private bool m_StructuralPending;
        private bool m_SoftPending;
        private string m_PendingReason;
        private int m_Tick;
        private int m_LastRecomputeTick;
        private int m_TicksUntilRefresh = Tuning.PeriodicRefreshTicks;
        private bool m_CountersPrimed;
        private int m_LastBindGeneration, m_LastCensusRevision, m_LastRatingRevision, m_LastSnowGeneration;
        private int m_LastK, m_LastBackcountry;
        private bool m_LastGating;
        private bool m_LastSnowSettled;
        private ulong m_StateHash;

        // Counts and cost (DUMP, [cost])
        private string m_LastReason = "-";
        private int m_Recomputes, m_Requests;
        private int m_PausedRecomputes;  // structural recomputes run by the watcher while the game was paused (DUMP)
        private readonly Dictionary<string, int> m_RecomputesByReason = new Dictionary<string, int>();
        private readonly Dictionary<string, int> m_RequestsByReason = new Dictionary<string, int>();
        private int m_CostCount, m_PollCount;
        private double m_CostSumMs, m_CostMaxMs, m_PollSumMs;
        private double m_FirstRecomputeMs = -1.0;   // the load's first recompute, kept out of mean / max / WARN (−1 = none yet)
        private bool m_CostWarned;
        // The WARN's day (SkiDayRules.DoneDayOf: today when it fired at or after 12:00, else none, so it
        // re-arms today at noon) and the clock of the cost max (month key SkiDayRules.NoMonth / minute −1 before any).
        private int m_CostWarnDay = int.MinValue;
        private int m_CostMaxMonth = SkiDayRules.NoMonth, m_CostMaxMinute = -1;


        /// <summary>++ per load, per recompute that changed any view, factor list or written value, and when
        /// WriteProviders drops a deleted lodge (Engine notes: Generation). Plain field read; never reset (readers
        /// compare for equality).</summary>
        public int Generation => m_Generation;

        /// <summary>True for a lodge BuildingPrefab entity (every SkiCatalog.Lodges row): the prefab catalog's answer.</summary>
        internal bool IsLodgePrefab(Entity prefab)
        {
            return m_PrefabCatalog.IsLodge(prefab);
        }

        /// <summary>The prefab catalog's live lodge-prefab view, for a caller
        /// that tests many buildings in one scan (SkiGuestCounterSystem.RebuildMap fetches it once instead of calling
        /// IsLodgePrefab per building). Main thread; do not keep it across frames (the catalog revalidates per frame).</summary>
        internal HashSet<Entity> LodgePrefabSet => m_PrefabCatalog.LodgePrefabSet;

        /// <summary>The catalog's bus depot prefabs (same caveat as LodgePrefabSet).</summary>
        internal HashSet<Entity> DepotPrefabSet => m_PrefabCatalog.DepotPrefabSet;

        /// <summary>Scoring runs this load: not error-disabled, a lodge prefab resolved (catalog) and the ownership
        /// system's prefab sets ready (sign set and run sets, Game mode).</summary>
        internal bool IsActive => !m_Disabled && m_PrefabCatalog.LodgeCount > 0 && m_Ownership.PrefabSetsReady;

        /// <summary>A STRUCTURAL recompute is already waiting for the next 16-frame tick (a pending soft one
        /// does not count, so a placement is never delayed behind it).</summary>
        internal bool RecomputePending => m_StructuralPending;

        /// <summary>A building prefab whose placement, move or removal changes a ski area: a lodge, the ski school, a
        /// patrol hut, a bus depot, a lift station (the prefab catalog's) or a Welcome Sign (the sign stays the ownership registry's).</summary>
        internal bool IsWatchedBuildingPrefab(Entity prefab)
        {
            // Lodge ∪ school ∪ patrol hut ∪ bus depot.
            return m_PrefabCatalog.IsUpgradeBuilding(prefab) ||
                   m_PrefabCatalog.IsStation(prefab) ||
                   m_Ownership.IsGatePrefab(prefab);
        }

        /// <summary>One of our run surface prefabs (all four kinds) — the ownership registry.</summary>
        internal bool IsWatchedAreaPrefab(Entity prefab)
        {
            return m_Ownership.IsRunPrefab(prefab);
        }

        /// <summary>STRUCTURAL request: the recompute runs on the next tick (it upgrades a pending soft one;
        /// the first structural reason is kept). Counted per reason; the first of each reason per load is
        /// logged. Main thread (the watcher at ModificationEnd, and this system's tick).</summary>
        internal void RequestRecompute(string reason)
        {
            CountRequest(reason);
            if (m_StructuralPending)
                return;
            m_StructuralPending = true;
            m_SoftPending = false;
            m_PendingReason = reason;
        }

        /// <summary>The watcher's placement request run NOW, for a paused game. GameSimulation does not run while paused
        /// (SimulationSystem.cs:221-224: selectedSpeed 0 → no frame), so a lodge, school or patrol hut placed while paused
        /// stayed Not linked until the game ran. Called only by SkiResortAttractivenessWatchSystem at ModificationEnd (main
        /// thread, runs paused) right after RequestRecompute: the same three steps as one OnUpdate tick (the pending
        /// structural Recompute, WriteProviders, FlushAreaLogs), so the published links, the provider writes and the logs stay
        /// in step. While the game runs the watcher leaves the request to the next 16-frame tick.</summary>
        internal void RecomputeWhilePaused()
        {
            try
            {
                if (m_Disabled || !IsActive || !m_StructuralPending)
                    return;
                string reason = m_PendingReason ?? ReasonTimer;
                m_StructuralPending = m_SoftPending = false;
                m_PendingReason = null;
                if (m_PausedRecomputes++ == 0)
                    Mod.Log.Info($"Attractiveness [watch]: recompute while paused ({reason}) — first this load; links and " +
                                 "scores follow a placement without unpausing (T120); later ones are counted (hub DUMP)");
                Recompute(reason, true);
                WriteProviders();
                FlushAreaLogs();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiResortAttractivenessSystem)} EXCEPTION (paused recompute) — attractiveness " +
                                  $"disabled until the next game load. Please report this stack:\n{e}");
                }
            }
        }

        /// <summary>SOFT request: runs once MinSoftGapTicks passed since the last recompute.</summary>
        private void RequestSoftRecompute(string reason)
        {
            CountRequest(reason);
            if (m_StructuralPending || m_SoftPending)
                return;
            m_SoftPending = true;
            m_PendingReason = reason;
        }

        private void CountRequest(string reason)
        {
            m_Requests++;
            m_RequestsByReason.TryGetValue(reason, out int n);
            m_RequestsByReason[reason] = n + 1;
            if (n == 0)
                Mod.Log.Info($"Attractiveness [watch]: trigger ({reason}) — first this load; later triggers are counted (hub DUMP)");
        }

        // Snapshot: plain reads of the last recompute (frozen while paused). Main thread only.

        /// <summary>One ski area's last result (the hub reads this, never recomputes).</summary>
        internal struct AreaScoreView
        {
            public Entity Sign;
            public SkiResortScore.Result Result;
            public bool WriteHeld;  // the first write after a load still waits for run ratings
            public int HoldTicks;           // ticks held so far (0 once written)
            public int EfficiencyPct;       // the sign's live efficiency, round(100 × raw): the problem row reads this
            public int EfficiencyUsedPct;  // round(100 × Result.Efficiency), the smoothed factor the score used (the receipt and tile read this)
            public int OpenUnknown;         // member runs the snow chapter had no open state for (RunSnow.Source == None)
        }

        /// <summary>One member run's inputs and part of the last recompute.</summary>
        internal struct RunScoreView { public Entity Run, Sign; public SkiResortScore.RunInput Input; public SkiResortScore.RunPart Part; }

        /// <summary>One census lift as the last recompute counted it. Powered = lift.Power != LiftPower.NoPower
        /// (what the game shows); Counted = Complete && LiftServedRule.IsPowered(lift.Power).</summary>
        internal struct LiftScoreView { public Entity Id, Base, Top, Sign; public bool Complete, Powered, Counted; }

        /// <summary>The last recompute's result for <paramref name="sign"/> (false: not a live sign at the
        /// last recompute). HoldTicks is the live count while held.</summary>
        internal bool TryGetAreaScore(Entity sign, out AreaScoreView view)
        {
            if (!m_Cur.TryGetSlot(sign, out AreaSlot slot))
            {
                view = default;
                return false;
            }
            view = new AreaScoreView
            {
                Sign = slot.Sign,
                Result = slot.Result,
                WriteHeld = slot.Held,
                HoldTicks = slot.Held && m_AreaStates.TryGetValue(slot.Sign, out AreaState state) ? state.HoldTicks : 0,
                EfficiencyPct = slot.EfficiencyPct,
                EfficiencyUsedPct = slot.StaffingUsedPct,
                OpenUnknown = slot.OpenUnknown,
            };
            return true;
        }

        /// <summary>The last recompute's view of member run <paramref name="run"/> (false: not a member —
        /// unbound, a paid orphan, or bound to a sign that was not live).</summary>
        internal bool TryGetRunScore(Entity run, out RunScoreView view)
        {
            return m_Cur.Runs.TryGetValue(run, out view);
        }

        /// <summary>The last recompute's view of the census row (<paramref name="id"/>, <paramref name="top"/>)
        /// (two lifts can share one base station). Only lifts that belong to a ski area (both
        /// stations live, base within the tether of a live sign) have a view.</summary>
        internal bool TryGetLiftScore(Entity id, Entity top, out LiftScoreView view)
        {
            return m_Cur.Lifts.TryGetValue(new LiftKey(id, top), out view);
        }

        /// <summary>The sign's efficiency factors below 100 % from the last recompute, worst first,
        /// appended to <paramref name="into"/> after clearing it; returns the count.</summary>
        internal int CopyEfficiencyFactors(Entity sign, List<Efficiency> into)
        {
            into.Clear();
            if (m_Cur.TryGetSlot(sign, out AreaSlot slot))
            {
                for (int i = 0; i < slot.Factors.Count; i++)
                    into.Add(slot.Factors[i]);
            }
            return into.Count;
        }

        // The lodge list and the site inputs of the last recompute, for SkiDaySystem, SkiLodgeParkingSystem and
        // SkiLiftTicketSystem's gate watch. Main thread only; plain copies (no AddRange), allocation-free once the caller's
        // lists have grown. Same recompute as TryGetAreaScore, so the lodge list and the gate inputs agree; the guest counter's
        // target map uses the same lodge rule, so a lodge from this list is a counted target.

        private int m_LodgeSetRevision;

        /// <summary>++ when the published (sign, active lodges) lists change; at every load; when WriteProviders
        /// drops a deleted lodge (consumers re-copy).</summary>
        internal int LodgeSetRevision => m_LodgeSetRevision;

        /// <summary>False only between a load and the first recompute of an active model: the snapshot is empty
        /// then, not "no ski area". While inactive (no lodge prefab, prefab sets not ready, error-disabled) it is
        /// true, and CopySigns' 0 is the truth (the parking census and SkiDaySystem wait on it).</summary>
        internal bool HasSnapshot => !IsActive || m_Recomputes > 0;

        /// <summary>Live Welcome Signs of the last recompute, slot order; clears <paramref name="into"/>; returns
        /// the count (0 while inactive).</summary>
        internal int CopySigns(List<Entity> into)
        {
            into.Clear();
            if (!IsActive)
                return 0;
            for (int s = 0; s < m_Cur.SlotCount; s++)
                into.Add(m_Cur.Slots[s].Sign);
            return into.Count;
        }

        /// <summary>One ACTIVE lodge of the last recompute with its tier. LodgeIndex = SkiCatalog.Lodges index (the lodge's
        /// PrefabRef is its tier); SummitServed = within Ladder.SummitServedRadiusM of a complete, powered lift's top stub tip of
        /// its ski area (evaluated for rows with μ summit &gt; μ; false for the others). Every active lodge counts.</summary>
        internal struct LodgeInfo
        {
            public Entity Lodge;
            public short LodgeIndex;
            public bool SummitServed;
        }

        /// <summary>The LodgeInfo of every active lodge of <paramref name="sign"/> (AreaSlot.Active order: lodge prefab, in the park
        /// walk, not Inactive; first sign wins; base and mountain lodges; NOT filtered for Destroyed); clears <paramref name="into"/>;
        /// returns the count (0 for an unknown sign). Main thread, plain copy (allocation-free once the caller's list has grown).</summary>
        internal int CopyActiveLodgeInfo(Entity sign, List<LodgeInfo> into)
        {
            into.Clear();
            if (m_Cur.TryGetSlot(sign, out AreaSlot slot))
            {
                for (int i = 0; i < slot.ActiveInfo.Count; i++)
                    into.Add(slot.ActiveInfo[i]);
            }
            return into.Count;
        }

        /// <summary>The active bus depots linked to <paramref name="sign"/> (the nearest live sign at any distance; not Inactive; NOT filtered
        /// for Destroyed: CheckDepot does); clears <paramref name="into"/>; returns the count (0 for an unknown sign). Main thread, plain copy.</summary>
        internal int CopyActiveDepots(Entity sign, List<Entity> into)
        {
            into.Clear();
            if (m_Cur.TryGetSlot(sign, out AreaSlot slot))
            {
                for (int i = 0; i < slot.DepotActive.Count; i++)
                    into.Add(slot.DepotActive[i]);
            }
            return into.Count;
        }

        /// <summary>Site inputs for SkiDaySystem's SiteJob: clears both; runs = slot.RunEntities
        /// of <paramref name="sign"/>; stations = the non-Null Base / Top of every m_Cur.Lifts value with Sign ==
        /// sign, deduplicated; returns runs.Count + liftStations.Count; 0 for an unknown sign.</summary>
        internal int CopySiteMembers(Entity sign, List<Entity> runs, List<Entity> liftStations)
        {
            runs.Clear();
            liftStations.Clear();
            if (!m_Cur.TryGetSlot(sign, out AreaSlot slot))
                return 0;
            for (int i = 0; i < slot.RunEntities.Count; i++)
                runs.Add(slot.RunEntities[i]);
            foreach (var pair in m_Cur.Lifts)
            {
                LiftScoreView lift = pair.Value;
                if (lift.Sign != sign)
                    continue;
                AddStation(liftStations, lift.Base);
                AddStation(liftStations, lift.Top);
            }
            return runs.Count + liftStations.Count;
        }

        /// <summary>Appends a non-Null station once (a handful per area: a linear Contains is cheaper than a set).</summary>
        private static void AddStation(List<Entity> into, Entity station)
        {
            if (station != Entity.Null && !into.Contains(station))
                into.Add(station);
        }


        /// <summary>Mirror vanilla AttractionSystem's cadence so the anchored write lands
        /// right after vanilla's each tick (see Engine notes: Write path).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            // PowerBridgeSystem is still CREATED here: this system is registered first (Mod.cs, before PowerBridge),
            // and creation order is load-hook subscription order (GameSystemBase.cs:27-29) — kept identical.
            World.GetOrCreateSystemManaged<PowerBridgeSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_Injection = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_SnowLine = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_Time = World.GetOrCreateSystemManaged<Game.Simulation.TimeSystem>();

            // The park-building query (signs and lodges carry Game.Buildings.Park, Park.cs:26-41). No
            // Transform (Engine notes: Sync points): the walk reads Entity and PrefabRef only; sign and
            // base-station positions are read through EntityManager when they are due (ReadPositions).
            m_ParkQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Game.Buildings.Park>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            // The bound-run query (only our runs carry SkiRunBinding).
            m_RunQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<SkiRunBinding>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });

            Mod.Log.Info($"{nameof(SkiResortAttractivenessSystem)} created");
        }

        /// <summary>A load (or leaving to the menu) writes the folded area lines of the city being left.
        /// Never throws: GameSystemBase.GamePreload DISABLES a system whose OnGamePreload throws
        /// (Game/GameSystemBase.cs:85-96; same in 1.6.2).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                FlushAllAreaLogs();
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Attractiveness: folded area lines could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        /// <summary>Quit to desktop raises no onGamePreload, but the world's systems are destroyed while the
        /// logger still lives (LogWindow.cs header): the folded area lines are written here. Never throws.</summary>
        protected override void OnDestroy()
        {
            try
            {
                FlushAllAreaLogs();
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Every per-load map starts empty, whatever the mode (nothing of the previous city survives).
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Cur.Clear();
            m_Next.Clear();
            m_Generation++;
            m_LodgeSetRevision++;  // the lists were emptied (consumers re-copy)
            m_EfficiencyLinesSkipped = 0;
            m_AreaStates.Clear();
            m_HeldValues.Clear();
            m_LastRatings.Clear();
            m_DropProvider.Clear();
            m_ZeroSigns.Clear();
            m_AreaLogs.Clear();   // flushed in OnGamePreload
            m_ProviderAddedLogged.Clear();
            m_ZeroSignLogged.Clear();
            m_DampedTicks.Clear();
            m_DampedLogged.Clear();
            m_CensusOpenLogged = false;
            m_HeldCount = 0;
            m_StructuralPending = m_SoftPending = false;
            m_PendingReason = null;
            m_Tick = 0;
            m_LastRecomputeTick = -Tuning.MinSoftGapTicks;
            m_TicksUntilRefresh = Tuning.PeriodicRefreshTicks;
            m_CountersPrimed = false;
            m_LastSnowSettled = false;  // the SnowSettled edge starts from false each load
            m_StateHash = 0UL;
            m_LastReason = "-";
            m_Recomputes = m_Requests = 0;
            m_RecomputesByReason.Clear();
            m_RequestsByReason.Clear();
            m_CostCount = m_PollCount = 0;
            m_CostSumMs = m_CostMaxMs = m_PollSumMs = 0.0;
            m_FirstRecomputeMs = -1.0;
            m_CostWarned = false;
            m_CostWarnDay = int.MinValue;
            m_CostMaxMonth = SkiDayRules.NoMonth;
            m_CostMaxMinute = -1;
            m_SignIndex.Clear();
            m_BaseXz.Clear();
            m_LodgeXz.Clear();
            m_PatrolXz.Clear();
            m_TopLevel.Clear();  // links are re-logged once per load (the first recompute links every building)
            m_TopKind.Clear();
            m_TopXz.Clear();
            m_TopLinks.Clear();
            m_TopLinksNext.Clear();
            m_PausedRecomputes = 0;
            m_TopKindOf.Clear();
            m_TopLinkLines = 0;
            m_PositionsDue = true;   // the load's recompute is structural anyway
            m_PositionReads = m_PositionFallbacks = 0;

            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;
            LogLodgePrefabs();
            if (mode != GameMode.Game)
                return;

            Mod.RefreshTourismOverhaulDetection(); // fixes the OnLoad-time load-order race
            ReadHeldValues();
            ReadSettings(out int k, out int backcountry, out bool gating);
            // Integers and fixed text only, or invariant culture (ArmModelText): the ladder, the per-type
            // lift values, the school and the patrol, every number from SkiCatalog.
            Mod.Log.Info(
                "Attractiveness [arm]: model v2 (E1) + Catalog Phase B — one score per ski area: A = clamp(round((lodge applied + K × sqrt(S)) × staffing factor), 0, 200), staffing factor = sign efficiency averaged over one game day, 25-100 %, " +
                "S = (runs + lifts (summer: × each type's share) + school) × variety × vertical; " + ArmModelText() + "; band 0/6/12/20/28, unrateable 3; " +
                "size 1 + 0.3 ln(area / 20000 m²) in [0.25, 2] from 10000 m², proportional below (0.792 per ha); night ×1.25; natural ×0.5/0.75/1/1; " +
                "variety ×1.15; vertical / 600 m (max ×2); summer = no run worth more than 0; " +
                $"K {k}, backcountry {backcountry} %, snow gating {(gating ? "on" : "off")}; each active lodge carries round(A / sqrt(N)); " +
                $"held saved values {m_HeldValues.Count} lodge(s); first write waits for ratings and snow (max {Tuning.RatingHoldMaxTicks} ticks); " +
                $"poll every {Tuning.StatePollTicks} ticks, backstop every {Tuning.PeriodicRefreshTicks} ticks");
            if (Mod.TourismOverhaulDetected)
                Mod.Log.Info("Attractiveness: Tourism Overhaul present — its crowding and park gate can damp the values we write " +
                             "(each damped lodge is logged once, §7.6); nothing to fold in");
            RequestRecompute(ReasonLoad);
        }

        /// <summary>Every lodge building's AttractivenessProvider as the save left it (the
        /// component is ISerializable, so it is deserialized before any tick).</summary>
        private void ReadHeldValues()
        {
            // The catalog's live lodge view, fetched once before the chunk loop.
            HashSet<Entity> lodgePrefabs = m_PrefabCatalog.LodgePrefabSet;
            if (lodgePrefabs.Count == 0 || m_ParkQuery.IsEmptyIgnoreFilter)
                return;
            m_ParkQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            m_ScratchEntities.Clear();
            using (var chunks = m_ParkQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (lodgePrefabs.Contains(prefabRefs[i].m_Prefab))
                            m_ScratchEntities.Add(entities[i]);
                    }
                }
            }
            for (int i = 0; i < m_ScratchEntities.Count; i++)
            {
                if (EntityManager.TryGetComponent(m_ScratchEntities[i], out AttractivenessProvider provider))
                    m_HeldValues[m_ScratchEntities[i]] = provider.m_Attractiveness;
            }
            m_ScratchEntities.Clear();
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !IsActive)
                    return; // lodge prefab not imported, or the ownership sets not ready — inert by design

                m_Tick++;
                CompareCounters();
                if (m_Tick % Tuning.StatePollTicks == 0)
                    PollState();
                if (--m_TicksUntilRefresh <= 0)
                {
                    m_TicksUntilRefresh = Tuning.PeriodicRefreshTicks;
                    m_PositionsDue = true;  // the backstop re-reads the positions too (Engine notes: Sync points)
                    RequestSoftRecompute(ReasonTimer);
                }
                AdvanceHolds();

                if (m_StructuralPending || (m_SoftPending && m_Tick - m_LastRecomputeTick >= Tuning.MinSoftGapTicks))
                {
                    string reason = m_PendingReason ?? ReasonTimer;
                    bool structural = m_StructuralPending;
                    m_StructuralPending = m_SoftPending = false;
                    m_PendingReason = null;
                    Recompute(reason, structural);
                }

                WriteProviders();
                FlushAreaLogs();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiResortAttractivenessSystem)} EXCEPTION — attractiveness " +
                                  $"disabled until the next game load. Please report this stack:\n{e}");
                }
            }
        }


        /// <summary>K, backcountry (Mod.Settings with the Default* fallback, clamped as the model clamps) and
        /// gating through SnowLineSystem (one read per setting).</summary>
        private void ReadSettings(out int k, out int backcountry, out bool gating)
        {
            k = Mod.Settings?.AttractivenessScaleK ?? SkiResortSettings.DefaultAttractivenessScaleK;
            k = k < SkiResortScore.MinK ? SkiResortScore.MinK : (k > SkiResortScore.MaxK ? SkiResortScore.MaxK : k);
            backcountry = Mod.Settings?.BackcountryValuePercent ?? SkiResortSettings.DefaultBackcountryValuePercent;
            backcountry = backcountry < 0 ? 0 : (backcountry > 100 ? 100 : backcountry);
            gating = m_SnowLine.IsSnowGatingEnabled();
        }

        /// <summary>Four int counters, the settings, the SnowSettled edge. The first
        /// tick of a load only records them (the load's own structural recompute covers that tick).</summary>
        private void CompareCounters()
        {
            int bind = m_Ownership.BindGeneration;
            int census = m_Census.Revision;
            int rating = m_Catalog.RatingRevision;
            int snow = m_Injection.RunSnowGeneration;
            ReadSettings(out int k, out int backcountry, out bool gating);
            bool settled = m_Injection.SnowSettled;

            if (!m_CountersPrimed)
            {
                m_CountersPrimed = true;
                m_LastBindGeneration = bind;
                m_LastCensusRevision = census;
                m_LastRatingRevision = rating;
                m_LastSnowGeneration = snow;
                m_LastK = k;
                m_LastBackcountry = backcountry;
                m_LastGating = gating;
            }
            else
            {
                if (bind != m_LastBindGeneration)
                {
                    m_LastBindGeneration = bind;
                    RequestRecompute(ReasonBinding);
                }
                if (census != m_LastCensusRevision)
                {
                    m_LastCensusRevision = census;
                    RequestRecompute(ReasonCensus);
                }
                if (k != m_LastK || backcountry != m_LastBackcountry || gating != m_LastGating)
                {
                    Mod.Log.Info($"Attractiveness [settings]: K {m_LastK} → {k}, backcountry {m_LastBackcountry} % → {backcountry} %, " +
                                 $"snow gating {(m_LastGating ? "on" : "off")} → {(gating ? "on" : "off")} — recompute");
                    m_LastK = k;
                    m_LastBackcountry = backcountry;
                    m_LastGating = gating;
                    RequestRecompute(ReasonSettings);
                }
                if (rating != m_LastRatingRevision)
                {
                    m_LastRatingRevision = rating;
                    RequestSoftRecompute(ReasonRating);
                }
                if (snow != m_LastSnowGeneration)
                {
                    m_LastSnowGeneration = snow;
                    RequestSoftRecompute(ReasonSnow);
                }
            }
            // The SnowSettled false → true edge while an area is held.
            if (settled && !m_LastSnowSettled && m_HeldCount > 0)
                RequestSoftRecompute(ReasonSnowSettled);
            m_LastSnowSettled = settled;
        }

        /// <summary>Every StatePollTicks, one hash of each area's efficiency percent and lodge on/off
        /// bits (ReadSign, the same reads as the recompute) and the census power signature; a change asks for
        /// a soft recompute.</summary>
        private void PollState()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ulong hash = SkiRunRatingRules.FnvOffset;
            for (int s = 0; s < m_Cur.SlotCount; s++)
                ReadSign(m_Cur.Slots[s].Sign, null, ref hash);
            LinkTopLevel(null, ref hash);  // the top-level buildings' on/off bits and lodge efficiency (the last walk's list)
            hash = SkiRunRatingRules.Mix(hash, unchecked((uint)m_Census.ComputePowerSignature()));
            m_PollCount++;
            m_PollSumMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (hash != m_StateHash)
            {
                m_StateHash = hash;
                RequestSoftRecompute(ReasonState);
            }
        }

        /// <summary>HoldTicks + 1 per tick for every held area; reaching the cap asks for a
        /// structural recompute (which then writes anyway).</summary>
        private void AdvanceHolds()
        {
            if (m_HeldCount == 0)
                return;
            for (int s = 0; s < m_Cur.SlotCount; s++)
            {
                AreaSlot slot = m_Cur.Slots[s];
                if (!slot.Held || !m_AreaStates.TryGetValue(slot.Sign, out AreaState state) || state.FirstWriteDone)
                    continue;
                state.HoldTicks++;
                if (state.HoldTicks == Tuning.RatingHoldMaxTicks)
                    RequestRecompute(ReasonHold);
            }
        }


        private void Recompute(string reason, bool structural)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            m_Recomputes++;
            m_LastReason = reason;
            m_RecomputesByReason.TryGetValue(reason, out int byReason);
            m_RecomputesByReason[reason] = byReason + 1;
            m_LastRecomputeTick = m_Tick;

            ReadSettings(out int k, out int backcountry, out bool gating);
            Snapshot next = m_Next;
            next.Clear();
            m_DropProvider.Clear();
            m_ZeroSigns.Clear();

            // 1. Signs and lodge buildings (one park walk), then each sign's efficiency and lodges. Positions
            // (the Transform sync) only when due: a structural recompute or the backstop (Engine notes: Sync points).
            bool readPositions = structural || m_PositionsDue;
            m_PositionsDue = false;
            readPositions = CollectAreas(next, readPositions);
            m_PositionsReadThisRecompute = readPositions;
            if (readPositions)
            {
                m_LodgeXz.Clear();  // re-read when due, like m_BaseXz
                m_PatrolXz.Clear();
                m_TopXz.Clear();
            }
            ulong stateHash = SkiRunRatingRules.FnvOffset;
            m_LodgeClaimed.Clear();
            for (int s = 0; s < next.SlotCount; s++)
                ReadSign(next.Slots[s].Sign, next.Slots[s], ref stateHash);
            LinkTopLevel(next, ref stateHash);  // top-level buildings by distance (after the signs)
            ReadPatrolPositions(next);  // each area's active patrol huts' xz

            // 2. Member runs (bound-run walk): rating, snow, top node.
            CollectRuns(next);

            // 3. The served rule, once for every member run against every lift of the city.
            bool censusArmed = m_Census.IsArmed;
            m_ServeLifts.Clear();
            m_Chords.Clear();
            if (censusArmed)
            {
                m_Census.CopyServeLifts(m_ServeLifts, m_Chords);
                // THE served rule with downhill run links (a run skiers get onto downhill from a served run is served). Links are
                // rebuilt only when an outline or height array, a slot or the run set changed (RunLinks.Build keeps its graph
                // otherwise), so the pair pass does not run on every soft recompute; the lift part (Assign) is the same cost as before.
                m_RunLinks.Build(m_RunOutlines, m_RunHeights, m_RunAreas);
                // Cat tracks carry service level or downhill along themselves (the track mask): the same rule as
                // SkiResortStatsSystem.AssignServedLifts.
                m_TrackRuns.Clear();
                bool anyTrack = false;
                for (int i = 0; i < m_RunRefs.Count; i++)
                {
                    RunRef lr = m_RunRefs[i];
                    bool track = RunKindRules.IsCatTrackLink(next.Slots[lr.Slot].Runs[lr.Index].Kind);
                    m_TrackRuns.Add(track);
                    anyTrack |= track;
                }
                LiftServedRule.AssignLinked(m_RunTops, m_RunLinks, m_ServeLifts, m_Chords, m_ServingLift, m_ServingDistance,
                                            m_ServedCount, m_ServedRoot, m_ServedVia, anyTrack ? m_TrackRuns : null);
            }
            else if (!m_CensusOpenLogged && m_RunRefs.Count > 0)
            {
                m_CensusOpenLogged = true;
                Mod.Log.Info("Attractiveness: lift census not armed — every member run counts as lift-served until it is " +
                             "(fail open, e1-spec R2); once per load");
            }
            for (int i = 0; i < m_RunRefs.Count; i++)
            {
                RunRef rr = m_RunRefs[i];
                AreaSlot slot = next.Slots[rr.Slot];
                SkiResortScore.RunInput input = slot.Runs[rr.Index];
                input.Served = !censusArmed || m_ServedRoot[i] >= 0;  // directly served OR reached downhill from a served run
                if (censusArmed && m_ServedVia[i] >= 0)
                {
                    RunRef via = m_RunRefs[m_ServedVia[i]];   // same slot by construction (links never cross areas)
                    slot.ServedVia[rr.Index] = next.Slots[via.Slot].RunEntities[via.Index];
                }
                // Patrolled = the run's top node within PatrolRule.RadiusM of an active patrol
                // hut of its own area (the served rule's test point; NaN without facts → not patrolled).
                if (slot.PatrolXz.Count > 0)
                {
                    float3 top = m_RunTops[i];
                    input.Patrolled = PatrolRule.Patrolled(new float2(top.x, top.z), slot.PatrolXz);
                }
                slot.Runs[rr.Index] = input;
            }
            next.Chords = m_Chords.Count;

            // 4. Lifts (census rows with both stations live), resolved to an area.
            CollectLifts(next, readPositions);
            stateHash = SkiRunRatingRules.Mix(stateHash, unchecked((uint)m_Census.ComputePowerSignature()));

            // 5. Score each area; holds; write lists; views; area logs.
            ScoreAreas(next, k, backcountry, gating, reason);

            // 6. Orphan lodges (listed by no live sign) carry no provider.
            foreach (Entity lodge in m_LiveLodges)
            {
                if (!m_LodgeClaimed.Contains(lodge) && EntityManager.HasComponent<AttractivenessProvider>(lodge))
                    m_DropProvider.Add(new DropEntry { Lodge = lodge, SwitchedOff = false });
            }

            // 7. Publish: compare, swap, prune.
            bool changed = SnapshotChanged(m_Cur, next);
            if (LodgeSetChanged(m_Cur, next))
                m_LodgeSetRevision++;
            m_Next = m_Cur;
            m_Cur = next;
            if (changed)
                m_Generation++;
            Prune();
            m_StateHash = stateHash;

            // 8. Cost. The first recompute of a load pays one-off costs (Mono JIT of this call tree on the
            // session's first load, the first sync on the park query after the load, the first-call log lines
            // written inside the timed span), so it is kept apart: not in the mean or max, not tested for the
            // WARN; the [cost] line and the DUMP print it on its own.
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (m_Recomputes == 1)
            {
                m_FirstRecomputeMs = ms;
                return;
            }
            // Only recomputes with at least one area count toward the [cost] line and its WARN (an empty city's are trivial).
            if (m_Cur.SlotCount == 0)
                return;
            m_CostCount++;
            m_CostSumMs += ms;
            // The game clock of this recompute (TimeSystem: normalizedTime, year, normalizedDate,
            // daysPerYear, as SkiLiftTicketSystem's midday re-arm reads them).
            float t = m_Time.normalizedTime;
            int day = SkiDayRules.DayKey(m_Time.year, m_Time.normalizedDate, m_Time.daysPerYear);
            int monthKey = SkiMoneyRules.MonthKey(m_Time.year, m_Time.normalizedDate);
            int minute = SkiDayTiming.MinuteOfDay(t);
            if (ms > m_CostMaxMs)
            {
                m_CostMaxMs = ms;
                m_CostMaxMonth = monthKey;
                m_CostMaxMinute = minute;
            }
            int runs = m_Cur.Runs.Count;
            if (SkiDayRules.MiddayRearmDue(m_CostWarned, t, day, m_CostWarnDay))
                m_CostWarned = false;  // once per game day, re-armed at midday
            if (ms > Tuning.CostWarnMs && !m_CostWarned)
            {
                m_CostWarned = true;
                m_CostWarnDay = SkiDayRules.DoneDayOf(t, day);
                Mod.Log.Warn(Invariant($"Attractiveness [cost]: one recompute took {ms:F2} ms (> 2 ms) — ") +
                             $"{m_Cur.SlotCount} area(s), {runs} run(s), {m_Cur.LiftCount} lift(s); reason '{reason}' at " +
                             $"{MonthClockText(monthKey, minute)} (once per game day, re-armed at midday; the load's first " +
                             "recompute is not tested)");
            }
            if (m_CostCount == Tuning.CostReportRecomputes)
            {
                double pollMean = m_PollCount > 0 ? m_PollSumMs / m_PollCount : 0.0;
                Mod.Log.Info(Invariant($"Attractiveness [cost]: {Tuning.CostReportRecomputes} recomputes with an area, mean {m_CostSumMs / m_CostCount:F3} ms, ") +
                             Invariant($"max {m_CostMaxMs:F3} ms (last: {m_Cur.SlotCount} area(s), {runs} run(s), {m_Cur.LiftCount} lift(s), ") +
                             Invariant($"{m_Cur.LiveLodges} lodge(s), {m_Cur.Chords} chord(s)); state poll mean {pollMean:F3} ms over {m_PollCount} poll(s); ") +
                             Invariant($"first recompute after the load {m_FirstRecomputeMs:F3} ms (one-off costs, not in mean or max)"));
            }
        }

        /// <summary>The park walk: one slot per live Welcome Sign, every lodge building into m_LiveLodges.
        /// Syncs only PrefabRef's writers (chunk reads do not wait on running jobs — the ownership engine notes's
        /// rule; ParkedVehiclesSystem.cs:1732). Then the sign index for the lift
        /// rule (Engine notes: Sync points): re-read from the signs' Transforms when
        /// <paramref name="readPositions"/>, else kept from the last read — unless it no longer holds exactly
        /// this walk's signs (a missed tag: read anyway, counted). Returns whether positions were read.</summary>
        private bool CollectAreas(Snapshot next, bool readPositions)
        {
            m_LiveLodges.Clear();
            m_TopLevel.Clear();
            m_TopKind.Clear();
            if (!m_ParkQuery.IsEmptyIgnoreFilter)
            {
                // The live lodge view, once before the chunk loop.
                HashSet<Entity> lodgePrefabs = m_PrefabCatalog.LodgePrefabSet;
                HashSet<Entity> schoolPrefabs = m_PrefabCatalog.SchoolPrefabSet;
                HashSet<Entity> patrolPrefabs = m_PrefabCatalog.PatrolPrefabSet;
                HashSet<Entity> depotPrefabs = m_PrefabCatalog.DepotPrefabSet;
                EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                // Owner presence is an archetype fact (chunk.Has reads no component data, no sync): a lodge,
                // school or patrol hut without Owner is TOP-LEVEL (Engine notes: Top-level buildings); one with an Owner is counted
                // for no area.
                var ownerHandle = GetComponentTypeHandle<Owner>(isReadOnly: true);
                using (var chunks = m_ParkQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        var entities = chunks[c].GetNativeArray(entityHandle);
                        var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                        bool owned = chunks[c].Has(ref ownerHandle);
                        for (int i = 0; i < entities.Length; i++)
                        {
                            Entity prefab = prefabRefs[i].m_Prefab;
                            if (m_Ownership.IsGatePrefab(prefab))
                                next.AddSlot(entities[i]);
                            else if (lodgePrefabs.Contains(prefab))
                            {
                                m_LiveLodges.Add(entities[i]);
                                if (!owned)
                                    AddTopLevel(entities[i], SchoolLinkRules.KindLodge);
                            }
                            else if (!owned && schoolPrefabs.Contains(prefab))
                                AddTopLevel(entities[i], SchoolLinkRules.KindSchool);
                            else if (!owned && patrolPrefabs.Contains(prefab))
                                AddTopLevel(entities[i], SchoolLinkRules.KindPatrol);
                            else if (!owned && depotPrefabs.Contains(prefab))
                                AddTopLevel(entities[i], SchoolLinkRules.KindDepot);
                        }
                    }
                }
            }
            next.LiveLodges = m_LiveLodges.Count;

            if (!readPositions && !SignIndexMatches(next))
            {
                readPositions = true;
                m_PositionFallbacks++;
            }
            if (readPositions)
            {
                // The one Transform sync of this recompute: EntityManager.GetComponentData completes the
                // Transform writers (the move systems' jobs) on its first call; the rest are plain reads.
                m_SignIndex.Clear();
                for (int s = 0; s < next.SlotCount; s++)
                {
                    Entity sign = next.Slots[s].Sign;
                    float2 xz = EntityManager.TryGetComponent(sign, out Game.Objects.Transform t)
                        ? t.m_Position.xz
                        : new float2(float.NaN, float.NaN);   // never resolves (SkiResortIndex: non-finite)
                    m_SignIndex.Add(LiftCensusSystem.Ref(sign), xz);
                }
                m_PositionReads++;
            }
            return readPositions;
        }

        /// <summary>The cached sign index holds exactly the signs of this walk (count and membership).</summary>
        private bool SignIndexMatches(Snapshot next)
        {
            if (m_SignIndex.Count != next.SlotCount)
                return false;
            for (int s = 0; s < next.SlotCount; s++)
            {
                if (m_SignIndex.IndexOf(LiftCensusSystem.Ref(next.Slots[s].Sign)) < 0)
                    return false;
            }
            return true;
        }

        /// <summary>One sign's efficiency, mixed into <paramref name="hash"/> — the state poll's input. With a slot (the recompute)
        /// it also fills the slot: efficiency, percent and the factors below 1 worst first. The lodges, the school and the patrol huts
        /// are top-level and counted by LinkTopLevel. Without a slot (the poll) it only hashes.</summary>
        private void ReadSign(Entity sign, AreaSlot slot, ref ulong hash)
        {
            hash = SkiRunRatingRules.Mix(SkiRunRatingRules.Mix(hash, unchecked((uint)sign.Index)), unchecked((uint)sign.Version));
            if (!EntityManager.Exists(sign))
            {
                hash = SkiRunRatingRules.Mix(hash, 0xFFFFFFFFu);
                return;
            }

            int count = 0, noStaffCount = 0;
            float staffing = 1f;
            if (EntityManager.TryGetBuffer(sign, isReadOnly: true, out DynamicBuffer<Efficiency> factors))
            {
                count = factors.Length;
                if (m_FactorValues.Length < count)
                    m_FactorValues = new float[count];
                if (m_NoStaffValues.Length < count)
                    m_NoStaffValues = new float[count];
                for (int i = 0; i < count; i++)
                {
                    Efficiency factor = factors[i];
                    m_FactorValues[i] = factor.m_Efficiency;
                    // The three factors WorkProviderSystem writes (:279-281) split off for the top-level lodge slice.
                    if (factor.m_Factor == EfficiencyFactor.NotEnoughEmployees || factor.m_Factor == EfficiencyFactor.SickEmployees ||
                        factor.m_Factor == EfficiencyFactor.EmployeeHappiness)
                        staffing *= factor.m_Efficiency > 0f ? factor.m_Efficiency : 0f;
                    else
                        m_NoStaffValues[noStaffCount++] = factor.m_Efficiency;
                    if (slot != null && factor.m_Efficiency < 1f)
                        slot.Factors.Add(factor);
                }
            }
            float efficiency = SkiResortScore.SignEfficiency(m_FactorValues, count);
            int pct = (int)Math.Round(100.0 * efficiency);
            hash = SkiRunRatingRules.Mix(hash, unchecked((uint)pct));
            if (slot != null)
            {
                slot.EfficiencyValue = efficiency;
                slot.EfficiencyPct = pct;
                slot.SignEfficiencyPct = pct;  // LinkTopLevel may fold the area's top-level lodges in
                slot.SignStaffing = staffing;
                slot.SignNoStaffValue = noStaffCount == 0 ? 1f : SkiResortScore.SignEfficiency(m_NoStaffValues, noStaffCount);
                // Efficiency.CompareTo orders the highest value first (Efficiency.cs:36-43): reversed = worst first.
                slot.Factors.Sort();
                slot.Factors.Reverse();
            }

        }

        /// <summary>A school or patrol hut entry counts while its building exists and is neither Deleted
        /// nor Temp (they are not in the park walk: m_LiveLodges holds lodges only).</summary>
        private bool IsLiveUpgrade(Entity building)
        {
            return building != Entity.Null && EntityManager.Exists(building) && !EntityManager.HasComponent<Deleted>(building) &&
                   !EntityManager.HasComponent<Temp>(building);
        }

        /// <summary>One park-walk building without Owner into the top-level list, with its kind (SchoolLinkRules.Kind*).</summary>
        private void AddTopLevel(Entity building, byte kind)
        {
            m_TopLevel.Add(building);
            m_TopKind.Add(kind);
        }

        /// <summary>(Engine notes, Top-level buildings) Each top-level building of the last park walk mixes (entity, active) into
        /// <paramref name="hash"/> (three words, so the poll and the recompute agree), and a top-level lodge also its efficiency
        /// percent. With <paramref name="next"/> (the recompute) it also resolves the building's sign (SchoolLinkRules.Resolve over
        /// this recompute's sign index, TetherMeters), counts it into that area's slot, folds the lodges into the area's
        /// efficiency, logs link changes (SchoolLinkRules.Diff) and publishes the links. Active = the building's own
        /// Building.m_OptionMask not Inactive (BuildingUtils.cs:351-354); reading Building syncs its writers
        /// (BuildingPoliciesSystem, RoadConnectionSystem), once per poll / recompute and only while a top-level building exists.</summary>
        private void LinkTopLevel(Snapshot next, ref ulong hash)
        {
            if (next != null)
                m_TopLinksNext.Clear();
            IReadOnlyDictionary<Entity, LodgeDef> lodgeDefs = m_PrefabCatalog.LodgeDefs;
            for (int i = 0; i < m_TopLevel.Count; i++)
            {
                Entity building = m_TopLevel[i];
                byte kind = m_TopKind[i];
                bool live = IsLiveUpgrade(building);   // the poll walks the last recompute's list: a removed building reads not live
                bool active = live && EntityManager.TryGetComponent(building, out Building data) &&
                              !BuildingUtils.CheckOption(data, BuildingOption.Inactive);
                hash = SkiRunRatingRules.Mix(SkiRunRatingRules.Mix(SkiRunRatingRules.Mix(hash,
                    unchecked((uint)building.Index)), unchecked((uint)building.Version)), active ? 1u : 0u);
                float efficiency = 1f;
                if (kind == SchoolLinkRules.KindLodge && active)
                {
                    efficiency = ReadLodgeEfficiency(building, null);
                    hash = SkiRunRatingRules.Mix(hash, unchecked((uint)(int)Math.Round(100.0 * efficiency)));
                }
                if (next == null)
                    continue;
                EntityRef id = LiftCensusSystem.Ref(building);
                m_TopKindOf[id] = kind;
                SchoolLinkRules.Link link = new SchoolLinkRules.Link { Sign = EntityRef.Null, Distance = float.PositiveInfinity };
                if (TryCachedXz(m_TopXz, building, out float2 xz))
                    link = SchoolLinkRules.Resolve(m_SignIndex, xz, kind == SchoolLinkRules.KindDepot ? SchoolLinkRules.DepotMaxDistance : SkiRunOwnershipSystem.TetherMeters);
                m_TopLinksNext[id] = link;
                if (!link.Linked || !next.TryGetSlot(LiftCensusSystem.Ent(link.Sign), out AreaSlot slot))
                    continue;
                if (kind == SchoolLinkRules.KindSchool)
                {
                    SchoolLinkRules.Count(ref slot.Schools, ref slot.SchoolActive, active, live);
                }
                else if (kind == SchoolLinkRules.KindPatrol)
                {
                    if (active)
                        slot.PatrolEntities.Add(building);
                }
                else if (kind == SchoolLinkRules.KindDepot)
                {
                    if (active)
                        slot.DepotActive.Add(building);
                }
                else if (m_LiveLodges.Contains(building) && m_LodgeClaimed.Add(building))
                {
                    if (!active)
                    {
                        slot.Inactive.Add(building);
                        continue;
                    }
                    slot.Active.Add(building);
                    Entity prefab = EntityManager.TryGetComponent(building, out PrefabRef prefabRef) ? prefabRef.m_Prefab : Entity.Null;
                    LodgeDef def = null;
                    bool known = prefab != Entity.Null && lodgeDefs.TryGetValue(prefab, out def);
                    slot.ActiveInfo.Add(new LodgeInfo { Lodge = building, LodgeIndex = (short)(known ? def.Index : 0), SummitServed = false });
                    // The tier base is the weight (SkiCatalog.Lodges B; the school and patrol huts weigh 0).
                    double weight = known ? def.B : 0.0;
                    efficiency = ReadLodgeEfficiency(building, slot);
                    // A mountain lodge's jobs ride on the sign, so the sign's staffing is its slice's staffing
                    // (MountainJobsRules; the fold below then takes the sign term without it, so it counts once).
                    if (known && def.SignJobs > 0 && weight > 0.0)
                    {
                        efficiency = MountainJobsRules.MountainEfficiency(efficiency, slot.SignStaffing);
                        slot.MountainInMean = true;
                    }
                    slot.LodgeEffSum += weight * efficiency;
                    slot.LodgeWeightSum += weight;
                }
            }
            if (next == null)
                return;
            for (int s = 0; s < next.SlotCount; s++)
            {
                AreaSlot slot = next.Slots[s];
                if (!(slot.LodgeWeightSum > 0.0))
                    continue;
                float signTerm = MountainJobsRules.SignTerm(slot.EfficiencyValue, slot.SignNoStaffValue, slot.MountainInMean);
                float area = SchoolLinkRules.AreaEfficiency(signTerm, slot.LodgeEffSum, slot.LodgeWeightSum);
                slot.EfficiencyValue = area;
                slot.EfficiencyPct = (int)Math.Round(100.0 * area);
                slot.Factors.Sort();      // worst first again after the lodge merge (Efficiency.CompareTo: highest first)
                slot.Factors.Reverse();
            }
            SchoolLinkRules.Diff(m_TopLinks, m_TopLinksNext, m_TopLinkChanges);
            for (int i = 0; i < m_TopLinkChanges.Count; i++)
                LogTopLink(m_TopLinkChanges[i]);
            Dictionary<EntityRef, SchoolLinkRules.Link> swap = m_TopLinks;
            m_TopLinks = m_TopLinksNext;
            m_TopLinksNext = swap;
        }

        /// <summary>A top-level lodge's efficiency (SkiResortScore.SignEfficiency over its own Efficiency buffer: a
        /// top-level CityServiceBuilding has one, Park.cs:27-37; none → 1). With <paramref name="slot"/> its factors below 1 are
        /// merged into the area's list, keeping the worst value per factor (the hub's Efficiency rows).</summary>
        private float ReadLodgeEfficiency(Entity lodge, AreaSlot slot)
        {
            if (!EntityManager.TryGetBuffer(lodge, isReadOnly: true, out DynamicBuffer<Efficiency> factors))
                return 1f;
            int count = factors.Length;
            if (m_LodgeFactorValues.Length < count)
                m_LodgeFactorValues = new float[count];
            for (int i = 0; i < count; i++)
            {
                Efficiency factor = factors[i];
                m_LodgeFactorValues[i] = factor.m_Efficiency;
                if (slot == null || !(factor.m_Efficiency < 1f))
                    continue;
                int at = -1;
                for (int k = 0; k < slot.Factors.Count; k++)
                {
                    if (slot.Factors[k].m_Factor == factor.m_Factor)
                    {
                        at = k;
                        break;
                    }
                }
                if (at < 0)
                    slot.Factors.Add(factor);
                else if (factor.m_Efficiency < slot.Factors[at].m_Efficiency)
                    slot.Factors[at] = factor;
            }
            return SkiResortScore.SignEfficiency(m_LodgeFactorValues, count);
        }

        /// <summary>The one line per link change (Engine notes: Logs; SchoolLinkRules header LOG DISCIPLINE). The school says
        /// "School [link] ski school …"; lodges and patrol huts say "Lodge [link] lodge …" / "Patrol [link] patrol
        /// hut …".</summary>
        private void LogTopLink(SchoolLinkRules.Change c)
        {
            m_TopLinkLines++;
            m_TopKindOf.TryGetValue(c.School, out byte kind);
            if (c.Kind == SchoolLinkRules.ChangeKind.Removed)
                m_TopKindOf.Remove(c.School);
            bool depot = kind == SchoolLinkRules.KindDepot;
            string tag = kind == SchoolLinkRules.KindSchool ? "School" : kind == SchoolLinkRules.KindPatrol ? "Patrol" : depot ? "Depot" : "Lodge";
            string what = (kind == SchoolLinkRules.KindSchool ? "ski school " : kind == SchoolLinkRules.KindPatrol ? "patrol hut " : depot ? "bus depot " : "lodge ") +
                          IdOf(LiftCensusSystem.Ent(c.School));
            string role = kind == SchoolLinkRules.KindSchool ? "the school" : kind == SchoolLinkRules.KindPatrol ? "the patrol hut" : depot ? "the bus depot" : "the lodge";
            string rule = depot ? "nearest sign at any distance" : "nearest sign within 5000 m";
            string at = float.IsInfinity(c.After.Distance) ? "no position or no sign"
                : Invariant($"nearest sign {c.After.Distance:F0} m");
            switch (c.Kind)
            {
                case SchoolLinkRules.ChangeKind.Linked:
                    Mod.Log.Info($"{tag} [link] {what} -> Welcome Sign {IdOf(LiftCensusSystem.Ent(c.After.Sign))} ({at}; top-level, " +
                                 rule + ")");
                    break;
                case SchoolLinkRules.ChangeKind.Relinked:
                    Mod.Log.Info($"{tag} [link] {what} -> Welcome Sign {IdOf(LiftCensusSystem.Ent(c.After.Sign))} ({at}; was " +
                                 $"{IdOf(LiftCensusSystem.Ent(c.Before.Sign))}: a nearer sign takes {role})");
                    break;
                case SchoolLinkRules.ChangeKind.Unlinked:
                    Mod.Log.Info($"{tag} [link] {what} NOT LINKED ({at}; no Welcome Sign{(depot ? "" : " within 5000 m")}" +
                                 (c.Before.Linked ? $", was {IdOf(LiftCensusSystem.Ent(c.Before.Sign))}" : "") +
                                 "): it counts for no ski area");
                    break;
                default:
                    Mod.Log.Info($"{tag} [link] {what} removed" +
                                 (c.Before.Linked ? $" (was linked to {IdOf(LiftCensusSystem.Ent(c.Before.Sign))})" : " (was not linked)"));
                    break;
            }
        }

        /// <summary>The published link of a TOP-LEVEL resort building (lodge, school, patrol hut, bus depot; main thread, the
        /// last recompute): true when <paramref name="building"/> is one; <paramref name="sign"/> is its Welcome Sign, Entity.Null
        /// = Not linked. False for a ServiceUpgrade child (its sign is its Owner) or anything else. For the hub (world selection,
        /// the Not linked view) and the stats system.</summary>
        internal bool TryGetTopLevelLink(Entity building, out Entity sign)
        {
            sign = Entity.Null;
            if (!m_TopLinks.TryGetValue(LiftCensusSystem.Ref(building), out SchoolLinkRules.Link link))
                return false;
            if (link.Linked)
                sign = LiftCensusSystem.Ent(link.Sign);
            return true;
        }

        /// <summary>Every top-level resort building of the last recompute (lodges, the school, patrol huts, bus depots) with its
        /// sign (Entity.Null = Not linked), in the published order; both lists cleared first (the stats system's membership, jobs
        /// and cost sums; the hub's locks and Not linked count). Callers filter by PrefabRef when they need one kind.</summary>
        internal void CopyTopLevelLinks(List<Entity> buildings, List<Entity> signs)
        {
            buildings.Clear();
            signs.Clear();
            foreach (KeyValuePair<EntityRef, SchoolLinkRules.Link> pair in m_TopLinks)
            {
                buildings.Add(LiftCensusSystem.Ent(pair.Key));
                signs.Add(pair.Value.Linked ? LiftCensusSystem.Ent(pair.Value.Sign) : Entity.Null);
            }
        }

        /// <summary>Each area's active patrol huts' xz into slot.PatrolXz, from m_PatrolXz (read
        /// when due: a structural recompute or the backstop; a miss on a soft recompute reads it and is counted as a
        /// fallback, Engine notes: Sync points). A hut without a Transform is skipped (it patrols nothing).</summary>
        private void ReadPatrolPositions(Snapshot next)
        {
            for (int s = 0; s < next.SlotCount; s++)
            {
                AreaSlot slot = next.Slots[s];
                for (int i = 0; i < slot.PatrolEntities.Count; i++)
                {
                    if (TryCachedXz(m_PatrolXz, slot.PatrolEntities[i], out float2 xz))
                        slot.PatrolXz.Add(xz);
                }
            }
        }

        /// <summary>One building's xz through a per-load cache (cleared when positions are due, Engine notes:
        /// Sync points). A miss reads the Transform (counted as a fallback on a soft recompute); false without one.</summary>
        private bool TryCachedXz(Dictionary<Entity, float2> cache, Entity building, out float2 xz)
        {
            if (cache.TryGetValue(building, out xz))
                return true;
            if (!EntityManager.TryGetComponent(building, out Game.Objects.Transform transform))
                return false;
            xz = transform.m_Position.xz;
            cache[building] = xz;
            if (!m_PositionsReadThisRecompute)
                m_PositionFallbacks++;
            return true;
        }

        /// <summary>The bound-run walk: each member run's RunInput (Served filled later), its entity, its
        /// top node for the served rule (NaN without facts: served by nothing).</summary>
        private void CollectRuns(Snapshot next)
        {
            m_RunTops.Clear();
            m_RunOutlines.Clear();
            m_RunHeights.Clear();
            m_RunAreas.Clear();
            m_RunRefs.Clear();
            if (next.SlotCount == 0 || m_RunQuery.IsEmptyIgnoreFilter)
                return;
            m_RunQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            var geometryHandle = GetComponentTypeHandle<Geometry>(isReadOnly: true);
            var bindingHandle = GetComponentTypeHandle<SkiRunBinding>(isReadOnly: true);
            using (var chunks = m_RunQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = chunks[c].GetNativeArray(ref geometryHandle);
                    var bindings = chunks[c].GetNativeArray(ref bindingHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity sign = bindings[i].m_Gate;
                        if (sign == Entity.Null || !next.SlotOfSign.TryGetValue(sign, out int slotIndex))
                            continue;  // unbound, paid orphan, or a sign that is not live: not a member
                        int kind = m_Ownership.KindOf(prefabRefs[i].m_Prefab);
                        if (kind < 0)
                            continue;
                        // Every run kind is a member (the cat track since 2026-10-24, at its reduced weight: SkiResortScore.CatTrackFactor);
                        // the whitelist keeps an unknown kind out.
                        if (!RunKindRules.CountsTowardResort(kind))
                            continue;
                        AddRun(next.Slots[slotIndex], slotIndex, entities[i], kind, geometries[i]);
                    }
                }
            }
        }

        private void AddRun(AreaSlot slot, int slotIndex, Entity run, int kind, Geometry geometry)
        {
            float bottomY = geometry.m_Bounds.min.y;  // node heights (GeometrySystem.cs:499-576)
            float topY = geometry.m_Bounds.max.y;
            var input = new SkiResortScore.RunInput
            {
                Kind = (byte)kind,
                AreaM2 = geometry.m_SurfaceArea,
                TopM = topY,
                BottomM = bottomY,
            };

            // Rating: a current rating; else the last one seen this load; else pending.
            if (m_Catalog.TryGetRating(run, out byte state, out sbyte band, out float topM, out float bottomM))
            {
                m_LastRatings[run] = new LastRating { State = state, Band = band, TopM = topM, BottomM = bottomM };
                ApplyRating(ref input, state, band, topM, bottomM);
            }
            else if (m_LastRatings.TryGetValue(run, out LastRating last))
            {
                ApplyRating(ref input, last.State, last.Band, last.TopM, last.BottomM);
            }
            else
            {
                input.State = SkiResortScore.BandState.Pending;
            }

            // Open state: the snow chapter's multiplier; no climate yet → 0, estimated, "open unknown".
            RunSnow snow = m_Injection.GetRunSnow(run, (RunKind)kind, bottomY, topY);
            if (snow.Source == RunSnowSource.None)
            {
                input.OpenFactor = 0.0;
                input.OpenEstimated = true;
                slot.OpenUnknown++;
            }
            else
            {
                input.OpenFactor = snow.Multiplier;
                input.OpenEstimated = snow.Source == RunSnowSource.Estimated;
            }

            // The served-rule test point: the highest outline node.
            float3 top = new float3(float.NaN);
            float2[] outline = null;  // the run's outline xz for the run links (null without facts: never linked)
            float[] heights = null;   // its node heights, same order (the downhill rule)
            if (m_Catalog.TryGetRunFacts(run, out SkiRunCatalogSystem.RunFacts facts))
            {
                top = facts.TopNode;
                outline = facts.OutlineXZ;
                heights = facts.OutlineY;
            }

            slot.Runs.Add(input);
            slot.RunEntities.Add(run);
            slot.ServedVia.Add(Entity.Null);
            m_RunTops.Add(top);
            m_RunOutlines.Add(outline);
            m_RunHeights.Add(heights);
            m_RunAreas.Add(slotIndex);
            m_RunRefs.Add(new RunRef { Slot = slotIndex, Index = slot.Runs.Count - 1 });
        }

        /// <summary>The catalog's (state, band, heights) → BandState / Band / heights.
        /// Rated with a SlopeBand 0..4 → Rated (rating heights when finite, else the node heights already set);
        /// states 2-4 → Unrateable (node heights); anything else → Pending.</summary>
        private static void ApplyRating(ref SkiResortScore.RunInput input, byte state, sbyte band, float topM, float bottomM)
        {
            if (state == SkiRunRatingRules.RateRated && band >= 0 && band <= SkiResortScore.BandDoubleBlack)
            {
                input.State = SkiResortScore.BandState.Rated;
                input.Band = (byte)band;
                if (IsFinite(topM) && IsFinite(bottomM))
                {
                    input.TopM = topM;
                    input.BottomM = bottomM;
                }
            }
            else if (state == SkiRunRatingRules.RateNoTerrain || state == SkiRunRatingRules.RateTooLarge ||
                     state == SkiRunRatingRules.RateFailed)
            {
                input.State = SkiResortScore.BandState.Unrateable;
            }
            else
            {
                input.State = SkiResortScore.BandState.Pending;
            }
        }

        private static bool IsFinite(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }

        /// <summary>The lift rule: census rows whose two stations are live, Complete or Open; area by the
        /// base station's xz; power from the CopyServeLifts row of the same index. The base xz comes from
        /// m_BaseXz (Engine notes: Sync points): cleared and re-read when <paramref name="readPositions"/>,
        /// else reused; a station missing from it is read (a sync; counted when it happens on a soft
        /// recompute — a new row moves the census Revision, so it should not).</summary>
        private void CollectLifts(Snapshot next, bool readPositions)
        {
            if (readPositions)
                m_BaseXz.Clear();
            m_CensusRows.Clear();
            m_Census.CopyRows(m_CensusRows);
            bool aligned = m_ServeLifts.Count == m_CensusRows.Count;
            for (int k = 0; k < m_CensusRows.Count; k++)
            {
                LiftCensus.Row row = m_CensusRows[k];
                if (row.State != LiftCensus.State.Complete && row.State != LiftCensus.State.Open)
                    continue;
                if (row.Base.IsNull || row.Top.IsNull)
                    continue;
                Entity baseStation = LiftCensusSystem.Ent(row.Base);
                Entity topStation = LiftCensusSystem.Ent(row.Top);
                if (!IsLiveBuilding(baseStation) || !IsLiveBuilding(topStation))
                    continue;
                if (!m_BaseXz.TryGetValue(baseStation, out float2 baseXz))
                {
                    if (!EntityManager.TryGetComponent(baseStation, out Game.Objects.Transform transform))
                        continue;
                    baseXz = transform.m_Position.xz;
                    m_BaseXz[baseStation] = baseXz;
                    if (!readPositions)
                        m_PositionFallbacks++;
                }
                if (!m_SignIndex.TryResolve(baseXz, SkiRunOwnershipSystem.TetherMeters, out EntityRef signRef, out float _))
                    continue;   // Not linked: no area, no value
                Entity sign = LiftCensusSystem.Ent(signRef);
                if (!next.TryGetSlot(sign, out AreaSlot slot))
                    continue;

                LiftPower power = LiftPower.Unknown;
                if (aligned && m_ServeLifts[k].Id == row.Id && m_ServeLifts[k].Top == row.Top)
                {
                    power = m_ServeLifts[k].Power;
                    slot.ServeLifts.Add(k);  // this area's lifts for SummitServed
                }
                bool complete = row.State == LiftCensus.State.Complete;
                bool powered = LiftServedRule.IsPowered(power);
                // The row's lift type (SkiCatalog.Lifts index; Mixed rows
                // are Unfinished and skipped above, a NoType 255 reads the fixed-grip row in the score).
                slot.Lifts.Add(new SkiResortScore.LiftInput { Complete = complete, Powered = powered, TypeIndex = row.Type });
                Entity id = LiftCensusSystem.Ent(row.Id);
                next.Lifts[new LiftKey(id, topStation)] = new LiftScoreView
                {
                    Id = id,
                    Base = baseStation,
                    Top = topStation,
                    Sign = sign,
                    Complete = complete,
                    Powered = power != LiftPower.NoPower,
                    Counted = complete && powered,
                };
                next.LiftCount++;
            }
        }

        private bool IsLiveBuilding(Entity entity)
        {
            return entity != Entity.Null && EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity);
        }

        /// <summary>Compute per area, holds, write lists, views, area lines.</summary>
        private void ScoreAreas(Snapshot next, int k, int backcountry, bool gating, string reason)
        {
            var settings = new SkiResortScore.ScoreSettings { K = k, BackcountryPercent = backcountry, SnowGating = gating };
            bool ratingFailed = m_Catalog.ServiceState == SkiRunCatalogSystem.RatingServiceState.Failed;
            bool snowSettled = m_Injection.SnowSettled;
            m_HeldCount = 0;
            for (int s = 0; s < next.SlotCount; s++)
            {
                AreaSlot slot = next.Slots[s];
                BuildLodgeInputs(slot);  // tiers + SummitServed
                if (!m_AreaStates.TryGetValue(slot.Sign, out AreaState state))
                {
                    state = new AreaState();
                    m_AreaStates[slot.Sign] = state;
                }
                // The score takes the smoothed staffing factor; slot.EfficiencyValue / EfficiencyPct stay the live value.
                slot.StaffingUsed = state.StaffingFactor(slot.EfficiencyValue, m_Tick);
                slot.StaffingUsedPct = (int)Math.Round(100.0 * slot.StaffingUsed);
                var input = new SkiResortScore.AreaInput
                {
                    ActiveLodges = slot.Active.Count,
                    InactiveLodges = slot.Inactive.Count,
                    Efficiency = slot.StaffingUsed,
                    Settings = settings,
                    Lodges = slot.Lodges,  // the ladder (Count == ActiveLodges)
                    SchoolActive = slot.SchoolActive,
                };
                slot.Result = SkiResortScore.Compute(input, slot.Runs, slot.Lifts, slot.Parts);

                // The first write of this load waits for ratings and snow, at most 1,024 ticks.
                bool firstWrite = false, expired = false;
                if (!state.FirstWriteDone)
                {
                    if ((slot.Result.RunsPending == 0 || ratingFailed) && snowSettled)
                        firstWrite = true;
                    else if (state.HoldTicks < Tuning.RatingHoldMaxTicks)
                        slot.Held = true;
                    else
                        firstWrite = expired = true;
                    if (firstWrite)
                        state.FirstWriteDone = true;
                }
                if (slot.Held)
                    m_HeldCount++;

                // Active lodges get PerLodge — or, while held, the value saved in the city.
                for (int i = 0; i < slot.Active.Count; i++)
                {
                    Entity lodge = slot.Active[i];
                    if (!slot.Held)
                    {
                        next.WriteValues[lodge] = slot.Result.PerLodge;
                        next.LodgeArea[lodge] = slot.Sign;
                    }
                    else if (m_HeldValues.TryGetValue(lodge, out int held))
                    {
                        next.WriteValues[lodge] = held;
                        next.LodgeArea[lodge] = slot.Sign;
                    }
                }
                // A switched-off lodge carries no provider.
                for (int i = 0; i < slot.Inactive.Count; i++)
                {
                    if (EntityManager.HasComponent<AttractivenessProvider>(slot.Inactive[i]))
                        m_DropProvider.Add(new DropEntry { Lodge = slot.Inactive[i], SwitchedOff = true });
                }
                // A sign carrying a provider is held at 0 (not removed).
                if (EntityManager.HasComponent<AttractivenessProvider>(slot.Sign))
                {
                    m_ZeroSigns.Add(slot.Sign);
                    if (m_ZeroSignLogged.Add(slot.Sign))
                        Mod.Log.Info($"Attractiveness: Welcome Sign {IdOf(slot.Sign)} carries a game AttractivenessProvider (a city saved " +
                                     "before E1, or another mod) — held at 0 after the game's write each tick; the ski area's score " +
                                     "lives on its lodges");
                }

                // Run views.
                for (int i = 0; i < slot.RunEntities.Count; i++)
                {
                    next.Runs[slot.RunEntities[i]] = new RunScoreView
                    {
                        Run = slot.RunEntities[i],
                        Sign = slot.Sign,
                        Input = slot.Runs[i],
                        Part = slot.Parts[i],
                    };
                }

                // Logs.
                if (firstWrite)
                    LogFirstWrite(slot, state, expired);
                LogArea(slot, reason, ratingFailed);
            }
        }

        /// <summary>slot.Lodges (the score's AreaInput.Lodges) from slot.ActiveInfo, with
        /// SummitServed evaluated for the rows whose summer share rises when served (μ summit &gt; μ): the lodge's xz
        /// (m_LodgeXz, read when due) within Ladder.SummitServedRadiusM of the top stub tip of a complete, powered lift
        /// of this area (LiftServedRule.SummitServed over the area's m_ServeLifts rows). Allocation-free once grown.</summary>
        private void BuildLodgeInputs(AreaSlot slot)
        {
            slot.Lodges.Clear();
            bool liftsCopied = false;
            for (int i = 0; i < slot.ActiveInfo.Count; i++)
            {
                LodgeInfo info = slot.ActiveInfo[i];
                bool served = false;
                if (info.LodgeIndex >= 0 && info.LodgeIndex < SkiCatalog.Lodges.Count)
                {
                    LodgeDef def = SkiCatalog.Lodges[info.LodgeIndex];
                    if (def.MuSummit > def.Mu && TryCachedXz(m_LodgeXz, info.Lodge, out float2 xz))
                    {
                        if (!liftsCopied)
                        {
                            m_AreaServeLifts.Clear();
                            for (int k = 0; k < slot.ServeLifts.Count; k++)
                            {
                                int idx = slot.ServeLifts[k];
                                if (idx >= 0 && idx < m_ServeLifts.Count)
                                    m_AreaServeLifts.Add(m_ServeLifts[idx]);
                            }
                            liftsCopied = true;
                        }
                        served = LiftServedRule.SummitServed(xz, m_AreaServeLifts, SkiCatalog.Ladder.SummitServedRadiusM);
                    }
                }
                info.SummitServed = served;
                slot.ActiveInfo[i] = info;
                slot.Lodges.Add(new SkiResortScore.LodgeInput { LodgeIndex = info.LodgeIndex, SummitServed = served });
            }
        }

        private static string LodgeName(int lodgeIndex)
        {
            return lodgeIndex >= 0 && lodgeIndex < SkiCatalog.Lodges.Count ? SkiCatalog.Lodges[lodgeIndex].Prefab : "?";
        }

        private void LogFirstWrite(AreaSlot slot, AreaState state, bool expired)
        {
            if (expired)
            {
                if (state.HoldWarned)
                    return;
                state.HoldWarned = true;
                Mod.Log.Warn($"Attractiveness [first-write] area {IdOf(slot.Sign)}: after {Tuning.RatingHoldMaxTicks} ticks " +
                             $"({Tuning.RatingHoldMaxTicks * 16} frames): {slot.Result.RunsPending} run(s) unrated, snow settled " +
                             $"{m_Injection.SnowSettled}, rating service {m_Catalog.ServiceState} — writing now; unrated runs count 0 " +
                             "until rated (look for 'Catalog [rate]' and 'Snow [' lines)");
                return;
            }
            var held = new StringBuilder();
            for (int i = 0; i < slot.Active.Count; i++)
            {
                if (!m_HeldValues.TryGetValue(slot.Active[i], out int value))
                    continue;
                held.Append(held.Length > 0 ? ", " : "").Append(IdOf(slot.Active[i])).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
            }
            // Name both release conditions, not only the ratings.
            string ratings = slot.Result.RunsPending == 0 ? "runs rated" : "rating service failed";
            Mod.Log.Info($"Attractiveness [first-write] area {IdOf(slot.Sign)}: released after {state.HoldTicks} tick(s) of 16 frames " +
                         $"({ratings}, snow settled) — A {slot.Result.Attraction}, each of {slot.Result.ActiveLodges} active lodge(s) " +
                         $"{slot.Result.PerLodge} (held until now: {(held.Length > 0 ? held.ToString() : "nothing")})");
        }

        /// <summary>Area line: written when A, a count, a flag or WriteHeld changed since the area's last
        /// line, through the area's LogWindow (the first after a quiet window plain, the rest folded). A held
        /// area names its blocker: "ratings pending" while a member run is unrated
        /// and the rating service has not failed, else "snow settling" (the SnowSettled clock).
        /// A change that is efficiency-only — same counts, flags and held state (StructKey), same runs value, another
        /// efficiency percent, A within EfficiencyLineMinDeltaA of the logged A — is skipped (counted once per
        /// change, DUMP) unless the area wrote no line for EfficiencyLineQuietS. Log only: score, Generation and
        /// the hub are unaffected.</summary>
        private void LogArea(AreaSlot slot, string reason, bool ratingFailed)
        {
            SkiResortScore.Result r = slot.Result;
            ulong structKey = SkiRunRatingRules.FnvOffset;
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.ActiveLodges));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.InactiveLodges));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.Runs));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsOpen));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsCounted));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsPending));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsUnrateable));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsFlat));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsUnserved));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsEstimated));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)slot.OpenUnknown));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.Lifts));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.LiftsComplete));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.LiftsOpenSection));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.LiftsUnpowered));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.LiftsCounted));
            structKey = SkiRunRatingRules.Mix(structKey, (uint)r.Flags);
            // The ladder's best tier, the school and the patrolled runs.
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.LodgeBestIndex));
            structKey = SkiRunRatingRules.Mix(structKey, (r.SchoolActive ? 1u : 0u) | (r.GreenCounted ? 2u : 0u));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsPatrolled));
            bool heldForRatings = slot.Held && r.RunsPending > 0 && !ratingFailed;
            structKey = SkiRunRatingRules.Mix(structKey, !slot.Held ? 0u : heldForRatings ? 1u : 2u);
            ulong key = SkiRunRatingRules.Mix(SkiRunRatingRules.Mix(structKey, unchecked((uint)r.Attraction)),
                                              unchecked((uint)r.PerLodge));

            if (!m_AreaLogs.TryGetValue(slot.Sign, out AreaLog log))
            {
                log = new AreaLog();
                m_AreaLogs[slot.Sign] = log;
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (log.HasKey && log.Key == key)
                return;
            // An efficiency-only change (summary above).
            if (log.HasKey && structKey == log.StructKey && r.RunsValue == log.LoggedRunsValue &&
                slot.EfficiencyPct != log.LoggedEffPct && Math.Abs(r.Attraction - log.LoggedA) < Tuning.EfficiencyLineMinDeltaA &&
                !log.Window.QuietFor(now, Tuning.EfficiencyLineQuietS))
            {
                if (key != log.SkippedKey)
                {
                    m_EfficiencyLinesSkipped++;
                    log.SkippedKey = key;
                }
                return;
            }
            log.HasKey = true;
            log.Key = key;
            log.StructKey = structKey;
            log.LoggedA = r.Attraction;
            log.LoggedRunsValue = r.RunsValue;
            log.LoggedEffPct = slot.EfficiencyPct;

            // Each Invariant(...) holds ONE interpolated string (C# 9: a `+` of two of them is a plain string).
            // LodgeBase × LodgeShare = the ladder's applied value (display); the best tier is named when it
            // is not the A-frame; "lifts n × 6 × λ" stays for fixed-grip-only areas, other types print their summed value;
            // the school and patrolled runs are appended only when on / non-zero.
            string lodgeTier = r.LodgeBestIndex > 0 ? ", best " + LodgeName(r.LodgeBestIndex) : "";
            string liftsText = r.LiftsValue == SkiResortScore.LiftValue * r.LiftsCounted
                ? Invariant($"lifts {r.LiftsCounted} × 6 × {r.LiftShare:0.##}")
                : Invariant($"lifts {r.LiftsCounted} = {r.LiftsValue:0.##} pts × {r.LiftShare:0.##}");
            string extras = (r.SchoolActive ? (r.GreenCounted ? "; school on (+" + r.SchoolValue.ToString("0.##", CultureInfo.InvariantCulture) + ")"
                                                              : "; school on (no counted green run: +0)") : "") +
                            (r.RunsPatrolled > 0 ? Invariant($"; patrolled {r.RunsPatrolled}") : "");
            string text =
                Invariant($"Attractiveness: area {slot.Sign.Index}:{slot.Sign.Version} A {r.Attraction} = round(({r.LodgeBase:0.##} × {r.LodgeShare:0.##} + ") +
                Invariant($"{r.K} × sqrt({r.S:F1})) × {r.Efficiency:F2} staffing (sign now {slot.EfficiencyPct} %)) — lodges {r.ActiveLodges} active (+{r.InactiveLodges} off) × {r.PerLodge}") +
                lodgeTier + "; " +
                Invariant($"runs {r.RunsCounted}/{r.Runs} counted = {r.RunsValue:F1} pts ({r.RunsOpen} open, {r.RunsUnserved} without a lift, ") +
                Invariant($"{r.RunsPending} unrated, {slot.OpenUnknown} open unknown") +
                ((r.Flags & SkiResortScore.ScoreFlags.OpenEstimated) != 0 ? ", estimated" : "") + "); " +
                liftsText + Invariant($" ({r.LiftsOpenSection} with a section open, {r.LiftsUnpowered} unpowered); ") +
                Invariant($"variety ×{r.VarietyMultiplier:0.##}; vertical {r.VerticalM:F0} m ×{r.VerticalMultiplier:F2}") + extras +
                ((r.Flags & SkiResortScore.ScoreFlags.SummerMode) != 0 ? "; SUMMER" : "") +
                ((r.Flags & SkiResortScore.ScoreFlags.NoLodge) != 0 ? "; NO LODGE" : "") +
                (!slot.Held ? "" : heldForRatings ? "; HELD (ratings pending)" : "; HELD (snow settling)") +
                ((r.Flags & SkiResortScore.ScoreFlags.Clamped) != 0 ? "; CLAMPED" : "") + " — " + reason;
            switch (log.Window.Offer(now, false, text, r.Attraction, r.RunsValue, slot.EfficiencyPct))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info(text);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteAreaSummary(slot.Sign, log.Window, now);
                    break;
            }
        }

        private static void WriteAreaSummary(Entity sign, LogWindow window, float now)
        {
            Mod.Log.Info(Invariant($"Attractiveness: area {sign.Index}:{sign.Version} — {window.Count} change(s) folded over {window.SpanS(now):F0} s ") +
                         $"(A {window.Range(0, "F0")}, runs {window.Range(1, "F1")} pts, efficiency {window.Range(2, "F0")} %) — last: " +
                         window.LastText);
            window.Wrote(now);
        }

        /// <summary>Trailing fold summaries, once the window has passed (on the tick: silence impossible).</summary>
        private void FlushAreaLogs()
        {
            if (m_AreaLogs.Count == 0)
                return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            foreach (var pair in m_AreaLogs)
            {
                if (pair.Value.Window.TrailingDue(now))
                    WriteAreaSummary(pair.Key, pair.Value.Window, now);
            }
        }

        /// <summary>Every waiting fold, now (OnGamePreload, OnDestroy, an area that left).</summary>
        private void FlushAllAreaLogs()
        {
            if (m_AreaLogs.Count == 0)
                return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            foreach (var pair in m_AreaLogs)
            {
                if (pair.Value.Window.Count > 0)
                    WriteAreaSummary(pair.Key, pair.Value.Window, now);
            }
        }


        /// <summary>True when any area view, run view, lift view, efficiency factor list or written value
        /// differs (Engine notes: Generation). HoldTicks is not compared (the live count is read through the view).</summary>
        private static bool SnapshotChanged(Snapshot cur, Snapshot next)
        {
            if (cur.SlotCount != next.SlotCount || cur.Runs.Count != next.Runs.Count || cur.Lifts.Count != next.Lifts.Count ||
                cur.WriteValues.Count != next.WriteValues.Count)
                return true;
            for (int s = 0; s < next.SlotCount; s++)
            {
                AreaSlot a = next.Slots[s];
                if (!cur.TryGetSlot(a.Sign, out AreaSlot b))
                    return true;
                if (!a.Result.Equals(b.Result) || a.Held != b.Held || a.EfficiencyPct != b.EfficiencyPct ||
                    a.OpenUnknown != b.OpenUnknown || a.Factors.Count != b.Factors.Count)
                    return true;
                for (int i = 0; i < a.Factors.Count; i++)
                {
                    if (a.Factors[i].m_Factor != b.Factors[i].m_Factor || a.Factors[i].m_Efficiency != b.Factors[i].m_Efficiency)
                        return true;
                }
                // A published LodgeInfo that changed (tier, SummitServed) moves
                // Generation even when the score did not (e.g. SummitServed flipping in winter).
                if (a.ActiveInfo.Count != b.ActiveInfo.Count || !SameEntities(a.DepotActive, b.DepotActive))
                    return true;
                for (int i = 0; i < a.ActiveInfo.Count; i++)
                {
                    LodgeInfo x = a.ActiveInfo[i], y = b.ActiveInfo[i];
                    if (x.Lodge != y.Lodge || x.LodgeIndex != y.LodgeIndex || x.SummitServed != y.SummitServed)
                        return true;
                }
            }
            foreach (var pair in next.Runs)
            {
                if (!cur.Runs.TryGetValue(pair.Key, out RunScoreView old) || !SameRun(old, pair.Value))
                    return true;
            }
            foreach (var pair in next.Lifts)
            {
                if (!cur.Lifts.TryGetValue(pair.Key, out LiftScoreView old))
                    return true;
                LiftScoreView v = pair.Value;
                if (old.Base != v.Base || old.Sign != v.Sign || old.Complete != v.Complete || old.Powered != v.Powered ||
                    old.Counted != v.Counted)
                    return true;
            }
            foreach (var pair in next.WriteValues)
            {
                if (!cur.WriteValues.TryGetValue(pair.Key, out int old) || old != pair.Value)
                    return true;
            }
            return false;
        }

        /// <summary>The published (sign, active lodges) lists differ — the slot
        /// count, a sign of <paramref name="next"/> with no slot in <paramref name="cur"/>, or a sign's Active list
        /// in count or element order. Compared per sign through SlotOfSign, so a slot reorder (chunk order) is not
        /// a change.</summary>
        private static bool LodgeSetChanged(Snapshot cur, Snapshot next)
        {
            if (cur.SlotCount != next.SlotCount)
                return true;
            for (int s = 0; s < next.SlotCount; s++)
            {
                AreaSlot a = next.Slots[s];
                if (!cur.TryGetSlot(a.Sign, out AreaSlot b) || a.Active.Count != b.Active.Count || !SameEntities(a.DepotActive, b.DepotActive))
                    return true;
                for (int i = 0; i < a.Active.Count; i++)
                {
                    if (a.Active[i] != b.Active[i])
                        return true;
                }
            }
            return false;
        }

        private static bool SameEntities(List<Entity> a, List<Entity> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        private static bool SameRun(RunScoreView a, RunScoreView b)
        {
            SkiResortScore.RunInput x = a.Input, y = b.Input;
            return a.Run == b.Run && a.Sign == b.Sign && a.Part.Equals(b.Part) &&
                   x.Kind == y.Kind && x.State == y.State && x.Band == y.Band && x.Served == y.Served &&
                   x.Patrolled == y.Patrolled &&
                   x.OpenEstimated == y.OpenEstimated && Bits(x.AreaM2) == Bits(y.AreaM2) &&
                   Bits(x.OpenFactor) == Bits(y.OpenFactor) && Bits(x.TopM) == Bits(y.TopM) && Bits(x.BottomM) == Bits(y.BottomM);
        }

        private static long Bits(double x)
        {
            return BitConverter.DoubleToInt64Bits(x);
        }

        /// <summary>Per-load maps keep only what the last recompute still knows: area states and log windows
        /// of signs that left (their folds written first), last ratings of runs that left, damping counters
        /// of lodges no longer written.</summary>
        private void Prune()
        {
            if (m_AreaStates.Count > m_Cur.SlotCount)
            {
                m_ScratchEntities.Clear();
                foreach (var pair in m_AreaStates)
                {
                    if (!m_Cur.SlotOfSign.ContainsKey(pair.Key))
                        m_ScratchEntities.Add(pair.Key);
                }
                for (int i = 0; i < m_ScratchEntities.Count; i++)
                    m_AreaStates.Remove(m_ScratchEntities[i]);
            }
            if (m_AreaLogs.Count > m_Cur.SlotCount)
            {
                m_ScratchEntities.Clear();
                float now = UnityEngine.Time.realtimeSinceStartup;
                foreach (var pair in m_AreaLogs)
                {
                    if (m_Cur.SlotOfSign.ContainsKey(pair.Key))
                        continue;
                    if (pair.Value.Window.Count > 0)
                        WriteAreaSummary(pair.Key, pair.Value.Window, now);
                    m_ScratchEntities.Add(pair.Key);
                }
                for (int i = 0; i < m_ScratchEntities.Count; i++)
                    m_AreaLogs.Remove(m_ScratchEntities[i]);
            }
            if (m_LastRatings.Count > m_Cur.Runs.Count)
            {
                m_ScratchEntities.Clear();
                foreach (var pair in m_LastRatings)
                {
                    if (!m_Cur.Runs.ContainsKey(pair.Key))
                        m_ScratchEntities.Add(pair.Key);
                }
                for (int i = 0; i < m_ScratchEntities.Count; i++)
                    m_LastRatings.Remove(m_ScratchEntities[i]);
            }
            if (m_DampedTicks.Count > m_Cur.WriteValues.Count)
            {
                m_ScratchEntities.Clear();
                foreach (var pair in m_DampedTicks)
                {
                    if (!m_Cur.WriteValues.ContainsKey(pair.Key))
                        m_ScratchEntities.Add(pair.Key);
                }
                for (int i = 0; i < m_ScratchEntities.Count; i++)
                    m_DampedTicks.Remove(m_ScratchEntities[i]);
            }
            m_ScratchEntities.Clear();
        }

        // The anchored write: every tick (interval 16, right after vanilla AttractionSystem).
        private void WriteProviders()
        {
            // Switched-off and orphan lodges lose their provider once (a structural change; once per
            // lodge toggle, never per tick).
            if (m_DropProvider.Count > 0)
            {
                for (int i = 0; i < m_DropProvider.Count; i++)
                {
                    DropEntry drop = m_DropProvider[i];
                    if (!EntityManager.Exists(drop.Lodge) || !EntityManager.HasComponent<AttractivenessProvider>(drop.Lodge))
                        continue;
                    EntityManager.RemoveComponent<AttractivenessProvider>(drop.Lodge);
                    Mod.Log.Info($"Attractiveness: provider removed from lodge {IdOf(drop.Lodge)} — " +
                                 $"{(drop.SwitchedOff ? "switched off" : "on no live Welcome Sign")} (no attraction while it is)");
                }
                m_DropProvider.Clear();
            }

            // The written lodges.
            Dictionary<Entity, int> values = m_Cur.WriteValues;
            if (values.Count > 0)
            {
                bool toDetected = Mod.TourismOverhaulDetected;
                m_ScratchEntities.Clear();
                foreach (var pair in values)
                {
                    Entity lodge = pair.Key;
                    if (!EntityManager.Exists(lodge) || EntityManager.HasComponent<Deleted>(lodge))
                    {
                        m_ScratchEntities.Add(lodge);
                        continue;
                    }
                    if (!EntityManager.HasComponent<AttractivenessProvider>(lodge))
                    {
                        // The lodge now matches vanilla AttractionSystem's query; vanilla rewrites it from the
                        // lodge prefab (Attraction 0) per slice pass and our anchored write restores it.
                        EntityManager.AddComponentData(lodge, new AttractivenessProvider { m_Attractiveness = pair.Value });
                        if (m_ProviderAddedLogged.Add(lodge))
                            Mod.Log.Info($"Attractiveness: provider added to lodge {IdOf(lodge)} of area {IdOf(AreaOf(lodge))} (value {pair.Value})");
                        continue;
                    }
                    if (toDetected)
                        CheckDamping(lodge, pair.Value);
                    EntityManager.SetComponentData(lodge, new AttractivenessProvider { m_Attractiveness = pair.Value });
                }
                if (m_ScratchEntities.Count > 0)
                {
                    for (int i = 0; i < m_ScratchEntities.Count; i++)
                    {
                        values.Remove(m_ScratchEntities[i]);
                        m_Cur.LodgeArea.Remove(m_ScratchEntities[i]);
                    }
                    m_ScratchEntities.Clear();
                    m_Generation++;   // never hand a deleted lodge to the hub
                    // Consumers re-copy; the deleted lodge stays listed until the next
                    // recompute (slot.Active keeps it; their lookups reject it).
                    m_LodgeSetRevision++;
                }
            }

            // A sign carrying a provider is held at 0 after vanilla's write.
            for (int i = 0; i < m_ZeroSigns.Count; i++)
            {
                Entity sign = m_ZeroSigns[i];
                if (EntityManager.Exists(sign) && EntityManager.TryGetComponent(sign, out AttractivenessProvider provider) &&
                    provider.m_Attractiveness != 0)
                    EntityManager.SetComponentData(sign, new AttractivenessProvider { m_Attractiveness = 0 });
            }
        }

        /// <summary>Tourism Overhaul holding a lodge below what we wrote, on 16 consecutive ticks →
        /// one line per lodge per load (vanilla's own slice write of 0 hits one tick in 16 and resets the run).</summary>
        private void CheckDamping(Entity lodge, int written)
        {
            int current = EntityManager.GetComponentData<AttractivenessProvider>(lodge).m_Attractiveness;
            if (current < written - Math.Max(1, written / 10))
            {
                m_DampedTicks.TryGetValue(lodge, out int ticks);
                ticks++;
                m_DampedTicks[lodge] = ticks;
                if (ticks >= Tuning.DampedTicksToLog && m_DampedLogged.Add(lodge))
                    Mod.Log.Info($"Attractiveness: lodge {IdOf(lodge)} of area {IdOf(AreaOf(lodge))} — Tourism Overhaul holds its provider at " +
                                 $"{current} of {written} (crowding or its park gate); the game sums the lower value");
            }
            else if (m_DampedTicks.ContainsKey(lodge))
            {
                m_DampedTicks.Remove(lodge);
            }
        }

        private Entity AreaOf(Entity lodge)
        {
            return m_Cur.LodgeArea.TryGetValue(lodge, out Entity sign) ? sign : Entity.Null;
        }

        private static string IdOf(Entity e)
        {
            return e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);
        }

        // DUMP (hub dev tab through SkiResortStatsSystem.AppendDump)

        /// <summary>The hub DUMP's attractiveness block: the system line, one line per area, and
        /// one line per member run with its five factors. Event time only, main thread (the
        /// provider reads complete their writers). Appends only; never throws.</summary>
        internal void AppendDump(StringBuilder sb)
        {
            try
            {
                double mean = m_CostCount > 0 ? m_CostSumMs / m_CostCount : 0.0;
                double pollMean = m_PollCount > 0 ? m_PollSumMs / m_PollCount : 0.0;
                sb.Append("  attractiveness: active ").Append(IsActive).Append(", disabled ").Append(m_Disabled)
                  .Append(", recomputes ").Append(m_Recomputes).Append(" (").Append(m_PausedRecomputes)
                  .Append(" while paused, T120; by reason: ");
                AppendCounts(sb, m_RecomputesByReason);
                sb.Append("), requests ").Append(m_Requests).Append(" (by reason: ");
                AppendCounts(sb, m_RequestsByReason);
                sb.Append("), last '").Append(m_LastReason).Append("', pending ")
                  .Append(m_StructuralPending ? "structural" : m_SoftPending ? "soft" : "none");
                if (m_StructuralPending || m_SoftPending)
                    sb.Append(" ('").Append(m_PendingReason).Append("')");
                sb.Append(", next backstop in ").Append(m_TicksUntilRefresh).Append(" tick(s), generation ").Append(m_Generation)
                  .Append(", lodge set revision ").Append(m_LodgeSetRevision)
                  .Append(", cost mean ").Append(mean.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(" ms max ").Append(m_CostMaxMs.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(" ms at ").Append(MonthClockText(m_CostMaxMonth, m_CostMaxMinute))   // "-" before any
                  .Append(" (first after load ").Append(m_FirstRecomputeMs < 0.0 ? "-" : m_FirstRecomputeMs.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(" ms, apart), poll mean ").Append(pollMean.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(" ms, positions read ").Append(m_PositionReads).Append(" time(s) (structural or backstop; ")
                  .Append(m_PositionFallbacks).Append(" fallback read(s) on a soft recompute)")
                  .Append(", efficiency-only area lines skipped ").Append(m_EfficiencyLinesSkipped).Append('\n');
                // The top-level resort buildings (lodges, the school, patrol huts) and their published links.
                sb.Append("  top-level buildings (T100 / T113): ").Append(m_TopLinks.Count);
                foreach (KeyValuePair<EntityRef, SchoolLinkRules.Link> pair in m_TopLinks)
                {
                    m_TopKindOf.TryGetValue(pair.Key, out byte kind);
                    sb.Append("; ").Append(kind == SchoolLinkRules.KindSchool ? "school " : kind == SchoolLinkRules.KindPatrol ? "patrol " : kind == SchoolLinkRules.KindDepot ? "bus depot " : "lodge ")
                      .Append(IdOf(LiftCensusSystem.Ent(pair.Key))).Append(" -> ")
                      .Append(pair.Value.Linked ? IdOf(LiftCensusSystem.Ent(pair.Value.Sign)) : "NOT LINKED")
                      .Append(float.IsInfinity(pair.Value.Distance) ? "" : " (" + pair.Value.Distance.ToString("F0", CultureInfo.InvariantCulture) + " m)");
                }
                sb.Append("; link lines this load ").Append(m_TopLinkLines).Append('\n');

                for (int s = 0; s < m_Cur.SlotCount; s++)
                {
                    AreaSlot slot = m_Cur.Slots[s];
                    SkiResortScore.Result r = slot.Result;
                    int holdTicks = slot.Held && m_AreaStates.TryGetValue(slot.Sign, out AreaState state) ? state.HoldTicks : 0;
                    sb.Append("    area ").Append(IdOf(slot.Sign)).Append(": A ").Append(r.Attraction)
                      .Append(" (full ").Append(r.AttractionFullEfficiency).Append("), held ").Append(slot.Held)
                      .Append(" (").Append(holdTicks).Append(" ticks), efficiency ").Append(slot.EfficiencyPct)
                      .Append(" % now (sign ").Append(slot.SignEfficiencyPct).Append(" %, top-level lodge weight ")
                      .Append(slot.LodgeWeightSum.ToString("0.#", CultureInfo.InvariantCulture))
                      .Append("), staffing factor used ").Append(slot.StaffingUsedPct)
                      .Append(" %, lodges ").Append(r.ActiveLodges).Append('+').Append(r.InactiveLodges).Append(" × ").Append(r.PerLodge)
                      .Append(", runs ").Append(r.RunsCounted).Append('/').Append(r.Runs).Append(" (").Append(r.RunsOpen)
                      .Append(" open, ").Append(r.RunsPending).Append(" unrated), lifts ").Append(r.LiftsCounted).Append('/')
                      .Append(r.Lifts).Append(", flags ").Append(r.Flags).Append(", provider now ");
                    int damped = 0;
                    for (int i = 0; i < slot.Active.Count; i++)
                    {
                        Entity lodge = slot.Active[i];
                        sb.Append(i > 0 ? ", " : "").Append(IdOf(lodge)).Append('=');
                        if (EntityManager.Exists(lodge) && EntityManager.TryGetComponent(lodge, out AttractivenessProvider provider))
                            sb.Append(provider.m_Attractiveness);
                        else
                            sb.Append("none");
                        if (m_DampedLogged.Contains(lodge))
                            damped++;
                    }
                    if (slot.Active.Count == 0)
                        sb.Append('-');
                    sb.Append(", TO-damped ").Append(damped).Append(" lodge(s)\n");
                    AppendLadder(sb, slot);

                    for (int i = 0; i < slot.RunEntities.Count && i < slot.Parts.Count; i++)
                        AppendRun(sb, slot.RunEntities[i], slot.Runs[i], slot.Parts[i], ServedViaText(slot, i));
                }
            }
            catch (Exception e)
            {
                sb.Append("  attractiveness: DUMP failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        /// <summary>One DUMP line per area — the ladder (best tier, winter base,
        /// the season's applied value and extras, price tolerance), each active lodge's tier with its SummitServed flag, the
        /// school, the patrol and the per-type lift sums.</summary>
        private static void AppendLadder(StringBuilder sb, AreaSlot slot)
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            SkiResortScore.Result r = slot.Result;
            sb.Append("      ladder: best ").Append(r.LodgeBestIndex >= 0 ? LodgeName(r.LodgeBestIndex) : "-")
              .Append(", base ").Append(r.LodgeBase.ToString("0.##", ic)).Append(" (winter), applied ").Append(r.LodgeApplied.ToString("0.##", ic))
              .Append(" (extras ").Append(r.LodgeExtraApplied.ToString("0.##", ic)).Append(')')
              .Append(", price tolerance ").Append(r.PriceTolerance).Append("; lodges:");
            if (slot.ActiveInfo.Count == 0)
                sb.Append(" -");
            for (int i = 0; i < slot.ActiveInfo.Count; i++)
            {
                LodgeInfo info = slot.ActiveInfo[i];
                sb.Append(i > 0 ? "," : "").Append(' ').Append(IdOf(info.Lodge)).Append(' ').Append(LodgeName(info.LodgeIndex));
                if (info.SummitServed)
                    sb.Append(" summit-served");
            }
            sb.Append("; school ").Append(r.SchoolActive ? "on" : "off").Append(" (").Append(slot.Schools).Append(" listed, green counted ")
              .Append(r.GreenCounted ? "y" : "n").Append(", +").Append(r.SchoolValue.ToString("0.##", ic)).Append("); patrols ")
              .Append(slot.PatrolEntities.Count).Append(" (").Append(slot.PatrolXz.Count).Append(" with a position), bus depots ")
              .Append(slot.DepotActive.Count).Append(", runs patrolled ")
              .Append(r.RunsPatrolled).Append("; lifts value ").Append(r.LiftsValue.ToString("0.##", ic)).Append(", applied ")
              .Append(r.LiftsApplied.ToString("0.##", ic)).Append('\n');
        }

        /// <summary>The DUMP text of the run a run is served through ("Run name" or "run index:version"), or null when the run is served
        /// directly or not at all. The name is the catalog row's (the hub's run name).</summary>
        private string ServedViaText(AreaSlot slot, int i)
        {
            if (i >= slot.ServedVia.Count || slot.ServedVia[i] == Entity.Null)
                return null;
            Entity via = slot.ServedVia[i];
            return m_Catalog.TryGetRow(via, out SkiRunCatalogSystem.RunRow row) && !string.IsNullOrEmpty(row.Name)
                ? row.Name + " (" + via.Index + ":" + via.Version + ")"
                : via.Index + ":" + via.Version;
        }

        private static void AppendRun(StringBuilder sb, Entity run, SkiResortScore.RunInput input, SkiResortScore.RunPart part, string servedVia = null)
        {
            string kind = input.Kind == SkiResortScore.KindSnowmaking ? "snowmaking"
                        : input.Kind == SkiResortScore.KindNight ? "night"
                        : input.Kind == SkiResortScore.KindNatural ? "natural" : "groomed";
            string band = input.State == SkiResortScore.BandState.Pending ? "unrated"
                        : input.State == SkiResortScore.BandState.Unrateable ? "unrateable"
                        : input.Band == SkiResortScore.BandGreen ? "green"
                        : input.Band == SkiResortScore.BandBlue ? "blue"
                        : input.Band == SkiResortScore.BandBlack ? "black"
                        : input.Band == SkiResortScore.BandDoubleBlack ? "double black" : "flat";
            sb.Append(Invariant($"      run {run.Index}:{run.Version} {kind} {band}, {input.AreaM2:F0} m²: band {part.BandValue:0.##} × size {part.SizeFactor:F3} × ") +
                      Invariant($"type {part.TypeFactor:0.##} × served {part.ServedFactor:0.##} × open {part.OpenFactor:F2}") +
                      (input.OpenEstimated ? " (estimated)" : "") +
                      Invariant($" = {part.Value:F3}") + (part.Counted ? "" : " (not counted)") +
                      (input.Patrolled ? " (patrolled)" : "") +
                      (servedVia != null ? " (served via " + servedVia + ")" : "") + "\n");  // served through a linked run
        }

        /// <summary>"YYYY-MM HH:MM" from a SkiMoneyRules month key and a SkiDayTiming.MinuteOfDay
        /// minute (invariant culture); "-" before any (NoMonth or a negative minute).</summary>
        private static string MonthClockText(int monthKey, int minute)
        {
            if (monthKey == SkiDayRules.NoMonth || minute < 0)
                return "-";
            return SkiMoneyRules.YearOfKey(monthKey).ToString(CultureInfo.InvariantCulture) + "-" +
                   SkiMoneyRules.MonthOfKey(monthKey).ToString("D2", CultureInfo.InvariantCulture) + " " +
                   (minute / 60).ToString("D2", CultureInfo.InvariantCulture) + ":" +
                   (minute % 60).ToString("D2", CultureInfo.InvariantCulture);
        }

        private static void AppendCounts(StringBuilder sb, Dictionary<string, int> counts)
        {
            bool any = false;
            foreach (var pair in counts)
            {
                sb.Append(any ? ", " : "").Append(pair.Key).Append(' ').Append(pair.Value);
                any = true;
            }
            if (!any)
                sb.Append('-');
        }

        // Prefab state: SkiPrefabCatalogSystem resolves per load and its "PrefabCatalog [arm]" line names
        // every lodge FOUND / MISSING; this system still says, per load, whether it is inert (tolerated pre-import).
        private void LogLodgePrefabs()
        {
            // A pending name (not imported yet) is listed apart from an expected
            // one that is missing; the school and the patrol huts are listed too (they change the score once imported).
            var found = new List<string>(SkiCatalog.Lodges.Count);
            var missing = new List<string>(SkiCatalog.Lodges.Count);
            var pending = new List<string>(SkiCatalog.Lodges.Count + 3);
            var upgrades = new List<string>(3);
            for (int i = 0; i < SkiCatalog.Lodges.Count; i++)
            {
                string name = SkiCatalog.Lodges[i].Prefab;
                if (m_PrefabCatalog.EntityOf(name) != Entity.Null)
                    found.Add(name);
                else if (SkiCatalog.IsExpected(name))
                    missing.Add(name);
                else
                    pending.Add(name);
            }
            int depotRows = SkiCatalog.Depots.Count, schoolRows = SkiCatalog.Schools.Count;
            for (int i = 0; i < depotRows + schoolRows + SkiCatalog.Patrols.Count; i++)
            {
                string name = i < depotRows ? SkiCatalog.Depots[i].Prefab
                    : i < depotRows + schoolRows ? SkiCatalog.Schools[i - depotRows].Prefab
                    : SkiCatalog.Patrols[i - depotRows - schoolRows].Prefab;
                if (m_PrefabCatalog.EntityOf(name) != Entity.Null)
                    upgrades.Add(name);
                else if (SkiCatalog.IsExpected(name))
                    missing.Add(name);
                else
                    pending.Add(name);
            }
            string pendingText = pending.Count > 0 ? $"; pending (not imported yet): '{string.Join("', '", pending.ToArray())}'" : "";

            if (m_PrefabCatalog.LodgeCount == 0)
            {
                var all = new List<string>(missing);
                all.AddRange(pending);
                Mod.Log.Info("Attractiveness: lodge prefab not present — system inert until " +
                             $"'{string.Join("', '", all.ToArray())}' is imported (expected pre-import)");
                return;
            }
            Mod.Log.Info($"Attractiveness: lodge prefab(s) '{string.Join("', '", found.ToArray())}' from the prefab catalog" +
                         (upgrades.Count > 0 ? $"; school / patrol prefab(s) '{string.Join("', '", upgrades.ToArray())}'" : "") +
                         (missing.Count > 0 ? $"; not found (tolerated): '{string.Join("', '", missing.ToArray())}'" : "") + pendingText);
        }

        /// <summary>The [arm] line's model numbers, every one from SkiCatalog (invariant culture): the
        /// ladder rows, its area rules, the per-type lift values and summer shares, the school and the patrol.</summary>
        private static string ArmModelText()
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(640);
            sb.Append("lodges by tier: lodge base = B(best) + min(sum E(others), ").Append(SkiCatalog.Ladder.ExtraCap.ToString("0", ic)).Append(") (");
            for (int i = 0; i < SkiCatalog.Lodges.Count; i++)
            {
                LodgeDef d = SkiCatalog.Lodges[i];
                sb.Append(i > 0 ? ", " : "").Append(d.Id).Append(' ').Append(d.B.ToString("0.##", ic)).Append('/').Append(d.E.ToString("0.##", ic))
                  .Append(" mu ").Append(d.Mu.ToString("0.##", ic));
                if (d.MuSummit > d.Mu)
                    sb.Append(" (").Append(d.MuSummit.ToString("0.##", ic)).Append(" summit-served)");
                if (d.Luxury)
                    sb.Append(" luxury");
            }
            sb.Append("); lodge applied = the base in winter, in summer best B × mu + the others' E × mu (scaled when sum E > ")
              .Append(SkiCatalog.Ladder.ExtraCap.ToString("0", ic)).Append("); no limit on the number of lodges of any tier; summit-served = within ").Append(SkiCatalog.Ladder.SummitServedRadiusM.ToString("0", ic))
              .Append(" m of a complete, powered lift's top; lifts (complete and powered) value / summer share: ");
            for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
            {
                LiftTypeDef l = SkiCatalog.Lifts[i];
                sb.Append(i > 0 ? ", " : "").Append(l.Id).Append(' ').Append(l.ScoreValue.ToString("0.##", ic)).Append(" / ")
                  .Append(l.SummerShare.ToString("0.##", ic));
            }
            sb.Append("; school +").Append(SchoolRule.ScoreValue.ToString("0.##", ic)).Append(" to S while a green run counts; patrol: expert runs within ")
              .Append(PatrolRule.RadiusM.ToString("0", ic)).Append(" m ×").Append(PatrolRule.ExpertFactor.ToString("0.##", ic))
              .Append(", an unserved patrolled run served at least ").Append((PatrolRule.BackcountryServedMin * 100.0).ToString("0", ic)).Append(" %");
            return sb.ToString();
        }
    }
}
