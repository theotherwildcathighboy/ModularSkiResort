// Measuring performance: frame times from one Stopwatch in OnUpdate, memory, and a switch for the ski mod's systems, for an A/B in one
// session on one camera (README.md, "Measuring performance"). Nothing here allocates per frame. Never `dump` during a window.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Profiling;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private const int PerfCapacity = 216000; // an hour at 60 fps
        private const string ModNamespace = "SkiResortMod";

        private readonly float[] m_PerfMs = new float[PerfCapacity];
        private readonly Stopwatch m_PerfFrame = new Stopwatch();
        private readonly Stopwatch m_PerfWall = new Stopwatch();
        private int m_PerfCount;
        private bool m_PerfOn;
        private uint m_PerfSimStart;
        private int m_PerfGc0Start, m_PerfGc1Start, m_PerfGc2Start;
        private string m_PerfLabel = "";
        private readonly HashSet<ComponentSystemBase> m_PerfSwitchedOff = new HashSet<ComponentSystemBase>();

        private ComponentSystemBase[] m_CostSystems;
        private int m_CostIndex, m_CostSample, m_CostSamples;
        private double[] m_CostSum, m_CostMax;
        private string[] m_CostError;

        /// <summary>Times one extra Update of each mod system in turn, on the bridge's own slot, once per frame. It answers what a run costs on the main thread,
        /// waits for other systems' jobs included. It runs each system more often than the game does: a throwaway state only.</summary>
        private void TickSystemCost()
        {
            if (m_CostSystems == null || m_CostIndex >= m_CostSystems.Length)
                return;
            ComponentSystemBase system = m_CostSystems[m_CostIndex];
            if (system.Enabled && m_CostError[m_CostIndex] == null)
            {
                long t0 = Stopwatch.GetTimestamp();
                try
                {
                    system.Update();
                }
                catch (Exception e)
                {
                    m_CostError[m_CostIndex] = e.GetType().Name;
                }
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                m_CostSum[m_CostIndex] += ms;
                m_CostMax[m_CostIndex] = Math.Max(m_CostMax[m_CostIndex], ms);
            }
            if (++m_CostSample >= m_CostSamples)
            {
                m_CostSample = 0;
                m_CostIndex++;
            }
        }

        private JObject SystemCostStart(JObject args)
        {
            List<ComponentSystemBase> systems = new List<ComponentSystemBase>();
            foreach (ComponentSystemBase system in World.Systems)
            {
                string ns = system.GetType().Namespace;
                if (ns != null && ns.StartsWith(ModNamespace, StringComparison.Ordinal) && system.Enabled)
                    systems.Add(system);
            }
            m_CostSamples = (int?)args["samples"] ?? 64;
            m_CostSum = new double[systems.Count];
            m_CostMax = new double[systems.Count];
            m_CostError = new string[systems.Count];
            m_CostIndex = 0;
            m_CostSample = 0;
            m_CostSystems = systems.ToArray();
            return new JObject { ["systems"] = systems.Count, ["samples"] = m_CostSamples };
        }

        private JObject SystemCostRead()
        {
            if (m_CostSystems == null)
                return new JObject { ["started"] = false };
            int[] order = new int[m_CostSystems.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            Array.Sort(order, (a, b) => m_CostSum[b].CompareTo(m_CostSum[a]));
            JArray rows = new JArray();
            foreach (int i in order)
            {
                int interval = m_CostSystems[i] is Game.GameSystemBase game ? game.GetUpdateInterval(Game.SystemUpdatePhase.GameSimulation) : 1;
                rows.Add(new JArray(m_CostSystems[i].GetType().Name, Math.Round(m_CostSum[i] / m_CostSamples, 3), Math.Round(m_CostMax[i], 3), interval, m_CostError[i]));
            }
            JObject result = new JObject { ["done"] = m_CostIndex >= m_CostSystems.Length, ["at"] = m_CostIndex, ["of"] = m_CostSystems.Length, ["rows"] = rows };
            if (m_CostIndex >= m_CostSystems.Length)
                Mod.Log.Info($"Perf: system cost {result.ToString(Newtonsoft.Json.Formatting.None)}");
            return result;
        }

        private void TickPerf()
        {
            TickSystemCost();
            if (!m_PerfOn)
                return;
            if (m_PerfFrame.IsRunning && m_PerfCount < PerfCapacity)
                m_PerfMs[m_PerfCount++] = (float)m_PerfFrame.Elapsed.TotalMilliseconds;
            m_PerfFrame.Restart();
        }

        private JObject PerfCommand(JObject args)
        {
            string action = (string)args["action"] ?? "read";
            switch (action)
            {
                case "start":
                    m_PerfLabel = (string)args["label"] ?? "";
                    m_PerfCount = 0;
                    m_PerfFrame.Reset();
                    m_PerfWall.Restart();
                    m_PerfSimStart = m_Simulation.frameIndex;
                    m_PerfGc0Start = GC.CollectionCount(0);
                    m_PerfGc1Start = GC.CollectionCount(1);
                    m_PerfGc2Start = GC.CollectionCount(2);
                    m_PerfOn = true;
                    return new JObject { ["started"] = m_PerfLabel };
                case "read":
                    return PerfRead(false);
                case "stop":
                    return PerfRead(true);
                case "memory":
                    return PerfMemory();
                case "modSystems":
                    return ModSystems(args);
                case "systemCost":
                    return SystemCostStart(args);
                case "systemCostRead":
                    return SystemCostRead();
                default:
                    throw new ArgumentException($"unknown perf action '{action}'");
            }
        }

        private JObject PerfRead(bool stop)
        {
            if (stop)
                m_PerfOn = false;
            double wall = m_PerfWall.Elapsed.TotalSeconds;
            int n = m_PerfCount;
            float[] sorted = new float[n];
            Array.Copy(m_PerfMs, sorted, n);
            Array.Sort(sorted);
            double sum = 0;
            for (int i = 0; i < n; i++)
                sum += sorted[i];
            JObject result = new JObject
            {
                ["label"] = m_PerfLabel,
                ["frames"] = n,
                ["seconds"] = Math.Round(wall, 2),
                ["fps"] = n > 0 ? Math.Round(n / (sum / 1000.0), 2) : 0,
                ["meanMs"] = n > 0 ? Math.Round(sum / n, 3) : 0,
                ["p50Ms"] = Percentile(sorted, 0.50),
                ["p95Ms"] = Percentile(sorted, 0.95),
                ["p99Ms"] = Percentile(sorted, 0.99),
                ["maxMs"] = n > 0 ? Math.Round(sorted[n - 1], 3) : 0,
                ["simSpeed"] = m_Simulation.selectedSpeed,
                ["simFramesPerSecond"] = wall > 0 ? Math.Round((m_Simulation.frameIndex - m_PerfSimStart) / wall, 2) : 0,
                ["gc"] = new JArray(GC.CollectionCount(0) - m_PerfGc0Start, GC.CollectionCount(1) - m_PerfGc1Start, GC.CollectionCount(2) - m_PerfGc2Start),
            };
            Mod.Log.Info($"Perf: {result.ToString(Newtonsoft.Json.Formatting.None)}");
            return result;
        }

        private static double Percentile(float[] sorted, double p)
            => sorted.Length == 0 ? 0 : Math.Round(sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * p))], 3);

        private JObject PerfMemory()
        {
            // Process memory reads 0 inside the game (measured): sample it from outside.
            const double Mb = 1024.0 * 1024.0;
            return new JObject
            {
                ["managedMb"] = Math.Round(GC.GetTotalMemory(false) / Mb),
                ["unityAllocatedMb"] = Math.Round(Profiler.GetTotalAllocatedMemoryLong() / Mb),
                ["unityReservedMb"] = Math.Round(Profiler.GetTotalReservedMemoryLong() / Mb),
                ["entities"] = EntityManager.UniversalQuery.CalculateEntityCount(),
                ["vSync"] = QualitySettings.vSyncCount,
                ["targetFrameRate"] = Application.targetFrameRate,
            };
        }

        /// <summary>The whole map: the game's own unlock, for a downloaded city whose mountain lies outside its tiles.</summary>
        private JObject UnlockTiles()
        {
            World.GetOrCreateSystemManaged<Game.Simulation.MapTilePurchaseSystem>().UnlockMapTiles();
            return new JObject();
        }

        /// <summary>Switches every system of the ski mod on or off. For the A half of an A/B only: while off the mod's state goes stale, and the
        /// test save is thrown away afterward.</summary>
        private JObject ModSystems(JObject args)
        {
            bool? enabled = (bool?)args["enabled"];
            string match = (string)args["match"];
            System.Text.RegularExpressions.Regex filter = match != null ? new System.Text.RegularExpressions.Regex(match) : null;
            JArray names = new JArray();
            int count = 0, on = 0;
            foreach (ComponentSystemBase system in World.Systems)
            {
                string ns = system.GetType().Namespace;
                if (ns == null || !ns.StartsWith(ModNamespace, StringComparison.Ordinal))
                    continue;
                count++;
                string name = system.GetType().Name;
                if (filter != null && !filter.IsMatch(name))
                {
                    if (system.Enabled)
                        on++;
                    continue;
                }
                if (system.Enabled)
                    names.Add(name);
                if (enabled == false && system.Enabled)
                {
                    // A system the mod itself keeps off stays off when the switch comes back.
                    m_PerfSwitchedOff.Add(system);
                    system.Enabled = false;
                }
                else if (enabled == true && m_PerfSwitchedOff.Remove(system))
                    system.Enabled = true;
                if (system.Enabled)
                    on++;
            }
            return new JObject { ["modSystems"] = count, ["enabled"] = on, ["names"] = names };
        }
    }
}
