// RunsFirstPlanner layout (round v4): what a designer sees on the trail map, and what scorecard v4's layout term reads. Each lift has
// its own terminal. Two bottoms (or two tops) on one spot are one shared station, which no real lift has. A top beside the next
// lift's bottom is a transfer and is fine. A hub does not crowd: more than 4 terminals within 300 m (6 at a base) read as a
// knot of ropes. Lift lines do not climb side by side up one valley (same direction within 20 degrees, under 250 m apart,
// overlapping 40 % of the longer line). A designer runs one long lift and branches from its top.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        // scorecard v4's layout rules
        private const float LaySameSpotM = 40f, LayHubM = 300f, LayParallelDeg = 20f, LayParallelM = 250f, LayParallelShare = 0.4f;
        // a station that shares its spot moves to a pad of its own this far away, the nearest first
        private const float SpreadMinM = 46f, SpreadMaxM = 130f;
        private const float LotMoveMinM = 18f;   // a station that only leaves another lot starts looking this near (T171)
        private readonly List<string> EarlyLog = new List<string>();   // what the lift placement did, for the plan's log

        // Station lots (post-release round 1, T171). A station is a building on a lot (16 x 24 to 48 x 24 m by lift type). The layout
        // rules above read stations as points: two bottoms (or two tops) keep SpreadMinM, but a top beside another lift's bottom (a
        // transfer) had no distance at all, and a station the game already has was only a blocked disc of its own size around its
        // stub tip. So a planned top could sit on a bottom station. The rule: the discs that hold two lots (PlanGeometry.
        // StationHalfDiagonalOf) do not overlap, whatever the ends, and a station that moves or is placed keeps LotGapM more.
        private const float LotGapM = 4f;
        private const float LotMaxR = 26.8f;   // the largest lot's half diagonal (the tram's): a station spot picked before its lift has a type

        /// <summary>The half diagonal of a lift type's station lot.</summary>
        private static float LotR(string type) => PlanGeometry.StationHalfDiagonalOf(TowerTypeOf(type));

        /// <summary>A station lot of half diagonal r at p keeps LotGapM from every station the game already has.</summary>
        private static bool PadFree(Grid g, float2 p, float r)
        {
            if (g.Stations != null)
                foreach (RunsFirstInputs.Station s in g.Stations)
                    if (math.distance(s.Xz, p) < s.RadiusM + r + LotGapM)
                        return false;
            return true;
        }

        /// <summary>A station lot of half diagonal r at p is closer than LotGapM to the lot of one of these lifts' stations (either end),
        /// or to a station the game already has.</summary>
        private static bool LotBlocked(Grid g, List<Lift> others, float2 p, float r)
        {
            foreach (Lift o in others)
            {
                float need = LotR(o.Type) + r + LotGapM;
                if (math.distance(o.Bottom.xz, p) < need || math.distance(o.Top.xz, p) < need)
                    return true;
            }
            return !PadFree(g, p, r);
        }

        /// <summary>The station lots that overlap: each pair of stations of two lifts (any ends), and each planned station on a station
        /// the game already has.</summary>
        private static int LotClashes(Grid g, List<Lift> lifts)
        {
            int n = 0;
            for (int a = 0; a < lifts.Count; a++)
            {
                float ra = LotR(lifts[a].Type);
                float2 ab = lifts[a].Bottom.xz, at = lifts[a].Top.xz;
                for (int b = 0; b < a; b++)
                {
                    float need = ra + LotR(lifts[b].Type), need2 = need * need;
                    float2 bb = lifts[b].Bottom.xz, bt = lifts[b].Top.xz;
                    n += (math.distancesq(ab, bb) < need2 ? 1 : 0) + (math.distancesq(ab, bt) < need2 ? 1 : 0)
                        + (math.distancesq(at, bb) < need2 ? 1 : 0) + (math.distancesq(at, bt) < need2 ? 1 : 0);
                }
                if (g.Stations != null)
                    foreach (RunsFirstInputs.Station s in g.Stations)
                        n += (math.distance(s.Xz, ab) < s.RadiusM + ra ? 1 : 0) + (math.distance(s.Xz, at) < s.RadiusM + ra ? 1 : 0);
            }
            return n;
        }

        /// <summary>The last word on station lots, after the passes that add, retype and shift lifts (T171): a station whose lot still
        /// overlaps another lot moves to a pad of its own (SpreadTerminals, lots only: its tower plan gets no worse). What cannot move
        /// stays and is named in the log.</summary>
        private void ClearLots(Grid g, PlanDraft plan, List<Lift> lifts)
        {
            int before = LotClashes(g, lifts);
            if (before == 0)
                return;
            int moved = O.Spread ? SpreadTerminals(g, lifts, plan, null, false, true) : 0;
            int after = LotClashes(g, lifts);
            plan.Log.Add($"station lots: {before} overlap(s), {moved} station(s) moved, {after} left" + (after > 0 ? " (place those stations by hand)" : ""));
        }

        /// <summary>Scorecard v4's parallel test: the two lines climb the same way within 20 degrees, overlap 40 % of the longer one
        /// along it, and lie under 250 m apart at the overlap's middle.</summary>
        private static bool ParallelLines(float2 bi, float2 ti, float2 bj, float2 tj)
        {
            float2 di = ti - bi, dj = tj - bj;
            float li = math.length(di), lj = math.length(dj);
            if (li < 1f || lj < 1f || math.dot(di / li, dj / lj) < math.cos(math.radians(LayParallelDeg)))
                return false;
            bool iLong = li >= lj;
            float2 b0 = iLong ? bi : bj, u = iLong ? di / li : dj / lj, c0 = iLong ? bj : bi, c1 = iLong ? tj : ti;
            float lLong = Math.Max(li, lj);
            float s0 = math.dot(c0 - b0, u), s1 = math.dot(c1 - b0, u);
            float lo = Math.Max(0f, Math.Min(s0, s1)), hi = Math.Min(lLong, Math.Max(s0, s1));
            if (hi - lo < LayParallelShare * lLong)
                return false;
            float2 mid = math.lerp(c0, c1, (0.5f * (lo + hi) - s0) / math.max(1f, s1 - s0));
            float2 off = mid - b0;
            return math.abs(off.x * u.y - off.y * u.x) < LayParallelM;
        }

        /// <summary>Scorecard v5.1's stacked lines, a validity violation: one lift riding up on another's line (tops within 300 m, bottoms
        /// over 300 m apart, directions within 30 degrees), with a margin for the station moves of the later passes. Fans (bottoms
        /// together) are fine.</summary>
        private bool StackedLines(float2 bi, float2 ti, float2 bj, float2 tj)
        {
            float2 di = ti - bi, dj = tj - bj;
            float li = math.length(di), lj = math.length(dj);
            return li >= 1f && lj >= 1f && math.dot(di / li, dj / lj) >= math.cos(math.radians(StackDegM)) && math.distance(ti, tj) < StackTopMm
                && math.distance(bi, bj) > StackBottomMm;
        }

        /// <summary>Scorecard v5's lone base, for a lift about to be placed. The lift is a lone base when:
        /// - its bottom is low (the lowest third of the lift-served vertical, the new lift counted)
        /// - no lift top lies within 150 m
        /// - no other low bottom lies within 300 m (it would be a base area of one) while the plan has another base area
        /// - no run from a base lift's top ends at it (a pod).</summary>
        private static bool LoneBase(Grid g, PlanDraft plan, List<Lift> lifts, float2 b, float2 t)
        {
            if (lifts.Count == 0)
                return false;
            float hb = RawH(g, b);
            float vLo = Math.Min(hb, lifts.Min(lf => RawH(g, lf.Bottom.xz))), vHi = Math.Max(RawH(g, t), lifts.Max(lf => RawH(g, lf.Top.xz)));
            float cut = vLo + (vHi - vLo) / 3f;
            bool Low(float2 p) => RawH(g, p) <= cut && !lifts.Any(lf => math.distance(lf.Top.xz, p) < BaseTopM2) && math.distance(t, p) >= BaseTopM2;
            if (!Low(b))
                return false;
            var low = lifts.Where(lf => Low(lf.Bottom.xz)).ToList();
            if (low.Count == 0 || low.Any(lf => math.distance(lf.Bottom.xz, b) < BaseAreaM2))
                return false;
            // a pod: a run from a base lift's top ends at the bottom (unknown while the code places the lifts: no runs yet)
            if (plan == null)
                return true;
            return !plan.Runs.Any(r => !r.Graded && r.Pts.Count >= 2 && math.distance(r.Pts[r.Pts.Count - 1], b) < 55f
                && low.Any(lf => math.distance(lf.Top.xz, r.Pts[0]) < 150f));
        }

        private const float BaseTopM2 = 160f, BaseAreaM2 = 290f;
        // off by default (RF_LONEBASE=1): rejecting lone-base cover lifts and bridges took 24 -> 15 lone bases. It cost coverage and
        // grade mix where those lifts open terrain (ft5-r3 94.7 -> 92.8). Visible 82.59 -> 82.06
        private bool NoLoneBase => O.NoLoneBase;

        private float StackDegM => O.StackDegM;

        private float StackTopMm => O.StackTopMm;

        private float StackBottomMm => O.StackBottomMm;
        private bool NoStacked => O.NoStacked;

        // scorecard v5's fall line: no cost to 30 degrees off the ground's mean downhill direction, full cost at 60
        private const float FallFreeDeg = 30f, FallFullDeg = 60f;

        /// <summary>Scorecard v5's fall-line angle of a lift line: the chord against the mean uphill gradient of the raw ground under it
        /// (sampled every step m). The angle is 0 where that mean is under the green line (no fall line).</summary>
        private static float FallDeg(Grid g, float2 b, float2 t, float step = 10f)
        {
            float2 d = t - b;
            float len = math.length(d);
            if (len < 1f)
                return 0f;
            float2 u = d / len, up = float2.zero;
            int k = 0;
            for (float along = 0f; along <= len; along += step, k++)
            {
                float2 q = b + u * along;
                const float dg = 10f;
                up += new float2(RawH(g, q + new float2(dg, 0f)) - RawH(g, q - new float2(dg, 0f)), RawH(g, q + new float2(0f, dg)) - RawH(g, q - new float2(0f, dg))) / (2f * dg);
            }
            up /= Math.Max(1, k);
            return math.length(up) < 0.06f ? 0f : math.degrees(math.acos(math.clamp(math.dot(u, math.normalize(up)), -1f, 1f)));
        }

        /// <summary>Scorecard v5's fall-line cost of a lift line (0 to 30 degrees off, 1 from 60).</summary>
        private static float FallCost(Grid g, float2 b, float2 t, float step = 10f) =>
            math.saturate((FallDeg(g, b, t, step) - FallFreeDeg) / (FallFullDeg - FallFreeDeg));

        /// <summary>The lift pairs that run parallel as scorecard v4 reads them, with lift lf at stations b and t.</summary>
        private static int ParallelsOf(List<Lift> lifts, Lift lf, float2 b, float2 t) =>
            lifts.Count(o => o != lf && ParallelLines(b, t, o.Bottom.xz, o.Top.xz));

        /// <summary>Scorecard v4's crowded terminals: the terminals past 4 in a 300 m chain (6 where it holds a base's bottom), and
        /// each bottom (or top) on an earlier lift's bottom (top).</summary>
        private static int HubExcess(Grid g, List<Lift> lifts)
        {
            int[] root = V3Bases(g, lifts, out HashSet<int> bases);
            var term = new List<(float2 P, bool Base)>();
            foreach (var (lf, L) in lifts.Select((lf, L) => (lf, L)))
            {
                term.Add((lf.Bottom.xz, bases.Contains(root[L])));
                term.Add((lf.Top.xz, false));
            }
            var hub = new int[term.Count];
            for (int a = 0; a < term.Count; a++)
            {
                hub[a] = a;
                for (int b = 0; b < a; b++)
                    if (math.distance(term[a].P, term[b].P) < LayHubM)
                        hub[Root(hub, a)] = Root(hub, b);
            }
            int excess = 0;
            foreach (var grp in Enumerable.Range(0, term.Count).GroupBy(a => Root(hub, a)))
                excess += Math.Max(0, grp.Count() - (grp.Any(a => term[a].Base) ? 6 : 4));
            for (int a = 0; a < term.Count; a++)
                for (int b = a % 2; b < a; b += 2)
                    if (math.distance(term[a].P, term[b].P) < LaySameSpotM)
                    {
                        excess++;
                        break;
                    }
            return excess;
        }

        // the spread may move a station along its own line too (RF_NOSPREADAXIS=1: off). It also runs after each lift of the search
        // and of the coverage pass (RF_NOSPREADEACH=1: off)
        private bool SpreadAxis => O.SpreadAxis;
        private bool SpreadEach => O.SpreadEach;

        /// <summary>A flat pad 46-130 m from cell c (for a bottom no higher than c, for a top no lower) that no other station of that
        /// end holds. It is where a station on another lift's station can move to.</summary>
        private static bool FreePad(Grid g, List<Lift> lifts, int c, bool bottom)
        {
            float2 at = g.World(c);
            float hAt = RawH(g, at);
            for (float r = SpreadMinM; r <= SpreadMaxM; r += 7f)
                for (int k = 0; k < 24; k++)
                {
                    float a = k * (float)Math.PI / 12f;
                    int cc = CellOf(g, at + r * new float2((float)Math.Cos(a), (float)Math.Sin(a)));
                    if (cc < 0 || !g.In[cc])
                        continue;
                    float2 p = g.World(cc);
                    float dh = RawH(g, p) - hAt;
                    if ((bottom ? dh > 8f : dh < -15f) || RawGrade(g, p) > StationMax
                        || lifts.Exists(o => math.distance(bottom ? o.Bottom.xz : o.Top.xz, p) < SpreadMinM))
                        continue;
                    return true;
                }
            return false;
        }

        /// <summary>Each lift gets its own terminal. A bottom on an earlier lift's bottom (or a top on an earlier lift's top) moves to
        /// a pad of its own 46-130 m away, the nearest first. A bottom moves to a pad no higher than it was (the runs come down to it).
        /// A top moves to a pad no lower (its traverses leave from it). Where it cannot move, the earlier lift's station moves
        /// instead. The code keeps a move where all of these hold:
        /// - the lift still fits (pads, norms, rope clearance, no crossing of its kind, no station under a rope)
        /// - its line stays off the runs
        /// - the layout gets no worse (crowded terminals, parallel lines)
        /// - once there is a plan, the ski graph loses nothing.
        /// Before the plan (plan null) the code holds the line off the picked lines instead. Returns the stations moved.</summary>
        private int SpreadTerminals(Grid g, List<Lift> lifts, PlanDraft plan, PointHash runs, bool bottomsOnly = false, bool lotsOnly = false)
        {
            int moved = 0;
            SkiRead before = null;
            var els = plan != null ? ChordEls(g, plan) : null;
            // lot: the station moves off another lot (T171). A short move is enough, so the spots start nearer
            bool TryMove(Lift lf, int end, bool lot)
            {
                float2 at = end == 0 ? lf.Bottom.xz : lf.Top.xz;
                float hAt = RawH(g, at);
                var moves = new List<(float Cost, int C)>();
                var seen = new HashSet<int>();
                float rMin = lot ? LotMoveMinM : SpreadMinM;
                for (float r = rMin; r <= SpreadMaxM; r += 7f)
                    for (int k = 0; k < 24; k++)
                    {
                        float a = k * (float)Math.PI / 12f;
                        int c = CellOf(g, at + r * new float2((float)Math.Cos(a), (float)Math.Sin(a)));
                        if (c < 0 || !g.In[c] || !seen.Add(c))
                            continue;
                        float2 p = g.World(c);
                        float dh = RawH(g, p) - hAt;
                        if (end == 0 ? dh > 8f : dh < -15f)
                            continue;
                        moves.Add((math.distance(p, at) + (end == 0 ? Math.Max(0f, dh) * 4f : Math.Max(0f, -dh) * 3f), c));
                    }
                // or along its own line, toward the other station. Where lines fan out from one spot, a sideways move crosses the next line.
                // A shorter line crosses nothing new (a little more rise allowed: the station stays on its own axis)
                if (SpreadAxis)
                {
                    float2 to = end == 0 ? lf.Top.xz : lf.Bottom.xz, u = math.normalize(to - at);
                    for (float r = rMin; r <= SpreadMaxM; r += 7f)
                    {
                        int c = CellOf(g, at + r * u);
                        if (c < 0 || !g.In[c] || !seen.Add(c))
                            continue;
                        float dh = RawH(g, g.World(c)) - hAt;
                        if (end == 0 ? dh > 20f : dh < -25f)
                            continue;
                        moves.Add((r + (end == 0 ? Math.Max(0f, dh - 8f) * 4f : Math.Max(0f, -dh - 15f) * 3f), c));
                    }
                }
                SortStable(moves, (x, y) => x.Cost.CompareTo(y.Cost));
                var others = lifts.Where(o => o != lf).ToList();
                int parBefore = ParallelsOf(lifts, lf, lf.Bottom.xz, lf.Top.xz), hubBefore = HubExcess(g, lifts), lotBefore = LotClashes(g, lifts);
                float lotR = LotR(lf.Type);
                // lots only (after the tower pass): the move may not cost the lift its tower plan
                bool towers = lotsOnly && O.Towers;
                TowerFit fitBefore = towers ? FitOf(g, lf.Type, lf.Bottom, lf.Top, out _) : TowerFit.HandFit;
                float shareBefore = els != null ? ChordShareV3(lf, els, out _) : 0f;
                var (ob, ot, obc, otc, oRise, oLen) = (lf.Bottom, lf.Top, lf.BottomCell, lf.TopCell, lf.Rise, lf.Length);
                void Restore() => (lf.Bottom, lf.Top, lf.BottomCell, lf.TopCell, lf.Rise, lf.Length) = (ob, ot, obc, otc, oRise, oLen);
                int tries = 0;
                var why = new int[8];   // near, pad, fits, rope/cross, parallel, runs, hub/chord, skiing
                foreach (var (cost, c) in moves)
                {
                    float2 p = g.World(c);
                    if (others.Exists(o => math.distance(end == 0 ? o.Bottom.xz : o.Top.xz, p) < SpreadMinM) || LotBlocked(g, others, p, lotR))
                    {
                        why[0]++;
                        continue;
                    }
                    int b = end == 0 ? c : lf.BottomCell, t = end == 0 ? lf.TopCell : c;
                    if (RawGrade(g, p) > StationMax)
                    {
                        why[1]++;
                        continue;
                    }
                    if (!LineFits(g, lifts, lf, b, t))
                    {
                        why[2]++;
                        continue;
                    }
                    if (UnderRope(g, others, t, b) || others.Exists(o => SegCrossScore(o.Bottom.xz, o.Top.xz, g.World(b), g.World(t))))
                    {
                        why[3]++;
                        continue;
                    }
                    float2 pb = g.World(b), pt = g.World(t);
                    if (ParallelsOf(lifts, lf, pb, pt) > parBefore)
                    {
                        why[4]++;
                        continue;
                    }
                    if (plan == null && OnRuns(g, runs, b, t))
                    {
                        why[5]++;
                        continue;
                    }
                    if (++tries > 40)
                        break;
                    if (plan != null)
                        before ??= SkiRead.Of(g, plan, lifts);
                    if (end == 0)
                    {
                        lf.BottomCell = c;
                        lf.Bottom = new float3(p.x, g.H[c], p.y);
                    }
                    else
                    {
                        lf.TopCell = c;
                        lf.Top = new float3(p.x, g.H[c], p.y);
                    }
                    lf.Rise = lf.Top.y - lf.Bottom.y;
                    lf.Length = math.distance(lf.Top, lf.Bottom);
                    // the move must pay: fewer crowded terminals (and no new lot overlap), or fewer lot overlaps (and no more crowding)
                    int hubNow = HubExcess(g, lifts), lotNow = LotClashes(g, lifts);
                    bool pays = (!lot && hubNow < hubBefore && lotNow <= lotBefore) || (lotNow < lotBefore && hubNow <= hubBefore);
                    if (!pays || (els != null && ChordShareV3(lf, els, out _) > Math.Max(ChordShareMax, shareBefore)))
                    {
                        why[6]++;
                        Restore();
                        continue;
                    }
                    if (towers && FitOf(g, lf.Type, lf.Bottom, lf.Top, out _) > fitBefore)
                    {
                        why[2]++;
                        Restore();
                        continue;
                    }
                    if (plan != null)
                    {
                        SkiRead after = SkiRead.Of(g, plan, lifts);
                        if (before.Loses(after))
                        {
                            why[7]++;
                            Restore();
                            continue;
                        }
                        before = after;
                    }
                    moved++;
                    (plan?.Log ?? EarlyLog).Add($"L{lf.Id}'s {(end == 0 ? "bottom" : "top")} station moved {math.distance(p, at):F0} m to {(lot ? "a lot" : "a terminal")} of its own " +
                        $"({RawH(g, p) - hAt:+0;-0} m)");
                    return true;
                }
                if (O.Debug)
                    Trace($"DEBUG spread L{lf.Id} {(end == 0 ? "bottom" : "top")}{(plan == null ? " (before the plan)" : "")}: {moves.Count} spot(s), tries {tries}; near {why[0]}, pad {why[1]}, " +
                        $"fits {why[2]}, rope/cross {why[3]}, parallel {why[4]}, runs {why[5]}, hub/chord {why[6]}, skiing {why[7]}");
                return false;
            }
            for (int L = 0; L < lifts.Count; L++)
                for (int end = 0; end < (bottomsOnly ? 1 : 2); end++)
                {
                    float2 at = end == 0 ? lifts[L].Bottom.xz : lifts[L].Top.xz;
                    int o = lotsOnly ? -1 : FirstOr(Enumerable.Range(0, L), M => math.distance(end == 0 ? lifts[M].Bottom.xz : lifts[M].Top.xz, at) < LaySameSpotM + 2f, -1);
                    if (o >= 0)
                    {
                        if (!TryMove(lifts[L], end, false))
                            TryMove(lifts[o], end, false);
                        continue;
                    }
                    // T171: the lot on the lot of an earlier lift's station (either end), or on a station the game already has
                    int oEnd = -1;
                    float r = LotR(lifts[L].Type);
                    for (int M = 0; M < L && o < 0; M++)
                        for (int e2 = 0; e2 < 2 && o < 0; e2++)
                            if (math.distance(e2 == 0 ? lifts[M].Bottom.xz : lifts[M].Top.xz, at) < r + LotR(lifts[M].Type))
                            {
                                o = M;
                                oEnd = e2;
                            }
                    if ((o >= 0 || !PadFree(g, at, r)) && !TryMove(lifts[L], end, true) && o >= 0)
                        TryMove(lifts[o], oEnd, true);
                }
            return moved;
        }

        /// <summary>Scorecard v4's sharp turns (60 degrees between 20 m legs) per green track, with where the track came from.</summary>
        private static int SharpTurns(List<float2> pl)
        {
            List<float2> p = Densify(pl);
            int n = 0;
            for (int k = 4; k < p.Count; k += 2)
            {
                float2 a1 = p[k - 2] - p[k - 4], a2 = p[k] - p[k - 2];
                if (math.lengthsq(a1) >= 1f && math.lengthsq(a2) >= 1f && math.dot(math.normalize(a1), math.normalize(a2)) < 0.5f)
                    n++;
            }
            return n;
        }
    }
}
