// Quick runs: the pure planner. Given a height snapshot, a player region, an obstacle model and knobs (Mix, Density, allowed kinds,
// seed) it proposes ski run outlines, each labeled with the band OUR rater gives it (SkiRunRater.Rate on the outline itself, the call
// the catalog makes after build), its kind, the junction node pairs that make the runs link (LiftServedRule.LinkMeters), up to three
// top clusters and the refusal / warning state. It never touches entities: the tool (wave 2) owns the snapshot copy, the obstacle
// collection, the overlay, emission and undo.
// Entities-free on purpose: usings are System, System.Collections.Generic and Unity.Mathematics ONLY, so the same files compile into
// the net48 mod (default **/*.cs glob) AND link into the net8 headless host tests/AutoGenBatch.
// Contract (the SkiSlopeClassifier one): no static mutable state, never logs, never throws on bad input (a status instead), buffers
// grow and are reused across scans, output is a pure function of (snapshot, region, obstacles, knobs) within a runtime.
// Time slicing: Begin does the cheap refusals; Step(budgetMs) runs bounded work units (one slope-field tile, a row chunk, a heap
// chunk, one phase of one candidate) until the budget is spent, so no single call is a whole-region pass (skeptic finding 2). The
// rating runs as SkiRunRater.Rate's own steps in separate units (Plan, Need heights in row chunks, Classify), each under 0.5 ms
// CoreCLR on the alpine fixtures.
// Files: SkiRunAutoPlanner.cs (API, stages 0-4), .Select.cs (stages 5-9), .Kernels.cs (field kernels, geometry), .Snapshot.cs.
// Engine notes: EngineNotes/SkiRunAutoPlanner.md; research: docs/research/autogen-2026-10-25/.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The refusal ladder, checked in this order. Running = Step has work left. Ok = at least one run.</summary>
    public enum AutoPlanStatus : byte
    {
        Running,
        Ok,
        BadInput,   // null / non-finite input, under 3 region points, a snapshot that does not cover the scan rectangle
        TooLarge,   // region area over MaxAreaM2, or its scan rectangle over MaxLatticeCells
        TooSmall,   // region under MinAreaM2, or no open spot MinHalfWidthM from every edge
        Blocked,    // under BlockedShare of the region is open (other surfaces, unowned tiles, off the map)
        NoVertical, // under MinVerticalM between the highest and lowest open ground
        AllCliff,   // over AllCliffShare of the open ground is cliff, or no usable ground reaches the base without cliff (minimax),
                    // or no route keeps any pass's band from MinDropM above its sources (review F11)
        TooFlat,    // under TooFlatShare of the usable ground is steeper than the green floor
        NoRuns,     // the field was fine but no candidate survived (Rejects says why)
        TooNarrow,  // no usable ground EdgeSeedMarginM inside the region and clear of obstacles: no seed (checked after TooSmall, and
                    // again after the routes for the ground they reach; review F2: it used to end as NoRuns with no reject)
    }

    /// <summary>Preview warnings (the planner's share; the tool adds money, sign, renumbering and game-check warnings).</summary>
    [Flags]
    public enum AutoPlanWarning : ushort
    {
        None = 0,
        FewRuns = 1,          // fewer than 2 runs
        NoGreen = 2,          // the mix asked for a band the terrain did not give
        NoBlue = 4,
        NoBlack = 8,
        NoEasyWayHome = 16,   // a top cluster with runs but no green from it
        OverWater = 32,       // a run crosses water (a wanted detail: warning, never a refusal)
        NoFlatRunout = 64,    // no base ground gentler than RunoutGrade
        MixShort = 128,       // fewer runs of some band than the mix target
        CrossesCatTrack = 256,// a run crosses a cat track (legal, informational)
        PartlyBlocked = 512,  // under PartlyBlockedShare of the region is open
    }

    /// <summary>Why a candidate did not become a run (histogram index in AutoPlanResult.Rejects).</summary>
    public enum AutoReject : byte
    {
        Kept,
        Duplicate,   // same centerline as an earlier candidate
        FlatLine,    // the centerline never leaves the Flat band
        TopTaken,    // the seed lies in an accepted run's gap
        TooShort,    // under MinLengthM / MinDropM after a merge truncation
        Pinched,     // over PinchShare of the corridor narrower than MinWidthM
        Crosses,     // the outline self-intersects or edges come closer than GradeRibbon.MinClearanceM
        Region,      // the outline leaves the region or the map
        Obstacle,    // the outline violates an obstacle's clearance
        Overlap,     // the outline comes too close to an accepted run
        Unrated,     // the rater returned no rating or a broken mask
        Flat,        // rated Flat
        RatingCap,   // MaxRatings reached
        MergeStub,   // a merged line keeps under MergeKeepShare of its free line and under MergeKeepM (review F12: was TooShort)
        Count,
    }

    /// <summary>Mix presets by run count (owner ruling: Balanced 28 / 42 / 27 / 3 green / blue / black / double black).</summary>
    public enum AutoMix : byte
    {
        Balanced,
        Beginner,
        Expert,
        Terrain,   // "As the terrain allows": no targets, the score decides
    }

    public enum AutoDensity : byte
    {
        Sparse,
        Normal,
        Dense,
    }

    /// <summary>Kinds the player allows (default groomed + natural; night never by default).</summary>
    [Flags]
    public enum AutoKinds : byte
    {
        None = 0,
        Groomed = 1,
        Natural = 2,
        Snowmaking = 4,
        Night = 8,
        Default = Groomed | Natural,
    }

    /// <summary>Obstacle classes (owner rulings, BUILD-BRIEF 1): every existing Surface area and unowned map tile blocks; cat tracks
    /// are a soft cost runs may cross. Roads, lots, lift stations and water are NOT obstacles (no class for them on purpose).</summary>
    public enum AutoObstacleKind : byte
    {
        Surface,      // any Surface area of any origin: painted surfaces, building sub-surfaces, surface packs, park mods
        ExistingRun,  // one of our runs (Surface too, but kept a tree island apart and eligible for junction links)
        Unowned,      // a map tile the city does not own (ExceedsCityLimits)
        CatTrack,     // soft: crossing costs, never blocks (ValidationHelpers.cs:529-532)
    }

    public sealed class AutoObstacle
    {
        public AutoObstacleKind Kind;
        public float2[] Ring;   // xz, closed implicitly
    }

    public struct AutoPlanKnobs
    {
        public AutoMix Mix;
        public AutoDensity Density;
        public AutoKinds Kinds;
        /// <summary>Run cap; 0 = the density's default (8 / 12 / 16 per km2, at least 1 km2), clamped to HardRunCap.</summary>
        public int RunCap;
        /// <summary>Layout seed (SkiRunAutoPlanner.SeedOf(region, shuffle)).</summary>
        public uint Seed;

        public static AutoPlanKnobs Default(uint seed) =>
            new AutoPlanKnobs { Mix = AutoMix.Balanced, Density = AutoDensity.Normal, Kinds = AutoKinds.Default, RunCap = 0, Seed = seed };
    }

    public sealed class AutoPlanInput
    {
        public HeightSnapshot Snapshot;
        public float2[] Region;
        public List<AutoObstacle> Obstacles;
        public AutoPlanKnobs Knobs;
        /// <summary>Owner ruling 9 km2 (3 x 3 km, DECISIONS 419; was 1 km2 in v1).</summary>
        public float MaxAreaM2 = SkiRunAutoPlanner.V1MaxAreaM2;
        public float MinAreaM2 = SkiRunAutoPlanner.DefaultMinAreaM2;
        public float MinVerticalM = SkiRunAutoPlanner.DefaultMinVerticalM;
    }

    /// <summary>One proposed run. Ring = the outline to emit (xz, no closing duplicate, junction nodes included: never simplify it
    /// again or the node pairs are lost). Nodes = Ring with snapshot heights.</summary>
    public sealed class AutoRun
    {
        public int Id;                 // stable per candidate (cluster, pass, seed ordinal): toggling one run never renumbers others
        public int Cluster;            // index into Clusters
        public SlopeBand Band;         // SkiRunRater's band of Ring (the label the game will show)
        public SlopeBand PassBand;     // the routing pass that found it
        public RunKind Kind;
        public float2[] Ring;
        public float3[] Nodes;
        public float2[] Centerline;    // top to bottom
        public float RatingGrade, MeanGrade, SteepestGrade, DropM, AreaM2, LengthM, TopY, BottomY;
        public bool Merge;             // ends at another run (trail merge) instead of the base
        public int MergeInto = -1;     // Runs index of that run
        public bool TouchesBase, OverWater, CrossesCatTrack;
        public double Score;
    }

    /// <summary>A node pair that links two runs (both nodes within LinkMeters). Existing = RunB is an index into the input's
    /// Obstacles (an ExistingRun) and NodeB its ring vertex.</summary>
    public struct AutoJunction
    {
        public int RunA, NodeA, RunB, NodeB;
        public float DistanceM;
        public bool Merge, Existing;
    }

    /// <summary>A top cluster: "one lift here serves N runs". Linked = runs of this cluster in the largest junction-linked group.
    /// Pod = not a summit pick but the highest ground a pass keeps its band from (beginner pods on the gentle lower slope for green,
    /// mid-stations for blue, black only when no summit keeps the black band); PodBand is that pass's band.</summary>
    public struct AutoCluster
    {
        public float2 Center;
        public float TopY;
        public int Runs, Linked;
        public bool HasGreen, Pod;
        public SlopeBand PodBand;
    }

    public sealed class AutoPlanResult
    {
        public AutoPlanStatus Status;
        public AutoPlanWarning Warnings;
        public readonly List<AutoRun> Runs = new List<AutoRun>();
        public readonly List<AutoJunction> Junctions = new List<AutoJunction>();
        public readonly List<AutoCluster> Clusters = new List<AutoCluster>();
        public readonly int[] BandCount = new int[5], BandTarget = new int[5];
        public readonly int[] Rejects = new int[(int)AutoReject.Count];
        public float RegionAreaM2, OpenShare, UsableShare, CliffShare, SteepShare, VerticalM;
        /// <summary>Mean terrain grade (rise over run, 0.25 = 25 %) over the open cells, each capped at SkiRunAutoPlanner.MeanGradeCap; the
        /// site verdict (QuickRunsRules.SiteTierOf) reads it. 0 with no open cell (OpenShare 0). Set before the refusal ladder, so refused scans have it.</summary>
        public float MeanGrade;
        public int Candidates, Rated;
        public readonly float[] StageMs = new float[SkiRunAutoPlanner.StageCount];
        public readonly float[] StageMaxMs = new float[SkiRunAutoPlanner.StageCount];   // longest unit per stage (the probe names the slow stage)
        public float MaxUnitMs;
        /// <summary>Longest stage 13 (Select) unit per phase: pick / A* (reroute) / build / rate / chamfer (the probe names the slow phase).</summary>
        public readonly float[] SelectPhaseMaxMs = new float[SkiRunAutoPlanner.SelectPhases];
        public int Units;
        public ulong Fingerprint;

        /// <summary>The non-zero reject counts as "Name count, ..." ("none" when empty); log text, not player text.</summary>
        public string RejectText()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i < (int)AutoReject.Count; i++)
                if (Rejects[i] > 0)
                    sb.Append(sb.Length > 0 ? ", " : "").Append((AutoReject)i).Append(' ').Append(Rejects[i]);
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        /// <summary>SelectPhaseMaxMs as "a/b/c/d/e" (pick/A*/build/rate/chamfer, 2 decimals, invariant culture).</summary>
        public string SelectPhaseText()
        {
            var parts = new string[SelectPhaseMaxMs.Length];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = SelectPhaseMaxMs[i].ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            return string.Join("/", parts);
        }

        public void Clear()
        {
            Status = AutoPlanStatus.Running;
            Warnings = AutoPlanWarning.None;
            Runs.Clear();
            Junctions.Clear();
            Clusters.Clear();
            Array.Clear(BandCount, 0, BandCount.Length);
            Array.Clear(BandTarget, 0, BandTarget.Length);
            Array.Clear(Rejects, 0, Rejects.Length);
            RegionAreaM2 = OpenShare = UsableShare = CliffShare = SteepShare = VerticalM = MeanGrade = 0f;
            Candidates = Rated = 0;
            Array.Clear(StageMs, 0, StageMs.Length);
            Array.Clear(StageMaxMs, 0, StageMaxMs.Length);
            Array.Clear(SelectPhaseMaxMs, 0, SelectPhaseMaxMs.Length);
            MaxUnitMs = 0f;
            Units = 0;
            Fingerprint = 0;
        }
    }

    public sealed partial class SkiRunAutoPlanner
    {
        // ---------------------------------------------------------------- constants (sources: design.md 3.2, FEASIBILITY 4/9)
        public const float V1MaxAreaM2 = 9.0e6f, ReadyMaxAreaM2 = 9.0e6f;      // owner ruling 9 km2 (DECISIONS 419); the code is tested to it
        public const float DefaultMinAreaM2 = 40000f, DefaultMinVerticalM = 50f;
        public const int MaxLatticeCells = 1 << 21;                              // scan rectangle cap (5 x 5 km at 3.5 m; seed keys pack 21 bits)
        public const float LatticeMarginM = 40f;                                 // 20 m windows + Dilation + bilinear reach
        public const int OffMapMarginTexels = SkiSlopeClassifier.Dilation + 2;   // keeps every rating Need node on the map
        public const float BlockedShare = 0.30f, PartlyBlockedShare = 0.60f, TooFlatShare = 0.20f, AllCliffShare = 0.60f;
        public const float CliffGrade = 1.0f;                                    // 45 deg
        public const float MeanGradeCap = 2.0f;                                  // one cell counts at most 200 % in the region mean (a sheer face must not drown the rest)
        public const float MinHalfWidthM = 7.5f;                                 // half the 15 m pinch width
        // Routing passes G / B / K / DB. Each pass has a band [floor, ceiling) and a HARD ceiling = the next pass's ceiling (the double
        // black's is the cliff grade): a step is impossible at or over the hard ceiling, cells between the ceiling and the hard ceiling
        // cost OverBandFactor (the rater judges a run by its crux percentiles, not by every cell, so one steeper cell must not end a
        // green or pinch its corridor), in-band cells cost InBandFactor (pulls blacks onto steep). The double black ceiling is 80 %
        // (expert max, real-world-layout.md 1), not the classifier's open band.
        public static readonly float[] PassFloor = { SkiSlopeClassifier.GradeGreen, SkiSlopeClassifier.GradeBlue, SkiSlopeClassifier.GradeBlack, SkiSlopeClassifier.GradeDoubleBlack };
        public static readonly float[] PassCeiling = { SkiSlopeClassifier.GradeBlue, SkiSlopeClassifier.GradeBlack, SkiSlopeClassifier.GradeDoubleBlack, 0.80f };
        public static readonly float[] PassHardCeiling = { SkiSlopeClassifier.GradeBlack, SkiSlopeClassifier.GradeDoubleBlack, 0.80f, CliffGrade };
        public static readonly float[] TargetWidthM = { 40f, 35f, 30f, 20f };
        public const int Passes = 4;
        public const float InBandFactor = 0.4f, OverBandFactor = 3.0f, SidehillCost = 2f, FlatCost = 3f, GullyCost = 0.5f, GullyAreaM2 = 20000f;
        public const float MaxClimbM = 0.5f, CatTrackCost = 1f;
        // A cell is IN BAND for a pass when its route to the base never crosses the pass ceiling. The rater's crux is a minimax (the
        // easiest line's steepest cell between the end zones, SkiSlopeClassifier.cs:1), not a percentile: a P90 allowance labeled 58-85
        // of ~280 blue-pass runs black on the alpine fixtures, the strict rule 9. Only in-band cells seed runs and pods; the hard ceiling
        // keeps the corridor wide and lets routes pass (their upslope cells then are not in band).
        // Green and blue routes end in the lower PassSourceShare of the base band (beginner and cruiser runs ski down into the base
        // area; the gentle ground just above the base is too short for a green), blacks and double blacks at its top.
        public static readonly float[] PassSourceShare = { 0.5f, 0.5f, 1f, 1f };
        // Routes and seeds keep off the player's polygon edge: seeds need DHard >= EdgeSeedMarginM (half the widest target width plus a
        // gap), route steps closer cost EdgeCostFactor; routes never enter, and the corridor march stops at, EdgeSeedMarginM / 2 from
        // the region edge (DEdge: the region and map edge only, so an obstacle's clearance is not widened)
        public const float EdgeSeedMarginM = 35f, EdgeCostFactor = 1.5f;
        public const float TopBandM = 30f, ClusterRadiusM = 150f, ClusterSpacingM = 300f;
        public const int MaxClusters = 3, SeedsPerClusterPass = 24;   // MaxClusters summits per km2 of side (ClusterCap)
        public const float SeedJitterM = 10f, SummitJitterM = 20f, ScoreJitter = 0.4f, TopShare = 0.25f;   // Shuffle: seeded per seed
        public const float RerouteShare = 0.7f, DetourFactor = 1.5f, MergePenalty = 1f;   // reroute when a merge keeps under 70 % of the line
        // H: 8192 pops a unit measured up to 1.15 ms CoreCLR. 48 k pops in all made the A* 60-90 % of Select once the seeds spread (9 km2
        // fBm: 443 ms); 12 k halves it at the same run counts on the alpine fixtures
        private const int RerouteMaxPops = 12000, RerouteUnitPops = 4096;
        public const float BaseShare = 0.15f, BaseMinM = 10f, BaseMaxM = 60f, RunoutGrade = 0.12f;
        public const float MinWidthM = 15f, PinchShare = 0.30f, MinLengthM = 250f, MinDropM = 30f, MaxLengthM = 2500f;
        public const float MergeKeepShare = 0.4f, MergeKeepM = 400f;             // a merged line keeps 40 % of its free line or 400 m
        public const float MaxRunAreaM2 = 200000f;                               // 20 ha; and StepMultiple must stay 1
        public const float SampleM = 4f, DecimateM = 25f, RingSimplifyM = 1.0f, WidthMarginM = 1f, MarchStepM = 1f;
        public const float SparseGapM = 30f, NormalGapM = 15f, DenseGapM = 10f, RasterSlackM = 1f;
        public const float ExactGapShare = 0.8f;                                 // exact distance >= 0.8 gap (raster approximations)
        public const float MergeGapM = 2f, MergeTaperM = 30f;                    // abutting is legal (ValidationHelpers.cs:575)
        public const float JunctionReachM = 20f;                                 // node pair search; +2 x 2 m snap stays <= LinkMeters
        public const float LinkMeters = LiftServedRule.LinkMeters;               // the link rule's own constant (one source)
        public const float SurfaceClearanceM = MergeGapM, UnownedClearanceM = 0.5f, RegionClearanceM = 0.25f;
        public const int HardRunCap = 96, MaxRatings = 48, RatingsPerRun = 3, TileCoreCells = 80;   // caps scale with AreaScale
        private const int MinimaxPops = 32768, RoutePops = 8192;                // heap work per unit (Mono: about 4-6 ms each)
        // Mix targets by run count, G / B / K / DB (design 3.2; Balanced is the owner's ruling)
        private static readonly float[][] MixShare =
        {
            new[] { 0.28f, 0.42f, 0.27f, 0.03f },
            new[] { 0.50f, 0.40f, 0.10f, 0.00f },
            new[] { 0.15f, 0.35f, 0.40f, 0.10f },
            new[] { 0f, 0f, 0f, 0f },
        };

        // ---------------------------------------------------------------- stages
        public const int StageCount = 16;
        public const int SelectPhases = 5;   // stage 13's m_SelPhase as probe slots: pick, A* (reroute), build, rate, chamfer (4 and 5)
        private enum Stage
        {
            Heights, Obstacles, Field, DistForward, DistBackward, FlowSort, FlowAccum, Usable, Zones, Minimax, Route,
            Seeds, Candidates, Select, Finish, Done,
        }

        // Cell flags
        private const byte FRegion = 1, FOffMap = 2, FHard = 4, FCat = 8, FWet = 16, FOpen = 32, FCliff = 64, FBase = 128;
        private const byte PredNone = 255, PredSource = 254;
        private const ushort MUnreached = ushort.MaxValue;
        private const float Sqrt2 = 1.41421356f;
        private static readonly int[] DirX = { 1, -1, 0, 0, 1, -1, 1, -1 };
        private static readonly int[] DirZ = { 0, 0, 1, -1, 1, 1, -1, -1 };

        // ---------------------------------------------------------------- state
        private AutoPlanInput m_In;
        private readonly AutoPlanResult m_Result = new AutoPlanResult();
        private Stage m_Stage = Stage.Done;
        private int m_Cursor, m_Cursor2;
        private AutoPlanKnobs m_Knobs;

        // Lattice: cell (i, j) = texel (LI0 + i, LJ0 + j); world = Origin + texel index * Texel
        private int m_LI0, m_LJ0, m_Nx, m_Nz, m_N;
        private float m_Texel;
        private double2 m_Origin;
        private int m_RegionI0, m_RegionI1, m_RegionJ0, m_RegionJ1;   // region bbox in lattice cells (inclusive)
        private float2[] m_Region;
        private int m_RegionCells;

        private byte[] m_Flags;
        private ushort[] m_Raw;
        private float[] m_H, m_O, m_DHard, m_DEdge, m_DAcc, m_Flow;
        private ushort[] m_M;
        private readonly byte[][] m_Pred = new byte[Passes][];
        private readonly float[][] m_Dist = new float[Passes][];   // per pass: route cost to the base (the reroute's A* heuristic)
        private byte[] m_InBand;                                      // bit k: pass k's route to the base stays under its ceiling
        private byte[] m_ROver;                                       // route scratch: 1 = the line so far crosses the pass ceiling
        private float[] m_AG;                                         // reroute: A* cost from the seed (infinity when untouched)
        private byte[] m_APar;                                        // reroute: the step each reached cell came from
        private int m_FlowN;
        private bool m_Reach;
        private int[] m_Order, m_Count;
        private readonly List<int> m_Tiles = new List<int>();

        // Reused classifier buffers: field tiles, obstacle / region rasters, candidate ratings
        private readonly SlopeGrid m_Grid = new SlopeGrid();
        private readonly SlopeResult m_Slope = new SlopeResult();
        private float3[] m_Pts3 = new float3[64];

        // Field statistics
        private float m_HMinU, m_HMaxU, m_HMinOpen, m_HMaxOpen, m_BaseTop, m_HReach;
        private int m_Open, m_Usable, m_Cliff, m_Steep, m_BaseGentle;
        private double m_GradeSum;
        private float m_MaxDHard, m_MaxDHardU;
        private int m_RouteReach, m_RouteSeedable, m_RouteSources;

        private readonly MinimaxQueue m_Minimax = new MinimaxQueue();
        private readonly DijkstraQueue m_Heap = new DijkstraQueue();
        private int m_Pass;

        public AutoPlanResult Result => m_Result;
        public bool Done => m_Stage == Stage.Done;

        /// <summary>0..1 by stage (for the "Scanning the slope... 40 %" line).</summary>
        public float Progress
        {
            get
            {
                if (m_Stage == Stage.Done)
                    return 1f;
                float s = (float)m_Stage / (float)Stage.Done;
                if (m_Stage == Stage.Field && m_Tiles.Count > 0)
                    s += (float)m_Cursor / m_Tiles.Count / (float)Stage.Done;
                return math.saturate(s);
            }
        }

        // ---------------------------------------------------------------- public entry points

        /// <summary>Default seed: FNV-1a over the region's node bits, starting at the lexicographically smallest node and walking
        /// counterclockwise, so the start vertex and the drawing direction never change the layout; Shuffle adds a counter.</summary>
        public static uint SeedOf(float2[] region, uint shuffle)
        {
            uint h = 2166136261u;
            if (region == null || region.Length == 0)
                return (h ^ shuffle) | 1u;
            int n = region.Length, start = 0;
            for (int i = 1; i < n; i++)
                if (region[i].x < region[start].x || (region[i].x == region[start].x && region[i].y < region[start].y))
                    start = i;
            double s = 0.0;
            for (int i = 0, k = n - 1; i < n; k = i++)
                s += (double)region[k].x * region[i].y - (double)region[i].x * region[k].y;
            int dir = s >= 0.0 ? 1 : -1;
            for (int c = 0; c < n; c++)
            {
                float2 p = region[((start + dir * c) % n + n) % n];
                h = Fnv(h, FloatBits(p.x));
                h = Fnv(h, FloatBits(p.y));
            }
            h = Fnv(h, shuffle);
            return h | 1u;
        }

        /// <summary>The texel rectangle the tool must copy into the snapshot for this region (inclusive bounds, clamped to the map).
        /// False for a degenerate region.</summary>
        public static bool SnapshotRect(float2[] region, int resX, int resZ, float3 scale, float3 offset,
            out int x0, out int z0, out int width, out int depth)
        {
            x0 = z0 = width = depth = 0;
            if (!BBox(region, out float2 lo, out float2 hi) || !(scale.x > 0f))
                return false;
            float texel = 1f / scale.x;
            int pad = (int)math.ceil(LatticeMarginM / texel) + 3;
            long k0 = (long)math.floor((lo.x + offset.x) * scale.x) - pad, k1 = (long)math.ceil((hi.x + offset.x) * scale.x) + pad;
            long l0 = (long)math.floor((lo.y + offset.z) * scale.z) - pad, l1 = (long)math.ceil((hi.y + offset.z) * scale.z) + pad;
            k0 = Math.Max(0L, Math.Min(resX - 1, k0));
            k1 = Math.Max(0L, Math.Min(resX - 1, k1));
            l0 = Math.Max(0L, Math.Min(resZ - 1, l0));
            l1 = Math.Max(0L, Math.Min(resZ - 1, l1));
            x0 = (int)k0;
            z0 = (int)l0;
            width = (int)(k1 - k0 + 1);
            depth = (int)(l1 - l0 + 1);
            return true;
        }

        /// <summary>Starts a scan. Cheap refusals (BadInput, TooLarge, TooSmall by area) are final here; otherwise Status is Running
        /// and Step does the rest. The input object is held until the next Begin (the tool must not mutate it meanwhile).</summary>
        public AutoPlanStatus Begin(AutoPlanInput input)
        {
            m_Result.Clear();
            m_In = input;
            m_Stage = Stage.Done;
            ClearCandidates();
            AutoPlanStatus s = Validate(input);
            if (s != AutoPlanStatus.Running)
                return Finish(s);
            m_Knobs = input.Knobs;
            m_Stage = Stage.Heights;
            m_Cursor = m_Cursor2 = 0;
            return AutoPlanStatus.Running;
        }

        /// <summary>Runs work units until budgetMs is spent (at least one unit per call). True when done.</summary>
        public bool Step(float budgetMs)
        {
            if (m_Stage == Stage.Done)
                return true;
            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            do
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                int stage = (int)m_Stage;
                // Read before the unit: the phase this unit runs (the reset rows at m_Cursor 0 count as pick)
                int phase = m_Cursor == 0 ? 0 : Math.Min(m_SelPhase, SelectPhases - 1);
                Unit();
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                float ms = (float)((t1 - t0) * toMs);
                m_Result.StageMs[stage] += ms;
                if (ms > m_Result.StageMaxMs[stage])
                    m_Result.StageMaxMs[stage] = ms;
                if (stage == (int)Stage.Select && ms > m_Result.SelectPhaseMaxMs[phase])
                    m_Result.SelectPhaseMaxMs[phase] = ms;
                m_Result.Units++;
                if (ms > m_Result.MaxUnitMs)
                    m_Result.MaxUnitMs = ms;
                if (m_Stage == Stage.Done)
                    return true;
            } while ((System.Diagnostics.Stopwatch.GetTimestamp() - start) * toMs < budgetMs);
            return false;
        }

        /// <summary>Runs the whole scan in one call (tests, or a worker thread).</summary>
        public AutoPlanResult Run(AutoPlanInput input)
        {
            if (Begin(input) == AutoPlanStatus.Running)
                while (!Step(float.PositiveInfinity))
                {
                }
            return m_Result;
        }

        /// <summary>Re-runs only what the knobs change on the cached field: a new seed re-seeds (stage Seeds), Mix / Density / cap
        /// re-select, Kinds only re-assigns kinds. False (nothing to do) when no field is cached (a refusal before the routing).</summary>
        public bool Reselect(AutoPlanKnobs knobs)
        {
            if (!m_FieldReady || m_In == null)
                return false;
            Stage restart;
            if (knobs.Density != m_Knobs.Density && HasExistingRuns())
                restart = Stage.Heights;   // an existing run's clearance is the density's gap, rastered in stage 0
            else if (knobs.Seed != m_Knobs.Seed)
                restart = Stage.Seeds;
            else if (knobs.Mix != m_Knobs.Mix || knobs.Density != m_Knobs.Density || knobs.RunCap != m_Knobs.RunCap)
                restart = Stage.Select;
            else
                restart = Stage.Finish;
            m_Knobs = knobs;
            if (restart == Stage.Heights)
            {
                // A copy: the tool's input object keeps its own knobs
                Begin(new AutoPlanInput
                {
                    Snapshot = m_In.Snapshot, Region = m_In.Region, Obstacles = m_In.Obstacles, Knobs = knobs, MaxAreaM2 = m_In.MaxAreaM2,
                    MinAreaM2 = m_In.MinAreaM2, MinVerticalM = m_In.MinVerticalM,
                });
                return true;
            }
            // The probe counters are per scan: a re-select reports only its own stages (they used to add up across Shuffles)
            Array.Clear(m_Result.StageMs, 0, m_Result.StageMs.Length);
            Array.Clear(m_Result.StageMaxMs, 0, m_Result.StageMaxMs.Length);
            Array.Clear(m_Result.SelectPhaseMaxMs, 0, m_Result.SelectPhaseMaxMs.Length);
            m_Result.Units = 0;
            m_Result.MaxUnitMs = 0f;
            ResetForStage(restart);
            m_Stage = restart;
            m_Cursor = m_Cursor2 = 0;
            return true;
        }

        private bool HasExistingRuns()
        {
            if (m_In.Obstacles == null)
                return false;
            for (int i = 0; i < m_In.Obstacles.Count; i++)
                if (m_In.Obstacles[i] != null && m_In.Obstacles[i].Kind == AutoObstacleKind.ExistingRun)
                    return true;
            return false;
        }

        /// <summary>Easiest band reachable from the cell under a world point down to the base (the preview heat map); false outside
        /// the usable ground or before the field is ready.</summary>
        public bool EasiestBand(float2 world, out SlopeBand band)
        {
            band = SlopeBand.Flat;
            if (!m_FieldReady || !Cell(world, out int c) || m_M[c] == MUnreached)
                return false;
            band = SkiSlopeClassifier.BandOf((m_M[c] + 0.5f) / 1024f);
            return true;
        }

        // ---------------------------------------------------------------- validation and lattice

        private AutoPlanStatus Validate(AutoPlanInput input)
        {
            if (input == null || input.Snapshot == null || !input.Snapshot.Valid || input.Region == null || input.Region.Length < 3)
                return AutoPlanStatus.BadInput;
            HeightSnapshot snap = input.Snapshot;
            for (int i = 0; i < input.Region.Length; i++)
                if (!Finite(input.Region[i].x) || !Finite(input.Region[i].y))
                    return AutoPlanStatus.BadInput;
            if (!Finite(input.MaxAreaM2) || !Finite(input.MinAreaM2) || !Finite(input.MinVerticalM))
                return AutoPlanStatus.BadInput;
            double area = PolygonArea(input.Region);
            m_Result.RegionAreaM2 = (float)area;
            if (area > input.MaxAreaM2)
                return AutoPlanStatus.TooLarge;
            if (area < input.MinAreaM2 || area < 1.0)
                return AutoPlanStatus.TooSmall;

            m_Texel = snap.TexelM;
            m_Origin = snap.Origin;
            BBox(input.Region, out float2 lo, out float2 hi);
            int margin = (int)math.ceil(LatticeMarginM / m_Texel) + 1;
            long i0 = (long)math.floor((lo.x - m_Origin.x) / m_Texel) - margin, i1 = (long)math.ceil((hi.x - m_Origin.x) / m_Texel) + margin;
            long j0 = (long)math.floor((lo.y - m_Origin.y) / m_Texel) - margin, j1 = (long)math.ceil((hi.y - m_Origin.y) / m_Texel) + margin;
            long nx = i1 - i0 + 1, nz = j1 - j0 + 1;
            if (nx * nz > MaxLatticeCells || Math.Abs(i0) > 1L << 24 || Math.Abs(j0) > 1L << 24)
                return AutoPlanStatus.TooLarge;
            if (!snap.CoversClamped(i0 - 1, j0 - 1, i1 + 1, j1 + 1))
                return AutoPlanStatus.BadInput;
            m_LI0 = (int)i0;
            m_LJ0 = (int)j0;
            m_Nx = (int)nx;
            m_Nz = (int)nz;
            m_N = m_Nx * m_Nz;
            m_Region = input.Region;
            Allocate();
            return AutoPlanStatus.Running;
        }

        private void Allocate()
        {
            Grow(ref m_Flags, m_N);
            Grow(ref m_Raw, m_N);
            Grow(ref m_H, m_N);
            Grow(ref m_O, m_N);
            Grow(ref m_DHard, m_N);
            Grow(ref m_DEdge, m_N);
            Grow(ref m_DAcc, m_N);
            Grow(ref m_AG, m_N);
            Grow(ref m_InBand, m_N);
            Grow(ref m_ROver, m_N);
            Grow(ref m_Flow, m_N);
            Grow(ref m_M, m_N);
            Grow(ref m_Order, m_N);
            Grow(ref m_APar, m_N);
            for (int k = 0; k < Passes; k++)
            {
                Grow(ref m_Pred[k], m_N);
                Grow(ref m_Dist[k], m_N);
            }
            Grow(ref m_Count, 65537);
            m_FieldReady = false;
        }

        private AutoPlanStatus Finish(AutoPlanStatus s)
        {
            m_Result.Status = s;
            m_Stage = Stage.Done;
            return s;
        }

        // ---------------------------------------------------------------- the stage machine

        private void Unit()
        {
            switch (m_Stage)
            {
                case Stage.Heights: UnitHeights(); break;
                case Stage.Obstacles: UnitObstacles(); break;
                case Stage.Field: UnitField(); break;
                case Stage.DistForward: UnitDist(true); break;
                case Stage.DistBackward: UnitDist(false); break;
                case Stage.FlowSort: UnitFlowSort(); break;
                case Stage.FlowAccum: UnitFlowAccum(); break;
                case Stage.Usable: UnitUsable(); break;
                case Stage.Zones: UnitZones(); break;
                case Stage.Minimax: UnitMinimax(); break;
                case Stage.Route: UnitRoute(); break;
                case Stage.Seeds: UnitSeeds(); break;
                case Stage.Candidates: UnitCandidates(); break;
                case Stage.Select: UnitSelect(); break;
                case Stage.Finish: UnitFinish(); break;
                default: m_Stage = Stage.Done; break;
            }
        }

        private void Next(Stage s)
        {
            m_Stage = s;
            m_Cursor = m_Cursor2 = 0;
        }

        private int RowsPerUnit => Math.Max(1, 65536 / Math.Max(1, m_Nx));

        // Stage 0a: the region raster (m_Cursor2 0: Plan; 1: its Mask in row chunks, P2: 9 km2), then heights and off-map flags in
        // row chunks (m_Cursor2 2)
        private void UnitHeights()
        {
            HeightSnapshot snap = m_In.Snapshot;
            if (m_Cursor2 == 0)
            {
                Array.Clear(m_Flags, 0, m_N);
                // Region raster with the classifier's own rule (cell center inside), on the world lattice
                m_RegionCells = 0;
                m_RegionI0 = m_RegionJ0 = int.MaxValue;
                m_RegionI1 = m_RegionJ1 = int.MinValue;
                m_Cursor = 0;
                m_Cursor2 = Rasterize(m_Region) > 0 ? 1 : 2;
                if (m_Cursor2 == 2)
                    Finish(AutoPlanStatus.TooSmall);
                return;
            }
            if (m_Cursor2 == 1)
            {
                int gEnd = Math.Min(m_Grid.Nz, m_Cursor + Math.Max(1, 65536 / Math.Max(1, m_Grid.Nx)));
                ForEachMaskCell(m_Cursor, gEnd, c =>
                {
                    m_Flags[c] |= FRegion;
                    m_RegionCells++;
                    int i = c % m_Nx, j = c / m_Nx;
                    m_RegionI0 = Math.Min(m_RegionI0, i);
                    m_RegionI1 = Math.Max(m_RegionI1, i);
                    m_RegionJ0 = Math.Min(m_RegionJ0, j);
                    m_RegionJ1 = Math.Max(m_RegionJ1, j);
                });
                m_Cursor = gEnd;
                if (m_Cursor < m_Grid.Nz)
                    return;
                m_Cursor = 0;
                m_Cursor2 = 2;
                if (m_RegionCells == 0)
                    Finish(AutoPlanStatus.TooSmall);
                return;
            }
            int rows = RowsPerUnit;
            int jEnd = Math.Min(m_Nz, m_Cursor + rows);
            int resX = snap.ResX, resZ = snap.ResZ;
            for (int j = m_Cursor; j < jEnd; j++)
            {
                long l = (long)m_LJ0 + j;
                for (int i = 0; i < m_Nx; i++)
                {
                    int c = j * m_Nx + i;
                    long k = (long)m_LI0 + i;
                    snap.TryRaw(k, l, out ushort raw);
                    m_Raw[c] = raw;
                    m_H[c] = snap.ToWorld(raw);
                    m_O[c] = float.NaN;
                    m_DHard[c] = float.PositiveInfinity;
                    m_DEdge[c] = float.PositiveInfinity;
                    m_DAcc[c] = float.PositiveInfinity;
                    if (k < OffMapMarginTexels || l < OffMapMarginTexels || k > resX - 1 - OffMapMarginTexels || l > resZ - 1 - OffMapMarginTexels)
                        m_Flags[c] |= FOffMap;
                    if (snap.IsWet(k, l))
                        m_Flags[c] |= FWet;
                    if ((m_Flags[c] & (FRegion | FOffMap)) != FRegion)
                        m_DHard[c] = m_DEdge[c] = -0.5f * m_Texel;
                }
            }
            m_Cursor = jEnd;
            if (m_Cursor >= m_Nz)
                Next(Stage.Obstacles);
        }

        // Stage 0b: one obstacle per unit. Hard ones seed the clearance field, cat tracks set the soft flag.
        private void UnitObstacles()
        {
            List<AutoObstacle> obs = m_In.Obstacles;
            if (obs == null || m_Cursor >= obs.Count)
            {
                BuildTiles();
                Next(Stage.Field);
                return;
            }
            AutoObstacle o = obs[m_Cursor++];
            if (o == null || o.Ring == null || o.Ring.Length < 2 || !RingFinite(o.Ring))
                return;
            float clearance = ClearanceOf(o.Kind, m_Knobs.Density);
            if (!BBox(o.Ring, out float2 lo, out float2 hi) || !OverlapsLattice(lo - clearance - 2f * m_Texel, hi + clearance + 2f * m_Texel))
                return;
            bool soft = o.Kind == AutoObstacleKind.CatTrack;
            float seed = -0.5f * m_Texel - clearance - RasterSlackM;
            if (o.Ring.Length >= 3 && Rasterize(o.Ring) > 0)
                ForEachMaskCell(c => Mark(c, soft, seed));
            // Edges too: a thin outline (a cat track, a narrow painted path) may hold no cell center
            int n = o.Ring.Length;
            for (int e = 0; e < n; e++)
            {
                float2 a = o.Ring[e], b = o.Ring[(e + 1) % n];
                if (n == 2 && e == 1)
                    break;
                int steps = Math.Max(1, (int)math.ceil(math.distance(a, b) / (0.5f * m_Texel)));
                for (int s = 0; s <= steps; s++)
                    if (Cell(math.lerp(a, b, s / (float)steps), out int c))
                        Mark(c, soft, seed);
            }
        }

        private void Mark(int c, bool soft, float seed)
        {
            if (soft)
            {
                m_Flags[c] |= FCat;
                return;
            }
            m_Flags[c] |= FHard;
            if (seed < m_DHard[c])
                m_DHard[c] = seed;
        }

        /// <summary>Clearance an obstacle keeps from a generated run: an existing run a tree island (the density's gap), any other
        /// surface only enough to never overlap (abutting is legal, ValidationHelpers.cs:575), an unowned tile a hair.</summary>
        public static float ClearanceOf(AutoObstacleKind kind, AutoDensity density)
        {
            switch (kind)
            {
                case AutoObstacleKind.ExistingRun: return GapOf(density, TargetWidthM[2]);
                case AutoObstacleKind.Unowned: return UnownedClearanceM;
                case AutoObstacleKind.CatTrack: return 0f;
                default: return SurfaceClearanceM;
            }
        }

        /// <summary>Gap between two runs (design 3.2): Sparse 30 m, Normal max(15 m, half the run width), Dense 10 m.</summary>
        public static float GapOf(AutoDensity density, float widthM)
        {
            switch (density)
            {
                case AutoDensity.Sparse: return SparseGapM;
                case AutoDensity.Dense: return DenseGapM;
                default: return Math.Max(NormalGapM, 0.5f * widthM);
            }
        }

        // Stage 1: the o field, one tile per unit (skeptic finding 2). Each tile is a rectangle of TileCoreCells cells plus an apron of
        // one 20 m window, so every core cell's window and box gradient read exactly what a single Plan over the whole rectangle
        // would: the tiled field is bit-identical to one call (AutoGenBatch asserts it).
        private void BuildTiles()
        {
            m_Tiles.Clear();
            int ti0 = m_RegionI0, tj0 = m_RegionJ0;
            for (int tj = tj0; tj <= m_RegionJ1; tj += TileCoreCells)
                for (int ti = ti0; ti <= m_RegionI1; ti += TileCoreCells)
                {
                    int i1 = Math.Min(m_RegionI1, ti + TileCoreCells - 1), j1 = Math.Min(m_RegionJ1, tj + TileCoreCells - 1);
                    bool any = false;
                    for (int j = tj; j <= j1 && !any; j++)
                        for (int i = ti; i <= i1; i++)
                            if ((m_Flags[j * m_Nx + i] & FRegion) != 0)
                            {
                                any = true;
                                break;
                            }
                    if (any)
                        m_Tiles.Add(tj * m_Nx + ti);
                }
        }

        /// <summary>Apron of a field tile in cells: one full 20 m window (the window reaches 10 m plus a rounded check node).</summary>
        public static int ApronCells(float texel) => (int)math.ceil(SkiSlopeClassifier.WindowLength / texel);

        private void UnitField()
        {
            if (m_Cursor >= m_Tiles.Count)
            {
                Next(Stage.DistForward);
                return;
            }
            int t = m_Tiles[m_Cursor++];
            int ti = t % m_Nx, tj = t / m_Nx;
            int i1 = Math.Min(m_RegionI1, ti + TileCoreCells - 1), j1 = Math.Min(m_RegionJ1, tj + TileCoreCells - 1);
            var sampler = new SnapshotSampler(m_In.Snapshot);
            if (!FieldTile(ref sampler, m_LI0 + ti, m_LJ0 + tj, m_LI0 + i1, m_LJ0 + j1, m_Grid, m_Slope))
                return;
            for (int j = tj; j <= j1; j++)
                for (int i = ti; i <= i1; i++)
                {
                    int q = (m_LJ0 + j - m_Grid.J0) * m_Grid.Nx + (m_LI0 + i - m_Grid.I0);
                    m_O[j * m_Nx + i] = m_Grid.Mask[q] != 0 ? m_Slope.CellGrade[q] : float.NaN;
                }
        }

        /// <summary>Plans and classifies the rectangle of texel indices [k0 - apron, k1 + apron] x [l0 - apron, l1 + apron] on the
        /// snapshot's world lattice; the caller reads CellGrade of the core. Public so the host can compare tiles with one call.</summary>
        public bool FieldTile<T>(ref T sampler, int k0, int l0, int k1, int l1, SlopeGrid grid, SlopeResult result)
            where T : struct, IHeightSampler
        {
            int a = ApronCells(m_Texel);
            return RectField(ref sampler, m_Origin, m_Texel, k0 - a, l0 - a, k1 + a, l1 + a, grid, result);
        }

        /// <summary>Plan + FillHeights + Classify over the cells [k0, k1] x [l0, l1] (texel indices; the rectangle's corners sit half a
        /// cell outside the end cells so the Mask is exactly that range).</summary>
        public static bool RectField<T>(ref T sampler, double2 origin, float texel, int k0, int l0, int k1, int l1, SlopeGrid grid,
            SlopeResult result) where T : struct, IHeightSampler
        {
            float x0 = (float)(origin.x + (k0 - 0.5) * texel), x1 = (float)(origin.x + (k1 + 0.5) * texel);
            float z0 = (float)(origin.y + (l0 - 0.5) * texel), z1 = (float)(origin.y + (l1 + 0.5) * texel);
            var pts = new[] { new float3(x0, 0f, z0), new float3(x1, 0f, z0), new float3(x1, 0f, z1), new float3(x0, 0f, z1) };
            grid.DiagonalRule = false;
            SlopeStatus ps = SkiSlopeClassifier.Plan(pts, 4, origin, texel, grid);
            if (ps != SlopeStatus.Ok || grid.PolylineMode)
                return false;
            SkiRunRater.FillHeights(ref sampler, grid, null, out int _, out int _);
            SlopeStatus cs = SkiSlopeClassifier.Classify(grid, result);
            return cs == SlopeStatus.Ok || cs == SlopeStatus.ShortRun;
        }

        // Clearance fields: two chamfer passes in row chunks, DHard (every hard cell, non-region and off-map cell) and DEdge (the
        // region edge and the map edge only: the corridor's edge margin, which must not widen an obstacle's own clearance)
        private void UnitDist(bool forward)
        {
            int rows = Math.Max(1, RowsPerUnit / 2);
            if (forward)
            {
                int jEnd = Math.Min(m_Nz, m_Cursor + rows);
                Chamfer(m_DHard, m_Nx, 0, m_Nx - 1, 0, m_Nz - 1, m_Cursor, jEnd - 1, true, m_Texel);
                Chamfer(m_DEdge, m_Nx, 0, m_Nx - 1, 0, m_Nz - 1, m_Cursor, jEnd - 1, true, m_Texel);
                m_Cursor = jEnd;
                if (m_Cursor >= m_Nz)
                    Next(Stage.DistBackward);
            }
            else
            {
                int jHi = m_Nz - 1 - m_Cursor, jLo = Math.Max(0, jHi - rows + 1);
                Chamfer(m_DHard, m_Nx, 0, m_Nx - 1, 0, m_Nz - 1, jLo, jHi, false, m_Texel);
                Chamfer(m_DEdge, m_Nx, 0, m_Nx - 1, 0, m_Nz - 1, jLo, jHi, false, m_Texel);
                m_Cursor += jHi - jLo + 1;
                if (m_Cursor >= m_Nz)
                    Next(Stage.FlowSort);
            }
        }

        // Flow accumulation (gully cost): open cells in descending raw height, each passes its catchment to its steepest lower
        // neighbor (D8). Counting sort on the R16 value: O(N + 65536), deterministic (ties in cell order).
        // Row chunks: phase 1 counts (m_Cursor2 = 1), phase 2 places (m_Cursor2 = 2); the result equals one pass over the lattice.
        private void UnitFlowSort()
        {
            int[] count = m_Count;
            if (m_Cursor2 == 0)
            {
                Array.Clear(count, 0, 65537);
                m_FlowN = 0;
                m_Cursor2 = 1;
            }
            int jEnd = Math.Min(m_Nz, m_Cursor + RowsPerUnit);
            int c1 = jEnd * m_Nx;
            if (m_Cursor2 == 1)
            {
                for (int c = m_Cursor * m_Nx; c < c1; c++)
                {
                    m_Flow[c] = m_Texel * m_Texel;
                    if ((m_Flags[c] & FRegion) != 0)
                    {
                        count[65535 - m_Raw[c] + 1]++;
                        m_FlowN++;
                    }
                }
                m_Cursor = jEnd;
                if (m_Cursor >= m_Nz)
                {
                    for (int v = 1; v <= 65536; v++)
                        count[v] += count[v - 1];
                    m_Cursor = 0;
                    m_Cursor2 = 2;
                }
                return;
            }
            for (int c = m_Cursor * m_Nx; c < c1; c++)
                if ((m_Flags[c] & FRegion) != 0)
                    m_Order[count[65535 - m_Raw[c]]++] = c;
            m_Cursor = jEnd;
            if (m_Cursor >= m_Nz)
            {
                int n = m_FlowN;
                Next(Stage.FlowAccum);
                m_Cursor2 = n;
            }
        }

        private void UnitFlowAccum()
        {
            int end = Math.Min(m_Cursor2, m_Cursor + 16384);   // random access (cache misses): a quarter of a row chunk
            for (int k = m_Cursor; k < end; k++)
            {
                int c = m_Order[k];
                int i = c % m_Nx, j = c / m_Nx;
                float best = 0f;
                int to = -1;
                for (int d = 0; d < 8; d++)
                {
                    int ni = i + DirX[d], nj = j + DirZ[d];
                    if (ni < 0 || nj < 0 || ni >= m_Nx || nj >= m_Nz)
                        continue;
                    int nc = nj * m_Nx + ni;
                    float drop = (m_H[c] - m_H[nc]) / (d < 4 ? 1f : Sqrt2);
                    if (drop > best)
                    {
                        best = drop;
                        to = nc;
                    }
                }
                if (to >= 0)
                    m_Flow[to] += m_Flow[c];
            }
            m_Cursor = end;
            if (m_Cursor >= m_Cursor2)
                Next(Stage.Usable);
        }

        // Stage 1b: open / usable / cliff flags (row chunks over the region bbox), then the field part of the refusal ladder
        private void UnitUsable()
        {
            if (m_Cursor2 == 0)
            {
                m_Open = m_Usable = m_Cliff = m_Steep = 0;
                m_GradeSum = 0.0;
                m_HMinU = m_HMinOpen = float.PositiveInfinity;
                m_HMaxU = m_HMaxOpen = float.NegativeInfinity;
                m_MaxDHard = m_MaxDHardU = 0f;
                m_Cursor = m_RegionJ0;
                m_Cursor2 = 1;
            }
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    byte f = m_Flags[c];
                    // Open = a cell center at least one texel (plus the region clearance) inside every hard edge: an outline point marched
                    // from an open cell lies within 0.71 texel of that center, so it stays inside the region and clear of obstacles
                    if ((f & FRegion) == 0 || (f & FOffMap) != 0 || !(m_DHard[c] >= m_Texel + RegionClearanceM))
                        continue;
                    float o = m_O[c];
                    if (float.IsNaN(o))
                        continue;
                    f |= FOpen;
                    m_Open++;
                    m_GradeSum += Math.Min(o, MeanGradeCap);
                    m_HMinOpen = Math.Min(m_HMinOpen, m_H[c]);
                    m_HMaxOpen = Math.Max(m_HMaxOpen, m_H[c]);
                    m_MaxDHard = Math.Max(m_MaxDHard, m_DHard[c]);
                    if (o >= CliffGrade)
                    {
                        f |= FCliff;
                        m_Cliff++;
                    }
                    else
                    {
                        m_Usable++;
                        m_MaxDHardU = Math.Max(m_MaxDHardU, m_DHard[c]);
                        m_HMinU = Math.Min(m_HMinU, m_H[c]);
                        m_HMaxU = Math.Max(m_HMaxU, m_H[c]);
                        if (o >= SkiSlopeClassifier.GradeGreen)
                            m_Steep++;
                    }
                    m_Flags[c] = f;
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            AutoPlanResult r = m_Result;
            float region = Math.Max(1, m_RegionCells);
            r.OpenShare = m_Open / region;
            r.UsableShare = m_Usable / region;
            r.CliffShare = m_Open > 0 ? m_Cliff / (float)m_Open : 0f;
            r.SteepShare = m_Usable > 0 ? m_Steep / (float)m_Usable : 0f;
            r.VerticalM = m_Open > 0 ? m_HMaxOpen - m_HMinOpen : 0f;
            r.MeanGrade = m_Open > 0 ? (float)(m_GradeSum / m_Open) : 0f;
            if (r.OpenShare < BlockedShare)
            {
                Finish(AutoPlanStatus.Blocked);
                return;
            }
            if (m_MaxDHard < MinHalfWidthM)
            {
                Finish(AutoPlanStatus.TooSmall);
                return;
            }
            // F2: seeds and pods need DHard >= EdgeSeedMarginM; without one such usable cell the scan could only end in NoRuns with an
            // empty reject histogram (a 60 m corridor down one slope, or open ground hemmed in by other surfaces)
            if (m_Usable > 0 && m_MaxDHardU < EdgeSeedMarginM)
            {
                Finish(AutoPlanStatus.TooNarrow);
                return;
            }
            if (r.VerticalM < m_In.MinVerticalM)
            {
                Finish(AutoPlanStatus.NoVertical);
                return;
            }
            if (r.CliffShare > AllCliffShare || m_Usable == 0)
            {
                Finish(AutoPlanStatus.AllCliff);
                return;
            }
            if (r.OpenShare < PartlyBlockedShare)
                r.Warnings |= AutoPlanWarning.PartlyBlocked;
            Next(Stage.Zones);
        }

        // Stage 2: base = usable ground in the lowest BaseShare of the vertical (both feet of a ridge, the whole rim of a cone)
        // Row chunks: phase 1 flags the base over the region bbox, phase 2 seeds the minimax queue over the lattice.
        private void UnitZones()
        {
            if (m_Cursor2 == 0)
            {
                // B: capped at BaseMaxM, so gentle lower ground above 60 m of relief is skiable ground (green routes), not base
                float v = m_HMaxU - m_HMinU;
                m_BaseTop = m_HMinU + math.clamp(BaseShare * v, BaseMinM, BaseMaxM);
                m_BaseGentle = 0;
                m_Cursor = m_RegionJ0;
                m_Cursor2 = 1;
            }
            if (m_Cursor2 == 2)
            {
                int jEnd2 = Math.Min(m_Nz, m_Cursor + RowsPerUnit);
                for (int c = m_Cursor * m_Nx; c < jEnd2 * m_Nx; c++)
                {
                    m_M[c] = MUnreached;
                    if ((m_Flags[c] & FBase) != 0)
                        m_Minimax.Push(Bucket(m_O[c]), c);
                }
                m_Cursor = jEnd2;
                if (m_Cursor >= m_Nz)
                    Next(Stage.Minimax);
                return;
            }
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    byte f = m_Flags[c];
                    if ((f & (FOpen | FCliff)) == FOpen && m_H[c] <= m_BaseTop)
                    {
                        m_Flags[c] = (byte)(f | FBase);
                        if (m_O[c] < RunoutGrade)
                            m_BaseGentle++;
                    }
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            if (m_BaseGentle == 0)
                m_Result.Warnings |= AutoPlanWarning.NoFlatRunout;
            m_Minimax.Init(m_N);
            m_Cursor = 0;
            m_Cursor2 = 2;
        }

        private static int Bucket(float o) => o >= 2047f / 1024f ? 2047 : (int)(o * 1024f);

        // Stage 3: M = minimax easiest band down to the base (bucket queue over the classifier's 1/1024 buckets, 8-neighbor)
        private void UnitMinimax()
        {
            if (m_Cursor2 == 1)
            {
                AfterMinimax();
                return;
            }
            int budget = MinimaxPops;
            while (budget-- > 0)
            {
                if (!m_Minimax.Pop(out int key, out int c))
                {
                    m_Reach = false;
                    m_HReach = float.NegativeInfinity;
                    m_Cursor = m_RegionJ0;
                    m_Cursor2 = 1;
                    return;
                }
                if (m_M[c] != MUnreached)
                    continue;
                m_M[c] = (ushort)key;
                int i = c % m_Nx, j = c / m_Nx;
                for (int d = 0; d < 8; d++)
                {
                    int ni = i + DirX[d], nj = j + DirZ[d];
                    if (ni < 0 || nj < 0 || ni >= m_Nx || nj >= m_Nz)
                        continue;
                    int nc = nj * m_Nx + ni;
                    if (m_M[nc] != MUnreached || (m_Flags[nc] & (FOpen | FCliff)) != FOpen)
                        continue;
                    m_Minimax.Push(Math.Max(key, Bucket(m_O[nc])), nc);
                }
            }
        }

        // Row chunks over the region bbox: the highest usable cell MinDropM above the base that reaches it (m_HReach)
        private void AfterMinimax()
        {
            // A: AllCliff only when no usable cell at least MinDropM above the base reaches the base below the double black ceiling, at
            // any height (the CliffShare test ran in stage 1b). One steep summit cap no longer refuses a whole mountain: the top band
            // is advisory for the summits (TopRows measures it from m_HReach). TooFlat: almost no usable cell steeper than the green floor.
            int ceiling = Bucket(PassCeiling[Passes - 1]);
            float line = m_BaseTop + MinDropM;
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    if ((m_Flags[c] & (FOpen | FCliff)) == FOpen && m_H[c] >= line && m_M[c] < ceiling)
                    {
                        m_Reach = true;
                        m_HReach = Math.Max(m_HReach, m_H[c]);
                    }
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            if (!m_Reach)
            {
                Finish(AutoPlanStatus.AllCliff);
                return;
            }
            if (m_Result.SteepShare < TooFlatShare)
            {
                Finish(AutoPlanStatus.TooFlat);
                return;
            }
            m_Pass = 0;
            m_RouteSources = 0;
            Next(Stage.Route);
        }

        // Stage 4: one multi-source reverse Dijkstra from the base per pass (G, B, K, DB): each pass resets its arrays and pushes the
        // base in row chunks (m_Cursor2 = 0), then pops in heap chunks (m_Cursor2 = 1)
        private void StartPassRows()
        {
            byte[] pred = m_Pred[m_Pass];
            float[] dist = m_Dist[m_Pass];
            float ceil = PassCeiling[m_Pass], hard = PassHardCeiling[m_Pass];
            byte clear = (byte)~(1 << m_Pass);
            if (m_Cursor == 0)
                m_Heap.Clear();
            int jEnd = Math.Min(m_Nz, m_Cursor + RowsPerUnit);
            for (int c = m_Cursor * m_Nx; c < jEnd * m_Nx; c++)
            {
                pred[c] = PredNone;
                dist[c] = float.PositiveInfinity;
                m_InBand[c] &= clear;
                if ((m_Flags[c] & FBase) != 0 && m_O[c] < hard && m_H[c] <= SourceTop(m_Pass) && !(m_DEdge[c] < 0.5f * EdgeSeedMarginM))
                {
                    dist[c] = 0f;
                    pred[c] = PredSource;
                    m_ROver[c] = (byte)(m_O[c] >= ceil ? 1 : 0);
                    m_Heap.Push(0f, c);
                    m_RouteSources++;
                }
            }
            m_Cursor = jEnd;
            if (m_Cursor >= m_Nz)
                m_Cursor2 = 1;
        }

        private void UnitRoute()
        {
            if (m_Cursor2 == 0)
            {
                StartPassRows();
                return;
            }
            if (m_Cursor2 == 2)
            {
                ReachRows();
                return;
            }
            byte[] pred = m_Pred[m_Pass];
            float[] dist = m_Dist[m_Pass];
            float floor = PassFloor[m_Pass], ceil = PassCeiling[m_Pass], hard = PassHardCeiling[m_Pass];
            float t = m_Texel, inv2t = 1f / (2f * m_Texel);
            byte bit = (byte)(1 << m_Pass);
            int budget = RoutePops;
            while (budget-- > 0)
            {
                if (!m_Heap.Pop(out float du, out int u))
                {
                    if (++m_Pass >= Passes)
                    {
                        m_RouteReach = m_RouteSeedable = 0;
                        m_Cursor = m_RegionJ0;
                        m_Cursor2 = 2;
                    }
                    else
                    {
                        m_Cursor = m_Cursor2 = 0;
                    }
                    return;
                }
                if (du > dist[u])
                    continue;   // stale
                // Settled: its line to the base is final. In band = no cell of it at or over the pass ceiling.
                byte ou = m_ROver[u];
                if (ou == 0)
                    m_InBand[u] |= bit;
                int ui = u % m_Nx, uj = u / m_Nx;
                for (int d = 0; d < 8; d++)
                {
                    int vi = ui + DirX[d], vj = uj + DirZ[d];
                    if (vi < 1 || vj < 1 || vi >= m_Nx - 1 || vj >= m_Nz - 1)
                        continue;
                    int v = vj * m_Nx + vi;
                    float w = StepCost(u, v, d, floor, ceil, hard, t, inv2t);
                    if (!(w >= 0f))
                        continue;
                    float dv = du + w;
                    if (dv < dist[v])
                    {
                        dist[v] = dv;
                        pred[v] = (byte)d;
                        m_ROver[v] = (byte)(ou | (m_O[v] >= ceil ? 1 : 0));
                        m_Heap.Push(dv, v);
                    }
                }
            }
        }

        /// <summary>Cost of the skier's step v -> u on a pass's graph, where v = u + Dir[d] (the route relaxes u -> v); -1 when the
        /// step is not allowed (v not open, cliff, at or over the HARD ceiling, within EdgeSeedMarginM / 2 of the region edge, or a climb
        /// over MaxClimbM). Over the pass ceiling a
        /// cell costs OverBandFactor, in band InBandFactor; within EdgeSeedMarginM of a hard cell (the region edge, an obstacle)
        /// EdgeCostFactor. v must be an interior cell. The one cost both the base routes and the per-candidate reroute use, so a
        /// route distance is an exact A* heuristic.</summary>
        private float StepCost(int u, int v, int d, float floor, float ceil, float hard, float t, float inv2t)
        {
            byte f = m_Flags[v];
            if ((f & (FOpen | FCliff)) != FOpen)
                return -1f;
            float o = m_O[v];
            if (!(o < hard) || m_DEdge[v] < 0.5f * EdgeSeedMarginM)
                return -1f;
            if (m_H[u] > m_H[v] + MaxClimbM)
                return -1f;
            float len = d < 4 ? t : t * Sqrt2;
            float dirx = -DirX[d] / (d < 4 ? 1f : Sqrt2), dirz = -DirZ[d] / (d < 4 ? 1f : Sqrt2);
            float gx = (m_H[v + 1] - m_H[v - 1]) * inv2t, gz = (m_H[v + m_Nx] - m_H[v - m_Nx]) * inv2t;
            float cross = math.abs(-gx * dirz + gz * dirx);
            float mult = 1f + SidehillCost * cross;
            if (o < SkiSlopeClassifier.GradeGreen)
                mult += FlatCost;
            if (m_Flow[v] > GullyAreaM2)
                mult += GullyCost * (float)Math.Log(m_Flow[v] / GullyAreaM2);
            if ((f & FCat) != 0)
                mult += CatTrackCost;
            if (o >= ceil)
                mult *= OverBandFactor;
            else if (o >= floor)
                mult *= InBandFactor;
            if (m_DHard[v] < EdgeSeedMarginM)
                mult *= EdgeCostFactor;
            return len * mult;
        }

        private bool m_FieldReady;

        // After the last pass, row chunks over the region bbox: does any route keep its pass's band from MinDropM above that pass's
        // sources (m_RouteReach), and does one such cell lie EdgeSeedMarginM inside (m_RouteSeedable)? AfterMinimax's reach test has no
        // edge strip and no source limit, so it can pass where no route can seed (review F11): AllCliff, or TooNarrow, then, never a
        // NoRuns with an empty reject histogram. The same cells PodRows and SeedRows draw from.
        private void ReachRows()
        {
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    byte bits = m_InBand[c];
                    if (bits == 0 || (m_Flags[c] & (FOpen | FCliff)) != FOpen)
                        continue;
                    bool reach = false;
                    for (int p = 0; p < Passes && !reach; p++)
                        reach = (bits & (1 << p)) != 0 && m_H[c] - SourceTop(p) >= MinDropM;
                    if (!reach)
                        continue;
                    m_RouteReach++;
                    if (m_DHard[c] >= EdgeSeedMarginM)
                        m_RouteSeedable++;
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            if (m_RouteReach == 0)
            {
                // No source at all = the base lies within the edge strip (a narrow tongue at the bottom): narrow, not cliff
                Finish(m_RouteSources == 0 ? AutoPlanStatus.TooNarrow : AutoPlanStatus.AllCliff);
                return;
            }
            if (m_RouteSeedable == 0)
            {
                Finish(AutoPlanStatus.TooNarrow);
                return;
            }
            m_FieldReady = true;
            Next(Stage.Seeds);
        }

        /// <summary>Highest source cell of a pass's routes (PassSourceShare of the base band).</summary>
        private float SourceTop(int pass) => m_HMinU + PassSourceShare[pass] * (m_BaseTop - m_HMinU);

        // ---------------------------------------------------------------- lattice helpers

        /// <summary>Lattice cell of a world point (nearest cell center); false outside the lattice.</summary>
        private bool Cell(float2 p, out int c)
        {
            long i = (long)Math.Round((p.x - m_Origin.x) / m_Texel) - m_LI0;
            long j = (long)Math.Round((p.y - m_Origin.y) / m_Texel) - m_LJ0;
            if (i < 0 || j < 0 || i >= m_Nx || j >= m_Nz)
            {
                c = -1;
                return false;
            }
            c = (int)(j * m_Nx + i);
            return true;
        }

        private float2 CellCenter(int c)
        {
            int i = c % m_Nx, j = c / m_Nx;
            return new float2((float)(m_Origin.x + (double)(m_LI0 + i) * m_Texel), (float)(m_Origin.y + (double)(m_LJ0 + j) * m_Texel));
        }

        private bool OverlapsLattice(float2 lo, float2 hi)
        {
            double x0 = m_Origin.x + (double)m_LI0 * m_Texel, z0 = m_Origin.y + (double)m_LJ0 * m_Texel;
            double x1 = x0 + (double)(m_Nx - 1) * m_Texel, z1 = z0 + (double)(m_Nz - 1) * m_Texel;
            return hi.x >= x0 && lo.x <= x1 && hi.y >= z0 && lo.y <= z1;
        }

        /// <summary>Plans a ring on the world lattice with the classifier (cell center inside); returns the Mask cell count.</summary>
        private int Rasterize(float2[] ring)
        {
            int n = ring.Length;
            if (m_Pts3.Length < n)
                m_Pts3 = new float3[Math.Max(n, m_Pts3.Length * 2)];
            for (int k = 0; k < n; k++)
                m_Pts3[k] = new float3(ring[k].x, 0f, ring[k].y);
            m_Grid.DiagonalRule = false;
            SlopeStatus s = SkiSlopeClassifier.Plan(m_Pts3, n, m_Origin, m_Texel, m_Grid);
            if (s != SlopeStatus.Ok || m_Grid.PolylineMode)
                return 0;
            return m_Grid.InsideCells;
        }

        /// <summary>Calls f for every Mask cell of m_Grid that lies on the lattice (lattice cell index).</summary>
        private void ForEachMaskCell(Action<int> f) => ForEachMaskCell(0, m_Grid.Nz, f);

        /// <summary>ForEachMaskCell over the grid rows [j0, j1).</summary>
        private void ForEachMaskCell(int j0, int j1, Action<int> f)
        {
            SlopeGrid g = m_Grid;
            for (int j = j0; j < j1; j++)
            {
                int lj = g.J0 + j - m_LJ0;
                if (lj < 0 || lj >= m_Nz)
                    continue;
                int row = j * g.Nx;
                for (int i = 0; i < g.Nx; i++)
                {
                    if (g.Mask[row + i] == 0)
                        continue;
                    int li = g.I0 + i - m_LI0;
                    if (li >= 0 && li < m_Nx)
                        f(lj * m_Nx + li);
                }
            }
        }

        private static void Grow<T>(ref T[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new T[Math.Max(n, a == null ? 0 : a.Length + a.Length / 2)];
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static bool RingFinite(float2[] r)
        {
            for (int i = 0; i < r.Length; i++)
                if (!Finite(r[i].x) || !Finite(r[i].y))
                    return false;
            return true;
        }

        private static uint FloatBits(float f) => math.asuint(f);

        private static uint Fnv(uint h, uint v)
        {
            for (int b = 0; b < 4; b++)
            {
                h ^= (v >> (8 * b)) & 0xFFu;
                h *= 16777619u;
            }
            return h;
        }
    }
}
