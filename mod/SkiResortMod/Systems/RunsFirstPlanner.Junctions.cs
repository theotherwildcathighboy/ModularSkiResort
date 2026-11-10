// RunsFirst junctions: a merging run ends where it first comes within 39 m of the run it joins (the serve pass). Every sample
// of the joined run within 40 m of it reads as crowded. That is three to five per junction where the joined run passes 35-39 m
// from the end. At the end of the plan, each merging run's end slides back along its last 60 m. It slides to the spot where the
// fewest samples of other runs lie within 40 m of its tail. At that spot the run it joins still lies within the skier's 40 m
// (no higher than 5 m above the end). The skier still flows on. The junction stops reading as two runs side by side.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        private static float JunctionBackM => O.JunctionBackM;   // how far a merging run's end may slide back

        /// <summary>Moves merging runs' ends back to where they crowd the run they join least. Returns the runs changed.</summary>
        [MethodImpl(Hot)]
        private static int TrimJunctions(Grid g, Plan plan, List<Lift> lifts)
        {
            SkiGraph sg = Ski(g, plan, lifts);
            static long Key(float2 p) => ((long)Math.Floor(p.x / 60f) << 32) ^ (uint)(int)Math.Floor(p.y / 60f);
            var served = new bool[sg.P.Count];
            for (int i = 0; i < sg.Runs; i++)
                served[i] = sg.Reached[i] && sg.Returns[i];
            // every element's samples by 60 m bucket (counted: a served run's sample outside its last 150 m)
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
            // the counted samples of other runs within 40 m of a run's last points
            int Cost(List<float3> pts, int self)
            {
                var hit = new HashSet<(int, int)>();
                for (int k = Math.Max(0, pts.Count - 8); k < pts.Count; k++)
                    foreach (var s in Near(pts[k].xz, FillCrowdM))
                        if (s.E != self && Counted(s))
                            hit.Add((s.E, s.K));
                return hit.Count;
            }

            int changed = 0;
            var old = new Dictionary<int, List<float2>>();
            for (int a = 0; a < sg.Runs; a++)
            {
                float3[] P = sg.P[a];
                int n = P.Length;
                if (!served[a] || sg.Delivers[a].Count > 0 || n < 12)
                    continue;
                // the elements it joins at its end now
                var joins = Near(P[n - 1].xz, 40f).Where(s => s.E != a && s.P.y <= P[n - 1].y + 5f).Select(s => s.E).ToHashSet();
                if (joins.Count == 0)
                    continue;
                int now = Cost(P.ToList(), a), best = now;
                List<float3> pick = null;
                float along = 0f;
                for (int k = n - 1; k > 0 && along <= JunctionBackM; k--)
                {
                    float seg = math.distance(P[k].xz, P[k - 1].xz);
                    for (float f = 0f; f < seg && along + f <= JunctionBackM; f += 2f)
                    {
                        float2 q = math.lerp(P[k].xz, P[k - 1].xz, f / math.max(seg, 1e-3f));
                        float hq = RawH(g, q);
                        // the skier still runs onto every element the run joined
                        if (float.IsNaN(hq) || !joins.All(j => Near(q, 39.5f).Any(s => s.E == j && s.P.y <= hq + 4.5f)))
                            continue;
                        var pts = new List<float3>(k + 1);
                        for (int i = 0; i < k; i++)
                            pts.Add(P[i]);
                        pts.Add(new float3(q.x, hq, q.y));
                        int c = Cost(pts, a);
                        if (c < best)
                        {
                            best = c;
                            pick = pts;
                        }
                    }
                    along += seg;
                }
                if (pick == null || pick[0].y - pick[pick.Count - 1].y < MinRunDropM)
                    continue;
                List<float2> moved = pick.ConvertAll(p => p.xz);
                if (RunBand(g, moved) != RunBand(g, plan.Runs[a].Pts))
                    continue;
                old[a] = plan.Runs[a].Pts;
                plan.Runs[a].Pts = moved;
                changed++;
            }
            if (changed == 0)
                return 0;
            // kept when every start, join and delivery the plan had stays (else all of it goes back)
            SkiGraph after = Ski(g, plan, lifts);
            bool keep = after.BestSize >= sg.BestSize;
            for (int i = 0; keep && i < sg.P.Count; i++)
                keep = sg.Next[i].All(after.Next[i].Contains) && sg.Delivers[i].All(after.Delivers[i].Contains) &&
                    (!sg.Reached[i] || after.Reached[i]) && (!sg.Returns[i] || after.Returns[i]);
            for (int L = 0; keep && L < lifts.Count; L++)
                keep = sg.Skied[L].All(after.Skied[L].Contains) && sg.Reaches[L].All(after.Reaches[L].Contains);
            if (!keep)
            {
                foreach (var kv in old)
                    plan.Runs[kv.Key].Pts = kv.Value;
                plan.Log.Add($"junctions: {changed} merging run end(s) would move, undone (the skiing changed)");
                return 0;
            }
            plan.Log.Add($"junctions: {changed} merging run end(s) moved back to where they crowd the run they join least");
            return changed;
        }
    }
}
