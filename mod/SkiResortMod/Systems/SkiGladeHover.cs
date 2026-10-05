// SkiGladeHover — hover kinds, plan states and the per-activation hover log of the glade tool
// (docs/research/run-binding-glade-spec.md §2 "G2 pure", §4.7; DECISIONS 121 DEFECT 3 → "silence
// must be impossible": one log line per hover-state change, a summary when the tool closes).
// PURE on purpose: usings System, System.Collections.Generic and Unity.Mathematics ONLY, so the
// same file compiles into the net48 mod and is linked into tests/GladeBatch (`hoverlog`).
// Created 2026-10-01 (Round G2, implementer I-2). Nothing here is copied from ParkManager.
//
// WHO DOES WHAT. SkiGladeToolSystem (I-3) resolves the hover every frame into a GladeHoverKind
// and keeps a GladePlanState; it owns one GladeHoverLog and:
//   * calls Begin() in OnStartRunning;
//   * calls Offer(kind, entityIndex, reason) from UpdateHoverStatus; when it returns true the
//     tool writes the transition line (text and engine facts are the tool's, §4.7); when it
//     returns false and CapJustReached is true, it writes the one "transitions capped — totals at
//     tool stop" line — reading CapJustReached RIGHT AFTER that Offer, before any early return
//     (the next Offer clears it and it is raised once per activation; fixer round, g2-tool-1);
//   * calls AddDwell(kind, state, UnityEngine.Time.unscaledDeltaTime) on EVERY UpdateCore path
//     (hold, cancel and apply early returns included, §4.7);
//   * increments Plans / Applies / ViaPoint (plain fields);
//   * at OnStopRunning writes "Glade: " + Summary(lastState) BEFORE ResetPreviewState().
//
// CONTRACT.
//   * Offer dedups on the LAST offered (kind, entityIndex, reason): the same triple twice →
//     one line. Switching (every hotkey activation's first frame has a disabled raycast, §4.7 a)
//     and None are NEVER logged and do not touch the dedup key, so Terrain → Switching → Terrain
//     stays one line. At most MaxLinesPerActivation (40) offers per activation return true;
//     every later transition counts in Suppressed (dedups are not transitions and count
//     nowhere). CapJustReached is true after exactly one Offer per activation: the first
//     suppressed one (it reflects the most recent Offer call).
//   * Dwell: every finite positive `seconds` lands in exactly ONE bucket and in the total, so the
//     buckets always sum to TotalSeconds (up to double rounding; accumulated in double, exposed
//     as float). Non-Bound kinds bucket by kind; Bound buckets by plan state. The summary always
//     prints the eight buckets the spec names (terrain, foreign, unbound, held, nothing,
//     previewing, complete, blocked) and appends clamped / saturated / money short / legacy /
//     switching / other only when non-zero — so the printed buckets also sum to the total.
//   * Allocation: Begin / Offer / AddDwell allocate nothing (they run per frame). Summary
//     allocates (once, at tool stop). Seconds print with one decimal, culture-free.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>What the cursor is on, as the glade tool resolved it (spec §4.7).</summary>
    public enum GladeHoverKind : byte
    {
        None,
        Switching,
        Held,
        Nothing,
        Terrain,
        Foreign,
        Unbound,
        Legacy,
        Bound,
    }

    /// <summary>State of the preview while the hover is Bound (spec §4.6-§4.9).</summary>
    public enum GladePlanState : byte
    {
        None,
        Previewing,
        Complete,
        Clamped,
        Saturated,
        Blocked,
        MoneyShort,
    }

    /// <summary>Per-activation transition log + dwell accounting (header CONTRACT). One
    /// instance per tool.</summary>
    public sealed class GladeHoverLog
    {
        public const int MaxLinesPerActivation = 40;

        // Dwell buckets. The first eight are always printed (spec order).
        public const int BucketTerrain = 0;
        public const int BucketForeign = 1;
        public const int BucketUnbound = 2;
        public const int BucketHeld = 3;
        public const int BucketNothing = 4;
        public const int BucketPreviewing = 5;
        public const int BucketComplete = 6;
        public const int BucketBlocked = 7;
        public const int BucketClamped = 8;
        public const int BucketSaturated = 9;
        public const int BucketMoneyShort = 10;
        public const int BucketLegacy = 11;
        public const int BucketSwitching = 12;
        public const int BucketOther = 13;
        public const int BucketCount = 14;
        private const int AlwaysPrinted = 8;

        /// <summary>Transition lines the tool was told to write / transitions beyond the cap.</summary>
        public int Logged, Suppressed;

        /// <summary>Counters the tool increments (plan lines written, applies, Bound hovers
        /// resolved by the point scan instead of the raycast hit).</summary>
        public int Plans, Applies, ViaPoint;

        /// <summary>Sum of every AddDwell delta since Begin (s).</summary>
        public float TotalSeconds;

        private readonly double[] m_Dwell = new double[BucketCount];
        private double m_Total;
        private bool m_HasLast;
        private GladeHoverKind m_LastKind;
        private int m_LastEntity;
        private byte m_LastReason;
        private bool m_CapReported;
        private bool m_CapJust;

        public GladeHoverLog()
        {
            Begin();
        }

        /// <summary>True after exactly one Offer per activation: the first suppressed one.</summary>
        public bool CapJustReached => m_CapJust;

        /// <summary>Exact (double) total behind TotalSeconds.</summary>
        public double TotalSecondsExact => m_Total;

        /// <summary>Resets everything (OnStartRunning).</summary>
        public void Begin()
        {
            Logged = 0;
            Suppressed = 0;
            Plans = 0;
            Applies = 0;
            ViaPoint = 0;
            TotalSeconds = 0f;
            m_Total = 0.0;
            for (int i = 0; i < m_Dwell.Length; i++)
                m_Dwell[i] = 0.0;
            m_HasLast = false;
            m_LastKind = GladeHoverKind.None;
            m_LastEntity = 0;
            m_LastReason = 0;
            m_CapReported = false;
            m_CapJust = false;
        }

        /// <summary>True = write the transition line now (header CONTRACT).</summary>
        public bool Offer(GladeHoverKind kind, int entityIndex, byte reason)
        {
            m_CapJust = false;
            if (kind == GladeHoverKind.Switching || kind == GladeHoverKind.None)
                return false;
            if (m_HasLast && kind == m_LastKind && entityIndex == m_LastEntity && reason == m_LastReason)
                return false;
            m_HasLast = true;
            m_LastKind = kind;
            m_LastEntity = entityIndex;
            m_LastReason = reason;
            if (Logged < MaxLinesPerActivation)
            {
                Logged++;
                return true;
            }
            Suppressed++;
            if (!m_CapReported)
            {
                m_CapReported = true;
                m_CapJust = true;
            }
            return false;
        }

        /// <summary>Adds a frame's delta to its bucket and the total. Ignores NaN, Inf, &lt;= 0.</summary>
        public void AddDwell(GladeHoverKind kind, GladePlanState state, float seconds)
        {
            if (!(seconds > 0f) || !math.isfinite(seconds))
                return;
            m_Dwell[BucketOf(kind, state)] += seconds;
            m_Total += seconds;
            TotalSeconds = (float)m_Total;
        }

        /// <summary>Seconds accumulated in a bucket (0 for an out-of-range index).</summary>
        public double Dwell(int bucket)
        {
            return bucket >= 0 && bucket < m_Dwell.Length ? m_Dwell[bucket] : 0.0;
        }

        /// <summary>The bucket a (kind, state) frame lands in.</summary>
        public static int BucketOf(GladeHoverKind kind, GladePlanState state)
        {
            switch (kind)
            {
                case GladeHoverKind.Terrain: return BucketTerrain;
                case GladeHoverKind.Foreign: return BucketForeign;
                case GladeHoverKind.Unbound: return BucketUnbound;
                case GladeHoverKind.Legacy: return BucketLegacy;
                case GladeHoverKind.Held: return BucketHeld;
                case GladeHoverKind.Nothing: return BucketNothing;
                case GladeHoverKind.Switching: return BucketSwitching;
                case GladeHoverKind.Bound:
                    switch (state)
                    {
                        case GladePlanState.Previewing: return BucketPreviewing;
                        case GladePlanState.Complete: return BucketComplete;
                        case GladePlanState.Blocked: return BucketBlocked;
                        case GladePlanState.Clamped: return BucketClamped;
                        case GladePlanState.Saturated: return BucketSaturated;
                        case GladePlanState.MoneyShort: return BucketMoneyShort;
                        default: return BucketOther;
                    }
                default: return BucketOther;
            }
        }

        /// <summary>Summary label of a bucket ("terrain", …, "money short", "other").</summary>
        public static string BucketName(int bucket)
        {
            switch (bucket)
            {
                case BucketTerrain: return "terrain";
                case BucketForeign: return "foreign";
                case BucketUnbound: return "unbound";
                case BucketHeld: return "held";
                case BucketNothing: return "nothing";
                case BucketPreviewing: return "previewing";
                case BucketComplete: return "complete";
                case BucketBlocked: return "blocked";
                case BucketClamped: return "clamped";
                case BucketSaturated: return "saturated";
                case BucketMoneyShort: return "money short";
                case BucketLegacy: return "legacy";
                case BucketSwitching: return "switching";
                default: return "other";
            }
        }

        /// <summary>Body of the stop line; the tool prefixes "Glade: ". Format:
        /// "tool stopped after T s — P plan(s), A apply(s); dwell: terrain x s, foreign x s,
        /// unbound x s, held x s, nothing x s, previewing x s, complete x s, blocked x s[, extra
        /// buckets]; L transition(s) logged, S suppressed, V via point test; last: STATE". With
        /// 0 plans the plan count reads "0 plan(s) (no bound run previewed)".</summary>
        public string Summary(string lastState)
        {
            string s = "tool stopped after " + Sec(m_Total) + " s — " + Plans.ToString() + " plan(s)" +
                       (Plans == 0 ? " (no bound run previewed)" : "") + ", " + Applies.ToString() +
                       " apply(s); dwell: ";
            bool first = true;
            for (int b = 0; b < BucketCount; b++)
            {
                if (b >= AlwaysPrinted && !(m_Dwell[b] > 0.0))
                    continue;
                s += (first ? "" : ", ") + BucketName(b) + " " + Sec(m_Dwell[b]) + " s";
                first = false;
            }
            s += "; " + Logged.ToString() + " transition(s) logged, " + Suppressed.ToString() + " suppressed, " +
                 ViaPoint.ToString() + " via point test; last: " + (string.IsNullOrEmpty(lastState) ? "-" : lastState);
            return s;
        }

        /// <summary>One decimal, culture-free ("12.3"); NaN / negative print "0.0".</summary>
        private static string Sec(double seconds)
        {
            if (!(seconds > 0.0))
                return "0.0";
            if (seconds > 1.0e9)
                seconds = 1.0e9;
            long tenths = (long)Math.Floor(seconds * 10.0 + 0.5);
            return (tenths / 10).ToString() + "." + (tenths % 10).ToString();
        }
    }
}
