// Quick runs: the tool's pure rules (QuickRunsToolSystem). The file holds: region readout against the area cap, preview totals (count, mix
// against the planner's target, the fee the binds will charge, skiers), the money check, the refund an undo pays back, the Temp ->
// candidate match by geometry, the click pick, the warning / refusal locale keys and the knob clamps. Pure file: it uses System,
// Collections and Unity.Mathematics only, plus the pure rules it reads (SkiRunFees, SkiResortScore, the planner's geometry). The caller
// decodes the SkiRunBinding flags, an ECS type. So tests/AutoGenBatch links it and proves every rule headless.
// Engine notes: EngineNotes/QuickRunsToolSystem.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The live size line's state (tooltip color): Open = fewer than 3 corners. TooSmall = under the planner's minimum area. Near
    /// = from NearCapShare of the cap. Over = above the cap (Enter refuses).</summary>
    public enum QuickRegionState : byte { Open, TooSmall, Ok, Near, Over }

    /// <summary>What a click does while the region is on the map (no preview): RegionClick.</summary>
    public enum QuickRegionClick : byte { Ignore, AddCorner, InsertCorner, CloseRegion, DragCorner }

    public struct QuickRegionReadout
    {
        public float AreaM2, VerticalM;
        public QuickRegionState State;
    }

    /// <summary>The preview totals over the enabled candidates. Count / Target are indexed by SlopeBand (0 Flat .. 4 DoubleBlack).</summary>
    public sealed class QuickTotals
    {
        public int Runs, Enabled;
        public readonly int[] Count = new int[5], Target = new int[5];
        public readonly int[] Kinds = new int[5];   // by RunKind
        public long Fee;
        public double SkiersPerDay;
        public float AreaM2;
        public bool Variety;

        public void Clear()
        {
            Runs = Enabled = 0;
            Array.Clear(Count, 0, Count.Length);
            Array.Clear(Target, 0, Target.Length);
            Array.Clear(Kinds, 0, Kinds.Length);
            Fee = 0;
            SkiersPerDay = 0.0;
            AreaM2 = 0f;
            Variety = false;
        }
    }

    /// <summary>The tool's own warnings, next to the planner's AutoPlanWarning.</summary>
    [Flags]
    public enum QuickToolWarning : byte
    {
        None = 0,
        MoneyShort = 1,       // the total fee is more than the city has (Enter refuses)
        Renumber = 2,         // default-named runs exist: the new runs renumber them (SkiRunCatalogSystem ordinals)
        OtherResort = 4,      // the enabled runs bind to more than one Welcome Sign
        NoSign = 8,           // no Welcome Sign within the tether: the runs build and wait unbound, as drawn runs do
        ChecksOff = 16,       // the game's own checks are off (QuickRunsRules.ChecksState). Only the planner's checks protect the build
    }

    /// <summary>Whether the game would flag an overlapping preview run (QuickRunsRules.ChecksState). The state is read from the vanilla
    /// error prefab for OverlapExisting, as ValidationSystem.FillErrorPrefabsJob does (ValidationSystem.cs:1318-1325: a prefab whose
    /// ToolErrorData carries DisableInGame is skipped, and with none left no Error is spawned). ToolSystem.ignoreErrors is a second
    /// source.</summary>
    public enum QuickGameChecks : byte
    {
        On,
        OverlapDisabled,   // every OverlapExisting error prefab carries DisableInGame (a mod disabled it for the whole frame). Anarchy only does so between Modification5 and ModificationEnd: SkiOtherMods.AnarchyDetected
        NoOverlapPrefab,   // no OverlapExisting error prefab at all: the game cannot flag an overlap
        IgnoreErrors,      // ToolSystem.ignoreErrors: errors are flagged but Apply ignores them (ToolBaseSystem.cs:533-540)
    }

    /// <summary>What to do after the game validated an emission (all-or-nothing Apply: ToolBaseSystem.cs:533-540).</summary>
    public enum QuickBuildStep : byte { Apply, Retry, GiveUp }

    public static class QuickRunsRules
    {
        /// <summary>Milliseconds since a Stopwatch.GetTimestamp() reading: the one conversion for every wall-time log of the tool.</summary>
        public static double MsSince(long ticks) =>
            (System.Diagnostics.Stopwatch.GetTimestamp() - ticks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>The size line turns amber from this share of the cap.</summary>
        public const float NearCapShare = 0.85f;

        /// <summary>A Temp node matches an emitted node within this (xz, m): GenerateAreasSystem copies the definition's xz verbatim and
        /// snaps only y (GenerateAreasSystem.cs:307-316).</summary>
        public const float MatchToleranceM = 0.5f;

        /// <summary>A click this close (xz, m) to a candidate's outline picks it when it is inside none.</summary>
        public const float PickM = 12f;

        /// <summary>Tree-line slider step (m) and its reach beyond the region's ground range.</summary>
        public const float TreeLineStepM = 10f;

        public const string TipPrefix = "SkiResort.Tooltip.QUICK_";

        // ---------------------------------------------------------------- region

        /// <summary>A click on the map with no preview. Open: the first corner closes a region of 3 or more. Other corners drag. A click on
        /// an edge inserts. Anywhere else adds. Closed: corners drag and edges insert. Anywhere else does nothing, so a stray click cannot
        /// make the outline cross itself (field test 2026-10-31). insertIndex == corners means no edge is near
        /// (GradeEdit.InsertIndex).</summary>
        public static QuickRegionClick RegionClick(bool closed, int corners, int hoverCorner, int insertIndex)
        {
            if (hoverCorner >= 0)
                return !closed && corners >= 3 && hoverCorner == 0 ? QuickRegionClick.CloseRegion : QuickRegionClick.DragCorner;
            if (insertIndex < corners)
                return QuickRegionClick.InsertCorner;
            return closed ? QuickRegionClick.Ignore : QuickRegionClick.AddCorner;
        }

        /// <summary>Area and vertical of the outline drawn so far. minY / maxY = the ground range under the corners (the tool samples
        /// them). The planner measures the real vertical over open ground on Enter.</summary>
        public static QuickRegionReadout Readout(float2[] corners, float minY, float maxY, float maxAreaM2, float minAreaM2)
        {
            var r = new QuickRegionReadout { State = QuickRegionState.Open };
            if (corners == null || corners.Length < 3)
                return r;
            r.AreaM2 = (float)PlanGeometry.PolygonArea(corners);
            r.VerticalM = math.isfinite(minY) && math.isfinite(maxY) && maxY >= minY ? maxY - minY : 0f;
            if (!(r.AreaM2 >= minAreaM2))
                r.State = QuickRegionState.TooSmall;
            else if (r.AreaM2 > maxAreaM2)
                r.State = QuickRegionState.Over;
            else if (r.AreaM2 >= NearCapShare * maxAreaM2)
                r.State = QuickRegionState.Near;
            else
                r.State = QuickRegionState.Ok;
            return r;
        }

        // ---------------------------------------------------------------- site verdict (DECISIONS 443)

        /// <summary>The plain-language verdict on how steep the outlined ground is (locale QUICK_SITE[Gentle|Steep|VerySteep|Beyond]).
        /// Unknown = no figure yet. Tiers come from the real-resort data (docs/research/quickruns-v2/data-report.md: base areas 15-57 %,
        /// pods over 55 % have no green descent, 45-55 % rarely).</summary>
        public enum SiteTier : byte { Unknown, Gentle, Steep, VerySteep, Beyond }

        /// <summary>Lowest whole percent of each tier: Steep from 25, Very steep from 45, Beyond from 58 (57 % is the steepest real area).</summary>
        public const int SiteSteepPct = 25, SiteVerySteepPct = 45, SiteBeyondPct = 58;

        /// <summary>Mean grade (0.25 = 25 %) as the whole percent the player reads. -1 for NaN, infinity or a negative value.</summary>
        public static int SitePercent(float meanGrade) =>
            float.IsNaN(meanGrade) || float.IsInfinity(meanGrade) || meanGrade < 0f ? -1 : (int)Math.Round(meanGrade * 100f, MidpointRounding.AwayFromZero);

        /// <summary>The tier of a whole percent (the number shown, so the label and the figure never disagree): under 25 Gentle, 25-44
        /// Steep, 45-57 Very steep, 58 and over Beyond. A negative value = Unknown.</summary>
        public static SiteTier SiteTierOf(int pct) =>
            pct < 0 ? SiteTier.Unknown : pct < SiteSteepPct ? SiteTier.Gentle : pct < SiteVerySteepPct ? SiteTier.Steep
            : pct < SiteBeyondPct ? SiteTier.VerySteep : SiteTier.Beyond;

        /// <summary>The locale key of a tier (tooltip prefix, which the hub mirrors in hubText.ts). null for Unknown.</summary>
        public static string SiteKey(SiteTier tier) => tier == SiteTier.Unknown ? null : TipPrefix + "SITE[" + tier + "]";

        /// <summary>Estimated mean grade of the ring before any scan. It samples a grid of at most maxSamples points inside the ring (at
        /// least 10 m apart). Each sample is the gradient magnitude of the height over a baselineM window (central differences, the same 20
        /// m window the planner rates with), capped at PlanGeometry.MeanGradeCap. The result is NaN when no sample falls inside (a
        /// sliver) or the ring has fewer than 3 corners.</summary>
        public static float EstimateMeanGrade(float2[] ring, Func<float2, float> height, int maxSamples = 300, float baselineM = 20f)
        {
            if (ring == null || ring.Length < 3 || height == null)
                return float.NaN;
            float2 lo = ring[0], hi = ring[0];
            for (int i = 1; i < ring.Length; i++)
            {
                lo = math.min(lo, ring[i]);
                hi = math.max(hi, ring[i]);
            }
            double area = PlanGeometry.PolygonArea(ring);
            if (!(area > 0.0))
                return float.NaN;
            float step = math.max(10f, (float)Math.Sqrt(area / Math.Max(1, maxSamples)));
            float h = 0.5f * baselineM;
            double sum = 0.0;
            int n = 0;
            for (float z = lo.y + 0.5f * step; z < hi.y; z += step)
                for (float x = lo.x + 0.5f * step; x < hi.x; x += step)
                {
                    if (!InsideRing(ring, new float2(x, z)))
                        continue;
                    float gx = (height(new float2(x + h, z)) - height(new float2(x - h, z))) / baselineM;
                    float gz = (height(new float2(x, z + h)) - height(new float2(x, z - h))) / baselineM;
                    sum += Math.Min(Math.Sqrt(gx * gx + gz * gz), PlanGeometry.MeanGradeCap);
                    n++;
                }
            return n > 0 ? (float)(sum / n) : float.NaN;
        }

        private static bool InsideRing(float2[] ring, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].y > p.y) != (ring[j].y > p.y) &&
                    p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>The area-weighted centroid of a ring (xz, either winding, no closing duplicate needed). It is the sign check's point.
        /// It is nearer the bind's Geometry.m_CenterPosition than a vertex average when one end of a run is a narrow tongue (CRITIQUE F3).
        /// A ring of (nearly) zero area gives the vertex average. A null or empty ring gives 0.</summary>
        public static float2 Centroid(float2[] ring)
        {
            if (ring == null || ring.Length == 0)
                return 0f;
            double2 o = ring[0], s = 0.0;
            double a2 = 0.0;
            for (int i = 0; i < ring.Length; i++)
            {
                double2 p = (double2)ring[i] - o, q = (double2)ring[i + 1 < ring.Length ? i + 1 : 0] - o;
                double cross = p.x * q.y - q.x * p.y;
                a2 += cross;
                s += (p + q) * cross;
            }
            if (math.abs(a2) < 1e-6)
            {
                double2 avg = 0.0;
                for (int i = 0; i < ring.Length; i++)
                    avg += ring[i];
                return (float2)(avg / ring.Length);
            }
            return (float2)(o + s / (3.0 * a2));
        }

        // ---------------------------------------------------------------- game checks

        /// <summary>The game-checks state. overlapPrefabs = the OverlapExisting error prefabs found (ToolErrorData.m_Error).
        /// overlapEnabledInGame = those without ToolErrorFlags.DisableInGame. The prefab reason wins over ignoreErrors (it is the one that
        /// hides the errors themselves).</summary>
        public static QuickGameChecks ChecksState(bool ignoreErrors, int overlapPrefabs, int overlapEnabledInGame)
        {
            if (overlapPrefabs <= 0)
                return QuickGameChecks.NoOverlapPrefab;
            if (overlapEnabledInGame <= 0)
                return QuickGameChecks.OverlapDisabled;
            return ignoreErrors ? QuickGameChecks.IgnoreErrors : QuickGameChecks.On;
        }

        public static bool ChecksOff(QuickGameChecks state) => state != QuickGameChecks.On;

        // ---------------------------------------------------------------- fees, skiers, totals

        /// <summary>The one-time fee the bind will charge (SkiRunFees.Fee, the call SkiRunOwnershipSystem.RunFeeFor makes on the plan area).</summary>
        public static int FeeOf(RunKind kind, float areaM2) => Math.Max(0, SkiRunFees.Fee((int)kind, areaM2));

        /// <summary>Skiers a day the run holds once served and open (SkiResortScore.RunSkiersPerDay, counted, factors 1).</summary>
        public static double SkiersOf(RunKind kind, SlopeBand band, float areaM2)
        {
            var run = new SkiResortScore.RunInput
            {
                Kind = (byte)kind, State = SkiResortScore.BandState.Rated, Band = (byte)band, AreaM2 = areaM2, Served = true, OpenFactor = 1.0,
            };
            var part = new SkiResortScore.RunPart { Counted = band != SlopeBand.Flat, ServedFactor = 1.0, OpenFactor = 1.0 };
            return SkiResortScore.RunSkiersPerDay(run, part);
        }

        /// <summary>Totals over the enabled candidates (enabled[i] for result.Runs[i]). A null or short array counts a run as enabled.
        /// Variety = the score's rule (green, blue and black or double black, SkiResortScore.cs:407).</summary>
        public static void Sum(AutoPlanResult result, bool[] enabled, QuickTotals t)
        {
            t.Clear();
            if (result == null)
                return;
            for (int b = 0; b < 5; b++)
                t.Target[b] = result.BandTarget[b];
            t.Runs = result.Runs.Count;
            for (int i = 0; i < result.Runs.Count; i++)
            {
                AutoRun run = result.Runs[i];
                if (run == null || (enabled != null && i < enabled.Length && !enabled[i]))
                    continue;
                t.Enabled++;
                int band = (int)run.Band;
                if (band >= 0 && band < 5)
                    t.Count[band]++;
                int kind = (int)run.Kind;
                if (kind >= 0 && kind < t.Kinds.Length)
                    t.Kinds[kind]++;
                t.Fee += FeeOf(run.Kind, run.AreaM2);
                t.SkiersPerDay += SkiersOf(run.Kind, run.Band, run.AreaM2);
                t.AreaM2 += run.AreaM2;
            }
            t.Variety = t.Count[(int)SlopeBand.Green] > 0 && t.Count[(int)SlopeBand.Blue] > 0 &&
                        t.Count[(int)SlopeBand.Black] + t.Count[(int)SlopeBand.DoubleBlack] > 0;
        }

        /// <summary>Enter is allowed only when the city can pay the whole batch (PlayerMoney.money: unlimited money reads 2e9,
        /// Game.City/PlayerMoney.cs:15-24).</summary>
        public static bool CanPay(long money, long fee) => fee <= 0 || money >= fee;

        /// <summary>What an undo pays back for one run: exactly what its binding recorded as paid. It pays back nothing for a restored
        /// (free) link, an unpaid run or a run that has no binding yet (it was never charged). paid / restored = SkiRunBinding FlagPaid /
        /// FlagRestored (the caller decodes them: SkiRunBinding is an ECS type, this file is pure).</summary>
        public static long RefundOf(bool hasBinding, bool paid, bool restored, int paidAmount)
        {
            if (!hasBinding || !paid || restored || paidAmount <= 0)
                return 0;
            return paidAmount;
        }

        // ---------------------------------------------------------------- emission

        /// <summary>After validation: no refusal -> Apply. Refusals with survivors and no retry yet -> Retry (drop the refused, emit the
        /// rest once more). Otherwise GiveUp (build nothing, say which).</summary>
        public static QuickBuildStep AfterValidation(int emitted, int refused, bool retried)
        {
            if (emitted <= 0)
                return QuickBuildStep.GiveUp;
            if (refused <= 0)
                return QuickBuildStep.Apply;
            if (!retried && refused < emitted)
                return QuickBuildStep.Retry;
            return QuickBuildStep.GiveUp;
        }

        /// <summary>Index of the emitted ring a Temp area's node ring belongs to, or -1. Geometry only (skeptic finding 6: the engine
        /// recycles Temps by key, so an entity never identifies a candidate across emissions). A match needs the same node count (a
        /// trailing closing duplicate on either side is ignored). Every node must be within MatchToleranceM of the emitted node at the same
        /// position, for some rotation of the start node and either direction. Ties go to the lowest index.</summary>
        public static int MatchRing(float2[] temp, IReadOnlyList<float2[]> emitted)
        {
            if (temp == null || emitted == null)
                return -1;
            int tn = OpenLength(temp);
            if (tn < 3)
                return -1;
            float tol2 = MatchToleranceM * MatchToleranceM;
            for (int k = 0; k < emitted.Count; k++)
            {
                float2[] e = emitted[k];
                if (e == null || OpenLength(e) != tn)
                    continue;
                if (SameRing(temp, e, tn, tol2))
                    return k;
            }
            return -1;
        }

        private static int OpenLength(float2[] ring)
        {
            int n = ring.Length;
            if (n > 1 && math.distancesq(ring[0], ring[n - 1]) <= 1e-6f)
                n--;
            return n;
        }

        private static bool SameRing(float2[] a, float2[] b, int n, float tol2)
        {
            for (int shift = 0; shift < n; shift++)
            {
                if (math.distancesq(a[0], b[shift]) > tol2)
                    continue;
                for (int dir = 1; dir >= -1; dir -= 2)
                {
                    bool all = true;
                    for (int i = 1; i < n && all; i++)
                        all = math.distancesq(a[i], b[((shift + dir * i) % n + n) % n]) <= tol2;
                    if (all)
                        return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- preview input

        /// <summary>The candidate a click at p picks: the smallest ring containing p, otherwise the nearest outline within PickM. -1 =
        /// none.</summary>
        public static int Pick(float2 p, IReadOnlyList<AutoRun> runs)
        {
            if (runs == null)
                return -1;
            int best = -1;
            float bestArea = float.PositiveInfinity;
            for (int i = 0; i < runs.Count; i++)
            {
                AutoRun r = runs[i];
                if (r?.Ring == null || r.Ring.Length < 3 || !GradeRaster.Inside(r.Ring, p))
                    continue;
                if (r.AreaM2 < bestArea)
                {
                    bestArea = r.AreaM2;
                    best = i;
                }
            }
            if (best >= 0)
                return best;
            float bestD = PickM;
            for (int i = 0; i < runs.Count; i++)
            {
                AutoRun r = runs[i];
                if (r?.Ring == null || r.Ring.Length < 3)
                    continue;
                float d = PlanGeometry.PointRingDistance(r.Ring, p);
                if (d <= bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- locale keys

        /// <summary>The refusal line of a planner status (null for Running / Ok).</summary>
        public static string StatusKey(AutoPlanStatus s)
        {
            switch (s)
            {
                case AutoPlanStatus.Running:
                case AutoPlanStatus.Ok:
                    return null;
                default:
                    return TipPrefix + "STATUS[" + s + "]";
            }
        }

        /// <summary>A scan that found nothing with unowned tiles in the region. Those tiles count as obstacles, so the plain reason ("too
        /// little vertical") would blame a hill the city just does not own yet.</summary>
        public static string StatusKey(AutoPlanStatus s, bool regionHasUnownedTiles)
        {
            bool sayTiles = regionHasUnownedTiles &&
                            (s == AutoPlanStatus.NoVertical || s == AutoPlanStatus.TooFlat || s == AutoPlanStatus.NoRuns);
            return sayTiles ? TipPrefix + "STATUS[OutsideTiles]" : StatusKey(s);
        }

        /// <summary>Warning keys in display order (planner warnings first, by bit, then the tool's). Clears and fills keys.</summary>
        public static void WarningKeys(AutoPlanWarning planner, QuickToolWarning tool, List<string> keys)
        {
            keys.Clear();
            for (int bit = 0; bit < 16; bit++)
            {
                var w = (AutoPlanWarning)(1 << bit);
                if ((planner & w) != 0 && Enum.IsDefined(typeof(AutoPlanWarning), w))
                    keys.Add(TipPrefix + "WARN[" + w + "]");
            }
            for (int bit = 0; bit < 8; bit++)
            {
                var w = (QuickToolWarning)(1 << bit);
                if ((tool & w) != 0 && Enum.IsDefined(typeof(QuickToolWarning), w))
                    keys.Add(TipPrefix + "WARN[" + w + "]");
            }
        }

        // ---------------------------------------------------------------- knobs (the hub sends ints)

        public static AutoMix ClampMix(int v) => v >= 0 && v <= (int)AutoMix.Terrain ? (AutoMix)v : AutoMix.Balanced;

        public static AutoDensity ClampDensity(int v) => v >= 0 && v <= (int)AutoDensity.Dense ? (AutoDensity)v : AutoDensity.Normal;

        /// <summary>Kinds bit set. Since run types R3 (DECISIONS 578) Groomed is always on. The hub's three feature chips are the other
        /// bits (Natural = leave black runs natural, Snowmaking = snowmaking near the base, Night = light the two lowest).</summary>
        public static AutoKinds ClampKinds(int v) => (AutoKinds)(v & (int)AllKinds) | AutoKinds.Groomed;

        /// <summary>Flip one feature chip. Groomed cannot be turned off.</summary>
        public static AutoKinds ToggleKind(AutoKinds kinds, AutoKinds kind) => ClampKinds((int)(ClampKinds((int)kinds) ^ (kind & AllKinds & ~AutoKinds.Groomed)));

        public const AutoKinds AllKinds = AutoKinds.Groomed | AutoKinds.Natural | AutoKinds.Snowmaking | AutoKinds.Night;

        /// <summary>The planner's reject histogram in the player's words (bindings.ts QuickRunsState.rejects, same order): 0 too short
        /// (TooShort, MergeStub), 1 overlaps other runs (TopTaken, Overlap), 2 too close to the region edge (Region), 3 blocked by
        /// other areas (Obstacle), 4 too narrow (Pinched), 5 too twisted (Crosses), 6 too flat (FlatLine, Flat), 7 other (Unrated,
        /// RatingCap). Duplicate (the same centerline twice) and Kept are not drops.</summary>
        public const int RejectBuckets = 8;

        public static int RejectBucket(AutoReject r)
        {
            switch (r)
            {
                case AutoReject.TooShort:
                case AutoReject.MergeStub: return 0;
                case AutoReject.TopTaken:
                case AutoReject.Overlap:
                case AutoReject.PodFull: return 1;   // Resort fans: the pod already holds its developable share of runs
                case AutoReject.Region: return 2;
                case AutoReject.Obstacle: return 3;
                case AutoReject.Pinched: return 4;
                case AutoReject.Crosses: return 5;
                case AutoReject.FlatLine:
                case AutoReject.Flat: return 6;
                case AutoReject.Unrated:
                case AutoReject.RatingCap: return 7;
                default: return -1;
            }
        }

        /// <summary>Sums the histogram into the RejectBuckets buckets (a null or short input gives zeros).</summary>
        public static int[] RejectCounts(int[] rejects)
        {
            var counts = new int[RejectBuckets];
            for (int i = 1; rejects != null && i < rejects.Length && i < (int)AutoReject.Count; i++)
            {
                int b = RejectBucket((AutoReject)i);
                if (b >= 0 && rejects[i] > 0)
                    counts[b] += rejects[i];
            }
            return counts;
        }

        /// <summary>The tree-line slider's value: clamped to [groundMin, groundMax + TreeLineStepM] (one step above the region's top =
        /// no tree line inside it), NaN to groundMax + TreeLineStepM.</summary>
        public static float ClampTreeLine(float h, float groundMin, float groundMax)
        {
            float hi = groundMax + TreeLineStepM;
            if (float.IsNaN(h))
                return hi;
            return math.clamp(h, groundMin, hi);
        }
    }
}
