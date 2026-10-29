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
    }
}
