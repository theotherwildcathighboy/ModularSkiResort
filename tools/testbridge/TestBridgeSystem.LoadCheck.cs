// Op "loadcheck": the machine half of the old-save checklist (docs/research/field/old-save-checklist.md, steps a, b, e) as data. The
// runner tools/loadcheck.ps1 drives it: `begin` before the load (log offsets), the load itself through the page helper `__load`
// (the bridge's existing path: the pause menu's real clicks), then `report`, polled with `status`.
//
// The report has three parts:
//   logs     hits since `begin` in Player.log, SceneFlow.log (Unknown prefab ID goes there: Game.Prefabs/ComponentBase.cs:32 baseLog =
//            "SceneFlow", PrefabSystem.cs:966, decompile 1.6.4f1), Modding.log and SkiResortMod.Mod.log. "Duplicate prefab ID" is
//            written while prefabs load at startup (PrefabSystem.cs:154 / :171), so that one pattern is scanned over the whole session.
//   census   entities per persisted type of SkiResortMod's SaveLayouts.All (read by reflection, so a new row is counted without a
//            bridge change), the history buffer's element count, and Ski* buildings, net edges, run areas and carriers by prefab.
//            Carriers that moved over `motionFrames` sim frames count as moving.
//   billing  the treasury, our ledger (SkiGuestCounterSystem.Ledger, this month and last month summed over the signs) and every
//            SkiRunBinding, before and after one in-game month at sim speed `speed`. A lost SkiRunBinding block shows as an
//            "Ownership [restore]" line (the load pass re-links free, SkiRunOwnershipSystem) or, if that defense ever failed, as an
//            "Ownership [bind]" line with a charge for a run that was already in the save (SkiRunBinding.cs header (b)).
// Read-only: the op writes nothing to the city except the sim speed (restored at the end). It reflects into SkiResortMod read-only,
// like `dump` (TestBridgeSystem.Dump.cs). Game types used here are all on tools/game-surface.lock already.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private const int kLcMaxHitsPerPattern = 50;
        private const int kLcLineChars = 400;
        private const int kLcExceptionLookahead = 40;
        private const int kLcSettleFramesAfterMonth = 4096;
        private const double kLcSimStartTimeoutS = 120;
        private const float kLcMovedM = 0.05f;
        private const int kTicksPerMonth = 262144; // TimeSystem.kTicksPerDay (Game.Simulation/TimeSystem.cs:18): one calendar day is one month

        /// <summary>Gated patterns: a hit FAILs part (a) when `ours` matches the line or its context (null = always ours).</summary>
        private static readonly (string name, Regex pattern, bool session, Regex ours)[] s_LcPatterns =
        {
            ("Unknown prefab ID", new Regex("Unknown prefab ID", RegexOptions.IgnoreCase), false, new Regex(@"\bSki")),
            ("Not serializable type", new Regex("Not serializable type", RegexOptions.IgnoreCase), false, new Regex("SkiResortMod")),
            ("Data size mismatch", new Regex("Data size mismatch", RegexOptions.IgnoreCase), false, null),
            ("Serializer type mismatch", new Regex("Serializer type mismatch", RegexOptions.IgnoreCase), false, null),
            ("ComponentSerializerException", new Regex("ComponentSerializerException"), false, null),
            ("Duplicate prefab ID", new Regex("Duplicate prefab ID", RegexOptions.IgnoreCase), true, new Regex(@"\bSki")),
        };

        private static readonly Regex s_LcException = new Regex(@"\w*Exception\b");
        private static readonly Regex s_LcModLogGated = new Regex(@"!!!|\[ERROR\]|\[CRITICAL\]");
        private static readonly Regex s_LcModLogInfo = new Regex(@"History \[load\]|ARMED|load pass|Ownership \[(restore|bind|orphan|rebind|migrate)\]");
        // The load pass summary counts every restore. Per-run [restore] lines stop after 16 per pass (SkiRunOwnershipSystem
        // Tuning.RunLinesPerPassMax).
        private static readonly Regex s_LcLoadPassRestored = new Regex(@"Binding: load pass .*?(\d+) restored free");
        private static readonly Regex s_LcBindCharge = new Regex(@"Ownership \[bind\].*charged (\d+)");
        private static readonly Regex s_LcRestore = new Regex(@"Ownership \[restore\]");
        private static readonly Regex s_LcCarrier = new Regex(@"^Ski(LiftChair|GondolaCabin|TramCabin|TramCarriage)");
        private static readonly Regex s_LcVehicle = new Regex(@"^Ski(GroomerA|CoachBus)$");
        private static readonly Regex s_LcLodge = new Regex(@"^Ski(Lodge|AFrameDayLodge)");

        private sealed class LcLog
        {
            public string name;
            public string path;
            public long offset;
        }

        private sealed class LoadCheckRun
        {
            public string save;
            public DateTime beganUtc;
            public int errorSerial;
            public List<LcLog> logs = new List<LcLog>();
            public bool begun;

            // report state
            public bool running;
            public bool done;
            public string phase;
            public string error;
            public bool month;
            public float speed;
            public int motionFrames;
            public double monthTimeoutS;
            public float speedBefore;
            public double phaseStarted;
            public uint frameMark;
            public int startMonth;
            public bool monthTurned;
            public uint monthTurnedFrame;
            public uint monthStartFrame;
            public double monthStartedAt;
            public Dictionary<Entity, float3> carrierPositions;
            public Dictionary<Entity, string> carrierPrefabs;
            public JObject report;
        }

        private LoadCheckRun m_LoadCheck;
        private readonly Dictionary<Entity, string> m_LcPrefabNames = new Dictionary<Entity, string>();

        // args: action = begin (save: a label for the report) | report (month, default true; speed, default 8; motionFrames, default 300;
        // monthTimeoutMinutes, default 45) | status | census (counts now, no motion, no logs).
        private JObject LoadCheckCommand(JObject args)
        {
            string action = (string)args["action"] ?? "status";
            switch (action)
            {
                case "begin": return LoadCheckBegin(args);
                case "report": return LoadCheckStart(args);
                case "status": return LoadCheckStatus();
                case "census": return new JObject { ["census"] = LcCensus(null, null) };
                default: throw new ArgumentException($"loadcheck: unknown action '{action}' (begin, report, status, census)");
            }
        }

        private JObject LoadCheckBegin(JObject args)
        {
            if (m_LoadCheck != null && m_LoadCheck.running)
                throw new InvalidOperationException("loadcheck: a report is running ({op:\"loadcheck\",action:\"status\"})");
            var run = new LoadCheckRun
            {
                save = (string)args["save"],
                beganUtc = DateTime.UtcNow,
                errorSerial = ErrorSerial(),
                begun = true,
            };
            foreach (LcLog log in LcLogFiles())
            {
                log.offset = LcFileLength(log.path);
                run.logs.Add(log);
            }
            m_LoadCheck = run;
            return new JObject
            {
                ["began"] = run.beganUtc.ToString("o"),
                ["gameMode"] = GameManager.instance.gameMode.ToString(),
                ["logs"] = new JArray(run.logs.Select(l => new JObject { ["name"] = l.name, ["path"] = l.path, ["offset"] = l.offset })),
            };
        }

        private JObject LoadCheckStart(JObject args)
        {
            if (m_LoadCheck != null && m_LoadCheck.running)
                throw new InvalidOperationException("loadcheck: a report is running ({op:\"loadcheck\",action:\"status\"})");
            if (GameManager.instance.gameMode != GameMode.Game || m_City.City == Entity.Null)
                throw new InvalidOperationException($"loadcheck: needs a loaded city (mode {GameManager.instance.gameMode})");
            LoadCheckRun run = m_LoadCheck;
            if (run == null || !run.begun || run.done)
            {
                // No begin since the last report: scan the whole session (the report says so).
                run = new LoadCheckRun { beganUtc = DateTime.UtcNow, errorSerial = 0 };
                foreach (LcLog log in LcLogFiles())
                    run.logs.Add(log);
            }
            run.month = (bool?)args["month"] ?? true;
            run.speed = (float?)args["speed"] ?? 8f;
            run.motionFrames = Math.Max(30, (int?)args["motionFrames"] ?? 300);
            run.monthTimeoutS = 60.0 * ((double?)args["monthTimeoutMinutes"] ?? 45.0);
            run.speedBefore = m_Simulation.selectedSpeed;
            run.running = true;
            run.done = false;
            run.report = new JObject
            {
                ["save"] = run.save,
                ["began"] = run.beganUtc.ToString("o"),
                ["logScope"] = run.begun ? "since begin" : "whole session (no begin)",
                ["gameVersion"] = UnityEngine.Application.version,
                ["modBuild"] = LcModBuild(),
                ["bridgeBuild"] = BridgeBuild(),
            };
            m_LoadCheck = run;
            LcPhase(run, "sim");
            if (m_Simulation.selectedSpeed <= 0f)
                m_Simulation.selectedSpeed = 1f;
            run.frameMark = m_Simulation.frameIndex;
            return LoadCheckStatus();
        }

        private JObject LoadCheckStatus()
        {
            LoadCheckRun run = m_LoadCheck;
            if (run == null)
                return new JObject { ["state"] = "idle" };
            var result = new JObject
            {
                ["state"] = run.done ? (run.error != null ? "failed" : "done") : run.running ? "running" : "begun",
                ["phase"] = run.phase,
                ["frame"] = m_City.City != Entity.Null ? (JToken)m_Simulation.frameIndex : null,
            };
            if (run.error != null)
                result["error"] = run.error;
            if (run.running && run.phase == "month")
            {
                result["date"] = m_Time.GetCurrentDateTime().ToString("yyyy-MM-dd HH:mm");
                result["monthFrames"] = (long)(m_Simulation.frameIndex - run.monthStartFrame);
                result["monthTurned"] = run.monthTurned;
                result["elapsedS"] = Math.Round(Now - run.monthStartedAt, 1);
            }
            if (run.done)
                result["report"] = run.report;
            return result;
        }

        private void LcPhase(LoadCheckRun run, string phase)
        {
            run.phase = phase;
            run.phaseStarted = Now;
            Mod.Log.Info($"loadcheck: phase {phase} at frame {(m_City.City != Entity.Null ? m_Simulation.frameIndex : 0)}");
        }

        private void LcFinish(LoadCheckRun run, string error)
        {
            run.error = error;
            run.running = false;
            run.done = true;
            if (m_City.City != Entity.Null)
                m_Simulation.selectedSpeed = run.speedBefore;
            if (error != null)
                run.report["error"] = error;
            Mod.Log.Info($"loadcheck: {(error == null ? "done" : "failed: " + error)}");
        }

        /// <summary>Called every frame from OnUpdate. Each phase waits on sim frames, never blocks a frame.</summary>
        private void TickLoadCheck()
        {
            LoadCheckRun run = m_LoadCheck;
            if (run == null || !run.running)
                return;
            try
            {
                if (GameManager.instance.gameMode != GameMode.Game || m_City.City == Entity.Null)
                {
                    LcFinish(run, $"the city went away (mode {GameManager.instance.gameMode}) in phase {run.phase}");
                    return;
                }
                uint frame = m_Simulation.frameIndex;
                switch (run.phase)
                {
                    case "sim":
                        // The simulation runs: frames advance after the load.
                        if (frame >= run.frameMark + 64)
                        {
                            run.report["simRunning"] = true;
                            JObject logs = LcScanLogs(run);
                            run.report["logs"] = logs;
                            run.carrierPositions = new Dictionary<Entity, float3>();
                            run.carrierPrefabs = new Dictionary<Entity, string>();
                            run.report["census"] = LcCensus(run.carrierPositions, run.carrierPrefabs);
                            run.report["billingBefore"] = LcBilling();
                            run.frameMark = frame;
                            LcPhase(run, "motion");
                        }
                        else if (Now - run.phaseStarted > kLcSimStartTimeoutS)
                        {
                            run.report["simRunning"] = false;
                            LcFinish(run, $"the simulation did not run within {kLcSimStartTimeoutS} s (frame {frame}, speed {m_Simulation.selectedSpeed})");
                        }
                        break;

                    case "motion":
                        if (frame >= run.frameMark + (uint)run.motionFrames)
                        {
                            ((JObject)run.report["census"])["motion"] = LcMotion(run);
                            if (!run.month)
                            {
                                LcFinishReport(run);
                                return;
                            }
                            run.startMonth = m_Time.GetCurrentDateTime().Month;
                            run.monthStartFrame = frame;
                            run.monthStartedAt = Now;
                            run.monthTurned = false;
                            m_Simulation.selectedSpeed = run.speed;
                            run.report["month"] = new JObject
                            {
                                ["startDate"] = m_Time.GetCurrentDateTime().ToString("yyyy-MM-dd HH:mm"),
                                ["startFrame"] = frame,
                                ["speed"] = run.speed,
                            };
                            LcPhase(run, "month");
                        }
                        else if (Now - run.phaseStarted > kLcSimStartTimeoutS)
                        {
                            LcFinish(run, $"the simulation stopped during the motion sample (frame {frame})");
                        }
                        break;

                    case "month":
                        if (!run.monthTurned && m_Time.GetCurrentDateTime().Month != run.startMonth)
                        {
                            run.monthTurned = true;
                            run.monthTurnedFrame = frame;
                            Mod.Log.Info($"loadcheck: month turned at frame {frame} ({m_Time.GetCurrentDateTime():yyyy-MM-dd HH:mm})");
                        }
                        bool settled = run.monthTurned && frame >= run.monthTurnedFrame + kLcSettleFramesAfterMonth;
                        bool frameCap = frame - run.monthStartFrame > (uint)(kTicksPerMonth * 3 / 2);
                        bool timeUp = Now - run.monthStartedAt > run.monthTimeoutS;
                        if (settled || frameCap || timeUp)
                        {
                            var month = (JObject)run.report["month"];
                            month["endDate"] = m_Time.GetCurrentDateTime().ToString("yyyy-MM-dd HH:mm");
                            month["frames"] = (long)(frame - run.monthStartFrame);
                            month["seconds"] = Math.Round(Now - run.monthStartedAt, 1);
                            month["simFramesPerSecond"] = Math.Round((frame - run.monthStartFrame) / Math.Max(1.0, Now - run.monthStartedAt), 1);
                            month["turned"] = run.monthTurned;
                            if (!run.monthTurned)
                                month["incomplete"] = timeUp ? $"wall-clock limit {run.monthTimeoutS / 60:F0} min" : "frame cap without a month change";
                            m_Simulation.selectedSpeed = run.speedBefore;
                            run.report["billingAfter"] = LcBilling();
                            run.report["censusAfter"] = LcCensus(null, null);
                            run.report["logs"] = LcScanLogs(run);
                            LcFinishReport(run);
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"loadcheck: {e}");
                LcFinish(run, $"{e.GetType().Name}: {e.Message}");
            }
        }

        private void LcFinishReport(LoadCheckRun run)
        {
            JObject parts = new JObject();
            var logs = (JObject)run.report["logs"];
            parts["a"] = new JObject { ["pass"] = (int)logs["gatedHits"] == 0 && (int)logs["gameErrorsOurs"] == 0, ["gatedHits"] = logs["gatedHits"] };
            var census = (JObject)run.report["census"];
            parts["b"] = new JObject { ["zeroTypes"] = census["zeroTypes"], ["unresolvedTypes"] = census["unresolvedTypes"] };
            parts["c"] = run.month ? LcBillingVerdict(run) : new JObject { ["skipped"] = "month: false" };
            run.report["parts"] = parts;
            LcFinish(run, null);
        }

        // ---- (a) logs -------------------------------------------------------------------------------------------------------------

        private static IEnumerable<LcLog> LcLogFiles()
        {
            string data = UnityEngine.Application.persistentDataPath;
            string logs = Path.Combine(data, "Logs");
            string player = null;
            try { player = UnityEngine.Application.consoleLogPath; } catch (Exception) { }
            if (string.IsNullOrEmpty(player))
                player = Path.Combine(data, "Player.log");
            yield return new LcLog { name = "Player.log", path = player };
            yield return new LcLog { name = "SceneFlow.log", path = Path.Combine(logs, "SceneFlow.log") };
            yield return new LcLog { name = "Modding.log", path = Path.Combine(logs, "Modding.log") };
            yield return new LcLog { name = "SkiResortMod.Mod.log", path = Path.Combine(logs, "SkiResortMod.Mod.log") };
        }

        private static long LcFileLength(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; } catch (Exception) { return 0; }
        }

        /// <summary>The lines of a log from byte `offset` to the end, read shared (the game holds the files open). A file shorter than the
        /// offset was rotated: read it whole.</summary>
        private static List<string> LcReadLines(string path, long offset, out bool rotated)
        {
            rotated = false;
            var lines = new List<string>();
            if (!File.Exists(path))
                return lines;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (offset > fs.Length)
                {
                    rotated = true;
                    offset = 0;
                }
                fs.Seek(offset, SeekOrigin.Begin);
                using (var reader = new StreamReader(fs))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        lines.Add(line);
                }
            }
            return lines;
        }

        private static string LcClip(string line)
        {
            return line.Length <= kLcLineChars ? line : line.Substring(0, kLcLineChars) + " ...";
        }

        private JObject LcScanLogs(LoadCheckRun run)
        {
            var files = new JArray();
            var hits = new JArray();
            var modInfo = new JArray();
            int gated = 0;
            var counts = new Dictionary<string, int>();
            int bindCharges = 0, restores = 0, restoredFree = 0;
            long bindChargeSum = 0;
            foreach (LcLog log in run.logs)
            {
                List<string> lines = LcReadLines(log.path, log.offset, out bool rotated);
                files.Add(new JObject
                {
                    ["name"] = log.name,
                    ["path"] = log.path,
                    ["exists"] = File.Exists(log.path),
                    ["fromOffset"] = rotated ? 0 : log.offset,
                    ["rotated"] = rotated,
                    ["lines"] = lines.Count,
                });
                bool modLog = log.name == "SkiResortMod.Mod.log";
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i];
                    foreach (var p in s_LcPatterns)
                    {
                        if (p.session || !p.pattern.IsMatch(line))
                            continue;
                        bool ours = p.ours == null || p.ours.IsMatch(line);
                        LcAddHit(hits, counts, p.name, log.name, i, line, ours, ref gated);
                    }
                    if (s_LcException.IsMatch(line) && !line.Contains("ComponentSerializerException"))
                    {
                        string frame = LcFindOurFrame(lines, i);
                        if (frame != null || (modLog && line.IndexOf("Exception", StringComparison.Ordinal) >= 0))
                        {
                            LcAddHit(hits, counts, "Exception naming SkiResortMod", log.name, i, line, true, ref gated);
                            ((JObject)hits.Last)["frame"] = frame;
                        }
                    }
                    if (modLog)
                    {
                        if (s_LcModLogGated.IsMatch(line))
                            LcAddHit(hits, counts, "mod log error (!!! / ERROR)", log.name, i, line, true, ref gated);
                        Match charge = s_LcBindCharge.Match(line);
                        if (charge.Success)
                        {
                            bindCharges++;
                            bindChargeSum += long.Parse(charge.Groups[1].Value);
                        }
                        if (s_LcRestore.IsMatch(line))
                            restores++;
                        Match loadPass = s_LcLoadPassRestored.Match(line);
                        if (loadPass.Success)
                            restoredFree += int.Parse(loadPass.Groups[1].Value);
                        if (s_LcModLogInfo.IsMatch(line) && modInfo.Count < 60)
                            modInfo.Add(LcClip(line));
                    }
                }
            }
            // Session-wide patterns (startup lines): whole files.
            foreach (var p in s_LcPatterns.Where(p => p.session))
            {
                foreach (LcLog log in run.logs.Where(l => l.name != "SkiResortMod.Mod.log"))
                {
                    List<string> lines = LcReadLines(log.path, 0, out _);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (!p.pattern.IsMatch(lines[i]))
                            continue;
                        bool ours = p.ours == null || p.ours.IsMatch(lines[i]);
                        LcAddHit(hits, counts, p.name + " (session)", log.name, i, lines[i], ours, ref gated);
                    }
                }
            }
            JObject gameErrors = Errors(new JObject());
            var errorsSince = new JArray(((JArray)gameErrors["errors"]).Where(e => (int)e["serial"] > run.errorSerial));
            int errorsOurs = errorsSince.Count(e => e.ToString().Contains("SkiResortMod"));
            return new JObject
            {
                ["files"] = files,
                ["gatedHits"] = gated,
                ["counts"] = JObject.FromObject(counts),
                ["hits"] = hits,
                ["modLogInfo"] = modInfo,
                ["bindCharges"] = bindCharges,
                ["bindChargeSum"] = bindChargeSum,
                ["restores"] = Math.Max(restores, restoredFree),
                ["restoreLines"] = restores,
                ["loadPassRestoredFree"] = restoredFree,
                ["gameErrors"] = errorsSince.Count,
                ["gameErrorsOurs"] = errorsOurs,
                ["gameErrorList"] = new JArray(errorsSince.Take(20)),
            };
        }

        private static void LcAddHit(JArray hits, Dictionary<string, int> counts, string pattern, string file, int index, string line, bool ours,
            ref int gated)
        {
            counts.TryGetValue(pattern, out int n);
            counts[pattern] = n + 1;
            if (ours)
                gated++;
            if (n < kLcMaxHitsPerPattern)
                hits.Add(new JObject { ["pattern"] = pattern, ["file"] = file, ["line"] = index + 1, ["ours"] = ours, ["text"] = LcClip(line) });
        }

        /// <summary>The first stack line naming SkiResortMod in the trace under an exception line (stops at a blank line).</summary>
        private static string LcFindOurFrame(List<string> lines, int index)
        {
            if (lines[index].Contains("SkiResortMod"))
                return LcClip(lines[index].Trim());
            for (int i = index + 1; i < lines.Count && i <= index + kLcExceptionLookahead; i++)
            {
                if (lines[i].Trim().Length == 0)
                    break;
                if (lines[i].Contains("SkiResortMod"))
                    return LcClip(lines[i].Trim());
            }
            return null;
        }

        // ---- (b) census -----------------------------------------------------------------------------------------------------------

        /// <summary>The persisted type names, from SkiResortMod's frozen table (SaveLayouts.All[i].TypeName).</summary>
        private static List<string> LcPersistedTypeNames()
        {
            Type table = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "SkiResortMod")
                .Select(a => a.GetType("SkiResortMod.Systems.SaveLayouts", false)).FirstOrDefault(t => t != null);
            if (table == null)
                throw new InvalidOperationException("SkiResortMod.Systems.SaveLayouts not found: is the ski mod loaded?");
            var rows = (Array)table.GetField("All", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var names = new List<string>();
            foreach (object row in rows)
                names.Add((string)row.GetType().GetField("TypeName").GetValue(row));
            return names;
        }

        private static string LcModBuild()
        {
            Assembly mod = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SkiResortMod");
            if (mod == null)
                return null;
            var info = mod.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return info?.InformationalVersion ?? mod.GetName().Version.ToString();
        }

        private string LcPrefabName(Entity prefab)
        {
            if (!m_LcPrefabNames.TryGetValue(prefab, out string name))
                m_LcPrefabNames[prefab] = name = m_Prefabs.GetPrefabName(prefab);
            return name;
        }

        /// <summary>Counts of live (not Deleted, not Temp) entities. With `positions`, also records every carrier's position for the
        /// motion sample.</summary>
        private JObject LcCensus(Dictionary<Entity, float3> positions, Dictionary<Entity, string> carrierPrefabs)
        {
            m_LcPrefabNames.Clear();
            var persisted = new JObject();
            var zero = new JArray();
            var unresolved = new JArray();
            long historyElements = 0;
            foreach (string typeName in LcPersistedTypeNames())
            {
                Type type;
                try
                {
                    type = FindType("SkiResortMod.Systems." + typeName);
                }
                catch (ArgumentException)
                {
                    unresolved.Add(typeName);
                    persisted[typeName] = null;
                    continue;
                }
                EntityQuery query = EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly(type) },
                    None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
                });
                try
                {
                    int count = query.CalculateEntityCount();
                    persisted[typeName] = count;
                    if (count == 0)
                        zero.Add(typeName);
                    if (typeof(IBufferElementData).IsAssignableFrom(type))
                    {
                        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
                        foreach (Entity e in entities)
                            historyElements += (int?)(ReadComponent(e, type) as JObject)?["length"] ?? 0;
                        persisted[typeName + ".elements"] = historyElements;
                    }
                }
                finally
                {
                    query.Dispose();
                }
            }

            JObject buildings = LcCountByPrefab(ComponentType.ReadOnly<Building>(), n => n.StartsWith("Ski", StringComparison.Ordinal), null, null);
            JObject edges = LcCountByPrefab(ComponentType.ReadOnly<Edge>(), n => n.StartsWith("Ski", StringComparison.Ordinal), null, null);
            JObject runs = LcCountByPrefab(ComponentType.ReadOnly<Area>(), n => n.StartsWith("SkiRun", StringComparison.Ordinal), null, null);
            JObject objects = LcCountByPrefab(ComponentType.ReadOnly<Game.Objects.Transform>(),
                n => s_LcCarrier.IsMatch(n) || s_LcVehicle.IsMatch(n), positions, carrierPrefabs);

            int Sum(JObject o, Func<string, bool> keep) => o.Properties().Where(p => keep(p.Name)).Sum(p => (int)p.Value);
            var summary = new JObject
            {
                ["welcomeSigns"] = Sum(buildings, n => n == "SkiResortGate"),
                ["lodges"] = Sum(buildings, n => s_LcLodge.IsMatch(n)),
                ["stations"] = Sum(buildings, n => n.EndsWith("StationBuilding", StringComparison.Ordinal)),
                ["liftSections"] = Sum(edges, n => Regex.IsMatch(n, "^Ski(Chairlift|Detachable|Gondola|Tram)HV$")),
                ["chairsAndCabins"] = Sum(objects, n => Regex.IsMatch(n, "^Ski(LiftChair|GondolaCabin)") && !n.EndsWith("Grip", StringComparison.Ordinal)),
                ["trams"] = Sum(objects, n => n == "SkiTramCabin"),
                ["paintedRuns"] = Sum(runs, n => n != "SkiRunCatTrackA"),
                ["catTracks"] = Sum(runs, n => n == "SkiRunCatTrackA"),
            };
            return new JObject
            {
                ["persisted"] = persisted,
                ["zeroTypes"] = zero,
                ["unresolvedTypes"] = unresolved,
                ["summary"] = summary,
                ["buildings"] = buildings,
                ["netEdges"] = edges,
                ["runs"] = runs,
                ["objects"] = objects,
            };
        }

        private JObject LcCountByPrefab(ComponentType required, Func<string, bool> keep, Dictionary<Entity, float3> positions,
            Dictionary<Entity, string> prefabsOut)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            EntityQuery query = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { required, ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            try
            {
                using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
                using NativeArray<PrefabRef> refs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                {
                    string name = LcPrefabName(refs[i].m_Prefab);
                    if (name == null || !keep(name))
                        continue;
                    counts.TryGetValue(name, out int n);
                    counts[name] = n + 1;
                    if (positions != null && EntityManager.HasComponent<Game.Objects.Transform>(entities[i]))
                    {
                        positions[entities[i]] = EntityManager.GetComponentData<Game.Objects.Transform>(entities[i]).m_Position;
                        prefabsOut[entities[i]] = name;
                    }
                }
            }
            finally
            {
                query.Dispose();
            }
            var result = new JObject();
            foreach (var kv in counts)
                result[kv.Key] = kv.Value;
            return result;
        }

        private JObject LcMotion(LoadCheckRun run)
        {
            var total = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var moving = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in run.carrierPositions)
            {
                string name = run.carrierPrefabs[kv.Key];
                total.TryGetValue(name, out int t);
                total[name] = t + 1;
                if (!EntityManager.Exists(kv.Key) || !EntityManager.HasComponent<Game.Objects.Transform>(kv.Key))
                    continue;
                float3 now = EntityManager.GetComponentData<Game.Objects.Transform>(kv.Key).m_Position;
                if (math.distance(now, kv.Value) > kLcMovedM)
                {
                    moving.TryGetValue(name, out int m);
                    moving[name] = m + 1;
                }
            }
            var byPrefab = new JObject();
            foreach (var kv in total)
            {
                moving.TryGetValue(kv.Key, out int m);
                byPrefab[kv.Key] = new JObject { ["count"] = kv.Value, ["moving"] = m };
            }
            int chairsMoving = moving.Where(kv => Regex.IsMatch(kv.Key, "^Ski(LiftChair|GondolaCabin)") && !kv.Key.EndsWith("Grip", StringComparison.Ordinal))
                .Sum(kv => kv.Value);
            moving.TryGetValue("SkiTramCabin", out int tramsMoving);
            return new JObject
            {
                ["frames"] = run.motionFrames,
                ["time"] = m_Time.GetCurrentDateTime().ToString("yyyy-MM-dd HH:mm"),
                ["chairsMoving"] = chairsMoving,
                ["tramsMoving"] = tramsMoving,
                ["byPrefab"] = byPrefab,
            };
        }

        // ---- (c) billing ----------------------------------------------------------------------------------------------------------

        private JObject LcBilling()
        {
            var result = new JObject
            {
                ["date"] = m_Time.GetCurrentDateTime().ToString("yyyy-MM-dd HH:mm"),
                ["frame"] = m_Simulation.frameIndex,
                ["money"] = EntityManager.GetComponentData<PlayerMoney>(m_City.City).money,
            };
            try
            {
                result["ledger"] = LcLedger();
            }
            catch (Exception e)
            {
                result["ledger"] = $"(read failed: {(e.InnerException ?? e).Message})";
            }
            result["bindings"] = LcBindings();
            return result;
        }

        /// <summary>SkiGuestCounterSystem.Ledger (internal, read by reflection): the month key and the numeric fields of This and Last summed
        /// over the signs (SkiResortLedger.Entry, Month).</summary>
        private JObject LcLedger()
        {
            Type systemType = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "SkiResortMod")
                .Select(a => a.GetType("SkiResortMod.Systems.SkiGuestCounterSystem", false)).FirstOrDefault(t => t != null)
                ?? throw new InvalidOperationException("SkiGuestCounterSystem not found");
            object system = World.GetExistingSystemManaged(systemType) ?? throw new InvalidOperationException("SkiGuestCounterSystem not in the world");
            object ledger = systemType.GetProperty("Ledger", kFields).GetValue(system);
            Type ledgerType = ledger.GetType();
            int count = (int)ledgerType.GetProperty("Count").GetValue(ledger);
            MethodInfo at = ledgerType.GetMethod("At");
            var thisMonth = new Dictionary<string, double>();
            var lastMonth = new Dictionary<string, double>();
            for (int i = 0; i < count; i++)
            {
                object entry = at.Invoke(ledger, new object[] { i });
                LcAddNumbers(thisMonth, entry.GetType().GetField("This").GetValue(entry));
                LcAddNumbers(lastMonth, entry.GetType().GetField("Last").GetValue(entry));
            }
            return new JObject
            {
                ["monthKey"] = (int)ledgerType.GetProperty("MonthKey").GetValue(ledger),
                ["signs"] = count,
                ["lastMonthKnown"] = (bool)ledgerType.GetProperty("LastMonthKnown").GetValue(ledger),
                ["this"] = JObject.FromObject(thisMonth.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2))),
                ["last"] = JObject.FromObject(lastMonth.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2))),
            };
        }

        private static void LcAddNumbers(Dictionary<string, double> sums, object month)
        {
            foreach (FieldInfo field in month.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                object v = field.GetValue(month);
                double d;
                switch (v)
                {
                    case int i: d = i; break;
                    case long l: d = l; break;
                    case double x: d = x; break;
                    case float f: d = f; break;
                    default: continue;
                }
                sums.TryGetValue(field.Name, out double s);
                sums[field.Name] = s + d;
            }
        }

        /// <summary>Every live SkiRunBinding: totals, and per run "flags/paid" for the before / after comparison.</summary>
        private JObject LcBindings()
        {
            Type type = FindType("SkiResortMod.Systems.SkiRunBinding");
            FieldInfo flagsField = type.GetField("m_Flags");
            FieldInfo paidField = type.GetField("m_PaidAmount");
            FieldInfo gateField = type.GetField("m_Gate");
            EntityQuery query = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly(type) },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            try
            {
                using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
                var runs = new JObject();
                long paidSum = 0;
                int paid = 0, restored = 0, orphans = 0;
                MethodInfo get = s_GetComponent.MakeGenericMethod(type);
                foreach (Entity e in entities)
                {
                    object value = get.Invoke(EntityManager, new object[] { e });
                    byte flags = (byte)flagsField.GetValue(value);
                    int amount = (int)paidField.GetValue(value);
                    paidSum += amount;
                    if ((flags & 1) != 0)
                        paid++;
                    if ((flags & 2) != 0)
                        restored++;
                    if ((Entity)gateField.GetValue(value) == Entity.Null)
                        orphans++;
                    runs[EntityId(e)] = $"{flags}/{amount}";
                }
                return new JObject
                {
                    ["count"] = entities.Length,
                    ["paid"] = paid,
                    ["restored"] = restored,
                    ["orphans"] = orphans,
                    ["paidSum"] = paidSum,
                    ["runs"] = runs,
                };
            }
            finally
            {
                query.Dispose();
            }
        }

        /// <summary>Part (c): FAIL on any bind charge or lost-link restore in the mod log since begin, on a run whose paid amount rose, and on a
        /// run that had no binding before the month but has a paid, not restored one after it (a fresh charge).</summary>
        private JObject LcBillingVerdict(LoadCheckRun run)
        {
            var before = (JObject)run.report["billingBefore"];
            var after = (JObject)run.report["billingAfter"];
            var logs = (JObject)run.report["logs"];
            var b = (JObject)before["bindings"]["runs"];
            var a = (JObject)after["bindings"]["runs"];
            var rose = new JArray();
            var fresh = new JArray();
            foreach (JProperty p in a.Properties())
            {
                string[] now = ((string)p.Value).Split('/');
                int paidNow = int.Parse(now[1]);
                byte flagsNow = byte.Parse(now[0]);
                if (b[p.Name] is JValue old)
                {
                    int paidOld = int.Parse(((string)old).Split('/')[1]);
                    if (paidNow > paidOld)
                        rose.Add($"{p.Name}: {paidOld} -> {paidNow}");
                }
                else if (paidNow > 0 && (flagsNow & 2) == 0)
                {
                    fresh.Add($"{p.Name}: {p.Value}");
                }
            }
            int charges = (int)logs["bindCharges"];
            int restores = (int)logs["restores"];
            int restoredFlags = (int)before["bindings"]["restored"];
            bool pass = charges == 0 && restores == 0 && rose.Count == 0 && fresh.Count == 0;
            return new JObject
            {
                ["pass"] = pass,
                ["moneyDelta"] = (int)after["money"] - (int)before["money"],
                ["bindChargeLines"] = charges,
                ["bindChargeSum"] = logs["bindChargeSum"],
                ["restoreLines"] = restores,
                ["restoredFlagsAtLoad"] = restoredFlags,
                ["paidRose"] = rose,
                ["freshPaidBindings"] = fresh,
                ["note"] = pass ? "no binding fee re-charge, no lost binding" :
                    "binding fee re-charged or a lost SkiRunBinding block re-linked: the signature of a skipped SkiRunBinding block",
            };
        }
    }
}
