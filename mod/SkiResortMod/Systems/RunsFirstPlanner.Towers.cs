// RunsFirstPlanner towers (port round 2, step R2-p2): lifts the lift tool can build. The realizer runs the mod's tower planner on every
// planned lift (RunsFirstRealizer.Lifts.cs RealizeLifts, TowerPlan) and the lift queue builds a line only when its plan is Sound, or with
// exactly one tower over its sheave-train limit and nothing else wrong (flagged, LiftTowerPlan.QueueRule). Any other line waits for a hand
// fit. This pass runs the same tower plan on each planned lift (LiftLineInput.PlanQueueLine: the stations where the queue places them, the
// line the queue's lift tool plans, as the realizer's lift rows call it; task 2b-l) and, for a lift that is not Sound,
// tries in this order: a small shift of its stations, another lift type on the same line, then a flagged plan. A lift that still needs a
// hand fit is kept and marked in the log. The plans are cached per line, and each new plan polls the cancel token. Pure C# 9, net48-clean.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        private enum TowerFit : byte { Sound = 0, Flagged = 1, HandFit = 2 }

        private const float TowerEdgeSlopeShare = 0.97f;   // RunsFirstRealizer.LiftEdgeSlopeShare: the edge-slope margin of a clear plan
        private const float TowerShiftMaxM = 30f;          // the farthest a station moves
        private const float TowerShiftStepM = 10f;
        private const int TowerBatch = 8;   // the shift plans made at once (on ScanThreads threads)
        // scorecard v5.1 pairs of lines: parallel within 20 degrees, 250 m apart, over 40 % of the shorter line; stacked within 30 degrees,
        // tops within 300 m, bottoms over 300 m apart
        private const float TowerParallelDeg = 20f, TowerParallelM = 250f, TowerParallelShare = 0.4f, TowerStackDeg = 30f, TowerStackM = 300f;
        private const float TowerRopeM = 40f;   // scorecard v5 RopeM: a station this close to another lift's line is under its rope
        private const float TowerAerialKm2 = 6f, TowerTrunkRiseM = 700f;   // scorecard v5 fleet: one more gondola or tram per 6 km2; a trunk line past 700 m of rise
        private const float TowerDeliverM = 60f, TowerDeliverDropM = 15f;   // RunsFirstRealizer.DeliverM, DeliverDropM: a run comes home to a bottom

        /// <summary>The cache of tower plans by line (type and both ends) for one plan, and the workers that make them.</summary>
        private sealed class TowerCheck
        {
            public readonly Dictionary<(byte T, float Bx, float Bz, float Tx, float Tz), (TowerFit Fit, int Over)> Cache =
                new Dictionary<(byte, float, float, float, float), (TowerFit, int)>();
            public readonly System.Collections.Concurrent.ConcurrentBag<TowerWorker> Idle = new System.Collections.Concurrent.ConcurrentBag<TowerWorker>();
            public int Plans, Hits;   // a report only
        }

        /// <summary>One thread's tower planner, its constants per lift type and the ground sampler.</summary>
        private sealed class TowerWorker
        {
            public readonly LiftTowerPlanner Planner = new LiftTowerPlanner();
            public readonly LiftTowerPlanConstants[] Consts = new LiftTowerPlanConstants[4];
            public HeightSnapshot Snap;
            public float Last;
            public int Misses;
            private Func<float3, float> m_Ground;

            /// <summary>The snapshot ground at a world point (RunsFirstRealizer.QueueGround, copied): a miss returns the last good height
            /// and is counted.</summary>
            public Func<float3, float> Ground => m_Ground ??= p =>
            {
                float h = Snap.Sample(new float3(p.x, 0f, p.z));
                if (float.IsNaN(h))
                {
                    Misses++;
                    return Last;
                }
                Last = h;
                return h;
            };
        }
        private TowerCheck m_Towers;

        /// <summary>The catalog index of a planner lift type (RunsFirstRealizer.TypeIndexOf): fixed grip 0 (and the learning chair),
        /// detachable 1, gondola 2, tram 3.</summary>
        private static byte TowerTypeOf(string type) => type == "detachable" ? (byte)1 : type == "gondola" ? (byte)2 : type == "tram" ? (byte)3 : (byte)0;

        /// <summary>The realizer's verdict on a lift line: its tower plan Sound, flagged (the queue builds it with one tower over its
        /// sheave-train limit), or a hand fit. over: the towers over their cap.</summary>
        private TowerFit FitOf(Grid g, string type, float3 bottom, float3 top, out int over)
        {
            m_Towers ??= new TowerCheck();
            TowerCheck tc = m_Towers;
            byte t = TowerTypeOf(type);
            var key = (t, bottom.x, bottom.z, top.x, top.z);
            if (tc.Cache.TryGetValue(key, out var hit))
            {
                tc.Hits++;
                over = hit.Over;
                return hit.Fit;
            }
            Poll();
            tc.Plans++;
            TowerWorker w = tc.Idle.TryTake(out TowerWorker x) ? x : new TowerWorker { Snap = g.S };
            TowerFit fit = PlanFit(w, t, bottom, top, out over);
            tc.Idle.Add(w);
            tc.Cache[key] = (fit, over);
            return fit;
        }

        /// <summary>Makes the tower plans of the lines the cache does not hold yet, on ScanThreads threads (each line's plan is its own:
        /// the result is the same on any thread count).</summary>
        private void FitMany(Grid g, string type, List<(float3 B, float3 T)> lines)
        {
            m_Towers ??= new TowerCheck();
            TowerCheck tc = m_Towers;
            byte t = TowerTypeOf(type);
            var todo = new List<(float3 B, float3 T)>();
            var keys = new HashSet<(byte, float, float, float, float)>();
            foreach (var (b, tp) in lines)
                if (!tc.Cache.ContainsKey((t, b.x, b.z, tp.x, tp.z)) && keys.Add((t, b.x, b.z, tp.x, tp.z)))
                    todo.Add((b, tp));
            if (todo.Count == 0)
                return;
            var res = new (TowerFit Fit, int Over)[todo.Count];
            System.Threading.Tasks.Parallel.For(0, todo.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = ScanThreads },
                () => tc.Idle.TryTake(out TowerWorker x) ? x : new TowerWorker { Snap = g.S },
                (i, _, w) =>
                {
                    if (CancelRequested)
                        return w;   // canceled: Poll below ends the plan
                    res[i].Fit = PlanFit(w, t, todo[i].B, todo[i].T, out res[i].Over);
                    return w;
                },
                w => tc.Idle.Add(w));
            Poll();
            for (int i = 0; i < todo.Count; i++)
            {
                tc.Plans++;
                tc.Cache[(t, todo[i].B.x, todo[i].B.z, todo[i].T.x, todo[i].T.z)] = res[i];
            }
        }

        /// <summary>The realizer's verdict on one line, on one worker: the line the queue plans between the stations it places
        /// (LiftLineInput.PlanQueueLine, as RunsFirstRealizer.Lifts calls it). Sound = QueueRule Build with the edge-slope margin, flagged =
        /// QueueRule BuildFlagged (one tower over its limit, by at most the flag cap), else a hand fit.</summary>
        private static TowerFit PlanFit(TowerWorker tc, byte t, float3 bottom, float3 top, out int over)
        {
            TowerFit fit = TowerFit.HandFit;
            over = 0;
            if (math.distance(bottom.xz, top.xz) <= 1f)
                return fit;
            LiftTowerPlanConstants k = tc.Consts[t] ??= LiftTowerPlanConstants.For(SkiCatalog.Lifts[t]);
            float hb = tc.Snap.Sample(new float3(bottom.x, 0f, bottom.z));
            tc.Last = float.IsNaN(hb) ? bottom.y : hb;
            tc.Misses = 0;
            LiftTowerPlan plan = LiftLineInput.PlanQueueLine(k, SkiCatalog.StationOf(t, StationRole.Base), SkiCatalog.StationOf(t, StationRole.Top),
                bottom.xz, top.xz, tc.Ground, tc.Planner, out _, out _, out _);
            if (plan == null)
                return fit;
            over = plan.OverCapTowers;
            bool edgeClear = tc.Misses == 0 && plan.Clear && plan.MaxEdgeSlope <= TowerEdgeSlopeShare * k.MaxEdgeSlope;
            LiftQueueBuild q = plan.QueueBuild;
            if (edgeClear && q == LiftQueueBuild.Build)
                fit = TowerFit.Sound;
            else if (edgeClear && q == LiftQueueBuild.BuildFlagged)
                fit = TowerFit.Flagged;
            return fit;
        }

        /// <summary>Whether a plan element's top (its highest point) is served from lift lf by the game's rule (LiftServedRule.Reaches,
        /// as the realizer's graph reads it).</summary>
        private static bool ServedFrom(Grid g, float3 elTop, Lift lf, List<LiftServedRule.Chord> chord)
        {
            LiftTypeDef def = SkiCatalog.Lifts[TowerTypeOf(lf.Type)];
            chord.Clear();
            chord.Add(new LiftServedRule.Chord { A = lf.Bottom.xz, B = lf.Top.xz });
            var row = new LiftServedRule.Lift
            {
                Complete = true, Power = LiftPower.Powered, TopY = RawH(g, lf.Top.xz), FirstChord = 0, ChordCount = 1, Rule = (byte)def.ServeRule,
                RadiusM = def.ServeRadiusM, TopMarginM = def.TopMarginM, TopXZ = lf.Top.xz,
            };
            return LiftServedRule.Reaches(elTop, row, chord, out float _);
        }

        /// <summary>Whether a plan element comes home to lift lf's bottom by the realizer's rule (a point after its first within 60 m
        /// of the bottom and at most 15 m below it).</summary>
        private static bool DeliversTo(Grid g, List<float2> pts, Lift lf)
        {
            for (int k = 1; k < pts.Count; k++)
                if (math.distancesq(pts[k], lf.Bottom.xz) < TowerDeliverM * TowerDeliverM && RawH(g, pts[k]) >= RawH(g, lf.Bottom.xz) - TowerDeliverDropM)
                    return true;
            return false;
        }

        /// <summary>Lifts the lift tool can build: each lift whose tower plan is not Sound is shifted (the smallest station move that makes
        /// it Sound), else given another type on the same line, else kept flagged; a lift that still needs a hand fit is kept and
        /// marked. Every move keeps the planner's line rules (LineFits), the lift's runs by the game's served rule and its ways home, and
        /// loses nothing in the ski graph. Returns the lifts changed.</summary>
        private int FitTowers(Grid g, PlanDraft plan, List<Lift> lifts, float km2)
        {
            int changed = 0, sound0 = 0, flagged0 = 0, hand0 = 0, sound = 0, flagged = 0, hand = 0, shifted = 0, retyped = 0;
            float shiftSum = 0f;
            var marked = new List<string>();
            List<(List<float2> D, int Conn, PlanRun Run, bool Green)> els = null;
            SkiRead before = null;
            var chord = new List<LiftServedRule.Chord>(1);
            // each element's highest point, for the served rule
            var tops = new List<(List<float2> Pts, float3 Top)>();
            foreach (PlanRun r in plan.Runs)
                tops.Add((r.Pts, HighestOf(g, r.Pts)));
            foreach (List<float2> c in plan.Connectors)
                tops.Add((c, HighestOf(g, c)));
            for (int li = 0; li < lifts.Count; li++)
            {
                Lift lf = lifts[li];
                TowerFit f0 = FitOf(g, lf.Type, lf.Bottom, lf.Top, out int over0);
                if (f0 == TowerFit.Sound)
                {
                    sound0++;
                    sound++;
                    continue;
                }
                if (f0 == TowerFit.Flagged)
                    flagged0++;
                else
                    hand0++;
                els ??= ChordEls(g, plan);
                before ??= SkiRead.Of(g, plan, lifts);
                // what the lift serves and takes home now (the game's rules), and its line's standing
                var served = new List<int>();
                var home = new List<int>();
                for (int i = 0; i < tops.Count; i++)
                {
                    if (ServedFrom(g, tops[i].Top, lf, chord))
                        served.Add(i);
                    if (DeliversTo(g, tops[i].Pts, lf))
                        home.Add(i);
                }
                float share0 = ChordShareV3(lf, els, out _);
                int hub0 = HubExcess(g, lifts), rope0 = RopeFaults(lifts);
                var (stack0, par0) = PairFaults(lifts);
                var (ob, ot, obc, otc, oRise, oLen, oType) = (lf.Bottom, lf.Top, lf.BottomCell, lf.TopCell, lf.Rise, lf.Length, lf.Type);
                void Restore()
                {
                    (lf.Bottom, lf.Top, lf.BottomCell, lf.TopCell, lf.Rise, lf.Length, lf.Type) = (ob, ot, obc, otc, oRise, oLen, oType);
                }
                void Place(int cb, int ct)
                {
                    lf.BottomCell = cb;
                    lf.TopCell = ct;
                    lf.Bottom = new float3(g.World(cb).x, g.H[cb], g.World(cb).y);
                    lf.Top = new float3(g.World(ct).x, g.H[ct], g.World(ct).y);
                    lf.Rise = lf.Top.y - lf.Bottom.y;
                    lf.Length = math.distance(lf.Top, lf.Bottom);
                }
                // the lift as it stands now keeps all it had: the line rules, its runs and ways home by the game's rules, the ski graph
                SkiRead Keeps()
                {
                    if (ChordShareV3(lf, els, out _) > math.max(ChordShareMax, share0) + 1e-4f)
                        return null;
                    if (HubExcess(g, lifts) > hub0 || RopeFaults(lifts) > rope0)
                        return null;
                    var (stack, par) = PairFaults(lifts);
                    if (stack > stack0 || par > par0)
                        return null;
                    foreach (int i in served)
                        if (!ServedFrom(g, tops[i].Top, lf, chord))
                            return null;
                    foreach (int i in home)
                        if (!DeliversTo(g, tops[i].Pts, lf))
                            return null;
                    SkiRead after = SkiRead.Of(g, plan, lifts);
                    return before.Loses(after) ? null : after;
                }

                // 1. a shift: one station, or the whole line across, by the smallest distance that makes the plan Sound
                float2 b0 = ob.xz, t0 = ot.xz, dir = math.normalize(t0 - b0), across = new float2(-dir.y, dir.x);
                var moves = new List<(float Cost, int Cb, int Ct)>();
                var seen = new HashSet<(int, int)> { (obc, otc) };
                void Move(float cost, float2 nb, float2 nt)
                {
                    int cb = CellOf(g, nb), ct = CellOf(g, nt);
                    if (cb >= 0 && ct >= 0 && seen.Add((cb, ct)))
                        moves.Add((cost, cb, ct));
                }
                for (float a = -TowerShiftMaxM; a <= TowerShiftMaxM; a += TowerShiftStepM)
                    for (float s = -TowerShiftMaxM; s <= TowerShiftMaxM; s += TowerShiftStepM)
                    {
                        float d = math.sqrt(a * a + s * s);
                        if (d < 1f || d > TowerShiftMaxM + 0.1f)
                            continue;
                        float2 off = a * dir + s * across;
                        Move(d, b0, t0 + off);
                        Move(d, b0 + off, t0);
                        if (math.abs(a) < 1f)
                            Move(d * 1.5f, b0 + off, t0 + off);   // the whole line across: both stations move
                    }
                SortStable(moves, (x, y) => x.Cost.CompareTo(y.Cost));
                moves.RemoveAll(m => !LineFits(g, lifts, lf, m.Cb, m.Ct));
                float3 At(int c) => new float3(g.World(c).x, g.H[c], g.World(c).y);
                string how = null;
                (float Cost, int Cb, int Ct, string Type, SkiRead Read)? flag = null;
                for (int mi = 0; mi < moves.Count; mi++)
                {
                    var (cost, cb, ct) = moves[mi];
                    if (mi % TowerBatch == 0)   // the next batch of plans at once, in the order of the moves
                        FitMany(g, lf.Type, moves.GetRange(mi, Math.Min(TowerBatch, moves.Count - mi)).ConvertAll(m => (At(m.Cb), At(m.Ct))));
                    float3 nb = At(cb), nt = At(ct);
                    TowerFit f = FitOf(g, lf.Type, nb, nt, out _);
                    if (f == TowerFit.HandFit || f == TowerFit.Flagged && (f0 == TowerFit.Flagged || flag != null))
                        continue;
                    Place(cb, ct);
                    SkiRead after = Keeps();
                    if (after == null)
                    {
                        Restore();
                        continue;
                    }
                    if (f == TowerFit.Flagged)
                    {
                        flag = (cost, cb, ct, lf.Type, after);
                        Restore();
                        continue;
                    }
                    before = after;
                    shifted++;
                    shiftSum += math.distance(nb.xz, ob.xz) + math.distance(nt.xz, ot.xz);
                    how = $"shifted (bottom {math.distance(nb.xz, ob.xz):F0} m, top {math.distance(nt.xz, ot.xz):F0} m)";
                    break;
                }

                // 2. another lift type on the same line: the next one up the ladder (more sheave wheels), where the line keeps its norms
                if (how == null && lf.Kind == "main")
                    foreach (string alt in oType == "fixed-grip" ? new[] { "detachable", "gondola" } : oType == "detachable" ? new[] { "gondola" } : new string[0])
                    {
                        // a gondola only within the scorecard v5 fleet allowance (1 + 1 per 6 km2 of gondolas and trams under 700 m of rise)
                        if (Aerial(alt) && lf.Rise <= TowerTrunkRiseM && lifts.Count(o => Aerial(o.Type) && o.Rise <= TowerTrunkRiseM) >= 1 + (int)Math.Floor(km2 / TowerAerialKm2))
                            continue;
                        lf.Type = alt;
                        TowerFit f = LineFits(g, lifts, lf, obc, otc) ? FitOf(g, alt, ob, ot, out _) : TowerFit.HandFit;
                        SkiRead after = f == TowerFit.HandFit || f == TowerFit.Flagged && (f0 == TowerFit.Flagged || flag != null) ? null : Keeps();
                        if (after == null)
                        {
                            Restore();
                            continue;
                        }
                        if (f == TowerFit.Flagged)
                        {
                            flag = (0f, obc, otc, alt, after);
                            Restore();
                            continue;
                        }
                        before = after;
                        retyped++;
                        how = $"type {oType} -> {alt}";
                        break;
                    }

                // 3. flagged: one tower over its limit and nothing else wrong is buildable
                if (how == null && flag is { } fl)
                {
                    Place(fl.Cb, fl.Ct);
                    lf.Type = fl.Type;
                    before = fl.Read;
                    if (fl.Type != oType)
                    {
                        retyped++;
                        how = $"type {oType} -> {fl.Type}, flagged";
                    }
                    else
                    {
                        shifted++;
                        shiftSum += math.distance(lf.Bottom.xz, ob.xz) + math.distance(lf.Top.xz, ot.xz);
                        how = $"shifted (bottom {math.distance(lf.Bottom.xz, ob.xz):F0} m, top {math.distance(lf.Top.xz, ot.xz):F0} m), flagged";
                    }
                }
                TowerFit now = how == null ? f0 : FitOf(g, lf.Type, lf.Bottom, lf.Top, out _);
                if (now == TowerFit.Sound)
                    sound++;
                else if (now == TowerFit.Flagged)
                    flagged++;
                else
                    hand++;
                if (how != null)
                {
                    changed++;
                    plan.Log.Add($"towers L{lf.Id} ({oType} {oLen:F0} m): {(f0 == TowerFit.Flagged ? "flagged" : $"hand fit, {over0} tower(s) over the cap")} -> {how}");
                }
                else if (f0 == TowerFit.HandFit)
                    marked.Add($"L{lf.Id} ({oType} {oLen:F0} m, {over0} over)");
            }
            plan.Log.Add($"towers: {lifts.Count} lifts; sound {sound0} -> {sound}, flagged {flagged0} -> {flagged}, hand fit {hand0} -> {hand}; " +
                $"shifted {shifted} ({(shifted > 0 ? shiftSum / shifted : 0f):F0} m mean station move), retyped {retyped}; " +
                $"{m_Towers?.Plans ?? 0} tower plans, {m_Towers?.Hits ?? 0} cached" + (marked.Count > 0 ? "; kept for a hand fit: " + string.Join(", ", marked) : ""));
            return changed;
        }

        /// <summary>The stations under another lift's rope as scorecard v5 counts them (a station within 40 m of the line, between 10 % and
        /// 90 % of its length).</summary>
        private static int RopeFaults(List<Lift> lifts)
        {
            int n = 0;
            for (int i = 0; i < lifts.Count; i++)
                for (int j = 0; j < lifts.Count; j++)
                {
                    if (i == j)
                        continue;
                    float2 b = lifts[j].Bottom.xz, d = lifts[j].Top.xz - b;
                    float l2 = math.lengthsq(d);
                    if (l2 < 1f)
                        continue;
                    foreach (float2 st in new[] { lifts[i].Bottom.xz, lifts[i].Top.xz })
                    {
                        float f = math.dot(st - b, d) / l2;
                        float off = math.abs((st - b).x * d.y - (st - b).y * d.x) / math.sqrt(l2);
                        if (f >= 0.1f && f <= 0.9f && off < TowerRopeM)
                            n++;
                    }
                }
            return n;
        }

        /// <summary>The pairs of lines that ride one on the other (stacked, a violation) and that run side by side (parallel, a layout
        /// cost), as scorecard v5.1 counts them: stacked first, then parallel by the overlap against the shorter line.</summary>
        private static (int Stacked, int Parallel) PairFaults(List<Lift> lifts)
        {
            int stacked = 0, parallel = 0;
            for (int i = 0; i < lifts.Count; i++)
                for (int j = 0; j < i; j++)
                {
                    float2 bi = lifts[i].Bottom.xz, ti = lifts[i].Top.xz, bj = lifts[j].Bottom.xz, tj = lifts[j].Top.xz;
                    float2 di = ti - bi, dj = tj - bj;
                    float li = math.length(di), lj = math.length(dj);
                    if (li < 1f || lj < 1f)
                        continue;
                    float cos = math.dot(di / li, dj / lj);
                    if (cos >= math.cos(math.radians(TowerStackDeg)) && math.distance(ti, tj) < TowerStackM && math.distance(bi, bj) > TowerStackM)
                    {
                        stacked++;
                        continue;
                    }
                    if (cos < math.cos(math.radians(TowerParallelDeg)))
                        continue;
                    bool iLong = li >= lj;
                    float2 b0 = iLong ? bi : bj, u = iLong ? di / li : dj / lj, c0 = iLong ? bj : bi, c1 = iLong ? tj : ti;
                    float lLong = Math.Max(li, lj);
                    float s0 = math.dot(c0 - b0, u), s1 = math.dot(c1 - b0, u);
                    float lo = Math.Max(0f, Math.Min(s0, s1)), hi = Math.Min(lLong, Math.Max(s0, s1));
                    if (hi - lo < TowerParallelShare * Math.Min(li, lj))
                        continue;
                    float2 mid = math.lerp(c0, c1, (0.5f * (lo + hi) - s0) / math.max(1f, s1 - s0));
                    float2 off = mid - b0;
                    if (math.abs(off.x * u.y - off.y * u.x) < TowerParallelM)
                        parallel++;
                }
            return (stacked, parallel);
        }

        /// <summary>An element's highest point at its 10 m samples (the raw heights).</summary>
        private static float3 HighestOf(Grid g, List<float2> pts)
        {
            float3 best = new float3(0f, float.NegativeInfinity, 0f);
            foreach (float2 p in Densify(pts))
            {
                float h = RawH(g, p);
                if (h > best.y)
                    best = new float3(p.x, h, p.y);
            }
            return best;
        }
    }
}
