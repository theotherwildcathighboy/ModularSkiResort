// Quick runs: the tool's pure rules (QuickRunsToolSystem). Region readout against the area cap, preview totals (count, mix against the
// planner's target, the fee the binds will charge, skiers), the money check, the refund an undo pays back, the Temp -> candidate match
// by geometry, the click pick, the warning / refusal locale keys and the knob clamps. Pure file: System, Collections and
// Unity.Mathematics only, plus the pure rules it reads (SkiRunFees, SkiResortScore, the planner's geometry; the caller decodes the
// SkiRunBinding flags, an ECS type), so tests/AutoGenBatch links it and proves every rule headless.
// Engine notes: EngineNotes/QuickRunsToolSystem.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The live size line's state (tooltip color): Open = fewer than 3 corners; TooSmall under the planner's minimum area;
    /// Near from NearCapShare of the cap; Over above the cap (Enter refuses).</summary>
    public enum QuickRegionState : byte { Open, TooSmall, Ok, Near, Over }

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
        ChecksOff = 16,       // ToolSystem.ignoreErrors: the game's own checks are off; only the planner's checks protect the build
    }

    /// <summary>What to do after the game validated an emission (all-or-nothing Apply: ToolBaseSystem.cs:533-540).</summary>
    public enum QuickBuildStep : byte { Apply, Retry, GiveUp }

    public static class QuickRunsRules
    {
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

        /// <summary>Area and vertical of the outline drawn so far. minY / maxY = the ground range under the corners (the tool samples
        /// them); the planner measures the real vertical over open ground on Enter.</summary>
        public static QuickRegionReadout Readout(float2[] corners, float minY, float maxY, float maxAreaM2, float minAreaM2)
        {
            var r = new QuickRegionReadout { State = QuickRegionState.Open };
            if (corners == null || corners.Length < 3)
                return r;
            r.AreaM2 = (float)SkiRunAutoPlanner.PolygonArea(corners);
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

        /// <summary>Totals over the enabled candidates (enabled[i] for result.Runs[i]; a null or short array counts a run as enabled).
        /// Variety = the score's rule (green, blue and black or double black; SkiResortScore.cs:407).</summary>
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

        /// <summary>What an undo pays back for one run: exactly what its binding recorded as paid; nothing for a restored (free)
        /// link, an unpaid run or a run that has no binding yet (it was never charged). paid / restored = SkiRunBinding FlagPaid /
        /// FlagRestored (the caller decodes them: SkiRunBinding is an ECS type, this file is pure).</summary>
        public static long RefundOf(bool hasBinding, bool paid, bool restored, int paidAmount)
        {
            if (!hasBinding || !paid || restored || paidAmount <= 0)
                return 0;
            return paidAmount;
        }

        // ---------------------------------------------------------------- emission

        /// <summary>After validation: no refusal -> Apply; refusals with survivors and no retry yet -> Retry (drop the refused, emit the
        /// rest once more); else GiveUp (build nothing, say which).</summary>
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
        /// trailing closing duplicate on either side is ignored) and every node within MatchToleranceM of the emitted node at the same
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

        /// <summary>The candidate a click at p picks: the smallest ring containing p, else the nearest outline within PickM; -1 none.</summary>
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
                float d = SkiRunAutoPlanner.PointRingDistance(r.Ring, p);
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

        /// <summary>Warning keys in display order (planner warnings first, by bit; then the tool's). Clears and fills keys.</summary>
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

        /// <summary>Kinds bit set; at least one kind stays on (an empty set falls back to the default).</summary>
        public static AutoKinds ClampKinds(int v)
        {
            var k = (AutoKinds)(v & (int)AllKinds);
            return k == AutoKinds.None ? AutoKinds.Default : k;
        }

        /// <summary>Flip one kind; refusing to clear the last one.</summary>
        public static AutoKinds ToggleKind(AutoKinds kinds, AutoKinds kind)
        {
            AutoKinds next = ClampKinds((int)kinds) ^ (kind & AllKinds);
            return next == AutoKinds.None ? ClampKinds((int)kinds) : next;
        }

        public const AutoKinds AllKinds = AutoKinds.Groomed | AutoKinds.Natural | AutoKinds.Snowmaking | AutoKinds.Night;

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
