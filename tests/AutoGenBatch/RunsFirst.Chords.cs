// RunsFirst chord repair: the serve pass and the beginner fallbacks keep a run under a lift line when it is the only one of its
// kind (a lift's only run, a base's only green way home). Where such runs still cover the middle of a line at the end, their
// stretches under it move out beside the line, the way a designer keeps a run off the chairs, when the moved run keeps the run
// rules, its band and every way the plan is skied.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        private const float ChordOnRunM = 20f, ChordShareMax = 0.3f;   // the scorecard's chord-on-run test
        private const float ChordClearM = 26f;                          // a moved stretch's distance off the chord

        /// <summary>The share of a lift chord's middle 70 % (sampled every 10 m) within 20 m of a run point, as the scorecard reads it.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static float ChordShare(Lift lf, List<List<float2>> dense)
        {
            float2 b = lf.Bottom.xz, t = lf.Top.xz;
            float plan2 = math.distance(b, t);
            int n = 0, near = 0;
            for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, plan2), n++)
            {
                float2 q = math.lerp(b, t, f);
                if (dense.Any(d => d.Any(p => math.distancesq(p, q) < ChordOnRunM * ChordOnRunM)))
                    near++;
            }
            return n > 0 ? near / (float)n : 0f;
        }

        /// <summary>Moves the runs' stretches under a lift line out beside it where the line's middle still runs along runs; returns
        /// the lifts cleared.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int RepairChords(Grid g, Plan plan, List<Lift> lifts)
        {
            int cleared = 0;
            List<List<float2>> dense = plan.Runs.Select(r => Densify(r.Pts)).ToList();
            foreach (Lift lf in lifts)
            {
                if (Late)
                    break;
                float share = ChordShare(lf, dense);
                if (share <= ChordShareMax)
                    continue;
                List<float> shares = lifts.Select(o => ChordShare(o, dense)).ToList();
                SkiGraph before = Ski(g, plan, lifts);
                var old = new Dictionary<int, (List<float2> Pts, SlopeBand Band)>();
                float2 b = lf.Bottom.xz, t = lf.Top.xz;
                float plan2 = math.distance(b, t);
                float2 dir = (t - b) / plan2, nrm = new float2(-dir.y, dir.x);
                for (int ri = 0; ri < plan.Runs.Count; ri++)
                {
                    List<float2> pts = dense[ri];
                    var off = new float[pts.Count];
                    var near = new bool[pts.Count];
                    for (int i = 0; i < pts.Count; i++)
                    {
                        float along = math.dot(pts[i] - b, dir) / plan2;
                        off[i] = math.dot(pts[i] - b, nrm);
                        near[i] = along > 0.1f && along < 0.9f && Math.Abs(off[i]) < ChordClearM;
                    }
                    if (!near.Any(x => x))
                        continue;
                    SlopeBand band = RunBand(g, pts);
                    List<float2> moved = null;
                    // each stretch beside the chord moves out to the side most of it lies on (else the other), tapered over 60 m
                    foreach (float flip in new[] { 1f, -1f })
                    {
                        var shift = new float[pts.Count];
                        for (int i = 0; i < pts.Count; )
                        {
                            if (!near[i])
                            {
                                i++;
                                continue;
                            }
                            int j = i;
                            while (j + 1 < pts.Count && near[j + 1])
                                j++;
                            float side = 0f;
                            for (int k = i; k <= j; k++)
                                side += off[k];
                            float sgn = (side >= 0f ? 1f : -1f) * flip;
                            for (int k = i; k <= j; k++)
                                shift[k] = sgn * ChordClearM - off[k];
                            for (int k = 1; k <= 6; k++)
                            {
                                float w = 1f - k / 7f;
                                foreach (var (at, edge) in new[] { (i - k, i), (j + k, j) })
                                    if (at >= 0 && at < pts.Count && !near[at] && off[at] * sgn >= 0f && Math.Abs(shift[edge] * w) > Math.Abs(shift[at]))
                                        shift[at] = shift[edge] * w;
                            }
                            i = j + 1;
                        }
                        var cand = new List<float2>(pts.Count);
                        for (int i = 0; i < pts.Count; i++)
                            cand.Add(pts[i] + nrm * shift[i]);
                        List<float2> ok = SanitizeRun(g, cand);
                        if (ok != null && ok.Count >= Densify(cand).Count && RunBand(g, ok) == band && ok.All(p => CellOf(g, p) is int c && c >= 0 && g.In[c]))
                        {
                            moved = ok;
                            break;
                        }
                    }
                    if (moved == null)
                        continue;
                    old[ri] = (plan.Runs[ri].Pts, plan.Runs[ri].Band);
                    plan.Runs[ri].Pts = moved;
                    dense[ri] = Densify(moved);
                }
                if (old.Count == 0)
                {
                    plan.Log.Add($"chord repair L{lf.Id}: no run stretch moves ({share:P0} of its middle on runs)");
                    continue;
                }
                // kept when the line clears, no other line takes its place, and every start, join and delivery the plan had stays
                bool SameSkiing()
                {
                    SkiGraph sg = Ski(g, plan, lifts);
                    for (int i = 0; i < before.P.Count; i++)
                        if (!before.Next[i].All(sg.Next[i].Contains) || !before.Delivers[i].All(sg.Delivers[i].Contains) ||
                            (before.Reached[i] && !sg.Reached[i]) || (before.Returns[i] && !sg.Returns[i]))
                            return false;
                    for (int L = 0; L < lifts.Count; L++)
                        if (!before.Skied[L].All(sg.Skied[L].Contains) || !before.Reaches[L].All(sg.Reaches[L].Contains))
                            return false;
                    return true;
                }
                bool Clear(out float now)
                {
                    now = ChordShare(lf, dense);
                    return now <= ChordShareMax && lifts.Select((o, k) => shares[k] > ChordShareMax || ChordShare(o, dense) <= ChordShareMax).All(x => x);
                }
                void Restore(int ri)
                {
                    plan.Runs[ri].Pts = old[ri].Pts;
                    plan.Runs[ri].Band = old[ri].Band;
                    dense[ri] = Densify(old[ri].Pts);
                }
                bool keep = Clear(out float after) && SameSkiing();
                int kept = old.Count;
                if (!keep && old.Count > 1)
                {
                    // one stretch may be what breaks a join (its end slid out of the skier's reach): each run's move is kept when the
                    // skiing stays as it was, and the line is judged again on the moves kept
                    var moved = old.Keys.ToDictionary(ri => ri, ri => plan.Runs[ri].Pts);
                    foreach (int ri in moved.Keys)
                        Restore(ri);
                    var took = new List<int>();
                    foreach (var kv in moved)
                    {
                        plan.Runs[kv.Key].Pts = kv.Value;
                        dense[kv.Key] = Densify(kv.Value);
                        if (SameSkiing())
                            took.Add(kv.Key);
                        else
                            Restore(kv.Key);
                    }
                    keep = took.Count > 0 && Clear(out after);
                    kept = took.Count;
                    if (!keep)
                        foreach (int ri in took)
                            Restore(ri);
                }
                if (keep)
                {
                    cleared++;
                    plan.Log.Add($"chord repair L{lf.Id}: {kept} run stretch(es) moved beside the line ({share:P0} -> {after:P0} of its middle on runs)");
                    continue;
                }
                foreach (int ri in old.Keys)
                    Restore(ri);
                plan.Log.Add($"chord repair L{lf.Id}: undone ({share:P0} -> {after:P0} of its middle on runs, or the skiing changed)");
            }
            return cleared;
        }

        /// <summary>RF_CHORDDBG: per lift, the share of its chord middle on runs as scorecard v3 reads them, by element.</summary>
        private static void ChordDebug(Grid g, Plan plan, List<Lift> lifts)
        {
            var els = new List<(string Name, List<float2> P, bool Own)>();
            for (int i = 0; i < plan.Runs.Count; i++)
            {
                PlanRun r = plan.Runs[i];
                bool run = !r.Graded || TrackClass(g, r.Pts).Run;
                if (run)
                    els.Add(($"run#{i}:{r.Kind}{(r.Graded ? "(graded)" : "")}:{GroundBand(g, r.Pts)}", Densify(r.Pts), false));
            }
            for (int i = 0; i < plan.Connectors.Count; i++)
            {
                var (run, band) = TrackClass(g, plan.Connectors[i]);
                if (run)
                    els.Add(($"conn#{i}:{band}{(plan.Links.Contains(plan.Connectors[i]) ? ":link" : "")}", Densify(plan.Connectors[i]), false));
            }
            foreach (Lift lf in lifts)
            {
                float2 b = lf.Bottom.xz, t = lf.Top.xz;
                float plan2 = math.distance(b, t);
                int n = 0, near = 0;
                var by = new Dictionary<string, int>();
                for (float f = 0.15f; f <= 0.85f; f += 10f / math.max(10f, plan2), n++)
                {
                    float2 q = math.lerp(b, t, f);
                    bool hit = false;
                    foreach (var e in els)
                        if (e.P.Any(p => math.distancesq(p, q) < 400f))
                        {
                            hit = true;
                            by[e.Name] = by.TryGetValue(e.Name, out int c) ? c + 1 : 1;
                        }
                    if (hit)
                        near++;
                }
                if (n > 0 && near > 0.2f * n)
                    Console.WriteLine($"CHORDDBG L{lf.Id} {lf.Type}: {100 * near / n}% ({string.Join(", ", by.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}/{n}"))})");
            }
        }

        // how far a station may move across its line: 100 m against 60 m moved three more stations 70-80 m (visible 97.64 -> 97.69,
        // 80 % outlines 95.50 -> 95.72); 150 m one more, 120 m (ft5-r2 L5 off the summit station it shared: visible 97.69 -> 97.80);
        // 350 m (scorecard v4): owner-4 L7 260 m off the valley runs it lay along (visible 97.30 -> 97.41)
        private static readonly float NudgeMaxM = Env("RF_NUDGEM", 350f);

        /// <summary>A lift line whose middle still lies along runs (or that crosses another lift of its kind) moves instead of the
        /// runs: the whole line shifted across itself, or swung about one station, up to NudgeMaxM, the smallest move first. A move is
        /// kept where the stations stay on pads inside the region, the rope clears the ground, the lift keeps its type's norms and
        /// crosses no lift it may not cross, the line clears and the ski graph loses nothing (its runs still start at its top and come
        /// home to its bottom). Returns the lines moved.</summary>
        private static int NudgeLines(Grid g, Plan plan, List<Lift> lifts)
        {
            int moved = 0;
            List<(List<float2> D, int Conn, PlanRun Run, bool Green)> els = ChordEls(g, plan);
            SkiRead before = null;
            foreach (Lift lf in lifts)
            {
                if (Late)
                    break;
                float share = ChordShareV3(lf, els, out _);
                bool crossing = CrossesKind(lifts, lf);
                if (share <= ChordShareMax && !crossing)
                    continue;
                float2 b0 = lf.Bottom.xz, t0 = lf.Top.xz, dir = math.normalize(t0 - b0), across = new float2(-dir.y, dir.x);
                var moves = new List<(float Cost, float2 B, float2 T)>();
                for (float o = 20f; o <= NudgeMaxM; o += 10f)
                    foreach (float s in new[] { o, -o })
                    {
                        moves.Add((o, b0 + s * across, t0 + s * across));
                        moves.Add((o * 0.9f, b0 + s * across, t0));
                        moves.Add((o * 0.9f, b0, t0 + s * across));
                    }
                moves.Sort((x, y) => x.Cost.CompareTo(y.Cost));
                var (ob, ot, obc, otc, oRise, oLen) = (lf.Bottom, lf.Top, lf.BottomCell, lf.TopCell, lf.Rise, lf.Length);
                void Restore()
                {
                    (lf.Bottom, lf.Top, lf.BottomCell, lf.TopCell, lf.Rise, lf.Length) = (ob, ot, obc, otc, oRise, oLen);
                }
                before ??= SkiRead.Of(g, plan, lifts);
                foreach (var (cost, nb, nt) in moves)
                {
                    int cb = CellOf(g, nb), ct = CellOf(g, nt);
                    if (cb < 0 || ct < 0 || !LineFits(g, lifts, lf, cb, ct))
                        continue;
                    lf.BottomCell = cb;
                    lf.TopCell = ct;
                    lf.Bottom = new float3(g.World(cb).x, g.H[cb], g.World(cb).y);
                    lf.Top = new float3(g.World(ct).x, g.H[ct], g.World(ct).y);
                    lf.Rise = lf.Top.y - lf.Bottom.y;
                    lf.Length = math.distance(lf.Top, lf.Bottom);
                    float now = ChordShareV3(lf, els, out _);
                    SkiRead after = now <= ChordShareMax ? SkiRead.Of(g, plan, lifts) : null;
                    if (after == null || before.Loses(after))
                    {
                        Restore();
                        continue;
                    }
                    before = after;
                    moved++;
                    plan.Log.Add($"nudge L{lf.Id}: {(nb.Equals(b0) ? "top" : nt.Equals(t0) ? "bottom" : "line")} moved {cost / (nb.Equals(b0) || nt.Equals(t0) ? 0.9f : 1f):F0} m " +
                        $"across ({100 * share:F0} % -> {100 * now:F0} % of its middle on runs{(crossing ? "; off the line it crossed" : "")})");
                    break;
                }
            }
            return moved;
        }

        /// <summary>Whether lift lf's line crosses another lift's of its kind (chair or aerial), as the scorecard reads a crossing.</summary>
        private static bool CrossesKind(List<Lift> lifts, Lift lf) =>
            lifts.Exists(o => o != lf && Aerial(o.Type) == Aerial(lf.Type) && SegCrossScore(o.Bottom.xz, o.Top.xz, lf.Bottom.xz, lf.Top.xz));

        /// <summary>Lift lf with its stations at cells b and t: pads inside the region, the type's norms as the scorecard reads them
        /// (with a margin), the rope clear of the ground, and no crossing with another lift of its kind (chair or aerial).</summary>
        private static bool LineFits(Grid g, List<Lift> lifts, Lift lf, int b, int t)
        {
            if (!g.In[b] || !g.In[t])
                return false;
            float2 pb = g.World(b), pt = g.World(t);
            if (RawGrade(g, pb) > StationMax || RawGrade(g, pt) > StationMax)
                return false;
            float hb = RawH(g, pb), ht = RawH(g, pt), run = math.distance(pb, pt), rise = ht - hb;
            float len = math.sqrt(run * run + rise * rise);
            int ti = Array.FindIndex(Types, x => x.Name == lf.Type);
            if (ti < 0 || len < Types[ti].L0 + 5f || rise < Types[ti].R0 + 2f || rise / math.max(1f, run) > Types[ti].Cap - 0.02f)
                return false;
            for (float d = 10f; d < run - 10f; d += 10f)
                if (RawH(g, math.lerp(pb, pt, d / run)) - math.lerp(hb, ht, d / run) > TowerLiftM - 2f)
                    return false;
            foreach (Lift o in lifts)
                if (o != lf && Aerial(o.Type) == Aerial(lf.Type) && SegCrossScore(o.Bottom.xz, o.Top.xz, pb, pt))
                    return false;
            return true;
        }
    }
}
