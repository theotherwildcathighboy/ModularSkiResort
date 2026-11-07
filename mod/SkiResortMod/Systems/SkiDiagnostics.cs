// Options > Diagnostics > Copy diagnostics puts one block of text for a bug report on the clipboard. The block holds:
// - versions (mod, build stamp, game)
// - the machine
// - whether a city is loaded
// - the options changed from their defaults
// - the code mods loaded
// - the log file's place
// - the log's error lines (the last few, user folder scrubbed)
// Read-only: it writes nothing but the clipboard and one log line.
// The code uses reflection on our own settings class only (the Default* constants), never on another mod.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Game;
using Game.SceneFlow;
using SkiResortMod.Settings;
using UnityEngine;

namespace SkiResortMod.Systems
{
    public static class SkiDiagnostics
    {
        /// <summary>Maximum number of error lines to copy from the mod log (the newest).</summary>
        public const int MaxErrorLines = 8;
        /// <summary>One copied log line is cut to this many characters.</summary>
        public const int MaxLineChars = 300;

        /// <summary>The mod log: the game user data folder + Logs. The folder is Unity persistentDataPath, LocalLowColossal OrderCities
        /// Skylines II. The game overrides it only through CSII_USERDATAPATH, which the code reads first.</summary>
        public static string LogFilePath
        {
            get
            {
                string root = Environment.GetEnvironmentVariable("CSII_USERDATAPATH");
                if (string.IsNullOrEmpty(root))
                    root = Application.persistentDataPath;
                return Path.Combine(root, "Logs", "SkiResortMod.Mod.log");
            }
        }

        /// <summary>Builds the text and puts it on the clipboard. Logs one line. Never throws.</summary>
        public static void CopyToClipboard(string why)
        {
            try
            {
                string text = Build();
                GUIUtility.systemCopyBuffer = text;
                Mod.Log.Info($"Diagnostics [copy]: {text.Length} characters copied to the clipboard ({why})");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Diagnostics [copy]: failed (non-fatal): {Mod.ScrubUserPath(e.Message)}");
            }
        }

        public static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Modular Ski Resort diagnostics");
            sb.Append("Mod: ").Append(ModVersion()).Append(", build ").AppendLine(BuildStamp.Value);
            sb.Append("Game: ").Append(Safe(() => Game.Version.current.fullVersion)).Append(", ").AppendLine(Application.platform.ToString());
            sb.Append("Machine: ").Append(SystemInfo.operatingSystem).Append("; ").Append(SystemInfo.processorType).Append(", ")
              .Append(SystemInfo.systemMemorySize).Append(" MB RAM; ").Append(SystemInfo.graphicsDeviceName).Append(", ")
              .Append(SystemInfo.graphicsMemorySize).AppendLine(" MB VRAM");
            sb.Append("City loaded: ").AppendLine(Safe(() => GameManager.instance != null && GameManager.instance.gameMode == GameMode.Game ? "yes" : "no"));
            sb.Append("Options changed from defaults: ").AppendLine(ChangedSettings());
            AppendMods(sb);
            sb.Append("Log file: ").AppendLine(Mod.ScrubUserPath(LogFilePath));
            AppendErrors(sb);
            return sb.ToString();
        }

        private static string ModVersion()
        {
            var v = typeof(SkiDiagnostics).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return v?.Version ?? typeof(SkiDiagnostics).Assembly.GetName().Version?.ToString() ?? "unknown";
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? "unknown"; }
            catch (Exception) { return "unknown"; }
        }

        /// <summary>Every option whose value differs from its Default* constant (only bool / number / enum / string options: key
        /// bindings and buttons have none). "none" when all are at their defaults.</summary>
        public static string ChangedSettings()
        {
            SkiResortSettings s = Mod.Settings;
            if (s == null)
                return "unknown (options not loaded)";
            var changed = new List<string>();
            foreach (PropertyInfo p in typeof(SkiResortSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0)
                    continue;
                FieldInfo d = typeof(SkiResortSettings).GetField("Default" + p.Name, BindingFlags.Public | BindingFlags.Static);
                if (d == null)
                    continue;
                object now, def;
                try
                {
                    now = p.GetValue(s);
                    def = d.GetValue(null);
                }
                catch (Exception)
                {
                    continue;
                }
                if (!Equals(now, def) && !(now != null && def != null && now.ToString() == def.ToString()))
                    changed.Add($"{p.Name} {def} -> {now}");
            }
            return changed.Count == 0 ? "none" : string.Join("; ", changed);
        }

        private static void AppendMods(StringBuilder sb)
        {
            var mods = new List<string>();
            try
            {
                foreach (var info in GameManager.instance.modManager)
                {
                    if (!info.isValid || !info.isLoaded)
                        continue;
                    AssemblyName n = info.asset.assembly.GetName();
                    mods.Add(n.Name + " " + n.Version);
                }
            }
            catch (Exception e)
            {
                mods.Add("(list failed: " + Mod.ScrubUserPath(e.Message) + ")");
            }
            mods.Sort(StringComparer.OrdinalIgnoreCase);
            sb.Append("Code mods loaded (").Append(mods.Count).Append("): ").AppendLine(string.Join(", ", mods));
        }

        /// <summary>The log's error lines ("!!!" or [ERROR]). The result holds the newest MaxErrorLines, cuts each to MaxLineChars and
        /// scrubs the user folder. The log is open for writing, so the code reads it with FileShare.ReadWrite.</summary>
        private static void AppendErrors(StringBuilder sb)
        {
            var errors = new List<string>();
            int total = 0;
            try
            {
                using (var fs = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line.IndexOf("!!!", StringComparison.Ordinal) < 0 && line.IndexOf("[ERROR]", StringComparison.Ordinal) < 0)
                            continue;
                        total++;
                        errors.Add(line.Length > MaxLineChars ? line.Substring(0, MaxLineChars) + " ..." : line);
                        if (errors.Count > MaxErrorLines)
                            errors.RemoveAt(0);
                    }
                }
            }
            catch (Exception e)
            {
                sb.Append("Log errors: could not read the log (").Append(Mod.ScrubUserPath(e.Message)).AppendLine(")");
                return;
            }
            sb.Append("Log errors this session: ").Append(total);
            if (total > errors.Count)
                sb.Append(" (the newest ").Append(errors.Count).Append(" below)");
            sb.AppendLine();
            foreach (string e in errors)
                sb.Append("  ").AppendLine(Mod.ScrubUserPath(e));
        }
    }
}
