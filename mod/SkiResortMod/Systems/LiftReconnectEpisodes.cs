// The pure per-station episode state machine behind LiftReconnectSystem's flood fix.
//
// Why: Move It keyboard nudges of an already connected top station changed the station Transform every frame; Transform is part
// of the reconnect's poke signature, so every moved frame poked AND logged (104 log lines, up to 34 per second). Placement itself
// pokes once, which is what makes a line drawn before its station close in the same frame.
//
// Contract (one instance per LiftReconnectSystem; keyed by station EntityRef):
//   * OnChange(...) is called by the system only when a station's signature (foreign-edge set + Transform) differs from the last
//     one it saw. It returns what to do now:
//       - no episode open and no line attached          → nothing;
//       - no episode open and a line attached           → open the episode and poke at once; if the foreign-edge set changed
//         (a line attached) also a full chair pass at once (same-frame closure on placement);
//       - episode open, foreign-edge set changed and a line attached → poke + full pass at once;
//       - episode open, otherwise → poke only when ≥ PokeIntervalFrames frames passed since the last poke; a detached line never pokes.
//   * Tick(frame, settled) is called every frame, whether or not any station changed (it must run before the system's early return
//     on an empty query, else the trailing poke never fires). Every episode whose last change is ≥ SettleFrames old is closed and
//     handed back; the system then does the trailing poke + full pass + the one log line. Tick never pokes on its own.
//   * Remove(station) closes one episode (station deleted); FlushAll closes every episode (game load, the system's catch).
//     The system logs each flushed episode.
//   * Frames are the system's own update count (every frame, paused included); times are wall-clock seconds, used only for the
//     "over N s" in the log line.
//
// Trace of a 34-change drag at 1-frame spacing (frames 0..33, already connected station): leading poke at 0, pokes at 10, 20, 30,
// none for 31..33, settle at 48 → trailing poke + full pass + one line "moved 34× …, 5 poke(s)". tests/LiftBatch checks exactly
// this, plus "attach → immediate full pass".
//
// Constants are tunable and unmeasured; printed in LiftReconnectSystem's ARM line.
// Pure file: System, System.Collections.Generic and Unity.Mathematics only (tests/LiftBatch links it). State is a dictionary of open
// episodes only; no per-frame allocation in steady state (Tick walks the dictionary only while an episode is open).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Per-station reconnect episodes. Pure.</summary>
    public sealed class LiftReconnectEpisodes
    {
        /// <summary>At most one poke per this many frames while changes continue (tunable, unmeasured).</summary>
        public const int PokeIntervalFrames = 10;

        /// <summary>An episode closes after this many frames without a change (tunable, unmeasured).</summary>
        public const int SettleFrames = 15;

        /// <summary>What the caller must do now for one change.</summary>
        public struct Decision
        {
            public bool Poke;       // tag the station's redraw-equivalent set Updated now
            public bool FullPass;   // ChairPinSystem.RequestFullPass now
            public bool Opened;     // this change opened a new episode
        }

        /// <summary>One episode (open, or closed and handed back by Tick / Remove / FlushAll).</summary>
        public struct Episode
        {
            public EntityRef Station;
            public long FirstFrame, LastChangeFrame, LastPokeFrame;
            public double FirstTime, LastChangeTime;
            public int Changes;     // signature changes seen (incl. the opening one)
            public int Moves;       // changes where the Transform moved
            public int Attaches;    // changes where the foreign-edge set changed with a line attached
            public int Detaches;    // changes where the foreign-edge set changed to "no line"
            public int Pokes;       // pokes decided by OnChange (the trailing poke is the caller's)
            public int FullPasses;  // full passes decided by OnChange
            public int PokedNodes, PokedEdges;   // size of the last poke set (NotePokeSet)

            /// <summary>Seconds between the first and the last change (≥ 0).</summary>
            public double SpanSeconds => math.max(0.0, LastChangeTime - FirstTime);
        }

        private readonly Dictionary<EntityRef, Episode> m_Open = new Dictionary<EntityRef, Episode>();
        private readonly List<EntityRef> m_Scratch = new List<EntityRef>();

        /// <summary>Episodes currently open.</summary>
        public int OpenCount => m_Open.Count;

        /// <summary>Lifetime counters (dev tab): episodes opened / closed by Tick.</summary>
        public long Opened { get; private set; }
        public long Settled { get; private set; }

        public bool TryGetOpen(EntityRef station, out Episode episode)
        {
            return m_Open.TryGetValue(station, out episode);
        }

        /// <summary>A signature change of <paramref name="station"/> (see CONTRACT).</summary>
        /// <param name="moved">the Transform differs from the last one seen</param>
        /// <param name="foreignSetChanged">the attached foreign-edge set differs from the last one seen
        /// (first sight with a line attached counts as changed)</param>
        /// <param name="hasForeignEdge">a line (an edge the station does not own) is attached now</param>
        public Decision OnChange(EntityRef station, long frame, double time, bool moved, bool foreignSetChanged,
            bool hasForeignEdge)
        {
            var d = new Decision();
            if (!m_Open.TryGetValue(station, out Episode ep))
            {
                if (!hasForeignEdge)
                    return d;   // nothing attached: nothing to reconnect, no episode, no log
                ep = new Episode
                {
                    Station = station,
                    FirstFrame = frame,
                    LastChangeFrame = frame,
                    LastPokeFrame = frame,
                    FirstTime = time,
                    LastChangeTime = time,
                    Changes = 1,
                    Moves = moved ? 1 : 0,
                    Attaches = foreignSetChanged ? 1 : 0,
                    Pokes = 1,
                    FullPasses = foreignSetChanged ? 1 : 0,
                };
                m_Open[station] = ep;
                Opened++;
                d.Opened = true;
                d.Poke = true;
                d.FullPass = foreignSetChanged;
                return d;
            }

            ep.Changes++;
            ep.LastChangeFrame = frame;
            ep.LastChangeTime = time;
            if (moved)
                ep.Moves++;
            if (foreignSetChanged)
            {
                if (hasForeignEdge) ep.Attaches++;
                else ep.Detaches++;
            }

            if (hasForeignEdge && foreignSetChanged)
            {
                // A line attached mid-episode: reconnect and re-pin at once, as on placement.
                d.Poke = true;
                d.FullPass = true;
            }
            else if (hasForeignEdge && frame - ep.LastPokeFrame >= PokeIntervalFrames)
            {
                d.Poke = true;
            }

            if (d.Poke)
            {
                ep.Pokes++;
                ep.LastPokeFrame = frame;
            }
            if (d.FullPass)
                ep.FullPasses++;
            m_Open[station] = ep;
            return d;
        }

        /// <summary>Records the size of the poke set just applied for an open episode (log line).</summary>
        public void NotePokeSet(EntityRef station, int nodes, int edges)
        {
            if (m_Open.TryGetValue(station, out Episode ep))
            {
                ep.PokedNodes = nodes;
                ep.PokedEdges = edges;
                m_Open[station] = ep;
            }
        }

        /// <summary>Every-frame tick: closes episodes stable for ≥ SettleFrames and appends them to
        /// <paramref name="settled"/> (not cleared here). Allocation-free when nothing is open.</summary>
        public void Tick(long frame, List<Episode> settled)
        {
            if (m_Open.Count == 0)
                return;
            m_Scratch.Clear();
            foreach (var kv in m_Open)
            {
                if (frame - kv.Value.LastChangeFrame >= SettleFrames)
                    m_Scratch.Add(kv.Key);
            }
            for (int i = 0; i < m_Scratch.Count; i++)
            {
                settled.Add(m_Open[m_Scratch[i]]);
                m_Open.Remove(m_Scratch[i]);
                Settled++;
            }
            m_Scratch.Clear();
        }

        /// <summary>Closes the episode of one station (deleted station). False when none was open.</summary>
        public bool Remove(EntityRef station, out Episode episode)
        {
            if (m_Open.TryGetValue(station, out episode))
            {
                m_Open.Remove(station);
                return true;
            }
            return false;
        }

        /// <summary>Closes every open episode (load, exception) and appends them to
        /// <paramref name="closed"/>.</summary>
        public void FlushAll(List<Episode> closed)
        {
            foreach (var kv in m_Open)
                closed.Add(kv.Value);
            m_Open.Clear();
        }
    }
}
