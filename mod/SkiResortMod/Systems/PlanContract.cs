// Plan contract: the result and input types of the ski run planner, shared by the tool layer, the tests and every planner behind
// them (the v1 / v2 SkiRunAutoPlanner and the runs-first planner that replaces it). The planner files hold none of these types, so
// deleting a planner never touches a reader. Resort plan types (ResortPlan, PlanLift, ...) live in the planner's Plan partial file, the
// height copy in its Snapshot partial file, the pure geometry and constants in PlanGeometry.cs.
// Entities-free: usings are System, System.Collections.Generic and Unity.Mathematics ONLY (the file also links into the net8 test host).

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
                    // again after the routes for the ground they reach. Review F2: it used to end as NoRuns with no reject)
    }

    /// <summary>Preview warnings (the planner's share, the tool adds money, sign, renumbering and game-check warnings).</summary>
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
        NoLiftSite = 1024,    // Resort: no pod could hold a feasible lift chord (proposals empty, runs still planned for pinned lifts)
        Download = 2048,      // Resort: at least one lift top has only a Download verdict (honest, not an error)
        DeadEnd = 4096,       // Resort: a run bottom reaches nothing (gate: 0 expected)
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
        PodFull,     // Resort: the ring would take its pod past DevelopableShare x SkiableAreaM2 (spec 4.5, Fans.cs)
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

    /// <summary>Kinds the player allows (default groomed + natural, night never by default).</summary>
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

    /// <summary>Obstacle classes (owner rulings, BUILD-BRIEF 1): every existing Surface area and unowned map tile blocks. Cat tracks
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
        /// <summary>Run cap. 0 = the density's default (8 / 12 / 16 per km2, at least 1 km2), clamped to HardRunCap.</summary>
        public int RunCap;
        /// <summary>Layout seed (PlanGeometry.SeedOf(region, shuffle)).</summary>
        public uint Seed;
        /// <summary>Resort: 0 = Auto (spec 4.4), 1..9 = cap on proposed lifts. Ignored in Runs mode.</summary>
        public int Lifts;
        /// <summary>Resort: the way-home level the verdicts ask for (Auto = green at the base, blue above). Ignored in Runs mode.</summary>
        public HomeLevel Home;

        public static AutoPlanKnobs Default(uint seed) =>
            new AutoPlanKnobs { Mix = AutoMix.Balanced, Density = AutoDensity.Normal, Kinds = AutoKinds.Default, RunCap = 0, Seed = seed };
    }

    public sealed class AutoPlanInput
    {
        public HeightSnapshot Snapshot;
        public float2[] Region;
        public List<AutoObstacle> Obstacles;
        public AutoPlanKnobs Knobs;
        /// <summary>Owner ruling 9 km2 (3 x 3 km, DECISIONS 419, was 1 km2 in v1).</summary>
        public float MaxAreaM2 = PlanGeometry.V1MaxAreaM2;
        public float MinAreaM2 = PlanGeometry.DefaultMinAreaM2;
        public float MinVerticalM = PlanGeometry.DefaultMinVerticalM;
        /// <summary>Runs = v1 exactly (default, test host only). Resort = v2 (the tool always).</summary>
        public AutoPlanMode Mode = AutoPlanMode.Runs;
        /// <summary>Existing lifts touching the region (census copy), sorted by the tool (key, then xz). null = none. Resort only.</summary>
        public List<PinnedLift> Pinned;
        /// <summary>Road edge samples (every 25 m) and Welcome Sign spots within the region bbox + 450 m, sorted. null = none. Resort only.</summary>
        public float2[] Access;
        /// <summary>The tool's tree line estimate when known (SkiGladeTreeLine), NaN = unknown. Resort only.</summary>
        public float TreeLineY = float.NaN;
        /// <summary>Polled once per unit. null = never canceled.</summary>
        public AutoPlanCancel Cancel;
        /// <summary>The planner is stepped on the main thread (the tool's fallback setting): the Lifts stage selects its picks without a
        /// tower plan (PlanLift.TowersUnchecked). Resort only.</summary>
        public bool MainThread;
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
        // Runs-first realizer (RunsFirstRealizer.cs). The v1 / v2 planner leaves them at their defaults.
        public bool Graded;            // the planner assumes this element is cut into the ground (round 3 grades it, round 2 paints it)
        public SlopeBand PlannedBand;  // the planner's band (a graded element: its band along the path, the band it has after grading)
        public int Source;             // the planner element it came from: the run index, or -1 - the connector index
        public int Segment;            // 0, 1, 2 along the source when the realizer split it
        public bool Unserved;          // the game's served rule does not reach this area
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
    /// mid-stations for blue, black only when no summit keeps the black band). PodBand is that pass's band.</summary>
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
        /// <summary>Mean terrain grade (rise over run, 0.25 = 25 %) over the open cells, each capped at PlanGeometry.MeanGradeCap. The
        /// site verdict (QuickRunsRules.SiteTierOf) reads it. 0 with no open cell (OpenShare 0). Set before the refusal ladder, so refused scans have it.</summary>
        public float MeanGrade;
        public int Candidates, Rated;
        public readonly float[] StageMs = new float[PlanGeometry.StageCount];
        public readonly float[] StageMaxMs = new float[PlanGeometry.StageCount];   // longest unit per stage (the probe names the slow stage)
        public float MaxUnitMs;
        /// <summary>Longest stage 13 (Select) unit per phase: pick / A* (reroute) / build / rate / chamfer (the probe names the slow phase).</summary>
        public readonly float[] SelectPhaseMaxMs = new float[PlanGeometry.SelectPhases];
        public int Units;
        public ulong Fingerprint;
        /// <summary>v2 additions (Resort mode). null in Runs mode.</summary>
        public ResortPlan Resort;
        /// <summary>Step returned early on Cancel: the result is incomplete and must be dropped.</summary>
        public bool Canceled;

        /// <summary>A published copy for another thread. The lists and arrays are copied. The AutoRun objects are shared (new per Finish,
        /// never mutated afterwards). The ResortPlan is deep-copied. Call it on the worker or after the scan is done.</summary>
        public AutoPlanResult Snapshot()
        {
            var c = new AutoPlanResult
            {
                Status = Status, Warnings = Warnings, RegionAreaM2 = RegionAreaM2, OpenShare = OpenShare, UsableShare = UsableShare,
                CliffShare = CliffShare, SteepShare = SteepShare, VerticalM = VerticalM, MeanGrade = MeanGrade, Candidates = Candidates,
                Rated = Rated, MaxUnitMs = MaxUnitMs, Units = Units, Fingerprint = Fingerprint, Canceled = Canceled,
                Resort = Resort?.Copy(),
            };
            c.Runs.AddRange(Runs);
            c.Junctions.AddRange(Junctions);
            c.Clusters.AddRange(Clusters);
            Array.Copy(BandCount, c.BandCount, BandCount.Length);
            Array.Copy(BandTarget, c.BandTarget, BandTarget.Length);
            Array.Copy(Rejects, c.Rejects, Rejects.Length);
            Array.Copy(StageMs, c.StageMs, StageMs.Length);
            Array.Copy(StageMaxMs, c.StageMaxMs, StageMaxMs.Length);
            Array.Copy(SelectPhaseMaxMs, c.SelectPhaseMaxMs, SelectPhaseMaxMs.Length);
            return c;
        }

        /// <summary>The non-zero reject counts as "Name count, ..." ("none" when empty). Log text, not player text.</summary>
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
            Resort = null;
            Canceled = false;
        }
    }
}
