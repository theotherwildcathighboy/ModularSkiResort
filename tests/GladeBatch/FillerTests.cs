// Tree filler tests (Quick runs package C): the glade planner's fill mask, compose's filler layer, the tree line estimate and the
// species groups. Oracles are test-local (winding-number inside, own segment distance, own fine-grid area, analytic ground).

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace GladeBatch
{
    internal static partial class Program
    {
        /// <summary>Analytic test ground: a tilted plane with a gentle bump, never NaN.</summary>
        private sealed class TestGround : GladeHeightField
        {
            public double X0, Z0;
            public ulong Print = 0x1234UL;

            public override float HeightAt(double x, double z)
            {
                double lx = x - X0, lz = z - Z0;
                return (float)(1000.0 + 0.4 * lz + 0.1 * lx + 6.0 * Math.Sin(lx / 37.0) * Math.Cos(lz / 53.0));
            }

            public override ulong Fingerprint => Print;
        }

        private sealed class FillCase
        {
            public string Name;
            public float3[] Ring;
            public double2[] Oracle;
            public float3[][] Holes;
            public double2[][] HoleOracle;
            public TestGround Ground;
            public GladeMask Mask;
            public double2 Min, Max;
            public double GroundMin, GroundMax;
        }

        private const float FillMargin = 3f;

        private static FillCase MakeFillCase(string name, double angle, double2 offset, float lineShare)
        {
            var c = new FillCase { Name = name };
            double2[] region = D2(0, 0, 400, 0, 400, 400, 0, 400);
            c.Ring = MakeRing(region, angle, offset);
            c.Oracle = Xz(c.Ring);
            double2[][] holes = { D2(80, -30, 120, -30, 120, 430, 80, 430), D2(250, -30, 300, -30, 290, 430, 260, 430) };
            c.Holes = new float3[holes.Length][];
            c.HoleOracle = new double2[holes.Length][];
            for (int i = 0; i < holes.Length; i++)
            {
                c.Holes[i] = MakeRing(holes[i], angle, offset);
                c.HoleOracle[i] = Xz(c.Holes[i]);
            }
            c.Ground = new TestGround { X0 = offset.x, Z0 = offset.y };
            c.Min = new double2(double.MaxValue, double.MaxValue);
            c.Max = new double2(double.MinValue, double.MinValue);
            foreach (double2 v in c.Oracle)
            {
                c.Min = math.min(c.Min, v);
                c.Max = math.max(c.Max, v);
            }
            // The tree line sits at lineShare of the region's own ground range (any rotation / offset keeps a real share above it).
            c.GroundMin = double.MaxValue;
            c.GroundMax = double.MinValue;
            for (double z = c.Min.y; z <= c.Max.y; z += 10)
            {
                for (double x = c.Min.x; x <= c.Max.x; x += 10)
                {
                    if (!OracleInside(c.Oracle, new double2(x, z)))
                        continue;
                    double h = c.Ground.HeightAt(x, z);
                    c.GroundMin = Math.Min(c.GroundMin, h);
                    c.GroundMax = Math.Max(c.GroundMax, h);
                }
            }
            float line = (float)(c.GroundMin + lineShare * (c.GroundMax - c.GroundMin));
            c.Mask = new GladeMask { Holes = c.Holes, HoleMargin = FillMargin, Ground = c.Ground, TreeLine = line };
            return c;
        }

        private static double2[] Xz(float3[] ring)
        {
            var v = new double2[ring.Length];
            for (int i = 0; i < ring.Length; i++)
                v[i] = new double2(ring[i].x, ring[i].z);
            return v;
        }

        private static bool OracleAllowed(FillCase c, GladeMask m, double2 q)
        {
            if (!OracleInside(c.Oracle, q))
                return false;
            for (int i = 0; i < c.HoleOracle.Length; i++)
            {
                if (OracleInside(c.HoleOracle[i], q) || OracleBoundaryDistance(c.HoleOracle[i], q) < m.HoleMargin)
                    return false;
            }
            return !(m.Ground != null) || c.Ground.HeightAt(q.x, q.y) <= m.TreeLine;
        }

        /// <summary>Allowed area on a 0.5 m grid offset by a quarter cell (not the planner's grid).</summary>
        private static double OracleFillArea(FillCase c, GladeMask m)
        {
            const double cell = 0.5;
            long count = 0;
            for (double z = c.Min.y + 0.125; z < c.Max.y; z += cell)
            {
                for (double x = c.Min.x + 0.125; x < c.Max.x; x += cell)
                {
                    if (OracleAllowed(c, m, new double2(x, z)))
                        count++;
                }
            }
            return count * cell * cell;
        }

        private static GladeParams FillParams()
        {
            GladeParams p = SimParams(SimTarget.Interior);
            SkiGladeSpecies.ApplyPool(ref p, 3);
            return p;
        }

        /// <summary>Wild stand: inside the fill, inside the holes, above the tree line and outside the region.</summary>
        private static void FillStand(FillCase c, GladeMask m, int seed, out float3[] ex, out bool[] th, out int inFill)
        {
            var r = new System.Random(seed * 7919 + 3);
            var pts = new List<float3>();
            inFill = 0;
            for (int i = 0; i < 260; i++)
            {
                double x = c.Min.x - 20 + r.NextDouble() * (c.Max.x - c.Min.x + 40);
                double z = c.Min.y - 20 + r.NextDouble() * (c.Max.y - c.Min.y + 40);
                pts.Add(new float3((float)x, c.Ground.HeightAt(x, z), (float)z));
                if (OracleAllowed(c, m, new double2((float)x, (float)z)))
                    inFill++;
            }
            ex = pts.ToArray();
            th = new bool[ex.Length];
            for (int i = 0; i < th.Length; i++)
                th[i] = true; // all thinnable: the filler must still fell none
        }

        private static ComposeInput FillInput(FillCase c, GladeMask m, int level, float3[] ex, bool[] th, ComposeMember[] own)
        {
            return new ComposeInput
            {
                Ring = c.Ring,
                Params = FillParams(),
                Level = (byte)level,
                Fill = m,
                Existing = ex,
                Thinnable = th,
                Own = own ?? Array.Empty<ComposeMember>(),
                Caps = Caps(TCap),
            };
        }

        /// <summary>Own after an apply that only planted: the old members plus the plants as current filler members.</summary>
        private static ComposeMember[] WithPlants(ComposeMember[] own, ComposeResult r)
        {
            var list = new List<ComposeMember>(own ?? Array.Empty<ComposeMember>());
            for (int i = 0; i < r.Plant.Count; i++)
            {
                PlantedTree t = r.Plan.Planted[r.Plant[i]];
                list.Add(new ComposeMember { Position = t.Position, Layer = SkiGladeCompose.LayerFiller, Variant = 0,
                    PlanGen = SkiGladeCompose.PlanGeneration });
            }
            return list.ToArray();
        }

        private static void FillerTests(int seeds)
        {
            FillerPlannerTests();
            FillerComposeTests(seeds);
            TreeLineTests();
            SpeciesTests();
            FillerTimingReport(1000.0);
            FillerTimingReport(2000.0);
        }

        /// <summary>Report only: one level-6 filler compose (first apply, cap TCap) on a side x side region with a run every 70 m.</summary>
        private static void FillerTimingReport(double side)
        {
            double2[] region = D2(0, 0, side, 0, side, side, 0, side);
            var offset = new double2(-4000, 2500);
            float3[] ring = MakeRing(region, 7, offset);
            var holes = new List<float3[]>();
            for (double x = 40; x + 30 < side; x += 70)
                holes.Add(MakeRing(D2(x, -20, x + 30, -20, x + 26, side + 20, x + 4, side + 20), 7, offset));
            var ground = new TestGround { X0 = offset.x, Z0 = offset.y };
            var mask = new GladeMask { Holes = holes.ToArray(), HoleMargin = FillMargin, Ground = ground, TreeLine = 1000f + 0.3f * (float)side };
            var input = new ComposeInput { Ring = ring, Params = FillParams(), Level = 6, Fill = mask, Caps = Caps(TCap) };
            var r = new ComposeResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SkiGladeCompose.Compose(input, r);
            sw.Stop();
            s_Plans++;
            Console.WriteLine("TIME  filler." + (int)side + "m: " + F(sw.Elapsed.TotalMilliseconds) + " ms (area " + F(r.Plan.Area) + " m2, masked " +
                F(r.Plan.MaskedArea) + " m2, " + holes.Count + " runs, T " + r.Target + ", plant " + r.Plant.Count + ", slice " + r.Slice +
                ", attempts " + r.Plan.Attempts + ", clamped " + r.Clamped + ")");
        }

        private static void FillerPlannerTests()
        {
            FillCase c = MakeFillCase("fill", 10, new double2(1000, -700), 0.7f);
            GladeParams p = FillParams();
            p.Mode = GladeMode.Glade;
            p.GladeDensity = 100f / 10000f;
            p.MaxPlant = 100000;
            uint seed = SkiGladePlanner.Seed32(c.Ring[0], 7u);

            // Masked area vs the oracle's fine grid.
            var plan = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed, in p, null, null, null, null, c.Mask, plan);
            s_Plans++;
            double oracleArea = OracleFillArea(c, c.Mask);
            Check(Math.Abs(plan.MaskedArea - oracleArea) <= 0.01 * oracleArea, "filler/area",
                "FILL AREA: masked " + F(plan.MaskedArea) + " vs oracle " + F(oracleArea));
            Check(plan.MaskedArea < plan.Area * 0.8 && plan.MaskedArea > plan.Area * 0.2, "filler/area",
                "FILL AREA: the fixture should mask a real share (" + F(plan.MaskedArea) + " of " + F(plan.Area) + ")");
            Check(plan.RawTargetGlade == (int)Math.Floor(p.GladeDensity * plan.MaskedArea + 0.5), "filler/area",
                "FILL TARGET: raw " + plan.RawTargetGlade + " is not round(density x masked area)");

            // Every tree in the fill region, spaced, on the ground.
            CheckFillTrees(c, c.Mask, plan.Planted, p.MinSpacing, "filler/plan");
            Check(plan.Planted.Count >= 0.9 * plan.RawTargetGlade, "filler/plan",
                "FILL COUNT: " + plan.Planted.Count + " of " + plan.RawTargetGlade + " placed");

            // Prefix property holds with a mask.
            GladeParams small = p;
            small.MaxPlant = plan.Planted.Count / 3;
            var a = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed, in small, null, null, null, null, c.Mask, a);
            s_Plans++;
            bool prefix = a.Planted.Count <= plan.Planted.Count;
            for (int i = 0; prefix && i < a.Planted.Count; i++)
                prefix = SameTree(a.Planted[i], plan.Planted[i]);
            Check(prefix && a.Planted.Count == small.MaxPlant, "filler/prefix", "FILL PREFIX: MaxPlant slice is not a prefix");

            // Replay is bit-identical; an empty mask keeps the null-mask stream (same draws, only the target differs).
            var b = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed, in p, null, null, null, null, c.Mask, b);
            Check(Diff(plan, b) == null, "filler/replay", "FILL REPLAY: " + Diff(plan, b));
            var open = new GladePlan();
            var none = new GladePlan();
            GladeParams q = p;
            q.MaxPlant = 500;
            SkiGladePlanner.Plan(c.Ring, null, seed, in q, null, null, null, null, new GladeMask(), open);
            SkiGladePlanner.Plan(c.Ring, null, seed, in q, null, null, null, null, none);
            s_Plans += 3;
            bool same = open.Planted.Count == none.Planted.Count;
            for (int i = 0; same && i < open.Planted.Count; i++)
                same = SameTree(open.Planted[i], none.Planted[i]);
            Check(same, "filler/emptymask", "FILL EMPTY MASK: an empty mask moved trees of the null-mask stream");

            // Species picks never move a tree.
            GladeParams one = p;
            SkiGladeSpecies.ApplyPool(ref one, 1);
            var single = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed, in one, null, null, null, null, c.Mask, single);
            s_Plans++;
            bool moved = single.Planted.Count != plan.Planted.Count;
            int[] hist = new int[3];
            for (int i = 0; !moved && i < plan.Planted.Count; i++)
            {
                moved = math.any(single.Planted[i].Position != plan.Planted[i].Position) || single.Planted[i].Species != 0;
                hist[plan.Planted[i].Species]++;
            }
            Check(!moved, "filler/species", "FILL SPECIES: a pool change moved a tree or a single pool planted index != 0");
            double nTrees = plan.Planted.Count, sd = Math.Sqrt(nTrees * (1.0 / 3) * (2.0 / 3));
            for (int s = 0; s < 3; s++)
                Check(Math.Abs(hist[s] - nTrees / 3) <= 5 * sd + 1, "filler/species", "FILL SPECIES: group pool not uniform: " +
                    hist[0] + " / " + hist[1] + " / " + hist[2]);

            // A NaN ground reads as above the line: no tree, no area.
            var nanMask = new GladeMask { Ground = new NanGround(), TreeLine = 2000f };
            var nanPlan = new GladePlan();
            SkiGladePlanner.Plan(c.Ring, null, seed, in p, null, null, null, null, nanMask, nanPlan);
            s_Plans++;
            Check(nanPlan.Planted.Count == 0 && nanPlan.MaskedArea == 0f, "filler/nan", "FILL NAN: unknown ground got trees or area");
        }

        private sealed class NanGround : GladeHeightField
        {
            public override float HeightAt(double x, double z) => float.NaN;
            public override ulong Fingerprint => 7UL;
        }

        private static void CheckFillTrees(FillCase c, GladeMask m, List<PlantedTree> trees, float spacing, string where)
        {
            int bad = 0, close = 0, offGround = 0;
            for (int i = 0; i < trees.Count; i++)
            {
                float3 t = trees[i].Position;
                double2 q = new double2(t.x, t.z);
                if (!OracleAllowed(c, m, q) && OracleClearance(c, m, q) == 0.0)
                    bad++;
                if (m.Ground != null && Math.Abs(t.y - c.Ground.HeightAt(t.x, t.z)) > 1e-3)
                    offGround++;
                for (int j = 0; j < i; j++)
                {
                    float3 u = trees[j].Position;
                    double dx = t.x - u.x, dz = t.z - u.z;
                    if (dx * dx + dz * dz < (double)spacing * spacing - 1e-6)
                        close++;
                }
            }
            Check(bad == 0, where, "FILL REGION: " + bad + " tree(s) outside the fill region (hole, margin, tree line or outside)");
            Check(close == 0, where, "FILL SPACING: " + close + " pair(s) under MinSpacing");
            Check(offGround == 0, where, "FILL GROUND: " + offGround + " tree(s) not on the ground height");
        }

        /// <summary>How far a disallowed point is past the margin (oracle rounding slack: 0 = clearly disallowed).</summary>
        private static double OracleClearance(FillCase c, GladeMask m, double2 q)
        {
            if (!OracleInside(c.Oracle, q))
                return 0.0;
            for (int i = 0; i < c.HoleOracle.Length; i++)
            {
                if (OracleInside(c.HoleOracle[i], q))
                    return 0.0;
                double d = OracleBoundaryDistance(c.HoleOracle[i], q);
                if (d < m.HoleMargin - 1e-3)
                    return 0.0;
            }
            return m.Ground != null && c.Ground.HeightAt(q.x, q.y) > m.TreeLine + 1e-3 ? 0.0 : 1.0;
        }

        private static void FillerComposeTests(int seeds)
        {
            int cases = Math.Max(2, Math.Min(12, seeds / 15));
            for (int s = 0; s < cases; s++)
            {
                FillCase c = MakeFillCase("fill" + s, 10 + 23 * s, new double2(1000 - 300 * s, -700 + 170 * s), 0.6f + 0.03f * s);
                string where = "filler/compose/" + s;
                float3[] ex;
                bool[] th;
                int inFill;
                FillStand(c, c.Mask, s, out ex, out th, out inFill);

                // Level 6 from bare: plants T - X; never fells; X = every wild tree in the fill region.
                ComposeInput input = FillInput(c, c.Mask, 6, ex, th, null);
                var r = new ComposeResult();
                SkiGladeCompose.Compose(input, r);
                s_Plans++;
                Check(r.Layer == SkiGladeCompose.LayerFiller, where, "FILL LAYER: compose layer " + r.Layer);
                Check(r.Fell.Count == 0 && r.WildInside == 0 && r.FellFull == 0, where, "FILL FELL: the filler felled or counted wild trees");
                Check(r.FixedInside == inFill, where, "FILL X: " + r.FixedInside + " fixed inside vs oracle " + inFill + " wild in the fill");
                int t6 = r.Target;
                Check(t6 == (int)Math.Floor(SkiGladeCompose.InteriorDensity(6) * r.Plan.MaskedArea + 0.5), where,
                    "FILL T: target " + t6 + " is not round(D(6) x masked area " + F(r.Plan.MaskedArea) + ")");
                Check(r.Plant.Count == t6 - inFill || r.Shortfall > 0, where, "FILL PLANT: " + r.Plant.Count + " planted for T " + t6 + " - X " + inFill);
                var planted = new List<PlantedTree>();
                foreach (int k in r.Plant)
                    planted.Add(r.Plan.Planted[k]);
                CheckFillTrees(c, c.Mask, planted, FillParams().MinSpacing, where);
                CheckNotNear(planted, ex, FillParams().MinSpacing, where);

                // Determinism and key contract.
                var r2 = new ComposeResult();
                SkiGladeCompose.Compose(FillInput(c, c.Mask, 6, ex, th, null), r2);
                s_Plans++;
                Check(SameInts(r.Plant, r2.Plant) && Diff(r.Plan, r2.Plan) == null && r.PlanKey == r2.PlanKey &&
                      r.PlanKey == SkiGladeCompose.PlanKey(input), where, "FILL DETERMINISM: replay or PlanKey differs");

                // Idempotent: the plants standing as members, the same level changes nothing.
                ComposeMember[] own = WithPlants(null, r);
                var again = new ComposeResult();
                SkiGladeCompose.Compose(FillInput(c, c.Mask, 6, ex, th, own), again);
                s_Plans++;
                Check(again.Complete && again.Inside == r.Inside + r.Plant.Count, where,
                    "FILL IDEMPOTENT: repeat apply plant " + again.Plant.Count + " remove " + again.Remove.Count);

                // Level down then back up: exact counts, only filler members removed, no fell.
                var down = new ComposeResult();
                SkiGladeCompose.Compose(FillInput(c, c.Mask, 4, ex, th, own), down);
                s_Plans++;
                int t4 = down.Target;
                int wantRemove = Math.Max(0, own.Length - Math.Max(0, t4 - inFill));
                Check(down.Plant.Count == 0 && down.Fell.Count == 0 && down.Remove.Count == wantRemove, where,
                    "FILL DOWN: remove " + down.Remove.Count + " (want " + wantRemove + "), plant " + down.Plant.Count + ", fell " + down.Fell.Count);
                var keptAfterDown = new List<ComposeMember>();
                var removed = new HashSet<int>(down.Remove);
                for (int i = 0; i < own.Length; i++)
                {
                    if (!removed.Contains(i))
                        keptAfterDown.Add(own[i]);
                }
                var up = new ComposeResult();
                SkiGladeCompose.Compose(FillInput(c, c.Mask, 6, ex, th, keptAfterDown.ToArray()), up);
                s_Plans++;
                Check(up.Plant.Count == own.Length - keptAfterDown.Count && up.Remove.Count == 0, where,
                    "FILL UP: replant " + up.Plant.Count + " vs removed " + (own.Length - keptAfterDown.Count));

                // Undo = Clear (level 1): every filler member removed, nothing felled, wild trees untouched.
                var clear = new ComposeResult();
                SkiGladeCompose.Compose(FillInput(c, c.Mask, 1, ex, th, own), clear);
                s_Plans++;
                Check(clear.Remove.Count == own.Length && clear.Fell.Count == 0 && clear.Plant.Count == 0, where,
                    "FILL UNDO: level 1 removed " + clear.Remove.Count + " of " + own.Length + ", fell " + clear.Fell.Count);

                // Lowering the tree line removes the members above it (misplaced) and nothing else first.
                GladeMask lower = new GladeMask { Holes = c.Holes, HoleMargin = FillMargin, Ground = c.Ground, TreeLine = c.Mask.TreeLine - 0.2f * (float)(c.GroundMax - c.GroundMin) };
                int above = 0;
                foreach (ComposeMember mbr in own)
                {
                    if (c.Ground.HeightAt(mbr.Position.x, mbr.Position.z) > lower.TreeLine)
                        above++;
                }
                var low = new ComposeResult();
                SkiGladeCompose.Compose(FillInput(c, lower, 6, ex, th, own), low);
                s_Plans++;
                Check(above > 0 && low.Misplaced == above && low.Fell.Count == 0 && low.Remove.Count >= above, where,
                    "FILL LINE DOWN: misplaced " + low.Misplaced + " vs above " + above + ", removed " + low.Remove.Count);
                Check(low.PlanKey != r.PlanKey, where, "FILL KEY: the tree line is not in the PlanKey");
                c.Ground.Print ^= 1UL;
                Check(SkiGladeCompose.PlanKey(input) != r.PlanKey, where, "FILL KEY: the ground fingerprint is not in the PlanKey");
                c.Ground.Print ^= 1UL;

                // A new run drawn over filler trees: those inside it (or its margin) are misplaced and removed.
                float3[] newRun = MakeRing(D2(150, -30, 200, -30, 200, 430, 150, 430), 10 + 23 * s, new double2(1000 - 300 * s, -700 + 170 * s));
                GladeMask withRun = new GladeMask { Holes = new[] { c.Holes[0], c.Holes[1], newRun }, HoleMargin = FillMargin, Ground = c.Ground,
                    TreeLine = c.Mask.TreeLine };
                var nr = new ComposeResult();
                SkiGladeCompose.Compose(FillInput(c, withRun, 6, ex, th, own), nr);
                s_Plans++;
                Check(nr.Misplaced > 0 && nr.Fell.Count == 0, where, "FILL NEW RUN: misplaced " + nr.Misplaced);

                // Glade members of a run (layer 0) and unknown layers handed in are never removed by the filler.
                var mixed = new List<ComposeMember>(own);
                mixed.Add(new ComposeMember { Position = own[0].Position + new float3(0.1f, 0, 0.1f), Layer = SkiGladeCompose.LayerGlade, Variant = 0, PlanGen = 1 });
                mixed.Add(new ComposeMember { Position = new float3((float)c.Min.x - 100f, 0, (float)c.Min.y - 100f), Layer = SkiGladeCompose.LayerBelt, Variant = 0, PlanGen = 1 });
                var mx = new ComposeResult();
                SkiGladeCompose.Compose(FillInput(c, c.Mask, 1, ex, th, mixed.ToArray()), mx);
                s_Plans++;
                bool touched = false;
                foreach (int k in mx.Remove)
                    touched |= mixed[k].Layer != SkiGladeCompose.LayerFiller;
                Check(!touched && mx.Remove.Count == own.Length, where, "FILL FOREIGN: the filler removed a glade / belt member");

                // The tree line target ignores Fill (layer stays the belt, key unchanged).
                ComposeInput belt = FillInput(c, c.Mask, 5, ex, th, null);
                belt.TreeLine = true;
                belt.Params.MinSpacing = TBeltMinSpacing;
                ulong kFill = SkiGladeCompose.PlanKey(belt);
                belt.Fill = null;
                Check(kFill == SkiGladeCompose.PlanKey(belt), where, "FILL BELT: Fill changed the tree-line key");

                // A glade compose (no Fill) never sees a filler member as its own: unknown layer, kept as X.
                ComposeInput glade = FillInput(c, c.Mask, 1, ex, th, own);
                glade.Fill = null;
                var gr = new ComposeResult();
                SkiGladeCompose.Compose(glade, gr);
                s_Plans++;
                Check(gr.Remove.Count == 0, where, "FILL IN GLADE: a glade compose removed " + gr.Remove.Count + " filler member(s)");
            }
        }

        private static void CheckNotNear(List<PlantedTree> planted, float3[] ex, float spacing, string where)
        {
            int close = 0;
            foreach (PlantedTree t in planted)
            {
                foreach (float3 e in ex)
                {
                    double dx = t.Position.x - e.x, dz = t.Position.z - e.z;
                    if (dx * dx + dz * dz < (double)spacing * spacing - 1e-6)
                        close++;
                }
            }
            Check(close == 0, where, "FILL OCCUPANTS: " + close + " plant(s) within MinSpacing of a standing tree");
        }

        private static void TreeLineTests()
        {
            float3[] ring = MakeRing(D2(0, 0, 300, 0, 300, 300, 0, 300), 0, new double2(5000, 5000));
            var rnd = new System.Random(99);
            // 400 trees inside, heights uniform 1000..1500: q95 ~ 1475.
            var trees = new List<float3>();
            for (int i = 0; i < 400; i++)
                trees.Add(new float3(5000f + (float)rnd.NextDouble() * 300f, 1000f + i * 500f / 399f, 5000f + (float)rnd.NextDouble() * 300f));
            TreeLineEstimate e = SkiGladeTreeLine.Estimate(ring, trees.ToArray(), 900f, 1800f);
            Check(e.Source == TreeLineSource.Region && e.TreesInside == 400 && Math.Abs(e.Height - 1475f) < 2f, "treeline/region",
                "TREE LINE: region " + e.Source + " " + F(e.Height) + " (" + e.TreesInside + " inside)");
            float3[] shuffled = trees.ToArray();
            Array.Reverse(shuffled);
            Check(SkiGladeTreeLine.Estimate(ring, shuffled, 900f, 1800f).Height == e.Height, "treeline/order", "TREE LINE: order-dependent");
            Check(SkiGladeTreeLine.Estimate(ring, trees.ToArray(), 900f, 1300f).Height == 1300f, "treeline/clamp", "TREE LINE: not clamped to the ground max");

            // 10 inside + 100 around (within 500 m): Around.
            var around = new List<float3>();
            for (int i = 0; i < 10; i++)
                around.Add(new float3(5100f + i, 1200f, 5100f));
            for (int i = 0; i < 100; i++)
                around.Add(new float3(5400f + i * 3f, 1100f + i, 5150f));
            around.Add(new float3(9000f, 5000f, 9000f)); // far away: ignored
            around.Add(new float3(float.NaN, 0f, 5100f));
            e = SkiGladeTreeLine.Estimate(ring, around.ToArray(), float.NaN, float.NaN);
            Check(e.Source == TreeLineSource.Around && e.TreesInside == 10 && e.TreesAround == 110, "treeline/around",
                "TREE LINE: around " + e.Source + " inside " + e.TreesInside + " around " + e.TreesAround);

            // 6 around: the highest; 2: none (ground max, or +Inf without a range).
            var sparse = new List<float3>();
            for (int i = 0; i < 6; i++)
                sparse.Add(new float3(5100f + i, 1000f + 10f * i, 5100f));
            e = SkiGladeTreeLine.Estimate(ring, sparse.ToArray(), 900f, 1800f);
            Check(e.Source == TreeLineSource.Sparse && e.Height == 1050f, "treeline/sparse", "TREE LINE: sparse " + e.Source + " " + F(e.Height));
            e = SkiGladeTreeLine.Estimate(ring, sparse.GetRange(0, 2).ToArray(), 900f, 1800f);
            Check(e.Source == TreeLineSource.None && e.Height == 1800f, "treeline/none", "TREE LINE: none " + e.Source + " " + F(e.Height));
            e = SkiGladeTreeLine.Estimate(ring, null, float.NaN, float.NaN);
            Check(e.Source == TreeLineSource.None && float.IsPositiveInfinity(e.Height), "treeline/none", "TREE LINE: null trees");
            e = SkiGladeTreeLine.Estimate(Pts(0, 0, 1, 1), trees.ToArray(), 900f, 1800f);
            Check(e.Source == TreeLineSource.None, "treeline/degenerate", "TREE LINE: degenerate ring gave " + e.Source);
        }

        private static void SpeciesTests()
        {
            string[] conifer = { "PineTree01", "SpruceTree01" };
            string[] deciduous = { "BirchTree01", "OakTree01", "AppleTree01", "EU_AlderTree01", "EU_ChestnutTree01", "EU_PoplarTree01",
                                   "NA_HickoryTree01", "NA_LindenTree01", "NA_LondonPlaneTree01" };
            string[] palm = { "CoconutPalmTree01", "FloridaPalmTree01", "RoyalPalmTree01", "SylvesterPalmTree01" };
            foreach (string n in conifer)
                Check(SkiGladeSpecies.GroupOf(n, 3000f) == TreeGroup.Conifer, "species/vanilla", n);
            foreach (string n in deciduous)
                Check(SkiGladeSpecies.GroupOf(n, 2000f) == TreeGroup.Deciduous, "species/vanilla", n);
            foreach (string n in palm)
                Check(SkiGladeSpecies.GroupOf(n, 2000f) == TreeGroup.Palm, "species/vanilla", n);
            Check(SkiGladeSpecies.GroupOf("GreenBushWild01", 0f) == TreeGroup.Bush, "species/vanilla", "GreenBushWild01");
            // Asset names (heuristic): whole words only.
            Check(SkiGladeSpecies.GroupOf("ScotsPine_A", 2500f) == TreeGroup.Conifer, "species/asset", "ScotsPine_A");
            Check(SkiGladeSpecies.GroupOf("Douglas_Fir_02", 2500f) == TreeGroup.Conifer, "species/asset", "Douglas_Fir_02");
            Check(SkiGladeSpecies.GroupOf("FirewoodStack", 2500f) == TreeGroup.Ungrouped, "species/asset", "FirewoodStack must not read as fir");
            Check(SkiGladeSpecies.GroupOf("RedMapleTree", 2500f) == TreeGroup.Deciduous, "species/asset", "RedMapleTree");
            Check(SkiGladeSpecies.GroupOf("DatePalm", 2500f) == TreeGroup.Palm, "species/asset", "DatePalm");
            Check(SkiGladeSpecies.GroupOf("WillowShrub", 2500f) == TreeGroup.Bush, "species/asset", "WillowShrub");
            Check(SkiGladeSpecies.GroupOf("OakTree02", 0f) == TreeGroup.Bush, "species/asset", "wood 0 is a bush");
            Check(SkiGladeSpecies.GroupOf("Baobab", 2500f) == TreeGroup.Ungrouped, "species/asset", "Baobab");
            Check(SkiGladeSpecies.GroupOf(null, 2500f) == TreeGroup.Ungrouped, "species/asset", "null name");
            GladeParams p = new GladeParams();
            SkiGladeSpecies.ApplyPool(ref p, 4);
            Check(p.SpeciesCount == 4 && p.DominantShare == 0.25f, "species/pool", "ApplyPool(4)");
            SkiGladeSpecies.ApplyPool(ref p, 0);
            Check(p.SpeciesCount == 1 && p.DominantShare == 1f, "species/pool", "ApplyPool(0)");
        }
    }
}
