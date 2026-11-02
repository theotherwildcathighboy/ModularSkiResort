// The pure part of the run prop placer: where guns, hydrants and masts stand along a run's border, from the ring and the run's two
// spacings. SkiRunPropSystem.Placer.cs does the entities; tests/ResortBatch PlacerTests checks this file with literal values.
// Why a walk: vanilla area props stand only on outline nodes, one roll per node (SecondaryObjectSystem.cs:2972-3007, :3279), so a 4-corner
// run shows 0 or 1 floodlight and a generated cat track (nodes down to 2 m apart, GradeRibbon.MinSpacingM) clumps on bends.
// Canonical ring: counter-clockwise (shoelace in x,z), starting at the lexicographically smallest (x, z) vertex, so the result does not
// depend on the start node, the winding or extra nodes on a straight side. A closed ring takes N = round(perimeter / spacing) slots at
// perimeter / N, so the seam shows no gap. A cat track walks one side of its ribbon only (SideChain).
// Look only: snow and upkeep never read the spacings. Pure file: System only (no Game.dll, no Unity types). C# 9 / net48.
// Engine notes: EngineNotes/RunPropPlacerRules.md

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    /// <summary>One entry of the placer's prop table: prefab name, census class and pick weight. The weights equal the prefabs'
    /// SpawnableObject m_Probability (gate: tools/test-hub-rules.js).</summary>
    public readonly struct PlacerProp
    {
        public readonly string Name;
        public readonly byte Class;
        public readonly int Weight;

        public PlacerProp(string name, byte cls, int weight)
        {
            Name = name;
            Class = cls;
            Weight = weight;
        }
    }

    /// <summary>One prop of a plan: its entry in RunPropPlacerRules.Props, xz position, unit forward direction (the object's +Z) and
    /// the slot it came from (the pick seed).</summary>
    public struct PlacedProp
    {
        public int Prop;
        public byte Class;
        public float X, Z, FwdX, FwdZ;
        public double S;
        public int Slot;
    }

    /// <summary>A prop that stands now, for the plan diff. Prop = -1: not in the table (always replaced).</summary>
    public struct StandingProp
    {
        public int Prop;
        public float X, Z, FwdX, FwdZ;
    }

    /// <summary>A run outline in canonical form (see the file header). Valid is false for a degenerate ring: fewer than 3 distinct
    /// nodes, no enclosed area, or a NaN or infinite coordinate.</summary>
    public sealed class PlacerRing
    {
        private const float DuplicateM = 0.01f;
        private const double MinArea2 = 1e-3;

        public readonly bool Valid;
        public readonly int N;
        public readonly float[] X, Y, Z;
        public readonly double Perimeter;
        private readonly double[] m_Cum;   // length from vertex 0 to vertex i; m_Cum[N] = Perimeter

        public PlacerRing(float[] x, float[] y, float[] z, int n)
        {
            X = Y = Z = new float[0];
            m_Cum = new double[1];
            if (x == null || y == null || z == null || n < 3 || x.Length < n || y.Length < n || z.Length < n)
                return;

            var px = new List<float>(n);
            var py = new List<float>(n);
            var pz = new List<float>(n);
            for (int i = 0; i < n; i++)
            {
                if (!Finite(x[i]) || !Finite(y[i]) || !Finite(z[i]))
                    return;
                int c = px.Count;
                if (c > 0 && Close(px[c - 1], pz[c - 1], x[i], z[i]))
                    continue;
                px.Add(x[i]);
                py.Add(y[i]);
                pz.Add(z[i]);
            }
            while (px.Count > 1 && Close(px[0], pz[0], px[px.Count - 1], pz[px.Count - 1]))
            {
                px.RemoveAt(px.Count - 1);
                py.RemoveAt(py.Count - 1);
                pz.RemoveAt(pz.Count - 1);
            }
            int m = px.Count;
            if (m < 3)
                return;

            double area2 = 0.0;
            for (int i = 0; i < m; i++)
            {
                int j = i + 1 == m ? 0 : i + 1;
                area2 += (double)px[i] * pz[j] - (double)px[j] * pz[i];
            }
            if (!(Math.Abs(area2) > MinArea2))
                return;

            if (area2 < 0.0)
            {
                px.Reverse();
                py.Reverse();
                pz.Reverse();
            }

            int start = 0;
            for (int i = 1; i < m; i++)
            {
                if (px[i] < px[start] || (px[i] == px[start] && pz[i] < pz[start]))
                    start = i;
            }

            X = new float[m];
            Y = new float[m];
            Z = new float[m];
            for (int i = 0; i < m; i++)
            {
                int k = (start + i) % m;
                X[i] = px[k];
                Y[i] = py[k];
                Z[i] = pz[k];
            }
            m_Cum = new double[m + 1];
            for (int i = 0; i < m; i++)
            {
                int j = i + 1 == m ? 0 : i + 1;
                double dx = (double)X[j] - X[i], dz = (double)Z[j] - Z[i];
                m_Cum[i + 1] = m_Cum[i] + Math.Sqrt(dx * dx + dz * dz);
            }
            N = m;
            Perimeter = m_Cum[m];
            Valid = Perimeter > 0.0;
        }

        /// <summary>The length along the ring from vertex 0 to <paramref name="vertex"/> (0..N).</summary>
        public double LengthTo(int vertex) => m_Cum[vertex];

        /// <summary>The ring point at length <paramref name="s"/> (wraps), height interpolated along the edge.</summary>
        public void PointAt(double s, out float x, out float y, out float z)
        {
            double t = s - Math.Floor(s / Perimeter) * Perimeter;
            int lo = 0, hi = N;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (m_Cum[mid] <= t)
                    lo = mid;
                else
                    hi = mid;
            }
            int j = lo + 1 == N ? 0 : lo + 1;
            double len = m_Cum[lo + 1] - m_Cum[lo];
            double f = len > 0.0 ? (t - m_Cum[lo]) / len : 0.0;
            x = (float)(X[lo] + (X[j] - X[lo]) * f);
            y = (float)(Y[lo] + (Y[j] - Y[lo]) * f);
            z = (float)(Z[lo] + (Z[j] - Z[lo]) * f);
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static bool Close(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz < DuplicateM * DuplicateM;
        }
    }

    public static class RunPropPlacerRules
    {
        // --- The two per-run sliders: 0 = Off, then SpacingMinM..SpacingMaxM in steps of SpacingStepM. Mirrored in src/bindings.ts.
        public const int SpacingOff = 0;
        public const int SpacingMinM = 20;
        public const int SpacingMaxM = 200;
        public const int SpacingStepM = 10;

        // Defaults. Reference: a hand-drawn or Quick run with many nodes has a node about every 40 m, and vanilla's corner roll put a gun
        // or hydrant on 75 % of the snowmaking nodes (110 + 40 of 200 weight) and on 43 % of the night nodes, a mast on another 43 % (150 of
        // 350). So 40 / 0.75 = about 50 m on snowmaking, 40 / 0.43 = about 90 m on night. A cat track has no such reference; it starts between
        // the two with its lights off.
        public const int GunSpacingSnowmakingM = 50;
        public const int GunSpacingNightM = 90;
        public const int LightSpacingNightM = 90;
        public const int GunSpacingCatTrackM = 60;
        public const int LightSpacingCatTrackM = SpacingOff;

        /// <summary>Half the chord (m) along the ring that measures a slot's tangent and fall, so a node next to a slot cannot swing it.</summary>
        public const float TangentWindowM = 4f;

        /// <summary>A gun or hydrant closer than this along the ring (m) to a mast moves on to this distance past it.</summary>
        public const float MastClearM = 4f;

        /// <summary>A standing prop within these of a planned one (same prefab) stays; so a rebuild leaves unchanged stretches alone.</summary>
        public const float KeepPositionM = 0.25f;
        public const float KeepYawDeg = 3f;

        // Cat track ribbon: a vertex turning less than this is a node on a straight side; a cap edge turns the ring by about 180 deg in total
        // (GradeRibbon.Build: left edge forward, right edge back, two straight ends).
        private const double CollinearDeg = 1.0;
        private const double CapMinTurnDeg = 110.0;
        private const double MinChainM = 1.0;

        /// <summary>Bumped when the plan's result changes for the same input, so saved runs rebuild once.</summary>
        public const uint PlanVersion = 1;

        // Walk ids: part of the pick hash, so the mast and gun walks of one run pick independently.
        private const int WalkGuns = 1, WalkMasts = 2;

        /// <summary>Every prefab the placer stands on a border. Same mix as the vanilla groups (SkiRunSnowmakingBorder, SkiRunNightBorder)
        /// without the empty marker SkiPropNone: the spacing is the density now. Not read from the groups, so a cat track needs no
        /// AreaSubObjects on its prefab.</summary>
        public static readonly PlacerProp[] Props =
        {
            new PlacerProp("SkiSnowGunTower", RunPropDensityRules.ClassGun, 25),
            new PlacerProp("SkiSnowGunTowerLow", RunPropDensityRules.ClassGun, 13),
            new PlacerProp("SkiSnowGunTowerTall", RunPropDensityRules.ClassGun, 12),
            new PlacerProp("SkiSnowGunFan", RunPropDensityRules.ClassGun, 18),
            new PlacerProp("SkiSnowGunFanIdle", RunPropDensityRules.ClassGun, 9),
            new PlacerProp("SkiSnowGunLance", RunPropDensityRules.ClassGun, 17),
            new PlacerProp("SkiSnowGunLanceDown", RunPropDensityRules.ClassGun, 8),
            new PlacerProp("SkiSnowGunLanceTall", RunPropDensityRules.ClassGun, 8),
            new PlacerProp("SkiHydrantBox", RunPropDensityRules.ClassHydrant, 24),
            new PlacerProp("SkiHydrantBoxBare", RunPropDensityRules.ClassHydrant, 16),
            new PlacerProp("SkiPisteFloodlight", RunPropDensityRules.ClassMast, 100),
            new PlacerProp("SkiPisteFloodlightLow", RunPropDensityRules.ClassMast, 28),
            new PlacerProp("SkiPisteFloodlightTall", RunPropDensityRules.ClassMast, 22),
        };

        /// <summary>Clamp to what the slider can show: 0 or less = Off, else min..max snapped to the step.</summary>
        public static int ClampSpacing(int meters)
        {
            if (meters <= SpacingOff)
                return SpacingOff;
            int m = meters < SpacingMinM ? SpacingMinM : meters > SpacingMaxM ? SpacingMaxM : meters;
            return (m + SpacingStepM / 2) / SpacingStepM * SpacingStepM;
        }

        public static bool CarriesGuns(int runKind) => RunPropDensityRules.PropClassAllowed(runKind, RunPropDensityRules.ClassGun, false);

        /// <summary>Whether the run can be lit at all (a cat track only with lights turned up: its slider is its lit state).</summary>
        public static bool CarriesLights(int runKind) => RunPropDensityRules.PropClassAllowed(runKind, RunPropDensityRules.ClassMast, true);

        /// <summary>Whether a gun value stands guns: above Off. On a cat track it also decides the snowmaking bill (Off = none;
        /// DECISIONS 563, SkiRunPropSystem.CatTrackGunsOn); on every other kind the value is look only.</summary>
        public static bool GunsOn(int gunSpacingM) => gunSpacingM > SpacingOff;

        public static int DefaultGunSpacing(int runKind)
        {
            switch (runKind)
            {
                case (int)RunKind.Snowmaking: return GunSpacingSnowmakingM;
                case (int)RunKind.Night: return GunSpacingNightM;
                case (int)RunKind.CatTrack: return GunSpacingCatTrackM;
                default: return SpacingOff;
            }
        }

        public static int DefaultLightSpacing(int runKind)
        {
            switch (runKind)
            {
                case (int)RunKind.Night: return LightSpacingNightM;
                case (int)RunKind.CatTrack: return LightSpacingCatTrackM;
                default: return SpacingOff;
            }
        }

        /// <summary>A closed ring's slot count: round(perimeter / spacing), at least 1; 0 for an off spacing or an invalid ring.</summary>
        public static int ClosedSlots(double perimeter, int spacingM)
        {
            if (spacingM <= 0 || !(perimeter > 0.0))
                return 0;
            int n = (int)Math.Round(perimeter / spacingM, MidpointRounding.AwayFromZero);
            return n < 1 ? 1 : n;
        }

        /// <summary>The cat track's side: the arc [start, end] of the ring (lengths; end may pass the perimeter) that holds the
        /// canonical start vertex, between the ribbon's two caps. A ring without two clear caps splits at its two farthest corners.</summary>
        public static bool SideChain(PlacerRing ring, out double start, out double end)
        {
            start = end = 0.0;
            if (ring == null || !ring.Valid)
                return false;

            // Corners only: extra nodes on a straight side must not change the answer.
            var corners = new List<int>(ring.N);
            var turns = new List<double>(ring.N);
            for (int i = 0; i < ring.N; i++)
            {
                double turn = TurnDeg(ring, i);
                if (Math.Abs(turn) < CollinearDeg)
                    continue;
                corners.Add(i);
                turns.Add(turn);
            }
            int m = corners.Count;
            if (m < 3)
                return false;

            // A cap: an edge whose two ends turn the same way as the ring by CapMinTurnDeg or more together. A rectangle has four of those,
            // so the two shortest that are not neighbors win; ties go to ring order, which is canonical.
            int capA = -1, capB = -1;
            double lenA = double.MaxValue;
            for (int k = 0; k < m; k++)
            {
                int next = k + 1 == m ? 0 : k + 1;
                if (!(turns[k] + turns[next] >= CapMinTurnDeg))
                    continue;
                double len = ArcLength(ring, corners[k], corners[next]);
                if (len < lenA)
                {
                    capA = k;
                    lenA = len;
                }
            }
            if (capA >= 0)
            {
                double lenB = double.MaxValue;
                for (int k = 0; k < m; k++)
                {
                    int next = k + 1 == m ? 0 : k + 1;
                    if (k == capA || Adjacent(capA, k, m) || !(turns[k] + turns[next] >= CapMinTurnDeg))
                        continue;
                    double len = ArcLength(ring, corners[k], corners[next]);
                    if (len < lenB)
                    {
                        capB = k;
                        lenB = len;
                    }
                }
            }

            int a0, b1;   // the corner a chain ends at (start of the lower cap), the corner it starts at (end of the higher cap)
            if (capA >= 0 && capB >= 0)
            {
                int lower = Math.Min(capA, capB), higher = Math.Max(capA, capB);
                a0 = corners[lower];
                b1 = corners[higher + 1 == m ? 0 : higher + 1];
            }
            else
            {
                FarthestCorners(ring, corners, out a0, out b1);
                // Same shape as the cap case: the chain through vertex 0 runs from the higher corner on to the lower one.
                int lo = Math.Min(a0, b1), hi = Math.Max(a0, b1);
                a0 = lo;
                b1 = hi;
            }
            start = ring.LengthTo(b1);
            end = ring.LengthTo(a0);
            if (end <= start)
                end += ring.Perimeter;
            return end - start >= MinChainM;
        }

        /// <summary>The plan of one run, in order: masts, then guns and hydrants. Empty for a degenerate ring, both spacings off or a kind
        /// that carries nothing. Same ring and settings, same plan, bit for bit.</summary>
        public static void Plan(PlacerRing ring, int runKind, int gunSpacingM, int lightSpacingM, List<PlacedProp> into)
        {
            into.Clear();
            if (ring == null || !ring.Valid)
                return;
            int guns = ClampSpacing(gunSpacingM), lights = ClampSpacing(lightSpacingM);
            bool lit = lights > SpacingOff;
            bool wantGuns = guns > SpacingOff && RunPropDensityRules.PropClassAllowed(runKind, RunPropDensityRules.ClassGun, lit);
            bool wantMasts = lit && RunPropDensityRules.PropClassAllowed(runKind, RunPropDensityRules.ClassMast, lit);
            if (!wantGuns && !wantMasts)
                return;

            bool open = runKind == (int)RunKind.CatTrack;
            double start = 0.0, end = ring.Perimeter;
            if (open && !SideChain(ring, out start, out end))
                return;
            uint seed = RingSeed(ring, runKind);

            if (wantMasts)
                Walk(ring, open, start, end, lights, seed, WalkMasts, null, into);
            if (wantGuns)
            {
                double[] masts = null;
                if (into.Count > 0)
                {
                    masts = new double[into.Count];
                    for (int i = 0; i < masts.Length; i++)
                        masts[i] = into[i].S;
                }
                Walk(ring, open, start, end, guns, seed, WalkGuns, masts, into);
            }
        }

        /// <summary>A hash of what the plan depends on, independent of start node and winding (not of extra nodes: those replan to the
        /// same props, so nothing changes). Heights are left out: the game re-snaps these props to the terrain itself
        /// (GroundHeightSystem.cs:229, :604; EngineNotes/SkiRunPropMember.md).</summary>
        public static uint RingKey(PlacerRing ring, int runKind, int gunSpacingM, int lightSpacingM)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = Mix(h, PlanVersion);
                h = Mix(h, (uint)runKind);
                h = Mix(h, (uint)ClampSpacing(gunSpacingM));
                h = Mix(h, (uint)ClampSpacing(lightSpacingM));
                if (ring == null || !ring.Valid)
                    return Mix(h, 0xFFFFFFFFu);
                for (int i = 0; i < ring.N; i++)
                {
                    h = Mix(h, (uint)(int)Math.Round(ring.X[i] * 10.0));
                    h = Mix(h, (uint)(int)Math.Round(ring.Z[i] * 10.0));
                }
                return h;
            }
        }

        /// <summary>What to do to reach the plan: plan entries with no standing match go to <paramref name="create"/>, standing props no
        /// plan entry matched go to <paramref name="remove"/> (indices). A match has the same prefab, a position within KeepPositionM and a
        /// forward within KeepYawDeg. Each standing prop matches once.</summary>
        public static void Diff(List<PlacedProp> plan, List<StandingProp> standing, List<int> create, List<int> remove)
        {
            create.Clear();
            remove.Clear();
            var used = new bool[standing.Count];
            for (int p = 0; p < plan.Count; p++)
            {
                PlacedProp want = plan[p];
                int match = -1;
                for (int s = 0; s < standing.Count; s++)
                {
                    if (used[s] || standing[s].Prop != want.Prop)
                        continue;
                    float dx = standing[s].X - want.X, dz = standing[s].Z - want.Z;
                    if (dx * dx + dz * dz > KeepPositionM * KeepPositionM)
                        continue;
                    if (MastAimRules.AngleDeg(standing[s].FwdX, standing[s].FwdZ, want.FwdX, want.FwdZ) > KeepYawDeg)
                        continue;
                    match = s;
                    break;
                }
                if (match >= 0)
                    used[match] = true;
                else
                    create.Add(p);
            }
            for (int s = 0; s < used.Length; s++)
            {
                if (!used[s])
                    remove.Add(s);
            }
        }

        /// <summary>The entry a slot stands: a stable hash of the ring seed, the walk and the slot, weighted over the walk's classes.</summary>
        public static int Pick(uint seed, bool mastWalk, int slot)
        {
            int total = 0;
            for (int i = 0; i < Props.Length; i++)
            {
                if (InWalk(Props[i].Class, mastWalk))
                    total += Props[i].Weight;
            }
            if (total <= 0)
                return -1;
            uint r = Hash(seed, mastWalk ? WalkMasts : WalkGuns, slot) % (uint)total;
            for (int i = 0; i < Props.Length; i++)
            {
                if (!InWalk(Props[i].Class, mastWalk))
                    continue;
                if (r < (uint)Props[i].Weight)
                    return i;
                r -= (uint)Props[i].Weight;
            }
            return -1;
        }

        private static bool InWalk(byte cls, bool mastWalk) => mastWalk
            ? cls == RunPropDensityRules.ClassMast
            : cls == RunPropDensityRules.ClassGun || cls == RunPropDensityRules.ClassHydrant;

        private static void Walk(PlacerRing ring, bool open, double start, double end, int spacingM, uint seed, int walk, double[] clear,
            List<PlacedProp> into)
        {
            double length = end - start;
            int count = ClosedSlots(length, spacingM);
            if (count <= 0)
                return;
            double step = length / count;
            bool mastWalk = walk == WalkMasts;
            // A closed ring has no ends: slot 0 stands on the canonical start vertex. A chain keeps half a step off each end. Masts take the
            // half-step phase on a closed ring too, away from the guns' slot 0.
            double phase = open || mastWalk ? 0.5 : 0.0;
            for (int i = 0; i < count; i++)
            {
                double s = start + (i + phase) * step;
                if (clear != null)
                    s = ClearOfMasts(s, clear, open, start, end, ring.Perimeter);
                int prop = Pick(seed, mastWalk, i);
                if (prop < 0)
                    continue;
                if (!SlotAt(ring, s, open, start, end, out float x, out float z, out float tx, out float tz, out float fall))
                    continue;
                float fx = tx, fz = tz;
                if (mastWalk && !MastAimRules.AimFromTangent(tx, tz, true, fall, out fx, out fz))
                    continue;
                into.Add(new PlacedProp
                {
                    Prop = prop,
                    Class = Props[prop].Class,
                    X = x,
                    Z = z,
                    FwdX = fx,
                    FwdZ = fz,
                    S = s,
                    Slot = i,
                });
            }
        }

        private static double ClearOfMasts(double s, double[] masts, bool open, double start, double end, double perimeter)
        {
            for (int i = 0; i < masts.Length; i++)
            {
                double d = s - masts[i];
                if (!open)
                    d -= Math.Round(d / perimeter) * perimeter;
                if (Math.Abs(d) >= MastClearM)
                    continue;
                double moved = masts[i] + MastClearM;
                if (open && moved > end)
                    moved = masts[i] - MastClearM;
                return open && moved < start ? s : moved;
            }
            return s;
        }

        private static bool SlotAt(PlacerRing ring, double s, bool open, double start, double end, out float x, out float z, out float tx,
            out float tz, out float fall)
        {
            ring.PointAt(s, out x, out float y, out z);
            double back = s - TangentWindowM, ahead = s + TangentWindowM;
            if (open)
            {
                back = back < start ? start : back;
                ahead = ahead > end ? end : ahead;
            }
            ring.PointAt(back, out float bx, out float by, out float bz);
            ring.PointAt(ahead, out float ax, out float ay, out float az);
            float dx = ax - bx, dz = az - bz;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (!(len > 1e-3f))
            {
                tx = tz = fall = 0f;
                return false;
            }
            tx = dx / len;
            tz = dz / len;
            fall = (by - ay) / len;
            return true;
        }

        /// <summary>The ring's exterior turn at vertex i in degrees: positive turns with the (counter-clockwise) ring.</summary>
        private static double TurnDeg(PlacerRing ring, int i)
        {
            int p = i == 0 ? ring.N - 1 : i - 1, q = i + 1 == ring.N ? 0 : i + 1;
            double ax = (double)ring.X[i] - ring.X[p], az = (double)ring.Z[i] - ring.Z[p];
            double bx = (double)ring.X[q] - ring.X[i], bz = (double)ring.Z[q] - ring.Z[i];
            return Math.Atan2(ax * bz - az * bx, ax * bx + az * bz) * 180.0 / Math.PI;
        }

        private static double ArcLength(PlacerRing ring, int from, int to)
        {
            double a = ring.LengthTo(from), b = ring.LengthTo(to);
            return b > a ? b - a : b - a + ring.Perimeter;
        }

        private static bool Adjacent(int a, int b, int m)
        {
            int d = Math.Abs(a - b);
            return d <= 1 || d == m - 1;
        }

        private static void FarthestCorners(PlacerRing ring, List<int> corners, out int a, out int b)
        {
            a = corners[0];
            b = corners[1];
            double best = -1.0;
            for (int i = 0; i < corners.Count; i++)
            {
                for (int j = i + 1; j < corners.Count; j++)
                {
                    double dx = (double)ring.X[corners[i]] - ring.X[corners[j]], dz = (double)ring.Z[corners[i]] - ring.Z[corners[j]];
                    double d2 = dx * dx + dz * dz;
                    if (d2 > best)
                    {
                        best = d2;
                        a = corners[i];
                        b = corners[j];
                    }
                }
            }
        }

        /// <summary>The pick seed: the canonical start vertex and the kind, so an edit elsewhere on the ring keeps the mix.</summary>
        private static uint RingSeed(PlacerRing ring, int runKind)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = Mix(h, (uint)(int)Math.Round(ring.X[0] * 10.0));
                h = Mix(h, (uint)(int)Math.Round(ring.Z[0] * 10.0));
                return Mix(h, (uint)runKind);
            }
        }

        private static uint Hash(uint seed, int walk, int slot)
        {
            unchecked
            {
                uint h = Mix(Mix(seed, (uint)walk), (uint)slot);
                h ^= h >> 15;
                h *= 2654435761u;
                h ^= h >> 13;
                return h;
            }
        }

        private static uint Mix(uint h, uint v)
        {
            unchecked
            {
                return (h ^ v) * 16777619u;
            }
        }
    }
}
