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
// chunk, one candidate) until the budget is spent, so no single call is a whole-region pass (skeptic finding 2). The one unit that
// cannot be cut is a single SkiRunRater.Rate (~1-1.5 ms CoreCLR for a large run).
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
        AllCliff,   // over AllCliffShare of the open ground is cliff, or no top reaches the base without cliff
        TooFlat,    // under TooFlatShare of the usable ground is steeper than the green floor
        NoRuns,     // the field was fine but no candidate survived (Rejects says why)
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
        /// <summary>Run cap; 0 = the density's default (8 / 12 / 16), clamped to HardRunCap.</summary>
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
        /// <summary>v1 owner ruling 1 km2; the code is tested to 4 km2 (ReadyMaxAreaM2).</summary>
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
    /// Pod = not a summit pick but the highest ground a pass reaches when no summit reaches it (a beginner pod near the base for
    /// green, a mid-station for blue); PodBand is that pass's band.</summary>
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
        public int Candidates, Rated;
        public readonly float[] StageMs = new float[SkiRunAutoPlanner.StageCount];
        public float MaxUnitMs;
        public int Units;
        public ulong Fingerprint;

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
            RegionAreaM2 = OpenShare = UsableShare = CliffShare = SteepShare = VerticalM = 0f;
            Candidates = Rated = 0;
            Array.Clear(StageMs, 0, StageMs.Length);
            MaxUnitMs = 0f;
            Units = 0;
            Fingerprint = 0;
        }
    }

    public sealed partial class SkiRunAutoPlanner
    {
        // ---------------------------------------------------------------- constants (sources: design.md 3.2, FEASIBILITY 4/9)
        public const float V1MaxAreaM2 = 1.0e6f, ReadyMaxAreaM2 = 4.0e6f;      // owner ruling: 1 km2 in v1, code ready for 4
        public const float DefaultMinAreaM2 = 40000f, DefaultMinVerticalM = 50f;
        public const int MaxLatticeCells = 1 << 20;                              // scan rectangle cap (3.6 x 3.6 km at 3.5 m)
        public const float LatticeMarginM = 40f;                                 // 20 m windows + Dilation + bilinear reach
        public const int OffMapMarginTexels = SkiSlopeClassifier.Dilation + 2;   // keeps every rating Need node on the map
        public const float BlockedShare = 0.30f, PartlyBlockedShare = 0.60f, TooFlatShare = 0.20f, AllCliffShare = 0.60f;
        public const float CliffGrade = 1.0f;                                    // 45 deg
        public const float MinHalfWidthM = 7.5f;                                 // half the 15 m pinch width
        // Routing passes G / B / K / DB: impassable at or over the ceiling; in-band cells cost InBandFactor (pulls blacks onto steep).
        // The double black ceiling is 80 % (expert max, real-world-layout.md 1), not the classifier's open band.
        public static readonly float[] PassFloor = { SkiSlopeClassifier.GradeGreen, SkiSlopeClassifier.GradeBlue, SkiSlopeClassifier.GradeBlack, SkiSlopeClassifier.GradeDoubleBlack };
        public static readonly float[] PassCeiling = { SkiSlopeClassifier.GradeBlue, SkiSlopeClassifier.GradeBlack, SkiSlopeClassifier.GradeDoubleBlack, 0.80f };
        public static readonly float[] TargetWidthM = { 40f, 35f, 30f, 20f };
        public const int Passes = 4;
        public const float InBandFactor = 0.4f, SidehillCost = 2f, FlatCost = 3f, GullyCost = 0.5f, GullyAreaM2 = 20000f;
        public const float MaxClimbM = 0.5f, CatTrackCost = 1f;
        public const float TopBandM = 30f, ClusterRadiusM = 150f, ClusterSpacingM = 300f;
        public const int MaxClusters = 3, SeedsPerClusterPass = 6;
        public const float SeedJitterM = 10f;
        public const float BaseShare = 0.15f, BaseMinM = 10f, RunoutGrade = 0.12f;
        public const float MinWidthM = 15f, PinchShare = 0.30f, MinLengthM = 150f, MinDropM = 30f, MaxLengthM = 2500f;
        public const float MaxRunAreaM2 = 200000f;                               // 20 ha; and StepMultiple must stay 1
        public const float SampleM = 4f, DecimateM = 10f, RingSimplifyM = 1.0f, WidthMarginM = 1f, MarchStepM = 1f;
        public const float SparseGapM = 30f, NormalGapM = 15f, DenseGapM = 10f, RasterSlackM = 1f;
        public const float ExactGapShare = 0.8f;                                 // exact distance >= 0.8 gap (raster approximations)
        public const float MergeGapM = 2f, MergeTaperM = 30f;                    // abutting is legal (ValidationHelpers.cs:575)
        public const float JunctionReachM = 20f;                                 // node pair search; +2 x 2 m snap stays <= LinkMeters
        public const float LinkMeters = LiftServedRule.LinkMeters;               // the link rule's own constant (one source)
        public const float SurfaceClearanceM = MergeGapM, UnownedClearanceM = 0.5f, RegionClearanceM = 0.25f;
        public const int HardRunCap = 16, MaxRatings = 48, TileCoreCells = 80;
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
        private float[] m_H, m_O, m_DHard, m_DAcc, m_Flow, m_Dist;
        private ushort[] m_M;
        private readonly byte[][] m_Pred = new byte[Passes][];
        private int[] m_Order, m_Count;
        private readonly List<int> m_Tiles = new List<int>();

        // Reused classifier buffers: field tiles, obstacle / region rasters, candidate ratings
        private readonly SlopeGrid m_Grid = new SlopeGrid();
        private readonly SlopeResult m_Slope = new SlopeResult();
        private float3[] m_Pts3 = new float3[64];

        // Field statistics
        private float m_HMinU, m_HMaxU, m_HMinOpen, m_HMaxOpen, m_BaseTop;
        private int m_Open, m_Usable, m_Cliff, m_Steep, m_BaseGentle;
        private float m_MaxDHard;

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
                Unit();
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                float ms = (float)((t1 - t0) * toMs);
                m_Result.StageMs[stage] += ms;
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
            Grow(ref m_DAcc, m_N);
            Grow(ref m_Flow, m_N);
            Grow(ref m_Dist, m_N);
            Grow(ref m_M, m_N);
            Grow(ref m_Order, m_N);
            for (int k = 0; k < Passes; k++)
                Grow(ref m_Pred[k], m_N);
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

        // Stage 0a: heights, off-map flags, region raster (first unit), then rows
        private void UnitHeights()
        {
            HeightSnapshot snap = m_In.Snapshot;
            if (m_Cursor == 0 && m_Cursor2 == 0)
            {
                Array.Clear(m_Flags, 0, m_N);
                // Region raster with the classifier's own rule (cell center inside), on the world lattice
                int n = Rasterize(m_Region);
                m_RegionCells = 0;
                m_RegionI0 = m_RegionJ0 = int.MaxValue;
                m_RegionI1 = m_RegionJ1 = int.MinValue;
                if (n > 0)
                    ForEachMaskCell(c =>
                    {
                        m_Flags[c] |= FRegion;
                        m_RegionCells++;
                        int i = c % m_Nx, j = c / m_Nx;
                        m_RegionI0 = Math.Min(m_RegionI0, i);
                        m_RegionI1 = Math.Max(m_RegionI1, i);
                        m_RegionJ0 = Math.Min(m_RegionJ0, j);
                        m_RegionJ1 = Math.Max(m_RegionJ1, j);
                    });
                m_Cursor2 = 1;
                if (m_RegionCells == 0)
                {
                    Finish(AutoPlanStatus.TooSmall);
                    return;
                }
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
                    m_DAcc[c] = float.PositiveInfinity;
                    if (k < OffMapMarginTexels || l < OffMapMarginTexels || k > resX - 1 - OffMapMarginTexels || l > resZ - 1 - OffMapMarginTexels)
                        m_Flags[c] |= FOffMap;
                    if (snap.IsWet(k, l))
                        m_Flags[c] |= FWet;
                    if ((m_Flags[c] & (FRegion | FOffMap)) != FRegion)
                        m_DHard[c] = -0.5f * m_Texel;
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

        // Clearance field: two chamfer passes in row chunks
        private void UnitDist(bool forward)
        {
            int rows = RowsPerUnit;
            if (forward)
            {
                int jEnd = Math.Min(m_Nz, m_Cursor + rows);
                Chamfer(m_DHard, m_Nx, 0, m_Nx - 1, 0, m_Nz - 1, m_Cursor, jEnd - 1, true, m_Texel);
                m_Cursor = jEnd;
                if (m_Cursor >= m_Nz)
                    Next(Stage.DistBackward);
            }
            else
            {
                int jHi = m_Nz - 1 - m_Cursor, jLo = Math.Max(0, jHi - rows + 1);
                Chamfer(m_DHard, m_Nx, 0, m_Nx - 1, 0, m_Nz - 1, jLo, jHi, false, m_Texel);
                m_Cursor += jHi - jLo + 1;
                if (m_Cursor >= m_Nz)
                    Next(Stage.FlowSort);
            }
        }

        // Flow accumulation (gully cost): open cells in descending raw height, each passes its catchment to its steepest lower
        // neighbor (D8). Counting sort on the R16 value: O(N + 65536), deterministic (ties in cell order).
        private void UnitFlowSort()
        {
            int[] count = m_Count;
            Array.Clear(count, 0, 65537);
            int n = 0;
            for (int c = 0; c < m_N; c++)
            {
                m_Flow[c] = m_Texel * m_Texel;
                if ((m_Flags[c] & FRegion) != 0)
                {
                    count[65535 - m_Raw[c] + 1]++;
                    n++;
                }
            }
            for (int v = 1; v <= 65536; v++)
                count[v] += count[v - 1];
            for (int c = 0; c < m_N; c++)
                if ((m_Flags[c] & FRegion) != 0)
                    m_Order[count[65535 - m_Raw[c]]++] = c;
            m_Cursor2 = n;
            m_Stage = Stage.FlowAccum;
            m_Cursor = 0;
        }

        private void UnitFlowAccum()
        {
            int end = Math.Min(m_Cursor2, m_Cursor + 65536);
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

        // Stage 1b: open / usable / cliff flags and the field part of the refusal ladder (one pass over the region bbox)
        private void UnitUsable()
        {
            m_Open = m_Usable = m_Cliff = m_Steep = 0;
            m_HMinU = m_HMinOpen = float.PositiveInfinity;
            m_HMaxU = m_HMaxOpen = float.NegativeInfinity;
            m_MaxDHard = 0f;
            for (int j = m_RegionJ0; j <= m_RegionJ1; j++)
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
                        m_HMinU = Math.Min(m_HMinU, m_H[c]);
                        m_HMaxU = Math.Max(m_HMaxU, m_H[c]);
                        if (o >= SkiSlopeClassifier.GradeGreen)
                            m_Steep++;
                    }
                    m_Flags[c] = f;
                }
            AutoPlanResult r = m_Result;
            float region = Math.Max(1, m_RegionCells);
            r.OpenShare = m_Open / region;
            r.UsableShare = m_Usable / region;
            r.CliffShare = m_Open > 0 ? m_Cliff / (float)m_Open : 0f;
            r.SteepShare = m_Usable > 0 ? m_Steep / (float)m_Usable : 0f;
            r.VerticalM = m_Open > 0 ? m_HMaxOpen - m_HMinOpen : 0f;
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
        private void UnitZones()
        {
            float v = m_HMaxU - m_HMinU;
            m_BaseTop = m_HMinU + Math.Max(BaseShare * v, BaseMinM);
            m_BaseGentle = 0;
            for (int j = m_RegionJ0; j <= m_RegionJ1; j++)
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
            if (m_BaseGentle == 0)
                m_Result.Warnings |= AutoPlanWarning.NoFlatRunout;
            m_Minimax.Init(m_N);
            for (int c = 0; c < m_N; c++)
            {
                m_M[c] = MUnreached;
                if ((m_Flags[c] & FBase) != 0)
                    m_Minimax.Push(Bucket(m_O[c]), c);
            }
            Next(Stage.Minimax);
        }

        private static int Bucket(float o) => o >= 2047f / 1024f ? 2047 : (int)(o * 1024f);

        // Stage 3: M = minimax easiest band down to the base (bucket queue over the classifier's 1/1024 buckets, 8-neighbor)
        private void UnitMinimax()
        {
            int budget = 65536;
            while (budget-- > 0)
            {
                if (!m_Minimax.Pop(out int key, out int c))
                {
                    AfterMinimax();
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

        private void AfterMinimax()
        {
            // AllCliff: no usable top cell reaches the base below the double black pass ceiling. TooFlat: almost no usable cell
            // steeper than the green floor.
            int ceiling = Bucket(PassCeiling[Passes - 1]);
            float topLine = m_HMaxU - TopBandM;
            bool reach = false;
            for (int j = m_RegionJ0; j <= m_RegionJ1 && !reach; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    if ((m_Flags[c] & (FOpen | FCliff)) == FOpen && m_H[c] >= topLine && m_M[c] < ceiling)
                    {
                        reach = true;
                        break;
                    }
                }
            if (!reach)
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
            StartPass();
            Next(Stage.Route);
        }

        // Stage 4: one multi-source reverse Dijkstra from the base per pass (G, B, K, DB), in heap chunks
        private void StartPass()
        {
            byte[] pred = m_Pred[m_Pass];
            float ceil = PassCeiling[m_Pass];
            m_Heap.Clear();
            for (int c = 0; c < m_N; c++)
            {
                pred[c] = PredNone;
                m_Dist[c] = float.PositiveInfinity;
            }
            for (int c = 0; c < m_N; c++)
                if ((m_Flags[c] & FBase) != 0 && m_O[c] < ceil)
                {
                    m_Dist[c] = 0f;
                    pred[c] = PredSource;
                    m_Heap.Push(0f, c);
                }
        }

        private void UnitRoute()
        {
            byte[] pred = m_Pred[m_Pass];
            float floor = PassFloor[m_Pass], ceil = PassCeiling[m_Pass];
            float t = m_Texel, inv2t = 1f / (2f * m_Texel);
            int budget = 32768;
            while (budget-- > 0)
            {
                if (!m_Heap.Pop(out float du, out int u))
                {
                    if (++m_Pass >= Passes)
                    {
                        m_FieldReady = true;
                        Next(Stage.Seeds);
                    }
                    else
                    {
                        StartPass();
                    }
                    return;
                }
                if (du > m_Dist[u])
                    continue;   // stale
                int ui = u % m_Nx, uj = u / m_Nx;
                for (int d = 0; d < 8; d++)
                {
                    int vi = ui + DirX[d], vj = uj + DirZ[d];
                    if (vi < 1 || vj < 1 || vi >= m_Nx - 1 || vj >= m_Nz - 1)
                        continue;
                    int v = vj * m_Nx + vi;
                    byte f = m_Flags[v];
                    if ((f & (FOpen | FCliff)) != FOpen)
                        continue;
                    float o = m_O[v];
                    if (!(o < ceil))
                        continue;
                    // the skier moves v -> u: no climb over MaxClimbM
                    if (m_H[u] > m_H[v] + MaxClimbM)
                        continue;
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
                    if (o >= floor)
                        mult *= InBandFactor;
                    float dv = du + len * mult;
                    if (dv < m_Dist[v])
                    {
                        m_Dist[v] = dv;
                        pred[v] = (byte)d;
                        m_Heap.Push(dv, v);
                    }
                }
            }
        }

        private bool m_FieldReady;

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
        private void ForEachMaskCell(Action<int> f)
        {
            SlopeGrid g = m_Grid;
            for (int j = 0; j < g.Nz; j++)
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
