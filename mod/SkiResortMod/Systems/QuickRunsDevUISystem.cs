// Dev-only trigger of the Resort Planner tool (port round R2-a, the bridge sheet's cross-runtime gate). One TriggerBinding in the group
// "skiResortDev" (not the hub's contract: bindings.ts never calls it, and tools/check-bundle.js reads only SkiResortHubUISystem.cs):
//   engine.trigger("skiResortDev.quickRuns", "<command>"), commands:
//     "scan <region> [bare]": opens the tool and scans that region. <region> is ft5-r1 .. ft5-r4 (the four field test 5 regions, the
//       host's built-in set and its golden) or the corners "x,z x,z x,z ..." (invariant culture). "bare" plans with no planner input
//       (no blocked ground, as the host's golden plans), so its plan hash must equal the golden line of that region.
//     "dump [stem]": writes the last published scan's exact input and hashes to <user data>\Logs\SkiResortMod.<stem>.rfscan (stem:
//       letters, digits, dash, default "scan"). The test host replays it: dotnet run ... -- rf-replay <file>.
//       Version 2 (port round 2b-f) also holds the game's per-pass prints and its own plan: rf-replay names the first planner pass
//       that differs from the host, rf-game-plans <dir> checks and scores the game's plans.
// It acts only with Options > Diagnostics > Measurement diagnostics on. Otherwise every call is refused with a log line.
// The trigger body only queues; OnUpdate (UIUpdate, main thread) parses and calls the tool (the hub's idiom: the tool acts in its own
// update, QuickRunsToolSystem.DevScan / DevDump).
// Logs (silence impossible): one line per call (accepted, or refused with the reason). An exception disables the trigger until the next
// load.
// Engine notes: EngineNotes/ResortPlanner.md (Runs-first in the tool: the dev path)

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.UI;
using SkiResortMod.Settings;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class QuickRunsDevUISystem : UISystemBase
    {
        private QuickRunsToolSystem m_Tool;
        private readonly List<string> m_Queue = new List<string>();
        private bool m_Disabled, m_ErrorLogged;

        // The field test 5 regions, corners as the mod log printed them (the test host's RunsFirst.Regions, the golden's built-in set)
        private static readonly (string Id, float2[] Ring)[] Regions =
        {
            ("ft5-r1", new[] { P(3372, -1943), P(3462, -3193), P(6386, -2246), P(6244, -1083) }),
            ("ft5-r2", new[] { P(2688, -1711), P(2768, -2269), P(2717, -2714), P(3317, -3708), P(3593, -3839), P(6312, -2292), P(7148, -1835),
                P(6951, -983), P(5793, -1107), P(4905, -1124), P(4047, -1423), P(3703, -1716), P(3358, -1858) }),
            ("ft5-r3", new[] { P(6276, -966), P(5976, 559), P(5020, 1226), P(2076, -686), P(3790, -756), P(4412, -411), P(4942, -362), P(5651, -1175) }),
            ("ft5-r4", new[] { P(-2941, -4929), P(-978, -5499), P(-339, -4233), P(-1945, -3261), P(-2580, -3672) }),
        };

        private static float2 P(float x, float z) => new float2(x, z);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<QuickRunsToolSystem>();
            AddBinding(new TriggerBinding<string>(StationPlaceProbeUISystem.Group, "quickRuns", OnCommand));
            Mod.Log.Info($"{nameof(QuickRunsDevUISystem)} created (trigger {StationPlaceProbeUISystem.Group}.quickRuns, Measurement diagnostics only)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = m_ErrorLogged = false;
            m_Queue.Clear();
        }

        private void OnCommand(string command)
        {
            try
            {
                if (m_Queue.Count < 8)
                    m_Queue.Add(command ?? string.Empty);
                else
                    Mod.Log.Warn($"QuickRuns [dev]: call '{command}' dropped — 8 calls already queued");
            }
            catch (Exception e)
            {
                Fail(e, "trigger");
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (m_Disabled || m_Queue.Count == 0)
                return;
            try
            {
                string[] calls = m_Queue.ToArray();
                m_Queue.Clear();
                foreach (string call in calls)
                    Handle(call.Trim());
            }
            catch (Exception e)
            {
                Fail(e, "OnUpdate");
            }
        }

        private void Handle(string call)
        {
            if (!SkiResortSettings.DiagnosticsOn)
            {
                Mod.Log.Warn($"QuickRuns [dev]: '{call}' REFUSED — Measurement diagnostics is off (Options > Diagnostics)");
                return;
            }
            string[] t = call.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0)
            {
                Mod.Log.Warn("QuickRuns [dev]: empty call REFUSED — expected \"scan <region> [bare]\" or \"dump [stem]\"");
                return;
            }
            if (t[0] == "dump")
            {
                Mod.Log.Info($"QuickRuns [dev]: '{call}' — {m_Tool.DevDump(t.Length > 1 ? t[1] : null)}");
                return;
            }
            if (t[0] != "scan" || t.Length < 2)
            {
                Mod.Log.Warn($"QuickRuns [dev]: '{call}' REFUSED — expected \"scan <ft5-r1..ft5-r4 | x,z x,z x,z ...> [bare]\" or \"dump [stem]\"");
                return;
            }
            bool bare = t[t.Length - 1] == "bare";
            int last = bare ? t.Length - 1 : t.Length;
            string tag = null;
            float2[] ring = null;
            foreach (var (id, r) in Regions)
                if (t[1] == id)
                {
                    tag = id;
                    ring = r;
                }
            if (ring == null)
            {
                var pts = new List<float2>();
                for (int i = 1; i < last; i++)
                {
                    string[] xz = t[i].Split(',');
                    if (xz.Length != 2 || !float.TryParse(xz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                        !float.TryParse(xz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) || !math.isfinite(x) || !math.isfinite(z))
                    {
                        Mod.Log.Warn($"QuickRuns [dev]: '{call}' REFUSED — '{t[i]}' is neither a region name nor a corner \"x,z\"");
                        return;
                    }
                    pts.Add(new float2(x, z));
                }
                ring = pts.ToArray();
                tag = "custom";
            }
            if (!m_Tool.IsActive && !m_Tool.TryActivate("dev trigger"))
            {
                Mod.Log.Warn($"QuickRuns [dev]: '{call}' REFUSED — the tool cannot open (see the activation line above)");
                return;
            }
            Mod.Log.Info($"QuickRuns [dev]: '{call}' — {m_Tool.DevScan(ring, bare, tag)}");
        }

        private void Fail(Exception e, string where)
        {
            m_Disabled = true;
            if (m_ErrorLogged)
                return;
            m_ErrorLogged = true;
            Mod.Log.Error($"!!! {nameof(QuickRunsDevUISystem)} EXCEPTION in {where} — the dev trigger is off until the next load:\n{e}");
        }
    }
}
