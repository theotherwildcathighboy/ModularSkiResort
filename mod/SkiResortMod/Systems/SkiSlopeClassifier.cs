// Pure, deterministic slope rating of a ski-run polygon: RatingGrade = the crux of the easiest line (a minimax path between the end zones). A difficulty layer then moves the band.
// Entities-free on purpose. The usings are System, System.Collections.Generic and Unity.Mathematics ONLY. So the same file compiles
// into the net48 mod (default **/*.cs glob) AND links into the net8 headless test host tests/SlopeBatch.
// Contract: no static mutable state (consts only), never logs, never throws on bad input (every degenerate input returns a
// SlopeStatus and leaves the outputs cleared. A tampered grid cannot index out of range). Zero managed allocation per call once the
// caller's reused SlopeGrid / SlopeResult buffers have grown (no Sort, no LINQ).
// Cross-runtime bit equality (CoreCLR test host vs the game's Mono) is unverified. Within a runtime a replay is bit-identical.
// Engine notes: EngineNotes/SkiSlopeClassifier.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public enum SlopeBand : byte
    {
        Flat = 0,
        Green = 1,
        Blue = 2,
        Black = 3,
        DoubleBlack = 4,
    }

    /// <summary>Difficulty layer: why SlopeResult.Band differs from (or nearly differs from) BandOf(RatingGrade). None = the crux
    /// band stands. Capped = a black (double black) crux capped one band down by the steep-cell share. WideEase = a crux just above
    /// the green/blue boundary on a wide gentle run rounded down to green. NarrowKept = the cap WOULD have applied but
    /// the steep section is narrower than NarrowWidthM (the width decides). LongKept = the cap WOULD have applied but the crux pitch is at least
    /// CruxLengthForBlack long (the length decides). GreenMean = a black or double black crux on a run whose MEAN grade is green, rated blue.</summary>
    public enum SlopeNote : byte
    {
        None = 0,
        Capped = 1,
        WideEase = 2,
        NarrowKept = 3,
        LongKept = 4,
        GreenMean = 5,
    }

    public enum SlopeStatus
    {
        Ok,
        ShortRun,
        Polyline,
        TooFewPoints,
        NonFinite,
        NoCells,
        TooLarge,
        BadStep,
        BadHeights,
    }

    /// <summary>Lattice plan of one polygon. The caller reuses it. Buffers grow, never shrink.
    /// After Plan: world x of node (i, j) = Origin.x + (I0 + i)·Step, z = Origin.y + (J0 + j)·Step,
    /// flat index j·Nx + i.</summary>
    public sealed class SlopeGrid
    {
        public int I0, J0, Nx, Nz;              // lattice index of cell (0,0) and extent
        public float Step;
        public double2 Origin;
        public byte[] Mask;                     // 1 = cell center inside the polygon (ring mode)
        public byte[] Need;                     // 1 = the caller must fill Heights for this node
        public float[] Heights;                 // caller-filled, length >= Nx * Nz, index j * Nx + i
        public bool PolylineMode;
        public int InsideCells, NeedCells;
        public float3[] Points;                 // sanitized input copy (polyline mode reads it)
        public int PointCount;

        /// <summary>Connectivity of the crux pass, set by the
        /// CALLER (the classifier does not know the step multiple): true when the caller's step
        /// multiple is >= 2. false (default) = 8-neighbor connectivity.
        /// true = the four orthogonal neighbors plus a diagonal neighbor only when BOTH cells
        /// orthogonally shared between the two are outside the Mask. Plan leaves it untouched.
        /// Classify copies it into SlopeResult.DiagonalRule.</summary>
        public bool DiagonalRule;

        // Classifier-private state (not part of the shared contract).
        internal SlopeStatus PlanStatus = SlopeStatus.NoCells;
        internal double[] LocalU, LocalV;       // sanitized points in LOCAL lattice units (node (0,0) = 0)
        internal double[] Crossings;            // scanline scratch: every row's crossings, bucketed by row
        internal int[] CrossStart, CrossFill;   // scanline scratch: per-row offsets into Crossings / fill cursors
        internal double[] ArcLength;            // polyline cumulative arc length, meters

        // Per-row RUN LISTS (a per-row first/last span would bridge the outside gap of every row that crosses two arms of a concave
        // ring). Row j's runs are the inclusive column pairs (Runs[2k], Runs[2k + 1]) for k in [RowStart[j], RowStart[j + 1]). RowStart has Nz + 1 entries. Runs are EXACT, sorted, disjoint and non-adjacent (same for Need). Ring mode has Mask and
        // Need runs. Polyline mode has Need runs only. Consumers (Classify, SkiRunDrawFeedbackSystem's fill / strips / capture,
        // SlopeBatch) walk these, so managed work is O(Mask) / O(Need), never O(bbox): the bbox-sized memsets (Plan's Array.Clear,
        // Classify's FillOutside) excepted.
        internal int[] MaskRuns, MaskRowStart;
        internal int[] NeedRuns, NeedRowStart;
        internal int MaskRunCount, NeedRunCount;
        internal int[] NeedLo, NeedHi;          // polyline-mode marking scratch only: per row first/last Need column
        internal int[] MergeHead;               // ring-mode Need merge scratch (2·Dilation + 1 row cursors)
    }

    public sealed class SlopeResult
    {
        public SlopeStatus Status;
        public SlopeBand Band;  // THE rated band: BandOf(RatingGrade) moved by the difficulty layer (CruxBand, Note) in ring mode
        public float RatingGrade, MeanGrade, Drop, MinHeight, MaxHeight, Area, FallCoherence;
        public float2 FallLine;
        public readonly float[] BandShare = new float[5];
        public int ValidWindows;
        public byte[] CellBand;                 // per cell, SkiSlopeClassifier.BandOutside (255) outside the mask
        public int I0, J0, Nx, Nz;              // grid geometry copy for the overlay
        public float Step;
        public double2 Origin;
        public float[] SegmentGrade;            // polyline mode
        public byte[] SegmentBand;
        public int SegmentCount;

        // Ring mode: RatingGrade = the crux of the easiest line.
        public float[] CellGrade;               // per cell overlay value o, index j·Nx + i. Only Mask cells are meaningful
        public float SteepestGrade;             // k-th largest full 20 m window (ShortRun: k-th/last overlay value)
        public float P90Grade;                  // smallest bucket top with >= 90 % of inside cells at or below it
        public float CruxHeight;                // node height of the crux cell (valid when CruxValid)
        public bool Connected;                  // a top-zone -> bottom-zone path exists over Mask cells (DiagonalRule's connectivity)
        public bool CruxValid;                  // ring mode and Connected: CruxI/CruxJ name the crux cell
        public int CruxI, CruxJ;                // grid-local index of the crux cell
        public int TopZoneCells, BottomZoneCells;

        public bool ZoneByFallLine;  // ring mode: end zones ranked along the mean fall line (else by node height)
        public bool DiagonalRule;               // copy of SlopeGrid.DiagonalRule this classification used

        // Run length (HORIZONTAL map meters). Geometry only:
        // computed in Classify from the sanitized points and the end-zone key range, never read by
        // the rating.
        public float Length;                    // m, ring: max(LengthRect, AxisLength), polyline: arc length
        public float LengthRect;                // m, ring: (P + sqrt(max(0, P² − 16A))) / 4 over the sanitized ring (closing edge included), polyline 0
        public float AxisLength;                // m, ring: ZoneByFallLine ? fall-line key range of the inside cells + step : 0, polyline 0

        // The difficulty layer
        // on top of the crux. RatingGrade stays the raw crux (the oracle, "Hardest unavoidable pitch").
        // Band = the band the run is RATED (the catalog, the hub and the economy read it). Polyline mode,
        // a broken Mask (!Connected) and cleared results: CruxBand == Band, BandGrade == RatingGrade,
        // Note None. Evaluated in ring mode only when the crux band is Blue, Black or Double black.
        public SlopeBand CruxBand;              // BandOf(RatingGrade): the crux's own band
        public float BandGrade;                 // the grade Band reads: RatingGrade, or when the layer moved the band the crux limited to just below the band's ceiling (a DisplayBand input)
        public SlopeNote Note;                  // why Band moved / nearly moved (SlopeNote)
        public float SteepShare;                // black/double-black crux: share of inside cells at/above the band floor (40 / 60 %), unweighted, else 0
        public float SteepShareWeighted;        // the same with cells in a section narrower than NarrowWidthM counted twice
        public float CruxPitchM;                // black/double-black crux: estimated length of the crux pitch along the easiest line, m (CruxStretchM + PitchBlurM), else 0
        public float CruxWidthM;                // mean perpendicular extent over the crux stretch, m. -1 = not evaluated / no usable fall line

        // Classifier-private: the easiest line (cell indices, bottom zone -> top zone) the crux pass walked,
        // and the index of the crux cell on it. Grown, never shrunk.
        internal int[] Path;
        internal int PathLen, PathCrux;

        // Classifier-private: |Σg| / n, the mean box gradient's magnitude (ring mode) — the
        // end-zone key gate's input, kept so CruxForTest re-decides the key exactly as Classify.
        internal float MeanGradient;

        // Classifier-private top-k insertion buffers (descending), grown to k.
        internal float[] TopFull, TopCell;

        // Classifier-private crux-pass buffers (grow, never shrink): union-find / BFS state per
        // flat cell index plus the two virtual nodes TOP = Nx·Nz and BOTTOM = Nx·Nz + 1, inside
        // cells in bucket order, BFS queue, bucket bounds (RatingBuckets + 1), end-zone key
        // histogram, the neighbor offset tables of the current grid (see Crux).
        internal int[] Uf, Order, Queue, BucketBound, HeightHist, Nbr;
        internal float[] HeapKey;               // open last bucket: the heap's keys, parallel to its Order segment

        /// <summary>Resets every output (arrays keep their capacity. Nx = Nz = SegmentCount = 0
        /// marks their contents as void). A cleared result reads Status NoCells, never Ok.</summary>
        public void Clear()
        {
            Status = SlopeStatus.NoCells;
            Band = SlopeBand.Flat;
            CruxBand = SlopeBand.Flat;
            BandGrade = 0f;
            Note = SlopeNote.None;
            SteepShare = 0f;
            SteepShareWeighted = 0f;
            CruxPitchM = 0f;
            CruxWidthM = -1f;
            PathLen = 0;
            PathCrux = 0;
            RatingGrade = 0f;
            SteepestGrade = 0f;
            P90Grade = 0f;
            CruxHeight = 0f;
            Connected = false;
            CruxValid = false;
            CruxI = 0;
            CruxJ = 0;
            TopZoneCells = 0;
            BottomZoneCells = 0;
            ZoneByFallLine = false;
            DiagonalRule = false;
            Length = 0f;
            LengthRect = 0f;
            AxisLength = 0f;
            MeanGradient = 0f;
            MeanGrade = 0f;
            Drop = 0f;
            MinHeight = 0f;
            MaxHeight = 0f;
            Area = 0f;
            FallCoherence = 0f;
            FallLine = new float2(0f, 0f);
            for (int b = 0; b < BandShare.Length; b++)
                BandShare[b] = 0f;
            ValidWindows = 0;
            I0 = 0;
            J0 = 0;
            Nx = 0;
            Nz = 0;
            Step = 0f;
            Origin = new double2(0.0, 0.0);
            SegmentCount = 0;
        }
    }

    public static class SkiSlopeClassifier
    {
        public const byte BandOutside = 255;

        // Bands: Flat < 6 % <= Green < 25 % <= Blue < 40 % <= Black < 60 % <= Double black. Half-open: band = number of thresholds <=
        // grade. 60 % for double black is our choice (no numeric convention exists).
        public const float GradeGreen = 0.06f, GradeBlue = 0.25f, GradeBlack = 0.40f, GradeDoubleBlack = 0.60f;
        public const int BaseValueFlat = 0, BaseValueGreen = 6, BaseValueBlue = 12, BaseValueBlack = 20, BaseValueDoubleBlack = 28;

        // Crux end zones (key, size and cap: EngineNotes/SkiSlopeClassifier.md). RatingBuckets = the counting-sort resolution of the
        // per-cell values: bucket = min(2047, (int)(o·1024)), i.e. 1/1024 = 0.098 % grade wide up to 200 %.
        // Difficulty layer, applied AFTER the crux (see FinishBand). The reason for each constant is in the same note:
        //  SteepShareForBlack, DoubleBlackShare  steep-cell share below which a black / double black crux is capped one band
        //  CruxLengthForBlack, PitchBlurM        a pitch this long (measured as stretch + blur) is never capped
        //  CapMaxOverFloor                       only a BORDERLINE band is capped (crux < floor + 10 pp)
        //  NarrowWidthM                          a steep section narrower than this counts double and is never capped
        //  WideRunM, WideEase                    a wide run with a crux just above the green/blue boundary rounds down to green
        public const float SteepShareForBlack = 0.15f, DoubleBlackShare = 0.10f, CruxLengthForBlack = 40f, PitchBlurM = 20f;
        public const float CapMaxOverFloor = 0.10f, NarrowWidthM = 20f, WideRunM = 60f, WideEase = 0.02f;
        //  GreenMeanKeepsBlueMax 25 %  a run whose MEAN grade is below this (the green band, the classifier's own green/blue
        //                           boundary) rates at most BLUE however short and steep its crux, unless the unavoidable pitch
        //                           is CruxLengthForBlack or longer: a cat track is narrow and has a short kick. It is not a
        //                           black diamond. The share, margin and width rules do not apply to it.
        public const float GreenMeanKeepsBlueMax = GradeBlue;

        public const float EndZoneShare = 0.10f, EndZoneMaxM2 = 2000f;
        public const float EndZoneDepthM = 20f, EndZoneMinMeanGrade = 0.03f, EndZoneMinCoherence = 0.5f;
        public const int RatingBuckets = 2048;
        private const float BucketsPerUnit = 1024f;           // buckets per 1.0 of grade
        private const int HeightBins = 1024;                  // end-zone key histogram
        private const float OneBinRange = 1e-3f;              // m, key range below this -> one bin
        private const int Inactive = 0;                       // union-find state in Uf (see Crux)
        // BFS state in Uf (see Crux): Unvisited | UnvisitedTop / UnvisitedBottom = traversable, not
        // yet reached, in the top / bottom zone (the zone bits of its Order entry). Every value
        // below UnvisitedLimit is unvisited. Inactive, -1 (seed) and predecessors (> 0) are not.
        private const int Unvisited = int.MinValue, UnvisitedTop = 1, UnvisitedBottom = 2, UnvisitedLimit = int.MinValue + 4;
        private const int TopBit = 1 << 29, BottomBit = 1 << 30, CellMask = TopBit - 1;   // Order entries (see Crux)
        private const int ZoneShift = 29;                     // (entry >> ZoneShift) & 3 = UnvisitedTop | UnvisitedBottom bits

        public const float WindowLength = 20f, RatingAreaM2 = 60f, DisplayHysteresis = 0.005f;
        // MinRatingCells = 7 (5 does not hold): measured in SlopeBatch (SPIKE lines, ±3 m single
        // texel on 10.5/20/32.5 % planes, 180 orientations × 2 offsets × 5 spots), worst shift at
        // m = 1: k=5 +1.96 % (315 of 10800 cases >= 1 %), k=6 +1.14 % (59), k=7 +0.40 % (0).
        // m = 2 stays <= 0.23 % for k = 5..7. A texel spike perturbs the 5×5 box direction of the
        // cells around it. So up to ~6 cells' windows reach it, not the 4 of the bilinear footprint.
        // CellBudget 16384: every run up to 200,704 m² (e.g. 2.5 km × 80 m) rates at m = 1. At 4096 every run over 50,176 m² fell to
        // m = 2 and narrow banks rated up to 14.7 % low.
        public const int MinRatingCells = 7, CellBudget = 16384, Dilation = 3;

        private const int CellBudgetDown = 12288;             // StepMultiple hysteresis: back down only below this (3/4 of CellBudget)
        private const float MaxStep = 100f;                   // m, larger -> BadStep
        private const long MaxBboxCells = 1L << 21;           // larger -> TooLarge
        private const long MaxCrossings = MaxBboxCells;       // scanline row crossings (8 B each, retained). More than bbox cells -> TooLarge (a ring zigzagging across thousands of rows)
        private const double MaxLatticeIndex = 1.0e9;         // |index| beyond this -> TooLarge (int safety)
        private const double DuplicateEpsSq = 1e-6;           // 1 mm in xz
        private const double MinRingArea = 1.0;               // m²
        private const float FlatGradient = 1e-6f;             // |g| below this -> fallback direction (1, 0)
        // Polyline k = max(PolylineMinWindows, ceil(8 m / ds)). 11, not the spec's original 3: windows are
        // sampled every half cell, and a texel's bilinear support crossed diagonally spans up to
        // 2√2 cells. So a spike touches up to ~6 window starts AND ~6 window ends. With the
        // absolute drop both ends raise a near-contour line. Measured (SlopeBatch SPIKE-POLY,
        // m = 1): min 5 -> worst +6.54 % with 202 band flips, 7 -> +3.32 %, 9 -> +0.89 %,
        // 11 -> +0.13 %.
        private const int PolylineMinWindows = 11;
        private const float PolylineShareM = 8f;
        private const int MaxStepMultiple = 1 << 20;

        /// <summary>Half-open band of a grade (rise/run fraction). NaN and negatives are Flat.</summary>
        public static SlopeBand BandOf(float grade)
        {
            int b = 0;
            if (grade >= GradeGreen)
                b++;
            if (grade >= GradeBlue)
                b++;
            if (grade >= GradeBlack)
                b++;
            if (grade >= GradeDoubleBlack)
                b++;
            return (SlopeBand)b;
        }

        /// <summary>Display rule of the tooltip's "Steepest patch (avoidable)" line. It is kept here, pure and Entities-free, so the net8 test
        /// host can sweep it (SlopeBatch TooltipLineTests). Shown only when ALL hold:
        /// (a) Connected (with a broken Mask the rating is the P90 fallback, not a crux, so no patch can be called "avoidable" relative to it),
        /// (b) both grades finite,
        /// (c) SteepestGrade >= RatingGrade + DisplayHysteresis (0.5 %): on a uniform plane height quantization alone puts SteepestGrade up
        /// to +0.29 % above the crux,
        /// (d) the two rounded percentages differ (the tooltip shows round(100·g)), so the line never
        /// repeats the crux line's number,
        /// (e) BandOf(SteepestGrade) is above the SHOWN band.
        /// Polyline mode never shows it (the
        /// feedback system publishes SteepestGrade == RatingGrade there, which fails (c)).</summary>
        public static bool SteepestPatchShown(float ratingGrade, float steepestGrade, SlopeBand shownBand, bool connected)
        {
            if (!connected || !IsFinite(ratingGrade) || !IsFinite(steepestGrade))
                return false;
            if (!(steepestGrade >= ratingGrade + DisplayHysteresis))
                return false;
            if ((int)math.round(100f * steepestGrade) <= (int)math.round(100f * ratingGrade))
                return false;
            return (int)BandOf(steepestGrade) > (int)shownBand;
        }

        /// <summary>Base value of a band. Unknown values -> 0.</summary>
        public static int BaseValue(SlopeBand band)
        {
            switch (band)
            {
                case SlopeBand.Green:
                    return BaseValueGreen;
                case SlopeBand.Blue:
                    return BaseValueBlue;
                case SlopeBand.Black:
                    return BaseValueBlack;
                case SlopeBand.DoubleBlack:
                    return BaseValueDoubleBlack;
                default:
                    return BaseValueFlat;
            }
        }

        /// <summary>Step multiple m (step = m·texel) for a polygon of the given shoelace area.
        /// Without a current value (current &lt; 1): the smallest m >= 1 with area/(m·texel)² <=
        /// CellBudget. With one: go UP (to that smallest m) only when the current m would exceed
        /// CellBudget estimated cells. Go DOWN only to an m whose estimate is below CellBudgetDown
        /// (12288). Never
        /// sized from the bbox. Invalid area or texel -> current (or 1).</summary>
        public static int StepMultiple(double areaM2, float texel, int current)
        {
            int keep = current >= 1 ? current : 1;
            if (!(texel > 0f) || float.IsInfinity(texel) || !(areaM2 >= 0.0) || double.IsInfinity(areaM2))
                return keep;
            int up = SmallestMultiple(areaM2, texel, CellBudget, false);
            if (current < 1)
                return up;
            if (EstimatedCells(areaM2, texel, current) > CellBudget)
                return up;
            int down = SmallestMultiple(areaM2, texel, CellBudgetDown, true);
            return down < current ? down : current;
        }

        private static double EstimatedCells(double areaM2, float texel, int m)
        {
            double s = (double)m * texel;
            return areaM2 / (s * s);
        }

        private static int SmallestMultiple(double areaM2, float texel, int budget, bool strict)
        {
            double guess = math.ceil(math.sqrt(areaM2 / budget) / texel);
            int m = guess < 1.0 ? 1 : guess > MaxStepMultiple ? MaxStepMultiple : (int)guess;
            // Correct float edge effects of the closed form in both directions.
            while (m > 1 && Fits(EstimatedCells(areaM2, texel, m - 1), budget, strict))
                m--;
            while (m < MaxStepMultiple && !Fits(EstimatedCells(areaM2, texel, m), budget, strict))
                m++;
            return m;
        }

        private static bool Fits(double cells, int budget, bool strict)
        {
            return strict ? cells < budget : cells <= budget;
        }


        public static SlopeStatus Plan(float3[] points, int count, double2 latticeOrigin, float step, SlopeGrid grid)
        {
            if (grid == null)
                return SlopeStatus.NoCells; // never throw, nothing to plan into
            ResetGrid(grid, latticeOrigin, step);
            SlopeStatus status = PlanCore(points, count, latticeOrigin, step, grid);
            if (status != SlopeStatus.Ok)
                ResetGrid(grid, latticeOrigin, step);
            grid.PlanStatus = status;
            return status;
        }

        private static void ResetGrid(SlopeGrid grid, double2 origin, float step)
        {
            grid.I0 = 0;
            grid.J0 = 0;
            grid.Nx = 0;
            grid.Nz = 0;
            grid.Step = step;
            grid.Origin = origin;
            grid.PolylineMode = false;
            grid.InsideCells = 0;
            grid.NeedCells = 0;
            grid.PointCount = 0;
            grid.MaskRunCount = 0;
            grid.NeedRunCount = 0;
        }

        private static SlopeStatus PlanCore(float3[] points, int count, double2 origin, float step, SlopeGrid grid)
        {
            if (points == null || count < 1)
                return SlopeStatus.TooFewPoints;
            int n = count < points.Length ? count : points.Length;
            for (int k = 0; k < n; k++)
            {
                float3 p = points[k];
                if (!IsFinite(p.x) || !IsFinite(p.y) || !IsFinite(p.z))
                    return SlopeStatus.NonFinite;
            }
            if (!(step > 0f) || step > MaxStep)
                return SlopeStatus.BadStep; // also NaN and +Inf
            if (!IsFinite(origin.x) || !IsFinite(origin.y))
                return SlopeStatus.NonFinite;

            // ---- sanitize into grid.Points
            Grow(ref grid.Points, n);
            float3[] pts = grid.Points;
            int m = 0;
            for (int k = 0; k < n; k++)
            {
                float3 p = points[k];
                if (m > 0 && DistSqXz(pts[m - 1], p) < DuplicateEpsSq)
                    continue;
                pts[m++] = p;
            }
            while (m > 1 && DistSqXz(pts[m - 1], pts[0]) < DuplicateEpsSq)
                m--;
            grid.PointCount = m;
            if (m < 2)
                return SlopeStatus.TooFewPoints;

            // ---- lattice coordinates (double, world-anchored)
            Grow(ref grid.LocalU, m);
            Grow(ref grid.LocalV, m);
            double[] u = grid.LocalU, v = grid.LocalV;
            double sd = step;
            double umin = double.MaxValue, umax = double.MinValue, vmin = double.MaxValue, vmax = double.MinValue;
            for (int k = 0; k < m; k++)
            {
                double uk = (pts[k].x - origin.x) / sd;
                double vk = (pts[k].z - origin.y) / sd;
                u[k] = uk;
                v[k] = vk;
                if (uk < umin) umin = uk;
                if (uk > umax) umax = uk;
                if (vk < vmin) vmin = vk;
                if (vk > vmax) vmax = vk;
            }
            if (!(math.abs(umin) < MaxLatticeIndex) || !(math.abs(umax) < MaxLatticeIndex) ||
                !(math.abs(vmin) < MaxLatticeIndex) || !(math.abs(vmax) < MaxLatticeIndex))
                return SlopeStatus.TooLarge;
            long i0 = (long)math.floor(umin) - Dilation - 1;
            long i1 = (long)math.floor(umax) + Dilation + 2;
            long j0 = (long)math.floor(vmin) - Dilation - 1;
            long j1 = (long)math.floor(vmax) + Dilation + 2;
            long nxl = i1 - i0 + 1, nzl = j1 - j0 + 1;
            if (nxl * nzl > MaxBboxCells)
                return SlopeStatus.TooLarge;

            // ---- ring or polyline (shoelace in lattice units, relative to point 0)
            bool ring = false;
            if (m >= 3)
            {
                double s2 = 0.0;
                for (int k = 0; k < m; k++)
                {
                    int l = k + 1 < m ? k + 1 : 0;
                    double ax = u[k] - u[0], az = v[k] - v[0], bx = u[l] - u[0], bz = v[l] - v[0];
                    s2 += ax * bz - bx * az;
                }
                ring = math.abs(0.5 * s2) * sd * sd >= MinRingArea;
            }

            int nx = (int)nxl, nz = (int)nzl, total = nx * nz;
            grid.I0 = (int)i0;
            grid.J0 = (int)j0;
            grid.Nx = nx;
            grid.Nz = nz;
            grid.PolylineMode = !ring;
            for (int k = 0; k < m; k++)
            {
                u[k] -= i0;
                v[k] -= j0;
            }

            Grow(ref grid.Mask, total);
            Grow(ref grid.Need, total);
            Grow(ref grid.Heights, total);
            Array.Clear(grid.Mask, 0, total);
            Array.Clear(grid.Need, 0, total);
            Grow(ref grid.MaskRowStart, nz + 1);
            Grow(ref grid.NeedRowStart, nz + 1);

            if (ring)
            {
                int inside = Rasterize(grid, m);
                if (inside < 0)
                    return SlopeStatus.TooLarge;   // more than MaxCrossings row crossings
                grid.InsideCells = inside;
                if (inside == 0)
                    return SlopeStatus.NoCells;
                BuildRingNeedRuns(grid);
            }
            else
            {
                int[] ms = grid.MaskRowStart;
                for (int j = 0; j <= nz; j++)
                    ms[j] = 0;   // polyline mode: no Mask run
                grid.MaskRunCount = 0;
                MarkPolylineNeed(grid, m);
            }

            // Count Need and poison every Need height: a node the caller forgets to fill reads
            // as NonFinite instead of rating garbage. The Need runs keep this O(Need), not
            // O(bbox) (and not O(row spans), which bridge the outside gap of concave rings).
            float[] h = grid.Heights;
            byte[] need = grid.Need;
            int[] runs = grid.NeedRuns, rs = grid.NeedRowStart;
            int needCount = 0;
            for (int j = 0; j < nz; j++)
            {
                int row = j * nx;
                for (int k = rs[j]; k < rs[j + 1]; k++)
                {
                    for (int i = runs[2 * k]; i <= runs[2 * k + 1]; i++)
                    {
                        need[row + i] = 1;
                        h[row + i] = float.NaN;
                    }
                    needCount += runs[2 * k + 1] - runs[2 * k] + 1;
                }
            }
            grid.NeedCells = needCount;
            return SlopeStatus.Ok;
        }

        /// <summary>Even-odd scanline per lattice row (local v = j exactly) in double. A node is inside when it lies in [x_2k, x_2k+1]
        /// of the sorted crossings. It records each row's inside runs into grid.MaskRuns (two consecutive pairs that meet at one node are
        /// merged, so the runs are disjoint and non-adjacent). Returns the inside count, or -1 when the polygon has more than
        /// MaxCrossings row crossings (Plan: TooLarge).
        /// Edge bucketing: row j crosses edge (k, l) iff min(v_k, v_l) <= j < max(v_k, v_l), so each edge visits rows ceil(min) ..
        /// ceil(max) - 1. A count pass, a prefix sum and a fill pass bucket every crossing by row. Then each row is insertion-sorted
        /// (no Sort). Testing every edge against every row cost 0.42 ms of Plan for a 662-vertex C-shaped run on CoreCLR. The Mask
        /// does not depend on the bucketing (a sorted list of equal values is the same list whatever the insertion order).</summary>
        private static int Rasterize(SlopeGrid grid, int m)
        {
            double[] u = grid.LocalU, v = grid.LocalV;
            int nx = grid.Nx, nz = grid.Nz;
            byte[] mask = grid.Mask;
            Grow(ref grid.CrossStart, nz + 1);
            Grow(ref grid.CrossFill, nz);
            int[] cs = grid.CrossStart, fill = grid.CrossFill;
            for (int j = 0; j <= nz; j++)
                cs[j] = 0;
            long totalCross = 0;
            for (int k = 0; k < m; k++)
            {
                int l = k + 1 < m ? k + 1 : 0;
                CrossedRows(v[k], v[l], nz, out int ja, out int jb);
                for (int j = ja; j <= jb; j++)
                    cs[j + 1]++;
                if (jb >= ja)
                    totalCross += jb - ja + 1;
            }
            if (totalCross > MaxCrossings)
                return -1;
            for (int j = 0; j < nz; j++)
            {
                cs[j + 1] += cs[j];
                fill[j] = cs[j];
            }
            Grow(ref grid.Crossings, (int)totalCross);
            double[] xs = grid.Crossings;
            for (int k = 0; k < m; k++)
            {
                int l = k + 1 < m ? k + 1 : 0;
                double va = v[k], vb = v[l];
                CrossedRows(va, vb, nz, out int ja, out int jb);
                for (int j = ja; j <= jb; j++)
                {
                    double row = j;
                    xs[fill[j]++] = u[k] + (row - va) * (u[l] - u[k]) / (vb - va);
                }
            }

            int[] rowStart = grid.MaskRowStart;
            int inside = 0, runCount = 0;
            for (int j = 0; j < nz; j++)
            {
                rowStart[j] = runCount;
                int rowFirst = runCount;
                int c0 = cs[j], c = cs[j + 1] - c0;
                // insertion sort of this row's bucket (few crossings per row, no Sort)
                for (int s = c0 + 1; s < c0 + c; s++)
                {
                    double x = xs[s];
                    int p = s;
                    while (p > c0 && xs[p - 1] > x)
                    {
                        xs[p] = xs[p - 1];
                        p--;
                    }
                    xs[p] = x;
                }
                int rowBase = j * nx;
                for (int k = c0; k + 1 < c0 + c; k += 2)
                {
                    double a = math.ceil(xs[k]), b = math.floor(xs[k + 1]);
                    int ia = a < 0.0 ? 0 : a > nx - 1 ? nx : (int)a;
                    int ib = b > nx - 1 ? nx - 1 : b < 0.0 ? -1 : (int)b;
                    if (ia > ib)
                        continue;
                    for (int i = ia; i <= ib; i++)
                    {
                        int q = rowBase + i;
                        if (mask[q] == 0)
                        {
                            mask[q] = 1;
                            inside++;
                        }
                    }
                    if (runCount > rowFirst && ia <= grid.MaskRuns[2 * runCount - 1] + 1)
                    {
                        if (ib > grid.MaskRuns[2 * runCount - 1])
                            grid.MaskRuns[2 * runCount - 1] = ib;   // shares / touches the previous run
                    }
                    else
                    {
                        GrowKeep(ref grid.MaskRuns, 2 * runCount + 2);
                        grid.MaskRuns[2 * runCount] = ia;
                        grid.MaskRuns[2 * runCount + 1] = ib;
                        runCount++;
                    }
                }
            }
            rowStart[nz] = runCount;
            grid.MaskRunCount = runCount;
            return inside;
        }

        /// <summary>The lattice rows j in [0, nz - 1] an edge from local v = va to v = vb crosses
        /// under the half-open rule (va > j) != (vb > j): min <= j < max, i.e. j from
        /// ceil(min) to ceil(max) - 1 (exact for integer j). jb < ja = none (also a horizontal
        /// edge, va == vb).</summary>
        private static void CrossedRows(double va, double vb, int nz, out int ja, out int jb)
        {
            double lo = va < vb ? va : vb, hi = va < vb ? vb : va;
            double a = math.ceil(lo), b = math.ceil(hi) - 1.0;
            if (a < 0.0) a = 0.0;
            if (b > nz - 1) b = nz - 1;
            if (!(a <= b))
            {
                ja = 0;
                jb = -1;
                return;
            }
            ja = (int)a;
            jb = (int)b;
        }

        /// <summary>Ring-mode Need runs = the Mask dilated by Dilation (Chebyshev), row by row:
        /// row j's Need is the union over rows j - Dilation .. j + Dilation of their Mask runs
        /// widened by Dilation on each side (the Chebyshev dilation of a row run is exactly that
        /// rectangle). Each source row's runs are sorted, so a (2·Dilation + 1)-way merge by
        /// run start emits row j's union in ascending order. The merge combines overlapping and adjacent
        /// intervals: O(7·runs) per row, no Sort, never O(bbox). Plan writes the Need bytes from
        /// these runs afterwards.</summary>
        private static void BuildRingNeedRuns(SlopeGrid grid)
        {
            int nx = grid.Nx, nz = grid.Nz;
            int[] mRuns = grid.MaskRuns, mStart = grid.MaskRowStart, nStart = grid.NeedRowStart;
            Grow(ref grid.MergeHead, 2 * Dilation + 1);
            int[] head = grid.MergeHead;
            int runCount = 0;
            for (int j = 0; j < nz; j++)
            {
                nStart[j] = runCount;
                int j0 = j - Dilation < 0 ? 0 : j - Dilation;
                int j1 = j + Dilation > nz - 1 ? nz - 1 : j + Dilation;
                for (int r = j0; r <= j1; r++)
                    head[r - j0] = mStart[r];
                bool open = false;
                int curA = 0, curB = 0;
                while (true)
                {
                    int best = -1, bestA = int.MaxValue;
                    for (int r = j0; r <= j1; r++)
                    {
                        int hk = head[r - j0];
                        if (hk < mStart[r + 1] && mRuns[2 * hk] < bestA)
                        {
                            bestA = mRuns[2 * hk];
                            best = r;
                        }
                    }
                    if (best < 0)
                        break;
                    int bestB = mRuns[2 * head[best - j0] + 1];
                    head[best - j0]++;
                    int a = bestA - Dilation, b = bestB + Dilation;
                    if (a < 0) a = 0;                 // never bites after Plan (bbox padded by Dilation + 1)
                    if (b > nx - 1) b = nx - 1;
                    if (open && a <= curB + 1)
                    {
                        if (b > curB)
                            curB = b;
                    }
                    else
                    {
                        if (open)
                            AppendRun(ref grid.NeedRuns, ref runCount, curA, curB);
                        curA = a;
                        curB = b;
                        open = true;
                    }
                }
                if (open)
                    AppendRun(ref grid.NeedRuns, ref runCount, curA, curB);
            }
            nStart[nz] = runCount;
            grid.NeedRunCount = runCount;
        }

        private static void AppendRun(ref int[] runs, ref int runCount, int a, int b)
        {
            GrowKeep(ref runs, 2 * runCount + 2);
            runs[2 * runCount] = a;
            runs[2 * runCount + 1] = b;
            runCount++;
        }

        /// <summary>Polyline Need: walk every segment at <= 0.5 cell spacing and mark the
        /// (2·Dilation+1)² box around each sample's rounded node. Any point on a segment is
        /// within 0.25 cell of a sample, so every bilinear read (floor, floor + 1) is covered.
        /// Then each row's exact Need runs are read back from its marked first..last span.
        /// A polyline crossing a row twice bridges the gap here, once, in Plan only.</summary>
        private static void MarkPolylineNeed(SlopeGrid grid, int m)
        {
            int nx = grid.Nx, nz = grid.Nz;
            Grow(ref grid.NeedLo, nz);
            Grow(ref grid.NeedHi, nz);
            int[] lo = grid.NeedLo, hi = grid.NeedHi;
            for (int j = 0; j < nz; j++)
            {
                lo[j] = int.MaxValue;
                hi[j] = -1;
            }
            double[] u = grid.LocalU, v = grid.LocalV;
            byte[] need = grid.Need;
            for (int k = 0; k + 1 < m; k++)
            {
                double du = u[k + 1] - u[k], dv = v[k + 1] - v[k];
                double len = math.sqrt(du * du + dv * dv);
                int samples = (int)math.ceil(len / 0.5);
                if (samples < 1)
                    samples = 1;
                for (int c = 0; c <= samples; c++)
                {
                    double t = (double)c / samples;
                    int ci = (int)math.floor(u[k] + du * t + 0.5);
                    int cj = (int)math.floor(v[k] + dv * t + 0.5);
                    int ja = cj - Dilation < 0 ? 0 : cj - Dilation;
                    int jb = cj + Dilation > nz - 1 ? nz - 1 : cj + Dilation;
                    int ia = ci - Dilation < 0 ? 0 : ci - Dilation;
                    int ib = ci + Dilation > nx - 1 ? nx - 1 : ci + Dilation;
                    if (ia > ib)
                        continue;
                    for (int j = ja; j <= jb; j++)
                    {
                        int r = j * nx;
                        for (int i = ia; i <= ib; i++)
                            need[r + i] = 1;
                        if (ia < lo[j]) lo[j] = ia;
                        if (ib > hi[j]) hi[j] = ib;
                    }
                }
            }
            int[] nStart = grid.NeedRowStart;
            int runCount = 0;
            for (int j = 0; j < nz; j++)
            {
                nStart[j] = runCount;
                int r = j * nx;
                int i = lo[j];
                while (i <= hi[j])
                {
                    if (need[r + i] == 0)
                    {
                        i++;
                        continue;
                    }
                    int a = i;
                    while (i + 1 <= hi[j] && need[r + i + 1] != 0)
                        i++;
                    AppendRun(ref grid.NeedRuns, ref runCount, a, i);
                    i++;
                }
            }
            nStart[nz] = runCount;
            grid.NeedRunCount = runCount;
        }


        public static SlopeStatus Classify(SlopeGrid grid, SlopeResult result)
        {
            return Classify(grid, result, EndZoneShare, EndZoneMaxM2, EndZoneDepthM, EndZoneMinMeanGrade, EndZoneMinCoherence);
        }

        /// <summary>Test-only overload (tests/SlopeBatch: sensitivity table, the curved-run
        /// report rows under the height key via endZoneMinMeanGrade = endZoneMinCoherence = +inf,
        /// the key-gate rows with the coherence clause off (+inf), the random oracle sweep. The
        /// host links this file, so `internal` is visible there). The mod calls the public
        /// overload, which passes the shipped constants.</summary>
        internal static SlopeStatus Classify(SlopeGrid grid, SlopeResult result, float endZoneShare, float endZoneMaxM2,
                                             float endZoneDepthM, float endZoneMinMeanGrade, float endZoneMinCoherence)
        {
            if (result == null)
                return SlopeStatus.NoCells; // never throw, nowhere to write
            result.Clear();
            SlopeStatus pre = Validate(grid);
            if (pre != SlopeStatus.Ok)
            {
                result.Status = pre;
                return pre;
            }

            result.DiagonalRule = grid.DiagonalRule;
            result.I0 = grid.I0;
            result.J0 = grid.J0;
            result.Nx = grid.Nx;
            result.Nz = grid.Nz;
            result.Step = grid.Step;
            result.Origin = grid.Origin;
            Grow(ref result.CellBand, grid.Nx * grid.Nz);
            Grow(ref result.CellGrade, grid.Nx * grid.Nz);

            SlopeStatus status = grid.PolylineMode
                ? ClassifyPolyline(grid, result)
                : ClassifyRing(grid, result, endZoneShare, endZoneMaxM2, endZoneDepthM, endZoneMinMeanGrade, endZoneMinCoherence);
            if (status != SlopeStatus.Ok && status != SlopeStatus.ShortRun && status != SlopeStatus.Polyline)
            {
                result.Clear();
            }
            else if (grid.PolylineMode)
            {
                // A line has exactly one path, so its k-th window statistic IS its crux.
                result.SteepestGrade = result.RatingGrade;
                result.P90Grade = result.RatingGrade;
                result.Connected = true;
                result.CruxBand = result.Band;  // no lattice, no difficulty layer
                result.BandGrade = result.RatingGrade;
            }
            result.Status = status;
            return status;
        }

        /// <summary>Test-only: re-runs the crux pass alone on a result that a ring-mode Classify
        /// of the SAME grid just produced (SlopeBatch times the crux share of Classify with it).
        /// Idempotent with the parameters of that Classify: it recomputes exactly what Classify
        /// wrote (the end-zone key gate reads the MeanGradient, FallCoherence and FallLine Classify
        /// stored). Returns false (and touches nothing) when the pair does not look like that.</summary>
        internal static bool CruxForTest(SlopeGrid grid, SlopeResult result, float endZoneShare, float endZoneMaxM2,
                                         float endZoneDepthM, float endZoneMinMeanGrade, float endZoneMinCoherence)
        {
            if (grid == null || result == null || grid.PolylineMode || Validate(grid) != SlopeStatus.Ok)
                return false;
            if (result.Status != SlopeStatus.Ok && result.Status != SlopeStatus.ShortRun)
                return false;
            int total = grid.Nx * grid.Nz;
            if (result.Nx != grid.Nx || result.Nz != grid.Nz || result.CellBand == null || result.CellBand.Length < total ||
                result.CellGrade == null || result.CellGrade.Length < total)
                return false;
            result.DiagonalRule = grid.DiagonalRule;
            Crux(grid, result, endZoneShare, endZoneMaxM2, endZoneDepthM, endZoneMinMeanGrade, endZoneMinCoherence);
            FinishBand(grid, result);
            return true;
        }

        private static SlopeStatus Validate(SlopeGrid grid)
        {
            if (grid == null)
                return SlopeStatus.NoCells;
            if (grid.PlanStatus != SlopeStatus.Ok)
                return grid.PlanStatus;
            if (!(grid.Step > 0f) || grid.Step > MaxStep)
                return SlopeStatus.BadStep;
            int nx = grid.Nx, nz = grid.Nz;
            // A planned grid is always >= 2·Dilation + 3 wide. Anything smaller was tampered with.
            if (nx < 2 * Dilation + 3 || nz < 2 * Dilation + 3 || (long)nx * nz > MaxBboxCells)
                return SlopeStatus.NoCells;
            int total = nx * nz;
            if (grid.Mask == null || grid.Mask.Length < total || grid.Need == null || grid.Need.Length < total)
                return SlopeStatus.NoCells;
            if (grid.PolylineMode && (grid.Points == null || grid.LocalU == null || grid.LocalV == null ||
                                      grid.PointCount < 2 || grid.PointCount > grid.Points.Length ||
                                      grid.PointCount > grid.LocalU.Length || grid.PointCount > grid.LocalV.Length))
                return SlopeStatus.TooFewPoints;
            if (!RunsValid(grid.MaskRuns, grid.MaskRowStart, nx, nz) || !RunsValid(grid.NeedRuns, grid.NeedRowStart, nx, nz))
                return SlopeStatus.NoCells;
            float[] h = grid.Heights;
            if (h == null || h.Length < total)
                return SlopeStatus.BadHeights;
            byte[] need = grid.Need;
            int[] runs = grid.NeedRuns, rs = grid.NeedRowStart;
            for (int j = 0; j < nz; j++)
            {
                int row = j * nx;
                for (int k = rs[j]; k < rs[j + 1]; k++)
                {
                    for (int i = runs[2 * k]; i <= runs[2 * k + 1]; i++)
                    {
                        if (need[row + i] != 0 && !IsFinite(h[row + i]))
                            return SlopeStatus.NonFinite;
                    }
                }
            }
            return SlopeStatus.Ok;
        }

        /// <summary>Run-list structure check (a tampered grid must not index out of range or
        /// visit a cell twice). RowStart has Nz + 1 non-decreasing entries from 0 within Runs.
        /// Every run lies in [0, Nx - 1] with start <= end. A row's runs are strictly
        /// ascending and disjoint. O(Nz + runs).</summary>
        private static bool RunsValid(int[] runs, int[] rowStart, int nx, int nz)
        {
            if (rowStart == null || rowStart.Length < nz + 1 || rowStart[0] != 0)
                return false;
            int count = rowStart[nz];
            if (count < 0 || (count > 0 && (runs == null || runs.Length < 2L * count)))
                return false;
            for (int j = 0; j < nz; j++)
            {
                int k0 = rowStart[j], k1 = rowStart[j + 1];
                if (k0 > k1 || k1 > count)
                    return false;
                int prevB = -2;
                for (int k = k0; k < k1; k++)
                {
                    int a = runs[2 * k], b = runs[2 * k + 1];
                    if (a <= prevB || a > b || a < 0 || b > nx - 1)
                        return false;
                    prevB = b;
                }
            }
            return true;
        }

        /// <summary>k for ring mode: max(MinRatingCells, ceil(RatingAreaM2 / step²)).</summary>
        private static int RingK(float step)
        {
            double k = math.ceil((double)RatingAreaM2 / ((double)step * step));
            int ki = k > 1.0e6 ? 1000000 : (int)k;
            return ki > MinRatingCells ? ki : MinRatingCells;
        }

        private static SlopeStatus ClassifyRing(SlopeGrid grid, SlopeResult result, float endZoneShare, float endZoneMaxM2,
                                                float endZoneDepthM, float endZoneMinMeanGrade, float endZoneMinCoherence)
        {
            int nx = grid.Nx, nz = grid.Nz;
            float s = grid.Step;
            float[] h = grid.Heights;
            byte[] mask = grid.Mask;
            byte[] cellBand = result.CellBand;
            float[] cellGrade = result.CellGrade;

            int k = RingK(s);
            Grow(ref result.TopFull, k);
            Grow(ref result.TopCell, k);
            float[] topFull = result.TopFull, topCell = result.TopCell;
            int topFullN = 0, topCellN = 0;

            float halfFull = 0.5f * WindowLength / s;                 // cells
            int checksFull = Checks(WindowLength, s);
            // Shorter centered windows (overlay only), longest first, never shorter than 2 steps.
            float l1 = 15f, l2 = 10f, l3 = 7f;
            bool use1 = l1 >= 2f * s, use2 = l2 >= 2f * s, use3 = l3 >= 2f * s;
            float half1 = 0.5f * l1 / s, half2 = 0.5f * l2 / s, half3 = 0.5f * l3 / s;
            int checks1 = Checks(l1, s), checks2 = Checks(l2, s), checks3 = Checks(l3, s);

            float inv50 = 1f / (50f * s);
            int nx2 = 2 * nx, nx3 = 3 * nx;
            double sumAbs = 0.0, sumGx = 0.0, sumGz = 0.0;
            float hmin = float.MaxValue, hmax = float.MinValue;
            int n = 0, valid = 0, c0 = 0, c1 = 0, c2 = 0, c3 = 0, c4 = 0;

            FillOutside(cellBand, nx * nz);

            // Plan never puts a Mask cell within Dilation of the border. The clamps below only
            // make a tampered grid unable to index out of range (such cells stay BandOutside).
            // Walks the Mask RUNS (Validate checked their structure): O(inside), not O(row span).
            int[] mRuns = grid.MaskRuns, mStart = grid.MaskRowStart;
            for (int j = Dilation; j < nz - Dilation; j++)
            {
                int row = j * nx;
                for (int rk = mStart[j]; rk < mStart[j + 1]; rk++)
                {
                    int lo = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                    int hi = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                    for (int i = lo; i <= hi; i++)
                    {
                        int q = row + i;
                        if (mask[q] == 0)
                            continue;
                        n++;
                        float hq = h[q];
                        if (hq < hmin) hmin = hq;
                        if (hq > hmax) hmax = hq;

                        // 5×5 box of central differences, telescoped per row / column.
                        float ax = 0f, az = 0f;
                        for (int b = -2; b <= 2; b++)
                        {
                            int r = q + b * nx;
                            ax += h[r + 3] + h[r + 2] - h[r - 2] - h[r - 3];
                            int c = q + b;
                            az += h[c + nx3] + h[c + nx2] - h[c - nx2] - h[c - nx3];
                        }
                        float gx = ax * inv50, gz = az * inv50;
                        float gm = math.sqrt(gx * gx + gz * gz);
                        sumAbs += gm;
                        sumGx += gx;
                        sumGz += gz;
                        float dx = 1f, dz = 0f;
                        if (gm >= FlatGradient)
                        {
                            dx = -gx / gm;
                            dz = -gz / gm;
                        }

                        float o = Window(h, mask, nx, nz, i, j, dx, dz, halfFull, checksFull, WindowLength);
                        if (o >= 0f)
                        {
                            valid++;
                            Push(topFull, ref topFullN, k, o);
                        }
                        else
                        {
                            if (use1)
                                o = Window(h, mask, nx, nz, i, j, dx, dz, half1, checks1, l1);
                            if (o < 0f && use2)
                                o = Window(h, mask, nx, nz, i, j, dx, dz, half2, checks2, l2);
                            if (o < 0f && use3)
                                o = Window(h, mask, nx, nz, i, j, dx, dz, half3, checks3, l3);
                            if (o < 0f)
                                o = gm;
                        }
                        Push(topCell, ref topCellN, k, o);
                        cellGrade[q] = o;
                        SlopeBand band = BandOf(o);
                        cellBand[q] = (byte)band;   // 0..4: also the crux pass's "processed inside cell" marker
                        switch (band)
                        {
                            case SlopeBand.Flat: c0++; break;
                            case SlopeBand.Green: c1++; break;
                            case SlopeBand.Blue: c2++; break;
                            case SlopeBand.Black: c3++; break;
                            default: c4++; break;
                        }
                    }
                }
            }

            if (n == 0)
                return SlopeStatus.NoCells;

            // SteepestGrade ("the steepest patch, avoidable or not").
            SlopeStatus status;
            float steepest;
            if (valid >= k)
            {
                steepest = topFull[k - 1];
                status = SlopeStatus.Ok;
            }
            else
            {
                steepest = topCell[(k < n ? k : n) - 1];
                status = SlopeStatus.ShortRun;
            }

            result.SteepestGrade = steepest;
            result.ValidWindows = valid;
            result.MeanGrade = (float)(sumAbs / n);
            result.MinHeight = hmin;
            result.MaxHeight = hmax;
            result.Drop = hmax - hmin;
            result.Area = n * s * s;
            float inv = 1f / n;
            result.BandShare[0] = c0 * inv;
            result.BandShare[1] = c1 * inv;
            result.BandShare[2] = c2 * inv;
            result.BandShare[3] = c3 * inv;
            result.BandShare[4] = c4 * inv;
            double fx = -sumGx, fz = -sumGz;
            double fl = math.sqrt(fx * fx + fz * fz);
            if (fl > 1e-12)
            {
                result.FallLine = new float2((float)(fx / fl), (float)(fz / fl));
                double coh = sumAbs > 0.0 ? fl / sumAbs : 0.0;
                result.FallCoherence = (float)(coh > 1.0 ? 1.0 : coh);
            }
            result.MeanGradient = (float)(fl / n);   // |ḡ| = |Σg| / n, the end-zone key gate. NaN only for tampered heights

            // The crux (needs MinHeight / MaxHeight, CellGrade, CellBand, and the FINAL FallLine,
            // MeanGradient and FallCoherence: the end-zone key gate reads them, so they are set
            // above on every path).
            Crux(grid, result, endZoneShare, endZoneMaxM2, endZoneDepthM, endZoneMinMeanGrade, endZoneMinCoherence);
            FinishBand(grid, result);  // Band = BandOf(RatingGrade) moved by the difficulty layer

            // Run length = max(equivalent rectangle, fall-line extent). Reads only the sanitized points and AxisLength (set by Crux).
            // It writes only the three length fields. No rating output depends on them.
            float rect = (float)EquivalentRectLength(grid.Points, grid.PointCount);
            result.LengthRect = rect;
            result.Length = rect > result.AxisLength ? rect : result.AxisLength;
            return status;
        }

        /// <summary>The centerline length of the rectangle with the ring's perimeter P and area A, (P + sqrt(max(0, P² − 16A))) / 4,
        /// horizontal meters (xz only). P includes the closing edge explicitly (Plan stripped the duplicated closing vertex, so
        /// pts[m - 1] → pts[0] is an edge here). A = |shoelace| relative to point 0, in double. Exact for an L × W rectangle and for
        /// any constant-width band (mitered polygonal bends, circular arcs). Shapes far from a band clamp the root at 0 (a circle reads
        /// P / 4). Returns 0 for fewer than 3 points, a short buffer (tampered grid) or a non-finite result.</summary>
        private static double EquivalentRectLength(float3[] pts, int m)
        {
            if (pts == null || m < 3 || m > pts.Length)
                return 0.0;
            double x0 = pts[0].x, z0 = pts[0].z;
            double per = 0.0, s2 = 0.0;
            for (int k = 0; k < m; k++)
            {
                int l = k + 1 < m ? k + 1 : 0;
                double ax = pts[k].x - x0, az = pts[k].z - z0, bx = pts[l].x - x0, bz = pts[l].z - z0;
                double dx = bx - ax, dz = bz - az;
                per += math.sqrt(dx * dx + dz * dz);
                s2 += ax * bz - bx * az;
            }
            double area = math.abs(0.5 * s2);
            double disc = per * per - 16.0 * area;
            if (!(disc > 0.0))
                disc = 0.0;
            double len = (per + math.sqrt(disc)) * 0.25;
            return IsFinite(len) ? len : 0.0;
        }


        /// <summary>RatingGrade = min over all connected Mask paths (8-neighbor, or the diagonal rule when result.DiagonalRule) from the
        /// top end zone to the bottom end zone of the max per-cell value o along the path. It uses a counting sort into RatingBuckets
        /// buckets and a union-find with TOP / BOTTOM virtual nodes. Then a BFS finds the crux cell. Also P90Grade, Connected, the zone
        /// sizes and ZoneByFallLine. "Inside cell" = a cell the per-cell loop processed (CellBand != BandOutside). Bucket 2047 is OPEN
        /// (o >= 199.9 %): its cells are activated in ascending o (binary min-heap) so RatingGrade is the exact minimax. End zones, the
        /// Uf encoding and the dilated-runs initialization: EngineNotes/SkiSlopeClassifier.md ("Near Crux").</summary>
        private static void Crux(SlopeGrid grid, SlopeResult result, float endZoneShare, float endZoneMaxM2,
                                 float endZoneDepthM, float endZoneMinMeanGrade, float endZoneMinCoherence)
        {
            int nx = grid.Nx, nz = grid.Nz, total = nx * nz;
            float[] h = grid.Heights;
            byte[] band = result.CellBand;
            float[] cg = result.CellGrade;
            int[] mRuns = grid.MaskRuns, mStart = grid.MaskRowStart;
            Grow(ref result.Uf, total + 2);
            Grow(ref result.BucketBound, RatingBuckets + 1);
            Grow(ref result.HeightHist, HeightBins);
            int[] uf = result.Uf, bound = result.BucketBound, hist = result.HeightHist;
            Array.Clear(bound, 0, RatingBuckets + 1);
            Array.Clear(hist, 0, HeightBins);
            result.PathLen = 0;

            // ---- end-zone key (Crux summary "END ZONES"). The fall-line key is
            // computed inline per cell by ZoneKey (no per-cell buffer). It is linear in i along a
            // row and float rounding is monotone. So its range over the inside cells is the range
            // over the clamped Mask runs' END cells (O(runs)). The SAME ZoneKey / HeightBin call
            // feeds the histogram (pass 1) and the zone bits (pass 2). The BFS reads those bits.
            float s = grid.Step;
            float2 fall = result.FallLine;
            float flx = fall.x, flz = fall.y;
            // Gate: a usable axis when the
            // mean box gradient is >= 3 % OR the box gradients agree (|Σg| / Σ|g| >= 0.5, a long
            // gentle run). NaN on either side compares false -> height.
            bool byFall = (flx != 0f || flz != 0f) &&
                          (result.MeanGradient >= endZoneMinMeanGrade || result.FallCoherence >= endZoneMinCoherence);
            result.ZoneByFallLine = byFall;
            float hmin, range;
            double keyLength = 0.0;
            if (byFall)
            {
                float kLo = float.MaxValue, kHi = float.MinValue;
                for (int j = Dilation; j < nz - Dilation; j++)
                {
                    for (int rk = mStart[j]; rk < mStart[j + 1]; rk++)
                    {
                        int lo = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                        int hi = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                        if (lo > hi)
                            continue;
                        float ka = ZoneKey(flx, flz, lo, j, s), kb = ZoneKey(flx, flz, hi, j, s);
                        if (ka < kLo) kLo = ka;
                        if (ka > kHi) kHi = ka;
                        if (kb < kLo) kLo = kb;
                        if (kb > kHi) kHi = kb;
                    }
                }
                hmin = kLo;
                range = kHi - kLo;
                keyLength = ((double)kHi - kLo) + s;  // L = key range of the inside cells + step
            }
            else
            {
                hmin = result.MinHeight;
                range = result.MaxHeight - result.MinHeight;
            }
            // The run's extent along the mean fall line, the
            // same L as the depth cap (an output only, nothing below reads it). 0 on the height key.
            result.AxisLength = byFall ? (float)keyLength : 0f;
            int bins = range >= OneBinRange ? HeightBins : 1;   // also 1 for a NaN range (tampered heights)
            float binScale = bins > 1 ? HeightBins / range : 0f;

            // ---- pass 1 (Mask runs): union-find init, count, histogram. Uf is set to Inactive
            // over every Mask run DILATED BY ONE CELL (rows j - 1 .. j + 1, columns lo - 1 ..
            // hi + 1), so every 8-neighbor of an inside cell holds fresh state and the later
            // passes test Uf alone (no CellBand read per neighbor). Cost: O(3·inside + 6·runs).
            int n = 0;
            for (int j = Dilation; j < nz - Dilation; j++)
            {
                int row = j * nx;
                for (int rk = mStart[j]; rk < mStart[j + 1]; rk++)
                {
                    int lo = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                    int hi = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                    if (lo > hi)
                        continue; // a run entirely in the clamped border (tampered grids only)
                    for (int jj = row - nx; jj <= row + nx; jj += nx)
                        for (int i = lo - 1; i <= hi + 1; i++)
                            uf[jj + i] = Inactive;
                    for (int i = lo; i <= hi; i++)
                    {
                        int q = row + i;
                        if (band[q] == BandOutside)
                            continue;
                        n++;
                        bound[BucketOf(cg[q])]++;
                        hist[HeightBin(byFall ? ZoneKey(flx, flz, i, j, s) : h[q], hmin, binScale, bins)]++;
                    }
                }
            }
            if (n == 0)
                return; // cannot happen: ClassifyRing returns NoCells first

            // ---- bucket ends (exclusive prefix sums) and P90 in the same walk
            int run = 0, p90Bucket = RatingBuckets - 1;
            bool p90Found = false;
            long need90 = 9L * n;                                // 10·count >= 9·n  <=>  count >= 90 % of n
            for (int b = 0; b < RatingBuckets; b++)
            {
                run += bound[b];
                bound[b] = run;
                if (!p90Found && 10L * run >= need90)
                {
                    p90Bucket = b;
                    p90Found = true;
                }
            }
            bound[RatingBuckets] = run;
            result.P90Grade = (p90Bucket + 1) / BucketsPerUnit;   // exact in float, saturates at 2.0 (200 %)

            // ---- end zones: whole key bins from each end until >= z cells. Cap: the depth cap
            // floor(n·EndZoneDepthM / L) on the fall-line key, the area cap on the height key.
            int z = ZoneSize(n, endZoneShare, byFall ? DepthCap(n, endZoneDepthM, keyLength) : AreaCap(s, endZoneMaxM2));
            int topBin = bins, topCells = 0;
            while (topBin > 0 && topCells < z)
            {
                topBin--;
                topCells += hist[topBin];
            }
            int bottomBin = -1, bottomCells = 0;
            while (bottomBin < bins - 1 && bottomCells < z)
            {
                bottomBin++;
                bottomCells += hist[bottomBin];
            }
            result.TopZoneCells = topCells;
            result.BottomZoneCells = bottomCells;

            // ---- pass 2 (Mask runs): counting-sort placement. Afterwards bound[b] = start of
            // bucket b and bound[b + 1] = its end (in-bucket order is irrelevant to the result
            // below bucket 2047. The last bucket is ordered by the heap below).
            // Each entry carries its cell's zone membership in bits 29 / 30 (flat indices are
            // < MaxBboxCells + 2 < 2^29). This sequential scan reads the heights cache-friendly.
            // The bucket-ordered union-find below would read them at random.
            Grow(ref result.Order, n);
            int[] order = result.Order;
            for (int j = Dilation; j < nz - Dilation; j++)
            {
                int row = j * nx;
                for (int rk = mStart[j]; rk < mStart[j + 1]; rk++)
                {
                    int lo = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                    int hi = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                    for (int i = lo; i <= hi; i++)
                    {
                        int q = row + i;
                        if (band[q] == BandOutside)
                            continue;
                        int zb = HeightBin(byFall ? ZoneKey(flx, flz, i, j, s) : h[q], hmin, binScale, bins);   // = pass 1's bin
                        int entry = q | (zb >= topBin ? TopBit : 0) | (zb <= bottomBin ? BottomBit : 0);
                        order[--bound[BucketOf(cg[q])]] = entry;
                    }
                }
            }

            // ---- union-find, bucket by bucket (ascending). Stop at the first connecting bucket. Activation turns Inactive into a
            // size-1 root and joins the cell to its ACTIVE inside neighbors under the grid's connectivity rule.
            //  * 8-neighbor (DiagonalRule false): the neighbors form a cycle (NW N NE E SE S SW W). Consecutive ones are
            //    8-adjacent to each other. So by induction on activation order every run of consecutive active neighbors is already
            //    one set: one union per run (usually one instead of up to eight. The union-find is most of this pass's cost).
            //  * DiagonalRule (step multiple >= 2): N W E S, plus a diagonal only when both cells it shares orthogonally with q are
            //    outside the Mask. Two orthogonal neighbors of q are NOT adjacent under this rule, so the run trick does not hold:
            //    one union per allowed active neighbor.
            Grow(ref result.Nbr, 32);
            int[] nbr = result.Nbr;                 // [0..7] row-major (BFS), [8..15] cyclic (union-find), [16..31] DiagonalRule (below)
            nbr[0] = -nx - 1;
            nbr[1] = -nx;
            nbr[2] = -nx + 1;
            nbr[3] = -1;
            nbr[4] = 1;
            nbr[5] = nx - 1;
            nbr[6] = nx;
            nbr[7] = nx + 1;
            nbr[8] = -nx - 1;
            nbr[9] = -nx;
            nbr[10] = -nx + 1;
            nbr[11] = 1;
            nbr[12] = nx + 1;
            nbr[13] = nx;
            nbr[14] = nx - 1;
            nbr[15] = -1;
            // DiagonalRule: for row-major neighbor e, the two cells it shares orthogonally with q
            // (nbr[16 + e], nbr[24 + e]), 0 for the orthogonal neighbors (always allowed).
            for (int e = 16; e < 32; e++)
                nbr[e] = 0;
            nbr[16] = -nx; nbr[24] = -1;            // NW: N and W
            nbr[18] = -nx; nbr[26] = 1;             // NE: N and E
            nbr[21] = nx; nbr[29] = -1;             // SW: S and W
            nbr[23] = nx; nbr[31] = 1;              // SE: S and E
            bool diagonalRule = result.DiagonalRule;
            int top = total, bottom = total + 1;
            uf[top] = -1;
            uf[bottom] = -1;
            int connecting = -1;
            const int last = RatingBuckets - 1;
            int heapEnd = bound[RatingBuckets];     // last bucket only: its popped (activated or tied) cells are in order[heapEnd .. n)
            float exactRating = float.NaN;
            float[] key = null;
            for (int b = 0; b < RatingBuckets && connecting < 0; b++)
            {
                int start = bound[b], end = bound[b + 1];
                if (start == end)
                    continue;
                bool exact = b == last;             // the open bucket [199.9 %, inf): ascending o, test per cell (Crux summary)
                if (exact)
                {
                    // Keys copied once into a contiguous array parallel to the segment, so the heap
                    // never reads CellGrade at random (halves the cost on a cliff face).
                    Grow(ref result.HeapKey, end - start);
                    key = result.HeapKey;
                    for (int k = start; k < end; k++)
                        key[k - start] = ValueKey(cg[order[k] & CellMask]);
                    HeapifyByKey(order, key, start, end);
                }
                for (int k = start; k < end; k++)
                {
                    int entry;
                    if (exact)
                    {
                        entry = PopMinByKey(order, key, start, heapEnd);
                        heapEnd--;
                    }
                    else
                    {
                        entry = order[k];
                    }
                    int q = entry & CellMask;
                    uf[q] = -1;
                    if (!diagonalRule)
                    {
                        bool prevActive = uf[q + nbr[15]] != Inactive;
                        bool linked = false;
                        for (int e = 8; e < 16; e++)
                        {
                            int r = q + nbr[e];
                            bool active = uf[r] != Inactive;
                            if (active && !prevActive)
                            {
                                Union(uf, q, r); // first cell of a run
                                linked = true;
                            }
                            prevActive = active;
                        }
                        if (!linked && prevActive)
                            Union(uf, q, q + nbr[8]); // all eight active: one run without a start
                    }
                    else
                    {
                        for (int e = 0; e < 8; e++)
                        {
                            int r = q + nbr[e];
                            if (uf[r] == Inactive)
                                continue;
                            int a = nbr[16 + e];
                            if (a != 0 && (band[q + a] != BandOutside || band[q + nbr[24 + e]] != BandOutside))
                                continue; // a diagonal past an inside orthogonal cell
                            Union(uf, q, r);
                        }
                    }
                    if ((entry & TopBit) != 0)
                        Union(uf, q, top);
                    if ((entry & BottomBit) != 0)
                        Union(uf, q, bottom);
                    if (exact && Find(uf, top) == Find(uf, bottom))
                    {
                        connecting = b;
                        exactRating = key[heapEnd - start];   // the popped cell's key (= ValueKey(o))
                        // Ties join the traversable set (o <= RatingGrade). They stay Inactive
                        // in the union-find, which is finished.
                        while (heapEnd > start && key[0] <= exactRating)
                        {
                            PopMinByKey(order, key, start, heapEnd);
                            heapEnd--;
                        }
                        break;
                    }
                }
                if (!exact && Find(uf, top) == Find(uf, bottom))
                    connecting = b;
            }

            if (connecting < 0)
            {
                // The Mask falls apart between the zones (self-intersecting or hair-thin polygon).
                result.Connected = false;
                result.RatingGrade = result.P90Grade;
                return;
            }

            float rating = -1f;
            if (connecting == last)
            {
                // Exact minimax (Crux summary, "the open last bucket").
                rating = exactRating < float.PositiveInfinity ? exactRating : last / BucketsPerUnit;   // +inf = NaN / non-finite values (tampered heights)
            }
            else
            {
                // Largest value in the connecting bucket: order-independent, and at most one
                // bucket width (1/1024) above the exact minimax (which lies in the same bucket).
                for (int k = bound[connecting]; k < bound[connecting + 1]; k++)
                {
                    float o = cg[order[k] & CellMask];
                    if (o > rating)
                        rating = o;
                }
            }
            if (!(rating >= 0f))
                rating = connecting / BucketsPerUnit; // only reachable with non-finite values (tampered heights)
            result.Connected = true;
            result.RatingGrade = rating;

            // ---- crux cell: BFS from the top-zone cells (row-major seed order) through the traversable cells (o <= RatingGrade).
            // The BFS expands neighbors in row-major order (nbr[0..7]: NW N NE W E SW S SE. Under DiagonalRule a diagonal only where the
            // union-find above allows it). It stops when the first bottom-zone cell is DISCOVERED (not dequeued), then walks back. Below
            // bucket 2047 the traversable cells are order[0 .. bound[connecting + 1]). In the last bucket they are order[0 .. bound[2047])
            // plus the popped segment order[heapEnd .. n). Each traversable cell's Unvisited state carries its Order entry's zone
            // bits, so seeds and the stop test read exactly the zones the union-find used (no key recomputation here).
            int traversable = connecting == last ? bound[last] : bound[connecting + 1];
            for (int k = 0; k < traversable; k++)
            {
                int entry = order[k];
                uf[entry & CellMask] = Unvisited | ((entry >> ZoneShift) & 3);
            }
            if (connecting == last)
            {
                for (int k = heapEnd; k < bound[RatingBuckets]; k++)
                {
                    int entry = order[k];
                    uf[entry & CellMask] = Unvisited | ((entry >> ZoneShift) & 3);
                }
            }
            Grow(ref result.Queue, n);
            int[] queue = result.Queue;
            int head = 0, tail = 0, found = -1;
            for (int j = Dilation; j < nz - Dilation && found < 0; j++)
            {
                int row = j * nx;
                for (int rk = mStart[j]; rk < mStart[j + 1] && found < 0; rk++)
                {
                    int lo = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                    int hi = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                    for (int i = lo; i <= hi; i++)
                    {
                        int q = row + i;
                        int u = uf[q];
                        if (u >= UnvisitedLimit || (u & UnvisitedTop) == 0)
                            continue; // not an inside cell, not traversable (Inactive), or not in the top zone
                        uf[q] = -1;
                        if ((u & UnvisitedBottom) != 0)
                        {
                            found = q; // the zones share a cell: a zero-length path, the crux is that cell
                            break;
                        }
                        queue[tail++] = q;
                    }
                }
            }
            while (head < tail && found < 0)
            {
                int q = queue[head++];
                for (int e = 0; e < 8; e++)   // row-major neighbor order
                {
                    int r = q + nbr[e];
                    int u = uf[r];
                    if (u >= UnvisitedLimit)
                        continue; // visited, outside or not traversable (both Inactive)
                    if (diagonalRule)
                    {
                        int a = nbr[16 + e];
                        if (a != 0 && (band[q + a] != BandOutside || band[q + nbr[24 + e]] != BandOutside))
                            continue; // the same diagonal rule as the union-find
                    }
                    uf[r] = q;
                    if ((u & UnvisitedBottom) != 0)
                    {
                        found = r;
                        break;
                    }
                    queue[tail++] = r;
                }
            }
            if (found < 0)
                return; // cannot happen once connected. CruxValid stays false

            // Walk bottom -> top. The ">=" lets a later (nearer the top) cell win ties.
            // The walked cells are also recorded (bottom -> top) so the difficulty layer can
            // measure the crux pitch and its width without a second traversal.
            Grow(ref result.Path, n + 2);
            int[] path = result.Path;
            int pathLen = 0, pathCrux = 0;
            int crux = found, cur = found, steps = 0;
            float cruxValue = cg[found];
            while (true)
            {
                if (pathLen < path.Length)
                    path[pathLen++] = cur;
                if (cg[cur] >= cruxValue)
                {
                    cruxValue = cg[cur];
                    crux = cur;
                    pathCrux = pathLen - 1;
                }
                int p = uf[cur];
                if (p < 0 || ++steps > n)
                    break;
                cur = p;
            }
            result.PathLen = pathLen;
            result.PathCrux = pathCrux;
            result.CruxValid = true;
            result.CruxJ = crux / nx;
            result.CruxI = crux - result.CruxJ * nx;
            result.CruxHeight = h[crux];
        }

        // ================================================================== Difficulty layer

        /// <summary>Floor of a band's grade range (Green 6 %, Blue 25 %, Black 40 %, Double black 60 %).</summary>
        private static float FloorOf(SlopeBand band)
        {
            switch (band)
            {
                case SlopeBand.Green: return GradeGreen;
                case SlopeBand.Blue: return GradeBlue;
                case SlopeBand.Black: return GradeBlack;
                case SlopeBand.DoubleBlack: return GradeDoubleBlack;
                default: return 0f;
            }
        }

        /// <summary>Sets Band / CruxBand / BandGrade / Note and the evidence fields from the finished crux pass. Pure: reads only the
        /// result (CellGrade, the recorded easiest line Path, FallLine, MeanGrade, RatingGrade) and the grid's Mask, with no terrain sampling and
        /// no allocation. Three rules: steep-share cap, green mean (checked before the share rule) and wide ease. Their text and
        /// constants are in EngineNotes/SkiSlopeClassifier.md ("Near FinishBand"). Widths need a usable fall line (ZoneByFallLine).
        /// Without one, width is unknown (CruxWidthM -1) and no width rule fires. Polyline mode, a broken Mask (the rating is the P90
        /// fallback, no easiest line) and ShortRun results with no valid crux keep Band = BandOf(RatingGrade).</summary>
        private static void FinishBand(SlopeGrid grid, SlopeResult result)
        {
            float rating = result.RatingGrade;
            SlopeBand crux = BandOf(rating);
            result.CruxBand = crux;
            result.Band = crux;
            result.BandGrade = rating;
            result.Note = SlopeNote.None;
            result.SteepShare = 0f;
            result.SteepShareWeighted = 0f;
            result.CruxPitchM = 0f;
            result.CruxWidthM = -1f;
            if (!result.Connected || !result.CruxValid || result.PathLen < 1 || result.PathCrux < 0 || result.PathCrux >= result.PathLen ||
                !IsFinite(rating) || !IsFinite(result.MeanGrade))
                return;
            if (crux != SlopeBand.Blue && crux != SlopeBand.Black && crux != SlopeBand.DoubleBlack)
                return;

            int nx = grid.Nx, nz = grid.Nz;
            float s = grid.Step;
            float[] cg = result.CellGrade;
            int[] path = result.Path;
            int pathLen = result.PathLen, pc = result.PathCrux;
            float floor = FloorOf(crux);

            // The crux stretch: the cells of the easiest line around the crux cell that read at/above the
            // crux band's floor (the line is recorded bottom -> top, indices lo..hi, lo the downhill end).
            int lo = pc, hi = pc;
            while (lo > 0 && cg[path[lo - 1]] >= floor)
                lo--;
            while (hi < pathLen - 1 && cg[path[hi + 1]] >= floor)
                hi++;
            double stretchM = s;                           // one cell long at least
            for (int k = lo; k < hi; k++)
            {
                int d = path[k + 1] - path[k];
                stretchM += (d == 1 || d == -1 || d == nx || d == -nx) ? s : 1.41421356 * s;   // 8-neighbor step: orthogonal or diagonal
            }
            // A stretch that reaches an end of the easiest line is cut by the end zone (the line starts / ends inside it, engine notes limit (a)).
            // The pitch may continue into the zone. So each touched end adds the zone depth, and a short-looking wall is not capped.
            if (lo == 0)
                stretchM += EndZoneDepthM;
            if (hi == pathLen - 1)
                stretchM += EndZoneDepthM;

            // Perpendicular direction of the mean fall line (lattice axes i <-> x, j <-> z).
            bool haveAxis = result.ZoneByFallLine && (result.FallLine.x != 0f || result.FallLine.y != 0f);
            float px = -result.FallLine.y, pz = result.FallLine.x;
            int wideSide = (int)math.ceil(WideRunM / s) + 1;

            if (crux == SlopeBand.Blue)
            {
                // Rule 2: wide ease at the green/blue boundary.
                if (rating < GradeBlue + WideEase && result.MeanGrade < GradeBlue && haveAxis)
                {
                    float w = StretchWidth(grid.Mask, nx, nz, path, lo, hi, px, pz, s, wideSide);
                    result.CruxWidthM = w;
                    if (w > WideRunM)
                    {
                        result.Band = SlopeBand.Green;
                        result.BandGrade = rating < GradeBlue - 2f * DisplayHysteresis ? rating : GradeBlue - 2f * DisplayHysteresis;
                        result.Note = SlopeNote.WideEase;
                    }
                }
                return;
            }

            // Rule 3: a green-mean run (cat track, traverse) rates at most blue unless its
            // unavoidable pitch is long. Narrowness does NOT keep the band here (cat tracks are narrow by nature).
            if (result.MeanGrade < GreenMeanKeepsBlueMax)
            {
                float pitchG = (float)stretchM + PitchBlurM;
                result.CruxPitchM = pitchG;
                if (haveAxis)
                    result.CruxWidthM = StretchWidth(grid.Mask, nx, nz, path, lo, hi, px, pz, s, wideSide);
                if (pitchG >= CruxLengthForBlack)
                {
                    result.Note = SlopeNote.LongKept;
                    return;
                }
                float ceilingG = GradeBlack - 2f * DisplayHysteresis;
                result.Band = SlopeBand.Blue;
                result.BandGrade = rating < ceilingG ? rating : ceilingG;
                result.Note = SlopeNote.GreenMean;
                return;
            }

            // Rule 1: the steep-share cap (crux black or double black).
            bool black = crux == SlopeBand.Black;
            float shareLimit = black ? SteepShareForBlack : DoubleBlackShare;
            if (!(rating - floor < CapMaxOverFloor) || !(result.MeanGrade < floor))
                return;

            int[] mRuns = grid.MaskRuns, mStart = grid.MaskRowStart;
            byte[] mask = grid.Mask;
            int narrowSide = (int)math.ceil(NarrowWidthM / s) + 1;
            long steep = 0, steepNarrow = 0, cells = 0;
            for (int j = Dilation; j < nz - Dilation; j++)
            {
                int row = j * nx;
                for (int rk = mStart[j]; rk < mStart[j + 1]; rk++)
                {
                    int a = mRuns[2 * rk] < Dilation ? Dilation : mRuns[2 * rk];
                    int b = mRuns[2 * rk + 1] > nx - 1 - Dilation ? nx - 1 - Dilation : mRuns[2 * rk + 1];
                    for (int i = a; i <= b; i++)
                    {
                        int q = row + i;
                        if (mask[q] == 0)
                            continue;
                        cells++;
                        if (cg[q] < floor)
                            continue;
                        steep++;
                        if (haveAxis && Extent(mask, nx, nz, i, j, px, pz, s, narrowSide) < NarrowWidthM)
                            steepNarrow++;
                    }
                }
            }
            if (cells == 0)
                return;
            float share = (float)((double)steep / cells);
            float weighted = (float)((double)(steep + steepNarrow) / cells);
            result.SteepShare = share;
            result.SteepShareWeighted = weighted;
            float pitchM = (float)stretchM + PitchBlurM;
            result.CruxPitchM = pitchM;
            if (haveAxis)
                result.CruxWidthM = StretchWidth(mask, nx, nz, path, lo, hi, px, pz, s, wideSide);
            if (!(share < shareLimit))
                return;                                     // plenty of steep cells: a real black (double black) run

            if (pitchM >= CruxLengthForBlack)
            {
                result.Note = SlopeNote.LongKept;           // a long unavoidable wall is black whatever the share
                return;
            }
            if (weighted >= shareLimit || (haveAxis && result.CruxWidthM >= 0f && result.CruxWidthM < NarrowWidthM))
            {
                result.Note = SlopeNote.NarrowKept;         // narrow + steep = harder: the width kept the band
                return;
            }
            result.Band = black ? SlopeBand.Blue : SlopeBand.Black;
            float ceilingBelow = floor - 2f * DisplayHysteresis;
            result.BandGrade = rating < ceilingBelow ? rating : ceilingBelow;
            result.Note = SlopeNote.Capped;
        }

        /// <summary>Mean perpendicular extent (m) over the easiest-line cells path[lo..hi].</summary>
        private static float StretchWidth(byte[] mask, int nx, int nz, int[] path, int lo, int hi, float px, float pz, float step, int maxSide)
        {
            double sum = 0.0;
            for (int k = lo; k <= hi; k++)
            {
                int q = path[k];
                int j = q / nx, i = q - j * nx;
                sum += Extent(mask, nx, nz, i, j, px, pz, step, maxSide);
            }
            return (float)(sum / (hi - lo + 1));
        }

        /// <summary>Extent (m) of the Mask through cell (i, j) along the unit direction (px, pz) in lattice
        /// axes: the inside cells met stepping one cell-length at a time each way, plus the cell itself, times
        /// the step. At most 2·maxSide + 1 cells (a lower bound when both sides run out of steps). Cells
        /// outside the clamped lattice count as outside.</summary>
        private static float Extent(byte[] mask, int nx, int nz, int i, int j, float px, float pz, float step, int maxSide)
        {
            int count = 1;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                for (int t = 1; t <= maxSide; t++)
                {
                    int ci = (int)math.floor(i + dir * t * px + 0.5f);
                    int cj = (int)math.floor(j + dir * t * pz + 0.5f);
                    if (ci < Dilation || ci >= nx - Dilation || cj < Dilation || cj >= nz - Dilation || mask[cj * nx + ci] == 0)
                        break;
                    count++;
                }
            }
            return count * step;
        }

        /// <summary>z = clamp(round(share·n), 1, max(1, cap)), never above n. Round half up, in
        /// double. Non-finite or negative parameters degrade to the clamps (the overload taking
        /// them is test-only).</summary>
        private static int ZoneSize(int n, float share, double cap)
        {
            double raw = (double)share * n;
            double z = raw >= 0.0 ? math.floor(raw + 0.5) : 0.0;           // NaN -> 0
            if (!(cap >= 1.0))
                cap = 1.0;
            if (z > cap)
                z = cap;
            if (z < 1.0)
                z = 1.0;
            if (z > n)
                z = n;
            return (int)z;
        }

        /// <summary>Area cap (height key): floor(EndZoneMaxM2 / step²) cells.</summary>
        private static double AreaCap(float step, float maxM2)
        {
            return math.floor((double)maxM2 / ((double)step * step));
        }

        /// <summary>Depth cap (fall-line key): floor(n·EndZoneDepthM / L) cells, L = the
        /// key range of the inside cells + step (> 0) — EndZoneDepthM of run at the run's MEAN
        /// width n·step² / L. It is a cell COUNT. So at an end narrower than the mean the zone
        /// reaches EndZoneDepthM × mean / end width along the fall line (engine notes limit (g)).</summary>
        private static double DepthCap(int n, float depthM, double keyLength)
        {
            return math.floor((double)n * depthM / keyLength);
        }

        /// <summary>Fall-line end-zone key of node (i, j): -(FallLine · (i, j))·step,
        /// "up the fall line" = larger key. Grid-local i, j (the key is only ranked, so the
        /// origin does not matter). The ONE expression for the key range, the histogram and the
        /// zone bits.</summary>
        private static float ZoneKey(float flx, float flz, int i, int j, float step)
        {
            return -(flx * i + flz * j) * step;
        }

        /// <summary>Counting-sort bucket of a per-cell value: min(2047, (int)(o·1024)). NaN,
        /// negative and >= 200 % go to the last bucket (a guard. It cannot occur with finite
        /// heights below 200 %).</summary>
        private static int BucketOf(float o)
        {
            if (!(o >= 0f) || !(o < 2f))
                return RatingBuckets - 1;
            int b = (int)(o * BucketsPerUnit);
            return b < RatingBuckets ? b : RatingBuckets - 1;
        }

        /// <summary>Heap key of a per-cell value for the open last bucket: o itself, +inf for
        /// NaN or negative (cannot occur with finite heights), so such cells come last.</summary>
        private static float ValueKey(float o)
        {
            return o >= 0f ? o : float.PositiveInfinity;
        }

        /// <summary>In-place binary min-heap over the Order segment a[start .. end) keyed by
        /// key[0 .. end - start) (parallel arrays, swapped together). Deterministic for a given
        /// input order.</summary>
        private static void HeapifyByKey(int[] a, float[] key, int start, int end)
        {
            int count = end - start;
            for (int i = count / 2 - 1; i >= 0; i--)
                SiftDownByKey(a, key, start, count, i);
        }

        /// <summary>Removes the minimum of the heap a[start .. heapEnd): swaps it (and its key) to
        /// the heap's last slot a[heapEnd - 1] / key[heapEnd - 1 - start], restores the heap over
        /// the rest and returns it (the caller decrements heapEnd).</summary>
        private static int PopMinByKey(int[] a, float[] key, int start, int heapEnd)
        {
            int lastSlot = heapEnd - 1 - start;
            int min = a[start];
            float kmin = key[0];
            a[start] = a[heapEnd - 1];
            key[0] = key[lastSlot];
            a[heapEnd - 1] = min;
            key[lastSlot] = kmin;
            SiftDownByKey(a, key, start, lastSlot, 0);
            return min;
        }

        private static void SiftDownByKey(int[] a, float[] key, int start, int count, int i)
        {
            float ki = key[i];
            int ai = a[start + i];
            while (true)
            {
                int l = 2 * i + 1;
                if (l >= count)
                    break;
                int c = l;
                float kc = key[l];
                int r = l + 1;
                if (r < count && key[r] < kc)
                {
                    c = r;
                    kc = key[r];
                }
                if (!(kc < ki))
                    break;
                key[i] = kc;
                a[start + i] = a[start + c];
                i = c;
            }
            key[i] = ki;
            a[start + i] = ai;
        }

        /// <summary>End-zone key bin: (int)((key - min key) · (1024 / key range)) in float, clamped
        /// to [0, bins - 1]. Always 0 with one bin. NaN -> 0. The key is the node height
        /// (min key = MinHeight) or ZoneKey (the fall-line key).</summary>
        private static int HeightBin(float hv, float hmin, float scale, int bins)
        {
            if (bins <= 1)
                return 0;
            float t = (hv - hmin) * scale;
            if (!(t >= 1f))
                return 0;
            return t < bins ? (int)t : bins - 1;
        }

        /// <summary>Union-find root with path halving (Uf[x] < 0 = root).</summary>
        private static int Find(int[] uf, int x)
        {
            while (true)
            {
                int p = uf[x];
                if (p < 0)
                    return x;
                int gp = uf[p];
                if (gp < 0)
                    return p;
                uf[x] = gp;
                x = gp;
            }
        }

        /// <summary>Union by size (sizes stored negated in the roots).</summary>
        private static void Union(int[] uf, int a, int b)
        {
            int ra = Find(uf, a), rb = Find(uf, b);
            if (ra == rb)
                return;
            if (uf[ra] > uf[rb])
            {
                int t = ra;
                ra = rb;
                rb = t;
            }
            uf[ra] += uf[rb];
            uf[rb] = ra;
        }

        private static int Checks(float lengthM, float step)
        {
            int c = (int)math.ceil(lengthM / step);
            return c < 2 ? 2 : c;
        }

        /// <summary>Centered window through node (i, j) along (dx, dz), half-length `half` cells:
        /// |H(p - half·d) - H(p + half·d)| / lengthM, or -1 when any of the checks + 1 evenly
        /// spaced points (both endpoints included) rounds to a node outside the Mask. Every
        /// accepted endpoint lies within 0.5 cell of an interior Mask node, so the bilinear
        /// reads stay inside the Need set. But they may read one node outside the Mask (engine notes,
        /// LATTICE RESOLUTION). All positions are OFFSETS from the node, computed
        /// without (i, j). So a cell's value is bit-identical wherever the grid's local origin
        /// lies (a superset polygon or a panning bbox never perturbs an old window).</summary>
        private static float Window(float[] h, byte[] mask, int nx, int nz, int i, int j, float dx, float dz,
                                    float half, int checks, float lengthM)
        {
            float ox1 = dx * half, oz1 = dz * half;
            float ox0 = -ox1, oz0 = -oz1;
            float ddx = ox1 - ox0, ddz = oz1 - oz0;
            for (int c = 0; c <= checks; c++)
            {
                float px, pz;
                if (c == checks)
                {
                    px = ox1;
                    pz = oz1;
                }
                else
                {
                    float t = (float)c / checks;
                    px = ox0 + ddx * t;
                    pz = oz0 + ddz * t;
                }
                int ii = i + (int)math.floor(px + 0.5f);
                int jj = j + (int)math.floor(pz + 0.5f);
                if (ii < 1 || jj < 1 || ii >= nx - 1 || jj >= nz - 1 || mask[jj * nx + ii] == 0)
                    return -1f;
            }
            // Rounded endpoint check on purpose: the bilinear can read ONE node past the last inside node (engine notes, LATTICE RESOLUTION).
            // Stricter endpoint rules were measured in SlopeBatch and each broke an invariant: four inside corners -> a 30x15 ShortRun in
            // 12 of 14 cases and a ridge at m=2 reading 26.07 % vs 27.73 %. Renormalized inside-corner weights -> planes up to +2.46 % at
            // m = 2. Pulling the end inward <= 1 cell -> runout -0.11 %, 2 cases, classify +70 % (worst 1.6 ms).
            float a = BilinearAt(h, nx, i, j, ox0, oz0);
            float b = BilinearAt(h, nx, i, j, ox1, oz1);
            return math.abs(a - b) / lengthM;
        }

        /// <summary>Bilinear over the four nodes around node (i, j) + offset (ox, oz) — the shape
        /// of TerrainUtils.SampleHeightInternal (TerrainUtils.cs:121-137): lerp along x per row,
        /// then along z. Callers guarantee the four nodes are inside the grid.</summary>
        private static float BilinearAt(float[] h, int nx, int i, int j, float ox, float oz)
        {
            float fi = math.floor(ox), fj = math.floor(oz);
            float fx = ox - fi, fz = oz - fj;
            int q = (j + (int)fj) * nx + i + (int)fi;
            float a = h[q], b = h[q + 1], c = h[q + nx], d = h[q + nx + 1];
            float r0 = a + (b - a) * fx;
            float r1 = c + (d - c) * fx;
            return r0 + (r1 - r0) * fz;
        }

        /// <summary>Descending top-k insertion (ties keep the earlier value first).</summary>
        private static void Push(float[] top, ref int count, int k, float value)
        {
            if (count == k && !(value > top[k - 1]))
                return;
            int p = count < k ? count++ : k - 1;
            while (p > 0 && top[p - 1] < value)
            {
                top[p] = top[p - 1];
                p--;
            }
            top[p] = value;
        }

        // ---------------------------------------------------------------- polyline

        private static SlopeStatus ClassifyPolyline(SlopeGrid grid, SlopeResult result)
        {
            int nx = grid.Nx, nz = grid.Nz;
            int m = grid.PointCount;
            float s = grid.Step;
            float[] h = grid.Heights;
            double[] u = grid.LocalU, v = grid.LocalV;
            // No Mask in polyline mode: CellBand is BandOutside everywhere (no consumer reads it
            // here. The overlay draws segments). Memset-speed fill, not a managed per-cell pass.
            FillOutside(result.CellBand, nx * nz);

            int segs = m - 1;
            Grow(ref result.SegmentGrade, segs);
            Grow(ref result.SegmentBand, segs);
            Grow(ref grid.ArcLength, m);
            double[] arc = grid.ArcLength;
            arc[0] = 0.0;
            double sd = s;
            double weighted = 0.0;
            for (int k = 0; k < segs; k++)
            {
                double du = u[k + 1] - u[k], dv = v[k + 1] - v[k];
                double len = sd * math.sqrt(du * du + dv * dv);
                arc[k + 1] = arc[k] + len;
                float ha = BilinearClamped(h, nx, nz, u[k], v[k]);
                float hb = BilinearClamped(h, nx, nz, u[k + 1], v[k + 1]);
                float g = len > 0.0 ? (float)(math.abs((double)hb - ha) / len) : 0f;
                result.SegmentGrade[k] = g;
                SlopeBand band = BandOf(g);
                result.SegmentBand[k] = (byte)band;
                weighted += g * len;
                result.BandShare[(int)band] += (float)len;
            }
            result.SegmentCount = segs;
            double totalLen = arc[segs];
            if (!(totalLen > 0.0))
                return SlopeStatus.TooFewPoints; // cannot happen after sanitize (1 mm dedupe)
            // A line's length is its horizontal arc length
            // (LengthRect / AxisLength stay 0: no ring, no fall-line key).
            result.Length = (float)totalLen;
            for (int b = 0; b < 5; b++)
                result.BandShare[b] = (float)(result.BandShare[b] / totalLen);
            result.MeanGrade = (float)(weighted / totalLen);

            // Heights along the arc at ds = step / 2 (plus the end point) for Min/Max/Drop.
            double ds = 0.5 * sd;
            float hmin = float.MaxValue, hmax = float.MinValue;
            int seg = 0;
            long samples = (long)math.floor(totalLen / ds);
            for (long c = 0; c <= samples + 1; c++)
            {
                double at = c <= samples ? c * ds : totalLen;
                float hv = HeightAt(grid, h, nx, nz, ref seg, at);
                if (hv < hmin) hmin = hv;
                if (hv > hmax) hmax = hv;
            }
            result.MinHeight = hmin;
            result.MaxHeight = hmax;
            result.Drop = hmax - hmin;

            float hStart = BilinearClamped(h, nx, nz, u[0], v[0]);
            float hEnd = BilinearClamped(h, nx, nz, u[m - 1], v[m - 1]);
            if (totalLen < WindowLength)
            {
                float g = (float)(math.abs((double)hEnd - hStart) / totalLen);
                result.RatingGrade = g;
                result.Band = BandOf(g);
                return SlopeStatus.ShortRun;
            }

            int k2 = (int)math.ceil(PolylineShareM / ds);
            if (k2 < PolylineMinWindows)
                k2 = PolylineMinWindows;
            Grow(ref result.TopFull, k2);
            float[] top = result.TopFull;
            int topN = 0;
            long count = (long)math.floor((totalLen - WindowLength) / ds + 1e-9) + 1;
            int segA = 0, segB = 0;
            for (long c = 0; c < count; c++)
            {
                double a = c * ds;
                double b = a + WindowLength;
                if (b > totalLen)
                    b = totalLen;
                float ha = HeightAt(grid, h, nx, nz, ref segA, a);
                float hb = HeightAt(grid, h, nx, nz, ref segB, b);
                Push(top, ref topN, k2, math.abs(ha - hb) / WindowLength);
            }
            int windows = count > int.MaxValue ? int.MaxValue : (int)count;
            result.ValidWindows = windows;
            float rating;
            SlopeStatus status;
            if (topN >= k2)
            {
                rating = top[k2 - 1];
                status = SlopeStatus.Polyline;
            }
            else
            {
                rating = top[topN - 1];
                status = SlopeStatus.ShortRun;
            }
            result.RatingGrade = rating;
            result.Band = BandOf(rating);
            return status;
        }

        /// <summary>Height at arc length `at` (meters) along the sanitized polyline. `seg` is a
        /// forward-only cursor (calls must be non-decreasing in `at`).</summary>
        private static float HeightAt(SlopeGrid grid, float[] h, int nx, int nz, ref int seg, double at)
        {
            double[] arc = grid.ArcLength, u = grid.LocalU, v = grid.LocalV;
            int last = grid.PointCount - 2;
            while (seg < last && arc[seg + 1] < at)
                seg++;
            double len = arc[seg + 1] - arc[seg];
            double t = len > 0.0 ? (at - arc[seg]) / len : 0.0;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            double x = u[seg] + (u[seg + 1] - u[seg]) * t;
            double z = v[seg] + (v[seg + 1] - v[seg]) * t;
            return BilinearClamped(h, nx, nz, x, z);
        }

        /// <summary>Bilinear with the cell index clamped into the grid — polyline points lie at
        /// least Dilation + 1 cells inside by construction, so the clamp never bites after Plan.
        /// It only keeps a tampered grid from indexing out of range.</summary>
        private static float BilinearClamped(float[] h, int nx, int nz, double x, double z)
        {
            float xf = (float)x, zf = (float)z;
            float fi = math.floor(xf), fj = math.floor(zf);
            if (fi < 0f) fi = 0f;
            if (fj < 0f) fj = 0f;
            if (fi > nx - 2) fi = nx - 2;
            if (fj > nz - 2) fj = nz - 2;
            float fx = xf - fi, fz = zf - fj;
            if (fx < 0f) fx = 0f;
            if (fx > 1f) fx = 1f;
            if (fz < 0f) fz = 0f;
            if (fz > 1f) fz = 1f;
            int q = (int)fj * nx + (int)fi;
            float a = h[q], b = h[q + 1], c = h[q + nx], d = h[q + nx + 1];
            float r0 = a + (b - a) * fx;
            float r1 = c + (d - c) * fx;
            return r0 + (r1 - r0) * fz;
        }

        // ================================================================== helpers

        /// <summary>a[0..n) = BandOutside by doubling Buffer.BlockCopy (memset-speed, net48 has
        /// no Array.Fill). The lattice is the polygon bbox (up to MaxBboxCells), so a managed
        /// per-byte loop here was an O(bbox) pass on every Classify.</summary>
        private static void FillOutside(byte[] a, int n)
        {
            if (a == null || n <= 0)
                return;
            if (n > a.Length)
                n = a.Length;
            int seed = n < 64 ? n : 64;
            for (int q = 0; q < seed; q++)
                a[q] = BandOutside;
            int done = seed;
            while (done < n)
            {
                int len = done < n - done ? done : n - done;
                Buffer.BlockCopy(a, 0, a, done, len);
                done += len;
            }
        }

        private static bool IsFinite(float f)
        {
            return !float.IsNaN(f) && !float.IsInfinity(f);
        }

        private static bool IsFinite(double d)
        {
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }

        private static double DistSqXz(float3 a, float3 b)
        {
            double dx = (double)a.x - b.x, dz = (double)a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static void Grow(ref float3[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new float3[Capacity(a == null ? 0 : a.Length, n)];
        }

        private static void Grow(ref double[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new double[Capacity(a == null ? 0 : a.Length, n)];
        }

        private static void Grow(ref float[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new float[Capacity(a == null ? 0 : a.Length, n)];
        }

        private static void Grow(ref int[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new int[Capacity(a == null ? 0 : a.Length, n)];
        }

        private static void Grow(ref byte[] a, int n)
        {
            if (a == null || a.Length < n)
                a = new byte[Capacity(a == null ? 0 : a.Length, n)];
        }

        /// <summary>Grow keeping the contents (the run lists are appended to while built).</summary>
        private static void GrowKeep(ref int[] a, int n)
        {
            if (a != null && a.Length >= n)
                return;
            int[] b = new int[Capacity(a == null ? 0 : a.Length, n)];
            if (a != null)
                Array.Copy(a, b, a.Length);
            a = b;
        }

        /// <summary>Geometric growth (×1.5, minimum 16) so a polygon that grows node by node
        /// while the player draws reallocates O(log n) times, not every recompute.</summary>
        private static int Capacity(int current, int needed)
        {
            long c = current + (current >> 1);
            if (c < 16)
                c = 16;
            if (c < needed)
                c = needed;
            return c > int.MaxValue ? int.MaxValue : (int)c;
        }
    }
}
