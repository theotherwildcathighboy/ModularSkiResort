// RunsFirstPlanner heads: the mirror of the junction pass. A run that leaves another run, a traverse fork or a lift top beside its
// siblings shares its first stretch with them. The scorecard reads both sides of that stretch as crowded. Each run's start
// slides forward along its first 60 m to where it crowds least. The slide keeps these true:
// - the skier still gets onto the run the same way:
//   - within 39.5 m of the element it leaves, no higher than that element there
//   - within 145 m of the top the skier skis from
// - every run that joins or leaves it still does.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        private float HeadForwardM => O.HeadForwardM;   // how far a run's start may slide forward

        /// <summary>Moves runs' starts forward to where their first stretch crowds other runs least. Returns the runs changed.</summary>
        [MethodImpl(Hot)]
        private int TrimHeads(Grid g, PlanDraft plan, List<Lift> lifts)
        {
            SkiGraph sg = Ski(g, plan, lifts);
            static long Key(float2 p) => ((long)Math.Floor(p.x / 60f) << 32) ^ (uint)(int)Math.Floor(p.y / 60f);
            var served = new bool[sg.P.Count];
            for (int i = 0; i < sg.Runs; i++)
                served[i] = sg.Reached[i] && sg.Returns[i];
            var at = new Dictionary<long, List<(float3 P, int E, int K)>>();
            for (int i = 0; i < sg.P.Count; i++)
                for (int k = 0; k < sg.P[i].Length; k++)
                {
                    long key = Key(sg.P[i][k].xz);
                    if (!at.TryGetValue(key, out var l))
                        at[key] = l = new List<(float3, int, int)>();
                    l.Add((sg.P[i][k], i, k));
                }
            IEnumerable<(float3 P, int E, int K)> Near(float2 q, float r)
            {
                int cx = (int)Math.Floor(q.x / 60f), cz = (int)Math.Floor(q.y / 60f);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (at.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l))
                            foreach (var s in l)
                                if (math.distancesq(s.P.xz, q) < r * r)
                                    yield return s;
            }
            bool Counted((float3 P, int E, int K) s) => s.E < sg.Runs && served[s.E] && s.K < sg.P[s.E].Length - 15;
            var tops = lifts.ConvertAll(lf => lf.Top.xz);

            int changed = 0;
            var old = new Dictionary<int, List<float2>>();
            for (int a = 0; a < sg.Runs; a++)
            {
                float3[] P = sg.P[a];
                int n = P.Length;
                if (!served[a] || n < 30)
                    continue;
                // how the skier gets onto it: tops it starts by, elements it leaves at its start
                var fromTops = Enumerable.Range(0, lifts.Count).Where(L => math.distancesq(P[0].xz, tops[L]) < 150f * 150f).ToList();
                var leaves = Near(P[0].xz, 40f).Where(s => s.E != a && P[0].y <= s.P.y + 5f).Select(s => s.E).ToHashSet();
                // elements that end on its first 70 m or start beside it there: they must still find it
                var ending = new List<float3>();
                var starting = new List<float3>();
                var related = new HashSet<int>(leaves);
                for (int i = 0; i < sg.P.Count; i++)
                {
                    if (i == a)
                        continue;
                    float3 e = sg.P[i][sg.P[i].Length - 1], st = sg.P[i][0];
                    for (int k = 0; k < Math.Min(n, 8); k++)
                    {
                        if (math.distancesq(P[k].xz, e.xz) < 40f * 40f && P[k].y <= e.y + 5f)
                        {
                            ending.Add(e);
                            related.Add(i);
                        }
                        if (math.distancesq(P[k].xz, st.xz) < 40f * 40f && st.y <= P[k].y + 5f)
                        {
                            starting.Add(st);
                            related.Add(i);
                        }
                    }
                }
                // a run keeps its start when its neighbor at the start already moved. The code judged their relations on the old places
                if (related.Any(old.ContainsKey))
                    continue;
                int window = Math.Min(n - 1, 13);
                // what its first stretch (to the window's end) crowds: other runs' counted samples near it, its own near others
                int Cost(List<float3> head)
                {
                    var hit = new HashSet<(int, int)>();
                    int own = 0;
                    foreach (float3 p in head)
                    {
                        bool crowded = false;
                        foreach (var s in Near(p.xz, FillCrowdM))
                            if (s.E != a && s.E < sg.Runs && served[s.E])
                            {
                                crowded = true;
                                if (Counted(s))
                                    hit.Add((s.E, s.K));
                            }
                        if (crowded)
                            own++;
                    }
                    return hit.Count + own;
                }
                var cur = new List<float3>();
                for (int k = 0; k <= window; k++)
                    cur.Add(P[k]);
                int best = Cost(cur);
                List<float3> pick = null;
                float along = 0f;
                for (int k = 0; k + 1 < window && along <= HeadForwardM; k++)
                {
                    float seg = math.distance(P[k].xz, P[k + 1].xz);
                    for (float f = 2f; f <= seg && along + f <= HeadForwardM; f += 2f)
                    {
                        float2 q = math.lerp(P[k].xz, P[k + 1].xz, f / math.max(seg, 1e-3f));
                        float hq = RawH(g, q);
                        if (float.IsNaN(hq) || !fromTops.All(L => math.distancesq(q, tops[L]) < 145f * 145f) ||
                            !leaves.All(j => Near(q, 39.5f).Any(s => s.E == j && hq <= s.P.y + 4.5f)))
                            continue;
                        var head = new List<float3> { new float3(q.x, hq, q.y) };
                        for (int i = k + 1; i <= window; i++)
                            head.Add(P[i]);
                        if (!ending.All(e => head.Any(p => math.distancesq(p.xz, e.xz) < 39.5f * 39.5f && p.y <= e.y + 4.5f)) ||
                            !starting.All(st => head.Any(p => math.distancesq(p.xz, st.xz) < 39.5f * 39.5f && st.y <= p.y + 4.5f)))
                            continue;
                        int c = Cost(head);
                        if (c < best)
                        {
                            best = c;
                            pick = head;
                        }
                    }
                    along += seg;
                }
                if (pick == null)
                    continue;
                var moved = pick.ConvertAll(p => p.xz);
                for (int i = window + 1; i < n; i++)
                    moved.Add(P[i].xz);
                if (RawH(g, moved[0]) - RawH(g, moved[moved.Count - 1]) < MinRunDropM || SeenRunBand(g, moved, RaterChord) != SeenRunBand(g, plan.Runs[a].Pts, RaterChord))
                    continue;
                old[a] = plan.Runs[a].Pts;
                plan.Runs[a].Pts = moved;
                changed++;
            }
            if (changed == 0)
                return 0;
            SkiGraph after = Ski(g, plan, lifts);
            bool keep = after.BestSize >= sg.BestSize;
            for (int i = 0; keep && i < sg.P.Count; i++)
                keep = sg.Next[i].All(after.Next[i].Contains) && sg.Delivers[i].All(after.Delivers[i].Contains) &&
                    (!sg.Reached[i] || after.Reached[i]) && (!sg.Returns[i] || after.Returns[i]);
            for (int L = 0; keep && L < lifts.Count; L++)
                keep = sg.Skied[L].All(after.Skied[L].Contains) && sg.Reaches[L].All(after.Reaches[L].Contains);
            if (!keep && O.Debug)
                for (int i = 0; i < sg.P.Count; i++)
                {
                    var lostNext = sg.Next[i].Where(j => !after.Next[i].Contains(j)).ToList();
                    var lostDel = sg.Delivers[i].Where(j => !after.Delivers[i].Contains(j)).ToList();
                    if (lostNext.Count > 0 || lostDel.Count > 0 || (sg.Reached[i] && !after.Reached[i]) || (sg.Returns[i] && !after.Returns[i]))
                        Trace($"DEBUG heads: element {i}{(i < sg.Runs ? " run" : " connector")}{(old.ContainsKey(i) ? " (moved)" : "")} lost next [{string.Join(",", lostNext.Select(j => j + (old.ContainsKey(j) ? "*" : "")))}] delivers [{string.Join(",", lostDel)}] reached {sg.Reached[i]}->{after.Reached[i]} returns {sg.Returns[i]}->{after.Returns[i]}");
                }
            if (!keep)
            {
                foreach (var kv in old)
                    plan.Runs[kv.Key].Pts = kv.Value;
                plan.Log.Add($"heads: {changed} run start(s) would move, undone (the skiing changed)");
                return 0;
            }
            plan.Log.Add($"heads: {changed} run start(s) moved forward to where they crowd other runs least");
            return changed;
        }
    }
}
