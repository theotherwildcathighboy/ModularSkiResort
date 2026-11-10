// RunsFirst scorecard: judges a resort plan from its geometry alone (runs, connectors, lifts) on the raw heightmap, independent of how
// the planner got there. The planner hands over a ScorePlan. The scorecard trusts nothing the plan says about itself (bands, served
// lines, credits).
//
// Skiing model: a skier starts at a lift top and takes any element (run or connector) starting within StartM of it. The skier may
// continue onto any element that the current element ends on (or that branches off it) within JoinM. An element that passes within
// StationM of a lift bottom delivers the skier to that lift. A run is served when a lift top reaches it AND it delivers to some lift
// bottom. Lift A links to lift B when a skier leaving A's top reaches B's bottom without riding another lift. The network share is the
// largest strongly connected set of lifts over all lifts (1 = every lift can get to every other one and back). A gondola or tram also
// carries skiers down: an element ending within StationM of its top delivers to it.
//
// v2 (after the 2026-10-29 contest. All four entries flagged the same faults):
// - crowding no longer counts the junction zone of two runs (within JunctionM of the other run's start or end: a shared top, a branch,
//   a merge)
// - a lift's own green run (from its top back to its bottom) may run under its chord, as a learning chair does
// - a base is a cluster of two or more lift bottoms (or the lowest cluster when none has two), so a lone low station no longer demands
//   its own learning zone
//
// v3 (the owner's rules, 2026-10-29): a run is rated by the ground under it, as the game's classifier rates a painted run (85th
// percentile of the terrain grade along it). A connector is a graded cat track (built with the grading tool, a bench across the
// face). The scorecard rates it by its own grade along the path: up to the blue line it is a cat track (green). Steeper, it is a diagonal
// run (blue or harder) and counts as a run, not as connector km. Lifts have no maximum length or rise (the mod's lift tool has none).
// Minimums and the mean-grade cap stay. A gondola or tram may cross a chair (pending an in-game check). Riding down is only on a
// gondola or tram. Beginner terrain may be anywhere a beginner gets to from the base. A beginner gets there up lifts whose tops have
// green skiing, or on a gondola or tram both ways (Palisades: the learning area at mid-mountain). Every lift needs an easy way home: a
// green or blue route from its top to a base lift's bottom, or to a gondola or tram top to ride down (a gondola or tram is its own
// way home).
//
// v4 (the owner's review of round v3, 2026-10-29): an experts-only lift is normal, so not every lift needs an easy way home. A resort
// strives for an intermediate way down from at least one of its peaks, but a steep area (Snowbird, Alta) may not have one. The way-home
// term is now the share of the resort's vertical (lowest base to highest lift top) from which a skier gets home on green or blue
// (skiing to a base lift's bottom, or to a gondola / tram top to ride down), at weight 5. New layout term (10), for what a designer
// sees and the other terms do not:
// - crowded terminals (more than 4 lift terminals within 300 m, 6 at a base, and two bottoms or two tops on one spot)
// - parallel lift lines (same direction within 20 degrees, 250 m apart, overlapping 40 % of the longer line)
// - zigzag cat tracks (60-degree turns between 20 m legs, past 4 per km of green cat track plus 2)
//
// v5 (the owner's rulings on round v4, the designer critique and the real ski-area comparison, 2026-10-29): beginner terrain counts
// green runs only (not cat tracks or graded tracks), by the km a beginner reaches from the base lifts' tops (full at 0.8 km, half at
// 0.3 km). A beginner chair (new lift type: a fixed-grip chair from 30 m of rise and 150 m of length, up to 150 m of rise) whose top
// has a green run adds a quarter. A run is groomed or natural (the mod's RunKind.Natural, ungroomed). A natural run must be black or
// double black by its ground, and counts half in coverage. Fleet replaces served vertical (it read 1.00 everywhere). Gondolas past
// 1 + 1 per 6 km2 cost (a tram, and a gondola past 700 m of rise, is a trunk line and never counts). So do thin lifts. A thin lift
// meets these conditions:
// - under 150 m of drop on the runs their top opens and no other lift both opens and laps
// - not needed by the network (taken out one by one, with the thin lifts before)
// - no green run at the top (reachability alone cannot tell: from any top a skier reaches nearly every run)
// Grade mix: the served runs' vertical per band (a graded diagonal by the ground it crosses) against the band mix of the region's skiable
// ground, by area. Coverage counts a 100 m lattice. A cell is covered within 80 m of a served run (runs 160 m apart cover everything
// between them, so a closer comb earns nothing more). Ground past the double-black line is hostile: it is not in the coverage target,
// groomed runs earn nothing on it, natural runs half. Layout adds stacked lines (tops together, bottoms apart, one direction), lone
// bases (a base area with one lift where the plan has another base area) and lifts off the fall line (30 to 60 degrees). It measures
// parallel overlap against the shorter line. It counts zigzags per track (graded diagonals included) and same spot at 60 m. Validity
// adds a station under another lift's line and a natural run that is not black. Graded diagonals count half their length as
// connector km. Dead lifts (now among the thin lifts) weigh nothing.
//
// v5.1 (the judge's rulings on the v5 baseline, 2026-10-29): a tram counts toward the aerial allowance like a gondola unless its rise
// passes 700 m. Stacked lines are a validity violation (rejected), no longer a layout pair. A single-lift base area is a pod, not a lone
// base, when a run from another base area's lift ends at its bottom. The scorecard scores every lift against the fall line (no transfer
// exemption). Layout weights: base consolidation 3, fall line 3, parallel lines 2, zigzag 1, crowded terminals 1 (of 10).
//
// v6 (round v5's report, the judge's real-layout comparison and the owner's rulings, 2026-10-30): beginner is continuous. A base's
// credit is min(1, green km / 0.8) plus the beginner chair's quarter (capped at 1). The plan's term is the mean over bases weighted by
// each base's lift count. Grade mix leaves hostile ground out of the target (as coverage does). It also leaves the vertical of natural
// runs on hostile ground out of the planned mix: that ground stays wild. The fall line has a band per use. Chairs (and any lift that is
// not a trunk line) get 30 to 60 degrees. Trunk lines (a tram, or a gondola past 700 m of rise) get 45 to 75 (real lifts, median / p90: chairs
// 13 / 28, gondolas 19 / 45, trams 23 / 53 degrees). There is still no transfer exemption. No planning time term: the planner has no
// time limit, and the host prints each region's planning time.
//
// v7 (the owner's ruling after the port's round 1, 2026-11-09): a run's band is the band the mod's rater gives its outline, as the
// player sees it (SkiRunRater.Rate on the ring's nodes at snapshot heights, as the realizer and the catalog rate a built run). The 85th
// percentile of the grade along the centerline read about 70 % of the planned green km and 72 % of the planned blue km one band too
// easy. When the plan gives a run's outline (the realized feed: ScorePlan.Rings), v7 rates that outline. A run fed as one centerline
// gets a ribbon: RibbonOf (GradePath.Build at RibbonStepM, GradeRibbon.Build at RibbonHalfWidthM, chosen by measurement against the
// realized outlines). A graded connector keeps its own grade along the path as its band (the band it has once graded), and its ground
// band (grade mix) is the rater's band of a GradedHalfWidthM ribbon, the realizer's graded piste. Rated Flat counts as green; a broken
// outline is rated by the classifier's P90 fallback, as the game shows it. An outline the rater cannot rate (the game shows "not
// rated") keeps the v6 ground band and is counted. The 100 m ground cells (coverage, the grade-mix target) keep the 85th percentile:
// they are ground, not a run. RunsFirstScore.Version = 6 (host: RF_SCORE=6) scores as v6, bit for bit.
//
// Composite (0-100): fleet 10, skiable coverage 10, lift network 15, beginner terrain at each base 15, intermediate way down 5, grade
// mix 10, layout 10, validity 10, few stubs 5, little crowding 5, connector economy 5 (served vertical and dead-free are reported at
// weight 0). Fixed contract: planners must not edit this file.

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
    /// The scorecard ignores the heights in the points: it samples the terrain itself.</summary>
    internal sealed class ScorePlan
    {
        public readonly List<(List<float2> Pts, RunKind Kind)> Runs = new List<(List<float2>, RunKind)>();   // Groomed or Natural (v5)
        public readonly List<List<float2>> Connectors = new List<List<float2>>();   // traverses, cat tracks, way-home tracks
        public readonly List<(string Type, float2 Bottom, float2 Top)> Lifts = new List<(string, float2, float2)>();
        /// <summary>v7, optional: the built outline(s) of a run or connector, keyed by the very list object in Runs or Connectors
        /// (reference keys). Several outlines = one line skied through pieces: it takes the hardest piece's band. A line without an
        /// entry is rated on a ribbon (RunsFirstScore.RibbonOf).</summary>
        public readonly Dictionary<List<float2>, float2[][]> Rings = new Dictionary<List<float2>, float2[][]>();
    }

    internal static class RunsFirstScore
    {
        private const float StartM = 150f, JoinM = 40f, StationM = 60f, SampleM = 10f;
        private const float RunMaxPitch = 1.0f, RunClimbM = 10f;   // raw 3.5 m terrain: small bumps and 45-degree pitches are real
        private const float ConnMaxDown = 0.50f, ConnWindowM = 50f, ConnMaxRiseM = 4.5f;
        private const float TowerM = 20f, StationGrade = 0.40f, OnRunM = 20f;
        private const float HubM = 300f, SameSpotM = 60f, ParallelDeg = 20f, ParallelM = 250f, ParallelShare = 0.4f, TurnDeg = 60f, TurnsPerKm = 4f;
        private const float StubDropM = 50f, CrowdM = 40f, JunctionM = 100f, CoverTarget = 0.6f, ConnectorShare = 0.35f, BaseShare = 0.35f;
        // v5 coverage and grade mix: a 100 m lattice. The scorecard rates a cell by the 85th percentile of the grade on a 20 m grid inside it (as
        // a painted run is rated). A cell is skiable from the green line to RunMaxPitch, and hostile past the double-black line. A cell is
        // covered when a served run passes within CoverM of its center
        private const float CellM = 100f, CellSubM = 20f, CoverM = 80f;
        // v5 fleet: aerial lifts allowed = 1 + 1 per AerialKm2. A gondola or tram past TrunkRiseM (beyond a chair's rise) is a trunk
        // line. A lift under ThinDropM of served drop of its own is thin
        private const float AerialKm2 = 6f, TrunkRiseM = 700f, ThinDropM = 150f;
        // v5 layout:
        // - stacked lines (tops within StackTopM, bottoms over StackBottomM apart, directions within StackDeg)
        // - base areas (lift bottoms within BaseAreaM of each other in the lowest third of the lift-served vertical, no lift top within BaseTopM)
        // - fall line: no cost up to FallFreeDeg off the ground's mean downhill direction, full cost at FallFullDeg. Real lifts run a median 15
        //   and a p90 33 degrees off it. v6: a trunk line from FallTrunkFreeDeg to FallTrunkFullDeg
        // - a station within RopeM of another lift's line between 10 and 90 % of it is under that rope
        private const float StackTopM = 300f, StackBottomM = 300f, StackDeg = 30f, BaseAreaM = 300f, BaseTopM = 150f;
        private const float FallFreeDeg = 30f, FallFullDeg = 60f, FallTrunkFreeDeg = 45f, FallTrunkFullDeg = 75f, RopeM = 40f;
        private const float BeginnerFullKm = 0.8f, BeginnerChair = 0.25f;   // v6: a base's credit is min(1, km / BeginnerFullKm)
        // v7: the scorecard version (6 = the v6 band rule, the 85th percentile of the grade along the centerline) and the ribbon a run
        // fed as a centerline is rated on: GradePath.Build(points, RibbonStepM) (the realizer's centerline step), then
        // GradeRibbon.Build(path, half width) with RibbonHalfWidthM for a run and GradedHalfWidthM for a graded connector (the realizer's
        // graded piste). The ribbon width was chosen by its band agreement with the realized outlines (v7/BASELINE.md)
        public static int Version = Environment.GetEnvironmentVariable("RF_SCORE") == "6" ? 6 : 7;
        public const float RibbonStepM = 4f, RibbonHalfWidthM = 15f, GradedHalfWidthM = 7.5f;

        // name, length min / max (m), rise min / max (m), mean grade cap (stage-a-spec 4.4). v3: the scorecard does not check the maxima (no lift
        // length limit in the mod). They stay as the planner's type ladder. v5: the beginner chair is a fixed-grip chair for a learning
        // area, the planner's norm only (the mod's lift tool has no minimum). Its rise maximum IS checked (past it, it is a chair)
        private static readonly (string Name, float L0, float L1, float R0, float R1, float Cap)[] Types =
        {
            ("fixed-grip", 300f, 1500f, 95f, 512f, 0.8f),
            ("detachable", 600f, 2200f, 95f, 512f, 0.9f),
            ("gondola", 800f, 2700f, 96f, 781f, 1.2f),
            ("tram", 800f, 3000f, 220f, 888f, 2.5f),   // the mod has no surface lifts: a beginner pod is a chair
            ("beginner", 150f, 1500f, 30f, 150f, 0.8f),
        };

        private sealed class Elem
        {
            public bool Run;
            public float3[] P;
            public bool Graded;   // a connector graded steeper than the blue line: a diagonal run
            public bool Natural;  // an ungroomed run (v5)
            public float Drop, Length;
            public List<float2> Src;     // the plan's polyline (v7: the ribbon's centerline, the key into ScorePlan.Rings)
            public SlopeBand Band;       // as skied: the ground for a run, the track's own grade for a graded connector
            public SlopeBand GroundBand; // the ground under it (v5 grade mix: a graded diagonal across black ground counts black)
        }

        public sealed class Result
        {
            public float Total, Served, Coverage, Network, Beginner, WayHome, Layout, DeadFree, Validity, StubFree, CrowdFree, Economy;
            public float Fleet, GradeMix;
            public int Lifts, Bases, BeginnerBases, WayHomeLifts, HubExcess, Parallels, SharpTurns, Dead, Violations, ServedRuns, Runs;
            public int Aerial, AerialAllowed, Thin, Stacked, BaseAreas, LoneBases, OffFall, NaturalRuns;
            public int RatedRings, RatedRibbons, Unrated, Fallback;   // v7: outlines the rater banded, given or built; not rated; P90 fallback
            public float[] BandKm = new float[5];
            public float[] MixTarget = new float[5], MixPlan = new float[5];   // shares by band (green .. double black)
            public float ServedVerticalM, ConnectorKm, GreenRunKm, HostileShare, FallCost, CoverRaw;
            public List<string> Notes = new List<string>();
        }

        public static Result Evaluate(HeightSnapshot s, float2[] ring, ScorePlan plan)
        {
            var r = new Result();
            var rateGrid = new SlopeGrid();   // v7: the rater's buffers, reused for every outline
            var rateRes = new SlopeResult();
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
            foreach ((List<float2> pl, RunKind kind) in plan.Runs)
                if (pl != null && pl.Count >= 2)
                {
                    els.Add(Make(pl, true));
                    els[els.Count - 1].Natural = kind == RunKind.Natural;
                }
            foreach (List<float2> pl in plan.Connectors)
                if (pl != null && pl.Count >= 2)
                    els.Add(Make(pl, false));
            Elem Make(List<float2> pl, bool run)
            {
                var e = new Elem { Run = run, P = SamplesOf(s, pl), Src = pl };
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
                    e.Band = e.GroundBand = GroundBand(e);
                    if (e.Natural && e.Band < SlopeBand.Black)
                        Bad($"{name}: a natural run on {e.Band} ground (natural runs are black or double black)");
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
                        e.GroundBand = GroundBand(e);
                        r.ConnectorKm += 0.5f * e.Length / 1000f;   // v5: a graded diagonal is half a connector
                    }
                    else
                    {
                        e.Band = e.GroundBand = SlopeBand.Green;
                        r.ConnectorKm += e.Length / 1000f;
                    }
                }
            }
            // the ground under an element, as the game rates a painted run. v6: the 85th percentile of the terrain grade along it. v7: the
            // mod's rater on its outline (given, or a ribbon), the hardest of several pieces; the v6 band when the rater rates none
            SlopeBand GroundBand(Elem e)
            {
                if (Version >= 7)
                {
                    bool given = plan.Rings.TryGetValue(e.Src, out float2[][] rings) && rings != null && rings.Length > 0;
                    if (!given)
                        rings = new[] { RibbonOf(e.Src, e.Graded ? GradedHalfWidthM : RibbonHalfWidthM) };
                    bool any = false;
                    SlopeBand hardest = SlopeBand.Green;
                    foreach (float2[] rg in rings)
                        if (RateRing(s, rg, rateGrid, rateRes, out SlopeBand b))
                        {
                            any = true;
                            hardest = b > hardest ? b : hardest;
                            if (!rateRes.Connected)
                                r.Fallback++;
                        }
                    if (any)
                    {
                        if (given)
                            r.RatedRings++;
                        else
                            r.RatedRibbons++;
                        return hardest;
                    }
                    r.Unrated++;   // counted in Line(), no note: the violation notes keep their cap
                }
                return GroundBandV6(s, e.P);
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
                    if (len < ty.L0 || rise < ty.R0 || rise / math.max(1f, plan2) > ty.Cap || (type == "beginner" && rise > ty.R1))
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
            // v5: a station under another lift's rope (within RopeM of its line, between 10 and 90 % of its length)
            for (int i = 0; i < lifts.Count; i++)
                for (int j = 0; j < lifts.Count; j++)
                {
                    if (i == j)
                        continue;
                    float2 b = lifts[j].Bottom, d = lifts[j].Top - b;
                    float l2 = math.lengthsq(d);
                    if (l2 < 1f)
                        continue;
                    foreach ((float2 st, string which) in new[] { (lifts[i].Bottom, "bottom"), (lifts[i].Top, "top") })
                    {
                        float f = math.dot(st - b, d) / l2;
                        float off = math.abs((st - b).x * d.y - (st - b).y * d.x) / math.sqrt(l2);
                        if (f >= 0.1f && f <= 0.9f && off < RopeM)
                            Bad($"lift {i + 1}: {which} {off:F0} m off lift {j + 1}'s line, under its rope");
                    }
                }
            // lift budget: 1.5 per km2 + 2 (Snowmass runs about 1.6 per km2 of skiable terrain)
            double km2 = Area(ring) / 1e6;
            int budget = (int)Math.Ceiling(1.5 * km2) + 2;
            for (int i = budget; i < lifts.Count; i++)
                Bad($"lift {i + 1}: over the lift budget of {budget} for {km2:F1} km2");

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
            int best = BestScc(new HashSet<int>());
            // the largest strongly connected set with the lifts in `skip` taken out
            int BestScc(HashSet<int> skip)
            {
                int top1 = 0;
                for (int L = 0; L < lifts.Count; L++)
                {
                    if (skip.Contains(L))
                        continue;
                    var fwd = Closure(L, x => reaches[x].Where(y => !skip.Contains(y)));
                    int scc = 0;
                    foreach (int M in fwd)
                        if (Closure(M, x => reaches[x].Where(y => !skip.Contains(y))).Contains(L))
                            scc++;
                    top1 = Math.Max(top1, Math.Max(1, scc));
                }
                return top1;
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

            // ---- skiable coverage (v5): a 100 m lattice, each cell rated by the 85th percentile of the grade on a 20 m grid inside it
            float minX = ring.Min(p => p.x), maxX = ring.Max(p => p.x), minZ = ring.Min(p => p.y), maxZ = ring.Max(p => p.y);
            float hLo = float.MaxValue, hHi = float.MinValue;
            var hash = new Dictionary<long, List<(float2 P, bool Natural)>>();
            long Key(float2 p) => ((long)Math.Floor(p.x / CoverM) << 32) ^ (uint)(int)Math.Floor(p.y / CoverM);
            foreach (int i in served)
                foreach (float3 p in els[i].P)
                {
                    long k = Key(p.xz);
                    if (!hash.TryGetValue(k, out List<(float2, bool)> l))
                        hash[k] = l = new List<(float2, bool)>();
                    l.Add((p.xz, els[i].Natural));
                }
            // a groomed and / or a natural served run passes within CoverM
            (bool Groomed, bool Natural) Covered(float2 p)
            {
                int cx = (int)Math.Floor(p.x / CoverM), cz = (int)Math.Floor(p.y / CoverM);
                bool gr = false, nat = false;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (hash.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out List<(float2 P, bool Natural)> l))
                            foreach ((float2 q, bool n) in l)
                                if (math.distancesq(q, p) < CoverM * CoverM)
                                {
                                    gr |= !n;
                                    nat |= n;
                                }
                return (gr, nat);
            }
            float skiable = 0f, covered = 0f;
            int hostile = 0, cells = 0;
            var groundBands = new float[5];
            for (float z = minZ + 0.5f * CellM; z - 0.5f * CellM < maxZ; z += CellM)
                for (float x = minX + 0.5f * CellM; x - 0.5f * CellM < maxX; x += CellM)
                {
                    var p = new float2(x, z);
                    var gs = new List<float>();
                    for (float dz = -0.5f * CellM + 0.5f * CellSubM; dz < 0.5f * CellM; dz += CellSubM)
                        for (float dx = -0.5f * CellM + 0.5f * CellSubM; dx < 0.5f * CellM; dx += CellSubM)
                        {
                            var q = p + new float2(dx, dz);
                            if (!InsideRing(ring, q))
                                continue;
                            float h = H(q);
                            hLo = Math.Min(hLo, h);   // the region's relief (bases), on the 20 m grid
                            hHi = Math.Max(hHi, h);
                            gs.Add(Grade(q));
                        }
                    if (gs.Count == 0 || !InsideRing(ring, p))
                        continue;
                    gs.Sort();
                    float rating = gs[(int)((gs.Count - 1) * 0.85f)];
                    if (rating < SkiSlopeClassifier.GradeGreen || rating > RunMaxPitch)
                        continue;   // too flat to ski, or past the steepest pitch a run may hold
                    cells++;
                    (bool byGroomed, bool byNatural) = Covered(p);
                    if (rating >= SkiSlopeClassifier.GradeDoubleBlack)
                    {
                        // hostile ground: not in the target. A groomed run earns nothing on it, a natural run half
                        hostile++;
                        if (byNatural)
                            covered += 0.5f;
                        continue;
                    }
                    skiable++;
                    groundBands[(int)SkiSlopeClassifier.BandOf(rating)]++;   // v6: the grade-mix target leaves hostile ground out too
                    covered += byGroomed ? 1f : byNatural ? 0.5f : 0f;
                }
            r.HostileShare = cells > 0 ? hostile / (float)cells : 0f;
            r.CoverRaw = skiable > 0f ? covered / skiable : 0f;
            r.Coverage = Math.Min(1f, r.CoverRaw / CoverTarget);

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
            var servedSet = new HashSet<int>(served);
            // v5: a green run, groomed and served (cat tracks, graded tracks and connectors are how a beginner moves, not learning terrain)
            bool GreenRun(int i) => els[i].Run && !els[i].Graded && !els[i].Natural && els[i].Band == SlopeBand.Green && servedSet.Contains(i);
            // beginner terrain: from the base, a beginner rides lifts whose tops have green skiing (or a gondola / tram, both ways). When
            // that skiing delivers them to a lift again (a loop they can lap), the credit is the km of green runs on it (v5)
            float LearnKm(int B)
            {
                var S = new HashSet<int>();
                var q = new Queue<int>();
                for (int L = 0; L < lifts.Count; L++)
                    if (Find(baseOf, L) == B && (IsAerial(L) || EasyFrom(L, SlopeBand.Green).Count > 0) && S.Add(L))
                        q.Enqueue(L);
                bool loop = false;
                var runs = new HashSet<int>();
                while (q.Count > 0)
                {
                    int L = q.Dequeue();
                    foreach (int i in EasyFrom(L, SlopeBand.Green))
                    {
                        if (GreenRun(i))
                            runs.Add(i);
                        foreach (int M in delivers[i])
                        {
                            loop = true;
                            if ((IsAerial(M) || EasyFrom(M, SlopeBand.Green).Count > 0) && S.Add(M))
                                q.Enqueue(M);
                        }
                    }
                }
                return loop ? runs.Sum(i => els[i].Length) / 1000f : 0f;
            }
            foreach (int B in bases.Keys.ToList())
            {
                float km = LearnKm(B);
                r.GreenRunKm += km;
                float credit = Math.Min(1f, km / BeginnerFullKm);   // v6: continuous (v5: half at 0.3 km, full at 0.8 km)
                if (Enumerable.Range(0, lifts.Count).Any(L => Find(baseOf, L) == B && lifts[L].Type == "beginner" && EasyFrom(L, SlopeBand.Green).Any(GreenRun)))
                    credit += BeginnerChair;   // a learning chair with green runs from its top
                bases[B] = Math.Min(1f, credit);
                if (credit < 1f && r.Notes.Count < 80)
                    r.Notes.Add($"base at lift {Enumerable.Range(0, lifts.Count).First(L => Find(baseOf, L) == B) + 1} ({size[B]} lifts): {km:F2} km of green runs for a beginner");
            }
            r.Bases = bases.Count;
            r.BeginnerBases = bases.Values.Count(v => v >= 1f);
            // v6: weighted by each base's lift count (a two-lift pad without greens costs less than the main base without greens)
            int baseLifts = bases.Keys.Sum(B => size[B]);
            r.Beginner = baseLifts > 0 ? bases.Sum(kv => size[kv.Key] * kv.Value) / baseLifts : 0f;

            // ---- intermediate way down (v4): the share of the resort's vertical, lowest base lift bottom to highest lift top, from which
            // a skier gets home on green or blue (to a base lift's bottom, or to a gondola / tram top to ride down). Experts-only lifts
            // cost nothing; WayHomeLifts counts the lifts with such a route (a gondola or tram counts as its own) for the log.
            float baseLo = float.MaxValue, topHi = float.MinValue, easyHi = float.MinValue;
            for (int L = 0; L < lifts.Count; L++)
            {
                if (bases.ContainsKey(Find(baseOf, L)))
                    baseLo = Math.Min(baseLo, bot[L].y);
                topHi = Math.Max(topHi, top[L].y);
            }
            for (int L = 0; L < lifts.Count; L++)
            {
                HashSet<int> easy = EasyFrom(L, SlopeBand.Blue);
                bool ok = easy.Any(i => atBottom[i].Any(M => bases.ContainsKey(Find(baseOf, M)))
                    || delivers[i].Any(M => IsAerial(M) && !atBottom[i].Contains(M)));
                if (ok)
                    easyHi = Math.Max(easyHi, top[L].y);
                if (ok || IsAerial(L))
                    r.WayHomeLifts++;
            }
            r.WayHome = lifts.Count == 0 ? 0f : topHi - baseLo < 1f ? 1f : Math.Max(0f, Math.Min(1f, (easyHi - baseLo) / (topHi - baseLo)));
            if (r.WayHome < 1f && r.Notes.Count < 60)
                r.Notes.Add($"intermediate way down from {Math.Max(0f, easyHi - baseLo):F0} m of the {topHi - baseLo:F0} m vertical");

            // ---- fleet (v5, v5.1): gondolas and trams past the area's allowance (one past TrunkRiseM of rise is a trunk line and never
            // counts), and thin lifts. A lift's top opens the served runs reached from it along tracks and traverses, no run in between;
            // each opened run has one keeper, a lift that laps it (gets its skiers back to its bottom) before one that does not, the
            // longest rise first: of two lifts to one top the shorter keeps nothing, two lifts from opposite valleys each keep their side.
            // A lift is thin under ThinDropM of kept runs' drop, unless the network needs it (taking it out, with the thin lifts before it,
            // shrinks the largest strongly connected set by more than those lifts), it is a beginner chair, or its top has a green run.
            // Lifts are taken out one by one, least kept drop first: of three access lifts into one upper area, two are thin, not three
            r.Aerial = Enumerable.Range(0, lifts.Count).Count(L => IsAerial(L) && top[L].y - bot[L].y <= TrunkRiseM);
            r.AerialAllowed = 1 + (int)Math.Floor(km2 / AerialKm2);
            float aerialS = (float)Math.Exp(-Math.Max(0, r.Aerial - r.AerialAllowed) / 1.5);
            if (r.Aerial > r.AerialAllowed && r.Notes.Count < 80)
                r.Notes.Add($"{r.Aerial} gondolas and trams under {TrunkRiseM:F0} m of rise, {r.AerialAllowed} allowed for {km2:F1} km2");
            var backTo = new HashSet<int>[m];   // the lifts a skier on element i gets back to without riding a lift
            for (int i = 0; i < m; i++)
            {
                backTo[i] = new HashSet<int>();
                var seen = new HashSet<int> { i };
                var q = new Queue<int>();
                q.Enqueue(i);
                while (q.Count > 0)
                {
                    int x = q.Dequeue();
                    backTo[i].UnionWith(delivers[x]);
                    foreach (int y in next[x])
                        if (seen.Add(y))
                            q.Enqueue(y);
                }
            }
            var opens = new HashSet<int>[lifts.Count];
            for (int L = 0; L < lifts.Count; L++)
            {
                opens[L] = new HashSet<int>();
                var seen = new HashSet<int>();
                var q = new Queue<int>();
                for (int i = 0; i < m; i++)
                    if (math.distancesq(els[i].P[0].xz, top[L].xz) < StartM * StartM && seen.Add(i))
                        q.Enqueue(i);
                while (q.Count > 0)
                {
                    int x = q.Dequeue();
                    if (els[x].Run)
                    {
                        if (servedSet.Contains(x))
                            opens[L].Add(x);
                        continue;
                    }
                    foreach (int y in next[x])
                        if (seen.Add(y))
                            q.Enqueue(y);
                }
            }
            // each opened run has one keeper: of the lifts whose tops open it, one that laps it first, the longest rise first
            var own = new float[lifts.Count];
            foreach (int i in served)
            {
                int keep = -1;
                for (int L = 0; L < lifts.Count; L++)
                    if (opens[L].Contains(i) && (keep < 0 || (backTo[i].Contains(L), top[L].y - bot[L].y).CompareTo((backTo[i].Contains(keep), top[keep].y - bot[keep].y)) > 0))
                        keep = L;
                if (keep >= 0)
                    own[keep] += Math.Max(0f, els[i].Drop);
            }
            var thin = new HashSet<int>();
            foreach (int L in Enumerable.Range(0, lifts.Count).OrderBy(L => own[L]))
            {
                if (lifts[L].Type == "beginner" || EasyFrom(L, SlopeBand.Green).Any(GreenRun) || own[L] >= ThinDropM)
                    continue;
                thin.Add(L);
                if (BestScc(thin) < best - thin.Count)
                {
                    thin.Remove(L);
                    if (r.Notes.Count < 80)   // v6: a link lift is never thin. The code says so, so a split is visible in the notes
                        r.Notes.Add($"lift {L + 1}: {own[L]:F0} m of served drop of its own, kept: the network needs it");
                    continue;
                }
                r.Thin++;
                if (r.Notes.Count < 80)
                    r.Notes.Add($"lift {L + 1}: thin ({own[L]:F0} m of served drop of its own, the network does not need it)");
            }
            float thinS = lifts.Count > 0 ? 1f - r.Thin / (float)lifts.Count : 0f;
            r.Fleet = (aerialS + thinS) / 2f;

            // ---- grade mix (v5): the served runs' vertical per band (a graded diagonal by the ground it crosses) against the band mix
            // of the region's skiable ground (the 100 m cells above). v6: hostile ground is out of the target (v5 counted it as double
            // black), and a natural run on hostile ground is out of the planned mix (that ground stays wild; coverage scores it)
            float cellSum = groundBands.Sum(), dropSum = 0f;
            var planBands = new float[5];
            foreach (int i in served)
            {
                if (els[i].Natural && els[i].GroundBand == SlopeBand.DoubleBlack)
                    continue;
                planBands[(int)els[i].GroundBand] += Math.Max(0f, els[i].Drop);
                dropSum += Math.Max(0f, els[i].Drop);
            }
            float l1 = 0f;
            for (int b = 1; b < 5; b++)
            {
                r.MixTarget[b] = cellSum > 0f ? groundBands[b] / cellSum : 0f;
                r.MixPlan[b] = dropSum > 0f ? planBands[b] / dropSum : 0f;
                l1 += Math.Abs(r.MixTarget[b] - r.MixPlan[b]);
            }
            r.GradeMix = dropSum > 0f && cellSum > 0f ? 1f - 0.5f * l1 : 0f;

            // ---- layout (v4, v5): crowded terminals, parallel and stacked lift lines, zigzag tracks, lone bases, lifts off the fall line
            var term = new List<(float2 P, bool Base)>();
            for (int L = 0; L < lifts.Count; L++)
            {
                term.Add((bot[L].xz, bases.ContainsKey(Find(baseOf, L))));
                term.Add((top[L].xz, false));
            }
            var hub = new int[term.Count];
            for (int a = 0; a < term.Count; a++)
            {
                hub[a] = a;
                for (int b2 = 0; b2 < a; b2++)
                    if (math.distance(term[a].P, term[b2].P) < HubM)
                        Union(hub, a, b2);
            }
            foreach (var g in Enumerable.Range(0, term.Count).GroupBy(a => Find(hub, a)))
            {
                int allowed = g.Any(a => term[a].Base) ? 6 : 4, n = g.Count();
                if (n > allowed)
                {
                    r.HubExcess += n - allowed;
                    if (r.Notes.Count < 80)
                        r.Notes.Add($"{n} lift terminals within {HubM} m of ({term[g.Key].P.x:F0}, {term[g.Key].P.y:F0})");
                }
            }
            // two bottoms (or two tops) on the same spot: each lift needs its own terminal (a top beside the next bottom is a transfer)
            for (int a = 0; a < term.Count; a++)
            {
                int same = 0;
                for (int b2 = a % 2; b2 < a; b2 += 2)
                    if (math.distance(term[a].P, term[b2].P) < SameSpotM)
                        same++;
                if (same > 0)
                {
                    r.HubExcess++;
                    if (r.Notes.Count < 80)
                        r.Notes.Add($"lift {a / 2 + 1}: {(a % 2 == 0 ? "bottom" : "top")} on another lift's {(a % 2 == 0 ? "bottom" : "top")}");
                }
            }
            // a pair of lines that run side by side (overlap against the shorter line, v5) costs layout. One that rides on top of the
            // other (stacked: tops together, bottoms apart, one direction) is a validity violation instead (v5.1). Fans (bottoms
            // together) are fine
            for (int i = 0; i < lifts.Count; i++)
                for (int j = 0; j < i; j++)
                {
                    float2 bi = bot[i].xz, di = top[i].xz - bi, bj = bot[j].xz, dj = top[j].xz - bj;
                    float li = math.length(di), lj = math.length(dj);
                    if (li < 1f || lj < 1f)
                        continue;
                    float cos = math.dot(di / li, dj / lj);
                    if (cos >= math.cos(math.radians(StackDeg)) && math.distance(top[i].xz, top[j].xz) < StackTopM
                        && math.distance(bi, bj) > StackBottomM)
                    {
                        r.Stacked++;
                        violations++;
                        if (r.Notes.Count < 80)
                            r.Notes.Add($"lifts {j + 1} and {i + 1} are stacked (tops {math.distance(top[i].xz, top[j].xz):F0} m apart, one line on the other)");
                        continue;
                    }
                    if (cos >= math.cos(math.radians(ParallelDeg)))
                    {
                        // overlap measured along the longer line against the shorter one, separation at the overlap's middle
                        bool iLong = li >= lj;
                        float2 b0 = iLong ? bi : bj, u = iLong ? di / li : dj / lj, c0 = iLong ? bj : bi, c1 = iLong ? top[j].xz : top[i].xz;
                        float lLong = Math.Max(li, lj);
                        float s0 = math.dot(c0 - b0, u), s1 = math.dot(c1 - b0, u);
                        float lo = Math.Max(0f, Math.Min(s0, s1)), hi = Math.Min(lLong, Math.Max(s0, s1));
                        if (hi - lo >= ParallelShare * Math.Min(li, lj))
                        {
                            float2 mid = math.lerp(c0, c1, (0.5f * (lo + hi) - s0) / math.max(1f, s1 - s0));
                            float2 off = mid - b0;
                            float sep = math.abs(off.x * u.y - off.y * u.x);
                            if (sep < ParallelM)
                            {
                                r.Parallels++;
                                if (r.Notes.Count < 80)
                                    r.Notes.Add($"lifts {j + 1} and {i + 1} run parallel ({sep:F0} m apart)");
                            }
                        }
                    }
                }
            // zigzags per track (v5): green cat tracks and graded diagonals (drawn as tracks, skied as such), each allowed 4 per km + 1
            var zig = new List<float>();
            foreach (Elem e in els)
            {
                if (e.Run && !e.Graded)
                    continue;
                int turns = 0;
                for (int k = 4; k < e.P.Length; k += 2)
                {
                    float2 a1 = e.P[k - 2].xz - e.P[k - 4].xz, a2 = e.P[k].xz - e.P[k - 2].xz;
                    if (math.lengthsq(a1) < 1f || math.lengthsq(a2) < 1f)
                        continue;
                    if (math.dot(math.normalize(a1), math.normalize(a2)) < math.cos(math.radians(TurnDeg)))
                        turns++;
                }
                r.SharpTurns += turns;
                float allowedTurns = TurnsPerKm * e.Length / 1000f + 1f;
                zig.Add(turns <= allowedTurns ? 1f : allowedTurns / turns);
            }
            // base areas (v5): lift bottoms within BaseAreaM of each other in the lowest third of the lift-served vertical, no lift top
            // within BaseTopM (that is a transfer station). A base area with one lift is lone where the plan has another base area. A lone base
            // is a separate pad on the region's edge instead of a base with its lifts strung along it. The question of whether runs from another
            // base reach it cannot tell them apart: they reach nearly every bottom. v5.1: unless a run from another base area's lift (one its top
            // opens) ends at its bottom. That lift is a pod
            var area = new int[lifts.Count];
            var isBase = new bool[lifts.Count];
            if (lifts.Count > 0)
            {
                float vLo = bot.Min(p => p.y), vHi = top.Max(p => p.y);
                for (int L = 0; L < lifts.Count; L++)
                {
                    area[L] = L;
                    isBase[L] = bot[L].y <= vLo + (vHi - vLo) / 3f
                        && !Enumerable.Range(0, lifts.Count).Any(M => math.distance(top[M].xz, bot[L].xz) < BaseTopM);
                }
                for (int L = 0; L < lifts.Count; L++)
                    for (int M = 0; M < L; M++)
                        if (isBase[L] && isBase[M] && math.distance(bot[L].xz, bot[M].xz) < BaseAreaM)
                            Union(area, L, M);
                var areas = Enumerable.Range(0, lifts.Count).Where(L => isBase[L]).GroupBy(L => Find(area, L)).ToList();
                r.BaseAreas = areas.Count;
                foreach (var A in areas)
                {
                    if (A.Count() != 1 || areas.Count < 2)
                        continue;
                    int L = A.First();
                    if (Enumerable.Range(0, lifts.Count).Any(M => isBase[M] && Find(area, M) != A.Key && opens[M].Any(i =>
                        math.distancesq(els[i].P[els[i].P.Length - 1].xz, bot[L].xz) < StationM * StationM)))
                        continue;
                    r.LoneBases++;
                    if (r.Notes.Count < 80)
                        r.Notes.Add($"lift {L + 1}: a base area of its own");
                }
            }
            // fall line (v5): the angle between a lift's line and the mean uphill direction of the ground under it. No cost to
            // FallFreeDeg, full cost at FallFullDeg. v6: a trunk line (a tram, or a gondola past TrunkRiseM) has the band from FallTrunkFreeDeg to
            // FallTrunkFullDeg. This is the relief for a line across a face. There is still no transfer exemption. Exempt: a line on ground
            // too flat to have a fall line
            for (int L = 0; L < lifts.Count; L++)
            {
                float2 b = bot[L].xz, d = top[L].xz - b;
                float len = math.length(d);
                if (len < 1f)
                    continue;
                float2 u = d / len, up = float2.zero;
                int k = 0;
                for (float along = 0f; along <= len; along += SampleM, k++)
                {
                    float2 q = b + u * along;
                    const float dg = 10f;
                    up += new float2(H(q + new float2(dg, 0f)) - H(q - new float2(dg, 0f)), H(q + new float2(0f, dg)) - H(q - new float2(0f, dg))) / (2f * dg);
                }
                up /= Math.Max(1, k);
                if (math.length(up) < SkiSlopeClassifier.GradeGreen)
                    continue;
                bool trunk = lifts[L].Type == "tram" || lifts[L].Type == "gondola" && top[L].y - bot[L].y > TrunkRiseM;
                float free = trunk ? FallTrunkFreeDeg : FallFreeDeg, full = trunk ? FallTrunkFullDeg : FallFullDeg;
                float deg = math.degrees(math.acos(math.clamp(math.dot(u, math.normalize(up)), -1f, 1f)));
                float cost = math.saturate((deg - free) / (full - free));
                if (cost > 0f)
                {
                    r.OffFall++;
                    r.FallCost += cost;
                    if (r.Notes.Count < 80)
                        r.Notes.Add($"lift {L + 1}: {deg:F0} degrees off the fall line{(trunk ? " (a trunk line)" : "")}");
                }
            }
            float hubS = (float)Math.Exp(-r.HubExcess / 2.0), parS = (float)Math.Exp(-r.Parallels / 3.0);
            float zigS = zig.Count > 0 ? zig.Average() : 1f, baseS = (float)Math.Exp(-r.LoneBases / 2.0);
            float fallS = lifts.Count > 0 ? 1f - r.FallCost / lifts.Count : 1f;
            r.Layout = (3f * baseS + 3f * fallS + 2f * parS + zigS + hubS) / 10f;   // v5.1 weights
            r.Violations = violations;   // the validity checks above, and stacked pairs (v5.1)
            r.Validity = (float)Math.Exp(-violations / 10.0);
            r.NaturalRuns = served.Count(i => els[i].Natural);

            r.Total = 10f * r.Fleet + 10f * r.Coverage + 15f * r.Network + 15f * r.Beginner + 5f * r.WayHome + 10f * r.GradeMix
                + 10f * r.Layout + 10f * r.Validity + 5f * r.StubFree + 5f * r.CrowdFree + 5f * r.Economy;
            return r;
        }

        public static string Line(string id, Result r) => string.Format(CultureInfo.InvariantCulture,
            "SCORE rf {0}: {1:F1} | fleet {2:F2} ({3}/{4} aerial, {5} thin) cover {6:F2} ({50:F2} raw, {7:F0} % hostile) network {8:F2} beginner {9:F2} ({10}/{11} bases green, {12:F2} km) " +
            "way-home {13:F2} ({14}/{15} lifts) grade-mix {16:F2} (ground G/B/K/DB {17:F0}/{18:F0}/{19:F0}/{20:F0} %, runs {21:F0}/{22:F0}/{23:F0}/{24:F0} %) " +
            "layout {25:F2} ({26} hub, {27} parallel, {29} turns, {30}/{31} lone bases, {32} off the fall line {33:F1}) valid {34:F2} ({35} violations, {28} stacked) " +
            "stub-free {36:F2} crowd-free {37:F2} economy {38:F2} served {39:F2} dead-free {40:F2} | {15} lifts, {41}/{42} runs served ({43} natural), " +
            "{44:F0} m served vertical, km G/B/K/DB {45:F1}/{46:F1}/{47:F1}/{48:F1}, connectors {49:F1} km",
            id, r.Total, r.Fleet, r.Aerial, r.AerialAllowed, r.Thin, r.Coverage, 100f * r.HostileShare, r.Network, r.Beginner, r.BeginnerBases, r.Bases, r.GreenRunKm,
            r.WayHome, r.WayHomeLifts, r.Lifts, r.GradeMix, 100f * r.MixTarget[1], 100f * r.MixTarget[2], 100f * r.MixTarget[3], 100f * r.MixTarget[4],
            100f * r.MixPlan[1], 100f * r.MixPlan[2], 100f * r.MixPlan[3], 100f * r.MixPlan[4],
            r.Layout, r.HubExcess, r.Parallels, r.Stacked, r.SharpTurns, r.LoneBases, r.BaseAreas, r.OffFall, r.FallCost, r.Validity, r.Violations,
            r.StubFree, r.CrowdFree, r.Economy, r.Served, r.DeadFree, r.ServedRuns, r.Runs, r.NaturalRuns,
            r.ServedVerticalM, r.BandKm[1], r.BandKm[2], r.BandKm[3], r.BandKm[4], r.ConnectorKm, r.CoverRaw)
            + (Version >= 7
                ? string.Format(CultureInfo.InvariantCulture, ", rater bands: {0} outlines, {1} ribbons, {2} unrated, {3} P90 fallback | scorecard v7",
                    r.RatedRings, r.RatedRibbons, r.Unrated, r.Fallback)
                : " | scorecard v6");

        /// <summary>A plan polyline resampled every SampleM (10 m) on the terrain: each segment in ceil(length / SampleM) equal steps,
        /// the last point kept.</summary>
        public static float3[] SamplesOf(HeightSnapshot s, List<float2> pl)
        {
            float H(float2 p) => s.Sample(new float3(p.x, 0f, p.y));
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
            return pts.ToArray();
        }

        /// <summary>The v6 band of an element (v3 to v6): the 85th percentile of the terrain grade (central differences over 10 m) at its
        /// 10 m samples. Flat counts as green.</summary>
        public static SlopeBand GroundBandV6(HeightSnapshot s, float3[] samples)
        {
            float H(float2 p) => s.Sample(new float3(p.x, 0f, p.y));
            float Grade(float2 p)
            {
                const float d = 10f;
                float gx = (H(p + new float2(d, 0f)) - H(p - new float2(d, 0f))) / (2f * d);
                float gz = (H(p + new float2(0f, d)) - H(p - new float2(0f, d))) / (2f * d);
                return math.sqrt(gx * gx + gz * gz);
            }
            var gs = samples.Select(p => Grade(p.xz)).OrderBy(x => x).ToList();
            SlopeBand band = SkiSlopeClassifier.BandOf(gs.Count > 0 ? gs[(int)((gs.Count - 1) * 0.85f)] : 0f);
            return band == SlopeBand.Flat ? SlopeBand.Green : band;
        }

        /// <summary>v7: the ribbon a run fed as a centerline is rated on. GradePath.Build(centerline, RibbonStepM) (a Catmull-Rom chain
        /// resampled at about 4 m, both ends kept), then GradeRibbon.Build(path, halfWidth) (constant half width, square ends at the end
        /// samples, inside-bend loops cut, simplified 0.5 m, thinned 2 m). The ring is rated as built: a ring that fails GradeRibbon's
        /// clearance (a bend tighter than the half width) is not retried. Null when the path has under two distinct points.</summary>
        public static float2[] RibbonOf(IList<float2> centerline, float halfWidth)
        {
            GradePath path = GradePath.Build(centerline, RibbonStepM);
            return path == null ? null : GradeRibbon.Build(path, halfWidth, out GradeRibbonFail _);
        }

        /// <summary>v7: SkiRunRater.Rate on the ring's nodes at snapshot heights, as the realizer (RunsFirstRealizer.RateRing) and the
        /// catalog rate a run. True with the rater's band when it rates (status Ok or ShortRun; a broken outline carries the classifier's
        /// P90 fallback band, res.Connected false). Flat counts as green. False when the game would show "not rated" (Failed, NoTerrain,
        /// TooLarge; a snapshot miss is a NaN height, NonFinite).</summary>
        public static bool RateRing(HeightSnapshot s, float2[] ring, SlopeGrid grid, SlopeResult res, out SlopeBand band)
        {
            band = SlopeBand.Green;
            if (ring == null || ring.Length < 3)
                return false;
            var nodes = new float3[ring.Length];
            for (int i = 0; i < ring.Length; i++)
                nodes[i] = new float3(ring[i].x, s.Sample(new float3(ring[i].x, 0f, ring[i].y)), ring[i].y);
            var sampler = new SnapshotSampler(s);
            RateStatus rs = SkiRunRater.Rate(nodes, nodes.Length, s.Origin, ref sampler, s.TexelM, 0, grid, null, res, out int _, out float _, out float _);
            if (rs != RateStatus.Rated || sampler.Misses > 0)
                return false;
            band = res.Band == SlopeBand.Flat ? SlopeBand.Green : res.Band;
            return true;
        }

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
