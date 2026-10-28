// Resort Planner (v2): every plan type, written once (stage A, task A0) so the terrain / pods / lifts / fans / flow tasks fill them
// and never touch each other's files. Pure and Entities-free like the rest of the planner (usings System and Unity.Mathematics).
// All plan objects are created on the worker and never mutated after the scan finishes: plain arrays and structs (the host prints and
// fingerprints them), world xz in meters, heights in meters (snapshot heights). Ids are stable for the same (snapshot, region,
// obstacles, pinned lifts, knobs) and survive a Reselect that does not restart before their stage.
// Spec: docs/research/quickruns-v2/spec/stage-a-spec.md section 2. Engine notes: EngineNotes/ResortPlanner.md.

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Runs = the v1 planner exactly (the default; test host only: it keeps v1's fingerprints as regression data).
    /// Resort = v2 (the tool always runs it).</summary>
    public enum AutoPlanMode : byte { Runs, Resort }

    /// <summary>Ecosign segmentation bands (planning only; the rater keeps SlopeBand 6 / 25 / 40 / 60 %).
    /// Flat under 6 %, Marginal 6-8, Beginner 8-25, Intermediate 25-45, Advanced 45-70, Expert 70-100, Cliff 100 % and over.</summary>
    public enum TerrainBand : byte { Flat, Marginal, Beginner, Intermediate, Advanced, Expert, Cliff }

    /// <summary>Pad kind by the pad's mean smoothed grade: under 8 / 8-15 / 15-25 %.</summary>
    public enum PadKind : byte { Flat, Learning, Novice }

    public enum HomeVerdict : byte { None, Green, Blue, Black, DoubleBlack, Download }

    /// <summary>The Home knob. Auto = Green for lifts whose bottom pad touches the base band, Blue above (Q4). It sets Wanted (the
    /// color and the warning), never the verdict (DECISIONS 451 b).</summary>
    public enum HomeLevel : byte { Auto, Green, Blue }

    /// <summary>Pinned = an existing lift (census) or the "plan around this lift" pick.</summary>
    public enum LiftOrigin : byte { Proposed, Pinned }

    public struct PlanPad
    {
        public int Id;                    // index in Pads (sorted by the pad's smallest cell index)
        public PadKind Kind;
        public float AreaM2, MeanGrade, MinY, MaxY;
        public float2 Center;             // the station spot: the pad cell with the largest inscribed distance (Chamfer)
        public float InscribedM;          // that distance; >= StationHalfDiagonalOf(type) = can hold that station's lot
        public bool IsBase;               // touches the v1 base band (Zones) or is the lowest pad
        public bool IsBench;              // not base, MinY above the base band top
        public bool TouchesBase;          // touches the v1 base band (IsBase without the lowest-pad fallback); set by FinishResort (Flow.cs)
        public float AccessM;             // distance from Center to the nearest access point (road / sign); +inf when none
        public float GradeM3, GradeMaxM;  // A3 (additive): balanced cut (= fill) to level the cheapest station lot at Center, deepest cut or fill
    }

    public struct PlanPod
    {
        public int Id;                    // index in Pods; letter A, B, C... in the UI (Id order)
        public uint Key;                  // stable toggle key: FNV(lower pad's smallest cell, top cell) (survives Reselect)
        public int LowerPad;              // the pad the pod drains to (its lift's bottom)
        public int UpperPad;              // a pad in the upper third of the pod, -1 = none (top station on the summit cells)
        public float2 TopCenter;          // the highest usable spot (top station spot when UpperPad < 0)
        public float TopY, BottomY, VerticalM, ReachM, StraightGrade;
        public float AreaM2, SkiableAreaM2, DevelopableShare;   // DevelopableShare per spec section 4.2
        public TerrainBand PrimaryClass;  // band of the 80th percentile of the skiable cells' 20 m window grade (Ecosign class)
        public float[] BandShare;         // [7] share of skiable cells per TerrainBand (allocated per pod by the planner; a struct field
                                          // cannot have an initializer in C# 9, the host LangVersion)
        public float MeanFanGrade;        // mean grade of the skiable cells (the [G] "fan steepness")
        public bool AboveTreeLine;        // over half the cells above TreeLineY
        public int Lift;                  // Lifts index serving it, -1 = unserved
        public int RunFirst, RunCount;    // slice of Plan.PodRuns (indices into AutoPlanResult.Runs)
    }

    public struct PlanLift
    {
        public int Id;                    // index in Lifts; "P1, P2..." for proposals in the UI, the census name for pinned
        public uint Key;                  // stable toggle key: FNV(pod key, type index) for proposals, the census EntityRef for pinned
        public LiftOrigin Origin;
        public byte TypeIndex;            // SkiCatalog lift type (fixedGrip, detachable, gondola, tram)
        public float3 Bottom, Top;        // station spots (snapshot heights)
        public float LengthM, RiseM, MaxEdgeSlope;
        public int Towers;                // LiftTowerPlanner count; -1 = not planned (pinned lifts keep their own; TowersUnchecked)
        public bool TowersUnchecked;      // a proposal selected without a tower plan (the main-thread fallback: AutoPlanInput.MainThread)
        public float2[] TowerXz;          // planned tower spots (overlay discs); empty for pinned
        public int Pod, BottomPad, TopPad;   // TopPad -1 = summit cells
        public bool TopSunk, BottomSunk;  // the station spot is steeper than 25 % (the station sinks, as today: C1 3b)
        public long Cost;                 // stations + line (SkiCatalog), the overlay's "about N" figure
        public int Serves;                // runs whose top LiftServedRule.Reaches would accept (150 m corridor, top margin)
        public int StartsNearTop, EndsAtBottom;   // gate counters: run tops within 100 m of Top, run ends within 100 m of Bottom
        public HomeVerdict Home;          // the easiest way down from Top through the plan graph (spec section 4.6)
        public HomeLevel Wanted;          // the level the knob asked for at this lift
        public int HomeRun;               // first run of the easiest descent (-1 none); the hub highlights the chain
        public double Score;              // selection score (spec section 4.4), logged
    }

    /// <summary>One plan graph edge. Kind 0 run, 1 lift, 2 cat track, 3 link.</summary>
    public struct PlanEdge { public int From, To; public byte Kind; public SlopeBand Band; public int Run; }

    public struct SiteVerdict
    {
        public byte Kind;                 // the v1 enum value (spec section 6.2); v2 keeps the v1 meaning
        public float MeanGrade, SteepShare, LargestBenchHa, BaseBenchHa;   // the inputs the line quotes
        public int PodsWithGreenHome;     // served pods whose lift's verdict meets its Wanted (Green or Blue at or under it; name kept, AR-13)
        public int PodsServed, Pods;      // pods with a lift (the line's denominator), all pods
    }

    /// <summary>v2 additions to AutoPlanResult (null in Runs mode). One object per scan, built on the worker.</summary>
    public sealed class ResortPlan
    {
        public PlanPad[] Pads = new PlanPad[0];
        public PlanPod[] Pods = new PlanPod[0];
        public PlanLift[] Lifts = new PlanLift[0];
        public int[] PodRuns = new int[0];    // run indices grouped by pod (PlanPod.RunFirst / RunCount)
        public int[] RunPod = new int[0];     // per AutoPlanResult.Runs index: pod, -1 = none
        public int[] RunLift = new int[0];    // per run: the lift whose corridor seeded it, -1 = none
        public PlanEdge[] Graph = new PlanEdge[0];   // nodes: 0..Pads-1 pads, then run ends; see Flow.cs
        public int DeadEnds;              // run bottoms with no downhill path to a way out (Flow.cs: lift bottoms, their pads, base pads with access)
        public float RunKm, HullKm2, RunKmPerKm2;
        public readonly float[] MountainMix = new float[5];   // the terrain's honest mix (run-km share by rated band), Flat..DoubleBlack
        public SiteVerdict Site;          // spec section 6.2
        public float TreeLineY = float.NaN;   // the value used (NaN = none known)
        public readonly int[] StageUnits = new int[SkiRunAutoPlanner.ResortStageCount];   // units per probe slot (stage-a-spec 1)
        // Probe timing per slot (additive to the spec: the planner records them as it runs; ms of all units, and the longest unit)
        public readonly float[] StageMs = new float[SkiRunAutoPlanner.ResortStageCount];
        public readonly float[] StageMaxMs = new float[SkiRunAutoPlanner.ResortStageCount];
        public ulong Fingerprint;         // over pads, pods, lifts, verdicts (AutoPlanResult.Fingerprint folds it in when non-zero)

        /// <summary>An independent copy (arrays and the nested per-pod / per-lift arrays are cloned), for AutoPlanResult.Snapshot.</summary>
        public ResortPlan Copy()
        {
            var c = new ResortPlan
            {
                Pads = (PlanPad[])Pads.Clone(),
                Pods = (PlanPod[])Pods.Clone(),
                Lifts = (PlanLift[])Lifts.Clone(),
                PodRuns = (int[])PodRuns.Clone(),
                RunPod = (int[])RunPod.Clone(),
                RunLift = (int[])RunLift.Clone(),
                Graph = (PlanEdge[])Graph.Clone(),
                DeadEnds = DeadEnds, RunKm = RunKm, HullKm2 = HullKm2, RunKmPerKm2 = RunKmPerKm2,
                Site = Site, TreeLineY = TreeLineY, Fingerprint = Fingerprint,
            };
            for (int i = 0; i < c.Pods.Length; i++)
                if (c.Pods[i].BandShare != null)
                    c.Pods[i].BandShare = (float[])c.Pods[i].BandShare.Clone();
            for (int i = 0; i < c.Lifts.Length; i++)
                if (c.Lifts[i].TowerXz != null)
                    c.Lifts[i].TowerXz = (float2[])c.Lifts[i].TowerXz.Clone();
            Array.Copy(MountainMix, c.MountainMix, MountainMix.Length);
            Array.Copy(StageUnits, c.StageUnits, StageUnits.Length);
            Array.Copy(StageMs, c.StageMs, StageMs.Length);
            Array.Copy(StageMaxMs, c.StageMaxMs, StageMaxMs.Length);
            return c;
        }
    }

    /// <summary>Cancel token polled once per planner unit (GladeCancel's shape, SkiGladePlanner.cs:91-96; a separate type because
    /// AutoGenBatch does not link SkiGladePlanner.cs). A canceled scan only truncates: its result is dropped, never shown.</summary>
    public sealed class AutoPlanCancel
    {
        private volatile bool m_R;
        public bool Requested => m_R;
        public void Request() => m_R = true;
    }

    /// <summary>An existing lift touching the region (census copy), planned around instead of replaced.</summary>
    public struct PinnedLift
    {
        public uint Key; public byte TypeIndex; public float3 Bottom, Top;   // the census row's stations (stub tips)
        public float2[] Chords;                                              // xz chord ends in pairs (LiftServedRule.Chord list slice)
        public bool Complete;
    }
}
