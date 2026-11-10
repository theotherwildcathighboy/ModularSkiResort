// Runs-first realizer: turns a runs-first plan (RunsFirstResult: centerlines, type names, a log) into what the Quick runs tool reads
// (AutoPlanResult + ResortPlan): outlines the game accepts, the band the game will show, lift rows the lift queue can build. It adds no
// planning and never moves a line to make it better. It narrows, trims, splits, extends a junction end by a few meters, or drops, and it
// reports every loss in a RealizeReport.
// Order (spec section 4): the lattice and the refusal ladder, the lifts, the pistes and graded pistes (the run that goes through a
// contact first), the cat tracks (each lift's traverse tree as chains, then the graded tracks, then the links), then groups, the
// graph and the verdicts.
// Pure: System, System.Collections.Generic, Unity.Mathematics and the mod's pure planner files. No use of the SkiRunAutoPlanner class:
// what the realizer needs from it is copied (spec section 9). Deterministic: no clock in any decision, no randomness, every order is
// the list order, then the index. Runs on the scan worker after the core. Polls input.Cancel once per element.
// Spec: docs/research/quickruns-v2/port/REALIZER-SPEC.md. Engine notes: EngineNotes/RunsFirstRealizer.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>What the realizer lost, kept and decided, as plain counters and lines. The test host prints it. The tool never reads it.</summary>
    public sealed class RealizeReport
    {
        // Pistes (runs that are not graded) and graded pistes (graded runs at or over the blue line along their path)
        public int PlannerRuns, GradedPisteSources, PisteAreas, GradedPisteAreas, PiecesLost;
        public readonly int[] PisteDrops = new int[(int)AutoReject.Count];   // whole sources with no area, by reason
        public double PisteKmPlanned, PisteKmKept, GradedKmPlanned, GradedKmKept;
        // Bands: PlannedBand x Band over the plain pistes (count and km), graded pistes by the band the game shows before grading
        public readonly int[,] BandTable = new int[5, 5];
        public readonly double[,] BandKm = new double[5, 5];
        public readonly int[] GradedShown = new int[5];
        public int NaturalToGroomed;
        public int FallbackRated;   // pistes the rater rated by its P90 fallback (a mask that is not connected)
        public int CrossesFallback; // pieces kept as a constant-width ribbon after the marched ring crossed itself
        // Tracks
        public int TrackSources, TrackChains, TrackAreas, TrackPiecesLost, TrackPitches, TrackPitchesOver;
        public readonly int[] TrackDrops = new int[(int)AutoReject.Count];
        public double TrackKmPlanned, TrackKmKept;
        // R2-r (DECISIONS 637): two chains in one corridor are one track. A chain with no own piece in the corridor of accepted tracks merges
        // into them (TrackMerged chains, TrackKmMerged their length). TrackKmShared: every chain's stretches in the gap of an accepted
        // track, which that track carries (built once). MergedRelations / MergedLinks: the merged chains' own planner relations and how
        // many of them are game links on the hosts
        public int TrackMerged, MergedRelations, MergedLinks, TrackSelfSplits, TrackSpurs;   // TrackSelfSplits: pieces split where the ribbon crosses itself (SelfSplit). TrackSpurs: MergedSpurs
        public double TrackKmShared, TrackKmMerged, TrackKmOwn;   // TrackKmOwn: the planned stretches of the kept pieces (TrackKmKept adds the reached ends)
        // Junctions
        public int ConvergeJunctions;   // pistes met in a converging line's path and made its partners (iteration 3, item 1)
        // Iteration 3, item 4 (plain pistes, by planned band): km that kept the band at full width, at 75 %, at 50 %, and that did not;
        // ring area and centerline length for the mean width as kept and at full width; km that agree with the planned band at full width
        public readonly double[,] KeepKm = new double[5, 4];
        public readonly double[] WidthM2 = new double[5], FullWidthM2 = new double[5], WidthLenM = new double[5];
        public double AgreeKmFull, AgreeKmKept, PlainKm;
        public int RemnantsKept, RemnantsDropped;   // cut pieces under 50 m down: kept (both ends connect), dropped (a free end) (iteration 3, item 3)
        public int PisteReaches;    // piste ends extended after the track pass to reach a related track or piste (iteration 3, item 2)
        public int TrackReaches;    // track ends extended to reach a related piste or track (iteration 2, item 6)
        public int Relations, RelationLinks, RelationVia, RelationMisses, Extensions, UnlinkedEnds, UnlinkedBlocked;   // RelationVia: linked through the area that cut the end
        // Served and dead ends
        public int UnservedAreas, DeadEnds;
        // Lifts
        public int Lifts, LiftsClear, SunkEnds, LotConflicts, BottomReach;
        // Iteration 3, item 5: lifts that wait for a hand fit (tower plan not Sound), by type index (chair, detachable, gondola, tram), and
        // the first reason of each: a span not clear, a tower over its cap, too steep, past its clearance, an edge slope, a sampler miss
        public readonly int[] HandFitByType = new int[4];
        public int HandFitSpans, HandFitOverCap, HandFitSteep, HandFitClearance, HandFitEdge, HandFitMiss;
        public bool NoWayToBottom;
        // R2-y: the way to the bottom on the realized plan, per lift (the game's link graph, no lift ride). Skis: a skier leaving the top
        // has an area under the served rule. Bottom: an area skied from the top ends in the base band. Planned: the planner's ToBottom.
        public bool[] LiftSkis = new bool[0], LiftBottom = new bool[0];
        public RunsFirstBottom[] LiftPlanned = new RunsFirstBottom[0];
        public int BottomWayDown, BottomPlanner, BottomRealized, BottomLost, BottomGained;
        public float BottomTop;   // the top of the base band (lattice heights)
        // Time (ms, a report only) and memory
        public double Ms, LatticeMs, LiftMs, PisteMs, TrackMs, GraphMs;
        public long LayerBytes;
        /// <summary>The print of the realizer state after each stage (port round 2b-f, RunsFirstPrint): lattice, classify, lifts, pistes,
        /// tracks, assemble. Parts: region stats, elements, lifts, pistes, tracks, result runs.</summary>
        public readonly List<RunsFirstPrint> Stages = new List<RunsFirstPrint>();
        /// <summary>One line per drop, lost piece, unlinked end, unserved area, dead end and lift flag.</summary>
        public readonly List<string> Lines = new List<string>();

        public int PisteDropCount
        {
            get
            {
                int s = 0;
                foreach (int v in PisteDrops)
                    s += v;
                return s;
            }
        }
    }

    public sealed partial class RunsFirstRealizer
    {
        // ---------------------------------------------------------------- constants (spec sections 2-8)
        public const float SampleM = 4f;                     // GradePath step (v2's SampleM)
        public const float MinHalfWidthM = 7.5f;             // half the 15 m pinch width
        public const float MergeGapM = PlanGeometry.MergeGapM, MergeTaperM = 30f, JunctionReachM = 20f, ExtendMaxM = 40f;
        public const float MinPieceM = 60f, MinPieceDropM = 15f, MinJoinedPieceM = 30f;
        public static readonly float[] NarrowShares = { 0.75f, 0.5f };   // band-keeping widths (iteration 3, item 4)
        public const float RemnantDropM = 50f;   // a cut piece under this drop is a remnant (iteration 3, item 3)
        public const float PinchWidthM = 15f, PinchShare = 0.30f, RetryShare = 0.7f, ExactGapShare = 0.8f;
        public const float GradedHalfWidthM = 7.5f;          // a graded piste is 15 m (its bench)
        public const float TrackHalfWidthM = 0.5f * GradeParams.NarrowTopWidthM, TrackNarrowHalfWidthM = 0.5f * GradeParams.NarrowMinTopWidthM;
        public const float TrackFreeM = MergeGapM + 5f, TrackMinPieceM = 30f, TrackReachM = 20f;
        public const float TrackStubM = 2f * GradeParams.NarrowTopWidthM;   // a shorter track piece is lost even when joined at both ends (a blob, not a track)
        public const float NearEdgeM = EdgeBlockM + TrackHalfWidthM + 2f;   // the band where the free tests check the region exactly
        public const float EdgeBlockM = 2f, RegionClearanceM = 0.25f, RingSimplifyM = 1f, WidthMarginM = 1f, MarchStepM = 1f;
        public const float CliffGrade = 1.0f;
        public const float RelationM = 40f, RelationRiseM = 5f;   // the planner's contact (RF/.Graph.cs:66-70)
        public const float JoinM = 1f, TreeLiftM = 150f;          // connector pieces that share an end form a tree, owned by the nearest lift top
        public const float MergeShare = 0.5f;   // R2-r: a chain with no own piece merges when at least this share of it lies in a corridor
        public const int SpurSegment = 1000;    // R2-r: a merged chain's spurs are segments 1000, 1001, ... (their stable ids)
        public const int MaxSelfSplits = 8;     // R2-r: a chain that crosses itself is split at most this often (SelfSplit)
        public const float DeliverM = 60f, DeliverDropM = 15f, RideDownM = 60f, NearTopM = 100f;
        public const float BottomShare = 0.15f, BottomMinM = 10f, BottomMaxM = 60f;   // the bottom band (plan 5.1, the v1 base band)
        public const float BlockedShare = 0.30f, PartlyBlockedShare = 0.60f;
        public const float SunkGrade = 0.25f, LiftEdgeSlopeShare = 0.97f;
        public const float PitchM = 30f, PitchGrade = 0.25f;
        public const float FairCrossM = 60f, FairCos = 0.70710678f;   // fair share: no crossing within 60 m, under 45 degrees
        /// <summary>The "other runs" group's toggle key (no lift to hash). Fixed, so the toggle survives a new scan.</summary>
        public const uint OtherGroupKey = 0x4F54524Eu;
        /// <summary>The band's hard ceiling the piste march stops at (v1 PassHardCeiling), by SlopeBand Green..DoubleBlack.</summary>
        public static readonly float[] HardCeiling = { SkiSlopeClassifier.GradeBlack, SkiSlopeClassifier.GradeDoubleBlack, 0.80f, CliffGrade };

        // ---------------------------------------------------------------- types

        private enum ElemType : byte { Piste, GradedPiste, Track }

        /// <summary>One planner element as the realizer sees it: a run (plain or graded piste, or a graded track), or a track chain rebuilt
        /// from connector pieces, or a link.</summary>
        private sealed class Elem
        {
            public int Id;
            public int Source;            // the run index, or -1 - the first connector index
            public ElemType Type;
            public List<float2> Pts;      // skiing direction
            public float3[] Samp;         // every 10 m with snapshot heights: the planner's contact geometry
            public float2 Lo, Hi;
            public SlopeBand Planned;
            public bool Groomed;
            public int Lift = -1;         // the group's lift (0-based), -1 = the last group
            public bool Link;             // a link or way home (no parent, last group)
            public bool Merged;           // R2-r: a chain in the corridor of accepted tracks. Areas holds those tracks (its hosts), not its own
            public int Spurs;             // R2-r: spurs built for a merged chain (MergedSpurs). They are its own areas
            public int Parent = -1;       // a chain's parent chain (element id)
            public string Kind;
            public float LengthM;
            public bool Done;             // the piste pass has realized (or dropped) it
            public readonly List<int> Areas = new List<int>();   // area ids in m_P (pistes) or m_T (tracks)
            public int StartArea = -1, EndArea = -1;              // the areas that hold the source's first and last point
        }

        /// <summary>An accepted area (or an existing cat track from the obstacle list).</summary>
        private sealed class Area
        {
            public int Elem = -1, Segment, Obstacle = -1;
            public bool Track, Existing, Graded;
            public float2[] Ring;
            public float3[] Nodes;
            public float2[] Line;         // the realized centerline, top to bottom
            public float2 Lo, Hi;
            public float GapM, LengthM, AreaM2, TopY, BottomY, DropM, RatingGrade, MeanGrade, SteepestGrade, EndY;
            public SlopeBand Band, Planned;
            public bool CrossesTrack;          // OverWater never: a wet texel blocks every area (spec 2)
            public int Out = -1;          // index in AutoPlanResult.Runs
            public List<int> J0 = new List<int>(), J1 = new List<int>();   // junction partners at the start / end (same type)
            public bool Orig0, Orig1;     // this area holds the source's start / end
            public int Unlinked0, Unlinked1;   // 0 linked or no junction, 1 unlinked, 2 unlinked because a blocked cell stopped the extension
            public int Chain = -1;        // tracks: the chain element
            public float PitchShare;      // tracks: share of 30 m pitches over 25 % on the raw ground
            public float[] Hl, Hr;        // pistes: the half widths per Line sample (iteration 3, item 2 extends an end with them)
            public PisteCtx Ctx;          // pistes: the context the area was built in
            public SlopeBand FullBand;    // plain pistes: the band at full width (iteration 3, item 4)
            public float FullAreaM2;      // plain pistes: the ring area at full width
            public int Keep;              // plain pistes: 0 the planned band at full width, 1 at 75 %, 2 at 50 %, 3 not kept
        }

        // ---------------------------------------------------------------- state (one realizer per call)

        private RunsFirstResult m_Plan;
        private AutoPlanInput m_In;
        private HeightSnapshot m_Snap;
        private RealizeReport m_Rep;
        private AutoPlanResult m_Res;
        private float2[] m_Region;
        private AutoDensity m_Density;
        private float m_HMin, m_BottomTop;
        private readonly List<Elem> m_E = new List<Elem>();
        private readonly List<Area> m_P = new List<Area>(), m_T = new List<Area>();
        private PlanLift[] m_Lifts = new PlanLift[0];
        private readonly SlopeGrid m_RateGrid = new SlopeGrid();
        private readonly SlopeResult m_RateRes = new SlopeResult();

        /// <summary>Realizes a runs-first plan. Pure and deterministic. report may be null.</summary>
        public static AutoPlanResult Realize(RunsFirstResult plan, AutoPlanInput input, RealizeReport report)
        {
            return new RunsFirstRealizer().Run(plan, input, report ?? new RealizeReport());
        }

        private bool Canceled => m_In.Cancel != null && m_In.Cancel.Requested;

        private AutoPlanResult Run(RunsFirstResult plan, AutoPlanInput input, RealizeReport rep)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            m_Plan = plan;
            m_In = input;
            m_Rep = rep;
            var res = m_Res = new AutoPlanResult { Status = AutoPlanStatus.Running };
            AutoPlanStatus s = Validate();
            if (s == AutoPlanStatus.Running)
            {
                s = BuildLattice();
                if (s == AutoPlanStatus.Running)
                {
                    MarkObstacles();
                    BuildGrade();
                    RegionStats();
                    rep.LayerBytes = LayerBytes;
                    if (res.OpenShare < BlockedShare)
                        s = AutoPlanStatus.Blocked;
                    else if (res.VerticalM < input.MinVerticalM)
                        s = AutoPlanStatus.NoVertical;
                    else if (res.OpenShare < PartlyBlockedShare)
                        res.Warnings |= AutoPlanWarning.PartlyBlocked;
                }
            }
            rep.LatticeMs = Ms(t0);
            Stage("lattice");
            if (s != AutoPlanStatus.Running || plan == null)
            {
                res.Status = s == AutoPlanStatus.Running ? AutoPlanStatus.BadInput : s;
                rep.Ms = Ms(t0);
                return res;
            }
            Classify();
            Stage("classify");
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            RealizeLifts();
            rep.LiftMs = Ms(t1);
            Stage("lifts");
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            RealizePistes();
            rep.PisteMs = Ms(t1);
            Stage("pistes");
            if (Canceled)
                return CanceledResult(t0);
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            RealizeTracks();
            rep.TrackMs = Ms(t1);
            Stage("tracks");
            if (Canceled)
                return CanceledResult(t0);
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            Assemble();
            rep.GraphMs = Ms(t1);
            Stage("assemble");
            res.Fingerprint = Hash(res);
            rep.Ms = Ms(t0);
            return res;
        }

        /// <summary>The print of the realizer state after one stage (RealizeReport.Stages, port round 2b-f). Parts: the region stats, the
        /// elements (type, band, samples), the lifts, the piste areas, the track areas, the result runs. One pass over the rings.</summary>
        private void Stage(string what)
        {
            var parts = new ulong[6];
            var counts = new int[6];
            var items = new ulong[6][];
            AutoPlanResult res = m_Res;
            ulong h = RunsFirstPrint.Seed;
            h = RunsFirstPrint.Mix(RunsFirstPrint.Mix(RunsFirstPrint.Mix(h, res.OpenShare), res.VerticalM), res.MeanGrade);
            h = RunsFirstPrint.Mix(RunsFirstPrint.Mix(RunsFirstPrint.Mix(h, res.UsableShare), res.CliffShare), m_BottomTop);
            parts[0] = h;
            var it = items[1] = new ulong[m_E.Count];
            for (int i = 0; i < it.Length; i++)
            {
                Elem e = m_E[i];
                h = RunsFirstPrint.Mix(RunsFirstPrint.Seed, (ulong)(uint)e.Source | ((ulong)e.Type << 32) | ((ulong)e.Planned << 40) | (e.Done ? 1UL << 48 : 0UL));
                h = RunsFirstPrint.Mix(h, (ulong)e.Areas.Count);
                if (e.Samp != null)
                    foreach (float3 p in e.Samp)
                        h = RunsFirstPrint.Mix(h, p);
                it[i] = h;
            }
            it = items[2] = new ulong[m_Lifts.Length];
            for (int i = 0; i < it.Length; i++)
            {
                PlanLift l = m_Lifts[i];
                h = RunsFirstPrint.Mix(RunsFirstPrint.Seed, (ulong)l.TypeIndex | ((ulong)(uint)l.Towers << 8) | (l.HandFit ? 1UL << 40 : 0UL));
                h = RunsFirstPrint.Mix(RunsFirstPrint.Mix(h, l.Bottom), l.Top);
                it[i] = RunsFirstPrint.Mix(h, l.TowerXz);
            }
            items[3] = AreaPrints(m_P);
            items[4] = AreaPrints(m_T);
            it = items[5] = new ulong[res.Runs.Count];
            for (int i = 0; i < it.Length; i++)
            {
                AutoRun r = res.Runs[i];
                it[i] = RunsFirstPrint.Mix(RunsFirstPrint.Mix(RunsFirstPrint.Seed, (ulong)(uint)r.Band | ((ulong)(uint)r.Kind << 8) | ((ulong)(uint)r.Source << 16)), r.Ring);
            }
            m_Rep.Stages.Add(RunsFirstPrint.Of(what, parts, counts, items));
        }

        private static ulong[] AreaPrints(List<Area> areas)
        {
            var it = new ulong[areas.Count];
            for (int i = 0; i < it.Length; i++)
            {
                Area a = areas[i];
                it[i] = RunsFirstPrint.Mix(RunsFirstPrint.Mix(RunsFirstPrint.Seed, (ulong)(uint)a.Elem | ((ulong)a.Band << 32) | ((ulong)(uint)a.Segment << 40)), a.Ring);
            }
            return it;
        }

        private AutoPlanResult CanceledResult(long t0)
        {
            m_Res.Canceled = true;
            m_Rep.Ms = Ms(t0);
            return m_Res;
        }

        private static double Ms(long t0) => (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>The first rungs of the v1 refusal ladder (copied from Begin / Validate): bad input, area.</summary>
        private AutoPlanStatus Validate()
        {
            AutoPlanInput input = m_In;
            if (input == null || input.Snapshot == null || !input.Snapshot.Valid || input.Region == null || input.Region.Length < 3)
                return AutoPlanStatus.BadInput;
            if (!RingFinite(input.Region) || float.IsNaN(input.MaxAreaM2) || float.IsNaN(input.MinAreaM2) || float.IsNaN(input.MinVerticalM))
                return AutoPlanStatus.BadInput;
            m_Snap = input.Snapshot;
            m_Region = input.Region;
            m_Density = input.Knobs.Density;
            double area = PlanGeometry.PolygonArea(m_Region);
            m_Res.RegionAreaM2 = (float)area;
            if (area > input.MaxAreaM2)
                return AutoPlanStatus.TooLarge;
            if (area < input.MinAreaM2 || area < 1.0)
                return AutoPlanStatus.TooSmall;
            return AutoPlanStatus.Running;
        }

        // ---------------------------------------------------------------- the elements (spec section 2)

        private void Classify()
        {
            RunsFirstResult plan = m_Plan;
            for (int r = 0; r < plan.Runs.Count; r++)
            {
                RunsFirstResult.Run run = plan.Runs[r];
                var e = new Elem
                {
                    Id = m_E.Count, Source = r, Pts = run.Pts, Kind = run.Kind, Groomed = run.Groomed,
                    Planned = run.Band == SlopeBand.Flat ? SlopeBand.Green : run.Band, Lift = run.Lift - 1,
                };
                Sample(e);
                if (!run.Graded)
                    e.Type = ElemType.Piste;
                else
                {
                    // a graded run is a piste when its own grade along the path is at or over the blue line and it drops (TrackClass,
                    // RF/.Graded.cs:92-98), else a cat track
                    float along = AlongGrade(e.Samp);
                    if (along >= SkiSlopeClassifier.GradeBlue && e.Samp[0].y > e.Samp[e.Samp.Length - 1].y)
                    {
                        e.Type = ElemType.GradedPiste;
                        e.Planned = SkiSlopeClassifier.BandOf(along);
                    }
                    else
                    {
                        e.Type = ElemType.Track;
                        e.Planned = SlopeBand.Green;
                    }
                }
                m_E.Add(e);
                if (e.Type == ElemType.Piste)
                {
                    m_Rep.PlannerRuns++;
                    m_Rep.PisteKmPlanned += e.LengthM / 1000.0;
                }
                else if (e.Type == ElemType.GradedPiste)
                {
                    m_Rep.GradedPisteSources++;
                    m_Rep.GradedKmPlanned += e.LengthM / 1000.0;
                }
                else
                {
                    m_Rep.TrackSources++;
                    m_Rep.TrackKmPlanned += e.LengthM / 1000.0;
                }
            }
            BuildChains();   // Tracks.cs: the traverse trees as chains, then the links
            m_Res.Candidates = m_E.Count;
        }

        private void Sample(Elem e)
        {
            List<float2> pl = e.Pts;
            var o = new List<float3>(pl.Count * 2);
            float len = 0f;
            for (int i = 0; i + 1 < pl.Count; i++)
            {
                float d = math.distance(pl[i], pl[i + 1]);
                len += d;
                int k = Math.Max(1, (int)Math.Ceiling(d / 10f));
                for (int j = 0; j < k; j++)
                    o.Add(At(math.lerp(pl[i], pl[i + 1], j / (float)k)));
            }
            if (pl.Count > 0)
                o.Add(At(pl[pl.Count - 1]));
            e.Samp = o.ToArray();
            e.LengthM = len;
            e.Lo = new float2(float.MaxValue);
            e.Hi = new float2(float.MinValue);
            foreach (float2 p in pl)
            {
                e.Lo = math.min(e.Lo, p);
                e.Hi = math.max(e.Hi, p);
            }
        }

        private float3 At(float2 p) => new float3(p.x, m_Snap.Sample(new float3(p.x, 0f, p.y)), p.y);

        private float H(float2 p) => m_Snap.Sample(new float3(p.x, 0f, p.y));

        /// <summary>The planner's along-path grade (RF/.Graded.cs AlongGrade, copied): the 85th percentile of the downhill grade over 20-30 m
        /// windows of the 10 m samples.</summary>
        private static float AlongGrade(float3[] p)
        {
            var cum = new float[p.Length];
            for (int k = 1; k < p.Length; k++)
                cum[k] = cum[k - 1] + math.distance(p[k].xz, p[k - 1].xz);
            var cs = new List<float>(p.Length);
            for (int k = 0, j = 0; k < p.Length; k++)
            {
                j = Math.Max(j, k);
                while (j < p.Length - 1 && cum[j] - cum[k] < 30f)
                    j++;
                if (cum[j] - cum[k] >= 20f)
                    cs.Add(Math.Max(0f, p[k].y - p[j].y) / (cum[j] - cum[k]));
            }
            cs.Sort();
            return cs.Count > 0 ? cs[(int)((cs.Count - 1) * 0.85f)] : 0f;
        }

        // ---------------------------------------------------------------- the planner's contact relation (RF/.Graph.cs:66-70)

        /// <summary>a ends onto b: a's last point lies within 40 m of a point of b no higher than that end + 5 m.</summary>
        private bool EndsOnto(Elem a, Elem b)
        {
            if (a == b || a.Samp.Length == 0 || b.Samp.Length == 0)
                return false;
            float3 end = a.Samp[a.Samp.Length - 1];
            if (math.any(end.xz < b.Lo - RelationM) || math.any(end.xz > b.Hi + RelationM))
                return false;
            foreach (float3 p in b.Samp)
                if (math.distancesq(p.xz, end.xz) < RelationM * RelationM && p.y <= end.y + RelationRiseM)
                    return true;
            return false;
        }

        /// <summary>a starts off b (a branches off b): a's first point lies within 40 m of a point of b, at most 5 m above it.</summary>
        private bool StartsOff(Elem a, Elem b)
        {
            if (a == b || a.Samp.Length == 0 || b.Samp.Length == 0)
                return false;
            float3 st = a.Samp[0];
            if (math.any(st.xz < b.Lo - RelationM) || math.any(st.xz > b.Hi + RelationM))
                return false;
            foreach (float3 p in b.Samp)
                if (math.distancesq(p.xz, st.xz) < RelationM * RelationM && st.y <= p.y + RelationRiseM)
                    return true;
            return false;
        }

        private bool Related(Elem a, Elem b) => EndsOnto(a, b) || StartsOff(a, b) || EndsOnto(b, a) || StartsOff(b, a);

        /// <summary>Section 4: pistes in list order, except that the run that goes through a contact comes before the run that ends or starts
        /// there (A ends onto B: B first; B branches off A: A first). A cycle is broken by list order.</summary>
        private List<Elem> PisteOrder()
        {
            var list = new List<Elem>();
            foreach (Elem e in m_E)
                if (e.Type != ElemType.Track)
                    list.Add(e);
            int n = list.Count;
            var before = new List<int>[n];   // before[a] = the elements that must come before a
            for (int a = 0; a < n; a++)
                before[a] = new List<int>();
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    if (a != b && (EndsOnto(list[a], list[b]) || StartsOff(list[a], list[b])))
                        before[a].Add(b);
            var done = new bool[n];
            var order = new List<Elem>(n);
            for (int step = 0; step < n; step++)
            {
                int pick = -1;
                for (int a = 0; a < n && pick < 0; a++)
                {
                    if (done[a])
                        continue;
                    bool ready = true;
                    foreach (int b in before[a])
                        if (!done[b])
                        {
                            ready = false;
                            break;
                        }
                    if (ready)
                        pick = a;
                }
                if (pick < 0)
                    for (int a = 0; a < n && pick < 0; a++)
                        if (!done[a])
                            pick = a;   // a cycle: list order
                done[pick] = true;
                order.Add(list[pick]);
            }
            return order;
        }

        // ---------------------------------------------------------------- the result (spec sections 5.5 kinds, 8.1-8.3)

        /// <summary>Kinds (spec 5.5): a natural run stays natural only when the rater says black or double black (and the knob allows
        /// natural), else it is groomed. Groomed pistes get snowmaking (greens that end in the bottom band) and night (at most two base
        /// pistes, lowest tops first) by the Kinds knob, as v1's AssignKinds. A graded piste is groomed. A track is a cat track.</summary>
        private void AssignKinds()
        {
            AutoKinds allowed = m_In.Knobs.Kinds == AutoKinds.None ? AutoKinds.Default : m_In.Knobs.Kinds;
            // A prepared piste under the knob: plain groomed when the knob has it, else with guns, else with guns and lights.
            bool plain = (allowed & AutoKinds.Groomed) != 0 || (allowed & (AutoKinds.Snowmaking | AutoKinds.Night)) == 0;
            bool gunsOnly = !plain && (allowed & AutoKinds.Snowmaking) != 0;
            RunKind prepared = PisteKind(true, !plain, !plain && !gunsOnly);
            List<AutoRun> runs = m_Res.Runs;
            var groomed = new List<AutoRun>();
            foreach (Area a in m_P)
            {
                AutoRun r = runs[a.Out];
                Elem e = m_E[a.Elem];
                bool natural = !a.Graded && !e.Groomed;
                if (natural && a.Band >= SlopeBand.Black && (allowed & AutoKinds.Natural) != 0)
                {
                    r.Kind = PisteKind(false, false, false);
                    continue;
                }
                if (natural)
                    m_Rep.NaturalToGroomed++;
                bool guns = r.Band <= SlopeBand.Green && r.TouchesBase && (allowed & AutoKinds.Snowmaking) != 0;
                r.Kind = guns ? PisteKind(true, true, false) : prepared;
                groomed.Add(r);
            }
            if ((allowed & AutoKinds.Night) == 0)
                return;
            for (int pick = 0; pick < 2; pick++)
            {
                AutoRun best = null;
                foreach (AutoRun r in groomed)
                {
                    if (!r.TouchesBase || r.Band > SlopeBand.Blue || RunFeatures.FromKind((int)r.Kind).Lights)
                        continue;
                    if (best == null || r.TopY < best.TopY || (r.TopY == best.TopY && r.Id < best.Id))
                        best = r;
                }
                if (best == null)
                    break;
                best.Kind = PisteKind(true, true, true);
            }
        }

        /// <summary>The realizer decides features (grooming, guns, lights). RunFeatures names the kind the plan carries (run types,
        /// DECISIONS 576: no kind is named here).</summary>
        private static RunKind PisteKind(bool groomed, bool guns, bool lights)
        {
            return RunFeatures.Of(RunBase.Piste, groomed, guns, lights).SnowKind;
        }

        private AutoRun ToRun(Area a, int id)
        {
            Elem e = m_E[a.Elem];
            return new AutoRun
            {
                Id = id, Band = a.Band, PassBand = a.Planned, Kind = RunFeatures.Of(a.Track ? RunBase.CatTrack : RunBase.Piste, true, false, false).SnowKind, Ring = a.Ring, Nodes = a.Nodes,
                Centerline = a.Line, RatingGrade = a.RatingGrade, MeanGrade = a.MeanGrade, SteepestGrade = a.SteepestGrade, DropM = a.DropM,
                AreaM2 = a.AreaM2, LengthM = a.LengthM, TopY = a.TopY, BottomY = a.BottomY,
                CrossesCatTrack = a.CrossesTrack, TouchesBase = a.EndY <= m_BottomTop,
                Graded = a.Graded || a.Track, PlannedBand = a.Planned, Source = e.Source, Segment = a.Segment,
            };
        }

        /// <summary>The stable id (spec 5.5): FNV of a kind tag, the source index, the segment, and the area's first and last centerline
        /// point quantized to 1 m.</summary>
        private static int IdOf(byte tag, int source, int segment, float2[] line)
        {
            uint h = 2166136261u;
            h = PlanGeometry.Fnv(h, tag);
            h = PlanGeometry.Fnv(h, (uint)source);
            h = PlanGeometry.Fnv(h, (uint)segment);
            float2 a = line[0], b = line[line.Length - 1];
            h = PlanGeometry.Fnv(h, (uint)(int)Math.Round(a.x));
            h = PlanGeometry.Fnv(h, (uint)(int)Math.Round(a.y));
            h = PlanGeometry.Fnv(h, (uint)(int)Math.Round(b.x));
            h = PlanGeometry.Fnv(h, (uint)(int)Math.Round(b.y));
            return (int)(h & 0x7FFFFFFF);
        }

        /// <summary>FNV-1a 64 over every output bit that matters: status, warnings, each run (id, band, kind, source, segment, ring bits),
        /// and the plan (lifts, groups, run groups, verdicts). The determinism gate compares it across two runs.</summary>
        public static ulong Hash(AutoPlanResult res)
        {
            ulong h = 14695981039346656037UL;
            void U(uint v)
            {
                for (int b = 0; b < 4; b++)
                {
                    h ^= (v >> (8 * b)) & 0xFFu;
                    h *= 1099511628211UL;
                }
            }
            void F(float f) => U(math.asuint(f));
            U((uint)res.Status);
            U((uint)res.Warnings);
            foreach (AutoRun r in res.Runs)
            {
                U((uint)r.Id);
                U((uint)r.Band);
                U((uint)r.PlannedBand);
                U((uint)r.Kind);
                U((uint)r.Source);
                U((uint)r.Segment);
                U(r.Unserved ? 1u : 0u);
                foreach (float2 p in r.Ring)
                {
                    F(p.x);
                    F(p.y);
                }
            }
            ResortPlan plan = res.Resort;
            if (plan != null)
            {
                foreach (PlanLift l in plan.Lifts)
                {
                    U(l.Key);
                    U(l.TypeIndex);
                    U((uint)l.Towers);
                    U((uint)l.Home);
                    U((uint)l.Serves);
                    U(l.TowersUnchecked ? 1u : 0u);
                }
                foreach (PlanPod p in plan.Pods)
                {
                    U(p.Key);
                    U((uint)p.Lift);
                    U((uint)p.PrimaryClass);
                }
                foreach (int v in plan.RunPod)
                    U((uint)v);
                U((uint)plan.DeadEnds);
                for (int b = 0; b < 5; b++)
                    F(plan.MountainMix[b]);
            }
            return h;
        }
    }
}
