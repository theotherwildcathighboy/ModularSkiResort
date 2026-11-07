// Tests for the run prop placer's pure rules (RunPropPlacerRules, PlacerRing) and the cat track gun rules (SnowGunPlumeRules, HubRules.GunWhyOf).
// Oracles are literal hand values, never a second run of the code. Examples: positions on simple rings. The Pick sequence comes
// from an independent plain-JS run of the hash. Not testable headless: the entities, the settle, the delete cascade, how a prop stands on the ground.
// Engine notes: EngineNotes/RunPropPlacerRules.md

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using R = SkiResortMod.Systems.RunPropPlacerRules;
using S = SkiResortMod.Systems.SnowGunPlumeRules;

namespace ResortBatch
{
    public static class PlacerTests
    {
        private const int Groomed = (int)RunKind.Groomed, Snowmaking = (int)RunKind.Snowmaking, Night = (int)RunKind.Night,
            Natural = (int)RunKind.Natural, Track = (int)RunKind.CatTrack;

        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Constants();
            RingForm();
            ClosedCoverage();
            NodeIndependence();
            Degenerate();
            OneSidedStraight();
            OneSidedBent();
            OneSidedHairpin();
            SideFallback();
            KindsAndLights();
            Masts();
            MixShares();
            Determinism();
            Keys();
            DiffRules();
            Billing();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static bool Near(double a, double b, double eps) => Math.Abs(a - b) <= eps;

        private static PlacerRing Ring(params float[] xz)
        {
            int n = xz.Length / 2;
            var x = new float[n];
            var y = new float[n];
            var z = new float[n];
            for (int i = 0; i < n; i++)
            {
                x[i] = xz[2 * i];
                z[i] = xz[2 * i + 1];
            }
            return new PlacerRing(x, y, z, n);
        }

        private static List<PlacedProp> Plan(PlacerRing ring, int kind, int guns, int lights)
        {
            var plan = new List<PlacedProp>();
            R.Plan(ring, kind, guns, lights, plan);
            return plan;
        }

        private static List<PlacedProp> OfClass(List<PlacedProp> plan, bool masts)
        {
            var list = new List<PlacedProp>();
            foreach (PlacedProp p in plan)
            {
                if ((p.Class == RunPropDensityRules.ClassMast) == masts)
                    list.Add(p);
            }
            return list;
        }

        private static void Constants()
        {
            const string w = "placer constants";
            Check(R.SpacingOff == 0 && R.SpacingMinM == 20 && R.SpacingMaxM == 200 && R.SpacingStepM == 10, w, "range Off, 20..200, step 10");
            Check(R.GunSpacingSnowmakingM == 50 && R.GunSpacingNightM == 90 && R.LightSpacingNightM == 90 && R.GunSpacingCatTrackM == 60 && R.LightSpacingCatTrackM == 0,
                  w, "defaults 50 / 90 / 90 / 60 / off");
            Check(R.MastClearM == 4f && R.KeepPositionM == 0.25f && R.KeepYawDeg == 3f && R.TangentWindowM == 4f && R.PlanVersion == 1u, w, "the walk's constants");

            int[,] clamp = { { -5, 0 }, { 0, 0 }, { 1, 20 }, { 19, 20 }, { 20, 20 }, { 24, 20 }, { 25, 30 }, { 96, 100 }, { 199, 200 }, { 200, 200 }, { 500, 200 } };
            for (int i = 0; i < clamp.GetLength(0); i++)
                Check(R.ClampSpacing(clamp[i, 0]) == clamp[i, 1], w, $"ClampSpacing({clamp[i, 0]}) = {R.ClampSpacing(clamp[i, 0])}, expected {clamp[i, 1]}");

            Check(R.DefaultGunSpacing(Snowmaking) == 50 && R.DefaultGunSpacing(Night) == 90 && R.DefaultGunSpacing(Track) == 60, w, "gun defaults by kind");
            Check(R.DefaultGunSpacing(Groomed) == 0 && R.DefaultGunSpacing(Natural) == 0, w, "groomed and natural: no guns");
            Check(R.DefaultLightSpacing(Night) == 90 && R.DefaultLightSpacing(Track) == 0 && R.DefaultLightSpacing(Snowmaking) == 0, w, "light defaults by kind");
            // R2 (DECISIONS 577): every groomed run carries both sliders (they are its Snowmaking and Lights features). Natural runs carry none.
            Check(R.CarriesGuns(Snowmaking) && R.CarriesGuns(Night) && R.CarriesGuns(Track) && R.CarriesGuns(Groomed) && !R.CarriesGuns(Natural), w, "CarriesGuns");
            Check(R.CarriesLights(Night) && R.CarriesLights(Track) && R.CarriesLights(Snowmaking) && R.CarriesLights(Groomed) && !R.CarriesLights(Natural), w, "CarriesLights");

            // The table: no empty marker. Its weights are the prefabs' (the prefab side is in tools/check-run-props.js).
            string[] names = { "SkiSnowGunTower", "SkiSnowGunTowerLow", "SkiSnowGunTowerTall", "SkiSnowGunFan", "SkiSnowGunFanIdle", "SkiSnowGunLance", "SkiSnowGunLanceDown",
                               "SkiSnowGunLanceTall", "SkiHydrantBox", "SkiHydrantBoxBare", "SkiPisteFloodlight", "SkiPisteFloodlightLow", "SkiPisteFloodlightTall" };
            int[] weights = { 25, 13, 12, 18, 9, 17, 8, 8, 24, 16, 100, 28, 22 };
            Check(R.Props.Length == names.Length, w, "13 table entries");
            for (int i = 0; i < names.Length && i < R.Props.Length; i++)
                Check(R.Props[i].Name == names[i] && R.Props[i].Weight == weights[i], w, $"table entry {i}: {R.Props[i].Name} {R.Props[i].Weight}");
            foreach (PlacerProp p in R.Props)
                Check(p.Name != "SkiPropNone" && p.Class == RunPropDensityRules.ClassOf(p.Name, p.Name.StartsWith("SkiPisteFloodlight", StringComparison.Ordinal)),
                      w, p.Name + ": its class matches ClassOf, and the empty marker is not placed");
        }

        private static void RingForm()
        {
            const string w = "placer ring";
            // Counter-clockwise from (0,0). Perimeter 400. PointAt walks and wraps.
            PlacerRing a = Ring(0f, 0f, 100f, 0f, 100f, 100f, 0f, 100f);
            Check(a.Valid && a.N == 4 && a.Perimeter == 400.0, w, "square: 4 nodes, perimeter 400");
            Check(a.X[0] == 0f && a.Z[0] == 0f && a.X[1] == 100f && a.Z[1] == 0f, w, "square: starts at (0,0), then (100,0)");
            Check(a.LengthTo(2) == 200.0 && a.LengthTo(4) == 400.0, w, "LengthTo 200, 400");
            a.PointAt(150.0, out float x, out _, out float z);
            Check(x == 100f && z == 50f, w, $"PointAt(150) = ({x},{z})");
            a.PointAt(450.0, out x, out _, out z);
            Check(x == 50f && z == 0f, w, $"PointAt(450) wraps to ({x},{z})");
            a.PointAt(-50.0, out x, out _, out z);
            Check(x == 0f && z == 50f, w, $"PointAt(-50) wraps to ({x},{z})");

            // Height interpolates along an edge.
            var h = new PlacerRing(new[] { 0f, 100f, 100f, 0f }, new[] { 0f, 10f, 20f, 30f }, new[] { 0f, 0f, 100f, 100f }, 4);
            h.PointAt(150.0, out _, out float y, out _);
            Check(y == 15f, w, $"height at 150 = {y}");

            // Same outline, other winding or start node: the same canonical arrays.
            foreach (PlacerRing b in new[]
            {
                Ring(100f, 0f, 100f, 100f, 0f, 100f, 0f, 0f),
                Ring(0f, 100f, 100f, 100f, 100f, 0f, 0f, 0f),
                Ring(100f, 100f, 100f, 0f, 0f, 0f, 0f, 100f),
            })
            {
                bool same = b.Valid && b.N == 4;
                for (int i = 0; same && i < 4; i++)
                    same = b.X[i] == a.X[i] && b.Z[i] == a.Z[i];
                Check(same, w, "start node and winding do not change the canonical ring");
            }

            // A repeated node (within 1 cm), also across the seam, is one node.
            PlacerRing d = Ring(0f, 0f, 100f, 0f, 100.005f, 0f, 100f, 100f, 0f, 100f, 0.002f, 0.002f);
            Check(d.Valid && d.N == 4, w, $"duplicates dropped: N = {d.N}");
        }

        private static void ClosedCoverage()
        {
            const string w = "placer closed";
            Check(R.ClosedSlots(400.0, 50) == 8 && R.ClosedSlots(400.0, 90) == 4 && R.ClosedSlots(400.0, 20) == 20, w, "400 m: 8, 4, 20 slots");
            Check(R.ClosedSlots(125.0, 50) == 3, w, "2.5 rounds up");
            Check(R.ClosedSlots(10.0, 50) == 1, w, "a small ring keeps one");
            Check(R.ClosedSlots(0.0, 50) == 0 && R.ClosedSlots(400.0, 0) == 0 && R.ClosedSlots(400.0, -10) == 0, w, "no length or off: none");

            // 100 m square, guns every 50 m: slot 0 on (0,0), then round the ring. Heading along the border.
            List<PlacedProp> p = Plan(Ring(0f, 0f, 100f, 0f, 100f, 100f, 0f, 100f), Snowmaking, 50, 0);
            float[,] expected = { { 0f, 0f }, { 50f, 0f }, { 100f, 0f }, { 100f, 50f }, { 100f, 100f }, { 50f, 100f }, { 0f, 100f }, { 0f, 50f } };
            Check(p.Count == 8, w, $"square at 50 m: {p.Count} props");
            for (int i = 0; i < p.Count && i < 8; i++)
                Check(Near(p[i].X, expected[i, 0], 1e-3) && Near(p[i].Z, expected[i, 1], 1e-3), w, $"slot {i} at ({p[i].X},{p[i].Z})");
            Check(Near(p[1].FwdX, 1.0, 1e-4) && Near(p[1].FwdZ, 0.0, 1e-4), w, "slot 1 heads along the border");
            Check(Near(p[3].FwdX, 0.0, 1e-4) && Near(p[3].FwdZ, 1.0, 1e-4), w, "slot 3 heads along the border");

            // Equal gaps all the way round, the seam included, on a perimeter that does not divide evenly: 430 / 9 = 47.78.
            PlacerRing r = Ring(0f, 0f, 115f, 0f, 115f, 100f, 0f, 100f);
            p = Plan(r, Snowmaking, 50, 0);
            Check(p.Count == 9, w, $"430 m at 50 m: {p.Count} slots");
            for (int i = 1; i < p.Count; i++)
                Check(Near(p[i].S - p[i - 1].S, 430.0 / 9.0, 1e-6), w, $"gap {i}: {p[i].S - p[i - 1].S}");
            Check(Near(p[0].S + r.Perimeter - p[p.Count - 1].S, 430.0 / 9.0, 1e-6), w, "the seam's gap equals the others");
        }

        // The same outline with extra nodes on a straight side, another start node or the other winding: the same props.
        private static void NodeIndependence()
        {
            const string w = "placer nodes";
            PlacerRing plain = Ring(0f, 0f, 100f, 0f, 100f, 100f, 0f, 100f);
            PlacerRing noded = Ring(0f, 0f, 25f, 0f, 50f, 0f, 75f, 0f, 100f, 0f, 100f, 60f, 100f, 100f, 60f, 100f, 0f, 100f, 0f, 40f);
            PlacerRing reversed = Ring(0f, 100f, 100f, 100f, 100f, 0f, 0f, 0f);
            PlacerRing rotated = Ring(100f, 100f, 0f, 100f, 0f, 0f, 100f, 0f);
            foreach (int kind in new[] { Snowmaking, Night })
            {
                List<PlacedProp> a = Plan(plain, kind, 50, kind == Night ? 90 : 0);
                Check(a.Count > 0, w, "the base plan stands something");
                foreach (PlacerRing other in new[] { noded, reversed, rotated })
                {
                    List<PlacedProp> b = Plan(other, kind, 50, kind == Night ? 90 : 0);
                    bool same = b.Count == a.Count;
                    for (int i = 0; same && i < a.Count; i++)
                        same = a[i].Prop == b[i].Prop && Near(a[i].X, b[i].X, 1e-3) && Near(a[i].Z, b[i].Z, 1e-3) && Near(a[i].FwdX, b[i].FwdX, 1e-3) && Near(a[i].FwdZ, b[i].FwdZ, 1e-3);
                    Check(same, w, $"kind {kind}, {other.N} nodes: {b.Count} props, expected the base's {a.Count}");
                }
            }

            // Extra nodes on a ribbon's long side change nothing either.
            List<PlacedProp> c = Plan(Ring(0f, -5f, 300f, -5f, 300f, 5f, 0f, 5f), Track, 60, 0);
            var xz = new List<float>();
            for (float xx = 0f; xx <= 300f; xx += 2f) { xz.Add(xx); xz.Add(-5f); }
            for (float xx = 300f; xx >= 0f; xx -= 2f) { xz.Add(xx); xz.Add(5f); }
            List<PlacedProp> e = Plan(Ring(xz.ToArray()), Track, 60, 0);
            bool sameRibbon = c.Count == e.Count;
            for (int i = 0; sameRibbon && i < c.Count; i++)
                sameRibbon = c[i].Prop == e[i].Prop && Near(c[i].X, e[i].X, 1e-3) && Near(c[i].Z, e[i].Z, 1e-3);
            Check(c.Count == 5 && sameRibbon, w, $"a ribbon with nodes every 2 m stands the same {c.Count} props: {e.Count}");
        }

        private static void Degenerate()
        {
            const string w = "placer degenerate";
            var empty = new PlacerRing(new float[0], new float[0], new float[0], 0);
            var nulls = new PlacerRing(null, null, null, 4);
            PlacerRing two = Ring(0f, 0f, 10f, 0f);
            PlacerRing line = Ring(0f, 0f, 50f, 0f, 100f, 0f);
            PlacerRing same = Ring(5f, 5f, 5f, 5f, 5f, 5f, 5f, 5f);
            PlacerRing nan = Ring(0f, 0f, float.NaN, 0f, 10f, 10f, 0f, 10f);
            PlacerRing inf = Ring(0f, 0f, float.PositiveInfinity, 0f, 10f, 10f, 0f, 10f);
            var shortArrays = new PlacerRing(new[] { 0f, 1f }, new[] { 0f, 1f }, new[] { 0f, 1f }, 3);
            foreach (PlacerRing r in new[] { empty, nulls, two, line, same, nan, inf, shortArrays })
            {
                Check(!r.Valid, w, "a degenerate ring is not valid");
                Check(Plan(r, Night, 50, 50).Count == 0, w, "a degenerate ring plans nothing");
                Check(Plan(r, Track, 50, 50).Count == 0, w, "a degenerate ring plans nothing on a track");
                Check(!R.SideChain(r, out _, out _), w, "a degenerate ring has no side");
                R.RingKey(r, Night, 50, 50);
            }
            Check(Plan(null, Night, 50, 50).Count == 0 && !R.SideChain(null, out _, out _), w, "a null ring plans nothing");

            // A tiny ring keeps its one prop. A spacing that is off stands nothing.
            PlacerRing tiny = Ring(0f, 0f, 1f, 0f, 1f, 1f);
            Check(Plan(tiny, Snowmaking, 50, 0).Count == 1, w, "a 3.4 m ring keeps one prop");
            Check(Plan(tiny, Snowmaking, 0, 0).Count == 0, w, "off stands nothing");
        }

        // A straight ribbon, x 0..300 at z -5..5: caps at both ends. The chain holding (0,-5) is the side z = -5.
        private static void OneSidedStraight()
        {
            const string w = "placer ribbon straight";
            PlacerRing r = Ring(0f, -5f, 300f, -5f, 300f, 5f, 0f, 5f);
            Check(R.SideChain(r, out double start, out double end), w, "a side chain");
            Check(start == 0.0 && end == 300.0, w, $"chain {start}..{end}");

            List<PlacedProp> p = Plan(r, Track, 60, 0);
            float[] xs = { 30f, 90f, 150f, 210f, 270f };
            Check(p.Count == 5, w, $"300 m at 60 m: {p.Count} props");
            for (int i = 0; i < p.Count && i < 5; i++)
                Check(Near(p[i].X, xs[i], 1e-3) && Near(p[i].Z, -5.0, 1e-4), w, $"slot {i} at ({p[i].X},{p[i].Z})");

            // Same side for the other winding and start node (the canonical ring), and for every spacing.
            List<PlacedProp> q = Plan(Ring(300f, 5f, 300f, -5f, 0f, -5f, 0f, 5f), Track, 60, 0);
            Check(q.Count == 5 && Near(q[2].X, 150.0, 1e-3) && Near(q[2].Z, -5.0, 1e-4), w, "same side from any start");
            for (int spacing = R.SpacingMinM; spacing <= R.SpacingMaxM; spacing += R.SpacingStepM)
                foreach (PlacedProp a in Plan(r, Track, spacing, 0))
                    Check(Near(a.Z, -5.0, 1e-4) && a.X > 0f && a.X < 300f, w, $"{spacing} m: ({a.X},{a.Z}) is off the side or on a cap");
        }

        // An L ribbon: the outer side (205,-5) corner holds the chain from (0,-5) to (205,200), 410 m.
        private static void OneSidedBent()
        {
            const string w = "placer ribbon bent";
            PlacerRing r = Ring(0f, -5f, 205f, -5f, 205f, 200f, 195f, 200f, 195f, 5f, 0f, 5f);
            Check(R.SideChain(r, out double start, out double end), w, "a side chain");
            Check(start == 0.0 && end == 410.0, w, $"chain {start}..{end}");
            List<PlacedProp> p = Plan(r, Track, 60, 0);
            Check(p.Count == 7, w, $"410 m at 60 m: {p.Count} props");
            for (int i = 0; i < p.Count; i++)
            {
                bool onSide = (Near(p[i].Z, -5.0, 1e-3) && p[i].X < 205.001f) || (Near(p[i].X, 205.0, 1e-3) && p[i].Z < 200f);
                Check(onSide, w, $"slot {i}: ({p[i].X},{p[i].Z}) is not on the outer side");
                if (i > 0)
                    Check(Near(p[i].S - p[i - 1].S, 410.0 / 7.0, 1e-6), w, $"gap {i}: {p[i].S - p[i - 1].S}");
            }
        }

        // A hairpin like a generated cat track: 100 m, a half circle of radius 50 with a node every 2 m, 100 m back. One side only, whichever.
        private static void OneSidedHairpin()
        {
            const string w = "placer ribbon hairpin";
            const float half = 5f, radius = 50f;
            var cx = new List<float>();
            var cz = new List<float>();
            var nx = new List<float>();
            var nz = new List<float>();
            for (float d = 0f; d < 100f; d += 2f)
            {
                cx.Add(d); cz.Add(0f); nx.Add(0f); nz.Add(1f);
            }
            int arcSteps = (int)Math.Round(Math.PI * radius / 2.0);
            for (int i = 0; i <= arcSteps; i++)
            {
                double a = Math.PI * i / arcSteps;
                cx.Add((float)(100.0 + radius * Math.Sin(a)));
                cz.Add((float)(radius - radius * Math.Cos(a)));
                nx.Add((float)-Math.Sin(a));
                nz.Add((float)Math.Cos(a));
            }
            for (float d = 2f; d <= 100f; d += 2f)
            {
                cx.Add(100f - d); cz.Add(2f * radius); nx.Add(0f); nz.Add(-1f);
            }

            // Right edge forward, left edge back: counter-clockwise. n points to the left of travel.
            int m = cx.Count;
            var ring = new float[4 * m];
            for (int i = 0; i < m; i++)
            {
                ring[2 * i] = cx[i] - half * nx[i];
                ring[2 * i + 1] = cz[i] - half * nz[i];
                int back = 2 * m - 1 - i;
                ring[2 * back] = cx[i] + half * nx[i];
                ring[2 * back + 1] = cz[i] + half * nz[i];
            }
            PlacerRing r = Ring(ring);
            Check(r.Valid, w, "the ribbon is a valid ring");
            Check(R.SideChain(r, out double start, out double end), w, "a side chain");
            // Canonical start: (0,-5) on the outer side. Outer length 100 + pi x 55 + 100 = 372.8 (a polygon, so a touch under).
            Check(start == 0.0 && Near(end - start, 372.8, 0.3), w, $"chain {start}..{end}");

            foreach (int spacing in new[] { 20, 60, 200 })
            {
                List<PlacedProp> p = Plan(r, Track, spacing, 0);
                Check(p.Count == R.ClosedSlots(end - start, spacing), w, $"{spacing} m: {p.Count} props");
                foreach (PlacedProp a in p)
                {
                    // Distance from the hairpin's center line: the outer side is half a width to the outside of it, the inner side to the inside.
                    double along = a.X <= 100f ? Math.Abs(a.Z - radius) : Math.Sqrt((a.X - 100f) * (a.X - 100f) + (a.Z - radius) * (a.Z - radius));
                    Check(along > radius + half - 0.1, w, $"{spacing} m: ({a.X},{a.Z}) is {along:F2} m from the bend's center, not on the outer side");
                }
            }
        }

        // No two caps: the two farthest corners split the ring. Triangle (0,0), (100,0), (0,50): the chain (0,50) -> (0,0) -> (100,0) is 150 m.
        private static void SideFallback()
        {
            const string w = "placer fallback";
            PlacerRing r = Ring(0f, 0f, 100f, 0f, 0f, 50f);
            Check(R.SideChain(r, out double start, out double end), w, "a side chain");
            Check(Near(end - start, 150.0, 1e-4), w, $"chain length {end - start}");
            List<PlacedProp> p = Plan(r, Track, 60, 0);
            float[,] expected = { { 0f, 25f }, { 25f, 0f }, { 75f, 0f } };
            Check(p.Count == 3, w, $"150 m at 60 m: {p.Count} props");
            for (int i = 0; i < p.Count && i < 3; i++)
                Check(Near(p[i].X, expected[i, 0], 1e-3) && Near(p[i].Z, expected[i, 1], 1e-3), w, $"slot {i} at ({p[i].X},{p[i].Z})");

            // A circle has no corners worth a cap: about half its length, none past the chain.
            var xz = new float[2 * 40];
            for (int i = 0; i < 40; i++)
            {
                xz[2 * i] = (float)(50.0 * Math.Cos(2.0 * Math.PI * i / 40));
                xz[2 * i + 1] = (float)(50.0 * Math.Sin(2.0 * Math.PI * i / 40));
            }
            PlacerRing circle = Ring(xz);
            Check(R.SideChain(circle, out start, out end), w, "the circle has a side chain");
            Check(Near(end - start, circle.Perimeter / 2.0, 0.1), w, $"half the circle: {end - start} of {circle.Perimeter}");
        }

        private static void KindsAndLights()
        {
            const string w = "placer kinds";
            PlacerRing square = Ring(0f, 0f, 100f, 0f, 100f, 100f, 0f, 100f);
            PlacerRing ribbon = Ring(0f, -5f, 300f, -5f, 300f, 5f, 0f, 5f);
            // Every groomed preset follows its sliders (R2): the prefab no longer limits the props.
            Check(Plan(square, Groomed, 0, 0).Count == 0, w, "groomed, both off, stands nothing");
            Check(OfClass(Plan(square, Groomed, 50, 50), false).Count == 8 && OfClass(Plan(square, Groomed, 50, 50), true).Count == 8, w,
                  "groomed with both sliders on: guns and masts");
            Check(Plan(square, Natural, 50, 50).Count == 0, w, "natural stands nothing");
            Check(OfClass(Plan(square, Snowmaking, 50, 50), true).Count == 8, w, "snowmaking with its lights on stands masts");
            Check(OfClass(Plan(square, Snowmaking, 0, 50), false).Count == 0, w, "snowmaking with its guns off stands no gun");
            Check(OfClass(Plan(square, Night, 0, 50), false).Count == 0 && OfClass(Plan(square, Night, 0, 50), true).Count == 8, w, "night, guns off: masts only");
            Check(OfClass(Plan(square, Night, 50, 0), true).Count == 0 && OfClass(Plan(square, Night, 50, 0), false).Count == 8, w, "night, lights off: guns only");
            Check(Plan(square, Night, 0, 0).Count == 0, w, "both off stands nothing");

            // A cat track: no masts with its lights off, masts on the same side when lit.
            List<PlacedProp> dark = Plan(ribbon, Track, 60, 0);
            Check(OfClass(dark, true).Count == 0 && OfClass(dark, false).Count == 5, w, "a dark track: 5 guns and hydrants, no mast");
            foreach (PlacedProp p in dark)
                Check(p.Class == RunPropDensityRules.ClassGun || p.Class == RunPropDensityRules.ClassHydrant, w, "a dark track's class");
            List<PlacedProp> lit = Plan(ribbon, Track, 60, 100);
            List<PlacedProp> masts = OfClass(lit, true);
            float[] xs = { 50f, 150f, 250f };
            Check(masts.Count == 3, w, $"a lit track, 300 m at 100 m: {masts.Count} masts");
            for (int i = 0; i < masts.Count && i < 3; i++)
                Check(Near(masts[i].X, xs[i], 1e-3) && Near(masts[i].Z, -5.0, 1e-4), w, $"mast {i} at ({masts[i].X},{masts[i].Z})");
            Check(Plan(ribbon, Track, 0, 100).Count == 3 && OfClass(Plan(ribbon, Track, 0, 100), false).Count == 0, w, "a lit track with its guns off: masts only");
        }

        private static void Masts()
        {
            const string w = "placer masts";
            // Closed ring: masts at the half step, aimed inward.
            List<PlacedProp> m = Plan(Ring(0f, 0f, 100f, 0f, 100f, 100f, 0f, 100f), Night, 0, 100);
            float[,] expected =
            {
                // x, z, fwd x, fwd z
                { 50f, 0f, 0f, 1f },
                { 100f, 50f, -1f, 0f },
                { 50f, 100f, 0f, -1f },
                { 0f, 50f, 1f, 0f },
            };
            Check(m.Count == 4, w, $"square at 100 m: {m.Count} masts");
            for (int i = 0; i < m.Count && i < 4; i++)
                Check(Near(m[i].X, expected[i, 0], 1e-3) && Near(m[i].Z, expected[i, 1], 1e-3) &&
                      Near(m[i].FwdX, expected[i, 2], 1e-4) && Near(m[i].FwdZ, expected[i, 3], 1e-4),
                      w, $"mast {i} at ({m[i].X},{m[i].Z}) aims ({m[i].FwdX},{m[i].FwdZ})");

            // A border falling 15 m over its 100 m: fall 0.15, the full 25 deg toward travel. Inward is +z.
            var sloped = new PlacerRing(new[] { 0f, 100f, 100f, 0f }, new[] { 0f, -15f, -15f, 0f }, new[] { 0f, 0f, 100f, 100f }, 4);
            List<PlacedProp> s = Plan(sloped, Night, 0, 100);
            Check(s.Count == 4 && Near(s[0].FwdX, 0.42262, 1e-4) && Near(s[0].FwdZ, 0.90631, 1e-4), w, $"sloped mast aims ({s[0].FwdX},{s[0].FwdZ})");

            // No gun nearer than MastClearM along the ring. Guns at 50 m, masts at 100 m share 50, 150, 250, 350: each gun moves 4 m on.
            List<PlacedProp> both = Plan(Ring(0f, 0f, 100f, 0f, 100f, 100f, 0f, 100f), Night, 50, 100);
            List<PlacedProp> masts = OfClass(both, true), guns = OfClass(both, false);
            Check(masts.Count == 4 && guns.Count == 8, w, $"{masts.Count} masts, {guns.Count} guns");
            Check(Near(guns[1].S, 54.0, 1e-6) && Near(guns[3].S, 154.0, 1e-6), w, $"guns moved on: {guns[1].S}, {guns[3].S}");
            foreach (PlacedProp g in guns)
                foreach (PlacedProp a in masts)
                    Check(Math.Abs(g.S - a.S) >= R.MastClearM - 1e-6, w, $"gun at {g.S} too near the mast at {a.S}");

            // The same on a chain.
            List<PlacedProp> track = Plan(Ring(0f, -5f, 300f, -5f, 300f, 5f, 0f, 5f), Track, 60, 100);
            List<PlacedProp> tMasts = OfClass(track, true), tGuns = OfClass(track, false);
            Check(tGuns.Count == 5 && Near(tGuns[2].X, 154.0, 1e-3), w, $"track: the gun at 150 moves to 154: {tGuns[2].X}");
            foreach (PlacedProp g in tGuns)
                foreach (PlacedProp a in tMasts)
                    Check(Math.Abs(g.S - a.S) >= R.MastClearM - 1e-6, w, $"track gun at {g.S} too near the mast at {a.S}");
        }

        // The mix over many slots follows the weights: of 150, guns 110 and hydrants 40 (the old 200-weight group without its 50 empty markers).
        private static void MixShares()
        {
            const string w = "placer mix";
            const int slots = 30000;
            var counts = new int[R.Props.Length];
            for (int i = 0; i < slots; i++)
                counts[R.Pick(987654321u, false, i)]++;
            int total = 0;
            for (int i = 0; i < 10; i++)
            {
                total += counts[i];
                Check(Near((double)counts[i] / slots, R.Props[i].Weight / 150.0, 0.01), w, $"{R.Props[i].Name}: {(double)counts[i] / slots:F4} of the guns' walk");
            }
            Check(total == slots, w, "the guns' walk stands guns and hydrants only");
            Check(Near((counts[8] + counts[9]) / (double)slots, 40.0 / 150.0, 0.01), w, "hydrants 26.7 %");

            counts = new int[R.Props.Length];
            for (int i = 0; i < slots; i++)
                counts[R.Pick(987654321u, true, i)]++;
            Check(counts[10] + counts[11] + counts[12] == slots, w, "the masts' walk stands masts only");
            Check(Near(counts[10] / (double)slots, 100.0 / 150.0, 0.01) && Near(counts[11] / (double)slots, 28.0 / 150.0, 0.01) && Near(counts[12] / (double)slots, 22.0 / 150.0, 0.01),
                  w, $"masts {counts[10]} / {counts[11]} / {counts[12]}");
        }

        private static void Determinism()
        {
            const string w = "placer determinism";
            // The frozen sequence: an independent plain-JS run of the hash (uint arithmetic: the same on every machine).
            int[] guns = { 5, 8, 9, 3, 9, 2, 8, 0 };
            int[] masts = { 12, 10, 10, 10, 10, 10, 10, 11 };
            for (int i = 0; i < 8; i++)
            {
                Check(R.Pick(12345u, false, i) == guns[i], w, $"Pick(12345, guns, {i}) = {R.Pick(12345u, false, i)}, expected {guns[i]}");
                Check(R.Pick(12345u, true, i) == masts[i], w, $"Pick(12345, masts, {i}) = {R.Pick(12345u, true, i)}, expected {masts[i]}");
            }

            PlacerRing r = Ring(3f, 7f, 210f, 40f, 260f, 320f, 90f, 410f, -30f, 200f);
            List<PlacedProp> a = Plan(r, Night, 40, 70), b = Plan(r, Night, 40, 70);
            bool same = a.Count > 0 && a.Count == b.Count;
            for (int i = 0; same && i < a.Count; i++)
                same = a[i].Prop == b[i].Prop && a[i].X == b[i].X && a[i].Z == b[i].Z && a[i].FwdX == b[i].FwdX && a[i].FwdZ == b[i].FwdZ && a[i].S == b[i].S;
            Check(same, w, "same ring and settings: the same plan, bit for bit");

            // The seed follows the canonical start, so an edit elsewhere on the ring keeps the first slot's prefab.
            List<PlacedProp> edited = Plan(Ring(3f, 7f, 210f, 40f, 280f, 300f, 90f, 410f, -30f, 200f), Snowmaking, 40, 0);
            List<PlacedProp> original = Plan(r, Snowmaking, 40, 0);
            Check(edited[0].Prop == original[0].Prop, w, "the first slot keeps its prefab across an edit elsewhere");
        }

        private static void Keys()
        {
            const string w = "placer keys";
            PlacerRing a = Ring(10f, 10f, 110f, 10f, 110f, 110f, 10f, 110f);
            uint key = R.RingKey(a, Night, 50, 90);
            Check(R.RingKey(Ring(110f, 110f, 10f, 110f, 10f, 10f, 110f, 10f), Night, 50, 90) == key, w, "start node: same key");
            Check(R.RingKey(Ring(10f, 110f, 110f, 110f, 110f, 10f, 10f, 10f), Night, 50, 90) == key, w, "winding: same key");
            var high = new PlacerRing(new[] { 10f, 110f, 110f, 10f }, new[] { 5f, 7f, 9f, 11f }, new[] { 10f, 10f, 110f, 110f }, 4);
            Check(R.RingKey(high, Night, 50, 90) == key, w, "heights: same key (the game re-snaps a prop to the terrain)");
            Check(R.RingKey(Ring(10f, 10.01f, 110f, 10f, 110f, 110f, 10f, 110f), Night, 50, 90) == key, w, "under 0.1 m: same key");
            Check(R.RingKey(Ring(10.2f, 10f, 110f, 10f, 110f, 110f, 10f, 110f), Night, 50, 90) != key, w, "0.2 m: another key");
            Check(R.RingKey(a, Night, 60, 90) != key, w, "the gun slider: another key");
            Check(R.RingKey(a, Night, 50, 100) != key, w, "the light slider: another key");
            Check(R.RingKey(a, Snowmaking, 50, 90) != key, w, "the kind: another key");
            Check(R.RingKey(a, Night, 53, 90) == R.RingKey(a, Night, 50, 90), w, "a raw 53 clamps to the same 50");
            Check(R.RingKey(new PlacerRing(null, null, null, 0), Night, 50, 90) != key, w, "a degenerate ring has its own key");
        }

        private static List<StandingProp> Standing(List<PlacedProp> plan)
        {
            var list = new List<StandingProp>();
            foreach (PlacedProp p in plan)
                list.Add(new StandingProp { Prop = p.Prop, X = p.X, Z = p.Z, FwdX = p.FwdX, FwdZ = p.FwdZ });
            return list;
        }

        private static void DiffRules()
        {
            const string w = "placer diff";
            PlacerRing plain = Ring(0f, 0f, 100f, 0f, 100f, 100f, 0f, 100f);
            PlacerRing noded = Ring(0f, 0f, 25f, 0f, 50f, 0f, 75f, 0f, 100f, 0f, 100f, 60f, 100f, 100f, 60f, 100f, 0f, 100f, 0f, 40f);
            var create = new List<int>();
            var remove = new List<int>();

            List<PlacedProp> plan = Plan(plain, Night, 50, 90);
            List<StandingProp> stand = Standing(plan);
            R.Diff(plan, stand, create, remove);
            Check(create.Count == 0 && remove.Count == 0, w, "the same plan: nothing to do");

            // An extra node re-plans to the same props: nothing moves, nothing pops.
            R.Diff(Plan(noded, Night, 50, 90), stand, create, remove);
            Check(create.Count == 0 && remove.Count == 0, w, $"an extra node: +{create.Count} -{remove.Count}");

            // A slider change: what the new plan asks for and nothing else.
            List<PlacedProp> guns50 = Plan(plain, Snowmaking, 50, 0), guns100 = Plan(plain, Snowmaking, 100, 0);
            R.Diff(guns100, Standing(guns50), create, remove);
            Check(guns100.Count == 4, w, "100 m on the square: 4 props");
            Check(guns50.Count - remove.Count + create.Count == guns100.Count, w, "standing - removed + created = planned");
            Check(remove.Count >= 4, w, $"going from 8 to 4: {remove.Count} removed");

            // Off: everything goes.
            R.Diff(new List<PlacedProp>(), Standing(guns50), create, remove);
            Check(create.Count == 0 && remove.Count == 8, w, "off: all 8 removed");

            var one = new List<PlacedProp> { new PlacedProp { Prop = 3, X = 10f, Z = 10f, FwdX = 1f, FwdZ = 0f } };
            void Case(string what, StandingProp s, int created, int removed)
            {
                R.Diff(one, new List<StandingProp> { s }, create, remove);
                Check(create.Count == created && remove.Count == removed, w, $"{what}: +{create.Count} -{remove.Count}");
            }
            Case("the same", new StandingProp { Prop = 3, X = 10f, Z = 10f, FwdX = 1f, FwdZ = 0f }, 0, 0);
            Case("another prefab", new StandingProp { Prop = 4, X = 10f, Z = 10f, FwdX = 1f, FwdZ = 0f }, 1, 1);
            Case("not in the table", new StandingProp { Prop = -1, X = 10f, Z = 10f, FwdX = 1f, FwdZ = 0f }, 1, 1);
            Case("0.2 m off stays", new StandingProp { Prop = 3, X = 10.2f, Z = 10f, FwdX = 1f, FwdZ = 0f }, 0, 0);
            Case("0.3 m off moves", new StandingProp { Prop = 3, X = 10.3f, Z = 10f, FwdX = 1f, FwdZ = 0f }, 1, 1);
            Case("2 deg stays", new StandingProp { Prop = 3, X = 10f, Z = 10f, FwdX = 0.99939f, FwdZ = 0.0349f }, 0, 0);
            Case("4 deg moves", new StandingProp { Prop = 3, X = 10f, Z = 10f, FwdX = 0.99756f, FwdZ = 0.06976f }, 1, 1);

            // One standing prop answers one planned slot.
            var two = new List<PlacedProp> { one[0], one[0] };
            R.Diff(two, new List<StandingProp> { new StandingProp { Prop = 3, X = 10f, Z = 10f, FwdX = 1f, FwdZ = 0f } }, create, remove);
            Check(create.Count == 1 && remove.Count == 0, w, "two planned, one standing: one created");
        }

        private static void Billing()
        {
            const string w = "placer billing";
            Check(S.TrackGunsCanRun(true, 0.5f), w, "in season, cold: can run");
            Check(S.TrackGunsCanRun(true, 0.001f), w, "in season, a little cold: can run");
            Check(!S.TrackGunsCanRun(true, 0f), w, "in season, too warm: cannot");
            Check(!S.TrackGunsCanRun(false, 1f), w, "out of season: cannot");
            Check(!S.TrackGunsCanRun(true, float.NaN), w, "a NaN share: cannot");

            Check(Near(S.TrackBilledHa(24000f, true, true), 2.4, 1e-5), w, "24000 m2 while the guns can run: 2.4 ha");
            Check(S.TrackBilledHa(24000f, false, true) == 0f, w, "guns cannot run: not billed");
            Check(S.TrackBilledHa(0f, true, true) == 0f && S.TrackBilledHa(-5f, true, true) == 0f, w, "no plan area: not billed");
            Check(S.TrackBilledHa(float.NaN, true, true) == 0f && S.TrackBilledHa(float.PositiveInfinity, true, true) == 0f, w, "a bad plan area: not billed");

            // DECISIONS 563: a track whose gun value is Off stands no gun and bills nothing, in season and cold or not.
            Check(S.TrackBilledHa(24000f, true, false) == 0f, w, "guns Off, can run: not billed");
            Check(S.TrackBilledHa(24000f, false, false) == 0f, w, "guns Off, cannot run: not billed");
            Check(!R.GunsOn(R.SpacingOff) && R.GunsOn(R.SpacingMinM) && R.GunsOn(R.GunSpacingCatTrackM) && !R.GunsOn(-1), w,
                  "GunsOn: Off and the none marker (-1) are off; 20 m and the 60 m default are on");
            Check(R.GunsOn(R.DefaultGunSpacing((int)RunKind.CatTrack)), w, "a track without settings yet bills as its default (60 m, on)");
            // The hub's gun row for a track set to Off: no line (the stats system passes gunRun = bound && guns on).
            Check(HubRules.GunWhyOf(false, true, true, true, true, true) == RunGunWhy.None, w, "track set to Off: no gun line");

            // The hub's gun row for a track: the track has no pack to fill, so never "the snow is deep enough".
            foreach (bool canRun in new[] { false, true })
            {
                RunGunWhy why = HubRules.GunWhyOf(true, true, true, true, true, canRun);
                Check(why == (canRun ? RunGunWhy.Making : RunGunWhy.TooWarm), w, $"track, can run {canRun}: {why}");
                Check(why != RunGunWhy.DepthReached, w, "a track never reads depth reached");
            }
            Check(HubRules.GunWhyOf(true, true, false, true, true, false) == RunGunWhy.OutOfSeason, w, "track out of season");
            Check(HubRules.GunWhyOf(false, true, true, true, true, true) == RunGunWhy.None, w, "an unbound track: no row");
            Check(HubRules.GunWhyOf(true, false, true, true, true, true) == RunGunWhy.None, w, "snow rules off: no row");
        }
    }
}
