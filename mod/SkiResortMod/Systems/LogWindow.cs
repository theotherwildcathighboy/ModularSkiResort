// Wall-clock folding for flood-prone log lines: at most one line per WindowS of wall clock, and the first event of a burst is
// never delayed. Pure (no Unity or Entities types); the caller passes UnityEngine.Time.realtimeSinceStartup.
//   * Offer(event): Plain verdict (the caller writes its usual line) when nothing is folded and no line was written for WindowS
//     or the caller forces it; otherwise the event is folded (count, min / max / sum / last of up to Slots numbers, its text)
//     until the window passes, then the verdict is Summary: the caller writes one line and calls Wrote.
//   * TrailingDue: callers ask on every pass that offered nothing, so a burst's end is written within WindowS. A paused sim holds
//     the fold until the next pass; callers also flush in OnGamePreload and OnDestroy (quit to desktop raises no onGamePreload:
//     the world's OnDestroy runs while the logger is alive, GameManager.cs:792-793 vs LogManager.ReleaseResources :805; 1.6.2 :802-815).

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

        /// <summary>One would-log event. <paramref name="text"/> is the event's full line body, kept only when the event is folded.</summary>
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

        /// <summary>True when no plain or summary line was written for <paramref name="seconds"/> of wall clock (always true before the
        /// first line).</summary>
        public bool QuietFor(float now, float seconds)
        {
            return !(now - m_LastLineAt < seconds);
        }

        /// <summary>True when folded events wait and the window since the last line has passed: write one summary line, then call Wrote.</summary>
        public bool TrailingDue(float now)
        {
            return m_Count > 0 && !(now - m_LastLineAt < WindowS);
        }

        /// <summary>A plain or summary line was written at <paramref name="now"/>: clears the fold and restarts the window.</summary>
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
