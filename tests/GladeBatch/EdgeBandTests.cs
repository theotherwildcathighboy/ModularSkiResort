// T157 edge band tests (the Quick runs tree filler's ragged outer edge, GladeMask.EdgeBand*) and the headless density plot.
// Oracles are test-local: winding-number inside, own segment distance, own distance bins. The band's noise is the planner's own
// (private), so the tests judge its effects (profile, dispersion along the outline, area loss bounds), never its values.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace GladeBatch
{
    internal static partial class Program
    {
        // QuickRunsToolSystem.Trees.cs mirrors (the tool needs Game.dll and cannot be linked)
        private const float TEdgeBandMin = 20f, TEdgeBandMax = 40f, TEdgeNoiseCell = 60f;

        private static GladeMask WithBand(GladeMask m, float min = TEdgeBandMin, float max = TEdgeBandMax, float cell = TEdgeNoiseCell)
        {
            return new GladeMask
            {
                Holes = m.Holes, HoleMargin = m.HoleMargin, Ground = m.Ground, TreeLine = m.TreeLine, AreaCellM = m.AreaCellM,
                EdgeBandMinM = min, EdgeBandMaxM = max, EdgeNoiseWavelengthM = cell,
            };
        }

        private static void EdgeBandTests(int seeds)
        {
            EdgeBandOffTests();
            EdgeBandAreaTests();
            EdgeBandProfileTests(24);   // fixed: the profile and ragged bounds are judged on these 24 seeds whatever the run's seed count
            EdgeBandStreamTests();
            EdgeBandComposeTests();
        }

        /// <summary>Band off (EdgeBandMaxM 0 with the other band fields set) is the pre-band mask bit for bit: plan, MaskedArea, PlanKey.
        /// A band-on mask leaves Thin (the thinned set) and Belt (the belt trees) exactly as they were.</summary>
        private static void EdgeBandOffTests()
        {
            FillCase c = MakeFillCase("edge.off", 10, new double2(1000, -700), 0.7f);
            GladeParams p = FillParams();
            p.Mode = GladeMode.Glade;
            p.GladeDensity = 100f / 10000f;
            p.MaxPlant = 100000;
            uint seed = SkiGladePlanner.Seed32(c.Ring[0], 7u);
            GladeMask off = WithBand(c.Mask, TEdgeBandMin, 0f, TEdgeNoiseCell);
            var a = new GladePlan();
            var b = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed, in p, null, null, null, null, c.Mask, a);
            SkiGladePlanner.Plan(c.Ring, null, seed, in p, null, null, null, null, off, b);
            s_Plans += 2;
            Check(Diff(a, b) == null && Bits(a.MaskedArea, b.MaskedArea), "edge/off", "EDGE OFF: a band-off mask changed the plan (" +
                (Diff(a, b) ?? "MaskedArea") + ")");

            float3[] ex;
            bool[] th;
            int inFill;
            FillStand(c, c.Mask, 3, out ex, out th, out inFill);
            ulong k0 = SkiGladeCompose.PlanKey(FillInput(c, c.Mask, 6, ex, th, null));
            ulong k1 = SkiGladeCompose.PlanKey(FillInput(c, off, 6, ex, th, null));
            ulong k2 = SkiGladeCompose.PlanKey(FillInput(c, WithBand(c.Mask), 6, ex, th, null));
            ulong k3 = SkiGladeCompose.PlanKey(FillInput(c, WithBand(c.Mask, TEdgeBandMin, 35f), 6, ex, th, null));
            s_Plans += 4;
            Check(k0 == k1, "edge/off", "EDGE OFF: a band-off mask changed the PlanKey");
            Check(k2 != k0 && k3 != k2, "edge/key", "EDGE KEY: the band (or its depth) is not in the PlanKey");

            // Thin and Belt never read the band
            GladeMask on = WithBand(c.Mask);
            GladeParams thin = p;
            thin.Mode = GladeMode.Thin;
            thin.ThinFraction = 0.5f;
            var ta = new GladePlan();
            var tb = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed, in thin, null, ex, null, null, c.Mask, ta);
            SkiGladePlanner.Plan(c.Ring, null, seed, in thin, null, ex, null, null, on, tb);
            GladeParams belt = SimParams(SimTarget.TreeLine);
            belt.Mode = GladeMode.Belt;
            belt.MaxPlant = 100000;
            var ba = new GladePlan();
            var bb = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed, in belt, null, null, null, null, null, ba);
            SkiGladePlanner.Plan(c.Ring, null, seed, in belt, null, null, null, null, on, bb);
            s_Plans += 4;
            Check(Diff(ta, tb) == null && ta.Thinned.Count > 0, "edge/thin", "EDGE THIN: the band changed the thinned set");
            Check(Diff(ba, bb) == null && ba.Planted.Count > 0, "edge/belt", "EDGE BELT: the band changed the belt layer");
        }

        /// <summary>MaskedArea with the band: on a plain square (no holes, no ground) the loss per meter of outline is the mean
        /// depth / 2 (the integral of 1 - smoothstep), so it lies in [min / 2, max / 2] up to corner overlap. The Glade target reads it.</summary>
        private static void EdgeBandAreaTests()
        {
            foreach (double sideArg in new[] { 300.0, 800.0, -800.0 })   // -800 = a 96-vertex wobbly ring of about 800 x 800 (the edge grid)
            {
                bool wobbly = sideArg < 0;
                double side = Math.Abs(sideArg);
                float3[] ring = MakeRing(wobbly ? Wobbly(side, 96) : D2(0, 0, side, 0, side, side, 0, side), 23, new double2(3100, -1800));
                GladeParams p = FillParams();
                p.Mode = GladeMode.Glade;
                p.GladeDensity = 70f / 10000f;
                p.MaxPlant = 0;
                var plain = new GladePlan();
                var band = new GladePlan();
                SkiGladePlanner.Plan(ring, null, 11u, in p, null, null, null, null, new GladeMask(), plain);
                SkiGladePlanner.Plan(ring, null, 11u, in p, null, null, null, null, WithBand(new GladeMask()), band);
                s_Plans += 2;
                double perLen = (plain.MaskedArea - band.MaskedArea) / OraclePerimeter(Xz(ring));
                Check(perLen >= 0.5 * TEdgeBandMin - 2.0 && perLen <= 0.5 * TEdgeBandMax + 0.5, "edge/area",
                    "EDGE AREA: loss per m of outline " + F(perLen) + " m, expected within [" + F(0.5 * TEdgeBandMin - 2) + ", " +
                    F(0.5 * TEdgeBandMax + 0.5) + "] (side " + side + (wobbly ? ", 96 vertices" : string.Empty) + ")");
                Check(band.RawTargetGlade == (int)Math.Floor(p.GladeDensity * band.MaskedArea + 0.5), "edge/area",
                    "EDGE TARGET: raw " + band.RawTargetGlade + " is not round(density x weighted area)");
                Console.WriteLine("INFO  edge.area." + (int)side + "m" + (wobbly ? ".96v" : string.Empty) + ": plain " + F(plain.MaskedArea) + " m2, band " + F(band.MaskedArea) +
                    " m2, loss " + F(perLen) + " m2 per m of outline");
            }
        }

        private const double BinM = 5.0;
        private const int Bins = 10;   // 0-50 m; the last bin is past the deepest band (40 m)

        /// <summary>Density by distance to the outline, band off vs on, summed over seeds (the stream varies by seed, the band's noise
        /// is world-anchored). Interior (>= 45 m) density stays the level's; the band ramps from near 0 at the outline up to it. The
        /// dispersion of per-segment edge counts along the outline shows the depth varies (ragged, not a uniform soft line).</summary>
        private static void EdgeBandProfileTests(int seeds)
        {
            const double Side = 600.0;
            double2[] local = D2(0, 0, Side, 0, Side, Side, 0, Side);
            var offset = new double2(-2200, 900);
            float3[] ring = MakeRing(local, 0, offset);
            double2[] oracle = Xz(ring);
            const float Density = 70f / 10000f;   // level 6
            var before = new double[Bins + 1];
            var after = new double[Bins + 1];
            const int Segments = 60;   // 40 m segments along the outline (2400 m)
            var segBefore = new double[Segments];
            var segAfter = new double[Segments];
            for (int s = 0; s < seeds; s++)
            {
                GladeParams p = FillParams();
                p.Mode = GladeMode.Glade;
                p.GladeDensity = Density;
                p.MaxPlant = 1000000;
                uint seed = SkiGladePlanner.Seed32(ring[0], (uint)(1000 + s));
                var off = new GladePlan();
                var on = new GladePlan();
                SkiGladePlanner.Plan(ring, null, seed, in p, null, null, null, null, new GladeMask(), off);
                SkiGladePlanner.Plan(ring, null, seed, in p, null, null, null, null, WithBand(new GladeMask()), on);
                s_Plans += 2;
                Check(!on.Saturated && on.Planted.Count == on.RawTargetGlade, "edge/profile",
                    "EDGE COUNT: the band plan placed " + on.Planted.Count + " of its target " + on.RawTargetGlade);
                Profile(off.Planted, oracle, offset, Side, before, segBefore);
                Profile(on.Planted, oracle, offset, Side, after, segAfter);
            }
            double[] area = BinAreas(oracle, offset, Side);
            var line = new System.Text.StringBuilder();
            double interiorOff = before[Bins] / (area[Bins] * seeds), interiorOn = after[Bins] / (area[Bins] * seeds);
            for (int b = 0; b <= Bins; b++)
            {
                double dOff = before[b] / (area[b] * seeds), dOn = after[b] / (area[b] * seeds);
                line.Append(b < Bins ? (b * BinM) + "-" + ((b + 1) * BinM) : ">=" + (Bins * BinM)).Append(" m: ")
                    .Append(F(dOff * 10000)).Append(" -> ").Append(F(dOn * 10000)).Append(b < Bins ? "; " : string.Empty);
            }
            Console.WriteLine("INFO  edge.profile (trees/ha by distance to the outline, before -> after, level 6 = 70, " + seeds + " seeds): " + line);

            Check(Math.Abs(interiorOn / Density - 1.0) <= 0.05, "edge/profile",
                "EDGE INTERIOR: density past the band " + F(interiorOn * 10000) + " /ha, level 70 /ha");
            Check(Math.Abs(interiorOn / interiorOff - 1.0) <= 0.05, "edge/profile",
                "EDGE INTERIOR: band changed the interior density " + F(interiorOff * 10000) + " -> " + F(interiorOn * 10000) + " /ha");
            double first = after[0] / (area[0] * seeds), third = after[2] / (area[2] * seeds), deep = after[Bins - 2] / (area[Bins - 2] * seeds);
            Check(first <= 0.08 * Density, "edge/profile", "EDGE RAMP: 0-5 m holds " + F(first * 10000) + " /ha (want <= 8 % of level)");
            Check(third <= 0.6 * Density, "edge/profile", "EDGE RAMP: 10-15 m holds " + F(third * 10000) + " /ha (want <= 60 % of level)");
            Check(deep >= 0.9 * Density, "edge/profile", "EDGE RAMP: 40-45 m holds " + F(deep * 10000) + " /ha (want >= 90 % of level)");
            for (int b = 1; b <= Bins; b++)
            {
                double prev = after[b - 1] / area[b - 1], cur = after[b] / area[b];
                double sigma = Math.Sqrt(after[b - 1]) / area[b - 1] + Math.Sqrt(after[b]) / area[b];
                Check(cur >= prev - 3 * sigma - 1e-9, "edge/profile", "EDGE RAMP: density falls inward at bin " + b + " (" + F(prev * 1e4 / seeds) +
                    " -> " + F(cur * 1e4 / seeds) + " /ha)");
            }

            // Ragged, not a uniform soft edge: per-segment counts within 25 m of the outline vary far beyond Poisson with the band
            double dispOff = Dispersion(segBefore), dispOn = Dispersion(segAfter);
            Console.WriteLine("INFO  edge.ragged: dispersion index of 40 m segment counts within 25 m of the outline, before " + F(dispOff) +
                ", after " + F(dispOn) + " (1 = Poisson)");
            Check(dispOff <= 1.5, "edge/ragged", "EDGE RAGGED: the no-band edge is not uniform (dispersion " + F(dispOff) + ")");
            Check(dispOn >= 1.8 && dispOn >= 3.0 * dispOff, "edge/ragged", "EDGE RAGGED: the band depth does not vary along the outline (dispersion " + F(dispOn) + ")");
        }

        private static void Profile(List<PlantedTree> trees, double2[] oracle, double2 offset, double side, double[] bins, double[] seg)
        {
            int segs = seg.Length;
            for (int i = 0; i < trees.Count; i++)
            {
                double2 q = new double2(trees[i].Position.x, trees[i].Position.z);
                double d = OracleBoundaryDistance(oracle, q);
                int b = (int)(d / BinM);
                bins[b < Bins ? b : Bins] += 1;
                if (d >= 25.0)
                    continue;
                // arc position of the nearest side on the axis-aligned square (angle 0)
                double lx = q.x - offset.x, lz = q.y - offset.y;
                double dl = lx, dr = side - lx, db = lz, dt = side - lz;
                double m = Math.Min(Math.Min(dl, dr), Math.Min(db, dt));
                double arc = m == db ? lx : m == dr ? side + lz : m == dt ? 2 * side + (side - lx) : 3 * side + (side - lz);
                int k = (int)(arc / (4 * side) * segs);
                seg[k < 0 ? 0 : k >= segs ? segs - 1 : k] += 1;
            }
        }

        private static double[] BinAreas(double2[] oracle, double2 offset, double side)
        {
            var a = new double[Bins + 1];
            const double cell = 0.5;
            for (double z = offset.y + 0.25; z < offset.y + side; z += cell)
            {
                for (double x = offset.x + 0.25; x < offset.x + side; x += cell)
                {
                    double2 q = new double2(x, z);
                    if (!OracleInside(oracle, q))
                        continue;
                    int b = (int)(OracleBoundaryDistance(oracle, q) / BinM);
                    a[b < Bins ? b : Bins] += cell * cell;
                }
            }
            return a;
        }

        /// <summary>Variance / mean of the segment counts, leaving out each side's first and last segment (a corner splits its near-edge
        /// area between two sides, so a corner segment holds about 30 % less ground and would read as raggedness).</summary>
        private static double Dispersion(double[] v)
        {
            int perSide = v.Length / 4;
            var keep = new List<double>();
            for (int i = 0; i < v.Length; i++)
            {
                int k = i % perSide;
                if (k != 0 && k != perSide - 1)
                    keep.Add(v[i]);
            }
            double mean = 0, var = 0;
            foreach (double x in keep)
                mean += x;
            mean /= keep.Count;
            foreach (double x in keep)
                var += (x - mean) * (x - mean);
            var /= keep.Count - 1;
            return mean > 0 ? var / mean : 0;
        }

        /// <summary>Replay, prefix and the masked rules with the band: same input = same plan; a MaxPlant slice is a prefix; every tree
        /// stays allowed (holes + margin, tree line) and spaced.</summary>
        private static void EdgeBandStreamTests()
        {
            FillCase c = MakeFillCase("edge.stream", 33, new double2(-600, 1400), 0.75f);
            GladeMask on = WithBand(c.Mask);
            GladeParams p = FillParams();
            p.Mode = GladeMode.Glade;
            p.GladeDensity = 100f / 10000f;
            p.MaxPlant = 100000;
            uint seed = SkiGladePlanner.Seed32(c.Ring[0], 5u);
            var plan = new GladePlan();
            var again = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed, in p, null, null, null, null, on, plan);
            SkiGladePlanner.Plan(c.Ring, null, seed, in p, null, null, null, null, on, again);
            s_Plans += 2;
            Check(Diff(plan, again) == null && Bits(plan.MaskedArea, again.MaskedArea), "edge/replay", "EDGE REPLAY: " + Diff(plan, again));
            Check(plan.Planted.Count == plan.RawTargetGlade, "edge/stream",
                "EDGE COUNT: " + plan.Planted.Count + " of " + plan.RawTargetGlade + " placed with holes and a tree line");
            CheckFillTrees(c, on, plan.Planted, p.MinSpacing, "edge/stream");

            GladeParams small = p;
            small.MaxPlant = plan.Planted.Count / 3;
            var a = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed, in small, null, null, null, null, on, a);
            s_Plans++;
            bool prefix = a.Planted.Count == small.MaxPlant;
            for (int i = 0; prefix && i < a.Planted.Count; i++)
                prefix = SameTree(a.Planted[i], plan.Planted[i]);
            Check(prefix, "edge/prefix", "EDGE PREFIX: a MaxPlant slice is not a prefix of the band stream");

            // Another seed moves the trees, never the weighted area (the probe and the stream must agree on the target)
            var other = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed ^ 0x5A5A5A5Au, in p, null, null, null, null, on, other);
            s_Plans++;
            Check(Bits(other.MaskedArea, plan.MaskedArea) && other.RawTargetGlade == plan.RawTargetGlade, "edge/seed",
                "EDGE SEED: the weighted area depends on the plan seed");
        }

        /// <summary>The filler compose with the band: the hub's "Planting trees N / M" stays honest (the Plant loop reaches Complete
        /// with exactly Target - fixed trees planted, Target = round(level density x weighted area)).</summary>
        private static void EdgeBandComposeTests()
        {
            FillCase c = MakeFillCase("edge.compose", 10, new double2(1000, -700), 0.7f);
            GladeMask on = WithBand(c.Mask);
            float3[] ex;
            bool[] th;
            int inFill;
            FillStand(c, c.Mask, 9, out ex, out th, out inFill);
            foreach (int level in new[] { 4, 6, 8 })
            {
                ComposeMember[] own = Array.Empty<ComposeMember>();
                var cache = new GladePlanCache(4);
                int planted = 0, target = -1, fixedIn = 0;
                bool complete = false;
                for (int guard = 0; guard < 20 && !complete; guard++)
                {
                    ComposeInput input = FillInput(c, on, level, ex, th, own);
                    input.Caps = Caps(1500);
                    var r = new ComposeResult();
                    SkiGladeCompose.Compose(input, r, cache);
                    s_Plans++;
                    if (target < 0)
                    {
                        target = r.Target;
                        fixedIn = r.FixedInside + r.WildInside;
                        Check(r.Target == (int)Math.Floor(SkiGladeCompose.InteriorDensity(level) * r.Plan.MaskedArea + 0.5), "edge/compose",
                            "EDGE COMPOSE TARGET: T " + r.Target + " is not round(D x weighted area)");
                    }
                    complete = r.Complete;
                    if (complete || r.Plant.Count == 0)
                        break;
                    Check(r.Fell.Count == 0, "edge/compose", "EDGE COMPOSE: the filler felled with the band");
                    planted += r.Plant.Count;
                    own = WithPlants(own, r);
                }
                Check(complete && planted == target - fixedIn, "edge/compose", "EDGE COMPOSE L" + level + ": planted " + planted + " of " + target + " - " + fixedIn +
                    (complete ? string.Empty : " (never Complete)"));
            }
        }

        // ================================================================== headless density plot (T157 before / after)

        /// <summary>`dotnet run --project tests/GladeBatch -c Release -- plot <dir> [level [min max cell]]`: writes t157-edge-before-after-*.png,
        /// the filler's compose (default level 7, the trailer's) on an irregular outline with two runs and a tree line, without the band (left) and with it (right). Top row:
        /// trees; bottom row: local density (trees within 20 m, white 0 to dark green the level's density).</summary>
        internal static void EdgeBandPlot(string dir, int level = 7, float bandMin = TEdgeBandMin, float bandMax = TEdgeBandMax, float cell = TEdgeNoiseCell)
        {
            double2[] local = D2(0, 0, 260, -40, 520, 10, 700, 120, 690, 330, 560, 470, 330, 430, 150, 500, -20, 360, 30, 170);
            var offset = new double2(1800, -2600);
            float3[] ring = MakeRing(local, 0, offset);
            double2[] oracle = Xz(ring);
            float3[][] holes =
            {
                MakeRing(D2(180, -80, 225, -80, 330, 560, 285, 560), 0, offset),
                MakeRing(D2(470, -80, 510, -80, 520, 560, 480, 560), 0, offset),
            };
            var ground = new TestGround { X0 = offset.x, Z0 = offset.y };
            // The tree line cuts the top-right corner (ground rises with z and x)
            var mask = new GladeMask { Holes = holes, HoleMargin = FillMargin, Ground = ground, TreeLine = 1000f + 0.4f * 400f + 0.1f * 500f };
            List<float3> before = PlotCompose(ring, mask, level);
            List<float3> after = PlotCompose(ring, WithBand(mask, bandMin, bandMax, cell), level);

            double2 min = new double2(double.MaxValue, double.MaxValue), max = new double2(double.MinValue, double.MinValue);
            foreach (double2 v in oracle)
            {
                min = math.min(min, v);
                max = math.max(max, v);
            }
            const double Pad = 25.0, Mpp = 0.5;
            min -= Pad;
            max += Pad;
            int pw = (int)Math.Ceiling((max.x - min.x) / Mpp), ph = (int)Math.Ceiling((max.y - min.y) / Mpp);
            const int Gap = 24;
            int w = 2 * pw + Gap, h = 2 * ph + Gap;
            var img = new byte[w * h * 3];
            for (int i = 0; i < img.Length; i++)
                img[i] = 255;
            var holeOracles = new double2[holes.Length][];
            for (int i = 0; i < holes.Length; i++)
                holeOracles[i] = Xz(holes[i]);
            float density = SkiGladeCompose.InteriorDensity(level);
            const double HeatR = 20.0;
            for (int panel = 0; panel < 2; panel++)
            {
                List<float3> trees = panel == 0 ? before : after;
                int ox = panel * (pw + Gap);
                var bins = new TreeBins(trees, HeatR);
                for (int py = 0; py < ph; py++)
                {
                    for (int px = 0; px < pw; px++)
                    {
                        // north up: image y grows to -z
                        double2 q = new double2(min.x + (px + 0.5) * Mpp, max.y - (py + 0.5) * Mpp);
                        bool inside = OracleInside(oracle, q);
                        bool hole = false;
                        foreach (double2[] ho in holeOracles)
                            hole |= OracleInside(ho, q);
                        bool above = ground.HeightAt(q.x, q.y) > mask.TreeLine;
                        byte r = 246, g = 243, b = 236;   // ground
                        if (hole)
                        {
                            r = 214; g = 230; b = 245;      // run
                        }
                        else if (above)
                        {
                            r = 226; g = 226; b = 230;      // above the tree line
                        }
                        Put(img, w, ox + px, py, r, g, b);
                        // bottom row: density heat (inside the outline only)
                        double t = inside ? Math.Min(1.0, bins.CountWithin(q) / (Math.PI * HeatR * HeatR) / density) : 0.0;
                        byte hr = (byte)(246 - t * (246 - 24)), hg = (byte)(243 - t * (243 - 92)), hb = (byte)(236 - t * (236 - 40));
                        Put(img, w, ox + px, ph + Gap + py, hole ? (byte)214 : hr, hole ? (byte)230 : hg, hole ? (byte)245 : hb);
                        if (OracleBoundaryDistance(oracle, q) < 0.8)
                        {
                            Put(img, w, ox + px, py, 180, 30, 30);
                            Put(img, w, ox + px, ph + Gap + py, 180, 30, 30);
                        }
                    }
                }
                foreach (float3 t in trees)
                {
                    double cx = (t.x - min.x) / Mpp, cy = (max.y - t.z) / Mpp;
                    const double R = 2.2 / Mpp;
                    for (int dy = -(int)R - 1; dy <= (int)R + 1; dy++)
                    {
                        for (int dx = -(int)R - 1; dx <= (int)R + 1; dx++)
                        {
                            int x = (int)Math.Floor(cx) + dx, y = (int)Math.Floor(cy) + dy;
                            double ex = x + 0.5 - cx, ey = y + 0.5 - cy;
                            if (x < 0 || y < 0 || x >= pw || y >= ph || ex * ex + ey * ey > R * R)
                                continue;
                            Put(img, w, ox + x, y, 34, 96, 48);
                        }
                    }
                }
            }
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "t157-edge-before-after-L" + level + "-" + bandMin + "-" + bandMax + "-" + cell + ".png");
            WritePng(path, img, w, h);
            Console.WriteLine("PLOT  " + path + " (" + w + " x " + h + " px, 0.5 m/px, level " + level + ", band " + bandMin + "-" + bandMax + " m, noise cell " + cell +
                " m; left before " + before.Count + " trees, right after " + after.Count + " trees; top trees, bottom density within " + HeatR + " m, dark = the level)");
        }

        private static List<float3> PlotCompose(float3[] ring, GladeMask mask, int level)
        {
            var input = new ComposeInput
            {
                Ring = ring, Params = FillParams(), Level = (byte)level, Fill = mask, Existing = Array.Empty<float3>(), Own = Array.Empty<ComposeMember>(),
                Caps = Caps(100000),
            };
            var r = new ComposeResult();
            SkiGladeCompose.Compose(input, r);
            var list = new List<float3>();
            for (int i = 0; i < r.Plant.Count; i++)
                list.Add(r.Plan.Planted[r.Plant[i]].Position);
            return list;
        }

        private sealed class TreeBins
        {
            private readonly Dictionary<long, List<double2>> m_Cells = new Dictionary<long, List<double2>>();
            private readonly double m_R;

            public TreeBins(List<float3> trees, double r)
            {
                m_R = r;
                foreach (float3 t in trees)
                {
                    long k = Key((long)Math.Floor(t.x / r), (long)Math.Floor(t.z / r));
                    if (!m_Cells.TryGetValue(k, out List<double2> l))
                        m_Cells[k] = l = new List<double2>();
                    l.Add(new double2(t.x, t.z));
                }
            }

            private static long Key(long x, long z) => (x << 32) ^ (z & 0xFFFFFFFFL);

            public int CountWithin(double2 q)
            {
                long cx = (long)Math.Floor(q.x / m_R), cz = (long)Math.Floor(q.y / m_R);
                int n = 0;
                for (long dx = -1; dx <= 1; dx++)
                {
                    for (long dz = -1; dz <= 1; dz++)
                    {
                        if (!m_Cells.TryGetValue(Key(cx + dx, cz + dz), out List<double2> l))
                            continue;
                        foreach (double2 p in l)
                        {
                            double2 d = p - q;
                            if (d.x * d.x + d.y * d.y <= m_R * m_R)
                                n++;
                        }
                    }
                }
                return n;
            }
        }

        private static void Put(byte[] img, int w, int x, int y, byte r, byte g, byte b)
        {
            int i = (y * w + x) * 3;
            img[i] = r;
            img[i + 1] = g;
            img[i + 2] = b;
        }

        /// <summary>Minimal RGB8 PNG (filter 0 rows, zlib via ZLibStream).</summary>
        private static void WritePng(string path, byte[] rgb, int w, int h)
        {
            var raw = new MemoryStream();
            using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true))
            {
                for (int y = 0; y < h; y++)
                {
                    z.WriteByte(0);
                    z.Write(rgb, y * w * 3, w * 3);
                }
            }
            using (var f = File.Create(path))
            {
                f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var ihdr = new byte[13];
                BE(ihdr, 0, (uint)w);
                BE(ihdr, 4, (uint)h);
                ihdr[8] = 8;   // bit depth
                ihdr[9] = 2;   // RGB
                Chunk(f, "IHDR", ihdr);
                Chunk(f, "IDAT", raw.ToArray());
                Chunk(f, "IEND", Array.Empty<byte>());
            }
        }

        private static void Chunk(Stream f, string type, byte[] data)
        {
            var len = new byte[4];
            BE(len, 0, (uint)data.Length);
            f.Write(len, 0, 4);
            byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
            f.Write(t, 0, 4);
            f.Write(data, 0, data.Length);
            uint crc = Crc(Crc(0xFFFFFFFFu, t), data) ^ 0xFFFFFFFFu;
            var c = new byte[4];
            BE(c, 0, crc);
            f.Write(c, 0, 4);
        }

        private static void BE(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }

        private static uint[] s_CrcTable;

        private static uint Crc(uint c, byte[] data)
        {
            if (s_CrcTable == null)
            {
                s_CrcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint k = n;
                    for (int i = 0; i < 8; i++)
                        k = (k & 1) != 0 ? 0xEDB88320u ^ (k >> 1) : k >> 1;
                    s_CrcTable[n] = k;
                }
            }
            foreach (byte b in data)
                c = s_CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c;
        }
    }
}
