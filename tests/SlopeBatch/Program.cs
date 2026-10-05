// SlopeBatch — headless invariant tests for SkiSlopeClassifier (run-feedback-spec.md §3, and §9
// "Round R2" with its last subsection §9.8 "Round R2b", which win where they disagree).
// Run (from the ModularSkiResort root):
//   dotnet run --project tests\SlopeBatch\SlopeBatch.csproj -c Release
// Prints `FAIL  test/case: msg` lines, report-only INFO/TIME/WARN/SPIKE lines and one SUMMARY
// line; Environment.ExitCode = failures. `WARN  SPEC-GAP` = a spec invariant the classifier does
// not meet at a run's operating step, with the measured numbers; a regression envelope (asserted)
// keeps the gap from widening. WARN lines do not count as failures.
//
// TERRAIN STAND-IN (test-local; the classifier only ever sees it through the public
// Plan -> Need -> Heights -> Classify contract): a 3.5 m texel lattice (14336/4096,
// Game.Simulation/TerrainSystem.cs:2618-2620 in 1.6.0f1, :2630-2632 in 1.6.2f1) anchored at
// the default map's texel centres (-7166.25 + 3.5k: ToWorldSpace TerrainUtils.cs:35-38 with
// the -0.5 texel offset of TerrainSystem.cs:2621-2622), heights quantised to 4096/65535 m
// (the default height scale), sampled with the float bilinear of
// TerrainUtils.SampleHeightInternal (TerrainUtils.cs:121-137: heightmap space, floor,
// saturate, lerp along x per row, then along z).
//
// ORACLES are deliberately different code from the classifier's: analytic plane grades and
// fall lines; the closed-form max 20 m window of piecewise-linear 1-D profiles (evaluated at
// the breakpoints); winding-number point-in-polygon (classifier: even-odd scanline);
// brute-force Chebyshev dilation (classifier: rectangle marking); sorted lists for the k-th
// selection (classifier: fixed top-k insertion). R2 crux rating: OracleCheck runs on EVERY
// ring-mode classification this host makes (plus RandomOracleTests' random polygons/terrains)
// and recomputes, by brute force and with its own data structures, the end zones (sorted bin
// table), the exact minimax (binary search over the sorted distinct per-cell values, a plain
// flood fill per threshold: 8-neighbour, or R2b's diagonal rule), the expected rating (the largest value of the
// minimax's bucket below 200 %, the exact minimax in the open last bucket — R2 fixer round),
// P90, Connected, and the EXACT crux cell from an independent BFS written from the §9.3
// traversal contract (row-major seeds, row-major neighbour order, stop on discovery, largest o
// nearest the top; R2 fixer round — before, it only checked that the crux lay on some path).
// OracleCheck takes the per-cell values (CellGrade) as given; they are pinned separately:
// KthCase asserts CellGrade of EVERY Mask cell against a per-cell oracle (20/15/10/7 m row
// windows, |g| fallback) on z-invariant profiles, and BankTests / ShortHeadwallTests assert
// RatingGrade of full-width (unavoidable) features against the closed-form max window (R2 fixer
// round; the plane / bank / ridge tests assert SteepestGrade, a separate top-k list).
// Within-runtime replay is proven bit-identical; CoreCLR vs the game's Mono bit equality is
// unverified, so no golden values are asserted.
//
// R2 (§9.4) map: (1) PlaneTests, (2) InvarianceTests, (3) ScaleTests, (4) RunoutTests, old (5)
// dropped (NestedReport prints it), R2-a/b AvoidableTests, R2-c/d UnavoidableTests (+
// ShortHeadwallTests), R2-e TopHeadwallReport, R2-f SpikeTests, R2-g NoiseTests, R2-h
// DisconnectedTests, R2-i ReplayTests + AllocationTests, R2-j ScenarioTables, R2-k CaptureTests,
// TIME TimingReport; FieldFixture = the DECISIONS 116 meadow under the R1 rule and R2.
// R2 fixer round additions: R2-l OpenLastBucketTests (the open bucket >= 199.9 %), CheckRuns
// (Plan's run lists vs a brute-force row scan), TooltipLineTests (§9.6 item 4 rule,
// SkiSlopeClassifier.SteepestPatchShown), concave and cliff TIME shapes.
// R2b (§9.8): the "caller" (RateWith, ReplayCapture, the TIME / allocation loops) sets
// SlopeGrid.DiagonalRule = step multiple >= 2, as SkiRunDrawFeedbackSystem does. OracleCheck
// recomputes the zones under BOTH keys (its own double-precision mean box gradient decides the
// key and checks FallLine / MeanGradient; the fall-line key and depth cap from the spec text,
// brute-force min / max) and uses the m >= 2 connectivity (4-connected flood fill with the
// diagonal exception) in its minimax and its crux BFS. EndZoneTests (replaces R2's
// RuleLimitsReport) asserts §9.8's fixtures at m = 1 over 8 frames and reports the known-and-
// deliberate curved runs (C-run, switchback) under the fall-line key and, through the test-only
// gate (+inf), the height key. R2-e (TopHeadwallReport) and R2-j (sensitivity: EndZoneShare ×
// EndZoneDepthM) are re-pointed; the envelopes §9.8 names are re-measured and tightened (values at
// s_GapEnvelope / s_CruxEnvelope). Captures: the writer uses G9 / G17, the loader reads both and
// the round trip proves an R2 "R" file parses to the same values.
// R2b fixer round: the key gate's coherence clause (|mean g| >= 3 % OR FallCoherence >= 0.5) —
// Zp.MinCoh, the oracle decides the OR from its own double sums and asserts FallCoherence, the
// random sweep varies the clause; EndZoneTests gains the 'below the gate' fixtures (§9.8's trails
// and the drop-in on 0-2 % ground, asserted, each also under R2b as first built) and four
// sections for the review findings: KeyGateReport (gate rows + coherence survey), ZoneDepthTests
// (zone depth at a narrowing end: constant-width controls asserted, two WARN SPEC-GAP lines with
// depth envelopes), OvershootReport (a polygon drawn past a crest / valley floor; <= 20 m
// asserted Green) and RotatedPokeTests (F116 pokes under a rotated / slanted top edge; the
// field-size 30 × 40 poke asserted Green).
// U2 (docs/research/hub-u2-spec.md §3.5 "Gate", §3.8; RaterParity.cs): the Plan → Fill → Classify
// path moved out of SkiRunDrawFeedbackSystem into the pure SkiRunRater (linked here). RaterParityTests
// proves SkiRunRater.Rate bit-identical to the direct calls and to verbatim copies of the
// pre-extraction ShoelaceArea / FillHeights / Recompute middle on every capture (plus the reuse path
// and stepCurrent 0 vs 1 at m = 1) and on synthetic shapes at stepCurrent 0..3; it runs with the
// fingerprint and the oracle off, so the printed fingerprint is unchanged by it.
// R3 (docs/research/run-binding-glade-spec.md §5.1, run length): LengthCheck on every PostCheck'd
// classification (polyline arc length, LengthRect with an independent area formula, Length =
// max(LengthRect, AxisLength), zeros on a non-rating status), OracleCheck pins AxisLength to the
// oracle's key range + step, CaptureTests prints a length column and asserts each capture's
// Length (regression, ±0.1 %) and LengthRect against the spec's skeptic values (±1 m), and
// LengthTests runs 11 synthetic shapes (regression ±0.1 %, error vs the designed centreline as
// INFO). "Every existing rating bit-identical" is proven by the rating fingerprint (FNV-1a over
// every PostCheck'd classification outside LengthTests, printed before SUMMARY): the pre-R3
// classifier and the R3 classifier both give 34,058 classifications, 0x3FAEB2F59B9897D1 (R3
// round, 2026-10-01; the value changes whenever a test is added, so it is printed, not asserted).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace SlopeBatch
{
    internal delegate double HeightFn(double x, double z);

    internal delegate double LocalHeight(double s, double t);

    /// <summary>Run-local frame: `along` = downhill distance from the run start, `across` to
    /// its left. Rotation by Deg about (Cx, Cz).</summary>
    internal struct Frame
    {
        public double Cx, Cz, Ca, Sa, Deg;

        public static Frame Make(double cx, double cz, double deg)
        {
            Frame f;
            f.Cx = cx;
            f.Cz = cz;
            f.Deg = deg;
            double a = deg * Math.PI / 180.0;
            f.Ca = Math.Cos(a);
            f.Sa = Math.Sin(a);
            return f;
        }

        public double Along(double x, double z)
        {
            return (x - Cx) * Ca + (z - Cz) * Sa;
        }

        public double Across(double x, double z)
        {
            return -(x - Cx) * Sa + (z - Cz) * Ca;
        }

        public double2 World(double a, double b)
        {
            return new double2(Cx + a * Ca - b * Sa, Cz + a * Sa + b * Ca);
        }
    }

    internal sealed class Terrain
    {
        public const float Texel = 14336f / 4096f;
        public const double Quantum = 4096.0 / 65535.0;
        public static readonly double2 Origin = new double2(-7166.25, -7166.25);   // -hd.offset.xz, default map
        private const float OffsetX = 7166.25f, OffsetZ = 7166.25f;                 // hd.offset.xz
        private const float Scale = 4096f / 14336f;                                 // hd.scale.x

        public int K0, L0, Nk, Nl;
        public float[] H;
        public int OutOfRange;

        public static Terrain Build(HeightFn f, double xmin, double zmin, double xmax, double zmax)
        {
            Terrain t = new Terrain();
            t.K0 = (int)Math.Floor((xmin - Origin.x) / Texel) - 2;
            t.L0 = (int)Math.Floor((zmin - Origin.y) / Texel) - 2;
            t.Nk = (int)Math.Ceiling((xmax - Origin.x) / Texel) + 3 - t.K0;
            t.Nl = (int)Math.Ceiling((zmax - Origin.y) / Texel) + 3 - t.L0;
            t.H = new float[t.Nk * t.Nl];
            for (int l = 0; l < t.Nl; l++)
            {
                double z = Origin.y + (double)(t.L0 + l) * Texel;
                for (int k = 0; k < t.Nk; k++)
                {
                    double x = Origin.x + (double)(t.K0 + k) * Texel;
                    t.H[l * t.Nk + k] = (float)(Math.Round(f(x, z) / Quantum) * Quantum);
                }
            }
            return t;
        }

        public static Terrain For(HeightFn f, double2[] ring, double margin)
        {
            double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
            for (int i = 0; i < ring.Length; i++)
            {
                x0 = Math.Min(x0, ring[i].x);
                x1 = Math.Max(x1, ring[i].x);
                z0 = Math.Min(z0, ring[i].y);
                z1 = Math.Max(z1, ring[i].y);
            }
            return Build(f, x0 - margin, z0 - margin, x1 + margin, z1 + margin);
        }

        /// <summary>TerrainUtils.SampleHeightInternal shape (TerrainUtils.cs:121-137).</summary>
        public float Sample(float x, float z)
        {
            float hx = (x + OffsetX) * Scale, hz = (z + OffsetZ) * Scale;
            float fi = math.floor(hx), fj = math.floor(hz);
            int k = (int)fi - K0, l = (int)fj - L0;
            if (k < 0 || l < 0 || k + 1 >= Nk || l + 1 >= Nl)
            {
                OutOfRange++;
                return 0f;
            }
            float fx = math.saturate(hx - fi), fz = math.saturate(hz - fj);
            int q = l * Nk + k;
            float r0 = math.lerp(H[q], H[q + 1], fx);
            float r1 = math.lerp(H[q + Nk], H[q + Nk + 1], fx);
            return math.lerp(r0, r1, fz);
        }

        /// <summary>Texel index nearest to world coordinate w; with multiple m only texels that
        /// are lattice nodes at step m·texel (index ≡ 0 mod m) are returned.</summary>
        public static int NearestTexel(double w, double origin, int m)
        {
            return m * (int)Math.Round((w - origin) / (m * (double)Texel));
        }

        public void Add(int k, int l, float amp)
        {
            H[(l - L0) * Nk + (k - K0)] += amp;
        }
    }

    /// <summary>A named R2 fixture in run-local coordinates (s along / downhill, t across).</summary>
    internal sealed class Scn
    {
        public readonly string Name, Size;
        public readonly double2[] LocalRing;
        public readonly LocalHeight H;

        public Scn(string name, string size, double2[] localRing, LocalHeight h)
        {
            Name = name;
            Size = size;
            LocalRing = localRing;
            H = h;
        }

        public double2[] Ring(Frame fr)
        {
            double2[] w = new double2[LocalRing.Length];
            for (int i = 0; i < w.Length; i++)
                w[i] = fr.World(LocalRing[i].x, LocalRing[i].y);
            return w;
        }

        public HeightFn Height(Frame fr)
        {
            LocalHeight h = H;
            return (x, z) => h(fr.Along(x, z), fr.Across(x, z));
        }

        public double Area()
        {
            double s2 = 0.0;
            for (int i = 0; i < LocalRing.Length; i++)
            {
                double2 a = LocalRing[i], b = LocalRing[(i + 1) % LocalRing.Length];
                s2 += a.x * b.y - b.x * a.y;
            }
            return Math.Abs(0.5 * s2);
        }
    }

    internal sealed class Snapshot
    {
        public SlopeStatus Status;
        public SlopeBand Band;
        public int[] Floats;
        public long OriginX, OriginY;
        public int ValidWindows, I0, J0, Nx, Nz, SegmentCount, InsideCells, NeedCells;
        public int CruxI, CruxJ, TopZoneCells, BottomZoneCells;
        public bool PolylineMode, Connected, CruxValid, ZoneByFallLine, DiagonalRule;
        public byte[] CellBand, Mask, Need, SegmentBand;
        public int[] SegmentGrade, CellGrade;

        public static Snapshot Take(SlopeGrid g, SlopeResult r)
        {
            Snapshot s = new Snapshot();
            s.Status = r.Status;
            s.Band = r.Band;
            List<int> f = new List<int>();
            float[] vals =
            {
                r.RatingGrade, r.MeanGrade, r.Drop, r.MinHeight, r.MaxHeight, r.Area, r.FallCoherence,
                r.FallLine.x, r.FallLine.y, r.Step, r.SteepestGrade, r.P90Grade, r.CruxHeight, r.MeanGradient,
                r.Length, r.LengthRect, r.AxisLength,   // R3 (run-binding-glade-spec.md §5.1)
            };
            foreach (float v in vals)
                f.Add(BitConverter.SingleToInt32Bits(v));
            for (int b = 0; b < 5; b++)
                f.Add(BitConverter.SingleToInt32Bits(r.BandShare[b]));
            s.Floats = f.ToArray();
            s.OriginX = BitConverter.DoubleToInt64Bits(r.Origin.x);
            s.OriginY = BitConverter.DoubleToInt64Bits(r.Origin.y);
            s.ValidWindows = r.ValidWindows;
            s.I0 = r.I0;
            s.J0 = r.J0;
            s.Nx = r.Nx;
            s.Nz = r.Nz;
            s.CruxI = r.CruxI;
            s.CruxJ = r.CruxJ;
            s.TopZoneCells = r.TopZoneCells;
            s.BottomZoneCells = r.BottomZoneCells;
            s.Connected = r.Connected;
            s.CruxValid = r.CruxValid;
            s.ZoneByFallLine = r.ZoneByFallLine;
            s.DiagonalRule = r.DiagonalRule;
            int n = r.Nx * r.Nz;
            s.CellBand = new byte[n];
            if (n > 0)
                Array.Copy(r.CellBand, s.CellBand, n);
            s.SegmentCount = r.SegmentCount;
            s.SegmentBand = new byte[r.SegmentCount];
            s.SegmentGrade = new int[r.SegmentCount];
            for (int i = 0; i < r.SegmentCount; i++)
            {
                s.SegmentBand[i] = r.SegmentBand[i];
                s.SegmentGrade[i] = BitConverter.SingleToInt32Bits(r.SegmentGrade[i]);
            }
            int gn = g.Nx * g.Nz;
            s.Mask = new byte[gn];
            s.Need = new byte[gn];
            s.CellGrade = new int[gn];
            if (gn > 0)
            {
                Array.Copy(g.Mask, s.Mask, gn);
                Array.Copy(g.Need, s.Need, gn);
                // Only Mask cells are meaningful (§9.3); everything else is compared as 0.
                if (n == gn)
                    for (int q = 0; q < gn; q++)
                        if (g.Mask[q] != 0)
                            s.CellGrade[q] = BitConverter.SingleToInt32Bits(r.CellGrade[q]);
            }
            s.InsideCells = g.InsideCells;
            s.NeedCells = g.NeedCells;
            s.PolylineMode = g.PolylineMode;
            return s;
        }

        public string Diff(Snapshot o)
        {
            if (Status != o.Status) return "Status " + Status + " vs " + o.Status;
            if (Band != o.Band) return "Band";
            for (int i = 0; i < Floats.Length; i++)
                if (Floats[i] != o.Floats[i]) return "float #" + i;
            if (OriginX != o.OriginX || OriginY != o.OriginY) return "Origin";
            if (ValidWindows != o.ValidWindows) return "ValidWindows";
            if (I0 != o.I0 || J0 != o.J0 || Nx != o.Nx || Nz != o.Nz) return "geometry";
            if (InsideCells != o.InsideCells || NeedCells != o.NeedCells || PolylineMode != o.PolylineMode) return "grid counts";
            if (CruxI != o.CruxI || CruxJ != o.CruxJ || CruxValid != o.CruxValid || Connected != o.Connected) return "crux / connected";
            if (TopZoneCells != o.TopZoneCells || BottomZoneCells != o.BottomZoneCells) return "zone cells";
            if (ZoneByFallLine != o.ZoneByFallLine || DiagonalRule != o.DiagonalRule) return "zone key / diagonal rule";
            if (!Same(CellBand, o.CellBand)) return "CellBand";
            if (!Same(Mask, o.Mask)) return "Mask";
            if (!Same(Need, o.Need)) return "Need";
            if (CellGrade.Length != o.CellGrade.Length) return "CellGrade length";
            for (int i = 0; i < CellGrade.Length; i++)
                if (CellGrade[i] != o.CellGrade[i]) return "CellGrade @" + i;
            if (SegmentCount != o.SegmentCount || !Same(SegmentBand, o.SegmentBand)) return "segments";
            for (int i = 0; i < SegmentGrade.Length; i++)
                if (SegmentGrade[i] != o.SegmentGrade[i]) return "SegmentGrade";
            return null;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }

    /// <summary>One parsed .skislope capture (run-feedback-spec.md §9.5).</summary>
    internal sealed class Capture
    {
        public string Name;
        public float Step, Texel;
        public int Multiple;
        public double2 Origin;
        public int I0, J0, Nx, Nz;
        public float RatedRating, RatedSteepest, RatedMean, RatedDrop;
        public string RatedBand;
        public float3[] Nodes;
        public Dictionary<long, float> Heights = new Dictionary<long, float>();   // key: world lattice (I0 + i, J0 + j)
        public bool HasExpect;
        public SlopeBand Expect;

        public static long Key(int wi, int wj)
        {
            return ((long)wi << 32) ^ (uint)wj;
        }
    }

    /// <summary>End-zone parameters of one classification (the classifier's test-only Classify
    /// overload, R2b §9.8): EndZoneShare, EndZoneMaxM2 (height key's area cap), EndZoneDepthM
    /// (fall-line key's depth cap), and the key gate's two clauses — EndZoneMinMeanGrade (|mean g|)
    /// OR EndZoneMinCoherence (FallCoherence; the clause added in the R2b fixer round). Both +inf
    /// forces the height key; MinMean 0 the fall-line key whenever a fall line exists; MinCoh +inf
    /// alone is R2b as first built (the |mean g| gate only).</summary>
    internal struct Zp
    {
        public float Share, MaxM2, DepthM, MinMean, MinCoh;

        public Zp(float share, float maxM2, float depthM, float minMean, float minCoh)
        {
            Share = share;
            MaxM2 = maxM2;
            DepthM = depthM;
            MinMean = minMean;
            MinCoh = minCoh;
        }

        public static Zp Shipped
        {
            get
            {
                return new Zp(SkiSlopeClassifier.EndZoneShare, SkiSlopeClassifier.EndZoneMaxM2, SkiSlopeClassifier.EndZoneDepthM,
                    SkiSlopeClassifier.EndZoneMinMeanGrade, SkiSlopeClassifier.EndZoneMinCoherence);
            }
        }

        /// <summary>The same parameters with the R2 height key forced (both gate clauses +inf).</summary>
        public Zp HeightKey()
        {
            return new Zp(Share, MaxM2, DepthM, float.PositiveInfinity, float.PositiveInfinity);
        }

        /// <summary>The same parameters with the fall-line key whenever a fall line exists
        /// (|mean g| gate 0).</summary>
        public Zp FallKey()
        {
            return new Zp(Share, MaxM2, DepthM, 0f, MinCoh);
        }

        /// <summary>R2b as first built: the |mean g| gate alone (coherence clause +inf).</summary>
        public Zp MeanGateOnly()
        {
            return new Zp(Share, MaxM2, DepthM, MinMean, float.PositiveInfinity);
        }

        public bool IsShipped()
        {
            Zp s = Shipped;
            return Share == s.Share && MaxM2 == s.MaxM2 && DepthM == s.DepthM && MinMean == s.MinMean && MinCoh == s.MinCoh;
        }
    }

    internal static partial class Program   // partial since U2: RaterParity.cs (SkiRunRater parity, hub-u2-spec.md §3.5)
    {
        private const int MaxPrinted = 250;
        private static readonly double[] Angles7 = { 0, 17, 37, 45, 73, 90, 211 };

        private static int s_Failures;
        private static long s_Checks;
        private static long s_Classifies;
        private static int s_Printed;

        private static readonly SlopeGrid s_Grid = new SlopeGrid();
        private static readonly SlopeResult s_Result = new SlopeResult();

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Stopwatch total = Stopwatch.StartNew();

            BandTests();
            StepMultipleTests();            // (15)
            DegenerateTests();              // (12)
            MaskNeedTests();                // (14)
            PlaneTests();                   // R2 (1)
            InvarianceTests();              // R2 (2)
            ScaleTests();                   // R2 (3)
            RunoutTests();                  // R2 (4)
            NestedReport();                 // old (5): dropped by R2 (§9.3 limit (b)), report only
            NoiseTests();                   // R2-g
            SpikeTests();                   // R2-f
            BankTests();                    // old (8), on SteepestGrade
            RidgeCrestTests();              // old (9), on SteepestGrade
            SmallRunTests();                // (10)
            PolylineTests();                // (11)
            ReplayTests();                  // R2-i
            KthAndAbsoluteDropTests();      // exact k-th selection (SteepestGrade), absolute drop
            OutsideTerrainTests();          // mask clipping
            AvoidableTests();               // R2-a, R2-b
            UnavoidableTests();             // R2-c, R2-d
            ShortHeadwallTests();           // R2-c short full-width headwalls vs the closed-form max window (fixer round)
            OpenLastBucketTests();          // R2-l the open last bucket (>= 199.9 %) (fixer round)
            TopHeadwallReport();            // R2-e (+ the drop-ins over 21 frames), re-pointed to R2b's depth cap
            EndZoneTests();                 // R2b §9.8: asserted end-zone fixtures, report rows, curved runs under both keys
            TooltipLineTests();             // §9.6 item 4 display rule (SteepestPatchShown) (fixer round)
            DisconnectedTests();            // R2-h
            FieldFixture();                 // DECISIONS 116 meadow: R1 rule vs R2
            ScenarioTables();               // scenario table + R2-j sensitivity
            RandomOracleTests();            // brute-force minimax oracle on random polygons / terrains
            CaptureTests();                 // R2-k field captures + format round trip (+ R3 length columns and regression values)
            RaterParityTests();             // U2 SkiRunRater == direct calls == pre-extraction path (hub-u2-spec.md §3.5 gate; RaterParity.cs)
            LengthTests();                  // R3 run length: 11 synthetic shapes (run-binding-glade-spec.md §5.1)
            AllocationTests();              // R2-i zero allocation once grown
            TimingReport();                 // TIME (1.5 ms budget, crux share)

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  oracle: {0} ring classifications compared with the brute-force minimax (connected {1}, disconnected {2}, zones sharing a cell {3}); " +
                "rating above the exact minimax in {4} (max +{5}, bound 1/1024 = 0.098 %); minimax in the open last bucket (>= 199.9 %, rating asserted == exact minimax) in {6}; " +
                "crux cell asserted equal to the oracle BFS in {7}; R2b: zones on the fall-line key {8} (by the coherence clause alone {14}), on the height key {9} " +
                "(key decision asserted against an independent double-precision mean box gradient and coherence, {10} within tolerance of a gate clause followed " +
                "the classifier; FallLine worst {11:G3} deg at coherence >= 0.05, |mean g| worst relative error {12:G3}, FallCoherence worst error {15:G3} of its " +
                "tolerance 1e-5 / mean |g|), " +
                "diagonal rule {13} (4-connected flood fill with the diagonal exception)",
                s_OracleChecks, s_OracleConnected, s_OracleDisconnected, s_OracleShared, s_OracleAbove, P(s_OracleMaxAbove), s_OracleLastBucket, s_OracleCrux,
                s_OracleFallKey, s_OracleHeightKey, s_OracleGateAmbiguous, s_OracleFallLineWorstDeg, s_OracleMeanGradWorst, s_OracleDiag, s_OracleCohKey,
                s_OracleCohWorst));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  rating fingerprint (every PostCheck'd classification outside the R3 length sections): {0} classification(s), FNV-1a 64 0x{1:X16}",
                s_FingerprintCount, s_Fingerprint));
            total.Stop();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "SUMMARY  SlopeBatch R2b bands 6/25/40/60 EndZoneShare={0} EndZoneDepthM={10} EndZoneMinMeanGrade={11} EndZoneMinCoherence={12} EndZoneMaxM2={1} " +
                "RatingBuckets={2} MinRatingCells={3} RatingAreaM2={4} classifies={5} checks={6} failures={7} specGaps={8} elapsed={9:F1}s",
                SkiSlopeClassifier.EndZoneShare, SkiSlopeClassifier.EndZoneMaxM2, SkiSlopeClassifier.RatingBuckets,
                SkiSlopeClassifier.MinRatingCells, SkiSlopeClassifier.RatingAreaM2, s_Classifies, s_Checks, s_Failures, s_SpecGaps,
                total.Elapsed.TotalSeconds, SkiSlopeClassifier.EndZoneDepthM, SkiSlopeClassifier.EndZoneMinMeanGrade, SkiSlopeClassifier.EndZoneMinCoherence));
            Environment.ExitCode = s_Failures;
            return s_Failures;
        }

        // ================================================================== reporting

        private static void Fail(string where, string msg)
        {
            s_Failures++;
            if (s_Printed < MaxPrinted)
                Console.WriteLine("FAIL  " + where + ": " + msg);
            else if (s_Printed == MaxPrinted)
                Console.WriteLine("FAIL  (further FAIL lines suppressed; see SUMMARY count)");
            s_Printed++;
        }

        private static bool Check(bool cond, string where, string msg)
        {
            s_Checks++;
            if (!cond)
                Fail(where, msg);
            return cond;
        }

        private static string P(double grade)
        {
            return (grade * 100.0).ToString("F2", CultureInfo.InvariantCulture) + "%";
        }

        private static string F(double v)
        {
            return v.ToString("G6", CultureInfo.InvariantCulture);
        }

        private static string Rt(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Ab(SlopeBand b)
        {
            switch (b)
            {
                case SlopeBand.Flat: return "Flat";
                case SlopeBand.Green: return "Green";
                case SlopeBand.Blue: return "Blue";
                case SlopeBand.Black: return "Black";
                case SlopeBand.DoubleBlack: return "DblBlk";
                default: return "?";
            }
        }

        /// <summary>The R1 band rule (6 / 15 / 25 / 40 %, SPEC §6.1 before §9.2), for the
        /// "what did R1 say" columns only.</summary>
        private static SlopeBand R1Band(float g)
        {
            int b = 0;
            if (g >= 0.06f) b++;
            if (g >= 0.15f) b++;
            if (g >= 0.25f) b++;
            if (g >= 0.40f) b++;
            return (SlopeBand)b;
        }

        // ================================================================== fixtures + plumbing

        private static double2[] Rect(double length, double width, Frame fr)
        {
            return new[]
            {
                fr.World(0, -width / 2), fr.World(length, -width / 2), fr.World(length, width / 2), fr.World(0, width / 2),
            };
        }

        private static double2[] Reversed(double2[] ring)
        {
            double2[] r = new double2[ring.Length];
            for (int i = 0; i < ring.Length; i++)
                r[i] = ring[ring.Length - 1 - i];
            return r;
        }

        private static float3[] Pts(double2[] ring, bool dupClose)
        {
            float3[] p = new float3[ring.Length + (dupClose ? 1 : 0)];
            for (int i = 0; i < ring.Length; i++)
                p[i] = new float3((float)ring[i].x, 0f, (float)ring[i].y);
            if (dupClose)
                p[ring.Length] = p[0];
            return p;
        }

        private static float Step(int m)
        {
            return m * Terrain.Texel;
        }

        private static void Fill(SlopeGrid g, Terrain t)
        {
            int nx = g.Nx, nz = g.Nz;
            for (int j = 0; j < nz; j++)
            {
                float wz = (float)(g.Origin.y + (double)(g.J0 + j) * g.Step);
                for (int i = 0; i < nx; i++)
                {
                    int q = j * nx + i;
                    if (g.Need[q] == 0)
                        continue;
                    float wx = (float)(g.Origin.x + (double)(g.I0 + i) * g.Step);
                    g.Heights[q] = t.Sample(wx, wz);
                }
            }
        }

        private static SlopeStatus Rate(Terrain t, float3[] pts, int m, SlopeGrid g, SlopeResult r)
        {
            return RateWith(t, pts, m, g, r, Zp.Shipped, null);
        }

        private static SlopeStatus Rate(Terrain t, double2[] ring, int m)
        {
            return Rate(t, Pts(ring, false), m, s_Grid, s_Result);
        }

        /// <summary>R2b: the caller's connectivity rule (SkiRunDrawFeedbackSystem sets
        /// SlopeGrid.DiagonalRule = step multiple >= 2). s_DiagOverride (random sweep only):
        /// -1 = the caller's rule, 0 / 1 = forced off / on (the classifier does not know m, so
        /// the rule is exercised at m = 1 too).</summary>
        private static int s_DiagOverride = -1;

        private static bool DiagFor(int m)
        {
            return s_DiagOverride < 0 ? m >= 2 : s_DiagOverride != 0;
        }

        /// <summary>Plan -> fill -> Classify (the test-only overload with the given end-zone
        /// parameters; with the shipped constants it is exactly the public overload) with the
        /// caller's DiagonalRule, then the post-checks: polyline fields, and the brute-force
        /// minimax oracle on every ring-mode rating.</summary>
        private static SlopeStatus RateWith(Terrain t, float3[] pts, int m, SlopeGrid g, SlopeResult r, Zp zp, string where)
        {
            s_Classifies++;
            SlopeStatus st = SkiSlopeClassifier.Plan(pts, pts.Length, Terrain.Origin, Step(m), g);
            g.DiagonalRule = DiagFor(m);
            if (st == SlopeStatus.Ok)
                Fill(g, t);
            if (t.OutOfRange != 0)
            {
                Fail("terrain", "fixture terrain too small (" + t.OutOfRange + " samples out of range)");
                t.OutOfRange = 0;
            }
            SlopeStatus cs = Classify(g, r, zp);
            PostCheck(where ?? "classify #" + s_Classifies, g, r, cs, zp);
            return cs;
        }

        private static SlopeStatus Classify(SlopeGrid g, SlopeResult r, Zp zp)
        {
            return SkiSlopeClassifier.Classify(g, r, zp.Share, zp.MaxM2, zp.DepthM, zp.MinMean, zp.MinCoh);
        }

        private static bool CruxForTest(SlopeGrid g, SlopeResult r, Zp zp)
        {
            return SkiSlopeClassifier.CruxForTest(g, r, zp.Share, zp.MaxM2, zp.DepthM, zp.MinMean, zp.MinCoh);
        }

        // R3 (run-binding-glade-spec.md §5.1): "the classifier change must leave every existing
        // rating bit-identical". Every classification that passes PostCheck (RateWith — the bulk
        // of this host —, the degenerate / mask sections and the capture replays) folds its
        // rating outputs into one FNV-1a 64 fingerprint, printed before SUMMARY. R3's own length
        // sections switch the fold off (s_FingerprintOn), so the value is comparable with a run
        // of the pre-R3 classifier on the same host: equal fingerprints = every PostCheck'd
        // rating, crux cell, zone, P90, band share, mean, drop and area bit-identical.
        private static ulong s_Fingerprint = 14695981039346656037UL;
        private static long s_FingerprintCount;
        private static bool s_FingerprintOn = true;

        private static void FoldBits(uint v)
        {
            for (int b = 0; b < 4; b++)
            {
                s_Fingerprint ^= (v >> (8 * b)) & 0xFFu;
                s_Fingerprint *= 1099511628211UL;
            }
        }

        private static void FoldFloat(float v)
        {
            FoldBits((uint)BitConverter.SingleToInt32Bits(v));
        }

        private static void FoldRating(SlopeGrid g, SlopeResult r, SlopeStatus st)
        {
            s_FingerprintCount++;
            FoldBits((uint)st);
            FoldBits((uint)r.Status);
            FoldBits((uint)r.Band);
            FoldFloat(r.RatingGrade);
            FoldFloat(r.SteepestGrade);
            FoldFloat(r.P90Grade);
            FoldFloat(r.MeanGrade);
            FoldFloat(r.Drop);
            FoldFloat(r.MinHeight);
            FoldFloat(r.MaxHeight);
            FoldFloat(r.Area);
            FoldFloat(r.FallCoherence);
            FoldFloat(r.FallLine.x);
            FoldFloat(r.FallLine.y);
            FoldFloat(r.MeanGradient);
            FoldFloat(r.CruxHeight);
            for (int b = 0; b < 5; b++)
                FoldFloat(r.BandShare[b]);
            FoldBits((uint)r.ValidWindows);
            FoldBits((uint)r.CruxI);
            FoldBits((uint)r.CruxJ);
            FoldBits((uint)r.TopZoneCells);
            FoldBits((uint)r.BottomZoneCells);
            FoldBits((r.Connected ? 1u : 0u) | (r.CruxValid ? 2u : 0u) | (r.ZoneByFallLine ? 4u : 0u) | (r.DiagonalRule ? 8u : 0u) |
                     (g.PolylineMode ? 16u : 0u));
            FoldBits((uint)r.SegmentCount);
            for (int i = 0; i < r.SegmentCount; i++)
                FoldFloat(r.SegmentGrade[i]);
        }

        // R3 length oracle counters (printed in LengthTests' summary line).
        private static long s_LenChecksRing, s_LenChecksLine, s_LenAxisChecks, s_LenAxisExact;
        private static double s_LenRectWorstRel, s_LenLineWorstRel, s_LenAxisWorstAbs;

        /// <summary>R3 (run-binding-glade-spec.md §5.1) on EVERY PostCheck'd classification:
        /// a non-rating status leaves Length / LengthRect / AxisLength at 0 (Clear); polyline mode
        /// Length = the horizontal arc length of the sanitized points (recomputed here from the
        /// world xz in double, not from the classifier's lattice units) with LengthRect =
        /// AxisLength = 0; ring mode LengthRect = (P + sqrt(max(0, P² − 16A))) / 4 recomputed
        /// with a different area formula (trapezoids Σ (x₂ − x₁)(z₂ + z₁) / 2 on unshifted
        /// coordinates; classifier: shoelace relative to point 0), AxisLength = 0 unless
        /// ZoneByFallLine (OracleCheck pins its value against the oracle's key range), and Length
        /// == max(LengthRect, AxisLength) exactly.</summary>
        private static void LengthCheck(string w, SlopeGrid g, SlopeResult r, SlopeStatus st)
        {
            bool rated = st == SlopeStatus.Ok || st == SlopeStatus.ShortRun || st == SlopeStatus.Polyline;
            if (!rated)
            {
                Check(r.Length == 0f && r.LengthRect == 0f && r.AxisLength == 0f, w, "non-rating status " + st + " but Length " + F(r.Length) +
                                                                                    " / LengthRect " + F(r.LengthRect) + " / AxisLength " + F(r.AxisLength));
                return;
            }
            int m = g.PointCount;
            float3[] p = g.Points;
            if (g.PolylineMode)
            {
                s_LenChecksLine++;
                double arc = 0.0;
                for (int k = 0; k + 1 < m; k++)
                {
                    double dx = (double)p[k + 1].x - p[k].x, dz = (double)p[k + 1].z - p[k].z;
                    arc += Math.Sqrt(dx * dx + dz * dz);
                }
                double rel = Math.Abs(r.Length - arc) / Math.Max(1.0, arc);
                s_LenLineWorstRel = Math.Max(s_LenLineWorstRel, rel);
                Check(rel <= 1e-5 && r.LengthRect == 0f && r.AxisLength == 0f, w,
                    "polyline Length " + F(r.Length) + " vs arc " + F(arc) + " (LengthRect " + F(r.LengthRect) + ", AxisLength " + F(r.AxisLength) + ")");
                return;
            }
            s_LenChecksRing++;
            double per = 0.0, trap = 0.0;
            for (int k = 0; k < m; k++)
            {
                float3 a = p[k], b = p[(k + 1) % m];
                double dx = (double)b.x - a.x, dz = (double)b.z - a.z;
                per += Math.Sqrt(dx * dx + dz * dz);
                trap += dx * ((double)b.z + a.z) * 0.5;
            }
            double area = Math.Abs(trap);
            double rect = (per + Math.Sqrt(Math.Max(0.0, per * per - 16.0 * area))) / 4.0;
            // Near-square rings put P² − 16A at catastrophic cancellation, where the two area
            // formulas legitimately differ by rounding: compare with an absolute floor of 0.05 m.
            double relR = Math.Abs(r.LengthRect - rect) / Math.Max(1.0, rect);
            s_LenRectWorstRel = Math.Max(s_LenRectWorstRel, relR);
            Check(Math.Abs(r.LengthRect - rect) <= Math.Max(1e-5 * rect, 0.05), w, "LengthRect " + F(r.LengthRect) + " vs oracle " + F(rect) +
                                                                                      " (P " + F(per) + ", A " + F(area) + ")");
            Check(r.ZoneByFallLine || r.AxisLength == 0f, w, "height key but AxisLength " + F(r.AxisLength));
            Check(r.Length == Math.Max(r.LengthRect, r.AxisLength), w, "Length " + F(r.Length) + " != max(LengthRect " + F(r.LengthRect) + ", AxisLength " +
                                                                    F(r.AxisLength) + ")");
        }

        private static void PostCheck(string w, SlopeGrid g, SlopeResult r, SlopeStatus st, Zp zp)
        {
            if (s_FingerprintOn)
                FoldRating(g, r, st);
            LengthCheck(w, g, r, st);
            if (g.PolylineMode && (st == SlopeStatus.Polyline || st == SlopeStatus.ShortRun))
            {
                Check(r.SteepestGrade == r.RatingGrade && r.P90Grade == r.RatingGrade && r.Connected && !r.CruxValid &&
                      r.TopZoneCells == 0 && r.BottomZoneCells == 0 && !r.ZoneByFallLine && r.DiagonalRule == g.DiagonalRule, w,
                    "polyline R2 fields: steepest " + P(r.SteepestGrade) + " p90 " + P(r.P90Grade) + " rating " + P(r.RatingGrade) +
                    " connected " + r.Connected + " cruxValid " + r.CruxValid + " zoneByFallLine " + r.ZoneByFallLine);
            }
            else if (!g.PolylineMode && (st == SlopeStatus.Ok || st == SlopeStatus.ShortRun) && s_OracleOn)
            {
                OracleCheck(w, g, r, zp);
            }
        }

        // Profiles in run-local coordinates (s = along, downhill; t = across).
        private static double BankProfile(double s, double bankLen, double bankGrade)
        {
            return 1000.0 - 0.10 * s - Math.Min(Math.Max(s - 120.0, 0.0), bankLen) * (bankGrade - 0.10);
        }

        private static double RunoutProfile(double s)
        {
            return s < 150.0 ? 1000.0 - 0.30 * s : 1000.0 - 45.0 - 0.03 * (s - 150.0);
        }

        private static double CrestProfile(double s)
        {
            return s < 100.0 ? 1000.0 + 0.15 * s : 1015.0 - 0.20 * (s - 100.0);
        }

        private static HeightFn Mountain()
        {
            double d30 = 30.0 * Math.PI / 180.0;
            return (x, z) => 1500.0 - 0.22 * (x * Math.Cos(d30) + z * Math.Sin(d30)) + 18.0 * Math.Sin(x / 95.0) * Math.Cos(z / 140.0)
                             + 3.0 * Math.Sin(x / 23.0 + z / 31.0) + 0.3 * Math.Sin(x / 7.3 - z / 9.1);
        }

        /// <summary>Closed-form max over s in [s0, s1 - L] of |p(s + L) - p(s)| / L for a
        /// piecewise-linear p with the given breakpoints: g(s) = p(s+L) - p(s) is itself
        /// piecewise linear with kinks at b and b - L, so the max sits on one of those or an end.</summary>
        private static double OracleMaxWindow(Func<double, double> p, double[] breaks, double s0, double s1, double len)
        {
            List<double> cand = new List<double> { s0, s1 - len };
            foreach (double b in breaks)
            {
                cand.Add(b);
                cand.Add(b - len);
            }
            double best = 0.0;
            foreach (double c in cand)
            {
                if (c < s0 - 1e-9 || c > s1 - len + 1e-9)
                    continue;
                best = Math.Max(best, Math.Abs(p(c + len) - p(c)) / len);
            }
            return best;
        }

        /// <summary>Winding-number point-in-polygon (Sunday's isLeft form).</summary>
        private static bool OracleInside(double2[] v, double2 p)
        {
            int wn = 0;
            for (int i = 0; i < v.Length; i++)
            {
                double2 a = v[i], b = v[(i + 1) % v.Length];
                double isLeft = (b.x - a.x) * (p.y - a.y) - (p.x - a.x) * (b.y - a.y);
                if (a.y <= p.y)
                {
                    if (b.y > p.y && isLeft > 0)
                        wn++;
                }
                else if (b.y <= p.y && isLeft < 0)
                {
                    wn--;
                }
            }
            return wn != 0;
        }

        private static double OracleBoundaryDistance(double2[] v, double2 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < v.Length; i++)
            {
                double2 a = v[i], b = v[(i + 1) % v.Length];
                double vx = b.x - a.x, vy = b.y - a.y, wx = p.x - a.x, wy = p.y - a.y;
                double ll = vx * vx + vy * vy;
                double t = ll > 0 ? Math.Max(0.0, Math.Min(1.0, (vx * wx + vy * wy) / ll)) : 0.0;
                double dx = wx - t * vx, dy = wy - t * vy;
                best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
            }
            return best;
        }

        private static double2[] AsDouble(float3[] pts)
        {
            // The ring exactly as the classifier received it (float-rounded), closing dup stripped.
            List<double2> v = new List<double2>();
            foreach (float3 p in pts)
                v.Add(new double2(p.x, p.z));
            if (v.Count > 1 && v[v.Count - 1].Equals(v[0]))
                v.RemoveAt(v.Count - 1);
            return v.ToArray();
        }

        private static double2 NodeWorld(SlopeGrid g, int i, int j)
        {
            return new double2(g.Origin.x + (double)(g.I0 + i) * g.Step, g.Origin.y + (double)(g.J0 + j) * g.Step);
        }

        private static double2 CruxWorld(SlopeResult r)
        {
            return new double2(r.Origin.x + (double)(r.I0 + r.CruxI) * r.Step, r.Origin.y + (double)(r.J0 + r.CruxJ) * r.Step);
        }

        // ================================================================== R2 brute-force minimax oracle

        private static bool s_OracleOn = true;
        private static long s_OracleChecks, s_OracleConnected, s_OracleDisconnected, s_OracleShared, s_OracleAbove, s_OracleLastBucket, s_OracleCrux;
        private static long s_OracleFallKey, s_OracleHeightKey, s_OracleDiag, s_OracleGateAmbiguous, s_OracleCohKey;
        private static double s_OracleMaxAbove, s_OracleFallLineWorstDeg, s_OracleMeanGradWorst, s_OracleCohWorst;

        // Connectivity of the oracle's flood fills (R2b §9.8): 8-neighbour (step multiple 1, R2),
        // 4-neighbour (report-only evidence), and the DiagonalRule (4-neighbour plus a diagonal
        // only when both orthogonally shared cells are outside the Mask; step multiple >= 2).
        private const int Conn8 = 0, Conn4 = 1, ConnDiag = 2;

        /// <summary>True when the oracle's connectivity lets (i, j) step to (i + di, j + dj)
        /// (a neighbour, already range-checked).</summary>
        private static bool OStep(SlopeGrid g, int conn, int i, int j, int di, int dj)
        {
            if (di == 0 || dj == 0 || conn == Conn8)
                return true;
            if (conn == Conn4)
                return false;
            return g.Mask[j * g.Nx + i + di] == 0 && g.Mask[(j + dj) * g.Nx + i] == 0;
        }
        private static int[] s_Mark = new int[0];
        private static int s_Gen;
        private static readonly Queue<int> s_OQueue = new Queue<int>();

        /// <summary>§9.3 bucket, written from the spec text: min(2047, (int)(o·1024)), NaN /
        /// negative / >= 200 % to the last bucket.</summary>
        private static int OBucket(float v)
        {
            if (float.IsNaN(v) || v < 0f || v >= 2f)
                return 2047;
            return Math.Min(2047, (int)(v * 1024f));
        }

        /// <summary>Plain flood fill over Mask cells with the given connectivity (Conn8 / Conn4 /
        /// ConnDiag): true when a `target` cell is reachable from a `seed` cell through `pass`
        /// cells (seeds must pass too).</summary>
        private static bool Reach(SlopeGrid g, List<int> cells, Func<int, bool> seed, Func<int, bool> pass, Func<int, bool> target, int conn)
        {
            int nx = g.Nx, nz = g.Nz, total = nx * nz;
            if (s_Mark.Length < total)
            {
                s_Mark = new int[total];
                s_Gen = 0;
            }
            s_Gen++;
            Queue<int> queue = s_OQueue;
            queue.Clear();
            foreach (int q in cells)
            {
                if (!seed(q) || !pass(q))
                    continue;
                if (target(q))
                    return true;
                s_Mark[q] = s_Gen;
                queue.Enqueue(q);
            }
            while (queue.Count > 0)
            {
                int q = queue.Dequeue();
                int i = q % nx, j = q / nx;
                for (int dj = -1; dj <= 1; dj++)
                {
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0)
                            continue;
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= nx || jj >= nz)
                            continue;
                        int r = jj * nx + ii;
                        if (g.Mask[r] == 0 || s_Mark[r] == s_Gen || !OStep(g, conn, i, j, di, dj) || !pass(r))
                            continue;
                        if (target(r))
                            return true;
                        s_Mark[r] = s_Gen;
                        queue.Enqueue(r);
                    }
                }
            }
            return false;
        }

        /// <summary>The oracle's end zones, from the spec text (§9.3 as replaced by §9.8 "R2b"):
        /// the key is the position along the mean fall line, -(FallLine · (i, j))·step, when the
        /// mean box gradient |Σg| / n >= EndZoneMinMeanGrade OR the coherence |Σg| / Σ|g| >=
        /// EndZoneMinCoherence (§9.8 as amended in the R2b fixer round), else the node height (R2); 1024
        /// equal bins over the Mask cells' [min, max] key (one bin below 1 mm), whole bins from
        /// each end until >= z cells, z = clamp(round(share·n), 1, max(1, cap)) with cap =
        /// floor(n·EndZoneDepthM / L), L = key range + step (fall-line key), or floor(EndZoneMaxM2 /
        /// step²) (height key). Built with a SortedDictionary bin table and brute-force min/max
        /// (classifier: histogram walk, run-end key range). The mean box gradient is recomputed
        /// independently (5×5 central differences summed in double) and decides the key; the
        /// published FallLine (float) is checked against it and then used for the key, which is
        /// the same float expression the spec writes.</summary>
        private sealed class OZones
        {
            public List<int> Cells = new List<int>();
            public int[] Bin;
            public float HMin = float.MaxValue, HMax = float.MinValue;
            public int Z, TopThr, BotThr, TopCount, BotCount;
            public bool ByFall, GateAmbiguous, ByCoherenceOnly;
            public double MeanGrad, FallX, FallZ, L, SumAbs, Coherence, CohTol;

            public bool InTop(int q)
            {
                return Bin[q] >= TopThr;
            }

            public bool InBot(int q)
            {
                return Bin[q] <= BotThr;
            }
        }

        private static OZones OracleZones(SlopeGrid g, SlopeResult r, Zp zp)
        {
            OZones o = new OZones();
            int nx = g.Nx, total = g.Nx * g.Nz;
            for (int q = 0; q < total; q++)
                if (g.Mask[q] != 0)
                    o.Cells.Add(q);
            int n = o.Cells.Count;
            float[] h = g.Heights;
            double sx = 0.0, sz = 0.0, sabs = 0.0;
            foreach (int q in o.Cells)
            {
                o.HMin = Math.Min(o.HMin, h[q]);
                o.HMax = Math.Max(o.HMax, h[q]);
                // 5×5 box of central differences: Σ over the 5 rows of h(+3) + h(+2) - h(-2) - h(-3)
                // along x, and the same along z over the 5 columns (spec §3).
                double cx = 0.0, cz = 0.0;
                for (int b = -2; b <= 2; b++)
                {
                    int rr = q + b * nx, cc = q + b;
                    cx += (double)h[rr + 3] + h[rr + 2] - h[rr - 2] - h[rr - 3];
                    cz += (double)h[cc + 3 * nx] + h[cc + 2 * nx] - h[cc - 2 * nx] - h[cc - 3 * nx];
                }
                sx += cx;
                sz += cz;
                sabs += Math.Sqrt(cx * cx + cz * cz);
            }
            o.SumAbs = sabs / (50.0 * g.Step);                                // Σ|g|
            double gsx = sx / (50.0 * g.Step), gsz = sz / (50.0 * g.Step);   // Σg
            double gl = Math.Sqrt(gsx * gsx + gsz * gsz);
            o.MeanGrad = n > 0 ? gl / n : 0.0;
            if (gl > 0.0)
            {
                o.FallX = -gsx / gl;
                o.FallZ = -gsz / gl;
            }
            o.Coherence = gl > 0.0 && sabs > 0.0 ? Math.Min(1.0, gl / o.SumAbs) : 0.0;
            // Coherence = |mean g| / mean |g|, so the |mean g| tolerance (1e-5 absolute below 1,
            // OracleCheck: float per-cell sums vs these double ones) scales by 1 / mean |g|.
            o.CohTol = 1e-5 * Math.Max(1.0, o.MeanGrad) / Math.Max(n > 0 ? o.SumAbs / n : 0.0, 1e-6) + 1e-6;
            bool hasFall = r.FallLine.x != 0f || r.FallLine.y != 0f;
            bool byMean = o.MeanGrad >= zp.MinMean, byCoh = o.Coherence >= zp.MinCoh;
            o.ByFall = hasFall && (byMean || byCoh);
            o.ByCoherenceOnly = hasFall && byCoh && !byMean;
            // Within the tolerance of a clause's threshold the double-vs-float sums may disagree;
            // the OR is decided only when a sure clause is true or both clauses are sure — else
            // follow the classifier.
            bool meanSure = !(Math.Abs(o.MeanGrad - zp.MinMean) <= 1e-5), cohSure = !(Math.Abs(o.Coherence - zp.MinCoh) <= o.CohTol);
            if (hasFall && !((meanSure && byMean) || (cohSure && byCoh) || (meanSure && cohSure)))
            {
                o.GateAmbiguous = true;
                o.ByFall = r.ZoneByFallLine;
            }
            float[] key = new float[total];
            float kmin = float.MaxValue, kmax = float.MinValue;
            foreach (int q in o.Cells)
            {
                int i = q % nx, j = q / nx;
                float k = o.ByFall ? -(r.FallLine.x * i + r.FallLine.y * j) * g.Step : h[q];
                key[q] = k;
                kmin = Math.Min(kmin, k);
                kmax = Math.Max(kmax, k);
            }
            float range = kmax - kmin;
            bool oneBin = !(range >= 1e-3f);
            float scale = oneBin ? 0f : 1024f / range;
            o.Bin = new int[total];
            SortedDictionary<int, int> binCount = new SortedDictionary<int, int>();
            foreach (int q in o.Cells)
            {
                int b = oneBin ? 0 : Math.Min(1023, (int)((key[q] - kmin) * scale));
                o.Bin[q] = b;
                binCount.TryGetValue(b, out int c);
                binCount[b] = c + 1;
            }
            double zr = Math.Round((double)zp.Share * n, MidpointRounding.AwayFromZero);
            if (!(zr >= 0.0))
                zr = 0.0;
            double capCells;
            if (o.ByFall)
            {
                o.L = ((double)kmax - kmin) + g.Step;
                capCells = Math.Floor((double)n * zp.DepthM / o.L);
            }
            else
            {
                capCells = Math.Floor((double)zp.MaxM2 / ((double)g.Step * g.Step));
            }
            if (!(capCells >= 1.0))
                capCells = 1.0;
            o.Z = (int)Math.Min(Math.Max(1.0, Math.Min(zr, capCells)), Math.Max(1, n));
            List<int> keys = new List<int>(binCount.Keys);
            for (int k = keys.Count - 1; k >= 0 && o.TopCount < o.Z; k--)
            {
                o.TopThr = keys[k];
                o.TopCount += binCount[keys[k]];
            }
            for (int k = 0; k < keys.Count && o.BotCount < o.Z; k++)
            {
                o.BotThr = keys[k];
                o.BotCount += binCount[keys[k]];
            }
            return o;
        }

        /// <summary>Exact minimax of the per-cell values from the top to the bottom zone (shipped
        /// zone parameters) with the given connectivity (NaN when disconnected). Report-only
        /// evidence (4- vs 8-connectivity); the classifier's own rule is checked by
        /// OracleCheck.</summary>
        private static float OracleMinimax(SlopeGrid g, SlopeResult r, int conn)
        {
            OZones oz = OracleZones(g, r, Zp.Shipped);
            if (oz.Cells.Count == 0)
                return float.NaN;
            float[] v = r.CellGrade;
            List<float> distinct = new List<float>();
            foreach (int q in oz.Cells)
                distinct.Add(v[q]);
            distinct.Sort();
            Func<float, bool> feasible = T => Reach(g, oz.Cells, oz.InTop, q => v[q] <= T, oz.InBot, conn);
            if (!feasible(distinct[distinct.Count - 1]))
                return float.NaN;
            int lo = 0, hi = distinct.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (feasible(distinct[mid]))
                    hi = mid;
                else
                    lo = mid + 1;
            }
            return distinct[lo];
        }

        /// <summary>The crux cell from the §9.3 traversal contract, written from the spec text
        /// (as pinned in the R2 fixer round) with its own data structures: BFS seeds = the
        /// top-zone traversable cells in row-major scan order (a seed that is also in the bottom
        /// zone is the crux at once); traversable = Mask cells with o &lt;= limit (the rating);
        /// the neighbours expanded in row-major order (dj = -1, 0, 1 outer; di = -1, 0, 1
        /// inner), under `conn` (R2b: ConnDiag skips a diagonal past an inside orthogonal cell);
        /// the search stops when the first bottom-zone cell is DISCOVERED; walk the parents back
        /// from it and take the largest o, the cell nearest the top on ties. Returns the flat
        /// index, or -1 when no path exists.</summary>
        private static int OracleCrux(SlopeGrid g, OZones oz, float[] v, float limit, int conn)
        {
            int nx = g.Nx, nz = g.Nz, total = nx * nz;
            Dictionary<int, int> parent = new Dictionary<int, int>();
            Queue<int> queue = new Queue<int>();
            Func<int, bool> trav = q => g.Mask[q] != 0 && v[q] <= limit;
            int found = -1;
            foreach (int q in oz.Cells)   // ascending flat index = row-major
            {
                if (!trav(q) || !oz.InTop(q))
                    continue;
                parent[q] = -1;
                if (oz.InBot(q))
                {
                    found = q;
                    break;
                }
                queue.Enqueue(q);
            }
            while (found < 0 && queue.Count > 0)
            {
                int q = queue.Dequeue();
                int i = q % nx, j = q / nx;
                for (int dj = -1; dj <= 1 && found < 0; dj++)
                {
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0)
                            continue;
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= nx || jj >= nz)
                            continue;
                        int rr = jj * nx + ii;
                        if (!trav(rr) || parent.ContainsKey(rr) || !OStep(g, conn, i, j, di, dj))
                            continue;
                        parent[rr] = q;
                        if (oz.InBot(rr))
                        {
                            found = rr;
                            break;
                        }
                        queue.Enqueue(rr);
                    }
                }
            }
            if (found < 0)
                return -1;
            int crux = found, cur = found;
            float best = v[found];
            for (int guard = 0; guard <= total; guard++)
            {
                if (v[cur] >= best)
                {
                    best = v[cur];
                    crux = cur;
                }
                int p = parent[cur];
                if (p < 0)
                    break;
                cur = p;
            }
            return crux;
        }

        private static void OracleCheck(string w, SlopeGrid g, SlopeResult r, Zp zp)
        {
            s_OracleChecks++;
            w = w + " [oracle]";
            int nx = g.Nx, nz = g.Nz;
            OZones oz = OracleZones(g, r, zp);
            List<int> cells = oz.Cells;
            int n = cells.Count;
            if (!Check(n == g.InsideCells && n > 0, w, n + " Mask cells vs InsideCells " + g.InsideCells))
                return;
            float[] h = g.Heights, v = r.CellGrade;
            Check(r.MinHeight == oz.HMin && r.MaxHeight == oz.HMax, w, "Min/MaxHeight " + F(r.MinHeight) + "/" + F(r.MaxHeight) + " vs " + F(oz.HMin) + "/" + F(oz.HMax));
            // R2b (§9.8): the connectivity rule is the caller's, the zone key follows |mean g|.
            int conn = g.DiagonalRule ? ConnDiag : Conn8;
            if (g.DiagonalRule)
                s_OracleDiag++;
            Check(r.DiagonalRule == g.DiagonalRule, w, "DiagonalRule " + r.DiagonalRule + " vs the grid's " + g.DiagonalRule);
            double mgErr = Math.Abs(r.MeanGradient - oz.MeanGrad);
            s_OracleMeanGradWorst = Math.Max(s_OracleMeanGradWorst, mgErr / Math.Max(1.0, oz.MeanGrad));
            Check(mgErr <= 1e-5 * Math.Max(1.0, oz.MeanGrad), w, "mean box gradient " + F(r.MeanGradient) + " vs oracle " + F(oz.MeanGrad));
            if (oz.MeanGrad * n > 1e-3)
            {
                // The classifier sums float per-cell gradients, the oracle doubles: their difference
                // is a fraction of Σ|g|, so the direction tolerance scales with 1 / coherence
                // (|Σg| / Σ|g|; a cone or saddle cancels to ~0.2 %).
                double dot = r.FallLine.x * oz.FallX + r.FallLine.y * oz.FallZ, cross = r.FallLine.x * oz.FallZ - r.FallLine.y * oz.FallX;
                double deg = Math.Atan2(Math.Abs(cross), dot) * 180.0 / Math.PI;   // atan2: acos of a float-normalised dot reads ~0.01° on equal vectors
                double coh = oz.SumAbs > 0.0 ? oz.MeanGrad * n / oz.SumAbs : 1.0;
                double tolDeg = Math.Min(180.0, 1e-4 / Math.Max(coh, 1e-9) * 180.0 / Math.PI);
                if (coh >= 0.05)
                    s_OracleFallLineWorstDeg = Math.Max(s_OracleFallLineWorstDeg, deg);
                Check(deg <= tolDeg, w, "FallLine (" + F(r.FallLine.x) + ", " + F(r.FallLine.y) + ") vs oracle (" + F(oz.FallX) + ", " + F(oz.FallZ) + "): " + F(deg) +
                                        " deg (tolerance " + F(tolDeg) + " at coherence " + F(coh) + ")");
            }
            if (r.FallLine.x != 0f || r.FallLine.y != 0f)
            {
                // The key gate's second input (R2b fixer round): |Σg| / Σ|g|, double sums here.
                double cohErr = Math.Abs(r.FallCoherence - oz.Coherence);
                s_OracleCohWorst = Math.Max(s_OracleCohWorst, cohErr / oz.CohTol);
                Check(cohErr <= oz.CohTol, w, "FallCoherence " + F(r.FallCoherence) + " vs oracle " + F(oz.Coherence) + " (tolerance " + F(oz.CohTol) + ")");
            }
            if (oz.GateAmbiguous)
                s_OracleGateAmbiguous++;
            Check(r.ZoneByFallLine == oz.ByFall, w, "ZoneByFallLine " + r.ZoneByFallLine + " vs oracle " + oz.ByFall + " (|mean g| " + F(oz.MeanGrad) +
                                                    ", gate " + F(zp.MinMean) + "; coherence " + F(oz.Coherence) + ", gate " + F(zp.MinCoh) + ")");
            if (oz.ByFall)
                s_OracleFallKey++;
            else
                s_OracleHeightKey++;
            // R3 (§5.1): AxisLength = the fall-line key range of the inside cells + step (the depth
            // cap's L) when the key is the fall line, else 0. The oracle's L is a brute-force
            // min / max over every Mask cell (classifier: the clamped Mask runs' end cells).
            if (r.ZoneByFallLine == oz.ByFall)
            {
                s_LenAxisChecks++;
                double want = oz.ByFall ? (float)oz.L : 0.0;
                double err = Math.Abs(r.AxisLength - want);
                s_LenAxisWorstAbs = Math.Max(s_LenAxisWorstAbs, err);
                if (err == 0.0)
                    s_LenAxisExact++;
                Check(err <= 1e-3 * Math.Max(1.0, want), w, "AxisLength " + F(r.AxisLength) + " vs oracle key range + step " + F(want) + " (key " +
                                                            (oz.ByFall ? "fall line" : "height") + ")");
            }
            if (oz.ByFall && oz.ByCoherenceOnly)
                s_OracleCohKey++;
            int bandBad = 0;
            foreach (int q in cells)
                if (r.CellBand[q] != (byte)SkiSlopeClassifier.BandOf(v[q]))
                    bandBad++;
            Check(bandBad == 0, w, bandBad + " Mask cells with CellBand != BandOf(CellGrade)");
            Check(r.TopZoneCells == oz.TopCount && r.BottomZoneCells == oz.BotCount, w,
                "zones " + r.TopZoneCells + "/" + r.BottomZoneCells + " vs oracle " + oz.TopCount + "/" + oz.BotCount + " (z " + oz.Z + ")");
            if (oz.TopThr <= oz.BotThr)
                s_OracleShared++;
            Func<int, bool> inTop = oz.InTop;
            Func<int, bool> inBot = oz.InBot;

            // P90 from a sorted list.
            List<float> sorted = new List<float>(n);
            foreach (int q in cells)
                sorted.Add(v[q]);
            sorted.Sort();
            int c90 = (9 * n + 9) / 10;
            float p90 = (OBucket(sorted[c90 - 1]) + 1) / 1024f;
            Check(r.P90Grade == p90, w, "P90 " + Rt(r.P90Grade) + " vs oracle " + Rt(p90));

            // Exact minimax: binary search over the distinct values with a flood fill each.
            List<float> distinct = new List<float>();
            foreach (float x in sorted)
                if (distinct.Count == 0 || x != distinct[distinct.Count - 1])
                    distinct.Add(x);
            Func<float, bool> feasible = T => Reach(g, cells, q => inTop(q), q => v[q] <= T, inBot, conn);
            bool connected = feasible(distinct[distinct.Count - 1]);
            Check(r.Connected == connected, w, "Connected " + r.Connected + " vs oracle " + connected);
            if (connected)
            {
                s_OracleConnected++;
                int lo = 0, hi = distinct.Count - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (feasible(distinct[mid]))
                        hi = mid;
                    else
                        lo = mid + 1;
                }
                float minimax = distinct[lo];
                int bstar = OBucket(minimax);
                // §9.3: below 200 % the rating is the largest value of the minimax's bucket; in the
                // open last bucket (>= 2047/1024) it is the exact minimax (§9.3 as amended in the
                // R2 fixer round — "largest o in the bucket" there would be the largest value of
                // ANY cell above 199.9 %, avoidable or not).
                float expected = float.MinValue;
                if (bstar == 2047)
                {
                    expected = minimax;
                }
                else
                {
                    foreach (int q in cells)
                        if (OBucket(v[q]) == bstar && v[q] > expected)
                            expected = v[q];
                }
                Check(r.RatingGrade == expected, w, "rating " + Rt(r.RatingGrade) + " vs oracle " + (bstar == 2047 ? "exact minimax (open last bucket) " :
                                                    "max of the minimax bucket ") + Rt(expected) + " (exact minimax " + Rt(minimax) + ")");
                Check(r.RatingGrade >= minimax && r.RatingGrade - minimax < 1f / 1024f, w,
                    "rating " + Rt(r.RatingGrade) + " not within one bucket above the minimax " + Rt(minimax));
                if (r.RatingGrade > minimax)
                {
                    s_OracleAbove++;
                    s_OracleMaxAbove = Math.Max(s_OracleMaxAbove, r.RatingGrade - minimax);
                }
                if (bstar == 2047)
                    s_OracleLastBucket++;
                if (Check(r.CruxValid, w, "connected but CruxValid false"))
                {
                    bool inGrid = r.CruxI >= 0 && r.CruxI < nx && r.CruxJ >= 0 && r.CruxJ < nz;
                    int c = r.CruxJ * nx + r.CruxI;
                    if (Check(inGrid && g.Mask[c] != 0, w, "crux (" + r.CruxI + "," + r.CruxJ + ") not a Mask cell"))
                    {
                        Check(v[c] <= expected, w, "crux value " + Rt(v[c]) + " above the rating " + Rt(expected));
                        Check(r.CruxHeight == h[c], w, "CruxHeight " + F(r.CruxHeight) + " vs node height " + F(h[c]));
                        // The full §9.3 traversal contract, re-implemented: exact cell.
                        int oc = OracleCrux(g, oz, v, expected, conn);
                        if (Check(oc >= 0, w, "oracle BFS found no top->bottom path through o <= " + Rt(expected)))
                        {
                            Check(oc == c, w, "crux (" + r.CruxI + "," + r.CruxJ + ") o " + Rt(v[c]) + " vs oracle BFS crux (" + oc % nx + "," + oc / nx +
                                              ") o " + Rt(v[oc]));
                            s_OracleCrux++;
                        }
                    }
                }
            }
            else
            {
                s_OracleDisconnected++;
                Check(r.RatingGrade == r.P90Grade && !r.CruxValid, w, "disconnected: rating " + Rt(r.RatingGrade) + " must equal P90 " +
                                                                     Rt(r.P90Grade) + ", CruxValid " + r.CruxValid);
            }
            Check(r.Band == SkiSlopeClassifier.BandOf(r.RatingGrade), w, "Band " + r.Band + " != BandOf(rating)");
        }

        // ================================================================== bands / constants

        private static void BandTests()
        {
            // §9.2: 6 / 25 / 40 / 60 %, half-open.
            Check(SkiSlopeClassifier.GradeGreen == 0.06f && SkiSlopeClassifier.GradeBlue == 0.25f &&
                  SkiSlopeClassifier.GradeBlack == 0.40f && SkiSlopeClassifier.GradeDoubleBlack == 0.60f, "band", "grade constants != §9.2");
            Check(SkiSlopeClassifier.EndZoneShare == 0.10f && SkiSlopeClassifier.EndZoneMaxM2 == 2000f && SkiSlopeClassifier.RatingBuckets == 2048,
                "band", "end-zone / bucket constants != §9.3");
            Check(SkiSlopeClassifier.EndZoneDepthM == 20f && SkiSlopeClassifier.EndZoneMinMeanGrade == 0.03f, "band", "R2b end-zone constants != §9.8");
            Check(SkiSlopeClassifier.EndZoneMinCoherence == 0.5f, "band", "R2b key-gate coherence clause != §9.8 (as amended in the R2b fixer round)");
            float[] g = { -1f, 0f, 0.0599f, 0.06f, 0.2499f, 0.25f, 0.3999f, 0.40f, 0.5999f, 0.60f, 3f, float.NaN };
            SlopeBand[] e =
            {
                SlopeBand.Flat, SlopeBand.Flat, SlopeBand.Flat, SlopeBand.Green, SlopeBand.Green, SlopeBand.Blue,
                SlopeBand.Blue, SlopeBand.Black, SlopeBand.Black, SlopeBand.DoubleBlack, SlopeBand.DoubleBlack, SlopeBand.Flat,
            };
            for (int i = 0; i < g.Length; i++)
                Check(SkiSlopeClassifier.BandOf(g[i]) == e[i], "band", "BandOf(" + F(g[i]) + ") = " + SkiSlopeClassifier.BandOf(g[i]) + ", want " + e[i]);
            int[] bv = { 0, 6, 12, 20, 28 };
            for (int b = 0; b < 5; b++)
                Check(SkiSlopeClassifier.BaseValue((SlopeBand)b) == bv[b], "band", "BaseValue(" + (SlopeBand)b + ")");
            Check(SkiSlopeClassifier.BaseValue((SlopeBand)200) == 0, "band", "BaseValue(unknown) must be 0");
        }

        // ================================================================== (15) StepMultiple

        // Spec §2/§3 (decided 2026-09-30): up above 16384 estimated cells, back down below 12288.
        // Test-local copies on purpose (CellBudgetDown is private); the first check pins the
        // public constant to the spec value.
        private const double SpecCellBudget = 16384.0, SpecCellBudgetDown = 12288.0;

        private static void StepMultipleTests()
        {
            float t = Terrain.Texel;
            Check(SkiSlopeClassifier.CellBudget == (int)SpecCellBudget, "stepmultiple", "CellBudget = " + SkiSlopeClassifier.CellBudget + ", spec " + F(SpecCellBudget));
            Expect(0.0, t, 0, 1);
            Expect(200704.0, t, 0, 1);    // 16384·3.5²
            Expect(200705.0, t, 0, 2);
            Expect(802816.0, t, 0, 2);    // 16384·7²
            Expect(802817.0, t, 0, 3);
            Expect(160000.0, t, 2, 2);    // 13061 cells at m=1: inside the hysteresis band -> stay coarse
            Expect(150528.0, t, 2, 2);    // exactly 12288 at m=1: not below -> stay coarse
            Expect(148000.0, t, 2, 1);    // 12082 < 12288 -> back down
            Expect(200000.0, t, 1, 1);    // 16327 <= 16384 -> stay
            Expect(200800.0, t, 1, 2);    // 16392 > 16384 -> up
            Expect(40000.0, t, 3, 1);
            Expect(1000000.0, t, 1, 3);   // up jumps straight to the smallest fitting m
            Expect(double.NaN, t, 2, 2);
            Expect(-5.0, t, 0, 1);
            Expect(1000.0, 0f, 3, 3);
            Expect(1000.0, float.NaN, 0, 1);
            Expect(double.PositiveInfinity, t, 4, 4);

            // Properties over a sweep: within budget, hysteresis band respected, idempotent.
            for (double area = 1.0; area < 2.0e8; area *= 1.07)
            {
                int fresh = SkiSlopeClassifier.StepMultiple(area, t, 0);
                Check(Cells(area, t, fresh) <= SpecCellBudget && (fresh == 1 || Cells(area, t, fresh - 1) > SpecCellBudget),
                    "stepmultiple", "fresh m not the smallest within budget at area " + F(area));
                for (int cur = 1; cur <= 6; cur++)
                {
                    int m = SkiSlopeClassifier.StepMultiple(area, t, cur);
                    string w = "stepmultiple area " + F(area) + " cur " + cur;
                    Check(Cells(area, t, m) <= SpecCellBudget, w, "result m=" + m + " exceeds the budget");
                    if (Cells(area, t, cur) <= SpecCellBudget)
                        Check(m <= cur && (m == cur || Cells(area, t, m) < SpecCellBudgetDown), w, "left the hysteresis band (m=" + m + ")");
                    else
                        Check(m == fresh, w, "going up must land on the smallest fitting m");
                    Check(SkiSlopeClassifier.StepMultiple(area, t, m) == m, w, "not idempotent");
                }
            }
        }

        private static double Cells(double area, float texel, int m)
        {
            double s = (double)m * texel;
            return area / (s * s);
        }

        private static void Expect(double area, float texel, int cur, int want)
        {
            int got = SkiSlopeClassifier.StepMultiple(area, texel, cur);
            Check(got == want, "stepmultiple", "StepMultiple(" + F(area) + ", " + F(texel) + ", " + cur + ") = " + got + ", want " + want);
        }

        // ================================================================== (12) degenerate

        private static void DegenerateTests()
        {
            Stopwatch sw = Stopwatch.StartNew();
            Frame fr = Frame.Make(100.3, 200.7, 20);
            HeightFn plane = (x, z) => 1000.0 - 0.2 * fr.Along(x, z);
            double2[] ring = Rect(300, 60, fr);
            Terrain t = Terrain.For(plane, ring, 60);
            float3[] good = Pts(ring, false);
            float3 a = good[0];

            Degenerate("null points", t, null, 0, Step(1), SlopeStatus.TooFewPoints);
            Degenerate("count 0", t, good, 0, Step(1), SlopeStatus.TooFewPoints);
            Degenerate("one point", t, new[] { a }, 1, Step(1), SlopeStatus.TooFewPoints);
            Degenerate("two identical", t, new[] { a, a }, 2, Step(1), SlopeStatus.TooFewPoints);
            Degenerate("three identical", t, new[] { a, a, a }, 3, Step(1), SlopeStatus.TooFewPoints);
            Degenerate("sub-mm duplicates", t, new[] { a, a + new float3(0.0004f, 0f, 0f), a }, 3, Step(1), SlopeStatus.TooFewPoints);
            Degenerate("NaN x", t, new[] { a, new float3(float.NaN, 0f, 5f), good[2] }, 3, Step(1), SlopeStatus.NonFinite);
            Degenerate("Inf z", t, new[] { a, good[1], new float3(5f, 0f, float.PositiveInfinity) }, 3, Step(1), SlopeStatus.NonFinite);
            Degenerate("NaN y", t, new[] { a, good[1], new float3(good[2].x, float.NaN, good[2].z) }, 3, Step(1), SlopeStatus.NonFinite);
            Degenerate("step 0", t, good, good.Length, 0f, SlopeStatus.BadStep);
            Degenerate("step -1", t, good, good.Length, -1f, SlopeStatus.BadStep);
            Degenerate("step NaN", t, good, good.Length, float.NaN, SlopeStatus.BadStep);
            Degenerate("step +Inf", t, good, good.Length, float.PositiveInfinity, SlopeStatus.BadStep);
            Degenerate("step 100.5", t, good, good.Length, 100.5f, SlopeStatus.BadStep);
            // Sliver between two lattice rows: area >= 1 m², no node inside.
            {
                double z0 = Terrain.Origin.y + 300 * (double)Terrain.Texel;
                double2[] sliver =
                {
                    new double2(0, z0 + 0.3 * Terrain.Texel), new double2(100, z0 + 0.3 * Terrain.Texel),
                    new double2(100, z0 + 0.6 * Terrain.Texel), new double2(0, z0 + 0.6 * Terrain.Texel),
                };
                Terrain ts = Terrain.For(plane, sliver, 60);
                Degenerate("sliver", ts, Pts(sliver, false), 4, Step(1), SlopeStatus.NoCells);
            }
            {
                double2[] huge = { new double2(-3000, -3000), new double2(3000, -3000), new double2(3000, 3000), new double2(-3000, 3000) };
                Degenerate("6 km square", null, Pts(huge, false), 4, Step(1), SlopeStatus.TooLarge);
                float3[] far = { new float3(1e12f, 0, 0), new float3(1e12f + 1e6f, 0, 0), new float3(1e12f, 0, 1e6f) };
                Degenerate("1e12 coordinates", null, far, 3, Step(1), SlopeStatus.TooLarge);
            }

            // Plan with a null grid / Classify with null arguments: no throw.
            try
            {
                Check(SkiSlopeClassifier.Plan(good, good.Length, Terrain.Origin, Step(1), null) == SlopeStatus.NoCells, "degenerate", "Plan(null grid)");
                SlopeResult r = new SlopeResult();
                Prime(t, ring, r);
                Check(SkiSlopeClassifier.Classify(null, r) == SlopeStatus.NoCells, "degenerate", "Classify(null grid)");
                CheckCleared("Classify(null grid)", r, SlopeStatus.NoCells);
                SlopeGrid g = new SlopeGrid();
                Check(SkiSlopeClassifier.Classify(g, r) == SlopeStatus.NoCells, "degenerate", "Classify(never planned grid)");
                SkiSlopeClassifier.Plan(good, good.Length, Terrain.Origin, Step(1), g);
                Fill(g, t);
                Check(SkiSlopeClassifier.Classify(g, null) == SlopeStatus.NoCells, "degenerate", "Classify(null result)");
                Check(!CruxForTest(null, r, Zp.Shipped) && !CruxForTest(g, null, Zp.Shipped),
                    "degenerate", "CruxForTest(null) must refuse");
            }
            catch (Exception e)
            {
                Fail("degenerate", "null-argument path threw " + e.GetType().Name + ": " + e.Message);
            }

            // Heights problems after a good Plan.
            try
            {
                SlopeGrid g = new SlopeGrid();
                SlopeResult r = new SlopeResult();
                Prime(t, ring, r);
                SkiSlopeClassifier.Plan(good, good.Length, Terrain.Origin, Step(1), g);
                // Unfilled: Plan poisons Need heights.
                Check(SkiSlopeClassifier.Classify(g, r) == SlopeStatus.NonFinite, "degenerate", "unfilled Need heights must read NonFinite");
                CheckCleared("unfilled", r, SlopeStatus.NonFinite);
                Fill(g, t);
                int need = -1;
                for (int q = 0; q < g.Nx * g.Nz && need < 0; q++)
                    if (g.Need[q] != 0 && g.Mask[q] == 0)
                        need = q;
                float keep = g.Heights[need];
                g.Heights[need] = float.PositiveInfinity;
                Prime(t, ring, r);
                Check(SkiSlopeClassifier.Classify(g, r) == SlopeStatus.NonFinite, "degenerate", "Inf in a Need (outside-mask) height");
                CheckCleared("Inf height", r, SlopeStatus.NonFinite);
                g.Heights[need] = keep;
                float[] hs = g.Heights;
                g.Heights = null;
                Prime(t, ring, r);
                Check(SkiSlopeClassifier.Classify(g, r) == SlopeStatus.BadHeights, "degenerate", "Heights null");
                CheckCleared("Heights null", r, SlopeStatus.BadHeights);
                g.Heights = new float[g.Nx * g.Nz - 1];
                Prime(t, ring, r);
                Check(SkiSlopeClassifier.Classify(g, r) == SlopeStatus.BadHeights, "degenerate", "Heights short");
                CheckCleared("Heights short", r, SlopeStatus.BadHeights);
                g.Heights = hs;
                Check(SkiSlopeClassifier.Classify(g, r) == SlopeStatus.Ok, "degenerate", "restored heights classify Ok");
                // A non-Need NaN is never read.
                int other = -1;
                for (int q = 0; q < g.Nx * g.Nz && other < 0; q++)
                    if (g.Need[q] == 0)
                        other = q;
                if (other >= 0)
                {
                    g.Heights[other] = float.NaN;
                    Check(SkiSlopeClassifier.Classify(g, r) == SlopeStatus.Ok, "degenerate", "a NaN outside Need must be ignored");
                }
                // Step 100 exactly is allowed.
                double2[] big = Rect(2000, 600, Frame.Make(0.5, 0.5, 0));
                Check(SkiSlopeClassifier.Plan(Pts(big, false), 4, Terrain.Origin, 100f, g) == SlopeStatus.Ok, "degenerate", "step 100 must plan");
            }
            catch (Exception e)
            {
                Fail("degenerate", "heights path threw " + e.GetType().Name + ": " + e.Message);
            }

            // Tampered grids: the classifier must never index out of range or throw.
            TamperTests(t, ring);

            // Two points / collinear points are polyline mode, not errors.
            {
                SlopeGrid g = new SlopeGrid();
                SlopeResult r = new SlopeResult();
                float3[] two = { Pts(ring, false)[0], Pts(ring, false)[1] };
                Check(SkiSlopeClassifier.Plan(two, 2, Terrain.Origin, Step(1), g) == SlopeStatus.Ok && g.PolylineMode, "degenerate", "2 points -> polyline mode");
                float3 b = good[1];
                float3[] col = { a, (a + b) * 0.5f, b };
                Check(SkiSlopeClassifier.Plan(col, 3, Terrain.Origin, Step(1), g) == SlopeStatus.Ok && g.PolylineMode, "degenerate", "3 collinear points -> polyline mode");
                Fill(g, t);
                SlopeStatus st = SkiSlopeClassifier.Classify(g, r);
                Check(st == SlopeStatus.Polyline, "degenerate", "collinear polyline classify = " + st);
                PostCheck("degenerate/collinear", g, r, st, Zp.Shipped);
            }

            sw.Stop();
            Check(sw.Elapsed.TotalSeconds < 5.0, "degenerate", "took " + F(sw.Elapsed.TotalSeconds) + " s (limit 5)");
        }

        /// <summary>Hand-tampered grids (fields written after Plan): Mask on the border, run lists
        /// past the grid / overlapping / with a broken row index, NaN inside-cell heights, a Mask
        /// cell outside Need. Classify must not throw; when it rates, the outputs must be finite
        /// and internally consistent. R2b: every variant also with DiagonalRule on, and with the
        /// fall-line key forced (gate 0), so neither new path can index out of range.</summary>
        private static void TamperTests(Terrain t, double2[] ring)
        {
            float3[] pts = Pts(ring, false);
            for (int combo = 0; combo < 32; combo++)
            {
                int variant = combo % 8;
                bool diag = (combo / 8) % 2 == 1, forceFall = combo >= 16;
                string w = "degenerate/tamper #" + variant + (diag ? " diag" : "") + (forceFall ? " fall-key" : "");
                Zp zp = forceFall ? Zp.Shipped.FallKey() : Zp.Shipped;
                try
                {
                    SlopeGrid g = new SlopeGrid();
                    SlopeResult r = new SlopeResult();
                    SkiSlopeClassifier.Plan(pts, pts.Length, Terrain.Origin, Step(1), g);
                    g.DiagonalRule = diag;
                    Fill(g, t);
                    int nx = g.Nx, nz = g.Nz;
                    switch (variant)
                    {
                        case 0: // Mask on every border cell
                            for (int i = 0; i < nx; i++)
                            {
                                g.Mask[i] = 1;
                                g.Mask[(nz - 1) * nx + i] = 1;
                            }
                            for (int j = 0; j < nz; j++)
                            {
                                g.Mask[j * nx] = 1;
                                g.Mask[j * nx + nx - 1] = 1;
                            }
                            break;
                        case 1: // Mask runs widened past the grid
                            for (int k = 0; k < g.MaskRunCount; k++)
                            {
                                g.MaskRuns[2 * k] = -5;
                                g.MaskRuns[2 * k + 1] = nx + 5;
                            }
                            break;
                        case 5: // overlapping runs: every row's first run duplicated
                        {
                            List<int> runs = new List<int>();
                            int[] start = new int[nz + 1];
                            for (int j = 0; j < nz; j++)
                            {
                                start[j] = runs.Count / 2;
                                for (int k = g.MaskRowStart[j]; k < g.MaskRowStart[j + 1]; k++)
                                {
                                    runs.Add(g.MaskRuns[2 * k]);
                                    runs.Add(g.MaskRuns[2 * k + 1]);
                                    if (k == g.MaskRowStart[j])
                                    {
                                        runs.Add(g.MaskRuns[2 * k]);
                                        runs.Add(g.MaskRuns[2 * k + 1]);
                                    }
                                }
                            }
                            start[nz] = runs.Count / 2;
                            g.MaskRuns = runs.ToArray();
                            g.MaskRowStart = start;
                            g.MaskRunCount = start[nz];
                            break;
                        }
                        case 6: // Need row index not monotone
                            if (nz > 4)
                                g.NeedRowStart[nz / 2] = g.NeedRowStart[nz] + 3;
                            break;
                        case 7: // Need runs pointing past the run array
                            g.NeedRowStart[nz] = g.NeedRuns.Length;
                            break;
                        case 2: // NaN in an inside cell that is not Need (Need cleared there)
                            for (int q = 0; q < nx * nz; q++)
                            {
                                if (g.Mask[q] != 0)
                                {
                                    g.Need[q] = 0;
                                    g.Heights[q] = float.NaN;
                                    break;
                                }
                            }
                            break;
                        case 3: // every inside height NaN outside Need
                            for (int q = 0; q < nx * nz; q++)
                            {
                                if (g.Mask[q] != 0)
                                {
                                    g.Need[q] = 0;
                                    g.Heights[q] = float.NaN;
                                }
                            }
                            break;
                        default: // Mask everywhere, one full-width run per row
                            for (int q = 0; q < nx * nz; q++)
                                g.Mask[q] = 1;
                            g.MaskRuns = new int[2 * nz];
                            g.MaskRowStart = new int[nz + 1];
                            for (int j = 0; j < nz; j++)
                            {
                                g.MaskRowStart[j] = j;
                                g.MaskRuns[2 * j] = 0;
                                g.MaskRuns[2 * j + 1] = nx - 1;
                            }
                            g.MaskRowStart[nz] = nz;
                            g.MaskRunCount = nz;
                            break;
                    }
                    SlopeStatus st = Classify(g, r, zp);
                    if (variant == 1 || variant >= 5)
                        Check(st == SlopeStatus.NoCells, w, "a broken run list must be refused (NoCells), got " + st);
                    if (st == SlopeStatus.Ok || st == SlopeStatus.ShortRun)
                    {
                        Check(!r.CruxValid || (r.CruxI >= 0 && r.CruxI < nx && r.CruxJ >= 0 && r.CruxJ < nz), w, "crux index out of the grid");
                        Check(!float.IsNaN(r.P90Grade), w, "P90 NaN");
                        Check(r.Connected || r.RatingGrade == r.P90Grade, w, "disconnected rating != P90");
                        Check(r.DiagonalRule == diag, w, "DiagonalRule not copied");
                    }
                }
                catch (Exception e)
                {
                    Fail(w, "threw " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        private static void Prime(Terrain t, double2[] ring, SlopeResult r)
        {
            SlopeGrid g = new SlopeGrid();
            SkiSlopeClassifier.Plan(Pts(ring, false), ring.Length, Terrain.Origin, Step(1), g);
            Fill(g, t);
            SkiSlopeClassifier.Classify(g, r);
        }

        private static void Degenerate(string name, Terrain t, float3[] pts, int count, float step, SlopeStatus want)
        {
            string w = "degenerate/" + name;
            try
            {
                SlopeGrid g = new SlopeGrid();
                SlopeResult r = new SlopeResult();
                // Prime both objects with a successful run so "cleared" is meaningful.
                Frame fr = Frame.Make(0.3, 0.3, 0);
                double2[] ring = Rect(100, 40, fr);
                Terrain tp = Terrain.For((x, z) => 1000.0 - 0.2 * x, ring, 60);
                SkiSlopeClassifier.Plan(Pts(ring, false), 4, Terrain.Origin, Step(1), g);
                Fill(g, tp);
                SkiSlopeClassifier.Classify(g, r);

                SlopeStatus ps = SkiSlopeClassifier.Plan(pts, count, Terrain.Origin, step, g);
                Check(ps == want, w, "Plan = " + ps + ", want " + want);
                Check(g.Nx == 0 && g.Nz == 0 && g.InsideCells == 0 && g.NeedCells == 0 && g.PointCount == 0 && !g.PolylineMode,
                    w, "grid not cleared");
                SlopeStatus cs = SkiSlopeClassifier.Classify(g, r);
                Check(cs == want, w, "Classify after failed Plan = " + cs + ", want " + want);
                CheckCleared(w, r, want);
            }
            catch (Exception e)
            {
                Fail(w, "threw " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void CheckCleared(string w, SlopeResult r, SlopeStatus want)
        {
            bool shares = true;
            for (int b = 0; b < 5; b++)
                shares &= r.BandShare[b] == 0f;
            Check(r.Status == want && r.Band == SlopeBand.Flat && r.RatingGrade == 0f && r.MeanGrade == 0f && r.Drop == 0f &&
                  r.MinHeight == 0f && r.MaxHeight == 0f && r.Area == 0f && r.FallCoherence == 0f &&
                  r.FallLine.x == 0f && r.FallLine.y == 0f && shares && r.ValidWindows == 0 &&
                  r.Nx == 0 && r.Nz == 0 && r.I0 == 0 && r.J0 == 0 && r.Step == 0f && r.SegmentCount == 0,
                w, "result not cleared (status " + r.Status + ", rating " + F(r.RatingGrade) + ", Nx " + r.Nx + ")");
            Check(r.SteepestGrade == 0f && r.P90Grade == 0f && r.CruxHeight == 0f && !r.Connected && !r.CruxValid && r.CruxI == 0 &&
                  r.CruxJ == 0 && r.TopZoneCells == 0 && r.BottomZoneCells == 0,
                w, "R2 fields not cleared (steepest " + F(r.SteepestGrade) + ", p90 " + F(r.P90Grade) + ", connected " + r.Connected + ")");
            Check(!r.ZoneByFallLine && !r.DiagonalRule && r.MeanGradient == 0f, w,
                "R2b fields not cleared (zoneByFallLine " + r.ZoneByFallLine + ", diagonalRule " + r.DiagonalRule + ")");
        }

        // ================================================================== (14) Mask / Need / CellBand

        private static void MaskNeedTests()
        {
            List<KeyValuePair<string, double2[]>> rings = new List<KeyValuePair<string, double2[]>>();
            foreach (double ang in new double[] { 0, 17, 45, 211 })
            {
                double2[] r = Rect(300, 60, Frame.Make(1234.56, -789.01, ang));
                rings.Add(new KeyValuePair<string, double2[]>("rect a" + ang, r));
                rings.Add(new KeyValuePair<string, double2[]>("rect cw a" + ang, Reversed(r)));
            }
            // Concave L and a comb; a triangle; an axis-aligned rect with edges ON lattice rows.
            rings.Add(new KeyValuePair<string, double2[]>("L", new[]
            {
                new double2(10.2, 10.7), new double2(210.2, 10.7), new double2(210.2, 60.7), new double2(60.2, 60.7),
                new double2(60.2, 250.7), new double2(10.2, 250.7),
            }));
            List<double2> comb = new List<double2>();
            for (int k = 0; k < 8; k++)
            {
                comb.Add(new double2(-500.0 + k * 30.0, 100.0));
                comb.Add(new double2(-500.0 + k * 30.0 + 15.0, 180.0 + 13.0 * (k % 3)));
            }
            comb.Add(new double2(-500.0 + 8 * 30.0, 100.0));
            comb.Add(new double2(-500.0 + 8 * 30.0, 60.0));
            comb.Add(new double2(-500.0, 60.0));
            rings.Add(new KeyValuePair<string, double2[]>("comb", comb.ToArray()));
            // R2 fixer round: U shapes whose rows hold two Mask runs; a 5 m gap makes their Need
            // runs merge, a 300 m gap keeps them apart (the run lists' two merge cases).
            rings.Add(new KeyValuePair<string, double2[]>("U gap 5 m", UShape(800.3, -900.6, 12, 150, 5)));
            rings.Add(new KeyValuePair<string, double2[]>("U gap 300 m", UShape(800.3, -900.6, 25, 200, 300)));
            rings.Add(new KeyValuePair<string, double2[]>("triangle", new[] { new double2(3000.1, 3000.2), new double2(3170.3, 3044.4), new double2(3050.5, 3190.6) }));
            {
                double x0 = Terrain.Origin.x + 500 * (double)Terrain.Texel, z0 = Terrain.Origin.y + 700 * (double)Terrain.Texel;
                rings.Add(new KeyValuePair<string, double2[]>("on-lattice rect", new[]
                {
                    new double2(x0, z0), new double2(x0 + 40 * Terrain.Texel, z0), new double2(x0 + 40 * Terrain.Texel, z0 + 12 * Terrain.Texel),
                    new double2(x0, z0 + 12 * Terrain.Texel),
                }));
            }

            int compared = 0, skipped = 0;
            foreach (KeyValuePair<string, double2[]> kv in rings)
            {
                for (int m = 1; m <= 2; m++)
                {
                    string w = "mask/" + kv.Key + " m" + m;
                    float3[] pts = Pts(kv.Value, false);
                    double2[] v = AsDouble(pts);
                    SlopeGrid g = s_Grid;
                    SlopeStatus st = SkiSlopeClassifier.Plan(pts, pts.Length, Terrain.Origin, Step(m), g);
                    g.DiagonalRule = DiagFor(m);   // the caller's rule (R2b)
                    if (!Check(st == SlopeStatus.Ok && !g.PolylineMode, w, "Plan = " + st))
                        continue;
                    int nx = g.Nx, nz = g.Nz, inside = 0, mism = 0;
                    string first = null;
                    for (int j = 0; j < nz; j++)
                    {
                        for (int i = 0; i < nx; i++)
                        {
                            double2 p = NodeWorld(g, i, j);
                            if (OracleBoundaryDistance(v, p) < 1e-3)
                            {
                                skipped++;
                                if (g.Mask[j * nx + i] != 0)
                                    inside++;
                                continue;
                            }
                            compared++;
                            bool want = OracleInside(v, p);
                            bool got = g.Mask[j * nx + i] != 0;
                            if (got)
                                inside++;
                            if (want != got)
                            {
                                mism++;
                                if (first == null)
                                    first = "node (" + i + "," + j + ") world " + F(p.x) + "," + F(p.y) + " oracle " + want;
                            }
                        }
                    }
                    Check(mism == 0, w, mism + " Mask nodes disagree with winding-number PIP, first " + first);
                    Check(inside == g.InsideCells, w, "InsideCells " + g.InsideCells + " vs counted " + inside);
                    // Brute-force Chebyshev dilation, and the border margin.
                    int needCount = 0, needMism = 0, border = 0;
                    for (int j = 0; j < nz; j++)
                    {
                        for (int i = 0; i < nx; i++)
                        {
                            bool want = false;
                            for (int b = -SkiSlopeClassifier.Dilation; b <= SkiSlopeClassifier.Dilation && !want; b++)
                                for (int a = -SkiSlopeClassifier.Dilation; a <= SkiSlopeClassifier.Dilation && !want; a++)
                                {
                                    int ii = i + a, jj = j + b;
                                    if (ii >= 0 && jj >= 0 && ii < nx && jj < nz && g.Mask[jj * nx + ii] != 0)
                                        want = true;
                                }
                            bool got = g.Need[j * nx + i] != 0;
                            if (got)
                                needCount++;
                            if (got != want)
                                needMism++;
                            if (g.Mask[j * nx + i] != 0 &&
                                (i < SkiSlopeClassifier.Dilation + 1 || j < SkiSlopeClassifier.Dilation + 1 ||
                                 i >= nx - SkiSlopeClassifier.Dilation - 1 || j >= nz - SkiSlopeClassifier.Dilation - 1))
                                border++;
                        }
                    }
                    Check(needMism == 0, w, needMism + " Need nodes differ from the brute-force dilation");
                    Check(needCount == g.NeedCells, w, "NeedCells " + g.NeedCells + " vs counted " + needCount);
                    CheckRuns(w, g);
                    Check(border == 0, w, border + " Mask nodes within Dilation + 1 of the grid border");
                    for (int q = 0; q < nx * nz; q++)
                    {
                        if (g.Need[q] != 0 && !float.IsNaN(g.Heights[q]))
                        {
                            Fail(w, "Plan did not poison Need height " + q);
                            break;
                        }
                    }

                    // CellBand / CellGrade / shares on a filled plane.
                    Frame fr = Frame.Make(v[0].x, v[0].y, 33);
                    Terrain t = Terrain.For((x, z) => 1000.0 - 0.2 * fr.Along(x, z), kv.Value, 60);
                    Fill(g, t);
                    SlopeResult r = s_Result;
                    SlopeStatus cs = SkiSlopeClassifier.Classify(g, r);
                    Check(cs == SlopeStatus.Ok || cs == SlopeStatus.ShortRun, w, "Classify = " + cs);
                    PostCheck(w, g, r, cs, Zp.Shipped);
                    int bad = 0, gradeBad = 0;
                    for (int q = 0; q < nx * nz; q++)
                    {
                        bool inMask = g.Mask[q] != 0;
                        byte cb = r.CellBand[q];
                        if (inMask ? cb > 4 : cb != SkiSlopeClassifier.BandOutside)
                            bad++;
                        if (inMask && (cb != (byte)SkiSlopeClassifier.BandOf(r.CellGrade[q]) || !(r.CellGrade[q] >= 0f)))
                            gradeBad++;
                    }
                    Check(bad == 0, w, bad + " CellBand entries break '255 exactly outside the Mask'");
                    Check(gradeBad == 0, w, gradeBad + " Mask cells with CellBand != BandOf(CellGrade) or a negative/NaN CellGrade");
                    Check(r.CellGrade != null && r.CellGrade.Length >= nx * nz, w, "CellGrade shorter than the grid");
                    double sum = 0;
                    for (int b = 0; b < 5; b++)
                        sum += r.BandShare[b];
                    Check(Math.Abs(sum - 1.0) <= 1e-5, w, "BandShare sums to " + F(sum));
                    Check(Math.Abs(r.Area - g.InsideCells * (double)g.Step * g.Step) <= 1e-3 * r.Area, w, "Area");
                    Check(r.Nx == g.Nx && r.Nz == g.Nz && r.I0 == g.I0 && r.J0 == g.J0 && r.Step == g.Step && r.Origin.Equals(g.Origin), w, "geometry copy");
                }
            }
            Console.WriteLine("INFO  mask: " + compared + " nodes compared against winding-number PIP, " + skipped + " within 1 mm of the boundary skipped");

            // Polyline Need covers every bilinear read along the segments.
            foreach (double ang in new double[] { 0, 23, 45, 118 })
            {
                for (int m = 1; m <= 2; m++)
                {
                    string w = "need/polyline a" + ang + " m" + m;
                    Frame fr = Frame.Make(-2000.3, 1500.8, ang);
                    // A dog-leg with < 1 m² shoelace area: 3 points, still polyline mode.
                    double2[] line = { fr.World(0, 0), fr.World(140, 0.002), fr.World(230, 0) };
                    float3[] pts = Pts(line, false);
                    SlopeGrid g = s_Grid;
                    SlopeStatus st = SkiSlopeClassifier.Plan(pts, pts.Length, Terrain.Origin, Step(m), g);
                    if (!Check(st == SlopeStatus.Ok, w, "Plan = " + st))
                        continue;
                    Check(g.PolylineMode, w, "expected polyline mode");
                    int missing = 0, count = 0;
                    for (int q = 0; q < g.Nx * g.Nz; q++)
                        if (g.Need[q] != 0)
                            count++;
                    Check(count == g.NeedCells, w, "NeedCells " + g.NeedCells + " vs counted " + count);
                    CheckRuns(w, g);
                    for (int k = 0; k + 1 < g.PointCount; k++)
                    {
                        float3 a = g.Points[k], b = g.Points[k + 1];
                        for (double tt = 0; tt <= 1.0; tt += 0.0005)
                        {
                            double x = a.x + (b.x - a.x) * tt, z = a.z + (b.z - a.z) * tt;
                            double u = (x - g.Origin.x) / g.Step - g.I0, vv = (z - g.Origin.y) / g.Step - g.J0;
                            int i0 = (int)Math.Floor(u), j0 = (int)Math.Floor(vv);
                            for (int dj = 0; dj <= 1; dj++)
                                for (int di = 0; di <= 1; di++)
                                {
                                    int ii = i0 + di, jj = j0 + dj;
                                    if (ii < 0 || jj < 0 || ii >= g.Nx || jj >= g.Nz || g.Need[jj * g.Nx + ii] == 0)
                                        missing++;
                                }
                        }
                    }
                    Check(missing == 0, w, missing + " bilinear reads along the polyline fall outside Need");
                }
            }
        }

        /// <summary>Plan's run lists (R2 fixer round) against a brute-force row scan: Mask runs
        /// (ring mode; none in polyline mode) and Need runs are exact, sorted, disjoint and
        /// non-adjacent, row index monotone, counts consistent.</summary>
        private static void CheckRuns(string w, SlopeGrid g)
        {
            int nx = g.Nx, nz = g.Nz;
            for (int pass = 0; pass < 2; pass++)
            {
                bool mask = pass == 0;
                byte[] bits = mask ? g.Mask : g.Need;
                int[] runs = mask ? g.MaskRuns : g.NeedRuns, start = mask ? g.MaskRowStart : g.NeedRowStart;
                int count = mask ? g.MaskRunCount : g.NeedRunCount;
                string what = mask ? "Mask" : "Need";
                if (!Check(start != null && start.Length >= nz + 1 && start[0] == 0 && start[nz] == count, w, what + " run row index broken"))
                    continue;
                int bad = 0, expectRuns = 0;
                for (int j = 0; j < nz && bad == 0; j++)
                {
                    // brute-force maximal runs of this row
                    List<int> want = new List<int>();
                    for (int i = 0; i < nx; i++)
                    {
                        bool on = bits[j * nx + i] != 0 && !(mask && g.PolylineMode);
                        if (!on)
                            continue;
                        int a = i;
                        while (i + 1 < nx && bits[j * nx + i + 1] != 0)
                            i++;
                        want.Add(a);
                        want.Add(i);
                    }
                    expectRuns += want.Count / 2;
                    if (start[j + 1] - start[j] != want.Count / 2)
                    {
                        bad++;
                        Fail(w, what + " runs of row " + j + ": " + (start[j + 1] - start[j]) + " vs brute force " + want.Count / 2);
                        break;
                    }
                    for (int k = 0; k < want.Count / 2; k++)
                    {
                        int kk = start[j] + k;
                        if (runs[2 * kk] != want[2 * k] || runs[2 * kk + 1] != want[2 * k + 1])
                        {
                            bad++;
                            Fail(w, what + " run " + k + " of row " + j + " = [" + runs[2 * kk] + "," + runs[2 * kk + 1] + "], brute force [" + want[2 * k] + "," +
                                    want[2 * k + 1] + "]");
                            break;
                        }
                    }
                }
                Check(bad != 0 || expectRuns == count, w, what + " run count " + count + " vs brute force " + expectRuns);
            }
        }

        // ================================================================== R2 (1) planes

        private static void PlaneTests()
        {
            // §9.4 (1): {3, 10.5, 20, 32.5, 50, 75 %}, every fixture >= 0.5 % from 6/25/40/60.
            double[] grades = { 0.03, 0.105, 0.20, 0.325, 0.50, 0.75 };
            double2[] offsets = { new double2(0, 0), new double2(7000, -7000) };
            string[] variants = { "ccw", "cw", "dup" };
            double[] lo = { 1, 1 }, hi = { -1, -1 }, sLo = { 1, 1 }, sHi = { -1, -1 }, fallWorst = { 0, 0 };
            foreach (double g in grades)
            {
                foreach (double ang in Angles7)
                {
                    foreach (double2 off in offsets)
                    {
                        double rad = ang * Math.PI / 180.0;
                        Frame fr = Frame.Make(off.x - 150 * Math.Cos(rad), off.y - 150 * Math.Sin(rad), ang);
                        HeightFn f = (x, z) => 1000.0 - g * fr.Along(x, z);
                        double2[] ring = Rect(300, 60, fr);
                        Terrain t = Terrain.For(f, ring, 60);
                        foreach (string variant in variants)
                        {
                            float3[] pts = Pts(variant == "cw" ? Reversed(ring) : ring, variant == "dup");
                            for (int m = 1; m <= 2; m++)
                            {
                                string w = "plane g" + P(g) + " a" + ang + " off" + F(off.x) + " " + variant + " m" + m;
                                SlopeStatus st = RateWith(t, pts, m, s_Grid, s_Result, Zp.Shipped, w);
                                SlopeResult r = s_Result;
                                if (!Check(st == SlopeStatus.Ok, w, "status " + st))
                                    continue;
                                SlopeBand want = SkiSlopeClassifier.BandOf((float)g);
                                Check(r.Band == want, w, "band " + r.Band + ", want " + want + " (rating " + P(r.RatingGrade) + ")");
                                double d = r.RatingGrade - g, ds = r.SteepestGrade - g;
                                lo[m - 1] = Math.Min(lo[m - 1], d);
                                hi[m - 1] = Math.Max(hi[m - 1], d);
                                sLo[m - 1] = Math.Min(sLo[m - 1], ds);
                                sHi[m - 1] = Math.Max(sHi[m - 1], ds);
                                Check(Math.Abs(d) <= 0.005, w, "rating " + P(r.RatingGrade) + " vs " + P(g));
                                Check(Math.Abs(ds) <= 0.005, w, "steepest " + P(r.SteepestGrade) + " vs " + P(g));
                                Check(r.Connected, w, "not Connected");
                                bool cruxIn = r.CruxValid && r.CruxI >= 0 && r.CruxI < s_Grid.Nx && r.CruxJ >= 0 && r.CruxJ < s_Grid.Nz &&
                                              s_Grid.Mask[r.CruxJ * s_Grid.Nx + r.CruxI] != 0;
                                Check(cruxIn, w, "crux not inside the Mask (valid " + r.CruxValid + ")");
                                Check(r.BandShare[(int)want] >= 0.95f, w, "share[" + want + "] = " + F(r.BandShare[(int)want]));
                                Check(Math.Abs(r.MeanGrade - g) <= 0.005, w, "mean " + P(r.MeanGrade));
                                double dot = r.FallLine.x * fr.Ca + r.FallLine.y * fr.Sa;
                                double deg = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot))) * 180.0 / Math.PI;
                                fallWorst[m - 1] = Math.Max(fallWorst[m - 1], deg);
                                Check(deg <= 2.0, w, "fall line off by " + F(deg) + " deg");
                                Check(r.FallCoherence > 0.99f, w, "fall coherence " + F(r.FallCoherence));
                            }
                        }
                    }
                }
            }
            for (int m = 1; m <= 2; m++)
                Console.WriteLine("INFO  planes m=" + m + ": rating - g in [" + P(lo[m - 1]) + ", " + P(hi[m - 1]) + "], steepest - g in [" +
                                  P(sLo[m - 1]) + ", " + P(sHi[m - 1]) + "], fall line worst " + F(fallWorst[m - 1]) + " deg (" +
                                  grades.Length * Angles7.Length * 2 * 3 + " cases)");
        }

        // ================================================================== R2 (2) rotation + translation

        private delegate HeightFn LocalFixture(Frame fr);

        /// <summary>The step the feedback system would actually use for a run of this area
        /// (no hysteresis history). Invariants that depend on lattice resolution are ASSERTED at
        /// this step and REPORTED (INFO) at the other one.</summary>
        private static int OperatingStep(double length, double width)
        {
            return SkiSlopeClassifier.StepMultiple(length * width, Terrain.Texel, 0);
        }

        /// <summary>Area above which StepMultiple picks m = 2 (CellBudget · texel² = 200,704 m²),
        /// and above which it picks m = 3 (CellBudget · (2·texel)² = 802,816 m²).</summary>
        private static readonly double M2Area = SkiSlopeClassifier.CellBudget * (double)Terrain.Texel * Terrain.Texel;
        private static readonly double M3Area = 4.0 * M2Area;

        private static string Mode(bool asserted)
        {
            return asserted ? "asserted" : "report only: not the operating step";
        }

        private static string Mode(bool asserted, int m)
        {
            string op = "operating step for runs " + (m == 1 ? "<= " + F(M2Area) : F(M2Area) + "-" + F(M3Area)) + " m²";
            return (asserted ? "asserted: " : "report only: ") + op;
        }

        // Regression envelopes for measured gaps, keyed by case name. Bank (8) (on SteepestGrade,
        // the R1 statistic, unchanged): spec bound not met at m = 2 — WARN SPEC-GAP, envelope ~0.5 %
        // beyond the measured worst (budget round 2026-09-30: -2.66 %). Invariance (2) is §9.4
        // "(m = 1)": at m = 1 a measured excess is a WARN SPEC-GAP; at m = 2 (the "xl" runs) it is
        // outside (2)'s scope and only kept from widening by the envelope (INFO). Values next to
        // each key are the measured worst shifts (R2 round, 2026-09-30).
        private static readonly Dictionary<string, double> s_GapEnvelope = new Dictionary<string, double>
        {
            { "bank 12 w60", 0.032 },          // bank 12 m @ 50 % at m = 2: steepest - oracle down to -2.66 %
            { "bank 12 w80", 0.032 },          // -2.66 %
            { "bank 12 w100", 0.032 },         // -2.66 %
            // (2) at m = 1, WARN SPEC-GAP: a knife-edge ridge (12 % along the crest, 25 % flanks).
            // The easiest line runs down the crest, and the crest cells' values depend on where
            // the crest falls between lattice nodes (12 % on a node, up to ~20 % between). R2
            // (height zones on the flanks) read 13.83-19.90 %, worst shift +6.07 %; R2b (fall-line
            // zones at the crest's ends, §9.8) reads 12.29-15.23 % (all Green), worst shift +1.69 %
            // (skeptic 1.69 %); envelope tightened from 6.6 % (2026-10-01).
            { "inv ridge m1", 0.022 },
            // (2) at m = 1, WARN SPEC-GAP (R2b, §9.8 "long ridge"): the 2,000 × 80 ridge on the
            // fall-line key reads 12.29-15.33 % over 3 starts × 7 angles, worst shift +1.78 %
            // (skeptic 1.78 %, envelope ~2.3 %). R2 read 26.8-27.7 % (bottom zone on a flank).
            { "inv long ridge m1", 0.023 },
            // R2's R2-e x40 gap (-2.81 %, height-binned top zone ending ~6.5 m above the wall's foot)
            // is gone under R2b's ~20 m fall-line zone: 44.90-45.18 %, worst shift -0.28 % — key removed.
            // (2) at m = 2 (outside §9.4 (2)'s m = 1 scope), INFO + envelope. R2's 8-connected path
            // slipped diagonally through a 12 m bank's one-cell-wide ridge of high values (worst
            // -7.21 / -6.41 / -7.06 %); R2b's diagonal rule (§9.8) closes that: re-measured
            // 2026-10-01 at 31.50-34.16 / 31.23-34.16 / 31.64-34.16 % (reference 33.35 %).
            { "inv xl bank12@50 w60 m2", 0.024 },   // worst -1.85 %
            { "inv xl bank12@50 w80 m2", 0.027 },   // worst -2.12 %
            { "inv xl bank12@50 w100 m2", 0.023 },  // worst -1.71 %
            // The knife-edge ridge at m = 2 under the diagonal rule: a crest running diagonally
            // across the lattice is a diagonal chain of cells, which the rule only links through
            // an orthogonal (flank) cell, so at 37° / 45° / 211° the crux reads 20.2-20.5 % for a
            // 14.04 % reference (12.53-20.49 %, worst +6.45 %; R2: worst -5.60 %). The predicted
            // trade-off of the rule (§9.3 R2 fixer round: "a diagonal ridge crest reading up to
            // 20.5 % instead of ~14 %"); all Green.
            { "inv xl ridge m2", 0.070 },           // worst +6.45 %
            // R2b fixer round (ZoneDepthTests, WARN SPEC-GAP against §9.8's "~20 m of run whatever
            // the width"): zone depth in METRES down (up) the run from a narrowing top (bottom) end,
            // measured worst + one step (3.5 m) over the 8 frames. The depth cap is a cell count
            // from the MEAN width, so a 20 m end of a 106.7 m-mean run gets ~5× EndZoneDepthM.
            { "zone depth trail top 20 m -> 150 m bowl, 45% pitch s20-80", 110.2 },              // 103.0-106.7 m
            { "zone depth trail top 20 m -> 150 m bowl, 45% pitch s40-80", 110.2 },              // 103.0-106.7 m
            { "zone depth top 200 m 50 wide -> 150, 45% wall s0-40", 52.2 },                     // 44.8-48.7 m
            { "zone depth cat track: 150 -> 20 m, 45% pitch 30-70 m above bottom", 120.6 },      // 108.6-117.1 m
            { "zone depth cat track: 150 -> 20 m, 45% pitch 40-100 m above bottom", 120.6 },     // 108.6-117.1 m
            { "zone depth 150 wide -> last 200 m 50, 45% wall 0-40 m above bottom", 50.8 },      // 45.1-47.3 m
        };

        // Crux (RatingGrade) regression envelopes of the full-width banks at m = 2 (R2 fixer
        // round, review finding tests-1; INFO, no spec bound: §9.4 states none for the crux at
        // m = 2). ~0.5 % beyond the measured worst crux - oracle. R2's 8-connected path slipped
        // diagonally across a one-cell-wide band of high values (-8.06 / -6.65 / -4.45 %, 5 m bank
        // -1.28 / -3.92 %); R2b's diagonal rule (§9.8) closes the slip: re-measured 2026-10-01
        // (skeptic: -2.88 % at 60 m, -2.66 % at 80 m and 30 m, -0.15 % for the 5 m bank) and
        // tightened. Absent keys = 1 %.
        private static readonly Dictionary<string, double> s_CruxEnvelope = new Dictionary<string, double>
        {
            { "bank crux 12 w60", 0.034 },   // -2.88 %
            { "bank crux 12 w80", 0.032 },   // -2.66 %
            { "bank crux 12 w100", 0.035 },  // -2.95 %
            { "bank crux 5 w60", 0.010 },    // -0.46 %
            { "bank crux 5 w80", 0.007 },    // -0.15 %
            { "bank crux 5 w100", 0.007 },   // -0.15 %
            // Short full-width headwalls at m = 1 (ShortHeadwallTests; no spec bound — §9.4 R2-c
            // is a 60 m pitch, §3 (8) are 12 m / 5 m banks): a pitch of length L at 50 % in
            // 10.5 % has a 20 m-window plateau only 20 - L m wide, narrower than a 3.5 m cell for
            // L > 16.5 m, so the lattice nodes straddle the peak and every per-cell value reads
            // below it (falls ~2 %/m off the plateau). Measured worst (R2 fixer round):
            { "short headwall x16 m1", 0.025 },            // crux -1.99 %
            { "short headwall x18 m1", 0.039 },            // crux -3.36 %
            { "short headwall x20 m1", 0.052 },            // crux -4.66 %
            { "short headwall steepest x18 m1", 0.020 },   // SteepestGrade -1.52 %
            { "short headwall steepest x20 m1", 0.040 },   // SteepestGrade -3.48 %
        };

        private static int s_SpecGaps;

        private struct InvCase
        {
            public string Name;
            public LocalFixture Fx;
            public double Len, Width;

            public InvCase(string name, LocalFixture fx, double len, double width)
            {
                Name = name;
                Fx = fx;
                Len = len;
                Width = width;
            }
        }

        private static void InvarianceTests()
        {
            List<InvCase> cases = new List<InvCase>();
            foreach (KeyValuePair<string, LocalFixture> kv in LocalFixtures())
                cases.Add(new InvCase(kv.Key, kv.Value, kv.Key == "runout" ? 600 : 300, 60));
            LocalFixture bank = fr => (x, z) => BankProfile(fr.Along(x, z), 12, 0.50);
            LocalFixture plane20 = fr => (x, z) => 1000.0 - 0.20 * fr.Along(x, z);
            LocalFixture longRunout = fr => (x, z) =>
            {
                double s = fr.Along(x, z);
                return s < 600.0 ? 1000.0 - 0.30 * s : 820.0 - 0.03 * (s - 600.0);
            };
            LocalFixture ridge = fr => (x, z) => 1000.0 - 0.12 * fr.Along(x, z) - 0.25 * Math.Abs(fr.Across(x, z));
            LocalFixture crest = fr => (x, z) => CrestProfile(fr.Along(x, z));
            LocalFixture headwall = fr => (x, z) => PitchProfile(0.105, 170, 60, 0.45)(fr.Along(x, z), 0);
            cases.Add(new InvCase("long plane20", plane20, 2000, 80));
            cases.Add(new InvCase("long runout", longRunout, 2000, 80));
            cases.Add(new InvCase("long ridge", ridge, 2000, 80));
            cases.Add(new InvCase("long crest", crest, 2000, 80));
            cases.Add(new InvCase("long bank12@50 w20", bank, 2600, 20));
            cases.Add(new InvCase("long bank12@50 w30", bank, 1700, 30));
            cases.Add(new InvCase("long bank12@50 w60", bank, 900, 60));
            cases.Add(new InvCase("headwall45 400x100", headwall, 400, 100));
            // R2 fixer round, review finding tests-2: R2-e's 40 m top headwall ran ~6.5 m past R2's
            // height-binned top zone (~33.5 m on a 60 m-wide run), and o falls ~1.7 %/m across that
            // transition, so where the sub-cell zone boundary fell moved the crux by several %. R2b's
            // fall-line zone is ~20 m deep, 20 m above the wall's foot: kept as a regression case.
            cases.Add(new InvCase("R2-e top headwall x40", fr => R2e(40).Height(fr), 600, 60));
            // Runs that operate at m = 2 (area > M2Area = 200,704 m²).
            cases.Add(new InvCase("xl plane20", plane20, 2600, 80));
            cases.Add(new InvCase("xl runout", longRunout, 3000, 100));
            cases.Add(new InvCase("xl ridge", ridge, 2600, 80));
            cases.Add(new InvCase("xl crest", crest, 3000, 100));
            cases.Add(new InvCase("xl bank12@50 w60", bank, 3400, 60));
            cases.Add(new InvCase("xl bank12@50 w80", bank, 2600, 80));
            cases.Add(new InvCase("xl bank12@50 w100", bank, 3000, 100));
            double2[] starts = { new double2(0.3, -0.7), new double2(7000.3, -6999.6), new double2(-3123.4, 2345.6) };
            foreach (InvCase ic in cases)
            {
                double len = ic.Len, width = ic.Width;
                int op = OperatingStep(len, width);
                Check(op == (ic.Name.StartsWith("xl ", StringComparison.Ordinal) ? 2 : 1), "invariance/" + ic.Name,
                    "fixture " + F(len) + "x" + F(width) + " operates at m=" + op);
                for (int m = 1; m <= 2; m++)
                {
                    bool asserted = m == op;
                    string envKey = "inv " + ic.Name + " m" + m;
                    bool gap = s_GapEnvelope.ContainsKey(envKey);
                    double bound = asserted && gap ? s_GapEnvelope[envKey] : 0.01;
                    double refRating = double.NaN, refSteep = double.NaN, worst = 0, worstSteep = 0, rLo = 1e9, rHi = -1e9;
                    string worstAt = "";
                    foreach (double2 st0 in starts)
                    {
                        foreach (double ang in Angles7)
                        {
                            Frame fr = Frame.Make(st0.x, st0.y, ang);
                            double2[] ring = Rect(len, width, fr);
                            Terrain t = Terrain.For(ic.Fx(fr), ring, 60);
                            string w = "invariance/" + ic.Name + " m" + m + " a" + ang + " at " + F(st0.x);
                            SlopeStatus st = RateWith(t, Pts(ring, false), m, s_Grid, s_Result, Zp.Shipped, w);
                            if (!Check(st == SlopeStatus.Ok, w, "status " + st))
                                continue;
                            rLo = Math.Min(rLo, s_Result.RatingGrade);
                            rHi = Math.Max(rHi, s_Result.RatingGrade);
                            if (double.IsNaN(refRating))
                            {
                                refRating = s_Result.RatingGrade;
                                refSteep = s_Result.SteepestGrade;
                                continue;
                            }
                            double d = s_Result.RatingGrade - refRating;
                            if (Math.Abs(d) > Math.Abs(worst))
                            {
                                worst = d;
                                worstAt = "a" + ang + " at " + F(st0.x);
                            }
                            double dsr = s_Result.SteepestGrade - refSteep;
                            if (Math.Abs(dsr) > Math.Abs(worstSteep))
                                worstSteep = dsr;
                            if (asserted)
                                Check(Math.Abs(d) <= bound, w, "rating " + P(s_Result.RatingGrade) + " vs reference " + P(refRating) +
                                      (gap ? " (regression envelope " + P(bound) + ")" : ""));
                        }
                    }
                    Console.WriteLine("INFO  invariance " + ic.Name + " " + F(len) + "x" + F(width) + " m=" + m + " (" + Mode(asserted, m) +
                                      "): rating reference " + P(refRating) + ", range [" + P(rLo) + ", " + P(rHi) + "], worst shift " + P(worst) +
                                      " (" + worstAt + "); steepest reference " + P(refSteep) + ", worst shift " + P(worstSteep));
                    if (asserted && Math.Abs(worst) > 0.01)
                    {
                        if (m == 1)
                            SpecGap("invariance " + ic.Name + " " + F(len) + "x" + F(width) + " m=" + m,
                                    "§9.4 (2) rotation/translation changes the rating by <= 1 % (m = 1)", "worst shift " + P(worst) + " (" + worstAt + ")" +
                                    (gap ? ", regression envelope " + P(bound) : ""));
                        else
                            Console.WriteLine("INFO  invariance " + ic.Name + " m=2: worst shift " + P(worst) +
                                              " > 1 % — outside §9.4 (2)'s m = 1 scope; regression envelope " + P(bound));
                    }
                }
            }
        }

        /// <summary>A spec invariant the classifier does not meet at a run's operating step, per
        /// the house rule "report the measured worst case instead of weakening silently". WARN
        /// lines do not count toward ExitCode; the integrator carries them into the docs.</summary>
        private static void SpecGap(string where, string invariant, string measured)
        {
            s_SpecGaps++;
            Console.WriteLine("WARN  SPEC-GAP " + where + ": spec " + invariant + " not met — measured " + measured);
        }

        private static List<KeyValuePair<string, LocalFixture>> LocalFixtures()
        {
            return new List<KeyValuePair<string, LocalFixture>>
            {
                new KeyValuePair<string, LocalFixture>("plane20", fr => (x, z) => 1000.0 - 0.20 * fr.Along(x, z)),
                new KeyValuePair<string, LocalFixture>("bank12@50", fr => (x, z) => BankProfile(fr.Along(x, z), 12, 0.50)),
                new KeyValuePair<string, LocalFixture>("ridge", fr => (x, z) => 1000.0 - 0.12 * fr.Along(x, z) - 0.25 * Math.Abs(fr.Across(x, z))),
                new KeyValuePair<string, LocalFixture>("crest", fr => (x, z) => CrestProfile(fr.Along(x, z))),
                new KeyValuePair<string, LocalFixture>("runout", fr => (x, z) => RunoutProfile(fr.Along(x, z))),
            };
        }

        // ================================================================== R2 (3) height scaling

        private static void ScaleTests()
        {
            List<KeyValuePair<string, LocalFixture>> fx = LocalFixtures();
            fx.Add(new KeyValuePair<string, LocalFixture>("bowl", fr => (x, z) =>
            {
                double s = fr.Along(x, z) - 250.0, t = fr.Across(x, z);
                return 1000.0 + 0.0012 * (s * s + t * t);
            }));
            fx.Add(new KeyValuePair<string, LocalFixture>("noisy20", fr => (x, z) =>
                1000.0 - 0.20 * fr.Along(x, z) + 0.3 * Math.Sin(2 * Math.PI * x / 5) * Math.Sin(2 * Math.PI * z / 5)));
            fx.Add(new KeyValuePair<string, LocalFixture>("cornerbank", fr =>
            {
                Scn sc = R2a();
                return sc.Height(fr);
            }));
            double[] lambdas = { 1.1, 1.5, 2.0, 3.0 };
            foreach (KeyValuePair<string, LocalFixture> kv in fx)
            {
                double len = kv.Key == "runout" ? 600 : kv.Key == "bowl" ? 250 : kv.Key == "cornerbank" ? 400 : 300;
                double width = kv.Key == "bowl" ? 80 : kv.Key == "cornerbank" ? 100 : 60;
                foreach (double ang in new double[] { 0, 37 })
                {
                    Frame fr = Frame.Make(400.2, -300.9, ang);
                    double2[] ring = Rect(len, width, fr);
                    HeightFn f = kv.Value(fr);
                    for (int m = 1; m <= 2; m++)
                    {
                        Terrain t1 = Terrain.For(f, ring, 60);
                        SlopeStatus s1 = Rate(t1, ring, m);
                        float r1 = s_Result.RatingGrade;
                        foreach (double lam in lambdas)
                        {
                            string w = "scale/" + kv.Key + " a" + ang + " m" + m + " x" + F(lam);
                            Terrain tl = Terrain.For((x, z) => 1000.0 + lam * (f(x, z) - 1000.0), ring, 60);
                            SlopeStatus sl = Rate(tl, ring, m);
                            Check(sl == s1, w, "status " + sl + " vs " + s1);
                            Check(s_Result.RatingGrade >= r1, w, "rating fell: " + P(r1) + " -> " + P(s_Result.RatingGrade));
                            Check(Math.Abs(s_Result.RatingGrade - lam * r1) <= (lam + 1) * 0.004, w,
                                "rating " + P(s_Result.RatingGrade) + " not ~ λ·" + P(r1));
                        }
                    }
                }
            }
        }

        // ================================================================== R2 (4) runout

        private static Scn RunoutBelow(double runout)
        {
            return new Scn("(4) pitch30 + runout " + F(runout) + " below", F(100 + runout) + "x60", LRect(100 + runout, 60),
                (s, t) => s < 100.0 ? 1000.0 - 0.30 * s : 970.0 - 0.03 * (s - 100.0));
        }

        private static Scn RunoutAbove(double runout)
        {
            return new Scn("(4) runout " + F(runout) + " above pitch30", F(100 + runout) + "x60", LRect(100 + runout, 60),
                (s, t) => s < runout ? 1000.0 - 0.03 * s : 1000.0 - 0.03 * runout - 0.30 * (s - runout));
        }

        private static void RunoutTests()
        {
            // §9.4 (4): a 100 m 30 % pitch, 60 m wide, followed by a 3 % runout of 450 m AND of
            // 2,000 m -> 30 ± 1.5 % (Blue); the same with the runout ABOVE the pitch is reported.
            Scn pitchOnly = new Scn("pitch30 alone", "100x60", LRect(100, 60), (s, t) => 1000.0 - 0.30 * s);
            foreach (double runout in new double[] { 450, 2000 })
            {
                foreach (bool above in new[] { false, true })
                {
                    Scn sc = above ? RunoutAbove(runout) : RunoutBelow(runout);
                    int op = SkiSlopeClassifier.StepMultiple(sc.Area(), Terrain.Texel, 0);
                    double lo = 1, hi = -1, worstVsPitch = 0;
                    SlopeBand bLo = SlopeBand.DoubleBlack, bHi = SlopeBand.Flat;
                    foreach (double ang in Angles7)
                    {
                        Frame fr = Frame.Make(-1500.4, 2500.6, ang);
                        string w = sc.Name + " a" + ang + " m" + op;
                        SlopeStatus sq = RunScn(sc, fr, op, s_Grid, s_Result, w);
                        float rq = s_Result.RatingGrade, mq = s_Result.MeanGrade;
                        SlopeBand bq = s_Result.Band;
                        RunScn(pitchOnly, above ? Frame.Make(fr.World(runout, 0).x, fr.World(runout, 0).y, ang) : fr, op, s_Grid, s_Result, w + " pitch");
                        float rp = s_Result.RatingGrade, mp = s_Result.MeanGrade;
                        worstVsPitch = Math.Abs(rq - rp) > Math.Abs(worstVsPitch) ? rq - rp : worstVsPitch;
                        lo = Math.Min(lo, rq);
                        hi = Math.Max(hi, rq);
                        if (bq < bLo) bLo = bq;
                        if (bq > bHi) bHi = bq;
                        Check(sq == SlopeStatus.Ok, w, "status " + sq);
                        Check(mq < mp, w, "runout did not lower the mean (" + P(mp) + " -> " + P(mq) + ")");
                        if (!above)
                        {
                            Check(Math.Abs(rq - 0.30) <= 0.015, w, "pitch + runout rates " + P(rq) + ", want 30 ± 1.5 %");
                            Check(bq == SlopeBand.Blue, w, "band " + bq + ", want Blue");
                        }
                    }
                    Console.WriteLine("INFO  " + sc.Name + " (" + sc.Size + ", m=" + op + ", " + (above ? "report only" : "asserted 30 ± 1.5 % Blue") +
                                      "): rating in [" + P(lo) + ", " + P(hi) + "], bands " + bLo + ".." + bHi + "; rating - pitch-alone worst " + P(worstVsPitch));
                }
            }
        }

        // ================================================================== old (5): report only

        /// <summary>Old invariant (5) "nested P ⊂ Q: rating(Q) >= rating(P)" is DROPPED in R2
        /// (§9.3 limit (b): widening a run can add an easier line). Reported so the integrator
        /// sees how often and by how much widening now lowers the rating.</summary>
        private static void NestedReport()
        {
            HeightFn mtn = Mountain();
            List<KeyValuePair<double2[], double2[]>> pairs = new List<KeyValuePair<double2[], double2[]>>();
            foreach (double ang in new double[] { 0, 30, 77, 151 })
            {
                Frame fr = Frame.Make(3400.3, -3500.6, ang);
                pairs.Add(new KeyValuePair<double2[], double2[]>(Rect(300, 60, fr), Rect(360, 110, Frame.Make(fr.World(-20, 0).x, fr.World(-20, 0).y, ang))));
                double2[] p = Rect(250, 70, fr);
                double2[] q = { p[0], p[1], fr.World(330, 0), p[2], p[3] };
                pairs.Add(new KeyValuePair<double2[], double2[]>(p, q));
            }
            Unity.Mathematics.Random rng = new Unity.Mathematics.Random(0x51A9E5u);
            for (int k = 0; k < 6; k++)
            {
                int n = 7 + k;
                double2 c = new double2(3600 + 37.1 * k, -3300 - 23.3 * k);
                double2[] p = new double2[n];
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * (i + 0.3 * rng.NextDouble()) / n;
                    double rad = 90 + 40 * rng.NextDouble();
                    p[i] = c + new double2(Math.Cos(a) * rad * 1.6, Math.Sin(a) * rad);
                }
                double2[] q = new double2[n];
                for (int i = 0; i < n; i++)
                    q[i] = c + (p[i] - c) * 1.3;
                pairs.Add(new KeyValuePair<double2[], double2[]>(p, q));
            }
            int lower = 0, equal = 0, higher = 0, steepLower = 0;
            double worst = 0;
            foreach (KeyValuePair<double2[], double2[]> pq in pairs)
            {
                Terrain t = Terrain.For(mtn, pq.Value, 60);
                for (int m = 1; m <= 2; m++)
                {
                    Rate(t, pq.Key, m);
                    float rp = s_Result.RatingGrade, sp = s_Result.SteepestGrade;
                    Rate(t, pq.Value, m);
                    float rq = s_Result.RatingGrade, sq = s_Result.SteepestGrade;
                    if (rq < rp) lower++;
                    else if (rq == rp) equal++;
                    else higher++;
                    if (sq < sp) steepLower++;
                    worst = Math.Min(worst, rq - rp);
                }
            }
            Console.WriteLine("INFO  nested (old (5), dropped in R2 — report only): " + pairs.Count * 2 + " pairs P ⊂ Q, rating(Q) < rating(P) in " + lower +
                              " (worst " + P(worst) + "), equal " + equal + ", higher " + higher + "; SteepestGrade(Q) < SteepestGrade(P) in " + steepLower);
            Check(steepLower == 0, "nested/steepest", "SteepestGrade (the R1 statistic) must stay superset-monotone: fell in " + steepLower + " pairs");
        }

        // ================================================================== R2-g noise

        /// <summary>R2-g: ±0.3 m at 5 m wavelength on mid-band planes: same band, |Δ| <= 3 %.
        /// Mid-band under §9.2: 15.5 % (Green 6-25), 32.5 % (Blue 25-40), 50 % (Black 40-60).
        /// The rigorous ceiling for one window is (0.6 m + 2 quanta) / 20 m ≈ 3.63 % (reported).
        /// Pattern 3 (report only) is the old 3.05 m oblique wave, harsher than the spec.</summary>
        private const double NoiseBound = 0.03;
        private const double NoiseCeiling = (0.6 + 2.0 * Terrain.Quantum) / 20.0;
        private static readonly double[] MidBand = { 0.155, 0.325, 0.50 };

        private static void NoiseTests()
        {
            double[] noiseLens = { 300, 1000, 2600 };
            int[] noiseOps = { 1, 1, 2 };
            for (int li = 0; li < noiseLens.Length; li++)
            {
                double len = noiseLens[li];
                double width = len > 500 ? 80 : 60;
                int op = OperatingStep(len, width);
                Check(op == noiseOps[li], "noise " + len + "x" + width, "fixture operates at m=" + op + ", want " + noiseOps[li]);
                for (int m = 1; m <= 2; m++)
                {
                    bool asserted = m == op;
                    double worst = 0, worstHarsh = 0, worstSteep = 0;
                    string worstAt = "", worstHarshAt = "";
                    foreach (double g in MidBand)
                    {
                        foreach (double ang in Angles7)
                        {
                            Frame fr = Frame.Make(5000.3, -3000.7, ang);
                            double2[] ring = Rect(len, width, fr);
                            HeightFn clean = (x, z) => 1000.0 - g * fr.Along(x, z);
                            HeightFn n1 = (x, z) => clean(x, z) + 0.3 * Math.Sin(2 * Math.PI * x / 5) * Math.Sin(2 * Math.PI * z / 5);
                            HeightFn n2 = (x, z) => clean(x, z) + 0.3 * Math.Sin(2 * Math.PI * (x + 1.3 * z) / (5 * Math.Sqrt(2.69)) + 0.7);
                            HeightFn n3 = (x, z) => clean(x, z) + 0.3 * Math.Sin(2 * Math.PI * (x + 1.3 * z) / 5 + 0.7);
                            Rate(Terrain.For(clean, ring, 60), ring, m);
                            float r0 = s_Result.RatingGrade, s0 = s_Result.SteepestGrade;
                            SlopeBand b0 = s_Result.Band;
                            int pat = 0;
                            foreach (HeightFn nf in new[] { n1, n2, n3 })
                            {
                                pat++;
                                bool harsh = pat == 3;
                                string w = "noise " + len + "x" + width + " g" + P(g) + " a" + ang + " m" + m + " pattern" + pat;
                                SlopeStatus st = Rate(Terrain.For(nf, ring, 60), ring, m);
                                double d = s_Result.RatingGrade - r0;
                                if (harsh)
                                {
                                    if (Math.Abs(d) > Math.Abs(worstHarsh))
                                    {
                                        worstHarsh = d;
                                        worstHarshAt = w;
                                    }
                                    continue;
                                }
                                if (Math.Abs(d) > Math.Abs(worst))
                                {
                                    worst = d;
                                    worstAt = w;
                                }
                                double dsn = s_Result.SteepestGrade - s0;
                                if (Math.Abs(dsn) > Math.Abs(worstSteep))
                                    worstSteep = dsn;
                                Check(st == SlopeStatus.Ok, w, "status " + st);
                                if (asserted)
                                {
                                    Check(s_Result.Band == b0, w, "band " + b0 + " -> " + s_Result.Band);
                                    Check(Math.Abs(d) <= NoiseBound, w, "rating " + P(r0) + " -> " + P(s_Result.RatingGrade) + " (spec bound " + P(NoiseBound) + ")");
                                }
                            }
                        }
                    }
                    Console.WriteLine("INFO  noise ±0.3 m @ 5 m " + len + "x" + width + " m=" + m + " (" + Mode(asserted) + "): worst rating shift " +
                                      P(worst) + " (" + worstAt + "); steepest worst " + P(worstSteep) + "; spec bound " + P(NoiseBound) +
                                      ", one-window ceiling (0.6 m + 2 quanta) / 20 m = " + P(NoiseCeiling));
                    Console.WriteLine("INFO  noise ±0.3 m @ 3.05 m oblique (pattern 3, report only: harsher than the spec's 5 m) " + len + "x" + width +
                                      " m=" + m + ": worst rating shift " + P(worstHarsh) + " (" + worstHarshAt + ")");
                }
            }
        }

        // ================================================================== R2-f single-texel spike

        /// <summary>R2-f: a ±3 m single texel anywhere on mid-band planes: same band, |Δ| <= 1 %,
        /// at m = 1 and m = 2, on RatingGrade. SteepestGrade (the R1 statistic whose k = 7 was
        /// settled by this sweep) is asserted the same way, so MinRatingCells keeps its
        /// evidence.</summary>
        private static void SpikeTests()
        {
            double2[] offsets = { new double2(5000.3, -3000.7), new double2(123.45, 987.6) };
            double2[] spots = { new double2(150, 0), new double2(150, 17), new double2(75, -20), new double2(181.3, -8.3), new double2(220.6, 5.1) };
            for (int m = 1; m <= 2; m++)
            {
                int n = 0, lt01 = 0, lt05 = 0, lt1 = 0, ge1 = 0, flips = 0, sGe1 = 0, sFlips = 0;
                double worst = 0, worstSteep = 0;
                string worstAt = "", worstSteepAt = "";
                foreach (double g in MidBand)
                {
                    for (int a = 0; a < 360; a += 2)
                    {
                        foreach (double2 off in offsets)
                        {
                            Frame fr = Frame.Make(off.x, off.y, a);
                            double2[] ring = Rect(300, 60, fr);
                            Terrain t = Terrain.For((x, z) => 1000.0 - g * fr.Along(x, z), ring, 60);
                            Rate(t, ring, m);
                            float r0 = s_Result.RatingGrade, s0 = s_Result.SteepestGrade;
                            SlopeBand b0 = s_Result.Band, sb0 = SkiSlopeClassifier.BandOf(s0);
                            foreach (double2 spot in spots)
                            {
                                double2 pw = fr.World(spot.x, spot.y);
                                int k = Terrain.NearestTexel(pw.x, Terrain.Origin.x, m);
                                int l = Terrain.NearestTexel(pw.y, Terrain.Origin.y, m);
                                foreach (float amp in new[] { 3f, -3f })
                                {
                                    t.Add(k, l, amp);
                                    SlopeStatus st = Rate(t, ring, m);
                                    t.Add(k, l, -amp);
                                    string w = "spike g" + P(g) + " a" + a + " off" + F(off.x) + " at(" + F(spot.x) + "," + F(spot.y) + ") " + (amp > 0 ? "+3" : "-3") + " m" + m;
                                    n++;
                                    double d = s_Result.RatingGrade - r0;
                                    double ad = Math.Abs(d);
                                    if (ad < 0.001) lt01++;
                                    else if (ad < 0.005) lt05++;
                                    else if (ad < 0.01) lt1++;
                                    else ge1++;
                                    if (s_Result.Band != b0) flips++;
                                    if (ad > Math.Abs(worst))
                                    {
                                        worst = d;
                                        worstAt = w + " " + P(r0) + " -> " + P(s_Result.RatingGrade);
                                    }
                                    double ds = s_Result.SteepestGrade - s0;
                                    if (Math.Abs(ds) >= 0.01) sGe1++;
                                    if (SkiSlopeClassifier.BandOf(s_Result.SteepestGrade) != sb0) sFlips++;
                                    if (Math.Abs(ds) > Math.Abs(worstSteep))
                                    {
                                        worstSteep = ds;
                                        worstSteepAt = w + " " + P(s0) + " -> " + P(s_Result.SteepestGrade);
                                    }
                                    Check(st == SlopeStatus.Ok, w, "status " + st);
                                    Check(s_Result.Band == b0 && ad <= 0.01, w, "rating " + P(r0) + " -> " + P(s_Result.RatingGrade) + " (" + b0 + " -> " + s_Result.Band + ")");
                                    Check(SkiSlopeClassifier.BandOf(s_Result.SteepestGrade) == sb0 && Math.Abs(ds) <= 0.01, w,
                                        "steepest " + P(s0) + " -> " + P(s_Result.SteepestGrade));
                                }
                            }
                        }
                    }
                }
                Console.WriteLine("SPIKE m=" + m + " k=" + RingK(Step(m)) + " cases=" + n + " rating: worst=" + P(worst) + " |d|<0.1%:" + lt01 + " 0.1-0.5%:" + lt05 +
                                  " 0.5-1%:" + lt1 + " >=1%:" + ge1 + " bandFlips=" + flips + " worstCase: " + worstAt);
                Console.WriteLine("SPIKE m=" + m + " steepest (R1 statistic): worst=" + P(worstSteep) + " >=1%:" + sGe1 + " bandFlips=" + sFlips + " worstCase: " + worstSteepAt);
            }
        }

        private static int RingK(float step)
        {
            return Math.Max(SkiSlopeClassifier.MinRatingCells, (int)Math.Ceiling(SkiSlopeClassifier.RatingAreaM2 / ((double)step * step)));
        }

        // ================================================================== old (8) banks, on SteepestGrade

        private static void BankTests()
        {
            double[][] banks = { new[] { 12.0, 0.50, 0.34 }, new[] { 5.0, 0.60, 0.225 } };
            foreach (double[] bk in banks)
            {
                double len = bk[0], gb = bk[1], analytic = bk[2];
                double oracle = OracleMaxWindow(s => BankProfile(s, len, gb), new[] { 120.0, 120.0 + len }, 0, 300, 20);
                Check(Math.Abs(oracle - analytic) < 1e-9, "bank oracle", "closed form " + P(oracle) + " vs analytic " + P(analytic));
                double[][] sizes =
                {
                    new[] { 20.0, 300, 1 }, new[] { 30.0, 300, 1 }, new[] { 60.0, 300, 1 }, new[] { 20.0, 2600, 1 }, new[] { 30.0, 1700, 1 },
                    new[] { 60.0, 900, 1 }, new[] { 60.0, 3400, 2 }, new[] { 80.0, 2600, 2 }, new[] { 100.0, 3000, 2 },
                };
                foreach (double[] sz in sizes)
                {
                    double width = sz[0], runLen = sz[1];
                    int op = SkiSlopeClassifier.StepMultiple(runLen * width, Terrain.Texel, 0);
                    Check(op == (int)sz[2], "bank " + F(runLen) + "x" + width, "fixture operates at m=" + op + ", want " + F(sz[2]));
                    string gapKey = "bank " + F(len) + " w" + width;
                    bool gap = op == 2 && s_GapEnvelope.ContainsKey(gapKey);
                    double runOracle = OracleMaxWindow(s => BankProfile(s, len, gb), new[] { 120.0, 120.0 + len }, 0, runLen, 20);
                    for (int m = 1; m <= 2; m++)
                    {
                        bool asserted = m == op;
                        double lo = 1, hi = -1, cLo = 1, cHi = -1, fLo = 1, fHi = -1;
                        int fDisc = 0;
                        foreach (double ang in Angles7)
                        {
                            Frame fr = Frame.Make(-800.6, 600.2, ang);
                            double2[] ring = Rect(runLen, width, fr);
                            Terrain t = Terrain.For((x, z) => BankProfile(fr.Along(x, z), len, gb), ring, 60);
                            string w = "bank " + F(len) + "m@" + P(gb) + " " + F(runLen) + "x" + width + " a" + ang + " m" + m;
                            SlopeStatus st = Rate(t, ring, m);
                            double d = s_Result.SteepestGrade - runOracle;
                            lo = Math.Min(lo, d);
                            hi = Math.Max(hi, d);
                            cLo = Math.Min(cLo, s_Result.RatingGrade - runOracle);
                            cHi = Math.Max(cHi, s_Result.RatingGrade - runOracle);
                            if (len == 12.0)
                            {
                                // Evidence for the 4-vs-8 connectivity question (report only).
                                float f4 = OracleMinimax(s_Grid, s_Result, Conn4);
                                if (float.IsNaN(f4))
                                {
                                    fDisc++;
                                }
                                else
                                {
                                    fLo = Math.Min(fLo, f4 - runOracle);
                                    fHi = Math.Max(fHi, f4 - runOracle);
                                }
                            }
                            if (!asserted)
                                continue; // report-only off the operating step (see INFO)
                            Check(st == SlopeStatus.Ok, w, "status " + st);
                            if (gap)
                                Check(d >= -s_GapEnvelope[gapKey] && d <= 0.01, w, "steepest " + P(s_Result.SteepestGrade) + " vs oracle " + P(runOracle) +
                                      " (regression envelope [-" + P(s_GapEnvelope[gapKey]) + ", +1.00%])");
                            else
                                Check(Math.Abs(d) <= 0.01, w, "steepest " + P(s_Result.SteepestGrade) + " vs oracle " + P(runOracle));
                            // The crux (RatingGrade) of a FULL-WIDTH bank is unavoidable, so it must read the
                            // bank (R2 fixer round, review finding tests-1: a wrong per-cell value
                            // that happens to be rotation-stable passed every value assertion before).
                            // m = 1: ±1 %, like SteepestGrade. m = 2: no spec bound exists for the crux
                            // (§9.4); the measured diagonal leak (classifier header, MEASURED LIMITS) is
                            // kept from widening by a regression envelope — INFO, not a SPEC-GAP.
                            double dc = s_Result.RatingGrade - runOracle;
                            string crKey = "bank crux " + F(len) + " w" + width;
                            if (m == 1)
                                Check(Math.Abs(dc) <= 0.01, w, "crux rating " + P(s_Result.RatingGrade) + " vs oracle " + P(runOracle) + " (±1 %)");
                            else
                                Check(dc >= -(s_CruxEnvelope.ContainsKey(crKey) ? s_CruxEnvelope[crKey] : 0.01) && dc <= 0.01, w,
                                    "crux rating " + P(s_Result.RatingGrade) + " vs oracle " + P(runOracle) + " (regression envelope [-" +
                                    P(s_CruxEnvelope.ContainsKey(crKey) ? s_CruxEnvelope[crKey] : 0.01) + ", +1.00%])");
                        }
                        Console.WriteLine("INFO  bank " + F(len) + " m @ " + P(gb) + " in 10% " + F(runLen) + "x" + width + " m=" + m +
                                          " (" + Mode(asserted, m) + "): steepest - oracle(" + P(runOracle) + ") in [" + P(lo) + ", " + P(hi) +
                                          "]; crux rating - oracle in [" + P(cLo) + ", " + P(cHi) + "] (full-width bank: unavoidable; " +
                                          (asserted ? (m == 1 ? "asserted ±1 %" : "asserted regression envelope -" +
                                                       P(s_CruxEnvelope.ContainsKey("bank crux " + F(len) + " w" + width) ? s_CruxEnvelope["bank crux " + F(len) + " w" + width] : 0.01) +
                                                       " (no spec bound at m = 2)") : "report only") + ")" +
                                          (len == 12.0 ? "; 4-connected exact minimax - oracle in [" + P(fLo) + ", " + P(fHi) + "]" +
                                                         (fDisc > 0 ? ", " + fDisc + " disconnected" : "") + " (report only)" : ""));
                        if (asserted && (lo < -0.01 || hi > 0.01))
                            SpecGap("bank " + F(len) + " m @ " + P(gb) + " " + F(runLen) + "x" + width + " m=" + m,
                                    "§3 (8) bank SteepestGrade within ±1 % of the oracle", "steepest - oracle in [" + P(lo) + ", " + P(hi) + "]" +
                                    (gap ? ", regression envelope -" + P(s_GapEnvelope[gapKey]) : ""));
                    }
                }
            }
        }

        // ================================================================== old (9) ridge + crest, on SteepestGrade

        private static void RidgeCrestTests()
        {
            double ridge = Math.Sqrt(0.12 * 0.12 + 0.25 * 0.25);
            double crest = OracleMaxWindow(CrestProfile, new[] { 100.0 }, 0, 300, 20);
            Check(Math.Abs(crest - 0.20) < 1e-9, "crest oracle", P(crest));
            double[][] sizes = { new[] { 300.0, 60, 1 }, new[] { 2000.0, 80, 1 }, new[] { 2600.0, 80, 2 }, new[] { 3000.0, 100, 2 } };
            foreach (double[] sz in sizes)
            {
                double runLen = sz[0], width = sz[1];
                int op = OperatingStep(runLen, width);
                Check(op == (int)sz[2], "ridge/crest " + F(runLen) + "x" + F(width), "fixture operates at m=" + op + ", want " + F(sz[2]));
                for (int m = 1; m <= 2; m++)
                {
                    double rLo = 1, rHi = -1, cLo = 1, cHi = -1, xrLo = 1, xrHi = -1, xcLo = 1, xcHi = -1, f4Lo = 1, f4Hi = -1;
                    foreach (double ang in Angles7)
                    {
                        Frame fr = Frame.Make(2222.2, 1111.1, ang);
                        double2[] ring = Rect(runLen, width, Frame.Make(fr.World(0, 0).x, fr.World(0, 0).y, ang));
                        Terrain tr = Terrain.For((x, z) => 1000.0 - 0.12 * fr.Along(x, z) - 0.25 * Math.Abs(fr.Across(x, z)), ring, 60);
                        string w = "ridge " + F(runLen) + "x" + F(width) + " a" + ang + " m" + m;
                        SlopeStatus st = Rate(tr, ring, m);
                        Check(st == SlopeStatus.Ok, w, "status " + st);
                        Check(Math.Abs(s_Result.SteepestGrade - ridge) <= 0.01, w, "steepest " + P(s_Result.SteepestGrade) + " vs " + P(ridge));
                        rLo = Math.Min(rLo, s_Result.SteepestGrade - ridge);
                        rHi = Math.Max(rHi, s_Result.SteepestGrade - ridge);
                        xrLo = Math.Min(xrLo, s_Result.RatingGrade);
                        xrHi = Math.Max(xrHi, s_Result.RatingGrade);
                        if (runLen == 300.0)
                        {
                            float f4 = OracleMinimax(s_Grid, s_Result, Conn4);   // 4-vs-8 evidence (report only)
                            f4Lo = Math.Min(f4Lo, f4);
                            f4Hi = Math.Max(f4Hi, f4);
                        }
                        Terrain tc = Terrain.For((x, z) => CrestProfile(fr.Along(x, z)), ring, 60);
                        w = "crest " + F(runLen) + "x" + F(width) + " a" + ang + " m" + m;
                        st = Rate(tc, ring, m);
                        Check(st == SlopeStatus.Ok, w, "status " + st);
                        Check(Math.Abs(s_Result.SteepestGrade - crest) <= 0.005, w, "steepest " + P(s_Result.SteepestGrade) + " vs " + P(crest));
                        cLo = Math.Min(cLo, s_Result.SteepestGrade - crest);
                        cHi = Math.Max(cHi, s_Result.SteepestGrade - crest);
                        xcLo = Math.Min(xcLo, s_Result.RatingGrade);
                        xcHi = Math.Max(xcHi, s_Result.RatingGrade);
                    }
                    Console.WriteLine("INFO  ridge/crest " + F(runLen) + "x" + F(width) + " m=" + m + " (steepest asserted; " +
                                      (m == op ? "operating step" : "not the operating step") + "): ridge steepest - " + P(ridge) + " in [" + P(rLo) + ", " +
                                      P(rHi) + "], crest steepest - " + P(crest) + " in [" + P(cLo) + ", " + P(cHi) + "]; crux rating: ridge [" +
                                      P(xrLo) + ", " + P(xrHi) + "] (down the crest), crest [" + P(xcLo) + ", " + P(xcHi) + "]" +
                                      (runLen == 300.0 ? "; ridge 4-connected exact minimax [" + P(f4Lo) + ", " + P(f4Hi) + "] (report only)" : ""));
                }
            }
        }

        // ================================================================== (10) small runs

        private static void SmallRunTests()
        {
            double2[] offsets = { new double2(5000.3, -3000.7), new double2(-12.34, 56.78) };
            foreach (double ang in Angles7)
            {
                foreach (double2 off in offsets)
                {
                    Frame fr = Frame.Make(off.x, off.y, ang);
                    SmallRun("12x12@20", fr, 12, 12, 0.20, SlopeStatus.ShortRun);
                    SmallRun("18x18@10", fr, 18, 18, 0.10, SlopeStatus.ShortRun);
                    SmallRun("30x15@10", fr, 30, 15, 0.10, SlopeStatus.Ok);
                    SmallRun("18x18@32.5", fr, 18, 18, 0.325, SlopeStatus.ShortRun);
                }
            }
        }

        private static void SmallRun(string name, Frame fr, double len, double width, double g, SlopeStatus want)
        {
            double2[] ring = Rect(len, width, fr);
            Terrain t = Terrain.For((x, z) => 1000.0 - g * fr.Along(x, z), ring, 60);
            string w = "small/" + name + " a" + fr.Deg + " at " + F(fr.Cx);
            SlopeStatus st = RateWith(t, Pts(ring, false), 1, s_Grid, s_Result, Zp.Shipped, w);
            Check(st == want, w, "status " + st + ", want " + want + " (valid windows " + s_Result.ValidWindows + ")");
            Check(s_Result.Band == SkiSlopeClassifier.BandOf((float)g), w, "band " + s_Result.Band + " (rating " + P(s_Result.RatingGrade) + ")");
        }

        // ================================================================== (11) polyline

        private static void PolylineTests()
        {
            double fa = 17.0 * Math.PI / 180.0;
            foreach (double g in new[] { 0.105, 0.20 })
            {
                foreach (double phiDeg in new double[] { 0, 30, 60 })
                {
                    for (int m = 1; m <= 2; m++)
                    {
                        double2 c = new double2(5000.3, -3000.7);
                        HeightFn f = (x, z) => 1000.0 - g * ((x - c.x) * Math.Cos(fa) + (z - c.y) * Math.Sin(fa));
                        double dir = fa + phiDeg * Math.PI / 180.0;
                        double2 p1 = c + 120.0 * new double2(Math.Cos(dir), Math.Sin(dir));
                        double2 p2 = p1 + 80.0 * new double2(Math.Cos(dir + 0.6), Math.Sin(dir + 0.6));
                        double2[] line = { c, p1, p2 };
                        Terrain t = Terrain.For(f, line, 60);
                        double[] want = { g * Math.Abs(Math.Cos(phiDeg * Math.PI / 180.0)), g * Math.Abs(Math.Cos(phiDeg * Math.PI / 180.0 + 0.6)) };
                        for (int sgi = 0; sgi < 2; sgi++)
                        {
                            double2[] seg = { line[sgi], line[sgi + 1] };
                            string w = "polyline g" + P(g) + " phi" + phiDeg + " seg" + sgi + " m" + m;
                            SlopeStatus st = RateWith(t, Pts(seg, false), m, s_Grid, s_Result, Zp.Shipped, w);
                            Check(s_Grid.PolylineMode, w, "not polyline mode");
                            Check(st == SlopeStatus.Polyline, w, "status " + st);
                            Check(s_Result.SegmentCount == 1, w, "SegmentCount " + s_Result.SegmentCount);
                            Check(Math.Abs(s_Result.SegmentGrade[0] - want[sgi]) <= 0.003, w, "segment grade " + P(s_Result.SegmentGrade[0]) + " vs " + P(want[sgi]));
                            Check(s_Result.SegmentBand[0] == (byte)SkiSlopeClassifier.BandOf(s_Result.SegmentGrade[0]), w, "segment band");
                            Check(Math.Abs(s_Result.RatingGrade - want[sgi]) <= 0.005, w, "rating " + P(s_Result.RatingGrade) + " vs " + P(want[sgi]));
                        }
                    }
                }
            }
            PolylineSpikeSweep();
            // A genuine 3-point polyline: a degenerate (area < 1 m²) dog-leg along one line.
            {
                Frame fr = Frame.Make(-400.1, 900.2, 64);
                HeightFn f = (x, z) => 1000.0 - 0.20 * fr.Along(x, z);
                double2[] line = { fr.World(0, 0), fr.World(100, 0.001), fr.World(180, 0) };
                Terrain t = Terrain.For(f, line, 60);
                SlopeStatus st = RateWith(t, Pts(line, false), 1, s_Grid, s_Result, Zp.Shipped, "polyline/3pt");
                Check(st == SlopeStatus.Polyline && s_Result.SegmentCount == 2, "polyline/3pt", "status " + st + " segments " + s_Result.SegmentCount);
                Check(Math.Abs(s_Result.RatingGrade - 0.20) <= 0.005 && Math.Abs(s_Result.MeanGrade - 0.20) <= 0.005, "polyline/3pt",
                    "rating " + P(s_Result.RatingGrade) + " mean " + P(s_Result.MeanGrade));
                double sum = 0;
                for (int b = 0; b < 5; b++) sum += s_Result.BandShare[b];
                Check(Math.Abs(sum - 1.0) < 1e-5, "polyline/3pt", "length shares sum " + F(sum));
                Check(Math.Abs(s_Result.Drop - 0.20 * 180) <= 0.2, "polyline/3pt", "drop " + F(s_Result.Drop));
                bool all255 = true;
                for (int q = 0; q < s_Result.Nx * s_Result.Nz; q++)
                    all255 &= s_Result.CellBand[q] == SkiSlopeClassifier.BandOutside;
                Check(all255, "polyline/3pt", "polyline CellBand must be all BandOutside");
            }
            // Shorter than the window: endpoint grade, ShortRun.
            foreach (double ang in new double[] { 0, 33, 90 })
            {
                Frame fr = Frame.Make(10.1, 20.2, ang);
                double2[] line = { fr.World(0, 0), fr.World(15, 0) };
                Terrain t = Terrain.For((x, z) => 1000.0 - 0.20 * fr.Along(x, z), line, 60);
                SlopeStatus st = RateWith(t, Pts(line, false), 1, s_Grid, s_Result, Zp.Shipped, "polyline/short a" + ang);
                Check(st == SlopeStatus.ShortRun, "polyline/short a" + ang, "status " + st);
                Check(Math.Abs(s_Result.RatingGrade - 0.20) <= 0.005, "polyline/short a" + ang, "rating " + P(s_Result.RatingGrade));
            }
        }

        /// <summary>±3 m single texel on 2-point polylines over a full turn of directions,
        /// including near-contour lines whose own grade is ~0 (there the absolute window makes a
        /// spike at EITHER end read as drop). Polylines always operate at m = 1:
        /// StepMultiple(area 0) = 1 whatever the history.</summary>
        private static void PolylineSpikeSweep()
        {
            double fa = 17.0 * Math.PI / 180.0;
            double2 c = new double2(5000.3, -3000.7);
            for (int m = 1; m <= 2; m++)
            {
                bool asserted = m == SkiSlopeClassifier.StepMultiple(0.0, Terrain.Texel, 2);
                int n = 0, ge1 = 0, flips = 0;
                double worst = 0;
                string worstAt = "";
                foreach (double g in new[] { 0.03, 0.105, 0.20, 0.325 })
                {
                    HeightFn f = (x, z) => 1000.0 - g * ((x - c.x) * Math.Cos(fa) + (z - c.y) * Math.Sin(fa));
                    for (int a = 0; a < 360; a += 5)
                    {
                        double dir = a * Math.PI / 180.0;
                        double2[] seg = { c, c + 120.0 * new double2(Math.Cos(dir), Math.Sin(dir)) };
                        Terrain t = Terrain.For(f, seg, 60);
                        Rate(t, Pts(seg, false), m, s_Grid, s_Result);
                        float r0 = s_Result.RatingGrade;
                        SlopeBand b0 = s_Result.Band;
                        foreach (double tt in new[] { 0.3, 0.5, 0.7 })
                        {
                            double2 p = seg[0] + (seg[1] - seg[0]) * tt;
                            int k = Terrain.NearestTexel(p.x, Terrain.Origin.x, m), l = Terrain.NearestTexel(p.y, Terrain.Origin.y, m);
                            foreach (float amp in new[] { 3f, -3f })
                            {
                                t.Add(k, l, amp);
                                SlopeStatus st = Rate(t, Pts(seg, false), m, s_Grid, s_Result);
                                t.Add(k, l, -amp);
                                n++;
                                double d = s_Result.RatingGrade - r0;
                                string w = "polyline spike g" + P(g) + " a" + a + " t" + F(tt) + (amp > 0 ? " +3" : " -3") + " m" + m;
                                if (Math.Abs(d) >= 0.01) ge1++;
                                if (s_Result.Band != b0) flips++;
                                if (Math.Abs(d) > Math.Abs(worst))
                                {
                                    worst = d;
                                    worstAt = w + " " + P(r0) + " -> " + P(s_Result.RatingGrade);
                                }
                                Check(st == SlopeStatus.Polyline, w, "status " + st);
                                if (asserted)
                                    Check(s_Result.Band == b0 && Math.Abs(d) <= 0.01, w, "rating " + P(r0) + " -> " + P(s_Result.RatingGrade));
                            }
                        }
                    }
                }
                Console.WriteLine("SPIKE-POLY m=" + m + " (" + Mode(asserted) + ") cases=" + n + " worst=" + P(worst) + " >=1%:" + ge1 +
                                  " bandFlips=" + flips + " worstCase: " + worstAt);
            }
        }

        // ================================================================== R2-i replay

        private static void ReplayTests()
        {
            Frame fa = Frame.Make(777.7, -555.5, 37);
            double2[] ringA = Rect(400, 90, fa);
            Terrain ta = Terrain.For(Mountain(), ringA, 60);
            Frame fb = Frame.Make(-900.1, 300.3, 101);
            double2[] ringB = Rect(120, 30, fb);
            Terrain tb = Terrain.For((x, z) => BankProfile(fb.Along(x, z), 12, 0.5), ringB, 60);
            double2[] lineC = { fb.World(0, 0), fb.World(90, 0) };
            Terrain tc = Terrain.For((x, z) => 1000.0 - 0.15 * fb.Along(x, z), lineC, 60);
            // A disconnected dumbbell (Connected = false path) also replays bit-identically.
            double2[] ringD = Dumbbell(-2000.0, 1500.0);
            Terrain td = Terrain.For((x, z) => 1000.0 - 0.15 * x, ringD, 60);

            for (int m = 1; m <= 2; m++)
            {
                SlopeGrid g = new SlopeGrid();
                SlopeResult r = new SlopeResult();
                Rate(ta, Pts(ringA, false), m, g, r);
                Snapshot s1 = Snapshot.Take(g, r);
                Check(r.CruxValid && r.Connected, "replay m" + m, "fixture A should be connected with a crux");
                Rate(ta, Pts(ringA, false), m, g, r);
                string d = s1.Diff(Snapshot.Take(g, r));
                Check(d == null, "replay m" + m, "replay into reused objects differs: " + d);
                // The public overload == the internal one with the shipped constants.
                SkiSlopeClassifier.Plan(Pts(ringA, false), ringA.Length, Terrain.Origin, Step(m), g);
                Fill(g, ta);
                SkiSlopeClassifier.Classify(g, r);
                d = s1.Diff(Snapshot.Take(g, r));
                Check(d == null, "replay m" + m, "public Classify differs from the internal overload with the shipped constants: " + d);
                // CruxForTest is idempotent on a fresh Classify.
                Check(CruxForTest(g, r, Zp.Shipped), "replay m" + m, "CruxForTest refused");
                d = s1.Diff(Snapshot.Take(g, r));
                Check(d == null, "replay m" + m, "CruxForTest changed the result: " + d);
                // Smaller polygon into the same (larger) buffers == fresh objects.
                Rate(tb, Pts(ringB, false), m, g, r);
                Snapshot reusedB = Snapshot.Take(g, r);
                SlopeGrid g2 = new SlopeGrid();
                SlopeResult r2 = new SlopeResult();
                Rate(tb, Pts(ringB, false), m, g2, r2);
                d = reusedB.Diff(Snapshot.Take(g2, r2));
                Check(d == null, "replay m" + m, "second polygon into reused objects differs from fresh: " + d);
                // Polyline into the reused objects, then back to ring.
                Rate(tc, Pts(lineC, false), m, g, r);
                Snapshot reusedC = Snapshot.Take(g, r);
                SlopeGrid g3 = new SlopeGrid();
                SlopeResult r3 = new SlopeResult();
                Rate(tc, Pts(lineC, false), m, g3, r3);
                d = reusedC.Diff(Snapshot.Take(g3, r3));
                Check(d == null, "replay m" + m, "polyline into reused objects differs from fresh: " + d);
                // Disconnected into reused objects == fresh.
                Rate(td, Pts(ringD, false), m, g, r);
                Snapshot reusedD = Snapshot.Take(g, r);
                SlopeGrid g4 = new SlopeGrid();
                SlopeResult r4 = new SlopeResult();
                Rate(td, Pts(ringD, false), m, g4, r4);
                d = reusedD.Diff(Snapshot.Take(g4, r4));
                Check(d == null, "replay m" + m, "disconnected polygon into reused objects differs from fresh: " + d);
                Rate(ta, Pts(ringA, false), m, g, r);
                d = s1.Diff(Snapshot.Take(g, r));
                Check(d == null, "replay m" + m, "ring after polyline/disconnected differs from the first run: " + d);
            }
        }

        // ================================================================== exact k-th + absolute drop (SteepestGrade)

        /// <summary>Axis-aligned runs on z-invariant profiles: the box direction is exactly ±x,
        /// so every full window is |PL(x + half) - PL(x - half)| / 20 with PL the piecewise-linear
        /// interpolation of the node heights along the row. The oracle builds its own mask
        /// (winding-number PIP), its own validity rule (nodes x ± round(half) inside the row) and
        /// sorts the values to take the k-th — exact up to float rounding. Asserted on
        /// SteepestGrade (the R1 statistic, §9.3 "Also published").</summary>
        private static void KthAndAbsoluteDropTests()
        {
            for (int m = 1; m <= 2; m++)
            {
                float step = Step(m);
                double zr = Terrain.Origin.y + 450 * (double)step; // a lattice row at this step
                double xa = 1000.1;
                Func<double, double> quad = x => 1000.0 + 0.001 * (x - xa) * (x - xa);
                Func<double, double> wash = x => 1000.0 + 2.0 * Math.Sin(2.0 * Math.PI * x / 18.0);
                KthCase("kth/quadratic 1 row m" + m, quad, new double2(xa + 30, zr - 1.2), new double2(xa + 230, zr + 1.2), m);
                KthCase("kth/quadratic 2 rows m" + m, quad, new double2(xa + 30, zr - 1.2), new double2(xa + 230, zr + step + 1.2), m);
                KthCase("kth/quadratic 3 rows m" + m, quad, new double2(xa + 30, zr - 1.2), new double2(xa + 230, zr + 2 * step + 1.2), m);
                KthCase("absdrop/washboard 18 m m" + m, wash, new double2(xa + 0.7, zr - 20), new double2(xa + 200.7, zr + 20), m);
            }
            Console.WriteLine("INFO  kth/absdrop: CellGrade asserted against the per-cell oracle (20/15/10/7 m row windows, |g| fallback) on " + s_KthCells +
                              " Mask cells (8 fixtures, m = 1 and 2)");
        }

        private static void KthCase(string w, Func<double, double> prof, double2 lo, double2 hi, int m)
        {
            double2[] ring = { lo, new double2(hi.x, lo.y), hi, new double2(lo.x, hi.y) };
            Terrain t = Terrain.For((x, z) => prof(x), ring, 60);
            SlopeStatus st = Rate(t, ring, m);
            SlopeGrid g = s_Grid;
            float step = Step(m);
            double2[] v = AsDouble(Pts(ring, false));
            double half = 0.5 * SkiSlopeClassifier.WindowLength / step;
            int reach = (int)Math.Floor(half + 0.5);
            List<double> vals = new List<double>();
            for (int j = 0; j < g.Nz; j++)
            {
                for (int i = 0; i < g.Nx; i++)
                {
                    if (!OracleInside(v, NodeWorld(g, i, j)))
                        continue;
                    bool ok = true;
                    for (int a = -reach; a <= reach && ok; a++)
                        ok = i + a >= 0 && i + a < g.Nx && OracleInside(v, NodeWorld(g, i + a, j));
                    if (!ok)
                        continue;
                    double pl0 = RowPl(t, g, j, i - half), pl1 = RowPl(t, g, j, i + half);
                    vals.Add(Math.Abs(pl1 - pl0) / SkiSlopeClassifier.WindowLength);
                }
            }
            vals.Sort();
            vals.Reverse();
            int k = RingK(step);
            if (!Check(vals.Count >= k, w, "fixture has only " + vals.Count + " full windows (k=" + k + ")"))
                return;
            double want = vals[k - 1];
            Check(st == SlopeStatus.Ok, w, "status " + st);
            Check(s_Result.ValidWindows == vals.Count, w, "ValidWindows " + s_Result.ValidWindows + " vs oracle " + vals.Count);
            Check(Math.Abs(s_Result.SteepestGrade - want) <= 2e-5, w, "steepest " + P(s_Result.SteepestGrade) + " vs oracle k-th(" + k + ") " + P(want) +
                                                                     " (max " + P(vals[0]) + ")");
            Check(s_Result.RatingGrade >= 0f && s_Result.SteepestGrade >= 0f, w, "negative rating");

            // Per-cell value o (CellGrade — what the crux and the heat-map read) for EVERY Mask
            // cell, from the spec text (R2 fixer round, review finding tests-1): on a z-invariant
            // profile the box gradient is along ±x, so o = the longest centred row window of
            // {20, 15, 10, 7} m (a length only when >= 2 steps; 20 always) whose nodes
            // round(-half) .. round(+half) are all inside, else |g| of the 5×5 box =
            // |h(i+3) + h(i+2) - h(i-2) - h(i-3)| / (10·step) on the row.
            double[] lens = { SkiSlopeClassifier.WindowLength, 15.0, 10.0, 7.0 };
            int cellBad = 0, cells = 0, fullCells = 0;
            string firstBad = null;
            for (int j = 0; j < g.Nz; j++)
            {
                for (int i = 0; i < g.Nx; i++)
                {
                    if (!OracleInside(v, NodeWorld(g, i, j)) || g.Mask[j * g.Nx + i] == 0)
                        continue;
                    cells++;
                    double o = double.NaN;
                    for (int li = 0; li < lens.Length && double.IsNaN(o); li++)
                    {
                        double L = lens[li];
                        if (li > 0 && L < 2.0 * step)
                            continue;
                        double hl = 0.5 * L / step;
                        int a0 = (int)Math.Floor(-hl + 0.5), a1 = (int)Math.Floor(hl + 0.5);
                        bool fits = true;
                        for (int a = a0; a <= a1 && fits; a++)
                            fits = i + a >= 1 && i + a < g.Nx - 1 && OracleInside(v, NodeWorld(g, i + a, j));
                        if (!fits)
                            continue;
                        o = Math.Abs(RowPl(t, g, j, i + hl) - RowPl(t, g, j, i - hl)) / L;
                        if (li == 0)
                            fullCells++;
                    }
                    if (double.IsNaN(o))
                        o = Math.Abs(NodeHeight(t, g, i + 3, j) + NodeHeight(t, g, i + 2, j) - NodeHeight(t, g, i - 2, j) - NodeHeight(t, g, i - 3, j)) /
                            (10.0 * step);
                    float got = s_Result.CellGrade[j * g.Nx + i];
                    if (!(Math.Abs(got - o) <= 2e-5))
                    {
                        cellBad++;
                        if (firstBad == null)
                            firstBad = "cell (" + i + "," + j + ") CellGrade " + Rt(got) + " vs oracle " + F(o);
                    }
                }
            }
            Check(cellBad == 0, w, cellBad + " of " + cells + " Mask cells with CellGrade off the per-cell oracle by > 2e-5, first " + firstBad);
            Check(fullCells == vals.Count, w, "per-cell oracle full windows " + fullCells + " vs " + vals.Count);
            s_KthCells += cells;
        }

        private static long s_KthCells;

        private static double RowPl(Terrain t, SlopeGrid g, int j, double x)
        {
            int i0 = (int)Math.Floor(x);
            double fx = x - i0;
            double h0 = NodeHeight(t, g, i0, j), h1 = NodeHeight(t, g, i0 + 1, j);
            return h0 + (h1 - h0) * fx;
        }

        private static double NodeHeight(Terrain t, SlopeGrid g, int i, int j)
        {
            double2 p = NodeWorld(g, i, j);
            return t.Sample((float)p.x, (float)p.y);
        }

        // ================================================================== mask clipping

        private static void OutsideTerrainTests()
        {
            double g = 0.105;
            double worstCliffAtEdge = 0, worstCliffAtEdgeSteep = 0;
            for (int m = 1; m <= 2; m++)
            {
                double step = Step(m);
                double worst = 0, worstSteep = 0;
                foreach (double ang in Angles7)
                {
                    Frame fr = Frame.Make(-2500.5, -1500.5, ang);
                    double2[] ring = Rect(300, 60, fr);
                    // Cliffs (60 %) on one side and past the downhill end, starting one lattice
                    // step outside the polygon: a valid window's endpoint lies within half a cell of
                    // an inside node, so its bilinear can read at most one node beyond the last
                    // inside node — anything further out must never move a FULL window. The crux
                    // also reads the fallback values of edge cells (short windows, |g| from the
                    // 5×5 box, which reaches 3 nodes out); the easiest line avoids them.
                    HeightFn cliff = (x, z) =>
                    {
                        double s = fr.Along(x, z), t = fr.Across(x, z);
                        return 1000.0 - g * s - 0.6 * Math.Max(0.0, t - 30.0 - step) - 0.6 * Math.Max(0.0, s - 300.0 - step);
                    };
                    Terrain tc = Terrain.For(cliff, ring, 80);
                    string w = "outside/cliff a" + ang + " m" + m;
                    SlopeStatus st = Rate(tc, ring, m);
                    Check(st == SlopeStatus.Ok, w, "status " + st);
                    double d = s_Result.RatingGrade - g, ds = s_Result.SteepestGrade - g;
                    if (Math.Abs(d) > Math.Abs(worst)) worst = d;
                    if (Math.Abs(ds) > Math.Abs(worstSteep)) worstSteep = ds;
                    // The crux gets 1 %, not 0.5 %: cells 2-3 nodes from the cliff side have a box
                    // gradient tilted toward the cliff, so their (valid) window runs off the fall
                    // line and reads g·cos(tilt) < g; the easiest line uses them (measured -0.55 %).
                    Check(Math.Abs(d) <= 0.01, w, "outside terrain moved the rating to " + P(s_Result.RatingGrade));
                    Check(Math.Abs(ds) <= 0.005, w, "outside terrain moved the steepest patch to " + P(s_Result.SteepestGrade));
                    HeightFn edge = (x, z) =>
                    {
                        double s = fr.Along(x, z), t = fr.Across(x, z);
                        return 1000.0 - g * s - 0.6 * Math.Max(0.0, t - 30.0) - 0.6 * Math.Max(0.0, s - 300.0);
                    };
                    Rate(Terrain.For(edge, ring, 80), ring, m);
                    worstCliffAtEdge = Math.Max(worstCliffAtEdge, s_Result.RatingGrade - g);
                    worstCliffAtEdgeSteep = Math.Max(worstCliffAtEdgeSteep, s_Result.SteepestGrade - g);
                }
                Console.WriteLine("INFO  outside cliff (one step out) m=" + m + ": worst rating shift " + P(worst) + ", steepest " + P(worstSteep));
            }
            Console.WriteLine("INFO  outside cliff starting exactly AT the boundary (report only): worst rating shift " + P(worstCliffAtEdge) +
                              ", steepest " + P(worstCliffAtEdgeSteep) +
                              " — the half-cell rounding of the mask check lets a valid endpoint's bilinear read one node past the last inside node");
        }

        // ================================================================== R2 scenarios: fixtures

        private const double MeadowGrade = 0.105;

        private static double2[] LRect(double len, double width)
        {
            return new[] { new double2(0, -width / 2), new double2(len, -width / 2), new double2(len, width / 2), new double2(0, width / 2) };
        }

        private static double Clamp01(double x)
        {
            return x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x;
        }

        /// <summary>g0 everywhere except a full-width pitch gp over [s0, s0 + len].</summary>
        private static LocalHeight PitchProfile(double g0, double s0, double len, double gp)
        {
            return (s, t) =>
            {
                if (s < s0) return 1000.0 - g0 * s;
                if (s < s0 + len) return 1000.0 - g0 * s0 - gp * (s - s0);
                return 1000.0 - g0 * s0 - gp * len - g0 * (s - s0 - len);
            };
        }

        /// <summary>k with |(-g0, 0) + k·n| = gb for a unit n = (ns, nt): the bank term k·d added
        /// to a g0 meadow makes the combined grade gb.</summary>
        private static double BankK(double g0, double ns, double gb)
        {
            return g0 * ns + Math.Sqrt(g0 * g0 * ns * ns - g0 * g0 + gb * gb);
        }

        /// <summary>A g0 meadow L × W whose top corner (s = 0, t = +W/2) lies on a bank: the
        /// polygon's corner triangle with legs legS (down the side) × legT (along the top edge) is
        /// on a half-plane bank whose combined grade is gb; the bank continues outside the
        /// polygon. Optional gentle undulation (amplitude `wave` m).</summary>
        private static Scn CornerBankScn(string name, double len, double width, double g0, double legS, double legT, double gb, double wave)
        {
            double ax = legS, at = width / 2, bx = 0.0, bt = width / 2 - legT;
            double ex = bx - ax, et = bt - at, el = Math.Sqrt(ex * ex + et * et);
            double ns = -et / el, nt = ex / el;
            if ((0.0 - ax) * ns + (width / 2 - at) * nt < 0.0)
            {
                ns = -ns;
                nt = -nt;
            }
            double k = BankK(g0, ns, gb);
            LocalHeight h = (s, t) => 1000.0 - g0 * s + wave * Math.Sin(2 * Math.PI * s / 140.0 + 0.3) * Math.Cos(2 * Math.PI * t / 110.0)
                                      + k * Math.Max(0.0, (s - ax) * ns + (t - at) * nt);
            return new Scn(name, F(len) + "x" + F(width), LRect(len, width), h);
        }

        // R2-a corner triangle legs (m). §9.4 says "≈ 30 × 40 m"; 35 × 45 is the smallest tried
        // that meets the fixture's own premise "SteepestGrade >= 35 %" in every orientation
        // (tuning sweep, 7 rotations × 2 offsets, m = 1: 30×40 -> 32.16-37.88 %, 40×30 ->
        // 32.84-40.36 %, 35×45 -> 39.28-44.81 %, 40×40 -> 38.71-43.83 %, 40×50 -> 44.79-45.01 %;
        // the crux rating was <= 10.74 % for all of them).
        private const double R2aLegS = 35.0, R2aLegT = 45.0;

        private static Scn R2a()
        {
            return CornerBankScn("R2-a avoidable corner 45%", 400, 100, MeadowGrade, R2aLegS, R2aLegT, 0.45, 0.0);
        }

        private static Scn R2b()
        {
            // 45 % bank along the +t side, 30 % of the width, over the middle third, with 20 m
            // ramps in and out (avoidable, so their steepness does not matter).
            double len = 400, width = 100, t0 = width / 2 - 0.3 * width, s1 = len / 3, s2 = 2 * len / 3;
            double kb = Math.Sqrt(0.45 * 0.45 - MeadowGrade * MeadowGrade);
            return new Scn("R2-b avoidable side bank 45%", "400x100", LRect(len, width),
                (s, t) => 1000.0 - MeadowGrade * s + kb * Math.Max(0.0, t - t0) * Clamp01((s - (s1 - 20.0)) / 20.0) * Clamp01((s2 + 20.0 - s) / 20.0));
        }

        private static Scn R2c()
        {
            return new Scn("R2-c headwall 45% x60 mid-run", "400x100", LRect(400, 100), PitchProfile(MeadowGrade, 170, 60, 0.45));
        }

        private static Scn R2d()
        {
            double2[] hourglass =
            {
                new double2(0, -50), new double2(100, -50), new double2(160, -7.5), new double2(240, -7.5), new double2(300, -50), new double2(400, -50),
                new double2(400, 50), new double2(300, 50), new double2(240, 7.5), new double2(160, 7.5), new double2(100, 50), new double2(0, 50),
            };
            return new Scn("R2-d chute 15 m @ 50% x60", "400x100 hourglass", hourglass, PitchProfile(MeadowGrade, 170, 60, 0.50));
        }

        private static Scn R2e(double headLen)
        {
            return new Scn("R2-e top headwall 45% x" + F(headLen), "600x60", LRect(600, 60),
                (s, t) => s < headLen ? 1000.0 - 0.45 * s : 1000.0 - 0.45 * headLen - MeadowGrade * (s - headLen));
        }

        /// <summary>Plan + fill + classify one scenario at a frame (terrain built per call).</summary>
        private static SlopeStatus RunScn(Scn sc, Frame fr, int m, SlopeGrid g, SlopeResult r, string where)
        {
            return RunScnWith(sc, fr, m, g, r, Zp.Shipped, where);
        }

        private static SlopeStatus RunScnWith(Scn sc, Frame fr, int m, SlopeGrid g, SlopeResult r, Zp zp, string where)
        {
            double2[] ring = sc.Ring(fr);
            Terrain t = Terrain.For(sc.Height(fr), ring, 60);
            return RateWith(t, Pts(ring, false), m, g, r, zp, where);
        }

        private static int ScnStep(Scn sc)
        {
            return SkiSlopeClassifier.StepMultiple(sc.Area(), Terrain.Texel, 0);
        }

        private static readonly double2[] ScnStarts = { new double2(0.3, -0.7), new double2(-3123.4, 2345.6) };

        // ================================================================== R2-a / R2-b avoidable

        private static void AvoidableTests()
        {
            foreach (Scn sc in new[] { R2a(), R2b() })
            {
                bool isA = sc.Name.StartsWith("R2-a", StringComparison.Ordinal);
                int op = ScnStep(sc);
                for (int m = 1; m <= 2; m++)
                {
                    bool asserted = m == op;
                    double lo = 1, hi = -1, sLo = 1, sHi = -1;
                    foreach (double2 st0 in ScnStarts)
                    {
                        foreach (double ang in Angles7)
                        {
                            Frame fr = Frame.Make(st0.x, st0.y, ang);
                            string w = sc.Name + " a" + ang + " at " + F(st0.x) + " m" + m;
                            SlopeStatus st = RunScn(sc, fr, m, s_Grid, s_Result, w);
                            SlopeResult r = s_Result;
                            lo = Math.Min(lo, r.RatingGrade);
                            hi = Math.Max(hi, r.RatingGrade);
                            sLo = Math.Min(sLo, r.SteepestGrade);
                            sHi = Math.Max(sHi, r.SteepestGrade);
                            if (!asserted)
                                continue;
                            Check(st == SlopeStatus.Ok, w, "status " + st);
                            Check(r.Band == SlopeBand.Green && r.RatingGrade <= 0.13f, w, "rating " + P(r.RatingGrade) + " " + r.Band + ", want Green <= 13 %");
                            if (isA)
                                Check(r.SteepestGrade >= 0.35f, w, "fixture: SteepestGrade " + P(r.SteepestGrade) + " < 35 % (the corner must read steep)");
                        }
                    }
                    Console.WriteLine("INFO  " + sc.Name + " " + sc.Size + " m=" + m + " (" + Mode(asserted) + (asserted ? ": Green, rating <= 13 %" +
                                      (isA ? ", steepest >= 35 %" : "") : "") + "): rating [" + P(lo) + ", " + P(hi) + "], steepest [" + P(sLo) + ", " + P(sHi) + "]");
                }
            }
        }

        // ================================================================== R2-c / R2-d unavoidable

        private static void UnavoidableTests()
        {
            foreach (Scn sc in new[] { R2c(), R2d() })
            {
                bool isC = sc.Name.StartsWith("R2-c", StringComparison.Ordinal);
                double want = isC ? 0.45 : 0.50;
                int op = ScnStep(sc);
                for (int m = 1; m <= 2; m++)
                {
                    bool asserted = m == op;
                    double lo = 1, hi = -1, cs0 = 1e9, cs1 = -1e9, f4Lo = 1e9, f4Hi = -1e9;
                    int f4Disc = 0;
                    foreach (double2 st0 in ScnStarts)
                    {
                        foreach (double ang in Angles7)
                        {
                            Frame fr = Frame.Make(st0.x, st0.y, ang);
                            string w = sc.Name + " a" + ang + " at " + F(st0.x) + " m" + m;
                            SlopeStatus st = RunScn(sc, fr, m, s_Grid, s_Result, w);
                            SlopeResult r = s_Result;
                            lo = Math.Min(lo, r.RatingGrade);
                            hi = Math.Max(hi, r.RatingGrade);
                            float f4 = OracleMinimax(s_Grid, r, Conn4);   // 4-vs-8 evidence (report only)
                            if (float.IsNaN(f4))
                            {
                                f4Disc++;
                            }
                            else
                            {
                                f4Lo = Math.Min(f4Lo, f4);
                                f4Hi = Math.Max(f4Hi, f4);
                            }
                            double cs = double.NaN;
                            if (r.CruxValid)
                            {
                                double2 cw = CruxWorld(r);
                                cs = fr.Along(cw.x, cw.y);
                                cs0 = Math.Min(cs0, cs);
                                cs1 = Math.Max(cs1, cs);
                            }
                            if (!asserted)
                                continue;
                            Check(st == SlopeStatus.Ok && r.Connected, w, "status " + st + ", connected " + r.Connected);
                            Check(Math.Abs(r.RatingGrade - want) <= 0.03 && r.Band == SlopeBand.Black, w,
                                "rating " + P(r.RatingGrade) + " " + r.Band + ", want " + P(want) + " ± 3 % Black");
                            if (isC)
                                Check(r.CruxValid && cs >= 170.0 && cs <= 230.0, w, "crux at s = " + F(cs) + " m, want inside the band [170, 230]");
                        }
                    }
                    Console.WriteLine("INFO  " + sc.Name + " " + sc.Size + " m=" + m + " (" + Mode(asserted) + (asserted ? ": " + P(want) + " ± 3 % Black" +
                                      (isC ? ", crux in the band" : "") : "") + "): rating [" + P(lo) + ", " + P(hi) + "], crux s in [" + F(cs0) + ", " + F(cs1) +
                                      "] m; 4-connected exact minimax [" + P(f4Lo) + ", " + P(f4Hi) + "]" + (f4Disc > 0 ? ", " + f4Disc + " disconnected" : "") + " (report only)");
                }
            }
        }

        /// <summary>A full-width 50 % pitch only `len` m long mid-run in a 10.5 % 400 × 100 run
        /// (R2 fixer round, review finding tests-1: R2-c / R2-d's 60 m pitches read the same for
        /// any window from 15 to 30 m, so they cannot see a wrong per-cell window length).</summary>
        private static Scn ShortHeadwall(double len)
        {
            return new Scn("R2-c short headwall 50% x" + F(len), "400x100", LRect(400, 100), PitchProfile(MeadowGrade, 190, len, 0.50));
        }

        private static void ShortHeadwallTests()
        {
            foreach (double len in new double[] { 16, 18, 20 })
            {
                Scn sc = ShortHeadwall(len);
                Func<double, double> prof = s => sc.H(s, 0);
                double oracle = OracleMaxWindow(prof, new[] { 190.0, 190.0 + len }, 0, 400, SkiSlopeClassifier.WindowLength);
                int op = ScnStep(sc);
                for (int m = 1; m <= 2; m++)
                {
                    bool asserted = m == op;
                    double lo = 1, hi = -1, sLo = 1, sHi = -1;
                    string envKey = "short headwall x" + F(len) + " m" + m, sEnvKey = "short headwall steepest x" + F(len) + " m" + m;
                    double env = s_CruxEnvelope.ContainsKey(envKey) ? s_CruxEnvelope[envKey] : 0.01;
                    double sEnv = s_CruxEnvelope.ContainsKey(sEnvKey) ? s_CruxEnvelope[sEnvKey] : 0.01;
                    foreach (double2 st0 in ScnStarts)
                    {
                        foreach (double ang in Angles7)
                        {
                            Frame fr = Frame.Make(st0.x, st0.y, ang);
                            string w = sc.Name + " a" + ang + " at " + F(st0.x) + " m" + m;
                            SlopeStatus st = RunScn(sc, fr, m, s_Grid, s_Result, w);
                            double d = s_Result.RatingGrade - oracle, ds = s_Result.SteepestGrade - oracle;
                            lo = Math.Min(lo, d);
                            hi = Math.Max(hi, d);
                            sLo = Math.Min(sLo, ds);
                            sHi = Math.Max(sHi, ds);
                            if (!asserted)
                                continue;
                            Check(st == SlopeStatus.Ok && s_Result.Connected, w, "status " + st + ", connected " + s_Result.Connected);
                            Check(d >= -env && d <= 0.01, w, "crux rating " + P(s_Result.RatingGrade) + " vs closed-form max window " + P(oracle) +
                                                             " (asserted [-" + P(env) + ", +1.00%])");
                            Check(ds >= -sEnv && ds <= 0.01, w, "steepest " + P(s_Result.SteepestGrade) + " vs closed-form max window " + P(oracle) +
                                                                " (asserted [-" + P(sEnv) + ", +1.00%])");
                        }
                    }
                    Console.WriteLine("INFO  " + sc.Name + " " + sc.Size + " m=" + m + " (" + (asserted ? "asserted: crux - oracle in [-" + P(env) + ", +1.00%], steepest in [-" +
                                      P(sEnv) + ", +1.00%]" + (env > 0.01 || sEnv > 0.01 ? " — lattice regression envelope, no spec bound" : "") : "report only: not the operating step") +
                                      "): closed-form max 20 m window " + P(oracle) + "; crux rating - oracle in [" + P(lo) + ", " + P(hi) + "], steepest - oracle in [" +
                                      P(sLo) + ", " + P(sHi) + "]");
                }
            }
        }

        /// <summary>The reviewer's open-last-bucket case (R2 fixer round, finding
        /// classifier-last-bucket-open): a 400 × 100 run on a 10.5 % plane with a full-width 45 m
        /// vertical step at s = 201 m (every line crosses ~235 %), optionally plus an AVOIDABLE
        /// 60 m-deep pit at s 90-130 m, t &lt;= -32 m (a line passes at t > -10 m). Before the fix
        /// the pit's ~310 % cells shared bucket 2047 with the step, so the rating read the pit
        /// (310.72 % vs an exact minimax of 235.45 %) and the crux ring sat in it.</summary>
        private static Scn StepScn(bool pit)
        {
            return new Scn(pit ? "R2-l 45 m step + avoidable 60 m pit" : "R2-l 45 m step alone", "400x100", LRect(400, 100),
                (s, t) => 1000.0 - MeadowGrade * s - (s >= 201.0 ? 45.0 : 0.0) - (pit && s >= 90.0 && s <= 130.0 && t <= -32.0 ? 60.0 : 0.0));
        }

        private static void OpenLastBucketTests()
        {
            Scn alone = StepScn(false), withPit = StepScn(true);
            int op = ScnStep(withPit);
            double lo = 1e9, hi = -1e9, sLo = 1e9, sHi = -1e9, worstDiff = 0, steepLo = 1e9, steepHi = -1e9;
            foreach (double2 st0 in ScnStarts)
            {
                foreach (double ang in Angles7)
                {
                    Frame fr = Frame.Make(st0.x, st0.y, ang);
                    string w = "R2-l a" + ang + " at " + F(st0.x) + " m" + op;
                    // OracleCheck (inside RateWith) asserts rating == exact minimax and the exact crux cell.
                    SlopeStatus s1 = RunScn(alone, fr, op, s_Grid, s_Result, w + " step alone");
                    float r1 = s_Result.RatingGrade;
                    SlopeStatus s2 = RunScn(withPit, fr, op, s_Grid, s_Result, w + " step + pit");
                    SlopeResult r = s_Result;
                    lo = Math.Min(lo, r.RatingGrade);
                    hi = Math.Max(hi, r.RatingGrade);
                    steepLo = Math.Min(steepLo, r.SteepestGrade);
                    steepHi = Math.Max(steepHi, r.SteepestGrade);
                    worstDiff = Math.Max(worstDiff, Math.Abs(r.RatingGrade - r1));
                    Check(s1 == SlopeStatus.Ok && s2 == SlopeStatus.Ok && r.Connected && r.CruxValid, w, "status " + s1 + "/" + s2);
                    Check(r.RatingGrade >= 2.0f && r.Band == SlopeBand.DoubleBlack, w, "rating " + P(r.RatingGrade) + " " + r.Band + ", want the step (>= 200 %)");
                    Check(r.RatingGrade == r1, w, "an AVOIDABLE pit moved the rating: " + P(r1) + " (step alone) -> " + P(r.RatingGrade));
                    double2 cw = CruxWorld(r);
                    double cs = fr.Along(cw.x, cw.y), ct = fr.Across(cw.x, cw.y);
                    sLo = Math.Min(sLo, cs);
                    sHi = Math.Max(sHi, cs);
                    Check(cs >= 185.0 && cs <= 217.0, w, "crux at s = " + F(cs) + ", t = " + F(ct) + " m, want on the step (s 185-217)");
                }
            }
            Console.WriteLine("INFO  R2-l open last bucket (400x100, 45 m step + avoidable 60 m pit, m=" + op + ", asserted: rating == the step alone == the exact " +
                              "minimax, crux on the step): rating [" + P(lo) + ", " + P(hi) + "], step-alone difference max " + P(worstDiff) + ", crux s in [" + F(sLo) +
                              ", " + F(sHi) + "] m; SteepestGrade [" + P(steepLo) + ", " + P(steepHi) + "] (the pit's walls)");
        }

        // ================================================================== R2-e top headwall (report)

        /// <summary>The invariance starts (InvarianceTests) — R2-e and the limit reports use all
        /// of them, so a printed band range is honest (R2 fixer round, review finding tests-2: one
        /// start printed "Blue..Blue" for R2-e x40, whose ratings over these starts span
        /// Blue..Black).</summary>
        private static readonly double2[] InvStarts = { new double2(0.3, -0.7), new double2(7000.3, -6999.6), new double2(-3123.4, 2345.6) };

        /// <summary>A 30 m drop-in at 45 % at the very top of a 12 % run, 600 m long (limit (a) by
        /// run width, review finding rule-soundness-end-zone-area-cap).</summary>
        private static Scn DropIn(double width)
        {
            return new Scn("drop-in 30 m @45% top of 12% w" + F(width), "600x" + F(width), LRect(600, width),
                (s, t) => s < 30.0 ? 1000.0 - 0.45 * s : 1000.0 - 13.5 - 0.12 * (s - 30.0));
        }

        /// <summary>A 20 m-wide 1,500 m trail at 20 % with a full-width 40 m wall at 45 % from s0
        /// (the area cap skips 2000 / 20 = 100 m at each end).</summary>
        private static Scn Trail20Wall(double s0, string where)
        {
            return new Scn("trail20 45% wall " + where, "1500x20", LRect(1500, 20), PitchProfile(0.20, s0, 40, 0.45));
        }

        /// <summary>A 10 m-wide 2,000 m trail at 20 % with a full-width 60 m wall at 50 % at
        /// s 100-160 (the area cap skips 200 m at each end).</summary>
        private static Scn Trail10Wall()
        {
            return new Scn("trail10 50% wall s100-160", "2000x10", LRect(2000, 10), PitchProfile(0.20, 100, 60, 0.50));
        }

        private static void TopHeadwallReport()
        {
            List<Scn> scns = new List<Scn> { R2e(20), R2e(40), R2e(80) };
            foreach (double wdt in new double[] { 20, 60, 125, 200 })
                scns.Add(DropIn(wdt));
            foreach (Scn sc in scns)
            {
                int op = ScnStep(sc);
                double lo = 1, hi = -1, width = sc.LocalRing[2].y - sc.LocalRing[1].y;
                SlopeBand bLo = SlopeBand.DoubleBlack, bHi = SlopeBand.Flat;
                int topLo = int.MaxValue, topHi = 0;
                foreach (double2 st0 in InvStarts)
                {
                    foreach (double ang in Angles7)
                    {
                        Frame fr = Frame.Make(st0.x, st0.y, ang);
                        RunScn(sc, fr, op, s_Grid, s_Result, sc.Name + " a" + ang + " at " + F(st0.x));
                        lo = Math.Min(lo, s_Result.RatingGrade);
                        hi = Math.Max(hi, s_Result.RatingGrade);
                        if (s_Result.Band < bLo) bLo = s_Result.Band;
                        if (s_Result.Band > bHi) bHi = s_Result.Band;
                        topLo = Math.Min(topLo, s_Result.TopZoneCells);
                        topHi = Math.Max(topHi, s_Result.TopZoneCells);
                    }
                }
                double zoneLen = topHi * (double)Step(op) * Step(op) / width;
                bool dropIn = sc.Name.StartsWith("drop-in", StringComparison.Ordinal);
                Console.WriteLine("INFO  " + sc.Name + " on " + sc.Size + " m=" + op + " (report only" + (dropIn ? ", the asserted 8-frame check is in R2b end zones" : ", §9.8 limit") +
                                  "; " + InvStarts.Length + " starts x " + Angles7.Length + " angles): rating [" + P(lo) + ", " + P(hi) + "], band " + bLo + ".." + bHi +
                                  "; top zone " + topLo + "-" + topHi + " cells ≈ the top " + F(Math.Round(zoneLen, 1)) + " m (fall-line key, EndZoneDepthM = " +
                                  F(SkiSlopeClassifier.EndZoneDepthM) + " m; R2's area cap was EndZoneMaxM2 / width = " +
                                  F(Math.Round(SkiSlopeClassifier.EndZoneMaxM2 / width, 1)) + " m)");
            }
        }

        // ================================================================== R2b end zones (§9.8)

        private const double KValley = 0.30;   // valley side cross-slope (R2 report fixture)

        private static Scn Valley()
        {
            return new Scn("valley 600x120: 40 m floor @15%, 30% sides", "600x120", LRect(600, 120),
                (s, t) => 1000.0 - 0.15 * s + KValley * Math.Max(0.0, Math.Abs(t) - 20.0));
        }

        private static Scn SteepValley(double wall)
        {
            return new Scn("steep-wall valley 600x120: 10% floor, " + F(wall * 100) + "% walls", "600x120", LRect(600, 120),
                (s, t) => 1000.0 - 0.10 * s + wall * Math.Max(0.0, Math.Abs(t) - 20.0));
        }

        private static Scn OpenBowl()
        {
            double kBowl = Math.Sqrt(0.42 * 0.42 - 0.15 * 0.15);
            return new Scn("open bowl 400x240: 120 m floor @15%, 42% walls", "400x240", LRect(400, 240),
                (s, t) => 1000.0 - 0.15 * s + kBowl * Math.Max(0.0, Math.Abs(t) - 60.0));
        }

        private static Scn Ridge(double len, double width)
        {
            return new Scn("ridge " + F(len) + "x" + F(width) + ": 12% crest, 27.7% flanks", F(len) + "x" + F(width), LRect(len, width),
                (s, t) => 1000.0 - 0.12 * s - 0.25 * Math.Abs(t));
        }

        private static Scn SideBank()
        {
            double kBank = Math.Sqrt(0.45 * 0.45 - 0.15 * 0.15);
            return new Scn("side bank 150x600: 15% run, 50 m 45% bank full length", "600x150", LRect(600, 150),
                (s, t) => 1000.0 - 0.15 * s + kBank * Math.Max(0.0, t - 25.0));
        }

        private static Scn CornerPoke(double legS, double legT)
        {
            return CornerBankScn("F116 meadow, 48% corner poke " + F(legS) + "x" + F(legT), 545, 125, F116Grade, legS, legT, F116BankGrade, F116Wave);
        }

        /// <summary>A 30 m-wide 71 % strip along the +t side of the F116 meadow (545 × 125 at 16 %)
        /// from the top down to s = 280 m, fading out by s = 300 m: 13 % of the area.</summary>
        private static Scn Sliver71()
        {
            double k71 = Math.Sqrt(0.71 * 0.71 - F116Grade * F116Grade);
            return new Scn("71% sliver along the top side (13% of area)", "545x125", LRect(545, 125),
                (s, t) => 1000.0 - F116Grade * s + k71 * Math.Max(0.0, t - 32.5) * Clamp01((300.0 - s) / 20.0));
        }

        private static Scn TopFace71()
        {
            return new Scn("full-width 71% top face 40 m on 16%", "800x125", LRect(800, 125), PitchProfile(F116Grade, 0, 40, 0.71));
        }

        private static Scn MountainFace68()
        {
            return new Scn("68% mountain face (±0.5 m undulation)", "600x150", LRect(600, 150),
                (s, t) => 1000.0 - 0.68 * s + 0.5 * Math.Sin(2 * Math.PI * s / 140.0 + 0.3) * Math.Cos(2 * Math.PI * t / 110.0));
        }

        private static Scn WideShort()
        {
            return new Scn("wide-short headwall 45% s50-90 on 10.5%", "150x400", LRect(150, 400), PitchProfile(MeadowGrade, 50, 40, 0.45));
        }

        /// <summary>An L-shaped run on 15 %: leg 1 along s (0..430, 60 m wide), leg 2 along +t
        /// (s 370..430, t 30..400) with a 45 % pitch at t 200-240 (the skeptic's lab fixture).</summary>
        private static Scn LRun(bool pitch)
        {
            double2[] l = { new double2(0, -30), new double2(430, -30), new double2(430, 400), new double2(370, 400), new double2(370, 30), new double2(0, 30) };
            return new Scn(pitch ? "L-run 15%, 45% pitch in leg 2" : "L-run 15%, no pitch", "430x430 L", l,
                (s, t) => 1000.0 - 0.15 * Math.Min(s, 400.0) - 0.15 * Math.Max(t, 0.0) - (pitch ? 0.30 * Math.Min(Math.Max(t - 200.0, 0.0), 40.0) : 0.0));
        }

        private static readonly double2[] Square300 = { new double2(-150, -150), new double2(150, -150), new double2(150, 150), new double2(-150, 150) };

        private static Scn Cone()
        {
            return new Scn("cone peak 30% (centred)", "300x300", Square300, (s, t) => 1000.0 - 0.30 * Math.Sqrt(s * s + t * t));
        }

        private static Scn Crater()
        {
            return new Scn("crater 30% (centred)", "300x300", Square300, (s, t) => 1000.0 + 0.30 * Math.Sqrt(s * s + t * t));
        }

        private static Scn Saddle()
        {
            return new Scn("saddle (centred)", "300x300", Square300, (s, t) => 1000.0 + 0.0015 * (s * s - t * t));
        }

        private static Scn SideCliff()
        {
            double kCliff = Math.Sqrt(0.60 * 0.60 - MeadowGrade * MeadowGrade);
            return new Scn("side cliff 60% over 40% of a 150 m width, 10.5% floor", "600x150", LRect(600, 150),
                (s, t) => 1000.0 - MeadowGrade * s + kCliff * Math.Max(0.0, t - 15.0));
        }

        private static Scn OneSidedValley()
        {
            return new Scn("one-sided valley 600x120: floor @15%, 60 m of 30% side", "600x120", LRect(600, 120),
                (s, t) => 1000.0 - 0.15 * s + KValley * Math.Max(0.0, t));
        }

        // C-run geometry (local s = x, t = z): two straight 150 m arms 40 m wide on centrelines
        // s = +60 (the first arm, run descends +t) and s = -60 (the last arm, descends -t), joined
        // by a half-annulus bend of centreline radius 60 m around (0, 150).
        private const double CArmLen = 150.0, CRad = 60.0, CWidth = 40.0;

        /// <summary>The C-run's position along its centreline u (m from the start) and its
        /// outward distance d from the centreline (away from the hill), for local (s, t).</summary>
        private static double2 CRunUd(double s, double t)
        {
            if (t <= CArmLen)
                return s > 0.0 ? new double2(t, s - CRad) : new double2(CArmLen + Math.PI * CRad + (CArmLen - t), -s - CRad);
            double phi = Math.Atan2(t - CArmLen, s);   // 0 (east) .. π (west) through the bend's apex
            return new double2(CArmLen + CRad * phi, Math.Sqrt(s * s + (t - CArmLen) * (t - CArmLen)) - CRad);
        }

        /// <summary>Known and deliberate (§9.8): a C-run 180° around a convex hill. The run climbs
        /// down the first arm at 40 % for its first 120 m (the steep first arm), then 12 % through
        /// the rest of the arm, the bend and the last arm (the run ends beside its own start); the
        /// hill falls 10 % away from the run's centreline outward everywhere, so the fall line
        /// turns with the run. The arms' along-run fall lines (+t, -t) cancel in the mean, which
        /// therefore points along +t (the steep arm wins): the fall-line key puts BOTH arms'
        /// south ends (the run's start and its end) in the top zone and the bend in the bottom
        /// zone, so the easiest "line" climbs the gentle last arm to the bend — lenient.</summary>
        private static Scn CRunHill()
        {
            const double hw = CWidth / 2;
            List<double2> ring = new List<double2>
            {
                new double2(CRad + hw, 0), new double2(CRad + hw, CArmLen),
            };
            for (int a = 5; a < 180; a += 5)
                ring.Add(new double2((CRad + hw) * Math.Cos(a * Math.PI / 180.0), CArmLen + (CRad + hw) * Math.Sin(a * Math.PI / 180.0)));
            ring.Add(new double2(-(CRad + hw), CArmLen));
            ring.Add(new double2(-(CRad + hw), 0));
            ring.Add(new double2(-(CRad - hw), 0));
            ring.Add(new double2(-(CRad - hw), CArmLen));
            for (int a = 175; a > 0; a -= 5)
                ring.Add(new double2((CRad - hw) * Math.Cos(a * Math.PI / 180.0), CArmLen + (CRad - hw) * Math.Sin(a * Math.PI / 180.0)));
            ring.Add(new double2(CRad - hw, CArmLen));
            ring.Add(new double2(CRad - hw, 0));
            return new Scn("C-run 180° around a hill, 40% first 120 m", "C 150 m arms", ring.ToArray(), (s, t) =>
            {
                double2 ud = CRunUd(s, t);
                double along = ud.x < 120.0 ? 0.40 * ud.x : 48.0 + 0.12 * (ud.x - 120.0);
                return 1000.0 - along - 0.10 * ud.y;
            });
        }

        /// <summary>Known and deliberate (§9.8): a switchback with a CONSTANT fall line (+s): three
        /// 300 m traverses 40 m wide at s 0-40, 100-140, 200-240, joined by 40 m-wide turns at the
        /// +t end (s 0-140) and the -t end (s 100-240); 20 % above s = 150, 35 % below.</summary>
        private static Scn Switchback()
        {
            double2[] z =
            {
                new double2(0, 0), new double2(0, 300), new double2(140, 300), new double2(140, 40), new double2(200, 40), new double2(200, 300),
                new double2(240, 300), new double2(240, 0), new double2(100, 0), new double2(100, 260), new double2(40, 260), new double2(40, 0),
            };
            return new Scn("switchback, constant fall line, 20% / 35% below s150", "240x300 Z", z,
                (s, t) => s < 150.0 ? 1000.0 - 0.20 * s : 1000.0 - 30.0 - 0.35 * (s - 150.0));
        }

        private enum ZExp
        {
            Green,
            Black,
            DoubleBlack,
            HeightFallback,
        }

        private struct ZoneCase
        {
            public Scn Sc;
            public ZExp Want;
            public double Target, Tol, Lo, Hi;   // Target ± Tol when Tol > 0; [Lo, Hi] when Hi > 0
            // R2b fixer round (review finding code-1): the fixture lies BELOW the |mean g| gate and
            // must take the fall-line key through the coherence clause (asserted premise, plus
            // R2b-as-first-built — the |mean g| gate alone — must have taken the height key).
            public bool BelowGate;

            public ZoneCase(Scn sc, ZExp want, double target, double tol, double lo, double hi)
                : this(sc, want, target, tol, lo, hi, false)
            {
            }

            public ZoneCase(Scn sc, ZExp want, double target, double tol, double lo, double hi, bool belowGate)
            {
                Sc = sc;
                Want = want;
                Target = target;
                Tol = tol;
                Lo = lo;
                Hi = hi;
                BelowGate = belowGate;
            }
        }

        // ---- R2b fixer round fixtures (review findings code-1, rule-and-tests-zone-depth-uses-mean-
        // width, rule-and-tests-fall-key-zone-past-crest, rule-and-tests-rotated-poke-not-exercised).

        /// <summary>§9.8's 10 × 2000 trail with a 60 m 50 % wall at s 100-160, on gentle ground g0
        /// (|mean g| 0.015-0.025 < 3 %: below the |mean g| gate; R2b as first built fell back to
        /// the height key and R2's area cap, which skips ~200 m at each end of a 10 m trail).</summary>
        private static Scn Trail10WallOn(double g0)
        {
            return new Scn("trail10 50% wall s100-160 on " + F(g0 * 100) + "% ground", "2000x10", LRect(2000, 10), PitchProfile(g0, 100, 60, 0.50));
        }

        /// <summary>A 20 × 1200 trail on 1.5 % ground with a full-width 40 m 45 % wall at s 60-100
        /// (reviewer: 1,100 m long read Black, 1,200 m Green..Blue).</summary>
        private static Scn Trail20WallGentle()
        {
            return new Scn("trail20 45% wall s60-100 on 1.5% ground", "1200x20", LRect(1200, 20), PitchProfile(0.015, 60, 40, 0.45));
        }

        /// <summary>The 30 m 45 % drop-in at the top of a 60 m-wide run on gentle ground g0.</summary>
        private static Scn DropInGentle(double g0, double len)
        {
            return new Scn("drop-in 30 m @45% top of " + F(g0 * 100) + "% w60 L" + F(len), F(len) + "x60", LRect(len, 60), PitchProfile(g0, 0, 30, 0.45));
        }

        /// <summary>A run whose width changes once: wTop for s in [0, split], wBot below.</summary>
        private static double2[] Step2(double len, double split, double wTop, double wBot)
        {
            return new[]
            {
                new double2(0, -wTop / 2), new double2(split, -wTop / 2), new double2(split, -wBot / 2), new double2(len, -wBot / 2),
                new double2(len, wBot / 2), new double2(split, wBot / 2), new double2(split, wTop / 2), new double2(0, wTop / 2),
            };
        }

        /// <summary>A 600 m run at 15 % whose top 200 m is a 20 m-wide trail opening into a 150 m
        /// bowl (mean width 106.7 m), with a full-width 45 % pitch at s [s0, s0 + len].</summary>
        private static Scn TrailTopPitch(double s0, double len)
        {
            return new Scn("trail top 20 m -> 150 m bowl, 45% pitch s" + F(s0) + "-" + F(s0 + len), "600 20/150", Step2(600, 200, 20, 150),
                PitchProfile(0.15, s0, len, 0.45));
        }

        /// <summary>A 150 m-wide 600 m slope at 15 % narrowing into a 20 m cat track for its last
        /// 150 m, with a full-width 45 % pitch whose foot is `above` m above the bottom.</summary>
        private static Scn CatTrackPitch(double above, double len)
        {
            return new Scn("cat track: 150 -> 20 m, 45% pitch " + F(above) + "-" + F(above + len) + " m above bottom", "600 150/20",
                Step2(600, 450, 150, 20), PitchProfile(0.15, 600 - above - len, len, 0.45));
        }

        /// <summary>A polygon drawn d m past a crest onto a back slope gb at its top end; 400 m at
        /// 20 % below the crest; 60 m wide.</summary>
        private static Scn PastCrest(double d, double gb)
        {
            return new Scn("top drawn " + F(d) + " m past a crest onto a " + F(gb * 100) + "% back slope", F(400 + d) + "x60", LRect(400 + d, 60),
                (s, t) => s < d ? 1000.0 - gb * (d - s) : 1000.0 - 0.20 * (s - d));
        }

        /// <summary>A polygon drawn d m past a valley floor up a counter-slope gc at its bottom end;
        /// 400 m at 20 % above the floor; 60 m wide.</summary>
        private static Scn PastValley(double d, double gc)
        {
            return new Scn("bottom drawn " + F(d) + " m past a valley floor up a " + F(gc * 100) + "% counter-slope", F(400 + d) + "x60", LRect(400 + d, 60),
                (s, t) => s < 400 ? 1000.0 - 0.20 * s : 920.0 + gc * (s - 400));
        }

        private static double2 RotAbout(double2 p, double2 c, double deg)
        {
            double a = deg * Math.PI / 180.0, ca = Math.Cos(a), sa = Math.Sin(a);
            double2 d = p - c;
            return c + new double2(d.x * ca - d.y * sa, d.x * sa + d.y * ca);
        }

        /// <summary>The F116 meadow (16 %, 2 m wave) with the TERRAIN fixed in the run frame and a
        /// 48 % half-plane bank whose edge joins the legs legS (along the polygon side from corner
        /// ci toward vertex sideIdx) and legT (along its top edge toward topIdx) — the poke follows
        /// the polygon. Used with the polygon rotated about the poke corner or its top edge
        /// slanted, i.e. a top edge NOT along the contours (EndZoneTests' other fixtures rotate
        /// polygon and terrain together).</summary>
        private static Scn PokeOnRing(string name, string size, double2[] ring, int ci, int sideIdx, int topIdx, double legS, double legT)
        {
            double g0 = F116Grade, gb = F116BankGrade, wave = F116Wave;
            double2 c = ring[ci];
            double2 us = math.normalize(ring[sideIdx] - c), ut = math.normalize(ring[topIdx] - c);
            double2 a = c + legS * us, b = c + legT * ut;
            double2 e = b - a;
            double el = math.length(e);
            double2 n = new double2(-e.y / el, e.x / el);
            if (math.dot(c - a, n) < 0)
                n = -n;
            double k = BankK(g0, n.x, gb);
            return new Scn(name, size, ring, (s, t) => 1000.0 - g0 * s + wave * Math.Sin(2 * Math.PI * s / 140.0 + 0.3) * Math.Cos(2 * Math.PI * t / 110.0)
                                                      + k * Math.Max(0.0, (s - a.x) * n.x + (t - a.y) * n.y));
        }

        /// <summary>The 545 × 125 F116 meadow polygon rotated by deg about its +t top corner (the
        /// poke corner; deg > 0 makes it the highest point), poke legS × legT.</summary>
        private static Scn PokeRotated(double legS, double legT, double deg)
        {
            double2 c = new double2(0, 62.5);
            double2[] ring = { new double2(0, -62.5), new double2(545, -62.5), new double2(545, 62.5), new double2(0, 62.5) };
            for (int i = 0; i < 4; i++)
                ring[i] = RotAbout(ring[i], c, deg);
            return PokeOnRing("F116 poke " + F(legS) + "x" + F(legT) + ", polygon rotated " + F(deg) + "° about the corner", "545x125 rot", ring, 3, 2, 0, legS, legT);
        }

        /// <summary>The 545 × 125 F116 meadow with its sides on the fall line and its top edge
        /// slanted: the -t top corner at s = slant (the +t poke corner at s = 0 is the highest
        /// point for slant > 0).</summary>
        private static Scn PokeSlanted(double legS, double legT, double slant)
        {
            double2[] ring = { new double2(slant, -62.5), new double2(545, -62.5), new double2(545, 62.5), new double2(0, 62.5) };
            return PokeOnRing("F116 poke " + F(legS) + "x" + F(legT) + ", top edge slanted " + F(slant) + " m", "545x125 slant", ring, 3, 2, 0, legS, legT);
        }

        private static readonly double[] ZoneAngs = { 0, 17, 45, 73 };

        /// <summary>R2b (§9.8, "§9.4 changes"): the asserted end-zone fixtures, m = 1, 8 frames
        /// (rotations 0/17/45/73° × the two ScnStarts origins). Expectations from §9.8; any that
        /// cannot be met is a WARN SPEC-GAP with the measured range and a regression envelope.
        /// Then the report rows (side cliff, one-sided valley, the trail controls) and the
        /// "known and deliberate" curved runs under both keys.</summary>
        private static void EndZoneTests()
        {
            List<ZoneCase> cases = new List<ZoneCase>
            {
                new ZoneCase(Valley(), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(SteepValley(0.40), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(SteepValley(0.50), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(OpenBowl(), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(Ridge(300, 60), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(Ridge(600, 120), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(Ridge(1000, 80), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(Ridge(2000, 80), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(SideBank(), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(CornerPoke(60, 80), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(CornerPoke(80, 100), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(CornerPoke(120, 100), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(Sliver71(), ZExp.Green, 0, 0, 0, 0),
                new ZoneCase(TopFace71(), ZExp.DoubleBlack, 0.71, 0.015, 0, 0),
                new ZoneCase(MountainFace68(), ZExp.DoubleBlack, 0, 0, 0, 0),
                new ZoneCase(Trail20Wall(60, "s60-100"), ZExp.Black, 0.45, 0.015, 0, 0),
                new ZoneCase(Trail20Wall(1400, "60-100 m above the bottom"), ZExp.Black, 0.45, 0.015, 0, 0),
                new ZoneCase(Trail10Wall(), ZExp.Black, 0.50, 0.015, 0, 0),
                new ZoneCase(DropIn(20), ZExp.Black, 0, 0, 0.43, 0.455),
                new ZoneCase(DropIn(60), ZExp.Black, 0, 0, 0.43, 0.455),
                new ZoneCase(DropIn(125), ZExp.Black, 0, 0, 0.43, 0.455),
                new ZoneCase(DropIn(200), ZExp.Black, 0, 0, 0.43, 0.455),
                new ZoneCase(WideShort(), ZExp.Black, 0.45, 0.015, 0, 0),
                new ZoneCase(LRun(true), ZExp.Black, 0, 0, 0, 0),
                new ZoneCase(Cone(), ZExp.HeightFallback, 0, 0, 0, 0),
                new ZoneCase(Crater(), ZExp.HeightFallback, 0, 0, 0, 0),
                new ZoneCase(Saddle(), ZExp.HeightFallback, 0, 0, 0, 0),
                // R2b fixer round (review finding code-1): §9.8's trail fixtures and the drop-in on
                // GENTLE ground, below the |mean g| gate. R2b as first built (|mean g| gate only)
                // fell back to the height key and R2's area cap there and skipped the pitch (trail10
                // on 1 % ground 0.95-1.16 % Flat); the coherence clause keeps the fall-line key.
                new ZoneCase(Trail10WallOn(0.01), ZExp.Black, 0.50, 0.015, 0, 0, true),
                new ZoneCase(Trail10WallOn(0.0), ZExp.Black, 0.50, 0.015, 0, 0, true),
                new ZoneCase(Trail20WallGentle(), ZExp.Black, 0.45, 0.015, 0, 0, true),
                new ZoneCase(DropInGentle(0.01, 700), ZExp.Black, 0, 0, 0, 0, true),
                new ZoneCase(DropInGentle(0.02, 1500), ZExp.Black, 0, 0, 0, 0, true),
            };
            Console.WriteLine("INFO  R2b end zones (§9.8; asserted, m = 1, " + ScnStarts.Length * ZoneAngs.Length +
                              " frames = rotations 0/17/45/73° × 2 origins; key F/H = frames on the fall-line / height key; |g| = mean box gradient; " +
                              "coh = FallCoherence; 'below the gate' rows: asserted |g| < " + F(SkiSlopeClassifier.EndZoneMinMeanGrade) +
                              ", fall-line key by the coherence clause, no 'avoidable' tooltip line, and the |mean g| gate alone takes the height key):");
            foreach (ZoneCase zc in cases)
            {
                Scn sc = zc.Sc;
                int op = ScnStep(sc);
                string w0 = "end zones/" + sc.Name;
                Check(op == 1, w0, "fixture operates at m=" + op + ", want 1 (§9.8: m = 1)");
                double lo = 1e9, hi = -1e9, gLo = 1e9, gHi = -1e9, r2Lo = 1e9, r2Hi = -1e9, cLo = 1e9, cHi = -1e9, oLo = 1e9, oHi = -1e9;
                int fall = 0, height = 0, tzLo = int.MaxValue, tzHi = 0, bzLo = int.MaxValue, bzHi = 0, bad = 0;
                SlopeBand bLo = SlopeBand.DoubleBlack, bHi = SlopeBand.Flat;
                foreach (double2 st0 in ScnStarts)
                {
                    foreach (double ang in ZoneAngs)
                    {
                        Frame fr = Frame.Make(st0.x, st0.y, ang);
                        string w = w0 + " a" + ang + " at " + F(st0.x);
                        SlopeStatus st = RunScn(sc, fr, op, s_Grid, s_Result, w);
                        SlopeResult r = s_Result;
                        float rating = r.RatingGrade;
                        SlopeBand band = r.Band;
                        bool byFall = r.ZoneByFallLine;
                        lo = Math.Min(lo, rating);
                        hi = Math.Max(hi, rating);
                        gLo = Math.Min(gLo, r.MeanGradient);
                        gHi = Math.Max(gHi, r.MeanGradient);
                        cLo = Math.Min(cLo, r.FallCoherence);
                        cHi = Math.Max(cHi, r.FallCoherence);
                        tzLo = Math.Min(tzLo, r.TopZoneCells);
                        tzHi = Math.Max(tzHi, r.TopZoneCells);
                        bzLo = Math.Min(bzLo, r.BottomZoneCells);
                        bzHi = Math.Max(bzHi, r.BottomZoneCells);
                        if (band < bLo) bLo = band;
                        if (band > bHi) bHi = band;
                        if (byFall) fall++;
                        else height++;
                        Check(st == SlopeStatus.Ok && r.Connected, w, "status " + st + ", connected " + r.Connected);
                        if (zc.BelowGate)
                        {
                            bool shown = SkiSlopeClassifier.SteepestPatchShown(rating, r.SteepestGrade, band, r.Connected);
                            Check(r.MeanGradient < SkiSlopeClassifier.EndZoneMinMeanGrade && byFall && r.FallCoherence >= SkiSlopeClassifier.EndZoneMinCoherence && !shown,
                                w, "premise: |mean g| " + F(r.MeanGradient) + " (want < gate), key " + (byFall ? "fall line" : "height") + " (want fall line), coherence " +
                                   F(r.FallCoherence) + " (want >= " + F(SkiSlopeClassifier.EndZoneMinCoherence) + "), 'avoidable' line " + shown + " (want false)");
                            // The same grid under R2b as first built (the |mean g| gate alone).
                            SlopeResult ro = new SlopeResult();
                            Classify(s_Grid, ro, Zp.Shipped.MeanGateOnly());
                            oLo = Math.Min(oLo, ro.RatingGrade);
                            oHi = Math.Max(oHi, ro.RatingGrade);
                            Check(!ro.ZoneByFallLine, w, "premise: the |mean g| gate alone must take the height key here");
                        }
                        bool ok;
                        switch (zc.Want)
                        {
                            case ZExp.Green:
                                ok = band == SlopeBand.Green;
                                break;
                            case ZExp.Black:
                                ok = band == SlopeBand.Black;
                                break;
                            case ZExp.DoubleBlack:
                                ok = band == SlopeBand.DoubleBlack;
                                break;
                            default:
                            {
                                // The height-key fallback must be taken, and the rating must be R2's
                                // (the same grid classified with the height key forced: +inf gate).
                                SlopeResult rh = new SlopeResult();
                                Classify(s_Grid, rh, Zp.Shipped.HeightKey());
                                r2Lo = Math.Min(r2Lo, rh.RatingGrade);
                                r2Hi = Math.Max(r2Hi, rh.RatingGrade);
                                ok = !byFall && rh.RatingGrade == rating && rh.TopZoneCells == r.TopZoneCells && rh.BottomZoneCells == r.BottomZoneCells &&
                                     rh.CruxI == r.CruxI && rh.CruxJ == r.CruxJ;
                                break;
                            }
                        }
                        if (zc.Tol > 0)
                            ok &= Math.Abs(rating - zc.Target) <= zc.Tol;
                        if (zc.Hi > 0)
                            ok &= rating >= zc.Lo && rating <= zc.Hi;
                        if (!ok)
                            bad++;
                        Check(ok, w, "rating " + P(rating) + " " + band + " (key " + (byFall ? "fall line" : "height") + ", |mean g| " + F(r.MeanGradient) +
                                     "), want " + ZoneWant(zc));
                    }
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO    {0,-56} {1,-10} want {2,-26} rating [{3}, {4}] {5}..{6} key F{7}/H{8} |g| {9:F3}-{10:F3} coh {16:F3}-{17:F3} zones t {11}-{12} b {13}-{14}{15}",
                    sc.Name, sc.Size, ZoneWant(zc), P(lo), P(hi), Ab(bLo), Ab(bHi), fall, height, gLo, gHi, tzLo, tzHi, bzLo, bzHi,
                    zc.Want == ZExp.HeightFallback ? "; height-key (R2) rating [" + P(r2Lo) + ", " + P(r2Hi) + "]" :
                    zc.BelowGate ? "; below the gate: R2b as first built (|mean g| gate alone, height key) [" + P(oLo) + ", " + P(oHi) + "]" : "",
                    cLo, cHi));
            }

            // Report rows (not asserted).
            Console.WriteLine("INFO  R2b end zones, report rows (not asserted; same 8 frames):");
            foreach (Scn sc in new[] { SideCliff(), OneSidedValley(), Trail20Wall(0, "s0-40"), Trail20Wall(200, "s200-240 (control)"), LRun(false) })
                ZoneReportRow(sc, false);
            // Known and deliberate after R2b (§9.8): curved runs, each under the fall-line key
            // (shipped) and, via the test-only gate (+inf), under R2's height key.
            Console.WriteLine("INFO  R2b known and deliberate (§9.8, report only): curved runs under the fall-line key (shipped) and the height key (test-only gate +inf):");
            foreach (Scn sc in new[] { CRunHill(), Switchback() })
                ZoneReportRow(sc, true);

            KeyGateReport();       // R2b fixer round, review finding code-1
            ZoneDepthTests();      // R2b fixer round, review finding rule-and-tests-zone-depth-uses-mean-width
            OvershootReport();     // R2b fixer round, review finding rule-and-tests-fall-key-zone-past-crest
            RotatedPokeTests();    // R2b fixer round, review finding rule-and-tests-rotated-poke-not-exercised
        }

        private static string ZoneWant(ZoneCase zc)
        {
            switch (zc.Want)
            {
                case ZExp.HeightFallback:
                    return "height key, rating = R2";
                default:
                    string b = zc.Want == ZExp.DoubleBlack ? "DblBlk" : zc.Want.ToString();
                    if (zc.Tol > 0)
                        return b + " " + P(zc.Target) + " ± " + P(zc.Tol);
                    if (zc.Hi > 0)
                        return b + " " + P(zc.Lo) + "-" + P(zc.Hi);
                    return b;
            }
        }

        /// <summary>One report row over the 8 frames: rating range, band range, key used, zone
        /// sizes, crux position (s, t; for the C-run also its angle along the run); with
        /// `bothKeys`, a second row under the height key.</summary>
        private static void ZoneReportRow(Scn sc, bool bothKeys)
        {
            if (bothKeys)
                ZoneReportRow(sc, new[] { Zp.Shipped, Zp.Shipped.HeightKey() }, new[] { "(shipped)", "(height key)" });
            else
                ZoneReportRow(sc, new[] { Zp.Shipped }, new[] { "(shipped)" });
        }

        /// <summary>The report row under each of the given end-zone parameter sets (tags printed).
        /// Returns the shipped (first) pass's rating range and band range.</summary>
        private static ZoneRange ZoneReportRow(Scn sc, Zp[] passes, string[] tags)
        {
            int op = ScnStep(sc);
            bool isC = sc.Name.StartsWith("C-run", StringComparison.Ordinal);
            ZoneRange first = new ZoneRange();
            for (int pass = 0; pass < passes.Length; pass++)
            {
                Zp zp = passes[pass];
                double lo = 1e9, hi = -1e9, gLo = 1e9, gHi = -1e9, cLo = 1e9, cHi = -1e9, kLo = 1e9, kHi = -1e9;
                int fall = 0, height = 0, tzLo = int.MaxValue, tzHi = 0;
                SlopeBand bLo = SlopeBand.DoubleBlack, bHi = SlopeBand.Flat;
                foreach (double2 st0 in ScnStarts)
                {
                    foreach (double ang in ZoneAngs)
                    {
                        Frame fr = Frame.Make(st0.x, st0.y, ang);
                        RunScnWith(sc, fr, op, s_Grid, s_Result, zp, "report " + sc.Name + " " + tags[pass] + " a" + ang + " at " + F(st0.x));
                        SlopeResult r = s_Result;
                        lo = Math.Min(lo, r.RatingGrade);
                        hi = Math.Max(hi, r.RatingGrade);
                        gLo = Math.Min(gLo, r.MeanGradient);
                        gHi = Math.Max(gHi, r.MeanGradient);
                        kLo = Math.Min(kLo, r.FallCoherence);
                        kHi = Math.Max(kHi, r.FallCoherence);
                        tzLo = Math.Min(tzLo, r.TopZoneCells);
                        tzHi = Math.Max(tzHi, r.TopZoneCells);
                        if (r.Band < bLo) bLo = r.Band;
                        if (r.Band > bHi) bHi = r.Band;
                        if (r.ZoneByFallLine) fall++;
                        else height++;
                        if (r.CruxValid)
                        {
                            double2 cw = CruxWorld(r);
                            double cs = fr.Along(cw.x, cw.y), ct = fr.Across(cw.x, cw.y);
                            double c = isC ? CRunUd(cs, ct).x : cs;
                            cLo = Math.Min(cLo, c);
                            cHi = Math.Max(cHi, c);
                        }
                    }
                }
                if (pass == 0)
                {
                    first.Lo = lo;
                    first.Hi = hi;
                    first.BLo = bLo;
                    first.BHi = bHi;
                    first.Fall = fall;
                    first.Height = height;
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO    {0,-56} {1,-10} {2,-11} rating [{3}, {4}] {5}..{6} key F{7}/H{8} |g| {9:F3}-{10:F3} coh {16:F3}-{17:F3} top zone {11}-{12} cells; crux {13} {14:F0}-{15:F0}",
                    sc.Name, sc.Size, tags[pass], P(lo), P(hi), Ab(bLo), Ab(bHi), fall, height, gLo, gHi, tzLo, tzHi,
                    isC ? "at run position u m (of " + F(Math.Round(2 * CArmLen + Math.PI * CRad)) + ")" : "at s m", cLo, cHi, kLo, kHi));
            }
            return first;
        }

        private struct ZoneRange
        {
            public double Lo, Hi;
            public SlopeBand BLo, BHi;
            public int Fall, Height;
        }

        /// <summary>R2b fixer round, review finding code-1 (report rows; the asserted fixtures are
        /// the 'below the gate' rows of EndZoneTests). The |mean g| gate alone sent a long gentle
        /// run whose gradients all agree (coherence ~1) to the height key and R2's area cap, so a
        /// full-width pitch within EndZoneMaxM2 / width of an end was skipped, and dragging the end
        /// node across the gate flipped the band. Each row: shipped (with the coherence clause)
        /// and R2b as first built (|mean g| gate alone). Then the coherence survey: terrain that
        /// must keep the height fallback, and where the new clause's own threshold is crossed.</summary>
        private static void KeyGateReport()
        {
            Zp[] passes = { Zp.Shipped, Zp.Shipped.MeanGateOnly() };
            string[] tags = { "(shipped)", "(|g| gate)" };
            Console.WriteLine("INFO  R2b fixer round, key gate (review finding code-1; report rows, same 8 frames; '(|g| gate)' = R2b as first built, " +
                              "the |mean g| >= " + F(SkiSlopeClassifier.EndZoneMinMeanGrade) + " gate alone):");
            foreach (double len in new double[] { 500, 600, 650, 1000 })
                ZoneReportRow(DropInGentle(0.01, len), passes, tags);
            ZoneReportRow(DropInGentle(0.02, 3000), passes, tags);
            ZoneReportRow(DropInGentle(0.025, 3000), passes, tags);
            ZoneReportRow(new Scn("trail20 45% wall s60-100 on 1.5% ground", "1100x20", LRect(1100, 20), PitchProfile(0.015, 60, 40, 0.45)), passes, tags);
            ZoneReportRow(new Scn("60x1000 on 1% ground, 45% wall s500-530 (mid-run control)", "1000x60", LRect(1000, 60), PitchProfile(0.01, 500, 30, 0.45)), passes, tags);
            Console.WriteLine("INFO  R2b fixer round, key gate: coherence survey (shipped; the height fallback must stay on cones / craters / saddles and on " +
                              "noise; where the coherence clause's own threshold " + F(SkiSlopeClassifier.EndZoneMinCoherence) + " is crossed):");
            Zp[] ship = { Zp.Shipped };
            string[] shipTag = { "(shipped)" };
            ZoneRange noise = ZoneReportRow(new Scn("flat 0% + 0.3 m noise at 5 m", "400x100", LRect(400, 100),
                (s, t) => 1000.0 + 0.3 * Math.Sin(2 * Math.PI * s / 5.0) * Math.Cos(2 * Math.PI * t / 5.0)), ship, shipTag);
            ZoneRange bumps = ZoneReportRow(new Scn("flat 0% + 0.5 m bumps at 30 m", "400x200", LRect(400, 200),
                (s, t) => 1000.0 + 0.5 * Math.Sin(2 * Math.PI * s / 30.0) * Math.Sin(2 * Math.PI * t / 30.0)), ship, shipTag);
            Check(noise.Fall == 0 && bumps.Fall == 0, "key gate/coherence survey", "noise on flat ground must keep the height fallback (frames on the fall-line key: " +
                                                                               noise.Fall + " / " + bumps.Fall + ")");
            foreach (double len in new double[] { 700, 1500 })
            {
                LocalHeight p = PitchProfile(0.01, 0, 30, 0.45);
                ZoneReportRow(new Scn("drop-in 30 m @45% top of 1% w60 + 1 m wave (140x110 m) L" + F(len), F(len) + "x60", LRect(len, 60),
                    (s, t) => p(s, t) + 1.0 * Math.Sin(2 * Math.PI * s / 140.0 + 0.3) * Math.Cos(2 * Math.PI * t / 110.0)), passes, tags);
            }
            {
                LocalHeight p = PitchProfile(0.0, 100, 60, 0.50);
                ZoneReportRow(new Scn("trail10 50% wall s100-160 on 0% + 1 m noise at 5 m", "2000x10", LRect(2000, 10),
                    (s, t) => p(s, t) + 1.0 * Math.Sin(2 * Math.PI * s / 5.0) * Math.Cos(2 * Math.PI * t / 5.0)), passes, tags);
            }
        }

        /// <summary>Top-zone depth (max s of a top-zone node above the run's top, run-local) and
        /// bottom-zone depth (bottom minus the min s of a bottom-zone node) from the oracle's
        /// zones, which OracleCheck asserts equal to the classifier's (counts) on this same grid.</summary>
        private static void ZoneDepths(Scn sc, Frame fr, SlopeGrid g, SlopeResult r, out double topDepth, out double botDepth)
        {
            double sMin = 1e9, sMax = -1e9;
            foreach (double2 v in sc.LocalRing)
            {
                sMin = Math.Min(sMin, v.x);
                sMax = Math.Max(sMax, v.x);
            }
            OZones oz = OracleZones(g, r, Zp.Shipped);
            topDepth = 0;
            botDepth = 0;
            foreach (int q in oz.Cells)
            {
                double2 p = NodeWorld(g, q % g.Nx, q / g.Nx);
                double s = fr.Along(p.x, p.y);
                if (oz.InTop(q))
                    topDepth = Math.Max(topDepth, s - sMin);
                if (oz.InBot(q))
                    botDepth = Math.Max(botDepth, sMax - s);
            }
        }

        /// <summary>R2b fixer round, review finding rule-and-tests-zone-depth-uses-mean-width.
        /// §9.8 sizes each fall-line zone by a cell COUNT, z = floor(n·EndZoneDepthM / L): that is
        /// EndZoneDepthM of run at the run's MEAN width n·step² / L, so at an end narrower than the
        /// mean the zone runs EndZoneDepthM × mean / end width down the fall line, and a full-width
        /// pitch inside it is skipped (R2's limit (b) again, by shape instead of by width).
        /// Constant-width controls: asserted Black 45 ± 1.5 % with a zone depth <= EndZoneDepthM +
        /// one step (the §9.8 claim holds at uniform width). Narrowing ends: one WARN SPEC-GAP per
        /// end against §9.8's "at most ~20 m of run whatever the width", each row's zone depth kept
        /// inside a regression envelope (s_GapEnvelope "zone depth ...").</summary>
        private static void ZoneDepthTests()
        {
            Console.WriteLine("INFO  R2b fixer round, zone depth vs run width (review finding rule-and-tests-zone-depth-uses-mean-width; same 8 frames; depth = how far " +
                              "the zone reaches down (up) the run from its top (bottom), oracle zones):");
            var rows = new List<KeyValuePair<Scn, int>>   // 0 = constant-width control, 1 = narrow top end, 2 = narrow bottom end
            {
                new KeyValuePair<Scn, int>(new Scn("const 20x600, 45% pitch s20-80 (control)", "600x20", LRect(600, 20), PitchProfile(0.15, 20, 60, 0.45)), 0),
                new KeyValuePair<Scn, int>(new Scn("const 20x600, 45% pitch 30-70 m above bottom (control)", "600x20", LRect(600, 20), PitchProfile(0.15, 530, 40, 0.45)), 0),
                new KeyValuePair<Scn, int>(new Scn("const 50x600, 45% wall s0-40 (control)", "600x50", LRect(600, 50), PitchProfile(0.15, 0, 40, 0.45)), 0),
                new KeyValuePair<Scn, int>(new Scn("const 150x600, 45% wall s0-40 (control)", "600x150", LRect(600, 150), PitchProfile(0.15, 0, 40, 0.45)), 0),
                new KeyValuePair<Scn, int>(TrailTopPitch(20, 60), 1),
                new KeyValuePair<Scn, int>(TrailTopPitch(40, 40), 1),
                new KeyValuePair<Scn, int>(new Scn("top 200 m 50 wide -> 150, 45% wall s0-40", "600 50/150", Step2(600, 200, 50, 150), PitchProfile(0.15, 0, 40, 0.45)), 1),
                new KeyValuePair<Scn, int>(CatTrackPitch(30, 40), 2),
                new KeyValuePair<Scn, int>(CatTrackPitch(40, 60), 2),
                new KeyValuePair<Scn, int>(new Scn("150 wide -> last 200 m 50, 45% wall 0-40 m above bottom", "600 150/50", Step2(600, 400, 150, 50), PitchProfile(0.15, 560, 40, 0.45)), 2),
            };
            double[] worst = { 0, 0, 0 };
            string[] worstRow = { "", "", "" };
            foreach (KeyValuePair<Scn, int> kv in rows)
            {
                Scn sc = kv.Key;
                int kind = kv.Value, op = ScnStep(sc);
                string w0 = "zone depth/" + sc.Name;
                Check(op == 1, w0, "fixture operates at m=" + op + ", want 1");
                double lo = 1e9, hi = -1e9, dLo = 1e9, dHi = -1e9;
                SlopeBand bLo = SlopeBand.DoubleBlack, bHi = SlopeBand.Flat;
                string envKey = "zone depth " + sc.Name;
                double env = kind == 0 ? SkiSlopeClassifier.EndZoneDepthM + Step(1) : s_GapEnvelope.ContainsKey(envKey) ? s_GapEnvelope[envKey] : 0.0;
                foreach (double2 st0 in ScnStarts)
                {
                    foreach (double ang in ZoneAngs)
                    {
                        Frame fr = Frame.Make(st0.x, st0.y, ang);
                        string w = w0 + " a" + ang + " at " + F(st0.x);
                        SlopeStatus st = RunScn(sc, fr, op, s_Grid, s_Result, w);
                        SlopeResult r = s_Result;
                        ZoneDepths(sc, fr, s_Grid, r, out double topD, out double botD);
                        // A control's pitch sits at the top or the bottom: measure that end.
                        bool bottomEnd = kind == 2 || (kind == 0 && sc.Name.Contains("above bottom"));
                        double depth = bottomEnd ? botD : topD;
                        lo = Math.Min(lo, r.RatingGrade);
                        hi = Math.Max(hi, r.RatingGrade);
                        dLo = Math.Min(dLo, depth);
                        dHi = Math.Max(dHi, depth);
                        if (r.Band < bLo) bLo = r.Band;
                        if (r.Band > bHi) bHi = r.Band;
                        Check(st == SlopeStatus.Ok && r.Connected && r.ZoneByFallLine, w, "status " + st + ", connected " + r.Connected + ", fall-line key " + r.ZoneByFallLine);
                        if (kind == 0)
                            Check(Math.Abs(r.RatingGrade - 0.45) <= 0.015 && r.Band == SlopeBand.Black && depth <= env, w,
                                "control: rating " + P(r.RatingGrade) + " " + r.Band + " (want Black 45 ± 1.5 %), zone depth " + F(Math.Round(depth, 1)) +
                                " m (want <= " + F(env) + " m)");
                        else
                            Check(depth <= env, w, "zone depth " + F(Math.Round(depth, 1)) + " m above the regression envelope " + F(env) + " m");
                    }
                }
                if (kind != 0 && dHi > worst[kind])
                {
                    worst[kind] = dHi;
                    worstRow[kind] = sc.Name + " (" + sc.Size + "): zone depth " + F(Math.Round(dLo, 1)) + "-" + F(Math.Round(dHi, 1)) + " m, rating [" + P(lo) + ", " +
                                     P(hi) + "] " + Ab(bLo) + ".." + Ab(bHi);
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO    {0,-62} {1,-13} {2,-9} rating [{3}, {4}] {5}..{6} zone depth {7:F1}-{8:F1} m ({9} {10:F1} m)",
                    sc.Name, sc.Size, kind == 0 ? "control" : kind == 1 ? "top end" : "bottom end", P(lo), P(hi), Ab(bLo), Ab(bHi), dLo, dHi,
                    kind == 0 ? "asserted <=" : "envelope", env));
            }
            for (int kind = 1; kind <= 2; kind++)
                SpecGap("end-zone depth at a narrow " + (kind == 1 ? "TOP" : "BOTTOM") + " end m=1",
                        "§9.8 'each zone is at most ~20 m of run along the fall line, whatever the width'",
                        worstRow[kind] + " (constant-width controls: depth <= " + F(SkiSlopeClassifier.EndZoneDepthM + Step(1)) +
                        " m, Black 45 ± 1.5 %); regression envelopes on each row's zone depth");
        }

        /// <summary>R2b fixer round, review finding rule-and-tests-fall-key-zone-past-crest (report;
        /// a known limit of the fall-line key): the key is position along the mean fall line and
        /// never reads height, so a polygon drawn more than ~EndZoneDepthM past a crest (past a
        /// valley floor) puts its top (bottom) zone on the back slope (counter-slope), and every
        /// line climbs it. Each row under the shipped key and R2's height key. Regression guard,
        /// asserted: an overshoot of EndZoneDepthM (20 m) still reads Green.</summary>
        private static void OvershootReport()
        {
            Zp[] passes = { Zp.Shipped, Zp.Shipped.HeightKey() };
            string[] tags = { "(shipped)", "(height key)" };
            Console.WriteLine("INFO  R2b fixer round, polygon drawn past a crest / valley floor (review finding rule-and-tests-fall-key-zone-past-crest; report, " +
                              "same 8 frames; 400 m at 20 % below the crest / above the floor, 60 m wide):");
            foreach (double2 c in new[] { new double2(20, 0.30), new double2(30, 0.30), new double2(40, 0.30), new double2(60, 0.30),
                                           new double2(20, 0.45), new double2(30, 0.45), new double2(40, 0.45), new double2(60, 0.45), new double2(30, 0.60) })
            {
                ZoneRange zr = ZoneReportRow(PastCrest(c.x, c.y), passes, tags);
                if (c.x <= SkiSlopeClassifier.EndZoneDepthM)
                    Check(zr.BLo == SlopeBand.Green && zr.BHi == SlopeBand.Green, "overshoot/crest d" + F(c.x), "an overshoot of <= EndZoneDepthM must still read Green, got " +
                                                                                                      Ab(zr.BLo) + ".." + Ab(zr.BHi));
            }
            foreach (double2 c in new[] { new double2(20, 0.30), new double2(30, 0.30), new double2(40, 0.30), new double2(60, 0.30), new double2(40, 0.45) })
            {
                ZoneRange zr = ZoneReportRow(PastValley(c.x, c.y), passes, tags);
                if (c.x <= SkiSlopeClassifier.EndZoneDepthM)
                    Check(zr.BLo == SlopeBand.Green && zr.BHi == SlopeBand.Green, "overshoot/valley d" + F(c.x), "an overshoot of <= EndZoneDepthM must still read Green, got " +
                                                                                                       Ab(zr.BLo) + ".." + Ab(zr.BHi));
            }
            ZoneReportRow(new Scn("500x80 across a crest at s 100 (45% up, 15% down)", "500x80", LRect(500, 80),
                (s, t) => s < 100 ? 1000.0 - 0.45 * (100 - s) : 1000.0 - 0.15 * (s - 100)), passes, tags);
        }

        /// <summary>R2b fixer round, review finding rule-and-tests-rotated-poke-not-exercised.
        /// EndZoneTests' corner pokes rotate polygon and terrain together, so their top edge always
        /// runs along the contours. Here the TERRAIN stays fixed and the polygon is rotated about
        /// the poke corner (corner highest for angle > 0) or its top edge slanted: the count-capped
        /// top zone collects in the highest corner wedge. Asserted: the field-size 30 × 40 poke
        /// (the DECISIONS 116 case) stays Green at every angle and slant; the larger pokes are
        /// reported (known limit).</summary>
        private static void RotatedPokeTests()
        {
            Console.WriteLine("INFO  R2b fixer round, F116 meadow pokes with the top edge NOT along the contours (review finding rule-and-tests-rotated-poke-not-exercised; " +
                              "same 8 frames; asserted: the 30x40 field-size poke Green everywhere, larger pokes report only):");
            Zp[] ship = { Zp.Shipped };
            string[] shipTag = { "(shipped)" };
            foreach (double2 legs in new[] { new double2(30, 40), new double2(60, 80), new double2(80, 100), new double2(120, 100) })
            {
                bool field = legs.x == F116LegS && legs.y == F116LegT;
                List<Scn> scns = new List<Scn>();
                foreach (double deg in new double[] { -30, 20, 30, 45 })
                    scns.Add(PokeRotated(legs.x, legs.y, deg));
                foreach (double slant in new double[] { -60, 60, 100 })
                    scns.Add(PokeSlanted(legs.x, legs.y, slant));
                foreach (Scn sc in scns)
                {
                    ZoneRange zr = ZoneReportRow(sc, ship, shipTag);
                    if (field)
                        Check(zr.BLo == SlopeBand.Green && zr.BHi == SlopeBand.Green, "rotated poke/" + sc.Name, "the field-size poke must read Green, got " + Ab(zr.BLo) + ".." +
                                                                                                                  Ab(zr.BHi) + " [" + P(zr.Lo) + ", " + P(zr.Hi) + "]");
                }
            }
        }

        // ================================================================== §9.6 item 4: tooltip line 4 rule

        /// <summary>SkiSlopeClassifier.SteepestPatchShown (R2 fixer round; review findings
        /// feedback-tooltip-line4-quantisation-margin and feedback-tooltip-disconnected-labels).
        /// (a) unit cases; (b) near-threshold planes — nothing to avoid, so the line must NEVER
        /// show, with the shown band = the raw band (first recompute) and the hysteresis-held
        /// band below it; (c) the field case and R2-a — it MUST show; (d) a hair-thin hourglass
        /// neck (Mask falls apart, rating = P90) with a full-width headwall — it must not
        /// show.</summary>
        private static void TooltipLineTests()
        {
            const string u = "tooltip/unit";
            Check(SkiSlopeClassifier.SteepestPatchShown(0.20f, 0.45f, SlopeBand.Green, true), u, "20 % crux, 45 % patch, shown Green: must show");
            Check(!SkiSlopeClassifier.SteepestPatchShown(0.20f, 0.45f, SlopeBand.Green, false), u, "not connected: must not show");
            Check(!SkiSlopeClassifier.SteepestPatchShown(0.2451f, 0.2501f, SlopeBand.Green, true), u, "both lines would read 25 %: must not show");
            Check(!SkiSlopeClassifier.SteepestPatchShown(0.2490f, 0.2510f, SlopeBand.Green, true), u, "+0.2 % (< DisplayHysteresis): must not show");
            Check(!SkiSlopeClassifier.SteepestPatchShown(0.20f, 0.45f, SlopeBand.Black, true), u, "patch not above the shown band: must not show");
            Check(!SkiSlopeClassifier.SteepestPatchShown(0.30f, 0.30f, SlopeBand.Blue, true), u, "polyline (steepest == rating): must not show");
            Check(!SkiSlopeClassifier.SteepestPatchShown(float.NaN, 0.45f, SlopeBand.Green, true) &&
                  !SkiSlopeClassifier.SteepestPatchShown(0.20f, float.PositiveInfinity, SlopeBand.Green, true), u, "non-finite: must not show");

            // (b) planes within ±0.3 % of 25 / 40 / 60 % in 0.05 % steps, 12 rotations, 2 sizes, m = 1.
            int cases = 0, fired = 0, firedOld = 0;
            double gapLo = 1, gapHi = -1;
            foreach (double thr in new[] { 0.25, 0.40, 0.60 })
            {
                for (int d = -6; d <= 6; d++)
                {
                    double gr = thr + d * 0.0005;
                    for (int ai = 0; ai < 12; ai++)
                    {
                        double ang = ai * 15.0;
                        foreach (double2 sz in new[] { new double2(300, 60), new double2(400, 100) })
                        {
                            Frame fr = Frame.Make(-1500.3 + 7.1 * ai, 900.7 - 3.3 * d, ang);
                            double2[] ring = Rect(sz.x, sz.y, fr);
                            Terrain t = Terrain.For((x, z) => 1000.0 - gr * fr.Along(x, z), ring, 60);
                            string w = "tooltip/plane " + P(gr) + " a" + ang + " " + F(sz.x) + "x" + F(sz.y);
                            SlopeStatus st = RateWith(t, Pts(ring, false), 1, s_Grid, s_Result, Zp.Shipped, w);
                            if (!Check(st == SlopeStatus.Ok, w, "status " + st))
                                continue;
                            SlopeResult r = s_Result;
                            gapLo = Math.Min(gapLo, r.SteepestGrade - r.RatingGrade);
                            gapHi = Math.Max(gapHi, r.SteepestGrade - r.RatingGrade);
                            SlopeBand raw = SkiSlopeClassifier.BandOf(r.RatingGrade);
                            SlopeBand held = SkiSlopeClassifier.BandOf(r.RatingGrade - SkiSlopeClassifier.DisplayHysteresis);
                            foreach (SlopeBand shown in held == raw ? new[] { raw } : new[] { raw, held })
                            {
                                cases++;
                                bool show = SkiSlopeClassifier.SteepestPatchShown(r.RatingGrade, r.SteepestGrade, shown, r.Connected);
                                if (show)
                                {
                                    fired++;
                                    Check(false, w, "line 4 shown on a uniform plane (rating " + P(r.RatingGrade) + ", steepest " + P(r.SteepestGrade) + ", shown " + shown + ")");
                                }
                                if (r.SteepestGrade > r.RatingGrade && (int)SkiSlopeClassifier.BandOf(r.SteepestGrade) > (int)shown)
                                    firedOld++;
                            }
                        }
                    }
                }
            }
            Console.WriteLine("INFO  tooltip line 4 on near-threshold planes (25/40/60 % ± 0.3 %, 12 rotations, 2 sizes, m = 1; shown band raw and hysteresis-held): " +
                              cases + " cases, shown in " + fired + " (asserted 0; the R2 rule 'steepest > crux && higher band' fired in " + firedOld +
                              "); steepest - crux in [" + P(gapLo) + ", " + P(gapHi) + "]");

            // (c) must show: the field case and R2-a (Green crux, Black steepest patch).
            int must = 0;
            foreach (Scn sc in new[] { F116Long(), R2a() })
            {
                foreach (double ang in Angles7)
                {
                    Frame fr = Frame.Make(-1800.4, -900.7, ang);
                    string w = "tooltip/" + sc.Name + " a" + ang;
                    RunScn(sc, fr, ScnStep(sc), s_Grid, s_Result, w);
                    SlopeResult r = s_Result;
                    Check(SkiSlopeClassifier.SteepestPatchShown(r.RatingGrade, r.SteepestGrade, r.Band, r.Connected), w,
                        "line 4 must show (crux " + P(r.RatingGrade) + " " + r.Band + ", steepest " + P(r.SteepestGrade) + ")");
                    must++;
                }
            }

            // (d) hourglass 60 x 200 m, 20 m neck (s 90-110) narrower than a cell, 10.5 % slope, a
            // full-width 25 m 45 % headwall (s 150-175) in the lower lobe (the reviewer's case).
            int disc = 0, conn = 0;
            foreach (double neck in new[] { 0.6, 1.0, 2.0, 8.0 })
            {
                double hn = neck / 2;
                Scn sc = new Scn("hourglass neck " + F(neck) + " m", "200x60", new[]
                {
                    new double2(0, -30), new double2(90, -30), new double2(90, -hn), new double2(110, -hn), new double2(110, -30), new double2(200, -30),
                    new double2(200, 30), new double2(110, 30), new double2(110, hn), new double2(90, hn), new double2(90, 30), new double2(0, 30),
                }, PitchProfile(MeadowGrade, 150, 25, 0.45));
                foreach (double ang in new double[] { 0, 30, 60, 90 })
                {
                    Frame fr = Frame.Make(2100.2, -1300.6, ang);
                    string w = "tooltip/" + sc.Name + " a" + ang;
                    RunScn(sc, fr, 1, s_Grid, s_Result, w);
                    SlopeResult r = s_Result;
                    bool show = SkiSlopeClassifier.SteepestPatchShown(r.RatingGrade, r.SteepestGrade, r.Band, r.Connected);
                    if (r.Connected)
                    {
                        conn++;
                        Check(r.Band == SlopeBand.Black && !show, w, "connected hourglass: want Black (the headwall) and no line 4; got " + P(r.RatingGrade) + " " + r.Band +
                                                                    ", line 4 " + show);
                    }
                    else
                    {
                        disc++;
                        Check(!show && r.RatingGrade == r.P90Grade, w, "disconnected: line 4 must not show and the rating is P90 (rating " + P(r.RatingGrade) +
                                                                      ", p90 " + P(r.P90Grade) + ", line 4 " + show + ")");
                    }
                }
            }
            Console.WriteLine("INFO  tooltip line 4 must show on the field case + R2-a (" + must + " frames, asserted); hourglass with a sub-cell neck: " + disc +
                              " disconnected (P90 fallback: the tooltip shows the 'typical steep pitch, approximate' line, never line 4), " + conn +
                              " connected (Black, no line 4)");
        }

        // ================================================================== R2-h degenerate / disconnected

        /// <summary>Two 60 × 60 lobes joined by a 40 m neck lying strictly between two lattice
        /// rows (no node inside it), so the Mask falls apart by construction.</summary>
        private static double2[] Dumbbell(double x0, double zRowBase)
        {
            double z0 = Terrain.Origin.y + Math.Round((zRowBase - Terrain.Origin.y) / Terrain.Texel) * Terrain.Texel;   // a lattice row (m = 1 and m = 2: even row)
            z0 = Terrain.Origin.y + 2.0 * Math.Round((z0 - Terrain.Origin.y) / (2.0 * Terrain.Texel)) * Terrain.Texel;
            double n0 = z0 + 0.3 * Terrain.Texel, n1 = z0 + 0.6 * Terrain.Texel;
            return new[]
            {
                new double2(x0, z0 - 30), new double2(x0 + 60, z0 - 30), new double2(x0 + 60, n0), new double2(x0 + 100, n0),
                new double2(x0 + 100, z0 - 30), new double2(x0 + 160, z0 - 30), new double2(x0 + 160, z0 + 30), new double2(x0 + 100, z0 + 30),
                new double2(x0 + 100, n1), new double2(x0 + 60, n1), new double2(x0 + 60, z0 + 30), new double2(x0, z0 + 30),
            };
        }

        private static void DisconnectedTests()
        {
            HeightFn slope = (x, z) => 1000.0 - 0.15 * x - 0.05 * z;
            List<KeyValuePair<string, double2[]>> shapes = new List<KeyValuePair<string, double2[]>>();
            shapes.Add(new KeyValuePair<string, double2[]>("bow-tie", new[] { new double2(500.3, 400.2), new double2(600.3, 460.2), new double2(600.3, 400.2), new double2(500.3, 460.2) }));
            shapes.Add(new KeyValuePair<string, double2[]>("bow-tie skew", new[] { new double2(500.3, 400.2), new double2(700.7, 530.1), new double2(690.2, 380.4), new double2(510.9, 520.6) }));
            foreach (double ang in new double[] { 45, 30, 0.7 })
                shapes.Add(new KeyValuePair<string, double2[]>("2 m sliver a" + ang, Rect(200, 2, Frame.Make(-800.1, 300.2, ang))));
            shapes.Add(new KeyValuePair<string, double2[]>("dumbbell", Dumbbell(-2000.0, 1500.0)));
            int connected = 0, disconnected = 0, other = 0;
            foreach (KeyValuePair<string, double2[]> kv in shapes)
            {
                for (int m = 1; m <= 2; m++)
                {
                    string w = "R2-h " + kv.Key + " m" + m;
                    try
                    {
                        Terrain t = Terrain.For(slope, kv.Value, 60);
                        SlopeStatus st = RateWith(t, Pts(kv.Value, false), m, s_Grid, s_Result, Zp.Shipped, w);
                        SlopeResult r = s_Result;
                        bool rated = st == SlopeStatus.Ok || st == SlopeStatus.ShortRun || st == SlopeStatus.Polyline;
                        if (rated && r.Connected) connected++;
                        else if (rated) disconnected++;
                        else other++;
                        Check(!rated || (r.Connected && !float.IsNaN(r.RatingGrade) && !float.IsInfinity(r.RatingGrade)) ||
                              (!r.Connected && r.RatingGrade == r.P90Grade), w,
                            "status " + st + ": connected " + r.Connected + " rating " + P(r.RatingGrade) + " p90 " + P(r.P90Grade));
                        if (kv.Key == "dumbbell")
                            Check(st == SlopeStatus.Ok && !r.Connected && r.RatingGrade == r.P90Grade && !r.CruxValid, w,
                                "dumbbell must be Ok + disconnected (status " + st + ", connected " + r.Connected + ")");
                        Console.WriteLine("INFO  " + w + ": status " + st + " cells " + s_Grid.InsideCells + " connected " + r.Connected +
                                          " rating " + P(r.RatingGrade) + " p90 " + P(r.P90Grade) + " steepest " + P(r.SteepestGrade));
                    }
                    catch (Exception e)
                    {
                        Fail(w, "threw " + e.GetType().Name + ": " + e.Message);
                    }
                }
            }
            // Collinear ring: polyline mode (Connected, rating == P90 == steepest via PostCheck).
            {
                Frame fr = Frame.Make(-300.2, 100.1, 23);
                double2[] col = { fr.World(0, 0), fr.World(60, 0), fr.World(140, 0), fr.World(220, 0) };
                Terrain t = Terrain.For((x, z) => 1000.0 - 0.2 * fr.Along(x, z), col, 60);
                try
                {
                    SlopeStatus st = RateWith(t, Pts(col, false), 1, s_Grid, s_Result, Zp.Shipped, "R2-h collinear ring");
                    Check(st == SlopeStatus.Polyline && s_Grid.PolylineMode && s_Result.Connected, "R2-h collinear ring", "status " + st);
                }
                catch (Exception e)
                {
                    Fail("R2-h collinear ring", "threw " + e.GetType().Name + ": " + e.Message);
                }
            }
            Console.WriteLine("INFO  R2-h: " + connected + " connected, " + disconnected + " disconnected (rating = P90), " + other + " not rated; no throw");
        }

        // ================================================================== DECISIONS 116 field fixture

        // F116 parameters, tuned (sweep of meadow grade 15.5/16/16.5 % × bank 42/45/48/52 % × wave
        // 1/2 m × legs 30×40/40×50 on 125 × 545, 7 rotations, m = 1) to the dump's numbers:
        // SteepestGrade 40.27-44.90 % (field 41.7 %), drop 93.3-95.3 m (93.9), mean 17.01-17.03 %
        // (17.2), area 68,125 m² (68,300). Wave = a 2 m undulation, 140 m along × 110 m across.
        private const double F116Grade = 0.16, F116Wave = 2.0, F116LegS = 30.0, F116LegT = 40.0, F116BankGrade = 0.48;

        private static Scn F116Long()
        {
            // Area 68,125 m², long enough for drop ≈ 94 m at a 17 % mean (see FieldFixture).
            return CornerBankScn("F116 meadow 125x545 corner bank", 545, 125, F116Grade, F116LegS, F116LegT, F116BankGrade, F116Wave);
        }

        private static Scn F116Wide()
        {
            return CornerBankScn("F116 meadow 170x400 corner bank", 400, 170, F116Grade, F116LegS, F116LegT, F116BankGrade, F116Wave);
        }

        /// <summary>The DECISIONS 116 field case (68,300 m², drop 93.9 m, mean 17.2 %, rated
        /// Double black 41.7 % by R1 because the polygon's top corner touched a bank). A plane
        /// meadow cannot have all of 170 × 400, drop 94 m AND mean 17 %: drop <= mean × extent
        /// along the fall line <= 0.172 × √(400² + 170²) = 75 m. So the fixture comes twice:
        /// 125 × 545 (area, drop, mean match) and 170 × 400 (shape, area, mean match; drop ~70).</summary>
        private static void FieldFixture()
        {
            foreach (Scn sc in new[] { F116Long(), F116Wide() })
            {
                int op = ScnStep(sc);
                double lo = 1, hi = -1, sLo = 1, sHi = -1, dLo = 1e9, dHi = -1e9, mLo = 1, mHi = -1;
                SlopeBand r1Lo = SlopeBand.DoubleBlack, r1Hi = SlopeBand.Flat, r2Lo = SlopeBand.DoubleBlack, r2Hi = SlopeBand.Flat;
                foreach (double ang in Angles7)
                {
                    Frame fr = Frame.Make(-1800.4, -900.7, ang);
                    string w = sc.Name + " a" + ang;
                    SlopeStatus st = RunScn(sc, fr, op, s_Grid, s_Result, w);
                    SlopeResult r = s_Result;
                    lo = Math.Min(lo, r.RatingGrade);
                    hi = Math.Max(hi, r.RatingGrade);
                    sLo = Math.Min(sLo, r.SteepestGrade);
                    sHi = Math.Max(sHi, r.SteepestGrade);
                    dLo = Math.Min(dLo, r.Drop);
                    dHi = Math.Max(dHi, r.Drop);
                    mLo = Math.Min(mLo, r.MeanGrade);
                    mHi = Math.Max(mHi, r.MeanGrade);
                    SlopeBand b1 = R1Band(r.SteepestGrade);
                    if (b1 < r1Lo) r1Lo = b1;
                    if (b1 > r1Hi) r1Hi = b1;
                    if (r.Band < r2Lo) r2Lo = r.Band;
                    if (r.Band > r2Hi) r2Hi = r.Band;
                    Check(st == SlopeStatus.Ok, w, "status " + st);
                    Check(Math.Abs(r.MeanGrade - 0.172) <= 0.015, w, "fixture: mean " + P(r.MeanGrade) + ", want ≈ 17.2 %");
                    if (sc.Size == "545x125")
                    {
                        // The variant tuned to the dump (the 170 × 400 one only shows the shape).
                        Check(Math.Abs(r.SteepestGrade - 0.42) <= 0.035, w, "fixture: SteepestGrade " + P(r.SteepestGrade) + ", want ≈ 42 % (field 41.7 %)");
                        Check(Math.Abs(r.Drop - 93.9) <= 5.0, w, "fixture: drop " + F(r.Drop) + " m, want ≈ 93.9 m");
                    }
                    Check(r.Band == SlopeBand.Green, w, "R2 rates " + P(r.RatingGrade) + " " + r.Band + ", want Green (§9.7 field test 1)");
                    Check(R1Band(r.SteepestGrade) >= SlopeBand.Black, w, "fixture: the R1 rule must reproduce the field's harsh rating (Black or Double black)");
                }
                Console.WriteLine("INFO  " + sc.Name + " (" + sc.Size + ", " + F(Math.Round(sc.Area())) + " m², m=" + op + ", 7 rotations): drop " + F(Math.Round(dLo, 1)) +
                                  "-" + F(Math.Round(dHi, 1)) + " m, mean " + P(mLo) + "-" + P(mHi) + " | R1 rule (k-th window, 6/15/25/40): " + P(sLo) + "-" + P(sHi) +
                                  " -> " + r1Lo + ".." + r1Hi + " | R2 (crux, 6/25/40/60): " + P(lo) + "-" + P(hi) + " -> " + r2Lo + ".." + r2Hi +
                                  " (SteepestGrade " + P(sLo) + "-" + P(sHi) + " -> " + SkiSlopeClassifier.BandOf((float)sLo) + ".." +
                                  SkiSlopeClassifier.BandOf((float)sHi) + " under R2 bands: the tooltip's 'Steepest patch (avoidable)' line)");
            }
        }

        // ================================================================== scenario table + R2-j sensitivity

        private static List<Scn> TableScenarios()
        {
            return new List<Scn>
            {
                RunoutBelow(450), RunoutBelow(2000), RunoutAbove(450), RunoutAbove(2000),
                R2a(), R2b(), R2c(), R2d(), R2e(20), R2e(40), R2e(80), F116Long(), F116Wide(),
                // The R2 area cap skipped EndZoneMaxM2 / width at EACH end (narrow trails lost
                // 100-200 m); R2b's depth cap is ~20 m of run whatever the width (§9.8).
                Trail20Wall(60, "s60-100"), Trail20Wall(1400, "60-100 above bottom"), Trail10Wall(),
                DropIn(20), DropIn(60), DropIn(125), DropIn(200),
                // R2b (§9.8) end-zone fixtures: side ground, the height fallback, curved runs.
                Valley(), SteepValley(0.50), OpenBowl(), Ridge(600, 120), Ridge(2000, 80), SideBank(), CornerPoke(120, 100), Sliver71(),
                TopFace71(), MountainFace68(), WideShort(), LRun(true), SideCliff(), Cone(), CRunHill(), Switchback(),
                // R2b fixer round: the key gate's coherence clause (gentle ground), a narrow top end
                // (zone depth by mean width), a polygon drawn past a crest, a rotated large poke.
                Trail10WallOn(0.01), DropInGentle(0.01, 700), TrailTopPitch(20, 60), CatTrackPitch(30, 40), PastCrest(40, 0.45),
                PokeRotated(80, 100, 30),
            };
        }

        private static void ScenarioTables()
        {
            Frame fr = Frame.Make(0.3, -0.7, 17);
            float[] shares = { 0.05f, 0.10f, 0.15f };
            float[] depths = { 15f, 20f, 30f };
            Console.WriteLine("INFO  R2b scenario table (shipped EndZoneShare " + F(SkiSlopeClassifier.EndZoneShare) + ", EndZoneDepthM " +
                              F(SkiSlopeClassifier.EndZoneDepthM) + ", EndZoneMinMeanGrade " + F(SkiSlopeClassifier.EndZoneMinMeanGrade) + " OR EndZoneMinCoherence " +
                              F(SkiSlopeClassifier.EndZoneMinCoherence) + ", EndZoneMaxM2 " +
                              F(SkiSlopeClassifier.EndZoneMaxM2) + " (height key); one orientation, 17°; operating step; key F = fall line, H = height):");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO    {0,-54} {1,-18} {2,1} {3,6} {4,7} {5,7} {6,7} {7,7} {8,7} {9,7} {10,-7} {11,-7} {12,-4} {13,-3} {14,-9} {15}",
                "scenario", "size", "m", "cells", "area", "drop m", "mean", "steep", "p90", "rating", "band", "R1 rule", "conn", "key", "zones t/b", "crux s,t m"));
            List<string> sens = new List<string>();
            foreach (Scn sc in TableScenarios())
            {
                int op = ScnStep(sc);
                SlopeStatus st = RunScn(sc, fr, op, s_Grid, s_Result, "table " + sc.Name);
                SlopeResult r = s_Result;
                string crux = "-";
                if (r.CruxValid)
                {
                    double2 cw = CruxWorld(r);
                    crux = F(Math.Round(fr.Along(cw.x, cw.y), 1)) + "," + F(Math.Round(fr.Across(cw.x, cw.y), 1));
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO    {0,-54} {1,-18} {2,1} {3,6} {4,7:F0} {5,7:F1} {6,7} {7,7} {8,7} {9,7} {10,-7} {11,-7} {12,-4} {13,-3} {14,-9} {15}",
                    sc.Name, sc.Size, op, s_Grid.InsideCells, r.Area, r.Drop, P(r.MeanGrade), P(r.SteepestGrade), P(r.P90Grade), P(r.RatingGrade),
                    Ab(r.Band), Ab(R1Band(r.SteepestGrade)), r.Connected ? "yes" : "NO", r.ZoneByFallLine ? "F" : "H", r.TopZoneCells + "/" + r.BottomZoneCells, crux));
                Check(st == SlopeStatus.Ok, "table " + sc.Name, "status " + st);

                StringBuilder sb = new StringBuilder();
                sb.Append(string.Format(CultureInfo.InvariantCulture, "INFO    {0,-54} {1,7} {2,7} |", sc.Name, P(r.SteepestGrade), P(r.P90Grade)));
                foreach (float sh in shares)
                {
                    foreach (float dp in depths)
                    {
                        Zp zp = new Zp(sh, SkiSlopeClassifier.EndZoneMaxM2, dp, SkiSlopeClassifier.EndZoneMinMeanGrade, SkiSlopeClassifier.EndZoneMinCoherence);
                        RunScnWith(sc, fr, op, s_Grid, s_Result, zp, "sensitivity " + sc.Name + " " + F(sh) + "/" + F(dp));
                        sb.Append(string.Format(CultureInfo.InvariantCulture, " {0,7} {1,-6}", P(s_Result.RatingGrade), Ab(s_Result.Band)));
                    }
                    sb.Append(" |");
                }
                sens.Add(sb.ToString());
            }
            Console.WriteLine("INFO  R2-j sensitivity (R2b §9.8 grid): RatingGrade and band per EndZoneShare / EndZoneDepthM (EndZoneMaxM2 2000 on the height key; " +
                              "SteepestGrade and P90 do not depend on the zones):");
            StringBuilder head = new StringBuilder();
            head.Append(string.Format(CultureInfo.InvariantCulture, "INFO    {0,-54} {1,7} {2,7} |", "scenario", "steep", "p90"));
            foreach (float sh in shares)
            {
                foreach (float dp in depths)
                    head.Append(string.Format(CultureInfo.InvariantCulture, " {0,14}", F(sh) + "/" + F(dp) +
                                                                              (sh == SkiSlopeClassifier.EndZoneShare && dp == SkiSlopeClassifier.EndZoneDepthM ? "*" : "")));
                head.Append(" |");
            }
            Console.WriteLine(head.ToString());
            foreach (string line in sens)
                Console.WriteLine(line);
        }

        // ================================================================== random oracle sweep

        /// <summary>Brute-force minimax oracle on random polygons (rectangles, star polygons,
        /// self-intersecting shuffles, dumbbells with random necks) over random terrains (plane +
        /// sinusoids + Gaussian bumps, sometimes dead flat or terraced), random step multiples
        /// and random end-zone parameters. OracleCheck runs inside RateWith.</summary>
        private static void RandomOracleTests()
        {
            Unity.Mathematics.Random rng = new Unity.Mathematics.Random(0xC0FFEEu);
            const int cases = 500;
            long before = s_OracleChecks, disc0 = s_OracleDisconnected, shared0 = s_OracleShared;
            int polyline = 0, notRated = 0, cmp48 = 0, cut4 = 0, fallKey = 0, diagOn = 0;
            double up4 = 0;
            for (int c = 0; c < cases; c++)
            {
                double2 centre = new double2(rng.NextDouble(-6000, 6000), rng.NextDouble(-6000, 6000));
                int kind = rng.NextInt(0, 5);
                double2[] ring;
                if (kind == 0)
                {
                    ring = Rect(rng.NextDouble(15, 700), rng.NextDouble(6, 160), Frame.Make(centre.x, centre.y, rng.NextDouble(0, 360)));
                }
                else if (kind == 1 || kind == 2)
                {
                    int n = rng.NextInt(3, 15);
                    double rad = rng.NextDouble(20, 260), aspect = rng.NextDouble(0.3, 1.0), rot = rng.NextDouble(0, 2 * Math.PI);
                    ring = new double2[n];
                    for (int i = 0; i < n; i++)
                    {
                        double a = 2 * Math.PI * (i + 0.8 * rng.NextDouble()) / n;
                        double rr = rad * rng.NextDouble(0.25, 1.0);
                        double lx = Math.Cos(a) * rr, lz = Math.Sin(a) * rr * aspect;
                        ring[i] = centre + new double2(lx * Math.Cos(rot) - lz * Math.Sin(rot), lx * Math.Sin(rot) + lz * Math.Cos(rot));
                    }
                    if (kind == 2)
                    {
                        // Shuffle: self-intersecting rings whose even-odd Mask often falls apart.
                        for (int i = n - 1; i > 0; i--)
                        {
                            int j = rng.NextInt(0, i + 1);
                            double2 tmp = ring[i];
                            ring[i] = ring[j];
                            ring[j] = tmp;
                        }
                    }
                }
                else if (kind == 3)
                {
                    ring = Dumbbell(centre.x, centre.y);
                    // random neck width 0.5..8 m (wide ones connect)
                    double neck = rng.NextDouble(0.5, 8.0);
                    double nz0 = ring[2].y;
                    ring[8].y = nz0 + neck;
                    ring[9].y = nz0 + neck;
                }
                else
                {
                    // Long thin random run (traverses, narrow necks on the lattice).
                    ring = Rect(rng.NextDouble(100, 900), rng.NextDouble(3, 25), Frame.Make(centre.x, centre.y, rng.NextDouble(0, 360)));
                }

                int terrainKind = rng.NextInt(0, 10);
                double gp = rng.NextDouble(0, 0.8), dir = rng.NextDouble(0, 2 * Math.PI);
                double[] amp = new double[3], lam = new double[3], ph = new double[3], ang = new double[3];
                for (int k = 0; k < 3; k++)
                {
                    amp[k] = rng.NextDouble(0, 10);
                    lam[k] = rng.NextDouble(15, 300);
                    ph[k] = rng.NextDouble(0, 6.3);
                    ang[k] = rng.NextDouble(0, 6.3);
                }
                double2 bump = centre + new double2(rng.NextDouble(-150, 150), rng.NextDouble(-150, 150));
                double bumpAmp = rng.NextDouble(-25, 25), bumpSigma = rng.NextDouble(5, 40);
                double terrace = rng.NextDouble(1, 6);
                double2 cc = centre;
                HeightFn f;
                if (terrainKind == 0)
                {
                    f = (x, z) => 1234.5; // dead flat: one height bin, zones share every cell
                }
                else
                {
                    f = (x, z) =>
                    {
                        double hgt = 1500.0 - gp * ((x - cc.x) * Math.Cos(dir) + (z - cc.y) * Math.Sin(dir));
                        for (int k = 0; k < 3; k++)
                            hgt += amp[k] * Math.Sin(2 * Math.PI * (x * Math.Cos(ang[k]) + z * Math.Sin(ang[k])) / lam[k] + ph[k]);
                        double dx = x - bump.x, dz = z - bump.y;
                        hgt += bumpAmp * Math.Exp(-(dx * dx + dz * dz) / (2 * bumpSigma * bumpSigma));
                        if (terrainKind == 1)
                            hgt = Math.Floor(hgt / terrace) * terrace; // terraces: many tied values
                        return hgt;
                    };
                }
                int m = rng.NextInt(1, 4);
                Zp zp = Zp.Shipped;
                if (rng.NextInt(0, 2) == 0)
                {
                    zp.Share = (float)rng.NextDouble(0, 0.5);
                    zp.MaxM2 = (float)rng.NextDouble(0, 10000);
                    zp.DepthM = (float)rng.NextDouble(0, 60);
                    float[] gates = { 0f, SkiSlopeClassifier.EndZoneMinMeanGrade, 0.1f, float.PositiveInfinity };
                    zp.MinMean = gates[rng.NextInt(0, 4)];
                    // R2b fixer round: the gate's coherence clause, chosen by a multiplicative hash of
                    // the case index (not drawn from rng, so the main sequence — polygons,
                    // terrains, the other parameters — is unchanged).
                    float[] cohs = { 0f, SkiSlopeClassifier.EndZoneMinCoherence, 0.9f, float.PositiveInfinity };
                    zp.MinCoh = cohs[(int)(((uint)c * 2654435761u) >> 30)];
                }
                // R2b: the caller's rule (m >= 2) half the time, forced off / on otherwise.
                int[] diagChoice = { -1, -1, 0, 1 };
                s_DiagOverride = diagChoice[rng.NextInt(0, 4)];
                string w = "random #" + c + " kind" + kind + " terrain" + terrainKind + " m" + m + " share " + F(zp.Share) + " cap " + F(zp.MaxM2) +
                           " depth " + F(zp.DepthM) + " gate " + F(zp.MinMean) + " coh " + F(zp.MinCoh) + " diag " + DiagFor(m);
                try
                {
                    Terrain t = Terrain.For(f, ring, 60 + 3 * m * Terrain.Texel);
                    SlopeStatus st = RateWith(t, Pts(ring, false), m, s_Grid, s_Result, zp, w);
                    if (st == SlopeStatus.Polyline || (st == SlopeStatus.ShortRun && s_Grid.PolylineMode))
                    {
                        polyline++;
                    }
                    else if (st != SlopeStatus.Ok && st != SlopeStatus.ShortRun)
                    {
                        notRated++;
                    }
                    else
                    {
                        if (s_Result.ZoneByFallLine) fallKey++;
                        if (s_Grid.DiagonalRule) diagOn++;
                        if (zp.IsShipped())
                        {
                            // 4-vs-8 evidence (report only): how often 4-connectivity would cut a run
                            // that 8-connectivity keeps connected, and how far it would move the rating.
                            float f8 = OracleMinimax(s_Grid, s_Result, Conn8), f4 = OracleMinimax(s_Grid, s_Result, Conn4);
                            if (!float.IsNaN(f8))
                            {
                                cmp48++;
                                if (float.IsNaN(f4))
                                    cut4++;
                                else
                                    up4 = Math.Max(up4, f4 - f8);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Fail(w, "threw " + e.GetType().Name + ": " + e.Message);
                }
                s_DiagOverride = -1;
            }
            Console.WriteLine("INFO  random oracle sweep: " + cases + " cases, " + (s_OracleChecks - before) + " ring ratings checked against the brute-force minimax (" +
                              (s_OracleDisconnected - disc0) + " disconnected, " + (s_OracleShared - shared0) + " with zones sharing a cell; " + fallKey +
                              " on the fall-line key, " + diagOn + " with the diagonal rule), " + polyline + " polyline, " + notRated + " not rated (NoCells / TooLarge)");
            Console.WriteLine("INFO  random sweep, 4- vs 8-connectivity (report only, shipped zone constants): of " + cmp48 +
                              " 8-connected runs, 4-connectivity disconnects " + cut4 + "; where both connect, the 4-connected minimax is higher by at most " + P(up4));
        }

        // ================================================================== R2-k field captures

        /// <summary>The folder holding SlopeBatch.csproj, found by walking up from the build
        /// output (AppContext.BaseDirectory = tests/SlopeBatch/bin/&lt;cfg&gt;/net8.0/).</summary>
        private static string ProjectDir()
        {
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "SlopeBatch.csproj")))
                    return d.FullName;
            }
            return null;
        }

        private static void CaptureTests()
        {
            CaptureRoundTrip();
            string proj = ProjectDir();
            if (proj == null)
            {
                Console.WriteLine("WARN  captures: SlopeBatch.csproj not found above " + AppContext.BaseDirectory + " — field captures not replayed");
                return;
            }
            string dir = Path.Combine(proj, "captures");
            string[] files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.skislope") : new string[0];
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0)
            {
                Console.WriteLine("INFO  captures: no *.skislope files in " + dir);
                return;
            }
            foreach (string file in files)
            {
                string w = "capture " + Path.GetFileName(file);
                Capture cap;
                string err;
                if (!Check(TryLoadCapture(file, out cap, out err), w, "parse: " + err))
                    continue;
                SlopeGrid g = new SlopeGrid();
                SlopeResult r = new SlopeResult();
                string problem = CaptureProblem(cap, g, r, w, out SlopeStatus st);
                if (!Check(problem == null, w, problem))
                    continue;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO  capture {0}: status {1} m={2} cells {3} area {4:F0} m² drop {5:F1} m mean {6} steepest {7} p90 {8} rating {9} band {10} (R1 rule {11}) " +
                    "connected {12} | length {17:F1} m (rect {18:F1}, axis {19:F1}, key {20}) | captured: rating {13} steepest {14} band {15}{16}",
                    cap.Name, st, cap.Multiple, g.InsideCells, r.Area, r.Drop, P(r.MeanGrade), P(r.SteepestGrade), P(r.P90Grade), P(r.RatingGrade), r.Band,
                    R1Band(r.SteepestGrade), r.Connected, P(cap.RatedRating), P(cap.RatedSteepest), cap.RatedBand,
                    cap.HasExpect ? " | expect " + cap.Expect : "", r.Length, r.LengthRect, r.AxisLength,
                    g.PolylineMode ? "-" : r.ZoneByFallLine ? "fall line" : "height"));
                if (cap.HasExpect)
                    Check(r.Band == cap.Expect, w, "band " + r.Band + " (rating " + P(r.RatingGrade) + "), expected " + cap.Expect);
                CaptureLengthCheck(w, cap.Name, r);
            }
        }

        /// <summary>R3 (run-binding-glade-spec.md §5.1): the field captures as REGRESSION
        /// fixtures of the computed Length (±0.1 %, values recorded from this round's first run),
        /// plus a cross-check of LengthRect against the spec's skeptic-recomputed values (§5.1:
        /// "092600 434 / 510 m (max), 093404 1,011 / 1,031, 093455 and 093534 240 / 309, 093640
        /// 833, 093940 2,170, 094240 2,666, 095100 826 / 864"; whole metres, so ±1 m). A capture
        /// that is not in the table (a new file) is reported, not failed.</summary>
        private static void CaptureLengthCheck(string w, string name, SlopeResult r)
        {
            // name -> { regression Length, skeptic LengthRect (m) }
            Dictionary<string, float[]> table = new Dictionary<string, float[]>(StringComparer.Ordinal)
            {
                // Regression values: R3 first run (CoreCLR), 2026-10-01. Length = max(rect, axis):
                // 092600 rect 433.8 / axis 509.9; 093404 1010.8 / 1030.6; 093455 + 093534 239.8 /
                // 308.5; 093640 833.1 / 703.8; 093940 2170.2 / 1940.4; 094240 2665.6 / 2618.5;
                // 095100 825.6 / 864.4 (all on the fall-line key).
                { "run-20261001-092600.skislope", new[] { 509.91565f, 434f } },
                { "run-20261001-093404.skislope", new[] { 1030.6295f, 1011f } },
                { "run-20261001-093455.skislope", new[] { 308.5458f, 240f } },
                { "run-20261001-093534.skislope", new[] { 308.5458f, 240f } },
                { "run-20261001-093640.skislope", new[] { 833.1054f, 833f } },
                { "run-20261001-093940.skislope", new[] { 2170.1711f, 2170f } },
                { "run-20261001-094240.skislope", new[] { 2665.6484f, 2666f } },
                { "run-20261001-095100.skislope", new[] { 864.40625f, 826f } },
            };
            if (!table.TryGetValue(name, out float[] want))
            {
                Console.WriteLine("INFO  capture " + name + ": no length regression value (a capture added after R3) — Length " +
                                  r.Length.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            Check(Math.Abs(r.LengthRect - want[1]) <= 1f, w, "LengthRect " + F(r.LengthRect) + " vs the spec's skeptic-recomputed " + F(want[1]) + " m (±1 m)");
            if (want[0] > 0f)
                Check(Math.Abs(r.Length - want[0]) <= 1e-3f * want[0], w, "Length " + F(r.Length) + " vs regression value " + F(want[0]) + " (±0.1 %)");
            else
                Fail(w, "no length regression value recorded (Length = " + r.Length.ToString("R", CultureInfo.InvariantCulture) + ")");
        }

        /// <summary>Parses the §9.5 format. Lenient only where the spec is silent: blank lines and
        /// lines starting with '#' are skipped, and an `expect &lt;Band&gt;` line may appear anywhere
        /// after the first line.</summary>
        private static bool TryLoadCapture(string path, out Capture cap, out string err)
        {
            cap = new Capture();
            cap.Name = Path.GetFileName(path);
            err = null;
            List<string> lines = new List<string>();
            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    lines.Add(line);
                }
            }
            catch (Exception e)
            {
                err = "read failed: " + e.Message;
                return false;
            }
            int at = 0;
            try
            {
                List<string> body = new List<string>();
                for (int i = 0; i < lines.Count; i++)
                {
                    string[] tk = Tok(lines[i]);
                    if (i > 0 && tk[0] == "expect")
                    {
                        if (tk.Length != 2 || !TryBand(tk[1], out cap.Expect))
                        {
                            err = "bad expect line '" + lines[i] + "'";
                            return false;
                        }
                        cap.HasExpect = true;
                        continue;
                    }
                    body.Add(lines[i]);
                }
                lines = body;
                string[] t0 = Next(lines, ref at, "SKISLOPE", 2);
                if (t0[1] != "1")
                {
                    err = "unsupported version " + t0[1];
                    return false;
                }
                string[] t1 = Next(lines, ref at, "step", 6);
                if (t1[2] != "texel" || t1[4] != "multiple")
                    throw new FormatException("step line");
                cap.Step = Fl(t1[1]);
                cap.Texel = Fl(t1[3]);
                cap.Multiple = int.Parse(t1[5], NumberStyles.Integer, CultureInfo.InvariantCulture);
                string[] t2 = Next(lines, ref at, "origin", 3);
                cap.Origin = new double2(Db(t2[1]), Db(t2[2]));
                string[] t3 = Next(lines, ref at, "grid", 5);
                cap.I0 = In(t3[1]);
                cap.J0 = In(t3[2]);
                cap.Nx = In(t3[3]);
                cap.Nz = In(t3[4]);
                string[] t4 = Next(lines, ref at, "rated", 6);
                cap.RatedRating = Fl(t4[1]);
                cap.RatedSteepest = Fl(t4[2]);
                cap.RatedMean = Fl(t4[3]);
                cap.RatedDrop = Fl(t4[4]);
                cap.RatedBand = t4[5];
                string[] t5 = Next(lines, ref at, "nodes", 2);
                int nn = In(t5[1]);
                if (nn < 0 || nn > 100000)
                    throw new FormatException("node count " + nn);
                cap.Nodes = new float3[nn];
                for (int k = 0; k < nn; k++)
                {
                    string[] p = Tok(lines[at++]);
                    if (p.Length != 3)
                        throw new FormatException("node line " + k);
                    cap.Nodes[k] = new float3(Fl(p[0]), Fl(p[1]), Fl(p[2]));
                }
                string[] t6 = Next(lines, ref at, "need", 2);
                int nneed = In(t6[1]);
                if (nneed < 0)
                    throw new FormatException("need count " + nneed);
                for (int k = 0; k < nneed; k++)
                {
                    string[] p = Tok(lines[at++]);
                    if (p.Length != 3)
                        throw new FormatException("need line " + k);
                    cap.Heights[Capture.Key(cap.I0 + In(p[0]), cap.J0 + In(p[1]))] = Fl(p[2]);
                }
                if (at != lines.Count)
                    throw new FormatException((lines.Count - at) + " unexpected trailing lines");
            }
            catch (Exception e)
            {
                err = "record " + at + ": " + e.Message;
                return false;
            }
            return true;
        }

        private static string[] Tok(string line)
        {
            return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string[] Next(List<string> lines, ref int at, string keyword, int count)
        {
            if (at >= lines.Count)
                throw new FormatException("missing '" + keyword + "' line");
            string[] tk = Tok(lines[at++]);
            if (tk.Length != count || tk[0] != keyword)
                throw new FormatException("expected '" + keyword + "' with " + (count - 1) + " values, got '" + string.Join(" ", tk) + "'");
            return tk;
        }

        private static float Fl(string s)
        {
            return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static double Db(string s)
        {
            return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static int In(string s)
        {
            return int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static bool TryBand(string s, out SlopeBand band)
        {
            if (Enum.TryParse(s, true, out band) && Enum.IsDefined(typeof(SlopeBand), band))
                return true;
            band = SlopeBand.Flat;
            return false;
        }

        /// <summary>Every reason a field capture's replay is a FAIL (R2 fixer round, review finding
        /// tests-3), or null: step != multiple·texel (the game's step is m·texel exactly); the
        /// re-Plan is not Ok (the game writes captures of RATED polygons only —
        /// SkiRunDrawFeedbackSystem.SnapshotCapture runs only for Ok / ShortRun / Polyline — so a
        /// polygon that no longer plans means Plan or the rasterizer changed, or the file is
        /// corrupt); the re-planned grid (I0, J0, Nx, Nz) differs from the captured `grid` line;
        /// a Need node the capture does not cover; a replay status that is not a rating.</summary>
        private static string CaptureProblem(Capture cap, SlopeGrid g, SlopeResult r, string where, out SlopeStatus st)
        {
            st = SlopeStatus.NoCells;
            if (!(cap.Texel > 0f) || cap.Multiple < 1 || cap.Step != cap.Multiple * cap.Texel)
                return "step " + Rt(cap.Step) + " != multiple " + cap.Multiple + " · texel " + Rt(cap.Texel);
            st = ReplayCapture(cap, g, r, out int missing, out SlopeStatus planStatus, where);
            if (planStatus != SlopeStatus.Ok)
                return "re-Plan returned " + planStatus + " (the game captures rated polygons only: Plan changed, or the file is corrupt)";
            if (g.I0 != cap.I0 || g.J0 != cap.J0 || g.Nx != cap.Nx || g.Nz != cap.Nz)
                return "re-planned grid " + g.I0 + " " + g.J0 + " " + g.Nx + " " + g.Nz + " differs from the captured grid line " + cap.I0 + " " + cap.J0 + " " +
                       cap.Nx + " " + cap.Nz;
            if (missing > 0)
                return "Plan's Need set is not covered by the capture: " + missing + " Need nodes have no captured height";
            if (st != SlopeStatus.Ok && st != SlopeStatus.ShortRun && st != SlopeStatus.Polyline)
                return "replay status " + st + " is not a rating";
            return null;
        }

        /// <summary>Re-plans the capture from its nodes (step and origin as captured), fills every
        /// Need node from the captured heights by WORLD lattice index, classifies. `missing` =
        /// Need nodes the capture does not cover (left NaN, so Classify reads NonFinite).</summary>
        private static SlopeStatus ReplayCapture(Capture cap, SlopeGrid g, SlopeResult r, out int missing, out SlopeStatus planStatus, string where)
        {
            missing = 0;
            s_Classifies++;
            SlopeStatus ps = SkiSlopeClassifier.Plan(cap.Nodes, cap.Nodes.Length, cap.Origin, cap.Step, g);
            g.DiagonalRule = cap.Multiple >= 2;   // the feedback system's rule (R2b §9.8)
            planStatus = ps;
            if (ps != SlopeStatus.Ok)
                return SkiSlopeClassifier.Classify(g, r);
            for (int j = 0; j < g.Nz; j++)
            {
                for (int i = 0; i < g.Nx; i++)
                {
                    int q = j * g.Nx + i;
                    if (g.Need[q] == 0)
                        continue;
                    if (cap.Heights.TryGetValue(Capture.Key(g.I0 + i, g.J0 + j), out float hv))
                        g.Heights[q] = hv;
                    else
                        missing++;
                }
            }
            SlopeStatus st = SkiSlopeClassifier.Classify(g, r);
            if (missing == 0)
                PostCheck(where, g, r, st, Zp.Shipped);
            return st;
        }

        /// <summary>Test-side writer, exactly the §9.5 format as amended by §9.8: invariant culture,
        /// floats "G9" and doubles "G17" (both round-trip exactly); `legacy` writes R2's "R" for
        /// both (the format of captures already on disk, which the loader must keep reading).</summary>
        private static void WriteCapture(string path, SlopeGrid g, SlopeResult r, float texel, int multiple, float3[] nodes, string extraLine, bool legacy)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string ff = legacy ? "R" : "G9", fd = legacy ? "R" : "G17";
            StringBuilder sb = new StringBuilder();
            sb.Append("SKISLOPE 1\n");
            sb.Append("step ").Append(g.Step.ToString(ff, inv)).Append(" texel ").Append(texel.ToString(ff, inv)).Append(" multiple ")
              .Append(multiple.ToString(inv)).Append('\n');
            sb.Append("origin ").Append(g.Origin.x.ToString(fd, inv)).Append(' ').Append(g.Origin.y.ToString(fd, inv)).Append('\n');
            sb.Append("grid ").Append(g.I0.ToString(inv)).Append(' ').Append(g.J0.ToString(inv)).Append(' ').Append(g.Nx.ToString(inv)).Append(' ')
              .Append(g.Nz.ToString(inv)).Append('\n');
            sb.Append("rated ").Append(r.RatingGrade.ToString(ff, inv)).Append(' ').Append(r.SteepestGrade.ToString(ff, inv)).Append(' ')
              .Append(r.MeanGrade.ToString(ff, inv)).Append(' ').Append(r.Drop.ToString(ff, inv)).Append(' ').Append(r.Band.ToString()).Append('\n');
            sb.Append("nodes ").Append(nodes.Length.ToString(inv)).Append('\n');
            foreach (float3 p in nodes)
                sb.Append(p.x.ToString(ff, inv)).Append(' ').Append(p.y.ToString(ff, inv)).Append(' ').Append(p.z.ToString(ff, inv)).Append('\n');
            sb.Append("need ").Append(g.NeedCells.ToString(inv)).Append('\n');
            for (int j = 0; j < g.Nz; j++)
                for (int i = 0; i < g.Nx; i++)
                    if (g.Need[j * g.Nx + i] != 0)
                        sb.Append(i.ToString(inv)).Append(' ').Append(j.ToString(inv)).Append(' ').Append(g.Heights[j * g.Nx + i].ToString(ff, inv)).Append('\n');
            if (extraLine != null)
                sb.Append(extraLine).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>True when two parsed captures hold bit-identical values (header, rated line,
        /// nodes, every captured height).</summary>
        private static bool SameCapture(Capture a, Capture b)
        {
            if (a.Step != b.Step || a.Texel != b.Texel || a.Multiple != b.Multiple || !a.Origin.Equals(b.Origin) || a.I0 != b.I0 || a.J0 != b.J0 || a.Nx != b.Nx ||
                a.Nz != b.Nz || a.RatedRating != b.RatedRating || a.RatedSteepest != b.RatedSteepest || a.RatedMean != b.RatedMean || a.RatedDrop != b.RatedDrop ||
                a.RatedBand != b.RatedBand || a.Nodes.Length != b.Nodes.Length || a.Heights.Count != b.Heights.Count)
                return false;
            for (int k = 0; k < a.Nodes.Length; k++)
                if (!a.Nodes[k].Equals(b.Nodes[k]))
                    return false;
            foreach (KeyValuePair<long, float> kv in a.Heights)
                if (!b.Heights.TryGetValue(kv.Key, out float hv) || BitConverter.SingleToInt32Bits(hv) != BitConverter.SingleToInt32Bits(kv.Value))
                    return false;
            return true;
        }

        /// <summary>R2-k self-test: a synthetic run (the F116 field fixture at an odd angle, with
        /// non-zero node y) is written with the test-side writer, read back with the loader and
        /// replayed; the classification must be bit-identical. Also: a truncated capture must
        /// report its uncovered Need nodes, and a bad expect line must not parse.</summary>
        private static void CaptureRoundTrip()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "SlopeBatch-roundtrip-" + Environment.ProcessId + ".skislope");
            try
            {
                // R2b: an m = 2 run too, so the replay exercises the diagonal rule (cap.Multiple >= 2).
                Scn bankM2 = new Scn("round-trip bank 12 m @ 50% 2600x80 (m = 2)", "2600x80", LRect(2600, 80), (s, t) => BankProfile(s, 12, 0.50));
                foreach (Scn sc in new[] { F116Long(), R2d(), bankM2 })
                {
                    int m = ScnStep(sc);
                    Frame fr = Frame.Make(-777.77, 345.67, 23.4);
                    double2[] ring = sc.Ring(fr);
                    Terrain t = Terrain.For(sc.Height(fr), ring, 60);
                    float3[] nodes = Pts(ring, false);
                    for (int k = 0; k < nodes.Length; k++)
                        nodes[k].y = t.Sample(nodes[k].x, nodes[k].z);
                    SlopeGrid g = new SlopeGrid();
                    SlopeResult r = new SlopeResult();
                    RateWith(t, nodes, m, g, r, Zp.Shipped, "roundtrip " + sc.Name);
                    Snapshot want = Snapshot.Take(g, r);
                    string w = "capture round trip " + sc.Name;
                    // R2b §9.8: the R2 "R" format must still load, to the same values as G9 / G17.
                    WriteCapture(tmp, g, r, Terrain.Texel, m, nodes, "expect " + r.Band, true);
                    bool legacyOk = Check(TryLoadCapture(tmp, out Capture legacy, out string errLegacy), w + " (legacy R format)", "parse: " + errLegacy);
                    WriteCapture(tmp, g, r, Terrain.Texel, m, nodes, "expect " + r.Band, false);
                    if (!Check(TryLoadCapture(tmp, out Capture cap, out string err), w, "parse: " + err))
                        continue;
                    if (legacyOk)
                    {
                        Check(SameCapture(cap, legacy), w, "a G9 / G17 capture and the same capture in R2's \"R\" format parse to different values");
                        SlopeGrid gl = new SlopeGrid();
                        SlopeResult rl = new SlopeResult();
                        string pl = CaptureProblem(legacy, gl, rl, w + " (legacy R format)", out SlopeStatus _);
                        Check(pl == null, w + " (legacy R format)", "a good legacy capture reported: " + pl);
                        string dl = want.Diff(Snapshot.Take(gl, rl));
                        Check(dl == null, w + " (legacy R format)", "replayed classification differs: " + dl);
                    }
                    Check(cap.HasExpect && cap.Expect == r.Band, w, "expect line not read back");
                    Check(cap.Step == g.Step && cap.Origin.Equals(g.Origin) && cap.I0 == g.I0 && cap.J0 == g.J0 && cap.Nx == g.Nx && cap.Nz == g.Nz &&
                          cap.Multiple == m && cap.Texel == Terrain.Texel, w, "header fields differ after the round trip");
                    Check(cap.RatedRating == r.RatingGrade && cap.RatedSteepest == r.SteepestGrade && cap.RatedMean == r.MeanGrade && cap.RatedDrop == r.Drop &&
                          cap.RatedBand == r.Band.ToString(), w, "rated line differs after the round trip");
                    bool nodesSame = cap.Nodes.Length == nodes.Length;
                    for (int k = 0; nodesSame && k < nodes.Length; k++)
                        nodesSame = cap.Nodes[k].Equals(nodes[k]);
                    Check(nodesSame, w, "nodes differ after the round trip");
                    SlopeGrid g2 = new SlopeGrid();
                    SlopeResult r2 = new SlopeResult();
                    string problem = CaptureProblem(cap, g2, r2, w, out SlopeStatus st);
                    Check(problem == null, w, "a good capture reported: " + problem);
                    string d = want.Diff(Snapshot.Take(g2, r2));
                    Check(d == null, w, "replayed classification differs: " + d + " (status " + st + ")");

                    // Negatives (review finding tests-3): each must be REPORTED by CaptureProblem,
                    // i.e. CaptureTests would FAIL on it.
                    string[] good = File.ReadAllLines(tmp);
                    int stepAt = Array.FindIndex(good, s => s.StartsWith("step ", StringComparison.Ordinal));
                    int gridAt = Array.FindIndex(good, s => s.StartsWith("grid ", StringComparison.Ordinal));
                    int nodesAt = Array.FindIndex(good, s => s.StartsWith("nodes ", StringComparison.Ordinal));
                    CultureInfo inv = CultureInfo.InvariantCulture;
                    List<KeyValuePair<string, string[]>> bad = new List<KeyValuePair<string, string[]>>();
                    {
                        string[] b = (string[])good.Clone();
                        b[stepAt] = "step 200 texel 100 multiple 2";   // consistent, but Plan refuses it (BadStep)
                        bad.Add(new KeyValuePair<string, string[]>("re-Plan BadStep|re-Plan returned BadStep", b));
                    }
                    {
                        string[] b = (string[])good.Clone();
                        b[stepAt] = "step " + g.Step.ToString("G9", inv) + " texel " + Terrain.Texel.ToString("G9", inv) + " multiple " + (m + 1).ToString(inv);
                        bad.Add(new KeyValuePair<string, string[]>("step != multiple·texel|!= multiple", b));
                    }
                    {
                        string[] b = (string[])good.Clone();
                        b[gridAt] = "grid " + (g.I0 + 1).ToString(inv) + " " + g.J0.ToString(inv) + " " + g.Nx.ToString(inv) + " " + g.Nz.ToString(inv);
                        bad.Add(new KeyValuePair<string, string[]>("grid line mismatch|differs from the captured grid line", b));
                    }
                    {
                        // Nodes collapsed to a 1.4 m² triangle strictly between lattice nodes: ring mode, NoCells.
                        int nn = int.Parse(good[nodesAt].Substring(6), inv);
                        double bx = Terrain.Origin.x + Math.Floor((nodes[0].x - Terrain.Origin.x) / g.Step) * g.Step + 0.3;
                        double bz = Terrain.Origin.y + Math.Floor((nodes[0].z - Terrain.Origin.y) / g.Step) * g.Step + 0.3;
                        List<string> b = new List<string>();
                        for (int i = 0; i < good.Length; i++)
                        {
                            if (i == nodesAt)
                            {
                                b.Add("nodes 3");
                                b.Add(((float)bx).ToString("G9", inv) + " 0 " + ((float)bz).ToString("G9", inv));
                                b.Add(((float)(bx + 1.7)).ToString("G9", inv) + " 0 " + ((float)bz).ToString("G9", inv));
                                b.Add(((float)bx).ToString("G9", inv) + " 0 " + ((float)(bz + 1.7)).ToString("G9", inv));
                                i += nn;
                                continue;
                            }
                            b.Add(good[i]);
                        }
                        bad.Add(new KeyValuePair<string, string[]>("nodes collapsed (NoCells)|re-Plan returned NoCells", b.ToArray()));
                    }
                    foreach (KeyValuePair<string, string[]> kv in bad)
                    {
                        string[] parts = kv.Key.Split('|');
                        File.WriteAllLines(tmp, kv.Value);
                        string wb = w + " (" + parts[0] + ")";
                        if (!Check(TryLoadCapture(tmp, out Capture cb, out err), wb, "parse: " + err))
                            continue;
                        string pb = CaptureProblem(cb, new SlopeGrid(), new SlopeResult(), wb, out SlopeStatus sb);
                        Check(pb != null && pb.Contains(parts[1]), wb, "a bad capture was not reported as '" + parts[1] + "' (got " + (pb ?? "no problem") +
                                                                       ", status " + sb + ")");
                    }
                    File.WriteAllLines(tmp, good);

                    // Negative: drop one need line -> the loader parses, the replay reports 1 missing node.
                    string[] all = File.ReadAllLines(tmp);
                    List<string> cut = new List<string>();
                    int needAt = Array.FindIndex(all, s => s.StartsWith("need ", StringComparison.Ordinal));
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (i == needAt)
                            cut.Add("need " + (cap.Heights.Count - 1).ToString(CultureInfo.InvariantCulture));
                        else if (i != needAt + 1)
                            cut.Add(all[i]);
                    }
                    File.WriteAllLines(tmp, cut);
                    if (Check(TryLoadCapture(tmp, out Capture cut1, out err), w + " (truncated)", "parse: " + err))
                    {
                        ReplayCapture(cut1, new SlopeGrid(), new SlopeResult(), out int miss1, out SlopeStatus _, w + " (truncated)");
                        Check(miss1 == 1, w + " (truncated)", "uncovered Need nodes " + miss1 + ", want 1");
                        string pt = CaptureProblem(cut1, new SlopeGrid(), new SlopeResult(), w + " (truncated)", out SlopeStatus _);
                        Check(pt != null && pt.Contains("not covered"), w + " (truncated)", "uncovered Need not reported (got " + (pt ?? "no problem") + ")");
                    }
                    // Negative: a bad band in expect does not parse.
                    File.WriteAllLines(tmp, new List<string>(all) { "expect Purple" });
                    Check(!TryLoadCapture(tmp, out _, out _), w + " (bad expect)", "an 'expect Purple' line parsed");
                }
            }
            catch (Exception e)
            {
                Fail("capture round trip", "threw " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp))
                        File.Delete(tmp);
                }
                catch (IOException)
                {
                }
            }
        }

        // ================================================================== R3 run length (§5.1)
        //
        // run-binding-glade-spec.md §5.1: Length = max(LengthRect, AxisLength) in ring mode,
        // horizontal metres (D14); LengthRect = (P + sqrt(max(0, P² − 16A))) / 4 over the
        // sanitized ring; AxisLength = the fall-line key range + step when ZoneByFallLine, else 0;
        // polyline = arc length. LengthCheck (every PostCheck) and OracleCheck (AxisLength vs the
        // oracle's key range) pin the DEFINITION on every classification of this host; this
        // section pins the VALUES: 11 synthetic shapes and the 8 field captures are REGRESSION
        // fixtures of the computed Length (±0.1 %, values recorded from this round's first run on
        // CoreCLR, frame 0 of each shape), each shape's error against its designed centreline
        // length is INFO only (asserting ±2 % against the true length would fail the funnel and
        // the height-key diamond by design — the two known limits in the classifier header). The
        // constant-width bands (rectangles, the mitred L and dogleg) additionally assert
        // LengthRect against the true length (±0.01 %): the closed form is exact for them.

        /// <summary>A mitred constant-width band of width w around a centreline polyline:
        /// left offsets forward, right offsets back. Interior vertices sit on the bisector at
        /// (w / 2) / cos(θ / 2), so the band's area is centreline × w and its perimeter 2 ×
        /// centreline + 2w — the case where LengthRect is exact.</summary>
        private static double2[] MitredBand(double2[] c, double w)
        {
            int n = c.Length;
            double2[] left = new double2[n], right = new double2[n];
            for (int i = 0; i < n; i++)
            {
                double2 nPrev = default, nNext = default;
                bool hasPrev = i > 0, hasNext = i < n - 1;
                if (hasPrev)
                {
                    double2 d = c[i] - c[i - 1];
                    double l = Math.Sqrt(d.x * d.x + d.y * d.y);
                    nPrev = new double2(-d.y / l, d.x / l);
                }
                if (hasNext)
                {
                    double2 d = c[i + 1] - c[i];
                    double l = Math.Sqrt(d.x * d.x + d.y * d.y);
                    nNext = new double2(-d.y / l, d.x / l);
                }
                double2 nrm;
                double scale = w / 2;
                if (hasPrev && hasNext)
                {
                    double2 s = nPrev + nNext;
                    double sl = Math.Sqrt(s.x * s.x + s.y * s.y);
                    nrm = new double2(s.x / sl, s.y / sl);
                    scale = (w / 2) / (nrm.x * nNext.x + nrm.y * nNext.y);
                }
                else
                {
                    nrm = hasPrev ? nPrev : nNext;
                }
                left[i] = c[i] + nrm * scale;
                right[i] = c[i] - nrm * scale;
            }
            double2[] ring = new double2[2 * n];
            for (int i = 0; i < n; i++)
            {
                ring[i] = left[i];
                ring[2 * n - 1 - i] = right[i];
            }
            return ring;
        }

        private static double PolyLen(double2[] c)
        {
            double s = 0.0;
            for (int i = 0; i + 1 < c.Length; i++)
            {
                double2 d = c[i + 1] - c[i];
                s += Math.Sqrt(d.x * d.x + d.y * d.y);
            }
            return s;
        }

        /// <summary>The C-run of the length fixtures: two straight `arm` m arms (centre lines at
        /// t = ±r) joined by a 180° bend of centre radius r around (arm, 0), width w, arcs at 1°
        /// vertex spacing. Designed centreline = 2·arm + π·r.</summary>
        private static double2[] CRunRing(double arm, double r, double w)
        {
            List<double2> p = new List<double2>();
            double ro = r + w / 2, ri = r - w / 2;
            p.Add(new double2(0, ro));
            for (int a = 90; a >= -90; a--)
                p.Add(new double2(arm + ro * Math.Cos(a * Math.PI / 180.0), ro * Math.Sin(a * Math.PI / 180.0)));
            p.Add(new double2(0, -ro));
            p.Add(new double2(0, -ri));
            for (int a = -90; a <= 90; a++)
                p.Add(new double2(arm + ri * Math.Cos(a * Math.PI / 180.0), ri * Math.Sin(a * Math.PI / 180.0)));
            p.Add(new double2(0, ri));
            return p.ToArray();
        }

        private sealed class LenShape
        {
            public string Name;
            public double2[] Ring;
            public LocalHeight H;
            public double True;     // designed centreline length (m)
            public bool ExactBand;  // constant-width band: LengthRect == True (±0.01 %)
            public float Want;      // regression value of Length at frame 0 (this round's first run)
        }

        private static readonly Frame[] LenFrames =
        {
            Frame.Make(0.3, -0.7, 17), Frame.Make(-3123.4, 2345.6, 0), Frame.Make(0.3, -0.7, 73), Frame.Make(1234.5, -987.6, 211),
        };

        private static LenShape[] LengthShapes()
        {
            LocalHeight slope20 = (s, t) => 1000.0 - 0.20 * s;
            LocalHeight slope10 = (s, t) => 1000.0 - 0.10 * s;
            LocalHeight flat = (s, t) => 1000.0;   // exact zero gradient: FallLine (0, 0) -> height key, AxisLength 0
            double2[] lCentre = { new double2(0, 0), new double2(300, 0), new double2(300, 200) };
            double c30 = Math.Cos(Math.PI / 6), s30 = Math.Sin(Math.PI / 6);
            double2[] dogCentre = { new double2(0, 0), new double2(200, 0), new double2(200 + 150 * c30, 150 * s30), new double2(400 + 150 * c30, 150 * s30) };
            return new[]
            {
                // Want = R3 first run (CoreCLR), frame 0 (rotation 17°). A straight run reads up to
                // ~one cell long through AxisLength (key range of cell CENTRES + step).
                new LenShape { Name = "rect 600x60 @20%", Ring = LRect(600, 60), H = slope20, True = 600, ExactBand = true, Want = 603.3069f },
                new LenShape { Name = "trail 1500x20 @20%", Ring = LRect(1500, 20), H = slope20, True = 1500, ExactBand = true, Want = 1503.4326f },
                new LenShape { Name = "wide 400x150 @20%", Ring = LRect(400, 150), H = slope20, True = 400, ExactBand = true, Want = 403.39972f },
                new LenShape { Name = "xl 2600x80 @20% (m = 2)", Ring = LRect(2600, 80), H = slope20, True = 2600, ExactBand = true, Want = 2606.497f },
                new LenShape { Name = "L 300+200 w40 (2nd leg a traverse) @20%", Ring = MitredBand(lCentre, 40), H = slope20, True = PolyLen(lCentre), ExactBand = true, Want = 500f },
                new LenShape { Name = "dogleg 200+150(30°)+200 w50 @20%", Ring = MitredBand(dogCentre, 50), H = slope20, True = PolyLen(dogCentre), ExactBand = true, Want = 550f },
                new LenShape { Name = "C-run 150+180°r60+150 w40 @10%", Ring = CRunRing(150, 60, 40), H = slope10, True = 300 + Math.PI * 60, ExactBand = false, Want = 488.4938f },
                new LenShape
                {
                    Name = "funnel: bowl 200 w x150, taper 100, trail 30 w x400 @20%",
                    Ring = new[]
                    {
                        new double2(0, -100), new double2(150, -100), new double2(250, -15), new double2(650, -15), new double2(650, 15), new double2(250, 15),
                        new double2(150, 100), new double2(0, 100),
                    },
                    H = slope20, True = 650, ExactBand = false, Want = 722.1608f,   // known limit: the bowl's perimeter over-reads (+11.1 %)
                },
                new LenShape
                {
                    Name = "diamond 500x240 on flat ground (height key)",
                    Ring = new[] { new double2(0, 0), new double2(250, -120), new double2(500, 0), new double2(250, 120) },
                    H = flat, True = 500, ExactBand = false, Want = 407.3085f,      // known limit: height key, LengthRect alone (-18.5 %)
                },
                new LenShape
                {
                    Name = "taper 500 long 150 -> 30 w on flat ground (height key)",
                    Ring = new[] { new double2(0, -75), new double2(500, -15), new double2(500, 15), new double2(0, 75) },
                    H = flat, True = 500, ExactBand = false, Want = 504.36624f,
                },
                new LenShape { Name = "polyline 2 nodes 250 m @20%", Ring = new[] { new double2(0, 0), new double2(250, 0) }, H = slope20, True = 250, ExactBand = false, Want = 250f },
            };
        }

        private static void LengthTests()
        {
            s_FingerprintOn = false;
            try
            {
                LenShape[] shapes = LengthShapes();
                SlopeGrid g = new SlopeGrid();
                SlopeResult r = new SlopeResult();
                foreach (LenShape ls in shapes)
                {
                    Scn sc = new Scn(ls.Name, "-", ls.Ring, ls.H);
                    int m = ScnStep(sc);
                    string w = "length/" + ls.Name;
                    float len0 = 0f, rect0 = 0f, axis0 = 0f;
                    bool key0 = false;
                    SlopeStatus st0 = SlopeStatus.NoCells;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int fi = 0; fi < LenFrames.Length; fi++)
                    {
                        SlopeStatus st = RunScn(sc, LenFrames[fi], m, g, r, w + " frame " + fi);
                        bool rated = st == SlopeStatus.Ok || st == SlopeStatus.ShortRun || st == SlopeStatus.Polyline;
                        if (!Check(rated, w, "frame " + fi + " status " + st + " is not a rating"))
                            continue;
                        if (ls.ExactBand)
                            Check(Math.Abs(r.LengthRect - ls.True) <= 1e-4 * ls.True, w,
                                "frame " + fi + ": LengthRect " + F(r.LengthRect) + " vs the band's centreline " + F(ls.True) + " (the closed form is exact for a constant-width band)");
                        lo = Math.Min(lo, r.Length);
                        hi = Math.Max(hi, r.Length);
                        if (fi == 0)
                        {
                            len0 = r.Length;
                            rect0 = r.LengthRect;
                            axis0 = r.AxisLength;
                            key0 = r.ZoneByFallLine;
                            st0 = st;
                        }
                    }
                    double err = (len0 - ls.True) / ls.True;
                    double errPct = Math.Round(100.0 * err, 1);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "INFO  length {0}: m={1} status {2} key {3} | Length {4:F1} m (LengthRect {5:F1}, AxisLength {6:F1}) at frame 0, {7:F1}-{8:F1} m over {9} frames | " +
                        "designed {10:F1} m, error {11}{12:F1}%{13}",
                        ls.Name, m, st0, g.PolylineMode ? "-" : key0 ? "fall line" : "height", len0, rect0, axis0, lo, hi, LenFrames.Length, ls.True,
                        errPct >= 0.0 ? "+" : "-", Math.Abs(errPct), Math.Abs(err) > 0.02 ? " (> 2 %: a documented limit, report only)" : ""));
                    if (ls.Want > 0f)
                        Check(Math.Abs(len0 - ls.Want) <= 1e-3f * ls.Want, w, "Length " + F(len0) + " vs regression value " + F(ls.Want) + " (±0.1 %)");
                    else
                        Fail(w, "no regression value recorded (Length at frame 0 = " + len0.ToString("R", CultureInfo.InvariantCulture) + ")");
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO  length definition (every PostCheck'd classification of this host): {0} ring LengthRect recomputed with a trapezoid area (worst relative {1:G3}), " +
                    "{2} polyline arc lengths (worst relative {3:G3}); AxisLength vs the oracle's brute-force key range + step on {4} oracle checks ({5} bit-equal, " +
                    "worst {6:G3} m); Length == max(LengthRect, AxisLength) asserted on every ring",
                    s_LenChecksRing, s_LenRectWorstRel, s_LenChecksLine, s_LenLineWorstRel, s_LenAxisChecks, s_LenAxisExact, s_LenAxisWorstAbs));
            }
            finally
            {
                s_FingerprintOn = true;
            }
        }

        // ================================================================== R2-i zero allocation

        private static void AllocationTests()
        {
            HeightFn mtn = Mountain();
            Frame fr = Frame.Make(3300.3, -3600.6, 41);
            double2[] big = Rect(900, 120, fr), small = Rect(300, 60, fr);
            double2[] line = { fr.World(0, 0), fr.World(250, 10) };
            double2[] dumb = Dumbbell(3300.0, -3700.0);
            Terrain t = Terrain.For(mtn, big, 60);
            Terrain td = Terrain.For(mtn, dumb, 60);
            // R2 fixer round: a 250 % face (the open last bucket's heap) and a U (several runs per row).
            Frame fc = Frame.Make(-2200.3, 1700.6, 12);
            double2[] cliff = Rect(200, 60, fc);
            Terrain tc = Terrain.For((x, z) => 1500.0 - 2.5 * fc.Along(x, z) + 4 * Math.Sin(x / 37.0), cliff, 60);
            double2[] uRing = UShape(-2600.3, -2600.7, 20, 300, 200);
            Terrain tu = Terrain.For((x, z) => 1500.0 - 0.15 * z + 3 * Math.Sin(x / 41.0), uRing, 60);
            // R2b: a cone (|mean g| < EndZoneMinMeanGrade: the height-key fallback); every fixture
            // under both connectivity rules (DiagonalRule off and on) at m = 1 and 2.
            double2[] cone = Rect(240, 240, Frame.Make(-1200.3, 2600.6, 0));
            Terrain tk = Terrain.For((x, z) => 1500.0 - 0.30 * Math.Sqrt((x + 1080.3) * (x + 1080.3) + (z - 2600.6) * (z - 2600.6)), cone, 60);
            SlopeGrid g = new SlopeGrid();
            SlopeResult r = new SlopeResult();
            float3[] pb = Pts(big, false), ps = Pts(small, false), pl = Pts(line, false), pd = Pts(dumb, false), pc = Pts(cliff, false), pu = Pts(uRing, false),
                     pk = Pts(cone, false);
            float3[][] shapes = { pb, ps, pl, pd, pc, pu, pk };
            Terrain[] terrains = { t, t, t, td, tc, tu, tk };
            bool sawFall = false, sawHeight = false;
            for (int combo = 0; combo < 4; combo++)
            {
                // grow
                int m = 1 + combo / 2;
                s_DiagOverride = combo % 2;
                for (int k = 0; k < shapes.Length; k++)
                {
                    Rate(terrains[k], shapes[k], m, g, r);
                    if (k == 4)
                        Check(r.RatingGrade >= 2.0f, "allocation", "the cliff fixture must rate in the open last bucket (rating " + P(r.RatingGrade) + ")");
                    if (!g.PolylineMode && r.ZoneByFallLine) sawFall = true;
                    if (!g.PolylineMode && !r.ZoneByFallLine && r.Status == SlopeStatus.Ok) sawHeight = true;
                }
                s_DiagOverride = -1;
            }
            Check(sawFall && sawHeight, "allocation", "the fixtures must cover both end-zone keys (fall line " + sawFall + ", height " + sawHeight + ")");
            long before = GC.GetAllocatedBytesForCurrentThread();
            int calls = 0;
            for (int rep = 0; rep < 5; rep++)
            {
                for (int combo = 0; combo < 4; combo++)
                {
                    int m = 1 + combo / 2;
                    for (int k = 0; k < shapes.Length; k++)
                    {
                        SkiSlopeClassifier.Plan(shapes[k], shapes[k].Length, Terrain.Origin, Step(m), g);
                        g.DiagonalRule = combo % 2 == 1;
                        Fill(g, terrains[k]);
                        SkiSlopeClassifier.Classify(g, r);
                        calls++;
                    }
                    SkiSlopeClassifier.StepMultiple(54321.0, Terrain.Texel, m);
                }
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(bytes == 0, "allocation", bytes + " bytes allocated by " + calls + " warmed Plan/Classify calls incl. the crux pass, the open-bucket heap, multi-run rows, " +
                                            "both end-zone keys and both connectivity rules (want 0)");
        }

        // ================================================================== TIME

        private const double TimeBudgetMs = 1.5;   // §9.4 TIME: was 1 ms before the crux pass

        private static void TimingReport()
        {
            s_OracleOn = false;
            double2[] runs0 = { new double2(2000.0, 80), new double2(2500.0, 80), new double2(2600.0, 80) };
            int[] ops = { 1, 1, 2 };
            for (int ri = 0; ri < runs0.Length; ri++)
            {
                double runLen = runs0[ri].x, runWidth = runs0[ri].y;
                int m = SkiSlopeClassifier.StepMultiple(runLen * runWidth, Terrain.Texel, 0);
                string name = F(runLen) + "x" + F(runWidth);
                Check(m == ops[ri], "time/" + name, "operates at m=" + m + ", want " + ops[ri]);
                foreach (double ang in new double[] { 0, 20, 45 })
                {
                    double rad = ang * Math.PI / 180.0, ca = Math.Cos(rad), sa = Math.Sin(rad);
                    double cx = 3000.3, cz = -4000.6;
                    HeightFn f = (x, z) => 1500 - 0.25 * ((x - cx) * ca + (z - cz) * sa) + 6 * Math.Sin(x / 90) * Math.Cos(z / 130) + 0.3 * Math.Sin(x / 5.0 * 6.283);
                    Frame fr = Frame.Make(cx, cz, ang);
                    double2[] ring = Rect(runLen, runWidth, fr);
                    Terrain t = Terrain.For(f, ring, 60);
                    float3[] pts = Pts(ring, false);
                    SlopeGrid g = new SlopeGrid();
                    SlopeResult r = new SlopeResult();
                    for (int w = 0; w < 20; w++)
                        Rate(t, pts, m, g, r);
                    const int reps = 50;
                    double tp = 0, tf = 0, tc = 0, worst = 0;
                    Stopwatch sw = new Stopwatch();
                    for (int rep = 0; rep < reps; rep++)
                    {
                        sw.Restart();
                        SkiSlopeClassifier.Plan(pts, pts.Length, Terrain.Origin, Step(m), g);
                        g.DiagonalRule = m >= 2;   // the feedback system's rule (R2b)
                        double a = sw.Elapsed.TotalMilliseconds;
                        Fill(g, t);
                        double b = sw.Elapsed.TotalMilliseconds;
                        SkiSlopeClassifier.Classify(g, r);
                        double c = sw.Elapsed.TotalMilliseconds;
                        tp += a;
                        tf += b - a;
                        tc += c - b;
                        worst = Math.Max(worst, c);
                    }
                    double txc = CruxMs(g, r, reps);
                    double totalMs = (tp + tf + tc) / reps;
                    string rule = g.DiagonalRule ? "diagonal rule" : "8-nbr";
                    if (g.DiagonalRule)
                    {
                        // R2b: the same crux pass with R2's 8-neighbour path, for the per-neighbour
                        // union's cost (report only; CruxForTest re-reads the grid's rule).
                        g.DiagonalRule = false;
                        double t8 = CruxMs(g, r, reps);
                        g.DiagonalRule = true;
                        CruxForTest(g, r, Zp.Shipped);   // restore the result of the shipped rule
                        rule += string.Format(CultureInfo.InvariantCulture, ", 8-nbr crux {0:F3}", t8);
                    }
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "TIME  {13} rot{0,2} m={1}: inside {2} need {3} bbox {4} | plan {5:F3} fill {6:F3} classify {7:F3} (crux {14:F3} = {15:F0} %; {17}, {18} key) total {8:F3} ms (worst {9:F3}) | {10} rating {11} steepest {16} valid {12}",
                        ang, m, g.InsideCells, g.NeedCells, g.Nx * g.Nz, tp / reps, tf / reps, tc / reps, totalMs, worst, r.Status, P(r.RatingGrade), r.ValidWindows,
                        name, txc, 100.0 * txc / (tc / reps), P(r.SteepestGrade), rule, r.ZoneByFallLine ? "fall-line" : "height"));
                    if (totalMs > TimeBudgetMs)
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "WARN  {3} rot{0} m={1}: plan+fill+classify mean {2:F3} ms > {4} ms budget (operating step)",
                            ang, m, totalMs, name, TimeBudgetMs));
                }
            }

            // Long thin shapes whose bbox dwarfs their Need set: a 2-node 5 km diagonal (the first
            // stroke of every new run; shoelace 0 -> m = 1) and a thin 3-node 5 km sliver ring
            // (5,000 m² -> m = 1). ~1M bbox cells each; the cost must follow Need, not the bbox.
            {
                double cx = -4000.3, cz = -4000.6, d = 5000.0 / Math.Sqrt(2.0);
                HeightFn f = (x, z) => 1500 - 0.18 * (x + z) / Math.Sqrt(2.0) + 6 * Math.Sin(x / 90) * Math.Cos(z / 130);
                double2 a = new double2(cx, cz), b = new double2(cx + d, cz + d);
                double2 mid = (a + b) * 0.5 + new double2(-2.0 / Math.Sqrt(2.0), 2.0 / Math.Sqrt(2.0));   // 2 m off the diagonal
                TimeShape("5000 diagonal 2-node", new[] { a, b }, f);
                TimeShape("5000 sliver 3-node", new[] { a, b, mid }, f);
            }

            // Concave runs (R2 fixer round, review finding classifier-concave-span-cost): every
            // lattice row of these crosses two arms, so R1's per-row first/last spans walked the
            // whole outside gap (span = 37× / 163× the inside cells for the C / 10 m U); the run
            // lists make the managed work follow the inside cells again. All operate at m = 1.
            {
                double x0 = 1000.3, z0 = -5000.6;
                HeightFn f = (x, z) => 1500 - 0.15 * (z - z0) + 6 * Math.Sin(x / 90) * Math.Cos(z / 130) + 0.3 * Math.Sin(x / 5.0 * 6.283);
                TimeShape("C 15 m wide r1500 330deg", CShape(x0 + 2000, z0 + 2000, 1500, 15, 330), f);
                TimeShape("U 10 m arms 4 km long 4 km apart", UShape(x0, z0, 10, 4000, 4000), f);
                TimeShape("U 30 m arms 2 km long 2 km apart", UShape(x0, z0, 30, 2000, 2000), f);
                TimeShape("U 60 m arms 1 km long 1 km apart", UShape(x0, z0, 60, 1000, 1000), f);
            }
            // The open last bucket's heap (every cell >= 199.9 %): a 250 % face. The 2.5 km one is a
            // stress shape (a 6 km vertical drop), the worst case of the heap, not a realistic run.
            {
                HeightFn f = (x, z) => 1500 - 2.5 * (x - 3000.3) + 6 * Math.Sin(x / 90) * Math.Cos(z / 130);
                TimeShape("cliff 250% 400x100", Rect(400, 100, Frame.Make(3000.3, -4000.6, 0)), f);
                TimeShape("cliff 250% 2500x80 (stress: every cell in the open bucket)", Rect(2500, 80, Frame.Make(3000.3, -4000.6, 0)), f);
            }
            s_OracleOn = true;
        }

        /// <summary>A C-shaped run: an annulus sector of centre radius r and width w over
        /// `sweepDeg` degrees, its gap centred on +x, 1° vertex spacing.</summary>
        private static double2[] CShape(double cx, double cz, double r, double w, double sweepDeg)
        {
            List<double2> p = new List<double2>();
            double a0 = (360.0 - sweepDeg) / 2.0, a1 = 360.0 - a0;
            for (double a = a0; a <= a1 + 1e-9; a += 1.0)
                p.Add(new double2(cx + (r + w / 2) * Math.Cos(a * Math.PI / 180.0), cz + (r + w / 2) * Math.Sin(a * Math.PI / 180.0)));
            for (double a = a1; a >= a0 - 1e-9; a -= 1.0)
                p.Add(new double2(cx + (r - w / 2) * Math.Cos(a * Math.PI / 180.0), cz + (r - w / 2) * Math.Sin(a * Math.PI / 180.0)));
            return p.ToArray();
        }

        /// <summary>A U-shaped run: two w-wide arms along +z, armLen long, `gap` m apart (inner
        /// edges), joined by a w-wide base along x at z0.</summary>
        private static double2[] UShape(double x0, double z0, double w, double armLen, double gap)
        {
            return new[]
            {
                new double2(x0, z0), new double2(x0 + gap + 2 * w, z0), new double2(x0 + gap + 2 * w, z0 + armLen), new double2(x0 + gap + w, z0 + armLen),
                new double2(x0 + gap + w, z0 + w), new double2(x0 + w, z0 + w), new double2(x0 + w, z0 + armLen), new double2(x0, z0 + armLen),
            };
        }

        /// <summary>Mean ms of the crux pass alone (CruxForTest on the result Classify just
        /// produced; idempotent), 0 in polyline mode.</summary>
        private static double CruxMs(SlopeGrid g, SlopeResult r, int reps)
        {
            if (g.PolylineMode)
                return 0.0;
            Stopwatch sw = Stopwatch.StartNew();
            for (int rep = 0; rep < reps; rep++)
                CruxForTest(g, r, Zp.Shipped);
            return sw.Elapsed.TotalMilliseconds / reps;
        }

        private static void TimeShape(string name, double2[] shape, HeightFn f)
        {
            int m = SkiSlopeClassifier.StepMultiple(0.0, Terrain.Texel, 0);   // the shoelace of both shapes is <= 5,000 m² -> 1
            if (shape.Length >= 3)
            {
                double s2 = 0.0;
                for (int k = 0; k < shape.Length; k++)
                {
                    double2 p = shape[k], q = shape[(k + 1) % shape.Length];
                    s2 += p.x * q.y - q.x * p.y;
                }
                m = SkiSlopeClassifier.StepMultiple(Math.Abs(0.5 * s2), Terrain.Texel, 0);
            }
            Terrain t = Terrain.For(f, shape, 60);
            float3[] pts = Pts(shape, false);
            SlopeGrid g = new SlopeGrid();
            SlopeResult r = new SlopeResult();
            for (int w = 0; w < 10; w++)
            {
                SkiSlopeClassifier.Plan(pts, pts.Length, Terrain.Origin, Step(m), g);
                g.DiagonalRule = m >= 2;
                FillSpans(g, t);
                SkiSlopeClassifier.Classify(g, r);
            }
            // The game's FillHeights walks only Plan's per-row Need runs: prove they cover every
            // Need node (else the game would leave NaN-poisoned or stale heights).
            int covered = 0;
            for (int j = 0; j < g.Nz; j++)
                for (int k = g.NeedRowStart[j]; k < g.NeedRowStart[j + 1]; k++)
                    for (int i = g.NeedRuns[2 * k]; i <= g.NeedRuns[2 * k + 1]; i++)
                        if (g.Need[j * g.Nx + i] != 0)
                            covered++;
            Check(covered == g.NeedCells, "time/" + name, "Need runs cover " + covered + " of " + g.NeedCells + " Need nodes");
            const int reps = 20;
            double tp = 0, tf = 0, tc = 0, worst = 0;
            Stopwatch sw = new Stopwatch();
            for (int rep = 0; rep < reps; rep++)
            {
                sw.Restart();
                SkiSlopeClassifier.Plan(pts, pts.Length, Terrain.Origin, Step(m), g);
                g.DiagonalRule = m >= 2;
                double ta = sw.Elapsed.TotalMilliseconds;
                FillSpans(g, t);
                double tb = sw.Elapsed.TotalMilliseconds;
                SkiSlopeClassifier.Classify(g, r);
                double tcl = sw.Elapsed.TotalMilliseconds;
                tp += ta;
                tf += tb - ta;
                tc += tcl - tb;
                worst = Math.Max(worst, tcl);
            }
            double txc = CruxMs(g, r, reps);
            double totalMs = (tp + tf + tc) / reps;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "TIME  {0} m={1}: inside {2} need {3} bbox {4} runs {14}/{15} | plan {5:F3} fill {6:F3} classify {7:F3} (crux {12:F3}; {16} key) total {8:F3} ms (worst {9:F3}) | {10} {11} connected {13}",
                name, m, g.InsideCells, g.NeedCells, g.Nx * g.Nz, tp / reps, tf / reps, tc / reps, totalMs, worst, r.Status, P(r.RatingGrade), txc, r.Connected,
                g.MaskRunCount, g.NeedRunCount, g.PolylineMode ? "no" : r.ZoneByFallLine ? "fall-line" : "height"));
            if (totalMs > TimeBudgetMs)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "WARN  {0} m={1}: plan+fill+classify {2:F3} ms > {3} ms budget", name, m, totalMs, TimeBudgetMs));
        }

        /// <summary>The game's fill (SkiRunDrawFeedbackSystem.FillHeights): Plan's per-row Need
        /// runs only, with the Need test kept. The classifier is linked into this assembly, so its
        /// internal run lists are visible here.</summary>
        private static void FillSpans(SlopeGrid g, Terrain t)
        {
            int nx = g.Nx, nz = g.Nz;
            for (int j = 0; j < nz; j++)
            {
                float wz = (float)(g.Origin.y + (double)(g.J0 + j) * g.Step);
                for (int k = g.NeedRowStart[j]; k < g.NeedRowStart[j + 1]; k++)
                {
                    for (int i = g.NeedRuns[2 * k]; i <= g.NeedRuns[2 * k + 1]; i++)
                    {
                        int q = j * nx + i;
                        if (g.Need[q] == 0)
                            continue;
                        float wx = (float)(g.Origin.x + (double)(g.I0 + i) * g.Step);
                        g.Heights[q] = t.Sample(wx, wz);
                    }
                }
            }
        }
    }
}
