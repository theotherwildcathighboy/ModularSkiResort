// TramShuttleSystem.Debug - the "Tram shuttle" foldout of the "Ski Resort" developer tab.
// This partial carries no [DebugContainer] / [DebugTab]. DebugSystem.AddPanel replaces the widget list stored under a
// tab's name (Game.Debug/DebugSystem.cs:1022, :1085-1095), so a second "Ski Resort" tab would replace ChairPinSystem's rows.
// ChairPinSystem.Debug's BuildSkiResortDebugUI calls AddDebugWidgets after its census foldout and appends AppendDump to
// its DumpDiagnostics.
// Cost: the getters run at 10 Hz while the tab is open and read counters and the first shuttle's run record only (no
// EntityQuery, no job completion). The dump runs on a Button press and writes to the mod log. The kill-switch Button
// flips TramShuttleSystem.MovingShuttles: off parks every cabin in its hosted pose, on lets them follow the clock again.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Rendering;

namespace SkiResortMod.Systems
{
    public sealed partial class TramShuttleSystem
    {
        /// <summary>The "Tram shuttle" foldout for ChairPinSystem's "Ski Resort" tab</summary>
        internal void AddDebugWidgets(List<DebugUI.Widget> into)
        {
            var foldout = new DebugUI.Foldout { displayName = "Tram shuttle (Phase C)", opened = false };
            foldout.children.Add(TramVal("Mode", () => MovingShuttles ? "moving" : "off (every cabin parked)"));
            foldout.children.Add(TramVal("Armed / disabled / last exit", () => $"{IsArmed} / {m_Disabled} / {m_LastExit}"));
            foldout.children.Add(TramVal("Last error", () => m_LastError == null ? "-" : FirstLineOf(m_LastError)));
            foldout.children.Add(TramVal("Census gate", () => m_CensusBuilt ? "built"
                : m_CensusWarned ? "not built: running on the cable path alone (power unknown)"
                : $"waiting ({m_GateWaitFrames} / {CensusGateFrames} frames)"));
            foldout.children.Add(TramVal("Shuttles Running / Held / Parked", () => CountStates()));
            foldout.children.Add(TramVal("Cabins found / riding / parked off a path", () =>
                $"{m_DiagCabinsFound} / {CountRiding()} / {m_ParkedCabins.Count}"));
            foldout.children.Add(TramVal("First shuttle: τ (s) / leg / u", () =>
            {
                if (m_Runs.Count == 0)
                    return "-";
                Run run = m_Runs[0];
                double tau = run.CycleS > 0.0 ? m_LastTimeS - run.CycleS * Math.Floor(m_LastTimeS / run.CycleS) : 0.0;
                return Inv($"{tau:F1} / {run.Leg} / {run.U:F3} ({run.State}{run.StateWhy})");
            }));
            foldout.children.Add(TramVal("First shuttle: L_A / L_B / profile L (m)", () =>
                m_Runs.Count == 0 ? "-" : Inv($"{m_Runs[0].LA:F1} / {m_Runs[0].LB:F1} / {m_Runs[0].L:F2}")));
            foldout.children.Add(TramVal("First shuttle: trip / cycle (s) / power", () =>
                m_Runs.Count == 0 ? "-" : Inv($"{m_Runs[0].TripS:F1} / {m_Runs[0].CycleS:F1} / {m_Runs[0].Power}")));
            foldout.children.Add(TramVal("First shuttle: cabins (A / B) / rule", () =>
                m_Runs.Count == 0 ? "-"
                    : $"{(m_Runs[0].Cabin[0] == Unity.Entities.Entity.Null ? "none" : Ent(m_Runs[0].Cabin[0]))} / " +
                      $"{(m_Runs[0].Cabin[1] == Unity.Entities.Entity.Null ? "none" : Ent(m_Runs[0].Cabin[1]))} / {m_Runs[0].AssignRule}"));
            foldout.children.Add(TramVal("Rebuilds / last reason / frame", () =>
                $"{m_DiagRebuilds} / {m_DiagLastRebuildWhy} / {(m_DiagLastRebuildFrame < 0 ? "-" : m_DiagLastRebuildFrame.ToString())}"));
            foldout.children.Add(TramVal("Probe hits last / total; power reads; paused skips", () =>
                $"{m_DiagProbeHitsLast} / {m_DiagProbeHits}; {m_DiagPowerReads}; {m_DiagPausedSkips}"));
            foldout.children.Add(TramVal("Last pass moved / in place / missing", () =>
                $"{m_DiagLastMoved} / {m_DiagLastInPlace} / {m_DiagLastMissing}"));
            foldout.children.Add(TramVal("Pass µs last / max; rebuild ms last / max", () =>
                Inv($"{m_DiagPassUs:F1} / {m_DiagPassUsMax:F1}; {m_DiagRebuildMs:F2} / {m_DiagRebuildMsMax:F2}")));
            foldout.children.Add(new DebugUI.Button
            {
                displayName = "Tram: toggle moving (kill switch; off parks every cabin)",
                action = () => ToggleMoving("debug tab"),
            });
            foldout.children.Add(new DebugUI.Button
            {
                displayName = "Dump tram shuttle to log",
                action = () => DumpToLog("debug tab"),
            });
            into.Add(foldout);
        }

        /// <summary>The dump block on its own (this foldout's Button). ChairPin's dump appends the same block</summary>
        internal void DumpToLog(string reason)
        {
            var sb = new StringBuilder(1024);
            sb.AppendLine($"{nameof(TramShuttleSystem)} [dump: {reason}] ────────────────────────────");
            AppendDump(sb);
            Mod.Log.Info(sb.ToString().TrimEnd());
        }

        private string CountStates()
        {
            int running = 0, held = 0, parked = 0;
            for (int r = 0; r < m_Runs.Count; r++)
            {
                switch (m_Runs[r].State)
                {
                    case ShuttleState.Running: running++; break;
                    case ShuttleState.Held: held++; break;
                    default: parked++; break;
                }
            }
            return $"{running} / {held} / {parked}";
        }

        private static DebugUI.Value TramVal(string name, Func<object> getter)
        {
            return new DebugUI.Value
            {
                displayName = name,
                getter = () =>
                {
                    try { return getter(); }
                    catch (Exception e) { return "err: " + FirstLineOf(e.ToString()); }
                },
            };
        }

        private static string FirstLineOf(string s)
        {
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl).TrimEnd('\r');
        }
    }
}
