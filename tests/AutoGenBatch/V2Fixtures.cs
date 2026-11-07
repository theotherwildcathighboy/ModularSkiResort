// V2 fixtures (A1): the region matrix of stage-a-gates 1 (synthetic fixtures in git, real heightmaps from the fixture folder outside git).
// The file also checks the fixtures themselves: field-test 3 regions transcribed from the log, Steep / Benched shape, and the refusal
// ladder in Resort mode = V1-4. A missing real fixture = one SKIP line, never a FAIL.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal enum FixtureKind { Synthetic, Real, Vanilla, Field }

    /// <summary>One planner run of the matrix: a named region on a fixture. Snap builds the snapshot lazily (real: reads the PNG window).</summary>
    internal sealed class V2Case
    {
        public string Id, Fixture;
        public FixtureKind Kind;
        public float2[] Region;
        public float AreaKm2;
        public bool Steeper;         // Steep and Mountain Village: structure and honesty gated, density and fans SOFT (gates 2.3 header)
        public bool ExpectBench;     // Benched, and real fixtures with a bench: S-3 / S-6 expect a bench pod
        public bool Determinism;     // D-1..D-3 run here (costs 6-10 planner runs)
        public Func<HeightSnapshot> Snap;
    }

    internal static class V2Fixtures
    {
        public const float MapHalfM = 7168f;

        /// <summary>Square of the given area (km2) around (cx, cz), centered inside the map (the planner masks the last 24 m).</summary>
        public static float2[] Sq(float cx, float cz, float km2)
        {
            float half = 500f * (float)Math.Sqrt(km2) - 1f;
            float lim = MapHalfM - 60f - half;
            return Terrain.Square(Math.Max(-lim, Math.Min(lim, cx)), Math.Max(-lim, Math.Min(lim, cz)), half);
        }

        /// <summary>Game xz meters of a lat/lon relative to the fixture's center (ground meters: equirectangular, like the builder's
        /// z15 tiles resampled to 3.5 m).</summary>
        public static float2 LatLonXz(double lat0, double lon0, double lat, double lon)
        {
            const double R = 6371008.8;
            return new float2((float)(R * (lon - lon0) * Math.PI / 180.0 * Math.Cos(lat0 * Math.PI / 180.0)), (float)(R * (lat - lat0) * Math.PI / 180.0));
        }

        // The four field-test 3 regions of the Mountain Village session (field/2026-10-26c, SkiResortMod.Mod.log).
        // The source is the `QuickRuns [region]` corner lines in click order, each with its final "corner 1 moved" position.
        // Logged areas: 410.6 / 781.2 / 776.8 / 486.4 ha.
        public static readonly float2[][] FieldRegions =
        {
            new[] { P(6364, -1075), P(3426, -1984), P(3352, -3453), P(6621, -2189) },
            new[]
            {
                P(2933, -1596), P(2325, -1889), P(2621, -2551), P(3521, -3907), P(4367, -3567), P(5041, -3283), P(5796, -2664), P(6540, -2166),
                P(7036, -1954), P(7128, -1846), P(7094, -895), P(6224, -904), P(5453, -1238), P(4568, -1216), P(3807, -1668), P(3328, -1871), P(3071, -1835),
            },
            new[]
            {
                P(6434, -964), P(6839, -904), P(7072, -1322), P(7127, -1837), P(6900, -2042), P(6268, -2363), P(5669, -2818), P(5318, -2864),
                P(5066, -2848), P(4478, -2951), P(4417, -3633), P(4076, -3770), P(3658, -3740), P(3327, -3726), P(3036, -3658), P(2718, -3258),
                P(2496, -3030), P(2159, -2815), P(2031, -2679), P(1830, -2487), P(2326, -2345), P(2377, -1885), P(2824, -1756), P(3122, -2250),
                P(3399, -1797), P(3824, -1742), P(4071, -1429), P(4395, -1508), P(4758, -1164), P(5474, -1280), P(5849, -1163), P(6068, -995),
            },
            new[]
            {
                P(5474, -1253), P(4861, -1206), P(3621, -1758), P(3163, -2132), P(3028, -1964), P(2827, -1673), P(2607, -1810), P(2553, -2156),
                P(2750, -2637), P(3133, -3069), P(3818, -3156), P(4405, -3081), P(4923, -2871), P(5955, -2440), P(6700, -1786), P(5572, -1364),
            },
        };
        public static readonly double[] FieldAreaHa = { 410.6, 781.2, 776.8, 486.4 };

        private static float2 P(float x, float z) => new float2(x, z);

        // Real fixtures: center lat/lon of the builder, the resort base point (E 2.3), the steepest 2 km window center (E table 3.3, game xz)
        private sealed class Spec
        {
            public string Name; public FixtureKind Kind; public double Lat0, Lon0, BaseLat, BaseLon; public float SteepX, SteepZ;
            public bool Bench;
        }

        private static readonly Spec[] Specs =
        {
            new Spec { Name = "Okemo", Kind = FixtureKind.Real, SteepX = 6151, SteepZ = -5087, Bench = true },
            new Spec { Name = "JacksonHole", Kind = FixtureKind.Real, Lat0 = 43.5920, Lon0 = -110.8450, BaseLat = 43.5873, BaseLon = -110.8277, SteepX = 1055, SteepZ = 6169, Bench = true },
            new Spec { Name = "Whistler", Kind = FixtureKind.Real, Lat0 = 50.0900, Lon0 = -122.9350, BaseLat = 50.1163, BaseLon = -122.9574, SteepX = 6151, SteepZ = -4079, Bench = true },
            new Spec { Name = "Snowbird", Kind = FixtureKind.Real, Lat0 = 40.5845, Lon0 = -111.6475, BaseLat = 40.5806, BaseLon = -111.6560, SteepX = -5721, SteepZ = 9 },
            new Spec { Name = "Zermatt", Kind = FixtureKind.Real, Lat0 = 45.9900, Lon0 = 7.7500, BaseLat = 46.0207, BaseLon = 7.7491, SteepX = -6169, SteepZ = -1391, Bench = true },
            new Spec { Name = "Chamonix", Kind = FixtureKind.Real, Lat0 = 45.9780, Lon0 = 6.9400, BaseLat = 45.9795, BaseLon = 6.9268, SteepX = 1783, SteepZ = -4751, Bench = true },
            new Spec { Name = "Fjords", Kind = FixtureKind.Vanilla, SteepX = -5721, SteepZ = -4919 },
            new Spec { Name = "Highlands", Kind = FixtureKind.Vanilla, SteepX = -9, SteepZ = 5217 },
            new Spec { Name = "Delta", Kind = FixtureKind.Vanilla, SteepX = -6169, SteepZ = 6057 },
            new Spec { Name = "Plains", Kind = FixtureKind.Vanilla, SteepX = 775, SteepZ = 4769 },
        };

        /// <summary>The in-git synthetic cases: Alpine (3 seeds, the field-mirror regions), Benched, Steep. Snapshots build lazily.</summary>
        public static List<V2Case> Synthetic()
        {
            var list = new List<V2Case>();
            foreach (uint seed in new uint[] { 1u, 2u, 3u })
                list.Add(Syn($"Alpine{seed} full", "Alpine", Terrain.Alpine(seed), Terrain.AlpineRegion(false), false, false, seed == 1u));
            list.Add(Syn("Alpine1 lower", "Alpine", Terrain.Alpine(1u), Terrain.AlpineRegion(true), false, false, false));
            foreach (uint seed in new uint[] { 1u, 2u })
                list.Add(Syn($"Benched{seed} full", "Benched", Terrain.Benched(seed), Terrain.AlpineRegion(false), false, true, seed == 1u));
            foreach (uint seed in new uint[] { 1u, 2u })
                list.Add(Syn($"Steep{seed} full", "Steep", Terrain.Steep(seed), Terrain.Square(0f, 0f, 1250f), true, true, seed == 1u));
            return list;
        }

        private static V2Case Syn(string id, string fixture, HeightFn f, float2[] region, bool steeper, bool bench, bool det)
        {
            return new V2Case
            {
                Id = id, Fixture = fixture, Kind = FixtureKind.Synthetic, Region = region, AreaKm2 = (float)(SkiRunAutoPlanner.PolygonArea(region) / 1.0e6),
                Steeper = steeper, ExpectBench = bench, Determinism = det, Snap = () => Terrain.Snapshot(f, region),
            };
        }

        /// <summary>The real-heightmap cases of one fixture (1, 4, 9 km2 around the base point, 4 km2 on the steepest window) plus the
        /// Mountain Village field regions. The list is empty, with a reason, when the fixture is missing.</summary>
        public static List<V2Case> Real(string name, out string skipReason)
        {
            var list = new List<V2Case>();
            skipReason = null;
            if (name == "MountainVillage")
                return MountainVillage(out skipReason);
            Spec sp = Array.Find(Specs, s => s.Name == name);
            if (sp == null)
            {
                skipReason = "unknown fixture " + name;
                return list;
            }
            if (!Heightmap.TryOpen(name, out HeightmapFile file, out skipReason))
                return list;
            float2 bp = sp.BaseLat == 0.0 && sp.BaseLon == 0.0 ? new float2(0f, 0f) : LatLonXz(sp.Lat0, sp.Lon0, sp.BaseLat, sp.BaseLon);
            foreach (float km2 in new[] { 1f, 4f, 9f })
                list.Add(Real(sp, file, $"{name} base {km2:0} km2", Sq(bp.x, bp.y, km2), false));
            list.Add(Real(sp, file, $"{name} steepest 4 km2", Sq(sp.SteepX, sp.SteepZ, 4f), false));
            return list;
        }

        private static V2Case Real(Spec sp, HeightmapFile file, string id, float2[] region, bool steeper)
        {
            return new V2Case
            {
                Id = id, Fixture = sp.Name, Kind = sp.Kind, Region = region, AreaKm2 = (float)(SkiRunAutoPlanner.PolygonArea(region) / 1.0e6),
                Steeper = steeper, ExpectBench = false, Determinism = false, Snap = () => Heightmap.Snapshot(file, region),
            };
        }

        private static List<V2Case> MountainVillage(out string skipReason)
        {
            var list = new List<V2Case>();
            if (!Heightmap.TryOpen("MountainVillage", out HeightmapFile file, out skipReason))
                return list;
            var sp = new Spec { Name = "MountainVillage", Kind = FixtureKind.Field };
            for (int i = 0; i < FieldRegions.Length; i++)
            {
                V2Case c = Real(sp, file, $"MountainVillage field {i + 1}", FieldRegions[i], true);
                c.Determinism = i == 0;
                list.Add(c);
            }
            // [E] 2.3: the 3.5 km window at (4895, -1528) is over the 9 km2 cap as a region, so it runs as the 3 km square. The base window
            // (2632, -2023) is the 3 km base window. The list also has the 1 / 4 km2 squares at the base and the steepest 4 km2 (5591, -3575).
            list.Add(Real(sp, file, "MountainVillage window 9 km2 (4895, -1528)", Sq(4895f, -1528f, 9f), true));
            foreach (float km2 in new[] { 1f, 4f, 9f })
                list.Add(Real(sp, file, $"MountainVillage base {km2:0} km2", Sq(2632f, -2023f, km2), true));
            list.Add(Real(sp, file, "MountainVillage steepest 4 km2", Sq(5591f, -3575f, 4f), true));
            return list;
        }

        /// <summary>[E] 3.1: fixture, builder center, base point (lat, lon), the elevation read there in the research (m).</summary>
        public static readonly (string, double, double, double, double, int)[] BasePointChecks =
        {
            ("JacksonHole", 43.5920, -110.8450, 43.5873, -110.8277, 1924),
            ("Snowbird", 40.5845, -111.6475, 40.5806, -111.6560, 2469),
            ("Whistler", 50.0900, -122.9350, 50.1163, -122.9574, 671),
            ("Chamonix", 45.9780, 6.9400, 45.9795, 6.9268, 1239),
            ("Zermatt", 45.9900, 7.7500, 46.0207, 7.7491, 1607),
        };

        public static readonly string[] RealNames =
            { "MountainVillage", "Okemo", "JacksonHole", "Whistler", "Snowbird", "Zermatt", "Chamonix", "Fjords", "Highlands", "Delta", "Plains" };

        // ---------------------------------------------------------------- terrain statistics for the fixture checks

        public struct GradeStats
        {
            public float MeanGrade, ShareOver100, VerticalM;
            public List<(float Ha, float Y)> Benches;   // connected patches (8-neighborhood) of 28 m grade under 0.12 above 15 % of the vertical
        }

        /// <summary>28 m-baseline grade (the [E] s8: central difference over 8 texels, both axes) of every cell inside the region.</summary>
        public static GradeStats Stats(HeightSnapshot s, float2[] region)
        {
            int w = s.Width, d = s.Depth;
            float t = s.TexelM;
            var grade = new float[w * d];
            var inside = new bool[w * d];
            double sum = 0.0;
            int n = 0, over = 0;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int l = 4; l < d - 4; l++)
                for (int k = 4; k < w - 4; k++)
                {
                    float x = (float)(s.Origin.x + (s.X0 + k) * (double)t), z = (float)(s.Origin.y + (s.Z0 + l) * (double)t);
                    if (!GradeRaster.Inside(region, new float2(x, z)))
                        continue;
                    int c = l * w + k;
                    float gx = (s.ToWorld(s.Heights[c + 4]) - s.ToWorld(s.Heights[c - 4])) / (8f * t);
                    float gz = (s.ToWorld(s.Heights[c + 4 * w]) - s.ToWorld(s.Heights[c - 4 * w])) / (8f * t);
                    float g = math.sqrt(gx * gx + gz * gz);
                    grade[c] = g;
                    inside[c] = true;
                    sum += g;
                    n++;
                    if (g >= 1f)
                        over++;
                    float y = s.ToWorld(s.Heights[c]);
                    lo = Math.Min(lo, y);
                    hi = Math.Max(hi, y);
                }
            var st = new GradeStats { MeanGrade = n > 0 ? (float)(sum / n) : 0f, ShareOver100 = n > 0 ? (float)over / n : 0f, VerticalM = hi - lo, Benches = new List<(float, float)>() };
            float floor = lo + 0.15f * (hi - lo);
            var seen = new bool[w * d];
            var stack = new Stack<int>();
            float cellHa = t * t / 10000f;
            for (int c0 = 0; c0 < w * d; c0++)
            {
                if (seen[c0] || !inside[c0] || grade[c0] >= 0.12f)
                    continue;
                int cells = 0;
                double ySum = 0.0;
                stack.Push(c0);
                seen[c0] = true;
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    cells++;
                    ySum += s.ToWorld(s.Heights[c]);
                    int cl = c / w, ck = c % w;
                    for (int dl = -1; dl <= 1; dl++)
                        for (int dk = -1; dk <= 1; dk++)
                        {
                            int nl = cl + dl, nk = ck + dk;
                            if (nl < 0 || nk < 0 || nl >= d || nk >= w)
                                continue;
                            int nc = nl * w + nk;
                            if (!seen[nc] && inside[nc] && grade[nc] < 0.12f)
                            {
                                seen[nc] = true;
                                stack.Push(nc);
                            }
                        }
                }
                float y = (float)(ySum / cells);
                if (y > floor && cells * cellHa >= 2f)
                    st.Benches.Add((cells * cellHa, y));
            }
            // a gully or two can cut one bench into pieces: patches at the same level (within 20 m of height) count as one bench
            st.Benches.Sort((a, b) => a.Y.CompareTo(b.Y));
            for (int i = 0; i + 1 < st.Benches.Count;)
            {
                if (st.Benches[i + 1].Y - st.Benches[i].Y <= 20f)
                {
                    var m = st.Benches[i];
                    var q = st.Benches[i + 1];
                    st.Benches[i] = (m.Ha + q.Ha, (m.Y * m.Ha + q.Y * q.Ha) / (m.Ha + q.Ha));
                    st.Benches.RemoveAt(i + 1);
                }
                else
                    i++;
            }
            st.Benches.Sort((a, b) => b.Ha.CompareTo(a.Ha));
            return st;
        }
    }

    internal static partial class Program
    {
        private static void V2FixtureTests()
        {
            var swFix = System.Diagnostics.Stopwatch.StartNew();
            // The four field-test 3 regions, transcribed from the log: the polygon areas must equal the logged ones (a mistyped corner shows here)
            for (int i = 0; i < V2Fixtures.FieldRegions.Length; i++)
            {
                double ha = SkiRunAutoPlanner.PolygonArea(V2Fixtures.FieldRegions[i]) / 1.0e4;
                Check(Math.Abs(ha - V2Fixtures.FieldAreaHa[i]) <= 0.003 * V2Fixtures.FieldAreaHa[i] + 0.2, "V2Fixtures field regions",
                    $"region {i + 1} polygon {ha:F1} ha vs logged {V2Fixtures.FieldAreaHa[i]} ha");
            }
            Check(V2Fixtures.FieldRegions[0].Length == 4 && V2Fixtures.FieldRegions[1].Length == 17 && V2Fixtures.FieldRegions[2].Length == 32 &&
                  V2Fixtures.FieldRegions[3].Length == 16, "V2Fixtures field regions", "corner counts are not 4 / 17 / 32 / 16");

            // Region squares: area as asked and inside the map
            foreach (float km2 in new[] { 1f, 4f, 9f })
            {
                float2[] sq = V2Fixtures.Sq(7000f, -7000f, km2);
                double a = SkiRunAutoPlanner.PolygonArea(sq) / 1.0e6;
                bool inside = true;
                foreach (float2 p in sq)
                    inside &= Math.Abs(p.x) <= V2Fixtures.MapHalfM - 59f && Math.Abs(p.y) <= V2Fixtures.MapHalfM - 59f;
                Check(a <= km2 && a > km2 * 0.99 && inside, "V2Fixtures squares", $"{km2} km2 square: area {a:F3}, inside the map {inside}");
            }
            float2 tet = V2Fixtures.LatLonXz(43.5920, -110.8450, 43.5873, -110.8277);
            Check(tet.x > 1300f && tet.x < 1500f && tet.y < -450f && tet.y > -600f, "V2Fixtures base point", $"Teton Village at {tet.x:F0}, {tet.y:F0}");

            // Steep and Benched: the shape the gates table promises (measured at the 28 m baseline of [E])
            foreach (uint seed in new uint[] { 1u })   // seed 2 has the same shape checks under -- v2 (V2Tests prelude)
            {
                float2[] region = Terrain.Square(0f, 0f, 1250f);
                V2Fixtures.GradeStats st = V2Fixtures.Stats(Terrain.Snapshot(Terrain.Steep(seed), region), region);
                string benches = BenchText(st);
                Console.WriteLine($"INFO  Steep{seed}: vertical {st.VerticalM:F0} m, mean grade {st.MeanGrade * 100:F0} %, over 100 % {st.ShareOver100 * 100:F1} % of cells, benches {benches}");
                Check(st.VerticalM >= 1000f && st.VerticalM <= 1400f, $"V2Fixtures Steep{seed}", $"vertical {st.VerticalM:F0} m outside 1,000-1,400");
                Check(st.MeanGrade >= 0.50f && st.MeanGrade <= 0.65f, $"V2Fixtures Steep{seed}", $"mean grade {st.MeanGrade * 100:F0} % outside 50-65 %");
                Check(st.ShareOver100 >= 0.08f && st.ShareOver100 <= 0.20f, $"V2Fixtures Steep{seed}", $"{st.ShareOver100 * 100:F1} % of cells over 100 % (want 8-20 %, the spec says about 15 %)");
                int two = 0;
                foreach (var b in st.Benches)
                    if (b.Ha >= 4f && b.Ha <= 12f)
                        two++;
                Check(two >= 2, $"V2Fixtures Steep{seed}", $"{two} benches of 4-12 ha (patches: {benches})");
            }
            {
                float2[] region = Terrain.AlpineRegion(false);
                V2Fixtures.GradeStats st = V2Fixtures.Stats(Terrain.Snapshot(Terrain.Benched(1u), region), region);
                string benches = BenchText(st);
                Console.WriteLine($"INFO  Benched1: vertical {st.VerticalM:F0} m, mean grade {st.MeanGrade * 100:F0} %, benches {benches}");
                int big = 0, mid = 0;
                foreach (var b in st.Benches)
                {
                    if (b.Ha >= 20f && b.Ha <= 60f)
                        big++;
                    if (b.Ha >= 9f)
                        mid++;
                }
                Check(big >= 2 && mid >= 3, "V2Fixtures Benched1", $"{big} benches of 20-60 ha and {mid} of 9 ha or more (want 2 and 3; patches: {benches})");
                Check(st.VerticalM > 600f, "V2Fixtures Benched1", $"vertical {st.VerticalM:F0} m (the stacking gates need over 600 m)");
            }

            // V1-4 in Resort mode: the refusal ladder keeps its statuses (the Ladder suite's fixtures, Mode = Resort)
            float2[] sqr = Terrain.Square(0f, 0f, 450f);
            var cover = new List<AutoObstacle> { new AutoObstacle { Kind = AutoObstacleKind.Surface, Ring = Terrain.Rect(-460f, -460f, 460f, 300f) } };
            var tiles = new List<AutoObstacle> { new AutoObstacle { Kind = AutoObstacleKind.Unowned, Ring = Terrain.Rect(-460f, -460f, 300f, 460f) } };
            HeightFn cliff = (x, z) => z < -30 ? 1220.0 : z > 30 ? 1100.0 : 1160.0 - 2.0 * z;
            AutoPlanInput small = Input(Terrain.Plane(0.3), Terrain.Square(0f, 0f, 400f));
            small.Region = Terrain.Square(0f, 0f, 86f);
            AutoPlanInput nan = Input(Terrain.Plane(0.3), sqr);
            nan.Region = new[] { new float2(0, 0), new float2(float.NaN, 0), new float2(10, 10) };
            AutoPlanInput hostCap = Input(Terrain.Plane(0.3), Terrain.Square(0f, 0f, 550f));
            hostCap.MaxAreaM2 = 1.0e6f;
            AutoPlanInput stale = Input(Terrain.Plane(0.3), sqr);
            stale.Region = Terrain.Square(800f, 0f, 450f);
            var cases = new (string, AutoPlanInput, AutoPlanStatus)[]
            {
                ("ladder null", new AutoPlanInput(), AutoPlanStatus.BadInput),
                ("ladder NaN region", nan, AutoPlanStatus.BadInput),
                ("ladder 3 ha", small, AutoPlanStatus.TooSmall),
                ("ladder 1.21 km2 (9 km2 cap)", Input(Terrain.Plane(0.3), Terrain.Square(0f, 0f, 550f)), AutoPlanStatus.Ok),
                ("ladder 1.21 km2 (1 km2 host cap)", hostCap, AutoPlanStatus.TooLarge),
                ("ladder 9.3 km2", Input(Terrain.Plane(0.3), Terrain.Rect(-1550f, -1500f, 1550f, 1500f)), AutoPlanStatus.TooLarge),
                ("ladder snapshot elsewhere", stale, AutoPlanStatus.BadInput),
                ("ladder flat", Input((x, z) => 900.0, sqr), AutoPlanStatus.NoVertical),
                ("ladder 4 % (64 m)", Input(Terrain.Plane(0.04), Terrain.Rect(-300f, -800f, 300f, 800f)), AutoPlanStatus.TooFlat),
                ("ladder cliff band", Input(cliff, sqr), AutoPlanStatus.AllCliff),
                ("ladder all cliff", Input(Terrain.Plane(1.4), sqr), AutoPlanStatus.AllCliff),
                ("ladder 82 % painted surface", Input(Terrain.Plane(0.3), sqr, cover), AutoPlanStatus.Blocked),
                ("ladder unowned tiles", Input(Terrain.Plane(0.3), sqr, tiles), AutoPlanStatus.Blocked),
                ("ladder off the map", Input(Terrain.Plane(0.3), Terrain.Rect(7050f, -400f, 7650f, 400f)), AutoPlanStatus.Blocked),
                ("ladder order flat + covered", Input((x, z) => 900.0, sqr, cover), AutoPlanStatus.Blocked),
            };
            foreach (var (name, input, want) in cases)
            {
                AutoPlanResult runs = new SkiRunAutoPlanner().Run(input);
                input.Mode = AutoPlanMode.Resort;
                AutoPlanResult res = new SkiRunAutoPlanner().Run(input);
                Check(res.Status == want, "V1-4 " + name, $"Resort status {res.Status}, want {want}");
                Check(res.Status == runs.Status, "V1-4 " + name, $"Resort status {res.Status} differs from Runs {runs.Status}");
                if (res.Status == AutoPlanStatus.Ok)
                    Invariants("V1-3 " + name, input, res);
            }
            Console.WriteLine($"INFO  V2Fixtures: {swFix.ElapsedMilliseconds} ms");
        }

        private static string BenchText(V2Fixtures.GradeStats st)
        {
            var parts = new List<string>();
            for (int i = 0; i < st.Benches.Count && i < 5; i++)
                parts.Add($"{st.Benches[i].Ha:F1} ha @ {st.Benches[i].Y:F0} m");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }
    }
}
