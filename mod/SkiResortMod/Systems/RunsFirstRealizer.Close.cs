// Runs-first realizer: closed ends (post-release round 1, T172 and T176). The passes before this one stop an end as soon as the game
// links it: a piste end within JunctionReachM (20 m) of the piste it joins, a track piece TrackFreeM (7 m) from the track that cuts it,
// a reached end within TrackReachM (20 m). The game's link rule (LiftServedRule.LinkMeters, 25 m) reads those as joined. A player sees
// runs that end before they connect and cat tracks in pieces.
// This pass runs last, on the accepted outlines. Each end cap (the outline edge across a centerline end) is moved forward along the
// end's direction until it lies on the outline it meets: CloseGapM from an outline of its own kind (two areas of one kind may not
// touch), just inside an outline of the other kind (a piste and a cat track may overlap). The cap gets a node every CloseNodeM, so it
// follows a curved edge. Only the cap moves: the centerline, the half widths and every other node stay.
// A piste closes onto its partners only (the pistes the planner relates to it, its own segments, converging lines): two unrelated
// pistes keep the density's gap. A piste closes onto any track, a track onto any piste or track.
// The closed outline passes the same exact checks as the first one (region, obstacles, gaps, MinClearanceM), and a piste must keep
// its band. An end that fails keeps its outline. Pure and deterministic.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstRealizer
    {
        // A closed cap's distance to an outline of its own kind. The exact check needs ExactGapShare x MergeGapM (1.6 m) on the whole cap:
        // two nodes CloseNodeM apart, each over CloseGapM - CloseStepM from a corner of the other outline, keep their edge 2.1 m from it
        public const float CloseGapM = MergeGapM + 1f;
        public const float CloseMaxM = ExtendMaxM;         // the farthest a cap node moves
        public const float CloseStepM = 0.5f;              // the smallest step of a cap node
        public const float CloseNodeM = 2.5f;              // the spacing of the nodes across a cap
        private const float CloseCoarseM = 5f;
        private const float CapOnEndM = 0.75f, CapSquareCos = 0.5f;   // the cap edge passes this near the centerline end, across its direction

        private struct CloseTarget
        {
            public Area A;
            public bool Same;   // the same kind as the area that closes (a gap), else the other kind (an overlap)
        }

        private void CloseEnds()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                List<Area> list = pass == 0 ? m_P : m_T;
                for (int i = 0; i < list.Count; i++)
                {
                    Area x = list[i];
                    if (x.Existing || x.Line == null || x.Line.Length < 2 || x.Ring == null)
                        continue;
                    if (Canceled)
                        return;
                    for (int s = 0; s < 2; s++)
                        CloseEnd(x, i, s == 0);
                }
            }
        }

        /// <summary>The outline edge across one centerline end (its index: ring[k] to ring[k + 1]), or -1.</summary>
        private static int CapEdge(float2[] ring, float2 end, float2 dir)
        {
            int cap = -1;
            float best = CapOnEndM;
            for (int k = 0, n = ring.Length; k < n; k++)
            {
                float2 a = ring[k], b = ring[(k + 1) % n];
                float d = GradePath.SegDistance(end, a, b, out float _);
                if (d < best && Math.Abs(math.dot(math.normalizesafe(b - a), dir)) < CapSquareCos)
                {
                    best = d;
                    cap = k;
                }
            }
            return cap;
        }

        private void CloseEnd(Area x, int self, bool start)
        {
            float2[] line = x.Line, ring = x.Ring;
            int n = line.Length, rn = ring.Length;
            float2 end = start ? line[0] : line[n - 1];
            float2 dir = math.normalizesafe(end - (start ? line[1] : line[n - 2]));
            if (math.lengthsq(dir) < 0.5f)
                return;
            int cap = CapEdge(ring, end, dir);
            if (cap < 0)
                return;
            float2 c0 = ring[cap], c1 = ring[(cap + 1) % rn];
            // What this end may close onto, in reach of the cap as it moves
            float2 lo = math.min(math.min(c0, c1), math.min(c0, c1) + dir * CloseMaxM) - CloseGapM;
            float2 hi = math.max(math.max(c0, c1), math.max(c0, c1) + dir * CloseMaxM) + CloseGapM;
            var targets = new List<CloseTarget>();
            for (int j = 0; j < m_P.Count; j++)
            {
                Area y = m_P[j];
                if (y == x || math.any(y.Hi < lo) || math.any(y.Lo > hi))
                    continue;
                if (x.Track)
                    targets.Add(new CloseTarget { A = y, Same = false });
                else if ((x.Ctx != null && x.Ctx.Partners.Contains(j)) || (y.Ctx != null && y.Ctx.Partners.Contains(self)))
                    targets.Add(new CloseTarget { A = y, Same = true });
            }
            foreach (Area y in m_T)
                if (y != x && y.Ring != null && !(math.any(y.Hi < lo) || math.any(y.Lo > hi)))
                    targets.Add(new CloseTarget { A = y, Same = x.Track });
            if (targets.Count == 0)
                return;
            // Each cap node moves along the end's direction until it meets a target
            int K = Math.Max(1, (int)Math.Ceiling(math.distance(c0, c1) / CloseNodeM));
            var slide = new float[K + 1];
            var met = new bool[K + 1];
            Area onto = null;
            float most = 0f;
            for (int k = 0; k <= K; k++)
            {
                slide[k] = Slide(x, self, math.lerp(c0, c1, k / (float)K), dir, targets, out met[k], out Area hit);
                if (slide[k] > most)
                {
                    most = slide[k];
                    onto = hit;
                }
            }
            if (most <= 0f)
                return;
            // A node that met nothing takes the move of the nearest node that did: the cap stays one edge, with no spike
            var follow = (float[])slide.Clone();
            for (int k = 0; k <= K; k++)
            {
                if (met[k])
                    continue;
                for (int d = 1; d <= K; d++)
                {
                    int a = k - d, b = k + d;
                    if (a >= 0 && met[a])
                    {
                        follow[k] = slide[a];
                        break;
                    }
                    if (b <= K && met[b])
                    {
                        follow[k] = slide[b];
                        break;
                    }
                }
            }
            Elem e = m_E[x.Elem];
            string what = $"{(x.Track ? "track" : "piste")} {e.Source} ({e.Kind}) segment {x.Segment}: {(start ? "start" : "end")}";
            string why = null;
            // the straight cap: every node by the smallest move that met something (the cap stays square and stops at first contact)
            var square = new float[K + 1];
            float least = float.PositiveInfinity;
            for (int k = 0; k <= K; k++)
                if (met[k] && slide[k] > 0f)
                    least = Math.Min(least, slide[k]);
            bool touches = false;
            for (int k = 0; k <= K; k++)
                touches |= met[k] && slide[k] <= 0f;
            for (int k = 0; k <= K; k++)
                square[k] = touches || float.IsPositiveInfinity(least) ? 0f : least;
            foreach (float[] s in new[] { follow, slide, square })
            {
                bool moves = false;
                foreach (float v in s)
                    moves |= v > 0f;
                if (!moves)
                    continue;
                float2[] closed = CapRing(ring, cap, c0, c1, dir, s);
                why = x.Track ? ApplyTrackRing(x, self, closed) : ApplyPisteRing(x, self, closed);
                if (why == null)
                    break;
            }
            if (why != null)
            {
                m_Rep.CloseFailed++;
                m_Rep.Lines.Add($"{what} not closed onto {(onto.Track ? "track" : "piste")} {(onto.Elem >= 0 ? m_E[onto.Elem].Source.ToString() : "(existing)")}: {why}");
                return;
            }
            if (x.Track)
                m_Rep.TrackEndsClosed++;
            else
                m_Rep.PisteEndsClosed++;
            m_Rep.Lines.Add($"{what} closed onto {(onto.Track ? "track" : "piste")} {(onto.Elem >= 0 ? m_E[onto.Elem].Source.ToString() : "(existing)")}, the cap moved {most:F0} m");
        }

        /// <summary>How far a cap node moves from q along dir: to the first point CloseGapM from a target of its own kind, or just inside a
        /// target of the other kind. 0 when it is there already, when a cell on the way is closed to this area, or when CloseMaxM comes
        /// first (met false). The step is the distance still free (the distance to an outline cannot shrink faster than the node
        /// moves), never under CloseStepM.</summary>
        private float Slide(Area x, int self, float2 q, float2 dir, List<CloseTarget> targets, out bool met, out Area hit)
        {
            met = false;
            hit = null;
            float s = 0f;
            while (s <= CloseMaxM)
            {
                float2 p = q + dir * s;
                if (s > 0f && !OpenFor(x, self, p))
                    return 0f;
                float free = float.PositiveInfinity, reach = CloseMaxM - s + CloseGapM;
                foreach (CloseTarget t in targets)
                {
                    Area y = t.A;
                    if (math.any(p < y.Lo - reach) || math.any(p > y.Hi + reach))
                        continue;
                    float d = GradeRaster.Inside(y.Ring, p) ? 0f : PlanGeometry.PointRingDistance(y.Ring, p);
                    float left = t.Same ? d - CloseGapM : d;
                    if (left < free)
                    {
                        free = left;
                        hit = y;
                    }
                }
                if (free <= 0f)
                {
                    met = true;
                    return s;
                }
                s += float.IsPositiveInfinity(free) ? CloseCoarseM : Math.Max(CloseStepM, free);   // nothing in reach yet: a coarse step
            }
            hit = null;
            return 0f;
        }

        /// <summary>A cell a closing cap may enter: in the region, not blocked for this kind, and (a piste) outside the pair gap of
        /// every piste that is not its partner.</summary>
        private bool OpenFor(Area x, int self, float2 p)
        {
            if (x.Track)
            {
                byte cause = FreeTrack(p, out int _);
                return cause != CRegion && cause != CObstacle;
            }
            if (!Cell(p, out int c))
                return false;
            byte f = m_Flags[c];
            if ((f & (FBlockHard | FBlockPiste)) != 0 || !RegionOpen(f, p, EdgeBlockM))
                return false;
            int N = m_OwnPiste[c];
            if (N < 0 || N == self || x.Ctx == null || x.Ctx.Partners.Contains(N) || (m_P[N].Ctx != null && m_P[N].Ctx.Partners.Contains(self)))
                return true;
            return m_DPiste[c] >= PairGap(x.Ctx.Gap, N);
        }

        /// <summary>The ring with its cap edge (ring[cap] to ring[cap + 1]) replaced by the moved cap: both corners and the nodes
        /// between them, each moved by its slide along dir. Nodes closer than GradeRibbon.MinSpacingM to the node before are left out.
        /// The ring keeps its first node (a cap on the closing edge appends its inner nodes).</summary>
        private static float2[] CapRing(float2[] ring, int cap, float2 c0, float2 c1, float2 dir, float[] slide)
        {
            int rn = ring.Length, K = slide.Length - 1;
            var nodes = new List<float2> { c0 + dir * slide[0] };
            float min2 = GradeRibbon.MinSpacingM * GradeRibbon.MinSpacingM;
            float2 last = c1 + dir * slide[K];
            for (int k = 1; k < K; k++)
            {
                float2 p = math.lerp(c0, c1, k / (float)K) + dir * slide[k];
                if (math.distancesq(p, nodes[nodes.Count - 1]) >= min2 && math.distancesq(p, last) >= min2)
                    nodes.Add(p);
            }
            nodes.Add(last);
            var o = new List<float2>(rn + nodes.Count);
            if (cap == rn - 1)
            {
                o.Add(last);   // ring[0] is the cap's second corner
                for (int k = 1; k < rn - 1; k++)
                    o.Add(ring[k]);
                for (int k = 0; k < nodes.Count - 1; k++)
                    o.Add(nodes[k]);
            }
            else
            {
                for (int k = 0; k < cap; k++)
                    o.Add(ring[k]);
                o.AddRange(nodes);
                for (int k = cap + 2; k < rn; k++)
                    o.Add(ring[k]);
            }
            return o.ToArray();
        }

        /// <summary>A piste takes the closed ring when it passes the exact check and the rater gives it the band it had. Null = taken,
        /// else why not.</summary>
        private string ApplyPisteRing(Area x, int self, float2[] ring)
        {
            if (x.Ctx == null)
                return "no context";
            if (!GradeRibbon.Clear(ring, GradeRibbon.MinClearanceM))
                return "the closed ring crosses itself";
            AutoReject why = CheckPiste(x.Ctx, ring, out int blocker, self);
            if (why != AutoReject.Kept)
                return $"the closed ring failed ({why}{(blocker >= 0 ? " by piste " + m_E[m_P[blocker].Elem].Source : "")})";
            var b = new Built { Ring = ring, Line = x.Line, Hl = x.Hl, Hr = x.Hr, LengthM = x.LengthM };
            if (!RateRing(b, x.Graded))
                return $"the closed ring is not rated ({m_RateWhy})";
            if (b.Band != x.Band)
                return $"the closed ring changes the band ({x.Band} to {b.Band})";
            x.Ring = b.Ring;
            x.Nodes = b.Nodes;
            x.AreaM2 = (float)PlanGeometry.PolygonArea(b.Ring);
            x.TopY = b.TopY;
            x.BottomY = b.BottomY;
            x.DropM = b.Drop;
            x.RatingGrade = b.RatingGrade;
            x.MeanGrade = b.MeanGrade;
            x.SteepestGrade = b.Steepest;
            PlanGeometry.BBox(x.Ring, out x.Lo, out x.Hi);
            return null;
        }

        /// <summary>A track takes the closed ring when it passes the track checks. Null = taken, else why not.</summary>
        private string ApplyTrackRing(Area x, int self, float2[] ring)
        {
            if (!GradeRibbon.Clear(ring, GradeRibbon.MinClearanceM))
                return "the closed ring crosses itself";
            AutoReject why = CheckTrack(ring, self);
            if (why != AutoReject.Kept)
                return $"the closed ring failed ({why})";
            var nodes = new float3[ring.Length];
            float top = float.NegativeInfinity, bot = float.PositiveInfinity;
            for (int i = 0; i < ring.Length; i++)
            {
                nodes[i] = At(ring[i]);
                top = Math.Max(top, nodes[i].y);
                bot = Math.Min(bot, nodes[i].y);
            }
            x.Ring = ring;
            x.Nodes = nodes;
            x.AreaM2 = (float)PlanGeometry.PolygonArea(ring);
            x.TopY = top;
            x.BottomY = bot;
            PlanGeometry.BBox(ring, out x.Lo, out x.Hi);
            return null;
        }
    }
}
