// SkiGladeToolSystem - the Glade tool: a vanilla-style ToolBaseSystem. Hover a drawn ski run bound to a Ski Area Welcome Sign and
// it previews "bring this run to tree density L" for one of two targets (run interior, or its outside tree-line belt) as
// ordinary Temp objects: felling wild trees, removing this run's planted trees, or planting the missing trees of the plan
// stream. Left click applies through the vanilla pipeline (validation, cost tooltip, charging, refunds). Engine citations are
// `Game.X/File.cs:line` in tools/decompile/1.6.0f1; "1.6.2 :N" notes a line shift in 1.6.2f1.
// Emit-and-hold: the preview is emitted once and held while applyMode == None; never hold with Clear (ValidationSystem.cs:645
// strips Error/Warning when applyMode != None). Silence must be impossible: GladeHoverLog lines, one-shot "!!!" catch.
// Engine notes: EngineNotes/SkiGladeToolSystem.md

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiGladeToolSystem : ToolBaseSystem, IGladeTagSource
    {

        /// <summary>The variant every new planting is tagged with (the layout shuffle is not reachable)</summary>
        public const byte NewPlantingVariant = 0;

        /// <summary>All tuning constants; every one is logged in the per-load ARMED line (with SkiGladeCompose's InteriorDensity /
        /// BeltFactor tables)</summary>
        public static class Tuning
        {
            // The density is SkiGladeCompose.InteriorDensity(level); the planner's stream density is SkiGladeCompose.StreamDensityPerM2 =
            // 1.10 x the densest level's (see StreamDensity); no thin fraction is read
            public const float GladeMinSpacing = 5.5f;
            public const float InnerMargin = 2.5f;
            /// <summary>1.5 -> 3.1 m, so InnerMargin + BeltGap = 5.6 > max(5.5, 4.5) and a correctly placed member of one layer can never
            /// block a slot of the other (SkiGladeCompose.SeparationHolds, checked in the ARMED line)</summary>
            public const float BeltGap = 3.1f;
            public const float BeltDepth = 10f;
            public const float BeltDensity = 1f / 40f;
            public const float BeltCoverage = 0.55f;
            public const float BeltNoiseWavelength = 40f;
            public const float BeltMinSpacing = 4.5f;
            public const float DominantShare = 0.7f;
            /// <summary>Sapling, Young, Mature, Elderly. Never Elderly (it would age to Dead if Decoration were ever disabled); the planner
            /// maps these to the vanilla GetRandomAge constants (ToolUtils.cs:148-170).</summary>
            public static readonly float4 AgeWeights = new float4(0f, 0.25f, 0.75f, 0f);
            /// <summary>Provisional cap: the field run needs 3,717 plants + 495 fells in one apply; the frame cost of ~4,000-8,000 Temps
            /// under Mono is unmeasured. Drop to 2,000 if the apply frame hitches > 100 ms.</summary>
            public const int MaxPlantPerApply = 4000;
            /// <summary>As MaxPlantPerApply</summary>
            public const int MaxThinPerApply = 4000;
            /// <summary>Removed members per apply (the count rule's removals plus other-variant / other-generation tags)</summary>
            public const int MaxRemovePerApply = 4000;
            public const int AttemptsPerTree = 60;
            public const float VertexPropRadius = 3f;

            /// <summary>Tooltip names; index = level - 1. The locale keys SkiResort.GladeTooltip.LEVEL_NAME[1..8] carry the same texts.</summary>
            public static readonly string[] LevelNames =
            {
                "Clear", "Scattered trees", "Sparse", "Open glade", "Glade", "Open forest", "Forest", "Thick forest",
            };

            /// <summary>Apply check: a new untagged tree within this xz distance (m) of a planted spot validation canceled counts as "at a
            /// canceled spot" (definition positions are written verbatim, GenerateObjectsSystem.cs:1253)</summary>
            public const float CheckSpotRadiusM = 0.5f;
            /// <summary>Apply check: "near a felled tree" radius (m). A tree the OverrideSystem un-overrides overlapped the felled one; 6 m
            /// covers the widest vanilla tree footprint (unverified per prefab; a label, never a filter).</summary>
            public const float CheckFelledRadiusM = 6f;

            // Overlay geometry (visual only)
            public const float OutlineWidth = 1.5f;
            public const float BeltRingWidth = 1f;
            /// <summary>Ring diameter (m) around a felled / removed tree (hiding on)</summary>
            public const float ThinMarkerDiameter = 4f;
            /// <summary>Ring outline width (m); fill alpha is 0</summary>
            public const float RingOutlineM = 0.35f;
            /// <summary>Ring diameter while doomed-tree hiding is switched off in the dev tab (keep rings at 6-8 m)</summary>
            public const float FallbackRingDiameter = 7f;
            /// <summary>Default of the after-state preview; the dev tab toggles it per session (field fallback)</summary>
            public const bool HideDoomedTrees = true;
            /// <summary>Price of the dearest planted tree (Adult): the money-short test of the skip-set learning</summary>
            public const int MaxTreeCost = 50;
            /// <summary>Candidate-ring alpha factor on the vanilla hover color</summary>
            public const float CandidateAlpha = 0.4f;
            /// <summary>Candidate-ring copy refresh period (s). Not an order-version gate: the area tool rewrites the Node buffer in place,
            /// and binding changes raise no tag.</summary>
            public const float CandidateRefreshS = 1f;
            /// <summary>GladePlanCache entries (keyed by PlanKey)</summary>
            public const int PlanCacheEntries = 4;
            /// <summary>Plan keys with a learned skip set (oldest evicted)</summary>
            public const int SkipKeysMax = 16;

            /// <summary>Ring colors (explicit because RenderingSettingsPrefab's m_ErrorColor code default is a faint salmon,
            /// RenderingSettingsPrefab.cs:17)</summary>
            public static readonly UnityEngine.Color FellRingColor = new UnityEngine.Color(0.95f, 0.20f, 0.15f, 0.90f);
            public static readonly UnityEngine.Color ReplaceRingColor = new UnityEngine.Color(1.00f, 0.55f, 0.10f, 0.90f);
        }

        public override string toolID => "SkiResort Glade Tool";

        public bool IsArmed => m_Armed;
        public bool IsDisabled => m_Disabled;
        public bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;

        /// <summary>The tree density level 1..8 (a tool setting, not stored per run)</summary>
        public byte Level => m_Level;

        /// <summary>False = run interior (default), true = tree line</summary>
        public bool TreeLineTarget => m_TreeLine;

        // The hub's tree controls go through the tool's own entry points (TryActivate / Deactivate, SetLevel / SetTreeLine) and gray
        // out with the tool's own reason. Locale keys (root LocaleEN.cs); "" when the tool can be activated.
        public const string ReasonKeyDisabled = "SkiResort.Hub.GLADE_REASON[Disabled]";
        public const string ReasonKeyNotInGame = "SkiResort.Hub.GLADE_REASON[NotInGame]";
        public const string ReasonKeyNoRuns = "SkiResort.Hub.GLADE_REASON[NoRuns]";
        public const string ReasonKeyNoSpecies = "SkiResort.Hub.GLADE_REASON[NoSpecies]";

        /// <summary>Why TryActivate would refuse right now, as a locale key ("" = it would not refuse for arming reasons). Mirrors
        /// TryActivate's own gates: disabled after an exception (m_Disabled), else not armed this load (the reason OnGameLoadingComplete
        /// recorded: not a game, run prefabs missing, no tree species). TryActivate's third gate (ToolSystem.actionMode not Game) is
        /// covered by NotInGame: actionMode is set from the loaded mode (ToolSystem.cs:182). Plain field reads.</summary>
        public string UnavailableReasonKey => m_Disabled ? ReasonKeyDisabled : (m_Armed ? "" : m_UnarmedReasonKey);

        /// <summary>Tooltip / log name of a level ("Open glade" for 4); "?" outside 1..8</summary>
        public static string LevelName(int level)
        {
            return level >= 1 && level <= Tuning.LevelNames.Length ? Tuning.LevelNames[level - 1] : "?";
        }

        public Entity HoveredRun => m_PlannedRun;
        public string HoverStatus => m_HoverStatus;

        /// <summary>Non-Null only on the frame applyMode == Apply (read by SkiGladeTagSystem in the ApplyTool phase of that same frame,
        /// ToolOutputSystem.cs:22-30)</summary>
        public Entity ApplyingRun => applyMode == ApplyMode.Apply ? m_ApplyingRun : Entity.Null;

        /// <summary>Layer of the plan being applied (SkiGladeCompose.LayerGlade for the interior, LayerBelt for the tree line). Valid
        /// while ApplyingRun != Null.</summary>
        public byte ApplyingLayer => m_ApplyingLayer;

        /// <summary>Always NewPlantingVariant (0): no adoption, no roll. Kept so the tag system writes the stored variant from one place.</summary>
        public byte ApplyingVariant => NewPlantingVariant;

        /// <summary>Level of the plan being applied (tag line). Valid while ApplyingRun != Null.</summary>
        public byte ApplyingLevel => m_ApplyingLevel;

        /// <summary>Target of the plan being applied. Valid while ApplyingRun != Null.</summary>
        public bool ApplyingTreeLine => m_ApplyingTreeLine;

        // IGladeTagSource (SkiGladeTagSystem): this tool plants for a run, so an Apply frame is ApplyingRun != Null, exactly the old guard.
        bool IGladeTagSource.ApplyingNow => ApplyingRun != Entity.Null;
        Entity IGladeTagSource.ApplyingRun => ApplyingRun;
        byte IGladeTagSource.ApplyingLayer => m_ApplyingLayer;
        byte IGladeTagSource.ApplyingVariant => NewPlantingVariant;
        void IGladeTagSource.OnTagged(Unity.Collections.NativeList<Entity> created) { }

        /// <summary>The last compose's counts for the planned run (tooltip "Trees now N", "Target T"); valid while PlanHasCounts. Plain fields.</summary>
        public bool PlanHasCounts;
        public int PlanTarget, PlanNow, PlanWild, PlanFixed, PlanOwn, PlanKeep;

        /// <summary>The last compose's Shortfall (plant branch: trees the stream could not place, from learned blocked spots or no room)
        /// and Misplaced (this run's trees outside their layer's region, removed first). Valid while PlanHasCounts. Plain fields.</summary>
        public int PlanShortfall, PlanMisplaced;

        /// <summary>The last apply check line (engine notes, Apply check), "-" until one ran</summary>
        public string LastApplyCheck = "-";

        /// <summary>What the cursor resolved to this frame</summary>
        public GladeHoverKind HoverKind => m_HoverKind;

        /// <summary>State of the preview while the hover is Bound</summary>
        public GladePlanState PlanState => m_PlanState;

        /// <summary>Bound runs in this city (live: refreshed every CandidateRefreshS while the tool runs; seeded from the ownership census
        /// at activation). Tooltip NO_RUNS detail.</summary>
        public int CityBoundRuns => m_CityBoundRuns;

        // Last preview census (plain fields; the dev tab reads them at 10 Hz). PreviewThin / PreviewRemove count only Delete Temps
        // without Cancel (a canceled delete Temp keeps its original, ApplyObjectsSystem.cs:337-340, 474-482); those with Cancel are
        // PreviewDeleteCancelled.
        public int PreviewPlant, PreviewThin, PreviewCancelled, PreviewCost;
        /// <summary>Delete Temps whose original is one of this run's members (removed trees)</summary>
        public int PreviewRemove;
        /// <summary>Delete Temps carrying TempFlags.Cancel (their original stays)</summary>
        public int PreviewDeleteCancelled;
        /// <summary>Known validation-blocked slots of the current plan (skip-set size)</summary>
        public int PreviewBlockedKnown;
        /// <summary>Error entities while the preview is held (0 when none)</summary>
        public int PreviewErrors;
        /// <summary>What the last plan emitted (the tooltip shows these until the preview is ready, then the census)</summary>
        public int PlannedPlant, PlannedFell, PlannedRemove;

        /// <summary>Temps generated + validated (census meaningful)</summary>
        public bool PreviewReady => m_PreviewReady;

        /// <summary>What an Apply would change now</summary>
        public int LiveTemps => (PreviewPlant - PreviewCancelled) + PreviewThin + PreviewRemove;

        /// <summary>After-state hiding of doomed trees is on</summary>
        public bool HideDoomedTrees => m_HideDoomed;

        /// <summary>"Apply blocked by N error(s)" while a validated preview holds Error entities, else "-"</summary>
        public string BlockedStatus = "-";
        public string LastApplyCensus = "-";
        public string LastError = "-";
        /// <summary>One-line summary of the last re-plan (the plan line without its prefix)</summary>
        public string LastPlan = "-";

        /// <summary>Implements the dev-tab hook declared in SkiGladeToolSystem.Debug.cs (the "Species pool" row). Field read only (10 Hz cost).</summary>
        partial void DescribeSpeciesPoolForDebug(ref string text)
        {
            if (m_SpeciesPoolText != null)
                text = m_SpeciesPoolText;
        }


        // Includes SkiRunNaturalA: the tree tool works on natural runs (else the hover reads Foreign and the tool refuses) and outlines them
        private static readonly string[] RunSurfaceNames =
            { "SkiRunGroomedA", "SkiRunSnowmakingA", "SkiRunNightA", "SkiRunNaturalA" };
        /// <summary>Pool order matters: index 0 is dominant (planner DominantShare)</summary>
        private static readonly string[] SpeciesNames = { "SpruceTree01", "PineTree01", "BirchTree01" };

        // Status strings (dev tab). Kind-only ones are constants so they never allocate.
        private const string NoRunStatus = "no run";
        private const string HeldStatus = "held — input not live (over UI)";
        private const string NothingStatus = "nothing — raycast empty";
        private const string TerrainStatus = "terrain — no run here";
        /// <summary>Binding happens on draw (paused too) or when a sign is placed; a moved sign binds on the next unpaused 64-frame sweep</summary>
        private const string UnboundStatus =
            "unbound run — a Ski Area Welcome Sign must stand within 5 km of the run's center; " +
            "it binds at once when the run is drawn or a sign is placed (paused too), else on the " +
            "next unpaused 64-frame sweep";

        private const string WhyAfterApply = "after apply";
        private const byte LayerNone = SkiGladeCompose.LayerNone;

        // Hover reasons (dedup key part)
        private const byte ReasonNone = 0;
        private const byte ReasonOrphan = 1;        // Unbound: binding present, sign gone (paid orphan)
        private const byte ReasonForeignOwner = 2;  // Unbound: a live non-sign Owner (LeaveForeign)


        private ToolOutputBarrier m_Barrier;
        private TerrainSystem m_TerrainSystem;
        private Game.Objects.SearchSystem m_ObjectSearchSystem;
        private OverlayRenderSystem m_OverlayRenderSystem;
        private Game.Simulation.CitySystem m_CitySystem;
        private SkiRunOwnershipSystem m_Ownership;
        private SkiGladeTagSystem m_TagSystem;     // its create-path / tagged counts (apply check)

        private EntityQuery m_DefinitionQuery;
        private EntityQuery m_TempCensusQuery;
        private EntityQuery m_RunQuery;
        private EntityQuery m_RenderingSettingsQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;

        private readonly List<Entity> m_SpeciesEntities = new List<Entity>();
        private readonly List<float> m_SpeciesOffsetY = new List<float>();
        private string m_SpeciesPoolText = "(unresolved)";

        // Per-load state
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private string m_UnarmedReasonKey = ReasonKeyNotInGame;   // why !m_Armed (UnavailableReasonKey)
        private bool m_PlanKeyWarned;              // contract check: PlanKey(input) == result.PlanKey
        private bool m_LayerWarned;                // contract check: result.Layer == the target's layer
        private readonly HashSet<Entity> m_LegacyWarned = new HashSet<Entity>();

        // Player-facing parameters (kept across loads, session preferences)
        private bool m_TreeLine;
        private byte m_Level = SkiGladeCompose.DefaultLevel;
        private bool m_HideDoomed = Tuning.HideDoomedTrees;

        // Preview state machine
        private Entity m_PlannedRun;               // run the current definitions were planned for (Null = none)
        private bool m_PlannedTreeLine;
        private byte m_PlannedLevel = SkiGladeCompose.DefaultLevel;
        private byte m_PlannedLayer = LayerNone;
        private bool m_HasPending;
        private bool m_EmitPending;
        private bool m_PreviewReady;
        private Entity m_ApplyingRun;
        private byte m_ApplyingLayer = LayerNone;
        private byte m_ApplyingLevel = SkiGladeCompose.DefaultLevel;
        private bool m_ApplyingTreeLine;
        private int m_LastBlockedErrors = -1;
        private string m_ReplanWhy;                // non-null: re-plan on the next resolve (after apply / skip learned)
        private bool m_PendingForce;
        private int m_CensusFrame = -1;
        private int m_MisHiddenDoomed;
        private bool m_HideWarnedThisPlan;
        private bool m_SelfHealLoggedThisPlan;
        private bool m_MoneyShortThisPlan;

        // Hover state
        private GladeHoverKind m_HoverKind = GladeHoverKind.None;
        private GladePlanState m_PlanState = GladePlanState.None;
        private bool m_InputLive;
        private string m_HoverStatus = NoRunStatus;
        private StatusSlot m_StatusCur;
        private StatusSlot m_StatusPrev;
        private bool m_LastViaPoint;
        private float m_UnboundNearestSignM = -1f;
        private int m_CityBoundRuns;
        private int m_BoundRunsAtActivation = -1;
        private readonly GladeHoverLog m_HoverLog = new GladeHoverLog();
        private readonly HashSet<Entity> m_ViaPointLogged = new HashSet<Entity>();
        /// <summary>Bound runs found by the point scan while the raycast winner was the terrain (a raycast miss, WARNed once per run
        /// per activation)</summary>
        private readonly HashSet<Entity> m_RaycastMissWarned = new HashSet<Entity>();
        private int m_RaycastMisses;               // rising edges of a via-point miss, this activation
        private long m_PointScans;
        private float m_LastPointScanUs;
        private float m_MaxPointScanUs;

        // Composition (re-plan only)
        private readonly ComposeInput m_Input = new ComposeInput();
        private readonly ComposeResult m_Result = new ComposeResult();
        private bool m_HasResult;
        private readonly GladePlanCache m_PlanCache = new GladePlanCache(Tuning.PlanCacheEntries);
        private ulong m_CurrentPlanKey;
        private readonly Dictionary<ulong, HashSet<int>> m_SkipSlots = new Dictionary<ulong, HashSet<int>>();
        private readonly List<ulong> m_SkipKeyOrder = new List<ulong>();
        private int m_SkipEvictions;
        private readonly StringBuilder m_LineBuilder = new StringBuilder(512);

        // Emitted definitions of the pending preview
        private readonly Dictionary<long, int> m_EmittedSlots = new Dictionary<long, int>();   // PositionKey -> slot id
        private readonly HashSet<Entity> m_EmittedRemoveOriginals = new HashSet<Entity>();
        private int m_EmittedFell;
        private int m_EmittedRemove;

        // Census (re-plan only)
        private readonly List<Candidate> m_Candidates = new List<Candidate>();
        private readonly List<OwnTree> m_Own = new List<OwnTree>();
        private readonly HashSet<Entity> m_Processed = new HashSet<Entity>();
        private int m_CenWildInside, m_CenFixedInside, m_CenBand, m_CenForeign, m_CenOwnInside, m_CenOwnBand, m_CenOwnFixed;
        /// <summary>Overridden trees the census includes (inside: any kind; band: non-members). Diagnostic only: they are counted where they belong.</summary>
        private int m_CenOverriddenInside, m_CenOverriddenBand;
        private float m_CensusBandMargin;          // band margin of the last census (apply check reuses it)
        private bool m_CensusWithBand;

        // Apply check (engine notes). Snapshot taken on the Apply frame; consumed by the first Replan after it. Collections are reused
        // (cleared, never re-allocated).
        private bool m_CheckPending;
        private Entity m_CheckRun;
        private int m_CheckFrame;
        private bool m_CheckTreeLine;
        private byte m_CheckLevel;
        private float m_CheckBandMargin;
        private bool m_CheckWithBand;
        private int m_CheckOwnBefore;
        private int m_CheckFelled, m_CheckRemoved, m_CheckPlanted, m_CheckCancelled, m_CheckDeleteCancelled;
        private readonly HashSet<Entity> m_CheckOthersBefore = new HashSet<Entity>();
        private readonly HashSet<Entity> m_CheckOverriddenBefore = new HashSet<Entity>();
        private readonly HashSet<Entity> m_CheckDoomedOriginals = new HashSet<Entity>();
        private readonly HashSet<Entity> m_CheckOthersNow = new HashSet<Entity>();
        private readonly List<float3> m_CheckCancelledSpots = new List<float3>();
        private readonly List<float3> m_CheckPlantedSpots = new List<float3>();
        private readonly List<float3> m_CheckFelledSpots = new List<float3>();

        // Overlay caches (rebuilt on re-plan / every CandidateRefreshS; drawn without allocation)
        private float3[] m_OutlineRing = new float3[0];
        private int m_OutlineCount;
        private float3[] m_BeltRing = new float3[0];
        private int m_BeltCount;
        private float3[] m_ThinMarkers = new float3[0];
        private int m_ThinMarkerCount;
        private float3[] m_RemoveMarkers = new float3[0];
        private int m_RemoveMarkerCount;
        private float3[] m_CandidatePoints = new float3[0];
        private int[] m_CandidateStarts = new int[0];
        private int[] m_CandidateCounts = new int[0];
        private int m_CandidateRunCount;
        private int m_CandidatePointCount;
        private float m_CandidateNextRefresh;
        private UnityEngine.Color m_HoverColor = new UnityEngine.Color(0.5f, 0.5f, 1f, 0.5f);
        private UnityEngine.Color m_CandidateColor = new UnityEngine.Color(0.5f, 0.5f, 1f, 0.2f);
        // Glade outline colors come from RunOutlineRules.GladeOutlineColour (the one place); dashed candidate pieces are cut on the CPU
        // at each candidate refresh (PisteMapRules.DashRing, the piste map's cutter).
        private static readonly UnityEngine.Color s_GladeColor = new UnityEngine.Color(
            RunOutlineRules.GladeOutlineColour.x, RunOutlineRules.GladeOutlineColour.y, RunOutlineRules.GladeOutlineColour.z,
            RunOutlineRules.GladeOutlineColour.w);
        private static readonly UnityEngine.Color s_GladeCandidateColor = new UnityEngine.Color(
            RunOutlineRules.GladeOutlineColour.x, RunOutlineRules.GladeOutlineColour.y, RunOutlineRules.GladeOutlineColour.z,
            RunOutlineRules.GladeOutlineColour.w * RunOutlineRules.GladeCandidateAlphaFactor);
        private const int MaxCandidateDashes = 8192;
        private readonly List<float3> m_CandidateDashA = new List<float3>(256);
        private readonly List<float3> m_CandidateDashB = new List<float3>(256);
        private readonly List<float3> m_CandidateRing = new List<float3>(64);
        private static readonly UnityEngine.Color s_FellFill = new UnityEngine.Color(0.95f, 0.20f, 0.15f, 0f);
        private static readonly UnityEngine.Color s_ReplaceFill = new UnityEngine.Color(1.00f, 0.55f, 0.10f, 0f);

        // Running dependency handle, so the exception path can return what was scheduled
        private JobHandle m_Deps;

        private struct Candidate
        {
            public Entity Entity;
            public float3 Position;
            public bool Thinnable;
        }

        private struct OwnTree
        {
            public Entity Entity;
            public float3 Position;
            public byte Layer, Variant, PlanGen;
        }

        /// <summary>One resolved hover</summary>
        private struct HoverResult
        {
            public GladeHoverKind Kind;
            public Entity Run;
            public Entity Subject;
            public byte Reason;
            public bool ViaPoint;
            public Entity Hit;
            public float2 Point;
            public bool HasPoint;
        }

        /// <summary>Two-slot status memo (engine notes, Logging contract)</summary>
        private struct StatusSlot
        {
            public bool Valid;
            public GladeHoverKind Kind;
            public Entity Subject;
            public byte Reason;
            public string Text;
            public float SignDistance;
        }


        protected override void OnCreate()
        {
            base.OnCreate(); // registers the tool, creates the proxies, Enabled=false (ToolBaseSystem.cs:292-316)
            m_Barrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_ObjectSearchSystem = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<Game.Simulation.CitySystem>();
            // The bound test and the sign helpers live in the ownership system
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();

            // All{CreationDefinition} None{Updated} (ToolBaseSystem.cs:689-692): sees only definitions from earlier frames, never the ones
            // emitted this frame.
            m_DefinitionQuery = GetDefinitionQuery();
            m_TempCensusQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            // All our run surfaces (runs are un-owned, and the point scan must also see unbound and legacy runs). Game.Areas.Surface
            // narrows the walk to surface areas: every SurfacePrefab archetype carries it (Game.Prefabs/SurfacePrefab.cs:21); lots,
            // extractors, districts and map tiles never do. The run-prefab set narrows it to ours.
            m_RunQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<Node>(),
                    ComponentType.ReadOnly<Triangle>(),
                    ComponentType.ReadOnly<Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            // Vanilla hover color source (Game.Rendering/AreaBorderRenderSystem.cs:657-676)
            m_RenderingSettingsQuery = GetEntityQuery(ComponentType.ReadOnly<RenderingSettingsData>());
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            Mod.Log.Info($"{nameof(SkiGladeToolSystem)} created");
        }

        // GameSystemBase dispatches this from GameManager.onGameLoadingComplete regardless of Enabled (Game/GameSystemBase.cs:28-30,
        // 59-65), so a disabled tool still arms.
        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_UnarmedReasonKey = ReasonKeyNotInGame;
            LastError = "-";
            LastApplyCensus = "-";
            LastPlan = "-";
            BlockedStatus = "-";
            m_LastBlockedErrors = -1;
            // Nothing of the previous city may survive a load
            ResetPreviewState();
            ResetLoadCaches();

            // Hard gate: Game only, since planted trees are real objects and must never be baked into a map from the editor
            if (mode != GameMode.Game)
                return;

            try
            {
                ResolvePrefabs();
                ResolveSpecies();
                if (m_RunPrefabs.Count == 0)
                {
                    m_UnarmedReasonKey = ReasonKeyNoRuns;
                    Mod.Log.Info("Glade: run prefabs missing — glade tool inert (expected pre-import)");
                    return;
                }
                if (m_SpeciesEntities.Count < 1)
                {
                    m_UnarmedReasonKey = ReasonKeyNoSpecies;
                    Mod.Log.Info("Glade: no built-in tree species resolved — glade tool inert " +
                                 "(expected only if the base-game tree prefabs are missing)");
                    return;
                }

                bool separation = SkiGladeCompose.SeparationHolds(Tuning.InnerMargin, Tuning.BeltGap,
                    Tuning.GladeMinSpacing, Tuning.BeltMinSpacing);
                m_Armed = true;
                Mod.Log.Info(
                    $"Glade: ARMED — tool '{toolID}', {m_RunPrefabs.Count} run prefab(s) (bound test: " +
                    $"SkiRunOwnershipSystem.IsBound), species pool [{m_SpeciesPoolText}]; " +
                    $"dial (Round G3): Levels={SkiGladeCompose.Levels} DefaultLevel={SkiGladeCompose.DefaultLevel} " +
                    $"InteriorDensity per ha [{DensityTableText()}] BeltFactor [{BeltFactorTableText()}] " +
                    $"LevelNames [{string.Join(", ", Tuning.LevelNames)}] " +
                    $"NewPlantingVariant={NewPlantingVariant} planner stream density StreamDensityPerM2=" +
                    $"{StreamDensity():F5}/m² ({StreamDensity() * 10000f:0.#}/ha, level {SkiGladeCompose.Levels} = " +
                    $"{SkiGladeCompose.InteriorDensity(SkiGladeCompose.Levels) * 10000f:0.#}/ha + headroom for blocked spots); " +
                    "misplaced own trees (outside their layer's region after a run edit) removed first; " +
                    "Apply only for the current level and target; census counts Overridden trees (§8.1 defect); " +
                    $"CheckSpotRadiusM={Tuning.CheckSpotRadiusM} CheckFelledRadiusM={Tuning.CheckFelledRadiusM}; " +
                    $"GladeMinSpacing={Tuning.GladeMinSpacing} " +
                    $"InnerMargin={Tuning.InnerMargin} BeltGap={Tuning.BeltGap} " +
                    $"BeltDepth={Tuning.BeltDepth} BeltDensity={Tuning.BeltDensity:F5}/m² " +
                    $"BeltCoverage={Tuning.BeltCoverage} BeltNoiseWavelength={Tuning.BeltNoiseWavelength} " +
                    $"BeltMinSpacing={Tuning.BeltMinSpacing} DominantShare={Tuning.DominantShare} " +
                    $"AgeWeights=({Tuning.AgeWeights.x},{Tuning.AgeWeights.y},{Tuning.AgeWeights.z},{Tuning.AgeWeights.w}) " +
                    $"MaxPlantPerApply={Tuning.MaxPlantPerApply} MaxThinPerApply={Tuning.MaxThinPerApply} " +
                    $"MaxRemovePerApply={Tuning.MaxRemovePerApply} " +
                    $"AttemptsPerTree={Tuning.AttemptsPerTree} VertexPropRadius={Tuning.VertexPropRadius} " +
                    $"OutlineWidth={Tuning.OutlineWidth} " +
                    $"BeltRingWidth={Tuning.BeltRingWidth} ThinMarkerDiameter={Tuning.ThinMarkerDiameter} " +
                    $"RingOutlineM={Tuning.RingOutlineM} FallbackRingDiameter={Tuning.FallbackRingDiameter} " +
                    $"HideDoomedTrees={Tuning.HideDoomedTrees} (this session: {m_HideDoomed}) " +
                    $"MaxTreeCost={Tuning.MaxTreeCost} CandidateAlpha={Tuning.CandidateAlpha} " +
                    $"CandidateRefreshS={Tuning.CandidateRefreshS} PlanCacheEntries={Tuning.PlanCacheEntries} " +
                    $"SkipKeysMax={Tuning.SkipKeysMax} (skip sets per activation); " +
                    $"PlanGeneration={SkiGladeCompose.PlanGeneration} " +
                    $"ThinSeed=0x{SkiGladeCompose.ThinSeed:X8}; SeparationHolds={separation} " +
                    $"(InnerMargin + BeltGap = {Tuning.InnerMargin + Tuning.BeltGap} vs max MinSpacing " +
                    $"{math.max(Tuning.GladeMinSpacing, Tuning.BeltMinSpacing)}); rings fell " +
                    $"{ColorText(Tuning.FellRingColor)} remove {ColorText(Tuning.ReplaceRingColor)}, " +
                    $"outline only; target {TargetText(m_TreeLine)}, density L{m_Level} ({LevelName(m_Level)})");
                if (!separation)
                {
                    Mod.Log.Warn("Glade: layer separation does NOT hold (InnerMargin + BeltGap must " +
                                 "exceed both MinSpacings by SkiGladeCompose.SeparationMarginM) — a " +
                                 "member of one layer can block slots of the other; repeats may not be " +
                                 "exact no-ops");
                }
            }
            catch (Exception e)
            {
                HandleException(e, "load setup");
            }
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning(); // m_ForceUpdate = true + SetActions (ToolBaseSystem.cs:325-330)
            try
            {
                ResetPreviewState();   // also GladeHoverLog.Begin()
                // Skip sets live for one activation. PlanKey has no road / building / tile input, so a set kept for the load hid a slot whose
                // blocker was removed (or whose tile was bought) while the tool was closed, and the run read "complete" until a reload. The
                // player's ways to remove a blocker or buy a tile are other tools (bulldozer; map tiles go through SelectionToolSystem,
                // SelectionToolSystem.cs:605, :991 - likely), and only one tool is active (ToolSystem.cs:84-98), so the glade tool is stopped
                // and restarted in between. A blocker the simulation removes while the tool stays open is re-tested at the next activation.
                ClearSkipSets();
                applyMode = ApplyMode.None;
                ReadColors();

                // The last ownership census, no extra scan
                int b = m_Ownership.LastBound;
                int u = m_Ownership.LastUnbound;
                int o = m_Ownership.LastOrphaned;
                int g = m_Ownership.LastGates;
                m_BoundRunsAtActivation = b;
                m_CityBoundRuns = b > 0 ? b : 0;
                string census = b < 0
                    ? "unknown (no ownership census yet this load)"
                    : $"{b} bound run(s), {u} unbound, {o} orphaned, {g} sign(s)";
                Mod.Log.Info($"Glade: tool running — target {TargetText(m_TreeLine)}, density L{m_Level} " +
                             $"({LevelName(m_Level)}); this city " +
                             $"(last ownership census): {census}; hover a bound run to preview" +
                             (b == 0
                                 ? " — nothing to preview until a run is drawn within 5 km of a Welcome Sign"
                                 : "") +
                             (m_Ownership.PrefabSetsReady
                                 ? ""
                                 : " — ownership prefab sets NOT ready (no run can read as bound this load)"));
            }
            catch (Exception e)
            {
                HandleException(e, "OnStartRunning");
            }
        }

        protected override void OnStopRunning()
        {
            try
            {
                // The summary is built before ResetPreviewState (which restarts the log)
                string stop = StopLine(m_HoverLog.Summary(LastStateText()));
                if (m_RaycastMisses > 0)
                    stop += $"; RAYCAST MISSES {m_RaycastMisses} of the via-point hovers (terrain won over a bound run, WARNed above)";
                Mod.Log.Info(stop);
                // An Apply followed by the tool closing before any re-plan still gets its check (the switch lands on a later ToolUpdate,
                // ToolSystem.cs:312-327)
                FlushApplyCheck(canRun: true, "tool stopped on the apply frame");
                ResetPreviewState();
                // Safe: definitions without Updated are inert (GenerateObjectsSystem.cs:1730-1748). Never hand-delete our Temps: thinning Temps
                // put Hidden on the originals (GenerateObjectsSystem.cs:974-978) and only ToolClearSystem (:96-106) or
                // ApplyObjectsSystem.Cancel (:474-482) remove it. When the tool is switched from outside, the incoming default tool's first
                // frame clears them (DefaultToolSystem.cs:973-980: m_ForceUpdate set in OnStartRunning, ToolBaseSystem.cs:328, surfaces as
                // forceUpdate via :557 -> applyMode = Clear :979; or :900-922 when the cursor is over UI). Our Hidden on Delete Temps dies with
                // them (Deleted at the clear, destroyed at Cleanup).
                if (!m_DefinitionQuery.IsEmptyIgnoreFilter)
                    EntityManager.DestroyEntity(m_DefinitionQuery);
            }
            catch (Exception e)
            {
                HandleException(e, "OnStopRunning");
            }
            base.OnStopRunning(); // ResetActions (ToolBaseSystem.cs:333-339, 347-360)
        }

        public override PrefabBase GetPrefab() => null;                  // ToolBaseSystem.cs:426

        public override bool TrySetPrefab(PrefabBase prefab) => false;    // ToolBaseSystem.cs:428; ToolSystem.cs:263-277

        public override void InitializeRaycast()
        {
            base.InitializeRaycast(); // first: clears flags + zeroes masks (ToolBaseSystem.cs:430-443)
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain | TypeMask.Areas;
            m_ToolRaycastSystem.areaTypeMask = AreaTypeMask.Surfaces;
            // Kept, inert for un-owned runs (engine notes, Raycast): Game.Areas/RaycastJobs.cs:95 needs typeMask2 != None, set only by an Owner walk
            // (:84-88); it makes a run still carrying a legacy Owner(sign) resolve to the run. Never add StaticObjects.
            m_ToolRaycastSystem.raycastFlags |= RaycastFlags.SubElements;      // AreaToolSystem.cs:3051 (1.6.2 :3062)
        }

        // Base requires no Error entity unless ignoreErrors and no deleted originals (ToolBaseSystem.cs:533-540); a preview of only
        // canceled Temps (blocked spots before they are learned) is not applicable.
        protected override bool GetAllowApply()
        {
            return base.GetAllowApply() && m_PreviewReady && LiveTemps > 0;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            m_Deps = inputDeps;
            try
            {
                UpdateCore();
            }
            catch (Exception e)
            {
                HandleException(e, "OnUpdate");
                // Clear this frame (m_LastTool is still us, ToolSystem.cs:147-157), then leave
                applyMode = ApplyMode.Clear;
                try { Deactivate(); } catch (Exception) { }
            }
            return m_Deps;
        }


        public bool TryActivate(string why)
        {
            try
            {
                if (m_Disabled)
                {
                    Mod.Log.Info($"Glade: activation refused ({why}) — disabled after an exception " +
                                 "until the next game load");
                    return false;
                }
                if (!m_Armed)
                {
                    Mod.Log.Info($"Glade: activation refused ({why}) — not armed (not in a game, or " +
                                 "run/species prefabs missing; see the load log)");
                    return false;
                }
                if (!m_ToolSystem.actionMode.IsGame())
                {
                    Mod.Log.Info($"Glade: activation refused ({why}) — action mode is not Game");
                    return false;
                }
                if (IsActive)
                    return true;
                m_ToolSystem.activeTool = this; // the switch happens next ToolUpdate (ToolSystem.cs:84-98)
                Mod.Log.Info($"Glade: activated ({why}) — target {TargetText(m_TreeLine)}, density L{m_Level} " +
                             $"({LevelName(m_Level)})");
                return true;
            }
            catch (Exception e)
            {
                HandleException(e, "TryActivate");
                return false;
            }
        }

        public void Deactivate()
        {
            if (m_ToolSystem != null && m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
                Mod.Log.Info("Glade: deactivated");
            }
        }

        /// <summary>Run interior / tree line (Alt+G, persisted action GladeNextMode). The next ToolUpdate re-plans the hovered run
        /// (UpdateFrame compares the planned target). Logs "Glade: target -> Tree line".</summary>
        public void ToggleTarget(string why)
        {
            m_TreeLine = !m_TreeLine;
            Mod.Log.Info($"Glade: target → {TargetText(m_TreeLine)} — {why}");
        }

        /// <summary>Level + 1 with wrap (... 7 -> 8 -> 1 ...) (Shift+G, persisted action GladeNextVariant). Logs "Glade: density -> L5
        /// (Glade)".</summary>
        public void StepLevel(string why)
        {
            int next = m_Level + 1;
            if (next > SkiGladeCompose.Levels || next < 1)
                next = 1;
            m_Level = (byte)next;
            Mod.Log.Info($"Glade: density → L{m_Level} ({LevelName(m_Level)}) — {why}");
        }

        /// <summary>Sets the level directly (hub density stepper), clamped to 1..SkiGladeCompose.Levels. Same effect as StepLevel: only
        /// m_Level changes, and the next ToolUpdate re-plans because UpdateFrame compares the planned level (Apply only for the current
        /// level). Works while the tool is inactive too (the level is a session setting). One log line per call, in the hotkey's format,
        /// also when nothing changes. Main thread (the hub applies it in its UIUpdate OnUpdate).</summary>
        public void SetLevel(int level, string why)
        {
            int clamped = level < 1 ? 1 : (level > SkiGladeCompose.Levels ? SkiGladeCompose.Levels : level);
            string clampNote = clamped != level ? $" (requested {level}, clamped to 1-{SkiGladeCompose.Levels})" : "";
            if (clamped == m_Level)
            {
                Mod.Log.Info($"Glade: density stays L{m_Level} ({LevelName(m_Level)}) — {why}{clampNote}");
                return;
            }
            m_Level = (byte)clamped;
            Mod.Log.Info($"Glade: density → L{m_Level} ({LevelName(m_Level)}) — {why}{clampNote}");
        }

        /// <summary>Sets the target directly (false = run interior, true = tree line). Same effect as ToggleTarget; one log line per
        /// call, also when nothing changes. Main thread.</summary>
        public void SetTreeLine(bool treeLine, string why)
        {
            if (treeLine == m_TreeLine)
            {
                Mod.Log.Info($"Glade: target stays {TargetText(m_TreeLine)} — {why}");
                return;
            }
            m_TreeLine = treeLine;
            Mod.Log.Info($"Glade: target → {TargetText(m_TreeLine)} — {why}");
        }

        /// <summary>Switches the after-state hiding of doomed trees on or off for this session (dev-tab field fallback). Hold frames fix
        /// the pending preview's Delete Temps (hide or un-hide) on the next frame; rings switch size at once.</summary>
        public void SetHideDoomedTrees(bool on, string why)
        {
            if (m_HideDoomed == on)
                return;
            m_HideDoomed = on;
            m_SelfHealLoggedThisPlan = false;
            Mod.Log.Info($"Glade: doomed-tree hiding {(on ? "ON (after-state preview)" : "OFF (fallback: doomed trees stay visible, rings " + Tuning.FallbackRingDiameter + " m)")} ({why})");
        }

        public void DumpToLog()
        {
            try
            {
                var sb = new StringBuilder(2048);
                sb.Append("Glade: DUMP — armed ").Append(m_Armed)
                  .Append(", disabled ").Append(m_Disabled)
                  .Append(", active ").Append(IsActive)
                  .Append(", target ").Append(TargetText(m_TreeLine))
                  .Append(", density L").Append(m_Level).Append(" (").Append(LevelName(m_Level)).Append(')')
                  .Append(", new plantings tagged variant ").Append(NewPlantingVariant).Append('\n');
                sb.Append("  hover: kind ").Append(m_HoverKind)
                  .Append(", input live ").Append(m_InputLive)
                  .Append(", status: ").Append(m_HoverStatus)
                  .Append("; unbound nearest sign ").Append(DistanceText(m_UnboundNearestSignM)).Append('\n');
                sb.Append("  planned run ").Append(EntityText(m_PlannedRun))
                  .Append(" (target ").Append(TargetText(m_PlannedTreeLine))
                  .Append(", density L").Append(m_PlannedLevel)
                  .Append(", layer ").Append(LayerText(m_PlannedLayer))
                  .Append("), pending ").Append(m_HasPending)
                  .Append(", ready ").Append(m_PreviewReady)
                  .Append(", plan state ").Append(m_PlanState)
                  .Append(", applyMode ").Append(applyMode)
                  .Append(", re-plan request ").Append(m_ReplanWhy ?? "-").Append('\n');
                sb.Append("  plan counts: ");
                if (PlanHasCounts)
                {
                    sb.Append("target ").Append(PlanTarget)
                      .Append(", now ").Append(PlanNow)
                      .Append(" (wild ").Append(PlanWild)
                      .Append(", fixed ").Append(PlanFixed)
                      .Append(", own ").Append(PlanOwn)
                      .Append("), keep ").Append(PlanKeep)
                      .Append(", shortfall ").Append(PlanShortfall)
                      .Append(", misplaced ").Append(PlanMisplaced).Append('\n');
                }
                else
                {
                    sb.Append("none (no compose for the planned run)\n");
                }
                sb.Append("  preview: plant ").Append(PreviewPlant)
                  .Append(" (canceled ").Append(PreviewCancelled)
                  .Append("), thin ").Append(PreviewThin)
                  .Append(", remove ").Append(PreviewRemove)
                  .Append(", canceled delete Temps ").Append(PreviewDeleteCancelled)
                  .Append(", live ").Append(LiveTemps)
                  .Append(", cost ").Append(PreviewCost)
                  .Append(", errors ").Append(PreviewErrors)
                  .Append(", blocked known ").Append(PreviewBlockedKnown)
                  .Append("; emitted plant/fell/remove ").Append(PlannedPlant).Append('/').Append(PlannedFell)
                  .Append('/').Append(PlannedRemove).Append('\n');
                sb.Append("  overlay: outline ").Append(m_OutlineCount)
                  .Append(" nodes, belt ring ").Append(m_BeltCount)
                  .Append(", fell rings ").Append(m_ThinMarkerCount)
                  .Append(", remove rings ").Append(m_RemoveMarkerCount)
                  .Append(", candidate rings ").Append(m_CandidateRunCount)
                  .Append("; bound runs in city live ").Append(m_CityBoundRuns)
                  .Append(" / at activation ").Append(m_BoundRunsAtActivation)
                  .Append("; emitted slots ").Append(m_EmittedSlots.Count)
                  .Append(", remove originals ").Append(m_EmittedRemoveOriginals.Count)
                  .Append("; doomed-tree hiding ").Append(m_HideDoomed ? "on" : "OFF (fallback)")
                  .Append(", mis-hidden now ").Append(m_MisHiddenDoomed)
                  .Append("; candidate ring nodes ").Append(m_CandidatePointCount)
                  .Append("; hover color alpha ").Append(m_HoverColor.a.ToString("F3"))
                  .Append(" → candidate alpha ").Append(m_CandidateColor.a.ToString("F3")).Append('\n');
                sb.Append("  last census: wild inside ").Append(m_CenWildInside)
                  .Append(" thinnable + ").Append(m_CenFixedInside).Append(" fixed, band ").Append(m_CenBand)
                  .Append(", foreign members ").Append(m_CenForeign)
                  .Append(", own members inside ").Append(m_CenOwnInside)
                  .Append(" + band ").Append(m_CenOwnBand)
                  .Append(" (+ ").Append(m_CenOwnFixed).Append(" burning/owned, fixed)")
                  .Append("; Overridden (hidden by the game, counted) inside ").Append(m_CenOverriddenInside)
                  .Append(", band ").Append(m_CenOverriddenBand).Append('\n');
                if (m_HasResult)
                {
                    ComposeResult r = m_Result;
                    sb.Append("  last compose: status ").Append(r.Status)
                      .Append(", layer ").Append(LayerText(r.Layer))
                      .Append(", target ").Append(r.Target)
                      .Append(", inside ").Append(r.Inside)
                      .Append(" (wild ").Append(r.WildInside)
                      .Append(", fixed ").Append(r.FixedInside)
                      .Append(", own ").Append(r.OwnInside)
                      .Append("), keep ").Append(r.Keep)
                      .Append("; raw ").Append(r.RawTarget)
                      .Append(", slice ").Append(r.Slice)
                      .Append(", same ").Append(r.Same)
                      .Append(", matched ").Append(r.Matched)
                      .Append(", other ").Append(r.Other)
                      .Append(", missing ").Append(r.Missing)
                      .Append(", skipped ").Append(r.Skipped)
                      .Append(", deficit ").Append(r.Deficit)
                      .Append(", shortfall ").Append(r.Shortfall)
                      .Append(", own unmatched ").Append(r.OwnUnmatched)
                      .Append(", other-tagged ").Append(r.OtherTagged)
                      .Append(", misplaced ").Append(r.Misplaced)
                      .Append(" → plant ").Append(r.Plant.Count)
                      .Append(", remove ").Append(r.Remove.Count)
                      .Append(", fell ").Append(r.Fell.Count).Append(" of ").Append(r.FellFull)
                      .Append("; clamped ").Append(r.Clamped)
                      .Append(", saturated ").Append(r.Saturated)
                      .Append(", complete ").Append(r.Complete)
                      .Append(", full re-plan ").Append(r.FullReplan)
                      .Append(", full for members ").Append(r.FullForMembers)
                      .Append(", cache hit ").Append(r.CacheHit)
                      .Append("; attempts ").Append(r.Plan.Attempts)
                      .Append(", plan key ").Append(r.PlanKey.ToString("X16")).Append('\n');
                }
                else
                {
                    sb.Append("  last compose: none since load\n");
                }
                sb.Append("  plan cache: hits ").Append(m_PlanCache.Hits)
                  .Append(", misses ").Append(m_PlanCache.Misses)
                  .Append(", entries ").Append(m_PlanCache.Count).Append('/').Append(m_PlanCache.Capacity)
                  .Append("; skip sets ").Append(m_SkipSlots.Count).Append('/').Append(Tuning.SkipKeysMax)
                  .Append(" key(s), ").Append(m_SkipEvictions).Append(" eviction(s) (lifetime: this activation)").Append('\n');
                sb.Append("  hover log: logged ").Append(m_HoverLog.Logged)
                  .Append(", suppressed ").Append(m_HoverLog.Suppressed)
                  .Append(", plans ").Append(m_HoverLog.Plans)
                  .Append(", applies ").Append(m_HoverLog.Applies)
                  .Append(", via point ").Append(m_HoverLog.ViaPoint)
                  .Append(" (raycast misses ").Append(m_RaycastMisses).Append(')')
                  .Append(", dwell total ").Append(m_HoverLog.TotalSeconds.ToString("F1")).Append(" s")
                  .Append("; point scans ").Append(m_PointScans)
                  .Append(", last ").Append(m_LastPointScanUs.ToString("F0"))
                  .Append(" µs, max ").Append(m_MaxPointScanUs.ToString("F0")).Append(" µs").Append('\n');
                sb.Append("  last plan: ").Append(LastPlan).Append('\n');
                sb.Append("  last apply: ").Append(LastApplyCensus).Append('\n');
                sb.Append("  last apply check: ").Append(LastApplyCheck)
                  .Append(m_CheckPending ? " (a check is pending for run " + EntityText(m_CheckRun) + ")" : "").Append('\n');
                sb.Append("  apply status: ").Append(BlockedStatus).Append('\n');
                sb.Append("  last error: ").Append(LastError).Append('\n');
                sb.Append("  species pool: ").Append(m_SpeciesPoolText)
                  .Append("; run prefabs ").Append(m_RunPrefabs.Count)
                  .Append("; ownership prefab sets ready ").Append(m_Ownership.PrefabSetsReady)
                  .Append(", load pass: ").Append(m_Ownership.LastLoadPass ?? "-");
                Mod.Log.Info(sb.ToString());
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Glade: dump failed: {e.Message}");
            }
        }


        private void UpdateCore()
        {
            m_ApplyingRun = Entity.Null; // non-Null for exactly one frame (the Apply frame)
            UpdateFrame();
            // Dwell on every path (held, switching, cancel, apply, re-plan, hold); Bound buckets by plan state, every other kind by kind.
            // No allocation.
            m_HoverLog.AddDwell(m_HoverKind,
                m_HoverKind == GladeHoverKind.Bound ? m_PlanState : GladePlanState.None,
                UnityEngine.Time.unscaledDeltaTime);
        }

        private void UpdateFrame()
        {
            if (m_Disabled || !m_Armed)
            {
                applyMode = ApplyMode.Clear;
                Deactivate();
                return;
            }

            RefreshCandidatesIfDue();

            // Temps from last frame's emission were generated in Modification1 and validated in ModificationEnd of that frame: census,
            // skip-set learning and the after-state hide happen now, before the action flags.
            bool readyFlipped = false;
            if (m_EmitPending)
            {
                m_EmitPending = false;
                m_PreviewReady = true;
                readyFlipped = true;
                OnPreviewReady();
            }

            // Action enable flags, every frame (base never enables them for a mod tool). Mirrors ObjectToolSystem.UpdateActions
            // (ObjectToolSystem.cs:2709-2765).
            bool can = !Game.Input.InputManager.instance.hasInputFieldFocus;
            // The held preview is applicable only while it is the plan of the tool's current level and target. StepLevel / ToggleTarget
            // (Shift+G, Alt+G, the dev tab) change m_Level / m_TreeLine without touching the preview, and SkiGladeHotkeySystem runs in
            // UIUpdate, after ToolUpdate in the same frame (Game.Common/SystemOrder.cs:58 ToolSystem vs :67 UIUpdateSystem, same lines in
            // 1.6.2), so a click landing on the next frame would apply the old level before the re-plan test below saw the change. Now the
            // press falls through to the re-plan and Apply becomes possible on the new preview (two frames later).
            bool planCurrent = m_Level == m_PlannedLevel && m_TreeLine == m_PlannedTreeLine;
            // No Apply while a re-plan is requested (a learned skip set is about to replace this preview, OnPreviewReady)
            SetEnabled(applyAction, can && m_PreviewReady && m_ReplanWhy == null && planCurrent && GetAllowApply());
            SetEnabled(cancelAction, can && m_HasPending);
            SetEnabled(secondaryApplyAction, can && m_HasPending);

            // (a) Over UI / input disabled: deliberate deviation from vanilla, which Clears every frame over UI (ObjectToolSystem.cs:3348
            // flag test [1.6.2 :3407] -> :3504-3524 -> Clear :3670-3674; DefaultToolSystem.cs:900-922). HOLD (applyMode None) and do not
            // touch the hover: the preview stays visible over the dev tab; safe because Temps are not serialized (SerializerSystem.cs:201-206)
            // and any incoming tool clears on its first frame. UIDisable mirrors controlOverWorld (ToolRaycastSystem.cs:105-112).
            bool inputLive = Game.Input.InputManager.instance.controlOverWorld &&
                (m_ToolRaycastSystem.raycastFlags & (RaycastFlags.DebugDisable | RaycastFlags.UIDisable)) == 0;
            m_InputLive = inputLive;
            if (!inputLive)
            {
                HoverResult held = default;
                held.Kind = GladeHoverKind.Held;
                UpdateHoverStatus(in held, false);
                // A requested re-plan (after apply / skip set learned) does not wait for the cursor
                if (m_ReplanWhy != null)
                {
                    Entity again = IsOurRunPrefabArea(m_PlannedRun) && m_Ownership.IsBound(m_PlannedRun)
                        ? m_PlannedRun
                        : Entity.Null;
                    Replan(again);
                    DrawOverlay();
                    return;
                }
                HoldFrame();
                return;
            }

            // Cancel / secondary apply: drop the preview, stay in the tool
            if (m_HasPending &&
                (cancelAction.WasPressedThisFrame() || secondaryApplyAction.WasPressedThisFrame()))
            {
                applyMode = ApplyMode.Clear;
                m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                ClearPending();
                m_PlanState = GladePlanState.None;
                m_ReplanWhy = null; // the preview must not come straight back
                Mod.Log.Info($"Glade: preview canceled on run {EntityText(m_PlannedRun)}");
                DrawOverlay();
                return;
            }

            // Apply (ObjectToolSystem.cs:3928-3930 pattern; 1.6.2 :3987). applyAction is only enabled while GetAllowApply() holds (flags
            // above; GetAllowApply is false while any Error exists, ToolBaseSystem.cs:533-540, and WasPressedThisFrame is false for a
            // disabled action, UIInputAction.cs:62-69), so the error arm of the "blocked" branch below is defensive only; the status is
            // published from the hold path (UpdateBlockedStatus -> BlockedStatus). Never on the ready frame: its ECB writes (Hidden) target
            // the Temps this Apply would consume. Never for a plan of another level or target (planCurrent above): the press then falls
            // through to the re-plan below instead of applying a level the player stepped away from.
            if (m_PreviewReady && !readyFlipped && m_ReplanWhy == null && planCurrent && applyAction.WasPressedThisFrame())
            {
                bool runAlive = m_PlannedRun != Entity.Null && EntityManager.Exists(m_PlannedRun) &&
                                !EntityManager.HasComponent<Deleted>(m_PlannedRun);
                if (runAlive && GetAllowApply())
                {
                    RefreshPreviewCensus(force: true);
                    applyMode = ApplyMode.Apply;
                    m_ApplyingRun = m_PlannedRun;
                    m_ApplyingLayer = m_PlannedLayer;
                    m_ApplyingLevel = m_PlannedLevel;
                    m_ApplyingTreeLine = m_PlannedTreeLine;
                    // Apply check snapshot before the definitions go: the census the plan used (m_Candidates / m_Own are still this plan's) and the
                    // Temp states that ApplyObjectsSystem will act on in this frame's ApplyTool phase.
                    TakeApplySnapshot();
                    m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                    LastApplyCensus =
                        $"run {EntityText(m_PlannedRun)} {TargetText(m_PlannedTreeLine)} L{m_PlannedLevel} " +
                        $"({LevelName(m_PlannedLevel)}; layer {LayerText(m_PlannedLayer)}): planted " +
                        $"{PreviewPlant - PreviewCancelled} ({PreviewCancelled} canceled by validation/budget — " +
                        $"not created, ApplyObjectsSystem.cs:337-340), felled {PreviewThin}, removed {PreviewRemove}" +
                        (PreviewDeleteCancelled > 0
                            ? $" ({PreviewDeleteCancelled} delete Temp(s) canceled — their trees stay)"
                            : "") +
                        $", net cost {PreviewCost}";
                    Mod.Log.Info($"Glade: APPLY — {LastApplyCensus}");
                    m_HoverLog.Applies++;
                    ClearPending();
                    m_PlanState = GladePlanState.None;
                    // The next ToolUpdate re-plans the same run (Complete or next slice)
                    m_ReplanWhy = WhyAfterApply;
                }
                else
                {
                    applyMode = ApplyMode.None;
                    int errors = m_ErrorQuery.IsEmptyIgnoreFilter ? 0 : m_ErrorQuery.CalculateEntityCount();
                    LastApplyCensus = runAlive
                        ? $"blocked by {errors} error(s)"
                        : "blocked — run no longer exists";
                    Mod.Log.Info($"Glade: apply {LastApplyCensus} on run {EntityText(m_PlannedRun)}");
                }
                DrawOverlay();
                return;
            }

            // Hover. GetRaycastResult rejects Deleted owners (ToolBaseSystem.cs:542-551) and reports OriginalDeletedSystem + m_ForceUpdate
            // as forceUpdate (:555-559), also when the raycast returned nothing.
            bool switching = m_ToolSystem.fullUpdateRequired ||
                             (m_ToolRaycastSystem.raycastFlags & RaycastFlags.ToolDisable) != 0;
            bool hasHit = GetRaycastResult(out ControlPoint cp, out bool forceUpdate);
            if (switching)
            {
                // (a) Switching: the raycast is disabled this frame. Hold, never log, keep the forceUpdate for the first live frame
                // (m_ForceUpdate is consumed after this OnUpdate, ToolBaseSystem.cs:416).
                m_PendingForce |= forceUpdate;
                m_HoverKind = GladeHoverKind.Switching;
                HoldFrame();
                return;
            }
            forceUpdate |= m_PendingForce;
            m_PendingForce = false;

            HoverResult h = ResolveHover(hasHit, in cp);
            bool replan = h.Run != m_PlannedRun || m_TreeLine != m_PlannedTreeLine ||
                          m_Level != m_PlannedLevel || forceUpdate || m_ReplanWhy != null;
            UpdateHoverStatus(in h, replan);

            if (replan)
            {
                Replan(h.Run);
                DrawOverlay();
                return;
            }

            // Hold (ObjectToolSystem.cs:4057; 1.6.2 :4116)
            HoldFrame();
        }

        private void HoldFrame()
        {
            applyMode = ApplyMode.None;
            RefreshPreviewCensus(force: false);
            if (m_PreviewReady && m_MisHiddenDoomed > 0)
                FixDoomedVisibility(readyFrame: false);
            UpdateBlockedStatus();
            DrawOverlay();
        }

        /// <summary>Clear old Temps this frame (they become reusable Deleted Temps, GenerateObjectsSystem.cs:89-139), destroy old
        /// definitions, emit new ones</summary>
        private void Replan(Entity run)
        {
            string why = m_ReplanWhy;
            m_ReplanWhy = null;
            if (why != null)
            {
                Mod.Log.Info(why == WhyAfterApply
                    ? $"Glade: re-plan after apply — run {EntityText(run)}"
                    : $"Glade: re-plan — {why} — run {EntityText(run)}");
            }
            applyMode = ApplyMode.Clear;
            m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            ClearPending();
            // The first re-plan after an Apply (the applied trees are in the static tree by now, engine notes Lifecycle), whatever run is hovered now
            if (m_CheckPending)
                RunApplyCheck();
            m_PlannedRun = run;
            m_PlannedTreeLine = m_TreeLine;
            m_PlannedLevel = m_Level;
            m_PlannedLayer = LayerNone;
            m_PlanState = GladePlanState.None;
            PlanHasCounts = false;
            m_OutlineCount = 0;
            m_BeltCount = 0;
            if (run != Entity.Null)
                PlanAndEmit(run);
        }

        /// <summary>"Apply blocked by N error(s)" while a validated preview holds Error entities (Apply is disabled then). Rebuilt only
        /// when the count changes.</summary>
        private void UpdateBlockedStatus()
        {
            int n = m_PreviewReady ? PreviewErrors : 0;
            if (n == m_LastBlockedErrors)
                return;
            m_LastBlockedErrors = n;
            BlockedStatus = n > 0 ? $"Apply blocked by {n} error(s)" : "-";
            if (n > 0)
                Mod.Log.Info($"Glade: {BlockedStatus} on run {EntityText(m_PlannedRun)}");
        }

        private static void SetEnabled(Game.Input.IProxyAction action, bool value)
        {
            // Write only on change: the setter re-resolves proxy state (unverified cost)
            if (action != null && action.shouldBeEnabled != value)
                action.shouldBeEnabled = value;
        }

        /// <summary>Drops everything that describes the pending preview (its Temps are cleared or applied by the caller's applyMode this frame)</summary>
        private void ClearPending()
        {
            m_HasPending = false;
            m_EmitPending = false;
            m_PreviewReady = false;
            PreviewPlant = 0;
            PreviewThin = 0;
            PreviewCancelled = 0;
            PreviewCost = 0;
            PreviewErrors = 0;
            PreviewRemove = 0;
            PreviewDeleteCancelled = 0;
            PreviewBlockedKnown = 0;
            PlannedPlant = 0;
            PlannedFell = 0;
            PlannedRemove = 0;
            m_LastBlockedErrors = -1;
            BlockedStatus = "-";
            m_EmittedSlots.Clear();
            m_EmittedRemoveOriginals.Clear();
            m_EmittedFell = 0;
            m_EmittedRemove = 0;
            m_ThinMarkerCount = 0;
            m_RemoveMarkerCount = 0;
            m_MisHiddenDoomed = 0;
            m_CensusFrame = -1;
        }

        /// <summary>Tool start / stop and every load: emitted slots, remove originals, the hover log and Preview*, all via ClearPending +
        /// GladeHoverLog.Begin</summary>
        private void ResetPreviewState()
        {
            ClearPending();
            m_PlannedRun = Entity.Null;
            m_PlannedTreeLine = m_TreeLine;
            m_PlannedLevel = m_Level;
            m_PlannedLayer = LayerNone;
            m_ApplyingRun = Entity.Null;
            m_ApplyingLayer = LayerNone;
            m_ApplyingLevel = m_Level;
            m_ApplyingTreeLine = m_TreeLine;
            PlanHasCounts = false;
            m_OutlineCount = 0;
            m_BeltCount = 0;
            m_HoverKind = GladeHoverKind.None;
            m_PlanState = GladePlanState.None;
            m_InputLive = false;
            m_StatusCur = default;
            m_StatusPrev = default;
            m_HoverStatus = NoRunStatus;
            m_LastViaPoint = false;
            m_UnboundNearestSignM = -1f;
            m_ReplanWhy = null;
            m_PendingForce = false;
            m_HideWarnedThisPlan = false;
            m_SelfHealLoggedThisPlan = false;
            m_MoneyShortThisPlan = false;
            m_ViaPointLogged.Clear();
            m_RaycastMissWarned.Clear();
            m_RaycastMisses = 0;
            m_CandidateNextRefresh = 0f; // refresh on the next running frame
            m_HoverLog.Begin();
        }

        /// <summary>Skip sets live for one activation (OnStartRunning) and never across a load</summary>
        private void ClearSkipSets()
        {
            m_SkipSlots.Clear();
            m_SkipKeyOrder.Clear();
            m_SkipEvictions = 0;
        }

        /// <summary>Per-load caches that must never leak into another city (engine notes, Load reset)</summary>
        private void ResetLoadCaches()
        {
            FlushApplyCheck(canRun: false, "a game load came first (the applied run belongs to the previous city)");
            m_CheckOthersBefore.Clear();
            m_CheckOverriddenBefore.Clear();
            m_CheckDoomedOriginals.Clear();
            m_CheckOthersNow.Clear();
            m_CheckCancelledSpots.Clear();
            m_CheckPlantedSpots.Clear();
            m_CheckFelledSpots.Clear();
            LastApplyCheck = "-";
            PlanHasCounts = false;
            m_CenOverriddenInside = 0;
            m_CenOverriddenBand = 0;
            ClearSkipSets();
            m_PlanCache.Clear();
            // GladePlanCache.Clear keeps Hits / Misses by its own contract (SkiGladeCompose.cs); a load must not, nor the census counters
            // DUMP and the dev tab print ("last census" next to "none since load").
            m_PlanCache.Hits = 0;
            m_PlanCache.Misses = 0;
            m_CenWildInside = 0;
            m_CenFixedInside = 0;
            m_CenBand = 0;
            m_CenForeign = 0;
            m_CenOwnInside = 0;
            m_CenOwnBand = 0;
            m_CenOwnFixed = 0;
            m_Result.Clear();
            m_HasResult = false;
            m_CurrentPlanKey = 0UL;
            m_LegacyWarned.Clear();
            m_PlanKeyWarned = false;
            m_LayerWarned = false;
            m_CandidateRunCount = 0;
            m_CandidatePointCount = 0;
            m_CityBoundRuns = 0;
            m_BoundRunsAtActivation = -1;
            m_PointScans = 0;
            m_LastPointScanUs = 0f;
            m_MaxPointScanUs = 0f;
        }


        private HoverResult ResolveHover(bool hasHit, in ControlPoint cp)
        {
            HoverResult r = default;
            if (!hasHit)
            {
                r.Kind = GladeHoverKind.Nothing;
                return r;
            }

            Entity hit = cp.m_OriginalEntity; // area entity or the terrain singleton (ControlPoint.cs:30-41)
            float2 p = cp.m_HitPosition.xz;   // terrain point in both cases (RaycastJobs.cs:298-304)
            r.Hit = hit;
            r.Point = p;
            r.HasPoint = true;

            // (c) the raycast hit is one of our bound runs
            if (IsOurRunPrefabArea(hit) && m_Ownership.IsBound(hit))
            {
                Entity chosen = hit;
                // Tie rule: keep the planned run when it also contains the point (two of our runs overlapping tie arbitrarily,
                // RaycastResult.cs:12-18); avoids re-plan flicker.
                if (hit != m_PlannedRun && m_PlannedRun != Entity.Null && IsOurRunPrefabArea(m_PlannedRun) &&
                    m_Ownership.IsBound(m_PlannedRun) && RunContainsPoint(m_PlannedRun, p))
                    chosen = m_PlannedRun;
                r.Kind = GladeHoverKind.Bound;
                r.Run = chosen;
                r.Subject = chosen;
                return r;
            }

            // (d) point scan over our runs at the hit point
            PointScan(p, hit, out Entity bound, out Entity other);
            if (bound != Entity.Null)
            {
                r.Kind = GladeHoverKind.Bound;
                r.Run = bound;
                r.Subject = bound;
                r.ViaPoint = true;
                return r;
            }
            if (other == Entity.Null && IsOurRunPrefabArea(hit))
                other = hit; // float edge: the raycast found it inside a triangle, the scan did not
            if (other != Entity.Null)
            {
                r.Subject = other;
                if (EntityManager.HasComponent<SkiRunBinding>(other))
                {
                    // IsBound is false, so the binding's sign is Null or dead: a paid orphan (re-links free on the next sweep within 5 km)
                    r.Kind = GladeHoverKind.Unbound;
                    r.Reason = ReasonOrphan;
                }
                else if (EntityManager.TryGetComponent(other, out Owner owner))
                {
                    if (IsLegacyOwner(owner.m_Owner))
                    {
                        r.Kind = GladeHoverKind.Legacy;  // Owner(our sign or a dead owner), no binding
                    }
                    else
                    {
                        r.Kind = GladeHoverKind.Unbound;
                        r.Reason = ReasonForeignOwner;   // LeaveForeign
                    }
                }
                else
                {
                    r.Kind = GladeHoverKind.Unbound;
                }
                return r;
            }

            // (e) / (f)
            r.Subject = hit;
            r.Kind = EntityManager.HasComponent<Area>(hit) ? GladeHoverKind.Foreign : GladeHoverKind.Terrain;
            return r;
        }

        /// <summary>"OwnerIsOurSign": the owner is a sign of ours or no longer exists (only our pre-binding sweep ever wrote Owner on a drawn run)</summary>
        private bool IsLegacyOwner(Entity owner)
        {
            if (owner == Entity.Null || !EntityManager.Exists(owner))
                return true;
            return EntityManager.TryGetComponent(owner, out PrefabRef pr) && m_Ownership.IsGatePrefab(pr.m_Prefab);
        }

        private bool IsOurRunPrefabArea(Entity e)
        {
            return e != Entity.Null &&
                   EntityManager.HasBuffer<Node>(e) &&
                   !EntityManager.HasComponent<Temp>(e) &&
                   !EntityManager.HasComponent<Deleted>(e) &&
                   EntityManager.TryGetComponent(e, out PrefabRef pr) &&
                   m_RunPrefabs.Contains(pr.m_Prefab);
        }

        /// <summary>Exact vanilla point-in-area test: any triangle containing the point, with the overload Game.Areas/RaycastJobs.cs:168
        /// uses (MathUtils.Intersect(Triangle2, float2, out float2))</summary>
        private bool RunContainsPoint(Entity run, float2 p)
        {
            if (!EntityManager.TryGetComponent(run, out Geometry geometry) ||
                !MathUtils.Intersect(geometry.m_Bounds.xz, p) ||
                !EntityManager.HasBuffer<Node>(run) || !EntityManager.HasBuffer<Triangle>(run))
                return false;
            DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(run, true);
            DynamicBuffer<Triangle> tris = EntityManager.GetBuffer<Triangle>(run, true);
            for (int t = 0; t < tris.Length; t++)
            {
                int3 idx = tris[t].m_Indices;
                if (idx.x < 0 || idx.y < 0 || idx.z < 0 ||
                    idx.x >= nodes.Length || idx.y >= nodes.Length || idx.z >= nodes.Length)
                    continue;
                if (MathUtils.Intersect(AreaUtils.GetTriangle3(nodes, tris[t]).xz, p, out float2 _))
                    return true;
            }
            return false;
        }

        /// <summary>Our runs containing p. bound = the planned run if it is one, else the lowest Entity.Index among bound runs; other =
        /// the raycast hit if it is an unbound run of ours, else the lowest index among them. Bounds first, triangles only for bounds
        /// hits. Timed (DUMP / dev tab).</summary>
        private void PointScan(float2 p, Entity hit, out Entity bound, out Entity other)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bound = Entity.Null;
            other = Entity.Null;
            bool boundIsPlanned = false;
            if (!m_RunQuery.IsEmptyIgnoreFilter)
            {
                m_RunQuery.CompleteDependency();
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var geometryHandle = GetComponentTypeHandle<Geometry>(isReadOnly: true);
                using (var chunks = m_RunQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    for (int c = 0; c < chunks.Length && !boundIsPlanned; c++)
                    {
                        var entities = chunks[c].GetNativeArray(entityHandle);
                        var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                        var geometries = chunks[c].GetNativeArray(ref geometryHandle);
                        for (int i = 0; i < entities.Length; i++)
                        {
                            if (!m_RunPrefabs.Contains(prefabRefs[i].m_Prefab))
                                continue;
                            if (!MathUtils.Intersect(geometries[i].m_Bounds.xz, p))
                                continue;
                            Entity e = entities[i];
                            if (!RunContainsPoint(e, p))
                                continue;
                            if (m_Ownership.IsBound(e))
                            {
                                if (e == m_PlannedRun)
                                {
                                    bound = e;
                                    boundIsPlanned = true;
                                    break;
                                }
                                if (bound == Entity.Null || e.Index < bound.Index)
                                    bound = e;
                            }
                            else if (other != hit)
                            {
                                if (e == hit || other == Entity.Null || e.Index < other.Index)
                                    other = e;
                            }
                        }
                    }
                }
            }
            float us = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000000.0 /
                               System.Diagnostics.Stopwatch.Frequency);
            m_PointScans++;
            m_LastPointScanUs = us;
            if (us > m_MaxPointScanUs)
                m_MaxPointScanUs = us;
        }

        /// <summary>Transition handling: dev-tab status (on change, two-slot memo), the via-point count, one transition line per state
        /// change through GladeHoverLog, the cap line, the legacy WARN. Allocation only on a state change before the cap.</summary>
        private void UpdateHoverStatus(in HoverResult h, bool replan)
        {
            m_HoverKind = h.Kind;
            if (h.Kind == GladeHoverKind.Switching || h.Kind == GladeHoverKind.None)
                return;

            // Via point (counted on the rising edge; logged once per run per activation). Only a ground-area winner is a tie (engine notes,
            // Hover resolver (d)).
            bool via = h.Kind == GladeHoverKind.Bound && h.ViaPoint;
            if (via)
            {
                // One HasComponent per via-point frame (no allocation)
                bool tie = h.Hit != Entity.Null && EntityManager.HasComponent<Area>(h.Hit);
                if (!m_LastViaPoint)
                {
                    m_HoverLog.ViaPoint++;
                    if (!tie)
                        m_RaycastMisses++;
                }
                if (tie)
                {
                    if (m_ViaPointLogged.Add(h.Run))
                    {
                        Mod.Log.Info($"Glade: hover -> bound run {EntityText(h.Run)} found by the point test under " +
                                     $"{HitText(h.Hit)} — another ground area won an exact distance tie (every " +
                                     "ground-area result carries the terrain HitEntity at terrain distance − " +
                                     "0.25/len, Game.Areas/RaycastJobs.cs:299-304; the first accumulated wins, " +
                                     "Game.Common/RaycastResult.cs:14): an ordinary tie, counted in the stop line, " +
                                     "logged once per run per activation");
                    }
                }
                else if (m_RaycastMissWarned.Add(h.Run))
                {
                    Mod.Log.Warn($"Glade: hover -> RAYCAST MISS — bound run {EntityText(h.Run)} contains the cursor " +
                                 $"point but the raycast's winner is {HitText(h.Hit)}: the area raycast returned no " +
                                 "validated result for it (a ground-area hit is always nearer than the terrain hit — " +
                                 "Game.Areas/RaycastJobs.cs:304 subtracts 0.25/len from the terrain distance of " +
                                 "Game.Common/RaycastSystem.cs:177 — so this is not a tie: a search-tree / validation " +
                                 "anomaly, or a raycast older than a geometry change). The point test planned it " +
                                 "anyway; warned once per run per activation, counted in the stop line");
                }
            }
            m_LastViaPoint = via;

            // Dedup key: Bound never carries the via-point reason (a flickering tie must not count as transitions)
            byte reason = h.Kind == GladeHoverKind.Bound ? ReasonNone : h.Reason;
            Entity subject = h.Subject;
            if (m_StatusCur.Valid && m_StatusCur.Kind == h.Kind && m_StatusCur.Subject == subject &&
                m_StatusCur.Reason == reason)
                return;

            if (m_StatusPrev.Valid && m_StatusPrev.Kind == h.Kind && m_StatusPrev.Subject == subject &&
                m_StatusPrev.Reason == reason)
            {
                StatusSlot swap = m_StatusCur;
                m_StatusCur = m_StatusPrev;
                m_StatusPrev = swap;
            }
            else
            {
                m_StatusPrev = m_StatusCur;
                m_StatusCur = BuildStatus(in h, reason);
            }
            m_HoverStatus = m_StatusCur.Text;
            m_UnboundNearestSignM = h.Kind == GladeHoverKind.Unbound ? m_StatusCur.SignDistance : -1f;

            bool write = m_HoverLog.Offer(h.Kind, subject == Entity.Null ? -1 : subject.Index, reason);
            // The cap line goes out on this Offer, before any early return below. CapJustReached is true after exactly one Offer per
            // activation (the first suppressed one) and the next Offer clears it, so placing it after the Legacy-WARN and Bound+re-plan
            // returns would lose the line for the whole activation.
            if (!write && m_HoverLog.CapJustReached)
            {
                Mod.Log.Info($"Glade: hover transitions capped at {GladeHoverLog.MaxLinesPerActivation} " +
                             "this activation — totals at tool stop");
            }
            if (h.Kind == GladeHoverKind.Legacy && m_LegacyWarned.Add(subject))
            {
                // WARN once per run per load, whatever the cap
                Mod.Log.Warn($"Glade: hover -> LEGACY OWNER — not migrated: {m_StatusCur.Text}; the O1 " +
                             "load pass should have converted it (see the 'Binding: load pass' line); the " +
                             "glade tool treats it as unbound (warned once per run per load)");
                return;
            }
            // Bound with a re-plan: the plan line written by PlanAndEmit is the transition line
            if (h.Kind == GladeHoverKind.Bound && replan)
                return;
            if (write)
                Mod.Log.Info(HoverLine(in h));
        }

        /// <summary>Dev-tab status of a hover state (built on change only)</summary>
        private StatusSlot BuildStatus(in HoverResult h, byte reason)
        {
            StatusSlot s = default;
            s.Valid = true;
            s.Kind = h.Kind;
            s.Subject = h.Subject;
            s.Reason = reason;
            s.SignDistance = -1f;
            switch (h.Kind)
            {
                case GladeHoverKind.Held:
                    s.Text = HeldStatus;
                    break;
                case GladeHoverKind.Nothing:
                    s.Text = NothingStatus;
                    break;
                case GladeHoverKind.Terrain:
                    s.Text = TerrainStatus;
                    break;
                case GladeHoverKind.Foreign:
                    s.Text = $"foreign surface {PrefabNameOf(h.Subject)} {EntityText(h.Subject)} — no bound run here";
                    break;
                case GladeHoverKind.Bound:
                    s.Text = $"bound run {PrefabNameOf(h.Subject)} {EntityText(h.Subject)}";
                    break;
                case GladeHoverKind.Legacy:
                case GladeHoverKind.Unbound:
                    s.Text = UnboundText(h.Kind, h.Subject, reason, out s.SignDistance);
                    break;
                default:
                    s.Text = NoRunStatus;
                    break;
            }
            return s;
        }

        /// <summary>The unbound / legacy sentence. One managed scan of every building (TryNearestGateEntity), on a state change only.
        /// Distance from the run's Geometry.m_CenterPosition.xz, the bind test's point.</summary>
        private string UnboundText(GladeHoverKind kind, Entity run, byte reason, out float distance)
        {
            distance = float.PositiveInfinity;
            string label = $"{PrefabNameOf(run)} {EntityText(run)}";
            string notReady = m_Ownership.PrefabSetsReady
                ? ""
                : " [ownership prefab sets not ready this load: no run can read as bound]";
            if (kind == GladeHoverKind.Legacy)
            {
                string ownerText = "?";
                if (EntityManager.TryGetComponent(run, out Owner legacyOwner))
                    ownerText = EntityText(legacyOwner.m_Owner);
                return $"legacy run {label} — Owner {ownerText} (a Welcome Sign or a dead owner) and no " +
                       $"SkiRunBinding: not migrated{notReady}";
            }
            if (reason == ReasonForeignOwner)
            {
                string ownerText = "?";
                if (EntityManager.TryGetComponent(run, out Owner foreignOwner))
                    ownerText = $"{PrefabNameOf(foreignOwner.m_Owner)} {EntityText(foreignOwner.m_Owner)}";
                return $"unbound run {label} — carries a foreign Owner {ownerText} (not a Welcome Sign; " +
                       $"left alone by the binding rules){notReady}";
            }

            if (!EntityManager.TryGetComponent(run, out Geometry geometry))
                return $"{UnboundStatus} ({label}; no Geometry, distance unknown){notReady}";
            bool inRange = m_Ownership.TryNearestGateEntity(geometry.m_CenterPosition.xz, out Entity _, out distance);
            if (reason == ReasonOrphan)
            {
                return $"unbound run {label} — paid orphan (its Welcome Sign is gone); nearest sign " +
                       $"{DistanceText(distance)} from the run's center: it re-links free to a sign within " +
                       $"5 km on the next unpaused 64-frame sweep{notReady}";
            }
            if (float.IsPositiveInfinity(distance))
            {
                return $"unbound run {label} — no Ski Area Welcome Sign in this city; place one within " +
                       $"5 km of the run's center (binds at once){notReady}";
            }
            if (inRange)
            {
                return $"unbound run {label} — nearest Welcome Sign {DistanceText(distance)} from the " +
                       $"run's center, within 5 km: it binds on the next unpaused 64-frame sweep{notReady}";
            }
            return $"unbound run {label} — nearest Welcome Sign {DistanceText(distance)} from the run's " +
                   "center (max 5 km); place a sign within 5 km (binds at once) or move one there (binds " +
                   $"on the next unpaused 64-frame sweep){notReady}";
        }

        /// <summary>The transition line. Built only when GladeHoverLog.Offer says to write it (at most 40 per activation).</summary>
        private string HoverLine(in HoverResult h)
        {
            switch (h.Kind)
            {
                case GladeHoverKind.Held:
                    return "Glade: hover held — input not live (over UI)";
                case GladeHoverKind.Nothing:
                    return "Glade: hover -> nothing (raycast empty)";
                case GladeHoverKind.Terrain:
                {
                    NearestBoundRun(h.Point, out int count, out Entity nearest, out float d);
                    string city = count == 0
                        ? "city: 0 bound run(s) — nothing to preview until a run is drawn within 5 km of a Welcome Sign"
                        : $"city: {count} bound run(s), nearest {EntityText(nearest)} {DistanceText(d)}";
                    return $"Glade: hover -> terrain (x {h.Point.x:F0}, z {h.Point.y:F0}); no run here; {city}";
                }
                case GladeHoverKind.Bound:
                    return $"Glade: hover -> bound run {EntityText(h.Run)} (preview kept)";
                default:
                    return "Glade: hover -> " + m_StatusCur.Text;
            }
        }

        /// <summary>Bound runs and the nearest one to p by its Geometry bounds (xz), on a Terrain transition line only</summary>
        private void NearestBoundRun(float2 p, out int count, out Entity nearest, out float distance)
        {
            count = 0;
            nearest = Entity.Null;
            distance = float.PositiveInfinity;
            if (m_RunQuery.IsEmptyIgnoreFilter)
                return;
            m_RunQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            var geometryHandle = GetComponentTypeHandle<Geometry>(isReadOnly: true);
            using (var chunks = m_RunQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = chunks[c].GetNativeArray(ref geometryHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!m_RunPrefabs.Contains(prefabRefs[i].m_Prefab) || !m_Ownership.IsBound(entities[i]))
                            continue;
                        count++;
                        Bounds3 b = geometries[i].m_Bounds;
                        float2 q = math.clamp(p, b.min.xz, b.max.xz);
                        float d = math.distance(p, q);
                        if (d < distance)
                        {
                            distance = d;
                            nearest = entities[i];
                        }
                    }
                }
            }
        }


        /// <summary>Copies a run's ring, triangles and bounds (no buffer is held across any other EntityManager call). False with a
        /// reason when the run cannot be planned.</summary>
        private bool TryCopyRunGeometry(Entity run, out float3[] ring, out int3[] triangles, out Bounds3 runBounds,
            out string why)
        {
            ring = null;
            triangles = null;
            runBounds = default;
            why = null;
            if (!EntityManager.Exists(run) || !EntityManager.HasBuffer<Node>(run) ||
                !EntityManager.HasBuffer<Triangle>(run) || !EntityManager.HasComponent<Geometry>(run))
            {
                why = "no Node/Triangle buffer or Geometry";
                return false;
            }
            DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(run, true);
            DynamicBuffer<Triangle> tris = EntityManager.GetBuffer<Triangle>(run, true);
            if (nodes.Length < 3 || tris.Length == 0)
            {
                why = $"{nodes.Length} node(s), {tris.Length} triangle(s)";
                return false;
            }
            ring = new float3[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
                ring[i] = nodes[i].m_Position;
            triangles = new int3[tris.Length];
            for (int t = 0; t < tris.Length; t++)
                triangles[t] = tris[t].m_Indices;
            runBounds = EntityManager.GetComponentData<Geometry>(run).m_Bounds;
            return true;
        }

        /// <summary>Census band margin per target: the tree line reaches every belt member (BeltDepth + BeltMinSpacing, the Compose
        /// SameIn region); the interior every tree that can block a glade slot. These bands are also how far each target sees this run's
        /// misplaced trees (EngineNotes/SkiGladeCompose.md): the interior removes its own strays up to 5.5 m outside, the tree line interior
        /// strays up to 14.5 m outside; farther ones stay until the run is deleted (documented limit, not widened: unverified need).</summary>
        private static float BandMarginFor(bool treeLine)
        {
            return treeLine ? Tuning.BeltDepth + Tuning.BeltMinSpacing : Tuning.GladeMinSpacing;
        }

        private void PlanAndEmit(Entity run)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            m_HideWarnedThisPlan = false;
            m_SelfHealLoggedThisPlan = false;
            m_MoneyShortThisPlan = false;
            m_HoverLog.Plans++;
            PlanHasCounts = false;

            // 1. Copy the run geometry
            if (!TryCopyRunGeometry(run, out float3[] ring, out int3[] triangles, out Bounds3 runBounds, out string why))
            {
                LastPlan = $"run {EntityText(run)}: {why} — not plannable";
                Mod.Log.Info($"Glade: {LastPlan}");
                m_PlanState = GladePlanState.Blocked;
                return;
            }

            // Overlay outline (closing edge included at draw time; areas are implicitly closed, AreaBorderRenderSystem.cs:486-503)
            EnsureCapacity(ref m_OutlineRing, ring.Length);
            Array.Copy(ring, m_OutlineRing, ring.Length);
            m_OutlineCount = ring.Length;

            bool treeLine = m_PlannedTreeLine;
            byte level = m_PlannedLevel;
            byte layer = LayerOf(treeLine);
            if (treeLine)
                BuildBeltRing(ring, Tuning.BeltDepth);

            // 2. Census (static search tree, main thread; Overridden trees included)
            CensusTrees(run, ring, triangles, withBand: true, BandMarginFor(treeLine));

            // Stable order so the caps and the ECB order are deterministic for the same stand
            m_Candidates.Sort(CompareCandidates);
            m_Own.Sort(CompareOwn);
            int n = m_Candidates.Count;
            var existing = new float3[n];
            var thinnable = new bool[n];
            for (int i = 0; i < n; i++)
            {
                existing[i] = m_Candidates[i].Position;
                thinnable[i] = m_Candidates[i].Thinnable;
            }
            var own = new ComposeMember[m_Own.Count];
            int otherLayerOwn = 0;
            for (int i = 0; i < own.Length; i++)
            {
                OwnTree o = m_Own[i];
                own[i] = new ComposeMember { Position = o.Position, Layer = o.Layer, Variant = o.Variant, PlanGen = o.PlanGen };
                if (o.Layer != layer)
                    otherLayerOwn++;
            }
            m_PlannedLayer = layer;

            // 3. Obstacles: every vertex (snowmaking/night runs carry a Secondary border prop at each vertex, SkiRunPropSystem). Exclusion
            // rings only matter for planting.
            var obstacles = new GladeObstacle[ring.Length];
            for (int i = 0; i < ring.Length; i++)
                obstacles[i] = new GladeObstacle { Center = ring[i], Radius = Tuning.VertexPropRadius };
            float3[][] exclusion = CollectExclusionRings(run, runBounds);

            // 4. Compose input (Level + TreeLine; Variant is always 0 for new plantings; Params.ThinFraction is not read)
            m_Input.Ring = ring;
            m_Input.Triangles = triangles;
            m_Input.Params = BuildParams(treeLine, m_SpeciesEntities.Count);
            m_Input.Level = level;
            m_Input.TreeLine = treeLine;
            m_Input.Variant = NewPlantingVariant;
            m_Input.Obstacles = obstacles;
            m_Input.ExclusionRings = exclusion;
            m_Input.Existing = existing;
            m_Input.Thinnable = thinnable;
            m_Input.Own = own;
            m_Input.SkipSlots = null;
            m_Input.Caps = new ComposeCaps
            {
                MaxPlant = Tuning.MaxPlantPerApply,
                MaxFell = Tuning.MaxThinPerApply,
                MaxRemove = Tuning.MaxRemovePerApply,
            };

            // 5. Skip set of this plan (PlanKey does not depend on SkipSlots; computed only when a skip set exists at all; Compose reports
            // the key anyway)
            ulong keyBefore = 0UL;
            HashSet<int> skip = null;
            if (layer != LayerNone && m_SkipSlots.Count > 0)
            {
                keyBefore = SkiGladeCompose.PlanKey(m_Input);
                if (m_SkipSlots.TryGetValue(keyBefore, out HashSet<int> known) && known.Count > 0)
                    skip = known;
            }
            m_Input.SkipSlots = skip;

            GladeStatus status = SkiGladeCompose.Compose(m_Input, m_Result, m_PlanCache);
            ComposeResult r = m_Result;
            m_HasResult = true;
            m_CurrentPlanKey = r.PlanKey;
            if (keyBefore != 0UL && r.PlanKey != keyBefore && !m_PlanKeyWarned)
            {
                m_PlanKeyWarned = true;
                Mod.Log.Warn($"Glade: PlanKey(input) {keyBefore:X16} != Compose PlanKey {r.PlanKey:X16} — " +
                             "skip sets may not apply (contract mismatch; warned once per load)");
            }
            PreviewBlockedKnown = SkipCountFor(m_CurrentPlanKey);

            if (status != GladeStatus.Ok && status != GladeStatus.Clamped)
            {
                LastPlan = $"run {EntityText(run)} {TargetText(treeLine)} L{level} ({LevelName(level)}): " +
                           $"planner status {status} — nothing emitted";
                Mod.Log.Info($"Glade: {LastPlan}");
                m_PlanState = GladePlanState.Blocked;
                return;
            }
            if (r.Layer != layer && !m_LayerWarned)
            {
                m_LayerWarned = true;
                Mod.Log.Warn($"Glade: Compose reported layer {LayerText(r.Layer)} for target {TargetText(treeLine)} " +
                             $"(expected {LayerText(layer)}) — the tool tags with the target's layer (warned once per load)");
            }
            // Tooltip counts (plain fields, read per frame by the tooltip / dev tab)
            PlanTarget = r.Target;
            PlanNow = r.Inside;
            PlanWild = r.WildInside;
            PlanFixed = r.FixedInside;
            PlanOwn = r.OwnInside;
            PlanKeep = r.Keep;
            PlanShortfall = r.Shortfall;
            PlanMisplaced = r.Misplaced;
            PlanHasCounts = true;

            // 7. Definitions. When Complete nothing is emitted.
            int fellEmitted = 0, removeEmitted = 0, plantEmitted = 0, gladeCount = 0, beltCount = 0;
            m_EmittedSlots.Clear();
            m_EmittedRemoveOriginals.Clear();
            if (!r.Complete)
            {
                EntityCommandBuffer ecb = m_Barrier.CreateCommandBuffer(); // ObjectToolBaseSystem.cs:2446 (1.6.2 :2466)

                EnsureCapacity(ref m_ThinMarkers, r.Fell.Count);
                for (int k = 0; k < r.Fell.Count; k++)
                {
                    int idx = r.Fell[k];
                    if (idx < 0 || idx >= n)
                        continue;
                    if (EmitThin(ecb, m_Candidates[idx].Entity, out float3 pos))
                        m_ThinMarkers[fellEmitted++] = pos;
                }

                EnsureCapacity(ref m_RemoveMarkers, r.Remove.Count);
                for (int k = 0; k < r.Remove.Count; k++)
                {
                    int idx = r.Remove[k];
                    if (idx < 0 || idx >= m_Own.Count)
                        continue;
                    Entity tree = m_Own[idx].Entity;
                    if (EmitThin(ecb, tree, out float3 pos))
                    {
                        m_RemoveMarkers[removeEmitted++] = pos;
                        m_EmittedRemoveOriginals.Add(tree);
                    }
                }

                if (r.Plant.Count > 0)
                {
                    // Main-thread terrain read; vanilla precedent SelectionToolSystem.cs:1151 (GetHeightData + TerrainUtils sampling outside a
                    // job, no reader registration).
                    TerrainHeightData heightData = m_TerrainSystem.GetHeightData();
                    List<PlantedTree> planted = r.Plan.Planted;
                    for (int k = 0; k < r.Plant.Count; k++)
                    {
                        int slot = r.Plant[k];
                        if (slot < 0 || slot >= planted.Count)
                            continue;
                        PlantedTree tree = planted[slot];
                        if (EmitPlant(ecb, ref heightData, tree, slot))
                        {
                            plantEmitted++;
                            if (tree.Layer == SkiGladeCompose.LayerBelt) beltCount++; else gladeCount++;
                        }
                    }
                }
            }
            m_ThinMarkerCount = fellEmitted;
            m_RemoveMarkerCount = removeEmitted;
            m_EmittedFell = fellEmitted;
            m_EmittedRemove = removeEmitted;
            PlannedPlant = plantEmitted;
            PlannedFell = fellEmitted;
            PlannedRemove = removeEmitted;

            m_HasPending = plantEmitted + fellEmitted + removeEmitted > 0;
            m_EmitPending = m_HasPending;   // ready flips on the next ToolUpdate
            m_PreviewReady = false;

            // 8. State. A Complete plan that is also Saturated (plant branch, Shortfall > 0, the FULL stream out of attempts - SkiGladeCompose
            // engine notes FLAGS) is "no room, final", not "already at this tree density": under the count rule the run stays below T, and every
            // later re-plan reads the same.
            if (r.Complete)
                m_PlanState = r.Saturated ? GladePlanState.Saturated : GladePlanState.Complete;
            else if (!m_HasPending)
                m_PlanState = GladePlanState.Blocked;   // changes planned, nothing emittable (logged below)
            else if (r.Clamped)
                m_PlanState = GladePlanState.Clamped;
            else if (r.Saturated)
                m_PlanState = GladePlanState.Saturated;
            else
                m_PlanState = GladePlanState.Previewing;

            // The plan line, also the Bound transition line:
            //    "Glade: plan run i:v Interior L4 (Open glade): target T, now N (wild W, fixed X, own M) -> plant P | remove R, fell F - <state>; ...".
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 /
                        System.Diagnostics.Stopwatch.Frequency;
            StringBuilder sb = m_LineBuilder;
            sb.Length = 0;
            sb.Append("run ").Append(EntityText(run)).Append(' ').Append(TargetText(treeLine))
              .Append(" L").Append(level).Append(" (").Append(LevelName(level)).Append("): target ").Append(r.Target)
              .Append(", now ").Append(r.Inside)
              .Append(" (wild ").Append(r.WildInside)
              .Append(", fixed ").Append(r.FixedInside)
              .Append(", own ").Append(r.OwnInside)
              .Append(") → plant ").Append(plantEmitted)
              .Append(" | remove ").Append(removeEmitted)
              .Append(", fell ").Append(fellEmitted);
            if (r.Complete && r.Saturated)
            {
                sb.Append(" — saturated: no room for more trees (final; ").Append(PreviewBlockedKnown)
                  .Append(" blocked spots known)");
            }
            else if (r.Complete && r.Shortfall > 0)
            {
                // Below T because the stream's plantable slots are used up by learned blocked spots (not "no room": that is Saturated)
                sb.Append(" — layer complete, ").Append(r.Shortfall)
                  .Append(" short of the target: the remaining spots are blocked (")
                  .Append(PreviewBlockedKnown).Append(" blocked spots known)");
            }
            else if (r.Complete && r.Inside > r.Target)
            {
                sb.Append(" — layer complete, above the target: ").Append(r.FixedInside)
                  .Append(" tree(s) inside may not be felled (other runs', owned or burning)");
            }
            else if (r.Complete)
            {
                sb.Append(" — layer complete (").Append(PreviewBlockedKnown).Append(" blocked spots known)");
            }
            else if (!m_HasPending)
            {
                sb.Append(" — NOTHING EMITTED although the plan has changes (trees vanished since the census?)");
            }
            else if (r.Clamped)
            {
                sb.Append(" — CLAMPED by the per-apply caps (plant ").Append(Tuning.MaxPlantPerApply)
                  .Append(", fell ").Append(Tuning.MaxThinPerApply)
                  .Append(", remove ").Append(Tuning.MaxRemovePerApply)
                  .Append("): apply again to continue");
            }
            else if (r.Saturated)
            {
                sb.Append(" — saturated: no room for more trees (final)");
            }
            else
            {
                sb.Append(" — previewing");
            }
            sb.Append("; keep ").Append(r.Keep)
              .Append(", removed first ").Append(r.Other)
              .Append(" (other-tag ").Append(r.OtherTagged)
              .Append(", misplaced ").Append(r.Misplaced)
              .Append("), stream: raw ").Append(r.RawTarget)
              .Append(", slice ").Append(r.Slice)
              .Append(", same ").Append(r.Same).Append(" (matched ").Append(r.Matched)
              .Append("), missing ").Append(r.Missing)
              .Append(", skipped ").Append(r.Skipped)
              .Append(", deficit ").Append(r.Deficit);
            if (r.Shortfall > 0)
                sb.Append(", shortfall ").Append(r.Shortfall).Append(" (blocked spots or no room)");
            if (r.OwnUnmatched > 0)
                sb.Append(", own off-stream ").Append(r.OwnUnmatched);
            if (r.Misplaced > 0)
                sb.Append(", MISPLACED own ").Append(r.Misplaced)
                  .Append(" (this run's trees outside their layer's region after a run edit: removed first)");
            sb.Append("; census: band ").Append(m_CenBand)
              .Append(", foreign ").Append(m_CenForeign)
              .Append(", other-layer own ").Append(otherLayerOwn)
              .Append(", Overridden (hidden, counted) inside ").Append(m_CenOverriddenInside)
              .Append(" / band ").Append(m_CenOverriddenBand)
              .Append("; ").Append(exclusion != null ? exclusion.Length : 0).Append(" exclusion ring(s), ")
              .Append(r.Plan.Attempts).Append(" attempts, ").Append(ms.ToString("F1")).Append(" ms")
              .Append("; planted glade ").Append(gladeCount).Append(" / belt ").Append(beltCount);
            if (r.CacheHit)
                sb.Append(" (cache hit)");
            if (r.FullReplan)
                sb.Append(" (slice ran out of attempts: re-planned in full)");
            if (r.FullForMembers)
                sb.Append(" (re-planned in full to order the members)");
            LastPlan = sb.ToString();
            if (!r.Complete && !m_HasPending)
                Mod.Log.Warn($"Glade: plan {LastPlan}");
            else
                Mod.Log.Info($"Glade: plan {LastPlan}");
        }

        private static int CompareCandidates(Candidate a, Candidate b)
        {
            int c = a.Position.x.CompareTo(b.Position.x);
            if (c != 0) return c;
            c = a.Position.z.CompareTo(b.Position.z);
            if (c != 0) return c;
            return a.Entity.Index.CompareTo(b.Entity.Index);
        }

        private static int CompareOwn(OwnTree a, OwnTree b)
        {
            int c = a.Position.x.CompareTo(b.Position.x);
            if (c != 0) return c;
            c = a.Position.z.CompareTo(b.Position.z);
            if (c != 0) return c;
            return a.Entity.Index.CompareTo(b.Entity.Index);
        }

        /// <summary>Interior -> LayerGlade (0), tree line -> LayerBelt (1); every plan is single-layer</summary>
        private static byte LayerOf(bool treeLine)
        {
            return treeLine ? SkiGladeCompose.LayerBelt : SkiGladeCompose.LayerGlade;
        }

        /// <summary>Density of the plan stream: SkiGladeCompose.StreamDensityPerM2 = 1.10 x the densest level's, so the stream does not
        /// depend on L and a level-8 run replaces learned blocked spots from its tail. SkiGladeCompose sets it itself (its engine notes PLAN
        /// STREAM: Params.Mode, ThinFraction, GladeDensity and MaxPlant are not read) and derives T from ComposeInput.Level; the tool
        /// passes the same value only so the Params it hands over are self-consistent, and prints it in the ARMED line.</summary>
        private static float StreamDensity()
        {
            return SkiGladeCompose.StreamDensityPerM2;
        }

        /// <summary>What Compose reads: MinSpacing of the target layer (GladeMinSpacing interior, BeltMinSpacing tree line), margins,
        /// belt shape (BeltDensity stays the belt; Compose scales it by BeltFactor(L)), species, ages, AttemptsPerTree. Mode /
        /// ThinFraction / GladeDensity / MaxPlant are set consistently but not read.</summary>
        internal static GladeParams BuildParams(bool treeLine, int speciesCount)
        {
            var p = new GladeParams
            {
                ThinFraction = 0f,
                GladeDensity = StreamDensity(),
                BeltDensity = Tuning.BeltDensity,
                MinSpacing = Tuning.GladeMinSpacing,
                InnerMargin = Tuning.InnerMargin,
                BeltGap = Tuning.BeltGap,
                BeltDepth = Tuning.BeltDepth,
                BeltCoverage = Tuning.BeltCoverage,
                BeltNoiseWavelength = Tuning.BeltNoiseWavelength,
                SpeciesCount = math.max(1, speciesCount),
                DominantShare = Tuning.DominantShare,
                AgeWeights = Tuning.AgeWeights,
                MaxPlant = Tuning.MaxPlantPerApply,   // ignored by Compose (it sets the slice)
                AttemptsPerTree = Tuning.AttemptsPerTree,
            };
            if (treeLine)
            {
                p.Mode = GladeMode.Belt;
                p.MinSpacing = Tuning.BeltMinSpacing;
            }
            else
            {
                p.Mode = GladeMode.Glade;
            }
            return p;
        }

        /// <summary>ARMED line: "1 0, 2 6, ... 8 143" trees per hectare from Compose's table</summary>
        private static string DensityTableText()
        {
            var sb = new StringBuilder(96);
            for (int l = 1; l <= SkiGladeCompose.Levels; l++)
            {
                if (l > 1)
                    sb.Append(", ");
                sb.Append(l).Append(' ').Append((SkiGladeCompose.InteriorDensity(l) * 10000f).ToString("0.#"));
            }
            return sb.ToString();
        }

        /// <summary>ARMED line: "1 0, 2 0.15, ... 8 1" from Compose's BeltFactor table</summary>
        private static string BeltFactorTableText()
        {
            var sb = new StringBuilder(96);
            for (int l = 1; l <= SkiGladeCompose.Levels; l++)
            {
                if (l > 1)
                    sb.Append(", ");
                sb.Append(l).Append(' ').Append(SkiGladeCompose.BeltFactor(l).ToString("0.##"));
            }
            return sb.ToString();
        }


        /// <summary>Static-tree quadtree iterator (shape of AreaResourceSystem.TreeIterator, Game.Areas/AreaResourceSystem.cs:621-660):
        /// trees only (IsTree is set iff the entity has Game.Objects.Tree and is not a marker, Game.Objects/SearchSystem.cs:106-123, and
        /// the static tree never holds Temp, :344-358). The mask test is IsTree only: also requiring NotOverridden would drop an
        /// Overridden object from the census (SearchSystem.cs:106, 138-140; OverrideSystem.cs:74-80, 132-140, 158-161 keep IsTree and
        /// clear NotOverridden), and Overridden map trees would come back as "new" wild trees once a fell un-overrode them
        /// (OverrideSystem.cs:1777-1791). Collects bounds hits; the position test runs on the main thread.</summary>
        internal struct TreeBoundsIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>,
            IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 m_Bounds;
            public Triangle2 m_Triangle;
            public bool m_UseTriangle;
            public NativeList<Entity> m_Results;

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                if ((bounds.m_Mask & BoundsMask.IsTree) == 0)
                    return false;
                if (!MathUtils.Intersect(bounds.m_Bounds.xz, m_Bounds))
                    return false;
                return !m_UseTriangle || MathUtils.Intersect(bounds.m_Bounds.xz, m_Triangle);
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (Intersect(bounds))
                    m_Results.Add(entity);
            }
        }

        /// <summary>Fills m_Candidates (Compose's Existing) and m_Own (Compose's Own). Inside the polygon: wild trees are thinnable
        /// unless excluded (IsThinnable); this run's members go to Own; other runs' members and excluded trees are fixed occupants.
        /// Optional band outside the polygon (planting modes): fixed occupants within `bandMargin` of an edge, and this run's members
        /// there go to Own (belt members, or glade members a run edit left outside). A member of this run that is burning or owned is a
        /// fixed occupant this time: its delete could block the whole Apply (ValidationSystem.cs:920-929). Overridden trees
        /// (Game.Common.Overridden, hidden by Game.Objects.OverrideSystem) are included exactly like visible ones and counted in
        /// m_CenOverriddenInside / Band (engine notes, Census).</summary>
        private void CensusTrees(Entity run, float3[] ring, int3[] triangles, bool withBand, float bandMargin)
        {
            m_CenWildInside = 0;
            m_CenFixedInside = 0;
            m_CenBand = 0;
            m_CenForeign = 0;
            m_CenOwnInside = 0;
            m_CenOwnBand = 0;
            m_CenOwnFixed = 0;
            m_CenOverriddenInside = 0;
            m_CenOverriddenBand = 0;
            m_CensusWithBand = withBand;
            m_CensusBandMargin = bandMargin;
            m_Candidates.Clear();
            m_Own.Clear();
            m_Processed.Clear();

            // Synchronous read: complete the writer deps, no reader registration needed (SearchSystem.cs:421-425;
            // TempExtractorTooltipSystem.cs:228-230). If this ever moves into a job, call AddStaticSearchTreeReader (:433-436).
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree =
                m_ObjectSearchSystem.GetStaticSearchTree(readOnly: true, out JobHandle treeDeps);
            treeDeps.Complete();

            var results = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                // Inside: one query per run triangle (a tree on a shared edge is visited twice; m_Processed dedupes)
                for (int t = 0; t < triangles.Length; t++)
                {
                    int3 idx = triangles[t];
                    if (idx.x < 0 || idx.y < 0 || idx.z < 0 ||
                        idx.x >= ring.Length || idx.y >= ring.Length || idx.z >= ring.Length)
                        continue;
                    var tri = new Triangle2(ring[idx.x].xz, ring[idx.y].xz, ring[idx.z].xz);
                    results.Clear();
                    var it = new TreeBoundsIterator
                    {
                        m_Bounds = MathUtils.Bounds(tri),
                        m_Triangle = tri,
                        m_UseTriangle = true,
                        m_Results = results,
                    };
                    tree.Iterate(ref it);
                    for (int r = 0; r < results.Length; r++)
                    {
                        Entity e = results[r];
                        if (m_Processed.Contains(e))
                            continue;
                        if (!EntityManager.TryGetComponent(e, out Game.Objects.Transform tr))
                            continue;
                        if (!MathUtils.Intersect(tri, tr.m_Position.xz)) // AreaResourceSystem.cs:654
                            continue;
                        m_Processed.Add(e);
                        // Deleted lingers in the static tree until Modification5 (SearchSystem.cs:91-99): never a candidate, never an occupant
                        if (EntityManager.HasComponent<Deleted>(e))
                            continue;
                        if (EntityManager.HasComponent<Game.Common.Overridden>(e))
                            m_CenOverriddenInside++;
                        if (EntityManager.TryGetComponent(e, out SkiRunTreeMember member))
                        {
                            AddMember(e, tr.m_Position, member, run, inside: true);
                            continue;
                        }
                        bool canThin = IsThinnable(e);
                        m_Candidates.Add(new Candidate { Entity = e, Position = tr.m_Position, Thinnable = canThin });
                        if (canThin) m_CenWildInside++; else m_CenFixedInside++;
                    }
                }

                if (!withBand || bandMargin <= 0f)
                    return;

                // Band: one bounds query per polygon edge (closing edge included)
                for (int i = 0; i < ring.Length; i++)
                {
                    float2 a = ring[i].xz;
                    float2 b = ring[(i + 1) % ring.Length].xz;
                    results.Clear();
                    var it = new TreeBoundsIterator
                    {
                        m_Bounds = new Bounds2(math.min(a, b) - bandMargin, math.max(a, b) + bandMargin),
                        m_UseTriangle = false,
                        m_Results = results,
                    };
                    tree.Iterate(ref it);
                    for (int r = 0; r < results.Length; r++)
                    {
                        Entity e = results[r];
                        if (m_Processed.Contains(e))
                            continue;
                        if (!EntityManager.TryGetComponent(e, out Game.Objects.Transform tr))
                            continue;
                        if (DistanceToSegment(tr.m_Position.xz, a, b) > bandMargin)
                            continue;
                        m_Processed.Add(e);
                        if (EntityManager.HasComponent<Deleted>(e))
                            continue;
                        if (EntityManager.TryGetComponent(e, out SkiRunTreeMember member))
                        {
                            AddMember(e, tr.m_Position, member, run, inside: false);
                            continue;
                        }
                        m_Candidates.Add(new Candidate { Entity = e, Position = tr.m_Position, Thinnable = false });
                        m_CenBand++;
                        if (EntityManager.HasComponent<Game.Common.Overridden>(e))
                            m_CenOverriddenBand++;
                    }
                }
            }
            finally
            {
                results.Dispose();
            }
        }

        /// <summary>Census split: this run's usable members -> Own; Quick runs filler trees (LayerFiller) count as wild trees
        /// (DECISIONS 415: a run drawn later over the filler can clear it); everything else tagged -> Existing, never thinnable</summary>
        private void AddMember(Entity e, float3 position, SkiRunTreeMember member, Entity run, bool inside)
        {
            if (member.m_Layer == SkiRunTreeMember.LayerFiller)
            {
                bool canThin = inside && IsThinnable(e);
                m_Candidates.Add(new Candidate { Entity = e, Position = position, Thinnable = canThin });
                if (!inside) m_CenBand++;
                else if (canThin) m_CenWildInside++;
                else m_CenFixedInside++;
                return;
            }
            if (member.m_Run == run)
            {
                if (!EntityManager.HasComponent<Owner>(e) && !EntityManager.HasComponent<Game.Events.OnFire>(e))
                {
                    m_Own.Add(new OwnTree
                    {
                        Entity = e,
                        Position = position,
                        Layer = member.m_Layer,
                        Variant = member.m_Variant,
                        PlanGen = member.m_PlanGen,
                    });
                    if (inside) m_CenOwnInside++; else m_CenOwnBand++;
                    return;
                }
                m_Candidates.Add(new Candidate { Entity = e, Position = position, Thinnable = false });
                m_CenOwnFixed++;
                return;
            }
            m_Candidates.Add(new Candidate { Entity = e, Position = position, Thinnable = false });
            m_CenForeign++;
        }

        /// <summary>Exclusions. Excluded: any Owner (building/net/area sub-objects, border props, AreaSpawn trees; vanilla bulldoze
        /// never deletes owned sub-objects either, BulldozeToolSystem.cs:118-129), OnFire (one burning tree is an ErrorType.OnFire that
        /// blocks the whole Apply, ValidationSystem.cs:920-929), SkiRunTreeMember trees (handled by AddMember), prefabs without
        /// TreeData. Not excluded: Native (every map tree is Native, NativeSystem.cs:41-45) and Hidden (our own preview hides the
        /// originals, GenerateObjectsSystem.cs:974-978; excluding it would flip the selection).</summary>
        private bool IsThinnable(Entity e)
        {
            if (EntityManager.HasComponent<Owner>(e))
                return false;
            if (EntityManager.HasComponent<Game.Events.OnFire>(e))
                return false;
            if (EntityManager.TryGetComponent(e, out SkiRunTreeMember member) && member.m_Layer != SkiRunTreeMember.LayerFiller)
                return false;   // filler trees are wild (DECISIONS 415)
            return EntityManager.TryGetComponent(e, out PrefabRef pr) &&
                   EntityManager.HasComponent<TreeData>(pr.m_Prefab);
        }

        private static float DistanceToSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len2 = math.dot(ab, ab);
            float t = len2 > 1e-8f ? math.clamp(math.dot(p - a, ab) / len2, 0f, 1f) : 0f;
            return math.distance(p, a + ab * t);
        }

        /// <summary>Rings of every other bound run (ownership.IsBound) whose Geometry.m_Bounds intersects ours expanded by BeltDepth.
        /// Null when none.</summary>
        private float3[][] CollectExclusionRings(Entity run, Bounds3 runBounds)
        {
            Bounds2 near = new Bounds2(runBounds.min.xz - Tuning.BeltDepth, runBounds.max.xz + Tuning.BeltDepth);
            var rings = new List<float3[]>();
            if (m_RunQuery.IsEmptyIgnoreFilter)
                return null;
            m_RunQuery.CompleteDependency();
            using (var chunks = m_RunQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var geometryHandle = GetComponentTypeHandle<Geometry>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = chunks[c].GetNativeArray(ref geometryHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity e = entities[i];
                        if (e == run || !m_RunPrefabs.Contains(prefabRefs[i].m_Prefab))
                            continue;
                        if (!MathUtils.Intersect(geometries[i].m_Bounds.xz, near))
                            continue;
                        if (!m_Ownership.IsBound(e))
                            continue;
                        DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(e, true);
                        if (nodes.Length < 3)
                            continue;
                        var r = new float3[nodes.Length];
                        for (int k = 0; k < nodes.Length; k++)
                            r[k] = nodes[k].m_Position;
                        rings.Add(r);
                    }
                }
            }
            return rings.Count > 0 ? rings.ToArray() : null;
        }


        /// <summary>Planted tree. No owner. Optional: a collision becomes a canceled Temp instead of an Apply-blocking Error
        /// (ValidationSystem.cs:1667-1712). m_IsDecoration = true leaves the archetype's Decoration enabled
        /// (GenerateObjectsSystem.cs:1311-1317): the tree never ages and counts no wood. y is sampled here because the pipeline never
        /// terrain-snaps (GenerateObjectsSystem.cs:1253). The slot is recorded by xz key: the Temp's Transform is the definition
        /// position verbatim, which is how a canceled Temp maps back to its slot (skip set).</summary>
        private bool EmitPlant(EntityCommandBuffer ecb, ref TerrainHeightData heightData, PlantedTree tree, int slot)
        {
            int species = tree.Species;
            if (species < 0 || species >= m_SpeciesEntities.Count)
                species = 0;
            if (!EmitPlantDefinition(ecb, ref heightData, m_SpeciesEntities[species], m_SpeciesOffsetY[species], tree, out float3 pos))
                return false;
            m_EmittedSlots[PositionKey(pos)] = slot;
            return true;
        }

        /// <summary>The planted-tree definition (EmitPlant; shared with the Quick runs tree filler). pos = the definition position (the
        /// Temp's Transform verbatim). False for a non-finite position or rotation.</summary>
        internal static bool EmitPlantDefinition(EntityCommandBuffer ecb, ref TerrainHeightData heightData, Entity prefab, float offsetY,
            PlantedTree tree, out float3 pos)
        {
            pos = tree.Position;
            if (!math.all(math.isfinite(pos.xz)) || !math.isfinite(tree.Rotation))
                return false;
            // SampleTransform ground branch (:1052-1055): terrain height + placement offset. Trees are not Hovering/Shoreline/Floating
            // (unverified per species; the vanilla water branches are not replicated, water hits are Optional-canceled anyway).
            pos.y = TerrainUtils.SampleHeight(ref heightData, pos) + offsetY;
            quaternion rot = quaternion.RotateY(tree.Rotation);

            Entity e = ecb.CreateEntity();
            ecb.AddComponent(e, new CreationDefinition
            {
                m_Prefab = prefab,
                m_Owner = Entity.Null,                      // no owner
                m_RandomSeed = PositionSeed(pos),           // -> PseudoRandomSeed (mesh variant), :1183/:1381
                m_Flags = CreationFlags.Optional,
            });
            // Vanilla top-level values (ObjectToolBaseSystem.UpdateObject :1129-1202); local == world because there is no owner Transform (:1198-1202)
            ecb.AddComponent(e, new ObjectDefinition
            {
                m_Position = pos,
                m_Rotation = rot,
                m_LocalPosition = pos,
                m_LocalRotation = rot,
                m_ParentMesh = -1,       // OnGround, no Elevation component (GenerateObjectsSystem.cs:1307-1310)
                m_Probability = 100,
                m_PrefabSubIndex = -1,
                m_Scale = 1f,
                m_Intensity = 1f,
                m_Elevation = 0f,
                m_Age = tree.Age,        // -> InitializeTreeState (GenerateObjectsSystem.cs:984-988)
                m_IsDecoration = true,
            });
            ecb.AddComponent(e, default(Updated));
            return true;
        }

        /// <summary>Felled or removed tree: the vanilla bulldoze definition for an unowned static object (BulldozeToolSystem.cs:664 ->
        /// AddEntity :731-751, ObjectDefinition :812-875). m_Prefab stays unset (filled from the original's PrefabRef,
        /// GenerateObjectsSystem.cs:744-746); the ObjectDefinition is mandatory (:362-388). Result: Temp{Delete|Essential}, a refund only
        /// when the original carries Recent (:1007-1014), Hidden on the original (:974-978); Apply adds Deleted to the original
        /// (ApplyObjectsSystem.cs:342-352, 484-504).</summary>
        private bool EmitThin(EntityCommandBuffer ecb, Entity tree, out float3 position) => EmitThinDefinition(EntityManager, ecb, tree, out position);

        /// <summary>EmitThin's body, shared with the Quick runs tree filler (its misplaced members and its undo).</summary>
        internal static bool EmitThinDefinition(EntityManager em, EntityCommandBuffer ecb, Entity tree, out float3 position)
        {
            position = default;
            if (!em.Exists(tree) || em.HasComponent<Deleted>(tree) ||
                !em.TryGetComponent(tree, out Game.Objects.Transform tr))
                return false;
            position = tr.m_Position;

            var od = new ObjectDefinition
            {
                m_Position = tr.m_Position,
                m_Rotation = tr.m_Rotation,
                m_LocalPosition = tr.m_Position,   // no owner: local == world (:858-862)
                m_LocalRotation = tr.m_Rotation,
                m_Probability = 100,
                m_PrefabSubIndex = -1,
            };
            if (em.TryGetComponent(tree, out Game.Objects.Elevation elevation))
            {
                od.m_Elevation = elevation.m_Elevation;
                od.m_ParentMesh = Game.Objects.ObjectUtils.GetSubParentMesh(elevation.m_Flags);
            }
            else
            {
                od.m_ParentMesh = -1;
            }
            // LocalTransformCache (editor containers, :837-846) is not replicated: unowned game-mode trees do not carry it (unverified)

            Entity e = ecb.CreateEntity();
            ecb.AddComponent(e, new CreationDefinition
            {
                m_Original = tree,
                m_Owner = Entity.Null,             // never set an owner on a delete
                m_Flags = CreationFlags.Delete,
            });
            ecb.AddComponent(e, od);
            ecb.AddComponent(e, default(Updated));
            return true;
        }

        /// <summary>Stable per-position seed (0.1 m quantization of x/z, the SkiRunPropSystem HashSeed quantization), so a re-emit never
        /// changes a tree's mesh variant. The low 16 bits (what PseudoRandomSeed keeps, GenerateObjectsSystem.cs:1381) are never 0.</summary>
        private static int PositionSeed(float3 p)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)(int)math.round(p.x * 10f)) * 16777619u;
                h = (h ^ (uint)(int)math.round(p.z * 10f)) * 16777619u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return (int)(h | 1u);
            }
        }

        internal static long PositionKey(float3 p)
        {
            unchecked
            {
                long x = (int)math.round(p.x * 10f);
                long z = (int)math.round(p.z * 10f);
                return (x << 32) ^ (z & 0xFFFFFFFFL);
            }
        }



        /// <summary>Apply frame, inside the tool's OnUpdate (ToolUpdate), i.e. before this frame's ApplyTool phase acts on the Temps
        /// (ToolOutputSystem.cs:20-31): records what the applied plan was composed from and what each Temp will do. m_Candidates /
        /// m_Own are still the census of the plan being applied (the last PlanAndEmit of m_PlannedRun). Allocation: reused collections only.</summary>
        private void TakeApplySnapshot()
        {
            m_CheckPending = true;
            m_CheckRun = m_PlannedRun;
            m_CheckTreeLine = m_PlannedTreeLine;
            m_CheckLevel = m_PlannedLevel;
            m_CheckBandMargin = m_CensusBandMargin;
            m_CheckWithBand = m_CensusWithBand;
            m_CheckFrame = UnityEngine.Time.frameCount;
            m_CheckOthersBefore.Clear();
            m_CheckOverriddenBefore.Clear();
            m_CheckDoomedOriginals.Clear();
            m_CheckCancelledSpots.Clear();
            m_CheckPlantedSpots.Clear();
            m_CheckFelledSpots.Clear();
            m_CheckFelled = 0;
            m_CheckRemoved = 0;
            m_CheckPlanted = 0;
            m_CheckCancelled = 0;
            m_CheckDeleteCancelled = 0;

            int ownFixed = 0;
            for (int i = 0; i < m_Candidates.Count; i++)
            {
                Entity e = m_Candidates[i].Entity;
                if (IsMemberOf(e, m_CheckRun))
                {
                    ownFixed++;   // this run's burning / owned member (a fixed occupant)
                    continue;
                }
                m_CheckOthersBefore.Add(e);
                if (EntityManager.Exists(e) && EntityManager.HasComponent<Game.Common.Overridden>(e))
                    m_CheckOverriddenBefore.Add(e);
            }
            m_CheckOwnBefore = m_Own.Count + ownFixed;

            if (m_TempCensusQuery.IsEmptyIgnoreFilter)
                return;
            m_TempCensusQuery.CompleteDependency();
            var tempHandle = GetComponentTypeHandle<Temp>(isReadOnly: true);
            var transformHandle = GetComponentTypeHandle<Game.Objects.Transform>(isReadOnly: true);
            bool checkRemove = m_EmittedRemoveOriginals.Count > 0;
            using (var chunks = m_TempCensusQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    if (!chunks[c].Has(ref transformHandle))
                        continue;
                    var temps = chunks[c].GetNativeArray(ref tempHandle);
                    var transforms = chunks[c].GetNativeArray(ref transformHandle);
                    for (int i = 0; i < temps.Length; i++)
                    {
                        Temp t = temps[i];
                        bool cancel = (t.m_Flags & TempFlags.Cancel) != 0;
                        float3 pos = transforms[i].m_Position;
                        if ((t.m_Flags & TempFlags.Delete) != 0)
                        {
                            // ApplyObjectsSystem.cs:337-352: Cancel first (original kept), else Delete
                            if (cancel)
                            {
                                m_CheckDeleteCancelled++;
                                continue;
                            }
                            m_CheckDoomedOriginals.Add(t.m_Original);
                            if (checkRemove && m_EmittedRemoveOriginals.Contains(t.m_Original))
                            {
                                m_CheckRemoved++;
                            }
                            else
                            {
                                m_CheckFelled++;
                                m_CheckFelledSpots.Add(pos);
                            }
                        }
                        else if (t.m_Original == Entity.Null)
                        {
                            if (cancel)
                            {
                                m_CheckCancelled++;
                                m_CheckCancelledSpots.Add(pos);
                            }
                            else
                            {
                                m_CheckPlanted++;
                                m_CheckPlantedSpots.Add(pos);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>True when e carries SkiRunTreeMember for run</summary>
        private bool IsMemberOf(Entity e, Entity run)
        {
            return EntityManager.Exists(e) && EntityManager.TryGetComponent(e, out SkiRunTreeMember m) && m.m_Run == run;
        }

        /// <summary>The first Replan after an Apply (or OnStopRunning when none came): re-takes the applied run's census (same band as
        /// the applied plan) and writes one "apply check" line (engine notes, Apply check). INFO when it balances, WARN "APPLY MISMATCH"
        /// otherwise. Apply time only (one census + set work); never per frame.</summary>
        private void RunApplyCheck()
        {
            m_CheckPending = false;
            Entity run = m_CheckRun;
            string head = $"Glade: apply check — run {EntityText(run)} {TargetText(m_CheckTreeLine)} L{m_CheckLevel} " +
                          $"({LevelName(m_CheckLevel)})";
            if (UnityEngine.Time.frameCount == m_CheckFrame)
            {
                LastApplyCheck = "skipped — asked on the apply frame itself (the apply lands later this frame)";
                Mod.Log.Warn($"{head}: {LastApplyCheck}");
                return;
            }
            if (run == Entity.Null || !EntityManager.Exists(run) || EntityManager.HasComponent<Deleted>(run))
            {
                LastApplyCheck = "skipped — the run no longer exists";
                Mod.Log.Info($"{head}: {LastApplyCheck}");
                return;
            }
            if (!TryCopyRunGeometry(run, out float3[] ring, out int3[] triangles, out Bounds3 _, out string why))
            {
                LastApplyCheck = $"skipped — {why}";
                Mod.Log.Info($"{head}: {LastApplyCheck}");
                return;
            }
            CensusTrees(run, ring, triangles, m_CheckWithBand, m_CheckBandMargin);

            if (m_TagSystem == null)
                m_TagSystem = World.GetExistingSystemManaged<SkiGladeTagSystem>();
            SkiGladeTagSystem tag = m_TagSystem;
            bool tagKnown = tag != null && tag.LastApplyRun == run && tag.LastApplyFrame == m_CheckFrame;
            int created = tagKnown ? tag.LastCreatePath : -1;
            int tagged = tagKnown ? tag.LastTagged : -1;

            int ownFixedNow = 0, othersNow = 0;
            int newAtCancelled = 0, newAtPlanted = 0, newNearFelled = 0, newElsewhere = 0, unOverridden = 0, doomedStanding = 0;
            m_CheckOthersNow.Clear();
            for (int i = 0; i < m_Candidates.Count; i++)
            {
                Entity e = m_Candidates[i].Entity;
                if (IsMemberOf(e, run))
                {
                    ownFixedNow++;
                    continue;
                }
                othersNow++;
                m_CheckOthersNow.Add(e);
                if (m_CheckDoomedOriginals.Contains(e))
                {
                    doomedStanding++;
                    continue;
                }
                if (m_CheckOthersBefore.Contains(e))
                {
                    if (m_CheckOverriddenBefore.Contains(e) && !EntityManager.HasComponent<Game.Common.Overridden>(e))
                        unOverridden++;
                    continue;
                }
                // A tree the plan never saw. Where is it?
                float3 p = m_Candidates[i].Position;
                if (NearAny(m_CheckCancelledSpots, p, Tuning.CheckSpotRadiusM))
                    newAtCancelled++;
                else if (NearAny(m_CheckPlantedSpots, p, Tuning.CheckSpotRadiusM))
                    newAtPlanted++;
                else if (NearAny(m_CheckFelledSpots, p, Tuning.CheckFelledRadiusM))
                    newNearFelled++;
                else
                    newElsewhere++;
            }
            for (int i = 0; i < m_Own.Count; i++)
            {
                if (m_CheckDoomedOriginals.Contains(m_Own[i].Entity))
                    doomedStanding++;
            }
            int vanished = 0;
            foreach (Entity e in m_CheckOthersBefore)
            {
                if (!m_CheckOthersNow.Contains(e) && !m_CheckDoomedOriginals.Contains(e))
                    vanished++;
            }

            int ownNow = m_Own.Count + ownFixedNow;
            int taggedForExpect = tagged >= 0 ? tagged : m_CheckPlanted;
            int ownExpected = m_CheckOwnBefore - m_CheckRemoved + taggedForExpect;
            int othersBefore = m_CheckOthersBefore.Count;
            int othersExpected = othersBefore - m_CheckFelled;
            int newTotal = newAtCancelled + newAtPlanted + newNearFelled + newElsewhere;
            bool ok = ownNow == ownExpected && othersNow == othersExpected && newTotal == 0 && vanished == 0 &&
                      doomedStanding == 0 && (!tagKnown || created == tagged) &&
                      (!tagKnown || tagged == m_CheckPlanted);

            LastApplyCheck =
                $"created {(tagKnown ? created.ToString() : "?")} by ApplyObjectsSystem's create path " +
                $"(tagged {(tagKnown ? tagged.ToString() : "? — tag system did not report this apply")}; planned " +
                $"{m_CheckPlanted}), canceled {m_CheckCancelled} planted spot(s) (not created), " +
                $"{m_CheckDeleteCancelled} canceled delete Temp(s); own members now {ownNow} (expected {ownExpected} = " +
                $"{m_CheckOwnBefore} − removed {m_CheckRemoved} + tagged {taggedForExpect}); other trees now {othersNow} " +
                $"(expected {othersExpected} = {othersBefore} − felled {m_CheckFelled}); NEW untagged trees {newTotal}: " +
                $"{newAtCancelled} at canceled planted spots, {newAtPlanted} at planted spots, {newNearFelled} " +
                $"within {Tuning.CheckFelledRadiusM} m of a felled tree, {newElsewhere} elsewhere; vanished {vanished}; " +
                $"doomed still standing {doomedStanding}; un-overridden since the apply {unOverridden} (hidden " +
                "before, counted before — OverrideSystem.cs:1777-1791)" +
                (ok ? " — OK" : " — APPLY MISMATCH");
            if (ok)
                Mod.Log.Info($"{head}: {LastApplyCheck}");
            else
                Mod.Log.Warn($"{head}: {LastApplyCheck} (a tree at a canceled planted spot means a canceled Temp " +
                             "was created; near a felled tree, a tree the census could not see; created ≠ tagged, a " +
                             "create-path Temp the tag system missed — see EngineNotes/SkiGladeToolSystem.md)");
        }

        private static bool NearAny(List<float3> spots, float3 p, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < spots.Count; i++)
            {
                if (math.distancesq(spots[i].xz, p.xz) <= r2)
                    return true;
            }
            return false;
        }

        /// <summary>Tool stop / load: a pending check runs (stop, a later frame) or is dropped with a line (load); never silently lost</summary>
        private void FlushApplyCheck(bool canRun, string why)
        {
            if (!m_CheckPending)
                return;
            if (canRun && UnityEngine.Time.frameCount != m_CheckFrame)
            {
                RunApplyCheck();
                return;
            }
            m_CheckPending = false;
            LastApplyCheck = $"dropped — {why}";
            Mod.Log.Info($"Glade: apply check — run {EntityText(m_CheckRun)}: {LastApplyCheck}");
        }

        /// <summary>The ready frame (engine notes, Lifecycle): census, learning, hide, derived state</summary>
        private void OnPreviewReady()
        {
            RefreshPreviewCensus(force: true);
            int learned = LearnBlockedSpots(out bool moneyShort);
            FixDoomedVisibility(readyFrame: true);
            if (moneyShort)
                m_PlanState = GladePlanState.MoneyShort;
            if (learned > 0)
            {
                // Re-plan with the skip set now, whether or not something else is applicable: a re-plan right after an apply must read
                // complete. Under the count rule a canceled spot leaves the run below T, so without this the re-plan after the Apply would
                // plant replacements at the next stream slots ("Trees to plant k" instead of "nothing to change"). With the skip set the new
                // preview already takes those slots, and the Apply reaches T in one click. Converges: each round re-plans only on new learned
                // slots (the false-Complete guard is a special case of this). Apply waits for the new preview (UpdateFrame gates it on
                // m_ReplanWhy == null).
                m_ReplanWhy = LiveTemps > 0
                    ? $"{learned} blocked spot(s) learned — re-planned so the apply reaches the target"
                    : $"{learned} blocked spot(s) learned and nothing else applicable";
                return;
            }
            if (LiveTemps > 0)
                return;
            if (!moneyShort)
            {
                m_PlanState = GladePlanState.Complete;
                Mod.Log.Info($"Glade: preview — nothing applicable after validation ({PreviewCancelled} " +
                             $"planted spot(s) canceled, {PreviewBlockedKnown} blocked spot(s) known) — layer complete");
            }
        }

        /// <summary>planted = create Temps (m_Original Null, not Delete), canceled = those with TempFlags.Cancel (ValidationSystem.cs:1705-1712),
        /// thin / remove = Delete Temps without Cancel whose original is not / is one of the emitted remove originals (a Delete Temp
        /// with Cancel takes ApplyObjectsSystem's Cancel path first and keeps its original, ApplyObjectsSystem.cs:337-340, 474-482:
        /// counted in PreviewDeleteCancelled, never as felled), cost = sum of m_Cost of non-Cancel Temps (what ToolApplySystem charges,
        /// ToolApplySystem.cs:59-73), m_MisHiddenDoomed = Delete Temps whose Hidden state differs from m_HideDoomed. The Temp set is
        /// ours alone while the tool is active. Once per frame; native allocation only.</summary>
        private void RefreshPreviewCensus(bool force)
        {
            if (!m_PreviewReady)
                return;
            int frame = UnityEngine.Time.frameCount;
            if (!force && frame == m_CensusFrame)
                return;
            m_CensusFrame = frame;
            int plant = 0, cancelled = 0, thin = 0, remove = 0, cost = 0, misHidden = 0, deleteCancelled = 0;
            if (!m_TempCensusQuery.IsEmptyIgnoreFilter)
            {
                m_TempCensusQuery.CompleteDependency();
                var tempHandle = GetComponentTypeHandle<Temp>(isReadOnly: true);
                var hiddenHandle = GetComponentTypeHandle<Game.Tools.Hidden>(isReadOnly: true);
                bool checkRemove = m_EmittedRemoveOriginals.Count > 0;
                using (var chunks = m_TempCensusQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        bool hidden = chunks[c].Has(ref hiddenHandle);
                        var temps = chunks[c].GetNativeArray(ref tempHandle);
                        for (int i = 0; i < temps.Length; i++)
                        {
                            Temp t = temps[i];
                            bool isCancel = (t.m_Flags & TempFlags.Cancel) != 0;
                            if ((t.m_Flags & TempFlags.Delete) != 0)
                            {
                                if (isCancel)
                                    deleteCancelled++;
                                else if (checkRemove && m_EmittedRemoveOriginals.Contains(t.m_Original))
                                    remove++;
                                else
                                    thin++;
                                if (hidden != m_HideDoomed)
                                    misHidden++;
                            }
                            else if (t.m_Original == Entity.Null)
                            {
                                plant++;
                                if (isCancel) cancelled++;
                            }
                            if (!isCancel)
                                cost += t.m_Cost;
                        }
                    }
                }
            }
            PreviewPlant = plant;
            PreviewCancelled = cancelled;
            PreviewThin = thin;
            PreviewRemove = remove;
            PreviewDeleteCancelled = deleteCancelled;
            PreviewCost = cost;
            m_MisHiddenDoomed = misHidden;
            PreviewErrors = m_ErrorQuery.IsEmptyIgnoreFilter ? 0 : m_ErrorQuery.CalculateEntityCount();
        }

        /// <summary>Blocked-spot learning (ready frame). Returns the number of new slots added to m_SkipSlots[PlanKey]; moneyShort =
        /// money may be short, nothing learned.</summary>
        private int LearnBlockedSpots(out bool moneyShort)
        {
            moneyShort = false;
            if (m_PlannedLayer == LayerNone || PreviewCancelled <= 0 || m_EmittedSlots.Count == 0)
                return 0;

            // PlayerMoney.money (Game.City/PlayerMoney.cs:15-24; unlimited money reads 2e9): the value ValidationSystem budgets Optional
            // Temps against (ValidationSystem.cs:1434-1449)
            long need = (long)PreviewPlant * Tuning.MaxTreeCost;
            if (TryReadMoney(out int money) && money < need)
            {
                moneyShort = true;
                if (!m_MoneyShortThisPlan)
                {
                    m_MoneyShortThisPlan = true;
                    Mod.Log.Info($"Glade: preview — {PreviewCancelled} planted spot(s) canceled while money " +
                                 $"{money} < {PreviewPlant} × {Tuning.MaxTreeCost}: nothing learned (validation " +
                                 "also cancels Optional trees for money; not distinguishable per tree)");
                }
                return 0;
            }

            HashSet<int> set = GetOrCreateSkipSet(m_CurrentPlanKey);
            int learned = 0, unmapped = 0, seen = 0;
            m_TempCensusQuery.CompleteDependency();
            var tempHandle = GetComponentTypeHandle<Temp>(isReadOnly: true);
            var transformHandle = GetComponentTypeHandle<Game.Objects.Transform>(isReadOnly: true);
            using (var chunks = m_TempCensusQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    if (!chunks[c].Has(ref transformHandle))
                        continue;
                    var temps = chunks[c].GetNativeArray(ref tempHandle);
                    var transforms = chunks[c].GetNativeArray(ref transformHandle);
                    for (int i = 0; i < temps.Length; i++)
                    {
                        Temp t = temps[i];
                        if (t.m_Original != Entity.Null || (t.m_Flags & TempFlags.Delete) != 0 ||
                            (t.m_Flags & TempFlags.Cancel) == 0)
                            continue;
                        seen++;
                        if (m_EmittedSlots.TryGetValue(PositionKey(transforms[i].m_Position), out int slot))
                        {
                            if (set.Add(slot))
                                learned++;
                        }
                        else
                        {
                            unmapped++;
                        }
                    }
                }
            }
            PreviewBlockedKnown = set.Count;
            Mod.Log.Info($"Glade: preview — {seen} planted spot(s) canceled by validation: learned {learned} " +
                         $"new blocked spot(s) (skip set {set.Count} for plan key {m_CurrentPlanKey:X16})" +
                         (unmapped > 0 ? $"; {unmapped} not mapped to a slot (unexpected)" : ""));
            return learned;
        }

        private HashSet<int> GetOrCreateSkipSet(ulong key)
        {
            if (m_SkipSlots.TryGetValue(key, out HashSet<int> set))
                return set;
            if (m_SkipKeyOrder.Count >= Tuning.SkipKeysMax)
            {
                ulong oldest = m_SkipKeyOrder[0];
                m_SkipKeyOrder.RemoveAt(0);
                m_SkipSlots.Remove(oldest);
                m_SkipEvictions++;
            }
            set = new HashSet<int>();
            m_SkipSlots.Add(key, set);
            m_SkipKeyOrder.Add(key);
            return set;
        }

        private int SkipCountFor(ulong key)
        {
            return m_SkipSlots.TryGetValue(key, out HashSet<int> set) ? set.Count : 0;
        }

        private bool TryReadMoney(out int money)
        {
            Entity city = m_CitySystem.City;   // Game.Simulation/CitySystem.cs:29
            if (city != Entity.Null && EntityManager.TryGetComponent(city, out Game.City.PlayerMoney pm))
            {
                money = pm.money;
                return true;
            }
            money = 0;
            return false;
        }

        /// <summary>After-state: Delete Temps get Game.Tools.Hidden + BatchesUpdated (hiding on) or lose Hidden (fallback, hiding off)
        /// through the barrier's ECB: Temp entities only, never an original. The ECB plays back in this frame's ToolUpdate, before any
        /// Cleanup, so every target still exists. Ready frame: one line per plan (WARN when the Delete Temps do not match what was
        /// emitted); hold frames: self-heal, logged once per plan.</summary>
        private void FixDoomedVisibility(bool readyFrame)
        {
            int total = 0, totalRemove = 0, alreadyRight = 0;
            var fix = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                if (!m_TempCensusQuery.IsEmptyIgnoreFilter)
                {
                    m_TempCensusQuery.CompleteDependency();
                    var entityHandle = GetEntityTypeHandle();
                    var tempHandle = GetComponentTypeHandle<Temp>(isReadOnly: true);
                    var hiddenHandle = GetComponentTypeHandle<Game.Tools.Hidden>(isReadOnly: true);
                    bool checkRemove = m_EmittedRemoveOriginals.Count > 0;
                    using (var chunks = m_TempCensusQuery.ToArchetypeChunkArray(Allocator.Temp))
                    {
                        for (int c = 0; c < chunks.Length; c++)
                        {
                            bool hidden = chunks[c].Has(ref hiddenHandle);
                            var entities = chunks[c].GetNativeArray(entityHandle);
                            var temps = chunks[c].GetNativeArray(ref tempHandle);
                            for (int i = 0; i < temps.Length; i++)
                            {
                                Temp t = temps[i];
                                if ((t.m_Flags & TempFlags.Delete) == 0)
                                    continue;
                                total++;
                                if (checkRemove && m_EmittedRemoveOriginals.Contains(t.m_Original))
                                    totalRemove++;
                                if (hidden == m_HideDoomed)
                                    alreadyRight++;
                                else
                                    fix.Add(entities[i]);
                            }
                        }
                    }
                }

                if (fix.Length > 0)
                {
                    EntityCommandBuffer ecb = m_Barrier.CreateCommandBuffer();
                    for (int i = 0; i < fix.Length; i++)
                    {
                        Entity e = fix[i];
                        if (m_HideDoomed)
                            ecb.AddComponent(e, default(Game.Tools.Hidden));   // GenerateObjectsSystem.cs:976
                        else
                            ecb.RemoveComponent<Game.Tools.Hidden>(e);
                        ecb.AddComponent(e, default(Game.Common.BatchesUpdated));   // :977
                    }
                }
                m_MisHiddenDoomed = 0;

                if (total == 0)
                    return;
                int totalFell = total - totalRemove;
                if (readyFrame)
                {
                    if (m_HideDoomed)
                    {
                        Mod.Log.Info($"Glade: preview — hid {total} doomed tree(s) (fell {totalFell} + remove " +
                                     $"{totalRemove})" + (alreadyRight > 0 ? $"; {alreadyRight} reused Temp(s) were already hidden" : ""));
                    }
                    else
                    {
                        Mod.Log.Info($"Glade: preview — {total} doomed tree(s) left visible (fell {totalFell} + remove " +
                                     $"{totalRemove}; hiding OFF, rings {Tuning.FallbackRingDiameter} m)" +
                                     (fix.Length > 0 ? $"; un-hid {fix.Length} reused Temp(s)" : ""));
                    }
                    if (total != m_EmittedFell + m_EmittedRemove && !m_HideWarnedThisPlan)
                    {
                        m_HideWarnedThisPlan = true;
                        Mod.Log.Warn($"Glade: preview — {total} Delete Temp(s) found but {m_EmittedFell} fell + " +
                                     $"{m_EmittedRemove} remove emitted (warned once per plan)");
                    }
                }
                else if (fix.Length > 0 && !m_SelfHealLoggedThisPlan)
                {
                    m_SelfHealLoggedThisPlan = true;
                    Mod.Log.Info(m_HideDoomed
                        ? $"Glade: preview — re-hid {fix.Length} doomed tree(s) that showed without Hidden (self-heal; once per plan)"
                        : $"Glade: preview — un-hid {fix.Length} doomed tree(s) (hiding OFF; once per plan)");
                }
            }
            finally
            {
                fix.Dispose();
            }
        }


        /// <summary>Candidate outlines: copies of every bound run's ring, refreshed every CandidateRefreshS (an order-version gate would
        /// miss in-place Node edits and binding changes). Also the live CityBoundRuns. One chunk walk per second.</summary>
        private void RefreshCandidatesIfDue()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < m_CandidateNextRefresh)
                return;
            m_CandidateNextRefresh = now + Tuning.CandidateRefreshS;
            int runs = 0, points = 0, bound = 0;
            if (!m_RunQuery.IsEmptyIgnoreFilter)
            {
                m_RunQuery.CompleteDependency();
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                using (var chunks = m_RunQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        var entities = chunks[c].GetNativeArray(entityHandle);
                        var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                        for (int i = 0; i < entities.Length; i++)
                        {
                            if (!m_RunPrefabs.Contains(prefabRefs[i].m_Prefab))
                                continue;
                            Entity e = entities[i];
                            if (!m_Ownership.IsBound(e))
                                continue;
                            bound++;
                            DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(e, true);
                            int count = nodes.Length;
                            if (count < 2)
                                continue;
                            GrowKeep(ref m_CandidatePoints, points + count);
                            GrowKeep(ref m_CandidateStarts, runs + 1);
                            GrowKeep(ref m_CandidateCounts, runs + 1);
                            for (int k = 0; k < count; k++)
                                m_CandidatePoints[points + k] = nodes[k].m_Position;
                            m_CandidateStarts[runs] = points;
                            m_CandidateCounts[runs] = count;
                            runs++;
                            points += count;
                        }
                    }
                }
            }
            // The dashed pieces of every candidate ring (cut along the whole perimeter, not per edge)
            m_CandidateDashA.Clear();
            m_CandidateDashB.Clear();
            for (int r = 0; r < runs; r++)
            {
                int room = MaxCandidateDashes - m_CandidateDashA.Count;
                if (room <= 0)
                    break;
                m_CandidateRing.Clear();
                int s0 = m_CandidateStarts[r];
                for (int k = 0; k < m_CandidateCounts[r]; k++)
                    m_CandidateRing.Add(m_CandidatePoints[s0 + k]);
                PisteMapRules.DashRing(m_CandidateRing, RunOutlineRules.GladeDashM, RunOutlineRules.GladeGapM, room,
                    m_CandidateDashA, m_CandidateDashB);
            }
            m_CandidateRunCount = runs;
            m_CandidatePointCount = points;
            m_CityBoundRuns = bound;
        }

        private void DrawOverlay()
        {
            // Candidate rings while nothing is pending and the hover is not a bound run (so the hint can draw over terrain)
            bool candidates = !m_HasPending && m_HoverKind != GladeHoverKind.Bound && m_CandidateRunCount > 0;
            if (m_OutlineCount < 2 && m_ThinMarkerCount == 0 && m_RemoveMarkerCount == 0 && !candidates)
                return;
            // Main-thread write: complete the existing writers first; no AddBufferWriter needed without a job (OverlayRenderSystem.cs:603-634, 636)
            OverlayRenderSystem.Buffer buffer = m_OverlayRenderSystem.GetBuffer(out JobHandle overlayDeps);
            overlayDeps.Complete();

            // Projected drapes onto terrain; absolute lines cut through a mountain (OverlayRenderSystem.cs:282-294). DrawLine overload :207.
            var flags = OverlayRenderSystem.StyleFlags.Projected;
            var roundness = new float2(1f, 1f);

            // A glade's run outline is violet-magenta (RunOutlineRules.GladeOutlineColour, the one place), dashed for candidate runs
            // (pieces cut at the candidate refresh), solid for the hovered / pending run.
            if (candidates)
            {
                int pieces = m_CandidateDashA.Count;
                for (int i = 0; i < pieces; i++)
                {
                    buffer.DrawLine(s_GladeCandidateColor, s_GladeCandidateColor, 0f, flags,
                        new Line3.Segment(m_CandidateDashA[i], m_CandidateDashB[i]), RunOutlineRules.GladeLineWidthM, roundness);
                }
            }

            for (int i = 0; i < m_OutlineCount && m_OutlineCount >= 2; i++)
            {
                float3 a = m_OutlineRing[i];
                float3 b = m_OutlineRing[(i + 1) % m_OutlineCount];
                buffer.DrawLine(s_GladeColor, s_GladeColor, 0f, flags, new Line3.Segment(a, b),
                    RunOutlineRules.GladeLineWidthM, roundness);
            }

            if (m_PlannedTreeLine && m_BeltCount >= 2)
            {
                for (int i = 0; i < m_BeltCount; i++)
                {
                    float3 a = m_BeltRing[i];
                    float3 b = m_BeltRing[(i + 1) % m_BeltCount];
                    buffer.DrawDashedLine(m_HoverColor, m_HoverColor, 0f, flags, new Line3.Segment(a, b),
                        Tuning.BeltRingWidth, 4f, 4f);
                }
            }

            // Rings, outline only (fill alpha 0: a Projected fill paints every mesh in its column, including planted trees within 2.5 m of a
            // stump). DrawCircle(outline, fill, outlineWidth, flags, direction, position, diameter), OverlayRenderSystem.cs:138.
            float diameter = m_HideDoomed ? Tuning.ThinMarkerDiameter : Tuning.FallbackRingDiameter;
            var up = new float2(0f, 1f);
            for (int i = 0; i < m_ThinMarkerCount; i++)
            {
                buffer.DrawCircle(Tuning.FellRingColor, s_FellFill, Tuning.RingOutlineM, flags, up,
                    m_ThinMarkers[i], diameter);
            }
            for (int i = 0; i < m_RemoveMarkerCount; i++)
            {
                buffer.DrawCircle(Tuning.ReplaceRingColor, s_ReplaceFill, Tuning.RingOutlineM, flags, up,
                    m_RemoveMarkers[i], diameter);
            }
        }

        private void ReadColors()
        {
            // Fallback is vanilla's (AreaBorderRenderSystem.cs:664-670)
            m_HoverColor = new UnityEngine.Color(0.5f, 0.5f, 1f, 0.5f);
            if (!m_RenderingSettingsQuery.IsEmptyIgnoreFilter)
            {
                RenderingSettingsData settings = m_RenderingSettingsQuery.GetSingleton<RenderingSettingsData>();
                m_HoverColor = settings.m_HoveredColor;
            }
            m_CandidateColor = m_HoverColor;
            m_CandidateColor.a = m_HoverColor.a * Tuning.CandidateAlpha;
        }

        /// <summary>Outer edge of the tree-line band for the overlay: each vertex offset along the mitred outward normal (sign from the
        /// shoelace winding on xz; miter clamped at sharp corners). Computed here rather than with AreaUtils.GetExpandedNode, whose
        /// positive-is-outward convention is inferred, not verified.</summary>
        private void BuildBeltRing(float3[] ring, float offset)
        {
            int n = ring.Length;
            EnsureCapacity(ref m_BeltRing, n);
            float area2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float2 a = ring[i].xz;
                float2 b = ring[(i + 1) % n].xz;
                area2 += a.x * b.y - b.x * a.y;
            }
            float sign = area2 >= 0f ? 1f : -1f; // CCW (+) -> outward normal of d is (d.y, -d.x)
            for (int i = 0; i < n; i++)
            {
                float2 prev = ring[(i + n - 1) % n].xz;
                float2 cur = ring[i].xz;
                float2 next = ring[(i + 1) % n].xz;
                float2 d0 = math.normalizesafe(cur - prev);
                float2 d1 = math.normalizesafe(next - cur);
                float2 n0 = sign * new float2(d0.y, -d0.x);
                float2 n1 = sign * new float2(d1.y, -d1.x);
                float2 m = math.normalizesafe(n0 + n1, n1);
                float denom = math.max(0.3f, math.dot(m, n1));
                float3 v = ring[i];
                v.xz = cur + m * (offset / denom);
                m_BeltRing[i] = v;
            }
            m_BeltCount = n;
        }

        /// <summary>Grows without keeping the contents (the caller overwrites them)</summary>
        private static void EnsureCapacity(ref float3[] array, int count)
        {
            if (array.Length < count)
                array = new float3[math.max(count, array.Length * 2)];
        }

        /// <summary>Grows keeping the contents (incremental fills)</summary>
        private static void GrowKeep(ref float3[] array, int count)
        {
            if (array.Length >= count)
                return;
            var grown = new float3[math.max(count, array.Length * 2)];
            Array.Copy(array, grown, array.Length);
            array = grown;
        }

        private static void GrowKeep(ref int[] array, int count)
        {
            if (array.Length >= count)
                return;
            var grown = new int[math.max(count, array.Length * 2)];
            Array.Copy(array, grown, array.Length);
            array = grown;
        }


        private void ResolvePrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;
            // The sign set is not resolved here: every bound / legacy test goes through SkiRunOwnershipSystem
            ResolveSet(RunSurfaceNames, "SurfacePrefab", m_RunPrefabs);
        }

        /// <summary>Fast PrefabID path + name-scan fallback, tolerating missing prefabs (pre-import states must not throw)</summary>
        private void ResolveSet(string[] names, string typeName, HashSet<Entity> set)
        {
            foreach (string name in names)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID(typeName, name), out PrefabBase prefabBase))
                    m_PrefabSystem.TryGetEntity(prefabBase, out prefabEntity);

                if (prefabEntity == Entity.Null)
                {
                    var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                            {
                                prefabEntity = candidates[i];
                                break;
                            }
                        }
                    }
                    finally
                    {
                        candidates.Dispose();
                    }
                }

                if (prefabEntity == Entity.Null)
                {
                    Mod.Log.Info($"Glade: prefab '{name}' not found (tolerated)");
                    continue;
                }
                set.Add(prefabEntity);
            }
        }

        /// <summary>Species pool: PrefabID("StaticObjectPrefab", name), kept only if prefab.isBuiltin (PrefabBase.cs:36-46, never a
        /// mod's look-alike) and the entity has TreeData. Never a placeholder (TreeWildRandom01): nothing resolves it mid-game
        /// (PlaceholderSystem.cs:276 is NewGame-only) and it has no Tree component.</summary>
        private void ResolveSpecies()
        {
            m_SpeciesEntities.Clear();
            m_SpeciesOffsetY.Clear();
            var text = new StringBuilder(96);
            foreach (string name in SpeciesNames)
            {
                if (!m_PrefabSystem.TryGetPrefab(new PrefabID("StaticObjectPrefab", name), out PrefabBase prefab) ||
                    prefab == null)
                {
                    Mod.Log.Info($"Glade: species '{name}' not found (tolerated)");
                    continue;
                }
                if (!prefab.isBuiltin)
                {
                    Mod.Log.Info($"Glade: species '{name}' is not built-in — skipped");
                    continue;
                }
                if (!m_PrefabSystem.TryGetEntity(prefab, out Entity entity) || entity == Entity.Null)
                {
                    Mod.Log.Info($"Glade: species '{name}' has no prefab entity — skipped");
                    continue;
                }
                if (!EntityManager.HasComponent<TreeData>(entity))
                {
                    Mod.Log.Info($"Glade: species '{name}' has no TreeData — skipped");
                    continue;
                }
                float offsetY = EntityManager.TryGetComponent(entity, out PlaceableObjectData placeable)
                    ? placeable.m_PlacementOffset.y
                    : 0f;
                m_SpeciesEntities.Add(entity);
                m_SpeciesOffsetY.Add(offsetY);
                if (text.Length > 0) text.Append(", ");
                text.Append(name);
                if (offsetY != 0f) text.Append(" (+").Append(offsetY.ToString("F2")).Append(" m)");
            }
            m_SpeciesPoolText = text.Length > 0 ? text.ToString() : "(none)";
            Mod.Log.Info($"Glade: species pool [{m_SpeciesPoolText}] — index 0 dominant " +
                         $"({Tuning.DominantShare:P0})");
        }


        private void HandleException(Exception e, string where)
        {
            m_Disabled = true;
            LastError = $"{where}: {e.GetType().Name}: {e.Message}";
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiGladeToolSystem)} EXCEPTION in {where} — glade tool " +
                              $"disabled until the next game load:\n{e}");
            }
        }

        /// <summary>The stop line: GladeHoverLog.Summary returns "tool stopped after ..."; the tool prefixes "Glade: " (SkiGladeHover.cs
        /// engine notes). Tolerant of either body shape.</summary>
        private static string StopLine(string body)
        {
            if (string.IsNullOrEmpty(body))
                return "Glade: tool stopped";
            if (body.StartsWith("Glade:", StringComparison.Ordinal))
                return body;
            if (body.StartsWith("tool stopped", StringComparison.Ordinal))
                return "Glade: " + body;
            return "Glade: tool stopped — " + body;
        }

        /// <summary>"last: STATE" of the stop line (built once, at stop)</summary>
        private string LastStateText()
        {
            switch (m_HoverKind)
            {
                case GladeHoverKind.Bound:
                    return $"bound run {EntityText(m_PlannedRun)} {TargetText(m_PlannedTreeLine)} L{m_PlannedLevel}, {m_PlanState}";
                case GladeHoverKind.None:
                    return "none";
                default:
                    return m_HoverKind.ToString();
            }
        }

        private string PrefabNameOf(Entity e)
        {
            if (e != Entity.Null && EntityManager.Exists(e) && EntityManager.TryGetComponent(e, out PrefabRef pr))
                return m_PrefabSystem.GetPrefabName(pr.m_Prefab);
            return "?";
        }

        private string HitText(Entity hit)
        {
            if (hit == Entity.Null)
                return "nothing";
            if (!EntityManager.HasComponent<Area>(hit))
                return "terrain";
            return $"surface {PrefabNameOf(hit)} {EntityText(hit)}";
        }

        /// <summary>Log texts: "Interior" in the plan line, "Tree line" in the hotkey line ("Glade: target -> Tree line"); one helper,
        /// both spellings readable</summary>
        private static string TargetText(bool treeLine)
        {
            return treeLine ? "Tree line" : "Interior";
        }

        private static string LayerText(byte layer)
        {
            switch (layer)
            {
                case SkiGladeCompose.LayerGlade: return "glade";
                case SkiGladeCompose.LayerBelt: return "belt";
                case SkiGladeCompose.LayerNone: return "none";
                default: return layer.ToString();
            }
        }

        private static string DistanceText(float d)
        {
            if (float.IsPositiveInfinity(d))
                return "none";
            if (!(d >= 0f))
                return "-";
            return d < 1000f ? $"{d:F0} m" : $"{d / 1000f:F1} km";
        }

        private static string ColorText(UnityEngine.Color c)
        {
            return $"({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
        }

        private static string EntityText(Entity e)
        {
            return e == Entity.Null ? "none" : $"{e.Index}:{e.Version}";
        }
    }
}
