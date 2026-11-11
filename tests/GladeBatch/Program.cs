// GladeBatch - headless invariant tests for SkiGladePlanner, SkiGladeCompose, SkiGladeHover and the tree filler (SkiGladeTreeLine, SkiGladeSpecies).
// Run (from the ModularSkiResort root): dotnet run --project tests\GladeBatch\GladeBatch.csproj -c Release -- 200
// Argument = number of seeds (default 200), or `plot <dir>` for the T157 edge band density plot (EdgeBandTests.cs).
// Prints `FAIL  fixture.params/seed: msg`, report-only TIME/WARN/INFO lines and one SUMMARY.
// Oracles are test-local and use a DIFFERENT algorithm from the planner's (winding-number point-in-polygon vs crossing number, plain
// shoelace, own segment distance, own FNV for Seed32). The test project links the planner as source (net8 here, net48 in the mod).
// Only within-runtime replay is proven. CoreCLR vs the game's Mono bit equality is unverified, so the tests assert no golden hashes.
// Statistical bounds (thin count, histograms) hold on aggregates across seeds at 3 sigma + 1. Per seed, only a loose 5 sigma + 1 holds,
// because a per-seed 3 sigma check over thousands of independent cases would fail ~0.27 % of them by chance.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace GladeBatch
{
    internal sealed class Fixture
    {
        public string Name;
        public string Family;          // fixtures of one family share the existing-tree stand
        public float3[] Ring;
        public int3[] Tris;
        public float3[][] Exclusions;
        public GladeObstacle[] Obstacles;
        public double2[] OracleRing;   // xz, closing duplicate stripped (oracle side)
        public double OracleArea;
        public double OraclePerimeter;
        public double2 Min, Max;
        public bool DensityCheck = true;
        public float3 SeedAnchor;      // first vertex of the family's first fixture: one seed per family
    }

    internal sealed class ParamSet
    {
        public string Name;
        public GladeParams P;
        public int SeedStride = 1;     // heavy sets run every Nth seed
    }

    internal static partial class Program
    {
        private const float VertexPropRadius = 3f;
        private const double Eps = 1e-3;
        private const int MaxPrinted = 250;

        private static int s_Failures;
        private static long s_Checks;
        private static long s_Plans;
        private static int s_Printed;

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length > 1 && args[0] == "plot")
            {
                // T157 before / after density plot, no tests: plot <dir> [level [bandMin bandMax noiseCell]]
                CultureInfo ic = CultureInfo.InvariantCulture;
                if (args.Length > 5)
                    EdgeBandPlot(args[1], int.Parse(args[2], ic), float.Parse(args[3], ic), float.Parse(args[4], ic), float.Parse(args[5], ic));
                else
                    EdgeBandPlot(args[1], args.Length > 2 ? int.Parse(args[2], ic) : 7);
                return 0;
            }
            int seeds = 200;
            if (args.Length > 0)
            {
                int parsed;
                if (int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                    seeds = parsed;
            }
            Stopwatch total = Stopwatch.StartNew();

            Seed32Tests();
            ThinSelectTests();
            DegenerateTests();

            List<Fixture> fixtures = BuildFixtures();
            List<ParamSet> sets = BuildParamSets();

            ThinPropertyTests(fixtures, seeds);
            PlanTests(fixtures, sets, seeds);
            LevelFixtureTests(fixtures, seeds);
            BeltGateTests(fixtures, seeds);
            AgeHistogramTest();
            TimingReport(fixtures, sets);
            AttemptCapReport();  // the worst-case attempt count and time on a giant saturated run, printed

            ComposeUnitTests(fixtures);
            HoverLogTests();
            List<SimRun> simRuns = BuildSimRuns();
            PrefixTests(fixtures, simRuns, seeds);
            ApplySimTests(simRuns, seeds);
            ComposeTimingReport(simRuns);
            PinTests(fixtures, sets, simRuns);
            FillerTests(seeds);
            EdgeBandTests(seeds);

            total.Stop();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "SUMMARY  GladeBatch seeds={0} fixtures={1} paramSets={2} plans={3} checks={4} failures={5} elapsed={6:F1}s",
                seeds, fixtures.Count, sets.Count, s_Plans, s_Checks, s_Failures, total.Elapsed.TotalSeconds));
            foreach (KeyValuePair<string, int> kv in s_FailKinds)
                Console.WriteLine("FAILKIND  " + kv.Value + "  " + kv.Key);
            Environment.ExitCode = s_Failures;
            return s_Failures;
        }

        // ================================================================== reporting

        /// <summary>Failures by kind (the message's leading CAPITALS tag, else its first words).
        /// The run prints them after SUMMARY when anything failed. So a run whose FAIL lines are capped still
        /// shows which assertion families fired (mutation proofs).</summary>
        private static readonly SortedDictionary<string, int> s_FailKinds = new SortedDictionary<string, int>(StringComparer.Ordinal);

        private static string FailKind(string where, string msg)
        {
            int colon = msg.IndexOf(':');
            if (colon > 2)
            {
                string head = msg.Substring(0, colon);
                bool caps = true;
                foreach (char ch in head)
                    caps &= ch == ' ' || ch == '/' || ch == '1' || (ch >= 'A' && ch <= 'Z');
                if (caps)
                    return head;
            }
            string scope = where.Split('/')[0];
            string[] words = msg.Split(' ');
            int nw = Math.Min(4, words.Length);
            string first = string.Join(" ", words, 0, nw);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in first)
            {
                if (!char.IsDigit(ch))
                    sb.Append(ch);
                else if (sb.Length == 0 || sb[sb.Length - 1] != 'N')
                    sb.Append('N'); // a digit run reads as one N
            }
            return "(" + (scope.Contains(".") ? scope.Substring(scope.IndexOf('.') + 1) : "sim") + ") " + sb;
        }

        private static void Fail(string where, string msg)
        {
            s_Failures++;
            string kind = FailKind(where, msg);
            int c;
            s_FailKinds.TryGetValue(kind, out c);
            s_FailKinds[kind] = c + 1;
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

        private static string F(double v)
        {
            return v.ToString("G6", CultureInfo.InvariantCulture);
        }

        // ================================================================== oracles

        private static double2[] ToXz(float3[] ring)
        {
            List<double2> v = new List<double2>();
            for (int i = 0; i < ring.Length; i++)
            {
                double2 p = new double2(ring[i].x, ring[i].z);
                if (v.Count > 0)
                {
                    double2 d = v[v.Count - 1] - p;
                    if (d.x * d.x + d.y * d.y < 1e-6)
                        continue;
                }
                v.Add(p);
            }
            while (v.Count > 1)
            {
                double2 d = v[v.Count - 1] - v[0];
                if (d.x * d.x + d.y * d.y >= 1e-6)
                    break;
                v.RemoveAt(v.Count - 1);
            }
            return v.ToArray();
        }

        private static double OracleShoelace(double2[] v)
        {
            double s = 0.0;
            for (int i = 0; i < v.Length; i++)
            {
                double2 a = v[i], b = v[(i + 1) % v.Length];
                s += a.x * b.y - b.x * a.y;
            }
            return 0.5 * s;
        }

        private static double OraclePerimeter(double2[] v)
        {
            double s = 0.0;
            for (int i = 0; i < v.Length; i++)
            {
                double2 d = v[(i + 1) % v.Length] - v[i];
                s += Math.Sqrt(d.x * d.x + d.y * d.y);
            }
            return s;
        }

        /// <summary>Winding-number point-in-polygon (Sunday's isLeft form) — independent of
        /// the planner's crossing-number test.</summary>
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
                else
                {
                    if (b.y <= p.y && isLeft < 0)
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
                double vx = b.x - a.x, vy = b.y - a.y;
                double wx = p.x - a.x, wy = p.y - a.y;
                double c1 = vx * wx + vy * wy;
                double dist;
                if (c1 <= 0)
                {
                    dist = Math.Sqrt(wx * wx + wy * wy);
                }
                else
                {
                    double c2 = vx * vx + vy * vy;
                    if (c2 <= c1)
                    {
                        double ux = p.x - b.x, uy = p.y - b.y;
                        dist = Math.Sqrt(ux * ux + uy * uy);
                    }
                    else
                    {
                        double t = c1 / c2;
                        double px = a.x + t * vx - p.x, py = a.y + t * vy - p.y;
                        dist = Math.Sqrt(px * px + py * py);
                    }
                }
                if (dist < best)
                    best = dist;
            }
            return best;
        }

        private static double Dist(float3 a, float3 b)
        {
            double dx = (double)a.x - b.x, dz = (double)a.z - b.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static float GroundY(double x, double z)
        {
            return (float)(800.0 + 0.02 * x - 0.03 * z);
        }

        private static uint OracleSeed32(float3 p, uint salt)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)Q10(p.x)) * 16777619u;
                h = (h ^ (uint)Q10(p.y)) * 16777619u;
                h = (h ^ (uint)Q10(p.z)) * 16777619u;
                h = (h ^ salt) * 16777619u;
                h ^= h >> 15;
                return h == 0u ? 0x6E624EB7u : h;
            }
        }

        private static int Q10(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
                return 0;
            return (int)Math.Round((double)(v * 10f), MidpointRounding.ToEven);
        }

        // ================================================================== fixtures

        private static float3[] MakeRing(double2[] local, double angleDeg, double2 offset)
        {
            double a = angleDeg * Math.PI / 180.0;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            float3[] r = new float3[local.Length];
            for (int i = 0; i < local.Length; i++)
            {
                double x = local[i].x * ca - local[i].y * sa + offset.x;
                double z = local[i].x * sa + local[i].y * ca + offset.y;
                r[i] = new float3((float)x, GroundY(x, z), (float)z);
            }
            return r;
        }

        private static double2[] D2(params double[] xz)
        {
            double2[] r = new double2[xz.Length / 2];
            for (int i = 0; i < r.Length; i++)
                r[i] = new double2(xz[2 * i], xz[2 * i + 1]);
            return r;
        }

        private static Fixture Fx(string name, string family, float3[] ring, int3[] tris, float3[][] excl)
        {
            Fixture f = new Fixture();
            f.Name = name;
            f.Family = family;
            f.Ring = ring;
            f.Tris = tris;
            f.Exclusions = excl;
            f.OracleRing = ToXz(ring);
            f.OracleArea = Math.Abs(OracleShoelace(f.OracleRing));
            f.OraclePerimeter = OraclePerimeter(f.OracleRing);
            f.Obstacles = new GladeObstacle[f.OracleRing.Length];
            double2 mn = new double2(double.MaxValue, double.MaxValue), mx = new double2(double.MinValue, double.MinValue);
            for (int i = 0; i < f.OracleRing.Length; i++)
            {
                double2 v = f.OracleRing[i];
                f.Obstacles[i] = new GladeObstacle { Center = new float3((float)v.x, GroundY(v.x, v.y), (float)v.y), Radius = VertexPropRadius };
                mn = math.min(mn, v);
                mx = math.max(mx, v);
            }
            f.Min = mn;
            f.Max = mx;
            return f;
        }

        private static List<Fixture> BuildFixtures()
        {
            List<Fixture> list = new List<Fixture>();
            double2 zero = new double2(0, 0);

            double2[] rect = D2(0, 0, 100, 0, 100, 300, 0, 300);
            double2[] rectCw = D2(0, 300, 100, 300, 100, 0, 0, 0);
            double2[] rectDup = D2(0, 0, 100, 0, 100, 300, 0, 300, 0, 0);
            list.Add(Fx("rect100x300", "rect", MakeRing(rect, 0, zero), null, null));
            list.Add(Fx("rect_cw", "rect", MakeRing(rectCw, 0, zero), null, null));
            list.Add(Fx("rect_dupclose", "rect", MakeRing(rectDup, 0, zero), null, null));
            list.Add(Fx("rect_tris", "rect", MakeRing(rect, 0, zero), new int3[] { new int3(0, 1, 2), new int3(0, 2, 3) }, null));

            float3[][] rectExcl = new float3[][]
            {
                MakeRing(D2(40, 140, 60, 140, 60, 160, 40, 160), 0, zero),            // island run inside
                MakeRing(D2(103, -50, 180, -50, 180, 350, 103, 350), 0, zero),       // neighbor 3 m to the east
            };
            Fixture fe = Fx("rect_excl", "rect", MakeRing(rect, 0, zero), null, rectExcl);
            fe.DensityCheck = false; // exclusions remove sampling area the target does not know about
            list.Add(fe);

            double2[] l = D2(0, 0, 100, 0, 100, 40, 40, 40, 40, 150, 0, 150);
            int3[] lFan = new int3[] { new int3(0, 1, 2), new int3(0, 2, 3), new int3(0, 3, 4), new int3(0, 4, 5) };
            list.Add(Fx("L", "L", MakeRing(l, 0, zero), null, null));
            list.Add(Fx("L_tris", "L", MakeRing(l, 0, zero), lFan, null));

            double2[] u = D2(0, 0, 120, 0, 120, 200, 80, 200, 80, 60, 40, 60, 40, 200, 0, 200);
            list.Add(Fx("U", "U", MakeRing(u, 0, zero), null, null));

            double2[] thin = D2(0, 0, 2000, 0, 2000, 8, 0, 8);
            list.Add(Fx("long2000x8_rot30", "long", MakeRing(thin, 30, zero), null, null));

            double2[] zig = D2(0, 0, 200, -100, 0, -200, 200, -300, 0, -400,
                               40, -400, 240, -300, 40, -200, 240, -100, 40, 0);
            list.Add(Fx("zigzag", "zig", MakeRing(zig, 0, zero), null, null));

            list.Add(Fx("L_far7000", "Lfar", MakeRing(l, 0, new double2(7000, 7000)), lFan, null));
            list.Add(Fx("U_far7000_cw", "Ufar", MakeRing(Reverse(u), 0, new double2(7000, 7000)), null, null));

            Fixture tiny = Fx("tiny3x3", "tiny", MakeRing(D2(0, 0, 3, 0, 3, 3, 0, 3), 0, zero), null, null);
            list.Add(tiny);

            // Seed anchor per family so cross-fixture comparisons (CW vs CCW, duplicated closing
            // vertex, supplied triangles) run with the same seed.
            Dictionary<string, float3> anchor = new Dictionary<string, float3>();
            foreach (Fixture fx in list)
            {
                if (!anchor.ContainsKey(fx.Family))
                    anchor[fx.Family] = fx.Ring[0];
                fx.SeedAnchor = anchor[fx.Family];
            }
            return list;
        }

        private static double2[] Reverse(double2[] v)
        {
            double2[] r = new double2[v.Length];
            for (int i = 0; i < v.Length; i++)
                r[i] = v[v.Length - 1 - i];
            return r;
        }

        private static GladeParams Defaults()
        {
            GladeParams p = new GladeParams();
            p.Mode = GladeMode.Thin | GladeMode.Glade;
            p.ThinFraction = 0.6f;
            p.GladeDensity = 1f / 70f;
            p.BeltDensity = 1f / 40f;
            p.MinSpacing = 5.5f;
            p.InnerMargin = 2.5f;
            p.BeltGap = 1.5f;
            p.BeltDepth = 10f;
            p.BeltCoverage = 0.55f;
            p.BeltNoiseWavelength = 40f;
            p.SpeciesCount = 3;
            p.DominantShare = 0.7f;
            p.AgeWeights = new float4(0f, 0.25f, 0.75f, 0f);
            p.MaxPlant = 300;
            p.AttemptsPerTree = 60;
            return p;
        }

        private static List<ParamSet> BuildParamSets()
        {
            List<ParamSet> s = new List<ParamSet>();

            ParamSet corridor = new ParamSet { Name = "corridor", P = Defaults() };
            corridor.P.Mode = GladeMode.Thin;
            corridor.P.ThinFraction = 1f;
            s.Add(corridor);

            ParamSet glade = new ParamSet { Name = "glade", P = Defaults() };
            s.Add(glade);

            ParamSet gladeLow = new ParamSet { Name = "gladeLow", P = Defaults() };
            gladeLow.P.GladeDensity = 1f / 150f;
            s.Add(gladeLow);

            ParamSet dense = new ParamSet { Name = "gladeDense", P = Defaults(), SeedStride = 10 };
            dense.P.Mode = GladeMode.Glade;
            dense.P.GladeDensity = 1f / 8f;
            dense.P.MaxPlant = 100000;
            s.Add(dense);

            ParamSet treeline = new ParamSet { Name = "treeline", P = Defaults() };
            treeline.P.Mode = GladeMode.Belt;
            treeline.P.ThinFraction = 0f;
            treeline.P.MinSpacing = 4.5f;
            s.Add(treeline);

            ParamSet treelineLow = new ParamSet { Name = "treelineLow", P = Defaults() };
            treelineLow.P.Mode = GladeMode.Belt;
            treelineLow.P.ThinFraction = 0f;
            treelineLow.P.MinSpacing = 4.5f;
            treelineLow.P.BeltDensity = 1f / 100f;
            s.Add(treelineLow);
            return s;
        }

        /// <summary>Existing stand for a fixture family and seed index: trees inside the polygon
        /// and up to 15 m outside it, ~1 per 190 m² of polygon area, 85 % thinnable.</summary>
        private static void MakeStand(Fixture f, int seedIndex, out float3[] existing, out bool[] thinnable)
        {
            System.Random r = new System.Random(StableHash(f.Family) ^ (seedIndex * 7919 + 17));
            int want = (int)Math.Max(10, Math.Min(800, Math.Round(f.OracleArea * 1.3 / 250.0)));
            List<float3> pts = new List<float3>();
            List<bool> fl = new List<bool>();
            double2 mn = f.Min - 15.0, mx = f.Max + 15.0;
            int tries = 0;
            while (pts.Count < want && tries < 400000)
            {
                tries++;
                double x = mn.x + r.NextDouble() * (mx.x - mn.x);
                double z = mn.y + r.NextDouble() * (mx.y - mn.y);
                double2 q = new double2(x, z);
                if (!OracleInside(f.OracleRing, q) && OracleBoundaryDistance(f.OracleRing, q) > 15.0)
                    continue;
                pts.Add(new float3((float)x, GroundY(x, z), (float)z));
                fl.Add(r.NextDouble() < 0.85);
            }
            existing = pts.ToArray();
            thinnable = fl.ToArray();
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                for (int i = 0; i < s.Length; i++)
                    h = h * 31 + s[i];
                return h;
            }
        }

        private static uint PlanSeed(Fixture f, int seedIndex)
        {
            return SkiGladePlanner.Seed32(f.SeedAnchor, (uint)seedIndex);
        }

        private static GladePlan Run(Fixture f, uint seed, GladeParams p, float3[] existing, bool[] thinnable, GladePlan into = null)
        {
            GladePlan plan = into ?? new GladePlan();
            SkiGladePlanner.Plan(f.Ring, f.Tris, seed, in p, f.Obstacles, existing, thinnable, f.Exclusions, plan);
            s_Plans++;
            return plan;
        }

        // ================================================================== unit tests

        private static void Seed32Tests()
        {
            System.Random r = new System.Random(12345);
            bool zero = false, mismatch = false;
            for (int i = 0; i < 200000; i++)
            {
                float3 p = new float3((float)(r.NextDouble() * 28000 - 14000), (float)(r.NextDouble() * 2000), (float)(r.NextDouble() * 28000 - 14000));
                uint salt = (uint)r.Next();
                uint s = SkiGladePlanner.Seed32(p, salt);
                if (s == 0u)
                    zero = true;
                if (s != OracleSeed32(p, salt))
                    mismatch = true;
            }
            Check(!zero, "seed32/random", "Seed32 returned 0");
            Check(!mismatch, "seed32/random", "Seed32 differs from the test-local FNV oracle");
            float3 a = new float3(100.02f, 50.03f, 7.01f);
            float3 b = new float3(100.04f, 50.01f, 7.03f); // same 0.1 m cells
            Check(SkiGladePlanner.Seed32(a, 3) == SkiGladePlanner.Seed32(b, 3), "seed32/quantize", "0.1 m quantization not honored");
            Check(SkiGladePlanner.Seed32(a, 3) != SkiGladePlanner.Seed32(a, 4), "seed32/salt", "salt has no effect");
            Check(SkiGladePlanner.Seed32(new float3(float.NaN, 1, 2), 0) == SkiGladePlanner.Seed32(new float3(0, 1, 2), 0), "seed32/nan", "NaN component not quantized to 0");
        }

        private static void ThinSelectTests()
        {
            System.Random r = new System.Random(777);
            bool monoFail = false, quantFail = false, yFail = false;
            for (int i = 0; i < 50000; i++)
            {
                float3 p = new float3((float)(r.NextDouble() * 4000 - 2000), 0f, (float)(r.NextDouble() * 4000 - 2000));
                uint seed = (uint)r.Next() | 1u;
                bool prev = false;
                for (int k = 0; k <= 20; k++)
                {
                    bool s = SkiGladePlanner.ThinSelect(p, seed, k / 20f);
                    if (prev && !s)
                        monoFail = true;
                    prev = s;
                }
                // same 1/8 m cell -> same answer
                float cx = (float)Math.Floor(p.x * 8f) / 8f + 0.03f, cz = (float)Math.Floor(p.z * 8f) / 8f + 0.03f;
                float3 c1 = new float3(cx, 0f, cz), c2 = new float3(cx + 0.05f, 0f, cz + 0.05f);
                if (SkiGladePlanner.ThinSelect(c1, seed, 0.5f) != SkiGladePlanner.ThinSelect(c2, seed, 0.5f))
                    quantFail = true;
                if (SkiGladePlanner.ThinSelect(p, seed, 0.5f) != SkiGladePlanner.ThinSelect(new float3(p.x, 999f, p.z), seed, 0.5f))
                    yFail = true;
            }
            // Seed sensitivity and spatial independence: at f = 0.5 the
            // selection rate is exactly 0.5 (32768/65536), so two independent verdicts disagree
            // with p = 0.5. Over 50k points σ ≈ 0.0022, ±0.01 ≈ 4.5σ.
            System.Random rs = new System.Random(4242);
            int seedDis = 0, cellDis = 0, cellDisZ = 0, meterDis = 0;
            const int nDis = 50000;
            for (int i = 0; i < nDis; i++)
            {
                float3 p = new float3((float)(rs.NextDouble() * 4000 - 2000), 0f, (float)(rs.NextDouble() * 4000 - 2000));
                uint s1 = (uint)rs.Next() | 1u;
                uint s2 = (uint)rs.Next() | 1u;
                if (s2 == s1)
                    s2 ^= 0x10u;
                if (SkiGladePlanner.ThinSelect(p, s1, 0.5f) != SkiGladePlanner.ThinSelect(p, s2, 0.5f))
                    seedDis++;
                // center of cell (i, j) vs cell (i+1, j), same seed
                float cx = ((float)Math.Floor(p.x * 8f) + 0.5f) / 8f, cz = ((float)Math.Floor(p.z * 8f) + 0.5f) / 8f;
                bool c0 = SkiGladePlanner.ThinSelect(new float3(cx, 0f, cz), s1, 0.5f);
                if (c0 != SkiGladePlanner.ThinSelect(new float3(cx + 0.125f, 0f, cz), s1, 0.5f))
                    cellDis++;
                if (c0 != SkiGladePlanner.ThinSelect(new float3(cx, 0f, cz + 0.125f), s1, 0.5f))
                    cellDisZ++;
                if (c0 != SkiGladePlanner.ThinSelect(new float3(cx + 1f, 0f, cz), s1, 0.5f))
                    meterDis++;
            }
            double seedRate = seedDis / (double)nDis, cellRate = cellDis / (double)nDis;
            double cellRateZ = cellDisZ / (double)nDis, meterRate = meterDis / (double)nDis;
            Check(Math.Abs(seedRate - 0.5) <= 0.01, "thinselect/seed", "two seeds disagree on " + F(seedRate) + " of points (expect 0.5 ± 0.01)");
            // Spatial independence. Before the Fmix32 finalizer math.hash(uint4) was linear in
            // its lanes: x-neighbors disagreed 0.477, z-neighbors 0.431, 1 m apart 0.171.
            // The result was a stripe lattice instead of a random 60 % felling.
            Check(Math.Abs(cellRate - 0.5) <= 0.01, "thinselect/spatial_x", "neighboring 1/8 m cells (x) disagree on " + F(cellRate) + " (expect 0.5 ± 0.01)");
            Check(Math.Abs(cellRateZ - 0.5) <= 0.01, "thinselect/spatial_z", "neighboring 1/8 m cells (z) disagree on " + F(cellRateZ) + " (expect 0.5 ± 0.01)");
            Check(Math.Abs(meterRate - 0.5) <= 0.01, "thinselect/spatial_1m", "points 1 m apart disagree on " + F(meterRate) + " (expect 0.5 ± 0.01)");
            Console.WriteLine("INFO  thinselect: seed disagreement " + F(seedRate) + ", neighbor-cell disagreement x " + F(cellRate) + " z " + F(cellRateZ) + ", 1 m apart " + F(meterRate));

            Check(!monoFail, "thinselect/monotone", "a larger fraction deselected a tree");
            Check(!quantFail, "thinselect/quantize", "two points in one 1/8 m cell disagree");
            Check(!yFail, "thinselect/y", "selection depends on y");
            float3 q = new float3(12.3f, 0f, 45.6f);
            Check(!SkiGladePlanner.ThinSelect(q, 99, 0f), "thinselect/f0", "f=0 selected");
            Check(SkiGladePlanner.ThinSelect(q, 99, 1f), "thinselect/f1", "f=1 not selected");
            Check(!SkiGladePlanner.ThinSelect(q, 99, float.NaN), "thinselect/fnan", "f=NaN selected");
            Check(!SkiGladePlanner.ThinSelect(new float3(float.NaN, 0, 1), 99, 1f), "thinselect/posnan", "NaN position selected");
            Check(SkiGladePlanner.ThinSelect(new float3(-0f, 0, 5f), 7, 0.5f) == SkiGladePlanner.ThinSelect(new float3(0f, 0, 5f), 7, 0.5f), "thinselect/negzero", "-0 and +0 split");
        }

        private static void DegenerateTests()
        {
            GladeParams p = Defaults();
            p.Mode = GladeMode.Thin | GladeMode.Glade | GladeMode.Belt;
            float3[] ex = new float3[] { new float3(5, 0, 5), new float3(float.NaN, 0, 1) };

            Degenerate("null_ring", null, null, p, GladeStatus.TooFewVertices, ex);
            Degenerate("empty_ring", new float3[0], null, p, GladeStatus.TooFewVertices, ex);
            Degenerate("two_vertices", Pts(0, 0, 10, 0), null, p, GladeStatus.TooFewVertices, ex);
            Degenerate("same_point", Pts(1, 1, 1, 1, 1, 1, 1, 1), null, p, GladeStatus.TooFewVertices, ex);
            Degenerate("two_plus_closing", Pts(0, 0, 10, 0, 0, 0), null, p, GladeStatus.TooFewVertices, ex);
            Degenerate("collinear", Pts(0, 0, 10, 0, 20, 0, 30, 0), null, p, GladeStatus.ZeroArea, ex);
            Degenerate("sub_m2", Pts(0, 0, 0.5f, 0, 0.5f, 0.5f, 0, 0.5f), null, p, GladeStatus.ZeroArea, ex);
            Degenerate("nan_vertex", Pts(0, 0, 10, 0, float.NaN, 10, 0, 10), null, p, GladeStatus.NonFinite, ex);
            Degenerate("inf_vertex", Pts(0, 0, float.PositiveInfinity, 0, 10, 10, 0, 10), null, p, GladeStatus.NonFinite, ex);
            Degenerate("nan_two_vertices", Pts(float.NaN, 0, 1, 1), null, p, GladeStatus.NonFinite, ex);
            Degenerate("bowtie_symmetric", Pts(0, 0, 10, 10, 10, 0, 0, 10), null, p, GladeStatus.ZeroArea, ex);
            Degenerate("bowtie_asymmetric", Pts(0, 0, 40, 40, 40, 0, 0, 10), null, p, null, ex);
            float3[] sq = Pts(0, 0, 50, 0, 50, 50, 0, 50);
            Degenerate("tris_out_of_range", sq, new int3[] { new int3(0, 1, 2), new int3(0, 2, 4) }, p, GladeStatus.BadTriangles, ex);
            Degenerate("tris_negative", sq, new int3[] { new int3(0, 1, 2), new int3(0, -1, 3) }, p, GladeStatus.BadTriangles, ex);
            Degenerate("tris_half_cover", sq, new int3[] { new int3(0, 1, 2) }, p, GladeStatus.BadTriangles, ex);
            Degenerate("tris_empty_array", sq, new int3[0], p, GladeStatus.Ok, ex);

            GladeParams bad = p;
            bad.GladeDensity = float.NaN;
            bad.BeltDensity = float.PositiveInfinity;
            bad.MinSpacing = -1f;
            bad.InnerMargin = float.NaN;
            bad.AttemptsPerTree = 0;
            bad.SpeciesCount = 0;
            bad.AgeWeights = new float4(0, 0, 0, 0);
            bad.BeltCoverage = 5f;
            bad.BeltNoiseWavelength = 0f;
            bad.BeltDepth = 1f; // < gap -> no band
            bad.ThinFraction = float.NaN;
            Degenerate("params_garbage", sq, null, bad, null, ex);

            GladeParams neg = p;
            neg.MaxPlant = -5;
            Degenerate("maxplant_negative", sq, null, neg, null, ex);

            GladeParams huge = p;
            huge.GladeDensity = 1e30f;
            huge.MinSpacing = 0f;
            huge.AttemptsPerTree = int.MaxValue;
            GladePlan hp = Degenerate("density_huge", sq, null, huge, GladeStatus.Clamped, ex);
            if (hp != null)
                Check(hp.Planted.Count <= huge.MaxPlant, "degenerate/density_huge", "count " + hp.Planted.Count + " > MaxPlant");

            // MaxPlant -> Clamped on a sane polygon
            GladeParams clamp = Defaults();
            clamp.Mode = GladeMode.Glade;
            clamp.GladeDensity = 1f / 60f;
            clamp.MaxPlant = 20;
            GladePlan cp = Degenerate("maxplant_clamp", sq, null, clamp, GladeStatus.Clamped, null);
            if (cp != null)
                Check(cp.Planted.Count <= 20 && cp.TargetGlade == 20, "degenerate/maxplant_clamp", "count " + cp.Planted.Count + " target " + cp.TargetGlade);

            // garbage side inputs: short thinnable array, NaN obstacle, broken exclusion rings, null output
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                float3[] ring = Pts(0, 0, 80, 0, 80, 80, 0, 80);
                float3[] exist = new float3[] { new float3(10, 0, 10), new float3(20, 0, 20), new float3(float.PositiveInfinity, 0, 0), new float3(30, 0, 30) };
                bool[] th = new bool[] { true };
                GladeObstacle[] obs = new GladeObstacle[] { new GladeObstacle { Center = new float3(float.NaN, 0, 0), Radius = 3 }, new GladeObstacle { Center = new float3(40, 0, 40), Radius = float.NaN }, new GladeObstacle { Center = new float3(40, 0, 40), Radius = -3 } };
                float3[][] excl = new float3[][] { null, new float3[0], Pts(0, 0, float.NaN, 0, 1, 1), Pts(0, 0, 1, 0) };
                GladeParams gp = Defaults();
                gp.Mode = GladeMode.Thin | GladeMode.Glade | GladeMode.Belt;
                gp.ThinFraction = 1f;
                GladePlan plan = new GladePlan();
                SkiGladePlanner.Plan(ring, null, 5u, in gp, obs, exist, th, excl, plan);
                s_Plans++;
                Check(plan.Thinned.Count == 1 && plan.Thinned[0] == 0, "degenerate/short_thinnable", "out-of-range thinnable must mean NOT thinnable; thinned=" + plan.Thinned.Count);
                SkiGladePlanner.Plan(ring, null, 5u, in gp, obs, exist, th, excl, null);
                s_Plans++;
                GladeParams none = gp;
                none.Mode = GladeMode.None;
                SkiGladePlanner.Plan(ring, null, 5u, in none, null, null, null, null, plan);
                s_Plans++;
                Check(plan.Status == GladeStatus.Ok && plan.Planted.Count == 0 && plan.Thinned.Count == 0, "degenerate/mode_none", "Mode None produced output");
                Check(sw.ElapsedMilliseconds < 5000, "degenerate/side_inputs", "took " + sw.ElapsedMilliseconds + " ms");
            }
            catch (Exception e)
            {
                Fail("degenerate/side_inputs", "exception " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static float3[] Pts(params float[] xz)
        {
            float3[] r = new float3[xz.Length / 2];
            for (int i = 0; i < r.Length; i++)
                r[i] = new float3(xz[2 * i], 0f, xz[2 * i + 1]);
            return r;
        }

        private static GladePlan Degenerate(string name, float3[] ring, int3[] tris, GladeParams p, GladeStatus? expect, float3[] existing)
        {
            string where = "degenerate/" + name;
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                GladePlan plan = new GladePlan();
                plan.Planted.Add(new PlantedTree()); // stale content must be cleared
                plan.Thinned.Add(42);
                GladeStatus st = SkiGladePlanner.Plan(ring, tris, 0u, in p, null, existing, null, null, plan);
                s_Plans++;
                sw.Stop();
                Check(st == plan.Status, where, "returned status " + st + " != plan.Status " + plan.Status);
                if (expect.HasValue)
                    Check(st == expect.Value, where, "status " + st + ", expected " + expect.Value);
                if (st != GladeStatus.Ok && st != GladeStatus.Clamped)
                    Check(plan.Planted.Count == 0 && plan.Thinned.Count == 0, where, "degenerate status " + st + " with non-empty lists");
                Check(sw.ElapsedMilliseconds < 5000, where, "took " + sw.ElapsedMilliseconds + " ms (hang guard)");
                for (int i = 0; i < plan.Planted.Count; i++)
                {
                    PlantedTree t = plan.Planted[i];
                    bool ok = math.isfinite(t.Position.x) && math.isfinite(t.Position.y) && math.isfinite(t.Position.z)
                              && t.Rotation >= 0f && t.Rotation < 6.2831855f && t.Species >= 0 && IsAgeConst(t.Age);
                    if (!Check(ok, where, "bad tree " + i))
                        break;
                }
                return plan;
            }
            catch (Exception e)
            {
                Fail(where, "exception " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        private static bool IsAgeConst(float a)
        {
            return a == SkiGladePlanner.AgeSapling || a == SkiGladePlanner.AgeYoung || a == SkiGladePlanner.AgeMature || a == SkiGladePlanner.AgeElderly;
        }

        // ================================================================== thinning properties

        private sealed class ThinAgg
        {
            public long N;
            public long K;
            public double Var;
        }

        private static void ThinPropertyTests(List<Fixture> fixtures, int seeds)
        {
            float[] fr = new float[] { 0f, 0.3f, 0.6f, 1f };
            GladeParams p = Defaults();
            p.Mode = GladeMode.Thin;
            foreach (Fixture f in fixtures)
            {
                ThinAgg[] agg = new ThinAgg[fr.Length];
                for (int k = 0; k < fr.Length; k++)
                    agg[k] = new ThinAgg();
                for (int s = 0; s < seeds; s++)
                {
                    string where = f.Name + ".thin/" + s;
                    float3[] ex;
                    bool[] th;
                    MakeStand(f, s, out ex, out th);
                    uint seed = PlanSeed(f, s);
                    int nInside = 0;
                    bool[] inside = new bool[ex.Length];
                    for (int i = 0; i < ex.Length; i++)
                    {
                        inside[i] = OracleInside(f.OracleRing, new double2(ex[i].x, ex[i].z));
                        if (inside[i] && th[i])
                            nInside++;
                    }

                    List<int>[] sets = new List<int>[fr.Length];
                    for (int k = 0; k < fr.Length; k++)
                    {
                        p.ThinFraction = fr[k];
                        GladePlan plan = Run(f, seed, p, ex, th);
                        sets[k] = new List<int>(plan.Thinned);
                        // sorted, unique, in range, thinnable, inside, oracle-consistent
                        bool okOrder = true;
                        for (int i = 0; i < plan.Thinned.Count; i++)
                        {
                            int idx = plan.Thinned[i];
                            if (idx < 0 || idx >= ex.Length || (i > 0 && plan.Thinned[i - 1] >= idx) || !th[idx] || !inside[idx])
                                okOrder = false;
                        }
                        Check(okOrder, where, "f=" + F(fr[k]) + " thinned list not ascending/unique/in-range/thinnable/inside");
                        int oracleCount = 0;
                        bool oracleOk = true;
                        HashSet<int> got = new HashSet<int>(plan.Thinned);
                        for (int i = 0; i < ex.Length; i++)
                        {
                            bool want = th[i] && inside[i] && SkiGladePlanner.ThinSelect(ex[i], seed, fr[k]);
                            if (want)
                                oracleCount++;
                            if (want != got.Contains(i))
                                oracleOk = false;
                        }
                        Check(oracleOk && oracleCount == plan.Thinned.Count, where, "f=" + F(fr[k]) + " thinned set != oracle (thinnable ∧ inside ∧ ThinSelect)");
                        double fk = fr[k];
                        double mean = fk * nInside;
                        double sd = Math.Sqrt(nInside * fk * (1 - fk));
                        Check(Math.Abs(plan.Thinned.Count - mean) <= 5 * sd + 1, where,
                            "f=" + F(fk) + " count " + plan.Thinned.Count + " vs f·N " + F(mean) + " beyond 5σ+1 (σ=" + F(sd) + ")");
                        agg[k].N += nInside;
                        agg[k].K += plan.Thinned.Count;
                        agg[k].Var += nInside * fk * (1 - fk);

                        // "seed+1 differs", for thinning (the PlanTests branch only
                        // reaches it when nothing is planted, which no default set does)
                        if (fk > 0.0 && fk < 1.0 && sets[k].Count >= 10)
                        {
                            GladePlan other = Run(f, seed + 1u, p, ex, th);
                            Check(!SameInts(sets[k], other.Thinned), where, "f=" + F(fk) + " seed+1 thinned the same set");
                        }
                    }
                    Check(sets[0].Count == 0, where, "f=0 thinned " + sets[0].Count);
                    Check(sets[3].Count == nInside, where, "f=1 thinned " + sets[3].Count + " of " + nInside);
                    Check(IsSubset(sets[1], sets[2]) && IsSubset(sets[2], sets[3]), where, "not monotone (f2 > f1 must thin a superset)");

                    // idempotent: thin survivors of f=0.6 again at 0.6 -> nothing new
                    float3[] sEx;
                    bool[] sTh;
                    int[] map;
                    Survivors(ex, th, sets[2], out sEx, out sTh, out map);
                    p.ThinFraction = 0.6f;
                    GladePlan again = Run(f, seed, p, sEx, sTh);
                    Check(again.Thinned.Count == 0, where, "second pass at f=0.6 removed " + again.Thinned.Count);

                    // extra share: survivors of 0.3, thinned at 0.6 == S(0.6) \ S(0.3)
                    Survivors(ex, th, sets[1], out sEx, out sTh, out map);
                    GladePlan extra = Run(f, seed, p, sEx, sTh);
                    HashSet<int> extraOrig = new HashSet<int>();
                    foreach (int i in extra.Thinned)
                        extraOrig.Add(map[i]);
                    HashSet<int> expectExtra = new HashSet<int>(sets[2]);
                    expectExtra.ExceptWith(sets[1]);
                    Check(extraOrig.SetEquals(expectExtra), where, "0.3 then 0.6 did not remove exactly S(0.6)\\S(0.3)");

                    // permutation invariance
                    System.Random pr = new System.Random(s * 31 + 5);
                    int[] perm = new int[ex.Length];
                    for (int i = 0; i < perm.Length; i++)
                        perm[i] = i;
                    for (int i = perm.Length - 1; i > 0; i--)
                    {
                        int j = pr.Next(i + 1);
                        int tmp = perm[i];
                        perm[i] = perm[j];
                        perm[j] = tmp;
                    }
                    float3[] pEx = new float3[ex.Length];
                    bool[] pTh = new bool[ex.Length];
                    for (int i = 0; i < perm.Length; i++)
                    {
                        pEx[i] = ex[perm[i]];
                        pTh[i] = th[perm[i]];
                    }
                    GladePlan permuted = Run(f, seed, p, pEx, pTh);
                    HashSet<int> permOrig = new HashSet<int>();
                    foreach (int i in permuted.Thinned)
                        permOrig.Add(perm[i]);
                    Check(permOrig.SetEquals(sets[2]), where, "thinning is not permutation-invariant");
                }
                for (int k = 1; k < fr.Length - 1; k++)
                {
                    double mean = fr[k] * (double)agg[k].N;
                    double bound = 3 * Math.Sqrt(agg[k].Var) + 1;
                    Check(Math.Abs(agg[k].K - mean) <= bound, f.Name + ".thin/aggregate",
                        "f=" + F(fr[k]) + " Σcount " + agg[k].K + " vs f·ΣN " + F(mean) + " beyond 3σ+1 = " + F(bound));
                }
            }
        }

        private static bool IsSubset(List<int> a, List<int> b)
        {
            HashSet<int> hb = new HashSet<int>(b);
            foreach (int x in a)
            {
                if (!hb.Contains(x))
                    return false;
            }
            return true;
        }

        private static void Survivors(float3[] ex, bool[] th, List<int> thinned, out float3[] sEx, out bool[] sTh, out int[] map)
        {
            HashSet<int> t = new HashSet<int>(thinned);
            List<float3> e = new List<float3>();
            List<bool> f = new List<bool>();
            List<int> m = new List<int>();
            for (int i = 0; i < ex.Length; i++)
            {
                if (t.Contains(i))
                    continue;
                e.Add(ex[i]);
                f.Add(th[i]);
                m.Add(i);
            }
            sEx = e.ToArray();
            sTh = f.ToArray();
            map = m.ToArray();
        }

        // ================================================================== plan invariants

        private sealed class Hist
        {
            public long[] Species = new long[8];
            public long[] Ages = new long[4];
            public long[] RotQuad = new long[4];
            public long Trees;
        }

        private static void PlanTests(List<Fixture> fixtures, List<ParamSet> sets, int seeds)
        {
            foreach (ParamSet ps in sets)
            {
                Hist hist = new Hist();
                bool hasGlade = (ps.P.Mode & GladeMode.Glade) != 0;
                for (int s = 0; s < seeds; s += ps.SeedStride)
                {
                    GladePlan rectPlan = null;
                    foreach (Fixture f in fixtures)
                    {
                        string where = f.Name + "." + ps.Name + "/" + s;
                        float3[] ex;
                        bool[] th;
                        MakeStand(f, s, out ex, out th);
                        uint seed = PlanSeed(f, s);
                        GladePlan a;
                        try
                        {
                            a = Run(f, seed, ps.P, ex, th);
                        }
                        catch (Exception e)
                        {
                            Fail(where, "exception " + e.GetType().Name + ": " + e.Message);
                            continue;
                        }
                        Check(a.Status == GladeStatus.Ok || a.Status == GladeStatus.Clamped, where, "status " + a.Status);
                        CheckPlan(f, ps.P, ex, th, a, where, hist);

                        // replay into a reused plan that held a different plan
                        GladePlan reuse = Run(f, seed ^ 0x5555u, ps.P, ex, th);
                        Run(f, seed, ps.P, ex, th, reuse);
                        string diff = Diff(a, reuse);
                        Check(diff == null, where, "replay not bit-identical: " + diff);

                        // seed + 1 differs
                        GladePlan b = Run(f, seed + 1u, ps.P, ex, th);
                        if (a.Planted.Count > 0 && b.Planted.Count > 0)
                            Check(Diff(a, b) != null, where, "seed+1 produced an identical plan");
                        else if (a.Planted.Count == 0 && b.Planted.Count == 0 && ps.P.ThinFraction > 0f && ps.P.ThinFraction < 1f && a.Thinned.Count >= 10)
                            Check(!SameInts(a.Thinned, b.Thinned), where, "seed+1 thinned the same set");

                        // phase order: thinning is unaffected by planting layers
                        if ((ps.P.Mode & GladeMode.Thin) != 0 && ps.P.Mode != GladeMode.Thin)
                        {
                            GladeParams tp = ps.P;
                            tp.Mode = GladeMode.Thin;
                            GladePlan t = Run(f, seed, tp, ex, th);
                            Check(SameInts(a.Thinned, t.Thinned), where, "Thinned changes when Glade/Belt are on");
                        }

                        // toggling Belt never moves a glade tree
                        if (hasGlade)
                        {
                            GladeParams bp = ps.P;
                            bp.Mode |= GladeMode.Belt;
                            GladePlan withBelt = Run(f, seed, bp, ex, th);
                            CheckPlan(f, bp, ex, th, withBelt, where + "+belt", null);
                            bool same = SameInts(a.Thinned, withBelt.Thinned) && withBelt.Planted.Count >= a.Planted.Count;
                            for (int i = 0; same && i < a.Planted.Count; i++)
                                same = SameTree(a.Planted[i], withBelt.Planted[i]);
                            for (int i = a.Planted.Count; same && i < withBelt.Planted.Count; i++)
                                same = withBelt.Planted[i].Layer == 1;
                            Check(same, where, "adding Belt moved/changed glade trees or thinning");
                        }

                        // cross-fixture: duplicated closing vertex == plain ring. CW thins like CCW
                        if (f.Name == "rect100x300")
                            rectPlan = a;
                        else if (rectPlan != null && f.Name == "rect_dupclose")
                            Check(Diff(rectPlan, a) == null, where, "duplicated closing vertex changed the plan: " + Diff(rectPlan, a));
                        else if (rectPlan != null && (f.Name == "rect_cw" || f.Name == "rect_tris"))
                            Check(SameInts(rectPlan.Thinned, a.Thinned) && Math.Abs(rectPlan.Area - a.Area) < 1e-3, where, "winding/triangles changed thinning or Area");
                    }
                }
                ReportHist(ps, hist);
            }
        }

        private static void CheckPlan(Fixture f, GladeParams p, float3[] ex, bool[] th, GladePlan plan, string where, Hist hist)
        {
            Check(Math.Abs(plan.Area - f.OracleArea) <= 1e-4 * f.OracleArea + 1e-3, where, "Area " + F(plan.Area) + " vs oracle " + F(f.OracleArea));
            bool wantBelt = (p.Mode & GladeMode.Belt) != 0;
            bool wantGlade = (p.Mode & GladeMode.Glade) != 0;
            double band = Math.Max(0.0, (double)p.BeltDepth - p.BeltGap);
            if (wantBelt)
                Check(Math.Abs(plan.BeltArea - f.OraclePerimeter * band) <= 1e-4 * f.OraclePerimeter * band + 1e-3, where, "BeltArea " + F(plan.BeltArea) + " vs perimeter·band " + F(f.OraclePerimeter * band));
            Check(plan.Planted.Count <= Math.Max(0, p.MaxPlant), where, "planted " + plan.Planted.Count + " > MaxPlant");

            HashSet<int> thinned = new HashSet<int>(plan.Thinned);
            List<float3> survivors = new List<float3>();
            int survivorsInside = 0;
            for (int i = 0; i < ex.Length; i++)
            {
                if (thinned.Contains(i))
                    continue;
                survivors.Add(ex[i]);
                if (OracleInside(f.OracleRing, new double2(ex[i].x, ex[i].z)))
                    survivorsInside++;
            }

            int gladeCount = 0, beltCount = 0;
            double s = p.MinSpacing;
            int errors = 0;
            for (int i = 0; i < plan.Planted.Count && errors < 3; i++)
            {
                PlantedTree t = plan.Planted[i];
                double2 q = new double2(t.Position.x, t.Position.z);
                if (t.Layer == 0)
                {
                    gladeCount++;
                    if (gladeCount != i + 1) { Fail(where, "glade tree after a belt tree (layer order)"); errors++; }
                    if (!OracleInside(f.OracleRing, q)) { Fail(where, "glade tree " + i + " outside polygon (winding oracle)"); errors++; }
                    double bd = OracleBoundaryDistance(f.OracleRing, q);
                    if (bd < p.InnerMargin - Eps) { Fail(where, "glade tree " + i + " " + F(bd) + " m from boundary < InnerMargin"); errors++; }
                    float gy = GroundY(t.Position.x, t.Position.z);
                    if (Math.Abs(t.Position.y - gy) > 0.05) { Fail(where, "glade tree " + i + " y " + F(t.Position.y) + " not barycentric (" + F(gy) + ")"); errors++; }
                }
                else if (t.Layer == 1)
                {
                    beltCount++;
                    if (OracleInside(f.OracleRing, q)) { Fail(where, "belt tree " + i + " inside polygon"); errors++; }
                    double bd = OracleBoundaryDistance(f.OracleRing, q);
                    if (bd < p.BeltGap - Eps || bd > p.BeltDepth + Eps) { Fail(where, "belt tree " + i + " boundary distance " + F(bd) + " outside [gap, depth]"); errors++; }
                }
                else
                {
                    Fail(where, "layer " + t.Layer);
                    errors++;
                }
                if (f.Exclusions != null)
                {
                    foreach (float3[] er in f.Exclusions)
                    {
                        if (OracleInside(ToXz(er), q)) { Fail(where, "tree " + i + " inside an exclusion ring"); errors++; }
                    }
                }
                foreach (GladeObstacle o in f.Obstacles)
                {
                    double d = Dist(t.Position, o.Center);
                    if (d < o.Radius + s / 2 - Eps) { Fail(where, "tree " + i + " " + F(d) + " m from obstacle < r + s/2"); errors++; break; }
                }
                if (!(t.Rotation >= 0f && t.Rotation < 6.2831855f)) { Fail(where, "rotation " + F(t.Rotation)); errors++; }
                if (t.Species < 0 || t.Species >= Math.Max(1, p.SpeciesCount)) { Fail(where, "species " + t.Species); errors++; }
                if (!IsAgeConst(t.Age)) { Fail(where, "age " + F(t.Age) + " not a vanilla constant"); errors++; }
                if (hist != null)
                {
                    hist.Trees++;
                    if (t.Species >= 0 && t.Species < hist.Species.Length)
                        hist.Species[t.Species]++;
                    hist.Ages[t.Age == SkiGladePlanner.AgeSapling ? 0 : t.Age == SkiGladePlanner.AgeYoung ? 1 : t.Age == SkiGladePlanner.AgeMature ? 2 : 3]++;
                    int quad = (int)(t.Rotation / (Math.PI / 2));
                    hist.RotQuad[Math.Min(3, Math.Max(0, quad))]++;
                }
            }
            s_Checks += plan.Planted.Count;

            // spacing: planted vs planted and planted vs survivors (brute force O(n·(n+m)))
            double minFound = double.MaxValue;
            string minPair = null;
            for (int i = 0; i < plan.Planted.Count; i++)
            {
                float3 a = plan.Planted[i].Position;
                for (int j = i + 1; j < plan.Planted.Count; j++)
                {
                    double d = Dist(a, plan.Planted[j].Position);
                    if (d < minFound) { minFound = d; minPair = i + "-" + j; }
                }
                for (int j = 0; j < survivors.Count; j++)
                {
                    double d = Dist(a, survivors[j]);
                    if (d < minFound) { minFound = d; minPair = i + "-survivor" + j; }
                }
            }
            Check(minFound >= s - 1e-4, where, "spacing " + F(minFound) + " < MinSpacing " + F(s) + " (" + minPair + ")");

            // RawTarget* = the unclamped target (floored at 0) from the oracle's own
            // area / inside count / perimeter. Clamped mirrors Status == Clamped.
            Check(plan.Clamped == (plan.Status == GladeStatus.Clamped), where, "GladePlan.Clamped " + plan.Clamped + " vs Status " + plan.Status);
            if (wantGlade && f.DensityCheck)
            {
                double rawG = Math.Max(0.0, Math.Floor(p.GladeDensity * f.OracleArea + 0.5) - survivorsInside);
                Check(Math.Abs(plan.RawTargetGlade - rawG) <= 1.0, where, "RawTargetGlade " + plan.RawTargetGlade + " vs oracle " + F(rawG));
                Check(plan.RawTargetGlade >= plan.TargetGlade, where, "RawTargetGlade " + plan.RawTargetGlade + " < TargetGlade " + plan.TargetGlade);
            }
            if (!wantGlade)
                Check(plan.RawTargetGlade == 0, where, "RawTargetGlade " + plan.RawTargetGlade + " without a glade layer");
            if (wantBelt && f.DensityCheck)
            {
                double rawB = Math.Max(0.0, Math.Floor(p.BeltDensity * p.BeltCoverage * f.OraclePerimeter * band + 0.5));
                Check(Math.Abs(plan.RawTargetBelt - rawB) <= 1.0, where, "RawTargetBelt " + plan.RawTargetBelt + " vs oracle " + F(rawB));
                Check(plan.RawTargetBelt >= plan.TargetBelt, where, "RawTargetBelt " + plan.RawTargetBelt + " < TargetBelt " + plan.TargetBelt);
            }
            if (!wantBelt)
                Check(plan.RawTargetBelt == 0, where, "RawTargetBelt " + plan.RawTargetBelt + " without a belt layer");

            // density
            if (f.DensityCheck)
            {
                if (wantGlade)
                {
                    double raw = Math.Floor(p.GladeDensity * f.OracleArea + 0.5) - survivorsInside;
                    double expected = Math.Max(0.0, Math.Min(raw, p.MaxPlant));
                    DensityCheck(where + "[glade]", p.GladeDensity, s, gladeCount, expected, plan.Saturated, plan.TargetGlade);
                    if (raw > p.MaxPlant + 1)
                        Check(plan.Status == GladeStatus.Clamped, where, "glade target " + F(raw) + " > MaxPlant but Status " + plan.Status);
                }
                if (wantBelt)
                {
                    double raw = Math.Floor(p.BeltDensity * p.BeltCoverage * f.OraclePerimeter * band + 0.5);
                    double left = Math.Max(0, p.MaxPlant - gladeCount);
                    double expected = Math.Max(0.0, Math.Min(raw, left));
                    DensityCheck(where + "[belt]", p.BeltDensity, s, beltCount, expected, plan.Saturated, plan.TargetBelt);
                    if (raw > left + 1)
                        Check(plan.Status == GladeStatus.Clamped, where, "belt target " + F(raw) + " > budget but Status " + plan.Status);
                }
            }
        }

        private static void DensityCheck(string where, double density, double s, int count, double expected, bool saturated, int planTarget)
        {
            double tol = Math.Max(0.2 * expected, 1.0);
            Check(Math.Abs(planTarget - expected) <= 1.0, where, "planner target " + planTarget + " vs oracle " + F(expected));
            if (density * s * s <= 0.35)
            {
                Check(Math.Abs(count - expected) <= tol, where, "count " + count + " vs expected " + F(expected) + " beyond ±20 % (feasible region, saturated=" + saturated + ")");
            }
            else
            {
                Check(count <= expected + 1.0, where, "count " + count + " > target " + F(expected));
                Check(saturated || Math.Abs(count - expected) <= tol, where, "infeasible region: count " + count + " vs " + F(expected) + " but not Saturated");
            }
        }

        private static void ReportHist(ParamSet ps, Hist h)
        {
            if (h.Trees < 500)
            {
                Console.WriteLine("INFO  " + ps.Name + ": " + h.Trees + " trees, histograms skipped");
                return;
            }
            double n = h.Trees;
            GladeParams p = ps.P;
            string w = "hist." + ps.Name;
            double d0 = h.Species[0] / n;
            Check(Math.Abs(d0 - p.DominantShare) <= 0.05, w, "dominant share " + F(d0) + " vs " + F(p.DominantShare));
            for (int i = 1; i < p.SpeciesCount; i++)
            {
                double si = h.Species[i] / n;
                double want = (1.0 - p.DominantShare) / (p.SpeciesCount - 1);
                Check(Math.Abs(si - want) <= 0.05, w, "species " + i + " share " + F(si) + " vs " + F(want));
            }
            float sum = p.AgeWeights.x + p.AgeWeights.y + p.AgeWeights.z + p.AgeWeights.w;
            float[] wts = new float[] { p.AgeWeights.x / sum, p.AgeWeights.y / sum, p.AgeWeights.z / sum, p.AgeWeights.w / sum };
            for (int i = 0; i < 4; i++)
            {
                double ai = h.Ages[i] / n;
                if (wts[i] == 0f)
                    Check(h.Ages[i] == 0, w, "age bucket " + i + " has weight 0 but " + h.Ages[i] + " trees");
                else
                    Check(Math.Abs(ai - wts[i]) <= 0.05, w, "age bucket " + i + " share " + F(ai) + " vs " + F(wts[i]));
            }
            for (int i = 0; i < 4; i++)
            {
                double ri = h.RotQuad[i] / n;
                Check(Math.Abs(ri - 0.25) <= 0.03, w, "rotation quadrant " + i + " share " + F(ri));
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  {0}: {1} trees; species {2:F3}/{3:F3}/{4:F3}; ages {5:F3}/{6:F3}/{7:F3}/{8:F3}",
                ps.Name, h.Trees, h.Species[0] / n, h.Species[1] / n, h.Species[2] / n, h.Ages[0] / n, h.Ages[1] / n, h.Ages[2] / n, h.Ages[3] / n));
        }

        // ================================================================== tool composition (Compose)

        /// <summary>Every level on every static fixture geometry (CW, duplicated closing vertex, supplied triangles, exclusion
        /// rings, non-convex U / zigzag, the 7 km offset, the 8 m strip, a 3 x 3 m square). Each case runs from the fixture's stand (85 %
        /// thinnable) with no members, for both targets. The test judges the result against test-local oracles:
        /// - Too many: Fell is exactly the top W - max(0, T - X) by the documented rank.
        /// - Too few: Plant is the first T - N slots of P. P obeys every planner
        ///   invariant against the WHOLE stand (nothing assumed felled, CheckPlan).
        /// Then the chain: the plants as members make the same level Complete, and one level down removes the highest slots first.</summary>
        private static void LevelFixtureTests(List<Fixture> fixtures, int seeds)
        {
            int cases = 0, fellCases = 0, plantCases = 0, chainCases = 0;
            ComposeResult r = new ComposeResult(), rc = new ComposeResult();
            foreach (Fixture f in fixtures)
            {
                for (int s = 0; s < seeds; s += 5)
                {
                    float3[] ex;
                    bool[] th;
                    MakeStand(f, s, out ex, out th);
                    foreach (SimTarget target in new[] { SimTarget.Interior, SimTarget.TreeLine })
                    {
                        bool interior = target == SimTarget.Interior;
                        for (int level = 1; level <= 8; level++)
                        {
                            string where = f.Name + ".levels." + target + "/L" + level + "/" + s;
                            ComposeInput input = new ComposeInput
                            {
                                Ring = f.Ring,
                                Triangles = f.Tris,
                                Params = SimParams(target),
                                Level = (byte)level,
                                TreeLine = !interior,
                                Obstacles = f.Obstacles,
                                ExclusionRings = f.Exclusions,
                                Existing = ex,
                                Thinnable = th,
                                Own = new ComposeMember[0],
                                Caps = BigCaps(),
                            };
                            GladeStatus st = SkiGladeCompose.Compose(input, r);
                            s_Plans += 3;
                            cases++;
                            if (!Check(st == GladeStatus.Ok, where, "compose status " + st))
                                continue;
                            Check(r.Plant.Count == 0 || r.Fell.Count == 0, where, "NEVER BOTH: plant " + r.Plant.Count + " and fell " + r.Fell.Count);

                            // Oracle counts and T.
                            List<int> wild = new List<int>();
                            int x = 0;
                            for (int i = 0; i < ex.Length; i++)
                            {
                                if (!OracleInside(f.OracleRing, new double2(ex[i].x, ex[i].z)))
                                    continue;
                                if (th[i])
                                    wild.Add(i);
                                else
                                    x++;
                            }
                            int w = interior ? wild.Count : 0;
                            if (!interior)
                                x = 0;
                            int n = w + x;
                            Check(r.WildInside == w && r.FixedInside == x && r.OwnInside == 0 && r.Inside == n, where,
                                "W/X/M/N " + r.WildInside + "/" + r.FixedInside + "/" + r.OwnInside + "/" + r.Inside + " vs " + w + "/" + x + "/0/" + n);
                            if (interior)
                            {
                                CheckRounded(where, "Target", r.Target, OracleDensityPerHa[level - 1] / 10000.0 * f.OracleArea);
                            }
                            else
                            {
                                CheckRounded(where, "belt base", r.RawTarget, (double)TBeltDensity * TBeltCoverage * f.OraclePerimeter * (TBeltDepth - TBeltGap));
                                CheckRounded(where, "belt Target", r.Target, OracleBeltFactor[level - 1] * r.RawTarget);
                            }
                            int t = r.Target;
                            if (n > t)
                            {
                                fellCases++;
                                int k = Math.Max(0, w - Math.Max(0, t - x));
                                OKey[] rank = new OKey[wild.Count];
                                for (int i = 0; i < wild.Count; i++)
                                {
                                    float3 p = ex[wild[i]];
                                    rank[i] = new OKey { Key = ~OracleThinHash(p, 0x7A1C3E5Bu), X = p.x, Z = p.z, Index = wild[i] };
                                }
                                Array.Sort(rank, OKeyCompare);
                                List<int> expect = new List<int>();
                                for (int i = 0; i < k; i++)
                                    expect.Add(rank[i].Index);
                                expect.Sort();
                                Check(r.Plant.Count == 0 && SameInts(expect, r.Fell), where, "too many: Fell (" + r.Fell.Count + ") is not the top " + k + " by rank");
                                continue;
                            }
                            Check(r.Fell.Count == 0, where, "N " + n + " <= T " + t + " but fell " + r.Fell.Count);
                            int d = t - n;
                            bool prefixPlant = r.Plant.Count == Math.Min(d, r.Slice);
                            for (int i = 0; prefixPlant && i < r.Plant.Count; i++)
                                prefixPlant = r.Plant[i] == i;
                            Check(prefixPlant, where, "too few: Plant (" + r.Plant.Count + ") is not the first min(D " + d + ", |P| " + r.Slice + ") slots");
                            if (r.Slice < d)
                                Check(!r.PlanIsSlice, where, "the stream fell short of D on a slice");
                            if (r.Plant.Count == 0)
                                continue;
                            plantCases++;
                            GladeParams pg = StreamTestParams(target);
                            // A full re-plan that is not cut carries the unbounded target.
                            pg.MaxPlant = r.FullReplan && !r.PlanIsSlice ? int.MaxValue : r.RequestedSlice;
                            CheckPlan(f, pg, ex, th, r.Plan, where, null);

                            // Chain: the plants as members.
                            ComposeMember[] own = new ComposeMember[r.Plant.Count];
                            for (int i = 0; i < own.Length; i++)
                                own[i] = new ComposeMember { Position = r.Plan.Planted[r.Plant[i]].Position, Layer = LayerOf(target), Variant = 0, PlanGen = SkiGladeCompose.PlanGeneration };
                            input.Own = own;
                            SkiGladeCompose.Compose(input, rc);
                            s_Plans += 2;
                            Check(rc.Complete && (rc.Shortfall > 0) == (r.Plant.Count < d) && rc.OwnInside == own.Length && rc.OwnUnmatched == 0, where,
                                "after planting, the same level: Complete " + rc.Complete + ", shortfall " + rc.Shortfall + " (planted " + r.Plant.Count + " of " + d +
                                "), own " + rc.OwnInside + ", unmatched " + rc.OwnUnmatched);
                            if (level > 1)
                            {
                                input.Level = (byte)(level - 1);
                                SkiGladeCompose.Compose(input, rc);
                                s_Plans += 2;
                                int keep = Math.Max(0, Math.Min(own.Length, interior ? rc.Target - w - x : rc.Target));
                                bool order = rc.Plant.Count == 0 && rc.Keep == keep && rc.Remove.Count == own.Length - keep;
                                for (int i = 0; order && i < rc.Remove.Count; i++)
                                    order = rc.Remove[i] == own.Length - 1 - i; // Own index i = slot i: highest first
                                Check(order, where, "one level down: keep " + rc.Keep + " (oracle " + keep + "), removed " + rc.Remove.Count +
                                    " — not the highest slots first");
                                if (keep > 0)
                                    Check(rc.Fell.Count == 0, where, "one level down felled with members kept");
                                chainCases++;
                            }
                        }
                    }
                }
            }
            Console.WriteLine("INFO  levelfixtures: " + cases + " composes on " + fixtures.Count + " fixture geometries — " + fellCases +
                              " too many (rank fell), " + plantCases + " planted (P checked against the whole stand), " + chainCases + " one-level-down chains");
        }

        private static ComposeCaps BigCaps()
        {
            return new ComposeCaps { MaxPlant = 1000000, MaxFell = 1000000, MaxRemove = 1000000 };
        }

        // ================================================================== belt coverage gate

        /// <summary>Belt coverage ("~55 % coverage with open stretches"). Paired
        /// control at equal target:
        /// - A = BeltCoverage 0.55 at density D.
        /// - B = BeltCoverage 1 (the gate is skipped, SkiGladePlanner `if (k.BeltCoverage &lt; 1f)`) at 0.55·D.
        /// The test bins belt trees by the arc length of their nearest boundary point (test-local
        /// projection) into wavelength/4 bins on a circular perimeter. It asserts these results on aggregates
        /// over seeds: A occupies a smaller share, has much longer empty stretches, almost always has
        /// an empty stretch ≥ 1 wavelength, and its occupancy depends on the seed.</summary>
        private static void BeltGateTests(List<Fixture> fixtures, int seeds)
        {
            string[] names = { "rect100x300", "L", "U" };
            foreach (string name in names)
            {
                Fixture f = fixtures.Find(x => x.Name == name);
                if (f == null)
                {
                    Fail("beltgate/" + name, "fixture missing");
                    continue;
                }
                GladeParams a = Defaults();
                a.Mode = GladeMode.Belt;
                a.ThinFraction = 0f;
                a.MinSpacing = 4.5f;
                GladeParams b = a;
                b.BeltCoverage = 1f;
                b.BeltDensity = a.BeltDensity * a.BeltCoverage;

                int nBins = (int)Math.Floor(f.OraclePerimeter / (a.BeltNoiseWavelength / 4.0));
                double binLen = f.OraclePerimeter / nBins;
                int wavelengthBins = (int)Math.Ceiling(a.BeltNoiseWavelength / binLen - 1e-9);
                double shareA = 0, shareB = 0, runA = 0, runB = 0, seedDiff = 0;
                int longRunA = 0, nSeeds = 0, nPairs = 0;
                bool[] prevOcc = null;
                for (int s = 0; s < seeds; s++)
                {
                    uint seed = PlanSeed(f, s);
                    GladePlan pa = Run(f, seed, a, null, null);
                    GladePlan pb = Run(f, seed, b, null, null);
                    bool[] occA = BeltOccupancy(f, pa, nBins, binLen);
                    bool[] occB = BeltOccupancy(f, pb, nBins, binLen);
                    shareA += OccupiedShare(occA);
                    shareB += OccupiedShare(occB);
                    int ra = LongestEmptyRun(occA), rb = LongestEmptyRun(occB);
                    runA += ra;
                    runB += rb;
                    if (ra >= wavelengthBins)
                        longRunA++;
                    if (prevOcc != null)
                    {
                        int d = 0;
                        for (int i = 0; i < nBins; i++)
                            if (occA[i] != prevOcc[i])
                                d++;
                        seedDiff += d / (double)nBins;
                        nPairs++;
                    }
                    prevOcc = occA;
                    nSeeds++;
                }
                shareA /= nSeeds;
                shareB /= nSeeds;
                runA /= nSeeds;
                runB /= nSeeds;
                double longFrac = longRunA / (double)nSeeds;
                double diffMean = nPairs > 0 ? seedDiff / nPairs : 0;
                string w = "beltgate/" + name;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO  {0}: {1} bins of {2:F1} m; occupied share A {3:F3} vs B {4:F3}; mean longest empty run A {5:F1} vs B {6:F1} bins; A run >= {7} bins in {8:P1} of seeds; seed-to-seed occupancy difference {9:F3}",
                    w, nBins, binLen, shareA, shareB, runA, runB, wavelengthBins, longFrac, diffMean));
                Check(shareA <= shareB - 0.08, w, "occupied share A " + F(shareA) + " not ≤ B " + F(shareB) + " − 0.08 (gate not thinning the perimeter)");
                Check(runA >= 2 * runB, w, "mean longest empty run A " + F(runA) + " < 2 × B " + F(runB) + " (no open stretches)");
                Check(longFrac >= 0.9, w, "an empty stretch ≥ 1 wavelength in only " + F(longFrac) + " of seeds (< 0.9)");
                Check(diffMean >= 0.10, w, "occupancy of seed s vs s+1 differs in " + F(diffMean) + " of bins (< 0.10: gate ignores the seed?)");
            }
        }

        private static bool[] BeltOccupancy(Fixture f, GladePlan plan, int nBins, double binLen)
        {
            bool[] occ = new bool[nBins];
            double2[] v = f.OracleRing;
            foreach (PlantedTree t in plan.Planted)
            {
                if (t.Layer != 1)
                    continue;
                double2 p = new double2(t.Position.x, t.Position.z);
                double best = double.MaxValue, bestArc = 0, start = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    double2 a = v[i], b = v[(i + 1) % v.Length];
                    double2 ab = b - a;
                    double len2 = ab.x * ab.x + ab.y * ab.y;
                    double len = Math.Sqrt(len2);
                    double u = len2 > 0 ? ((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / len2 : 0;
                    if (u < 0) u = 0; else if (u > 1) u = 1;
                    double dx = a.x + u * ab.x - p.x, dy = a.y + u * ab.y - p.y;
                    double d2 = dx * dx + dy * dy;
                    if (d2 < best)
                    {
                        best = d2;
                        bestArc = start + u * len;
                    }
                    start += len;
                }
                int bin = (int)Math.Floor(bestArc / binLen);
                if (bin < 0) bin = 0;
                occ[bin % nBins] = true;
            }
            return occ;
        }

        private static double OccupiedShare(bool[] occ)
        {
            int c = 0;
            for (int i = 0; i < occ.Length; i++)
                if (occ[i])
                    c++;
            return c / (double)occ.Length;
        }

        /// <summary>Longest run of empty bins on the circular perimeter (all empty → length).</summary>
        private static int LongestEmptyRun(bool[] occ)
        {
            int n = occ.Length;
            int first = -1;
            for (int i = 0; i < n; i++)
            {
                if (occ[i])
                {
                    first = i;
                    break;
                }
            }
            if (first < 0)
                return n;
            int best = 0, run = 0;
            for (int k = 1; k <= n; k++)
            {
                if (occ[(first + k) % n])
                {
                    if (run > best) best = run;
                    run = 0;
                }
                else
                {
                    run++;
                }
            }
            return best;
        }

        private static void AgeHistogramTest()
        {
            Fixture f = Fx("ages_rect", "agesrect", MakeRing(D2(0, 0, 400, 0, 400, 400, 0, 400), 0, new double2(0, 0)), null, null);
            GladeParams p = Defaults();
            p.Mode = GladeMode.Glade;
            p.AgeWeights = new float4(1, 1, 1, 1);
            p.SpeciesCount = 5;
            p.DominantShare = 0.2f;
            p.MaxPlant = 100000;
            long[] ages = new long[4];
            long[] species = new long[5];
            long n = 0;
            for (int s = 0; s < 10; s++)
            {
                GladePlan plan = Run(f, SkiGladePlanner.Seed32(f.Ring[0], (uint)s), p, null, null);
                foreach (PlantedTree t in plan.Planted)
                {
                    n++;
                    ages[t.Age == SkiGladePlanner.AgeSapling ? 0 : t.Age == SkiGladePlanner.AgeYoung ? 1 : t.Age == SkiGladePlanner.AgeMature ? 2 : 3]++;
                    species[t.Species]++;
                }
            }
            for (int i = 0; i < 4; i++)
                Check(Math.Abs(ages[i] / (double)n - 0.25) <= 0.03, "hist.ages_uniform", "age bucket " + i + " share " + F(ages[i] / (double)n));
            for (int i = 0; i < 5; i++)
                Check(Math.Abs(species[i] / (double)n - 0.2) <= 0.03, "hist.species_uniform", "species " + i + " share " + F(species[i] / (double)n));
        }

        // ================================================================== timing (report-only)

        private static void TimingReport(List<Fixture> fixtures, List<ParamSet> sets)
        {
            foreach (Fixture f in fixtures)
            {
                foreach (ParamSet ps in sets)
                {
                    float3[] ex;
                    bool[] th;
                    MakeStand(f, 0, out ex, out th);
                    Run(f, PlanSeed(f, 0), ps.P, ex, th); // warm-up / JIT
                    double max = 0, sum = 0;
                    const int reps = 20;
                    GladePlan plan = new GladePlan();
                    for (int r = 0; r < reps; r++)
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        Run(f, PlanSeed(f, r), ps.P, ex, th, plan);
                        sw.Stop();
                        double ms = sw.Elapsed.TotalMilliseconds;
                        sum += ms;
                        if (ms > max)
                            max = ms;
                    }
                    bool report = f.Name.StartsWith("long", StringComparison.Ordinal) || max > 20.0;
                    if (!report)
                        continue;
                    string tag = max > 20.0 && f.Name.StartsWith("long", StringComparison.Ordinal) ? "WARN" : "TIME";
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}  {1}.{2}: mean {3:F2} ms, max {4:F2} ms over {5} plans (planted {6}, attempts {7}){8}",
                        tag, f.Name, ps.Name, sum / reps, max, reps, plan.Planted.Count, plan.Attempts,
                        tag == "WARN" ? " — above the 20 ms main-thread budget" : ""));
                }
            }
        }

        // ================================================================== comparisons

        private static bool SameInts(List<int> a, List<int> b)
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

        private static bool Bits(float a, float b)
        {
            return BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        }

        private static bool SameTree(PlantedTree a, PlantedTree b)
        {
            return Bits(a.Position.x, b.Position.x) && Bits(a.Position.y, b.Position.y) && Bits(a.Position.z, b.Position.z)
                   && Bits(a.Rotation, b.Rotation) && a.Species == b.Species && Bits(a.Age, b.Age) && a.Layer == b.Layer;
        }

        private static string Diff(GladePlan a, GladePlan b)
        {
            if (a.Status != b.Status) return "Status";
            if (!Bits(a.Area, b.Area)) return "Area";
            if (!Bits(a.BeltArea, b.BeltArea)) return "BeltArea";
            if (a.TargetGlade != b.TargetGlade || a.TargetBelt != b.TargetBelt) return "Target";
            if (a.Attempts != b.Attempts) return "Attempts";
            if (a.Saturated != b.Saturated) return "Saturated";
            if (!SameInts(a.Thinned, b.Thinned)) return "Thinned";
            if (a.Planted.Count != b.Planted.Count) return "Planted.Count";
            for (int i = 0; i < a.Planted.Count; i++)
            {
                if (!SameTree(a.Planted[i], b.Planted[i]))
                    return "Planted[" + i + "]";
            }
            return null;
        }
    }
}
