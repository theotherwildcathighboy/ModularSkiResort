// RunsFirst scorecard: judges a resort plan from its geometry alone (runs, connectors, lifts) on the raw heightmap, independent of how
// the planner got there. The planner hands over a ScorePlan; nothing it says about itself (bands, served lines, credits) is trusted.
//
// Skiing model: a skier starts at a lift top, takes any element (run or connector) starting within StartM of it, and may continue onto
// any element it ends on (or that branches off it) within JoinM; an element that passes within StationM of a lift bottom delivers the
// skier to that lift. A run is served when a lift top reaches it AND it delivers to some lift bottom. Lift A links to lift B when a
// skier leaving A's top reaches B's bottom without riding another lift; the network share is the largest strongly connected set of
// lifts over all lifts (1 = every lift can get to every other one and back). A gondola or tram also carries skiers down: an element
// ending within StationM of its top delivers to it.
//
// v2 (after the 2026-10-29 contest; all four entries flagged the same faults): crowding no longer counts the junction zone of two runs
// (within JunctionM of the other run's start or end: a shared top, a branch, a merge); a lift's own green run (from its top back to its
// bottom) may run under its chord, as a learning chair does; a base is a cluster of two or more lift bottoms (or the lowest cluster
// when none has two), so a lone low station no longer demands its own learning zone.
//
// v3 (the owner's rules, 2026-10-29): a run is rated by the ground under it, as the game's classifier rates a painted run (85th
// percentile of the terrain grade along it); a connector is a graded cat track (built with the grading tool, a bench across the
// face) and is rated by its own grade along the path: up to the blue line it is a cat track (green), steeper it is a diagonal run
// (blue or harder) and counts as a run, not as connector km. Lifts have no maximum length or rise (the mod's lift tool has none);
// minimums and the mean-grade cap stay. A gondola or tram may cross a chair (pending an in-game check). Riding down is only on a
// gondola or tram. Beginner terrain may be anywhere a beginner gets to from the base: up lifts whose tops have green skiing, or on a
// gondola or tram both ways (Palisades: the learning area at mid-mountain). Every lift needs an easy way home: a green or blue route
// from its top to a base lift's bottom, or to a gondola or tram top to ride down (a gondola or tram is its own way home).
//
// Composite (0-100): served vertical 15, skiable coverage 10, lift network 15, beginner terrain at each base 15, easy way home 10,
// no dead lifts 10, validity 10, few stubs 5, little crowding 5, connector economy 5. Fixed contract: planners must not edit this file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    /// <summary>A plan as the scorecard reads it. Runs and connectors run in the skiing direction (first point = where a skier starts).
    /// Heights in the points are ignored: the scorecard samples the terrain itself.</summary>
    internal sealed class ScorePlan
    {
        public readonly List<List<float2>> Runs = new List<List<float2>>();
        public readonly List<List<float2>> Connectors = new List<List<float2>>();   // traverses, cat tracks, way-home tracks
        public readonly List<(string Type, float2 Bottom, float2 Top)> Lifts = new List<(string, float2, float2)>();
    }

    internal static class RunsFirstScore
    {
        private const float StartM = 150f, JoinM = 40f, StationM = 60f, SampleM = 10f;
        private const float RunMaxPitch = 1.0f, RunClimbM = 10f;   // raw 3.5 m terrain: small bumps and 45-degree pitches are real
        private const float ConnMaxDown = 0.50f, ConnWindowM = 50f, ConnMaxRiseM = 4.5f;
        private const float TowerM = 20f, StationGrade = 0.40f, OnRunM = 20f;
        private const float StubDropM = 50f, CrowdM = 40f, JunctionM = 100f, CoverM = 50f, CoverTarget = 0.6f, ConnectorShare = 0.35f, BaseShare = 0.35f;

        // name, length min / max (m), rise min / max (m), mean grade cap (stage-a-spec 4.4). v3: the maxima are not checked (no lift
        // length limit in the mod); they stay as the planner's type ladder
        private static readonly (string Name, float L0, float L1, float R0, float R1, float Cap)[] Types =
        {
            ("fixed-grip", 300f, 1500f, 95f, 512f, 0.8f),
            ("detachable", 600f, 2200f, 95f, 512f, 0.9f),
            ("gondola", 800f, 2700f, 96f, 781f, 1.2f),
            ("tram", 800f, 3000f, 220f, 888f, 2.5f),   // the mod has no surface lifts: a beginner pod is a fixed-grip chair
        };

        private sealed class Elem
        {
            public bool Run;
            public float3[] P;
            public bool Graded;   // a connector graded steeper than the blue line: a diagonal run
            public float Drop, Length;
            public SlopeBand Band;
        }

        public sealed class Result
        {
            public float Total, Served, Coverage, Network, Beginner, WayHome, DeadFree, Validity, StubFree, CrowdFree, Economy;
            public int Lifts, Bases, BeginnerBases, WayHomeLifts, Dead, Violations, ServedRuns, Runs;
            public float[] BandKm = new float[5];
            public float ServedVerticalM, ConnectorKm;
            public List<string> Notes = new List<string>();
        }

        public static Result Evaluate(HeightSnapshot s, float2[] ring, ScorePlan plan)
        {
            var r = new Result();
            float H(float2 p) => s.Sample(new float3(p.x, 0f, p.y));
            float Grade(float2 p)
            {
                const float d = 10f;
                float gx = (H(p + new float2(d, 0f)) - H(p - new float2(d, 0f))) / (2f * d);
                float gz = (H(p + new float2(0f, d)) - H(p - new float2(0f, d))) / (2f * d);
                return math.sqrt(gx * gx + gz * gz);
            }

            // ---- elements, resampled every 10 m on the terrain
            var els = new List<Elem>();
            foreach (List<float2> pl in plan.Runs)
                if (pl != null && pl.Count >= 2)
                    els.Add(Make(pl, true));
            foreach (List<float2> pl in plan.Connectors)
                if (pl != null && pl.Count >= 2)
                    els.Add(Make(pl, false));
            Elem Make(List<float2> pl, bool run)
            {
                var pts = new List<float3>();
                for (int i = 0; i + 1 < pl.Count; i++)
                {
                    float len = math.distance(pl[i], pl[i + 1]);
                    int k = Math.Max(1, (int)Math.Ceiling(len / SampleM));
                    for (int j = 0; j < k; j++)
                    {
                        float2 q = math.lerp(pl[i], pl[i + 1], j / (float)k);
                        pts.Add(new float3(q.x, H(q), q.y));
                    }
                }
                pts.Add(new float3(pl[pl.Count - 1].x, H(pl[pl.Count - 1]), pl[pl.Count - 1].y));
                var e = new Elem { Run = run, P = pts.ToArray() };
                for (int i = 1; i < e.P.Length; i++)
                    e.Length += math.distance(e.P[i].xz, e.P[i - 1].xz);
                e.Drop = e.P[0].y - e.P[e.P.Length - 1].y;
                return e;
            }

            int violations = 0;
            void Bad(string why)
            {
                violations++;
                if (r.Notes.Count < 40)
                    r.Notes.Add(why);
            }

            // ---- validity and bands
            for (int i = 0; i < els.Count; i++)
            {
                Elem e = els[i];
                string name = (e.Run ? "run " : "connector ") + (i + 1);
                int outside = e.P.Count(p => !InsideRing(ring, p.xz));
                if (outside > 0.1f * e.P.Length)
                    Bad($"{name}: {100 * outside / e.P.Length}% outside the region");
                if (e.Run)
                {
                    var ps = new List<float>();
                    for (int k = 0; k + 3 < e.P.Length; k++)
                        ps.Add(Math.Max(0f, e.P[k].y - e.P[k + 3].y) / math.max(1f, math.distance(e.P[k].xz, e.P[k + 3].xz)));
                    ps.Sort();
                    // the band: the ground under the run, as the game rates a painted run (a traverse across a black face is black)
                    var gs = e.P.Select(p => Grade(p.xz)).OrderBy(x => x).ToList();
                    float rating = gs.Count > 0 ? gs[(int)((gs.Count - 1) * 0.85f)] : 0f;
                    e.Band = SkiSlopeClassifier.BandOf(rating);
                    if (e.Band == SlopeBand.Flat)
                        e.Band = SlopeBand.Green;
                    if (ps.Count > 0 && ps[ps.Count - 1] > RunMaxPitch)
                        Bad($"{name}: a 30 m pitch of {ps[ps.Count - 1]:F2} (max {RunMaxPitch})");
                    float lo = float.MaxValue;
                    foreach (float3 p in e.P)
                    {
                        if (p.y > lo + RunClimbM)
                        {
                            Bad($"{name}: climbs {p.y - lo:F0} m");
                            break;
                        }
                        lo = Math.Min(lo, p.y);
                    }
                    if (e.Drop <= 0f)
                        Bad($"{name}: ends higher than it starts");
                }
                else
                {
                    int w = (int)(ConnWindowM / SampleM);
                    for (int k = 0; k + w < e.P.Length; k++)
                        if (e.P[k + w].y - e.P[k].y > ConnMaxRiseM)
                        {
                            Bad($"{name}: climbs {e.P[k + w].y - e.P[k].y:F1} m in {ConnWindowM} m");
                            break;
                        }
                    for (int k = 0; k + 3 < e.P.Length; k++)
                        if ((e.P[k].y - e.P[k + 3].y) / math.max(1f, math.distance(e.P[k].xz, e.P[k + 3].xz)) > ConnMaxDown)
                        {
                            Bad($"{name}: a 30 m pitch steeper than {ConnMaxDown} (a run, not a connector)");
                            break;
                        }
                    // graded: rated by its own grade along the path (30 m of path, not of chord)
                    var cum = new float[e.P.Length];
                    for (int k = 1; k < e.P.Length; k++)
                        cum[k] = cum[k - 1] + math.distance(e.P[k].xz, e.P[k - 1].xz);
                    var cs = new List<float>();
                    for (int k = 0, j = 0; k < e.P.Length; k++)
                    {
                        j = Math.Max(j, k);
                        while (j < e.P.Length - 1 && cum[j] - cum[k] < 30f)
                            j++;
                        if (cum[j] - cum[k] >= 20f)
                            cs.Add(Math.Max(0f, e.P[k].y - e.P[j].y) / (cum[j] - cum[k]));
                    }
                    cs.Sort();
                    float along = cs.Count > 0 ? cs[(int)((cs.Count - 1) * 0.85f)] : 0f;
                    if (along >= SkiSlopeClassifier.GradeBlue && e.Drop > 0f)
                    {
                        e.Run = e.Graded = true;
                        e.Band = SkiSlopeClassifier.BandOf(along);
                    }
                    else
                    {
                        e.Band = SlopeBand.Green;
                        r.ConnectorKm += e.Length / 1000f;
                    }
                }
            }

            var lifts = plan.Lifts;
            var top = new float3[lifts.Count];
            var bot = new float3[lifts.Count];
            for (int i = 0; i < lifts.Count; i++)
            {
                (string type, float2 b, float2 t) = lifts[i];
                bot[i] = new float3(b.x, H(b), b.y);
                top[i] = new float3(t.x, H(t), t.y);
                string name = $"lift {i + 1} ({type})";
                float len = math.distance(bot[i], top[i]), rise = top[i].y - bot[i].y, plan2 = math.distance(b, t);
                int ti = Array.FindIndex(Types, x => x.Name == type);
                if (ti < 0)
                    Bad($"{name}: unknown type");
                else
                {
                    var ty = Types[ti];
                    if (len < ty.L0 || rise < ty.R0 || rise / math.max(1f, plan2) > ty.Cap)
                        Bad($"{name}: {len:F0} m / {rise:F0} m up is outside {type} norms");
                }
                if (!InsideRing(ring, b) || !InsideRing(ring, t))
                    Bad($"{name}: a station outside the region");
                for (float d = SampleM; d < plan2 - SampleM; d += SampleM)
                {
                    float f = d / plan2;
                    float2 q = math.lerp(b, t, f);
                    if (H(q) - math.lerp(bot[i].y, top[i].y, f) > TowerM)
                    {
                        Bad($"{name}: terrain {H(q) - math.lerp(bot[i].y, top[i].y, f):F0} m above the chord");
                        break;
                    }
                }
                if (Grade(b) > StationGrade || Grade(t) > StationGrade)
                    Bad($"{name}: station on a {Math.Max(Grade(b), Grade(t)):F2} grade (max {StationGrade})");
                bool Aerial(string ty) => ty == "gondola" || ty == "tram";
                for (int j = 0; j < i; j++)
                    if (Aerial(type) == Aerial(lifts[j].Type) && SegCross(lifts[j].Bottom, lifts[j].Top, b, t))
                        Bad($"{name}: crosses lift {j + 1}");
                // a chord whose middle runs along a run (not the lift's own green run, which a learning chair rides over)
                bool Own(Elem e) => e.Band == SlopeBand.Green && math.distancesq(e.P[0].xz, t) < StartM * StartM
                    && math.distancesq(e.P[e.P.Length - 1].xz, b) < StationM * StationM;
                int n = 0, near = 0;
                for (float f = 0.15f; f <= 0.85f; f += SampleM / math.max(SampleM, plan2), n++)
                {
                    float2 q = math.lerp(b, t, f);
                    if (els.Any(e => e.Run && !Own(e) && e.P.Any(p => math.distancesq(p.xz, q) < OnRunM * OnRunM)))
                        near++;
                }
                if (n > 0 && near > 0.3f * n)
                    Bad($"{name}: {100 * near / n}% of the chord's middle lies on a run");
            }
            // lift budget: 1.5 per km2 + 2 (Snowmass runs about 1.6 per km2 of skiable terrain)
            double km2 = Area(ring) / 1e6;
            int budget = (int)Math.Ceiling(1.5 * km2) + 2;
            for (int i = budget; i < lifts.Count; i++)
                Bad($"lift {i + 1}: over the lift budget of {budget} for {km2:F1} km2");
            r.Violations = violations;
            r.Validity = (float)Math.Exp(-violations / 10.0);

            // ---- the skiing graph
            int m = els.Count;
            var next = new List<int>[m];
            for (int i = 0; i < m; i++)
            {
                next[i] = new List<int>();
                float3 end = els[i].P[els[i].P.Length - 1];
                for (int j = 0; j < m; j++)
                {
                    if (i == j)
                        continue;
                    float3 st = els[j].P[0];
                    bool ok = els[j].P.Any(p => math.distancesq(p.xz, end.xz) < JoinM * JoinM && p.y <= end.y + 5f)
                        || els[i].P.Any(p => math.distancesq(p.xz, st.xz) < JoinM * JoinM && st.y <= p.y + 5f);
                    if (ok)
                        next[i].Add(j);
                }
            }
            var delivers = new List<int>[m];   // lift bottoms an element passes (and gondola / tram tops it ends at: ride down)
            var atBottom = new List<int>[m];   // lift bottoms only
            for (int i = 0; i < m; i++)
            {
                delivers[i] = new List<int>();
                atBottom[i] = new List<int>();
                for (int L = 0; L < lifts.Count; L++)
                    if (els[i].P.Skip(1).Any(p => math.distancesq(p.xz, bot[L].xz) < StationM * StationM && p.y >= bot[L].y - 15f))
                        atBottom[i].Add(L);
                for (int L = 0; L < lifts.Count; L++)
                    if (els[i].P.Skip(1).Any(p => math.distancesq(p.xz, bot[L].xz) < StationM * StationM && p.y >= bot[L].y - 15f)
                        || ((lifts[L].Type == "gondola" || lifts[L].Type == "tram")
                            && math.distancesq(els[i].P[els[i].P.Length - 1].xz, top[L].xz) < StationM * StationM))
                        delivers[i].Add(L);   // a gondola or tram top: the skier rides it down
            }
            // per lift: elements skied from its top, lift bottoms reached
            var skied = new HashSet<int>[lifts.Count];
            var reaches = new HashSet<int>[lifts.Count];
            for (int L = 0; L < lifts.Count; L++)
            {
                skied[L] = new HashSet<int>();
                reaches[L] = new HashSet<int>();
                var q = new Queue<int>();
                for (int i = 0; i < m; i++)
                    if (math.distancesq(els[i].P[0].xz, top[L].xz) < StartM * StartM && skied[L].Add(i))
                        q.Enqueue(i);
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    foreach (int b in delivers[i])
                        reaches[L].Add(b);
                    foreach (int j in next[i])
                        if (skied[L].Add(j))
                            q.Enqueue(j);
                }
            }
            // an element returns when it, or anything it leads to, delivers to a lift bottom
            var returns = new bool[m];
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < m; i++)
                    if (!returns[i] && (delivers[i].Count > 0 || next[i].Any(j => returns[j])))
                        returns[i] = changed = true;
            }
            var reached = new bool[m];
            for (int L = 0; L < lifts.Count; L++)
                foreach (int i in skied[L])
                    reached[i] = true;

            float allDrop = 0f, servedDrop = 0f;
            int stubs = 0;
            var served = new List<int>();
            for (int i = 0; i < m; i++)
            {
                if (!els[i].Run)
                    continue;
                r.Runs++;
                allDrop += Math.Max(0f, els[i].Drop);
                if (reached[i] && returns[i])
                {
                    served.Add(i);
                    servedDrop += Math.Max(0f, els[i].Drop);
                    r.BandKm[(int)els[i].Band] += els[i].Length / 1000f;
                    if (els[i].Drop < StubDropM)
                        stubs++;
                }
            }
            r.ServedRuns = served.Count;
            r.ServedVerticalM = servedDrop;
            r.Served = allDrop > 0f ? servedDrop / allDrop : 0f;
            r.StubFree = served.Count > 0 ? 1f - stubs / (float)served.Count : 0f;

            // ---- lift network: largest strongly connected set of lifts
            r.Lifts = lifts.Count;
            int best = 0;
            for (int L = 0; L < lifts.Count; L++)
            {
                var fwd = Closure(L, x => reaches[x]);
                int scc = 0;
                foreach (int M in fwd)
                    if (Closure(M, x => reaches[x]).Contains(L))
                        scc++;
                best = Math.Max(best, Math.Max(1, scc));
            }
            HashSet<int> Closure(int start, Func<int, IEnumerable<int>> adj)
            {
                var seen = new HashSet<int> { start };
                var st = new Stack<int>();
                st.Push(start);
                while (st.Count > 0)
                    foreach (int y in adj(st.Pop()))
                        if (seen.Add(y))
                            st.Push(y);
                return seen;
            }
            r.Network = lifts.Count > 0 ? best / (float)lifts.Count : 0f;

            // ---- dead lifts: nothing served leaves the top
            for (int L = 0; L < lifts.Count; L++)
                if (!skied[L].Any(i => els[i].Run && returns[i]))
                {
                    r.Dead++;
                    r.Notes.Add($"lift {L + 1}: dead (no served run from its top)");
                }
            r.DeadFree = lifts.Count > 0 ? 1f - r.Dead / (float)lifts.Count : 0f;
            float servedKm = served.Sum(i => els[i].Length) / 1000f;
            r.Economy = r.ConnectorKm <= ConnectorShare * servedKm ? 1f : ConnectorShare * servedKm / r.ConnectorKm;   // connectors past 35 % of the served run km cost

            // ---- skiable coverage on a 20 m lattice
            float minX = ring.Min(p => p.x), maxX = ring.Max(p => p.x), minZ = ring.Min(p => p.y), maxZ = ring.Max(p => p.y);
            float hLo = float.MaxValue, hHi = float.MinValue;
            var hash = new Dictionary<long, List<float2>>();
            long Key(float2 p) => ((long)Math.Floor(p.x / CoverM) << 32) ^ (uint)(int)Math.Floor(p.y / CoverM);
            foreach (int i in served)
                foreach (float3 p in els[i].P)
                {
                    long k = Key(p.xz);
                    if (!hash.TryGetValue(k, out List<float2> l))
                        hash[k] = l = new List<float2>();
                    l.Add(p.xz);
                }
            bool Covered(float2 p)
            {
                int cx = (int)Math.Floor(p.x / CoverM), cz = (int)Math.Floor(p.y / CoverM);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (hash.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out List<float2> l) && l.Any(q => math.distancesq(q, p) < CoverM * CoverM))
                            return true;
                return false;
            }
            int skiable = 0, covered = 0;
            for (float z = minZ + 10f; z < maxZ; z += 20f)
                for (float x = minX + 10f; x < maxX; x += 20f)
                {
                    var p = new float2(x, z);
                    if (!InsideRing(ring, p))
                        continue;
                    float h = H(p);
                    hLo = Math.Min(hLo, h);
                    hHi = Math.Max(hHi, h);
                    float gr = Grade(p);
                    if (gr < 0.06f || gr > 0.85f)
                        continue;
                    skiable++;
                    if (Covered(p))
                        covered++;
                }
            r.Coverage = skiable > 0 ? Math.Min(1f, covered / (float)skiable / CoverTarget) : 0f;

            // ---- crowding: served run samples within 40 m of another served run (the last 150 m, where runs meet, and the junction zone
            // within JunctionM of the other run's start or end excluded)
            int pts = 0, crowded = 0;
            foreach (int i in served)
            {
                int tail = (int)(150f / SampleM);
                for (int k = 0; k < els[i].P.Length - tail; k++)
                {
                    pts++;
                    float2 p = els[i].P[k].xz;
                    if (served.Any(j => j != i && math.distancesq(els[j].P[0].xz, p) >= JunctionM * JunctionM
                            && math.distancesq(els[j].P[els[j].P.Length - 1].xz, p) >= JunctionM * JunctionM
                            && els[j].P.Any(q => math.distancesq(q.xz, p) < CrowdM * CrowdM)))
                        crowded++;
                }
            }
            r.CrowdFree = pts > 0 ? 1f - Math.Min(1f, crowded / (float)pts / 0.3f) : 1f;

            // ---- bases (two or more lift bottoms clustered within 400 m, in the lowest 35 % of the region's relief; the lowest such
            // cluster when none has two) and their beginner terrain:
            // full credit when a lift at the base serves a green run back to the base, half for a blue one
            var baseOf = new int[lifts.Count];
            for (int L = 0; L < lifts.Count; L++)
                baseOf[L] = L;
            for (int L = 0; L < lifts.Count; L++)
                for (int M = 0; M < L; M++)
                    if (math.distance(bot[L].xz, bot[M].xz) < 400f)
                        Union(baseOf, L, M);
            float lowCut = hLo + BaseShare * (hHi - hLo);
            var bases = new Dictionary<int, float>();
            var size = new Dictionary<int, int>();
            for (int L = 0; L < lifts.Count; L++)
                size[Find(baseOf, L)] = size.TryGetValue(Find(baseOf, L), out int c) ? c + 1 : 1;
            for (int L = 0; L < lifts.Count; L++)
                if (bot[L].y <= lowCut && size[Find(baseOf, L)] >= 2)
                    bases[Find(baseOf, L)] = 0f;
            if (bases.Count == 0 && lifts.Count > 0)
            {
                int low = Enumerable.Range(0, lifts.Count).OrderBy(L => bot[L].y).First();
                bases[Find(baseOf, low)] = 0f;
            }
            bool IsAerial(int L) => lifts[L].Type == "gondola" || lifts[L].Type == "tram";
            // elements a skier holding to bands <= max takes from lift L's top
            var easyMemo = new Dictionary<(int, SlopeBand), HashSet<int>>();
            HashSet<int> EasyFrom(int L, SlopeBand max)
            {
                if (easyMemo.TryGetValue((L, max), out HashSet<int> got))
                    return got;
                var seen = new HashSet<int>();
                var q = new Queue<int>();
                for (int i = 0; i < m; i++)
                    if (els[i].Band <= max && math.distancesq(els[i].P[0].xz, top[L].xz) < StartM * StartM && seen.Add(i))
                        q.Enqueue(i);
                while (q.Count > 0)
                    foreach (int j in next[q.Dequeue()])
                        if (els[j].Band <= max && seen.Add(j))
                            q.Enqueue(j);
                return easyMemo[(L, max)] = seen;
            }
            // beginner terrain: from the base, a beginner rides lifts whose tops have skiing at their level (or a gondola / tram, both
            // ways); credit when that skiing delivers them to a lift again (a loop they can lap), anywhere on the mountain
            bool Learn(int B, SlopeBand max)
            {
                var S = new HashSet<int>();
                var q = new Queue<int>();
                for (int L = 0; L < lifts.Count; L++)
                    if (Find(baseOf, L) == B && (IsAerial(L) || EasyFrom(L, max).Count > 0) && S.Add(L))
                        q.Enqueue(L);
                bool loop = false;
                while (q.Count > 0)
                {
                    int L = q.Dequeue();
                    foreach (int i in EasyFrom(L, max))
                        foreach (int M in delivers[i])
                        {
                            loop = true;
                            if ((IsAerial(M) || EasyFrom(M, max).Count > 0) && S.Add(M))
                                q.Enqueue(M);
                        }
                }
                return loop;
            }
            foreach (int B in bases.Keys.ToList())
                bases[B] = Learn(B, SlopeBand.Green) ? 1f : Learn(B, SlopeBand.Blue) ? 0.5f : 0f;
            r.Bases = bases.Count;
            r.BeginnerBases = bases.Values.Count(v => v >= 1f);
            r.Beginner = bases.Count > 0 ? bases.Values.Average() : 0f;

            // ---- easy way home: from every lift's top a green or blue route (runs, cat tracks) to a base lift's bottom, or to a
            // gondola / tram top to ride down (a gondola or tram is its own way home: its riders download)
            for (int L = 0; L < lifts.Count; L++)
            {
                HashSet<int> easy = EasyFrom(L, SlopeBand.Blue);
                bool ok = IsAerial(L) || easy.Any(i => atBottom[i].Any(M => bases.ContainsKey(Find(baseOf, M)))
                    || delivers[i].Any(M => IsAerial(M) && !atBottom[i].Contains(M)));
                if (ok)
                    r.WayHomeLifts++;
                else if (r.Notes.Count < 60)
                    r.Notes.Add($"lift {L + 1}: no green or blue way home");
            }
            r.WayHome = lifts.Count > 0 ? r.WayHomeLifts / (float)lifts.Count : 0f;

            r.Total = 15f * r.Served + 10f * r.Coverage + 15f * r.Network + 15f * r.Beginner + 10f * r.WayHome + 10f * r.DeadFree
                + 10f * r.Validity + 5f * r.StubFree + 5f * r.CrowdFree + 5f * r.Economy;
            return r;
        }

        public static string Line(string id, Result r) => string.Format(CultureInfo.InvariantCulture,
            "SCORE rf {0}: {1:F1} | served {2:F2} cover {3:F2} network {4:F2} beginner {5:F2} ({6}/{7} bases green) way-home {23:F2} ({24}/{14}) dead-free {8:F2} " +
            "valid {9:F2} ({10} violations) stub-free {11:F2} crowd-free {12:F2} economy {13:F2} | {14} lifts, {15}/{16} runs served, " +
            "{17:F0} m served vertical, km G/B/K/DB {18:F1}/{19:F1}/{20:F1}/{21:F1}, connectors {22:F1} km",
            id, r.Total, r.Served, r.Coverage, r.Network, r.Beginner, r.BeginnerBases, r.Bases, r.DeadFree, r.Validity, r.Violations,
            r.StubFree, r.CrowdFree, r.Economy, r.Lifts, r.ServedRuns, r.Runs, r.ServedVerticalM,
            r.BandKm[1], r.BandKm[2], r.BandKm[3], r.BandKm[4], r.ConnectorKm, r.WayHome, r.WayHomeLifts);

        private static int Find(int[] p, int i)
        {
            while (p[i] != i)
                i = p[i] = p[p[i]];
            return i;
        }

        private static void Union(int[] p, int a, int b) => p[Find(p, a)] = Find(p, b);

        private static bool SegCross(float2 a, float2 b, float2 c, float2 d)
        {
            float S(float2 p, float2 q, float2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            if (math.distance(a, c) < 30f || math.distance(a, d) < 30f || math.distance(b, c) < 30f || math.distance(b, d) < 30f)
                return false;   // shared stations are not a crossing
            return S(a, b, c) * S(a, b, d) < 0f && S(c, d, a) * S(c, d, b) < 0f;
        }

        private static double Area(float2[] r)
        {
            double a = 0.0;
            for (int i = 0, j = r.Length - 1; i < r.Length; j = i++)
                a += (double)r[j].x * r[i].y - (double)r[i].x * r[j].y;
            return Math.Abs(a) * 0.5;
        }

        private static bool InsideRing(float2[] r, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = r.Length - 1; i < r.Length; j = i++)
                if ((r[i].y > p.y) != (r[j].y > p.y) && p.x < (r[j].x - r[i].x) * (p.y - r[i].y) / (r[j].y - r[i].y) + r[i].x)
                    inside = !inside;
            return inside;
        }
    }
}
