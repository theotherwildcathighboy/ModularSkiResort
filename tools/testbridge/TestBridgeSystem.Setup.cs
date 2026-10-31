// Getting a test city ready fast (the same calls as the dev menu's Unlock all, Next MS and Unlimited demands, DebugSystem.cs:4823,
// :4839, :1393), picking a tool by prefab name, reading terrain, and watching for game errors. Tool selection is for setup: a test of
// the hub or a toolbar clicks the real buttons.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.Logging;
using Game.City;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private const int kMaxErrors = 50;
        private const int kMaxHeightCells = 10000;
        private const int kMaxPrefabMatches = 200;

        private readonly object m_ErrorLock = new object();
        private readonly List<JObject> m_Errors = new List<JObject>();
        private int m_ErrorSerial;
        private readonly Dictionary<string, Entity> m_PrefabEntities = new Dictionary<string, Entity>();

        private void WatchErrors()
        {
            UnityLogger.OnException += OnGameException;
            UnityLogger.OnWarnOrHigher += OnGameWarnOrHigher;
        }

        private void UnwatchErrors()
        {
            UnityLogger.OnException -= OnGameException;
            UnityLogger.OnWarnOrHigher -= OnGameWarnOrHigher;
        }

        private void OnGameException(Exception exception, UnityEngine.Object context)
        {
            RecordError("Exception", null, exception?.Message, exception);
        }

        private void OnGameWarnOrHigher(ILog log, Level level, string message, Exception exception, UnityEngine.Object context)
        {
            if (level >= Level.Error)
                RecordError(level.ToString(), log?.name, message, exception);
        }

        // May arrive on a worker thread: only the lock and plain data here.
        private void RecordError(string level, string logger, string message, Exception exception)
        {
            var entry = new JObject
            {
                ["time"] = DateTime.Now.ToString("HH:mm:ss.fff"),
                ["level"] = level,
                ["log"] = logger,
                ["message"] = message,
                ["exception"] = exception?.GetType().Name,
                ["stack"] = exception?.StackTrace?.Split('\n').Take(4).Aggregate("", (a, b) => a + b.Trim() + " | "),
            };
            lock (m_ErrorLock)
            {
                m_ErrorSerial++;
                entry["serial"] = m_ErrorSerial;
                m_Errors.Add(entry);
                if (m_Errors.Count > kMaxErrors)
                    m_Errors.RemoveAt(0);
            }
        }

        private int ErrorSerial()
        {
            lock (m_ErrorLock)
                return m_ErrorSerial;
        }

        private string LastErrorText()
        {
            lock (m_ErrorLock)
                return m_Errors.Count == 0 ? null : $"{m_Errors[m_Errors.Count - 1]["level"]}: {m_Errors[m_Errors.Count - 1]["message"]}";
        }

        /// <summary>{clear: true} empties the list; the serial keeps counting.</summary>
        private JObject Errors(JObject args)
        {
            lock (m_ErrorLock)
            {
                var result = new JObject { ["serial"] = m_ErrorSerial, ["errors"] = new JArray(m_Errors.Select(e => e.DeepClone())) };
                if ((bool?)args["clear"] == true)
                    m_Errors.Clear();
                return result;
            }
        }

        /// <summary>When the deployed DLL was built, so a status shows at once if the game runs an old bridge.</summary>
        private static string BridgeBuild()
        {
            try
            {
                return File.GetLastWriteTime(typeof(Mod).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        /// <summary>{prefab, mode}: the tool a player gets by picking that asset; mode is a NetToolSystem.Mode name, for roads and lines.</summary>
        private JObject ActivateTool(JObject args)
        {
            string name = (string)args["prefab"];
            if (string.IsNullOrEmpty(name))
            {
                m_Tools.ActivatePrefabTool(null);
                return new JObject { ["tool"] = m_Tools.activeTool?.toolID };
            }
            if (!m_Prefabs.TryGetPrefab(FindPrefabEntity(name), out PrefabBase prefab))
                throw new ArgumentException($"prefab '{name}' has no prefab object");
            if (!m_Tools.ActivatePrefabTool(prefab))
                throw new InvalidOperationException($"no tool takes prefab '{name}'");
            if (args["mode"] != null)
            {
                if (!(m_Tools.activeTool is NetToolSystem net))
                    throw new ArgumentException("mode applies only to the net tool");
                net.mode = (NetToolSystem.Mode)Enum.Parse(typeof(NetToolSystem.Mode), (string)args["mode"]);
            }
            return new JObject { ["tool"] = m_Tools.activeTool.toolID, ["prefab"] = m_Tools.activePrefab?.name };
        }

        /// <summary>{match}: prefab names holding this text, any case, with the prefab's class.</summary>
        private JObject FindPrefabs(JObject args)
        {
            RefreshPrefabNames();
            string match = (string)args["match"] ?? "";
            List<string> names = m_PrefabEntities.Keys
                .Where(n => n.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(n => n)
                .ToList();
            var rows = new JArray();
            foreach (string name in names.Take(kMaxPrefabMatches))
            {
                m_Prefabs.TryGetPrefab(m_PrefabEntities[name], out PrefabBase prefab);
                rows.Add(new JArray(name, prefab?.GetType().Name));
            }
            return new JObject { ["count"] = names.Count, ["prefabs"] = rows };
        }

        private Entity FindPrefabEntity(string name)
        {
            if (m_PrefabEntities.TryGetValue(name, out Entity entity) && EntityManager.Exists(entity))
                return entity;
            RefreshPrefabNames();
            if (m_PrefabEntities.TryGetValue(name, out entity))
                return entity;
            throw new ArgumentException($"no prefab named '{name}'; the prefabs op searches by part of the name");
        }

        private void RefreshPrefabNames()
        {
            m_PrefabEntities.Clear();
            EntityQuery query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>());
            try
            {
                using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
                foreach (Entity entity in entities)
                    m_PrefabEntities[m_Prefabs.GetPrefabName(entity)] = entity;
            }
            finally
            {
                query.Dispose();
            }
        }

        private JObject UnlockAll()
        {
            World.GetOrCreateSystemManaged<UnlockAllSystem>().Enabled = true;
            return new JObject();
        }

        /// <summary>Queues the XP for the next milestone, as the dev menu's Next MS does (DebugSystem.cs:5006-5016).</summary>
        private JObject NextMilestone()
        {
            EntityQuery query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<XP>());
            try
            {
                int current = query.GetSingleton<XP>().m_XP;
                int amount = World.GetOrCreateSystemManaged<MilestoneSystem>().nextRequiredXP - current;
                World.GetOrCreateSystemManaged<XPSystem>().GetQueue(out _).Enqueue(new XPGain { reason = XPReason.Unknown, amount = amount });
                return new JObject { ["xp"] = current, ["added"] = amount };
            }
            finally
            {
                query.Dispose();
            }
        }

        private JObject UnlimitedDemand()
        {
            World.GetOrCreateSystemManaged<ResidentialDemandSystem>().SetUnlimitedDemand(unlimited: true);
            World.GetOrCreateSystemManaged<CommercialDemandSystem>().SetUnlimitedDemand(unlimited: true);
            World.GetOrCreateSystemManaged<IndustrialDemandSystem>().SetUnlimitedDemand(unlimited: true);
            return new JObject();
        }

        /// <summary>{x0, z0, x1, z1, step}: a terrain height grid for picking test sites by slope without looking; rows run along x.</summary>
        private JObject Heights(JObject args)
        {
            float x0 = (float)args["x0"];
            float z0 = (float)args["z0"];
            float x1 = (float)args["x1"];
            float z1 = (float)args["z1"];
            float step = (float)args["step"];
            if (step <= 0f || (Math.Floor((x1 - x0) / step) + 1) * (Math.Floor((z1 - z0) / step) + 1) > kMaxHeightCells)
                throw new ArgumentException($"step must be positive and the grid at most {kMaxHeightCells} cells");
            var rows = new JArray();
            float min = float.MaxValue;
            float max = float.MinValue;
            for (float z = z0; z <= z1; z += step)
            {
                var row = new JArray();
                for (float x = x0; x <= x1; x += step)
                {
                    float y = TerrainHeight(x, z);
                    min = Math.Min(min, y);
                    max = Math.Max(max, y);
                    row.Add(Math.Round(y, 1));
                }
                rows.Add(row);
            }
            return new JObject { ["min"] = Math.Round(min, 1), ["max"] = Math.Round(max, 1), ["rows"] = rows };
        }
    }
}
