// Runs-first realizer: pistes and graded pistes (spec section 5). The planner's centerline is cut into stretches of free samples, each
// stretch is marched to its half widths, cut again at a long pinch, and kept when long enough. A junction end that misses its partner
// by more than 20 m is extended straight toward it (at most 40 m). Each piece's ring is built (v2's GradeRibbon tail), checked exactly,
// and rated by SkiRunRater on today's ground. Pure. Spec: docs/research/quickruns-v2/port/REALIZER-SPEC.md section 5.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstRealizer
    {
        // Why a sample is not free
        private const byte CFree = 0, CRegion = 1, CObstacle = 2, CPiste = 3, CTrack = 4;

        /// <summary>The context of one piste: its partners (accepted pistes it may touch), its gap and width, the grade ceiling.</summary>
        private sealed class PisteCtx
        {
            public Elem E;
            public bool Graded;
            public float TargetHw, Gap, Ceil;
            public readonly HashSet<int> Partners = new HashSet<int>();
            public readonly List<int> StartJ = new List<int>(), EndJ = new List<int>();
            public readonly List<Neighbor> Others = new List<Neighbor>();   // fair share (iteration 2, item 3)
        }

        /// <summary>Another piste centerline near this one, for the fair-share width: its samples, its gap, whether the planner relates
        /// the two lines, and the points where it crosses this run.</summary>
        private sealed class Neighbor
        {
            public float2[] Pts;
            public float2 Lo, Hi;
            public float Gap;
            public bool Related;
            public readonly List<float2> Crossings = new List<float2>();
        }

        /// <summary>A candidate piece: samples, half widths, junction sets at its two ends.</summary>
        private sealed class Piece
        {
            public int S, E;                               // sample range in the source path
            public List<int> J0 = new List<int>(), J1 = new List<int>();
            public bool Orig0, Orig1;                      // the piece starts at the source's start / ends at its end (after a junction trim)
        }

        /// <summary>The outcome of one ring attempt.</summary>
        private string m_RateWhy;   // the last Unrated answer (report only)

        private sealed class Built
        {
            public AutoReject Why = AutoReject.Kept;
            public int Blocker = -1;                       // the accepted area an Overlap came from
            public float2[] Ring, Line;
            public float[] Hl, Hr;                         // the half widths per Line sample that made Ring
            public float3[] Nodes;
            public int LeftCount;                          // ring[0 .. LeftCount - 1] is the left edge (start to end)
            public bool Fallback;                          // rated by the P90 fallback (a mask that is not connected)
            public string RateWhy;                         // the rater's answer when Unrated (report only)
            public SlopeBand Band;
            public float RatingGrade, MeanGrade, Steepest, Drop, TopY, BottomY, LengthM;
        }

        private void RealizePistes()
        {
            foreach (Elem e in PisteOrder())
            {
                if (Canceled)
                    return;
                RealizePiste(e);
                e.Done = true;
            }
        }

        private float PairGap(float own, int other) => Math.Min(own, m_P[other].GapM);

        private void RealizePiste(Elem e)
        {
            var ctx = new PisteCtx { E = e, Graded = e.Type == ElemType.GradedPiste };
            int bandIx = Math.Max(1, Math.Min(4, (int)e.Planned)) - 1;
            ctx.TargetHw = ctx.Graded ? GradedHalfWidthM : 0.5f * PlanGeometry.TargetWidthM[bandIx];
            ctx.Gap = PlanGeometry.GapOf(m_Density, 2f * ctx.TargetHw);
            ctx.Ceil = ctx.Graded ? float.PositiveInfinity : HardCeiling[bandIx];
            GradePath path = GradePath.Build(e.Pts, SampleM);
            if (path == null)
            {
                DropElem(e, AutoReject.TooShort, "no centerline");
                return;
            }
            int n = path.Count;
            if (!ctx.Graded)
                FairShare(ctx, path);
            // Partners (5.1): the accepted pistes the planner relates to this run, and those whose ring its centerline enters
            for (int b = 0; b < m_P.Count; b++)
            {
                Area B = m_P[b];
                Elem be = m_E[B.Elem];
                bool rel = Related(e, be);
                if (rel || Enters(path, B))
                    ctx.Partners.Add(b);
                if (StartsOff(e, be) && PlanGeometry.PointRingDistance(B.Ring, path.Xz[0]) <= RelationM + MinHalfWidthM)
                    ctx.StartJ.Add(b);
                if (EndsOnto(e, be) && PlanGeometry.PointRingDistance(B.Ring, path.Xz[n - 1]) <= RelationM + MinHalfWidthM)
                    ctx.EndJ.Add(b);
            }
            // Free samples
            var cause = new byte[n];
            var owner = new int[n];
            for (int i = 0; i < n; i++)
                cause[i] = FreePiste(ctx, path.Xz[i], out owner[i]);
            // Converging lines (iteration 3, item 1): a line that comes so near a piste it has no relation with that a ring at
            // MinHalfWidthM would enter the pair gap (two fall lines converge) treats that piste as a partner from there. The free test
            // runs again with the partner need: the piece ends with a junction on it (taper, reach, extension) and is not dropped as
            // Overlap, and a line that leaves the zone farther down continues as the next segment.
            for (int pass = 0; pass < 3; pass++)
            {
                bool added = false;
                for (int i = 0; i < n; i++)
                {
                    int N = Converging(ctx, path.Xz[i]);
                    if (N >= 0 && ctx.Partners.Add(N))
                    {
                        added = true;
                        m_Rep.ConvergeJunctions++;
                    }
                }
                if (!added)
                    break;
                for (int i = 0; i < n; i++)
                    cause[i] = FreePiste(ctx, path.Xz[i], out owner[i]);
            }
            // Stretches of free samples
            var pieces = new List<Piece>();
            bool anyPiste = false, anyRegion = false;
            for (int i = 0; i < n; i++)
            {
                if (cause[i] == CPiste)
                    anyPiste = true;
                if (cause[i] == CRegion)
                    anyRegion = true;
                if (cause[i] != CFree || (i > 0 && cause[i - 1] == CFree))
                    continue;
                int j = i;
                while (j + 1 < n && cause[j + 1] == CFree)
                    j++;
                var pc = new Piece { S = i, E = j };
                pc.Orig0 = i == 0 || AllJunction(cause, owner, 0, i - 1, ctx);
                pc.Orig1 = j == n - 1 || AllJunction(cause, owner, j + 1, n - 1, ctx);
                StretchEnd(ctx, pc.J0, pc.Orig0, i > 0 ? cause[i - 1] : CFree, i > 0 ? owner[i - 1] : -1, ctx.StartJ);
                StretchEnd(ctx, pc.J1, pc.Orig1, j < n - 1 ? cause[j + 1] : CFree, j < n - 1 ? owner[j + 1] : -1, ctx.EndJ);
                pieces.Add(pc);
            }
            if (pieces.Count == 0)
            {
                DropElem(e, anyPiste ? AutoReject.Overlap : anyRegion ? AutoReject.Region : AutoReject.Obstacle, "no free stretch");
                return;
            }
            // March each stretch, cut it at its longest pinch while over PinchShare of it is pinched (5.2)
            var final = new List<Piece>();
            foreach (Piece pc in pieces)
            {
                Sub(path, pc.S, pc.E, out List<float2> xz);
                March(ctx, xz, null, pc.J0, pc.J1, 1f, out float[] hl, out float[] hr);
                // a graded piste is 15 m at most: its pinch is under RetryShare of that (10.5 m), a plain piste's under PinchWidthM
                PinchSplit(pc, hl, hr, final, ctx.Graded ? RetryShare * 2f * GradedHalfWidthM : PinchWidthM);
            }
            AutoReject last = AutoReject.TooShort;
            int segment = 0;
            for (int k = 0; k < final.Count; k++)
            {
                Piece pc = final[k];
                if (Canceled)
                    return;
                float len = path.S[pc.E] - path.S[pc.S];
                float drop = H(path.Xz[pc.S]) - H(path.Xz[pc.E]);
                bool joined = pc.J0.Count > 0 && pc.J1.Count > 0;
                // Iteration 3, item 3: a remnant (a cut piece, under RemnantDropM down) stays when both ends connect something (a junction,
                // the source's own end, a lift bottom, the bottom band). A remnant with a free end is a stub that leads nowhere.
                bool remnant = (pc.S > 0 || pc.E < n - 1) && drop < RemnantDropM && pc.E - pc.S >= 1 && len >= MinJoinedPieceM;
                if (remnant)
                {
                    // a cut shorter than JunctionReachM to the next piece of the same line is a game link: that end connects too
                    bool near0 = k > 0 && path.S[pc.S] - path.S[final[k - 1].E] <= JunctionReachM;
                    bool near1 = k + 1 < final.Count && path.S[final[k + 1].S] - path.S[pc.E] <= JunctionReachM;
                    bool c0 = near0 || Connects(pc.J0, pc.Orig0, path.Xz[pc.S]), c1 = near1 || Connects(pc.J1, pc.Orig1, path.Xz[pc.E]);
                    if (!c0 || !c1)
                    {
                        m_Rep.PiecesLost++;
                        m_Rep.RemnantsDropped++;
                        m_Res.Rejects[(int)AutoReject.TooShort]++;
                        m_Rep.Lines.Add($"piste {e.Source} ({e.Kind}): piece {len:F0} m / {drop:F0} m down at ({path.Xz[pc.S].x:F0}, {path.Xz[pc.S].y:F0}) lost (a stub, its {(c0 ? "end" : "start")} is free)");
                        last = AutoReject.TooShort;
                        continue;
                    }
                }
                if (pc.E - pc.S < 1 || (!remnant && (joined ? len < MinJoinedPieceM : len < MinPieceM || (drop < MinPieceDropM && e.Kind != "learning"))) || len < MinJoinedPieceM)
                {
                    m_Rep.PiecesLost++;
                    m_Res.Rejects[(int)AutoReject.TooShort]++;
                    m_Rep.Lines.Add($"piste {e.Source} ({e.Kind}): piece {len:F0} m / {drop:F0} m down at ({path.Xz[pc.S].x:F0}, {path.Xz[pc.S].y:F0}) lost (too short{(joined ? ", joined" : "")})");
                    last = AutoReject.TooShort;
                    continue;
                }
                AutoReject why = BuildPiece(ctx, path, pc, segment);
                if (why == AutoReject.Kept)
                {
                    segment++;
                    if (remnant)
                        m_Rep.RemnantsKept++;
                }
                else
                {
                    last = why;
                    m_Res.Rejects[(int)why]++;
                    m_Rep.Lines.Add($"piste {e.Source} ({e.Kind}): piece at ({path.Xz[pc.S].x:F0}, {path.Xz[pc.S].y:F0}) dropped ({why}{(why == AutoReject.Unrated || why == AutoReject.Overlap ? ": " + m_RateWhy : "")})");
                }
            }
            if (e.Areas.Count == 0)
                DropElem(e, last, "every piece failed");
        }

        /// <summary>Item 3 of iteration 3: true when a piece end connects something. That is a junction, the source's own end, a lift
        /// bottom within RelationM, or the bottom band.</summary>
        private bool Connects(List<int> j, bool orig, float2 p)
        {
            if (j.Count > 0 || orig || H(p) <= m_BottomTop)
                return true;
            foreach (RunsFirstResult.Lift l in m_Plan.Lifts)
                if (math.distance(p, l.Bottom.xz) <= RelationM)
                    return true;
            return false;
        }

        private void DropElem(Elem e, AutoReject why, string what)
        {
            bool track = e.Type == ElemType.Track;
            (track ? m_Rep.TrackDrops : m_Rep.PisteDrops)[(int)why]++;
            m_Res.Rejects[(int)why]++;
            float2 p = e.Pts.Count > 0 ? e.Pts[0] : default;
            m_Rep.Lines.Add($"{(track ? "track" : "piste")} {e.Source} ({e.Kind}) at ({p.x:F0}, {p.y:F0}) dropped: {why}, {what}");
        }

        /// <summary>True when every sample in [a, b] is held by a junction partner (a trim at a junction, not a lost stretch).</summary>
        private bool AllJunction(byte[] cause, int[] owner, int a, int b, PisteCtx ctx)
        {
            for (int i = a; i <= b; i++)
                if (cause[i] != CPiste || !ctx.Partners.Contains(owner[i]))
                    return false;
            return true;
        }

        /// <summary>The junction set of a stretch end: the source's own end takes the planner's relation set; an end cut by a partner's
        /// ring takes that partner (a crossing or a relation met early).</summary>
        private static void StretchEnd(PisteCtx ctx, List<int> j, bool sourceEnd, byte cause, int owner, List<int> rel)
        {
            if (cause == CPiste && owner >= 0 && ctx.Partners.Contains(owner))
                j.Add(owner);
            if (sourceEnd)
                foreach (int r in rel)
                    if (!j.Contains(r))
                        j.Add(r);
        }

        /// <summary>Does the centerline enter the ring (a sample inside it)?</summary>
        private static bool Enters(GradePath path, Area b)
        {
            for (int i = 0; i < path.Count; i++)
            {
                float2 p = path.Xz[i];
                if (math.all(p >= b.Lo) && math.all(p <= b.Hi) && GradeRaster.Inside(b.Ring, p))
                    return true;
            }
            return false;
        }

        /// <summary>Spec 5.1: a sample is free when its cell is not BlockedAll or BlockedPiste, it keeps the gap from the nearest accepted
        /// piste (9.5 m from a partner), and, for a normal piste, its grade is under 100 %.</summary>
        private byte FreePiste(PisteCtx ctx, float2 p, out int own)
        {
            own = -1;
            if (!Cell(p, out int c))
                return CRegion;
            byte f = m_Flags[c];
            if ((f & FBlockRegion) != 0 || ((f & FNearEdge) != 0 && !InRegion(p, EdgeBlockM)))
                return CRegion;
            if ((f & (FBlockHard | FBlockPiste)) != 0)
                return CObstacle;
            int N = m_OwnPiste[c];
            if (N >= 0)
            {
                float need = ctx.Partners.Contains(N) ? MergeGapM + MinHalfWidthM : PairGap(ctx.Gap, N);
                if (m_DPiste[c] < need)
                {
                    own = N;
                    return CPiste;
                }
            }
            return CFree;
        }

        /// <summary>Item 1 of iteration 3: the accepted piste with no partner status whose pair gap a MinHalfWidthM ring at p would
        /// enter, or -1.</summary>
        private int Converging(PisteCtx ctx, float2 p)
        {
            if (!Cell(p, out int c))
                return -1;
            int N = m_OwnPiste[c];
            if (N < 0 || ctx.Partners.Contains(N))
                return -1;
            return m_DPiste[c] < PairGap(ctx.Gap, N) + MinHalfWidthM ? N : -1;
        }

        private static void Sub(GradePath path, int s, int e, out List<float2> xz)
        {
            xz = new List<float2>(e - s + 1);
            for (int i = s; i <= e; i++)
                xz.Add(path.Xz[i]);
        }

        /// <summary>The gap to accepted piste N at a sample sFrom from the piece start and sTo from its end (5.3): the pair gap, tapering
        /// to MergeGapM over MergeTaperM toward an end that is a junction with N.</summary>
        private float GapAt(PisteCtx ctx, int N, float sFrom, float sTo, List<int> j0, List<int> j1)
        {
            float g = PairGap(ctx.Gap, N);
            if (!ctx.Partners.Contains(N))
                return g;
            if (sFrom < MergeTaperM && j0.Contains(N))
                g = Math.Min(g, MergeGapM + (g - MergeGapM) * sFrom / MergeTaperM);
            if (sTo < MergeTaperM && j1.Contains(N))
                g = Math.Min(g, MergeGapM + (g - MergeGapM) * sTo / MergeTaperM);
            return g;
        }

        /// <summary>The half widths (5.2): per sample and side, march outward in 1 m steps to the first blocked cell, steep cell (normal
        /// pistes) or gap. Half width = free distance less 1 m, at least 1 m, at most the target (cap per sample when given). Then the v1
        /// smoothing.</summary>
        private void March(PisteCtx ctx, List<float2> xz, float[] cap, List<int> j0, List<int> j1, float scale, out float[] hl, out float[] hr)
        {
            int n = xz.Count;
            hl = new float[n];
            hr = new float[n];
            var s = new float[n];
            for (int i = 1; i < n; i++)
                s[i] = s[i - 1] + math.distance(xz[i - 1], xz[i]);
            float total = s[n - 1];
            for (int i = 0; i < n; i++)
            {
                float2 t = math.normalizesafe(xz[Math.Min(i + 1, n - 1)] - xz[Math.Max(i - 1, 0)], new float2(1f, 0f));
                float2 nl = new float2(-t.y, t.x);
                float hw = cap != null ? Math.Min(ctx.TargetHw, cap[i]) : ctx.TargetHw;
                FairCaps(ctx, xz[i], t, nl, hw, Math.Min(s[i], total - s[i]), out float capL, out float capR);
                hl[i] = MarchSide(ctx, xz[i], nl, capL, s[i], total - s[i], j0, j1);
                hr[i] = MarchSide(ctx, xz[i], -nl, capR, s[i], total - s[i], j0, j1);
            }
            Smooth(hl);
            Smooth(hr);
            if (scale < 1f)
                for (int i = 0; i < n; i++)
                {
                    hl[i] = Math.Max(1f, hl[i] * scale);
                    hr[i] = Math.Max(1f, hr[i] * scale);
                }
        }

        /// <summary>Iteration 2, item 3: the other piste centerlines (plain and graded, accepted or still to come, not the dropped ones)
        /// whose box comes within reach of this run, with the points where they cross it. Exact polylines, not a raster.</summary>
        private void FairShare(PisteCtx ctx, GradePath path)
        {
            Elem a = ctx.E;
            float reach = 2f * ctx.TargetHw + ctx.Gap + FairCrossM;
            foreach (Elem b in m_E)
            {
                if (b == a || b.Type == ElemType.Track || (b.Done && b.Areas.Count == 0))
                    continue;
                if (b.Lo.x > a.Hi.x + reach || b.Hi.x < a.Lo.x - reach || b.Lo.y > a.Hi.y + reach || b.Hi.y < a.Lo.y - reach)
                    continue;
                int bandIx = Math.Max(1, Math.Min(4, (int)b.Planned)) - 1;
                float bHw = b.Type == ElemType.GradedPiste ? GradedHalfWidthM : 0.5f * PlanGeometry.TargetWidthM[bandIx];
                var nb = new Neighbor { Lo = b.Lo, Hi = b.Hi, Gap = Math.Min(ctx.Gap, PlanGeometry.GapOf(m_Density, 2f * bHw)), Related = Related(a, b) };
                nb.Pts = new float2[b.Samp.Length];
                for (int k = 0; k < b.Samp.Length; k++)
                    nb.Pts[k] = b.Samp[k].xz;
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    float2 p0 = path.Xz[i], p1 = path.Xz[i + 1];
                    for (int k = 0; k + 1 < nb.Pts.Length; k++)
                        if (SegCross(p0, p1, nb.Pts[k], nb.Pts[k + 1], out float2 x))
                            nb.Crossings.Add(x);
                }
                ctx.Others.Add(nb);
            }
        }

        private static bool SegCross(float2 a0, float2 a1, float2 b0, float2 b1, out float2 x)
        {
            x = default;
            float2 r = a1 - a0, q = b1 - b0;
            float den = r.x * q.y - r.y * q.x;
            if (Math.Abs(den) < 1e-9f)
                return false;
            float2 w = b0 - a0;
            float t = (w.x * q.y - w.y * q.x) / den, u = (w.x * r.y - w.y * r.x) / den;
            if (t < 0f || t > 1f || u < 0f || u > 1f)
                return false;
            x = a0 + r * t;
            return true;
        }

        /// <summary>The fair-share caps at one sample. Each side stops at half the distance to the nearest centerline of another run that
        /// runs alongside on that side, minus half the pair gap, floored at MinHalfWidthM. Alongside: the two lines meet at under 45 degrees
        /// and no crossing of the two lies within FairCrossM. A related pair tapers its gap to MergeGapM over MergeTaperM at the ends.</summary>
        private void FairCaps(PisteCtx ctx, float2 p, float2 t, float2 nl, float hw, float sEnd, out float capL, out float capR)
        {
            capL = capR = hw;
            if (ctx.Others.Count == 0)
                return;
            float reach = 2f * hw + ctx.Gap;
            foreach (Neighbor nb in ctx.Others)
            {
                if (p.x < nb.Lo.x - reach || p.x > nb.Hi.x + reach || p.y < nb.Lo.y - reach || p.y > nb.Hi.y + reach)
                    continue;
                float best = float.PositiveInfinity;
                float2 q = default, tb = default;
                for (int k = 0; k + 1 < nb.Pts.Length; k++)
                {
                    float2 a = nb.Pts[k], d = nb.Pts[k + 1] - a;
                    float len2 = math.lengthsq(d);
                    float u = len2 > 0f ? math.saturate(math.dot(p - a, d) / len2) : 0f;
                    float2 c = a + d * u;
                    float dist = math.distancesq(p, c);
                    if (dist < best)
                    {
                        best = dist;
                        q = c;
                        tb = d;
                    }
                }
                float dd = math.sqrt(best);
                if (dd > reach || dd < 1e-3f)
                    continue;
                if (Math.Abs(math.dot(t, math.normalizesafe(tb))) < FairCos)
                    continue;   // not alongside: the lines meet at 45 degrees or more
                bool nearCross = false;
                foreach (float2 x in nb.Crossings)
                    if (math.distancesq(x, p) < FairCrossM * FairCrossM)
                    {
                        nearCross = true;
                        break;
                    }
                if (nearCross)
                    continue;
                float gap = nb.Related && sEnd < MergeTaperM ? MergeGapM + (nb.Gap - MergeGapM) * sEnd / MergeTaperM : nb.Gap;
                float lim = Math.Max(MinHalfWidthM, 0.5f * dd - 0.5f * gap);
                if (math.dot(q - p, nl) >= 0f)
                    capL = Math.Min(capL, lim);
                else
                    capR = Math.Min(capR, lim);
            }
        }

        private float MarchSide(PisteCtx ctx, float2 p, float2 dir, float maxHw, float sFrom, float sTo, List<int> j0, List<int> j1)
        {
            // The grade ceiling limits the lateral march only, and never under MinHalfWidthM: the planner vetted the centerline with
            // its own pitch rule, and the band the rater then shows is the honest one (iteration 2, item 2)
            float free = 0f;
            bool steep = false;
            for (float t = MarchStepM; t <= maxHw + WidthMarginM; t += MarchStepM)
            {
                if (!Cell(p + dir * t, out int c))
                    break;
                if ((m_Flags[c] & (FBlockAll | FBlockPiste)) != 0)
                    break;
                if ((m_Flags[c] & FNearEdge) != 0 && !InRegion(p + dir * t, EdgeBlockM))
                    break;   // iteration 2, item 5: the exact edge, so the ring check never finds a vertex outside
                if (!ctx.Graded && !(m_Grade[c] < ctx.Ceil))
                    steep = true;
                if (steep && t > MinHalfWidthM + WidthMarginM)
                    break;
                int N = m_OwnPiste[c];
                if (N >= 0 && m_DPiste[c] < GapAt(ctx, N, sFrom, sTo, j0, j1))
                    break;
                free = t;
            }
            return Math.Max(1f, Math.Min(maxHw, free - WidthMarginM));
        }

        /// <summary>Conservative smoothing (v1): each half width becomes the minimum of itself and its two neighbors.</summary>
        private static void Smooth(float[] hw)
        {
            int n = hw.Length;
            if (n == 0)
                return;
            float prev = hw[0];
            for (int i = 0; i < n; i++)
            {
                float cur = hw[i];
                float next = i + 1 < n ? hw[i + 1] : cur;
                hw[i] = Math.Min(cur, Math.Min(prev, next));
                prev = cur;
            }
        }

        /// <summary>5.2: a stretch with over PinchShare of its samples under PinchWidthM in total is cut at its longest pinched run, and the
        /// two parts are handled as stretches (their cut ends are no junctions).</summary>
        private static void PinchSplit(Piece pc, float[] hl, float[] hr, List<Piece> outList, float pinchM)
        {
            int n = pc.E - pc.S + 1, pinched = 0, bestA = -1, bestLen = 0;
            for (int i = 0; i < n; i++)
            {
                if (hl[i] + hr[i] >= pinchM)
                    continue;
                pinched++;
                if (i > 0 && hl[i - 1] + hr[i - 1] < pinchM)
                    continue;
                int k = i;
                while (k + 1 < n && hl[k + 1] + hr[k + 1] < pinchM)
                    k++;
                if (k - i + 1 > bestLen)
                {
                    bestLen = k - i + 1;
                    bestA = i;
                }
            }
            if (pinched <= PinchShare * n || bestA < 0)
            {
                outList.Add(pc);
                return;
            }
            int a = bestA, b = bestA + bestLen - 1;
            if (a > 0)
            {
                var left = new Piece { S = pc.S, E = pc.S + a - 1, Orig0 = pc.Orig0 };
                left.J0.AddRange(pc.J0);
                PinchSplit(left, Slice(hl, 0, a - 1), Slice(hr, 0, a - 1), outList, pinchM);
            }
            if (b < n - 1)
            {
                var right = new Piece { S = pc.S + b + 1, E = pc.E, Orig1 = pc.Orig1 };
                right.J1.AddRange(pc.J1);
                PinchSplit(right, Slice(hl, b + 1, n - 1), Slice(hr, b + 1, n - 1), outList, pinchM);
            }
        }

        private static float[] Slice(float[] a, int s, int e)
        {
            var o = new float[e - s + 1];
            Array.Copy(a, s, o, 0, o.Length);
            return o;
        }

        // ---------------------------------------------------------------- one piece: ring, extension, checks, rating, accept

        private AutoReject BuildPiece(PisteCtx ctx, GradePath path, Piece pc, int segment)
        {
            Elem e = ctx.E;
            Sub(path, pc.S, pc.E, out List<float2> xz);
            Built b0 = TryRing(ctx, xz, null, pc.J0, pc.J1);
            if (b0.Why != AutoReject.Kept)
            {
                if (b0.Why == AutoReject.Overlap && b0.Blocker >= 0)
                    m_RateWhy = $"by piste {m_E[m_P[b0.Blocker].Elem].Source}{(ctx.Partners.Contains(b0.Blocker) ? ", a partner" : "")}";
                return b0.Why;
            }
            // 5.4: a junction end farther than JunctionReachM from its partner's outline is extended toward it
            int ext0 = 0, ext1 = 0;
            string why0 = null, why1 = null;
            bool blocked0 = false, blocked1 = false;
            List<float2> ext = null;
            if (pc.J0.Count > 0 && EndMiss(b0, pc.J0, true, out int p0) > JunctionReachM)
            {
                ext = new List<float2>(xz);
                ext0 = Extend(ctx, ext, true, p0, out why0, out blocked0);
            }
            if (pc.J1.Count > 0 && EndMiss(b0, pc.J1, false, out int p1) > JunctionReachM)
            {
                if (ext == null)
                    ext = new List<float2>(xz);
                ext1 = Extend(ctx, ext, false, p1, out why1, out blocked1);
            }
            Built use = b0;
            if (ext0 + ext1 > 0)
            {
                var cap = new float[ext.Count];
                for (int i = 0; i < cap.Length; i++)
                    cap[i] = i < ext0 || i >= ext.Count - ext1 ? MinHalfWidthM : float.PositiveInfinity;
                Built b1 = TryRing(ctx, ext, cap, pc.J0, pc.J1);
                if (b1.Why == AutoReject.Kept)
                {
                    use = b1;
                    m_Rep.Extensions += (ext0 > 0 ? 1 : 0) + (ext1 > 0 ? 1 : 0);
                }
                else
                {
                    why0 = ext0 > 0 ? $"the extended ring failed ({b1.Why})" : why0;
                    why1 = ext1 > 0 ? $"the extended ring failed ({b1.Why})" : why1;
                    blocked0 = blocked1 = false;
                }
            }
            int idx = Accept(ctx, use, segment);
            Area a = m_P[idx];
            // The same piste continues past a cut: its kept segments are partners of its later pieces (merge gap, not the pair gap)
            ctx.Partners.Add(idx);
            // The ends that still miss are unlinked, with the reason
            if (pc.J0.Count > 0 && EndMiss(use, pc.J0, true, out _) > JunctionReachM)
                Unlinked(e, a, true, why0 ?? "no extension possible", blocked0);
            if (pc.J1.Count > 0 && EndMiss(use, pc.J1, false, out _) > JunctionReachM)
                Unlinked(e, a, false, why1 ?? "no extension possible", blocked1);
            a.J0 = pc.J0;
            a.J1 = pc.J1;
            a.Orig0 = pc.Orig0;
            a.Orig1 = pc.Orig1;
            return AutoReject.Kept;
        }

        // ---------------------------------------------------------------- iteration 3, item 2: piste ends reach a related track

        /// <summary>After the track pass: every planner relation of a kept piste end that is not a game link (the relation gate's set, the
        /// way through the cutting area included) gets one straight extension of that end toward the nearest realized centerline of the
        /// element, in SampleM steps, at most ExtendMaxM, until the end is within TrackReachM of its outline. The kept half widths stay,
        /// the extension is MinHalfWidthM each side. The extended ring goes through the exact check and the rater again. A blocked cell,
        /// the region and the gap of another piste stop it. A track never stops a piste.</summary>
        private void ReachPisteEnds()
        {
            for (int i = 0; i < m_P.Count; i++)
            {
                Area x = m_P[i];
                if (x.Ctx == null || x.Hl == null || x.Line.Length < 2)
                    continue;
                for (int s = 0; s < 2; s++)
                {
                    Area y = MissedRelation(x, s, out bool junction);
                    if (y == null || !ReachPiste(i, x, s == 0, y))
                        continue;
                    m_Rep.PisteReaches++;
                    int unl = s == 0 ? x.Unlinked0 : x.Unlinked1;
                    if (junction && unl != 0)
                    {
                        m_Rep.UnlinkedEnds--;
                        if (unl == 2)
                            m_Rep.UnlinkedBlocked--;
                        if (s == 0)
                            x.Unlinked0 = 0;
                        else
                            x.Unlinked1 = 0;
                    }
                }
            }
        }

        private Area AreaOf(Elem b, int k) => b.Type == ElemType.Track ? m_T[k] : m_P[k];

        /// <summary>The nearest kept area of a related element (a junction partner at this end, or a planner contact at the source end
        /// within RelationM + JunctionReachM) that is not a game link to x, not linked through the area that cut the end, and within
        /// TrackReachM + ExtendMaxM of the end. Null when there is none.</summary>
        private Area MissedRelation(Area x, int s, out bool junction)
        {
            junction = false;
            Elem a = m_E[x.Elem];
            List<int> js = s == 0 ? x.J0 : x.J1;
            float2 end = s == 0 ? x.Line[0] : x.Line[x.Line.Length - 1];
            var elems = new List<Elem>();
            var fromJ = new List<Elem>();
            foreach (int j in js)
            {
                Elem b = m_E[m_P[j].Elem];
                if (b != a && !elems.Contains(b))
                {
                    elems.Add(b);
                    fromJ.Add(b);
                }
            }
            if (s == 0 ? x.Orig0 : x.Orig1)
            {
                float2 endPt = s == 0 ? a.Pts[0] : a.Pts[a.Pts.Count - 1];
                foreach (Elem b in m_E)
                {
                    if (b == a || b.Areas.Count == 0 || elems.Contains(b) || !(s == 0 ? StartsOff(a, b) : EndsOnto(a, b)))
                        continue;
                    foreach (int k in b.Areas)
                        if (!AreaOf(b, k).Existing && PlanGeometry.PointRingDistance(AreaOf(b, k).Ring, endPt) <= RelationM + JunctionReachM)
                        {
                            elems.Add(b);
                            break;
                        }
                }
            }
            Area best = null;
            float bestD = TrackReachM + ExtendMaxM;
            foreach (Elem b in elems)
            {
                bool linked = false;
                Area near = null;
                float nd = float.PositiveInfinity;
                foreach (int k in b.Areas)
                {
                    Area y = AreaOf(b, k);
                    if (y.Existing)
                        continue;
                    if (LiftServedRule.OutlinesLinked(x.Ring, y.Ring))
                    {
                        linked = true;
                        break;
                    }
                    float d = PlanGeometry.PointRingDistance(y.Ring, end);
                    if (d < nd)
                    {
                        nd = d;
                        near = y;
                    }
                }
                if (linked || near == null || Via(x, js, b))
                    continue;
                if (nd <= bestD)
                {
                    bestD = nd;
                    best = near;
                    junction = fromJ.Contains(b);
                }
            }
            return best;
        }

        private bool ReachPiste(int self, Area x, bool start, Area y)
        {
            PisteCtx ctx = x.Ctx;
            float2[] line = x.Line;
            int n = line.Length;
            float2 end = start ? line[0] : line[n - 1], inner = start ? line[1] : line[n - 2];
            float2 aim = y.Line[0];
            float bd = float.PositiveInfinity;
            for (int k = 0; k + 1 < y.Line.Length; k++)
            {
                float2 a = y.Line[k], d = y.Line[k + 1] - a;
                float len2 = math.lengthsq(d);
                float2 c = a + d * (len2 > 0f ? math.saturate(math.dot(end - a, d) / len2) : 0f);
                float dd = math.distancesq(end, c);
                if (dd < bd)
                {
                    bd = dd;
                    aim = c;
                }
            }
            float2 dir = math.normalizesafe(aim - end);
            if (math.lengthsq(dir) < 0.5f || math.dot(dir, math.normalizesafe(end - inner)) < 0f)
                return ReachMiss(x, start, y, "the target lies behind the end");
            int yIdx = y.Track ? -1 : m_P.IndexOf(y);
            var add = new List<float2>();
            bool reached = false;
            for (float t = SampleM; t <= ExtendMaxM + 0.01f; t += SampleM)
            {
                float2 q = end + dir * t;
                if (!Cell(q, out int c))
                    return ReachMiss(x, start, y, "the map edge");
                byte f = m_Flags[c];
                if ((f & (FBlockAll | FBlockPiste)) != 0 || ((f & FNearEdge) != 0 && !InRegion(q, EdgeBlockM)))
                    return ReachMiss(x, start, y, $"a blocked cell after {t - SampleM:F0} m");
                int N = m_OwnPiste[c];
                if (N >= 0 && N != self && N != yIdx)
                {
                    float need = ctx.Partners.Contains(N) ? MergeGapM + MinHalfWidthM : PairGap(ctx.Gap, N);
                    if (m_DPiste[c] < need)
                        return ReachMiss(x, start, y, $"the gap of piste {m_E[m_P[N].Elem].Source} after {t - SampleM:F0} m");
                }
                add.Add(q);
                if (Linked(q, y))
                {
                    reached = true;
                    break;
                }
            }
            if (!reached)
                return ReachMiss(x, start, y, $"the {ExtendMaxM:F0} m cap");
            int m = add.Count;
            var xz = new List<float2>(n + m);
            var hl = new float[n + m];
            var hr = new float[n + m];
            if (start)
            {
                for (int k = m - 1; k >= 0; k--)
                    xz.Add(add[k]);
                xz.AddRange(line);
            }
            else
            {
                xz.AddRange(line);
                xz.AddRange(add);
            }
            int off = start ? m : 0;
            for (int k = 0; k < n + m; k++)
            {
                int o = k - off;
                bool ext = o < 0 || o >= n;
                hl[k] = ext ? MinHalfWidthM : x.Hl[o];
                hr[k] = ext ? MinHalfWidthM : x.Hr[o];
            }
            var b = new Built { Line = xz.ToArray(), Hl = hl, Hr = hr };
            if (!MakeRing(xz, hl, hr, b))
                return ReachMiss(x, start, y, "the extended ring crosses itself");
            AutoReject why = CheckPiste(ctx, b.Ring, out int blocker, self);
            // The extension converges on a piste it has no relation with: that piste becomes a partner (as item 1), one more check
            if (why == AutoReject.Overlap && blocker >= 0 && blocker != yIdx && ctx.Partners.Add(blocker))
            {
                int first = blocker;
                why = CheckPiste(ctx, b.Ring, out blocker, self);
                if (why == AutoReject.Kept)
                    m_Rep.ConvergeJunctions++;
                else
                    ctx.Partners.Remove(first);
            }
            if (why != AutoReject.Kept)
                return ReachMiss(x, start, y, $"the extended ring failed ({why}{(blocker >= 0 ? " by piste " + m_E[m_P[blocker].Elem].Source : "")})");
            if (!RateRing(b, x.Graded))
                return ReachMiss(x, start, y, $"the extended ring is not rated ({m_RateWhy})");
            Count(x, -1);
            x.Ring = b.Ring;
            x.Nodes = b.Nodes;
            x.Line = b.Line;
            x.Hl = hl;
            x.Hr = hr;
            x.LengthM = b.LengthM;
            x.AreaM2 = (float)PlanGeometry.PolygonArea(b.Ring);
            x.TopY = b.TopY;
            x.BottomY = b.BottomY;
            x.DropM = b.Drop;
            x.RatingGrade = b.RatingGrade;
            x.MeanGrade = b.MeanGrade;
            x.SteepestGrade = b.Steepest;
            x.Band = b.Band;
            x.EndY = H(b.Line[b.Line.Length - 1]);
            PlanGeometry.BBox(x.Ring, out x.Lo, out x.Hi);
            Count(x, 1);
            Elem e = m_E[x.Elem];
            m_Rep.Lines.Add($"piste {e.Source} ({e.Kind}) segment {x.Segment}: {(start ? "start" : "end")} extended {m * SampleM:F0} m to reach {(y.Track ? "track" : "piste")} {m_E[y.Elem].Source}");
            return true;
        }

        private bool ReachMiss(Area x, bool start, Area y, string why)
        {
            m_Rep.Lines.Add($"piste {m_E[x.Elem].Source} ({m_E[x.Elem].Kind}) segment {x.Segment}: {(start ? "start" : "end")} cannot reach {(y.Track ? "track" : "piste")} {m_E[y.Elem].Source}: {why}");
            return false;
        }

        /// <summary>Adds (sign 1) or removes (sign -1) a kept piste area from the km and band counts of the report.</summary>
        private void Count(Area a, int sign)
        {
            if (a.Graded)
            {
                m_Rep.GradedKmKept += sign * a.LengthM / 1000.0;
                m_Rep.GradedShown[(int)a.Band] += sign;
            }
            else
            {
                m_Rep.PisteKmKept += sign * a.LengthM / 1000.0;
                m_Rep.BandTable[(int)a.Planned, (int)a.Band] += sign;
                m_Rep.BandKm[(int)a.Planned, (int)a.Band] += sign * a.LengthM / 1000.0;
            }
        }

        private void Unlinked(Elem e, Area a, bool start, string why, bool blocked)
        {
            m_Rep.UnlinkedEnds++;
            if (blocked)
                m_Rep.UnlinkedBlocked++;
            if (start)
                a.Unlinked0 = blocked ? 2 : 1;
            else
                a.Unlinked1 = blocked ? 2 : 1;
            float2 p = start ? a.Line[0] : a.Line[a.Line.Length - 1];
            m_Rep.Lines.Add($"piste {e.Source} ({e.Kind}) segment {a.Segment}: {(start ? "start" : "end")} at ({p.x:F0}, {p.y:F0}) unlinked: {why}");
        }

        /// <summary>The distance from a ring end's two corner nodes to the nearest junction partner's outline (partner = its index).</summary>
        private float EndMiss(Built b, List<int> js, bool start, out int partner)
        {
            int n = b.Ring.Length, L = b.LeftCount;
            float2 c0 = start ? b.Ring[0] : b.Ring[L - 1], c1 = start ? b.Ring[n - 1] : b.Ring[L];
            float2 mid = start ? b.Line[0] : b.Line[b.Line.Length - 1];
            partner = -1;
            float best = float.PositiveInfinity, bestMid = float.PositiveInfinity;
            foreach (int j in js)
            {
                float[] dd = { PlanGeometry.PointRingDistance(m_P[j].Ring, c0), PlanGeometry.PointRingDistance(m_P[j].Ring, c1) };
                float d = Math.Min(dd[0], dd[1]);
                float dm = PlanGeometry.PointRingDistance(m_P[j].Ring, mid);
                if (dm < bestMid)
                {
                    bestMid = dm;
                    partner = j;
                }
                best = Math.Min(best, d);
            }
            return best;
        }

        /// <summary>5.4: extends the centerline straight from its end toward the nearest point of the partner's outline in SampleM steps,
        /// until the centerline is MergeGapM + MinHalfWidthM from that outline, at most ExtendMaxM. It stops at a blocked cell or another
        /// piste's gap. Returns the samples added (inserted at the front for the start end).</summary>
        private int Extend(PisteCtx ctx, List<float2> xz, bool start, int partner, out string why, out bool blocked)
        {
            why = null;
            blocked = false;
            float2[] ring = m_P[partner].Ring;
            float2 end = start ? xz[0] : xz[xz.Count - 1];
            float2 q = Nearest(ring, end);
            float2 dir = math.normalizesafe(q - end);
            if (!math.any(dir != 0f))
            {
                why = "no direction to the partner";
                return 0;
            }
            var add = new List<float2>();
            bool reached = false;
            for (float t = SampleM; t <= ExtendMaxM + 0.01f; t += SampleM)
            {
                float2 p = end + dir * t;
                if (!Cell(p, out int c) || (m_Flags[c] & (FBlockAll | FBlockPiste)) != 0)
                {
                    why = "a blocked cell stopped the extension";
                    blocked = true;
                    break;
                }
                int N = m_OwnPiste[c];
                if (N >= 0 && N != partner)
                {
                    float need = ctx.Partners.Contains(N) ? MergeGapM + MinHalfWidthM : PairGap(ctx.Gap, N);
                    if (m_DPiste[c] < need)
                    {
                        why = "another piste's gap stopped the extension";
                        break;
                    }
                }
                add.Add(p);
                if (PlanGeometry.PointRingDistance(ring, p) <= MergeGapM + MinHalfWidthM)
                {
                    reached = true;
                    break;
                }
            }
            if (!reached && why == null)
                why = $"the {ExtendMaxM:F0} m cap";
            if (start)
            {
                add.Reverse();
                xz.InsertRange(0, add);
            }
            else
                xz.AddRange(add);
            return add.Count;
        }

        private static float2 Nearest(float2[] ring, float2 p)
        {
            float best = float.PositiveInfinity;
            float2 q = p;
            for (int i = 0, n = ring.Length; i < n; i++)
            {
                float2 a = ring[i], b = ring[(i + 1) % n];
                float d = GradePath.SegDistance(p, a, b, out float u);
                if (d < best)
                {
                    best = d;
                    q = math.lerp(a, b, u);
                }
            }
            return q;
        }

        /// <summary>March, ring, exact check and rating at full width, then once at RetryShare of the half widths (5.5).</summary>
        private Built TryRing(PisteCtx ctx, List<float2> xz, float[] cap, List<int> j0, List<int> j1)
        {
            Built b = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                March(ctx, xz, cap, j0, j1, attempt == 0 ? 1f : RetryShare, out float[] hl, out float[] hr);
                b = new Built { Line = xz.ToArray(), Hl = hl, Hr = hr };
                if (!MakeRing(xz, hl, hr, b))
                {
                    b.Why = AutoReject.Crosses;
                    continue;
                }
                b.Why = CheckPiste(ctx, b.Ring, out b.Blocker, -1);
                if (b.Why != AutoReject.Kept)
                    continue;
                if (!RateRing(b, ctx.Graded))
                {
                    b.Why = AutoReject.Unrated;
                    continue;
                }
                return b;
            }
            // Iteration 2, item 4: a ring that still crosses itself gets one more try as a plain constant-width ribbon (MinHalfWidthM each
            // side, the same edge pipeline as GradeRibbon.Build, so the end corners stay known)
            if (b != null && b.Why == AutoReject.Crosses && xz.Count >= 2)
            {
                var hw = new float[xz.Count];
                for (int i = 0; i < hw.Length; i++)
                    hw[i] = MinHalfWidthM;
                var c = new Built { Line = xz.ToArray(), Hl = hw, Hr = (float[])hw.Clone() };
                if (MakeRing(xz, hw, hw, c))
                {
                    c.Why = CheckPiste(ctx, c.Ring, out c.Blocker, -1);
                    if (c.Why == AutoReject.Kept && !RateRing(c, ctx.Graded))
                        c.Why = AutoReject.Unrated;
                    if (c.Why == AutoReject.Kept)
                        m_Rep.CrossesFallback++;
                    return c;
                }
            }
            return b;
        }

        /// <summary>The ring: both edges through GradeRibbon.CutLoops, Simplify (1 m), Thin, then GradeRibbon.Clear (v2's BuildRing tail).</summary>
        private static bool MakeRing(List<float2> xz, float[] hl, float[] hr, Built b)
        {
            int n = xz.Count;
            if (n < 2)
                return false;
            var left = new List<float2>(n);
            var right = new List<float2>(n);
            float maxHw = 0f, len = 0f;
            for (int i = 0; i < n; i++)
            {
                float2 t = math.normalizesafe(xz[Math.Min(i + 1, n - 1)] - xz[Math.Max(i - 1, 0)], new float2(1f, 0f));
                float2 nl = new float2(-t.y, t.x);
                left.Add(xz[i] + nl * hl[i]);
                right.Add(xz[i] - nl * hr[i]);
                maxHw = Math.Max(maxHw, Math.Max(hl[i], hr[i]));
                if (i > 0)
                    len += math.distance(xz[i - 1], xz[i]);
            }
            float step = Math.Max(0.1f, len / Math.Max(1, n - 1));
            int window = (int)math.ceil((4f * maxHw + 20f) / step);
            List<float2> l = GradeRibbon.Thin(GradeRibbon.Simplify(GradeRibbon.CutLoops(left, window), RingSimplifyM), GradeRibbon.MinSpacingM);
            List<float2> r = GradeRibbon.Thin(GradeRibbon.Simplify(GradeRibbon.CutLoops(right, window), RingSimplifyM), GradeRibbon.MinSpacingM);
            if (l.Count < 2 || r.Count < 2)
                return false;
            var ring = new float2[l.Count + r.Count];
            for (int i = 0; i < l.Count; i++)
                ring[i] = l[i];
            for (int i = 0; i < r.Count; i++)
                ring[l.Count + i] = r[r.Count - 1 - i];
            if (!GradeRibbon.Clear(ring, GradeRibbon.MinClearanceM))
                return false;
            b.Ring = ring;
            b.LeftCount = l.Count;
            b.LengthM = len;
            return true;
        }

        /// <summary>The region part of the exact check: every vertex inside the region and the map margin, and RegionClearanceM from its
        /// edges.</summary>
        private bool RegionOk(float2[] ring)
        {
            HeightSnapshot snap = m_Snap;
            for (int i = 0; i < ring.Length; i++)
            {
                float2 p = ring[i];
                if (!GradeRaster.Inside(m_Region, p))
                    return false;
                float hx = (p.x + snap.Offset.x) * snap.Scale.x, hz = (p.y + snap.Offset.z) * snap.Scale.z;
                if (hx < OffMapMarginTexels || hz < OffMapMarginTexels || hx > snap.ResX - 1 - OffMapMarginTexels || hz > snap.ResZ - 1 - OffMapMarginTexels)
                    return false;
            }
            return !(PlanGeometry.EdgeDistance(ring, m_Region, RegionClearanceM, out _, out _, out _, out _) < RegionClearanceM);
        }

        /// <summary>5.5, after v2's CheckRing: region, obstacles by the type rule (cat tracks never block a piste), and ExactGapShare of the
        /// gap from every accepted piste (of MergeGapM from each partner).</summary>
        private AutoReject CheckPiste(PisteCtx ctx, float2[] ring, out int blocker, int self)
        {
            blocker = -1;
            if (!RegionOk(ring))
                return AutoReject.Region;
            if (!PlanGeometry.ObstaclesClear(ring, m_In.Obstacles, m_Density))
                return AutoReject.Obstacle;
            PlanGeometry.BBox(ring, out float2 lo, out float2 hi);
            for (int a = 0; a < m_P.Count; a++)
            {
                if (a == self)
                    continue;
                Area acc = m_P[a];
                float need = ctx.Partners.Contains(a) ? ExactGapShare * MergeGapM : ExactGapShare * PairGap(ctx.Gap, a);
                if (math.any(acc.Hi < lo - need) || math.any(acc.Lo > hi + need))
                    continue;
                if (PlanGeometry.RingDistance(ring, acc.Ring, need, out _, out _, out _, out _) < need)
                {
                    blocker = a;
                    return AutoReject.Overlap;
                }
            }
            return AutoReject.Kept;
        }

        /// <summary>SkiRunRater.Rate on the ring's nodes at snapshot heights. Rated (status Ok, no sampler miss) or not. A mask that is
        /// not connected is a rating: the classifier falls back to its P90 band (SkiSlopeClassifier SlopeResult.Connected, Band), and the
        /// game shows that band. Such a piece is kept and counted as rated by the fallback.</summary>
        private bool RateRing(Built b, bool graded)
        {
            int n = b.Ring.Length;
            var nodes = new float3[n];
            for (int i = 0; i < n; i++)
                nodes[i] = At(b.Ring[i]);
            var sampler = new SnapshotSampler(m_Snap);
            RateStatus rs = SkiRunRater.Rate(nodes, n, m_Snap.Origin, ref sampler, m_Snap.TexelM, 0, m_RateGrid, null, m_RateRes,
                out int _, out float _, out float _);
            SlopeResult res = m_RateRes;
            // A graded piste crosses the fall line: the rater finds too few full 20 m windows across 15 m and answers ShortRun, with a
            // band the game shows (SkiSlopeClassifier treats ShortRun as rated). Plain pistes need Ok, as v2.
            bool statusOk = res.Status == SlopeStatus.Ok || (graded && res.Status == SlopeStatus.ShortRun);
            if (rs != RateStatus.Rated || !statusOk || sampler.Misses > 0)
            {
                m_RateWhy = b.RateWhy = $"{rs} {res.Status}{(res.Connected ? "" : " not connected")}{(sampler.Misses > 0 ? " sampler misses" : "")}";
                return false;
            }
            b.Nodes = nodes;
            b.Fallback = !res.Connected;
            b.Band = res.Band;
            b.RatingGrade = res.RatingGrade;
            b.MeanGrade = res.MeanGrade;
            b.Steepest = res.SteepestGrade;
            b.Drop = res.Drop;
            b.TopY = res.MaxHeight;
            b.BottomY = res.MinHeight;
            return true;
        }

        private int Accept(PisteCtx ctx, Built b, int segment)
        {
            if (b.Fallback)
                m_Rep.FallbackRated++;
            Elem e = ctx.E;
            var a = new Area
            {
                Elem = e.Id, Segment = segment, Graded = ctx.Graded, Ring = b.Ring, Nodes = b.Nodes, Line = b.Line, GapM = ctx.Gap,
                LengthM = b.LengthM, AreaM2 = (float)PlanGeometry.PolygonArea(b.Ring), TopY = b.TopY, BottomY = b.BottomY, DropM = b.Drop,
                RatingGrade = b.RatingGrade, MeanGrade = b.MeanGrade, SteepestGrade = b.Steepest, Band = b.Band, Planned = e.Planned,
                EndY = H(b.Line[b.Line.Length - 1]), Hl = b.Hl, Hr = b.Hr, Ctx = ctx,
            };
            PlanGeometry.BBox(a.Ring, out a.Lo, out a.Hi);
            int idx = m_P.Count;
            m_P.Add(a);
            e.Areas.Add(idx);
            SeedArea(m_DPiste, m_OwnPiste, a.Ring, idx, PlanGeometry.SparseGapM + 0.5f * PlanGeometry.TargetWidthM[0] + MinHalfWidthM + WidthMarginM);
            if (ctx.Graded)
            {
                m_Rep.GradedPisteAreas++;
                m_Rep.GradedKmKept += a.LengthM / 1000.0;
                m_Rep.GradedShown[(int)a.Band]++;
            }
            else
            {
                m_Rep.PisteAreas++;
                m_Rep.PisteKmKept += a.LengthM / 1000.0;
                m_Rep.BandTable[(int)a.Planned, (int)a.Band]++;
                m_Rep.BandKm[(int)a.Planned, (int)a.Band] += a.LengthM / 1000.0;
            }
            return idx;
        }
    }
}
