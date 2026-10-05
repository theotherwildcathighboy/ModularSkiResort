// LogWindow — wall-clock folding for flood-prone log lines (Round F6, item D3; docs/research/field/
// 2026-10-03b/findings/log-audit.md A2 / defect 3: `Snow [topup]` was 1,275 of 2,787 lines (46 %),
// up to 203 a minute at 4×, with `Upkeep [charge]` (126) and `Surface:` (183) behind it). Created
// 2026-10-03 (Round F6, implementer D).
//
// THE RULE (contract D3): at most one line per WindowS of wall clock, and the first event of a burst
// is never delayed.
//   * Offer(event): when nothing is folded and no line was written for WindowS (or the caller forces
//     it), the verdict is Plain — the caller writes its usual line at once. Otherwise the event is
//     FOLDED: counted, with min / max / sum / last of up to Slots numbers the caller picks, and its
//     full text kept as the "last" text. When the window has passed (or the event is forced) the
//     verdict is Summary: the caller writes ONE line — its prefix, the fold, and the last event's
//     text — and calls Wrote.
//   * TrailingDue: the caller asks it on every pass that offered nothing, so the end of a burst is
//     written at most WindowS after the last plain or summary line while its system runs ("silence
//     impossible"). A paused simulation holds the fold until the next pass; the callers also flush
//     before an idle line, in OnGamePreload (a load) and in OnDestroy (quit to desktop raises no
//     onGamePreload: TerminateGame disposes the mods, then the world, whose systems' OnDestroy runs
//     while the logger is still alive — GameManager.cs:792-793 vs LogManager.ReleaseResources at
//     :805; 1.6.2 :802-803 / :815), so a fold is lost only on a crash. (F6 fixer: OnDestroy.)
//   * QuietFor: the callers' unchanged-value heartbeat is wall clock (F6 fixer, after D4's
//     CensusHeartbeatS): an unchanged pass is offered only when no line was written for that long.
//   * Wall clock = UnityEngine.Time.realtimeSinceStartup, passed in by the caller: this class is pure
//     (no Unity or Entities types), field arithmetic per event, no allocation of its own.

using System;
using System.Globalization;

namespace SkiResortMod.Systems
{
    internal sealed class LogWindow
    {
        internal enum Verdict : byte
        {
            /// <summary>Folded; write nothing now.</summary>
            Fold,
            /// <summary>Write the plain line now (first event after a quiet window, or forced).</summary>
            Plain,
            /// <summary>Write one summary line now (Count, the slots, LastText), then call Wrote.</summary>
            Summary,
        }

        /// <summary>Numbers tracked per folded event (min / max / sum / last each).</summary>
        public const int Slots = 3;

        public readonly float WindowS;

        private float m_LastLineAt = float.NegativeInfinity;
        private float m_FirstFoldAt;
        private int m_Count;
        private string m_LastText;
        private readonly double[] m_Min = new double[Slots];
        private readonly double[] m_Max = new double[Slots];
        private readonly double[] m_Sum = new double[Slots];
        private readonly double[] m_Last = new double[Slots];

        public LogWindow(float windowS)
        {
            WindowS = windowS;
        }

        /// <summary>Events folded since the last written line (0 = nothing waiting).</summary>
        public int Count => m_Count;

        /// <summary>The full text of the last folded event (null when nothing is folded).</summary>
        public string LastText => m_LastText;

        public double Min(int slot) => m_Min[slot];
        public double Max(int slot) => m_Max[slot];
        public double Sum(int slot) => m_Sum[slot];
        public double Last(int slot) => m_Last[slot];

        /// <summary>Seconds from the first folded event to <paramref name="now"/> (0 when nothing is folded).</summary>
        public float SpanS(float now)
        {
            return m_Count > 0 ? Math.Max(0f, now - m_FirstFoldAt) : 0f;
        }

        /// <summary>"a" when min = max, else "a-b", in <paramref name="format"/> (invariant culture).</summary>
        public string Range(int slot, string format)
        {
            string lo = m_Min[slot].ToString(format, CultureInfo.InvariantCulture);
            string hi = m_Max[slot].ToString(format, CultureInfo.InvariantCulture);
            return lo == hi ? lo : lo + "-" + hi;
        }

        /// <summary>Forget everything (a new load): the next event is written plain.</summary>
        public void Reset()
        {
            m_LastLineAt = float.NegativeInfinity;
            m_Count = 0;
            m_LastText = null;
        }

        /// <summary>One would-log event (header THE RULE). <paramref name="text"/> is the event's full
        /// line body, kept only when the event is folded.</summary>
        public Verdict Offer(float now, bool force, string text, double v0 = 0.0, double v1 = 0.0, double v2 = 0.0)
        {
            bool windowPassed = !(now - m_LastLineAt < WindowS);
            if (m_Count == 0 && (force || windowPassed))
            {
                m_LastLineAt = now;
                return Verdict.Plain;
            }
            Fold(0, v0);
            Fold(1, v1);
            Fold(2, v2);
            if (m_Count == 0)
                m_FirstFoldAt = now;
            m_Count++;
            m_LastText = text;
            return force || windowPassed ? Verdict.Summary : Verdict.Fold;
        }

        /// <summary>True when no plain or summary line was written for <paramref name="seconds"/> of wall
        /// clock (always true before the first line): the unchanged-value heartbeat test (header QuietFor).</summary>
        public bool QuietFor(float now, float seconds)
        {
            return !(now - m_LastLineAt < seconds);
        }

        /// <summary>True when folded events wait and the window since the last line has passed: the
        /// caller writes one summary line now, then calls Wrote.</summary>
        public bool TrailingDue(float now)
        {
            return m_Count > 0 && !(now - m_LastLineAt < WindowS);
        }

        /// <summary>A plain or summary line was written at <paramref name="now"/>: the fold is cleared
        /// and the window restarts.</summary>
        public void Wrote(float now)
        {
            m_LastLineAt = now;
            m_Count = 0;
            m_LastText = null;
        }

        private void Fold(int slot, double v)
        {
            if (m_Count == 0)
            {
                m_Min[slot] = v;
                m_Max[slot] = v;
                m_Sum[slot] = v;
            }
            else
            {
                if (v < m_Min[slot])
                    m_Min[slot] = v;
                if (v > m_Max[slot])
                    m_Max[slot] = v;
                m_Sum[slot] += v;
            }
            m_Last[slot] = v;
        }
    }
}
